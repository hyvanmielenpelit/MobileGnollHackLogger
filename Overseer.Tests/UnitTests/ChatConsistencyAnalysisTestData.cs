namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
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
/// The seeds, requests and factories of the chat consistency analysis tests: one subject in two periods with
/// call telemetry and a control subject, the subject's battery runs of two suites, and the re-grade service.
/// Every test opens its own in-memory database.
/// </summary>
internal static class ChatConsistencyAnalysisTestData
{
    public const string PromptSha = "e9b3e9a7c4d1b8f0a2e6c9d3b7f1a4e8c2d6b0f9a3e7c1d5b9f3a7e1c5d9b3f7";
    public const string GuidesBefore = "f59d8b30a1c7e4d2b6f0a8c3e9d5b1f7a3c9e5d1b7f3a9c5e1d7b3f9a5c1e7d3";
    public const string GuidesAfter = "0a1b2c3d4e5f60718293a4b5c6d7e8f90a1b2c3d4e5f60718293a4b5c6d7e8f9";
    public const string KnowledgeSha = "576ca574b2e8d0f6a4c2e8d4b0f6a2c8";
    public const string PricingJson = "{\"candidate\":{\"inputPerMillion\":1.0,\"outputPerMillion\":4.0}}";
    public const int QuestionCount = 24;

    /// <summary>
    /// The bootstrap replicates of <see cref="Service"/>'s analyses. At 1,000 the smallest bootstrap p is 2/1,001,
    /// which Holm over six endpoints keeps below 0.05.
    /// </summary>
    public const int TestBootstrapReplicates = 1_000;

    // Tuesday 2026-09-01 and Wednesday 2026-09-02; Tuesday 2026-09-15 and Wednesday 2026-09-16.
    public static readonly DateTime BaselineDay1 = new(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc);
    public static readonly DateTime BaselineDay2 = new(2026, 9, 2, 9, 30, 0, DateTimeKind.Utc);
    public static readonly DateTime ComparisonDay1 = new(2026, 9, 15, 9, 0, 0, DateTimeKind.Utc);
    public static readonly DateTime ComparisonDay2 = new(2026, 9, 16, 9, 30, 0, DateTimeKind.Utc);

    internal sealed record Scenario
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

    public static DbContextOptions<ApplicationDbContext> NewOptions(string name, InMemoryDatabaseRoot root)
        => new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(name, root).Options;

    public static ApplicationDbContext NewDb() => new(NewOptions(Guid.NewGuid().ToString(), new InMemoryDatabaseRoot()));

    /// <summary>The analysis service over <paramref name="db"/>, computing with <see cref="TestBootstrapReplicates"/>.</summary>
    public static ChatConsistencyAnalysisService Service(ApplicationDbContext db)
        => new(db, new ChatConsistencyEvidenceBuilder(db), NullLogger<ChatConsistencyAnalysisService>.Instance, TestBootstrapReplicates);

    /// <summary>The analysis service over <paramref name="db"/> as production builds it, computing with the protocol's replicates.</summary>
    public static ChatConsistencyAnalysisService ProductionService(ApplicationDbContext db)
        => new(db, new ChatConsistencyEvidenceBuilder(db), NullLogger<ChatConsistencyAnalysisService>.Instance);

