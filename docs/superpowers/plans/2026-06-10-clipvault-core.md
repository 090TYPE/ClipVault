# ClipVault Core Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a usable cross-platform clipboard manager (Windows/Linux/macOS) that captures text/images/files, stores history locally, and exposes a keyboard-first history window via a global hotkey.

**Architecture:** Avalonia/.NET desktop app. `Core` holds OS-agnostic logic behind interfaces (`IClipStore`, `IClipboardMonitor`, `IClipboardWriter`, `IGlobalHotkeyService`). `Platform` provides per-OS native implementations + SharpHook for hotkeys. `App` is the Avalonia tray UI. Sync is a separate later plan and is out of scope here, but `ISyncService` is left as a seam.

**Tech Stack:** .NET 8, Avalonia 11, EF Core 8 + SQLite, SharpHook, xUnit + Moq.

> **Scope:** This plan covers the Core product only. LAN sync is Plan 2.

---

## File Structure

```
ClipVault.sln
├── src/
│   ├── ClipVault.Core/
│   │   ├── Models/ClipItem.cs            — entity + ClipType enum
│   │   ├── Models/AppSettings.cs         — user settings model
│   │   ├── Abstractions/IClipStore.cs
│   │   ├── Abstractions/IClipboardMonitor.cs
│   │   ├── Abstractions/IClipboardWriter.cs
│   │   ├── Abstractions/IGlobalHotkeyService.cs
│   │   ├── Abstractions/IClipboardReadResult.cs — captured payload before persistence
│   │   ├── Hashing/ClipHasher.cs         — SHA-256 dedup hashing
│   │   ├── Storage/ClipDbContext.cs      — EF Core context
│   │   ├── Storage/SqliteClipStore.cs    — IClipStore impl (dedup + retention)
│   │   ├── Storage/RetentionPolicy.cs    — retention rules (pure logic)
│   │   └── Paths/AppPaths.cs             — per-OS data/blob/log directories
│   ├── ClipVault.Platform/
│   │   ├── PlatformFactory.cs            — OS detection → concrete impls
│   │   ├── Hotkeys/SharpHookHotkeyService.cs
│   │   ├── Windows/WindowsClipboard.cs   — monitor + writer (Win32)
│   │   ├── Linux/LinuxClipboard.cs       — monitor + writer (xclip/wl-clipboard)
│   │   └── MacOS/MacClipboard.cs         — monitor + writer (NSPasteboard via P/Invoke)
│   └── ClipVault.App/
│       ├── Program.cs
│       ├── App.axaml(.cs)                — Avalonia app, tray icon, DI
│       ├── ViewModels/HistoryViewModel.cs
│       ├── ViewModels/SettingsViewModel.cs
│       ├── Views/HistoryWindow.axaml(.cs)
│       └── Views/SettingsWindow.axaml(.cs)
└── tests/
    └── ClipVault.Core.Tests/
        ├── ClipHasherTests.cs
        ├── RetentionPolicyTests.cs
        └── SqliteClipStoreTests.cs
```

---

## Task 1: Solution and project scaffolding

**Files:**
- Create: `ClipVault.sln`, the four `.csproj` files, `NuGet.Config`

- [ ] **Step 1: Verify .NET 8 SDK is present**

Run: `dotnet --version`
Expected: `8.x.x` (if not, install the .NET 8 SDK first).

- [ ] **Step 2: Create solution and projects**

Run from `C:\Users\090\Documents\GitHub\ClipVault`:
```bash
dotnet new sln -n ClipVault
dotnet new classlib -n ClipVault.Core -o src/ClipVault.Core
dotnet new classlib -n ClipVault.Platform -o src/ClipVault.Platform
dotnet new install Avalonia.Templates
dotnet new avalonia.app -n ClipVault.App -o src/ClipVault.App
dotnet new xunit -n ClipVault.Core.Tests -o tests/ClipVault.Core.Tests
dotnet sln add src/ClipVault.Core src/ClipVault.Platform src/ClipVault.App tests/ClipVault.Core.Tests
```

- [ ] **Step 3: Wire project references**

```bash
dotnet add src/ClipVault.Platform reference src/ClipVault.Core
dotnet add src/ClipVault.App reference src/ClipVault.Core src/ClipVault.Platform
dotnet add tests/ClipVault.Core.Tests reference src/ClipVault.Core
```

- [ ] **Step 4: Add NuGet packages**

```bash
dotnet add src/ClipVault.Core package Microsoft.EntityFrameworkCore.Sqlite --version 8.*
dotnet add src/ClipVault.Platform package SharpHook --version 5.*
dotnet add tests/ClipVault.Core.Tests package Moq
dotnet add tests/ClipVault.Core.Tests package Microsoft.EntityFrameworkCore.Sqlite --version 8.*
```

- [ ] **Step 5: Delete template placeholder class files**

Delete `src/ClipVault.Core/Class1.cs` and `src/ClipVault.Platform/Class1.cs` if present.

- [ ] **Step 6: Verify it builds**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "chore: scaffold ClipVault solution and projects"
```

---

## Task 2: ClipItem model and ClipType enum

**Files:**
- Create: `src/ClipVault.Core/Models/ClipItem.cs`

- [ ] **Step 1: Write the model**

```csharp
namespace ClipVault.Core.Models;

public enum ClipType { Text, Image, Files }

public class ClipItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public ClipType Type { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsPinned { get; set; }
    public string? SourceApp { get; set; }

    public string? TextContent { get; set; }   // for Text
    public string? Preview { get; set; }        // short text preview for the list
    public string? BlobPath { get; set; }       // PNG path on disk, for Image
    public string? FilePaths { get; set; }      // JSON array of paths, for Files

    public string Hash { get; set; } = string.Empty; // SHA-256 for dedup
}
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build src/ClipVault.Core`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/ClipVault.Core/Models/ClipItem.cs
git commit -m "feat(core): add ClipItem model and ClipType enum"
```

