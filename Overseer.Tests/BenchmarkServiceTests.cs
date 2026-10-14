namespace Overseer.Tests;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MobileGnollHackLogger.Data;
using Overseer.Services;
using Overseer.Services.Benchmarking;
using Overseer.Tests.Helpers;
using Xunit;

public class BenchmarkServiceTests
{
    // --- 1. Answer Sanitizer Tests ---

    [Fact]
    public void Sanitize_HandlesUnclosedThoughtTagAtEnd()
    {
        string raw = "Here is the answer.\n\n<div class=\"ai-thought\">Partial trailing thoughts";
        var sanitized = BenchmarkAnswerSanitizer.Sanitize(raw);

        Assert.Equal("Here is the answer.", sanitized.AnswerText);
        Assert.Equal("Partial trailing thoughts", sanitized.ThoughtText);
    }

    [Fact]
    public void Sanitize_PreservesCodeBlocksByteForByte()
    {
        string raw = "Here is code:\n```c\n#define GNOLL_STAT 10\n\n\nint x = 1;\n```\nDone.";
        var sanitized = BenchmarkAnswerSanitizer.Sanitize(raw);

        Assert.Contains("```c\n#define GNOLL_STAT 10\n\n\nint x = 1;\n```", sanitized.AnswerText);
    }

    // --- 2. Per-Question Assessment Parser Tests ---

    [Fact]
    public void ParsePerQuestion_ClampsInvalidLevels()
    {
        string json = @"{ ""accuracyLevel"": 10, ""completenessLevel"": -2, ""concisenessLevel"": 3, ""readabilityLevel"": 2, ""criticalError"": true }";

        var parseResult = BenchmarkAssessmentParser.ParsePerQuestion(json);

        Assert.True(parseResult.Success);
        Assert.NotNull(parseResult.Result);
        Assert.Equal(6, parseResult.Result.AccuracyLevel); // clamped to 6
        Assert.Equal(0, parseResult.Result.CompletenessLevel); // clamped to 0
        Assert.True(parseResult.Result.CriticalError);
    }

    [Fact]
    public void ParsePerQuestion_DemotesCriticalErrorWithNoQuote()
    {
        // The 2026-09-03 run capped Q17 at 25 on the assessor's word that the answer "completely
        // omits the character level 3 requirement" — an omission, and nothing in the record
        // pointed at a claim the answer had actually made.
        string json = @"{ ""accuracyLevel"": 2, ""completenessLevel"": 2, ""concisenessLevel"": 4,
                          ""readabilityLevel"": 5, ""criticalError"": true,
                          ""comment"": ""Omits the character level 3 requirement."" }";

        var parseResult = BenchmarkAssessmentParser.ParsePerQuestion(json, "The gift chance is one in ten after the first.");

