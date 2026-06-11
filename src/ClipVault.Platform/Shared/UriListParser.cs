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
