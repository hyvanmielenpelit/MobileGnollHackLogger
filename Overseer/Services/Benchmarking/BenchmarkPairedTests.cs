namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services;

/// <summary>One suite of a paired-test entry: its runs, with their answers.</summary>
public sealed record BenchmarkPairedSuiteRuns(int SuiteIndex, string SuiteName, IReadOnlyList<BenchmarkRun> Runs);

/// <summary>
/// One side of a paired test with everything it needs loaded: the group statistics of a run or group
/// entry, or the persisted result of a battery entry; the runs with their answers, by suite; the
/// candidate price cards on the comparison's basis; and the degraded axes it sits on.
/// </summary>
public sealed record BenchmarkPairedEntry
{
    /// <summary>`run:12`, `group:3` or `battery:7`.</summary>
    public string Key { get; init; } = string.Empty;

    public string Label { get; init; } = string.Empty;

    public int RunCount { get; init; }

    /// <summary>The entry has one run (a battery result, one run in some suite).</summary>
    public bool SingleRun { get; init; }

    /// <summary>The Intelligence Index (for a battery result, the Overall Index); the default reference is the highest.</summary>
    public double? IntelligenceIndex { get; init; }

    /// <summary>The group statistics of a run or group entry; null on a battery entry.</summary>
    public BenchmarkGroupStatisticsResult? Statistics { get; init; }

    /// <summary>The persisted battery analysis of a battery entry; null on a run or group entry.</summary>
    public BenchmarkBatteryStatisticsResult? BatteryResult { get; init; }

    /// <summary>The runs with their answers: one suite at index 0 for a run or group entry.</summary>
    public IReadOnlyList<BenchmarkPairedSuiteRuns> Suites { get; init; } = Array.Empty<BenchmarkPairedSuiteRuns>();

    /// <summary>The candidate price card per run id; a missing or null card leaves cost untested.</summary>
    public IReadOnlyDictionary<long, ModelPricing?> CandidatePricing { get; init; } = new Dictionary<long, ModelPricing?>();

    public bool SpeedDegraded { get; init; }
    public string? SpeedDegradedReason { get; init; }
    public bool CostDegraded { get; init; }
    public string? CostDegradedReason { get; init; }

    public bool IsBattery => BatteryResult != null;
}

/// <summary>The kind of paired comparison two runs on one suite make.</summary>
public enum BenchmarkRunPairKind
{
    NotComparable = 0,

    /// <summary>Every must-match key agrees and a model-axis key differs.</summary>
    ModelComparison = 1,

    /// <summary>Every candidate key agrees and exactly one instrument key differs.</summary>
    Verification = 2,

    /// <summary>No quality-relevant key differs.</summary>
    Replicate = 3
}

/// <summary>What two runs differ on, and therefore what their paired test can show.</summary>
public sealed record BenchmarkRunPairClassification
{
    public BenchmarkRunPairKind Kind { get; init; }

    /// <summary>The differing model-axis keys of a model comparison, the instrument key of a verification.</summary>
    public IReadOnlyList<string> ChangedKeys { get; init; } = Array.Empty<string>();

    /// <summary>Every differing key of the 27, degrading keys and item revisions included.</summary>
    public IReadOnlyList<BenchmarkComparabilityKeyDifference> Differences { get; init; } = Array.Empty<BenchmarkComparabilityKeyDifference>();

    public IReadOnlyList<string> SpeedDegradingKeys { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> CostDegradingKeys { get; init; } = Array.Empty<string>();

    /// <summary>The kind and what differs; for <see cref="BenchmarkRunPairKind.NotComparable"/>, the reason.</summary>
    public string Explanation { get; init; } = string.Empty;
}

/// <summary>
/// Paired tests between comparison entries: which model is better on the same questions, and on
/// which measures. Each measure is its own family of tests, adjusted with Holm (1979) when it holds
/// more than one.
///
/// <para>Nothing statistical is implemented here. The Intelligence test of a run or group entry is
/// <see cref="BenchmarkGroupStatistics.Compare"/>, the one Multi-Run Analysis uses; of a battery
/// entry, <see cref="BenchmarkBatteryStatistics.Compare"/> (M7) with its fixed seed. The dimension,
/// speed and cost rows apply <see cref="BenchmarkGroupStatistics.WilcoxonSignedRank"/>,
/// <see cref="BenchmarkGroupStatistics.CohensDz"/> and <see cref="BenchmarkGroupStatistics.StudentTCritical95"/>
/// to per-question means, and the families use <see cref="BenchmarkBatteryStatistics.HolmAdjust"/>.</para>
///
/// <para>Pure: the I/O is <see cref="BenchmarkPairedTestsService"/>.</para>
/// </summary>
public static class BenchmarkPairedTests
{
    /// <summary>A paired test needs at least this many paired questions: the Report Pack's own minimum.</summary>
    public const int MinimumPairedItems = BenchmarkReportFacts.PairedMinimumQuestions;

    /// <summary>The largest number of comparable entries All pairs is offered for: 66 tests per measure.</summary>
    public const int AllPairsLimit = 12;

    /// <summary>An adjusted p-value below this establishes a difference.</summary>
    public const double SignificanceLevel = 0.05;

    public const string IntelligenceMeasure = "Intelligence";
    public const string SpeedMeasure = "Speed";
    public const string CostMeasure = "Cost";

    public static readonly IReadOnlyList<string> DimensionMeasures = new[] { "Accuracy", "Completeness", "Conciseness", "Readability" };

    public const string CategoryIntelligence = "Intelligence";
    public const string CategoryDimension = "QualityDimension";
    public const string CategorySpeed = "Speed";
    public const string CategoryCost = "Cost";

    public const string EffectDifference = "Difference";
    public const string EffectRatio = "Ratio";

    public const string AdjustmentNone = "None";
    public const string AdjustmentHolm = "Holm";

    public const string DirectionHigher = "Higher";
    public const string DirectionLower = "Lower";
    public const string DirectionNone = "None";

    public const string NoDifferenceVerdict = "No difference established";
    public const string NotTestedVerdict = "Not tested";

    public const string NeedsTwoEntriesError = "Comparing needs two comparable entries.";

    public const string ReferenceNotComparableError = "The reference must be one of the comparable entries of this comparison.";

    public const string SingleComparisonNote = "Single comparison — no adjustment needed";

    public const string MeasuresNote =
        "Each measure is its own family of tests. The measures are separate questions and are not adjusted for each other.";

    public const string SingleRunCaveat =
        "At least one entry has a single run. With one run a side, a paired test captures question sampling "
        + "only, not run-to-run variation, so it can look more certain than it is.";

    public const string BatterySingleRunCaveat =
        "At least one battery result has a single run in some suite. There, a paired test captures question "
        + "sampling only, not run-to-run variation, so it can look more certain than it is.";

    public const string BatteryDimensionCaption =
        "Pooled over (suite, question) pairs and unweighted across suites, unlike the Overall Index.";

    public const string RatioCaption =
        "Tested as Wilcoxon signed-rank on log(B / A) of the per-question means; the effect is the geometric-mean "
        + "ratio B ÷ A with a t interval on the log scale.";

    private const string AllZeroNote =
        "Every paired difference is zero, so the signed-rank test has nothing to rank.";

    public static string AllPairsLimitError(int comparableCount)
        => $"All pairs is offered for at most {AllPairsLimit} comparable entries; this comparison has "
           + $"{comparableCount.ToString(CultureInfo.InvariantCulture)}. Test against a reference instead.";

    // --- Entries ---------------------------------------------------------------------------------------

    /// <summary>
    /// A run or group entry over <paramref name="runs"/>, which carry their answers, measured on the exam
    /// they sat exactly as the comparison measures it.
    /// </summary>
    public static BenchmarkPairedEntry FromRuns(
        string key,
        string label,
        IReadOnlyList<BenchmarkRun> runs,
        IReadOnlyDictionary<long, ModelPricing?>? candidatePricing = null)
    {
        ArgumentNullException.ThrowIfNull(runs);

        var members = runs.Where(r => r != null).OrderBy(r => r.Id).ToList();
        if (members.Count == 0)
        {
            throw new ArgumentException("A paired-test entry needs at least one run.", nameof(runs));
        }

        var exam = BenchmarkRunExam.Build(members);
        var statistics = BenchmarkGroupStatistics.Compute(exam.Suite, exam.Questions, members);

        return new BenchmarkPairedEntry
        {
            Key = key,
            Label = label,
            RunCount = members.Count,
            SingleRun = members.Count == 1,
            IntelligenceIndex = statistics.Index.PointEstimate,
            Statistics = statistics,
            Suites = new[] { new BenchmarkPairedSuiteRuns(0, exam.Suite.Name, members) },
            CandidatePricing = candidatePricing ?? new Dictionary<long, ModelPricing?>()
        };
    }

    /// <summary>A battery entry: its persisted result, and its usable member runs by suite with their answers.</summary>
    public static BenchmarkPairedEntry FromBattery(
        string key,
        string label,
        BenchmarkBatteryStatisticsResult result,
        IReadOnlyList<BenchmarkPairedSuiteRuns> suites,
        IReadOnlyDictionary<long, ModelPricing?>? candidatePricing = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(suites);

        return new BenchmarkPairedEntry
        {
            Key = key,
            Label = label,
            RunCount = suites.SelectMany(s => s.Runs.Select(r => r.Id)).Distinct().Count(),
            SingleRun = suites.Any(s => s.Runs.Count == 1),
            IntelligenceIndex = result.OverallIndex?.PointEstimate,
            BatteryResult = result,
            Suites = suites,
            CandidatePricing = candidatePricing ?? new Dictionary<long, ModelPricing?>()
        };
    }

