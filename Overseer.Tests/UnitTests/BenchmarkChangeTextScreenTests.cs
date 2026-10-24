namespace Overseer.Tests.UnitTests;

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Services.Benchmarking;
using Overseer.Tests.Helpers;
using Xunit;

/// <summary>
/// The overlap check of the experimental-change editor: a run of eight or more consecutive words a
/// text shares with a benchmark question or its rubric, compared as lower-cased words with
/// punctuation and spacing ignored.
/// </summary>
public sealed class BenchmarkChangeTextScreenTests
{
    private static readonly BenchmarkScreenQuestion[] Questions =
    {
        new(11, 1, "Gnoll Basics", 2,
            "Which artifact does a healer receive at the end of the quest in GnollHack?",
            "The quest artifact of the healer is the Staff of Aesculapius, which drains life."),
        new(12, 2, "Other Suite", 0, "How heavy is a boulder?", null)
    };

    [Fact]
    public void AnEightWordRunSharedWithARubric_IsFoundAndQuoted()
    {
        var overlap = Assert.Single(BenchmarkChangeTextScreen.FindOverlaps(
            new[] { "Always say: the quest artifact of the healer is the staff." }, Questions));

        Assert.Equal("the quest artifact of the healer is the staff", overlap.Text);
        Assert.Equal((1L, "Gnoll Basics", 11L, 3, BenchmarkChangeTextScreen.RubricField),
            (overlap.SuiteId, overlap.SuiteName, overlap.QuestionId, overlap.QuestionNumber, overlap.Field));
    }

    [Fact]
    public void AnEightWordRunSharedWithAQuestion_IsFound()
    {
        var overlap = Assert.Single(BenchmarkChangeTextScreen.FindOverlaps(
            new[] { "When asked which artifact does a healer receive at the end, read the role page." }, Questions));

        Assert.Equal("which artifact does a healer receive at the end", overlap.Text);
        Assert.Equal(BenchmarkChangeTextScreen.QuestionField, overlap.Field);
    }

    [Fact]
    public void SevenSharedWords_AreNotAnOverlap()
    {
        Assert.Empty(BenchmarkChangeTextScreen.FindOverlaps(
            new[] { "Note that the quest artifact of the healer is lost." }, Questions));
    }

    [Fact]
    public void PunctuationCaseAndSpacing_AreIgnored()
    {
        var overlap = Assert.Single(BenchmarkChangeTextScreen.FindOverlaps(
            new[] { "THE QUEST—ARTIFACT, of   the\r\nhealer; is (the) STAFF!" }, Questions));

        Assert.Equal("the quest artifact of the healer is the staff", overlap.Text);
    }

    [Fact]
    public void EachQuestionFieldIsReportedOnce_AndAtMostTenFindingsAreReturned()
    {
        var many = Enumerable.Range(0, 15)
            .Select(i => new BenchmarkScreenQuestion(100 + i, 9, "Many", i, null, "one two three four five six seven eight nine"))
            .ToArray();

        var findings = BenchmarkChangeTextScreen.FindOverlaps(
            new[] { "one two three four five six seven eight nine", "one two three four five six seven eight" }, many);

        Assert.Equal(BenchmarkChangeTextScreen.MaxFindings, findings.Count);
        Assert.Equal(findings.Count, findings.Select(f => f.QuestionId).Distinct().Count());
        Assert.All(findings, f => Assert.Equal("one two three four five six seven eight nine", f.Text));
    }

    [Fact]
    public async Task TheScreen_ReadsEveryStoredQuestion()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var connection = SqliteSchemaTemplate.OpenCopy();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var db = new ApplicationDbContext(options);
        var suite = new BenchmarkSuite { Name = "Stored Suite", Description = "Desc" };
        suite.Questions.Add(new BenchmarkQuestion
        {
            QuestionText = "Q",
            OrderIndex = 0,
            ExpectedPoints = "A dwarvish mattock deals d12 damage to small monsters and large ones.",
            Difficulty = BenchmarkDifficulty.Simple
        });
        db.BenchmarkSuites.Add(suite);
        await db.SaveChangesAsync(ct);

        var screen = new BenchmarkChangeTextScreen(db);

        var overlap = Assert.Single(await screen.ScreenAsync(new[] { "A dwarvish mattock deals d12 damage to small monsters.", null, " " }, ct));
        Assert.Equal(("Stored Suite", 1), (overlap.SuiteName, overlap.QuestionNumber));
        Assert.Empty(await screen.ScreenAsync(Array.Empty<string?>(), ct));
    }
}
