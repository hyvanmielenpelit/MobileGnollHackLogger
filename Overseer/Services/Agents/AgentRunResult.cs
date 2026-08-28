namespace Overseer.Services.Agents;

using MobileGnollHackLogger.Data;

public class AgentRunResult
{
    public string? FinalText { get; set; }
    public List<ChatMessageToolCall> ToolCalls { get; set; } = new();
    public int IterationsUsed { get; set; }
    public int EstimatedInputTokens { get; set; }
    public int EstimatedOutputTokens { get; set; }
    public int EmittedDivCount { get; set; }
    public int? TimeToFirstTokenMs { get; set; }
    public int? TotalDurationMs { get; set; }
    public string? TerminationReason { get; set; }
}
