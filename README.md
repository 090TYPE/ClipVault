# ClipVault

A cross-platform clipboard manager for **Windows, Linux, and macOS**. Runs in the
system tray, records everything you copy (text, images, files), and gives fast
keyboard-driven access to your clipboard history. Local-first and privacy-first —
nothing leaves your machine.

> Built on Avalonia / .NET. Optional LAN sync between your own devices is built in (encrypted, paired by PIN).

## Features

- 📋 Background capture of clipboard history (text, images, files)
- ⌨️ Global hotkey opens a keyboard-first history window (default `Ctrl+Shift+V`)
- 🔍 Instant search over history
- 📌 Pin frequently used items so they never expire
- 🧹 Automatic dedup and retention (keep last N items / N days)
- 🔒 100% local — SQLite database in your user profile

## Platform support (MVP)

| OS | Text | Images | Files |
|----|------|--------|-------|
| Windows | ✅ | ✅ | ✅ (pastes as paths) |
| Linux | ✅ | ⏳ | ⏳ |
| macOS | ✅ | ⏳ | ⏳ |

Image/file capture on Linux and macOS is a planned follow-up.

## Requirements

- **.NET 10 SDK** (to build/run from source)
- **Linux:** `wl-clipboard` (Wayland) or `xclip` (X11) must be installed
- **macOS:** grant the app **Accessibility** permission (System Settings →
  Privacy & Security → Accessibility) so the global hotkey works

## Build & run

```bash
dotnet build
dotnet run --project src/ClipVault.App
```

Or use the helper scripts: `run.bat` (Windows) / `run.sh` (Linux/macOS).

The app starts minimized to the tray — there is no main window. Use the tray menu
or the global hotkey.

## Usage

- Copy anything as usual — it's captured automatically.
- Press **`Ctrl+Shift+V`** to open history.
- Type to **search**, arrow keys to navigate.
- **Enter** pastes the selected item back to the clipboard (then paste with Ctrl+V).
- **Ctrl+P** pins/unpins, **Del** deletes, **Esc** closes.
- Change the hotkey and retention in **tray → Settings** (restart to apply).

## Sync (LAN)

Sync shares clipboard items between your own devices on the same local network —
encrypted end-to-end, no cloud, no account.

**How it works:** devices discover each other via UDP broadcast, pair with a
6-digit PIN (ECDH P-256 + HKDF key agreement), and exchange items over
AES-256-GCM-encrypted TCP. Duplicate items are dropped by content hash, so there
are no echo loops.

**Pairing:**
1. Run ClipVault on both devices (same LAN). Open **tray → Settings** on each;
   each device appears in the other's "Sync devices" list within a few seconds.
2. On device A click **Show PIN**.
3. On device B select device A, type the PIN, click **Pair with selected** →
   "Paired ✓". Now copies on either device appear on the other.

**Firewall:** allow **UDP port 45678** (discovery) and the app's dynamic TCP
port. On first run the OS may prompt to allow ClipVault on private networks —
accept it.

**Testing sync:** the loopback integration test (`SyncServiceLoopbackTests`)
needs UDP broadcast on the local host; some CI sandboxes block it. Run
`dotnet test` locally if it is skipped/flaky in a restricted environment.

## Data location

| OS | Path |
|----|------|
| Windows | `%APPDATA%\ClipVault\` |
| Linux | `~/.config/ClipVault/` |
| macOS | `~/Library/Application Support/ClipVault/` |

Contains `clipvault.db` (SQLite), `blobs/` (image data), `logs/`, `settings.json`,
`device.json` (this device's sync identity), and `trust.json` (paired devices).

## Tests

```bash
dotnet test
```

## License

TBD
