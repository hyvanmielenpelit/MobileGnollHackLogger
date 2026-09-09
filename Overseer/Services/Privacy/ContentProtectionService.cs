using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using MobileGnollHackLogger.Data;

namespace Overseer.Services.Privacy;

/// <summary>
/// Envelope encryption for confidential session content: a per-session data encryption key,
/// itself wrapped by a versioned master key.
/// </summary>
/// <remarks>
/// <para>
/// **Why a per-session DEK rather than encrypting rows under the master key.** Rotating the
/// master key then re-wraps **one row per session** and rewrites no content at all. It also
/// makes crypto-shredding a single-column update: null the wrapped DEK and the session's rows
/// are unreadable whatever else survives.
/// </para>
/// <para>
/// **The row prefix carries a FORMAT version, not a key version.** The key version lives on the
/// session beside the wrapped DEK. Putting it in the row would imply rotation had to rewrite
/// every row, which is exactly the cost this design avoids.
/// </para>
/// <para>
/// **The prefix is load-bearing because a session can be upgraded.** A normal chat that becomes
/// confidential legitimately contains both plaintext and encrypted rows, so every row declares
/// its own state. A per-session boolean could not express that, and would read a plaintext row
/// as ciphertext or the reverse.
/// </para>
/// </remarks>
public class ContentProtectionService
{
    /// <summary>Row envelope prefix. The digit is the envelope format, not the key version.</summary>
    public const string RowPrefix = "enc:v1:";

    /// <summary>Magic bytes at the head of an encrypted attachment file.</summary>
    private static readonly byte[] FileMagic = Encoding.ASCII.GetBytes("ENC1");

    private const int NonceBytes = 12;
    private const int TagBytes = 16;
    private const int DekBytes = 32;

    /// <summary>Suffix for an encrypted attachment on disk.</summary>
    public const string EncryptedFileSuffix = ".enc";

    private readonly IContentKeyRing _keyRing;
    private readonly ILogger<ContentProtectionService>? _logger;

    /* Unwrapped DEKs, keyed by session id. Scoped to this instance, which is registered scoped
       -- so the cache lives for one request or one turn and dies with it, rather than holding
       plaintext key material for the process lifetime. Prompt assembly decrypts every past
       message in a session, so without it a long history would unwrap the same DEK hundreds of
       times per turn. */
    private readonly ConcurrentDictionary<long, byte[]> _dekCache = new();

    public ContentProtectionService(IContentKeyRing keyRing, ILogger<ContentProtectionService>? logger = null)
    {
        _keyRing = keyRing;
        _logger = logger;
    }

    /// <summary>Whether a value is an envelope this service wrote.</summary>
    public static bool IsEncrypted(string? value)
        => value != null && value.StartsWith(RowPrefix, StringComparison.Ordinal);

    /// <summary>
    /// Ensures the session has a DEK, creating and wrapping one on first use.
    /// </summary>
    /// <remarks>
    /// Called before the first encrypt of a session. The caller saves the entity; this only
    /// sets the columns, so creating the DEK and persisting the content it protects happen in
    /// one transaction — a DEK saved without its content is harmless, content saved without its
    /// DEK is unreadable.
    /// </remarks>
    public void EnsureSessionKey(ChatSession session)
    {
        if (session.EncryptedContentKey != null && session.ContentKeyNonce != null
            && session.ContentKeyTag != null && session.ContentKeyVersion != null)
        {
            return;
        }

        byte[] dek = RandomNumberGenerator.GetBytes(DekBytes);
        string version = _keyRing.ActiveVersion;
        byte[] masterKey = _keyRing.GetKey(version);

        byte[] nonce = RandomNumberGenerator.GetBytes(NonceBytes);
        byte[] wrapped = new byte[dek.Length];
        byte[] tag = new byte[TagBytes];

        using (var aes = new AesGcm(masterKey, TagBytes))
        {
            aes.Encrypt(nonce, dek, wrapped, tag, SessionAssociatedData(session.Id));
        }

        session.EncryptedContentKey = Convert.ToBase64String(wrapped);
        session.ContentKeyNonce = Convert.ToBase64String(nonce);
        session.ContentKeyTag = Convert.ToBase64String(tag);
        session.ContentKeyVersion = version;

        _dekCache[session.Id] = dek;
    }

    /// <summary>Whether the session already has a wrapped DEK.</summary>
    public static bool HasSessionKey(ChatSession session)
        => session.EncryptedContentKey != null && session.ContentKeyNonce != null
            && session.ContentKeyTag != null;

