namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using MobileGnollHackLogger.Data;

/// <summary>How a comparability key is treated across the suites of one battery run (M8).</summary>
public enum BenchmarkBatteryKeyClass
{
    /// <summary>
    /// Differs between suites by design. Must match within a suite, and suite for suite between two
    /// battery runs being compared.
    /// </summary>
    SuiteIntrinsic = 0,

    /// <summary>Must be identical across every member of the battery run; a difference refuses the composite.</summary>
    BatteryWide = 1,

    /// <summary>A difference degrades the speed and/or cost composite, as in Tier B.</summary>
    SpeedAndCost = 2
}

/// <summary>One suite's runs, judged by the existing single-suite resolution.</summary>
public sealed record BenchmarkBatterySuiteVerdict(int SuiteIndex, string SuiteName, BenchmarkComparabilityResult Comparability);

/// <summary>Whether the members of one battery run may be combined into a composite, and why.</summary>
public sealed record BenchmarkBatteryComparabilityResult(
    bool CompositePermitted,
    bool SpeedDegraded,
    bool CostDegraded,
    IReadOnlyList<BenchmarkComparabilityKeyDifference> BatteryWideDifferences,
    IReadOnlyList<BenchmarkBatterySuiteVerdict> Suites,
    string Explanation)
{
    /// <summary>
    /// Suites of one board class (every suite with a game board, or every suite without one) whose
    /// runs carry different candidate system prompts. Any entry refuses the composite.
    /// </summary>
    public IReadOnlyList<BenchmarkComparabilityKeyDifference> PromptDifferences { get; init; }
        = Array.Empty<BenchmarkComparabilityKeyDifference>();

    /// <summary>Speed-and-cost keys that differ across the battery run's members.</summary>
    public IReadOnlyList<BenchmarkComparabilityKeyDifference> SpeedAndCostDifferences { get; init; }
        = Array.Empty<BenchmarkComparabilityKeyDifference>();

    /// <summary>Provenance notes that refuse nothing: a differing wiki or source-code HEAD.</summary>
    public IReadOnlyList<string> Caveats { get; init; } = Array.Empty<string>();
}

/// <summary>Which of the two permitted comparisons of M7 a pair of battery runs is.</summary>
public enum BenchmarkBatteryComparisonKind
{
    /// <summary>(a) The same instrument; the model axis differs.</summary>
    ModelComparison = 1,

    /// <summary>(b) The same candidate; exactly one instrument key name differs across the battery.</summary>
    Verification = 2
}

/// <summary>One suite of one side of a battery comparison: its usable member runs.</summary>
public sealed record BenchmarkBatteryComparisonSuite(int SuiteIndex, long SuiteId, IReadOnlyList<BenchmarkRun> Runs);

/// <summary>One side of a battery comparison: the definition hash and the usable runs per suite.</summary>
public sealed record BenchmarkBatteryComparisonSide(string DefinitionSha256, IReadOnlyList<BenchmarkBatteryComparisonSuite> Suites);

/// <summary>A key on which the two sides of a battery comparison differ in one suite.</summary>
public sealed record BenchmarkBatteryComparisonDifference
{
    public string Name { get; init; } = string.Empty;

    public BenchmarkComparabilityKeyKind Kind { get; init; }

    public long SuiteId { get; init; }

    public string SuiteName { get; init; } = string.Empty;

    /// <summary>The baseline's value; several values joined by <c> / </c> on a degrading key.</summary>
    public string BaselineValue { get; init; } = BenchmarkComparabilityKey.NoValue;

    /// <summary>The treatment's value; several values joined by <c> / </c> on a degrading key.</summary>
    public string TreatmentValue { get; init; } = BenchmarkComparabilityKey.NoValue;

    public string Describe() => $"{Name} in suite '{SuiteName}' (#{SuiteId}): {BaselineValue} vs {TreatmentValue}";
}

/// <summary>Whether two battery runs may be compared (M7), as which kind, and why.</summary>
public sealed record BenchmarkBatteryComparisonEligibility
{
    public bool Allowed { get; init; }

    /// <summary>Null when the comparison is refused.</summary>
    public BenchmarkBatteryComparisonKind? Kind { get; init; }

    /// <summary>Every differing key, per suite, speed-and-cost keys included.</summary>
    public IReadOnlyList<BenchmarkBatteryComparisonDifference> Differences { get; init; }
        = Array.Empty<BenchmarkBatteryComparisonDifference>();

    /// <summary>The distinct names of <see cref="Differences"/>, in key order.</summary>
    public IReadOnlyList<string> DifferingKeys { get; init; } = Array.Empty<string>();

    public bool SpeedDegraded { get; init; }

    public bool CostDegraded { get; init; }

    public string Explanation { get; init; } = string.Empty;
}

/// <summary>
/// What one existing run is judged against when it is offered for one (suite, round) slot of a
/// battery run: the slot's suite, the battery run's tested configuration and the fingerprint
/// recorded (or, before a start, computed now) for that suite, and the battery run's other members.
/// </summary>
public sealed record BenchmarkBatteryAttachTarget
{
    /// <summary>0-based position of the suite in the definition.</summary>
    public int SuiteIndex { get; init; }

