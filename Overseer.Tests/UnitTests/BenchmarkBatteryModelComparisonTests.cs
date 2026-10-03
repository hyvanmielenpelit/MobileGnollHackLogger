namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services;
using Overseer.Services.Benchmarking;
using Xunit;

/// <summary>
/// Battery results as entries of the model comparison: read from the latest persisted analysis,
/// refused with identity only when that analysis cannot stand for the battery, charted only within
/// one definition and comparability class, and costed from the member runs on the requested basis.
/// The fixtures are <see cref="BenchmarkBatteryTestData"/>: suite A scores 60, 70, 80 at difficulty 40
/// (I = 70), suite B 90 and 50 at difficulties 20 and 60 (I = 60), so the Overall Index is 66.
/// </summary>
public class BenchmarkBatteryModelComparisonTests
{
    private const string Opus = "claude-opus-5";

    private readonly ApplicationDbContext _db = BenchmarkBatteryTestData.NewDb();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<long> SeedAnalysedAsync(
        BenchmarkBatteryDefinition definition,
        params (BenchmarkRun Run, int SuiteIndex, int Round)[] members)
    {
        long id = await BenchmarkBatteryTestData.SeedAsync(_db, definition, members);
        var (analysis, _, _, error) = await BenchmarkBatteryTestData.Service(_db).AnalyseAsync(id, null, null, Ct);
        Assert.True(analysis != null, error);
        return id;
    }

    /// <summary>One round of both suites for <paramref name="modelId"/>, analysed.</summary>
    private Task<long> SeedBatteryAsync(long firstRunId, string modelId = "gpt-5.6-luna", Action<BenchmarkRun>? adjust = null)
    {
        var a = BenchmarkBatteryTestData.SuiteARun(firstRunId, modelId);
        var b = BenchmarkBatteryTestData.SuiteBRun(firstRunId + 1, modelId);
        adjust?.Invoke(a);
        adjust?.Invoke(b);
        return SeedAnalysedAsync(BenchmarkBatteryTestData.Definition(), (a, 0, 1), (b, 1, 1));
    }

    private async Task<BenchmarkModelComparisonDto> CompareAsync(
        BenchmarkModelComparisonPricingBasis basis, params long[] batteryRunIds)
    {
        var (result, error) = await new BenchmarkModelComparisonService(_db).CompareAsync(
            new BenchmarkModelComparisonRequest { BatteryRunIds = batteryRunIds.ToList(), PricingBasis = basis }, Ct);
        Assert.True(result != null, error);
        return result!;
    }

    private Task<BenchmarkModelComparisonDto> CompareAsync(params long[] batteryRunIds)
        => CompareAsync(BenchmarkModelComparisonPricingBasis.AsRun, batteryRunIds);

    private static BenchmarkModelComparisonEntryDto Entry(BenchmarkModelComparisonDto dto, long batteryRunId)
        => dto.Entries.Single(e => e.Key == BenchmarkBatteryModelComparison.KeyOf(batteryRunId));

    private async Task<BenchmarkBatteryStatisticsResult> PersistedResultAsync(long batteryRunId)
    {
        var analysis = await BenchmarkBatteryTestData.Service(_db).GetLatestAsync(batteryRunId, Ct);
        return BenchmarkBatteryAnalysisService.DeserializeResult(analysis)!;
    }

    // --- Entries ----------------------------------------------------------------------------------------

