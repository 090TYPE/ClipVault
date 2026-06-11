# Full OS Clipboard Parity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make Linux and macOS capture text, images, and files (parity with Windows), without new dependencies.

**Architecture:** Extract two pure, unit-tested helpers (`UriListParser`, `ClipTypeSelector`) into `ClipVault.Platform/Shared`, then extend `LinuxClipboard` and `MacClipboard` to query available clipboard types each poll, pick the richest (Files → Image → Text), read it via CLI tools, and emit the matching `ClipboardReadResult`. Windows is untouched.

**Tech Stack:** .NET 10, `wl-clipboard`/`xclip` (Linux), `pbpaste`/`pbcopy`/`osascript` (macOS), xUnit.

> **Testing reality:** Linux/macOS clipboard classes compile here but can only be
> behavior-verified by the user on those OSes. The pure helpers (Tasks 1–2) are
> fully unit-tested and run anywhere.

---

## File Structure

```
src/ClipVault.Platform/
├── Shared/UriListParser.cs     — pure: text/uri-list → file paths (NEW, tested)
├── Shared/ClipTypeSelector.cs  — pure: available type tokens → ClipType? (NEW, tested)
├── Linux/LinuxClipboard.cs     — MODIFY: type query + image + files + writes
└── MacOS/MacClipboard.cs       — MODIFY: clipboard info + image + files + writes

tests/ClipVault.Core.Tests/
├── UriListParserTests.cs       — NEW
└── ClipTypeSelectorTests.cs    — NEW
```

---

## Task 1: UriListParser (pure) — TDD

**Files:**
- Create: `src/ClipVault.Platform/Shared/UriListParser.cs`
- Test: `tests/ClipVault.Core.Tests/UriListParserTests.cs`
- Modify: `tests/ClipVault.Core.Tests/ClipVault.Core.Tests.csproj` (reference Platform)

- [ ] **Step 1: Reference the Platform project from tests**

Run: `dotnet add tests/ClipVault.Core.Tests reference src/ClipVault.Platform`

- [ ] **Step 2: Write the failing tests**

```csharp
using ClipVault.Platform.Shared;
using Xunit;

namespace ClipVault.Core.Tests;

public class UriListParserTests
{
    [Fact]
    public void Parses_SingleFileUri()
    {
        var paths = UriListParser.Parse("file:///home/user/file.txt");
        Assert.Single(paths);
        Assert.Equal("/home/user/file.txt", paths[0]);
    }

    [Fact]
    public void Decodes_PercentEncoding()
    {
        var paths = UriListParser.Parse("file:///home/user/My%20File.txt");
        Assert.Equal("/home/user/My File.txt", paths[0]);
    }

    [Fact]
    public void Parses_MultipleLines_CrLf_SkipsCommentsAndBlanks()
    {
        var text = "#comment\r\nfile:///a.txt\r\n\r\nfile:///b.txt\r\n";
        var paths = UriListParser.Parse(text);
        Assert.Equal(new[] { "/a.txt", "/b.txt" }, paths);
    }

    [Fact]
    public void Ignores_NonFileSchemes()
    {
        var paths = UriListParser.Parse("https://example.com/x\nfile:///c.txt");
        Assert.Equal(new[] { "/c.txt" }, paths);
    }

    [Fact]
    public void Empty_ReturnsEmpty()
    {
        Assert.Empty(UriListParser.Parse(""));
    }
}
```

- [ ] **Step 3: Run to verify it fails**

Run: `dotnet test --filter UriListParserTests`
Expected: FAIL — `UriListParser` does not exist.

- [ ] **Step 4: Implement**

```csharp
namespace ClipVault.Platform.Shared;

/// Parses a text/uri-list payload into file system paths.
public static class UriListParser
{
    public static IReadOnlyList<string> Parse(string uriList)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(uriList)) return result;

        foreach (var raw in uriList.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            if (line.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                if (Uri.TryCreate(line, UriKind.Absolute, out var uri) && uri.IsFile)
                    result.Add(uri.LocalPath);
            }
            else if (line.Contains("://"))
            {
                // some other scheme (http, etc.) — skip
            }
            else
            {
                result.Add(line); // already a bare path
            }
        }
        return result;
    }
}
```

- [ ] **Step 5: Run to verify it passes**

