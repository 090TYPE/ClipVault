using ClipVault.Platform.Shared;
using Xunit;

namespace ClipVault.Core.Tests;

public class UriListParserTests
{
    [Fact]
    public void Parses_SingleFileUri()
    {
        var paths = UriListParser.Parse("file:///home/user/file.txt");
        Assert.Single(paths);
        Assert.Equal("/home/user/file.txt", paths[0].Replace('\\', '/'));
    }

    [Fact]
    public void Decodes_PercentEncoding()
    {
        var paths = UriListParser.Parse("file:///home/user/My%20File.txt");
        Assert.Equal("/home/user/My File.txt", paths[0].Replace('\\', '/'));
    }

    [Fact]
    public void Parses_MultipleLines_CrLf_SkipsCommentsAndBlanks()
    {
        var text = "#comment\r\nfile:///a.txt\r\n\r\nfile:///b.txt\r\n";
        var paths = UriListParser.Parse(text).Select(p => p.Replace('\\', '/')).ToArray();
        Assert.Equal(new[] { "/a.txt", "/b.txt" }, paths);
    }

    [Fact]
    public void Ignores_NonFileSchemes()
    {
        var paths = UriListParser.Parse("https://example.com/x\nfile:///c.txt")
            .Select(p => p.Replace('\\', '/')).ToArray();
        Assert.Equal(new[] { "/c.txt" }, paths);
    }

    [Fact]
    public void Empty_ReturnsEmpty()
    {
        Assert.Empty(UriListParser.Parse(""));
    }
}
