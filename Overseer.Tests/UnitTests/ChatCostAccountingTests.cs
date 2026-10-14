namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Services;
using Overseer.Services.Providers;
using Xunit;

public class ChatCostAccountingTests
{
    private static ApplicationDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public void ComputeCost_WithCustomPricing_UsesTheConfiguredPerMillionRates()
    {
        using var db = CreateInMemoryDbContext();

        var config = new SystemAiApiConfiguration
        {
            DisplayName = "Test Model",
            Provider = "OpenAI",
            ModelId = "gpt-5.6",
            IsEnabled = true,
            PricingMode = "custom",
            InputPricePerMillion = 2.00m,
            OutputPricePerMillion = 8.00m
        };

        var pricingService = new ModelPricingService(new ModelMetadataService(), db);
        var pricing = pricingService.Resolve(config);
        Assert.NotNull(pricing);
        Assert.Equal(ModelPricingSource.Custom, pricing.Source);

        // Cost a 1M input, 100k output message: 1 * 2.00 + 0.1 * 8.00 = $2.80
        decimal cost = ModelPricingService.ComputeCost(pricing, 1_000_000, 100_000);
        Assert.Equal(2.80m, cost);
    }

    [Fact]
    public void Accumulator_FirstPricedTurn_SetsTotalFromNull()
    {
        var session = new ChatSession { Title = "S", CreatedUtc = DateTime.UtcNow, LastMessageUtc = DateTime.UtcNow };
        Assert.Null(session.TotalEstimatedCost);

        ChatSessionCostAccumulator.Apply(session, 0.02m, isOperatorFunded: false);

        Assert.Equal(0.02m, session.TotalEstimatedCost);
    }

    [Fact]
    public void Accumulator_TwoPricedTurns_Accumulate()
    {
        var session = new ChatSession { Title = "S", CreatedUtc = DateTime.UtcNow, LastMessageUtc = DateTime.UtcNow };

        ChatSessionCostAccumulator.Apply(session, 0.02m, isOperatorFunded: false);
        ChatSessionCostAccumulator.Apply(session, 0.03m, isOperatorFunded: false);

        Assert.Equal(0.05m, session.TotalEstimatedCost);
    }

    [Fact]
    public void Accumulator_UnpricedTurn_LeavesTotalUntouched()
    {
        // A null total must stay null: an unpriced turn is not a free turn.
        var fresh = new ChatSession { Title = "S", CreatedUtc = DateTime.UtcNow, LastMessageUtc = DateTime.UtcNow };
        ChatSessionCostAccumulator.Apply(fresh, null, isOperatorFunded: false);
        Assert.Null(fresh.TotalEstimatedCost);
        Assert.Null(fresh.TotalUserEstimatedCost);

        // An existing total must not move either.
        var priced = new ChatSession { Title = "S", CreatedUtc = DateTime.UtcNow, LastMessageUtc = DateTime.UtcNow };
        ChatSessionCostAccumulator.Apply(priced, 0.04m, isOperatorFunded: false);
        ChatSessionCostAccumulator.Apply(priced, null, isOperatorFunded: false);
        Assert.Equal(0.04m, priced.TotalEstimatedCost);
        Assert.Equal(0.04m, priced.TotalUserEstimatedCost);
    }

    [Fact]
    public void Accumulator_OperatorFundedTurn_CountsInTheFullTotalOnly()
    {
        // R1: the operator paid for this turn, so it belongs in the session's full total but not in what
        // the chat cost the user. Scrubbing the cost for display must never reach this arithmetic.
        var session = new ChatSession { Title = "S", CreatedUtc = DateTime.UtcNow, LastMessageUtc = DateTime.UtcNow };

        ChatSessionCostAccumulator.Apply(session, 0.05m, isOperatorFunded: true);

        Assert.Equal(0.05m, session.TotalEstimatedCost);
        Assert.Null(session.TotalUserEstimatedCost);
    }

    [Fact]
    public void Accumulator_UserModelTurn_CountsInBothTotals()
    {
        var session = new ChatSession { Title = "S", CreatedUtc = DateTime.UtcNow, LastMessageUtc = DateTime.UtcNow };

        ChatSessionCostAccumulator.Apply(session, 0.05m, isOperatorFunded: false);

        Assert.Equal(0.05m, session.TotalEstimatedCost);
        Assert.Equal(0.05m, session.TotalUserEstimatedCost);
    }

    [Fact]
    public void Accumulator_MixedSequence_KeepsUserTotalAtOrBelowTheFullTotal()
    {
        // R9: TotalUserEstimatedCost must never exceed TotalEstimatedCost, and the difference must be
        // exactly the operator-funded sum.
        var session = new ChatSession { Title = "S", CreatedUtc = DateTime.UtcNow, LastMessageUtc = DateTime.UtcNow };

        ChatSessionCostAccumulator.Apply(session, 0.01m, isOperatorFunded: false);
        ChatSessionCostAccumulator.Apply(session, 0.02m, isOperatorFunded: true);
        ChatSessionCostAccumulator.Apply(session, 0.04m, isOperatorFunded: false);
        ChatSessionCostAccumulator.Apply(session, null, isOperatorFunded: true);
        ChatSessionCostAccumulator.Apply(session, 0.08m, isOperatorFunded: true);

        Assert.Equal(0.15m, session.TotalEstimatedCost);
        Assert.Equal(0.05m, session.TotalUserEstimatedCost);
        Assert.True(session.TotalUserEstimatedCost <= session.TotalEstimatedCost);
        Assert.Equal(0.10m, session.TotalEstimatedCost - session.TotalUserEstimatedCost);
    }