    // --- The wizard's families ---------------------------------------------------------------------------

    /// <summary>Null when the comparable entries can be tested in <paramref name="mode"/>; else the refusal.</summary>
    public static string? Validate(int comparableCount, BenchmarkPairedComparisonMode mode)
    {
        if (comparableCount < 2) return NeedsTwoEntriesError;
        if (mode == BenchmarkPairedComparisonMode.AllPairs && comparableCount > AllPairsLimit)
        {
            return AllPairsLimitError(comparableCount);
        }

        return null;
    }

    /// <summary>The entry with the highest Intelligence Index; ties go to the earlier entry.</summary>
    public static string DefaultReferenceKey(IReadOnlyList<BenchmarkPairedEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count == 0) throw new ArgumentException("No entries.", nameof(entries));

        return entries
            .Select((e, i) => (Entry: e, Order: i))
            .OrderByDescending(x => x.Entry.IntelligenceIndex ?? double.NegativeInfinity)
            .ThenBy(x => x.Order)
            .First()
            .Entry.Key;
    }

    /// <summary>
    /// The pairs tested, each as (baseline, treatment). Two entries: the reference against the other.
    /// <c>Reference</c>: the reference against every other entry, in entry order. <c>AllPairs</c>: entry i
    /// against entry j for every i &lt; j.
    /// </summary>
    public static IReadOnlyList<(BenchmarkPairedEntry Baseline, BenchmarkPairedEntry Treatment)> PlanPairs(
        IReadOnlyList<BenchmarkPairedEntry> entries,
        BenchmarkPairedComparisonMode mode,
        string referenceKey)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var reference = entries.FirstOrDefault(e => string.Equals(e.Key, referenceKey, StringComparison.Ordinal))
            ?? throw new ArgumentException(ReferenceNotComparableError, nameof(referenceKey));

        if (entries.Count == 2 || mode == BenchmarkPairedComparisonMode.Reference)
        {
            return entries
                .Where(e => !ReferenceEquals(e, reference))
                .Select(e => (reference, e))
                .ToList();
        }

        var pairs = new List<(BenchmarkPairedEntry, BenchmarkPairedEntry)>();
        for (int i = 0; i < entries.Count; i++)
        {
            for (int j = i + 1; j < entries.Count; j++)
            {
                pairs.Add((entries[i], entries[j]));
            }
        }

