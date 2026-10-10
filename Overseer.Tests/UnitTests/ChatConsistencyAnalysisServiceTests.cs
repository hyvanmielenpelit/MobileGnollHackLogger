namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MobileGnollHackLogger.Data;
using Overseer.Services.Benchmarking;
using Overseer.Services.ChatConsistency;
using Overseer.Tests.Helpers;
using Xunit;

/// <summary>
/// The chat consistency analysis over an in-memory database: one subject in two periods with call
/// telemetry and a control subject. Covers persistence, determinism, the headline, a quality drop, an
/// Overseer event, the minimum-sample and legacy caps, the re-grade's refusals and job, and the delete guard;
/// and battery and suite comparison sets, in the analysis, in the evidence the builder loads for them and
/// in the timeline's battery points.
/// No network: the re-grade runs a fake calibration runner.
/// </summary>
public class ChatConsistencyAnalysisServiceTests
{
    private const string PromptSha = "e9b3e9a7c4d1b8f0a2e6c9d3b7f1a4e8c2d6b0f9a3e7c1d5b9f3a7e1c5d9b3f7";
    private const string GuidesBefore = "f59d8b30a1c7e4d2b6f0a8c3e9d5b1f7a3c9e5d1b7f3a9c5e1d7b3f9a5c1e7d3";
    private const string GuidesAfter = "0a1b2c3d4e5f60718293a4b5c6d7e8f90a1b2c3d4e5f60718293a4b5c6d7e8f9";
    private const string KnowledgeSha = "576ca574b2e8d0f6a4c2e8d4b0f6a2c8";
    private const string PricingJson = "{\"candidate\":{\"inputPerMillion\":1.0,\"outputPerMillion\":4.0}}";
    private const int QuestionCount = 24;

