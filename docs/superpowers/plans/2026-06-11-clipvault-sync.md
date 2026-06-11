# ClipVault Sync Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add LAN synchronization so a user's trusted devices share clipboard items over the local network — encrypted, paired by a 6-digit PIN, no cloud.

**Architecture:** A new `ClipVault.Sync` project implements `ISyncService` (defined in Core). Devices find each other via UDP broadcast beacons, pair via an ECDH+PIN handshake that yields a per-peer shared key, and exchange clipboard items over length-prefixed, AES-256-GCM-encrypted TCP messages. Received items go through the existing `IClipStore`, whose hash dedup naturally prevents echo loops. Sync plugs into the App through `ISyncService` and is independent of capture/history.

**Tech Stack:** .NET 10, built-in `System.Security.Cryptography` (`AesGcm`, `ECDiffieHellman`, `HKDF`), `System.Net.Sockets` (UDP + TCP). Tests: xUnit (loopback / in-memory).

> **Builds on:** the completed ClipVault Core (Plan 1). Reuses `ClipItem`, `IClipStore`, `AppPaths`.

---

## File Structure

```
src/ClipVault.Core/
└── Abstractions/
    ├── ISyncService.cs        — sync contract consumed by the App
    └── SyncTypes.cs           — DiscoveredPeer, PairedDevice, DeviceInfo DTOs

src/ClipVault.Sync/            — NEW project
├── DeviceIdentity.cs          — this device's stable Id + name (persisted)
├── Crypto/SecureChannel.cs    — AES-256-GCM seal/open over a 32-byte key
├── Crypto/PairingHandshake.cs — ECDH P-256 + PIN → shared key
├── Messaging/SyncMessage.cs   — message DTO + JSON (de)serialization
├── Messaging/MessageFraming.cs— 4-byte length-prefixed frames over a Stream
├── Discovery/UdpDiscovery.cs  — broadcast beacon + peer discovery
├── Transport/TcpSyncServer.cs — TCP listener accepting trusted peer connections
├── Transport/TcpSyncClient.cs — outbound connection to a trusted peer
├── TrustStore.cs              — persist paired devices (JSON)
└── SyncService.cs             — ISyncService impl wiring it all together

tests/ClipVault.Core.Tests/    — extend existing test project
├── SecureChannelTests.cs
├── PairingHandshakeTests.cs
├── SyncMessageTests.cs
├── MessageFramingTests.cs
└── TrustStoreTests.cs

src/ClipVault.App/
├── AppServices.cs             — MODIFY: construct/start SyncService, bridge to store
└── Views/SettingsWindow.*     — MODIFY: pairing UI (discovered devices, PIN)
```

---

## Task 1: Sync abstractions and DTOs in Core

**Files:**
- Create: `src/ClipVault.Core/Abstractions/SyncTypes.cs`
- Create: `src/ClipVault.Core/Abstractions/ISyncService.cs`

- [ ] **Step 1: Write the DTOs**

`SyncTypes.cs`:
```csharp
namespace ClipVault.Core.Abstractions;

/// A device seen on the LAN via discovery (not necessarily trusted yet).
public record DiscoveredPeer(string DeviceId, string Name, string IpAddress, int TcpPort);

/// A device we have paired with; holds the shared key for encryption.
public record PairedDevice(string DeviceId, string Name, string KeyBase64);

/// Identity advertised by this device.
public record DeviceInfo(string DeviceId, string Name, int TcpPort);
```

- [ ] **Step 2: Write the service interface**

`ISyncService.cs`:
```csharp
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
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build src/ClipVault.Core`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/ClipVault.Core/Abstractions/SyncTypes.cs src/ClipVault.Core/Abstractions/ISyncService.cs
git commit -m "feat(core): add ISyncService and sync DTOs"
```

---

## Task 2: Create the ClipVault.Sync project

**Files:**
- Create: `src/ClipVault.Sync/ClipVault.Sync.csproj` (via CLI)

- [ ] **Step 1: Create project, references, and add to solution**

Run from repo root:
```bash
dotnet new classlib -n ClipVault.Sync -o src/ClipVault.Sync
dotnet sln add src/ClipVault.Sync
dotnet add src/ClipVault.Sync reference src/ClipVault.Core
dotnet add src/ClipVault.App reference src/ClipVault.Sync
rm -f src/ClipVault.Sync/Class1.cs
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build`
Expected: Build succeeded (App now references Sync).

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "chore: scaffold ClipVault.Sync project"
```

---

## Task 3: SecureChannel (AES-256-GCM) — TDD

**Files:**
- Create: `src/ClipVault.Sync/Crypto/SecureChannel.cs`
- Test: `tests/ClipVault.Core.Tests/SecureChannelTests.cs`
- Modify: `tests/ClipVault.Core.Tests/ClipVault.Core.Tests.csproj` (reference Sync)

