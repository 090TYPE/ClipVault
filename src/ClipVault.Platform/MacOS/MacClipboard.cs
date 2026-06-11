using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using ClipVault.Core.Abstractions;
using ClipVault.Core.Hashing;
using ClipVault.Core.Models;
using ClipVault.Platform.Shared;

namespace ClipVault.Platform.MacOS;

[SupportedOSPlatform("macos")]
public class MacClipboard : IClipboardMonitor, IClipboardWriter
{
    public event Action<ClipboardReadResult>? ClipboardChanged;

    private readonly System.Timers.Timer _timer = new(400);
    private string _lastHash = "";

    public void Start()
    {
        _timer.Elapsed += (_, _) => Poll();
        _timer.AutoReset = true;
        _timer.Start();
    }

    public void Stop() => _timer.Stop();

    private void Poll()
    {
        try
        {
            var info = RunText("osascript", "-e \"clipboard info\"");
            var tokens = info.Split(new[] { ',', '{', '}', ':' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var kind = ClipTypeSelector.PickBest(tokens);
            if (kind is null) return;

            ClipboardReadResult? result = kind switch
            {
                ClipType.Files => ReadFiles(),
                ClipType.Image => ReadImage(),
                ClipType.Text => ReadText(),
                _ => null
            };
            if (result is null) return;

            var hash = result.Type switch
            {
                ClipType.Image => ClipHasher.HashBytes(result.ImagePng ?? Array.Empty<byte>()),
                ClipType.Files => ClipHasher.HashText(string.Join("\n", result.FilePaths ?? new List<string>())),
                _ => ClipHasher.HashText(result.Text ?? "")
            };
            if (hash == _lastHash) return;
            _lastHash = hash;
            ClipboardChanged?.Invoke(result);
        }
        catch { /* unreadable — skip */ }
    }

    private ClipboardReadResult ReadText() =>
        new(ClipType.Text, RunText("pbpaste", ""), null, null, null);

    private ClipboardReadResult? ReadImage()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"cv-clip-{Guid.NewGuid():N}.png");
        // Dump the clipboard PNG to a temp file via AppleScript, then read it.
        var script =
            $"-e \"set thePng to (the clipboard as «class PNGf»)\" " +
            $"-e \"set f to open for access POSIX file \\\"{tmp}\\\" with write permission\" " +
            $"-e \"write thePng to f\" -e \"close access f\"";
        RunText("osascript", script);
        if (!File.Exists(tmp)) return null;
        try
        {
            var bytes = File.ReadAllBytes(tmp);
            return new ClipboardReadResult(ClipType.Image, null, bytes, null, null);
        }
        finally { try { File.Delete(tmp); } catch { } }
    }

    private ClipboardReadResult ReadFiles()
    {
        // Returns newline-separated POSIX paths for all file URLs on the pasteboard.
        var script =
            "-e \"set out to \\\"\\\"\" " +
            "-e \"repeat with f in (the clipboard as «class furl»)\" " +
            "-e \"set out to out & POSIX path of f & linefeed\" " +
            "-e \"end repeat\" -e \"return out\"";
        var raw = RunText("osascript", script);
        var paths = raw.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (paths.Length == 0)
        {
            // Single-file clipboards aren't a list; fall back to a single path.
            var single = RunText("osascript",
                "-e \"POSIX path of (the clipboard as «class furl»)\"").Trim();
            if (!string.IsNullOrEmpty(single)) paths = new[] { single };
        }
        return new ClipboardReadResult(ClipType.Files, null, null, paths, null);
    }

    public void Write(ClipItem item)
    {
        try
        {
            switch (item.Type)
            {
                case ClipType.Text when item.TextContent is not null:
                    PipeIn("pbcopy", "", Encoding.UTF8.GetBytes(item.TextContent));
                    break;
                case ClipType.Image when item.BlobPath is not null && File.Exists(item.BlobPath):
                    RunText("osascript",
                        $"-e \"set the clipboard to (read (POSIX file \\\"{item.BlobPath}\\\") as «class PNGf»)\"");
                    break;
                case ClipType.Files when item.FilePaths is not null:
                    var paths = JsonSerializer.Deserialize<string[]>(item.FilePaths) ?? Array.Empty<string>();
                    PipeIn("pbcopy", "", Encoding.UTF8.GetBytes(string.Join(Environment.NewLine, paths)));
                    break;
            }
        }
        catch { /* swallow */ }
    }

    private static string RunText(string exe, string args)
    {
        var psi = new ProcessStartInfo(exe, args)
        { RedirectStandardOutput = true, UseShellExecute = false };
        using var p = Process.Start(psi)!;
        var o = p.StandardOutput.ReadToEnd();
        p.WaitForExit(2000);
        return o;
    }

    private static void PipeIn(string exe, string args, byte[] data)
    {
        var psi = new ProcessStartInfo(exe, args)
        { RedirectStandardInput = true, UseShellExecute = false };
        using var p = Process.Start(psi)!;
        p.StandardInput.BaseStream.Write(data, 0, data.Length);
        p.StandardInput.Close();
        p.WaitForExit(2000);
    }

    public void Dispose() => Stop();
}