        return pairs;
    }

    /// <summary>The wizard's paired tests over the comparable <paramref name="entries"/>. Deterministic.</summary>
    public static BenchmarkPairedComparisonDto Build(
        IReadOnlyList<BenchmarkPairedEntry> entries,
        string subjectKind,
        BenchmarkModelComparisonPricingBasis basis,
        BenchmarkPairedComparisonMode mode,
        string referenceKey,
        DateTime computedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(entries);

        string? invalid = Validate(entries.Count, mode);
        if (invalid != null) throw new ArgumentException(invalid, nameof(entries));

        var pairs = PlanPairs(entries, mode, referenceKey);

        return new BenchmarkPairedComparisonDto
        {
            ComputedAtUtc = computedAtUtc,
            SubjectKind = subjectKind,
            PricingBasis = basis.ToString(),
            Mode = mode.ToString(),
            ReferenceKey = referenceKey,
            EntryKeys = entries.Select(e => e.Key).ToList(),
            AllPairsLimit = AllPairsLimit,
            SingleRunCaveat = SingleRunCaveatOf(entries),
            MeasuresNote = MeasuresNote,
            Measures = Families(pairs)
        };
    }

    /// <summary>One pair on every measure, each a family of one: no adjustment.</summary>
    public static List<BenchmarkPairedMeasureDto> ComparePair(BenchmarkPairedEntry baseline, BenchmarkPairedEntry treatment)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(treatment);

        return Families(new[] { (baseline, treatment) });
    }

    /// <summary>The caveat when any entry has a single run; null otherwise.</summary>
    public static string? SingleRunCaveatOf(IReadOnlyList<BenchmarkPairedEntry> entries)
    {
        var single = entries.Where(e => e.SingleRun).ToList();
        if (single.Count == 0) return null;
        return single.Any(e => e.IsBattery) ? BatterySingleRunCaveat : SingleRunCaveat;
    }

    /// <summary>The shared verdict wording: the words a family's tests are read by, here and in every view.</summary>
    public static string Verdict(string measure, bool established, string direction)
    {
        if (!established || direction == DirectionNone) return NoDifferenceVerdict;

        bool higher = direction == DirectionHigher;
        return measure switch
        {
            SpeedMeasure => higher ? "Slower on the same questions" : "Faster on the same questions",
            CostMeasure => higher ? "More expensive on the same questions" : "Cheaper on the same questions",
            _ => higher ? "Higher on the same questions" : "Lower on the same questions"
        };
    }

    /// <summary>
    /// Holm-adjusts one measure's family in place: the pairs with a p-value form the family, the rest keep
    /// a null adjusted p-value. A family of one is left unadjusted. Then sets every pair's verdict.
    /// </summary>
    public static void AdjustFamily(string measure, IReadOnlyList<BenchmarkPairedTestDto> tests)
    {
        ArgumentNullException.ThrowIfNull(tests);

        var tested = tests.Where(t => t.NotTestedReason == null && t.PValue.HasValue).ToList();
        var adjusted = tested.Count > 1
            ? BenchmarkBatteryStatistics.HolmAdjust(tested.Select(t => t.PValue!.Value).ToList())
            : tested.Select(t => t.PValue!.Value).ToList();

        for (int i = 0; i < tested.Count; i++)
        {
            tested[i].AdjustedPValue = adjusted[i];
        }

        foreach (var test in tests)
        {
            if (test.NotTestedReason != null)
            {
                test.AdjustedPValue = null;
                test.Established = false;
                test.Verdict = NotTestedVerdict;
                continue;
            }

            test.Established = test.AdjustedPValue.HasValue && test.AdjustedPValue.Value < SignificanceLevel;
            test.Verdict = Verdict(measure, test.Established, test.Direction);
        }
    }

    private static List<BenchmarkPairedMeasureDto> Families(
        IReadOnlyList<(BenchmarkPairedEntry Baseline, BenchmarkPairedEntry Treatment)> pairs)
    {
        bool battery = pairs.Any(p => p.Baseline.IsBattery || p.Treatment.IsBattery);
        var measures = new List<BenchmarkPairedMeasureDto>();

        measures.Add(Family(IntelligenceMeasure, "Intelligence Index", CategoryIntelligence, primary: true, caption: null,
            pairs.Select(p => p.Baseline.IsBattery
                ? BatteryIntelligence(p.Baseline, p.Treatment)
                : SuiteIntelligence(p.Baseline, p.Treatment)).ToList()));

        foreach (string dimension in DimensionMeasures)
        {
            measures.Add(Family(dimension, dimension, CategoryDimension, primary: false,
                caption: battery ? BatteryDimensionCaption : null,
                pairs.Select(p => DimensionTest(dimension, p.Baseline, p.Treatment)).ToList()));
        }

        measures.Add(Family(SpeedMeasure, "Speed (candidate model time per question)", CategorySpeed, primary: false,
            caption: RatioCaption,
            pairs.Select(p => RatioTest(SpeedMeasure, p.Baseline, p.Treatment)).ToList()));

        measures.Add(Family(CostMeasure, "Cost (candidate spend per question)", CategoryCost, primary: false,
            caption: RatioCaption,
            pairs.Select(p => RatioTest(CostMeasure, p.Baseline, p.Treatment)).ToList()));

        return measures;
    }

    private static BenchmarkPairedMeasureDto Family(
        string measure,
        string label,
        string category,
        bool primary,
        string? caption,
        List<BenchmarkPairedTestDto> tests)
    {
        AdjustFamily(measure, tests);

        int familySize = tests.Count(t => t.NotTestedReason == null && t.PValue.HasValue);

        string? notTested = null;
        if (tests.Count > 0 && tests.All(t => t.NotTestedReason != null))
        {
            var reasons = tests.Select(t => t.NotTestedReason!).Distinct(StringComparer.Ordinal).ToList();
            notTested = reasons.Count == 1 ? reasons[0] : "No pair could be tested on this measure; each pair says why.";
        }

        return new BenchmarkPairedMeasureDto
        {
            Measure = measure,
            Label = label,
            Category = category,
            Primary = primary,
            FamilySize = familySize,
            Adjustment = familySize > 1 ? AdjustmentHolm : AdjustmentNone,
            AdjustmentNote = familySize > 1
                ? $"Holm-adjusted across {familySize.ToString(CultureInfo.InvariantCulture)} tests"
                : familySize == 1 ? SingleComparisonNote : "No test was made in this family.",
            NotTestedReason = notTested,
            Caption = caption,
            Pairs = tests
        };
    }

    // --- Intelligence ----------------------------------------------------------------------------------

    /// <summary>Multi-Run Analysis's <i>Compare with group</i>, unchanged.</summary>
    private static BenchmarkPairedTestDto SuiteIntelligence(BenchmarkPairedEntry baseline, BenchmarkPairedEntry treatment)
    {
        var dto = NewTest(baseline, treatment, EffectDifference);

        if (baseline.Statistics == null || treatment.Statistics == null)
        {
            return NotTested(dto, "The per-question scores of an entry could not be read.");
        }

        var comparison = BenchmarkGroupStatistics.Compare(baseline.Statistics, treatment.Statistics);
        dto.PairedItems = comparison.PairedItemCount;
        dto.UnpairedItems = comparison.UnpairedItemCount;
        dto.RevisionMismatched = comparison.RevisionMismatchedItemCount;
        dto.Method = "Wilcoxon signed-rank on per-question mean quality, " + comparison.Wilcoxon.Method;

        if (comparison.PairedItemCount < MinimumPairedItems)
        {
            return TooFew(dto, comparison.PairedItemCount);
        }

        dto.Effect = comparison.MeanDifference;
        dto.EffectLower = comparison.DifferenceConfidenceLower;
        dto.EffectUpper = comparison.DifferenceConfidenceUpper;
        dto.Dz = comparison.CohensDz;
        dto.PValue = comparison.Wilcoxon.PValue;
        dto.Direction = DirectionOf(comparison.MeanDifference);
        dto.Note = WilcoxonNote(comparison.Wilcoxon);

        return dto;
    }

    /// <summary>M7: the stratified sign-flip test on the weighted composite difference, with the fixed seed.</summary>
    private static BenchmarkPairedTestDto BatteryIntelligence(BenchmarkPairedEntry baseline, BenchmarkPairedEntry treatment)
    {
        var dto = NewTest(baseline, treatment, EffectDifference);

        if (baseline.BatteryResult == null || treatment.BatteryResult == null)
        {
            return NotTested(dto, "The battery analysis of an entry could not be read.");
        }

        BenchmarkBatteryComparison m7;
        try
        {
            m7 = BenchmarkBatteryStatistics.Compare(baseline.BatteryResult, treatment.BatteryResult);
        }
        catch (ArgumentException ex)
        {
            return NotTested(dto, ex.Message);
        }

        dto.PairedItems = m7.PairedItemCount;
        dto.UnpairedItems = m7.Suites.Sum(s => s.Comparison?.UnpairedItemCount ?? 0);
        dto.RevisionMismatched = m7.Suites.Sum(s => s.Comparison?.RevisionMismatchedItemCount ?? 0);
        dto.Suites = m7.Suites.Select(s => new BenchmarkPairedSuiteDetailDto
        {
            SuiteIndex = s.SuiteIndex,
            SuiteName = s.SuiteName,
            PairedItems = s.PairedItemCount,
            WeightedDifference = s.WeightedDifference,
            WilcoxonPValue = s.WilcoxonPValue,
            HolmAdjustedPValue = s.HolmAdjustedPValue,
            Note = s.Note
        }).ToList();
        dto.Method = RandomizationMethod(m7);

        if (m7.PairedItemCount < MinimumPairedItems)
        {
            return TooFew(dto, m7.PairedItemCount);
        }

        if (!m7.CompositeDifference.HasValue || !m7.RandomizationPValue.HasValue)
        {
            var withheld = m7.Suites.Where(s => s.Note != null).Select(s => s.Note!).ToList();
            return NotTested(dto, withheld.Count > 0
                ? "The composite difference is withheld. " + string.Join(" ", withheld)
                : "The composite difference is withheld.");
        }

        dto.Effect = m7.CompositeDifference;
        dto.EffectLower = m7.CompositeConfidenceLower;
        dto.EffectUpper = m7.CompositeConfidenceUpper;
        dto.PValue = m7.RandomizationPValue;
        dto.Direction = DirectionOf(m7.CompositeDifference.Value);

        if (!m7.CompositeConfidenceLower.HasValue)
        {
            dto.Note = "The interval is withheld: a suite has fewer than three paired questions.";
        }

        return dto;
    }

    private static string RandomizationMethod(BenchmarkBatteryComparison m7) => m7.RandomizationMethod switch
    {
        BenchmarkBatteryRandomizationMethod.Exact =>
            "Stratified paired sign-flip test on the weighted composite difference, exact",
        BenchmarkBatteryRandomizationMethod.MonteCarlo =>
            "Stratified paired sign-flip test on the weighted composite difference, "
            + $"{(m7.MonteCarloResamples ?? 0).ToString("N0", CultureInfo.InvariantCulture)} Monte Carlo resamples, "
            + $"seed {(m7.Seed ?? 0).ToString(CultureInfo.InvariantCulture)}",
        _ => "Stratified paired sign-flip test on the weighted composite difference"
    };

    // --- Quality dimensions ------------------------------------------------------------------------------

    private static BenchmarkPairedTestDto DimensionTest(string dimension, BenchmarkPairedEntry baseline, BenchmarkPairedEntry treatment)
    {
        var dto = NewTest(baseline, treatment, EffectDifference);
        dto.Method = "Wilcoxon signed-rank on per-question mean " + dimension + " score";

        var baselineItems = DimensionItems(baseline, dimension);
        var treatmentItems = DimensionItems(treatment, dimension);

        foreach (var (entry, items) in new[] { (baseline, baselineItems), (treatment, treatmentItems) })
        {
            if (items.Count == 0)
            {
                return NotTested(dto, $"{entry.Label} has no scored {dimension} on its answers, so the dimension cannot be paired.");
            }
        }

        var pairing = PairUp(baselineItems, treatmentItems, matchRevision: true);
        dto.UnpairedItems = pairing.Unpaired;
        dto.RevisionMismatched = pairing.RevisionMismatched;

        return DifferenceTest(dto, pairing.Pairs.Select(p => p.Treatment - p.Baseline).ToList());
    }

    /// <summary>Per (suite, question): the cross-run mean of the dimension, with the item's order and revision.</summary>
    private static Dictionary<(int Suite, long Question), ItemValue> DimensionItems(BenchmarkPairedEntry entry, string dimension)
    {
        var items = new Dictionary<(int, long), ItemValue>();

        void Add(int suiteIndex, BenchmarkGroupStatisticsResult? statistics)
        {
            var means = statistics?.Dimensions.FirstOrDefault(d => d.Dimension == dimension)?.ItemMeans;
            if (statistics == null || means == null) return;

            foreach (var item in statistics.Items)
            {
                if (means.TryGetValue(item.QuestionId, out double mean))
                {
                    items[(suiteIndex, item.QuestionId)] = new ItemValue(item.OrderIndex, item.ItemRevision, mean);
                }
            }
        }

        if (entry.BatteryResult != null)
        {
            foreach (var suite in entry.BatteryResult.Suites)
            {
                Add(suite.SuiteIndex, suite.Statistics);
            }
        }
        else
        {
            Add(0, entry.Statistics);
        }

        return items;
    }

    // --- Speed and cost ----------------------------------------------------------------------------------

    private static BenchmarkPairedTestDto RatioTest(string measure, BenchmarkPairedEntry baseline, BenchmarkPairedEntry treatment)
    {
        bool speed = measure == SpeedMeasure;
        var dto = NewTest(baseline, treatment, EffectRatio);
        dto.Method = "Wilcoxon signed-rank on log(B / A) of per-question mean "
            + (speed ? "candidate model time" : "candidate spend");

        bool degraded = speed
            ? baseline.SpeedDegraded || treatment.SpeedDegraded
            : baseline.CostDegraded || treatment.CostDegraded;
        if (degraded)
        {
            var reasons = (speed
                    ? new[] { baseline.SpeedDegradedReason, treatment.SpeedDegradedReason }
                    : new[] { baseline.CostDegradedReason, treatment.CostDegradedReason })
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            return NotTested(dto, $"Not tested: the {(speed ? "speed" : "cost")} axis is degraded"
                + (reasons.Count > 0 ? " (" + string.Join("; ", reasons) + ")" : string.Empty)
                + ", so a paired difference would carry the differing key's effect, not the models'.");
        }

        var baselineItems = speed ? SpeedItems(baseline) : CostItems(baseline);
        var treatmentItems = speed ? SpeedItems(treatment) : CostItems(treatment);

        foreach (var side in new[] { baselineItems, treatmentItems })
        {
            if (side.Reason != null) return NotTested(dto, side.Reason);
        }

        var pairing = PairUp(baselineItems.Items, treatmentItems.Items, matchRevision: false);
        var positive = pairing.Pairs.Where(p => p.Baseline > 0.0 && p.Treatment > 0.0).ToList();
        int nonPositive = pairing.Pairs.Count - positive.Count;

        dto.UnpairedItems = pairing.Unpaired;

        var notes = new List<string>();
        if (nonPositive > 0)
        {
            notes.Add($"{nonPositive.ToString(CultureInfo.InvariantCulture)} paired question(s) with a zero "
                + (speed ? "model time" : "cost") + " on a side have no ratio and are left out.");
        }

        int missing = baselineItems.MissingAnswers + treatmentItems.MissingAnswers;
        if (missing > 0)
        {
            notes.Add($"{missing.ToString(CultureInfo.InvariantCulture)} answer(s) without per-answer token counts are left out.");
        }

        var logs = positive.Select(p => Math.Log(p.Treatment / p.Baseline)).ToList();
        dto.PairedItems = logs.Count;

        if (logs.Count < MinimumPairedItems)
        {
            TooFew(dto, logs.Count);
            if (notes.Count > 0) dto.Note = string.Join(" ", notes);
            return dto;
        }

        var wilcoxon = BenchmarkGroupStatistics.WilcoxonSignedRank(logs);
        double mean = logs.Average();
        double? half = HalfWidth(logs);

        dto.Effect = Math.Exp(mean);
        dto.EffectLower = half.HasValue ? Math.Exp(mean - half.Value) : null;
        dto.EffectUpper = half.HasValue ? Math.Exp(mean + half.Value) : null;
        dto.Dz = BenchmarkGroupStatistics.CohensDz(logs);
        dto.PValue = wilcoxon.PValue;
        dto.Direction = DirectionOf(mean);
        dto.Method += ", " + wilcoxon.Method;

        string? wilcoxonNote = WilcoxonNote(wilcoxon);
        if (wilcoxonNote != null) notes.Insert(0, wilcoxonNote);
        dto.Note = notes.Count > 0 ? string.Join(" ", notes) : null;

        return dto;
    }

    /// <summary>Per (suite, question): the mean candidate model time over the Ok answers to the scored items.</summary>
    private static ItemSet SpeedItems(BenchmarkPairedEntry entry)
    {
        var values = new Dictionary<(int, long), (List<double> Values, int Order)>();
        bool anyAnswers = false;

        foreach (var suite in entry.Suites)
        {
            var scored = ScoredItemIds(entry, suite.SuiteIndex);
            foreach (var run in suite.Runs)
            {
                foreach (var answer in run.Answers ?? new List<BenchmarkRunAnswer>())
                {
                    anyAnswers = true;
                    if (!Counts(answer, scored, out long question)) continue;
                    Collect(values, (suite.SuiteIndex, question), answer.OrderIndex, answer.ModelTimeMs);
                }
            }
        }

        if (!anyAnswers)
        {
            return new ItemSet(new Dictionary<(int, long), ItemValue>(),
                $"The answers of {entry.Label} were not loaded, so its per-question model time is unknown.", 0);
        }

        return new ItemSet(Means(values), null, 0);
    }

    /// <summary>
    /// Per (suite, question): the mean candidate cost of the Ok answers to the scored items, each answer
    /// costed from its own token counts on its run's card and served tier, as the run total is.
    /// </summary>
    private static ItemSet CostItems(BenchmarkPairedEntry entry)
    {
        var values = new Dictionary<(int, long), (List<double> Values, int Order)>();
        bool anyAnswers = false;
        int missing = 0;

        foreach (var suite in entry.Suites)
        {
            var scored = ScoredItemIds(entry, suite.SuiteIndex);
            foreach (var run in suite.Runs)
            {
                var answers = run.Answers ?? new List<BenchmarkRunAnswer>();
                if (answers.Count == 0) continue;
                anyAnswers = true;

                if (!entry.CandidatePricing.TryGetValue(run.Id, out var card) || card == null)
                {
                    return new ItemSet(new Dictionary<(int, long), ItemValue>(),
                        $"No candidate price card was resolved for run #{run.Id.ToString(CultureInfo.InvariantCulture)} "
                        + $"of {entry.Label} on this pricing basis, so its cost is unknown.", 0);
                }

                string? servedTier = BenchmarkRunFinalizer.ResolveServedServiceTier(answers);

                foreach (var answer in answers)
                {
                    if (!Counts(answer, scored, out long question)) continue;

                    if (!answer.InputTokens.HasValue || !answer.OutputTokens.HasValue)
                    {
                        missing++;
                        continue;
                    }

                    decimal cost = ModelPricingService.ComputeCostFromTotals(
                        card,
                        answer.InputTokens.Value, answer.OutputTokens.Value,
                        answer.CacheReadInputTokens ?? 0, answer.CacheCreationInputTokens ?? 0,
                        answer.LongContextInputTokens ?? 0, answer.LongContextOutputTokens ?? 0,
                        answer.LongContextCacheReadTokens ?? 0, answer.LongContextCacheCreationTokens ?? 0,
                        actualServiceTier: servedTier,
                        requestedServiceTier: run.TestedModelSnapshot?.ServiceTier);

                    Collect(values, (suite.SuiteIndex, question), answer.OrderIndex, (double)cost);
                }
            }
        }

        if (!anyAnswers)
        {
            return new ItemSet(new Dictionary<(int, long), ItemValue>(),
                $"The answers of {entry.Label} were not loaded, so its per-question cost is unknown.", 0);
        }

        if (values.Count == 0 && missing > 0)
        {
            return new ItemSet(new Dictionary<(int, long), ItemValue>(),
                $"The answers of {entry.Label} recorded no per-answer token counts, so a per-question cost cannot be formed.",
                missing);
        }

        return new ItemSet(Means(values), null, missing);
    }

    /// <summary>The scored items of one suite of the entry, as its statistics hold them; null reads every item.</summary>
    private static HashSet<long>? ScoredItemIds(BenchmarkPairedEntry entry, int suiteIndex)
    {
        var statistics = entry.BatteryResult != null
            ? entry.BatteryResult.Suites.FirstOrDefault(s => s.SuiteIndex == suiteIndex)?.Statistics
            : entry.Statistics;

        return statistics == null ? null : new HashSet<long>(statistics.Items.Select(i => i.QuestionId));
    }

    private static bool Counts(BenchmarkRunAnswer answer, HashSet<long>? scored, out long question)
    {
        question = 0;
        if (answer.Status != BenchmarkAnswerStatus.Ok) return false;
        if (BenchmarkItemAnalysis.QuestionKey(answer) is not long key) return false;
        if (scored != null && !scored.Contains(key)) return false;

        question = key;
        return true;
    }

    private static void Collect(
        Dictionary<(int, long), (List<double> Values, int Order)> values,
        (int, long) key,
        int order,
        double value)
    {
        if (!values.TryGetValue(key, out var slot))
        {
            slot = (new List<double>(), order);
            values[key] = slot;
        }

        slot.Values.Add(value);
        if (order < slot.Order) values[key] = (slot.Values, order);
    }

    private static Dictionary<(int Suite, long Question), ItemValue> Means(
        Dictionary<(int, long), (List<double> Values, int Order)> values)
        => values.ToDictionary(kv => kv.Key, kv => new ItemValue(kv.Value.Order, null, kv.Value.Values.Average()));

    // --- Shared test arithmetic --------------------------------------------------------------------------

    /// <summary>Wilcoxon, the mean difference with its t interval, and dz, as <see cref="BenchmarkGroupStatistics.Compare"/> reports them.</summary>
    private static BenchmarkPairedTestDto DifferenceTest(BenchmarkPairedTestDto dto, IReadOnlyList<double> differences)
    {
        dto.PairedItems = differences.Count;
        if (differences.Count < MinimumPairedItems)
        {
            return TooFew(dto, differences.Count);
        }

        var wilcoxon = BenchmarkGroupStatistics.WilcoxonSignedRank(differences);
        double mean = differences.Average();
        double? half = HalfWidth(differences);

        dto.Effect = mean;
        dto.EffectLower = half.HasValue ? mean - half.Value : null;
        dto.EffectUpper = half.HasValue ? mean + half.Value : null;
        dto.Dz = BenchmarkGroupStatistics.CohensDz(differences);
        dto.PValue = wilcoxon.PValue;
        dto.Direction = DirectionOf(mean);
        dto.Method += ", " + wilcoxon.Method;
        dto.Note = WilcoxonNote(wilcoxon);

        return dto;
    }

    /// <summary><c>t(n−1) · SD / √n</c>; null below two values or at an undefined SD.</summary>
    private static double? HalfWidth(IReadOnlyList<double> values)
    {
        double? sd = BenchmarkGroupStatistics.SampleStandardDeviation(values);
        if (!sd.HasValue || values.Count < 2) return null;
        return BenchmarkGroupStatistics.StudentTCritical95(values.Count - 1) * sd.Value / Math.Sqrt(values.Count);
    }

    private static string? WilcoxonNote(BenchmarkWilcoxonSignedRankResult wilcoxon)
    {
        if (!wilcoxon.PValue.HasValue) return AllZeroNote;
        if (wilcoxon.ZeroDifferenceCount == 0) return null;

        return $"{wilcoxon.ZeroDifferenceCount.ToString(CultureInfo.InvariantCulture)} paired question(s) with no "
            + "difference are left out of the ranking, as Wilcoxon's test discards them.";
    }

    private static string DirectionOf(double effect)
        => effect > 0.0 ? DirectionHigher : effect < 0.0 ? DirectionLower : DirectionNone;

    /// <summary>
    /// Items present on both sides, in (suite, baseline order, question) order. With
    /// <paramref name="matchRevision"/>, a shared item graded under two rubric revisions is counted and
    /// left out; timing and spend do not depend on the rubric, so speed and cost pair without it.
    /// </summary>
    private static Pairing PairUp(
        IReadOnlyDictionary<(int Suite, long Question), ItemValue> baseline,
        IReadOnlyDictionary<(int Suite, long Question), ItemValue> treatment,
        bool matchRevision)
    {
        var shared = baseline.Keys.Where(treatment.ContainsKey).ToList();
        int unpaired = baseline.Count + treatment.Count - 2 * shared.Count;

        var paired = shared
            .Where(k => !matchRevision || baseline[k].Revision == treatment[k].Revision)
            .OrderBy(k => k.Suite)
            .ThenBy(k => baseline[k].Order)
            .ThenBy(k => k.Question)
            .Select(k => (Baseline: baseline[k].Value, Treatment: treatment[k].Value))
            .ToList();

        return new Pairing(paired, unpaired, shared.Count - paired.Count);
    }

    private static BenchmarkPairedTestDto NewTest(BenchmarkPairedEntry baseline, BenchmarkPairedEntry treatment, string effectKind)
        => new()
        {
            BaselineKey = baseline.Key,
            TreatmentKey = treatment.Key,
            EffectKind = effectKind,
            Direction = DirectionNone,
            Verdict = NotTestedVerdict
        };

    private static BenchmarkPairedTestDto NotTested(BenchmarkPairedTestDto dto, string reason)
    {
        dto.NotTestedReason = reason;
        dto.Effect = null;
        dto.EffectLower = null;
        dto.EffectUpper = null;
        dto.Dz = null;
        dto.PValue = null;
        dto.Direction = DirectionNone;
        return dto;
    }

    private static BenchmarkPairedTestDto TooFew(BenchmarkPairedTestDto dto, int paired)
        => NotTested(dto, $"Only {paired.ToString(CultureInfo.InvariantCulture)} question(s) are paired; a paired test "
            + $"needs at least {MinimumPairedItems.ToString(CultureInfo.InvariantCulture)}.");

    private readonly record struct ItemValue(int Order, int? Revision, double Value);

    private sealed record Pairing(IReadOnlyList<(double Baseline, double Treatment)> Pairs, int Unpaired, int RevisionMismatched);

    private sealed record ItemSet(IReadOnlyDictionary<(int Suite, long Question), ItemValue> Items, string? Reason, int MissingAnswers);

    // --- Run-pair kinds ----------------------------------------------------------------------------------

    public static string KindName(BenchmarkRunPairKind kind) => kind.ToString();

    public static string KindLabel(BenchmarkRunPairKind kind) => kind switch
    {
        BenchmarkRunPairKind.ModelComparison => "Model comparison",
        BenchmarkRunPairKind.Verification => "Verification of a change",
        BenchmarkRunPairKind.Replicate => "Replicate",
        _ => "Not comparable"
    };

    /// <summary>
    /// The kind of comparison two runs on one suite make, from the 27 comparability keys as
    /// <see cref="BenchmarkBatteryComparability.CanCompare"/> reads them for batteries:
    /// <list type="bullet">
    /// <item><b>Model comparison</b>: every must-match key agrees and a model-axis key differs;</item>
    /// <item><b>Verification of a change</b>: every candidate key agrees and exactly one instrument key differs;</item>
    /// <item><b>Replicate</b>: no quality-relevant key differs;</item>
    /// <item><b>Not comparable</b>: a fundamental key other than the item revisions differs, or the
    /// differences are none of the above.</item>
    /// </list>
    /// Item revisions only reduce the paired set. The degrading keys degrade speed and cost and refuse
    /// nothing; under a repriced basis the pricing snapshot degrades nothing.
    /// </summary>
    public static BenchmarkRunPairClassification ClassifyRunPair(BenchmarkRun baseline, BenchmarkRun treatment, bool repriced)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(treatment);

        foreach (var run in new[] { baseline, treatment })
        {
            if (BenchmarkCrossModelComparability.HasAbsentFundamentalIdentity(run))
            {
                return new BenchmarkRunPairClassification
                {
                    Kind = BenchmarkRunPairKind.NotComparable,
                    Explanation = $"The exam of run #{run.Id.ToString(CultureInfo.InvariantCulture)} cannot be identified: "
                        + "a fundamental key has no value, so agreement on it would be absence, not a match."
                };
            }
        }

        var baselineKeys = BenchmarkCrossModelComparability.Keys(baseline);
        var treatmentKeys = BenchmarkCrossModelComparability.Keys(treatment)
            .ToDictionary(k => k.Name, StringComparer.Ordinal);

        var differences = new List<BenchmarkComparabilityKeyDifference>();
        var entries = new List<BenchmarkComparabilityKeyEntry>();
        foreach (var key in baselineKeys)
        {
            string treatmentValue = treatmentKeys.TryGetValue(key.Name, out var other) ? other.Value : BenchmarkComparabilityKey.NoValue;
            if (string.Equals(key.Value, treatmentValue, StringComparison.Ordinal)) continue;

            entries.Add(key);
            differences.Add(new BenchmarkComparabilityKeyDifference
            {
                Name = key.Name,
                Kind = key.Kind,
                Variants = new[]
                {
                    new BenchmarkComparabilityKeyVariant { Value = key.Value, RunIds = new[] { baseline.Id } },
                    new BenchmarkComparabilityKeyVariant { Value = treatmentValue, RunIds = new[] { treatment.Id } }
                }
            });
        }

        var fundamental = differences
            .Where(d => d.Kind == BenchmarkComparabilityKeyKind.Fundamental
                        && d.Name != BenchmarkComparabilityKey.ItemRevisionsKey)
            .ToList();
        if (fundamental.Count > 0)
        {
            return new BenchmarkRunPairClassification
            {
                Kind = BenchmarkRunPairKind.NotComparable,
                Differences = differences,
                Explanation = "The two runs sat different exams: " + string.Join("; ", fundamental.Select(d => d.Describe())) + "."
            };
        }

        var speedKeys = entries
            .Where(k => BenchmarkCrossModelComparability.IsDegradingKey(k.Name) && k.DegradesSpeed)
            .Select(k => k.Name)
            .ToList();
        var costKeys = entries
            .Where(k => BenchmarkCrossModelComparability.IsDegradingKey(k.Name) && k.DegradesCost
                        && !(repriced && k.Name == BenchmarkComparabilityKey.PricingSnapshotKey))
            .Select(k => k.Name)
            .ToList();

        var quality = differences
            .Where(d => !BenchmarkCrossModelComparability.IsDegradingKey(d.Name)
                        && d.Name != BenchmarkComparabilityKey.ItemRevisionsKey)
            .ToList();
        var mustMatch = quality.Where(d => BenchmarkCrossModelComparability.IsMustMatchKey(d.Name)).ToList();
        var modelAxis = quality.Where(d => BenchmarkCrossModelComparability.IsModelAxisKey(d.Name)).Select(d => d.Name).ToList();
        var candidate = quality.Where(d => d.Kind == BenchmarkComparabilityKeyKind.Candidate).ToList();
        var instrument = quality.Where(d => d.Kind == BenchmarkComparabilityKeyKind.Instrument).Select(d => d.Name).ToList();

        string tail = DegradedTail(speedKeys, costKeys)
            + (differences.Any(d => d.Name == BenchmarkComparabilityKey.ItemRevisionsKey)
                ? " Some questions were graded under different rubric revisions and are left out of the pairing."
                : string.Empty);

        BenchmarkRunPairKind kind;
        IReadOnlyList<string> changed;
        string explanation;

        if (mustMatch.Count == 0 && modelAxis.Count > 0)
        {
            kind = BenchmarkRunPairKind.ModelComparison;
            changed = modelAxis;
            explanation = "Model comparison: every must-match key agrees, and the model axis differs on "
                + string.Join(", ", modelAxis) + "." + tail;
        }
        else if (candidate.Count == 0 && instrument.Count == 1)
        {
            kind = BenchmarkRunPairKind.Verification;
            changed = instrument;
            explanation = $"Verification of a change: the candidate is identical, and one instrument key, {instrument[0]}, differs." + tail;
        }
        else if (quality.Count == 0)
        {
            kind = BenchmarkRunPairKind.Replicate;
            changed = Array.Empty<string>();
            explanation = "Replicate: no quality-relevant key differs, so the two runs repeat one condition." + tail;
        }
        else
        {
            kind = BenchmarkRunPairKind.NotComparable;
            changed = Array.Empty<string>();
            explanation = modelAxis.Count > 0
                ? "The model axis differs, but so do must-match keys: " + string.Join("; ", mustMatch.Select(d => d.Describe())) + "."
                : candidate.Count > 0
                    ? "A candidate key outside the model axis differs, which is neither a model comparison nor a single "
                      + "instrument change: " + string.Join("; ", candidate.Select(d => d.Describe())) + "."
                    : $"{instrument.Count.ToString(CultureInfo.InvariantCulture)} instrument keys differ "
                      + $"({string.Join(", ", instrument)}); a verification allows exactly one.";
        }

        return new BenchmarkRunPairClassification
        {
            Kind = kind,
            ChangedKeys = changed,
            Differences = differences,
            SpeedDegradingKeys = kind == BenchmarkRunPairKind.NotComparable ? Array.Empty<string>() : speedKeys,
            CostDegradingKeys = kind == BenchmarkRunPairKind.NotComparable ? Array.Empty<string>() : costKeys,
            Explanation = explanation
        };
    }

    private static string DegradedTail(IReadOnlyList<string> speedKeys, IReadOnlyList<string> costKeys)
    {
        var parts = new List<string>();
        if (speedKeys.Count > 0) parts.Add("speed (" + string.Join(", ", speedKeys) + ")");
        if (costKeys.Count > 0) parts.Add("cost (" + string.Join(", ", costKeys) + ")");

        return parts.Count == 0
            ? string.Empty
            : " Not tested on " + string.Join(" or ", parts) + ", which the differing keys degrade.";
    }
}

