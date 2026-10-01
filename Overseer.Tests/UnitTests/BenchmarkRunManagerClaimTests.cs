namespace Overseer.Tests.UnitTests;

using System.Threading;
using Overseer.Services.Benchmarking;
using Xunit;

/// <summary>
/// The orchestrator claim of <see cref="BenchmarkRunManager"/>: one series or battery owns the run
/// gate between its members, and <c>TryStart</c> admits only that owner's runs while it does.
/// </summary>
public class BenchmarkRunManagerClaimTests
{
    private const string Battery = "battery:7";
    private const string Series = "series:3";

    [Fact]
    public void Claim_IsTakenWhenFree_AndReported()
    {
        var manager = new BenchmarkRunManager();

        Assert.Null(manager.OrchestratorOwner);
        Assert.True(manager.TryClaimOrchestrator(Battery));
        Assert.Equal(Battery, manager.OrchestratorOwner);
    }

    [Fact]
    public void Claim_CanBeTakenAgainByTheSameOwner_ButNotByAnother()
    {
        var manager = new BenchmarkRunManager();
        Assert.True(manager.TryClaimOrchestrator(Battery));

        Assert.True(manager.TryClaimOrchestrator(Battery));
        Assert.False(manager.TryClaimOrchestrator(Series));
        Assert.Equal(Battery, manager.OrchestratorOwner);
    }

    [Fact]
    public void Release_ByAForeignOwner_IsANoOp()
    {
        var manager = new BenchmarkRunManager();
        Assert.True(manager.TryClaimOrchestrator(Battery));

        manager.ReleaseOrchestrator(Series);
        Assert.Equal(Battery, manager.OrchestratorOwner);

        manager.ReleaseOrchestrator(Battery);
        Assert.Null(manager.OrchestratorOwner);
        Assert.True(manager.TryClaimOrchestrator(Series));
    }

    [Fact]
    public void TryStart_IsRefused_UnderAForeignClaim()
    {
        var manager = new BenchmarkRunManager();
        Assert.True(manager.TryClaimOrchestrator(Battery));

        Assert.False(manager.TryStart(1, new CancellationTokenSource(), out _));
        Assert.False(manager.TryStart(2, new CancellationTokenSource(), out _, Series));
        Assert.Null(manager.CurrentRunId);
    }

    [Fact]
    public void TryStart_IsAccepted_ForTheClaimOwner()
    {
        var manager = new BenchmarkRunManager();
        Assert.True(manager.TryClaimOrchestrator(Battery));

        Assert.True(manager.TryStart(1, new CancellationTokenSource(), out var state, Battery));
        Assert.Equal(1L, state.RunId);
        Assert.Equal(1L, manager.CurrentRunId);
    }

    [Fact]
    public void TryStart_WithoutAClaim_BehavesAsBefore_WhateverTheOwner()
    {
        var manager = new BenchmarkRunManager();

        Assert.True(manager.TryStart(1, new CancellationTokenSource(), out _, Battery));
        Assert.False(manager.TryStart(2, new CancellationTokenSource(), out _));

        manager.Complete(1);
        Assert.True(manager.TryStart(3, new CancellationTokenSource(), out _));
    }

    [Fact]
    public void OwnerTokens_AndConflictMessages_NameTheKind()
    {
        Assert.Equal("series:12", BenchmarkRunManager.SeriesOwner(12));
        Assert.Equal("battery:34", BenchmarkRunManager.BatteryOwner(34));

        Assert.Equal("A battery is running; wait for it or cancel it.",
            BenchmarkRunManager.ClaimConflictMessage(BenchmarkRunManager.BatteryOwner(34)));
        Assert.Equal("A benchmark series is running; wait for it or cancel it.",
            BenchmarkRunManager.ClaimConflictMessage(BenchmarkRunManager.SeriesOwner(12)));
    }
}
