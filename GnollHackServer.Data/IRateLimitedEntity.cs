namespace MobileGnollHackLogger.Data;

public interface IRateLimitedEntity
{
    int? MaxDailyChatRequests { get; set; }
    int? MaxMonthlyChatRequests { get; set; }
    int? MaxTotalChatRequests { get; set; }
    int DailyChatRequestsCount { get; set; }
    int MonthlyChatRequestsCount { get; set; }
    int TotalChatRequestsCount { get; set; }

    int? MaxDailyTitleRequests { get; set; }
    int? MaxMonthlyTitleRequests { get; set; }
    int? MaxTotalTitleRequests { get; set; }
    int DailyTitleRequestsCount { get; set; }
    int MonthlyTitleRequestsCount { get; set; }
    int TotalTitleRequestsCount { get; set; }

    long? MaxDailyChatTokens { get; set; }
    long? MaxMonthlyChatTokens { get; set; }
    long? MaxTotalChatTokens { get; set; }
    long DailyChatTokensCount { get; set; }
    long MonthlyChatTokensCount { get; set; }
    long TotalChatTokensCount { get; set; }

    long? MaxDailyTitleTokens { get; set; }
    long? MaxMonthlyTitleTokens { get; set; }
    long? MaxTotalTitleTokens { get; set; }
    long DailyTitleTokensCount { get; set; }
    long MonthlyTitleTokensCount { get; set; }
    long TotalTitleTokensCount { get; set; }
    DateTime? LastDailyReset { get; set; }
    DateTime? LastMonthlyReset { get; set; }
}
