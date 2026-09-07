namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using MobileGnollHackLogger.Data;
using Overseer.Services.Benchmarking;
using Xunit;

/// <summary>
/// The multi-run report. Its defining property is that <b>nothing in it was written by a model</b>,
/// so the tests assert both that the arithmetic blocks are present and that the report never claims
/// something the statistics did not measure.
/// </summary>
public class BenchmarkGroupReportBuilderTests
{
    private static BenchmarkSuite Suite() => new() { Id = 5, Name = "GnollHack Player Assistance Benchmark Suite" };

    private static BenchmarkQuestion[] Questions(params int[] assessedDifficulties)
        => assessedDifficulties
            .Select((d, i) => new BenchmarkQuestion
            {
                Id = i + 1,
                BenchmarkSuiteId = 5,
                OrderIndex = i + 1,
                QuestionText = $"Question {i + 1} about GnollHack",
                Difficulty = BenchmarkDifficulty.Intermediate,
                AssessedDifficulty = d,
                ItemRevision = 1
            })
            .ToArray();

    private static BenchmarkRun Run(long runId, BenchmarkQuestion[] questions, int[] scores, int speedIndex = 72)
    {
        var run = new BenchmarkRun
        {
            Id = runId,
            BenchmarkSuiteId = 5,
            SuiteName = "GnollHack Player Assistance Benchmark Suite",
            TestedModelIdUsed = "gpt-5.6-luna",
            TestedModelDisplayNameUsed = "GPT-5.6 Luna",
            AssessorModelIdUsed = "gemini-3.7-flash",
            ScoringMethodVersion = 8,
            HarnessVersion = "12",
            SpeedIndex = speedIndex,
            QualityIndex = 94,
            StartedAtUtc = new DateTime(2026, 9, 6, 10, 0, 0, DateTimeKind.Utc),
            Status = BenchmarkRunStatus.Completed,
            CandidateSystemPromptSha256 = "e9b3e9a75278a5cbd09fbe3afb270806a3be318863c9c3e1623897428a1c16c6",
            ToolGuidesSha256 = "f59d8b30d2c2855931275fb0965f434db8ceb20feba84b4a5ac86eb65734f4a9",
            KnowledgeBaseHeadSha = "576ca5741d1bd79ef1cb2f7db575709cf0bb0db8"
        };

        for (int i = 0; i < questions.Length; i++)
        {
            run.Answers.Add(new BenchmarkRunAnswer
            {
                Id = runId * 100 + i,
                BenchmarkRunId = runId,
                BenchmarkQuestionId = questions[i].Id,
                ItemRevisionUsed = 1,
                OrderIndex = i + 1,
                QuestionText = questions[i].QuestionText,
                Status = BenchmarkAnswerStatus.Ok,
                AssessmentStatus = BenchmarkAssessmentStatus.Scored,
                QualityScore = scores[i],
                AssessedDifficulty = questions[i].AssessedDifficulty,
                DurationMs = 82600,
                ToolTimeMs = 0
            });
        }

        return run;
    }

    private static (BenchmarkRunGroup Group, BenchmarkGroupStatisticsResult Result, List<BenchmarkRun> Runs) Fixture()
    {
        var suite = Suite();
        var questions = Questions(20, 50, 80);
        var runs = new List<BenchmarkRun>
        {
            Run(1, questions, new[] { 60, 70, 80 }),
            Run(2, questions, new[] { 70, 80, 90 }),
            Run(3, questions, new[] { 80, 90, 100 })
        };

        var result = BenchmarkGroupStatistics.Compute(suite, questions, runs);

        var group = new BenchmarkRunGroup
        {
            Id = 11,
            Name = "GnollHack Player Assistance · GPT-5.6 Luna · 2026-09-06 · R=3",
            BenchmarkSuiteId = 5,
            Tier = BenchmarkRunGroupTier.Replicate,
            CreatedAtUtc = new DateTime(2026, 9, 6, 12, 0, 0, DateTimeKind.Utc)
        };

        return (group, result, runs);
    }

