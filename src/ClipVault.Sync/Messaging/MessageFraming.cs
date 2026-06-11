using System.Buffers.Binary;

namespace ClipVault.Sync.Messaging;

/// Frames are a 4-byte big-endian length prefix followed by that many bytes.
public static class MessageFraming
{
    private const int MaxFrame = 32 * 1024 * 1024; // 32 MB guard

    public static async Task WriteFrameAsync(Stream s, byte[] payload, CancellationToken ct = default)
    {
        var len = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(len, payload.Length);
        await s.WriteAsync(len, ct);
        await s.WriteAsync(payload, ct);
        await s.FlushAsync(ct);
    }

    /// Returns the payload, or null on clean EOF (peer closed).
    public static async Task<byte[]?> ReadFrameAsync(Stream s, CancellationToken ct = default)
    {
        var lenBuf = await ReadExactAsync(s, 4, ct);
        if (lenBuf is null) return null;
        int len = BinaryPrimitives.ReadInt32BigEndian(lenBuf);
        if (len < 0 || len > MaxFrame) throw new InvalidDataException($"Bad frame length {len}");
        var payload = await ReadExactAsync(s, len, ct);
        if (payload is null) throw new EndOfStreamException("Truncated frame");
        return payload;
    }

    private static async Task<byte[]?> ReadExactAsync(Stream s, int count, CancellationToken ct)
    {
        if (count == 0) return Array.Empty<byte>();
        var buf = new byte[count];
        int read = 0;
        while (read < count)
        {
            int n = await s.ReadAsync(buf.AsMemory(read, count - read), ct);
            if (n == 0) return read == 0 ? null : throw new EndOfStreamException();
            read += n;
        }
        return buf;
    }
}
