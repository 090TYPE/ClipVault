namespace ClipVault.Core.Abstractions;

public interface IClipboardMonitor : IDisposable
{
    event Action<ClipboardReadResult>? ClipboardChanged;
    void Start();
    void Stop();
}
