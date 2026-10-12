namespace Overseer.Services;

/// <summary>What one viewer may see of a chat turn's cost. Shares are parts of <see cref="EstimatedCost"/>.</summary>
public sealed record DisclosedChatCost(
    decimal? EstimatedCost, string? PricingSource, bool IsOperatorCost,
    decimal? OperatorCost, decimal? SubAgentCost);

/// <summary>
/// The single rule for disclosing a chat turn's cost, used by the streaming <c>cost</c> event and by both
/// history loads.
///
/// <para>The operator-funded share of a turn is an operator figure: a regular user is shown only their own
/// share, and no price at all when the operator paid the whole turn — never zero, which would read as "this
/// reply was free to produce". <see cref="DisclosedChatCost.IsOperatorCost"/> travels beside it so the client
/// can tell "withheld" from "unpriced" and keep the PARTIAL badge off.</para>
///
/// <para>A row's operator share is <c>OperatorEstimatedCost</c> when set; otherwise the whole cost when the
/// main model ran on a system configuration. Replies saved before <c>SystemAiConfigurationIdUsed</c> existed
/// carry null attribution and are therefore treated as user-funded, so they still show operator cost to a
/// regular user. Known and accepted (D1 option A); also recorded in the
/// AddChatCostAttributionAndTieredPricing migration and in chat.service.ts.</para>
/// </summary>
public static class ChatCostDisclosure
{
    public static DisclosedChatCost For(
        bool viewerIsAdmin, decimal? estimatedCost, string? pricingSource,
        long? systemAiConfigurationIdUsed, decimal? operatorEstimatedCost, decimal? subAgentEstimatedCost)
    {
        decimal? operatorShare = operatorEstimatedCost
            ?? (systemAiConfigurationIdUsed.HasValue ? estimatedCost : null);

        // The first term keeps an unpriced turn on a system configuration from raising the PARTIAL badge.
        bool isOperatorCost = systemAiConfigurationIdUsed.HasValue || operatorShare > 0m;

        if (viewerIsAdmin)
        {
            return new DisclosedChatCost(
                estimatedCost, pricingSource, isOperatorCost, operatorShare, subAgentEstimatedCost);
        }

        if (!operatorShare.HasValue)
        {
            return new DisclosedChatCost(
                estimatedCost, pricingSource, isOperatorCost, null, subAgentEstimatedCost);
        }

        // The same figure ChatSessionCostAccumulator adds to TotalUserEstimatedCost.
        decimal? userShare = estimatedCost - operatorShare;
        return userShare > 0m
            ? new DisclosedChatCost(userShare, pricingSource, isOperatorCost, null, null)
            : new DisclosedChatCost(null, null, isOperatorCost, null, null);
    }
}
