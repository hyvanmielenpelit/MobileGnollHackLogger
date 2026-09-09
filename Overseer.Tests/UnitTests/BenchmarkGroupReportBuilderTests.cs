namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MobileGnollHackLogger.Data;
using Overseer.Services.Benchmarking;
using Xunit;

// System.Linq declares a ParallelExecutionMode of its own, so the unqualified name is
// ambiguous in this file.
using ParallelExecutionMode = MobileGnollHackLogger.Data.ParallelExecutionMode;

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

    /// <summary>
    /// <see cref="Overseer.Services.Tools.ToolRegistry.GetParallelOverrideText"/> returns
    /// <see cref="string.Empty"/> for <see cref="ParallelExecutionMode.Enabled"/>: no override file
    /// is loaded, so the report must not claim one is. The base <c>_policy.md</c> batching text
    /// still applies and the report must say so.
    /// </summary>
    [Fact]
    public void Report_ToolBatchingPolicy_EnabledNamesNoOverrideFile_AndSaysThePolicyFileApplies()
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
        foreach (var run in runs)
        {
            run.CandidatePromptOptionsJson = Options;
            run.TestedModelParallelExecutionModeUsed = ParallelExecutionMode.Enabled;
        }

        var result = BenchmarkGroupStatistics.Compute(suite, questions, runs);
        var group = new BenchmarkRunGroup { Id = 1, Name = "G", BenchmarkSuiteId = 5 };

        string md = BenchmarkGroupReportBuilder.BuildMarkdownReport(
            group, result, BenchmarkComparabilityKey.Resolve(runs), runs);

        Assert.DoesNotContain("_policy_parallel_on_request.md", md);
        Assert.DoesNotContain("_policy_parallel_disabled.md", md);
        Assert.Contains("Tool batching policy:** Enabled", md);
        Assert.Contains("the batching guidance in `Overseer/ToolGuides/_policy.md` applies unchanged", md);
    }

    /// <summary>
    /// The other two modes still name their override file, unlike <c>Enabled</c>.
    /// </summary>
    [Theory]
    [InlineData(ParallelExecutionMode.Disabled, "_policy_parallel_disabled.md")]
    [InlineData(ParallelExecutionMode.OnRequest, "_policy_parallel_on_request.md")]
    public void Report_ToolBatchingPolicy_NamesTheOverrideFile_ForDisabledAndOnRequest(
        ParallelExecutionMode mode, string expectedFile)
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
        foreach (var run in runs)
        {
            run.CandidatePromptOptionsJson = Options;
            run.TestedModelParallelExecutionModeUsed = mode;
        }

        var result = BenchmarkGroupStatistics.Compute(suite, questions, runs);
        var group = new BenchmarkRunGroup { Id = 1, Name = "G", BenchmarkSuiteId = 5 };

        string md = BenchmarkGroupReportBuilder.BuildMarkdownReport(
            group, result, BenchmarkComparabilityKey.Resolve(runs), runs);

        Assert.Contains($"selects `Overseer/ToolGuides/{expectedFile}`", md);
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
                    [BenchmarkGroupAnalysisService.CandidateRole] = 0.30,
                    [BenchmarkGroupAnalysisService.AssessorRole] = 0.50,
                    [BenchmarkGroupAnalysisService.ClaimVerifierRole] = 2.00
                }
            },
            new BenchmarkGroupRunCost
            {
                RunId = 2,
                CostByRole = new Dictionary<string, double>
                {
                    [BenchmarkGroupAnalysisService.CandidateRole] = 0.30,
                    [BenchmarkGroupAnalysisService.AssessorRole] = 0.50,
                    [BenchmarkGroupAnalysisService.ClaimVerifierRole] = 5.00
                }
            }
        });

        var group = new BenchmarkRunGroup { Id = 1, Name = "G", BenchmarkSuiteId = 5 };

        string md = BenchmarkGroupReportBuilder.BuildMarkdownReport(
            group, result, BenchmarkComparabilityKey.Resolve(runs), runs);

        Assert.Contains("| Role | Total | Mean per run | SD | Min | Max | Share |", md);
        Assert.Contains("**Per-run totals:**", md);
        Assert.Contains($"Cost dispersion sits mostly in `{BenchmarkGroupAnalysisService.ClaimVerifierRole}`", md);
    }

    // --- Tracing an unstable item to the run that produced it -------------------------------------

    /// <summary>
    /// Min and Max say an item swung; only the score vector says which run it swung on. Two items
    /// that scored 25/97/97 and 26/100/100 across three runs have one collapsed run between them,
    /// and a report that cannot name it cannot be acted on.
    /// </summary>
    [Fact]
    public void Report_RendersThePerRunScoreVector_AndNamesTheOrderItIsIn()
    {
        var suite = Suite();
        var questions = Questions(50, 50);
        var runs = new List<BenchmarkRun>
        {
            Run(19, questions, new[] { 25, 26 }),
            Run(20, questions, new[] { 97, 100 }),
            Run(21, questions, new[] { 97, 100 })
        };

        var result = BenchmarkGroupStatistics.Compute(suite, questions, runs);
        var group = new BenchmarkRunGroup { Id = 1, Name = "G", BenchmarkSuiteId = 5 };

        string md = BenchmarkGroupReportBuilder.BuildMarkdownReport(
            group, result, BenchmarkComparabilityKey.Resolve(runs), runs);

        Assert.Contains("| Per-run scores |", md);
        Assert.Contains("| 25 / 97 / 97 |", md);
        Assert.Contains("| 26 / 100 / 100 |", md);

        // The order is named once, under the table, or the vector cannot be read back to a run.
        Assert.Contains("Per-run scores are in run-id order: 19, 20, 21.", md);
    }

    /// <summary>
    /// A ceiling-driven SD and one earned on the dimensions look identical in the item table and
    /// need opposite responses: the first is a rubric that cannot decide a borderline case, the
    /// second an answer that genuinely varies.
    /// </summary>
    [Fact]
    public void Report_SeparatesACeilingDrivenUnstableItemFromOneEarnedOnTheDimensions()
    {
        var suite = Suite();
        var questions = Questions(50, 50);
        var runs = new List<BenchmarkRun>
        {
            Run(19, questions, new[] { 25, 26 }),
            Run(20, questions, new[] { 97, 100 }),
            Run(21, questions, new[] { 97, 100 })
        };

        // Q1's low run tripped the critical-error ceiling; Q2's did not.
        runs[0].Answers[0].CriticalError = true;

        var result = BenchmarkGroupStatistics.Compute(suite, questions, runs);
        Assert.True(result.Items.Single(i => i.OrderIndex == 1).Unstable);
        Assert.True(result.Items.Single(i => i.OrderIndex == 2).Unstable);

        var group = new BenchmarkRunGroup { Id = 1, Name = "G", BenchmarkSuiteId = 5 };

        string md = BenchmarkGroupReportBuilder.BuildMarkdownReport(
            group, result, BenchmarkComparabilityKey.Resolve(runs), runs);

        Assert.Contains("**unstable — ceiling**", md);
        Assert.Contains("**Unstable because the critical-error ceiling tripped in some runs and not others:** Q1 (1/3 runs)", md);
        Assert.Contains("**Unstable with the critical-error rate at 0, so earned on the dimensions:** Q2", md);

        // Q2 is never named as a ceiling case, and Q1 is never named as a dimensional one.
        Assert.DoesNotContain("Q2 (0/3 runs)", md);
    }

    // --- The reproducibility SD's own uncertainty --------------------------------------------------

    /// <summary>
    /// An SD from three runs is uncertain by better than a factor of six upward. Without the
    /// interval a reader comparing one report's 0.30 against another's 3.34 concludes the instrument
    /// became ten times less reproducible, when the two intervals overlap.
    /// </summary>
    [Fact]
    public void Report_RendersTheReproducibilitySdInterval_AndRefusesTheBareComparison()
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
        var group = new BenchmarkRunGroup { Id = 1, Name = "G", BenchmarkSuiteId = 5 };

        string md = BenchmarkGroupReportBuilder.BuildMarkdownReport(
            group, result, BenchmarkComparabilityKey.Resolve(runs), runs);

        // Per-run indices 74 / 84 / 94 give a sample SD of exactly 10 on df = 2.
        Assert.Contains("SD 10.00, 95 % interval on σ [5.21, 62.85], from the χ²(2) distribution", md);
        Assert.Contains("the upper bound is 6.3 × the point estimate", md);
        Assert.Contains("two groups' SDs are not comparable point estimates", md);
    }

    [Fact]
    public void Report_OmitsTheSdInterval_WhenNoReproducibilitySdWasComputed()
    {
        var suite = Suite();
        var questions = Questions(20, 50, 80);
        var runs = new List<BenchmarkRun>
        {
            Run(1, questions, new[] { 60, 70, 80 }),
            Run(2, questions, new[] { 80, 90, 100 })
        };

        var result = BenchmarkGroupStatistics.Compute(suite, questions, runs);
        var group = new BenchmarkRunGroup { Id = 1, Name = "R=2", BenchmarkSuiteId = 5 };

        string md = BenchmarkGroupReportBuilder.BuildMarkdownReport(
            group, result, BenchmarkComparabilityKey.Resolve(runs), runs);

        Assert.DoesNotContain("95 % interval on σ", md);
    }

    // --- Time to first token ----------------------------------------------------------------------

    [Fact]
    public void Report_RendersPooledTimeToFirstToken_WithTheAnswerCountItCovers()
    {
        var suite = Suite();
        var questions = Questions(50, 50);
        var runs = new List<BenchmarkRun>
        {
            Run(1, questions, new[] { 90, 80 }),
            Run(2, questions, new[] { 92, 82 })
        };

        runs[0].Answers[0].TimeToFirstTokenMs = 1000;
        runs[0].Answers[1].TimeToFirstTokenMs = 3000;
        runs[1].Answers[0].TimeToFirstTokenMs = 5000;
        runs[1].Answers[1].TimeToFirstTokenMs = 7000;

        var result = BenchmarkGroupStatistics.Compute(suite, questions, runs);
        var group = new BenchmarkRunGroup { Id = 1, Name = "G", BenchmarkSuiteId = 5 };

        string md = BenchmarkGroupReportBuilder.BuildMarkdownReport(
            group, result, BenchmarkComparabilityKey.Resolve(runs), runs);

        // Pooled {1000, 3000, 5000, 7000}: P50 interpolates to 4000, P90 to 6400.
        Assert.Contains("**Pooled time to first token** over 4 answers — P50 4.0 s, P90 6.4 s, max 7.0 s", md);
        Assert.Contains("the latency a chat user actually waits through", md);
    }

    /// <summary>
    /// An absent measurement renders as an em dash. A stale analysis printing <c>0.0 s</c> would be
    /// a false statement about a latency nobody measured.
    /// </summary>
    [Fact]
    public void Report_ReportsAnAbsentTimeToFirstTokenAsAbsent_RatherThanZero()
    {
        var (group, result, runs) = Fixture();

        string md = BenchmarkGroupReportBuilder.BuildMarkdownReport(
            group, result, BenchmarkComparabilityKey.Resolve(runs), runs);

        Assert.Contains("**Pooled time to first token:** —", md);
        Assert.DoesNotContain("**Pooled time to first token** over 0 answers", md);
    }

    // --- Model calls, which is what input cost tracks ---------------------------------------------

    [Fact]
    public void Report_RendersModelCalls_AndSaysInputCostTracksThemRatherThanToolCalls()
    {
        var suite = Suite();
        var questions = Questions(50, 50);
        var runs = new List<BenchmarkRun>
        {
            Run(1, questions, new[] { 90, 80 }),
            Run(2, questions, new[] { 92, 82 }),
            Run(3, questions, new[] { 94, 84 })
        };

        runs[0].Answers[0].ModelCallCount = 4;
        runs[0].Answers[1].ModelCallCount = 6;
        runs[1].Answers[0].ModelCallCount = 8;

        // Run 3 recorded none, so it carries no per-run figure at all.

        var result = BenchmarkGroupStatistics.Compute(suite, questions, runs);
        var group = new BenchmarkRunGroup { Id = 1, Name = "G", BenchmarkSuiteId = 5 };

        string md = BenchmarkGroupReportBuilder.BuildMarkdownReport(
            group, result, BenchmarkComparabilityKey.Resolve(runs), runs);

        Assert.Contains("**Model calls:** 18 across 3 runs, 9.0 per run", md);
        Assert.Contains("**Per-run model calls:** 10, 8, —", md);
        Assert.Contains("Input cost tracks model calls, not tool calls", md);
    }

    /// <summary>
    /// An analysis persisted before the model-call fields existed deserialises them as absent, and
    /// the report has to say absent. Rendering <c>0 model calls</c> would be a false statement.
    /// </summary>
    [Fact]
    public void Report_ReportsAnAbsentModelCallCountAsAbsent_RatherThanZero()
    {
        var suite = Suite();
        var questions = Questions(50);
        var runs = new List<BenchmarkRun>
        {
            Run(1, questions, new[] { 90 }),
            Run(2, questions, new[] { 92 })
        };

        // Tokens make the usage section render; nothing recorded a model-call count.
        foreach (var run in runs) run.TotalInputTokens = 1_000_000;

        var result = BenchmarkGroupStatistics.Compute(suite, questions, runs);
        Assert.Null(result.Usage!.TotalModelCalls);

        var group = new BenchmarkRunGroup { Id = 1, Name = "G", BenchmarkSuiteId = 5 };

        string md = BenchmarkGroupReportBuilder.BuildMarkdownReport(
            group, result, BenchmarkComparabilityKey.Resolve(runs), runs);

        Assert.Contains("**Model calls:** — *no member answer recorded a model-call count", md);
        Assert.DoesNotContain("**Model calls:** 0 across", md);
    }

    // --- What the claim verifier's spend bought ----------------------------------------------------

    /// <summary>
    /// The verifier's yield as division rather than as two counts placed side by side. Zero
    /// refutations is said in words: an infinity, a zero and a blank all read as a measurement.
    /// </summary>
    [Fact]
    public void Report_DividesTheVerifierSpendByItsYield_AndSaysNoRatioExistsAtZeroRefutations()
    {
        var suite = Suite();
        var questions = Questions(50);
        var runs = new List<BenchmarkRun>
        {
            Run(1, questions, new[] { 90 }),
            Run(2, questions, new[] { 92 })
        };

        runs[0].ClaimsSupportedCount = 12;
        runs[0].ClaimsIndeterminateCount = 4;
        runs[0].ClaimVerifiedAnswerCount = 1;
        runs[1].ClaimsSupportedCount = 11;
        runs[1].ClaimsIndeterminateCount = 4;
        runs[1].ClaimVerifiedAnswerCount = 1;

        var result = BenchmarkGroupStatistics.Compute(suite, questions, runs, new[]
        {
            new BenchmarkGroupRunCost
            {
                RunId = 1,
                CostByRole = new Dictionary<string, double>
                {
                    [BenchmarkGroupAnalysisService.CandidateRole] = 0.30,
                    [BenchmarkGroupAnalysisService.ClaimVerifierRole] = 1.55
                }
            },
            new BenchmarkGroupRunCost
            {
                RunId = 2,
                CostByRole = new Dictionary<string, double>
                {
                    [BenchmarkGroupAnalysisService.CandidateRole] = 0.30,
                    [BenchmarkGroupAnalysisService.ClaimVerifierRole] = 1.55
                }
            }
        });

        var group = new BenchmarkRunGroup { Id = 1, Name = "G", BenchmarkSuiteId = 5 };

        string md = BenchmarkGroupReportBuilder.BuildMarkdownReport(
            group, result, BenchmarkComparabilityKey.Resolve(runs), runs);

        // $3.10 of verifier spend over 31 claims, 23 supported and 8 indeterminate.
        Assert.Contains("**Cost per claim checked:** $0.1000 — $3.10 over 31 claims", md);
        Assert.Contains("**Cost per refutation:** no refutations, so this ratio does not exist", md);
        Assert.Contains("**Indeterminate share:** 25.8 % — 8 of 31", md);
    }

    [Fact]
    public void Report_DividesTheVerifierSpendByRefutations_WhenThereWereAny()
    {
        var suite = Suite();
        var questions = Questions(50);
        var runs = new List<BenchmarkRun>
        {
            Run(1, questions, new[] { 90 }),
            Run(2, questions, new[] { 92 })
        };

        runs[0].ClaimsSupportedCount = 12;
        runs[0].ClaimsRefutedCount = 1;
        runs[0].ClaimVerifiedAnswerCount = 1;
        runs[1].ClaimsSupportedCount = 11;
        runs[1].ClaimsRefutedCount = 1;
        runs[1].ClaimVerifiedAnswerCount = 1;

        var result = BenchmarkGroupStatistics.Compute(suite, questions, runs, new[]
        {
            new BenchmarkGroupRunCost
            {
                RunId = 1,
                CostByRole = new Dictionary<string, double>
                {
                    [BenchmarkGroupAnalysisService.ClaimVerifierRole] = 1.55
                }
            },
            new BenchmarkGroupRunCost
            {
                RunId = 2,
                CostByRole = new Dictionary<string, double>
                {
                    [BenchmarkGroupAnalysisService.ClaimVerifierRole] = 1.55
                }
            }
        });

        var group = new BenchmarkRunGroup { Id = 1, Name = "G", BenchmarkSuiteId = 5 };

        string md = BenchmarkGroupReportBuilder.BuildMarkdownReport(
            group, result, BenchmarkComparabilityKey.Resolve(runs), runs);

        Assert.Contains("**Cost per refutation:** $1.55 — $3.10 over 2 refutation(s)", md);
        Assert.Contains("**Indeterminate share:** 0.0 % — 0 of 25", md);
        Assert.DoesNotContain("no refutations, so this ratio does not exist", md);
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

    // --- Role keys are written and read through one set of constants --------------------------------

    /// <summary>
    /// The per-role cost dictionary is written by <see cref="BenchmarkGroupAnalysisService"/> and read
    /// by the report, under <see cref="StringComparer.Ordinal"/>. A role the report looks up by a
    /// spelling the analysis service never writes is unreachable, and the section degrades to its
    /// "not resolvable" prose while the spend it was meant to divide sits in the table one page above.
    /// </summary>
    [Fact]
    public void Report_PricesTheVerifier_WhenKeyedAsTheAnalysisServiceKeysIt()
    {
        var suite = Suite();
        var questions = Questions(50);
        var runs = new List<BenchmarkRun> { Run(1, questions, new[] { 90 }) };

        runs[0].ClaimsSupportedCount = 8;
        runs[0].ClaimsRefutedCount = 2;
        runs[0].ClaimVerifiedAnswerCount = 1;

        var result = BenchmarkGroupStatistics.Compute(suite, questions, runs, new[]
        {
            new BenchmarkGroupRunCost
            {
                RunId = 1,
                CostByRole = new Dictionary<string, double>(StringComparer.Ordinal)
                {
                    [BenchmarkGroupAnalysisService.ClaimVerifierRole] = 4.00
                }
            }
        });

        var group = new BenchmarkRunGroup { Id = 1, Name = "G", BenchmarkSuiteId = 5 };

        string md = BenchmarkGroupReportBuilder.BuildMarkdownReport(
            group, result, BenchmarkComparabilityKey.Resolve(runs), runs);

        Assert.Contains("**Cost per claim checked:** $0.4000 — $4.00 over 10 claims", md);
        Assert.Contains("**Cost per refutation:** $2.00 — $4.00 over 2 refutation(s)", md);
        Assert.DoesNotContain("the verifier's cost is not resolvable", md);
        Assert.DoesNotContain("cannot be priced", md);
    }

    /// <summary>
    /// Every role name is <see cref="BenchmarkGroupAnalysisService"/>'s to declare. A second copy of
    /// one of those strings elsewhere is the defect this guards, because the copy can disagree with
    /// the original and nothing fails until a reader notices a missing line in a report.
    /// </summary>
    [Fact]
    public void ReportBuilder_DeclaresNoRoleKeyStringOfItsOwn()
    {
        static string Fold(string s) => s.Replace(" ", string.Empty).ToLowerInvariant();

        var roleNames = typeof(BenchmarkGroupAnalysisService)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string))
            .Where(f => f.Name.EndsWith("Role", StringComparison.Ordinal))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        Assert.NotEmpty(roleNames);

        var duplicated = typeof(BenchmarkGroupReportBuilder)
            .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string))
            .Select(f => (Name: f.Name, Value: (string)f.GetRawConstantValue()!))
            .Where(f => roleNames.Any(r => Fold(r) == Fold(f.Value)))
            .ToList();

        Assert.Empty(duplicated);
    }
}
