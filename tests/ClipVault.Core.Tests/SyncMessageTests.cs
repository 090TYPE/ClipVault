using ClipVault.Core.Models;
using ClipVault.Sync.Messaging;
using Xunit;

namespace ClipVault.Core.Tests;

public class SyncMessageTests
{
    [Fact]
    public void Clip_SerializeDeserialize_RoundTrips()
    {
        var item = new ClipItem { Type = ClipType.Text, TextContent = "x", Preview = "x", Hash = "h" };
        var msg = SyncMessage.ForClip(item);
        var bytes = msg.ToBytes();
        var back = SyncMessage.FromBytes(bytes);
        Assert.Equal("clip", back.Type);
        Assert.NotNull(back.Item);
        Assert.Equal("x", back.Item!.TextContent);
        Assert.Equal("h", back.Item.Hash);
    }

    [Fact]
    public void Clip_WithImageBytes_RoundTrips()
    {
        var item = new ClipItem { Type = ClipType.Image, Preview = "[image]", Hash = "h" };
        var bytes = new byte[] { 1, 2, 3, 4, 5 };
        var back = SyncMessage.FromBytes(SyncMessage.ForClip(item, bytes).ToBytes());
        Assert.Equal("clip", back.Type);
        Assert.NotNull(back.ImageBytes);
        Assert.Equal(bytes, back.ImageBytes);
    }
}