    /// <summary>The definition's suite at <see cref="SuiteIndex"/>.</summary>
    public long SuiteId { get; init; }

    public string SuiteName { get; init; } = string.Empty;

    /// <summary>The tested configuration of the battery run's stored start request.</summary>
    public long? TestedModelConfigurationId { get; init; }

    /// <summary>The five hashes recorded for the suite; null when none were recorded.</summary>
    public BenchmarkInstrumentFingerprint? Fingerprint { get; init; }

    /// <summary>The scoring method this build grades under.</summary>
    public int ScoringMethodVersion { get; init; }

    /// <summary>
    /// The harness a run must carry while the battery run holds no usable member to agree with;
    /// null skips the check.
    /// </summary>
    public string? HarnessVersion { get; init; }

    /// <summary>
    /// The usable members the run would join, by suite index, the slot's own occupant excluded. The
    /// runs carry their answers or answer stubs.
    /// </summary>
    public IReadOnlyList<(int SuiteIndex, IReadOnlyList<BenchmarkRun> Runs)> UsableMembers { get; init; }
        = Array.Empty<(int, IReadOnlyList<BenchmarkRun>)>();

    /// <summary>The runs already filling another slot of the battery run.</summary>
    public IReadOnlyCollection<long> OccupiedRunIds { get; init; } = Array.Empty<long>();
}

/// <summary>Whether a run may fill a slot of a battery run, and the reason when it may not.</summary>
public sealed record BenchmarkBatteryAttachCheck(bool Eligible, string? Reason)
{
    public static BenchmarkBatteryAttachCheck Accepted { get; } = new(true, null);

    public static BenchmarkBatteryAttachCheck Refused(string reason) => new(false, reason);
}

/// <summary>
/// Comparability across the suites of a battery run (M8), the comparability class of a result
/// (M9), the eligibility of two battery runs for comparison (M7), and the eligibility of an existing
/// run for a slot of a battery run. Pure computation: no I/O, no AI calls, no writes.
///
/// <para>The 27 keys of <see cref="BenchmarkComparabilityKey.Extract"/> are reused unchanged and
/// split into three classes. Suite-intrinsic keys differ between suites by design and are judged
/// within each suite by <see cref="BenchmarkComparabilityKey.Resolve"/>; battery-wide keys must agree
/// across every member; speed-and-cost keys degrade the speed and cost composites.</para>
///
/// <para>Runs must carry their answers, or the answer stubs
/// <c>BenchmarkSeriesOrchestrator.HydrateItemRevisionsAsync</c> builds: the item-revision and
/// assessed-difficulty keys are read from them.</para>
/// </summary>
public static class BenchmarkBatteryComparability
{
    private const string WikiHeadName = "WikiHeadSha";
    private const string SourceCodeHeadName = "SourceCodeHeadSha";

    /// <summary>
    /// The class of one key of <see cref="BenchmarkComparabilityKey.Extract"/>.
    ///
    /// <para><see cref="BenchmarkComparabilityKey.CandidatePromptOptionsKey"/> is battery-wide: it is
    /// compared re-rendered with the board flag cleared
    /// (<see cref="BenchmarkCandidatePromptOptions.BatteryWideSignature"/>), and the board flag
    /// itself is suite-intrinsic through <see cref="BenchmarkComparabilityKey.GameSnapshotKey"/> and
    /// <see cref="BenchmarkComparabilityKey.CandidateSystemPromptKey"/>, whose text depends on it.</para>
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The key is not one this class knows.</exception>
    public static BenchmarkBatteryKeyClass Classify(string keyName)
    {
        return keyName switch
        {
            BenchmarkComparabilityKey.SuiteKey
                or BenchmarkComparabilityKey.ItemRevisionsKey
                or BenchmarkComparabilityKey.AssessedDifficultiesKey
                or BenchmarkComparabilityKey.GameSnapshotKey
                or BenchmarkComparabilityKey.CandidateSystemPromptKey => BenchmarkBatteryKeyClass.SuiteIntrinsic,

            BenchmarkComparabilityKey.CandidateProviderKey
                or BenchmarkComparabilityKey.CandidateModelKey
                or BenchmarkComparabilityKey.CandidateThinkingLevelKey
                or BenchmarkComparabilityKey.CandidateReasoningModeKey
                or BenchmarkComparabilityKey.CandidateReasoningSummaryKey
                or BenchmarkComparabilityKey.CandidateServiceTierKey
                or BenchmarkComparabilityKey.CandidateMaxOutputTokensKey
                or BenchmarkComparabilityKey.CandidateParallelExecutionModeKey
                or BenchmarkComparabilityKey.CandidateEndpointKey
                or BenchmarkComparabilityKey.CandidatePromptOptionsKey
                or BenchmarkComparabilityKey.ToolGuidesKey
                or BenchmarkComparabilityKey.KnowledgeBaseKey
                or BenchmarkComparabilityKey.HarnessVersionKey
                or BenchmarkComparabilityKey.ScoringMethodVersionKey
                or BenchmarkComparabilityKey.ScoringProfileKey
                or BenchmarkComparabilityKey.AssessorConfigurationKey
                or BenchmarkComparabilityKey.SecondOpinionConfigurationKey
                or BenchmarkComparabilityKey.ClaimVerifierConfigurationKey
                or BenchmarkComparabilityKey.PerQuestionBudgetsKey => BenchmarkBatteryKeyClass.BatteryWide,

            BenchmarkComparabilityKey.QuestionParallelismKey
                or BenchmarkComparabilityKey.SpeedCalibrationKey
                or BenchmarkComparabilityKey.PricingSnapshotKey => BenchmarkBatteryKeyClass.SpeedAndCost,

            _ => throw new ArgumentOutOfRangeException(nameof(keyName), keyName,
                "Unclassified comparability key: every key Extract emits must be classified for batteries.")
        };
    }

