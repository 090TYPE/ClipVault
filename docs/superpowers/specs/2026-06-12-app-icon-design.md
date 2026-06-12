# App Icon — Design Spec

**Date:** 2026-06-12
**Status:** Approved (design), pending implementation plan

## Overview

Design and produce a real app icon for ClipVault — the "Clip Stack" concept
(three stacked cards with a neon-green gradient + a clip on top), matching the
app's Neon Terminal aesthetic. Replace the placeholder solid-teal tray icon and
add window + executable icons across platforms.

## Goals

- One master SVG as the source of truth.
- Windows `app.ico` (16/32/48/256) used for the `.exe` and app windows.
- Windows `tray.ico` (16/32) replacing the current solid placeholder.
- A 512px PNG for macOS/Linux packaging.
- A reproducible generator so the icons can be regenerated from the SVG.

## Non-Goals

- macOS `.icns` bundle assembly and notarization (a 512 PNG is provided; full
  `.icns`/app-bundle packaging is out of scope).
- Animated or adaptive icons.
- Shipping any icon-generation dependency inside the app.

## Visual Design

Rounded-square tile, background `#05060a`, subtle border `#1f5132`. Three
stacked rounded cards offset down-right, stroked in a depth gradient — back
`#1f7a3a` (dim), middle `#2dd4bf` (teal), front `#39ff14` (neon green, with a
soft glow). A small filled clip tab (`#39ff14`) sits on the front card's top
edge. At 16px the back (dim) card is dropped so strokes don't merge.

## Components

```
assets/icon.svg                       — NEW master source
tools/IconGen/IconGen.csproj          — NEW throwaway generator (net10 + Svg.Skia)
tools/IconGen/Program.cs              — renders PNGs, assembles .ico (PNG-in-ICO)
src/ClipVault.App/Assets/app.ico      — generated (16/32/48/256)
src/ClipVault.App/Assets/tray.ico     — generated (16/32), replaces placeholder
src/ClipVault.App/Assets/icon-512.png — generated (macOS/Linux)
src/ClipVault.App/ClipVault.App.csproj — + <ApplicationIcon>Assets/app.ico
src/ClipVault.App/Views/HistoryWindow.axaml  — Icon="/Assets/app.ico"
src/ClipVault.App/Views/SettingsWindow.axaml — Icon="/Assets/app.ico"
```

### Generator (`tools/IconGen`)

Console app referencing `Svg.Skia`. Loads `assets/icon.svg`, renders to SkBitmap
at each size, encodes PNG. Assembles `.ico` files by writing the ICONDIR +
ICONDIRENTRY headers with each size stored as embedded PNG (supported on Windows
Vista+). Writes outputs to `src/ClipVault.App/Assets/`. The generator is **not**
referenced by the solution build; it is run once to produce committed assets and
kept for regeneration.

> The IconGen project is excluded from `ClipVault.sln` (run via
> `dotnet run --project tools/IconGen`) so the app/test build gains no
> Svg.Skia dependency.

### Wiring

- `<ApplicationIcon>Assets\app.ico</ApplicationIcon>` in the App csproj sets the
  Windows `.exe` icon.
- `Icon="/Assets/app.ico"` on both windows sets the title-bar/taskbar icon.
- Tray already references `/Assets/tray.ico`; the file is replaced in place.

## Error Handling

Icons are static assets. The only runtime concern is a missing/invalid file:
Avalonia already tolerates a tray icon load; windows with a bad icon path would
throw at construction, so the build-and-launch check covers it.

## Testing

No automated tests (binary assets). Verified by:
- Generator emits files with valid headers (ICO magic `00 00 01 00`, PNG magic)
  and non-trivial sizes.
- `dotnet build` of the App succeeds with `<ApplicationIcon>` set.
- App launches without resource/icon exceptions.
- Visual appearance in OS (taskbar/tray/exe) confirmed by the user.