    [Fact]
    public async Task TwoBatteryResults_AreTwoEntries_ReadFromTheirAnalyses()
    {
        long luna = await SeedBatteryAsync(1);
        long opus = await SeedAnalysedAsync(
            BenchmarkBatteryTestData.Definition(),
            (BenchmarkBatteryTestData.SuiteARun(3, Opus, shift: 10), 0, 1),
            (BenchmarkBatteryTestData.SuiteBRun(4, Opus, shift: 10), 1, 1));

        var dto = await CompareAsync(luna, opus);

        Assert.Equal(BenchmarkModelComparisonSubjectKinds.Batteries, dto.SubjectKind);
        Assert.Equal(2, dto.ComparableCount);
        Assert.Equal(0, dto.ExcludedCount);
        Assert.Equal(new[] { $"battery:{luna}", $"battery:{opus}" }, dto.BaselineEntryKeys);
        Assert.Equal("Core knowledge", dto.BaselineBatteryName);
        Assert.Null(dto.BaselineSuiteId);
        Assert.Null(dto.BaselineSuiteName);
        Assert.Null(dto.PanelDiagnostics);

        var entry = Entry(dto, luna);
        var definition = BenchmarkBatteryTestData.Definition();
        Assert.Equal(BenchmarkBatteryModelComparison.SourceKind, entry.SourceKind);
        Assert.Equal(luna, entry.SourceId);
        Assert.Equal(luna, entry.BatteryRunId);
        Assert.Equal("Core knowledge", entry.BatteryName);
        Assert.Equal(definition.DefinitionSha256, entry.BatteryDefinitionSha256);
        Assert.False(string.IsNullOrEmpty(entry.BatteryComparabilityClassSha256));
        Assert.Equal(entry.BatteryComparabilityClassSha256, dto.BaselineSignature);
        Assert.Equal(definition.DefinitionSha256, dto.BaselineKeyValues[BenchmarkBatteryModelComparison.BatteryDefinitionKey]);
        Assert.Equal(2, entry.SuiteCount);
        Assert.Equal(1, entry.RunsPerSuite);
        Assert.Null(entry.SuiteId);
        Assert.Null(entry.SuiteName);
        Assert.Equal(new long[] { 1, 2 }, entry.RunIds);
        Assert.Equal(2, entry.RunCount);
        Assert.Equal("gpt-5.6-luna", entry.ModelId);
        Assert.True(entry.Comparable);

        Assert.Equal(66.0, entry.Quality!.PointEstimate, 9);
        Assert.Equal(76.0, Entry(dto, opus).Quality!.PointEstimate, 9);
        Assert.Equal(Opus, Entry(dto, opus).ModelId);
    }

    [Fact]
    public async Task TheIdentity_IsReadFromTheNewestUsableMember()
    {
        long id = await SeedAnalysedAsync(
            BenchmarkBatteryTestData.Definition(),
            (BenchmarkBatteryTestData.SuiteARun(1), 0, 1),
            (BenchmarkBatteryTestData.SuiteBRun(2), 1, 1));

        var newest = await _db.BenchmarkRuns.SingleAsync(r => r.Id == 2, Ct);

        var entry = Entry(await CompareAsync(id), id);

        Assert.Equal(newest.StartedAtUtc, entry.LastRunStartedAtUtc);
        Assert.Equal(newest.TestedModelSnapshot.ModelId, entry.ModelId);
    }

    // --- Measures ---------------------------------------------------------------------------------------

    [Fact]
    public async Task TheMeasures_AreMappedFromThePersistedAnalysis()
    {
        long id = await SeedBatteryAsync(1, adjust: run =>
        {
            foreach (var answer in run.Answers) answer.SpeedScore = 100;
        });

        var entry = Entry(await CompareAsync(id), id);
        var result = await PersistedResultAsync(id);
        var index = result.OverallIndex!;

        var quality = entry.Quality!;
        Assert.Equal(index.PointEstimate, quality.PointEstimate, 12);
        Assert.Equal(index.CombinedHalfWidth, quality.IntervalHalfWidth);
        Assert.Equal(index.CombinedLower, quality.IntervalLower);
        Assert.Equal(index.CombinedUpper, quality.IntervalUpper);
        Assert.Equal(index.ItemSamplingHalfWidth, quality.ItemSamplingHalfWidth);
        Assert.Equal(index.ReproducibilityHalfWidth, quality.ReproducibilityHalfWidth);

        // One round: no reproducibility estimate, and the basis says so.
        Assert.False(quality.ReproducibilityAvailable);
        Assert.Null(quality.ReproducibilityStandardDeviation);
        Assert.Equal(BenchmarkBatteryModelComparison.IntervalBasisItemSamplingOnly, quality.IntervalBasis);

        // Item counts are summed over the suites: three questions and two.
        Assert.Equal(5, quality.ExamItemCount);
        Assert.Equal(5, quality.ItemCount);
        Assert.Equal(0, quality.UnscoredItemCount);

        // Speed: suite A's run spends 20 + 21 + 22 s and suite B's 20 + 21 s, so one pass is 104 s.
        var speed = entry.Speed!;
        Assert.Equal(5, speed.PooledAnswerCount);
        Assert.Equal(5, speed.TtftAnswerCount);
        Assert.Equal(result.Speed!.ModelTimeP50Ms, speed.ModelTimeP50Ms);
        Assert.Equal(result.Speed.TtftP50Ms, speed.TtftP50Ms);
        Assert.Equal(104000.0, speed.TotalModelTimePerRunMeanMs!.Value, 6);
        Assert.Null(speed.TotalModelTimeSdMs);
        Assert.False(speed.Degraded);

        var table = entry.Table!;
        Assert.Equal(result.Speed.OverallSpeedIndex, table.MeanSpeedIndex);
        Assert.Equal(70.0, table.MeanStoredQualityIndex!.Value, 9);
        Assert.Equal(0, table.UnstableItemCount);
        Assert.True(table.SpeedIndexSaturated);
        Assert.Equal(5, table.SpeedIndexScoredAnswerCount);
        Assert.Equal(5, table.SpeedIndexCeilingAnswerCount);

        // No pricing service: the cost is unknown, never zero.
        Assert.False(entry.Cost!.PricingResolved);
        Assert.Null(entry.Cost.CandidateCostPerRunUsd);
        Assert.Null(table.CostPerIndexPointUsd);
    }

