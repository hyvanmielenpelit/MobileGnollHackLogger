namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services.Benchmarking.Pdf;

/// <summary>
/// The comparison-scope documents (<see cref="BenchmarkReportFactSheet.IsComparison"/>): every covered
/// model described as an equal, in letter order, with no subject. A named copy prints each model's
/// label and provider and no letter in its prose; tables that list the models carry a Letter column
/// under both namings, so a named and an anonymized copy can be matched. A copy covering a subset of
/// the comparison says so, and counts the comparison's other models without naming them. From format
/// version 14 a document also sets every model against its reference model, marking the reference and
/// the baseline model in its tables and lists the notable findings code detected; a document stored
/// earlier renders without them.
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
            case BenchmarkReportAudience.RootCauseAnalysis:
                RenderRootCauseAnalysis(sb, ctx);
                break;
            default:
                throw new NotSupportedException($"A {AudienceName(ctx.Document.Audience)} is not written for comparison documents.");
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

        ReferenceSection(sb, ctx, "## ", full: false);
        AlsoWorthKnowing(sb, ctx);

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

    // ---------------------------------------------------------------------------------------------
    // Sections
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The comparison's name as the title (<see cref="BenchmarkPdfDocumentInfo.ComparisonDocumentHeading"/>), then, with
    /// <see cref="BenchmarkReportRenderOptions.IncludeFrontMatter"/>, the document type as its kicker above it, the
    /// comparison line with only what the title does not say (<see cref="BenchmarkPdfDocumentInfo.ComparisonDetailText"/>),
    /// the stamp and the facts list: the suite or battery, the questions, the models, the coverage of a subset and
    /// the pricing basis. Without the front matter the title alone opens the document, as the PDF and Word cover
    /// print the document type.
    /// </summary>
    private static void ComparisonTitleBlock(StringBuilder sb, Context ctx)
    {
        var sheet = ctx.Sheet;
        var comparison = BenchmarkPdfDocumentInfo.ComparisonOf(ctx.Document);
        string audience = AudienceName(ctx.Document.Audience);
        string heading = BenchmarkPdfDocumentInfo.ComparisonDocumentHeading(
            BuildComparisonTitle(ctx.Document.Audience, sheet, comparison, ctx.Options.PeerNaming), audience);
        if (ctx.Options.IncludeFrontMatter)
        {
            Line(sb, "**" + audience.ToUpperInvariant() + "**");
            Line(sb);
        }
        Line(sb, "# " + heading);
        Line(sb);
        if (!ctx.Options.IncludeFrontMatter) return;

        if (comparison != null && BenchmarkPdfDocumentInfo.ComparisonDetailText(
                comparison, BenchmarkPdfDocumentInfo.ComparisonComputedOn(sheet), ctx.Options.PeerNaming, heading) is { Length: > 0 } detail)
        {
            Line(sb, "**Comparison:** " + detail);
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

    /// <summary>Every covered model's Intelligence Index with its interval and tied rank, median answer time, cost per question and critical errors.</summary>
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
        Line(sb, "*Each model's Intelligence Index is followed by its 95% interval. A rank shown as a span, such as 5–9 (tied), means the model's "
            + "interval overlaps others', so its rank is the span of places the interval allows. " + RoundedBoundsNote + "*");
        Line(sb);
    }

    private const string RoundedBoundsNote = "Interval bounds are rounded to whole numbers, so two intervals printed as touching may not overlap; "
        + "ranks use the unrounded bounds.";

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

    /// <summary>How many pairs of models have overlapping 95% intervals.</summary>
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
                ? "The two models' 95% intervals overlap; the intervals alone do not establish which scored higher."
                : "The two models' 95% intervals do not overlap.");
            Line(sb);
            return;
        }

        Line(sb, "Of the " + Inv(overlaps.Count) + (overlaps.Count == 1 ? " pair" : " pairs") + " of models, " + Inv(overlapping)
            + (overlapping == 1 ? " has" : " have") + " overlapping 95% intervals"
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
                : NameOfLetter(ctx, family.ReferenceLetter ?? "A") + ", " + ReferenceRole(ctx, family.ReferenceLetter ?? "A") + ", was compared with each other model";
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

    /// <summary>
    /// Each paired family <paramref name="include"/> keeps, with its tables: Intelligence, then speed and cost, each pair
    /// with its result after the family's adjustment. When the sheet has families but none is kept, <paramref name="pointer"/>
    /// alone says where they are printed.
    /// </summary>
    private static void PairedTestsSection(StringBuilder sb, Context ctx, string heading, Func<BenchmarkReportPairedFamily, bool> include, string pointer)
    {
        Heading(sb, heading);
        var all = ctx.Sheet.PairedTests ?? new List<BenchmarkReportPairedFamily>();
        if (all.Count == 0)
        {
            Line(sb, "No pair of models was tested" + (string.IsNullOrWhiteSpace(ctx.Sheet.PairedTestsUnavailableReason) ? "." : ": " + SheetText(ctx, ctx.Sheet.PairedTestsUnavailableReason))
                + " A gap between two models may be noise.");
            Line(sb);
            return;
        }

        var families = all.Where(include).ToList();
        if (families.Count == 0)
        {
            Line(sb, pointer);
            Line(sb);
            return;
        }

        Line(sb, "*" + PairedTestsNote + "*");
        Line(sb);
        UseTerms(ctx, RootCause.RootCauseGlossary.PairedTest, RootCause.RootCauseGlossary.PValue, RootCause.RootCauseGlossary.Holm,
            RootCause.RootCauseGlossary.Interval95);

        foreach (var family in families)
        {
            string title = family.Mode == "AllPairs"
                ? "Every pair"
                : "Against " + NameOfLetter(ctx, family.ReferenceLetter ?? "A")
                  + (ctx.Sheet.ReferenceIsBaselineModel == true ? " (the reference, the baseline model)" : " (the reference)");
            Line(sb, "**" + title + "**");
            Line(sb);

            var intelligence = family.Measures.FirstOrDefault(m => m.Measure == BenchmarkPairedTests.IntelligenceMeasure);
            if (intelligence != null)
            {
                Line(sb, Cell(FamilySizeText(ctx, "Intelligence", intelligence)));
                Line(sb);
                Line(sb, "| Pair | Questions | Difference (95% interval) | Adjusted p | Result |");
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
                UseTerms(ctx, RootCause.RootCauseGlossary.Ratio);
                Line(sb, "| Pair | Time ratio (95% interval) | Speed result | Spend ratio (95% interval) | Cost result |");
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

    /// <summary>
    /// <c>Intelligence: Holm-adjusted across 8 tests.</c>, with the Holm adjustment glossed at its first use in a
    /// Root Cause Analysis; a single test, no test, or why the measure was not tested.
    /// </summary>
    private static string FamilySizeText(Context ctx, string measure, BenchmarkReportPairedMeasure family)
        => family.NotTestedReason != null
            ? measure + ": not tested. " + SheetText(ctx, family.NotTestedReason)
            : measure + ": " + (family.FamilySize > 1
                ? "Holm-adjusted across " + Inv(family.FamilySize) + " tests" + Gloss(ctx, RootCause.RootCauseGlossary.Holm) + "."
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
        _ => BenchmarkReportFactLabels.ModelSuffixLabel(rest)
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

    /// <summary>The setup and method bullets with the graders, then, with <paramref name="comparedModels"/>, the compared models' table.</summary>
    private static void ComparisonSetup(StringBuilder sb, Context ctx, string heading = "## Setup and method", bool comparedModels = true)
    {
        var sheet = ctx.Sheet;
        var models = OrderedPeers(sheet);
        string first = models.Count > 0 ? models[0].Letter : "A";

        Heading(sb, heading);
        Line(sb, "- **" + (IsBatteryComparison(ctx) ? "Battery" : "Suite") + ":** " + sheet.SuiteName + ", " + Inv(sheet.Questions.Count) + " questions in the per-question matrix.");
        Line(sb, "- **Coverage:** " + (CoverageText(ctx) ?? "every non-excluded model of " + ComparisonNameText(ctx) + "."));
        Line(sb, "- **" + Label("config.chat") + ":** " + SharedOrPerModel(ctx, "config.chat"));
        Line(sb, GradingLine(ctx, D(ctx, ModelKey(first, "scoring.weights")), D(ctx, ModelKey(first, "scoring.levels")),
            D(ctx, ModelKey(first, "scoring.criticalErrorCap"))));

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

        Line(sb, FormulasLine(ctx, "Cost per question is each model's own spend divided by the questions asked."));
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

        if (comparedModels) ComparisonModelsTable(sb, ctx, "### Compared models");
    }

    /// <summary>One row per covered model: its letter, name, kind, runs, thinking level, harness version, run dates and price card.</summary>
    private static void ComparisonModelsTable(StringBuilder sb, Context ctx, string heading)
    {
        var sheet = ctx.Sheet;
        Heading(sb, heading);
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

    private static void ComparisonReproducibility(StringBuilder sb, Context ctx, string heading = "## Reproducibility appendix")
    {
        var (pricing, _) = PricingBasisText(ctx.Sheet);
        Heading(sb, heading);
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
    /// The questions whose text the writer was given with at least one answer: the question, and with
    /// <paramref name="grading"/> its rubric without its <c>SOURCE</c> paragraphs; then each answer the writer was
    /// given, under its model, as its excerpt, with its critical-error resolution where one is stored and, with
    /// <paramref name="grading"/>, the graders and the claim verifier. A grader's no-fault evidence line is left out,
    /// and the answer sentences the verifier supported are counted rather than listed. <paramref name="only"/>, when
    /// given, keeps the questions it holds.
    /// </summary>
    private static void ComparisonQuestionDetails(
        StringBuilder sb, Context ctx, bool grading, int level, IReadOnlySet<int>? only = null, string? title = null)
    {
        string prefix = new('#', level);
        var questions = (ctx.Content.Questions ?? new List<BenchmarkReportContentQuestion>())
            .Where(q => only == null || only.Contains(q.Number))
            .Where(q => ctx.Content.Runs.Any(r => r.Questions.Any(a => a.Number == q.Number)))
            .OrderBy(q => q.Number)
            .ToList();
        if (questions.Count == 0) return;

        Heading(sb, prefix + " " + (title ?? (grading ? "Question details" : "Questions and answers")));
        Line(sb, ctx.Document.ReportFormatVersion >= RoundExcerptsFormatVersion
            ? "*Each answer shown is one the writer was given as an excerpt. The excerpts were given in rounds, one per question in each round and at most "
              + "three per question, taking the suites in turn: the questions with critical errors or refuted sentences first, then the widest spread "
              + "between models, then those every model scored low; within a question, a critical error or refuted sentence first, then the lowest score."
              + " Rubrics are shown without their source paragraphs.*"
            : "*Each answer shown is one the writer was given as an excerpt; the excerpts were chosen for the questions with critical errors or refuted sentences first, then the widest spread between models, then those every model scored low.*");
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
                else Quote(sb, BenchmarkReportPackPrompt.WithoutSourceParagraphs(question.ExpectedPoints));
                Line(sb);
            }

            var answers = ctx.Content.Runs
                .Where(r => r.Questions.Any(q => q.Number == question.Number))
                .OrderBy(r => (r.Letter ?? string.Empty).Length)
                .ThenBy(r => r.Letter ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(r => r.RunId)
                .ToList();
            foreach (var run in answers)
            {
                var item = run.Questions.First(q => q.Number == question.Number);
                var peer = ctx.Sheet.Peers.FirstOrDefault(p => string.Equals(p.Letter, run.Letter, StringComparison.Ordinal));
                Heading(sb, prefix + "## " + (peer != null ? ProseName(ctx, peer) : "Model " + run.Letter) + ", run " + Inv(run.RunId));

                AnswerExcerptBlock(sb, item);
                CriticalErrorResolutionLine(sb, item);
                if (!grading) continue;

                Line(sb, "**Graders:**");
                Line(sb);
                if (item.Graders.Count == 0) Line(sb, "- No grader verdict was recorded.");
                foreach (var grader in item.Graders)
                {
                    var graderOnSheet = ctx.Sheet.Graders.FirstOrDefault(g => string.Equals(g.Role, grader.Role, StringComparison.Ordinal));
                    Line(sb, "- **" + grader.Role + " (" + (GraderWithheld(ctx, graderOnSheet) ? WithheldGrader : grader.Label) + "):** "
                        + (grader.Score.HasValue ? "score " + Inv(grader.Score.Value) + "." : "not scored.")
                        + (string.IsNullOrWhiteSpace(grader.Comment) ? string.Empty : " " + OneLine(grader.Comment)));
                    foreach (var evidence in grader.Evidence.Where(e => !IsNoFaultEvidence(e)))
                    {
                        Line(sb, "  - " + OneLine(evidence));
                    }
                }
                Line(sb);

                Line(sb, "**Claim verifier:**");
                Line(sb);
                if (item.ClaimRulings.Count == 0) Line(sb, "- No claims were checked.");
                int supported = 0;
                foreach (var ruling in item.ClaimRulings)
                {
                    if (IsSupportedAnswerSentence(ruling))
                    {
                        supported++;
                        continue;
                    }
                    Line(sb, "- **" + BenchmarkReportContent.RulingLabel(ruling.Role, ruling.Verdict) + ":** \"" + OneLine(ruling.Claim) + "\""
                        + (string.IsNullOrWhiteSpace(ruling.Rationale) ? string.Empty : " — " + OneLine(ruling.Rationale)));
                }
                if (supported > 0)
                {
                    Line(sb, "- " + Inv(supported) + (supported < item.ClaimRulings.Count ? " further" : string.Empty)
                        + (supported == 1 ? " answer sentence" : " answer sentences") + " supported.");
                }
                Line(sb);
            }
        }
    }

    /// <summary>A grader's evidence line that records no fault: <c>Accuracy: Matches rubric.</c>, ignoring case.</summary>
    private const string NoFaultEvidencePattern = @"^[\p{L}][\p{L} ]*:\s*matches rubric\.$";

    private static bool IsNoFaultEvidence(string evidence)
        => Regex.IsMatch(OneLine(evidence).Trim(), NoFaultEvidencePattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>A ruling that the answer's own sentence is supported, on no grader's accusation.</summary>
    private static bool IsSupportedAnswerSentence(BenchmarkReportContentClaimRuling ruling)
        => string.Equals((ruling.Verdict ?? string.Empty).Trim(), "supported", StringComparison.Ordinal)
           && ruling.Role is null or BenchmarkReportContent.ClaimRole;

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

    // ---------------------------------------------------------------------------------------------
    // The reference, notable findings and failure profiles (format version 14)
    // ---------------------------------------------------------------------------------------------

    private const string ReferenceTableNote = "Paired difference: the mean per-question difference, the model minus the reference, on the "
        + "questions both answered, with its 95% interval. Better, similar and worse count the questions both scored on the same item "
        + "revision on which the model scored more than 15 points above the reference, within 15 points of it, or more than 15 points "
        + "below it; they are not tests. May mislead per 20: answers with an accuracy level below 4 or a critical error, per 20 answers. "
        + "Time and cost ratios: the model's median answer time and cost per question divided by the reference's.";

    private const string FailureProfileOutcomesNote = "Counted over each model's scored answers in all its runs; in a panel run a level is "
        + "the mean of both panel members' levels. May mislead: an accuracy level below 4 or a critical error. Outcomes are recorded from "
        + "scoring method 13. Low levels: answers at level 2 or below on each dimension.";

    private const string FailureProfileSignalsNote = "Output defects: an empty, truncated or scrubbed answer, reasoning bleed, repeated "
        + "fragments, or an answer that did not complete normally. Tool-limit hits: the tool budget or the iteration limit was reached. An "
        + "isolated miss is a question the model scored 50 or less where the other models' median is 75 or more; a shared miss is one it "
        + "scored below 50 along with at least half the others. Lowest scorer: questions where no other model scored lower. The counts say "
        + "what the graders recorded, not why.";

    private const string FindingsTableNote = "Of each model's runs whose final synthesis holds structured findings, how many raised a weakness "
        + "of each category, by either panel member. A category says what the graders charged, not why.";

    /// <summary>The sheet keeps a reference model's facts (format version 14 and later).</summary>
    private static bool HasReference(Context ctx) => Fact(ctx, "comparison.reference") != null;

    /// <summary>Some covered model has failure profile facts (format version 14 and later).</summary>
    private static bool HasProfiles(Context ctx)
        => OrderedPeers(ctx.Sheet).Any(p => Fact(ctx, ModelKey(p.Letter, "profile.answers")) != null);

    /// <summary>
    /// <c>Compared with the baseline model</c> (<c>… the reference model</c> without one): the reference,
    /// then a table of every covered model against it, the reference first, and the writer's slot.
    /// <paramref name="full"/> adds the questions both answered and the paired result. Nothing on a sheet
    /// that keeps no reference.
    /// </summary>
    private static void ReferenceSection(StringBuilder sb, Context ctx, string headingPrefix, bool full)
    {
        if (!HasReference(ctx)) return;

        string reference = ctx.Sheet.ReferenceLetter ?? OrderedPeers(ctx.Sheet).FirstOrDefault()?.Letter ?? "A";
        Heading(sb, headingPrefix + (ctx.Sheet.ReferenceIsBaselineModel == true ? "Compared with the baseline model" : "Compared with the reference model"));
        Line(sb, "**Reference:** " + D(ctx, "comparison.reference") + ".");
        Line(sb);

        var header = ModelColumns(ctx);
        if (full) header.Add("Questions");
        header.Add("Paired difference (95% interval)");
        if (full) header.Add("Result");
        header.AddRange(new[] { "Better / similar / worse", "May mislead per 20", "Time ratio", "Cost ratio" });
        TableHeaderOf(sb, header);

        var rows = ComparisonRows(ctx);
        var referenceEntry = rows.FirstOrDefault(r => r.Peer.Letter == reference).Entry;
        foreach (var (entry, peer) in rows.OrderBy(r => r.Peer.Letter == reference ? 0 : 1))
        {
            bool isReference = peer.Letter == reference;
            var pair = isReference ? null : ReferencePair(ctx, BenchmarkPairedTests.IntelligenceMeasure, peer.Letter, reference);
            string prefix = ModelKey(peer.Letter, "vsReference.");

            var cells = ModelCells(ctx, peer);
            if (full) cells.Add(isReference ? NoValue : NumberOf(Fact(ctx, prefix + "shared")) is double shared ? Inv((int)shared) : NoValue);
            cells.Add(isReference ? NoValue : DifferenceToReference(pair, peer.Letter));
            if (full) cells.Add(isReference || pair == null ? NoValue : Cell(ResultText(ctx, pair, "scored higher")));
            cells.Add(isReference ? NoValue : BetterSimilarWorse(ctx, prefix));
            cells.Add(NumberOf(Fact(ctx, ModelKey(peer.Letter, "profile.mayMisleadPer20"))) is double per20 ? BenchmarkReportFormat.CompactDecimal(per20) : NoValue);
            cells.Add(RatioToReference(Speed(entry), referenceEntry == null ? null : Speed(referenceEntry)));
            cells.Add(RatioToReference(Cost(entry), referenceEntry == null ? null : Cost(referenceEntry)));
            Line(sb, "| " + string.Join(" | ", cells) + " |");
        }
        Line(sb);
        Line(sb, "*" + ReferenceTableNote + "*");
        Line(sb);

        OptionalSlot(sb, ctx, BenchmarkReportSlots.AgainstReference);
    }

    /// <summary>The reference family's test of one measure between a model and the reference, either way round; null when it has none.</summary>
    private static BenchmarkReportPairedTest? ReferencePair(Context ctx, string measure, string letter, string reference)
        => (ctx.Sheet.PairedTests ?? new List<BenchmarkReportPairedFamily>())
            .Where(f => f.Mode != "AllPairs")
            .SelectMany(f => f.Measures.Where(m => m.Measure == measure))
            .SelectMany(m => m.Pairs)
            .FirstOrDefault(p => (p.FirstLetter == letter && p.SecondLetter == reference) || (p.FirstLetter == reference && p.SecondLetter == letter));

    /// <summary>A pair's difference turned to read the model minus the reference: <c>-4.6 (-9.8 to +0.7)</c>.</summary>
    private static string DifferenceToReference(BenchmarkReportPairedTest? pair, string letter)
    {
        if (pair?.Effect is not double effect) return NoValue;

        bool first = pair.FirstLetter == letter;
        double value = first ? effect : -effect;
        string text = BenchmarkReportFormat.SignedOneDecimal(value);
        if (pair.Lower is double lower && pair.Upper is double upper)
        {
            var (low, high) = first ? (lower, upper) : (-upper, -lower);
            text += " (" + BenchmarkReportFormat.SignedOneDecimal(low) + " to " + BenchmarkReportFormat.SignedOneDecimal(high) + ")";
        }
        return text;
    }

    /// <summary><c>3 / 10 / 2</c> from a model's <c>vsReference.better</c>, <c>.similar</c> and <c>.worse</c> facts.</summary>
    private static string BetterSimilarWorse(Context ctx, string prefix)
    {
        var counts = new[] { "better", "similar", "worse" }.Select(k => NumberOf(Fact(ctx, prefix + k))).ToList();
        return counts.All(c => c.HasValue) ? string.Join(" / ", counts.Select(c => Inv((int)c!.Value))) : NoValue;
    }

    /// <summary>The model's figure divided by the reference's, <c>0.45×</c>; <see cref="NoValue"/> when either is missing.</summary>
    private static string RatioToReference(double? own, double? reference)
        => own is double o && reference is double r && r > 0
            ? Math.Round(o / r, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture) + "×"
            : NoValue;

    private static double? Speed(BenchmarkReportEntryFigures entry) => entry.SpeedDegraded ? null : entry.ModelTimeP50Ms;

    private static double? Cost(BenchmarkReportEntryFigures entry) => entry.CostDegraded ? null : entry.CostPerQuestionUsd;

    /// <summary>The first three notable findings as code wrote them; nothing when there is none.</summary>
    private static void AlsoWorthKnowing(StringBuilder sb, Context ctx)
    {
        var findings = ctx.Sheet.NotableFindings;
        if (findings is not { Count: > 0 }) return;

        Heading(sb, "## Also worth knowing");
        foreach (var finding in findings.OrderBy(f => f.Number).Take(3))
        {
            Line(sb, "- " + NotableText(ctx, finding));
        }
        Line(sb);
    }

    /// <summary>A notable finding's sentence as this copy prints it, from its <c>notable.&lt;n&gt;.summary</c> fact.</summary>
    private static string NotableText(Context ctx, BenchmarkReportNotableFinding finding)
        => Fact(ctx, "notable." + Inv(finding.Number) + ".summary") is { Available: true } fact
            ? Shown(ctx, fact)
            : SheetText(ctx, finding.Summary);

    /// <summary>
    /// Each covered model's failure profile counts, one row per model, as two tables of at most nine columns
    /// each so that both stay readable on a printed page: the outcomes, then the failure signals, each under
    /// a bold lead-in and followed by the part of the note it needs.
    /// </summary>
    private static void FailureProfileTable(StringBuilder sb, Context ctx)
    {
        Line(sb, "**Outcomes**");
        Line(sb);
        var outcomes = ModelColumns(ctx);
        outcomes.AddRange(new[]
        {
            "Answers", "May mislead", "Correct / partial / incorrect / not attempted", "Wrong instead of abstaining",
            "Level 2 or below: accuracy / completeness / conciseness / readability"
        });
        TableHeaderOf(sb, outcomes);
        foreach (var peer in OrderedPeers(ctx.Sheet))
        {
            var profile = new ProfileCells(ctx, peer.Letter);
            string mislead = profile.Value("mayMislead") is double count
                ? Inv((int)count) + (profile.Value("mayMisleadPer20") is double per ? " (" + BenchmarkReportFormat.CompactDecimal(per) + " per 20)" : string.Empty)
                : NoValue;
            string wrong = profile.Value("outcome.wrongInsteadOfAbstaining") is double rate ? BenchmarkReportFormat.Percent(rate * 100) : NoValue;

            var cells = ModelCells(ctx, peer);
            cells.AddRange(new[]
            {
                profile.Count("answers"), mislead,
                profile.Joined("outcome.correct", "outcome.partial", "outcome.incorrect", "outcome.notAttempted"), wrong,
                profile.Joined("deficit.accuracy", "deficit.completeness", "deficit.conciseness", "deficit.readability")
            });
            Line(sb, "| " + string.Join(" | ", cells) + " |");
        }
        Line(sb);
        Line(sb, "*" + FailureProfileOutcomesNote + "*");
        Line(sb);

        Line(sb, "**Signals**");
        Line(sb);
        var signals = ModelColumns(ctx);
        signals.AddRange(new[]
        {
            "Truncated", "Output defects", "No tool call", "Tool-limit hits", "Median answer length", "Isolated / shared misses", "Lowest scorer"
        });
        TableHeaderOf(sb, signals);
        foreach (var peer in OrderedPeers(ctx.Sheet))
        {
            var profile = new ProfileCells(ctx, peer.Letter);
            string length = profile.Value("answerChars.median") is double chars ? BenchmarkReportFormat.Count(chars) : NoValue;

            var cells = ModelCells(ctx, peer);
            cells.AddRange(new[]
            {
                profile.Count("truncated"), profile.Count("outputDefects"), profile.Count("noToolAnswers"), profile.Count("toolLimitHits"), length,
                profile.Joined("isolatedMisses", "sharedMisses"), profile.Count("lowestScorer")
            });
            Line(sb, "| " + string.Join(" | ", cells) + " |");
        }
        Line(sb);
        Line(sb, "*" + FailureProfileSignalsNote + "*");
        Line(sb);
    }

    /// <summary>The cells of one model's failure profile facts (<c>model.X.profile.*</c>, or a per-model sheet's <c>profile.*</c>).</summary>
    private sealed class ProfileCells
    {
        private readonly Context _ctx;
        private readonly string _prefix;

        /// <summary>A comparison-scope model's profile by its letter; a per-model sheet's own with a null letter.</summary>
        public ProfileCells(Context ctx, string? letter)
        {
            _ctx = ctx;
            _prefix = letter == null ? BenchmarkComparisonModelProfile.KeyPrefix : ModelKey(letter, BenchmarkComparisonModelProfile.KeyPrefix);
        }

        public double? Value(string key) => NumberOf(Fact(_ctx, _prefix + key));

        public string Count(string key) => Value(key) is double v ? Inv((int)v) : NoValue;

        public string Joined(params string[] keys)
        {
            var values = keys.Select(Value).ToList();
            return values.All(v => v.HasValue) ? string.Join(" / ", values.Select(v => Inv((int)v!.Value))) : NoValue;
        }
    }

    /// <summary>
    /// Each covered model's runs with structured grader findings and, of them, how many raised a weakness of
    /// each category; a sentence instead when no run of any model recorded them. A category no model's runs raised
    /// is left out, and the note says so.
    /// </summary>
    private static void FindingsTable(StringBuilder sb, Context ctx)
    {
        var categories = new (string Key, string Name)[]
        {
            ("accuracy", "Accuracy"), ("completeness", "Completeness"), ("conciseness", "Conciseness"), ("readability", "Readability"),
            ("critical_error", "Critical error"), ("tool_use", "Tool use"), ("other", "Other")
        };
        string Key(BenchmarkReportPeer peer, string rest) => ModelKey(peer.Letter, BenchmarkComparisonModelProfile.KeyPrefix + "findings." + rest);

        var models = OrderedPeers(ctx.Sheet);
        if (!models.Any(p => IsAvailable(ctx, Key(p, "accuracy"))))
        {
            Line(sb, "No run of these models recorded structured grader findings; they are recorded from harness 40.");
            Line(sb);
            return;
        }

        var header = ModelColumns(ctx);
        header.Add("Runs with findings");
        int keepFirst = header.Count;
        header.AddRange(categories.Select(c => c.Name));
        var rows = new List<IReadOnlyList<string>>();
        foreach (var peer in models)
        {
            var cells = ModelCells(ctx, peer);
            double? runs = NumberOf(Fact(ctx, Key(peer, "runsWithSynthesis")));
            cells.Add(Num(ctx, Key(peer, "runsWithSynthesis")));
            cells.AddRange(categories.Select(c => NumberOf(Fact(ctx, Key(peer, c.Key))) is double count && runs is double of
                ? Inv((int)count) + " of " + Inv((int)of)
                : NoValue));
            rows.Add(cells);
        }

        var (shown, shownRows, dropped) = DropConstantColumns(header, rows, keepFirst);
        TableHeaderOf(sb, shown);
        foreach (var cells in shownRows) Line(sb, "| " + string.Join(" | ", cells) + " |");
        Line(sb);
        string droppedSentence = DroppedColumnsSentence(dropped);
        Line(sb, "*" + FindingsTableNote + (droppedSentence.Length > 0 ? " " + droppedSentence : string.Empty) + "*");
        Line(sb);
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
        => ctx.Anonymized
            ? new List<string> { "Model " + peer.Letter + RoleMarks(ctx, peer.Letter) }
            : new List<string> { Cell(peer.Label) + RoleMarks(ctx, peer.Letter), Cell(peer.Provider) };

    /// <summary>
    /// <c> (reference)</c>, <c> (baseline model)</c> or both as <c> (baseline model, reference)</c> after the
    /// model's name in a table; nothing on a sheet stored before format version 14, which keeps no reference.
    /// </summary>
    private static string RoleMarks(Context ctx, string letter)
    {
        bool reference = ctx.Sheet.ReferenceLetter == letter;
        bool baseline = ctx.Sheet.BaselineModelLetter == letter;
        return (reference, baseline) switch
        {
            (true, true) => " (baseline model, reference)",
            (true, false) => " (reference)",
            (false, true) => " (baseline model)",
            _ => string.Empty,
        };
    }

    /// <summary>
    /// The reference model's role in prose: <c>the baseline model</c>, <c>the highest Intelligence Index</c>
    /// for the model lettered first (and on a sheet that keeps no reference), else <c>the reference</c>.
    /// </summary>
    private static string ReferenceRole(Context ctx, string letter)
        => ctx.Sheet.ReferenceIsBaselineModel == true && ctx.Sheet.ReferenceLetter == letter
            ? "the baseline model"
            : letter == (OrderedPeers(ctx.Sheet).FirstOrDefault()?.Letter ?? "A") ? "the highest Intelligence Index" : "the reference";

    private static void TableHeaderOf(StringBuilder sb, IReadOnlyList<string> columns)
    {
        Line(sb, "| " + string.Join(" | ", columns) + " |");
        Line(sb, "|" + string.Concat(columns.Select(_ => "---|")));
    }

    /// <summary>Whether the sheet carries any covered model's provider availability facts (format version 13 and later).</summary>
    private static bool HasComparisonAvailability(Context ctx)
        => OrderedPeers(ctx.Sheet).Any(p => Fact(ctx, ModelKey(p.Letter, "availability.attempts")) != null);

    /// <summary>
    /// The per-model provider availability: valid attempts,
    /// provider errors, the rate with its interval, unattributed errors and re-runs needed, with the
    /// footnote defining the classes and the denominator; nothing on a sheet without the availability facts.
    /// </summary>
    private static void ComparisonAvailabilityTable(StringBuilder sb, Context ctx)
    {
        if (!HasComparisonAvailability(ctx)) return;

        // A table whose every row would say "not recorded" is one sentence.
        var models = OrderedPeers(ctx.Sheet);
        var modelAttempts = models.Select(p => Fact(ctx, ModelKey(p.Letter, "availability.attempts"))).ToList();
        if (modelAttempts.All(a => a is { Available: false }))
        {
            var reasons = modelAttempts.Select(a => NotRecordedText(a!)).Distinct(StringComparer.Ordinal).ToList();
            Line(sb, "Provider availability is not recorded for " + (models.Count == 2 ? "either model" : "any of the " + Inv(models.Count) + " models")
                + (reasons.Count == 1 ? NotRecordedParenthesis(reasons[0]) : string.Empty) + ".");
            Line(sb);
            return;
        }

        Heading(sb, "### Provider availability");
        var header = ModelColumns(ctx);
        header.AddRange(new[]
        {
            Label("availability.attempts"), Label("availability.providerErrors"), Label("availability.providerErrorRate"),
            Label("availability.unattributedErrors"), Label("availability.reruns")
        });
        TableHeaderOf(sb, header);

        foreach (var peer in OrderedPeers(ctx.Sheet))
        {
            string prefix = BenchmarkComparisonReportFacts.ModelPrefix(peer.Letter);
            var cells = ModelCells(ctx, peer);
            var attempts = Fact(ctx, prefix + "availability.attempts");
            if (attempts is not { Available: true })
            {
                cells.AddRange(new[] { Cell(attempts == null ? BenchmarkReportFacts.NotAvailable : NotRecordedText(attempts)), NoValue, NoValue, NoValue, NoValue });
                Line(sb, "| " + string.Join(" | ", cells) + " |");
                continue;
            }

            string Count(string key) => NumberOf(Fact(ctx, prefix + key)) is double count ? BenchmarkReportFacts.Thousands((long)count) : NoValue;
            string rate = Fact(ctx, prefix + "availability.providerErrorRate") is { Available: true } r
                ? r.Display + (AvailabilityInterval(ctx, prefix + "availability.providerErrorRate") is { Length: > 0 } interval ? " (" + interval + ")" : string.Empty)
                : NoValue;
            cells.AddRange(new[]
            {
                Count("availability.attempts"), Count("availability.providerErrors"), Cell(rate),
                Count("availability.unattributedErrors"), Cell(AvailabilityRerunsText(ctx, prefix))
            });
            Line(sb, "| " + string.Join(" | ", cells) + " |");
        }
        Line(sb);
        Line(sb, "*Valid attempts: each model's API attempts except our-side failures, the denominator of its provider error rate, "
            + "whose interval is a cluster-adjusted Wilson 95% interval. " + AvailabilityFootnoteClasses + "*");
        Line(sb);
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