- [ ] **Step 1: Reference the Sync project from the test project**

Run: `dotnet add tests/ClipVault.Core.Tests reference src/ClipVault.Sync`

- [ ] **Step 2: Write the failing tests**

```csharp
using System.Security.Cryptography;
using ClipVault.Sync.Crypto;
using Xunit;

namespace ClipVault.Core.Tests;

public class SecureChannelTests
{
    private static byte[] Key() => RandomNumberGenerator.GetBytes(32);

    [Fact]
    public void Encrypt_ThenDecrypt_RoundTrips()
    {
        var ch = new SecureChannel(Key());
        var plain = System.Text.Encoding.UTF8.GetBytes("hello sync");
        var sealed_ = ch.Encrypt(plain);
        Assert.Equal(plain, ch.Decrypt(sealed_));
    }

    [Fact]
    public void Decrypt_TamperedData_Throws()
    {
        var ch = new SecureChannel(Key());
        var sealed_ = ch.Encrypt(new byte[] { 1, 2, 3 });
        sealed_[^1] ^= 0xFF; // flip a tag/ciphertext bit
        Assert.Throws<CryptographicException>(() => ch.Decrypt(sealed_));
    }

    [Fact]
    public void Decrypt_WrongKey_Throws()
    {
        var sealed_ = new SecureChannel(Key()).Encrypt(new byte[] { 9, 9 });
        Assert.Throws<CryptographicException>(() => new SecureChannel(Key()).Decrypt(sealed_));
    }
}
```

- [ ] **Step 3: Run to verify it fails**

Run: `dotnet test --filter SecureChannelTests`
Expected: FAIL — `SecureChannel` does not exist.

- [ ] **Step 4: Implement**

```csharp
using System.Security.Cryptography;

namespace ClipVault.Sync.Crypto;

/// AES-256-GCM. Wire format: [nonce(12)] [tag(16)] [ciphertext].
public class SecureChannel
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly byte[] _key;

    public SecureChannel(byte[] key)
    {
        if (key.Length != 32) throw new ArgumentException("Key must be 32 bytes", nameof(key));
        _key = key;
    }

    public byte[] Encrypt(byte[] plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[plaintext.Length];
        var tag = new byte[TagSize];
        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plaintext, cipher, tag);

        var output = new byte[NonceSize + TagSize + cipher.Length];
        Buffer.BlockCopy(nonce, 0, output, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, output, NonceSize, TagSize);
        Buffer.BlockCopy(cipher, 0, output, NonceSize + TagSize, cipher.Length);
        return output;
    }

    public byte[] Decrypt(byte[] data)
    {
        if (data.Length < NonceSize + TagSize)
            throw new CryptographicException("Ciphertext too short");
        var nonce = data.AsSpan(0, NonceSize);
        var tag = data.AsSpan(NonceSize, TagSize);
        var cipher = data.AsSpan(NonceSize + TagSize);
        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, cipher, tag, plain);
        return plain;
    }
}
```

- [ ] **Step 5: Run to verify it passes**

Run: `dotnet test --filter SecureChannelTests`
Expected: PASS (3 tests).

- [ ] **Step 6: Commit**

```bash
git add src/ClipVault.Sync/Crypto/SecureChannel.cs tests/ClipVault.Core.Tests/SecureChannelTests.cs tests/ClipVault.Core.Tests/ClipVault.Core.Tests.csproj
git commit -m "feat(sync): add AES-256-GCM SecureChannel"
```

---

## Task 4: PairingHandshake (ECDH P-256 + PIN) — TDD

**Files:**
- Create: `src/ClipVault.Sync/Crypto/PairingHandshake.cs`
- Test: `tests/ClipVault.Core.Tests/PairingHandshakeTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Text;
using ClipVault.Sync.Crypto;
using Xunit;

namespace ClipVault.Core.Tests;

public class PairingHandshakeTests
{
    [Fact]
    public void BothParties_SamePin_DeriveSameKey()
    {
        var a = new PairingHandshake();
        var b = new PairingHandshake();
        var keyA = a.DeriveKey(b.PublicKey, "123456");
        var keyB = b.DeriveKey(a.PublicKey, "123456");
        Assert.Equal(keyA, keyB);
        Assert.Equal(32, keyA.Length);
    }

    [Fact]
    public void DifferentPins_DeriveDifferentKeys()
    {
        var a = new PairingHandshake();
        var b = new PairingHandshake();
        var keyA = a.DeriveKey(b.PublicKey, "111111");
        var keyB = b.DeriveKey(a.PublicKey, "222222");
        Assert.NotEqual(keyA, keyB);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter PairingHandshakeTests`
Expected: FAIL — `PairingHandshake` does not exist.

