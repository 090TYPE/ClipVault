using System;
using ClipVault.Core.Abstractions;
using ClipVault.Core.Models;
using ClipVault.Core.Paths;
using ClipVault.Core.Storage;
using ClipVault.Platform;

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
    }

    public void Start(Action onHotkey)
    {
        Monitor.ClipboardChanged += async r =>
        {
            try
            {
                var item = Factory.Create(r);
                await Store.AddAsync(item);
                await Store.ApplyRetentionAsync();
            }
            catch { /* never let a capture crash the listener */ }
        };
        Monitor.Start();

        HotkeyRegistered = Hotkeys.Register(Settings.Hotkey, onHotkey);
        if (!HotkeyRegistered)
            Console.Error.WriteLine($"Hotkey {Settings.Hotkey} is unavailable.");
        Hotkeys.Start();
    }
}
