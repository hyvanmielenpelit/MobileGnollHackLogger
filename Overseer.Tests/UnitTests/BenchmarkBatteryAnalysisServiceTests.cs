namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MobileGnollHackLogger.Data;
using Overseer.Services.Benchmarking;
using Overseer.Tests.Helpers;
using Xunit;

/// <summary>
/// Battery runs, member runs and definitions for the battery analysis and report tests. Every run is
/// one model under one instrument; a test moves exactly the key it is about.
/// </summary>
internal static class BenchmarkBatteryTestData
{
    public const long SuiteA = 21;
    public const long SuiteB = 22;

    private const string NoBoardPromptSha = "a1b2c3d4e5f60718293a4b5c6d7e8f90a1b2c3d4e5f60718293a4b5c6d7e8f90";
    private const string GuidesSha = "f59d8b30a1c7e4d2b6f0a8c3e9d5b1f7a3c9e5d1b7f3a9c5e1d7b3f9a5c1e7d3";
    private const string KnowledgeSha = "576ca574b2e8d0f6a4c2e8d4b0f6a2c8";

    public static string SuiteName(long suiteId) => $"Suite {suiteId}";

    /// <summary>One completed, fully keyed run of one suite, one answer per score.</summary>
    public static BenchmarkRun Run(
        long runId,
        long suiteId,
        int[] scores,
        int[] difficulties,
        string modelId = "gpt-5.6-luna",
        string harnessVersion = "17",
        BenchmarkRunStatus status = BenchmarkRunStatus.Completed,
        int? qualityIndex = 70,
        int? terminalFailures = null,
        DateTime? startedAtUtc = null)
    {
        var run = new BenchmarkRun
        {
            Id = runId,
            BenchmarkSuiteId = suiteId,
            BenchmarkSuiteIdUsed = suiteId,
            SuiteName = SuiteName(suiteId),
            Status = status,
            QualityIndex = qualityIndex,
            SpeedIndex = 80,
            TerminalFailureAnswerCount = terminalFailures,
            TotalQuestionCount = scores.Length,
            StartedAtUtc = startedAtUtc ?? new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc).AddMinutes(runId),

            TestedModelSnapshot = BenchmarkModelSnapshots.Model(
                provider: "OpenAI",
                modelId: modelId,
                displayName: modelId,
                thinkingLevel: "high",
                maxOutputTokens: 32000),
            AssessorModelSnapshot = BenchmarkModelSnapshots.Model(
                provider: "Google",
                modelId: "gemini-3.7-pro",
                displayName: "Gemini 3.7 Pro"),
            AssessorEffectiveMaxOutputTokens = 16000,

            CandidatePromptOptionsJson = new BenchmarkCandidatePromptOptions { VerboseMode = false, HasGameSnapshot = false }.ToCanonicalJson(),
            CandidateSystemPromptSha256 = NoBoardPromptSha,
            ToolGuidesSha256 = GuidesSha,
            KnowledgeBaseHeadSha = KnowledgeSha,
            WikiHeadSha = "wiki-head-1",
            SourceCodeHeadSha = "source-head-1",

            HarnessVersion = harnessVersion,
            ScoringMethodVersion = 9,
            ScoringProfileId = 1,
            ScoringProfileSnapshotJson = "{\"SpeedTargetMs\":15000,\"SpeedDecayK\":20.0}",

            MaxToolCallsPerQuestionUsed = 45,
            ToolIterationCapsJson = "{\"Simple\":22,\"Intermediate\":22,\"Advanced\":22}",
            TotalModelCallCapsJson = "{\"Simple\":28,\"Intermediate\":28,\"Advanced\":28}",
            QuestionTimeoutSecondsJson = "{\"Simple\":420,\"Intermediate\":600,\"Advanced\":720}",
            MaxParallelQuestionsUsed = 1,
            PricingSnapshotJson = "{\"candidate\":{\"inputPerMillion\":1.25}}"
        };