    [Fact]
    public void ComputeCost_WithNoPerCallUsage_FallsBackToTheAggregateOverloadExactly()
    {
        // A turn whose provider reported no per-call usage must cost exactly what it costed before per-call
        // costing existed. The per-call overload returns 0 for an empty list precisely so the caller has to
        // choose the aggregate path rather than silently charging nothing.
        var pricing = new ModelPricing(10m, 50m, 1m, 12.5m);

        decimal aggregate = ModelPricingService.ComputeCost(pricing, 1_100_000, 15_000, 200_000, 50_000);
        decimal perCallOnEmpty = ModelPricingService.ComputeCost(pricing, new List<TokenUsageReport>());

        Assert.Equal(0m, perCallOnEmpty);
        Assert.Equal(
            (1_100_000 / 1_000_000m * 10m) + (15_000 / 1_000_000m * 50m) +
            (200_000 / 1_000_000m * 1m) + (50_000 / 1_000_000m * 12.5m),
            aggregate);
    }

    [Fact]
    public void Accumulator_NullOperatorShare_CountsTheWholeTurnInBothTotals()
    {
        var session = new ChatSession { Title = "S", CreatedUtc = DateTime.UtcNow, LastMessageUtc = DateTime.UtcNow };

        ChatSessionCostAccumulator.Apply(session, 0.05m, operatorFundedCost: null);

        Assert.Equal(0.05m, session.TotalEstimatedCost);
        Assert.Equal(0.05m, session.TotalUserEstimatedCost);
    }

    [Fact]
    public void Accumulator_PartialOperatorShare_CountsOnlyTheUserShareInTheUserTotal()
    {
        var session = new ChatSession { Title = "S", CreatedUtc = DateTime.UtcNow, LastMessageUtc = DateTime.UtcNow };

        ChatSessionCostAccumulator.Apply(session, 0.05m, operatorFundedCost: 0.02m);

        Assert.Equal(0.05m, session.TotalEstimatedCost);
        Assert.Equal(0.03m, session.TotalUserEstimatedCost);
    }

    [Fact]
    public void Accumulator_FullOperatorShare_LeavesTheUserTotalUntouched()
    {
        var session = new ChatSession { Title = "S", CreatedUtc = DateTime.UtcNow, LastMessageUtc = DateTime.UtcNow };

        ChatSessionCostAccumulator.Apply(session, 0.05m, operatorFundedCost: 0.05m);

        Assert.Equal(0.05m, session.TotalEstimatedCost);
        Assert.Null(session.TotalUserEstimatedCost);
    }

    [Fact]
    public void Accumulator_MixedOperatorShares_KeepUserTotalAtOrBelowTheFullTotal()
    {
        var session = new ChatSession { Title = "S", CreatedUtc = DateTime.UtcNow, LastMessageUtc = DateTime.UtcNow };

        ChatSessionCostAccumulator.Apply(session, 0.10m, operatorFundedCost: null);
        ChatSessionCostAccumulator.Apply(session, 0.08m, operatorFundedCost: 0.03m);
        ChatSessionCostAccumulator.Apply(session, 0.04m, operatorFundedCost: 0.04m);
        ChatSessionCostAccumulator.Apply(session, null, operatorFundedCost: null);
        ChatSessionCostAccumulator.Apply(session, 0.02m, isOperatorFunded: true);

        Assert.Equal(0.24m, session.TotalEstimatedCost);
        Assert.Equal(0.15m, session.TotalUserEstimatedCost);
        Assert.True(session.TotalUserEstimatedCost <= session.TotalEstimatedCost);
        Assert.Equal(0.09m, session.TotalEstimatedCost - session.TotalUserEstimatedCost);
    }

    [Fact]
    public void ComputeRunCost_WithPerCallUsage_EqualsThePerCallOverload()
    {
        var pricing = new ModelPricing(10m, 50m, 1m, 12.5m,
            LongContext: new LongContextPricing(200_000, 20m, 75m, 2m, 25m),
            ServiceTierMultipliers: new Dictionary<string, decimal> { ["priority"] = 2m });
        var calls = new List<TokenUsageReport>
        {
            new() { TotalPromptTokens = 250_000, UncachedInputTokens = 250_000, OutputTokens = 4_000 },
            new() { TotalPromptTokens = 30_000, UncachedInputTokens = 20_000, CacheReadTokens = 10_000, OutputTokens = 900 }
        };

        decimal runCost = ModelPricingService.ComputeRunCost(
            pricing, calls, "priority", null, 270_000, 4_900, 10_000, 0);

        Assert.Equal(ModelPricingService.ComputeCost(pricing, calls, "priority", null), runCost);
    }

    [Fact]
    public void ComputeRunCost_WithNoPerCallUsage_EqualsTheAggregateOverloadWithoutCacheWrites()
    {
        var pricing = new ModelPricing(10m, 50m, 1m, 12.5m);

        decimal runCost = ModelPricingService.ComputeRunCost(
            pricing, new List<TokenUsageReport>(), null, null, 140_000, 15_000, 200_000, 40_000);

        Assert.Equal(ModelPricingService.ComputeCost(pricing, 100_000, 15_000, 200_000, 40_000), runCost);
    }
}
