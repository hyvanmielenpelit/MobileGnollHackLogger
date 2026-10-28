namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using Overseer.Models;
using Overseer.Services.Benchmarking;
using Overseer.Services.Benchmarking.Charts;
using Overseer.Services.Benchmarking.RootCause;
using UglyToad.PdfPig;
using Xunit;

/// <summary>
/// The four server-drawn Root Cause Analysis charts (R1–R4): drawn for a comparison and a stand-alone
/// subject, PNG sides within the chart store's limits, alt text that names each row within 1,000
/// characters, "not recorded" rows, labels a PDF reader finds, and an anonymized variant without names.
/// </summary>
public class RootCauseChartRendererTests
{
    public RootCauseChartRendererTests() => BenchmarkPdfTestSetup.Configure();

    private static RootCauseChartRow Row(string label, string? letter, string kind, IReadOnlyList<string> keys, string unit, params double[] values)
        => new()
        {
            Label = label,
            Letter = letter,
            Token = letter,
            Kind = kind,
            Unit = unit,
            Segments = keys.Select((k, i) => new RootCauseSegment { Key = k, Label = RootCauseSegments.Label(k), Value = values[i] }).ToList(),
            Total = values.Sum()
        };

    private static RootCauseChartRow Unavailable(string label, string? letter, string kind, string unit, string reason)
        => new() { Label = label, Letter = letter, Token = letter, Kind = kind, Unit = unit, UnavailableReason = reason };

    private static RootCauseHypothesis Hypothesis(string id, string axis, string grade, string statement, double? share = null)
        => new()
        {
            Id = id,
            Number = 1,
            Key = id + ".1",
            Axis = axis,
            Kind = "pair",
            ShortStatement = statement,
            Statement = statement,
            Grade = grade,
            Share = share,
            ShareLower = share - 0.1,
            ShareUpper = share + 0.1
        };

    private static RootCauseResult Comparison() => new()
    {
        Scope = RootCauseEngine.ComparisonScope,
        Origin = RootCauseOrigins.Comparison,
        ComputedAtUtc = new DateTime(2026, 10, 15, 8, 30, 0, DateTimeKind.Utc),
        PricingBasis = "As run",
        Models =
        {
            new RootCauseModel { Token = "A", Letter = "A", Label = "Orion Max", Provider = "Northwind", ModelId = "orion-max", EntryKey = "run:31" },
            new RootCauseModel { Token = "B", Letter = "B", Label = "Vega Pro", Provider = "Southstar", ModelId = "vega-pro", EntryKey = "run:32" }
        },
        PointsAtRisk =
        {
            Row("Orion Max", "A", "model", RootCauseSegments.AtRisk, "points", 2.0, 1.5, 6.0, 4.6),
            Row("Vega Pro", "B", "model", RootCauseSegments.AtRisk, "points", 0.4, 0.2, 1.0, 0.3)
        },
        SpeedRows =
        {
            Row("Orion Max", "A", "model", RootCauseSegments.Time, "seconds", 3.1, 10.4, 2.0, 0.3, 0.6),
            Unavailable("Vega Pro", "B", "model", "seconds", "no candidate call telemetry")
        },
        CostRows =
        {
            Row("Orion Max", "A", "model", RootCauseSegments.CostComponents, "usd", 0.004, 0.0002, 0.0001, 0.008, 0.006),
            Row("Vega Pro", "B", "model", RootCauseSegments.CostComponents, "usd", 0.002, 0.0001, 0.0, 0.003, 0.001)
        },
        Hypotheses =
        {
            Hypothesis("HS3", RootCauseAxes.Speed, RootCauseGrades.Moderate, "Model A is slower mainly from more output tokens", 0.62),
            Hypothesis("HQ2", RootCauseAxes.Intelligence, RootCauseGrades.Low, "Model A's shortfall is in confirmed critical errors"),
            Hypothesis("HC1", RootCauseAxes.Cost, RootCauseGrades.High, "Model A costs more mainly from token volume", 0.71)
        }
    };

