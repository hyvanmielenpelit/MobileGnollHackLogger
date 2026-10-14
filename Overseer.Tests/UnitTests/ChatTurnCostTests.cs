namespace Overseer.Tests.UnitTests;

using System.Collections.Generic;
using Overseer.Services;
using Overseer.Services.Agents;
using Xunit;

public class ChatTurnCostTests
{
    private static DelegatedRunCost Run(decimal? cost, bool operatorFunded, string? source = "catalog", int calls = 1) =>
        new("wiki_researcher", "call-1", "OpenAI", "model", operatorFunded ? 5 : null, operatorFunded,
            cost, cost.HasValue ? source : null, calls, 1000, 100, 0, 0);

    [Fact]
    public void NoDelegatedRuns_UserMain_IsTheMainCostAlone()
    {
        var turn = ChatTurnCost.Combine(0.05m, "catalog", mainIsOperatorFunded: false, new List<DelegatedRunCost>());

        Assert.Equal(0.05m, turn.EstimatedCost);
        Assert.Null(turn.SubAgentEstimatedCost);
        Assert.Null(turn.OperatorEstimatedCost);
        Assert.Equal("catalog", turn.PricingSource);
        Assert.Equal(0, turn.SubAgentRuns);
    }

    [Fact]
    public void NoDelegatedRuns_SystemMain_IsAllOperator()
    {
        var turn = ChatTurnCost.Combine(0.05m, "catalog", mainIsOperatorFunded: true, new List<DelegatedRunCost>());

        Assert.Equal(0.05m, turn.EstimatedCost);
        Assert.Null(turn.SubAgentEstimatedCost);
        Assert.Equal(0.05m, turn.OperatorEstimatedCost);
    }

    [Theory]
    // User main, user run: no operator share.
    [InlineData(false, false, null)]
    // User main, operator run: the operator share is the run.
    [InlineData(false, true, 2)]
    // Operator main, user run: the operator share is the main.
    [InlineData(true, false, 5)]
    // Operator main, operator run: the operator share is the whole turn.
    [InlineData(true, true, 7)]
    public void FundingQuadrants_SumTheTurnAndSplitTheOperatorShare(bool mainOperatorFunded, bool runOperatorFunded, int? expectedOperatorCents)
    {
        var turn = ChatTurnCost.Combine(0.05m, "catalog", mainOperatorFunded, new[] { Run(0.02m, operatorFunded: runOperatorFunded) });

        Assert.Equal(0.07m, turn.EstimatedCost);
        Assert.Equal(0.02m, turn.SubAgentEstimatedCost);
        Assert.Equal(expectedOperatorCents / 100m, turn.OperatorEstimatedCost);
        Assert.Equal(1, turn.SubAgentRuns);
    }

    [Fact]
    public void RunWithNoModelCall_IsIgnored()
    {
        var turn = ChatTurnCost.Combine(0.05m, "catalog", false, new[] { Run(null, operatorFunded: true, calls: 0) });

        Assert.Equal(0.05m, turn.EstimatedCost);
        Assert.Null(turn.SubAgentEstimatedCost);
        Assert.Null(turn.OperatorEstimatedCost);
        Assert.Equal("catalog", turn.PricingSource);
        Assert.Equal(0, turn.SubAgentRuns);
    }

    [Fact]
    public void UnpricedRunThatMadeCalls_MakesTheWholeTurnUnpriced()
    {
        var turn = ChatTurnCost.Combine(0.05m, "catalog", true,
            new[] { Run(0.02m, operatorFunded: false), Run(null, operatorFunded: true) });

        Assert.Null(turn.EstimatedCost);
        Assert.Null(turn.OperatorEstimatedCost);
        Assert.Null(turn.SubAgentEstimatedCost);
        Assert.Null(turn.PricingSource);
        Assert.Equal(2, turn.SubAgentRuns);
    }

    [Fact]
    public void UnpricedMain_MakesTheWholeTurnUnpriced()
    {
        var turn = ChatTurnCost.Combine(null, null, false, new[] { Run(0.02m, operatorFunded: false) });

        Assert.Null(turn.EstimatedCost);
        Assert.Null(turn.OperatorEstimatedCost);
        Assert.Null(turn.SubAgentEstimatedCost);
        Assert.Null(turn.PricingSource);
        Assert.Equal(1, turn.SubAgentRuns);
    }

    [Fact]
    public void SameSourceEverywhere_KeepsThatSource()
    {
        var turn = ChatTurnCost.Combine(0.05m, "custom", false, new[] { Run(0.02m, false, "custom") });

        Assert.Equal("custom", turn.PricingSource);
    }

    [Fact]
    public void DifferingSources_AreMixed()
    {
        var turn = ChatTurnCost.Combine(0.05m, "catalog", false, new[] { Run(0.02m, false, "custom") });

        Assert.Equal("mixed", turn.PricingSource);
    }
}
