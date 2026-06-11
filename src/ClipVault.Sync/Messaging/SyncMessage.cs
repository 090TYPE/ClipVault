using System.Text;
using System.Text.Json;
using ClipVault.Core.Models;

namespace ClipVault.Sync.Messaging;

public class SyncMessage
{
    public string Type { get; set; } = "";
    public ClipItem? Item { get; set; }
    public byte[]? ImageBytes { get; set; } // PNG bytes for Image clips (base64 in JSON)

    public static SyncMessage ForClip(ClipItem item) => ForClip(item, null);

    public static SyncMessage ForClip(ClipItem item, byte[]? imageBytes) =>
        new() { Type = "clip", Item = item, ImageBytes = imageBytes };

    public byte[] ToBytes() => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(this));

    public static SyncMessage FromBytes(byte[] bytes) =>
        JsonSerializer.Deserialize<SyncMessage>(Encoding.UTF8.GetString(bytes))
        ?? throw new FormatException("Invalid SyncMessage");
}
