namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Services.Benchmarking;
using Overseer.Tests.Helpers;
using Xunit;

/// <summary>
/// The board guard, and the loads of the three secondary grading paths it protects: re-assess,
/// retry failed assessments and assessor calibration. Each load test reads through a fresh
/// DbContext, because a context that seeded the rows fixes up the navigation whether or not the
/// query included it, and would pass for a load that forgot the board. Also the launch-time stamp
/// of the rubrics' BOARD FACTS check, and of the run's own board record.
/// </summary>
public class BenchmarkBoardGuardTests
{
    private const string BoardText = "Dungeon Level 3\nHP: 12/60\na - a blessed +1 quarterstaff (weapon in hands)";
    private const string BoardLabel = "--- GAME CONTEXT BOARD (GROUND TRUTH REFERENCE DATA) ---";

    /// <summary>A run as launched from a suite: the suite's live board, before the run stamps anything.</summary>
    private static BenchmarkRun RunWith(long? snapshotId, BenchmarkGameSnapshot? snapshot) => BenchmarkModelSnapshots.Attach(new BenchmarkRun
    {
        Id = 7,
        SuiteName = "Snapshot suite",
        BenchmarkSuite = new BenchmarkSuite { Id = 3, Name = "Snapshot suite", GameSnapshotId = snapshotId, GameSnapshot = snapshot }
    });

    /// <summary>
    /// A run as graded: its board hash, and its board record by id and, when <paramref name="loaded"/>,
    /// as a loaded navigation.
    /// </summary>
    private static BenchmarkRun GradedRun(bool hasBoard, long? recordId, bool loaded) => BenchmarkModelSnapshots.Attach(new BenchmarkRun
    {
        Id = 7,
        SuiteName = "Snapshot suite",
        GameSnapshotNameUsed = hasBoard ? "Board" : null,
        GameSnapshotSha256Used = hasBoard ? "board-sha" : null,
        BoardSnapshotId = recordId,
        BoardSnapshot = loaded && recordId != null ? Record() : null
    });

    private static BenchmarkRunBoardSnapshot Record() => new()
    {
        Id = 11,
        Sha256 = BenchmarkRunBoardSnapshotStore.ComputeSha256(BoardText, null),
        SanitizedText = BoardText,
        CharCount = BoardText.Length
    };

    private static BenchmarkGameSnapshot Snapshot() => new()
    {
        Id = 11,
        Name = "Board",
        SanitizedText = BoardText,
        Sha256 = "board-sha",
        CaptureMethod = "Upload"
    };

