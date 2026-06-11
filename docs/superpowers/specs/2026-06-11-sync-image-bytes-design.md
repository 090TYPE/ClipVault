# Sync Image Bytes — Design Spec (Phone Receive, Part A)

**Date:** 2026-06-11
**Status:** Approved (design), pending implementation plan

## Overview

Make images sync across devices by transmitting the image **bytes** in the sync
message, instead of only the (sender-local) blob path. Each receiver materializes
the bytes into its own blob directory and rewrites `BlobPath` to a local path.

This is **Part A** of the "receive on phone" feature. It is valuable on its own:
it also fixes desktop↔desktop image sync, which today transmits a path that does
not exist on the receiving machine.

Part B (the Avalonia.Android receive-only client) is a separate spec/plan and
depends on this.

## Problem

`SyncMessage` currently serializes the whole `ClipItem`, including `BlobPath` —
an absolute path on the **sender's** disk. For image clips the receiver stores an
item pointing at a path that doesn't exist locally, so the image is unusable.
Image bytes are never transmitted.

## Goals

- Image clips sync correctly between any two devices (desktop or phone).
- Each receiver writes received image bytes into its own blob directory and uses
  a local `BlobPath`.
- Text and file clips are unchanged.
- Hash dedup still prevents echo loops (image hash = hash of bytes, identical on
  both sides).
- Verified by an automated loopback test (runs on the dev machine).

## Non-Goals

- The Android app itself (Part B).
- Transmitting actual file contents for `Files` clips (still paths-as-text).
- History backfill on pair (live items only, per the feature decision).

## Design

### SyncMessage

Add an optional image payload:

```csharp
public class SyncMessage
{
    public string Type { get; set; } = "";
    public ClipItem? Item { get; set; }
    public byte[]? ImageBytes { get; set; }   // PNG bytes for Image clips
    // ToBytes/FromBytes unchanged (System.Text.Json encodes byte[] as base64)
}
```

`ForClip(item)` stays for text/files. A new overload attaches bytes:
`ForClip(item, imageBytes)`.

### SyncService

- **Constructor** gains a `string blobDir` parameter — where received image bytes
  are written. Existing callers pass their blob directory.
- **BroadcastAsync:** when `item.Type == Image` and `item.BlobPath` exists, read
  the bytes and send `SyncMessage.ForClip(item, bytes)`. If the blob file is
  missing, send the item with no `ImageBytes` (do not throw).
- **Receive (HandleData):** after decrypting and parsing, if the item is an
  `Image` and `ImageBytes` is present, write the bytes to
  `blobDir/{newGuid}.png`, set `item.BlobPath` to that local path, then raise
  `RemoteClipReceived(item)`. If writing fails, raise the item unchanged (history
  shows "image unavailable") — never throw on the receive path.

### AppServices (desktop host)

Pass `AppPaths.BlobDir` to the `SyncService` constructor. No other change — the
existing `RemoteClipReceived → Store.AddAsync` bridge already persists the (now
locally-materialized) item.

## Data Flow

```
PC: copy image → Store (blob on PC) → BroadcastAsync
      └─ SyncMessage { Item (BlobPath=PC path, ignored), ImageBytes = PNG }
Receiver: decrypt → ImageBytes present →
      write blobDir/{guid}.png → item.BlobPath = local → RemoteClipReceived → Store
```

## Error Handling

- Send: missing blob file → message sent without `ImageBytes`; no crash.
- Receive: blob write failure → item delivered without a local blob; no crash.
- Size: the existing 32 MB `MessageFraming` cap rejects oversized frames; such an
  image simply fails to transfer (logged/dropped), not fatal.

## Testing

- **Loopback (runs here):** pair two `SyncService` instances, broadcast an
  `Image` clip backed by a real PNG blob, assert the receiver's item has a
  **local** `BlobPath` (under the receiver's blob dir, different from the sender's)
  whose file bytes equal the original.
- **Existing tests:** text loopback and all unit tests must still pass.
