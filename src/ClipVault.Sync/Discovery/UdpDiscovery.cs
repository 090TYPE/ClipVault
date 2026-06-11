using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ClipVault.Core.Abstractions;

namespace ClipVault.Sync.Discovery;

/// Broadcasts this device's DeviceInfo on a UDP port every few seconds and
/// listens for others. Peers expire if not seen for 15 seconds.
public class UdpDiscovery : IDisposable
{
    public const int DiscoveryPort = 45678;

    private readonly DeviceInfo _self;
    private readonly UdpClient _udp;
    private readonly Dictionary<string, (DiscoveredPeer Peer, DateTime Seen)> _peers = new();
    private readonly object _lock = new();
    private CancellationTokenSource? _cts;

    public UdpDiscovery(DeviceInfo self)
    {
        _self = self;
        _udp = new UdpClient();
        _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _udp.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
        _udp.EnableBroadcast = true;
    }

    public IReadOnlyList<DiscoveredPeer> Peers
    {
        get
        {
            lock (_lock)
            {
                var cutoff = DateTime.UtcNow.AddSeconds(-15);
                return _peers.Values.Where(v => v.Seen >= cutoff).Select(v => v.Peer).ToList();
            }
        }
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _ = ListenLoop(_cts.Token);
        _ = BroadcastLoop(_cts.Token);
    }

    private async Task BroadcastLoop(CancellationToken ct)
    {
        var payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(_self));
        var ep = new IPEndPoint(IPAddress.Broadcast, DiscoveryPort);
        while (!ct.IsCancellationRequested)
        {
            try { await _udp.SendAsync(payload, payload.Length, ep); } catch { }
            try { await Task.Delay(2000, ct); } catch { break; }
        }
    }

    private async Task ListenLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = await _udp.ReceiveAsync(ct);
                var info = JsonSerializer.Deserialize<DeviceInfo>(
                    Encoding.UTF8.GetString(result.Buffer));
                if (info is null || info.DeviceId == _self.DeviceId) continue; // ignore self
                var peer = new DiscoveredPeer(info.DeviceId, info.Name,
                    result.RemoteEndPoint.Address.ToString(), info.TcpPort);
                lock (_lock) _peers[info.DeviceId] = (peer, DateTime.UtcNow);
            }
            catch (OperationCanceledException) { break; }
            catch { /* malformed packet — ignore */ }
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _udp.Dispose();
    }
}