/// <summary>
/// The wizard's paired-test responses, kept in memory for ten minutes per (comparison key, pricing
/// basis, mode, reference): all pairs over twelve battery results is 66 M7 resamplings, seconds of
/// work that a redraw must not repeat. A singleton with its own bounded store.
/// </summary>
public sealed class BenchmarkPairedComparisonCache : IDisposable
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    /// <summary>Responses held at once; the oldest are evicted beyond it.</summary>
    public const int Capacity = 256;

    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = Capacity });

    public static string KeyOf(
        string comparisonKey,
        BenchmarkModelComparisonPricingBasis basis,
        BenchmarkPairedComparisonMode mode,
        string? referenceKey)
        => string.Join("|",
            comparisonKey,
            basis.ToString(),
            mode.ToString(),
            string.IsNullOrWhiteSpace(referenceKey) ? "*" : referenceKey.Trim());

    public bool TryGet(string key, out BenchmarkPairedComparisonDto? value)
        => _cache.TryGetValue(key, out value) && value != null;

    public void Set(string key, BenchmarkPairedComparisonDto value)
        => _cache.Set(key, value, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = Lifetime,
            Size = 1
        });

    public void Dispose() => _cache.Dispose();
}

/// <summary>
/// Loads what <see cref="BenchmarkPairedTests"/> needs and hands it over: the wizard's comparable
/// entries (recomputed through <see cref="BenchmarkModelComparisonService.CompareAsync"/>, so they are
/// exactly the wizard's), two runs on one suite, or two battery results of one definition. Answers are
/// loaded only for the entries that take part. Admin-initiated, and it makes no AI calls.
/// </summary>
public class BenchmarkPairedTestsService
{
    /// <summary>The most candidate baselines one kinds request may classify.</summary>
    public const int MaxKindCandidates = 1000;

