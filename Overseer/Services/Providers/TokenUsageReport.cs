namespace Overseer.Services.Providers;

public record TokenUsageReport
{
    public int TotalPromptTokens { get; init; }
    public int CacheReadTokens { get; init; }
    public int CacheCreationTokens { get; init; }
    public int UncachedInputTokens { get; init; }
    public int OutputTokens { get; init; }
    public int ReasoningTokens { get; init; }

    /// <summary>
    /// The input tokens billed at the base input rate: the prompt minus the portion served from cache and
    /// the portion written to it. Cache reads and cache writes carry their own rates, so a provider that
    /// reports cache-creation tokens must not have them charged here as well.
    /// </summary>
    public int BillableUncachedInputTokens =>
        Math.Max(0, TotalPromptTokens - CacheReadTokens - CacheCreationTokens);
}
