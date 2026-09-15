namespace Overseer.Models;

public class ChatRetentionSettings
{
    public int MaxActiveSessionsPerUser { get; set; } = 50;
    public int MaxPinnedSessionsPerUser { get; set; } = 5;
    public int InactivityTtlDays { get; set; } = 90;
    public int SoftDeleteGracePeriodDays { get; set; } = 30;
    public int PruneToolCallResultsDays { get; set; } = 30;

    /// <summary>
    /// Days a <c>ChatAccessAuditLog</c> row is kept. Zero or negative disables pruning entirely.
    /// </summary>
    /// <remarks>
    /// Long by default, because an incident is usually noticed months after it happened. This is
    /// the only setting anywhere that removes an audit row, and it is worth being precise about
    /// what that means: "append-only with a retention policy" is a weaker claim than
    /// "append-only", and the difference should not be discovered by a reviewer.
    /// </remarks>
    public int AuditLogRetentionDays { get; set; } = 365;

    /// <summary>
    /// Age, in days, after which a benchmark tool call's <c>ArgsText</c> and <c>Result</c> are
    /// nulled, measured from its run's <c>StartedAtUtc</c> rather than the row's own age. Set far
    /// above <see cref="PruneToolCallResultsDays"/>: a chat tool result is transient conversational
    /// context, but a benchmark run is a retained measurement that gets re-analysed long after it
    /// finished, and its payloads are the evidence a tool-layer finding rests on.
    /// </summary>
    public int PruneBenchmarkToolCallResultsDays { get; set; } = 90;

    /// <summary>
    /// Days a dismissed <c>SystemAiErrorLog</c> row is kept after dismissal. Undismissed rows are
    /// never pruned. Zero or negative disables pruning.
    /// </summary>
    public int PruneDismissedAiErrorLogDays { get; set; } = 90;

    /// <summary>Days a <c>MaintenanceRunLog</c> row is kept. Zero or negative disables pruning.</summary>
    public int MaintenanceHistoryRetentionDays { get; set; } = 180;

    public int MaintenanceRunHourUtc { get; set; } = 3;

    /// <summary>
    /// Ceiling for the capacity meter, in MB. Zero means auto-detect from the instance
    /// edition (see <see cref="SqlServerCapacity"/>). Set this to pre-configure a limit
    /// before the instance is actually upgraded, or to budget Overseer to less than the
    /// engine allows on an instance shared with other workloads.
    /// </summary>
    public double DatabaseMaxSizeMbOverride { get; set; } = 0;

    /// <summary>Percentage of the resolved limit at which a Warning is raised.</summary>
    public double DatabaseWarningThresholdPercent { get; set; } = SqlServerCapacity.DefaultWarningThresholdPercent;

    /// <summary>Percentage of the resolved limit at which a Critical alert is raised.</summary>
    public double DatabaseCriticalThresholdPercent { get; set; } = SqlServerCapacity.DefaultCriticalThresholdPercent;

    /// <summary>
    /// Optional absolute Warning trip point in MB; zero disables it. Evaluated in addition
    /// to <see cref="DatabaseWarningThresholdPercent"/>, with the more severe result winning.
    /// </summary>
    public double DatabaseWarningThresholdMb { get; set; } = 0;

    /// <summary>
    /// Optional absolute Critical trip point in MB; zero disables it. Evaluated in addition
    /// to <see cref="DatabaseCriticalThresholdPercent"/>, with the more severe result winning.
    /// </summary>
    public double DatabaseCriticalThresholdMb { get; set; } = 0;

    public bool EnableStorageWarningEmails { get; set; } = true;
}

public class TableStorageMetricDto
{
    public string TableName { get; set; } = string.Empty;
    public long RowCount { get; set; }
    public double TotalSpaceMb { get; set; }
    public double UsedSpaceMb { get; set; }

    /// <summary>Pages used by non-clustered indexes, in MB. Included in <see cref="UsedSpaceMb"/>.</summary>
    public double IndexSpaceMb { get; set; }
}

public class KeyVersionUsageDto
{
    public string Version { get; set; } = string.Empty;
    public int SessionCount { get; set; }

