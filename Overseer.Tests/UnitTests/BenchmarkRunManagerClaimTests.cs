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

    // --- The model batch claim ------------------------------------------------------------------

    private const string Batch = "modelbatch:9";
    private const string OtherBatch = "modelbatch:10";

    [Fact]
    public void BatchClaim_IsTakenWhenFree_AgainByItsOwner_AndNotByAnother()
    {
        var manager = new BenchmarkRunManager();

        Assert.True(manager.TryClaimBatch(Batch));
        Assert.True(manager.TryClaimBatch(Batch));
        Assert.False(manager.TryClaimBatch(OtherBatch));
        Assert.Equal(Batch, manager.BatchOwner);
        Assert.Equal(Batch, manager.ClaimHolder);
    }

    [Fact]
    public void BatchClaim_AdmitsItsOwnChildren_AndRefusesAnOutsideLaunch()
    {
        var manager = new BenchmarkRunManager();
        Assert.True(manager.TryClaimBatch(Batch));

        // Outside launches and outside orchestrators are refused.
        Assert.False(manager.TryStart(1, new CancellationTokenSource(), out _));
        Assert.False(manager.TryClaimOrchestrator(Series));
        Assert.False(manager.TryClaimOrchestrator(Battery, OtherBatch));

        // The batch's own plain run is admitted.
        Assert.True(manager.TryStart(2, new CancellationTokenSource(), out _, batchOwner: Batch));
        manager.Complete(2);

        // The batch's own series is admitted, and so are the series' members.
        Assert.True(manager.TryClaimOrchestrator(Series, Batch));
        Assert.False(manager.TryStart(3, new CancellationTokenSource(), out _, orchestratorOwner: Series));
        Assert.True(manager.TryStart(4, new CancellationTokenSource(), out _, Series, Batch));
        Assert.Equal(4L, manager.CurrentRunId);
    }

    [Fact]
    public void ChildClaimInsideABatch_ReleasesBackToTheBatch()
    {
        var manager = new BenchmarkRunManager();
        Assert.True(manager.TryClaimBatch(Batch));
        Assert.True(manager.TryClaimOrchestrator(Battery, Batch));
        Assert.Equal(Batch, manager.ClaimHolder);

        manager.ReleaseOrchestrator(Battery);

        Assert.Null(manager.OrchestratorOwner);
        Assert.Equal(Batch, manager.BatchOwner);
        Assert.False(manager.TryStart(5, new CancellationTokenSource(), out _));
        Assert.False(manager.TryClaimOrchestrator(Series));
        Assert.True(manager.TryClaimOrchestrator(Series, Batch));
    }

    [Fact]
    public void BatchRelease_ByAForeignOwner_IsANoOp_AndByItsOwner_FreesTheGate()
    {
        var manager = new BenchmarkRunManager();
        Assert.True(manager.TryClaimBatch(Batch));

        manager.ReleaseBatch(OtherBatch);
        Assert.Equal(Batch, manager.BatchOwner);

        manager.ReleaseBatch(Batch);
        Assert.Null(manager.BatchOwner);
        Assert.Null(manager.ClaimHolder);
        Assert.True(manager.TryStart(6, new CancellationTokenSource(), out _));
        manager.Complete(6);
        Assert.True(manager.TryClaimBatch(OtherBatch));
    }

    [Fact]
    public void ModelBatchOwnerToken_AndConflictMessage_NameTheBatch()
    {
        Assert.Equal("modelbatch:56", BenchmarkRunManager.ModelBatchOwner(56));
        Assert.Equal("A model batch is running; wait for it or cancel it.",
            BenchmarkRunManager.ClaimConflictMessage(BenchmarkRunManager.ModelBatchOwner(56)));
    }
}