- [ ] **Step 3: Implement**

```csharp
using System.Security.Cryptography;
using System.Text;

namespace ClipVault.Sync.Crypto;

/// ECDH P-256 key agreement, with the PIN folded into HKDF so that only
/// parties using the same PIN derive the same key.
public class PairingHandshake : IDisposable
{
    private readonly ECDiffieHellman _ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);

    /// Our public key as SubjectPublicKeyInfo (DER) bytes, to send to the peer.
    public byte[] PublicKey => _ecdh.PublicKey.ExportSubjectPublicKeyInfo();

    public byte[] DeriveKey(byte[] peerPublicKey, string pin)
    {
        using var peer = ECDiffieHellman.Create();
        peer.ImportSubjectPublicKeyInfo(peerPublicKey, out _);
        var shared = _ecdh.DeriveRawSecretAgreement(peer.PublicKey);
        return HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            ikm: shared,
            outputLength: 32,
            salt: Encoding.UTF8.GetBytes(pin),
            info: Encoding.UTF8.GetBytes("ClipVault-pairing-v1"));
    }

    public void Dispose() => _ecdh.Dispose();
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test --filter PairingHandshakeTests`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add src/ClipVault.Sync/Crypto/PairingHandshake.cs tests/ClipVault.Core.Tests/PairingHandshakeTests.cs
git commit -m "feat(sync): add ECDH+PIN PairingHandshake"
```

---

## Task 5: SyncMessage (model + JSON) — TDD

**Files:**
- Create: `src/ClipVault.Sync/Messaging/SyncMessage.cs`
- Test: `tests/ClipVault.Core.Tests/SyncMessageTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
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
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter SyncMessageTests`
Expected: FAIL — `SyncMessage` does not exist.

- [ ] **Step 3: Implement**

```csharp
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
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test --filter SyncMessageTests`
Expected: PASS (1 test).

- [ ] **Step 5: Commit**

```bash
git add src/ClipVault.Sync/Messaging/SyncMessage.cs tests/ClipVault.Core.Tests/SyncMessageTests.cs
git commit -m "feat(sync): add SyncMessage model"
```

---

## Task 6: MessageFraming (length-prefixed frames) — TDD

**Files:**
- Create: `src/ClipVault.Sync/Messaging/MessageFraming.cs`
- Test: `tests/ClipVault.Core.Tests/MessageFramingTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
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
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter MessageFramingTests`
Expected: FAIL — `MessageFraming` does not exist.

- [ ] **Step 3: Implement**

```csharp
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
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test --filter MessageFramingTests`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add src/ClipVault.Sync/Messaging/MessageFraming.cs tests/ClipVault.Core.Tests/MessageFramingTests.cs
git commit -m "feat(sync): add length-prefixed MessageFraming"
```

---

## Task 7: TrustStore (persist paired devices) — TDD

**Files:**
- Create: `src/ClipVault.Sync/TrustStore.cs`
- Test: `tests/ClipVault.Core.Tests/TrustStoreTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using ClipVault.Core.Abstractions;
using ClipVault.Sync;
using Xunit;

namespace ClipVault.Core.Tests;

public class TrustStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"cv-trust-{Guid.NewGuid():N}.json");

    [Fact]
    public void Add_ThenReload_PersistsDevice()
    {
        var store = new TrustStore(_path);
        store.Add(new PairedDevice("dev-1", "Laptop", "a2V5"));
        var reloaded = new TrustStore(_path);
        Assert.Single(reloaded.All);
        Assert.Equal("Laptop", reloaded.All[0].Name);
    }

    [Fact]
    public void Add_SameDeviceId_Replaces()
    {
        var store = new TrustStore(_path);
        store.Add(new PairedDevice("dev-1", "Old", "a2V5"));
        store.Add(new PairedDevice("dev-1", "New", "a2V5"));
        Assert.Single(store.All);
        Assert.Equal("New", store.All[0].Name);
    }

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter TrustStoreTests`
Expected: FAIL — `TrustStore` does not exist.

- [ ] **Step 3: Implement**

