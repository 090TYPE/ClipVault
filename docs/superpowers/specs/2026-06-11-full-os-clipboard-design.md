# Full OS Clipboard Parity — Design Spec

**Date:** 2026-06-11
**Status:** Approved (design), pending implementation plan

## Overview

Bring Linux and macOS clipboard capture up to parity with Windows: capture
**text, images, and files** on all three platforms. Today Linux and macOS only
capture text (a documented "fast follow" from Plan 1). This completes the
text+images+files scope the original ClipVault spec defined for every OS.

Windows is already complete and is **not modified** by this work.

## Goals

- Linux captures text, images (PNG), and files; macOS the same.
- Change detection covers all three content types (not just text).
- No new third-party dependencies — use the CLI tools already required
  (`wl-clipboard`/`xclip` on Linux) and macOS built-ins (`pbpaste`/`pbcopy`,
  `osascript`).
- Pure parsing/selection logic is unit-tested; CLI invocation is verified by
  manual smoke tests on real machines.

## Non-Goals

- Changing Windows behavior.
- Native `NSPasteboard`/X11 P/Invoke (CLI approach chosen for simplicity and
  zero dependencies).
- Real file-object paste-back. To match current Windows behavior, copied **files
  are captured** (and shown in history) but **pasted back as their paths (text)**
  on every platform. This is consistent, documented parity — not a regression.

## Testing Reality

Development happens on Windows; Linux and macOS clipboard code can be
**compiled** here but **not behavior-verified**. Therefore:
- All decision/parsing logic is extracted into pure, OS-agnostic helpers with
  xUnit tests that run anywhere.
- The CLI-invoking clipboard classes are verified by the user via manual smoke
  tests on Linux and macOS (documented in the plan and README).

## Approach

Each platform clipboard already polls on a timer. Extend the poll to:

1. Query the **available types** currently on the clipboard.
2. Pick the **richest** type by priority: **Files → Image → Text**.
3. Read that type's content, hash it, and emit a `ClipboardReadResult` only when
   the hash changes (dedup across all types, replacing the text-only hash).

### Linux (`wl-clipboard` / `xclip`)

- Detect environment via `WAYLAND_DISPLAY` (already done).
- List types: `wl-paste --list-types` (Wayland) / `xclip -selection clipboard -t TARGETS -o` (X11).
- **Files:** `text/uri-list` present → read it, parse `file://` URIs to paths.
- **Image:** `image/png` present → read raw PNG bytes
  (`wl-paste --type image/png` / `xclip -selection clipboard -t image/png -o`).
- **Text:** otherwise read plain text (current behavior).
- **Write:** text and image via the corresponding `--type`/`-t image/png`;
  files written back as newline-joined paths (text), matching Windows.

### macOS (`pbpaste` / `pbcopy` / `osascript`)

- **Types:** `osascript -e 'clipboard info'` reports class names
  (`«class furl»`, `«class PNGf»`, `«class utf8»`, …).
- **Files:** `furl` present → `osascript` returns POSIX path(s).
- **Image:** `PNGf` present → `osascript` dumps the PNG to a temp file; read bytes.
- **Text:** otherwise `pbpaste`.
- **Write:** text via `pbcopy`; image via
  `osascript -e 'set the clipboard to (read (POSIX file "…") as «class PNGf»)'`;
  files written back as paths (text), matching Windows.
- **Change detection:** hash of the richest content each ~400 ms poll.

## Components

```
src/ClipVault.Platform/
├── Shared/UriListParser.cs      — pure: file:// URI list → string[] paths (TESTED)
├── Shared/ClipTypeSelector.cs   — pure: available type tokens → ClipType? (TESTED)
├── Linux/LinuxClipboard.cs      — extend: type query, image, files, write
└── MacOS/MacClipboard.cs        — extend: clipboard info, image, files, write
```

- **`UriListParser`** — input: `text/uri-list` text; output: file system paths.
  Skips comment lines (`#`) and blank lines; decodes percent-encoding; strips the
  `file://` scheme (and host).
- **`ClipTypeSelector`** — input: the set of available type tokens for the
  platform; output: the chosen `ClipType` (Files > Image > Text > none). Knows the
  token spellings for both Linux MIME types and macOS class names.

## Error Handling

Unchanged philosophy — the background poll must never crash:
- Missing CLI tool, empty clipboard, or unreadable type → skip the tick.
- Malformed `uri-list` / `clipboard info` → fall through to the next type or skip.
- Temp-file failures on macOS image read → skip the image, do not throw.

## Testing

- **Unit (run here):** `UriListParser` (URIs with spaces/percent-encoding,
  comments, CRLF, multiple files) and `ClipTypeSelector` (priority ordering,
  Linux MIME tokens, macOS class tokens, empty input).
- **Manual smoke (user, on each OS):** copy text/image/files in another app →
  open ClipVault history via hotkey → confirm each appears with the right preview;
  select and paste back.
