namespace Overseer.Models;

public class ChatRetentionSettings
{
    public int MaxActiveSessionsPerUser { get; set; } = 50;
    public int MaxPinnedSessionsPerUser { get; set; } = 5;
    public int InactivityTtlDays { get; set; } = 90;
    public int SoftDeleteGracePeriodDays { get; set; } = 30;
    public int PruneToolCallResultsDays { get; set; } = 30;
    public int MaintenanceRunHourUtc { get; set; } = 3;
    public double DatabaseWarningThresholdMb { get; set; } = 7680;
    public double DatabaseCriticalThresholdMb { get; set; } = 8704;
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
    public double FreeSpaceWithin10GbMb { get; set; }
    public double MaxLimitMb { get; set; } = 10240;
    public double UsedPercentage { get; set; }
    public List<TableStorageMetricDto> TableMetrics { get; set; } = new();
    
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
