# Sync Image Bytes Implementation Plan (Phone Receive, Part A)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Transmit image bytes in sync messages so image clips sync across devices, with each receiver materializing the bytes into its own blob directory.

**Architecture:** Add an optional `ImageBytes` payload to `SyncMessage`. `SyncService` gains a `blobDir`: `BroadcastAsync` attaches an image clip's bytes; the receive path writes received bytes to `blobDir/{guid}.png` and rewrites `BlobPath` to local before raising `RemoteClipReceived`. Desktop host passes `AppPaths.BlobDir`.

**Tech Stack:** .NET 10, existing `ClipVault.Sync` + `ClipVault.Core`, xUnit (loopback).

> Verifiable here: the loopback image test runs on the dev machine. This unblocks
> and also fixes desktop↔desktop image sync.

---

## File Structure

```
src/ClipVault.Sync/Messaging/SyncMessage.cs   — MODIFY: + ImageBytes, ForClip overload
src/ClipVault.Sync/SyncService.cs             — MODIFY: + blobDir ctor param, attach/materialize
src/ClipVault.App/AppServices.cs              — MODIFY: pass AppPaths.BlobDir
tests/ClipVault.Core.Tests/SyncMessageTests.cs        — MODIFY: + image round-trip
tests/ClipVault.Core.Tests/SyncServiceLoopbackTests.cs — MODIFY: ctor + image test
```

---

## Task 1: SyncMessage carries image bytes — TDD

**Files:**
- Modify: `src/ClipVault.Sync/Messaging/SyncMessage.cs`
- Test: `tests/ClipVault.Core.Tests/SyncMessageTests.cs`

- [ ] **Step 1: Add the failing test**

Append to `SyncMessageTests`:
```csharp
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
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter SyncMessageTests`
Expected: FAIL — `ForClip(item, bytes)` / `ImageBytes` does not exist.

- [ ] **Step 3: Implement**

Replace `src/ClipVault.Sync/Messaging/SyncMessage.cs` with:
```csharp
using System.Text;
using System.Text.Json;
using ClipVault.Core.Models;

namespace ClipVault.Sync.Messaging;

public class SyncMessage
{
    public string Type { get; set; } = "";
    public ClipItem? Item { get; set; }
    public byte[]? ImageBytes { get; set; } // PNG bytes for Image clips (base64 in JSON)

    public static SyncMessage ForClip(ClipItem item) => ForClip(item, null);

    public static SyncMessage ForClip(ClipItem item, byte[]? imageBytes) =>
        new() { Type = "clip", Item = item, ImageBytes = imageBytes };

    public byte[] ToBytes() => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(this));

    public static SyncMessage FromBytes(byte[] bytes) =>
        JsonSerializer.Deserialize<SyncMessage>(Encoding.UTF8.GetString(bytes))
        ?? throw new FormatException("Invalid SyncMessage");
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test --filter SyncMessageTests`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add src/ClipVault.Sync/Messaging/SyncMessage.cs tests/ClipVault.Core.Tests/SyncMessageTests.cs
git commit -m "feat(sync): carry image bytes in SyncMessage"
```

---

## Task 2: SyncService attaches and materializes image bytes — TDD

**Files:**
- Modify: `src/ClipVault.Sync/SyncService.cs`
- Modify: `src/ClipVault.App/AppServices.cs`
- Test: `tests/ClipVault.Core.Tests/SyncServiceLoopbackTests.cs`

- [ ] **Step 1: Update the loopback test ctor calls and add the image test**

In `SyncServiceLoopbackTests`, change both constructions to pass a blob dir:
```csharp
        using var a = new SyncService(idA, trustA, Path.Combine(_dirA, "blobs"));
        using var b = new SyncService(idB, trustB, Path.Combine(_dirB, "blobs"));
