namespace Overseer.Tests.UnitTests;

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services.Benchmarking;
using Xunit;

/// <summary>
/// POST suites/{suiteId}/questions/import: replaces questions that carry an id, creates those that
/// do not, and validates the whole batch before writing anything.
/// </summary>
public class AdminBenchmarkQuestionImportTests
{
    private static ImportBenchmarkQuestionsResult ReadResult(IActionResult result)
        => Assert.IsType<ImportBenchmarkQuestionsResult>(Assert.IsType<OkObjectResult>(result).Value);

    [Fact]
    public async Task CreatesAndReplaces_WithCountsAndOrderContinuingAfterTheMax()
    {
        var ct = TestContext.Current.CancellationToken;
        var (controller, db, _) = BenchmarkComplianceGuardTests.CreateTestBenchmarkController();
        var (suite, _, _, _) = await BenchmarkComplianceGuardTests.SeedConfigsAndSuite(db);
        var existing = suite.Questions.Single();
        existing.OrderIndex = 5;
        await db.SaveChangesAsync(ct);

        var result = await controller.ImportQuestions(suite.Id, new ImportBenchmarkQuestionsRequest
        {
            Items = new List<ImportBenchmarkQuestionItem>
            {
                new() { QuestionId = existing.Id, QuestionText = "Q1 rewritten" },
                new() { QuestionText = "New A", Difficulty = BenchmarkDifficulty.Advanced, ExpectedPoints = "- point", ReplaceExpectedPoints = true },
                new() { QuestionText = "  New B  " }
            }
        }, ct);

        var dto = ReadResult(result);
        Assert.Equal(2, dto.CreatedCount);
        Assert.Equal(1, dto.ReplacedCount);
        Assert.Equal(0, dto.UnchangedCount);
        Assert.Equal(new[] { 5, 6, 7 }, dto.Questions.Select(q => q.OrderIndex).ToArray());

        var created = await db.BenchmarkQuestions.Where(q => q.BenchmarkSuiteId == suite.Id && q.Id != existing.Id)
            .OrderBy(q => q.OrderIndex).ToListAsync(ct);
        Assert.Equal("New A", created[0].QuestionText);
        Assert.Equal(BenchmarkDifficulty.Advanced, created[0].Difficulty);
        Assert.Equal("- point", created[0].ExpectedPoints);
        Assert.Equal("New B", created[1].QuestionText);
        Assert.Equal(BenchmarkDifficulty.Simple, created[1].Difficulty);
        Assert.Null(created[1].ExpectedPoints);
        Assert.All(created, q => Assert.False(q.IsGenerated));
    }

    [Fact]
    public async Task IdenticalReplace_IsUnchanged_AndKeepsItemRevisionAndAssessment()
    {
        var ct = TestContext.Current.CancellationToken;
        var (controller, db, _) = BenchmarkComplianceGuardTests.CreateTestBenchmarkController();
        var (suite, _, _, _) = await BenchmarkComplianceGuardTests.SeedConfigsAndSuite(db);
        var q = suite.Questions.Single();

        var result = await controller.ImportQuestions(suite.Id, new ImportBenchmarkQuestionsRequest
        {
            Items = { new() { QuestionId = q.Id, QuestionText = "Q1\n", Difficulty = BenchmarkDifficulty.Simple } }
        }, ct);

        var dto = ReadResult(result);
        Assert.Equal(1, dto.UnchangedCount);
        Assert.Equal(0, dto.ReplacedCount);
        var stored = await db.BenchmarkQuestions.SingleAsync(x => x.Id == q.Id, ct);
        Assert.Equal(1, stored.ItemRevision);
        Assert.Equal(25, stored.AssessedDifficulty);
    }

    [Fact]
    public async Task ChangedRubric_BumpsItemRevision_AndClearsAssessedDifficulty()
    {
        var ct = TestContext.Current.CancellationToken;
        var (controller, db, _) = BenchmarkComplianceGuardTests.CreateTestBenchmarkController();
        var (suite, _, _, _) = await BenchmarkComplianceGuardTests.SeedConfigsAndSuite(db);
        var q = suite.Questions.Single();

        var result = await controller.ImportQuestions(suite.Id, new ImportBenchmarkQuestionsRequest
        {
            Items = { new() { QuestionId = q.Id, ExpectedPoints = "- new rubric", ReplaceExpectedPoints = true } }
        }, ct);

        Assert.Equal(1, ReadResult(result).ReplacedCount);
        var stored = await db.BenchmarkQuestions.SingleAsync(x => x.Id == q.Id, ct);
        Assert.Equal(2, stored.ItemRevision);
        Assert.Null(stored.AssessedDifficulty);
        Assert.Equal("- new rubric", stored.ExpectedPoints);
        Assert.Equal("Q1", stored.QuestionText);
    }

