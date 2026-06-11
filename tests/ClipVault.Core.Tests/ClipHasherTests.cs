using ClipVault.Core.Hashing;
using Xunit;

namespace ClipVault.Core.Tests;

public class ClipHasherTests
{
    [Fact]
    public void SameText_ProducesSameHash()
    {
        Assert.Equal(ClipHasher.HashText("hello"), ClipHasher.HashText("hello"));
    }

    [Fact]
    public void DifferentText_ProducesDifferentHash()
    {
        Assert.NotEqual(ClipHasher.HashText("hello"), ClipHasher.HashText("world"));
    }

    [Fact]
    public void SameBytes_ProducesSameHash()
    {
        var a = new byte[] { 1, 2, 3 };
        var b = new byte[] { 1, 2, 3 };
        Assert.Equal(ClipHasher.HashBytes(a), ClipHasher.HashBytes(b));
    }
}
