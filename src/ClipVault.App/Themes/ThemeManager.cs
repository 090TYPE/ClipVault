using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
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
