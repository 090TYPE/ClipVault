# Image Thumbnails in History — Design Spec

**Date:** 2026-06-11
**Status:** Approved (design), pending implementation plan

## Overview

Show image clips in the history window as actual thumbnails instead of the
`[image]` text placeholder. Scope is the desktop history window only; capture,
storage, and sync are unchanged.

## Goals

- Image history rows display a ~64px thumbnail loaded from the item's `BlobPath`.
- Text and file rows look exactly as before.
- Missing/corrupt blob → no thumbnail, row still renders (no crash).
- Thumbnails decode downscaled (efficient for a list of up to ~200 items).

## Non-Goals

- Large/zoom preview pane (a possible later enhancement).
- Any change to capture, storage, retention, or sync.

## Design

`HistoryWindow.axaml` row template gains an `Image` (max 64×64, `Stretch=Uniform`)
shown for image items, alongside the existing `Preview` `TextBlock` shown for
non-image items. Two small converters in the App:

- **`PathToThumbnailConverter`** (`IValueConverter`): `string?` path →
  `Avalonia.Media.Imaging.Bitmap?`. Loads via `Bitmap.DecodeToWidth(stream, 128)`
  for a downscaled thumbnail; returns `null` if the path is null/empty or the file
  is missing/unreadable (caught).
- **`EnumEqualsConverter`** (`IValueConverter`): returns `true` when the bound
  value's string form equals the `ConverterParameter`. Used to drive `IsVisible`:
  image control visible when `Type` == `Image`; preview text visible otherwise
  (an `Invert` variant or a second binding handles the inverse).

Converters are registered in `App.axaml` resources so the data template can
reference them as `StaticResource`. The row template keeps `x:DataType` =
`ClipVault.Core.Models.ClipItem` for compiled bindings.

## Error Handling

`PathToThumbnailConverter` wraps file IO in try/catch and returns `null` on any
failure; an `Image` with a null `Source` renders nothing, so the row degrades to
just the (hidden) text without breaking layout.

## Testing

No XAML unit tests. Verified by running the app: copy an image, open history with
the hotkey, confirm a thumbnail appears; confirm text/file rows are unchanged.
