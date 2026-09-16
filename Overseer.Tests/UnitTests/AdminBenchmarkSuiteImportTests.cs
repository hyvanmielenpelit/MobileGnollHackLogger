namespace Overseer.Tests.UnitTests;

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Xunit;

/// <summary>POST suites/import: always creates a new suite from an imported YAML document.</summary>
public class AdminBenchmarkSuiteImportTests
{
    private static ImportBenchmarkSuiteRequest Request(string name, int questionCount) => new()
    {
        Name = name,
        Description = "  Imported description  ",
        Questions = Enumerable.Range(1, questionCount)
            .Select(i => new ImportBenchmarkQuestionItem
            {
                QuestionText = $"Question {i}",
                Difficulty = i == 2 ? BenchmarkDifficulty.Intermediate : null,
                ExpectedPoints = $"- point {i}",
                ReplaceExpectedPoints = true
            })
            .ToList()
    };

    [Fact]
    public async Task CreatesSuiteWithQuestionsInOrder_NotGenerated_NoSnapshot()
    {
        var ct = TestContext.Current.CancellationToken;
        var (controller, db, _) = BenchmarkComplianceGuardTests.CreateTestBenchmarkController();

        var result = await controller.ImportSuite(Request("  YAML Suite  ", 3));

        var dto = Assert.IsType<BenchmarkSuiteDto>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal("YAML Suite", dto.Name);
        Assert.Equal(3, dto.QuestionCount);
        Assert.Null(dto.GameSnapshotId);
        Assert.Null(dto.DefaultSuiteKey);

        var suite = await db.BenchmarkSuites.Include(s => s.Questions).SingleAsync(s => s.Id == dto.Id, ct);
        Assert.Equal("Imported description", suite.Description);
        var questions = suite.Questions.OrderBy(q => q.OrderIndex).ToList();
        Assert.Equal(new[] { 1, 2, 3 }, questions.Select(q => q.OrderIndex).ToArray());
        Assert.Equal(new[] { "Question 1", "Question 2", "Question 3" }, questions.Select(q => q.QuestionText).ToArray());
        Assert.Equal(BenchmarkDifficulty.Simple, questions[0].Difficulty);
        Assert.Equal(BenchmarkDifficulty.Intermediate, questions[1].Difficulty);
        Assert.All(questions, q => Assert.False(q.IsGenerated));
    }

    [Fact]
    public async Task BlankName_Returns400()
    {
        var (controller, _, _) = BenchmarkComplianceGuardTests.CreateTestBenchmarkController();
        var bad = Assert.IsType<BadRequestObjectResult>(await controller.ImportSuite(Request("  ", 1)));
        Assert.Equal("Suite name is required.", bad.Value);
    }

    [Fact]
    public async Task OverlongName_Returns400()
    {
        var (controller, _, _) = BenchmarkComplianceGuardTests.CreateTestBenchmarkController();
        var bad = Assert.IsType<BadRequestObjectResult>(await controller.ImportSuite(Request(new string('x', 129), 1)));
        Assert.Equal("Suite name must be at most 128 characters.", bad.Value);
    }

    [Fact]
    public async Task NameCollision_GetsImportedSuffix()
    {
        var (controller, db, _) = BenchmarkComplianceGuardTests.CreateTestBenchmarkController();
        await BenchmarkComplianceGuardTests.SeedConfigsAndSuite(db);

        var first = Assert.IsType<BenchmarkSuiteDto>(Assert.IsType<OkObjectResult>(await controller.ImportSuite(Request("Test Suite", 1))).Value);
        var second = Assert.IsType<BenchmarkSuiteDto>(Assert.IsType<OkObjectResult>(await controller.ImportSuite(Request("Test Suite", 1))).Value);

        Assert.Equal("Test Suite (Imported)", first.Name);
        Assert.Equal("Test Suite (Imported 2)", second.Name);
    }

    [Fact]
    public async Task OverTheGuardLimit_Returns400_AndPersistsNoSuite()
    {
        var ct = TestContext.Current.CancellationToken;
        var (controller, db, _) = BenchmarkComplianceGuardTests.CreateTestBenchmarkController(maxQuestions: 2);

        var bad = Assert.IsType<BadRequestObjectResult>(await controller.ImportSuite(Request("Too Big", 3)));

        Assert.Contains("Suite question limit reached (2 questions maximum)", bad.Value?.ToString());
        Assert.Equal(0, await db.BenchmarkSuites.CountAsync(ct));
    }

    [Fact]
    public async Task QuestionWithoutText_Returns400()
    {
        var (controller, _, _) = BenchmarkComplianceGuardTests.CreateTestBenchmarkController();
        var request = Request("Blank Question", 2);
        request.Questions[1].QuestionText = null;

        var bad = Assert.IsType<BadRequestObjectResult>(await controller.ImportSuite(request));
        Assert.Equal("Question text is required for a new question.", bad.Value);
    }

    [Fact]
    public async Task EmptyQuestionList_Returns400()
    {
        var (controller, _, _) = BenchmarkComplianceGuardTests.CreateTestBenchmarkController();
        var bad = Assert.IsType<BadRequestObjectResult>(await controller.ImportSuite(
            new ImportBenchmarkSuiteRequest { Name = "Empty", Questions = new List<ImportBenchmarkQuestionItem>() }));
        Assert.Equal("A suite needs at least one question.", bad.Value);
    }
}
