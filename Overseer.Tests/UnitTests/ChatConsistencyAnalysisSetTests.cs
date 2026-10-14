namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MobileGnollHackLogger.Data;
using Overseer.Services.Benchmarking;
using Overseer.Services.ChatConsistency;
using Xunit;
using static Overseer.Tests.UnitTests.ChatConsistencyAnalysisTestData;

/// <summary>
/// Battery and suite comparison sets of the chat consistency analysis over an in-memory database: in the
/// analysis, in the evidence the builder loads for them, and in the timeline's battery and run points.
/// No network.
/// </summary>
public class ChatConsistencyAnalysisSetTests
{
    // --- Battery and suite sets ------------------------------------------------------------------

    [Fact]
    public async Task ABatterySetAnalyzesItsBatteryRunsAsUnits()
    {
        using var db = NewDb();
        string key = SeedBatteries(db);

        var result = await Service(db).AnalyzeAsync(Request(key) with { ComparisonSet = BatterySet() }, TestContext.Current.CancellationToken);

        Assert.Equal(8, ChatConsistencyAnalysisService.CurrentAnalysisCodeVersion);
        Assert.Equal(ChatConsistencyAnalysisService.CurrentAnalysisCodeVersion, result.AnalysisCodeVersion);
        Assert.Equal(ChatConsistencyComparisonSetKinds.BatteryRunUnit, result.UnitKind);
        Assert.Equal(BatteryKey(TwoSuites()), result.ComparisonSet!.Key);
        Assert.Equal(BatteryName + " (revision 1)", result.ComparisonSet.Label);
        Assert.Equal(
            new[]
            {
                (101L, "baseline", "1,21"),
                (102L, "baseline", "2,22"),
                (103L, "comparison", "3,23"),
                (104L, "comparison", "4,24")
            },
            result.Units.Select(u => (u.UnitId, u.Period, string.Join(",", u.MemberRunIds))).ToList());
        Assert.All(result.Units, u => Assert.Equal(ChatConsistencyComparisonSetKinds.BatteryRunUnit, u.Kind));
        Assert.Equal(BaselineDay1.AddMinutes(-1), result.Units[0].StartedAtUtc);

        var p1 = EndpointOf(result, ChatConsistencyEndpointIds.Quality);
        Assert.True(p1.MinimumSampleMet, p1.MinimumSampleDetail);

        var row = Assert.Single(db.ChatConsistencyAnalyses.ToList());
        Assert.Equal("[1,2,3,4,21,22,23,24]", row.TargetRunIdsJson);
        Assert.Equal(ChatConsistencyAnalysisService.CurrentAnalysisCodeVersion, row.AnalysisCodeVersion);

        var summary = Assert.Single(await Service(db).ListAnalysesAsync(TestContext.Current.CancellationToken));
        Assert.Equal(BatteryKey(TwoSuites()), summary.ComparisonSetKey);
        Assert.Equal(BatteryName + " (revision 1)", summary.ComparisonSetLabel);
    }

    [Fact]
    public async Task OneBatteryRunPerPeriodFallsShortOfTheMinimumSampleItsMemberRunsWouldMeet()
    {
        using var db = NewDb();
        string key = SeedBatteries(db);
        var service = Service(db);

        var battery = await service.AnalyzeAsync(
            Request(key) with { ComparisonSet = BatterySet(), BaselineBatteryRunIds = new long[] { 101 }, ComparisonBatteryRunIds = new long[] { 103 } },
            TestContext.Current.CancellationToken);
        var p1 = EndpointOf(battery, ChatConsistencyEndpointIds.Quality);
        Assert.False(p1.MinimumSampleMet);
        Assert.Contains("battery run", p1.MinimumSampleDetail);
        Assert.Equal(new long[] { 101, 103 }, battery.Units.Select(u => u.UnitId));

        // Run by run, the same members are two runs on two days per period.
        var runs = await service.AnalyzeAsync(
            Request(key) with { BaselineRunIds = new long[] { 1, 21 }, ComparisonRunIds = new long[] { 3, 23 } },
            TestContext.Current.CancellationToken);
        Assert.True(EndpointOf(runs, ChatConsistencyEndpointIds.Quality).MinimumSampleMet);
        Assert.Equal(ChatConsistencyComparisonSetKinds.RunUnit, runs.UnitKind);
        Assert.Null(runs.ComparisonSet);
    }

