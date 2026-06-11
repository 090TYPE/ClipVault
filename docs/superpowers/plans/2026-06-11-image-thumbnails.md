# Image Thumbnails in History Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Render image history rows as ~64px thumbnails instead of the `[image]` text placeholder.

**Architecture:** Two `IValueConverter`s in the App (`PathToThumbnailConverter`, `EnumEqualsConverter`) registered in `App.axaml`; the `HistoryWindow` row template shows an `Image` for image items and the `Preview` text otherwise. No change to capture/storage/sync.

**Tech Stack:** Avalonia 12 (`Bitmap.DecodeToWidth`, `IValueConverter`), .NET 10.

> No automated tests (XAML/UI); verified by running the app.

---

## File Structure

```
src/ClipVault.App/Converters/AppConverters.cs  — NEW: PathToThumbnailConverter, EnumEqualsConverter
src/ClipVault.App/App.axaml                     — MODIFY: register converters as resources
src/ClipVault.App/Views/HistoryWindow.axaml     — MODIFY: row template shows Image or text
```

---

## Task 1: Converters

**Files:**
- Create: `src/ClipVault.App/Converters/AppConverters.cs`

- [ ] **Step 1: Implement both converters**

```csharp
using System;
using System.Globalization;
using System.IO;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace ClipVault.App.Converters;

/// Loads a downscaled thumbnail Bitmap from a file path; null on any failure.
public class PathToThumbnailConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrEmpty(path) || !File.Exists(path))
            return null;
        try
        {
            using var stream = File.OpenRead(path);
            return Bitmap.DecodeToWidth(stream, 128);
        }
        catch { return null; }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// True when value.ToString() equals the ConverterParameter (XOR Invert).
public class EnumEqualsConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var equal = value?.ToString() == parameter?.ToString();
        return equal ^ Invert;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build src/ClipVault.App`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/ClipVault.App/Converters/AppConverters.cs
git commit -m "feat(app): add thumbnail and enum-equals converters"
```

---

## Task 2: Register converters and update the row template

**Files:**
- Modify: `src/ClipVault.App/App.axaml`
- Modify: `src/ClipVault.App/Views/HistoryWindow.axaml`

- [ ] **Step 1: Register converters in App.axaml**

Add the namespace to the `<Application>` root element:
```xml
xmlns:conv="using:ClipVault.App.Converters"
```
And add resources just before `<Application.Styles>`:
```xml
    <Application.Resources>
        <conv:PathToThumbnailConverter x:Key="PathToThumbnail"/>
        <conv:EnumEqualsConverter x:Key="EnumEquals"/>
        <conv:EnumEqualsConverter x:Key="EnumNotEquals" Invert="True"/>
    </Application.Resources>
```

- [ ] **Step 2: Update the HistoryWindow row template**

Replace the `<DataTemplate>` block inside the `ListBox.ItemTemplate` with:
```xml
        <DataTemplate x:DataType="models:ClipItem">
          <StackPanel Orientation="Horizontal" Spacing="6">
            <TextBlock Text="📌" IsVisible="{Binding IsPinned}" VerticalAlignment="Center"/>
            <Image MaxWidth="64" MaxHeight="64" Stretch="Uniform"
                   Source="{Binding BlobPath, Converter={StaticResource PathToThumbnail}}"
                   IsVisible="{Binding Type, Converter={StaticResource EnumEquals}, ConverterParameter=Image}"/>
            <TextBlock Text="{Binding Preview}" TextTrimming="CharacterEllipsis"
                       VerticalAlignment="Center"
                       IsVisible="{Binding Type, Converter={StaticResource EnumNotEquals}, ConverterParameter=Image}"/>
          </StackPanel>
        </DataTemplate>
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build`
Expected: Build succeeded (the existing 2 AVLN3001 warnings are unchanged/benign).

- [ ] **Step 4: Commit**

```bash
git add src/ClipVault.App/App.axaml src/ClipVault.App/Views/HistoryWindow.axaml
git commit -m "feat(app): show image thumbnails in history rows"
```

---

## Task 3: Run-verify

- [ ] **Step 1: Launch the app and confirm thumbnails**

Run: `dotnet run --project src/ClipVault.App`
Then:
1. Copy an image (e.g. a screenshot to clipboard).
2. Press `Ctrl+Shift+V`.
3. Confirm the image row shows a thumbnail (not `[image]`).
4. Copy some text; confirm text rows still show their text normally.
5. Esc / quit from tray.
Expected: all pass. (If launched in this environment, verify the process starts
without exceptions; full visual confirmation is the user's.)

- [ ] **Step 2: Final regression**

Run: `dotnet test`
Expected: all existing tests still pass (39).

---

## Self-Review Notes

- **Spec coverage:** thumbnail in image rows (Task 2 `Image` + `PathToThumbnail`),
  text/file rows unchanged (Task 2 `EnumNotEquals` keeps `Preview` for non-image),
  missing/corrupt blob → null source, no crash (Task 1 try/catch + File.Exists),
  downscaled decode (`DecodeToWidth(128)`), converters registered (Task 2 Step 1).
- **Placeholder scan:** none.
- **Type consistency:** `ClipItem.BlobPath`, `ClipItem.Type` (`ClipType.Image`),
  `ClipItem.Preview`, `ClipItem.IsPinned` match the entity; resource keys
  `PathToThumbnail`, `EnumEquals`, `EnumNotEquals` match their template usages.
```