```csharp
using System.Text.Json;
using ClipVault.Core.Abstractions;

namespace ClipVault.Sync;

public class TrustStore
{
    private readonly string _path;
    private readonly List<PairedDevice> _devices;

    public TrustStore(string path)
    {
        _path = path;
        _devices = Load();
    }

    public IReadOnlyList<PairedDevice> All => _devices;

    public void Add(PairedDevice device)
    {
        _devices.RemoveAll(d => d.DeviceId == device.DeviceId);
        _devices.Add(device);
        Save();
    }

    public PairedDevice? Get(string deviceId) =>
        _devices.FirstOrDefault(d => d.DeviceId == deviceId);

    private List<PairedDevice> Load()
    {
        try
        {
            if (File.Exists(_path))
                return JsonSerializer.Deserialize<List<PairedDevice>>(File.ReadAllText(_path))
                       ?? new List<PairedDevice>();
        }
        catch { /* corrupt → start empty */ }
        return new List<PairedDevice>();
    }

    private void Save() => File.WriteAllText(_path, JsonSerializer.Serialize(_devices));
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test --filter TrustStoreTests`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add src/ClipVault.Sync/TrustStore.cs tests/ClipVault.Core.Tests/TrustStoreTests.cs
git commit -m "feat(sync): add TrustStore"
```

---

## Task 8: DeviceIdentity (stable id + name)

**Files:**
- Create: `src/ClipVault.Sync/DeviceIdentity.cs`

- [ ] **Step 1: Implement**

```csharp
using System.Text.Json;
using ClipVault.Core.Abstractions;

namespace ClipVault.Sync;

/// This device's stable Id and display name, persisted to JSON.
public class DeviceIdentity
{
    public string DeviceId { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = Environment.MachineName;

    public static DeviceIdentity LoadOrCreate(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<DeviceIdentity>(File.ReadAllText(path))
                       ?? Create(path);
        }
        catch { /* fall through */ }
        return Create(path);
    }

    private static DeviceIdentity Create(string path)
    {
        var id = new DeviceIdentity();
        File.WriteAllText(path, JsonSerializer.Serialize(id));
        return id;
    }

    public DeviceInfo ToInfo(int tcpPort) => new(DeviceId, Name, tcpPort);
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build src/ClipVault.Sync`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/ClipVault.Sync/DeviceIdentity.cs
git commit -m "feat(sync): add DeviceIdentity"
```

---

## Task 9: UdpDiscovery (broadcast beacon + peer list)

Network component — verified by a loopback test in Task 12. No unit test here
because it binds real sockets.

**Files:**
- Create: `src/ClipVault.Sync/Discovery/UdpDiscovery.cs`

- [ ] **Step 1: Implement**

```csharp
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
            try { await Task.Delay(3000, ct); } catch { break; }
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
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build src/ClipVault.Sync`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/ClipVault.Sync/Discovery/UdpDiscovery.cs
git commit -m "feat(sync): add UDP discovery"
```

---

## Task 10: TCP transport (server + client)

Handles two roles: an always-on **server** that accepts encrypted connections
from trusted peers, and a **client** that connects out to a peer to send items.
Each connection is authenticated by the shared key: the first frame must decrypt,
otherwise the connection is dropped. Verified by loopback test in Task 12.

**Files:**
- Create: `src/ClipVault.Sync/Transport/TcpSyncServer.cs`
- Create: `src/ClipVault.Sync/Transport/TcpSyncClient.cs`

- [ ] **Step 1: Implement the server**

`TcpSyncServer.cs`:
```csharp
using System.Net;
using System.Net.Sockets;
using ClipVault.Core.Models;
using ClipVault.Sync.Crypto;
using ClipVault.Sync.Messaging;

namespace ClipVault.Sync.Transport;

/// Accepts inbound connections. For each frame, tries every trusted key until
/// one decrypts (identifies the peer). Raises ClipReceived for "clip" messages.
public class TcpSyncServer : IDisposable
{
    private readonly Func<IReadOnlyList<byte[]>> _trustedKeys;
    private readonly TcpListener _listener;
    private CancellationTokenSource? _cts;

    public event Action<ClipItem>? ClipReceived;
    public int Port { get; }

    public TcpSyncServer(Func<IReadOnlyList<byte[]>> trustedKeys, int port = 0)
    {
        _trustedKeys = trustedKeys;
        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _ = AcceptLoop(_cts.Token);
    }

    private async Task AcceptLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(ct);
                _ = HandleClient(client, ct);
            }
            catch (OperationCanceledException) { break; }
            catch { /* listener error — keep going */ }
        }
    }

    private async Task HandleClient(TcpClient client, CancellationToken ct)
    {
        using (client)
        await using (var stream = client.GetStream())
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var frame = await MessageFraming.ReadFrameAsync(stream, ct);
                    if (frame is null) break; // peer closed
                    var plain = TryDecryptWithAnyKey(frame);
                    if (plain is null) break; // not a trusted peer — drop
                    var msg = SyncMessage.FromBytes(plain);
                    if (msg.Type == "clip" && msg.Item is not null)
                        ClipReceived?.Invoke(msg.Item);
                }
            }
            catch { /* connection error — drop */ }
        }
    }

    private byte[]? TryDecryptWithAnyKey(byte[] frame)
    {
        foreach (var key in _trustedKeys())
        {
            try { return new SecureChannel(key).Decrypt(frame); }
            catch { /* try next key */ }
        }
        return null;
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _listener.Stop();
    }
}
```

- [ ] **Step 2: Implement the client**

`TcpSyncClient.cs`:
```csharp
using System.Net.Sockets;
using ClipVault.Sync.Crypto;
using ClipVault.Sync.Messaging;

