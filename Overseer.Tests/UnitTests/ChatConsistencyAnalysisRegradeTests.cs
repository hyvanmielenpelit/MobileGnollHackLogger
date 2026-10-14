namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MobileGnollHackLogger.Data;
using Overseer.Services.Benchmarking;
using Overseer.Services.ChatConsistency;
using Overseer.Tests.Helpers;
using Xunit;
using static Overseer.Tests.UnitTests.ChatConsistencyAnalysisTestData;

/// <summary>
/// The chat consistency re-grade's refusals and job; battery units: the paired floor, the missing controls,
/// the Overall Index and its interval; and provider availability (P6) under Protocol V2, in the analysis,
/// its freshness and the timeline. No network: the re-grade runs a fake calibration runner.
/// </summary>
public class ChatConsistencyAnalysisRegradeTests
{
    // --- Re-grade --------------------------------------------------------------------------------

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

    // --- Provider availability (P6) and Protocol V2 ----------------------------------------------

    /// <summary>
    /// Records the provider attempt ledger for the subject's runs #1–#4: one candidate attempt per answer at its
    /// start, <c>Ok</c>, and in the comparison period a <c>ProviderOverloaded</c> attempt before each of the first
    /// <paramref name="comparisonErrorsPerRun"/> answers. With <paramref name="failedRunAt"/>, adds a failed run #20
    /// of the subject there with one recorded <c>ProviderOverloaded</c> attempt.
    /// </summary>
    private static void RecordLedger(ApplicationDbContext db, int comparisonErrorsPerRun, params (long Id, DateTime Start)[] failedRunAt)
    {
        long attemptId = 1;
        BenchmarkProviderAttempt Attempt(long runId, long answerId, DateTime start, ProviderAttemptOutcome outcome, byte index) => new()
        {
            Id = attemptId++,
            BenchmarkRunId = runId,
            BenchmarkRunAnswerId = answerId,
            Source = ModelCallSource.BenchmarkCandidate,
            Provider = "OpenAI",
            RequestedModelId = "gpt-test",
            StartedAtUtc = start,
            DurationMs = 100,
            Outcome = outcome,
            AttemptIndex = index,
            HttpStatus = outcome == ProviderAttemptOutcome.Ok ? (short)200 : (short)503,
            Retried = outcome != ProviderAttemptOutcome.Ok
        };

        foreach (var run in db.BenchmarkRuns.Local.Where(r => r.Id is >= 1 and <= 4).ToList())
        {
            run.ProviderLedgerVersion = 1;
            int errors = 0;
            foreach (var answer in run.Answers.OrderBy(a => a.Id))
            {
                DateTime start = answer.StartedAtUtc ?? run.StartedAtUtc;
                byte index = 0;
                if (run.Id >= 3 && errors < comparisonErrorsPerRun)
                {
                    db.BenchmarkProviderAttempts.Add(Attempt(run.Id, answer.Id, start, ProviderAttemptOutcome.ProviderOverloaded, index++));
                    errors++;
                }

                db.BenchmarkProviderAttempts.Add(Attempt(run.Id, answer.Id, start.AddSeconds(1), ProviderAttemptOutcome.Ok, index));
            }

            run.CandidateValidAttemptCount = run.Answers.Count + errors;
            run.CandidateProviderErrorCount = errors;
            run.CandidateUnattributedErrorCount = 0;
            run.CandidateOurSideFailureCount = 0;
        }

        var template = db.BenchmarkRuns.Local.Single(r => r.Id == 3);
        foreach (var (id, start) in failedRunAt)
        {
            var failed = Run(id, start, template.TestedModelSnapshot!, template.AssessorModelSnapshot!, template.ToolGuidesSha256!, _ => 80, false, 1.0, "gpt-test-2026-09-01");
            failed.Status = BenchmarkRunStatus.Failed;
            failed.ProviderLedgerVersion = 1;
            failed.CandidateValidAttemptCount = 1;
            failed.CandidateProviderErrorCount = 1;
            failed.CandidateUnattributedErrorCount = 0;
            failed.CandidateOurSideFailureCount = 0;
            db.BenchmarkRuns.Add(failed);
            db.BenchmarkProviderAttempts.Add(Attempt(id, id * 100 + 1, start, ProviderAttemptOutcome.ProviderOverloaded, 0));
        }

        db.SaveChanges();
    }

