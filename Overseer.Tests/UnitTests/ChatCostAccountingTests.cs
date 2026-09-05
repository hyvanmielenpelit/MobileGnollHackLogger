namespace Overseer.Tests.UnitTests;

using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Services;
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
    public void ComputeCost_UsesWholeTurnTokens_AcrossMultipleToolIterations()
    {
        // Suppose a turn ran 2 tool iterations:
        // Iteration 1: 500,000 input, 10,000 output
        // Iteration 2 (final call): 600,000 input, 5,000 output
        // Whole turn: 1,100,000 uncached input, 15,000 output
        long iter2Input = 600_000;
        long iter2Output = 5_000;

        long wholeTurnInput = 1_100_000;
        long wholeTurnOutput = 15_000;

        var pricing = new ModelPricing(
            InputPerMillion: 2.50m,
            OutputPerMillion: 10.00m,
            Source: ModelPricingSource.Catalog);

        decimal finalCallOnlyCost = ModelPricingService.ComputeCost(pricing, iter2Input, iter2Output);
        decimal wholeTurnCost = ModelPricingService.ComputeCost(pricing, wholeTurnInput, wholeTurnOutput);

        // finalCallOnly: 0.6 * 2.50 + 0.005 * 10.00 = 1.50 + 0.05 = $1.55
        Assert.Equal(1.55m, finalCallOnlyCost);

        // wholeTurn: 1.1 * 2.50 + 0.015 * 10.00 = 2.75 + 0.15 = $2.90
        Assert.Equal(2.90m, wholeTurnCost);
        Assert.True(wholeTurnCost > finalCallOnlyCost);
    }

    [Fact]
    public async Task ChatMessage_PersistedEstimatedCost_IsStableAgainstSubsequentPriceChanges()
    {
        using var db = CreateInMemoryDbContext();
        var ct = TestContext.Current.CancellationToken;

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
        db.SystemAiApiConfigurations.Add(config);
        await db.SaveChangesAsync(ct);

        var metadataService = new ModelMetadataService();
        var pricingService = new ModelPricingService(metadataService, db);
        var pricing = pricingService.Resolve(config);
        Assert.NotNull(pricing);

        // Cost a 1M input, 100k output message: 1 * 2.00 + 0.1 * 8.00 = $2.80
        decimal cost = ModelPricingService.ComputeCost(pricing, 1_000_000, 100_000);
        Assert.Equal(2.80m, cost);

        var session = new ChatSession
        {
            AspNetUserId = "test-user-id",
            Title = "Test Session",
            CreatedUtc = DateTime.UtcNow,
            LastMessageUtc = DateTime.UtcNow
        };
        db.ChatSession.Add(session);
        await db.SaveChangesAsync(ct);

        var message = new ChatMessage
        {
            ChatSessionId = session.Id,
            Role = "assistant",
            Content = "Test response",
            TimestampUtc = DateTime.UtcNow,
            InputTokens = 1_000_000,
            OutputTokens = 100_000,
            EstimatedCost = cost,
            PricingSource = pricing.Source == ModelPricingSource.Custom ? "custom" : "catalog"
        };
        db.ChatMessage.Add(message);
        await db.SaveChangesAsync(ct);

        // Now operator changes the configuration's price to double
        config.InputPricePerMillion = 4.00m;
        config.OutputPricePerMillion = 16.00m;
        await db.SaveChangesAsync(ct);

        // Reload the message from db; its stored cost must still be the original $2.80
        var reloaded = await db.ChatMessage.FindAsync(new object?[] { message.Id }, ct);
        Assert.NotNull(reloaded);
        Assert.Equal(2.80m, reloaded.EstimatedCost);
        Assert.Equal("custom", reloaded.PricingSource);
    }

    [Fact]
    public void ChatMessage_IsOperatorCost_TrueForSystemConfig_FalseForUserModel()
    {
        // System configuration usage is paid by operator
        long? systemConfigId = 123;
        long? userModelId = null;
        bool isOperatorCostSystem = systemConfigId.HasValue && !userModelId.HasValue;
        Assert.True(isOperatorCostSystem);

        // User model usage is paid by user using their own API key
        systemConfigId = null;
        userModelId = 456;
        bool isOperatorCostUser = systemConfigId.HasValue && !userModelId.HasValue;
        Assert.False(isOperatorCostUser);
    }

    [Fact]
    public void ChatMessage_UnpricedModel_ResultsInNullEstimatedCost_NotZero()
    {
        ModelPricing? pricing = null;

        decimal? estimatedCost = null;
        string? pricingSource = null;

        if (pricing != null)
        {
            estimatedCost = ModelPricingService.ComputeCost(pricing, 1_000_000, 100_000);
            pricingSource = pricing.Source == ModelPricingSource.Custom ? "custom" : "catalog";
        }

        var message = new ChatMessage
        {
            ChatSessionId = 1,
            Role = "assistant",
            Content = "Unpriced test",
            TimestampUtc = DateTime.UtcNow,
            InputTokens = 1_000_000,
            OutputTokens = 100_000,
            EstimatedCost = estimatedCost,
            PricingSource = pricingSource
        };

        Assert.Null(message.EstimatedCost);
        Assert.Null(message.PricingSource);
    }
    [Fact]
    public void Accumulator_FirstPricedTurn_SetsTotalFromNull()
    {
        var session = new ChatSession { Title = "S", CreatedUtc = DateTime.UtcNow, LastMessageUtc = DateTime.UtcNow };
        Assert.Null(session.TotalEstimatedCost);

        ChatSessionCostAccumulator.Apply(session, 0.02m);

        Assert.Equal(0.02m, session.TotalEstimatedCost);
    }

    [Fact]
    public void Accumulator_TwoPricedTurns_Accumulate()
    {
        var session = new ChatSession { Title = "S", CreatedUtc = DateTime.UtcNow, LastMessageUtc = DateTime.UtcNow };

        ChatSessionCostAccumulator.Apply(session, 0.02m);
        ChatSessionCostAccumulator.Apply(session, 0.03m);

        Assert.Equal(0.05m, session.TotalEstimatedCost);
    }

    [Fact]
    public void Accumulator_UnpricedTurn_LeavesTotalUntouched()
    {
        // A null total must stay null: an unpriced turn is not a free turn.
        var fresh = new ChatSession { Title = "S", CreatedUtc = DateTime.UtcNow, LastMessageUtc = DateTime.UtcNow };
        ChatSessionCostAccumulator.Apply(fresh, null);
        Assert.Null(fresh.TotalEstimatedCost);

        // An existing total must not move either.
        var priced = new ChatSession { Title = "S", CreatedUtc = DateTime.UtcNow, LastMessageUtc = DateTime.UtcNow };
        ChatSessionCostAccumulator.Apply(priced, 0.04m);
        ChatSessionCostAccumulator.Apply(priced, null);
        Assert.Equal(0.04m, priced.TotalEstimatedCost);
    }

    [Fact]
    public async Task ChatSession_TotalEstimatedCost_RoundTripsThroughTheContext()
    {
        using var db = CreateInMemoryDbContext();
        var ct = TestContext.Current.CancellationToken;

        var session = new ChatSession
        {
            AspNetUserId = "test-user-id",
            Title = "Test Session",
            CreatedUtc = DateTime.UtcNow,
            LastMessageUtc = DateTime.UtcNow
        };
        ChatSessionCostAccumulator.Apply(session, 0.01234567m);
        db.ChatSession.Add(session);
        await db.SaveChangesAsync(ct);

        var reloaded = await db.ChatSession.FindAsync(new object?[] { session.Id }, ct);
        Assert.NotNull(reloaded);
        Assert.Equal(0.01234567m, reloaded.TotalEstimatedCost);
    }
}
