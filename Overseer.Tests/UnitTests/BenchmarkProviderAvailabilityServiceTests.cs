namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services.Benchmarking;
using Xunit;

/// <summary>
/// The provider availability loader: a run pooled with its lineage predecessors, a set of runs, a
/// battery run with a superseded member, and card figures read from the rollup columns alone.
/// </summary>
public class BenchmarkProviderAvailabilityServiceTests
{
    private static readonly DateTime Start = new(2026, 10, 12, 9, 0, 0, DateTimeKind.Utc);

    private static BenchmarkRun RecordedRun(long id, long? replaces = null, BenchmarkReplacementCause? cause = null,
        BenchmarkRunStatus status = BenchmarkRunStatus.Completed)
    {
        var run = BenchmarkBatteryTestData.SuiteARun(id);
        run.Status = status;
        run.ProviderLedgerVersion = 1;
        run.ReplacesRunId = replaces;
        run.ReplacementCause = cause;
        return run;
    }

    private static BenchmarkProviderAttempt Attempt(long runId, int callIndex, ProviderAttemptOutcome outcome,
        int minute = 0, short? status = null, ModelCallSource source = ModelCallSource.BenchmarkCandidate, byte attemptIndex = 0)
        => new()
        {
            BenchmarkRunId = runId,
            BenchmarkRunAnswerId = runId * 100 + 1,
            Source = source,
            Provider = "OpenAI",
            RequestedModelId = "gpt-5.6-luna",
            CallIndex = callIndex,
            AttemptIndex = attemptIndex,
            StartedAtUtc = Start.AddMinutes(minute),
            DurationMs = 1000,
            Outcome = outcome,
            HttpStatus = status
        };

    /// <summary>
    /// <paramref name="ok"/> successful and <paramref name="overloaded"/> provider-overloaded (HTTP 529)
    /// candidate attempts for one run, each its own call.
    /// </summary>
    private static IEnumerable<BenchmarkProviderAttempt> Attempts(long runId, int ok, int overloaded)
    {
        int call = 0;
        for (int i = 0; i < ok; i++) yield return Attempt(runId, call++, ProviderAttemptOutcome.Ok, minute: i % 7, status: 200);
        for (int i = 0; i < overloaded; i++) yield return Attempt(runId, call++, ProviderAttemptOutcome.ProviderOverloaded, minute: i % 7, status: 529);
    }

    // --- One run and its lineage -----------------------------------------------------------------------

    [Fact]
    public async Task ForRun_PoolsTheReplacedFailedPredecessor_AndLinksIt()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = BenchmarkBatteryTestData.NewDb();

        var failed = RecordedRun(1, status: BenchmarkRunStatus.Failed);
        failed.Answers[0].Status = BenchmarkAnswerStatus.ProviderError;
        failed.Answers[0].FailureOrigin = BenchmarkFailureOrigin.Provider;
        db.BenchmarkRuns.Add(failed);
        db.BenchmarkRuns.Add(RecordedRun(2, replaces: 1, cause: BenchmarkReplacementCause.ProviderFailure));
        db.BenchmarkProviderAttempts.AddRange(Attempts(1, ok: 7, overloaded: 3));
        db.BenchmarkProviderAttempts.AddRange(Attempts(2, ok: 19, overloaded: 1));
        db.BenchmarkProviderAttempts.Add(Attempt(2, 0, ProviderAttemptOutcome.Ok, source: ModelCallSource.BenchmarkGrader));
        await db.SaveChangesAsync(ct);

        var pool = await new BenchmarkProviderAvailabilityService(db).ForRunAsync(2, ct);
        var s = pool.Summary;

        Assert.True(s.Recorded);
        Assert.Equal(2, s.TotalRuns);
        Assert.Equal(2, s.RecordedRuns);
        Assert.Equal(1, s.ReplacedRunsIncluded);
        Assert.Equal(30, s.ValidAttempts);
        Assert.Equal(4, s.ProviderErrors);
        Assert.Equal(1, s.QuestionsLost);
        Assert.Equal(1, s.ReplacementRuns);
        Assert.Equal(1, s.ProviderReplacementRuns);
        Assert.Equal(1, s.GraderValidAttempts);

        var link = Assert.Single(pool.Replaces);
        Assert.Equal(1, link.RunId);
        Assert.Equal(2, link.ReplacedByRunId);
        Assert.Equal(BenchmarkReplacementCause.ProviderFailure, link.Cause);
        Assert.Equal(new[] { 529 }, pool.HttpStatusesByOutcome[ProviderAttemptOutcome.ProviderOverloaded].ToArray());

