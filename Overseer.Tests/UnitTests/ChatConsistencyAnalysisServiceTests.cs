namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using MobileGnollHackLogger.Data;
using Overseer.Services.ChatConsistency;
using Xunit;
using static Overseer.Tests.UnitTests.ChatConsistencyAnalysisTestData;

/// <summary>
/// The chat consistency analysis over an in-memory database: one subject in two periods with call
/// telemetry and a control subject. Covers persistence, determinism, the headline, a quality drop, an
/// Overseer event, freshness, and the next runs after a replaced build.
/// No network.
/// </summary>
public class ChatConsistencyAnalysisServiceTests
{
    // The quality-drop analysis at the protocol's bootstrap replicates, shared by the tests that only read it.
    private static readonly Lazy<Task<ChatConsistencyAnalysisResult>> QualityDropAnalysis =
        new(() => AnalyzeOnceAsync(new Scenario { QualityDrop = true }, null));

    // The analysis whose comparison runs start 12 hours later than the baseline's, shared by the tests that only read it.
    private static readonly Lazy<Task<ChatConsistencyAnalysisResult>> NoCommonStratumAnalysis =
        new(() => AnalyzeOnceAsync(new Scenario { ComparisonHourOffset = 12 }, TestBootstrapReplicates));

    // --- Persistence and determinism -------------------------------------------------------------

    [Fact]
    public async Task AnalyzePersistsOneRowWithTheResultAndItsFingerprint()
    {
        using var db = NewDb();
        string key = Seed(db, new Scenario { QualityDrop = true });

        var result = await Service(db).AnalyzeAsync(Request(key), TestContext.Current.CancellationToken);

        var row = Assert.Single(db.ChatConsistencyAnalyses.ToList());
        Assert.Equal(result.AnalysisId, row.Id);
        Assert.Equal(ChatConsistencyProtocol.V1Version, row.ProtocolVersion);
        Assert.Equal(ChatConsistencyAnalysisService.CurrentAnalysisCodeVersion, row.AnalysisCodeVersion);
        Assert.Equal(result.InputSha256, row.InputSha256);
        Assert.Matches("^[0-9a-f]{64}$", row.InputSha256);
        Assert.Equal("[1,2,3,4]", row.TargetRunIdsJson);
        Assert.Equal("[11,12,13,14]", row.ControlRunIdsJson);
        Assert.Equal(ChatConsistencyProtocol.V1.ToJson(), row.ProtocolJson);

        var reloaded = await Service(db).GetAnalysisAsync(row.Id, TestContext.Current.CancellationToken);
        Assert.NotNull(reloaded);
        Assert.Equal(result.Headline, reloaded!.Headline);
        Assert.Equal(row.Id, reloaded.AnalysisId);
    }

    [Fact]
    public async Task ASummaryCarriesTheSubjectAndTheEndpointVerdictsOfItsResult()
    {
        using var db = NewDb();
        string key = Seed(db, new Scenario { QualityDrop = true });
        var service = Service(db);
        var result = await service.AnalyzeAsync(Request(key), TestContext.Current.CancellationToken);

        var summary = Assert.Single(await service.ListAnalysesAsync(TestContext.Current.CancellationToken));

        Assert.Equal(ChatConsistencyAnalysisService.CurrentAnalysisCodeVersion, summary.AnalysisCodeVersion);
        Assert.NotNull(summary.Subject);
        Assert.Equal(result.Subject.DisplayName, summary.Subject!.DisplayName);
        Assert.Equal("OpenAI", summary.Subject.Provider);
        Assert.Equal("gpt-test", summary.Subject.ModelId);
        Assert.Equal(result.Subject.ThinkingLevel, summary.Subject.ThinkingLevel);
        Assert.Equal(result.Subject.ServiceTier, summary.Subject.ServiceTier);
        Assert.Equal(
            result.Endpoints.Select(e => (e.Id, e.Name, e.Computed, e.VerdictLabel, e.Grade)).ToList(),
            summary.Endpoints.Select(e => (e.Id, e.Name, e.Computed, e.VerdictLabel, e.Grade)).ToList());

        var p1 = Assert.Single(summary.Endpoints, e => e.Id == ChatConsistencyEndpointIds.Quality);
        Assert.Equal("degraded", p1.VerdictLabel);
        Assert.Equal(ChatConsistencyEvidenceGrade.Indicated, p1.Grade);
    }

