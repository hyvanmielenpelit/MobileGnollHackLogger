using System.Security.Cryptography;
using System.Text;
using MobileGnollHackLogger.Data;
using Overseer.Services.Privacy;

namespace Overseer.Services.Security;

/// <summary>
/// Seals and opens the game text the snapshot guard keeps in <see cref="SnapshotGuardQuarantine"/>: AES-GCM under
/// the content keyring's active version, bound to the user by its associated data.
/// </summary>
/// <remarks>
/// Content belongs under the versioned keyring, and <c>AesEncryptionKey</c> stays API-key material only. A row lives
/// at most the quarantine's retention, so rotating a version means waiting out that window before retiring it, and a
/// per-row data key would add a wrap for no gain.
/// </remarks>
public static class SnapshotQuarantineCipher
{
    /// <summary>The associated data's prefix; the user id follows it.</summary>
    public const string AssociatedDataPrefix = "snapshot-quarantine:v1:";

    private const int NonceBytes = 12;
    private const int TagBytes = 16;

    /// <summary>Encrypts <paramref name="text"/> for <paramref name="userId"/> under the ring's active version.</summary>
    /// <exception cref="KeyNotFoundException">The ring has no active version, or the active version has no key.</exception>
    public static (string KeyVersion, string Ciphertext, string Nonce, string Tag) Seal(IContentKeyRing ring, string userId, string text)
    {
        string version = ring.ActiveVersion;
        if (string.IsNullOrEmpty(version))
        {
            throw new KeyNotFoundException("The content keyring has no active version.");
        }

        byte[] key = ring.GetKey(version);
        byte[] plaintext = Encoding.UTF8.GetBytes(text);
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceBytes);
        byte[] ciphertext = new byte[plaintext.Length];
        byte[] tag = new byte[TagBytes];

        using (var aes = new AesGcm(key, TagBytes))
        {
            aes.Encrypt(nonce, plaintext, ciphertext, tag, AssociatedData(userId));
        }

        return (version, Convert.ToBase64String(ciphertext), Convert.ToBase64String(nonce), Convert.ToBase64String(tag));
    }

    /// <summary>
    /// The row's text, or <see cref="ContentProtectionService.UnreadableNotice"/> when it cannot be decrypted: a retired
    /// key version, another user's associated data, or a corrupt or tampered value. Never throws.
    /// </summary>
    public static string Open(IContentKeyRing ring, SnapshotGuardQuarantine row)
    {
        try
        {
            byte[] key = ring.GetKey(row.KeyVersion);
            byte[] nonce = Convert.FromBase64String(row.Nonce);
            byte[] tag = Convert.FromBase64String(row.Tag);
            byte[] ciphertext = Convert.FromBase64String(row.Ciphertext);
            byte[] plaintext = new byte[ciphertext.Length];

            using (var aes = new AesGcm(key, TagBytes))
            {
                aes.Decrypt(nonce, ciphertext, tag, plaintext, AssociatedData(row.AspNetUserId ?? string.Empty));
            }

            return Encoding.UTF8.GetString(plaintext);
        }
        // ArgumentException covers a nonce or tag of the wrong length and a null column.
        catch (Exception ex) when (ex is CryptographicException or FormatException or KeyNotFoundException or ArgumentException)
        {
            return ContentProtectionService.UnreadableNotice;
        }
    }

    private static byte[] AssociatedData(string userId) => Encoding.UTF8.GetBytes(AssociatedDataPrefix + userId);
}
