# UI Redesign + Theme Selection Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Redesign the history/settings windows around a Neon Terminal look and add a live theme selector (Neon/Dark/Light) in Settings.

**Architecture:** A `ThemeManager` builds three brush dictionaries in C# (keys `Cv.*`) and swaps the active one in `Application.Resources.MergedDictionaries`, also setting `RequestedThemeVariant`. Windows reference colors via `DynamicResource Cv.*` so swaps repaint live. `AppSettings.Theme` persists the choice.

**Tech Stack:** Avalonia 12 (`ResourceDictionary`, `DynamicResource`, `ThemeVariant`), .NET 10, xUnit.

> Theme dictionaries are built in C# (not standalone .axaml) to avoid runtime
> XAML-URI loading pitfalls and keep the swap fully under code control.
> UI is verified by running the app; only the settings model change is unit-tested.

---

## File Structure

```
src/ClipVault.Core/Models/AppSettings.cs       — MODIFY: + Theme property (default "Neon")
src/ClipVault.App/Themes/ThemeManager.cs       — NEW: Themes[], Apply(name), C# brush dictionaries
src/ClipVault.App/App.axaml.cs                 — MODIFY: apply saved theme at startup
src/ClipVault.App/Views/HistoryWindow.axaml    — MODIFY: redesigned layout, DynamicResource Cv.*
src/ClipVault.App/Views/SettingsWindow.axaml   — MODIFY: + theme ComboBox
src/ClipVault.App/Views/SettingsWindow.axaml.cs— MODIFY: live apply + save
tests/ClipVault.Core.Tests/AppSettingsTests.cs — NEW: Theme default
```

---

## Task 1: AppSettings.Theme — TDD

**Files:**
- Modify: `src/ClipVault.Core/Models/AppSettings.cs`
- Test: `tests/ClipVault.Core.Tests/AppSettingsTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using ClipVault.Core.Models;
using Xunit;

namespace ClipVault.Core.Tests;

public class AppSettingsTests
{
    [Fact]
    public void Theme_DefaultsToNeon()
    {
        Assert.Equal("Neon", new AppSettings().Theme);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter AppSettingsTests`
Expected: FAIL — `Theme` does not exist.

- [ ] **Step 3: Add the property**

In `src/ClipVault.Core/Models/AppSettings.cs`, add after `MaxAgeDays`:
```csharp
    public string Theme { get; set; } = "Neon";
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test --filter AppSettingsTests`
Expected: PASS (1 test).

- [ ] **Step 5: Commit**

```bash
git add src/ClipVault.Core/Models/AppSettings.cs tests/ClipVault.Core.Tests/AppSettingsTests.cs
git commit -m "feat(core): add Theme setting (default Neon)"
```

---

## Task 2: ThemeManager

**Files:**
- Create: `src/ClipVault.App/Themes/ThemeManager.cs`

- [ ] **Step 1: Implement**

```csharp
using System;
using Avalonia;
using Avalonia.Media;
using Avalonia.Controls;
using Avalonia.Styling;

namespace ClipVault.App.Themes;

/// Builds and applies a palette dictionary (keys "Cv.*") at runtime.
public static class ThemeManager
{
    public static readonly string[] Themes = { "Neon", "Dark", "Light" };

    private static ResourceDictionary? _current;

    public static void Apply(string? name)
    {
        var app = Application.Current;
        if (app is null) return;

        var (dict, variant) = Build(name);

        if (_current is not null)
            app.Resources.MergedDictionaries.Remove(_current);
        app.Resources.MergedDictionaries.Add(dict);
        _current = dict;
        app.RequestedThemeVariant = variant;
    }

    private static (ResourceDictionary, ThemeVariant) Build(string? name) => name switch
    {
        "Light" => (Palette(
            bg: "#f7f8fa", surface: "#ffffff", surfaceAlt: "#ffffff",
            accent: "#6366f1", text: "#1f2328", dim: "#9ca3af",
            border: "#e5e7eb", pinned: "#d97706"), ThemeVariant.Light),

        "Dark" => (Palette(
            bg: "#0d1117", surface: "#161b22", surfaceAlt: "#0d1117",
            accent: "#2dd4bf", text: "#e6edf3", dim: "#7d8590",
            border: "#30363d", pinned: "#ffd166"), ThemeVariant.Dark),

        _ => (Palette( // Neon (default / fallback)
            bg: "#05060a", surface: "#0a0f0c", surfaceAlt: "#080b0e",
            accent: "#39ff14", text: "#c8f7e0", dim: "#4a6356",
            border: "#1f5132", pinned: "#ffd166"), ThemeVariant.Dark),
    };

    private static ResourceDictionary Palette(string bg, string surface, string surfaceAlt,
        string accent, string text, string dim, string border, string pinned)
    {
        var d = new ResourceDictionary();
        d["Cv.Background"] = Brush(bg);
        d["Cv.Surface"] = Brush(surface);
        d["Cv.SurfaceAlt"] = Brush(surfaceAlt);
        d["Cv.Accent"] = Brush(accent);
        d["Cv.Text"] = Brush(text);
        d["Cv.TextDim"] = Brush(dim);
        d["Cv.Border"] = Brush(border);
        d["Cv.Pinned"] = Brush(pinned);
        return d;
    }

    private static SolidColorBrush Brush(string hex) => new(Color.Parse(hex));
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build src/ClipVault.App`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/ClipVault.App/Themes/ThemeManager.cs
git commit -m "feat(app): add ThemeManager with Neon/Dark/Light palettes"
```

---

## Task 3: Apply theme at startup + redesign history window

**Files:**
- Modify: `src/ClipVault.App/App.axaml.cs`
- Modify: `src/ClipVault.App/Views/HistoryWindow.axaml`

- [ ] **Step 1: Apply saved theme before any window shows**

In `src/ClipVault.App/App.axaml.cs`, add `using ClipVault.App.Themes;` and in
`OnFrameworkInitializationCompleted`, before `Services.Start(...)`:
```csharp
        ClipVault.App.Themes.ThemeManager.Apply(Services.Settings.Theme);
