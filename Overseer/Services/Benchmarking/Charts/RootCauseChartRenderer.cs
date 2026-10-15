namespace Overseer.Services.Benchmarking.Charts;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Overseer.Models;
using Overseer.Services.Benchmarking.Pdf;
using Overseer.Services.Benchmarking.RootCause;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

/// <summary>One server-drawn chart: its figure key, its text, and a PNG of <see cref="Width"/> by <see cref="Height"/> pixels.</summary>
public sealed record DrawnChart(string Key, string Title, string Caption, string AltText, byte[] Png, int Width, int Height);

/// <summary>
/// Draws the four Root Cause Analysis charts from a stored <see cref="RootCauseResult"/>: R1 points at
/// risk, R2 speed decomposition and R3 cost decomposition as stacked horizontal bars, and R4 hypothesis
/// confidence as a dot on an ordinal grade axis with a share bar.
///
/// <para>Each chart is a one-page QuestPDF document whose page is the chart, the full text-column width
/// (<see cref="WidthPoints"/>) by 40 pt plus 18 pt per row, and 11 pt more for R1–R3's axis title, at most
/// <see cref="MaxHeightPoints"/>; its
/// text is set in the document fonts <see cref="BenchmarkPdfResources"/> registers, at
/// <see cref="FontSize"/> pt. It is rasterized with
/// <c>document.GenerateImages(new ImageGenerationSettings { ImageFormat = ImageFormat.Png, RasterDpi = 300 })</c>
/// (QuestPDF 2026.9.1, <c>QuestPDF.Fluent.GenerateExtensions</c>), which returns one PNG per page; the
/// page content is scaled to fit, so there is always exactly one.</para>
///
/// <para>Static and pure: the same result, sheet and naming always give the same charts. A chart with no
/// row to draw is left out. The named variant prints model names, in R4's statements too; the anonymized
/// variant prints a peer's letter (<c>Model A</c>) and never its name, as the anonymized document does.</para>
///
/// <para>R1–R3 name each segment in a key by its color and print a segment's value inside it where the value
/// fits; the axis title stands on its own row under the ticks. R4 shows the hypotheses the body's tables show,
/// the null hypotheses left out.</para>
/// </summary>
public static class RootCauseChartRenderer
{
    public const string PointsAtRiskTitle = "Where the points were lost";
    public const string SpeedTitle = "Where the time goes";
    public const string CostTitle = "Where the cost goes";
    public const string HypothesesTitle = "How sure each hypothesis is";

    /// <summary>The text column of an A4 page, which every server-drawn figure fills.</summary>
    public const float WidthPoints = 481.9f;

    public const float BaseHeightPoints = 40f;
    public const float RowPoints = 18f;
    public const float FontSize = 8f;
    public const int RasterDpi = 300;

    /// <summary>The estimated width of one character of a segment's value label, in points.</summary>
    public const float LabelCharPoints = 4.5f;

    /// <summary>The room a segment keeps around its value label, in points: a segment narrower than the label plus this prints none.</summary>
    public const float LabelPaddingPoints = 4f;

    /// <summary>The tallest chart: <see cref="BenchmarkReportChartLayout.DefaultMaxHeightShare"/> of an A4 page's content height, as a figure's default height cap.</summary>
    public static float MaxHeightPoints => BenchmarkPdfRenderer.FigureFrameFor(BenchmarkPdfPaper.A4).MaxHeight;

    /// <summary>The shortest chart, so that its PNG is at least <see cref="BenchmarkReportChartStore.MinDimensionPx"/> tall.</summary>
    public static float MinHeightPoints => MathF.Ceiling(BenchmarkReportChartStore.MinDimensionPx * 72f / RasterDpi) + 1;

    private const float PaddingPoints = 2f;
    private const float ContentWidth = WidthPoints - 2 * PaddingPoints;
    private const float LineHeight = 1.15f;

    // R1–R3: the row label, then the bars scaled to PlotWidth, the total after each bar's end.
    private const float LabelWidth = 120f;
    private const float PlotWidth = 300f;
    private const float BarHeight = 12f;
    private const float SegmentGap = 1f;
    private const float AxisTitlePoints = 11f;

    // R4: the label, the four grade positions, then the share bar and its text.
    private const float HypothesisLabelWidth = 150f;
    private const float GradeWidth = 180f;
    private const float GradeDot = 7f;
    private const float ShareTrackWidth = 56f;
    private const float ShareBarHeight = 7f;
    private const float HeaderHeight = 20f;
    private const float MinHypothesisRowPoints = 12f;

