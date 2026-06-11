using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using ClipVault.Core.Abstractions;
using ClipVault.Core.Models;

namespace ClipVault.Platform.Windows;

[SupportedOSPlatform("windows")]
public class WindowsClipboard : IClipboardMonitor, IClipboardWriter
{
    public event Action<ClipboardReadResult>? ClipboardChanged;

    private const int WM_CLIPBOARDUPDATE = 0x031D;
    private const int WM_DESTROY = 0x0002;
    private static readonly IntPtr HWND_MESSAGE = new(-3);

    private const uint CF_UNICODETEXT = 13;
    private const uint CF_HDROP = 15;
    private const uint CF_BITMAP = 2;
    private const uint GMEM_MOVEABLE = 0x0002;

    private IntPtr _hwnd;
    private Thread? _thread;
    private WndProcDelegate? _wndProc; // kept alive to prevent GC of the callback

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct WNDCLASS
    {
        public uint style;
        public WndProcDelegate lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam;
        public uint time; public int ptX; public int ptY;
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern ushort RegisterClassW(ref WNDCLASS c);
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowExW(uint ex, string cls, string? name, uint style,
        int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
    [DllImport("user32.dll")] private static extern IntPtr DefWindowProcW(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern int GetMessageW(out MSG m, IntPtr h, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG m);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessageW(ref MSG m);
    [DllImport("user32.dll")] private static extern void PostQuitMessage(int code);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool AddClipboardFormatListener(IntPtr h);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RemoveClipboardFormatListener(IntPtr h);
    [DllImport("kernel32.dll")] private static extern IntPtr GetModuleHandleW(string? name);

    [DllImport("user32.dll", SetLastError = true)] private static extern bool OpenClipboard(IntPtr h);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool CloseClipboard();
    [DllImport("user32.dll", SetLastError = true)] private static extern bool EmptyClipboard();
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr GetClipboardData(uint fmt);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetClipboardData(uint fmt, IntPtr h);
    [DllImport("user32.dll")] private static extern bool IsClipboardFormatAvailable(uint fmt);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr h);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr h);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint DragQueryFileW(IntPtr hDrop, uint i, StringBuilder? file, uint cch);

    public void Start()
    {
        _thread = new Thread(ThreadProc) { IsBackground = true, Name = "ClipVault-ClipboardListener" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    private void ThreadProc()
    {
        _wndProc = WndProc;
        var cls = new WNDCLASS
        {
            lpfnWndProc = _wndProc,
            hInstance = GetModuleHandleW(null),
            lpszClassName = "ClipVaultMsgWindow"
        };
        RegisterClassW(ref cls);
        _hwnd = CreateWindowExW(0, cls.lpszClassName, null, 0, 0, 0, 0, 0,
            HWND_MESSAGE, IntPtr.Zero, cls.hInstance, IntPtr.Zero);
        AddClipboardFormatListener(_hwnd);

        while (GetMessageW(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessageW(ref msg);
        }
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_CLIPBOARDUPDATE)
        {
            try
            {
                var r = ReadCurrent();
                if (r is not null) ClipboardChanged?.Invoke(r);
            }
            catch { /* locked/unsupported clipboard — skip, never crash */ }
            return IntPtr.Zero;
        }
        if (msg == WM_DESTROY) { PostQuitMessage(0); return IntPtr.Zero; }
        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    private ClipboardReadResult? ReadCurrent()
    {
        if (!OpenClipboard(IntPtr.Zero)) return null;
        try
        {
            if (IsClipboardFormatAvailable(CF_HDROP))
            {
                var hDrop = GetClipboardData(CF_HDROP);
                if (hDrop != IntPtr.Zero)
                {
                    uint count = DragQueryFileW(hDrop, 0xFFFFFFFF, null, 0);
                    var paths = new List<string>();
                    for (uint i = 0; i < count; i++)
                    {
                        uint len = DragQueryFileW(hDrop, i, null, 0);
                        var sb = new StringBuilder((int)len + 1);
                        DragQueryFileW(hDrop, i, sb, len + 1);
                        paths.Add(sb.ToString());
                    }
                    if (paths.Count > 0)
                        return new ClipboardReadResult(ClipType.Files, null, null, paths, null);
                }
            }

            if (IsClipboardFormatAvailable(CF_BITMAP))
            {
                var hbm = GetClipboardData(CF_BITMAP);
                if (hbm != IntPtr.Zero)
                {
                    using var bmp = Image.FromHbitmap(hbm);
                    using var ms = new MemoryStream();
                    bmp.Save(ms, ImageFormat.Png);
                    return new ClipboardReadResult(ClipType.Image, null, ms.ToArray(), null, null);
                }
            }

            if (IsClipboardFormatAvailable(CF_UNICODETEXT))
            {
                var h = GetClipboardData(CF_UNICODETEXT);
                if (h != IntPtr.Zero)
                {
                    var ptr = GlobalLock(h);
                    try
                    {
                        var text = Marshal.PtrToStringUni(ptr);
                        if (!string.IsNullOrEmpty(text))
                            return new ClipboardReadResult(ClipType.Text, text, null, null, null);
                    }
                    finally { GlobalUnlock(h); }
                }
            }
            return null;
        }
        finally { CloseClipboard(); }
    }

    public void Write(ClipItem item)
    {
        try
        {
            switch (item.Type)
            {
                case ClipType.Text when item.TextContent is not null:
                    SetText(item.TextContent);
                    break;
                case ClipType.Image when item.BlobPath is not null && File.Exists(item.BlobPath):
                    SetImage(item.BlobPath);
                    break;
                case ClipType.Files when item.FilePaths is not null:
                    var paths = JsonSerializer.Deserialize<string[]>(item.FilePaths) ?? Array.Empty<string>();
                    SetText(string.Join(Environment.NewLine, paths)); // MVP: paste file paths as text
                    break;
            }
        }
        catch { /* swallow; caller surfaces a tray notice */ }
    }

    private static void SetText(string text)
    {
        if (!OpenClipboard(IntPtr.Zero)) return;
        try
        {
            EmptyClipboard();
            var data = Encoding.Unicode.GetBytes(text + "\0");
            var hMem = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)data.Length);
            var ptr = GlobalLock(hMem);
            try { Marshal.Copy(data, 0, ptr, data.Length); }
            finally { GlobalUnlock(hMem); }
            SetClipboardData(CF_UNICODETEXT, hMem);
        }
        finally { CloseClipboard(); }
    }

    private static void SetImage(string pngPath)
    {
        using var bmp = new Bitmap(pngPath);
        var hbm = bmp.GetHbitmap();
        if (!OpenClipboard(IntPtr.Zero)) return;
        try
        {
            EmptyClipboard();
            SetClipboardData(CF_BITMAP, hbm); // clipboard takes ownership on success
        }
        finally { CloseClipboard(); }
    }

    public void Stop()
    {
        if (_hwnd != IntPtr.Zero)
        {
            RemoveClipboardFormatListener(_hwnd);
            DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }
    }

    public void Dispose() => Stop();
}