    /// <summary>False means every session wrapped under this version is unreadable.</summary>
    public bool InRing { get; set; }
    public bool IsActive { get; set; }
}

/// <summary>The effective retention configuration, echoed so the tab can show what the pass will use.</summary>
public class RetentionPolicyDto
{
    public int MaxActiveSessionsPerUser { get; set; }
    public int MaxPinnedSessionsPerUser { get; set; }
    public int InactivityTtlDays { get; set; }
    public int SoftDeleteGracePeriodDays { get; set; }
    public int PruneToolCallResultsDays { get; set; }
    public int PruneBenchmarkToolCallResultsDays { get; set; }
    public int AuditLogRetentionDays { get; set; }
    public int PruneDismissedAiErrorLogDays { get; set; }
    public int MaintenanceHistoryRetentionDays { get; set; }
    public int MaintenanceRunHourUtc { get; set; }
    public bool EnableStorageWarningEmails { get; set; }
    public string? ReportEmailAddress { get; set; }
    public bool EmailSenderConfigured { get; set; }
}

public class DatabaseStorageMetricsDto
{
    public double AllocatedDataSizeMb { get; set; }
    public double UsedDataSizeMb { get; set; }
    public double FreeSpaceWithinLimitMb { get; set; }

    /// <summary>
    /// The ceiling the capacity meter is drawn against, in MB. Zero means the edition
    /// imposes no per-database limit and none was configured; consumers must not divide
    /// by this without checking.
    /// </summary>
    public double MaxLimitMb { get; set; }
    public double UsedPercentage { get; set; }
    public List<TableStorageMetricDto> TableMetrics { get; set; } = new();

    /// <summary>True when the database engine itself enforces the limit above.</summary>
    public bool HasEngineSizeLimit { get; set; }

    /// <summary>"Detected", "Configured", or "Fallback".</summary>
    public string LimitSource { get; set; } = "Detected";

    /// <summary>Display name of the instance, e.g. "SQL Server 2022 Express".</summary>
    public string ServerProductLabel { get; set; } = string.Empty;

    /// <summary>Raw SERVERPROPERTY('Edition'), e.g. "Express Edition (64-bit)".</summary>
    public string? ServerEditionName { get; set; }

    /// <summary>Raw SERVERPROPERTY('ProductVersion'), e.g. "16.0.4200.1".</summary>
    public string? ServerProductVersion { get; set; }


    public int ActiveSessionCount { get; set; }
    public int SoftDeletedSessionCount { get; set; }
    public int InactiveSessionCount { get; set; }
    public int PinnedSessionCount { get; set; }
    
    public long DiskAttachmentsSizeBytes { get; set; }
    public double DiskAttachmentsSizeMb { get; set; }
    public int DiskAttachmentsFolderCount { get; set; }
    public int DiskAttachmentsFileCount { get; set; }
    public double EstimatedReclaimableMb { get; set; }
    
    public DateTime? LastMaintenanceRunUtc { get; set; }
    public string StatusLevel { get; set; } = "Normal"; // "Normal", "Warning", "Critical"

    // Transaction log, sys.database_files type = 1
    public double LogAllocatedMb { get; set; }
    public double LogUsedMb { get; set; }

    // TableMetrics holds the largest tables; these summarise the rest
    public double OtherTablesTotalSpaceMb { get; set; }
    public int OtherTablesCount { get; set; }
    public double AllTablesTotalSpaceMb { get; set; }

    // Privacy framework
    public int ConfidentialSessionCount { get; set; }
    public int OwnTtlSessionCount { get; set; }
    public int ImmediatePurgeSessionCount { get; set; }
    public int EphemeralSessionCount { get; set; }
    public string? ActiveContentKeyVersion { get; set; }
    public List<KeyVersionUsageDto> ContentKeyVersions { get; set; } = new();

    /// <summary>Sessions whose ContentKeyVersion is not in the key ring, and therefore unreadable.</summary>
    public int SessionsWithUnknownKeyVersionCount { get; set; }

