namespace Overseer.Tests.UnitTests;

using System.Collections.Generic;
using Overseer.Services;
using Overseer.Services.Agents;
using Overseer.Services.Benchmarking;
using Overseer.Services.Providers;
using Xunit;

/// <summary>
/// The pure usage helpers of <see cref="BenchmarkDescriptionService"/>: token normalisation,
/// reasoning-token totals, and the choice between per-call and aggregate costing. The model call
/// itself is not exercised here.
/// </summary>
public class BenchmarkDescriptionServiceTests
{
    private static readonly ModelPricing Card = new(
        InputPerMillion: 1m,
        OutputPerMillion: 2m,
        CachedInputPerMillion: 0.1m,
        CacheWritePerMillion: 1.25m);

    [Fact]
    public void NormalizeTokens_UsesProviderTotals_WhenReported()
    {
        var run = new AgentRunResult
        {
            TotalPromptTokens = 1200,
            OutputTokens = 300,
            EstimatedInputTokens = 9999,
            EstimatedOutputTokens = 9999
        };

        var (prompt, output, estimated) = BenchmarkDescriptionService.NormalizeTokens(run);

        Assert.Equal(1200, prompt);
        Assert.Equal(300, output);
        Assert.False(estimated);
    }

    [Fact]
    public void NormalizeTokens_FallsBackToEstimates_WhenProviderReportedNothing()
    {
        var run = new AgentRunResult { EstimatedInputTokens = 800, EstimatedOutputTokens = 150 };

        var (prompt, output, estimated) = BenchmarkDescriptionService.NormalizeTokens(run);

        Assert.Equal(800, prompt);
        Assert.Equal(150, output);
        Assert.True(estimated);
    }

    [Fact]
    public void NormalizeTokens_IsEstimated_WhenOnlyOutputIsMissing()
    {
        var run = new AgentRunResult { TotalPromptTokens = 500, EstimatedOutputTokens = 40 };

        var (prompt, output, estimated) = BenchmarkDescriptionService.NormalizeTokens(run);

        Assert.Equal(500, prompt);
        Assert.Equal(40, output);
        Assert.True(estimated);
    }

    [Fact]
    public void SumReasoningTokens_SumsPerCallUsages_WhenPresent()
    {
        var run = new AgentRunResult
        {
            ReasoningTokens = 1,
            ModelCallUsages = new List<TokenUsageReport>
            {
                new() { ReasoningTokens = 100 },
                new() { ReasoningTokens = 25 }
            }
        };

        Assert.Equal(125, BenchmarkDescriptionService.SumReasoningTokens(run));
    }

    [Fact]
    public void SumReasoningTokens_UsesTheAggregate_WithoutPerCallUsages()
    {
        var run = new AgentRunResult { ReasoningTokens = 42 };

        Assert.Equal(42, BenchmarkDescriptionService.SumReasoningTokens(run));
    }

    [Fact]
    public void ComputeCost_UsesPerCallUsages_WhenPresent()
    {
        var run = new AgentRunResult
        {
            // Aggregates deliberately disagree with the per-call record, so the test shows which one was costed.
            TotalPromptTokens = 5_000_000,
            UncachedInputTokens = 5_000_000,
            OutputTokens = 5_000_000,
            ModelCallUsages = new List<TokenUsageReport>
            {
                new()
                {
                    TotalPromptTokens = 1_000_000,
                    CacheReadTokens = 200_000,
                    CacheCreationTokens = 100_000,
                    UncachedInputTokens = 800_000,
                    OutputTokens = 500_000
                }
            }
        };

        // 700k base input at 1, 200k cache read at 0.1, 100k cache write at 1.25, 500k output at 2.
        Assert.Equal(0.7m + 0.02m + 0.125m + 1.0m, BenchmarkDescriptionService.ComputeCost(Card, run, null));
    }

    [Fact]
    public void ComputeCost_FallsBackToAggregates_ExcludingCacheWritesFromBaseInput()
    {
        var run = new AgentRunResult
        {
            TotalPromptTokens = 1_000_000,
            UncachedInputTokens = 800_000,
            CacheReadTokens = 200_000,
            CacheCreationTokens = 100_000,
            OutputTokens = 500_000
        };

        Assert.Equal(0.7m + 0.02m + 0.125m + 1.0m, BenchmarkDescriptionService.ComputeCost(Card, run, null));
    }

    [Fact]
    public void ComputeCost_UsesEstimates_WhenProviderReportedNothing()
    {
        var run = new AgentRunResult { EstimatedInputTokens = 1_000_000, EstimatedOutputTokens = 500_000 };

        Assert.Equal(2.0m, BenchmarkDescriptionService.ComputeCost(Card, run, null));
    }
}
