using System.Diagnostics;
using GnollHackServer.Data;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Models;

namespace Overseer.Services;

public class ChatRetentionService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ChatRetentionService> _logger;
    private readonly ChatRetentionSettings _settings;

    public ChatRetentionService(
        ApplicationDbContext dbContext,
        IConfiguration configuration,
        ILogger<ChatRetentionService> logger)
    {
        _dbContext = dbContext;
        _configuration = configuration;
        _logger = logger;
        
        _settings = new ChatRetentionSettings();
        _configuration.GetSection("ChatRetentionSettings").Bind(_settings);
    }

    /* Every deletion path partitions its set the same way: a session with
       ImmediatePurgeOnDelete is purged outright, the rest go to the trash as before.

       There are four such paths, and a guarantee that holds for one gesture and not the other
       three is not a guarantee. Two of them fire with no user gesture at all -- quota eviction
       runs on ordinary session creation and on every snapshot attach, and inactivity expiry
       runs nightly -- so those are the ones where a confidential chat could quietly enter a
       30-day trash it was promised it would not. */
    private async Task<int> PartitionAndDeleteAsync(
        List<ChatSession> sessions, string reason, CancellationToken cancellationToken)
    {
        if (sessions.Count == 0)
            return 0;

        var purgeIds = sessions.Where(s => s.ImmediatePurgeOnDelete).Select(s => s.Id).ToList();
        var trashSessions = sessions.Where(s => !s.ImmediatePurgeOnDelete).ToList();

        if (trashSessions.Count > 0)
        {
            var now = DateTime.UtcNow;
            foreach (var s in trashSessions)
            {
                s.IsDeleted = true;
                s.DeletedUtc = now;
                s.DeletionReason = reason;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        if (purgeIds.Count > 0)
        {
            /* Purged rather than soft-deleted, so there is no trash row and no grace period.
               PermanentlyPurgeSessionsAsync also removes the session's files from disk, which
               a soft delete leaves in place. */
            await PermanentlyPurgeSessionsAsync(purgeIds, isDryRun: false, cancellationToken);
            _logger.LogInformation(
                "Immediately purged {Count} confidential sessions on {Reason} deletion", purgeIds.Count, reason);
        }

        return sessions.Count;
    }

    public async Task<int> EnforceUserSessionQuotaAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(userId) || _settings.MaxActiveSessionsPerUser <= 0)
            return 0;

        int totalActiveCount = await _dbContext.ChatSession
            .CountAsync(s => s.AspNetUserId == userId && !s.IsDeleted, cancellationToken);

        int excess = totalActiveCount - _settings.MaxActiveSessionsPerUser;
        if (excess <= 0)
            return 0;

        var toSoftDelete = await _dbContext.ChatSession
            .Where(s => s.AspNetUserId == userId && !s.IsDeleted && !s.IsPinned)
            .OrderBy(s => s.LastMessageUtc)
            .Take(excess)
            .ToListAsync(cancellationToken);

        /* The sharpest of the four paths: it evicts a user's oldest sessions the moment they
           pass MaxActiveSessionsPerUser, with no gesture and no notice. A confidential chat
           reaching the trash here is a deletion nobody asked for. */
        int count = await PartitionAndDeleteAsync(toSoftDelete, "Quota", cancellationToken);
        _logger.LogInformation("Enforced quota for user {UserId}: removed {Count} excess sessions", userId, count);
        return count;
    }

    public async Task<bool> SoftDeleteSessionAsync(long sessionId, string userId, string reason = "User", CancellationToken cancellationToken = default)
    {
        var session = await _dbContext.ChatSession
            .FirstOrDefaultAsync(s => s.Id == sessionId && (string.IsNullOrEmpty(userId) || s.AspNetUserId == userId), cancellationToken);

        if (session == null || session.IsDeleted)
            return false;

        await PartitionAndDeleteAsync(new List<ChatSession> { session }, reason, cancellationToken);
        return true;
    }

    public async Task<bool> RestoreSessionAsync(long sessionId, string userId, CancellationToken cancellationToken = default)
    {
        var session = await _dbContext.ChatSession
            .FirstOrDefaultAsync(s => s.Id == sessionId && (string.IsNullOrEmpty(userId) || s.AspNetUserId == userId), cancellationToken);

        if (session == null || !session.IsDeleted)
            return false;

        if (!string.IsNullOrEmpty(userId))
        {
            if (session.IsPinned)
            {
                var pinnedCount = await _dbContext.ChatSession
                    .CountAsync(s => s.AspNetUserId == userId && !s.IsDeleted && s.IsPinned, cancellationToken);
                if (pinnedCount >= _settings.MaxPinnedSessionsPerUser)
                {
                    // Pinned quota is full: restore as unpinned
                    session.IsPinned = false;
                }
            }

            var totalActiveCount = await _dbContext.ChatSession
                .CountAsync(s => s.AspNetUserId == userId && !s.IsDeleted, cancellationToken);
            if (totalActiveCount >= _settings.MaxActiveSessionsPerUser)
            {
                throw new InvalidOperationException($"Cannot restore chat: active chat quota ({_settings.MaxActiveSessionsPerUser}) reached. Please delete or permanently remove an active chat first.");
            }
        }

        session.IsDeleted = false;
        session.DeletedUtc = null;
        session.DeletionReason = null;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool?> TogglePinSessionAsync(long sessionId, string userId, CancellationToken cancellationToken = default)
    {
        var session = await _dbContext.ChatSession
            .FirstOrDefaultAsync(s => s.Id == sessionId && (string.IsNullOrEmpty(userId) || s.AspNetUserId == userId), cancellationToken);

        if (session == null || session.IsDeleted)
            return null;

        if (!session.IsPinned)
        {
            // Pinning: check max pinned quota
            var pinnedCount = await _dbContext.ChatSession
                .CountAsync(s => s.AspNetUserId == userId && s.IsPinned && !s.IsDeleted, cancellationToken);
            if (pinnedCount >= _settings.MaxPinnedSessionsPerUser)
            {
                throw new InvalidOperationException($"You can pin a maximum of {_settings.MaxPinnedSessionsPerUser} sessions. Please unpin an existing session to pin a new one.");
            }
            session.IsPinned = true;
        }
        else
        {
            session.IsPinned = false;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return session.IsPinned;
    }

    public async Task<int> BulkSoftDeleteSessionsAsync(string userId, bool includePinned, string reason = "User", CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(userId))
            return 0;

        var query = _dbContext.ChatSession
            .Where(s => s.AspNetUserId == userId && !s.IsDeleted);

        if (!includePinned)
        {
            query = query.Where(s => !s.IsPinned);
        }

        var sessions = await query.ToListAsync(cancellationToken);
        if (sessions.Count == 0)
            return 0;

        int count = await PartitionAndDeleteAsync(sessions, reason, cancellationToken);
        _logger.LogInformation("Bulk deleted {Count} sessions for user {UserId} (includePinned: {IncludePinned})", count, userId, includePinned);
        return count;
    }

    public async Task<int> UnpinAllSessionsAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(userId))
            return 0;

        var sessions = await _dbContext.ChatSession
            .Where(s => s.AspNetUserId == userId && !s.IsDeleted && s.IsPinned)
            .ToListAsync(cancellationToken);

        if (sessions.Count == 0)
            return 0;

        foreach (var s in sessions)
        {
            s.IsPinned = false;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Unpinned {Count} sessions for user {UserId}", sessions.Count, userId);
        return sessions.Count;
    }

    /// <summary>
    /// Deletes sessions and their content outright: rows, tool calls, attachments and the
    /// files on disk.
    /// </summary>
    /// <remarks>
    /// Virtual so a test can observe which sessions a deletion path routed here without
    /// running the bulk-SQL statements, which the in-memory provider does not support. The
    /// partitioning decision is what the four deletion paths have to get right; the SQL beneath
    /// it is unchanged and provider-specific.
    /// </remarks>
    public virtual async Task<MaintenanceResultDto> PermanentlyPurgeSessionsAsync(List<long> sessionIds, bool isDryRun = false, CancellationToken cancellationToken = default)
    {
        var result = new MaintenanceResultDto { IsDryRun = isDryRun };
        if (sessionIds == null || sessionIds.Count == 0)
        {
            result.Success = true;
            return result;
        }

        var sw = Stopwatch.StartNew();
        var baseDir = _configuration["ConversationsDataLocation"];

        /* 0. Crypto-shred first. Nulling the wrapped content keys before anything is deleted
              means an interruption anywhere below leaves content that cannot be read rather
              than content that can -- and the files on disk are ciphertext whose key is now
              gone. It is a set-based update: no ciphertext is read and no key material is
              needed.

              The same helper runs from the account-deletion page, which cannot reach this
              service at all: MobileGnollHackLogger references only GnollHackServer.Data. One
              implementation, two callers. */
        if (!isDryRun)
        {
            /* The erasure record goes first, for the same reason the shred does: written after
               the rows were destroyed it would have nothing left to name, and an interruption
               between the two would leave a purge with no trace of having happened. It carries
               the session id, which is about to stop existing anywhere else. */
            foreach (long sessionId in sessionIds)
            {
                await GnollHackServer.Data.Privacy.ChatAccessAudit.RecordAsync(
                    _dbContext,
                    ChatAccessAction.Erasure,
                    actorUserId: null,
                    actorUserName: "retention",
                    chatSessionId: sessionId,
                    sessionRef: sessionId.ToString(),
                    detail: "permanent purge",
                    cancellationToken: cancellationToken);
            }

            await GnollHackServer.Data.Privacy.CryptoShred.NullSessionKeysAsync(
                _dbContext, sessionIds, cancellationToken);
        }

        // 1. Delete physical files from disk
        int deletedDirs = 0;
        int deletedFiles = 0;
        long reclaimedBytes = 0;

        foreach (var sid in sessionIds)
        {
            if (!string.IsNullOrEmpty(baseDir))
            {
                var sessionDir = Path.Combine(baseDir, sid.ToString());
                if (Directory.Exists(sessionDir))
                {
                    try
                    {
                        var files = Directory.GetFiles(sessionDir, "*.*", SearchOption.AllDirectories);
                        deletedFiles += files.Length;
                        foreach (var f in files)
                        {
                            try { reclaimedBytes += new FileInfo(f).Length; } catch {}
                        }

                        if (!isDryRun)
                        {
                            Directory.Delete(sessionDir, recursive: true);
                        }
                        deletedDirs++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to delete session directory {SessionDir}", sessionDir);
                        result.Logs.Add($"Warning: Failed to delete disk folder {sessionDir}: {ex.Message}");
                    }
                }
            }
        }

        result.DeletedDiskFolderCount = deletedDirs;
        result.DeletedDiskFileCount = deletedFiles;
        result.ReclaimedDiskBytes = reclaimedBytes;

        // 2. Count DB records to be purged
        result.PurgedSessionCount = sessionIds.Count;
        result.PurgedMessageCount = await _dbContext.ChatMessage.CountAsync(m => sessionIds.Contains(m.ChatSessionId), cancellationToken);
        result.PurgedToolCallCount = await _dbContext.ChatMessageToolCall.CountAsync(tc => tc.ChatMessage != null && sessionIds.Contains(tc.ChatMessage.ChatSessionId), cancellationToken);

        if (!isDryRun)
        {
            // Execute bulk deletes in dependency order
            await _dbContext.ChatMessageAttachment
                .Where(a => a.ChatMessage != null && sessionIds.Contains(a.ChatMessage.ChatSessionId))
                .ExecuteDeleteAsync(cancellationToken);

            await _dbContext.ChatMessageToolCall
                .Where(tc => tc.ChatMessage != null && sessionIds.Contains(tc.ChatMessage.ChatSessionId))
                .ExecuteDeleteAsync(cancellationToken);

            await _dbContext.ChatMessage
                .Where(m => sessionIds.Contains(m.ChatSessionId))
                .ExecuteDeleteAsync(cancellationToken);

            await _dbContext.ChatSession
                .Where(s => sessionIds.Contains(s.Id))
                .ExecuteDeleteAsync(cancellationToken);
        }

        sw.Stop();
        result.ElapsedMilliseconds = sw.ElapsedMilliseconds;
        result.Success = true;
        result.Logs.Add($"{(isDryRun ? "[DRY RUN] " : "")}Purged {result.PurgedSessionCount} sessions, {result.PurgedMessageCount} messages, {result.DeletedDiskFolderCount} disk folders.");

        return result;
    }

    /// <summary>
    /// Expires sessions inactive for longer than their TTL.
    /// </summary>
    /// <remarks>
    /// Runs in two passes, because a confidential session's TTL is per-session while this
    /// sweep is global:
    /// <list type="number">
    /// <item><description>
    /// Sessions with no materialised TTL, against <paramref name="inactivityDays"/> — the
    /// original set-based <c>ExecuteUpdateAsync</c>, untouched and still one statement.
    /// </description></item>
    /// <item><description>
    /// One pass per distinct <see cref="ChatSession.EffectiveRetentionDays"/> value. A per-user
    /// value cannot reach a single set-based query, and a JSON column cannot be filtered there
    /// portably, which is exactly why E1 materialises the scalar onto the row.
    /// </description></item>
    /// </list>
    /// The second pass also **purges** rather than soft-deletes where the session says so. The
    /// grace period is 30 days, so a confidential session with a 30-day TTL that soft-deleted
    /// on expiry would be retained for 60 — under a 30-day promise. That is the whole point of
    /// the pass existing.
    /// </remarks>
    public async Task<int> SoftDeleteInactiveSessionsAsync(int inactivityDays, bool isDryRun = false, CancellationToken cancellationToken = default)
    {
        var cutoff = DateTime.UtcNow.AddDays(-inactivityDays);
        var query = _dbContext.ChatSession
            .Where(s => !s.IsDeleted && !s.IsPinned && s.LastMessageUtc < cutoff
                && s.EffectiveRetentionDays == null);

        int count = await query.CountAsync(cancellationToken);
        if (count > 0 && !isDryRun)
        {
            var now = DateTime.UtcNow;
            await query.ExecuteUpdateAsync(s => s
                .SetProperty(x => x.IsDeleted, true)
                .SetProperty(x => x.DeletedUtc, now)
                .SetProperty(x => x.DeletionReason, "Inactivity"), cancellationToken);
            _logger.LogInformation("Soft-deleted {Count} inactive sessions (> {Days} days)", count, inactivityDays);
        }

        count += await ExpireSessionsWithOwnTtlAsync(isDryRun, cancellationToken);
        return count;
    }

    /* One pass per distinct materialised TTL. The distinct set is small in practice -- it is
       the number of different retention values users have actually chosen, not the number of
       users -- so this is a handful of queries, not one per session. */
    private async Task<int> ExpireSessionsWithOwnTtlAsync(bool isDryRun, CancellationToken cancellationToken)
    {
        var ttls = await _dbContext.ChatSession
            .Where(s => !s.IsDeleted && !s.IsPinned && s.EffectiveRetentionDays != null)
            .Select(s => s.EffectiveRetentionDays!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

        int total = 0;

        foreach (int ttl in ttls)
        {
            if (ttl <= 0)
                continue;

            var cutoff = DateTime.UtcNow.AddDays(-ttl);
            var expired = await _dbContext.ChatSession
                .Where(s => !s.IsDeleted && !s.IsPinned
                    && s.EffectiveRetentionDays == ttl && s.LastMessageUtc < cutoff)
                .ToListAsync(cancellationToken);

            if (expired.Count == 0)
                continue;

            total += expired.Count;

            if (!isDryRun)
            {
                await PartitionAndDeleteAsync(expired, "Inactivity", cancellationToken);
                _logger.LogInformation(
                    "Expired {Count} sessions past their own {Days}-day TTL", expired.Count, ttl);
            }
        }

        return total;
    }

    public async Task<int> PruneAgedToolCallResultsAsync(int daysOld, bool isDryRun = false, CancellationToken cancellationToken = default)
    {
        var cutoff = DateTime.UtcNow.AddDays(-daysOld);
        var query = _dbContext.ChatMessageToolCall
            .Where(tc => tc.ChatMessage != null && tc.ChatMessage.TimestampUtc < cutoff && (tc.Result != null || tc.ArgsText != null));

        int count = await query.CountAsync(cancellationToken);
        if (count > 0 && !isDryRun)
        {
            await query.ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Result, (string?)null)
                .SetProperty(x => x.ArgsText, (string?)null), cancellationToken);
            _logger.LogInformation("Pruned {Count} tool call result payloads older than {Days} days", count, daysOld);
        }
        return count;
    }

    public async Task<int> PruneAgedBenchmarkToolCallPayloadsAsync(int daysOld, bool isDryRun = false, CancellationToken cancellationToken = default)
    {
        var cutoff = DateTime.UtcNow.AddDays(-daysOld);

        // The age is the run's, not the row's: BenchmarkRunAnswerToolCall carries no timestamp of
        // its own, and a run re-analysed long after it finished should keep its evidence until the
        // run itself is old enough, not until some unrelated clock on the row ticks over.
        var query = _dbContext.BenchmarkRunAnswerToolCalls
            .Where(tc => tc.BenchmarkRunAnswer!.BenchmarkRun.StartedAtUtc < cutoff
                && (tc.Result != null || tc.ArgsText != null));

        int count = await query.CountAsync(cancellationToken);
        if (count > 0 && !isDryRun)
        {
            // Name, Status, Error, SortOrder, the timings and ResultLengthChars are never pruned:
            // they are a few bytes each and are what the aggregates and the tool-layer diagnostics
            // read.
            await query.ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Result, (string?)null)
                .SetProperty(x => x.ArgsText, (string?)null), cancellationToken);
            _logger.LogInformation("Pruned {Count} benchmark tool call payloads for runs older than {Days} days", count, daysOld);
        }
        return count;
    }

    public async Task<int> SweepOrphanedDiskDirectoriesAsync(bool isDryRun = false, CancellationToken cancellationToken = default)
    {
        var baseDir = _configuration["ConversationsDataLocation"];
        if (string.IsNullOrEmpty(baseDir) || !Directory.Exists(baseDir))
            return 0;

        int sweptCount = 0;
        try
        {
            var subDirs = Directory.GetDirectories(baseDir);
            foreach (var dir in subDirs)
            {
                var dirName = Path.GetFileName(dir);
                if (long.TryParse(dirName, out long sid))
                {
                    bool existsInDb = await _dbContext.ChatSession.AnyAsync(s => s.Id == sid, cancellationToken);
                    if (!existsInDb)
                    {
                        sweptCount++;
                        if (!isDryRun)
                        {
                            try
                            {
                                Directory.Delete(dir, recursive: true);
                                _logger.LogInformation("Swept orphaned disk directory: {Dir}", dir);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Failed to sweep orphaned directory {Dir}", dir);
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sweeping orphaned disk directories in {BaseDir}", baseDir);
        }

        return sweptCount;
    }

    public async Task<MaintenanceResultDto> RunFullMaintenanceAsync(MaintenanceRequestDto? request = null, CancellationToken cancellationToken = default)
    {
        var isDryRun = request?.DryRun ?? false;
        var inactivityDays = request?.InactivityDays ?? _settings.InactivityTtlDays;
        var toolCallDays = request?.ToolCallPruneDays ?? _settings.PruneToolCallResultsDays;
        var benchmarkToolCallDays = request?.BenchmarkToolCallPruneDays ?? _settings.PruneBenchmarkToolCallResultsDays;

        var sw = Stopwatch.StartNew();
        var result = new MaintenanceResultDto { IsDryRun = isDryRun };

        // 1. Soft-delete inactive sessions
        result.SoftDeletedCount = await SoftDeleteInactiveSessionsAsync(inactivityDays, isDryRun, cancellationToken);
        result.Logs.Add($"Inactive sessions soft-deleted (> {inactivityDays}d): {result.SoftDeletedCount}");

        // 2. Find expired trash sessions (> SoftDeleteGracePeriodDays)
        var trashCutoff = DateTime.UtcNow.AddDays(-_settings.SoftDeleteGracePeriodDays);
        var expiredTrashIds = await _dbContext.ChatSession
            .Where(s => s.IsDeleted && s.DeletedUtc < trashCutoff)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);

        // 3. Purge expired trash sessions
        var purgeResult = await PermanentlyPurgeSessionsAsync(expiredTrashIds, isDryRun, cancellationToken);
        result.PurgedSessionCount = purgeResult.PurgedSessionCount;
        result.PurgedMessageCount = purgeResult.PurgedMessageCount;
        result.PurgedToolCallCount = purgeResult.PurgedToolCallCount;
        result.DeletedDiskFolderCount = purgeResult.DeletedDiskFolderCount;
        result.DeletedDiskFileCount = purgeResult.DeletedDiskFileCount;
        result.ReclaimedDiskBytes = purgeResult.ReclaimedDiskBytes;
        result.Logs.AddRange(purgeResult.Logs);

        // 4. Prune aged tool call results
        result.PrunedToolResultCount = await PruneAgedToolCallResultsAsync(toolCallDays, isDryRun, cancellationToken);
        result.Logs.Add($"Aged tool call payloads pruned (> {toolCallDays}d): {result.PrunedToolResultCount}");

        // 5. Prune aged benchmark tool call payloads
        result.PrunedBenchmarkToolResultCount = await PruneAgedBenchmarkToolCallPayloadsAsync(benchmarkToolCallDays, isDryRun, cancellationToken);
        result.Logs.Add($"Aged benchmark tool call payloads pruned (> {benchmarkToolCallDays}d): {result.PrunedBenchmarkToolResultCount}");

        // 6. Sweep orphaned disk folders
        int orphanedSwept = await SweepOrphanedDiskDirectoriesAsync(isDryRun, cancellationToken);
        result.Logs.Add($"Orphaned disk folders swept: {orphanedSwept}");

        /* 7. Prune the access journal. This is the only operation anywhere that removes an audit
              row, and it exists because a journal nobody prunes becomes the largest table in the
              database. The default window is deliberately long -- an incident is usually noticed
              months after it happened -- and it is stated in the framework document rather than
              left implicit, because "append-only with a retention policy" is a weaker claim than
              "append-only" and the difference should not be discovered by a reviewer. */
        int auditRetentionDays = _settings.AuditLogRetentionDays;
        if (!isDryRun && auditRetentionDays > 0)
        {
            int prunedAudit = await GnollHackServer.Data.Privacy.ChatAccessAudit.PruneAsync(
                _dbContext, auditRetentionDays, cancellationToken);
            result.Logs.Add($"Access journal rows pruned (> {auditRetentionDays}d): {prunedAudit}");
        }
        else if (auditRetentionDays <= 0)
        {
            result.Logs.Add("Access journal retention is disabled; no rows were pruned.");
        }

        if (!isDryRun)
        {
            DatabaseStorageMetricsService.RecordMaintenanceRun();
        }

        sw.Stop();
        result.ElapsedMilliseconds = sw.ElapsedMilliseconds;
        result.Success = true;
        return result;
    }
}
