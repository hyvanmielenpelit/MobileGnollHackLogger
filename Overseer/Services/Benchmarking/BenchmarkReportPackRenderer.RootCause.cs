namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services.Benchmarking.RootCause;

/// <summary>
/// The Root Cause Analysis, in comparison scope (every covered model as an equal) and model scope (a subject
/// with its peers, or a run or battery run on its own with its controls), answer first. The body: the summary
/// (the headline, the main findings code states, each research question with the writer's answer, the models at a
/// glance and how sure the grades can be); the key terms the summary and the conclusions use
/// (<see cref="RootCauseGlossary"/>), each term glossed once in the text, at its first use; why each model scored as
/// it did; scope and design; the evidence on intelligence, speed and cost; the hypotheses per axis with the writer's
/// own; the conclusions; the runs to make next; limitations and threats to validity. Then the lettered appendices:
/// A intelligence detail (in comparison scope, without the results the body gives, and only the paired tests the
/// body does not print), B speed and cost decompositions, C hypothesis details, D run settings, E the known counts
/// and configuration facts, F reproducibility, G the cited and critical-error questions with their answer excerpts
/// and H glossary, the letters after D closing up when there is no known count or configuration fact to list;
/// and, in the Markdown copy, the fact sheet. A table column that is zero or not recorded in every row is left out,
/// and the table's note says so. Every table is computed by code from the stored
/// <see cref="RootCauseResult"/> and facts; the writer's slots follow the code-rendered part of their section.
/// The headline, the questions, the caveats, the writer's hypotheses and the conclusions stand in GitHub alert
/// callouts (<c>&gt; [!NOTE]</c> and the like), each opened by a bold label. Figures go at the four Root Cause
/// Analysis anchors. Models are named, or lettered in an anonymized copy, except the subject of a per-model
/// document and the controls, which are evidence and always named.
/// </summary>
public static partial class BenchmarkReportPackRenderer
{
    private const string RcaAtRiskNote = "Index points at risk: the points lost on the answers showing each signal, relative to the model's "
        + "own median answer, each answer counted once under its first signal. They are an upper bound, not a causal estimate.";

    private const string RcaDecompositionNote = "Each difference is split into parts that add up exactly to it (a standard method, the "
        + "logarithmic-mean Divisia index), using only the questions both models have figures for. A share's interval comes from "
        + "resampling those questions (a bootstrap interval).";

    private const string RcaGradeWordsNote = "How sure each answer is. **Established (High)**: measured, still clear after the correction for "
        + "many comparisons, robust in every check, not explained by another setting, and each model ran at least twice per suite. "
        + "**Probable (Moderate)**: one of those is missing. **Suggestive (Low)**: a lead only; it is not measured, two or more are "
        + "missing, or the difference may be chance. **Ruled out (Not supported)**: the hypothesis's own test contradicts it.";

    private const string RcaWriterHypothesesNote = "Hypotheses the report writer added from the grader comments and excerpts. Code has not "
        + "tested them, so their confidence is Low.";

    /// <summary>The lead of the Key terms section, naming the glossary by its <paramref name="letter"/>.</summary>
    private static string RcaKeyTermsLead(string letter) => "The terms the summary and the conclusions use, each in plain words. A term is also explained where it "
        + "first appears, and Appendix " + letter + " defines every term this document uses.";

    /// <summary>The note under a cost or token table whose output column marks a merged figure with <c>*</c>; the mark is escaped, so Markdown prints it.</summary>
    private const string RcaThinkingIncludedNote = "\\* Thinking included; the provider does not report it separately.";

    private const string RcaShareNote = "Share: how much of the difference, or of the total it is part of, this cause accounts for, with its "
        + "95% interval where one was computed. Settled by: the run or check under What to run next that would confirm or rule it out.";

    /// <summary>A list of pairs longer than this goes in bullets under its sentence instead of inside it.</summary>
    private const int RcaInlinePairsMax = 4;

    private const string RcaNoAnalysis = "No root-cause analysis was computed for this document.";

    private const string RcaNoProfiles = "No model profile was computed for this document; regenerate it to get them.";

    private const string RcaControlsNote = "Controls' mean: the mean score of the controls that scored the question. Instrument-suspect: the "
        + "subject and every control scored below 50. Model-specific: the subject scored below 50 while every control scored 75 or above.";

    private const string RcaAnswersBeforeFormat16 = "*Written before format 16; regenerate to get the answers.*";

    private const string RcaAnswersBeforeSlots = "*Written before the answers were added; regenerate to get them.*";

    private static void RenderRootCauseAnalysis(StringBuilder sb, Context ctx)
    {
        var result = ctx.Sheet.RootCause;
        if (ctx.Comparison) ComparisonTitleBlock(sb, ctx);
        else TitleBlock(sb, ctx);

        ctx.RecordsKeyTerms = true;
        RcaSummary(sb, ctx, result);
        ctx.RecordsKeyTerms = false;

        // The Key terms section lists the terms the Summary and the Conclusions use, so the rest is written first.
        var rest = new StringBuilder();
        RcaWhyEachModel(rest, ctx, result);
        RcaScope(rest, ctx, result);
        RcaIntelligence(rest, ctx, result);
        RcaSpeed(rest, ctx, result);
        RcaCost(rest, ctx, result);
        RcaHypotheses(rest, ctx, result);
        ctx.RecordsKeyTerms = true;
        RcaConclusions(rest, ctx, result);
        ctx.RecordsKeyTerms = false;
        RcaNextRuns(rest, ctx, result);
        RcaLimitations(rest, ctx, result);

        var letters = new RcaAppendixLetters(result == null || RcaListedKnownFacts(result).Count > 0);
        RcaAppendixList(rest, letters);
        RcaIntelligenceDetail(rest, ctx, result);
        RcaDecompositionDetail(rest, ctx, result);
        RcaHypothesisDetail(rest, ctx, result);
        RcaRunSettings(rest, ctx, result);
        if (letters.KnownFacts) RcaKnownFacts(rest, ctx, result);
        string reproducibility = "## Appendix " + letters.Reproducibility + ": Reproducibility";
        if (ctx.Comparison) ComparisonReproducibility(rest, ctx, reproducibility);
        else Reproducibility(rest, ctx, reproducibility);
        RcaQuestionDetails(rest, ctx, result, letters.QuestionDetails);
        RcaGlossary(rest, ctx, letters.Glossary);
        RcaFactSheet(rest, ctx);

        RcaKeyTerms(sb, ctx, letters.Glossary);
        sb.Append(rest);
    }

    /// <summary>
    /// The letters of the appendices after D: E the known facts where the document has a count or a configuration fact
    /// to list (or no analysis at all, which E says), then reproducibility, the question details and the glossary,
    /// lettered on without a gap when E is left out.
    /// </summary>
    private sealed record RcaAppendixLetters(bool KnownFacts)
    {
        public string Reproducibility => KnownFacts ? "F" : "E";

        public string QuestionDetails => KnownFacts ? "G" : "F";

        public string Glossary => KnownFacts ? "H" : "G";
    }

    // ---------------------------------------------------------------------------------------------
    // Terms
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The Key terms section, after the Summary: every term the Summary and the Conclusions use
    /// (<see cref="Context.KeyTerms"/>), alphabetically, each with its short plain gloss; nothing when they use none.
    /// The glossary, Appendix <paramref name="glossaryLetter"/>, keeps every term of the document.
    /// </summary>
    private static void RcaKeyTerms(StringBuilder sb, Context ctx, string glossaryLetter)
    {
        var terms = RootCauseGlossary.Alphabetical(ctx.KeyTerms);
        if (terms.Count == 0) return;

        Heading(sb, "## Key terms");
        Line(sb, RcaKeyTermsLead(glossaryLetter));
        Line(sb);
        Line(sb, "| Term | What it means |");
        Line(sb, "|---|---|");
        foreach (var term in terms)
        {
            Line(sb, "| " + Cell(term.Name) + " | " + Cell(Capitalized(term.Gloss)) + ". |");
        }
        Line(sb);
    }

    /// <summary>
    /// Marks each term as used by the document, and by the Key terms section while <see cref="Context.RecordsKeyTerms"/>;
    /// nothing outside a Root Cause Analysis. An unknown key throws.
    /// </summary>
    private static void UseTerms(Context ctx, params string[] keys)
    {
        if (!ctx.TracksTerms) return;
        foreach (string key in keys)
        {
            RootCauseGlossary.Get(key);
            ctx.UsedTerms.Add(key);
            if (ctx.RecordsKeyTerms) ctx.KeyTerms.Add(key);
        }
    }

    /// <summary>
    /// Marks each term as used and as explained, for a text that explains it in its own words, so no later text
    /// glosses it again; nothing outside a Root Cause Analysis.
    /// </summary>
    private static void ExplainedTerms(Context ctx, params string[] keys)
    {
        if (!ctx.TracksTerms) return;
        UseTerms(ctx, keys);
        ctx.GlossedTerms.UnionWith(keys);
    }

    /// <summary>
    /// Marks the term as used and returns <c> (its gloss)</c> the first time a Root Cause Analysis prints it, to
    /// follow the term; empty after that, and in any other document.
    /// </summary>
    private static string Gloss(Context ctx, string key)
    {
        if (!ctx.TracksTerms) return string.Empty;
        var term = RootCauseGlossary.Get(key);
        UseTerms(ctx, key);
        return ctx.GlossedTerms.Add(key) ? " (" + term.Gloss + ")" : string.Empty;
    }

    /// <summary>The Grading bullet of a setup, with the scoring levels and the critical error glossed at their first use.</summary>
    private static string GradingLine(Context ctx, string weights, string levels, string criticalErrorCap)
    {
        UseTerms(ctx, RootCauseGlossary.Dimension, RootCauseGlossary.AnswerQuality);
        return "- **Grading:** each answer is graded on accuracy, completeness, conciseness and readability, weighted " + weights
            + ". Each dimension is graded on behaviorally anchored levels" + Gloss(ctx, RootCauseGlossary.ScoringLevels) + " scored " + levels
            + ". A critical error" + Gloss(ctx, RootCauseGlossary.CriticalError) + " caps the answer's quality at " + criticalErrorCap + ".";
    }

    /// <summary>The Formulas bullet of a setup, ending in <paramref name="costSentence"/>, with its two means glossed at their first use.</summary>
    private static string FormulasLine(Context ctx, string costSentence)
    {
        UseTerms(ctx, RootCauseGlossary.IntelligenceIndex, RootCauseGlossary.MedianAndMean);
        return "- **Formulas:** answer quality is the weighted geometric mean" + Gloss(ctx, RootCauseGlossary.GeometricMean)
            + " of the four dimension scores, capped by a critical error; in a panel run it is the mean of both graders' scores. "
            + "The Intelligence Index is the difficulty-weighted mean" + Gloss(ctx, RootCauseGlossary.DifficultyWeighted)
            + " of answer quality. Median answer time is the median model time per answer, with tool time excluded. " + costSentence;
    }

    /// <summary>The terms of the design: the grader roles the sheet records, suite or battery, runs per suite, controls, the baseline model.</summary>
    private static void UseDesignTerms(Context ctx)
    {
        UseTerms(ctx, ctx.Battery || (ctx.Comparison && IsBatteryComparison(ctx)) ? RootCauseGlossary.Battery : RootCauseGlossary.Suite,
            RootCauseGlossary.RunsPerSuite);
        if (ctx.Battery || (ctx.Comparison && IsBatteryComparison(ctx))) UseTerms(ctx, RootCauseGlossary.Suite, RootCauseGlossary.OverallIndex);
        foreach (string role in ctx.Sheet.Graders.Select(g => g.Role ?? string.Empty))
        {
            if (role.Contains("panel member", StringComparison.OrdinalIgnoreCase)) UseTerms(ctx, RootCauseGlossary.PanelMember);
            if (role.Contains("reference reader", StringComparison.OrdinalIgnoreCase)) UseTerms(ctx, RootCauseGlossary.ReferenceReader);
            if (role.Contains("claim verifier", StringComparison.OrdinalIgnoreCase)) UseTerms(ctx, RootCauseGlossary.ClaimVerifier);
        }
        if (RootCauseClaimSupport.IsCompletion(ctx.Sheet)) UseTerms(ctx, RootCauseGlossary.Control);
        if (ctx.Comparison && ctx.Sheet.BaselineModelLetter != null) UseTerms(ctx, RootCauseGlossary.BaselineModel);
    }

    // ---------------------------------------------------------------------------------------------
    // 1. Summary
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The headline as the key finding, then the main findings code states (<see cref="RcaFindings"/>); each research
    /// question with its answer strength, its measured signals and the writer's answer; every model at a glance; and the grade words with the
    /// cap the run counts put on them and, where it stands out, the grader-family gap.
    /// </summary>
    private static void RcaSummary(StringBuilder sb, Context ctx, RootCauseResult? result)
    {
        Heading(sb, "## Summary");

        Heading(sb, "### What we found");
        Callout(sb, "IMPORTANT", new[] { "**Key finding.** " + Prose(ctx, ctx.Writer.Headline) });
        var findings = RcaFindings(ctx, result);
        foreach (string finding in findings)
        {
            Line(sb, "- " + finding);
        }
        if (findings.Count > 0) Line(sb);

        var questions = (result?.Questions ?? new List<RootCauseQuestion>()).OrderBy(q => q.Number).ToList();
        if (questions.Count > 0)
        {
            UseTerms(ctx, RootCauseGlossary.GradeWords, RootCauseGlossary.Hypothesis);
            Heading(sb, "### The questions");
            bool answered = BenchmarkReportSlots.RootCauseAnswerSlots.Any(s => ctx.Writer.Sections.ContainsKey(s));
            foreach (var question in questions)
            {
                var lines = new List<string>
                {
                    "**Question.** **" + SheetText(ctx, question.Text) + "**",
                    string.Empty,
                    "*Answer strength: " + RcaAnswerStrength(question, token => RcaTokenName(ctx, token), result?.Hypotheses) + "*",
                    string.Empty
                };
                if (RcaSignalsLine(ctx, question.Number) is string signals)
                {
                    lines.Add(signals);
                    lines.Add(string.Empty);
                }
                lines.Add(RcaAnswer(ctx, question.Number, answered));
                Callout(sb, "NOTE", lines);
            }
        }

        Heading(sb, "### At a glance");
        RcaGlance(sb, ctx, result);

        Heading(sb, "### How sure are we?");
        UseTerms(ctx, RootCauseGlossary.GradeWords, RootCauseGlossary.Holm, RootCauseGlossary.RobustnessCheck, RootCauseGlossary.Confound,
            RootCauseGlossary.RunsPerSuite);
        if (result?.GradeCap != null) UseTerms(ctx, RootCauseGlossary.ReplicateRun);
        var caveat = new List<string> { "**Caveat.** " + RcaGradeWordsNote, string.Empty };
        caveat.Add(result?.GradeCap is string cap
            ? SheetText(ctx, cap)
            : result == null
                ? RcaNoAnalysis
                : "Every model has two or more runs per suite, so a cause can be graded up to " + RootCauseGrades.WordWithGrade(RootCauseGrades.High) + ".");
        if (result?.ControlLimit is string limit)
        {
            caveat.Add(string.Empty);
            caveat.Add("The intelligence hypotheses that could lie in the model or in the instrument are capped at " + RootCauseGrades.WordWithGrade(limit)
                + (string.IsNullOrWhiteSpace(result.ControlLimitReason) ? "." : ": " + OneLine(result.ControlLimitReason).TrimEnd('.') + "."));
        }
        if (RcaFamilyGapSentence(ctx, result) is string familyGap)
        {
            caveat.Add(string.Empty);
            caveat.Add(familyGap);
        }
        Callout(sb, "WARNING", caveat);
    }

    /// <summary>A no-tool share at or above this, for any model, is one of the Summary's findings.</summary>
    private const double RcaNoToolFindingShare = 0.20;

    /// <summary>
    /// Up to four sentences code builds from the stored figures, for What we found: which models the paired tests
    /// against the reference establish above or below it; the cheapest model and how many others its Intelligence
    /// Index interval overlaps; the dimension lowest for at least two thirds of the models; and each model that
    /// answered 20% or more of its scored answers without a tool call. Empty without an analysis.
    /// </summary>
    private static List<string> RcaFindings(Context ctx, RootCauseResult? result)
    {
        var findings = new List<string>();
        if (result == null) return findings;

        if (ctx.Comparison && RcaReferenceFinding(ctx) is string reference) findings.Add(reference);
        if (RcaCheapestFinding(ctx) is string cheapest) findings.Add(cheapest);
        if (RcaLowestDimension(result.Profiles) is { } lowest && lowest.Count * 3 >= lowest.Of * 2)
        {
            UseTerms(ctx, RootCauseGlossary.Dimension);
            findings.Add(lowest.Text);
        }

        var noTool = result.Evidence
            .Where(e => e.ScoredAnswers > 0 && e.NoToolAnswers / (double)e.ScoredAnswers >= RcaNoToolFindingShare - 1e-9)
            .ToList();
        if (noTool.Count > 0)
        {
            UseTerms(ctx, RootCauseGlossary.NoToolAnswers);
            string Share(RootCauseModelEvidenceSummary e)
                => Inv(e.NoToolAnswers) + " of " + Inv(e.ScoredAnswers) + " scored answers (" + RootCauseHypotheses.Pct(e.NoToolAnswers / (double)e.ScoredAnswers) + ")";
            findings.Add(noTool.Count == 1
                ? RcaTokenName(ctx, noTool[0].Token) + " answered " + Share(noTool[0]) + " without any tool call."
                : "Answers without any tool call: " + string.Join("; ", noTool.Select(e => RcaTokenName(ctx, e.Token) + ", " + Share(e))) + ".");
        }
        return findings;
    }

