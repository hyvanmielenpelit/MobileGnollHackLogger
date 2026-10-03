namespace Overseer.Models;

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

// DTOs for paired tests: the Paired tests view of the Model Comparison wizard, the Paired Test tab of
// the single-run report, and the speed, cost and dimension rows of the battery report's Paired Test
// tab. Every figure is computed by BenchmarkPairedTests from the existing statistical primitives.

/// <summary>Which pairs the wizard's Paired tests view tests.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BenchmarkPairedComparisonMode
{
    /// <summary>Every other comparable entry against one reference: k − 1 tests per measure.</summary>
    Reference = 0,

    /// <summary>Every pair of comparable entries: k(k − 1)/2 tests per measure, at most 12 entries.</summary>
    AllPairs = 1
}

/// <summary>
/// The wizard's paired tests: the same sources as <see cref="BenchmarkModelComparisonRequest"/>, which
/// the server compares again so the entries, condition and degrade flags are exactly the wizard's.
/// </summary>
public class BenchmarkPairedComparisonRequest
{
    public List<long> RunIds { get; set; } = new();

    public List<long> GroupIds { get; set; } = new();

    /// <summary>Battery results; a request naming any may name no run or group.</summary>
    public List<long> BatteryRunIds { get; set; } = new();

    /// <summary>Sent as a number, as on the report-pack request: 0 = AsRun, 1 = Current.</summary>
    public BenchmarkModelComparisonPricingBasis PricingBasis { get; set; }
        = BenchmarkModelComparisonPricingBasis.Current;

    /// <summary>`Reference` or `AllPairs` (a number is accepted too). Ignored with two comparable entries.</summary>
    public BenchmarkPairedComparisonMode Mode { get; set; } = BenchmarkPairedComparisonMode.Reference;

    /// <summary>
    /// The entry key every other entry is tested against in <c>Reference</c> mode. Null takes the
    /// comparable entry with the highest Intelligence Index.
    /// </summary>
    public string? ReferenceKey { get; set; }

    /// <summary>Bypasses the ten-minute response cache.</summary>
    public bool Recompute { get; set; }
}

/// <summary>The wizard's paired tests: one family per measure.</summary>
public class BenchmarkPairedComparisonDto
{
    public DateTime ComputedAtUtc { get; set; }

    /// <summary>`Runs` or `Batteries`, as on the comparison.</summary>
    public string SubjectKind { get; set; } = string.Empty;

    /// <summary>`AsRun` or `Current`.</summary>
    public string PricingBasis { get; set; } = string.Empty;

    /// <summary>`Reference` or `AllPairs`.</summary>
    public string Mode { get; set; } = string.Empty;

    /// <summary>
    /// The reference entry: the one requested, else the default. Present in both modes, so a view
    /// can offer it when switching to <c>Reference</c>.
    /// </summary>
    public string? ReferenceKey { get; set; }

    /// <summary>The comparable entries that took part, in the comparison's order.</summary>
    public List<string> EntryKeys { get; set; } = new();

    /// <summary>The largest number of comparable entries <c>AllPairs</c> is offered for.</summary>
    public int AllPairsLimit { get; set; }

    /// <summary>Set whenever an entry has a single run (for a battery result, one run per suite).</summary>
    public string? SingleRunCaveat { get; set; }

    /// <summary>States that each measure is its own family and the measures are not adjusted for each other.</summary>
    public string MeasuresNote { get; set; } = string.Empty;

    /// <summary>Intelligence first, then Accuracy, Completeness, Conciseness, Readability, Speed and Cost.</summary>
    public List<BenchmarkPairedMeasureDto> Measures { get; set; } = new();
}