Run: `dotnet test --filter UriListParserTests`
Expected: PASS (5 tests).

> Note: on Windows `Uri.LocalPath` for `file:///a.txt` yields `\a.txt`; the test
> runs on the dev box (Windows) so it returns `/a.txt` only if the path is treated
> as UNC-free POSIX. If the CI/dev box normalizes separators, adjust the expected
> value to `Path`-agnostic by comparing `paths[0].Replace('\\','/')`. (Apply this
> only if the assertion fails due to separator differences.)

- [ ] **Step 6: Commit**

```bash
git add src/ClipVault.Platform/Shared/UriListParser.cs tests/ClipVault.Core.Tests/UriListParserTests.cs tests/ClipVault.Core.Tests/ClipVault.Core.Tests.csproj
git commit -m "feat(platform): add UriListParser"
```

---

## Task 2: ClipTypeSelector (pure) — TDD

**Files:**
- Create: `src/ClipVault.Platform/Shared/ClipTypeSelector.cs`
- Test: `tests/ClipVault.Core.Tests/ClipTypeSelectorTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using ClipVault.Core.Models;
using ClipVault.Platform.Shared;
using Xunit;

namespace ClipVault.Core.Tests;

public class ClipTypeSelectorTests
{
    [Fact]
    public void Files_BeatImageAndText()
    {
        var t = ClipTypeSelector.PickBest(new[] { "text/uri-list", "image/png", "text/plain" });
        Assert.Equal(ClipType.Files, t);
    }

    [Fact]
    public void Image_BeatsText()
    {
        Assert.Equal(ClipType.Image, ClipTypeSelector.PickBest(new[] { "image/png", "text/plain" }));
    }

    [Fact]
    public void Text_WhenOnlyText()
    {
        Assert.Equal(ClipType.Text, ClipTypeSelector.PickBest(new[] { "text/plain", "UTF8_STRING" }));
    }

    [Fact]
    public void MacOsClassTokens_Recognized()
    {
        Assert.Equal(ClipType.Files, ClipTypeSelector.PickBest(new[] { "«class furl»", "«class utf8»" }));
        Assert.Equal(ClipType.Image, ClipTypeSelector.PickBest(new[] { "«class PNGf»" }));
        Assert.Equal(ClipType.Text, ClipTypeSelector.PickBest(new[] { "«class utf8»" }));
    }

    [Fact]
    public void None_WhenNothingRecognized()
    {
        Assert.Null(ClipTypeSelector.PickBest(new[] { "application/x-weird" }));
        Assert.Null(ClipTypeSelector.PickBest(Array.Empty<string>()));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter ClipTypeSelectorTests`
Expected: FAIL — `ClipTypeSelector` does not exist.

- [ ] **Step 3: Implement**

```csharp
using ClipVault.Core.Models;

namespace ClipVault.Platform.Shared;

/// Chooses the richest clip type from the set of available clipboard type
/// tokens. Understands Linux MIME types and macOS pasteboard class names.
public static class ClipTypeSelector
{
    public static ClipType? PickBest(IEnumerable<string> typeTokens)
    {
        var tokens = typeTokens.Select(t => t.ToLowerInvariant()).ToList();

        if (tokens.Any(t => t.Contains("uri-list") || t.Contains("furl") || t.Contains("file-url")))
            return ClipType.Files;
        if (tokens.Any(t => t.Contains("png") || t.Contains("image/")))
            return ClipType.Image;
        if (tokens.Any(t => t.Contains("utf8") || t.Contains("text") || t.Contains("string")))
            return ClipType.Text;
        return null;
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test --filter ClipTypeSelectorTests`
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git add src/ClipVault.Platform/Shared/ClipTypeSelector.cs tests/ClipVault.Core.Tests/ClipTypeSelectorTests.cs
git commit -m "feat(platform): add ClipTypeSelector"
```

---

## Task 3: Extend LinuxClipboard (text + image + files)

Compile-verified here; behavior verified by the user on Linux.

**Files:**
- Modify: `src/ClipVault.Platform/Linux/LinuxClipboard.cs` (full replacement)

- [ ] **Step 1: Replace the file with the extended implementation**

```csharp
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using ClipVault.Core.Abstractions;
using ClipVault.Core.Hashing;
using ClipVault.Core.Models;
using ClipVault.Platform.Shared;

