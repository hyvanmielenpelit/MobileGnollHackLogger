namespace Overseer.Services.Providers;

public record TokenUsageReport
{
    public int TotalPromptTokens { get; init; }
    public int CacheReadTokens { get; init; }
    public int CacheCreationTokens { get; init; }
    public int UncachedInputTokens { get; init; }
    public int OutputTokens { get; init; }
    public int ReasoningTokens { get; init; }
}
