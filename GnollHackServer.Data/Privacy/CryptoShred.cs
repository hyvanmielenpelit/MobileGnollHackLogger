using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;

namespace GnollHackServer.Data.Privacy;

/// <summary>
/// Destroys the keys that make stored content readable, so a partial deletion leaves
/// unreadable data rather than readable data.
/// </summary>
/// <remarks>
/// <para>
/// **Why a static helper in the data project rather than a service call.** Both web projects
/// need this: <c>Overseer</c>'s retention service and <c>MobileGnollHackLogger</c>'s account
/// deletion page. Each project references only <c>GnollHackServer.Data</c> and neither
/// references the other, so <c>ChatRetentionService</c> is simply unreachable from a Razor
/// page — "route it through the purge service" cannot be done. Lifting the whole retention
/// service down here is the alternative and is worse: it depends on <c>IConfiguration</c>,
/// <c>ILogger</c>, disk paths and settings, none of which belong in an entity assembly.
/// </para>
/// <para>
/// **What crypto-shredding does and does not do.** It makes data unrecoverable from a backup
/// restored *after* the shred. A backup taken *before* it contains the wrapped key, and the
/// master key is still in configuration, so **that backup stays readable**. Any claim that
/// shredding reaches existing backup media is false.
/// </para>
/// </remarks>
public static class CryptoShred
{
    /// <summary>
    /// Nulls the wrapped content key on the given sessions, making their message content, tool
    /// payloads, titles and attachments permanently unreadable.
    /// </summary>
    /// <remarks>
    /// Called **before** deleting the rows, so an interruption between the two leaves content
    /// that cannot be read rather than content that can. A set-based update: it reads no
    /// ciphertext and needs no key material.
    /// </remarks>
    public static async Task<int> NullSessionKeysAsync(
        ApplicationDbContext dbContext, IReadOnlyCollection<long> sessionIds, CancellationToken cancellationToken = default)
    {
        if (sessionIds == null || sessionIds.Count == 0)
            return 0;

        var ids = sessionIds as ICollection<long> ?? sessionIds.ToList();

        return await dbContext.ChatSession
            .Where(s => ids.Contains(s.Id))
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.EncryptedContentKey, (string?)null)
                .SetProperty(x => x.ContentKeyNonce, (string?)null)
                .SetProperty(x => x.ContentKeyTag, (string?)null)
                .SetProperty(x => x.ContentKeyVersion, (string?)null), cancellationToken);
    }

    /// <summary>Nulls the wrapped content key on every session belonging to a user.</summary>
    public static async Task<int> NullAllSessionKeysForUserAsync(
        ApplicationDbContext dbContext, string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(userId))
            return 0;

        return await dbContext.ChatSession
            .Where(s => s.AspNetUserId == userId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.EncryptedContentKey, (string?)null)
                .SetProperty(x => x.ContentKeyNonce, (string?)null)
                .SetProperty(x => x.ContentKeyTag, (string?)null)
                .SetProperty(x => x.ContentKeyVersion, (string?)null), cancellationToken);
    }

    /// <summary>
    /// Nulls a user's stored AI provider credentials.
    /// </summary>
    /// <remarks>
    /// A different master key protects these — <c>AesEncryptionKey</c>, which does not rotate —
    /// so they are not covered by nulling a session's content key. Account deletion has to
    /// reach both.
    /// </remarks>
    public static async Task<int> NullUserApiKeyMaterialAsync(
        ApplicationDbContext dbContext, string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(userId))
            return 0;

        return await dbContext.UserAiApiKeys
            .Where(k => k.AspNetUserId == userId)
            .ExecuteUpdateAsync(k => k
                .SetProperty(x => x.EncryptedApiKey, (string?)null)
                .SetProperty(x => x.ApiKeyNonce, (string?)null)
                .SetProperty(x => x.ApiKeyTag, (string?)null), cancellationToken);
    }
}
