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

    public int ModelRole { get; set; } = 3; // Bitmask: 1 = Chat, 2 = Title Generation, 4 = Benchmark (valid: 1-7, 0 is invalid). Next capability is bit 8.

    public int OrderIndex { get; set; }

    // Rate limits (null means use system defaults)
    public int? MaxDailyChatRequests { get; set; }
    public int? MaxMonthlyChatRequests { get; set; }
    public int? MaxTotalChatRequests { get; set; }

    public int? MaxDailyTitleRequests { get; set; }
    public int? MaxMonthlyTitleRequests { get; set; }
    public int? MaxTotalTitleRequests { get; set; }

    public long? MaxDailyChatTokens { get; set; }
    public long? MaxMonthlyChatTokens { get; set; }
    public long? MaxTotalChatTokens { get; set; }

    public long? MaxDailyTitleTokens { get; set; }
    public long? MaxMonthlyTitleTokens { get; set; }
    public long? MaxTotalTitleTokens { get; set; }

    // Usage tracking
    public int DailyChatRequestsCount { get; set; }
    public int MonthlyChatRequestsCount { get; set; }
    public int TotalChatRequestsCount { get; set; }

    public int DailyTitleRequestsCount { get; set; }
    public int MonthlyTitleRequestsCount { get; set; }
    public int TotalTitleRequestsCount { get; set; }

    public long DailyChatTokensCount { get; set; }
    public long MonthlyChatTokensCount { get; set; }
    public long TotalChatTokensCount { get; set; }

    public long DailyTitleTokensCount { get; set; }
    public long MonthlyTitleTokensCount { get; set; }
    public long TotalTitleTokensCount { get; set; }

    public DateTime? LastDailyReset { get; set; }
    public DateTime? LastMonthlyReset { get; set; }
}
