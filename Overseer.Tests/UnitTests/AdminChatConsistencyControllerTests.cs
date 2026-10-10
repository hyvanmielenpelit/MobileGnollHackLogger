namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MobileGnollHackLogger.Data;
using Overseer.Controllers;
using Overseer.Models;
using Overseer.Services.Benchmarking;
using Overseer.Services.ChatConsistency;
using Overseer.Tests.Helpers;
using Xunit;

/// <summary>
/// The chat consistency API over real services and an in-memory database: the model axes, the
/// timeline, run table, battery-run table and comparison sets, saved analyses and their delete guard, the re-grade's refusals, anchors,
/// annotations, and the report documents list accepting chat consistency documents. No network: the
/// re-grade's calibration runner is a no-op and no job is started.
/// </summary>
public class AdminChatConsistencyControllerTests
{
    private const string PromptSha = "e9b3e9a7c4d1b8f0a2e6c9d3b7f1a4e8c2d6b0f9a3e7c1d5b9f3a7e1c5d9b3f7";
    private const string GuidesSha = "f59d8b30a1c7e4d2b6f0a8c3e9d5b1f7a3c9e5d1b7f3a9c5e1d7b3f9a5c1e7d3";
    private const string KnowledgeSha = "576ca574b2e8d0f6a4c2e8d4b0f6a2c8";
    private const string PricingJson = "{\"candidate\":{\"inputPerMillion\":1.0,\"outputPerMillion\":4.0}}";
    private const int QuestionCount = 24;

