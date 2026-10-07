namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
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