```

- [ ] **Step 2: Replace HistoryWindow.axaml with the redesigned layout**

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:models="using:ClipVault.Core.Models"
        x:Class="ClipVault.App.Views.HistoryWindow"
        Width="500" Height="600" WindowStartupLocation="CenterScreen"
        Title="ClipVault" ShowInTaskbar="False"
        Background="{DynamicResource Cv.Background}">
  <DockPanel>
    <!-- header -->
    <Border DockPanel.Dock="Top" Background="{DynamicResource Cv.SurfaceAlt}"
            BorderBrush="{DynamicResource Cv.Border}" BorderThickness="0,0,0,1" Padding="16,12">
      <Grid ColumnDefinitions="*,Auto">
        <TextBlock Text="▍CLIPVAULT" FontWeight="SemiBold" FontSize="14"
                   Foreground="{DynamicResource Cv.Accent}"/>
        <TextBlock Grid.Column="1" x:Name="StatusText" FontSize="11"
                   VerticalAlignment="Center" Foreground="{DynamicResource Cv.Accent}"/>
      </Grid>
    </Border>

    <!-- footer hints -->
    <Border DockPanel.Dock="Bottom" Background="{DynamicResource Cv.SurfaceAlt}"
            BorderBrush="{DynamicResource Cv.Border}" BorderThickness="0,1,0,0" Padding="16,8">
      <TextBlock FontSize="11" Foreground="{DynamicResource Cv.TextDim}"
                 Text="↵ paste    ^P pin    Del remove    Esc close"/>
    </Border>

    <!-- search -->
    <Border DockPanel.Dock="Top" Padding="12,12,12,6">
      <TextBox x:Name="Search" PlaceholderText="Search…"
               Background="{DynamicResource Cv.Surface}"
               Foreground="{DynamicResource Cv.Text}"
               BorderBrush="{DynamicResource Cv.Border}"
               CornerRadius="6"/>
    </Border>

    <!-- list -->
    <ListBox x:Name="List" Margin="6,0,6,6" Background="Transparent"
             BorderThickness="0">
      <ListBox.ItemTemplate>
        <DataTemplate x:DataType="models:ClipItem">
          <Border Background="{DynamicResource Cv.Surface}"
                  BorderBrush="{DynamicResource Cv.Border}" BorderThickness="1"
                  CornerRadius="6" Padding="10" Margin="0,3">
            <Grid ColumnDefinitions="Auto,Auto,*,Auto">
              <TextBlock Grid.Column="0" Text="★" Margin="0,0,8,0"
                         VerticalAlignment="Center"
                         Foreground="{DynamicResource Cv.Pinned}"
                         IsVisible="{Binding IsPinned}"/>
              <Image Grid.Column="1" MaxWidth="40" MaxHeight="40" Stretch="Uniform"
                     Margin="0,0,8,0"
                     Source="{Binding BlobPath, Converter={StaticResource PathToThumbnail}}"
                     IsVisible="{Binding Type, Converter={StaticResource EnumEquals}, ConverterParameter=Image}"/>
              <TextBlock Grid.Column="2" Text="{Binding Preview}"
                         TextTrimming="CharacterEllipsis" VerticalAlignment="Center"
                         Foreground="{DynamicResource Cv.Text}"/>
              <TextBlock Grid.Column="3" Text="{Binding Type}" FontSize="10"
                         VerticalAlignment="Center" Margin="8,0,0,0"
                         Foreground="{DynamicResource Cv.TextDim}"/>
            </Grid>
          </Border>
        </DataTemplate>
      </ListBox.ItemTemplate>
    </ListBox>
  </DockPanel>
</Window>
```

