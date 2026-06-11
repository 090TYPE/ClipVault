using System.Security.Cryptography;
using System.Text;

namespace ClipVault.Core.Hashing;

public static class ClipHasher
{
    public static string HashText(string text) =>
        HashBytes(Encoding.UTF8.GetBytes(text));

    public static string HashBytes(byte[] data) =>
        Convert.ToHexString(SHA256.HashData(data));
}
