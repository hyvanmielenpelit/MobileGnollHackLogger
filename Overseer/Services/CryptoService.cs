using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace Overseer.Services;

public class CryptoService
{
    private readonly byte[] _masterKey;

    public CryptoService(IConfiguration configuration)
    {
        var keyBase64 = configuration["Overseer:AesEncryptionKey"];
        if (string.IsNullOrEmpty(keyBase64))
        {
            // Fallback for development if not provided, though it's dangerous for prod
            // In a real app, you'd throw an exception. We'll throw one here to ensure it's configured.
            throw new InvalidOperationException("Overseer:AesEncryptionKey is not configured.");
        }
        _masterKey = Convert.FromBase64String(keyBase64);
        if (_masterKey.Length != 32)
        {
            throw new InvalidOperationException("Overseer:AesEncryptionKey must be a 256-bit (32 bytes) key.");
        }
    }

    public (string ciphertext, string nonce, string tag) Encrypt(string plaintext, string userId)
    {
        if (string.IsNullOrEmpty(plaintext)) return (string.Empty, string.Empty, string.Empty);

        byte[] plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        byte[] nonceBytes = new byte[12];
        RandomNumberGenerator.Fill(nonceBytes);

        byte[] ciphertextBytes = new byte[plaintextBytes.Length];
        byte[] tagBytes = new byte[16];
        byte[] associatedData = Encoding.UTF8.GetBytes(userId);

        using (var aesGcm = new AesGcm(_masterKey, tagBytes.Length))
        {
            aesGcm.Encrypt(nonceBytes, plaintextBytes, ciphertextBytes, tagBytes, associatedData);
        }

        return (
            Convert.ToBase64String(ciphertextBytes),
            Convert.ToBase64String(nonceBytes),
            Convert.ToBase64String(tagBytes)
        );
    }

    public string Decrypt(string ciphertext, string nonce, string tag, string userId)
    {
        if (string.IsNullOrEmpty(ciphertext)) return string.Empty;

        byte[] ciphertextBytes = Convert.FromBase64String(ciphertext);
        byte[] nonceBytes = Convert.FromBase64String(nonce);
        byte[] tagBytes = Convert.FromBase64String(tag);
        byte[] associatedData = Encoding.UTF8.GetBytes(userId);

        byte[] plaintextBytes = new byte[ciphertextBytes.Length];

        using (var aesGcm = new AesGcm(_masterKey, tagBytes.Length))
        {
            aesGcm.Decrypt(nonceBytes, ciphertextBytes, tagBytes, plaintextBytes, associatedData);
        }

        return Encoding.UTF8.GetString(plaintextBytes);
    }
}