    [Fact]
    public async Task ASummaryOfAStoredResultWithoutSubjectOrEndpointsHasNone()
    {
        using var db = NewDb();
        var result = ChatConsistencyReportTestData.Result();
        StoreResult(db, result, "subject", "endpoints");

        var summary = Assert.Single(await Service(db).ListAnalysesAsync(TestContext.Current.CancellationToken));

        Assert.Null(summary.Subject);
        Assert.Empty(summary.Endpoints);
        Assert.Equal(result.Headline, summary.Headline);
    }

    [Fact]
    public async Task TwoAnalysesOfTheSameInputsStoreTheSameResultAndFingerprint()
    {
        using var db = NewDb();
        string key = Seed(db, new Scenario { QualityDrop = true });
        var service = ProductionService(db);

        var first = await service.AnalyzeAsync(Request(key), TestContext.Current.CancellationToken);
        var second = await service.AnalyzeAsync(Request(key), TestContext.Current.CancellationToken);

        var rows = db.ChatConsistencyAnalyses.OrderBy(a => a.Id).ToList();
        Assert.Equal(2, rows.Count);
        Assert.NotEqual(first.AnalysisId, second.AnalysisId);
        Assert.Equal(rows[0].InputSha256, rows[1].InputSha256);
        Assert.Equal(rows[0].ResultJson, rows[1].ResultJson);
    }

    // --- The verdict on the chat -----------------------------------------------------------------

    [Fact]
    public async Task TheHeadlineLeadsWithTheVerdictOnTheChat()
    {
        var result = await QualityDropAnalysis.Value;

        Assert.StartsWith("Overseer chat with gpt-test: quality degraded", result.Headline);
        Assert.Contains("; speed ", result.Headline);
        Assert.Contains("; work ", result.Headline);
        Assert.Contains("; cost ", result.Headline);
        Assert.Contains(" within weekdays 08–12 UTC", result.Headline);
        Assert.True(result.Scope.OneTimeStratum);
    }

    [Fact]
    public async Task WithoutACommonTimeStratumTheHeadlineSaysThePeriodsShareNone()
    {
        var result = await NoCommonStratumAnalysis.Value;

        Assert.Empty(result.Scope.StrataIndexes);
        Assert.Contains("; cost ", result.Headline);
        Assert.Contains("; the periods share no common time stratum", result.Headline);
        Assert.DoesNotContain(" within ", result.Headline);
    }

    [Fact]
    public async Task WithoutACommonTimeStratum_SpeedIsNotComputedForThatKind_AndOneStratumRunIsSuggested()
    {
        var result = await NoCommonStratumAnalysis.Value;

        foreach (string id in new[] { ChatConsistencyEndpointIds.TimeToFirstAnswerText, ChatConsistencyEndpointIds.StreamingRate })
        {
            var endpoint = EndpointOf(result, id);
            Assert.False(endpoint.Computed);
            Assert.Equal(ChatConsistencyNotComputedKinds.NoCommonStratum, endpoint.NotComputedKind);
        }
        Assert.Null(EndpointOf(result, ChatConsistencyEndpointIds.Work).NotComputedKind);

        var stratum = Assert.Single(result.NextRuns, n => n.Kind == "stratum");
        Assert.Equal("comparison", stratum.Period);
        Assert.Equal("3 runs of gpt-test on Core Suite starting in weekdays 08–12 UTC, in the comparison period.", stratum.Suggestion);
        Assert.Equal(4L, stratum.RepeatRunId);

        // Under the current build the comparison period can still take runs.
        Assert.DoesNotContain(result.NextRuns, n => n.Kind == ChatConsistencyNextRunKinds.NewCheckpoint);
        Assert.All(result.NextRuns, n => Assert.Null(n.TargetKind));

        Assert.Equal(new[] { "baseline", "comparison" }, result.PeriodHours!.Select(h => h.Period));
        Assert.Equal("weekdays 08–12 UTC", result.PeriodHours![0].Text);
        Assert.Equal(new[] { "Weekday 08–12 UTC" }, result.PeriodHours[0].Strata);
        Assert.Equal("weekdays 20–24 UTC", result.PeriodHours[1].Text);
    }

