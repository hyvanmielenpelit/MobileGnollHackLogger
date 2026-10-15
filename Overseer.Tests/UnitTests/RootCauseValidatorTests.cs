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
    [InlineData(BenchmarkReportSlots.AnswerBest, 60)]
    [InlineData(BenchmarkReportSlots.AnswerCost, 60)]
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
    /// axes not established, a Moderate hypothesis, a High one, a Low one and a Not supported one (each with a
    /// statement and a label), and one next run.
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
            TextFact("rca.hypothesis.HC1.1.statement", "Model B pays a higher output price than Model A."),
            TextFact("rca.hypothesis.HQ1.1.grade", RootCauseGrades.Moderate),
            TextFact("rca.hypothesis.HQ1.1.label", "critical errors of Model B against Model A"),
            TextFact("rca.hypothesis.HQ1.1.statement", "Model B made more critical errors than Model A."),
            TextFact("rca.hypothesis.HS1.1.grade", RootCauseGrades.High),
            TextFact("rca.hypothesis.HS1.1.label", "longer outputs of Model B"),
            TextFact("rca.hypothesis.HS1.1.statement", "Model B writes longer answers than Model A."),
            TextFact("rca.hypothesis.HS4.1.grade", RootCauseGrades.NotSupported, RootCauseGrades.Display(RootCauseGrades.NotSupported)),
            TextFact("rca.hypothesis.HS4.1.label", "slower first token of Model B"),
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
    public void TheAnswerSlots_NeedNoQuestionStatus()
    {
        var sheet = QuestionSheet(1, 3);

        // Only the questions the sheet asks have a slot of the document.
        var empty = ValidateSections(sheet, new Dictionary<string, string>());
        Assert.Contains(empty, n => n.Rule == 1 && n.Location == "sections." + BenchmarkReportSlots.AnswerBest);
        Assert.Contains(empty, n => n.Rule == 1 && n.Location == "sections." + BenchmarkReportSlots.AnswerSpeed);
        Assert.DoesNotContain(empty, n => n.Rule == 1 && n.Location == "sections." + BenchmarkReportSlots.AnswerWorst);
        Assert.DoesNotContain(empty, n => n.Rule == 1 && n.Location == "sections." + BenchmarkReportSlots.AnswerCost);

        const string uncited = "The best model scored highest on the hard questions.";
        var sections = new Dictionary<string, string>
        {
            [BenchmarkReportSlots.AnswerBest] = uncited,
            [BenchmarkReportSlots.AnswerSpeed] = "Its answers run longer, as {{rca.hypothesis.HS1.1.label}} suggests.",
            [BenchmarkReportSlots.AnswerWorst] = "The lowest model lost points on hard questions.",
        };
        var notes = ValidateSections(sheet, sections);

        Assert.DoesNotContain(notes, n => n.Rule == BenchmarkReportPackValidator.RootCauseAxisStatusRule
                                          && n.Location.StartsWith("sections." + BenchmarkReportSlots.AnswerBest, StringComparison.Ordinal));
        Assert.DoesNotContain(notes, n => n.Rule == BenchmarkReportPackValidator.RootCauseAxisStatusRule
                                          && n.Location.StartsWith("sections." + BenchmarkReportSlots.AnswerSpeed, StringComparison.Ordinal));
        Assert.Contains(notes, n => n.Rule == 1 && n.Location == "sections." + BenchmarkReportSlots.AnswerWorst && n.Message.Contains("does not ask", StringComparison.Ordinal));

        // An answer without a status token is kept as written.
        var output = new BenchmarkReportWriterOutput { Headline = "The models differ on every axis." };
        foreach (var (slot, text) in sections) output.Sections[slot] = text;
        var cleaned = BenchmarkReportPackValidator.DropInvalid(Rca, output, sheet, new BenchmarkReportContentSnapshot());
        Assert.Equal(uncited, cleaned.Output.Sections[BenchmarkReportSlots.AnswerBest]);
        Assert.False(cleaned.Output.Sections.ContainsKey(BenchmarkReportSlots.AnswerWorst));
    }

    [Fact]
    public void ACertaintyWordInAnAnswer_StillNeedsItsSupport()
    {
        var notes = ValidateSections(QuestionSheet(1, 3), new Dictionary<string, string>
        {
            [BenchmarkReportSlots.AnswerBest] = "It certainly lost points to {{rca.hypothesis.HQ1.1.label}}.",
            [BenchmarkReportSlots.AnswerSpeed] = "It is certainly slower through {{rca.hypothesis.HS1.1.label}}.",
        });

        Assert.Contains(notes, n => n.Rule == BenchmarkReportPackValidator.RootCauseCertaintyRule
                                    && n.Location.StartsWith("sections." + BenchmarkReportSlots.AnswerBest, StringComparison.Ordinal));
        Assert.DoesNotContain(notes, n => n.Rule == BenchmarkReportPackValidator.RootCauseCertaintyRule
                                          && n.Location.StartsWith("sections." + BenchmarkReportSlots.AnswerSpeed, StringComparison.Ordinal));
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
