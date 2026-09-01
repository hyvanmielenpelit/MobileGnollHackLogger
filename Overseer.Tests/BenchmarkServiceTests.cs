namespace Overseer.Tests;

using System;
using System.Collections.Generic;
using MobileGnollHackLogger.Data;
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
}
