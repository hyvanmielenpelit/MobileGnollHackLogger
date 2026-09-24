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
/// The exam a set of runs sat, built from their own answer rows. Also the seeded suite, runs and
/// suite mutations the service-level isolation tests in <see cref="BenchmarkGroupStatisticsTests"/>
/// and <see cref="BenchmarkModelComparisonServiceTests"/> share.
/// </summary>
public class BenchmarkRunExamTests
{
    private static BenchmarkRunAnswer Answer(
        long questionId, int orderIndex, string text, int? revision = 1, int? quality = 80, int? weight = 50,
        bool linkedByForeignKey = true)
        => new()
        {
            BenchmarkQuestionId = linkedByForeignKey ? questionId : null,
            BenchmarkQuestionIdUsed = questionId,
            ItemRevisionUsed = revision,
            OrderIndex = orderIndex,
            QuestionText = text,
            AnswerText = "A",
            ExpectedPointsUsed = $"- rubric of {text}",
            ExpectedPointsRecorded = true,
            Status = BenchmarkAnswerStatus.Ok,
            AssessmentStatus = BenchmarkAssessmentStatus.Scored,
            QualityScore = quality,
            AssessedDifficulty = weight,
            Difficulty = BenchmarkDifficulty.Intermediate
        };

    private static BenchmarkRun Run(long id, DateTime startedAt, params BenchmarkRunAnswer[] answers)
    {
        var run = BenchmarkModelSnapshots.Attach(new BenchmarkRun
        {
            Id = id,
            BenchmarkSuiteId = 5,
            BenchmarkSuiteIdUsed = 5,
            SuiteName = $"Suite as of run {id}",
            StartedAtUtc = startedAt
        });
        foreach (var a in answers)
        {
            a.BenchmarkRunId = id;
            run.Answers.Add(a);
        }
        return run;
    }

    private static readonly DateTime T0 = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void TheExam_HoldsTheQuestionsTheRunsWereAsked_InTheirOrder()
    {
        var runs = new[]
        {
            Run(1, T0, Answer(11, 2, "Q2"), Answer(10, 1, "Q1"), Answer(12, 3, "Q3"))
        };

        var (suite, questions) = BenchmarkRunExam.Build(runs);

        Assert.Equal(new long[] { 10, 11, 12 }, questions.Select(q => q.Id));
        Assert.Equal(new[] { "Q1", "Q2", "Q3" }, questions.Select(q => q.QuestionText));
        Assert.Equal("- rubric of Q2", questions[1].ExpectedPoints);
        Assert.Equal(5, suite.Id);
        Assert.Equal("Suite as of run 1", suite.Name);
    }

    [Fact]
    public void ADeletedQuestion_StaysInTheExam_AndAnUnlinkedAnswerIsExcluded()
    {
        var unlinked = Answer(0, 3, "Q3");
        unlinked.BenchmarkQuestionId = null;
        unlinked.BenchmarkQuestionIdUsed = null;

        var runs = new[]
        {
            Run(1, T0, Answer(10, 1, "Q1"), Answer(11, 2, "Q2", linkedByForeignKey: false), unlinked)
        };

        var (_, questions) = BenchmarkRunExam.Build(runs);

        Assert.Equal(new long[] { 10, 11 }, questions.Select(q => q.Id));
    }

    [Fact]
    public void AQuestionAddedToTheLiveSuiteLater_IsNotPartOfTheExam()
    {
        // The exam is built from answer rows alone; a question nobody answered has none.
        var runs = new[] { Run(1, T0, Answer(10, 1, "Q1"), Answer(11, 2, "Q2")) };

        var (_, questions) = BenchmarkRunExam.Build(runs);

        Assert.Equal(2, questions.Count);
        Assert.DoesNotContain(questions, q => q.Id == 12);
    }

    [Fact]
    public void ItemRevision_IsTheGradedRevision_AndMixedRevisionsGiveTheHighest()
    {
        var runs = new[]
        {
            Run(1, T0, Answer(10, 1, "Q1", revision: 1), Answer(11, 2, "Q2", revision: 1)),
            Run(2, T0.AddHours(1), Answer(10, 1, "Q1", revision: 2), Answer(11, 2, "Q2", revision: 1))
        };

        var (_, questions) = BenchmarkRunExam.Build(runs);

        Assert.Equal(2, questions.Single(q => q.Id == 10).ItemRevision);
        Assert.Equal(1, questions.Single(q => q.Id == 11).ItemRevision);
    }

