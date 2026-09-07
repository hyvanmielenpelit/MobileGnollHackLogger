namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services;
using Overseer.Services.Benchmarking;
using Xunit;

/// <summary>
/// The comparison builder. Every figure here is arithmetic over a hand-built fixture with token
/// counts and scores chosen so the expected value is derived in the test's own comment.
///
/// <para>The two properties being defended are the ones that make the view honest rather than
/// decorative: an excluded entry carries no measures at all, so no chart can render it; and every
/// entry's cost is recomputed from one basis, so a cost axis compares models rather than the dates
/// their prices were captured on.</para>
/// </summary>
public class BenchmarkModelComparisonServiceTests
{
    private static readonly DateOnly Today = new(2026, 9, 8);
    private static readonly DateTime ComputedAt = new(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>$2 / M input, $10 / M output, $0.20 / M cache read. Flat: no long-context card, no tiers.</summary>
    private static ModelPricing Card() => new(2m, 10m, 0.20m);

    private static BenchmarkSuite Suite() => new() { Id = 5, Name = "GnollHack Player Assistance Benchmark Suite" };

    private static BenchmarkQuestion[] Questions()
        => Enumerable.Range(1, 3)
            .Select(i => new BenchmarkQuestion
            {
                Id = i,
                BenchmarkSuiteId = 5,
                OrderIndex = i,
                QuestionText = $"Q{i}",
                Difficulty = BenchmarkDifficulty.Intermediate,
                AssessedDifficulty = 50,
                ItemRevision = 1
            })
            .ToArray();

    private static BenchmarkRun Run(
        long id,
        string modelId = "gpt-5.6-luna",
        int[]? scores = null,
        long ttftMs = 900,
        long inputTokens = 0,
        long outputTokens = 0,
        long cacheReadTokens = 0)
    {
        scores ??= new[] { 70, 80, 90 };

        var run = new BenchmarkRun
        {
            Id = id,
            BenchmarkSuiteId = 5,
            SuiteName = "GnollHack Player Assistance Benchmark Suite",
            Status = BenchmarkRunStatus.Completed,
            StartedAtUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc).AddHours(id),
            SpeedIndex = 100,

            TestedModelProviderUsed = "OpenAI",
            TestedModelIdUsed = modelId,
            TestedModelDisplayNameUsed = modelId,
            TestedModelThinkingLevelUsed = "high",
            TestedModelReasoningModeUsed = "enabled",
            TestedModelReasoningSummaryUsed = "auto",
            TestedModelServiceTierUsed = "default",
            TestedModelMaxOutputTokensUsed = 32000,
            TestedModelParallelExecutionModeUsed = MobileGnollHackLogger.Data.ParallelExecutionMode.Enabled,

            AssessorModelProviderUsed = "Google",
            AssessorModelIdUsed = "gemini-3.7-pro",
            AssessorModelParallelExecutionModeUsed = MobileGnollHackLogger.Data.ParallelExecutionMode.Enabled,

            CandidatePromptOptionsJson = "{\"verboseMode\":false,\"spoilerFreeMode\":false,\"overseerMode\":0}",
            CandidateSystemPromptSha256 = "e9b3e9a7c4d1b8f0a2e6c9d3b7f1a4e8",
            ToolGuidesSha256 = "f59d8b30a1c7e4d2b6f0a8c3e9d5b1f7",
            KnowledgeBaseHeadSha = "576ca574b2e8d0f6a4c2e8d4b0f6a2c8",

            HarnessVersion = "12",
            ScoringMethodVersion = 9,
            ScoringProfileId = 1,
            MaxParallelQuestionsUsed = 1,
            PricingSnapshotJson = "{\"candidate\":{\"inputPerMillion\":1.25}}",

            TotalInputTokens = inputTokens,
            TotalOutputTokens = outputTokens,
            TotalCacheReadTokens = cacheReadTokens
        };

        for (int i = 0; i < 3; i++)
        {
            run.Answers.Add(new BenchmarkRunAnswer
            {
                Id = id * 100 + i,
                BenchmarkRunId = id,
                BenchmarkQuestionId = i + 1,
                ItemRevisionUsed = 1,
                OrderIndex = i + 1,
                QuestionText = $"Q{i + 1}",
                Status = BenchmarkAnswerStatus.Ok,
                AssessmentStatus = BenchmarkAssessmentStatus.Scored,
                QualityScore = scores[i],
                SpeedScore = 100,
                AssessedDifficulty = 50,
                DurationMs = 30000,
                ToolTimeMs = 0,
                TimeToFirstTokenMs = ttftMs
            });
        }

        return run;
    }

