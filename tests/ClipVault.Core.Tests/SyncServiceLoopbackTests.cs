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

        using var a = new SyncService(idA, trustA, Path.Combine(_dirA, "blobs"));
        using var b = new SyncService(idB, trustB, Path.Combine(_dirB, "blobs"));
        a.Start(); b.Start();

        // wait for mutual discovery
        await WaitUntil(() => a.DiscoveredPeers.Any(p => p.DeviceId == idB.DeviceId)
                          && b.DiscoveredPeers.Any(p => p.DeviceId == idA.DeviceId), 6000);

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
        Assert.StartsWith(Path.GetFullPath(blobB), Path.GetFullPath(received.BlobPath!));
        Assert.NotEqual(Path.GetFullPath(senderBlob), Path.GetFullPath(received.BlobPath!));
        Assert.Equal(pngBytes, File.ReadAllBytes(received.BlobPath!));
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