    // Tuesday 2026-09-01 and Wednesday 2026-09-02; Tuesday 2026-09-15 and Wednesday 2026-09-16.
    private static readonly DateTime BaselineDay1 = new(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime BaselineDay2 = new(2026, 9, 2, 9, 30, 0, DateTimeKind.Utc);
    private static readonly DateTime ComparisonDay1 = new(2026, 9, 15, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ComparisonDay2 = new(2026, 9, 16, 9, 30, 0, DateTimeKind.Utc);

    private sealed record Scenario
    {
        public bool QualityDrop { get; init; }
        public bool Legacy { get; init; }
        public bool BaselineOnOneDay { get; init; }
        public bool ToolGuidesChange { get; init; } = true;
        public bool Controls { get; init; } = true;
        public double ComparisonDurationFactor { get; init; } = 1.0;

        /// <summary>Hours added to the start of every comparison-period run, its controls' included.</summary>
        public double ComparisonHourOffset { get; init; }

        /// <summary>Six more runs of the subject inside the periods, #5–#10, for the run-selection tests.</summary>
        public bool SelectionRuns { get; init; }
    }

    private static DbContextOptions<ApplicationDbContext> NewOptions(string name, InMemoryDatabaseRoot root)
        => new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(name, root).Options;

    private static ApplicationDbContext NewDb() => new(NewOptions(Guid.NewGuid().ToString(), new InMemoryDatabaseRoot()));

    private static ChatConsistencyAnalysisService Service(ApplicationDbContext db)
        => new(db, new ChatConsistencyEvidenceBuilder(db), NullLogger<ChatConsistencyAnalysisService>.Instance);

    private static BenchmarkRun Run(
        long id,
        DateTime start,
        SystemAiConfigurationSnapshot candidate,
        SystemAiConfigurationSnapshot assessor,
        string guides,
        Func<int, int> quality,
        bool legacy,
        double durationFactor,
        string servedModel,
        int scoringMethod = 14)
    {
        var run = new BenchmarkRun
        {
            Id = id,
            BenchmarkSuiteIdUsed = 7,
            SuiteName = "Core Suite",
            StartedAtUtc = start,
            CompletedAtUtc = start.AddMinutes(10),
            Status = BenchmarkRunStatus.Completed,
            TestedModelSnapshot = candidate,
            AssessorModelSnapshot = assessor,
            HarnessVersion = HarnessImpactLedger.CurrentVersion,
            ScoringMethodVersion = scoringMethod,
            MaxParallelQuestionsUsed = 1,
            CallTelemetryVersion = legacy ? null : 1,
            CandidatePromptOptionsJson = "{\"verboseMode\":false}",
            CandidateSystemPromptSha256 = PromptSha,
            ToolGuidesSha256 = guides,
            KnowledgeBaseHeadSha = KnowledgeSha,
            PricingSnapshotJson = PricingJson,
            TotalQuestionCount = QuestionCount,
            AnsweredQuestionCount = QuestionCount,
            TotalAssessmentInputTokens = 120_000,
            TotalAssessmentOutputTokens = 24_000
        };

        long durationMs = (long)Math.Round(3000 * durationFactor);
        for (int q = 1; q <= QuestionCount; q++)
        {
            DateTime answerStart = start.AddSeconds(4 * (q - 1));
            var answer = new BenchmarkRunAnswer
            {
                Id = id * 100 + q,
                BenchmarkRunId = id,
                OrderIndex = q - 1,
                BenchmarkQuestionIdUsed = q,
                ItemRevisionUsed = 1,
                QuestionText = "Question " + q,
                AnswerText = "Answer text for question " + q,
                ExpectedPointsRecorded = true,
                ExpectedPointsUsed = "- point",
                Status = BenchmarkAnswerStatus.Ok,
                AssessmentStatus = BenchmarkAssessmentStatus.Scored,
                QualityScore = quality(q),
                AccuracyLevel = 5,
                CompletenessLevel = 5,
                ConcisenessLevel = 5,
                ReadabilityLevel = 5,
                InputTokens = 2000,
                OutputTokens = 400 + 10 * q,
                DurationMs = durationMs,
                ToolTimeMs = 500,
                ToolCallCount = 2,
                ModelCallCount = 2
            };

            if (!legacy)
            {
                answer.StartedAtUtc = answerStart;
                answer.CompletedAtUtc = answerStart.AddMilliseconds(durationMs);
                answer.PermitWaitMs = 0;
                answer.BackoffWaitMs = 0;
                answer.RetryAttemptCount = 0;
                answer.ServedModelId = servedModel;
                answer.ModelCalls.Add(new ModelCallTelemetry
                {
                    Id = id * 1000 + q,
                    Source = ModelCallSource.BenchmarkCandidate,
                    BenchmarkRunId = id,
                    Provider = candidate.Provider,
                    RequestedModelId = candidate.ModelId,
                    ServedModelId = servedModel,
                    StartedAtUtc = answerStart.AddMilliseconds(10),
                    CallIndex = 0,
                    AttemptCount = 1,
                    FirstEventMs = 300,
                    FirstOutputMs = (int)Math.Round((800 + 5 * q) * durationFactor),
                    CompletedMs = 2500,
                    LastDeltaMs = 2400,
                    Last80DecodeSpanMs = 1500,
                    Last80VisibleChars = 1200,
                    VisibleOutputChars = 1500,
                    OutputTokens = 400 + 10 * q,
                    ReasoningTokens = 0
                });
            }

            run.Answers.Add(answer);
        }

        return run;
    }

    /// <summary>
    /// Seeds two baseline runs and two comparison runs of the subject, and as many control runs of another
    /// provider, each period's runs on two days unless the scenario puts the baseline on one.
    /// </summary>
    private static string Seed(ApplicationDbContext db, Scenario scenario)
    {
        var subject = BenchmarkModelSnapshots.Model(provider: "OpenAI", modelId: "gpt-test");
        var control = BenchmarkModelSnapshots.Model(provider: "Google", modelId: "gemini-control");
        var assessor = BenchmarkModelSnapshots.Model(provider: "Anthropic", modelId: "test-assessor");

        string guidesAfter = scenario.ToolGuidesChange ? GuidesAfter : GuidesBefore;
        Func<int, int> steady = q => 85 + q % 5 - 2;
        Func<int, int> dropped = q => 55 + q % 5 - 2;
        DateTime baseline2 = scenario.BaselineOnOneDay ? BaselineDay1.AddHours(1) : BaselineDay2;
        DateTime comparison1 = ComparisonDay1.AddHours(scenario.ComparisonHourOffset);
        DateTime comparison2 = ComparisonDay2.AddHours(scenario.ComparisonHourOffset);

        var runs = new List<BenchmarkRun>
        {
            Run(1, BaselineDay1, subject, assessor, GuidesBefore, steady, scenario.Legacy, 1.0, "gpt-test-2026-09-01"),
            Run(2, baseline2, subject, assessor, GuidesBefore, steady, scenario.Legacy, 1.0, "gpt-test-2026-09-01"),
            Run(3, comparison1, subject, assessor, guidesAfter, scenario.QualityDrop ? dropped : steady, scenario.Legacy, scenario.ComparisonDurationFactor, "gpt-test-2026-09-01"),
            Run(4, comparison2, subject, assessor, guidesAfter, scenario.QualityDrop ? dropped : steady, scenario.Legacy, scenario.ComparisonDurationFactor, "gpt-test-2026-09-01")
        };

        if (scenario.Controls)
        {
            runs.Add(Run(11, BaselineDay1.AddHours(1), control, assessor, GuidesBefore, steady, scenario.Legacy, 1.0, "gemini-control-001"));
            runs.Add(Run(12, BaselineDay2.AddHours(1), control, assessor, GuidesBefore, steady, scenario.Legacy, 1.0, "gemini-control-001"));
            runs.Add(Run(13, comparison1.AddHours(1), control, assessor, guidesAfter, steady, scenario.Legacy, 1.0, "gemini-control-001"));
            runs.Add(Run(14, comparison2.AddHours(1), control, assessor, guidesAfter, steady, scenario.Legacy, 1.0, "gemini-control-001"));
        }

        if (scenario.SelectionRuns)
        {
            runs.Add(Run(5, new DateTime(2026, 9, 3, 9, 0, 0, DateTimeKind.Utc), subject, assessor, GuidesBefore, steady, scenario.Legacy, 1.0, "gpt-test-2026-09-01"));
            runs.Add(Run(6, new DateTime(2026, 8, 31, 9, 0, 0, DateTimeKind.Utc), subject, assessor, GuidesBefore, steady, scenario.Legacy, 1.0, "gpt-test-2026-09-01"));
            runs.Add(Run(7, new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc), subject, assessor, GuidesBefore, steady, scenario.Legacy, 1.0, "gpt-test-2026-09-01"));
            runs.Add(Run(8, new DateTime(2026, 9, 17, 9, 0, 0, DateTimeKind.Utc), subject, assessor, guidesAfter, steady, scenario.Legacy, 1.0, "gpt-test-2026-09-01"));
            runs.Add(Run(9, new DateTime(2026, 9, 14, 9, 0, 0, DateTimeKind.Utc), subject, assessor, guidesAfter, steady, scenario.Legacy, 1.0, "gpt-test-2026-09-01"));
            runs.Add(Run(10, new DateTime(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc), subject, assessor, guidesAfter, steady, scenario.Legacy, 1.0, "gpt-test-2026-09-01"));
        }

        db.BenchmarkRuns.AddRange(runs);
        db.SaveChanges();
        return ChatConsistencyComparability.ModelAxisKey(runs[0]);
    }

    private static ChatConsistencyAnalysisRequest Request(string subjectKey) => new()
    {
        Name = "GPT test, September",
        SubjectModelKey = subjectKey,
        BaselineStartUtc = new DateTime(2026, 8, 31, 0, 0, 0, DateTimeKind.Utc),
        BaselineEndUtc = new DateTime(2026, 9, 7, 0, 0, 0, DateTimeKind.Utc),
        ComparisonStartUtc = new DateTime(2026, 9, 14, 0, 0, 0, DateTimeKind.Utc),
        ComparisonEndUtc = new DateTime(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc)
    };

    /// <summary>The request with runs #1–#2 as the baseline and #3–#4 as the comparison, and <paramref name="selection"/>.</summary>
    private static ChatConsistencyAnalysisRequest ExplicitRequest(string subjectKey, ChatConsistencyRunSelection? selection) => Request(subjectKey) with
    {
        BaselineRunIds = new long[] { 1, 2 },
        ComparisonRunIds = new long[] { 3, 4 },
        RunSelection = selection
    };

    /// <summary>A step-1 selection: September, from run #1 to run #4, with <paramref name="leftOut"/> unchecked.</summary>
    private static ChatConsistencyRunSelection Selection(params long[] leftOut) => new()
    {
        RangeLabel = "2026-09-01 to 2026-09-30",
        RangeFromUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
        RangeToUtc = new DateTime(2026, 9, 30, 23, 59, 59, DateTimeKind.Utc),
        FirstRunId = 1,
        LastRunId = 4,
        LeftOutRunIds = leftOut
    };

    private const string RunSelectionLimitationStart = "The operator chose the runs: ";

    private static ChatConsistencyEndpointResult EndpointOf(ChatConsistencyAnalysisResult result, string id)
        => result.Endpoints.Single(e => e.Id == id);

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

        Assert.Equal(7, summary.AnalysisCodeVersion);
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
        string key = Seed(db, new Scenario { QualityDrop = true });
        var service = Service(db);
        var result = await service.AnalyzeAsync(Request(key), TestContext.Current.CancellationToken);

        var row = Assert.Single(db.ChatConsistencyAnalyses.ToList());
        var json = JsonNode.Parse(row.ResultJson)!.AsObject();
        Assert.True(json.Remove("subject"));
        Assert.True(json.Remove("endpoints"));
        row.ResultJson = json.ToJsonString();
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var summary = Assert.Single(await service.ListAnalysesAsync(TestContext.Current.CancellationToken));

        Assert.Null(summary.Subject);
        Assert.Empty(summary.Endpoints);
        Assert.Equal(result.Headline, summary.Headline);
    }

    [Fact]
    public async Task TwoAnalysesOfTheSameInputsStoreTheSameResultAndFingerprint()
    {
        using var db = NewDb();
        string key = Seed(db, new Scenario { QualityDrop = true });
        var service = Service(db);

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
        using var db = NewDb();
        string key = Seed(db, new Scenario { QualityDrop = true });

        var result = await Service(db).AnalyzeAsync(Request(key), TestContext.Current.CancellationToken);

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
        using var db = NewDb();
        string key = Seed(db, new Scenario { ComparisonHourOffset = 12 });

        var result = await Service(db).AnalyzeAsync(Request(key), TestContext.Current.CancellationToken);

        Assert.Empty(result.Scope.StrataIndexes);
        Assert.Contains("; cost ", result.Headline);
        Assert.Contains("; the periods share no common time stratum", result.Headline);
        Assert.DoesNotContain(" within ", result.Headline);
    }

