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
/// Overseer event, the minimum-sample and legacy caps, the re-grade's refusals and job, and the delete guard.
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

        var runs = new List<BenchmarkRun>
        {
            Run(1, BaselineDay1, subject, assessor, GuidesBefore, steady, scenario.Legacy, 1.0, "gpt-test-2026-09-01"),
            Run(2, baseline2, subject, assessor, GuidesBefore, steady, scenario.Legacy, 1.0, "gpt-test-2026-09-01"),
            Run(3, ComparisonDay1, subject, assessor, guidesAfter, scenario.QualityDrop ? dropped : steady, scenario.Legacy, scenario.ComparisonDurationFactor, "gpt-test-2026-09-01"),
            Run(4, ComparisonDay2, subject, assessor, guidesAfter, scenario.QualityDrop ? dropped : steady, scenario.Legacy, scenario.ComparisonDurationFactor, "gpt-test-2026-09-01")
        };

        if (scenario.Controls)
        {
            runs.Add(Run(11, BaselineDay1.AddHours(1), control, assessor, GuidesBefore, steady, scenario.Legacy, 1.0, "gemini-control-001"));
            runs.Add(Run(12, BaselineDay2.AddHours(1), control, assessor, GuidesBefore, steady, scenario.Legacy, 1.0, "gemini-control-001"));
            runs.Add(Run(13, ComparisonDay1.AddHours(1), control, assessor, guidesAfter, steady, scenario.Legacy, 1.0, "gemini-control-001"));
            runs.Add(Run(14, ComparisonDay2.AddHours(1), control, assessor, guidesAfter, steady, scenario.Legacy, 1.0, "gemini-control-001"));
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
        Assert.Contains(result.NextRuns, n => n.Period == "baseline" && n.RepeatRunId == 2);
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
            + "not selected in step 4: #9 (comparison).",
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
            "6 usable runs of the model inside the periods were not analyzed — not selected in step 4: #6 (baseline), #7 (baseline), "
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
}
