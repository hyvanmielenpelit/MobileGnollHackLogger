namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services.Benchmarking;
using Overseer.Services.Benchmarking.RootCause;
using Xunit;

/// <summary>
/// The Root Cause Analysis claim rules (32–36) in both scopes, its writer-hypothesis and observation
/// checks, and the v1 validator changes every comparison document shares: rule 16 checks the pair a
/// comparative word relates, rule 31 flags misused fact shapes, and <c>HeadlineIssues</c>.
/// </summary>
public class RootCauseValidatorTests
{
    private const BenchmarkReportAudience Rca = BenchmarkReportAudience.RootCauseAnalysis;

    private static readonly Lazy<BenchmarkComparisonReportFactsResult> ComparisonBuilt = new(RootCauseReportFixture.ComparisonFacts);
    private static readonly Lazy<BenchmarkReportFactSheet> RunSheetNoControls = new(() => RootCauseReportFixture.RunSheet(controls: false));

    private static BenchmarkReportFactSheet ComparisonSheet => ComparisonBuilt.Value.Sheet!;
    private static BenchmarkReportContentSnapshot ComparisonContent => ComparisonBuilt.Value.Content!;
    private static BenchmarkReportContentSnapshot RunContent
        => BenchmarkReportContent.Build(new[] { RootCauseReportFixture.Runs()[RootCauseReportFixture.SubjectRunId] }, 120);

    private static (BenchmarkReportFactSheet Sheet, BenchmarkReportContentSnapshot Content) Scope(bool comparison)
        => comparison ? (ComparisonSheet, ComparisonContent) : (RunSheetNoControls.Value, RunContent);

    private static BenchmarkReportWriterOutput Writer(bool comparison) => RootCauseReportFixture.Writer(Scope(comparison).Sheet, comparison);

    private static IReadOnlyList<BenchmarkReportValidationNote> Validate(bool comparison, BenchmarkReportWriterOutput output)
    {
        var (sheet, content) = Scope(comparison);
        return BenchmarkReportPackValidator.Validate(Rca, output, sheet, content);
    }

    private static string HighOrAnyHypothesisToken(BenchmarkReportFactSheet sheet)
        => "{{rca.hypothesis." + sheet.RootCause!.Hypotheses.First(h => h.Kind != "null").Key + ".statement}}";

    // --- The fixture writer ----------------------------------------------------------------------

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheFixtureWriter_BreaksNoBlockingRule(bool comparison)
    {
        var notes = Validate(comparison, Writer(comparison));

        Assert.DoesNotContain(notes, BenchmarkReportPackValidator.BlocksText);
        Assert.DoesNotContain(notes, n => n.Rule is >= 32 and <= 36);
    }

    // --- Rule 32: no causal word where only evidence belongs --------------------------------------

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Rule32_ACausalWordInAnEvidenceSlot_DropsTheParagraph(bool comparison)
    {
        var output = Writer(comparison);
        output.Sections[BenchmarkReportSlots.EvidenceSpeed] = "The speed tables split the time per question.\n\nThe model is slow because it writes long answers.";

        var notes = Validate(comparison, output);
        Assert.Contains(notes, n => n.Rule == BenchmarkReportPackValidator.RootCauseEvidenceCausalRule && n.Location.Contains(BenchmarkReportSlots.EvidenceSpeed, StringComparison.Ordinal));

        var (sheet, content) = Scope(comparison);
        var cleaned = BenchmarkReportPackValidator.DropInvalid(Rca, output, sheet, content);
        Assert.False(cleaned.Fatal);
        Assert.Equal("The speed tables split the time per question.", cleaned.Output.Sections[BenchmarkReportSlots.EvidenceSpeed]);
    }

