namespace Overseer.Models;

public class ChatRetentionSettings
{
    public int MaxActiveSessionsPerUser { get; set; } = 50;
    public int MaxPinnedSessionsPerUser { get; set; } = 5;
    public int InactivityTtlDays { get; set; } = 90;
    public int SoftDeleteGracePeriodDays { get; set; } = 30;
    public int PruneToolCallResultsDays { get; set; } = 30;
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
}

public class MaintenanceRequestDto
{
    public bool DryRun { get; set; } = false;
    public int? InactivityDays { get; set; }
    public int? ToolCallPruneDays { get; set; }
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
    public int DeletedDiskFolderCount { get; set; }
    public int DeletedDiskFileCount { get; set; }
    public long ReclaimedDiskBytes { get; set; }
    public long ElapsedMilliseconds { get; set; }
    public List<string> Logs { get; set; } = new();
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
    public int MessageCount { get; set; }
}