    [Fact]
    public async Task ThreeCompleteRounds_ReportReproducibilityAcrossRounds_AndSpeedPerPass()
    {
        // Round composites 66, 71 and 61: a sample SD of exactly 5.
        long id = await SeedAnalysedAsync(
            BenchmarkBatteryTestData.Definition(),
            (BenchmarkBatteryTestData.SuiteARun(1), 0, 1),
            (BenchmarkBatteryTestData.SuiteBRun(2), 1, 1),
            (BenchmarkBatteryTestData.SuiteARun(3, shift: 5), 0, 2),
            (BenchmarkBatteryTestData.SuiteBRun(4, shift: 5), 1, 2),
            (BenchmarkBatteryTestData.SuiteARun(5, shift: -5), 0, 3),
            (BenchmarkBatteryTestData.SuiteBRun(6, shift: -5), 1, 3));

        var entry = Entry(await CompareAsync(id), id);
        var result = await PersistedResultAsync(id);

        Assert.Equal(BenchmarkBatteryReproducibilitySource.Rounds, result.OverallIndex!.ReproducibilitySource);
        Assert.True(entry.Quality!.ReproducibilityAvailable);
        Assert.Equal(BenchmarkBatteryModelComparison.IntervalBasisAcrossRounds, entry.Quality.IntervalBasis);
        Assert.Equal(5.0, entry.Quality.ReproducibilityStandardDeviation!.Value, 9);
        Assert.Equal(result.OverallIndex.ReproducibilityHalfWidth, entry.Quality.ReproducibilityHalfWidth);

        Assert.Equal(3, entry.RunsPerSuite);
        Assert.Equal(6, entry.RunCount);

        // Every run takes the same time, so a pass is still one run of each suite: 104 s.
        Assert.Equal(104000.0, entry.Speed!.TotalModelTimePerRunMeanMs!.Value, 6);
        Assert.Equal(15, entry.Speed.PooledAnswerCount);
    }

    // --- Refusals ---------------------------------------------------------------------------------------