    private static BenchmarkModelComparisonSource Source(
        string key, ModelPricing? card, params BenchmarkRun[] runs)
        => new()
        {
            Key = key,
            SourceKind = runs.Length == 1 ? "Run" : "Group",
            SourceId = runs[0].Id,
            Suite = Suite(),
            Questions = Questions(),
            Runs = runs,
            CandidatePricing = runs.ToDictionary(r => r.Id, _ => card)
        };

    private static BenchmarkModelComparisonDto Build(
        IEnumerable<BenchmarkModelComparisonSource> sources,
        BenchmarkModelComparisonPricingBasis basis = BenchmarkModelComparisonPricingBasis.Current)
        => BenchmarkModelComparison.Build(sources.ToList(), basis, Today, ComputedAt);

    private static BenchmarkModelComparisonEntryDto Entry(BenchmarkModelComparisonDto dto, string key)
        => dto.Entries.Single(e => e.Key == key);

    // --- One point per model ----------------------------------------------------------------------

    [Fact]
    public void TwoModels_AreTwoPoints_AndAreNeverPooled()
    {
        var dto = Build(new[]
        {
            Source("a", Card(), Run(1, "gpt-5.6-luna", new[] { 70, 80, 90 })),
            Source("b", Card(), Run(2, "gemini-3.8-flash-lite", new[] { 40, 50, 60 }))
        });

        Assert.Equal(2, dto.Entries.Count);
        Assert.Equal(2, dto.ComparableCount);
        Assert.Equal(0, dto.ExcludedCount);

        // Equal difficulty weights, so each index is the plain mean of its own run's scores.
        Assert.Equal(80.0, Entry(dto, "a").Quality!.PointEstimate, 9);
        Assert.Equal(50.0, Entry(dto, "b").Quality!.PointEstimate, 9);

        Assert.Equal("gpt-5.6-luna", Entry(dto, "a").ModelId);
        Assert.Equal(1, Entry(dto, "a").RunCount);
    }

    [Fact]
    public void ASingleRunEntry_HasNoReproducibilityStandardDeviation()
    {
        // At R = 1 there is nothing to re-run against, so the interval covers item sampling alone.
        // Reporting a reproducibility figure here would be arithmetic dressed as evidence.
        var dto = Build(new[] { Source("a", Card(), Run(1)) });
        var quality = Entry(dto, "a").Quality!;

        Assert.Null(quality.ReproducibilityStandardDeviation);
        Assert.Null(quality.ReproducibilityHalfWidth);
        Assert.False(quality.ReproducibilityAvailable);

        // The interval is still reported, and still wide: the point estimate alone is the most
        // misleading thing this view could show.
        Assert.NotNull(quality.ItemSamplingHalfWidth);
        Assert.Equal(quality.ItemSamplingHalfWidth, quality.IntervalHalfWidth);
        Assert.Contains("Item sampling only", quality.IntervalBasis);
    }

    [Fact]
    public void ThreeRunsInOneEntry_ReportBothIntervalComponents()
    {
        var dto = Build(new[]
        {
            Source("a", Card(),
                Run(1, scores: new[] { 70, 80, 90 }),
                Run(2, scores: new[] { 60, 80, 100 }),
                Run(3, scores: new[] { 80, 80, 80 }))
        });

        var quality = Entry(dto, "a").Quality!;

        // Every run's weighted mean is 80, so run-to-run variation is exactly zero and the
        // reproducibility component is present at 0 rather than absent.
        Assert.True(quality.ReproducibilityAvailable);
        Assert.Equal(0.0, quality.ReproducibilityStandardDeviation!.Value, 9);
        Assert.Contains("reproducibility", quality.IntervalBasis);
        Assert.Equal(3, Entry(dto, "a").RunCount);
    }

    // --- Exclusion withholds the measures ------------------------------------------------------------

