namespace Overseer.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
            await db.SaveChangesAsync();
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
            null!,
            new ConfigurationBuilder().Build(),
            NullLogger<BenchmarkService>.Instance);

        await benchmarkService.CleanupOrphanedRunsAsync();

        await using (var verifyDb = new ApplicationDbContext(dbOptions))
        {
            var updated = await verifyDb.BenchmarkRuns.FindAsync(1L);
            Assert.NotNull(updated);
            Assert.Equal(BenchmarkRunStatus.Failed, updated.Status);
            Assert.NotNull(updated.CompletedAtUtc);
            Assert.Equal("Run interrupted by application restart.", updated.ErrorMessage);
        }
    }

    [Fact]
    public async Task BenchmarkService_CleanupOrphanedRunsAsync_WithAnswers_FinalizesRun()
    {
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
                        DurationMs = 2000
                    }
                }
            };
            db.BenchmarkRuns.Add(run);
            await db.SaveChangesAsync();
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
            null!,
            new ConfigurationBuilder().Build(),
            NullLogger<BenchmarkService>.Instance);

        await benchmarkService.CleanupOrphanedRunsAsync();

        await using (var verifyDb = new ApplicationDbContext(dbOptions))
        {
            var updated = await verifyDb.BenchmarkRuns.Include(r => r.Answers).FirstOrDefaultAsync(r => r.Id == 2L);
            Assert.NotNull(updated);
            Assert.Equal(BenchmarkRunStatus.Completed, updated.Status);
            Assert.NotNull(updated.CompletedAtUtc);
            Assert.Equal(85, updated.QualityIndex);
            Assert.Equal(75, updated.SpeedIndex);
        }
    }
}