    [Fact]
    public async Task UnmeasurableBatteryResults_AreExcludedWithIdentityOnly_AndTheirReasons()
    {
        long good = await SeedBatteryAsync(1);

        // No analysis at all.
        long unanalysed = await BenchmarkBatteryTestData.SeedAsync(
            _db,
            BenchmarkBatteryTestData.Definition(),
            (BenchmarkBatteryTestData.SuiteARun(11, Opus), 0, 1),
            (BenchmarkBatteryTestData.SuiteBRun(12, Opus), 1, 1));

        // An analysis over members the battery run no longer holds.
        long stale = await SeedBatteryAsync(21, Opus);
        var staleRun = await _db.BenchmarkBatteryRuns.Include(b => b.Members).SingleAsync(b => b.Id == stale, Ct);
        staleRun.Members.Single(m => m.BenchmarkRunId == 22).Superseded = true;

        // Suite B's only member withheld its index, so the analysis is incomplete.
        long incomplete = await SeedAnalysedAsync(
            BenchmarkBatteryTestData.Definition(),
            (BenchmarkBatteryTestData.SuiteARun(31, Opus), 0, 1),
            (BenchmarkBatteryTestData.Run(32, BenchmarkBatteryTestData.SuiteB, new[] { 90, 50 }, new[] { 20, 60 }, Opus,
                status: BenchmarkRunStatus.CompletedWithErrors, qualityIndex: null, terminalFailures: 1), 1, 1));

        // A battery run still in progress.
        long running = await SeedBatteryAsync(41, Opus);
        (await _db.BenchmarkBatteryRuns.SingleAsync(b => b.Id == running, Ct)).Status = BenchmarkRunSeriesStatus.Running;

        await _db.SaveChangesAsync(Ct);

        var dto = await CompareAsync(good, unanalysed, stale, incomplete, running);

        Assert.True(Entry(dto, good).Comparable);
        Assert.Equal(1, dto.ComparableCount);
        Assert.Equal(4, dto.ExcludedCount);
        Assert.Equal(new[] { $"battery:{good}" }, dto.BaselineEntryKeys);

        foreach (long id in new[] { unanalysed, stale, incomplete, running })
        {
            var entry = Entry(dto, id);
            Assert.True(entry.Excluded);
            Assert.Equal("Excluded", entry.State);
            Assert.Null(entry.Quality);
            Assert.Null(entry.Speed);
            Assert.Null(entry.Cost);
            Assert.Null(entry.Table);
            Assert.Equal(Opus, entry.ModelId);
            Assert.Equal(id, entry.BatteryRunId);
        }

        Assert.Contains("Compute the battery analysis first", Entry(dto, unanalysed).Explanation);
        Assert.Contains(BenchmarkBatteryModelComparison.RecomputeAnalysisInstruction, Entry(dto, stale).Explanation);
        Assert.Contains("incomplete", Entry(dto, incomplete).Explanation);
        Assert.Contains("has not finished", Entry(dto, running).Explanation);

        // The run ids are the usable members as they stand now.
        Assert.Equal(new long[] { 21 }, Entry(dto, stale).RunIds);
        Assert.Equal(new long[] { 31 }, Entry(dto, incomplete).RunIds);
    }

    [Fact]
    public async Task AnUnknownBatteryRun_IsAnError()
    {
        long good = await SeedBatteryAsync(1);

        var (result, error) = await new BenchmarkModelComparisonService(_db).CompareAsync(
            new BenchmarkModelComparisonRequest { BatteryRunIds = { good, 404 } }, Ct);

        Assert.Null(result);
        Assert.Equal("Battery run(s) not found: 404.", error);
    }

    [Theory]
    [InlineData(BenchmarkRunSeriesStatus.Pending, false)]
    [InlineData(BenchmarkRunSeriesStatus.Running, false)]
    [InlineData(BenchmarkRunSeriesStatus.WaitingForCap, false)]
    [InlineData(BenchmarkRunSeriesStatus.Stopped, true)]
    [InlineData(BenchmarkRunSeriesStatus.Completed, true)]
    [InlineData(BenchmarkRunSeriesStatus.CompletedWithErrors, true)]
    [InlineData(BenchmarkRunSeriesStatus.Cancelled, true)]
    [InlineData(BenchmarkRunSeriesStatus.Failed, true)]
    public void IsFinished_IsFalseOnlyWhileMembersCanStillBeAdded(BenchmarkRunSeriesStatus status, bool finished)
        => Assert.Equal(finished, BenchmarkBatteryModelComparison.IsFinished(status));

    // --- The condition ----------------------------------------------------------------------------------