/// <summary>One measure's family of paired tests.</summary>
public class BenchmarkPairedMeasureDto
{
    /// <summary>`Intelligence`, `Accuracy`, `Completeness`, `Conciseness`, `Readability`, `Speed` or `Cost`.</summary>
    public string Measure { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    /// <summary>`Intelligence`, `QualityDimension`, `Speed` or `Cost`.</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>True for the Intelligence Index, the primary test.</summary>
    public bool Primary { get; set; }

    /// <summary>The tests actually made in this family: pairs with a p-value.</summary>
    public int FamilySize { get; set; }

    /// <summary>`None` or `Holm`.</summary>
    public string Adjustment { get; set; } = string.Empty;

    /// <summary>Ready to render: "Single comparison — no adjustment needed", "Holm-adjusted across 3 tests".</summary>
    public string AdjustmentNote { get; set; } = string.Empty;

    /// <summary>Set when no pair of this measure was tested, and why.</summary>
    public string? NotTestedReason { get; set; }

    /// <summary>A standing caption for the measure, such as how battery dimensions are pooled.</summary>
    public string? Caption { get; set; }

    public List<BenchmarkPairedTestDto> Pairs { get; set; } = new();
}

/// <summary>One paired test: treatment B against baseline A on the questions both answered.</summary>
public class BenchmarkPairedTestDto
{
    public string BaselineKey { get; set; } = string.Empty;
    public string TreatmentKey { get; set; } = string.Empty;

    /// <summary>Questions (for a battery, suite and question) in the test.</summary>
    public int PairedItems { get; set; }

    /// <summary>Questions present on one side only.</summary>
    public int UnpairedItems { get; set; }

    /// <summary>Questions on both sides graded under different rubric revisions, and left out.</summary>
    public int RevisionMismatched { get; set; }

    /// <summary>B − A for a difference; B ÷ A, the geometric-mean ratio, for a ratio.</summary>
    public double? Effect { get; set; }

    /// <summary>The 95 % interval; for a ratio, exponentiated from the log scale.</summary>
    public double? EffectLower { get; set; }

    public double? EffectUpper { get; set; }

    /// <summary>`Difference` or `Ratio`.</summary>
    public string EffectKind { get; set; } = string.Empty;

    /// <summary>Cohen's dz of the paired differences (of the log ratios for speed and cost).</summary>
    public double? Dz { get; set; }

    /// <summary>The tested p-value; null when the pair was not tested or every difference was zero.</summary>
    public double? PValue { get; set; }

    /// <summary>Holm-adjusted within the measure's family; equal to <see cref="PValue"/> in a family of one.</summary>
    public double? AdjustedPValue { get; set; }

    public string Method { get; set; } = string.Empty;

    /// <summary>`Higher`, `Lower` or `None`: the sign of the effect, B relative to A.</summary>
    public string Direction { get; set; } = string.Empty;

    /// <summary>The adjusted p-value is below 0.05.</summary>
    public bool Established { get; set; }

    /// <summary>"Higher on the same questions", "Faster on the same questions", "No difference established", "Not tested".</summary>
    public string Verdict { get; set; } = string.Empty;

    /// <summary>Why this pair was not tested.</summary>
    public string? NotTestedReason { get; set; }

    public string? Note { get; set; }

    /// <summary>For a battery Intelligence row only: the per-suite figures behind the composite test.</summary>
    public List<BenchmarkPairedSuiteDetailDto>? Suites { get; set; }
}

/// <summary>One suite of a battery Intelligence test (M7): its weighted difference and Holm-adjusted Wilcoxon p.</summary>
public class BenchmarkPairedSuiteDetailDto
{
    public int SuiteIndex { get; set; }
    public string SuiteName { get; set; } = string.Empty;
    public int PairedItems { get; set; }
    public double? WeightedDifference { get; set; }
    public double? WilcoxonPValue { get; set; }

    /// <summary>Holm-adjusted across the suites with a Wilcoxon p.</summary>
    public double? HolmAdjustedPValue { get; set; }

