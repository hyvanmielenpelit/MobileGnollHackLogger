namespace Overseer.Services;

using MobileGnollHackLogger.Data;

/// <summary>
/// Folds one completed assistant turn's cost into a session's running total.
/// Costs are snapshotted, never recomputed, so this is strictly additive.
/// Overseer prices exclusively in USD; a turn is either priced in USD or unpriced.
/// </summary>
public static class ChatSessionCostAccumulator
{
    public static void Apply(ChatSession session, decimal? turnCost)
    {
        if (session == null || !turnCost.HasValue)
        {
            return; // Unpriced turn: the total stays as it is, never treated as zero.
        }

        session.TotalEstimatedCost = (session.TotalEstimatedCost ?? 0m) + turnCost.Value;
    }
}
