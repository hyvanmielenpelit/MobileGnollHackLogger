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
                    ToolCallCount = 25,
                    ToolCallBudgetUsed = 25
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
        // The integrity partition replaces the old "Degraded" total, whose breakdown omitted
        // tool-budget exhaustion and therefore did not add up. A leaked-artifact answer the
        // scrubber repaired is Recovered, not a transport defect: it graded normally.
        Assert.Contains("**Transport Defects:** 0", report);
        Assert.Contains("**Recovered:** 1", report);
        Assert.Contains("**Harness Limits:**", report);
        Assert.Contains("Clean + transport defects + recovered + harness limits =", report);
        Assert.Contains("**Advisory Flags:**", report);

        // Latency percentiles
        Assert.Contains("Turn Duration Percentiles", report);
        Assert.Contains("Median (P50)", report);

        // Raw Quality Index
        // Raw Quality Index is printed only when a critical-error cap actually moved it; this
        // fixture has one, so it must appear.
        Assert.Contains("Raw Quality Index", report);

        // Question detail
        Assert.Contains("**Tool Budget:** Exhausted", report);
        Assert.Contains("configured limit, not an error", report);
        Assert.Contains("**Integrity Flags:** HarnessArtifacts", report);
        Assert.Contains("Quality Score:** 25 / 100 (raw: 95)", report);

        // Tool call arithmetic: attempts over budget produced "27 of 25 calls used", which is
        // not a sentence that can be true. Executed and blocked are now separate numbers.
        Assert.Contains("25 executed, budget 25", report);
        Assert.DoesNotContain(" of 25 calls used", report);
    }

    [Fact]
    public void BuildMarkdownReport_SeparatesExecutedAndBlockedToolCalls()
    {
        var run = new BenchmarkRun
        {
            Id = 43,
            SuiteName = "Budget Suite",
            TestedModelDisplayNameUsed = "Model X",
            AssessorModelDisplayNameUsed = "Assessor Y",
            Status = BenchmarkRunStatus.CompletedWithLimits,
            StartedAtUtc = DateTime.UtcNow.AddMinutes(-5),
            CompletedAtUtc = DateTime.UtcNow,
            TotalQuestionCount = 1,
            Answers = new List<BenchmarkRunAnswer>
            {
                new BenchmarkRunAnswer
                {
                    OrderIndex = 11,
                    QuestionText = "Question 11",
                    Difficulty = BenchmarkDifficulty.Intermediate,
                    AssessedDifficulty = 52,
                    QualityScore = 84,
                    SpeedScore = 60,
                    DurationMs = 104067,
                    Status = BenchmarkAnswerStatus.Ok,
                    AssessmentStatus = BenchmarkAssessmentStatus.Scored,
                    AnswerText = "Answer 11",
                    // The 2026-09-03 shape: 27 attempts against a budget of 25, two of them
                    // refused, reported as "27 of 25 calls used".
                    ToolCallCount = 27,
                    ToolCallBudgetUsed = 25,
                    ToolBudgetExhausted = true,
                    ToolCallSummary = "wiki_search×6, wiki_view×13 (2 blocked by budget)"
                }
            }
        };

        var report = BenchmarkReportBuilder.BuildMarkdownReport(run);

        Assert.Contains("25 executed, 2 blocked, budget 25", report);
        Assert.DoesNotContain("27 of 25", report);
    }

    [Fact]
    public void BuildMarkdownReport_NamesTheSecondOpinionAssessorOrSaysThereWasNone()
    {
        var run = new BenchmarkRun
        {
            Id = 45,
            SuiteName = "Second Opinion Suite",
            TestedModelDisplayNameUsed = "Model X",
            AssessorModelDisplayNameUsed = "Assessor Y",
            Status = BenchmarkRunStatus.Completed,
            StartedAtUtc = DateTime.UtcNow,
            Answers = new List<BenchmarkRunAnswer>()
        };

        // Whether a run had a second opinion is a fact about how it was graded, so the report
        // says so either way rather than staying silent when none was selected.
        Assert.Contains("**None selected.**", BenchmarkReportBuilder.BuildMarkdownReport(run));

        run.SecondOpinionAssessorModelConfigurationId = 7;
        run.SecondOpinionAssessorModelDisplayNameUsed = "Claude Reviewer";
        run.SecondOpinionAssessorModelProviderUsed = "Anthropic";
        run.SecondOpinionAssessorModelIdUsed = "claude-reviewer-1";

        var report = BenchmarkReportBuilder.BuildMarkdownReport(run);

        Assert.Contains("### Second Opinion Assessor", report);
        Assert.Contains("Claude Reviewer", report);
        Assert.Contains("Anthropic", report);
        Assert.DoesNotContain("**None selected.**", report);
    }

    [Fact]
    public void BuildMarkdownReport_UsesSuppliedOverseerVersion()
    {
        var run = new BenchmarkRun
        {
            Id = 44,
            SuiteName = "Version Suite",
            TestedModelDisplayNameUsed = "Model X",
            AssessorModelDisplayNameUsed = "Assessor Y",
            Status = BenchmarkRunStatus.Completed,
            StartedAtUtc = DateTime.UtcNow,
            Answers = new List<BenchmarkRunAnswer>()
        };

        // Every report ever produced said 1.0.0, because the only caller passed nothing.
        var report = BenchmarkReportBuilder.BuildMarkdownReport(run, "1.0.29");

        Assert.Contains("**Overseer Version:** 1.0.29", report);
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
                    AssessedDifficulty = 20, // Simple (1-35)
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
                    AssessedDifficulty = 50, // Intermediate (36-70)
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
                    AssessedDifficulty = 85, // Advanced (71-100)
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

        Assert.Contains("Simple (1–35)", report);
        Assert.Contains("Intermediate (36–70)", report);
        Assert.Contains("Advanced (71–100)", report);
        Assert.Contains("Authored Band Distribution", report);
    }

    // --- Heading demotion ---------------------------------------------------------------
    //
    // Question headings render as "### Question N", so answer headings are demoted to sit
    // strictly below that: minLevel 4. These call the internal method directly (Overseer.csproj
    // grants InternalsVisibleTo Overseer.Tests) because the interesting cases are about the
    // transformation itself, not about locating it inside a full report.

    [Fact]
    public void DemoteAnswerHeadings_ShiftsEveryHeadingByTheSameAmount()
    {
        string answer = "## Top\n\nSome text.\n\n### Sub\n\nMore text.";

        string result = BenchmarkReportBuilder.DemoteAnswerHeadings(answer, minLevel: 4);

        // Shallowest heading is "## Top" (level 2); shift = minLevel(4) - 2 = 2, applied to
        // every heading, so "### Sub" (level 3) becomes level 5, not level 4.
        string expected = "#### Top\n\nSome text.\n\n##### Sub\n\nMore text.";
        Assert.Equal(expected, result);
    }

    [Fact]
    public void DemoteAnswerHeadings_LeavesTextUnchanged_WhenShallowestHeadingAlreadyAtMinLevel()
    {
        string answer = "#### Already Deep Enough\n\nBody text.";

        string result = BenchmarkReportBuilder.DemoteAnswerHeadings(answer, minLevel: 4);

        Assert.Equal(answer, result);
    }

    [Fact]
    public void DemoteAnswerHeadings_LeavesTextUnchanged_WhenNoHeadingsPresent()
    {
        string answer = "Just a plain paragraph with no headings at all, and a # that is not one because there's no space? Actually just prose.";

        string result = BenchmarkReportBuilder.DemoteAnswerHeadings(answer, minLevel: 4);

        Assert.Equal(answer, result);
    }

    [Fact]
    public void DemoteAnswerHeadings_LeavesFencedCodeBlocksAlone_ButDemotesRealHeadingsOutsideThem()
    {
        string answer = "## Real Heading\n\n```\n# comment, not a heading\ncode();\n```\n\nMore text.";

        string result = BenchmarkReportBuilder.DemoteAnswerHeadings(answer, minLevel: 4);

        Assert.Contains("#### Real Heading", result);
        Assert.Contains("# comment, not a heading", result);
        Assert.DoesNotContain("#### comment, not a heading", result);
    }

    [Fact]
    public void BuildMarkdownReport_DemotesAnswerHeadingsUnderQuestionHeading()
    {
        var run = new BenchmarkRun
        {
            Id = 50,
            SuiteName = "Heading Suite",
            TestedModelDisplayNameUsed = "Model X",
            AssessorModelDisplayNameUsed = "Assessor Y",
            Status = BenchmarkRunStatus.Completed,
            StartedAtUtc = DateTime.UtcNow,
            TotalQuestionCount = 1,
            Answers = new List<BenchmarkRunAnswer>
            {
                new BenchmarkRunAnswer
                {
                    OrderIndex = 1,
                    QuestionText = "What are the spell schools?",
                    Difficulty = BenchmarkDifficulty.Simple,
                    AssessedDifficulty = 25,
                    QualityScore = 90,
                    SpeedScore = 90,
                    DurationMs = 1000,
                    Status = BenchmarkAnswerStatus.Ok,
                    AssessmentStatus = BenchmarkAssessmentStatus.Scored,
                    // The 2026-09-03 shape: an answer opening with its own "##" heading, a
                    // sibling of the report's own "## 3. Questions and Replies".
                    AnswerText = "## GnollHack's spell schools\n\nThere are several."
                }
            }
        };

        var report = BenchmarkReportBuilder.BuildMarkdownReport(run);

        Assert.Contains("#### GnollHack's spell schools", report);
        // "#### X" contains "## X" as a plain substring, so anchor on the marker boundary: the
        // undemoted two-hash form would appear as a *blank-line-then-##* run; the demoted
        // four-hash form does not contain that four-character sequence.
        Assert.DoesNotContain("\n\n## GnollHack", report);
    }

    // --- Advisory wording ----------------------------------------------------------------

    [Fact]
    public void BuildMarkdownReport_AdvisorySentence_UsesSimpleFormWhenAllNarrationWasRemoved()
    {
        var run = new BenchmarkRun
        {
            Id = 51,
            SuiteName = "Advisory Suite",
            TestedModelDisplayNameUsed = "Model X",
            AssessorModelDisplayNameUsed = "Assessor Y",
            Status = BenchmarkRunStatus.Completed,
            StartedAtUtc = DateTime.UtcNow,
            TotalQuestionCount = 1,
            Answers = new List<BenchmarkRunAnswer>
            {
                new BenchmarkRunAnswer
                {
                    OrderIndex = 1,
                    QuestionText = "Q1",
                    Difficulty = BenchmarkDifficulty.Simple,
                    Status = BenchmarkAnswerStatus.Ok,
                    AssessmentStatus = BenchmarkAssessmentStatus.Scored,
                    AnswerText = "Answer 1",
                    AnswerFlags = (int)BenchmarkAnswerFlags.ReasoningBleed,
                    ScrubbedArtifactText = "some narration that was removed"
                }
            }
        };

        var report = BenchmarkReportBuilder.BuildMarkdownReport(run);

        Assert.Contains("the text they describe was removed before grading", report);
        Assert.DoesNotContain("Removed before grading in", report);
    }

    [Fact]
    public void BuildMarkdownReport_AdvisorySentence_ReportsPartialRemoval_WhenNarrationSurvivedGrading()
    {
        var run = new BenchmarkRun
        {
            Id = 52,
            SuiteName = "Advisory Suite",
            TestedModelDisplayNameUsed = "Model X",
            AssessorModelDisplayNameUsed = "Assessor Y",
            Status = BenchmarkRunStatus.Completed,
            StartedAtUtc = DateTime.UtcNow,
            TotalQuestionCount = 2,
            Answers = new List<BenchmarkRunAnswer>
            {
                new BenchmarkRunAnswer
                {
                    OrderIndex = 1,
                    QuestionText = "Q1",
                    Difficulty = BenchmarkDifficulty.Simple,
                    Status = BenchmarkAnswerStatus.Ok,
                    AssessmentStatus = BenchmarkAssessmentStatus.Scored,
                    AnswerText = "Answer 1",
                    AnswerFlags = (int)BenchmarkAnswerFlags.ReasoningBleed,
                    ScrubbedArtifactText = "removed narration"
                },
                new BenchmarkRunAnswer
                {
                    OrderIndex = 2,
                    QuestionText = "Q2",
                    Difficulty = BenchmarkDifficulty.Simple,
                    Status = BenchmarkAnswerStatus.Ok,
                    AssessmentStatus = BenchmarkAssessmentStatus.Scored,
                    // Flagged, but nothing was actually stripped from this one — the
                    // 2026-09-03 shape where the report used to claim removal regardless.
                    AnswerText = "I'll check the wiki first. Answer 2",
                    AnswerFlags = (int)BenchmarkAnswerFlags.ReasoningBleed,
                    ScrubbedArtifactText = null
                }
            }
        };

        var report = BenchmarkReportBuilder.BuildMarkdownReport(run);

        Assert.Contains("Removed before grading in 1 of 2", report);
        Assert.Contains("in the remainder the text was detected but remained in the graded answer", report);
        Assert.Contains("Reasoning narration present in the graded answer (advisory)", report);
    }

    // --- Scrub counter ---------------------------------------------------------------------

    [Fact]
    public void BuildMarkdownReport_ScrubCounter_ReportsTransportPayloadsAndNarrationSeparately()
    {
        var run = new BenchmarkRun
        {
            Id = 53,
            SuiteName = "Scrub Suite",
            TestedModelDisplayNameUsed = "Model X",
            AssessorModelDisplayNameUsed = "Assessor Y",
            Status = BenchmarkRunStatus.Completed,
            StartedAtUtc = DateTime.UtcNow,
            TotalQuestionCount = 2,
            Answers = new List<BenchmarkRunAnswer>
            {
                new BenchmarkRunAnswer
                {
                    OrderIndex = 1,
                    QuestionText = "Q1",
                    Difficulty = BenchmarkDifficulty.Simple,
                    Status = BenchmarkAnswerStatus.Ok,
                    AssessmentStatus = BenchmarkAssessmentStatus.Scored,
                    AnswerText = "Answer 1",
                    AnswerFlags = (int)BenchmarkAnswerFlags.HarnessArtifacts,
                    ScrubbedArtifactCount = 1,
                    ScrubbedArtifactText = "leaked payload"
                },
                new BenchmarkRunAnswer
                {
                    OrderIndex = 2,
                    QuestionText = "Q2",
                    Difficulty = BenchmarkDifficulty.Simple,
                    Status = BenchmarkAnswerStatus.Ok,
                    AssessmentStatus = BenchmarkAssessmentStatus.Scored,
                    AnswerText = "Answer 2",
                    AnswerFlags = (int)BenchmarkAnswerFlags.ReasoningBleed,
                    ScrubbedArtifactCount = 0,
                    ScrubbedArtifactText = "removed narration"
                }
            }
        };

        var report = BenchmarkReportBuilder.BuildMarkdownReport(run);

        Assert.Contains("**Answers Scrubbed:** 2 of 2 (transport payloads: 1, reasoning narration: 1)", report);
    }

    // --- Critical-error headline -------------------------------------------------------------

    [Fact]
    public void BuildMarkdownReport_ShowsCriticalErrorsHeadline_WhenAnAnswerWasCapped()
    {
        var run = new BenchmarkRun
        {
            Id = 54,
            SuiteName = "Critical Error Suite",
            TestedModelDisplayNameUsed = "Model X",
            AssessorModelDisplayNameUsed = "Assessor Y",
            Status = BenchmarkRunStatus.Completed,
            StartedAtUtc = DateTime.UtcNow,
            TotalQuestionCount = 2,
            Answers = new List<BenchmarkRunAnswer>
            {
                new BenchmarkRunAnswer
                {
                    OrderIndex = 1,
                    QuestionText = "Q1",
                    Difficulty = BenchmarkDifficulty.Simple,
                    Status = BenchmarkAnswerStatus.Ok,
                    AssessmentStatus = BenchmarkAssessmentStatus.Scored,
                    AnswerText = "Answer 1",
                    QualityScore = 25,
                    RawQualityScore = 95,
                    CriticalError = true,
                    CriticalErrorQuote = "This is definitely safe to do."
                },
                new BenchmarkRunAnswer
                {
                    OrderIndex = 2,
                    QuestionText = "Q2",
                    Difficulty = BenchmarkDifficulty.Simple,
                    Status = BenchmarkAnswerStatus.Ok,
                    AssessmentStatus = BenchmarkAssessmentStatus.Scored,
                    AnswerText = "Answer 2",
                    QualityScore = 90,
                    CriticalError = false
                }
            }
        };

        var report = BenchmarkReportBuilder.BuildMarkdownReport(run);

        Assert.Contains("**Critical Errors:** 1 of 2 answered (question(s) 1)", report);
    }

    [Fact]
    public void BuildMarkdownReport_OmitsCriticalErrorsHeadline_WhenNoAnswerWasCapped()
    {
        var run = new BenchmarkRun
        {
            Id = 55,
            SuiteName = "No Critical Error Suite",
            TestedModelDisplayNameUsed = "Model X",
            AssessorModelDisplayNameUsed = "Assessor Y",
            Status = BenchmarkRunStatus.Completed,
            StartedAtUtc = DateTime.UtcNow,
            TotalQuestionCount = 1,
            Answers = new List<BenchmarkRunAnswer>
            {
                new BenchmarkRunAnswer
                {
                    OrderIndex = 1,
                    QuestionText = "Q1",
                    Difficulty = BenchmarkDifficulty.Simple,
                    Status = BenchmarkAnswerStatus.Ok,
                    AssessmentStatus = BenchmarkAssessmentStatus.Scored,
                    AnswerText = "Answer 1",
                    QualityScore = 90,
                    CriticalError = false
                }
            }
        };

        var report = BenchmarkReportBuilder.BuildMarkdownReport(run);

        Assert.DoesNotContain("**Critical Errors:**", report);
    }
}
