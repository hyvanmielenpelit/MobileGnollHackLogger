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
}