    [Fact]
    public async Task AMeasurementChange_IsNotComputedForThatKind_WithItsReasonsPunctuatedOnce()
    {
        using var db = NewDb();
        string key = Seed(db, new Scenario());
        foreach (var run in db.BenchmarkRuns.Local.Where(r => r.Id is 3 or 4)) run.ScoringMethodVersion = 13;
        db.SaveChanges();

        var result = await Service(db).AnalyzeAsync(Request(key), TestContext.Current.CancellationToken);

        var p1 = EndpointOf(result, ChatConsistencyEndpointIds.Quality);
        Assert.False(p1.Computed);
        Assert.Equal(ChatConsistencyNotComputedKinds.MeasurementChanged, p1.NotComputedKind);
        Assert.StartsWith("The measurement of quality changed between the periods (", p1.NotComputedReason);
        Assert.DoesNotContain(".)", p1.NotComputedReason, StringComparison.Ordinal);
        Assert.EndsWith("Re-grade every compared run with one assessor (a common grader), or choose relaxed pooling.", p1.NotComputedReason);
    }

    [Fact]
    public async Task RevisedQuestions_AreCountedAsQuestions_NotTwiceAsUnpairedItems()
    {
        using var db = NewDb();
        string key = Seed(db, new Scenario());
        foreach (var answer in db.BenchmarkRuns.Local.Where(r => r.Id is 3 or 4).SelectMany(r => r.Answers).Where(a => a.OrderIndex < 2))
        {
            answer.ItemRevisionUsed = 2;
        }
        db.SaveChanges();

        var result = await Service(db).AnalyzeAsync(Request(key), TestContext.Current.CancellationToken);

        var revised = Assert.Single(result.DataQuality, n => n.Kind == "revisedQuestions");
        Assert.Equal("2 of 24 questions (8.3%) were revised between the periods and are left out of the paired comparison; 22 are paired.", revised.Text);
        Assert.DoesNotContain(result.DataQuality, n => n.Kind == "unpairedItems");
    }

    [Fact]
    public async Task EachPeriodsLevels_ArePooledOverItsAnalyzedAnswers()
    {
        var result = await QualityDropAnalysis.Value;

        // The shared analysis's runs, seeded again for the expected values.
        using var db = NewDb();
        Seed(db, new Scenario { QualityDrop = true });

        var levels = result.PeriodLevels!;
        Assert.Equal(new[] { "baseline", "comparison" }, levels.Select(l => l.Period));
        var baselineAnswers = db.BenchmarkRuns.Local.Where(r => r.Id is 1 or 2).SelectMany(r => r.Answers).ToList();
        Assert.Equal(baselineAnswers.Count, levels[0].AnswerCount);
        Assert.Equal(baselineAnswers.Average(a => (double)a.OutputTokens!.Value), levels[0].MeanOutputTokensPerAnswer!.Value, 9);
        Assert.Equal(baselineAnswers.Average(a => (double)a.QualityScore!.Value), levels[0].NativeMeanQuality!.Value, 9);
        Assert.True(levels[1].NativeMeanQuality < levels[0].NativeMeanQuality);
        Assert.NotNull(levels[0].MedianTimeToFirstAnswerTextMs);
        Assert.NotNull(levels[0].MeanCostPerQuestionUsd);
        Assert.Equal(0, levels[0].FailedAnswerCount);
        Assert.Null(levels[0].OverallIndex);
    }

    // --- Freshness -------------------------------------------------------------------------------

