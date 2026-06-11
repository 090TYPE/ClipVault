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