    [Fact]
    public void ItemRevision_PrefersAnswersThatCount_AndIsZeroWhenUnknown()
    {
        // The revision-3 answer failed at the provider, so it does not count; the counted one is 2.
        var failed = Answer(10, 1, "Q1", revision: 3, quality: null);
        failed.Status = BenchmarkAnswerStatus.ProviderError;

        var runs = new[]
        {
            Run(1, T0, Answer(10, 1, "Q1", revision: 2), Answer(11, 2, "Q2", revision: null)),
            Run(2, T0.AddHours(1), failed, Answer(11, 2, "Q2", revision: null))
        };

        var (_, questions) = BenchmarkRunExam.Build(runs);

        Assert.Equal(2, questions.Single(q => q.Id == 10).ItemRevision);
        Assert.Equal(0, questions.Single(q => q.Id == 11).ItemRevision);
    }

    [Fact]
    public void TextOrderAndRubric_ComeFromTheNewestRun()
    {
        var older = Answer(10, 4, "Old wording");
        older.ExpectedPointsUsed = "- old rubric";
        var newer = Answer(10, 1, "New wording");
        newer.ExpectedPointsUsed = "- new rubric";

        var runs = new[]
        {
            Run(2, T0.AddHours(1), newer),
            Run(1, T0, older)
        };

        var (suite, questions) = BenchmarkRunExam.Build(runs);

        var q = Assert.Single(questions);
        Assert.Equal("New wording", q.QuestionText);
        Assert.Equal(1, q.OrderIndex);
        Assert.Equal("- new rubric", q.ExpectedPoints);
        Assert.Equal("Suite as of run 2", suite.Name);
    }

    [Fact]
    public void AssessedDifficulty_IsTheRoundedMeanOfTheAnswers()
    {
        var runs = new[]
        {
            Run(1, T0, Answer(10, 1, "Q1", weight: 40), Answer(11, 2, "Q2", weight: null)),
            Run(2, T0.AddHours(1), Answer(10, 1, "Q1", weight: 45), Answer(11, 2, "Q2", weight: null))
        };

        var (_, questions) = BenchmarkRunExam.Build(runs);

        Assert.Equal(43, questions.Single(q => q.Id == 10).AssessedDifficulty);
        Assert.Null(questions.Single(q => q.Id == 11).AssessedDifficulty);
    }

    [Fact]
    public void QuestionKey_PrefersTheRetainedId_OverTheForeignKey()
    {
        Assert.Equal(7L, BenchmarkItemAnalysis.QuestionKey(new BenchmarkRunAnswer { BenchmarkQuestionIdUsed = 7, BenchmarkQuestionId = null }));
        Assert.Equal(8L, BenchmarkItemAnalysis.QuestionKey(new BenchmarkRunAnswer { BenchmarkQuestionIdUsed = null, BenchmarkQuestionId = 8 }));
        Assert.Null(BenchmarkItemAnalysis.QuestionKey(new BenchmarkRunAnswer()));
    }

    // --- Shared seeding for the service-level isolation tests -------------------------------------

    internal sealed record SeededSuite(long SuiteId, long GroupId, long[] RunIds, long[] QuestionIds);

    internal static DbContextOptions<ApplicationDbContext> InMemoryOptions() =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

