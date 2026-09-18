namespace Overseer.Tests.UnitTests;

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Services.Benchmarking;
using Xunit;

/// <summary>
/// The board guard, and the loads of the three secondary grading paths it protects: re-assess,
/// retry failed assessments and assessor calibration. Each load test reads through a fresh
/// DbContext, because a context that seeded the rows fixes up the navigation whether or not the
/// query included it, and would pass for a load that forgot the board.
/// </summary>
public class BenchmarkBoardGuardTests
{
    private const string BoardText = "Dungeon Level 3\nHP: 12/60\na - a blessed +1 quarterstaff (weapon in hands)";
    private const string BoardLabel = "--- GAME CONTEXT BOARD (GROUND TRUTH REFERENCE DATA) ---";

    private static BenchmarkRun RunWith(long? snapshotId, BenchmarkGameSnapshot? snapshot) => new()
    {
        Id = 7,
        SuiteName = "Snapshot suite",
        BenchmarkSuite = new BenchmarkSuite { Id = 3, Name = "Snapshot suite", GameSnapshotId = snapshotId, GameSnapshot = snapshot }
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
    public void RequireBoardLoaded_SnapshotReferencedButNotLoaded_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => BenchmarkBoardGuard.RequireBoardLoaded(RunWith(11, null)));

        Assert.Contains("run 7", ex.Message);
        Assert.Contains("suite 3", ex.Message);
    }

    [Fact]
    public void RequireBoardLoaded_SuiteWithoutABoard_Passes()
    {
        BenchmarkBoardGuard.RequireBoardLoaded(RunWith(null, null));
    }

    [Fact]
    public void RequireBoardLoaded_BoardLoaded_Passes()
    {
        BenchmarkBoardGuard.RequireBoardLoaded(RunWith(11, Snapshot()));
    }

    [Fact]
    public void BoardCharsSent_IsTheBoardLength_ZeroWhenMissing_NullWithoutABoard()
    {
        Assert.Equal(BoardText.Length, BenchmarkBoardGuard.BoardCharsSent(RunWith(11, Snapshot())));
        Assert.Equal(0, BenchmarkBoardGuard.BoardCharsSent(RunWith(11, null)));
        Assert.Null(BenchmarkBoardGuard.BoardCharsSent(RunWith(null, null)));
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
            TestedModelDisplayNameUsed = "Candidate",
            TestedModelProviderUsed = "TestProvider",
            TestedModelIdUsed = "candidate-model",
            AssessorModelDisplayNameUsed = "Assessor",
            AssessorModelProviderUsed = "TestProvider",
            AssessorModelIdUsed = "assessor-model",
            Status = BenchmarkRunStatus.Completed
        };
        var answer = new BenchmarkRunAnswer
        {
            BenchmarkRun = run,
            OrderIndex = 1,
            QuestionText = "What is wielded?",
            AnswerText = "A blessed +1 quarterstaff."
        };
        db.BenchmarkRunAnswers.Add(answer);
        await db.SaveChangesAsync(ct);
        return answer.Id;
    }

    private static string AssessorPromptFor(BenchmarkRun run, BenchmarkRunAnswer answer)
    {
        BenchmarkBoardGuard.RequireBoardLoaded(run);
        return BenchmarkAssessmentPrompt.BuildPerQuestionBody(
            answer.OrderIndex,
            answer.QuestionText,
            BenchmarkDifficulty.Simple,
            run.BenchmarkSuite!.Questions.Single().ExpectedPoints,
            answer.AnswerText,
            BenchmarkAnswerStatus.Ok,
            boardName: run.BenchmarkSuite.GameSnapshot?.Name,
            boardText: run.BenchmarkSuite.GameSnapshot?.SanitizedText);
    }

    [Fact]
    public async Task ReassessmentLoad_CarriesTheBoardIntoTheAssessorPrompt()
    {
        string name = Guid.NewGuid().ToString();
        long answerId = await SeedAsync(name);

        using var db = new ApplicationDbContext(Options(name));
        var answer = await BenchmarkService.ReassessmentAnswerQuery(db)
            .FirstAsync(a => a.Id == answerId, TestContext.Current.CancellationToken);

        string prompt = AssessorPromptFor(answer.BenchmarkRun, answer);

        Assert.Contains(BoardLabel, prompt);
        Assert.Contains("a - a blessed +1 quarterstaff (weapon in hands)", prompt);
    }

    [Fact]
    public async Task RetryAssessmentsLoad_CarriesTheBoardIntoTheAssessorPrompt()
    {
        string name = Guid.NewGuid().ToString();
        await SeedAsync(name);

        using var db = new ApplicationDbContext(Options(name));
        var run = await BenchmarkService.RetryAssessmentsRunQuery(db)
            .FirstAsync(TestContext.Current.CancellationToken);

        Assert.Contains(BoardLabel, AssessorPromptFor(run, run.Answers.Single()));
    }

    [Fact]
    public async Task CalibrationLoad_CarriesTheBoardIntoTheAssessorPrompt()
    {
        string name = Guid.NewGuid().ToString();
        long answerId = await SeedAsync(name);

        using var db = new ApplicationDbContext(Options(name));
        var run = await BenchmarkService.CalibrationRunQuery(db)
            .FirstAsync(TestContext.Current.CancellationToken);
        var answer = await db.BenchmarkRunAnswers.AsNoTracking()
            .FirstAsync(a => a.Id == answerId, TestContext.Current.CancellationToken);

        Assert.Contains(BoardLabel, AssessorPromptFor(run, answer));
    }

    [Fact]
    public async Task ALoadWithoutTheBoard_IsRefusedByTheGuard()
    {
        string name = Guid.NewGuid().ToString();
        await SeedAsync(name);

        using var db = new ApplicationDbContext(Options(name));
        var run = await db.BenchmarkRuns
            .Include(r => r.BenchmarkSuite)
            .ThenInclude(s => s!.Questions)
            .FirstAsync(TestContext.Current.CancellationToken);

        Assert.Throws<InvalidOperationException>(() => BenchmarkBoardGuard.RequireBoardLoaded(run));
    }
}
