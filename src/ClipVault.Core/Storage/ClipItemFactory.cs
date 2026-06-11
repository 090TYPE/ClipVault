using System.Text.Json;
using ClipVault.Core.Abstractions;
using ClipVault.Core.Hashing;
using ClipVault.Core.Models;

namespace ClipVault.Core.Storage;

public class ClipItemFactory
{
    private readonly string _blobDir;

    public ClipItemFactory(string blobDir)
    {
        _blobDir = blobDir;
        Directory.CreateDirectory(_blobDir);
    }

    public ClipItem Create(ClipboardReadResult r)
    {
        var item = new ClipItem { Type = r.Type, SourceApp = r.SourceApp };

        switch (r.Type)
        {
            case ClipType.Text:
                item.TextContent = r.Text;
                item.Preview = Truncate(r.Text ?? "");
                item.Hash = ClipHasher.HashText(r.Text ?? "");
                break;

            case ClipType.Image:
                var bytes = r.ImagePng ?? Array.Empty<byte>();
                item.BlobPath = Path.Combine(_blobDir, $"{item.Id}.png");
                File.WriteAllBytes(item.BlobPath, bytes);
                item.Preview = "[image]";
                item.Hash = ClipHasher.HashBytes(bytes);
                break;

            case ClipType.Files:
                var paths = r.FilePaths ?? Array.Empty<string>();
                item.FilePaths = JsonSerializer.Serialize(paths);
                item.Preview = string.Join(", ", paths.Select(Path.GetFileName));
                item.Hash = ClipHasher.HashText(item.FilePaths);
                break;
        }

        return item;
    }

    private static string Truncate(string s) =>
        s.Length <= 120 ? s : s[..120] + "…";
}
