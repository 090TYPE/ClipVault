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
