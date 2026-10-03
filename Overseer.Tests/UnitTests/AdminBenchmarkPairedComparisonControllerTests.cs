namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using MobileGnollHackLogger.Data;
using Overseer.Controllers;
using Overseer.Models;
using Overseer.Services.Benchmarking;
using Xunit;

/// <summary>
/// The paired-test endpoints: <c>POST model-comparison/paired</c> for the wizard and
/// <c>POST runs/{id}/paired-comparison</c> for the run report, with the run-pair kinds (model
/// comparison, verification of a change, replicate, not comparable) read from the comparability keys.
/// </summary>
public class AdminBenchmarkPairedComparisonControllerTests
{
    private const long SuiteId = 61;
    private const long OtherSuiteId = 62;

    private static readonly int[] Difficulties = { 50, 50, 50, 50, 50, 50 };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly ApplicationDbContext _db = BenchmarkBatteryTestData.NewDb();

    private static AdminBenchmarkController CreateController(ApplicationDbContext db)
        => new(
            db, null!, null!, new BenchmarkRunManager(), null!, null!, null!, null!, null!, null!,
            null!, null!, null!, null!, null!, null!, null!, null!, null!);

    private BenchmarkPairedTestsService Service(BenchmarkPairedComparisonCache? cache = null)
        => new(_db, new BenchmarkModelComparisonService(_db), cache);

    private static BenchmarkRun Run(
        long id,
        int[] scores,
        string modelId = "gpt-5.6-luna",
        long suiteId = SuiteId,
        BenchmarkRunStatus status = BenchmarkRunStatus.Completed,
        Action<BenchmarkRun>? adjust = null)
    {
        var run = BenchmarkBatteryTestData.Run(id, suiteId, scores, Difficulties.Take(scores.Length).ToArray(), modelId, status: status);
        adjust?.Invoke(run);
        return run;
    }

    private async Task SeedAsync(params BenchmarkRun[] runs)
    {
        _db.BenchmarkRuns.AddRange(runs);
        await _db.SaveChangesAsync(Ct);
    }

    private static readonly int[] Low = { 60, 70, 80, 65, 75, 85 };
    private static readonly int[] High = { 75, 82, 88, 80, 79, 95 };

    private static T Ok<T>(IActionResult result) => Assert.IsType<T>(Assert.IsType<OkObjectResult>(result).Value);

    private static string BadRequest(IActionResult result) => Assert.IsType<string>(Assert.IsType<BadRequestObjectResult>(result).Value);

    // --- POST model-comparison/paired ------------------------------------------------------------------

    [Fact]
    public async Task TheWizardEndpoint_TestsTwoRunsOncePerMeasure()
    {
        await SeedAsync(Run(1, Low), Run(2, High, "claude-opus-5"));

        var dto = Ok<BenchmarkPairedComparisonDto>(await CreateController(_db).PostPairedComparison(
            new BenchmarkPairedComparisonRequest { RunIds = { 1, 2 } }, Service(), Ct));

        Assert.Equal(new[] { "run:1", "run:2" }, dto.EntryKeys);
        Assert.Equal("run:2", dto.ReferenceKey);
        Assert.Equal(BenchmarkModelComparisonSubjectKinds.Runs, dto.SubjectKind);
        Assert.Equal("Current", dto.PricingBasis);
        Assert.Equal(BenchmarkPairedTests.SingleRunCaveat, dto.SingleRunCaveat);
        Assert.Equal(7, dto.Measures.Count);

        var intelligence = dto.Measures.Single(m => m.Measure == BenchmarkPairedTests.IntelligenceMeasure);
        var row = Assert.Single(intelligence.Pairs);
        Assert.Equal("run:2", row.BaselineKey);
        Assert.Equal("run:1", row.TreatmentKey);
        Assert.Equal(BenchmarkPairedTests.AdjustmentNone, intelligence.Adjustment);
        Assert.Equal(row.PValue, row.AdjustedPValue);
        Assert.Equal("Lower on the same questions", row.Verdict);
    }

    [Fact]
    public async Task TheWizardEndpoint_HonorsAReference_AndRefusesOneThatIsNotComparable()
    {
        await SeedAsync(Run(1, Low), Run(2, High, "claude-opus-5"));

        var dto = Ok<BenchmarkPairedComparisonDto>(await CreateController(_db).PostPairedComparison(
            new BenchmarkPairedComparisonRequest { RunIds = { 1, 2 }, ReferenceKey = "run:1" }, Service(), Ct));
        Assert.Equal("run:1", dto.Measures[0].Pairs[0].BaselineKey);

        Assert.Equal(BenchmarkPairedTests.ReferenceNotComparableError, BadRequest(await CreateController(_db).PostPairedComparison(
            new BenchmarkPairedComparisonRequest { RunIds = { 1, 2 }, ReferenceKey = "run:9" }, Service(), Ct)));
    }

