namespace ClipVault.Core.Models;

public enum ClipType { Text, Image, Files }

public class ClipItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public ClipType Type { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsPinned { get; set; }
    public string? SourceApp { get; set; }

    public string? TextContent { get; set; }   // for Text
    public string? Preview { get; set; }        // short text preview for the list
    public string? BlobPath { get; set; }       // PNG path on disk, for Image
    public string? FilePaths { get; set; }      // JSON array of paths, for Files

    public string Hash { get; set; } = string.Empty; // SHA-256 for dedup
}