    [Fact]
    public async Task AnotherBatteryDefinition_IsExcluded_NamingTheDefinition()
    {
        long luna = await SeedBatteryAsync(1);
        long opus = await SeedBatteryAsync(3, Opus);
        long equal = await SeedAnalysedAsync(
            BenchmarkBatteryTestData.Definition(BenchmarkBatteryWeightingScheme.Equal),
            (BenchmarkBatteryTestData.SuiteARun(5, "gemini-3.8-flash-lite"), 0, 1),
            (BenchmarkBatteryTestData.SuiteBRun(6, "gemini-3.8-flash-lite"), 1, 1));

        var dto = await CompareAsync(equal, luna, opus);

        Assert.Equal(new[] { $"battery:{luna}", $"battery:{opus}" }, dto.BaselineEntryKeys);

        var excluded = Entry(dto, equal);
        Assert.True(excluded.Excluded);
        Assert.Null(excluded.Quality);
        Assert.Equal(new[] { BenchmarkBatteryModelComparison.BatteryDefinitionKey }, excluded.ExcludingKeys);
        Assert.Contains("another battery definition", excluded.Explanation);
        Assert.Contains("\"Core knowledge\"", excluded.Explanation);

        var difference = Assert.Single(excluded.Differences);
        Assert.Equal(BenchmarkBatteryModelComparison.BatteryDefinitionKey, difference.Name);
        Assert.Equal(BenchmarkBatteryTestData.Definition().DefinitionSha256, difference.Variants[0].Value);
        Assert.Equal(BenchmarkBatteryTestData.Definition(BenchmarkBatteryWeightingScheme.Equal).DefinitionSha256, difference.Variants[1].Value);
    }

    [Fact]
    public async Task AnotherComparabilityClass_IsExcluded_NamingTheKeysThatDistinguishIt()
    {
        long luna = await SeedBatteryAsync(1);
        long opus = await SeedBatteryAsync(3, Opus);
        long other = await SeedBatteryAsync(5, "gemini-3.8-flash-lite", run => run.HarnessVersion = "18");

        var dto = await CompareAsync(other, luna, opus);

        Assert.Equal(new[] { $"battery:{luna}", $"battery:{opus}" }, dto.BaselineEntryKeys);

        var excluded = Entry(dto, other);
        Assert.True(excluded.Excluded);
        Assert.Null(excluded.Quality);
        Assert.Contains(BenchmarkComparabilityKey.HarnessVersionKey, excluded.ExcludingKeys);
        Assert.Contains("another comparability class", excluded.Explanation);
        Assert.Contains(BenchmarkComparabilityKey.HarnessVersionKey, excluded.Explanation);
        Assert.Equal(BenchmarkBatteryModelComparison.BatteryComparabilityClassKey, Assert.Single(excluded.Differences).Name);
        Assert.NotEqual(Entry(dto, luna).BatteryComparabilityClassSha256, excluded.BatteryComparabilityClassSha256);
    }

    private static BenchmarkBatteryComparisonSource HandSource(
        long id, string definition, string comparabilityClass, int runCount, string? refusal = null)
        => new()
        {
            Key = BenchmarkBatteryModelComparison.KeyOf(id),
            BatteryRunId = id,
            DefinitionSha256 = definition,
            Analysis = new BenchmarkBatteryAnalysis
            {
                Id = id,
                BenchmarkBatteryRunId = id,
                DefinitionSha256 = definition,
                ComparabilityClassSha256 = comparabilityClass,
                Complete = true,
                MemberRunIdsJson = "[]",
                ResultJson = "{}"
            },
            Result = new BenchmarkBatteryStatisticsResult
            {
                Complete = true,
                OverallIndex = new BenchmarkBatteryOverallIndex { PointEstimate = 50.0 }
            },
            Runs = Enumerable.Range(1, runCount).Select(i => new BenchmarkRun { Id = id * 100 + i }).ToList(),
            Refusal = refusal
        };

    [Fact]
    public void TheBaseline_IsTheLargestBucket_WhereverItsMembersAreListed()
    {
        var baseline = BenchmarkBatteryModelComparison.ChooseBaseline(new[]
        {
            HandSource(1, "def-1", "class-1", 6),
            HandSource(2, "def-1", "class-2", 2),
            HandSource(3, "def-1", "class-2", 2)
        });

        Assert.NotNull(baseline);
        Assert.Equal("class-2", baseline!.ComparabilityClassSha256);
        Assert.Equal(new[] { "battery:2", "battery:3" }, baseline.EntryKeys);
    }

    [Fact]
    public void ABaselineTie_GoesToTheMoreRuns_ThenToTheFirstListed()
    {
        var byRuns = BenchmarkBatteryModelComparison.ChooseBaseline(new[]
        {
            HandSource(1, "def-1", "class-1", 2),
            HandSource(2, "def-2", "class-2", 4)
        });
        Assert.Equal(new[] { "battery:2" }, byRuns!.EntryKeys);

        var byOrder = BenchmarkBatteryModelComparison.ChooseBaseline(new[]
        {
            HandSource(1, "def-1", "class-1", 2),
            HandSource(2, "def-2", "class-2", 2)
        });
        Assert.Equal(new[] { "battery:1" }, byOrder!.EntryKeys);
        Assert.Equal("def-1", byOrder.DefinitionSha256);
    }