    [Fact]
    public async Task ReplaceExpectedPoints_TrueWithEmptyClears_FalseKeeps()
    {
        var ct = TestContext.Current.CancellationToken;
        var (controller, db, _) = BenchmarkComplianceGuardTests.CreateTestBenchmarkController();
        var (suite, _, _, _) = await BenchmarkComplianceGuardTests.SeedConfigsAndSuite(db);
        var q = suite.Questions.Single();
        q.ExpectedPoints = "- keep me";
        await db.SaveChangesAsync(ct);

        await controller.ImportQuestions(suite.Id, new ImportBenchmarkQuestionsRequest
        {
            Items = { new() { QuestionId = q.Id, ExpectedPoints = "", ReplaceExpectedPoints = false } }
        }, ct);
        Assert.Equal("- keep me", (await db.BenchmarkQuestions.AsNoTracking().SingleAsync(x => x.Id == q.Id, ct)).ExpectedPoints);

        await controller.ImportQuestions(suite.Id, new ImportBenchmarkQuestionsRequest
        {
            Items = { new() { QuestionId = q.Id, ExpectedPoints = "", ReplaceExpectedPoints = true } }
        }, ct);
        Assert.Null((await db.BenchmarkQuestions.AsNoTracking().SingleAsync(x => x.Id == q.Id, ct)).ExpectedPoints);
    }

    [Fact]
    public async Task ForeignId_Returns400_AndWritesNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var (controller, db, _) = BenchmarkComplianceGuardTests.CreateTestBenchmarkController();
        var (suite, _, _, _) = await BenchmarkComplianceGuardTests.SeedConfigsAndSuite(db);

