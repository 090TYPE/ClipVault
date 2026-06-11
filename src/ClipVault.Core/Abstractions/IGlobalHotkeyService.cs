namespace ClipVault.Core.Abstractions;

public interface IGlobalHotkeyService : IDisposable
{
    /// Returns true if registration succeeded; false if the combo is taken.
    bool Register(string hotkey, Action callback);
    void Start();
}