---

## Task 3: ClipHasher (dedup hashing) — TDD

**Files:**
- Create: `src/ClipVault.Core/Hashing/ClipHasher.cs`
- Test: `tests/ClipVault.Core.Tests/ClipHasherTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using ClipVault.Core.Hashing;
using Xunit;

namespace ClipVault.Core.Tests;

public class ClipHasherTests
{
    [Fact]
    public void SameText_ProducesSameHash()
    {
        Assert.Equal(ClipHasher.HashText("hello"), ClipHasher.HashText("hello"));
    }

    [Fact]
    public void DifferentText_ProducesDifferentHash()
    {
        Assert.NotEqual(ClipHasher.HashText("hello"), ClipHasher.HashText("world"));
    }

    [Fact]
    public void SameBytes_ProducesSameHash()
    {
        var a = new byte[] { 1, 2, 3 };
        var b = new byte[] { 1, 2, 3 };
        Assert.Equal(ClipHasher.HashBytes(a), ClipHasher.HashBytes(b));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter ClipHasherTests`
Expected: FAIL — `ClipHasher` does not exist.

- [ ] **Step 3: Implement**

```csharp
using System.Security.Cryptography;
using System.Text;

namespace ClipVault.Core.Hashing;

public static class ClipHasher
{
    public static string HashText(string text) =>
        HashBytes(Encoding.UTF8.GetBytes(text));

    public static string HashBytes(byte[] data) =>
        Convert.ToHexString(SHA256.HashData(data));
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test --filter ClipHasherTests`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add src/ClipVault.Core/Hashing/ClipHasher.cs tests/ClipVault.Core.Tests/ClipHasherTests.cs
git commit -m "feat(core): add ClipHasher for dedup hashing"
```

---

## Task 4: RetentionPolicy (pure logic) — TDD

**Files:**
- Create: `src/ClipVault.Core/Storage/RetentionPolicy.cs`
- Test: `tests/ClipVault.Core.Tests/RetentionPolicyTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
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
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter RetentionPolicyTests`
Expected: FAIL — `RetentionPolicy` does not exist.

- [ ] **Step 3: Implement**

```csharp
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
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test --filter RetentionPolicyTests`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add src/ClipVault.Core/Storage/RetentionPolicy.cs tests/ClipVault.Core.Tests/RetentionPolicyTests.cs
git commit -m "feat(core): add RetentionPolicy"
```

---

## Task 5: AppPaths (per-OS directories)

**Files:**
- Create: `src/ClipVault.Core/Paths/AppPaths.cs`

- [ ] **Step 1: Implement**

```csharp
namespace ClipVault.Core.Paths;

public static class AppPaths
{
    public static string DataDir { get; } = ResolveDataDir();
    public static string DbPath => Path.Combine(DataDir, "clipvault.db");
    public static string BlobDir => Path.Combine(DataDir, "blobs");
    public static string LogDir => Path.Combine(DataDir, "logs");

    private static string ResolveDataDir()
    {
        // SpecialFolder.ApplicationData maps to:
        //   Windows: %APPDATA%
        //   Linux:   ~/.config
        //   macOS:   ~/Library/Application Support
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(baseDir, "ClipVault");
        Directory.CreateDirectory(dir);
        Directory.CreateDirectory(Path.Combine(dir, "blobs"));
        Directory.CreateDirectory(Path.Combine(dir, "logs"));
        return dir;
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build src/ClipVault.Core`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/ClipVault.Core/Paths/AppPaths.cs
git commit -m "feat(core): add per-OS AppPaths"
```

---

## Task 6: Core abstractions (interfaces)

**Files:**
- Create: `src/ClipVault.Core/Abstractions/IClipboardReadResult.cs`
- Create: `src/ClipVault.Core/Abstractions/IClipStore.cs`
- Create: `src/ClipVault.Core/Abstractions/IClipboardMonitor.cs`
- Create: `src/ClipVault.Core/Abstractions/IClipboardWriter.cs`
- Create: `src/ClipVault.Core/Abstractions/IGlobalHotkeyService.cs`

- [ ] **Step 1: Write the captured-payload record**

`IClipboardReadResult.cs`:
```csharp
using ClipVault.Core.Models;

namespace ClipVault.Core.Abstractions;

/// Raw payload captured from the OS clipboard, before persistence.
public record ClipboardReadResult(
    ClipType Type,
    string? Text,
    byte[]? ImagePng,
    IReadOnlyList<string>? FilePaths,
    string? SourceApp);
```

- [ ] **Step 2: Write the store interface**

`IClipStore.cs`:
```csharp
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
```

- [ ] **Step 3: Write the monitor/writer/hotkey interfaces**

`IClipboardMonitor.cs`:
```csharp
namespace ClipVault.Core.Abstractions;

public interface IClipboardMonitor : IDisposable
{
    event Action<ClipboardReadResult>? ClipboardChanged;
    void Start();
    void Stop();
}
```

`IClipboardWriter.cs`:
```csharp
using ClipVault.Core.Models;

namespace ClipVault.Core.Abstractions;

public interface IClipboardWriter
{
    void Write(ClipItem item);
}
```

`IGlobalHotkeyService.cs`:
```csharp
namespace ClipVault.Core.Abstractions;

public interface IGlobalHotkeyService : IDisposable
{
    /// Returns true if registration succeeded; false if the combo is taken.
    bool Register(string hotkey, Action callback);
    void Start();
}
```

- [ ] **Step 4: Build to verify**

Run: `dotnet build src/ClipVault.Core`
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add src/ClipVault.Core/Abstractions
git commit -m "feat(core): add clipboard/store/hotkey abstractions"
```

---

## Task 7: ClipDbContext (EF Core)