    /// <summary>
    /// One run's battery-wide keys: <see cref="BenchmarkComparabilityKey.Extract"/> narrowed to
    /// <see cref="BenchmarkBatteryKeyClass.BatteryWide"/>, with the prompt options rendered by
    /// <see cref="BenchmarkCandidatePromptOptions.BatteryWideSignature"/>.
    /// </summary>
    public static IReadOnlyList<BenchmarkComparabilityKeyEntry> BatteryWideKeys(BenchmarkRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        string promptOptions = BenchmarkCandidatePromptOptions
            .FromJson(run.CandidatePromptOptionsJson)
            .BatteryWideSignature(run.TestedModelSnapshot?.ParallelExecutionMode ?? MobileGnollHackLogger.Data.ParallelExecutionMode.Enabled);

        return BenchmarkComparabilityKey.Extract(run)
            .Where(k => Classify(k.Name) == BenchmarkBatteryKeyClass.BatteryWide)
            .Select(k => k.Name == BenchmarkComparabilityKey.CandidatePromptOptionsKey
                ? k with { Value = promptOptions }
                : k)
            .ToList();
    }

    /// <summary>
    /// Judges the members of one battery run, per suite index. The composite is permitted when
    /// every suite's runs resolve Tier A or B, every battery-wide key agrees across every member,
    /// and every suite of one board class carries one candidate system prompt. Speed-and-cost
    /// differences set the degraded flags; a differing wiki or source-code HEAD adds a caveat and
    /// refuses nothing.
    ///
    /// <para>A suite with no runs is not judged: whether every suite has a member is the
    /// completeness question (M4), not this one.</para>
    /// </summary>
    public static BenchmarkBatteryComparabilityResult Resolve(
        IReadOnlyList<(int SuiteIndex, IReadOnlyList<BenchmarkRun> Runs)> suites)
    {
        var judged = (suites ?? Array.Empty<(int, IReadOnlyList<BenchmarkRun>)>())
            .Select(s => (s.SuiteIndex, Runs: (s.Runs ?? Array.Empty<BenchmarkRun>()).Where(r => r != null).ToList()))
            .Where(s => s.Runs.Count > 0)
            .OrderBy(s => s.SuiteIndex)
            .ToList();

        if (judged.Count == 0)
        {
            return new BenchmarkBatteryComparabilityResult(
                false, false, false,
                Array.Empty<BenchmarkComparabilityKeyDifference>(),
                Array.Empty<BenchmarkBatterySuiteVerdict>(),
                "No runs: a battery composite is undefined over an empty set.");
        }

        var suiteOfRun = new Dictionary<long, int>();
        foreach (var suite in judged)
        {
            foreach (var run in suite.Runs) suiteOfRun[run.Id] = suite.SuiteIndex;
        }

        var verdicts = judged
            .Select(s => new BenchmarkBatterySuiteVerdict(
                s.SuiteIndex,
                SuiteLabel(s.Runs),
                BenchmarkComparabilityKey.Resolve(s.Runs)))
            .ToList();

        var all = judged.SelectMany(s => s.Runs).ToList();

        var batteryWide = Differences(all, BatteryWideKeys);
        var speedAndCost = Differences(all, run => BenchmarkComparabilityKey.Extract(run)
            .Where(k => Classify(k.Name) == BenchmarkBatteryKeyClass.SpeedAndCost)
            .ToList());
        var promptDifferences = PromptDifferencesByBoardClass(all);

        bool speedDegraded = verdicts.Any(v => v.Comparability.SpeedAggregatesDegraded);
        bool costDegraded = verdicts.Any(v => v.Comparability.CostAggregatesDegraded);
        var taxonomy = BenchmarkComparabilityKey.Extract(all[0]).ToDictionary(k => k.Name, StringComparer.Ordinal);
        foreach (var difference in speedAndCost)
        {
            speedDegraded |= taxonomy[difference.Name].DegradesSpeed;
            costDegraded |= taxonomy[difference.Name].DegradesCost;
        }

        var refusingSuites = verdicts.Where(v => !v.Comparability.PoolingPermitted).ToList();
        bool permitted = refusingSuites.Count == 0 && batteryWide.Count == 0 && promptDifferences.Count == 0;

        if (!permitted)
        {
            speedDegraded = false;
            costDegraded = false;
        }

        var reasons = new List<string>();
        foreach (var verdict in refusingSuites)
        {
            reasons.Add($"Suite {verdict.SuiteIndex} ('{verdict.SuiteName}') does not pool: {verdict.Comparability.Explanation}");
        }

        if (batteryWide.Count > 0)
        {
            reasons.Add("Battery-wide keys differ across the members: "
                + string.Join("; ", batteryWide.Select(d => DescribeWithSuites(d, suiteOfRun))) + ".");
        }

        foreach (var difference in promptDifferences)
        {
            reasons.Add("Suites of one board class carry different candidate system prompts: "
                + DescribeWithSuites(difference, suiteOfRun) + ".");
        }

        string explanation;
        if (permitted)
        {
            explanation = $"The composite is permitted: the runs of every suite pool, and every battery-wide key "
                + $"matches across {all.Count} runs of {judged.Count} suites.";
            if (speedDegraded || costDegraded)
            {
                explanation += " " + (speedDegraded && costDegraded ? "Speed and cost composites are"
                    : speedDegraded ? "The speed composite is" : "The cost composite is")
                    + " degraded: " + string.Join("; ", speedAndCost.Select(d => DescribeWithSuites(d, suiteOfRun))) + ".";
            }
        }
        else
        {
            explanation = "The composite is refused. " + string.Join(" ", reasons);
        }

        return new BenchmarkBatteryComparabilityResult(
            permitted, speedDegraded, costDegraded, batteryWide, verdicts, explanation)
        {
            PromptDifferences = promptDifferences,
            SpeedAndCostDifferences = speedAndCost,
            Caveats = ProvenanceCaveats(all, suiteOfRun)
        };
    }

