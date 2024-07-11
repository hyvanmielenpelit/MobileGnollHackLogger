namespace MobileGnollHackLogger.Areas.API
{
    public class LogPostResponseInfo
    {
        public long DatabaseRowId { get; set; }
        public long TopScoreDisplayIndex { get; set; }
        public long TopScoreIndex { get; set; }
        public string? TopScorePageUrl { get; set; }

    }
}
