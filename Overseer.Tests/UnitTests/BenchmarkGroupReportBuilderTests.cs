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

        Assert.Contains("# Multi-Run Benchmark Analysis", md);
        Assert.Contains("## 1. Group Manifest", md);
        Assert.Contains("## 2. Multi-Run Intelligence Index", md);
        Assert.Contains("## 3. Per-Item Statistics", md);
        Assert.Contains("## 4. Speed", md);
        Assert.Contains("## 5. Cost", md);
        Assert.Contains("What This Analysis Cannot Decompose", md);
        Assert.Contains("1.2.3", md);
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

        Assert.Contains("## 6. Paired Group Comparison", md);
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
}
