# UI Redesign + Theme Selection — Design Spec

**Date:** 2026-06-11
**Status:** Approved (design), pending implementation plan

## Overview

Redesign the desktop UI (history + settings windows) around a polished "Neon
Terminal" look, and add a theme selector in Settings with three themes — **Neon**
(default), **Dark**, **Light** — applied live without restart.

Scope is the Avalonia App only (presentation). Capture, storage, and sync are
unchanged.

## Goals

- A cohesive visual design: header with sync status, search box, rich list rows
  (type icon, image thumbnail, preview text, timestamp, pin marker), keyboard-hint
  footer.
- Three switchable themes driven by a single set of resource keys.
- Theme choice is persisted and applied **immediately** when changed in Settings
  (no restart), and loaded at startup.
- Default theme is Neon.

## Non-Goals

- Changing capture/storage/sync/hotkey behavior.
- Per-OS native title bars or custom window chrome.
- Animations beyond what the default Fluent theme provides.

## Theme Contract

Every theme is a `ResourceDictionary` defining the same brush keys:

| Key | Role |
|-----|------|
| `Cv.Background` | window background |
| `Cv.Surface` | list row / input background |
| `Cv.SurfaceAlt` | header / footer background |
| `Cv.Accent` | accent (selection border, glyphs, logo) |
| `Cv.Text` | primary text |
| `Cv.TextDim` | secondary text (timestamps, hints) |
| `Cv.Border` | row / input borders |
| `Cv.Pinned` | pinned-star color |

Windows reference these via `{DynamicResource Cv.*}` so a runtime swap repaints
them instantly.

### Theme palettes

- **Neon** (default): `Background #05060a`, `Surface #0a0f0c`, `SurfaceAlt #080b0e`,
  `Accent #39ff14`, `Text #c8f7e0`, `TextDim #4a6356`, `Border #1f5132`,
  `Pinned #ffd166`. Base variant: Dark.
- **Dark**: `Background #0d1117`, `Surface #161b22`, `SurfaceAlt #0d1117`,
  `Accent #2dd4bf`, `Text #e6edf3`, `TextDim #7d8590`, `Border #30363d`,
  `Pinned #ffd166`. Base variant: Dark.
- **Light**: `Background #f7f8fa`, `Surface #ffffff`, `SurfaceAlt #ffffff`,
  `Accent #6366f1`, `Text #1f2328`, `TextDim #9ca3af`, `Border #e5e7eb`,
  `Pinned #d97706`. Base variant: Light.

## Components

```
src/ClipVault.App/Themes/ThemeNeon.axaml    — NEW ResourceDictionary
src/ClipVault.App/Themes/ThemeDark.axaml    — NEW
src/ClipVault.App/Themes/ThemeLight.axaml   — NEW
src/ClipVault.App/Themes/ThemeManager.cs    — NEW: Apply(name), Themes list
src/ClipVault.App/App.axaml(.cs)            — MODIFY: load default theme on init
src/ClipVault.App/Views/HistoryWindow.axaml — MODIFY: redesigned layout, DynamicResource
src/ClipVault.App/Views/SettingsWindow.*    — MODIFY: theme dropdown + live apply
src/ClipVault.Core/Models/AppSettings.cs    — MODIFY: + Theme property (default "Neon")
```

### ThemeManager

- `static readonly string[] Themes = { "Neon", "Dark", "Light" }`.
- `Apply(string name)`: builds the matching `ResourceInclude`/dictionary, replaces
  the current theme entry in `Application.Current.Resources.MergedDictionaries`,
  and sets `Application.Current.RequestedThemeVariant` (Light for "Light", Dark
  otherwise). Unknown name falls back to "Neon".

### App startup

`App.OnFrameworkInitializationCompleted` (before showing anything) calls
`ThemeManager.Apply(Services.Settings.Theme)`.

### Settings window

A `ComboBox` bound to `ThemeManager.Themes`, initialized to the saved theme. On
`SelectionChanged`: call `ThemeManager.Apply(selected)`, set
`Settings.Theme = selected`, and `Save()`. This is the only Settings field that
applies live; hotkey/retention still note "restart required".

### History window redesign

DockPanel: header (logo + sync status), search box, scrollable list, footer
hints. Row template: pin star (when pinned), 40px image thumbnail (image items)
or type glyph + preview text (others), and a dim timestamp. All colors via
`DynamicResource Cv.*`. Selection uses an `Accent`-colored border/glow.

> Sync status text ("◉ N peers online") binds to `ISyncService.TrustedDevices`
> count for a static count; a live "online" indicator is out of scope (shows
> trusted device count).

## Error Handling

- `ThemeManager.Apply` with an unknown/empty name → Neon (no crash).
- A missing thumbnail still degrades gracefully (existing `PathToThumbnail`
  returns null).

## Testing

No XAML/theme unit tests. Verified by running the app: it starts in Neon without
resource errors; opening Settings and switching theme recolors the history window
live; the choice persists across restart. Existing unit tests must stay green.