```

Add this test method (reuses the same pairing helper flow):
```csharp
    [Fact]
    public async Task PairedDevices_PropagateImage_WithLocalBlob()
    {
        var idA = DeviceIdentity.LoadOrCreate(Path.Combine(_dirA, "id.json"));
        var idB = DeviceIdentity.LoadOrCreate(Path.Combine(_dirB, "id.json"));
        var trustA = new TrustStore(Path.Combine(_dirA, "trust.json"));
        var trustB = new TrustStore(Path.Combine(_dirB, "trust.json"));
        var blobA = Path.Combine(_dirA, "blobs");
        var blobB = Path.Combine(_dirB, "blobs");

        using var a = new SyncService(idA, trustA, blobA);
        using var b = new SyncService(idB, trustB, blobB);
        a.Start(); b.Start();

        await WaitUntil(() => a.DiscoveredPeers.Any(p => p.DeviceId == idB.DeviceId)
                          && b.DiscoveredPeers.Any(p => p.DeviceId == idA.DeviceId), 6000);

        var pin = b.EnterPairingMode();
        var peerB = a.DiscoveredPeers.First(p => p.DeviceId == idB.DeviceId);
        Assert.True(await a.PairWithAsync(peerB, pin));

        // sender-side blob
        Directory.CreateDirectory(blobA);
        var senderBlob = Path.Combine(blobA, "src.png");
        var pngBytes = new byte[] { 9, 8, 7, 6, 5, 4 };
        File.WriteAllBytes(senderBlob, pngBytes);

        ClipItem? received = null;
        b.RemoteClipReceived += item => received = item;

        await a.BroadcastAsync(new ClipItem
        {
            Type = ClipType.Image, BlobPath = senderBlob, Preview = "[image]", Hash = "img1"
        });
        await WaitUntil(() => received is not null, 5000);

        Assert.NotNull(received);
        Assert.Equal(ClipType.Image, received!.Type);
        Assert.NotNull(received.BlobPath);
        Assert.StartsWith(Path.GetFullPath(blobB), Path.GetFullPath(received.BlobPath!)); // local to receiver
        Assert.NotEqual(Path.GetFullPath(senderBlob), Path.GetFullPath(received.BlobPath!));
        Assert.Equal(pngBytes, File.ReadAllBytes(received.BlobPath!));
    }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter SyncServiceLoopbackTests`
Expected: FAIL — `SyncService` ctor has no `blobDir` parameter.

- [ ] **Step 3: Implement the SyncService changes**

In `src/ClipVault.Sync/SyncService.cs`:

Add a field and extend the constructor:
```csharp
    private readonly string _blobDir;

    public SyncService(DeviceIdentity identity, TrustStore trust, string blobDir)
    {
        _identity = identity;
        _trust = trust;
        _blobDir = blobDir;
        Directory.CreateDirectory(_blobDir);
        _listener = new TcpListener(IPAddress.Any, 0);
        _listener.Start();
        _server = null!; // (only if a _server field exists; otherwise omit this line)
    }
```
> Note: the current `SyncService` constructor already assigns `_identity`,
> `_trust`, and `_listener`. Replace that constructor body with the version above
> **minus** the `_server` line (there is no `_server` field — the listener is
> owned directly). Keep the rest of the class unchanged except the two methods
> below.

Replace `BroadcastAsync` so it attaches image bytes:
```csharp
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
```

Replace `HandleData` so it materializes received image bytes:
```csharp
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
    }
```

- [ ] **Step 4: Update the desktop host to pass the blob dir**

In `src/ClipVault.App/AppServices.cs`, change the SyncService construction:
```csharp
        Sync = new SyncService(identity, trust, AppPaths.BlobDir);
```

- [ ] **Step 5: Build, then run the loopback tests**

Run: `dotnet build`
Expected: Build succeeded.

Run: `dotnet test --filter SyncServiceLoopbackTests`
Expected: PASS (2 tests — text and image).

- [ ] **Step 6: Commit**

```bash
git add src/ClipVault.Sync/SyncService.cs src/ClipVault.App/AppServices.cs tests/ClipVault.Core.Tests/SyncServiceLoopbackTests.cs
git commit -m "feat(sync): transmit and materialize image bytes across devices"
```

---

## Task 3: Full regression

- [ ] **Step 1: Run the whole suite**

Run: `dotnet test`
Expected: all pass (38 tests: previous 37 + image round-trip; loopback now has 2).

- [ ] **Step 2: Build the whole solution**

Run: `dotnet build`
Expected: Build succeeded.

- [ ] **Step 3: Commit (if any incidental changes)**

```bash
git add -A
git commit -m "test: confirm full suite green after image-bytes sync" --allow-empty
```

---

## Self-Review Notes

- **Spec coverage:** `ImageBytes` on `SyncMessage` (Task 1); `blobDir` ctor param,
  `BroadcastAsync` attaches bytes, receive materializes to local blob + rewrites
  `BlobPath` (Task 2); `AppServices` passes `AppPaths.BlobDir` (Task 2 Step 4);
  loopback image test asserting local blob + byte equality (Task 2). Text/files
  unchanged. Error handling: send-without-bytes on missing file, deliver-without-
  blob on write failure (both in Task 2 code).
- **Placeholder scan:** none. The `_server` caveat in Task 2 Step 3 is an explicit
  instruction (there is no `_server` field), not a placeholder.
- **Type consistency:** `SyncMessage.ForClip(item)` and
  `ForClip(item, byte[]?)`, `SyncMessage.ImageBytes`, `SyncService(DeviceIdentity,
  TrustStore, string)`, `ClipItem.BlobPath`, `ClipType.Image`,
  `RemoteClipReceived` all match existing definitions.
```