        var dto = BenchmarkProviderAvailabilityService.ToDto(pool);
        Assert.Equal("ProviderFailure", Assert.Single(dto.Replaces).Cause);
        Assert.Equal(Enum.GetValues<ProviderAttemptOutcome>().Length, dto.ByOutcome.Count);
        var overloaded = dto.ByOutcome.Single(o => o.Outcome == "ProviderOverloaded");
        Assert.Equal(10, overloaded.Code);
        Assert.Equal("provider", overloaded.Side);
        Assert.Equal(4, overloaded.Count);
    }

    [Fact]
    public async Task ForRun_EndsOnACycle_AndOnADeletedPredecessor()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = BenchmarkBatteryTestData.NewDb();

        db.BenchmarkRuns.Add(RecordedRun(3, replaces: 4, cause: BenchmarkReplacementCause.OurFailure));
        db.BenchmarkRuns.Add(RecordedRun(4, replaces: 3, cause: BenchmarkReplacementCause.OurFailure));
        db.BenchmarkRuns.Add(RecordedRun(5, replaces: 999, cause: BenchmarkReplacementCause.ProviderFailure));
        await db.SaveChangesAsync(ct);

        var service = new BenchmarkProviderAvailabilityService(db);

        var cycle = await service.ForRunAsync(3, ct);
        Assert.Equal(2, cycle.Summary.TotalRuns);

        var orphan = await service.ForRunAsync(5, ct);
        Assert.Equal(1, orphan.Summary.TotalRuns);
        Assert.Empty(orphan.Replaces);
        Assert.Equal(0, orphan.Summary.ReplacementRuns);
    }

    // --- A set of runs ---------------------------------------------------------------------------------

    [Fact]
    public async Task ForRuns_PoolsEverySelectedRunAndItsPredecessors_RecordedForMOfN()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = BenchmarkBatteryTestData.NewDb();

        db.BenchmarkRuns.Add(RecordedRun(1, status: BenchmarkRunStatus.Failed));
        db.BenchmarkRuns.Add(RecordedRun(2, replaces: 1, cause: BenchmarkReplacementCause.ProviderFailure));
        db.BenchmarkRuns.Add(BenchmarkBatteryTestData.SuiteARun(6));
        db.BenchmarkProviderAttempts.AddRange(Attempts(1, ok: 5, overloaded: 1));
        db.BenchmarkProviderAttempts.AddRange(Attempts(2, ok: 9, overloaded: 1));
        await db.SaveChangesAsync(ct);

        var pool = await new BenchmarkProviderAvailabilityService(db).ForRunsAsync(new long[] { 2, 6 }, ct);

        Assert.Equal(3, pool.Summary.TotalRuns);
        Assert.Equal(2, pool.Summary.RecordedRuns);
        Assert.Equal(1, pool.Summary.ReplacedRunsIncluded);
        Assert.Equal(16, pool.Summary.ValidAttempts);
        Assert.Equal(2, pool.Summary.ProviderErrors);
        Assert.Contains(pool.Runs, r => r.RunId == 1 && !r.Selected);
        Assert.Contains(pool.Runs, r => r.RunId == 6 && r.Selected && !r.Recorded);
    }

    [Fact]
    public async Task ForRuns_OfUnrecordedRunsOnly_IsNotRecorded()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = BenchmarkBatteryTestData.NewDb();
        db.BenchmarkRuns.Add(BenchmarkBatteryTestData.SuiteARun(7));
        await db.SaveChangesAsync(ct);

        var pool = await new BenchmarkProviderAvailabilityService(db).ForRunsAsync(new long[] { 7 }, ct);
        var dto = BenchmarkProviderAvailabilityService.ToDto(pool);

        Assert.False(dto.Recorded);
        Assert.Equal(0, dto.RecordedRuns);
        Assert.Equal(1, dto.TotalRuns);
        Assert.Null(dto.ProviderErrorRate);
    }

    // --- A battery run ---------------------------------------------------------------------------------

    [Fact]
    public async Task ForBatteryRun_IncludesTheSupersededMember_AndItsRowsSumToTheBatteryReruns()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = BenchmarkBatteryTestData.NewDb();

        var superseded = RecordedRun(10, status: BenchmarkRunStatus.Failed);
        var replacement = RecordedRun(11, replaces: 10, cause: BenchmarkReplacementCause.ProviderFailure);
        replacement.RepairPassCount = 1;
        replacement.ProviderRepairPassCount = 1;
        var other = BenchmarkBatteryTestData.SuiteBRun(12);
        other.ProviderLedgerVersion = 1;

        long batteryRunId = await BenchmarkBatteryTestData.SeedAsync(db, BenchmarkBatteryTestData.Definition(),
            (superseded, 0, 1), (replacement, 0, 1), (other, 1, 1));
        db.BenchmarkBatteryRunMembers.Single(m => m.BenchmarkRunId == 10).Superseded = true;
        db.BenchmarkProviderAttempts.AddRange(Attempts(10, ok: 4, overloaded: 2));
        db.BenchmarkProviderAttempts.AddRange(Attempts(11, ok: 10, overloaded: 0));
        db.BenchmarkProviderAttempts.AddRange(Attempts(12, ok: 9, overloaded: 1));
        await db.SaveChangesAsync(ct);

        var battery = await new BenchmarkProviderAvailabilityService(db).ForBatteryRunAsync(batteryRunId, ct);

        Assert.NotNull(battery);
        var s = battery!.Pool.Summary;
        Assert.Equal(3, s.TotalRuns);
        Assert.Equal(1, s.ReplacedRunsIncluded);
        Assert.Equal(26, s.ValidAttempts);
        Assert.Equal(3, s.ProviderErrors);
        Assert.Equal(2, s.RerunsNeeded);
        Assert.Equal(2, s.ProviderRerunsNeeded);

        Assert.Equal(3, battery.Members.Count);
        var row10 = battery.Members.Single(m => m.RunId == 10);
        Assert.True(row10.Superseded);
        Assert.Equal(6, row10.ValidAttempts);
        Assert.Equal(2, row10.ProviderErrors);
        Assert.Equal(0, row10.RerunsNeeded);
        Assert.Equal(2, battery.Members.Single(m => m.RunId == 11).RerunsNeeded);
        Assert.Equal(s.RerunsNeeded, battery.Members.Sum(m => m.RerunsNeeded));

        var memberDto = BenchmarkProviderAvailabilityService.ToDto(battery.Members.Single(m => m.RunId == 12));
        Assert.Equal(0.1, memberDto.ProviderErrorRate!.Value, 9);
        Assert.Equal(1, memberDto.SuiteIndex);
    }

    [Fact]
    public async Task ForBatteryRun_OfAMissingBatteryRun_IsNull()
    {
        await using var db = BenchmarkBatteryTestData.NewDb();
        Assert.Null(await new BenchmarkProviderAvailabilityService(db).ForBatteryRunAsync(404, TestContext.Current.CancellationToken));
    }

    // --- Card figures ----------------------------------------------------------------------------------

    [Fact]
    public async Task Cards_ReadTheRollupColumnsOnly_AndPoolPredecessors()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = BenchmarkBatteryTestData.NewDb();

        var first = RecordedRun(20, status: BenchmarkRunStatus.Failed);
        first.CandidateValidAttemptCount = 40;
        first.CandidateProviderErrorCount = 4;
        first.CandidateUnattributedErrorCount = 1;
        var second = RecordedRun(21, replaces: 20, cause: BenchmarkReplacementCause.ProviderFailure);
        second.CandidateValidAttemptCount = 160;
        second.CandidateProviderErrorCount = 6;
        second.CandidateUnattributedErrorCount = 0;
        second.RepairPassCount = 1;
        db.BenchmarkRuns.AddRange(first, second, BenchmarkBatteryTestData.SuiteARun(22));

        // Ledger rows that disagree with the rollups: a card never reads them.
        db.BenchmarkProviderAttempts.AddRange(Attempts(21, ok: 1, overloaded: 1));
        await db.SaveChangesAsync(ct);

        var cards = await new BenchmarkProviderAvailabilityService(db).LoadCardsAsync(new long[] { 21, 22 }, ct);

        var card = cards.ForRun(21);
        Assert.True(card.Recorded);
        Assert.Equal(2, card.TotalRuns);
        Assert.Equal(1, card.ReplacedRunsIncluded);
        Assert.Equal(200, card.ValidAttempts);
        Assert.Equal(10, card.ProviderErrors);
        Assert.Equal(1, card.UnattributedErrors);
        Assert.Equal(0.05, card.ProviderErrorRate!.Value, 9);
        Assert.Equal(2, card.RerunsNeeded);
        Assert.Equal(1, card.ProviderRerunsNeeded);

        // A pool counts each run once, however many of its runs share a predecessor.
        Assert.Equal(card, cards.ForPool(new long[] { 21, 21 }));

        var legacy = new BenchmarkRunSummaryDto();
        BenchmarkProviderAvailabilityService.ApplyCard(legacy, cards.ForRun(22));
        Assert.False(legacy.ProviderAvailabilityRecorded);
        Assert.Equal(1, legacy.ProviderRunCount);
        Assert.Null(legacy.ProviderValidAttempts);
        Assert.Null(legacy.ProviderErrorCount);
        Assert.Null(legacy.ProviderErrorRate);
        Assert.Null(legacy.RerunsNeeded);

        var group = new BenchmarkRunGroupDto();
        BenchmarkProviderAvailabilityService.ApplyCard(group, cards.ForPool(new long[] { 21, 22 }));
        Assert.True(group.ProviderAvailabilityRecorded);
        Assert.Equal(2, group.ProviderRecordedRunCount);
        Assert.Equal(3, group.ProviderRunCount);
        Assert.Equal(200, group.ProviderValidAttempts);
        Assert.Equal(10, group.ProviderErrorCount);
        Assert.Equal(2, group.RerunsNeeded);
    }

    [Fact]
    public async Task Cards_OfAnUnknownRun_AreEmpty()
    {
        await using var db = BenchmarkBatteryTestData.NewDb();
        var cards = await new BenchmarkProviderAvailabilityService(db).LoadCardsAsync(new long[] { 77 }, TestContext.Current.CancellationToken);

        Assert.Equal(ProviderAvailabilityCard.None, cards.ForRun(77));
        Assert.False(cards.ForRun(77).Recorded);
    }
}
