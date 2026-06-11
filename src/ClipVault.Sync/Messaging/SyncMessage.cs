using System.Text;
using System.Text.Json;
using ClipVault.Core.Models;

namespace ClipVault.Sync.Messaging;

public class SyncMessage
{
    public string Type { get; set; } = "";
    public ClipItem? Item { get; set; }

    public static SyncMessage ForClip(ClipItem item) => new() { Type = "clip", Item = item };

    public byte[] ToBytes() => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(this));

    public static SyncMessage FromBytes(byte[] bytes) =>
        JsonSerializer.Deserialize<SyncMessage>(Encoding.UTF8.GetString(bytes))
        ?? throw new FormatException("Invalid SyncMessage");
}