    [Fact]
    public async Task RunIdsWithABatterySetAreRefused()
    {
        using var db = NewDb();
        string key = SeedBatteries(db);
        var request = Request(key) with { ComparisonSet = BatterySet(), BaselineRunIds = new long[] { 1, 21 }, ComparisonRunIds = new long[] { 3, 23 } };

        var refusal = await Assert.ThrowsAsync<ChatConsistencyRequestException>(
            () => Service(db).AnalyzeAsync(request, TestContext.Current.CancellationToken));

        Assert.Equal("A battery comparison takes battery run ids.", refusal.Message);
        Assert.Empty(db.ChatConsistencyAnalyses.ToList());
    }

    [Fact]
    public async Task ABatteryRunWithAMemberTheSegmentRuleDropsIsDroppedWhole()
    {
        using var db = NewDb();
        string key = SeedBatteries(db, firstRunAtOtherScoring: true);

        var result = await Service(db).AnalyzeAsync(Request(key) with { ComparisonSet = BatterySet() }, TestContext.Current.CancellationToken);

        // Run #1 alone was scored by another method; its partner #21 goes with it, leaving one baseline battery run for quality.
        var p1 = EndpointOf(result, ChatConsistencyEndpointIds.Quality);
        Assert.False(p1.MinimumSampleMet, p1.MinimumSampleDetail);
        Assert.Contains(result.DataQuality, n => n.Kind == "segment" && n.Text.Contains("#101", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WithoutASetEveryEndpointEqualsASuiteSetOverTheSameSingleSuiteRuns()
    {
        using var db = NewDb();
        string key = Seed(db, new Scenario { QualityDrop = true });
        var service = Service(db);

        var plain = await service.AnalyzeAsync(Request(key), TestContext.Current.CancellationToken);
        var suite = await service.AnalyzeAsync(Request(key) with { ComparisonSet = CoreSuiteSet() }, TestContext.Current.CancellationToken);

        static string Json(object value) => JsonSerializer.Serialize(value, ChatConsistencyJson.Options);
        Assert.Equal(Json(plain.Endpoints), Json(suite.Endpoints));
        Assert.Equal(Json(plain.Baseline), Json(suite.Baseline));
        Assert.Equal(Json(plain.Comparison), Json(suite.Comparison));
        Assert.Equal(Json(plain.Controls), Json(suite.Controls));
        Assert.Equal(Json(plain.Reliability), Json(suite.Reliability));
        Assert.Equal(Json(plain.SecondaryFamilies), Json(suite.SecondaryFamilies));
        Assert.Equal(plain.Headline, suite.Headline);

        Assert.Null(plain.ComparisonSet);
        Assert.Equal(ChatConsistencyComparisonSetKinds.RunUnit, plain.UnitKind);
        Assert.Equal(ChatConsistencyComparisonSetKinds.RunUnit, suite.UnitKind);
        Assert.Equal("suite:id:7", suite.ComparisonSet!.Key);
        Assert.Equal("Core Suite", suite.ComparisonSet.Label);
        Assert.Equal(new long[] { 1, 2, 3, 4 }, suite.Units.Select(u => u.UnitId));
    }

    // --- Battery and suite sets: the evidence ----------------------------------------------------

    [Fact]
    public async Task TheEvidenceOfABatterySetMapsEveryMemberToItsBatteryRun()
    {
        using var db = NewDb();
        string key = SeedBatteries(db);

        var evidence = await Evidence(db, Request(key) with { ComparisonSet = BatterySet() });

        Assert.Equal(new long[] { 1, 21, 2, 22 }, evidence.BaselineRuns.Select(r => r.Id));
        Assert.Equal(new long[] { 3, 23, 4, 24 }, evidence.ComparisonRuns.Select(r => r.Id));
        Assert.Equal(ChatConsistencyComparisonSetKinds.BatteryRunUnit, evidence.UnitKind);
        Assert.Equal(
            new Dictionary<long, long> { [1] = 101, [21] = 101, [2] = 102, [22] = 102, [3] = 103, [23] = 103, [4] = 104, [24] = 104 },
            evidence.UnitOf.OrderBy(p => p.Key).ToDictionary(p => p.Key, p => p.Value));
        Assert.Equal(ComparisonDay2.AddMinutes(-1), evidence.UnitStartedAtUtc[104]);
        Assert.Equal(4, evidence.UnitStartedAtUtc.Count);
        Assert.Equal(102L, evidence.UnitIdOf(22));
        Assert.Equal(new ChatConsistencyComparedSet { Kind = "battery", Key = BatteryKey(TwoSuites()), Label = BatteryName + " (revision 1)" }, evidence.ComparisonSet);
        Assert.Empty(evidence.Notes);
        Assert.Empty(evidence.UnanalyzedRuns);
    }

    [Fact]
    public async Task IncompleteForeignMissingAndSharingBatteryRunsAreLeftOutWithNotes()
    {
        using var db = NewDb();
        string key = SeedBatteries(db, extras: true);
        var request = Request(key) with
        {
            ComparisonSet = BatterySet(),
            BaselineBatteryRunIds = new long[] { 107, 106, 105, 102, 101, 999 },
            ComparisonBatteryRunIds = new long[] { 103, 104 },
            RunSelection = new ChatConsistencyRunSelection { LeftOutBatteryRunIds = new long[] { 105 } }
        };

        var evidence = await Evidence(db, request);

        Assert.Equal(
            new[]
            {
                "Battery run #105 is incomplete (1 of 2 suites usable) and was left out.",
                "Battery run #106 belongs to another battery definition and was left out.",
                "Battery run #999 was not found.",
                "Battery run #107 shares run #2 with battery run #102 and was left out."
            },
            evidence.Notes.Where(n => n.Kind == "excludedBatteryRun").Select(n => n.Text).ToList());
        Assert.Equal(new long[] { 1, 21, 2, 22 }, evidence.BaselineRuns.Select(r => r.Id));
        Assert.Equal(new long[] { 101, 102, 103, 104 }, evidence.UnitStartedAtUtc.Keys.OrderBy(i => i));

        // The set's other battery runs list their usable members; every other run of the model is outside the set.
        Assert.Equal(
            new[]
            {
                (27L, "baseline", ChatConsistencyUnanalyzedReasons.NotSelected, (long?)107),
                (5L, "baseline", ChatConsistencyUnanalyzedReasons.LeftOut, (long?)105),
                (6L, "baseline", ChatConsistencyUnanalyzedReasons.OutsideComparisonSet, (long?)null),
                (31L, "baseline", ChatConsistencyUnanalyzedReasons.OutsideComparisonSet, (long?)null),
                (26L, "baseline", ChatConsistencyUnanalyzedReasons.OutsideComparisonSet, (long?)null)
            },
            evidence.UnanalyzedRuns.Select(u => (u.RunId, u.Period, u.Reason, u.BatteryRunId)).ToList());
    }

    [Fact]
    public async Task ASuiteSetTakesOnlyItsSuitesRunsAndListsTheOthersAsOutsideTheSet()
    {
        using var db = NewDb();
        string key = SeedBatteries(db);

        var automatic = await Evidence(db, Request(key) with { ComparisonSet = CoreSuiteSet() });
        Assert.Equal(new long[] { 1, 2 }, automatic.BaselineRuns.Select(r => r.Id));
        Assert.Equal(new long[] { 3, 4 }, automatic.ComparisonRuns.Select(r => r.Id));
        Assert.Equal("Core Suite", automatic.ComparisonSet!.Label);
        Assert.Equal(ChatConsistencyComparisonSetKinds.RunUnit, automatic.UnitKind);
        Assert.Equal(3L, automatic.UnitIdOf(3));

        var given = await Evidence(db, Request(key) with
        {
            ComparisonSet = CoreSuiteSet(),
            BaselineRunIds = new long[] { 1, 2, 21 },
            ComparisonRunIds = new long[] { 3, 4 }
        });
        Assert.Equal(new long[] { 1, 2 }, given.BaselineRuns.Select(r => r.Id));
        var note = Assert.Single(given.Notes);
        Assert.Equal("excludedRun", note.Kind);
        Assert.Equal("Run #21 answered another suite and was left out.", note.Text);
        Assert.Equal(new long[] { 21, 22, 23, 24 }, given.UnanalyzedRuns.Select(u => u.RunId));
        Assert.All(given.UnanalyzedRuns, u =>
        {
            Assert.Equal(ChatConsistencyUnanalyzedReasons.OutsideComparisonSet, u.Reason);
            Assert.Null(u.BatteryRunId);
        });
    }

    [Fact]
    public async Task WithoutASetRunsOfSeveralSuitesAreAnalyzedWithAMixedSuitesNote()
    {
        using var db = NewDb();
        string key = SeedBatteries(db);

        var evidence = await Evidence(db, Request(key));

        Assert.Equal(new long[] { 1, 21, 2, 22 }, evidence.BaselineRuns.Select(r => r.Id));
        Assert.Null(evidence.ComparisonSet);
        Assert.Equal(ChatConsistencyComparisonSetKinds.RunUnit, evidence.UnitKind);
        Assert.All(evidence.UnitOf, p => Assert.Equal(p.Key, p.Value));
        Assert.Equal(8, evidence.UnitOf.Count);
        var note = Assert.Single(evidence.Notes);
        Assert.Equal("mixedSuites", note.Kind);
        Assert.Equal(
            "The analyzed runs answered 2 suites (Core Suite, Second Suite); their items pair by question and revision across them. "
            + "Choose a battery or a suite to compare within.",
            note.Text);
    }

    [Fact]
    public async Task AMalformedComparisonSetOrMisplacedBatteryRunIdsAreRefused()
    {
        using var db = NewDb();
        string key = SeedBatteries(db);

        async Task Refused(ChatConsistencyAnalysisRequest request, string message)
        {
            var refusal = await Assert.ThrowsAsync<ChatConsistencyRequestException>(() => Evidence(db, request));
            Assert.Equal(message, refusal.Message);
        }

        await Refused(
            Request(key) with { ComparisonSet = new ChatConsistencyComparisonSetRef { Kind = "battery", Key = "suite:id:7" } },
            "A battery comparison set's key is battery: followed by the battery definition hash.");
        await Refused(
            Request(key) with { ComparisonSet = new ChatConsistencyComparisonSetRef { Kind = "suite", Key = "suite:" } },
            "A suite comparison set's key is suite: followed by the suite identity.");
        await Refused(
            Request(key) with { ComparisonSet = new ChatConsistencyComparisonSetRef { Kind = "group", Key = "group:1" } },
            "A comparison set is a battery or a suite.");
        await Refused(
            Request(key) with { ComparisonSet = BatterySet(), BaselineRunIds = new long[] { 1 } },
            "A battery comparison takes battery run ids.");
        await Refused(
            Request(key) with { ComparisonSet = CoreSuiteSet(), BaselineBatteryRunIds = new long[] { 101 } },
            "Battery run ids need a battery comparison set.");
    }

    // --- Battery and suite sets: the timeline ----------------------------------------------------

    [Fact]
    public async Task ATimelineBatteryPointPoolsTheAnswersOfItsMembers()
    {
        using var db = NewDb();
        string key = SeedBatteries(db);

        // Run #21 reaches its first answer text 5 s later from its seventh question on, and its last answer was never delivered.
        var second = db.BenchmarkRuns.Local.Single(r => r.Id == 21);
        foreach (var answer in second.Answers.Where(a => a.OrderIndex >= 6))
        {
            foreach (var call in answer.ModelCalls) call.FirstOutputMs += 5000;
        }

        second.Answers.Single(a => a.OrderIndex == QuestionCount - 1).Status = BenchmarkAnswerStatus.ProviderError;
        db.SaveChanges();

        var timeline = await Timeline(db, key);

        Assert.Equal(new long[] { 101, 102, 103, 104 }, timeline.BatteryPoints.Select(p => p.RunId));
        var point = timeline.BatteryPoints[0];
        Assert.Equal(new long[] { 1, 21 }, point.MemberRunIds);
        Assert.Equal(BaselineDay1.AddMinutes(-1), point.StartedAtUtc);
        Assert.Equal(BaselineDay1.AddMinutes(-1).AddDays(1), point.CompletedAtUtc);
        Assert.Equal(BatteryKey(TwoSuites()), point.SetKey);
        Assert.Equal(BatteryName, point.BatteryName);
        Assert.Equal(BatteryName + " (revision 1)", point.SuiteName);
        Assert.Equal(1, point.DefinitionRevision);
        Assert.Equal(2, point.SuiteCount);
        Assert.True(point.Complete);
        Assert.Null(point.IncompleteReason);
        Assert.Equal(BenchmarkRunSeriesStatus.Completed, point.BatteryStatus);
        Assert.Equal(BenchmarkRunStatus.Completed, point.Status);
        Assert.Null(point.SuiteId);
        Assert.Null(point.QualityIndex);
        Assert.False(point.IsLegacy);
        Assert.Equal("telemetry", point.LatencyLabel);

        // Every answer counts; the measures of speed and work read the 47 delivered ones together.
        Assert.Equal(2 * QuestionCount, point.AnswerCount);
        Assert.Equal(1.0 / (2 * QuestionCount), point.TerminalFailureRate!.Value, 9);
        var members = new[] { db.BenchmarkRuns.Local.Single(r => r.Id == 1), second };
        var delivered = members.SelectMany(r => r.Answers).Where(a => a.Status == BenchmarkAnswerStatus.Ok).ToList();
        Assert.Equal(2 * QuestionCount - 1, delivered.Count);
        Assert.Equal(delivered.Average(a => (double)a.OutputTokens!.Value), point.OutputTokensPerAnswer!.Value, 9);

        // The pooled median is the 24th of the 47 answers, not the mean of the members' medians.
        var runOne = timeline.Points.Single(p => p.RunId == 1);
        var runTwentyOne = timeline.Points.Single(p => p.RunId == 21);
        Assert.Equal(872.5, runOne.MedianTimeToFirstAnswerTextMs);
        Assert.Equal(5870.0, runTwentyOne.MedianTimeToFirstAnswerTextMs);
        Assert.Equal(900.0, point.MedianTimeToFirstAnswerTextMs);
        Assert.NotEqual((872.5 + 5870.0) / 2, point.MedianTimeToFirstAnswerTextMs!.Value);
        Assert.Equal(runOne.AnswerCount + runTwentyOne.AnswerCount, point.AnswerCount);

        var json = JsonNode.Parse(JsonSerializer.Serialize(timeline, ChatConsistencyJson.Options))!;
        var node = json["batteryPoints"]![0]!;
        Assert.Equal(101L, node["runId"]!.GetValue<long>());
        Assert.Equal("completed", node["batteryStatus"]!.GetValue<string>());
        Assert.Equal("completed", node["status"]!.GetValue<string>());
        Assert.Equal(BatteryKey(TwoSuites()), node["setKey"]!.GetValue<string>());
        Assert.Equal(900.0, node["medianTimeToFirstAnswerTextMs"]!.GetValue<double>());
        Assert.Equal("[1,21]", node["memberRunIds"]!.ToJsonString());

        // With the dates ending before run #21 started, the battery point still pools both members.
        var early = await Timeline(db, key, null, BaselineDay1.AddHours(1));
        Assert.Equal(new long[] { 1 }, early.Points.Select(p => p.RunId));
        var earlyPoint = Assert.Single(early.BatteryPoints);
        Assert.Equal(101L, earlyPoint.RunId);
        Assert.Equal(new long[] { 1, 21 }, earlyPoint.MemberRunIds);
        Assert.Equal(2 * QuestionCount, earlyPoint.AnswerCount);
        Assert.Equal(900.0, earlyPoint.MedianTimeToFirstAnswerTextMs);
    }

    [Fact]
    public async Task ATimelineBatteryPointTakesTheOverallIndexOfItsLatestCurrentAnalysis()
    {
        using var db = NewDb();
        string key = SeedBatteries(db);
        db.BenchmarkBatteryAnalyses.AddRange(
            BatteryAnalysis(1, 101, ComparisonDay2, 70.0, 1, 21),
            BatteryAnalysis(2, 101, ComparisonDay2.AddHours(1), 83.5, 21, 1),
            BatteryAnalysis(3, 103, ComparisonDay2, 80.0, 3, 99));
        db.SaveChanges();

        var timeline = await Timeline(db, key);

        Assert.Equal(new long[] { 101, 102, 103, 104 }, timeline.BatteryPoints.Select(p => p.RunId));
        var current = timeline.BatteryPoints[0];
        Assert.Equal(83.5, current.OverallIndex);
        Assert.Null(current.OverallIndexNote);

        var none = timeline.BatteryPoints[1];
        Assert.Null(none.OverallIndex);
        Assert.Equal("No battery analysis. Compute it from the battery report.", none.OverallIndexNote);

        var stale = timeline.BatteryPoints[2];
        Assert.Null(stale.OverallIndex);
        Assert.Equal("The battery analysis was computed over other member runs. Recompute it from the battery report.", stale.OverallIndexNote);
    }

    [Fact]
    public async Task ATimelineHasABatteryPointForEveryBatteryRunOfTheModelAndAnIncompleteOneHasNoOverallIndex()
    {
        using var db = NewDb();
        string key = SeedBatteries(db, extras: true);

        var timeline = await Timeline(db, key);

        Assert.Equal(new long[] { 101, 102, 107, 105, 106, 103, 104 }, timeline.BatteryPoints.Select(p => p.RunId));

        var incomplete = timeline.BatteryPoints.Single(p => p.RunId == 105);
        Assert.False(incomplete.Complete);
        Assert.Equal("1 of 2 suites usable", incomplete.IncompleteReason);
        Assert.Equal(new long[] { 5 }, incomplete.MemberRunIds);
        Assert.Equal(QuestionCount, incomplete.AnswerCount);
        Assert.Null(incomplete.OverallIndex);
        Assert.Equal("The battery run is incomplete (1 of 2 suites usable), so it has no Overall Index.", incomplete.OverallIndexNote);

        // Another definition has its own set key; a battery run sharing a run with another lists it too.
        var other = timeline.BatteryPoints.Single(p => p.RunId == 106);
        Assert.Equal(BatteryKey(TwoSuites() with { Scheme = BenchmarkBatteryWeightingScheme.ItemCount }), other.SetKey);
        Assert.NotEqual(BatteryKey(TwoSuites()), other.SetKey);
        Assert.True(other.Complete);
        Assert.Equal(new long[] { 6, 26 }, other.MemberRunIds);
        Assert.Equal(new long[] { 2, 27 }, timeline.BatteryPoints.Single(p => p.RunId == 107).MemberRunIds);
    }

    [Fact]
    public async Task ATimelineBatteryPointWeighsCommonGraderQualityByItemsAndOmitsASnapshotAMemberLacks()
    {
        using var db = NewDb();
        string key = SeedBatteries(db);
        db.BenchmarkAssessorCalibrations.AddRange(
            Calibration(1, 1, 900, ComparisonDay2, 80, 80, 80),
            Calibration(2, 21, 900, ComparisonDay2.AddHours(1), 40),
            Calibration(3, 1, 901, ComparisonDay2, 90));
        db.SaveChanges();

        var timeline = await Timeline(db, key);

        // (3 · 80 + 1 · 40) / 4, not the mean of the members' means (60); snapshot 901 graded run #1 alone.
        var grader = Assert.Single(timeline.BatteryPoints.Single(p => p.RunId == 101).CommonGraderQuality);
        Assert.Equal(900L, grader.SnapshotId);
        Assert.Equal(70.0, grader.MeanQuality, 9);
        Assert.Equal(4, grader.ItemCount);
        Assert.Equal(2L, grader.CalibrationId);
        Assert.Equal(ComparisonDay2.AddHours(1), grader.CalibratedAtUtc);
        Assert.Empty(timeline.BatteryPoints.Single(p => p.RunId == 102).CommonGraderQuality);

        // The run points keep each run's own figures.
        var runOne = timeline.Points.Single(p => p.RunId == 1);
        Assert.Equal(new long[] { 900, 901 }, runOne.CommonGraderQuality.Select(g => g.SnapshotId));
        Assert.Equal(80.0, runOne.CommonGraderQuality[0].MeanQuality, 9);
        Assert.Equal(3, runOne.CommonGraderQuality[0].ItemCount);
    }

    [Fact]
    public async Task ATimelineRunPointMeasuresOneRunAndAModelWithoutBatteryRunsHasNoBatteryPoints()
    {
        using var db = NewDb();
        string key = Seed(db, new Scenario());

        var timeline = await Timeline(db, key);

        Assert.Equal(new long[] { 1, 2, 3, 4 }, timeline.Points.Select(p => p.RunId));
        Assert.Empty(timeline.BatteryPoints);
        var point = timeline.Points[0];
        Assert.Equal(BaselineDay1, point.StartedAtUtc);
        Assert.Equal("Core Suite", point.SuiteName);
        Assert.Equal(7L, point.SuiteId);
        Assert.Equal(BenchmarkRunStatus.Completed, point.Status);
        Assert.Equal(HarnessImpactLedger.CurrentVersion, point.HarnessVersion);
        Assert.False(point.IsLegacy);
        Assert.Equal("telemetry", point.LatencyLabel);
        Assert.Equal(QuestionCount, point.AnswerCount);
        Assert.Equal(872.5, point.MedianTimeToFirstAnswerTextMs);
        Assert.Equal(525.0, point.OutputTokensPerAnswer!.Value, 9);
        Assert.Equal(2.0, point.ToolCallsPerAnswer);
        Assert.Equal(0.0, point.RefusalRate);
        var served = Assert.Single(point.ServedModelIds);
        Assert.Equal("gpt-test-2026-09-01", served.ModelId);
        Assert.Equal(QuestionCount, served.CallCount);

        using var legacyDb = NewDb();
        string legacyKey = Seed(legacyDb, new Scenario { Legacy = true });
        var legacy = (await Timeline(legacyDb, legacyKey)).Points[0];
        Assert.True(legacy.IsLegacy);
        Assert.Equal("legacy proxy", legacy.LatencyLabel);
        Assert.Null(legacy.RefusalRate);
        Assert.Null(legacy.MedianTimeToFirstAnswerTextMs);
    }
}