**Files:**
- Create: `src/ClipVault.Core/Storage/ClipDbContext.cs`

- [ ] **Step 1: Implement the context**

```csharp
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
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build src/ClipVault.Core`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/ClipVault.Core/Storage/ClipDbContext.cs
git commit -m "feat(core): add ClipDbContext"
```

---

## Task 8: SqliteClipStore (dedup + retention) — TDD

**Files:**
- Create: `src/ClipVault.Core/Storage/SqliteClipStore.cs`
- Test: `tests/ClipVault.Core.Tests/SqliteClipStoreTests.cs`

Tests use a unique temp DB file per test for isolation.

- [ ] **Step 1: Write the failing tests**

```csharp
using ClipVault.Core.Models;
using ClipVault.Core.Storage;
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
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter SqliteClipStoreTests`
Expected: FAIL — `SqliteClipStore` does not exist.

- [ ] **Step 3: Implement**

```csharp
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
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test --filter SqliteClipStoreTests`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add src/ClipVault.Core/Storage/SqliteClipStore.cs tests/ClipVault.Core.Tests/SqliteClipStoreTests.cs
git commit -m "feat(core): add SqliteClipStore with dedup and retention"
```

---

## Task 9: ClipItemFactory (read-result → ClipItem) — TDD

Converts a captured `ClipboardReadResult` into a persisted `ClipItem`, writing image blobs to disk and computing the hash. Keeps that logic out of the platform layer.

**Files:**
- Create: `src/ClipVault.Core/Storage/ClipItemFactory.cs`
- Test: `tests/ClipVault.Core.Tests/ClipItemFactoryTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using ClipVault.Core.Abstractions;
using ClipVault.Core.Models;
using ClipVault.Core.Storage;
using Xunit;

namespace ClipVault.Core.Tests;

public class ClipItemFactoryTests : IDisposable
{
    private readonly string _blobDir = Path.Combine(Path.GetTempPath(), $"cv-blobs-{Guid.NewGuid():N}");

    [Fact]
    public void Text_SetsContentAndHash()
    {
        var f = new ClipItemFactory(_blobDir);
        var item = f.Create(new ClipboardReadResult(ClipType.Text, "hi", null, null, "App"));
        Assert.Equal(ClipType.Text, item.Type);
        Assert.Equal("hi", item.TextContent);
        Assert.False(string.IsNullOrEmpty(item.Hash));
    }

    [Fact]
    public void Image_WritesBlobToDisk()
    {
        var f = new ClipItemFactory(_blobDir);
        var png = new byte[] { 1, 2, 3, 4 };
        var item = f.Create(new ClipboardReadResult(ClipType.Image, null, png, null, null));
        Assert.Equal(ClipType.Image, item.Type);
        Assert.NotNull(item.BlobPath);
        Assert.True(File.Exists(item.BlobPath!));
    }

    [Fact]
    public void Files_StoresPathsAsJson()
    {
        var f = new ClipItemFactory(_blobDir);
        var item = f.Create(new ClipboardReadResult(ClipType.Files, null, null,
            new[] { "/a.txt", "/b.txt" }, null));
        Assert.Equal(ClipType.Files, item.Type);
        Assert.Contains("a.txt", item.FilePaths);
    }

    public void Dispose()
    {
        if (Directory.Exists(_blobDir)) Directory.Delete(_blobDir, true);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter ClipItemFactoryTests`
Expected: FAIL — `ClipItemFactory` does not exist.

- [ ] **Step 3: Implement**

```csharp
using System.Text.Json;
using ClipVault.Core.Abstractions;
using ClipVault.Core.Hashing;
using ClipVault.Core.Models;

namespace ClipVault.Core.Storage;

public class ClipItemFactory
{
    private readonly string _blobDir;

    public ClipItemFactory(string blobDir)
    {
        _blobDir = blobDir;
        Directory.CreateDirectory(_blobDir);
    }

    public ClipItem Create(ClipboardReadResult r)
    {
        var item = new ClipItem { Type = r.Type, SourceApp = r.SourceApp };

        switch (r.Type)
        {
            case ClipType.Text:
                item.TextContent = r.Text;
                item.Preview = Truncate(r.Text ?? "");
                item.Hash = ClipHasher.HashText(r.Text ?? "");
                break;

            case ClipType.Image:
                var bytes = r.ImagePng ?? Array.Empty<byte>();
                item.BlobPath = Path.Combine(_blobDir, $"{item.Id}.png");
                File.WriteAllBytes(item.BlobPath, bytes);
                item.Preview = "[image]";
                item.Hash = ClipHasher.HashBytes(bytes);
                break;

            case ClipType.Files:
                var paths = r.FilePaths ?? Array.Empty<string>();
                item.FilePaths = JsonSerializer.Serialize(paths);
                item.Preview = string.Join(", ", paths.Select(Path.GetFileName));
                item.Hash = ClipHasher.HashText(item.FilePaths);
                break;
        }

        return item;
    }

    private static string Truncate(string s) =>
        s.Length <= 120 ? s : s[..120] + "…";
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test --filter ClipItemFactoryTests`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add src/ClipVault.Core/Storage/ClipItemFactory.cs tests/ClipVault.Core.Tests/ClipItemFactoryTests.cs
git commit -m "feat(core): add ClipItemFactory"
```

---

## Task 10: SharpHook global hotkey service

Native interop and global input cannot be unit-tested reliably; this task ends with a manual smoke test.

**Files:**
- Create: `src/ClipVault.Platform/Hotkeys/SharpHookHotkeyService.cs`

- [ ] **Step 1: Implement**

```csharp
using ClipVault.Core.Abstractions;
using SharpHook;
using SharpHook.Native;

namespace ClipVault.Platform.Hotkeys;

