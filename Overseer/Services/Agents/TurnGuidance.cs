namespace Overseer.Services.Agents;

/// <summary>Which limit notices this turn has already sent to the model.</summary>
public sealed class TurnGuidanceState
{
    public bool RoundsNoticeSent { get; set; }
    public bool LastChanceNoticeSent { get; set; }
    public bool FirstTimeNoticeSent { get; set; }
    public bool LastTimeNoticeSent { get; set; }
    public bool ToolOutputNoticeSent { get; set; }
}

/// <summary>
/// The limit notices a model receives on its last tool result of a batch, and when each is due.
/// GnollBench candidates receive the same text, so a change here needs a harness bump.
/// </summary>
public static class TurnGuidance
{
    public const int RoundsNoticeThreshold = 3;
    public const double FirstTimeNoticeFraction = 0.75;
    public const double LastTimeNoticeFraction = 0.90;
    public const double ToolOutputNoticeFraction = 0.80;

    /// <summary>
    /// How many further responses can still call tools after the tool round at zero-based
    /// <paramref name="toolIterations"/>. The model-call budget is shared with sub-agents and optional.
    /// </summary>
    public static int RoundsLeft(int maxToolIterations, int toolIterations, int? maxTotalModelCalls, int totalModelCalls)
    {
        int rounds = maxToolIterations - (toolIterations + 1);
        if (maxTotalModelCalls.HasValue)
        {
            rounds = Math.Min(rounds, maxTotalModelCalls.Value - totalModelCalls);
        }
        return Math.Max(0, rounds);
    }

    /// <summary>The notice block due after this tool batch, or null when none is. Marks what it returns as sent.</summary>
    public static string? BuildNotices(
        TurnGuidanceState state,
        int roundsLeft,
        DateTime nowUtc,
        DateTime turnStartUtc,
        DateTime? deadlineUtc,
        int cumulativeToolOutput,
        int maxToolOutput)
    {
        var notices = new List<string>();

        if (roundsLeft == 1 && !state.LastChanceNoticeSent)
        {
            state.LastChanceNoticeSent = true;
            state.RoundsNoticeSent = true;
            notices.Add("[Limit notice: 1 more round of tool calls is available for this question. This is your last chance to use tools, so request everything you still need in one response. After it, you must answer from what you have.]");
        }
        else if (roundsLeft > 1 && roundsLeft <= RoundsNoticeThreshold && !state.RoundsNoticeSent)
        {
            state.RoundsNoticeSent = true;
            notices.Add($"[Limit notice: {roundsLeft} more rounds of tool calls are available for this question. Plan to finish within them.]");
        }

        if (deadlineUtc.HasValue && deadlineUtc.Value > turnStartUtc)
        {
            double fraction = (nowUtc - turnStartUtc).TotalSeconds / (deadlineUtc.Value - turnStartUtc).TotalSeconds;
            bool lastDue = fraction >= LastTimeNoticeFraction && !state.LastTimeNoticeSent;
            bool firstDue = fraction >= FirstTimeNoticeFraction && !state.FirstTimeNoticeSent;
            if (lastDue || firstDue)
            {
                state.FirstTimeNoticeSent = true;
                state.LastTimeNoticeSent |= lastDue;
                int seconds = Math.Max(0, (int)(deadlineUtc.Value - nowUtc).TotalSeconds);
                notices.Add($"[Limit notice: about {seconds} seconds remain for this question. Finish your research and start your answer soon.]");
            }
        }

        if (maxToolOutput > 0 && !state.ToolOutputNoticeSent
            && cumulativeToolOutput >= maxToolOutput * ToolOutputNoticeFraction)
        {
            state.ToolOutputNoticeSent = true;
            notices.Add("[Limit notice: most of the tool output allowed for this question is used. Later results will be cut, so request only what you still need.]");
        }

        return notices.Count == 0 ? null : string.Join("\n\n", notices);
    }
}
