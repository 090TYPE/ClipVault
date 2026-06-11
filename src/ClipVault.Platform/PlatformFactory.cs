using System.Runtime.InteropServices;
using ClipVault.Core.Abstractions;
using ClipVault.Platform.Hotkeys;
using ClipVault.Platform.Linux;
using ClipVault.Platform.MacOS;
using ClipVault.Platform.Windows;

namespace ClipVault.Platform;

public static class PlatformFactory
{
    public static (IClipboardMonitor Monitor, IClipboardWriter Writer) CreateClipboard()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var w = new WindowsClipboard();
            return (w, w);
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var l = new LinuxClipboard();
            return (l, l);
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var m = new MacClipboard();
            return (m, m);
        }
        throw new PlatformNotSupportedException();
    }

    public static IGlobalHotkeyService CreateHotkeys() => new SharpHookHotkeyService();
}
