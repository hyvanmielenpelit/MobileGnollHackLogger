namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Controllers;
using Overseer.Models;
using Overseer.Services.Benchmarking;
using Xunit;

/// <summary>
/// POST suites/{suiteId}/questions and PUT questions/{id} with gold evidence: null keeps, an empty
/// list clears, and evidence alone bumps no item revision.
/// </summary>
public class AdminBenchmarkQuestionEditEvidenceTests
{
    private const string HumanEvidenceJson = "[{\"kind\":\"wiki\",\"path\":\"Races/Human.md\",\"personal\":true}]";

    private static readonly DateTime Earlier = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

    private static List<BenchmarkEvidenceUnit> HumanEvidence() => new()
    {
        new() { Kind = BenchmarkEvidenceUnit.WikiKind, Path = "Races/Human.md", Personal = true }
    };

    private static BenchmarkQuestionDto ReadQuestion(IActionResult result)
        => Assert.IsType<BenchmarkQuestionDto>(Assert.IsType<OkObjectResult>(result).Value);

    /// <summary>The seeded question, reviewed at revision 1 with a rubric and an old modification time.</summary>
    private static async Task<(AdminBenchmarkController controller, ApplicationDbContext db, BenchmarkSuite suite, BenchmarkQuestion q)> SeedReviewedQuestion(
        string? evidenceJson = null)
    {
        var ct = TestContext.Current.CancellationToken;
        var (controller, db, _) = BenchmarkComplianceGuardTests.CreateTestBenchmarkController();
        var (suite, _, _, _) = await BenchmarkComplianceGuardTests.SeedConfigsAndSuite(db);
        var q = suite.Questions.Single();
        q.ExpectedPoints = "- the rubric";
        q.ReviewedAtRevision = 1;
        q.EvidenceJson = evidenceJson;
        q.ModifiedAtUtc = Earlier;
        suite.ModifiedAtUtc = Earlier;
        await db.SaveChangesAsync(ct);
        return (controller, db, suite, q);
    }

    private static UpdateBenchmarkQuestionRequest SameContent(List<BenchmarkEvidenceUnit>? evidence) => new()
    {
        QuestionText = "Q1",
        Difficulty = BenchmarkDifficulty.Simple,
        ExpectedPoints = "- the rubric",
        Evidence = evidence
    };

    [Fact]
    public async Task EvidenceOnlyUpdate_StoresTheEvidence_AndKeepsRevisionAssessmentAndReviewMark()
    {
        var ct = TestContext.Current.CancellationToken;
        var (controller, db, suite, q) = await SeedReviewedQuestion();

        var dto = ReadQuestion(await controller.UpdateQuestion(q.Id, SameContent(HumanEvidence())));

        var unit = Assert.Single(dto.Evidence!);
        Assert.Equal("Races/Human.md", unit.Path);
        Assert.True(unit.Personal);

        var stored = await db.BenchmarkQuestions.AsNoTracking().SingleAsync(x => x.Id == q.Id, ct);
        Assert.Equal(HumanEvidenceJson, stored.EvidenceJson);
        Assert.Equal(1, stored.ItemRevision);
        Assert.Equal(25, stored.AssessedDifficulty);
        Assert.Equal(1, stored.ReviewedAtRevision);
        Assert.True(stored.ModifiedAtUtc > Earlier);
        Assert.True((await db.BenchmarkSuites.AsNoTracking().SingleAsync(s => s.Id == suite.Id, ct)).ModifiedAtUtc > Earlier);
    }

    [Fact]
    public async Task NullEvidence_KeepsTheStoredEvidence_AndDoesNotAdvanceModifiedAt()
    {
        var ct = TestContext.Current.CancellationToken;
        var (controller, db, _, q) = await SeedReviewedQuestion(HumanEvidenceJson);

        ReadQuestion(await controller.UpdateQuestion(q.Id, SameContent(null)));

        var stored = await db.BenchmarkQuestions.AsNoTracking().SingleAsync(x => x.Id == q.Id, ct);
        Assert.Equal(HumanEvidenceJson, stored.EvidenceJson);
        Assert.Equal(Earlier, stored.ModifiedAtUtc);
    }