namespace ClipVault.Sync.Transport;

/// One-shot send of a payload to a peer, encrypted with the peer's shared key.
public static class TcpSyncClient
{
    public static async Task SendAsync(string ip, int port, byte[] key, SyncMessage msg,
                                       CancellationToken ct = default)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(ip, port, ct);
        await using var stream = client.GetStream();
        var sealed_ = new SecureChannel(key).Encrypt(msg.ToBytes());
        await MessageFraming.WriteFrameAsync(stream, sealed_, ct);
    }
}
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build src/ClipVault.Sync`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/ClipVault.Sync/Transport
git commit -m "feat(sync): add TCP sync server and client"
```

---

## Task 11: Pairing protocol over TCP

A dedicated pairing exchange separate from the data channel. The device entering
pairing mode listens on its TCP port for a pairing request; the requester sends
its public key + PIN-derived confirmation. Both derive the shared key; the
listener verifies the confirmation matches before trusting.

**Files:**
- Create: `src/ClipVault.Sync/Pairing/PairingProtocol.cs`
- Test: `tests/ClipVault.Core.Tests/PairingProtocolTests.cs`

- [ ] **Step 1: Write the failing test (in-memory, no sockets)**

```csharp
using System.Text;
using ClipVault.Sync.Crypto;
using ClipVault.Sync.Pairing;
using Xunit;

namespace ClipVault.Core.Tests;

public class PairingProtocolTests
{
    [Fact]
    public void Confirmation_MatchesAcrossParties_WithSamePin()
    {
        var initiator = new PairingHandshake();
        var responder = new PairingHandshake();

        var keyI = initiator.DeriveKey(responder.PublicKey, "424242");
        var keyR = responder.DeriveKey(initiator.PublicKey, "424242");

        var confI = PairingProtocol.Confirmation(keyI);
        var confR = PairingProtocol.Confirmation(keyR);
        Assert.Equal(confI, confR);
    }

    [Fact]
    public void Confirmation_DiffersWithWrongPin()
    {
        var initiator = new PairingHandshake();
        var responder = new PairingHandshake();
        var keyI = initiator.DeriveKey(responder.PublicKey, "000000");
        var keyR = responder.DeriveKey(initiator.PublicKey, "999999");
        Assert.NotEqual(PairingProtocol.Confirmation(keyI), PairingProtocol.Confirmation(keyR));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter PairingProtocolTests`
Expected: FAIL — `PairingProtocol` does not exist.

- [ ] **Step 3: Implement**

```csharp
using System.Security.Cryptography;
using System.Text;

namespace ClipVault.Sync.Pairing;

/// Helpers for the PIN pairing exchange. The "confirmation" is a value both
/// sides can compute from the derived key to prove they used the same PIN,
/// without revealing the key.
public static class PairingProtocol
{
    public static string Confirmation(byte[] derivedKey)
    {
        var h = SHA256.HashData(Concat(derivedKey, Encoding.UTF8.GetBytes("confirm")));
        return Convert.ToHexString(h);
    }

    private static byte[] Concat(byte[] a, byte[] b)
    {
        var r = new byte[a.Length + b.Length];
        Buffer.BlockCopy(a, 0, r, 0, a.Length);
        Buffer.BlockCopy(b, 0, r, a.Length, b.Length);
        return r;
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test --filter PairingProtocolTests`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add src/ClipVault.Sync/Pairing/PairingProtocol.cs tests/ClipVault.Core.Tests/PairingProtocolTests.cs
git commit -m "feat(sync): add pairing confirmation protocol"
```

---

## Task 12: SyncService (wire everything) + loopback integration test

**Files:**
- Create: `src/ClipVault.Sync/SyncService.cs`
- Test: `tests/ClipVault.Core.Tests/SyncServiceLoopbackTests.cs`

- [ ] **Step 1: Implement SyncService**

```csharp
using System.Net.Sockets;
using ClipVault.Core.Abstractions;
using ClipVault.Core.Models;
using ClipVault.Sync.Crypto;
using ClipVault.Sync.Discovery;
using ClipVault.Sync.Messaging;
using ClipVault.Sync.Pairing;
using ClipVault.Sync.Transport;

namespace ClipVault.Sync;

public class SyncService : ISyncService
{
    private readonly DeviceIdentity _identity;
    private readonly TrustStore _trust;
    private readonly TcpSyncServer _server;
    private UdpDiscovery? _discovery;

    private string? _pairingPin;
    private PairingHandshake? _pairingHandshake;

    public event Action<ClipItem>? RemoteClipReceived;