    /// <summary>
    /// <c>Against the reference, Orion Max, the paired tests establish that Vega Pro scored higher and that Lyra Mini scored
    /// lower; no other difference is established.</c>; null without a reference family or an intelligence test in it.
    /// </summary>
    private static string? RcaReferenceFinding(Context ctx)
    {
        var family = (ctx.Sheet.PairedTests ?? new List<BenchmarkReportPairedFamily>()).FirstOrDefault(f => f.Mode != "AllPairs");
        var tests = family?.Measures.FirstOrDefault(m => m.Measure == BenchmarkPairedTests.IntelligenceMeasure);
        if (family == null || tests == null || tests.NotTestedReason != null || tests.Pairs.Count == 0) return null;

        string reference = family.ReferenceLetter ?? OrderedPeers(ctx.Sheet).FirstOrDefault()?.Letter ?? "A";
        var above = new List<string>();
        var below = new List<string>();
        foreach (var pair in tests.Pairs.Select(p => ReferenceSecond(p, reference)).Where(p => p.SecondLetter == reference))
        {
            if (pair.NotTestedReason != null || pair.AdjustedPValue == null || !pair.Established) continue;
            if (pair.Favors == "First") above.Add(NameOfLetter(ctx, pair.FirstLetter));
            else if (pair.Favors == "Second") below.Add(NameOfLetter(ctx, pair.FirstLetter));
        }

        UseTerms(ctx, RootCauseGlossary.PairedTest, RootCauseGlossary.ReferenceModel);
        string lead = "Against the reference model, " + NameOfLetter(ctx, reference) + ", the paired tests ";
        if (above.Count == 0 && below.Count == 0) return lead + "establish no difference in intelligence.";

        var parts = new List<string>();
        if (above.Count > 0) parts.Add("that " + BenchmarkReportFormat.LetterList(above) + " scored higher");
        if (below.Count > 0) parts.Add("that " + BenchmarkReportFormat.LetterList(below) + " scored lower");
        bool all = above.Count + below.Count == tests.Pairs.Count;
        return lead + "establish " + string.Join(" and ", parts) + (all ? "." : "; no other difference is established.");
    }

    /// <summary>
    /// <c>Vega Pro is the cheapest model, at $0.030 per question; the paired tests above establish that 2 of the 4 other
    /// models differ from it in quality.</c> where a paired intelligence test covers the cheapest model, and otherwise
    /// <c>Vega Pro is the cheapest model, at $0.030 per question, and its Intelligence Index interval overlaps those of 2 of
    /// the other 4 models.</c>; null with fewer than two models with a cost, or without the cheapest model's interval.
    /// </summary>
    private static string? RcaCheapestFinding(Context ctx)
    {
        var rows = RcaRows(ctx);
        var priced = rows.Where(r => !r.Entry.CostDegraded && r.Entry.CostPerQuestionUsd.HasValue).ToList();
        if (priced.Count < 2) return null;

        var cheapest = priced.OrderBy(r => r.Entry.CostPerQuestionUsd!.Value).First();
        if (priced.Count(r => r.Entry.CostPerQuestionUsd == cheapest.Entry.CostPerQuestionUsd) > 1) return null;
        if (cheapest.Entry.QualityLower is not double lower || cheapest.Entry.QualityUpper is not double upper) return null;

        var others = rows.Where(r => !ReferenceEquals(r.Entry, cheapest.Entry) && r.Entry.QualityLower.HasValue && r.Entry.QualityUpper.HasValue).ToList();
        if (others.Count == 0) return null;
        int overlapping = others.Count(r => !(r.Entry.QualityLower!.Value > upper) && !(lower > r.Entry.QualityUpper!.Value));

        string name = PlainName(ctx, cheapest.Peer);
        string price = name + " is the cheapest model, at " + BenchmarkReportFormat.Usd(cheapest.Entry.CostPerQuestionUsd!.Value) + " per question";
        if (ctx.Comparison && cheapest.Peer is { } peer && RcaCheapestPairedTests(ctx, peer.Letter) is { } paired)
        {
            UseTerms(ctx, RootCauseGlossary.PairedTest);
            string which = paired.Tested == rows.Count - 1 ? " other models" : " models they compare it with";
            return price + "; the paired tests" + (paired.Above ? " above" : string.Empty) + " establish "
                + (paired.Differ == 0
                    ? "that none of the " + Inv(paired.Tested) + which + " differs from it in quality."
                    : "that " + Inv(paired.Differ) + " of the " + Inv(paired.Tested) + which + (paired.Differ == 1 ? " differs" : " differ") + " from it in quality.");
        }

        UseTerms(ctx, RootCauseGlossary.IntelligenceIndex, RootCauseGlossary.Interval95);
        string overlap = overlapping == 0
            ? "its Intelligence Index interval overlaps no other model's"
            : "its Intelligence Index interval overlaps " + (overlapping == others.Count ? "those of all " + Inv(others.Count) + " other models"
                : "those of " + Inv(overlapping) + " of the other " + Inv(others.Count) + " models");
        return price + ", and " + overlap + ".";
    }

    /// <summary>
    /// The paired intelligence tests that cover the model with <paramref name="letter"/>: from the reference family when it is
    /// the reference (the tests <see cref="RcaReferenceFinding"/> reads, so they stand above), else from the all-pairs
    /// family, else its one test against the reference. How many were tested and how many establish a difference; null
    /// when none covers it.
    /// </summary>
    private static (int Tested, int Differ, bool Above)? RcaCheapestPairedTests(Context ctx, string letter)
    {
        var families = ctx.Sheet.PairedTests ?? new List<BenchmarkReportPairedFamily>();
        var reference = families.FirstOrDefault(f => f.Mode != "AllPairs");
        var allPairs = families.FirstOrDefault(f => f.Mode == "AllPairs");
        string referenceLetter = reference?.ReferenceLetter ?? OrderedPeers(ctx.Sheet).FirstOrDefault()?.Letter ?? "A";
        bool isReference = reference != null && referenceLetter == letter;

        foreach (var family in isReference ? new[] { reference } : new[] { allPairs, reference })
        {
            var tests = family?.Measures.FirstOrDefault(m => m.Measure == BenchmarkPairedTests.IntelligenceMeasure);
            if (tests == null || tests.NotTestedReason != null) continue;
            var covering = tests.Pairs
                .Where(p => (p.FirstLetter == letter || p.SecondLetter == letter) && p.NotTestedReason == null && p.AdjustedPValue != null)
                .ToList();
            if (covering.Count == 0) continue;
            int differ = covering.Count(p => p.Established && p.Favors is "First" or "Second");
            bool above = isReference && RcaReferenceFinding(ctx) != null;
            return (covering.Count, differ, above);
        }
        return null;
    }

    /// <summary>
    /// For How sure are we?: the gap between the candidates of the two panel members' own providers under each member,
    /// and their contrast, when the contrast's 95% interval excludes zero; null otherwise and on a result stored without
    /// the family gaps. The providers are named unless the copy withholds them.
    /// </summary>
    private static string? RcaFamilyGapSentence(Context ctx, RootCauseResult? result)
    {
        var gap = result?.FamilyGaps?.FirstOrDefault(g => g.IsMemberProviderPair && !g.InsufficientData
            && g.Contrast is { Value: not null } contrast && contrast.ExcludesZero());
        if (gap == null || gap.GapA.Value == null || gap.GapB.Value == null) return null;

        UseTerms(ctx, RootCauseGlossary.PanelMember, RootCauseGlossary.Interval95);
        bool named = !ProviderWithheld(ctx, gap.Provider1.Trim()) && !ProviderWithheld(ctx, gap.Provider2.Trim());
        string pair = named
            ? "models from " + gap.Provider1 + " minus models from " + gap.Provider2 + ", the two panel members' own providers,"
            : "models from one panel member's provider minus models from the other member's provider";
        return "Grader family: the quality gap of " + pair + " is " + RootCauseFacts.GapText(gap.GapA)
               + " under Panel member A and " + RootCauseFacts.GapText(gap.GapB) + " under Panel member B, a contrast of "
               + RootCauseFacts.GapText(gap.Contrast!) + ". The order between models of those two providers depends on the panel's balance.";
    }

    /// <summary>
    /// A research question's measured signals, <c>*Measured signals:* …</c>, from its <c>rca.question.&lt;n&gt;.signals</c>
    /// fact resolved as the writer's prose is; null before <see cref="SignalsLineFormatVersion"/> (in comparison scope
    /// <see cref="ComparisonSignalsLineFormatVersion"/>), or when the fact is missing, unavailable or records none.
    /// </summary>
    private static string? RcaSignalsLine(Context ctx, int question)
    {
        int since = ctx.Comparison ? ComparisonSignalsLineFormatVersion : SignalsLineFormatVersion;
        if (ctx.Document.ReportFormatVersion < since) return null;
        string key = "rca.question." + Inv(question) + ".signals";
        if (Fact(ctx, key) is not { Available: true } fact || string.Equals(fact.Display, "none recorded", StringComparison.Ordinal)) return null;
        return "*Measured signals:* " + Prose(ctx, "{{" + key + "}}");
    }

    /// <summary>
    /// A research question's answer strength, after <c>Answer strength: </c>: the grade with the comparative
    /// hypotheses apart from the model's own within-model ones; the grade with every hypothesis together for a
    /// result stored before engine version 4 or when no hypothesis is graded Low or higher; a note when none fired.
    /// Question 1 with no comparative hypothesis graded Low or higher states its measured lead over the runner-up
    /// (<see cref="RootCauseQuestion.Neighbor"/>, named by <paramref name="name"/>; unnamed in a result stored without
    /// it) and whether that lead is established; question 4 answered from the price cards
    /// (<see cref="RootCauseQuestion.PriceCard"/>) says so and cites its price effect, found among
    /// <paramref name="hypotheses"/>.
    /// </summary>
    internal static string RcaAnswerStrength(
        RootCauseQuestion question, Func<string, string>? name = null, IReadOnlyList<RootCauseHypothesis>? hypotheses = null)
    {
        ArgumentNullException.ThrowIfNull(question);
        string grade = RootCauseGrades.WordWithGrade(question.Grade);
        var within = question.WithinKeys ?? new List<string>();
        string own = within.Count > 0 ? BenchmarkReportFormat.LetterList(within) : string.Empty;

        if (question.Number == 1 && question.ComparativeKeys is { } lead && question.Grade == null && within.Count == 0
            && question.OrderEstablished is bool established)
        {
            string over = question.Neighbor is string neighbor ? " over " + (name?.Invoke(neighbor) ?? neighbor) : string.Empty;
            return "Measured lead" + over + ": " + (established ? "established" : "not established") + " — see its split; "
                   + (lead.Count == 0 ? "no cause compared." : "no cause graded Low or higher, see " + BenchmarkReportFormat.LetterList(lead) + ".");
        }

        if (question.PriceCard && question.ComparativeKeys is { Count: > 0 } priced)
        {
            int low = RootCauseGrades.Rank(RootCauseGrades.Low);
            string key = priced.FirstOrDefault(k => hypotheses?.FirstOrDefault(x => x.Key == k) is { PriceEffect: true } effect
                                                    && RootCauseGrades.Rank(effect.Grade) >= low)
                         ?? priced.FirstOrDefault(k => k.StartsWith("HC2.", StringComparison.Ordinal))
                         ?? priced[0];
            return RootCauseGrades.PriceCardWord + " — see " + key + (within.Count > 0 ? "; its own breakdown, see " + own : string.Empty) + ".";
        }

        if (question.HypothesisKeys.Count == 0) return "No hypothesis about this model fired — see its profile.";
        if (question.ComparativeKeys is not { } comparative || question.Grade == null || (comparative.Count == 0 && !question.WithinOnly))
        {
            return grade + " — see " + BenchmarkReportFormat.LetterList(question.HypothesisKeys) + ".";
        }

        if (question.WithinOnly)
        {
            return grade + " — no hypothesis graded Low or higher compares it with the other models; its own breakdown, see "
                   + own + ", says where its own time, cost or points go.";
        }
        return grade + " — against the other models, see " + BenchmarkReportFormat.LetterList(comparative)
               + (within.Count > 0 ? "; its own breakdown, see " + own : string.Empty) + ".";
    }

    /// <summary>
    /// The writer's answer to a research question; a note when it was not written, or when the writer output
    /// holds no answer slot at all, as a document written before them does.
    /// </summary>
    private static string RcaAnswer(Context ctx, int number, bool answered)
    {
        string slot = BenchmarkReportSlots.RootCauseAnswerSlots[number - 1];
        if (ctx.Writer.Sections.TryGetValue(slot, out var text) && !string.IsNullOrWhiteSpace(text)) return Prose(ctx, text);
        if (answered || (ctx.Comparison && ctx.Document.ReportFormatVersion >= AnswerSlotsFormatVersion)) return "*Not written.*";
        return ctx.Comparison ? RcaAnswersBeforeFormat16 : RcaAnswersBeforeSlots;
    }

    /// <summary>
    /// One row per model in rank order: the Intelligence Index with its interval and rank, the median time,
    /// the cost per question, where it lost most points and what was flagged.
    /// </summary>
    private static void RcaGlance(StringBuilder sb, Context ctx, RootCauseResult? result)
    {
        UseTerms(ctx, RootCauseGlossary.IntelligenceIndex, RootCauseGlossary.Interval95, RootCauseGlossary.MedianAndMean);
        if (result != null) UseFlagTerms(ctx, result.Profiles);
        Line(sb, "| Model | Intelligence | Median time | Cost per question | Where it lost most points | Flags |");
        Line(sb, "|---|---|---|---|---|---|");
        foreach (var (entry, peer) in RcaRankedRows(ctx))
        {
            string token = peer?.Letter ?? RootCauseEngine.SubjectToken;
            string index = entry.QualityIndex.HasValue
                ? BenchmarkReportFormat.Whole(entry.QualityIndex.Value)
                  + (entry.QualityLower.HasValue && entry.QualityUpper.HasValue
                      ? " (" + BenchmarkReportFormat.Whole(entry.QualityLower.Value) + "–" + BenchmarkReportFormat.Whole(entry.QualityUpper.Value) + ")"
                      : string.Empty)
                  + ", rank " + QualityRankCell(ctx, entry)
                : BenchmarkReportFacts.NotAvailable;
            var profile = result?.Profiles.FirstOrDefault(p => p.Token == token);
            string lost = profile?.Weaknesses.FirstOrDefault(w => w.Kind == RootCauseProfiles.LostPointsOn)?.Short ?? NoValue;
            string flags = profile == null ? NoValue : profile.Flags.Count == 0 ? "none" : string.Join("; ", profile.Flags.Select(f => f.Short));
            Line(sb, RcaRow(RcaRowName(ctx, peer), index, TimeCell(entry), CostCell(entry), Cell(SheetText(ctx, lost)), Cell(SheetText(ctx, flags))));
        }
        Line(sb);
    }

    // ---------------------------------------------------------------------------------------------
    // 2. Why each model scored as it did
    // ---------------------------------------------------------------------------------------------

    private static void RcaWhyEachModel(StringBuilder sb, Context ctx, RootCauseResult? result)
    {
        Heading(sb, "## Why each model scored as it did");
        if (result == null || result.Profiles.Count == 0)
        {
            Line(sb, result == null ? RcaNoAnalysis : RcaNoProfiles);
            Line(sb);
            return;
        }

        Line(sb, "| Model | Index | Strengths | Lost points on | Flags |");
        Line(sb, "|---|---|---|---|---|");
        foreach (var (entry, peer) in RcaRankedRows(ctx))
        {
            string token = peer?.Letter ?? RootCauseEngine.SubjectToken;
            var profile = result.Profiles.FirstOrDefault(p => p.Token == token);
            if (profile == null) continue;

            string strengths = profile.Strengths.Count == 0 ? "none ranked first or second" : string.Join("; ", profile.Strengths.Select(s => s.Text));
            var lost = new List<string>();
            foreach (var weakness in profile.Weaknesses)
            {
                lost.Add(weakness.Kind == RootCauseProfiles.WeakestDimension ? "weakest dimension " + weakness.Text : weakness.Text);
            }
            string flags = profile.Flags.Count == 0 ? "none" : string.Join("; ", profile.Flags.Select(f => f.Text));
            Line(sb, RcaRow(
                RcaRowName(ctx, peer),
                entry.QualityIndex.HasValue ? BenchmarkReportFormat.Whole(entry.QualityIndex.Value) : NoValue,
                Cell(SheetText(ctx, strengths)),
                Cell(SheetText(ctx, lost.Count == 0 ? "no answer below the model's median" : string.Join("; ", lost))),
                Cell(SheetText(ctx, flags))));
        }
        Line(sb);
        Line(sb, "*" + RcaProfileNote(ctx, result) + "*");
        Line(sb);
    }