    // Tuesday 2026-09-01 and Wednesday 2026-09-02; Tuesday 2026-09-15 and Wednesday 2026-09-16.
    private static readonly DateTime BaselineDay1 = new(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime BaselineDay2 = new(2026, 9, 2, 9, 30, 0, DateTimeKind.Utc);
    private static readonly DateTime ComparisonDay1 = new(2026, 9, 15, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ComparisonDay2 = new(2026, 9, 16, 9, 30, 0, DateTimeKind.Utc);

    /// <summary>The controller, its database and the service provider its re-grade service opens scopes from.</summary>
    private sealed class Harness : IDisposable
    {
        public Harness()
        {
            string name = Guid.NewGuid().ToString();
            var root = new InMemoryDatabaseRoot();
            var services = new ServiceCollection();
            services.AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(name, root));
            Provider = services.BuildServiceProvider();
            Db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(name, root).Options);

            var evidence = new ChatConsistencyEvidenceBuilder(Db);
            var analysis = new ChatConsistencyAnalysisService(Db, evidence, NullLogger<ChatConsistencyAnalysisService>.Instance);
            var regrade = new ChatConsistencyRegradeService(
                Db, new ChatConsistencyRegradeJobManager(), Provider.GetRequiredService<IServiceScopeFactory>(), new BenchmarkRunManager(),
                NullLogger<ChatConsistencyRegradeService>.Instance, calibrationRunner: (_, _, _, _) => Task.CompletedTask);

            var reports = ChatConsistencyReportHarness.ServiceFor(
                Db, Provider.GetRequiredService<IServiceScopeFactory>(), ChatConsistencyReportHarness.TestConfiguration(),
                new BenchmarkReportPackJobManager(), new BenchmarkReportPackServiceTests.WriterProvider());

            Controller = new AdminChatConsistencyController(analysis, evidence, regrade, reports)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "admin") }, "Test"))
                    }
                }
            };
        }

        public ApplicationDbContext Db { get; }
        public ServiceProvider Provider { get; }
        public AdminChatConsistencyController Controller { get; }

        public void Dispose()
        {
            Db.Dispose();
            Provider.Dispose();
        }
    }

    private static BenchmarkRun Run(long id, DateTime start, SystemAiConfigurationSnapshot candidate, SystemAiConfigurationSnapshot assessor)
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
            ScoringMethodVersion = 14,
            MaxParallelQuestionsUsed = 1,
            CallTelemetryVersion = 1,
            CandidatePromptOptionsJson = "{\"verboseMode\":false}",
            CandidateSystemPromptSha256 = PromptSha,
            ToolGuidesSha256 = GuidesSha,
            KnowledgeBaseHeadSha = KnowledgeSha,
            PricingSnapshotJson = PricingJson,
            TotalQuestionCount = QuestionCount,
            AnsweredQuestionCount = QuestionCount,
            TotalAssessmentInputTokens = 120_000,
            TotalAssessmentOutputTokens = 24_000
        };

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
                QualityScore = 85 + q % 5 - 2,
                AccuracyLevel = 5,
                CompletenessLevel = 5,
                ConcisenessLevel = 5,
                ReadabilityLevel = 5,
                InputTokens = 2000,
                OutputTokens = 400 + 10 * q,
                DurationMs = 3000,
                ToolTimeMs = 500,
                ToolCallCount = 2,
                ModelCallCount = 2,
                StartedAtUtc = answerStart,
                CompletedAtUtc = answerStart.AddMilliseconds(3000),
                PermitWaitMs = 0,
                BackoffWaitMs = 0,
                RetryAttemptCount = 0,
                ServedModelId = "gpt-test-2026-09-01"
            };
            answer.ModelCalls.Add(new ModelCallTelemetry
            {
                Id = id * 1000 + q,
                Source = ModelCallSource.BenchmarkCandidate,
                BenchmarkRunId = id,
                Provider = candidate.Provider,
                RequestedModelId = candidate.ModelId,
                ServedModelId = "gpt-test-2026-09-01",
                StartedAtUtc = answerStart.AddMilliseconds(10),
                CallIndex = 0,
                AttemptCount = 1,
                FirstEventMs = 300,
                FirstOutputMs = 800 + 5 * q,
                CompletedMs = 2500,
                LastDeltaMs = 2400,
                Last80DecodeSpanMs = 1500,
                Last80VisibleChars = 1200,
                VisibleOutputChars = 1500,
                OutputTokens = 400 + 10 * q,
                ReasoningTokens = 0
            });
            run.Answers.Add(answer);
        }

        return run;
    }

    /// <summary>Two baseline and two comparison runs of one subject, on two days per period; returns the subject's model key.</summary>
    private static string Seed(ApplicationDbContext db)
    {
        var subject = BenchmarkModelSnapshots.Model(provider: "OpenAI", modelId: "gpt-test");
        var assessor = BenchmarkModelSnapshots.Model(provider: "Anthropic", modelId: "test-assessor");
        var runs = new List<BenchmarkRun>
        {
            Run(1, BaselineDay1, subject, assessor),
            Run(2, BaselineDay2, subject, assessor),
            Run(3, ComparisonDay1, subject, assessor),
            Run(4, ComparisonDay2, subject, assessor)
        };
        db.BenchmarkRuns.AddRange(runs);
        db.SaveChanges();
        return ChatConsistencyComparability.ModelAxisKey(runs[0]);
    }

    private static BenchmarkBatteryDefinition TwoSuites(int revision = 1) => new(
        1, "Two initial suites", revision, BenchmarkBatteryWeightingScheme.Equal,
        new[] { new BenchmarkBatteryDefinitionSuite(0, 7, "Core Suite", null), new BenchmarkBatteryDefinitionSuite(1, 8, "Second Suite", null) });

    private static string BatterySetKey => ChatConsistencyComparisonSetKinds.BatteryKeyPrefix + TwoSuites().DefinitionSha256;

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
            row.Members.Add(new BenchmarkBatteryRunMember { Id = id * 100 + i, BenchmarkRunId = members[i].RunId, SuiteIndex = members[i].SuiteIndex, Round = 1, AddedAtUtc = start });
        }

        return row;
    }

    /// <summary>
    /// <see cref="Seed"/> plus Second Suite (id 8) runs #21–#24, each a day after its Core Suite partner, every
    /// run with a quality index; battery runs #101–#104 of <see cref="TwoSuites"/> pair them, each starting a
    /// minute before its Core Suite run. Returns the subject's model key.
    /// </summary>
    private static string SeedBatteries(ApplicationDbContext db)
    {
        string key = Seed(db);
        var first = db.BenchmarkRuns.Local.Single(r => r.Id == 1);
        var subject = first.TestedModelSnapshot!;
        var assessor = first.AssessorModelSnapshot!;
        foreach (var (id, start) in new[] { (21L, BaselineDay1), (22L, BaselineDay2), (23L, ComparisonDay1), (24L, ComparisonDay2) })
        {
            var run = Run(id, start.AddDays(1), subject, assessor);
            run.BenchmarkSuiteIdUsed = 8;
            run.SuiteName = "Second Suite";
            foreach (var answer in run.Answers) answer.BenchmarkQuestionIdUsed += 100;
            db.BenchmarkRuns.Add(run);
        }

        foreach (var run in db.BenchmarkRuns.Local) run.QualityIndex = 85;

        var definition = TwoSuites();
        db.BenchmarkBatteryRuns.AddRange(
            BatteryRun(101, BaselineDay1.AddMinutes(-1), definition, (1, 0), (21, 1)),
            BatteryRun(102, BaselineDay2.AddMinutes(-1), definition, (2, 0), (22, 1)),
            BatteryRun(103, ComparisonDay1.AddMinutes(-1), definition, (3, 0), (23, 1)),
            BatteryRun(104, ComparisonDay2.AddMinutes(-1), definition, (4, 0), (24, 1)));
        db.SaveChanges();
        return key;
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

    private static BenchmarkReportDocument Document(BenchmarkReportDocumentOrigin origin, string subjectKey, int? analysisId) => new()
    {
        PackId = Guid.NewGuid(),
        Audience = default,
        Origin = origin,
        Scope = origin == BenchmarkReportDocumentOrigin.ChatConsistencyReport ? BenchmarkReportScope.ChatConsistency : BenchmarkReportScope.Model,
        SubjectKey = subjectKey,
        SubjectLabel = "gpt-test",
        SubjectRunIdsJson = "[1,2,3,4]",
        ComparisonRequestJson = "{}",
        ChatConsistencyAnalysisId = analysisId,
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

    private static T ValueOf<T>(IActionResult result, int statusCode = StatusCodes.Status200OK)
    {
        var json = Assert.IsType<JsonResult>(result);
        Assert.Equal(statusCode, json.StatusCode);
        Assert.Same(ChatConsistencyJson.Options, json.SerializerSettings);
        return Assert.IsAssignableFrom<T>(json.Value);
    }

    private static string ErrorOf(IActionResult result)
    {
        var body = result switch
        {
            BadRequestObjectResult bad => bad.Value,
            ConflictObjectResult conflict => conflict.Value,
            _ => throw new Xunit.Sdk.XunitException("Expected a 400 or 409 with a body, got " + result.GetType().Name + ".")
        };
        return body?.GetType().GetProperty("error")?.GetValue(body) as string ?? string.Empty;
    }

    // --- Model axes, timeline and run table ------------------------------------------------------

    [Fact]
    public async Task ModelsListsTheSubjectWithItsRunCountAndDates()
    {
        using var h = new Harness();
        string key = Seed(h.Db);

        var axes = ValueOf<IReadOnlyList<ChatConsistencyModelAxis>>(await h.Controller.ListModels(CancellationToken.None));

        var axis = Assert.Single(axes);
        Assert.Equal(key, axis.Key);
        Assert.Equal(4, axis.RunCount);
        Assert.Equal(BaselineDay1, axis.FirstRunAtUtc);
        Assert.Equal(ComparisonDay2, axis.LastRunAtUtc);
    }

    [Fact]
    public async Task TimelineAndRunsRequireAModelKeyAndAnOrderedRange()
    {
        using var h = new Harness();
        string key = Seed(h.Db);
        var ct = CancellationToken.None;

        Assert.Equal(AdminChatConsistencyController.ModelKeyRequiredError, ErrorOf(await h.Controller.Timeline(null, null, null, ct)));
        Assert.Equal(AdminChatConsistencyController.ModelKeyRequiredError, ErrorOf(await h.Controller.Timeline("  ", null, null, ct)));
        Assert.Equal(AdminChatConsistencyController.ModelKeyRequiredError, ErrorOf(await h.Controller.Runs(null, null, null, ct)));
        Assert.Equal(AdminChatConsistencyController.RangeError, ErrorOf(await h.Controller.Timeline(key, ComparisonDay1, BaselineDay1, ct)));
        Assert.Equal(AdminChatConsistencyController.RangeError, ErrorOf(await h.Controller.Runs(key, ComparisonDay1, BaselineDay1, ct)));

        var timeline = ValueOf<ChatConsistencyTimeline>(await h.Controller.Timeline(key, null, null, ct));
        Assert.Equal(new long[] { 1, 2, 3, 4 }, timeline.Points.Select(p => p.RunId));

        var baseline = ValueOf<ChatConsistencyTimeline>(await h.Controller.Timeline(key, null, new DateTime(2026, 9, 7, 0, 0, 0, DateTimeKind.Utc), ct));
        Assert.Equal(new long[] { 1, 2 }, baseline.Points.Select(p => p.RunId));

        var rows = ValueOf<IReadOnlyList<ChatConsistencyRunRow>>(await h.Controller.Runs(key, null, null, ct));
        Assert.Equal(new long[] { 1, 2, 3, 4 }, rows.Select(r => r.RunId));
    }

    [Fact]
    public async Task ModelsTimelineAndRunsAnswer499WhenTheClientAborts()
    {
        using var h = new Harness();
        string key = Seed(h.Db);
        using var aborted = new CancellationTokenSource();
        aborted.Cancel();

        Assert.Equal(StatusCodes.Status499ClientClosedRequest, Assert.IsType<StatusCodeResult>(await h.Controller.ListModels(aborted.Token)).StatusCode);
        Assert.Equal(StatusCodes.Status499ClientClosedRequest, Assert.IsType<StatusCodeResult>(await h.Controller.Timeline(key, null, null, aborted.Token)).StatusCode);
        Assert.Equal(StatusCodes.Status499ClientClosedRequest, Assert.IsType<StatusCodeResult>(await h.Controller.Runs(key, null, null, aborted.Token)).StatusCode);
    }

    [Fact]
    public async Task BatteryRunsAndComparisonSetsRequireAModelKeyAndAnOrderedRange()
    {
        using var h = new Harness();
        string key = SeedBatteries(h.Db);
        var ct = CancellationToken.None;

        Assert.Equal(AdminChatConsistencyController.ModelKeyRequiredError, ErrorOf(await h.Controller.BatteryRuns(null, null, null, ct)));
        Assert.Equal(AdminChatConsistencyController.ModelKeyRequiredError, ErrorOf(await h.Controller.ComparisonSets(" ", null, null, ct)));
        Assert.Equal(AdminChatConsistencyController.RangeError, ErrorOf(await h.Controller.BatteryRuns(key, ComparisonDay1, BaselineDay1, ct)));
        Assert.Equal(AdminChatConsistencyController.RangeError, ErrorOf(await h.Controller.ComparisonSets(key, ComparisonDay1, BaselineDay1, ct)));

        using var aborted = new CancellationTokenSource();
        aborted.Cancel();
        Assert.Equal(StatusCodes.Status499ClientClosedRequest, Assert.IsType<StatusCodeResult>(await h.Controller.BatteryRuns(key, null, null, aborted.Token)).StatusCode);
        Assert.Equal(StatusCodes.Status499ClientClosedRequest, Assert.IsType<StatusCodeResult>(await h.Controller.ComparisonSets(key, null, null, aborted.Token)).StatusCode);
    }

    [Fact]
    public async Task BatteryRunsListsTheSubjectsBatteryRunsNewestFirstWithTheirMembers()
    {
        using var h = new Harness();
        string key = SeedBatteries(h.Db);
        var ct = CancellationToken.None;

        var rows = ValueOf<IReadOnlyList<ChatConsistencyBatteryRunRow>>(await h.Controller.BatteryRuns(key, null, null, ct));
        Assert.Equal(new long[] { 104, 103, 102, 101 }, rows.Select(r => r.BatteryRunId));

        var oldest = rows[^1];
        Assert.Equal("Two initial suites", oldest.BatteryName);
        Assert.Equal(1, oldest.DefinitionRevision);
        Assert.Equal(BatterySetKey, oldest.SetKey);
        Assert.Equal(2, oldest.SuiteCount);
        Assert.True(oldest.Complete);
        Assert.Null(oldest.IncompleteReason);
        Assert.Equal(BaselineDay1.AddMinutes(-1), oldest.StartedAtUtc);
        Assert.Equal(new long[] { 1, 21 }, oldest.Members.Select(m => m.RunId));
        Assert.Equal(new int?[] { 1, 2 }, oldest.Members.Select(m => m.BatterySuitePosition));
        Assert.All(oldest.Members, m => Assert.Equal(101L, m.BatteryRunId));
        Assert.Equal(new[] { HarnessImpactLedger.CurrentVersion }, oldest.HarnessVersions);
        Assert.True(oldest.Eligibility.Single(e => e.Axis == ChatConsistencyAxis.Quality).Eligible);

        var baseline = ValueOf<IReadOnlyList<ChatConsistencyBatteryRunRow>>(
            await h.Controller.BatteryRuns(key, null, new DateTime(2026, 9, 7, 0, 0, 0, DateTimeKind.Utc), ct));
        Assert.Equal(new long[] { 102, 101 }, baseline.Select(r => r.BatteryRunId));

        // The run table names each run's suite and battery.
        var run21 = ValueOf<IReadOnlyList<ChatConsistencyRunRow>>(await h.Controller.Runs(key, null, null, ct)).Single(r => r.RunId == 21);
        Assert.Equal(8L, run21.SuiteId);
        Assert.Equal("id:8", run21.SuiteKey);
        Assert.Equal(101L, run21.BatteryRunId);
        Assert.Equal("Two initial suites", run21.BatteryName);
        Assert.Equal(2, run21.BatterySuitePosition);
        Assert.Equal(2, run21.BatterySuiteCount);

        var axis = Assert.Single(ValueOf<IReadOnlyList<ChatConsistencyModelAxis>>(await h.Controller.ListModels(ct)));
        Assert.Equal(4, axis.BatteryRunCount);
    }

    [Fact]
    public async Task AnIncompleteBatteryRunIsListedWithItsReason()
    {
        using var h = new Harness();
        string key = SeedBatteries(h.Db);
        var ct = CancellationToken.None;
        h.Db.BenchmarkBatteryRunMembers.Single(m => m.BenchmarkRunId == 23).GuardFailure = "tool guides changed";
        await h.Db.SaveChangesAsync(ct);

        var row = ValueOf<IReadOnlyList<ChatConsistencyBatteryRunRow>>(await h.Controller.BatteryRuns(key, null, null, ct)).Single(r => r.BatteryRunId == 103);
        Assert.False(row.Complete);
        Assert.Equal("1 of 2 suites usable", row.IncompleteReason);
        Assert.Equal(new long[] { 3 }, row.Members.Select(m => m.RunId));
    }

    [Fact]
    public async Task ComparisonSetsListTheBatteryFirstAndDefaultToIt()
    {
        using var h = new Harness();
        string key = SeedBatteries(h.Db);
        var ct = CancellationToken.None;

        var sets = ValueOf<ChatConsistencyComparisonSets>(await h.Controller.ComparisonSets(key, null, null, ct));

        Assert.Equal(new[] { BatterySetKey, "suite:id:8", "suite:id:7" }, sets.Sets.Select(s => s.Key));
        Assert.Equal(BatterySetKey, sets.DefaultKey);

        var battery = sets.Sets[0];
        Assert.Equal(ChatConsistencyComparisonSetKinds.Battery, battery.Kind);
        Assert.Equal("Two initial suites (revision 1)", battery.Label);
        Assert.Equal(4, battery.UnitCount);
        Assert.Equal(8, battery.MemberRunCount);
        Assert.Equal(ComparisonDay2.AddMinutes(-1), battery.LatestStartedAtUtc);

        var core = sets.Sets[2];
        Assert.Equal(ChatConsistencyComparisonSetKinds.Suite, core.Kind);
        Assert.Equal("Core Suite", core.Label);
        Assert.Equal(4, core.UnitCount);
        Assert.Equal(4, core.MemberRunCount);
        Assert.Equal("Second Suite", sets.Sets[1].Label);
    }

    [Fact]
    public async Task ComparisonSetsJoinTheRevisionsOfOneDefinition()
    {
        using var h = new Harness();
        string key = SeedBatteries(h.Db);
        var ct = CancellationToken.None;

        // A later revision with the same suites and weights hashes the same; run #4 serves it as well.
        h.Db.BenchmarkBatteryRuns.Add(BatteryRun(105, ComparisonDay2.AddHours(3), TwoSuites(revision: 2), (4, 0), (24, 1)));
        await h.Db.SaveChangesAsync(ct);

        var battery = ValueOf<ChatConsistencyComparisonSets>(await h.Controller.ComparisonSets(key, null, null, ct)).Sets[0];
        Assert.Equal(BatterySetKey, battery.Key);
        Assert.Equal("Two initial suites (revisions 1, 2)", battery.Label);
        Assert.Equal(5, battery.UnitCount);
        Assert.Equal(8, battery.MemberRunCount);
    }

    [Fact]
    public async Task WithoutBatteryRunsTheDefaultIsTheSuiteOfTheNewestRun()
    {
        using var h = new Harness();
        string key = Seed(h.Db);

        var sets = ValueOf<ChatConsistencyComparisonSets>(await h.Controller.ComparisonSets(key, null, null, CancellationToken.None));

        var suite = Assert.Single(sets.Sets);
        Assert.Equal("suite:id:7", suite.Key);
        Assert.Equal("Core Suite", suite.Label);
        Assert.Equal("suite:id:7", sets.DefaultKey);
        Assert.Empty(ValueOf<IReadOnlyList<ChatConsistencyBatteryRunRow>>(await h.Controller.BatteryRuns(key, null, null, CancellationToken.None)));
    }

    [Fact]
    public void ToUtcConvertsALocalTimeAndTakesAnUnspecifiedTimeAsUtc()
    {
        var local = new DateTime(2026, 9, 15, 12, 0, 0, DateTimeKind.Local);
        var converted = AdminChatConsistencyController.ToUtc(local);
        Assert.Equal(DateTimeKind.Utc, converted.Kind);
        Assert.Equal(local.ToUniversalTime(), converted);

        var unspecified = AdminChatConsistencyController.ToUtc(new DateTime(2026, 9, 15, 12, 0, 0, DateTimeKind.Unspecified));
        Assert.Equal(new DateTime(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc), unspecified);
        Assert.Equal(DateTimeKind.Utc, unspecified.Kind);
    }

    // --- Analyses --------------------------------------------------------------------------------

    [Fact]
    public async Task PostingAnAnalysisSavesItAndGetReturnsIt()
    {
        using var h = new Harness();
        string key = Seed(h.Db);
        var ct = CancellationToken.None;

        var saved = ValueOf<ChatConsistencyAnalysisResult>(await h.Controller.Analyze(Request(key), ct));
        Assert.NotNull(saved.AnalysisId);
        int id = saved.AnalysisId!.Value;
        Assert.Equal(id, Assert.Single(h.Db.ChatConsistencyAnalyses.ToList()).Id);

        // The result serializes in the stored format, enums as strings.
        string json = JsonSerializer.Serialize(saved, ChatConsistencyJson.Options);
        Assert.Contains("\"analysisId\":" + id, json);

        var loaded = ValueOf<ChatConsistencyAnalysisResult>(await h.Controller.GetAnalysis(id, ct));
        Assert.Equal(saved.Headline, loaded.Headline);
        Assert.Equal(id, loaded.AnalysisId);

        var list = ValueOf<IReadOnlyList<ChatConsistencyAnalysisSummary>>(await h.Controller.ListAnalyses(ct));
        var summary = Assert.Single(list);
        Assert.Equal(id, summary.Id);
        Assert.Equal("GPT test, September", summary.Name);
        Assert.Equal(saved.Headline, summary.Headline);

        Assert.IsType<NotFoundResult>(await h.Controller.GetAnalysis(id + 1000, ct));
    }

    [Fact]
    public async Task TheAnalysesListCarriesEachAnalysisSubjectAndEndpointVerdicts()
    {
        using var h = new Harness();
        string key = Seed(h.Db);
        var ct = CancellationToken.None;
        var saved = ValueOf<ChatConsistencyAnalysisResult>(await h.Controller.Analyze(Request(key), ct));

        var list = ValueOf<IReadOnlyList<ChatConsistencyAnalysisSummary>>(await h.Controller.ListAnalyses(ct));
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(list, ChatConsistencyJson.Options));
        var summary = Assert.Single(doc.RootElement.EnumerateArray().ToList());

        var subject = summary.GetProperty("subject");
        Assert.Equal(
            new[] { "displayName", "provider", "modelId", "thinkingLevel", "serviceTier" },
            subject.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal(saved.Subject.DisplayName, subject.GetProperty("displayName").GetString());
        Assert.Equal(saved.Subject.Provider, subject.GetProperty("provider").GetString());
        Assert.Equal(saved.Subject.ModelId, subject.GetProperty("modelId").GetString());

        var endpoints = summary.GetProperty("endpoints").EnumerateArray().ToList();
        Assert.Equal(saved.Endpoints.Select(e => e.Id), endpoints.Select(e => e.GetProperty("id").GetString()));
        foreach (var endpoint in endpoints)
        {
            Assert.Equal(
                new[] { "id", "name", "computed", "verdictLabel", "grade" },
                endpoint.EnumerateObject().Select(p => p.Name).ToArray());
            Assert.Contains(endpoint.GetProperty("grade").GetString(), new[] { "established", "indicated", "notEstablished" });
        }

        var p1 = saved.Endpoints.Single(e => e.Id == ChatConsistencyEndpointIds.Quality);
        var p1Json = endpoints.Single(e => e.GetProperty("id").GetString() == ChatConsistencyEndpointIds.Quality);
        Assert.Equal(p1.Name, p1Json.GetProperty("name").GetString());
        Assert.Equal(p1.Computed, p1Json.GetProperty("computed").GetBoolean());
        Assert.Equal(p1.VerdictLabel, p1Json.GetProperty("verdictLabel").GetString());
        Assert.Equal(JsonNamingPolicy.CamelCase.ConvertName(p1.Grade.ToString()), p1Json.GetProperty("grade").GetString());
    }

    [Fact]
    public async Task PostingAnAnalysisRefusesOverlappingPeriodsAndAMissingBody()
    {
        using var h = new Harness();
        string key = Seed(h.Db);
        var ct = CancellationToken.None;

        var overlapping = Request(key) with { ComparisonStartUtc = new DateTime(2026, 9, 5, 0, 0, 0, DateTimeKind.Utc) };
        Assert.Equal("The baseline and comparison periods overlap.", ErrorOf(await h.Controller.Analyze(overlapping, ct)));
        Assert.Equal(AdminChatConsistencyController.BodyRequiredError, ErrorOf(await h.Controller.Analyze(null, ct)));
        Assert.Empty(h.Db.ChatConsistencyAnalyses.ToList());
    }

    [Fact]
    public async Task DeletingAnAnalysisIsAConflictWhileAReportDocumentReferencesIt()
    {
        using var h = new Harness();
        string key = Seed(h.Db);
        var ct = CancellationToken.None;
        int id = ValueOf<ChatConsistencyAnalysisResult>(await h.Controller.Analyze(Request(key), ct)).AnalysisId!.Value;

        var document = Document(BenchmarkReportDocumentOrigin.ChatConsistencyReport, "chat-consistency:" + id, id);
        h.Db.BenchmarkReportDocuments.Add(document);
        await h.Db.SaveChangesAsync(ct);

        var refused = await h.Controller.DeleteAnalysis(id, ct);
        Assert.IsType<ConflictObjectResult>(refused);
        Assert.Contains("report document", ErrorOf(refused));
        Assert.Single(h.Db.ChatConsistencyAnalyses.ToList());

        Assert.IsType<NotFoundResult>(await h.Controller.DeleteAnalysis(id + 1000, ct));

        h.Db.BenchmarkReportDocuments.Remove(document);
        await h.Db.SaveChangesAsync(ct);
        Assert.IsType<NoContentResult>(await h.Controller.DeleteAnalysis(id, ct));
        Assert.Empty(h.Db.ChatConsistencyAnalyses.ToList());
    }

    // --- Re-grade --------------------------------------------------------------------------------

    [Fact]
    public async Task StartingAReGradeWithoutConfirmationIsABadRequest()
    {
        using var h = new Harness();
        Seed(h.Db);
        var ct = CancellationToken.None;

        var unconfirmed = await h.Controller.StartRegrade(
            new ChatConsistencyRegradeRequest { RunIds = new long[] { 1 }, AssessorConfigId = 501, Confirmed = false }, ct);
        Assert.StartsWith("Confirm the estimate first", ErrorOf(unconfirmed));
        Assert.Equal(AdminChatConsistencyController.BodyRequiredError, ErrorOf(await h.Controller.StartRegrade(null, ct)));

        Assert.IsType<NoContentResult>(h.Controller.GetRegradeJob());
        Assert.Equal(AdminChatConsistencyController.NoRegradeRunningError, ErrorOf(h.Controller.CancelRegrade()));
    }

    [Fact]
    public async Task EstimatingAReGradeNeedsRunsAndReportsEachRun()
    {
        using var h = new Harness();
        Seed(h.Db);
        var ct = CancellationToken.None;

        Assert.Equal(AdminChatConsistencyController.NoRegradeRunsError,
            ErrorOf(await h.Controller.EstimateRegrade(new ChatConsistencyRegradeEstimateRequest { AssessorConfigId = 501 }, ct)));
        var tooMany = Enumerable.Range(1, ChatConsistencyRegradeService.MaxRunsPerJob + 1).Select(i => (long)i).ToList();
        Assert.Contains("at most", ErrorOf(await h.Controller.EstimateRegrade(
            new ChatConsistencyRegradeEstimateRequest { RunIds = tooMany, AssessorConfigId = 501 }, ct)));

        var estimate = ValueOf<ChatConsistencyRegradeEstimate>(await h.Controller.EstimateRegrade(
            new ChatConsistencyRegradeEstimateRequest { RunIds = new List<long> { 2, 1, 999 }, AssessorConfigId = 501 }, ct));
        Assert.Equal(new long[] { 1, 2, 999 }, estimate.Runs.Select(r => r.RunId));
        Assert.False(estimate.Runs.Single(r => r.RunId == 999).Eligible);
    }

    // --- Anchors ---------------------------------------------------------------------------------

    [Fact]
    public async Task TheAnchorTogglesAndAnUnknownRunIsNotFound()
    {
        using var h = new Harness();
        Seed(h.Db);
        var ct = CancellationToken.None;

        var marked = ValueOf<ChatConsistencyAnchorResponse>(await h.Controller.SetAnchor(2, new ChatConsistencyAnchorRequest { IsAnchor = true }, ct));
        Assert.Equal(2, marked.RunId);
        Assert.True(marked.IsAnchor);
        Assert.True(h.Db.BenchmarkRuns.Single(r => r.Id == 2).IsConsistencyAnchor);

        ValueOf<ChatConsistencyAnchorResponse>(await h.Controller.SetAnchor(2, new ChatConsistencyAnchorRequest { IsAnchor = false }, ct));
        Assert.False(h.Db.BenchmarkRuns.Single(r => r.Id == 2).IsConsistencyAnchor);

        Assert.IsType<NotFoundResult>(await h.Controller.SetAnchor(999, new ChatConsistencyAnchorRequest { IsAnchor = true }, ct));
        Assert.Equal(AdminChatConsistencyController.BodyRequiredError, ErrorOf(await h.Controller.SetAnchor(2, null, ct)));
    }

    // --- Annotations -----------------------------------------------------------------------------

    private static ChatConsistencyAnnotationRequest Annotation(string? sourceUrl = null, string text = "Model update announced") => new()
    {
        AtUtc = new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc),
        Provider = "OpenAI",
        ModelId = "gpt-test",
        Kind = ChatConsistencyAnnotationKind.ModelRelease,
        Text = text,
        SourceUrl = sourceUrl
    };

    [Fact]
    public async Task AnAnnotationIsRefusedForABadSourceAnOverLongTextOrAnUndefinedKind()
    {
        using var h = new Harness();
        var ct = CancellationToken.None;

        foreach (var url in new[] { "ftp://example.com/notes", "javascript:alert(1)", "/relative/path", "https://example.com/" + new string('a', 512) })
        {
            Assert.Contains("http or https", ErrorOf(await h.Controller.AddAnnotation(Annotation(url), ct)));
        }

        Assert.Contains("1,000", ErrorOf(await h.Controller.AddAnnotation(Annotation(text: new string('x', 1001)), ct)));
        Assert.Contains("needs text", ErrorOf(await h.Controller.AddAnnotation(Annotation(text: "  "), ct)));

        var provider = Annotation();
        provider.Provider = new string('p', 65);
        Assert.Contains("64", ErrorOf(await h.Controller.AddAnnotation(provider, ct)));

        var model = Annotation();
        model.ModelId = new string('m', 129);
        Assert.Contains("128", ErrorOf(await h.Controller.AddAnnotation(model, ct)));

        var kind = Annotation();
        kind.Kind = (ChatConsistencyAnnotationKind)0;
        Assert.Equal("Unknown annotation kind.", ErrorOf(await h.Controller.AddAnnotation(kind, ct)));

        Assert.Equal(AdminChatConsistencyController.BodyRequiredError, ErrorOf(await h.Controller.AddAnnotation(null, ct)));
        Assert.Empty(h.Db.ChatConsistencyAnnotations.ToList());
    }

    [Fact]
    public async Task AnAnnotationIsAddedListedAndDeleted()
    {
        using var h = new Harness();
        var ct = CancellationToken.None;

        var added = ValueOf<ChatConsistencyAnnotationView>(await h.Controller.AddAnnotation(Annotation("https://example.com/release-notes"), ct));
        Assert.Equal(ChatConsistencyAnnotationKind.ModelRelease, added.Kind);
        Assert.Equal("https://example.com/release-notes", added.SourceUrl);

        var all = ValueOf<IReadOnlyList<ChatConsistencyAnnotationView>>(await h.Controller.ListAnnotations(null, null, ct));
        Assert.Equal(added.Id, Assert.Single(all).Id);
        var forModel = ValueOf<IReadOnlyList<ChatConsistencyAnnotationView>>(await h.Controller.ListAnnotations("OpenAI", "gpt-test", ct));
        Assert.Equal(added.Id, Assert.Single(forModel).Id);
        Assert.Empty(ValueOf<IReadOnlyList<ChatConsistencyAnnotationView>>(await h.Controller.ListAnnotations("Google", "gemini-control", ct)));

        Assert.IsType<NoContentResult>(await h.Controller.DeleteAnnotation(added.Id, ct));
        Assert.IsType<NotFoundResult>(await h.Controller.DeleteAnnotation(added.Id, ct));
        Assert.Empty(h.Db.ChatConsistencyAnnotations.ToList());
    }

    [Fact]
    public void TheAnnotationRequestReadsTheKindByNameOrByNumber()
    {
        var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        var byName = JsonSerializer.Deserialize<ChatConsistencyAnnotationRequest>(
            "{\"atUtc\":\"2026-09-10T00:00:00Z\",\"kind\":\"providerStatement\",\"text\":\"Statement\"}", web);
        Assert.Equal(ChatConsistencyAnnotationKind.ProviderStatement, byName!.Kind);

        var byNumber = JsonSerializer.Deserialize<ChatConsistencyAnnotationRequest>(
            "{\"atUtc\":\"2026-09-10T00:00:00Z\",\"kind\":3,\"text\":\"Cause\"}", web);
        Assert.Equal(ChatConsistencyAnnotationKind.ProviderConfirmedCause, byNumber!.Kind);
    }

    // --- Report documents list -------------------------------------------------------------------

    [Fact]
    public async Task TheDocumentsListAcceptsTheChatConsistencyOriginAndSubjectKey()
    {
        using var h = new Harness();
        var ct = CancellationToken.None;
        var chat = Document(BenchmarkReportDocumentOrigin.ChatConsistencyReport, "chat-consistency:3", null);
        var pack = Document(BenchmarkReportDocumentOrigin.ReportPack, "run:5", null);
        h.Db.BenchmarkReportDocuments.AddRange(chat, pack);
        await h.Db.SaveChangesAsync(ct);

        var controller = new AdminBenchmarkReportDocumentsController(
            new BenchmarkReportRenderService(h.Db, TestChartStores.Unconfigured(), NullLogger<BenchmarkReportRenderService>.Instance));
        async Task<List<BenchmarkReportDocumentListItemDto>> ListAsync(string? origin = null, string? subject = null)
        {
            var ok = Assert.IsType<OkObjectResult>(await controller.List(null, null, null, ct, null, origin, subject));
            return Assert.IsType<List<BenchmarkReportDocumentListItemDto>>(ok.Value);
        }

        Assert.Equal(chat.Id, Assert.Single(await ListAsync(origin: "chatConsistencyReport")).Id);
        Assert.Equal(chat.Id, Assert.Single(await ListAsync(origin: "ChatConsistencyReport")).Id);
        Assert.Equal(chat.Id, Assert.Single(await ListAsync(subject: "chat-consistency:3")).Id);
        Assert.Equal(chat.Id, Assert.Single(await ListAsync(origin: "chatConsistencyReport", subject: "chat-consistency:3")).Id);
        Assert.Empty(await ListAsync(subject: "chat-consistency:4"));
        Assert.Empty(await ListAsync(origin: "reportPack", subject: "chat-consistency:3"));

        foreach (var malformed in new[] { "chat-consistency:", "chat-consistency:0", "chat-consistency:-3", "chat-consistency:x", "chat-consistency:99999999999", "Chat-Consistency:3" })
        {
            var bad = await controller.List(null, null, null, ct, null, null, malformed);
            Assert.Equal(AdminBenchmarkReportDocumentsController.SubjectError, ErrorOf(bad));
        }

        var badOrigin = await controller.List(null, null, null, ct, null, "chatConsistency");
        Assert.Equal(AdminBenchmarkReportDocumentsController.OriginError, ErrorOf(badOrigin));
    }

    // --- Report documents of an analysis --------------------------------------------------------

    /// <summary>The report endpoints answer in the run report-documents contract: default JSON, enums as numbers.</summary>
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static ServiceProvider RegradeScopes() => new ServiceCollection().BuildServiceProvider();

    private static string ReportErrorOf(IActionResult result, int statusCode)
    {
        var body = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(statusCode, body.StatusCode);
        return body.Value?.GetType().GetProperty("error")?.GetValue(body.Value) as string ?? string.Empty;
    }

    [Fact]
    public async Task TheReportEstimate_IsTheRunEstimate_WithTheProviderIssueReportAvailability()
    {
        await using var h = await ChatConsistencyReportHarness.CreateAsync();
        using var scopes = RegradeScopes();
        var controller = h.Controller(scopes.GetRequiredService<IServiceScopeFactory>());
        var ct = TestContext.Current.CancellationToken;

        var ok = Assert.IsType<OkObjectResult>(await controller.EstimateReports(ChatConsistencyReportHarness.AnalysisId, new BenchmarkRunReportEstimateRequest
        {
            WriterModelConfigurationId = h.Writer.Id,
            Audiences = new List<BenchmarkReportAudience> { BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportAudience.ProviderIssueReport }
        }, ct));
        var estimate = Assert.IsType<BenchmarkChatConsistencyReportEstimateDto>(ok.Value);
        Assert.True(estimate.ProviderIssueReportAvailable);
        Assert.Null(estimate.ProviderIssueReportReason);
        Assert.Equal(2, estimate.Estimates.Count);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, ok.Value!.GetType(), Web));
        var root = json.RootElement;
        Assert.True(root.GetProperty("providerIssueReportAvailable").GetBoolean());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("providerIssueReportReason").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("refusal").ValueKind);
        Assert.Equal(4, root.GetProperty("estimates")[1].GetProperty("audience").GetInt32());

        Assert.IsType<BadRequestObjectResult>(await controller.EstimateReports(ChatConsistencyReportHarness.AnalysisId, null, ct));
        Assert.IsType<NotFoundResult>(await controller.EstimateReports(99, new BenchmarkRunReportEstimateRequest { WriterModelConfigurationId = h.Writer.Id }, ct));
    }

    [Fact]
    public async Task WritingReports_Answers202_AndTheJobEndpointShowsTheFinishedJob()
    {
        await using var h = await ChatConsistencyReportHarness.CreateAsync();
        using var scopes = RegradeScopes();
        var controller = h.Controller(scopes.GetRequiredService<IServiceScopeFactory>());
        var ct = TestContext.Current.CancellationToken;
        h.Provider.Replies.Enqueue(ChatConsistencyReportHarness.Reply(ChatConsistencyReportTestData.ValidOutput(BenchmarkReportAudience.ExecutiveSummary)));

        Assert.IsType<NoContentResult>(await controller.GetReportJob(ChatConsistencyReportHarness.AnalysisId, ct));

        var accepted = Assert.IsType<AcceptedResult>(await controller.WriteReports(ChatConsistencyReportHarness.AnalysisId, new WriteRunReportDocumentsRequest
        {
            WriterModelConfigurationId = h.Writer.Id,
            Audiences = new List<BenchmarkReportAudience> { BenchmarkReportAudience.ExecutiveSummary }
        }, ct));
        await h.Service().ChatConsistencyJobCompletion(ChatConsistencyReportHarness.AnalysisId);

        var response = Assert.IsType<WriteRunReportDocumentsResponse>(accepted.Value);
        Assert.Equal(ChatConsistencyReportHarness.AnalysisId, response.RunId);
        Assert.Equal(BenchmarkRunReportDocumentsStatus.Pending, response.Status);
        Assert.Equal(new[] { BenchmarkReportAudience.ExecutiveSummary }, response.Audiences);

        var ok = Assert.IsType<OkObjectResult>(await controller.GetReportJob(ChatConsistencyReportHarness.AnalysisId, ct));
        var job = Assert.IsType<BenchmarkRunReportJobDto>(ok.Value);
        Assert.Equal(ChatConsistencyReportHarness.AnalysisId, job.RunId);
        Assert.Equal("Finished", job.Phase);
        Assert.Equal(BenchmarkRunReportDocumentsStatus.Completed, job.Status);
        var document = Assert.Single(await h.Db.BenchmarkReportDocuments.AsNoTracking().ToListAsync(ct));
        Assert.Equal(document.Id, Assert.Single(job.Job.Documents).DocumentId);
        Assert.Equal(ChatConsistencyReportHarness.UserId, Assert.Single(await h.Db.SystemAiUsageLogs.ToListAsync(ct)).AspNetUserId);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(job, Web));
        Assert.Equal((int)BenchmarkRunReportDocumentsStatus.Completed, json.RootElement.GetProperty("status").GetInt32());

        Assert.Equal(AdminChatConsistencyController.BodyRequiredError,
            ReportErrorOf(await controller.WriteReports(ChatConsistencyReportHarness.AnalysisId, null, ct), StatusCodes.Status400BadRequest));
        Assert.IsType<NotFoundResult>(await controller.GetReportJob(99, ct));
    }

    [Fact]
    public async Task WritingReports_WithAWriterOfTheModelsProvider_Answers409WithTheSameProviderWarning()
    {
        await using var h = await ChatConsistencyReportHarness.CreateAsync();
        using var scopes = RegradeScopes();
        var controller = h.Controller(scopes.GetRequiredService<IServiceScopeFactory>());
        var ct = TestContext.Current.CancellationToken;

        var conflict = Assert.IsType<ObjectResult>(await controller.WriteReports(ChatConsistencyReportHarness.AnalysisId, new WriteRunReportDocumentsRequest
        {
            WriterModelConfigurationId = h.SameProvider.Id
        }, ct));
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
        var warning = Assert.IsType<SameProviderWarningDto>(conflict.Value);
        Assert.Equal("reportWriter", warning.Role);

        Assert.Equal(BenchmarkRunReportDocumentService.ModelUnderTestMessage, ReportErrorOf(
            await controller.WriteReports(ChatConsistencyReportHarness.AnalysisId, new WriteRunReportDocumentsRequest { WriterModelConfigurationId = h.SubjectModel.Id }, ct),
            StatusCodes.Status400BadRequest));
        Assert.IsType<NoContentResult>(await controller.GetReportJob(ChatConsistencyReportHarness.AnalysisId, ct));
    }

    [Fact]
    public async Task CancelingReports_Answers409WithoutAJob_And202WithTheViewWhileQueued()
    {
        await using var h = await ChatConsistencyReportHarness.CreateAsync();
        using var scopes = RegradeScopes();
        var controller = h.Controller(scopes.GetRequiredService<IServiceScopeFactory>());
        var ct = TestContext.Current.CancellationToken;

        Assert.Equal(BenchmarkReportPackService.ChatConsistencyNothingInProgressMessage,
            ReportErrorOf(await controller.CancelReportJob(ChatConsistencyReportHarness.AnalysisId, ct), StatusCodes.Status409Conflict));
        Assert.IsType<NotFoundResult>(await controller.CancelReportJob(99, ct));

        var holder = h.HoldTheSlot();
        Assert.IsType<AcceptedResult>(await controller.WriteReports(ChatConsistencyReportHarness.AnalysisId, new WriteRunReportDocumentsRequest
        {
            WriterModelConfigurationId = h.Writer.Id,
            Audiences = new List<BenchmarkReportAudience> { BenchmarkReportAudience.ExecutiveSummary }
        }, ct));

        var accepted = Assert.IsType<AcceptedResult>(await controller.CancelReportJob(ChatConsistencyReportHarness.AnalysisId, ct));
        var view = Assert.IsType<BenchmarkRunReportJobDto>(accepted.Value);
        Assert.Equal(ChatConsistencyReportHarness.AnalysisId, view.RunId);
        Assert.NotNull(view.CancelRequestedAtUtc);

        await h.Service().ChatConsistencyJobCompletion(ChatConsistencyReportHarness.AnalysisId);
        holder.SetStatus(BenchmarkReportPackJobStatus.Completed);
        var finished = Assert.IsType<BenchmarkRunReportJobDto>(Assert.IsType<OkObjectResult>(
            await controller.GetReportJob(ChatConsistencyReportHarness.AnalysisId, ct)).Value);
        Assert.Equal(BenchmarkRunReportDocumentsStatus.Canceled, finished.Status);
        Assert.Equal(0, h.Provider.Calls);
    }

    [Fact]
    public async Task TheFreshnessEndpoint_AnswersWhetherTheAnalysisIsOutOfDate_And404ForAnUnknownOne()
    {
        await using var h = await ChatConsistencyReportHarness.CreateAsync(ChatConsistencyReportTestData.Result() with { AnalysisCodeVersion = 4 });
        using var scopes = RegradeScopes();
        var controller = h.Controller(scopes.GetRequiredService<IServiceScopeFactory>());
        var ct = TestContext.Current.CancellationToken;

        var result = Assert.IsType<JsonResult>(await controller.GetFreshness(ChatConsistencyReportHarness.AnalysisId, ct));
        var freshness = Assert.IsType<ChatConsistencyFreshness>(result.Value);
        Assert.True(freshness.OutOfDate);
        Assert.True(freshness.EarlierAnalysisCode);
        Assert.Equal(4, freshness.AnalysisCodeVersion);
        Assert.Null(freshness.InputsChanged);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result.Value, ChatConsistencyJson.Options));
        Assert.True(json.RootElement.GetProperty("outOfDate").GetBoolean());
        Assert.Equal(ChatConsistencyAnalysisService.CurrentAnalysisCodeVersion, json.RootElement.GetProperty("currentAnalysisCodeVersion").GetInt32());

        Assert.IsType<NotFoundResult>(await controller.GetFreshness(99, ct));
    }

    [Fact]
    public async Task WritingReportsFromAnOutOfDateAnalysis_Answers409WithOutOfDate_UntilAcknowledged()
    {
        await using var h = await ChatConsistencyReportHarness.CreateAsync(ChatConsistencyReportTestData.Result() with { AnalysisCodeVersion = 4 });
        using var scopes = RegradeScopes();
        var controller = h.Controller(scopes.GetRequiredService<IServiceScopeFactory>());
        var ct = TestContext.Current.CancellationToken;
        var request = new WriteRunReportDocumentsRequest
        {
            WriterModelConfigurationId = h.Writer.Id,
            Audiences = new List<BenchmarkReportAudience> { BenchmarkReportAudience.ExecutiveSummary }
        };

        var refused = Assert.IsType<ObjectResult>(await controller.WriteReports(ChatConsistencyReportHarness.AnalysisId, request, ct));
        Assert.Equal(StatusCodes.Status409Conflict, refused.StatusCode);
        using (var json = JsonDocument.Parse(JsonSerializer.Serialize(refused.Value, refused.Value!.GetType(), Web)))
        {
            Assert.True(json.RootElement.GetProperty("outOfDate").GetBoolean());
            Assert.StartsWith("Analysis #7 is out of date: ", json.RootElement.GetProperty("error").GetString());
        }
        Assert.Equal(0, h.Provider.Calls);

        h.Provider.Replies.Enqueue(ChatConsistencyReportHarness.Reply(ChatConsistencyReportTestData.ValidOutput(BenchmarkReportAudience.ExecutiveSummary)));
        request.AcknowledgeOutOfDate = true;
        Assert.IsType<AcceptedResult>(await controller.WriteReports(ChatConsistencyReportHarness.AnalysisId, request, ct));
        await h.Service().ChatConsistencyJobCompletion(ChatConsistencyReportHarness.AnalysisId);
        Assert.Single(await h.Db.BenchmarkReportDocuments.AsNoTracking().ToListAsync(ct));
    }
}