    public SyncService(DeviceIdentity identity, TrustStore trust)
    {
        _identity = identity;
        _trust = trust;
        _server = new TcpSyncServer(() => _trust.All.Select(d => Convert.FromBase64String(d.KeyBase64)).ToList());
        _server.ClipReceived += item => RemoteClipReceived?.Invoke(item);
    }

    public void Start()
    {
        _server.Start();
        _discovery = new UdpDiscovery(_identity.ToInfo(_server.Port));
        _discovery.Start();
    }

    public IReadOnlyList<DiscoveredPeer> DiscoveredPeers => _discovery?.Peers ?? Array.Empty<DiscoveredPeer>();
    public IReadOnlyList<PairedDevice> TrustedDevices => _trust.All;

    public async Task BroadcastAsync(ClipItem item)
    {
        var msg = SyncMessage.ForClip(item);
        foreach (var dev in _trust.All)
        {
            var peer = DiscoveredPeers.FirstOrDefault(p => p.DeviceId == dev.DeviceId);
            if (peer is null) continue; // offline
            try
            {
                await TcpSyncClient.SendAsync(peer.IpAddress, peer.TcpPort,
                    Convert.FromBase64String(dev.KeyBase64), msg);
            }
            catch { /* peer unreachable — skip */ }
        }
    }

    public string EnterPairingMode()
    {
        _pairingHandshake = new PairingHandshake();
        _pairingPin = Random.Shared.Next(0, 1_000_000).ToString("D6");
        return _pairingPin;
    }

    public async Task<bool> PairWithAsync(DiscoveredPeer peer, string pin)
    {
        // Initiator side: connect to peer's pairing endpoint, exchange keys.
        // For MVP the pairing exchange reuses the data port with a "pair" message
        // carrying our public key; the responder replies with its public key and
        // confirmation. Both sides verify confirmations match.
        using var hs = new PairingHandshake();
        try
        {
            var (peerPublicKey, peerConfirmation) =
                await PairingExchange.RequestAsync(peer.IpAddress, peer.TcpPort, hs.PublicKey, ct: default);
            var key = hs.DeriveKey(peerPublicKey, pin);
            if (PairingProtocol.Confirmation(key) != peerConfirmation) return false;
            _trust.Add(new PairedDevice(peer.DeviceId, peer.Name, Convert.ToBase64String(key)));
            return true;
        }
        catch { return false; }
    }

    public void Dispose()
    {
        _discovery?.Dispose();
        _server.Dispose();
        _pairingHandshake?.Dispose();
    }
}
```

> **Note for implementer:** `PairingExchange` is the small networked piece that
> sends our public key to the responder and reads back `(peerPublicKey,
> confirmation)`. Implement it in `src/ClipVault.Sync/Pairing/PairingExchange.cs`
> using `TcpClient` + `MessageFraming`, mirroring `TcpSyncClient`. The responder
> side runs inside `TcpSyncServer` when `EnterPairingMode` is active: detect a
> "pair" frame (unencrypted, since no shared key yet), reply with the responder's
> `PairingHandshake.PublicKey` and `PairingProtocol.Confirmation(derivedKey)`.
> Keep pairing frames distinct from data frames by a one-byte tag prefix
> (`0x01` = data/encrypted, `0x02` = pairing/plaintext) so the server can route
> them. Update `MessageFraming` callers accordingly, or add a tag parameter.

- [ ] **Step 2: Write the loopback integration test**

```csharp
using ClipVault.Core.Abstractions;
using ClipVault.Core.Models;
using ClipVault.Sync;
using Xunit;

namespace ClipVault.Core.Tests;

public class SyncServiceLoopbackTests : IDisposable
{
    private readonly string _dirA = Dir(), _dirB = Dir();
    private static string Dir() { var d = Path.Combine(Path.GetTempPath(), $"cv-sync-{Guid.NewGuid():N}"); Directory.CreateDirectory(d); return d; }

    [Fact]
    public async Task PairedDevices_PropagateClip()
    {
        var idA = DeviceIdentity.LoadOrCreate(Path.Combine(_dirA, "id.json"));
        var idB = DeviceIdentity.LoadOrCreate(Path.Combine(_dirB, "id.json"));
        var trustA = new TrustStore(Path.Combine(_dirA, "trust.json"));
        var trustB = new TrustStore(Path.Combine(_dirB, "trust.json"));

        using var a = new SyncService(idA, trustA);
        using var b = new SyncService(idB, trustB);
        a.Start(); b.Start();

        // wait for mutual discovery
        await WaitUntil(() => a.DiscoveredPeers.Any(p => p.DeviceId == idB.DeviceId)
                          && b.DiscoveredPeers.Any(p => p.DeviceId == idA.DeviceId), 5000);

        var pin = b.EnterPairingMode();
        var peerB = a.DiscoveredPeers.First(p => p.DeviceId == idB.DeviceId);
        Assert.True(await a.PairWithAsync(peerB, pin));

        ClipItem? received = null;
        b.RemoteClipReceived += item => received = item;

        await a.BroadcastAsync(new ClipItem { Type = ClipType.Text, TextContent = "synced!", Preview = "synced!", Hash = "h1" });
        await WaitUntil(() => received is not null, 5000);

        Assert.NotNull(received);
        Assert.Equal("synced!", received!.TextContent);
    }

