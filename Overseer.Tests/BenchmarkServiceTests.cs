namespace Overseer.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MobileGnollHackLogger.Data;
using Overseer.Services;
using Overseer.Services.Benchmarking;
using Xunit;

public class BenchmarkServiceTests
{
    // --- 1. Provider Error Classifier Tests ---

    [Theory]
    [InlineData("429 Rate Limited. Max retries exceeded.", true, 429)]
    [InlineData("Rate limit reached for requests per minute.", true, 429)]
    [InlineData("529 Overloaded error from Anthropic.", true, 529)]
    [InlineData("[overloaded_error] Service is overloaded.", true, 529)]
    [InlineData("503 Unavailable. Max retries exceeded.", true, 503)]
    [InlineData("502 Bad Gateway from cloudflare.", true, 502)]
    [InlineData("504 Gateway Timeout.", true, 504)]
    [InlineData("The request to Anthropic timed out after 300 seconds.", true, 408)]
    [InlineData("API Error: 503 - Service temporarily unavailable.", true, 503)]
    [InlineData("The requested monster could not be found in the database.", false, null)]
    [InlineData("Syntax error in user query.", false, null)]
    public void Classify_CorrectlyIdentifiesProviderErrors(string message, bool expectedIsProvider, int? expectedStatus)
    {
        var result = BenchmarkProviderErrorClassifier.Classify(message);
        Assert.Equal(expectedIsProvider, result.IsProviderError);
        Assert.Equal(expectedStatus, result.HttpStatus);
    }

    // --- 2. Answer Sanitizer Tests ---

    [Fact]
    public void Sanitize_StripsThoughtDivsAndExtractsThoughtText()
    {
        string raw = "<div class=\"ai-thought\">Thinking through the Gnoll Barbarian starting stats...</div>\n\nA Gnoll Barbarian starts with high Strength and Constitution.";
        var sanitized = BenchmarkAnswerSanitizer.Sanitize(raw);

        Assert.Equal("A Gnoll Barbarian starts with high Strength and Constitution.", sanitized.AnswerText);
        Assert.Equal("Thinking through the Gnoll Barbarian starting stats...", sanitized.ThoughtText);
    }

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

    // --- 3. Per-Question Assessment Parser Tests ---

