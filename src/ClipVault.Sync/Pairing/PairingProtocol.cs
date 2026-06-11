using System.Security.Cryptography;
using System.Text;

namespace ClipVault.Sync.Pairing;

/// Helpers for the PIN pairing exchange. The "confirmation" is a value both
/// sides can compute from the derived key to prove they used the same PIN,
/// without revealing the key.
public static class PairingProtocol
{
    public static string Confirmation(byte[] derivedKey)
    {
        var h = SHA256.HashData(Concat(derivedKey, Encoding.UTF8.GetBytes("confirm")));
        return Convert.ToHexString(h);
    }

    private static byte[] Concat(byte[] a, byte[] b)
    {
        var r = new byte[a.Length + b.Length];
        Buffer.BlockCopy(a, 0, r, 0, a.Length);
        Buffer.BlockCopy(b, 0, r, a.Length, b.Length);
        return r;
    }
}