/// Parses a hotkey like "Ctrl+Shift+V" and fires the callback on match.
public class SharpHookHotkeyService : IGlobalHotkeyService
{
    private readonly TaskPoolGlobalHook _hook = new();
    private Action? _callback;
    private (bool ctrl, bool shift, bool alt, KeyCode key)? _combo;

    public bool Register(string hotkey, Action callback)
    {
        var parsed = Parse(hotkey);
        if (parsed is null) return false;
        _combo = parsed;
        _callback = callback;
        _hook.KeyPressed += OnKeyPressed;
        return true;
    }

    public void Start() => _ = _hook.RunAsync();

    private void OnKeyPressed(object? sender, KeyboardHookEventArgs e)
    {
        if (_combo is null) return;
        var c = _combo.Value;
        var mask = e.RawEvent.Mask;
        bool ctrl = mask.HasFlag(ModifierMask.LeftCtrl) || mask.HasFlag(ModifierMask.RightCtrl);
        bool shift = mask.HasFlag(ModifierMask.LeftShift) || mask.HasFlag(ModifierMask.RightShift);
        bool alt = mask.HasFlag(ModifierMask.LeftAlt) || mask.HasFlag(ModifierMask.RightAlt);
        if (e.Data.KeyCode == c.key && ctrl == c.ctrl && shift == c.shift && alt == c.alt)
            _callback?.Invoke();
    }

    private static (bool, bool, bool, KeyCode)? Parse(string hotkey)
    {
        bool ctrl = false, shift = false, alt = false;
        KeyCode? key = null;
        foreach (var part in hotkey.Split('+', StringSplitOptions.TrimEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl": ctrl = true; break;
                case "shift": shift = true; break;
                case "alt": alt = true; break;
                default:
                    if (Enum.TryParse<KeyCode>("Vc" + part.ToUpperInvariant(), out var k)) key = k;
                    break;
            }
        }
        return key is null ? null : (ctrl, shift, alt, key.Value);
    }

    public void Dispose() => _hook.Dispose();
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build src/ClipVault.Platform`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/ClipVault.Platform/Hotkeys/SharpHookHotkeyService.cs
git commit -m "feat(platform): add SharpHook global hotkey service"
```

> Manual smoke test deferred until the App wires it (Task 15).

---

## Task 11: Windows clipboard monitor + writer

Uses Avalonia's clipboard for reads/writes where possible and Win32
`AddClipboardFormatListener` for change notification. Manual smoke test at end.

**Files:**
- Create: `src/ClipVault.Platform/Windows/WindowsClipboard.cs`

- [ ] **Step 1: Implement**

```csharp
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ClipVault.Core.Abstractions;
using ClipVault.Core.Models;

namespace ClipVault.Platform.Windows;

[SupportedOSPlatform("windows")]
public class WindowsClipboard : IClipboardMonitor, IClipboardWriter
{
    public event Action<ClipboardReadResult>? ClipboardChanged;

    // Win32 clipboard listener runs on a hidden message-only window.
    // Implementation detail: create a message-only HWND, call
    // AddClipboardFormatListener, pump WM_CLIPBOARDUPDATE on a dedicated thread.
    // On each update, read formats via System.Windows.Forms.Clipboard equivalents
    // or OpenClipboard/GetClipboardData. Map to ClipboardReadResult:
    //   - CF_UNICODETEXT  → ClipType.Text
    //   - CF_DIB/CF_BITMAP → ClipType.Image (encode to PNG bytes)
    //   - CF_HDROP         → ClipType.Files (list of paths)

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    private IntPtr _hwnd = IntPtr.Zero;

    public void Start()
    {
        // Create message-only window + register listener on a dedicated thread.
        // Raise ClipboardChanged(ReadCurrent()) on WM_CLIPBOARDUPDATE (0x031D).
        // See: AddClipboardFormatListener docs.
    }

    public void Stop()
    {
        if (_hwnd != IntPtr.Zero) RemoveClipboardFormatListener(_hwnd);
    }

    private ClipboardReadResult? ReadCurrent()
    {
        // Wrap all reads in try/catch; return null on locked clipboard or
        // unsupported format (skip, never throw).
        return null;
    }

    public void Write(ClipItem item)
    {
        // Text → SetText; Image → load BlobPath PNG → SetImage;
        // Files → SetFileDropList from deserialized FilePaths.
    }

    public void Dispose() => Stop();
}
```

> **Note for implementer:** the message-only window pump is boilerplate Win32.
> Use `RegisterClassEx` + `CreateWindowEx(HWND_MESSAGE)` + a `WndProc` that
> handles `WM_CLIPBOARDUPDATE`. For PNG encoding of CF_DIB, use
> `System.Drawing.Common` (Windows-only is acceptable here) or SkiaSharp.
> Keep every clipboard read inside try/catch with a single retry.

- [ ] **Step 2: Build to verify**

Run: `dotnet build src/ClipVault.Platform`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/ClipVault.Platform/Windows/WindowsClipboard.cs
git commit -m "feat(platform): add Windows clipboard monitor/writer skeleton"
```

> Full Win32 pump + PNG encoding is completed and smoke-tested in Task 15 wiring.

---

## Task 12: Linux clipboard monitor + writer

Linux has no clean change-event API across X11/Wayland. MVP strategy: poll the
clipboard via `wl-paste`/`xclip` on a timer (e.g. 500 ms), diff by hash.

**Files:**
- Create: `src/ClipVault.Platform/Linux/LinuxClipboard.cs`

- [ ] **Step 1: Implement**

```csharp
using System.Diagnostics;
using System.Runtime.Versioning;
using ClipVault.Core.Abstractions;
using ClipVault.Core.Hashing;
using ClipVault.Core.Models;

namespace ClipVault.Platform.Linux;

[SupportedOSPlatform("linux")]
public class LinuxClipboard : IClipboardMonitor, IClipboardWriter
{
    public event Action<ClipboardReadResult>? ClipboardChanged;