    private static ChatConsistencyAnalysisRequest V2Request(string key) => Request(key) with { ProtocolVersion = ChatConsistencyProtocol.V2Version };

    [Fact]
    public async Task AV1Analysis_NeverReadsProviderAvailability_EvenOnRecordedRuns()
    {
        using var db = NewDb();
        string key = Seed(db, new Scenario { QualityDrop = true });
        RecordLedger(db, 12, (20, ComparisonDay1.AddHours(6)));

        var result = await Service(db).AnalyzeAsync(Request(key), TestContext.Current.CancellationToken);

        Assert.Equal(ChatConsistencyProtocol.V1Version, result.Protocol.ProtocolVersion);
        Assert.Equal(new[] { "P1", "P2", "P3", "P4", "P5" }, result.Endpoints.Select(e => e.Id));
        Assert.All(result.Endpoints, e => Assert.Null(e.Availability));
        Assert.DoesNotContain(result.RunSelection.UnanalyzedRuns, u => u.Reason == ChatConsistencyUnanalyzedReasons.AvailabilityOnly);
        Assert.All(result.PeriodLevels!, l => Assert.Null(l.ValidAttemptCount));
        Assert.Contains(result.Reliability, r => r.Id == "http429");
        Assert.DoesNotContain(result.Reliability, r => r.Id == "providerOverloaded");
        Assert.DoesNotContain("availability", result.Headline);
        Assert.DoesNotContain(result.Attribution.Attributions, a => a.Rule == ChatConsistencyAttribution.RuleProviderAvailability);
    }