    private static RootCauseResult Standalone() => new()
    {
        Scope = RootCauseEngine.ModelScope,
        Origin = RootCauseOrigins.RunCompletion,
        ComputedAtUtc = new DateTime(2026, 10, 15, 8, 30, 0, DateTimeKind.Utc),
        PricingBasis = "As run",
        PointsAtRisk =
        {
            Row("Orion Max", null, "model", RootCauseSegments.AtRisk, "points", 2.0, 1.5, 6.0, 4.6),
            Row("Simple questions", null, "band", RootCauseSegments.AtRisk, "points", 0.5, 0.2, 1.0, 1.1),
            Row("Advanced questions", null, "band", RootCauseSegments.AtRisk, "points", 1.5, 1.3, 5.0, 3.5)
        },
        SpeedRows = { Row("Orion Max", null, "model", RootCauseSegments.Time, "seconds", 3.1, 10.4, 2.0, 0.3, 0.6) },
        CostRows = { Row("Orion Max", null, "model", RootCauseSegments.CostComponents, "usd", 0.004, 0.0002, 0.0001, 0.008, 0.006) },
        Hypotheses = { Hypothesis("WS2", RootCauseAxes.Speed, RootCauseGrades.Moderate, "Most of Orion Max's time is output generation", 0.55) }
    };

    private static BenchmarkReportFactSheet Sheet() => new();

    private static string Squash(string text) => new(text.Where(c => !char.IsWhiteSpace(c)).ToArray());

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AllFourCharts_AreDrawn_WithinTheStoreLimits(bool comparison)
    {
        var charts = RootCauseChartRenderer.Draw(comparison ? Comparison() : Standalone(), Sheet(), anonymized: false);

        Assert.Equal(BenchmarkReportChartPlacement.ServerFigureKeys, charts.Select(c => c.Key));
        Assert.All(charts, chart =>
        {
            Assert.True(BenchmarkReportChartStore.TryReadPngSize(chart.Png, out int width, out int height));
            Assert.Equal((chart.Width, chart.Height), (width, height));
            Assert.InRange(width, BenchmarkReportChartStore.MinDimensionPx, BenchmarkReportChartStore.MaxDimensionPx);
            Assert.InRange(height, BenchmarkReportChartStore.MinDimensionPx, BenchmarkReportChartStore.MaxDimensionPx);
            Assert.True(chart.Png.Length <= BenchmarkReportChartStore.MaxPngBytes);
            Assert.InRange(chart.AltText.Length, 1, BenchmarkReportChartStore.MaxAltTextChars);
            Assert.Contains("Drawn by Overseer from the analysis computed", chart.Caption);
            Assert.True(chart.Caption.Length <= BenchmarkReportChartStore.MaxCaptionChars);
        });
        Assert.Contains("median per question", charts.Single(c => c.Key == BenchmarkReportChartPlacement.RcaSpeedKey).Caption);
        Assert.Contains("As run", charts.Single(c => c.Key == BenchmarkReportChartPlacement.RcaCostKey).Caption);
    }

    [Fact]
    public void TheAltText_StartsWithTheTitle_AndNamesEveryRow()
    {
        var chart = RootCauseChartRenderer.Draw(Standalone(), Sheet(), anonymized: false)
            .Single(c => c.Key == BenchmarkReportChartPlacement.RcaPointsAtRiskKey);

        Assert.StartsWith(RootCauseChartRenderer.PointsAtRiskTitle, chart.AltText);
        Assert.Contains("Orion Max", chart.AltText);
        Assert.Contains("Simple questions", chart.AltText);
        Assert.Contains("Advanced questions", chart.AltText);
    }

    [Fact]
    public void ALongAltText_IsCutAtARowBoundary_AndSaysHowManyRowsAreLeft()
    {
        var result = Standalone();
        result.PointsAtRisk.Clear();
        for (int i = 0; i < 80; i++)
        {
            result.PointsAtRisk.Add(Row("A rather long row label number " + i, null, "band", RootCauseSegments.AtRisk, "points", 1, 2, 3, 4));
        }

        var chart = RootCauseChartRenderer.Draw(result, Sheet(), anonymized: false)
            .Single(c => c.Key == BenchmarkReportChartPlacement.RcaPointsAtRiskKey);

        Assert.True(chart.AltText.Length <= BenchmarkReportChartStore.MaxAltTextChars);
        Assert.Matches(@"and \d+ more rows", chart.AltText);
    }