    [Fact]
    public async Task AnEmptyList_ClearsTheEvidence()
    {
        var ct = TestContext.Current.CancellationToken;
        var (controller, db, _, q) = await SeedReviewedQuestion(HumanEvidenceJson);

        var dto = ReadQuestion(await controller.UpdateQuestion(q.Id, SameContent(new List<BenchmarkEvidenceUnit>())));

        Assert.Null(dto.Evidence);
        var stored = await db.BenchmarkQuestions.AsNoTracking().SingleAsync(x => x.Id == q.Id, ct);
        Assert.Null(stored.EvidenceJson);
        Assert.Equal(1, stored.ItemRevision);
    }

    [Theory]
    [InlineData("source", "9-3", null, "Evidence unit 1: lines '9-3' must read a-b with 1 <= a <= b.")]
    [InlineData("source", null, "Intro", "Evidence unit 1: section applies to wiki units only.")]
    public async Task InvalidEvidence_Returns400_AndChangesNothing(string kind, string? lines, string? section, string message)
    {
        var ct = TestContext.Current.CancellationToken;
        var (controller, db, _, q) = await SeedReviewedQuestion();

        var request = SameContent(new List<BenchmarkEvidenceUnit> { new() { Kind = kind, Path = "src/bones.c", Lines = lines, Section = section } });
        request.QuestionText = "Q1 rewritten";
        var result = await controller.UpdateQuestion(q.Id, request);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(message, bad.Value);
        var stored = await db.BenchmarkQuestions.AsNoTracking().SingleAsync(x => x.Id == q.Id, ct);
        Assert.Equal("Q1", stored.QuestionText);
        Assert.Null(stored.EvidenceJson);
        Assert.Equal(1, stored.ItemRevision);
        Assert.Equal(Earlier, stored.ModifiedAtUtc);
    }

    [Fact]
    public async Task ChangedRubricWithEvidence_BumpsTheRevisionOnce_AndStoresTheEvidence()
    {
        var ct = TestContext.Current.CancellationToken;
        var (controller, db, _, q) = await SeedReviewedQuestion();

        var request = SameContent(HumanEvidence());
        request.ExpectedPoints = "- a new rubric";
        ReadQuestion(await controller.UpdateQuestion(q.Id, request));

        var stored = await db.BenchmarkQuestions.AsNoTracking().SingleAsync(x => x.Id == q.Id, ct);
        Assert.Equal(2, stored.ItemRevision);
        Assert.Null(stored.AssessedDifficulty);
        Assert.Equal("- a new rubric", stored.ExpectedPoints);
        Assert.Equal(HumanEvidenceJson, stored.EvidenceJson);
    }

    [Fact]
    public async Task Create_StoresTheEvidence_AndRejectsAnInvalidUnitWithoutAddingAQuestion()
    {
        var ct = TestContext.Current.CancellationToken;
        var (controller, db, _) = BenchmarkComplianceGuardTests.CreateTestBenchmarkController();
        var (suite, _, _, _) = await BenchmarkComplianceGuardTests.SeedConfigsAndSuite(db);

        var dto = ReadQuestion(await controller.CreateQuestion(suite.Id, new CreateBenchmarkQuestionRequest
        {
            QuestionText = "New",
            Evidence = HumanEvidence()
        }));
        Assert.Single(dto.Evidence!);
        var created = await db.BenchmarkQuestions.AsNoTracking().SingleAsync(x => x.Id == dto.Id, ct);
        Assert.Equal(HumanEvidenceJson, created.EvidenceJson);

        var result = await controller.CreateQuestion(suite.Id, new CreateBenchmarkQuestionRequest
        {
            QuestionText = "Invalid",
            Evidence = new List<BenchmarkEvidenceUnit> { new() { Kind = BenchmarkEvidenceUnit.SourceKind, Path = "src/bones.c", Lines = "9-3" } }
        });

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Evidence unit 1: lines '9-3' must read a-b with 1 <= a <= b.", bad.Value);
        Assert.Equal(2, await db.BenchmarkQuestions.CountAsync(x => x.BenchmarkSuiteId == suite.Id, ct));
    }
}
