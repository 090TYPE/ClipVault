using System.Security.Cryptography;
using System.Text;

namespace ClipVault.Sync.Crypto;

/// ECDH P-256 key agreement, with the PIN folded into HKDF so that only
/// parties using the same PIN derive the same key.
public class PairingHandshake : IDisposable
{
    private readonly ECDiffieHellman _ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);

    /// Our public key as SubjectPublicKeyInfo (DER) bytes, to send to the peer.
    public byte[] PublicKey => _ecdh.PublicKey.ExportSubjectPublicKeyInfo();

    public byte[] DeriveKey(byte[] peerPublicKey, string pin)
    {
        using var peer = ECDiffieHellman.Create();
        peer.ImportSubjectPublicKeyInfo(peerPublicKey, out _);
        var shared = _ecdh.DeriveRawSecretAgreement(peer.PublicKey);
        return HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            ikm: shared,
            outputLength: 32,
            salt: Encoding.UTF8.GetBytes(pin),
            info: Encoding.UTF8.GetBytes("ClipVault-pairing-v1"));
    }

    public void Dispose() => _ecdh.Dispose();
}
