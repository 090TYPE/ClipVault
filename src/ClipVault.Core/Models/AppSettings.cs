using System.Text.Json;
using ClipVault.Core.Paths;

namespace ClipVault.Core.Models;

public class AppSettings
{
    public string Hotkey { get; set; } = "Ctrl+Shift+V";
    public int MaxUnpinned { get; set; } = 500;
    public int MaxAgeDays { get; set; } = 30;
    public string Theme { get; set; } = "Neon";

    private static string FilePath => Path.Combine(AppPaths.DataDir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath))
                       ?? new AppSettings();
        }
        catch { /* corrupt settings → defaults */ }
        return new AppSettings();
    }

    public void Save() =>
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
}