    /// <summary>
    /// The comparability class of a battery result (M9): SHA-256, lower-case hex, over one
    /// <c>suiteId:signature</c> line per suite in ascending suite id, where the signature is the
    /// <see cref="BenchmarkCrossModelComparability.MustMatchSignature"/> the suite's usable runs
    /// share. Null when a suite has no run or its runs do not share one signature.
    /// </summary>
    public static string? ComparabilityClass(IReadOnlyList<(long SuiteId, IReadOnlyList<BenchmarkRun> Runs)> suites)
    {
        if (suites == null || suites.Count == 0) return null;

        var lines = new List<string>();
        foreach (var suite in suites.OrderBy(s => s.SuiteId))
        {
            var runs = (suite.Runs ?? Array.Empty<BenchmarkRun>()).Where(r => r != null).ToList();
            if (runs.Count == 0) return null;

            var signatures = runs
                .Select(BenchmarkCrossModelComparability.MustMatchSignature)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (signatures.Count != 1) return null;

            lines.Add(suite.SuiteId.ToString(CultureInfo.InvariantCulture) + ":" + signatures[0]);
        }

        return Sha256Hex(string.Join("\n", lines));
    }

    /// <summary>
    /// Whether two battery runs may be compared (M7). Requires equal definition hashes, the same
    /// suites with usable runs on both sides, coherent runs within each side's suite, and equal
    /// Fundamental keys suite for suite. Then exactly one of:
    /// <list type="bullet">
    /// <item>(a) model comparison — every must-match key
    /// (<see cref="BenchmarkCrossModelComparability.IsMustMatchKey"/>) agrees in every suite and at
    /// least one model-axis key differs;</item>
    /// <item>(b) verification — every Candidate key agrees in every suite and exactly one Instrument
    /// key name differs across the battery, in one suite or in all of them.</item>
    /// </list>
    /// Speed-and-cost differences degrade the speed and cost comparison and refuse nothing.
    /// </summary>
    public static BenchmarkBatteryComparisonEligibility CanCompare(
        BenchmarkBatteryComparisonSide baseline,
        BenchmarkBatteryComparisonSide treatment)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(treatment);

        if (!string.Equals(baseline.DefinitionSha256, treatment.DefinitionSha256, StringComparison.Ordinal))
        {
            return Refused("The two battery runs have different definitions (" + Short(baseline.DefinitionSha256)
                + " vs " + Short(treatment.DefinitionSha256) + "): their suites or weights differ, so their "
                + "Overall Indices estimate different things.");
        }

        var baselineSuites = SuitesById(baseline);
        var treatmentSuites = SuitesById(treatment);

        var missing = new List<string>();
        foreach (long suiteId in baselineSuites.Keys.Union(treatmentSuites.Keys).OrderBy(id => id))
        {
            if (baselineSuites.GetValueOrDefault(suiteId)?.Count is not > 0) missing.Add($"#{suiteId} has no usable run on the baseline side");
            if (treatmentSuites.GetValueOrDefault(suiteId)?.Count is not > 0) missing.Add($"#{suiteId} has no usable run on the treatment side");
        }

