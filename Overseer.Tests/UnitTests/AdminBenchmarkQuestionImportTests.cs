namespace Overseer.Tests.UnitTests;

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Models;
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
}