        Assert.True(parseResult.Success);
        Assert.NotNull(parseResult.Result);
        Assert.False(parseResult.Result.CriticalError);
        Assert.True(parseResult.Result.CriticalErrorDemoted);
        Assert.Contains("critical error not applied", parseResult.Result.Comment);
    }

    [Fact]
    public void ParsePerQuestion_DemotesCriticalErrorWhoseQuoteIsNotInTheAnswer()
    {
        string json = @"{ ""accuracyLevel"": 2, ""completenessLevel"": 2, ""concisenessLevel"": 4,
                          ""readabilityLevel"": 5, ""criticalError"": true,
                          ""criticalErrorQuote"": ""Praying at 1 HP on an unaligned altar is always safe."" }";

        var parseResult = BenchmarkAssessmentParser.ParsePerQuestion(json, "Pray only when your prayer timeout has expired.");

        Assert.NotNull(parseResult.Result);
        Assert.False(parseResult.Result.CriticalError);
        Assert.True(parseResult.Result.CriticalErrorDemoted);
    }

    [Fact]
    public void ParsePerQuestion_KeepsCriticalErrorWithAVerifiableQuote()
    {
        string answer = "The gift check is effectively **guaranteed for your first artifact gift** once all those conditions are met.";
        string json = @"{ ""accuracyLevel"": 2, ""completenessLevel"": 2, ""concisenessLevel"": 4,
                          ""readabilityLevel"": 5, ""criticalError"": true,
                          ""criticalErrorQuote"": ""The gift check is effectively guaranteed for your first artifact gift"",
                          ""accuracyEvidence"": ""Rubric point 3: the chance is 1 / (10 + 2 * Gifts * Artifacts)."" }";

        var parseResult = BenchmarkAssessmentParser.ParsePerQuestion(json, answer);

        Assert.NotNull(parseResult.Result);
        // Markdown emphasis in the answer must not defeat the match: the assessor is quoting.
        Assert.True(parseResult.Result.CriticalError);
        Assert.False(parseResult.Result.CriticalErrorDemoted);
        Assert.Contains("Rubric point 3", parseResult.Result.AccuracyEvidence);
    }

    [Fact]
    public void ParsePerQuestion_WithoutGradedText_LeavesCriticalErrorAlone()
    {
        // Re-scoring an old answer passes no graded text; nothing may be demoted retroactively.
        string json = @"{ ""accuracyLevel"": 2, ""completenessLevel"": 2, ""concisenessLevel"": 4,
                          ""readabilityLevel"": 5, ""criticalError"": true }";

        var parseResult = BenchmarkAssessmentParser.ParsePerQuestion(json);

        Assert.NotNull(parseResult.Result);
        Assert.True(parseResult.Result.CriticalError);
        Assert.False(parseResult.Result.CriticalErrorDemoted);
    }

    // --- 3. Final Synthesis Parser Tests ---

    [Fact]
    public void ParseFinalSynthesis_ParsesValidJson()
    {
        string json = @"```json
{
  ""finalScore"": 85,
  ""strengths"": ""Excellent memory of C macros."",
  ""weaknesses"": ""Minor verbose explanation in question 2."",
  ""overallComments"": ""High performing candidate model.""
}
```";

        var parseResult = BenchmarkAssessmentParser.ParseFinalSynthesis(json);

        Assert.True(parseResult.Success);
        Assert.NotNull(parseResult.Result);
        Assert.Equal(85, parseResult.Result.FinalScore);
        Assert.Equal("Excellent memory of C macros.", parseResult.Result.Strengths);
    }

    // --- 4. Difficulty Parser Tests ---

    [Fact]
    public void ParseDifficulty_ParsesSuiteJson()
    {
        string suiteJson = @"```json
{
  ""questions"": [
    { ""id"": 1, ""difficulty"": 20, ""rationale"": ""Basic lookup."" },
    { ""id"": 2, ""difficulty"": 80, ""rationale"": ""Complex mechanic."" }
  ]
}
```";
        var parseResult = BenchmarkDifficultyParser.Parse(suiteJson);
        Assert.True(parseResult.Success);
        Assert.Equal(2, parseResult.Items.Count);
        Assert.Equal(20, parseResult.Items[0].Difficulty);
        Assert.Equal(80, parseResult.Items[1].Difficulty);
    }

    // --- 5. Report Builder Tests ---

    [Fact]
    public void BuildMarkdownReport_GeneratesAllSectionsWithIndices()
    {
        var run = new BenchmarkRun
        {
            Id = 101,
            SuiteName = "GnollHack Core Suite",
            TestedModelSnapshot = BenchmarkModelSnapshots.Model(provider: "Anthropic", modelId: "claude-3-5-sonnet-20241022", displayName: "Claude 3.5 Sonnet"),
            AssessorModelSnapshot = BenchmarkModelSnapshots.Model(provider: "Anthropic", modelId: "claude-3-opus", displayName: "Claude Opus"),
            StartedAtUtc = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc),
            CompletedAtUtc = new DateTime(2026, 9, 1, 12, 5, 30, DateTimeKind.Utc),
            TotalDurationMs = 330000,
            TotalAnswerDurationMs = 45000,
            Status = BenchmarkRunStatus.Completed,
            FinalScore = 85,
            QualityIndex = 82,
            SpeedIndex = 76,
            ComputedScore = 82,
            ScoringMethodVersion = 2,
            SpeedMeasurementDegraded = true,
            TotalQuestionCount = 2,
            AnsweredQuestionCount = 2,
            TotalInputTokens = 15000,
            TotalOutputTokens = 3000,
            AssessmentText = "The model performed reasonably well with good reasoning.",
            Answers = new List<BenchmarkRunAnswer>
            {
                new BenchmarkRunAnswer
                {
                    ExpectedPointsRecorded = true,
                    OrderIndex = 1,
                    QuestionText = "Question 1",
                    Difficulty = BenchmarkDifficulty.Simple,
                    AssessedDifficulty = 25,
                    AnswerText = "Answer 1",
                    Status = BenchmarkAnswerStatus.Ok,
                    AccuracyLevel = 5,
                    CompletenessLevel = 4,
                    ConcisenessLevel = 6,
                    ReadabilityLevel = 5,
                    AccuracyScore = 87,
                    CompletenessScore = 72,
                    ConcisenessScore = 100,
                    ReadabilityScore = 87,
                    QualityScore = 83,
                    SpeedScore = 80,
                    DurationMs = 6000,
                    ReviewComment = "Decent."
                },
                new BenchmarkRunAnswer
                {
                    ExpectedPointsRecorded = true,
                    OrderIndex = 2,
                    QuestionText = "Question 2",
                    Difficulty = BenchmarkDifficulty.Intermediate,
                    AssessedDifficulty = 60,
                    AnswerText = "Answer 2",
                    Status = BenchmarkAnswerStatus.Ok,
                    AccuracyLevel = 4,
                    CompletenessLevel = 5,
                    ConcisenessLevel = 5,
                    ReadabilityLevel = 5,
                    AccuracyScore = 72,
                    CompletenessScore = 87,
                    ConcisenessScore = 87,
                    ReadabilityScore = 87,
                    QualityScore = 80,
                    SpeedScore = 70,
                    DurationMs = 9000,
                    ReviewComment = "Okay."
                }
            }
        };

        string report = BenchmarkReportBuilder.BuildMarkdownReport(run, "1.0.29");

        Assert.Contains("# GnollHack Overseer AI Intelligence Benchmark Report", report);
        Assert.Contains("## 1. Run Manifest", report);
        Assert.Contains("## 2. Results Summary", report);
        Assert.Contains("Intelligence Index: 82 / 100", report);
        Assert.Contains("Speed Index: 76 / 100", report);
        Assert.Contains("## 3. Questions and Replies", report);
        Assert.Contains("## 4. Scoring Method & Configuration", report);
        Assert.Contains("## 5. Issues", report);
        Assert.Contains("## 6. Synthesis Assessment", report);
        Assert.Contains("## 7. Final Indices", report);
        Assert.Contains("Concurrency Timing Notice", report);
    }

    // --- 6. Benchmark Assessment Failure Tests ---

    [Fact]
    public void BenchmarkAssessmentFailure_Describe_FormatsCorrectly()
    {
        var info1 = BenchmarkAssessmentFailure.Describe("504 Gateway Timeout", null);
        Assert.Contains("Assessor provider error (HTTP 504): 504 Gateway Timeout", info1.Message);
        Assert.Equal(504, info1.HttpStatus);
        Assert.True(info1.IsProviderError);

        var info2 = BenchmarkAssessmentFailure.Describe(null, "Missing closing brace");
        Assert.Contains("Assessor response could not be parsed: Missing closing brace", info2.Message);
        Assert.Null(info2.HttpStatus);
        Assert.False(info2.IsProviderError);

        var info3 = BenchmarkAssessmentFailure.Describe("429 Rate Limit", "Invalid JSON");
        Assert.Contains("Assessor provider error (HTTP 429): 429 Rate Limit", info3.Message);
        Assert.Equal(429, info3.HttpStatus);
        Assert.True(info3.IsProviderError);

        var info4 = BenchmarkAssessmentFailure.Describe(null, null);
        Assert.Contains("Assessor response could not be parsed", info4.Message);

        // The governor's own refusal is a failed call, not a provider error, whatever digits its key holds.
        var info5 = BenchmarkAssessmentFailure.Describe("Request throttled: Timed out after 120.0s waiting for concurrent model slot on OpenAI:system:429.", null);
        Assert.Contains("Assessor call failed: Request throttled:", info5.Message);
        Assert.Null(info5.HttpStatus);
        Assert.False(info5.IsProviderError);
    }

    [Fact]
    public void BenchmarkAssessmentFailure_Truncate_TruncatesToMax2048()
    {
        string shortStr = "Simple short error";
        Assert.Equal(shortStr, BenchmarkAssessmentFailure.Truncate(shortStr));

        string longStr = new string('A', 3000);
        string truncated = BenchmarkAssessmentFailure.Truncate(longStr)!;
        Assert.Equal(2048, truncated.Length);
        Assert.EndsWith("…", truncated);
    }

    // --- 7. Benchmark Run Finalizer Tests ---

    [Fact]
    public void BenchmarkRunFinalizer_ComputeStatus_ReturnsCorrectStatus()
    {
        var answersAllOk = new List<BenchmarkRunAnswer>
        {
            new() { Status = BenchmarkAnswerStatus.Ok, AssessmentStatus = BenchmarkAssessmentStatus.Scored }
        };
        Assert.False(BenchmarkRunFinalizer.HasUnresolvedWork(answersAllOk[0]));
        Assert.Equal(BenchmarkRunStatus.Completed, BenchmarkRunFinalizer.ComputeStatus(answersAllOk));

        var answersWithError = new List<BenchmarkRunAnswer>
        {
            new() { Status = BenchmarkAnswerStatus.ProviderError, AssessmentStatus = BenchmarkAssessmentStatus.Scored }
        };
        Assert.True(BenchmarkRunFinalizer.HasUnresolvedWork(answersWithError[0]));
        Assert.Equal(BenchmarkRunStatus.CompletedWithErrors, BenchmarkRunFinalizer.ComputeStatus(answersWithError));

        var answersWithFailedAssessment = new List<BenchmarkRunAnswer>
        {
            new() { Status = BenchmarkAnswerStatus.Ok, AssessmentStatus = BenchmarkAssessmentStatus.Failed }
        };
        Assert.True(BenchmarkRunFinalizer.HasUnresolvedWork(answersWithFailedAssessment[0]));
        Assert.Equal(BenchmarkRunStatus.CompletedWithErrors, BenchmarkRunFinalizer.ComputeStatus(answersWithFailedAssessment));

        var answersInProgress = new List<BenchmarkRunAnswer>
        {
            new() { Status = BenchmarkAnswerStatus.Ok, AssessmentStatus = BenchmarkAssessmentStatus.Pending }
        };
        Assert.True(BenchmarkRunFinalizer.HasUnresolvedWork(answersInProgress[0]));
        Assert.Equal(BenchmarkRunStatus.CompletedWithErrors, BenchmarkRunFinalizer.ComputeStatus(answersInProgress));
    }

    [Fact]
    public void BenchmarkRunFinalizer_Apply_RecalculatesMetricsAndCompletedTime()
    {
        var run = new BenchmarkRun
        {
            Id = 1,
            Status = BenchmarkRunStatus.Running,
            Answers = new List<BenchmarkRunAnswer>
            {
                new()
                {
                    OrderIndex = 1,
                    QuestionText = "Q1",
                    AnswerText = "A1",
                    Status = BenchmarkAnswerStatus.Ok,
                    AssessmentStatus = BenchmarkAssessmentStatus.Scored,
                    QualityScore = 90,
                    SpeedScore = 80,
                    DurationMs = 2000,
                    InputTokens = 100,
                    OutputTokens = 50,
                    CacheReadInputTokens = 20,
                    CacheCreationInputTokens = 10
                },
                new()
                {
                    OrderIndex = 2,
                    QuestionText = "Q2",
                    AnswerText = "A2",
                    Status = BenchmarkAnswerStatus.Ok,
                    AssessmentStatus = BenchmarkAssessmentStatus.Scored,
                    QualityScore = 70,
                    SpeedScore = 60,
                    DurationMs = 3000,
                    InputTokens = 200,
                    OutputTokens = 100,
                    CacheReadInputTokens = 40,
                    CacheCreationInputTokens = 20
                }
            }
        };

        BenchmarkRunFinalizer.Apply(run, run.Answers);

        Assert.Equal(BenchmarkRunStatus.Completed, run.Status);
        Assert.NotNull(run.CompletedAtUtc);
        Assert.Equal(2, run.AnsweredQuestionCount);
        Assert.Equal(5000, run.TotalAnswerDurationMs);
        Assert.Equal(300, run.TotalInputTokens);
        Assert.Equal(150, run.TotalOutputTokens);
        Assert.Equal(60, run.TotalCacheReadTokens);
        Assert.Equal(30, run.TotalCacheCreationTokens);
        Assert.Equal(80, run.QualityIndex);
        Assert.Equal(70, run.SpeedIndex);
    }

    // --- 8. Benchmark Report Builder Assessor Provenance Tests ---

    [Fact]
    public void BenchmarkReportBuilder_AssessedBy_IncludesCalloutWhenOverridden()
    {
        var run = new BenchmarkRun
        {
            Id = 1,
            SuiteName = "Test Suite",
            TestedModelSnapshot = BenchmarkModelSnapshots.Model(provider: "Anthropic", modelId: "claude-3-5-sonnet", displayName: "Tested Model"),
            AssessorModelConfigurationId = 10,
            AssessorModelSnapshot = BenchmarkModelSnapshots.Model(provider: "Anthropic", modelId: "claude-3-5-sonnet", displayName: "Original Assessor"),
            Status = BenchmarkRunStatus.Completed,
            Answers = new List<BenchmarkRunAnswer>
            {
                new()
                {
                    OrderIndex = 1,
                    QuestionText = "Question 1",
                    AnswerText = "Answer 1",
                    Status = BenchmarkAnswerStatus.Ok,
                    AssessmentStatus = BenchmarkAssessmentStatus.Scored,
                    QualityScore = 80,
                    AssessedByModelConfigurationId = 20, // Differs!
                    AssessedByModelSnapshot = BenchmarkModelSnapshots.Model(provider: "Anthropic", modelId: "claude-3-opus", displayName: "Override Assessor"),
                    AssessedAtUtc = new DateTime(2026, 9, 2, 12, 0, 0, DateTimeKind.Utc)
                },
                new()
                {
                    OrderIndex = 2,
                    QuestionText = "Question 2",
                    AnswerText = "Answer 2",
                    Status = BenchmarkAnswerStatus.Ok,
                    AssessmentStatus = BenchmarkAssessmentStatus.Scored,
                    QualityScore = 80,
                    AssessedByModelConfigurationId = 10, // Matches run assessor!
                    AssessedByModelSnapshot = BenchmarkModelSnapshots.Model(provider: "Anthropic", modelId: "claude-3-5-sonnet", displayName: "Original Assessor")
                }
            }
        };

        string report = BenchmarkReportBuilder.BuildMarkdownReport(run, "1.0.29");

        Assert.Contains("Assessed by:** Override Assessor (Anthropic, claude-3-opus) — differs from this run's assessor", report);
        // Question 2 should not have "differs from this run's assessor" callout
        Assert.DoesNotContain("Original Assessor (Anthropic, claude-3-5-sonnet) — differs from this run's assessor", report);
    }

    // --- 9. Benchmark Service Orphaned Run Cleanup Tests ---

    [Fact]
    public async Task BenchmarkService_CleanupOrphanedRunsAsync_WithoutAnswers_MarksFailed()
    {
        var ct = TestContext.Current.CancellationToken;
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        await using (var db = new ApplicationDbContext(dbOptions))
        {
            var run = BenchmarkModelSnapshots.Attach(new BenchmarkRun
            {
                Id = 1,
                SuiteName = "Test Suite",
                TestedModelSnapshot = BenchmarkModelSnapshots.Model(provider: "Provider A", modelId: "model-a", displayName: "Model A"),
                AssessorModelSnapshot = BenchmarkModelSnapshots.Model(provider: "Provider B", modelId: "model-b", displayName: "Model B"),
                Status = BenchmarkRunStatus.Running,
                StartedAtUtc = DateTime.UtcNow.AddHours(-1)
            });
            db.BenchmarkRuns.Add(run);
            await db.SaveChangesAsync(ct);
        }

        var services = new ServiceCollection();
        services.AddScoped(_ => new ApplicationDbContext(dbOptions));
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        var benchmarkService = new BenchmarkService(
            scopeFactory,
            null!,
            null!,
            null!,
            new BenchmarkRunManager(),
            new BenchmarkDifficultyJobManager(),
            null!,
            null!,
            new ConfigurationBuilder().Build(),
            NullLogger<BenchmarkService>.Instance);

        await benchmarkService.CleanupOrphanedRunsAsync();

        await using (var verifyDb = new ApplicationDbContext(dbOptions))
        {
            var updated = await verifyDb.BenchmarkRuns.FindAsync([1L], ct);
            Assert.NotNull(updated);
            Assert.Equal(BenchmarkRunStatus.Failed, updated.Status);
            Assert.NotNull(updated.CompletedAtUtc);
            Assert.Equal("Run interrupted by application restart.", updated.ErrorMessage);
        }
    }

    [Fact]
    public async Task RunDifficultyAssessmentAsync_WithCancelledToken_EndsJobCancelledAndReleasesManager()
    {
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        var services = new ServiceCollection();
        services.AddScoped(_ => new ApplicationDbContext(dbOptions));
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        var manager = new BenchmarkDifficultyJobManager();
        var benchmarkService = new BenchmarkService(
            scopeFactory,
            null!,
            null!,
            null!,
            new BenchmarkRunManager(),
            manager,
            null!,
            null!,
            new ConfigurationBuilder().Build(),
            NullLogger<BenchmarkService>.Instance);

        using var cts = new CancellationTokenSource();
        var job = new BenchmarkDifficultyJob
        {
            SuiteId = 1,
            SuiteName = "Test Suite",
            Scope = "suite",
            AssessorConfigId = 1,
            AssessorDisplayName = "Assessor Model",
            Cts = cts,
            Items = new List<BenchmarkDifficultyJobItem>
            {
                new() { QuestionId = 1, OrderIndex = 1, QuestionTextExcerpt = "Q1" },
                new() { QuestionId = 2, OrderIndex = 2, QuestionTextExcerpt = "Q2" }
            }
        };
        Assert.True(manager.TryStart(job, out _));

        cts.Cancel();
        await benchmarkService.RunDifficultyAssessmentAsync(job.Id, cts.Token);

        Assert.Equal(BenchmarkDifficultyJobStatus.Cancelled, job.Status);
        Assert.All(job.Items, i => Assert.Equal(BenchmarkDifficultyItemStatus.Cancelled, i.Status));
        Assert.NotNull(job.CompletedAtUtc);

        var nextJob = new BenchmarkDifficultyJob
        {
            SuiteId = 1,
            SuiteName = "Test Suite",
            Cts = new CancellationTokenSource()
        };
        Assert.True(manager.TryStart(nextJob, out var existing));
        Assert.Null(existing);
    }

    [Fact]
    public async Task CleanupOrphanedRuns_PublishesNoIndex_AndRecordsTotals()
    {
        var ct = TestContext.Current.CancellationToken;
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        await using (var db = new ApplicationDbContext(dbOptions))
        {
            var run = BenchmarkModelSnapshots.Attach(new BenchmarkRun
            {
                Id = 2,
                SuiteName = "Test Suite",
                TestedModelSnapshot = BenchmarkModelSnapshots.Model(provider: "Provider A", modelId: "model-a", displayName: "Model A"),
                AssessorModelSnapshot = BenchmarkModelSnapshots.Model(provider: "Provider B", modelId: "model-b", displayName: "Model B"),
                Status = BenchmarkRunStatus.Running,
                StartedAtUtc = DateTime.UtcNow.AddHours(-1),
                Answers = new List<BenchmarkRunAnswer>
                {
                    new()
                    {
                        OrderIndex = 1,
                        QuestionText = "Q1",
                        AnswerText = "A1",
                        Status = BenchmarkAnswerStatus.Ok,
                        AssessmentStatus = BenchmarkAssessmentStatus.Scored,
                        QualityScore = 85,
                        SpeedScore = 75,
                        DurationMs = 2000,
                        InputTokens = 12000,
                        OutputTokens = 900
                    }
                }
            });
            db.BenchmarkRuns.Add(run);
            await db.SaveChangesAsync(ct);
        }

        var services = new ServiceCollection();
        services.AddScoped(_ => new ApplicationDbContext(dbOptions));
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        var benchmarkService = new BenchmarkService(
            scopeFactory,
            null!,
            null!,
            null!,
            new BenchmarkRunManager(),
            new BenchmarkDifficultyJobManager(),
            null!,
            null!,
            new ConfigurationBuilder().Build(),
            NullLogger<BenchmarkService>.Instance);

        await benchmarkService.CleanupOrphanedRunsAsync();

        await using (var verifyDb = new ApplicationDbContext(dbOptions))
        {
            var updated = await verifyDb.BenchmarkRuns.Include(r => r.Answers).FirstOrDefaultAsync(r => r.Id == 2L, ct);
            Assert.NotNull(updated);

            // An interrupted run never reached the end of its suite, so it carries no index — an
            // index over whichever answers happen to exist is indistinguishable, once stored, from
            // an index over all of them.
            Assert.Equal(BenchmarkRunStatus.Failed, updated.Status);
            Assert.Equal("Run interrupted by application restart.", updated.ErrorMessage);
            Assert.NotNull(updated.CompletedAtUtc);
            Assert.Null(updated.QualityIndex);
            Assert.Null(updated.SpeedIndex);

            // What it did consume is recorded, which is the whole point of splitting the totals
            // out of the scores.
            Assert.Equal(12000, updated.TotalInputTokens);
            Assert.Equal(900, updated.TotalOutputTokens);
            Assert.Equal(2000, updated.TotalAnswerDurationMs);
            Assert.Equal(1, updated.AnsweredQuestionCount);

            // Wall clock is deliberately left at zero: the outage between the crash and the
            // restart is not run time, and the reader derives elapsed from the two timestamps.
            Assert.Equal(0, updated.TotalDurationMs);
        }
    }

    [Fact]
    public async Task RescoreRun_OnAbortedRun_IsRefused()
    {
        var ct = TestContext.Current.CancellationToken;
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        await using (var db = new ApplicationDbContext(dbOptions))
        {
            db.BenchmarkRuns.Add(BenchmarkModelSnapshots.Attach(new BenchmarkRun
            {
                Id = 3,
                SuiteName = "Test Suite",
                TestedModelSnapshot = BenchmarkModelSnapshots.Model(provider: "Provider A", modelId: "model-a", displayName: "Model A"),
                AssessorModelSnapshot = BenchmarkModelSnapshots.Model(provider: "Provider B", modelId: "model-b", displayName: "Model B"),
                Status = BenchmarkRunStatus.Canceled,
                StartedAtUtc = DateTime.UtcNow.AddHours(-1),
                CompletedAtUtc = DateTime.UtcNow,
                Answers = new List<BenchmarkRunAnswer>
                {
                    new()
                    {
                        OrderIndex = 1,
                        QuestionText = "Q1",
                        AnswerText = "A1",
                        Status = BenchmarkAnswerStatus.Ok,
                        AssessmentStatus = BenchmarkAssessmentStatus.Scored,
                        AccuracyLevel = 5,
                        CompletenessLevel = 5,
                        ConcisenessLevel = 5,
                        ReadabilityLevel = 5,
                        QualityScore = 85,
                        SpeedScore = 75,
                        DurationMs = 2000
                    }
                }
            }));
            await db.SaveChangesAsync(ct);
        }

        var benchmarkService = CreateBenchmarkServiceOver(dbOptions);

        var (success, error) = await benchmarkService.RescoreRunAsync(3);

        Assert.False(success);
        Assert.Equal(BenchmarkService.AbortedRunRefusal, error);

        await using (var verifyDb = new ApplicationDbContext(dbOptions))
        {
            var run = await verifyDb.BenchmarkRuns.FindAsync([3L], ct);
            Assert.NotNull(run);
            Assert.Null(run.QualityIndex);
            Assert.Null(run.SpeedIndex);
        }
    }

    [Fact]
    public async Task RescoreRun_WritesAllThreeQualityFigures()
    {
        var ct = TestContext.Current.CancellationToken;
        // H13: the method wrote QualityIndex and SpeedIndex and nothing else, so a rescored run's
        // report printed an index from the new profile beside an unweighted mean and a standard
        // error from the old one.
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        await using (var db = new ApplicationDbContext(dbOptions))
        {
            var run = BenchmarkModelSnapshots.Attach(new BenchmarkRun
            {
                Id = 4,
                SuiteName = "Test Suite",
                TestedModelSnapshot = BenchmarkModelSnapshots.Model(provider: "Provider A", modelId: "model-a", displayName: "Model A"),
                AssessorModelSnapshot = BenchmarkModelSnapshots.Model(provider: "Provider B", modelId: "model-b", displayName: "Model B"),
                Status = BenchmarkRunStatus.CompletedWithErrors,
                StartedAtUtc = DateTime.UtcNow.AddHours(-1),
                CompletedAtUtc = DateTime.UtcNow,
                TotalQuestionCount = 4,
                UnweightedQualityIndex = 11,
                QualityIndexStandardError = 99.0,
                Answers = new List<BenchmarkRunAnswer>()
            });

            for (int i = 1; i <= 3; i++)
            {
                run.Answers.Add(new BenchmarkRunAnswer
                {
                    ExpectedPointsRecorded = true,
                    OrderIndex = i,
                    QuestionText = $"Q{i}",
                    AnswerText = $"A{i}",
                    Status = BenchmarkAnswerStatus.Ok,
                    AssessmentStatus = BenchmarkAssessmentStatus.Scored,
                    AccuracyLevel = i + 2,
                    CompletenessLevel = i + 2,
                    ConcisenessLevel = i + 2,
                    ReadabilityLevel = i + 2,
                    AssessedDifficulty = 50,
                    DurationMs = 2000
                });
            }

            // The zero the rescore must keep in the index: a question the model failed to answer.
            run.Answers.Add(new BenchmarkRunAnswer
            {
                ExpectedPointsRecorded = true,
                OrderIndex = 4,
                QuestionText = "Q4",
                AnswerText = string.Empty,
                Status = BenchmarkAnswerStatus.EmptyAnswer,
                AssessmentStatus = BenchmarkAssessmentStatus.Scored,
                ProviderFinishReason = "STOP",
                AssessedDifficulty = 50,
                QualityScore = 0,
                RawQualityScore = 0,
                Score = 0,
                DurationMs = 400
            });

            db.BenchmarkRuns.Add(run);
            await db.SaveChangesAsync(ct);
        }

        var benchmarkService = CreateBenchmarkServiceOver(dbOptions);

        var (success, error) = await benchmarkService.RescoreRunAsync(4);
        Assert.True(success, error);

        await using (var verifyDb = new ApplicationDbContext(dbOptions))
        {
            var run = await verifyDb.BenchmarkRuns.Include(r => r.Answers).FirstAsync(r => r.Id == 4L, ct);

            Assert.NotNull(run.QualityIndex);
            Assert.NotNull(run.UnweightedQualityIndex);
            Assert.NotNull(run.QualityIndexStandardError);

            // All three come from the same item set, which includes the unanswered question at 0.
            var scorable = run.Answers
                .Where(a => BenchmarkRunFinalizer.CountsTowardQualityIndex(a) && a.QualityScore.HasValue)
                .Select(a => (a.QualityScore, a.AssessedDifficulty ?? BenchmarkRunFinalizer.FallbackDifficulty(a.Difficulty)))
                .ToList();

            Assert.Equal(4, scorable.Count);
            Assert.Equal(BenchmarkScoring.QualityIndex(scorable), run.QualityIndex);
            Assert.Equal(
                BenchmarkScoring.UnweightedQualityMean(
                    run.Answers.Where(BenchmarkRunFinalizer.CountsTowardQualityIndex).Select(a => a.QualityScore)),
                run.UnweightedQualityIndex);
            Assert.Equal(BenchmarkScoring.QualityIndexStandardError(scorable), run.QualityIndexStandardError);

            // The stale figures from the previous profile are gone rather than left behind.
            Assert.NotEqual(11, run.UnweightedQualityIndex);
            Assert.NotEqual(99.0, run.QualityIndexStandardError);
        }
    }

    /// <summary>
    /// A BenchmarkService wired only far enough for the paths that touch the database and the
    /// scoring profiles. The provider-facing collaborators stay null: nothing here makes a model
    /// call.
    /// </summary>
    // --- 10. Segmented Candidate Prompt ---

    /// <summary>
    /// The candidate request carries the prompt in the three cache segments the providers key their
    /// prompt caches on, while the run snapshot keeps hashing the flat string. Those two are only
    /// the same instrument while the segments concatenate to it byte for byte, which is what this
    /// pins: <c>ChatService.BuildSystemPrompt</c> is that concatenation today, so the test guards a
    /// future divergence rather than a present one.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SegmentedCandidatePrompt_ConcatenatesToTheFlatCandidatePrompt(bool verboseMode)
    {
        var chatService = CreateChatService();
        var promptOptions = new BenchmarkCandidatePromptOptions { VerboseMode = verboseMode };

        string flat = promptOptions.BuildSystemPrompt(chatService, MobileGnollHackLogger.Data.ParallelExecutionMode.Enabled);
        var (frozen, session, volatileSuffix) =
            promptOptions.BuildSegmentedSystemPrompt(chatService, MobileGnollHackLogger.Data.ParallelExecutionMode.Enabled);

        Assert.Equal(flat, frozen + session + volatileSuffix);

        // A benchmark passes no wiki context, so the volatile suffix is empty and the providers
        // skip that block. A non-empty one here would mean the candidate had been handed context
        // the flat prompt did not describe.
        Assert.Equal(string.Empty, volatileSuffix);
        Assert.NotEmpty(frozen);
    }

    /// <summary>
    /// A ChatService over an in-memory database, built the way the other prompt tests in this
    /// suite build one. Only the prompt builders are exercised through it, so nothing here reaches
    /// a provider.
    /// </summary>
    private static ChatService CreateChatService()
    {
        var services = new ServiceCollection();
        var inMemorySettings = new Dictionary<string, string?>
        {
            { "AesEncryptionKey", Convert.ToBase64String(new byte[32]) },
            { "PromptCacheSettings:EnableSegmentedPrompt", "true" }
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemorySettings).Build();

        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString()));

        services.AddSingleton<IConfiguration>(config);
        services.AddHttpClient();
        services.AddSignalR();
        services.AddMemoryCache();
        services.AddSingleton<Overseer.Services.Tools.IClientToolBridge, DummyClientToolBridge>();
        services.AddScoped<Overseer.Services.Tools.ToolRegistry>();
        services.AddScoped<Overseer.Services.Tools.ToolExecutor>();
        services.AddScoped<CryptoService>();
        services.AddScoped<WikiService>();
        services.AddScoped<ModelMetadataService>();
        services.AddScoped<KnowledgeBaseService>();
        services.AddScoped<OngoingChatManager>();
        services.AddScoped<Overseer.Services.Providers.IAiProvider, Overseer.Services.Providers.OpenAiResponsesProvider>();
        services.AddScoped<Overseer.Services.Agents.AgentLoopRunner>();
        services.AddSingleton<ParallelExecutionResolver>();
        services.AddSingleton<Overseer.Services.Privacy.AttachmentValidator>();
        services.AddSingleton<Overseer.Services.Privacy.IAntiMalwareScanner,
            Overseer.Services.Privacy.NullAntiMalwareScanner>();
        services.AddSingleton<Overseer.Services.Privacy.EndpointPolicy>();
        services.AddSingleton<Overseer.Services.Privacy.ConfidentialityPostureService>();
        services.AddSingleton<Overseer.Services.Privacy.IContentKeyRing,
            Overseer.Services.Privacy.ConfigurationContentKeyRing>();
        services.AddSingleton<Overseer.Services.Privacy.ContentProtectionService>();
        services.AddSingleton<Overseer.Services.Privacy.Dlp.DlpScannerService>();
        services.AddSingleton<Overseer.Services.Documents.DocumentParserService>();
        services.AddSingleton<Overseer.Services.Rag.DocumentChunker>();
        services.AddSingleton<Overseer.Services.Rag.IEmbeddingService,
            Overseer.Services.Rag.LocalOnnxEmbeddingService>();
        services.AddSingleton<Overseer.Services.Rag.DocumentRagService>();
        services.AddScoped<Overseer.Services.Rag.RagSidecarStore>();
        services.AddSingleton(sp => new Overseer.Services.Privacy.EphemeralSessionStore(
            sp.GetRequiredService<IConfiguration>(), startSweeper: false));
        services.AddScoped<ChatService>();

        return services.BuildServiceProvider().GetRequiredService<ChatService>();
    }

    private static BenchmarkService CreateBenchmarkServiceOver(DbContextOptions<ApplicationDbContext> dbOptions)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => new ApplicationDbContext(dbOptions));
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        return new BenchmarkService(
            scopeFactory,
            null!,
            null!,
            null!,
            new BenchmarkRunManager(),
            new BenchmarkDifficultyJobManager(),
            new BenchmarkScoringProfileService(scopeFactory, NullLogger<BenchmarkScoringProfileService>.Instance),
            null!,
            new ConfigurationBuilder().Build(),
            NullLogger<BenchmarkService>.Instance);
    }

    [Fact]
    public void ResolveSecondOpinionTrigger_ContestedVerdictBeatsUnevidencedDeduction()
    {
        var answer = new BenchmarkRunAnswer
        {
            ExpectedPointsRecorded = true,
            AnswerFlags = (int)(BenchmarkAnswerFlags.ContestedVerdict | BenchmarkAnswerFlags.UnevidencedDeduction),
            QualityScore = 80
        };

        var trigger = BenchmarkService.ResolveSecondOpinionTrigger(
            answer,
            BenchmarkSecondOpinionMode.Flagged,
            BenchmarkScoringConstants.Default);

        Assert.Equal(BenchmarkService.SecondOpinionTriggers.ContestedVerdict, trigger);
    }

    [Fact]
    public void ResolveSecondOpinionTrigger_UnevidencedDeductionBeatsUnverifiedClaimsAndThreshold()
    {
        var answer = new BenchmarkRunAnswer
        {
            ExpectedPointsRecorded = true,
            AnswerFlags = (int)BenchmarkAnswerFlags.UnevidencedDeduction,
            UnverifiedClaimCount = 3,
            AccuracyLevel = 2,
            QualityScore = 30
        };

        var constants = new BenchmarkScoringConstants { SecondOpinionQualityThreshold = 50 };

        var trigger = BenchmarkService.ResolveSecondOpinionTrigger(
            answer,
            BenchmarkSecondOpinionMode.Flagged,
            constants);

        Assert.Equal(BenchmarkService.SecondOpinionTriggers.UnevidencedDeduction, trigger);
    }

    [Fact]
    public async Task RunClaimVerificationAsync_AnswerWithZeroUnverifiedClaims_ProducesNoVerifierCall()
    {
        var ct = TestContext.Current.CancellationToken;
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var dummyKey = Convert.ToBase64String(new byte[32]);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AesEncryptionKey"] = dummyKey
        }).Build();
        var crypto = new CryptoService(configuration);
        var (cipher, nonce, tag) = crypto.Encrypt("test-api-key", "SYSTEM_API_KEY");

        long runId;
        await using (var db = new ApplicationDbContext(dbOptions))
        {
            var config = new SystemAiApiConfiguration
            {
                Id = 10,
                DisplayName = "Verifier Model",
                ModelId = "model-v",
                Provider = "Anthropic",
                ModelRole = 4, // Assessor / verifier
                IsEnabled = true,
                EncryptedApiKey = cipher,
                ApiKeyNonce = nonce,
                ApiKeyTag = tag
            };
            db.SystemAiApiConfigurations.Add(config);

            var run = new BenchmarkRun
            {
                SuiteName = "Suite",
                TestedModelSnapshot = BenchmarkModelSnapshots.Model(provider: "Provider T", modelId: "model-t", displayName: "Model T"),
                AssessorModelSnapshot = BenchmarkModelSnapshots.Model(provider: "Provider A", modelId: "model-a", displayName: "Model A"),
                ClaimVerifierModelConfigurationId = 10,
                // The provider must match `config` above, or SystemAiConfigurationSnapshotStore.Bind
                // refuses the pairing before RunClaimVerificationAsync ever reaches the gating below.
                ClaimVerifierModelSnapshot = SystemAiConfigurationSnapshotStore.FromConfiguration(config),
                Status = BenchmarkRunStatus.Running,
                StartedAtUtc = DateTime.UtcNow
            };
            db.BenchmarkRuns.Add(run);
            await db.SaveChangesAsync(ct);
            runId = run.Id;

            var answer = new BenchmarkRunAnswer
            {
                ExpectedPointsRecorded = true,
                BenchmarkRunId = runId,
                OrderIndex = 1,
                QuestionText = "Question 1",
                AnswerText = "Answer 1",
                Status = BenchmarkAnswerStatus.Ok,
                UnverifiedClaimCount = 0,
                UnverifiedClaimsJson = "[]"
            };
            db.BenchmarkRunAnswers.Add(answer);
            await db.SaveChangesAsync(ct);
        }

        var services = new ServiceCollection();
        services.AddScoped(_ => new ApplicationDbContext(dbOptions));
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        // _agentLoopRunner is passed as null: if gating failed and called the verifier,
        // it would throw NullReferenceException.
        var benchmarkService = new BenchmarkService(
            scopeFactory,
            null!,
            null!,
            crypto,
            new BenchmarkRunManager(),
            new BenchmarkDifficultyJobManager(),
            null!,
            new Overseer.Services.Privacy.EndpointPolicy(configuration),
            configuration,
            NullLogger<BenchmarkService>.Instance);

        await using (var db = new ApplicationDbContext(dbOptions))
        {
            var run = await db.BenchmarkRuns.FindAsync([runId], ct);
            Assert.NotNull(run);

            await benchmarkService.RunClaimVerificationAsync(db, null!, run, CancellationToken.None);

            var verifyAnswer = await db.BenchmarkRunAnswers.FirstAsync(a => a.BenchmarkRunId == runId, ct);
            Assert.Null(verifyAnswer.ClaimVerificationJson);
            Assert.Equal(0, run.ClaimVerifiedAnswerCount);
        }
    }

    [Fact]
    public void BuildClaimVerificationRequest_PlacesPromptInUserTurn()
    {
        var verifierConfig = new SystemAiApiConfiguration
        {
            Id = 5,
            Provider = "OpenAI",
            ModelId = "gpt-4o",
            DisplayName = "GPT-4o Verifier"
        };
        var claims = new List<string> { "Claim 1: Master Kaen has 350 HP" };
        string prompt = BenchmarkClaimVerificationPrompt.BuildPrompt(
            "GnollHack Suite",
            1,
            "What are Master Kaen stats?",
            "Expected points",
            claims,
            new List<string> { "repo_search" },
            3);

        var request = BenchmarkService.BuildClaimVerificationRequest(
            verifierConfig,
            "api-key-test",
            Overseer.Services.Providers.AiEndpointDescriptor.Official,
            prompt,
            new List<string> { "repo_search" },
            maxOutputTokens: 1024,
            toolIterations: 5,
            totalModelCalls: 10,
            toolCallBudget: 3,
            maxResultLength: 2000,
            runId: 42,
            orderIndex: 1,
            startedByUserId: "user-1");

        Assert.Single(request.SeedHistory);
        var userTurn = request.SeedHistory[0];
        var roleProp = userTurn.GetType().GetProperty("role")?.GetValue(userTurn) as string;
        var contentProp = userTurn.GetType().GetProperty("content")?.GetValue(userTurn) as string;

        Assert.Equal("user", roleProp);
        Assert.NotNull(contentProp);
        Assert.Contains("What are Master Kaen stats?", contentProp);
        Assert.Contains("Claim 1: Master Kaen has 350 HP", contentProp);
        Assert.Contains("Strictly adhere to the requested JSON response format", request.SystemPrompt);
    }

    [Fact]
    public void BuildClaimVerificationRequest_EnablesToolsWithConfiguredBudget()
    {
        var verifierConfig = new SystemAiApiConfiguration
        {
            Id = 5,
            Provider = "Anthropic",
            ModelId = "claude-3-sonnet",
            DisplayName = "Claude Sonnet Verifier"
        };

        var request = BenchmarkService.BuildClaimVerificationRequest(
            verifierConfig,
            "api-key-test",
            Overseer.Services.Providers.AiEndpointDescriptor.Official,
            "test prompt",
            new List<string> { "repo_search", "c_code_definition" },
            maxOutputTokens: 1024,
            toolIterations: 6,
            totalModelCalls: 12,
            toolCallBudget: 4,
            maxResultLength: 2048,
            runId: 99,
            orderIndex: 2,
            startedByUserId: "user-2");

        Assert.True(request.EnableToolUse);
        Assert.Equal(4, request.ToolExecutionContext.MaxCallsPerSession);
        Assert.Equal(6, request.MaxToolIterations);
        Assert.Equal(new[] { "repo_search", "c_code_definition" }, request.AllowedTools);
    }

    [Fact]
    public void BuildClaimVerificationRequest_ScalesTheBudgetWithTheManifest_UpToTheCap()
    {
        static IConfiguration Config(params (string Key, string Value)[] values)
            => new ConfigurationBuilder()
                .AddInMemoryCollection(values.ToDictionary(v => "Benchmark:ClaimVerification:" + v.Key, v => (string?)v.Value))
                .Build();

        var scaled = Config(("ToolCallBudget", "15"), ("ToolCallBudgetPerItem", "1"), ("ToolCallBudgetMax", "30"),
            ("ToolIterations", "8"), ("TotalModelCalls", "12"));

        // The base budget with no items; one call per item above it; the cap past fifteen items.
        Assert.Equal((15, 8, 12), BenchmarkService.ResolveClaimVerificationBudget(scaled, 0));
        Assert.Equal((20, 10, 14), BenchmarkService.ResolveClaimVerificationBudget(scaled, 5));
        Assert.Equal((30, 15, 19), BenchmarkService.ResolveClaimVerificationBudget(scaled, 16));
        Assert.Equal((30, 15, 19), BenchmarkService.ResolveClaimVerificationBudget(scaled, 40));

        // No per-item setting keeps the fixed limits, whatever the manifest.
        var fixedBudget = Config(("ToolCallBudget", "15"), ("ToolIterations", "8"), ("TotalModelCalls", "12"));
        Assert.Equal((15, 8, 12), BenchmarkService.ResolveClaimVerificationBudget(fixedBudget, 16));

        // Unset or non-positive base budget is 15; a cap below the base never lowers it.
        Assert.Equal((15, 8, 12), BenchmarkService.ResolveClaimVerificationBudget(Config(), 16));
        Assert.Equal((15, 8, 12), BenchmarkService.ResolveClaimVerificationBudget(Config(("ToolCallBudget", "0")), 3));
        Assert.Equal((15, 8, 12), BenchmarkService.ResolveClaimVerificationBudget(
            Config(("ToolCallBudgetPerItem", "1"), ("ToolCallBudgetMax", "10")), 16));

        var (toolCallBudget, toolIterations, totalModelCalls) = BenchmarkService.ResolveClaimVerificationBudget(scaled, 16);
        var request = BenchmarkService.BuildClaimVerificationRequest(
            new SystemAiApiConfiguration { Id = 5, Provider = "OpenAI", ModelId = "gpt-4o", DisplayName = "Verifier" },
            "api-key-test",
            Overseer.Services.Providers.AiEndpointDescriptor.Official,
            "test prompt",
            new List<string> { "repo_search" },
            maxOutputTokens: 1024,
            toolIterations: toolIterations,
            totalModelCalls: totalModelCalls,
            toolCallBudget: toolCallBudget,
            maxResultLength: 2048,
            runId: 7,
            orderIndex: 3,
            startedByUserId: "user-3");

        Assert.Equal(30, request.ToolExecutionContext.MaxCallsPerSession);
        Assert.Equal(15, request.MaxToolIterations);
        Assert.Equal(19, request.Budget!.MaxTotalModelCalls);
    }

    [Fact]
    public void Truncate_ClaimVerificationLength_FitsColumn()
    {
        var longError = new string('x', 2000);
        var truncated = BenchmarkAssessmentFailure.Truncate(longError, BenchmarkAssessmentFailure.MaxClaimVerificationErrorLength);

        Assert.NotNull(truncated);
        Assert.Equal(BenchmarkAssessmentFailure.MaxClaimVerificationErrorLength, truncated.Length);
        Assert.EndsWith("…", truncated);
    }

    [Fact]
    public void ResolveSecondOpinionTrigger_UnverifiabilityDeduction_TriggersSecondOpinion()
    {
        var answer = new BenchmarkRunAnswer
        {
            ExpectedPointsRecorded = true,
            AnswerFlags = (int)BenchmarkAnswerFlags.UnevidencedDeduction,
            UnverifiedClaimCount = 2,
            AccuracyLevel = 4,
            QualityScore = 75
        };

        var constants = new BenchmarkScoringConstants();

        var trigger = BenchmarkService.ResolveSecondOpinionTrigger(
            answer,
            BenchmarkSecondOpinionMode.Flagged,
            constants);

        Assert.Equal(BenchmarkService.SecondOpinionTriggers.UnevidencedDeduction, trigger);
    }

    [Fact]
    public void ResolveSecondOpinionTrigger_RefutedClaim_TriggersSecondOpinion()
    {
        var answer = new BenchmarkRunAnswer
        {
            ExpectedPointsRecorded = true,
            AnswerFlags = (int)BenchmarkAnswerFlags.RefutedClaim,
            QualityScore = 85
        };

        var trigger = BenchmarkService.ResolveSecondOpinionTrigger(
            answer,
            BenchmarkSecondOpinionMode.Flagged,
            BenchmarkScoringConstants.Default);

        Assert.Equal(BenchmarkService.SecondOpinionTriggers.RefutedClaim, trigger);
    }

    [Fact]
    public void ResolveSecondOpinionTrigger_OmissionAsAccuracy_TriggersSecondOpinion()
    {
        var answer = new BenchmarkRunAnswer
        {
            ExpectedPointsRecorded = true,
            AnswerFlags = (int)BenchmarkAnswerFlags.OmissionAsAccuracy,
            QualityScore = 85
        };

        var trigger = BenchmarkService.ResolveSecondOpinionTrigger(
            answer,
            BenchmarkSecondOpinionMode.Flagged,
            BenchmarkScoringConstants.Default);

        Assert.Equal(BenchmarkService.SecondOpinionTriggers.OmissionAsAccuracy, trigger);
    }

    [Fact]
    public void ResolveSecondOpinionTrigger_UnverifiedClaims_SuppressedWhenAllClaimsSupported()
    {
        // When claim verification positively supported all claims, the UnverifiedClaims trigger does not fire
        var answer = new BenchmarkRunAnswer
        {
            ExpectedPointsRecorded = true,
            UnverifiedClaimCount = 3,
            AccuracyLevel = 3,
            ClaimsSupportedCount = 3,
            ClaimsRefutedCount = 0,
            ClaimsIndeterminateCount = 0,
            QualityScore = 75
        };

        var trigger = BenchmarkService.ResolveSecondOpinionTrigger(
            answer,
            BenchmarkSecondOpinionMode.Flagged,
            new BenchmarkScoringConstants { SecondOpinionQualityThreshold = 50 });

        Assert.Null(trigger);
    }

    [Fact]
    public void ResolveSecondOpinionTrigger_UnverifiedClaims_FiresWhenAnyClaimRefutedOrIndeterminate()
    {
        var answer = new BenchmarkRunAnswer
        {
            ExpectedPointsRecorded = true,
            UnverifiedClaimCount = 3,
            AccuracyLevel = 3,
            ClaimsSupportedCount = 2,
            ClaimsRefutedCount = 0,
            ClaimsIndeterminateCount = 1,
            QualityScore = 75
        };

        var trigger = BenchmarkService.ResolveSecondOpinionTrigger(
            answer,
            BenchmarkSecondOpinionMode.Flagged,
            new BenchmarkScoringConstants { SecondOpinionQualityThreshold = 50 });

        Assert.Equal(BenchmarkService.SecondOpinionTriggers.UnverifiedClaims, trigger);
    }

    [Fact]
    public void CandidatePromptOptions_DefaultParity_MatchesProductionLiterals()
    {
        // Parity test: every default on BenchmarkCandidatePromptOptions must match the hard-coded
        // literals previously at BenchmarkService.cs:263-276, member by member.
        var opts = new BenchmarkCandidatePromptOptions();

        Assert.False(opts.VerboseMode);
        Assert.False(opts.SpoilerFreeMode);
        Assert.Equal(0, opts.OverseerMode);
        Assert.True(opts.EnableToolUse);
        Assert.False(opts.EnableWebSearch);
        Assert.True(opts.AllowSourceCodeReferences);
        Assert.False(opts.EnableSubAgents);
        Assert.False(opts.IsGameOn);
        Assert.False(opts.DeveloperMode);
        Assert.False(opts.HasMessageHistory);
        Assert.False(opts.HasWikiContext);
        Assert.False(opts.HasGameSnapshot);
    }

    [Fact]
    public void CandidatePromptOptions_CanonicalJson_RoundTripsAccurately()
    {
        var original = new BenchmarkCandidatePromptOptions
        {
            VerboseMode = true,
            HasGameSnapshot = true
        };

        string json = original.ToCanonicalJson();
        var roundTripped = BenchmarkCandidatePromptOptions.FromJson(json);

        Assert.NotNull(roundTripped);
        Assert.True(roundTripped.VerboseMode);
        Assert.True(roundTripped.HasGameSnapshot);
        Assert.Equal(original.EnableToolUse, roundTripped.EnableToolUse);
        Assert.Equal(original.AllowSourceCodeReferences, roundTripped.AllowSourceCodeReferences);
        Assert.Equal(original.OverseerMode, roundTripped.OverseerMode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PopulateInstrumentFingerprint_ComputesSha256_AndConditionallyStoresText(bool storeText)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Benchmark:StoreSystemPromptText"] = storeText.ToString()
            })
            .Build();

        var service = new BenchmarkService(
            null!, null!, null!, null!, null!, null!, null!, null!,
            config,
            NullLogger<BenchmarkService>.Instance);

        var run = new BenchmarkRun();
        string prompt = "You are an assistant for GnollHack.";

        service.PopulateInstrumentFingerprint(run, prompt);

        using var sha256 = System.Security.Cryptography.SHA256.Create();
        string expectedSha = Convert.ToHexString(sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(prompt))).ToLowerInvariant();

        Assert.Equal(expectedSha, run.CandidateSystemPromptSha256);
        if (storeText)
        {
            Assert.Equal(prompt, run.CandidateSystemPromptText);
        }
        else
        {
            Assert.Null(run.CandidateSystemPromptText);
        }

        // No corpus path is configured here, so the three corpus heads stay null — "not recorded",
        // which is what every run made before a corpus was fingerprinted also carries.
        Assert.Null(run.KnowledgeBaseHeadSha);
        Assert.Null(run.WikiHeadSha);
        Assert.Null(run.SourceCodeHeadSha);
    }

    /// <summary>
    /// A configured corpus path that does not exist on disk leaves its head null rather than
    /// throwing, so a machine missing one corpus still produces a complete run.
    /// </summary>
    [Fact]
    public void PopulateInstrumentFingerprint_MissingCorpusPaths_LeaveCorpusHeadsNull()
    {
        string missing = Path.Combine(Path.GetTempPath(), "overseer-corpus-absent-" + Guid.NewGuid().ToString("N"));

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Benchmark:StoreSystemPromptText"] = "false",
                ["WikiPath"] = missing,
                ["SourceCodePath"] = missing
            })
            .Build();

        var service = new BenchmarkService(
            null!, null!, null!, null!, null!, null!, null!, null!,
            config,
            NullLogger<BenchmarkService>.Instance);

        var run = new BenchmarkRun();

        service.PopulateInstrumentFingerprint(run, "You are an assistant for GnollHack.");

        Assert.Null(run.WikiHeadSha);
        Assert.Null(run.SourceCodeHeadSha);
        Assert.NotNull(run.CandidateSystemPromptSha256);
    }

    /// <summary>
    /// A configured corpus path that exists but is not a Git working tree leaves its head null.
    /// The NetHack wiki is such a tree in production, so this is the shape of a real corpus rather
    /// than a defensive edge case.
    /// </summary>
    [Fact]
    public void PopulateInstrumentFingerprint_NonGitCorpusPaths_LeaveCorpusHeadsNull()
    {
        var tempDir = Directory.CreateTempSubdirectory("overseer-corpus-nongit-");
        try
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Benchmark:StoreSystemPromptText"] = "false",
                    ["WikiPath"] = tempDir.FullName,
                    ["SourceCodePath"] = tempDir.FullName
                })
                .Build();

            var service = new BenchmarkService(
                null!, null!, null!, null!, null!, null!, null!, null!,
                config,
                NullLogger<BenchmarkService>.Instance);

            var run = new BenchmarkRun();

            service.PopulateInstrumentFingerprint(run, "You are an assistant for GnollHack.");

            Assert.Null(run.WikiHeadSha);
            Assert.Null(run.SourceCodeHeadSha);
            Assert.NotNull(run.CandidateSystemPromptSha256);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    /// <summary>
    /// A corpus path that is a Git working tree records that tree's HEAD, and records it on the run
    /// column belonging to that corpus rather than on another one.
    /// </summary>
    [Fact]
    public void PopulateInstrumentFingerprint_GitCorpusPath_RecordsThatTreesHead()
    {
        var tempDir = Directory.CreateTempSubdirectory("overseer-corpus-git-");
        try
        {
            string gitDir = Path.Combine(tempDir.FullName, ".git");
            Directory.CreateDirectory(gitDir);
            File.WriteAllText(Path.Combine(gitDir, "HEAD"), "ref: refs/heads/main\n");
            Directory.CreateDirectory(Path.Combine(gitDir, "refs", "heads"));
            string expectedSha = new string('a', 40);
            File.WriteAllText(Path.Combine(gitDir, "refs", "heads", "main"), expectedSha + "\n");

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Benchmark:StoreSystemPromptText"] = "false",
                    ["WikiPath"] = tempDir.FullName
                })
                .Build();

            var service = new BenchmarkService(
                null!, null!, null!, null!, null!, null!, null!, null!,
                config,
                NullLogger<BenchmarkService>.Instance);

            var run = new BenchmarkRun();

            service.PopulateInstrumentFingerprint(run, "You are an assistant for GnollHack.");

            Assert.Equal(expectedSha, run.WikiHeadSha);
            Assert.Null(run.SourceCodeHeadSha);
            Assert.Null(run.KnowledgeBaseHeadSha);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    private sealed class FixedCorpusIndexFingerprintProvider : CorpusIndexFingerprintProvider
    {
        private readonly IReadOnlyDictionary<string, CorpusContentFingerprint?> _snapshot;

        public FixedCorpusIndexFingerprintProvider(IReadOnlyDictionary<string, CorpusContentFingerprint?> snapshot)
        {
            _snapshot = snapshot;
        }

        public override IReadOnlyDictionary<string, CorpusContentFingerprint?> Snapshot() => _snapshot;
    }

    [Fact]
    public void PopulateInstrumentFingerprint_WithProvider_RecordsTheFiveCorpusIndexFingerprints()
    {
        var indexedAt = new DateTime(2026, 10, 4, 9, 30, 0, DateTimeKind.Utc);
        var snapshot = new Dictionary<string, CorpusContentFingerprint?>
        {
            ["gnollhackWiki"] = new CorpusContentFingerprint(new string('1', 64), 410, indexedAt),
            ["gnollhackSource"] = new CorpusContentFingerprint(new string('2', 64), 520, indexedAt),
            ["knowledgeBase"] = new CorpusContentFingerprint(new string('3', 64), 7, indexedAt),
            ["nethackWiki"] = null,
            ["nethackSource"] = new CorpusContentFingerprint(new string('5', 64), 300, indexedAt)
        };
        var services = new ServiceCollection();
        services.AddSingleton<CorpusIndexFingerprintProvider>(new FixedCorpusIndexFingerprintProvider(snapshot));
        using var serviceProvider = services.BuildServiceProvider();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Benchmark:StoreSystemPromptText"] = "false" })
            .Build();
        var service = new BenchmarkService(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(), null!, null!, null!, null!, null!, null!, null!,
            config,
            NullLogger<BenchmarkService>.Instance);

        var run = new BenchmarkRun();
        service.PopulateInstrumentFingerprint(run, "You are an assistant for GnollHack.");

        Assert.NotNull(run.CorpusIndexFingerprintsJson);
        using var doc = System.Text.Json.JsonDocument.Parse(run.CorpusIndexFingerprintsJson!);
        Assert.Equal(
            new[] { "gnollhackWiki", "gnollhackSource", "knowledgeBase", "nethackWiki", "nethackSource" },
            doc.RootElement.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, doc.RootElement.GetProperty("nethackWiki").ValueKind);
        Assert.Equal(new string('1', 64), doc.RootElement.GetProperty("gnollhackWiki").GetProperty("sha256").GetString());
        Assert.Equal(410, doc.RootElement.GetProperty("gnollhackWiki").GetProperty("fileCount").GetInt32());
        Assert.Equal("2026-10-04T09:30:00.000Z", doc.RootElement.GetProperty("gnollhackWiki").GetProperty("indexedAtUtc").GetString());
    }

    [Fact]
    public void PopulateInstrumentFingerprint_WithoutProvider_LeavesCorpusIndexFingerprintsNull()
    {
        using var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Benchmark:StoreSystemPromptText"] = "false" })
            .Build();
        var service = new BenchmarkService(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(), null!, null!, null!, null!, null!, null!, null!,
            config,
            NullLogger<BenchmarkService>.Instance);

        var run = new BenchmarkRun();
        service.PopulateInstrumentFingerprint(run, "You are an assistant for GnollHack.");

        Assert.Null(run.CorpusIndexFingerprintsJson);
        Assert.NotNull(run.CandidateSystemPromptSha256);
    }

    [Fact]
    public void ExtractDisputedClaims_ExtractsCriticalErrorQuote_NumericAssertions_AndCounterClaims()
    {
        var answer = new BenchmarkRunAnswer
        {
            ExpectedPointsRecorded = true,
            OrderIndex = 12,
            CriticalErrorQuote = "Exceptional body armor gives +2 AC and +1 blocking.",
            AnswerText = "Exceptional body armor gives +2 AC and +1 blocking. Elite shields give +3 AC and +1 blocking.",
            ReviewComment = "Completely hallucinates the mechanics."
        };

        string accuracyEvidence = "In GnollHack, exceptional body armor gives -4 AC / +1 MC, shields -3 AC / +1 MC; elite gives -8 AC / +2 MC.";

        var claims = BenchmarkService.ExtractDisputedClaims(answer, accuracyEvidence);

        // The answer's sentences and the assessor's counter-claim come back apart.
        Assert.Contains(claims.AnswerClaims, c => c.Contains("+2 AC"));
        Assert.DoesNotContain(claims.AnswerClaims, c => c.Contains("-4 AC"));
        Assert.Contains(claims.AssessorStatements, c => c.Contains("-4 AC"));
    }

    // --- 11. Second-Opinion Sample Top-Up (FlaggedPlusSample) ---

    [Fact]
    public async Task SecondOpinionSampleTopUp_SelectsTheLowestScoringAnswers_TiesByOrderIndex()
    {
        // Scores 40, 40, 55 are the three lowest; the pair at 40 resolves to Q2 and Q4 by ascending
        // order index, which is the tie-break that makes the selection reproducible rather than
        // merely small. The three run concurrently, so the set is what is asserted, not its order.
        var (run, attempted) = await RunSampleTopUpAsync(
            BenchmarkSecondOpinionMode.FlaggedPlusSample,
            minimumSample: 3,
            answers: new[] { (1, 90), (2, 40), (3, 70), (4, 40), (5, 55) });

        Assert.Equal(new[] { 2, 4, 5 }, attempted.OrderBy(i => i).ToArray());
        Assert.Equal(3, run.SecondOpinionSampleCountUsed);
    }

    [Fact]
    public async Task SecondOpinionSampleTopUp_IsStableUnderReorderingOfTheInput()
    {
        // Same answers, inserted in a different order, so the underlying query returns them in a
        // different order too. The selected *set* must not move: a re-run that reshuffled the
        // sample could be used to fish for a different agreement figure.
        var (run, attempted) = await RunSampleTopUpAsync(
            BenchmarkSecondOpinionMode.FlaggedPlusSample,
            minimumSample: 3,
            answers: new[] { (5, 55), (3, 70), (1, 90), (4, 40), (2, 40) });

        Assert.Equal(new[] { 2, 4, 5 }, attempted.OrderBy(i => i).ToArray());
        Assert.Equal(3, run.SecondOpinionSampleCountUsed);
    }

    [Fact]
    public async Task SecondOpinionSampleTopUp_IsANoOp_UnderFlagged()
    {
        // Flagged is the mode every pre-existing profile carries. The top-up must not reach it,
        // or the migration would have changed what every existing profile costs per run.
        var (run, attempted) = await RunSampleTopUpAsync(
            BenchmarkSecondOpinionMode.Flagged,
            minimumSample: 3,
            answers: new[] { (1, 90), (2, 40), (3, 70), (4, 40), (5, 55) });

        Assert.Empty(attempted);
        Assert.Equal(0, run.SecondOpinionSampleCountUsed);
    }

    [Fact]
    public async Task SecondOpinionSampleTopUp_CountsAnswersAlreadyGradedTwice_TowardTheTarget()
    {
        // The trigger path may already have covered part of the target. Topping up to the full
        // minimum on top of those would over-spend on a run that was already sampled.
        var (run, attempted) = await RunSampleTopUpAsync(
            BenchmarkSecondOpinionMode.FlaggedPlusSample,
            minimumSample: 3,
            answers: new[] { (1, 90), (2, 40), (3, 70), (4, 40), (5, 55) },
            alreadyGradedOrderIndexes: new[] { 2, 4 });

        Assert.Equal(new[] { 5 }, attempted);
        Assert.Equal(3, run.SecondOpinionSampleCountUsed);
    }

    /// <summary>
    /// Drives <c>RunSecondOpinionSampleTopUpAsync</c> and reports the order indexes it actually
    /// attempted to re-grade, in the order the attempts logged. The stage runs the selected
    /// opinions concurrently, so that is completion order and not selection order: a caller
    /// asserting which answers were selected sorts this list first.
    ///
    /// The stage is private and has no seam of its own, so it is reached by reflection rather than
    /// by changing production code for a test's convenience. The run's second-opinion assessor
    /// points at a configuration id that does not exist, which makes <c>ResolveAssessorAsync</c>
    /// return its "not found" error before any model call: each selected answer then produces one
    /// warning naming its order index, and nothing leaves the process. That warning is the only
    /// per-answer trace the stage leaves, which is why the logger is captured here.
    /// </summary>
    private static async Task<(BenchmarkRun Run, List<int> Attempted)> RunSampleTopUpAsync(
        BenchmarkSecondOpinionMode mode,
        int minimumSample,
        (int OrderIndex, int QualityScore)[] answers,
        int[]? alreadyGradedOrderIndexes = null)
    {
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        long runId;
        await using (var seedDb = new ApplicationDbContext(dbOptions))
        {
            var seededRun = BenchmarkModelSnapshots.Attach(new BenchmarkRun
            {
                SuiteName = "Suite",
                TestedModelSnapshot = BenchmarkModelSnapshots.Model(provider: "Provider T", modelId: "model-t", displayName: "Model T"),
                AssessorModelSnapshot = BenchmarkModelSnapshots.Model(provider: "Provider A", modelId: "model-a", displayName: "Model A"),
                SecondOpinionAssessorModelConfigurationId = 999,
                SecondOpinionModeUsed = (int)mode,
                Status = BenchmarkRunStatus.Running,
                StartedAtUtc = DateTime.UtcNow
            });
            seedDb.BenchmarkRuns.Add(seededRun);
            await seedDb.SaveChangesAsync();
            runId = seededRun.Id;

            foreach (var (orderIndex, qualityScore) in answers)
            {
                seedDb.BenchmarkRunAnswers.Add(new BenchmarkRunAnswer
                {
                    ExpectedPointsRecorded = true,
                    BenchmarkRunId = runId,
                    OrderIndex = orderIndex,
                    QuestionText = $"Q{orderIndex}",
                    AnswerText = $"Answer {orderIndex}",
                    Status = BenchmarkAnswerStatus.Ok,
                    AssessmentStatus = BenchmarkAssessmentStatus.Scored,
                    QualityScore = qualityScore,
                    RawQualityScore = qualityScore,
                    SecondOpinionQualityScore = (alreadyGradedOrderIndexes?.Contains(orderIndex) ?? false)
                        ? qualityScore
                        : null
                });
            }

            await seedDb.SaveChangesAsync();
        }

        var logger = new RecordingLogger<BenchmarkService>();

        // Real, not null: the stage runs each selected opinion in its own DI scope, so it needs a
        // scope factory that hands out a context over the same in-memory database the answers were
        // seeded into.
        var scopeServices = new ServiceCollection();
        scopeServices.AddScoped(_ => new ApplicationDbContext(dbOptions));
        scopeServices.AddScoped(sp => new SystemAiConfigService(
            sp.GetRequiredService<ApplicationDbContext>(),
            NullLogger<SystemAiConfigService>.Instance));
        var scopeFactory = scopeServices.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        var benchmarkService = new BenchmarkService(
            scopeFactory,
            null!,
            null!,
            null!,
            // Real, not null: the second-opinion path marks each answer in flight so the progress
            // dialog can show a re-graded row as re-grading. The instance is inert for a run it
            // does not own, which is every run here.
            new BenchmarkRunManager(),
            null!,
            null!,
            null!,
            new ConfigurationBuilder().Build(),
            logger);

        var topUp = typeof(BenchmarkService).GetMethod(
            "RunSecondOpinionSampleTopUpAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(topUp);

        var profile = new BenchmarkScoringProfile
        {
            Name = "Profile Under Test",
            SecondOpinionMode = (int)mode,
            SecondOpinionMinimumSample = minimumSample
        };

        await using var db = new ApplicationDbContext(dbOptions);
        var run = await db.BenchmarkRuns.FirstAsync(r => r.Id == runId);

        await (Task)topUp!.Invoke(benchmarkService, new object?[]
        {
            db,
            null,
            run,
            profile,
            BenchmarkScoringConstants.Default,
            CancellationToken.None
        })!;

        var attempted = logger.Messages
            .Select(m => Regex.Match(m, @"answer (\d+): second reader \d+ unusable"))
            .Where(m => m.Success)
            .Select(m => int.Parse(m.Groups[1].Value))
            .ToList();

        return (run, attempted);
    }

    /// <summary>
    /// Records formatted log messages so a test can assert which answers a stage acted on. Kept
    /// deliberately minimal: nothing here filters, so a message the production code stops writing
    /// fails the assertion rather than silently passing it.
    /// </summary>
    private sealed class RecordingLogger<T> : ILogger<T>
    {
        private readonly List<string> _messages = new List<string>();

        // The stages under test log from concurrent tasks, so both ends are locked and a reader
        // gets a snapshot rather than a list another task may be appending to.
        public List<string> Messages
        {
            get { lock (_messages) { return new List<string>(_messages); } }
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        // Fully qualified: MobileGnollHackLogger.Data also declares a LogLevel, and this file
        // has both namespaces in scope.
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (_messages)
            {
                _messages.Add(formatter(state, exception));
            }
        }
    }

    // --- 12. Claim Verification Token Budget ---

    [Fact]
    public async Task RunClaimVerificationAsync_ExhaustedTokenBudget_MarksRemainingAnswersNotChecked()
    {
        // Q1 has already spent 5,000 verifier input tokens on this run, which is past the 1,000
        // budget, so Q2 and Q3 must be marked before any call is made. The budget is seeded from
        // what the *run* has spent, not from zero, so a second pass cannot spend it twice.
        var answers = await RunClaimVerificationWithBudgetAsync(tokenBudget: 1000);

        var notChecked = answers.Where(a => a.OrderIndex > 1).ToList();
        Assert.Equal(2, notChecked.Count);
        Assert.All(notChecked, a =>
        {
            Assert.NotNull(a.ClaimVerificationError);
            Assert.StartsWith(BenchmarkService.ClaimVerificationNotCheckedPrefix, a.ClaimVerificationError, StringComparison.Ordinal);
            Assert.Contains("1,000", a.ClaimVerificationError!, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task RunClaimVerificationAsync_ZeroTokenBudget_SkipsNoAnswer()
    {
        // 0 is "unlimited", the default, and it is what every existing run and every operator who
        // never sets the key gets: no answer may be stamped NotChecked under it.
        var answers = await RunClaimVerificationWithBudgetAsync(tokenBudget: 0);

        Assert.All(answers.Where(a => a.OrderIndex > 1), a => Assert.Null(a.ClaimVerificationError));
    }

    /// <summary>
    /// Runs claim verification over three answers — Q1 already verified and carrying the run's
    /// spent verifier tokens, Q2 and Q3 pending — under the given
    /// <c>Benchmark:ClaimVerificationInputTokenBudget</c>.
    ///
    /// The pending answers declare claims but carry an empty claims array, so an answer the budget
    /// does *not* stop returns before reaching the agent loop. That is what lets the loop runner be
    /// null: if the budget check ever let an answer through to a real verifier call, the test would
    /// fail with a NullReferenceException rather than pass quietly.
    /// </summary>
    private static async Task<List<BenchmarkRunAnswer>> RunClaimVerificationWithBudgetAsync(int tokenBudget)
    {
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var dummyKey = Convert.ToBase64String(new byte[32]);
        var settings = new Dictionary<string, string?>
        {
            ["AesEncryptionKey"] = dummyKey
        };
        if (tokenBudget > 0)
        {
            settings["Benchmark:ClaimVerificationInputTokenBudget"] = tokenBudget.ToString();
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var crypto = new CryptoService(configuration);
        var (cipher, nonce, tag) = crypto.Encrypt("test-api-key", "SYSTEM_API_KEY");

        long runId;
        await using (var seedDb = new ApplicationDbContext(dbOptions))
        {
            var verifierConfig = new SystemAiApiConfiguration
            {
                Id = 10,
                DisplayName = "Verifier Model",
                ModelId = "model-v",
                Provider = "Anthropic",
                ModelRole = 4, // Assessor / verifier
                IsEnabled = true,
                EncryptedApiKey = cipher,
                ApiKeyNonce = nonce,
                ApiKeyTag = tag
            };
            seedDb.SystemAiApiConfigurations.Add(verifierConfig);

            var run = new BenchmarkRun
            {
                SuiteName = "Suite",
                TestedModelSnapshot = BenchmarkModelSnapshots.Model(provider: "Provider T", modelId: "model-t", displayName: "Model T"),
                AssessorModelSnapshot = BenchmarkModelSnapshots.Model(provider: "Provider A", modelId: "model-a", displayName: "Model A"),
                ClaimVerifierModelConfigurationId = 10,
                // The provider must match verifierConfig above, or SystemAiConfigurationSnapshotStore.Bind
                // refuses the pairing before RunClaimVerificationAsync can reach the token budget gate.
                ClaimVerifierModelSnapshot = SystemAiConfigurationSnapshotStore.FromConfiguration(verifierConfig),
                Status = BenchmarkRunStatus.Running,
                StartedAtUtc = DateTime.UtcNow
            };
            seedDb.BenchmarkRuns.Add(run);
            await seedDb.SaveChangesAsync();
            runId = run.Id;

            seedDb.BenchmarkRunAnswers.Add(new BenchmarkRunAnswer
            {
                ExpectedPointsRecorded = true,
                BenchmarkRunId = runId,
                OrderIndex = 1,
                QuestionText = "Question 1",
                AnswerText = "Answer 1",
                Status = BenchmarkAnswerStatus.Ok,
                UnverifiedClaimCount = 2,
                UnverifiedClaimsJson = "[]",
                ClaimVerificationJson = "[]",
                ClaimVerificationInputTokens = 5000
            });

            foreach (int orderIndex in new[] { 2, 3 })
            {
                seedDb.BenchmarkRunAnswers.Add(new BenchmarkRunAnswer
                {
                    ExpectedPointsRecorded = true,
                    BenchmarkRunId = runId,
                    OrderIndex = orderIndex,
                    QuestionText = $"Question {orderIndex}",
                    AnswerText = $"Answer {orderIndex}",
                    Status = BenchmarkAnswerStatus.Ok,
                    UnverifiedClaimCount = 2,
                    UnverifiedClaimsJson = "[]"
                });
            }

            await seedDb.SaveChangesAsync();
        }

        var services = new ServiceCollection();
        services.AddScoped(_ => new ApplicationDbContext(dbOptions));
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        var benchmarkService = new BenchmarkService(
            scopeFactory,
            null!,
            null!,
            crypto,
            new BenchmarkRunManager(),
            new BenchmarkDifficultyJobManager(),
            null!,
            new Overseer.Services.Privacy.EndpointPolicy(configuration),
            configuration,
            NullLogger<BenchmarkService>.Instance);

        await using var db = new ApplicationDbContext(dbOptions);
        var loadedRun = await db.BenchmarkRuns.FirstAsync(r => r.Id == runId);

        await benchmarkService.RunClaimVerificationAsync(db, null!, loadedRun, CancellationToken.None);

        return await db.BenchmarkRunAnswers
            .Where(a => a.BenchmarkRunId == runId)
            .OrderBy(a => a.OrderIndex)
            .ToListAsync();
    }
    // --- 13. Claim-verifier long-context buckets ---

    private static readonly ModelPricing HaikuVerifierCard =
        new(0.10m, 0.50m, CachedInputPerMillion: 0.01m, CacheWritePerMillion: 0.125m,
            LongContext: new LongContextPricing(100_000, 0.50m, 2.50m, 0.05m, 0.625m));

    private static List<Overseer.Services.Providers.TokenUsageReport> VerifierCalls() => new()
    {
        new() { TotalPromptTokens = 50_000, CacheReadTokens = 30_000, CacheCreationTokens = 2_000, UncachedInputTokens = 20_000, OutputTokens = 800 },
        new() { TotalPromptTokens = 120_000, CacheReadTokens = 90_000, CacheCreationTokens = 5_000, UncachedInputTokens = 30_000, OutputTokens = 1_500 }
    };

    [Fact]
    public void RecordClaimVerificationModelCalls_LongContextCard_BucketsOnlyTheCallOverTheThreshold()
    {
        var answer = new BenchmarkRunAnswer();

        BenchmarkService.RecordClaimVerificationModelCalls(
            answer, 2, VerifierCalls(), null, verifierPricing: HaikuVerifierCard);

        Assert.Equal(120_000, answer.ClaimVerificationLongContextInputTokens);
        Assert.Equal(1_500, answer.ClaimVerificationLongContextOutputTokens);
        Assert.Equal(90_000, answer.ClaimVerificationLongContextCacheReadTokens);
        Assert.Equal(5_000, answer.ClaimVerificationLongContextCacheCreationTokens);
    }

    [Fact]
    public void RecordClaimVerificationModelCalls_NullOrFlatCard_SetsZeros()
    {
        foreach (var card in new[] { null, HaikuVerifierCard with { LongContext = null } })
        {
            var answer = new BenchmarkRunAnswer();

            BenchmarkService.RecordClaimVerificationModelCalls(answer, 2, VerifierCalls(), null, verifierPricing: card);

            Assert.Equal(0, answer.ClaimVerificationLongContextInputTokens);
            Assert.Equal(0, answer.ClaimVerificationLongContextOutputTokens);
            Assert.Equal(0, answer.ClaimVerificationLongContextCacheReadTokens);
            Assert.Equal(0, answer.ClaimVerificationLongContextCacheCreationTokens);
        }
    }

    [Fact]
    public void ApplyTotals_SumsTheClaimVerifierLongContextColumnsAcrossAnswers()
    {
        var run = BenchmarkModelSnapshots.Attach(new BenchmarkRun { Id = 1, TotalQuestionCount = 3 });
        var answers = new List<BenchmarkRunAnswer>
        {
            new() { OrderIndex = 1, ClaimVerificationLongContextInputTokens = 120_000, ClaimVerificationLongContextOutputTokens = 1_500,
                ClaimVerificationLongContextCacheReadTokens = 90_000, ClaimVerificationLongContextCacheCreationTokens = 5_000 },
            new() { OrderIndex = 2, ClaimVerificationLongContextInputTokens = 110_000, ClaimVerificationLongContextOutputTokens = 700,
                ClaimVerificationLongContextCacheReadTokens = 80_000, ClaimVerificationLongContextCacheCreationTokens = 1_000 },
            // Recorded before the columns existed.
            new() { OrderIndex = 3 }
        };

        BenchmarkRunFinalizer.ApplyTotals(run, answers);

        Assert.Equal(230_000, run.TotalClaimVerificationLongContextInputTokens);
        Assert.Equal(2_200, run.TotalClaimVerificationLongContextOutputTokens);
        Assert.Equal(170_000, run.TotalClaimVerificationLongContextCacheReadTokens);
        Assert.Equal(6_000, run.TotalClaimVerificationLongContextCacheCreationTokens);

        var live = BenchmarkRunCostEstimator.BuildLiveTotals(new BenchmarkRun
        {
            Id = 1,
            TestedModelSnapshot = run.TestedModelSnapshot,
            Answers = answers
        });
        Assert.Equal(230_000, live.TotalClaimVerificationLongContextInputTokens);
        Assert.Equal(6_000, live.TotalClaimVerificationLongContextCacheCreationTokens);
    }

    // --- 14. Candidate failure attribution (harness 56) ---

    private static readonly ProviderErrorClassification NotAProviderError = new(false, null, "Not a provider error");

    /// <summary>A candidate turn whose single call ended on <paramref name="attempts"/>, the last of them terminal.</summary>
    private static Overseer.Services.Agents.AgentRunResult TurnEndingOn(params ProviderAttemptOutcome[] attempts)
    {
        var call = new Overseer.Services.Agents.ModelCallRecord { Provider = "OpenAI", RequestedModelId = "gpt-6.1-sol" };
        for (int i = 0; i < attempts.Length; i++)
        {
            call.Attempts.Add(new Overseer.Services.Agents.ProviderAttemptRecord { AttemptIndex = i, Outcome = attempts[i] });
        }

        var result = new Overseer.Services.Agents.AgentRunResult();
        result.ModelCalls.Add(call);
        if (attempts.Length > 0 && attempts[^1] != ProviderAttemptOutcome.Ok)
        {
            result.TerminalOutcome = attempts[^1];
        }

        return result;
    }

    private static BenchmarkService.CandidateOutcome Attribute(
        Overseer.Services.Agents.AgentRunResult runResult,
        bool recorded = true,
        string? terminalError = "503 Unavailable. Max retries exceeded.",
        ProviderErrorClassification? classification = null,
        bool canceledByOperator = false,
        bool operatorCanceled = false,
        bool questionBudgetCanceled = false,
        bool emptyAnswer = false)
        => BenchmarkService.AttributeCandidateOutcome(
            recorded, canceledByOperator, operatorCanceled, questionBudgetCanceled, terminalError,
            classification ?? NotAProviderError, emptyAnswer, runResult);

    [Theory]
    [InlineData(ProviderAttemptOutcome.ProviderOverloaded, BenchmarkAnswerStatus.ProviderError, BenchmarkFailureOrigin.Provider)]
    [InlineData(ProviderAttemptOutcome.ProviderServerError, BenchmarkAnswerStatus.ProviderError, BenchmarkFailureOrigin.Provider)]
    [InlineData(ProviderAttemptOutcome.ProviderTimeout, BenchmarkAnswerStatus.ProviderError, BenchmarkFailureOrigin.Provider)]
    [InlineData(ProviderAttemptOutcome.ProviderStreamFailure, BenchmarkAnswerStatus.ProviderError, BenchmarkFailureOrigin.Provider)]
    [InlineData(ProviderAttemptOutcome.RateLimitUnattributed, BenchmarkAnswerStatus.ProviderError, BenchmarkFailureOrigin.Unattributed)]
    [InlineData(ProviderAttemptOutcome.TransportUnattributed, BenchmarkAnswerStatus.ProviderError, BenchmarkFailureOrigin.Unattributed)]
    [InlineData(ProviderAttemptOutcome.OurRateLimit, BenchmarkAnswerStatus.Failed, BenchmarkFailureOrigin.Ours)]
    [InlineData(ProviderAttemptOutcome.OurBilling, BenchmarkAnswerStatus.Failed, BenchmarkFailureOrigin.Ours)]
    [InlineData(ProviderAttemptOutcome.OurKey, BenchmarkAnswerStatus.Failed, BenchmarkFailureOrigin.Ours)]
    [InlineData(ProviderAttemptOutcome.OurRequest, BenchmarkAnswerStatus.Failed, BenchmarkFailureOrigin.Ours)]
    [InlineData(ProviderAttemptOutcome.OurThrottle, BenchmarkAnswerStatus.Failed, BenchmarkFailureOrigin.Ours)]
    [InlineData(ProviderAttemptOutcome.OurNetwork, BenchmarkAnswerStatus.Failed, BenchmarkFailureOrigin.Ours)]
    public void ARecordedTerminalFailure_IsAttributedToTheAttemptThatEndedIt(
        ProviderAttemptOutcome terminal, BenchmarkAnswerStatus expectedStatus, BenchmarkFailureOrigin expectedOrigin)
    {
        // The classifier's verdict is overridden by the attempt record, whichever way it points.
        var outcome = Attribute(
            TurnEndingOn(ProviderAttemptOutcome.ProviderOverloaded, terminal),
            classification: new ProviderErrorClassification(true, 503, "Provider error"));

        Assert.Equal(expectedStatus, outcome.Status);
        Assert.Equal(expectedOrigin, outcome.FailureOrigin);
        Assert.Equal(terminal.ToString(), outcome.FailureKind);
    }

    [Fact]
    public void OurQuestionBudget_IsAFailedAnswerOnOurSide()
    {
        var caught = Attribute(
            TurnEndingOn(ProviderAttemptOutcome.OurCanceled),
            terminalError: "Per-question timeout exceeded (600 s) after 3 model call(s) and 2 tool call(s); …",
            questionBudgetCanceled: true);
        Assert.Equal(new BenchmarkService.CandidateOutcome(
            BenchmarkAnswerStatus.Failed, BenchmarkFailureOrigin.Ours, BenchmarkService.QuestionBudgetFailureKind), caught);

        // The runner's own record of the budget cancel is enough, whatever the catch saw.
        var turn = TurnEndingOn(ProviderAttemptOutcome.OurCanceled);
        turn.TerminalQuestionBudgetCanceled = true;
        var recorded = Attribute(turn, terminalError: "Request canceled.");
        Assert.Equal(BenchmarkService.QuestionBudgetFailureKind, recorded.FailureKind);
        Assert.Equal(BenchmarkAnswerStatus.Failed, recorded.Status);
    }

    [Theory]
    [InlineData(false, BenchmarkAnswerStatus.Ok)]
    [InlineData(true, BenchmarkAnswerStatus.EmptyAnswer)]
    public void ARecordedAnswerThatDidNotFail_HasOriginNone(bool emptyAnswer, BenchmarkAnswerStatus expectedStatus)
    {
        // A failed attempt that was retried into a good answer leaves no failure on the answer.
        var outcome = Attribute(
            TurnEndingOn(ProviderAttemptOutcome.ProviderOverloaded, ProviderAttemptOutcome.Ok),
            terminalError: null, emptyAnswer: emptyAnswer);

        Assert.Equal(new BenchmarkService.CandidateOutcome(expectedStatus, BenchmarkFailureOrigin.None, null), outcome);
    }

    [Fact]
    public void AnOperatorCancel_IsCanceled_OnEveryRun()
    {
        var recorded = Attribute(TurnEndingOn(ProviderAttemptOutcome.OurCanceled), canceledByOperator: true, operatorCanceled: true);
        Assert.Equal(new BenchmarkService.CandidateOutcome(
            BenchmarkAnswerStatus.Canceled, BenchmarkFailureOrigin.Ours, nameof(ProviderAttemptOutcome.OurCanceled)), recorded);

        var unrecorded = Attribute(
            TurnEndingOn(ProviderAttemptOutcome.OurCanceled), recorded: false, canceledByOperator: true, operatorCanceled: true);
        Assert.Equal(new BenchmarkService.CandidateOutcome(BenchmarkAnswerStatus.Canceled, null, null), unrecorded);
    }

    [Fact]
    public void AnOperatorCancelAfterAProviderError_KeepsTheProviderError()
    {
        // The cancel cut the retry that followed a 500; the 500 is what ended the answer.
        var outcome = Attribute(
            TurnEndingOn(ProviderAttemptOutcome.ProviderServerError, ProviderAttemptOutcome.OurCanceled),
            operatorCanceled: true);

        Assert.Equal(BenchmarkAnswerStatus.ProviderError, outcome.Status);
        Assert.Equal(BenchmarkFailureOrigin.Provider, outcome.FailureOrigin);
        Assert.Equal(nameof(ProviderAttemptOutcome.ProviderServerError), outcome.FailureKind);
    }

    [Theory]
    [InlineData(503, ProviderAttemptOutcome.ProviderOverloaded, BenchmarkFailureOrigin.Provider)]
    [InlineData(529, ProviderAttemptOutcome.ProviderOverloaded, BenchmarkFailureOrigin.Provider)]
    [InlineData(500, ProviderAttemptOutcome.ProviderServerError, BenchmarkFailureOrigin.Provider)]
    [InlineData(408, ProviderAttemptOutcome.ProviderTimeout, BenchmarkFailureOrigin.Provider)]
    [InlineData(429, ProviderAttemptOutcome.RateLimitUnattributed, BenchmarkFailureOrigin.Unattributed)]
    [InlineData(null, ProviderAttemptOutcome.TransportUnattributed, BenchmarkFailureOrigin.Unattributed)]
    public void WithoutAFailedAttempt_TheClassifiersProviderVerdictIsMappedOntoAClass(
        int? httpStatus, ProviderAttemptOutcome expectedKind, BenchmarkFailureOrigin expectedOrigin)
    {
        var outcome = Attribute(
            new Overseer.Services.Agents.AgentRunResult(),
            classification: new ProviderErrorClassification(true, httpStatus, "Provider error"));

        Assert.Equal(BenchmarkAnswerStatus.ProviderError, outcome.Status);
        Assert.Equal(expectedOrigin, outcome.FailureOrigin);
        Assert.Equal(expectedKind.ToString(), outcome.FailureKind);
    }

    [Fact]
    public void WithoutAFailedAttempt_AFailureTheClassifierDoesNotCallAProviderError_IsOurs()
    {
        // A delivery check that refused the request before any attempt, for one.
        var outcome = Attribute(
            new Overseer.Services.Agents.AgentRunResult(),
            terminalError: "Harness delivery check failed: the board did not reach the candidate.");

        Assert.Equal(new BenchmarkService.CandidateOutcome(BenchmarkAnswerStatus.Failed, BenchmarkFailureOrigin.Ours, null), outcome);
    }

    [Fact]
    public void AnUnrecordedRun_KeepsTheClassifiersStatus_AndCarriesNoAttribution()
    {
        var turn = TurnEndingOn(ProviderAttemptOutcome.OurBilling);

        var providerError = Attribute(turn, recorded: false, classification: new ProviderErrorClassification(true, 429, "Rate limited"));
        Assert.Equal(new BenchmarkService.CandidateOutcome(BenchmarkAnswerStatus.ProviderError, null, null), providerError);

        var failed = Attribute(turn, recorded: false);
        Assert.Equal(new BenchmarkService.CandidateOutcome(BenchmarkAnswerStatus.Failed, null, null), failed);

        var ok = Attribute(new Overseer.Services.Agents.AgentRunResult(), recorded: false, terminalError: null);
        Assert.Equal(new BenchmarkService.CandidateOutcome(BenchmarkAnswerStatus.Ok, null, null), ok);
    }

    // --- 15. Repair passes ---

    [Fact]
    public void BeginRepairPass_CountsEveryPass_AndAProviderPassWhenAReExecutedAnswerLastFailedAtTheProvider()
    {
        var run = new BenchmarkRun { Id = 1 };

        BenchmarkService.BeginRepairPass(run, new[]
        {
            new BenchmarkRunAnswer { Status = BenchmarkAnswerStatus.Failed, FailureOrigin = BenchmarkFailureOrigin.Ours },
            new BenchmarkRunAnswer { Status = BenchmarkAnswerStatus.ProviderError, FailureOrigin = BenchmarkFailureOrigin.Unattributed }
        });
        Assert.Equal(1, run.RepairPassCount);
        Assert.Equal(0, run.ProviderRepairPassCount);

        BenchmarkService.BeginRepairPass(run, new[]
        {
            new BenchmarkRunAnswer { Status = BenchmarkAnswerStatus.ProviderError, FailureOrigin = BenchmarkFailureOrigin.Provider },
            new BenchmarkRunAnswer { Status = BenchmarkAnswerStatus.Failed, FailureOrigin = BenchmarkFailureOrigin.Ours }
        });
        Assert.Equal(2, run.RepairPassCount);
        Assert.Equal(1, run.ProviderRepairPassCount);

        // An unrecorded answer carries no origin, so its pass is never provider-caused; a
        // single-answer re-run of a good answer is still a pass.
        BenchmarkService.BeginRepairPass(run, new[] { new BenchmarkRunAnswer { Status = BenchmarkAnswerStatus.ProviderError } });
        BenchmarkService.BeginRepairPass(run, new[] { new BenchmarkRunAnswer { Status = BenchmarkAnswerStatus.Ok, FailureOrigin = BenchmarkFailureOrigin.None } });
        Assert.Equal(4, run.RepairPassCount);
        Assert.Equal(1, run.ProviderRepairPassCount);
    }

    [Fact]
    public void RecordsProviderLedger_OnlyForTheCurrentLedgerVersion()
    {
        Assert.True(BenchmarkService.RecordsProviderLedger(new BenchmarkRun { ProviderLedgerVersion = BenchmarkService.CurrentProviderLedgerVersion }));
        Assert.False(BenchmarkService.RecordsProviderLedger(new BenchmarkRun { ProviderLedgerVersion = null }));
    }
}