        for (int q = 0; q < scores.Length; q++)
        {
            run.Answers.Add(new BenchmarkRunAnswer
            {
                Id = runId * 100 + q + 1,
                BenchmarkRunId = runId,
                BenchmarkQuestionId = suiteId * 100 + q + 1,
                BenchmarkQuestionIdUsed = suiteId * 100 + q + 1,
                ItemRevisionUsed = 1,
                AssessedDifficulty = difficulties[q],
                OrderIndex = q + 1,
                QuestionText = $"S{suiteId}Q{q + 1}",
                AnswerText = "An answer.",
                Status = BenchmarkAnswerStatus.Ok,
                AssessmentStatus = BenchmarkAssessmentStatus.Scored,
                QualityScore = scores[q],
                DurationMs = 20000 + 1000 * q,
                ToolTimeMs = 0,
                TimeToFirstTokenMs = 500 + 10 * q
            });
        }

        return run;
    }

    public static BenchmarkBatteryDefinition Definition(
        BenchmarkBatteryWeightingScheme scheme = BenchmarkBatteryWeightingScheme.DifficultyMass)
        => new(
            1,
            "Core knowledge",
            1,
            scheme,
            new[]
            {
                new BenchmarkBatteryDefinitionSuite(0, SuiteA, SuiteName(SuiteA), null),
                new BenchmarkBatteryDefinitionSuite(1, SuiteB, SuiteName(SuiteB), null)
            });

    /// <summary>
    /// Saves the runs and a battery run whose members are the runs, each at its (suite index, round).
    /// Returns the battery run id.
    /// </summary>
    public static async Task<long> SeedAsync(
        ApplicationDbContext db,
        BenchmarkBatteryDefinition definition,
        params (BenchmarkRun Run, int SuiteIndex, int Round)[] members)
    {
        foreach (var member in members)
        {
            if (!db.BenchmarkRuns.Local.Any(r => r.Id == member.Run.Id))
            {
                db.BenchmarkRuns.Add(member.Run);
            }
        }

        var batteryRun = new BenchmarkBatteryRun
        {
            BatteryName = definition.Name,
            DefinitionJson = definition.ToJson(),
            DefinitionSha256 = definition.DefinitionSha256,
            RunsPerSuite = members.Length == 0 ? 1 : members.Max(m => m.Round),
            RequestedMemberCount = members.Length,
            Status = BenchmarkRunSeriesStatus.Completed,
            StartRequestJson = "{}",
            StartedAtUtc = new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc),
            CompletedAtUtc = new DateTime(2026, 10, 1, 12, 5, 0, DateTimeKind.Utc)
        };

        foreach (var member in members)
        {
            batteryRun.Members.Add(new BenchmarkBatteryRunMember
            {
                BenchmarkRunId = member.Run.Id,
                SuiteIndex = member.SuiteIndex,
                Round = member.Round
            });
        }

        db.BenchmarkBatteryRuns.Add(batteryRun);
        await db.SaveChangesAsync();
        return batteryRun.Id;
    }

    /// <summary>Suite A: three questions at difficulty 40 scored 60, 70, 80 (I = 70).</summary>
    public static BenchmarkRun SuiteARun(long runId, string modelId = "gpt-5.6-luna", int shift = 0)
        => Run(runId, SuiteA, new[] { 60 + shift, 70 + shift, 80 + shift }, new[] { 40, 40, 40 }, modelId);

    /// <summary>Suite B: two questions at difficulties 20 and 60 scored 90 and 50 (I = 60).</summary>
    public static BenchmarkRun SuiteBRun(long runId, string modelId = "gpt-5.6-luna", int shift = 0)
        => Run(runId, SuiteB, new[] { 90 + shift, 50 + shift }, new[] { 20, 60 }, modelId);

    public static ApplicationDbContext NewDb()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    public static BenchmarkBatteryAnalysisService Service(ApplicationDbContext db)
        => new(db, NullLogger<BenchmarkBatteryAnalysisService>.Instance);
}

/// <summary>
/// The battery analysis service: comparability refusal over the usable members, the composite end to
/// end over the database, the exclusion of a member whose index was withheld, persistence, and the
/// paired comparison of two battery runs.
/// </summary>
public class BenchmarkBatteryAnalysisServiceTests
{
    private readonly ApplicationDbContext _db = BenchmarkBatteryTestData.NewDb();

