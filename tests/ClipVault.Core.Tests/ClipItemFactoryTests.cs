using ClipVault.Core.Abstractions;
using ClipVault.Core.Models;
using ClipVault.Core.Storage;
using Xunit;

namespace ClipVault.Core.Tests;

public class ClipItemFactoryTests : IDisposable
{
    private readonly string _blobDir = Path.Combine(Path.GetTempPath(), $"cv-blobs-{Guid.NewGuid():N}");

    [Fact]
    public void Text_SetsContentAndHash()
    {
        var f = new ClipItemFactory(_blobDir);
        var item = f.Create(new ClipboardReadResult(ClipType.Text, "hi", null, null, "App"));
        Assert.Equal(ClipType.Text, item.Type);
        Assert.Equal("hi", item.TextContent);
        Assert.False(string.IsNullOrEmpty(item.Hash));
    }

    [Fact]
    public void Image_WritesBlobToDisk()
    {
        var f = new ClipItemFactory(_blobDir);
        var png = new byte[] { 1, 2, 3, 4 };
        var item = f.Create(new ClipboardReadResult(ClipType.Image, null, png, null, null));
        Assert.Equal(ClipType.Image, item.Type);
        Assert.NotNull(item.BlobPath);
        Assert.True(File.Exists(item.BlobPath!));
    }

    [Fact]
    public void Files_StoresPathsAsJson()
    {
        var f = new ClipItemFactory(_blobDir);
        var item = f.Create(new ClipboardReadResult(ClipType.Files, null, null,
            new[] { "/a.txt", "/b.txt" }, null));
        Assert.Equal(ClipType.Files, item.Type);
        Assert.Contains("a.txt", item.FilePaths);
    }

    public void Dispose()
    {
        if (Directory.Exists(_blobDir)) Directory.Delete(_blobDir, true);
    }
}