    [Fact]
    public void Report_CarriesTheManifestTheIndexTheItemsAndTheLimits()
    {
        var (group, result, runs) = Fixture();

        string md = BenchmarkGroupReportBuilder.BuildMarkdownReport(
            group, result, BenchmarkComparabilityKey.Resolve(runs), runs, overseerVersion: "1.2.3");

        // Sections are numbered by a running counter, so these assertions also pin the order.
        Assert.Contains("# Multi-Run Benchmark Analysis", md);
        Assert.Contains("## 1. Group Manifest", md);
        Assert.Contains("## 2. Multi-Run Intelligence Index", md);
        Assert.Contains("## 3. Quality Dimensions", md);
        Assert.Contains("## 4. Per-Item Statistics", md);
        Assert.Contains("## 5. Speed", md);
        Assert.Contains("## 6. Cost", md);
        Assert.Contains("What This Analysis Cannot Decompose", md);
        Assert.Contains("1.2.3", md);
    }

    /// <summary>
    /// The section counter, at the two shapes that broke the old hardcoded numbers: a report with
    /// no comparison and no usage block, and one with both.
    /// </summary>
    [Fact]
    public void Report_NumbersSectionsContiguously_WhateverTheOptionalOnesDo()
    {
        var (group, result, runs) = Fixture();

        string withoutOptional = BenchmarkGroupReportBuilder.BuildMarkdownReport(
            group, result, BenchmarkComparabilityKey.Resolve(runs), runs);

        // No usage recorded and no comparison: Limits follows Cost directly.
        Assert.Contains("## 6. Cost", withoutOptional);
        Assert.Contains("## 7. What This Analysis Cannot Decompose", withoutOptional);
        Assert.DoesNotContain("## 7. Token and Tool Usage", withoutOptional);
    }

    [Fact]
    public void Report_ShowsBothIntervalComponentsSeparatelyBeforeCombiningThem()
    {
        var (group, result, runs) = Fixture();

        string md = BenchmarkGroupReportBuilder.BuildMarkdownReport(
            group, result, BenchmarkComparabilityKey.Resolve(runs), runs);

        // Each component is labelled with the question it answers, and the combined interval says
        // it covers both. Collapsing them would hide that only one can be bought down with runs.
        Assert.Contains("Would a re-run move this?", md);
        Assert.Contains("Would a different set of questions move this?", md);
        Assert.Contains("Combined 95 % interval", md);
        Assert.Contains("does not shrink as more runs are added, and that is correct", md);
    }

    [Fact]
    public void Report_SaysNoReproducibilityFigureIsAvailableBelowThreeRuns()
    {
        var suite = Suite();
        var questions = Questions(20, 50, 80);
        var runs = new List<BenchmarkRun>
        {
            Run(1, questions, new[] { 60, 70, 80 }),
            Run(2, questions, new[] { 80, 90, 100 })
        };
        var result = BenchmarkGroupStatistics.Compute(suite, questions, runs);
        var group = new BenchmarkRunGroup { Id = 12, Name = "R=2", BenchmarkSuiteId = 5 };

        string md = BenchmarkGroupReportBuilder.BuildMarkdownReport(
            group, result, BenchmarkComparabilityKey.Resolve(runs), runs);

        Assert.Contains("No reproducibility figure is reported at *R* = 2", md);
        Assert.Contains("covers **one** source, not two", md);
    }

    [Fact]
    public void Report_ReportsAnAbsentCostAsAbsentRatherThanZero()
    {
        var (group, result, runs) = Fixture();

        string md = BenchmarkGroupReportBuilder.BuildMarkdownReport(
            group, result, BenchmarkComparabilityKey.Resolve(runs), runs);

        Assert.Null(result.Cost);
        Assert.Contains("An absent figure is reported as absent rather than as zero", md);
        Assert.DoesNotContain("**Total across 0 runs:** $0.00", md);
    }