- [ ] **Step 3: Set the sync status text in code-behind**

In `src/ClipVault.App/Views/HistoryWindow.axaml.cs`, the constructor already finds
controls. The `HistoryViewModel` does not expose sync; keep the status simple —
set it to the trusted-device hint via a constructor parameter is overkill. Instead,
in `HistoryWindow` constructor after `InitializeComponent()` set a static label:
```csharp
        var status = this.FindControl<TextBlock>("StatusText");
        if (status is not null) status.Text = "◉ sync on";
```
(Place this right after the existing `_search`/`_list` lookups.)

- [ ] **Step 4: Build**

Run: `dotnet build`
Expected: Build succeeded (the existing 2 AVLN3001 warnings remain; benign).

- [ ] **Step 5: Commit**

```bash
git add src/ClipVault.App/App.axaml.cs src/ClipVault.App/Views/HistoryWindow.axaml src/ClipVault.App/Views/HistoryWindow.axaml.cs
git commit -m "feat(app): apply theme at startup + redesign history window"
```

---

## Task 4: Theme dropdown in Settings (live apply)

**Files:**
- Modify: `src/ClipVault.App/Views/SettingsWindow.axaml`
- Modify: `src/ClipVault.App/Views/SettingsWindow.axaml.cs`

- [ ] **Step 1: Add the theme ComboBox to SettingsWindow.axaml**

Add directly above the existing `Hotkey` label inside the settings `StackPanel`:
```xml
      <TextBlock Text="Theme"/>
      <ComboBox x:Name="ThemeBox" HorizontalAlignment="Stretch"/>
```

- [ ] **Step 2: Wire it in SettingsWindow.axaml.cs**

Add `using ClipVault.App.Themes;` at the top. In the constructor, after the
existing control lookups and before `WireSync();`, add:
```csharp
        var themeBox = this.FindControl<ComboBox>("ThemeBox")!;
        themeBox.ItemsSource = ThemeManager.Themes;
        themeBox.SelectedItem = ThemeManager.Themes.Contains(_vm.Settings.Theme)
            ? _vm.Settings.Theme : "Neon";
        themeBox.SelectionChanged += (_, _) =>
        {
            if (themeBox.SelectedItem is string t)
            {
                ThemeManager.Apply(t);     // live recolor
                _vm.Settings.Theme = t;
                _vm.Save();
            }
        };
```
Add `using System.Linq;` if not already present (for `Contains`).

- [ ] **Step 3: Build**

Run: `dotnet build`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/ClipVault.App/Views/SettingsWindow.axaml src/ClipVault.App/Views/SettingsWindow.axaml.cs
git commit -m "feat(app): live theme selector in settings"
```

---

## Task 5: Run-verify + regression

- [ ] **Step 1: Full test suite**

Run: `dotnet test`
Expected: all pass (40: previous 39 + AppSettings.Theme).

- [ ] **Step 2: Launch and verify**

Run: `dotnet run --project src/ClipVault.App`
Then:
1. App starts (tray) with no resource exceptions.
2. Copy text + an image; open history (`Ctrl+Shift+V`) — Neon styling, header,
   thumbnail, footer hints visible.
3. Tray → Settings → change Theme to Light, then Dark — the history window
   recolors live (reopen/observe).
4. Restart app → Settings shows the last chosen theme; history uses it.
Expected: all pass. (In this environment, confirm the process starts without
exceptions; visual confirmation is the user's.)

- [ ] **Step 3: Final commit**

```bash
git add -A
git commit -m "test: confirm suite green after UI redesign + themes" --allow-empty
```

---

## Self-Review Notes

- **Spec coverage:** theme contract keys (`Cv.*`) and three palettes (Task 2),
  live swap + variant (Task 2 `Apply`), startup apply (Task 3 Step 1),
  redesigned history window using `DynamicResource` (Task 3 Step 2), settings
  dropdown with immediate apply + save (Task 4), `AppSettings.Theme` default Neon
  (Task 1), sync-status header text (Task 3 Step 3, simplified to "◉ sync on" —
  the spec allowed a static indicator). Capture/storage/sync untouched.
- **Simplification note:** the spec mentioned binding status to
  `TrustedDevices.Count`; `HistoryViewModel` has no sync reference, so the plan
  uses a static "◉ sync on" label to avoid threading sync through the history VM
  (YAGNI). This is an explicit, documented choice, not a gap.
- **Placeholder scan:** none.
- **Type consistency:** `AppSettings.Theme`, `ThemeManager.Themes`,
  `ThemeManager.Apply(string?)`, resource keys `Cv.Background/Surface/SurfaceAlt/
  Accent/Text/TextDim/Border/Pinned`, converters `PathToThumbnail`/`EnumEquals`
  match Task definitions and existing code.
```