    [Fact]
    public async Task WithoutACommonTimeStratum_SpeedIsNotComputedForThatKind_AndOneStratumRunIsSuggested()
    {
        using var db = NewDb();
        string key = Seed(db, new Scenario { ComparisonHourOffset = 12 });

        var result = await Service(db).AnalyzeAsync(Request(key), TestContext.Current.CancellationToken);

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
        Assert.Equal("2 of 24 questions (8.3 %) were revised between the periods and are left out of the paired comparison; 22 are paired.", revised.Text);
        Assert.DoesNotContain(result.DataQuality, n => n.Kind == "unpairedItems");
    }

    [Fact]
    public async Task EachPeriodsLevels_ArePooledOverItsAnalyzedAnswers()
    {
        using var db = NewDb();
        string key = Seed(db, new Scenario { QualityDrop = true });

        var result = await Service(db).AnalyzeAsync(Request(key), TestContext.Current.CancellationToken);

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
        Assert.Equal(ChatConsistencyAnalysisService.CurrentAnalysisCodeVersion, freshness.CurrentAnalysisCodeVersion);
        Assert.Equal(result.InputSha256, ChatConsistencyAnalysisService.ComputeInputSha256(
            result.Request!, ChatConsistencyProtocol.V1, await new ChatConsistencyEvidenceBuilder(db).LoadAsync(result.Request!, TestContext.Current.CancellationToken)));
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
    public async Task AnAnalysisSavedUnderEarlierCode_IsOutOfDate_WithoutCheckingItsInputs()
    {
        using var db = NewDb();
        string key = Seed(db, new Scenario { QualityDrop = true });
        var service = Service(db);
        var result = await service.AnalyzeAsync(Request(key), TestContext.Current.CancellationToken);
        var row = db.ChatConsistencyAnalyses.Single();
        row.AnalysisCodeVersion = 4;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var freshness = await service.CheckFreshnessAsync(result.AnalysisId!.Value, TestContext.Current.CancellationToken);

        Assert.True(freshness!.EarlierAnalysisCode);
        Assert.Null(freshness.InputsChanged);
        Assert.Equal("Not checked: the analysis code changed, so its input fingerprint is not comparable.", freshness.InputsNote);
        Assert.True(freshness.OutOfDate);
        Assert.Equal(4, freshness.AnalysisCodeVersion);
        Assert.Null(await service.CheckFreshnessAsync(999, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AnAnalysisThatRecordsNoRequest_IsNotCheckedForChangedInputs()
    {
        using var db = NewDb();
        string key = Seed(db, new Scenario { QualityDrop = true });
        var service = Service(db);
        var result = await service.AnalyzeAsync(Request(key), TestContext.Current.CancellationToken);
        var row = db.ChatConsistencyAnalyses.Single();
        var json = JsonNode.Parse(row.ResultJson)!.AsObject();
        Assert.True(json.Remove("request"));
        row.ResultJson = json.ToJsonString();
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var freshness = await service.CheckFreshnessAsync(result.AnalysisId!.Value, TestContext.Current.CancellationToken);

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

        // Under the current build the comparison period can still take runs.
        using var current = NewDb();
        string currentKey = Seed(current, new Scenario { ComparisonHourOffset = 12 });
        var kept = await Service(current).AnalyzeAsync(Request(currentKey), TestContext.Current.CancellationToken);
        Assert.DoesNotContain(kept.NextRuns, n => n.Kind == ChatConsistencyNextRunKinds.NewCheckpoint);
        Assert.Contains(kept.NextRuns, n => n.Kind == "stratum" && n.Period == "comparison");
        Assert.All(kept.NextRuns, n => Assert.Null(n.TargetKind));
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
        using var db = NewDb();
        string key = Seed(db, new Scenario { QualityDrop = true });

        var result = await Service(db).AnalyzeAsync(Request(key), TestContext.Current.CancellationToken);

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
        using var db = NewDb();
        string key = Seed(db, new Scenario { QualityDrop = true, ToolGuidesChange = true });

        var result = await Service(db).AnalyzeAsync(Request(key), TestContext.Current.CancellationToken);

        var change = Assert.Single(result.Events, e => e.Kind == OverseerEventKinds.ToolGuides);
        Assert.Equal(GuidesBefore, change.From);
        Assert.Equal(GuidesAfter, change.To);
        Assert.StartsWith("tool guides edited on 2026-09-15", change.Label);
        Assert.DoesNotContain(result.Boundaries, b => !b.Bridged && b.Axes.Contains(ChatConsistencyAxis.Quality));
    }

    // --- Caps ------------------------------------------------------------------------------------

    [Fact]
    public async Task AMinimumSampleShortfallCapsTheGradeAtIndicated()
    {
        using var db = NewDb();
        string key = Seed(db, new Scenario { QualityDrop = true, BaselineOnOneDay = true });

        var result = await Service(db).AnalyzeAsync(Request(key), TestContext.Current.CancellationToken);

        var p1 = EndpointOf(result, ChatConsistencyEndpointIds.Quality);
        Assert.Equal(ConsistencyVerdict.ChangedDegraded, p1.Verdict);
        Assert.False(p1.MinimumSampleMet);
        Assert.Equal(ChatConsistencyEvidenceGrade.Indicated, p1.Grade);
        Assert.Contains(p1.GradeReasons, r => r.StartsWith("Below the minimum sample", StringComparison.Ordinal));

        // Work is equivalent and otherwise robust, so the shortfall alone holds it at Indicated.
        var p4 = EndpointOf(result, ChatConsistencyEndpointIds.Work);
        Assert.Equal(ConsistencyVerdict.Equivalent, p4.Verdict);
        Assert.Equal(ChatConsistencyEvidenceGrade.Indicated, p4.Grade);

        // No earlier run of the model exists, so widening the baseline is not suggested.
        Assert.Contains(result.NextRuns, n => n.Period == "baseline" && n.RepeatRunId == null
            && n.Suggestion == "No other run of gpt-test exists for the baseline period. A later analysis can take this comparison period as its baseline.");
        Assert.DoesNotContain(result.NextRuns, n => n.Suggestion.StartsWith("Widen the baseline", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnEarlierUnusedRunOfTheModel_LetsTheBaselineBeWidened()
    {
        using var db = NewDb();
        string key = Seed(db, new Scenario { QualityDrop = true, BaselineOnOneDay = true, SelectionRuns = true });

        // Run #6 (2026-08-31) lies in the baseline window, before the step-1 dates, and before the first baseline run.
        var result = await Service(db).AnalyzeAsync(ExplicitRequest(key, Selection()), TestContext.Current.CancellationToken);

        Assert.Contains(result.RunSelection.UnanalyzedRuns, u => u.RunId == 6 && u.Reason == ChatConsistencyUnanalyzedReasons.OutsideDateRange);
        Assert.Contains(result.NextRuns, n => n.Period == "baseline" && n.RepeatRunId == 2
            && n.Suggestion.StartsWith("Widen the baseline period", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AWellSampledRobustEquivalenceIsEstablished()
    {
        using var db = NewDb();
        string key = Seed(db, new Scenario());

        var result = await Service(db).AnalyzeAsync(Request(key), TestContext.Current.CancellationToken);

        var p4 = EndpointOf(result, ChatConsistencyEndpointIds.Work);
        Assert.Equal(ConsistencyVerdict.Equivalent, p4.Verdict);
        Assert.True(p4.MinimumSampleMet);
        Assert.Equal(ChatConsistencyEvidenceGrade.Established, p4.Grade);
    }

    [Fact]
    public async Task LegacyRunsUseTheLegacyProxyAndCapAtIndicated()
    {
        using var db = NewDb();
        string key = Seed(db, new Scenario { Legacy = true, ComparisonDurationFactor = 2.0 });

        var result = await Service(db).AnalyzeAsync(Request(key), TestContext.Current.CancellationToken);

        var p2 = EndpointOf(result, ChatConsistencyEndpointIds.TimeToFirstAnswerText);
        Assert.True(p2.Computed);
        Assert.True(p2.LegacyProxy);
        Assert.Equal(ConsistencyVerdict.ChangedDegraded, p2.Verdict);
        Assert.Equal(ChatConsistencyEvidenceGrade.Indicated, p2.Grade);
        Assert.Contains(p2.GradeReasons, r => r.StartsWith("Legacy latency proxy", StringComparison.Ordinal));
        Assert.Contains("[legacy proxy]", result.Headline);

        var p3 = EndpointOf(result, ChatConsistencyEndpointIds.StreamingRate);
        Assert.False(p3.Computed);

        // Work is computed from legacy runs too, and capped.
        var p4 = EndpointOf(result, ChatConsistencyEndpointIds.Work);
        Assert.True(p4.Computed);
        Assert.True(p4.UsesLegacyData);
        Assert.NotEqual(ChatConsistencyEvidenceGrade.Established, p4.Grade);
        Assert.Contains(result.DataQuality, n => n.Kind == "estimatedStarts");
    }

    // --- Refusals --------------------------------------------------------------------------------

    [Fact]
    public async Task OverlappingPeriodsAreRefused()
    {
        using var db = NewDb();
        string key = Seed(db, new Scenario());
        var request = Request(key) with { ComparisonStartUtc = new DateTime(2026, 9, 5, 0, 0, 0, DateTimeKind.Utc) };

        await Assert.ThrowsAsync<ChatConsistencyRequestException>(() => Service(db).AnalyzeAsync(request, TestContext.Current.CancellationToken));
        Assert.Empty(db.ChatConsistencyAnalyses.ToList());
    }

    [Fact]
    public async Task DeletingAnAnalysisIsRefusedWhileAReportDocumentReferencesIt()
    {
        using var db = NewDb();
        string key = Seed(db, new Scenario { Controls = false });
        var service = Service(db);
        var result = await service.AnalyzeAsync(Request(key), TestContext.Current.CancellationToken);
        int id = result.AnalysisId!.Value;

        var document = new BenchmarkReportDocument
        {
            PackId = Guid.NewGuid(),
            Audience = default,
            Origin = BenchmarkReportDocumentOrigin.ChatConsistencyReport,
            Scope = BenchmarkReportScope.ChatConsistency,
            SubjectKey = "subject",
            SubjectLabel = "gpt-test",
            SubjectRunIdsJson = "[1,2,3,4]",
            ComparisonRequestJson = "{}",
            ChatConsistencyAnalysisId = id,
            SuiteName = "Core Suite",
            WriterDisplayName = "writer",
            WriterProvider = "Anthropic",
            WriterModelId = "writer-model",
            WriterPromptSha256 = new string('a', 64),
            FactsJson = "{}",
            ContentJson = "{}",
            WriterOutputJson = "{}",
            ValidationNotesJson = "[]",
            Title = "Chat consistency report",
            CreatedAtUtc = DateTime.UtcNow
        };
        db.BenchmarkReportDocuments.Add(document);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var refused = await service.DeleteAnalysisAsync(id, TestContext.Current.CancellationToken);
        Assert.True(refused.Found);
        Assert.False(refused.Deleted);
        Assert.Contains("report document", refused.Refusal);
        Assert.Single(db.ChatConsistencyAnalyses.ToList());

        db.BenchmarkReportDocuments.Remove(document);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var deleted = await service.DeleteAnalysisAsync(id, TestContext.Current.CancellationToken);
        Assert.True(deleted.Deleted);
        Assert.Empty(db.ChatConsistencyAnalyses.ToList());
    }

    // --- Run selection ---------------------------------------------------------------------------

    [Fact]
    public async Task TheRunSelectionClassifiesEveryUsableRunNotAnalyzedInBothPeriods()
    {
        using var db = NewDb();
        string key = Seed(db, new Scenario { QualityDrop = true, SelectionRuns = true });

        var result = await Service(db).AnalyzeAsync(ExplicitRequest(key, Selection(10, 5, 10)), TestContext.Current.CancellationToken);

        Assert.Equal(new long[] { 1, 2 }, result.Baseline.RunIds);
        Assert.Equal(new long[] { 3, 4 }, result.Comparison.RunIds);

        var selection = result.RunSelection;
        Assert.True(selection.Recorded);
        Assert.Equal("2026-09-01 to 2026-09-30", selection.RangeLabel);
        Assert.Equal(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), selection.RangeFromUtc);
        Assert.Equal(new DateTime(2026, 9, 30, 23, 59, 59, DateTimeKind.Utc), selection.RangeToUtc);
        Assert.Equal(1L, selection.FirstRunId);
        Assert.Equal(4L, selection.LastRunId);
        Assert.Equal(new long[] { 5, 10 }, selection.LeftOutRunIds);

        // #6 is both outside the dates and before the first run: the first matching reason wins.
        Assert.Equal(
            new[]
            {
                (6L, "baseline", ChatConsistencyUnanalyzedReasons.OutsideDateRange),
                (7L, "baseline", ChatConsistencyUnanalyzedReasons.BeforeFirstRun),
                (5L, "baseline", ChatConsistencyUnanalyzedReasons.LeftOut),
                (9L, "comparison", ChatConsistencyUnanalyzedReasons.NotSelected),
                (10L, "comparison", ChatConsistencyUnanalyzedReasons.LeftOut),
                (8L, "comparison", ChatConsistencyUnanalyzedReasons.AfterLastRun)
            },
            selection.UnanalyzedRuns.Select(u => (u.RunId, u.Period, u.Reason)).ToList());
        Assert.Equal(new DateTime(2026, 8, 31, 9, 0, 0, DateTimeKind.Utc), selection.UnanalyzedRuns[0].StartedAtUtc);

        var note = Assert.Single(result.DataQuality, n => n.Kind == "runSelection");
        Assert.Equal(
            "6 usable runs of the model inside the periods were not analyzed — left out in step 1: #5 (baseline), #10 (comparison); "
            + "outside the step-1 dates: #6 (baseline); before the first run: #7 (baseline); after the last run: #8 (comparison); "
            + "not assigned to a period: #9 (comparison).",
            note.Text);

        string limitation = Assert.Single(result.Limitations, l => l.StartsWith(RunSelectionLimitationStart, StringComparison.Ordinal));
        Assert.Equal(
            "The operator chose the runs: 6 usable runs of the model inside the periods were not analyzed (see the run selection). "
            + "The verdicts hold for the analyzed runs; leaving runs out after looking at the timeline can bias them.",
            limitation);
        Assert.DoesNotContain(
            BenchmarkReportPackValidator.ChatIntentWords.Concat(BenchmarkReportPackValidator.ChatMechanismWords),
            word => limitation.Contains(word, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExplicitRunsWithoutASelectionListTheOthersAsNotSelected()
    {
        using var db = NewDb();
        string key = Seed(db, new Scenario { SelectionRuns = true });

        var result = await Service(db).AnalyzeAsync(ExplicitRequest(key, null), TestContext.Current.CancellationToken);

        var selection = result.RunSelection;
        Assert.False(selection.Recorded);
        Assert.Null(selection.RangeLabel);
        Assert.Null(selection.FirstRunId);
        Assert.Empty(selection.LeftOutRunIds);
        Assert.Equal(new long[] { 6, 7, 5, 9, 10, 8 }, selection.UnanalyzedRuns.Select(u => u.RunId).ToList());
        Assert.All(selection.UnanalyzedRuns, u => Assert.Equal(ChatConsistencyUnanalyzedReasons.NotSelected, u.Reason));

        var note = Assert.Single(result.DataQuality, n => n.Kind == "runSelection");
        Assert.Equal(
            "6 usable runs of the model inside the periods were not analyzed — not assigned to a period: #6 (baseline), #7 (baseline), "
            + "#5 (baseline), #9 (comparison), #10 (comparison), #8 (comparison).",
            note.Text);
        Assert.Single(result.Limitations, l => l.StartsWith(RunSelectionLimitationStart, StringComparison.Ordinal));
    }

    [Fact]
    public async Task WithoutExplicitRunsEveryUsableRunIsAnalyzedAndNoneIsListed()
    {
        using var db = NewDb();
        string key = Seed(db, new Scenario { SelectionRuns = true });
        var request = Request(key) with { RunSelection = new ChatConsistencyRunSelection { RangeLabel = "All dates" } };

        var result = await Service(db).AnalyzeAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(new long[] { 6, 7, 1, 2, 5 }, result.Baseline.RunIds);
        Assert.Equal(new long[] { 9, 3, 10, 4, 8 }, result.Comparison.RunIds);
        Assert.True(result.RunSelection.Recorded);
        Assert.Equal("All dates", result.RunSelection.RangeLabel);
        Assert.Empty(result.RunSelection.UnanalyzedRuns);
        Assert.DoesNotContain(result.DataQuality, n => n.Kind == "runSelection");
        Assert.DoesNotContain(result.Limitations, l => l.StartsWith(RunSelectionLimitationStart, StringComparison.Ordinal));
    }

    [Fact]
    public async Task AMarkWhoseRunIsNotFoundIsIgnoredWithANote()
    {
        using var db = NewDb();
        string key = Seed(db, new Scenario { SelectionRuns = true });
        var selection = new ChatConsistencyRunSelection { FirstRunId = 999, LastRunId = 4 };

        var result = await Service(db).AnalyzeAsync(ExplicitRequest(key, selection), TestContext.Current.CancellationToken);

        Assert.Contains(result.DataQuality, n => n.Kind == "runSelection" && n.Text == "The first run of the selection, #999, was not found.");
        Assert.Equal(
            new[]
            {
                (6L, ChatConsistencyUnanalyzedReasons.NotSelected),
                (7L, ChatConsistencyUnanalyzedReasons.NotSelected),
                (5L, ChatConsistencyUnanalyzedReasons.NotSelected),
                (9L, ChatConsistencyUnanalyzedReasons.NotSelected),
                (10L, ChatConsistencyUnanalyzedReasons.NotSelected),
                (8L, ChatConsistencyUnanalyzedReasons.AfterLastRun)
            },
            result.RunSelection.UnanalyzedRuns.Select(u => (u.RunId, u.Reason)).ToList());
    }

    [Fact]
    public void AMalformedOrContradictoryRunSelectionIsRefused()
    {
        var request = Request("subject") with { BaselineRunIds = new long[] { 1, 2 }, ComparisonRunIds = new long[] { 3, 45 } };

        void Refused(ChatConsistencyRunSelection selection, string message)
        {
            var refusal = Assert.Throws<ChatConsistencyRequestException>(() => ChatConsistencyAnalysisService.Validate(request with { RunSelection = selection }));
            Assert.Equal(message, refusal.Message);
        }

        Refused(new ChatConsistencyRunSelection { RangeLabel = new string('x', 65) }, "The run selection's date label is at most 64 characters.");
        Refused(
            new ChatConsistencyRunSelection
            {
                RangeFromUtc = new DateTime(2026, 9, 30, 0, 0, 0, DateTimeKind.Utc),
                RangeToUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            "The run selection's dates end before they start.");
        Refused(
            new ChatConsistencyRunSelection { LeftOutRunIds = Enumerable.Range(1000, 5001).Select(i => (long)i).ToList() },
            "The run selection leaves out at most 5,000 runs.");
        Refused(new ChatConsistencyRunSelection { LeftOutRunIds = new long[] { 2 } }, "Run #2 is left out in step 1 but selected for the baseline.");
        Refused(new ChatConsistencyRunSelection { LeftOutRunIds = new long[] { 45 } }, "Run #45 is left out in step 1 but selected for the comparison.");

        // At the limits, the selection is accepted.
        ChatConsistencyAnalysisService.Validate(request with
        {
            RunSelection = new ChatConsistencyRunSelection
            {
                RangeLabel = new string('x', 64),
                LeftOutRunIds = Enumerable.Range(1000, 5000).Select(i => (long)i).ToList()
            }
        });
    }

    [Fact]
    public async Task ARefusedRunSelectionSavesNothing()
    {
        using var db = NewDb();
        string key = Seed(db, new Scenario());

        var refusal = await Assert.ThrowsAsync<ChatConsistencyRequestException>(
            () => Service(db).AnalyzeAsync(ExplicitRequest(key, Selection(3)), TestContext.Current.CancellationToken));

        Assert.Equal("Run #3 is left out in step 1 but selected for the comparison.", refusal.Message);
        Assert.Empty(db.ChatConsistencyAnalyses.ToList());
    }

    [Fact]
    public async Task AnalysesDifferingOnlyInTheLeftOutRunsHaveDifferentFingerprints()
    {
        using var db = NewDb();
        string key = Seed(db, new Scenario { QualityDrop = true, SelectionRuns = true });
        var service = Service(db);

        var first = await service.AnalyzeAsync(ExplicitRequest(key, Selection(5)), TestContext.Current.CancellationToken);
        var again = await service.AnalyzeAsync(ExplicitRequest(key, Selection(5)), TestContext.Current.CancellationToken);
        var other = await service.AnalyzeAsync(ExplicitRequest(key, Selection(5, 10)), TestContext.Current.CancellationToken);

        Assert.Equal(first.InputSha256, again.InputSha256);
        Assert.NotEqual(first.InputSha256, other.InputSha256);
        Assert.Equal(first.Headline, other.Headline);
    }

    [Fact]
    public async Task ASavedAnalysisRoundTripsItsRunSelection()
    {
        using var db = NewDb();
        string key = Seed(db, new Scenario { QualityDrop = true, SelectionRuns = true });
        var service = Service(db);

        var result = await service.AnalyzeAsync(ExplicitRequest(key, Selection(10, 5)), TestContext.Current.CancellationToken);
        var row = Assert.Single(db.ChatConsistencyAnalyses.ToList());
        Assert.Contains("\"runSelection\":{\"recorded\":true,", row.ResultJson);

        var reloaded = await service.GetAnalysisAsync(row.Id, TestContext.Current.CancellationToken);

        Assert.NotNull(reloaded);
        Assert.True(reloaded!.RunSelection.Recorded);
        Assert.Equal(new long[] { 5, 10 }, reloaded.RunSelection.LeftOutRunIds);
        Assert.Equal(DateTimeKind.Utc, reloaded.RunSelection.RangeFromUtc!.Value.Kind);
        Assert.Equal(6, reloaded.RunSelection.UnanalyzedRuns.Count);
        Assert.Equal(
            JsonSerializer.Serialize(result.RunSelection, ChatConsistencyJson.Options),
            JsonSerializer.Serialize(reloaded.RunSelection, ChatConsistencyJson.Options));
    }

    [Fact]
    public async Task AStoredResultWithoutARunSelectionReadsAsNotRecorded()
    {
        using var db = NewDb();
        string key = Seed(db, new Scenario { QualityDrop = true, SelectionRuns = true });
        var service = Service(db);
        var result = await service.AnalyzeAsync(ExplicitRequest(key, Selection(5)), TestContext.Current.CancellationToken);

        var row = Assert.Single(db.ChatConsistencyAnalyses.ToList());
        var json = JsonNode.Parse(row.ResultJson)!.AsObject();
        Assert.True(json.Remove("runSelection"));
        row.ResultJson = json.ToJsonString();
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var reloaded = await service.GetAnalysisAsync(row.Id, TestContext.Current.CancellationToken);

        Assert.NotNull(reloaded);
        Assert.Equal(result.Headline, reloaded!.Headline);
        Assert.False(reloaded.RunSelection.Recorded);
        Assert.Null(reloaded.RunSelection.RangeLabel);
        Assert.Empty(reloaded.RunSelection.LeftOutRunIds);
        Assert.Empty(reloaded.RunSelection.UnanalyzedRuns);
    }

    // --- Battery and suite sets ------------------------------------------------------------------

    private const string SecondSuite = "Second Suite";
    private const string BatteryName = "Two initial suites";

    private static BenchmarkBatteryDefinition TwoSuites() => new(
        1, BatteryName, 1, BenchmarkBatteryWeightingScheme.Equal,
        new[] { new BenchmarkBatteryDefinitionSuite(0, 7, "Core Suite", null), new BenchmarkBatteryDefinitionSuite(1, 8, SecondSuite, null) });

    private static string BatteryKey(BenchmarkBatteryDefinition definition) => ChatConsistencyComparisonSetKinds.BatteryKeyPrefix + definition.DefinitionSha256;

    private static ChatConsistencyComparisonSetRef BatterySet() => new() { Kind = ChatConsistencyComparisonSetKinds.Battery, Key = BatteryKey(TwoSuites()) };

    private static ChatConsistencyComparisonSetRef CoreSuiteSet() => new() { Kind = ChatConsistencyComparisonSetKinds.Suite, Key = "suite:id:7" };

    /// <summary>The run moved to Second Suite (id 8), its questions numbered from 101 so its items never pair with Core Suite's.</summary>
    private static BenchmarkRun OnSecondSuite(BenchmarkRun run)
    {
        run.BenchmarkSuiteIdUsed = 8;
        run.SuiteName = SecondSuite;
        foreach (var answer in run.Answers) answer.BenchmarkQuestionIdUsed += 100;
        return run;
    }

    private static BenchmarkBatteryRun BatteryRun(long id, DateTime start, BenchmarkBatteryDefinition definition, params (long RunId, int SuiteIndex)[] members)
    {
        var row = new BenchmarkBatteryRun
        {
            Id = id,
            BatteryName = definition.Name,
            DefinitionJson = definition.ToJson(),
            DefinitionSha256 = definition.DefinitionSha256,
            RunsPerSuite = 1,
            RequestedMemberCount = definition.Suites.Count,
            CompletedMemberCount = members.Length,
            Status = BenchmarkRunSeriesStatus.Completed,
            StartRequestJson = "{}",
            StartedAtUtc = start,
            CompletedAtUtc = start.AddDays(1)
        };
        for (int i = 0; i < members.Length; i++)
        {
            row.Members.Add(new BenchmarkBatteryRunMember
            {
                Id = id * 100 + i,
                BenchmarkRunId = members[i].RunId,
                SuiteIndex = members[i].SuiteIndex,
                Round = 1,
                AddedAtUtc = start
            });
        }

        return row;
    }

    /// <summary>
    /// The subject's Core Suite runs #1–#4 at the usual times and Second Suite runs #21–#24, each a day after
    /// its Core Suite partner, all with a quality index and no controls; battery runs #101–#104 of
    /// <see cref="TwoSuites"/> pair them, two per period on two days, each starting a minute before its Core
    /// Suite run. With <paramref name="extras"/>, in the baseline: #105, an incomplete battery run (#5, and
    /// #25 which failed); #106 under another definition (#6, #26); #107, sharing run #2 with #102, plus #27;
    /// and #31, a Core Suite run in no battery.
    /// </summary>
    private static string SeedBatteries(ApplicationDbContext db, bool extras = false, bool firstRunAtOtherScoring = false)
    {
        var subject = BenchmarkModelSnapshots.Model(provider: "OpenAI", modelId: "gpt-test");
        var assessor = BenchmarkModelSnapshots.Model(provider: "Anthropic", modelId: "test-assessor");
        Func<int, int> steady = q => 85 + q % 5 - 2;
        BenchmarkRun Core(long id, DateTime start, int scoring = 14)
            => Run(id, start, subject, assessor, GuidesBefore, steady, false, 1.0, "gpt-test-2026-09-01", scoring);
        BenchmarkRun Second(long id, DateTime start) => OnSecondSuite(Core(id, start));

        var runs = new List<BenchmarkRun>
        {
            Core(1, BaselineDay1, firstRunAtOtherScoring ? 13 : 14), Second(21, BaselineDay1.AddDays(1)),
            Core(2, BaselineDay2), Second(22, BaselineDay2.AddDays(1)),
            Core(3, ComparisonDay1), Second(23, ComparisonDay1.AddDays(1)),
            Core(4, ComparisonDay2), Second(24, ComparisonDay2.AddDays(1))
        };

        var definition = TwoSuites();
        var batteries = new List<BenchmarkBatteryRun>
        {
            BatteryRun(101, BaselineDay1.AddMinutes(-1), definition, (1, 0), (21, 1)),
            BatteryRun(102, BaselineDay2.AddMinutes(-1), definition, (2, 0), (22, 1)),
            BatteryRun(103, ComparisonDay1.AddMinutes(-1), definition, (3, 0), (23, 1)),
            BatteryRun(104, ComparisonDay2.AddMinutes(-1), definition, (4, 0), (24, 1))
        };

        if (extras)
        {
            var sep3 = new DateTime(2026, 9, 3, 12, 0, 0, DateTimeKind.Utc);
            var sep4 = new DateTime(2026, 9, 4, 9, 0, 0, DateTimeKind.Utc);
            var failed = Second(25, sep3.AddHours(1));
            failed.Status = BenchmarkRunStatus.Failed;
            runs.AddRange(new[]
            {
                Core(5, sep3), failed, Core(6, sep4), Second(26, sep4.AddDays(1)),
                Second(27, BaselineDay2.AddDays(1).AddHours(3)), Core(31, sep4.AddHours(6))
            });
            batteries.Add(BatteryRun(105, sep3.AddMinutes(-1), definition, (5, 0), (25, 1)));
            batteries.Add(BatteryRun(106, sep4.AddMinutes(-1), definition with { Scheme = BenchmarkBatteryWeightingScheme.ItemCount }, (6, 0), (26, 1)));
            batteries.Add(BatteryRun(107, BaselineDay2.AddHours(2), definition, (2, 0), (27, 1)));
        }

        foreach (var run in runs) run.QualityIndex = 85;
        db.BenchmarkRuns.AddRange(runs);
        db.BenchmarkBatteryRuns.AddRange(batteries);
        db.SaveChanges();
        return ChatConsistencyComparability.ModelAxisKey(runs[0]);
    }

    private static Task<ChatConsistencyEvidence> Evidence(ApplicationDbContext db, ChatConsistencyAnalysisRequest request)
        => new ChatConsistencyEvidenceBuilder(db).LoadAsync(request, TestContext.Current.CancellationToken);

    [Fact]
    public async Task ABatterySetAnalyzesItsBatteryRunsAsUnits()
    {
        using var db = NewDb();
        string key = SeedBatteries(db);

        var result = await Service(db).AnalyzeAsync(Request(key) with { ComparisonSet = BatterySet() }, TestContext.Current.CancellationToken);

        Assert.Equal(7, ChatConsistencyAnalysisService.CurrentAnalysisCodeVersion);
        Assert.Equal(7, result.AnalysisCodeVersion);
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
        Assert.Equal(7, row.AnalysisCodeVersion);

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

    private static Task<ChatConsistencyTimeline> Timeline(ApplicationDbContext db, string key, DateTime? fromUtc = null, DateTime? toUtc = null)
        => new ChatConsistencyEvidenceBuilder(db).GetTimelineAsync(key, fromUtc, toUtc, TestContext.Current.CancellationToken);

    /// <summary>A stored, complete battery analysis of <paramref name="batteryRunId"/> over <paramref name="memberRunIds"/>.</summary>
    private static BenchmarkBatteryAnalysis BatteryAnalysis(long id, long batteryRunId, DateTime computedAtUtc, double overallIndex, params long[] memberRunIds) => new()
    {
        Id = id,
        BenchmarkBatteryRunId = batteryRunId,
        ComputedAtUtc = computedAtUtc,
        MemberRunIdsJson = JsonSerializer.Serialize(memberRunIds),
        ResultJson = JsonSerializer.Serialize(new BenchmarkBatteryStatisticsResult
        {
            Complete = true,
            OverallIndex = new BenchmarkBatteryOverallIndex { PointEstimate = overallIndex }
        }),
        DefinitionSha256 = TwoSuites().DefinitionSha256,
        Complete = true
    };

    /// <summary>A calibration of <paramref name="runId"/> by assessor snapshot <paramref name="snapshotId"/>, one verdict per quality.</summary>
    private static BenchmarkAssessorCalibration Calibration(long id, long runId, long snapshotId, DateTime createdAtUtc, params double[] qualities) => new()
    {
        Id = id,
        BenchmarkRunId = runId,
        AssessorModelSnapshotId = snapshotId,
        CreatedAtUtc = createdAtUtc,
        AnswerCount = qualities.Length,
        VerdictsJson = JsonSerializer.Serialize(qualities.Select((q, i) => new { orderIndex = i, calibrationQualityScore = q }))
    };

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

    // --- Re-grade --------------------------------------------------------------------------------

    private static (ChatConsistencyRegradeService Service, ApplicationDbContext Db, ServiceProvider Provider, ChatConsistencyRegradeJobManager Jobs) Regrade(
        ChatConsistencyCalibrationRunner? runner = null)
    {
        string name = Guid.NewGuid().ToString();
        var root = new InMemoryDatabaseRoot();
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(name, root));
        var provider = services.BuildServiceProvider();
        var db = new ApplicationDbContext(NewOptions(name, root));
        var jobs = new ChatConsistencyRegradeJobManager();
        var service = new ChatConsistencyRegradeService(
            db, jobs, provider.GetRequiredService<IServiceScopeFactory>(), new BenchmarkRunManager(),
            NullLogger<ChatConsistencyRegradeService>.Instance, calibrationRunner: runner);
        return (service, db, provider, jobs);
    }

    private static SystemAiApiConfiguration AssessorConfig() => new()
    {
        Id = 501,
        DisplayName = "Test assessor",
        Provider = "Anthropic",
        ModelId = "test-assessor",
        IsEnabled = true,
        EncryptedApiKey = "encrypted",
        ModelRole = 4
    };

    [Fact]
    public async Task ReGradingRefusesARunAtAnotherScoringMethod()
    {
        var (service, db, provider, _) = Regrade((_, _, _, _) => Task.CompletedTask);
        using var _db = db;
        using var _provider = provider;

        var subject = BenchmarkModelSnapshots.Model(provider: "OpenAI", modelId: "gpt-test");
        var assessor = BenchmarkModelSnapshots.Model(provider: "Anthropic", modelId: "test-assessor");
        db.BenchmarkRuns.Add(Run(21, BaselineDay1, subject, assessor, GuidesBefore, q => 80, false, 1.0, "gpt-test-2026-09-01", scoringMethod: 13));
        db.SystemAiApiConfigurations.Add(AssessorConfig());
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var run = db.BenchmarkRuns.Include(r => r.Answers).Single(r => r.Id == 21);
        Assert.Equal(
            "Re-grading needs scoring method " + BenchmarkAssessmentPrompt.ScoringMethodVersion + "; run #21 is at method 13.",
            ChatConsistencyRegradeService.Refusal(run));

        var estimate = await service.EstimateAsync(new long[] { 21 }, 501, TestContext.Current.CancellationToken);
        var row = Assert.Single(estimate.Runs);
        Assert.False(row.Eligible);
        Assert.Contains("is at method 13", row.Refusal);
        Assert.Equal(0, estimate.EligibleRunCount);
        Assert.StartsWith("Estimate:", estimate.Note);

        var start = await service.StartAsync(new ChatConsistencyRegradeRequest { RunIds = new long[] { 21 }, AssessorConfigId = 501, Confirmed = true }, "admin", TestContext.Current.CancellationToken);
        Assert.False(start.Started);
        Assert.Contains("is at method 13", start.Refusal);
        Assert.Null(service.GetJob());
    }

    [Fact]
    public async Task ReGradingNeedsAConfirmedEstimate()
    {
        var (service, db, provider, _) = Regrade((_, _, _, _) => Task.CompletedTask);
        using var _db = db;
        using var _provider = provider;

        var start = await service.StartAsync(new ChatConsistencyRegradeRequest { RunIds = new long[] { 1 }, AssessorConfigId = 501, Confirmed = false }, "admin", TestContext.Current.CancellationToken);

        Assert.False(start.Started);
        Assert.StartsWith("Confirm the estimate first", start.Refusal);
    }

    [Fact]
    public async Task AReGradeJobCalibratesTheRunsInOrderWithOneAssessor()
    {
        var calls = new List<(long RunId, long ConfigId)>();
        ServiceProvider? provider = null;
        ChatConsistencyCalibrationRunner runner = async (runId, configId, _, ct) =>
        {
            lock (calls) calls.Add((runId, configId));
            using var scope = provider!.CreateScope();
            var scoped = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            scoped.BenchmarkAssessorCalibrations.Add(new BenchmarkAssessorCalibration
            {
                BenchmarkRunId = runId,
                AssessorModelConfigurationId = configId,
                CreatedAtUtc = DateTime.UtcNow,
                AnswerCount = QuestionCount,
                VerdictsJson = "[]"
            });
            await scoped.SaveChangesAsync(ct);
        };

        var (service, db, built, jobs) = Regrade(runner);
        provider = built;
        using var _db = db;
        using var _provider = built;

        var subject = BenchmarkModelSnapshots.Model(provider: "OpenAI", modelId: "gpt-test");
        var assessor = BenchmarkModelSnapshots.Model(provider: "Anthropic", modelId: "test-assessor");
        db.BenchmarkRuns.Add(Run(32, ComparisonDay1, subject, assessor, GuidesBefore, q => 80, false, 1.0, "gpt-test-2026-09-01"));
        db.BenchmarkRuns.Add(Run(31, BaselineDay1, subject, assessor, GuidesBefore, q => 80, false, 1.0, "gpt-test-2026-09-01"));
        db.SystemAiApiConfigurations.Add(AssessorConfig());
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var start = await service.StartAsync(new ChatConsistencyRegradeRequest { RunIds = new long[] { 32, 31 }, AssessorConfigId = 501, Confirmed = true }, "admin", TestContext.Current.CancellationToken);
        Assert.True(start.Started, start.Refusal);

        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (jobs.Current!.Status == ChatConsistencyRegradeJobStatus.Running && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        var job = service.GetJob()!;
        Assert.Equal("completed", job.Status);
        Assert.Equal(2, job.Done);
        Assert.Equal(2, job.Total);
        Assert.Empty(job.Errors);
        Assert.Equal(new[] { (31L, 501L), (32L, 501L) }, calls);
    }

    // --- Battery units: the paired floor, the missing controls, the Overall Index and its interval ----

    /// <summary>A stored, complete battery analysis whose Overall Index carries a 95 % half-width from item sampling alone.</summary>
    private static BenchmarkBatteryAnalysis BatteryAnalysisWithInterval(long id, long batteryRunId, double overallIndex, double halfWidth, params long[] memberRunIds)
    {
        var analysis = BatteryAnalysis(id, batteryRunId, ComparisonDay2, overallIndex, memberRunIds);
        analysis.ResultJson = JsonSerializer.Serialize(new BenchmarkBatteryStatisticsResult
        {
            Complete = true,
            OverallIndex = new BenchmarkBatteryOverallIndex { PointEstimate = overallIndex, ItemSamplingHalfWidth = halfWidth, CombinedHalfWidth = halfWidth }
        });
        return analysis;
    }

    [Fact]
    public async Task OneBatteryRunPerPeriod_UsesThePairedMinimumDetectableEffect_AFloor()
    {
        using var db = NewDb();
        string key = SeedBatteries(db);
        foreach (var answer in db.BenchmarkRuns.Local.Where(r => r.Id is 3 or 23).SelectMany(r => r.Answers))
        {
            answer.OutputTokens += answer.BenchmarkQuestionIdUsed % 3 == 0 ? 60 : 10;
        }
        db.SaveChanges();

        var result = await Service(db).AnalyzeAsync(
            Request(key) with { ComparisonSet = BatterySet(), BaselineBatteryRunIds = new long[] { 101 }, ComparisonBatteryRunIds = new long[] { 103 } },
            TestContext.Current.CancellationToken);

        var p4 = EndpointOf(result, ChatConsistencyEndpointIds.Work);
        Assert.True(p4.Computed);
        Assert.Equal(MinimumDetectableEffectResult.PairedFloorNote, p4.MinimumDetectableEffectNote);
        Assert.Null(p4.RunsPerPeriodForMargin);
        Assert.NotNull(p4.MinimumDetectableEffect);
        Assert.True(p4.MinimumDetectableEffect > 0 && p4.MinimumDetectableEffect < 0.2, p4.MinimumDetectableEffect.ToString());
    }

    [Fact]
    public async Task ABatteryComparisonWithoutControls_HasOneMissingControlNotePerPeriodAndBatteryRun()
    {
        using var db = NewDb();
        string key = SeedBatteries(db);

        var result = await Service(db).AnalyzeAsync(Request(key) with { ComparisonSet = BatterySet() }, TestContext.Current.CancellationToken);

        var missing = result.Controls.MissingControls;
        Assert.Equal(new[] { ("baseline", (long?)102L), ("comparison", (long?)104L) }, missing.Select(m => (m.Period, m.BatteryRunId)));
        Assert.All(missing, m => Assert.False(m.BuildReplaced));
        Assert.Equal("Core Suite, " + SecondSuite, missing[0].SuiteName);
        Assert.Equal("No control run for period baseline: make a battery run of a model from a provider other than OpenAI on "
            + BatteryName + " (revision 1) under the same Overseer build as battery run #102 (suites Core Suite, " + SecondSuite + ").",
            missing[0].SuggestedText);
    }

    [Fact]
    public async Task TheBatteryOverallIndexAndItsInterval_ReachTheLevelsAndTheTimeline()
    {
        using var db = NewDb();
        string key = SeedBatteries(db);
        db.BenchmarkBatteryAnalyses.AddRange(
            BatteryAnalysisWithInterval(1, 101, 82.05, 2.45, 1, 21),
            BatteryAnalysisWithInterval(2, 103, 81.7, 2.5, 3, 23));
        db.SaveChanges();

        var result = await Service(db).AnalyzeAsync(
            Request(key) with { ComparisonSet = BatterySet(), BaselineBatteryRunIds = new long[] { 101 }, ComparisonBatteryRunIds = new long[] { 103 } },
            TestContext.Current.CancellationToken);

        var baseline = result.PeriodLevels![0];
        Assert.Equal(82.05, baseline.OverallIndex);
        Assert.Equal(2.45, baseline.OverallIndexHalfWidth);
        Assert.Equal("question sampling only", baseline.OverallIndexIntervalNote);
        Assert.Equal(81.7, result.PeriodLevels[1].OverallIndex);

        var timeline = await Timeline(db, key);
        var point = timeline.BatteryPoints.Single(p => p.RunId == 101);
        Assert.Equal(82.05, point.OverallIndex);
        Assert.Equal(2.45, point.OverallIndexHalfWidth);
        Assert.Equal("question sampling only", point.OverallIndexIntervalNote);
        var without = timeline.BatteryPoints.Single(p => p.RunId == 102);
        Assert.Null(without.OverallIndexHalfWidth);
        Assert.Null(without.OverallIndexIntervalNote);
    }
}