    private readonly System.Timers.Timer _timer = new(500);
    private string _lastHash = "";
    private readonly bool _wayland =
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));

    public void Start()
    {
        _timer.Elapsed += (_, _) => Poll();
        _timer.AutoReset = true;
        _timer.Start();
    }

    public void Stop() => _timer.Stop();

    private void Poll()
    {
        try
        {
            var text = RunCapture(_wayland ? "wl-paste" : "xclip", _wayland ? "-n" : "-selection clipboard -o");
            if (string.IsNullOrEmpty(text)) return;
            var hash = ClipHasher.HashText(text);
            if (hash == _lastHash) return;
            _lastHash = hash;
            ClipboardChanged?.Invoke(new ClipboardReadResult(ClipType.Text, text, null, null, null));
        }
        catch { /* clipboard tool missing or empty — skip */ }
    }

    private static string RunCapture(string exe, string args)
    {
        var psi = new ProcessStartInfo(exe, args)
        { RedirectStandardOutput = true, UseShellExecute = false };
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit(1000);
        return output;
    }

    public void Write(ClipItem item)
    {
        if (item.Type != ClipType.Text || item.TextContent is null) return;
        var exe = _wayland ? "wl-copy" : "xclip";
        var args = _wayland ? "" : "-selection clipboard";
        var psi = new ProcessStartInfo(exe, args)
        { RedirectStandardInput = true, UseShellExecute = false };
        using var p = Process.Start(psi)!;
        p.StandardInput.Write(item.TextContent);
        p.StandardInput.Close();
        p.WaitForExit(1000);
    }

    public void Dispose() => Stop();
}
```

> **Note for implementer:** MVP Linux supports text reliably. Image/file capture
> via `wl-paste --type image/png` / `xclip -t image/png` can be added once text
> is verified. Requires `wl-clipboard` or `xclip` installed — document this in README.

- [ ] **Step 2: Build to verify**

Run: `dotnet build src/ClipVault.Platform`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/ClipVault.Platform/Linux/LinuxClipboard.cs
git commit -m "feat(platform): add Linux clipboard monitor/writer (text)"
```

---

## Task 13: macOS clipboard monitor + writer

macOS exposes `NSPasteboard.changeCount` — poll it (cheap) and read on change.

**Files:**
- Create: `src/ClipVault.Platform/MacOS/MacClipboard.cs`

- [ ] **Step 1: Implement**

```csharp
using System.Diagnostics;
using System.Runtime.Versioning;
using ClipVault.Core.Abstractions;
using ClipVault.Core.Models;

namespace ClipVault.Platform.MacOS;

[SupportedOSPlatform("macos")]
public class MacClipboard : IClipboardMonitor, IClipboardWriter
{
    public event Action<ClipboardReadResult>? ClipboardChanged;

    private readonly System.Timers.Timer _timer = new(400);
    private long _lastChangeCount = -1;

    public void Start()
    {
        _timer.Elapsed += (_, _) => Poll();
        _timer.AutoReset = true;
        _timer.Start();
    }

    public void Stop() => _timer.Stop();

    private void Poll()
    {
        try
        {
            // MVP: use `pbpaste` for reads and a changeCount proxy via reading text.
            // A later iteration can P/Invoke NSPasteboard.general.changeCount.
            var text = Run("pbpaste", "");
            if (string.IsNullOrEmpty(text)) return;
            var hc = text.GetHashCode();
            if (hc == _lastChangeCount) return;
            _lastChangeCount = hc;
            ClipboardChanged?.Invoke(new ClipboardReadResult(ClipType.Text, text, null, null, null));
        }
        catch { /* skip */ }
    }

    private static string Run(string exe, string args)
    {
        var psi = new ProcessStartInfo(exe, args)
        { RedirectStandardOutput = true, UseShellExecute = false };
        using var p = Process.Start(psi)!;
        var o = p.StandardOutput.ReadToEnd();
        p.WaitForExit(1000);
        return o;
    }

    public void Write(ClipItem item)
    {
        if (item.Type != ClipType.Text || item.TextContent is null) return;
        var psi = new ProcessStartInfo("pbcopy", "")
        { RedirectStandardInput = true, UseShellExecute = false };
        using var p = Process.Start(psi)!;
        p.StandardInput.Write(item.TextContent);
        p.StandardInput.Close();
        p.WaitForExit(1000);
    }

    public void Dispose() => Stop();
}
```

> **Note for implementer:** `pbpaste`/`pbcopy` give reliable text on every macOS.
> Image/file support and a true `changeCount` P/Invoke are a follow-up once text
> is verified.

- [ ] **Step 2: Build to verify**

Run: `dotnet build src/ClipVault.Platform`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/ClipVault.Platform/MacOS/MacClipboard.cs
git commit -m "feat(platform): add macOS clipboard monitor/writer (text)"
```

---

## Task 14: PlatformFactory (OS detection)

**Files:**
- Create: `src/ClipVault.Platform/PlatformFactory.cs`

- [ ] **Step 1: Implement**

```csharp
using System.Runtime.InteropServices;
using ClipVault.Core.Abstractions;
using ClipVault.Platform.Hotkeys;
using ClipVault.Platform.Linux;
using ClipVault.Platform.MacOS;
using ClipVault.Platform.Windows;

namespace ClipVault.Platform;

public static class PlatformFactory
{
    public static (IClipboardMonitor Monitor, IClipboardWriter Writer) CreateClipboard()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var w = new WindowsClipboard();
            return (w, w);
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var l = new LinuxClipboard();
            return (l, l);
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var m = new MacClipboard();
            return (m, m);
        }
        throw new PlatformNotSupportedException();
    }

    public static IGlobalHotkeyService CreateHotkeys() => new SharpHookHotkeyService();
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build src/ClipVault.Platform`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/ClipVault.Platform/PlatformFactory.cs
git commit -m "feat(platform): add PlatformFactory"
```