    /// <summary>
    /// Encrypts a value into a self-describing envelope. Null and empty pass through, and a
    /// value that is already an envelope is returned unchanged.
    /// </summary>
    public string? Encrypt(ChatSession session, string? plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
            return plaintext;

        /* Idempotent, because the write paths are not all in one place and double-encrypting
           would be unrecoverable without knowing how many times it happened. */
        if (IsEncrypted(plaintext))
            return plaintext;

        byte[] dek = UnwrapDek(session);
        byte[] plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceBytes);
        byte[] ciphertext = new byte[plaintextBytes.Length];
        byte[] tag = new byte[TagBytes];

        using (var aes = new AesGcm(dek, TagBytes))
        {
            aes.Encrypt(nonce, plaintextBytes, ciphertext, tag, RowAssociatedData(session.Id));
        }

        return RowPrefix + Convert.ToBase64String(nonce)
            + ":" + Convert.ToBase64String(tag)
            + ":" + Convert.ToBase64String(ciphertext);
    }

    /// <summary>
    /// Decrypts an envelope. A value that is not an envelope is returned unchanged, which is
    /// what makes a mixed session — the normal state after an upgrade — read correctly.
    /// </summary>
    /// <remarks>
    /// A malformed or unauthenticated envelope returns a placeholder rather than throwing. A
    /// session whose key version has been retired, or whose row was corrupted, must still open:
    /// failing the whole read would make one bad row hide an entire conversation.
    /// </remarks>
    public string? Decrypt(ChatSession session, string? stored)
    {
        if (string.IsNullOrEmpty(stored) || !IsEncrypted(stored))
            return stored;

        /* A session whose key has been crypto-shredded is an expected state, not a programming
           error: a partial deletion leaves exactly this. Reading it must produce the notice,
           not a 500. */
        if (!HasSessionKey(session))
            return UnreadableNotice;

        try
        {
            var parts = stored[RowPrefix.Length..].Split(':', 3);
            if (parts.Length != 3)
                return UnreadableNotice;

            byte[] nonce = Convert.FromBase64String(parts[0]);
            byte[] tag = Convert.FromBase64String(parts[1]);
            byte[] ciphertext = Convert.FromBase64String(parts[2]);
            byte[] plaintext = new byte[ciphertext.Length];
            byte[] dek = UnwrapDek(session);

            using (var aes = new AesGcm(dek, TagBytes))
            {
                aes.Decrypt(nonce, ciphertext, tag, plaintext, RowAssociatedData(session.Id));
            }

            return Encoding.UTF8.GetString(plaintext);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or KeyNotFoundException)
        {
            _logger?.LogError(ex, "Could not decrypt content in session {SessionId}.", session.Id);
            return UnreadableNotice;
        }
    }

    /// <summary>
    /// What a row that cannot be decrypted reads as. Never silently empty — an empty message
    /// would look like the model said nothing.
    /// </summary>
    /// <remarks>
    /// Reached when the row is corrupt, when the session's key version has been retired, or
    /// when the session has been crypto-shredded. The last is a normal outcome of a partial
    /// deletion and must not throw.
    /// </remarks>
    public const string UnreadableNotice = "[This content could not be decrypted.]";

    /// <summary>
    /// Encrypts attachment bytes for disk: magic, format byte, nonce, tag, then ciphertext.
    /// </summary>
    public byte[] EncryptFile(ChatSession session, byte[] plaintext)
    {
        byte[] dek = UnwrapDek(session);
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceBytes);
        byte[] ciphertext = new byte[plaintext.Length];
        byte[] tag = new byte[TagBytes];

        using (var aes = new AesGcm(dek, TagBytes))
        {
            aes.Encrypt(nonce, plaintext, ciphertext, tag, RowAssociatedData(session.Id));
        }

        var output = new byte[FileMagic.Length + 1 + NonceBytes + TagBytes + ciphertext.Length];
        int offset = 0;
        FileMagic.CopyTo(output, offset); offset += FileMagic.Length;
        output[offset++] = 1; // format version
        nonce.CopyTo(output, offset); offset += NonceBytes;
        tag.CopyTo(output, offset); offset += TagBytes;
        ciphertext.CopyTo(output, offset);

        return output;
    }

    /// <summary>Whether a file begins with the encrypted-attachment magic.</summary>
    public static bool IsEncryptedFile(byte[] content)
    {
        if (content.Length < FileMagic.Length + 1 + NonceBytes + TagBytes)
            return false;

        for (int i = 0; i < FileMagic.Length; i++)
        {
            if (content[i] != FileMagic[i])
                return false;
        }

        return true;
    }

    /// <summary>
    /// Decrypts attachment bytes. **A file without the magic is legacy and is returned as-is**,
    /// which is what lets an upgraded session's older attachments keep working.
    /// </summary>
    public byte[] DecryptFile(ChatSession session, byte[] content)
    {
        if (!IsEncryptedFile(content))
            return content;

        // As above: a shredded session's attachment is unreadable, not an exception.
        if (!HasSessionKey(session))
            return Array.Empty<byte>();

        try
        {
            int offset = FileMagic.Length + 1; // magic + format byte
            var nonce = content.AsSpan(offset, NonceBytes).ToArray(); offset += NonceBytes;
            var tag = content.AsSpan(offset, TagBytes).ToArray(); offset += TagBytes;
            var ciphertext = content.AsSpan(offset).ToArray();
            byte[] plaintext = new byte[ciphertext.Length];
            byte[] dek = UnwrapDek(session);

            using (var aes = new AesGcm(dek, TagBytes))
            {
                aes.Decrypt(nonce, ciphertext, tag, plaintext, RowAssociatedData(session.Id));
            }

            return plaintext;
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or KeyNotFoundException)
        {
            _logger?.LogError(ex, "Could not decrypt an attachment in session {SessionId}.", session.Id);
            return Array.Empty<byte>();
        }
    }

    /// <summary>
    /// Re-wraps a session's DEK under the ring's active version, for the rotation pass.
    /// Returns false when the session has no key or its old version is gone.
    /// </summary>
    public bool TryRewrapSessionKey(ChatSession session)
    {
        if (!HasSessionKey(session))
            return false;

        if (string.Equals(session.ContentKeyVersion, _keyRing.ActiveVersion, StringComparison.OrdinalIgnoreCase))
            return false;

        byte[] dek;
        try
        {
            dek = UnwrapDek(session);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or KeyNotFoundException)
        {
            _logger?.LogError(ex,
                "Cannot re-wrap the content key for session {SessionId}: its current version '{Version}' is unavailable.",
                session.Id, session.ContentKeyVersion);
            return false;
        }

        string version = _keyRing.ActiveVersion;
        byte[] masterKey = _keyRing.GetKey(version);
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceBytes);
        byte[] wrapped = new byte[dek.Length];
        byte[] tag = new byte[TagBytes];

        using (var aes = new AesGcm(masterKey, TagBytes))
        {
            aes.Encrypt(nonce, dek, wrapped, tag, SessionAssociatedData(session.Id));
        }

        session.EncryptedContentKey = Convert.ToBase64String(wrapped);
        session.ContentKeyNonce = Convert.ToBase64String(nonce);
        session.ContentKeyTag = Convert.ToBase64String(tag);
        session.ContentKeyVersion = version;

        /* Only the wrapping changed. The DEK is the same bytes, so not one row of content is
           rewritten -- which is the whole reason for the per-session DEK. */
        return true;
    }

    private byte[] UnwrapDek(ChatSession session)
    {
        if (_dekCache.TryGetValue(session.Id, out var cached))
            return cached;

        if (!HasSessionKey(session))
        {
            throw new InvalidOperationException(
                $"Session {session.Id} has no content key. EnsureSessionKey must be called before encrypting.");
        }

        byte[] masterKey = _keyRing.GetKey(session.ContentKeyVersion ?? _keyRing.ActiveVersion);
        byte[] wrapped = Convert.FromBase64String(session.EncryptedContentKey!);
        byte[] nonce = Convert.FromBase64String(session.ContentKeyNonce!);
        byte[] tag = Convert.FromBase64String(session.ContentKeyTag!);
        byte[] dek = new byte[wrapped.Length];

        using (var aes = new AesGcm(masterKey, TagBytes))
        {
            aes.Decrypt(nonce, wrapped, tag, dek, SessionAssociatedData(session.Id));
        }

        _dekCache[session.Id] = dek;
        return dek;
    }

    /* The DEK is bound to the session it belongs to, NOT to the user. Binding to the user would
       let a wrapped DEK be replayed onto another of that same user's sessions -- the AAD would
       still authenticate, and the DEK would decrypt content it was never issued for. A DEK is a
       per-session object and its associated data says so. */
    private static byte[] SessionAssociatedData(long sessionId)
        => Encoding.UTF8.GetBytes($"chatsession:{sessionId}");

    /* Row content is bound to the session too, so a ciphertext moved between sessions fails
       authentication rather than decrypting under the wrong session's DEK. */
    private static byte[] RowAssociatedData(long sessionId)
        => Encoding.UTF8.GetBytes($"chatsession-content:{sessionId}");
}
