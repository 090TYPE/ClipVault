using ClipVault.Core.Models;

namespace ClipVault.Core.Storage;

public class RetentionPolicy
{
    private readonly int _maxUnpinned;
    private readonly TimeSpan _maxAge;

    public RetentionPolicy(int maxUnpinned = 500, TimeSpan? maxAge = null)
    {
        _maxUnpinned = maxUnpinned;
        _maxAge = maxAge ?? TimeSpan.FromDays(30);
    }

    public IReadOnlyList<ClipItem> SelectForDeletion(IEnumerable<ClipItem> items, DateTime now)
    {
        var unpinned = items.Where(i => !i.IsPinned)
                            .OrderByDescending(i => i.CreatedAt)
                            .ToList();

        var delete = new HashSet<ClipItem>();

        // Over age
        foreach (var i in unpinned.Where(i => now - i.CreatedAt > _maxAge))
            delete.Add(i);

        // Over count (keep newest _maxUnpinned)
        foreach (var i in unpinned.Skip(_maxUnpinned))
            delete.Add(i);

        return delete.ToList();
    }
}