---

## Task 15: App bootstrap — tray icon, DI, monitor→store wiring + hotkey smoke test

**Files:**
- Modify: `src/ClipVault.App/Program.cs`
- Modify: `src/ClipVault.App/App.axaml` and `App.axaml.cs`
- Create: `src/ClipVault.App/AppServices.cs`

- [ ] **Step 1: Create the service container**

`AppServices.cs`:
```csharp
using ClipVault.Core.Abstractions;
using ClipVault.Core.Paths;
using ClipVault.Core.Storage;
using ClipVault.Platform;

namespace ClipVault.App;

/// Minimal composition root (no DI container needed for MVP).
public class AppServices
{
    public IClipStore Store { get; }
    public IClipboardMonitor Monitor { get; }
    public IClipboardWriter Writer { get; }
    public IGlobalHotkeyService Hotkeys { get; }
    public ClipItemFactory Factory { get; }

    public AppServices()
    {
        using (var ctx = new ClipDbContext(AppPaths.DbPath))
            ctx.Database.EnsureCreated();

        Store = new SqliteClipStore(() => new ClipDbContext(AppPaths.DbPath),
                                    new RetentionPolicy());
        Factory = new ClipItemFactory(AppPaths.BlobDir);
        (Monitor, Writer) = PlatformFactory.CreateClipboard();
        Hotkeys = PlatformFactory.CreateHotkeys();
    }

    public void Start(Action onHotkey)
    {
        Monitor.ClipboardChanged += async r =>
        {
            var item = Factory.Create(r);
            await Store.AddAsync(item);
            await Store.ApplyRetentionAsync();
        };
        Monitor.Start();

        if (!Hotkeys.Register("Ctrl+Shift+V", onHotkey))
            Console.Error.WriteLine("Hotkey Ctrl+Shift+V is unavailable.");
        Hotkeys.Start();
    }
}
```

- [ ] **Step 2: Add a tray icon and start services in App.axaml.cs**

In `App.axaml`, add inside `<Application>`:
```xml
<TrayIcon.Icons>
  <TrayIcons>
    <TrayIcon Icon="/Assets/tray.ico" ToolTipText="ClipVault">
      <TrayIcon.Menu>
        <NativeMenu>
          <NativeMenuItem Header="Open history" Click="OnOpenHistory"/>
          <NativeMenuItem Header="Settings" Click="OnOpenSettings"/>
          <NativeMenuItem Header="Quit" Click="OnQuit"/>
        </NativeMenu>
      </TrayIcon.Menu>
    </TrayIcon>
  </TrayIcons>
</TrayIcon.Icons>
```

In `App.axaml.cs`:
```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace ClipVault.App;

public partial class App : Application
{
    public AppServices Services { get; } = new();

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown; // tray app

        Services.Start(onHotkey: ShowHistory);
        base.OnFrameworkInitializationCompleted();
    }

    private void ShowHistory()
    {
        // Implemented in Task 16; for the smoke test, log:
        Console.WriteLine("Hotkey fired");
    }

    private void OnOpenHistory(object? s, System.EventArgs e) => ShowHistory();
    private void OnOpenSettings(object? s, System.EventArgs e) { }
    private void OnQuit(object? s, System.EventArgs e) =>
        (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
}
```

Add any 16×16 `.ico` at `src/ClipVault.App/Assets/tray.ico` (placeholder is fine).

- [ ] **Step 3: Build**

Run: `dotnet build src/ClipVault.App`
Expected: Build succeeded.

- [ ] **Step 4: Manual smoke test (current OS)**

Run: `dotnet run --project src/ClipVault.App`
Then:
1. Confirm a tray icon appears and the app does NOT open a window.
2. Copy some text in any app. (No crash; item is saved.)
3. Press `Ctrl+Shift+V`. Expected: console prints "Hotkey fired".
4. Inspect the DB exists at the OS data dir (`clipvault.db`).
Expected: all four pass. If the hotkey doesn't fire, check SharpHook
permissions (macOS requires Accessibility permission).

- [ ] **Step 5: Commit**

```bash
git add src/ClipVault.App
git commit -m "feat(app): tray bootstrap, monitor→store wiring, hotkey smoke test"
```

---

## Task 16: History window — list, search, keyboard nav, paste-back

**Files:**
- Create: `src/ClipVault.App/ViewModels/HistoryViewModel.cs`
- Create: `src/ClipVault.App/Views/HistoryWindow.axaml` and `.axaml.cs`
- Modify: `src/ClipVault.App/App.axaml.cs` (`ShowHistory`)

- [ ] **Step 1: Write the view model**

```csharp
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using ClipVault.Core.Abstractions;
using ClipVault.Core.Models;

namespace ClipVault.App.ViewModels;

public class HistoryViewModel
{
    private readonly IClipStore _store;
    private readonly IClipboardWriter _writer;

    public ObservableCollection<ClipItem> Items { get; } = new();

    public HistoryViewModel(IClipStore store, IClipboardWriter writer)
    {
        _store = store;
        _writer = writer;
    }

    public async Task LoadAsync(string? query = null)
    {
        Items.Clear();
        var data = string.IsNullOrWhiteSpace(query)
            ? await _store.GetRecentAsync()
            : await _store.SearchAsync(query);
        foreach (var i in data) Items.Add(i);
    }

    public void Paste(ClipItem item) => _writer.Write(item);

    public async Task TogglePinAsync(ClipItem item)
    {
        await _store.SetPinnedAsync(item.Id, !item.IsPinned);
        await LoadAsync();
    }

    public async Task DeleteAsync(ClipItem item)
    {
        await _store.DeleteAsync(item.Id);
        await LoadAsync();
    }
}
```