        if (baselineSuites.Count == 0 && treatmentSuites.Count == 0)
        {
            return Refused("Neither side lists any suite.");
        }

        if (missing.Count > 0)
        {
            return Refused("Every suite needs usable runs on both sides: suite " + string.Join("; suite ", missing) + ".");
        }

        var differences = new List<BenchmarkBatteryComparisonDifference>();
        var incoherent = new List<string>();
        var absent = new List<string>();
        bool speedDegraded = false;
        bool costDegraded = false;

        foreach (long suiteId in baselineSuites.Keys.OrderBy(id => id))
        {
            var baselineRuns = baselineSuites[suiteId];
            var treatmentRuns = treatmentSuites[suiteId];
            string suiteName = SuiteLabel(baselineRuns);

            if (baselineRuns.Concat(treatmentRuns).Any(BenchmarkCrossModelComparability.HasAbsentFundamentalIdentity))
            {
                absent.Add($"'{suiteName}' (#{suiteId})");
                continue;
            }

            var baselineKeys = baselineRuns.Select(BenchmarkCrossModelComparability.Keys).ToList();
            var treatmentKeys = treatmentRuns.Select(BenchmarkCrossModelComparability.Keys).ToList();

            foreach (var key in baselineKeys[0])
            {
                var baselineValues = DistinctValues(baselineKeys, key.Name);
                var treatmentValues = DistinctValues(treatmentKeys, key.Name);
                bool degrading = BenchmarkCrossModelComparability.IsDegradingKey(key.Name);

                if (!degrading && (baselineValues.Count > 1 || treatmentValues.Count > 1))
                {
                    incoherent.Add($"{key.Name} in '{suiteName}' (#{suiteId})");
                    continue;
                }

                bool differs = degrading
                    ? baselineValues.Union(treatmentValues, StringComparer.Ordinal).Count() > 1
                    : !string.Equals(baselineValues[0], treatmentValues[0], StringComparison.Ordinal);
                if (!differs) continue;

                if (degrading)
                {
                    speedDegraded |= key.DegradesSpeed;
                    costDegraded |= key.DegradesCost;
                }

                differences.Add(new BenchmarkBatteryComparisonDifference
                {
                    Name = key.Name,
                    Kind = key.Kind,
                    SuiteId = suiteId,
                    SuiteName = suiteName,
                    BaselineValue = string.Join(" / ", baselineValues),
                    TreatmentValue = string.Join(" / ", treatmentValues)
                });
            }
        }

        var differingKeys = differences.Select(d => d.Name).Distinct(StringComparer.Ordinal).ToList();

        if (absent.Count > 0)
        {
            return Refused("The exam cannot be identified in suite " + string.Join(", ", absent)
                + ": a Fundamental key has no value, so agreement on it would be absence, not a match.", differences);
        }

        if (incoherent.Count > 0)
        {
            return Refused("The runs within one side's suite disagree on " + string.Join("; ", incoherent)
                + ", so that side is not one condition.", differences);
        }

        var fundamental = differences.Where(d => d.Kind == BenchmarkComparabilityKeyKind.Fundamental).ToList();
        if (fundamental.Count > 0)
        {
            return Refused("The two battery runs sat different exams: "
                + string.Join("; ", fundamental.Select(d => d.Describe())) + ".", differences);
        }

        var quality = differences.Where(d => !BenchmarkCrossModelComparability.IsDegradingKey(d.Name)).ToList();
        var mustMatch = quality.Where(d => BenchmarkCrossModelComparability.IsMustMatchKey(d.Name)).ToList();
        var modelAxis = quality.Where(d => BenchmarkCrossModelComparability.IsModelAxisKey(d.Name)).ToList();
        var candidate = quality.Where(d => d.Kind == BenchmarkComparabilityKeyKind.Candidate).ToList();
        var instrumentNames = quality
            .Where(d => d.Kind == BenchmarkComparabilityKeyKind.Instrument)
            .Select(d => d.Name)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        string degradedNote = DegradedNote(speedDegraded, costDegraded, differences);

        if (mustMatch.Count == 0 && modelAxis.Count > 0)
        {
            return new BenchmarkBatteryComparisonEligibility
            {
                Allowed = true,
                Kind = BenchmarkBatteryComparisonKind.ModelComparison,
                Differences = differences,
                DifferingKeys = differingKeys,
                SpeedDegraded = speedDegraded,
                CostDegraded = costDegraded,
                Explanation = "Model comparison: every must-match key agrees in every suite, and the model axis "
                    + "differs on " + string.Join(", ", modelAxis.Select(d => d.Name).Distinct(StringComparer.Ordinal))
                    + "." + degradedNote
            };
        }