    public static BenchmarkRun Run(
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
    public static string Seed(ApplicationDbContext db, Scenario scenario)
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

    /// <summary>
    /// Seeds <paramref name="scenario"/> into a database of its own, analyzes <see cref="Request"/> with
    /// <paramref name="replicates"/> bootstrap replicates (the protocol's when null) and returns the result. For a
    /// result several tests share: they read it and never change it.
    /// </summary>
    public static async Task<ChatConsistencyAnalysisResult> AnalyzeOnceAsync(Scenario scenario, int? replicates)
    {
        using var db = NewDb();
        string key = Seed(db, scenario);
        var service = replicates is int count
            ? new ChatConsistencyAnalysisService(db, new ChatConsistencyEvidenceBuilder(db), NullLogger<ChatConsistencyAnalysisService>.Instance, count)
            : ProductionService(db);
        // Not the first reader's test token: canceling that test must not fail the result the others share.
        return await service.AnalyzeAsync(Request(key), CancellationToken.None);
    }

    /// <summary>
    /// Saves <paramref name="result"/> as an analysis row of <paramref name="db"/> with <paramref name="removed"/> taken
    /// out of its stored JSON, as an analysis saved before those properties existed; returns the row's id.
    /// </summary>
    public static int StoreResult(ApplicationDbContext db, ChatConsistencyAnalysisResult result, params string[] removed)
    {
        var json = JsonNode.Parse(JsonSerializer.Serialize(result, ChatConsistencyJson.Options))!.AsObject();
        foreach (string name in removed)
        {
            Assert.True(json.Remove(name), name);
        }

        var row = new ChatConsistencyAnalysis
        {
            Name = result.Name,
            SubjectModelKey = result.Subject.Key,
            BaselineStartUtc = result.Baseline.StartUtc,
            BaselineEndUtc = result.Baseline.EndUtc,
            ComparisonStartUtc = result.Comparison.StartUtc,
            ComparisonEndUtc = result.Comparison.EndUtc,
            TargetRunIdsJson = JsonSerializer.Serialize(result.Baseline.RunIds.Concat(result.Comparison.RunIds).OrderBy(i => i).ToList()),
            ControlRunIdsJson = JsonSerializer.Serialize(result.Controls.ControlRunIds),
            ProtocolVersion = ChatConsistencyProtocol.V1Version,
            ProtocolJson = ChatConsistencyProtocol.V1.ToJson(),
            ResultJson = json.ToJsonString(),
            InputSha256 = result.InputSha256,
            AnalysisCodeVersion = result.AnalysisCodeVersion,
            CreatedAtUtc = result.Comparison.EndUtc.AddDays(1)
        };
        db.ChatConsistencyAnalyses.Add(row);
        db.SaveChanges();
        return row.Id;
    }

    /// <summary>The September request under Protocol V1, whose five endpoints most tests pin; the Protocol V2 tests set their own version.</summary>
    public static ChatConsistencyAnalysisRequest Request(string subjectKey) => new()
    {
        Name = "GPT test, September",
        ProtocolVersion = ChatConsistencyProtocol.V1Version,
        SubjectModelKey = subjectKey,
        BaselineStartUtc = new DateTime(2026, 8, 31, 0, 0, 0, DateTimeKind.Utc),
        BaselineEndUtc = new DateTime(2026, 9, 7, 0, 0, 0, DateTimeKind.Utc),
        ComparisonStartUtc = new DateTime(2026, 9, 14, 0, 0, 0, DateTimeKind.Utc),
        ComparisonEndUtc = new DateTime(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc)
    };

    /// <summary>The request with runs #1–#2 as the baseline and #3–#4 as the comparison, and <paramref name="selection"/>.</summary>
    public static ChatConsistencyAnalysisRequest ExplicitRequest(string subjectKey, ChatConsistencyRunSelection? selection) => Request(subjectKey) with
    {
        BaselineRunIds = new long[] { 1, 2 },
        ComparisonRunIds = new long[] { 3, 4 },
        RunSelection = selection
    };

    /// <summary>A step-1 selection: September, from run #1 to run #4, with <paramref name="leftOut"/> unchecked.</summary>
    public static ChatConsistencyRunSelection Selection(params long[] leftOut) => new()
    {
        RangeLabel = "2026-09-01 to 2026-09-30",
        RangeFromUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
        RangeToUtc = new DateTime(2026, 9, 30, 23, 59, 59, DateTimeKind.Utc),
        FirstRunId = 1,
        LastRunId = 4,
        LeftOutRunIds = leftOut
    };

    public const string RunSelectionLimitationStart = "The operator chose the runs: ";

    /// <summary>The benchmark-only configurations <see cref="MakeNonProduction"/> applies.</summary>
    public const string InformationConditionKind = "informationCondition";
    public const string CorpusConditionKind = "corpusCondition";
    public const string InstructionVariantKind = "instructionVariant";

    /// <summary>
    /// Puts <paramref name="run"/> under the benchmark-only configuration <paramref name="kind"/> names: the
    /// source-only information condition, a scarce corpus or an instruction variant. Returns the reason
    /// <see cref="ChatConsistencyComparability.NonProductionReason"/> gives for it.
    /// </summary>
    public static string MakeNonProduction(BenchmarkRun run, string kind)
    {
        switch (kind)
        {
            case InformationConditionKind:
                run.CandidatePromptOptionsJson = "{\"verboseMode\":false,\"informationCondition\":\"sourceOnly\"}";
                return "the information condition Source code only — no wiki, knowledge base or lookup tools";
            case CorpusConditionKind:
                run.CorpusCondition = "scarce:Run 125 scrub";
                return "the corpus condition scarce — Run 125 scrub";
            case InstructionVariantKind:
                run.InstructionVariant = "source-tools-v1:0123456789ab";
                return "the instruction variant source-tools-v1:0123456789ab";
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown benchmark-only configuration.");
        }
    }

    public static ChatConsistencyEndpointResult EndpointOf(ChatConsistencyAnalysisResult result, string id)
        => result.Endpoints.Single(e => e.Id == id);

    // --- Battery and suite sets ------------------------------------------------------------------

    public const string SecondSuite = "Second Suite";
    public const string BatteryName = "Two initial suites";

    public static BenchmarkBatteryDefinition TwoSuites() => new(
        1, BatteryName, 1, BenchmarkBatteryWeightingScheme.Equal,
        new[] { new BenchmarkBatteryDefinitionSuite(0, 7, "Core Suite", null), new BenchmarkBatteryDefinitionSuite(1, 8, SecondSuite, null) });

    public static string BatteryKey(BenchmarkBatteryDefinition definition) => ChatConsistencyComparisonSetKinds.BatteryKeyPrefix + definition.DefinitionSha256;

    public static ChatConsistencyComparisonSetRef BatterySet() => new() { Kind = ChatConsistencyComparisonSetKinds.Battery, Key = BatteryKey(TwoSuites()) };

    public static ChatConsistencyComparisonSetRef CoreSuiteSet() => new() { Kind = ChatConsistencyComparisonSetKinds.Suite, Key = "suite:id:7" };

    /// <summary>The run moved to Second Suite (id 8), its questions numbered from 101 so its items never pair with Core Suite's.</summary>
    public static BenchmarkRun OnSecondSuite(BenchmarkRun run)
    {
        run.BenchmarkSuiteIdUsed = 8;
        run.SuiteName = SecondSuite;
        foreach (var answer in run.Answers) answer.BenchmarkQuestionIdUsed += 100;
        return run;
    }

    public static BenchmarkBatteryRun BatteryRun(long id, DateTime start, BenchmarkBatteryDefinition definition, params (long RunId, int SuiteIndex)[] members)
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
    public static string SeedBatteries(ApplicationDbContext db, bool extras = false, bool firstRunAtOtherScoring = false)
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

    public static Task<ChatConsistencyEvidence> Evidence(ApplicationDbContext db, ChatConsistencyAnalysisRequest request)
        => new ChatConsistencyEvidenceBuilder(db).LoadAsync(request, TestContext.Current.CancellationToken);

    public static Task<ChatConsistencyTimeline> Timeline(ApplicationDbContext db, string key, DateTime? fromUtc = null, DateTime? toUtc = null)
        => new ChatConsistencyEvidenceBuilder(db).GetTimelineAsync(key, fromUtc, toUtc, TestContext.Current.CancellationToken);

    /// <summary>A stored, complete battery analysis of <paramref name="batteryRunId"/> over <paramref name="memberRunIds"/>.</summary>
    public static BenchmarkBatteryAnalysis BatteryAnalysis(long id, long batteryRunId, DateTime computedAtUtc, double overallIndex, params long[] memberRunIds) => new()
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
    public static BenchmarkAssessorCalibration Calibration(long id, long runId, long snapshotId, DateTime createdAtUtc, params double[] qualities) => new()
    {
        Id = id,
        BenchmarkRunId = runId,
        AssessorModelSnapshotId = snapshotId,
        CreatedAtUtc = createdAtUtc,
        AnswerCount = qualities.Length,
        VerdictsJson = JsonSerializer.Serialize(qualities.Select((q, i) => new { orderIndex = i, calibrationQualityScore = q }))
    };

    // --- Re-grade --------------------------------------------------------------------------------

    public static (ChatConsistencyRegradeService Service, ApplicationDbContext Db, ServiceProvider Provider, ChatConsistencyRegradeJobManager Jobs) Regrade(
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
}