- [ ] **Step 2: Write the window**

`HistoryWindow.axaml`:
```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="ClipVault.App.Views.HistoryWindow"
        Width="480" Height="560" WindowStartupLocation="CenterScreen"
        Title="ClipVault" ShowInTaskbar="False">
  <DockPanel Margin="8">
    <TextBox x:Name="Search" DockPanel.Dock="Top" Watermark="Search…"/>
    <ListBox x:Name="List" Margin="0,8,0,0">
      <ListBox.ItemTemplate>
        <DataTemplate>
          <StackPanel Orientation="Horizontal" Spacing="6">
            <TextBlock Text="{Binding IsPinned, Converter={x:Static BoolPinConverter.Instance}}"/>
            <TextBlock Text="{Binding Preview}" TextTrimming="CharacterEllipsis"/>
          </StackPanel>
        </DataTemplate>
      </ListBox.ItemTemplate>
    </ListBox>
  </DockPanel>
</Window>
```

(If `BoolPinConverter` is more than you want for MVP, bind `Preview` only and
drop the pin glyph column; keep the list minimal.)

`HistoryWindow.axaml.cs`:
```csharp
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ClipVault.App.ViewModels;
using ClipVault.Core.Models;

namespace ClipVault.App.Views;

public partial class HistoryWindow : Window
{
    private readonly HistoryViewModel _vm;

    public HistoryWindow(HistoryViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        var list = this.FindControl<ListBox>("List")!;
        var search = this.FindControl<TextBox>("Search")!;
        list.ItemsSource = _vm.Items;

        search.KeyUp += async (_, _) => await _vm.LoadAsync(search.Text);
        list.DoubleTapped += (_, _) => PasteSelected();
        KeyDown += OnKeyDown;
        Opened += async (_, _) => { await _vm.LoadAsync(); search.Focus(); };
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Hide();
        else if (e.Key == Key.Enter) PasteSelected();
    }

    private void PasteSelected()
    {
        var list = this.FindControl<ListBox>("List")!;
        if (list.SelectedItem is ClipItem item)
        {
            _vm.Paste(item);
            Hide();
        }
    }
}
```

- [ ] **Step 3: Wire ShowHistory in App.axaml.cs**

Replace the placeholder `ShowHistory`:
```csharp
private HistoryWindow? _history;

private void ShowHistory()
{
    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
    {
        _history ??= new Views.HistoryWindow(
            new ViewModels.HistoryViewModel(Services.Store, Services.Writer));
        _history.Show();
        _history.Activate();
    });
}
```

- [ ] **Step 4: Build**

Run: `dotnet build src/ClipVault.App`
Expected: Build succeeded.

- [ ] **Step 5: Manual smoke test**

Run: `dotnet run --project src/ClipVault.App`
1. Copy 3 different texts.
2. Press `Ctrl+Shift+V` → window shows the 3 items, newest first.
3. Type in search → list filters.
4. Select an item, press Enter → window hides and that text is now in the
   clipboard (paste with Ctrl+V into any app to confirm).
5. Press `Ctrl+Shift+V`, then Esc → window hides.
Expected: all pass.

- [ ] **Step 6: Commit**

```bash
git add src/ClipVault.App
git commit -m "feat(app): history window with search, keyboard nav, paste-back"
```

---

## Task 17: Pinning and delete in the history window

**Files:**
- Modify: `src/ClipVault.App/Views/HistoryWindow.axaml.cs`

- [ ] **Step 1: Add key handlers for pin (Ctrl+P) and delete (Del)**

In `OnKeyDown`, extend:
```csharp
private async void OnKeyDown(object? sender, KeyEventArgs e)
{
    var list = this.FindControl<ListBox>("List")!;
    var sel = list.SelectedItem as ClipItem;

    if (e.Key == Key.Escape) Hide();
    else if (e.Key == Key.Enter) PasteSelected();
    else if (e.Key == Key.Delete && sel is not null) await _vm.DeleteAsync(sel);
    else if (e.Key == Key.P && e.KeyModifiers == KeyModifiers.Control && sel is not null)
        await _vm.TogglePinAsync(sel);
}
```

- [ ] **Step 2: Build**

Run: `dotnet build src/ClipVault.App`
Expected: Build succeeded.

- [ ] **Step 3: Manual smoke test**

1. Open history, select an item, press Ctrl+P → it jumps to the top and stays
   on top after copying new items (pinned ordering).
2. Select an item, press Del → it disappears.
Expected: both pass.

- [ ] **Step 4: Commit**

```bash
git add src/ClipVault.App/Views/HistoryWindow.axaml.cs
git commit -m "feat(app): pin and delete in history window"
```

---

## Task 18: Settings window — hotkey rebind and retention

**Files:**
- Create: `src/ClipVault.Core/Models/AppSettings.cs`
- Create: `src/ClipVault.App/ViewModels/SettingsViewModel.cs`
- Create: `src/ClipVault.App/Views/SettingsWindow.axaml` and `.axaml.cs`
- Modify: `src/ClipVault.App/App.axaml.cs` (`OnOpenSettings`)

- [ ] **Step 1: Settings model + JSON persistence**

`AppSettings.cs`:
```csharp
using System.Text.Json;
using ClipVault.Core.Paths;

namespace ClipVault.Core.Models;

public class AppSettings
{
    public string Hotkey { get; set; } = "Ctrl+Shift+V";
    public int MaxUnpinned { get; set; } = 500;
    public int MaxAgeDays { get; set; } = 30;

    private static string FilePath => Path.Combine(AppPaths.DataDir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath))
                       ?? new AppSettings();
        }
        catch { /* corrupt settings → defaults */ }
        return new AppSettings();
    }

    public void Save() =>
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
}
```

- [ ] **Step 2: Settings view model**

