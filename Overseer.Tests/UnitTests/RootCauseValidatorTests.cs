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
/// The Root Cause Analysis claim rules (32–36) in both scopes, among them prove as a certainty word and a price
/// effect that supports a cause but no certainty, the hypothesis references as bare ids, its writer-hypothesis and
/// observation checks, and the v1 validator changes every comparison document shares: rule 16 checks the pair a
/// comparative word relates, rule 31 flags misused fact shapes, rule 37 flags bare model letters, and
/// <c>HeadlineIssues</c>.
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

    private static string SupportedHypothesisToken(BenchmarkReportFactSheet sheet)
        => "{{rca.hypothesis." + sheet.RootCause!.Hypotheses
            .First(h => h.Kind != "null" && RootCauseGrades.Rank(h.Grade) >= RootCauseGrades.Rank(RootCauseGrades.Low)).Key + ".statement}}";

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
        cited.Sections[BenchmarkReportSlots.HypothesesSpeed] = "The gap may be caused by the output length, as " + SupportedHypothesisToken(sheet) + " reads.";
        Assert.DoesNotContain(Validate(comparison, cited), n => n.Rule == BenchmarkReportPackValidator.RootCauseCausalClaimRule);
    }

    // --- Sentence salvage of a required slot ------------------------------------------------------

    private const string UncitedCause = "Every speed hypothesis is graded Moderate because each model ran once per suite.";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ARequiredSlotsOnlyParagraph_LosesItsFaultySentenceAlone(bool comparison)
    {
        var (sheet, content) = Scope(comparison);
        var output = Writer(comparison);
        string clean = output.Sections[BenchmarkReportSlots.HypothesesSpeed];
        output.Sections[BenchmarkReportSlots.HypothesesSpeed] = clean + " " + UncitedCause + " The next runs add replicates.";

        var cleaned = BenchmarkReportPackValidator.DropInvalid(Rca, output, sheet, content);

        Assert.False(cleaned.Fatal, cleaned.FatalReason);
        Assert.Equal(clean + " The next runs add replicates.", cleaned.Output.Sections[BenchmarkReportSlots.HypothesesSpeed]);
        var location = "sections." + BenchmarkReportSlots.HypothesesSpeed + "[p1]";
        var removed = Assert.Single(cleaned.Notes, n => n.Dropped && n.Location == location);
        Assert.True(removed.SentenceOnly);
        Assert.Equal(BenchmarkReportPackValidator.RootCauseCausalClaimRule, removed.Rule);
        Assert.Equal(UncitedCause, removed.Text);
        Assert.EndsWith(" Only this sentence was removed, so the section kept its paragraph.", removed.Message);
    }

    [Fact]
    public void ARequiredSlotWhoseEverySentenceFails_StillFailsTheDocument()
    {
        var output = Writer(comparison: true);
        output.Sections[BenchmarkReportSlots.HypothesesSpeed] = UncitedCause + " The gap is caused by the output length.";

        var cleaned = BenchmarkReportPackValidator.DropInvalid(Rca, output, ComparisonSheet, ComparisonContent);

        Assert.True(cleaned.Fatal);
        Assert.Equal($"Every paragraph of the required slot \"{BenchmarkReportSlots.HypothesesSpeed}\" was dropped.", cleaned.FatalReason);
        Assert.DoesNotContain(cleaned.Notes, n => n.SentenceOnly);
    }

    [Fact]
    public void ARequiredSlotWithAnotherCleanParagraph_StillDropsTheFaultyParagraphWhole()
    {
        var output = Writer(comparison: true);
        string clean = output.Sections[BenchmarkReportSlots.HypothesesSpeed];
        output.Sections[BenchmarkReportSlots.HypothesesSpeed] = clean + "\n\n" + UncitedCause + " The next runs add replicates.";

        var cleaned = BenchmarkReportPackValidator.DropInvalid(Rca, output, ComparisonSheet, ComparisonContent);

        Assert.False(cleaned.Fatal);
        Assert.Equal(clean, cleaned.Output.Sections[BenchmarkReportSlots.HypothesesSpeed]);
        Assert.Contains(cleaned.Notes, n => n.Dropped && n.Location == "sections." + BenchmarkReportSlots.HypothesesSpeed + "[p2]");
        Assert.DoesNotContain(cleaned.Notes, n => n.SentenceOnly);
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

    // --- Next runs: a thinking-level pair runs one model at two levels; a missing level becomes a check ------

    [Fact]
    public void AThinkingPair_RunsOneModelAtTwoLevels_ServesNoCrossModelHypothesis_AndAMissingLevelIsACheck()
    {
        var result = ComparisonSheet.RootCause!;
        var hypotheses = result.Hypotheses.ToDictionary(h => h.Key, StringComparer.Ordinal);
        var modelIds = result.Models.ToDictionary(m => m.Token, m => m.ModelId, StringComparer.Ordinal);

        foreach (var run in result.RecommendedRuns.Where(r => r.Kind == RootCauseRunKinds.ThinkingPair))
        {
            Assert.Equal(2, run.TestedModelConfigurationIds.Count);
            Assert.All(run.HypothesisIds, key => Assert.Single(hypotheses[key].Models.Select(t => modelIds[t]).Distinct(StringComparer.OrdinalIgnoreCase)));
        }
        Assert.All(result.Checks.Where(c => c.Text.Contains("another thinking level", StringComparison.Ordinal)), c => Assert.Equal("configuration", c.Kind));
        Assert.DoesNotContain(result.RecommendedRuns, r => r.Kind == RootCauseRunKinds.StylePair && r.Mark != RootCauseMarks.Optional);
        Assert.All(result.RecommendedRuns, r => Assert.Equal(r.Decides, string.Join(" ", r.DecidesItems!)));
        var orders = result.RecommendedRuns.Select(r => RootCausePurposes.Order(r.Purpose)).ToList();
        Assert.Equal(orders.OrderBy(o => o).ToList(), orders);
        Assert.All(result.RecommendedRuns, r => Assert.Equal(r.AfterSystemHypotheses, result.SystemFirst && r.Purpose == RootCausePurposes.ModelSelection));
    }

    // --- Caps ---------------------------------------------------------------------------------------

    [Fact]
    public void TheEvidenceSlots_AreCappedAtAHundredWords_InEveryScope()
    {
        Assert.Equal(BenchmarkReportPackValidator.RootCauseEvidenceMaxWords,
            BenchmarkReportPackValidator.SlotMaxWords(Rca, BenchmarkReportSlots.EvidenceSpeed, ComparisonSheet));
        Assert.Equal(BenchmarkReportPackValidator.RootCauseCompletionEvidenceMaxWords,
            BenchmarkReportPackValidator.SlotMaxWords(Rca, BenchmarkReportSlots.EvidenceSpeed, RunSheetNoControls.Value));
        Assert.Equal(100, BenchmarkReportPackValidator.RootCauseCompletionEvidenceMaxWords);
        Assert.Equal(100, BenchmarkReportPackValidator.RootCauseEvidenceMaxWords);
    }

    [Theory]
    [InlineData(BenchmarkReportSlots.HypothesesIntelligence, 120)]
    [InlineData(BenchmarkReportSlots.Conclusions, 120)]
    [InlineData(BenchmarkReportSlots.NextRuns, 50)]
    public void TheSlotCaps_AreTheirWordLimits(string slot, int cap)
    {
        Assert.Equal(cap, BenchmarkReportPackValidator.RootCauseSlotMaxWords(slot, completion: false));
        Assert.Equal(cap, BenchmarkReportPackValidator.RootCauseSlotMaxWords(slot, completion: true));
    }

    // --- Answer slots, run tokens and labels (sheets built by hand) ---------------------------------

    private static BenchmarkReportFact TextFact(string key, string value, string? display = null)
        => new() { Key = key, Value = System.Text.Json.Nodes.JsonValue.Create(value), Display = display ?? value };

    /// <summary>
    /// A comparison-scope Root Cause Analysis sheet of two models asking <paramref name="questions"/>: three
    /// axes not established, a Moderate hypothesis, a High one, a Low one, a Not supported one and a High price
    /// effect (each with a statement, a label and a reference that is its id alone), and one next run.
    /// </summary>
    private static BenchmarkReportFactSheet QuestionSheet(params int[] questions)
    {
        var facts = new List<BenchmarkReportFact>
        {
            TextFact("rca.axis.cost.status", RootCauseStatuses.NotEstablished, "Not established"),
            TextFact("rca.axis.intelligence.status", RootCauseStatuses.NotEstablished, "Not established"),
            TextFact("rca.axis.speed.status", RootCauseStatuses.NotEstablished, "Not established"),
            TextFact("rca.hypothesis.HC1.1.grade", RootCauseGrades.Low),
            TextFact("rca.hypothesis.HC1.1.label", "higher output price of Model B"),
            TextFact("rca.hypothesis.HC1.1.ref", "HC1.1"),
            TextFact("rca.hypothesis.HC1.1.statement", "Model B pays a higher output price than Model A."),
            TextFact("rca.hypothesis.HC2.1.grade", RootCauseGrades.High, RootCauseGrades.PriceCardWordWithGrade(RootCauseGrades.High)),
            TextFact("rca.hypothesis.HC2.1.label", "Model B's higher price per token, compared with Model A"),
            new() { Key = "rca.hypothesis.HC2.1.priceCard", Value = System.Text.Json.Nodes.JsonValue.Create(true), Display = RootCauseFacts.PriceCardDisplay },
            TextFact("rca.hypothesis.HC2.1.ref", "HC2.1"),
            TextFact("rca.hypothesis.HC2.1.statement", "Model B costs more than Model A mainly from its price per token."),
            TextFact("rca.hypothesis.HQ1.1.grade", RootCauseGrades.Moderate),
            TextFact("rca.hypothesis.HQ1.1.label", "critical errors of Model B against Model A"),
            TextFact("rca.hypothesis.HQ1.1.ref", "HQ1.1"),
            TextFact("rca.hypothesis.HQ1.1.statement", "Model B made more critical errors than Model A."),
            TextFact("rca.hypothesis.HS1.1.grade", RootCauseGrades.High),
            TextFact("rca.hypothesis.HS1.1.label", "longer outputs of Model B"),
            TextFact("rca.hypothesis.HS1.1.ref", "HS1.1"),
            TextFact("rca.hypothesis.HS1.1.statement", "Model B writes longer answers than Model A."),
            TextFact("rca.hypothesis.HS4.1.grade", RootCauseGrades.NotSupported, RootCauseGrades.Display(RootCauseGrades.NotSupported)),
            TextFact("rca.hypothesis.HS4.1.label", "slower first token of Model B"),
            TextFact("rca.hypothesis.HS4.1.ref", "HS4.1"),
            TextFact("rca.hypothesis.HS4.1.statement", "Model B waits longer for its first token than Model A."),
            TextFact("rca.run.1.summary", "a replicate of every model"),
        };
        foreach (int q in questions)
        {
            facts.Add(TextFact("rca.question." + q + ".text", "why this model stands where it does"));
            facts.Add(TextFact("rca.question." + q + ".status", "Probable"));
        }

        return new BenchmarkReportFactSheet
        {
            Scope = BenchmarkReportFactSheet.ComparisonScopeValue,
            SubjectKey = "comparison:1",
            SubjectLabel = "Comparison #1",
            Peers = new[] { "A", "B" }
                .Select((l, i) => new BenchmarkReportPeer { Letter = l, EntryKey = "run:" + (50 + i), Label = "Orion " + l, Provider = "Northwind", State = "Comparable" })
                .ToList(),
            Facts = facts.OrderBy(f => f.Key, StringComparer.Ordinal).ToList(),
        };
    }

    private static IReadOnlyList<BenchmarkReportValidationNote> ValidateSections(BenchmarkReportFactSheet sheet, IReadOnlyDictionary<string, string> sections)
    {
        var output = new BenchmarkReportWriterOutput { Headline = "The models differ on every axis." };
        foreach (var (slot, text) in sections) output.Sections[slot] = text;
        return BenchmarkReportPackValidator.Validate(Rca, output, sheet, new BenchmarkReportContentSnapshot());
    }

    [Fact]
    public void Rule33_ARunTokenSupportsACausalWord_InNextRunsOnly()
    {
        const string sentence = "Make this run first because it settles the grading: {{rca.run.1.summary}}.";
        var notes = ValidateSections(QuestionSheet(), new Dictionary<string, string>
        {
            [BenchmarkReportSlots.NextRuns] = sentence,
            [BenchmarkReportSlots.Conclusions] = sentence,
        });

        Assert.DoesNotContain(notes, n => n.Rule == BenchmarkReportPackValidator.RootCauseCausalClaimRule && n.Location.StartsWith("sections.nextRuns", StringComparison.Ordinal));
        Assert.Contains(notes, n => n.Rule == BenchmarkReportPackValidator.RootCauseCausalClaimRule && n.Location.StartsWith("sections.conclusions", StringComparison.Ordinal));
    }

    // --- Rule 33: the grade a cause rests on, hedges and negations ------------------------------------

    private static List<BenchmarkReportValidationNote> CausalClaimNotes(string slot, string paragraph)
        => ValidateSections(QuestionSheet(), new Dictionary<string, string> { [slot] = paragraph })
            .Where(n => n.Rule == BenchmarkReportPackValidator.RootCauseCausalClaimRule && n.Location.StartsWith("sections." + slot, StringComparison.Ordinal))
            .ToList();

    [Theory]
    [InlineData("The speed gap is due to {{rca.hypothesis.HS4.1.label}}.", false)]
    [InlineData("The speed gap is not due to {{rca.hypothesis.HS4.1.label}}.", true)]
    [InlineData("The speed gap cannot be caused by {{rca.hypothesis.HS4.1.label}}.", true)]
    [InlineData("The speed gap widens not only because of {{rca.hypothesis.HS4.1.label}}.", false)]
    [InlineData("The speed gap is due to {{rca.hypothesis.HQ1.1.label}}.", true)]
    [InlineData("The speed gap is due to {{rca.hypothesis.HC1.1.label}}.", false)]
    [InlineData("The speed gap may be due to {{rca.hypothesis.HC1.1.label}}.", true)]
    [InlineData("The speed gap is consistent with, and possibly due to, {{rca.hypothesis.HC1.1.label}}.", true)]
    [InlineData("The speed gap is unlikely to be due to anything but {{rca.hypothesis.HC1.1.label}}.", false)]
    [InlineData("The speed gap is due to {{rca.hypothesis.HC1.1.label}} and {{rca.hypothesis.HQ1.1.label}}.", true)]
    [InlineData("Per {{rca.axis.speed.status}}, the speed gap is due to the output length.", true)]
    [InlineData("The speed gap is due to {{rca.hypothesis.HQ1.1.ref}}.", true)]
    [InlineData("The speed gap is due to {{rca.hypothesis.HS4.1.ref}}.", false)]
    [InlineData("The speed gap is not due to {{rca.hypothesis.HS4.1.ref}}.", true)]
    [InlineData("The speed gap is due to {{rca.hypothesis.HC1.1.ref}}.", false)]
    [InlineData("The speed gap may be due to {{rca.hypothesis.HC1.1.ref}}.", true)]
    public void Rule33_ACauseNeedsAHypothesisGradedLowOrHigher_HedgedWhenLow_AndARuledOutCauseMayCiteAnyHypothesis(string paragraph, bool kept)
    {
        var notes = CausalClaimNotes(BenchmarkReportSlots.HypothesesSpeed, paragraph);

        if (kept) Assert.Empty(notes);
        else Assert.NotEmpty(notes);
    }

    [Fact]
    public void Rule33_ACauseOnANotSupportedHypothesis_AndAnUnhedgedCauseOnALowOne_AreDropped_WithTheirNotes()
    {
        const string refuted = "The speed gap is due to {{rca.hypothesis.HS4.1.label}}.";
        const string unhedged = "The cost gap is due to {{rca.hypothesis.HC1.1.label}}.";
        const string measured = "The cost tables split the cost per question.";
        const string hedged = "The intelligence gap may be due to {{rca.hypothesis.HC1.1.label}}.";
        var sheet = QuestionSheet();
        var output = new BenchmarkReportWriterOutput { Headline = "The models differ on every axis." };
        output.Sections[BenchmarkReportSlots.HypothesesSpeed] = refuted;
        output.Sections[BenchmarkReportSlots.HypothesesCost] = unhedged + "\n\n" + measured;
        output.Sections[BenchmarkReportSlots.HypothesesIntelligence] = hedged;

        var notes = BenchmarkReportPackValidator.Validate(Rca, output, sheet, new BenchmarkReportContentSnapshot())
            .Where(n => n.Rule == BenchmarkReportPackValidator.RootCauseCausalClaimRule)
            .ToList();

        var speed = Assert.Single(notes, n => n.Location.StartsWith("sections." + BenchmarkReportSlots.HypothesesSpeed, StringComparison.Ordinal));
        Assert.Contains("without a hypothesis graded Low or higher in the same sentence", speed.Message, StringComparison.Ordinal);
        Assert.Contains("A Not supported hypothesis may be cited only to rule a cause out", speed.Message, StringComparison.Ordinal);
        var cost = Assert.Single(notes, n => n.Location.StartsWith("sections." + BenchmarkReportSlots.HypothesesCost, StringComparison.Ordinal));
        Assert.Contains("on a hypothesis graded Low only", cost.Message, StringComparison.Ordinal);
        Assert.Contains("a Low hypothesis is a lead", cost.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(notes, n => n.Location.StartsWith("sections." + BenchmarkReportSlots.HypothesesIntelligence, StringComparison.Ordinal));

        var cleaned = BenchmarkReportPackValidator.DropInvalid(Rca, output, sheet, new BenchmarkReportContentSnapshot());
        Assert.Equal(measured, cleaned.Output.Sections[BenchmarkReportSlots.HypothesesCost]);
        Assert.Equal(hedged, cleaned.Output.Sections[BenchmarkReportSlots.HypothesesIntelligence]);
    }

    [Fact]
    public void Rule32_ANegatedCausalWordInAnEvidenceSlot_IsStillDropped()
    {
        var notes = ValidateSections(QuestionSheet(), new Dictionary<string, string>
        {
            [BenchmarkReportSlots.EvidenceSpeed] = "The speed gap is not due to {{rca.hypothesis.HS1.1.label}}."
        });

        Assert.Contains(notes, n => n.Rule == BenchmarkReportPackValidator.RootCauseEvidenceCausalRule
                                    && n.Location.StartsWith("sections." + BenchmarkReportSlots.EvidenceSpeed, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("The gap is not due to the price.", "due to", true)]
    [InlineData("It never leads to a lower score.", "leads to", true)]
    [InlineData("The gap cannot be caused by the price.", "caused", true)]
    [InlineData("It has not been caused by the price.", "caused", true)]
    [InlineData("The price is not the root cause.", "root cause", true)]
    [InlineData("The gap widens not only because of the price.", "because", false)]
    [InlineData("The gap is due to the price.", "due to", false)]
    public void TheCausalPhraseReaders_TellARuledOutCauseFromAStatedOne(string plain, string phrase, bool negated)
    {
        var none = Array.Empty<string>();

        Assert.Equal(new[] { phrase }, RootCauseClaimSupport.CausalPhrases(plain));
        Assert.Equal(negated ? new[] { phrase } : none, RootCauseClaimSupport.NegatedCausalPhrases(plain));
        Assert.Equal(negated ? none : new[] { phrase }, RootCauseClaimSupport.UnnegatedCausalPhrases(plain));
    }

    [Theory]
    [InlineData("The gap may be due to the price.", true)]
    [InlineData("It is consistent with the price.", true)]
    [InlineData("The price is unlikely to matter.", false)]
    [InlineData("The price matters.", false)]
    public void TheHedgeWords_MatchAsWholeWords(string plain, bool hedged)
    {
        Assert.Equal(hedged, RootCauseClaimSupport.HedgePhrases(plain).Any());
    }

    [Fact]
    public void Rule31_ALabelToken_MayStandInsideASentence()
    {
        const string slot = BenchmarkReportSlots.HypothesesIntelligence;
        var labeled = ValidateSections(QuestionSheet(), new Dictionary<string, string>
        {
            [slot] = "Part of the gap is due to {{rca.hypothesis.HQ1.1.label}} on the hard questions."
        }).Where(n => n.Location.StartsWith("sections." + slot, StringComparison.Ordinal)).ToList();

        Assert.DoesNotContain(labeled, n => n.Rule == BenchmarkReportPackValidator.ComparisonTokenShapeRule);
        Assert.DoesNotContain(labeled, n => n.Rule == BenchmarkReportPackValidator.RootCauseCausalClaimRule);

        // The reference is a noun phrase too.
        var referenced = ValidateSections(QuestionSheet(), new Dictionary<string, string>
        {
            [slot] = "Part of the gap is due to {{rca.hypothesis.HQ1.1.ref}} on the hard questions."
        }).Where(n => n.Location.StartsWith("sections." + slot, StringComparison.Ordinal)).ToList();
        Assert.DoesNotContain(referenced, n => n.Rule == BenchmarkReportPackValidator.ComparisonTokenShapeRule);
        Assert.DoesNotContain(referenced, n => n.Rule == BenchmarkReportPackValidator.RootCauseCausalClaimRule);

        // The statement is a sentence: inside another one it is still flagged.
        var stated = ValidateSections(QuestionSheet(), new Dictionary<string, string>
        {
            [slot] = "Part of the gap is due to {{rca.hypothesis.HQ1.1.statement}} on the hard questions."
        });
        Assert.Contains(stated, n => n.Rule == BenchmarkReportPackValidator.ComparisonTokenShapeRule && n.Location.StartsWith("sections." + slot, StringComparison.Ordinal));

        // A High hypothesis' label supports a certainty word as its other tokens do; a Moderate one's does not.
        var certain = ValidateSections(QuestionSheet(), new Dictionary<string, string>
        {
            [BenchmarkReportSlots.HypothesesSpeed] = "The speed gap is certainly due to {{rca.hypothesis.HS1.1.label}}.",
            [BenchmarkReportSlots.HypothesesCost] = "The cost gap is certainly due to {{rca.hypothesis.HQ1.1.label}}.",
        });
        Assert.DoesNotContain(certain, n => n.Rule == BenchmarkReportPackValidator.RootCauseCertaintyRule && n.Location.StartsWith("sections.hypothesesSpeed", StringComparison.Ordinal));
        Assert.Contains(certain, n => n.Rule == BenchmarkReportPackValidator.RootCauseCertaintyRule && n.Location.StartsWith("sections.hypothesesCost", StringComparison.Ordinal));
    }

    [Fact]
    public void AFilledAnswerSlot_IsAnUnknownSlot_AndASheetWithQuestionsRequiresNone()
    {
        var sheet = QuestionSheet(1, 3);
        sheet.Facts.Add(TextFact("rca.question.1.signals", "tied for first of 2 models on Accuracy"));

        // The sheet asks two research questions, and no answer slot is required.
        var empty = ValidateSections(sheet, new Dictionary<string, string>());
        foreach (string slot in BenchmarkReportSlots.RootCauseAnswerSlots)
        {
            Assert.DoesNotContain(empty, n => n.Location.StartsWith("sections." + slot, StringComparison.Ordinal));
        }

        // A filled answer slot is an unknown slot (rule 1), whatever it cites, and is dropped.
        var sections = new Dictionary<string, string>
        {
            [BenchmarkReportSlots.AnswerBest] = "It makes fewer critical errors ({{rca.hypothesis.HQ1.1.ref}}): {{rca.question.1.signals}}.",
            [BenchmarkReportSlots.AnswerSpeed] = "It is certainly slower through {{rca.hypothesis.HS1.1.label}}.",
        };
        var notes = ValidateSections(sheet, sections);
        foreach (string slot in sections.Keys)
        {
            var note = Assert.Single(notes, n => n.Location.StartsWith("sections." + slot, StringComparison.Ordinal));
            Assert.Equal(1, note.Rule);
            Assert.StartsWith("\"" + slot + "\" is not a slot of the ", note.Message, StringComparison.Ordinal);
        }
        Assert.DoesNotContain(notes, n => n.Rule == BenchmarkReportPackValidator.RootCauseCodePrintedRule);

        var output = new BenchmarkReportWriterOutput { Headline = "The models differ on every axis." };
        foreach (var (slot, text) in sections) output.Sections[slot] = text;
        var cleaned = BenchmarkReportPackValidator.DropInvalid(Rca, output, sheet, new BenchmarkReportContentSnapshot());
        Assert.DoesNotContain(cleaned.Output.Sections.Keys, k => BenchmarkReportSlots.RootCauseAnswerSlots.Contains(k));
    }

    // --- Rule 34: prove is a certainty word; a price effect supports none ----------------------------

    private static List<BenchmarkReportValidationNote> CertaintyNotes(string slot, string paragraph)
        => ValidateSections(QuestionSheet(), new Dictionary<string, string> { [slot] = paragraph })
            .Where(n => n.Rule == BenchmarkReportPackValidator.RootCauseCertaintyRule && n.Location.StartsWith("sections." + slot, StringComparison.Ordinal))
            .ToList();

    [Theory]
    [InlineData("The configuration proves that the cost gap comes from {{rca.hypothesis.HQ1.1.label}}.", false)]
    [InlineData("The runs prove that the cost gap comes from {{rca.hypothesis.HQ1.1.label}}.", false)]
    [InlineData("The runs proved that the cost gap comes from {{rca.hypothesis.HQ1.1.label}}.", false)]
    [InlineData("The runs do not prove that the cost gap comes from {{rca.hypothesis.HQ1.1.label}}.", true)]
    [InlineData("The runs prove that the speed gap comes from {{rca.hypothesis.HS1.1.label}}.", true)]
    public void Rule34_ProveIsACertaintyWord_UnlessNegated(string paragraph, bool kept)
    {
        var notes = CertaintyNotes(BenchmarkReportSlots.Conclusions, paragraph);

        if (kept) Assert.Empty(notes);
        else Assert.NotEmpty(notes);
        Assert.Contains("proves", RootCauseClaimSupport.CertaintyWords);
    }

    [Fact]
    public void Rule34_APriceEffectGradedHigh_SupportsACauseButNoCertainty()
    {
        var support = RootCauseClaimSupport.From(QuestionSheet());
        Assert.True(support.SupportsCausal("rca.hypothesis.HC2.1.label"));
        Assert.False(support.SupportsCertainty("rca.hypothesis.HC2.1.label"));
        Assert.True(support.SupportsCertainty("rca.hypothesis.HS1.1.label"));
        Assert.DoesNotContain("rca.hypothesis.HC2.1.*", support.CertaintySupport());
        Assert.Contains("rca.hypothesis.HC2.1.*", support.CausalSupport());

        const string slot = BenchmarkReportSlots.HypothesesCost;
        var certain = ValidateSections(QuestionSheet(), new Dictionary<string, string>
        {
            [slot] = "The cost gap is certainly due to {{rca.hypothesis.HC2.1.label}}."
        }).Where(n => n.Location.StartsWith("sections." + slot, StringComparison.Ordinal)).ToList();
        Assert.Contains(certain, n => n.Rule == BenchmarkReportPackValidator.RootCauseCertaintyRule);
        Assert.DoesNotContain(certain, n => n.Rule == BenchmarkReportPackValidator.RootCauseCausalClaimRule);

        // A causal sentence on the price effect, or beside the cost axis' status, needs no certainty and stands.
        Assert.Empty(CausalClaimNotes(slot, "The cost gap is due to {{rca.hypothesis.HC2.1.ref}}, which the price cards show."));
        Assert.Empty(CertaintyNotes(slot, "The cost gap is due to {{rca.hypothesis.HC2.1.ref}}, which the price cards show."));
        Assert.Empty(CausalClaimNotes(slot, "Per {{rca.axis.cost.status}}, the cost gap is due to the price per token."));
        Assert.Empty(CertaintyNotes(slot, "Per {{rca.axis.cost.status}}, the cost gap is due to the price per token."));
    }

    [Fact]
    public void AHypothesisReference_IsItsIdAlone_AndStandsInsideASentence()
    {
        var sheet = QuestionSheet();
        Assert.All(sheet.Facts.Where(f => f.Key.EndsWith(".ref", StringComparison.Ordinal)), f =>
            Assert.Equal(f.Key["rca.hypothesis.".Length..^".ref".Length], f.Display));

        const string slot = BenchmarkReportSlots.HypothesesSpeed;
        var notes = ValidateSections(sheet, new Dictionary<string, string>
        {
            [slot] = "Longer outputs may explain the gap, as {{rca.hypothesis.HS1.1.ref}} finds."
        }).Where(n => n.Location.StartsWith("sections." + slot, StringComparison.Ordinal)).ToList();
        Assert.DoesNotContain(notes, n => n.Rule == BenchmarkReportPackValidator.ComparisonTokenShapeRule);
        Assert.DoesNotContain(notes, n => n.Rule == BenchmarkReportPackValidator.RootCauseCausalClaimRule);

        // The engine's own sheet: every reference is the hypothesis' key.
        var comparison = ComparisonSheet;
        Assert.All(comparison.RootCause!.Hypotheses, h =>
            Assert.Equal(h.Key, comparison.Facts.Single(f => f.Key == "rca.hypothesis." + h.Key + ".ref").Display));
    }

    // --- Rule 37: no bare model letter in a comparison -------------------------------------------------

    [Fact]
    public void Rule37_FlagsABareModelLetter_InComparisonScope_AsAWarning()
    {
        const string slot = BenchmarkReportSlots.HypothesesIntelligence;
        var notes = ValidateSections(QuestionSheet(), new Dictionary<string, string>
        {
            [slot] = "Models H and I lose Accuracy on the hard questions."
        });

        var note = Assert.Single(notes, n => n.Rule == BenchmarkReportPackValidator.BareModelLetterRule);
        Assert.StartsWith("sections." + slot, note.Location, StringComparison.Ordinal);
        Assert.Contains("Models H", note.Message, StringComparison.Ordinal);
        Assert.True(BenchmarkReportPackValidator.IsWarningRule(note.Rule));
        Assert.False(BenchmarkReportPackValidator.BlocksText(note));
    }

    [Fact]
    public void Rule37_LeavesModelTokens_Alone()
    {
        var notes = ValidateSections(QuestionSheet(), new Dictionary<string, string>
        {
            [BenchmarkReportSlots.HypothesesIntelligence] = "{{model:A}} and {{model:B}} lose Accuracy on the hard questions."
        });

        Assert.DoesNotContain(notes, n => n.Rule == BenchmarkReportPackValidator.BareModelLetterRule);
    }

    [Fact]
    public void Rule37_DoesNotApply_OutsideComparisonScope()
    {
        var output = Writer(comparison: false);
        output.Sections[BenchmarkReportSlots.HypothesesIntelligence] = "Models H and I lose Accuracy on the hard questions.";

        Assert.DoesNotContain(Validate(false, output), n => n.Rule == BenchmarkReportPackValidator.BareModelLetterRule);
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

    /// <summary>The rule 16 notes of an overview paragraph on the fixture comparison, where A and B overlap.</summary>
    private static List<BenchmarkReportValidationNote> OverviewRankingNotes(string overview)
        => ValidateExecutive(overview)
            .Where(n => n.Rule == BenchmarkReportPackValidator.OverlapHedgeRule && n.Location.Contains("overview", StringComparison.Ordinal))
            .ToList();

    [Fact]
    public void Rule16_AComparativeWithNoModelBeforeIt_RanksNothing()
    {
        Assert.Empty(OverviewRankingNotes(
            "In the speed results, models at higher thinking settings made many tool calls on hard questions, such as {{model:A}} on Q1 and {{model:B}} on Q2."));
    }

    [Fact]
    public void Rule16_ASettingComparative_RanksNothing()
    {
        Assert.Empty(OverviewRankingNotes("{{model:A}} ran at a higher thinking level than {{model:B}}."));
        Assert.Empty(OverviewRankingNotes("{{model:A}} ran at a higher-priced tier than {{model:B}}."));
    }

    [Fact]
    public void Rule16_AMechanismThatLeadsTheAxis_RanksNothing()
    {
        Assert.Empty(OverviewRankingNotes("Answering without tools leads the intelligence axis for {{model:B}} against {{model:A}}."));
    }

    [Fact]
    public void Rule16_ARankingWithItsSubjectAfterACompareClause_IsStillRead()
    {
        var note = Assert.Single(OverviewRankingNotes("Compared with {{model:B}}, {{model:A}} scored higher."));

        Assert.Contains("Ranks {{model:A}} against {{model:B}}, whose intervals overlap", note.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rule16_SpeedIsASpeedWord()
    {
        Assert.Contains("speed", BenchmarkReportPackValidator.SpeedWords);
        Assert.DoesNotContain(OverviewRankingNotes("{{model:A}} ranks higher on speed than {{model:B}}."),
            n => n.Message.Contains("intervals overlap", StringComparison.Ordinal));
    }

    // --- Rule 31: fact shapes (v1) -------------------------------------------------------------------

    [Theory]
    [InlineData("{{model:A}} took {{pair.A.B.speed.ratio}}.")]
    [InlineData("The panel agreed ({{notable.1.summary}})")]
    [InlineData("{{notable.1.summary}} because the panel agreed.")]
    public void Rule31_FlagsMisusedFactShapes_AsAWarning(string paragraph)
    {
        var notes = ValidateExecutive(paragraph);

        var note = Assert.Single(notes, n => n.Rule == BenchmarkReportPackValidator.ComparisonTokenShapeRule);
        Assert.True(BenchmarkReportPackValidator.IsWarningRule(note.Rule));
        Assert.False(note.Dropped);
    }

    /// <summary>The fixture comparison's Executive Summary with <paramref name="overview"/>, its sheet changed first by <paramref name="change"/>.</summary>
    private static IReadOnlyList<BenchmarkReportValidationNote> ValidateExecutive(string overview, Action<BenchmarkReportFactSheet> change)
    {
        var sheet = BenchmarkReportPackFixture.ComparisonFacts().Sheet!;
        change(sheet);
        var content = BenchmarkReportPackFixture.ComparisonFacts().Content!;
        var output = BenchmarkReportPackFixture.ComparisonWriter(BenchmarkReportAudience.ExecutiveSummary, sheet);
        output.Sections[BenchmarkReportSlots.Overview] = overview;
        return BenchmarkReportPackValidator.Validate(BenchmarkReportAudience.ExecutiveSummary, output, sheet, content);
    }

    [Fact]
    public void Rule31_FlagsARunOfCounts_OnlyWhereOneOfThemIsBare()
    {
        const string run = "{{model:B}} answered {{model.B.vsReference.better}}, {{model.B.vsReference.similar}}, {{model.B.vsReference.worse}}.";
        static bool Listed(BenchmarkReportValidationNote n)
            => n.Rule == BenchmarkReportPackValidator.ComparisonTokenShapeRule && n.Message.StartsWith("Lists counts in a row", StringComparison.Ordinal);

        // Every count says what it counts: the run reads.
        Assert.DoesNotContain(ValidateExecutive(run), Listed);

        // A display stored before counts said what they count is bare: the run is a list of figures.
        var bare = ValidateExecutive(run, sheet => sheet.Facts.Single(f => f.Key == "model.B.vsReference.similar").Display = "5 of 6 questions");
        Assert.Single(bare, Listed);
    }

    [Theory]
    [InlineData("{{model:A}} kept answering without tools on {{model.A.profile.noToolAnswers}}.", "without', 'tools")]
    [InlineData("{{model:A}} gave {{model.A.profile.noToolAnswers}} without tool calls.", "without', 'tool")]
    [InlineData("{{model:A}} hit a tool limit on {{model.A.profile.toolLimitHits}}, hitting the limit often.", "tool', 'limit")]
    [InlineData("{{model:A}} recorded {{model.A.errors.critical}} with a critical error.", "critical', 'error")]
    public void Rule39_FlagsWordsBesideACountThatRepeatWhatItCounts_AsAWarning(string overview, string repeated)
    {
        var note = Assert.Single(ValidateExecutive(overview), n => n.Rule == BenchmarkReportPackValidator.CountRepetitionRule);

        Assert.Contains("'" + repeated + "'", note.Message, StringComparison.Ordinal);
        Assert.Contains("already prints '", note.Message, StringComparison.Ordinal);
        Assert.False(note.Dropped);
        Assert.True(BenchmarkReportPackValidator.IsWarningRule(BenchmarkReportPackValidator.CountRepetitionRule));
        Assert.Equal("Words repeated beside a count", BenchmarkReportRuleCatalog.Name(BenchmarkReportPackValidator.CountRepetitionRule));
    }

    [Theory]
    [InlineData("{{model:A}} wrote {{model.A.profile.noToolAnswers}}, more than any other model.")]
    [InlineData("Without tools, {{model:A}} answered few questions well; it gave {{model.A.errors.critical}}.")]
    public void Rule39_LeavesACountWrittenAsANounPhraseAlone(string overview)
    {
        Assert.DoesNotContain(ValidateExecutive(overview), n => n.Rule == BenchmarkReportPackValidator.CountRepetitionRule);
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

    // --- Rule 31: the token a splice continues, one note per token, statuses side by side ---------------

    private const string CheckSentence = "Run a panel swap to settle the grader-dependent hypotheses.";

    /// <summary><see cref="QuestionSheet"/> with a check, its noun-phrase summary, and a known fact whose value is a lowercase sentence.</summary>
    private static BenchmarkReportFactSheet CheckSheet()
    {
        var sheet = QuestionSheet();
        sheet.Facts.Add(TextFact("rca.check.1", CheckSentence));
        sheet.Facts.Add(TextFact("rca.check.1.summary", "Check 1, a panel swap with two graders of other providers"));
        sheet.Facts.Add(TextFact("rca.known.1", "none recorded."));
        return sheet;
    }

    private static List<BenchmarkReportValidationNote> TokenShapeNotes(BenchmarkReportFactSheet sheet, string slot, string paragraph)
        => ValidateSections(sheet, new Dictionary<string, string> { [slot] = paragraph })
            .Where(n => n.Rule == BenchmarkReportPackValidator.ComparisonTokenShapeRule && n.Location.StartsWith("sections." + slot, StringComparison.Ordinal))
            .ToList();

    [Fact]
    public void Rule31_ASplice_NamesItsToken_IsReportedOnce_AndPointsACheckToItsSummary()
    {
        var notes = TokenShapeNotes(CheckSheet(), BenchmarkReportSlots.NextRuns, "Complete {{rca.check.1}} would settle these axes.");

        var note = Assert.Single(notes);
        Assert.StartsWith("Continues {{rca.check.1}} after its full stop (\"", note.Message, StringComparison.Ordinal);
        Assert.Contains("hypotheses. would settle", note.Message, StringComparison.Ordinal);
        Assert.Contains("cite it as {{rca.check.1.summary}}", note.Message, StringComparison.Ordinal);
        Assert.Contains("It also stands inside a sentence", note.Message, StringComparison.Ordinal);

        // The excerpt begins and ends with whole words.
        string excerpt = note.Message.Split("(\"")[1].Split("\")")[0].Trim('…');
        var words = ("Complete " + CheckSentence + " would settle these axes.").Split(' ');
        Assert.Contains(excerpt.Split(' ')[0], words);
        Assert.Contains(excerpt.Split(' ')[^1], words);
    }

    [Fact]
    public void Rule31_TwoSplicesOfOneToken_AreTwoNotes_AndNoPlacementNote()
    {
        var notes = TokenShapeNotes(CheckSheet(), BenchmarkReportSlots.NextRuns,
            "Complete {{rca.check.1}} would settle it. Later {{rca.check.1}} would help again.");

        Assert.Equal(2, notes.Count(n => n.Message.StartsWith("Continues {{rca.check.1}} after its full stop", StringComparison.Ordinal)));
        Assert.DoesNotContain(notes, n => n.Message.StartsWith("Places the sentence-valued", StringComparison.Ordinal));
    }

    [Fact]
    public void Rule31_ACheckInsideASentence_IsPointedToItsSummary()
    {
        var note = Assert.Single(TokenShapeNotes(CheckSheet(), BenchmarkReportSlots.Limitations, "The next step is {{rca.check.1}}"));

        Assert.StartsWith("Places the sentence-valued fact {{rca.check.1}} inside a sentence or parentheses", note.Message, StringComparison.Ordinal);
        Assert.Contains("cite it as {{rca.check.1.summary}}", note.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("The axes stand at {{rca.axis.speed.status}} and {{rca.axis.cost.status}}.",
        "Lists {{rca.axis.speed.status}} and {{rca.axis.cost.status}} side by side: name each axis before its status.")]
    [InlineData("The verdicts are in ({{rca.axis.intelligence.status}}, {{rca.axis.speed.status}}).",
        "Lists {{rca.axis.intelligence.status}} and {{rca.axis.speed.status}} side by side: name each axis before its status.")]
    [InlineData("The hypotheses are graded {{rca.hypothesis.HS1.1.grade}} and {{rca.hypothesis.HQ1.1.grade}}.",
        "Lists {{rca.hypothesis.HS1.1.grade}} and {{rca.hypothesis.HQ1.1.grade}} side by side: name each hypothesis before its grade.")]
    public void Rule31_StatusesOrGradesSideBySide_AreFlagged(string paragraph, string message)
    {
        Assert.Contains(TokenShapeNotes(CheckSheet(), BenchmarkReportSlots.Conclusions, paragraph), n => n.Message == message);
    }

    [Fact]
    public void Rule31_EachAxisNamedBeforeItsStatus_IsNotFlagged()
    {
        var notes = TokenShapeNotes(CheckSheet(), BenchmarkReportSlots.Conclusions, "Speed: {{rca.axis.speed.status}}; cost: {{rca.axis.cost.status}}.");

        Assert.DoesNotContain(notes, n => n.Message.Contains("side by side", StringComparison.Ordinal));
    }

    [Fact]
    public void Rule31_ALowercaseSentenceFact_AtASentenceStart_IsNotFlagged_ButTheWritersOwnLowercaseWordIs()
    {
        Assert.Empty(TokenShapeNotes(CheckSheet(), BenchmarkReportSlots.Limitations, "Known facts come first. {{rca.known.1}} The rest is in the table."));

        var spliced = TokenShapeNotes(CheckSheet(), BenchmarkReportSlots.Limitations, "Known facts come first. {{rca.known.1}} the rest is in the table.");
        Assert.Contains(spliced, n => n.Message.StartsWith("Continues {{rca.known.1}} after its full stop", StringComparison.Ordinal));
    }

    // --- Rule 7: trimming never removes an axis status from the conclusions ----------------------------

    /// <summary>A sentence of <paramref name="words"/> words: "Lore" and then "lore" repeated, ending in a full stop.</summary>
    private static string Filler(int words) => "Lore " + string.Join(" ", Enumerable.Repeat("lore", words - 1)) + ".";

    private static BenchmarkReportCleanResult DropConclusions(string conclusions)
    {
        var output = new BenchmarkReportWriterOutput { Headline = "The models differ on every axis." };
        output.Sections[BenchmarkReportSlots.Conclusions] = conclusions;
        return BenchmarkReportPackValidator.DropInvalid(Rca, output, QuestionSheet(), new BenchmarkReportContentSnapshot());
    }

    [Fact]
    public void Rule7_TheConclusions_LoseATrailingSentence_ButNeverAnAxisStatus()
    {
        string intelligence = "Intelligence: {{rca.axis.intelligence.status}}. " + Filler(50);
        string speed = "Speed: {{rca.axis.speed.status}}. " + Filler(50);
        string cost = "Cost: {{rca.axis.cost.status}}.";

        var cleaned = DropConclusions(intelligence + "\n\n" + speed + "\n\n" + cost + " " + Filler(30));

        Assert.Equal(intelligence + "\n\n" + speed + "\n\n" + cost, cleaned.Output.Sections[BenchmarkReportSlots.Conclusions]);
        var note = Assert.Single(cleaned.Notes, n => n.Rule == 7 && n.Location.StartsWith("sections.conclusions", StringComparison.Ordinal));
        Assert.Equal("sections.conclusions[p3]", note.Location);
        Assert.True(note.Dropped);
        Assert.Equal(Filler(30), note.Text);
    }

    [Fact]
    public void Rule7_ConclusionsWhoseEveryRemovableSentenceStatesAStatus_AreKeptOverTheCap_WithAWarning()
    {
        string conclusions = Filler(60) + " Intelligence: {{rca.axis.intelligence.status}}.\n\n" + Filler(60) + " Speed: {{rca.axis.speed.status}}.";

        var cleaned = DropConclusions(conclusions);

        Assert.Equal(conclusions, cleaned.Output.Sections[BenchmarkReportSlots.Conclusions]);
        var note = Assert.Single(cleaned.Notes, n => n.Rule == 7 && n.Location.StartsWith("sections.conclusions", StringComparison.Ordinal));
        Assert.False(note.Dropped);
        Assert.Equal("sections.conclusions", note.Location);
        Assert.StartsWith("Kept over its cap at 124 words", note.Message, StringComparison.Ordinal);
        Assert.Equal(BenchmarkReportRuleCatalog.WarningTextKeptSeverity, BenchmarkReportRuleCatalog.ToIssueDto(note).Severity);
    }

    [Fact]
    public void LocationLabel_NamesTheBareSectionsLocationOfAStoredShorteningNote()
    {
        Assert.Equal("The capped sections", BenchmarkReportRuleCatalog.LocationLabel("sections"));
        Assert.Equal("Conclusions", BenchmarkReportRuleCatalog.LocationLabel("sections.conclusions", BenchmarkReportSlots.RootCauseSlotTitles));
    }

    // --- HeadlineIssues (v1)---------------------------------------------------------------------------

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

    // --- Rules 40 to 43: what code prints, unavailable figures, the next runs' order, borrowed figures ---

    private static List<BenchmarkReportValidationNote> RuleNotes(BenchmarkReportFactSheet sheet, int rule, string slot, string paragraph)
        => ValidateSections(sheet, new Dictionary<string, string> { [slot] = paragraph })
            .Where(n => n.Rule == rule)
            .ToList();

    /// <summary><see cref="QuestionSheet"/> with one unexpected result: its count, reference and summary.</summary>
    private static BenchmarkReportFactSheet AnomalySheet()
    {
        var sheet = QuestionSheet();
        sheet.Facts.Add(new BenchmarkReportFact { Key = "rca.anomaly.count", Value = System.Text.Json.Nodes.JsonValue.Create(1), Display = "1 unexpected result" });
        sheet.Facts.Add(TextFact("rca.anomaly.1.ref", "U1"));
        sheet.Facts.Add(TextFact("rca.anomaly.1.summary", "Model B scored below the cheaper Model A; not established, the intervals overlap."));
        return sheet;
    }

    [Fact]
    public void Rule40_TheUnexpectedResultsCitingAResultsSummary_IsAWarning_ButItsReferenceAndOtherSlotsAreNot()
    {
        var sheet = AnomalySheet();

        var note = Assert.Single(RuleNotes(sheet, BenchmarkReportPackValidator.RootCauseCodePrintedRule, BenchmarkReportSlots.UnexpectedResults,
            "The grader comments on {{rca.anomaly.1.ref}} name a missing step. {{rca.anomaly.1.summary}}"));
        Assert.StartsWith("{{rca.anomaly.1.summary}} is printed by Overseer in the Unexpected results section", note.Message, StringComparison.Ordinal);
        Assert.True(BenchmarkReportPackValidator.IsWarningRule(note.Rule));
        Assert.Empty(RuleNotes(sheet, BenchmarkReportPackValidator.RootCauseCodePrintedRule, BenchmarkReportSlots.UnexpectedResults,
            "The grader comments on {{rca.anomaly.1.ref}} name a missing step."));
        Assert.Empty(RuleNotes(sheet, BenchmarkReportPackValidator.RootCauseCodePrintedRule, BenchmarkReportSlots.Conclusions,
            "Cost: {{rca.axis.cost.status}}. {{rca.anomaly.1.summary}}"));

        // A reference the sheet does not hold is an unknown token.
        var unknown = Assert.Single(RuleNotes(sheet, 2, BenchmarkReportSlots.UnexpectedResults, "The grader comments on {{rca.anomaly.2.ref}} name a missing step."));
        Assert.Contains("{{rca.anomaly.2.ref}}", unknown.Message, StringComparison.Ordinal);
    }

    // --- The optional unexpected results ------------------------------------------------------------

    [Fact]
    public void TheUnexpectedResults_AreOptional_AndCappedAtEightyWords()
    {
        var sheet = AnomalySheet();
        const string slot = BenchmarkReportSlots.UnexpectedResults;
        Assert.Equal(80, BenchmarkReportPackValidator.RootCauseUnexpectedResultsMaxWords);

        // Left out or empty, the slot is no issue; it is never an unknown slot.
        Assert.DoesNotContain(ValidateSections(sheet, new Dictionary<string, string>()), n => n.Location.StartsWith("sections." + slot, StringComparison.Ordinal));
        var filled = ValidateSections(sheet, new Dictionary<string, string> { [slot] = Filler(80) });
        Assert.DoesNotContain(filled, n => n.Location.StartsWith("sections." + slot, StringComparison.Ordinal) && n.Rule is 1 or 7);

        var over = ValidateSections(sheet, new Dictionary<string, string> { [slot] = Filler(81) });
        var note = Assert.Single(over, n => n.Rule == 7 && n.Location == "sections." + slot);
        Assert.EndsWith("has 81 words; the limit is 80.", note.Message, StringComparison.Ordinal);

        // A kept slot is copied; one over its cap loses its last sentences.
        var output = new BenchmarkReportWriterOutput { Headline = "The models differ on every axis." };
        output.Sections[slot] = "The grader comments on {{rca.anomaly.1.ref}} name a missing step.";
        var cleaned = BenchmarkReportPackValidator.DropInvalid(Rca, output, sheet, new BenchmarkReportContentSnapshot());
        Assert.Equal(output.Sections[slot], cleaned.Output.Sections[slot]);

        output.Sections[slot] = Filler(40) + " " + Filler(41);
        cleaned = BenchmarkReportPackValidator.DropInvalid(Rca, output, sheet, new BenchmarkReportContentSnapshot());
        Assert.Equal(Filler(40), cleaned.Output.Sections[slot]);
        Assert.Contains(cleaned.Notes, n => n.Rule == 7 && n.Dropped && n.Location == "sections." + slot + "[p1]");
    }

    [Fact]
    public void TheUnexpectedResults_OfASheetWithoutOne_AreARule7Issue_AndAreRemoved()
    {
        const string slot = BenchmarkReportSlots.UnexpectedResults;
        const string text = "The grader comments name a missing step.";

        var note = Assert.Single(ValidateSections(QuestionSheet(), new Dictionary<string, string> { [slot] = text }),
            n => n.Location.StartsWith("sections." + slot, StringComparison.Ordinal));
        Assert.Equal(7, note.Rule);
        Assert.Equal("There are no unexpected results; leave the slot empty.", note.Message);

        var output = new BenchmarkReportWriterOutput { Headline = "The models differ on every axis." };
        output.Sections[slot] = text;
        var cleaned = BenchmarkReportPackValidator.DropInvalid(Rca, output, QuestionSheet(), new BenchmarkReportContentSnapshot());
        Assert.False(cleaned.Output.Sections.ContainsKey(slot));
        var dropped = Assert.Single(cleaned.Notes, n => n.Location == "sections." + slot);
        Assert.True(dropped.Dropped);
        Assert.Equal(text, dropped.Text);

        // An empty slot stays silent.
        Assert.DoesNotContain(ValidateSections(QuestionSheet(), new Dictionary<string, string> { [slot] = " " }),
            n => n.Location.StartsWith("sections." + slot, StringComparison.Ordinal));
    }

    [Fact]
    public void TheUnexpectedResults_AreAnObservationSlot_WhereACausalWordIsRemoved()
    {
        Assert.Contains(BenchmarkReportSlots.UnexpectedResults, RootCauseClaimSupport.ObservationSlots);
        Assert.Contains(RuleNotes(AnomalySheet(), BenchmarkReportPackValidator.RootCauseEvidenceCausalRule, BenchmarkReportSlots.UnexpectedResults,
            "The grader comments on {{rca.anomaly.1.ref}} show a gap because of a missing step."), n => !BenchmarkReportPackValidator.IsWarningRule(n.Rule));
    }

    // --- Rule 2: a price effect's token is unknown to the Root Cause Analysis writer ---------------------

    [Fact]
    public void Rule2_APriceEffectsToken_IsUnknown_WhereTheSheetsResultHidesIt()
    {
        var sheet = QuestionSheet();
        sheet.RootCause = new RootCauseResult
        {
            Hypotheses = new List<RootCauseHypothesis>
            {
                new() { Id = "HC2", Number = 1, Key = "HC2.1", Axis = RootCauseAxes.Cost, Kind = "pair", PriceEffect = true },
                new() { Id = "HQ1", Number = 1, Key = "HQ1.1", Axis = RootCauseAxes.Intelligence, Kind = "pair" },
            }
        };
        const string priceCards = "Price per token differs between the models ({{rca.hypothesis.HC2.1.ref}}).";

        var note = Assert.Single(RuleNotes(sheet, 2, BenchmarkReportSlots.HypothesesCost, priceCards));
        Assert.Contains("{{rca.hypothesis.HC2.1.ref}}", note.Message, StringComparison.Ordinal);
        Assert.Contains("is a price-card difference, which Overseer prints in one line: leave it out.", note.Message, StringComparison.Ordinal);
        Assert.Empty(RuleNotes(sheet, 2, BenchmarkReportSlots.HypothesesIntelligence, "Critical errors may account for part of the gap ({{rca.hypothesis.HQ1.1.ref}})."));

        // A sheet whose result marks no price effect, or that has none, hides nothing.
        Assert.Empty(RuleNotes(QuestionSheet(), 2, BenchmarkReportSlots.HypothesesCost, priceCards));
    }

    // --- Rules 44 and 45: counts in words, nulls restated ------------------------------------------------

    [Theory]
    [InlineData("Completeness is the lowest dimension for eight of the nine models.", "eight of the nine")]
    [InlineData("All of the nine models lose most on Completeness.", "All of the nine")]
    [InlineData("Three of four answers cite no tool.", "Three of four")]
    [InlineData("Twenty-one of thirty questions were answered well.", "Twenty-one of thirty")]
    public void Rule44_ACountAcrossATotalWrittenInWords_IsAWarning(string paragraph, string count)
    {
        var note = Assert.Single(RuleNotes(QuestionSheet(), BenchmarkReportPackValidator.NumberWordCountRule, BenchmarkReportSlots.EvidenceIntelligence, paragraph));

        Assert.Contains("\"" + count + "\"", note.Message, StringComparison.Ordinal);
        Assert.EndsWith("write this count as a fact token, or leave the total out.", note.Message, StringComparison.Ordinal);
        Assert.True(BenchmarkReportPackValidator.IsWarningRule(note.Rule));
    }

    [Theory]
    [InlineData("The evidence covers three pairs.")]
    [InlineData("One of the models answered without tools.")]
    [InlineData("Completeness is the lowest dimension for {{rca.axis.cost.status}} of the models.")]
    [InlineData("Someone of note checked the tone.")]
    public void Rule44_LeavesAPlainCountAlone(string paragraph)
    {
        Assert.Empty(RuleNotes(QuestionSheet(), BenchmarkReportPackValidator.NumberWordCountRule, BenchmarkReportSlots.EvidenceIntelligence, paragraph));
    }

    [Fact]
    public void Rule44_ReadsEveryProseField_OfARootCauseAnalysisOnly()
    {
        const string count = "eight of the nine models";
        Assert.Single(RuleNotes(QuestionSheet(), BenchmarkReportPackValidator.NumberWordCountRule, BenchmarkReportSlots.Conclusions,
            "Intelligence: {{rca.axis.intelligence.status}}. It holds for " + count + "."));

        var output = Writer(comparison: true);
        output.Observations![0].Text = "The graders recorded the same gap for " + count + ".";
        Assert.Contains(Validate(true, output), n => n.Rule == BenchmarkReportPackValidator.NumberWordCountRule && n.Location.StartsWith("observations", StringComparison.Ordinal));

        Assert.DoesNotContain(ValidateExecutive("The models agree for " + count + "."), n => n.Rule == BenchmarkReportPackValidator.NumberWordCountRule);
    }

    [Theory]
    [InlineData(BenchmarkReportSlots.HypothesesIntelligence, true)]
    [InlineData(BenchmarkReportSlots.HypothesesCost, true)]
    [InlineData(BenchmarkReportSlots.Conclusions, false)]
    [InlineData(BenchmarkReportSlots.EvidenceIntelligence, false)]
    public void Rule45_ANullRestatedInAHypothesesParagraph_IsAWarning(string slot, bool flagged)
    {
        var notes = RuleNotes(QuestionSheet(), BenchmarkReportPackValidator.RootCauseNullRestatedRule, slot, "The null hypothesis stands for this pair.");

        if (!flagged)
        {
            Assert.Empty(notes);
            return;
        }
        var note = Assert.Single(notes);
        Assert.Equal("sections." + slot + "[p1]", note.Location);
        Assert.EndsWith("Overseer prints the nulls beside this paragraph; remove the sentence.", note.Message, StringComparison.Ordinal);
        Assert.True(BenchmarkReportPackValidator.IsWarningRule(note.Rule));
    }

    [Fact]
    public void Rule45_FlagsEachSentenceThatRestatesTheNulls_AndLeavesTheOthersAlone()
    {
        const string slot = BenchmarkReportSlots.HypothesesSpeed;
        var notes = RuleNotes(QuestionSheet(), BenchmarkReportPackValidator.RootCauseNullRestatedRule, slot,
            "Longer outputs may explain the gap ({{rca.hypothesis.HS1.1.ref}}). The other gaps may be chance. One pair shows a real difference.");

        Assert.Equal(2, notes.Count);
        Assert.Contains(notes, n => n.Message.Contains("\"may be chance\"", StringComparison.Ordinal));
        Assert.Contains(notes, n => n.Message.Contains("\"real difference\"", StringComparison.Ordinal));
        Assert.Empty(RuleNotes(QuestionSheet(), BenchmarkReportPackValidator.RootCauseNullRestatedRule, slot,
            "Longer outputs may explain the gap ({{rca.hypothesis.HS1.1.ref}})."));
    }

    [Fact]
    public void Rule45_ReadsNonNull()
    {
        Assert.Contains("non-null", BenchmarkReportPackValidator.NullRestatedPhrases);
        var note = Assert.Single(RuleNotes(QuestionSheet(), BenchmarkReportPackValidator.RootCauseNullRestatedRule, BenchmarkReportSlots.HypothesesCost,
            "No non-null mechanism hypothesis fired on the cost axis."));

        Assert.Contains("\"non-null\"", note.Message, StringComparison.Ordinal);
    }

    // --- A quiet axis: no hypothesis other than a null or a price effect ------------------------------

    /// <summary>
    /// <see cref="QuestionSheet"/> with a result whose cost axis holds only a price effect and a null, while the
    /// intelligence and speed axes each hold a hypothesis that fired.
    /// </summary>
    private static BenchmarkReportFactSheet QuietCostSheet()
    {
        var sheet = QuestionSheet();
        sheet.RootCause = new RootCauseResult
        {
            Hypotheses = new List<RootCauseHypothesis>
            {
                new() { Id = "H0-C", Number = 1, Key = "H0-C.1", Axis = RootCauseAxes.Cost, Kind = "null" },
                new() { Id = "HC2", Number = 1, Key = "HC2.1", Axis = RootCauseAxes.Cost, Kind = "pair", PriceEffect = true },
                new() { Id = "HQ1", Number = 1, Key = "HQ1.1", Axis = RootCauseAxes.Intelligence, Kind = "pair" },
                new() { Id = "HS1", Number = 1, Key = "HS1.1", Axis = RootCauseAxes.Speed, Kind = "pair" },
            }
        };
        return sheet;
    }

    [Fact]
    public void QuietAxis_HypothesesSlotMayBeLeftOut()
    {
        var sheet = QuietCostSheet();
        Assert.Equal(new[] { RootCauseAxes.Cost }, BenchmarkReportPackPrompt.RootCauseQuietAxes(sheet.RootCause));

        // Left out, the quiet axis's slot raises no rule 1 note; the other hypotheses slots still do.
        var notes = ValidateSections(sheet, new Dictionary<string, string>());
        Assert.DoesNotContain(notes, n => n.Location.StartsWith("sections." + BenchmarkReportSlots.HypothesesCost, StringComparison.Ordinal));
        Assert.Contains(notes, n => n.Rule == 1 && n.Location == "sections." + BenchmarkReportSlots.HypothesesIntelligence);
        Assert.Contains(notes, n => n.Rule == 1 && n.Location == "sections." + BenchmarkReportSlots.HypothesesSpeed);

        var output = new BenchmarkReportWriterOutput { Headline = "The models differ on every axis." };
        var cleaned = BenchmarkReportPackValidator.DropInvalid(Rca, output, sheet, new BenchmarkReportContentSnapshot());
        Assert.DoesNotContain(cleaned.Notes, n => n.Location.StartsWith("sections." + BenchmarkReportSlots.HypothesesCost, StringComparison.Ordinal));
        Assert.False(cleaned.Output.Sections.ContainsKey(BenchmarkReportSlots.HypothesesCost));

        // Written anyway, it is checked as before, and it is no unknown slot.
        var written = RuleNotes(sheet, BenchmarkReportPackValidator.RootCauseNullRestatedRule, BenchmarkReportSlots.HypothesesCost, "The null hypothesis stands for this pair.");
        Assert.Equal("sections." + BenchmarkReportSlots.HypothesesCost + "[p1]", Assert.Single(written).Location);
        Assert.DoesNotContain(ValidateSections(sheet, new Dictionary<string, string> { [BenchmarkReportSlots.HypothesesCost] = "The cost axis shows nothing more." }),
            n => n.Rule == 1 && n.Location.StartsWith("sections." + BenchmarkReportSlots.HypothesesCost, StringComparison.Ordinal));
    }

    // --- A writer hypothesis' evidence ------------------------------------------------------------------

    [Fact]
    public void AWriterHypothesis_GainsAsEvidenceEveryQuestionItsTextsCite()
    {
        var (sheet, content) = Scope(comparison: true);
        var output = Writer(comparison: true);
        var hypothesis = output.WriterHypotheses![0];
        var numbers = sheet.Questions.Select(q => q.Number).Distinct().OrderBy(n => n).ToList();
        string listed = Assert.Single(hypothesis.Evidence);
        string added = "Q" + numbers.First(n => "Q" + n != listed);
        hypothesis.Against = new List<string> { "The answers to " + added + " kept the step the rubric asks for." };

        var cleaned = BenchmarkReportPackValidator.DropInvalid(Rca, output, sheet, content);

        var kept = Assert.Single(cleaned.Output.WriterHypotheses!);
        Assert.Equal(new[] { listed, added }, kept.Evidence);
        Assert.Equal(new[] { listed }, hypothesis.Evidence);
    }

    [Fact]
    public void Rule41_ATokenWithoutAValue_IsAWarning_AndAnAvailableOneIsNot()
    {
        var sheet = QuestionSheet();
        sheet.Facts.Add(new BenchmarkReportFact { Key = "comparison.memberRuns", Display = "not available", Available = false, UnavailableReason = "Not every model records this count." });
        sheet.Facts.Add(TextFact("comparison.suiteCount", "2 suites"));

        var note = Assert.Single(RuleNotes(sheet, BenchmarkReportPackValidator.UnavailableTokenRule, BenchmarkReportSlots.Scope,
            "Each model rests on {{comparison.memberRuns}} across {{comparison.suiteCount}}."));
        Assert.StartsWith("{{comparison.memberRuns}} has no value in this data and prints 'not available'", note.Message, StringComparison.Ordinal);
        Assert.True(BenchmarkReportPackValidator.IsWarningRule(note.Rule));

        Assert.Empty(RuleNotes(sheet, BenchmarkReportPackValidator.UnavailableTokenRule, BenchmarkReportSlots.Scope,
            "Each model rests on runs across {{comparison.suiteCount}}."));
    }

    /// <summary><see cref="QuestionSheet"/> with a second run, run 1's purpose and one check.</summary>
    private static BenchmarkReportFactSheet NextRunsSheet(string firstPurpose)
    {
        var sheet = QuestionSheet();
        sheet.Facts.Add(TextFact("rca.run.1.purpose", RootCausePurposes.Text(firstPurpose)));
        sheet.Facts.Add(TextFact("rca.run.2.summary", "a response-style pair of every model"));
        sheet.Facts.Add(TextFact("rca.run.2.purpose", RootCausePurposes.Text(RootCausePurposes.ModelSelection)));
        sheet.Facts.Add(TextFact("rca.check.1", CheckSentence));
        sheet.Facts.Add(TextFact("rca.check.1.summary", "Check 1, a panel swap with two graders of other providers"));
        return sheet;
    }

    [Theory]
    [InlineData(RootCausePurposes.Chat, "First {{rca.check.1.summary}}. Then make {{rca.run.1.summary}} and {{rca.run.2.summary}}.", true)]
    [InlineData(RootCausePurposes.ModelSelection, "First {{rca.check.1.summary}}. Then make {{rca.run.1.summary}} and {{rca.run.2.summary}}.", false)]
    [InlineData(RootCausePurposes.Chat, "Make {{rca.run.2.summary}} first. Then make {{rca.run.1.summary}}.", true)]
    [InlineData(RootCausePurposes.Chat, "Make {{rca.run.1.summary}} first, then {{rca.run.2.summary}}. Afterwards, {{rca.check.1.summary}}.", false)]
    public void Rule42_TheNextRuns_FollowCodesOrder(string firstPurpose, string paragraph, bool flagged)
    {
        var notes = RuleNotes(NextRunsSheet(firstPurpose), BenchmarkReportPackValidator.RootCauseNextRunsOrderRule, BenchmarkReportSlots.NextRuns, paragraph);

        if (!flagged)
        {
            Assert.Empty(notes);
            return;
        }
        var note = Assert.Single(notes);
        Assert.Equal("sections." + BenchmarkReportSlots.NextRuns, note.Location);
        Assert.Equal("Write the runs in code's order: {{rca.run.1.summary}} first, then {{rca.run.2.summary}}; a check comes after them.", note.Message);
        Assert.True(BenchmarkReportPackValidator.IsWarningRule(note.Rule));
    }

    /// <summary><see cref="QuestionSheet"/> with four models, an at-risk figure and an output price each; A and B share both.</summary>
    private static BenchmarkReportFactSheet BorrowedFigureSheet()
    {
        var sheet = QuestionSheet();
        sheet.Peers.AddRange(new[] { "C", "D" }
            .Select((l, i) => new BenchmarkReportPeer { Letter = l, EntryKey = "run:" + (52 + i), Label = "Orion " + l, Provider = "Northwind", State = "Comparable" }));
        foreach (var (letter, risk, price) in new[] { ("A", "1.6 points", "$10.00"), ("B", "1.6 points", "$10.00"), ("C", "2.4 points", "$12.00") })
        {
            sheet.Facts.Add(TextFact("rca.atRisk." + letter + ".criticalError", risk));
            sheet.Facts.Add(TextFact("model." + letter + ".price.output.perMillion", price + " per million tokens"));
        }
        return sheet;
    }

    [Theory]
    [InlineData("Critical errors put {{rca.atRisk.A.criticalError}} at risk for {{model:A}} and {{model:B}}.", null)]
    [InlineData("Critical errors put {{rca.atRisk.A.criticalError}} at risk for {{model:A}}, {{model:B}} and {{model:C}}.",
        "{{rca.atRisk.A.criticalError}} prints {{model:A}}'s figure; {{model:C}}'s is {{rca.atRisk.C.criticalError}} (2.4 points): cite each model's own token.")]
    [InlineData("Output prices start at {{model.A.price.output.perMillion}} for {{model:A}} and {{model:B}}.", null)]
    [InlineData("Output prices start at {{model.A.price.output.perMillion}} for {{model:B}}, {{model:C}} and {{model:D}}.",
        "{{model.A.price.output.perMillion}} prints {{model:A}}'s figure; {{model:C}}'s is {{model.C.price.output.perMillion}} ($12.00 per million tokens); {{model:D}} has no such figure: cite each model's own token.")]
    [InlineData("Critical errors put {{rca.atRisk.A.criticalError}} at risk for {{model:A}} and {{rca.atRisk.C.criticalError}} for {{model:C}}.", null)]
    public void Rule43_AModelsFigure_GivenForAnotherModel_IsFlaggedOnlyWhereTheFiguresDiffer(string paragraph, string? message)
    {
        var notes = RuleNotes(BorrowedFigureSheet(), BenchmarkReportPackValidator.BorrowedModelFigureRule, BenchmarkReportSlots.EvidenceCost, paragraph);

        if (message == null)
        {
            Assert.Empty(notes);
            return;
        }
        var note = Assert.Single(notes);
        Assert.Equal(message, note.Message);
        Assert.True(BenchmarkReportPackValidator.IsWarningRule(note.Rule));
    }

    [Fact]
    public void Rules40To45_AreInTheCatalog_AsWarningsWithDigitFreeFixes()
    {
        var rules = new[]
        {
            BenchmarkReportPackValidator.RootCauseCodePrintedRule, BenchmarkReportPackValidator.UnavailableTokenRule,
            BenchmarkReportPackValidator.RootCauseNextRunsOrderRule, BenchmarkReportPackValidator.BorrowedModelFigureRule,
            BenchmarkReportPackValidator.NumberWordCountRule, BenchmarkReportPackValidator.RootCauseNullRestatedRule
        };
        foreach (int rule in rules)
        {
            Assert.True(BenchmarkReportPackValidator.IsWarningRule(rule));
            var info = BenchmarkReportRuleCatalog.Get(rule);
            Assert.NotNull(info);
            Assert.Contains(BenchmarkReportRuleCatalog.All, r => r.Rule == rule);
            Assert.Equal(BenchmarkReportRuleSeverity.Warning, info!.Severity);
            Assert.DoesNotContain(info.Fix, char.IsDigit);
        }
        Assert.Equal(new[] { 40, 41, 42, 43, 44, 45 }, rules);
        Assert.Equal("A count written in words", BenchmarkReportRuleCatalog.Name(BenchmarkReportPackValidator.NumberWordCountRule));
        Assert.Equal("Write this count as a fact token, or leave the total out.", BenchmarkReportRuleCatalog.Get(BenchmarkReportPackValidator.NumberWordCountRule).Fix);
        Assert.Equal("Nulls restated in a hypotheses paragraph", BenchmarkReportRuleCatalog.Name(BenchmarkReportPackValidator.RootCauseNullRestatedRule));
        Assert.Equal("Overseer prints the nulls beside this paragraph; remove the sentence.", BenchmarkReportRuleCatalog.Get(BenchmarkReportPackValidator.RootCauseNullRestatedRule).Fix);
    }
}
