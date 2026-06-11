using ClipVault.Core.Models;
using ClipVault.Core.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace ClipVault.Core.Tests;

public class SqliteClipStoreTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"cv-{Guid.NewGuid():N}.db");

    private SqliteClipStore NewStore()
    {
        var ctx = new ClipDbContext(_dbPath);
        ctx.Database.EnsureCreated();
        ctx.Dispose();
        return new SqliteClipStore(() => new ClipDbContext(_dbPath),
                                   new RetentionPolicy(maxUnpinned: 2, maxAge: TimeSpan.FromDays(30)));
    }

    private static ClipItem Text(string s) =>
        new() { Type = ClipType.Text, TextContent = s, Preview = s, Hash = s };

    [Fact]
    public async Task Add_PersistsItem()
    {
        var store = NewStore();
        await store.AddAsync(Text("a"));
        var recent = await store.GetRecentAsync();
        Assert.Single(recent);
    }

    [Fact]
    public async Task Add_DuplicateHash_DoesNotCreateSecondRow()
    {
        var store = NewStore();
        await store.AddAsync(Text("dup"));
        await store.AddAsync(Text("dup"));
        var recent = await store.GetRecentAsync();
        Assert.Single(recent);
    }

    [Fact]
    public async Task Search_FindsByTextContent()
    {
        var store = NewStore();
        await store.AddAsync(Text("hello world"));
        await store.AddAsync(Text("goodbye"));
        var hits = await store.SearchAsync("hello");
        Assert.Single(hits);
        Assert.Equal("hello world", hits[0].TextContent);
    }

    [Fact]
    public async Task SetPinned_KeepsItemThroughRetention()
    {
        var store = NewStore();
        var pinned = Text("keep"); pinned.IsPinned = true;
        await store.AddAsync(pinned);
        await store.AddAsync(Text("1"));
        await store.AddAsync(Text("2"));
        await store.AddAsync(Text("3"));
        await store.ApplyRetentionAsync();
        var recent = await store.GetRecentAsync();
        Assert.Contains(recent, i => i.TextContent == "keep");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools(); // release file handles held by the pool
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }
}
