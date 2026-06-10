# ClipVault — Design Spec

**Date:** 2026-06-10
**Status:** Approved (design), pending implementation plan

## Overview

ClipVault is a cross-platform clipboard manager for Windows, Linux, and macOS.
It runs in the background (system tray), records everything the user copies
(text, images, files), and gives fast keyboard-driven access to clipboard
history via a global hotkey. History is stored locally; an optional LAN sync
module shares clips between the user's own trusted devices over the local
network. No cloud, privacy-first.

The market gap: good free cross-platform clipboard managers are scarce
(Ditto = Windows-only, Maccy = Mac-only, CopyQ = cross-platform but dated UI).

## Goals

- Cross-platform: Windows, Linux, macOS from a single Avalonia/.NET codebase.
- Capture text, images, and files from the system clipboard.
- Fast, keyboard-first history window opened by a global hotkey.
- Search, pinning, deduplication, retention policy.
- Optional LAN sync between the user's trusted devices, paired by PIN, encrypted.
- Local-first, no cloud, no account.

## Non-Goals (MVP)

- Cloud sync / hosted backend / user accounts.
- Mobile clients.
- Full-text search engine (simple LIKE is enough at expected volumes).
- Rich clip editing.

## Tech Stack

- **.NET 8 + Avalonia 11** (reuse of the MeetingTranscriber stack).
- **EF Core 8 + SQLite** for storage (proven pattern in MeetingTranscriber).
- **SharpHook** (libuiohook wrapper) for cross-platform global hotkeys.
- Thin per-OS native layer for clipboard change monitoring (no mature
  cross-platform library exists for clipboard *change events*).

### Chosen approach (B)

Lean on proven libraries where they exist (SharpHook for hotkeys); write a
thin native layer only where unavoidable (clipboard monitoring), hidden behind
`Core` interfaces. This minimizes platform-specific code and risk while keeping
the door open to replace SharpHook with custom native code later without
touching the core logic.

## Architecture

Single solution `ClipVault.sln` with clean separation of core logic, platform
layer, and UI:

```
ClipVault.sln
├── ClipVault.App          — Avalonia UI (tray, history window, settings, pairing)
├── ClipVault.Core         — logic: history, search, pinning, models,
│                            interfaces IClipboardMonitor / IClipboardWriter /
│                            IGlobalHotkeyService / IClipStore / ISyncService
├── ClipVault.Platform     — native implementations per Win/Linux/macOS
│                            + SharpHook for hotkeys
├── ClipVault.Sync         — LAN sync (discovery, PIN pairing, TCP, encryption)
└── ClipVault.Core.Tests   — unit tests for Core and Sync (interfaces/mocks)
```

**Principle:** `Core` knows nothing about a specific OS or about the UI — it
works through interfaces. This gives testability (core tested on mocks) and
swap-ability (replace SharpHook with native later) without rewriting logic.

### Key interfaces (in Core)

- `IClipboardMonitor` — raises `ClipboardChanged(ClipItem)`; implemented per OS in Platform.
- `IClipboardWriter` — writes an item back to the system clipboard (on selection from history).
- `IGlobalHotkeyService` — registers the open-window hotkey (via SharpHook).
- `IClipStore` — CRUD over history (EF Core + SQLite implementation).
- `ISyncService` — implemented in `ClipVault.Sync`.

### Lifecycle

App starts minimized to tray. `IClipboardMonitor` listens in the background;
new items are written to `IClipStore`. The global hotkey opens the history
window.

## Data Model & Storage

Single item model with type-specific payloads:

```csharp
class ClipItem {
    Guid     Id;
    ClipType Type;          // Text | Image | Files
    DateTime CreatedAt;
    bool     IsPinned;
    string?  SourceApp;     // originating app, if available

    string?  TextContent;   // for Text
    string?  Preview;       // short text preview for the list (any type)
    string?  BlobPath;      // path to PNG on disk, for Image
    string?  FilePaths;     // JSON array of paths, for Files

    string   Hash;          // SHA-256 for deduplication
}

enum ClipType { Text, Image, Files }
```

### Storage

- EF Core 8 + SQLite at:
  - Windows: `%APPDATA%\ClipVault\clipvault.db`
  - Linux: `~/.config/ClipVault/clipvault.db`
  - macOS: `~/Library/Application Support/ClipVault/clipvault.db`
