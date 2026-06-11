using ClipVault.Core.Abstractions;
using SharpHook;
using SharpHook.Native;

namespace ClipVault.Platform.Hotkeys;

/// Parses a hotkey like "Ctrl+Shift+V" and fires the callback on match.
public class SharpHookHotkeyService : IGlobalHotkeyService
{
    private readonly TaskPoolGlobalHook _hook = new();
    private Action? _callback;
    private (bool ctrl, bool shift, bool alt, KeyCode key)? _combo;

    public bool Register(string hotkey, Action callback)
    {
        var parsed = Parse(hotkey);
        if (parsed is null) return false;
        _combo = parsed;
        _callback = callback;
        _hook.KeyPressed += OnKeyPressed;
        return true;
    }

    public void Start() => _ = _hook.RunAsync();

    private void OnKeyPressed(object? sender, KeyboardHookEventArgs e)
    {
        if (_combo is null) return;
        var c = _combo.Value;
        var mask = e.RawEvent.Mask;
        bool ctrl = mask.HasFlag(ModifierMask.LeftCtrl) || mask.HasFlag(ModifierMask.RightCtrl);
        bool shift = mask.HasFlag(ModifierMask.LeftShift) || mask.HasFlag(ModifierMask.RightShift);
        bool alt = mask.HasFlag(ModifierMask.LeftAlt) || mask.HasFlag(ModifierMask.RightAlt);
        if (e.Data.KeyCode == c.key && ctrl == c.ctrl && shift == c.shift && alt == c.alt)
            _callback?.Invoke();
    }

    private static (bool, bool, bool, KeyCode)? Parse(string hotkey)
    {
        bool ctrl = false, shift = false, alt = false;
        KeyCode? key = null;
        foreach (var part in hotkey.Split('+', StringSplitOptions.TrimEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl": ctrl = true; break;
                case "shift": shift = true; break;
                case "alt": alt = true; break;
                default:
                    if (Enum.TryParse<KeyCode>("Vc" + part.ToUpperInvariant(), out var k)) key = k;
                    break;
            }
        }
        return key is null ? null : (ctrl, shift, alt, key.Value);
    }

    public void Dispose() => _hook.Dispose();
}
