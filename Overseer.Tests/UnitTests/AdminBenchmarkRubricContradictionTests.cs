namespace Overseer.Tests.UnitTests;

using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Controllers;
using Overseer.Models;
using Overseer.Services.Benchmarking;
using Overseer.Tests.Helpers;
using Xunit;

/// <summary>
/// The Rubric gaps report's rubric contradictions: sentences graders docked against the rubric that
/// the claim verifier supported with a citation, aggregated over the suite's runs at each question's
/// current item revision.
/// </summary>
public class AdminBenchmarkRubricContradictionTests
{
    private const string ClericalRow = "| Clerical | Wisdom | No |";
    private const string MovementRow = "| Movement | Intelligence or Wisdom | No |";

    private static ApplicationDbContext CreateDb()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options);

    // The rubric-gaps action touches only the DbContext.
    private static AdminBenchmarkController CreateController(ApplicationDbContext db)
        => new(
            db, null!, null!, null!, null!, null!, null!, null!, null!, null!,
            null!, null!, null!, null!, null!, null!, null!, null!, null!);

    private static BenchmarkClaimVerification RubricCited(
        string claim,
        BenchmarkClaimVerdict verdict,
        string? rubricQuote,
        string? citation = "src/spell.c:1210",
        bool rubricCited = true)
        => new(0, claim, verdict, citation, "Clerical spells use Wisdom alone.")
        {
            Roles = new[] { BenchmarkClaimRoles.AccusedQuote },
            QuotedFragments = new[] { "Wisdom" },
            RubricCited = rubricCited ? true : null,
            RubricQuote = rubricCited ? rubricQuote : null
        };

    private static BenchmarkRun Run(long suiteId, int itemRevisionUsed, BenchmarkQuestion question, params BenchmarkClaimVerification[] verifications)
    {
        var run = new BenchmarkRun
        {
            BenchmarkSuiteId = suiteId,
            SuiteName = "GnollHack Player Assistance Benchmark Suite",
            TestedModelSnapshot = BenchmarkModelSnapshots.Model(),
            AssessorModelSnapshot = BenchmarkModelSnapshots.Assessor(),
            Status = BenchmarkRunStatus.Completed,
            StartedAtUtc = new DateTime(2026, 10, 3, 16, 0, 0, DateTimeKind.Utc)
        };
        run.Answers.Add(new BenchmarkRunAnswer
        {
            BenchmarkQuestionId = question.Id,
            OrderIndex = question.OrderIndex,
            ItemRevisionUsed = itemRevisionUsed,
            QuestionText = question.QuestionText,
            AnswerText = "Answer",
            Status = BenchmarkAnswerStatus.Ok,
            ClaimVerificationJson = JsonSerializer.Serialize(verifications)
        });
        return run;
    }

    [Fact]
    public async Task RubricGaps_AggregatesRubricContradictionsOverRuns_AtTheCurrentItemRevisionOnly()
    {
        await using var db = CreateDb();
        var suite = new BenchmarkSuite { Name = "GnollHack Player Assistance Benchmark Suite", Description = "Desc" };
        var q9 = new BenchmarkQuestion
        {
            QuestionText = "Explain GnollHack's spell system.",
            OrderIndex = 9,
            Difficulty = BenchmarkDifficulty.Intermediate,
            ItemRevision = 2
        };
        suite.Questions.Add(q9);
        db.BenchmarkSuites.Add(suite);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        db.BenchmarkRuns.AddRange(
            // Two runs at the current revision raise the Clerical row; the first also raises Movement.
            Run(suite.Id, 2, q9,
                RubricCited(ClericalRow, BenchmarkClaimVerdict.Supported, "Wis/Cha"),
                RubricCited(MovementRow, BenchmarkClaimVerdict.Supported, null) with { ClaimIndex = 1, QuotedFragments = null }),
            Run(suite.Id, 2, q9,
                RubricCited(ClericalRow, BenchmarkClaimVerdict.Supported, "Wis/Cha"),
                // Refuted, uncited, or not charged against the rubric: none is a contradiction.
                RubricCited(MovementRow, BenchmarkClaimVerdict.Refuted, null) with { ClaimIndex = 1 },
                RubricCited("| Arcane | Intelligence | Yes |", BenchmarkClaimVerdict.Supported, "Int", citation: null) with { ClaimIndex = 2 },
                RubricCited("| Healing | Wisdom | No |", BenchmarkClaimVerdict.Supported, null, rubricCited: false) with { ClaimIndex = 3 }),
            // A run at the previous revision graded the rubric since repaired.
            Run(suite.Id, 1, q9,
                RubricCited("| Nature | Wisdom or Charisma | No |", BenchmarkClaimVerdict.Supported, "Wis")));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateController(db).GetRubricGaps(suite.Id);

        var report = Assert.IsType<BenchmarkRubricGapReportDto>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(2, report.RubricContradictions.Count);

        var clerical = report.RubricContradictions[0];
        Assert.Equal(q9.Id, clerical.QuestionId);
        Assert.Equal(9, clerical.QuestionOrderIndex);
        Assert.Equal(ClericalRow, clerical.ChargedSentence);
        Assert.Equal(new[] { "Wisdom" }, clerical.ChargedParts);
        Assert.Equal("Wis/Cha", clerical.RubricQuote);
        Assert.Equal("src/spell.c:1210", clerical.Citation);
        Assert.Equal("Clerical spells use Wisdom alone.", clerical.Basis);
        Assert.Equal(2, clerical.RunCount);

        var movement = report.RubricContradictions[1];
        Assert.Equal(MovementRow, movement.ChargedSentence);
        Assert.Empty(movement.ChargedParts);
        Assert.Null(movement.RubricQuote);
        Assert.Equal(1, movement.RunCount);

        Assert.DoesNotContain(report.RubricContradictions, r => r.ChargedSentence.Contains("Nature", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RubricGaps_WithoutAnyRubricContradiction_ReturnsAnEmptyList()
    {
        await using var db = CreateDb();
        var suite = new BenchmarkSuite { Name = "Suite", Description = "Desc" };
        db.BenchmarkSuites.Add(suite);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateController(db).GetRubricGaps(suite.Id);

        var report = Assert.IsType<BenchmarkRubricGapReportDto>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Empty(report.RubricContradictions);
    }
}