    private static readonly string[] GradeOrder =
    {
        RootCauseGrades.NotSupported, RootCauseGrades.Low, RootCauseGrades.Moderate, RootCauseGrades.High
    };

    /// <summary>
    /// The charts of <paramref name="result"/>, in the order of <see cref="BenchmarkReportChartPlacement.ServerFigureKeys"/>,
    /// each rasterized to PNG; a chart with no row to draw is left out. Throws when QuestPDF cannot lay a
    /// chart out or its PNG falls outside the chart store's limits.
    /// </summary>
    public static IReadOnlyList<DrawnChart> Draw(RootCauseResult result, BenchmarkReportFactSheet sheet, bool anonymized)
    {
        var drawn = new List<DrawnChart>();
        foreach (var plan in Plan(result, sheet, anonymized))
        {
            byte[] png = Rasterize(plan);
            if (!BenchmarkReportChartStore.TryReadPngSize(png, out int width, out int height)
                || width < BenchmarkReportChartStore.MinDimensionPx || width > BenchmarkReportChartStore.MaxDimensionPx
                || height < BenchmarkReportChartStore.MinDimensionPx || height > BenchmarkReportChartStore.MaxDimensionPx
                || png.Length > BenchmarkReportChartStore.MaxPngBytes)
            {
                throw new InvalidOperationException($"The {plan.Key} chart rasterized outside the chart store's limits.");
            }

            drawn.Add(new DrawnChart(plan.Key, plan.Title, plan.Caption, plan.AltText, png, width, height));
        }
        return drawn;
    }

    /// <summary>The same charts as <see cref="Draw"/>, each as a one-page PDF instead of a PNG, keyed by figure key.</summary>
    public static IReadOnlyList<KeyValuePair<string, byte[]>> DrawPdfs(RootCauseResult result, BenchmarkReportFactSheet sheet, bool anonymized)
        => Plan(result, sheet, anonymized)
            .Select(plan => new KeyValuePair<string, byte[]>(plan.Key, Compose(plan).GeneratePdf()))
            .ToList();

    // ---------------------------------------------------------------- plans

    /// <summary>One chart before it is drawn: its text, its page height and how to compose its content.</summary>
    private sealed record ChartPlan(string Key, string Title, string Caption, string AltText, float HeightPoints, Action<IContainer> Content);

    private static List<ChartPlan> Plan(RootCauseResult result, BenchmarkReportFactSheet sheet, bool anonymized)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(sheet);

        string drawnBy = "Drawn by Overseer from the analysis computed "
            + DateTime.SpecifyKind(result.ComputedAtUtc, DateTimeKind.Utc).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC.";

