namespace MobileGnollHackLogger.Data;

using System;

public class GroupSystemAiApiConfiguration
{
    public long Id { get; set; }

    public long GroupId { get; set; }
    public Group Group { get; set; } = default!;

    public long SystemAiApiConfigurationId { get; set; }
    public SystemAiApiConfiguration SystemAiApiConfiguration { get; set; } = default!;

    public bool IsEnabled { get; set; }
    public int OrderIndex { get; set; }

    // Rate limits
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