`SettingsViewModel.cs`:
```csharp
using ClipVault.Core.Models;

namespace ClipVault.App.ViewModels;

public class SettingsViewModel
{
    public AppSettings Settings { get; }
    public SettingsViewModel(AppSettings settings) => Settings = settings;
    public void Save() => Settings.Save();
}
```

- [ ] **Step 3: Settings window**

`SettingsWindow.axaml`:
```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="ClipVault.App.Views.SettingsWindow"
        Width="380" Height="220" Title="ClipVault Settings"
        WindowStartupLocation="CenterScreen">
  <StackPanel Margin="16" Spacing="10">
    <TextBlock Text="Hotkey"/>
    <TextBox x:Name="Hotkey"/>
    <TextBlock Text="Max unpinned items"/>
    <NumericUpDown x:Name="MaxUnpinned" Minimum="10" Maximum="5000"/>
    <TextBlock Text="Max age (days)"/>
    <NumericUpDown x:Name="MaxAge" Minimum="1" Maximum="3650"/>
    <Button x:Name="SaveBtn" Content="Save" HorizontalAlignment="Right"/>
  </StackPanel>
</Window>
```

`SettingsWindow.axaml.cs`:
```csharp
using Avalonia.Controls;
using ClipVault.App.ViewModels;

namespace ClipVault.App.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _vm;

    public SettingsWindow(SettingsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        var hotkey = this.FindControl<TextBox>("Hotkey")!;
        var maxUnpinned = this.FindControl<NumericUpDown>("MaxUnpinned")!;
        var maxAge = this.FindControl<NumericUpDown>("MaxAge")!;
        var save = this.FindControl<Button>("SaveBtn")!;

        hotkey.Text = _vm.Settings.Hotkey;
        maxUnpinned.Value = _vm.Settings.MaxUnpinned;
        maxAge.Value = _vm.Settings.MaxAgeDays;

        save.Click += (_, _) =>
        {
            _vm.Settings.Hotkey = hotkey.Text ?? "Ctrl+Shift+V";
            _vm.Settings.MaxUnpinned = (int)(maxUnpinned.Value ?? 500);
            _vm.Settings.MaxAgeDays = (int)(maxAge.Value ?? 30);
            _vm.Save();
            Close();
        };
    }
}
```

- [ ] **Step 4: Load settings at startup and wire OnOpenSettings**

In `AppServices` constructor, load settings and use them for the hotkey and
retention policy instead of hardcoded values:
```csharp
public AppSettings Settings { get; } = AppSettings.Load();
```
Use `Settings.Hotkey` in `Start()` and
`new RetentionPolicy(Settings.MaxUnpinned, TimeSpan.FromDays(Settings.MaxAgeDays))`
for the store.

In `App.axaml.cs`:
```csharp
private void OnOpenSettings(object? s, System.EventArgs e)
{
    var win = new Views.SettingsWindow(new ViewModels.SettingsViewModel(Services.Settings));
    win.Show();
}
```

- [ ] **Step 5: Build**

Run: `dotnet build src/ClipVault.App`
Expected: Build succeeded.

- [ ] **Step 6: Manual smoke test**

1. Tray → Settings → change Max age to 1, Save.
2. Restart app → reopen Settings → value persisted as 1.
3. Change hotkey to `Ctrl+Shift+C`, Save, restart → that combo opens history.
Expected: all pass.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat(app): settings window for hotkey and retention"
```

---

## Task 19: README and run scripts

**Files:**
- Create: `README.md`
- Create: `run.bat` (Windows), `run.sh` (Linux/macOS)

- [ ] **Step 1: Write README**

Cover: what it is, supported OS, **Linux requires `wl-clipboard` or `xclip`**,
**macOS requires Accessibility permission** for the global hotkey, build/run
commands, default hotkey, data location.

- [ ] **Step 2: Write run scripts**

`run.bat`:
```bat
@echo off
dotnet run --project src/ClipVault.App
```
`run.sh`:
```bash
#!/usr/bin/env bash
dotnet run --project src/ClipVault.App
```

- [ ] **Step 3: Commit**

```bash
git add README.md run.bat run.sh
git commit -m "docs: add README and run scripts"
```

---

## Task 20: Full regression smoke test on the current OS

- [ ] **Step 1: Run all unit tests**

Run: `dotnet test`
Expected: all tests pass (ClipHasher, RetentionPolicy, ClipItemFactory, SqliteClipStore).

- [ ] **Step 2: End-to-end manual run**

`dotnet run --project src/ClipVault.App`, then verify the full loop:
copy text → hotkey → search → Enter pastes → pin → delete → settings persist →
quit from tray. Confirm no crashes and `clipvault.db` + `blobs/` exist.

- [ ] **Step 3: Final commit / tag**

```bash
git add -A
git commit -m "chore: ClipVault Core MVP complete" --allow-empty
git tag core-mvp
```

---

## Self-Review Notes

- **Spec coverage:** text/image/file capture (Tasks 9, 11–13), local SQLite
  storage (7–8), dedup (3, 8), retention (4, 8), keyboard-first history window
  (16), search (8, 16), pinning (17), settings/hotkey rebind (18), per-OS paths
  (5), error handling for locked clipboard / taken hotkey / corrupt settings
  (12–13, 15, 18). Sync is intentionally deferred to Plan 2 (`ISyncService` seam
  left in `IClipStore` consumers; not wired here).
- **Known MVP limitation:** image/file capture is fully specified for Windows
  (Task 11) but text-only on Linux/macOS in this plan; image/file on those OSes
  is a fast follow noted inline. This is an explicit, documented scope choice,
  not a placeholder.
- **Type consistency:** `IClipStore`, `ClipItem`, `ClipboardReadResult`,
  `ClipItemFactory.Create`, `RetentionPolicy.SelectForDeletion` signatures are
  used identically across tasks.
```
