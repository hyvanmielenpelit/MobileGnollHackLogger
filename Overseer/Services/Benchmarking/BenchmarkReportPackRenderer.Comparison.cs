namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services.Benchmarking.Pdf;

/// <summary>
/// The comparison-scope documents (<see cref="BenchmarkReportFactSheet.IsComparison"/>): every covered
/// model described as an equal, in letter order, with no subject. A named copy prints each model's
/// label and provider and no letter in its prose; tables that list the models carry a Letter column
/// under both namings, so a named and an anonymized copy can be matched. A copy covering a subset of
/// the comparison says so, and counts the comparison's other models without naming them.
/// </summary>
public static partial class BenchmarkReportPackRenderer
{
    private const string PairedTestsNote = "Each pair is compared on the questions both models answered. Each measure is its own "
        + "family of tests, Holm-adjusted across the tests it makes over these models; a result is established when its adjusted "
        + "p-value is below 0.05. Another set of models gives another family and another adjustment.";

    /// <summary>
    /// The title of a comparison-scope document. Named: <c>Comparison #12 — name: Executive Summary</c>
    /// for a document covering every model, <c>Comparison #12 — A vs B: …</c> for a subset of up to
    /// three models and <c>Comparison #12 — 4 of 10 models: …</c> for a larger one. Anonymized, the
    /// name and the models are left out: <c>Comparison #12: …</c>, <c>Comparison #12 — 2 of 5 models: …</c>.
    /// Without a number the head is the sheet's subject label.
    /// </summary>
    public static string BuildComparisonTitle(
        BenchmarkReportAudience audience, BenchmarkReportFactSheet sheet, BenchmarkPdfComparison? comparison, BenchmarkReportPeerNaming naming)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        string head = comparison is { } numbered ? "Comparison #" + Inv(numbered.Id) : "Comparison";
        bool named = naming == BenchmarkReportPeerNaming.Named;
        bool coversAll = sheet.CoversAllEntries ?? false;
        var models = OrderedPeers(sheet);
        int of = sheet.ComparisonEntryCount ?? comparison?.EntryCount ?? models.Count;

        string? detail;
        if (coversAll)
        {
            detail = named && !string.IsNullOrWhiteSpace(comparison?.Name) ? comparison!.Name : null;
        }
        else if (named && models.Count <= BenchmarkComparisonIdentityService.MaxNamedEntries)
        {
            detail = string.Join(" vs ", models.Select(m => m.Label));
        }
        else
        {
            detail = Inv(models.Count) + " of " + Inv(of) + " models";
        }