- Images stored as files in `…/ClipVault/blobs/{id}.png`, **not** in the DB —
  the DB holds only the path. Keeps the database light and fast.

### Deduplication

On copy, compute `Hash` (SHA-256 of text / image bytes / file-path list). If the
most recent item has the same hash, do not duplicate — move it to the top
instead. Solves "copied the same thing three times".

### Retention (auto-cleanup)

Configurable; defaults: keep the last **500** unpinned items and nothing older
than **30 days**. Pinned items are never deleted. Old image blobs are deleted
together with their records.

### Search

Over `TextContent` and `Preview`, a simple `LIKE` query through EF Core. No
full-text engine needed at MVP volumes.

## Data Flow

```
[System clipboard] --change--> IClipboardMonitor (Platform)
       │
       ├─ reads content, detects type, computes Hash
       ├─ dedup check → IClipStore.Add(ClipItem)   [+ blob save]
       │
   [Hotkey Ctrl+Shift+V] → history window (Avalonia)
       │
       ├─ list with search/type filter, pinned on top
       ├─ select item → IClipboardWriter.Write(item) → system clipboard
       └─ window hides, focus returns to previous app
```

History window is lightweight, opens near the cursor / centered, full keyboard
navigation (arrows + Enter), Esc closes. Daily-use tool → speed and keyboard
are the priority.

## Sync Module (`ClipVault.Sync`)

Built on top of the working core, as a separate stage.

```
Discovery:  mDNS / UDP broadcast — devices find each other on the LAN
Pairing:    device A shows a 6-digit PIN →
            user enters PIN on B → exchange public keys (X25519) →
            shared secret stored, device added to trusted list
Transport:  persistent TCP connection between trusted devices
Crypto:     payload encrypted with AES-256-GCM using the shared secret
Sync:       new ClipItem → broadcast to trusted devices → they add to their IClipStore
Conflicts:  each item carries Id + CreatedAt + DeviceId; duplicates by Hash are
            dropped → ordering by time, effectively no conflicts
```

**Scope note:** the core (sections above + capture/paste/history/search/pinning)
is a complete, useful product on its own. Sync plugs in via `ISyncService` and
is not required for the rest to work. Implementation plan places sync as the
final stages; the core can ship earlier if desired.

## Error Handling

The app is a background process — it must not crash.

- **Clipboard monitoring:** another app may lock the clipboard or place a
  nonstandard format. Reads wrapped in try/catch with retry; unsupported
  formats are skipped, not fatal.
- **Clipboard write:** on failure → quiet log + brief tray notification; window
  stays open.
- **Hotkey taken:** if `Ctrl+Shift+V` is already registered by another program,
  catch the error at startup, show a warning in settings, and offer to rebind.
- **Storage:** DB/blob errors are logged; a corrupt image blob → item shows as
  "image unavailable", does not break the list.
- **Sync:** TCP drop → auto-reconnect with backoff; wrong PIN → pairing
  rejected; decryption failure → packet dropped and logged (possible attack).
- **Logs:** to `…/ClipVault/logs/`, with rotation.

## Testing

- **Unit tests (`ClipVault.Core.Tests`)** — on interfaces with mocked
  `IClipboardMonitor` / `IClipStore`: hash dedup, retention policy,
  search/filter, pinning logic.
- **Sync tests** — two in-memory `ISyncService` instances (loopback): PIN
  pairing, encrypt/decrypt round-trip, hash-based duplicate dropping.
- **Platform layer** — thin by design; verified by manual smoke tests on each
  OS (capture text/image/file, hotkey, paste), since native integration can't
  be unit-tested.
- **Approach:** Core and Sync written with TDD (test → implementation);
  Platform verified manually.

## Build Order (high level)

1. Core models + `IClipStore` (EF Core + SQLite) + retention/dedup logic (TDD).
2. Platform: `IClipboardMonitor` / `IClipboardWriter` per OS; `IGlobalHotkeyService` via SharpHook.
3. App: tray, history window (keyboard-first), search, pinning, settings.
4. Sync: discovery, PIN pairing, encrypted TCP transport, item propagation (TDD).
5. Cross-OS smoke testing and polish.
