namespace Overseer.Services.Agents;

using System.Threading;

public class AgentRunBudget
{
    private int _totalModelCalls;
    private int _estimatedTokens;
    private int _subAgentRunsStarted;
    private int _activeSubAgentRuns;

    public int MaxTotalModelCalls { get; set; } = 48;
    public int MaxSubAgentRuns { get; set; } = 6;
    public int MaxParallelSubAgents { get; set; } = 3;

    public int TotalModelCalls => Volatile.Read(ref _totalModelCalls);
    public int EstimatedTokens => Volatile.Read(ref _estimatedTokens);
    public int SubAgentRunsStarted => Volatile.Read(ref _subAgentRunsStarted);
    public int ActiveSubAgentRuns => Volatile.Read(ref _activeSubAgentRuns);

    public bool TryIncrementModelCall()
    {
        int current = Interlocked.Increment(ref _totalModelCalls);
        return current <= MaxTotalModelCalls;
    }

    public void AddEstimatedTokens(int tokens)
    {
        if (tokens > 0)
        {
            Interlocked.Add(ref _estimatedTokens, tokens);
        }
    }

    public bool TryStartSubAgent(bool showDebugLog, out string? error)
    {
        error = null;
        int totalStarted = Interlocked.Increment(ref _subAgentRunsStarted);
        if (totalStarted > MaxSubAgentRuns)
        {
            Interlocked.Decrement(ref _subAgentRunsStarted);
            error = $"Total subagent run limit ({MaxSubAgentRuns}) reached for this conversation turn.";
            return false;
        }

        int active = Interlocked.Increment(ref _activeSubAgentRuns);
        if (active > MaxParallelSubAgents)
        {
            Interlocked.Decrement(ref _activeSubAgentRuns);
            Interlocked.Decrement(ref _subAgentRunsStarted);
            error = $"Maximum concurrent subagent runs ({MaxParallelSubAgents}) reached. Please try again sequentially.";
            return false;
        }

        return true;
    }

    public bool TryStartSubAgentRun() => TryStartSubAgent(false, out _);

    public void EndSubAgent()
    {
        Interlocked.Decrement(ref _activeSubAgentRuns);
    }

    public void FinishSubAgentRun() => EndSubAgent();
}
