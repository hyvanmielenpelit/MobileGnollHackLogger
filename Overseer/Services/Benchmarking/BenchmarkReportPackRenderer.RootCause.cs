namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services.Benchmarking.RootCause;

/// <summary>
/// The Root Cause Analysis, in comparison scope (every covered model as an equal) and model scope (a subject
/// with its peers, or a run or battery run on its own with its controls), answer first. The body: the summary
/// (the headline, the findings code ranks, the models at a glance and how sure the grades can be); the key terms the summary and the conclusions use
/// (<see cref="RootCauseGlossary"/>), each term glossed once in the text, at its first use; why each model scored as
/// it did; in comparison scope, the unexpected results; scope and design; the evidence on intelligence, speed and cost;
/// the hypotheses per axis with the writer's own; the conclusions; the runs to make next; limitations and threats to validity. Then the lettered appendices:
/// A intelligence detail (in comparison scope, without the results the body gives, and only the paired tests the
/// body does not print), B speed and cost decompositions, C hypothesis details, D run settings, E the known counts
/// and configuration facts, F reproducibility, G the cited and critical-error questions with their answer excerpts
/// and H glossary, the letters after D closing up when there is no known count or configuration fact to list;
/// and, in the Markdown copy, the fact sheet. A table column that is zero or not recorded in every row is left out,
/// and the table's note says so. Every table is computed by code from the stored
/// <see cref="RootCauseResult"/> and facts; the writer's slots follow the code-rendered part of their section.
/// The headline, the caveats, the writer's hypotheses and the conclusions stand in GitHub alert
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

    /// <summary>The most null hypotheses an axis' sentence on them names before it counts the rest.</summary>
    private const int RcaNullPairsMax = 5;

    private const string RcaNoAnalysis = "No root-cause analysis was computed for this document.";

    /// <summary>The Conclusions table's best-hypothesis cell of an axis whose every hypothesis was ruled out.</summary>
    internal const string RcaAllRuledOut = "none (all ruled out)";

    private const string RcaNoProfiles = "No model profile was computed for this document; regenerate it to get them.";

    private const string RcaControlsNote = "Controls' mean: the mean score of the controls that scored the question. Instrument-suspect: the "
        + "subject and every control scored below 50. Model-specific: the subject scored below 50 while every control scored 75 or above.";

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
        RcaUnexpectedResults(rest, ctx, result);
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
        if (ctx.Comparison) ComparisonReproducibility(rest, ctx, reproducibility, sharedOnce: true);
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
    /// The headline as the key finding, then the findings code ranks (<see cref="RcaFindings"/>); every model at a glance;
    /// and the grade words with the cap the run counts put on them.
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
        if (RcaFamilyGap(result) != null)
        {
            caveat.Add(string.Empty);
            caveat.Add("The order between models of those two providers depends on the panel's balance; see What we found.");
        }
        Callout(sb, "WARNING", caveat);
    }

    /// <summary>The most bullets What we found prints under the key finding.</summary>
    private const int RcaMaxFindings = 7;

    /// <summary>A share of answers without tools at or above this is a finding.</summary>
    private const double RcaNoToolFindingShare = 0.20;

    /// <summary>With two or more models, a model's share of answers without tools is a finding at this many times the other models' median.</summary>
    private const double RcaNoToolContrast = 2.0;

    /// <summary>Value for money is a finding when a model not established to differ from the cheapest costs this many times as much.</summary>
    private const double RcaValueCostRatio = 3.0;

    /// <summary>A higher thinking level is a finding when it takes or costs this many times as much as the lower.</summary>
    private const double RcaThinkingRatio = 1.5;

    /// <summary>The most thinking-level pairs, and the most notable findings, What we found prints.</summary>
    private const int RcaMaxPerCandidate = 2;

    /// <summary>The differences from the reference are a finding when one that is not established is at least this many index points.</summary>
    private const double RcaReferenceGapPoints = 3.0;

    /// <summary>A model's tokens per question are a finding at this many times the other models' median.</summary>
    private const double RcaVolumeContrast = 2.0;

    /// <summary>The most models the token-volume bullet names.</summary>
    private const int RcaMaxVolumeModels = 2;

    /// <summary>
    /// The kinds of notable finding What we found prints: not grader disagreement or a judge-dependent order, which the profile
    /// flags and the grader family cover, and not answer length, which is no finding about quality, speed or cost.
    /// </summary>
    private static bool RcaNotableKind(string kind) => kind is BenchmarkNotableFindings.CheaperAndBetter or BenchmarkNotableFindings.FasterAndBetter
        or BenchmarkNotableFindings.ThinkingLevelInversion or BenchmarkNotableFindings.LeaderMiss or BenchmarkNotableFindings.UnderdogWin
        or BenchmarkNotableFindings.SharedFailure or BenchmarkNotableFindings.DifficultyInversion or BenchmarkNotableFindings.CriticalErrorConcentration
        or BenchmarkNotableFindings.OutputTrouble or BenchmarkNotableFindings.ToolUseOutlier or BenchmarkNotableFindings.ProviderErrors;

    /// <summary>One bullet of What we found, with the terms it uses.</summary>
    private sealed record RcaFinding(string Text, IReadOnlyList<string> Terms);

    /// <summary>A model of the analysis as What we found reads it; its time and cost are null where its figures are degraded.</summary>
    private sealed record RcaModel(string Token, string? Letter, BenchmarkReportEntryFigures Entry, string? Provider, string? ModelId, string? ThinkingLevel)
    {
        public double? Time => Entry.SpeedDegraded ? null : Entry.ModelTimeP50Ms;

        public double? Cost => Entry.CostDegraded ? null : Entry.CostPerQuestionUsd;
    }

    /// <summary>Whether one model is established to score higher or lower than another.</summary>
    private enum RcaQuality
    {
        NotEstablished,
        Higher,
        Lower
    }

    /// <summary>
    /// What we found's bullets, in the project's order and at most <see cref="RcaMaxFindings"/>: what bears on the Overseer chat
    /// (answers without tools, a weakness every model shares), then the benchmark itself (the grader family), then the unexpected
    /// results, then model selection (value for money, thinking levels, token volume, the notable findings, the differences from
    /// the reference). Each is printed only when its gate passes, so none restates an extreme the At a glance table shows. Empty
    /// without an analysis.
    /// </summary>
    private static List<string> RcaFindings(Context ctx, RootCauseResult? result)
    {
        var findings = new List<string>();
        if (result == null) return findings;

        void Add(RcaFinding? finding)
        {
            if (finding == null || findings.Count >= RcaMaxFindings) return;
            UseTerms(ctx, finding.Terms.ToArray());
            findings.Add(finding.Text);
        }

        Add(RcaNoToolFinding(ctx, result));
        Add(RcaSharedWeaknessFinding(ctx, result));
        if (RcaFamilyGapSentence(ctx, result) is string family) Add(new RcaFinding(family, Array.Empty<string>()));
        Add(RcaUnexpectedFinding(ctx, result));
        var value = RcaValueFinding(ctx);
        Add(value?.Finding);
        var levels = RcaThinkingLevelFindings(ctx);
        Add(levels.Finding);
        Add(RcaVolumeFinding(ctx, result));
        if (!ctx.Comparison) return findings;

        foreach (var notable in RcaNotableFindings(ctx, value?.Cheapest, levels.Pairs)) Add(notable);
        if (value == null || value.Cheapest.Letter != RcaReferenceLetter(ctx)) Add(RcaReferenceFinding(ctx));
        return findings;
    }

    /// <summary>
    /// The unexpected results in one bullet, the anomalies of one prior with the same model expected ahead in one clause:
    /// <c>Unexpected: Orion Max scored below the cheaper Vega Pro (U1); Vega Pro was slower than the dearer Orion Max and Lyra Mini
    /// (U2–U3). See Unexpected results.</c> Null without an anomaly.
    /// </summary>
    private static RcaFinding? RcaUnexpectedFinding(Context ctx, RootCauseResult result)
    {
        var anomalies = result.Anomalies ?? new List<RootCauseAnomaly>();
        if (anomalies.Count == 0) return null;

        var clauses = new List<string>();
        foreach (var group in anomalies
                     .OrderBy(a => a.Number)
                     .GroupBy(a => (a.Prior, a.Expected)))
        {
            var members = group.ToList();
            string others = BenchmarkReportFormat.LetterList(members.Select(a => RcaTokenName(ctx, a.Other)).Distinct(StringComparer.Ordinal).ToList());
            string relation = group.Key.Prior switch
            {
                RootCausePriors.PriceTierSpeed => " was slower than the dearer ",
                RootCausePriors.NewerRelease => " scored below the older ",
                _ => " scored below the cheaper "
            };
            clauses.Add(RcaTokenName(ctx, group.Key.Expected) + relation + others + " (" + RcaRefRanges(members.Select(a => a.Ref).ToList()) + ")");
        }
        return new RcaFinding("Unexpected: " + string.Join("; ", clauses) + ". See Unexpected results.", new[] { RootCauseGlossary.UnexpectedResult });
    }

    /// <summary>
    /// Refs such as <c>U2</c>, <c>U3</c>, <c>U4</c> and <c>U6</c> as <c>U2–U4 and U6</c>, or numbers alone as <c>3–5</c>:
    /// consecutive numbers as a range; a ref without a number as it is.
    /// </summary>
    private static string RcaRefRanges(IReadOnlyList<string> refs)
    {
        var parts = new List<string>();
        var numbered = new List<(string Ref, int Number)>();
        foreach (string r in refs.Distinct(StringComparer.Ordinal))
        {
            int digits = r.Length;
            while (digits > 0 && char.IsDigit(r[digits - 1])) digits--;
            if (digits < r.Length && int.TryParse(r.AsSpan(digits), NumberStyles.None, CultureInfo.InvariantCulture, out int n)) numbered.Add((r, n));
            else parts.Add(r);
        }
        numbered = numbered.OrderBy(x => x.Number).ToList();
        for (int i = 0; i < numbered.Count;)
        {
            int j = i;
            while (j + 1 < numbered.Count && numbered[j + 1].Number == numbered[j].Number + 1) j++;
            parts.Add(j == i ? numbered[i].Ref : numbered[i].Ref + "–" + numbered[j].Ref);
            i = j + 1;
        }
        return BenchmarkReportFormat.LetterList(parts);
    }

    /// <summary>
    /// Whether <paramref name="x"/> is established to score higher or lower than <paramref name="y"/>, as the notable findings
    /// and the tied ranks read it: by the pair's intelligence test where it establishes the order (in the all-pairs family, else
    /// in the reference family), otherwise by Intelligence Index intervals that do not overlap.
    /// </summary>
    private static RcaQuality RcaQualityVerdict(Context ctx, RcaModel x, RcaModel y)
    {
        if (x.Letter is string a && y.Letter is string b && RcaTestVerdict(RcaIntelligenceTest(ctx, a, b), a) is RcaQuality tested and not RcaQuality.NotEstablished)
        {
            return tested;
        }
        return RcaIntervalVerdict(x.Entry, y.Entry);
    }

    /// <summary>The order a paired test establishes for <paramref name="letter"/> against the other model of the pair.</summary>
    private static RcaQuality RcaTestVerdict(BenchmarkReportPairedTest? test, string letter)
    {
        string? winner = test is not { Established: true } ? null : test.Favors == "First" ? test.FirstLetter : test.Favors == "Second" ? test.SecondLetter : null;
        return winner == null ? RcaQuality.NotEstablished : winner == letter ? RcaQuality.Higher : RcaQuality.Lower;
    }

    /// <summary>The order the Intelligence Index intervals establish for <paramref name="x"/> against <paramref name="y"/>: where they do not overlap.</summary>
    private static RcaQuality RcaIntervalVerdict(BenchmarkReportEntryFigures x, BenchmarkReportEntryFigures y)
    {
        if (x.QualityLower is double xLower && y.QualityUpper is double yUpper && xLower > yUpper) return RcaQuality.Higher;
        if (x.QualityUpper is double xUpper && y.QualityLower is double yLower && xUpper < yLower) return RcaQuality.Lower;
        return RcaQuality.NotEstablished;
    }

    /// <summary>The intelligence test made on the pair of <paramref name="a"/> and <paramref name="b"/>: in the all-pairs family, else in the reference family; null when neither made one.</summary>
    private static BenchmarkReportPairedTest? RcaIntelligenceTest(Context ctx, string a, string b)
    {
        var families = ctx.Sheet.PairedTests ?? new List<BenchmarkReportPairedFamily>();
        foreach (var family in families.Where(f => f.Mode == "AllPairs").Concat(families.Where(f => f.Mode != "AllPairs")))
        {
            var tests = family.Measures.FirstOrDefault(m => m.Measure == BenchmarkPairedTests.IntelligenceMeasure);
            if (tests == null || tests.NotTestedReason != null) continue;
            var pair = tests.Pairs.FirstOrDefault(p => (p.FirstLetter == a && p.SecondLetter == b) || (p.FirstLetter == b && p.SecondLetter == a));
            if (pair != null && pair.NotTestedReason == null && pair.AdjustedPValue != null) return pair;
        }
        return null;
    }

    /// <summary>The models of the analysis in the order <see cref="RcaRows"/> gives them.</summary>
    private static List<RcaModel> RcaModels(Context ctx)
        => RcaRows(ctx)
            .Select(r => r.Peer is { } peer
                ? new RcaModel(peer.Letter, peer.Letter, r.Entry, peer.Provider, peer.ModelId, peer.ThinkingLevel)
                : new RcaModel(RootCauseEngine.SubjectToken, null, r.Entry, ctx.Sheet.SubjectProvider, ctx.Sheet.SubjectModelId, ctx.Sheet.SubjectThinkingLevel))
            .ToList();

    /// <summary>A ratio as What we found prints it: one decimal below 10 (<c>2.3</c>), whole from 10 (<c>67</c>).</summary>
    private static string RcaTimes(double ratio)
        => Math.Round(ratio, 1, MidpointRounding.AwayFromZero) < 10 ? BenchmarkReportFormat.OneDecimal(ratio) : BenchmarkReportFormat.Whole(ratio);

    /// <summary>The median of <paramref name="values"/>; zero when there is none.</summary>
    private static double RcaMedian(IEnumerable<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        if (sorted.Count == 0) return 0;
        int mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2.0;
    }

    /// <summary>
    /// Answers without tools: each model that answered at least 20% of its scored answers without a tool call and, with two or more
    /// models, at least twice the other models' median share, contrasted with the most any other model did; one shared sentence when
    /// every model is at or above 20%. A model on its own needs the 20% only. Null when no model qualifies.
    /// </summary>
    private static RcaFinding? RcaNoToolFinding(Context ctx, RootCauseResult result)
    {
        var models = result.Evidence.Where(e => e.ScoredAnswers > 0).ToList();
        if (models.Count == 0) return null;
        static double ShareOf(RootCauseModelEvidenceSummary e) => e.NoToolAnswers / (double)e.ScoredAnswers;
        static bool Qualifies(double share) => share >= RcaNoToolFindingShare - 1e-9;
        string Answered(RootCauseModelEvidenceSummary e)
            => RcaTokenName(ctx, e.Token) + " answered " + Inv(e.NoToolAnswers) + " of " + Inv(e.ScoredAnswers) + " scored answers ("
               + RootCauseHypotheses.Pct(ShareOf(e)) + ")";
        var terms = new[] { RootCauseGlossary.NoToolAnswers };

        if (models.Count == 1)
        {
            return Qualifies(ShareOf(models[0])) ? new RcaFinding(Answered(models[0]) + " without any tool call.", terms) : null;
        }
        if (models.All(e => Qualifies(ShareOf(e))))
        {
            double least = Math.Floor(models.Min(ShareOf) * 100 + 1e-9);
            return new RcaFinding("Every model answered at least " + BenchmarkReportFormat.Percent(least) + " of its scored answers without any tool call.", terms);
        }

        var outliers = models
            .Where(e => Qualifies(ShareOf(e))
                        && ShareOf(e) >= RcaNoToolContrast * RcaMedian(models.Where(o => !ReferenceEquals(o, e)).Select(ShareOf)) - 1e-9)
            .ToList();
        if (outliers.Count == 0) return null;

        int most = models.Except(outliers).Max(e => e.NoToolAnswers);
        string others = most == 0 ? "no other model did so"
            : most == 1 ? "no other model did so more than once"
            : "no other model did so more than " + Inv(most) + " times";
        return new RcaFinding(BenchmarkReportFormat.LetterList(outliers.Select(Answered).ToList()) + " without any tool call; " + others + ".", terms);
    }

    /// <summary>
    /// A weakness every model shares: the dimension lowest for every model with four dimensions, or for at least two thirds of them
    /// where the response-style rule marks at least two thirds of those (<see cref="RcaConciseStyle"/>), with the response style named
    /// when the rule marks them; null otherwise.
    /// </summary>
    private static RcaFinding? RcaSharedWeaknessFinding(Context ctx, RootCauseResult result)
    {
        if (RcaLowestDimension(result.Profiles) is not { } lowest) return null;
        bool style = lowest.Marked * 3 >= lowest.Count * 2 && RcaConciseStyle(ctx);
        if (lowest.Count != lowest.Of && !(lowest.Count * 3 >= lowest.Of * 2 && style)) return null;

        string text = BenchmarkReportFactLabels.Dimensions[lowest.Dimension].Name + " is the weakest dimension of " + Inv(lowest.Count) + " of "
            + Inv(lowest.Of) + " models";
        if (lowest.Gaps is string gaps) text += ", " + gaps + " points below Accuracy";
        if (style) text += ", under the production chat's concise response style";
        var terms = style ? new[] { RootCauseGlossary.Dimension, RootCauseGlossary.ResponseStyle } : new[] { RootCauseGlossary.Dimension };
        return new RcaFinding(text + "; a pattern this wide does not tell the models apart.", terms);
    }

    /// <summary>
    /// The sheet's response-style facts allow the concise style: it records none, or at least one is true. Under the detailed style
    /// every one is false.
    /// </summary>
    private static bool RcaConciseStyle(Context ctx)
    {
        const string key = "style.responseStyleConflict";
        var facts = ctx.Sheet.Facts.Where(f => f.Key == key || f.Key.EndsWith("." + key, StringComparison.Ordinal)).ToList();
        return facts.Count == 0 || facts.Any(f => f.Available && BenchmarkReportPackPrompt.IsTrue(f));
    }

    /// <summary>The value-for-money bullet and the cheapest model it is about.</summary>
    private sealed record RcaValue(RcaFinding Finding, RcaModel Cheapest);

    /// <summary>
    /// Value for money, with three or more priced models and one uniquely cheapest: the other priced models split by
    /// <see cref="RcaQualityVerdict"/> against it into those established higher, those established lower and the rest, by count;
    /// no cost multiple is printed, since the price cards give them. <c>Vega Pro costs the least per question. Only Orion Max is
    /// established to score higher, and Lyra Mini scores lower; for the other 6 models the order is not established.</c> Null unless
    /// a model is established higher or one of the rest costs at least <see cref="RcaValueCostRatio"/> times as much.
    /// </summary>
    private static RcaValue? RcaValueFinding(Context ctx)
    {
        var priced = RcaModels(ctx).Where(m => m.Cost.HasValue).ToList();
        if (priced.Count < 3) return null;
        double least = priced.Min(m => m.Cost!.Value);
        if (least <= 0 || priced.Count(m => m.Cost == least) > 1) return null;
        var cheapest = priced.First(m => m.Cost == least);

        var higher = new List<RcaModel>();
        var lower = new List<RcaModel>();
        var rest = new List<RcaModel>();
        foreach (var model in priced.Where(m => !ReferenceEquals(m, cheapest)).OrderBy(m => m.Cost!.Value))
        {
            switch (RcaQualityVerdict(ctx, model, cheapest))
            {
                case RcaQuality.Higher: higher.Add(model); break;
                case RcaQuality.Lower: lower.Add(model); break;
                default: rest.Add(model); break;
            }
        }
        if (higher.Count == 0 && !rest.Any(m => m.Cost!.Value >= RcaValueCostRatio * least)) return null;

        string Name(RcaModel m) => RcaTokenName(ctx, m.Token);
        string Names(List<RcaModel> models) => BenchmarkReportFormat.LetterList(models.Select(Name).ToList());

        string order = higher.Count == 0
            ? "No model is established to score higher"
            : (rest.Count + lower.Count > 0 ? "Only " : string.Empty) + Names(higher) + (higher.Count == 1 ? " is" : " are") + " established to score higher";
        if (lower.Count > 0) order += ", and " + Names(lower) + (lower.Count == 1 ? " scores" : " score") + " lower";
        if (rest.Count > 0)
        {
            order += rest.Count == 1
                ? "; for the other model, " + Name(rest[0]) + ", the order is not established"
                : "; for the other " + Inv(rest.Count) + " models the order is not established";
        }

        string text = Name(cheapest) + " costs the least per question. " + order + ".";
        return new RcaValue(new RcaFinding(text, Array.Empty<string>()), cheapest);
    }

    /// <summary>
    /// Thinking levels: one model id at two thinking levels (<see cref="BenchmarkNotableFindings.SameModel(string, string, string, string)"/>,
    /// ordered by <see cref="BenchmarkNotableFindings.CompareLevels"/>) where the higher level is not established above the lower and
    /// takes or costs at least <see cref="RcaThinkingRatio"/> times as much; at most <see cref="RcaMaxPerCandidate"/> pairs, the largest
    /// time ratio first, as clauses of one bullet joined by <c>;</c>, with the letters of each pair's lower and higher level. A ratio
    /// is the pair's paired time or cost test where one was made (<see cref="RcaPairedRatio"/>), else the ratio of the median times
    /// or of the mean costs. The bullet is null when no pair qualifies.
    /// </summary>
    private static (RcaFinding? Finding, List<(string? Low, string? High)> Pairs) RcaThinkingLevelFindings(Context ctx)
    {
        var models = RcaModels(ctx);
        var pairs = new List<(RcaModel Low, RcaModel High, double? Time, double? Cost)>();
        foreach (var low in models)
        {
            foreach (var high in models.Where(m => !ReferenceEquals(m, low)))
            {
                if (!BenchmarkNotableFindings.SameModel(low.Provider, low.ModelId, high.Provider, high.ModelId)) continue;
                if (BenchmarkNotableFindings.CompareLevels(low.ThinkingLevel, high.ThinkingLevel) is not < 0) continue;
                if (RcaQualityVerdict(ctx, high, low) == RcaQuality.Higher) continue;
                double? time = high.Time is double ht && low.Time is double lt && lt > 0 ? ht / lt : null;
                double? cost = high.Cost is double hc && low.Cost is double lc && lc > 0 ? hc / lc : null;
                if (high.Time.HasValue && low.Time.HasValue && RcaPairedRatio(ctx, BenchmarkPairedTests.SpeedMeasure, high.Letter, low.Letter) is double pairedTime)
                {
                    time = pairedTime;
                }
                if (high.Cost.HasValue && low.Cost.HasValue && RcaPairedRatio(ctx, BenchmarkPairedTests.CostMeasure, high.Letter, low.Letter) is double pairedCost)
                {
                    cost = pairedCost;
                }
                if (!(time >= RcaThinkingRatio) && !(cost >= RcaThinkingRatio)) continue;
                pairs.Add((low, high, time, cost));
            }
        }

        var clauses = new List<string>();
        var letters = new List<(string?, string?)>();
        foreach (var (low, high, time, cost) in pairs.OrderByDescending(p => p.Time ?? 0).ThenByDescending(p => p.Cost ?? 0).Take(RcaMaxPerCandidate))
        {
            string scores = high.Entry.QualityIndex is double hq && low.Entry.QualityIndex is double lq
                ? " (" + BenchmarkReportFormat.Whole(hq) + " against " + BenchmarkReportFormat.Whole(lq) + ")"
                : string.Empty;
            var more = new List<string>();
            if (time >= RcaThinkingRatio) more.Add("takes " + RcaTimes(time!.Value) + " times as long");
            if (cost >= RcaThinkingRatio) more.Add("costs " + RcaTimes(cost!.Value) + " times as much");
            clauses.Add(RcaTokenName(ctx, high.Token) + " is not established to score higher than " + RcaTokenName(ctx, low.Token)
                + ", the same model at a lower thinking level" + scores + ", but " + string.Join(" and ", more));
            letters.Add((low.Letter, high.Letter));
        }
        return (clauses.Count == 0 ? null : new RcaFinding(string.Join("; ", clauses) + ".", Array.Empty<string>()), letters);
    }

    /// <summary>
    /// The ratio of <paramref name="first"/> to <paramref name="second"/> in the pair's paired test of <paramref name="measure"/>,
    /// in the all-pairs family first, then in the reference family; null when neither tested the pair or a letter is missing.
    /// </summary>
    private static double? RcaPairedRatio(Context ctx, string measure, string? first, string? second)
    {
        if (first == null || second == null) return null;
        var families = ctx.Sheet.PairedTests ?? new List<BenchmarkReportPairedFamily>();
        foreach (var family in families.Where(f => f.Mode == "AllPairs").Concat(families.Where(f => f.Mode != "AllPairs")))
        {
            var tests = family.Measures.FirstOrDefault(m => m.Measure == measure);
            if (tests == null || tests.NotTestedReason != null) continue;
            var pair = tests.Pairs.FirstOrDefault(p => (p.FirstLetter == first && p.SecondLetter == second) || (p.FirstLetter == second && p.SecondLetter == first));
            if (pair is not { NotTestedReason: null, Effect: double effect } || effect <= 0) continue;
            return pair.FirstLetter == first ? effect : 1.0 / effect;
        }
        return null;
    }

    /// <summary>
    /// Token volume, with three or more models with the figure: each model whose input tokens per question are at least
    /// <see cref="RcaVolumeContrast"/> times the other models' median, with its model calls per answer against theirs, or, where
    /// only its output tokens (reasoning included) are, those; at most <see cref="RcaMaxVolumeModels"/>, the largest multiple
    /// first, in one bullet. <c>Vega Pro read 231,442 input tokens per question, 2.4 times the other models' median, at 5.6 model
    /// calls per answer against their median of 3.4.</c> Null when no model qualifies.
    /// </summary>
    private static RcaFinding? RcaVolumeFinding(Context ctx, RootCauseResult result)
    {
        var models = RcaModels(ctx);
        var input = models.ToDictionary(m => m.Token, m => RcaModelNumber(ctx, m, "tokens.inputPerQuestion"), StringComparer.Ordinal);
        var output = models.ToDictionary(m => m.Token, m => RcaModelNumber(ctx, m, "tokens.outputPerQuestion"), StringComparer.Ordinal);
        var calls = result.Evidence.GroupBy(e => e.Token, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().CallsPerAnswer, StringComparer.Ordinal);

        double? Contrast(RcaModel model, IReadOnlyDictionary<string, double?> values)
        {
            if (values[model.Token] is not double own) return null;
            var others = models.Where(m => !ReferenceEquals(m, model)).Select(m => values[m.Token]).OfType<double>().ToList();
            if (others.Count < 2) return null;
            double median = RcaMedian(others);
            return median > 0 && own >= RcaVolumeContrast * median - 1e-9 ? own / median : null;
        }

        var found = new List<(double Ratio, string Text)>();
        foreach (var model in models)
        {
            string name = RcaTokenName(ctx, model.Token);
            if (Contrast(model, input) is double inputRatio)
            {
                string text = name + " read " + BenchmarkReportFormat.Count(input[model.Token]!.Value) + " input tokens per question, " + RcaTimes(inputRatio)
                    + " times the other models' median";
                var otherCalls = models.Where(m => !ReferenceEquals(m, model)).Select(m => calls.GetValueOrDefault(m.Token)).OfType<double>().ToList();
                if (calls.GetValueOrDefault(model.Token) is double own && otherCalls.Count > 0)
                {
                    text += ", at " + BenchmarkReportFormat.OneDecimal(own) + " model calls per answer against their median of " + BenchmarkReportFormat.OneDecimal(RcaMedian(otherCalls));
                }
                found.Add((inputRatio, text));
            }
            else if (Contrast(model, output) is double outputRatio)
            {
                found.Add((outputRatio, name + " wrote " + BenchmarkReportFormat.Count(output[model.Token]!.Value) + " output tokens per question, reasoning included, "
                    + RcaTimes(outputRatio) + " times the other models' median"));
            }
        }
        if (found.Count == 0) return null;
        var listed = found.OrderByDescending(f => f.Ratio).Take(RcaMaxVolumeModels).Select(f => f.Text).ToList();
        return new RcaFinding(string.Join("; ", listed) + ".", new[] { RootCauseGlossary.Tokens });
    }

    /// <summary>A model's numeric fact: <c>model.X.&lt;key&gt;</c> in comparison scope, the bare key for the subject, <c>peer.X.&lt;key&gt;</c> for a peer.</summary>
    private static double? RcaModelNumber(Context ctx, RcaModel model, string key)
        => NumberOf(Fact(ctx, ctx.Comparison && model.Letter != null
            ? ModelKey(model.Letter, key)
            : model.Letter == null ? key : BenchmarkReportFacts.PeerPrefix(model.Letter) + key));

    /// <summary>
    /// The sheet's notable findings of a kind <see cref="RcaNotableKind"/> accepts, in their stored order, at most <see cref="RcaMaxPerCandidate"/>, as
    /// <see cref="NotableText"/> prints them; a cheaper-and-better finding about <paramref name="cheapest"/>, which value for money
    /// states, and a thinking-level inversion of a pair in <paramref name="levels"/>, which the thinking-level bullet states, left out.
    /// </summary>
    private static List<RcaFinding> RcaNotableFindings(Context ctx, RcaModel? cheapest, IReadOnlyList<(string? Low, string? High)> levels)
    {
        bool Stated(BenchmarkReportNotableFinding finding) => finding.Kind switch
        {
            BenchmarkNotableFindings.CheaperAndBetter => cheapest?.Letter is string letter && finding.Models.Contains(letter, StringComparer.Ordinal),
            BenchmarkNotableFindings.ThinkingLevelInversion => levels.Any(l => l.Low != null && l.High != null && finding.Models.Count == 2
                && finding.Models.Contains(l.Low, StringComparer.Ordinal) && finding.Models.Contains(l.High, StringComparer.Ordinal)),
            _ => false
        };

        return (ctx.Sheet.NotableFindings ?? new List<BenchmarkReportNotableFinding>())
            .OrderBy(f => f.Number)
            .Where(f => RcaNotableKind(f.Kind) && !Stated(f))
            .Take(RcaMaxPerCandidate)
            .Select(f => new RcaFinding(NotableText(ctx, f), Array.Empty<string>()))
            .ToList();
    }

    /// <summary>The reference model's letter: the reference family's, else the first model's; null without a reference family.</summary>
    private static string? RcaReferenceLetter(Context ctx)
    {
        var family = (ctx.Sheet.PairedTests ?? new List<BenchmarkReportPairedFamily>()).FirstOrDefault(f => f.Mode != "AllPairs");
        return family == null ? null : family.ReferenceLetter ?? OrderedPeers(ctx.Sheet).FirstOrDefault()?.Letter ?? "A";
    }

    /// <summary>
    /// The differences from the reference, where one that neither the paired tests nor the 95% intervals establish is at least
    /// <see cref="RcaReferenceGapPoints"/> index points: <c>Against the reference model, Orion Max, the paired tests establish only Vega Pro
    /// higher and Lyra Mini lower; the 95% intervals separate Nova Lite from it; the other 5 differences, up to 5 points, may be chance.</c>
    /// Null otherwise, and without a reference family or an intelligence test in it.
    /// </summary>
    private static RcaFinding? RcaReferenceFinding(Context ctx)
    {
        var family = (ctx.Sheet.PairedTests ?? new List<BenchmarkReportPairedFamily>()).FirstOrDefault(f => f.Mode != "AllPairs");
        var tests = family?.Measures.FirstOrDefault(m => m.Measure == BenchmarkPairedTests.IntelligenceMeasure);
        if (family == null || tests == null || tests.NotTestedReason != null || tests.Pairs.Count == 0) return null;

        string reference = RcaReferenceLetter(ctx)!;
        var entries = RcaModels(ctx).Where(m => m.Letter != null).ToDictionary(m => m.Letter!, m => m.Entry, StringComparer.Ordinal);
        var above = new List<string>();
        var below = new List<string>();
        var separated = new List<string>();
        int open = 0;
        double widest = 0;
        foreach (var pair in tests.Pairs.Select(p => ReferenceSecond(p, reference)).Where(p => p.SecondLetter == reference))
        {
            if (pair.NotTestedReason != null || pair.AdjustedPValue == null) continue;
            switch (RcaTestVerdict(pair, pair.FirstLetter))
            {
                case RcaQuality.Higher: above.Add(NameOfLetter(ctx, pair.FirstLetter)); continue;
                case RcaQuality.Lower: below.Add(NameOfLetter(ctx, pair.FirstLetter)); continue;
            }
            if (entries.TryGetValue(pair.FirstLetter, out var own) && entries.TryGetValue(reference, out var theirs)
                && RcaIntervalVerdict(own, theirs) != RcaQuality.NotEstablished)
            {
                separated.Add(NameOfLetter(ctx, pair.FirstLetter));
                continue;
            }
            open++;
            if (pair.Effect is double effect) widest = Math.Max(widest, Math.Abs(effect));
        }
        if (open == 0 || widest < RcaReferenceGapPoints) return null;

        var established = new List<string>();
        if (above.Count > 0) established.Add(BenchmarkReportFormat.LetterList(above) + " higher");
        if (below.Count > 0) established.Add(BenchmarkReportFormat.LetterList(below) + " lower");
        var terms = new List<string> { RootCauseGlossary.PairedTest, RootCauseGlossary.ReferenceModel };
        string text = "Against the reference model, " + NameOfLetter(ctx, reference) + ", the paired tests establish "
            + (established.Count == 0 ? "no difference" : "only " + string.Join(" and ", established));
        if (separated.Count > 0)
        {
            terms.Add(RootCauseGlossary.Interval95);
            text += "; the 95% intervals separate " + BenchmarkReportFormat.LetterList(separated) + " from it";
        }
        bool other = established.Count + separated.Count > 0;
        text += "; " + (open == 1
            ? (other ? "the other difference" : "the difference") + ", of " + BenchmarkReportFormat.Whole(widest) + " points, may be chance."
            : (other ? "the other " : "the ") + Inv(open) + " differences, up to " + BenchmarkReportFormat.Whole(widest) + " points, may be chance.");
        return new RcaFinding(text, terms);
    }

    /// <summary>The grader-family gap What we found states: the members' provider pair whose contrast's 95% interval excludes zero; null otherwise.</summary>
    private static RootCauseFamilyGap? RcaFamilyGap(RootCauseResult? result)
    {
        var gap = result?.FamilyGaps?.FirstOrDefault(g => g.IsMemberProviderPair && !g.InsufficientData
            && g.Contrast is { Value: not null } contrast && contrast.ExcludesZero());
        return gap == null || gap.GapA.Value == null || gap.GapB.Value == null ? null : gap;
    }

    /// <summary>
    /// For What we found: the gap between the candidates of the two panel members' own providers under each member, and their
    /// contrast, when the contrast's 95% interval excludes zero (<see cref="RcaFamilyGap"/>); null otherwise and on a result stored
    /// without the family gaps. The providers are named unless the copy withholds them.
    /// </summary>
    private static string? RcaFamilyGapSentence(Context ctx, RootCauseResult? result)
    {
        if (RcaFamilyGap(result) is not { } gap) return null;

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
    /// One row per model in rank order, the figures only: the Intelligence Index with its interval and rank, the median time and
    /// the cost per question. Why each model scored as it did gives where each lost points and what was flagged.
    /// </summary>
    private static void RcaGlance(StringBuilder sb, Context ctx, RootCauseResult? result)
    {
        UseTerms(ctx, RootCauseGlossary.IntelligenceIndex, RootCauseGlossary.Interval95, RootCauseGlossary.MedianAndMean);
        Line(sb, "| Model | Intelligence | Median time | Cost per question |");
        Line(sb, "|---|---|---|---|");
        foreach (var (entry, peer) in RcaRankedRows(ctx))
        {
            Line(sb, RcaRow(RcaRowName(ctx, peer), RcaIndexCell(ctx, entry), TimeCell(entry), CostCell(entry)));
        }
        Line(sb);
        if (result != null && result.Profiles.Count > 0)
        {
            Line(sb, "Why each model scored as it did gives, for each model, its strengths, where it gained and lost points, "
                + (ctx.Comparison ? "its difference from the reference by dimension, " : string.Empty) + "its flags and the hypotheses about it.");
            Line(sb);
        }
    }

    /// <summary>
    /// The Intelligence Index as At a glance prints it: <c>79 (75–83), rank 5–9 (tied)</c>, without the rank in a document
    /// of one model (<see cref="RcaRanksModels"/>); not available without one.
    /// </summary>
    private static string RcaIndexCell(Context ctx, BenchmarkReportEntryFigures entry)
        => entry.QualityIndex.HasValue
            ? BenchmarkReportFormat.Whole(entry.QualityIndex.Value)
              + (entry.QualityLower.HasValue && entry.QualityUpper.HasValue
                  ? " (" + BenchmarkReportFormat.Whole(entry.QualityLower.Value) + "–" + BenchmarkReportFormat.Whole(entry.QualityUpper.Value) + ")"
                  : string.Empty)
              + (RcaRanksModels(ctx) ? ", rank " + QualityRankCell(ctx, entry) : string.Empty)
            : BenchmarkReportFacts.NotAvailable;

    /// <summary>The document shows two or more models, so it prints their ranks; a rank of a model shown alone ranks nothing here.</summary>
    private static bool RcaRanksModels(Context ctx) => RcaRows(ctx).Count >= 2;

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

        bool reference = false;
        foreach (var (entry, peer) in RcaRankedRows(ctx))
        {
            string token = peer?.Letter ?? RootCauseEngine.SubjectToken;
            var profile = result.Profiles.FirstOrDefault(p => p.Token == token);
            if (profile == null) continue;

            Line(sb, "**" + RcaBlockName(ctx, peer) + "**: " + (entry.QualityIndex.HasValue ? "Intelligence " + RcaIndexCell(ctx, entry) : "Intelligence not available") + ".");
            Line(sb);

            var ranks = profile.Strengths.Where(s => s.Kind == RootCauseProfiles.Strength).ToList();
            Line(sb, "- *Strengths:* " + WithPeriod(SheetText(ctx, ranks.Count == 0 ? "none ranked first or second" : string.Join("; ", ranks.Select(s => s.Text)))));

            var gained = profile.Strengths.FirstOrDefault(s => s.Kind == RootCauseProfiles.GainedPointsOn);
            if (gained != null) Line(sb, "- *Gained points on:* " + WithPeriod(SheetText(ctx, gained.Text)));
            else if (result.Profiles.Count < BenchmarkComparisonModelProfile.MatrixMinModels)
            {
                Line(sb, "- *Gained points on:* Not measured with fewer than " + Inv(BenchmarkComparisonModelProfile.MatrixMinModels) + " models.");
            }

            var lost = new List<string>();
            if (!profile.Weaknesses.Any(w => w.Kind == RootCauseProfiles.LostPointsOn)) lost.Add("no answer below the model's median");
            foreach (var weakness in profile.Weaknesses)
            {
                lost.Add(weakness.Kind == RootCauseProfiles.WeakestDimension ? "weakest dimension " + weakness.Text : weakness.Text);
            }
            Line(sb, "- *Lost points on:* " + WithPeriod(SheetText(ctx, string.Join("; ", lost))));

            if (RcaAgainstReference(ctx, result, token) is string against)
            {
                Line(sb, "- *Against the reference:* " + against);
                reference |= token != result.ReferenceToken;
            }

            Line(sb, "- *Flags:* " + WithPeriod(SheetText(ctx, profile.Flags.Count == 0 ? "none" : string.Join("; ", profile.Flags.Select(f => f.Text)))));

            var hypotheses = RcaModelHypotheses(result, token);
            if (hypotheses.Count > 0)
            {
                UseTerms(ctx, RootCauseGlossary.Hypothesis);
                Line(sb, "- *Hypotheses:* " + string.Join("; ", hypotheses.Select(h => RcaHypothesisName(ctx, h))) + ".");
            }

            var unexpected = (result.Anomalies ?? new List<RootCauseAnomaly>())
                .Where(a => a.Expected == token || a.Other == token)
                .OrderBy(a => a.Number)
                .Select(a => a.Ref)
                .ToList();
            if (unexpected.Count > 0)
            {
                UseTerms(ctx, RootCauseGlossary.UnexpectedResult);
                Line(sb, "- *Unexpected results:* " + string.Join(", ", unexpected) + " (Unexpected results).");
            }
            Line(sb);
        }
        Line(sb, "*" + RcaProfileNote(ctx, result, reference) + "*");
        Line(sb);
    }

    /// <summary>The most hypotheses a model's block under Why each model scored as it did names.</summary>
    private const int RcaMaxModelHypotheses = 3;

    /// <summary>A model's name in its block under Why each model scored as it did: with its role marks in comparison scope.</summary>
    private static string RcaBlockName(Context ctx, BenchmarkReportPeer? peer)
        => peer == null || !ctx.Comparison ? PlainName(ctx, peer) : PlainName(ctx, peer) + RoleMarks(ctx, peer.Letter);

    /// <summary>
    /// The non-null intelligence hypotheses about the model with <paramref name="token"/>, in the result's order, at most
    /// <see cref="RcaMaxModelHypotheses"/>: a pair hypothesis with the model as its worse side, or a within-model one. A
    /// hypothesis about every model (<see cref="RootCauseHypotheses.IsDocumentWide"/>) is about none of them.
    /// </summary>
    private static List<RootCauseHypothesis> RcaModelHypotheses(RootCauseResult result, string token)
        => result.Hypotheses
            .Where(h => h.Axis == RootCauseAxes.Intelligence && h.Kind != "null" && !RootCauseHypotheses.IsDocumentWide(h)
                        && h.Models.Count > 0 && h.Models[0] == token
                        && (h.Kind == "pair" ? h.Models.Count == 2 : h.Kind == "within"))
            .Take(RcaMaxModelHypotheses)
            .ToList();

    /// <summary>
    /// A model's Intelligence Index against the reference's, split by dimension (<see cref="RootCauseResult.QualitySplits"/>),
    /// model minus reference: <c>-4.1 index points: Accuracy -5.0, Completeness +1.2.</c>, the dimensions of at least 0.05
    /// points largest first, the critical-error cap and difficulty weighting named only when they are the largest part;
    /// <c>The reference.</c> for the reference itself. Null in model scope and when the pair has no split.
    /// </summary>
    private static string? RcaAgainstReference(Context ctx, RootCauseResult result, string token)
    {
        if (!ctx.Comparison || result.ReferenceToken is not string reference) return null;
        if (token == reference) return "The reference.";

        var split = result.QualitySplits.FirstOrDefault(s => (s.First == token && s.Second == reference) || (s.First == reference && s.Second == token));
        if (split == null) return null;
        double sign = split.First == token ? 1 : -1;
        var parts = split.Components.Select(c => (c.Key, c.Label, Amount: sign * c.Amount)).ToList();
        var largest = parts.OrderByDescending(p => Math.Abs(p.Amount)).FirstOrDefault();
        var named = parts
            .Where(p => (p.Key != "other" || p.Key == largest.Key) && Math.Abs(p.Amount) >= 0.05 - 1e-9)
            .OrderByDescending(p => Math.Abs(p.Amount))
            .Select(p => (p.Key == "other" ? LowerFirst(p.Label) : p.Label) + " " + BenchmarkReportFormat.SignedOneDecimal(p.Amount))
            .ToList();
        string text = BenchmarkReportFormat.SignedOneDecimal(sign * split.IndexDifference) + " index points";
        return text + (named.Count == 0 ? "." : ": " + string.Join(", ", named) + ".");
    }

    /// <summary>
    /// The note under the profiles: what an entry is, what Gained points on and Lost points on measure, the shared weakest
    /// dimension a Lost points entry leaves out, what Against the reference is where <paramref name="reference"/>, a gloss for
    /// each kind of flag the profiles show at its first use, and two rules behind the flags, each stated once: answers without
    /// tools are flagged above the models' median share, and an isolated miss is set against the other models' median.
    /// </summary>
    private static string RcaProfileNote(Context ctx, RootCauseResult result, bool reference)
    {
        var profiles = result.Profiles;
        UseFlagTerms(ctx, profiles);
        string note = "Each entry is a count, a share or a rank measured over the model's scored answers; none says why.";
        if (profiles.Any(p => p.Strengths.Any(s => s.Kind == RootCauseProfiles.GainedPointsOn)))
        {
            note += " Gained points on: the questions the model answered at least " + BenchmarkReportFormat.Whole(RootCauseProfiles.GainMargin)
                + " points above the other models' median, with the index points those margins add up to over its questions.";
        }
        note += " Lost points on: the largest share of the model's index points at risk" + Gloss(ctx, RootCauseGlossary.PointsAtRisk)
            + ", with its questions (Appendix A).";
        if (!string.IsNullOrWhiteSpace(result.SharedLowestDimension))
        {
            note += " " + result.SharedLowestDimension + " is the weakest dimension of every model (What we found), so a Lost points entry that would say 'mostly "
                + result.SharedLowestDimension + "' leaves it out.";
        }
        if (reference)
        {
            note += " Against the reference: the model's Intelligence Index minus the reference's, split by dimension (Appendix A).";
        }

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
    // 2a. Unexpected results
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The unexpected results of a comparison-scope analysis (<see cref="RootCauseResult.Anomalies"/>), each a block: its summary in
    /// bold, then the configuration, the graders, the dimensions, the questions or the time component, the hypotheses and what
    /// settles it; a line on the pairs without a paired test; without one, which priors were checked, or why none could be. Then
    /// the writer's optional slot. Nothing in model scope and on a result stored without the anomalies.
    /// </summary>
    private static void RcaUnexpectedResults(StringBuilder sb, Context ctx, RootCauseResult? result)
    {
        if (!ctx.Comparison || result == null || result.Anomalies is not { } anomalies) return;

        Heading(sb, "## Unexpected results");
        string Name(string token) => RcaTokenName(ctx, token);
        if (anomalies.Count == 0)
        {
            var priors = result.PriorsChecked ?? new List<string>();
            Line(sb, priors.Count > 0
                ? "No unexpected results: every ordering the priors predict either holds or is within noise. Checked: "
                  + RootCauseAnomalies.PriorsCheckedText(priors) + "."
                : "No unexpected results could be looked for: " + LowerFirst(RootCauseAnomalies.NoPriorReason(result.Models)));
            Line(sb);
            OptionalSlot(sb, ctx, BenchmarkReportSlots.UnexpectedResults);
            return;
        }

        UseTerms(ctx, RootCauseGlossary.UnexpectedResult);
        foreach (var anomaly in anomalies.OrderBy(a => a.Number))
        {
            string pair = Name(anomaly.Expected) + " against " + Name(anomaly.Other);
            Line(sb, "**" + anomaly.Ref + ". " + RootCauseAnomalies.Summary(anomaly, Name) + "**");
            Line(sb);
            if (anomaly.Confounds.Count > 0) UseTerms(ctx, RootCauseGlossary.Confound);
            Line(sb, "- *Configuration:* " + WithPeriod(RootCauseAnomalies.ConfoundsText(anomaly)));
            if (RootCauseAnomalies.GradersText(anomaly, Name) is string graders)
            {
                UseTerms(ctx, RootCauseGlossary.PanelMember);
                if (anomaly.Graders?.ExpectedReader != null || anomaly.Graders?.OtherReader != null) UseTerms(ctx, RootCauseGlossary.ReferenceReader);
                Line(sb, "- *Graders:* " + WithPeriod(graders));
            }
            if (anomaly.Dimensions.Count > 0)
            {
                UseTerms(ctx, RootCauseGlossary.Dimension);
                Line(sb, "- *Dimensions:* " + pair + ": " + string.Join(", ", anomaly.Dimensions.Select(d =>
                    d.Name + " " + BenchmarkReportFormat.Whole(d.Expected) + " against " + BenchmarkReportFormat.Whole(d.Other))) + ".");
            }
            if (anomaly.Questions.Count > 0)
            {
                Line(sb, "- *Where:* " + pair + ", with the other models' median: " + string.Join("; ", anomaly.Questions.Select(q =>
                    q.Reference + " " + BenchmarkReportFormat.Whole(q.Expected) + " against " + BenchmarkReportFormat.Whole(q.Other)
                    + (q.OthersMedian is double median ? " (" + BenchmarkReportFormat.Whole(median) + ")" : string.Empty))) + ".");
            }
            if (anomaly.SpeedComponent is { } component)
            {
                UseTerms(ctx, RootCauseGlossary.DecompositionShare);
                Line(sb, "- *Time:* " + Capitalized(component.Label) + ", " + RootCauseHypotheses.SharePct(component) + " of the time difference (Appendix B).");
            }
            if (anomaly.Hypotheses.Count > 0)
            {
                UseTerms(ctx, RootCauseGlossary.Hypothesis);
                Line(sb, "- *Hypotheses:* " + string.Join(", ", anomaly.Hypotheses) + ".");
            }
            if (anomaly.SettledByRun is int run) Line(sb, "- *Settled by:* Run " + Inv(run) + " (What to run next).");
            else if (anomaly.SettledByCheck is int check) Line(sb, "- *Settled by:* Check " + Inv(check) + " (What to run next).");
            Line(sb);
        }

        int untested = RootCauseAnomalies.UntestedPairs(anomalies);
        if (untested > 0)
        {
            UseTerms(ctx, RootCauseGlossary.PairedTest, RootCauseGlossary.Interval95);
            Line(sb, Inv(untested) + " of these pairs had no paired test: the paired tests cover the reference pairs, and every pair only up to "
                + Inv(BenchmarkComparisonReportFacts.AllPairsMaxEntries) + " models (Appendix A). Established here means the two 95% intervals do not overlap.");
            Line(sb);
        }
        OptionalSlot(sb, ctx, BenchmarkReportSlots.UnexpectedResults);
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
    /// name is never broken mid-word; a document of one model has no Rank column and no tied-rank note. Speed and cost
    /// are under their own evidence.
    /// </summary>
    private static void RcaScoreboard(StringBuilder sb, Context ctx)
    {
        bool ranked = RcaRanksModels(ctx);
        Line(sb, "| Model | " + Label("quality.index") + " | " + Label("quality.interval") + (ranked ? " | Rank | " : " | ") + Label("errors.critical") + " |");
        Line(sb, ranked ? "|---|---|---|---|---|" : "|---|---|---|---|");
        foreach (var (entry, peer) in RcaRows(ctx))
        {
            string index = entry.QualityIndex.HasValue ? BenchmarkReportFormat.Whole(entry.QualityIndex.Value) : BenchmarkReportFacts.NotAvailable;
            string interval = entry.QualityLower.HasValue && entry.QualityUpper.HasValue
                ? BenchmarkReportFormat.Whole(entry.QualityLower.Value) + "–" + BenchmarkReportFormat.Whole(entry.QualityUpper.Value)
                : BenchmarkReportFacts.NotAvailable;
            Line(sb, ranked
                ? RcaRow(RcaRowName(ctx, peer), index, interval, QualityRankCell(ctx, entry), CriticalErrorsCell(ctx, entry, peer))
                : RcaRow(RcaRowName(ctx, peer), index, interval, CriticalErrorsCell(ctx, entry, peer)));
        }
        Line(sb);
        UseTerms(ctx, RootCauseGlossary.IntelligenceIndex, RootCauseGlossary.CriticalError);
        if (!ranked) return;
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
    /// models counted, the models with all four dimensions, the dimension's index, its gaps below Accuracy (null for
    /// Accuracy itself) and the models the rule marks; null with fewer than two such models, or when no dimension is lowest
    /// for two of them.
    /// </summary>
    private static (string Text, int Count, int Of, int Dimension, string? Gaps, int Marked)? RcaLowestDimension(IReadOnlyList<RootCauseModelProfile> profiles)
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
        if (lowest.Key == 0) return (text + ".", count, complete.Count, lowest.Key, null, 0);

        double low = lowest.Min(x => x.Gap), high = lowest.Max(x => x.Gap);
        string gaps = BenchmarkReportFormat.OneDecimal(low) == BenchmarkReportFormat.OneDecimal(high)
            ? BenchmarkReportFormat.OneDecimal(low)
            : BenchmarkReportFormat.OneDecimal(low) + " to " + BenchmarkReportFormat.OneDecimal(high);
        text += " (" + gaps + " points below Accuracy)";

        int marked = lowest.Count(x => BenchmarkChatTransfer.HasResponseStyleConflict(false, x.Dimensions[0], x.Dimensions[1], x.Dimensions[2], x.Dimensions[3]));
        if (lowest.Key == 1 && marked < count)
        {
            text += ", and at least " + BenchmarkReportFormat.CompactDecimal(BenchmarkChatTransfer.LevelStepThreshold) + " points below Accuracy for "
                    + (marked == 0 ? "none" : Inv(marked)) + " of them";
        }
        return (text + ".", count, complete.Count, lowest.Key, gaps, marked);
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
        else if (measure == BenchmarkPairedTests.SpeedMeasure)
        {
            Line(sb, "| Pair | Time ratio (95% interval) | Result |");
            Line(sb, "|---|---|---|");
            foreach (var pair in pairs)
            {
                Line(sb, RcaRow(Cell(PairName(ctx, pair)), RatioCell(pair), Cell(ResultText(ctx, pair, "was faster"))));
            }
        }
        else
        {
            Line(sb, "| Pair | Spend ratio (95% interval) | At equal prices | Result |");
            Line(sb, "|---|---|---|---|");
            foreach (var pair in pairs)
            {
                string result = pair.NotTestedReason != null || pair.AdjustedPValue == null ? "not tested" : pair.Established ? string.Empty : "may be chance";
                Line(sb, RcaRow(Cell(PairName(ctx, pair)), RatioCell(pair), RcaVolumeRatioCell(ctx, pair.FirstLetter, pair.SecondLetter), result));
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
        if (measure == BenchmarkPairedTests.CostMeasure)
        {
            explain += " Spend ratios include the price cards. At equal prices is the token-volume ratio, the part the price cards do not set.";
        }
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

    /// <summary>
    /// The cost ratio at equal prices of <paramref name="first"/> to <paramref name="second"/>
    /// (<see cref="RootCausePairDecomposition.VolumeRatio"/>), turned when the decomposition has the pair the other way round, to two
    /// decimals as the spend ratio is printed; <see cref="NoValue"/> when the pair was not decomposed.
    /// </summary>
    private static string RcaVolumeRatioCell(Context ctx, string first, string second)
    {
        var pairs = ctx.Sheet.RootCause?.CostPairs ?? new List<RootCausePairDecomposition>();
        var pair = pairs.FirstOrDefault(p => (p.First == first && p.Second == second) || (p.First == second && p.Second == first));
        if (pair?.VolumeRatio is not double ratio || ratio <= 0) return NoValue;
        double oriented = pair.First == first ? ratio : 1.0 / ratio;
        return Math.Round(oriented, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture);
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

    /// <summary>
    /// The note under the time table: how the seconds are split, each part named, what the wait before output sums,
    /// the wait before output, output generation and time to first token glossed at their first use.
    /// </summary>
    private static string RcaTimeNote(Context ctx)
    {
        UseTerms(ctx, RootCauseGlossary.MedianAndMean);
        return "Wall time per question: the median time per question, tool execution included, split into its parts in proportion to their mean times. "
            + "The parts are the wait before output" + Gloss(ctx, RootCauseGlossary.WaitBeforeOutput) + ", output generation" + Gloss(ctx, RootCauseGlossary.OutputGeneration)
            + ", tool execution, the model's own waits and retries, and other time. "
            + "The wait before output is summed over every model call of the answer, hidden thinking included; the run report's time to first token"
            + Gloss(ctx, RootCauseGlossary.TimeToFirstToken) + " is the first call's alone. "
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
    /// The decomposed pairs, one line each with the leading component in seconds or dollars and its share; for cost, the
    /// pairs the price cards lead in one line, and the others with their leading component that is not a price effect; the
    /// pairs whose difference is not established in one line; each pair that could not be decomposed with its reason.
    /// Appendix B has the full decompositions.
    /// </summary>
    private static void RcaDecompositionSummary(StringBuilder sb, Context ctx, IReadOnlyList<RootCausePairDecomposition> pairs, bool cost)
    {
        if (pairs.Count == 0) return;

        Heading(sb, cost ? "### Cost differences between the models" : "### Time differences between the models");
        var decomposed = pairs.Where(p => p.UnavailableReason == null && p.Difference.HasValue && p.Components.Count > 0).ToList();
        var priceLed = cost ? decomposed.Where(RcaPriceLed).ToList() : new List<RootCausePairDecomposition>();
        bool overExplained = false;
        foreach (var pair in decomposed.Except(priceLed))
        {
            var lead = pair.Components
                .Where(c => c.Key != RootCauseCostKeys.All && c.Share.HasValue && !(cost && c.Effect == RootCauseCostKeys.Price))
                .OrderByDescending(c => c.Share!.Value)
                .FirstOrDefault();
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
        if (decomposed.Count > priceLed.Count) Line(sb);

        if (priceLed.Count > 0)
        {
            Line(sb, "Mostly the price cards: " + string.Join("; ", priceLed.Select(p => RcaPairName(ctx, p.First, p.Second))) + ".");
            Line(sb);
        }

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
                + "component pulls the other way. Appendix B gives each pair's split.*");
            Line(sb);
        }
    }

    private static string RcaAmount(double value, bool cost)
        => cost
            ? (value > 0 ? "+" : value < 0 ? "-" : string.Empty) + BenchmarkReportFormat.Usd(Math.Abs(value))
            : BenchmarkReportFormat.SignedOneDecimal(value / 1000.0) + " s";

    /// <summary>
    /// A cost pair the price cards lead: of its whole-difference effects (token volume, price per token and, in a grouped pair,
    /// caching), the price effect has the largest share; false for a pair stored without them.
    /// </summary>
    private static bool RcaPriceLed(RootCausePairDecomposition pair)
    {
        var lead = pair.Components
            .Where(c => c.Key == RootCauseCostKeys.All && c.Share.HasValue)
            .OrderByDescending(c => c.Share!.Value)
            .FirstOrDefault();
        return lead?.Effect == RootCauseCostKeys.Price;
    }

    // ---------------------------------------------------------------------------------------------
    // 7. Hypotheses
    // ---------------------------------------------------------------------------------------------

    private static void RcaHypotheses(StringBuilder sb, Context ctx, RootCauseResult? result)
    {
        Heading(sb, "## Hypotheses");

        bool legend = false;
        foreach (string axis in RootCauseAxes.All)
        {
            Heading(sb, "### " + RootCauseAxes.Name(axis));
            var hypotheses = (result?.Hypotheses ?? new List<RootCauseHypothesis>()).Where(h => h.Axis == axis).ToList();
            var priceEffects = hypotheses.Where(h => h.Kind != "null" && h.PriceEffect).ToList();
            var fired = hypotheses.Where(h => h.Kind != "null" && !h.PriceEffect).ToList();
            if (result == null)
            {
                Line(sb, RcaNoAnalysis);
                Line(sb);
            }
            else if (fired.Count == 0)
            {
                // Where only price effects fired, the lines on them below stand in for the table.
                if (priceEffects.Count == 0)
                {
                    Line(sb, "No hypothesis other than a null fired on this axis.");
                    Line(sb);
                }
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

            if (priceEffects.Count > 0)
            {
                string keys = RootCauseHypotheses.KeyRanges(priceEffects.Select(h => h.Key).ToList());
                Line(sb, priceEffects.Count == 1
                    ? "1 cost difference follows mainly from the price cards (" + keys + "); Appendix C lists it."
                    : Inv(priceEffects.Count) + " cost differences follow mainly from the price cards (" + keys + "); Appendix C lists them.");
                Line(sb);
                if (fired.Count == 0)
                {
                    Line(sb, "No hypothesis other than a null or a price-card difference fired on this axis.");
                    Line(sb);
                }
            }

            if (RcaNullsSentence(ctx, hypotheses.Where(h => h.Kind == "null").ToList()) is string nulls)
            {
                Line(sb, nulls);
                Line(sb);
            }

            // An axis with nothing to discuss lets the writer leave its slot out (BenchmarkReportPackPrompt.RootCauseQuietAxes).
            string slot = BenchmarkReportPackPrompt.RootCauseHypothesesSlot(axis)!;
            if (result != null && fired.Count == 0) OptionalSlot(sb, ctx, slot);
            else Slot(sb, ctx, slot);
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
    /// The null hypotheses of an axis in one sentence: <c>Every tested difference is real (H0-C.1–H0-C.10).</c>, <c>No tested
    /// difference could be shown to be real; each may be chance.</c>, or <c>3 differences may be chance: Model A vs Model B; …; the
    /// other 7 are real.</c>, naming up to <see cref="RcaNullPairsMax"/> of them and counting the rest for Appendix C; null when the
    /// axis has no null hypothesis.
    /// </summary>
    private static string? RcaNullsSentence(Context ctx, IReadOnlyList<RootCauseHypothesis> nulls)
    {
        if (nulls.Count == 0) return null;
        UseTerms(ctx, RootCauseGlossary.NullHypothesis);

        string Subject(RootCauseHypothesis h) => h.Id == "H0-W" || h.Models.Count < 2
            ? "within " + RcaTokenName(ctx, h.Models.FirstOrDefault() ?? string.Empty) + "'s own questions"
            : RcaPairName(ctx, h.Models[0], h.Models[1]);

        var chance = nulls.Where(h => h.Grade != RootCauseGrades.NotSupported).ToList();
        int real = nulls.Count - chance.Count;
        if (chance.Count == 0) return "Every tested difference is real (" + RootCauseHypotheses.KeyRanges(nulls.Select(h => h.Key).ToList()) + ").";
        if (real == 0) return "No tested difference could be shown to be real; each may be chance.";

        string named = string.Join("; ", chance.Take(RcaNullPairsMax).Select(Subject));
        if (chance.Count > RcaNullPairsMax) named += " and " + Inv(chance.Count - RcaNullPairsMax) + " more (Appendix C)";
        return (chance.Count == 1 ? "1 difference may be chance: " : Inv(chance.Count) + " differences may be chance: ") + named
            + (real == 1 ? "; the other is real." : "; the other " + Inv(real) + " are real.");
    }

    // ---------------------------------------------------------------------------------------------
    // 8. Conclusions
    // ---------------------------------------------------------------------------------------------

    private static void RcaConclusions(StringBuilder sb, Context ctx, RootCauseResult? result)
    {
        UseTerms(ctx, RootCauseGlossary.Hypothesis);
        Heading(sb, "## Conclusions");
        if (result == null || result.Axes.Count == 0)
        {
            Line(sb, RcaNoAnalysis);
            Line(sb);
        }
        else
        {
            // A reason every axis shares is stated once under the table, each Why cell keeping its grade.
            const string because = " because ";
            string shared = result.Axes[0].Reason.Trim();
            int at = shared.IndexOf(because, StringComparison.Ordinal);
            bool once = result.Axes.Count > 1 && at > 0 && result.Axes.All(a => string.Equals(a.Reason.Trim(), shared, StringComparison.Ordinal));

            Line(sb, "| Axis | Status | Best hypothesis | Why |");
            Line(sb, "|---|---|---|---|");
            foreach (var axis in result.Axes)
            {
                UseReasonTerms(ctx, axis.Reason);
                var best = axis.BestHypothesis == null ? null : result.Hypotheses.FirstOrDefault(h => h.Key == axis.BestHypothesis);
                // A price-card difference is no mechanism the runs found; the Why cell says so.
                string name = best is { PriceEffect: true } ? NoValue
                    : best != null ? RcaHypothesisName(ctx, best)
                    : axis.BestHypothesis == null && axis.Reason == RootCauseAxisStatus.AllRuledOutReason ? RcaAllRuledOut
                    : axis.BestHypothesis ?? NoValue;
                string why = once ? shared[..at] : axis.Reason;
                Line(sb, RcaRow(RootCauseAxes.Name(axis.Axis), RootCauseStatuses.Display(axis.Status), Cell(name), Cell(SheetText(ctx, why))));
            }
            Line(sb);
            if (once)
            {
                Line(sb, "Each grade is capped at " + WithPeriod(SheetText(ctx, shared)));
                Line(sb);
            }
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
    /// When to make a run, after its label: <c>Launch it 3 times, each at a different time of day; …; after the system
    /// hypotheses are settled</c>, its first word capitalized; empty for any time.
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
    /// for a grader whose name an anonymized copy withholds (<see cref="RcaGraderNameShown"/>). A run of several launches
    /// ends with their number.
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
        if (run.Launches > 1)
        {
            settings.Add(("Launches", Inv(run.Launches) + ", each at a different time of day"));
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
        Line(sb, "- **B. Speed and cost decompositions:** calls, latency, tokens and the share of each component in each decomposed difference.");
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
            FindingsTable(sb, ctx, onlyDifferingRows: true);
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

    /// <summary>
    /// The configuration keys that differ between the analyzed models, from the facts and the hypotheses' confounds; a
    /// difference a configuration fact of Appendix E states is left out for one line that points to those facts. Nothing
    /// without peers.
    /// </summary>
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

        var keys = new HashSet<string>(facts.Select(f => f.Key), StringComparer.Ordinal);
        var stating = (result == null ? new List<RootCauseKnownFact>() : RcaListedKnownFacts(result))
            .Where(k => k.Kind == RootCauseKnownFacts.KindConfiguration && k.FactKeys.Any(keys.Contains))
            .ToList();
        var stated = new HashSet<string>(stating.SelectMany(k => k.FactKeys), StringComparer.Ordinal);
        foreach (var fact in facts.Where(f => !stated.Contains(f.Key)))
        {
            Line(sb, "- **" + (ctx.Comparison ? ComparisonFactLabel(ctx, fact.Key) : FactLabel(ctx, fact.Key)) + ":** "
                + BenchmarkReportSentenceCase.UpperFirst(Shown(ctx, fact)));
        }
        if (stating.Count > 0)
        {
            string numbers = RcaRefRanges(stating.Select(k => Inv(k.Number)).ToList());
            Line(sb, "- The configuration differences between the analyzed pairs " + (stating.Count == 1 ? "are Appendix E's known fact " : "are Appendix E's known facts ")
                + numbers + ".");
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
        string wrong = profile.WrongInsteadOfAbstaining(compact: false);
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
    /// The speed columns of Appendix B: each component of <see cref="RootCauseDecomposition.SpeedComponents"/> under its header,
    /// in table order.
    /// </summary>
    private static IReadOnlyList<(string Key, string Header)> RcaSpeedColumns() => new[]
    {
        ("callCount", "Model calls"),
        ("perCallLatency", "Time to first token"),
        ("outputTokens", "Output tokens"),
        ("decodeRate", "Decode rate"),
        ("gap", "Other")
    };

    /// <summary>
    /// The decomposed pairs the body lists, one row each: for speed, each component's share of the difference with its 95%
    /// interval; for cost, the shares of the token volume, the price card and caching in the whole difference (caching only
    /// for a grouped pair), and the leading component with its share. A column that is zero or missing in every row is left
    /// out, and the note says so; nothing without a decomposed pair.
    /// </summary>
    private static void RcaPairDecompositions(StringBuilder sb, Context ctx, IReadOnlyList<RootCausePairDecomposition> pairs, string heading, bool cost)
    {
        var available = pairs.Where(p => p.UnavailableReason == null && p.Difference.HasValue && p.Components.Count > 0).ToList();
        if (available.Count == 0) return;

        static string Share(RootCauseComponent? component) => component is { Share: not null } part ? RootCauseHypotheses.SharePct(part) : NoValue;

        var header = new List<string> { "Pair", "Questions", "Difference" };
        header.AddRange(cost ? new[] { "Token volume", "Price card", "Caching", "Leading component" } : RcaSpeedColumns().Select(c => c.Header));
        var rows = new List<IReadOnlyList<string>>();
        foreach (var pair in available)
        {
            var cells = new List<string> { Cell(RcaPairName(ctx, pair.First, pair.Second)), Inv(pair.SharedQuestions), RcaAmount(pair.Difference!.Value, cost) };
            if (cost)
            {
                RootCauseComponent? Whole(string effect) => pair.Components.FirstOrDefault(c => c.Key == RootCauseCostKeys.All && c.Effect == effect);
                cells.Add(Share(Whole(RootCauseCostKeys.Volume)));
                cells.Add(Share(Whole(RootCauseCostKeys.Price)));
                cells.Add(pair.Grouped ? Share(Whole(RootCauseCostKeys.Caching)) : NoValue);
                var lead = pair.Components.Where(c => c.Key != RootCauseCostKeys.All && c.Share.HasValue).OrderByDescending(c => c.Share!.Value).FirstOrDefault();
                cells.Add(lead == null ? NoValue : Cell(Capitalized(lead.Label)) + " (" + RootCauseHypotheses.Pct(lead.Share!.Value) + ")");
            }
            else
            {
                cells.AddRange(RcaSpeedColumns().Select(c => Share(pair.Components.FirstOrDefault(x => x.Key == c.Key))));
            }
            rows.Add(cells);
        }

        Heading(sb, heading);
        var (shown, shownRows, dropped) = DropConstantColumns(header, rows, keepFirst: 3);
        TableHeaderOf(sb, shown);
        foreach (var cells in shownRows) Line(sb, RcaRow(cells));
        Line(sb);
        ExplainedTerms(ctx, RootCauseGlossary.DecompositionShare, RootCauseGlossary.BootstrapInterval);
        UseTerms(ctx, RootCauseGlossary.Interval95);
        string what = cost
            ? " Each share is of the difference, the first model's minus the second's: token volume, the price card and caching split the whole of it, "
              + "caching only for a pair whose two sides bill different components; the leading component is the largest part."
            : " Each share is of the difference, the first model's minus the second's. Other is the time outside the model calls.";
        string droppedSentence = DroppedColumnsSentence(dropped);
        Line(sb, "*" + RcaDecompositionNote + what + (droppedSentence.Length > 0 ? " " + droppedSentence : string.Empty) + "*");
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
            var fired = hypotheses.Where(h => h.Kind != "null").ToList();
            var entries = fired
                .Select((h, i) => (Hypothesis: h, Index: i, Rule: RcaRulePattern(ctx, h)))
                .GroupBy(x => x.Rule == null
                    ? "\u0001" + x.Index.ToString(CultureInfo.InvariantCulture)
                    : x.Hypothesis.Id + "\u0001" + x.Hypothesis.Locus + "\u0001" + x.Rule, StringComparer.Ordinal)
                .OrderBy(g => g.Min(x => x.Index))
                .Select(g => g.Select(x => x.Hypothesis).ToList())
                .ToList();
            // Whether the last line printed is empty, so a block ends in exactly one.
            bool blank = false;
            foreach (var entry in entries)
            {
                foreach (var h in entry)
                {
                    foreach (string reason in h.GradeReasons.Concat(h.Robustness.Select(r => r.Name))) UseReasonTerms(ctx, reason);
                    if (h.Robustness.Count > 0) UseTerms(ctx, RootCauseGlossary.RobustnessCheck);
                    if (h.Confounds.Count > 0) UseTerms(ctx, RootCauseGlossary.Confound);
                }
                if (entry.Count == 1)
                {
                    var h = entry[0];
                    Line(sb, "- **" + RcaHypothesisName(ctx, h) + "** (" + SheetText(ctx, h.ModelsText) + "; locus " + h.Locus + "; " + RootCauseGrades.HypothesisWordWithGrade(h) + ")");
                    foreach (var (label, text) in RcaHypothesisFields(ctx, h)) Line(sb, "  - *" + label + ":* " + text);
                    blank = false;
                    continue;
                }

                // One rule fired for several pairs: what they share once, then a row per pair, then each pair's own evidence.
                var first = entry[0];
                var fields = entry.Select(h => RcaHypothesisFields(ctx, h)).ToList();
                var labels = fields.SelectMany(f => f.Select(x => x.Label)).Distinct(StringComparer.Ordinal).ToList();
                var sharedLabels = labels.Where(label => fields.All(f => f.Any(x => x.Label == label))
                    && fields.Select(f => f.First(x => x.Label == label).Text).Distinct(StringComparer.Ordinal).Count() == 1).ToList();
                Line(sb, "- **" + RootCauseHypotheses.KeyRanges(entry.Select(h => h.Key).ToList()) + "** (" + Inv(entry.Count) + " pairs; locus " + first.Locus + "): "
                    + BenchmarkReportSentenceCase.UpperFirst(RcaRulePattern(ctx, first)!));
                foreach (string label in sharedLabels) Line(sb, "  - *" + label + ":* " + fields[0].First(x => x.Label == label).Text);
                Line(sb);
                Line(sb, "| Hypothesis | Pair | Share (95% interval) | Grade | Settled by |");
                Line(sb, "|---|---|---|---|---|");
                foreach (var h in entry)
                {
                    Line(sb, RcaRow(Cell(h.Key), Cell(RcaPairName(ctx, h.Models[0], h.Models[1])), RcaShareText(h), RootCauseGrades.HypothesisWordWithGrade(h), RcaSettledBy(h)));
                }
                Line(sb);
                blank = true;
                for (int i = 0; i < entry.Count; i++)
                {
                    var own = fields[i].Where(x => !sharedLabels.Contains(x.Label)).ToList();
                    if (own.Count == 0) continue;
                    Line(sb, "- **" + entry[i].Key + "**");
                    foreach (var (label, text) in own) Line(sb, "  - *" + label + ":* " + text);
                    blank = false;
                }
                if (!blank)
                {
                    Line(sb);
                    blank = true;
                }
            }

            var nulls = hypotheses.Where(h => h.Kind == "null").ToList();
            if (nulls.Count > 0)
            {
                if (hypotheses.Count > nulls.Count && !blank) Line(sb);
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
            if (!blank || nulls.Count > 0) Line(sb);
        }
    }

    /// <summary>
    /// A pair hypothesis' statement as its rule's pattern, the worse model named <c>the first model</c> and the other <c>the
    /// second model</c>: <c>the first model costs more than the second model mainly from token volume.</c> Null for a hypothesis
    /// that is not about exactly two models, or whose statement does not name both.
    /// </summary>
    private static string? RcaRulePattern(Context ctx, RootCauseHypothesis h)
    {
        if (h.Kind != "pair" || h.Models.Count != 2 || RootCauseHypotheses.IsDocumentWide(h)) return null;
        string statement = SheetText(ctx, h.Statement);
        string pattern = statement;
        foreach (var (token, words) in new[] { (h.Models[0], "the first model"), (h.Models[1], "the second model") })
        {
            string name = RcaTokenName(ctx, token);
            if (name.Length == 0) return null;
            var named = new Regex("(?<![\\p{L}\\p{N}])" + Regex.Escape(name) + "(?![\\p{L}\\p{N}])", RegexOptions.CultureInvariant);
            if (!named.IsMatch(pattern)) return null;
            pattern = named.Replace(pattern, words);
        }
        return LowerFirst(pattern);
    }

    /// <summary>
    /// What Appendix C gives for one hypothesis, each under its label: for, against, the grade reasons its robustness checks do
    /// not already give, refuted if, and its robustness checks and open confounds where it has them.
    /// </summary>
    private static List<(string Label, string Text)> RcaHypothesisFields(Context ctx, RootCauseHypothesis h)
    {
        var fields = new List<(string, string)>
        {
            ("For", h.For.Count == 0 ? "None recorded." : BenchmarkReportSentenceCase.UpperFirst(SheetText(ctx, string.Join(" ", h.For)))),
            ("Against", h.Against.Count == 0 ? "None recorded." : BenchmarkReportSentenceCase.UpperFirst(SheetText(ctx, string.Join(" ", h.Against))))
        };
        var reasons = h.GradeReasons.Where(r => !IsRobustnessReason(h, r)).ToList();
        fields.Add(("Grade reasons", h.GradeReasons.Count == 0 ? "Every condition of the grade is met."
            : reasons.Count == 0 ? "See Robustness."
            : BenchmarkReportSentenceCase.UpperFirst(SheetText(ctx, string.Join("; ", reasons)).TrimEnd('.') + ".")));
        fields.Add(("Refuted if", BenchmarkReportSentenceCase.UpperFirst(SheetText(ctx, h.RefutedIf))));
        if (h.Robustness.Count > 0)
        {
            fields.Add(("Robustness", SheetText(ctx, string.Join("; ", h.Robustness.Select(r => r.Name + " " + RcaCheckText(r.Status)
                + (string.IsNullOrWhiteSpace(r.Detail) ? string.Empty : " (" + r.Detail.TrimEnd('.') + ")")))) + "."));
        }
        if (h.Confounds.Count > 0)
        {
            fields.Add(("Open confounds", OneLine(string.Join(", ", h.Confounds)) + "."));
        }
        return fields;
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
    /// excerpt and the rubric without its <c>SOURCE</c> paragraphs. In comparison scope, each answer's graders in one line,
    /// at most <see cref="RcaMaxDetailedQuestions"/> questions in <see cref="RcaQuestionPriority"/> order, and the others
    /// named in one line.
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
        IReadOnlyList<int> left = Array.Empty<int>();
        if (cited.Count > 0)
        {
            if (ctx.Comparison)
            {
                left = ComparisonQuestionDetails(sb, ctx, grading: true, level: 2, only: cited, title: title, compactGraders: true,
                    order: RcaQuestionPriority(ctx, cited), maxQuestions: RcaMaxDetailedQuestions);
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
        if (left.Count > 0)
        {
            Line(sb, "Also cited, in the question matrix only (Appendix A): " + string.Join(", ", left.Select(n => ctx.QuestionLabel(n))) + ".");
            Line(sb);
        }
    }

    /// <summary>The most questions Appendix G of a comparison-scope analysis gives in detail; the rest are named in one line.</summary>
    private const int RcaMaxDetailedQuestions = 8;

    /// <summary>
    /// The order in which Appendix G details the <paramref name="cited"/> questions: those with a critical error first
    /// (<see cref="RcaHadCriticalError"/>), then those an observation or a writer's hypothesis cites, then the rest; within
    /// each, the widest spread between the models' scores first, then by number.
    /// </summary>
    private static List<int> RcaQuestionPriority(Context ctx, IReadOnlySet<int> cited)
    {
        var writerCited = new HashSet<int>();
        foreach (var item in ctx.Writer.Observations ?? new List<BenchmarkReportWriterItem>())
        {
            writerCited.UnionWith(item.Questions ?? new List<int>());
            foreach (string id in (item.Evidence ?? new List<string>()).Where(id => !string.IsNullOrWhiteSpace(id)))
            {
                if (ComparisonQuestionNumber(ctx, id.Trim()) is int number) writerCited.Add(number);
            }
        }
        foreach (var h in ctx.Writer.WriterHypotheses ?? new List<BenchmarkReportWriterHypothesis>())
        {
            foreach (string id in (h.Evidence ?? new List<string>()).Where(id => !string.IsNullOrWhiteSpace(id)))
            {
                if (ComparisonQuestionNumber(ctx, id.Trim()) is int number) writerCited.Add(number);
            }
        }

        static double Spread(BenchmarkReportQuestion q)
        {
            var scores = (q.Models ?? new List<BenchmarkReportQuestionModelScore>()).Select(c => c.Score).OfType<double>().ToList();
            return scores.Count < 2 ? 0 : scores.Max() - scores.Min();
        }

        return ctx.Sheet.Questions
            .Where(q => cited.Contains(q.Number))
            .GroupBy(q => q.Number)
            .Select(g => g.First())
            .OrderBy(q => RcaHadCriticalError(ctx, q) ? 0 : writerCited.Contains(q.Number) ? 1 : 2)
            .ThenByDescending(Spread)
            .ThenBy(q => q.Number)
            .Select(q => q.Number)
            .ToList();
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