    private BenchmarkBatteryAnalysisService Service() => BenchmarkBatteryTestData.Service(_db);

    [Fact]
    public async Task Analyse_RefusesABatteryWideDifference_AndPersistsNothing()
    {
        long id = await BenchmarkBatteryTestData.SeedAsync(
            _db,
            BenchmarkBatteryTestData.Definition(),
            (BenchmarkBatteryTestData.SuiteARun(1), 0, 1),
            (BenchmarkBatteryTestData.Run(2, BenchmarkBatteryTestData.SuiteB, new[] { 90, 50 }, new[] { 20, 60 }, harnessVersion: "18"), 1, 1));

        var (analysis, result, comparison, error) = await Service().AnalyseAsync(id, "user-1", null, TestContext.Current.CancellationToken);

        Assert.Null(analysis);
        Assert.Null(result);
        Assert.Null(comparison);
        Assert.NotNull(error);
        Assert.Contains(BenchmarkComparabilityKey.HarnessVersionKey, error);
        Assert.Empty(await _db.BenchmarkBatteryAnalyses.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Analyse_TwoSuites_ComputesTheDifficultyMassCompositeAndPersistsIt()
    {
        var definition = BenchmarkBatteryTestData.Definition();
        long id = await BenchmarkBatteryTestData.SeedAsync(
            _db,
            definition,
            (BenchmarkBatteryTestData.SuiteARun(1), 0, 1),
            (BenchmarkBatteryTestData.SuiteBRun(2), 1, 1));

        var (analysis, result, comparison, error) = await Service().AnalyseAsync(id, "user-1", null, TestContext.Current.CancellationToken);

        Assert.Null(error);
        Assert.Null(comparison);
        Assert.NotNull(analysis);
        Assert.NotNull(result);

        // D_A = 3 × 40 = 120 and D_B = 20 + 60 = 80, so w = 0.6 / 0.4; I_A = 70 and I_B = 60.
        Assert.True(result!.Complete);
        Assert.Equal(2, result.CompletedSuiteCount);
        Assert.Equal(0.6, result.Weights[0], 9);
        Assert.Equal(0.4, result.Weights[1], 9);
        Assert.Equal(70.0, result.Suites[0].Index!.Value, 9);
        Assert.Equal(60.0, result.Suites[1].Index!.Value, 9);
        Assert.NotNull(result.OverallIndex);
        Assert.Equal(66.0, result.OverallIndex!.PointEstimate, 9);

        // The default scheme's identity: the pooled difficulty-weighted mean over every question.
        double pooled = (40.0 * (60 + 70 + 80) + 20.0 * 90 + 60.0 * 50) / (120.0 + 80.0);
        Assert.Equal(pooled, result.OverallIndex.PointEstimate, 9);
        Assert.True(result.PooledIdentityHolds);

        // Pooled answer timings reach the speed composite: five answers, all with a first token.
        Assert.NotNull(result.Speed);
        Assert.Equal(5, result.Speed!.PooledAnswerCount);
        Assert.Equal(5, result.Speed.TtftAnswerCount);

        Assert.True(analysis!.Complete);
        Assert.Equal(definition.DefinitionSha256, analysis.DefinitionSha256);
        Assert.False(string.IsNullOrWhiteSpace(analysis.ComparabilityClassSha256));
        Assert.Equal("17", analysis.HarnessVersion);
        Assert.Equal(9, analysis.ScoringMethodVersion);
        Assert.Equal("user-1", analysis.ComputedByUserId);
        Assert.Null(analysis.ComparedWithBatteryRunId);
        Assert.Equal(new long[] { 1, 2 }, JsonSerializer.Deserialize<long[]>(analysis.MemberRunIdsJson));
        Assert.Single(await _db.BenchmarkBatteryAnalyses.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Analyse_ExcludesAMemberWithAWithheldIndex_AndTheSuiteIsIncompleteAtROne()
    {
        long id = await BenchmarkBatteryTestData.SeedAsync(
            _db,
            BenchmarkBatteryTestData.Definition(),
            (BenchmarkBatteryTestData.SuiteARun(1), 0, 1),
            (BenchmarkBatteryTestData.Run(2, BenchmarkBatteryTestData.SuiteB, new[] { 90, 50 }, new[] { 20, 60 },
                status: BenchmarkRunStatus.CompletedWithErrors, qualityIndex: null, terminalFailures: 1), 1, 1));

        var loaded = await Service().LoadAsync(id, TestContext.Current.CancellationToken);
        Assert.NotNull(loaded);
        Assert.Single(loaded!.Suites[0].UsableRuns);
        Assert.Empty(loaded.Suites[1].UsableRuns);
        var excludedOnLoad = Assert.Single(loaded.Excluded);
        Assert.Equal(2, excludedOnLoad.RunId);
        Assert.Equal(BenchmarkBatteryPlanner.IndexWithheldProviderFailureReason, excludedOnLoad.Reason);
        Assert.Equal(new long[] { 1 }, loaded.UsableMemberRunIds);

        var (analysis, result, _, error) = await Service().AnalyseAsync(id, null, null, TestContext.Current.CancellationToken);

        Assert.Null(error);
        Assert.NotNull(analysis);
        Assert.False(result!.Complete);
        Assert.Equal(1, result.CompletedSuiteCount);
        Assert.Null(result.OverallIndex);
        Assert.Null(result.Suites[1].Index);

        var excluded = Assert.Single(result.ExcludedMembers);
        Assert.Equal(1, excluded.SuiteIndex);
        Assert.Equal(1, excluded.Round);
        Assert.Equal(2, excluded.RunId);
        Assert.Equal(BenchmarkBatteryPlanner.IndexWithheldProviderFailureReason, excluded.Reason);

        Assert.False(analysis!.Complete);
        Assert.Null(analysis.ComparabilityClassSha256);
        Assert.Null(analysis.ComputedByUserId);
        Assert.Equal(new long[] { 1 }, JsonSerializer.Deserialize<long[]>(analysis.MemberRunIdsJson));
    }

    [Fact]
    public async Task Analyse_SupersededMembersAreIgnoredEntirely()
    {
        long id = await BenchmarkBatteryTestData.SeedAsync(
            _db,
            BenchmarkBatteryTestData.Definition(),
            (BenchmarkBatteryTestData.SuiteARun(1), 0, 1),
            (BenchmarkBatteryTestData.SuiteBRun(2), 1, 1));

        var battery = await _db.BenchmarkBatteryRuns.Include(b => b.Members).SingleAsync(TestContext.Current.CancellationToken);
        battery.Members.Single(m => m.BenchmarkRunId == 2).Superseded = true;
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var loaded = await Service().LoadAsync(id, TestContext.Current.CancellationToken);

        Assert.Empty(loaded!.Excluded);
        Assert.Empty(loaded.Suites[1].UsableRuns);
        Assert.Equal(new long[] { 1 }, loaded.UsableMemberRunIds);
    }

    [Fact]
    public async Task PersistedResult_RoundTrips_AndStalenessFollowsTheUsableMembers()
    {
        long id = await BenchmarkBatteryTestData.SeedAsync(
            _db,
            BenchmarkBatteryTestData.Definition(),
            (BenchmarkBatteryTestData.SuiteARun(1), 0, 1),
            (BenchmarkBatteryTestData.SuiteBRun(2), 1, 1));

        var service = Service();
        var (_, computed, _, _) = await service.AnalyseAsync(id, null, null, TestContext.Current.CancellationToken);

        var latest = await service.GetLatestAsync(id, TestContext.Current.CancellationToken);
        var restored = BenchmarkBatteryAnalysisService.DeserializeResult(latest);

        Assert.NotNull(restored);
        Assert.True(restored!.Complete);
        Assert.Equal(computed!.OverallIndex!.PointEstimate, restored.OverallIndex!.PointEstimate, 12);
        Assert.Equal(computed.Weights, restored.Weights);
        Assert.Equal(computed.Scheme, restored.Scheme);
        Assert.Equal(2, restored.Suites.Count);
        Assert.Equal(computed.SuiteMasses, restored.SuiteMasses);
        Assert.NotNull(restored.Suites[0].Statistics);
        Assert.Null(BenchmarkBatteryAnalysisService.DeserializeComparison(latest));

        var loaded = await service.LoadAsync(id, TestContext.Current.CancellationToken);
        Assert.False(BenchmarkBatteryAnalysisService.IsStale(loaded!, latest));
        Assert.True(BenchmarkBatteryAnalysisService.IsStale(new long[] { 1 }, latest));
        Assert.False(BenchmarkBatteryAnalysisService.IsStale(new long[] { 1, 2 }, null));
    }

    [Fact]
    public async Task Analyse_WithABaselineOfAnotherModel_StoresThePairedComparison()
    {
        var definition = BenchmarkBatteryTestData.Definition();
        long baselineId = await BenchmarkBatteryTestData.SeedAsync(
            _db,
            definition,
            (BenchmarkBatteryTestData.SuiteARun(1), 0, 1),
            (BenchmarkBatteryTestData.SuiteBRun(2), 1, 1));
        long treatmentId = await BenchmarkBatteryTestData.SeedAsync(
            _db,
            definition,
            (BenchmarkBatteryTestData.SuiteARun(3, modelId: "claude-opus-5", shift: 10), 0, 1),
            (BenchmarkBatteryTestData.SuiteBRun(4, modelId: "claude-opus-5", shift: 10), 1, 1));

        var service = Service();
        var eligibility = await service.CheckComparisonAsync(baselineId, treatmentId, TestContext.Current.CancellationToken);
        Assert.NotNull(eligibility);
        Assert.True(eligibility!.Allowed, eligibility.Explanation);
        Assert.Equal(BenchmarkBatteryComparisonKind.ModelComparison, eligibility.Kind);

        var (analysis, _, comparison, error) = await service.AnalyseAsync(treatmentId, null, baselineId, TestContext.Current.CancellationToken);

        Assert.Null(error);
        Assert.NotNull(comparison);
        Assert.Equal(10.0, comparison!.CompositeDifference!.Value, 9);
        Assert.Equal(5, comparison.PairedItemCount);
        Assert.Equal(baselineId, analysis!.ComparedWithBatteryRunId);

        var restored = BenchmarkBatteryAnalysisService.DeserializeComparison(analysis);
        Assert.NotNull(restored);
        Assert.Equal(comparison.CompositeDifference, restored!.CompositeDifference);

        // The baseline is read, never analysed: only the treatment's row is written.
        Assert.Single(await _db.BenchmarkBatteryAnalyses.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Analyse_WithABaselineOfAnotherDefinition_IsRefused_AndPersistsNothing()
    {
        long baselineId = await BenchmarkBatteryTestData.SeedAsync(
            _db,
            BenchmarkBatteryTestData.Definition(BenchmarkBatteryWeightingScheme.Equal),
            (BenchmarkBatteryTestData.SuiteARun(1), 0, 1),
            (BenchmarkBatteryTestData.SuiteBRun(2), 1, 1));
        long treatmentId = await BenchmarkBatteryTestData.SeedAsync(
            _db,
            BenchmarkBatteryTestData.Definition(),
            (BenchmarkBatteryTestData.SuiteARun(3, modelId: "claude-opus-5"), 0, 1),
            (BenchmarkBatteryTestData.SuiteBRun(4, modelId: "claude-opus-5"), 1, 1));

        var (analysis, _, comparison, error) = await Service().AnalyseAsync(treatmentId, null, baselineId, TestContext.Current.CancellationToken);

        Assert.Null(analysis);
        Assert.Null(comparison);
        Assert.NotNull(error);
        Assert.StartsWith("Comparison refused.", error);
        Assert.Empty(await _db.BenchmarkBatteryAnalyses.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Analyse_UnknownBatteryRun_ReturnsAnError()
    {
        var (analysis, _, _, error) = await Service().AnalyseAsync(404, null, null, TestContext.Current.CancellationToken);

        Assert.Null(analysis);
        Assert.Equal("Battery run not found.", error);
    }
}
