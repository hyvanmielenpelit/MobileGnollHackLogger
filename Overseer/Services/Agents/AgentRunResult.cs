namespace Overseer.Services.Agents;

using MobileGnollHackLogger.Data;

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
