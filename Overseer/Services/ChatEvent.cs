namespace Overseer.Services;

using Overseer.Services.Providers;

public class ChatEvent
{
    public string Type { get; set; } = "chunk";
    public string Data { get; set; } = "";
    public long? SessionId { get; set; }
    public int? SeqNo { get; set; }
    public TokenUsageReport? UsageReport { get; set; }
}

