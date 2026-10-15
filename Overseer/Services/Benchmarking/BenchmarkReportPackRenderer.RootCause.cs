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
/// (the headline, each research question with the writer's answer, the models at a glance and how sure the
/// grades can be); why each model scored as it did; scope and design; the evidence on intelligence, speed and
/// cost; the hypotheses per axis with the writer's own; the conclusions; the runs to make next; limitations and
/// threats to validity. Then the lettered appendices: A intelligence detail, B speed and cost decompositions,
/// C hypothesis details, D run settings, E known facts, F reproducibility, G question details and H glossary;
/// and, in the Markdown copy, the fact sheet. Every table is computed by code from the stored
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

    private const string RcaTimeNote = "Seconds per question: the median of the per-question time, split into its parts in proportion to "
        + "their means. Tool execution is excluded from the model's own time elsewhere in this document.";

    private const string RcaDecompositionNote = "Each difference is split exactly into parts that sum to it (logarithmic-mean Divisia index), "
        + "over the questions both models have figures for; a share's interval is a 95 % bootstrap interval over those questions.";

    private const string RcaGradesNote = "Code fired each hypothesis on a pre-declared rule and graded it: High (quantified, established after "
        + "adjustment, its null rejected, no open confound, every robustness check passed, two or more runs per suite on both sides), "
        + "Moderate (exactly one of those conditions missed), Low (qualitative only, two or more conditions missed, or the null stands) or "
        + "Not supported (refuted by the rule's own test). Each null hypothesis says that the difference is within sampling noise.";

    private const string RcaGradeWordsNote = "Established (High): measured, established after adjustment, robust, with two or more runs per "
        + "suite on both sides. Probable (Moderate): one of those conditions is missed. Suggestive (Low): qualitative only, two or more "
        + "conditions missed, or the difference may be noise. Ruled out (Not supported): the rule's own test refutes it.";

    private const string RcaIdLegend = "Ids: H0 is a null hypothesis, H one between two models, W one within a single model; Q, S and C "
        + "mark intelligence, speed and cost. Appendix C gives each hypothesis' evidence for and against.";

    private const string RcaWriterHypothesesNote = "Hypotheses the report writer added from the grader comments and excerpts. Code has not "
        + "tested them, so their confidence is Low.";

    private const string RcaProfileNote = "Each entry is a count, a share or a rank that was measured over the model's scored answers; none "
        + "says why. Lost points on: the largest share of the model's index points at risk (Appendix A), with its questions.";

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

        RcaSummary(sb, ctx, result);
        RcaWhyEachModel(sb, ctx, result);
        RcaScope(sb, ctx, result);
        RcaIntelligence(sb, ctx, result);
        RcaSpeed(sb, ctx, result);
        RcaCost(sb, ctx, result);
        RcaHypotheses(sb, ctx, result);
        RcaConclusions(sb, ctx, result);
        RcaNextRuns(sb, ctx, result);
        RcaLimitations(sb, ctx, result);

        RcaAppendixList(sb);
        RcaIntelligenceDetail(sb, ctx, result);
        RcaDecompositionDetail(sb, ctx, result);
        RcaHypothesisDetail(sb, ctx, result);
        RcaRunSettings(sb, ctx, result);
        RcaKnownFacts(sb, ctx, result);
        if (ctx.Comparison) ComparisonReproducibility(sb, ctx, "## Appendix F: Reproducibility");
        else Reproducibility(sb, ctx, "## Appendix F: Reproducibility");
        RcaQuestionDetails(sb, ctx, result);
        RcaGlossary(sb);
        RcaFactSheet(sb, ctx);
    }

    // ---------------------------------------------------------------------------------------------
    // 1. Summary
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The headline as the key finding; each research question with its answer strength and the writer's
    /// answer; every model at a glance; and the grade words with the cap the run counts put on them.
    /// </summary>
    private static void RcaSummary(StringBuilder sb, Context ctx, RootCauseResult? result)
    {
        Heading(sb, "## Summary");

        Heading(sb, "### What we found");
        Callout(sb, "IMPORTANT", new[] { "**Key finding.** " + Prose(ctx, ctx.Writer.Headline) });

        var questions = (result?.Questions ?? new List<RootCauseQuestion>()).OrderBy(q => q.Number).ToList();
        if (questions.Count > 0)
        {
            Heading(sb, "### The questions");
            bool answered = BenchmarkReportSlots.RootCauseAnswerSlots.Any(s => ctx.Writer.Sections.ContainsKey(s));
            foreach (var question in questions)
            {
                var lines = new List<string>
                {
                    "**Question.** **" + SheetText(ctx, question.Text) + "**",
                    string.Empty,
                    "*Answer strength: " + RootCauseGrades.WordWithGrade(question.Grade)
                        + (question.HypothesisKeys.Count > 0 ? " — see " + BenchmarkReportFormat.LetterList(question.HypothesisKeys) : string.Empty) + ".*",
                    string.Empty
                };
                lines.Add(RcaAnswer(ctx, question.Number, answered));
                Callout(sb, "NOTE", lines);
            }
        }

        Heading(sb, "### At a glance");
        RcaGlance(sb, ctx, result);

        Heading(sb, "### How sure are we?");
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
        Callout(sb, "WARNING", caveat);
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
            Line(sb, "| " + RcaRowName(ctx, peer) + " | " + index + " | " + TimeCell(entry) + " | " + CostCell(entry)
                + " | " + Cell(SheetText(ctx, lost)) + " | " + Cell(SheetText(ctx, flags)) + " |");
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
            Line(sb, "| " + RcaRowName(ctx, peer)
                + " | " + (entry.QualityIndex.HasValue ? BenchmarkReportFormat.Whole(entry.QualityIndex.Value) : NoValue)
                + " | " + Cell(SheetText(ctx, strengths))
                + " | " + Cell(SheetText(ctx, lost.Count == 0 ? "no answer below the model's median" : string.Join("; ", lost)))
                + " | " + Cell(SheetText(ctx, flags)) + " |");
        }
        Line(sb);
        Line(sb, "*" + RcaProfileNote + "*");
        Line(sb);
    }

    // ---------------------------------------------------------------------------------------------
    // 3. Scope and design
    // ---------------------------------------------------------------------------------------------

    private static void RcaScope(StringBuilder sb, Context ctx, RootCauseResult? result)
    {
        Heading(sb, "## Scope and design");
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
                RcaControls(sb, result);
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
    private static void RcaControls(StringBuilder sb, RootCauseResult? result)
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
            string name = control.ModelLabel + (string.IsNullOrWhiteSpace(control.ThinkingLevel) ? string.Empty : " (" + control.ThinkingLevel + ")");
            string run = control.RunId is long runId ? "run " + Inv(runId)
                : control.BatteryRunId is long batteryRunId ? "battery run " + Inv(batteryRunId)
                : NoValue;
            Line(sb, "| " + Cell(name) + " | " + Cell(control.Provider) + " | " + run + " | "
                + control.DateUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " | " + Inv(control.SharedQuestions) + " |");
        }
        Line(sb);

        Line(sb, "- **Control limit:** " + (result!.ControlLimit == null
            ? "none: two or more controls, at least one from another provider, so the intelligence hypotheses are graded as any other."
            : "the intelligence hypotheses that could lie in the model or in the instrument are capped at " + RootCauseGrades.Display(result.ControlLimit)
              + (string.IsNullOrWhiteSpace(result.ControlLimitReason) ? "." : ": " + OneLine(result.ControlLimitReason).TrimEnd('.') + ".")));
        if (result.ControlDeficitMean is double mean)
        {
            string interval = result.ControlDeficitLower is double lo && result.ControlDeficitUpper is double hi
                ? " (95 % interval " + BenchmarkReportFormat.SignedOneDecimal(lo) + " to " + BenchmarkReportFormat.SignedOneDecimal(hi) + ")"
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
    /// Every model's Intelligence Index, its 95 % interval, its rank and its critical errors: five columns, so a
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
            Line(sb, "| " + RcaRowName(ctx, peer) + " | " + index + " | " + interval + " | " + QualityRankCell(ctx, entry)
                + " | " + CriticalErrorsCell(ctx, entry, peer) + " |");
        }
        Line(sb);
        Line(sb, "*A joint rank means the model's 95 % interval overlaps a neighbor's, so the intervals do not establish the order between them.*");
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

        if (RcaLowestDimensionLine(profiles) is string line)
        {
            Line(sb, line);
            Line(sb);
        }
    }

    /// <summary>
    /// <c>Completeness is the lowest dimension for 8 of 9 models (13.5 to 37.0 points below Accuracy).</c>, for the
    /// dimension lowest for the most models; null with fewer than two models with all four dimensions, or when no
    /// dimension is lowest for two of them.
    /// </summary>
    private static string? RcaLowestDimensionLine(IReadOnlyList<RootCauseModelProfile> profiles)
    {
        var complete = profiles
            .Where(p => p.Dimensions.Count == 4 && p.Dimensions.All(d => d.HasValue))
            .Select(p => p.Dimensions.Select(d => d!.Value).ToArray())
            .ToList();
        if (complete.Count < 2) return null;

        var lowest = complete
            .Where(d => d.Max() - d.Min() > 0.05)
            .Select(d => (Dimension: Array.IndexOf(d, d.Min()), Gap: d[0] - d.Min()))
            .GroupBy(x => x.Dimension)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key)
            .FirstOrDefault();
        if (lowest == null || lowest.Count() < 2) return null;

        string name = BenchmarkReportFactLabels.Dimensions[lowest.Key].Name;
        string text = name + " is the lowest dimension for " + Inv(lowest.Count()) + " of " + Inv(complete.Count) + " models";
        if (lowest.Key == 0) return text + ".";

        double low = lowest.Min(x => x.Gap), high = lowest.Max(x => x.Gap);
        string gaps = BenchmarkReportFormat.OneDecimal(low) == BenchmarkReportFormat.OneDecimal(high)
            ? BenchmarkReportFormat.OneDecimal(low)
            : BenchmarkReportFormat.OneDecimal(low) + " to " + BenchmarkReportFormat.OneDecimal(high);
        return text + " (" + gaps + " points below Accuracy).";
    }

    /// <summary>
    /// The reference family's paired tests of one measure, one row per pair, with the family's adjustment
    /// stated once under the table; nothing in a comparison without paired tests but the reason.
    /// </summary>
    private static void RcaReferenceTests(StringBuilder sb, Context ctx, string measure)
    {
        var families = ctx.Sheet.PairedTests ?? new List<BenchmarkReportPairedFamily>();
        var family = families.FirstOrDefault(f => f.Mode != "AllPairs") ?? families.FirstOrDefault();
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
        Heading(sb, family.Mode == "AllPairs" ? "### Paired tests" : "### Paired tests against " + NameOfLetter(ctx, reference));
        if (tests.NotTestedReason != null || tests.Pairs.Count == 0)
        {
            Line(sb, SheetText(ctx, FamilySizeText(ctx, name, tests)));
            Line(sb);
            return;
        }

        if (intelligence)
        {
            Line(sb, "| Pair | Questions | Difference (95 % interval) | Result |");
            Line(sb, "|---|---|---|---|");
            foreach (var pair in tests.Pairs)
            {
                string difference = pair.Effect is double effect
                    ? BenchmarkReportFormat.SignedOneDecimal(effect) + " points"
                      + (pair.Lower is double l && pair.Upper is double u
                          ? " (" + BenchmarkReportFormat.SignedOneDecimal(l) + " to " + BenchmarkReportFormat.SignedOneDecimal(u) + ")"
                          : string.Empty)
                    : NoValue;
                Line(sb, "| " + Cell(PairName(ctx, pair)) + " | " + Inv(pair.PairedItems) + " | " + difference + " | " + Cell(ResultText(ctx, pair, "scored higher")) + " |");
            }
        }
        else
        {
            bool speed = measure == BenchmarkPairedTests.SpeedMeasure;
            Line(sb, "| Pair | " + (speed ? "Time ratio" : "Spend ratio") + " (95 % interval) | Result |");
            Line(sb, "|---|---|---|");
            foreach (var pair in tests.Pairs)
            {
                Line(sb, "| " + Cell(PairName(ctx, pair)) + " | " + RatioCell(pair) + " | " + Cell(ResultText(ctx, pair, speed ? "was faster" : "was cheaper")) + " |");
            }
        }
        Line(sb);

        string explain = intelligence
            ? " Difference: the mean per-question difference, first model minus second, on the questions both answered."
            : " A ratio is the first model's own " + (measure == BenchmarkPairedTests.SpeedMeasure ? "time" : "spend") + " per question divided by the second's, on the questions both answered.";
        Line(sb, "*" + SheetText(ctx, FamilySizeText(ctx, name, tests)) + explain + " Appendix A gives every family.*");
        Line(sb);
        if (intelligence && !string.IsNullOrWhiteSpace(family.SingleRunCaveat))
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

        Heading(sb, "### Paired differences against the peers");
        Line(sb, "| Peer | Paired difference (95 % interval) |");
        Line(sb, "|---|---|");
        foreach (var peer in peers)
        {
            Line(sb, "| " + TableName(ctx, peer) + " | " + PairedCell(ctx, peer) + " |");
        }
        Line(sb);
        Line(sb, "*" + PairedDifferenceNote + "*");
        Line(sb);
    }

    /// <summary>The writer's observations, each with its evidence.</summary>
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
        }
        Line(sb);
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
            RcaSegmentTable(sb, ctx, result.SpeedRows, RootCauseSegments.Time, "Seconds per question", v => BenchmarkReportFormat.OneDecimal(v) + " s");
            Line(sb, "*" + RcaTimeNote + "*");
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
            RcaSegmentTable(sb, ctx, result.CostRows, RootCauseSegments.CostComponents, "Cost per question", BenchmarkReportFormat.Usd);
            Line(sb, "*Mean cost per question by component, priced on this basis: "
                + OneLine(string.IsNullOrWhiteSpace(result.PricingBasis) ? PricingBasisText(ctx.Sheet).Text : result.PricingBasis).TrimEnd('.') + ".*");
            Line(sb);
            Figures(sb, ctx, BenchmarkReportChartAnchor.RcaCost);
            if (ctx.Comparison) RcaReferenceTests(sb, ctx, BenchmarkPairedTests.CostMeasure);
            RcaDecompositionSummary(sb, ctx, result.CostPairs, cost: true);
        }
        Slot(sb, ctx, BenchmarkReportSlots.EvidenceCost);
    }

    /// <summary>
    /// One row per chart row of R2 or R3: each model, then for a subject on its own each difficulty band or
    /// suite, with its total and its segments; an unavailable row says why.
    /// </summary>
    private static void RcaSegmentTable(
        StringBuilder sb, Context ctx, IReadOnlyList<RootCauseChartRow> rows, IReadOnlyList<string> keys, string totalColumn, Func<double, string> format)
    {
        Line(sb, "| Model | " + totalColumn + " | " + string.Join(" | ", keys.Select(k => Capitalized(RootCauseSegments.Label(k)))) + " |");
        Line(sb, "|---|---|" + string.Concat(keys.Select(_ => "---|")));
        foreach (var row in rows)
        {
            string name = row.Token != null ? RcaTokenName(ctx, row.Token) : row.Label;
            if (row.Total is not double total)
            {
                Line(sb, "| " + Cell(name) + " | not recorded (" + Cell(OneLine(row.UnavailableReason ?? "no data").TrimEnd('.')) + ") | "
                    + string.Join(" | ", keys.Select(_ => NoValue)) + " |");
                continue;
            }
            Line(sb, "| " + Cell(name) + " | " + format(total) + " | "
                + string.Join(" | ", keys.Select(k => row.Segments.FirstOrDefault(s => s.Key == k) is { } s ? format(s.Value) : NoValue)) + " |");
        }
        Line(sb);
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
        foreach (var pair in decomposed)
        {
            var lead = pair.Components.Where(c => c.Key != "all" && c.Share.HasValue).OrderByDescending(c => c.Share!.Value).FirstOrDefault();
            string difference = RcaAmount(pair.Difference!.Value, cost) + " per question";
            string leading = lead == null
                ? string.Empty
                : "; leading component " + LowerFirst(lead.Label) + ", " + RcaAmount(lead.Amount, cost) + ", " + RootCauseHypotheses.SharePct(lead) + " of the difference";
            Line(sb, "- **" + RcaPairName(ctx, pair.First, pair.Second) + ":** " + difference + leading + ".");
        }
        if (decomposed.Count > 0) Line(sb);

        var notEstablished = pairs.Where(p => p.NotEstablished).ToList();
        if (notEstablished.Count > 0)
        {
            Line(sb, "Not decomposed, as the difference is not established: "
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
            Line(sb, "*" + RcaDecompositionNote + " The difference is the first model's minus the second's; a share can pass 100 % when another "
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
                foreach (var h in fired)
                {
                    Line(sb, "| " + Cell(RcaHypothesisName(ctx, h)) + " | " + Cell(SheetText(ctx, h.Statement)) + " | " + RootCauseGrades.WordWithGrade(h.Grade)
                        + " | " + RcaShareText(h) + " | " + RcaSettledBy(h) + " |");
                }
                Line(sb);
                if (!legend)
                {
                    Line(sb, "*" + RcaIdLegend + "*");
                    Line(sb);
                    legend = true;
                }
            }

            if (RcaNullsLine(ctx, hypotheses.Where(h => h.Kind == "null").ToList()) is string nulls)
            {
                Line(sb, nulls);
                Line(sb);
            }
            Slot(sb, ctx, slots[axis]);
        }

        Figures(sb, ctx, BenchmarkReportChartAnchor.RcaHypotheses);

        Heading(sb, "### The writer's hypotheses");
        var lines = new List<string> { "**Not tested by code.** " + RcaWriterHypothesesNote, string.Empty };
        var own = ctx.Writer.WriterHypotheses ?? new List<BenchmarkReportWriterHypothesis>();
        if (own.Count == 0)
        {
            lines.Add("The writer added none.");
        }
        foreach (var h in own)
        {
            string models = h.Models.Count == 0 ? string.Empty : BenchmarkReportFormat.LetterList(h.Models.Select(t => RcaTokenName(ctx, t)).ToList()) + "; ";
            lines.Add("- **" + RootCauseAxes.Name(h.Axis) + " · " + RootCauseGrades.Low + "** (" + models + "locus " + OneLine(h.Locus) + "): " + OneLine(Prose(ctx, h.Statement)));
            lines.Add("  - *For:* " + string.Join(" ", h.For.Select(t => OneLine(Prose(ctx, t)))));
            lines.Add("  - *Against:* " + (h.Against.Count == 0 ? "none given." : string.Join(" ", h.Against.Select(t => OneLine(Prose(ctx, t))))));
            lines.Add("  - *Refuted if:* " + OneLine(Prose(ctx, h.RefutedIf)));
        }
        Callout(sb, "WARNING", lines);
    }

    /// <summary><c>HQ2.1: critical errors of Model F against Model E</c>; the key alone on a result stored without labels.</summary>
    private static string RcaHypothesisName(Context ctx, RootCauseHypothesis h)
        => string.IsNullOrWhiteSpace(h.Label) ? h.Key : h.Key + ": " + SheetText(ctx, h.Label);

    private static string RcaShareText(RootCauseHypothesis h)
        => h.Share is double share
            ? RootCauseHypotheses.Pct(share) + (h.ShareLower is double lo && h.ShareUpper is double hi ? " (" + RootCauseHypotheses.Interval(lo, hi) + ")" : string.Empty)
            : NoValue;

    private static string RcaSettledBy(RootCauseHypothesis h)
        => h.NextRunNumber is int run ? "run " + Inv(run) : h.CheckNumber is int check ? "check " + Inv(check) : NoValue;

    /// <summary>
    /// The null hypotheses of an axis in one sentence: the pairs (and models' own questions) whose null stands,
    /// then those where a paired result rejects it; null when the axis has none.
    /// </summary>
    private static string? RcaNullsLine(Context ctx, IReadOnlyList<RootCauseHypothesis> nulls)
    {
        if (nulls.Count == 0) return null;

        string Subject(RootCauseHypothesis h) => h.Kind == "null" && h.Id == "H0-W"
            ? "within " + RcaTokenName(ctx, h.Models.FirstOrDefault() ?? string.Empty) + "'s own questions"
            : h.Models.Count >= 2 ? RcaPairName(ctx, h.Models[0], h.Models[1]) : RcaTokenName(ctx, h.Models.FirstOrDefault() ?? string.Empty);

        var stands = nulls.Where(h => h.Grade != RootCauseGrades.NotSupported).Select(Subject).ToList();
        var rejected = nulls.Where(h => h.Grade == RootCauseGrades.NotSupported).Select(Subject).ToList();
        var parts = new List<string>();
        if (stands.Count > 0) parts.Add("it stands for " + BenchmarkReportFormat.LetterList(stands));
        if (rejected.Count > 0) parts.Add("a paired result rejects it for " + BenchmarkReportFormat.LetterList(rejected));
        return "*Null hypotheses (the difference is within sampling noise):* " + string.Join("; ", parts) + ".";
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
                var best = axis.BestHypothesis == null ? null : result.Hypotheses.FirstOrDefault(h => h.Key == axis.BestHypothesis);
                string name = best != null ? RcaHypothesisName(ctx, best) : axis.BestHypothesis ?? NoValue;
                Line(sb, "| " + RootCauseAxes.Name(axis.Axis) + " | " + RootCauseStatuses.Display(axis.Status) + " | " + Cell(name)
                    + " | " + Cell(SheetText(ctx, axis.Reason)) + " |");
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
            if (analysis.SystemFirst)
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
                var settles = run.HypothesisIds.Select(key => analysis.Hypotheses.FirstOrDefault(h => h.Key == key) is { } h ? RcaHypothesisName(ctx, h) : key).ToList();
                string estimate = RootCauseNextRuns.EstimateText(run);
                Line(sb, "| " + Inv(run.Number)
                    + " | " + Capitalized(RootCauseNextRuns.KindPhrase(run.Kind)) + " (" + run.Mark.ToLowerInvariant() + ")"
                    + " | " + RcaPurposeText(run.Purpose)
                    + " | " + Cell(modelText)
                    + " | " + Cell(settles.Count == 0 ? NoValue : string.Join("; ", settles))
                    + " | " + (estimate.Length == 0 ? "not estimated" : estimate) + " |");
            }
            Line(sb);

            foreach (var run in runs.Where(r => r.When != null || r.AfterSystemHypotheses))
            {
                var when = new List<string>();
                if (run.When != null) when.Add(run.When);
                if (run.AfterSystemHypotheses) when.Add("after the system hypotheses are settled");
                Line(sb, "- **Run " + Inv(run.Number) + ", when:** " + string.Join("; ", when) + ".");
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
    /// Every launcher field of a recommended run, labeled as Run Benchmark labels it, in the launcher's order;
    /// a setting the analyzed runs did not share says so.
    /// </summary>
    private static List<(string Field, string Setting)> RcaLauncherSettings(Context ctx, RootCauseRecommendedRun run)
    {
        const string NotShared = "not shared by the analyzed runs: choose it";
        bool batch = string.Equals(run.RunMode, "Batch", StringComparison.Ordinal);
        bool battery = string.Equals(run.TargetKind, "Battery", StringComparison.Ordinal);
        bool panel = run.CoAssessorModelConfigurationId.HasValue;
        string Config(long? id, string none) => id is long value ? "configuration " + Inv(value) : none;

        var models = run.ModelNames.Select(name => RcaRunModelName(ctx, name)).ToList();
        var settings = new List<(string, string)>
        {
            ("Models", batch ? "Model batch" : "One model"),
            (batch ? "Models Under Test" : "Model Under Test", models.Count == 0 ? "no model resolved" : string.Join(", ", models)),
            ("Run Target", battery ? "Battery" : "Single suite"),
            battery
                ? ("Battery", run.BatteryName ?? (run.BatteryId is long batteryId ? "battery " + Inv(batteryId) : NotShared))
                : ("Benchmark Suite", run.SuiteName ?? (run.SuiteId is long suiteId ? "suite " + Inv(suiteId) : NotShared)),
            ("Scoring Profile", run.ScoringProfileId is long profile ? "profile " + Inv(profile) : NotShared),
            ("Response Style", run.VerboseMode switch { true => "Detailed — diagnostic", false => "Concise — production default", _ => NotShared }),
            ("Source Code References", run.AllowSourceCodeReferences switch
            {
                true => "Allowed — answers cite source files and lines",
                false => "Disallowed — production default",
                _ => NotShared
            }),
            ("Assessor", Config(run.AssessorModelConfigurationId, NotShared)),
            ("Co-Assessor", Config(run.CoAssessorModelConfigurationId, "none")),
            (panel ? "Reference Reader" : "Second Reader", Config(run.SecondOpinionAssessorModelConfigurationId, "none"))
        };
        if (!panel)
        {
            settings.Add(("Coverage", RcaCoverageText(run.SecondOpinionMode)));
        }
        settings.Add(("Claim Verifier", Config(run.ClaimVerifierModelConfigurationId, "none")));
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

    private static string RcaPurposeText(string purpose) => purpose switch
    {
        RootCausePurposes.Chat => "C, the Overseer chat",
        RootCausePurposes.Benchmark => "B, the benchmark",
        _ => "M, model selection"
    };

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
        Line(sb, "- The benchmark asks single-turn questions under one chat configuration: live chat carries the conversation before each question, "
            + "and spoiler-free mode, web search and subagents are off, so a cause found here holds for that configuration only.");
        Line(sb, "- It runs without the wiki text that live chat always gives the model before it answers, so a reading about retrieval or tool routing was measured under a condition live chat does not share.");
        Line(sb, "- The hypotheses come from a fixed rule table over these runs; points at risk are upper bounds, and a decomposition rests on the questions both sides have figures for. The writer's own hypotheses are qualitative.");
        if (ctx.Comparison)
        {
            Line(sb, "- Intervals: " + SharedOrPerModel(ctx, "quality.intervalBasis"));
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
            Line(sb, "- Interval: " + D(ctx, "quality.intervalBasis"));
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

    private static void RcaAppendixList(StringBuilder sb)
    {
        Heading(sb, "## Appendices");
        Line(sb, "- **A. Intelligence detail:** the compared models, configuration differences, failure profiles, grader findings, panel agreement, points at risk, the index difference by dimension, every paired test and the per-question matrix.");
        Line(sb, "- **B. Speed and cost decompositions:** calls, latency, tokens and every component of each decomposed difference.");
        Line(sb, "- **C. Hypothesis details:** each hypothesis' evidence for and against, grade reasons and robustness checks, and the null hypotheses.");
        Line(sb, "- **D. Run settings:** every launcher setting of each run to make next.");
        Line(sb, "- **E. Known facts:** what a test, a count, a decomposition or the configuration proves.");
        Line(sb, "- **F. Reproducibility:** run ids, dates, hashes and the pricing basis.");
        Line(sb, "- **G. Question details:** the questions this document cites, with their answers.");
        Line(sb, "- **H. Glossary:** the statistical terms in plain words.");
        Line(sb);
    }

    /// <summary>Appendix A: every intelligence table the body leaves out.</summary>
    private static void RcaIntelligenceDetail(StringBuilder sb, Context ctx, RootCauseResult? result)
    {
        Heading(sb, "## Appendix A: Intelligence detail");
        if (ctx.Comparison)
        {
            ComparisonModelsTable(sb, ctx, "### Compared models");
            RcaConfiguration(sb, ctx, result);
            Heading(sb, "### Results");
            ResultsTable(sb, ctx);
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
            PairedTestsSection(sb, ctx, "### Paired tests: every family");
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
            Line(sb, "- **" + (ctx.Comparison ? ComparisonFactLabel(ctx, fact.Key) : FactLabel(ctx, fact.Key)) + ":** " + Shown(ctx, fact));
        }
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
            Line(sb, "| " + string.Join(" | ", cells) + " |");
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
            Line(sb, "| " + string.Join(" | ", cells) + " |");
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
            Line(sb, "| " + Cell(string.IsNullOrWhiteSpace(item.Reference) ? ctx.QuestionLabel(item.Number) : item.Reference)
                + " | " + Cell(Topic(ctx, item.Number) ?? NoValue)
                + " | " + BenchmarkReportFormat.Whole(item.SubjectScore)
                + " | " + BenchmarkReportFormat.Whole(item.ControlsMean)
                + " | " + BenchmarkReportFormat.WholeDifference(item.SubjectScore, item.ControlsMean)
                + " | " + Inv(item.ControlCount)
                + " | " + mark + " |");
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
                Line(sb, "| " + Cell(RcaTokenName(ctx, summary.Token)) + " | not recorded (" + Cell(OneLine(summary.TimeUnavailableReason).TrimEnd('.')) + ") | " + NoValue + " | " + NoValue + " | " + NoValue + " | " + NoValue + " |");
                continue;
            }
            Line(sb, "| " + Cell(RcaTokenName(ctx, summary.Token))
                + " | " + (summary.CallsPerAnswer is double calls ? BenchmarkReportFormat.CompactDecimal(calls) : NoValue)
                + " | " + (summary.TtftPerCallP50Ms is double ttft ? BenchmarkReportFormat.Seconds(ttft) : NoValue)
                + " | " + (summary.DecodeTokensPerSecond is double rate ? BenchmarkReportFormat.Whole(rate) + " tokens/s" : NoValue)
                + " | " + (summary.ReasoningShare is double share ? RootCauseHypotheses.Pct(share) : NoValue)
                + " | " + (summary.OutputTokens is double tokens ? BenchmarkReportFormat.Count(tokens) : NoValue)
                + " |");
        }
        Line(sb);

        Heading(sb, "### Tokens per question");
        var keys = RootCauseSegments.CostComponents;
        Line(sb, "| Model | " + string.Join(" | ", keys.Select(k => Capitalized(RootCauseSegments.Label(k)))) + " | Cache-read share |");
        Line(sb, "|---|" + string.Concat(keys.Select(_ => "---|")) + "---|");
        foreach (var summary in result.Evidence)
        {
            var cells = new List<string> { Cell(RcaTokenName(ctx, summary.Token)) };
            cells.AddRange(keys.Select(k => summary.Tokens.FirstOrDefault(s => s.Key == k) is { } s ? BenchmarkReportFormat.Count(s.Value) : NoValue));
            cells.Add(summary.CacheReadShare is double share ? RootCauseHypotheses.Pct(share) : NoValue);
            Line(sb, "| " + string.Join(" | ", cells) + " |");
        }
        Line(sb);

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

    /// <summary>Each decomposed pair's exact decomposition, one row per component; nothing without one.</summary>
    private static void RcaPairDecompositions(StringBuilder sb, Context ctx, IReadOnlyList<RootCausePairDecomposition> pairs, string heading, bool cost)
    {
        var available = pairs.Where(p => p.UnavailableReason == null && p.Difference.HasValue && p.Components.Count > 0).ToList();
        if (available.Count == 0) return;

        Heading(sb, heading);
        Line(sb, cost
            ? "| Pair | Questions | Difference per question | Component | Effect | Part | Share (95 % interval) |"
            : "| Pair | Questions | Difference per question | Component | Part | Share (95 % interval) |");
        Line(sb, cost ? "|---|---|---|---|---|---|---|" : "|---|---|---|---|---|---|");
        foreach (var pair in available)
        {
            string name = Cell(RcaPairName(ctx, pair.First, pair.Second));
            string difference = RcaAmount(pair.Difference!.Value, cost);
            foreach (var component in pair.Components)
            {
                string share = component.Share is double s
                    ? RootCauseHypotheses.Pct(s) + (component.ShareLower is double lo && component.ShareUpper is double hi ? " (" + RootCauseHypotheses.Interval(lo, hi) + ")" : string.Empty)
                    : NoValue;
                Line(sb, "| " + name + " | " + Inv(pair.SharedQuestions) + " | " + difference + " | " + Cell(Capitalized(component.Label))
                    + (cost ? " | " + Cell(component.Effect ?? NoValue) : string.Empty)
                    + " | " + RcaAmount(component.Amount, cost) + " | " + share + " |");
            }
        }
        Line(sb);
        Line(sb, "*" + RcaDecompositionNote + " The difference is the first model's minus the second's.*");
        Line(sb);
    }

    /// <summary>
    /// Appendix C: the grading rule, then per axis each fired hypothesis' evidence, grade reasons and robustness
    /// checks, and the null hypotheses as one compact table.
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

        Line(sb, "*" + RcaGradesNote + "*");
        Line(sb);
        foreach (string axis in RootCauseAxes.All)
        {
            var hypotheses = result.Hypotheses.Where(h => h.Axis == axis).ToList();
            if (hypotheses.Count == 0) continue;

            Heading(sb, "### " + RootCauseAxes.Name(axis));
            foreach (var h in hypotheses.Where(h => h.Kind != "null"))
            {
                Line(sb, "- **" + RcaHypothesisName(ctx, h) + "** (" + SheetText(ctx, h.ModelsText) + "; locus " + h.Locus + "; " + RootCauseGrades.WordWithGrade(h.Grade) + ")");
                Line(sb, "  - *For:* " + (h.For.Count == 0 ? "none recorded." : SheetText(ctx, string.Join(" ", h.For))));
                Line(sb, "  - *Against:* " + (h.Against.Count == 0 ? "none recorded." : SheetText(ctx, string.Join(" ", h.Against))));
                Line(sb, "  - *Grade reasons:* " + (h.GradeReasons.Count == 0 ? "every condition of the grade is met." : SheetText(ctx, string.Join("; ", h.GradeReasons)).TrimEnd('.') + "."));
                Line(sb, "  - *Refuted if:* " + SheetText(ctx, h.RefutedIf));
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
                    Line(sb, "| " + Cell(h.Key) + " | " + Cell(pair) + " | " + Cell(RcaNullResult(ctx, h)) + " | " + RootCauseGrades.WordWithGrade(h.Grade) + " |");
                }
            }
            Line(sb);
        }
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

        Line(sb, "Each run's settings are the launcher's, copied from the analyzed runs unless its kind changes them; a setting the runs did not share is left for you to choose.");
        Line(sb);
        foreach (var run in runs.OrderBy(r => r.Number))
        {
            Heading(sb, "### Run " + Inv(run.Number) + ": " + RootCauseNextRuns.KindName(run.Kind) + " (" + RcaPurposeText(run.Purpose) + ", " + run.Mark.ToLowerInvariant() + ")");
            Line(sb, "- **Decides:** " + SheetText(ctx, run.Decides));
            Line(sb, "- **Predicted comparability:** " + (string.IsNullOrWhiteSpace(run.PredictedTier) ? "not predicted" : OneLine(run.PredictedTier)));
            string estimate = RootCauseNextRuns.EstimateText(run);
            Line(sb, "- **Estimate:** " + (estimate.Length == 0 ? "not estimated" : estimate));
            if (run.HypothesisIds.Count > 0)
            {
                Line(sb, "- **Settles:** " + string.Join(", ", run.HypothesisIds));
            }
            var when = new List<string>();
            if (run.When != null) when.Add(run.When);
            if (run.AfterSystemHypotheses) when.Add("after the system hypotheses are settled");
            if (when.Count > 0)
            {
                Line(sb, "- **When:** " + string.Join("; ", when) + ".");
            }
            Line(sb);

            Line(sb, "| Launcher field | Setting |");
            Line(sb, "|---|---|");
            foreach (var (field, setting) in RcaLauncherSettings(ctx, run))
            {
                Line(sb, "| " + field + " | " + Cell(setting) + " |");
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

    /// <summary>Appendix E: the known facts, numbered as their <c>rca.known.&lt;n&gt;</c> facts.</summary>
    private static void RcaKnownFacts(StringBuilder sb, Context ctx, RootCauseResult? result)
    {
        Heading(sb, "## Appendix E: Known facts");
        if (result == null)
        {
            Line(sb, RcaNoAnalysis);
        }
        else if (result.KnownFacts.Count == 0)
        {
            Line(sb, "No rule found a fact to state.");
        }
        else
        {
            foreach (var known in result.KnownFacts.OrderBy(k => k.Number))
            {
                Line(sb, Inv(known.Number) + ". " + SheetText(ctx, known.Text));
            }
        }
        Line(sb);
    }

    /// <summary>
    /// Appendix G: the details of every question the document cites: in the writer's prose and lists, in the
    /// code's hypotheses, known facts, checks, runs and profiles, and each instrument-suspect or model-specific
    /// control item.
    /// </summary>
    private static void RcaQuestionDetails(StringBuilder sb, Context ctx, RootCauseResult? result)
    {
        const string Title = "Appendix G: Question details";
        var texts = new List<string?>();
        if (result != null)
        {
            foreach (var h in result.Hypotheses)
            {
                texts.Add(h.Statement);
                texts.AddRange(h.For);
                texts.AddRange(h.Against);
                texts.Add(h.RefutedIf);
            }
            texts.AddRange(result.KnownFacts.Select(k => k.Text));
            texts.AddRange(result.Checks.Select(c => c.Text));
            texts.AddRange(result.RecommendedRuns.Select(r => r.Decides));
            texts.AddRange(result.Profiles.SelectMany(p => p.Strengths.Concat(p.Weaknesses).Concat(p.Flags)).Select(e => e.Text));
        }

        var cited = new HashSet<int>(BenchmarkReportPackValidator.CitedQuestions(ctx.Writer, ctx.Sheet));
        cited.UnionWith(BenchmarkReportPackValidator.QuestionsReferencedIn(texts, ctx.Sheet));
        if (result != null)
        {
            cited.UnionWith(result.ControlItems.Where(i => i.InstrumentSuspect || i.ModelSpecific).Select(i => i.Number));
        }

        int length = sb.Length;
        if (cited.Count > 0)
        {
            if (ctx.Comparison)
            {
                ComparisonQuestionDetails(sb, ctx, grading: true, level: 2, only: cited, title: Title);
            }
            else
            {
                var questions = ctx.Sheet.Questions.Where(q => cited.Contains(q.Number)).OrderBy(q => q.Number).ToList();
                QuestionDetails(sb, ctx, questions, grading: ctx.Options.Disclosure == BenchmarkReportDisclosure.Full, level: 2, title: Title);
            }
        }
        if (sb.Length == length)
        {
            Heading(sb, "## " + Title);
            Line(sb, "No question given in detail is cited.");
            Line(sb);
        }
    }

    /// <summary>Appendix H: the statistical terms of the document in plain words.</summary>
    private static void RcaGlossary(StringBuilder sb)
    {
        Heading(sb, "## Appendix H: Glossary");
        Line(sb, "- **95 % interval:** a range built so that, if the questions were sampled again and again, 95 of 100 such ranges would hold the true value. When two models' intervals overlap, the intervals alone do not say which is higher.");
        Line(sb, "- **Paired test:** a comparison of two models question by question, on the questions both answered, so that how hard each question is counts the same for both.");
        Line(sb, "- **Holm adjustment:** testing many pairs at once turns up some differences by chance. The Holm adjustment raises each p-value so that the whole family of tests keeps a 5 % chance of a false finding; a result is established when its adjusted p-value is below 0.05.");
        Line(sb, "- **Null hypothesis:** the statement that a difference is within sampling noise. It stands until a paired test, or without one a bootstrap interval that excludes zero, rejects it; a null that stands is not proof that the two are equal.");
        Line(sb, "- **Grade words:** Established (High) is measured, established after adjustment, robust and replicated; Probable (Moderate) misses one of those conditions; Suggestive (Low) is qualitative, misses two or more, or may be noise; Ruled out (Not supported) is refuted by its own rule's test.");
        Line(sb, "- **Points at risk:** the index points a model lost on its answers below its own median answer, each answer counted once under its first signal (no tool call, a defect, a critical error). An upper bound of what each signal costs, not an estimate of its effect.");
        Line(sb, "- **Decomposition share:** the part of a speed or cost difference one component accounts for; the parts sum exactly to the difference. A share can pass 100 % when two components pull in opposite directions: if the difference is 2 s, one component adding 3 s and another taking 1 s back are 150 % and −50 %. Over few questions or a small difference, a share's interval can be very wide.");
        Line(sb, "- **Isolated miss:** a question a model scored 50 or less on while the other models' median was 75 or more.");
        Line(sb, "- **Joint rank:** two models share a rank when their 95 % intervals overlap.");
        Line(sb);
    }

    /// <summary>The fact sheet as JSON, in the Markdown copy only; an anonymized copy letters every peer's name, the root-cause data's too.</summary>
    private static void RcaFactSheet(StringBuilder sb, Context ctx)
    {
        Heading(sb, "## Fact sheet");
        if (!ctx.Options.IncludeFactSheet)
        {
            Line(sb, FactSheetElsewhere);
            Line(sb);
            return;
        }
        Line(sb, "```json");
        Line(sb, BenchmarkReportJson.SerializeSorted(ctx.Anonymized ? RcaAnonymizedSheet(ctx) : ctx.Sheet));
        Line(sb, "```");
        Line(sb);
    }

    /// <summary>
    /// The anonymized copy of the sheet with its root-cause data lettered too: every analyzed model but a
    /// per-model subject reads <c>Model X</c> in the models, the evidence and the chart rows, with no provider
    /// or model id, and a recommended run names an analyzed model so; in comparison scope another configured
    /// model is not named. The controls stay named: they are evidence, not peers.
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

    private static string Capitalized(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