    [Fact]
    public void DifferingScoringMethodVersion_ExcludesTheEntry_AndWithholdsEveryMeasure()
    {
        // The step-5 scenario, and the reason the exclusion lives in the service: a v8-graded run
        // must be unable to reach a chart beside a v9-graded one, not merely badged on it.
        var old = Run(3, "claude-opus-5");
        old.ScoringMethodVersion = 8;

        var dto = Build(new[]
        {
            Source("a", Card(), Run(1, "gpt-5.6-luna")),
            Source("b", Card(), Run(2, "gemini-3.8-flash-lite")),
            Source("c", Card(), old)
        });

        var excluded = Entry(dto, "c");
        Assert.True(excluded.Excluded);
        Assert.Equal("Excluded", excluded.State);
        Assert.Null(excluded.Quality);
        Assert.Null(excluded.Speed);
        Assert.Null(excluded.Cost);
        Assert.Null(excluded.Table);

        // Still returned, and still explains itself.
        Assert.Equal(new[] { BenchmarkComparabilityKey.ScoringMethodVersionKey }, excluded.ExcludingKeys);
        Assert.Single(excluded.Differences);
        Assert.Equal("claude-opus-5", excluded.ModelId);

        Assert.Equal(2, dto.ComparableCount);
        Assert.Equal(1, dto.ExcludedCount);
    }

    [Fact]
    public void MixedCandidatePromptOptions_ExcludeTheEntry()
    {
        var odd = Run(3, "claude-opus-5");
        odd.CandidatePromptOptionsJson = "{\"verboseMode\":true,\"spoilerFreeMode\":false,\"overseerMode\":0}";

        var dto = Build(new[]
        {
            Source("a", Card(), Run(1, "gpt-5.6-luna")),
            Source("b", Card(), Run(2, "gemini-3.8-flash-lite")),
            Source("c", Card(), odd)
        });

        var excluded = Entry(dto, "c");
        Assert.True(excluded.Excluded);
        Assert.Null(excluded.Quality);
        Assert.Equal(new[] { BenchmarkComparabilityKey.CandidatePromptOptionsKey }, excluded.ExcludingKeys);
    }

    [Fact]
    public void ARefusedSource_IsExcludedWithItsOwnReason()
    {
        // A group whose members do not pool cannot be one point. The reason travels with the entry.
        var refused = Source("c", Card(), Run(3, "claude-opus-5")) with
        {
            Refusal = "Excluded: the members of \"Mixed\" do not pool, so they cannot be one point."
        };

        var dto = Build(new[] { Source("a", Card(), Run(1)), refused });

        Assert.True(Entry(dto, "c").Excluded);
        Assert.Null(Entry(dto, "c").Quality);
        Assert.Contains("do not pool", Entry(dto, "c").Explanation);
    }

    // --- Pricing: one basis, arithmetic over stored totals ---------------------------------------------

    [Fact]
    public void CurrentBasis_RepricesFromStoredTokenTotals()
    {
        // 1,000,000 total prompt tokens of which 400,000 were cache reads, and 200,000 output.
        //   uncached input 600,000 @ $2 / M  = $1.20
        //   output         200,000 @ $10 / M = $2.00
        //   cache read     400,000 @ $0.20/M = $0.08
        //                                      -----
        //                                      $3.28 per run, over three questions.
        var dto = Build(
            new[]
            {
                Source("a", Card(), Run(1, inputTokens: 1_000_000, outputTokens: 200_000, cacheReadTokens: 400_000))
            },
            BenchmarkModelComparisonPricingBasis.Current);

        var cost = Entry(dto, "a").Cost!;

        Assert.True(cost.PricingResolved);
        Assert.Equal("Current", cost.Basis);
        Assert.Equal(3.28, cost.CandidateCostPerRunUsd!.Value, 6);
        Assert.Equal(3.28, cost.CandidateTotalCostUsd!.Value, 6);
        Assert.Equal(3.28 / 3.0, cost.CandidateCostPerQuestionUsd!.Value, 6);

        // Per question rather than per run, because run cost scales with suite size.
        Assert.Contains("2026-09-08", dto.PricingBasisLabel);
    }

    [Fact]
    public void TokensAreTheInvariant_SoADifferentCardChangesOnlyTheCost()
    {
        var run = Run(1, inputTokens: 1_000_000, outputTokens: 200_000, cacheReadTokens: 400_000);
        var cheap = new ModelPricing(0.20m, 1m, 0.02m);

        var dto = Build(new[] { Source("a", cheap, run) });
        var cost = Entry(dto, "a").Cost!;

        //   600,000 @ $0.20/M = $0.12; 200,000 @ $1/M = $0.20; 400,000 @ $0.02/M = $0.008.
        Assert.Equal(0.328, cost.CandidateCostPerRunUsd!.Value, 6);
        Assert.Equal(80.0, Entry(dto, "a").Quality!.PointEstimate, 9);
    }