        if (candidate.Count == 0 && instrumentNames.Count == 1)
        {
            int suiteCount = quality.Select(d => d.SuiteId).Distinct().Count();
            return new BenchmarkBatteryComparisonEligibility
            {
                Allowed = true,
                Kind = BenchmarkBatteryComparisonKind.Verification,
                Differences = differences,
                DifferingKeys = differingKeys,
                SpeedDegraded = speedDegraded,
                CostDegraded = costDegraded,
                Explanation = $"Verification of a change: the candidate is identical, and one instrument key, "
                    + $"{instrumentNames[0]}, differs in {suiteCount} of {baselineSuites.Count} suites." + degradedNote
            };
        }

        string reason;
        if (quality.Count == 0)
        {
            reason = "No quality-relevant key differs: the two battery runs are replicates of one condition, "
                + "neither a model comparison nor a verification of a change.";
        }
        else if (modelAxis.Count > 0)
        {
            reason = "The model axis differs, but so do must-match keys: "
                + string.Join("; ", mustMatch.Select(d => d.Describe())) + ".";
        }
        else if (candidate.Count > 0)
        {
            reason = "A candidate key outside the model axis differs, which is neither a model comparison nor "
                + "a single instrument change: " + string.Join("; ", candidate.Select(d => d.Describe())) + ".";
        }
        else
        {
            reason = $"{instrumentNames.Count} instrument keys differ ({string.Join(", ", instrumentNames)}); a "
                + "verification allows exactly one.";
        }