    [Fact]
    public void ARefusedResult_NeverDefinesTheBaseline()
    {
        var baseline = BenchmarkBatteryModelComparison.ChooseBaseline(new[]
        {
            HandSource(1, "def-1", "class-1", 4, refusal: "Excluded: stale."),
            HandSource(2, "def-1", "class-1", 4, refusal: "Excluded: stale."),
            HandSource(3, "def-2", "class-2", 2)
        });

        Assert.Equal(new[] { "battery:3" }, baseline!.EntryKeys);
        Assert.Null(BenchmarkBatteryModelComparison.ChooseBaseline(new[] { HandSource(1, "d", "c", 2, refusal: "x") }));
    }

    // --- Degrading keys ---------------------------------------------------------------------------------

    [Fact]
    public async Task DifferingQuestionParallelism_DegradesSpeedAndCost_OnEveryBaselineEntry()
    {
        long luna = await SeedBatteryAsync(1);
        long opus = await SeedBatteryAsync(3, Opus, run => run.MaxParallelQuestionsUsed = 3);

        var dto = await CompareAsync(luna, opus);

        foreach (long id in new[] { luna, opus })
        {
            var entry = Entry(dto, id);
            Assert.False(entry.Excluded);
            Assert.Equal("Degraded", entry.State);
            Assert.True(entry.SpeedDegraded);
            Assert.True(entry.CostDegraded);
            Assert.Equal(new[] { BenchmarkComparabilityKey.QuestionParallelismKey }, entry.SpeedDegradingKeys);
            Assert.Equal(new[] { BenchmarkComparabilityKey.QuestionParallelismKey }, entry.CostDegradingKeys);
            Assert.True(entry.Speed!.Degraded);
            Assert.True(entry.Cost!.Degraded);
            Assert.NotNull(entry.Quality);
        }
    }

    [Fact]
    public async Task DifferingPricingSnapshot_DegradesCostUnderAsRun_ButNotWhenRepriced()
    {
        long luna = await SeedBatteryAsync(1);
        long opus = await SeedBatteryAsync(3, Opus, run => run.PricingSnapshotJson = "{\"candidate\":{\"inputPerMillion\":0.10}}");

        var asRun = await CompareAsync(BenchmarkModelComparisonPricingBasis.AsRun, luna, opus);
        Assert.All(asRun.Entries, e =>
        {
            Assert.True(e.CostDegraded);
            Assert.False(e.SpeedDegraded);
            Assert.Equal(new[] { BenchmarkComparabilityKey.PricingSnapshotKey }, e.CostDegradingKeys);
        });

        var current = await CompareAsync(BenchmarkModelComparisonPricingBasis.Current, luna, opus);
        Assert.All(current.Entries, e =>
        {
            Assert.True(e.Comparable);
            Assert.False(e.CostDegraded);
        });
    }

    // --- Cost -------------------------------------------------------------------------------------------

    /// <summary>$2 / M input, $10 / M output, $0.20 / M cache read.</summary>
    private static ModelPricing Card() => new(2m, 10m, 0.20m);

