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
        run.ClaimVerifierModelConfigurationId = 8;
        run.ClaimVerifierDisplayNameUsed = "Verifier Model";
        run.ClaimVerifierProviderUsed = "Google";
        run.ClaimVerifierModelIdUsed = "gemini-3.7-flash";

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
            TestedModelDisplayNameUsed = "GPT-5.6 Luna",
            TestedModelProviderUsed = "OpenAI",
            TestedModelIdUsed = "gpt-5.6-luna",
            TestedModelThinkingLevelUsed = "max",
            AssessorModelDisplayNameUsed = "Gemini 3.7 Flash",
            AssessorModelProviderUsed = "Google",
            AssessorModelIdUsed = "gemini-3.7-flash",
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
        run.TestedModelProviderUsed = "Anthropic";
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
        run.TestedModelThinkingLevelUsed = "low";

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

        Assert.Contains("range 60–97, lowest Q1", report);
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

    [Fact]
    public void AssessorAgreement_CarriesTheConditioningCaveat_UnderTriggerSelectedCoverage()
    {
        var q1 = ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 60);
        q1.SecondOpinionQualityScore = 85;
        q1.SecondOpinionDisagreed = true;
        q1.SecondOpinionTrigger = "BelowThreshold";
        q1.SecondOpinionByModelDisplayNameUsed = "Claude Opus 5";

        var run = HarnessV7Run(
            BenchmarkSecondOpinionMode.Flagged,
            q1,
            ScoredAnswer(2, BenchmarkDifficulty.Advanced, 85, 99));
        run.SecondOpinionAssessorModelConfigurationId = 4;
        run.SecondOpinionAssessorModelDisplayNameUsed = "Claude Opus 5";
        run.SecondOpinionAssessorModelProviderUsed = "Anthropic";
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
        run.SecondOpinionAssessorModelDisplayNameUsed = "Claude Opus 5";
        run.SecondOpinionAssessorModelProviderUsed = "Anthropic";
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
        Assert.Contains("Benchmark:ToolCallBudget:{Band}", report);
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

    [Fact]
    public void Reassessment_RecordsTheScoreItReplacedAndWhoReplacedIt()
    {
        var q1 = ScoredAnswer(1, BenchmarkDifficulty.Simple, 25, 85);
        q1.ReassessmentCount = 1;
        q1.PreviousQualityScore = 60;
        q1.ReassessedAtUtc = new DateTime(2026, 9, 4, 8, 30, 0, DateTimeKind.Utc);
        q1.ReassessedByModelDisplayNameUsed = "Claude Opus 5";

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
        run.SecondOpinionAssessorModelDisplayNameUsed = "Gemini 3.7 Pro";
        run.SecondOpinionAssessorModelProviderUsed = "Google";

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
        run.SecondOpinionAssessorModelDisplayNameUsed = "Claude Opus 5";
        run.SecondOpinionAssessorModelProviderUsed = "Anthropic";

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
        run.SecondOpinionAssessorModelDisplayNameUsed = "Claude Opus 5";
        run.SecondOpinionAssessorModelProviderUsed = "Anthropic";
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

        Assert.Contains("**Advisory Flags:** 2 (reasoning bleed: 1, repeated fragments: 0, contested verdicts: 1, unevidenced deductions: 0, omissions as accuracy: 0, refuted claims: 0)", report);
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
        run.ClaimVerifierDisplayNameUsed = "Verifier Model";
        run.ClaimVerifierProviderUsed = "Google";
        run.ClaimVerifierModelIdUsed = "gemini-3.7-pro";

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
        run.SecondOpinionAssessorModelDisplayNameUsed = "Second Assessor";
        run.SecondOpinionBlindUsed = true;
        BenchmarkRunFinalizer.Apply(run, new[] { q1 });

        var report = BenchmarkReportBuilder.BuildMarkdownReport(run);

        Assert.Contains("### Disputed Assessments", report);
        Assert.Contains("Claims: 3 supported, 0 refuted, 0 indeterminate", report);
        Assert.Contains("Assessor agreement is reported for a **blind** second reader", report);
    }
}