    private readonly ApplicationDbContext _db;
    private readonly BenchmarkModelComparisonService _comparison;
    private readonly BenchmarkPairedComparisonCache? _cache;
    private readonly ModelPricingService? _pricingService;
    private readonly ILogger<BenchmarkPairedTestsService>? _logger;

    public BenchmarkPairedTestsService(
        ApplicationDbContext db,
        BenchmarkModelComparisonService comparison,
        BenchmarkPairedComparisonCache? cache = null,
        ModelPricingService? pricingService = null,
        ILogger<BenchmarkPairedTestsService>? logger = null)
    {
        _db = db;
        _comparison = comparison;
        _cache = cache;
        _pricingService = pricingService;
        _logger = logger;
    }

    // --- The wizard ------------------------------------------------------------------------------------

    /// <summary>The Paired tests view of the Model Comparison wizard. A null result carries the refusal.</summary>
    public async Task<(BenchmarkPairedComparisonDto? Result, string? Error)> CompareAsync(
        BenchmarkPairedComparisonRequest? request, CancellationToken ct = default)
    {
        var runIds = (request?.RunIds ?? new List<long>()).Distinct().ToList();
        var groupIds = (request?.GroupIds ?? new List<long>()).Distinct().ToList();
        var batteryRunIds = (request?.BatteryRunIds ?? new List<long>()).Distinct().ToList();

        if (batteryRunIds.Count > 0 && (runIds.Count > 0 || groupIds.Count > 0))
        {
            return (null, BenchmarkBatteryModelComparison.MixedSourcesError);
        }

        if (runIds.Count + groupIds.Count + batteryRunIds.Count < 2)
        {
            return (null, BenchmarkPairedTests.NeedsTwoEntriesError);
        }

        var basis = request?.PricingBasis ?? BenchmarkModelComparisonPricingBasis.Current;
        var mode = request?.Mode ?? BenchmarkPairedComparisonMode.Reference;
        string? requestedReference = string.IsNullOrWhiteSpace(request?.ReferenceKey) ? null : request!.ReferenceKey!.Trim();

        string cacheKey = BenchmarkPairedComparisonCache.KeyOf(
            BenchmarkReportComparisonKey.From(runIds, groupIds, batteryRunIds), basis, mode, requestedReference);

        if (request?.Recompute != true && _cache != null && _cache.TryGet(cacheKey, out var cached))
        {
            return (cached, null);
        }

        var (comparison, error) = await _comparison.CompareAsync(new BenchmarkModelComparisonRequest
        {
            RunIds = runIds,
            GroupIds = groupIds,
            BatteryRunIds = batteryRunIds,
            PricingBasis = basis
        }, ct);

        if (comparison == null) return (null, error ?? "The comparison could not be computed.");

        var comparable = comparison.Entries.Where(e => !e.Excluded).ToList();

        string? invalid = BenchmarkPairedTests.Validate(comparable.Count, mode);
        if (invalid != null) return (null, invalid);

        if (requestedReference != null && !comparable.Any(e => e.Key == requestedReference))
        {
            return (null, BenchmarkPairedTests.ReferenceNotComparableError);
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var (entries, loadError) = comparison.SubjectKind == BenchmarkModelComparisonSubjectKinds.Batteries
            ? await LoadBatteryEntriesAsync(comparable, basis, today, ct)
            : await LoadRunEntriesAsync(comparable, basis, today, ct);

        if (entries == null) return (null, loadError);

        string reference = requestedReference ?? BenchmarkPairedTests.DefaultReferenceKey(entries);
        var result = BenchmarkPairedTests.Build(entries, comparison.SubjectKind, basis, mode, reference, DateTime.UtcNow);

        _cache?.Set(cacheKey, result);

        _logger?.LogInformation(
            "Computed paired tests over {EntryCount} comparable entries in {Mode} mode on the {Basis} pricing basis.",
            entries.Count, mode, basis);

        return (result, null);
    }

    private async Task<(IReadOnlyList<BenchmarkPairedEntry>? Entries, string? Error)> LoadRunEntriesAsync(
        IReadOnlyList<BenchmarkModelComparisonEntryDto> comparable,
        BenchmarkModelComparisonPricingBasis basis,
        DateOnly today,
        CancellationToken ct)
    {
        var runIds = comparable.SelectMany(e => e.RunIds).Distinct().ToList();

        var runs = await _db.BenchmarkRuns
            .AsNoTracking()
            .Include(r => r.Answers)
            .Where(r => runIds.Contains(r.Id))
            .OrderBy(r => r.Id)
            .ToListAsync(ct);

        var byId = runs.ToDictionary(r => r.Id);
        var pricing = await ResolveCandidatePricingAsync(runs, basis, today);

        var entries = new List<BenchmarkPairedEntry>(comparable.Count);
        foreach (var entry in comparable)
        {
            var members = entry.RunIds.Where(byId.ContainsKey).Select(id => byId[id]).ToList();
            if (members.Count == 0)
            {
                return (null, $"The runs of {entry.Label} could not be loaded.");
            }

            entries.Add(WithDegradation(BenchmarkPairedTests.FromRuns(entry.Key, entry.Label, members, pricing), entry));
        }

        return (entries, null);
    }

    private async Task<(IReadOnlyList<BenchmarkPairedEntry>? Entries, string? Error)> LoadBatteryEntriesAsync(
        IReadOnlyList<BenchmarkModelComparisonEntryDto> comparable,
        BenchmarkModelComparisonPricingBasis basis,
        DateOnly today,
        CancellationToken ct)
    {
        var ids = comparable.Where(e => e.BatteryRunId.HasValue).Select(e => e.BatteryRunId!.Value).ToList();

        var (sources, error) = await BenchmarkBatteryModelComparison.LoadAsync(_db, ids, ct);
        if (sources == null) return (null, error);

        var withAnswers = await LoadRunsWithAnswersAsync(sources.SelectMany(s => s.Runs.Select(r => r.Id)), ct);
        var pricing = await ResolveCandidatePricingAsync(withAnswers.Values.ToList(), basis, today);

        var entries = new List<BenchmarkPairedEntry>(comparable.Count);
        foreach (var entry in comparable)
        {
            var source = sources.FirstOrDefault(s => s.Key == entry.Key);
            if (source?.Result == null)
            {
                return (null, $"The battery analysis of {entry.Label} could not be read.");
            }

            entries.Add(WithDegradation(
                BenchmarkPairedTests.FromBattery(entry.Key, entry.Label, source.Result, SuitesOf(source, withAnswers), pricing),
                entry));
        }

        return (entries, null);
    }

    /// <summary>The comparison's degrade flags, with the entry's own within-set reason where no key names one.</summary>
    private static BenchmarkPairedEntry WithDegradation(BenchmarkPairedEntry paired, BenchmarkModelComparisonEntryDto entry)
    {
        bool speed = entry.SpeedDegraded || (entry.Speed?.Degraded ?? false);
        bool cost = entry.CostDegraded || (entry.Cost?.Degraded ?? false);

        return paired with
        {
            SpeedDegraded = speed,
            SpeedDegradedReason = speed
                ? entry.SpeedDegradingKeys.Count > 0 ? string.Join(", ", entry.SpeedDegradingKeys) : entry.Speed?.DegradedReason
                : null,
            CostDegraded = cost,
            CostDegradedReason = cost
                ? entry.CostDegradingKeys.Count > 0 ? string.Join(", ", entry.CostDegradingKeys) : entry.Cost?.DegradedReason
                : null
        };
    }

    // --- Two runs (the run report's Paired Test tab) --------------------------------------------------------

    /// <summary>
    /// Run <paramref name="runId"/> (the treatment) against the request's baseline run, on one suite. A null
    /// result carries the refusal; <c>NotFound</c> is set when a run does not exist.
    /// </summary>
    public async Task<(BenchmarkPairComparisonDto? Result, string? Error, bool NotFound)> CompareRunsAsync(
        long runId, BenchmarkRunPairedComparisonRequest? request, CancellationToken ct = default)
    {
        long baselineId = request?.BaselineRunId ?? 0;
        if (baselineId <= 0) return (null, "Choose a baseline run to compare with.", false);
        if (baselineId == runId)
        {
            return (null, "Choose another run as the baseline: a run compared with itself has nothing to test.", false);
        }

        var runs = await _db.BenchmarkRuns
            .AsNoTracking()
            .Include(r => r.Answers)
            .Where(r => r.Id == runId || r.Id == baselineId)
            .ToListAsync(ct);

        var treatment = runs.FirstOrDefault(r => r.Id == runId);
        var baseline = runs.FirstOrDefault(r => r.Id == baselineId);
        if (treatment == null) return (null, $"Run {runId.ToString(CultureInfo.InvariantCulture)} not found.", true);
        if (baseline == null) return (null, $"Run {baselineId.ToString(CultureInfo.InvariantCulture)} not found.", true);

        string? refusal = RunPairRefusal(baseline, treatment);
        if (refusal != null) return (null, refusal, false);

        var basis = request?.PricingBasis ?? BenchmarkModelComparisonPricingBasis.Current;
        var classification = BenchmarkPairedTests.ClassifyRunPair(
            baseline, treatment, repriced: basis == BenchmarkModelComparisonPricingBasis.Current);

        if (classification.Kind == BenchmarkRunPairKind.NotComparable)
        {
            return (null, "Not comparable: " + classification.Explanation, false);
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var pricing = await ResolveCandidatePricingAsync(runs, basis, today);

        BenchmarkPairedEntry RunEntry(BenchmarkRun run) => BenchmarkPairedTests.FromRuns(RunKey(run.Id), RunLabel(run), new[] { run }, pricing) with
        {
            SpeedDegraded = classification.SpeedDegradingKeys.Count > 0,
            SpeedDegradedReason = classification.SpeedDegradingKeys.Count > 0 ? string.Join(", ", classification.SpeedDegradingKeys) : null,
            CostDegraded = classification.CostDegradingKeys.Count > 0,
            CostDegradedReason = classification.CostDegradingKeys.Count > 0 ? string.Join(", ", classification.CostDegradingKeys) : null
        };

        var baselineEntry = RunEntry(baseline);
        var treatmentEntry = RunEntry(treatment);

        return (new BenchmarkPairComparisonDto
        {
            ComputedAtUtc = DateTime.UtcNow,
            SubjectKind = "Run",
            TreatmentId = treatment.Id,
            BaselineId = baseline.Id,
            TreatmentKey = treatmentEntry.Key,
            BaselineKey = baselineEntry.Key,
            TreatmentLabel = treatmentEntry.Label,
            BaselineLabel = baselineEntry.Label,
            Kind = BenchmarkPairedTests.KindName(classification.Kind),
            KindLabel = BenchmarkPairedTests.KindLabel(classification.Kind),
            Explanation = classification.Explanation,
            ChangedKeys = classification.ChangedKeys.ToList(),
            Differences = classification.Differences.Select(d => ToDto(d)).ToList(),
            SpeedDegraded = classification.SpeedDegradingKeys.Count > 0,
            SpeedDegradingKeys = classification.SpeedDegradingKeys.ToList(),
            CostDegraded = classification.CostDegradingKeys.Count > 0,
            CostDegradingKeys = classification.CostDegradingKeys.ToList(),
            SingleRunCaveat = BenchmarkPairedTests.SingleRunCaveat,
            PricingBasis = basis.ToString(),
            Measures = BenchmarkPairedTests.ComparePair(baselineEntry, treatmentEntry)
        }, null, false);
    }

    /// <summary>
    /// The kind each candidate baseline would make with run <paramref name="runId"/>, in request order, for
    /// the run report's baseline select. Reads the runs and their item-revision stubs only.
    /// </summary>
    public async Task<(IReadOnlyList<BenchmarkRunPairKindDto>? Result, string? Error, bool NotFound)> ClassifyRunsAsync(
        long runId, BenchmarkRunPairKindsRequest? request, CancellationToken ct = default)
    {
        var candidateIds = (request?.RunIds ?? new List<long>()).Distinct().Where(id => id != runId).ToList();
        if (candidateIds.Count > MaxKindCandidates)
        {
            return (null, $"At most {MaxKindCandidates.ToString(CultureInfo.InvariantCulture)} runs can be classified at once.", false);
        }

        var wanted = candidateIds.Append(runId).ToList();
        var runs = await _db.BenchmarkRuns
            .AsNoTracking()
            .Where(r => wanted.Contains(r.Id))
            .ToListAsync(ct);

        var treatment = runs.FirstOrDefault(r => r.Id == runId);
        if (treatment == null) return (null, $"Run {runId.ToString(CultureInfo.InvariantCulture)} not found.", true);

        await BenchmarkSeriesOrchestrator.HydrateItemRevisionsAsync(_db, runs, ct);

        var byId = runs.ToDictionary(r => r.Id);
        var result = new List<BenchmarkRunPairKindDto>(candidateIds.Count);

        foreach (long id in candidateIds)
        {
            if (!byId.TryGetValue(id, out var candidate))
            {
                result.Add(NotComparableKind(id, $"Run {id.ToString(CultureInfo.InvariantCulture)} not found."));
                continue;
            }

            string? refusal = RunPairRefusal(candidate, treatment);
            if (refusal != null)
            {
                result.Add(NotComparableKind(id, refusal));
                continue;
            }

            var classification = BenchmarkPairedTests.ClassifyRunPair(candidate, treatment, repriced: true);
            result.Add(new BenchmarkRunPairKindDto
            {
                RunId = id,
                Kind = BenchmarkPairedTests.KindName(classification.Kind),
                KindLabel = BenchmarkPairedTests.KindLabel(classification.Kind),
                ChangedKeys = classification.ChangedKeys.ToList(),
                Explanation = classification.Explanation
            });
        }

        return (result, null, false);
    }

    private static BenchmarkRunPairKindDto NotComparableKind(long runId, string reason) => new()
    {
        RunId = runId,
        Kind = BenchmarkPairedTests.KindName(BenchmarkRunPairKind.NotComparable),
        KindLabel = BenchmarkPairedTests.KindLabel(BenchmarkRunPairKind.NotComparable),
        Explanation = reason
    };

    /// <summary>Null when both runs are finished with a measurement and sat the same suite.</summary>
    private static string? RunPairRefusal(BenchmarkRun baseline, BenchmarkRun treatment)
    {
        foreach (var run in new[] { baseline, treatment })
        {
            string id = run.Id.ToString(CultureInfo.InvariantCulture);
            if (run.Status == BenchmarkRunStatus.Running)
            {
                return $"Not comparable: run #{id} is still in progress, so its figures are not settled.";
            }

            if (run.Status == BenchmarkRunStatus.Failed || run.Status == BenchmarkRunStatus.Canceled)
            {
                return $"Not comparable: run #{id} ended {run.Status}, so it produced no measurement to compare.";
            }
        }

        long? baselineSuite = baseline.BenchmarkSuiteIdUsed ?? baseline.BenchmarkSuiteId;
        long? treatmentSuite = treatment.BenchmarkSuiteIdUsed ?? treatment.BenchmarkSuiteId;
        if (baselineSuite != treatmentSuite)
        {
            return $"Not comparable: run #{baseline.Id.ToString(CultureInfo.InvariantCulture)} answered "
                + $"\"{baseline.SuiteName}\" and run #{treatment.Id.ToString(CultureInfo.InvariantCulture)} "
                + $"\"{treatment.SuiteName}\"; a paired test needs the same suite.";
        }

        return null;
    }

    private static string RunKey(long runId) => "run:" + runId.ToString(CultureInfo.InvariantCulture);

    private static string RunLabel(BenchmarkRun run)
        => $"{run.TestedModelSnapshot.Label() ?? "Run"} (run #{run.Id.ToString(CultureInfo.InvariantCulture)})";

    private static BenchmarkComparabilityDifferenceDto ToDto(BenchmarkComparabilityKeyDifference difference) => new()
    {
        Name = difference.Name,
        Kind = difference.Kind.ToString(),
        Description = difference.Describe(),
        Variants = difference.Variants.Select(v => new BenchmarkComparabilityVariantDto
        {
            Value = v.Value,
            RunIds = v.RunIds.ToList()
        }).ToList()
    };

    // --- Two battery results (the battery report's Paired Test tab) -----------------------------------------

    /// <summary>
    /// The treatment battery run against the baseline, both read from their latest analyses and judged by
    /// <see cref="BenchmarkBatteryComparability.CanCompare"/> (M7). The Intelligence row is M7 itself; the
    /// dimension rows pool (suite, question) pairs; speed and cost are the log-ratio tests.
    /// </summary>
    public async Task<(BenchmarkPairComparisonDto? Result, string? Error, bool NotFound)> CompareBatteriesAsync(
        BenchmarkBatteryPairedComparisonRequest? request, CancellationToken ct = default)
    {
        long treatmentId = request?.BatteryRunId ?? 0;
        long baselineId = request?.BaselineBatteryRunId ?? 0;
        if (treatmentId <= 0 || baselineId <= 0) return (null, "Name the battery run and the baseline battery run to compare it with.", false);
        if (treatmentId == baselineId)
        {
            return (null, "Choose another battery run as the baseline: a result compared with itself has nothing to test.", false);
        }

        var (sources, error) = await BenchmarkBatteryModelComparison.LoadAsync(_db, new[] { baselineId, treatmentId }, ct);
        if (sources == null) return (null, error, true);

        var baseline = sources.Single(s => s.BatteryRunId == baselineId);
        var treatment = sources.Single(s => s.BatteryRunId == treatmentId);

        foreach (var side in new[] { baseline, treatment })
        {
            if (side.Refusal != null || side.Result == null)
            {
                return (null, $"Battery run #{side.BatteryRunId.ToString(CultureInfo.InvariantCulture)}: "
                    + (side.Refusal ?? "its battery analysis cannot be read."), false);
            }
        }

        var withAnswers = await LoadRunsWithAnswersAsync(sources.SelectMany(s => s.Runs.Select(r => r.Id)), ct);

        var eligibility = BenchmarkBatteryComparability.CanCompare(Side(baseline, withAnswers), Side(treatment, withAnswers));
        if (!eligibility.Allowed || !eligibility.Kind.HasValue)
        {
            return (null, "Not comparable: " + eligibility.Explanation, false);
        }

        var basis = request?.PricingBasis ?? BenchmarkModelComparisonPricingBasis.Current;
        bool repriced = basis == BenchmarkModelComparisonPricingBasis.Current;

        var sample = withAnswers.Values.FirstOrDefault();
        var taxonomy = sample == null
            ? new Dictionary<string, BenchmarkComparabilityKeyEntry>(StringComparer.Ordinal)
            : BenchmarkComparabilityKey.Extract(sample).ToDictionary(k => k.Name, StringComparer.Ordinal);

        var degrading = eligibility.DifferingKeys
            .Where(BenchmarkCrossModelComparability.IsDegradingKey)
            .Where(name => taxonomy.ContainsKey(name))
            .ToList();
        var speedKeys = degrading.Where(name => taxonomy[name].DegradesSpeed).ToList();
        var costKeys = degrading
            .Where(name => taxonomy[name].DegradesCost && !(repriced && name == BenchmarkComparabilityKey.PricingSnapshotKey))
            .ToList();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var pricing = await ResolveCandidatePricingAsync(withAnswers.Values.ToList(), basis, today);

        BenchmarkPairedEntry Entry(BenchmarkBatteryComparisonSource source) => BenchmarkPairedTests.FromBattery(
            source.Key, BatteryLabel(source), source.Result!, SuitesOf(source, withAnswers), pricing) with
        {
            SpeedDegraded = speedKeys.Count > 0,
            SpeedDegradedReason = speedKeys.Count > 0 ? string.Join(", ", speedKeys) : null,
            CostDegraded = costKeys.Count > 0,
            CostDegradedReason = costKeys.Count > 0 ? string.Join(", ", costKeys) : null
        };

        var baselineEntry = Entry(baseline);
        var treatmentEntry = Entry(treatment);

        var kind = eligibility.Kind.Value == BenchmarkBatteryComparisonKind.ModelComparison
            ? BenchmarkRunPairKind.ModelComparison
            : BenchmarkRunPairKind.Verification;

        var quality = eligibility.Differences.Where(d => !BenchmarkCrossModelComparability.IsDegradingKey(d.Name)).ToList();
        var changed = (kind == BenchmarkRunPairKind.ModelComparison
                ? quality.Where(d => BenchmarkCrossModelComparability.IsModelAxisKey(d.Name))
                : quality.Where(d => d.Kind == BenchmarkComparabilityKeyKind.Instrument))
            .Select(d => d.Name)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return (new BenchmarkPairComparisonDto
        {
            ComputedAtUtc = DateTime.UtcNow,
            SubjectKind = BenchmarkBatteryModelComparison.SourceKind,
            TreatmentId = treatment.BatteryRunId,
            BaselineId = baseline.BatteryRunId,
            TreatmentKey = treatment.Key,
            BaselineKey = baseline.Key,
            TreatmentLabel = treatmentEntry.Label,
            BaselineLabel = baselineEntry.Label,
            Kind = BenchmarkPairedTests.KindName(kind),
            KindLabel = BenchmarkPairedTests.KindLabel(kind),
            Explanation = eligibility.Explanation,
            ChangedKeys = changed,
            Differences = eligibility.Differences.Select(d => ToDto(d, baseline, treatment)).ToList(),
            SpeedDegraded = speedKeys.Count > 0,
            SpeedDegradingKeys = speedKeys,
            CostDegraded = costKeys.Count > 0,
            CostDegradingKeys = costKeys,
            SingleRunCaveat = BenchmarkPairedTests.SingleRunCaveatOf(new[] { baselineEntry, treatmentEntry }),
            PricingBasis = basis.ToString(),
            Measures = BenchmarkPairedTests.ComparePair(baselineEntry, treatmentEntry)
        }, null, false);
    }

    private static BenchmarkBatteryComparisonSide Side(
        BenchmarkBatteryComparisonSource source, IReadOnlyDictionary<long, BenchmarkRun> withAnswers)
        => new(
            source.DefinitionSha256,
            source.Suites
                .Select(s => new BenchmarkBatteryComparisonSuite(
                    s.SuiteIndex,
                    s.SuiteId ?? 0,
                    s.Runs.Select(r => withAnswers.GetValueOrDefault(r.Id) ?? r).ToList()))
                .ToList());

    private static string BatteryLabel(BenchmarkBatteryComparisonSource source)
    {
        string? model = source.IdentityRuns.OrderBy(r => r.StartedAtUtc).LastOrDefault()?.TestedModelSnapshot.Label();
        string id = source.BatteryRunId.ToString(CultureInfo.InvariantCulture);
        return model == null ? $"Battery run #{id}" : $"{model} (battery run #{id})";
    }

    private static BenchmarkComparabilityDifferenceDto ToDto(
        BenchmarkBatteryComparisonDifference difference,
        BenchmarkBatteryComparisonSource baseline,
        BenchmarkBatteryComparisonSource treatment)
    {
        List<long> RunsOf(BenchmarkBatteryComparisonSource source)
            => source.Suites
                .Where(s => (s.SuiteId ?? 0) == difference.SuiteId)
                .SelectMany(s => s.Runs.Select(r => r.Id))
                .OrderBy(id => id)
                .ToList();

        return new BenchmarkComparabilityDifferenceDto
        {
            Name = difference.Name,
            Kind = difference.Kind.ToString(),
            Description = difference.Describe(),
            Variants = new List<BenchmarkComparabilityVariantDto>
            {
                new() { Value = difference.BaselineValue, RunIds = RunsOf(baseline) },
                new() { Value = difference.TreatmentValue, RunIds = RunsOf(treatment) }
            }
        };
    }

    private static IReadOnlyList<BenchmarkPairedSuiteRuns> SuitesOf(
        BenchmarkBatteryComparisonSource source, IReadOnlyDictionary<long, BenchmarkRun> withAnswers)
        => source.Suites
            .Select(s => new BenchmarkPairedSuiteRuns(
                s.SuiteIndex,
                s.SuiteName ?? $"Suite {(s.SuiteIndex + 1).ToString(CultureInfo.InvariantCulture)}",
                s.Runs.Select(r => withAnswers.GetValueOrDefault(r.Id) ?? r).ToList()))
            .ToList();

    // --- Loading -----------------------------------------------------------------------------------------

    private async Task<Dictionary<long, BenchmarkRun>> LoadRunsWithAnswersAsync(IEnumerable<long> runIds, CancellationToken ct)
    {
        var ids = runIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<long, BenchmarkRun>();

        return await _db.BenchmarkRuns
            .AsNoTracking()
            .Include(r => r.Answers)
            .Where(r => ids.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, ct);
    }

    /// <summary>
    /// The candidate price card of every run on the requested basis, as the comparison resolves it: the
    /// run's own snapshot under <c>AsRun</c>, today's catalog under <c>Current</c>. Unresolvable is null.
    /// </summary>
    private async Task<Dictionary<long, ModelPricing?>> ResolveCandidatePricingAsync(
        IReadOnlyCollection<BenchmarkRun> runs,
        BenchmarkModelComparisonPricingBasis basis,
        DateOnly today)
    {
        var cards = new Dictionary<long, ModelPricing?>();
        foreach (var run in runs)
        {
            if (_pricingService == null)
            {
                cards[run.Id] = null;
                continue;
            }

            try
            {
                cards[run.Id] = basis == BenchmarkModelComparisonPricingBasis.AsRun
                    ? (await _pricingService.ResolveForRunAsync(run)).Candidate
                    : _pricingService.ResolveDefault(run.TestedModelSnapshot.Provider, run.TestedModelSnapshot.ModelId, today);
            }
            catch (Exception ex)
            {
                // An unresolvable card leaves one entry's cost untested, never the comparison.
                _logger?.LogWarning(ex,
                    "Could not resolve {Basis} pricing for benchmark run {RunId}; its paired cost is not tested.",
                    basis, run.Id);
                cards[run.Id] = null;
            }
        }

        return cards;
    }
}
