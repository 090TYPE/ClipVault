using ClipVault.Core.Models;

namespace ClipVault.Core.Abstractions;

public interface IClipboardWriter
{
    void Write(ClipItem item);
}
