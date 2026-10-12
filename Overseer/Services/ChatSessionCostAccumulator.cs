namespace Overseer.Services;

using MobileGnollHackLogger.Data;

/// <summary>
/// Folds one completed assistant turn's cost into a session's running totals.
/// Costs are snapshotted, never recomputed, so this is strictly additive.
/// Overseer prices exclusively in USD; a turn is either priced in USD or unpriced — a turn with any
/// unpriced part, main loop or delegated run, is unpriced as a whole (see <see cref="ChatTurnCost"/>).
/// </summary>
public static class ChatSessionCostAccumulator
{
    /// <param name="isOperatorFunded">True when the turn ran on a system AI configuration, so the
    /// operator paid for it. Such a turn counts towards the session's full total but not towards
    /// what the chat cost the user.</param>
    public static void Apply(ChatSession session, decimal? turnCost, bool isOperatorFunded)
        => Apply(session, turnCost, isOperatorFunded ? turnCost : null);

    /// <param name="turnCost">The whole turn, sub-agent runs included, whoever paid for it.</param>
    /// <param name="operatorFundedCost">The share of <paramref name="turnCost"/> the operator paid, or
    /// null when it paid nothing. It counts towards the session's full total but not towards what the
    /// chat cost the user.</param>
    public static void Apply(ChatSession session, decimal? turnCost, decimal? operatorFundedCost)
    {
        if (session == null || !turnCost.HasValue)
        {
            return; // Unpriced turn: both totals stay as they are, never treated as zero.
        }

        session.TotalEstimatedCost = (session.TotalEstimatedCost ?? 0m) + turnCost.Value;

        decimal userShare = turnCost.Value - (operatorFundedCost ?? 0m);
        if (!operatorFundedCost.HasValue || userShare > 0m)
        {
            session.TotalUserEstimatedCost = (session.TotalUserEstimatedCost ?? 0m) + userShare;
        }
    }
}