    public string? Note { get; set; }
}

/// <summary>The body of <c>POST runs/{id}/paired-comparison</c>: this run is the treatment.</summary>
public class BenchmarkRunPairedComparisonRequest
{
    public long BaselineRunId { get; set; }

    /// <summary>Sent as a number: 0 = AsRun, 1 = Current.</summary>
    public BenchmarkModelComparisonPricingBasis PricingBasis { get; set; }
        = BenchmarkModelComparisonPricingBasis.Current;
}

/// <summary>The body of <c>POST model-comparison/paired/battery</c>: two battery results of one definition.</summary>
public class BenchmarkBatteryPairedComparisonRequest
{
    /// <summary>The treatment battery run.</summary>
    public long BatteryRunId { get; set; }

    public long BaselineBatteryRunId { get; set; }

    /// <summary>Sent as a number: 0 = AsRun, 1 = Current.</summary>
    public BenchmarkModelComparisonPricingBasis PricingBasis { get; set; }
        = BenchmarkModelComparisonPricingBasis.Current;
}

/// <summary>
/// One pair, tested on every measure without adjustment: two runs on one suite, or two battery
/// results of one definition. The kind names what the pair can show.
/// </summary>
public class BenchmarkPairComparisonDto
{
    public DateTime ComputedAtUtc { get; set; }

    /// <summary>`Run` or `Battery`.</summary>
    public string SubjectKind { get; set; } = string.Empty;

    /// <summary>The run or battery run id of each side.</summary>
    public long TreatmentId { get; set; }

    public long BaselineId { get; set; }

    /// <summary>`run:12` or `battery:7`.</summary>
    public string TreatmentKey { get; set; } = string.Empty;

    public string BaselineKey { get; set; } = string.Empty;

    public string TreatmentLabel { get; set; } = string.Empty;
    public string BaselineLabel { get; set; } = string.Empty;

    /// <summary>`ModelComparison`, `Verification` or `Replicate` (a battery pair is never a replicate).</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>"Model comparison", "Verification of a change" or "Replicate".</summary>
    public string KindLabel { get; set; } = string.Empty;

    /// <summary>A sentence naming the kind and exactly what differs.</summary>
    public string Explanation { get; set; } = string.Empty;

    /// <summary>The model-axis keys of a model comparison, the instrument key of a verification; empty for a replicate.</summary>
    public List<string> ChangedKeys { get; set; } = new();

    /// <summary>Every key the two sides differ on, degrading keys and item revisions included.</summary>
    public List<BenchmarkComparabilityDifferenceDto> Differences { get; set; } = new();

    public bool SpeedDegraded { get; set; }
    public List<string> SpeedDegradingKeys { get; set; } = new();
    public bool CostDegraded { get; set; }
    public List<string> CostDegradingKeys { get; set; } = new();

    /// <summary>Set whenever a side has a single run (for a battery result, one run per suite).</summary>
    public string? SingleRunCaveat { get; set; }

    public string PricingBasis { get; set; } = string.Empty;

    /// <summary>The same measures as the wizard, one pair each, unadjusted.</summary>
    public List<BenchmarkPairedMeasureDto> Measures { get; set; } = new();
}

/// <summary>The body of <c>POST runs/{id}/paired-comparison/kinds</c>: the candidate baselines to classify.</summary>
public class BenchmarkRunPairKindsRequest
{
    public List<long> RunIds { get; set; } = new();
}

/// <summary>The kind of paired comparison one candidate baseline would make with the run.</summary>
public class BenchmarkRunPairKindDto
{
    public long RunId { get; set; }

    /// <summary>`ModelComparison`, `Verification`, `Replicate` or `NotComparable`.</summary>
    public string Kind { get; set; } = string.Empty;

    public string KindLabel { get; set; } = string.Empty;

    public List<string> ChangedKeys { get; set; } = new();

    /// <summary>The explanation; for `NotComparable`, the reason.</summary>
    public string Explanation { get; set; } = string.Empty;
}