    [Fact]
    public async Task ACurrentAnalysisWithUnchangedInputs_IsNotOutOfDate()
    {
        using var db = NewDb();
        string key = Seed(db, new Scenario { QualityDrop = true, SelectionRuns = true });
        var service = Service(db);
        var result = await service.AnalyzeAsync(ExplicitRequest(key, Selection()), TestContext.Current.CancellationToken);

        var freshness = await service.CheckFreshnessAsync(result.AnalysisId!.Value, TestContext.Current.CancellationToken);

        Assert.NotNull(freshness);
        Assert.False(freshness!.EarlierAnalysisCode);
        Assert.False(freshness.InputsChanged);
        Assert.Null(freshness.InputsNote);
        Assert.False(freshness.OutOfDate);
        Assert.Equal(ChatConsistencyAnalysisService.CurrentAnalysisCodeVersion, freshness.AnalysisCodeVersion);
    }

    [Fact]
    public async Task AnAnalysisWithMarginOverrides_IsCheckedUnderTheSameProtocol()
    {
        using var db = NewDb();
        string key = Seed(db, new Scenario { QualityDrop = true });
        var service = Service(db);
        var request = Request(key) with
        {
            ProtocolOverrides = new ChatConsistencyProtocolOverrides { Margins = new Dictionary<string, double> { ["P1"] = 4.0 }, Alpha = 0.1 }
        };
        var result = await service.AnalyzeAsync(request, TestContext.Current.CancellationToken);

        var freshness = await service.CheckFreshnessAsync(result.AnalysisId!.Value, TestContext.Current.CancellationToken);

        Assert.False(freshness!.InputsChanged);
        Assert.Null(freshness.InputsNote);
        Assert.False(freshness.OutOfDate);
    }

    [Fact]
    public async Task AnAnnotationAddedAfterSaving_MakesTheAnalysisOutOfDateForChangedInputs()
    {
        using var db = NewDb();
        string key = Seed(db, new Scenario { QualityDrop = true });
        var service = Service(db);
        var result = await service.AnalyzeAsync(Request(key), TestContext.Current.CancellationToken);

        await service.AddAnnotationAsync(new ChatConsistencyAnnotationInput
        {
            AtUtc = ComparisonDay1.AddHours(2),
            Provider = "OpenAI",
            Kind = ChatConsistencyAnnotationKind.ProviderStatement,
            Text = "A status note."
        }, TestContext.Current.CancellationToken);

        var freshness = await service.CheckFreshnessAsync(result.AnalysisId!.Value, TestContext.Current.CancellationToken);

        Assert.False(freshness!.EarlierAnalysisCode);
        Assert.True(freshness.InputsChanged);
        Assert.True(freshness.OutOfDate);
    }

    [Fact]
    public async Task ACommonGraderReGradeAfterSaving_MakesTheAnalysisOutOfDateForChangedInputs()
    {
        using var db = NewDb();
        string key = Seed(db, new Scenario { QualityDrop = true });
        var service = Service(db);
        var result = await service.AnalyzeAsync(Request(key), TestContext.Current.CancellationToken);

        db.BenchmarkAssessorCalibrations.Add(Calibration(1, 1, 900, ComparisonDay2, 80, 80));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var freshness = await service.CheckFreshnessAsync(result.AnalysisId!.Value, TestContext.Current.CancellationToken);

        Assert.True(freshness!.InputsChanged);
        Assert.True(freshness.OutOfDate);
    }

    [Fact]
    public async Task AnAnalysisThatRecordsNoRequest_IsNotCheckedForChangedInputs()
    {
        using var db = NewDb();
        int id = StoreResult(db, ChatConsistencyReportTestData.Result(), "request");

        var freshness = await Service(db).CheckFreshnessAsync(id, TestContext.Current.CancellationToken);

        Assert.False(freshness!.EarlierAnalysisCode);
        Assert.Null(freshness.InputsChanged);
        Assert.Equal("Not checked: this analysis does not record how its runs were chosen.", freshness.InputsNote);
        Assert.False(freshness.OutOfDate);
    }