    /// <summary>
    /// A three-question suite, three comparable completed runs that answered all three, and a group
    /// over them, in a context of its own.
    /// </summary>
    internal static async Task<SeededSuite> SeedSuiteWithRunsAsync(DbContextOptions<ApplicationDbContext> options)
    {
        await using var db = new ApplicationDbContext(options);

        var suite = new BenchmarkSuite { Name = "Isolation Suite" };
        for (int i = 1; i <= 3; i++)
        {
            suite.Questions.Add(new BenchmarkQuestion
            {
                QuestionText = $"Q{i}",
                ExpectedPoints = $"- point {i}",
                OrderIndex = i,
                ItemRevision = 1,
                AssessedDifficulty = 50,
                Difficulty = BenchmarkDifficulty.Intermediate
            });
        }
        db.BenchmarkSuites.Add(suite);
        await db.SaveChangesAsync();

        var questions = suite.Questions.OrderBy(q => q.OrderIndex).ToList();
        var scores = new[] { new[] { 60, 70, 80 }, new[] { 70, 80, 90 }, new[] { 80, 90, 100 } };
        var runs = new List<BenchmarkRun>();

        for (int r = 0; r < 3; r++)
        {
            var run = new BenchmarkRun
            {
                BenchmarkSuiteId = suite.Id,
                BenchmarkSuiteIdUsed = suite.Id,
                SuiteName = suite.Name,
                Status = BenchmarkRunStatus.Completed,
                StartedAtUtc = T0.AddHours(r),
                CompletedAtUtc = T0.AddHours(r).AddMinutes(30),
                SpeedIndex = 100,
                TestedModelSnapshot = BenchmarkModelSnapshots.Model(
                    provider: "OpenAI", modelId: "gpt-5.6-luna", displayName: "gpt-5.6-luna", thinkingLevel: "high"),
                AssessorModelSnapshot = BenchmarkModelSnapshots.Model(provider: "Google", modelId: "gemini-3.7-pro"),
                CandidatePromptOptionsJson = "{\"verboseMode\":false,\"spoilerFreeMode\":false,\"overseerMode\":0}",
                CandidateSystemPromptSha256 = "e9b3e9a7c4d1b8f0a2e6c9d3b7f1a4e8",
                ToolGuidesSha256 = "f59d8b30a1c7e4d2b6f0a8c3e9d5b1f7",
                KnowledgeBaseHeadSha = "576ca574b2e8d0f6a4c2e8d4b0f6a2c8",
                HarnessVersion = "12",
                ScoringMethodVersion = 9,
                MaxParallelQuestionsUsed = 1,
                TotalQuestionCount = 3
            };

            for (int i = 0; i < questions.Count; i++)
            {
                run.Answers.Add(new BenchmarkRunAnswer
                {
                    BenchmarkQuestionId = questions[i].Id,
                    BenchmarkQuestionIdUsed = questions[i].Id,
                    ItemRevisionUsed = 1,
                    ExpectedPointsUsed = questions[i].ExpectedPoints,
                    ExpectedPointsRecorded = true,
                    OrderIndex = questions[i].OrderIndex,
                    QuestionText = questions[i].QuestionText,
                    AnswerText = "An answer.",
                    Difficulty = questions[i].Difficulty,
                    AssessedDifficulty = 50,
                    Status = BenchmarkAnswerStatus.Ok,
                    AssessmentStatus = BenchmarkAssessmentStatus.Scored,
                    QualityScore = scores[r][i],
                    AccuracyScore = scores[r][i],
                    SpeedScore = 100,
                    DurationMs = 30000,
                    ToolTimeMs = 0,
                    TimeToFirstTokenMs = 900
                });
            }

            runs.Add(run);
        }

        db.BenchmarkRuns.AddRange(runs);
        await db.SaveChangesAsync();

        var group = new BenchmarkRunGroup { Name = "Isolation group", BenchmarkSuiteId = suite.Id };
        foreach (var run in runs)
        {
            group.Members.Add(new BenchmarkRunGroupMember { BenchmarkRunId = run.Id });
        }
        db.BenchmarkRunGroups.Add(group);
        await db.SaveChangesAsync();

        return new SeededSuite(suite.Id, group.Id, runs.Select(r => r.Id).ToArray(), questions.Select(q => q.Id).ToArray());
    }

    /// <summary>
    /// Every suite operation an old run must survive, done the way the admin endpoints do them: edit a
    /// rubric and bump its revision, delete a question with its answers' foreign key set null, add a
    /// question, reorder, and rename the suite.
    /// </summary>
    internal static async Task MutateSuiteEveryWayAsync(DbContextOptions<ApplicationDbContext> options, SeededSuite seeded)
    {
        await using var db = new ApplicationDbContext(options);
        var suite = await db.BenchmarkSuites.Include(s => s.Questions).FirstAsync(s => s.Id == seeded.SuiteId);
        var questions = suite.Questions.OrderBy(q => q.OrderIndex).ToList();

        questions[0].ExpectedPoints = "- an entirely different answer key";
        questions[0].ItemRevision++;

        var deleted = questions[1];
        foreach (var answer in db.BenchmarkRunAnswers.Where(a => a.BenchmarkQuestionId == deleted.Id))
        {
            answer.BenchmarkQuestionId = null;
        }
        db.BenchmarkQuestions.Remove(deleted);

        suite.Questions.Add(new BenchmarkQuestion { QuestionText = "Q4", ExpectedPoints = "- point 4", OrderIndex = 4, ItemRevision = 1 });

        questions[0].OrderIndex = 3;
        questions[2].OrderIndex = 1;

        suite.Name = "Renamed Suite";
        await db.SaveChangesAsync();
    }

    /// <summary>Deletes the suite the way <c>DeleteSuite</c> does: every link to it nulled by hand first.</summary>
    internal static async Task DeleteSuiteAsync(DbContextOptions<ApplicationDbContext> options, SeededSuite seeded)
    {
        await using var db = new ApplicationDbContext(options);
        var suite = await db.BenchmarkSuites.Include(s => s.Questions).FirstAsync(s => s.Id == seeded.SuiteId);

        foreach (var run in db.BenchmarkRuns.Where(r => r.BenchmarkSuiteId == suite.Id)) run.BenchmarkSuiteId = null;
        foreach (var group in db.BenchmarkRunGroups.Where(g => g.BenchmarkSuiteId == suite.Id)) group.BenchmarkSuiteId = null;
        var questionIds = suite.Questions.Select(q => q.Id).ToList();
        foreach (var answer in db.BenchmarkRunAnswers.Where(a => a.BenchmarkQuestionId != null && questionIds.Contains(a.BenchmarkQuestionId.Value)))
        {
            answer.BenchmarkQuestionId = null;
        }

        db.BenchmarkSuites.Remove(suite);
        await db.SaveChangesAsync();
    }
}