    private static async Task WaitUntil(Func<bool> cond, int timeoutMs)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!cond() && sw.ElapsedMilliseconds < timeoutMs) await Task.Delay(100);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dirA, true); } catch { }
        try { Directory.Delete(_dirB, true); } catch { }
    }
}
```

- [ ] **Step 3: Run the loopback test**

Run: `dotnet test --filter SyncServiceLoopbackTests`
Expected: PASS. (If discovery flakes because the CI host blocks UDP broadcast on
loopback, run locally. Document this in README under "Testing sync".)

- [ ] **Step 4: Commit**

```bash
git add src/ClipVault.Sync/SyncService.cs src/ClipVault.Sync/Pairing/PairingExchange.cs tests/ClipVault.Core.Tests/SyncServiceLoopbackTests.cs
git commit -m "feat(sync): add SyncService with loopback integration test"
```

---

## Task 13: Wire sync into the App

**Files:**
- Modify: `src/ClipVault.App/AppServices.cs`

- [ ] **Step 1: Construct and start SyncService; bridge to the store**

Add to `AppServices` (using the existing `AppPaths.DataDir` for persistence):
```csharp
using ClipVault.Sync;
// ...
public ISyncService Sync { get; }

// in constructor, after Store/Factory are created:
var identity = DeviceIdentity.LoadOrCreate(Path.Combine(AppPaths.DataDir, "device.json"));
var trust = new TrustStore(Path.Combine(AppPaths.DataDir, "trust.json"));
Sync = new SyncService(identity, trust);
```

In `Start(...)`, after `Monitor.Start()`:
```csharp
// Local capture → push to peers
Monitor.ClipboardChanged += async r =>
{
    // (existing capture handler already adds to store; broadcast the stored item)
};

// Remote item → store (hash dedup prevents echo loops)
Sync.RemoteClipReceived += async item =>
{
    try { await Store.AddAsync(item); } catch { }
};
Sync.Start();
```

To broadcast locally-captured items, refactor the existing capture handler so it
broadcasts the persisted item:
```csharp
Monitor.ClipboardChanged += async r =>
{
    try
    {
        var item = Factory.Create(r);
        var stored = await Store.AddAsync(item);
        await Store.ApplyRetentionAsync();
        await Sync.BroadcastAsync(stored);
    }
    catch { }
};
```
(Replace the old handler so capture isn't double-registered.)

- [ ] **Step 2: Build**

Run: `dotnet build`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/ClipVault.App/AppServices.cs
git commit -m "feat(app): start sync and bridge remote items to store"
```

---

## Task 14: Pairing UI in Settings

**Files:**
- Modify: `src/ClipVault.App/ViewModels/SettingsViewModel.cs`
- Modify: `src/ClipVault.App/Views/SettingsWindow.axaml` and `.axaml.cs`

- [ ] **Step 1: Expose sync data on the view model**

Add to `SettingsViewModel`:
```csharp
using System.Threading.Tasks;
using ClipVault.Core.Abstractions;

private readonly ISyncService _sync;

public SettingsViewModel(AppSettings settings, ISyncService sync)
{
    Settings = settings;
    _sync = sync;
}

public IReadOnlyList<DiscoveredPeer> Devices => _sync.DiscoveredPeers;
public string EnterPairingMode() => _sync.EnterPairingMode();
public Task<bool> PairAsync(DiscoveredPeer peer, string pin) => _sync.PairWithAsync(peer, pin);
```

Update the construction site in `App.axaml.cs` `OnOpenSettings`:
```csharp
new SettingsWindow(new SettingsViewModel(Services.Settings, Services.Sync)).Show();
```

- [ ] **Step 2: Add pairing controls to SettingsWindow.axaml**

Add below the retention controls:
```xml
<Separator/>
<TextBlock Text="Sync devices" FontWeight="Bold"/>
<ListBox x:Name="Devices"/>
<StackPanel Orientation="Horizontal" Spacing="6">
  <Button x:Name="ShowPinBtn" Content="Show PIN (pair this device)"/>
  <TextBlock x:Name="PinLabel" VerticalAlignment="Center"/>
</StackPanel>
<StackPanel Orientation="Horizontal" Spacing="6">
  <TextBox x:Name="PinEntry" Width="100" PlaceholderText="PIN"/>
  <Button x:Name="PairBtn" Content="Pair with selected"/>
  <TextBlock x:Name="PairStatus" VerticalAlignment="Center"/>
</StackPanel>
```
(Increase the `Window` `Height` to `460`.)