    [Fact]
    public void Rule32_ACausalObservation_IsDropped()
    {
        var output = Writer(comparison: true);
        output.Observations!.Add(new BenchmarkReportWriterItem
        {
            Text = "The answer was wrong due to a missing tool call.",
            Questions = new List<int> { 1 },
            Evidence = new List<string> { "Q1" }
        });

        var notes = Validate(true, output);

        Assert.Contains(notes, n => n.Rule == BenchmarkReportPackValidator.RootCauseEvidenceCausalRule && n.Location.StartsWith("observations", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Output tokens drive most of the time, driven by reasoning.")]
    [InlineData("This explains the gap.")]
    [InlineData("The root cause is the decode rate.")]
    public void Rule32_EveryCausalWordCounts(string paragraph)
    {
        var output = Writer(comparison: true);
        output.Sections[BenchmarkReportSlots.EvidenceCost] = paragraph;

        Assert.Contains(Validate(true, output), n => n.Rule == BenchmarkReportPackValidator.RootCauseEvidenceCausalRule);
    }

    // --- Rule 33: a causal claim must cite a hypothesis -------------------------------------------

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Rule33_ACausalClaimWithoutAHypothesisToken_IsDropped_AndWithOneItStands(bool comparison)
    {
        var sheet = Scope(comparison).Sheet;
        var bare = Writer(comparison);
        bare.Sections[BenchmarkReportSlots.HypothesesSpeed] = "The gap is caused by the output length.";
        Assert.Contains(Validate(comparison, bare), n => n.Rule == BenchmarkReportPackValidator.RootCauseCausalClaimRule);

        var cited = Writer(comparison);
        cited.Sections[BenchmarkReportSlots.HypothesesSpeed] = "The gap is caused by the output length, as " + HighOrAnyHypothesisToken(sheet) + " reads.";
        Assert.DoesNotContain(Validate(comparison, cited), n => n.Rule == BenchmarkReportPackValidator.RootCauseCausalClaimRule);
    }

    // --- Rule 34: certainty needs a High hypothesis or an established status -----------------------

    [Fact]
    public void Rule34_AnUnsupportedCertaintyWord_IsDropped_ButANegatedOneIsNot()
    {
        var claimed = Writer(comparison: true);
        claimed.Sections[BenchmarkReportSlots.Conclusions] = claimed.Sections[BenchmarkReportSlots.Conclusions] + "\n\nThe speed cause is definitely the output length.";
        Assert.Contains(Validate(true, claimed), n => n.Rule == BenchmarkReportPackValidator.RootCauseCertaintyRule);

        var negated = Writer(comparison: true);
        negated.Sections[BenchmarkReportSlots.Conclusions] = negated.Sections[BenchmarkReportSlots.Conclusions] + "\n\nThe intelligence cause is not established {{rca.axis.intelligence.status}}.";
        Assert.DoesNotContain(Validate(true, negated), n => n.Rule == BenchmarkReportPackValidator.RootCauseCertaintyRule);
    }

    // --- Rule 35: the writer's own hypotheses -------------------------------------------------------

    [Theory]
    [InlineData("Moderate", true, true)]
    [InlineData("Low", false, true)]
    [InlineData("Low", true, false)]
    public void Rule35_AWriterHypothesisAboveLow_OrWithoutItsParts_IsDropped(string confidence, bool withRefutedIf, bool withFor)
    {
        var output = Writer(comparison: true);
        var hypothesis = output.WriterHypotheses![0];
        hypothesis.Confidence = confidence;
        if (!withRefutedIf) hypothesis.RefutedIf = string.Empty;
        if (!withFor) hypothesis.For.Clear();

        Assert.Contains(Validate(true, output), n => n.Rule == BenchmarkReportPackValidator.RootCauseWriterHypothesisRule);

        var cleaned = BenchmarkReportPackValidator.DropInvalid(Rca, output, ComparisonSheet, ComparisonContent);
        Assert.Empty(cleaned.Output.WriterHypotheses ?? new List<BenchmarkReportWriterHypothesis>());
    }

    [Fact]
    public void Rule35_AWriterHypothesisCitingAnUnknownFact_IsDropped()
    {
        var output = Writer(comparison: true);
        output.WriterHypotheses![0].Evidence.Add("rca.hypothesis.HZ9.1.statement");

        Assert.Contains(Validate(true, output), n => n.Rule == BenchmarkReportPackValidator.RootCauseWriterHypothesisRule);
    }

    [Fact]
    public void TheWriterHypotheses_AreAtMostFour()
    {
        var output = Writer(comparison: true);
        while (output.WriterHypotheses!.Count < 6) output.WriterHypotheses.Add(output.WriterHypotheses[0]);

        var cleaned = BenchmarkReportPackValidator.DropInvalid(Rca, output, ComparisonSheet, ComparisonContent);

        Assert.True(cleaned.Output.WriterHypotheses!.Count <= BenchmarkReportSlots.RootCauseMaxWriterHypotheses);
    }

    // --- Rule 36: the conclusions cite every axis' status --------------------------------------------

    [Fact]
    public void Rule36_ConclusionsWithoutAnAxisStatus_AreAWarning()
    {
        var output = Writer(comparison: true);
        output.Sections[BenchmarkReportSlots.Conclusions] = "Intelligence: {{rca.axis.intelligence.status}}. Speed: {{rca.axis.speed.status}}.";

        var notes = Validate(true, output);

        var note = Assert.Single(notes, n => n.Rule == BenchmarkReportPackValidator.RootCauseAxisStatusRule);
        Assert.True(BenchmarkReportPackValidator.IsWarningRule(note.Rule));
        Assert.False(BenchmarkReportPackValidator.BlocksText(note));
    }

    // --- Rule 4: a topic for every cited question whose text was given -------------------------------

    [Fact]
    public void Rule4_ACitedQuestionWithoutATopic_IsNoted()
    {
        var output = Writer(comparison: true);
        output.QuestionTopics.Clear();

        Assert.Contains(Validate(true, output), n => n.Rule == 4);
    }

    // --- Labels -------------------------------------------------------------------------------------

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EveryRootCauseAndEvidenceKey_HasALabelOfWords(bool comparison)
    {
        var sheet = Scope(comparison).Sheet;
        var keys = sheet.Facts.Select(f => f.Key).Where(k => k.StartsWith("rca.", StringComparison.Ordinal)
                                                               || k.Contains(".speed.", StringComparison.Ordinal)
                                                               || k.Contains("cost.component", StringComparison.Ordinal)
                                                               || k.Contains("tokens.", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(keys);

        foreach (string key in keys)
        {
            Assert.True(BenchmarkReportFactLabels.TryLabel(key, out string label), "No label for " + key);
            Assert.DoesNotContain(".", label.TrimEnd('.'));
            Assert.NotEqual(key, label);
        }
    }

    // --- Next runs: a missing configuration becomes a check -------------------------------------------

    [Fact]
    public void AThinkingPairWithNoConfiguredLevel_BecomesACheck_NotARun()
    {
        var result = ComparisonSheet.RootCause!;

        Assert.Contains(result.Checks, c => c.Kind == "configuration" && c.Text.Contains("another thinking level", StringComparison.Ordinal));
        Assert.All(result.RecommendedRuns.Where(r => r.Kind == RootCauseRunKinds.ThinkingPair), r => Assert.Equal(2, r.TestedModelConfigurationIds.Count));
        var orders = result.RecommendedRuns.Select(r => RootCausePurposes.Order(r.Purpose)).ToList();
        Assert.Equal(orders.OrderBy(o => o).ToList(), orders);
        Assert.All(result.RecommendedRuns, r => Assert.Equal(r.AfterSystemHypotheses, result.SystemFirst && r.Purpose == RootCausePurposes.ModelSelection));
    }

    // --- Caps ---------------------------------------------------------------------------------------

    [Fact]
    public void TheEvidenceSlots_AreCappedLowerInARunCompletion()
    {
        Assert.Equal(BenchmarkReportPackValidator.RootCauseEvidenceMaxWords,
            BenchmarkReportPackValidator.SlotMaxWords(Rca, BenchmarkReportSlots.EvidenceSpeed, ComparisonSheet));
        Assert.Equal(BenchmarkReportPackValidator.RootCauseCompletionEvidenceMaxWords,
            BenchmarkReportPackValidator.SlotMaxWords(Rca, BenchmarkReportSlots.EvidenceSpeed, RunSheetNoControls.Value));
        Assert.Equal(100, BenchmarkReportPackValidator.RootCauseCompletionEvidenceMaxWords);
        Assert.Equal(150, BenchmarkReportPackValidator.RootCauseEvidenceMaxWords);
    }

    // --- Rule 16: the pair a comparative word relates (v1) ------------------------------------------

    private static IReadOnlyList<BenchmarkReportValidationNote> ValidateExecutive(string meaning)
    {
        var sheet = BenchmarkReportPackFixture.ComparisonFacts().Sheet!;
        var content = BenchmarkReportPackFixture.ComparisonFacts().Content!;
        var output = BenchmarkReportPackFixture.ComparisonWriter(BenchmarkReportAudience.ExecutiveSummary, sheet);
        output.Sections[BenchmarkReportSlots.Overview] = meaning;
        return BenchmarkReportPackValidator.Validate(BenchmarkReportAudience.ExecutiveSummary, output, sheet, content);
    }

    [Fact]
    public void Rule16_FlagsAnUnhedgedRankingOfTwoOverlappingModels()
    {
        var notes = ValidateExecutive("{{model:A}} scored higher than {{model:B}} on the same questions.");

        Assert.Contains(notes, n => n.Rule == BenchmarkReportPackValidator.OverlapHedgeRule && n.Location.Contains("overview", StringComparison.Ordinal));
    }

    [Fact]
    public void Rule16_DoesNotFlagAPairNoComparativeWordRelates()
    {
        // A and C do not overlap; B appears only after "while", which ranks nothing.
        var notes = ValidateExecutive("{{model:A}} scored higher than {{model:C}} on the same questions, while {{model:B}} answered in about nine seconds.");

        Assert.DoesNotContain(notes, n => n.Rule == BenchmarkReportPackValidator.OverlapHedgeRule && n.Location.Contains("overview", StringComparison.Ordinal));
    }

    [Fact]
    public void Rule16_FallsBackToEveryPair_WhenNoModelFollowsTheComparativeWord()
    {
        var notes = ValidateExecutive("{{model:A}} and {{model:B}} scored higher on the same questions.");

        Assert.Contains(notes, n => n.Rule == BenchmarkReportPackValidator.OverlapHedgeRule && n.Location.Contains("overview", StringComparison.Ordinal));
    }

    // --- Rule 31: fact shapes (v1) -------------------------------------------------------------------

    [Theory]
    [InlineData("{{model:A}} took {{pair.A.B.speed.ratio}}.")]
    [InlineData("The panel agreed ({{notable.1.summary}})")]
    [InlineData("{{model:B}} answered {{model.B.vsReference.better}}, {{model.B.vsReference.similar}}, {{model.B.vsReference.worse}}.")]
    [InlineData("{{notable.1.summary}} because the panel agreed.")]
    public void Rule31_FlagsMisusedFactShapes_AsAWarning(string paragraph)
    {
        var notes = ValidateExecutive(paragraph);

        var note = Assert.Single(notes, n => n.Rule == BenchmarkReportPackValidator.ComparisonTokenShapeRule);
        Assert.True(BenchmarkReportPackValidator.IsWarningRule(note.Rule));
        Assert.False(note.Dropped);
    }

    [Theory]
    [InlineData("{{notable.1.summary}} The rest is in the table.")]
    [InlineData("While {{pair.A.B.speed.ratio}}, the costs differ.")]
    [InlineData("Questions answered better than the reference: {{model.B.vsReference.better}}; worse: {{model.B.vsReference.worse}}.")]
    [InlineData("The figures, e.g. the index, are in the table.")]
    public void Rule31_LeavesWellShapedFactsAlone(string paragraph)
    {
        Assert.DoesNotContain(ValidateExecutive(paragraph), n => n.Rule == BenchmarkReportPackValidator.ComparisonTokenShapeRule);
    }

    // --- HeadlineIssues (v1) ---------------------------------------------------------------------------

    [Fact]
    public void HeadlineIssues_BlocksAHeadlineOfThirtySixWords_AndPassesThirtyFive()
    {
        var sheet = BenchmarkReportPackFixture.ComparisonFacts().Sheet!;
        var content = BenchmarkReportPackFixture.ComparisonFacts().Content!;
        string Words(int n) => string.Join(" ", Enumerable.Repeat("word", n)) + ".";

        var tooLong = BenchmarkReportPackValidator.HeadlineIssues(BenchmarkReportAudience.ExecutiveSummary, Words(36), sheet, content);
        Assert.Contains(tooLong, n => n.Rule == 7 && BenchmarkReportPackValidator.BlocksText(n));

        var fits = BenchmarkReportPackValidator.HeadlineIssues(BenchmarkReportAudience.ExecutiveSummary, Words(35), sheet, content);
        Assert.DoesNotContain(fits, BenchmarkReportPackValidator.BlocksText);
    }

    [Fact]
    public void AnOverlongHeadline_IsTheOnlyFatalCause_AndSaysSo()
    {
        var sheet = BenchmarkReportPackFixture.ComparisonFacts().Sheet!;
        var content = BenchmarkReportPackFixture.ComparisonFacts().Content!;
        var output = BenchmarkReportPackFixture.ComparisonWriter(BenchmarkReportAudience.ExecutiveSummary, sheet);
        output.Headline = string.Join(" ", Enumerable.Repeat("word", 40)) + ".";

        var cleaned = BenchmarkReportPackValidator.DropInvalid(BenchmarkReportAudience.ExecutiveSummary, output, sheet, content);

        Assert.True(cleaned.Fatal);
        Assert.True(cleaned.HeadlineOnlyFatal);
    }
}
