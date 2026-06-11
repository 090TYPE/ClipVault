using System.Diagnostics;
using System.Runtime.Versioning;
using ClipVault.Core.Abstractions;
using ClipVault.Core.Hashing;
using ClipVault.Core.Models;

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
            var text = RunCapture(_wayland ? "wl-paste" : "xclip",
                                  _wayland ? "-n" : "-selection clipboard -o");
            if (string.IsNullOrEmpty(text)) return;
            var hash = ClipHasher.HashText(text);
            if (hash == _lastHash) return;
            _lastHash = hash;
            ClipboardChanged?.Invoke(new ClipboardReadResult(ClipType.Text, text, null, null, null));
        }
        catch { /* clipboard tool missing or empty — skip */ }
    }

    private static string RunCapture(string exe, string args)
    {
        var psi = new ProcessStartInfo(exe, args)
        { RedirectStandardOutput = true, UseShellExecute = false };
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit(1000);
        return output;
    }

    public void Write(ClipItem item)
    {
        if (item.Type != ClipType.Text || item.TextContent is null) return;
        var exe = _wayland ? "wl-copy" : "xclip";
        var args = _wayland ? "" : "-selection clipboard";
        var psi = new ProcessStartInfo(exe, args)
        { RedirectStandardInput = true, UseShellExecute = false };
        using var p = Process.Start(psi)!;
        p.StandardInput.Write(item.TextContent);
        p.StandardInput.Close();
        p.WaitForExit(1000);
    }

    public void Dispose() => Stop();
}
