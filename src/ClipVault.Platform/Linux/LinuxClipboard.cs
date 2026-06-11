using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using ClipVault.Core.Abstractions;
using ClipVault.Core.Hashing;
using ClipVault.Core.Models;
using ClipVault.Platform.Shared;

namespace ClipVault.Platform.Linux;

[SupportedOSPlatform("linux")]
public class LinuxClipboard : IClipboardMonitor, IClipboardWriter
{
    public event Action<ClipboardReadResult>? ClipboardChanged;

    private readonly System.Timers.Timer _timer = new(500);
    private string _lastHash = "";
    private readonly bool _wayland =
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));

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
            var types = ListTypes();
            var kind = ClipTypeSelector.PickBest(types);
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
        catch { /* tool missing / empty / unreadable — skip */ }
    }

    private IReadOnlyList<string> ListTypes()
    {
        var text = _wayland
            ? RunText("wl-paste", "--list-types")
            : RunText("xclip", "-selection clipboard -t TARGETS -o");
        return text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private ClipboardReadResult ReadText()
    {
        var text = _wayland
            ? RunText("wl-paste", "-n")
            : RunText("xclip", "-selection clipboard -o");
        return new ClipboardReadResult(ClipType.Text, text, null, null, null);
    }

    private ClipboardReadResult ReadImage()
    {
        var bytes = _wayland
            ? RunBytes("wl-paste", "--type image/png")
            : RunBytes("xclip", "-selection clipboard -t image/png -o");
        return new ClipboardReadResult(ClipType.Image, null, bytes, null, null);
    }

    private ClipboardReadResult ReadFiles()
    {
        var uriList = _wayland
            ? RunText("wl-paste", "--type text/uri-list")
            : RunText("xclip", "-selection clipboard -t text/uri-list -o");
        var paths = UriListParser.Parse(uriList);
        return new ClipboardReadResult(ClipType.Files, null, null, paths, null);
    }

    public void Write(ClipItem item)
    {
        try
        {
            switch (item.Type)
            {
                case ClipType.Text when item.TextContent is not null:
                    WriteText(item.TextContent);
                    break;
                case ClipType.Image when item.BlobPath is not null && File.Exists(item.BlobPath):
                    WriteImage(item.BlobPath);
                    break;
                case ClipType.Files when item.FilePaths is not null:
                    var paths = JsonSerializer.Deserialize<string[]>(item.FilePaths) ?? Array.Empty<string>();
                    WriteText(string.Join(Environment.NewLine, paths)); // parity with Windows
                    break;
            }
        }
        catch { /* swallow; caller surfaces a tray notice */ }
    }

    private void WriteText(string text)
    {
        var (exe, args) = _wayland ? ("wl-copy", "") : ("xclip", "-selection clipboard");
        PipeIn(exe, args, Encoding.UTF8.GetBytes(text));
    }

    private void WriteImage(string pngPath)
    {
        var bytes = File.ReadAllBytes(pngPath);
        var (exe, args) = _wayland
            ? ("wl-copy", "--type image/png")
            : ("xclip", "-selection clipboard -t image/png -i");
        PipeIn(exe, args, bytes);
    }

    private static string RunText(string exe, string args)
    {
        var psi = new ProcessStartInfo(exe, args)
        { RedirectStandardOutput = true, UseShellExecute = false };
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit(2000);
        return output;
    }

    private static byte[] RunBytes(string exe, string args)
    {
        var psi = new ProcessStartInfo(exe, args)
        { RedirectStandardOutput = true, UseShellExecute = false };
        using var p = Process.Start(psi)!;
        using var ms = new MemoryStream();
        p.StandardOutput.BaseStream.CopyTo(ms);
        p.WaitForExit(2000);
        return ms.ToArray();
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
