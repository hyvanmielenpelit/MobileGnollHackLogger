namespace MobileGnollHackLogger.Data;

public interface IRateLimitedEntity
{
    int? MaxDailyRequests { get; set; }
    int? MaxMonthlyRequests { get; set; }
    int? MaxTotalRequests { get; set; }
    int DailyRequestsCount { get; set; }
    int MonthlyRequestsCount { get; set; }
    int TotalRequestsCount { get; set; }
    DateTime? LastDailyReset { get; set; }
    DateTime? LastMonthlyReset { get; set; }
}
