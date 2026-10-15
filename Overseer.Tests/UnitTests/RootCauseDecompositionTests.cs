namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using Overseer.Services.Benchmarking.RootCause;
using Xunit;

/// <summary>
/// The exact decompositions of the root-cause engine: the LMDI speed and cost splits of a pair, a
/// model's own time and cost shares, the intelligence weight split and the points at risk.
/// </summary>
public class RootCauseDecompositionTests
{
    private static RootCauseItem SpeedItem(string key, double ttft, double decode, double gap, double calls, double tokens)
        => new()
        {
            Key = key,
            TtftMs = ttft,
            DecodeMs = decode,
            ModelTimeMs = ttft + decode + gap,
            DurationMs = ttft + decode + gap + 100,
            Calls = calls,
            OutputTokens = tokens
        };

    private static RootCauseItem CostItem(string key, double[] tokens, double[] cost)
        => new() { Key = key, Tokens = tokens, Cost = cost };

    private static List<RootCauseItem> SlowItems() => Enumerable.Range(1, 8)
        .Select(i => SpeedItem("q" + i, 2000 + 100 * i, 6000 + 300 * i, 400, 3 + i % 2, 900 + 50 * i))
        .ToList();

    private static List<RootCauseItem> FastItems() => Enumerable.Range(1, 8)
        .Select(i => SpeedItem("q" + i, 900 + 40 * i, 2500 + 90 * i, 300, 2, 600 + 20 * i))
        .ToList();

    [Fact]
    public void SpeedEffects_SumExactlyToTheModelTimeDifference()
    {
        var (effects, total, first, second) = RootCauseDecomposition.SpeedEffects(SlowItems(), FastItems());

        Assert.Equal(5, effects.Length);
        Assert.Equal(first - second, total, 9);
        Assert.Equal(total, effects.Sum(), 9);
    }

    [Fact]
    public void Speed_OfTwoModels_CarriesEveryComponent_WithReproducibleIntervals()
    {
        var a = new RootCauseModelEvidence { SpeedItems = SlowItems() };
        var b = new RootCauseModelEvidence { SpeedItems = FastItems() };

        var first = RootCauseDecomposition.Speed("A", a, "B", b);
        var second = RootCauseDecomposition.Speed("A", a, "B", b);

        Assert.Null(first.UnavailableReason);
        Assert.Equal(8, first.SharedQuestions);
        Assert.Equal(RootCauseDecomposition.SpeedComponents, first.Components.Select(c => c.Key));
        Assert.Equal(first.Difference!.Value, first.Components.Sum(c => c.Amount), 9);
        Assert.Equal(1.0, first.Components.Sum(c => c.Share!.Value), 9);
        Assert.Equal(first.Components.Select(c => (c.ShareLower, c.ShareUpper)), second.Components.Select(c => (c.ShareLower, c.ShareUpper)));
        Assert.All(first.Components, c => Assert.NotNull(c.ShareLower));
    }

    [Fact]
    public void Speed_WithFewerThanFiveSharedQuestions_IsUnavailable_WithAReason()
    {
        var a = new RootCauseModelEvidence { SpeedItems = SlowItems().Take(4).ToList() };
        var b = new RootCauseModelEvidence { SpeedItems = FastItems() };

        var result = RootCauseDecomposition.Speed("A", a, "B", b);

        Assert.NotNull(result.UnavailableReason);
        Assert.Empty(result.Components);
        Assert.Null(result.Difference);
    }

    [Fact]
    public void Speed_WithoutTelemetryOnOneSide_IsUnavailable_NeverZero()
    {
        var result = RootCauseDecomposition.Speed("A", new RootCauseModelEvidence(), "B", new RootCauseModelEvidence { SpeedItems = FastItems() });

        Assert.NotNull(result.UnavailableReason);
        Assert.Null(result.Difference);
    }

    [Fact]
    public void CostEffects_SumExactlyToTheCostDifference_IncludingAZeroComponent()
    {
        // Component 2 (cache writes) is zero on the second side: its whole difference goes to volume.
        var first = Enumerable.Range(1, 6).Select(i => CostItem("q" + i,
            new double[] { 1000 + i, 500, 200, 300 + i, 100 },
            new double[] { 0.003 + i * 1e-5, 0.0005, 0.0008, 0.0045, 0.0015 })).ToList();
        var second = Enumerable.Range(1, 6).Select(i => CostItem("q" + i,
            new double[] { 800, 900, 0, 250, 40 },
            new double[] { 0.0016, 0.00045, 0, 0.0025, 0.0004 })).ToList();

        var (effects, total, a, b) = RootCauseDecomposition.CostEffects(first, second);

        Assert.Equal(10, effects.Length);
        Assert.Equal(a - b, total, 12);
        Assert.Equal(total, effects.Sum(), 12);
        Assert.Equal(0.0008, effects[4], 12);
        Assert.Equal(0, effects[5], 12);
    }