        return Refused(reason, differences);
    }

    // --- Attaching existing runs ------------------------------------------------------------------

    /// <summary>
    /// The one eligibility rule for placing an existing run in a slot of a battery run, shared by the
    /// attach, the candidate list and the reuse preview. The run is accepted when, in this order:
    /// <list type="number">
    /// <item>its suite (<c>BenchmarkSuiteIdUsed ?? BenchmarkSuiteId</c>) is the slot's suite;</item>
    /// <item>it is usable (M4) as a fresh member: finished successfully with an index;</item>
    /// <item>it was graded under the current scoring method;</item>
    /// <item>its tested configuration is the battery run's;</item>
    /// <item>its five instrument hashes equal the suite's fingerprint, a hash missing on either side
    /// not counting (decision 8);</item>
    /// <item>it fills no other slot of the battery run;</item>
    /// <item>with no usable member to agree with, it carries the required harness version;</item>
    /// <item><see cref="Resolve"/> over the usable members plus this run still permits the
    /// composite.</item>
    /// </list>
    /// Whether the slot itself is free is <see cref="AttachSlotRefusal"/>'s question.
    /// </summary>
    public static BenchmarkBatteryAttachCheck CheckAttach(BenchmarkRun run, BenchmarkBatteryAttachTarget target)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(target);

        long? runSuiteId = run.BenchmarkSuiteIdUsed ?? run.BenchmarkSuiteId;
        if (runSuiteId != target.SuiteId)
        {
            return BenchmarkBatteryAttachCheck.Refused(
                $"Run #{run.Id} is a run of suite {(runSuiteId.HasValue ? "#" + runSuiteId.Value : "(unknown)")}, "
                + $"not of '{target.SuiteName}' (#{target.SuiteId}).");
        }

        string? unusable = BenchmarkBatteryPlanner.UnusableReason(new BenchmarkBatteryRunMember(), run);
        if (unusable != null)
        {
            return BenchmarkBatteryAttachCheck.Refused($"Run #{run.Id} has no usable result: {unusable}.");
        }

        if (run.ScoringMethodVersion != target.ScoringMethodVersion)
        {
            return BenchmarkBatteryAttachCheck.Refused(
                $"Run #{run.Id} was graded under scoring method {run.ScoringMethodVersion}; this build grades under "
                + $"{target.ScoringMethodVersion}.");
        }

        if (run.TestedModelConfigurationId != target.TestedModelConfigurationId)
        {
            return BenchmarkBatteryAttachCheck.Refused(
                $"Run #{run.Id} tested configuration "
                + $"{(run.TestedModelConfigurationId.HasValue ? "#" + run.TestedModelConfigurationId.Value : "(deleted)")}, "
                + $"not the battery run's #{target.TestedModelConfigurationId}.");
        }

        var moved = BenchmarkBatteryOrchestrator.FingerprintDifferences(
            target.Fingerprint, BenchmarkBatteryOrchestrator.FingerprintOf(run));
        if (moved.Count > 0)
        {
            return BenchmarkBatteryAttachCheck.Refused(
                $"Run #{run.Id} ran under another instrument than the one recorded for '{target.SuiteName}': "
                + $"{string.Join(", ", moved)} differ.");
        }

        var members = (target.UsableMembers ?? Array.Empty<(int, IReadOnlyList<BenchmarkRun>)>())
            .Select(s => (s.SuiteIndex, Runs: (s.Runs ?? Array.Empty<BenchmarkRun>()).Where(r => r != null).ToList()))
            .Where(s => s.Runs.Count > 0)
            .ToList();

        if ((target.OccupiedRunIds ?? Array.Empty<long>()).Contains(run.Id)
            || members.Any(s => s.Runs.Any(r => r.Id == run.Id)))
        {
            return BenchmarkBatteryAttachCheck.Refused($"Run #{run.Id} already fills another slot of this battery run.");
        }

        if (members.Count == 0
            && target.HarnessVersion != null
            && !string.Equals(run.HarnessVersion, target.HarnessVersion, StringComparison.Ordinal))
        {
            return BenchmarkBatteryAttachCheck.Refused(
                $"Run #{run.Id} ran under harness {run.HarnessVersion ?? "unrecorded"}; this build is harness "
                + $"{target.HarnessVersion}, and the battery run holds no usable member it could agree with instead.");
        }

        bool placed = false;
        var withRun = new List<(int SuiteIndex, IReadOnlyList<BenchmarkRun> Runs)>();
        foreach (var (suiteIndex, runs) in members)
        {
            if (suiteIndex == target.SuiteIndex)
            {
                withRun.Add((suiteIndex, runs.Append(run).ToList()));
                placed = true;
            }
            else
            {
                withRun.Add((suiteIndex, runs));
            }
        }

        if (!placed) withRun.Add((target.SuiteIndex, new[] { run }));

        var verdict = Resolve(withRun);
        return verdict.CompositePermitted
            ? BenchmarkBatteryAttachCheck.Accepted
            : BenchmarkBatteryAttachCheck.Refused(
                $"Run #{run.Id} would not combine with the battery run's usable members. {verdict.Explanation}");
    }

    /// <summary>
    /// Why the (suite, round) slot cannot take an attached run, or null when it can: it lies inside
    /// the grid, and it is empty or held by a member that is not usable (which the attach
    /// supersedes). <paramref name="occupant"/> is the slot's non-superseded member, if any, and
    /// <paramref name="occupantRun"/> its run, null when that run was deleted.
    /// </summary>
    public static string? AttachSlotRefusal(
        int suiteCount,
        int runsPerSuite,
        int suiteIndex,
        int round,
        BenchmarkBatteryRunMember? occupant,
        BenchmarkRun? occupantRun)
    {
        if (suiteIndex < 0 || suiteIndex >= suiteCount)
        {
            return $"Suite {suiteIndex + 1} is not in this battery run, which has {suiteCount} suites.";
        }

        if (round < 1 || round > runsPerSuite)
        {
            return $"Round {round} is not in this battery run, which has {runsPerSuite} rounds.";
        }

        if (occupant == null || occupant.Superseded || occupantRun == null) return null;

        return BenchmarkBatteryPlanner.IsUsable(occupant, occupantRun)
            ? $"Suite {suiteIndex + 1}, round {round} already holds a usable member (run #{occupant.BenchmarkRunId})."
            : null;
    }

    // --- Helpers ---------------------------------------------------------------------------------

    private static BenchmarkBatteryComparisonEligibility Refused(
        string explanation,
        IReadOnlyList<BenchmarkBatteryComparisonDifference>? differences = null)
    {
        var list = differences ?? Array.Empty<BenchmarkBatteryComparisonDifference>();
        return new BenchmarkBatteryComparisonEligibility
        {
            Allowed = false,
            Kind = null,
            Differences = list,
            DifferingKeys = list.Select(d => d.Name).Distinct(StringComparer.Ordinal).ToList(),
            Explanation = "Comparison refused. " + explanation
        };
    }

    private static string DegradedNote(
        bool speedDegraded,
        bool costDegraded,
        IReadOnlyList<BenchmarkBatteryComparisonDifference> differences)
    {
        if (!speedDegraded && !costDegraded) return string.Empty;

        var keys = differences
            .Where(d => BenchmarkCrossModelComparability.IsDegradingKey(d.Name))
            .Select(d => d.Name)
            .Distinct(StringComparer.Ordinal);
        string axes = speedDegraded && costDegraded ? "speed and cost comparisons are"
            : speedDegraded ? "speed comparison is" : "cost comparison is";
        return $" The {axes} degraded ({string.Join(", ", keys)}).";
    }

    private static Dictionary<long, List<BenchmarkRun>> SuitesById(BenchmarkBatteryComparisonSide side)
    {
        return (side.Suites ?? Array.Empty<BenchmarkBatteryComparisonSuite>())
            .Where(s => s != null)
            .GroupBy(s => s.SuiteId)
            .ToDictionary(
                g => g.Key,
                g => g.SelectMany(s => s.Runs ?? Array.Empty<BenchmarkRun>()).Where(r => r != null).ToList());
    }

    private static List<string> DistinctValues(IEnumerable<IReadOnlyList<BenchmarkComparabilityKeyEntry>> perRun, string name)
    {
        return perRun
            .Select(keys => keys.FirstOrDefault(k => k.Name == name)?.Value ?? BenchmarkComparabilityKey.NoValue)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(v => v, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Every key of <paramref name="keys"/> on which <paramref name="runs"/> disagree, with the runs carrying each value.</summary>
    private static List<BenchmarkComparabilityKeyDifference> Differences(
        IReadOnlyList<BenchmarkRun> runs,
        Func<BenchmarkRun, IReadOnlyList<BenchmarkComparabilityKeyEntry>> keys)
    {
        var perRun = runs.Select(r => (Run: r, Keys: keys(r))).ToList();
        var differences = new List<BenchmarkComparabilityKeyDifference>();

        foreach (var key in perRun[0].Keys)
        {
            var variants = perRun
                .GroupBy(p => p.Keys.FirstOrDefault(k => k.Name == key.Name)?.Value ?? BenchmarkComparabilityKey.NoValue,
                    StringComparer.Ordinal)
                .Select(g => new BenchmarkComparabilityKeyVariant
                {
                    Value = g.Key,
                    RunIds = g.Select(p => p.Run.Id).OrderBy(id => id).ToList()
                })
                .OrderBy(v => v.RunIds.Min())
                .ToList();

            if (variants.Count <= 1) continue;

            differences.Add(new BenchmarkComparabilityKeyDifference
            {
                Name = key.Name,
                Kind = key.Kind,
                Variants = variants
            });
        }

        return differences;
    }

    /// <summary>
    /// The candidate system prompt depends on the suite only through the board flag, so every suite
    /// with the same <see cref="BenchmarkCandidatePromptOptions.HasGameSnapshot"/> must carry the same
    /// <see cref="BenchmarkRun.CandidateSystemPromptSha256"/>. One difference per board class that
    /// does not.
    /// </summary>
    private static List<BenchmarkComparabilityKeyDifference> PromptDifferencesByBoardClass(IReadOnlyList<BenchmarkRun> runs)
    {
        var differences = new List<BenchmarkComparabilityKeyDifference>();

        foreach (var boardClass in runs
                     .GroupBy(r => BenchmarkCandidatePromptOptions.FromJson(r.CandidatePromptOptionsJson).HasGameSnapshot)
                     .OrderBy(g => g.Key))
        {
            differences.AddRange(Differences(boardClass.ToList(), run => new[]
            {
                BenchmarkComparabilityKey.Extract(run).Single(k => k.Name == BenchmarkComparabilityKey.CandidateSystemPromptKey)
            }));
        }

        return differences;
    }

    private static IReadOnlyList<string> ProvenanceCaveats(IReadOnlyList<BenchmarkRun> runs, IReadOnlyDictionary<long, int> suiteOfRun)
    {
        var caveats = new List<string>();
        AddProvenanceCaveat(caveats, WikiHeadName, runs, r => r.WikiHeadSha, suiteOfRun);
        AddProvenanceCaveat(caveats, SourceCodeHeadName, runs, r => r.SourceCodeHeadSha, suiteOfRun);
        return caveats;
    }

    private static void AddProvenanceCaveat(
        List<string> caveats,
        string name,
        IReadOnlyList<BenchmarkRun> runs,
        Func<BenchmarkRun, string?> value,
        IReadOnlyDictionary<long, int> suiteOfRun)
    {
        var recorded = runs
            .Where(r => !string.IsNullOrWhiteSpace(value(r)))
            .GroupBy(r => value(r)!.Trim(), StringComparer.Ordinal)
            .OrderBy(g => g.Min(r => r.Id))
            .ToList();

        if (recorded.Count <= 1) return;

        var parts = recorded.Select(g =>
        {
            var runIds = g.Select(r => r.Id).OrderBy(id => id).ToList();
            var suites = runIds.Select(id => suiteOfRun[id]).Distinct().OrderBy(i => i);
            return $"{Short(g.Key)} (runs {string.Join(", ", runIds)}; suites {string.Join(", ", suites)})";
        });

        caveats.Add($"{name} differs across the members: {string.Join(" vs ", parts)}. It is provenance, not a "
            + "comparability key, so nothing was refused; the members may have had different wiki or source "
            + "material to retrieve.");
    }

    private static string DescribeWithSuites(BenchmarkComparabilityKeyDifference difference, IReadOnlyDictionary<long, int> suiteOfRun)
    {
        var parts = difference.Variants.Select(v =>
        {
            var suites = v.RunIds.Where(suiteOfRun.ContainsKey).Select(id => suiteOfRun[id]).Distinct().OrderBy(i => i);
            return $"{v.Value} (runs {string.Join(", ", v.RunIds)}; suites {string.Join(", ", suites)})";
        });
        return $"{difference.Name}: {string.Join(" vs ", parts)}";
    }

    private static string SuiteLabel(IReadOnlyList<BenchmarkRun> runs)
        => runs.Select(r => r.SuiteName).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? string.Empty;

    private static string Short(string? hash)
        => string.IsNullOrEmpty(hash) ? BenchmarkComparabilityKey.NoValue : hash.Length > 12 ? hash.Substring(0, 12) : hash;

    private static string Sha256Hex(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (byte b in bytes)
        {
            sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }
}
