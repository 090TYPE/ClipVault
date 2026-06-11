using ClipVault.Sync.Crypto;
using ClipVault.Sync.Pairing;
using Xunit;

namespace ClipVault.Core.Tests;

public class PairingProtocolTests
{
    [Fact]
    public void Confirmation_MatchesAcrossParties_WithSamePin()
    {
        var initiator = new PairingHandshake();
        var responder = new PairingHandshake();

        var keyI = initiator.DeriveKey(responder.PublicKey, "424242");
        var keyR = responder.DeriveKey(initiator.PublicKey, "424242");

        var confI = PairingProtocol.Confirmation(keyI);
        var confR = PairingProtocol.Confirmation(keyR);
        Assert.Equal(confI, confR);
    }

    [Fact]
    public void Confirmation_DiffersWithWrongPin()
    {
        var initiator = new PairingHandshake();
        var responder = new PairingHandshake();
        var keyI = initiator.DeriveKey(responder.PublicKey, "000000");
        var keyR = responder.DeriveKey(initiator.PublicKey, "999999");
        Assert.NotEqual(PairingProtocol.Confirmation(keyI), PairingProtocol.Confirmation(keyR));
    }
}