    [Fact]
    public void Cost_SplitsVolumeAndPrice_AndTheAllRowsSumToTheDifference()
    {
        var first = Enumerable.Range(1, 6).Select(i => CostItem("q" + i,
            new double[] { 2000, 0, 0, 1000 + 10 * i, 0 },
            new double[] { 0.006, 0, 0, 0.015 + 1e-4 * i, 0 })).ToList();
        var second = Enumerable.Range(1, 6).Select(i => CostItem("q" + i,
            new double[] { 1000, 0, 0, 500, 0 },
            new double[] { 0.001, 0, 0, 0.002, 0 })).ToList();
        var a = new RootCauseModelEvidence { Priced = true, CostItems = first };
        var b = new RootCauseModelEvidence { Priced = true, CostItems = second };

        var result = RootCauseDecomposition.Cost("A", a, "B", b);

        Assert.Null(result.UnavailableReason);
        var components = result.Components.Where(c => c.Key != "all").ToList();
        Assert.Equal(10, components.Count);
        Assert.Equal(result.Difference!.Value, components.Sum(c => c.Amount), 12);
        var all = result.Components.Where(c => c.Key == "all").ToList();
        Assert.Equal(new[] { "volume", "price" }, all.Select(c => c.Effect));
        Assert.Equal(result.Difference!.Value, all.Sum(c => c.Amount), 12);
    }

    [Fact]
    public void Cost_WithoutAPriceCard_IsUnavailable()
    {
        var result = RootCauseDecomposition.Cost("A", new RootCauseModelEvidence { Priced = false }, "B", new RootCauseModelEvidence { Priced = true });

        Assert.NotNull(result.UnavailableReason);
        Assert.Empty(result.Components);
    }

    [Fact]
    public void TimeSegments_SumToTheirOwnTotal_AndOtherIsNeverNegative()
    {
        var items = SlowItems();

        var segments = RootCauseDecomposition.TimeSegments(items)!;

        Assert.Equal(5, segments.Length);
        Assert.All(segments, s => Assert.True(s >= 0));
        Assert.Equal(items.Average(i => i.DurationMs), segments.Sum(), 9);
        Assert.Null(RootCauseDecomposition.TimeSegments(Array.Empty<RootCauseItem>()));
    }

    [Fact]
    public void ModelTimeShares_SumToOne()
    {
        var shares = RootCauseDecomposition.ModelTimeShares(SlowItems())!.Value;

        Assert.Equal(1.0, shares.Shares.Sum(), 9);
        Assert.Equal(3, shares.Intervals.Length);
    }

    [Fact]
    public void QualitySplit_SumsExactlyToTheIndexDifference()
    {
        var split = RootCauseDecomposition.QualitySplit(
            "A", 82, new double?[] { 85, 78, 90, 88 },
            "B", 71, new double?[] { 70, 72, 86, 80 },
            new[] { 0.4, 0.3, 0.15, 0.15 })!;

        Assert.Equal(11, split.IndexDifference);
        Assert.Equal(new[] { "accuracy", "completeness", "conciseness", "readability", "other" }, split.Components.Select(c => c.Key));
        Assert.Equal(split.IndexDifference, split.Components.Sum(c => c.Amount), 9);
        Assert.Null(RootCauseDecomposition.QualitySplit("A", 1, new double?[] { 1, null, 1, 1 }, "B", 1, new double?[] { 1, 1, 1, 1 }, new[] { 0.25, 0.25, 0.25, 0.25 }));
    }

    [Fact]
    public void AtRisk_CountsEachAnswerOnceUnderItsStrongestSignal_AgainstTheModelsOwnMedian()
    {
        RootCauseAnswerRecord Answer(string q, double quality, bool critical = false, bool budget = false, bool noTool = false)
            => new() { QuestionKey = q, Counting = true, Quality = quality, CriticalError = critical, BudgetOrDefect = budget, NoTool = noTool };

        var answers = new[]
        {
            Answer("q1", 80),
            Answer("q2", 40, critical: true, budget: true, noTool: true),
            Answer("q3", 60, budget: true, noTool: true),
            Answer("q4", 70, noTool: true),
            Answer("q5", 75)
        };

        var risk = RootCauseDecomposition.AtRisk(answers, 80);

        // Lost points per answer = (80 − quality) / 5: q2 → 8 (critical), q3 → 4 (budget), q4 → 2 (no tool), q5 → 1 (other).
        Assert.Equal(new[] { 2.0, 4.0, 8.0, 1.0 }, risk.Points.Select(p => Math.Round(p, 9)));
        Assert.Equal(15.0, risk.Total, 9);
        Assert.Equal(5, risk.Answers);
        Assert.Equal(0, RootCauseDecomposition.AtRisk(answers, null).Total);
    }
}