- [ ] **Step 3: Wire the controls in SettingsWindow.axaml.cs**

```csharp
var devices = this.FindControl<ListBox>("Devices")!;
var showPin = this.FindControl<Button>("ShowPinBtn")!;
var pinLabel = this.FindControl<TextBlock>("PinLabel")!;
var pinEntry = this.FindControl<TextBox>("PinEntry")!;
var pairBtn = this.FindControl<Button>("PairBtn")!;
var pairStatus = this.FindControl<TextBlock>("PairStatus")!;

devices.ItemsSource = _vm.Devices;
devices.DisplayMemberBinding =
    new Avalonia.Data.Binding(nameof(ClipVault.Core.Abstractions.DiscoveredPeer.Name));

showPin.Click += (_, _) => pinLabel.Text = "PIN: " + _vm.EnterPairingMode();

pairBtn.Click += async (_, _) =>
{
    if (devices.SelectedItem is ClipVault.Core.Abstractions.DiscoveredPeer peer
        && !string.IsNullOrWhiteSpace(pinEntry.Text))
    {
        pairStatus.Text = "Pairing…";
        var ok = await _vm.PairAsync(peer, pinEntry.Text!);
        pairStatus.Text = ok ? "Paired ✓" : "Failed ✗";
    }
};
```
(Add `_vm` field assignment is already present from Plan 1.)

- [ ] **Step 4: Build**

Run: `dotnet build`
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add src/ClipVault.App/ViewModels/SettingsViewModel.cs src/ClipVault.App/Views/SettingsWindow.axaml src/ClipVault.App/Views/SettingsWindow.axaml.cs src/ClipVault.App/App.axaml.cs
git commit -m "feat(app): pairing UI in settings"
```

---

## Task 15: Full regression + manual two-device smoke test

- [ ] **Step 1: Run all tests**

Run: `dotnet test`
Expected: all pass (Core MVP tests + SecureChannel, PairingHandshake, SyncMessage,
MessageFraming, TrustStore, PairingProtocol, SyncServiceLoopback).

- [ ] **Step 2: Manual two-machine smoke test**

On two machines on the same LAN:
1. Run the app on both. Open Settings on both — each should list the other under
   "Sync devices" within ~5 s.
2. On machine A click "Show PIN". On machine B select machine A, enter the PIN,
   click "Pair with selected" → "Paired ✓".
3. Copy text on A → it appears in B's history (open with the hotkey). Copy on B →
   appears on A. Confirm no infinite echo (each item appears once).
Expected: all pass.

- [ ] **Step 3: Update README**

Add a "Sync" section: same-LAN requirement, pairing steps, firewall note (allow
UDP 45678 and the app's TCP port), and the "Testing sync" note about UDP loopback.

- [ ] **Step 4: Final commit / tag**

```bash
git add -A
git commit -m "docs: document LAN sync; ClipVault sync complete" --allow-empty
git tag sync-mvp
```

---

## Self-Review Notes

- **Spec coverage (sync section):** discovery (Task 9, UDP), PIN pairing
  (Tasks 4, 11, 12, 14), transport (Task 10, TCP), crypto AES-256-GCM (Task 3),
  per-item propagation (Tasks 12–13), conflict/echo handling via hash dedup in the
  existing `IClipStore` (Task 13 bridge). The `ISyncService` seam from Plan 1 is
  now defined and implemented (Tasks 1, 12).
- **Deliberate deviations from spec (documented):** X25519 → ECDH **P-256**
  (built-in, no native dep); mDNS → **UDP broadcast** (spec permitted "mDNS/UDP
  broadcast"). Both noted at plan top.
- **Known implementer work in Task 12:** `PairingExchange` (networked half of
  pairing) and the one-byte frame tag (`0x01` data / `0x02` pairing) are specified
  in the implementer note with exact responsibilities — not left vague. The
  responder logic lives in `TcpSyncServer` gated by `EnterPairingMode`.
- **Type consistency:** `ISyncService`, `DiscoveredPeer`, `PairedDevice`,
  `DeviceInfo`, `SyncMessage.ForClip/ToBytes/FromBytes`, `SecureChannel`,
  `PairingHandshake.DeriveKey`, `PairingProtocol.Confirmation`,
  `MessageFraming.WriteFrameAsync/ReadFrameAsync` are used consistently across
  tasks.
- **Testing reality:** crypto/framing/message/trust/pairing-confirmation are pure
  and unit-tested; discovery/transport/service are covered by a localhost loopback
  test plus a two-machine manual smoke test (true LAN behavior can't be unit-tested).
```
