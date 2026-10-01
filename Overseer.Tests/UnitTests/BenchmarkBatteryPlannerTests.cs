namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using MobileGnollHackLogger.Data;
using Overseer.Services.Benchmarking;
using Xunit;

/// <summary>
/// The round-robin slot plan of a battery run and the usability rule for its members (M4).
/// </summary>
public class BenchmarkBatteryPlannerTests
{
    private static BenchmarkBatteryRunMember Member(bool superseded = false, string? guardFailure = null)
        => new() { Id = 1, BenchmarkRunId = 11, SuiteIndex = 0, Round = 1, Superseded = superseded, GuardFailure = guardFailure };

    private static BenchmarkRun Run(BenchmarkRunStatus status, int? qualityIndex = 72, int? terminalFailures = 0)
        => new() { Id = 11, Status = status, QualityIndex = qualityIndex, TerminalFailureAnswerCount = terminalFailures };

    // --- Slot plan -------------------------------------------------------------------------------

    [Fact]
    public void NextMember_FillsTheSlotsRoundRobin()
    {
        var occupied = new List<(int SuiteIndex, int Round)>();
        var order = new List<(int SuiteIndex, int Round)>();

        while (BenchmarkBatteryPlanner.NextMember(3, 2, occupied) is { } next)
        {
            order.Add(next);
            occupied.Add(next);
        }

        var expected = new List<(int SuiteIndex, int Round)> { (0, 1), (1, 1), (2, 1), (0, 2), (1, 2), (2, 2) };
        Assert.Equal(expected, order);
    }

    [Fact]
    public void NextMember_SkipsOccupiedSlots()
    {
        (int SuiteIndex, int Round)? first = BenchmarkBatteryPlanner.NextMember(3, 2, new[] { (0, 1), (2, 1) });
        (int SuiteIndex, int Round)? second = BenchmarkBatteryPlanner.NextMember(3, 2, new[] { (0, 1), (1, 1), (2, 1), (1, 2) });

        Assert.Equal((1, 1), first!.Value);
        Assert.Equal((0, 2), second!.Value);
    }

    [Fact]
    public void NextMember_IsNullOnceEverySlotIsOccupied()
    {
        var all = new[] { (0, 1), (1, 1) };

        Assert.Null(BenchmarkBatteryPlanner.NextMember(2, 1, all));
        Assert.Equal(0, BenchmarkBatteryPlanner.RemainingLaunches(2, 1, all));
    }

    [Fact]
    public void RemainingLaunches_CountsUnoccupiedSlotsInTheGrid()
    {
        Assert.Equal(6, BenchmarkBatteryPlanner.RemainingLaunches(3, 2, Array.Empty<(int, int)>()));
        Assert.Equal(4, BenchmarkBatteryPlanner.RemainingLaunches(3, 2, new[] { (0, 1), (2, 2) }));

        // A pair outside the grid occupies nothing.
        Assert.Equal(6, BenchmarkBatteryPlanner.RemainingLaunches(3, 2, new[] { (5, 1), (0, 3) }));
    }

    [Fact]
    public void ThePlanner_RefusesAnEmptyGrid()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BenchmarkBatteryPlanner.NextMember(0, 1, Array.Empty<(int, int)>()));
        Assert.Throws<ArgumentOutOfRangeException>(() => BenchmarkBatteryPlanner.RemainingLaunches(2, 0, Array.Empty<(int, int)>()));
    }

    // --- Usability (M4) --------------------------------------------------------------------------

    [Theory]
    [InlineData(BenchmarkRunStatus.Completed)]
    [InlineData(BenchmarkRunStatus.CompletedWithLimits)]
    [InlineData(BenchmarkRunStatus.CompletedWithErrors)]
    public void ASuccessfulRunWithAnIndex_IsUsable(BenchmarkRunStatus status)
    {
        Assert.True(BenchmarkBatteryPlanner.IsUsable(Member(), Run(status)));
        Assert.Null(BenchmarkBatteryPlanner.UnusableReason(Member(), Run(status)));
    }

    [Fact]
    public void ASupersededMember_IsNotUsable()
    {
        Assert.False(BenchmarkBatteryPlanner.IsUsable(Member(superseded: true), Run(BenchmarkRunStatus.Completed)));
        Assert.Equal("superseded", BenchmarkBatteryPlanner.UnusableReason(Member(superseded: true), Run(BenchmarkRunStatus.Completed)));
    }

    [Fact]
    public void AMemberWithAGuardFailure_IsNotUsable()
    {
        var member = Member(guardFailure: "ToolGuidesSha256 moved");

        Assert.False(BenchmarkBatteryPlanner.IsUsable(member, Run(BenchmarkRunStatus.Completed)));
        Assert.Equal("instrument guard: ToolGuidesSha256 moved", BenchmarkBatteryPlanner.UnusableReason(member, Run(BenchmarkRunStatus.Completed)));
    }

    [Fact]
    public void ACompletedWithErrorsRunWhoseIndexWasWithheld_IsNotUsable()
    {
        var run = Run(BenchmarkRunStatus.CompletedWithErrors, qualityIndex: null, terminalFailures: 2);

        Assert.False(BenchmarkBatteryPlanner.IsUsable(Member(), run));
        Assert.Equal("index withheld (provider failure)", BenchmarkBatteryPlanner.UnusableReason(Member(), run));
    }

    [Fact]
    public void ACompletedRunWithoutAnIndex_IsNotUsable()
    {
        var run = Run(BenchmarkRunStatus.Completed, qualityIndex: null, terminalFailures: null);

        Assert.False(BenchmarkBatteryPlanner.IsUsable(Member(), run));
        Assert.Equal("index withheld", BenchmarkBatteryPlanner.UnusableReason(Member(), run));
    }

    [Theory]
    [InlineData(BenchmarkRunStatus.Failed, "run failed")]
    [InlineData(BenchmarkRunStatus.Canceled, "run canceled")]
    [InlineData(BenchmarkRunStatus.Running, "run not finished")]
    public void AnUnsuccessfulOrUnfinishedRun_IsNotUsable(BenchmarkRunStatus status, string reason)
    {
        Assert.False(BenchmarkBatteryPlanner.IsUsable(Member(), Run(status)));
        Assert.Equal(reason, BenchmarkBatteryPlanner.UnusableReason(Member(), Run(status)));
    }
}
