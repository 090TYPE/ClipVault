using System;
using System.IO;
using ClipVault.Core.Abstractions;
using ClipVault.Core.Models;
using ClipVault.Core.Paths;
using ClipVault.Core.Storage;
using ClipVault.Platform;
using ClipVault.Sync;

namespace ClipVault.App;

/// Minimal composition root (no DI container needed for MVP).
public class AppServices
{
    public AppSettings Settings { get; } = AppSettings.Load();
    public IClipStore Store { get; }
    public IClipboardMonitor Monitor { get; }
    public IClipboardWriter Writer { get; }
    public IGlobalHotkeyService Hotkeys { get; }
    public ClipItemFactory Factory { get; }
    public ISyncService Sync { get; }

    public bool HotkeyRegistered { get; private set; }

    public AppServices()
    {
        using (var ctx = new ClipDbContext(AppPaths.DbPath))
            ctx.Database.EnsureCreated();

        Store = new SqliteClipStore(
            () => new ClipDbContext(AppPaths.DbPath),
            new RetentionPolicy(Settings.MaxUnpinned, TimeSpan.FromDays(Settings.MaxAgeDays)));
        Factory = new ClipItemFactory(AppPaths.BlobDir);
        (Monitor, Writer) = PlatformFactory.CreateClipboard();
        Hotkeys = PlatformFactory.CreateHotkeys();

        var identity = DeviceIdentity.LoadOrCreate(Path.Combine(AppPaths.DataDir, "device.json"));
        var trust = new TrustStore(Path.Combine(AppPaths.DataDir, "trust.json"));
        Sync = new SyncService(identity, trust, AppPaths.BlobDir);
    }

    public void Start(Action onHotkey)
    {
        // Local capture → store → push to trusted peers.
        Monitor.ClipboardChanged += async r =>
        {
            try
            {
                var item = Factory.Create(r);
                var stored = await Store.AddAsync(item);
                await Store.ApplyRetentionAsync();
                await Sync.BroadcastAsync(stored);
            }
            catch { /* never let a capture crash the listener */ }
        };
        Monitor.Start();

        // Remote item → store. Hash dedup in the store prevents echo loops.
        Sync.RemoteClipReceived += async item =>
        {
            try { await Store.AddAsync(item); } catch { /* ignore bad remote item */ }
        };
        Sync.Start();

        HotkeyRegistered = Hotkeys.Register(Settings.Hotkey, onHotkey);
        if (!HotkeyRegistered)
            Console.Error.WriteLine($"Hotkey {Settings.Hotkey} is unavailable.");
        Hotkeys.Start();
    }
}
