using ClipVault.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace ClipVault.Core.Storage;

public class ClipDbContext : DbContext
{
    public DbSet<ClipItem> Clips => Set<ClipItem>();

    private readonly string _dbPath;

    public ClipDbContext(string dbPath) => _dbPath = dbPath;

    protected override void OnConfiguring(DbContextOptionsBuilder options) =>
        options.UseSqlite($"Data Source={_dbPath}");

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<ClipItem>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Hash);
            e.HasIndex(x => x.CreatedAt);
        });
    }
}