    /// <summary>
    /// The note under the profiles: what an entry is, what Lost points on measures, a gloss for each kind of flag the
    /// profiles show at its first use, and two rules behind the flags, each stated once: answers without tools are
    /// flagged above the models' median share, and an isolated miss is set against the other models' median.
    /// </summary>
    private static string RcaProfileNote(Context ctx, RootCauseResult result)
    {
        var profiles = result.Profiles;
        UseFlagTerms(ctx, profiles);
        string note = "Each entry is a count, a share or a rank measured over the model's scored answers; none says why. "
            + "Lost points on: the largest share of the model's index points at risk" + Gloss(ctx, RootCauseGlossary.PointsAtRisk)
            + ", with its questions (Appendix A).";

        var kinds = new HashSet<string>(profiles.SelectMany(p => p.Flags).Select(f => f.Kind), StringComparer.Ordinal);
        var flags = new List<string>();
        foreach (var (kind, key, words) in new[]
        {
            (RootCauseProfiles.CriticalErrors, RootCauseGlossary.CriticalError, "critical errors"),
            (RootCauseProfiles.NoTool, RootCauseGlossary.NoToolAnswers, "answers without tools"),
            (RootCauseProfiles.PanelDisagreement, RootCauseGlossary.PanelDisagreement, "panel disagreement"),
            (RootCauseProfiles.IsolatedMisses, RootCauseGlossary.IsolatedMiss, "isolated misses")
        })
        {
            if (kinds.Contains(kind) && Gloss(ctx, key) is { Length: > 0 } gloss) flags.Add(words + gloss);
        }
        if (flags.Count > 0) note += " Flags: " + string.Join("; ", flags) + ".";

        var rules = new List<string>();
        if (kinds.Contains(RootCauseProfiles.NoTool) && RcaNoToolMedian(result) is double median)
        {
            rules.Add("Answers without tools are flagged where a model's share of them is above the models' median of " + RootCauseHypotheses.Pct(median) + ".");
        }
        if (kinds.Contains(RootCauseProfiles.IsolatedMisses))
        {
            rules.Add("An isolated miss is a question the model scored " + BenchmarkReportFormat.Whole(BenchmarkComparisonModelProfile.IsolatedMissAtMost)
                + " or less on while the other models' median was " + BenchmarkReportFormat.Whole(BenchmarkComparisonModelProfile.IsolatedMissOthersMedianAtLeast)
                + " or more.");
        }
        return rules.Count == 0 ? note : note + " " + string.Join(" ", rules);
    }

    /// <summary>The models' median share of answers without tools over their scored answers, as the profiles flag against it; null with fewer than two models with scored answers.</summary>
    private static double? RcaNoToolMedian(RootCauseResult result)
    {
        var shares = result.Evidence.Where(e => e.ScoredAnswers > 0).Select(e => e.NoToolAnswers / (double)e.ScoredAnswers).OrderBy(s => s).ToList();
        if (shares.Count < 2) return null;
        int mid = shares.Count / 2;
        return shares.Count % 2 == 1 ? shares[mid] : (shares[mid - 1] + shares[mid]) / 2.0;
    }

    /// <summary>The terms the profiles' entries name: points at risk, dimensions and each kind of flag shown.</summary>
    private static void UseFlagTerms(Context ctx, IReadOnlyList<RootCauseModelProfile> profiles)
    {
        foreach (var entry in profiles.SelectMany(p => p.Weaknesses.Concat(p.Flags)))
        {
            switch (entry.Kind)
            {
                case RootCauseProfiles.LostPointsOn: UseTerms(ctx, RootCauseGlossary.PointsAtRisk); break;
                case RootCauseProfiles.WeakestDimension: UseTerms(ctx, RootCauseGlossary.Dimension); break;
                case RootCauseProfiles.CriticalErrors: UseTerms(ctx, RootCauseGlossary.CriticalError); break;
                case RootCauseProfiles.NoTool: UseTerms(ctx, RootCauseGlossary.NoToolAnswers); break;
                case RootCauseProfiles.PanelDisagreement: UseTerms(ctx, RootCauseGlossary.PanelDisagreement); break;
                case RootCauseProfiles.IsolatedMisses: UseTerms(ctx, RootCauseGlossary.IsolatedMiss); break;
            }
        }
    }

    // ---------------------------------------------------------------------------------------------
    // 3. Scope and design
    // ---------------------------------------------------------------------------------------------

    private static void RcaScope(StringBuilder sb, Context ctx, RootCauseResult? result)
    {
        Heading(sb, "## Scope and design");
        UseDesignTerms(ctx);
        if (ctx.Comparison)
        {
            ComparisonSetup(sb, ctx, "### Setup and method", comparedModels: false);
            ComparisonAvailabilityTable(sb, ctx);
        }
        else
        {
            Line(sb, RcaOriginText(ctx, result));
            Line(sb);
            SetupAndMethod(sb, ctx, "### Setup and method", comparedModels: false);
            if (RootCauseClaimSupport.IsCompletion(ctx.Sheet))
            {
                RcaControls(sb, ctx, result);
            }
            ProviderAvailabilitySection(sb, ctx);
        }
        Slot(sb, ctx, BenchmarkReportSlots.Scope);
    }

    /// <summary>What a per-model analysis is about: a run or battery run on its own, or the subject among its peers.</summary>
    private static string RcaOriginText(Context ctx, RootCauseResult? result)
    {
        if (!RootCauseClaimSupport.IsCompletion(ctx.Sheet))
        {
            int peers = ctx.Sheet.Peers.Count;
            return "This analysis sets " + ctx.Sheet.SubjectLabel + " against " + Inv(peers) + (peers == 1 ? " peer" : " peers")
                + " of the same comparison.";
        }

        string what = ctx.Sheet.Battery is { } battery
            ? "battery run " + Inv(battery.BatteryRunId)
            : (ctx.Sheet.SubjectRunIds.Count == 1 ? "run " : "runs ") + string.Join(", ", ctx.Sheet.SubjectRunIds.Select(id => Inv(id)));
        int controls = result?.Controls.Count ?? 0;
        return "This analysis describes " + what + " of " + ctx.Sheet.SubjectLabel + " on its own. Other models enter only as controls: "
            + (controls == 0 ? "none qualified." : Inv(controls) + " qualified.");
    }

    /// <summary>The controls of a run- or battery-completion analysis, the control limit and the deficit against them.</summary>
    private static void RcaControls(StringBuilder sb, Context ctx, RootCauseResult? result)
    {
        Heading(sb, "### Controls");
        var controls = result?.Controls ?? new List<RootCauseControl>();
        if (controls.Count == 0)
        {
            Line(sb, "No earlier run of another model answered these questions under the same graders, scoring and prompt options. "
                + "Speed and cost are analyzed from the model's own accounting. The intelligence hypotheses that could lie in the model or in "
                + "the instrument are capped at Low, and the intelligence axis stays not established until a control run exists.");
            Line(sb);
            return;
        }

        Line(sb, "Earlier runs of other models on the same exam under the same instrument; their per-question scores tell how hard each question was.");
        Line(sb);
        Line(sb, "| Control | Provider | Run | Date (UTC) | Shared questions |");
        Line(sb, "|---|---|---|---|---|");
        foreach (var control in controls.OrderBy(c => c.Number))
        {
            string name = control.LabelWithLevel();
            string run = control.RunId is long runId ? "Run " + Inv(runId)
                : control.BatteryRunId is long batteryRunId ? "Battery run " + Inv(batteryRunId)
                : NoValue;
            Line(sb, "| " + Cell(name) + " | " + Cell(control.Provider) + " | " + run + " | "
                + control.DateUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " | " + Inv(control.SharedQuestions) + " |");
        }
        Line(sb);

        Line(sb, "- **Control limit:** " + (result!.ControlLimit == null
            ? "None: two or more controls, at least one from another provider, so the intelligence hypotheses are graded as any other."
            : "The intelligence hypotheses that could lie in the model or in the instrument are capped at " + RootCauseGrades.Display(result.ControlLimit)
              + (string.IsNullOrWhiteSpace(result.ControlLimitReason) ? "." : ": " + OneLine(result.ControlLimitReason).TrimEnd('.') + ".")));
        if (result.ControlDeficitMean is double mean)
        {
            UseTerms(ctx, RootCauseGlossary.Interval95);
            string interval = result.ControlDeficitLower is double lo && result.ControlDeficitUpper is double hi
                ? " (95% interval " + BenchmarkReportFormat.SignedOneDecimal(lo) + " to " + BenchmarkReportFormat.SignedOneDecimal(hi) + ")"
                : string.Empty;
            Line(sb, "- **Against the controls:** " + BenchmarkReportFormat.SignedOneDecimal(mean) + " points on average over "
                + Inv(result.ControlItems.Count) + " shared questions" + interval + ".");
        }
        Line(sb);
    }

    // ---------------------------------------------------------------------------------------------
    // 4. Evidence: intelligence
    // ---------------------------------------------------------------------------------------------

    private static void RcaIntelligence(StringBuilder sb, Context ctx, RootCauseResult? result)
    {
        Heading(sb, "## Evidence: intelligence");
        RcaScoreboard(sb, ctx);
        RcaDimensions(sb, ctx, result);
        Figures(sb, ctx, BenchmarkReportChartAnchor.RcaIntelligence);

        if (ctx.Comparison)
        {
            RcaReferenceTests(sb, ctx, BenchmarkPairedTests.IntelligenceMeasure);
        }
        else if (!ctx.Standalone)
        {
            RcaPeerDifferences(sb, ctx);
        }

        RcaObservations(sb, ctx);
        Slot(sb, ctx, BenchmarkReportSlots.EvidenceIntelligence);
    }

    /// <summary>
    /// Every model's Intelligence Index, its 95% interval, its rank and its critical errors: five columns, so a
    /// name is never broken mid-word. Speed and cost are under their own evidence.
    /// </summary>
    private static void RcaScoreboard(StringBuilder sb, Context ctx)
    {
        Line(sb, "| Model | " + Label("quality.index") + " | " + Label("quality.interval") + " | Rank | " + Label("errors.critical") + " |");
        Line(sb, "|---|---|---|---|---|");
        foreach (var (entry, peer) in RcaRows(ctx))
        {
            string index = entry.QualityIndex.HasValue ? BenchmarkReportFormat.Whole(entry.QualityIndex.Value) : BenchmarkReportFacts.NotAvailable;
            string interval = entry.QualityLower.HasValue && entry.QualityUpper.HasValue
                ? BenchmarkReportFormat.Whole(entry.QualityLower.Value) + "–" + BenchmarkReportFormat.Whole(entry.QualityUpper.Value)
                : BenchmarkReportFacts.NotAvailable;
            Line(sb, RcaRow(RcaRowName(ctx, peer), index, interval, QualityRankCell(ctx, entry), CriticalErrorsCell(ctx, entry, peer)));
        }
        Line(sb);
        UseTerms(ctx, RootCauseGlossary.IntelligenceIndex, RootCauseGlossary.CriticalError);
        ExplainedTerms(ctx, RootCauseGlossary.TiedRank);
        Line(sb, "*A rank shown as a span, such as 5–9 (tied), is a tied rank: the model's 95% interval" + Gloss(ctx, RootCauseGlossary.Interval95)
            + " overlaps others', so its rank is the span of places the interval allows. " + RoundedBoundsNote + "*");
        Line(sb);
    }

    /// <summary>
    /// Each model's four dimensions from its profile, then one sentence on the dimension that is lowest for
    /// most models and how far below Accuracy it lies; the comparison's dimension table on a result without
    /// profiles.
    /// </summary>
    private static void RcaDimensions(StringBuilder sb, Context ctx, RootCauseResult? result)
    {
        var profiles = result?.Profiles ?? new List<RootCauseModelProfile>();
        UseTerms(ctx, RootCauseGlossary.Dimension);
        if (profiles.Count == 0)
        {
            if (ctx.Comparison) DimensionsTable(sb, ctx);
            return;
        }

        Line(sb, "| Model | " + string.Join(" | ", BenchmarkReportFactLabels.Dimensions.Select(d => d.Name)) + " |");
        Line(sb, "|---|" + string.Concat(BenchmarkReportFactLabels.Dimensions.Select(_ => "---|")));
        foreach (var (_, peer) in RcaRows(ctx))
        {
            string token = peer?.Letter ?? RootCauseEngine.SubjectToken;
            var profile = profiles.FirstOrDefault(p => p.Token == token);
            if (profile == null) continue;
            var cells = Enumerable.Range(0, 4).Select(k => k < profile.Dimensions.Count && profile.Dimensions[k] is double v ? BenchmarkReportFormat.Whole(v) : NoValue);
            Line(sb, "| " + RcaRowName(ctx, peer) + " | " + string.Join(" | ", cells) + " |");
        }
        Line(sb);

        if (RcaLowestDimension(profiles) is { } lowest)
        {
            Line(sb, lowest.Text);
            Line(sb);
        }
    }

    /// <summary>
    /// The dimension lowest for the most models, in a sentence: <c>Completeness is the lowest dimension for 9 of 9 models
    /// (11.0 to 37.0 points below Accuracy), and at least 13 points below Accuracy for 8 of them.</c> The second clause counts
    /// the models the response-style rule (<see cref="BenchmarkChatTransfer.HasResponseStyleConflict"/>) marks, Completeness
    /// strictly lowest and at least 13 points below Accuracy, and is left out when it marks every one of them. With the
    /// models counted and the models with all four dimensions; null with fewer than two such models, or when no dimension
    /// is lowest for two of them.
    /// </summary>
    private static (string Text, int Count, int Of)? RcaLowestDimension(IReadOnlyList<RootCauseModelProfile> profiles)
    {
        var complete = profiles
            .Where(p => p.Dimensions.Count == 4 && p.Dimensions.All(d => d.HasValue))
            .Select(p => p.Dimensions.Select(d => d!.Value).ToArray())
            .ToList();
        if (complete.Count < 2) return null;

        var lowest = complete
            .Where(d => d.Max() - d.Min() > 0.05)
            .Select(d => (Dimensions: d, Dimension: Array.IndexOf(d, d.Min()), Gap: d[0] - d.Min()))
            .GroupBy(x => x.Dimension)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key)
            .FirstOrDefault();
        if (lowest == null || lowest.Count() < 2) return null;

        int count = lowest.Count();
        string name = BenchmarkReportFactLabels.Dimensions[lowest.Key].Name;
        string text = name + " is the lowest dimension for " + Inv(count) + " of " + Inv(complete.Count) + " models";
        if (lowest.Key == 0) return (text + ".", count, complete.Count);

        double low = lowest.Min(x => x.Gap), high = lowest.Max(x => x.Gap);
        string gaps = BenchmarkReportFormat.OneDecimal(low) == BenchmarkReportFormat.OneDecimal(high)
            ? BenchmarkReportFormat.OneDecimal(low)
            : BenchmarkReportFormat.OneDecimal(low) + " to " + BenchmarkReportFormat.OneDecimal(high);
        text += " (" + gaps + " points below Accuracy)";