    // Access journal
    public long AuditLogRowCount { get; set; }
    public DateTime? AuditLogOldestUtc { get; set; }
    public int AuditLogPrunableCount { get; set; }

    // AI error log
    public int AiErrorLogUndismissedCount { get; set; }
    public int AiErrorLogDismissedCount { get; set; }
    public int AiErrorLogPrunableCount { get; set; }

    // Next-pass preview
    public int ExpiredTrashSessionCount { get; set; }
    public int PrunableToolCallCount { get; set; }
    public int PrunableBenchmarkToolCallCount { get; set; }
    public DateTime? NextScheduledMaintenanceUtc { get; set; }
    public DateTime ServiceStartedUtc { get; set; }

    // Schema
    public int AppliedMigrationCount { get; set; }
    public string? LastAppliedMigration { get; set; }
    public List<string> PendingMigrations { get; set; } = new();

    /// <summary>False when migration state could not be read, e.g. on a non-relational provider.</summary>
    public bool SchemaStatusAvailable { get; set; }

    public RetentionPolicyDto Policy { get; set; } = new();
}

public class MaintenanceRequestDto
{
    public bool DryRun { get; set; } = false;
    public int? InactivityDays { get; set; }
    public int? ToolCallPruneDays { get; set; }
    public int? BenchmarkToolCallPruneDays { get; set; }

    /// <summary>Granular prune only; the full pass always reads the configured window.</summary>
    public int? AuditLogRetentionDays { get; set; }

    /// <summary>Granular prune only; the full pass always reads the configured window.</summary>
    public int? AiErrorLogPruneDays { get; set; }
}

public static class MaintenanceTriggers
{
    public const string Scheduled = "Scheduled";
    public const string Startup = "Startup";
    public const string Manual = "Manual";
    public const string PurgeTrash = "Manual:PurgeTrash";
    public const string PurgeInactive = "Manual:PurgeInactive";
    public const string PruneToolResults = "Manual:PruneToolResults";
    public const string PruneBenchmarkToolResults = "Manual:PruneBenchmarkToolResults";
    public const string PruneAuditLog = "Manual:PruneAuditLog";
    public const string PruneAiErrorLog = "Manual:PruneAiErrorLog";
    public const string SweepOrphans = "Manual:SweepOrphans";
}

public class MaintenanceResultDto
{
    public bool Success { get; set; }
    public bool IsDryRun { get; set; }
    public int SoftDeletedCount { get; set; }
    public int PurgedSessionCount { get; set; }
    public int PurgedMessageCount { get; set; }
    public int PurgedToolCallCount { get; set; }
    public int PrunedToolResultCount { get; set; }
    public int PrunedBenchmarkToolResultCount { get; set; }
    public int DeletedDiskFolderCount { get; set; }
    public int DeletedDiskFileCount { get; set; }
    public long ReclaimedDiskBytes { get; set; }
    public int SweptOrphanFolderCount { get; set; }
    public int PrunedAuditLogCount { get; set; }
    public int PrunedAiErrorLogCount { get; set; }
    public long ElapsedMilliseconds { get; set; }

    /// <summary>One of <see cref="MaintenanceTriggers"/>.</summary>
    public string Trigger { get; set; } = MaintenanceTriggers.Manual;
    public string? ErrorMessage { get; set; }
    public List<string> Logs { get; set; } = new();
}

/// <summary>A flat mirror of <c>MaintenanceRunLog</c>.</summary>
public class MaintenanceRunLogDto
{
    public long Id { get; set; }
    public DateTime StartedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }
    public string Trigger { get; set; } = string.Empty;
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
    public string? ErrorMessage { get; set; }
    public string? LogText { get; set; }
}

public class TrashSessionDto
{
    public long Id { get; set; }
    public string? Title { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime LastMessageUtc { get; set; }
    public DateTime? DeletedUtc { get; set; }
    public string? DeletionReason { get; set; }
    public int DaysRemaining { get; set; }
    public bool IsPinned { get; set; }
    public bool IsGnollHackSession { get; set; }
    public bool IsConfidential { get; set; }
    public int MessageCount { get; set; }
}
