namespace ClipVault.Core.Abstractions;

/// A device seen on the LAN via discovery (not necessarily trusted yet).
public record DiscoveredPeer(string DeviceId, string Name, string IpAddress, int TcpPort);

/// A device we have paired with; holds the shared key for encryption.
public record PairedDevice(string DeviceId, string Name, string KeyBase64);

/// Identity advertised by this device.
public record DeviceInfo(string DeviceId, string Name, int TcpPort);
