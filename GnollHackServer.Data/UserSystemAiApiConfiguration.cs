namespace MobileGnollHackLogger.Data;

using System;
using System.ComponentModel.DataAnnotations;

public class UserSystemAiApiConfiguration
{
    public long Id { get; set; }

    [MaxLength(450)]
    public string AspNetUserId { get; set; } = default!;
    public ApplicationUser AspNetUser { get; set; } = default!;

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
