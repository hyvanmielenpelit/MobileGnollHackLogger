namespace Overseer.Tests.UnitTests;

using System;
using MobileGnollHackLogger.Data;
using Overseer.Services.Benchmarking;
using Xunit;

/// <summary>
/// The rubric and board a run recorded for itself: returned when recorded, thrown on when not, and
/// the refusal an operation on an existing run gives before it grades anything.
/// </summary>
public class BenchmarkRunExamRecordTests
{
    private static BenchmarkRunAnswer Recorded(int orderIndex, string? rubric) => new()
    {
        OrderIndex = orderIndex,
        ExpectedPointsUsed = rubric,
        ExpectedPointsRecorded = true
    };

    private static BenchmarkRunAnswer Unrecorded(int orderIndex) => new() { OrderIndex = orderIndex };

    [Fact]
    public void Rubric_IsReturnedWhenRecorded_IncludingANullRubric()
    {
        Assert.Equal("- the quarterstaff", BenchmarkRunExamRecord.Rubric(Recorded(1, "- the quarterstaff")));
        Assert.Null(BenchmarkRunExamRecord.Rubric(Recorded(2, null)));
    }

    [Fact]
    public void Rubric_ThrowsWhenNotRecorded()
    {
        Assert.Throws<InvalidOperationException>(() => BenchmarkRunExamRecord.Rubric(Unrecorded(3)));
    }

    [Fact]
    public void RefusalFor_NamesTheUnrecordedQuestions_ByStoredOrder()
    {
        var run = new BenchmarkRun { Id = 7 };

        string? refusal = BenchmarkRunExamRecord.RefusalFor(run, new[] { Unrecorded(4), Recorded(1, "- x"), Unrecorded(2), Unrecorded(4) });

        Assert.NotNull(refusal);
        Assert.StartsWith("Refused: ", refusal);
        Assert.StartsWith(BenchmarkRunExamRecord.RubricNotRecordedRefusal, refusal);
        Assert.EndsWith("Questions: Q2, Q4.", refusal);
    }

    [Fact]
    public void RefusalFor_NamesAnUnknownBoard()
    {
        var run = new BenchmarkRun { Id = 7, GameSnapshotSha256Used = "board-sha" };

        Assert.Equal(BenchmarkRunExamRecord.BoardNotRecordedRefusal, BenchmarkRunExamRecord.RefusalFor(run, new[] { Recorded(1, "- x") }));
        Assert.True(BenchmarkRunExamRecord.BoardUnknown(run));
        Assert.Throws<InvalidOperationException>(() => BenchmarkRunExamRecord.Board(run));
    }

    [Fact]
    public void ARunWithoutABoard_IsNotRefused_AndHasNoBoard()
    {
        var run = new BenchmarkRun { Id = 7 };

        Assert.Null(BenchmarkRunExamRecord.RefusalFor(run, new[] { Recorded(1, "- x"), Recorded(2, null) }));
        Assert.False(BenchmarkRunExamRecord.BoardUnknown(run));
        Assert.Null(BenchmarkRunExamRecord.Board(run));
    }

    [Fact]
    public void Board_IsTheRecord_AndThrowsWhenTheRecordWasNotLoaded()
    {
        var record = new BenchmarkRunBoardSnapshot { Id = 3, Sha256 = new string('a', 64), SanitizedText = "HP: 12/60" };
        var loaded = new BenchmarkRun { Id = 7, GameSnapshotSha256Used = "board-sha", BoardSnapshotId = 3, BoardSnapshot = record };
        var notLoaded = new BenchmarkRun { Id = 8, GameSnapshotSha256Used = "board-sha", BoardSnapshotId = 3 };

        Assert.Same(record, BenchmarkRunExamRecord.Board(loaded));
        Assert.Null(BenchmarkRunExamRecord.RefusalFor(loaded, Array.Empty<BenchmarkRunAnswer>()));
        Assert.Throws<InvalidOperationException>(() => BenchmarkRunExamRecord.Board(notLoaded));
    }
}
