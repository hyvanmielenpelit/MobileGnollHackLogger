namespace Overseer.Services.Benchmarking.Charts;

using System.Collections.Generic;

/// <summary>
/// The colors of the server-drawn Root Cause Analysis charts, for print on white. The segment palette
/// colors the stacked bars of R1 to R3 by stacking position; the grade ramp colors the confidence dots
/// and share bars of R4. Text always wears the ink, never a segment or grade color.
/// </summary>
public static class RootCauseChartPalette
{
    /// <summary>The chart surface: the white page the figures print on.</summary>
    public const string Surface = "#ffffff";

    /// <summary>Primary ink: labels, totals and the interval whiskers.</summary>
    public const string Ink = "#0b0b0b";

    /// <summary>Secondary ink: the key, the axis and the "not recorded" rows.</summary>
    public const string SecondaryInk = "#52514e";

    /// <summary>The baseline under the bars and the grade axis guides.</summary>
    public const string Baseline = "#c3c2b7";

    /// <summary>
    /// The five segment colors, in stacking order: the dataviz reference slots yellow, green, magenta,
    /// violet and red. None is one of the three provider hues (blue, orange and aqua in
    /// <c>model-comparison-charts.ts</c>), because a segment is a cause, not a provider. R1 uses the first
    /// four. The order is the one of the 120 that keeps the worst adjacent pair furthest apart under both
    /// simulated color blindness and normal vision.
    ///
    /// Validate with, from the dataviz skill's base directory:
    ///   node scripts/validate_palette.js "#eda100,#008300,#e87ba4,#4a3aa7,#e34948" --mode light --surface "#ffffff"
    /// Result on 2026-10-15: lightness band, chroma floor, CVD separation (worst adjacent ΔE 16.2, protan,
    /// green and yellow) and normal-vision floor (worst adjacent ΔE 30.3) pass; contrast warns for yellow
    /// (2.17:1) and magenta (2.69:1) on white. The relief is visible text: every segment of 8% of its bar
    /// or more carries its number, and the key names every segment in stacking order. The first four
    /// alone give the same result.
    /// </summary>
    public static readonly IReadOnlyList<string> Segments = new[] { "#eda100", "#008300", "#e87ba4", "#4a3aa7", "#e34948" };

    /// <summary>
    /// The ink of the number printed inside each segment, aligned with <see cref="Segments"/>: whichever
    /// of <see cref="Ink"/> and white contrasts more with the fill, each at least 4.5:1 (9.09, 4.95,
    /// 7.31, 8.56 and 4.98).
    /// </summary>
    public static readonly IReadOnlyList<string> SegmentLabelInk = new[] { Ink, Surface, Ink, Surface, Ink };

    /// <summary>
    /// The grade ramp, one hue in four lightness steps from Not supported to High: steps 250, 400, 550
    /// and 700 of the dataviz reference blue ramp, the light end at step 250 as an ordinal ramp on a light
    /// surface requires.
    ///
    /// Validate with, from the dataviz skill's base directory:
    ///   node scripts/validate_palette.js "#86b6ef,#3987e5,#1c5cab,#0d366b" --ordinal --surface "#ffffff"
    /// Result on 2026-10-15: lightness monotone, adjacent ΔL at least 0.06, light-end contrast 2.11:1 and
    /// single hue (spread 4°) pass. The grade is also printed as text beside each dot.
    /// </summary>
    public static readonly IReadOnlyList<string> Grades = new[] { "#86b6ef", "#3987e5", "#1c5cab", "#0d366b" };

    /// <summary>The segment color of a stacking position; positions past the palette reuse the last color.</summary>
    public static string Segment(int index) => Segments[System.Math.Clamp(index, 0, Segments.Count - 1)];

    /// <summary>The ink of a stacking position's number, as <see cref="Segment"/> picks its color.</summary>
    public static string SegmentLabel(int index) => SegmentLabelInk[System.Math.Clamp(index, 0, SegmentLabelInk.Count - 1)];

    /// <summary>The color of a grade rank, 0 (Not supported) to 3 (High); an unknown rank takes Not supported's.</summary>
    public static string Grade(int rank) => Grades[System.Math.Clamp(rank, 0, Grades.Count - 1)];
}
