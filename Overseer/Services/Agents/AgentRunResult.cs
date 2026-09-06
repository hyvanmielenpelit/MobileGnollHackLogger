namespace Overseer.Services.Agents;

using MobileGnollHackLogger.Data;
using Overseer.Services.Providers;

public class AgentRunResult
{
    public string? FinalText { get; set; }
    public List<ChatMessageToolCall> ToolCalls { get; set; } = new();
    public int IterationsUsed { get; set; }
    public int EstimatedInputTokens { get; set; }
    public int EstimatedOutputTokens { get; set; }
    public int TotalPromptTokens { get; set; }
    public int UncachedInputTokens { get; set; }
    public int CacheReadTokens { get; set; }
    public int CacheCreationTokens { get; set; }
    public int OutputTokens { get; set; }
    public int ReasoningTokens { get; set; }
    public int EmittedDivCount { get; set; }
    public int? TimeToFirstTokenMs { get; set; }
    public int? TotalDurationMs { get; set; }
    public string? TerminationReason { get; set; }
    public string? ActualServiceTier { get; set; }
    public bool ToolBudgetExhausted { get; set; }
    public int ToolCallsBlocked { get; set; }
    public int ModelCallCount { get; set; }
    public int ToolCallCount { get; set; }

    /// <summary>
    /// One entry per model call of this turn, in call order. Exists so cost can be computed per request:
    /// a long-context rate card is keyed on a single request's prompt size, and the aggregates above have
    /// already lost that. Bounded by the run's MaxTotalModelCalls (48 by default), so it is a few dozen
    /// small records and is never persisted as a list. Empty when the provider reported no usage; costing
    /// then falls back to the aggregates.
    /// </summary>
    public List<TokenUsageReport> ModelCallUsages { get; set; } = new();

    /// <summary>
    /// Wall-clock time spent executing tool batches during this turn, summed per batch rather
    /// than per tool: tools within one batch run concurrently, so summing individual tool
    /// durations would over-count. Subtracting this from the turn duration gives the
    /// model-attributable time that the benchmark scores speed on.
    /// </summary>
    public long ToolTimeMs { get; set; }

    /// <summary>
    /// Prompt tokens from the <b>most recent</b> provider usage report, overwritten each time one
    /// arrives — unlike <see cref="TotalPromptTokens"/>, which sums them. The last report is the
    /// final iteration's, and that is the conversation's real context occupancy. Zero when no
    /// provider usage was reported.
    /// </summary>
    public int LastPromptTokens { get; set; }

    /// <summary>Output tokens from the most recent provider usage report. See
    /// <see cref="LastPromptTokens"/>.</summary>
    public int LastOutputTokens { get; set; }
}
