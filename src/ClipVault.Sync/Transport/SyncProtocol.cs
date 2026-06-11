namespace ClipVault.Sync.Transport;

/// One-byte tag prefixed to every frame payload to route it.
public static class FrameTag
{
    public const byte Data = 0x01;     // body = AES-GCM sealed SyncMessage
    public const byte PairReq = 0x02;  // body = JSON PairReq (plaintext)
    public const byte PairResp = 0x03; // body = JSON PairResp (plaintext)
    public const byte PairAck = 0x04;  // body = "ok" | "fail" (plaintext)
}

public record PairReq(string DeviceId, string Name, string PubKeyB64);

public record PairResp(string DeviceId, string Name, string PubKeyB64, string Confirmation);