        var plans = new List<ChartPlan>();
        AddStack(plans, BenchmarkReportChartPlacement.RcaPointsAtRiskKey, PointsAtRiskTitle, result.PointsAtRisk, anonymized,
            RootCauseSegments.AtRisk, "points",
            "Index points at risk, out of 100, split by cause" + GroupSuffix(result.PointsAtRisk) + ". " + drawnBy);
        AddStack(plans, BenchmarkReportChartPlacement.RcaSpeedKey, SpeedTitle, result.SpeedRows, anonymized,
            RootCauseSegments.Time, "seconds",
            "Seconds per question, median per question, split by where the time goes" + GroupSuffix(result.SpeedRows) + ". " + drawnBy);
        AddStack(plans, BenchmarkReportChartPlacement.RcaCostKey, CostTitle, result.CostRows, anonymized,
            RootCauseSegments.CostComponents, "usd",
            "Cost per question in US dollars, split by token kind" + GroupSuffix(result.CostRows)
            + (string.IsNullOrWhiteSpace(result.PricingBasis) ? "" : "; pricing basis: " + result.PricingBasis.Trim().TrimEnd('.'))
            + ". " + drawnBy);
        AddHypotheses(plans, (result.Hypotheses ?? new List<RootCauseHypothesis>()).Where(h => h != null && h.Kind != "null").ToList(),
            ModelNames(result, anonymized),
            "The confidence grade of each hypothesis in the tables, and its share of the difference with its 95% interval where it has one. " + drawnBy);
        return plans;
    }

    /// <summary>
    /// The named variant's text with each <c>Model X</c> of an analyzed model replaced by the model's label; the anonymized
    /// variant's text unchanged.
    /// </summary>
    private static Func<string, string> ModelNames(RootCauseResult result, bool anonymized)
    {
        var labels = (result.Models ?? new List<RootCauseModel>())
            .Where(m => !string.IsNullOrEmpty(m.Letter) && !string.IsNullOrWhiteSpace(m.Label))
            .GroupBy(m => m.Letter!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Label, StringComparer.Ordinal);
        if (anonymized || labels.Count == 0) return text => text;
        return text => ModelToken.Replace(text, match => labels.TryGetValue(match.Groups[1].Value, out string? label) ? label : match.Value);
    }

    private static readonly Regex ModelToken = new(@"\bModel ([A-Z]{1,3})\b", RegexOptions.CultureInvariant);

    /// <summary>The key whose slot a segment takes: its own, and the visible output's for <see cref="RootCauseSegments.OutputWithThinking"/>.</summary>
    private static string SlotKey(string key) => key == RootCauseSegments.OutputWithThinking ? RootCauseSegments.Output : key;

    /// <summary>
    /// The words of each slot of a chart's key: its segment's label; for the visible output slot of R3, the output with
    /// thinking included where some row holds that segment, or both where rows hold each. Null for a slot no row holds.
    /// </summary>
    private static List<string?> KeyLabels(IReadOnlyList<string> segmentKeys, IReadOnlyList<RootCauseChartRow> rows)
    {
        var held = new HashSet<string>(rows.SelectMany(r => r.Segments).Where(s => s?.Key != null).Select(s => s!.Key), StringComparer.Ordinal);
        bool merged = held.Contains(RootCauseSegments.OutputWithThinking);
        bool plain = held.Contains(RootCauseSegments.Output);
        return segmentKeys.Select(k => k == RootCauseSegments.Output && merged
                ? (plain
                    ? RootCauseSegments.Label(RootCauseSegments.Output) + " (or " + RootCauseSegments.Label(RootCauseSegments.OutputWithThinking) + ")"
                    : RootCauseSegments.Label(RootCauseSegments.OutputWithThinking))
                : held.Contains(k) ? RootCauseSegments.Label(k) : null)
            .ToList();
    }

    /// <summary>Whom the rows of a chart are: per model, or the subject and then its difficulty bands or suites.</summary>
    private static string GroupSuffix(IReadOnlyList<RootCauseChartRow> rows)
        => rows.Any(r => r.Kind == "suite") ? ", for the subject and per suite"
            : rows.Any(r => r.Kind == "band") ? ", for the subject and per difficulty band"
            : ", per model";

    /// <summary>A row's name in this variant: a peer's letter when anonymized, its label otherwise.</summary>
    private static string RowName(RootCauseChartRow row, bool anonymized)
        => anonymized && !string.IsNullOrEmpty(row.Letter) ? "Model " + row.Letter : row.Label ?? string.Empty;

    private static void AddStack(
        List<ChartPlan> plans, string key, string title, IReadOnlyList<RootCauseChartRow>? source, bool anonymized,
        IReadOnlyList<string> segmentKeys, string unit, string caption)
    {
        var rows = (source ?? Array.Empty<RootCauseChartRow>()).Where(r => r != null).ToList();
        if (!rows.Any(Available)) return;

        float cap = MaxHeightPoints;
        float baseHeight = BaseHeightPoints + AxisTitlePoints;
        int shown = rows.Count;
        int hidden = 0;
        if (baseHeight + RowPoints * rows.Count > cap)
        {
            shown = Math.Max(1, (int)Math.Floor((cap - baseHeight) / RowPoints) - 1);
            hidden = rows.Count - shown;
        }

        float height = Math.Max(MinHeightPoints, baseHeight + RowPoints * (shown + (hidden > 0 ? 1 : 0)));
        double max = NiceCeiling(rows.Where(Available).Max(r => r.Total!.Value));
        var visible = rows.Take(shown).Select(r => (Name: RowName(r, anonymized), Row: r)).ToList();
        var keyLabels = KeyLabels(segmentKeys, rows);

        string alt = AltText(title, rows.Select(r => StackClause(r, RowName(r, anonymized), unit)).ToList());
        plans.Add(new ChartPlan(key, title, Clip(caption, BenchmarkReportChartStore.MaxCaptionChars), alt, height,
            content => ComposeStack(content, segmentKeys, keyLabels, visible, hidden, unit, max)));
    }

    private static bool Available(RootCauseChartRow row)
        => row.UnavailableReason == null && row.Total is double total && double.IsFinite(total) && row.Segments.Count > 0;

    private static void AddHypotheses(List<ChartPlan> plans, IReadOnlyList<RootCauseHypothesis>? source, Func<string, string> names, string caption)
    {
        var hypotheses = (source ?? Array.Empty<RootCauseHypothesis>()).Where(h => h != null).ToList();
        if (hypotheses.Count == 0) return;

        // Grouped by axis, in the stored order: a heading line, then the axis' hypotheses.
        var items = new List<(string? Heading, RootCauseHypothesis? Hypothesis)>();
        foreach (string axis in hypotheses.Select(h => h.Axis ?? string.Empty).Distinct(StringComparer.Ordinal))
        {
            items.Add((RootCauseAxes.Name(axis), null));
            items.AddRange(hypotheses.Where(h => string.Equals(h.Axis ?? string.Empty, axis, StringComparison.Ordinal)).Select(h => ((string?)null, (RootCauseHypothesis?)h)));
        }

        float cap = MaxHeightPoints;
        float pitch = Math.Clamp((cap - BaseHeightPoints) / items.Count, MinHypothesisRowPoints, RowPoints);
        int shownItems = items.Count;
        int hidden = 0;
        if (BaseHeightPoints + pitch * items.Count > cap)
        {
            shownItems = Math.Max(1, (int)Math.Floor((cap - BaseHeightPoints) / pitch) - 1);
            hidden = items.Skip(shownItems).Count(i => i.Hypothesis != null);
        }

        var visible = items.Take(shownItems).ToList();
        float height = Math.Max(MinHeightPoints, BaseHeightPoints + pitch * (visible.Count + (hidden > 0 ? 1 : 0)));
        string alt = AltText(HypothesesTitle, hypotheses.Select(h => HypothesisClause(h, names)).ToList());
        plans.Add(new ChartPlan(BenchmarkReportChartPlacement.RcaHypothesesKey, HypothesesTitle,
            Clip(caption, BenchmarkReportChartStore.MaxCaptionChars), alt, height,
            content => ComposeHypotheses(content, visible, hidden, pitch, names)));
    }

    // ---------------------------------------------------------------- text

    /// <summary>
    /// The title, then one clause per row, at most <see cref="BenchmarkReportChartStore.MaxAltTextChars"/>
    /// characters; past the limit the clauses stop at a row boundary with "and N more rows".
    /// </summary>
    internal static string AltText(string title, IReadOnlyList<string> clauses)
    {
        string Join(int count, int more)
        {
            var parts = clauses.Take(count).ToList();
            if (more > 0) parts.Add("and " + more.ToString(CultureInfo.InvariantCulture) + (more == 1 ? " more row" : " more rows"));
            return parts.Count == 0 ? title + "." : title + ". " + string.Join("; ", parts) + ".";
        }

        for (int count = clauses.Count; count >= 0; count--)
        {
            string text = Join(count, clauses.Count - count);
            if (text.Length <= BenchmarkReportChartStore.MaxAltTextChars) return text;
        }
        return Clip(title, BenchmarkReportChartStore.MaxAltTextChars);
    }

    private static string StackClause(RootCauseChartRow row, string name, string unit)
    {
        if (!Available(row))
        {
            return name + ": " + NotRecorded(row.UnavailableReason);
        }

        double total = row.Total!.Value;
        string clause = unit switch
        {
            "points" => name + ": " + Format(total, unit) + " points at risk",
            _ => name + ": " + Format(total, unit) + " per question"
        };

        var largest = row.Segments.Where(s => s != null).Select((s, i) => (Segment: s, Index: i))
            .OrderByDescending(s => s.Segment.Value).ThenBy(s => s.Index).FirstOrDefault();
        if (largest.Segment == null || largest.Segment.Value <= 0) return clause;

        string word = unit == "seconds" ? ", most in " : ", most from ";
        return clause + word + SegmentLabel(largest.Segment) + " (" + Format(largest.Segment.Value, unit) + ")";
    }

    private static string HypothesisClause(RootCauseHypothesis hypothesis, Func<string, string> names)
    {
        string clause = HypothesisLabel(hypothesis, names) + ": " + RootCauseGrades.Display(hypothesis.Grade);
        if (hypothesis.Share is double share)
        {
            clause += ", share " + Percent(share);
            if (hypothesis.ShareLower is double lower && hypothesis.ShareUpper is double upper)
            {
                clause += " (95% interval " + Percent(lower) + " to " + Percent(upper) + ")";
            }
        }
        return clause;
    }

    /// <summary>The hypothesis' key and short statement, each <c>Model X</c> as <paramref name="names"/> names it.</summary>
    private static string HypothesisLabel(RootCauseHypothesis hypothesis, Func<string, string> names)
    {
        string id = string.IsNullOrWhiteSpace(hypothesis.Key) ? hypothesis.Id ?? string.Empty : hypothesis.Key;
        string text = string.IsNullOrWhiteSpace(hypothesis.ShortStatement) ? hypothesis.Statement ?? string.Empty : hypothesis.ShortStatement;
        return (id + " " + names(text)).Trim();
    }

    private static string SegmentLabel(RootCauseSegment segment)
        => string.IsNullOrWhiteSpace(segment.Label) ? RootCauseSegments.Label(segment.Key) : segment.Label;

    /// <summary><c>not recorded (no candidate call telemetry)</c>: the reason without a closing period.</summary>
    private static string NotRecorded(string? reason)
    {
        string text = (reason ?? string.Empty).Trim().TrimEnd('.');
        if (text.Length > 1 && char.IsUpper(text[0]) && char.IsLower(text[1]))
        {
            text = char.ToLowerInvariant(text[0]) + text[1..];
        }
        return text.Length == 0 ? "not recorded" : "not recorded (" + text + ")";
    }

    /// <summary>A value in its unit: <c>14.1</c> points, <c>12.3 s</c>, <c>$0.0123</c>.</summary>
    internal static string Format(double value, string unit) => unit switch
    {
        "seconds" => value.ToString("0.0", CultureInfo.InvariantCulture) + " s",
        "usd" => value == 0 ? "$0"
            : "$" + value.ToString(Math.Abs(value) >= 1 ? "0.00" : Math.Abs(value) >= 0.01 ? "0.000" : "0.0000", CultureInfo.InvariantCulture),
        _ => value.ToString("0.0", CultureInfo.InvariantCulture)
    };

    private static string Percent(double share)
        => (share * 100).ToString("0", CultureInfo.InvariantCulture) + "%";

    private static string AxisUnit(string unit) => unit switch
    {
        "seconds" => "seconds per question",
        "usd" => "USD per question",
        _ => "index points (of 100)"
    };

    /// <summary>
    /// The smallest of 1, 1.2, 1.5, 2, 2.5, 3, 4, 5, 6, 8 and 10 times a power of ten at or above <paramref name="value"/>,
    /// so 60 stays 60; 1 for zero or less.
    /// </summary>
    internal static double NiceCeiling(double value)
    {
        if (!(value > 0) || !double.IsFinite(value)) return 1;
        double power = Math.Pow(10, Math.Floor(Math.Log10(value)));
        // The steps in tenths, so that a whole power of ten gives a whole ceiling.
        foreach (int tenths in new[] { 10, 12, 15, 20, 25, 30, 40, 50, 60, 80, 100 })
        {
            double nice = tenths * power / 10;
            if (nice >= value * (1 - 1e-12)) return nice;
        }
        return 10 * power;
    }

    private static string Clip(string text, int max) => text.Length <= max ? text : text[..(max - 1)].TrimEnd() + "…";

    // ---------------------------------------------------------------- drawing

    private static Color Hex(string hex) => Color.FromHex(hex);

    private static TextStyle ChartStyle() => TextStyle.Default
        .FontFamily(BenchmarkPdfResources.SansFamily, BenchmarkPdfResources.FallbackFamily)
        .FontSize(FontSize)
        .LineHeight(LineHeight)
        .FontColor(Hex(RootCauseChartPalette.Ink));

    private static IDocument Compose(ChartPlan plan)
    {
        BenchmarkPdfResources.EnsureRegistered();
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(WidthPoints, plan.HeightPoints, Unit.Point);
                page.Margin(0);
                page.PageColor(Hex(RootCauseChartPalette.Surface));
                page.DefaultTextStyle(ChartStyle());
                page.Content().ScaleToFit().Padding(PaddingPoints).Element(plan.Content);
            });
        });
    }

    private static byte[] Rasterize(ChartPlan plan)
    {
        var images = Compose(plan)
            .GenerateImages(new ImageGenerationSettings { ImageFormat = ImageFormat.Png, RasterDpi = RasterDpi })
            .ToList();
        if (images.Count == 0)
        {
            throw new InvalidOperationException($"The {plan.Key} chart produced no image.");
        }
        return images[0];
    }

    /// <summary>One line of text that never wraps: clamped to one line with an ellipsis.</summary>
    private static void Line(IContainer container, string text, string ink, bool semibold = false)
    {
        var block = container.Text(text).FontColor(Hex(ink));
        if (semibold)
        {
            block.FontFamily(BenchmarkPdfResources.SemiboldFamily, BenchmarkPdfResources.SansFamily, BenchmarkPdfResources.FallbackFamily).SemiBold();
        }
        block.ClampLines(1, "…");
    }

    /// <summary>
    /// R1–R3: the key (each segment's color swatch and name, in stacking order), then one row per model, band or
    /// suite with its stacked bar and the total after its end, then the axis: its rule, its ticks, and its title on
    /// a row of its own.
    /// </summary>
    private static void ComposeStack(
        IContainer container, IReadOnlyList<string> segmentKeys, IReadOnlyList<string?> keyLabels,
        IReadOnlyList<(string Name, RootCauseChartRow Row)> rows, int hidden, string unit, double max)
    {
        container.Column(col =>
        {
            col.Item().PaddingBottom(3).Text(text =>
            {
                text.DefaultTextStyle(s => s.FontColor(Hex(RootCauseChartPalette.SecondaryInk)));
                text.Span("Key: ");
                bool first = true;
                for (int i = 0; i < segmentKeys.Count; i++)
                {
                    if (i < keyLabels.Count && keyLabels[i] == null) continue;
                    if (!first) text.Span("   ");
                    first = false;
                    // A swatch (U+25A0) and a no-break space (U+00A0), then the label.
                    text.Span("■ ").FontColor(Hex(RootCauseChartPalette.Segment(i)));
                    text.Span((i < keyLabels.Count ? keyLabels[i] : null) ?? RootCauseSegments.Label(segmentKeys[i]));
                }
            });

            foreach (var (name, row) in rows)
            {
                col.Item().Height(RowPoints).Row(line =>
                {
                    line.ConstantItem(LabelWidth).Height(RowPoints).AlignMiddle().PaddingRight(6)
                        .Element(c => Line(c, name, RootCauseChartPalette.Ink));
                    if (!Available(row))
                    {
                        line.RelativeItem().Height(RowPoints).AlignMiddle()
                            .Element(c => Line(c, NotRecorded(row.UnavailableReason), RootCauseChartPalette.SecondaryInk));
                        return;
                    }

                    line.RelativeItem().Row(bar => ComposeBar(bar, segmentKeys, row, unit, max));
                });
            }

            if (hidden > 0)
            {
                col.Item().Height(RowPoints).AlignMiddle().PaddingLeft(LabelWidth)
                    .Element(c => Line(c, "and " + hidden.ToString(CultureInfo.InvariantCulture) + (hidden == 1 ? " more row" : " more rows") + ", in the tables",
                        RootCauseChartPalette.SecondaryInk));
            }

            col.Item().PaddingTop(2).Row(axis =>
            {
                axis.ConstantItem(LabelWidth);
                axis.RelativeItem().Column(scale =>
                {
                    scale.Item().Row(rule =>
                    {
                        rule.ConstantItem(PlotWidth).Height(0.75f).Background(Hex(RootCauseChartPalette.Baseline));
                        rule.RelativeItem();
                    });
                    scale.Item().Row(ticks =>
                    {
                        ticks.ConstantItem(PlotWidth).Row(t =>
                        {
                            t.RelativeItem().Element(c => Line(c, Format(0, unit), RootCauseChartPalette.SecondaryInk));
                            t.RelativeItem().AlignCenter().Element(c => Line(c, Format(max / 2, unit), RootCauseChartPalette.SecondaryInk));
                            t.RelativeItem().AlignRight().Element(c => Line(c, Format(max, unit), RootCauseChartPalette.SecondaryInk));
                        });
                        ticks.RelativeItem();
                    });
                    scale.Item().Row(title =>
                    {
                        title.ConstantItem(PlotWidth).AlignCenter().Element(c => Line(c, AxisUnit(unit), RootCauseChartPalette.SecondaryInk));
                        title.RelativeItem();
                    });
                });
            });
        });
    }

    /// <summary>
    /// One stacked bar: each segment in its stacking color with a surface gap after it, with its value inside
    /// it (<see cref="SegmentValue"/>) where the segment is at least as wide as the label's estimated width
    /// (<see cref="LabelCharPoints"/> a character) plus <see cref="LabelPaddingPoints"/>; then the total.
    /// </summary>
    private static void ComposeBar(RowDescriptor bar, IReadOnlyList<string> segmentKeys, RootCauseChartRow row, string unit, double max)
    {
        double total = row.Total!.Value;
        float scale = (float)(PlotWidth / max);
        float used = 0;
        float verticalPad = (RowPoints - BarHeight) / 2;

        for (int i = 0; i < row.Segments.Count; i++)
        {
            var segment = row.Segments[i];
            if (segment == null || !(segment.Value > 0) || !double.IsFinite(segment.Value)) continue;

            float width = Math.Min((float)segment.Value * scale, PlotWidth - used);
            if (width < 0.05f) continue;
            used += width;

            int slot = SlotOf(segmentKeys, segment.Key, i);
            bool gap = width > SegmentGap + 0.5f;
            string label = SegmentValue(segment.Value, unit);
            bool labeled = width - (gap ? SegmentGap : 0) >= label.Length * LabelCharPoints + LabelPaddingPoints;

            var box = bar.ConstantItem(width).Height(RowPoints).PaddingVertical(verticalPad);
            if (gap) box = box.PaddingRight(SegmentGap);
            box = box.Background(Hex(RootCauseChartPalette.Segment(slot)));
            if (labeled)
            {
                box.AlignCenter().AlignMiddle()
                    .Text(label)
                    .FontColor(Hex(RootCauseChartPalette.SegmentLabel(slot)))
                    .ClampLines(1, "");
            }
        }

        bar.ConstantItem(3);
        bar.RelativeItem().Height(RowPoints).AlignMiddle().Element(c => Line(c, Format(total, unit), RootCauseChartPalette.Ink));
    }

    /// <summary>
    /// The stacking position of a segment: its key's place in the chart's key, the visible output's for output with
    /// thinking included (<see cref="SlotKey"/>), or its own index.
    /// </summary>
    private static int SlotOf(IReadOnlyList<string> segmentKeys, string? key, int index)
    {
        string? slotKey = key == null ? null : SlotKey(key);
        for (int i = 0; i < segmentKeys.Count; i++)
        {
            if (string.Equals(segmentKeys[i], slotKey, StringComparison.Ordinal)) return i;
        }
        return index;
    }

    /// <summary>
    /// A segment's value as its label prints it, in the stack's unit: one decimal for points and seconds, <c>14.1</c>,
    /// and <see cref="BenchmarkReportFormat.Usd"/> for cost, <c>$0.0040</c>.
    /// </summary>
    internal static string SegmentValue(double value, string unit)
        => unit == "usd" ? BenchmarkReportFormat.Usd(value) : BenchmarkReportFormat.OneDecimal(value);

    /// <summary>
    /// R4: the header naming the four grade positions, then per axis a heading line and one line per
    /// hypothesis: its id and short statement, a dot at its grade with the grade beside it, and where it
    /// has a share, a bar to the share with its 95% interval as a whisker and the figures beside it.
    /// </summary>
    private static void ComposeHypotheses(
        IContainer container, IReadOnlyList<(string? Heading, RootCauseHypothesis? Hypothesis)> items, int hidden, float pitch, Func<string, string> names)
    {
        container.Column(col =>
        {
            col.Item().Height(HeaderHeight).Row(header =>
            {
                header.ConstantItem(HypothesisLabelWidth).AlignBottom()
                    .Element(c => Line(c, "Hypothesis", RootCauseChartPalette.SecondaryInk));
                header.ConstantItem(GradeWidth).Row(grades =>
                {
                    foreach (string grade in GradeOrder)
                    {
                        grades.RelativeItem().AlignBottom().AlignCenter()
                            .Text(RootCauseGrades.Display(grade)).FontColor(Hex(RootCauseChartPalette.SecondaryInk)).AlignCenter().ClampLines(2, "…");
                    }
                });
                header.RelativeItem().AlignBottom().PaddingLeft(4)
                    .Element(c => Line(c, "Share (95% interval)", RootCauseChartPalette.SecondaryInk));
            });

            col.Item().Height(0.75f).Background(Hex(RootCauseChartPalette.Baseline));

            foreach (var (heading, hypothesis) in items)
            {
                if (hypothesis is not { } shown)
                {
                    col.Item().Height(pitch).AlignMiddle().Element(c => Line(c, heading ?? string.Empty, RootCauseChartPalette.Ink, semibold: true));
                    continue;
                }

                col.Item().Height(pitch).Row(line =>
                {
                    line.ConstantItem(HypothesisLabelWidth).Height(pitch).AlignMiddle().PaddingRight(6)
                        .Element(c => Line(c, HypothesisLabel(shown, names), RootCauseChartPalette.Ink));
                    line.ConstantItem(GradeWidth).Row(grade => ComposeGrade(grade, shown.Grade, pitch));
                    line.RelativeItem().Row(share => ComposeShare(share, shown, pitch));
                });
            }

            if (hidden > 0)
            {
                col.Item().Height(pitch).AlignMiddle()
                    .Element(c => Line(c, "and " + hidden.ToString(CultureInfo.InvariantCulture) + (hidden == 1 ? " more hypothesis" : " more hypotheses") + ", in the tables",
                        RootCauseChartPalette.SecondaryInk));
            }
        });
    }

    /// <summary>The grade dot at its position on the ordinal axis, with the grade's name beside it (to its left for High).</summary>
    private static void ComposeGrade(RowDescriptor row, string? grade, float pitch)
    {
        int rank = Math.Max(0, RootCauseGrades.Rank(grade));
        float slot = GradeWidth / GradeOrder.Length;
        float center = slot * (rank + 0.5f);
        float dotPad = (pitch - GradeDot) / 2;
        string text = RootCauseGrades.Display(GradeOrder[rank]);
        var color = Hex(RootCauseChartPalette.Grade(rank));

        if (rank < GradeOrder.Length - 1)
        {
            row.ConstantItem(center - GradeDot / 2);
            row.ConstantItem(GradeDot).Height(pitch).PaddingVertical(dotPad).Background(color).CornerRadius(GradeDot / 2);
            row.ConstantItem(3);
            row.RelativeItem().Height(pitch).AlignMiddle().Element(c => Line(c, text, RootCauseChartPalette.Ink));
        }
        else
        {
            row.RelativeItem().Height(pitch).AlignMiddle().AlignRight().Element(c => Line(c, text, RootCauseChartPalette.Ink));
            row.ConstantItem(3);
            row.ConstantItem(GradeDot).Height(pitch).PaddingVertical(dotPad).Background(color).CornerRadius(GradeDot / 2);
            row.ConstantItem(GradeWidth - center - GradeDot / 2);
        }
    }

    /// <summary>The share bar from zero, its 95% interval as a whisker over it, and the figures; nothing without a share. Shares are drawn within 0 to 1.</summary>
    private static void ComposeShare(RowDescriptor row, RootCauseHypothesis hypothesis, float pitch)
    {
        if (hypothesis.Share is not double share || !double.IsFinite(share))
        {
            row.RelativeItem();
            return;
        }

        // The bar starts 1 pt in; the whisker's caps are centered on its two ends, all within the track.
        float scale = ShareTrackWidth - 3f;
        float barWidth = (float)Math.Clamp(share, 0, 1) * scale;
        float barPad = (pitch - ShareBarHeight) / 2;
        var color = Hex(RootCauseChartPalette.Grade(Math.Max(0, RootCauseGrades.Rank(hypothesis.Grade))));
        var ink = Hex(RootCauseChartPalette.Ink);

        double lower = hypothesis.ShareLower ?? double.NaN;
        double upper = hypothesis.ShareUpper ?? double.NaN;
        bool interval = double.IsFinite(lower) && double.IsFinite(upper);
        double low = interval ? Math.Min(lower, upper) : 0;
        double high = interval ? Math.Max(lower, upper) : 0;
        string figures = Percent(share)
            + (interval ? " (" + (low * 100).ToString("0", CultureInfo.InvariantCulture) + "–" + Percent(high) + ")" : "");

        row.ConstantItem(4);
        row.ConstantItem(ShareTrackWidth).Layers(layers =>
        {
            layers.PrimaryLayer().Height(pitch).Row(track =>
            {
                track.ConstantItem(1);
                if (barWidth >= 0.05f)
                {
                    track.ConstantItem(barWidth).Height(pitch).PaddingVertical(barPad).Background(color);
                }
                track.RelativeItem();
            });

            if (interval)
            {
                float from = (float)Math.Clamp(low, 0, 1) * scale;
                float span = (float)Math.Clamp(high, 0, 1) * scale - from;
                float capPad = (pitch - 5) / 2;
                float linePad = (pitch - 0.75f) / 2;
                layers.Layer().Height(pitch).Row(whisker =>
                {
                    whisker.ConstantItem(from + 0.5f);
                    whisker.ConstantItem(1).Height(pitch).PaddingVertical(capPad).Background(ink);
                    if (span > 1.05f) whisker.ConstantItem(span - 1).Height(pitch).PaddingVertical(linePad).Background(ink);
                    else if (span > 0.05f) whisker.ConstantItem(span);
                    whisker.ConstantItem(1).Height(pitch).PaddingVertical(capPad).Background(ink);
                    whisker.RelativeItem();
                });
            }
        });
        row.ConstantItem(4);
        row.RelativeItem().Height(pitch).AlignMiddle().Element(c => Line(c, figures, RootCauseChartPalette.Ink));
    }
}