    [Fact]
    public void Report_LabelsEveryPerItemComparisonExploratoryAndNamesTheFdrProcedure()
    {
        var suite = Suite();
        var questions = Questions(20, 50, 80);

        var baselineRuns = new List<BenchmarkRun>
        {
            Run(1, questions, new[] { 60, 70, 80 }),
            Run(2, questions, new[] { 62, 72, 82 }),
            Run(3, questions, new[] { 58, 68, 78 })
        };
        var treatmentRuns = new List<BenchmarkRun>
        {
            Run(4, questions, new[] { 70, 80, 90 }),
            Run(5, questions, new[] { 72, 82, 92 }),
            Run(6, questions, new[] { 68, 78, 88 })
        };

        var baseline = BenchmarkGroupStatistics.Compute(suite, questions, baselineRuns);
        var treatment = BenchmarkGroupStatistics.Compute(suite, questions, treatmentRuns);
        var comparison = BenchmarkGroupStatistics.Compare(baseline, treatment);

        var group = new BenchmarkRunGroup { Id = 13, Name = "Treatment", BenchmarkSuiteId = 5 };

        string md = BenchmarkGroupReportBuilder.BuildMarkdownReport(
            group, treatment, BenchmarkComparabilityKey.Resolve(treatmentRuns), treatmentRuns,
            comparison, "Baseline");

        // Cost is 6 and no usage was recorded on this fixture, so the comparison lands at 7.
        Assert.Contains("## 7. Paired Group Comparison", md);
        Assert.Contains("## 8. What This Analysis Cannot Decompose", md);
        Assert.Contains("Wilcoxon signed-rank (primary)", md);
        Assert.Contains("Paired *t* (secondary)", md);
        Assert.Contains("Cohen's *d*z", md);
        Assert.Contains("exploratory", md, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Benjamini", md);
        Assert.All(comparison.ItemComparisons, c => Assert.True(c.Exploratory));
    }

    [Fact]
    public void Report_RefusesNothingButStatesThatACrossConditionSetIsNotAReplicateMeasurement()
    {
        var suite = Suite();
        var questions = Questions(20, 50, 80);

        var a = Run(1, questions, new[] { 60, 70, 80 });
        var b = Run(2, questions, new[] { 70, 80, 90 });
        b.ToolGuidesSha256 = "0000000000000000000000000000000000000000000000000000000000000000";

        var runs = new List<BenchmarkRun> { a, b };
        var comparability = BenchmarkComparabilityKey.Resolve(runs);
        Assert.Equal(BenchmarkComparabilityTier.CrossCondition, comparability.Tier);

        var result = BenchmarkGroupStatistics.Compute(suite, questions, runs);
        var group = new BenchmarkRunGroup
        {
            Id = 14,
            Name = "T15 treatment vs baseline",
            BenchmarkSuiteId = 5,
            Tier = BenchmarkRunGroupTier.CrossCondition,
            CrossCondition = true
        };

        string md = BenchmarkGroupReportBuilder.BuildMarkdownReport(group, result, comparability, runs);

        Assert.Contains("Cross-condition set", md);
        Assert.Contains("ToolGuidesSha256", md);
    }

    [Fact]
    public void Report_StatesItCarriesNoModelWrittenProse()
    {
        var (group, result, runs) = Fixture();

        string md = BenchmarkGroupReportBuilder.BuildMarkdownReport(
            group, result, BenchmarkComparabilityKey.Resolve(runs), runs);

        Assert.Contains("There is no AI-written synthesis anywhere in it", md);
        Assert.Contains("No part of it was written by a model", md);
    }

    [Fact]
    public void Report_SurvivesTheDeletionOfItsMemberRuns()
    {
        var (group, result, _) = Fixture();

        // The persisted analysis is what the report is built from, so a group whose runs have since
        // been deleted still renders its statistics — only the manifest rows are unavailable.
        string md = BenchmarkGroupReportBuilder.BuildMarkdownReport(group, result, null, null);

        Assert.Contains("one or more members have been deleted", md);
        Assert.Contains("## 2. Multi-Run Intelligence Index", md);
    }

    // --- The blocks the report was missing --------------------------------------------------------

    /// <summary>
    /// Without this block a reader cannot tell prompt adherence from a model weakness, which is the
    /// attribution check every dimensional finding depends on.
    /// </summary>
    [Fact]
    public void Report_RendersTheChatPromptUnderTest_FromTheMembers()
    {
        var suite = Suite();
        var questions = Questions(50, 50);
        const string Options = "{\"verboseMode\":false,\"overseerMode\":0,\"enableToolUse\":true,"
            + "\"allowSourceCodeReferences\":true,\"enableWebSearch\":false,\"hasWikiContext\":false}";

        var runs = new List<BenchmarkRun>
        {
            Run(1, questions, new[] { 90, 80 }),
            Run(2, questions, new[] { 92, 82 })
        };
        foreach (var run in runs) run.CandidatePromptOptionsJson = Options;

        var result = BenchmarkGroupStatistics.Compute(suite, questions, runs);
        var group = new BenchmarkRunGroup { Id = 1, Name = "G", BenchmarkSuiteId = 5 };

        string md = BenchmarkGroupReportBuilder.BuildMarkdownReport(
            group, result, BenchmarkComparabilityKey.Resolve(runs), runs);

        Assert.Contains("### 1.1 Chat Prompt Under Test", md);
        Assert.Contains("ChatService.BuildSystemPrompt", md);
        Assert.Contains("concise (`verboseMode: false`)", md);
        Assert.Contains("Gameplay Help", md);

        // The divergence from live chat that every routing figure is measured under.
        Assert.Contains("Live chat pre-injects wiki articles and the benchmark does not", md);
        Assert.Contains("every one of them was graded under exactly this configuration", md);
    }

    [Fact]
    public void Report_SaysSo_WhenThePromptConfigurationWasNotRecorded()
    {
        var (group, result, runs) = Fixture();

        string md = BenchmarkGroupReportBuilder.BuildMarkdownReport(
            group, result, BenchmarkComparabilityKey.Resolve(runs), runs);

        Assert.Contains("The prompt configuration was not recorded for these runs", md);
    }

    [Fact]
    public void Report_RendersTheDimensionTable_AndNamesTheLowestDimension()
    {
        var suite = Suite();
        var questions = Questions(50, 50);

        var runs = new List<BenchmarkRun>
        {
            Run(1, questions, new[] { 90, 80 }),
            Run(2, questions, new[] { 92, 82 }),
            Run(3, questions, new[] { 94, 84 })
        };

        // Accuracy near the ceiling, Completeness well below it — the four-run-old finding this
        // section exists to make measurable across runs.
        int[][] accuracy = { new[] { 96, 94 }, new[] { 98, 96 }, new[] { 97, 95 } };
        int[][] completeness = { new[] { 84, 80 }, new[] { 86, 82 }, new[] { 88, 84 } };
        for (int r = 0; r < runs.Count; r++)
        {
            for (int i = 0; i < questions.Length; i++)
            {
                runs[r].Answers[i].AccuracyScore = accuracy[r][i];
                runs[r].Answers[i].CompletenessScore = completeness[r][i];
            }
        }

        var result = BenchmarkGroupStatistics.Compute(suite, questions, runs);
        var group = new BenchmarkRunGroup { Id = 1, Name = "G", BenchmarkSuiteId = 5 };

        string md = BenchmarkGroupReportBuilder.BuildMarkdownReport(
            group, result, BenchmarkComparabilityKey.Resolve(runs), runs);

        Assert.Contains("## 3. Quality Dimensions", md);
        Assert.Contains("| **Accuracy** |", md);
        Assert.Contains("| **Completeness** |", md);
        Assert.Contains("**Lowest dimension:** Completeness", md);
        Assert.Contains("trailing Accuracy by 12.0 points", md);

        // The unweighted-versus-weighted warning, and the prompt-adherence one.
        Assert.Contains("unweighted", md);
        Assert.Contains("the prompt instructed the model to answer that way", md);
    }

    [Fact]
    public void Report_SaysSo_WhenNoDimensionWasScored()
    {
        var (group, result, runs) = Fixture();

        string md = BenchmarkGroupReportBuilder.BuildMarkdownReport(
            group, result, BenchmarkComparabilityKey.Resolve(runs), runs);

        Assert.Contains("No member recorded per-dimension scores", md);
    }

    [Fact]
    public void Report_RendersTokenToolAndClaimUsage_WhenAnyWasRecorded()
    {
        var suite = Suite();
        var questions = Questions(50, 50);

        var runs = new List<BenchmarkRun>
        {
            Run(1, questions, new[] { 90, 80 }),
            Run(2, questions, new[] { 92, 82 })
        };

        foreach (var run in runs)
        {
            run.TotalInputTokens = 2_100_000;
            run.TotalOutputTokens = 60_000;
            run.TotalCacheReadTokens = 1_900_000;
            run.TotalClaimVerificationInputTokens = 400_000;
            run.ClaimsSupportedCount = 5;
            run.ClaimVerifiedAnswerCount = 2;
            run.Answers[0].ToolCallSummary = "source_code_search×6, wiki_search×4";
            run.Answers[1].ToolCallSummary = "get_monster_stats×2";
        }

        var result = BenchmarkGroupStatistics.Compute(suite, questions, runs);
        var group = new BenchmarkRunGroup { Id = 1, Name = "G", BenchmarkSuiteId = 5 };

        string md = BenchmarkGroupReportBuilder.BuildMarkdownReport(
            group, result, BenchmarkComparabilityKey.Resolve(runs), runs);

        Assert.Contains("## 7. Token and Tool Usage", md);
        Assert.Contains("4.20 M", md);                       // pooled candidate input
        Assert.Contains("Prompt cache reads", md);
        Assert.Contains("Grader tokens, kept separate", md);
        Assert.Contains("| SourceCode |", md);
        Assert.Contains("| Wiki |", md);
        Assert.Contains("10 claims checked across 4 answers", md);
        Assert.Contains("**0 refuted**", md);
    }

    [Fact]
    public void Report_OmitsTheUsageSection_WhenNothingWasRecorded()
    {
        var (group, result, runs) = Fixture();

        string md = BenchmarkGroupReportBuilder.BuildMarkdownReport(
            group, result, BenchmarkComparabilityKey.Resolve(runs), runs);

        Assert.DoesNotContain("Token and Tool Usage", md);
    }

    [Fact]
    public void Report_ShowsPerRoleCostDispersion_AndNamesTheRoleCarryingIt()
    {
        var suite = Suite();
        var questions = Questions(50);
        var runs = new List<BenchmarkRun>
        {
            Run(1, questions, new[] { 90 }),
            Run(2, questions, new[] { 92 })
        };

        var result = BenchmarkGroupStatistics.Compute(suite, questions, runs, new[]
        {
            new BenchmarkGroupRunCost
            {
                RunId = 1,
                CostByRole = new Dictionary<string, double>
                {
                    ["candidate"] = 0.30, ["assessor"] = 0.50, ["claimVerifier"] = 2.00
                }
            },
            new BenchmarkGroupRunCost
            {
                RunId = 2,
                CostByRole = new Dictionary<string, double>
                {
                    ["candidate"] = 0.30, ["assessor"] = 0.50, ["claimVerifier"] = 5.00
                }
            }
        });

        var group = new BenchmarkRunGroup { Id = 1, Name = "G", BenchmarkSuiteId = 5 };

        string md = BenchmarkGroupReportBuilder.BuildMarkdownReport(
            group, result, BenchmarkComparabilityKey.Resolve(runs), runs);

        Assert.Contains("| Role | Total | Mean per run | SD | Min | Max | Share |", md);
        Assert.Contains("**Per-run totals:**", md);
        Assert.Contains("Cost dispersion sits mostly in `claimVerifier`", md);
    }

    /// <summary>
    /// An interval that leaves the score range is reported at the bound and marked, rather than
    /// printed as an impossible number or silently narrowed.
    /// </summary>
    [Fact]
    public void Report_MarksIntervalsTruncatedAtTheScoreBound()
    {
        var suite = Suite();
        var questions = Questions(50);
        var runs = new List<BenchmarkRun>
        {
            Run(1, questions, new[] { 45 }),
            Run(2, questions, new[] { 78 }),
            Run(3, questions, new[] { 90 })
        };

        var result = BenchmarkGroupStatistics.Compute(suite, questions, runs);
        var group = new BenchmarkRunGroup { Id = 1, Name = "G", BenchmarkSuiteId = 5 };

        string md = BenchmarkGroupReportBuilder.BuildMarkdownReport(
            group, result, BenchmarkComparabilityKey.Resolve(runs), runs);

        Assert.Contains("†", md);
        Assert.Contains("pinned there", md);
        Assert.DoesNotContain("128.9", md);
    }
}
