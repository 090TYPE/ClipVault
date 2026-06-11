using ClipVault.Core.Models;
using ClipVault.Platform.Shared;
using Xunit;

namespace ClipVault.Core.Tests;

public class ClipTypeSelectorTests
{
    [Fact]
    public void Files_BeatImageAndText()
    {
        var t = ClipTypeSelector.PickBest(new[] { "text/uri-list", "image/png", "text/plain" });
        Assert.Equal(ClipType.Files, t);
    }

    [Fact]
    public void Image_BeatsText()
    {
        Assert.Equal(ClipType.Image, ClipTypeSelector.PickBest(new[] { "image/png", "text/plain" }));
    }

    [Fact]
    public void Text_WhenOnlyText()
    {
        Assert.Equal(ClipType.Text, ClipTypeSelector.PickBest(new[] { "text/plain", "UTF8_STRING" }));
    }

    [Fact]
    public void MacOsClassTokens_Recognized()
    {
        Assert.Equal(ClipType.Files, ClipTypeSelector.PickBest(new[] { "«class furl»", "«class utf8»" }));
        Assert.Equal(ClipType.Image, ClipTypeSelector.PickBest(new[] { "«class PNGf»" }));
        Assert.Equal(ClipType.Text, ClipTypeSelector.PickBest(new[] { "«class utf8»" }));
    }

    [Fact]
    public void None_WhenNothingRecognized()
    {
        Assert.Null(ClipTypeSelector.PickBest(new[] { "application/x-weird" }));
        Assert.Null(ClipTypeSelector.PickBest(Array.Empty<string>()));
    }
}
