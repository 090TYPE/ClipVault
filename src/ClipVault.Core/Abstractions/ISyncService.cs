using ClipVault.Core.Models;

namespace ClipVault.Core.Abstractions;

public interface ISyncService : IDisposable
{
    /// Begin discovery + transport. No pairing happens automatically.
    void Start();

    /// Send a locally-captured item to all connected trusted peers.
    Task BroadcastAsync(ClipItem item);

    /// Raised when an item arrives from a trusted peer. The host inserts it
    /// into IClipStore (whose hash dedup prevents echo loops).
    event Action<ClipItem>? RemoteClipReceived;

    /// Peers currently visible on the LAN.
    IReadOnlyList<DiscoveredPeer> DiscoveredPeers { get; }

    /// Trusted (paired) devices.
    IReadOnlyList<PairedDevice> TrustedDevices { get; }

    /// Enter pairing mode and return the 6-digit PIN to show the user.
    /// The other device must call PairWithAsync with this PIN.
    string EnterPairingMode();

    /// Pair with a discovered peer by entering the PIN it is displaying.
    /// Returns true on success (PIN verified, key stored).
    Task<bool> PairWithAsync(DiscoveredPeer peer, string pin);
}
