# ClipVault

A cross-platform clipboard manager for **Windows, Linux, and macOS**. Runs in the
system tray, records everything you copy (text, images, files), and gives fast
keyboard-driven access to your clipboard history. Local-first and privacy-first —
nothing leaves your machine.

> Built on Avalonia / .NET. LAN sync between your own devices is planned (Plan 2).

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

## Data location

| OS | Path |
|----|------|
| Windows | `%APPDATA%\ClipVault\` |
| Linux | `~/.config/ClipVault/` |
| macOS | `~/Library/Application Support/ClipVault/` |

Contains `clipvault.db` (SQLite), `blobs/` (image data), `logs/`, `settings.json`.

## Tests

```bash
dotnet test
```

## License

TBD