    [Fact]
    public async Task AV2Analysis_ComparesTheProviderErrorRate_InPercentagePoints_WithAvailabilityOnlyRuns()
    {
        using var db = NewDb();
        string key = Seed(db, new Scenario { QualityDrop = true });

        // Run #20 failed between the comparison's first and last runs; #21 failed after its last run.
        RecordLedger(db, 12, (20, ComparisonDay1.AddHours(6)), (21, ComparisonDay2.AddHours(6)));

        var result = await Service(db).AnalyzeAsync(V2Request(key), TestContext.Current.CancellationToken);

        var p6 = EndpointOf(result, ChatConsistencyEndpointIds.ProviderErrorRate);
        Assert.True(p6.Computed, p6.NotComputedReason);
        Assert.Equal("pp", p6.Unit);
        Assert.Equal(1.0, p6.Margin);
        Assert.Equal("lowerIsBetter", p6.Direction);
        Assert.True(p6.Estimate > 30.0, "estimate " + p6.Estimate);
        Assert.Equal(ChatConsistencyAnalysisService.MoreProviderErrors, p6.VerdictLabel);
        Assert.Equal(ChatConsistencyEvidenceGrade.Indicated, p6.Grade);
        Assert.Equal(new[] { "Weekday 08–12 UTC" }, p6.StrataUsed);
        Assert.Contains(20L, p6.ComparisonRunIds);
        Assert.DoesNotContain(21L, p6.ComparisonRunIds);
        Assert.NotNull(p6.Availability);
        Assert.Equal(48, p6.Availability!.BaselineValidAttempts);
        Assert.Equal(0, p6.Availability.BaselineProviderErrors);
        Assert.Equal(24 + 1, p6.Availability.ComparisonProviderErrors);
        Assert.Equal(new long[] { 20 }, p6.Availability.ComparisonAvailabilityOnlyRunIds);
        Assert.Equal(30, p6.Availability.MinimumAttemptsPerStratum);

        // The checks P6 skips, and the one it states its own way.
        Assert.DoesNotContain(p6.RobustnessChecks, c => c.Name == "Served-tier match");
        var ownSide = Assert.Single(p6.RobustnessChecks, c => c.Name == "No unexamined own-side explanation");
        Assert.DoesNotContain("parallel", ownSide.Detail);
        Assert.DoesNotContain("no answer was retried", ownSide.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("left out of the denominator", ownSide.Detail);

        // Holm runs across six endpoints, the run selection lists the availability-only run, and the headline states availability.
        Assert.Equal(6, result.Endpoints.Count);
        var availabilityOnly = Assert.Single(result.RunSelection.UnanalyzedRuns, u => u.Reason == ChatConsistencyUnanalyzedReasons.AvailabilityOnly);
        Assert.Equal((20L, "comparison"), (availabilityOnly.RunId, availabilityOnly.Period));
        Assert.DoesNotContain(result.Limitations, l => l.StartsWith(RunSelectionLimitationStart, StringComparison.Ordinal));
        Assert.Contains(result.DataQuality, n => n.Kind == "availabilityOnly" && n.Text.Contains("#20", StringComparison.Ordinal));
        Assert.Contains("; availability: more provider errors (indicated) within weekdays 08–12 UTC", result.Headline);

        // The V2 reliability family: attributed attempt rates, no 429 or 5xx call rates.
        Assert.DoesNotContain(result.Reliability, r => r.Id is "http429" or "http5xx");
        var overloaded = Assert.Single(result.Reliability, r => r.Id == "providerOverloaded");
        Assert.Equal("valid attempts", overloaded.Denominator);
        Assert.NotNull(overloaded.Method);
        Assert.Contains(result.Reliability, r => r.Id == "providerRerunUnits");
        Assert.DoesNotContain(result.HeadlineReliabilityIncreases, s => s.StartsWith("Provider overloaded", StringComparison.Ordinal));

        // The levels describe each period with its availability.
        var comparison = result.PeriodLevels![1];
        Assert.True(comparison.AvailabilityRecorded);
        Assert.Equal(48 + 24 + 1, comparison.ValidAttemptCount);
        Assert.Equal(1, comparison.AvailabilityOnlyRunCount);
        Assert.Equal(0, result.PeriodLevels[0].ProviderErrorCount);
    }

    [Fact]
    public async Task AV2AnalysisOnUnrecordedRuns_LeavesP6NotComputed_AsNotRecorded()
    {
        using var db = NewDb();
        string key = Seed(db, new Scenario { QualityDrop = true });

        var result = await Service(db).AnalyzeAsync(V2Request(key), TestContext.Current.CancellationToken);

        var p6 = EndpointOf(result, ChatConsistencyEndpointIds.ProviderErrorRate);
        Assert.False(p6.Computed);
        Assert.Equal(ChatConsistencyNotComputedKinds.NotRecorded, p6.NotComputedKind);
        Assert.Equal("not computable", p6.VerdictLabel);
        Assert.Contains("; availability: not computable", result.Headline);
        Assert.True(EndpointOf(result, ChatConsistencyEndpointIds.Quality).Computed);
    }

    [Fact]
    public async Task AV2AnalysisWithFewAttemptsPerStratum_HasNoCommonStratum_AndAsksForAvailabilityRuns()
    {
        using var db = NewDb();
        string key = Seed(db, new Scenario { QualityDrop = true });
        RecordLedger(db, 0);
        var protocol = ChatConsistencyProtocol.V2 with { MinimumAttemptsPerStratum = 500 };
        var request = V2Request(key);
        var evidence = await Evidence(db, request);

        var result = ChatConsistencyAnalysisService.Compute(request, protocol, evidence, TestBootstrapReplicates);

        var p6 = EndpointOf(result, ChatConsistencyEndpointIds.ProviderErrorRate);
        Assert.Equal(ChatConsistencyNotComputedKinds.NoCommonStratum, p6.NotComputedKind);
        Assert.Contains("500 valid attempts", p6.NotComputedReason);
        var next = Assert.Single(result.NextRuns, n => n.EndpointId == ChatConsistencyEndpointIds.ProviderErrorRate);
        Assert.Equal("stratum", next.Kind);
        Assert.Contains("provider error rates cannot be compared", next.Reason);
        Assert.Contains("; availability: not computable; the periods' attempts share no common time stratum", result.Headline);
    }

    [Fact]
    public async Task AV2Analysis_IsCurrentUnderItsOwnProtocol_AndALedgerChangeMakesItOutOfDate()
    {
        using var db = NewDb();
        string key = Seed(db, new Scenario { QualityDrop = true });
        RecordLedger(db, 12);
        var service = Service(db);
        var result = await service.AnalyzeAsync(V2Request(key), TestContext.Current.CancellationToken);

        var fresh = await service.CheckFreshnessAsync(result.AnalysisId!.Value, TestContext.Current.CancellationToken);
        Assert.False(fresh!.InputsChanged);
        Assert.False(fresh.OutOfDate);

        var run = db.BenchmarkRuns.Single(r => r.Id == 3);
        run.CandidateProviderErrorCount = 13;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var changed = await service.CheckFreshnessAsync(result.AnalysisId!.Value, TestContext.Current.CancellationToken);
        Assert.True(changed!.InputsChanged);
    }

    [Fact]
    public async Task TheTimeline_CarriesAvailability_AndAFailedRunAsAnAvailabilityOnlyPoint()
    {
        using var db = NewDb();
        string key = Seed(db, new Scenario());
        RecordLedger(db, 12, (20, ComparisonDay1.AddHours(6)));

        var timeline = await Timeline(db, key);

        var failed = Assert.Single(timeline.Points, p => p.AvailabilityOnly);
        Assert.Equal(20L, failed.RunId);
        Assert.Equal(BenchmarkRunStatus.Failed, failed.Status);
        Assert.True(failed.AvailabilityRecorded);
        Assert.Equal(1, failed.ProviderErrorCount);
        Assert.Null(failed.NativeMeanQuality);

        var comparison = timeline.Points.Single(p => p.RunId == 3);
        Assert.True(comparison.AvailabilityRecorded);
        Assert.Equal(36, comparison.ValidAttemptCount);
        Assert.Equal(12, comparison.ProviderErrorCount);
        Assert.Equal(12.0 / 36.0, comparison.ProviderErrorRate!.Value, 9);
        Assert.NotNull(comparison.ProviderErrorRateHigh);
        Assert.Equal(0, comparison.RerunsNeeded);
        Assert.Equal(new long[] { 1, 2, 3, 20, 4 }, timeline.Points.Select(p => p.RunId));

        var rows = await new ChatConsistencyEvidenceBuilder(db).GetRunTableAsync(key, null, null, TestContext.Current.CancellationToken);
        var row = Assert.Single(rows, r => r.AvailabilityOnly);
        Assert.Equal(20L, row.RunId);
        Assert.All(row.Eligibility.Where(e => e.Axis != ChatConsistencyAxis.Availability), e => Assert.False(e.Eligible));
        Assert.True(row.Eligibility.Single(e => e.Axis == ChatConsistencyAxis.Availability).Eligible);
        Assert.Equal(12.0 / 36.0, rows.Single(r => r.RunId == 3).ProviderErrorRate.GetValueOrDefault(), 9);

        var axis = Assert.Single(await new ChatConsistencyEvidenceBuilder(db).ListModelAxesAsync(TestContext.Current.CancellationToken), a => a.Key == key);
        Assert.Equal(1, axis.AvailabilityOnlyRunCount);
        Assert.Equal(4, axis.RunCount);
    }
}
