using ClipVault.Sync.Crypto;
using Xunit;

namespace ClipVault.Core.Tests;

public class PairingHandshakeTests
{
    [Fact]
    public void BothParties_SamePin_DeriveSameKey()
    {
        var a = new PairingHandshake();
        var b = new PairingHandshake();
        var keyA = a.DeriveKey(b.PublicKey, "123456");
        var keyB = b.DeriveKey(a.PublicKey, "123456");
        Assert.Equal(keyA, keyB);
        Assert.Equal(32, keyA.Length);
    }

    [Fact]
    public void DifferentPins_DeriveDifferentKeys()
    {
        var a = new PairingHandshake();
        var b = new PairingHandshake();
        var keyA = a.DeriveKey(b.PublicKey, "111111");
        var keyB = b.DeriveKey(a.PublicKey, "222222");
        Assert.NotEqual(keyA, keyB);
    }
}
