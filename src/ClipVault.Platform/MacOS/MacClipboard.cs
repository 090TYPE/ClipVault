using System.Diagnostics;
using System.Runtime.Versioning;
using ClipVault.Core.Abstractions;
using ClipVault.Core.Hashing;
using ClipVault.Core.Models;

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
            // MVP: use `pbpaste` for reads, diff by hash. A later iteration can
            // P/Invoke NSPasteboard.general.changeCount for image/file support.
            var text = Run("pbpaste", "");
            if (string.IsNullOrEmpty(text)) return;
            var hash = ClipHasher.HashText(text);
            if (hash == _lastHash) return;
            _lastHash = hash;
            ClipboardChanged?.Invoke(new ClipboardReadResult(ClipType.Text, text, null, null, null));
        }
        catch { /* skip */ }
    }

    private static string Run(string exe, string args)
    {
        var psi = new ProcessStartInfo(exe, args)
        { RedirectStandardOutput = true, UseShellExecute = false };
        using var p = Process.Start(psi)!;
        var o = p.StandardOutput.ReadToEnd();
        p.WaitForExit(1000);
        return o;
    }

    public void Write(ClipItem item)
    {
        if (item.Type != ClipType.Text || item.TextContent is null) return;
        var psi = new ProcessStartInfo("pbcopy", "")
        { RedirectStandardInput = true, UseShellExecute = false };
        using var p = Process.Start(psi)!;
        p.StandardInput.Write(item.TextContent);
        p.StandardInput.Close();
        p.WaitForExit(1000);
    }

    public void Dispose() => Stop();
}
