using ClipVault.Core.Models;

namespace ClipVault.Core.Abstractions;

public interface IClipStore
{
    /// Adds an item, deduping against the most recent (same hash → moved to top, returns existing).
    Task<ClipItem> AddAsync(ClipItem item);
    Task<IReadOnlyList<ClipItem>> GetRecentAsync(int limit = 200);
    Task<IReadOnlyList<ClipItem>> SearchAsync(string query, ClipType? type = null);
    Task SetPinnedAsync(Guid id, bool pinned);
    Task DeleteAsync(Guid id);
    Task ApplyRetentionAsync();
}