    [Fact]
    public void ParsePerQuestion_ParsesValidBARSJson()
    {
        string json = @"```json
{
  ""accuracyLevel"": 5,
  ""completenessLevel"": 4,
  ""concisenessLevel"": 6,
  ""readabilityLevel"": 5,
  ""criticalError"": false,
  ""comment"": ""Accurate answer with all essential details.""
}
```";

        var parseResult = BenchmarkAssessmentParser.ParsePerQuestion(json);

        Assert.True(parseResult.Success);
        Assert.NotNull(parseResult.Result);
        Assert.Equal(5, parseResult.Result.AccuracyLevel);
        Assert.Equal(4, parseResult.Result.CompletenessLevel);
        Assert.Equal(6, parseResult.Result.ConcisenessLevel);
        Assert.Equal(5, parseResult.Result.ReadabilityLevel);
        Assert.False(parseResult.Result.CriticalError);
        Assert.Equal("Accurate answer with all essential details.", parseResult.Result.Comment);
    }

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

    // --- 4. Final Synthesis Parser Tests ---

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

    // --- 5. Difficulty Parser Tests ---

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

    // --- 6. Report Builder Tests ---

    [Fact]
    public void BuildMarkdownReport_GeneratesAllSectionsWithIndices()
    {
        var run = new BenchmarkRun
        {
            Id = 101,
            SuiteName = "GnollHack Core Suite",
            TestedModelDisplayNameUsed = "Claude 3.5 Sonnet",
            TestedModelProviderUsed = "Anthropic",
            TestedModelIdUsed = "claude-3-5-sonnet-20241022",
            AssessorModelDisplayNameUsed = "Claude Opus",
            AssessorModelProviderUsed = "Anthropic",
            AssessorModelIdUsed = "claude-3-opus",
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

    // --- 7. Model Role Bitmask Tests ---

    [Theory]
    [InlineData(1, true, false, false)]  // Chat only
    [InlineData(2, false, true, false)]  // Title only
    [InlineData(4, false, false, true)]  // Benchmark only
    [InlineData(3, true, true, false)]   // Chat + Title
    [InlineData(5, true, false, true)]   // Chat + Benchmark
    [InlineData(6, false, true, true)]   // Title + Benchmark
    [InlineData(7, true, true, true)]    // Chat + Title + Benchmark
    public void ModelRoleBitmask_CorrectlyIdentifiesRoles(int role, bool expectChat, bool expectTitle, bool expectBenchmark)
    {
        bool hasChat = (role & 1) == 1;
        bool hasTitle = (role & 2) == 2;
        bool hasBenchmark = (role & 4) == 4;

        Assert.Equal(expectChat, hasChat);
        Assert.Equal(expectTitle, hasTitle);
        Assert.Equal(expectBenchmark, hasBenchmark);
    }

    // --- 8. Benchmark Assessment Failure Tests ---

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

    // --- 9. Benchmark Run Finalizer Tests ---

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

    // --- 10. Benchmark Report Builder Assessor Provenance Tests ---

    [Fact]
    public void BenchmarkReportBuilder_AssessedBy_IncludesCalloutWhenOverridden()
    {
        var run = new BenchmarkRun
        {
            Id = 1,
            SuiteName = "Test Suite",
            TestedModelDisplayNameUsed = "Tested Model",
            TestedModelProviderUsed = "Anthropic",
            TestedModelIdUsed = "claude-3-5-sonnet",
            AssessorModelConfigurationId = 10,
            AssessorModelDisplayNameUsed = "Original Assessor",
            AssessorModelProviderUsed = "Anthropic",
            AssessorModelIdUsed = "claude-3-5-sonnet",
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
                    AssessedByModelDisplayNameUsed = "Override Assessor",
                    AssessedByModelProviderUsed = "Anthropic",
                    AssessedByModelIdUsed = "claude-3-opus",
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
                    AssessedByModelDisplayNameUsed = "Original Assessor",
                    AssessedByModelProviderUsed = "Anthropic",
                    AssessedByModelIdUsed = "claude-3-5-sonnet"
                }
            }
        };

        string report = BenchmarkReportBuilder.BuildMarkdownReport(run, "1.0.29");

        Assert.Contains("Assessed by:** Override Assessor (Anthropic, claude-3-opus) — differs from this run's assessor", report);
        // Question 2 should not have "differs from this run's assessor" callout
        Assert.DoesNotContain("Original Assessor (Anthropic, claude-3-5-sonnet) — differs from this run's assessor", report);
    }

    // --- 11. Benchmark Service Orphaned Run Cleanup Tests ---

    [Fact]
    public async Task BenchmarkService_CleanupOrphanedRunsAsync_WithoutAnswers_MarksFailed()
    {
        var ct = TestContext.Current.CancellationToken;
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        await using (var db = new ApplicationDbContext(dbOptions))
        {
            var run = new BenchmarkRun
            {
                Id = 1,
                SuiteName = "Test Suite",
                TestedModelDisplayNameUsed = "Model A",
                TestedModelProviderUsed = "Provider A",
                TestedModelIdUsed = "model-a",
                AssessorModelDisplayNameUsed = "Model B",
                AssessorModelProviderUsed = "Provider B",
                AssessorModelIdUsed = "model-b",
                Status = BenchmarkRunStatus.Running,
                StartedAtUtc = DateTime.UtcNow.AddHours(-1)
            };
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
    public async Task CleanupOrphanedRuns_PublishesNoIndex_AndRecordsTotals()
    {
        var ct = TestContext.Current.CancellationToken;
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        await using (var db = new ApplicationDbContext(dbOptions))
        {
            var run = new BenchmarkRun
            {
                Id = 2,
                SuiteName = "Test Suite",
                TestedModelDisplayNameUsed = "Model A",
                TestedModelProviderUsed = "Provider A",
                TestedModelIdUsed = "model-a",
                AssessorModelDisplayNameUsed = "Model B",
                AssessorModelProviderUsed = "Provider B",
                AssessorModelIdUsed = "model-b",
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
            };
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
            db.BenchmarkRuns.Add(new BenchmarkRun
            {
                Id = 3,
                SuiteName = "Test Suite",
                TestedModelDisplayNameUsed = "Model A",
                TestedModelProviderUsed = "Provider A",
                TestedModelIdUsed = "model-a",
                AssessorModelDisplayNameUsed = "Model B",
                AssessorModelProviderUsed = "Provider B",
                AssessorModelIdUsed = "model-b",
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
            });
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
            var run = new BenchmarkRun
            {
                Id = 4,
                SuiteName = "Test Suite",
                TestedModelDisplayNameUsed = "Model A",
                TestedModelProviderUsed = "Provider A",
                TestedModelIdUsed = "model-a",
                AssessorModelDisplayNameUsed = "Model B",
                AssessorModelProviderUsed = "Provider B",
                AssessorModelIdUsed = "model-b",
                Status = BenchmarkRunStatus.CompletedWithErrors,
                StartedAtUtc = DateTime.UtcNow.AddHours(-1),
                CompletedAtUtc = DateTime.UtcNow,
                TotalQuestionCount = 4,
                UnweightedQualityIndex = 11,
                QualityIndexStandardError = 99.0,
                Answers = new List<BenchmarkRunAnswer>()
            };

            for (int i = 1; i <= 3; i++)
            {
                run.Answers.Add(new BenchmarkRunAnswer
                {
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
            new ConfigurationBuilder().Build(),
            NullLogger<BenchmarkService>.Instance);
    }

    [Fact]
    public void BenchmarkAnswer_PersistsDiagnostics_AndStampsHarnessV2()
    {
        var run = new BenchmarkRun
        {
            Id = 99,
            HarnessVersion = "2",
            MaxToolCallsPerQuestionUsed = 25,
            Answers = new List<BenchmarkRunAnswer>
            {
                new BenchmarkRunAnswer
                {
                    OrderIndex = 1,
                    Status = BenchmarkAnswerStatus.Ok,
                    QualityScore = 25,
                    RawQualityScore = 95,
                    ModelCallCount = 3,
                    ToolCallCount = 25,
                    ToolBudgetExhausted = true,
                    TerminationReason = "BudgetExhausted",
                    AnswerFlags = (int)BenchmarkAnswerFlags.HarnessArtifacts
                }
            }
        };

        Assert.Equal("2", run.HarnessVersion);
        Assert.Equal(25, run.MaxToolCallsPerQuestionUsed);
        var ans = run.Answers[0];
        Assert.Equal(25, ans.QualityScore);
        Assert.Equal(95, ans.RawQualityScore);
        Assert.Equal(3, ans.ModelCallCount);
        Assert.Equal(25, ans.ToolCallCount);
        Assert.True(ans.ToolBudgetExhausted);
        Assert.Equal("BudgetExhausted", ans.TerminationReason);
        Assert.Equal((int)BenchmarkAnswerFlags.HarnessArtifacts, ans.AnswerFlags);
    }

    [Fact]
    public void ResolveSecondOpinionTrigger_ContestedVerdictBeatsUnevidencedDeduction()
    {
        var answer = new BenchmarkRunAnswer
        {
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
                TestedModelDisplayNameUsed = "Model T",
                TestedModelProviderUsed = "Provider T",
                TestedModelIdUsed = "model-t",
                AssessorModelDisplayNameUsed = "Model A",
                AssessorModelProviderUsed = "Provider A",
                AssessorModelIdUsed = "model-a",
                ClaimVerifierModelConfigurationId = 10,
                Status = BenchmarkRunStatus.Running,
                StartedAtUtc = DateTime.UtcNow
            };
            db.BenchmarkRuns.Add(run);
            await db.SaveChangesAsync(ct);
            runId = run.Id;

            var answer = new BenchmarkRunAnswer
            {
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
    [InlineData(null, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void CandidatePromptOptions_VerboseMode_ResolvedCorrectlyFromRequest(bool? requestVerbose, bool expectedVerbose)
    {
        var options = new BenchmarkCandidatePromptOptions
        {
            VerboseMode = requestVerbose ?? false
        };

        Assert.Equal(expectedVerbose, options.VerboseMode);
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
            null!, null!, null!, null!, null!, null!, null!,
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
            null!, null!, null!, null!, null!, null!, null!,
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
                null!, null!, null!, null!, null!, null!, null!,
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
                null!, null!, null!, null!, null!, null!, null!,
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

    [Fact]
    public void ExtractDisputedClaims_ExtractsCriticalErrorQuote_NumericAssertions_AndCounterClaims()
    {
        var answer = new BenchmarkRunAnswer
        {
            OrderIndex = 12,
            CriticalErrorQuote = "Exceptional body armor gives +2 AC and +1 blocking.",
            AnswerText = "Exceptional body armor gives +2 AC and +1 blocking. Elite shields give +3 AC and +1 blocking.",
            ReviewComment = "Completely hallucinates the mechanics."
        };

        string accuracyEvidence = "In GnollHack, exceptional body armor gives -4 AC / +1 MC, shields -3 AC / +1 MC; elite gives -8 AC / +2 MC.";

        var claims = BenchmarkService.ExtractDisputedClaims(answer, accuracyEvidence);

        Assert.NotEmpty(claims);
        Assert.Contains(claims, c => c.Contains("+2 AC"));
        Assert.Contains(claims, c => c.Contains("-4 AC"));
    }

    // --- 12. Second-Opinion Sample Top-Up (FlaggedPlusSample) ---

    [Fact]
    public async Task SecondOpinionSampleTopUp_SelectsTheLowestScoringAnswers_TiesByOrderIndex()
    {
        // Scores 40, 40, 55 are the three lowest; the pair at 40 must resolve Q2 before Q4, which
        // is the tie-break that makes the selection reproducible rather than merely small.
        var (run, attempted) = await RunSampleTopUpAsync(
            BenchmarkSecondOpinionMode.FlaggedPlusSample,
            minimumSample: 3,
            answers: new[] { (1, 90), (2, 40), (3, 70), (4, 40), (5, 55) });

        Assert.Equal(new[] { 2, 4, 5 }, attempted);
        Assert.Equal(3, run.SecondOpinionSampleCountUsed);
    }

    [Fact]
    public async Task SecondOpinionSampleTopUp_IsStableUnderReorderingOfTheInput()
    {
        // Same answers, inserted in a different order, so the underlying query returns them in a
        // different order too. Selection must not move: a re-run that reshuffled the sample could
        // be used to fish for a different agreement figure.
        var (run, attempted) = await RunSampleTopUpAsync(
            BenchmarkSecondOpinionMode.FlaggedPlusSample,
            minimumSample: 3,
            answers: new[] { (5, 55), (3, 70), (1, 90), (4, 40), (2, 40) });

        Assert.Equal(new[] { 2, 4, 5 }, attempted);
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
    /// Drives <c>RunSecondOpinionSampleTopUpAsync</c> and reports, in selection order, the order
    /// indexes it actually attempted to re-grade.
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
            var seededRun = new BenchmarkRun
            {
                SuiteName = "Suite",
                TestedModelDisplayNameUsed = "Model T",
                TestedModelProviderUsed = "Provider T",
                TestedModelIdUsed = "model-t",
                AssessorModelDisplayNameUsed = "Model A",
                AssessorModelProviderUsed = "Provider A",
                AssessorModelIdUsed = "model-a",
                SecondOpinionAssessorModelConfigurationId = 999,
                SecondOpinionModeUsed = (int)mode,
                Status = BenchmarkRunStatus.Running,
                StartedAtUtc = DateTime.UtcNow
            };
            seedDb.BenchmarkRuns.Add(seededRun);
            await seedDb.SaveChangesAsync();
            runId = seededRun.Id;

            foreach (var (orderIndex, qualityScore) in answers)
            {
                seedDb.BenchmarkRunAnswers.Add(new BenchmarkRunAnswer
                {
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
        var benchmarkService = new BenchmarkService(
            null!, null!, null!, null!, null!, null!, null!,
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
            .Select(m => Regex.Match(m, @"answer (\d+): second-opinion assessor"))
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
        public List<string> Messages { get; } = new List<string>();

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
            Messages.Add(formatter(state, exception));
        }
    }

    // --- 13. Claim Verification Token Budget ---

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
            seedDb.SystemAiApiConfigurations.Add(new SystemAiApiConfiguration
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
            });

            var run = new BenchmarkRun
            {
                SuiteName = "Suite",
                TestedModelDisplayNameUsed = "Model T",
                TestedModelProviderUsed = "Provider T",
                TestedModelIdUsed = "model-t",
                AssessorModelDisplayNameUsed = "Model A",
                AssessorModelProviderUsed = "Provider A",
                AssessorModelIdUsed = "model-a",
                ClaimVerifierModelConfigurationId = 10,
                Status = BenchmarkRunStatus.Running,
                StartedAtUtc = DateTime.UtcNow
            };
            seedDb.BenchmarkRuns.Add(run);
            await seedDb.SaveChangesAsync();
            runId = run.Id;

            seedDb.BenchmarkRunAnswers.Add(new BenchmarkRunAnswer
            {
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
}