    [Fact]
    public async Task AReplacedComparisonBuild_AsksForANewCheckpoint_InsteadOfRunsInTheComparisonPeriod()
    {
        using var db = NewDb();
        string key = Seed(db, new Scenario { ComparisonHourOffset = 12 });
        foreach (var run in db.BenchmarkRuns) run.HarnessVersion = "53";
        db.SaveChanges();

        var result = await Service(db).AnalyzeAsync(Request(key), TestContext.Current.CancellationToken);

        var checkpoint = Assert.Single(result.NextRuns, n => n.Kind == ChatConsistencyNextRunKinds.NewCheckpoint);
        Assert.Equal("newCheckpoint", checkpoint.Kind);
        Assert.Same(checkpoint, result.NextRuns[0]);
        Assert.DoesNotContain(result.NextRuns, n => n.Period == "comparison" && n.Kind is "checkpoint" or "stratum");
        Assert.DoesNotContain(result.NextRuns, n => n.Suggestion.Contains("in the comparison period", StringComparison.Ordinal));

        Assert.Equal(string.Empty, checkpoint.Period);
        Assert.Equal("The comparison period's Overseer build (harness 53) has been replaced, so no run can join that period any more.", checkpoint.Reason);
        Assert.Equal("Start a new baseline under the current build: 2 runs of gpt-test on Core Suite, on 2 different UTC days, each starting in "
            + "weekdays 20–24 UTC, with one control run of another provider's model beside each. Quality will start a new segment; to compare it "
            + "with these periods, re-grade with a common grader.", checkpoint.Suggestion);
        Assert.Equal("run", checkpoint.UnitNoun);
        Assert.Equal(2, checkpoint.Count);
        Assert.Equal(2, checkpoint.Days);
        Assert.Equal("Weekday 20–24 UTC", checkpoint.Stratum);
        Assert.Equal(ChatConsistencyComparisonSetKinds.Suite, checkpoint.TargetKind);
        Assert.Equal(7L, checkpoint.SuiteId);
        Assert.Null(checkpoint.BatteryId);
        Assert.Equal(key, checkpoint.SubjectModelKey);
        Assert.True(checkpoint.ControlSuggested);
        Assert.Equal(4L, checkpoint.RepeatRunId);

        // The record's JSON shape, as the client reads it.
        var json = JsonNode.Parse(JsonSerializer.Serialize(checkpoint, ChatConsistencyJson.Options))!.AsObject();
        Assert.Equal(
            new[] { "kind", "period", "endpointId", "reason", "suggestion", "repeatRunId", "unitNoun", "count", "days", "stratum", "targetKind",
                "suiteId", "batteryId", "subjectModelKey", "subjectModelConfigurationId", "controlSuggested" },
            json.Select(p => p.Key));
        Assert.Equal("newCheckpoint", json["kind"]!.GetValue<string>());
        Assert.Equal("suite", json["targetKind"]!.GetValue<string>());
        Assert.True(json["controlSuggested"]!.GetValue<bool>());
    }

    [Theory]
    [InlineData(HarnessImpact.Grading, true)]
    [InlineData(HarnessImpact.Scoring, true)]
    [InlineData(HarnessImpact.CandidateInput | HarnessImpact.Grading, true)]
    [InlineData(HarnessImpact.CandidateInput, false)]
    [InlineData(HarnessImpact.None, false)]
    public void ANewCheckpoint_SaysQualityStartsANewSegment_OnlyAcrossAGradingOrScoringChange(HarnessImpact impact, bool segment)
    {
        string text = ChatConsistencyAnalysisService.NewCheckpointSuggestion("battery run", 2, 2, "Claude 5.5 Haiku", "Two initial suites (revision 1)", null, impact);

        Assert.StartsWith("Start a new baseline under the current build: 2 battery runs of Claude 5.5 Haiku on Two initial suites (revision 1), "
            + "on 2 different UTC days, with one control run of another provider's model beside each.", text);
        Assert.Equal(segment, text.Contains("Quality will start a new segment; to compare it with these periods, re-grade with a common grader.", StringComparison.Ordinal));
    }

    [Fact]
    public void TheMinimumSampleText_StatesEachPartAsMetOrMissed()
    {
        var protocol = ChatConsistencyProtocol.V1;

        Assert.Equal("1 battery run per period on 1 day, below the minimum of 2 battery runs on 2 days per period; paired items 32, above the minimum of 20",
            ChatConsistencyAnalysisService.MinimumSampleText("battery run", 1, 1, 1, 1, 32, protocol));
        Assert.Equal("2 runs per period on 2 days, meeting the minimum of 2 runs on 2 days per period; paired items 12, below the minimum of 20",
            ChatConsistencyAnalysisService.MinimumSampleText("run", 2, 2, 2, 2, 12, protocol));
        Assert.Equal("2 runs per period on 2 days, meeting the minimum of 2 runs on 2 days per period",
            ChatConsistencyAnalysisService.MinimumSampleText("run", 2, 2, 2, 2, null, protocol));
    }