    [Fact]
    public void AnUnavailableRow_ReadsNotRecorded_WithItsReason()
    {
        var pdf = RootCauseChartRenderer.DrawPdfs(Comparison(), Sheet(), anonymized: false)
            .Single(p => p.Key == BenchmarkReportChartPlacement.RcaSpeedKey).Value;

        using var reader = PdfDocument.Open(pdf);
        string text = Squash(string.Concat(reader.GetPages().Select(p => p.Text)));
        Assert.Contains(Squash("not recorded (no candidate call telemetry)"), text);
    }

    [Fact]
    public void AChartWithNoAvailableRow_IsNotDrawn()
    {
        var result = Standalone();
        result.SpeedRows.Clear();
        result.SpeedRows.Add(Unavailable("Orion Max", null, "model", "seconds", "no candidate call telemetry"));

        var charts = RootCauseChartRenderer.Draw(result, Sheet(), anonymized: false);

        Assert.DoesNotContain(charts, c => c.Key == BenchmarkReportChartPlacement.RcaSpeedKey);
        Assert.Equal(3, charts.Count);
    }

    [Fact]
    public void APdfReader_FindsEveryChartsKeyAndRowLabels()
    {
        // The title and caption print beside the figure in the document, not inside the image.
        var pdfs = RootCauseChartRenderer.DrawPdfs(Comparison(), Sheet(), anonymized: false);

        string Text(string key)
        {
            using var reader = PdfDocument.Open(pdfs.Single(p => p.Key == key).Value);
            return Squash(string.Concat(reader.GetPages().Select(p => p.Text)));
        }

        string risk = Text(BenchmarkReportChartPlacement.RcaPointsAtRiskKey);
        Assert.Contains(Squash(RootCauseSegments.Label(RootCauseSegments.CriticalError)), risk);
        Assert.Contains("OrionMax", risk);
        Assert.Contains("VegaPro", risk);

        string hypotheses = Text(BenchmarkReportChartPlacement.RcaHypothesesKey);
        Assert.Contains("HS3", hypotheses);
        Assert.Contains("Moderate", hypotheses);
        Assert.Contains("High", hypotheses);
    }

    [Fact]
    public void ASegment_PrintsItsValue_WhereTheValueFits()
    {
        var pdf = RootCauseChartRenderer.DrawPdfs(Comparison(), Sheet(), anonymized: false)
            .Single(p => p.Key == BenchmarkReportChartPlacement.RcaSpeedKey).Value;

        using var reader = PdfDocument.Open(pdf);
        string text = Squash(string.Concat(reader.GetPages().Select(p => p.Text)));
        // Output generation, 10.4 s of a 20 s axis, is wide enough for its value; the key names the segments by color alone.
        Assert.Contains("10.4", text);
        Assert.Contains(Squash(RootCauseSegments.Label(RootCauseSegments.OutputGeneration)), text);
        Assert.Contains(Squash(RootCauseChartRenderer.SegmentValue(10.4, "seconds")), text);
        Assert.DoesNotContain(Squash("1 " + RootCauseSegments.Label(RootCauseSegments.Ttft)), text);

        Assert.Equal("10.4", RootCauseChartRenderer.SegmentValue(10.4, "seconds"));
        Assert.Equal("6.0", RootCauseChartRenderer.SegmentValue(6, "points"));
        Assert.Equal("$0.0040", RootCauseChartRenderer.SegmentValue(0.004, "usd"));
        Assert.Equal("$0.012", RootCauseChartRenderer.SegmentValue(0.012, "usd"));
    }

    [Theory]
    [InlineData(60, 60)]
    [InlineData(55, 60)]
    [InlineData(61, 80)]
    [InlineData(11.5, 12)]
    [InlineData(1.3, 1.5)]
    [InlineData(16.4, 20)]
    [InlineData(0.026, 0.03)]
    [InlineData(100, 100)]
    [InlineData(0, 1)]
    public void TheAxis_EndsAtTheNextNiceValue(double value, double ceiling)
    {
        Assert.Equal(ceiling, RootCauseChartRenderer.NiceCeiling(value), 12);
    }

