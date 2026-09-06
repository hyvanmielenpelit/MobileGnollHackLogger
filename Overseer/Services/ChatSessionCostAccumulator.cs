namespace Overseer.Services;

using MobileGnollHackLogger.Data;

/// <summary>
/// Folds one completed assistant turn's cost into a session's running totals.
/// Costs are snapshotted, never recomputed, so this is strictly additive.
/// Overseer prices exclusively in USD; a turn is either priced in USD or unpriced.
/// </summary>
public static class ChatSessionCostAccumulator
{
    /// <param name="isOperatorFunded">True when the turn ran on a system AI configuration, so the
    /// operator paid for it. Such a turn counts towards the session's full total but not towards
    /// what the chat cost the user.</param>
    public static void Apply(ChatSession session, decimal? turnCost, bool isOperatorFunded)
    {
        if (session == null || !turnCost.HasValue)
        {
            return; // Unpriced turn: both totals stay as they are, never treated as zero.
        }

        session.TotalEstimatedCost = (session.TotalEstimatedCost ?? 0m) + turnCost.Value;

        if (!isOperatorFunded)
        {
            session.TotalUserEstimatedCost = (session.TotalUserEstimatedCost ?? 0m) + turnCost.Value;
        }
    }
}
