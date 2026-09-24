namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using MobileGnollHackLogger.Data;
using Overseer.Services;
using Overseer.Services.Benchmarking;
using Overseer.Tests.Helpers;
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
                TestedModelSnapshot = BenchmarkModelSnapshots.Model(provider: "Provider A", modelId: "model-a", displayName: "Model A"),
                AssessorModelSnapshot = BenchmarkModelSnapshots.Model(provider: "Provider B", modelId: "assessor-b", displayName: "Assessor B"),
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

            // And timestamps must use colons. ":" in a custom format string is the culture's
            // time separator, which is "." under fi-FI, so an interpolated
            // "{d:yyyy-MM-dd HH:mm:ss}" silently produced "19.32.00" in every report this
            // repository's own machines generated.
            Assert.Matches(@"\*\*Start Time \(UTC\):\*\* \d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}", report);
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
            TestedModelSnapshot = BenchmarkModelSnapshots.Model(displayName: "Model X"),
            AssessorModelSnapshot = BenchmarkModelSnapshots.Model(displayName: "Assessor Y"),
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
        Assert.Contains("Clean + transport defects + recovered + harness limits + unanswered =", report);
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
            TestedModelSnapshot = BenchmarkModelSnapshots.Model(displayName: "Model X"),
            AssessorModelSnapshot = BenchmarkModelSnapshots.Model(displayName: "Assessor Y"),
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
            TestedModelSnapshot = BenchmarkModelSnapshots.Model(displayName: "Model X"),
            AssessorModelSnapshot = BenchmarkModelSnapshots.Model(displayName: "Assessor Y"),
            Status = BenchmarkRunStatus.Completed,
            StartedAtUtc = DateTime.UtcNow,
            Answers = new List<BenchmarkRunAnswer>()
        };

        // Whether a run had a second opinion is a fact about how it was graded, so the report
        // says so either way rather than staying silent when none was selected.
        Assert.Contains("**None selected.**", BenchmarkReportBuilder.BuildMarkdownReport(run));

        run.SecondOpinionAssessorModelConfigurationId = 7;
        run.SecondOpinionAssessorModelSnapshot = BenchmarkModelSnapshots.Model(provider: "Anthropic", modelId: "claude-reviewer-1", displayName: "Claude Reviewer");
        run.ClaimVerifierModelConfigurationId = 8;
        run.ClaimVerifierModelSnapshot = BenchmarkModelSnapshots.Model(provider: "Google", modelId: "gemini-3.7-flash", displayName: "Verifier Model");

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
            TestedModelSnapshot = BenchmarkModelSnapshots.Model(displayName: "Model X"),
            AssessorModelSnapshot = BenchmarkModelSnapshots.Model(displayName: "Assessor Y"),
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
            TestedModelSnapshot = BenchmarkModelSnapshots.Model(displayName: "Model D"),
            AssessorModelSnapshot = BenchmarkModelSnapshots.Model(displayName: "Assessor D"),
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
        // The label names the difficulty the line buckets by, and the parenthetical names the
        // count basis: the answers this run stored, not the suite as authored. Both halves are
        // load-bearing, because the line sits inside the Band Agreement section, where an
        // unqualified distribution reads as an assessed-band figure.
        Assert.Contains("Authored Band Distribution (of ", report);
        Assert.Contains("by authored difficulty", report);
        // The Difficulty Breakdown above it buckets by assessed difficulty, and now says so.
        Assert.Contains("Buckets by **assessed** difficulty", report);
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
            TestedModelSnapshot = BenchmarkModelSnapshots.Model(displayName: "Model X"),
            AssessorModelSnapshot = BenchmarkModelSnapshots.Model(displayName: "Assessor Y"),
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
            TestedModelSnapshot = BenchmarkModelSnapshots.Model(displayName: "Model X"),
            AssessorModelSnapshot = BenchmarkModelSnapshots.Model(displayName: "Assessor Y"),
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
            TestedModelSnapshot = BenchmarkModelSnapshots.Model(displayName: "Model X"),
            AssessorModelSnapshot = BenchmarkModelSnapshots.Model(displayName: "Assessor Y"),
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
            TestedModelSnapshot = BenchmarkModelSnapshots.Model(displayName: "Model X"),
            AssessorModelSnapshot = BenchmarkModelSnapshots.Model(displayName: "Assessor Y"),
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
            TestedModelSnapshot = BenchmarkModelSnapshots.Model(displayName: "Model X"),
            AssessorModelSnapshot = BenchmarkModelSnapshots.Model(displayName: "Assessor Y"),
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

        Assert.Contains("**Critical Errors:** 1 applied (question(s) 1)", report);
    }

    [Fact]
    public void BuildMarkdownReport_OmitsCriticalErrorsHeadline_WhenNoAnswerWasCapped()
    {
        var run = new BenchmarkRun
        {
            Id = 55,
            SuiteName = "No Critical Error Suite",
            TestedModelSnapshot = BenchmarkModelSnapshots.Model(displayName: "Model X"),
            AssessorModelSnapshot = BenchmarkModelSnapshots.Model(displayName: "Assessor Y"),
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

    [Fact]
    public void BuildMarkdownReport_RendersContestedCriticalErrorsAndSensitivity_WhenContestedVerdictExists()
    {
        var run = new BenchmarkRun
        {
            Id = 56,
            SuiteName = "Contested Error Suite",
            TestedModelSnapshot = BenchmarkModelSnapshots.Model(displayName: "Model X"),
            AssessorModelSnapshot = BenchmarkModelSnapshots.Model(displayName: "Assessor Y"),
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
                    QualityScore = 90,
                    CriticalError = false
                },
                new BenchmarkRunAnswer
                {
                    OrderIndex = 12,
                    QuestionText = "Q12",
                    Difficulty = BenchmarkDifficulty.Intermediate,
                    Status = BenchmarkAnswerStatus.Ok,
                    AssessmentStatus = BenchmarkAssessmentStatus.Scored,
                    AnswerText = "Answer 12",
                    QualityScore = 42,
                    CriticalError = false,
                    AnswerFlags = (int)BenchmarkAnswerFlags.ContestedVerdict,
                    SecondOpinionCriticalError = true,
                    SecondOpinionQualityScore = 25
                }
            }
        };

        var report = BenchmarkReportBuilder.BuildMarkdownReport(run);

        Assert.Contains("**Critical Errors:** 0 applied; 1 raised only by the second reader (question(s) 12)", report);
        Assert.Contains("**Contested-Verdict Sensitivity:**", report);
    }

    [Fact]
    public void BuildMarkdownReport_RendersConfirmedAndContested_WhenBothExist()
    {
        var run = new BenchmarkRun
        {
            Id = 57,
            SuiteName = "Split Error Suite",
            TestedModelSnapshot = BenchmarkModelSnapshots.Model(displayName: "Model X"),
            AssessorModelSnapshot = BenchmarkModelSnapshots.Model(displayName: "Assessor Y"),
            Status = BenchmarkRunStatus.Completed,
            StartedAtUtc = DateTime.UtcNow,
            TotalQuestionCount = 2,
            Answers = new List<BenchmarkRunAnswer>
            {
                new BenchmarkRunAnswer
                {
                    OrderIndex = 3,
                    QuestionText = "Q3",
                    Difficulty = BenchmarkDifficulty.Simple,
                    Status = BenchmarkAnswerStatus.Ok,
                    AssessmentStatus = BenchmarkAssessmentStatus.Scored,
                    AnswerText = "Answer 3",
                    QualityScore = 25,
                    CriticalError = true
                },
                new BenchmarkRunAnswer
                {
                    OrderIndex = 12,
                    QuestionText = "Q12",
                    Difficulty = BenchmarkDifficulty.Intermediate,
                    Status = BenchmarkAnswerStatus.Ok,
                    AssessmentStatus = BenchmarkAssessmentStatus.Scored,
                    AnswerText = "Answer 12",
                    QualityScore = 42,
                    CriticalError = false,
                    SecondOpinionCriticalError = true,
                    SecondOpinionQualityScore = 25
                }
            }
        };

        var report = BenchmarkReportBuilder.BuildMarkdownReport(run);

        Assert.Contains("**Critical Errors:** 1 applied (question(s) 3); 1 raised only by the second reader (question(s) 12)", report);
        Assert.Contains("**Contested-Verdict Sensitivity:**", report);
    }


    // -------------------------------------------------------------------------------------
    // Harness version 6 report changes. Every fixture below is shaped after the 2026-09-03
    // GPT-5.6 Luna run, which is where each of these defects was found.
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// A run carrying the profile snapshot the Luna run used: 15,000 ms target, k = 20,
    /// difficulty scaling 1.0, second-opinion threshold 50.
    /// </summary>
    private const string StandardProfileSnapshot =
        "{\"SpeedTargetMs\":15000,\"SpeedDecayK\":20.0,\"SpeedDifficultyScaling\":1.0," +
        "\"SecondOpinionQualityThreshold\":50}";

    private static BenchmarkRun HarnessV6Run(params BenchmarkRunAnswer[] answers)
    {
        return new BenchmarkRun
        {
            Id = 6,
            SuiteName = "GnollHack Player Assistance Benchmark Suite",
            TestedModelSnapshot = BenchmarkModelSnapshots.Model(provider: "OpenAI", modelId: "gpt-5.6-luna", displayName: "GPT-5.6 Luna", thinkingLevel: "max"),
            AssessorModelSnapshot = BenchmarkModelSnapshots.Model(provider: "Google", modelId: "gemini-3.7-flash", displayName: "Gemini 3.7 Flash"),
            Status = BenchmarkRunStatus.Completed,
            StartedAtUtc = DateTime.UtcNow.AddMinutes(-36),
            CompletedAtUtc = DateTime.UtcNow,
            QualityIndex = 91,
            SpeedIndex = 65,
            HarnessVersion = "6",
            ScoringMethodVersion = 5,
            ScoringProfileSnapshotJson = StandardProfileSnapshot,
            TotalQuestionCount = answers.Length,
            Answers = new List<BenchmarkRunAnswer>(answers)
        };
    }

    private static BenchmarkRunAnswer ScoredAnswer(
        int orderIndex,
        BenchmarkDifficulty band,
        int assessedDifficulty,
        int qualityScore)
    {
        return new BenchmarkRunAnswer
        {
            OrderIndex = orderIndex,
            QuestionText = $"Q{orderIndex}",
            AnswerText = $"Answer {orderIndex}",
            Difficulty = band,
            AssessedDifficulty = assessedDifficulty,
            Status = BenchmarkAnswerStatus.Ok,
            AssessmentStatus = BenchmarkAssessmentStatus.Scored,
            QualityScore = qualityScore,
            RawQualityScore = qualityScore
        };
    }

    [Fact]
    public void SpeedScore_IsAnnotatedWithModelTimeAndTarget_NotRawTurnDuration()
    {
        var answer = ScoredAnswer(3, BenchmarkDifficulty.Simple, 32, 25);
        answer.SpeedScore = 29;
        answer.DurationMs = 236723;
        answer.ToolTimeMs = 1056;

        var report = BenchmarkReportBuilder.BuildMarkdownReport(HarnessV6Run(answer));

        // ModelTimeMs = 236723 - 1056. The target is 15000 * (1 + 32/100) = 19,800.
        Assert.Contains("**Speed Score:** 29 / 100 (model 235667 ms vs target 19,800 ms)", report);
        Assert.DoesNotContain("29 / 100 (236723 ms)", report);
    }

    [Fact]
    public void NarrationAdvisory_SaysRemoved_OnlyWhenTheRunRecordedARemoval()
    {
        var removed = ScoredAnswer(5, BenchmarkDifficulty.Simple, 42, 81);
        removed.AnswerFlags = (int)BenchmarkAnswerFlags.ReasoningBleed;
        removed.ScrubbedArtifactText = "I found the relevant implementation";
        removed.NarrationBlockCount = 3;

        var report = BenchmarkReportBuilder.BuildMarkdownReport(HarnessV6Run(removed));

        Assert.Contains("Reasoning narration removed before grading — 3 block(s) (advisory)", report);
        Assert.DoesNotContain("removal not recorded", report);
    }

    [Fact]
    public void NarrationAdvisory_SaysNotRecorded_ForRunsPredatingTheCounter()
    {
        var historical = ScoredAnswer(3, BenchmarkDifficulty.Simple, 32, 25);
        historical.AnswerFlags = (int)BenchmarkAnswerFlags.ReasoningBleed;
        historical.ScrubbedArtifactText = "tsotlhe";
        historical.NarrationBlockCount = null;

        var report = BenchmarkReportBuilder.BuildMarkdownReport(HarnessV6Run(historical));

        // The old proxy cannot tell a removed payload from removed narration, so the report
        // must not claim the flattering reading it used to assert unconditionally.
        Assert.Contains("removal not recorded for this run (advisory)", report);
        Assert.DoesNotContain("Reasoning narration removed before grading — ", report);
    }

    [Fact]
    public void BudgetPressure_NamesQuestionsThatNearlyExhaustedTheirBudget()
    {
        var pressured = ScoredAnswer(7, BenchmarkDifficulty.Intermediate, 60, 88);
        pressured.ToolCallCount = 34;
        pressured.ToolCallBudgetUsed = 35;

        var comfortable = ScoredAnswer(8, BenchmarkDifficulty.Intermediate, 54, 100);
        comfortable.ToolCallCount = 3;
        comfortable.ToolCallBudgetUsed = 35;

        var report = BenchmarkReportBuilder.BuildMarkdownReport(HarnessV6Run(pressured, comfortable));

        Assert.Contains("**Budget Pressure:**", report);
        Assert.Contains("Q7 34/35 (1 left)", report);
        Assert.DoesNotContain("Q8 3/35", report);
    }

    [Fact]
    public void Grounding_NamesAdvancedQuestionsAnsweredWithoutSearching()
    {
        var ungrounded = ScoredAnswer(14, BenchmarkDifficulty.Advanced, 78, 83);
        ungrounded.ToolCallCount = 1;
        ungrounded.ToolCallBudgetUsed = 45;

        var grounded = ScoredAnswer(13, BenchmarkDifficulty.Advanced, 75, 99);
        grounded.ToolCallCount = 39;
        grounded.ToolCallBudgetUsed = 45;

        var report = BenchmarkReportBuilder.BuildMarkdownReport(HarnessV6Run(ungrounded, grounded));

        Assert.Contains("**Grounding:**", report);
        Assert.Contains("Q14 (1)", report);
        Assert.DoesNotContain("Q13 (39)", report);
    }

    [Fact]
    public void CacheCreationTokens_ReadNotApplicable_WhenTheProviderDoesNotReportThem()
    {
        var run = HarnessV6Run(ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 25));
        run.TotalCacheReadTokens = 4102396;
        run.TotalCacheCreationTokens = 0;

        var report = BenchmarkReportBuilder.BuildMarkdownReport(run);

        Assert.Contains("**Total Cache Creation Tokens:** n/a *(not reported by this provider)*", report);
    }

    [Fact]
    public void CacheCreationTokens_PrintZero_WhenTheProviderDoesReportThem()
    {
        var run = HarnessV6Run(ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 25));
        run.TestedModelSnapshot = BenchmarkModelSnapshots.Model(provider: "Anthropic", modelId: run.TestedModelSnapshot.ModelId, displayName: run.TestedModelSnapshot.DisplayName, thinkingLevel: run.TestedModelSnapshot.ThinkingLevel);
        run.TotalCacheReadTokens = 4102396;
        run.TotalCacheCreationTokens = 0;

        var report = BenchmarkReportBuilder.BuildMarkdownReport(run);

        Assert.Contains("**Total Cache Creation Tokens:** 0", report);
        Assert.DoesNotContain("not reported by this provider", report);
    }

    [Fact]
    public void ProfileFit_WarnsWhenAHeavyThinkerIsGradedOnAnInteractiveLatencyProfile()
    {
        var report = BenchmarkReportBuilder.BuildMarkdownReport(
            HarnessV6Run(ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 25)));

        Assert.Contains("**Profile Fit:**", report);
        Assert.Contains("thinking level **max**", report);
    }

    [Fact]
    public void ProfileFit_IsSilentForAModelThatIsNotDeliberating()
    {
        var run = HarnessV6Run(ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 25));
        run.TestedModelSnapshot = BenchmarkModelSnapshots.Model(provider: run.TestedModelSnapshot.Provider, modelId: run.TestedModelSnapshot.ModelId, displayName: run.TestedModelSnapshot.DisplayName, thinkingLevel: "low");

        var report = BenchmarkReportBuilder.BuildMarkdownReport(run);

        Assert.DoesNotContain("**Profile Fit:**", report);
    }

    [Fact]
    public void NonMonotonicNote_NamesTheBandWhenEveryCriticalErrorLandedInOne()
    {
        // The Luna run's shape: both caps on Simple questions, so the Simple average is
        // depressed by the cap rather than by difficulty.
        var q1 = ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 25);
        q1.CriticalError = true;
        var q3 = ScoredAnswer(3, BenchmarkDifficulty.Simple, 32, 25);
        q3.CriticalError = true;
        var q7 = ScoredAnswer(7, BenchmarkDifficulty.Intermediate, 60, 92);
        var q13 = ScoredAnswer(13, BenchmarkDifficulty.Advanced, 75, 95);

        var report = BenchmarkReportBuilder.BuildMarkdownReport(HarnessV6Run(q1, q3, q7, q13));

        Assert.Contains("critical-error cap(s) on this run fell in the **Simple** band", report);
        Assert.Contains("question(s) 1, 3", report);
        Assert.DoesNotContain("This is common on small question sets", report);
    }

    [Fact]
    public void SecondOpinion_ReportsWhatWouldHaveBeenRegraded_WhenNoneWasSelected()
    {
        var critical = ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 25);
        critical.CriticalError = true;
        var lowScoring = ScoredAnswer(2, BenchmarkDifficulty.Simple, 28, 40);
        var fine = ScoredAnswer(4, BenchmarkDifficulty.Simple, 30, 97);

        var report = BenchmarkReportBuilder.BuildMarkdownReport(
            HarnessV6Run(critical, lowScoring, fine));

        Assert.Contains("**2 answer(s) would have been re-graded**", report);
        Assert.Contains("critical error: 1", report);
        Assert.Contains("below the profile's threshold of 50: 1", report);
    }

    /// <summary>
    /// A harness version 7 run: the second-opinion mode is stamped on the run, and the
    /// unweighted mean is a stored column rather than something the report has to recompute.
    /// </summary>
    private static BenchmarkRun HarnessV7Run(
        BenchmarkSecondOpinionMode mode,
        params BenchmarkRunAnswer[] answers)
    {
        var run = HarnessV6Run(answers);
        run.HarnessVersion = "7";
        run.ScoringMethodVersion = 6;
        run.SecondOpinionModeUsed = (int)mode;
        return run;
    }

    [Fact]
    public void WeightingTransparency_ReportsHowFarDifficultyWeightingMovedTheIndex()
    {
        // Run 7's shape in miniature: the two weakest answers are also the two easiest
        // questions, so the difficulty-weighted index reads above the plain mean.
        var run = HarnessV7Run(
            BenchmarkSecondOpinionMode.Off,
            ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 60),
            ScoredAnswer(2, BenchmarkDifficulty.Intermediate, 55, 97),
            ScoredAnswer(3, BenchmarkDifficulty.Advanced, 85, 99));
        run.QualityIndex = 94;
        run.UnweightedQualityIndex = 92;

        var report = BenchmarkReportBuilder.BuildMarkdownReport(run);

        Assert.Contains("**Unweighted Quality Mean:** 92 / 100", report);
        Assert.Contains("moved the index by **+2** points", report);
    }

    [Fact]
    public void WeightingTransparency_IsSilentWhenTheTwoAggregationsAgree()
    {
        var run = HarnessV7Run(
            BenchmarkSecondOpinionMode.Off,
            ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 90),
            ScoredAnswer(2, BenchmarkDifficulty.Advanced, 85, 90));
        run.QualityIndex = 90;
        run.UnweightedQualityIndex = 90;

        var report = BenchmarkReportBuilder.BuildMarkdownReport(run);

        Assert.DoesNotContain("**Unweighted Quality Mean:**", report);
    }

    [Fact]
    public void BandDispersion_ReportsTheSpreadAndTheWeakestQuestion()
    {
        // 88.2 out of 60, 95, 97, 97, 92 is a different finding from 88.2 out of five answers
        // near 88, and a band average alone cannot tell them apart.
        var report = BenchmarkReportBuilder.BuildMarkdownReport(HarnessV7Run(
            BenchmarkSecondOpinionMode.Off,
            ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 60),
            ScoredAnswer(2, BenchmarkDifficulty.Simple, 28, 95),
            ScoredAnswer(3, BenchmarkDifficulty.Simple, 30, 97),
            ScoredAnswer(4, BenchmarkDifficulty.Simple, 32, 97),
            ScoredAnswer(5, BenchmarkDifficulty.Simple, 30, 92)));

        Assert.Contains("quality range 60–97, lowest Q1", report);
    }

    [Fact]
    public void BandDispersion_IsOmittedForASingleAnsweredQuestion()
    {
        var report = BenchmarkReportBuilder.BuildMarkdownReport(HarnessV7Run(
            BenchmarkSecondOpinionMode.Off,
            ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 60)));

        Assert.DoesNotContain("lowest Q", report);
    }

    [Fact]
    public void NonMonotonicNote_NamesTheOneAnswerThatExplainsTheInversion()
    {
        // No critical error anywhere, so the capped-band branch cannot fire. Simple averages
        // 84.7 against Intermediate's 90 purely because of Q1; removing it lifts Simple to 97.
        var report = BenchmarkReportBuilder.BuildMarkdownReport(HarnessV7Run(
            BenchmarkSecondOpinionMode.Off,
            ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 60),
            ScoredAnswer(2, BenchmarkDifficulty.Simple, 28, 97),
            ScoredAnswer(3, BenchmarkDifficulty.Simple, 30, 97),
            ScoredAnswer(4, BenchmarkDifficulty.Intermediate, 55, 90),
            ScoredAnswer(5, BenchmarkDifficulty.Advanced, 85, 88)));

        Assert.Contains("Removing the **Simple** band's single weakest answer (question 1, 60 / 100)", report);
        Assert.Contains("restores the ordering", report);
        Assert.DoesNotContain("This is common on small question sets", report);
    }

    [Fact]
    public void AssessorFindings_ListsUnverifiedClaimsAndContestedVerdicts()
    {
        var q1 = ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 60);
        q1.UnverifiedClaimCount = 2;
        q1.UnverifiedClaimsJson = "[\"gnomes gain infravision\",\"orcs gain poison resistance\"]";
        q1.AnswerFlags = (int)BenchmarkAnswerFlags.ContestedVerdict;
        var q10 = ScoredAnswer(10, BenchmarkDifficulty.Simple, 30, 60);
        q10.UnverifiedClaimCount = 1;
        q10.AnswerFlags = (int)BenchmarkAnswerFlags.ContestedVerdict;
        var clean = ScoredAnswer(2, BenchmarkDifficulty.Advanced, 85, 99);
        clean.UnverifiedClaimCount = 0;

        var report = BenchmarkReportBuilder.BuildMarkdownReport(
            HarnessV7Run(BenchmarkSecondOpinionMode.Off, q1, q10, clean));

        Assert.Contains("### Assessor Findings", report);
        Assert.Contains("**Unverified Claims:** 3 across 2 answer(s) (Q1, Q10)", report);
        Assert.Contains("**Contested Verdicts:** 2 (Q1, Q10)", report);
        // The claims themselves, on the answer that carried them.
        Assert.Contains("gnomes gain infravision", report);
    }

    [Fact]
    public void AssessorFindings_AreOmittedWhenTheAssessorFoundNothing()
    {
        var clean = ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 97);
        clean.UnverifiedClaimCount = 0;

        var report = BenchmarkReportBuilder.BuildMarkdownReport(
            HarnessV7Run(BenchmarkSecondOpinionMode.Off, clean));

        Assert.DoesNotContain("### Assessor Findings", report);
    }

    [Fact]
    public void AssessorFindings_SayNotRecorded_ForARunThatWasNeverAsked()
    {
        // UnverifiedClaimCount is null, not zero: "the assessor found none" and "the assessor
        // was never asked" are different facts and the report must not conflate them.
        var report = BenchmarkReportBuilder.BuildMarkdownReport(
            HarnessV6Run(ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 97)));

        Assert.Contains("**Unverified Claims:** not recorded", report);
    }

    /// <summary>
    /// An answer in the verification-cleared population: Accuracy docked out of rubric, unverified
    /// claims recorded, and every claim the verifier checked supported — nothing refuted, nothing
    /// left indeterminate. All four levels are present so the sensitivity index has something to
    /// recompute from.
    /// </summary>
    private static BenchmarkRunAnswer VerificationClearedAnswer(
        int orderIndex,
        BenchmarkDifficulty band,
        int assessedDifficulty,
        int qualityScore,
        int accuracyLevel = 4)
    {
        var answer = ScoredAnswer(orderIndex, band, assessedDifficulty, qualityScore);
        answer.AnswerFlags = (int)BenchmarkAnswerFlags.UnevidencedDeduction;
        answer.UnverifiedClaimCount = 2;
        answer.ClaimsSupportedCount = 2;
        answer.ClaimsRefutedCount = 0;
        answer.ClaimsIndeterminateCount = 0;
        answer.AccuracyLevel = accuracyLevel;
        answer.CompletenessLevel = 5;
        answer.ConcisenessLevel = 5;
        answer.ReadabilityLevel = 5;
        return answer;
    }

    [Fact]
    public void AssessorFindings_ListVerificationClearedAccuracyDeductions_AndExcludeEveryNearMiss()
    {
        var cleared = VerificationClearedAnswer(1, BenchmarkDifficulty.Simple, 25, 70);

        // One near miss per condition, each otherwise identical to the population answer.
        var noFlag = VerificationClearedAnswer(2, BenchmarkDifficulty.Simple, 25, 70);
        noFlag.AnswerFlags = 0;

        var noClaims = VerificationClearedAnswer(3, BenchmarkDifficulty.Simple, 25, 70);
        noClaims.UnverifiedClaimCount = 0;
        noClaims.ClaimsSupportedCount = 0;

        var refuted = VerificationClearedAnswer(4, BenchmarkDifficulty.Simple, 25, 70);
        refuted.ClaimsRefutedCount = 1;

        var indeterminate = VerificationClearedAnswer(5, BenchmarkDifficulty.Simple, 25, 70);
        indeterminate.ClaimsIndeterminateCount = 1;

        var accuracyUndocked = VerificationClearedAnswer(6, BenchmarkDifficulty.Simple, 25, 70, accuracyLevel: 6);

        var report = BenchmarkReportBuilder.BuildMarkdownReport(HarnessV7Run(
            BenchmarkSecondOpinionMode.Off, cleared, noFlag, noClaims, refuted, indeterminate, accuracyUndocked));

        Assert.Contains("**Verification-cleared Accuracy deductions:** 1 (Q1)", report);
        Assert.Contains("as OUT-OF-SCOPE is of Completeness", report);
    }

    [Fact]
    public void AssessorFindings_OmitVerificationClearedAccuracyDeductions_WhenNoAnswerQualifies()
    {
        var refuted = VerificationClearedAnswer(1, BenchmarkDifficulty.Simple, 25, 70);
        refuted.ClaimsRefutedCount = 1;

        var report = BenchmarkReportBuilder.BuildMarkdownReport(
            HarnessV7Run(BenchmarkSecondOpinionMode.Off, refuted));

        Assert.DoesNotContain("Verification-cleared Accuracy deductions", report);
        Assert.DoesNotContain("Verification-cleared Accuracy Sensitivity", report);
    }

    [Fact]
    public void VerificationClearedSensitivity_RaisesAccuracyByOne_AndStillHonoursTheCriticalErrorCap()
    {
        // Levels 5/5/5/5 score 87, so Q1 rises 70 → 87. Q2 is recomputed from the same levels but
        // carries a critical error, so its 87 is capped back to 25 rather than lifting the index.
        // Q3 is outside the population and keeps its stored 90. Equal difficulties, so the index
        // is the plain mean: (87 + 25 + 90) / 3 = 67.
        var cleared = VerificationClearedAnswer(1, BenchmarkDifficulty.Intermediate, 50, 70);
        var clearedWithCriticalError = VerificationClearedAnswer(2, BenchmarkDifficulty.Intermediate, 50, 25);
        clearedWithCriticalError.CriticalError = true;
        var untouched = ScoredAnswer(3, BenchmarkDifficulty.Intermediate, 50, 90);

        var report = BenchmarkReportBuilder.BuildMarkdownReport(HarnessV7Run(
            BenchmarkSecondOpinionMode.Off, cleared, clearedWithCriticalError, untouched));

        Assert.Contains("**Verification-cleared Accuracy Sensitivity:** 67 / 100", report);

        // § 2 prints the figure above the Assessor Findings list, so it names where the answers
        // are; § 7 prints it below that list.
        Assert.Contains("- **Verification-cleared Accuracy Sensitivity:** 67 / 100 — Intelligence Index recomputed with Accuracy one level higher on the 2 answer(s) listed under Assessor Findings; advisory, changes no score.", report);
        Assert.Contains("### Verification-cleared Accuracy Sensitivity: 67 / 100 — Intelligence Index recomputed with Accuracy one level higher on the 2 answer(s) above; advisory, changes no score.", report);

        // § 2 and § 7 carry one computation, so the two figures cannot drift.
        int finalIndicesStart = report.IndexOf("## 7. Final Indices", StringComparison.Ordinal);
        Assert.True(finalIndicesStart >= 0);
        var matches = Regex.Matches(report, @"Verification-cleared Accuracy Sensitivity:\**\s*(\d+)\s*/\s*100");
        Assert.Equal(2, matches.Count);
        Assert.True(matches[0].Index < finalIndicesStart, "§ 2 must carry the sensitivity figure.");
        Assert.True(matches[1].Index > finalIndicesStart, "§ 7 must carry the same sensitivity figure.");
        Assert.Equal(matches[0].Groups[1].Value, matches[1].Groups[1].Value);
    }

    [Fact]
    public void VerificationClearedSensitivity_IsNotRenderedAtAll_WhenThePopulationIsEmpty()
    {
        // Nothing to raise, so no second index is produced and the real Intelligence Index stands
        // alone in both § 2 and § 7.
        var q1 = ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 70);
        q1.UnverifiedClaimCount = 2;
        q1.ClaimsSupportedCount = 2;
        var q2 = ScoredAnswer(2, BenchmarkDifficulty.Advanced, 85, 90);

        var report = BenchmarkReportBuilder.BuildMarkdownReport(
            HarnessV7Run(BenchmarkSecondOpinionMode.Off, q1, q2));

        Assert.DoesNotContain("Verification-cleared Accuracy Sensitivity", report);
        Assert.Contains("**Unverified Claims:** 2 across 1 answer(s) (Q1)", report);
    }

    [Fact]
    public void AssessorAgreement_CarriesTheConditioningCaveat_UnderTriggerSelectedCoverage()
    {
        var q1 = ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 60);
        q1.SecondOpinionQualityScore = 85;
        q1.SecondOpinionDisagreed = true;
        q1.SecondOpinionTrigger = "BelowThreshold";
        q1.SecondOpinionByModelSnapshot = BenchmarkModelSnapshots.Model(displayName: "Claude Opus 5");

        var run = HarnessV7Run(
            BenchmarkSecondOpinionMode.Flagged,
            q1,
            ScoredAnswer(2, BenchmarkDifficulty.Advanced, 85, 99));
        run.SecondOpinionAssessorModelConfigurationId = 4;
        run.SecondOpinionAssessorModelSnapshot = BenchmarkModelSnapshots.Model(provider: "Anthropic", modelId: "claude-opus-5", displayName: "Claude Opus 5");
        run.SecondOpinionGradedAnswerCount = 1;
        run.SecondOpinionMeanAbsDelta = 25.0;

        var report = BenchmarkReportBuilder.BuildMarkdownReport(run);

        Assert.Contains("### Assessor Agreement", report);
        Assert.Contains("**Coverage:** 1 of 2 answered questions.", report);
        Assert.Contains("**Mean absolute difference:** 25.0 points.", report);
        Assert.Contains("**Disagreements:** 1 of 1 (100.0%) — Q1", report);
        Assert.Contains("is not an unbiased estimate of grader agreement", report);
        Assert.Contains("(trigger: score below the profile threshold)", report);
    }

    [Theory]
    [InlineData("CriticalError")]
    [InlineData("RefutedClaim")]
    [InlineData("ContestedVerdict")]
    [InlineData("OutOfRubricAccuracy")]
    [InlineData("UnevidencedDeduction")]
    [InlineData("OmissionAsAccuracy")]
    [InlineData("DimensionOutlier")]
    [InlineData("UnverifiedClaims")]
    [InlineData("BelowThreshold")]
    [InlineData("Outlier")]
    [InlineData("All")]
    [InlineData("Manual")]
    [InlineData("Sample")]
    public void TriggerLabel_MapsEverySecondOpinionTriggerToWords(string trigger)
    {
        // The constants of BenchmarkService.SecondOpinionTriggers.
        string label = BenchmarkReportBuilder.TriggerLabel(trigger);

        Assert.NotEqual(trigger, label);
        Assert.True(label.Contains(' ') || char.IsLower(label[0]), label);
    }

    [Fact]
    public void AssessorAgreement_DropsTheCaveat_WhenEveryAnswerWasGradedTwice()
    {
        var q1 = ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 60);
        q1.SecondOpinionQualityScore = 62;
        q1.SecondOpinionTrigger = "All";
        var q2 = ScoredAnswer(2, BenchmarkDifficulty.Advanced, 85, 99);
        q2.SecondOpinionQualityScore = 95;
        q2.SecondOpinionTrigger = "All";

        var run = HarnessV7Run(BenchmarkSecondOpinionMode.All, q1, q2);
        run.SecondOpinionAssessorModelConfigurationId = 4;
        run.SecondOpinionAssessorModelSnapshot = BenchmarkModelSnapshots.Model(provider: "Anthropic", modelId: "claude-opus-5", displayName: "Claude Opus 5");
        run.SecondOpinionGradedAnswerCount = 2;
        run.SecondOpinionMeanAbsDelta = 3.0;

        var report = BenchmarkReportBuilder.BuildMarkdownReport(run);

        Assert.Contains("**Mode:** All — every answer graded twice.", report);
        Assert.Contains("**Coverage:** 2 of 2 answered questions.", report);
        Assert.DoesNotContain("is not an unbiased estimate of grader agreement", report);
    }

    [Fact]
    public void BudgetPressure_MarksTheQuestionsThatAlsoScoredBelowTheRunMean()
    {
        // Run 7's Q10: one call short of its budget, and the worst answer in its band.
        var pressured = ScoredAnswer(10, BenchmarkDifficulty.Simple, 30, 60);
        pressured.ToolCallBudgetUsed = 35;
        pressured.ToolCallCount = 34;
        var exhausted = ScoredAnswer(11, BenchmarkDifficulty.Intermediate, 55, 84);
        exhausted.ToolCallBudgetUsed = 25;
        exhausted.ToolCallCount = 25;
        exhausted.ToolBudgetExhausted = true;

        var run = HarnessV7Run(
            BenchmarkSecondOpinionMode.Off,
            pressured,
            exhausted,
            ScoredAnswer(12, BenchmarkDifficulty.Advanced, 85, 100),
            ScoredAnswer(13, BenchmarkDifficulty.Advanced, 90, 100));
        run.UnweightedQualityIndex = 92;

        var report = BenchmarkReportBuilder.BuildMarkdownReport(run);

        Assert.Contains("Q10 34/35 (1 left) **— scored 60, below the run mean of 92**", report);
        Assert.Contains("**Budget/Quality Correlation:** 2 budget-constrained question(s)", report);
        Assert.Contains("Q10 (60, budget pressured)", report);
        Assert.Contains("Q11 (84, budget exhausted)", report);
        Assert.Contains("Benchmark:ToolCallBudget", report);
    }

    [Fact]
    public void SynthesisDivergence_ReportsAQuestionTheSynthesisCallsAHallucination()
    {
        var q10 = ScoredAnswer(10, BenchmarkDifficulty.Simple, 30, 60);
        q10.ReviewComment = "Mischaracterizes gemstone armor.";

        var run = HarnessV7Run(
            BenchmarkSecondOpinionMode.Off,
            q10,
            ScoredAnswer(11, BenchmarkDifficulty.Advanced, 85, 99));
        run.AssessmentText =
            "The model performed strongly overall.\n" +
            "Question 10 hallucinates a material that does not exist in the game.\n" +
            "Question 11 was answered from the source.";

        var report = BenchmarkReportBuilder.BuildMarkdownReport(run);

        Assert.Contains("### Synthesis Divergence", report);
        Assert.Contains("**Question 10:** the run synthesis reports a hallucination", report);
        Assert.DoesNotContain("**Question 11:** the run synthesis", report);
    }

    [Fact]
    public void SynthesisDivergence_IsSilentWhenThePerQuestionVerdictAlreadyCapped()
    {
        var q10 = ScoredAnswer(10, BenchmarkDifficulty.Simple, 30, 25);
        q10.CriticalError = true;

        var run = HarnessV7Run(BenchmarkSecondOpinionMode.Off, q10);
        run.AssessmentText = "Question 10 hallucinates a material that does not exist.";

        var report = BenchmarkReportBuilder.BuildMarkdownReport(run);

        Assert.DoesNotContain("### Synthesis Divergence", report);
    }

    /// <summary>The assessor evidence blob as the harness stores it.</summary>
    private static string EvidenceJson(string? accuracy, string? completeness = null)
    {
        string Field(string? value) => value == null
            ? "null"
            : "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        return $"{{\"accuracy\":{Field(accuracy)},\"completeness\":{Field(completeness)}}}";
    }

    /// <summary>
    /// The 2026-09-06 run in miniature: two answers docked to Accuracy 5 with evidence naming a
    /// concrete false assertion, beside answers whose evidence is the full-level boilerplate.
    /// </summary>
    private static BenchmarkRun Run14ShapedRun()
    {
        var q11 = ScoredAnswer(11, BenchmarkDifficulty.Intermediate, 55, 92);
        q11.AccuracyLevel = 5;
        q11.AssessmentEvidenceJson = EvidenceJson(
            "The answer overlooks that weapon swapping between sets takes 0 turns in GnollHack, suggesting instead that switching weapons requires extra equipment management overhead.");

        var q12 = ScoredAnswer(12, BenchmarkDifficulty.Intermediate, 60, 95);
        q12.AccuracyLevel = 5;
        q12.AssessmentEvidenceJson = EvidenceJson("Matches rubric.");

        var q14 = ScoredAnswer(14, BenchmarkDifficulty.Advanced, 85, 90);
        q14.AccuracyLevel = 5;
        q14.AssessmentEvidenceJson = EvidenceJson(
            "The answer lists Level as 40 and Hit dice as 25; in the monster definition LVL(25, 16, -10, 15, 10, -20), his level/HD is 25, while 40 is his monster difficulty.");

        var q15 = ScoredAnswer(15, BenchmarkDifficulty.Advanced, 80, 99);
        q15.AccuracyLevel = 6;
        q15.AssessmentEvidenceJson = EvidenceJson("Aligns with rubric.");

        return HarnessV7Run(BenchmarkSecondOpinionMode.Off, q11, q12, q14, q15);
    }

    [Fact]
    public void SynthesisAccuracyDivergence_NamesTheQuestionsWhoseEvidenceTheSynthesisContradicts()
    {
        var run = Run14ShapedRun();
        run.AssessmentText =
            "The model demonstrates an elite command of GnollHack's mechanics across the suite.\n" +
            "Identified weaknesses were confined to secondary omissions rather than factual errors or critical rubric failures.";

        var report = BenchmarkReportBuilder.BuildMarkdownReport(run);

        Assert.Contains("### Synthesis Accuracy Divergence", report);
        Assert.Contains("**Question 11:** Accuracy 5 / 6", report);
        Assert.Contains("**Question 14:** Accuracy 5 / 6", report);
        Assert.Contains("40 is his monster difficulty", report);

        // The boilerplate-evidence answers are not accusations, and the level-6 one is faultless
        // by definition. Naming them would fire this block on every strong run.
        Assert.DoesNotContain("**Question 12:** Accuracy", report);
        Assert.DoesNotContain("**Question 15:** Accuracy", report);

        // Advisory, exactly like its neighbour: it says so, and changes no score.
        Assert.Contains("no score changes here", report);
    }

    [Fact]
    public void SynthesisAccuracyDivergence_IsSilentWhenTheSynthesisMakesNoSuchClaim()
    {
        // The same verdicts, under a synthesis that reports its weaknesses honestly. Accuracy
        // deductions alone are not a divergence — a run is allowed to have them.
        var run = Run14ShapedRun();
        run.AssessmentText =
            "The model demonstrates an elite command of GnollHack's mechanics across the suite.\n" +
            "Question 14 misreports Master Kaen's level as his monster difficulty, and Question 11 misstates the cost of a weapon swap.";

        var report = BenchmarkReportBuilder.BuildMarkdownReport(run);

        Assert.DoesNotContain("### Synthesis Accuracy Divergence", report);
    }

    [Fact]
    public void SynthesisAccuracyDivergence_IsSilentWhenNoVerdictNamesADefect()
    {
        // The claim, made honestly. Every deduction's evidence is the full-level boilerplate the
        // prompt itself offers, so there is nothing for the claim to contradict.
        var clean = ScoredAnswer(1, BenchmarkDifficulty.Simple, 30, 97);
        clean.AccuracyLevel = 6;
        clean.AssessmentEvidenceJson = EvidenceJson("Matches rubric.");

        var run = HarnessV7Run(BenchmarkSecondOpinionMode.Off, clean);
        run.AssessmentText = "The run was free of factual errors throughout.";

        var report = BenchmarkReportBuilder.BuildMarkdownReport(run);

        Assert.DoesNotContain("### Synthesis Accuracy Divergence", report);
    }

    [Fact]
    public void OutOfScopeCompletenessDeductions_AreCountedAndNamedUnderTheDimensionalAverages()
    {
        var q12 = ScoredAnswer(12, BenchmarkDifficulty.Intermediate, 60, 95);
        q12.CompletenessLevel = 5;
        q12.CompletenessOutOfScope = true;

        var q18 = ScoredAnswer(18, BenchmarkDifficulty.Advanced, 85, 90);
        q18.CompletenessLevel = 5;
        q18.CompletenessOutOfScope = true;

        var ordinary = ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 70);
        ordinary.CompletenessLevel = 3;

        var report = BenchmarkReportBuilder.BuildMarkdownReport(
            HarnessV7Run(BenchmarkSecondOpinionMode.Off, ordinary, q12, q18));

        Assert.Contains("**Out-of-scope completeness deductions:** 2 (Q12, Q18)", report);
        Assert.Contains("Accuracy→Completeness gap", report);
    }

    [Fact]
    public void OutOfScopeCompletenessDeductions_AreNotReportedAsZeroWhenNoneWereRecorded()
    {
        // A run graded before the marker existed and a run whose assessor found nothing out of
        // scope look identical here, so the report asserts neither.
        var report = BenchmarkReportBuilder.BuildMarkdownReport(
            HarnessV7Run(
                BenchmarkSecondOpinionMode.Off,
                ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 90)));

        Assert.DoesNotContain("Out-of-scope completeness deductions", report);
    }

    [Fact]
    public void RubricFormSuggestions_AreCountedAndNamedUnderTheDimensionalAverages()
    {
        var q3 = ScoredAnswer(3, BenchmarkDifficulty.Simple, 30, 95);
        q3.ReadabilityLevel = 5;
        q3.ReadabilityFormOnly = true;

        var q9 = ScoredAnswer(9, BenchmarkDifficulty.Intermediate, 60, 90);
        q9.ReadabilityLevel = 5;
        q9.ReadabilityFormOnly = true;

        var ordinary = ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 70);
        ordinary.ReadabilityLevel = 4;

        var report = BenchmarkReportBuilder.BuildMarkdownReport(
            HarnessV7Run(BenchmarkSecondOpinionMode.Off, ordinary, q3, q9));

        Assert.Contains("**Rubric format suggestions not followed:** 2 (Q3, Q9)", report);
        Assert.Contains("Readability is graded on its level anchors alone", report);
    }

    [Fact]
    public void RubricFormSuggestions_AreNotReportedAsZeroWhenNoneWereRecorded()
    {
        // Same silence as the out-of-scope line, for the same reason: a run graded before the
        // FORM marker existed and a run whose rubrics suggested nothing look identical here.
        var report = BenchmarkReportBuilder.BuildMarkdownReport(
            HarnessV7Run(
                BenchmarkSecondOpinionMode.Off,
                ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 90)));

        Assert.DoesNotContain("Rubric format suggestions not followed", report);
    }

    [Fact]
    public void NarrationAdvisory_SaysThatNarrationIsPromptCompliantInProductionChat()
    {
        // H5. The scrubber is benchmark-only by design, and this note exists so that a reader of
        // the Issues list does not "fix" chat by porting it there.
        var removed = ScoredAnswer(18, BenchmarkDifficulty.Advanced, 85, 90);
        removed.AnswerFlags = (int)BenchmarkAnswerFlags.ReasoningBleed;
        removed.NarrationBlockCount = 1;

        var report = BenchmarkReportBuilder.BuildMarkdownReport(HarnessV6Run(removed));

        Assert.Contains("Briefly tell the player what you're looking up when using a tool", report);
        Assert.Contains("Overseer/ToolGuides/_policy.md", report);
        Assert.Contains("must not acquire one", report);
    }

    [Fact]
    public void NarrationAdvisory_PolicyNoteIsAbsentWhenNoAnswerCarriedNarration()
    {
        var report = BenchmarkReportBuilder.BuildMarkdownReport(
            HarnessV6Run(ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 90)));

        Assert.DoesNotContain("Briefly tell the player what you're looking up", report);
    }

    [Fact]
    public void Reassessment_RecordsTheScoreItReplacedAndWhoReplacedIt()
    {
        var q1 = ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 85);
        q1.ReassessmentCount = 1;
        q1.PreviousQualityScore = 60;
        q1.ReassessedAtUtc = new DateTime(2026, 9, 4, 8, 30, 0, DateTimeKind.Utc);
        q1.ReassessedByModelSnapshot = BenchmarkModelSnapshots.Model(displayName: "Claude Opus 5");

        var report = BenchmarkReportBuilder.BuildMarkdownReport(
            HarnessV7Run(BenchmarkSecondOpinionMode.Off, q1));

        Assert.Contains("**Re-assessed:** 1 time(s), most recently 2026-09-04 08:30:00 UTC by Claude Opus 5", report);
        Assert.Contains("the first verdict scored 60 / 100 and this one replaced it", report);
    }

    [Fact]
    public void AssessorPairing_WarnsWhenBothGradersShareAProvider()
    {
        var run = HarnessV7Run(
            BenchmarkSecondOpinionMode.All,
            ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 90));
        run.SecondOpinionAssessorModelConfigurationId = 4;
        run.SecondOpinionAssessorModelSnapshot = BenchmarkModelSnapshots.Model(provider: "Google", modelId: "gemini-3.7-pro", displayName: "Gemini 3.7 Pro");

        var report = BenchmarkReportBuilder.BuildMarkdownReport(run);

        Assert.Contains("**Assessor Pairing:** candidate OpenAI, assessor Google, second opinion Google — 2 distinct provider(s)", report);
        Assert.Contains("come from the same provider", report);
    }

    [Fact]
    public void AssessorPairing_IsQuietWhenAllThreeRolesAreDistinctProviders()
    {
        var run = HarnessV7Run(
            BenchmarkSecondOpinionMode.All,
            ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 90));
        run.SecondOpinionAssessorModelConfigurationId = 4;
        run.SecondOpinionAssessorModelSnapshot = BenchmarkModelSnapshots.Model(provider: "Anthropic", modelId: "claude-opus-5", displayName: "Claude Opus 5");

        var report = BenchmarkReportBuilder.BuildMarkdownReport(run);

        Assert.Contains("3 distinct provider(s)", report);
        Assert.DoesNotContain("come from the same provider", report);
    }

    [Fact]
    public void Report_CarriesNoCalibrationData()
    {
        // Calibration runs grade a run's answers with a third model to measure that model's
        // agreement with the run's assessor. They change no score and belong to assessor
        // selection, not to the run's published record — so nothing about them appears here.
        var report = BenchmarkReportBuilder.BuildMarkdownReport(HarnessV7Run(
            BenchmarkSecondOpinionMode.All,
            ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 90)));

        Assert.DoesNotContain("Calibration", report);
        Assert.DoesNotContain("calibration", report);
    }

    [Fact]
    public void AssessorAgreement_ZeroCoverage_ReportsAssessorConfiguredWithNoTriggersMet()
    {
        var q1 = ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 70);
        var q2 = ScoredAnswer(2, BenchmarkDifficulty.Intermediate, 50, 85);

        var run = HarnessV7Run(BenchmarkSecondOpinionMode.Flagged, q1, q2);
        run.SecondOpinionAssessorModelConfigurationId = 4;
        run.SecondOpinionAssessorModelSnapshot = BenchmarkModelSnapshots.Model(provider: "Anthropic", modelId: "claude-opus-5", displayName: "Claude Opus 5");
        run.SecondOpinionGradedAnswerCount = 0;

        var report = BenchmarkReportBuilder.BuildMarkdownReport(run);

        Assert.Contains("### Assessor Agreement", report);
        Assert.Contains("**Coverage:** 0 of 2 answered questions.", report);
        Assert.Contains("No answer met a trigger, so no answer was graded twice", report);
        Assert.Contains("Claude Opus 5", report);
    }

    [Fact]
    public void AdvisoryFlags_BreakdownIncludesContestedVerdictsAndReflectedTotal()
    {
        var q1 = ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 80);
        q1.AnswerFlags = (int)BenchmarkAnswerFlags.ReasoningBleed;

        var q2 = ScoredAnswer(2, BenchmarkDifficulty.Intermediate, 50, 75);
        q2.AnswerFlags = (int)BenchmarkAnswerFlags.ContestedVerdict;

        var run = HarnessV7Run(BenchmarkSecondOpinionMode.Off, q1, q2);
        BenchmarkRunFinalizer.Apply(run, new[] { q1, q2 });

        var report = BenchmarkReportBuilder.BuildMarkdownReport(run);

        // Every member of AdvisoryFlags is enumerated, so the parenthetical accounts for the
        // total rather than listing a subset of it. The two harness-18 members and the harness-19
        // one are included for that reason and read 0 here; the harness-20 one reads "not
        // recorded", because this run is stamped 7.
        Assert.Contains(
            "**Advisory Flags:** 2 (reasoning bleed: 1, repeated fragments: 0, contested verdicts: 1, " +
            "unevidenced deductions: 0, omissions as accuracy: 0, refuted claims: 0, " +
            "contested critical errors: 0, out-of-rubric accuracy deductions: 0, " +
            "contested accuracy deductions: not recorded, dimension outliers: 0, " +
            "answer-framing openers: 0)",
            report);
    }

    [Fact]
    public void ClaimVerification_ManifestAndRefutedClaimsRenderedInReport()
    {
        var q1 = ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 75);
        q1.UnverifiedClaimCount = 2;
        q1.ClaimsSupportedCount = 1;
        q1.ClaimsRefutedCount = 1;
        q1.AnswerFlags = (int)BenchmarkAnswerFlags.RefutedClaim;
        q1.ClaimVerificationJson = @"[{""claimIndex"":0,""" + "claim" + @""":""Gnolls can breathe underwater"",""verdict"":""Refuted"",""citation"":""src/role.c:120"",""basis"":""Gnolls have no water breathing intrinsic.""}]";

        q1.ClaimVerificationInputTokens = 1200;
        q1.ClaimVerificationOutputTokens = 300;
        q1.ClaimVerificationDurationMs = 2500;

        var run = HarnessV7Run(BenchmarkSecondOpinionMode.Off, q1);
        run.ClaimVerifierModelConfigurationId = 7;
        run.ClaimVerifierModelSnapshot = BenchmarkModelSnapshots.Model(provider: "Google", modelId: "gemini-3.7-pro", displayName: "Verifier Model");

        BenchmarkRunFinalizer.Apply(run, new[] { q1 });

        var report = BenchmarkReportBuilder.BuildMarkdownReport(run);

        Assert.Contains("### Claim Verifier", report);
        Assert.Contains("- **Display Name:** Verifier Model", report);
        Assert.Contains("- **Provider:** Google", report);
        Assert.Contains("- **Model ID:** gemini-3.7-pro", report);
        Assert.Contains("Claim Verifier Tokens:", report);
        Assert.Contains("#### Refuted Claims", report);
        Assert.Contains("Gnolls can breathe underwater", report);
        Assert.Contains("src/role.c:120", report);
    }

    [Fact]
    public void ToolUsageProfile_RendersThreeBudgetStatesCorrectly()
    {
        // q1: pressured (23 of 25 = 92% >= 90% and < 100%)
        var q1 = ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 80);
        q1.ToolCallCount = 23;
        q1.ToolCallBudgetUsed = 25;
        q1.ToolCallsBlocked = 0;
        q1.ToolBudgetExhausted = false;

        // q2: saturated (35 of 35 = 100%, 0 blocked)
        var q2 = ScoredAnswer(2, BenchmarkDifficulty.Intermediate, 50, 85);
        q2.ToolCallCount = 35;
        q2.ToolCallBudgetUsed = 35;
        q2.ToolCallsBlocked = 0;
        q2.ToolBudgetExhausted = false;

        // q3: exhausted (25 of 25, 3 blocked)
        var q3 = ScoredAnswer(3, BenchmarkDifficulty.Simple, 25, 75);
        q3.ToolCallCount = 25;
        q3.ToolCallBudgetUsed = 25;
        q3.ToolCallsBlocked = 3;
        q3.ToolBudgetExhausted = true;

        var run = HarnessV7Run(BenchmarkSecondOpinionMode.Off, q1, q2, q3);
        BenchmarkRunFinalizer.Apply(run, new[] { q1, q2, q3 });

        var report = BenchmarkReportBuilder.BuildMarkdownReport(run);

        Assert.Contains("Budget Pressure:", report);
        Assert.Contains("Q1 23/25", report);
        Assert.Contains("Budget Saturated:", report);
        Assert.Contains("Q2 35/35", report);
        Assert.Contains("Budget Exhausted:", report);
        Assert.Contains("Q3 25/25 (3 calls refused by budget)", report);
    }

    [Fact]
    public void Report_RendersStandardErrorAndConfidenceInterval()
    {
        var q1 = ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 80);
        var q2 = ScoredAnswer(2, BenchmarkDifficulty.Intermediate, 50, 90);
        var q3 = ScoredAnswer(3, BenchmarkDifficulty.Advanced, 75, 70);

        var run = HarnessV7Run(BenchmarkSecondOpinionMode.Off, q1, q2, q3);
        BenchmarkRunFinalizer.Apply(run, new[] { q1, q2, q3 });

        var report = BenchmarkReportBuilder.BuildMarkdownReport(run);

        Assert.Contains("(95% CI over 3 items)", report);
        Assert.Contains("finite item-sampling uncertainty", report);
    }

    [Fact]
    public void DisputedAssessments_RendersClaimVerificationBesideDispute()
    {
        var q1 = ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 54);
        q1.SecondOpinionQualityScore = 25;
        q1.SecondOpinionCriticalError = true;
        q1.SecondOpinionDisagreed = true;
        q1.SecondOpinionTrigger = "LowQualityScore";
        q1.ClaimsSupportedCount = 3;
        q1.ClaimsRefutedCount = 0;
        q1.ClaimsIndeterminateCount = 0;

        var run = HarnessV7Run(BenchmarkSecondOpinionMode.Flagged, q1);
        run.SecondOpinionAssessorModelSnapshot = BenchmarkModelSnapshots.Model(displayName: "Second Assessor");
        run.SecondOpinionBlindUsed = true;
        BenchmarkRunFinalizer.Apply(run, new[] { q1 });

        var report = BenchmarkReportBuilder.BuildMarkdownReport(run);

        Assert.Contains("### Disputed Assessments", report);
        Assert.Contains("Claims: 3 supported, 0 refuted, 0 indeterminate", report);
        Assert.Contains("Assessor agreement is reported for a **blind** second reader", report);
    }

    [Fact]
    public void ChatPromptUnderTest_RendersConfiguration_AndFallsBackWhenNull()
    {
        var q1 = ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 80);
        var runWithoutSnapshot = HarnessV7Run(BenchmarkSecondOpinionMode.Off, q1);
        runWithoutSnapshot.CandidatePromptOptionsJson = null;
        BenchmarkRunFinalizer.Apply(runWithoutSnapshot, new[] { q1 });

        var report1 = BenchmarkReportBuilder.BuildMarkdownReport(runWithoutSnapshot);
        Assert.Contains("### Chat Prompt Under Test", report1);
        Assert.Contains("not recorded for this run", report1);

        var runWithSnapshot = HarnessV7Run(BenchmarkSecondOpinionMode.Off, q1);
        runWithSnapshot.CandidatePromptOptionsJson = new BenchmarkCandidatePromptOptions { VerboseMode = true }.ToCanonicalJson();
        BenchmarkRunFinalizer.Apply(runWithSnapshot, new[] { q1 });

        var report2 = BenchmarkReportBuilder.BuildMarkdownReport(runWithSnapshot);
        Assert.Contains("### Chat Prompt Under Test", report2);
        Assert.Contains("detailed (`verboseMode: true`)", report2);
    }

    [Fact]
    public void ChatPromptUnderTest_NamesTheGameSnapshot()
    {
        var q1 = ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 80);

        var withBoard = HarnessV7Run(BenchmarkSecondOpinionMode.Off, q1);
        withBoard.CandidatePromptOptionsJson =
            new BenchmarkCandidatePromptOptions { HasGameSnapshot = true }.ToCanonicalJson();
        BenchmarkRunFinalizer.Apply(withBoard, new[] { q1 });
        Assert.Contains("**Game snapshot:** yes", BenchmarkReportBuilder.BuildMarkdownReport(withBoard));

        var withoutBoard = HarnessV7Run(BenchmarkSecondOpinionMode.Off, q1);
        withoutBoard.CandidatePromptOptionsJson =
            new BenchmarkCandidatePromptOptions { HasGameSnapshot = false }.ToCanonicalJson();
        BenchmarkRunFinalizer.Apply(withoutBoard, new[] { q1 });
        Assert.Contains("**Game snapshot:** no", BenchmarkReportBuilder.BuildMarkdownReport(withoutBoard));
    }

    [Fact]
    public void ChatPromptUnderTest_StatesWhetherDeliveryWasVerified()
    {
        var q1 = ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 80);

        var verified = HarnessV7Run(BenchmarkSecondOpinionMode.Off, q1);
        verified.HarnessVersion = "29";
        verified.CandidatePromptOptionsJson =
            new BenchmarkCandidatePromptOptions { HasGameSnapshot = true }.ToCanonicalJson();
        BenchmarkRunFinalizer.Apply(verified, new[] { q1 });
        Assert.Contains(
            "prompt and board delivery verified against the provider request body",
            BenchmarkReportBuilder.BuildMarkdownReport(verified));

        // A snapshot run before harness 29 lost the board on Google and Anthropic and the prompt on
        // OpenAI, and nothing checked either.
        var oldSnapshotRun = HarnessV7Run(BenchmarkSecondOpinionMode.Off, q1);
        oldSnapshotRun.HarnessVersion = "28";
        oldSnapshotRun.CandidatePromptOptionsJson =
            new BenchmarkCandidatePromptOptions { HasGameSnapshot = true }.ToCanonicalJson();
        BenchmarkRunFinalizer.Apply(oldSnapshotRun, new[] { q1 });
        Assert.Contains(
            "not verified — before harness 29 a Google or Anthropic candidate did not receive the board",
            BenchmarkReportBuilder.BuildMarkdownReport(oldSnapshotRun));

        var oldOpenAiRun = HarnessV7Run(BenchmarkSecondOpinionMode.Off, q1);
        oldOpenAiRun.HarnessVersion = "28";
        oldOpenAiRun.TestedModelSnapshot = BenchmarkModelSnapshots.Model(provider: "OpenAI", modelId: "gpt-5.6-luna", displayName: "GPT-5.6 Luna", thinkingLevel: "max");
        oldOpenAiRun.CandidatePromptOptionsJson =
            new BenchmarkCandidatePromptOptions { HasGameSnapshot = false }.ToCanonicalJson();
        BenchmarkRunFinalizer.Apply(oldOpenAiRun, new[] { q1 });
        Assert.Contains(
            "not verified — before harness 29 an OpenAI candidate did not receive this prompt.",
            BenchmarkReportBuilder.BuildMarkdownReport(oldOpenAiRun));

        // Nothing was wrong with a pre-29 run on another provider with no board, so nothing is said.
        var unaffected = HarnessV7Run(BenchmarkSecondOpinionMode.Off, q1);
        unaffected.HarnessVersion = "28";
        unaffected.TestedModelSnapshot = BenchmarkModelSnapshots.Model(provider: "Google", modelId: "gpt-5.6-luna", displayName: "GPT-5.6 Luna", thinkingLevel: "max");
        unaffected.CandidatePromptOptionsJson =
            new BenchmarkCandidatePromptOptions { HasGameSnapshot = false }.ToCanonicalJson();
        BenchmarkRunFinalizer.Apply(unaffected, new[] { q1 });
        Assert.DoesNotContain("**Delivery:**", BenchmarkReportBuilder.BuildMarkdownReport(unaffected));
    }

    /// <summary>A harness-30 run on a snapshot suite with the given answers.</summary>
    private static BenchmarkRun Harness30BoardRun(params BenchmarkRunAnswer[] answers)
    {
        var run = HarnessV7Run(BenchmarkSecondOpinionMode.Off, answers);
        run.HarnessVersion = "30";
        run.ScoringMethodVersion = 10;
        run.GameSnapshotSha256Used = "8f8c4778d449";
        run.CandidatePromptOptionsJson =
            new BenchmarkCandidatePromptOptions { HasGameSnapshot = true }.ToCanonicalJson();
        BenchmarkRunFinalizer.Apply(run, answers);
        return run;
    }

    private static BenchmarkRunAnswer BoardGradedAnswer(int orderIndex, int qualityScore, int? assessorBoardChars = 12037)
    {
        var answer = ScoredAnswer(orderIndex, BenchmarkDifficulty.Simple, 25, qualityScore);
        answer.AssessedByModelConfigurationId = 1;
        answer.AssessorBoardChars = assessorBoardChars;
        return answer;
    }

    [Fact]
    public void ChatPromptUnderTest_Harness30_DeliverySentenceRestsOnTheRecordedProbe()
    {
        var unrecorded = Harness30BoardRun(BoardGradedAnswer(1, 80));
        string report = BenchmarkReportBuilder.BuildMarkdownReport(unrecorded);
        Assert.Contains("**Delivery:** not recorded — no pre-run delivery probe is on record for this run.", report);
        Assert.DoesNotContain("delivery verified against the provider request body", report);

        var recorded = Harness30BoardRun(BoardGradedAnswer(1, 80));
        recorded.CandidateDeliveryVerifiedAtUtc = new DateTime(2026, 9, 18, 7, 11, 0, DateTimeKind.Utc);
        Assert.Contains(
            "prompt and board delivery verified against the provider request body before the first question (2026-09-18 07:11:00 UTC).",
            BenchmarkReportBuilder.BuildMarkdownReport(recorded));
    }

    [Fact]
    public void ChatPromptUnderTest_Harness30BoardSuite_PrintsBoardDeliveryPerRole()
    {
        var q1 = BoardGradedAnswer(1, 80);
        q1.SecondOpinionQualityScore = 75;
        q1.SecondOpinionBoardChars = 12037;
        q1.ClaimVerificationJson = "[]";
        q1.VerifierBoardChars = 12037;
        var q2 = BoardGradedAnswer(2, 60);

        string report = BenchmarkReportBuilder.BuildMarkdownReport(Harness30BoardRun(q1, q2));

        Assert.Contains(
            "Board delivered — assessor 2 of 2 graded, second opinion 1 of 1, claim verifier 1 of 1; synthesis: yes; difficulty assessment: digest (no map).",
            report);
        Assert.DoesNotContain("Board Not Delivered", report);
    }

    [Fact]
    public void RunIntegrity_Harness30BoardSuite_NamesARoleThatGradedWithoutTheBoard()
    {
        var q1 = BoardGradedAnswer(1, 80);
        q1.SecondOpinionQualityScore = 75;
        q1.SecondOpinionBoardChars = 0;

        string report = BenchmarkReportBuilder.BuildMarkdownReport(Harness30BoardRun(q1));

        Assert.Contains("second opinion 0 of 1", report);
        Assert.Contains("- **Board Not Delivered (second opinion):** 1 of 1 verdict(s) — Q1", report);
    }

    [Fact]
    public void ChatPromptUnderTest_Harness29_KeepsItsSentenceAndPrintsNoBoardBlock()
    {
        var run = Harness30BoardRun(BoardGradedAnswer(1, 80));
        run.HarnessVersion = "29";

        string report = BenchmarkReportBuilder.BuildMarkdownReport(run);

        Assert.Contains("prompt and board delivery verified against the provider request body before the first question.", report);
        Assert.DoesNotContain("Board delivered —", report);
    }

    [Fact]
    public void Grounding_OnABoardSuite_CallsABoardAnswerExpected()
    {
        var advanced = ScoredAnswer(1, BenchmarkDifficulty.Advanced, 85, 71);
        advanced.ToolCallCount = 0;

        string boardReport = BenchmarkReportBuilder.BuildMarkdownReport(Harness30BoardRun(advanced));
        Assert.Contains("answered from the board with one tool call or fewer", boardReport);
        Assert.Contains("Expected on a snapshot suite when the board settles the question.", boardReport);
        Assert.DoesNotContain("may no longer test source retrieval", boardReport);

        var plain = HarnessV7Run(BenchmarkSecondOpinionMode.Off, advanced);
        Assert.Contains("may no longer test source retrieval", BenchmarkReportBuilder.BuildMarkdownReport(plain));
    }

    [Fact]
    public void EvidenceInformedSensitivity_AbsentAtZero_PresentWithItsCount_AndMovesNothingElse()
    {
        var q1 = BoardGradedAnswer(1, 40);
        var q2 = BoardGradedAnswer(2, 90);
        var run = Harness30BoardRun(q1, q2);

        string before = BenchmarkReportBuilder.BuildMarkdownReport(run);
        Assert.DoesNotContain("Evidence-informed", before);

        q1.EvidenceInformedQualityScore = 70;
        q1.EvidenceInformedCriticalError = false;
        q1.EvidenceInformedJson = "{\"assessor\":\"Claude 5 Opus\",\"withdrawn\":[\"Accuracy deduction: peacefuls are never displaced\"]}";
        string after = BenchmarkReportBuilder.BuildMarkdownReport(run);

        // A harness-30 record carries no validation provenance: it keeps its calculation and says so.
        Assert.Contains("**Evidence-informed Sensitivity (legacy, unvalidated):**", after);
        Assert.Contains("on the 1 answer(s) re-graded with the verifier's findings in hand; advisory, changes no score.", after);
        Assert.Contains("**Evidence-informed re-grade (Claude 5 Opus; legacy, unvalidated):** 70 / 100, critical error no, levels not recorded — withdrew: Accuracy deduction: peacefuls are never displaced.", after);

        // Every other line of the report is unchanged by the stored re-grade.
        static string[] Without(string report) => report.Replace("\r\n", "\n").Split('\n')
            .Where(l => !l.Contains("Evidence-informed", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(Without(before), Without(after));
        Assert.Equal(40, q1.QualityScore);
    }

    private static string ValidatedRegradeJson(bool eligible, string errors = "[]", string dropped = "[]") =>
        "{\"assessor\":\"Claude 5 Opus\",\"accuracyLevel\":6,\"completenessLevel\":5,\"concisenessLevel\":5,\"readabilityLevel\":4,"
        + "\"withdrawn\":[\"T1 accuracy: \\\"It has no charges.\\\" (F2) — the verifier supported the sentence\"],"
        + $"\"validationVersion\":1,\"withdrawnDropped\":{dropped},\"validationErrors\":{errors},\"eligibleForSensitivity\":{(eligible ? "true" : "false")}}}";

    [Fact]
    public void EvidenceInformedSensitivity_Harness31_CountsValidatedRegradesOnly_AndReportsTheExcluded()
    {
        var q1 = BoardGradedAnswer(1, 40);
        var q2 = BoardGradedAnswer(2, 60);
        var q3 = BoardGradedAnswer(3, 90);
        var run = Harness30BoardRun(q1, q2, q3);
        run.HarnessVersion = "31";
        run.ScoringMethodVersion = 11;

        q1.EvidenceInformedQualityScore = 80;
        q1.EvidenceInformedCriticalError = false;
        q1.EvidenceInformedJson = ValidatedRegradeJson(eligible: true);
        q2.EvidenceInformedQualityScore = 100;
        q2.EvidenceInformedCriticalError = false;
        q2.EvidenceInformedJson = ValidatedRegradeJson(eligible: false, errors: "[\"Accuracy was raised from 4 to 6 with no valid accuracy withdrawal.\"]");
        // No provenance on a harness-31 run: ineligible even though the JSON parses.
        q3.EvidenceInformedQualityScore = 95;
        q3.EvidenceInformedJson = "{\"assessor\":\"Claude 5 Opus\",\"withdrawn\":[\"something\"]}";

        string report = BenchmarkReportBuilder.BuildMarkdownReport(run);

        int expected = BenchmarkScoring.QualityIndex(new List<(int?, int)> { (80, 25), (60, 25), (90, 25) })!.Value;
        Assert.Contains($"- **Evidence-informed Sensitivity (validated re-grades only):** {expected} / 100", report);
        Assert.Contains("on the 1 answer(s) whose re-grade passed validation; 2 re-grade(s) excluded (rejected, or without validation provenance) keep their primary score", report);
        Assert.Contains($"### Evidence-informed Sensitivity (validated re-grades only): {expected} / 100", report);

        Assert.Contains("**Evidence-informed re-grade (Claude 5 Opus; validated):** 80 / 100, critical error no, levels 6/5/5/4 (Accuracy/Completeness/Conciseness/Readability)", report);
        Assert.Contains("**Evidence-informed re-grade (Claude 5 Opus; rejected):** 100 / 100", report);
        Assert.Contains("rejected because Accuracy was raised from 4 to 6 with no valid accuracy withdrawal.", report);
        Assert.Contains("**Evidence-informed re-grade (Claude 5 Opus; no validation provenance, excluded):** 95 / 100", report);
    }

    [Fact]
    public void EvidenceInformedSensitivity_Harness31_ReportsTheExcludedCount_WhenEveryRegradeIsExcluded()
    {
        var q1 = BoardGradedAnswer(1, 40);
        var run = Harness30BoardRun(q1);
        run.HarnessVersion = "31";
        q1.EvidenceInformedQualityScore = 90;
        q1.EvidenceInformedJson = ValidatedRegradeJson(eligible: false, errors: "[\"the re-grade lowered Accuracy from 4 to 3.\"]");

        string report = BenchmarkReportBuilder.BuildMarkdownReport(run);

        Assert.Contains("on the 0 answer(s) whose re-grade passed validation; 1 re-grade(s) excluded", report);
        Assert.Contains($"- **Evidence-informed Sensitivity (validated re-grades only):** {run.QualityIndex} / 100", report);
    }

    [Fact]
    public void AssessorFindings_NameASupportedAccusation_AndKeepAdvisoryItemsOutOfRefutedClaims()
    {
        var q1 = BoardGradedAnswer(1, 60);
        q1.AccuracyLevel = 4;
        q1.ClaimsRefutedCount = 1;
        q1.ClaimVerificationJson = System.Text.Json.JsonSerializer.Serialize(new[]
        {
            new BenchmarkClaimVerification(0, "Own refuted claim.", BenchmarkClaimVerdict.Refuted, "src/own.c:1", "False.")
                { Roles = new[] { BenchmarkClaimRoles.UnverifiedClaim } },
            new BenchmarkClaimVerification(1, "Charged but true.", BenchmarkClaimVerdict.Supported, "src/objects.c:2889", "True.")
                { Roles = new[] { BenchmarkClaimRoles.AccusedQuote } },
            new BenchmarkClaimVerification(2, "Charged and false.", BenchmarkClaimVerdict.Refuted, "src/zap.c:9", "False.")
                { Roles = new[] { BenchmarkClaimRoles.AccusedQuote } }
        });

        string report = BenchmarkReportBuilder.BuildMarkdownReport(Harness30BoardRun(q1));

        Assert.Contains("- **Supported Accusations:** 1 (Q1)", report);
        Assert.Contains("a sentence the assessor charged as false was checked by the claim verifier and **supported** — \"Charged but true.\" (src/objects.c:2889)", report);
        Assert.Contains("- **Q1:** \"Own refuted claim.\"", report);
        Assert.DoesNotContain("- **Q1:** \"Charged and false.\"", report);
    }

    [Fact]
    public void ToolRouting_RendersFamilyCountsAndCaveats()
    {
        var q1 = ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 80);
        q1.ToolCallSummary = "source_code_search×5, wiki_search×3, monster_lookup×1, get_knowledge_article×1";
        var run = HarnessV7Run(BenchmarkSecondOpinionMode.Off, q1);
        BenchmarkRunFinalizer.Apply(run, new[] { q1 });

        var report = BenchmarkReportBuilder.BuildMarkdownReport(run);
        Assert.Contains("#### Tool Routing", report);
        Assert.Contains("Source Code", report);
        Assert.Contains("Wiki", report);
        Assert.Contains("Structured Lookup", report);
        Assert.Contains("Knowledge Base", report);
        Assert.Contains("ordering is not recorded", report);
    }

    [Fact]
    public void ResponseStyleConflict_RendersOnlyWhenPredicateHolds()
    {
        var q1 = ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 80);
        q1.AccuracyLevel = 6;
        q1.CompletenessLevel = 4;
        q1.ConcisenessLevel = 5;
        q1.ReadabilityLevel = 5;

        q1.AccuracyScore = 98;
        q1.CompletenessScore = 83;
        q1.ConcisenessScore = 95;
        q1.ReadabilityScore = 95;

        var run = HarnessV7Run(BenchmarkSecondOpinionMode.Off, q1);
        run.CandidatePromptOptionsJson = new BenchmarkCandidatePromptOptions { VerboseMode = false }.ToCanonicalJson();
        BenchmarkRunFinalizer.Apply(run, new[] { q1 });

        var reportWithConflict = BenchmarkReportBuilder.BuildMarkdownReport(run);
        Assert.Contains("Response-style conflict.", reportWithConflict);

        run.CandidatePromptOptionsJson = new BenchmarkCandidatePromptOptions { VerboseMode = true }.ToCanonicalJson();
        var reportWithoutConflict = BenchmarkReportBuilder.BuildMarkdownReport(run);
        Assert.DoesNotContain("Response-style conflict.", reportWithoutConflict);
    }

    [Fact]
    public void SameModelClaimVerifier_RendersAdvisoryWhenIdsMatch()
    {
        var q1 = ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 80);
        var run = HarnessV7Run(BenchmarkSecondOpinionMode.Off, q1);
        run.SecondOpinionAssessorModelConfigurationId = 42;
        run.ClaimVerifierModelConfigurationId = 42;
        BenchmarkRunFinalizer.Apply(run, new[] { q1 });

        var report = BenchmarkReportBuilder.BuildMarkdownReport(run);
        Assert.Contains("Same model as the second-opinion assessor.", report);
    }

    [Fact]
    public void SynthesisCaveat_RendersWhenRefutedClaimsOrDisputedAssessmentsExist()
    {
        var q1 = ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 80);
        q1.ClaimsRefutedCount = 2;
        var run = HarnessV7Run(BenchmarkSecondOpinionMode.Off, q1);
        run.AssessmentText = "Everything was flawless.";
        run.ClaimsRefutedCount = 2;
        BenchmarkRunFinalizer.Apply(run, new[] { q1 });

        var report = BenchmarkReportBuilder.BuildMarkdownReport(run);
        Assert.Contains("The synthesis above is the primary assessor's own narrative. This run recorded 2 refuted claim(s)", report);
    }

    [Fact]
    public void Section5_DoesNotRenderEmptyFlagDescriptions()
    {
        var q1 = ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 80);
        q1.AnswerFlags = (int)BenchmarkAnswerFlags.ContestedVerdict;
        var run = HarnessV7Run(BenchmarkSecondOpinionMode.Off, q1);
        BenchmarkRunFinalizer.Apply(run, new[] { q1 });

        var report = BenchmarkReportBuilder.BuildMarkdownReport(run);
        Assert.DoesNotContain("— .", report);
    }

    [Fact]
    public void KnowledgeBaseRouting_SuppressesUnderUseLineOnAllMechanicsRun()
    {
        var q1 = ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 80);
        q1.QuestionText = "In GnollHack, what do Exceptional and Elite give to body armor?";
        q1.ToolCallSummary = "wiki_search×3, source_code_search×5";
        var run = HarnessV7Run(BenchmarkSecondOpinionMode.Off, q1);
        BenchmarkRunFinalizer.Apply(run, new[] { q1 });

        var report = BenchmarkReportBuilder.BuildMarkdownReport(run);
        Assert.DoesNotContain("Knowledge base under-use:", report);
        Assert.Contains("Per `Overseer/Services/ChatService.cs` § \"Information Routing\" and `Overseer/ToolGuides/get_knowledge_article.md`", report);

        // When a question covers a KB topic, the under-use line appears
        var qKb = ScoredAnswer(2, BenchmarkDifficulty.Simple, 25, 80);
        qKb.QuestionText = "How do I change the tileset settings in GnollHack?";
        qKb.ToolCallSummary = "wiki_search×1";
        var runKb = HarnessV7Run(BenchmarkSecondOpinionMode.Off, qKb);
        BenchmarkRunFinalizer.Apply(runKb, new[] { qKb });

        var reportKb = BenchmarkReportBuilder.BuildMarkdownReport(runKb);
        Assert.Contains("Knowledge base under-use:", reportKb);
    }

    [Fact]
    public void DirectionalAgreement_SentenceAbsentBelowThresholdAndPresentAtOrAbove()
    {
        // Case 1: n = 1 (below threshold of 3)
        var q1 = ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 80);
        q1.SecondOpinionQualityScore = 70; // delta = -10
        var run1 = HarnessV7Run(BenchmarkSecondOpinionMode.Flagged, q1);
        BenchmarkRunFinalizer.Apply(run1, new[] { q1 });

        var report1 = BenchmarkReportBuilder.BuildMarkdownReport(run1);
        Assert.Contains("Mean signed difference:", report1);
        Assert.Contains("(over 1 of 1 answered)", report1);
        Assert.DoesNotContain("A one-directional gap of this size is a statement about the grader", report1);

        // Case 2: n = 3 (meets threshold of 3)
        var qA = ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 80);
        qA.SecondOpinionQualityScore = 70;
        var qB = ScoredAnswer(2, BenchmarkDifficulty.Simple, 25, 85);
        qB.SecondOpinionQualityScore = 75;
        var qC = ScoredAnswer(3, BenchmarkDifficulty.Simple, 25, 90);
        qC.SecondOpinionQualityScore = 80;
        var run3 = HarnessV7Run(BenchmarkSecondOpinionMode.All, qA, qB, qC);
        BenchmarkRunFinalizer.Apply(run3, new[] { qA, qB, qC });

        var report3 = BenchmarkReportBuilder.BuildMarkdownReport(run3);
        Assert.Contains("Mean signed difference:", report3);
        Assert.Contains("(over 3 of 3 answered)", report3);
        Assert.Contains("A one-directional gap of this size is a statement about the grader", report3);
    }

    [Fact]
    public void ToolBatchingPolicy_RendersInPromptBlockAndModelBlock()
    {
        var q1 = ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 80);
        var run = HarnessV7Run(BenchmarkSecondOpinionMode.Off, q1);
        run.TestedModelSnapshot = BenchmarkModelSnapshots.Model(provider: "OpenAI", modelId: "gpt-5.6-luna", displayName: "GPT-5.6 Luna", thinkingLevel: "max", parallelExecutionMode: ParallelExecutionMode.OnRequest);
        run.CandidatePromptOptionsJson = new BenchmarkCandidatePromptOptions { VerboseMode = true }.ToCanonicalJson();
        BenchmarkRunFinalizer.Apply(run, new[] { q1 });

        var report = BenchmarkReportBuilder.BuildMarkdownReport(run);
        Assert.Contains("- **Parallel Tool Calls:** OnRequest *(provider-side tool batching)*", report);
        Assert.Contains("- **Tool batching policy:** OnRequest — selects Overseer/ToolGuides/_policy_parallel_on_request.md, so this is part of the prompt text.", report);
    }

    [Fact]
    public void ComparabilitySignature_IncludesCanonicalJsonAndParallelMode()
    {
        var opts = new BenchmarkCandidatePromptOptions { VerboseMode = true };
        string sig = opts.ComparabilitySignature(ParallelExecutionMode.OnRequest);
        Assert.StartsWith(opts.ToCanonicalJson(), sig);
        Assert.EndsWith("|parallelMode=1", sig);
    }

    [Fact]
    public void EstimatedCost_RendersNotAvailable_WhenRunPricingIsNull()
    {
        var q1 = ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 80);
        var run = HarnessV7Run(BenchmarkSecondOpinionMode.Off, q1);
        BenchmarkRunFinalizer.Apply(run, new[] { q1 });
        run.TotalAssessmentInputTokens = 1000;
        run.TotalAssessmentOutputTokens = 200;

        var report = BenchmarkReportBuilder.BuildMarkdownReport(run, runPricing: null);
        Assert.Contains("- **Estimated Cost:** not available — no price is known for candidate, assessor. Set a price in Admin → System AI Configs (Custom), or add `pricing` to the model's catalog entry.", report);
    }

    [Fact]
    public void EstimatedCost_RendersNotAvailable_WhenModelPricingIsMissingForRole()
    {
        var q1 = ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 80);
        var run = HarnessV7Run(BenchmarkSecondOpinionMode.Off, q1);
        run.TestedModelSnapshot = BenchmarkModelSnapshots.Model(provider: "OpenAI", modelId: "gpt-5.6", displayName: "GPT-5.6 Luna", thinkingLevel: "max");
        run.AssessorModelSnapshot = BenchmarkModelSnapshots.Model(provider: "Google", modelId: "gemini-3.7-flash", displayName: "Gemini 3.7 Flash");
        BenchmarkRunFinalizer.Apply(run, new[] { q1 });
        run.TotalAssessmentInputTokens = 1000;
        run.TotalAssessmentOutputTokens = 200;

        var runPricing = new BenchmarkRunPricing(
            Candidate: new ModelPricing(2.50m, 10.00m, Source: ModelPricingSource.Catalog, AsOf: "2026-09-05"),
            Assessor: null,
            SecondOpinion: null,
            ClaimVerifier: null,
            IsSnapshot: true
        );

        var report = BenchmarkReportBuilder.BuildMarkdownReport(run, runPricing: runPricing);
        Assert.Contains("- **Estimated Cost:** not available — no price is known for assessor. Set a price in Admin → System AI Configs (Custom), or add `pricing` to the model's catalog entry.", report);
    }

    [Fact]
    public void EstimatedCost_RendersCostBreakdown_WhenPricingIsConfigured()
    {
        var q1 = ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 80);
        var run = HarnessV7Run(BenchmarkSecondOpinionMode.Off, q1);
        run.TestedModelSnapshot = BenchmarkModelSnapshots.Model(provider: "OpenAI", modelId: "gpt-5.6", displayName: "GPT-5.6 Luna", thinkingLevel: "max");
        run.AssessorModelSnapshot = BenchmarkModelSnapshots.Model(provider: "Google", modelId: "gemini-3.7-flash", displayName: "Gemini 3.7 Flash");
        run.ClaimVerifierModelSnapshot = BenchmarkModelSnapshots.Model(modelId: "gpt-5-mini");
        run.TotalInputTokens = 1_000_000;
        run.TotalCacheReadTokens = 800_000;
        run.TotalOutputTokens = 50_000;
        run.TotalAssessmentInputTokens = 200_000;
        run.TotalAssessmentOutputTokens = 10_000;
        run.TotalClaimVerificationInputTokens = 100_000;
        run.TotalClaimVerificationOutputTokens = 5_000;

        var runPricing = new BenchmarkRunPricing(
            Candidate: new ModelPricing(2.50m, 10.00m, CachedInputPerMillion: 0.25m, Source: ModelPricingSource.Catalog, AsOf: "2026-09-05"),
            Assessor: new ModelPricing(0.15m, 0.60m, Source: ModelPricingSource.Catalog, AsOf: "2026-09-05"),
            SecondOpinion: null,
            ClaimVerifier: new ModelPricing(1.00m, 4.00m, Source: ModelPricingSource.Custom),
            IsSnapshot: true
        );

        var report = BenchmarkReportBuilder.BuildMarkdownReport(run, runPricing: runPricing);
        Assert.Contains("- **Estimated Cost:** $1.36 total", report);
        Assert.Contains("Candidate (gpt-5.6): $1.20 (uncached in: $0.50, cached in: $0.20, out: $0.50)", report);
        Assert.Contains("Assessor (gemini-3.7-flash): $0.04 (in: $0.03, out: $0.01)", report);
        Assert.Contains("Claim Verifier (gpt-5-mini): $0.12 (in: $0.10, out: $0.02)", report);
        Assert.Contains("- *Prices: candidate catalog (as of 2026-09-05); assessor catalog (as of 2026-09-05); verifier custom.*", report);
    }

    [Fact]
    public void ClaimVerificationYield_ReportsWhatTheVerifiersDollarsBought()
    {
        // Run 14's shape: 10 claims checked, none refuted, $1.70 of verifier spend against a
        // $2.52 run — two thirds of the run's cost for zero refutations. That ratio is the figure
        // an operator steers by, and before H4 it appeared nowhere.
        var q1 = ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 80);
        var run = HarnessV7Run(BenchmarkSecondOpinionMode.Off, q1);
        run.TestedModelSnapshot = BenchmarkModelSnapshots.Model(provider: "OpenAI", modelId: "gpt-5.6", displayName: "GPT-5.6 Luna", thinkingLevel: "max");
        run.AssessorModelSnapshot = BenchmarkModelSnapshots.Model(provider: "Google", modelId: "gemini-3.7-flash", displayName: "Gemini 3.7 Flash");
        run.ClaimVerifierModelSnapshot = BenchmarkModelSnapshots.Model(modelId: "gpt-5-mini");
        run.TotalInputTokens = 200_000;
        run.TotalOutputTokens = 30_000;
        run.TotalAssessmentInputTokens = 100_000;
        run.TotalAssessmentOutputTokens = 10_000;
        run.TotalClaimVerificationInputTokens = 1_300_000;
        run.TotalClaimVerificationOutputTokens = 100_000;
        run.ClaimsSupportedCount = 7;
        run.ClaimsRefutedCount = 0;
        run.ClaimsIndeterminateCount = 3;

        var runPricing = new BenchmarkRunPricing(
            Candidate: new ModelPricing(2.50m, 10.00m, Source: ModelPricingSource.Catalog, AsOf: "2026-09-05"),
            Assessor: new ModelPricing(0.15m, 0.60m, Source: ModelPricingSource.Catalog, AsOf: "2026-09-05"),
            SecondOpinion: null,
            ClaimVerifier: new ModelPricing(1.00m, 4.00m, Source: ModelPricingSource.Custom),
            IsSnapshot: true
        );

        var report = BenchmarkReportBuilder.BuildMarkdownReport(run, runPricing: runPricing);

        Assert.Contains(
            "- **Claim Verification Yield:** 10 claim(s) checked — 7 supported, 0 refuted, 3 indeterminate. $1.70 ($0.17/claim), 67% of run cost.",
            report);
    }

    [Fact]
    public void HarnessCost_PrintsTheFiveRoleLinesContiguously_ThenTheClaimVerificationYieldLine()
    {
        // H4 follow-up: the Yield line used to sit between the Claim Verifier and Synthesis cost
        // lines, splitting the five role lines apart. It now prints after Synthesis, so a reader
        // can read the cost of every role in one unbroken block before the yield commentary.
        var q1 = ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 80);
        var run = HarnessV7Run(BenchmarkSecondOpinionMode.All, q1);
        run.TestedModelSnapshot = BenchmarkModelSnapshots.Model(provider: "OpenAI", modelId: "gpt-5.6", displayName: "GPT-5.6 Luna", thinkingLevel: "max");
        run.AssessorModelSnapshot = BenchmarkModelSnapshots.Model(provider: "Google", modelId: "gemini-3.7-flash", displayName: "Gemini 3.7 Flash");
        run.SecondOpinionAssessorModelSnapshot = BenchmarkModelSnapshots.Model(provider: "Google", modelId: "gemini-3.7-pro");
        run.ClaimVerifierModelSnapshot = BenchmarkModelSnapshots.Model(modelId: "gpt-5-mini");
        run.TotalInputTokens = 200_000;
        run.TotalOutputTokens = 30_000;
        run.TotalAssessmentInputTokens = 100_000;
        run.TotalAssessmentOutputTokens = 10_000;
        run.TotalSecondOpinionInputTokens = 50_000;
        run.TotalSecondOpinionOutputTokens = 5_000;
        run.TotalClaimVerificationInputTokens = 1_300_000;
        run.TotalClaimVerificationOutputTokens = 100_000;
        run.TotalSynthesisInputTokens = 50_000;
        run.TotalSynthesisOutputTokens = 2_000;
        run.ClaimsSupportedCount = 7;
        run.ClaimsRefutedCount = 0;
        run.ClaimsIndeterminateCount = 3;

        var runPricing = new BenchmarkRunPricing(
            Candidate: new ModelPricing(2.50m, 10.00m, Source: ModelPricingSource.Catalog, AsOf: "2026-09-05"),
            Assessor: new ModelPricing(0.15m, 0.60m, Source: ModelPricingSource.Catalog, AsOf: "2026-09-05"),
            SecondOpinion: new ModelPricing(1.25m, 5.00m, Source: ModelPricingSource.Catalog, AsOf: "2026-09-05"),
            ClaimVerifier: new ModelPricing(1.00m, 4.00m, Source: ModelPricingSource.Custom),
            IsSnapshot: true
        );

        var report = BenchmarkReportBuilder.BuildMarkdownReport(run, runPricing: runPricing);

        int candidateAt = report.IndexOf("Candidate (gpt-5.6):", StringComparison.Ordinal);
        int assessorAt = report.IndexOf("Assessor (gemini-3.7-flash):", StringComparison.Ordinal);
        int secondOpinionAt = report.IndexOf("Second Opinion (gemini-3.7-pro):", StringComparison.Ordinal);
        int verifierAt = report.IndexOf("Claim Verifier (gpt-5-mini):", StringComparison.Ordinal);
        int synthesisAt = report.IndexOf("Synthesis (gemini-3.7-flash):", StringComparison.Ordinal);
        int yieldAt = report.IndexOf("**Claim Verification Yield:**", StringComparison.Ordinal);

        Assert.True(candidateAt >= 0 && assessorAt > candidateAt && secondOpinionAt > assessorAt
            && verifierAt > secondOpinionAt && synthesisAt > verifierAt && yieldAt > synthesisAt,
            "Expected Candidate, Assessor, Second Opinion, Claim Verifier and Synthesis to print contiguously, with the Yield line after all five.");
    }

    [Fact]
    public void ClaimVerificationYield_IsOmitted_WhenNoClaimWasChecked()
    {
        // A verifier that was configured, billed, and checked nothing must not render a yield line
        // with a zero denominator — "no claims" and "no verifier" are different facts.
        var q1 = ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 80);
        var run = HarnessV7Run(BenchmarkSecondOpinionMode.Off, q1);
        run.TestedModelSnapshot = BenchmarkModelSnapshots.Model(provider: "OpenAI", modelId: "gpt-5.6", displayName: "GPT-5.6 Luna", thinkingLevel: "max");
        run.AssessorModelSnapshot = BenchmarkModelSnapshots.Model(provider: "Google", modelId: "gemini-3.7-flash", displayName: "Gemini 3.7 Flash");
        run.ClaimVerifierModelSnapshot = BenchmarkModelSnapshots.Model(modelId: "gpt-5-mini");
        run.TotalInputTokens = 200_000;
        run.TotalOutputTokens = 30_000;
        run.TotalAssessmentInputTokens = 100_000;
        run.TotalAssessmentOutputTokens = 10_000;
        run.TotalClaimVerificationInputTokens = 1_300_000;
        run.TotalClaimVerificationOutputTokens = 100_000;

        var runPricing = new BenchmarkRunPricing(
            Candidate: new ModelPricing(2.50m, 10.00m, Source: ModelPricingSource.Catalog, AsOf: "2026-09-05"),
            Assessor: new ModelPricing(0.15m, 0.60m, Source: ModelPricingSource.Catalog, AsOf: "2026-09-05"),
            SecondOpinion: null,
            ClaimVerifier: new ModelPricing(1.00m, 4.00m, Source: ModelPricingSource.Custom),
            IsSnapshot: true
        );

        var report = BenchmarkReportBuilder.BuildMarkdownReport(run, runPricing: runPricing);

        Assert.DoesNotContain("**Claim Verification Yield:**", report);
    }

    [Fact]
    public void ClaimVerificationNotChecked_IsReportedApartFromAVerifierFailure()
    {
        // The budget skip and a real verifier failure share one field. Reporting a budget cutoff
        // as a verifier defect would send an operator hunting a bug that is a setting.
        var q1 = ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 80);
        q1.UnverifiedClaimCount = 2;
        var q2 = ScoredAnswer(2, BenchmarkDifficulty.Simple, 30, 75);
        q2.UnverifiedClaimCount = 3;
        q2.ClaimVerificationError = BenchmarkService.BenchmarkClaimVerificationNotCheckedReason(50000);

        var report = BenchmarkReportBuilder.BuildMarkdownReport(
            HarnessV7Run(BenchmarkSecondOpinionMode.Off, q1, q2));

        Assert.Contains("- **Claim Verification Not Checked (budget):** 1 answer(s) (Q2)", report);
        Assert.Contains("`Benchmark:ClaimVerificationInputTokenBudget` was exhausted", report);
        Assert.DoesNotContain("**Claim Verification Failed:**", report);
    }

    [Fact]
    public void ClaimVerificationFailure_IsStillReportedAsAFailure_NotAsABudgetSkip()
    {
        // The other half of the same distinction: an ordinary verifier error must keep reading as
        // a failure once the budget sentinel exists.
        var q1 = ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 80);
        q1.UnverifiedClaimCount = 2;
        q1.ClaimVerificationError = "The verifier returned no parsable verdict.";

        var report = BenchmarkReportBuilder.BuildMarkdownReport(
            HarnessV7Run(BenchmarkSecondOpinionMode.Off, q1));

        Assert.Contains("**Claim Verification Failed:**", report);
        Assert.DoesNotContain("**Claim Verification Not Checked (budget):**", report);
    }

    [Fact]
    public void ClaimVerification_CarriesEachAnswersSpend_AndTheRunListsTheHighest()
    {
        var q1 = ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 80);
        q1.UnverifiedClaimCount = 2;
        q1.ClaimsSupportedCount = 2;
        q1.ClaimVerificationToolCallCount = 6;
        q1.ClaimVerificationInputTokens = 40_000;
        q1.ClaimVerificationDurationMs = 12_500;
        var q2 = ScoredAnswer(2, BenchmarkDifficulty.Simple, 30, 75);
        q2.UnverifiedClaimCount = 1;
        q2.ClaimsRefutedCount = 1;
        q2.ClaimVerificationToolCallCount = 14;
        q2.ClaimVerificationInputTokens = 120_000;
        q2.ClaimVerificationDurationMs = 30_000;
        var q3 = ScoredAnswer(3, BenchmarkDifficulty.Advanced, 85, 90);
        q3.UnverifiedClaimCount = 1;
        q3.ClaimsIndeterminateCount = 1;
        q3.ClaimVerificationToolCallCount = 10;
        q3.ClaimVerificationInputTokens = 80_000;
        q3.ClaimVerificationDurationMs = 20_000;

        var run = HarnessV7Run(BenchmarkSecondOpinionMode.Off, q1, q2, q3);
        run.TotalInputTokens = 200_000;
        run.TotalOutputTokens = 30_000;
        run.TotalClaimVerificationInputTokens = 240_000;
        run.TotalClaimVerificationOutputTokens = 10_000;

        var report = BenchmarkReportBuilder.BuildMarkdownReport(run);

        Assert.Contains("advisory, not reflected in the score.* — 14 tool call(s), 120,000 input tokens, 30.0 s", report);
        Assert.Contains(
            "- **Verifier spend by answer:** highest Q2 (120,000 input tokens), Q3, Q1; mean 80,000 input tokens and 10.0 tool calls per verified answer.",
            report);
    }

    [Fact]
    public void ClaimVerificationFailure_ShowsTheHeadOfTheRawResponse_OnOneLineWithoutBackticks()
    {
        var q1 = ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 80);
        q1.UnverifiedClaimCount = 2;
        q1.ClaimVerificationError = "The verifier returned no parsable verdict.";
        q1.ClaimVerificationRawText = "```json\n{\"verdicts\": [\n  {\"claim\": \"x\"" + new string('z', 700);

        var report = BenchmarkReportBuilder.BuildMarkdownReport(
            HarnessV7Run(BenchmarkSecondOpinionMode.Off, q1));

        string line = report.Split('\n').Single(l => l.Contains("Raw response, first 600 characters:"));
        Assert.Contains("failed — The verifier returned no parsable verdict.", line);
        Assert.Contains("Raw response, first 600 characters: '''json {\"verdicts\": [ {\"claim\": \"x\"", line);
        Assert.DoesNotContain("`", line.Substring(line.IndexOf("Raw response", StringComparison.Ordinal)));
        Assert.DoesNotContain(new string('z', 600), line);
    }

    // -------------------------------------------------------------------------------------
    // Run 22 truthfulness fixes: each fixture below reproduces the shape of the defect the
    // hand analysis of that run found.
    // -------------------------------------------------------------------------------------

    [Fact]
    public void ModeGloss_DescribesFlaggedPlusSample_InsteadOfClaimingNoSecondVerdict()
    {
        // FlaggedPlusSample fell through ModeGloss's default arm, which reads as Off: a run
        // configured with it was reported as having had no second verdict configured at all.
        var run = HarnessV7Run(
            BenchmarkSecondOpinionMode.FlaggedPlusSample,
            ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 90));
        run.SecondOpinionAssessorModelConfigurationId = 7;
        run.SecondOpinionAssessorModelSnapshot = BenchmarkModelSnapshots.Model(provider: "Anthropic", modelId: "claude-reviewer-1", displayName: "Claude Reviewer");

        var report = BenchmarkReportBuilder.BuildMarkdownReport(run);

        Assert.Contains("flagged answers, topped up to the profile's minimum sample", report);
        Assert.DoesNotContain("no second verdict was configured", report);
    }

    [Fact]
    public void RawQualityIndexHeadline_NamesWhatItCounts_NotCappedByCriticalError()
    {
        // cappedCount counts answers whose score the cap actually lowered, which is a different
        // quantity from "carries the critical-error flag" — the two must not share one phrase.
        var capped = ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 25);
        capped.RawQualityScore = 95;
        capped.CriticalError = true;
        var clean = ScoredAnswer(2, BenchmarkDifficulty.Simple, 30, 90);

        var report = BenchmarkReportBuilder.BuildMarkdownReport(
            HarnessV7Run(BenchmarkSecondOpinionMode.Off, capped, clean));

        Assert.Contains("question(s) whose score the cap lowered", report);
        Assert.DoesNotContain("capped by critical error", report);
    }

    [Fact]
    public void PerAnswerCapMarker_DistinguishesActualCappingFromCriticalErrorAlone()
    {
        // Run 22's shape: Q1 and Q16 scored 21 raw, already below the cap of 25, so the cap
        // changed nothing even though both carry a critical error. The marker must say so
        // instead of claiming the cap applied.
        var capNotBinding = ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 21);
        capNotBinding.CriticalError = true;

        var capBinding = ScoredAnswer(2, BenchmarkDifficulty.Simple, 25, 25);
        capBinding.RawQualityScore = 95;
        capBinding.CriticalError = true;

        var report = BenchmarkReportBuilder.BuildMarkdownReport(
            HarnessV7Run(BenchmarkSecondOpinionMode.Off, capNotBinding, capBinding));

        Assert.Contains("*(CRITICAL ERROR — cap not binding)*", report);
        Assert.Contains("*(CRITICAL ERROR CAP APPLIED)*", report);
    }

    [Fact]
    public void BandRangeLabel_NamesQualityRange_NotDifficultyRange()
    {
        // 21 and 77 here are QualityScore values, not difficulty — the bare "range " literal
        // printed right after "avg diff: 29" reads as a difficulty range instead.
        var report = BenchmarkReportBuilder.BuildMarkdownReport(HarnessV7Run(
            BenchmarkSecondOpinionMode.Off,
            ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 21),
            ScoredAnswer(2, BenchmarkDifficulty.Simple, 33, 77)));

        Assert.Contains("avg diff: 29, quality range 21–77", report);
    }

    [Fact]
    public void FinalIndices_CarriesContestedVerdictSensitivity_WhenContestedCriticalAnswersExist()
    {
        // Section 7 carries the headline figures; it must not drop the one number that says how
        // fragile they are, and it must not recompute it — the same value has to appear in both
        // places.
        var clean = ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 90);
        var contested = ScoredAnswer(12, BenchmarkDifficulty.Intermediate, 55, 42);
        contested.AnswerFlags = (int)BenchmarkAnswerFlags.ContestedVerdict;
        contested.SecondOpinionCriticalError = true;
        contested.SecondOpinionQualityScore = 25;

        var report = BenchmarkReportBuilder.BuildMarkdownReport(
            HarnessV7Run(BenchmarkSecondOpinionMode.Off, clean, contested));

        int finalIndicesStart = report.IndexOf("## 7. Final Indices", StringComparison.Ordinal);
        Assert.True(finalIndicesStart >= 0);

        var matches = Regex.Matches(report, @"Contested-Verdict Sensitivity:\**\s*(\d+)\s*/\s*100");
        Assert.Equal(2, matches.Count);
        Assert.True(matches[0].Index < finalIndicesStart, "§ 2 must carry the sensitivity figure.");
        Assert.True(matches[1].Index > finalIndicesStart, "§ 7 must carry the same sensitivity figure.");
        Assert.Equal(matches[0].Groups[1].Value, matches[1].Groups[1].Value);
    }

    [Fact]
    public void SpeedIndexSaturationNotice_AppearsWhenAtLeastHalfTheAnswersAreAtTheCeiling()
    {
        // Boundary case: exactly half at the ceiling, which the "at least half" predicate must
        // still catch. Run 22's shape was 17 of 18 at 100 with a Speed Index that carried no
        // discriminating information at all.
        var a1 = ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 90);
        a1.SpeedScore = 100;
        a1.DurationMs = 5000;
        var a2 = ScoredAnswer(2, BenchmarkDifficulty.Simple, 25, 92);
        a2.SpeedScore = 100;
        a2.DurationMs = 7000;
        var a3 = ScoredAnswer(3, BenchmarkDifficulty.Simple, 25, 88);
        a3.SpeedScore = 60;
        a3.DurationMs = 20000;
        var a4 = ScoredAnswer(4, BenchmarkDifficulty.Simple, 25, 91);
        a4.SpeedScore = 55;
        a4.DurationMs = 25000;

        var report = BenchmarkReportBuilder.BuildMarkdownReport(
            HarnessV7Run(BenchmarkSecondOpinionMode.Off, a1, a2, a3, a4));

        // The true statistical median over the four (sorted 5000, 7000, 20000, 25000) is the
        // mean of the two middle values, 13,500 ms — not 7,000 ms, which is Percentile's
        // nearest-rank pick.
        Assert.Contains(
            "*Saturated — 2 of 4 answers finished inside their difficulty-scaled target, so this index cannot discriminate at this speed. Compare median model time (13,500 ms) instead.*",
            report);
    }

    [Fact]
    public void SpeedIndexSaturationNotice_IsAbsentWhenFewerThanHalfAreAtTheCeiling()
    {
        var a1 = ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 90);
        a1.SpeedScore = 100;
        a1.DurationMs = 5000;
        var a2 = ScoredAnswer(2, BenchmarkDifficulty.Simple, 25, 92);
        a2.SpeedScore = 60;
        a2.DurationMs = 20000;
        var a3 = ScoredAnswer(3, BenchmarkDifficulty.Simple, 25, 88);
        a3.SpeedScore = 55;
        a3.DurationMs = 22000;
        var a4 = ScoredAnswer(4, BenchmarkDifficulty.Simple, 25, 91);
        a4.SpeedScore = 50;
        a4.DurationMs = 25000;

        var report = BenchmarkReportBuilder.BuildMarkdownReport(
            HarnessV7Run(BenchmarkSecondOpinionMode.Off, a1, a2, a3, a4));

        Assert.DoesNotContain("Saturated —", report);
    }

    // --- Aborted runs, provenance, and the failure to answer -----------------------------

    /// <summary>
    /// An answer the model itself ended without producing text: status EmptyAnswer plus a provider
    /// finish reason that means a normal stop, scored 0 by rule rather than by a grader.
    /// </summary>
    private static BenchmarkRunAnswer UnansweredAnswer(int orderIndex, int assessedDifficulty = 50)
    {
        return new BenchmarkRunAnswer
        {
            OrderIndex = orderIndex,
            QuestionText = $"Q{orderIndex}",
            AnswerText = string.Empty,
            Difficulty = BenchmarkDifficulty.Intermediate,
            AssessedDifficulty = assessedDifficulty,
            Status = BenchmarkAnswerStatus.EmptyAnswer,
            AssessmentStatus = BenchmarkAssessmentStatus.Scored,
            ProviderFinishReason = "STOP",
            QualityScore = 0,
            RawQualityScore = 0,
            Score = 0,
            AnswerFlags = (int)BenchmarkAnswerFlags.Empty,
            ReviewComment = "Not assessed by a grader: the model ended its turn without producing an answer. Scored 0 under scoring method 10."
        };
    }

    [Fact]
    public void HarnessCost_AppearsForARunWithCandidateTokensAndNoGradingSpend()
    {
        // Run 24's shape: 2.86 M candidate input tokens and no grading stage at all. Gating the
        // section on the grading roles hid the cost of exactly the runs whose cost is least
        // obvious elsewhere.
        var run = HarnessV7Run(BenchmarkSecondOpinionMode.Off, ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 80));
        run.Status = BenchmarkRunStatus.Canceled;
        run.TotalInputTokens = 2_856_966;
        run.TotalOutputTokens = 25_387;
        run.TotalAssessmentInputTokens = 0;
        run.TotalAssessmentOutputTokens = 0;
        run.TotalAssessmentDurationMs = 0;

        var report = BenchmarkReportBuilder.BuildMarkdownReport(run);

        Assert.Contains("### Harness Cost", report);
        Assert.Contains("**Candidate Tokens:** 2,856,966 in / 25,387 out", report);
    }

    [Fact]
    public void ToolOverheadProvenance_BlamesAgeOnlyForARunThatIsActuallyOld()
    {
        var current = HarnessV7Run(BenchmarkSecondOpinionMode.Off, ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 80));
        current.HarnessVersion = "12";

        string currentReport = BenchmarkReportBuilder.BuildMarkdownReport(current);
        Assert.Contains("**Tool Overhead:** Not recorded — no answered question carries tool timing.", currentReport);
        Assert.DoesNotContain("predates harness version 3", currentReport);

        var old = HarnessV7Run(BenchmarkSecondOpinionMode.Off, ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 80));
        old.HarnessVersion = "2";

        string oldReport = BenchmarkReportBuilder.BuildMarkdownReport(old);
        Assert.Contains("predates harness version 3", oldReport);
    }

    [Fact]
    public void AssessorAccountingProvenance_BlamesAgeOnlyForARunThatIsActuallyOld()
    {
        var current = HarnessV7Run(BenchmarkSecondOpinionMode.Off, ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 80));
        current.HarnessVersion = "12";
        current.TotalInputTokens = 0;
        current.TotalOutputTokens = 0;

        string currentReport = BenchmarkReportBuilder.BuildMarkdownReport(current);
        Assert.Contains("*Assessor and claim-verifier accounting is zero for this run: no grading stage recorded any usage.*", currentReport);
        Assert.DoesNotContain("predates harness version 4", currentReport);
    }

    [Fact]
    public void UnverifiedClaimsProvenance_NeverClaimsARunPredatesItsOwnHarnessVersion()
    {
        var current = HarnessV7Run(BenchmarkSecondOpinionMode.Off, ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 80));
        current.HarnessVersion = "12";

        string report = BenchmarkReportBuilder.BuildMarkdownReport(current);

        Assert.Contains("**Unverified Claims:** not recorded — no answer carries a claim count", report);
        Assert.DoesNotContain("predates harness version 12", report);
    }

    [Fact]
    public void AnswerRate_ReportsAnsweredAgainstTheSuiteSize()
    {
        var run = HarnessV7Run(BenchmarkSecondOpinionMode.Off, ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 80));
        run.AnsweredQuestionCount = 3;
        run.TotalQuestionCount = 18;

        var report = BenchmarkReportBuilder.BuildMarkdownReport(run);

        Assert.Contains("**Answer Rate:** 3 of 18 (16.7%)", report);
    }

    [Fact]
    public void UnansweredQuestions_AreNamedAndScoredZero_AndTheLineIsOmittedAtZero()
    {
        var answered = ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 80);
        var run = HarnessV7Run(BenchmarkSecondOpinionMode.Off, answered, UnansweredAnswer(4));
        run.Status = BenchmarkRunStatus.CompletedWithErrors;
        run.TotalQuestionCount = 18;
        run.AnsweredQuestionCount = 1;
        run.UnansweredQuestionCount = 1;

        var report = BenchmarkReportBuilder.BuildMarkdownReport(run);

        Assert.Contains("**Unanswered Questions:** 1 of 18 (question(s) 4)", report);
        Assert.Contains("**Unanswered:** 1 — *the model produced no answer; scored 0, not excluded*", report);
        Assert.Contains("**Reply:** *(No answer — the model ended its turn without producing text; provider finish reason: `STOP`)*", report);
        Assert.Contains("**Quality Score:** 0 / 100 *(NO ANSWER — scored 0 by rule; no grader read this)*", report);
        Assert.Contains("*(Scored 0: no answer produced)*", report);

        var cleanRun = HarnessV7Run(BenchmarkSecondOpinionMode.Off, ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 80));
        Assert.DoesNotContain("**Unanswered Questions:**", BenchmarkReportBuilder.BuildMarkdownReport(cleanRun));
    }

    [Fact]
    public void TransportDefectEmpty_KeepsTheOldWordingAndStaysExcludedFromScoring()
    {
        // No recorded finish reason is "not recorded", never "stopped normally", so this answer
        // stays a transport defect and stays unscored.
        var defect = UnansweredAnswer(2);
        defect.ProviderFinishReason = null;
        defect.QualityScore = null;
        defect.RawQualityScore = null;
        defect.Score = null;
        defect.ReviewComment = null;

        var run = HarnessV7Run(
            BenchmarkSecondOpinionMode.Off,
            ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 80),
            defect);
        run.Status = BenchmarkRunStatus.CompletedWithErrors;

        var report = BenchmarkReportBuilder.BuildMarkdownReport(run);

        Assert.Contains("**Reply:** *(Empty answer produced)*", report);
        Assert.Contains("*(Note: Excluded from scoring)*", report);
        Assert.DoesNotContain("*(Scored 0: no answer produced)*", report);
        Assert.DoesNotContain("**Unanswered Questions:**", report);
    }

    [Fact]
    public void AggregationFormulas_SayAnUnansweredQuestionScoresZero()
    {
        var report = BenchmarkReportBuilder.BuildMarkdownReport(
            HarnessV7Run(BenchmarkSecondOpinionMode.Off, ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 80)));

        Assert.Contains("over answered questions and unanswered questions alike, the latter at 0", report);
        Assert.Contains("over answered questions only, since an answer that does not exist has no latency", report);
    }

    // -------------------------------------------------------------------------------------
    // Harness version 15: per-role cost tracking (Second Opinion and Synthesis as their own
    // Harness Cost roles, and the Grading subtotal that sums them with the assessor and verifier).
    // -------------------------------------------------------------------------------------

    [Fact]
    public void HarnessCost_SecondOpinionAndSynthesisTokenLinesAppear_AndTotalTokensSumsFiveRoles()
    {
        var q1 = ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 80);
        var run = HarnessV7Run(BenchmarkSecondOpinionMode.All, q1);
        run.HarnessVersion = "15";
        run.TotalInputTokens = 100_000;
        run.TotalOutputTokens = 10_000;
        run.TotalAssessmentInputTokens = 50_000;
        run.TotalAssessmentOutputTokens = 5_000;
        run.TotalSecondOpinionInputTokens = 20_000;
        run.TotalSecondOpinionOutputTokens = 2_000;
        run.TotalClaimVerificationInputTokens = 10_000;
        run.TotalClaimVerificationOutputTokens = 1_000;
        run.TotalSynthesisInputTokens = 5_000;
        run.TotalSynthesisOutputTokens = 500;

        var report = BenchmarkReportBuilder.BuildMarkdownReport(run);

        Assert.Contains("**Second Opinion Tokens:** 20,000 in / 2,000 out", report);
        Assert.Contains("**Synthesis Tokens:** 5,000 in / 500 out", report);
        // 100,000 + 50,000 + 20,000 + 10,000 + 5,000 in; 10,000 + 5,000 + 2,000 + 1,000 + 500 out.
        Assert.Contains("**Total Tokens:** 185,000 in / 18,500 out", report);
    }

    [Fact]
    public void HarnessCost_PrintsARolesCacheFiguresOnlyWhenNonZero()
    {
        var withCache = HarnessV7Run(BenchmarkSecondOpinionMode.Off, ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 80));
        withCache.HarnessVersion = "15";
        withCache.TotalAssessmentInputTokens = 50_000;
        withCache.TotalAssessmentOutputTokens = 5_000;
        withCache.TotalAssessmentCacheReadTokens = 12_000;
        withCache.TotalAssessmentCacheCreationTokens = 3_000;

        var reportWithCache = BenchmarkReportBuilder.BuildMarkdownReport(withCache);
        Assert.Contains("**Assessor Tokens:** 50,000 in / 5,000 out (12,000 cache read, 3,000 cache creation)", reportWithCache);

        var withoutCache = HarnessV7Run(BenchmarkSecondOpinionMode.Off, ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 80));
        withoutCache.HarnessVersion = "15";
        withoutCache.TotalAssessmentInputTokens = 50_000;
        withoutCache.TotalAssessmentOutputTokens = 5_000;

        var reportWithoutCache = BenchmarkReportBuilder.BuildMarkdownReport(withoutCache);
        Assert.Contains("**Assessor Tokens:** 50,000 in / 5,000 out", reportWithoutCache);
        Assert.DoesNotContain("cache read", reportWithoutCache);
        Assert.DoesNotContain("cache creation", reportWithoutCache);
    }

    [Fact]
    public void EstimatedCost_RendersGradingSubtotalAndSecondOpinionAndSynthesisPeerLines()
    {
        var q1 = ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 80);
        var run = HarnessV7Run(BenchmarkSecondOpinionMode.All, q1);
        run.HarnessVersion = "15";
        run.TestedModelSnapshot = BenchmarkModelSnapshots.Model(provider: "OpenAI", modelId: "gpt-5.6", displayName: "GPT-5.6 Luna", thinkingLevel: "max");
        run.AssessorModelSnapshot = BenchmarkModelSnapshots.Model(provider: "Google", modelId: "gemini-3.7-flash", displayName: "Gemini 3.7 Flash");
        run.SecondOpinionAssessorModelSnapshot = BenchmarkModelSnapshots.Model(provider: "Google", modelId: "gemini-3.7-pro");
        run.TotalInputTokens = 1_000_000;
        run.TotalOutputTokens = 50_000;
        run.TotalAssessmentInputTokens = 200_000;
        run.TotalAssessmentOutputTokens = 10_000;
        run.TotalSecondOpinionInputTokens = 100_000;
        run.TotalSecondOpinionOutputTokens = 5_000;
        run.TotalSynthesisInputTokens = 50_000;
        run.TotalSynthesisOutputTokens = 2_000;

        var runPricing = new BenchmarkRunPricing(
            Candidate: new ModelPricing(2.50m, 10.00m, Source: ModelPricingSource.Catalog, AsOf: "2026-09-05"),
            Assessor: new ModelPricing(0.15m, 0.60m, Source: ModelPricingSource.Catalog, AsOf: "2026-09-05"),
            SecondOpinion: new ModelPricing(1.25m, 5.00m, Source: ModelPricingSource.Catalog, AsOf: "2026-09-05"),
            ClaimVerifier: null,
            IsSnapshot: true
        );

        var report = BenchmarkReportBuilder.BuildMarkdownReport(run, runPricing: runPricing);

        // Dollar figures are sourced from ModelPricingService.ComputeRunRoleCosts, not recomputed
        // here, so the assertions check which lines and models render rather than exact amounts.
        Assert.Contains("Second Opinion (gemini-3.7-pro):", report);
        Assert.Contains("Synthesis (gemini-3.7-flash):", report);
        Assert.Contains("- **Grading subtotal:**", report);
        Assert.Contains("of total)", report);
        Assert.Contains("second opinion catalog (as of 2026-09-05)", report);
    }

    [Fact]
    public void HarnessCost_PipeliningSentenceGatedOnParallelism_MeasuredOverlapOtherwise()
    {
        var sequential = HarnessV7Run(BenchmarkSecondOpinionMode.Off, ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 80));
        sequential.HarnessVersion = "15";
        sequential.MaxParallelQuestionsUsed = 1;
        sequential.TotalInputTokens = 10_000;
        sequential.TotalOutputTokens = 1_000;
        sequential.TotalAnswerDurationMs = 5_000;
        sequential.TotalAssessmentInputTokens = 2_000;
        sequential.TotalAssessmentOutputTokens = 200;
        sequential.TotalAssessmentDurationMs = 3_000;
        sequential.TotalDurationMs = 9_000;

        var sequentialReport = BenchmarkReportBuilder.BuildMarkdownReport(sequential);
        Assert.DoesNotContain("Assessment runs pipelined behind each answer", sequentialReport);
        // 9,000 wall clock minus (5,000 candidate + 3,000 assessment) leaves a 1,000 ms residual.
        Assert.Contains("*Measured overlap:", sequentialReport);
        Assert.Contains("1,000 ms", sequentialReport);

        var parallel = HarnessV7Run(BenchmarkSecondOpinionMode.Off, ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 80));
        parallel.HarnessVersion = "15";
        parallel.MaxParallelQuestionsUsed = 4;
        parallel.TotalInputTokens = 10_000;
        parallel.TotalOutputTokens = 1_000;
        parallel.TotalAssessmentInputTokens = 2_000;
        parallel.TotalAssessmentOutputTokens = 200;

        var parallelReport = BenchmarkReportBuilder.BuildMarkdownReport(parallel);
        Assert.Contains("Assessment runs pipelined behind each answer", parallelReport);
        Assert.DoesNotContain("*Measured overlap:", parallelReport);
    }

    private static BenchmarkRun RepairedSequentialRun()
    {
        // Run 37's shape: the stage sum exceeds the preserved wall clock by the re-run's answer time.
        var run = HarnessV7Run(BenchmarkSecondOpinionMode.Off, ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 80));
        run.HarnessVersion = "21";
        run.MaxParallelQuestionsUsed = 1;
        run.TotalInputTokens = 10_000;
        run.TotalOutputTokens = 1_000;
        run.TotalAnswerDurationMs = 20_000;
        run.TotalAssessmentInputTokens = 2_000;
        run.TotalAssessmentOutputTokens = 200;
        run.TotalAssessmentDurationMs = 3_000;
        run.TotalDurationMs = 12_000;
        run.CandidateSystemPromptSha256 = "aaaa";
        run.ToolGuidesSha256 = "bbbb";
        run.RerunCandidateSystemPromptSha256 = "aaaa";
        run.RerunToolGuidesSha256 = "bbbb";
        return run;
    }

    [Fact]
    public void RepairedRun_SameInstrument_RendersRerunBlockAndOriginalExecutionTiming()
    {
        var run = RepairedSequentialRun();
        run.RerunStartedAtUtc = new DateTime(2026, 9, 11, 9, 50, 0, DateTimeKind.Utc);
        run.RerunCompletedAtUtc = new DateTime(2026, 9, 11, 10, 2, 8, DateTimeKind.Utc);

        var report = BenchmarkReportBuilder.BuildMarkdownReport(run);

        Assert.Contains("> **Repaired by a failed-question re-run** from ", report);
        Assert.Contains("(12m 8s) under harness not recorded (re-run predates harness 22) (this run: 21)", report);
        Assert.DoesNotContain("Re-run under a different instrument", report);
        Assert.Contains("- **End Time (UTC, original execution):**", report);
        Assert.Contains("- **Re-run span (UTC):**", report);
        Assert.DoesNotContain("- **End Time (UTC):**", report);
        Assert.Contains("*(includes re-executed answers; the wall time above is the original execution's)*", report);
        Assert.Contains("overlap is not computed for a repaired run.*", report);
        Assert.DoesNotContain("*Measured overlap:", report);
    }

    [Fact]
    public void RepairedRun_OnlyRerunStartRecorded_RendersRerunBlockWithRerunHarness()
    {
        var run = RepairedSequentialRun();
        run.RerunStartedAtUtc = new DateTime(2026, 9, 11, 9, 50, 0, DateTimeKind.Utc);
        run.RerunHarnessVersion = "22";

        var report = BenchmarkReportBuilder.BuildMarkdownReport(run);

        Assert.Contains("> **Repaired by a failed-question re-run** from ", report);
        Assert.Contains("to unrecorded end UTC under harness 22 (this run: 21)", report);
    }

    [Fact]
    public void RepairedRun_DifferentInstrument_KeepsCautionAndNamesRerunHarness()
    {
        var run = RepairedSequentialRun();
        run.RerunToolGuidesSha256 = "cccc";
        run.RerunStartedAtUtc = new DateTime(2026, 9, 11, 9, 50, 0, DateTimeKind.Utc);
        run.RerunCompletedAtUtc = new DateTime(2026, 9, 11, 10, 2, 8, DateTimeKind.Utc);
        run.RerunHarnessVersion = "22";

        var report = BenchmarkReportBuilder.BuildMarkdownReport(run);

        Assert.Contains("> **Re-run under a different instrument.**", report);
        Assert.Contains("under harness 22 (this run: 21)", report);
        Assert.DoesNotContain("**Repaired by a failed-question re-run**", report);
    }

    [Fact]
    public void NeverRerunRun_KeepsPlainEndTimeAndNoRerunBlock()
    {
        var run = RepairedSequentialRun();
        run.RerunCandidateSystemPromptSha256 = null;
        run.RerunToolGuidesSha256 = null;

        var report = BenchmarkReportBuilder.BuildMarkdownReport(run);

        Assert.Contains("- **End Time (UTC):**", report);
        Assert.DoesNotContain("Re-run span", report);
        Assert.DoesNotContain("Repaired by a failed-question re-run", report);
        Assert.Contains("*Measured overlap:", report);
    }

    [Fact]
    public void HarnessCost_MeasuredOverlap_StagesExceedingWallClockAreReportedAsAPositiveExcess()
    {
        var run = HarnessV7Run(BenchmarkSecondOpinionMode.Off, ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 80));
        run.HarnessVersion = "15";
        run.MaxParallelQuestionsUsed = 1;
        run.TotalInputTokens = 10_000;
        run.TotalOutputTokens = 1_000;
        run.TotalAnswerDurationMs = 5_000;
        run.TotalAssessmentInputTokens = 2_000;
        run.TotalAssessmentOutputTokens = 200;
        run.TotalAssessmentDurationMs = 3_000;
        run.TotalDurationMs = 6_500;

        var report = BenchmarkReportBuilder.BuildMarkdownReport(run);

        // (5,000 candidate + 3,000 assessment) exceeds the 6,500 wall clock by 1,500 ms.
        Assert.Contains("exceed the wall clock by 1.5s (1,500 ms)", report);
        Assert.DoesNotContain("unaccounted for by sequential stage time", report);
        Assert.DoesNotContain("-1.", report);
    }

    [Fact]
    public void HarnessCost_LegacyPerRoleCostNoteAppearsOnlyBelowHarness15()
    {
        var old = HarnessV7Run(BenchmarkSecondOpinionMode.Off, ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 80));
        old.HarnessVersion = "14";
        old.TotalAssessmentInputTokens = 1_000;
        old.TotalAssessmentOutputTokens = 100;

        var oldReport = BenchmarkReportBuilder.BuildMarkdownReport(old);
        Assert.Contains(
            "*Recorded before per-role cost tracking: the second opinion's spend is inside the assessor line, and the final synthesis is not counted at all.*",
            oldReport);

        var current = HarnessV7Run(BenchmarkSecondOpinionMode.Off, ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 80));
        current.HarnessVersion = "15";
        current.TotalAssessmentInputTokens = 1_000;
        current.TotalAssessmentOutputTokens = 100;

        var currentReport = BenchmarkReportBuilder.BuildMarkdownReport(current);
        Assert.DoesNotContain("Recorded before per-role cost tracking", currentReport);

        // An unparseable version is not evidence of age, so the note must not print for one either.
        var unparseable = HarnessV7Run(BenchmarkSecondOpinionMode.Off, ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 80));
        unparseable.HarnessVersion = "unknown";
        unparseable.TotalAssessmentInputTokens = 1_000;
        unparseable.TotalAssessmentOutputTokens = 100;

        var unparseableReport = BenchmarkReportBuilder.BuildMarkdownReport(unparseable);
        Assert.DoesNotContain("Recorded before per-role cost tracking", unparseableReport);
    }

    // -------------------------------------------------------------------------------------
    // Tool Call Outcomes line and the per-question ordered call table. Both are gated on
    // whether any answer in the run carries BenchmarkRunAnswerToolCall rows, which exist only
    // from harness 17 onward — every run before that has none, and ToolCallSummary remains
    // the only record for those.
    // -------------------------------------------------------------------------------------

    [Fact]
    public void BuildMarkdownReport_RowCarryingRun_RendersOutcomesLineAndOrdersPerQuestionTableBySortOrder()
    {
        var answer = ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 80);
        answer.ToolCalls = new List<BenchmarkRunAnswerToolCall>
        {
            new BenchmarkRunAnswerToolCall { SortOrder = 2, Name = "call_a", Status = "completed", ExecutionMs = 10 },
            new BenchmarkRunAnswerToolCall { SortOrder = 0, Name = "call_b", Status = "completed", ExecutionMs = 20 },
            new BenchmarkRunAnswerToolCall { SortOrder = 1, Name = "call_c", Status = "error", ExecutionMs = 30 }
        };

        var report = BenchmarkReportBuilder.BuildMarkdownReport(HarnessV6Run(answer));

        Assert.Contains("**Tool Call Outcomes:** 2 succeeded, 1 failed, 0 refused by budget.", report);

        // Rendered order follows SortOrder (0, 1, 2) — call_b, then call_c, then call_a — not
        // the order the rows were added to the list.
        //
        // Searched from the section 3 heading onward, not across the whole report. Section 2's
        // Tool Usage Profile also names tools, sorted alphabetically and counting successes only,
        // so a whole-report IndexOf finds call_a and call_b there and measures that table's
        // ordering instead of this one's.
        int sectionStart = report.IndexOf("## 3. Questions and Replies", StringComparison.Ordinal);
        Assert.True(sectionStart >= 0, "The per-question section must be present.");
        string perQuestion = report.Substring(sectionStart);

        int posB = perQuestion.IndexOf("`call_b`", StringComparison.Ordinal);
        int posC = perQuestion.IndexOf("`call_c`", StringComparison.Ordinal);
        int posA = perQuestion.IndexOf("`call_a`", StringComparison.Ordinal);
        Assert.True(posB >= 0 && posC >= 0 && posA >= 0, "All three rows must appear in the call table.");
        Assert.True(posB < posC && posC < posA, "Rows must render in SortOrder, not insertion, order.");
    }

    [Fact]
    public void BuildMarkdownReport_PerQuestionCallTable_RendersArgsPreviewButNeverResultPayloadText()
    {
        const string argsSentinel = "SENTINEL_ARGS_PAYLOAD_9f3c";
        const string resultSentinel = "SENTINEL_RESULT_PAYLOAD_2b7e";

        var answer = ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 80);
        answer.ToolCalls = new List<BenchmarkRunAnswerToolCall>
        {
            new BenchmarkRunAnswerToolCall
            {
                SortOrder = 0,
                Name = "source_code_search",
                Status = "completed",
                ArgsText = argsSentinel,
                Result = resultSentinel,
                ResultLengthChars = resultSentinel.Length
            }
        };

        var report = BenchmarkReportBuilder.BuildMarkdownReport(HarnessV6Run(answer));

        // The Args column carries a bounded preview of the call's arguments; the result column
        // still reports only ResultLengthChars as a bare number, never the payload itself — a
        // single tool result can be tens of thousands of characters of game source, and full
        // payloads belong behind the admin endpoint and the tool-call log export instead.
        Assert.Contains(argsSentinel, report);
        Assert.DoesNotContain(resultSentinel, report);
        Assert.Contains($"{resultSentinel.Length}", report);
    }

    [Fact]
    public void BuildMarkdownReport_LegacyRun_RendersNoOutcomeLineOrCallTable_ButToolUsageProfileStillReadsToolCallSummary()
    {
        var answer = ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 80);
        answer.ToolCallSummary = "wiki_search×5, source_code_search×2";
        // ToolCalls stays at its default empty list: this is exactly the shape of every run
        // recorded before harness 17.

        var report = BenchmarkReportBuilder.BuildMarkdownReport(HarnessV6Run(answer));

        // A legacy run printing "0 failed" would assert something false — no rows exist to
        // count, so the line must be absent rather than printed with zeroes.
        Assert.DoesNotContain("**Tool Call Outcomes:**", report);
        Assert.DoesNotContain("**Not-found results:**", report);
        Assert.DoesNotContain("**Cut before the model saw it:**", report);
        Assert.DoesNotContain("| Round | Tool | Args | Status | Exec (ms) | Result Size | Note |", report);

        Assert.Contains("### Tool Usage Profile", report);
        Assert.Contains("`wiki_search`", report);
        Assert.Contains("| `wiki_search` | 5 |", report);
        Assert.Contains("| `source_code_search` | 2 |", report);
    }

    [Fact]
    public void BuildMarkdownReport_ToolOrderingCaveat_IsQualifiedForRowCarryingRuns_AndUnqualifiedForLegacyRuns()
    {
        var rowCarrying = ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 80);
        rowCarrying.ToolCalls = new List<BenchmarkRunAnswerToolCall>
        {
            new BenchmarkRunAnswerToolCall { SortOrder = 0, Name = "wiki_search", Status = "completed" }
        };
        var rowReport = BenchmarkReportBuilder.BuildMarkdownReport(HarnessV6Run(rowCarrying));

        // Deleting the legacy branch would make this sentence assert something false of every
        // run recorded before harness 17, so both directions are pinned here.
        Assert.Contains("ordering **is** derivable here", rowReport);
        Assert.DoesNotContain("ordering is not recorded", rowReport);

        var legacy = ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 80);
        legacy.ToolCallSummary = "wiki_search×5";
        var legacyReport = BenchmarkReportBuilder.BuildMarkdownReport(HarnessV6Run(legacy));

        Assert.Contains("ordering is not recorded", legacyReport);
        Assert.DoesNotContain("ordering **is** derivable here", legacyReport);
    }

    // -------------------------------------------------------------------------------------
    // P50 as the true statistical median, not Percentile's nearest-rank pick. Both read the
    // "Model Time Percentiles" line, which requires at least one answer to carry a (possibly
    // zero) ToolTimeMs so the block renders at all.
    // -------------------------------------------------------------------------------------

    [Fact]
    public void MedianModelTime_EvenCount_IsTheMeanOfTheTwoMiddleValues()
    {
        var a1 = ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 80);
        a1.DurationMs = 19406;
        a1.ToolTimeMs = 0;
        var a2 = ScoredAnswer(2, BenchmarkDifficulty.Simple, 25, 80);
        a2.DurationMs = 25159;
        a2.ToolTimeMs = 0;

        var report = BenchmarkReportBuilder.BuildMarkdownReport(HarnessV7Run(BenchmarkSecondOpinionMode.Off, a1, a2));

        // (19406 + 25159) / 2 = 22282.5, rounded away from zero to 22,283 — not 19,406, which is
        // what Percentile's nearest-rank pick would have selected for this pair.
        Assert.Contains("Median (P50) = 22,283 ms", report);
    }

    [Fact]
    public void MedianModelTime_OddCount_IsStillTheMiddleValue()
    {
        var a1 = ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 80);
        a1.DurationMs = 1000;
        a1.ToolTimeMs = 0;
        var a2 = ScoredAnswer(2, BenchmarkDifficulty.Simple, 25, 80);
        a2.DurationMs = 2000;
        a2.ToolTimeMs = 0;
        var a3 = ScoredAnswer(3, BenchmarkDifficulty.Simple, 25, 80);
        a3.DurationMs = 3000;
        a3.ToolTimeMs = 0;

        var report = BenchmarkReportBuilder.BuildMarkdownReport(HarnessV7Run(BenchmarkSecondOpinionMode.Off, a1, a2, a3));

        Assert.Contains("Median (P50) = 2,000 ms", report);
    }

    // -------------------------------------------------------------------------------------
    // The Args column on the per-question ordered tool-call table.
    // -------------------------------------------------------------------------------------

    [Fact]
    public void PerQuestionCallTable_ArgsColumn_CollapsesToOneLineAndEscapesPipes()
    {
        var answer = ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 80);
        answer.ToolCalls = new List<BenchmarkRunAnswerToolCall>
        {
            new BenchmarkRunAnswerToolCall
            {
                SortOrder = 0,
                Name = "wiki_search",
                Status = "completed",
                ArgsText = "line1|middle\nline2"
            }
        };

        var report = BenchmarkReportBuilder.BuildMarkdownReport(HarnessV6Run(answer));

        // The newline is collapsed away and the pipe is escaped, so the whole preview survives
        // as one contiguous, table-safe run of text.
        Assert.Contains("line1\\|middle line2", report);
    }

    [Fact]
    public void PerQuestionCallTable_ArgsColumn_RendersPrunedMarker_WhenArgsTextIsNullButResultWasNot()
    {
        var answer = ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 80);
        answer.ToolCalls = new List<BenchmarkRunAnswerToolCall>
        {
            new BenchmarkRunAnswerToolCall
            {
                SortOrder = 0,
                Name = "wiki_search",
                Status = "completed",
                ArgsText = null,
                ResultLengthChars = 42
            }
        };

        var report = BenchmarkReportBuilder.BuildMarkdownReport(HarnessV6Run(answer));

        Assert.Contains("(pruned)", report);
    }

    // -------------------------------------------------------------------------------------
    // Diagnostics read from the stored rows by BenchmarkToolResultClassifier: the two Tool
    // Usage Profile lines and the per-question Note column.
    // -------------------------------------------------------------------------------------

    private static List<BenchmarkRunAnswerToolCall> DiagnosticToolCalls()
    {
        const string capped = "src/zap.c:1: x... [Truncated: showing 20 of 90 characters. Narrow the query, or ask for a specific section, to see the rest.]";
        const string recordCut = "abc... [Record truncated: stored 3 of 50 characters]";
        return new List<BenchmarkRunAnswerToolCall>
        {
            new BenchmarkRunAnswerToolCall { SortOrder = 0, Name = "wiki_search", Status = "completed", Result = "No GnollHack wiki article matched 'grail'.", ResultLengthChars = 42 },
            new BenchmarkRunAnswerToolCall { SortOrder = 1, Name = "source_code_search", Status = "completed", Result = capped, ResultLengthChars = capped.Length },
            new BenchmarkRunAnswerToolCall { SortOrder = 2, Name = "wiki_search", Status = "completed", Result = null, ResultLengthChars = 4096 },
            new BenchmarkRunAnswerToolCall { SortOrder = 3, Name = "wiki_view", Status = "completed", Result = recordCut, ResultLengthChars = 50, ResultTruncated = true },
            new BenchmarkRunAnswerToolCall { SortOrder = 4, Name = "wiki_view", Status = "completed", Result = "The Holy Grail heals.", ResultLengthChars = 21 },
            new BenchmarkRunAnswerToolCall { SortOrder = 5, Name = "get_knowledge_article", Status = "error", Error = "Article not found for topic 'grail'. Available topics: artifacts" }
        };
    }

    [Fact]
    public void ToolUsageProfile_RowCarryingRun_PrintsNotFoundAndCutLines()
    {
        var answer = ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 80);
        answer.ToolCalls = DiagnosticToolCalls();

        var report = BenchmarkReportBuilder.BuildMarkdownReport(HarnessV6Run(answer));

        // Four inspectable successes (the pruned payload is neither a hit nor a miss), one miss
        // among them, and the knowledge-article miss reported apart because it is a failed call.
        Assert.Contains(
            "- **Not-found results:** 1 of 4 inspectable successful payloads (`wiki_search` ×1); plus 1 failed call(s) whose error is a not-found (`get_knowledge_article` ×1); 1 payloads unavailable.",
            report);
        // The batch budget and the turn limit cut after a result is stored, so they are never
        // printed as a count: "not recorded" is not zero.
        Assert.Contains(
            "- **Cut before the model saw it:** 1 by the per-tool cap; batch budget and turn limit not recorded. **Cut in the stored record only:** 1.",
            report);
        Assert.DoesNotContain("batch budget 0", report);

        // The outcome split is unchanged beside them.
        Assert.Contains("**Tool Call Outcomes:** 5 succeeded, 1 failed, 0 refused by budget.", report);
    }

    [Fact]
    public void PerQuestionCallTable_NoteColumn_UsesTheFixedVocabulary()
    {
        var answer = ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 80);
        answer.ToolCalls = DiagnosticToolCalls();

        var report = BenchmarkReportBuilder.BuildMarkdownReport(HarnessV6Run(answer));

        Assert.Contains("| Round | Tool | Args | Status | Exec (ms) | Result Size | Note |", report);
        Assert.Contains("|------:|------|------|--------|----------:|------------:|------|", report);
        Assert.Contains("| 42 chars | miss |", report);
        Assert.Contains("| 4,096 chars | unavailable |", report);
        Assert.Contains("| 50 chars | record cut |", report);
        Assert.Contains("| 21 chars |  |", report);
        Assert.Contains("| — | miss |", report);
        Assert.Contains(" | cut |", report);
    }

    [Fact]
    public void AccuracyWithheldForPrecision_IsCountedOnItsOwnLine_AndNamedInTheHarnessNote()
    {
        // Run 53's Q9 shape: Accuracy withheld for missing precision on an answer with no
        // unverified claims.
        var q9 = ScoredAnswer(9, BenchmarkDifficulty.Advanced, 60, 70);
        q9.AccuracyLevel = 4;
        q9.CompletenessLevel = 6;
        q9.ConcisenessLevel = 6;
        q9.ReadabilityLevel = 6;
        q9.AssessmentEvidenceJson = EvidenceJson(
            "Accuracy is held at 4 rather than 6 because the answer lacks the source-level precision the rubric's formula implies.",
            "Matches rubric.");
        q9.AnswerFlags = (int)BenchmarkAnswerFlags.UnevidencedDeduction;

        var clean = ScoredAnswer(10, BenchmarkDifficulty.Simple, 20, 90);
        clean.AccuracyLevel = 5;
        clean.AssessmentEvidenceJson = EvidenceJson("The answer incorrectly gives the timeout as 50 turns; held at 5 rather than 6.");

        var report = BenchmarkReportBuilder.BuildMarkdownReport(HarnessV6Run(q9, clean));

        Assert.Contains("- **Accuracy Withheld for Precision:** 1 (Q9)", report);
        Assert.Contains("Accuracy to 4/6 while its stated evidence withholds the level for missing precision, nuance or depth rather than naming a statement that is wrong or imprecise", report);
    }

    [Fact]
    public void AccuracyWithheldForUnverifiedClaims_NamesTheDeductionWithoutNamingAHarnessVersion()
    {
        // The wording used to cite "scoring method v7" as if that were still the current one; the
        // rule is not version-specific, so the harness note no longer names one.
        var q1 = ScoredAnswer(1, BenchmarkDifficulty.Advanced, 60, 70);
        q1.AccuracyLevel = 4;
        q1.UnverifiedClaimCount = 1;
        q1.UnverifiedClaimsJson = JsonSerializer.Serialize(new[] { "The prayer timeout is 400 turns." });
        q1.AssessmentEvidenceJson = EvidenceJson(
            "Docked to 4 because the claim cannot be verified from the provided context.",
            "Matches rubric.");
        q1.AnswerFlags = (int)BenchmarkAnswerFlags.UnevidencedDeduction;

        var report = BenchmarkReportBuilder.BuildMarkdownReport(HarnessV6Run(q1));

        Assert.Contains("rests only on claims it could not verify, which the scoring method does not permit as an accuracy deduction", report);
        Assert.DoesNotContain("scoring method v7 does not permit", report);
    }

    [Fact]
    public void AccuracyWithheldForPrecision_IsAbsent_WhenNoAnswerMatches()
    {
        var answer = ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 80);
        answer.AccuracyLevel = 5;
        answer.AssessmentEvidenceJson = EvidenceJson("The answer lists Level as 40 when it is 25.");

        var report = BenchmarkReportBuilder.BuildMarkdownReport(HarnessV6Run(answer));

        Assert.DoesNotContain("**Accuracy Withheld for Precision:**", report);
    }

    // -------------------------------------------------------------------------------------
    // The second reader's comment bullet under Disputed Assessments.
    // -------------------------------------------------------------------------------------

    [Fact]
    public void DisputedAssessments_RendersSecondReaderComment_WhenStored()
    {
        var q1 = ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 54);
        q1.SecondOpinionQualityScore = 25;
        q1.SecondOpinionCriticalError = true;
        q1.SecondOpinionDisagreed = true;
        q1.SecondOpinionTrigger = "LowQualityScore";
        q1.SecondOpinionJson = "{\"comment\":\"The critical error quote is not actually false.\"}";

        var run = HarnessV7Run(BenchmarkSecondOpinionMode.Flagged, q1);
        BenchmarkRunFinalizer.Apply(run, new[] { q1 });

        var report = BenchmarkReportBuilder.BuildMarkdownReport(run);

        Assert.Contains("### Disputed Assessments", report);
        Assert.Contains("  - Second reader: The critical error quote is not actually false.", report);
    }

    // -------------------------------------------------------------------------------------
    // Contested critical errors: the quote the cap rested on, checked against the source and
    // supported. Advisory throughout — the line reports it, nothing recomputes an index.
    // -------------------------------------------------------------------------------------

    [Fact]
    public void RunIntegrity_NamesContestedCriticalErrors_AndKeepsThemAdvisory()
    {
        var q1 = ScoredAnswer(1, BenchmarkDifficulty.Advanced, 78, 25);
        q1.CriticalError = true;
        q1.CriticalErrorQuote = "Gnolls are immune to lycanthropy.";
        q1.AnswerFlags = (int)BenchmarkAnswerFlags.ContestedCriticalError;

        var run = HarnessV7Run(BenchmarkSecondOpinionMode.Off, q1);
        BenchmarkRunFinalizer.Apply(run, new[] { q1 });

        var report = BenchmarkReportBuilder.BuildMarkdownReport(run);

        Assert.Contains("**Contested Critical Errors:** 1 (question(s) Q1)", report);
        Assert.Contains("checked against the source code/wiki by the claim verifier and **supported** as a standalone sentence; the error may lie in its context, so read the verdict's basis before treating the critical error as overturned.", report);
        Assert.Contains("the cap stands and no index moved", report);

        // No second opinion was run, so nothing disputes the cap, and the line says nothing about it.
        Assert.DoesNotContain("second-reader disputes are counted on the Critical Errors line", report);

        // Counted in the advisory breakdown, and named in section 5 as changing no score.
        Assert.Contains("contested critical errors: 1", report);
        Assert.Contains("Contested critical error (advisory, changed no score)", report);

        // And carried into the synthesis caveat's third clause.
        Assert.Contains("and 1 contested critical error(s)", report);
    }

    [Fact]
    public void RunIntegrity_ContestedCriticalErrors_NotesWhenTheSecondReaderAlsoDisputedTheCap()
    {
        // The two counts can diverge: this line counts quotes the claim verifier supported, while
        // the second reader's dispute is counted on the separate Critical Errors line — the note
        // exists so a reader does not read one figure as covering the other.
        var q1 = ScoredAnswer(1, BenchmarkDifficulty.Advanced, 78, 25);
        q1.CriticalError = true;
        q1.CriticalErrorQuote = "Gnolls are immune to lycanthropy.";
        q1.AnswerFlags = (int)BenchmarkAnswerFlags.ContestedCriticalError;
        q1.SecondOpinionQualityScore = 80;
        q1.SecondOpinionCriticalError = false;

        var run = HarnessV7Run(BenchmarkSecondOpinionMode.All, q1);
        BenchmarkRunFinalizer.Apply(run, new[] { q1 });

        var report = BenchmarkReportBuilder.BuildMarkdownReport(run);

        Assert.Contains(
            "**Contested Critical Errors:** 1 (question(s) Q1) — the critical-error quote was checked against the source code/wiki by the claim verifier and **supported** as a standalone sentence; the error may lie in its context, so read the verdict's basis before treating the critical error as overturned. Advisory: the cap stands and no index moved; re-assess from the run detail. — counts quotes the claim verifier supported; second-reader disputes are counted on the Critical Errors line.",
            report);
    }

    [Fact]
    public void RunIntegrity_OmitsTheContestedCriticalErrorLine_WhenNoAnswerCarriesTheFlag()
    {
        var q1 = ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 80);
        var run = HarnessV7Run(BenchmarkSecondOpinionMode.Off, q1);
        BenchmarkRunFinalizer.Apply(run, new[] { q1 });

        var report = BenchmarkReportBuilder.BuildMarkdownReport(run);

        Assert.DoesNotContain("**Contested Critical Errors:**", report);
        Assert.Contains("contested critical errors: 0", report);
    }

    // -------------------------------------------------------------------------------------
    // Contested accuracy deductions: the own-knowledge statement an out-of-rubric Accuracy
    // deduction rested on, checked against the source and refuted. Advisory throughout, and
    // "not recorded" rather than zero on a run before harness 20.
    // -------------------------------------------------------------------------------------

    private const string RefutedBasis = "The prayer timeout reset is independent of experience level.";

    private static BenchmarkRunAnswer ContestedDeductionAnswer(int orderIndex)
    {
        var answer = ScoredAnswer(orderIndex, BenchmarkDifficulty.Advanced, 78, 60);
        answer.AnswerFlags = (int)(BenchmarkAnswerFlags.OutOfRubricAccuracyDeduction | BenchmarkAnswerFlags.ContestedAccuracyDeduction);
        answer.AssessmentEvidenceJson = JsonSerializer.Serialize(new { accuracy = $"Level 4. Not in rubric: {RefutedBasis} Otherwise matches." });
        return answer;
    }

    [Fact]
    public void RunIntegrity_NamesContestedAccuracyDeductions_AndKeepsThemAdvisory()
    {
        var q1 = ContestedDeductionAnswer(1);
        var q2 = ScoredAnswer(2, BenchmarkDifficulty.Simple, 25, 80);
        var q3 = ContestedDeductionAnswer(3);

        var run = HarnessV7Run(BenchmarkSecondOpinionMode.Off, q1, q2, q3);
        run.HarnessVersion = "20";
        BenchmarkRunFinalizer.Apply(run, new[] { q1, q2, q3 });

        var report = BenchmarkReportBuilder.BuildMarkdownReport(run);

        // No verification items are stored, so neither cause can be read from roles.
        Assert.Contains("**Contested Accuracy Deductions:** 2 — cause not recorded: Q1, Q3.", report);
        Assert.Contains("the own-knowledge statement an out-of-rubric Accuracy deduction rests on was **refuted**, or a sentence the assessor quoted as false was **supported**", report);
        Assert.Contains("the deduction stands and no index moved", report);

        Assert.Contains("contested accuracy deductions: 2", report);
        Assert.Contains("Contested accuracy deduction (advisory, changed no score) (cause not recorded)", report);

        // Carried into the synthesis caveat beside refuted claims and disputed verdicts.
        Assert.Contains("0 refuted claim(s), 0 disputed verdict(s) and 2 contested accuracy deduction(s)", report);
    }

    [Fact]
    public void RunIntegrity_PrintsAZeroContestedAccuracyDeductionCount_OnAHarness20Run()
    {
        var q1 = ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 80);
        var run = HarnessV7Run(BenchmarkSecondOpinionMode.Off, q1);
        run.HarnessVersion = "20";
        BenchmarkRunFinalizer.Apply(run, new[] { q1 });

        var report = BenchmarkReportBuilder.BuildMarkdownReport(run);

        Assert.DoesNotContain("**Contested Accuracy Deductions:**", report);
        Assert.Contains("contested accuracy deductions: 0", report);
    }

    [Fact]
    public void RunIntegrity_PrintsTheContestedAccuracyDeductionCountAsNotRecorded_BeforeHarness20()
    {
        var q1 = ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 80);
        var run = HarnessV7Run(BenchmarkSecondOpinionMode.Off, q1);
        run.HarnessVersion = "19";
        BenchmarkRunFinalizer.Apply(run, new[] { q1 });

        var report = BenchmarkReportBuilder.BuildMarkdownReport(run);

        Assert.Null(run.ContestedAccuracyDeductionAnswerCount);
        Assert.Contains("contested accuracy deductions: not recorded", report);
        Assert.DoesNotContain("contested accuracy deductions: 0", report);
        Assert.DoesNotContain("**Contested Accuracy Deductions:**", report);
    }

    [Fact]
    public void RefutedClaims_ListsTheAnswersOwnClaims_AndNotTheRefutedOutOfRubricBasis()
    {
        const string candidateClaim = "Gnolls regenerate 3 HP per turn.";
        var q1 = ContestedDeductionAnswer(1);
        q1.ClaimsRefutedCount = 1;
        q1.ClaimsSupportedCount = 0;
        q1.ClaimsIndeterminateCount = 0;
        q1.ClaimVerificationJson = JsonSerializer.Serialize(new[]
        {
            new BenchmarkClaimVerification(0, RefutedBasis, BenchmarkClaimVerdict.Refuted, "src/pray.c:1020", "Timeout depends on level."),
            new BenchmarkClaimVerification(1, candidateClaim, BenchmarkClaimVerdict.Refuted, "src/regen.c:40", "Regeneration is 1 HP.")
        });

        var run = HarnessV7Run(BenchmarkSecondOpinionMode.Off, q1);
        run.HarnessVersion = "20";
        BenchmarkRunFinalizer.Apply(run, new[] { q1 });

        var report = BenchmarkReportBuilder.BuildMarkdownReport(run);

        Assert.Contains("#### Refuted Claims", report);
        Assert.Contains($"- **Q1:** \"{candidateClaim}\"", report);
        Assert.DoesNotContain($"- **Q1:** \"{RefutedBasis}\"", report);
    }

    // -------------------------------------------------------------------------------------
    // Tool rounds, per question and run-wide. Both are derived from the per-call rows harness
    // 17 introduced, so both are absent — never zero — on a run that carries none.
    // -------------------------------------------------------------------------------------

    [Fact]
    public void ToolRounds_ReportedPerQuestionAndRunWide_WhenCallRowsExist()
    {
        var answer = ScoredAnswer(1, BenchmarkDifficulty.Intermediate, 50, 80);
        answer.ToolCallCount = 4;
        answer.ToolCallBudgetUsed = 25;
        answer.ToolCalls = new List<BenchmarkRunAnswerToolCall>
        {
            new BenchmarkRunAnswerToolCall { SortOrder = 0, IterationIndex = 0, Name = "wiki_search", Status = "completed" },
            new BenchmarkRunAnswerToolCall { SortOrder = 1, IterationIndex = 0, Name = "wiki_search", Status = "completed" },
            new BenchmarkRunAnswerToolCall { SortOrder = 2, IterationIndex = 1, Name = "source_code_search", Status = "completed" },
            new BenchmarkRunAnswerToolCall { SortOrder = 3, IterationIndex = 1, Name = "source_code_search", Status = "completed" }
        };

        var report = BenchmarkReportBuilder.BuildMarkdownReport(HarnessV6Run(answer));

        // Four attempted calls across two distinct rounds.
        Assert.Contains("tool rounds: 2 (2.0 calls/round)", report);
        Assert.Contains("- **Tool Rounds:** mean 2.0 per answered question with tool calls; mean calls per round 2.0; max 2 on Q1.", report);
    }

    [Fact]
    public void ToolRounds_AreAbsent_OnARunCarryingNoCallRows()
    {
        var answer = ScoredAnswer(1, BenchmarkDifficulty.Intermediate, 50, 80);
        answer.ToolCallCount = 4;
        answer.ToolCallBudgetUsed = 25;
        answer.ToolCallSummary = "wiki_search×4";
        // ToolCalls stays empty: the shape of every run recorded before harness 17, where an
        // omitted figure is the only honest one.

        var report = BenchmarkReportBuilder.BuildMarkdownReport(HarnessV6Run(answer));

        Assert.DoesNotContain("tool rounds:", report);
        Assert.DoesNotContain("**Tool Rounds:**", report);

        // The rest of the budget line is unaffected.
        Assert.Contains("**Tool Budget:** 4 executed, budget 25", report);
    }

    [Fact]
    public void DisputedAssessments_OmitsSecondReaderComment_WhenSecondOpinionJsonIsMalformed()
    {
        var q1 = ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 54);
        q1.SecondOpinionQualityScore = 25;
        q1.SecondOpinionCriticalError = true;
        q1.SecondOpinionDisagreed = true;
        q1.SecondOpinionTrigger = "LowQualityScore";
        q1.SecondOpinionJson = "not json";

        var run = HarnessV7Run(BenchmarkSecondOpinionMode.Flagged, q1);
        BenchmarkRunFinalizer.Apply(run, new[] { q1 });

        // Must not throw despite the malformed blob.
        var report = BenchmarkReportBuilder.BuildMarkdownReport(run);

        Assert.Contains("### Disputed Assessments", report);
        Assert.DoesNotContain("Second reader:", report);
    }

    [Fact]
    public void DisputedAssessments_CarriesAccusedSentenceCounts_WhenTheAnswerHasAccusedSentences()
    {
        string[] accused = { BenchmarkClaimRoles.AccusedQuote };
        var q1 = ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 54);
        q1.SecondOpinionQualityScore = 25;
        q1.SecondOpinionCriticalError = true;
        q1.SecondOpinionDisagreed = true;
        q1.SecondOpinionTrigger = "LowQualityScore";
        q1.ClaimsSupportedCount = 0;
        q1.ClaimsRefutedCount = 0;
        q1.ClaimsIndeterminateCount = 0;
        q1.ClaimVerificationJson = JsonSerializer.Serialize(new[]
        {
            new BenchmarkClaimVerification(0, "The Grail has three charges.", BenchmarkClaimVerdict.Supported, "src/artifact.c:120", "True.") { Roles = accused },
            new BenchmarkClaimVerification(1, "The Grail cures lycanthropy.", BenchmarkClaimVerdict.Refuted, "src/artifact.c:140", "False.") { Roles = accused }
        });

        var run = HarnessV7Run(BenchmarkSecondOpinionMode.Flagged, q1);
        BenchmarkRunFinalizer.Apply(run, new[] { q1 });

        var report = BenchmarkReportBuilder.BuildMarkdownReport(run);

        Assert.Contains(
            "[Claims: 0 supported, 0 refuted, 0 indeterminate; accused sentences: 1 supported, 1 refuted, 0 indeterminate]",
            report);
    }

    [Fact]
    public void DisputedAssessments_CarriesNoAccusedSentenceCounts_WhenTheAnswerHasNone()
    {
        var q1 = ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 54);
        q1.SecondOpinionQualityScore = 25;
        q1.SecondOpinionCriticalError = true;
        q1.SecondOpinionDisagreed = true;
        q1.SecondOpinionTrigger = "LowQualityScore";
        q1.ClaimsSupportedCount = 1;
        q1.ClaimsRefutedCount = 0;
        q1.ClaimsIndeterminateCount = 0;

        var run = HarnessV7Run(BenchmarkSecondOpinionMode.Flagged, q1);
        BenchmarkRunFinalizer.Apply(run, new[] { q1 });

        var report = BenchmarkReportBuilder.BuildMarkdownReport(run);

        Assert.Contains("[Claims: 1 supported, 0 refuted, 0 indeterminate]", report);
        Assert.DoesNotContain("accused sentences:", report);
    }

    // --- Terminal provider failures withhold the indices (harness version 21) ---

    private static BenchmarkRunAnswer TerminalFailureAnswer(int orderIndex, BenchmarkAnswerStatus status, int? httpStatusCode, string errorMessage)
    {
        return new BenchmarkRunAnswer
        {
            OrderIndex = orderIndex,
            QuestionText = $"Q{orderIndex}",
            AnswerText = string.Empty,
            Difficulty = BenchmarkDifficulty.Simple,
            AssessedDifficulty = 25,
            Status = status,
            AssessmentStatus = BenchmarkAssessmentStatus.Failed,
            AssessmentError = "Not assessed: the provider failed the request; excluded from scoring.",
            HttpStatusCode = httpStatusCode,
            ErrorMessage = errorMessage
        };
    }

    [Fact]
    public void BuildMarkdownReport_ShowsNotComputedHeadline_WhenATerminalFailureWithheldTheIndices()
    {
        var ok = ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 90);
        ok.SpeedScore = 80;
        var failed = TerminalFailureAnswer(2, BenchmarkAnswerStatus.ProviderError, 503, "Our servers are currently overloaded.");

        var run = new BenchmarkRun
        {
            Id = 60,
            SuiteName = "Terminal Failure Suite",
            TestedModelSnapshot = BenchmarkModelSnapshots.Model(displayName: "Model X"),
            AssessorModelSnapshot = BenchmarkModelSnapshots.Model(displayName: "Assessor Y"),
            Status = BenchmarkRunStatus.CompletedWithErrors,
            StartedAtUtc = DateTime.UtcNow.AddMinutes(-5),
            CompletedAtUtc = DateTime.UtcNow,
            HarnessVersion = "21",
            // Left null on purpose: this run was never finalized in this test, so the report must
            // fall back to counting HasTerminalFailure over the answers rather than reading zero.
            QualityIndex = null,
            SpeedIndex = null,
            TotalQuestionCount = 2,
            Answers = new List<BenchmarkRunAnswer> { ok, failed }
        };

        var report = BenchmarkReportBuilder.BuildMarkdownReport(run);

        Assert.Contains("### **Intelligence Index: Not computed — 1 of 2 question(s) failed at the provider; see § 5**", report);
        Assert.Contains("# **Intelligence Index: Not computed — 1 of 2 question(s) failed at the provider; see § 5**", report);
        Assert.DoesNotContain("Not Scored", report);
    }

    [Fact]
    public void BuildMarkdownReport_UsesTheStoredTerminalFailureCount_WhenTheRunWasFinalized()
    {
        var ok = ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 90);
        var failed = TerminalFailureAnswer(2, BenchmarkAnswerStatus.Failed, null, "Timed out.");

        var run = new BenchmarkRun
        {
            Id = 61,
            SuiteName = "Terminal Failure Suite",
            TestedModelSnapshot = BenchmarkModelSnapshots.Model(displayName: "Model X"),
            AssessorModelSnapshot = BenchmarkModelSnapshots.Model(displayName: "Assessor Y"),
            Status = BenchmarkRunStatus.CompletedWithErrors,
            StartedAtUtc = DateTime.UtcNow.AddMinutes(-5),
            CompletedAtUtc = DateTime.UtcNow,
            HarnessVersion = "21",
            TotalQuestionCount = 2,
            Answers = new List<BenchmarkRunAnswer> { ok, failed }
        };
        BenchmarkRunFinalizer.Apply(run, run.Answers);

        Assert.Null(run.QualityIndex);
        Assert.Null(run.SpeedIndex);
        Assert.Equal(1, run.TerminalFailureAnswerCount);

        var report = BenchmarkReportBuilder.BuildMarkdownReport(run);

        Assert.Contains("Not computed — 1 of 2 question(s) failed at the provider; see § 5", report);
    }

    [Fact]
    public void BuildMarkdownReport_ProviderErrorsCount_IncludesBothProviderErrorAndFailed()
    {
        var ok = ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 90);
        var providerError = TerminalFailureAnswer(2, BenchmarkAnswerStatus.ProviderError, 503, "overloaded");
        var failed = TerminalFailureAnswer(3, BenchmarkAnswerStatus.Failed, null, "timed out");

        var run = new BenchmarkRun
        {
            Id = 62,
            SuiteName = "Terminal Failure Suite",
            TestedModelSnapshot = BenchmarkModelSnapshots.Model(displayName: "Model X"),
            AssessorModelSnapshot = BenchmarkModelSnapshots.Model(displayName: "Assessor Y"),
            Status = BenchmarkRunStatus.CompletedWithErrors,
            StartedAtUtc = DateTime.UtcNow.AddMinutes(-5),
            CompletedAtUtc = DateTime.UtcNow,
            HarnessVersion = "21",
            TotalQuestionCount = 3,
            Answers = new List<BenchmarkRunAnswer> { ok, providerError, failed }
        };

        var report = BenchmarkReportBuilder.BuildMarkdownReport(run);

        Assert.Contains("**Provider Errors:** 2", report);
        Assert.Contains("**Terminal provider failures:** 2", report);
    }

    [Fact]
    public void BuildMarkdownReport_IssuesSection_PrintsFullErrorMessage_AndOmitsHttpParenWhenStatusIsNull()
    {
        var providerError = TerminalFailureAnswer(1, BenchmarkAnswerStatus.ProviderError, 503, "Our servers are currently overloaded.");
        var failed = TerminalFailureAnswer(2, BenchmarkAnswerStatus.Failed, null, "Per-question timeout exceeded (60 s).");

        var run = new BenchmarkRun
        {
            Id = 63,
            SuiteName = "Terminal Failure Suite",
            TestedModelSnapshot = BenchmarkModelSnapshots.Model(displayName: "Model X"),
            AssessorModelSnapshot = BenchmarkModelSnapshots.Model(displayName: "Assessor Y"),
            Status = BenchmarkRunStatus.CompletedWithErrors,
            StartedAtUtc = DateTime.UtcNow.AddMinutes(-5),
            CompletedAtUtc = DateTime.UtcNow,
            HarnessVersion = "21",
            TotalQuestionCount = 2,
            Answers = new List<BenchmarkRunAnswer> { providerError, failed }
        };

        var report = BenchmarkReportBuilder.BuildMarkdownReport(run);

        Assert.Contains("Provider error (HTTP 503): Our servers are currently overloaded.", report);
        Assert.Contains("Failed: Per-question timeout exceeded (60 s).", report);
        // No "(HTTP )" placeholder when the status code was never recorded.
        Assert.DoesNotContain("Failed (HTTP ", report);
        Assert.DoesNotContain("Failed (HTTP):", report);
    }

    [Fact]
    public void BuildMarkdownReport_IssuesSection_ListsACanceledAnswer_WithNoHttpSuffix_AndExcludesItFromScoring()
    {
        var ok = ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 90);
        var canceled = TerminalFailureAnswer(
            2, BenchmarkAnswerStatus.Canceled, null, "Canceled by the operator before the answer completed.");

        var run = new BenchmarkRun
        {
            Id = 64,
            SuiteName = "Canceled Answer Suite",
            TestedModelSnapshot = BenchmarkModelSnapshots.Model(displayName: "Model X"),
            AssessorModelSnapshot = BenchmarkModelSnapshots.Model(displayName: "Assessor Y"),
            Status = BenchmarkRunStatus.CompletedWithErrors,
            StartedAtUtc = DateTime.UtcNow.AddMinutes(-5),
            CompletedAtUtc = DateTime.UtcNow,
            HarnessVersion = "21",
            TotalQuestionCount = 2,
            Answers = new List<BenchmarkRunAnswer> { ok, canceled }
        };

        var report = BenchmarkReportBuilder.BuildMarkdownReport(run);

        Assert.Contains("Canceled: Canceled by the operator before the answer completed.", report);
        // Unlike Provider error and Failed, the Canceled line never carries an HTTP suffix.
        Assert.DoesNotContain("Canceled (HTTP", report);
        Assert.Contains("*(Note: Excluded from scoring)*", report);
    }

    // -------------------------------------------------------------------------------------
    // Harness 32: the BOARD FACTS quote check in the manifest, contested deductions split by
    // cause, accused sentences with their verdicts, and the § 2 order.
    // -------------------------------------------------------------------------------------

    private static Overseer.Models.BoardFactIssueDto BoardFactIssue(int orderIndex, string? literal = null)
        => new() { QuestionId = orderIndex * 10, OrderIndex = orderIndex, Literal = literal, LineExcerpt = "excerpt" };

    [Fact]
    public void BoardFactsManifest_NoneMissing_WithUnquotedLinesCountedPerQuestion()
    {
        var check = new Overseer.Models.BoardFactsCheckDto
        {
            BulletCount = 84,
            CheckedLiteralCount = 81,
            UnquotedBulletCount = 3,
            UnquotedBullets = { BoardFactIssue(17), BoardFactIssue(5), BoardFactIssue(17) }
        };

        Assert.Equal(
            "- **Rubric board quotes:** 81 checked, none missing. 3 BOARD FACTS lines carry no quoted literal and were not checked (Q5 ×1, Q17 ×2).",
            BenchmarkReportBuilder.BoardFactsManifestLine(check));
    }

    [Fact]
    public void BoardFactsManifest_MissingLiterals_AreNamedPerQuestion()
    {
        var check = new Overseer.Models.BoardFactsCheckDto
        {
            BulletCount = 84,
            CheckedLiteralCount = 81,
            MissingLiterals = { BoardFactIssue(6, "T - the Holy Grail (0 charges, 0 rechargings)"), BoardFactIssue(1, "HP:15(15)") }
        };

        Assert.Equal(
            "- **Rubric board quotes:** 81 checked, 2 missing (Q1 ×1, Q6 ×1) — these rubrics quote text this board does not contain; grades on them rest on stale facts.",
            BenchmarkReportBuilder.BoardFactsManifestLine(check));
    }

    [Fact]
    public void BoardFactsManifest_OneUnquotedLine_IsSingular()
    {
        var check = new Overseer.Models.BoardFactsCheckDto
        {
            CheckedLiteralCount = 4,
            UnquotedBulletCount = 1,
            UnquotedBullets = { BoardFactIssue(2) }
        };

        Assert.Equal(
            "- **Rubric board quotes:** 4 checked, none missing. 1 BOARD FACTS line carries no quoted literal and was not checked (Q2 ×1).",
            BenchmarkReportBuilder.BoardFactsManifestLine(check));
    }

    [Fact]
    public void BoardFactsManifest_PrintedUnderTheGameSnapshotLine_AndNothingForARunWithoutTheColumn()
    {
        var legacy = Harness30BoardRun(BoardGradedAnswer(1, 80));
        legacy.GameSnapshotNameUsed = "tommi2";
        Assert.DoesNotContain("Rubric board quotes", BenchmarkReportBuilder.BuildMarkdownReport(legacy));

        var run = Harness30BoardRun(BoardGradedAnswer(1, 80));
        run.HarnessVersion = "32";
        run.GameSnapshotNameUsed = "tommi2";
        run.BoardFactsCheckJson = BenchmarkBoardFactsChecker.Serialize(new Overseer.Models.BoardFactsCheckDto
        {
            BulletCount = 2,
            CheckedLiteralCount = 2,
            MissingLiterals = { BoardFactIssue(1, "T - the Holy Grail (0 charges, 0 rechargings)") }
        });

        string report = BenchmarkReportBuilder.BuildMarkdownReport(run);

        int snapshotLine = report.IndexOf("- **Game Snapshot:** tommi2", StringComparison.Ordinal);
        int quotesLine = report.IndexOf("- **Rubric board quotes:** 2 checked, 1 missing (Q1 ×1) — these rubrics quote text this board does not contain; grades on them rest on stale facts.", StringComparison.Ordinal);
        int totalQuestionsLine = report.IndexOf("- **Total Questions:**", StringComparison.Ordinal);
        Assert.True(snapshotLine >= 0, "the Game Snapshot line is printed");
        Assert.True(quotesLine > snapshotLine, "the quote check follows the Game Snapshot line");
        Assert.True(totalQuestionsLine > quotesLine, "the quote check stays in the manifest");
    }

    // -------------------------------------------------------------------------------------
    // Harness 33: each missing board quote named, the board's format, re-run provenance.
    // -------------------------------------------------------------------------------------

    [Fact]
    public void BoardFactsManifest_EachMissingLiteralIsListedUnderTheLine()
    {
        var run = Harness30BoardRun(BoardGradedAnswer(1, 80));
        run.HarnessVersion = "33";
        run.GameSnapshotNameUsed = "tommi2";
        run.BoardFactsCheckJson = BenchmarkBoardFactsChecker.Serialize(new Overseer.Models.BoardFactsCheckDto
        {
            BulletCount = 3,
            CheckedLiteralCount = 3,
            MissingLiterals = { BoardFactIssue(6, "T - the Holy Grail (0 charges, 0 rechargings)"), BoardFactIssue(1, "HP:15(15)") }
        });

        string report = BenchmarkReportBuilder.BuildMarkdownReport(run);

        string expected = string.Join(Environment.NewLine,
            "- **Rubric board quotes:** 3 checked, 2 missing (Q1 ×1, Q6 ×1) — these rubrics quote text this board does not contain; grades on them rest on stale facts.",
            "  - Q1: \"HP:15(15)\"",
            "  - Q6: \"T - the Holy Grail (0 charges, 0 rechargings)\"",
            "- **Total Questions:**");
        Assert.Contains(expected, report);
    }

    [Fact]
    public void BoardFactsMissingLiteralLines_AreCappedWithACountOfTheRest()
    {
        var check = new Overseer.Models.BoardFactsCheckDto();
        for (int i = 1; i <= 23; i++)
        {
            check.MissingLiterals.Add(BoardFactIssue(i, $"quote {i}"));
        }

        var lines = BenchmarkReportBuilder.BoardFactsMissingLiteralLines(check);

        Assert.Equal(21, lines.Count);
        Assert.Equal("  - Q20: \"quote 20\"", lines[19]);
        Assert.Equal("  - and 3 more", lines[20]);
        Assert.Empty(BenchmarkReportBuilder.BoardFactsMissingLiteralLines(new Overseer.Models.BoardFactsCheckDto()));
        Assert.Empty(BenchmarkReportBuilder.BoardFactsMissingLiteralLines(null));
    }

    [Fact]
    public void GameSnapshotLine_StatesTheFormatFromHarness33Only()
    {
        var formatted = Harness30BoardRun(BoardGradedAnswer(1, 80));
        formatted.HarnessVersion = "33";
        formatted.GameSnapshotNameUsed = "tommi2";
        formatted.GameSnapshotFormatVersionUsed = 3;
        Assert.Contains("SHA-256 8f8c4778d449, format 3)", BenchmarkReportBuilder.BuildMarkdownReport(formatted));

        var unstated = Harness30BoardRun(BoardGradedAnswer(1, 80));
        unstated.HarnessVersion = "33";
        unstated.GameSnapshotNameUsed = "tommi2";
        Assert.Contains("SHA-256 8f8c4778d449, format not stated)", BenchmarkReportBuilder.BuildMarkdownReport(unstated));

        var legacy = Harness30BoardRun(BoardGradedAnswer(1, 80));
        legacy.HarnessVersion = "32";
        legacy.GameSnapshotNameUsed = "tommi2";
        string legacyReport = BenchmarkReportBuilder.BuildMarkdownReport(legacy);
        Assert.Contains("SHA-256 8f8c4778d449)", legacyReport);
        Assert.DoesNotContain(", format", legacyReport);
    }

    [Fact]
    public void Delivery_Harness33_KeepsThePreRunStampAndAddsTheReRunStamp()
    {
        var run = Harness30BoardRun(BoardGradedAnswer(1, 80));
        run.HarnessVersion = "33";
        run.CandidateDeliveryVerifiedAtUtc = new DateTime(2026, 9, 18, 21, 1, 30, DateTimeKind.Utc);
        run.RerunStartedAtUtc = new DateTime(2026, 9, 18, 21, 20, 30, DateTimeKind.Utc);
        run.RerunCandidateDeliveryVerifiedAtUtc = new DateTime(2026, 9, 18, 21, 20, 41, DateTimeKind.Utc);

        Assert.Contains(
            "**Delivery:** prompt and board delivery verified against the provider request body before the first question (2026-09-18 21:01:30 UTC); re-verified before the re-run (2026-09-18 21:20:41 UTC).",
            BenchmarkReportBuilder.BuildMarkdownReport(run));
    }

    [Fact]
    public void Delivery_BeforeHarness33_AStampTakenAfterTheReRunBeganIsNamedAsTheReRuns()
    {
        var run = Harness30BoardRun(BoardGradedAnswer(1, 80));
        run.HarnessVersion = "32";
        run.RerunStartedAtUtc = new DateTime(2026, 9, 18, 21, 20, 30, DateTimeKind.Utc);
        run.CandidateDeliveryVerifiedAtUtc = new DateTime(2026, 9, 18, 21, 20, 41, DateTimeKind.Utc);

        string report = BenchmarkReportBuilder.BuildMarkdownReport(run);

        Assert.Contains(
            "**Delivery:** prompt and board delivery verified against the provider request body before the re-run (2026-09-18 21:20:41 UTC); the pre-run probe's stamp was overwritten by the re-run, as on every run before harness 33.",
            report);
        Assert.DoesNotContain("before the first question", report);
    }

    [Fact]
    public void ReExecutedAnswer_NamesWhatItReplaced_InTheRerunBlockAndUnderItsQuestion()
    {
        var replaced = BoardGradedAnswer(15, 80);
        replaced.RerunAtUtc = new DateTime(2026, 9, 18, 21, 21, 5, DateTimeKind.Utc);
        replaced.RerunOfStatus = BenchmarkAnswerStatus.ProviderError;
        replaced.RerunOfErrorMessage = "Stream ended\r\nwithout a final message.";
        var run = Harness30BoardRun(BoardGradedAnswer(1, 80), replaced);
        run.HarnessVersion = "33";
        run.RerunStartedAtUtc = new DateTime(2026, 9, 18, 21, 20, 30, DateTimeKind.Utc);
        run.RerunCompletedAtUtc = new DateTime(2026, 9, 18, 21, 22, 42, DateTimeKind.Utc);

        string report = BenchmarkReportBuilder.BuildMarkdownReport(run);

        Assert.Contains("> - Q15 re-executed: was ProviderError — Stream ended without a final message.", report);
        Assert.Contains("> The replaced attempts' tool-call records are not kept.", report);
        Assert.Contains("- **Re-executed:** 2026-09-18 21:21:05 UTC; was ProviderError — Stream ended without a final message.", report);
        Assert.DoesNotContain("Q1 re-executed", report);
    }

    [Fact]
    public void ReExecutedAnswer_WithoutARecordedOriginalStatus_SaysSo()
    {
        var answer = BoardGradedAnswer(3, 80);
        answer.RerunAtUtc = new DateTime(2026, 9, 18, 21, 21, 5, DateTimeKind.Utc);

        Assert.Equal(
            new[] { "Q3 re-executed: replaced attempt not recorded" },
            BenchmarkReportBuilder.ReExecutedAnswerLines(new[] { answer }));
    }

    private const string BasisStatement = "The prayer timeout reset is independent of experience level.";

    private static BenchmarkClaimVerification RoleItem(int index, string claim, BenchmarkClaimVerdict verdict, string? citation, string role)
        => new(index, claim, verdict, citation, "Basis.") { Roles = new[] { role } };

    private static BenchmarkRunAnswer ContestedByCause(int orderIndex, params BenchmarkClaimVerification[] verifications)
    {
        var answer = BoardGradedAnswer(orderIndex, 70);
        answer.AnswerFlags = (int)BenchmarkAnswerFlags.ContestedAccuracyDeduction;
        answer.ClaimVerificationJson = JsonSerializer.Serialize(verifications);
        return answer;
    }

    [Fact]
    public void ContestedAccuracyDeductions_AreSplitByCause_ReadFromTheVerificationRoles()
    {
        var basisRefuted = ContestedByCause(1,
            RoleItem(0, BasisStatement, BenchmarkClaimVerdict.Refuted, "src/pray.c:1", BenchmarkClaimRoles.OutOfRubricBasis));
        var accusationSupported = ContestedByCause(2,
            RoleItem(0, "Charged but true.", BenchmarkClaimVerdict.Supported, "src/objects.c:2889", BenchmarkClaimRoles.AccusedQuote));
        var both = ContestedByCause(3,
            RoleItem(0, BasisStatement, BenchmarkClaimVerdict.Refuted, "src/pray.c:1", BenchmarkClaimRoles.OutOfRubricBasis),
            RoleItem(1, "Also charged but true.", BenchmarkClaimVerdict.Supported, "src/read.c:12", BenchmarkClaimRoles.AccusedQuote));
        var legacy = ContestedByCause(4,
            new BenchmarkClaimVerification(0, BasisStatement, BenchmarkClaimVerdict.Refuted, "src/pray.c:1", "False."));
        var clean = BoardGradedAnswer(5, 90);

        string report = BenchmarkReportBuilder.BuildMarkdownReport(
            Harness30BoardRun(basisRefuted, accusationSupported, both, legacy, clean));

        Assert.Contains(
            "- **Contested Accuracy Deductions:** 4 — own-knowledge basis refuted: Q1, Q3; a sentence the assessor quoted as false was supported: Q2, Q3; cause not recorded: Q4. The claim verifier checked these against the source code/wiki: either the own-knowledge statement an out-of-rubric Accuracy deduction rests on was **refuted**, or a sentence the assessor quoted as false was **supported**. Advisory: the deduction stands and no index moved; re-assess from the run detail.",
            report);

        // The per-answer Issues entry names its own cause.
        Assert.Contains("Contested accuracy deduction (advisory, changed no score) (own-knowledge basis refuted)", report);
        Assert.Contains("Contested accuracy deduction (advisory, changed no score) (a sentence the assessor quoted as false was supported)", report);
        Assert.Contains("Contested accuracy deduction (advisory, changed no score) (own-knowledge basis refuted and a sentence the assessor quoted as false was supported)", report);
        Assert.Contains("Contested accuracy deduction (advisory, changed no score) (cause not recorded)", report);
        Assert.DoesNotContain("Contested out-of-rubric accuracy deduction", report);

        // The Advisory Flags parenthetical is neutral already and unchanged.
        Assert.Contains("contested accuracy deductions: 4", report);
    }

    [Fact]
    public void AccusedSentences_EveryOneAppearsWithItsVerdict_PerAnswerAndAtRunLevel()
    {
        var q1 = BoardGradedAnswer(1, 60);
        q1.AccuracyLevel = 4;
        q1.ClaimsSupportedCount = 1;
        q1.ClaimVerificationJson = JsonSerializer.Serialize(new[]
        {
            RoleItem(0, "Own supported claim.", BenchmarkClaimVerdict.Supported, "src/own.c:1", BenchmarkClaimRoles.UnverifiedClaim),
            RoleItem(1, "Charged but true.", BenchmarkClaimVerdict.Supported, "src/objects.c:2889", BenchmarkClaimRoles.AccusedQuote),
            RoleItem(2, "Charged and false.", BenchmarkClaimVerdict.Refuted, "src/zap.c:9", BenchmarkClaimRoles.AccusedQuote),
            RoleItem(3, "Charged, unsettled.", BenchmarkClaimVerdict.Indeterminate, null, BenchmarkClaimRoles.AccusedQuote)
        });

        // A record stored before roles existed has no accused sentences to count.
        var legacy = BoardGradedAnswer(2, 80);
        legacy.ClaimVerificationJson = JsonSerializer.Serialize(new[]
        {
            new BenchmarkClaimVerification(0, "Legacy claim.", BenchmarkClaimVerdict.Supported, "src/legacy.c:1", "True.")
        });

        string report = BenchmarkReportBuilder.BuildMarkdownReport(Harness30BoardRun(q1, legacy));

        Assert.Contains("> - **Accused sentences checked:** 3 — supported 1, refuted 1, indeterminate 1", report);
        Assert.Single(Regex.Matches(report, Regex.Escape("> - **Accused sentences checked:**")));
        Assert.Contains("> - **Supported accusation:** a sentence the assessor charged as false was checked by the claim verifier and **supported** — \"Charged but true.\" (src/objects.c:2889). *Advisory; the deduction stands.*", report);
        Assert.Contains("> - **Accused sentence, refuted:** a sentence the assessor charged as false was checked by the claim verifier and returned **refuted** — \"Charged and false.\" (src/zap.c:9).", report);
        Assert.Contains("> - **Accused sentence, indeterminate:** a sentence the assessor charged as false was checked by the claim verifier and returned **indeterminate** — \"Charged, unsettled.\" (no citation).", report);

        Assert.Contains("- **Accused Sentences Checked:** 3 across 1 answer(s) (Q1) — supported 1, refuted 1, indeterminate 1.", report);
        Assert.Contains("- **Supported Accusations:** 1 (Q1)", report);
    }

    [Fact]
    public void ClaimVerificationYield_StatesUnverifiedClaimsAndAccusedSentencesApart()
    {
        var q1 = ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 80);
        q1.ClaimVerificationJson = JsonSerializer.Serialize(new[]
        {
            RoleItem(0, "Charged but true.", BenchmarkClaimVerdict.Supported, "src/objects.c:2889", BenchmarkClaimRoles.AccusedQuote),
            RoleItem(1, "Charged and false.", BenchmarkClaimVerdict.Refuted, "src/zap.c:9", BenchmarkClaimRoles.AccusedQuote)
        });
        var run = HarnessV7Run(BenchmarkSecondOpinionMode.Off, q1);
        run.TestedModelSnapshot = BenchmarkModelSnapshots.Model(provider: "OpenAI", modelId: "gpt-5.6", displayName: "GPT-5.6 Luna", thinkingLevel: "max");
        run.AssessorModelSnapshot = BenchmarkModelSnapshots.Model(provider: "Google", modelId: "gemini-3.7-flash", displayName: "Gemini 3.7 Flash");
        run.ClaimVerifierModelSnapshot = BenchmarkModelSnapshots.Model(modelId: "gpt-5-mini");
        run.TotalInputTokens = 200_000;
        run.TotalOutputTokens = 30_000;
        run.TotalAssessmentInputTokens = 100_000;
        run.TotalAssessmentOutputTokens = 10_000;
        run.TotalClaimVerificationInputTokens = 1_300_000;
        run.TotalClaimVerificationOutputTokens = 100_000;

        // The run's claim columns count the answers' own claims only.
        run.ClaimsSupportedCount = 7;
        run.ClaimsRefutedCount = 0;
        run.ClaimsIndeterminateCount = 1;

        var runPricing = new BenchmarkRunPricing(
            Candidate: new ModelPricing(2.50m, 10.00m, Source: ModelPricingSource.Catalog, AsOf: "2026-09-05"),
            Assessor: new ModelPricing(0.15m, 0.60m, Source: ModelPricingSource.Catalog, AsOf: "2026-09-05"),
            SecondOpinion: null,
            ClaimVerifier: new ModelPricing(1.00m, 4.00m, Source: ModelPricingSource.Custom),
            IsSnapshot: true
        );

        var report = BenchmarkReportBuilder.BuildMarkdownReport(run, runPricing: runPricing);

        Assert.Contains(
            "- **Claim Verification Yield:** 8 unverified claim(s) + 2 accused sentence(s) checked — claims: 7 supported, 0 refuted, 1 indeterminate; accused sentences: 1 supported, 1 refuted, 0 indeterminate. $1.70 ($0.17/item over both), 67% of run cost.",
            report);
    }

    // -------------------------------------------------------------------------------------
    // Harness 33 / scoring method 12: assessor statements apart, suspected-false claims,
    // citation-liveness notes, widened accused sentences.
    // -------------------------------------------------------------------------------------

    [Fact]
    public void AssessorStatements_AreCountedApart_AndARefutationIsNotARefutedClaimOfTheAnswer()
    {
        var q1 = ContestedByCause(1,
            RoleItem(0, "The repower time is randomized, about 100 turns.", BenchmarkClaimVerdict.Refuted, "include/artilist.h:335", BenchmarkClaimRoles.AssessorStatement),
            RoleItem(1, "The Grail heals when invoked.", BenchmarkClaimVerdict.Supported, "src/artifact.c:10", BenchmarkClaimRoles.AssessorStatement));
        q1.ClaimsRefutedCount = 0;

        string report = BenchmarkReportBuilder.BuildMarkdownReport(Harness30BoardRun(q1));

        Assert.Contains("> - **Assessor statements checked:** 2 — supported 1, refuted 1, indeterminate 0", report);
        Assert.Contains("> - **Assessor statement refuted:** a statement of the assessor's own evidence was checked by the claim verifier and **refuted** — \"The repower time is randomized, about 100 turns.\" (include/artilist.h:335).", report);
        Assert.Contains("- **Assessor Statements Checked:** 2 across 1 answer(s) (Q1) — supported 1, refuted 1, indeterminate 0.", report);
        Assert.Contains("- **Contested Accuracy Deductions:** 1 — a statement of the assessor's own evidence was refuted: Q1.", report);
        Assert.Contains("or a statement of the assessor's own accuracy evidence was **refuted**.", report);
        Assert.DoesNotContain("#### Refuted Claims", report);
    }

    [Fact]
    public void SuspectedFalseClaims_AreCountedWithTheAssessorRightOnARefutation()
    {
        var suspected = new BenchmarkClaimVerification(0, "Lizard corpses cure confusion.", BenchmarkClaimVerdict.Refuted, "src/eat.c:1670", "Basis.")
        {
            Roles = new[] { BenchmarkClaimRoles.UnverifiedClaim },
            SuspectedFalse = true,
            Suspicion = "they cure stoning",
            RecordedClaim = "Suspected false: Lizard corpses cure confusion. — they cure stoning"
        };
        var plain = RoleItem(1, "Own claim, supported.", BenchmarkClaimVerdict.Supported, "src/own.c:1", BenchmarkClaimRoles.UnverifiedClaim);
        var q1 = BoardGradedAnswer(1, 80);
        q1.UnverifiedClaimCount = 2;
        q1.UnverifiedClaimsJson = JsonSerializer.Serialize(new[] { suspected.RecordedClaim, plain.Claim });
        q1.ClaimsSupportedCount = 1;
        q1.ClaimsRefutedCount = 1;
        q1.ClaimVerificationJson = JsonSerializer.Serialize(new[] { suspected, plain });

        string report = BenchmarkReportBuilder.BuildMarkdownReport(Harness30BoardRun(q1));

        Assert.Contains("> - **Suspected false by the assessor:** 1 — refuted 1 (the verifier sided with the assessor), supported 0 (the verifier sided with the answer), indeterminate 0", report);
        Assert.Contains("- **Suspected False by the Assessor:** 1 across 1 answer(s) (Q1) — refuted 1 (the verifier sided with the assessor), supported 0 (the verifier sided with the answer), indeterminate 0.", report);
        Assert.Contains("- **Q1:** \"Lizard corpses cure confusion.\" *(suspected false by the assessor)*", report);
    }

    [Fact]
    public void SuspectedFalseClaims_AreCountedWithTheAssessorWrongOnASupport()
    {
        // The other side of the pair: a claim the assessor suspected false that the verifier
        // instead supported. "Supported" alone reads as a success; the annotation says whose.
        var suspected = new BenchmarkClaimVerification(0, "Fortune cookies are vegan.", BenchmarkClaimVerdict.Supported, "src/food.c:10", "Basis.")
        {
            Roles = new[] { BenchmarkClaimRoles.UnverifiedClaim },
            SuspectedFalse = true,
            Suspicion = "they contain gelatin",
            RecordedClaim = "Suspected false: Fortune cookies are vegan. — they contain gelatin"
        };
        var q1 = BoardGradedAnswer(1, 80);
        q1.UnverifiedClaimCount = 1;
        q1.UnverifiedClaimsJson = JsonSerializer.Serialize(new[] { suspected.RecordedClaim });
        q1.ClaimsSupportedCount = 1;
        q1.ClaimsRefutedCount = 0;
        q1.ClaimVerificationJson = JsonSerializer.Serialize(new[] { suspected });

        string report = BenchmarkReportBuilder.BuildMarkdownReport(Harness30BoardRun(q1));

        Assert.Contains("> - **Suspected false by the assessor:** 1 — refuted 0 (the verifier sided with the assessor), supported 1 (the verifier sided with the answer), indeterminate 0", report);
        Assert.Contains("- **Suspected False by the Assessor:** 1 across 1 answer(s) (Q1) — refuted 0 (the verifier sided with the assessor), supported 1 (the verifier sided with the answer), indeterminate 0.", report);
    }

    [Fact]
    public void CitationNote_DemotesTheVerdictInEveryCount_AndPrintsBoth()
    {
        var demoted = new BenchmarkClaimVerification(0, "Charged sentence.", BenchmarkClaimVerdict.Refuted, "src/priest.c:120", "Basis.")
        {
            Roles = new[] { BenchmarkClaimRoles.AccusedQuote },
            CitationNote = "cited function priest_talk has no live call site"
        };
        var q1 = BoardGradedAnswer(1, 60);
        q1.ClaimVerificationJson = JsonSerializer.Serialize(new[] { demoted });

        string report = BenchmarkReportBuilder.BuildMarkdownReport(Harness30BoardRun(q1));

        Assert.Contains("> - **Accused sentences checked:** 1 — supported 0, refuted 0, indeterminate 1", report);
        Assert.Contains("> - **Accused sentence, indeterminate:** a sentence the assessor charged as false was checked by the claim verifier and returned **indeterminate (verifier: refuted; cited function priest_talk has no live call site)** — \"Charged sentence.\" (src/priest.c:120).", report);
        Assert.Contains("> - **Citation note:** \"Charged sentence.\" — the verifier returned refuted citing src/priest.c:120, but cited function priest_talk has no live call site; the harness reads it as indeterminate.", report);
    }

    [Fact]
    public void AccusedSentence_WidenedFromFragments_KeepsTheQuotationsVisible()
    {
        var widened = new BenchmarkClaimVerification(0, "Keep a healthy supply of vegan food such as fortune cookies, and candy bars.", BenchmarkClaimVerdict.Refuted, "src/eat.c:5", "Basis.")
        {
            Roles = new[] { BenchmarkClaimRoles.AccusedQuote },
            QuotedFragments = new[] { "healthy supply of vegan food", "cookies, and candy" },
            Charge = "The answer's \"healthy supply of vegan food\" is wrong."
        };
        var q1 = BoardGradedAnswer(1, 60);
        q1.ClaimVerificationJson = JsonSerializer.Serialize(new[] { widened });

        string report = BenchmarkReportBuilder.BuildMarkdownReport(Harness30BoardRun(q1));

        Assert.Contains("— \"Keep a healthy supply of vegan food such as fortune cookies, and candy bars.\" (quoted: \"healthy supply of vegan food\", \"cookies, and candy\") (src/eat.c:5).", report);
    }

    [Fact]
    public void ClaimVerificationYield_CountsAssessorStatementsAsAThirdPopulation()
    {
        var q1 = ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 80);
        q1.ClaimVerificationJson = JsonSerializer.Serialize(new[]
        {
            RoleItem(0, "Charged but true.", BenchmarkClaimVerdict.Supported, "src/objects.c:2889", BenchmarkClaimRoles.AccusedQuote),
            RoleItem(1, "The assessor's own statement.", BenchmarkClaimVerdict.Refuted, "src/zap.c:9", BenchmarkClaimRoles.AssessorStatement)
        });
        var run = HarnessV7Run(BenchmarkSecondOpinionMode.Off, q1);
        run.TestedModelSnapshot = BenchmarkModelSnapshots.Model(provider: "OpenAI", modelId: "gpt-5.6", displayName: "GPT-5.6 Luna", thinkingLevel: "max");
        run.AssessorModelSnapshot = BenchmarkModelSnapshots.Model(provider: "Google", modelId: "gemini-3.7-flash", displayName: "Gemini 3.7 Flash");
        run.ClaimVerifierModelSnapshot = BenchmarkModelSnapshots.Model(modelId: "gpt-5-mini");
        run.TotalInputTokens = 200_000;
        run.TotalOutputTokens = 30_000;
        run.TotalAssessmentInputTokens = 100_000;
        run.TotalAssessmentOutputTokens = 10_000;
        run.TotalClaimVerificationInputTokens = 1_300_000;
        run.TotalClaimVerificationOutputTokens = 100_000;
        run.ClaimsSupportedCount = 7;
        run.ClaimsRefutedCount = 0;
        run.ClaimsIndeterminateCount = 1;

        var runPricing = new BenchmarkRunPricing(
            Candidate: new ModelPricing(2.50m, 10.00m, Source: ModelPricingSource.Catalog, AsOf: "2026-09-05"),
            Assessor: new ModelPricing(0.15m, 0.60m, Source: ModelPricingSource.Catalog, AsOf: "2026-09-05"),
            SecondOpinion: null,
            ClaimVerifier: new ModelPricing(1.00m, 4.00m, Source: ModelPricingSource.Custom),
            IsSnapshot: true
        );

        var report = BenchmarkReportBuilder.BuildMarkdownReport(run, runPricing: runPricing);

        Assert.Contains(
            "- **Claim Verification Yield:** 8 unverified claim(s) + 1 accused sentence(s) + 1 assessor statement(s) checked — claims: 7 supported, 0 refuted, 1 indeterminate; accused sentences: 1 supported, 0 refuted, 0 indeterminate; assessor statements: 0 supported, 1 refuted, 0 indeterminate. $1.70 ($0.17/item over all three), 67% of run cost.",
            report);
    }

    [Theory]
    [InlineData(11, "These are Accuracy deductions whose basis is the assessor's own knowledge rather than the rubric or the corpus it was given")]
    [InlineData(12, "These are Accuracy deductions the assessor made from its own knowledge although scoring method 12 tells it not to")]
    public void OutOfRubricAccuracyDeductions_AreDescribedByTheRunsScoringMethod(int method, string expected)
    {
        var q1 = BoardGradedAnswer(1, 60);
        q1.AnswerFlags = (int)BenchmarkAnswerFlags.OutOfRubricAccuracyDeduction;
        var run = Harness30BoardRun(q1);
        run.ScoringMethodVersion = method;

        Assert.Contains(expected, BenchmarkReportBuilder.BuildMarkdownReport(run));
    }

    [Fact]
    public void Section2_PrintsCriticalErrorsAndSensitivities_AboveTheSpeedIndex()
    {
        var cleared = VerificationClearedAnswer(1, BenchmarkDifficulty.Intermediate, 50, 70);
        var clearedWithCriticalError = VerificationClearedAnswer(2, BenchmarkDifficulty.Intermediate, 50, 25);
        clearedWithCriticalError.CriticalError = true;
        var untouched = ScoredAnswer(3, BenchmarkDifficulty.Intermediate, 50, 90);

        string report = BenchmarkReportBuilder.BuildMarkdownReport(HarnessV7Run(
            BenchmarkSecondOpinionMode.Off, cleared, clearedWithCriticalError, untouched));

        int finalIndices = report.IndexOf("## 7. Final Indices", StringComparison.Ordinal);
        int intelligence = report.IndexOf("### **Intelligence Index:", StringComparison.Ordinal);
        int criticalErrors = report.IndexOf("- **Critical Errors:**", StringComparison.Ordinal);
        int sensitivity = report.IndexOf("- **Verification-cleared Accuracy Sensitivity:**", StringComparison.Ordinal);
        int speed = report.IndexOf("### **Speed Index:", StringComparison.Ordinal);
        if (speed < 0)
        {
            speed = report.IndexOf("### **Median Model Time:", StringComparison.Ordinal);
        }

        Assert.True(intelligence >= 0 && criticalErrors >= 0 && sensitivity >= 0 && speed >= 0);
        Assert.True(intelligence < criticalErrors, "Critical Errors follows the Intelligence Index.");
        Assert.True(criticalErrors < sensitivity, "the sensitivities follow Critical Errors.");
        Assert.True(sensitivity < speed, "the sensitivities come before the Speed Index.");
        Assert.True(speed < finalIndices, "all of this is § 2, ahead of § 7.");
    }
}
