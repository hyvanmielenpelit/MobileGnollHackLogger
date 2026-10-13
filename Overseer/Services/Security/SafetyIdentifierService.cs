using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;

namespace Overseer.Services.Security;

/// <summary>
/// The per-user pseudonym sent to OpenAI (<c>safety_identifier</c>) and Anthropic (<c>metadata.user_id</c>)
/// with operator-funded requests, so that a provider can act on one end user instead of the operator's whole
/// key. <c>"u"</c> plus the first 32 hex digits of HMAC-SHA256 over the user id, keyed with a key derived by
/// HKDF-SHA256 from <c>AesEncryptionKey</c>; deterministic, and not reversible without that key.
/// </summary>
public class SafetyIdentifierService
{
    private const string DerivationInfo = "overseer:provider-safety-identifier:v1";

    private readonly byte[] _key;
    private readonly IServiceScopeFactory? _scopeFactory;

    public SafetyIdentifierService(IConfiguration configuration, IServiceScopeFactory? scopeFactory = null)
        : this(Convert.FromBase64String(configuration["AesEncryptionKey"]
            ?? throw new InvalidOperationException("AesEncryptionKey is not configured.")), scopeFactory)
    {
    }

    internal SafetyIdentifierService(byte[] masterKey, IServiceScopeFactory? scopeFactory = null)
    {
        _key = HKDF.DeriveKey(HashAlgorithmName.SHA256, masterKey, 32, salt: null, info: Encoding.UTF8.GetBytes(DerivationInfo));
        _scopeFactory = scopeFactory;
    }

    /// <summary>The user's identifier: 33 characters, <c>u</c> followed by 32 lowercase hex digits.</summary>
    public string For(string userId)
    {
        byte[] mac = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(userId));
        return "u" + Convert.ToHexStringLower(mac)[..32];
    }

    /// <summary>
    /// The user id whose identifier this is, or null. Computes the identifier of every user in turn, so it is
    /// O(users); fine for the account counts here.
    /// </summary>
    public async Task<string?> FindUserIdAsync(string identifier, CancellationToken ct = default)
    {
        if (_scopeFactory == null || string.IsNullOrWhiteSpace(identifier)) return null;
        identifier = identifier.Trim().ToLowerInvariant();

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await foreach (var userId in db.Users.AsNoTracking().Select(u => u.Id).AsAsyncEnumerable().WithCancellation(ct))
        {
            if (string.Equals(For(userId), identifier, StringComparison.Ordinal))
            {
                return userId;
            }
        }
        return null;
    }
}
