namespace Overseer.Services.Agents;

/// <summary>
/// One delegated agent run's spend, priced on that run's own model.
/// </summary>
public sealed record DelegatedRunCost(
    string AgentName,
    string ToolCallId,
    string Provider,
    string ModelId,
    long? SystemModelId,
    bool IsOperatorFunded,
    decimal? EstimatedCost,   // null: the model has no price card
    string? PricingSource,    // "custom" | "catalog" | null
    int ModelCallCount,
    int UncachedInputTokens,
    int OutputTokens,
    int CacheReadTokens,
    int CacheCreationTokens);