        if (lowest.Key == 1)
        {
            int marked = lowest.Count(x => BenchmarkChatTransfer.HasResponseStyleConflict(false, x.Dimensions[0], x.Dimensions[1], x.Dimensions[2], x.Dimensions[3]));
            if (marked < count)
            {
                text += ", and at least " + BenchmarkReportFormat.CompactDecimal(BenchmarkChatTransfer.LevelStepThreshold) + " points below Accuracy for "
                        + (marked == 0 ? "none" : Inv(marked)) + " of them";
            }
        }
        return (text + ".", count, complete.Count);
    }

    /// <summary>The paired-test family the body prints: the reference family, else the first.</summary>
    private static BenchmarkReportPairedFamily? RcaBodyFamily(Context ctx)
    {
        var families = ctx.Sheet.PairedTests ?? new List<BenchmarkReportPairedFamily>();
        return families.FirstOrDefault(f => f.Mode != "AllPairs") ?? families.FirstOrDefault();
    }

    /// <summary>
    /// The body family's paired tests of one measure (<see cref="RcaBodyFamily"/>), one row per pair with the reference
    /// second, with the family's adjustment stated once under the table; nothing in a comparison without paired tests
    /// but the reason. The family's single-run caveat is printed only when How sure are we? does not state the cap.
    /// </summary>
    private static void RcaReferenceTests(StringBuilder sb, Context ctx, string measure)
    {
        var families = ctx.Sheet.PairedTests ?? new List<BenchmarkReportPairedFamily>();
        var family = RcaBodyFamily(ctx);
        bool intelligence = measure == BenchmarkPairedTests.IntelligenceMeasure;
        string name = intelligence ? "Intelligence" : measure == BenchmarkPairedTests.SpeedMeasure ? "Speed" : "Cost";

        if (family == null)
        {
            if (!intelligence) return;
            Heading(sb, "### Paired tests");
            Line(sb, "No pair of models was tested" + (string.IsNullOrWhiteSpace(ctx.Sheet.PairedTestsUnavailableReason) ? "." : ": " + SheetText(ctx, ctx.Sheet.PairedTestsUnavailableReason))
                + " A gap between two models may be noise.");
            Line(sb);
            return;
        }

        var tests = family.Measures.FirstOrDefault(m => m.Measure == measure);
        if (tests == null) return;

        string reference = family.ReferenceLetter ?? OrderedPeers(ctx.Sheet).FirstOrDefault()?.Letter ?? "A";
        if (family.Mode != "AllPairs") UseTerms(ctx, RootCauseGlossary.ReferenceModel);
        Heading(sb, family.Mode == "AllPairs" ? "### Paired tests" : "### Paired tests against " + NameOfLetter(ctx, reference));
        if (tests.NotTestedReason != null || tests.Pairs.Count == 0)
        {
            Line(sb, SheetText(ctx, FamilySizeText(ctx, name, tests)));
            Line(sb);
            return;
        }

        var pairs = family.Mode == "AllPairs"
            ? tests.Pairs
            : tests.Pairs.Select(p => ReferenceSecond(p, reference, ratio: !intelligence)).ToList();
        if (intelligence)
        {
            Line(sb, "| Pair | Questions | Difference (95% interval) | Result |");
            Line(sb, "|---|---|---|---|");
            foreach (var pair in pairs)
            {
                string difference = pair.Effect is double effect
                    ? BenchmarkReportFormat.SignedOneDecimal(effect) + " points"
                      + (pair.Lower is double l && pair.Upper is double u
                          ? " (" + BenchmarkReportFormat.SignedOneDecimal(l) + " to " + BenchmarkReportFormat.SignedOneDecimal(u) + ")"
                          : string.Empty)
                    : NoValue;
                Line(sb, RcaRow(Cell(PairName(ctx, pair)), Inv(pair.PairedItems), difference, Cell(ResultText(ctx, pair, "scored higher"))));
            }
        }
        else
        {
            bool speed = measure == BenchmarkPairedTests.SpeedMeasure;
            Line(sb, "| Pair | " + (speed ? "Time ratio" : "Spend ratio") + " (95% interval) | Result |");
            Line(sb, "|---|---|---|");
            foreach (var pair in pairs)
            {
                Line(sb, RcaRow(Cell(PairName(ctx, pair)), RatioCell(pair), Cell(ResultText(ctx, pair, speed ? "was faster" : "was cheaper"))));
            }
        }
        Line(sb);

        UseTerms(ctx, RootCauseGlossary.Interval95);
        if (!intelligence) ExplainedTerms(ctx, RootCauseGlossary.Ratio);
        string second = family.Mode == "AllPairs" ? "second" : "the reference";
        string explain = intelligence
            ? " Difference: the mean per-question difference, first model minus " + second + ", on the questions both answered."
            : " A ratio is the first model's own " + (measure == BenchmarkPairedTests.SpeedMeasure ? "time" : "spend") + " per question divided by "
              + (family.Mode == "AllPairs" ? "the second's" : "the reference's") + ", on the questions both answered.";
        string paired = "Each row is a paired test" + Gloss(ctx, RootCauseGlossary.PairedTest) + ". ";
        bool allPairsElsewhere = families.Any(f => f.Mode == "AllPairs" && !ReferenceEquals(f, family));
        Line(sb, "*" + paired + SheetText(ctx, FamilySizeText(ctx, name, tests)) + explain
            + (allPairsElsewhere ? " Appendix A gives the tests among all pairs." : string.Empty) + "*");
        Line(sb);
        if (intelligence && ctx.Sheet.RootCause?.GradeCap == null && !string.IsNullOrWhiteSpace(family.SingleRunCaveat))
        {
            Line(sb, "*" + SheetText(ctx, family.SingleRunCaveat) + "*");
            Line(sb);
        }
    }

    /// <summary>A per-model sheet's paired difference against each peer, with its interval; not a test.</summary>
    private static void RcaPeerDifferences(StringBuilder sb, Context ctx)
    {
        var peers = OrderedPeers(ctx.Sheet);
        if (!peers.Any(p => Fact(ctx, BenchmarkReportFacts.PeerPrefix(p.Letter) + "pairedDifference") != null)) return;

        UseTerms(ctx, RootCauseGlossary.Interval95, RootCauseGlossary.BootstrapInterval);
        Heading(sb, "### Paired differences against the peers");
        Line(sb, "| Peer | Paired difference (95% interval) |");
        Line(sb, "|---|---|");
        foreach (var peer in peers)
        {
            Line(sb, RcaRow(TableName(ctx, peer), PairedCell(ctx, peer)));
        }
        Line(sb);
        Line(sb, "*" + PairedDifferenceNote + "*");
        Line(sb);
    }

    /// <summary>
    /// The writer's observations, each with its evidence and how each critical error on a question it cites was
    /// resolved; a document stored without the resolutions prints none.
    /// </summary>
    private static void RcaObservations(StringBuilder sb, Context ctx)
    {
        Heading(sb, "### What the graders recorded");
        var observations = ctx.Writer.Observations ?? new List<BenchmarkReportWriterItem>();
        if (observations.Count == 0)
        {
            Line(sb, "No observations were recorded.");
        }
        foreach (var item in observations)
        {
            Line(sb, "- " + Prose(ctx, item.Text));
            if (!ctx.PrintsEvidence) continue;
            if (ctx.Comparison)
            {
                if (EvidenceText(ctx, item) is { Length: > 0 } evidence) Line(sb, "  - *Evidence:* " + evidence);
            }
            else
            {
                EvidenceLine(sb, ctx, item, listsRefutations: true);
            }
            if (RcaResolutionsText(ctx, item) is { Length: > 0 } resolutions) Line(sb, "  - *Critical errors:* " + resolutions);
        }
        Line(sb);
    }

    /// <summary>
    /// <c>Lyra Mini on Q3: upheld by the claim verifier (cap applied).</c>: how the critical errors on each question the
    /// item cites were resolved. In a comparison, each model's resolutions on the question from its matrix cell, which
    /// covers every answer whether or not it was excerpted, each stated once; from the content's excerpted answers for a
    /// cell stored without them. In model scope, each answer whose resolution the content stores, by run when the
    /// subject has several. Empty for none.
    /// </summary>
    private static string RcaResolutionsText(Context ctx, BenchmarkReportWriterItem item)
    {
        var numbers = new HashSet<int>(item.Questions ?? new List<int>());
        foreach (string id in (item.Evidence ?? new List<string>()).Where(id => !string.IsNullOrWhiteSpace(id)))
        {
            if (ComparisonQuestionNumber(ctx, id.Trim()) is int number) numbers.Add(number);
        }
        if (numbers.Count == 0) return string.Empty;

        var parts = new List<string>();
        if (ctx.Comparison)
        {
            var cited = ctx.Sheet.Questions.Where(q => numbers.Contains(q.Number)).OrderBy(q => q.Number).ToList();
            var letters = cited.SelectMany(q => q.Models ?? new List<BenchmarkReportQuestionModelScore>()).Select(c => c.Letter)
                .Concat(ctx.Content.Runs.Select(r => r.Letter).OfType<string>())
                .Where(l => !string.IsNullOrEmpty(l))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(l => l.Length)
                .ThenBy(l => l, StringComparer.Ordinal)
                .ToList();
            foreach (string letter in letters)
            {
                foreach (var question in cited)
                {
                    var cell = question.Models?.FirstOrDefault(c => string.Equals(c.Letter, letter, StringComparison.Ordinal));
                    IEnumerable<string?> names = cell?.CriticalErrorResolutions is { } stored
                        ? stored
                        : ctx.Content.Runs
                            .Where(r => string.Equals(r.Letter, letter, StringComparison.Ordinal))
                            .OrderBy(r => r.RunId)
                            .SelectMany(r => r.Questions.Where(a => a.Number == question.Number))
                            .Select(a => a.CriticalErrorResolution);
                    var texts = names.Select(CriticalErrorResolutionText).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
                    if (texts.Count == 0) continue;
                    parts.Add(NameOfLetter(ctx, letter) + " on " + ctx.QuestionLabel(question.Number) + ": " + BenchmarkReportFormat.LetterList(texts));
                }
            }
            return parts.Count == 0 ? string.Empty : string.Join("; ", parts) + ".";
        }

        foreach (var run in ctx.Content.Runs
                     .OrderBy(r => (r.Letter ?? string.Empty).Length)
                     .ThenBy(r => r.Letter ?? string.Empty, StringComparer.Ordinal)
                     .ThenBy(r => r.RunId))
        {
            foreach (var answer in run.Questions.Where(q => numbers.Contains(q.Number)).OrderBy(q => q.Number))
            {
                if (CriticalErrorResolutionText(answer.CriticalErrorResolution) is not string text) continue;
                string who = run.Letter != null ? NameOfLetter(ctx, run.Letter)
                    : ctx.Content.Runs.Count > 1 ? "run " + Inv(run.RunId)
                    : ctx.Sheet.SubjectLabel;
                parts.Add(who + " on " + ctx.QuestionLabel(answer.Number) + ": " + text);
            }
        }
        return parts.Count == 0 ? string.Empty : string.Join("; ", parts) + ".";
    }

    // ---------------------------------------------------------------------------------------------
    // 5. and 6. Evidence: speed and cost
    // ---------------------------------------------------------------------------------------------

    private static void RcaSpeed(StringBuilder sb, Context ctx, RootCauseResult? result)
    {
        Heading(sb, "## Evidence: speed");
        if (result == null)
        {
            Line(sb, RcaNoAnalysis);
            Line(sb);
            Figures(sb, ctx, BenchmarkReportChartAnchor.RcaSpeed);
        }
        else
        {
            Heading(sb, "### Where the time goes");
            string dropped = RcaSegmentTable(sb, ctx, result.SpeedRows, RootCauseSegments.Time, "Wall time per question (median, tools included)",
                v => BenchmarkReportFormat.OneDecimal(v) + " s");
            Line(sb, "*" + RcaTimeNote(ctx) + (dropped.Length > 0 ? " " + dropped : string.Empty) + "*");
            Line(sb);
            Figures(sb, ctx, BenchmarkReportChartAnchor.RcaSpeed);
            if (ctx.Comparison) RcaReferenceTests(sb, ctx, BenchmarkPairedTests.SpeedMeasure);
            RcaDecompositionSummary(sb, ctx, result.SpeedPairs, cost: false);
        }
        Slot(sb, ctx, BenchmarkReportSlots.EvidenceSpeed);
    }

    private static void RcaCost(StringBuilder sb, Context ctx, RootCauseResult? result)
    {
        Heading(sb, "## Evidence: cost");
        if (result == null)
        {
            Line(sb, RcaNoAnalysis);
            Line(sb);
            Figures(sb, ctx, BenchmarkReportChartAnchor.RcaCost);
        }
        else
        {
            Heading(sb, "### Where the cost goes");
            string dropped = RcaSegmentTable(sb, ctx, result.CostRows, RootCauseSegments.CostComponents, "Cost per question",
                BenchmarkReportFormat.Usd);
            Line(sb, "*" + RcaCostNote(ctx, result) + (dropped.Length > 0 ? " " + dropped : string.Empty)
                + (RcaOutputMerged(result.CostRows.Select(r => r.Segments)) ? " " + RcaThinkingIncludedNote : string.Empty) + "*");
            Line(sb);
            Figures(sb, ctx, BenchmarkReportChartAnchor.RcaCost);
            if (ctx.Comparison) RcaReferenceTests(sb, ctx, BenchmarkPairedTests.CostMeasure);
            RcaDecompositionSummary(sb, ctx, result.CostPairs, cost: true);
        }
        Slot(sb, ctx, BenchmarkReportSlots.EvidenceCost);
    }

    /// <summary>The note under the time table: how the seconds are split, each part named, the first two glossed at their first use.</summary>
    private static string RcaTimeNote(Context ctx)
    {
        UseTerms(ctx, RootCauseGlossary.MedianAndMean);
        return "Wall time per question: the median time per question, tool execution included, split into its parts in proportion to their mean times. "
            + "The parts are time to first token" + Gloss(ctx, RootCauseGlossary.TimeToFirstToken)
            + ", output generation" + Gloss(ctx, RootCauseGlossary.OutputGeneration)
            + ", tool execution, the model's own waits and retries, and other time. "
            + "It differs from the median model time the other tables of this document give, which leaves tool execution out.";
    }

    /// <summary>
    /// The note under the cost table: what each token component is, and the pricing basis, glossed at their first use;
    /// where a provider does not report reasoning separately, the output column holds its output with the thinking.
    /// </summary>
    private static string RcaCostNote(Context ctx, RootCauseResult result)
    {
        string basis = OneLine(string.IsNullOrWhiteSpace(result.PricingBasis) ? PricingBasisText(ctx.Sheet).Text : result.PricingBasis).TrimEnd('.');
        bool merged = RcaOutputMerged(result.CostRows.Select(r => r.Segments));
        return "Mean cost per question by component. Each component is a kind of token" + Gloss(ctx, RootCauseGlossary.Tokens) + ". "
            + "Uncached input is text read at the full price, and cache reads are stored input read at a lower price. "
            + "Cache writes store input for later calls; " + (merged ? "output" : "visible output") + " is the answer; reasoning is the model's hidden thinking. "
            + "Priced on this basis" + Gloss(ctx, RootCauseGlossary.PricingBasis) + ": " + basis + ".";
    }

    /// <summary>Whether some row holds <see cref="RootCauseSegments.OutputWithThinking"/>: a provider that does not report reasoning separately.</summary>
    private static bool RcaOutputMerged(IEnumerable<IEnumerable<RootCauseSegment>> rows)
        => rows.Any(r => r.Any(s => s.Key == RootCauseSegments.OutputWithThinking));

    /// <summary>
    /// A cost component's column header: <c>Output</c> for the visible output where <paramref name="merged"/>, since the
    /// column then also holds the output with thinking included; the component's label otherwise.
    /// </summary>
    private static string RcaComponentHeader(string key, bool merged)
        => merged && key == RootCauseSegments.Output ? "Output" : Capitalized(RootCauseSegments.Label(key));

    /// <summary>
    /// A row's figure in the column of <paramref name="key"/>: its segment formatted; in the output column, the output
    /// with thinking included followed by <c>*</c> for a row that holds that in place of the visible output;
    /// <see cref="NoValue"/> for none.
    /// </summary>
    private static string RcaSegmentCell(IEnumerable<RootCauseSegment> segments, string key, Func<double, string> format)
    {
        if (segments.FirstOrDefault(s => s.Key == key) is { } segment) return format(segment.Value);
        if (key == RootCauseSegments.Output && segments.FirstOrDefault(s => s.Key == RootCauseSegments.OutputWithThinking) is { } merged)
        {
            return format(merged.Value) + "*";
        }
        return NoValue;
    }

    /// <summary>
    /// One row per chart row of R2 or R3: each model, then for a subject on its own each difficulty band or
    /// suite, with its total and its segments; an unavailable row says why. A row's output with thinking included
    /// stands in the output column, marked <c>*</c> (<see cref="RcaSegmentCell"/>). A segment column that is zero or
    /// not recorded in every row is left out; returns the sentence that names the columns left out, or empty.
    /// </summary>
    private static string RcaSegmentTable(
        StringBuilder sb, Context ctx, IReadOnlyList<RootCauseChartRow> rows, IReadOnlyList<string> keys, string totalColumn, Func<double, string> format)
    {
        bool merged = RcaOutputMerged(rows.Select(r => r.Segments));
        var header = new List<string> { "Model", totalColumn };
        header.AddRange(keys.Select(k => RcaComponentHeader(k, merged)));
        var cells = new List<IReadOnlyList<string>>();
        foreach (var row in rows)
        {
            string name = row.Token != null ? RcaTokenName(ctx, row.Token) : row.Label;
            var line = new List<string> { Cell(name) };
            if (row.Total is not double total)
            {
                line.Add("not recorded (" + Cell(OneLine(row.UnavailableReason ?? "no data").TrimEnd('.')) + ")");
                line.AddRange(keys.Select(_ => NoValue));
            }
            else
            {
                line.Add(format(total));
                line.AddRange(keys.Select(k => RcaSegmentCell(row.Segments, k, format)));
            }
            cells.Add(line);
        }

        var (shown, shownRows, dropped) = DropConstantColumns(header, cells, keepFirst: 2);
        TableHeaderOf(sb, shown);
        foreach (var line in shownRows) Line(sb, RcaRow(line));
        Line(sb);
        return DroppedColumnsSentence(dropped);
    }

    /// <summary>
    /// The decomposed pairs, one line each with the leading component in seconds or dollars and its share; the
    /// pairs whose difference is not established in one line; each pair that could not be decomposed with its
    /// reason. Appendix B has the full decompositions.
    /// </summary>
    private static void RcaDecompositionSummary(StringBuilder sb, Context ctx, IReadOnlyList<RootCausePairDecomposition> pairs, bool cost)
    {
        if (pairs.Count == 0) return;

        Heading(sb, cost ? "### Cost differences between the models" : "### Time differences between the models");
        var decomposed = pairs.Where(p => p.UnavailableReason == null && p.Difference.HasValue && p.Components.Count > 0).ToList();
        bool overExplained = false;
        foreach (var pair in decomposed)
        {
            var lead = pair.Components.Where(c => c.Key != "all" && c.Share.HasValue).OrderByDescending(c => c.Share!.Value).FirstOrDefault();
            string difference = RcaAmount(pair.Difference!.Value, cost) + " per question";
            string over = string.Empty;
            if (lead?.Share is double share && Math.Round(share * 100, MidpointRounding.AwayFromZero) > 100 && !overExplained)
            {
                over = " (above 100%: another component pulls the other way)";
                overExplained = true;
            }
            string leading = lead == null
                ? string.Empty
                : "; leading component " + LowerFirst(lead.Label) + ", " + RcaAmount(lead.Amount, cost) + ", " + RootCauseHypotheses.SharePct(lead) + " of the difference" + over;
            Line(sb, "- **" + RcaPairName(ctx, pair.First, pair.Second) + ":** " + difference + leading + ".");
        }
        if (decomposed.Count > 0) Line(sb);

        var notEstablished = pairs.Where(p => p.NotEstablished).ToList();
        if (notEstablished.Count > 0)
        {
            Line(sb, "Not split into parts, because the difference itself may be chance: "
                + BenchmarkReportFormat.LetterList(notEstablished.Select(p => RcaPairName(ctx, p.First, p.Second)).ToList()) + ".");
            Line(sb);
        }

        var unavailable = pairs.Where(p => !p.NotEstablished && (p.UnavailableReason != null || !p.Difference.HasValue)).ToList();
        if (unavailable.Count > 0)
        {
            Line(sb, "Not decomposed for want of figures: " + string.Join("; ", unavailable.Select(p =>
                RcaPairName(ctx, p.First, p.Second) + " (" + SheetText(ctx, p.UnavailableReason ?? "not computed").TrimEnd('.') + ")")) + ".");
            Line(sb);
        }

        if (decomposed.Count > 0)
        {
            ExplainedTerms(ctx, RootCauseGlossary.DecompositionShare, RootCauseGlossary.BootstrapInterval);
            Line(sb, "*" + RcaDecompositionNote + " The difference is the first model's minus the second's. A share can pass 100% when another "
                + "component pulls the other way. Appendix B gives every component.*");
            Line(sb);
        }
    }

    private static string RcaAmount(double value, bool cost)
        => cost
            ? (value > 0 ? "+" : value < 0 ? "-" : string.Empty) + BenchmarkReportFormat.Usd(Math.Abs(value))
            : BenchmarkReportFormat.SignedOneDecimal(value / 1000.0) + " s";

    // ---------------------------------------------------------------------------------------------
    // 7. Hypotheses
    // ---------------------------------------------------------------------------------------------

    private static void RcaHypotheses(StringBuilder sb, Context ctx, RootCauseResult? result)
    {
        Heading(sb, "## Hypotheses");

        var slots = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [RootCauseAxes.Intelligence] = BenchmarkReportSlots.HypothesesIntelligence,
            [RootCauseAxes.Speed] = BenchmarkReportSlots.HypothesesSpeed,
            [RootCauseAxes.Cost] = BenchmarkReportSlots.HypothesesCost
        };
        bool legend = false;
        foreach (string axis in RootCauseAxes.All)
        {
            Heading(sb, "### " + RootCauseAxes.Name(axis));
            var hypotheses = (result?.Hypotheses ?? new List<RootCauseHypothesis>()).Where(h => h.Axis == axis).ToList();
            var fired = hypotheses.Where(h => h.Kind != "null").ToList();
            if (result == null)
            {
                Line(sb, RcaNoAnalysis);
                Line(sb);
            }
            else if (fired.Count == 0)
            {
                Line(sb, "No hypothesis other than a null fired on this axis.");
                Line(sb);
            }
            else
            {
                Line(sb, "| Hypothesis | Statement | Grade | Share | Settled by |");
                Line(sb, "|---|---|---|---|---|");
                UseTerms(ctx, RootCauseGlossary.Hypothesis, RootCauseGlossary.GradeWords, RootCauseGlossary.Interval95);
                if (fired.Any(h => h.Share.HasValue)) UseTerms(ctx, RootCauseGlossary.DecompositionShare);
                foreach (var h in fired)
                {
                    Line(sb, RcaRow(Cell(h.Key), Cell(SheetText(ctx, h.Statement)), RootCauseGrades.HypothesisWordWithGrade(h), RcaShareText(h), RcaSettledBy(h)));
                }
                Line(sb);
                Line(sb, "*" + RcaShareNote + "*");
                Line(sb);
                if (!legend)
                {
                    Line(sb, "*" + RcaIdLegend(ctx) + "*");
                    Line(sb);
                    legend = true;
                }
            }

            foreach (string line in RcaNullsLines(ctx, hypotheses.Where(h => h.Kind == "null").ToList()))
            {
                Line(sb, line);
            }
            Slot(sb, ctx, slots[axis]);
        }

        Figures(sb, ctx, BenchmarkReportChartAnchor.RcaHypotheses);

        Heading(sb, "### The writer's hypotheses");
        var own = ctx.Writer.WriterHypotheses ?? new List<BenchmarkReportWriterHypothesis>();
        string locus = own.Count == 0 ? string.Empty : " Each names its locus" + Gloss(ctx, RootCauseGlossary.Locus) + ".";
        var lines = new List<string> { "**Not tested by code.** " + RcaWriterHypothesesNote + locus, string.Empty };
        if (own.Count == 0)
        {
            lines.Add("The writer added none.");
        }
        foreach (var h in own)
        {
            string models = h.Models.Count == 0 ? string.Empty : BenchmarkReportFormat.LetterList(h.Models.Select(t => RcaTokenName(ctx, t)).ToList()) + "; ";
            lines.Add("- **" + RootCauseAxes.Name(h.Axis) + " · " + RootCauseGrades.Low + "** (" + models + "locus " + OneLine(h.Locus) + "): " + OneLine(Prose(ctx, h.Statement)));
            lines.Add("  - *For:* " + string.Join(" ", h.For.Select(t => OneLine(Prose(ctx, t)))));
            lines.Add("  - *Against:* " + (h.Against.Count == 0 ? "None given." : string.Join(" ", h.Against.Select(t => OneLine(Prose(ctx, t))))));
            lines.Add("  - *Refuted if:* " + OneLine(Prose(ctx, h.RefutedIf)));
        }
        Callout(sb, "WARNING", lines);
    }

    /// <summary>
    /// <c>HQ2.1: critical errors of Model F against Model E</c>, as the Conclusions and Appendix C name a hypothesis; the
    /// key alone on a result stored without labels. The body's hypothesis tables give the key alone, beside the statement.
    /// </summary>
    private static string RcaHypothesisName(Context ctx, RootCauseHypothesis h)
        => string.IsNullOrWhiteSpace(h.Label) ? h.Key : h.Key + ": " + SheetText(ctx, h.Label);

    private static string RcaShareText(RootCauseHypothesis h)
        => h.Share is double share
            ? RootCauseHypotheses.Pct(share) + (h.ShareLower is double lo && h.ShareUpper is double hi ? " (" + RootCauseHypotheses.Interval(lo, hi) + ")" : string.Empty)
            : NoValue;

    private static string RcaSettledBy(RootCauseHypothesis h)
        => h.NextRunNumber is int run ? "Run " + Inv(run) : h.CheckNumber is int check ? "Check " + Inv(check) : NoValue;

    /// <summary>
    /// How to read a hypothesis id, with the null hypothesis glossed at its first use.
    /// </summary>
    private static string RcaIdLegend(Context ctx)
        => "How to read a hypothesis ID such as HQ1.2: H compares two models, W looks inside one model, and H0 is a null hypothesis"
           + Gloss(ctx, RootCauseGlossary.NullHypothesis) + ". Q, S and C stand for intelligence, speed and cost. "
           + "Appendix C gives each hypothesis's evidence for and against.";

    /// <summary>
    /// The null hypotheses of an axis as counts with their pairs: those where no real difference could be shown,
    /// then those where a test found one. Up to <see cref="RcaInlinePairsMax"/> pairs stand in the sentence, more
    /// in a list under it. Each block ends in an empty line; nothing when the axis has no null hypothesis.
    /// </summary>
    private static List<string> RcaNullsLines(Context ctx, IReadOnlyList<RootCauseHypothesis> nulls)
    {
        var lines = new List<string>();
        if (nulls.Count == 0) return lines;
        UseTerms(ctx, RootCauseGlossary.NullHypothesis);

        string Subject(RootCauseHypothesis h) => h.Id == "H0-W"
            ? "within " + RcaTokenName(ctx, h.Models.FirstOrDefault() ?? string.Empty) + "'s own questions"
            : h.Models.Count >= 2
                ? RcaTokenName(ctx, h.Models[0]) + " vs. " + RcaTokenName(ctx, h.Models[1])
                : RcaTokenName(ctx, h.Models.FirstOrDefault() ?? string.Empty);

        void Block(IReadOnlyList<RootCauseHypothesis> group, string lead)
        {
            if (group.Count == 0) return;
            bool pairs = group.All(h => h.Id != "H0-W" && h.Models.Count >= 2);
            string count = Inv(group.Count) + " " + (pairs ? (group.Count == 1 ? "pair" : "pairs") : (group.Count == 1 ? "comparison" : "comparisons"));
            var subjects = group.Select(Subject).ToList();
            if (subjects.Count <= RcaInlinePairsMax)
            {
                lines.Add(lead + count + ": " + string.Join("; ", subjects) + ".");
            }
            else
            {
                lines.Add(lead + count + ":");
                lines.Add(string.Empty);
                lines.AddRange(subjects.Select(s => "- " + BenchmarkReportSentenceCase.UpperFirst(s)));
            }
            lines.Add(string.Empty);
        }

        Block(nulls.Where(h => h.Grade != RootCauseGrades.NotSupported).ToList(), "No real difference could be shown (the gap may be chance) for ");
        Block(nulls.Where(h => h.Grade == RootCauseGrades.NotSupported).ToList(), "A real difference was found for ");
        return lines;
    }

    // ---------------------------------------------------------------------------------------------
    // 8. Conclusions
    // ---------------------------------------------------------------------------------------------

    private static void RcaConclusions(StringBuilder sb, Context ctx, RootCauseResult? result)
    {
        Heading(sb, "## Conclusions");
        if (result == null || result.Axes.Count == 0)
        {
            Line(sb, RcaNoAnalysis);
            Line(sb);
        }
        else
        {
            Line(sb, "| Axis | Status | Best hypothesis | Why |");
            Line(sb, "|---|---|---|---|");
            foreach (var axis in result.Axes)
            {
                UseReasonTerms(ctx, axis.Reason);
                var best = axis.BestHypothesis == null ? null : result.Hypotheses.FirstOrDefault(h => h.Key == axis.BestHypothesis);
                string name = best != null ? RcaHypothesisName(ctx, best) : axis.BestHypothesis ?? NoValue;
                string why = string.IsNullOrWhiteSpace(axis.PriceNote) ? axis.Reason : WithPeriod(axis.Reason) + " " + axis.PriceNote;
                Line(sb, RcaRow(RootCauseAxes.Name(axis.Axis), RootCauseStatuses.Display(axis.Status), Cell(name), Cell(SheetText(ctx, why))));
            }
            Line(sb);
        }

        string conclusion = ctx.Writer.Sections.TryGetValue(BenchmarkReportSlots.Conclusions, out var text) && !string.IsNullOrWhiteSpace(text)
            ? Prose(ctx, text)
            : "*Not written.*";
        Callout(sb, "TIP", new[] { "**Conclusion.** " + conclusion });
    }

    // ---------------------------------------------------------------------------------------------
    // 9. What to run next
    // ---------------------------------------------------------------------------------------------

    private static void RcaNextRuns(StringBuilder sb, Context ctx, RootCauseResult? result)
    {
        Heading(sb, "## What to run next");
        var runs = (result?.RecommendedRuns ?? new List<RootCauseRecommendedRun>()).OrderBy(r => r.Number).ToList();
        if (runs.Count == 0)
        {
            Line(sb, "Code proposes no run.");
            Line(sb);
        }
        else
        {
            var analysis = result!;
            Line(sb, "In the order to make them: the Overseer chat first (C), then the benchmark (B), then model selection (M). "
                + "Appendix D gives each run's launcher settings.");
            Line(sb);
            if (analysis.SystemFirst && runs.Any(r => r.AfterSystemHypotheses))
            {
                Line(sb, "A harness or suite hypothesis is graded Moderate or higher, so the model-selection runs wait until the system hypotheses are settled.");
                Line(sb);
            }

            Line(sb, "| # | Run | Purpose | Models | Settles | Estimate |");
            Line(sb, "|---|---|---|---|---|---|");
            foreach (var run in runs)
            {
                var models = run.ModelNames.Select(name => RcaRunModelName(ctx, name)).Distinct(StringComparer.Ordinal).ToList();
                string modelText = models.Count == 0 ? "no model resolved"
                    : models.Count >= 3 && models.Count == analysis.Models.Count ? "all " + Inv(models.Count) + " models"
                    : BenchmarkReportFormat.LetterList(models);
                string estimate = RootCauseNextRuns.EstimateText(run);
                Line(sb, RcaRow(
                    Inv(run.Number),
                    Capitalized(RootCauseNextRuns.KindPhrase(run.Kind)) + " (" + run.Mark.ToLowerInvariant() + ")",
                    RootCausePurposes.Text(run.Purpose),
                    Cell(modelText),
                    Cell(RcaSettlesText(run.HypothesisIds)),
                    estimate.Length == 0 ? "not estimated" : estimate));
            }
            Line(sb);

            foreach (var run in runs.Where(r => r.When != null || r.AfterSystemHypotheses))
            {
                Line(sb, "- **Run " + Inv(run.Number) + ", when:** " + RcaWhenText(run) + ".");
            }
            if (runs.Any(r => r.When != null || r.AfterSystemHypotheses)) Line(sb);
        }

        Heading(sb, "### Checks");
        var checks = result?.Checks ?? new List<RootCauseCheck>();
        if (checks.Count == 0)
        {
            Line(sb, "No check is proposed.");
        }
        foreach (var check in checks.OrderBy(c => c.Number))
        {
            Line(sb, Inv(check.Number) + ". " + SheetText(ctx, check.Text)
                + (check.HypothesisIds.Count > 0 ? " *(" + OneLine(check.Kind) + "; settles " + string.Join(", ", check.HypothesisIds) + ")*" : " *(" + OneLine(check.Kind) + ")*"));
        }
        Line(sb);

        Slot(sb, ctx, BenchmarkReportSlots.NextRuns);
    }

    /// <summary>
    /// When to make a run, after its label: <c>Start each run at a different time of day; after the system hypotheses are
    /// settled</c>, its first word capitalized; empty for any time.
    /// </summary>
    private static string RcaWhenText(RootCauseRecommendedRun run)
    {
        var when = new List<string>();
        if (run.When != null) when.Add(run.When);
        if (run.AfterSystemHypotheses) when.Add("after the system hypotheses are settled");
        return BenchmarkReportSentenceCase.UpperFirst(string.Join("; ", when));
    }

    /// <summary>The most hypothesis ids a run's Settles cell lists before it refers to Appendix D.</summary>
    private const int RcaSettlesMaxIds = 8;

    /// <summary>
    /// A run's Settles cell: its hypothesis ids, comma-separated; past <see cref="RcaSettlesMaxIds"/>, the
    /// first of them and how many more Appendix D lists.
    /// </summary>
    private static string RcaSettlesText(IReadOnlyList<string> ids)
    {
        if (ids.Count == 0) return NoValue;
        if (ids.Count <= RcaSettlesMaxIds) return string.Join(", ", ids);
        return string.Join(", ", ids.Take(RcaSettlesMaxIds)) + " and " + Inv(ids.Count - RcaSettlesMaxIds) + " more (Appendix D)";
    }

    /// <summary>
    /// Every launcher field of a recommended run, labeled as Run Benchmark labels it, in the launcher's order;
    /// a setting the analyzed runs did not share says so. A grader and the scoring profile are named with their id,
    /// <c>Claude 5 Opus @ medium (configuration 19)</c>; by the id alone on a result stored without the names, and
    /// for a grader whose name an anonymized copy withholds (<see cref="RcaGraderNameShown"/>).
    /// </summary>
    private static List<(string Field, string Setting)> RcaLauncherSettings(Context ctx, RootCauseRecommendedRun run)
    {
        const string NotShared = "not shared by the analyzed runs: choose it";
        bool batch = string.Equals(run.RunMode, "Batch", StringComparison.Ordinal);
        bool battery = string.Equals(run.TargetKind, "Battery", StringComparison.Ordinal);
        bool panel = run.CoAssessorModelConfigurationId.HasValue;
        string Config(long? id, string none, string? name = null)
            => id is not long value ? none
                : RcaGraderNameShown(ctx, name) ? OneLine(name) + " (configuration " + Inv(value) + ")"
                : "configuration " + Inv(value);
        string profile = run.ScoringProfileId is long profileId
            ? (string.IsNullOrWhiteSpace(run.ScoringProfileName) ? "profile " + Inv(profileId) : OneLine(run.ScoringProfileName) + " (profile " + Inv(profileId) + ")")
            : NotShared;

        var models = run.ModelNames.Select(name => RcaRunModelName(ctx, name)).ToList();
        var settings = new List<(string, string)>
        {
            ("Models", batch ? "Model batch" : "One model"),
            (batch ? "Models Under Test" : "Model Under Test", models.Count == 0 ? "no model resolved" : string.Join(", ", models)),
            ("Run Target", battery ? "Battery" : "Single suite"),
            battery
                ? ("Battery", run.BatteryName ?? (run.BatteryId is long batteryId ? "battery " + Inv(batteryId) : NotShared))
                : ("Benchmark Suite", run.SuiteName ?? (run.SuiteId is long suiteId ? "suite " + Inv(suiteId) : NotShared)),
            ("Scoring Profile", profile),
            ("Response Style", run.VerboseMode switch { true => "Detailed — diagnostic", false => "Concise — production default", _ => NotShared }),
            ("Source Code References", run.AllowSourceCodeReferences switch
            {
                true => "Allowed — answers cite source files and lines",
                false => "Disallowed — production default",
                _ => NotShared
            }),
            ("Assessor", Config(run.AssessorModelConfigurationId, NotShared, run.AssessorName)),
            ("Co-Assessor", Config(run.CoAssessorModelConfigurationId, "none", run.CoAssessorName)),
            (panel ? "Reference Reader" : "Second Reader", Config(run.SecondOpinionAssessorModelConfigurationId, "none", run.ReferenceReaderName))
        };
        if (!panel)
        {
            settings.Add(("Coverage", RcaCoverageText(run.SecondOpinionMode)));
        }
        settings.Add(("Claim Verifier", Config(run.ClaimVerifierModelConfigurationId, "none", run.ClaimVerifierName)));
        settings.Add(("Report Writer", Config(run.ReportWriterModelConfigurationId, "none")));
        settings.Add(batch
            ? (battery ? "Runs per Suite" : "Runs per model", Inv(run.RunsPerModel))
            : (battery ? "Runs per Suite" : "Number of Runs", Inv(run.RunCount)));
        if (batch)
        {
            settings.Add(("Model order", string.Equals(run.Order, "AsListed", StringComparison.Ordinal) ? "As listed" : "Randomized (recommended)"));
        }
        return settings;
    }

    /// <summary>
    /// Whether a recommended run's grader name may be printed: a name was stored, and in an anonymized copy it is the
    /// label of a grader on the sheet whose identity the copy does not withhold (<see cref="GraderWithheld"/>); any
    /// other name could name a compared model's provider, so an anonymized copy prints its id alone.
    /// </summary>
    private static bool RcaGraderNameShown(Context ctx, string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        if (!ctx.Anonymized) return true;
        var graders = ctx.Sheet.Graders.Where(g => string.Equals(g.Label?.Trim(), name.Trim(), StringComparison.Ordinal)).ToList();
        return graders.Count > 0 && graders.All(g => !GraderWithheld(ctx, g));
    }

    /// <summary>The launcher's Coverage choice for a second-opinion mode.</summary>
    private static string RcaCoverageText(int? mode) => mode switch
    {
        0 => "Never",
        1 => "Only flagged answers",
        2 => "Flagged answers and statistical outliers",
        3 => "Every answer (double grading)",
        4 => "Flagged answers plus a sample",
        null => "not shared by the analyzed runs: choose it",
        _ => "mode " + Inv(mode.GetValueOrDefault())
    };

    /// <summary>
    /// A recommended run's model as this copy names it: an analyzed model as the document names it; another
    /// configured model by its label, except in an anonymized comparison-scope copy.
    /// </summary>
    private static string RcaRunModelName(Context ctx, string label)
    {
        var analyzed = ctx.Sheet.RootCause?.Models.FirstOrDefault(m => string.Equals(m.Label, label, StringComparison.Ordinal));
        if (analyzed != null) return RcaTokenName(ctx, analyzed.Token);
        return ctx.Comparison && ctx.Anonymized ? "another configured model" : label;
    }

    private static string RcaCheckText(string status) => status switch
    {
        RootCauseCheckStatuses.Passed => "passed",
        RootCauseCheckStatuses.Failed => "failed",
        _ => "could not be assessed"
    };

    // ---------------------------------------------------------------------------------------------
    // 10. Limitations and threats to validity
    // ---------------------------------------------------------------------------------------------

    /// <summary>What the design cannot decide and what could bias its figures, each said once, then the writer's slot.</summary>
    private static void RcaLimitations(StringBuilder sb, Context ctx, RootCauseResult? result)
    {
        Heading(sb, "## Limitations and threats to validity");
        UseTerms(ctx, RootCauseGlossary.Hypothesis, RootCauseGlossary.PointsAtRisk);
        if (ctx.Comparison) UseTerms(ctx, RootCauseGlossary.PairedTest, RootCauseGlossary.Holm);
        Line(sb,"- The benchmark asks single-turn questions under one chat configuration: live chat carries the conversation before each question, "
            + "and spoiler-free mode, web search and subagents are off, so a cause found here holds for that configuration only.");
        Line(sb, "- It runs without the wiki text that live chat always gives the model before it answers, so a reading about retrieval or tool routing was measured under a condition live chat does not share.");
        Line(sb, "- The hypotheses come from a fixed rule table over these runs; points at risk are upper bounds, and a decomposition rests on the questions both sides have figures for. The writer's own hypotheses are qualitative.");
        if (ctx.Comparison)
        {
            Line(sb, "- Intervals: " + WithPeriod(SharedOrPerModel(ctx, "quality.intervalBasis")));
            Line(sb, "- The graders are AI models. Each grader's provider relation to the models is stated under Setup and method; "
                + "a grader from a model's own provider may read that model more favorably.");
            Line(sb, "- Paired tests: each measure's family is Holm-adjusted across the tests it makes over these models only.");
            foreach (var peer in OrderedPeers(ctx.Sheet).Where(p => string.Equals(p.State, "Degraded", StringComparison.Ordinal)))
            {
                Line(sb, "- Comparability: " + ProseName(ctx, peer) + ": " + SheetText(ctx, peer.Explanation));
            }
            if (IsBatteryComparison(ctx))
            {
                Line(sb, "- Composite: each Overall Index weights the suites under the battery's scheme; another scheme gives another figure.");
                Line(sb, "- Detail: the writer was given the full text of " + DetailQuestionsText(ctx) + ", and the other questions as matrix rows.");
            }
        }
        else
        {
            Line(sb, "- Interval: " + WithPeriod(D(ctx, "quality.intervalBasis")));
            Line(sb, "- The graders are AI models. Each grader's provider relation to the model under test is stated under "
                + "Setup and method; a grader from the model's own provider may read it more favorably.");
            if (string.Equals(ctx.Sheet.SubjectState, "Degraded", StringComparison.Ordinal))
            {
                Line(sb, "- Comparability: " + ctx.Sheet.SubjectExplanation);
            }
            if (ctx.Battery)
            {
                Line(sb, "- Composite: the Overall Index weights the suites under the battery's scheme; another scheme or "
                    + "another set of suites gives another figure, and it is not comparable with a single suite's index.");
                Line(sb, "- Detail: the writer was given the full text of at most a few questions per suite, each with one answer, "
                    + "and the other questions as one-line rows.");
                foreach (var fact in BatteryCaveats(ctx))
                {
                    Line(sb, "- Battery analysis: " + OneLine(fact.Display));
                }
            }
        }
        if (result != null && RootCauseClaimSupport.IsCompletion(ctx.Sheet))
        {
            Line(sb, result.Controls.Count == 0
                ? "- No other model answered these questions, so the data cannot tell the model from the instrument on intelligence."
                : result.ControlLimit != null
                    ? "- The controls cap the model-versus-instrument intelligence hypotheses at " + RootCauseGrades.Display(result.ControlLimit) + "."
                    : "- The controls are earlier runs, made at other times; they inform intelligence only.");
        }
        foreach (string note in result?.Notes ?? new List<string>())
        {
            if (!string.IsNullOrWhiteSpace(note)) Line(sb, "- " + SheetText(ctx, note));
        }

        var availability = ctx.Comparison
            ? OrderedPeers(ctx.Sheet).Select(p => (ProseName(ctx, p), BenchmarkComparisonReportFacts.ModelPrefix(p.Letter))).ToList()
            : new List<(string, string)> { (ctx.Sheet.SubjectLabel, string.Empty) };
        foreach (string threat in GroupedAvailabilityThreats(ctx, availability))
        {
            Line(sb, "- " + threat);
        }
        if ((ctx.Comparison ? ComparisonWriterCaveat(ctx) : WriterIndependenceCaveat(ctx)) is string caveat)
        {
            Line(sb, "- " + caveat);
        }
        Line(sb);
        Slot(sb, ctx, BenchmarkReportSlots.Limitations);
    }

    // ---------------------------------------------------------------------------------------------
    // Appendices
    // ---------------------------------------------------------------------------------------------

    /// <summary>The appendices, one bullet each under its letter; Known facts only where the document has it (<paramref name="letters"/>).</summary>
    private static void RcaAppendixList(StringBuilder sb, RcaAppendixLetters letters)
    {
        Heading(sb, "## Appendices");
        Line(sb, "- **A. Intelligence detail:** the compared models, configuration differences, failure profiles, grader findings, panel agreement, points at risk, the index difference by dimension, the paired tests the body does not give and the per-question matrix.");
        Line(sb, "- **B. Speed and cost decompositions:** calls, latency, tokens and every component of each decomposed difference.");
        Line(sb, "- **C. Hypothesis details:** each hypothesis's evidence for and against, grade reasons and robustness checks, and the null hypotheses.");
        Line(sb, "- **D. Run settings:** every launcher setting of each run to make next.");
        if (letters.KnownFacts)
        {
            Line(sb, "- **E. Known facts:** the counts and the configuration facts the analysis states; its tests and decompositions are in the body and in Appendices A and B.");
        }
        Line(sb, "- **" + letters.Reproducibility + ". Reproducibility:** run ids, dates, hashes and the pricing basis.");
        Line(sb, "- **" + letters.QuestionDetails + ". Question details:** the questions the writer cites, those with a critical error, confirmed or overturned, and the control items, each with its answer excerpts.");
        Line(sb, "- **" + letters.Glossary + ". Glossary:** every term this document uses, in plain words.");
        Line(sb);
    }

    /// <summary>
    /// Appendix A: every intelligence table the body leaves out. In comparison scope the results are in the body's At a
    /// glance and scoreboard, and the paired tests here are only the families the body does not print.
    /// </summary>
    private static void RcaIntelligenceDetail(StringBuilder sb, Context ctx, RootCauseResult? result)
    {
        Heading(sb, "## Appendix A: Intelligence detail");
        if (ctx.Comparison)
        {
            ComparisonModelsTable(sb, ctx, "### Compared models");
            RcaConfiguration(sb, ctx, result);
            if (HasProfiles(ctx))
            {
                Heading(sb, "### Failure profile");
                FailureProfileTable(sb, ctx);
            }
            Heading(sb, "### Grader findings by category");
            FindingsTable(sb, ctx);
            Heading(sb, "### Panel agreement");
            GraderTable(sb, ctx);
        }
        else
        {
            if (!ctx.Standalone) ComparedModels(sb, ctx);
            RcaConfiguration(sb, ctx, result);
            KeyFigures(sb, ctx, "### Key figures");
            WithheldRunsLine(sb, ctx);
            if (ctx.Battery)
            {
                BatteryProfile(sb, ctx, "### Battery profile");
            }
            if (ctx.Standalone) StandaloneResults(sb, ctx, "### Results");
            else ResultsAgainstPeers(sb, ctx, "### Results against peers");
            RcaSubjectProfile(sb, ctx);
            GraderReliability(sb, ctx, "### Grader findings and panel agreement");
            ToolUse(sb, ctx, "### Tool use");
        }

        RcaAtRisk(sb, ctx, result);
        RcaQualitySplits(sb, ctx, result);
        if (ctx.Comparison)
        {
            var body = RcaBodyFamily(ctx);
            PairedTestsSection(sb, ctx, "### Paired tests", f => f.Mode == "AllPairs" && !ReferenceEquals(f, body),
                "Every paired test of this comparison is under the evidence on intelligence, speed and cost.");
            MatrixTable(sb, ctx, "### Per-question matrix");
        }
        else
        {
            PerQuestion(sb, ctx, "### Per-question results", withDetails: false);
            if (RootCauseClaimSupport.IsCompletion(ctx.Sheet) && (result?.Controls.Count ?? 0) > 0)
            {
                RcaControlItems(sb, ctx, result!);
            }
        }
    }

    /// <summary>The configuration keys that differ between the analyzed models, from the facts and the hypotheses' confounds; nothing without peers.</summary>
    private static void RcaConfiguration(StringBuilder sb, Context ctx, RootCauseResult? result)
    {
        if (ctx.Standalone) return;

        var facts = ctx.Sheet.Facts
            .Where(f => f.Available && f.Key.EndsWith(".configDifferences", StringComparison.Ordinal))
            .OrderBy(f => f.Key, StringComparer.Ordinal)
            .ToList();
        var confounds = (result?.Hypotheses ?? new List<RootCauseHypothesis>())
            .Where(h => h.Confounds.Count > 0)
            .ToList();

        Heading(sb, "### Configuration differences");
        if (facts.Count == 0 && confounds.Count == 0)
        {
            Line(sb, "The analyzed models share every configuration key other than the model.");
            Line(sb);
            return;
        }
        foreach (var fact in facts)
        {
            Line(sb, "- **" + (ctx.Comparison ? ComparisonFactLabel(ctx, fact.Key) : FactLabel(ctx, fact.Key)) + ":** "
                + BenchmarkReportSentenceCase.UpperFirst(Shown(ctx, fact)));
        }
        if (confounds.Count > 0) UseTerms(ctx, RootCauseGlossary.Confound);
        foreach (var hypothesis in confounds)
        {
            Line(sb, "- **Open confounds of " + hypothesis.Key + "** (" + SheetText(ctx, hypothesis.ModelsText) + "): " + OneLine(string.Join(", ", hypothesis.Confounds)));
        }
        Line(sb);
    }

    /// <summary>A per-model sheet's own failure profile, one measure per row; nothing on a sheet without the <c>profile.*</c> facts.</summary>
    private static void RcaSubjectProfile(StringBuilder sb, Context ctx)
    {
        if (Fact(ctx, BenchmarkComparisonModelProfile.KeyPrefix + "answers") == null) return;

        var profile = new ProfileCells(ctx, null);
        string mislead = profile.Value("mayMislead") is double count
            ? Inv((int)count) + (profile.Value("mayMisleadPer20") is double per ? " (" + BenchmarkReportFormat.CompactDecimal(per) + " per 20)" : string.Empty)
            : NoValue;
        string wrong = profile.Value("outcome.wrongInsteadOfAbstaining") is double rate ? BenchmarkReportFormat.Percent(rate * 100) : NoValue;
        string length = profile.Value("answerChars.median") is double chars ? BenchmarkReportFormat.Count(chars) : NoValue;

        Heading(sb, "### Failure profile");
        Line(sb, "| Measure | " + Cell(ctx.Sheet.SubjectLabel) + " |");
        Line(sb, "|---|---|");
        foreach (var (name, value) in new[]
        {
            ("Answers", profile.Count("answers")),
            ("May mislead", mislead),
            ("Correct / partial / incorrect / not attempted", profile.Joined("outcome.correct", "outcome.partial", "outcome.incorrect", "outcome.notAttempted")),
            ("Wrong instead of abstaining", wrong),
            ("Level 2 or below: accuracy / completeness / conciseness / readability", profile.Joined("deficit.accuracy", "deficit.completeness", "deficit.conciseness", "deficit.readability")),
            ("Truncated", profile.Count("truncated")),
            ("Output defects", profile.Count("outputDefects")),
            ("No tool call", profile.Count("noToolAnswers")),
            ("Tool-limit hits", profile.Count("toolLimitHits")),
            ("Median answer length", length)
        })
        {
            Line(sb, "| " + name + " | " + value + " |");
        }
        Line(sb);
        Line(sb, "*" + FailureProfileOutcomesNote + " Output defects: an empty, truncated or scrubbed answer, reasoning bleed, repeated fragments, "
            + "or an answer that did not complete normally. Tool-limit hits: the tool budget or the iteration limit was reached. The counts say "
            + "what the graders recorded, not why.*");
        Line(sb);
    }

    /// <summary>Each model's index points at risk by signal, then, for a subject on its own, each difficulty band or suite.</summary>
    private static void RcaAtRisk(StringBuilder sb, Context ctx, RootCauseResult? result)
    {
        if (result == null) return;

        Heading(sb, "### Points at risk");
        UseTerms(ctx, RootCauseGlossary.PointsAtRisk, RootCauseGlossary.NoToolAnswers, RootCauseGlossary.CriticalError);
        var keys = RootCauseSegments.AtRisk;
        Line(sb, "| Model | " + string.Join(" | ", keys.Select(k => Capitalized(RootCauseSegments.Label(k)))) + " | Total | Median answer |");
        Line(sb, "|---|" + string.Concat(keys.Select(_ => "---|")) + "---|---|");
        foreach (var summary in result.Evidence)
        {
            var cells = new List<string> { Cell(RcaTokenName(ctx, summary.Token)) };
            if (summary.ScoredAnswers == 0 || summary.MedianQuality == null)
            {
                cells.AddRange(keys.Select(_ => NoValue));
                cells.Add("not recorded (no scored answer)");
                cells.Add(NoValue);
            }
            else
            {
                cells.AddRange(keys.Select(k => summary.AtRisk.FirstOrDefault(s => s.Key == k) is { } s ? BenchmarkReportFormat.OneDecimal(s.Value) : NoValue));
                cells.Add(BenchmarkReportFormat.OneDecimal(summary.AtRisk.Sum(s => s.Value)));
                cells.Add(BenchmarkReportFormat.Whole(summary.MedianQuality.Value));
            }
            Line(sb, RcaRow(cells));
        }
        foreach (var row in result.PointsAtRisk.Where(r => r.Kind != "model"))
        {
            var cells = new List<string> { Cell(row.Label) };
            if (row.Total == null)
            {
                cells.AddRange(keys.Select(_ => NoValue));
                cells.Add("not recorded (" + OneLine(row.UnavailableReason ?? "no data") + ")");
            }
            else
            {
                cells.AddRange(keys.Select(k => row.Segments.FirstOrDefault(s => s.Key == k) is { } s ? BenchmarkReportFormat.OneDecimal(s.Value) : NoValue));
                cells.Add(BenchmarkReportFormat.OneDecimal(row.Total.Value));
            }
            cells.Add(NoValue);
            Line(sb, RcaRow(cells));
        }
        Line(sb);
        Line(sb, "*" + RcaAtRiskNote + "*");
        Line(sb);
    }

    /// <summary>Each analyzed pair's Intelligence Index difference split by the scoring weights; nothing without a pair.</summary>
    private static void RcaQualitySplits(StringBuilder sb, Context ctx, RootCauseResult? result)
    {
        if (result == null || result.QualitySplits.Count == 0) return;

        var columns = result.QualitySplits
            .SelectMany(s => s.Components)
            .Select(c => (c.Key, c.Label))
            .DistinctBy(c => c.Key, StringComparer.Ordinal)
            .ToList();

        UseTerms(ctx, RootCauseGlossary.IntelligenceIndex, RootCauseGlossary.Dimension);
        Heading(sb, "### Intelligence Index difference by dimension");
        Line(sb, "| Pair | Difference | " + string.Join(" | ", columns.Select(c => Cell(Capitalized(c.Label)))) + " |");
        Line(sb, "|---|---|" + string.Concat(columns.Select(_ => "---|")));
        foreach (var split in result.QualitySplits)
        {
            var cells = new List<string>
            {
                Cell(RcaPairName(ctx, split.First, split.Second)),
                BenchmarkReportFormat.SignedOneDecimal(split.IndexDifference)
            };
            cells.AddRange(columns.Select(c => split.Components.FirstOrDefault(x => x.Key == c.Key) is { } component
                ? BenchmarkReportFormat.SignedOneDecimal(component.Amount)
                : NoValue));
            Line(sb, "| " + string.Join(" | ", cells) + " |");
        }
        Line(sb);
        Line(sb, "*Index points, first model minus second, split by the scoring weights; the parts sum to the difference. Other is the critical-error cap and the difficulty weighting.*");
        Line(sb);
    }

    /// <summary>The item-level control table of a run- or battery-completion analysis with controls.</summary>
    private static void RcaControlItems(StringBuilder sb, Context ctx, RootCauseResult result)
    {
        Heading(sb, "### Item-level controls");
        if (result.ControlItems.Count == 0)
        {
            Line(sb, "No control shares a scored question with the subject on the same item revision.");
            Line(sb);
            return;
        }

        Line(sb, "| Question | Topic | " + Cell(ctx.Sheet.SubjectLabel) + " | Controls' mean | Difference | Controls | Mark |");
        Line(sb, "|---|---|---|---|---|---|---|");
        foreach (var item in result.ControlItems.OrderBy(i => i.Number))
        {
            string mark = item.InstrumentSuspect ? "instrument-suspect" : item.ModelSpecific ? "model-specific" : NoValue;
            Line(sb, RcaRow(
                Cell(string.IsNullOrWhiteSpace(item.Reference) ? ctx.QuestionLabel(item.Number) : item.Reference),
                Cell(Topic(ctx, item.Number) ?? NoValue),
                BenchmarkReportFormat.Whole(item.SubjectScore),
                BenchmarkReportFormat.Whole(item.ControlsMean),
                BenchmarkReportFormat.WholeDifference(item.SubjectScore, item.ControlsMean),
                Inv(item.ControlCount),
                mark));
        }
        Line(sb);
        Line(sb, "*" + RcaControlsNote + "*");
        Line(sb);
    }

    /// <summary>Appendix B: calls, latency and tokens per model, then every component of each decomposed pair.</summary>
    private static void RcaDecompositionDetail(StringBuilder sb, Context ctx, RootCauseResult? result)
    {
        Heading(sb, "## Appendix B: Speed and cost decompositions");
        if (result == null)
        {
            Line(sb, RcaNoAnalysis);
            Line(sb);
            return;
        }

        Heading(sb, "### Calls, latency and output");
        Line(sb, "| Model | Model calls per answer | Time to first token per call, median | Decode rate | Reasoning share | Output tokens per question |");
        Line(sb, "|---|---|---|---|---|---|");
        foreach (var summary in result.Evidence)
        {
            if (summary.TimeUnavailableReason != null)
            {
                Line(sb, RcaRow(Cell(RcaTokenName(ctx, summary.Token)), "not recorded (" + Cell(OneLine(summary.TimeUnavailableReason).TrimEnd('.')) + ")",
                    NoValue, NoValue, NoValue, NoValue));
                continue;
            }
            Line(sb, RcaRow(
                Cell(RcaTokenName(ctx, summary.Token)),
                summary.CallsPerAnswer is double calls ? BenchmarkReportFormat.CompactDecimal(calls) : NoValue,
                summary.TtftPerCallP50Ms is double ttft ? BenchmarkReportFormat.Seconds(ttft) : NoValue,
                summary.DecodeTokensPerSecond is double rate ? BenchmarkReportFormat.Whole(rate) + " tokens/s" : NoValue,
                summary.ReasoningShare is double share ? RootCauseHypotheses.Pct(share) : NoValue,
                summary.OutputTokens is double tokens ? BenchmarkReportFormat.Count(tokens) : NoValue));
        }
        Line(sb);
        UseTerms(ctx, RootCauseGlossary.TimeToFirstToken, RootCauseGlossary.OutputGeneration, RootCauseGlossary.MedianAndMean, RootCauseGlossary.Tokens);
        bool merged = result.Evidence.Any(e => e.Tokens.Any(s => s.Key == RootCauseSegments.OutputWithThinking));
        Line(sb, "*Model calls per answer: the model is called again after each tool round" + Gloss(ctx, RootCauseGlossary.ToolRound)
            + ". Decode rate: the median output tokens per second once the model has started writing. Reasoning share: the part of the "
            + "output tokens spent on hidden thinking"
            + (merged ? "; not stated for a provider that counts thinking inside its output tokens and does not report it separately" : string.Empty) + ".*");
        Line(sb);

        Heading(sb, "### Tokens per question");
        var keys = RootCauseSegments.CostComponents;
        bool mergedTokens = RcaOutputMerged(result.Evidence.Select(e => e.Tokens));
        var header = new List<string> { "Model" };
        header.AddRange(keys.Select(k => RcaComponentHeader(k, mergedTokens)));
        header.Add("Cache-read share");
        var rows = new List<IReadOnlyList<string>>();
        foreach (var summary in result.Evidence)
        {
            var cells = new List<string> { Cell(RcaTokenName(ctx, summary.Token)) };
            cells.AddRange(keys.Select(k => RcaSegmentCell(summary.Tokens, k, BenchmarkReportFormat.Count)));
            cells.Add(summary.CacheReadShare is double share ? RootCauseHypotheses.Pct(share) : NoValue);
            rows.Add(cells);
        }
        var (shown, shownRows, dropped) = DropConstantColumns(header, rows, keepFirst: 1);
        TableHeaderOf(sb, shown);
        foreach (var cells in shownRows) Line(sb, RcaRow(cells));
        Line(sb);
        var tokenNotes = new List<string>();
        if (DroppedColumnsSentence(dropped) is { Length: > 0 } droppedSentence) tokenNotes.Add(droppedSentence);
        if (mergedTokens && shown.Contains("Output")) tokenNotes.Add(RcaThinkingIncludedNote);
        if (tokenNotes.Count > 0)
        {
            Line(sb, "*" + string.Join(" ", tokenNotes) + "*");
            Line(sb);
        }

        if (ctx.Comparison)
        {
            if (SpeedAndCostKeys().Any(key => OrderedPeers(ctx.Sheet).Any(p => IsAvailable(ctx, ModelKey(p.Letter, key)))))
            {
                Heading(sb, "### Speed and cost figures");
                SpeedAndCostTable(sb, ctx);
            }
        }
        else
        {
            SpeedAndCost(sb, ctx, "### Speed and cost figures");
        }

        RcaPairDecompositions(sb, ctx, result.SpeedPairs, "### Speed differences, decomposed", cost: false);
        RcaPairDecompositions(sb, ctx, result.CostPairs, "### Cost differences, decomposed", cost: true);
    }

    /// <summary>
    /// Each decomposed pair's exact decomposition, one row per component, leaving out a component whose part and share
    /// are both zero; nothing without one.
    /// </summary>
    private static void RcaPairDecompositions(StringBuilder sb, Context ctx, IReadOnlyList<RootCausePairDecomposition> pairs, string heading, bool cost)
    {
        var available = pairs.Where(p => p.UnavailableReason == null && p.Difference.HasValue && p.Components.Count > 0).ToList();
        if (available.Count == 0) return;

        Heading(sb, heading);
        Line(sb, cost
            ? "| Pair | Questions | Difference per question | Component | Effect | Part | Share (95% interval) |"
            : "| Pair | Questions | Difference per question | Component | Part | Share (95% interval) |");
        Line(sb, cost ? "|---|---|---|---|---|---|---|" : "|---|---|---|---|---|---|");
        bool skipped = false;
        foreach (var pair in available)
        {
            string name = Cell(RcaPairName(ctx, pair.First, pair.Second));
            string difference = RcaAmount(pair.Difference!.Value, cost);
            foreach (var component in pair.Components)
            {
                if (IsZeroLikeCell(RcaAmount(component.Amount, cost).TrimStart('+', '-'))
                    && (component.Share is not double part || RootCauseHypotheses.Pct(part) is "0%" or "-0%"))
                {
                    skipped = true;
                    continue;
                }
                string share = component.Share is double s
                    ? RootCauseHypotheses.Pct(s) + (component.ShareLower is double lo && component.ShareUpper is double hi ? " (" + RootCauseHypotheses.Interval(lo, hi) + ")" : string.Empty)
                    : NoValue;
                var cells = new List<string> { name, Inv(pair.SharedQuestions), difference, Cell(Capitalized(component.Label)) };
                if (cost) cells.Add(Cell(component.Effect ?? NoValue));
                cells.Add(RcaAmount(component.Amount, cost));
                cells.Add(share);
                Line(sb, RcaRow(cells));
            }
        }
        Line(sb);
        ExplainedTerms(ctx, RootCauseGlossary.DecompositionShare, RootCauseGlossary.BootstrapInterval);
        UseTerms(ctx, RootCauseGlossary.Interval95);
        Line(sb, "*" + RcaDecompositionNote + " The difference is the first model's minus the second's."
            + (skipped ? " A component whose part and share are both zero is left out." : string.Empty) + "*");
        Line(sb);
    }

    /// <summary>A grade reason that restates one of the hypothesis' robustness checks (<see cref="RootCauseHypotheses.IsRobustnessReason"/>).</summary>
    private static bool IsRobustnessReason(RootCauseHypothesis h, string reason)
        => RootCauseHypotheses.IsRobustnessReason(h, reason);

    /// <summary>
    /// Appendix C: the grading rule, then per axis each fired hypothesis' evidence, the grade reasons its
    /// robustness checks do not already give, and those checks, and the null hypotheses as one compact table.
    /// </summary>
    private static void RcaHypothesisDetail(StringBuilder sb, Context ctx, RootCauseResult? result)
    {
        Heading(sb, "## Appendix C: Hypothesis details");
        if (result == null)
        {
            Line(sb, RcaNoAnalysis);
            Line(sb);
            return;
        }

        Line(sb, RcaGradesNote(ctx, result));
        Line(sb);
        foreach (string axis in RootCauseAxes.All)
        {
            var hypotheses = result.Hypotheses.Where(h => h.Axis == axis).ToList();
            if (hypotheses.Count == 0) continue;

            Heading(sb, "### " + RootCauseAxes.Name(axis));
            foreach (var h in hypotheses.Where(h => h.Kind != "null"))
            {
                foreach (string reason in h.GradeReasons.Concat(h.Robustness.Select(r => r.Name))) UseReasonTerms(ctx, reason);
                if (h.Robustness.Count > 0) UseTerms(ctx, RootCauseGlossary.RobustnessCheck);
                if (h.Confounds.Count > 0) UseTerms(ctx, RootCauseGlossary.Confound);
                Line(sb, "- **" + RcaHypothesisName(ctx, h) + "** (" + SheetText(ctx, h.ModelsText) + "; locus " + h.Locus + "; " + RootCauseGrades.HypothesisWordWithGrade(h) + ")");
                Line(sb, "  - *For:* " + (h.For.Count == 0 ? "None recorded." : BenchmarkReportSentenceCase.UpperFirst(SheetText(ctx, string.Join(" ", h.For)))));
                Line(sb, "  - *Against:* " + (h.Against.Count == 0 ? "None recorded." : BenchmarkReportSentenceCase.UpperFirst(SheetText(ctx, string.Join(" ", h.Against)))));
                var reasons = h.GradeReasons.Where(r => !IsRobustnessReason(h, r)).ToList();
                Line(sb, "  - *Grade reasons:* " + (h.GradeReasons.Count == 0 ? "Every condition of the grade is met."
                    : reasons.Count == 0 ? "See Robustness."
                    : BenchmarkReportSentenceCase.UpperFirst(SheetText(ctx, string.Join("; ", reasons)).TrimEnd('.') + ".")));
                Line(sb, "  - *Refuted if:* " + BenchmarkReportSentenceCase.UpperFirst(SheetText(ctx, h.RefutedIf)));
                if (h.Robustness.Count > 0)
                {
                    Line(sb, "  - *Robustness:* " + SheetText(ctx, string.Join("; ", h.Robustness.Select(r => r.Name + " " + RcaCheckText(r.Status)
                        + (string.IsNullOrWhiteSpace(r.Detail) ? string.Empty : " (" + r.Detail.TrimEnd('.') + ")")))) + ".");
                }
                if (h.Confounds.Count > 0)
                {
                    Line(sb, "  - *Open confounds:* " + OneLine(string.Join(", ", h.Confounds)) + ".");
                }
            }

            var nulls = hypotheses.Where(h => h.Kind == "null").ToList();
            if (nulls.Count > 0)
            {
                if (hypotheses.Count > nulls.Count) Line(sb);
                Line(sb, "| Hypothesis | Pair | Paired result | Grade |");
                Line(sb, "|---|---|---|---|");
                foreach (var h in nulls)
                {
                    string pair = h.Id == "H0-W" || h.Models.Count < 2
                        ? "within " + RcaTokenName(ctx, h.Models.FirstOrDefault() ?? string.Empty)
                        : RcaPairName(ctx, h.Models[0], h.Models[1]);
                    Line(sb, RcaRow(Cell(h.Key), Cell(pair), Cell(RcaNullResult(ctx, h)), RootCauseGrades.HypothesisWordWithGrade(h)));
                }
            }
            Line(sb);
        }
    }

    /// <summary>
    /// The grading rule of Appendix C in the order of the summary's grade words, with the null hypothesis, a
    /// confound and a robustness check glossed at their first use, then what each entry gives.
    /// </summary>
    private static string RcaGradesNote(Context ctx, RootCauseResult result)
    {
        UseTerms(ctx, RootCauseGlossary.GradeWords, RootCauseGlossary.Hypothesis, RootCauseGlossary.RunsPerSuite);
        string locus = result.Hypotheses.Any(h => h.Kind != "null")
            ? " Each entry gives the models, the locus" + Gloss(ctx, RootCauseGlossary.Locus) + " and the grade, then the evidence for and against."
            : string.Empty;
        return "Code raised each hypothesis from a rule fixed in advance and graded it. "
            + "**Established (High)** needs all of these: it is measured; it is still clear after the correction for many comparisons; "
            + "its null hypothesis" + Gloss(ctx, RootCauseGlossary.NullHypothesis) + " is rejected; no confound"
            + Gloss(ctx, RootCauseGlossary.Confound) + " is open; every robustness check" + Gloss(ctx, RootCauseGlossary.RobustnessCheck)
            + " passed; and each model ran at least twice per suite. **Probable (Moderate)**: exactly one of those conditions is missing. "
            + "**Suggestive (Low)**: it is not measured, two or more conditions are missing, or the null hypothesis stands. "
            + "**Ruled out (Not supported)**: the hypothesis's own test contradicts it. A null hypothesis reads **Rejected — a real difference** "
            + "when a test found a real difference, and **Stands — may be chance** when none could be shown." + locus;
    }

    /// <summary>
    /// What a null rests on: the pair's short paired result on a comparison-scope sheet, else the first reason
    /// against it, else the first for it.
    /// </summary>
    private static string RcaNullResult(Context ctx, RootCauseHypothesis h)
    {
        if (ctx.Comparison && h.Id != "H0-W" && h.Models.Count >= 2)
        {
            string axis = h.Axis switch { RootCauseAxes.Speed => "speed", RootCauseAxes.Cost => "cost", _ => "quality" };
            string first = h.Models[0], second = h.Models[1];
            if (LetterPrecedes(second, first)) (first, second) = (second, first);
            if (Fact(ctx, BenchmarkComparisonReportFacts.PairPrefix(first, second) + axis + ".short") is { Available: true } fact)
            {
                return Shown(ctx, fact);
            }
        }
        string? reason = h.Against.FirstOrDefault() ?? h.For.FirstOrDefault();
        return reason == null ? NoValue : SheetText(ctx, reason);
    }

    /// <summary>Letter order before ordinal: <c>Z</c> before <c>AA</c>.</summary>
    private static bool LetterPrecedes(string a, string b) => a.Length != b.Length ? a.Length < b.Length : string.CompareOrdinal(a, b) < 0;

    /// <summary>Appendix D: each recommended run's card and its launcher settings.</summary>
    private static void RcaRunSettings(StringBuilder sb, Context ctx, RootCauseResult? result)
    {
        Heading(sb, "## Appendix D: Run settings");
        var runs = result?.RecommendedRuns ?? new List<RootCauseRecommendedRun>();
        if (runs.Count == 0)
        {
            Line(sb, "Code proposes no run.");
            Line(sb);
            return;
        }

        UseTerms(ctx, RootCauseGlossary.ResponseStyle);
        Line(sb, "Each run's settings are the launcher's, copied from the analyzed runs unless its kind changes them; a setting the runs did not share is left for you to choose.");
        Line(sb);
        foreach (var run in runs.OrderBy(r => r.Number))
        {
            Heading(sb, "### Run " + Inv(run.Number) + ": " + RootCauseNextRuns.KindName(run.Kind) + " (" + RootCausePurposes.Text(run.Purpose) + ", " + run.Mark.ToLowerInvariant() + ")");
            var decides = (run.DecidesItems ?? new List<string>()).Where(d => !string.IsNullOrWhiteSpace(d)).ToList();
            if (decides.Count > 1)
            {
                Line(sb, "- **Decides:**");
                foreach (string item in decides) Line(sb, "  - " + SheetText(ctx, item));
            }
            else
            {
                Line(sb, "- **Decides:** " + SheetText(ctx, decides.Count == 1 ? decides[0] : run.Decides));
            }
            Line(sb, "- **Predicted comparability:** " + (string.IsNullOrWhiteSpace(run.PredictedTier) ? "Not predicted" : OneLine(run.PredictedTier)));
            string estimate = RootCauseNextRuns.EstimateText(run);
            Line(sb, "- **Estimate:** " + BenchmarkReportSentenceCase.UpperFirst(estimate.Length == 0 ? "not estimated" : estimate));
            if (run.HypothesisIds.Count > 0)
            {
                Line(sb, "- **Settles:** " + string.Join(", ", run.HypothesisIds));
            }
            if (run.When != null || run.AfterSystemHypotheses)
            {
                Line(sb, "- **When:** " + RcaWhenText(run) + ".");
            }
            Line(sb);

            Line(sb, "| Launcher field | Setting |");
            Line(sb, "|---|---|");
            foreach (var (field, setting) in RcaLauncherSettings(ctx, run))
            {
                Line(sb, RcaRow(field, Cell(setting)));
            }
            Line(sb);

            if (run.Notes.Count > 0)
            {
                foreach (string note in run.Notes)
                {
                    Line(sb, "- " + SheetText(ctx, note));
                }
                Line(sb);
            }
        }
    }

    /// <summary>
    /// Appendix E: the known facts of kind <c>count</c> and <c>configuration</c>, numbered as their <c>rca.known.&lt;n&gt;</c>
    /// facts, so the numbering may skip; the tests and decompositions are in the body and Appendices A and B. A result
    /// stored without the kinds lists every known fact. The document leaves the appendix out when it has none to list.
    /// </summary>
    private static void RcaKnownFacts(StringBuilder sb, Context ctx, RootCauseResult? result)
    {
        Heading(sb, "## Appendix E: Known facts");
        var listed = result == null ? new List<RootCauseKnownFact>() : RcaListedKnownFacts(result);
        if (result == null)
        {
            Line(sb, RcaNoAnalysis);
        }
        else
        {
            Line(sb, "The counts and the configuration facts the analysis states, each numbered as the writer cites it. Its tests and decompositions are given "
                + "once, under the evidence and in Appendices A and B.");
            Line(sb);
            // A bullet with its number in bold, since a Markdown ordered list would renumber past a gap.
            foreach (var fact in listed)
            {
                Line(sb, "- **" + Inv(fact.Number) + ".** " + SheetText(ctx, fact.Text));
            }
        }
        Line(sb);
    }

    /// <summary>
    /// The known facts Appendix E lists, by number: those of kind <c>count</c> and <c>configuration</c>, or every one for
    /// a result stored without the kinds.
    /// </summary>
    private static List<RootCauseKnownFact> RcaListedKnownFacts(RootCauseResult result)
    {
        var known = result.KnownFacts ?? new List<RootCauseKnownFact>();
        bool kinds = known.Any(k => !string.IsNullOrEmpty(k.Kind));
        return known
            .Where(k => !kinds || k.Kind is RootCauseKnownFacts.KindCount or RootCauseKnownFacts.KindConfiguration)
            .OrderBy(k => k.Number)
            .ToList();
    }

    /// <summary>
    /// The question details appendix, lettered <paramref name="letter"/>: the details of the questions the writer cites
    /// in its prose and lists, those with a critical error (<see cref="RcaHadCriticalError"/>), and each
    /// instrument-suspect or model-specific control item; only those with answers given in detail, each answer as its
    /// excerpt and the rubric without its <c>SOURCE</c> paragraphs.
    /// </summary>
    private static void RcaQuestionDetails(StringBuilder sb, Context ctx, RootCauseResult? result, string letter)
    {
        string title = "Appendix " + letter + ": Question details";
        var cited = new HashSet<int>(BenchmarkReportPackValidator.CitedQuestions(ctx.Writer, ctx.Sheet));
        foreach (var q in ctx.Sheet.Questions)
        {
            if (RcaHadCriticalError(ctx, q)) cited.Add(q.Number);
        }
        if (result != null)
        {
            cited.UnionWith(result.ControlItems.Where(i => i.InstrumentSuspect || i.ModelSpecific).Select(i => i.Number));
        }

        int length = sb.Length;
        if (cited.Count > 0)
        {
            if (ctx.Comparison)
            {
                ComparisonQuestionDetails(sb, ctx, grading: true, level: 2, only: cited, title: title);
            }
            else
            {
                var questions = ctx.Sheet.Questions
                    .Where(q => cited.Contains(q.Number) && ContentFor(ctx, q.Number).Count > 0)
                    .OrderBy(q => q.Number)
                    .ToList();
                if (questions.Count > 0)
                {
                    QuestionDetails(sb, ctx, questions, grading: ctx.Options.Disclosure == BenchmarkReportDisclosure.Full, level: 2, title: title, excerpts: true);
                }
            }
        }
        if (sb.Length == length)
        {
            Heading(sb, "## " + title);
            Line(sb, "No question given in detail is cited.");
            Line(sb);
        }
    }

    /// <summary>
    /// Whether a question had a critical error, confirmed or overturned: a model's matrix cell (on a per-model sheet,
    /// the question itself) records one, or records a resolution other than <c>None</c>, or one of the content's
    /// answers to it stores a resolution. A refuted sentence or a disputed claim alone does not count.
    /// </summary>
    private static bool RcaHadCriticalError(Context ctx, BenchmarkReportQuestion q)
    {
        bool recorded = q.Models is { } cells
            ? cells.Any(c => c.CriticalError || (c.CriticalErrorCount ?? 0) > 0
                             || (c.CriticalErrorResolutions ?? new List<string>()).Any(r => CriticalErrorResolutionText(r) != null))
            : q.CriticalError || (q.CriticalErrorCount ?? 0) > 0;
        return recorded || ContentFor(ctx, q.Number).Any(b => CriticalErrorResolutionText(b.Question.CriticalErrorResolution) != null);
    }

    /// <summary>The glossary appendix, lettered <paramref name="letter"/>: every term the document uses (<see cref="Context.UsedTerms"/>), alphabetically, with its full definition.</summary>
    private static void RcaGlossary(StringBuilder sb, Context ctx, string letter)
    {
        Heading(sb, "## Appendix " + letter + ": Glossary");
        var terms = RootCauseGlossary.Alphabetical(ctx.UsedTerms);
        if (terms.Count == 0)
        {
            Line(sb, "This document uses no term the glossary explains.");
        }
        foreach (var term in terms)
        {
            Line(sb, "- **" + term.Name + ":** " + term.Definition);
        }
        Line(sb);
    }

    /// <summary>
    /// The fact sheet as JSON, in the Markdown copy only; an anonymized copy letters every peer's name, the root-cause
    /// data's too. A render without the fact sheet (the PDF and Word) has no such section and no contents entry for it.
    /// </summary>
    private static void RcaFactSheet(StringBuilder sb, Context ctx)
    {
        if (!ctx.Options.IncludeFactSheet) return;

        Heading(sb, "## Fact sheet");
        Line(sb, "```json");
        Line(sb, BenchmarkReportJson.SerializeSorted(ctx.Anonymized ? RcaAnonymizedSheet(ctx) : ctx.Sheet));
        Line(sb, "```");
        Line(sb);
    }

    /// <summary>
    /// The anonymized copy of the sheet with its root-cause data lettered too: every analyzed model but a
    /// per-model subject reads <c>Model X</c> in the models, the evidence and the chart rows, with no provider
    /// or model id, and a recommended run names an analyzed model so; in comparison scope another configured
    /// model is not named. A recommended run's grader names are left out in either scope. The controls stay named:
    /// they are evidence, not peers.
    /// </summary>
    private static BenchmarkReportFactSheet RcaAnonymizedSheet(Context ctx)
    {
        var copy = ctx.Comparison ? ComparisonAnonymizedSheet(ctx) : AnonymizedSheet(ctx);
        var result = copy.RootCause;
        if (result == null) return copy;

        var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        var byLabel = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var model in result.Models)
        {
            if (model.IsSubject && !ctx.Comparison)
            {
                aliases[model.Token] = model.Label;
                continue;
            }
            string alias = "Model " + (model.Letter ?? model.Token);
            aliases[model.Token] = alias;
            if (!string.IsNullOrWhiteSpace(model.Label)) byLabel.TryAdd(model.Label, alias);
            model.Label = alias;
            model.Provider = string.Empty;
            model.ModelId = string.Empty;
        }

        foreach (var summary in result.Evidence)
        {
            if (aliases.TryGetValue(summary.Token, out string? alias)) summary.Label = alias;
        }
        foreach (var row in result.PointsAtRisk.Concat(result.SpeedRows).Concat(result.CostRows))
        {
            if (row.Token != null && aliases.TryGetValue(row.Token, out string? alias)) row.Label = alias;
        }
        foreach (var run in result.RecommendedRuns)
        {
            run.ModelNames = run.ModelNames
                .Select(name => byLabel.TryGetValue(name, out string? alias) ? alias : ctx.Comparison ? "another configured model" : name)
                .ToList();
            run.AssessorName = null;
            run.CoAssessorName = null;
            run.ReferenceReaderName = null;
            run.ClaimVerifierName = null;
        }
        return copy;
    }

    // ---------------------------------------------------------------------------------------------
    // Building blocks
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// A GitHub alert callout: <c>&gt; [!NOTE]</c> (or <c>IMPORTANT</c>, <c>TIP</c>, <c>WARNING</c>), then every line
    /// inside the blockquote, an empty line as a bare <c>&gt;</c>. The first line opens with its bold label.
    /// </summary>
    private static void Callout(StringBuilder sb, string kind, IEnumerable<string> lines)
    {
        Line(sb, "> [!" + kind + "]");
        foreach (string line in lines)
        {
            foreach (string part in Normalize(line).Split('\n'))
            {
                Line(sb, part.Length == 0 ? ">" : "> " + part);
            }
        }
        Line(sb);
    }

    /// <summary>The terms a grade reason or a robustness check names: replicate runs and the leave-one-out check.</summary>
    private static void UseReasonTerms(Context ctx, string? text)
    {
        if (string.IsNullOrEmpty(text)) return;
        if (text.Contains("replicate", StringComparison.OrdinalIgnoreCase)) UseTerms(ctx, RootCauseGlossary.ReplicateRun);
        if (text.Contains("leave one", StringComparison.OrdinalIgnoreCase) || text.Contains("leave-one", StringComparison.OrdinalIgnoreCase))
        {
            UseTerms(ctx, RootCauseGlossary.LeaveOneOut);
        }
        if (text.Contains("confound", StringComparison.OrdinalIgnoreCase)) UseTerms(ctx, RootCauseGlossary.Confound);
    }

    /// <summary>The models' rows as the document lists them: letter order in comparison scope, the subject first in model scope.</summary>
    private static List<(BenchmarkReportEntryFigures Entry, BenchmarkReportPeer? Peer)> RcaRows(Context ctx)
        => ctx.Comparison
            ? ComparisonRows(ctx).Select(r => (r.Entry, (BenchmarkReportPeer?)r.Peer)).ToList()
            : OrderedEntries(ctx.Sheet);

    /// <summary><see cref="RcaRows"/> by Intelligence Index, highest first; a model without one last, then in document order.</summary>
    private static List<(BenchmarkReportEntryFigures Entry, BenchmarkReportPeer? Peer)> RcaRankedRows(Context ctx)
        => RcaRows(ctx)
            .Select((row, i) => (Row: row, Index: i))
            .OrderByDescending(x => x.Row.Entry.QualityIndex ?? double.NegativeInfinity)
            .ThenBy(x => x.Index)
            .Select(x => x.Row)
            .ToList();

    /// <summary>A model's table cell: its name with its role marks in comparison scope, the subject in bold in model scope.</summary>
    private static string RcaRowName(Context ctx, BenchmarkReportPeer? peer)
        => peer == null || !ctx.Comparison
            ? TableName(ctx, peer)
            : (ctx.Anonymized ? "Model " + peer.Letter : Cell(peer.Label)) + RoleMarks(ctx, peer.Letter);

    /// <summary>A model of the analysis by its token as this copy names it: the subject by its label, a letter by <see cref="NameOfLetter"/>.</summary>
    private static string RcaTokenName(Context ctx, string token)
        => string.Equals(token, RootCauseEngine.SubjectToken, StringComparison.Ordinal) ? ctx.Sheet.SubjectLabel : NameOfLetter(ctx, token);

    private static string RcaPairName(Context ctx, string first, string second) => RcaTokenName(ctx, first) + " vs " + RcaTokenName(ctx, second);

    /// <summary>
    /// A model profile's entry (<c>rca.profile.&lt;T&gt;.*</c>) as an evidence line gives it: the model as this copy names
    /// it, then the entry, <c>Lyra Mini: 1 critical error (Q3)</c>; null for any other key and for an entry the sheet
    /// lacks or leaves unavailable, which keep their label.
    /// </summary>
    private static string? RcaProfileEvidence(Context ctx, string key, BenchmarkReportFact? fact)
    {
        if (fact is not { Available: true } || !key.StartsWith(RootCauseFacts.Prefix + "profile.", StringComparison.Ordinal)) return null;
        string[] parts = key.Split('.');
        return parts.Length >= 4 && parts[2].Length > 0 ? RcaTokenName(ctx, parts[2]) + ": " + Shown(ctx, fact) : null;
    }

    private static string Capitalized(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    /// <summary>
    /// A table row of a Root Cause Analysis: the cells, already escaped, each opening with a capital
    /// (<see cref="BenchmarkReportSentenceCase.UpperFirst"/>), between pipes.
    /// </summary>
    private static string RcaRow(IEnumerable<string> cells)
        => "| " + string.Join(" | ", cells.Select(c => BenchmarkReportSentenceCase.UpperFirst(c))) + " |";

    /// <inheritdoc cref="RcaRow(IEnumerable{string})"/>
    private static string RcaRow(params string[] cells) => RcaRow((IEnumerable<string>)cells);

    /// <summary>The text ending in one period: <c>item sampling only.</c></summary>
    private static string WithPeriod(string text)
    {
        string trimmed = text.TrimEnd();
        return trimmed.Length == 0 || trimmed.EndsWith('.') ? trimmed : trimmed + ".";
    }
}
