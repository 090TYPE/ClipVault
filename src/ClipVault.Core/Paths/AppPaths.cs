namespace ClipVault.Core.Paths;

public static class AppPaths
{
    public static string DataDir { get; } = ResolveDataDir();
    public static string DbPath => Path.Combine(DataDir, "clipvault.db");
    public static string BlobDir => Path.Combine(DataDir, "blobs");
    public static string LogDir => Path.Combine(DataDir, "logs");

    private static string ResolveDataDir()
    {
        // SpecialFolder.ApplicationData maps to:
        //   Windows: %APPDATA%
        //   Linux:   ~/.config
        //   macOS:   ~/Library/Application Support
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(baseDir, "ClipVault");
        Directory.CreateDirectory(dir);
        Directory.CreateDirectory(Path.Combine(dir, "blobs"));
        Directory.CreateDirectory(Path.Combine(dir, "logs"));
        return dir;
    }
}
