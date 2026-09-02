namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Globalization;
using MobileGnollHackLogger.Data;
using Overseer.Services.Benchmarking;
using Xunit;

public class BenchmarkReportBuilderTests
{
    [Fact]
    public void BuildMarkdownReport_ProducesInvariantNumberFormatting_EvenUnderCommaDecimalCulture()
    {
        var prevCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("fi-FI");

            var run = new BenchmarkRun
            {
                Id = 10,
                SuiteName = "Invariant Suite",
                TestedModelDisplayNameUsed = "Model A",
                TestedModelProviderUsed = "Provider A",
                TestedModelIdUsed = "model-a",
                AssessorModelDisplayNameUsed = "Assessor B",
                AssessorModelProviderUsed = "Provider B",
                AssessorModelIdUsed = "assessor-b",
                Status = BenchmarkRunStatus.Completed,
                StartedAtUtc = DateTime.UtcNow.AddMinutes(-5),
                CompletedAtUtc = DateTime.UtcNow,
                QualityIndex = 85,
                SpeedIndex = 90,
                TotalAnswerDurationMs = 12500,
                HarnessVersion = "2",
                ScoringMethodVersion = 3,
                MaxToolCallsPerQuestionUsed = 25,
                Answers = new List<BenchmarkRunAnswer>
                {
                    new BenchmarkRunAnswer
                    {
                        OrderIndex = 1,
                        QuestionText = "Question 1",
                        Difficulty = BenchmarkDifficulty.Simple,
                        AssessedDifficulty = 25,
                        QualityScore = 85,
                        RawQualityScore = 85,
                        SpeedScore = 90,
                        DurationMs = 2500,
                        Status = BenchmarkAnswerStatus.Ok,
                        AssessmentStatus = BenchmarkAssessmentStatus.Scored,
                        AnswerText = "Answer 1"
                    }
                }
            };

            var report = BenchmarkReportBuilder.BuildMarkdownReport(run);

            // Numbers must use periods for decimals, not commas
            Assert.DoesNotContain(",0%", report);
            Assert.DoesNotContain(",5%", report);
            Assert.Contains("85 / 100", report);
        }
        finally
        {
            CultureInfo.CurrentCulture = prevCulture;
        }
    }

    [Fact]
    public void BuildMarkdownReport_IncludesComparabilityBlockAndIntegrity()
    {
        var run = new BenchmarkRun
        {
            Id = 42,
            SuiteName = "Harness Test Suite",
            TestedModelDisplayNameUsed = "Model X",
            AssessorModelDisplayNameUsed = "Assessor Y",
            Status = BenchmarkRunStatus.CompletedWithErrors,
            StartedAtUtc = DateTime.UtcNow.AddMinutes(-10),
            CompletedAtUtc = DateTime.UtcNow,
            QualityIndex = 70,
            SpeedIndex = 80,
            TotalAnswerDurationMs = 20000,
            HarnessVersion = "2",
            ScoringMethodVersion = 3,
            MaxToolCallsPerQuestionUsed = 25,
            DegradedAnswerCount = 1,
            ToolStarvedAnswerCount = 1,
            Answers = new List<BenchmarkRunAnswer>
            {
                new BenchmarkRunAnswer
                {
                    OrderIndex = 1,
                    QuestionText = "Question 1",
                    Difficulty = BenchmarkDifficulty.Intermediate,
                    AssessedDifficulty = 50,
                    QualityScore = 25,
                    RawQualityScore = 95,
                    SpeedScore = 50,
                    DurationMs = 12000,
                    Status = BenchmarkAnswerStatus.Ok,
                    AssessmentStatus = BenchmarkAssessmentStatus.Scored,
                    ToolBudgetExhausted = true,
                    AnswerFlags = (int)BenchmarkAnswerFlags.HarnessArtifacts,
                    CriticalError = true,
                    AnswerText = "Answer 1",
                    ModelCallCount = 5,
                    ToolCallCount = 25
                }
            }
        };

        var report = BenchmarkReportBuilder.BuildMarkdownReport(run);

        // Comparability block
        Assert.Contains("**Harness Version:** 2", report);
        Assert.Contains("**Scoring Method Version:** 3", report);
        Assert.Contains("**Tool Call Budget per Question:** 25", report);

        // Run Integrity block
        Assert.Contains("### Run Integrity", report);
        Assert.Contains("**Degraded Answers:** 1", report);
        Assert.Contains("**Tool-Starved Answers:** 1", report);

        // Latency percentiles
        Assert.Contains("Turn Duration Percentiles", report);
        Assert.Contains("Median (P50)", report);

        // Raw Quality Index
        Assert.Contains("Raw Quality Index", report);

        // Question detail
        Assert.Contains("**Tool Budget:** Exhausted", report);
        Assert.Contains("**Integrity Flags:** HarnessArtifacts", report);
        Assert.Contains("Quality Score:** 25 / 100 (raw: 95)", report);
    }

    [Fact]
    public void BuildMarkdownReport_AssessedDifficultyBucketing_BucketsCorrectly()
    {
        var run = new BenchmarkRun
        {
            Id = 5,
            SuiteName = "Difficulty Bucketing Suite",
            TestedModelDisplayNameUsed = "Model D",
            AssessorModelDisplayNameUsed = "Assessor D",
            Status = BenchmarkRunStatus.Completed,
            StartedAtUtc = DateTime.UtcNow.AddMinutes(-5),
            CompletedAtUtc = DateTime.UtcNow,
            QualityIndex = 75,
            SpeedIndex = 80,
            Answers = new List<BenchmarkRunAnswer>
            {
                new BenchmarkRunAnswer
                {
                    OrderIndex = 1,
                    Difficulty = BenchmarkDifficulty.Simple,
                    AssessedDifficulty = 20, // Simple (1-33)
                    QualityScore = 90,
                    SpeedScore = 90,
                    DurationMs = 1000,
                    Status = BenchmarkAnswerStatus.Ok,
                    AssessmentStatus = BenchmarkAssessmentStatus.Scored,
                    AnswerText = "A1"
                },
                new BenchmarkRunAnswer
                {
                    OrderIndex = 2,
                    Difficulty = BenchmarkDifficulty.Intermediate,
                    AssessedDifficulty = 50, // Intermediate (34-66)
                    QualityScore = 80,
                    SpeedScore = 80,
                    DurationMs = 2000,
                    Status = BenchmarkAnswerStatus.Ok,
                    AssessmentStatus = BenchmarkAssessmentStatus.Scored,
                    AnswerText = "A2"
                },
                new BenchmarkRunAnswer
                {
                    OrderIndex = 3,
                    Difficulty = BenchmarkDifficulty.Advanced,
                    AssessedDifficulty = 85, // Advanced (67-100)
                    QualityScore = 70,
                    SpeedScore = 70,
                    DurationMs = 3000,
                    Status = BenchmarkAnswerStatus.Ok,
                    AssessmentStatus = BenchmarkAssessmentStatus.Scored,
                    AnswerText = "A3"
                }
            }
        };

        var report = BenchmarkReportBuilder.BuildMarkdownReport(run);

        Assert.Contains("Simple (1–33)", report);
        Assert.Contains("Intermediate (34–66)", report);
        Assert.Contains("Advanced (67–100)", report);
        Assert.Contains("Authored Band Distribution", report);
    }
}
