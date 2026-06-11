using ClipVault.Sync.Messaging;
using Xunit;

namespace ClipVault.Core.Tests;

public class MessageFramingTests
{
    [Fact]
    public async Task WriteFrame_ThenReadFrame_RoundTrips()
    {
        using var ms = new MemoryStream();
        var payload = new byte[] { 10, 20, 30, 40 };
        await MessageFraming.WriteFrameAsync(ms, payload);
        ms.Position = 0;
        var read = await MessageFraming.ReadFrameAsync(ms);
        Assert.Equal(payload, read);
    }

    [Fact]
    public async Task ReadFrame_OnClosedStream_ReturnsNull()
    {
        using var ms = new MemoryStream();
        var read = await MessageFraming.ReadFrameAsync(ms); // empty/EOF
        Assert.Null(read);
    }
}