    [Fact]
    public async Task CountsInTheTextsAgreeWithTheirNumbers()
    {
        using var db = NewDb();
        string key = Seed(db, new Scenario { QualityDrop = true, BaselineOnOneDay = true });

        var result = await Service(db).AnalyzeAsync(Request(key), TestContext.Current.CancellationToken);

        var p1 = EndpointOf(result, ChatConsistencyEndpointIds.Quality);
        Assert.Equal("baseline 2 runs on 1 day, comparison 2 runs on 2 days, below the minimum of 2 runs on 2 days per period; "
            + "paired items 24, above the minimum of 20.", p1.MinimumSampleDetail);
        Assert.Contains(p1.RobustnessChecks, c => c.Name == "Runs on separate days"
            && c.Detail == "Baseline 2 runs on 1 day; comparison 2 runs on 2 days.");
        Assert.Contains(result.NextRuns, n => n.Period == "baseline" && n.Reason == "The baseline has 2 runs on 1 day.");

        string json = JsonSerializer.Serialize(result, ChatConsistencyJson.Options);
        Assert.DoesNotContain("(s)", json);
        Assert.DoesNotContain(".).", json);
    }

    [Fact]
    public async Task AClearQualityDropIsDegradedOnP1()
    {
        var result = await QualityDropAnalysis.Value;

        var p1 = EndpointOf(result, ChatConsistencyEndpointIds.Quality);
        Assert.True(p1.Computed);
        Assert.Equal(ConsistencyVerdict.ChangedDegraded, p1.Verdict);
        Assert.Equal("degraded", p1.VerdictLabel);
        Assert.InRange(p1.Estimate!.Value, -30.5, -29.5);
        Assert.Equal(QuestionCount, p1.ItemCount);
        Assert.True(p1.AdjustedPValue < 0.05);
        Assert.True(p1.MinimumSampleMet);

        // Native grades with no common grader and no anchor: the grader is not shown stable.
        Assert.Contains(p1.RobustnessChecks, c => c.Name == "Grader stability" && c.Status == ChatConsistencyCheckStatus.Failed);
        Assert.Equal(ChatConsistencyEvidenceGrade.Indicated, p1.Grade);

        // Unchanged speed, work and cost stay equivalent.
        Assert.Equal(ConsistencyVerdict.Equivalent, EndpointOf(result, ChatConsistencyEndpointIds.TimeToFirstAnswerText).Verdict);
        Assert.Equal(ConsistencyVerdict.Equivalent, EndpointOf(result, ChatConsistencyEndpointIds.Work).Verdict);
        Assert.Equal(ConsistencyVerdict.Equivalent, EndpointOf(result, ChatConsistencyEndpointIds.Cost).Verdict);

        // The control did not drop, so the drop is the target's own.
        Assert.Contains(result.Controls.Effects, e => e.EndpointId == ChatConsistencyEndpointIds.Quality && e.DidSeparatesTarget);
        Assert.Equal(ChatConsistencyEndpointIds.Quality, result.Attribution.TotalChanges[0].EndpointId);
    }

    [Fact]
    public async Task AToolGuidesChangeBetweenThePeriodsAppearsAsAnOverseerEvent()
    {
        var result = await QualityDropAnalysis.Value;

        var change = Assert.Single(result.Events, e => e.Kind == OverseerEventKinds.ToolGuides);
        Assert.Equal(GuidesBefore, change.From);
        Assert.Equal(GuidesAfter, change.To);
        Assert.StartsWith("tool guides edited on 2026-09-15", change.Label);
        Assert.DoesNotContain(result.Boundaries, b => !b.Bridged && b.Axes.Contains(ChatConsistencyAxis.Quality));
    }
}
