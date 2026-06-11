using System.Security.Cryptography;
using ClipVault.Sync.Crypto;
using Xunit;

namespace ClipVault.Core.Tests;

public class SecureChannelTests
{
    private static byte[] Key() => RandomNumberGenerator.GetBytes(32);

    [Fact]
    public void Encrypt_ThenDecrypt_RoundTrips()
    {
        var ch = new SecureChannel(Key());
        var plain = System.Text.Encoding.UTF8.GetBytes("hello sync");
        var sealed_ = ch.Encrypt(plain);
        Assert.Equal(plain, ch.Decrypt(sealed_));
    }

    [Fact]
    public void Decrypt_TamperedData_Throws()
    {
        var ch = new SecureChannel(Key());
        var sealed_ = ch.Encrypt(new byte[] { 1, 2, 3 });
        sealed_[^1] ^= 0xFF; // flip a tag/ciphertext bit
        Assert.ThrowsAny<CryptographicException>(() => ch.Decrypt(sealed_));
    }

    [Fact]
    public void Decrypt_WrongKey_Throws()
    {
        var sealed_ = new SecureChannel(Key()).Encrypt(new byte[] { 9, 9 });
        Assert.ThrowsAny<CryptographicException>(() => new SecureChannel(Key()).Decrypt(sealed_));
    }
}
