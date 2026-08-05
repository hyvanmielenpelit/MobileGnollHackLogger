namespace MobileGnollHackLogger.Data;

using System;

public class GroupSystemAiApiConfiguration : IRateLimitedEntity
{
    public long Id { get; set; }

    public long GroupId { get; set; }
    public Group Group { get; set; } = default!;

    public long SystemAiApiConfigurationId { get; set; }
    public SystemAiApiConfiguration SystemAiApiConfiguration { get; set; } = default!;

    public bool IsEnabled { get; set; } = true;

    public int ModelRole { get; set; } = 3; // 1 = Chat, 2 = Title Generation, 3 = Both

    public int OrderIndex { get; set; }

    // Rate limits (null means use system defaults)
    public int? MaxDailyRequests { get; set; }
    public int? MaxMonthlyRequests { get; set; }
    public int? MaxTotalRequests { get; set; }

    // Usage tracking
    public int DailyRequestsCount { get; set; }
    public int MonthlyRequestsCount { get; set; }
    public int TotalRequestsCount { get; set; }

    public DateTime? LastDailyReset { get; set; }
    public DateTime? LastMonthlyReset { get; set; }
}