    [Fact]
    public async Task Cost_IsRecomputedFromTheMembers_AndAPassIsTheSumOfTheSuiteMeans()
    {
        // Round 1 runs: 1,000,000 prompt tokens of which 400,000 cache reads, and 200,000 output:
        //   $1.20 + $2.00 + $0.08 = $3.28. Round 2 runs spend 100,000 more output tokens: $4.28.
        // Each suite's mean per run is $3.78, so one pass is $7.56 and the four runs total $15.12.
        static BenchmarkRun Tokens(BenchmarkRun run, long extraOutput)
        {
            run.TotalInputTokens = 1_000_000;
            run.TotalOutputTokens = 200_000 + extraOutput;
            run.TotalCacheReadTokens = 400_000;
            return run;
        }

        long id = await SeedAnalysedAsync(
            BenchmarkBatteryTestData.Definition(),
            (Tokens(BenchmarkBatteryTestData.SuiteARun(1), 0), 0, 1),
            (Tokens(BenchmarkBatteryTestData.SuiteBRun(2), 0), 1, 1),
            (Tokens(BenchmarkBatteryTestData.SuiteARun(3), 100_000), 0, 2),
            (Tokens(BenchmarkBatteryTestData.SuiteBRun(4), 100_000), 1, 2));

        var (loaded, error) = await BenchmarkBatteryModelComparison.LoadAsync(_db, new[] { id }, Ct);
        Assert.True(loaded != null, error);

        var pricing = new Dictionary<long, BenchmarkRunPricing?>
        {
            [1] = new BenchmarkRunPricing(Candidate: Card()),
            [2] = new BenchmarkRunPricing(Candidate: Card()),
            [3] = new BenchmarkRunPricing(Candidate: Card()),
            [4] = new BenchmarkRunPricing(Candidate: Card())
        };

        var sources = loaded!.Select(s => s.WithPricing(pricing)).ToList();
        var dto = BenchmarkBatteryModelComparison.Build(
            sources, null, BenchmarkModelComparisonPricingBasis.Current, new DateOnly(2026, 10, 3), DateTime.UtcNow);

        var entry = Entry(dto, id);
        var cost = entry.Cost!;

        Assert.True(cost.PricingResolved);
        Assert.Equal("Current", cost.Basis);
        Assert.Equal(7.56, cost.CandidateCostPerRunUsd!.Value, 6);
        Assert.Equal(15.12, cost.CandidateTotalCostUsd!.Value, 6);

        // A pass asks suite A's three questions and suite B's two.
        Assert.Equal(5.0, cost.QuestionsAskedPerRun!.Value, 9);
        Assert.Equal(7.56 / 5.0, cost.CandidateCostPerQuestionUsd!.Value, 6);

        // No grading role spent tokens, so the run total is the candidate's, per pass.
        Assert.Equal(7.56, cost.TotalRunCostPerRunUsd!.Value, 6);
        Assert.Null(cost.TotalRunCostSdUsd);

        Assert.Equal(7.56 / 66.0, entry.Table!.CostPerIndexPointUsd!.Value, 6);

        // One member without a card leaves the cost unknown rather than understated.
        pricing[3] = null;
        var partial = BenchmarkBatteryModelComparison.Build(
            loaded.Select(s => s.WithPricing(pricing)).ToList(), null,
            BenchmarkModelComparisonPricingBasis.Current, new DateOnly(2026, 10, 3), DateTime.UtcNow);
        var unknown = Entry(partial, id).Cost!;
        Assert.False(unknown.PricingResolved);
        Assert.Null(unknown.CandidateCostPerRunUsd);
        Assert.Null(unknown.CandidateCostPerQuestionUsd);
        Assert.Null(Entry(partial, id).Table!.CostPerIndexPointUsd);
    }

    [Fact]
    public async Task Members_AreLoadedWithoutAnswers_AndTheirAnswerRowsAreSummarized()
    {
        long id = await SeedBatteryAsync(1, adjust: run => run.Answers[0].ActualServiceTierUsed = "priority");

        var (loaded, _) = await BenchmarkBatteryModelComparison.LoadAsync(_db, new[] { id }, Ct);
        var source = Assert.Single(loaded!);

        Assert.Null(source.Refusal);
        Assert.Equal(new long[] { 1, 2 }, source.Runs.Select(r => r.Id));
        Assert.All(source.Runs, r => Assert.Empty(r.Answers));

        Assert.Equal(3, source.AnswerSummaries[1].AnswerRowCount);
        Assert.Equal(2, source.AnswerSummaries[2].AnswerRowCount);
        Assert.Equal("priority", source.AnswerSummaries[1].ServedServiceTier);

        // The suites hold their own members, for a reader that pairs results suite by suite.
        Assert.Equal(new long[] { 1 }, source.Suites[0].Runs.Select(r => r.Id));
        Assert.Equal(new long[] { 2 }, source.Suites[1].Runs.Select(r => r.Id));
        Assert.Equal((long?)BenchmarkBatteryTestData.SuiteA, source.Suites[0].SuiteId);
        Assert.NotNull(source.Result);
        Assert.NotNull(source.Analysis);
    }
}
