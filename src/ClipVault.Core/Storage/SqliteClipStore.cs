using ClipVault.Core.Abstractions;
using ClipVault.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace ClipVault.Core.Storage;

public class SqliteClipStore : IClipStore
{
    private readonly Func<ClipDbContext> _factory;
    private readonly RetentionPolicy _retention;

    public SqliteClipStore(Func<ClipDbContext> factory, RetentionPolicy retention)
    {
        _factory = factory;
        _retention = retention;
    }

    public async Task<ClipItem> AddAsync(ClipItem item)
    {
        await using var ctx = _factory();
        var existing = await ctx.Clips
            .Where(c => c.Hash == item.Hash)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync();

        if (existing is not null)
        {
            existing.CreatedAt = DateTime.UtcNow; // bubble to top
            await ctx.SaveChangesAsync();
            return existing;
        }

        ctx.Clips.Add(item);
        await ctx.SaveChangesAsync();
        return item;
    }

    public async Task<IReadOnlyList<ClipItem>> GetRecentAsync(int limit = 200)
    {
        await using var ctx = _factory();
        return await ctx.Clips
            .OrderByDescending(c => c.IsPinned)
            .ThenByDescending(c => c.CreatedAt)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<ClipItem>> SearchAsync(string query, ClipType? type = null)
    {
        await using var ctx = _factory();
        var q = ctx.Clips.AsQueryable();
        if (type is not null) q = q.Where(c => c.Type == type);
        if (!string.IsNullOrWhiteSpace(query))
            q = q.Where(c => (c.TextContent != null && EF.Functions.Like(c.TextContent, $"%{query}%"))
                          || (c.Preview != null && EF.Functions.Like(c.Preview, $"%{query}%")));
        return await q.OrderByDescending(c => c.CreatedAt).Take(200).ToListAsync();
    }

    public async Task SetPinnedAsync(Guid id, bool pinned)
    {
        await using var ctx = _factory();
        var item = await ctx.Clips.FindAsync(id);
        if (item is null) return;
        item.IsPinned = pinned;
        await ctx.SaveChangesAsync();
    }

    public async Task DeleteAsync(Guid id)
    {
        await using var ctx = _factory();
        var item = await ctx.Clips.FindAsync(id);
        if (item is null) return;
        if (item.Type == ClipType.Image && item.BlobPath is not null && File.Exists(item.BlobPath))
            File.Delete(item.BlobPath);
        ctx.Clips.Remove(item);
        await ctx.SaveChangesAsync();
    }

    public async Task ApplyRetentionAsync()
    {
        await using var ctx = _factory();
        var all = await ctx.Clips.ToListAsync();
        var doomed = _retention.SelectForDeletion(all, DateTime.UtcNow);
        foreach (var d in doomed)
        {
            if (d.Type == ClipType.Image && d.BlobPath is not null && File.Exists(d.BlobPath))
                File.Delete(d.BlobPath);
            ctx.Clips.Remove(d);
        }
        await ctx.SaveChangesAsync();
    }
}
