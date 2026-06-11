using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ClipVault.Core.Abstractions;
using ClipVault.Core.Models;
using ClipVault.Sync.Crypto;
using ClipVault.Sync.Discovery;
using ClipVault.Sync.Messaging;
using ClipVault.Sync.Pairing;
using ClipVault.Sync.Transport;

namespace ClipVault.Sync;

/// Owns the TCP listener and the discovery beacon. Frames are length-prefixed
/// (MessageFraming) with a one-byte FrameTag routing data vs pairing.
public class SyncService : ISyncService
{
    private readonly DeviceIdentity _identity;
    private readonly TrustStore _trust;
    private readonly string _blobDir;
    private readonly TcpListener _listener;
    private UdpDiscovery? _discovery;
    private CancellationTokenSource? _cts;

    // Active only while in pairing mode (responder side).
    private string? _pairingPin;
    private PairingHandshake? _pairingHandshake;

    public event Action<ClipItem>? RemoteClipReceived;

    public SyncService(DeviceIdentity identity, TrustStore trust, string blobDir)
    {
        _identity = identity;
        _trust = trust;
        _blobDir = blobDir;
        Directory.CreateDirectory(_blobDir);
        _listener = new TcpListener(IPAddress.Any, 0);
        _listener.Start();
    }

    private int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _ = AcceptLoop(_cts.Token);
        _discovery = new UdpDiscovery(_identity.ToInfo(Port));
        _discovery.Start();
    }

    public IReadOnlyList<DiscoveredPeer> DiscoveredPeers =>
        _discovery?.Peers ?? Array.Empty<DiscoveredPeer>();

    public IReadOnlyList<PairedDevice> TrustedDevices => _trust.All;

    // ---- inbound ----

    private async Task AcceptLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(ct);
                _ = HandleConnection(client, ct);
            }
            catch (OperationCanceledException) { break; }
            catch { /* transient listener error */ }
        }
    }

    private async Task HandleConnection(TcpClient client, CancellationToken ct)
    {
        using (client)
        await using (var stream = client.GetStream())
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var frame = await MessageFraming.ReadFrameAsync(stream, ct);
                    if (frame is null || frame.Length == 0) break;
                    var tag = frame[0];
                    var body = frame[1..];

                    if (tag == FrameTag.Data)
                        HandleData(body);
                    else if (tag == FrameTag.PairReq)
                    {
                        await HandlePairRequest(body, stream, ct);
                        break; // pairing connection is single-shot
                    }
                    else break;
                }
            }
            catch { /* drop connection on any error */ }
        }
    }

    private void HandleData(byte[] body)
    {
        foreach (var dev in _trust.All)
        {
            try
            {
                var plain = new SecureChannel(Convert.FromBase64String(dev.KeyBase64)).Decrypt(body);
                var msg = SyncMessage.FromBytes(plain);
                if (msg.Type == "clip" && msg.Item is not null)
                {
                    var item = msg.Item;
                    if (item.Type == ClipType.Image && msg.ImageBytes is not null)
                    {
                        try
                        {
                            var local = Path.Combine(_blobDir, $"{Guid.NewGuid():N}.png");
                            File.WriteAllBytes(local, msg.ImageBytes);
                            item.BlobPath = local;
                        }
                        catch { /* deliver without local blob */ }
                    }
                    RemoteClipReceived?.Invoke(item);
                }
                return; // decrypted with this peer's key
            }
            catch { /* not this key — try next */ }
        }
        // No trusted key decrypted it — ignore.
    }

    private async Task HandlePairRequest(byte[] body, Stream stream, CancellationToken ct)
    {
        if (_pairingPin is null || _pairingHandshake is null) return; // not pairing
        var req = JsonSerializer.Deserialize<PairReq>(Encoding.UTF8.GetString(body));
        if (req is null) return;

        var key = _pairingHandshake.DeriveKey(Convert.FromBase64String(req.PubKeyB64), _pairingPin);
        var resp = new PairResp(_identity.DeviceId, _identity.Name,
            Convert.ToBase64String(_pairingHandshake.PublicKey),
            PairingProtocol.Confirmation(key));
        await WriteFrame(stream, FrameTag.PairResp,
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(resp)), ct);

        var ackFrame = await MessageFraming.ReadFrameAsync(stream, ct);
        if (ackFrame is null || ackFrame.Length == 0 || ackFrame[0] != FrameTag.PairAck) return;
        if (Encoding.UTF8.GetString(ackFrame[1..]) == "ok")
        {
            _trust.Add(new PairedDevice(req.DeviceId, req.Name, Convert.ToBase64String(key)));
            _pairingPin = null;
            _pairingHandshake?.Dispose();
            _pairingHandshake = null;
        }
    }

    // ---- outbound ----

    public async Task BroadcastAsync(ClipItem item)
    {
        byte[]? imageBytes = null;
        if (item.Type == ClipType.Image && item.BlobPath is not null && File.Exists(item.BlobPath))
        {
            try { imageBytes = File.ReadAllBytes(item.BlobPath); } catch { /* send without image */ }
        }
        var msgBytes = SyncMessage.ForClip(item, imageBytes).ToBytes();
        foreach (var dev in _trust.All)
        {
            var peer = DiscoveredPeers.FirstOrDefault(p => p.DeviceId == dev.DeviceId);
            if (peer is null) continue; // offline
            try
            {
                var sealed_ = new SecureChannel(Convert.FromBase64String(dev.KeyBase64)).Encrypt(msgBytes);
                using var client = new TcpClient();
                await client.ConnectAsync(peer.IpAddress, peer.TcpPort);
                await using var stream = client.GetStream();
                await WriteFrame(stream, FrameTag.Data, sealed_);
            }
            catch { /* peer unreachable — skip */ }
        }
    }

    public string EnterPairingMode()
    {
        _pairingHandshake?.Dispose();
        _pairingHandshake = new PairingHandshake();
        _pairingPin = Random.Shared.Next(0, 1_000_000).ToString("D6");
        return _pairingPin;
    }

    public async Task<bool> PairWithAsync(DiscoveredPeer peer, string pin)
    {
        using var hs = new PairingHandshake();
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(peer.IpAddress, peer.TcpPort);
            await using var stream = client.GetStream();

            var req = new PairReq(_identity.DeviceId, _identity.Name,
                Convert.ToBase64String(hs.PublicKey));
            await WriteFrame(stream, FrameTag.PairReq,
                Encoding.UTF8.GetBytes(JsonSerializer.Serialize(req)));

            var respFrame = await MessageFraming.ReadFrameAsync(stream);
            if (respFrame is null || respFrame.Length == 0 || respFrame[0] != FrameTag.PairResp)
                return false;
            var resp = JsonSerializer.Deserialize<PairResp>(Encoding.UTF8.GetString(respFrame[1..]));
            if (resp is null) return false;

            var key = hs.DeriveKey(Convert.FromBase64String(resp.PubKeyB64), pin);
            bool ok = PairingProtocol.Confirmation(key) == resp.Confirmation;
            await WriteFrame(stream, FrameTag.PairAck, Encoding.UTF8.GetBytes(ok ? "ok" : "fail"));
            if (!ok) return false;

            _trust.Add(new PairedDevice(resp.DeviceId, resp.Name, Convert.ToBase64String(key)));
            return true;
        }
        catch { return false; }
    }

    private static async Task WriteFrame(Stream s, byte tag, byte[] body, CancellationToken ct = default)
    {
        var framed = new byte[body.Length + 1];
        framed[0] = tag;
        Buffer.BlockCopy(body, 0, framed, 1, body.Length);
        await MessageFraming.WriteFrameAsync(s, framed, ct);
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _discovery?.Dispose();
        try { _listener.Stop(); } catch { }
        _pairingHandshake?.Dispose();
    }
}