namespace ClipVault.Platform.Linux;

[SupportedOSPlatform("linux")]
public class LinuxClipboard : IClipboardMonitor, IClipboardWriter
{
    public event Action<ClipboardReadResult>? ClipboardChanged;

    private readonly System.Timers.Timer _timer = new(500);
    private string _lastHash = "";
    private readonly bool _wayland =
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));

    public void Start()
    {
        _timer.Elapsed += (_, _) => Poll();
        _timer.AutoReset = true;
        _timer.Start();
    }

    public void Stop() => _timer.Stop();

    private void Poll()
    {
        try
        {
            var types = ListTypes();
            var kind = ClipTypeSelector.PickBest(types);
            if (kind is null) return;

            ClipboardReadResult? result = kind switch
            {
                ClipType.Files => ReadFiles(),
                ClipType.Image => ReadImage(),
                ClipType.Text => ReadText(),
                _ => null
            };
            if (result is null) return;

            var hash = result.Type switch
            {
                ClipType.Image => ClipHasher.HashBytes(result.ImagePng ?? Array.Empty<byte>()),
                ClipType.Files => ClipHasher.HashText(string.Join("\n", result.FilePaths ?? new List<string>())),
                _ => ClipHasher.HashText(result.Text ?? "")
            };
            if (hash == _lastHash) return;
            _lastHash = hash;
            ClipboardChanged?.Invoke(result);
        }
        catch { /* tool missing / empty / unreadable — skip */ }
    }

    private IReadOnlyList<string> ListTypes()
    {
        var text = _wayland
            ? RunText("wl-paste", "--list-types")
            : RunText("xclip", "-selection clipboard -t TARGETS -o");
        return text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private ClipboardReadResult ReadText()
    {
        var text = _wayland
            ? RunText("wl-paste", "-n")
            : RunText("xclip", "-selection clipboard -o");
        return new ClipboardReadResult(ClipType.Text, text, null, null, null);
    }

    private ClipboardReadResult ReadImage()
    {
        var bytes = _wayland
            ? RunBytes("wl-paste", "--type image/png")
            : RunBytes("xclip", "-selection clipboard -t image/png -o");
        return new ClipboardReadResult(ClipType.Image, null, bytes, null, null);
    }

    private ClipboardReadResult ReadFiles()
    {
        var uriList = _wayland
            ? RunText("wl-paste", "--type text/uri-list")
            : RunText("xclip", "-selection clipboard -t text/uri-list -o");
        var paths = UriListParser.Parse(uriList);
        return new ClipboardReadResult(ClipType.Files, null, null, paths, null);
    }

    public void Write(ClipItem item)
    {
        try
        {
            switch (item.Type)
            {
                case ClipType.Text when item.TextContent is not null:
                    WriteText(item.TextContent);
                    break;
                case ClipType.Image when item.BlobPath is not null && File.Exists(item.BlobPath):
                    WriteImage(item.BlobPath);
                    break;
                case ClipType.Files when item.FilePaths is not null:
                    var paths = JsonSerializer.Deserialize<string[]>(item.FilePaths) ?? Array.Empty<string>();
                    WriteText(string.Join(Environment.NewLine, paths)); // parity with Windows
                    break;
            }
        }
        catch { /* swallow; caller surfaces a tray notice */ }
    }

    private void WriteText(string text)
    {
        var (exe, args) = _wayland ? ("wl-copy", "") : ("xclip", "-selection clipboard");
        PipeIn(exe, args, Encoding.UTF8.GetBytes(text));
    }

    private void WriteImage(string pngPath)
    {
        var bytes = File.ReadAllBytes(pngPath);
        var (exe, args) = _wayland
            ? ("wl-copy", "--type image/png")
            : ("xclip", "-selection clipboard -t image/png -i");
        PipeIn(exe, args, bytes);
    }

    private static string RunText(string exe, string args)
    {
        var psi = new ProcessStartInfo(exe, args)
        { RedirectStandardOutput = true, UseShellExecute = false };
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit(2000);
        return output;
    }

    private static byte[] RunBytes(string exe, string args)
    {
        var psi = new ProcessStartInfo(exe, args)
        { RedirectStandardOutput = true, UseShellExecute = false };
        using var p = Process.Start(psi)!;
        using var ms = new MemoryStream();
        p.StandardOutput.BaseStream.CopyTo(ms);
        p.WaitForExit(2000);
        return ms.ToArray();
    }

    private static void PipeIn(string exe, string args, byte[] data)
    {
        var psi = new ProcessStartInfo(exe, args)
        { RedirectStandardInput = true, UseShellExecute = false };
        using var p = Process.Start(psi)!;
        p.StandardInput.BaseStream.Write(data, 0, data.Length);
        p.StandardInput.Close();
        p.WaitForExit(2000);
    }

    public void Dispose() => Stop();
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build src/ClipVault.Platform`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/ClipVault.Platform/Linux/LinuxClipboard.cs
git commit -m "feat(platform): Linux clipboard text+image+files"
```

> Manual smoke (on Linux, after Task 5 build): copy text, an image, and files in
> a file manager; open ClipVault history; confirm each appears; paste back text
> and image.

---

## Task 4: Extend MacClipboard (text + image + files)

Compile-verified here; behavior verified by the user on macOS.

**Files:**
- Modify: `src/ClipVault.Platform/MacOS/MacClipboard.cs` (full replacement)

- [ ] **Step 1: Replace the file with the extended implementation**

```csharp
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using ClipVault.Core.Abstractions;
using ClipVault.Core.Hashing;
using ClipVault.Core.Models;
using ClipVault.Platform.Shared;

namespace ClipVault.Platform.MacOS;

[SupportedOSPlatform("macos")]
public class MacClipboard : IClipboardMonitor, IClipboardWriter
{
    public event Action<ClipboardReadResult>? ClipboardChanged;

    private readonly System.Timers.Timer _timer = new(400);
    private string _lastHash = "";

    public void Start()
    {
        _timer.Elapsed += (_, _) => Poll();
        _timer.AutoReset = true;
        _timer.Start();
    }

    public void Stop() => _timer.Stop();

    private void Poll()
    {
        try
        {
            var info = RunText("osascript", "-e \"clipboard info\"");
            var tokens = info.Split(new[] { ',', '{', '}', ':' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var kind = ClipTypeSelector.PickBest(tokens);
            if (kind is null) return;

            ClipboardReadResult? result = kind switch
            {
                ClipType.Files => ReadFiles(),
                ClipType.Image => ReadImage(),
                ClipType.Text => ReadText(),
                _ => null
            };
            if (result is null) return;

            var hash = result.Type switch
            {
                ClipType.Image => ClipHasher.HashBytes(result.ImagePng ?? Array.Empty<byte>()),
                ClipType.Files => ClipHasher.HashText(string.Join("\n", result.FilePaths ?? new List<string>())),
                _ => ClipHasher.HashText(result.Text ?? "")
            };
            if (hash == _lastHash) return;
            _lastHash = hash;
            ClipboardChanged?.Invoke(result);
        }
        catch { /* unreadable — skip */ }
    }

    private ClipboardReadResult ReadText() =>
        new(ClipType.Text, RunText("pbpaste", ""), null, null, null);

    private ClipboardReadResult? ReadImage()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"cv-clip-{Guid.NewGuid():N}.png");
        // Dump the clipboard PNG to a temp file via AppleScript, then read it.
        var script =
            $"-e \"set thePng to (the clipboard as «class PNGf»)\" " +
            $"-e \"set f to open for access POSIX file \\\"{tmp}\\\" with write permission\" " +
            $"-e \"write thePng to f\" -e \"close access f\"";
        RunText("osascript", script);
        if (!File.Exists(tmp)) return null;
        try
        {
            var bytes = File.ReadAllBytes(tmp);
            return new ClipboardReadResult(ClipType.Image, null, bytes, null, null);
        }
        finally { try { File.Delete(tmp); } catch { } }
    }

    private ClipboardReadResult ReadFiles()
    {
        // Returns newline-separated POSIX paths for all file URLs on the pasteboard.
        var script =
            "-e \"set out to \\\"\\\"\" " +
            "-e \"repeat with f in (the clipboard as «class furl»)\" " +
            "-e \"set out to out & POSIX path of f & linefeed\" " +
            "-e \"end repeat\" -e \"return out\"";
        var raw = RunText("osascript", script);
        var paths = raw.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (paths.Length == 0)
        {
            // Single-file clipboards aren't a list; fall back to single path.
            var single = RunText("osascript", "-e \"POSIX path of (the clipboard as «class furl»)\"").Trim();
            if (!string.IsNullOrEmpty(single)) paths = new[] { single };
        }
        return new ClipboardReadResult(ClipType.Files, null, null, paths, null);
    }

    public void Write(ClipItem item)
    {
        try
        {
            switch (item.Type)
            {
                case ClipType.Text when item.TextContent is not null:
                    PipeIn("pbcopy", "", Encoding.UTF8.GetBytes(item.TextContent));
                    break;
                case ClipType.Image when item.BlobPath is not null && File.Exists(item.BlobPath):
                    RunText("osascript",
                        $"-e \"set the clipboard to (read (POSIX file \\\"{item.BlobPath}\\\") as «class PNGf»)\"");
                    break;
                case ClipType.Files when item.FilePaths is not null:
                    var paths = JsonSerializer.Deserialize<string[]>(item.FilePaths) ?? Array.Empty<string>();
                    PipeIn("pbcopy", "", Encoding.UTF8.GetBytes(string.Join(Environment.NewLine, paths)));
                    break;
            }
        }
        catch { /* swallow */ }
    }

    private static string RunText(string exe, string args)
    {
        var psi = new ProcessStartInfo(exe, args)
        { RedirectStandardOutput = true, UseShellExecute = false };
        using var p = Process.Start(psi)!;
        var o = p.StandardOutput.ReadToEnd();
        p.WaitForExit(2000);
        return o;
    }

    private static void PipeIn(string exe, string args, byte[] data)
    {
        var psi = new ProcessStartInfo(exe, args)
        { RedirectStandardInput = true, UseShellExecute = false };
        using var p = Process.Start(psi)!;
        p.StandardInput.BaseStream.Write(data, 0, data.Length);
        p.StandardInput.Close();
        p.WaitForExit(2000);
    }

    public void Dispose() => Stop();
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build src/ClipVault.Platform`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/ClipVault.Platform/MacOS/MacClipboard.cs
git commit -m "feat(platform): macOS clipboard text+image+files via osascript"
```

> Manual smoke (on macOS, after Task 5 build): copy text, an image (e.g.
> screenshot), and files in Finder; open ClipVault history; confirm each appears;
> paste back text and image. Grant Accessibility permission for the hotkey.

---

## Task 5: Update README + full regression

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Update the platform support table**

Replace the support table so all three rows read ✅ for Text, Images, and Files:
```markdown
| OS | Text | Images | Files |
|----|------|--------|-------|
| Windows | ✅ | ✅ | ✅ (pastes as paths) |
| Linux | ✅ | ✅ | ✅ (pastes as paths) |
| macOS | ✅ | ✅ | ✅ (pastes as paths) |
```
Remove the line that said image/file capture on Linux/macOS is a planned follow-up.

- [ ] **Step 2: Run the full test suite**

Run: `dotnet test`
Expected: all pass, including `UriListParserTests` and `ClipTypeSelectorTests`.

- [ ] **Step 3: Build the whole solution**

Run: `dotnet build`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add README.md
git commit -m "docs: full clipboard parity across Windows/Linux/macOS"
```

---

## Self-Review Notes

- **Spec coverage:** richest-type selection (`ClipTypeSelector`, Task 2),
  uri-list parsing (`UriListParser`, Task 1), Linux text/image/files capture +
  write (Task 3), macOS text/image/files capture + write (Task 4), all-type
  change detection via content hash (Tasks 3–4), README parity table (Task 5).
  Files-paste-back-as-paths parity is implemented in both Write methods.
- **Deliberate constraints (from spec):** CLI approach (no native P/Invoke);
  files pasted back as paths; Windows untouched.
- **Type consistency:** `ClipboardReadResult(Type, Text, ImagePng, FilePaths,
  SourceApp)`, `ClipType` enum, `ClipHasher.HashText/HashBytes`,
  `UriListParser.Parse`, `ClipTypeSelector.PickBest` are used consistently with
  their definitions and the existing Core contracts.
- **Testing reality:** Tasks 1–2 are TDD and run here. Tasks 3–4 are
  compile-only in this environment; the inline manual-smoke notes tell the user
  exactly what to verify on Linux/macOS.
```