    [Fact]
    public async Task FewerThanTwoComparableEntries_AreRefused()
    {
        // The second run was graded under another scoring method, so the comparison excludes it.
        await SeedAsync(Run(1, Low), Run(2, High, "claude-opus-5", adjust: r => r.ScoringMethodVersion = 8));

        Assert.Equal("Comparing needs two comparable entries.", BadRequest(await CreateController(_db).PostPairedComparison(
            new BenchmarkPairedComparisonRequest { RunIds = { 1, 2 } }, Service(), Ct)));

        Assert.Equal("Comparing needs two comparable entries.", BadRequest(await CreateController(_db).PostPairedComparison(
            new BenchmarkPairedComparisonRequest { RunIds = { 1 } }, Service(), Ct)));
    }

    [Fact]
    public async Task AMixedRequest_IsRefused()
    {
        Assert.Equal(BenchmarkBatteryModelComparison.MixedSourcesError, BadRequest(await CreateController(_db).PostPairedComparison(
            new BenchmarkPairedComparisonRequest { RunIds = { 1 }, GroupIds = { 2 }, BatteryRunIds = { 3 } }, Service(), Ct)));
    }

    [Fact]
    public async Task AllPairs_AboveTwelveComparableEntries_IsRefused_ButAReferenceIsNot()
    {
        var runs = Enumerable.Range(1, 13)
            .Select(i => Run(i, Low.Select(s => s + i).ToArray(), $"model-{i}"))
            .ToArray();
        await SeedAsync(runs);
        var ids = runs.Select(r => r.Id).ToList();

        Assert.Equal(BenchmarkPairedTests.AllPairsLimitError(13), BadRequest(await CreateController(_db).PostPairedComparison(
            new BenchmarkPairedComparisonRequest { RunIds = ids, Mode = BenchmarkPairedComparisonMode.AllPairs }, Service(), Ct)));

        var dto = Ok<BenchmarkPairedComparisonDto>(await CreateController(_db).PostPairedComparison(
            new BenchmarkPairedComparisonRequest { RunIds = ids, Mode = BenchmarkPairedComparisonMode.Reference }, Service(), Ct));
        Assert.Equal(12, dto.Measures[0].Pairs.Count);
        Assert.Equal("run:13", dto.ReferenceKey);
    }

    [Fact]
    public async Task Responses_AreCached_UntilARecomputeIsAsked()
    {
        await SeedAsync(Run(1, Low), Run(2, High, "claude-opus-5"));
        using var cache = new BenchmarkPairedComparisonCache();
        var service = Service(cache);
        var request = new BenchmarkPairedComparisonRequest { RunIds = { 1, 2 } };

        var first = (await service.CompareAsync(request, Ct)).Result;
        var second = (await service.CompareAsync(request, Ct)).Result;
        Assert.Same(first, second);

        request.Recompute = true;
        var third = (await service.CompareAsync(request, Ct)).Result;
        Assert.NotSame(first, third);

        // The reference is part of the key.
        var other = (await service.CompareAsync(new BenchmarkPairedComparisonRequest { RunIds = { 1, 2 }, ReferenceKey = "run:1" }, Ct)).Result;
        Assert.Equal("run:1", other!.ReferenceKey);
    }

    // --- POST runs/{id}/paired-comparison ----------------------------------------------------------------

    private Task<IActionResult> PostRunPairAsync(long id, long baselineRunId)
        => CreateController(_db).PostRunPairedComparison(
            id, new BenchmarkRunPairedComparisonRequest { BaselineRunId = baselineRunId }, Service(), Ct);

    [Fact]
    public async Task ARunPair_DifferingOnTheModel_IsAModelComparison()
    {
        await SeedAsync(Run(1, Low), Run(2, High, "claude-opus-5"));

        var dto = Ok<BenchmarkPairComparisonDto>(await PostRunPairAsync(2, 1));

        Assert.Equal("ModelComparison", dto.Kind);
        Assert.Equal("Model comparison", dto.KindLabel);
        Assert.Equal(new[] { BenchmarkComparabilityKey.CandidateModelKey }, dto.ChangedKeys);
        Assert.Equal("run:2", dto.TreatmentKey);
        Assert.Equal("run:1", dto.BaselineKey);
        Assert.Equal("Run", dto.SubjectKind);
        Assert.Equal(BenchmarkPairedTests.SingleRunCaveat, dto.SingleRunCaveat);

        var intelligence = dto.Measures.Single(m => m.Measure == BenchmarkPairedTests.IntelligenceMeasure);
        Assert.Equal(BenchmarkPairedTests.AdjustmentNone, intelligence.Adjustment);
        Assert.Equal("Higher on the same questions", Assert.Single(intelligence.Pairs).Verdict);
    }

    [Fact]
    public async Task ARunPair_DifferingOnOneInstrumentKey_IsAVerification_NamingTheKey()
    {
        await SeedAsync(Run(1, Low), Run(2, High, adjust: r => r.ToolGuidesSha256 = "b7c1d9e3f5a7b9c1d3e5f7a9b1c3d5e7"));

        var dto = Ok<BenchmarkPairComparisonDto>(await PostRunPairAsync(2, 1));

        Assert.Equal("Verification", dto.Kind);
        Assert.Equal("Verification of a change", dto.KindLabel);
        Assert.Equal(new[] { BenchmarkComparabilityKey.ToolGuidesKey }, dto.ChangedKeys);
        Assert.Contains(dto.Differences, d => d.Name == BenchmarkComparabilityKey.ToolGuidesKey);
    }

