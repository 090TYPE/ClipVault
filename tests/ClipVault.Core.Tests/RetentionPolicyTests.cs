using ClipVault.Core.Models;
using ClipVault.Core.Storage;
using Xunit;

namespace ClipVault.Core.Tests;

public class RetentionPolicyTests
{
    private static ClipItem Item(DateTime created, bool pinned = false) =>
        new() { CreatedAt = created, IsPinned = pinned, Type = ClipType.Text };

    [Fact]
    public void KeepsPinned_EvenWhenOldAndOverLimit()
    {
        var policy = new RetentionPolicy(maxUnpinned: 1, maxAge: TimeSpan.FromDays(30));
        var now = DateTime.UtcNow;
        var items = new[] { Item(now.AddDays(-100), pinned: true) };
        Assert.Empty(policy.SelectForDeletion(items, now));
    }

    [Fact]
    public void DeletesUnpinnedOverCountLimit_OldestFirst()
    {
        var policy = new RetentionPolicy(maxUnpinned: 1, maxAge: TimeSpan.FromDays(30));
        var now = DateTime.UtcNow;
        var older = Item(now.AddMinutes(-10));
        var newer = Item(now);
        var toDelete = policy.SelectForDeletion(new[] { newer, older }, now);
        Assert.Contains(older, toDelete);
        Assert.DoesNotContain(newer, toDelete);
    }

    [Fact]
    public void DeletesUnpinnedOverAge()
    {
        var policy = new RetentionPolicy(maxUnpinned: 500, maxAge: TimeSpan.FromDays(30));
        var now = DateTime.UtcNow;
        var old = Item(now.AddDays(-40));
        var toDelete = policy.SelectForDeletion(new[] { old }, now);
        Assert.Contains(old, toDelete);
    }
}