    [Fact]
    public void TheHypothesisChart_LeavesOutTheNulls_AndTheNamedVariantNamesTheModels()
    {
        var result = Comparison();
        result.Hypotheses.Add(new RootCauseHypothesis
        {
            Id = "H0-Q", Number = 1, Key = "H0-Q.1", Axis = RootCauseAxes.Intelligence, Kind = "null",
            ShortStatement = "No difference between Model A and Model B", Statement = "No difference between Model A and Model B",
            Grade = RootCauseGrades.NotSupported
        });

        string named = RootCauseChartRenderer.Draw(result, Sheet(), anonymized: false).Single(c => c.Key == BenchmarkReportChartPlacement.RcaHypothesesKey).AltText;
        Assert.DoesNotContain("H0-Q", named);
        Assert.Contains("HS3.1 Orion Max is slower mainly from more output tokens", named);
        Assert.DoesNotContain("Model A", named);

        string anonymized = RootCauseChartRenderer.Draw(result, Sheet(), anonymized: true).Single(c => c.Key == BenchmarkReportChartPlacement.RcaHypothesesKey).AltText;
        Assert.Contains("HS3.1 Model A is slower mainly from more output tokens", anonymized);
        Assert.DoesNotContain("Orion", anonymized);

        // A result of null hypotheses alone draws no hypothesis chart.
        var nullsOnly = Comparison();
        nullsOnly.Hypotheses.RemoveAll(h => h.Kind != "null");
        nullsOnly.Hypotheses.Add(result.Hypotheses.Single(h => h.Kind == "null"));
        Assert.DoesNotContain(RootCauseChartRenderer.Draw(nullsOnly, Sheet(), anonymized: false), c => c.Key == BenchmarkReportChartPlacement.RcaHypothesesKey);
    }

    /// <summary>Model Comparison #5's entries: two share every word but the thinking level.</summary>
    private static readonly IReadOnlyDictionary<string, string> SharedPrefixLabels = new Dictionary<string, string>
    {
        ["A"] = "GPT-6 Luna @ high",
        ["B"] = "GPT-6.1 Terra @ medium",
        ["C"] = "GPT-6.1 Terra @ high"
    };

    [Fact]
    public void AHypothesisLabel_ThatWouldCutALabelSharingItsFirstWords_ShortensItFromTheMiddle()
    {
        string medium = RootCauseChartRenderer.HypothesisChartLabel(
            Hypothesis("HQ2", RootCauseAxes.Intelligence, RootCauseGrades.Low, "Concise style: Model B"), SharedPrefixLabels);
        string high = RootCauseChartRenderer.HypothesisChartLabel(
            Hypothesis("HQ2", RootCauseAxes.Intelligence, RootCauseGrades.Low, "Concise style: Model C"), SharedPrefixLabels);

        // The column would have cut both at "GPT-6.1 Ter…"; the shortened labels keep what tells them apart.
        Assert.Equal("HQ2.1 Concise style: GPT…medium", medium);
        Assert.Equal("HQ2.1 Concise style: GPT-6…high", high);
        Assert.All(new[] { medium, high }, l => Assert.True(l.Length <= RootCauseChartRenderer.HypothesisLabelChars));

        // A line one character too long loses only what it must.
        Assert.Equal("HC1.1 Cost: GPT-6.1 Terra…high", RootCauseChartRenderer.HypothesisChartLabel(
            Hypothesis("HC1", RootCauseAxes.Cost, RootCauseGrades.High, "Cost: Model C"), SharedPrefixLabels));
    }

