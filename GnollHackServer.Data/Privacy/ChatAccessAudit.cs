using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;

namespace GnollHackServer.Data.Privacy;

/// <summary>
/// Writes <see cref="ChatAccessAuditLog"/> rows.
/// </summary>
/// <remarks>
/// <para>
/// A static helper in the data project rather than a service, for the same reason
/// <see cref="CryptoShred"/> is: <c>MobileGnollHackLogger</c> and <c>Overseer</c> each reference
/// only this project and neither references the other, so anything both web projects have to
/// write has to live here. The portability export runs in the Razor application and the
/// conversation reads run in Overseer; both produce audit rows.
/// </para>
/// <para>
/// <b>Every method here swallows its own failures.</b> That is the one design decision worth
/// arguing about, so the reasoning is recorded: an audit write that throws would turn a
/// successful read into a 500, and a user who cannot open their own conversation because the
/// journal is full is a worse outcome than a missing journal entry. The alternative — refusing
/// the read when it cannot be recorded — is the right choice for a system whose audit trail is a
/// compliance control, and it is a deliberate deployment-level decision rather than something to
/// impose from here. A failed write is logged by the caller's logger where one is supplied.
/// </para>
/// </remarks>
public static class ChatAccessAudit
{
    /// <summary>
    /// Records an access and saves it immediately.
    /// </summary>
    /// <remarks>
    /// Saved on its own rather than left for the caller's next <c>SaveChangesAsync</c>: an audit
    /// row that is rolled back with an unrelated failure has not recorded anything, and the
    /// whole point is that the record survives whatever happened next.
    /// </remarks>
    public static async Task<bool> RecordAsync(
        ApplicationDbContext dbContext,
        ChatAccessAction action,
        string? actorUserId,
        string? actorUserName = null,
        bool actorWasAdmin = false,
        string? subjectUserId = null,
        long? chatSessionId = null,
        long? attachmentId = null,
        string? sessionRef = null,
        bool wasConfidential = false,
        string? ipAddress = null,
        string? detail = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            dbContext.ChatAccessAuditLogs.Add(new ChatAccessAuditLog
            {
                OccurredUtc = DateTime.UtcNow,
                Action = action.ToString(),
                ActorUserId = Clamp(actorUserId, 450),
                ActorUserName = Clamp(actorUserName, 256),
                ActorWasAdmin = actorWasAdmin,
                SubjectUserId = Clamp(subjectUserId ?? actorUserId, 450),
                ChatSessionId = chatSessionId,
                ChatMessageAttachmentId = attachmentId,
                SessionRef = Clamp(sessionRef, 64),
                WasConfidential = wasConfidential,
                IpAddress = Clamp(ipAddress, 64),
                Detail = Clamp(detail, 512)
            });

            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            /* See the class remarks. A read must not fail because its journal entry could not be
               written; the caller logs it. */
            return false;
        }
    }

    /// <summary>
    /// Deletes rows older than <paramref name="retentionDays"/>, or does nothing when that is
    /// zero or negative.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Retention on an audit log is a policy decision and needs stating plainly: this is the one
    /// operation that removes an audit row, and it exists because a journal nobody prunes
    /// eventually becomes the largest table in the database. A deployment that needs a longer
    /// window sets a longer window; one that needs true immutability needs storage the
    /// application cannot rewrite, which no setting here provides.
    /// </para>
    /// <para>
    /// Set-based, so it reads no rows and materialises nothing. The EF in-memory provider cannot
    /// translate that, which is why the tests for it use a relational provider.
    /// </para>
    /// </remarks>
    public static async Task<int> PruneAsync(
        ApplicationDbContext dbContext,
        int retentionDays,
        CancellationToken cancellationToken = default)
    {
        if (retentionDays <= 0) return 0;

        DateTime cutoff = DateTime.UtcNow.AddDays(-retentionDays);

        return await dbContext.ChatAccessAuditLogs
            .Where(a => a.OccurredUtc < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private static string? Clamp(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
