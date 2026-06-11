using ClipVault.Core.Models;
using Xunit;

namespace ClipVault.Core.Tests;

public class AppSettingsTests
{
    [Fact]
    public void Theme_DefaultsToNeon()
    {
        Assert.Equal("Neon", new AppSettings().Theme);
    }
}