    [Fact]
    public void AHypothesisLabel_KeepsADistinctLabel_AndALineThatFits_Unchanged()
    {
        // GPT-6 Luna differs from the Terra entries within its first word: cut or not, it stays whole.
        Assert.Equal("HQ2.1 Concise style: GPT-6 Luna @ high", RootCauseChartRenderer.HypothesisChartLabel(
            Hypothesis("HQ2", RootCauseAxes.Intelligence, RootCauseGrades.Low, "Concise style: Model A"), SharedPrefixLabels));

        var distinct = new Dictionary<string, string> { ["A"] = "Orion Max", ["B"] = "Vega Pro" };
        Assert.Equal("HS3.1 Orion Max is slower mainly from more output tokens", RootCauseChartRenderer.HypothesisChartLabel(
            Hypothesis("HS3", RootCauseAxes.Speed, RootCauseGrades.Moderate, "Model A is slower mainly from more output tokens"), distinct));

        Assert.Equal("HC1.1 GPT-6.1 Terra @ high", RootCauseChartRenderer.HypothesisChartLabel(
            Hypothesis("HC1", RootCauseAxes.Cost, RootCauseGrades.High, "Model C"), SharedPrefixLabels));

        // The anonymized variant has no labels: the letters stand.
        Assert.Equal("HQ2.1 Concise style: Model B", RootCauseChartRenderer.HypothesisChartLabel(
            Hypothesis("HQ2", RootCauseAxes.Intelligence, RootCauseGrades.Low, "Concise style: Model B"), new Dictionary<string, string>()));
    }

    [Fact]
    public void TheCostKey_NamesOutputWithThinkingIncluded_ForAProviderThatDoesNotReportReasoning()
    {
        var result = Comparison();
        result.CostRows[1] = new RootCauseChartRow
        {
            Label = "Vega Pro", Letter = "B", Token = "B", Kind = "model", Unit = "usd",
            Segments = new[] { RootCauseSegments.UncachedInput, RootCauseSegments.CacheRead, RootCauseSegments.CacheWrite, RootCauseSegments.OutputWithThinking }
                .Select((k, i) => new RootCauseSegment { Key = k, Label = RootCauseSegments.Label(k), Value = new[] { 0.002, 0.0001, 0.0, 0.004 }[i] })
                .ToList(),
            Total = 0.0061
        };

        var pdf = RootCauseChartRenderer.DrawPdfs(result, Sheet(), anonymized: false).Single(p => p.Key == BenchmarkReportChartPlacement.RcaCostKey).Value;
        using var reader = PdfDocument.Open(pdf);
        string text = Squash(string.Concat(reader.GetPages().Select(p => p.Text)));

        Assert.Contains(Squash(RootCauseSegments.Label(RootCauseSegments.OutputWithThinking)), text);
        Assert.Contains(Squash(RootCauseSegments.Label(RootCauseSegments.Reasoning)), text);
    }

    [Fact]
    public void TheAnonymizedVariant_NamesNoModel()
    {
        var charts = RootCauseChartRenderer.Draw(Comparison(), Sheet(), anonymized: true);
        var pdfs = RootCauseChartRenderer.DrawPdfs(Comparison(), Sheet(), anonymized: true);

        Assert.All(charts, c =>
        {
            Assert.DoesNotContain("Orion", c.AltText);
            Assert.DoesNotContain("Vega", c.AltText);
            Assert.DoesNotContain("Orion", c.Caption);
        });
        foreach (var pdf in pdfs)
        {
            using var reader = PdfDocument.Open(pdf.Value);
            string text = string.Concat(reader.GetPages().Select(p => p.Text));
            Assert.DoesNotContain("Orion", text);
            Assert.DoesNotContain("Vega", text);
        }
        Assert.Contains("Model A", charts.Single(c => c.Key == BenchmarkReportChartPlacement.RcaPointsAtRiskKey).AltText);
    }

    [Fact]
    public void TheSameResult_DrawsTheSameImages()
    {
        var first = RootCauseChartRenderer.Draw(Comparison(), Sheet(), anonymized: false);
        var second = RootCauseChartRenderer.Draw(Comparison(), Sheet(), anonymized: false);

        Assert.Equal(first.Select(c => c.AltText), second.Select(c => c.AltText));
        Assert.Equal(first.Select(c => (c.Width, c.Height)), second.Select(c => (c.Width, c.Height)));
    }

    [Fact]
    public void ThePalettes_HaveFiveSegmentsAndFourGrades()
    {
        Assert.Equal(5, RootCauseChartPalette.Segments.Count);
        Assert.Equal(4, RootCauseChartPalette.Grades.Count);
        Assert.Equal(RootCauseChartPalette.Segments.Count, RootCauseChartPalette.Segments.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}
