namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services;
using Overseer.Services.Benchmarking;
using Overseer.Tests.Helpers;
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

            TestedModelSnapshot = BenchmarkModelSnapshots.Model(
                provider: "OpenAI",
                modelId: modelId,
                displayName: modelId,
                thinkingLevel: "high",
                reasoningMode: "enabled",
                reasoningSummary: "auto",
                serviceTier: "default",
                maxOutputTokens: 32000,
                parallelExecutionMode: MobileGnollHackLogger.Data.ParallelExecutionMode.Enabled),

            AssessorModelSnapshot = BenchmarkModelSnapshots.Model(
                provider: "Google",
                modelId: "gemini-3.7-pro",
                parallelExecutionMode: MobileGnollHackLogger.Data.ParallelExecutionMode.Enabled),

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

    // --- Question coverage ---------------------------------------------------------------------------

    private static void AssertCoverageSumsToExam(BenchmarkModelComparisonQualityDto quality)
        => Assert.Equal(quality.ExamItemCount, quality.ItemCount + quality.UnscoredItemCount);

    [Fact]
    public void EveryQuestionScored_CoversTheWholeExam()
    {
        var quality = Entry(Build(new[] { Source("a", Card(), Run(1)) }), "a").Quality!;

        Assert.Equal(3, quality.ExamItemCount);
        Assert.Equal(3, quality.ItemCount);
        Assert.Equal(0, quality.UnscoredItemCount);
        AssertCoverageSumsToExam(quality);
    }

    [Fact]
    public void RubricsRevisedAfterTheRuns_StayInTheIndex()
    {
        // The source is measured on the exam its runs sat, built from their answers. A rubric repair
        // imported afterwards bumps ItemRevision on the live questions only, which this never reads.
        var run = Run(1);

        var source = BenchmarkModelComparisonService.BuildSource(
            "a", "Run", 1, null, new[] { run }, new Dictionary<long, ModelPricing?> { [1] = Card() }, null);
        var quality = Entry(Build(new[] { source }), "a").Quality!;

        Assert.Equal(3, quality.ExamItemCount);
        Assert.Equal(3, quality.ItemCount);
        Assert.Equal(0, quality.UnscoredItemCount);
        Assert.Equal(80.0, quality.PointEstimate, 9);
        AssertCoverageSumsToExam(quality);
    }

    [Fact]
    public async Task TheComparison_IsUnchangedByEverySuiteOperation_AndBySuiteDeletion()
    {
        var options = BenchmarkRunExamTests.InMemoryOptions();
        var seeded = await BenchmarkRunExamTests.SeedSuiteWithRunsAsync(options);

        async Task<BenchmarkModelComparisonDto> CompareAsync()
        {
            await using var db = new ApplicationDbContext(options);
            var service = new BenchmarkModelComparisonService(db);
            var (result, error) = await service.CompareAsync(new BenchmarkModelComparisonRequest
            {
                RunIds = { seeded.RunIds[0] },
                GroupIds = { seeded.GroupId },
                PricingBasis = BenchmarkModelComparisonPricingBasis.AsRun
            }, TestContext.Current.CancellationToken);
            Assert.True(result != null, error);
            return result!;
        }

        static string Figures(BenchmarkModelComparisonDto dto) => string.Join(" | ", dto.Entries
            .OrderBy(e => e.Key)
            .Select(e => $"{e.Key}:{e.Excluded}:{e.SuiteId}:{e.SuiteName}:{e.Quality?.PointEstimate}:{e.Quality?.ItemCount}:"
                + $"{e.Quality?.ExamItemCount}:{e.Quality?.UnscoredItemCount}:{e.Quality?.IntervalHalfWidth}"));

        var before = await CompareAsync();
        Assert.All(before.Entries, e => Assert.False(e.Excluded, e.Explanation));
        Assert.All(before.Entries, e => Assert.Equal(3, e.Quality!.ExamItemCount));

        await BenchmarkRunExamTests.MutateSuiteEveryWayAsync(options, seeded);
        Assert.Equal(Figures(before), Figures(await CompareAsync()));

        await BenchmarkRunExamTests.DeleteSuiteAsync(options, seeded);
        var afterDelete = await CompareAsync();
        Assert.Equal(Figures(before), Figures(afterDelete));
        Assert.Equal("Isolation Suite", afterDelete.BaselineSuiteName);
    }

    [Fact]
    public void AProviderErrorInTheOnlyRun_LeavesThatQuestionUnscored()
    {
        var run = Run(1);
        var failed = run.Answers.Single(a => a.BenchmarkQuestionId == 2);
        failed.Status = BenchmarkAnswerStatus.ProviderError;

        var quality = Entry(Build(new[] { Source("a", Card(), run) }), "a").Quality!;

        Assert.Equal(3, quality.ExamItemCount);
        Assert.Equal(2, quality.ItemCount);
        Assert.Equal(1, quality.UnscoredItemCount);
        AssertCoverageSumsToExam(quality);
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
        Assert.Equal(3.0, cost.QuestionsAskedPerRun!.Value, 9);

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
        Assert.Null(cost.QuestionsAskedPerRun);

        // Quality and speed are unaffected: an unknown price is not an unknown score.
        Assert.NotNull(Entry(dto, "a").Quality);
        Assert.NotNull(Entry(dto, "a").Speed);
    }

    [Fact]
    public void CostPerQuestion_DividesByQuestionsAsked_WhenAnAnswerFailed()
    {
        // The $3.28 run above with its third answer failed. Two items are scored, but the run
        // paid for three questions: $3.28 / 3, not $3.28 / 2.
        var run = Run(1, inputTokens: 1_000_000, outputTokens: 200_000, cacheReadTokens: 400_000);
        run.Answers[2].Status = BenchmarkAnswerStatus.ProviderError;
        run.Answers[2].QualityScore = null;

        var entry = Entry(Build(new[] { Source("a", Card(), run) }), "a");

        Assert.Equal(2, entry.Quality!.ItemCount);
        Assert.Equal(3.28 / 3.0, entry.Cost!.CandidateCostPerQuestionUsd!.Value, 6);
        Assert.Equal(3.0, entry.Cost.QuestionsAskedPerRun!.Value, 9);
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

    // --- Total run cost, grading roles included ---------------------------------------------------------

    /// <summary>$1 / M input, $5 / M output. Every grading role in these fixtures is priced on it.</summary>
    private static ModelPricing GraderCard() => new(1m, 5m);

    /// <summary>
    /// A harness-15 run whose candidate costs $3.28 (see <see cref="CurrentBasis_RepricesFromStoredTokenTotals"/>)
    /// plus <paramref name="extraCandidateOutput"/> output tokens at $10 / M, and whose graders cost
    /// $2.70 on <see cref="GraderCard"/>:
    ///   assessor        1,000,000 in, 100,000 out = $1.00 + $0.50 = $1.50
    ///   second opinion    500,000 in,  50,000 out = $0.50 + $0.25 = $0.75
    ///   claim verifier    200,000 in,  20,000 out = $0.20 + $0.10 = $0.30
    ///   synthesis         100,000 in,  10,000 out = $0.10 + $0.05 = $0.15
    /// </summary>
    private static BenchmarkRun GradedRun(long id, long extraCandidateOutput = 0)
    {
        var run = Run(id, inputTokens: 1_000_000, outputTokens: 200_000 + extraCandidateOutput, cacheReadTokens: 400_000);
        run.HarnessVersion = "15";
        run.TotalAssessmentInputTokens = 1_000_000;
        run.TotalAssessmentOutputTokens = 100_000;
        run.TotalSecondOpinionInputTokens = 500_000;
        run.TotalSecondOpinionOutputTokens = 50_000;
        run.TotalClaimVerificationInputTokens = 200_000;
        run.TotalClaimVerificationOutputTokens = 20_000;
        run.TotalSynthesisInputTokens = 100_000;
        run.TotalSynthesisOutputTokens = 10_000;
        return run;
    }

    private static BenchmarkRunPricing AllRoles(ModelPricing? grader)
        => new(Candidate: Card(), Assessor: grader, ClaimVerifier: grader, SecondOpinion: grader);

    private static BenchmarkModelComparisonSource PricedSource(
        string key, BenchmarkRunPricing pricing, params BenchmarkRun[] runs)
        => Source(key, pricing.Candidate, runs) with
        {
            RunPricing = runs.ToDictionary(r => r.Id, _ => (BenchmarkRunPricing?)pricing)
        };

    [Fact]
    public void TotalRunCost_IsCandidatePlusEveryGradingRole()
    {
        var cost = Entry(Build(new[] { PricedSource("a", AllRoles(GraderCard()), GradedRun(1)) }), "a").Cost!;

        // $3.28 candidate + $2.70 grading.
        Assert.Equal(5.98, cost.TotalRunCostPerRunUsd!.Value, 6);
        Assert.Null(cost.TotalRunCostUnavailableReason);

        // Grader tokens never reach the candidate figures.
        Assert.Equal(3.28, cost.CandidateCostPerRunUsd!.Value, 6);
        Assert.Equal(3.28 / 3.0, cost.CandidateCostPerQuestionUsd!.Value, 6);
    }

    [Fact]
    public void TotalRunCost_HasSampleSd_AtTwoRuns_AndNoneAtOne()
    {
        // The second run spends 100,000 more candidate output tokens: $1.00 more, so $6.98.
        // Mean $6.48; sample SD sqrt((0.5² + 0.5²) / 1) = sqrt(0.5).
        var pooled = Entry(Build(new[]
        {
            PricedSource("a", AllRoles(GraderCard()), GradedRun(1), GradedRun(2, extraCandidateOutput: 100_000))
        }), "a").Cost!;

        Assert.Equal(6.48, pooled.TotalRunCostPerRunUsd!.Value, 6);
        Assert.Equal(Math.Sqrt(0.5), pooled.TotalRunCostSdUsd!.Value, 6);

        var single = Entry(Build(new[] { PricedSource("a", AllRoles(GraderCard()), GradedRun(1)) }), "a").Cost!;
        Assert.Null(single.TotalRunCostSdUsd);
    }

    [Fact]
    public void TotalRunCost_IsNull_WhenAGradingRoleIsUnpriced()
    {
        var pricing = AllRoles(GraderCard()) with { SecondOpinion = null };
        var cost = Entry(Build(new[] { PricedSource("a", pricing, GradedRun(1)) }), "a").Cost!;

        Assert.Null(cost.TotalRunCostPerRunUsd);
        Assert.Null(cost.TotalRunCostSdUsd);
        Assert.Contains("no price card", cost.TotalRunCostUnavailableReason);

        // The candidate figures still stand.
        Assert.True(cost.PricingResolved);
        Assert.Equal(3.28, cost.CandidateCostPerRunUsd!.Value, 6);
    }

    [Fact]
    public void TotalRunCost_IsNull_ForARunBeforeHarness15()
    {
        var old = GradedRun(1);
        old.HarnessVersion = "14";

        var cost = Entry(Build(new[] { PricedSource("a", AllRoles(GraderCard()), old) }), "a").Cost!;

        Assert.Null(cost.TotalRunCostPerRunUsd);
        Assert.Contains("harness 15", cost.TotalRunCostUnavailableReason);
        Assert.Equal(3.28, cost.CandidateCostPerRunUsd!.Value, 6);
    }

    [Fact]
    public void TotalRunCost_IsNull_OnAnExcludedEntry()
    {
        var old = GradedRun(3);
        old.ScoringMethodVersion = 8;

        var dto = Build(new[]
        {
            PricedSource("a", AllRoles(GraderCard()), GradedRun(1)),
            PricedSource("b", AllRoles(GraderCard()), GradedRun(2)),
            PricedSource("c", AllRoles(GraderCard()), old)
        });

        Assert.True(Entry(dto, "c").Excluded);
        Assert.Null(Entry(dto, "c").Cost);
        Assert.NotNull(Entry(dto, "a").Cost!.TotalRunCostPerRunUsd);
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
    public void TheSpeedAxis_IsModelTime_WithTtftBesideIt()
    {
        var dto = Build(new[]
        {
            Source("a", Card(), Run(1, "gpt-5.6-luna", ttftMs: 900)),
            Source("b", Card(), Run(2, "gemini-3.8-flash-lite", ttftMs: 300))
        });

        Assert.Equal(900.0, Entry(dto, "a").Speed!.TtftP50Ms!.Value, 6);
        Assert.Equal(300.0, Entry(dto, "b").Speed!.TtftP50Ms!.Value, 6);
        Assert.Equal(3, Entry(dto, "a").Speed!.TtftAnswerCount);

        // Each of "a"'s three answers carries DurationMs 30000 with no tool time, so every model-time
        // figure resolves to the same 30000 ms.
        Assert.Equal(30000.0, Entry(dto, "a").Speed!.ModelTimeP50Ms!.Value, 6);
        Assert.Equal(30000.0, Entry(dto, "a").Speed!.ModelTimeMeanMs!.Value, 6);
        Assert.Equal(90000.0, Entry(dto, "a").Speed!.TotalModelTimePerRunMeanMs!.Value, 6);
    }

    [Fact]
    public void DifferingThinkingLevel_IsDisclosedOnTheSpeedAxis_NotExcluded()
    {
        var b = Run(2, "gemini-3.8-flash-lite");
        b.TestedModelSnapshot.ThinkingLevel = "low";

        var dto = Build(new[] { Source("a", Card(), Run(1)), Source("b", Card(), b) });

        Assert.All(dto.Entries, e => Assert.False(e.Excluded));
        Assert.True(dto.ThinkingLevelsDiffer);
        Assert.Equal(BenchmarkCrossModelComparability.ThinkingLevelSpeedCaveat, dto.SpeedAxisCaveat);

        // The label disambiguates the points once the set mixes thinking levels.
        Assert.Equal("gemini-3.8-flash-lite (low)", Entry(dto, "b").Label);
    }

    // --- The measures that are deliberately not axes ------------------------------------------------------

    [Fact]
    public void SpeedIndexAndCostPerIndexPoint_AreComputedOnTheTableDto()
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
    }

    [Fact]
    public void ExcludedMeasures_NeverListTheSpeedIndex_WhichIsAChartableMeasure()
    {
        var dto = Build(new[]
        {
            Source("a", Card(), Run(1, "gpt-5.6-luna")),
            Source("b", Card(), Run(2, "gemini-3.8-flash-lite"))
        });

        Assert.Equal(
            new[] { "Cost per index point", "Pairwise significance" },
            dto.ExcludedMeasures.Select(m => m.Measure));
        Assert.All(dto.ExcludedMeasures, m =>
        {
            Assert.False(string.IsNullOrWhiteSpace(m.Summary));
            Assert.False(string.IsNullOrWhiteSpace(m.Reason));
            Assert.False(string.IsNullOrWhiteSpace(m.Instead));
        });
        Assert.All(dto.ExcludedMeasures, m => Assert.DoesNotContain("step", m.Instead ?? "", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ExcludedMeasures_AreEmpty_BelowTwoChartableEntries()
    {
        var dto = Build(new[] { Source("a", Card(), Run(1)) });

        Assert.Empty(dto.ExcludedMeasures);
    }

    [Fact]
    public void PairwiseSignificance_IsWordedForTheChartedCount()
    {
        var pair = Build(new[]
        {
            Source("a", Card(), Run(1, "gpt-5.6-luna")),
            Source("b", Card(), Run(2, "gemini-3.8-flash-lite"))
        });
        var trio = Build(new[]
        {
            Source("a", Card(), Run(1, "gpt-5.6-luna")),
            Source("b", Card(), Run(2, "gemini-3.8-flash-lite")),
            Source("c", Card(), Run(3, "claude-sonnet-5"))
        });

        Assert.Contains("the two models",
            pair.ExcludedMeasures.Single(m => m.Measure == "Pairwise significance").Summary);
        Assert.Contains("these 3 models",
            trio.ExcludedMeasures.Single(m => m.Measure == "Pairwise significance").Summary);
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
