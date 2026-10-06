namespace Overseer.Services.Benchmarking.Pdf;

using System;
using System.Collections.Generic;
using System.Linq;
using CellAlign = Overseer.Services.Benchmarking.Pdf.BenchmarkPdfMarkdownComposer.CellAlign;

/// <summary>A long table header printed short, with the legend line that spells it out.</summary>
internal sealed record BenchmarkTableAbbreviation(int Column, string Short, string Long);

/// <summary>
/// How a pipe table is set, shared by the PDF and the Word document: the table text size, the
/// columns of the grid and their widths, the headers printed short, and the column moved out of the
/// grid onto a full-width second line of each body row. Decided by
/// <see cref="BenchmarkPdfMarkdownComposer.TableLayout"/>.
/// </summary>
internal sealed record BenchmarkTableLayout
{
    /// <summary>The deterministic tables' long headers and their short forms, matched on the header's whole text.</summary>
    public static readonly IReadOnlyDictionary<string, string> HeaderAbbreviations = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Assessed band"] = "Band",
        ["Refuted answer sentences"] = "Refuted",
        ["Critical errors"] = "Crit.",
        ["Contribution"] = "Contrib.",
        ["Cost per run, graders included"] = "Cost/run",
        ["Critical-error rate"] = "Crit. rate"
    };

    /// <summary>The header of the per-question tables' column that may move onto a second line.</summary>
    public const string SecondLineHeader = "Topic";

    /// <summary>Each source column's alignment.</summary>
    public required CellAlign[] Aligns { get; init; }

    /// <summary>The source columns set in the grid, in order: every column but <see cref="SecondLineColumn"/>.</summary>
    public required int[] GridColumns { get; init; }

    /// <summary>
    /// The PDF's width of each grid column: constant widths in points when <see cref="Constant"/>, else
    /// relative widths, in points for the A4 text width.
    /// </summary>
    public required float[] Widths { get; init; }

    /// <summary>Each grid column's relative width across the full text width, for Word.</summary>
    public required float[] Weights { get; init; }

    /// <summary>A narrow table, set at its preferred widths against the left margin.</summary>
    public bool Constant { get; init; }

    /// <summary>The table text size, in points.</summary>
    public required float FontSize { get; init; }

    /// <summary>The headers printed short, in column order.</summary>
    public IReadOnlyList<BenchmarkTableAbbreviation> Abbreviations { get; init; } = Array.Empty<BenchmarkTableAbbreviation>();

    /// <summary>The source column printed on a full-width second line of each body row; null when every column is in the grid.</summary>
    public int? SecondLineColumn { get; init; }

    /// <summary>Every grid column holds its longest word; false when even the last fallback leaves a word to break.</summary>
    public bool Fits { get; init; } = true;

    /// <summary>The line under the table that spells out its short headers, as "Band: Assessed band · Refuted: …"; null when none is short.</summary>
    public string? Legend => Abbreviations.Count == 0
        ? null
        : string.Join(" · ", Abbreviations.Select(a => a.Short + ": " + a.Long));

    /// <summary>The short header printed for a source column; null when its header prints as written.</summary>
    public string? HeaderTextOf(int column) => Abbreviations.FirstOrDefault(a => a.Column == column)?.Short;

    /// <summary>
    /// The grid columns a cell at source column <paramref name="column"/> spanning <paramref name="span"/>
    /// source columns takes: the first grid index and the count, 0 when it covers only the second-line column.
    /// </summary>
    public (int First, int Span) GridSpanOf(int column, int span)
    {
        int first = -1;
        int count = 0;
        for (int g = 0; g < GridColumns.Length; g++)
        {
            if (GridColumns[g] >= column && GridColumns[g] < column + span)
            {
                if (first < 0) first = g;
                count++;
            }
        }
        return (Math.Max(first, 0), count);
    }
}