    [Fact]
    public void UnresolvablePricing_ReportsCostAsUnknownRatherThanZero()
    {
        var dto = Build(new[] { Source("a", null, Run(1, inputTokens: 1_000_000)) });
        var cost = Entry(dto, "a").Cost!;

        Assert.False(cost.PricingResolved);
        Assert.Null(cost.CandidateCostPerQuestionUsd);
        Assert.Null(cost.CandidateCostPerRunUsd);

        // Quality and speed are unaffected: an unknown price is not an unknown score.
        Assert.NotNull(Entry(dto, "a").Quality);
        Assert.NotNull(Entry(dto, "a").Speed);
    }

    [Fact]
    public void DifferingPricingSnapshot_DegradesCostUnderAsRun_AndLeavesQualityIntact()
    {
        var b = Run(2, "gemini-3.8-flash-lite");
        b.PricingSnapshotJson = "{\"candidate\":{\"inputPerMillion\":0.10}}";

        var dto = Build(
            new[] { Source("a", Card(), Run(1)), Source("b", Card(), b) },
            BenchmarkModelComparisonPricingBasis.AsRun);

        foreach (var entry in dto.Entries)
        {
            Assert.False(entry.Excluded);
            Assert.True(entry.CostDegraded);
            Assert.True(entry.Cost!.Degraded);
            Assert.Equal(new[] { BenchmarkComparabilityKey.PricingSnapshotKey }, entry.CostDegradingKeys);

            // Prices cannot move a score.
            Assert.False(entry.SpeedDegraded);
            Assert.Equal(80.0, entry.Quality!.PointEstimate, 9);
        }
    }

    [Fact]
    public void DifferingPricingSnapshot_IsNotDegradedUnderTheCurrentBasis()
    {
        // Every entry was recomputed from today's catalog, so the stored snapshots are not what the
        // cost axis shows and cannot degrade it. This is why Current is the default.
        var b = Run(2, "gemini-3.8-flash-lite");
        b.PricingSnapshotJson = "{\"candidate\":{\"inputPerMillion\":0.10}}";

        var dto = Build(
            new[] { Source("a", Card(), Run(1)), Source("b", Card(), b) },
            BenchmarkModelComparisonPricingBasis.Current);

        Assert.All(dto.Entries, e =>
        {
            Assert.True(e.Comparable);
            Assert.False(e.CostDegraded);
        });
    }

    [Fact]
    public void AnUpcomingScheduledPriceChange_IsSurfacedOnTheEntry()
    {
        var scheduled = new ModelPricing(
            2m, 10m, 0.20m, null, ModelPricingSource.Catalog, "2026-09-01",
            LongContext: null,
            ServiceTierMultipliers: null,
            ScheduledChange: new ScheduledPricingChange(new DateOnly(2027, 1, 1), 4m, 20m, Note: "input doubles"));

        var dto = Build(new[] { Source("a", scheduled, Run(1, inputTokens: 1_000_000)) });
        var cost = Entry(dto, "a").Cost!;

        Assert.Equal("2027-01-01", cost.ScheduledChangeEffectiveFrom);
        Assert.Equal("input doubles", cost.ScheduledChangeNote);
        Assert.Equal("2026-09-01", cost.PricingAsOf);
    }

    [Fact]
    public void AScheduledPriceChangeBeyondTheHorizon_IsNotSurfaced()
    {
        var distant = new ModelPricing(
            2m, 10m, 0.20m, null, ModelPricingSource.Catalog, "2026-09-01",
            LongContext: null,
            ServiceTierMultipliers: null,
            ScheduledChange: new ScheduledPricingChange(new DateOnly(2029, 1, 1), 4m, 20m));

        var dto = Build(new[] { Source("a", distant, Run(1, inputTokens: 1_000_000)) });

        Assert.Null(Entry(dto, "a").Cost!.ScheduledChangeEffectiveFrom);
    }

    // --- Speed -----------------------------------------------------------------------------------------

