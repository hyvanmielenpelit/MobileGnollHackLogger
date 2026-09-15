namespace MobileGnollHackLogger.Data;

using System;
using System.ComponentModel.DataAnnotations;

/// <summary>
/// One database maintenance run: a full pass or a granular admin action, dry runs included.
/// </summary>
public class MaintenanceRunLog
{
    public long Id { get; set; }

    public DateTime StartedUtc { get; set; }

    public DateTime? CompletedUtc { get; set; }

    /// <summary>"Scheduled", "Startup", "Manual", or "Manual:&lt;Action&gt;".</summary>
    [MaxLength(64)]
    public string Trigger { get; set; } = default!;

    public bool IsDryRun { get; set; }

    public bool Success { get; set; }

    public long ElapsedMilliseconds { get; set; }

    public int SoftDeletedCount { get; set; }

    public int PurgedSessionCount { get; set; }

    public int PurgedMessageCount { get; set; }

    public int PurgedToolCallCount { get; set; }

    public int PrunedToolResultCount { get; set; }

    public int PrunedBenchmarkToolResultCount { get; set; }

    public int PrunedAuditLogCount { get; set; }

    public int PrunedAiErrorLogCount { get; set; }

    public int DeletedDiskFolderCount { get; set; }

    public int DeletedDiskFileCount { get; set; }

    public int SweptOrphanFolderCount { get; set; }

    public long ReclaimedDiskBytes { get; set; }

    [MaxLength(1024)]
    public string? ErrorMessage { get; set; }

    /// <summary>The run's log lines joined by newlines, truncated to the column length.</summary>
    [MaxLength(4000)]
    public string? LogText { get; set; }
}