    [Fact]
    public void RequireBoardLoaded_RecordReferencedButNotLoaded_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => BenchmarkBoardGuard.RequireBoardLoaded(GradedRun(true, 11, loaded: false)));

        Assert.Contains("run 7", ex.Message);
        Assert.Contains("board record 11", ex.Message);
    }

    [Fact]
    public void RequireBoardLoaded_BoardNotRecorded_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => BenchmarkBoardGuard.RequireBoardLoaded(GradedRun(true, null, loaded: false)));

        Assert.Contains(BenchmarkRunExamRecord.BoardNotRecordedRefusal, ex.Message);
    }

    [Fact]
    public void RequireBoardLoaded_RunWithoutABoard_Passes()
    {
        BenchmarkBoardGuard.RequireBoardLoaded(GradedRun(false, null, loaded: false));
    }

    [Fact]
    public void RequireBoardLoaded_RecordLoaded_Passes()
    {
        BenchmarkBoardGuard.RequireBoardLoaded(GradedRun(true, 11, loaded: true));
    }

    [Fact]
    public void RequireBoardLoaded_ReadsTheRunsRecord_NeverTheSuitesCurrentBoard()
    {
        // The suite's board was replaced after the run; the run's record is what is graded with.
        var run = GradedRun(true, 11, loaded: true);
        run.BenchmarkSuite = new BenchmarkSuite { Id = 3, Name = "Snapshot suite", GameSnapshotId = 99 };

        BenchmarkBoardGuard.RequireBoardLoaded(run);
        Assert.Contains("a - a blessed +1 quarterstaff", BenchmarkService.GradingBoardBlock(run));
    }

    [Fact]
    public void BoardCharsSent_IsTheBoardLength_ZeroWhenMissing_NullWithoutABoard()
    {
        Assert.Equal(BoardText.Length, BenchmarkBoardGuard.BoardCharsSent(GradedRun(true, 11, loaded: true)));
        Assert.Equal(0, BenchmarkBoardGuard.BoardCharsSent(GradedRun(true, 11, loaded: false)));
        Assert.Null(BenchmarkBoardGuard.BoardCharsSent(GradedRun(false, null, loaded: false)));
    }

    private static DbContextOptions<ApplicationDbContext> Options(string name) =>
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(name).Options;

    /// <summary>Seeds a snapshot suite, one run and one answer in a context of its own; returns the answer id.</summary>
    private static async Task<long> SeedAsync(string databaseName)
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = new ApplicationDbContext(Options(databaseName));

        var snapshot = new BenchmarkGameSnapshot
        {
            Name = "Board",
            SanitizedText = BoardText,
            CharCount = BoardText.Length,
            Sha256 = "board-sha",
            CaptureMethod = "Upload"
        };
        var suite = new BenchmarkSuite { Name = "Snapshot suite", GameSnapshot = snapshot };
        suite.Questions.Add(new BenchmarkQuestion { QuestionText = "What is wielded?", ExpectedPoints = "- the quarterstaff", OrderIndex = 1 });
        var run = new BenchmarkRun
        {
            SuiteName = "Snapshot suite",
            BenchmarkSuite = suite,
            GameSnapshotNameUsed = "Board",
            GameSnapshotSha256Used = "board-sha",
            BoardSnapshot = new BenchmarkRunBoardSnapshot
            {
                Sha256 = BenchmarkRunBoardSnapshotStore.ComputeSha256(BoardText, null),
                SanitizedText = BoardText,
                CharCount = BoardText.Length
            },
            TestedModelSnapshot = BenchmarkModelSnapshots.Model(provider: "TestProvider", modelId: "candidate-model", displayName: "Candidate"),
            AssessorModelSnapshot = BenchmarkModelSnapshots.Model(provider: "TestProvider", modelId: "assessor-model", displayName: "Assessor"),
            Status = BenchmarkRunStatus.Completed
        };
        var answer = new BenchmarkRunAnswer
        {
            BenchmarkRun = run,
            OrderIndex = 1,
            QuestionText = "What is wielded?",
            AnswerText = "A blessed +1 quarterstaff.",
            ExpectedPointsUsed = "- the quarterstaff",
            ExpectedPointsRecorded = true
        };
        db.BenchmarkRunAnswers.Add(answer);
        await db.SaveChangesAsync(ct);
        return answer.Id;
    }

    /// <summary>
    /// The assessor's seed history as the grading paths build it from <paramref name="run"/>: the
    /// grading instructions, the board block, then the per-question body.
    /// </summary>
    private static List<object> AssessorSeedFor(BenchmarkRun run, BenchmarkRunAnswer answer)
    {
        BenchmarkBoardGuard.RequireBoardLoaded(run);
        string? boardBlock = BenchmarkService.GradingBoardBlock(run);
        string body = BenchmarkAssessmentPrompt.BuildPerQuestionBody(
            answer.OrderIndex,
            answer.QuestionText,
            BenchmarkDifficulty.Simple,
            answer.ExpectedPointsRecorded ? BenchmarkRunExamRecord.Rubric(answer) : null,
            answer.AnswerText,
            BenchmarkAnswerStatus.Ok,
            boardGivenAbove: boardBlock != null);
        return BenchmarkService.BuildGradingPrompt(
            BenchmarkService.GradingSystemPrompt,
            BenchmarkAssessmentPrompt.BuildPerQuestionPreamble(run.SuiteName),
            body,
            boardBlock).SeedHistory;
    }

    private static string? Role(object message) =>
        message.GetType().GetProperty("role")?.GetValue(message) as string;

    private static string Content(object message) =>
        message.GetType().GetProperty("content")?.GetValue(message) as string ?? string.Empty;

    /// <summary>The board is the second system message, whole, and the question's body only points at it.</summary>
    private static void AssertBoardInTheSecondSystemMessage(List<object> seed)
    {
        Assert.Equal(3, seed.Count);
        Assert.Equal(new[] { "system", "system", "user" }, seed.Select(Role));
        Assert.StartsWith(BoardLabel, Content(seed[1]));
        Assert.Contains("a - a blessed +1 quarterstaff (weapon in hands)", Content(seed[1]));
        Assert.DoesNotContain(BoardLabel, Content(seed[0]));
        Assert.DoesNotContain(BoardLabel, Content(seed[2]));
        Assert.Contains(BenchmarkAssessmentPrompt.BoardGivenAboveLine, Content(seed[2]));
    }

    [Fact]
    public async Task ReassessmentLoad_CarriesTheBoardIntoTheAssessorRequest()
    {
        string name = Guid.NewGuid().ToString();
        long answerId = await SeedAsync(name);

        using var db = new ApplicationDbContext(Options(name));
        var answer = await BenchmarkService.ReassessmentAnswerQuery(db)
            .FirstAsync(a => a.Id == answerId, TestContext.Current.CancellationToken);

        AssertBoardInTheSecondSystemMessage(AssessorSeedFor(answer.BenchmarkRun, answer));
    }

    [Fact]
    public async Task RetryAssessmentsLoad_CarriesTheBoardIntoTheAssessorRequest()
    {
        string name = Guid.NewGuid().ToString();
        await SeedAsync(name);

        using var db = new ApplicationDbContext(Options(name));
        var run = await BenchmarkService.RetryAssessmentsRunQuery(db)
            .FirstAsync(TestContext.Current.CancellationToken);

        AssertBoardInTheSecondSystemMessage(AssessorSeedFor(run, run.Answers.Single()));
    }

    [Fact]
    public async Task CalibrationLoad_CarriesTheBoardIntoTheAssessorRequest()
    {
        string name = Guid.NewGuid().ToString();
        long answerId = await SeedAsync(name);

        using var db = new ApplicationDbContext(Options(name));
        var run = await BenchmarkService.CalibrationRunQuery(db)
            .FirstAsync(TestContext.Current.CancellationToken);
        var answer = await db.BenchmarkRunAnswers.AsNoTracking()
            .FirstAsync(a => a.Id == answerId, TestContext.Current.CancellationToken);

        AssertBoardInTheSecondSystemMessage(AssessorSeedFor(run, answer));
    }

    [Fact]
    public void ARunWithoutABoard_SendsNoBoardMessage_AndNoPointerToOne()
    {
        var run = GradedRun(false, null, loaded: false);

        var seed = AssessorSeedFor(run, new BenchmarkRunAnswer
        {
            OrderIndex = 1, QuestionText = "Q", AnswerText = "A.", ExpectedPointsUsed = "- p", ExpectedPointsRecorded = true
        });

        Assert.Equal(new[] { "system", "user" }, seed.Select(Role));
        Assert.DoesNotContain(BenchmarkAssessmentPrompt.BoardGivenAboveLine, Content(seed[1]));
        Assert.Null(BenchmarkService.GradingBoardBlock(run));
    }

    // --- The launch-time BOARD FACTS stamp ------------------------------------------------------

    [Fact]
    public void LaunchStamp_ASuiteWithoutABoard_RecordsNoCheck()
    {
        var run = RunWith(null, null);
        run.BoardFactsCheckJson = "stale";

        BenchmarkService.StampBoardProvenance(run, new[]
        {
            new BenchmarkQuestion { Id = 1, OrderIndex = 1, ExpectedPoints = "**BOARD FACTS**\n- \"HP: 12/60\"" }
        });

        Assert.Null(run.BoardFactsCheckJson);
        Assert.Null(run.GameSnapshotSha256Used);
    }

    [Fact]
    public void LaunchStamp_ABoardWithNoBoardFacts_RecordsAnEmptyCheck_NotNull()
    {
        var run = RunWith(11, Snapshot());

        BenchmarkService.StampBoardProvenance(run, new[]
        {
            new BenchmarkQuestion { Id = 1, OrderIndex = 1, ExpectedPoints = "**REQUIRED**\n- the quarterstaff" }
        });

        Assert.NotNull(run.BoardFactsCheckJson);
        var check = BenchmarkBoardFactsChecker.Deserialize(run.BoardFactsCheckJson);
        Assert.NotNull(check);
        Assert.Equal(0, check!.BulletCount);
        Assert.Equal(0, check.CheckedLiteralCount);
        Assert.Empty(check.MissingLiterals);
        Assert.Equal("board-sha", run.GameSnapshotSha256Used);
    }

    [Fact]
    public void LaunchStamp_RecordsTheQuotesThatAreNotOnTheBoard()
    {
        var run = RunWith(11, Snapshot());

        BenchmarkService.StampBoardProvenance(run, new[]
        {
            new BenchmarkQuestion
            {
                Id = 41,
                OrderIndex = 2,
                ExpectedPoints = "**BOARD FACTS**\n- \"HP: 12/60\" and \"a - a cursed -1 quarterstaff\"\n- The hero is not hungry.\n**REQUIRED**\n- x"
            }
        });

        var check = BenchmarkBoardFactsChecker.Deserialize(run.BoardFactsCheckJson);
        Assert.NotNull(check);
        Assert.Equal(2, check!.BulletCount);
        Assert.Equal(2, check.CheckedLiteralCount);
        Assert.Equal(1, check.UnquotedBulletCount);
        var missing = Assert.Single(check.MissingLiterals);
        Assert.Equal("a - a cursed -1 quarterstaff", missing.Literal);
        Assert.Equal(41L, missing.QuestionId);
        Assert.Equal(2, missing.OrderIndex);
    }

    [Fact]
    public async Task ALoadWithoutTheBoard_IsRefusedByTheGuard()
    {
        string name = Guid.NewGuid().ToString();
        await SeedAsync(name);

        using var db = new ApplicationDbContext(Options(name));
        var run = await db.BenchmarkRuns
            .Include(r => r.BenchmarkSuite)
            .ThenInclude(s => s!.GameSnapshot)
            .FirstAsync(TestContext.Current.CancellationToken);

        Assert.Throws<InvalidOperationException>(() => BenchmarkBoardGuard.RequireBoardLoaded(run));
    }

    [Fact]
    public async Task LaunchStamp_PointsTheRunAtARecordOfItsSuitesBoard()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = new ApplicationDbContext(Options(Guid.NewGuid().ToString()));
        var run = RunWith(11, Snapshot());

        BenchmarkService.StampBoardProvenance(run, Array.Empty<BenchmarkQuestion>());
        await BenchmarkService.StampBoardRecordAsync(db, run, ct);

        Assert.NotNull(run.BoardSnapshot);
        Assert.Equal(BoardText, run.BoardSnapshot!.SanitizedText);
        Assert.Equal("board-sha", run.GameSnapshotSha256Used);
        Assert.Equal(1, await db.BenchmarkRunBoardSnapshots.CountAsync(ct));

        var withoutBoard = RunWith(null, null);
        await BenchmarkService.StampBoardRecordAsync(db, withoutBoard, ct);
        Assert.Null(withoutBoard.BoardSnapshot);
    }
}