    [Fact]
    public void DifferingQuestionParallelism_DegradesSpeed_AndLeavesQualityIntact()
    {
        var b = Run(2, "gemini-3.8-flash-lite");
        b.MaxParallelQuestionsUsed = 3;

        var dto = Build(new[] { Source("a", Card(), Run(1)), Source("b", Card(), b) });

        foreach (var entry in dto.Entries)
        {
            Assert.False(entry.Excluded);
            Assert.True(entry.SpeedDegraded);
            Assert.True(entry.Speed!.Degraded);
            Assert.Equal(new[] { BenchmarkComparabilityKey.QuestionParallelismKey }, entry.SpeedDegradingKeys);

            Assert.Equal(80.0, entry.Quality!.PointEstimate, 9);
        }
    }

    [Fact]
    public void TheSpeedAxis_IsTimeToFirstToken_WithModelTimeBesideIt()
    {
        var dto = Build(new[]
        {
            Source("a", Card(), Run(1, "gpt-5.6-luna", ttftMs: 900)),
            Source("b", Card(), Run(2, "gemini-3.8-flash-lite", ttftMs: 300))
        });

        Assert.Equal(900.0, Entry(dto, "a").Speed!.TtftP50Ms!.Value, 6);
        Assert.Equal(300.0, Entry(dto, "b").Speed!.TtftP50Ms!.Value, 6);
        Assert.Equal(3, Entry(dto, "a").Speed!.TtftAnswerCount);

        // Model time is the secondary column, never the axis.
        Assert.Equal(30000.0, Entry(dto, "a").Speed!.ModelTimeP50Ms!.Value, 6);
    }

    [Fact]
    public void DifferingThinkingLevel_IsDisclosedOnTheSpeedAxis_NotExcluded()
    {
        var b = Run(2, "gemini-3.8-flash-lite");
        b.TestedModelThinkingLevelUsed = "low";

        var dto = Build(new[] { Source("a", Card(), Run(1)), Source("b", Card(), b) });

        Assert.All(dto.Entries, e => Assert.False(e.Excluded));
        Assert.True(dto.ThinkingLevelsDiffer);
        Assert.Equal(BenchmarkCrossModelComparability.ThinkingLevelSpeedCaveat, dto.SpeedAxisCaveat);

        // The label disambiguates the points once the set mixes thinking levels.
        Assert.Equal("gemini-3.8-flash-lite (low)", Entry(dto, "b").Label);
    }

    // --- The measures that are deliberately not axes ------------------------------------------------------

    [Fact]
    public void SpeedIndexAndCostPerIndexPoint_AreTableColumns_AndTheirExclusionIsStated()
    {
        var dto = Build(new[]
        {
            Source("a", Card(), Run(1, inputTokens: 1_000_000, outputTokens: 200_000, cacheReadTokens: 400_000))
        });

        var table = Entry(dto, "a").Table!;

        Assert.Equal(100.0, table.MeanSpeedIndex!.Value, 6);
        Assert.True(table.SpeedIndexSaturated);
        Assert.Equal(3, table.SpeedIndexCeilingAnswerCount);
        Assert.Equal(3, table.SpeedIndexScoredAnswerCount);

        // $3.28 per run over an index of 80.
        Assert.Equal(3.28 / 80.0, table.CostPerIndexPointUsd!.Value, 6);

        var measures = dto.ExcludedMeasures.Select(m => m.Measure).ToList();
        Assert.Contains("Speed Index", measures);
        Assert.Contains("Cost per index point", measures);
        Assert.Contains("Pairwise significance", measures);
    }

    [Fact]
    public void TheComparison_NamesTheModelAxisAndTheBaselineCondition()
    {
        var dto = Build(new[]
        {
            Source("a", Card(), Run(1, "gpt-5.6-luna")),
            Source("b", Card(), Run(2, "gemini-3.8-flash-lite"))
        });

        Assert.Equal(BenchmarkCrossModelComparability.ModelAxisKeys.ToList(), dto.ModelAxisKeys);
        Assert.Equal("9", dto.BaselineKeyValues[BenchmarkComparabilityKey.ScoringMethodVersionKey]);
        Assert.Equal(5, dto.BaselineSuiteId);
        Assert.Equal(new[] { "a", "b" }, dto.BaselineEntryKeys);
        Assert.Equal(ComputedAt, dto.ComputedAtUtc);
    }
}