        return head + (detail != null ? " — " + detail : string.Empty) + ": " + AudienceName(audience);
    }

    /// <summary>A stored comparison-scope document's title as a copy of <paramref name="naming"/> prints it, with its comparison's name now.</summary>
    public static string ComparisonTitle(BenchmarkReportDocument document, BenchmarkReportPeerNaming naming)
    {
        ArgumentNullException.ThrowIfNull(document);
        var sheet = BenchmarkReportJson.Deserialize<BenchmarkReportFactSheet>(document.FactsJson);
        return BuildComparisonTitle(document.Audience, sheet, BenchmarkPdfDocumentInfo.ComparisonOf(document), naming);
    }

    private static void RenderComparison(StringBuilder sb, Context ctx)
    {
        switch (ctx.Document.Audience)
        {
            case BenchmarkReportAudience.ExecutiveSummary:
                ComparisonExecutiveSummary(sb, ctx);
                break;
            case BenchmarkReportAudience.TechnicalReport:
                ComparisonTechnicalReport(sb, ctx);
                break;
            default:
                ComparisonInternalBrief(sb, ctx);
                break;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Documents
    // ---------------------------------------------------------------------------------------------

    private static void ComparisonExecutiveSummary(StringBuilder sb, Context ctx)
    {
        ComparisonTitleBlock(sb, ctx);

        Heading(sb, "## The result in one sentence");
        Line(sb, Prose(ctx, ctx.Writer.Headline));
        Line(sb);

        Heading(sb, "## The comparison in one paragraph");
        Slot(sb, ctx, BenchmarkReportSlots.Overview);

        Heading(sb, "## Which model to use");
        Slot(sb, ctx, BenchmarkReportSlots.WhichModel);

        Heading(sb, "## How they compare");
        ModelsTable(sb, ctx);
        DimensionsTable(sb, ctx);
        Figures(sb, ctx, BenchmarkReportChartAnchor.HowTheyCompare);

        Heading(sb, "## Model by model");
        ModelPoints(sb, ctx, headingLevel: 0);

        Heading(sb, "## Trade-offs");
        Slot(sb, ctx, BenchmarkReportSlots.TradeOffs);
        FrontierLines(sb, ctx);

        Heading(sb, "## How reliable this is");
        Slot(sb, ctx, BenchmarkReportSlots.Reliability);
        OverlapSentence(sb, ctx);
        PairedSummary(sb, ctx);
        if (ComparisonWriterCaveat(ctx) is string caveat)
        {
            Line(sb, caveat);
            Line(sb);
        }

        ComparisonAboutBenchmark(sb, ctx);
    }

    private static void ComparisonTechnicalReport(StringBuilder sb, Context ctx)
    {
        ComparisonTitleBlock(sb, ctx);

        Heading(sb, "## Abstract");
        Slot(sb, ctx, BenchmarkReportSlots.Abstract);

        ComparisonSetup(sb, ctx);

        Heading(sb, "## Results");
        ResultsTable(sb, ctx);
        Figures(sb, ctx, BenchmarkReportChartAnchor.ComparisonResults);
        Slot(sb, ctx, BenchmarkReportSlots.Results);
        PairedTestsSection(sb, ctx, "### Paired tests");

        Heading(sb, "## Dimension profiles");
        DimensionsTable(sb, ctx);
        Figures(sb, ctx, BenchmarkReportChartAnchor.DimensionProfiles);
        Slot(sb, ctx, BenchmarkReportSlots.DimensionProfiles);

        Heading(sb, "## Speed and cost frontier");
        SpeedAndCostTable(sb, ctx);
        FrontierLines(sb, ctx);
        Figures(sb, ctx, BenchmarkReportChartAnchor.SpeedAndCostFrontier);
        Slot(sb, ctx, BenchmarkReportSlots.Frontier);

        Heading(sb, "## Per-model analysis");
        ModelPoints(sb, ctx, headingLevel: 3);

        Heading(sb, "## Cross-model question patterns");
        Slot(sb, ctx, BenchmarkReportSlots.QuestionPatterns);
        MatrixTable(sb, ctx, "### Per-question matrix");

        Heading(sb, "## Grader reliability");
        GraderTable(sb, ctx);
        Slot(sb, ctx, BenchmarkReportSlots.GraderReliability);

        ComparisonThreats(sb, ctx);
        ComparisonReproducibility(sb, ctx);

        if (ctx.Options.Disclosure >= BenchmarkReportDisclosure.Detailed)
        {
            ComparisonQuestionDetails(sb, ctx, grading: ctx.Options.Disclosure == BenchmarkReportDisclosure.Full, level: 2);
        }
    }

    private static void ComparisonInternalBrief(StringBuilder sb, Context ctx)
    {
        ComparisonTitleBlock(sb, ctx);

        Heading(sb, "## Models compared");
        ModelsTable(sb, ctx);
        Figures(sb, ctx, BenchmarkReportChartAnchor.ModelsCompared);

        Heading(sb, "## 1. Shared gaps");
        Slot(sb, ctx, BenchmarkReportSlots.SharedGaps);

        Heading(sb, "## 2. Model-specific gaps");
        Slot(sb, ctx, BenchmarkReportSlots.ModelGaps);

        Heading(sb, "## 3. The benchmarking system");
        Slot(sb, ctx, BenchmarkReportSlots.BenchmarkSystem);
        PairedTestsSection(sb, ctx, "### Paired tests");

        Heading(sb, "## 4. Leads");
        Line(sb, "*" + LeadsBanner + "*");
        Line(sb);
        if (ctx.Writer.Leads.Count == 0)
        {
            Line(sb, "No leads were recorded.");
        }
        foreach (var lead in ctx.Writer.Leads)
        {
            string evidence = EvidenceText(ctx, lead);
            Line(sb, "- **[" + lead.Triage + "]** " + Prose(ctx, lead.Text) + (evidence.Length > 0 ? " *(evidence: " + evidence + ")*" : string.Empty));
        }
        Line(sb);

        MatrixTable(sb, ctx, "## 5. Per-question matrix");
        ComparisonQuestionDetails(sb, ctx, grading: true, level: 3);

        Heading(sb, "## 6. Fact sheet");
        if (!ctx.Options.IncludeFactSheet)
        {
            Line(sb, FactSheetElsewhere);
            Line(sb);
            return;
        }
        Line(sb, "```json");
        Line(sb, BenchmarkReportJson.SerializeSorted(ctx.Anonymized ? ComparisonAnonymizedSheet(ctx) : ctx.Sheet));
        Line(sb, "```");
        Line(sb);
    }

    // ---------------------------------------------------------------------------------------------
    // Sections
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The title, then, with <see cref="BenchmarkReportRenderOptions.IncludeFrontMatter"/>, the comparison
    /// line, the stamp and the facts list: the suite or battery, the questions, the models, the coverage
    /// of a subset and the pricing basis.
    /// </summary>
    private static void ComparisonTitleBlock(StringBuilder sb, Context ctx)
    {
        var sheet = ctx.Sheet;
        var comparison = BenchmarkPdfDocumentInfo.ComparisonOf(ctx.Document);
        Line(sb, "# " + BuildComparisonTitle(ctx.Document.Audience, sheet, comparison, ctx.Options.PeerNaming));
        Line(sb);
        if (!ctx.Options.IncludeFrontMatter) return;

        if (comparison != null)
        {
            Line(sb, "**Comparison:** " + BenchmarkPdfDocumentInfo.ComparisonText(
                comparison, BenchmarkPdfDocumentInfo.ComparisonComputedOn(sheet), ctx.Options.PeerNaming));
            Line(sb);
        }

        Line(sb, "*" + Stamp(ctx.Document.Audience, ctx.Options.Disclosure) + "*");
        Line(sb);
        Line(sb, "- **Date:** " + ctx.Document.CreatedAtUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Line(sb, "- **" + (IsBatteryComparison(ctx) ? "Battery" : "Suite") + ":** " + sheet.SuiteName);
        Line(sb, "- **Questions:** " + Inv(sheet.Questions.Count));
        Line(sb, "- **Models:** " + ModelsText(ctx));
        if (CoverageText(ctx) is string coverage)
        {
            Line(sb, "- **Coverage:** " + coverage);
        }
        Line(sb, "- **" + Label("comparison.pricingBasis") + ":** " + PricingBasisText(sheet).Text);
        Line(sb);
    }

    /// <summary>Named: the models' labels; anonymized: <c>4 models (A to D), identities withheld</c>.</summary>
    private static string ModelsText(Context ctx)
    {
        var models = OrderedPeers(ctx.Sheet);
        if (!ctx.Anonymized) return BenchmarkReportFormat.LetterList(models.Select(m => m.Label).ToList());

        string letters = models.Count switch
        {
            0 => string.Empty,
            1 => models[0].Letter,
            2 => models[0].Letter + " and " + models[1].Letter,
            _ => models[0].Letter + " to " + models[^1].Letter
        };
        return Inv(models.Count) + " models (" + letters + "), identities withheld";
    }

    /// <summary>
    /// <c>2 of 5 models of Comparison #12; the other 3 are not part of this document.</c> for a subset;
    /// null for a document covering every model. The comparison's other models are counted, never named.
    /// </summary>
    private static string? CoverageText(Context ctx)
    {
        if (ctx.Sheet.CoversAllEntries != false) return null;

        int covered = ctx.Sheet.Peers.Count;
        int of = ctx.Sheet.ComparisonEntryCount ?? covered;
        int others = Math.Max(0, of - covered);
        var comparison = BenchmarkPdfDocumentInfo.ComparisonOf(ctx.Document);
        string name = comparison != null ? "Comparison #" + Inv(comparison.Id) : "the comparison";
        return Inv(covered) + " of " + Inv(of) + " models of " + name + "; the other " + Inv(others)
            + (others == 1 ? " is" : " are") + " not part of this document.";
    }

    /// <summary>Every covered model's Intelligence Index with its interval and joint rank, median answer time, cost per question and critical errors.</summary>
    private static void ModelsTable(StringBuilder sb, Context ctx)
    {
        var header = ModelColumns(ctx);
        header.AddRange(new[] { Label("quality.index"), "Rank", Label("speed.modelTimeP50"), Label("cost.perQuestion"), Label("errors.critical") });
        TableHeaderOf(sb, header);

        foreach (var (entry, peer) in ComparisonRows(ctx))
        {
            string index = entry.QualityIndex.HasValue
                ? BenchmarkReportFormat.Whole(entry.QualityIndex.Value)
                    + (entry.QualityLower.HasValue && entry.QualityUpper.HasValue
                        ? " (" + BenchmarkReportFormat.Whole(entry.QualityLower.Value) + "–" + BenchmarkReportFormat.Whole(entry.QualityUpper.Value) + ")"
                        : string.Empty)
                : BenchmarkReportFacts.NotAvailable;
            var cells = ModelCells(ctx, peer);
            cells.AddRange(new[] { index, QualityRankCell(ctx, entry), TimeCell(entry), CostCell(entry), CriticalErrorsCell(ctx, entry, peer) });
            Line(sb, "| " + string.Join(" | ", cells) + " |");
        }
        Line(sb);
        Line(sb, "*Each model's Intelligence Index is followed by its 95 % interval; a joint rank means its interval overlaps a neighbor's, so the order between them is not established by the intervals.*");
        Line(sb);
    }

    private static void DimensionsTable(StringBuilder sb, Context ctx)
    {
        var header = ModelColumns(ctx);
        header.AddRange(BenchmarkReportFactLabels.Dimensions.Select(d => d.Name));
        TableHeaderOf(sb, header);

        foreach (var peer in OrderedPeers(ctx.Sheet))
        {
            var cells = ModelCells(ctx, peer);
            cells.AddRange(BenchmarkReportFactLabels.Dimensions.Select(d => Num(ctx, ModelKey(peer.Letter, "dimension." + d.Key))));
            Line(sb, "| " + string.Join(" | ", cells) + " |");
        }
        Line(sb);

        var spreads = BenchmarkReportFactLabels.Dimensions
            .Select(d => (d.Name, Fact: Fact(ctx, "spread.dimension." + d.Key)))
            .Where(s => s.Fact is { Available: true })
            .ToList();
        foreach (var (name, fact) in spreads)
        {
            Line(sb, "- **" + name + ":** " + Shown(ctx, fact!));
        }
        if (spreads.Count > 0) Line(sb);
    }

    private static void ResultsTable(StringBuilder sb, Context ctx)
    {
        var header = ModelColumns(ctx);
        header.AddRange(new[]
        {
            Label("quality.index"), Label("quality.interval"), "Rank", Label("speed.modelTimeP50"), "Speed rank",
            Label("cost.perQuestion"), "Cost rank", Label("errors.critical")
        });
        TableHeaderOf(sb, header);

        foreach (var (entry, peer) in ComparisonRows(ctx))
        {
            string index = entry.QualityIndex.HasValue ? BenchmarkReportFormat.Whole(entry.QualityIndex.Value) : BenchmarkReportFacts.NotAvailable;
            string interval = entry.QualityLower.HasValue && entry.QualityUpper.HasValue
                ? BenchmarkReportFormat.Whole(entry.QualityLower.Value) + "–" + BenchmarkReportFormat.Whole(entry.QualityUpper.Value)
                : BenchmarkReportFacts.NotAvailable;
            var cells = ModelCells(ctx, peer);
            cells.AddRange(new[]
            {
                index, interval, QualityRankCell(ctx, entry), TimeCell(entry), RankCell(entry.SpeedRank),
                CostCell(entry), RankCell(entry.CostRank), CriticalErrorsCell(ctx, entry, peer)
            });
            Line(sb, "| " + string.Join(" | ", cells) + " |");
        }
        Line(sb);

        var rows = ComparisonRows(ctx).Select(r => (r.Entry, (BenchmarkReportPeer?)r.Peer)).ToList();
        DegradedNotes(sb, ctx, rows, "Not ranked on speed:", e => e.SpeedDegraded);
        DegradedNotes(sb, ctx, rows, "Not ranked on cost:", e => e.CostDegraded);
    }

    private static void SpeedAndCostTable(StringBuilder sb, Context ctx)
    {
        var keys = SpeedAndCostKeys()
            .Where(key => OrderedPeers(ctx.Sheet).Any(p => IsAvailable(ctx, ModelKey(p.Letter, key))))
            .ToList();
        if (keys.Count == 0) return;

        var header = ModelColumns(ctx);
        header.AddRange(keys.Select(k => SpeedAndCostLabel(ctx, k)));
        TableHeaderOf(sb, header);
        foreach (var peer in OrderedPeers(ctx.Sheet))
        {
            var cells = ModelCells(ctx, peer);
            cells.AddRange(keys.Select(k => Num(ctx, ModelKey(peer.Letter, k))));
            Line(sb, "| " + string.Join(" | ", cells) + " |");
        }
        Line(sb);
    }

    /// <summary>The models on each two-measure Pareto frontier.</summary>
    private static void FrontierLines(StringBuilder sb, Context ctx)
    {
        var lines = new[]
        {
            ("frontier.qualityCost", "Intelligence against cost"),
            ("frontier.qualitySpeed", "Intelligence against speed"),
            ("frontier.speedCost", "Speed against cost")
        }
        .Select(f => (f.Item2, Fact: Fact(ctx, f.Item1)))
        .Where(f => f.Fact != null)
        .ToList();
        if (lines.Count == 0) return;

        Line(sb, "On the Pareto frontier (no other model is at least as good on both measures and better on one):");
        Line(sb);
        foreach (var (name, fact) in lines)
        {
            Line(sb, "- **" + name + ":** " + (fact!.Available ? Shown(ctx, fact) : SheetText(ctx, NotAvailableText(fact))));
        }
        Line(sb);
    }

    /// <summary>How many pairs of models have overlapping 95 % intervals.</summary>
    private static void OverlapSentence(StringBuilder sb, Context ctx)
    {
        var overlaps = ctx.Sheet.Facts
            .Where(f => f.Available && f.Key.StartsWith("pair.", StringComparison.Ordinal) && f.Key.EndsWith(".intervalOverlap", StringComparison.Ordinal))
            .ToList();
        if (overlaps.Count == 0) return;

        int overlapping = overlaps.Count(BenchmarkReportPackPrompt.IsTrue);
        if (overlaps.Count == 1)
        {
            Line(sb, overlapping == 1
                ? "The two models' 95 % intervals overlap; the intervals alone do not establish which scored higher."
                : "The two models' 95 % intervals do not overlap.");
            Line(sb);
            return;
        }

        Line(sb, "Of the " + Inv(overlaps.Count) + (overlaps.Count == 1 ? " pair" : " pairs") + " of models, " + Inv(overlapping)
            + (overlapping == 1 ? " has" : " have") + " overlapping 95 % intervals"
            + (overlapping > 0 ? "; the intervals alone do not establish the order within such a pair." : "."));
        Line(sb);
    }

    /// <summary>One sentence per family: who was tested against whom, the adjustment, and how many intelligence orders it establishes.</summary>
    private static void PairedSummary(StringBuilder sb, Context ctx)
    {
        var families = ctx.Sheet.PairedTests ?? new List<BenchmarkReportPairedFamily>();
        if (families.Count == 0)
        {
            Line(sb, "No pair of models was tested" + (string.IsNullOrWhiteSpace(ctx.Sheet.PairedTestsUnavailableReason) ? string.Empty : " (" + SheetText(ctx, ctx.Sheet.PairedTestsUnavailableReason) + ")")
                + ", so a gap between two models may be noise.");
            Line(sb);
            return;
        }

        foreach (var family in families)
        {
            var intelligence = family.Measures.FirstOrDefault(m => m.Measure == BenchmarkPairedTests.IntelligenceMeasure);
            if (intelligence == null) continue;

            int established = intelligence.Pairs.Count(p => p.Established && p.Favors != "None");
            string who = family.Mode == "AllPairs"
                ? "Every pair of models was compared"
                : NameOfLetter(ctx, family.ReferenceLetter ?? "A") + ", the highest Intelligence Index, was compared with each other model";
            string adjustment = intelligence.FamilySize > 1
                ? "Holm-adjusted across " + Inv(intelligence.FamilySize) + " tests"
                : intelligence.FamilySize == 1 ? "a single test" : "no test could be made";
            string result = intelligence.Pairs.Count == 1
                ? (established == 1 ? "the comparison establishes" : "the comparison does not establish")
                : Inv(established) + " of " + Inv(intelligence.Pairs.Count) + " comparisons establish";
            Line(sb, who + " on the questions both answered (" + adjustment + "); " + result + " which scored higher.");
            Line(sb);
        }
    }

    /// <summary>Each paired family's tables: Intelligence, then speed and cost, each pair with its result after the family's adjustment.</summary>
    private static void PairedTestsSection(StringBuilder sb, Context ctx, string heading)
    {
        Heading(sb, heading);
        var families = ctx.Sheet.PairedTests ?? new List<BenchmarkReportPairedFamily>();
        if (families.Count == 0)
        {
            Line(sb, "No pair of models was tested" + (string.IsNullOrWhiteSpace(ctx.Sheet.PairedTestsUnavailableReason) ? "." : ": " + SheetText(ctx, ctx.Sheet.PairedTestsUnavailableReason))
                + " A gap between two models may be noise.");
            Line(sb);
            return;
        }

        Line(sb, "*" + PairedTestsNote + "*");
        Line(sb);

        foreach (var family in families)
        {
            string title = family.Mode == "AllPairs"
                ? "Every pair"
                : "Against " + NameOfLetter(ctx, family.ReferenceLetter ?? "A") + " (the reference)";
            Line(sb, "**" + title + "**");
            Line(sb);

            var intelligence = family.Measures.FirstOrDefault(m => m.Measure == BenchmarkPairedTests.IntelligenceMeasure);
            if (intelligence != null)
            {
                Line(sb, Cell(FamilySizeText(ctx, "Intelligence", intelligence)));
                Line(sb);
                Line(sb, "| Pair | Questions | Difference (95 % interval) | Adjusted p | Result |");
                Line(sb, "|---|---|---|---|---|");
                foreach (var pair in intelligence.Pairs)
                {
                    string difference = pair.Effect is double effect
                        ? BenchmarkReportFormat.SignedOneDecimal(effect) + " points"
                          + (pair.Lower is double l && pair.Upper is double u
                              ? " (" + BenchmarkReportFormat.SignedOneDecimal(l) + " to " + BenchmarkReportFormat.SignedOneDecimal(u) + ")"
                              : string.Empty)
                        : NoValue;
                    Line(sb, "| " + Cell(PairName(ctx, pair)) + " | " + Inv(pair.PairedItems) + " | " + difference + " | "
                        + AdjustedCell(pair) + " | " + Cell(ResultText(ctx, pair, "scored higher")) + " |");
                }
                Line(sb);
                Line(sb, "*Difference: the mean per-question difference, first model minus second, on the questions both answered.*");
                Line(sb);
            }

            var speed = family.Measures.FirstOrDefault(m => m.Measure == BenchmarkPairedTests.SpeedMeasure);
            var cost = family.Measures.FirstOrDefault(m => m.Measure == BenchmarkPairedTests.CostMeasure);
            if (speed != null || cost != null)
            {
                var notes = new List<string>();
                if (speed != null) notes.Add(FamilySizeText(ctx, "Speed", speed));
                if (cost != null) notes.Add(FamilySizeText(ctx, "Cost", cost));
                Line(sb, Cell(string.Join(" ", notes)));
                Line(sb);

                var pairs = (speed?.Pairs ?? new List<BenchmarkReportPairedTest>())
                    .Concat(cost?.Pairs ?? new List<BenchmarkReportPairedTest>())
                    .Select(p => (p.FirstLetter, p.SecondLetter))
                    .Distinct()
                    .ToList();
                Line(sb, "| Pair | Time ratio (95 % interval) | Speed result | Spend ratio (95 % interval) | Cost result |");
                Line(sb, "|---|---|---|---|---|");
                foreach (var (first, second) in pairs)
                {
                    var s = speed?.Pairs.FirstOrDefault(p => p.FirstLetter == first && p.SecondLetter == second);
                    var c = cost?.Pairs.FirstOrDefault(p => p.FirstLetter == first && p.SecondLetter == second);
                    Line(sb, "| " + Cell(PairName(ctx, first, second))
                        + " | " + RatioCell(s) + " | " + Cell(s == null ? NoValue : ResultText(ctx, s, "was faster"))
                        + " | " + RatioCell(c) + " | " + Cell(c == null ? NoValue : ResultText(ctx, c, "was cheaper")) + " |");
                }
                Line(sb);
                Line(sb, "*A ratio is the first model's own time or spend per question divided by the second's, on the questions both answered.*");
                Line(sb);
            }

            if (!string.IsNullOrWhiteSpace(family.SingleRunCaveat))
            {
                Line(sb, "*" + SheetText(ctx, family.SingleRunCaveat) + "*");
                Line(sb);
            }
        }
    }

    /// <summary>
    /// Free text of a comparison-scope sheet on one line. The sheet names its models as <c>Model X</c>;
    /// an anonymized copy prints it so, a named copy with each model's label.
    /// </summary>
    private static string SheetText(Context ctx, string? text)
        => ctx.Anonymized ? OneLine(text) : NamedLetters(ctx, OneLine(text));

    private static string FamilySizeText(Context ctx, string measure, BenchmarkReportPairedMeasure family)
        => family.NotTestedReason != null
            ? measure + ": not tested. " + SheetText(ctx, family.NotTestedReason)
            : measure + ": " + (family.FamilySize > 1
                ? "Holm-adjusted across " + Inv(family.FamilySize) + " tests."
                : family.FamilySize == 1 ? "a single test, no adjustment." : "no test could be made.");

    private static string AdjustedCell(BenchmarkReportPairedTest pair)
        => pair.AdjustedPValue is double p ? BenchmarkComparisonReportFacts.PValue(p) : NoValue;

    private static string RatioCell(BenchmarkReportPairedTest? pair)
    {
        if (pair?.Effect is not double effect) return NoValue;
        string ratio = Math.Round(effect, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture);
        return pair.Lower is double l && pair.Upper is double u
            ? ratio + " (" + Math.Round(l, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture)
              + " to " + Math.Round(u, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture) + ")"
            : ratio;
    }

    /// <summary><c>GPT-5.6 Luna scored higher</c>, <c>not established</c> or <c>not tested</c>.</summary>
    private static string ResultText(Context ctx, BenchmarkReportPairedTest pair, string verb)
    {
        if (pair.NotTestedReason != null || pair.AdjustedPValue == null) return "not tested";
        if (!pair.Established || pair.Favors == "None") return "not established";
        return NameOfLetter(ctx, pair.Favors == "First" ? pair.FirstLetter : pair.SecondLetter) + " " + verb;
    }

    private static string PairName(Context ctx, BenchmarkReportPairedTest pair) => PairName(ctx, pair.FirstLetter, pair.SecondLetter);

    private static string PairName(Context ctx, string first, string second) => NameOfLetter(ctx, first) + " vs " + NameOfLetter(ctx, second);

    /// <summary>The points about each model, under its name in letter order; with <paramref name="headingLevel"/> 0, as a bold line.</summary>
    private static void ModelPoints(StringBuilder sb, Context ctx, int headingLevel)
    {
        var lists = ctx.Writer.Models ?? new List<BenchmarkReportModelPoints>();
        foreach (var peer in OrderedPeers(ctx.Sheet))
        {
            string name = ProseName(ctx, peer);
            if (headingLevel > 0)
            {
                Heading(sb, new string('#', headingLevel) + " " + name);
            }
            else
            {
                Line(sb, "**" + name + "**");
                Line(sb);
            }

            var points = lists.FirstOrDefault(m => string.Equals(m.Model, peer.Letter, StringComparison.Ordinal))?.Points
                ?? new List<BenchmarkReportWriterItem>();
            if (points.Count == 0)
            {
                Line(sb, "No points were recorded for this model.");
            }
            foreach (var point in points)
            {
                Line(sb, "- " + Prose(ctx, point.Text));
                if (ctx.PrintsEvidence && EvidenceText(ctx, point) is { Length: > 0 } evidence)
                {
                    Line(sb, "  - *Evidence:* " + evidence);
                }
            }
            Line(sb);
        }
    }

    /// <summary>An item's evidence in words: each fact by its label and value, each question with the models' scores; empty for none.</summary>
    private static string EvidenceText(Context ctx, BenchmarkReportWriterItem item)
    {
        var parts = new List<string>();
        var numbers = new List<int>(item.Questions ?? new List<int>());
        foreach (string id in (item.Evidence ?? new List<string>()).Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()).Distinct(StringComparer.Ordinal))
        {
            if (ComparisonQuestionNumber(ctx, id) is int number)
            {
                numbers.Add(number);
                continue;
            }
            var fact = Fact(ctx, id);
            parts.Add(ComparisonFactLabel(ctx, id) + (fact == null ? string.Empty : ": " + (fact.Available ? Shown(ctx, fact) : BenchmarkReportFacts.NotAvailable)));
        }

        var listed = numbers.Distinct().OrderBy(n => n).ToList();
        if (listed.Count > 0)
        {
            var shown = listed.Take(EvidenceQuestionLimit).Select(ctx.QuestionLabel).ToList();
            parts.Add(string.Join(", ", shown) + (listed.Count > shown.Count ? " and " + Inv(listed.Count - shown.Count) + " more" : string.Empty));
        }
        return string.Join(" · ", parts);
    }

    private static int? ComparisonQuestionNumber(Context ctx, string id)
    {
        if (ctx.QuestionNumberOf(id) is int reference) return reference;
        return id.Length > 1 && id[0] == 'Q' && int.TryParse(id.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out int n) && ctx.Sheet.Questions.Any(q => q.Number == n)
            ? n
            : null;
    }

    /// <summary>
    /// A comparison-scope fact's label: <c>Model B's critical errors</c> for <c>model.B.errors.critical</c>,
    /// <c>Model A against Model C: …</c> for a pair's fact, each model named in a named copy.
    /// </summary>
    private static string ComparisonFactLabel(Context ctx, string key)
    {
        string[] parts = key.Split('.');
        if (parts.Length >= 3 && parts[0] == "model")
        {
            string rest = string.Join('.', parts.Skip(2));
            return NameOfLetter(ctx, parts[1]) + "'s " + LowerFirst(ModelFactLabel(rest));
        }
        if (parts.Length >= 4 && parts[0] == "pair")
        {
            string rest = string.Join('.', parts.Skip(3));
            return NameOfLetter(ctx, parts[1]) + " against " + NameOfLetter(ctx, parts[2]) + ": " + LowerFirst(PairFactLabel(rest));
        }
        return Label(key);
    }

    private static string ModelFactLabel(string rest) => rest switch
    {
        "runs" => "Runs",
        "state" => "Comparability state",
        "thinkingLevel" => "Thinking level",
        "frontier" => "Frontier",
        _ => Label(rest)
    };

    private static string PairFactLabel(string rest) => rest switch
    {
        "intervalOverlap" => "Interval overlap",
        "sharedQuestions" => "Questions both answered",
        "quality.difference" => "Paired difference",
        "quality.interval" => "Paired interval",
        "speed.ratio" => "Time ratio",
        "cost.ratio" => "Spend ratio",
        _ when rest.EndsWith("." + BenchmarkComparisonReportFacts.ReferenceFamilyName, StringComparison.Ordinal) => "Result against the reference",
        _ when rest.EndsWith("." + BenchmarkComparisonReportFacts.AllPairsFamilyName, StringComparison.Ordinal) => "Result among all pairs",
        _ => Label(rest)
    };

    /// <summary>
    /// One row per question: its topic and band, then each model's score under its letter, <c>CE</c>
    /// for a critical error, and the spread.
    /// </summary>
    private static void MatrixTable(StringBuilder sb, Context ctx, string heading)
    {
        var models = OrderedPeers(ctx.Sheet);
        Heading(sb, heading);
        Line(sb, "| Q | Topic | Band | " + string.Join(" | ", models.Select(m => m.Letter)) + " | Spread |");
        Line(sb, "|---|---|---|" + string.Concat(models.Select(_ => "---|")) + "---|");
        bool topicMissing = false;
        foreach (var q in ctx.Sheet.Questions.OrderBy(q => q.Number))
        {
            var cells = models.Select(m =>
            {
                var cell = q.Models?.FirstOrDefault(c => string.Equals(c.Letter, m.Letter, StringComparison.Ordinal));
                if (cell == null) return NoValue;
                string score = cell.Score.HasValue ? BenchmarkReportFormat.Whole(cell.Score.Value) : NoValue;
                return cell.CriticalError ? score + " CE" : score;
            });
            string spread = q.PeerMin.HasValue && q.PeerMax.HasValue && q.PeerCount >= 2
                ? BenchmarkReportFormat.WholeDifference(q.PeerMax.Value, q.PeerMin.Value).TrimStart('+')
                : NoValue;
            string? topic = Topic(ctx, q.Number);
            topicMissing |= topic == null;
            Line(sb, "| " + ctx.QuestionLabel(q.Number) + " | " + Cell(topic ?? NoValue) + " | " + Cell(q.Band)
                + " | " + string.Join(" | ", cells) + " | " + spread + " |");
        }
        Line(sb);
        Line(sb, "*" + MatrixColumnsSentence(ctx, models) + " CE marks a critical error; Spread is the highest score minus the lowest. "
            + NoValue + " under a model marks a question it was not asked or not scored on."
            + (topicMissing ? " " + NoValue + " under Topic marks a question not given in detail." : string.Empty) + "*");
        Line(sb);
    }

    /// <summary>
    /// The matrix legend's first sentence: each column letter with its model's name in a named copy,
    /// <c>Columns: A = …, B = ….</c>; a pointer to the models table in an anonymized one.
    /// </summary>
    private static string MatrixColumnsSentence(Context ctx, IReadOnlyList<BenchmarkReportPeer> models)
        => ctx.Anonymized || models.Count == 0
            ? "Each model's column is headed by its letter in the models table."
            : "Columns: " + string.Join(", ", models.Select(m => m.Letter + " = " + ProseName(ctx, m))) + ".";

    /// <summary>Each model's panel figures: agreement, disagreements, each member's index alone and the reference reader's.</summary>
    private static void GraderTable(StringBuilder sb, Context ctx)
    {
        var keys = new[] { "panel.icc", "panel.meanAbsDelta", "panel.disagreements", "panel.memberAAlone", "panel.memberBAlone", "panel.referenceReaderIndex" }
            .Where(key => OrderedPeers(ctx.Sheet).Any(p => IsAvailable(ctx, ModelKey(p.Letter, key))))
            .ToList();
        if (keys.Count == 0)
        {
            Line(sb, "No model was graded by an assessor panel in every run, so no panel agreement figures are stated.");
            Line(sb);
        }
        else
        {
            var header = ModelColumns(ctx);
            header.AddRange(keys.Select(Label));
            TableHeaderOf(sb, header);
            foreach (var peer in OrderedPeers(ctx.Sheet))
            {
                var cells = ModelCells(ctx, peer);
                cells.AddRange(keys.Select(k => Num(ctx, ModelKey(peer.Letter, k))));
                Line(sb, "| " + string.Join(" | ", cells) + " |");
            }
            Line(sb);
            if (keys.Contains("panel.referenceReaderIndex"))
            {
                Line(sb, "*" + Label("panel.referenceReaderIndex") + ": " + ReferenceReaderCaveat + "*");
                Line(sb);
            }
        }

        var conflicts = OrderedPeers(ctx.Sheet)
            .Select(p => (Peer: p, Fact: Fact(ctx, ModelKey(p.Letter, "style.responseStyleConflict"))))
            .Where(x => x.Fact is { Available: true } && BenchmarkReportPackPrompt.IsTrue(x.Fact))
            .ToList();
        Line(sb, "- **" + Label("style.responseStyleConflict") + ":** " + (conflicts.Count == 0
            ? "none"
            : string.Join("; ", conflicts.Select(c => ProseName(ctx, c.Peer) + ": " + Shown(ctx, c.Fact!)))));
        Line(sb);
    }

    private static void ComparisonSetup(StringBuilder sb, Context ctx)
    {
        var sheet = ctx.Sheet;
        var models = OrderedPeers(sheet);
        string first = models.Count > 0 ? models[0].Letter : "A";

        Heading(sb, "## Setup and method");
        Line(sb, "- **" + (IsBatteryComparison(ctx) ? "Battery" : "Suite") + ":** " + sheet.SuiteName + ", " + Inv(sheet.Questions.Count) + " questions in the per-question matrix.");
        Line(sb, "- **Coverage:** " + (CoverageText(ctx) ?? "every model of " + ComparisonNameText(ctx) + " that is not excluded."));
        Line(sb, "- **" + Label("config.chat") + ":** " + SharedOrPerModel(ctx, "config.chat"));
        Line(sb, "- **Grading:** each answer is graded on accuracy, completeness, conciseness and readability, weighted "
            + D(ctx, ModelKey(first, "scoring.weights")) + ". Each dimension is graded on behaviorally anchored levels scored "
            + D(ctx, ModelKey(first, "scoring.levels")) + ". A critical error caps the answer's quality at " + D(ctx, ModelKey(first, "scoring.criticalErrorCap")) + ".");

        if (sheet.Graders.Count == 0)
        {
            Line(sb, "- **Graders:** not recorded.");
        }
        else
        {
            Line(sb, "- **Graders:**");
            foreach (var grader in sheet.Graders)
            {
                string identity = GraderWithheld(ctx, grader) ? WithheldGrader : grader.Label + " (" + grader.Provider + ", " + grader.ModelId + ")";
                Line(sb, "  - " + grader.Role + ": " + identity + ", " + GraderRelationText(ctx, grader));
            }
        }

        Line(sb, "- **Formulas:** answer quality is the weighted geometric mean of the four dimension scores, capped by a "
            + "critical error; in a panel run it is the mean of both graders' scores. The Intelligence Index is the "
            + "difficulty-weighted mean of answer quality. Median answer time is the median model time per answer, with "
            + "tool time excluded. Cost per question is each model's own spend divided by the questions asked.");
        if (IsBatteryComparison(ctx))
        {
            Line(sb, "- **Battery composite:** each model's Overall Index is the sum over the suites of each suite's weight times its "
                + "Intelligence Index, under the battery's weighting scheme; it is not comparable with a single suite's index. "
                + "Cost per run is per battery pass, one run of every suite.");
        }
        Line(sb, "- **Comparability:** every model in this report was measured under one instrument condition, signature `" + D(ctx, "comparison.signature") + "`.");
        Line(sb, "- **" + Label("comparison.pricingBasis") + ":** " + PricingBasisText(sheet).Text);
        Line(sb, "- **Versions:** harness " + SharedOrPerModel(ctx, "run.harnessVersion") + "; scoring method " + SharedOrPerModel(ctx, "scoring.methodVersion") + ".");
        Line(sb);

        Heading(sb, "### Compared models");
        bool harness = sheet.Entries.Any(e => e.HarnessVersion != null);
        bool dates = sheet.Entries.Any(e => e.FirstRunUtc.HasValue || e.LastRunUtc.HasValue);
        var header = new List<string> { "Letter" };
        header.AddRange(ModelColumns(ctx));
        header.AddRange(new[] { "Kind", "Runs", "Thinking level" });
        if (harness) header.Add("Harness version");
        if (dates) header.Add("Run dates (UTC)");
        header.Add("Price card");
        TableHeaderOf(sb, header);
        foreach (var (entry, peer) in ComparisonRows(ctx))
        {
            var cells = new List<string> { peer.Letter };
            cells.AddRange(ModelCells(ctx, peer));
            cells.Add(entry.EntryKey.StartsWith("group:", StringComparison.Ordinal) ? "group"
                : entry.EntryKey.StartsWith("battery:", StringComparison.Ordinal) ? "battery" : "run");
            cells.Add(Inv(entry.RunCount));
            cells.Add(Cell(peer.ThinkingLevel ?? "not set"));
            if (harness) cells.Add(Cell(entry.HarnessVersion ?? NoValue));
            if (dates) cells.Add(RunDates(entry));
            cells.Add(PriceCardCell(ctx, peer));
            Line(sb, "| " + string.Join(" | ", cells) + " |");
        }
        Line(sb);
    }

    /// <summary>
    /// The price card a model's cost figures use: its card's date on the catalog basis, the prices stored
    /// with each run on the snapshot basis.
    /// </summary>
    private static string PriceCardCell(Context ctx, BenchmarkReportPeer peer)
    {
        if (PricingBasisKind(ctx) == BenchmarkReportFacts.PricingBasisSnapshot) return "prices stored with each run";
        return Fact(ctx, ModelKey(peer.Letter, "cost.pricingAsOf")) is { Available: true } card ? "dated " + Cell(card.Display) : NoValue;
    }

    /// <summary>A fact's value when every model shares it, else each model's value by name.</summary>
    private static string SharedOrPerModel(Context ctx, string key)
    {
        var models = OrderedPeers(ctx.Sheet);
        var values = models.Select(m => (Peer: m, Value: D(ctx, ModelKey(m.Letter, key)))).ToList();
        if (values.Count == 0) return BenchmarkReportFacts.NotAvailable;
        if (values.Select(v => v.Value).Distinct(StringComparer.Ordinal).Count() == 1) return values[0].Value;
        return string.Join("; ", values.Select(v => ProseName(ctx, v.Peer) + ": " + v.Value));
    }

    /// <summary>
    /// Whose provider a grader shares: <c>same provider as Model B</c>, else <c>a different provider
    /// from every model</c>.
    /// </summary>
    private static string GraderRelationText(Context ctx, BenchmarkReportGrader grader)
    {
        var sharing = OrderedPeers(ctx.Sheet)
            .Where(p => !string.IsNullOrWhiteSpace(grader.Provider)
                        && string.Equals(p.Provider?.Trim(), grader.Provider.Trim(), StringComparison.OrdinalIgnoreCase))
            .Select(p => ProseName(ctx, p))
            .ToList();
        return sharing.Count == 0
            ? "a different provider from every model"
            : "same provider as " + BenchmarkReportFormat.LetterList(sharing);
    }

    private static void ComparisonThreats(StringBuilder sb, Context ctx)
    {
        Heading(sb, "## Threats to validity");
        Line(sb, "- The benchmark asks single-turn questions under one chat configuration. It does not exercise conversation "
            + "history, pre-injected wiki context, spoiler-free mode, web search or subagents.");
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
        if (ComparisonWriterCaveat(ctx) is string caveat)
        {
            Line(sb, "- " + caveat);
        }
        Line(sb);

        OptionalSlot(sb, ctx, BenchmarkReportSlots.Limitations);
    }

    /// <summary>
    /// How many distinct questions the writer was given in full: <c>17 questions (8 of suite 1, 9 of suite 2)</c>,
    /// each suite of the matrix in suite order; without the breakdown when the matrix has one suite, and
    /// <c>at most a few questions per suite</c> when there are none.
    /// </summary>
    private static string DetailQuestionsText(Context ctx)
    {
        var numbers = (ctx.Content.Questions ?? new List<BenchmarkReportContentQuestion>())
            .Select(q => q.Number)
            .Distinct()
            .ToList();
        if (numbers.Count == 0) return "at most a few questions per suite";

        string total = Inv(numbers.Count) + (numbers.Count == 1 ? " question" : " questions");
        var suites = ctx.Sheet.Questions
            .Where(q => q.Suite.HasValue)
            .Select(q => q.Suite!.Value)
            .Distinct()
            .OrderBy(s => s)
            .ToList();
        if (suites.Count < 2) return total;

        var suiteOf = ctx.Sheet.Questions
            .Where(q => q.Suite.HasValue)
            .GroupBy(q => q.Number)
            .ToDictionary(g => g.Key, g => g.First().Suite!.Value);
        var perSuite = suites.Select(s => Inv(numbers.Count(n => suiteOf.TryGetValue(n, out int suite) && suite == s)) + " of suite " + Inv(s));
        return total + " (" + string.Join(", ", perSuite) + ")";
    }

    private static void ComparisonReproducibility(StringBuilder sb, Context ctx)
    {
        var (pricing, _) = PricingBasisText(ctx.Sheet);
        Heading(sb, "## Reproducibility appendix");
        foreach (var peer in OrderedPeers(ctx.Sheet))
        {
            string identity = ctx.Anonymized ? string.Empty : " (" + peer.Provider + " " + peer.ModelId + ", thinking level " + (peer.ThinkingLevel ?? "not set") + ")";
            Line(sb, "- **" + ProseName(ctx, peer) + "**" + identity + ": " + Label("run.ids").ToLowerInvariant() + " " + D(ctx, ModelKey(peer.Letter, "run.ids"))
                + "; " + Label("run.dates").ToLowerInvariant() + " " + D(ctx, ModelKey(peer.Letter, "run.dates"))
                + "; " + Label("run.promptSha256") + " `" + D(ctx, ModelKey(peer.Letter, "run.promptSha256")) + "`"
                + "; " + Label("run.toolGuidesSha256") + " `" + D(ctx, ModelKey(peer.Letter, "run.toolGuidesSha256")) + "`");
        }
        Line(sb, "- **Grader models:** " + (ctx.Sheet.Graders.Count == 0
            ? "not recorded"
            : string.Join("; ", ctx.Sheet.Graders.Select(g => g.Role + ": " + (GraderWithheld(ctx, g) ? WithheldGrader : g.Provider + " " + g.ModelId)
                + (string.IsNullOrWhiteSpace(g.ThinkingLevel) ? string.Empty : ", thinking level " + g.ThinkingLevel)))));
        Line(sb, "- **" + Label("comparison.signature") + ":** `" + D(ctx, "comparison.signature") + "`");
        Line(sb, "- **" + Label("comparison.pricingBasis") + ":** " + pricing);
        Line(sb);
    }

    /// <summary>
    /// The questions whose text the writer was given: the question, and at Full its rubric; then each
    /// answer the writer was given as an excerpt, under its model, as the excerpt at Detailed and whole
    /// at Full, with the graders and the claim verifier.
    /// </summary>
    private static void ComparisonQuestionDetails(StringBuilder sb, Context ctx, bool grading, int level)
    {
        string prefix = new('#', level);
        var questions = (ctx.Content.Questions ?? new List<BenchmarkReportContentQuestion>()).OrderBy(q => q.Number).ToList();
        if (questions.Count == 0) return;

        Heading(sb, prefix + (grading ? " Question details" : " Questions and answers"));
        Line(sb, "*Each answer shown is one the writer was given as an excerpt; the excerpts were chosen for the questions with critical errors or refuted sentences first, then the widest spread between models, then those every model scored low.*");
        Line(sb);

        foreach (var question in questions)
        {
            string? topic = Topic(ctx, question.Number);
            Heading(sb, prefix + "# " + ctx.QuestionLabel(question.Number) + (topic != null ? ": " + topic : string.Empty));
            Line(sb, "**Question:**");
            Line(sb);
            Quote(sb, question.QuestionText);
            Line(sb);

            if (grading)
            {
                Line(sb, "**Rubric:**");
                Line(sb);
                if (!question.ExpectedPointsRecorded) Line(sb, "*Rubric not recorded for this answer.*");
                else if (string.IsNullOrWhiteSpace(question.ExpectedPoints)) Line(sb, "*No rubric points.*");
                else Quote(sb, question.ExpectedPoints);
                Line(sb);
            }

            var answers = ctx.Content.Runs
                .Where(r => r.Questions.Any(q => q.Number == question.Number))
                .OrderBy(r => (r.Letter ?? string.Empty).Length)
                .ThenBy(r => r.Letter ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(r => r.RunId)
                .ToList();
            if (answers.Count == 0)
            {
                Line(sb, "*No answer to this question was given as an excerpt.*");
                Line(sb);
                continue;
            }

            foreach (var run in answers)
            {
                var item = run.Questions.First(q => q.Number == question.Number);
                var peer = ctx.Sheet.Peers.FirstOrDefault(p => string.Equals(p.Letter, run.Letter, StringComparison.Ordinal));
                Heading(sb, prefix + "## " + (peer != null ? ProseName(ctx, peer) : "Model " + run.Letter) + ", run " + Inv(run.RunId));

                if (!grading)
                {
                    Line(sb, "**Answer excerpt:**");
                    Line(sb);
                    Quote(sb, item.AnswerExcerpt);
                    Line(sb);
                    continue;
                }

                Line(sb, "**Answer:**");
                Line(sb);
                Quote(sb, item.AnswerText ?? item.AnswerExcerpt);
                Line(sb);

                Line(sb, "**Graders:**");
                Line(sb);
                if (item.Graders.Count == 0) Line(sb, "- No grader verdict was recorded.");
                foreach (var grader in item.Graders)
                {
                    var graderOnSheet = ctx.Sheet.Graders.FirstOrDefault(g => string.Equals(g.Role, grader.Role, StringComparison.Ordinal));
                    Line(sb, "- **" + grader.Role + " (" + (GraderWithheld(ctx, graderOnSheet) ? WithheldGrader : grader.Label) + "):** "
                        + (grader.Score.HasValue ? "score " + Inv(grader.Score.Value) + "." : "not scored.")
                        + (string.IsNullOrWhiteSpace(grader.Comment) ? string.Empty : " " + OneLine(grader.Comment)));
                    foreach (var evidence in grader.Evidence)
                    {
                        Line(sb, "  - " + OneLine(evidence));
                    }
                }
                Line(sb);

                Line(sb, "**Claim verifier:**");
                Line(sb);
                if (item.ClaimRulings.Count == 0) Line(sb, "- No claims were checked.");
                foreach (var ruling in item.ClaimRulings)
                {
                    Line(sb, "- **" + BenchmarkReportContent.RulingLabel(ruling.Role, ruling.Verdict) + ":** \"" + OneLine(ruling.Claim) + "\""
                        + (string.IsNullOrWhiteSpace(ruling.Rationale) ? string.Empty : " — " + OneLine(ruling.Rationale)));
                }
                Line(sb);
            }
        }
    }

    private static void ComparisonAboutBenchmark(StringBuilder sb, Context ctx)
    {
        var graders = ctx.Sheet.Graders;
        var memberA = graders.FirstOrDefault(g => g.Role == BenchmarkReportFacts.PanelMemberARole);
        var memberB = graders.FirstOrDefault(g => g.Role == BenchmarkReportFacts.PanelMemberBRole);
        var assessor = graders.FirstOrDefault(g => g.Role == BenchmarkReportFacts.AssessorRole);
        bool verifier = graders.Any(g => g.Role == BenchmarkReportFacts.ClaimVerifierRole);

        string grading = memberA != null && memberB != null
            ? (string.Equals(memberA.Provider, memberB.Provider, StringComparison.OrdinalIgnoreCase)
                ? "Two AI graders score every answer"
                : "Two AI graders from two different companies score every answer")
            : "An AI grader scores every answer";

        var sameCompany = new[] { memberA ?? assessor, memberB }
            .Where(g => g != null)
            .Select(g => (Grader: g!, Models: OrderedPeers(ctx.Sheet)
                .Where(p => string.Equals(p.Provider?.Trim(), g!.Provider?.Trim(), StringComparison.OrdinalIgnoreCase))
                .Select(p => ProseName(ctx, p))
                .ToList()))
            .Where(x => x.Models.Count > 0)
            .Select(x => "One grader is from the same company as " + BenchmarkReportFormat.LetterList(x.Models) + ", which it may read more favorably.")
            .Distinct(StringComparer.Ordinal)
            .ToList();

        string cost = PricingBasisKind(ctx) switch
        {
            BenchmarkReportFacts.PricingBasisCatalog => "Cost is each model's price per question at the catalog prices"
                + (IsAvailable(ctx, "comparison.pricedOn") ? " of " + D(ctx, "comparison.pricedOn") : string.Empty)
                + ", which is comparable across dates but is not what was actually spent.",
            BenchmarkReportFacts.PricingBasisSnapshot => "Cost is what each model's answers actually cost, at the prices stored with each run.",
            _ => "Cost is each model's list price, on this basis: " + D(ctx, "comparison.pricingBasis")
        };

        Heading(sb, "## About this benchmark");
        Line(sb, "GnollHack is a roguelike game descended from NetHack. The Overseer is its AI assistant: players ask it "
            + "questions about the game, and it answers with the help of tools that search the game's source code, its "
            + "wiki and a knowledge base.");
        Line(sb);
        Line(sb, "This benchmark gives the production assistant prompt and tools, unchanged, a fixed set of single-turn "
            + "questions about the game, and asks for the concise answer style the live assistant uses. Every model "
            + "compared here answered the same questions under the same configuration.");
        Line(sb);
        Line(sb, grading + " for accuracy, completeness, conciseness and readability, weighted "
            + D(ctx, ModelKey(OrderedPeers(ctx.Sheet).FirstOrDefault()?.Letter ?? "A", "scoring.weights")) + "."
            + (sameCompany.Count > 0 ? " " + string.Join(" ", sameCompany) : string.Empty)
            + (verifier ? " A separate verifier checks disputed claims against the game's source code." : string.Empty)
            + " Speed is each model's own time per answer, with time spent in tools excluded. " + cost);
        Line(sb);
        Line(sb, "What it does not measure: conversations longer than one question, the wiki text the live assistant is "
            + "given before it answers, spoiler-free mode, web search, and delegation to subagents. A result here "
            + "describes the assistant as configured for this benchmark; it may not carry over to those situations.");
        Line(sb);
    }

    /// <summary>
    /// The evaluation terms of a comparison-scope document: the purpose statements, the distillation
    /// prohibition, and whose content it rests on, the models named in a named copy only.
    /// </summary>
    private static void ComparisonEvaluationTerms(StringBuilder sb, Context ctx)
    {
        var purposes = (ctx.Sheet.PurposeStatements ?? new List<string>())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => SheetText(ctx, p))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Heading(sb, "## Evaluation terms");
        if (purposes.Count == 0) Line(sb, "- **Purpose statement:** not recorded for this document.");
        else if (purposes.Count == 1) Line(sb, "- **Purpose statement:** " + purposes[0]);
        else
        {
            Line(sb, "- **Purpose statements:**");
            foreach (var purpose in purposes) Line(sb, "  - " + purpose);
        }

        Line(sb, "- **Distillation / training prohibition:** " + DistillationProhibition);

        var graderProviders = ctx.Sheet.Graders
            .Select(g => g.Provider?.Trim() ?? string.Empty)
            .Where(p => p.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(p => GraderProviderText(ctx, p))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        string graded = graderProviders.Count == 0 ? "graded by AI models" : "graded by models from " + BenchmarkReportFormat.LetterList(graderProviders);

        string models = ctx.Anonymized
            ? "the compared models (identities withheld)"
            : BenchmarkReportFormat.LetterList(OrderedPeers(ctx.Sheet).Select(p => "**" + p.Label + "** (" + p.Provider + ")").ToList());
        Line(sb, "- **Third-party model content:** Outputs generated by " + models + ", " + graded + ", and described in this document by **"
            + ctx.Document.WriterDisplayName + "** (" + ctx.Document.WriterProvider + ") are third-party content evaluated solely for "
            + "domain-specific benchmark scoring and operational model selection.");
        Line(sb);
    }

    /// <summary>
    /// The caveat of a document whose writer shares a covered model's provider: named, which models; anonymized,
    /// that one of them does, without naming it. Null for an independent writer.
    /// </summary>
    private static string? ComparisonWriterCaveat(Context ctx)
    {
        string writer = ctx.Document.WriterProvider?.Trim() ?? string.Empty;
        if (writer.Length == 0) return null;

        var sharing = OrderedPeers(ctx.Sheet)
            .Where(p => string.Equals(p.Provider?.Trim(), writer, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (sharing.Count == 0) return null;

        return ctx.Anonymized
            ? "This document was written by a model from the same provider as one of the compared models, which it may describe more favorably; the figures and tables were computed by Overseer, not by the writer."
            : "This document was written by " + ctx.Document.WriterDisplayName + ", a model from " + writer + ", the provider of "
                + BenchmarkReportFormat.LetterList(sharing.Select(p => p.Label).ToList())
                + ". A writer from the same provider may describe a model more favorably; the figures and tables were computed by Overseer, not by the writer.";
    }

    /// <summary>A copy of the sheet with every covered model's name, model id and provider replaced by its letter.</summary>
    private static BenchmarkReportFactSheet ComparisonAnonymizedSheet(Context ctx)
    {
        var copy = AnonymizedSheet(ctx);
        foreach (var model in copy.Models ?? new List<BenchmarkReportComparisonModel>())
        {
            model.Label = "Model " + model.Letter;
            model.Provider = string.Empty;
        }
        return copy;
    }

    // ---------------------------------------------------------------------------------------------
    // Building blocks
    // ---------------------------------------------------------------------------------------------

    private static string ModelKey(string letter, string key) => BenchmarkComparisonReportFacts.ModelPrefix(letter) + key;

    /// <summary>The covered models' rows in letter order, each with its model.</summary>
    private static List<(BenchmarkReportEntryFigures Entry, BenchmarkReportPeer Peer)> ComparisonRows(Context ctx)
        => OrderedPeers(ctx.Sheet)
            .Select(p => (Entry: ctx.Sheet.Entries.FirstOrDefault(e => string.Equals(e.EntryKey, p.EntryKey, StringComparison.Ordinal)), Peer: p))
            .Where(x => x.Entry != null)
            .Select(x => (x.Entry!, x.Peer))
            .ToList();

    /// <summary>The columns naming a model: Model and Provider in a named copy, Model alone in an anonymized one.</summary>
    private static List<string> ModelColumns(Context ctx) => ctx.Anonymized ? new List<string> { "Model" } : new List<string> { "Model", "Provider" };

    private static List<string> ModelCells(Context ctx, BenchmarkReportPeer peer)
        => ctx.Anonymized ? new List<string> { "Model " + peer.Letter } : new List<string> { Cell(peer.Label), Cell(peer.Provider) };

    private static void TableHeaderOf(StringBuilder sb, IReadOnlyList<string> columns)
    {
        Line(sb, "| " + string.Join(" | ", columns) + " |");
        Line(sb, "|" + string.Concat(columns.Select(_ => "---|")));
    }

    private static string TimeCell(BenchmarkReportEntryFigures entry)
        => !entry.SpeedDegraded && entry.ModelTimeP50Ms.HasValue ? BenchmarkReportFormat.Seconds(entry.ModelTimeP50Ms.Value) : BenchmarkReportFacts.NotAvailable;

    private static string CostCell(BenchmarkReportEntryFigures entry)
        => !entry.CostDegraded && entry.CostPerQuestionUsd.HasValue ? BenchmarkReportFormat.Usd(entry.CostPerQuestionUsd.Value) : BenchmarkReportFacts.NotAvailable;

    /// <summary>A model by letter as prose names it: its label when named, <c>Model A</c> when anonymized or unknown.</summary>
    private static string NameOfLetter(Context ctx, string letter)
    {
        var peer = ctx.Sheet.Peers.FirstOrDefault(p => string.Equals(p.Letter, letter, StringComparison.Ordinal));
        return peer == null ? "Model " + letter : ProseName(ctx, peer);
    }

    /// <summary><c>Comparison #12</c>, with its name in a named copy; <c>the comparison</c> without a number.</summary>
    private static string ComparisonNameText(Context ctx)
        => BenchmarkPdfDocumentInfo.ComparisonOf(ctx.Document) is { } comparison
            ? BenchmarkPdfDocumentInfo.ComparisonHeading(comparison, ctx.Options.PeerNaming)
            : "the comparison";

    /// <summary>The covered models are battery results: their questions carry suite-qualified references.</summary>
    private static bool IsBatteryComparison(Context ctx) => ctx.Sheet.Questions.Any(q => !string.IsNullOrWhiteSpace(q.Reference));
}
