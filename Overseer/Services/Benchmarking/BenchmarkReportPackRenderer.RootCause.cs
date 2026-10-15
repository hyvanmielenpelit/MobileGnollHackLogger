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
/// with its peers, or a run or battery run on its own with its controls). Sections in order: the headline;
/// scope and design; the evidence on intelligence, speed and cost; the known facts with the writer's
/// observations; the hypotheses per axis with the writer's own; the conclusions; the next runs, each with
/// every launcher setting under the launcher's own labels, and the checks; the limitations; threats and
/// reproducibility; the details of every question cited; and, in the Markdown copy, the fact sheet. Every
/// table is computed by code from the stored <see cref="RootCauseResult"/> and facts; the writer's slots follow
/// the code-rendered part of their section. Figures go at the four Root Cause Analysis anchors. Models are
/// named, or lettered in an anonymized copy, except the subject of a per-model document and the controls,
/// which are evidence and always named.
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

    private const string RcaWriterHypothesesNote = "Hypotheses the report writer added from the grader comments and excerpts. Code has not "
        + "tested them, so their confidence is Low.";

    private const string RcaNoAnalysis = "No root-cause analysis was computed for this document.";

    private const string RcaControlsNote = "Controls' mean: the mean score of the controls that scored the question. Instrument-suspect: the "
        + "subject and every control scored below 50. Model-specific: the subject scored below 50 while every control scored 75 or above.";

    private static void RenderRootCauseAnalysis(StringBuilder sb, Context ctx)
    {
        var result = ctx.Sheet.RootCause;
        if (ctx.Comparison) ComparisonTitleBlock(sb, ctx);
        else TitleBlock(sb, ctx);

        Heading(sb, "## The root causes in one sentence");
        Line(sb, Prose(ctx, ctx.Writer.Headline));
        Line(sb);

        RcaScope(sb, ctx, result);
        RcaIntelligence(sb, ctx, result);
        RcaSpeed(sb, ctx, result);
        RcaCost(sb, ctx, result);
        RcaKnownFacts(sb, ctx, result);
        RcaHypotheses(sb, ctx, result);
        RcaConclusions(sb, ctx, result);
        RcaNextRuns(sb, ctx, result);
        RcaLimitations(sb, ctx, result);
        if (ctx.Comparison)
        {
            ComparisonThreats(sb, ctx);
            ComparisonReproducibility(sb, ctx);
        }
        else
        {
            ThreatsToValidity(sb, ctx);
            Reproducibility(sb, ctx);
        }
        RcaQuestionDetails(sb, ctx, result);
        RcaFactSheet(sb, ctx);
    }

    // ---------------------------------------------------------------------------------------------
    // 1. Scope and design
    // ---------------------------------------------------------------------------------------------

    private static void RcaScope(StringBuilder sb, Context ctx, RootCauseResult? result)
    {
        Heading(sb, "## Scope and design");
        if (ctx.Comparison)
        {
            ComparisonSetup(sb, ctx, "### Setup and method");
            ComparisonAvailabilityTable(sb, ctx);
        }
        else
        {
            Line(sb, RcaOriginText(ctx, result));
            Line(sb);
            SetupAndMethod(sb, ctx, "### Setup and method");
            if (RootCauseClaimSupport.IsCompletion(ctx.Sheet))
            {
                RcaControls(sb, result);
            }
            ProviderAvailabilitySection(sb, ctx);
        }
        RcaConfiguration(sb, ctx, result);
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

    // ---------------------------------------------------------------------------------------------
    // 2. Evidence: intelligence
    // ---------------------------------------------------------------------------------------------

    private static void RcaIntelligence(StringBuilder sb, Context ctx, RootCauseResult? result)
    {
        Heading(sb, "## Evidence: intelligence");
        if (ctx.Comparison)
        {
            ResultsTable(sb, ctx);
            DimensionsTable(sb, ctx);
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
            PairedTestsSection(sb, ctx, "### Paired tests: intelligence, speed and cost");
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

        Figures(sb, ctx, BenchmarkReportChartAnchor.RcaIntelligence);
        Slot(sb, ctx, BenchmarkReportSlots.EvidenceIntelligence);
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

    // ---------------------------------------------------------------------------------------------
    // 3. and 4. Evidence: speed and cost
    // ---------------------------------------------------------------------------------------------

    private static void RcaSpeed(StringBuilder sb, Context ctx, RootCauseResult? result)
    {
        Heading(sb, "## Evidence: speed");
        if (result == null)
        {
            Line(sb, RcaNoAnalysis);
            Line(sb);
        }
        else
        {
            Heading(sb, "### Where the time goes");
            RcaSegmentTable(sb, ctx, result.SpeedRows, RootCauseSegments.Time, "Seconds per question", v => BenchmarkReportFormat.OneDecimal(v) + " s");
            Line(sb, "*" + RcaTimeNote + "*");
            Line(sb);

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

            RcaPairDecompositions(sb, ctx, result.SpeedPairs, "### Speed differences, decomposed", cost: false);
        }

        if (ctx.Comparison) SpeedAndCostTable(sb, ctx);
        else SpeedAndCost(sb, ctx, "### Speed and cost figures");

        Figures(sb, ctx, BenchmarkReportChartAnchor.RcaSpeed);
        Slot(sb, ctx, BenchmarkReportSlots.EvidenceSpeed);
    }

    private static void RcaCost(StringBuilder sb, Context ctx, RootCauseResult? result)
    {
        Heading(sb, "## Evidence: cost");
        if (result == null)
        {
            Line(sb, RcaNoAnalysis);
            Line(sb);
        }
        else
        {
            Heading(sb, "### Where the cost goes");
            RcaSegmentTable(sb, ctx, result.CostRows, RootCauseSegments.CostComponents, "Cost per question", BenchmarkReportFormat.Usd);
            Line(sb, "*Mean cost per question by component, priced on this basis: "
                + OneLine(string.IsNullOrWhiteSpace(result.PricingBasis) ? PricingBasisText(ctx.Sheet).Text : result.PricingBasis).TrimEnd('.') + ".*");
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

            RcaPairDecompositions(sb, ctx, result.CostPairs, "### Cost differences, decomposed", cost: true);
        }

        Figures(sb, ctx, BenchmarkReportChartAnchor.RcaCost);
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

    /// <summary>Each analyzed pair's exact decomposition, one row per component, then the pairs it could not decompose.</summary>
    private static void RcaPairDecompositions(StringBuilder sb, Context ctx, IReadOnlyList<RootCausePairDecomposition> pairs, string heading, bool cost)
    {
        if (pairs.Count == 0) return;

        string Amount(double value) => cost
            ? (value > 0 ? "+" : value < 0 ? "-" : string.Empty) + BenchmarkReportFormat.Usd(Math.Abs(value))
            : BenchmarkReportFormat.SignedOneDecimal(value / 1000.0) + " s";

        Heading(sb, heading);
        var available = pairs.Where(p => p.UnavailableReason == null && p.Difference.HasValue).ToList();
        if (available.Count > 0)
        {
            Line(sb, cost
                ? "| Pair | Questions | Difference per question | Component | Effect | Part | Share (95 % interval) |"
                : "| Pair | Questions | Difference per question | Component | Part | Share (95 % interval) |");
            Line(sb, cost ? "|---|---|---|---|---|---|---|" : "|---|---|---|---|---|---|");
            foreach (var pair in available)
            {
                string name = Cell(RcaPairName(ctx, pair.First, pair.Second));
                string difference = Amount(pair.Difference!.Value);
                foreach (var component in pair.Components)
                {
                    string share = component.Share is double s
                        ? RootCauseHypotheses.Pct(s) + (component.ShareLower is double lo && component.ShareUpper is double hi ? " (" + RootCauseHypotheses.Interval(lo, hi) + ")" : string.Empty)
                        : NoValue;
                    Line(sb, "| " + name + " | " + Inv(pair.SharedQuestions) + " | " + difference + " | " + Cell(Capitalized(component.Label))
                        + (cost ? " | " + Cell(component.Effect ?? NoValue) : string.Empty)
                        + " | " + Amount(component.Amount) + " | " + share + " |");
                }
            }
            Line(sb);
            Line(sb, "*" + RcaDecompositionNote + " The difference is the first model's minus the second's.*");
            Line(sb);
        }

        var missing = pairs.Where(p => p.UnavailableReason != null || !p.Difference.HasValue).ToList();
        if (missing.Count > 0)
        {
            Line(sb, "Not decomposed:");
            Line(sb);
            foreach (var pair in missing)
            {
                Line(sb, "- " + RcaPairName(ctx, pair.First, pair.Second) + ": " + SheetText(ctx, pair.UnavailableReason ?? "not computed."));
            }
            Line(sb);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // 5. Known facts
    // ---------------------------------------------------------------------------------------------

    private static void RcaKnownFacts(StringBuilder sb, Context ctx, RootCauseResult? result)
    {
        Heading(sb, "## Known facts");
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
    // 6. Hypotheses
    // ---------------------------------------------------------------------------------------------

    private static void RcaHypotheses(StringBuilder sb, Context ctx, RootCauseResult? result)
    {
        Heading(sb, "## Hypotheses");
        Line(sb, "*" + RcaGradesNote + "*");
        Line(sb);

        var slots = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [RootCauseAxes.Intelligence] = BenchmarkReportSlots.HypothesesIntelligence,
            [RootCauseAxes.Speed] = BenchmarkReportSlots.HypothesesSpeed,
            [RootCauseAxes.Cost] = BenchmarkReportSlots.HypothesesCost
        };
        foreach (string axis in RootCauseAxes.All)
        {
            Heading(sb, "### " + RootCauseAxes.Name(axis));
            var hypotheses = (result?.Hypotheses ?? new List<RootCauseHypothesis>()).Where(h => h.Axis == axis).ToList();
            if (hypotheses.Count == 0)
            {
                Line(sb, result == null ? RcaNoAnalysis : "No hypothesis fired on this axis.");
                Line(sb);
            }
            else
            {
                Line(sb, "| Hypothesis | Statement | Models | Locus | Grade |");
                Line(sb, "|---|---|---|---|---|");
                foreach (var h in hypotheses)
                {
                    Line(sb, "| " + Cell(h.Key) + " | " + Cell(SheetText(ctx, h.Statement)) + " | " + Cell(SheetText(ctx, h.ModelsText))
                        + " | " + Cell(h.Locus) + " | " + Cell(RootCauseGrades.Display(h.Grade)) + " |");
                }
                Line(sb);

                foreach (var h in hypotheses)
                {
                    Line(sb, "- **" + h.Key + "**");
                    Line(sb, "  - *For:* " + (h.For.Count == 0 ? "none recorded." : SheetText(ctx, string.Join(" ", h.For))));
                    Line(sb, "  - *Against:* " + (h.Against.Count == 0 ? "none recorded." : SheetText(ctx, string.Join(" ", h.Against))));
                    Line(sb, "  - *Grade reasons:* " + (h.GradeReasons.Count == 0 ? "every condition of the grade is met." : SheetText(ctx, string.Join("; ", h.GradeReasons)).TrimEnd('.') + "."));
                    Line(sb, "  - *Refuted if:* " + SheetText(ctx, h.RefutedIf));
                    if (h.Share is double share)
                    {
                        string interval = h.ShareLower is double lo && h.ShareUpper is double hi ? " (95 % interval " + RootCauseHypotheses.Interval(lo, hi) + ")" : string.Empty;
                        Line(sb, "  - *Share:* " + RootCauseHypotheses.Pct(share) + interval);
                    }
                    if (h.Robustness.Count > 0)
                    {
                        Line(sb, "  - *Robustness:* " + SheetText(ctx, string.Join("; ", h.Robustness.Select(r => r.Name + " " + RcaCheckText(r.Status)
                            + (string.IsNullOrWhiteSpace(r.Detail) ? string.Empty : " (" + r.Detail.TrimEnd('.') + ")")))) + ".");
                    }
                    if (h.Confounds.Count > 0)
                    {
                        Line(sb, "  - *Open confounds:* " + OneLine(string.Join(", ", h.Confounds)) + ".");
                    }
                    if (h.NextRunNumber is int run) Line(sb, "  - *Settled by:* next run " + Inv(run) + ".");
                    else if (h.CheckNumber is int check) Line(sb, "  - *Settled by:* check " + Inv(check) + ".");
                }
                Line(sb);
            }
            Slot(sb, ctx, slots[axis]);
        }

        Figures(sb, ctx, BenchmarkReportChartAnchor.RcaHypotheses);

        Heading(sb, "### The writer's hypotheses");
        Line(sb, "*" + RcaWriterHypothesesNote + "*");
        Line(sb);
        var own = ctx.Writer.WriterHypotheses ?? new List<BenchmarkReportWriterHypothesis>();
        if (own.Count == 0)
        {
            Line(sb, "The writer added none.");
        }
        foreach (var h in own)
        {
            string models = h.Models.Count == 0 ? string.Empty : BenchmarkReportFormat.LetterList(h.Models.Select(t => RcaTokenName(ctx, t)).ToList()) + "; ";
            Line(sb, "- **" + RootCauseAxes.Name(h.Axis) + " · " + RootCauseGrades.Low + "** (" + models + "locus " + OneLine(h.Locus) + "): " + Prose(ctx, h.Statement));
            Line(sb, "  - *For:* " + string.Join(" ", h.For.Select(t => Prose(ctx, t))));
            Line(sb, "  - *Against:* " + (h.Against.Count == 0 ? "none given." : string.Join(" ", h.Against.Select(t => Prose(ctx, t)))));
            Line(sb, "  - *Refuted if:* " + Prose(ctx, h.RefutedIf));
        }
        Line(sb);
    }

    // ---------------------------------------------------------------------------------------------
    // 7. Conclusions
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
                Line(sb, "| " + RootCauseAxes.Name(axis.Axis) + " | " + RootCauseStatuses.Display(axis.Status) + " | " + Cell(axis.BestHypothesis ?? NoValue)
                    + " | " + Cell(SheetText(ctx, axis.Reason)) + " |");
            }
            Line(sb);
        }
        Slot(sb, ctx, BenchmarkReportSlots.Conclusions);
    }

    // ---------------------------------------------------------------------------------------------
    // 8. Next runs and checks
    // ---------------------------------------------------------------------------------------------

    private static void RcaNextRuns(StringBuilder sb, Context ctx, RootCauseResult? result)
    {
        Heading(sb, "## Next runs and checks");
        var runs = result?.RecommendedRuns ?? new List<RootCauseRecommendedRun>();
        if (runs.Count == 0)
        {
            Line(sb, "Code proposes no run.");
            Line(sb);
        }
        else
        {
            Line(sb, "Runs are listed in the order to make them: the Overseer chat first (C), then the benchmark (B), then model selection (M). "
                + "Each one's settings are the launcher's, copied from the analyzed runs unless its kind changes them; a setting the runs did not share is left for you to choose.");
            Line(sb);
            if (result!.SystemFirst)
            {
                Line(sb, "A harness or suite hypothesis is graded Moderate or higher, so the model-selection runs wait until the system hypotheses are settled.");
                Line(sb);
            }
        }

        foreach (var run in runs.OrderBy(r => r.Number))
        {
            Heading(sb, "### Run " + Inv(run.Number) + ": " + RootCauseNextRuns.KindName(run.Kind) + " (" + RcaPurposeText(run.Purpose) + ", " + run.Mark.ToLowerInvariant() + ")");
            Line(sb, "- **Decides:** " + SheetText(ctx, run.Decides));
            Line(sb, "- **Predicted comparability:** " + (string.IsNullOrWhiteSpace(run.PredictedTier) ? "not predicted" : OneLine(run.PredictedTier)));
            Line(sb, "- **Estimate:** " + (run.EstimatedCostUsd is double cost
                ? "about " + BenchmarkReportFormat.Usd(cost) + (run.EstimatedMinutes is double minutes ? " and " + BenchmarkReportFormat.Whole(minutes) + " minutes" : string.Empty)
                : "not estimated"));
            if (run.HypothesisIds.Count > 0)
            {
                Line(sb, "- **Settles:** " + string.Join(", ", run.HypothesisIds));
            }
            if (run.AfterSystemHypotheses)
            {
                Line(sb, "- **When:** after the system hypotheses are settled.");
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
    // 9. Limitations, 10. Question details, the fact sheet
    // ---------------------------------------------------------------------------------------------

    private static void RcaLimitations(StringBuilder sb, Context ctx, RootCauseResult? result)
    {
        Heading(sb, "## Limitations");
        Line(sb, "- The benchmark asks single-turn questions; live chat carries the conversation before each question.");
        Line(sb, "- It runs without the wiki text that live chat always gives the model before it answers, so a reading about retrieval or tool routing was measured under a condition live chat does not share.");
        Line(sb, "- Spoiler-free mode, web search and subagents are off, and one combination of the chat's options is graded; a cause found here holds for that configuration only.");
        Line(sb, "- The hypotheses come from a fixed rule table over these runs; points at risk are upper bounds, and a decomposition rests on the questions both sides have figures for. The writer's own hypotheses are qualitative.");
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
        Line(sb);
        Slot(sb, ctx, BenchmarkReportSlots.Limitations);
    }

    /// <summary>
    /// The details of every question the document cites: in the writer's prose and lists, in the code's
    /// hypotheses, known facts, checks and runs, and each instrument-suspect or model-specific control item.
    /// </summary>
    private static void RcaQuestionDetails(StringBuilder sb, Context ctx, RootCauseResult? result)
    {
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
        }

        var cited = new HashSet<int>(BenchmarkReportPackValidator.CitedQuestions(ctx.Writer, ctx.Sheet));
        cited.UnionWith(BenchmarkReportPackValidator.QuestionsReferencedIn(texts, ctx.Sheet));
        if (result != null)
        {
            cited.UnionWith(result.ControlItems.Where(i => i.InstrumentSuspect || i.ModelSpecific).Select(i => i.Number));
        }
        if (cited.Count == 0) return;

        if (ctx.Comparison)
        {
            ComparisonQuestionDetails(sb, ctx, grading: true, level: 2, only: cited);
        }
        else
        {
            var questions = ctx.Sheet.Questions.Where(q => cited.Contains(q.Number)).OrderBy(q => q.Number).ToList();
            QuestionDetails(sb, ctx, questions, grading: ctx.Options.Disclosure == BenchmarkReportDisclosure.Full, level: 2);
        }
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

    /// <summary>A model of the analysis by its token as this copy names it: the subject by its label, a letter by <see cref="NameOfLetter"/>.</summary>
    private static string RcaTokenName(Context ctx, string token)
        => string.Equals(token, RootCauseEngine.SubjectToken, StringComparison.Ordinal) ? ctx.Sheet.SubjectLabel : NameOfLetter(ctx, token);

    private static string RcaPairName(Context ctx, string first, string second) => RcaTokenName(ctx, first) + " vs " + RcaTokenName(ctx, second);

    private static string Capitalized(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
