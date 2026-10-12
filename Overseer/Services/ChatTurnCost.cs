namespace Overseer.Services;

using System.Collections.Generic;
using System.Linq;
using Overseer.Services.Agents;

/// <summary>
/// One chat turn's price: the main loop plus every delegated sub-agent run that reached a provider.
/// <see cref="OperatorEstimatedCost"/> and <see cref="SubAgentEstimatedCost"/> are shares of
/// <see cref="EstimatedCost"/>, never additions to it.
///
/// <para>A turn is either fully priced or unpriced: when the main model or any delegated run that made a
/// model call has no price card, every figure is null. Keeping the priced parts would quietly understate
/// the turn.</para>
/// </summary>
public sealed record ChatTurnCost(
    decimal? EstimatedCost, decimal? OperatorEstimatedCost, decimal? SubAgentEstimatedCost,
    string? PricingSource, int SubAgentRuns)
{
    /// <param name="mainCost">The main loop's cost, or null when the main model has no price card.</param>
    /// <param name="mainPricingSource">"custom" or "catalog"; null with <paramref name="mainCost"/>.</param>
    /// <param name="mainIsOperatorFunded">True when the main model ran on a system configuration.</param>
    /// <param name="delegatedRuns">Every delegated run of the turn; runs that made no model call are ignored.</param>
    public static ChatTurnCost Combine(
        decimal? mainCost, string? mainPricingSource, bool mainIsOperatorFunded,
        IReadOnlyList<DelegatedRunCost> delegatedRuns)
    {
        var runs = (delegatedRuns ?? new List<DelegatedRunCost>())
            .Where(r => r.ModelCallCount > 0)
            .ToList();

        if (!mainCost.HasValue || runs.Any(r => !r.EstimatedCost.HasValue))
        {
            return new ChatTurnCost(null, null, null, null, runs.Count);
        }

        decimal? subAgentCost = runs.Count > 0 ? runs.Sum(r => r.EstimatedCost!.Value) : null;

        var operatorRuns = runs.Where(r => r.IsOperatorFunded).ToList();
        decimal? operatorCost = mainIsOperatorFunded || operatorRuns.Count > 0
            ? (mainIsOperatorFunded ? mainCost.Value : 0m) + operatorRuns.Sum(r => r.EstimatedCost!.Value)
            : null;

        string? source = runs.All(r => r.PricingSource == mainPricingSource) ? mainPricingSource : "mixed";

        return new ChatTurnCost(
            mainCost.Value + (subAgentCost ?? 0m), operatorCost, subAgentCost, source, runs.Count);
    }
}