        var result = await controller.ImportQuestions(suite.Id, new ImportBenchmarkQuestionsRequest
        {
            Items =
            {
                new() { QuestionText = "Would be created" },
                new() { QuestionId = 999999, QuestionText = "Foreign" }
            }
        }, ct);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("does not belong to suite", bad.Value?.ToString());
        Assert.Equal(1, await db.BenchmarkQuestions.CountAsync(x => x.BenchmarkSuiteId == suite.Id, ct));
    }

    [Fact]
    public async Task BlankTextOnCreate_Returns400()
    {
        var ct = TestContext.Current.CancellationToken;
        var (controller, db, _) = BenchmarkComplianceGuardTests.CreateTestBenchmarkController();
        var (suite, _, _, _) = await BenchmarkComplianceGuardTests.SeedConfigsAndSuite(db);

        var result = await controller.ImportQuestions(suite.Id, new ImportBenchmarkQuestionsRequest
        {
            Items = { new() { QuestionText = "   " } }
        }, ct);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Question text is required for a new question.", bad.Value);
    }

    [Fact]
    public async Task OverTheGuardLimit_Returns400WithGuardText_AndPersistsNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var (controller, db, _) = BenchmarkComplianceGuardTests.CreateTestBenchmarkController(maxQuestions: 2);
        var (suite, _, _, _) = await BenchmarkComplianceGuardTests.SeedConfigsAndSuite(db);
        var q = suite.Questions.Single();

        var result = await controller.ImportQuestions(suite.Id, new ImportBenchmarkQuestionsRequest
        {
            Items =
            {
                new() { QuestionId = q.Id, QuestionText = "Changed" },
                new() { QuestionText = "A" },
                new() { QuestionText = "B" }
            }
        }, ct);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("Suite question limit reached (2 questions maximum)", bad.Value?.ToString());
        Assert.Equal(1, await db.BenchmarkQuestions.CountAsync(x => x.BenchmarkSuiteId == suite.Id, ct));
        Assert.Equal("Q1", (await db.BenchmarkQuestions.AsNoTracking().SingleAsync(x => x.Id == q.Id, ct)).QuestionText);
    }

    [Fact]
    public async Task UnknownSuite_Returns404()
    {
        var (controller, _, _) = BenchmarkComplianceGuardTests.CreateTestBenchmarkController();

        var result = await controller.ImportQuestions(404, new ImportBenchmarkQuestionsRequest
        {
            Items = { new() { QuestionText = "A" } }
        }, TestContext.Current.CancellationToken);

        Assert.IsType<NotFoundResult>(result);
    }

    // --- BOARD FACTS quote check -------------------------------------------------------------

    private const string Board = "T - the uncursed Holy Grail (0 charges, 0 rechargings)\nHP:14(14)";

    [Fact]
    public async Task ImportIntoASuiteWithABoard_ReturnsTheCheck_AndStillWritesTheRubric()
    {
        var ct = TestContext.Current.CancellationToken;
        var (controller, db, _) = BenchmarkComplianceGuardTests.CreateTestBenchmarkController();
        var (suite, _, _, _) = await BenchmarkComplianceGuardTests.SeedConfigsAndSuite(db);
        suite.GameSnapshot = new BenchmarkGameSnapshot
        {
            Name = "board",
            SanitizedText = Board,
            CharCount = Board.Length,
            Sha256 = "sha",
            CaptureMethod = "TextUpload"
        };
        await db.SaveChangesAsync(ct);
        var q = suite.Questions.Single();

        string rubric = "**BOARD FACTS**\n"
            + "- The status line reads \"HP:14(14)\".\n"
            + "- The inventory lists \"T - the Holy Grail (0 charges, 0 rechargings)\".\n"
            + "- The status line shows no hunger state.\n"
            + "**REQUIRED**\n"
            + "- A point.";

        var dto = ReadResult(await controller.ImportQuestions(suite.Id, new ImportBenchmarkQuestionsRequest
        {
            Items = { new() { QuestionId = q.Id, ExpectedPoints = rubric, ReplaceExpectedPoints = true } }
        }, ct));

        // Advisory: the write lands whatever the check finds.
        Assert.Equal(1, dto.ReplacedCount);
        Assert.Equal(rubric, (await db.BenchmarkQuestions.AsNoTracking().SingleAsync(x => x.Id == q.Id, ct)).ExpectedPoints);

        var check = Assert.IsType<BoardFactsCheckDto>(dto.BoardFactsCheck);
        Assert.Equal(3, check.BulletCount);
        Assert.Equal(2, check.CheckedLiteralCount);
        Assert.Equal(1, check.UnquotedBulletCount);
        var missing = Assert.Single(check.MissingLiterals);
        Assert.Equal(q.Id, missing.QuestionId);
        Assert.Equal(q.OrderIndex, missing.OrderIndex);
        Assert.Equal("T - the Holy Grail (0 charges, 0 rechargings)", missing.Literal);
    }

    [Fact]
    public async Task ImportIntoASuiteWithoutABoard_ReturnsANullCheck()
    {
        var ct = TestContext.Current.CancellationToken;
        var (controller, db, _) = BenchmarkComplianceGuardTests.CreateTestBenchmarkController();
        var (suite, _, _, _) = await BenchmarkComplianceGuardTests.SeedConfigsAndSuite(db);

        var dto = ReadResult(await controller.ImportQuestions(suite.Id, new ImportBenchmarkQuestionsRequest
        {
            Items = { new() { QuestionText = "New", ExpectedPoints = "**BOARD FACTS**\n- \"x\"", ReplaceExpectedPoints = true } }
        }, ct));

        Assert.Equal(1, dto.CreatedCount);
        Assert.Null(dto.BoardFactsCheck);
    }

    // --- Gold evidence ------------------------------------------------------------------------

    private const string HumanEvidenceJson = "[{\"kind\":\"wiki\",\"path\":\"Races/Human.md\",\"personal\":true}]";

    private static List<BenchmarkEvidenceUnit> HumanEvidence() => new()
    {
        new() { Kind = BenchmarkEvidenceUnit.WikiKind, Path = "Races/Human.md", Personal = true }
    };

    [Fact]
    public async Task EvidenceOnlyImport_StoresTheEvidence_AndKeepsRevisionAssessmentAndReviewMark()
    {
        var ct = TestContext.Current.CancellationToken;
        var (controller, db, _) = BenchmarkComplianceGuardTests.CreateTestBenchmarkController();
        var (suite, _, _, _) = await BenchmarkComplianceGuardTests.SeedConfigsAndSuite(db);
        var q = suite.Questions.Single();
        q.ExpectedPoints = "- the rubric";
        q.ReviewedAtRevision = 1;
        await db.SaveChangesAsync(ct);

        var dto = ReadResult(await controller.ImportQuestions(suite.Id, new ImportBenchmarkQuestionsRequest
        {
            Items = { new() { QuestionId = q.Id, Evidence = HumanEvidence() } }
        }, ct));

        Assert.Equal(1, dto.ReplacedCount);
        Assert.Equal(0, dto.UnchangedCount);
        var unit = Assert.Single(dto.Questions.Single().Evidence!);
        Assert.Equal("Races/Human.md", unit.Path);
        Assert.True(unit.Personal);

        var stored = await db.BenchmarkQuestions.AsNoTracking().SingleAsync(x => x.Id == q.Id, ct);
        Assert.Equal(HumanEvidenceJson, stored.EvidenceJson);
        Assert.Equal(1, stored.ItemRevision);
        Assert.Equal(25, stored.AssessedDifficulty);
        Assert.Equal(1, stored.ReviewedAtRevision);
        Assert.Equal("- the rubric", stored.ExpectedPoints);
        Assert.Equal("Q1", stored.QuestionText);
    }

    [Fact]
    public async Task TheSameEvidenceAgain_IsUnchanged()
    {
        var ct = TestContext.Current.CancellationToken;
        var (controller, db, _) = BenchmarkComplianceGuardTests.CreateTestBenchmarkController();
        var (suite, _, _, _) = await BenchmarkComplianceGuardTests.SeedConfigsAndSuite(db);
        var q = suite.Questions.Single();
        q.EvidenceJson = HumanEvidenceJson;
        await db.SaveChangesAsync(ct);

        var dto = ReadResult(await controller.ImportQuestions(suite.Id, new ImportBenchmarkQuestionsRequest
        {
            Items = { new() { QuestionId = q.Id, Evidence = HumanEvidence() } }
        }, ct));

        Assert.Equal(1, dto.UnchangedCount);
        Assert.Equal(0, dto.ReplacedCount);
    }

    [Fact]
    public async Task NullEvidenceKeeps_AndAnEmptyListClears_WithoutARevisionBump()
    {
        var ct = TestContext.Current.CancellationToken;
        var (controller, db, _) = BenchmarkComplianceGuardTests.CreateTestBenchmarkController();
        var (suite, _, _, _) = await BenchmarkComplianceGuardTests.SeedConfigsAndSuite(db);
        var q = suite.Questions.Single();
        q.EvidenceJson = HumanEvidenceJson;
        await db.SaveChangesAsync(ct);

        var kept = ReadResult(await controller.ImportQuestions(suite.Id, new ImportBenchmarkQuestionsRequest
        {
            Items = { new() { QuestionId = q.Id, QuestionText = "Q1", Evidence = null } }
        }, ct));
        Assert.Equal(1, kept.UnchangedCount);
        Assert.Equal(HumanEvidenceJson, (await db.BenchmarkQuestions.AsNoTracking().SingleAsync(x => x.Id == q.Id, ct)).EvidenceJson);

        var cleared = ReadResult(await controller.ImportQuestions(suite.Id, new ImportBenchmarkQuestionsRequest
        {
            Items = { new() { QuestionId = q.Id, Evidence = new List<BenchmarkEvidenceUnit>() } }
        }, ct));
        Assert.Equal(1, cleared.ReplacedCount);
        Assert.Null(cleared.Questions.Single().Evidence);
        var stored = await db.BenchmarkQuestions.AsNoTracking().SingleAsync(x => x.Id == q.Id, ct);
        Assert.Null(stored.EvidenceJson);
        Assert.Equal(1, stored.ItemRevision);
        Assert.Equal(25, stored.AssessedDifficulty);
    }

    [Fact]
    public async Task ChangedRubricWithEvidence_BumpsItemRevision_AndStoresTheEvidence()
    {
        var ct = TestContext.Current.CancellationToken;
        var (controller, db, _) = BenchmarkComplianceGuardTests.CreateTestBenchmarkController();
        var (suite, _, _, _) = await BenchmarkComplianceGuardTests.SeedConfigsAndSuite(db);
        var q = suite.Questions.Single();

        var dto = ReadResult(await controller.ImportQuestions(suite.Id, new ImportBenchmarkQuestionsRequest
        {
            Items = { new() { QuestionId = q.Id, ExpectedPoints = "- new rubric", ReplaceExpectedPoints = true, Evidence = HumanEvidence() } }
        }, ct));

        Assert.Equal(1, dto.ReplacedCount);
        var stored = await db.BenchmarkQuestions.AsNoTracking().SingleAsync(x => x.Id == q.Id, ct);
        Assert.Equal(2, stored.ItemRevision);
        Assert.Null(stored.AssessedDifficulty);
        Assert.Equal("- new rubric", stored.ExpectedPoints);
        Assert.Equal(HumanEvidenceJson, stored.EvidenceJson);
    }

    [Fact]
    public async Task ANewQuestion_IsCreatedWithItsEvidence()
    {
        var ct = TestContext.Current.CancellationToken;
        var (controller, db, _) = BenchmarkComplianceGuardTests.CreateTestBenchmarkController();
        var (suite, _, _, _) = await BenchmarkComplianceGuardTests.SeedConfigsAndSuite(db);

        var dto = ReadResult(await controller.ImportQuestions(suite.Id, new ImportBenchmarkQuestionsRequest
        {
            Items = { new() { QuestionText = "New", Evidence = HumanEvidence() } }
        }, ct));

        Assert.Equal(1, dto.CreatedCount);
        var created = await db.BenchmarkQuestions.AsNoTracking().SingleAsync(x => x.BenchmarkSuiteId == suite.Id && x.QuestionText == "New", ct);
        Assert.Equal(HumanEvidenceJson, created.EvidenceJson);
        Assert.Equal(1, created.ItemRevision);
    }

    [Fact]
    public async Task InvalidEvidence_Returns400_AndWritesNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var (controller, db, _) = BenchmarkComplianceGuardTests.CreateTestBenchmarkController();
        var (suite, _, _, _) = await BenchmarkComplianceGuardTests.SeedConfigsAndSuite(db);
        var q = suite.Questions.Single();

        var result = await controller.ImportQuestions(suite.Id, new ImportBenchmarkQuestionsRequest
        {
            Items =
            {
                new() { QuestionText = "Would be created" },
                new()
                {
                    QuestionId = q.Id,
                    ExpectedPoints = "- would be replaced",
                    ReplaceExpectedPoints = true,
                    Evidence = new List<BenchmarkEvidenceUnit> { new() { Kind = BenchmarkEvidenceUnit.WikiKind, Path = "Races\\Human.md" } }
                }
            }
        }, ct);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal($"Question {q.Id}: Evidence unit 1: path 'Races\\Human.md' must be repository-relative with forward slashes.", bad.Value);
        Assert.Equal(1, await db.BenchmarkQuestions.CountAsync(x => x.BenchmarkSuiteId == suite.Id, ct));
        var stored = await db.BenchmarkQuestions.AsNoTracking().SingleAsync(x => x.Id == q.Id, ct);
        Assert.Null(stored.EvidenceJson);
        Assert.Null(stored.ExpectedPoints);
    }

    [Fact]
    public async Task InvalidEvidenceOnANewQuestion_Returns400WithoutAQuestionId()
    {
        var ct = TestContext.Current.CancellationToken;
        var (controller, db, _) = BenchmarkComplianceGuardTests.CreateTestBenchmarkController();
        var (suite, _, _, _) = await BenchmarkComplianceGuardTests.SeedConfigsAndSuite(db);

        var result = await controller.ImportQuestions(suite.Id, new ImportBenchmarkQuestionsRequest
        {
            Items =
            {
                new()
                {
                    QuestionText = "New",
                    Evidence = new List<BenchmarkEvidenceUnit> { new() { Kind = BenchmarkEvidenceUnit.WikiKind, Path = "Races/Human.md", Lines = "1-5" } }
                }
            }
        }, ct);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Evidence unit 1: lines apply to source units only.", bad.Value);
        Assert.Equal(1, await db.BenchmarkQuestions.CountAsync(x => x.BenchmarkSuiteId == suite.Id, ct));
    }
}