    [Fact]
    public async Task ARunPair_DifferingOnNothing_IsAReplicate()
    {
        await SeedAsync(Run(1, Low), Run(2, High));

        var dto = Ok<BenchmarkPairComparisonDto>(await PostRunPairAsync(2, 1));

        Assert.Equal("Replicate", dto.Kind);
        Assert.Empty(dto.ChangedKeys);
        Assert.False(dto.SpeedDegraded);
    }

    [Fact]
    public async Task ARunPair_DifferingOnlyOnParallelism_IsAReplicateWithSpeedAndCostUntested()
    {
        await SeedAsync(Run(1, Low), Run(2, High, adjust: r => r.MaxParallelQuestionsUsed = 4));

        var dto = Ok<BenchmarkPairComparisonDto>(await PostRunPairAsync(2, 1));

        Assert.Equal("Replicate", dto.Kind);
        Assert.Equal(new[] { BenchmarkComparabilityKey.QuestionParallelismKey }, dto.SpeedDegradingKeys);
        Assert.Contains("speed axis is degraded", dto.Measures.Single(m => m.Measure == BenchmarkPairedTests.SpeedMeasure).NotTestedReason);
        Assert.Contains("cost axis is degraded", dto.Measures.Single(m => m.Measure == BenchmarkPairedTests.CostMeasure).NotTestedReason);
    }

    [Fact]
    public async Task ARunPair_DifferingOnTwoInstrumentKeys_IsNotComparable()
    {
        await SeedAsync(Run(1, Low), Run(2, High, adjust: r =>
        {
            r.ToolGuidesSha256 = "b7c1d9e3f5a7b9c1d3e5f7a9b1c3d5e7";
            r.KnowledgeBaseHeadSha = "0a1b2c3d4e5f60718293a4b5c6d7e8f9";
        }));

        string error = BadRequest(await PostRunPairAsync(2, 1));

        Assert.StartsWith("Not comparable:", error);
        Assert.Contains("a verification allows exactly one", error);
    }

    [Fact]
    public async Task ARunPair_DifferingOnTheModelAndAnInstrumentKey_IsNotComparable()
    {
        await SeedAsync(Run(1, Low), Run(2, High, "claude-opus-5", adjust: r => r.HarnessVersion = "18"));

        string error = BadRequest(await PostRunPairAsync(2, 1));

        Assert.Contains("The model axis differs, but so do must-match keys", error);
    }

    [Fact]
    public async Task ARunPair_OnDifferentSuites_IsNotComparable()
    {
        await SeedAsync(Run(1, Low), Run(2, High, "claude-opus-5", suiteId: OtherSuiteId));

        string error = BadRequest(await PostRunPairAsync(2, 1));

        Assert.StartsWith("Not comparable:", error);
        Assert.Contains("the same suite", error);
    }

    [Fact]
    public async Task ARunPair_WithARunStillInProgress_IsRefused()
    {
        await SeedAsync(Run(1, Low, status: BenchmarkRunStatus.Running), Run(2, High, "claude-opus-5"));

        Assert.Contains("still in progress", BadRequest(await PostRunPairAsync(2, 1)));
    }

    [Fact]
    public async Task ARunPair_WithAMissingBaseline_IsNotFound_AndWithItself_IsRefused()
    {
        await SeedAsync(Run(1, Low));

        Assert.IsType<NotFoundObjectResult>(await PostRunPairAsync(1, 99));
        Assert.Contains("compared with itself", BadRequest(await PostRunPairAsync(1, 1)));
    }

    // --- POST runs/{id}/paired-comparison/kinds -----------------------------------------------------------

    [Fact]
    public async Task TheKindsEndpoint_ClassifiesEveryCandidateBaseline_InRequestOrder()
    {
        await SeedAsync(
            Run(1, Low),
            Run(2, High, "claude-opus-5"),
            Run(3, High, adjust: r => r.ToolGuidesSha256 = "b7c1d9e3f5a7b9c1d3e5f7a9b1c3d5e7"),
            Run(4, High),
            Run(5, High, suiteId: OtherSuiteId));

        var kinds = Ok<List<BenchmarkRunPairKindDto>>(await CreateController(_db).PostRunPairedComparisonKinds(
            1, new BenchmarkRunPairKindsRequest { RunIds = { 2, 3, 4, 5, 1, 77 } }, Service(), Ct));

        Assert.Equal(new long[] { 2, 3, 4, 5, 77 }, kinds.Select(k => k.RunId));
        Assert.Equal(new[] { "ModelComparison", "Verification", "Replicate", "NotComparable", "NotComparable" },
            kinds.Select(k => k.Kind));
        Assert.Equal(new[] { BenchmarkComparabilityKey.ToolGuidesKey }, kinds[1].ChangedKeys);
    }
}
