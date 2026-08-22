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

    public async Task<int> EnforceUserSessionQuotaAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(userId) || _settings.MaxActiveSessionsPerUser <= 0)
            return 0;

        var activeUnpinned = await _dbContext.ChatSession
            .Where(s => s.AspNetUserId == userId && !s.IsDeleted && !s.IsPinned)
            .OrderBy(s => s.LastMessageUtc)
            .ToListAsync(cancellationToken);

        int excess = activeUnpinned.Count - _settings.MaxActiveSessionsPerUser;
        if (excess <= 0)
            return 0;

        var toSoftDelete = activeUnpinned.Take(excess).ToList();
        var now = DateTime.UtcNow;
        foreach (var s in toSoftDelete)
        {
            s.IsDeleted = true;
            s.DeletedUtc = now;
            s.DeletionReason = "Quota";
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Enforced quota for user {UserId}: soft-deleted {Count} excess sessions", userId, toSoftDelete.Count);
        return toSoftDelete.Count;
    }

    public async Task<bool> SoftDeleteSessionAsync(long sessionId, string userId, string reason = "User", CancellationToken cancellationToken = default)
    {
        var session = await _dbContext.ChatSession
            .FirstOrDefaultAsync(s => s.Id == sessionId && (string.IsNullOrEmpty(userId) || s.AspNetUserId == userId), cancellationToken);

        if (session == null || session.IsDeleted)
            return false;

        session.IsDeleted = true;
        session.DeletedUtc = DateTime.UtcNow;
        session.DeletionReason = reason;

        await _dbContext.SaveChangesAsync(cancellationToken);
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
            // Check quota: if user already has MaxActiveSessionsPerUser, soft-delete oldest unpinned
            var activeCount = await _dbContext.ChatSession
                .CountAsync(s => s.AspNetUserId == userId && !s.IsDeleted && !s.IsPinned, cancellationToken);
            if (activeCount >= _settings.MaxActiveSessionsPerUser)
            {
                var oldest = await _dbContext.ChatSession
                    .Where(s => s.AspNetUserId == userId && !s.IsDeleted && !s.IsPinned)
                    .OrderBy(s => s.LastMessageUtc)
                    .FirstOrDefaultAsync(cancellationToken);
                if (oldest != null)
                {
                    oldest.IsDeleted = true;
                    oldest.DeletedUtc = DateTime.UtcNow;
                    oldest.DeletionReason = "Quota";
                }
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
                throw new InvalidOperationException($"You can pin a maximum of {_settings.MaxPinnedSessionsPerUser} sessions.");
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

    public async Task<MaintenanceResultDto> PermanentlyPurgeSessionsAsync(List<long> sessionIds, bool isDryRun = false, CancellationToken cancellationToken = default)
    {
        var result = new MaintenanceResultDto { IsDryRun = isDryRun };
        if (sessionIds == null || sessionIds.Count == 0)
        {
            result.Success = true;
            return result;
        }

        var sw = Stopwatch.StartNew();
        var baseDir = _configuration["ConversationsDataLocation"];

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

    public async Task<int> SoftDeleteInactiveSessionsAsync(int inactivityDays, bool isDryRun = false, CancellationToken cancellationToken = default)
    {
        var cutoff = DateTime.UtcNow.AddDays(-inactivityDays);
        var query = _dbContext.ChatSession
            .Where(s => !s.IsDeleted && !s.IsPinned && s.LastMessageUtc < cutoff);

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
        return count;
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

        // 5. Sweep orphaned disk folders
        int orphanedSwept = await SweepOrphanedDiskDirectoriesAsync(isDryRun, cancellationToken);
        result.Logs.Add($"Orphaned disk folders swept: {orphanedSwept}");

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
