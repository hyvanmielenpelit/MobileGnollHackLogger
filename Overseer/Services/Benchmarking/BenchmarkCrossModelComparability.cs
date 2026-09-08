namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using MobileGnollHackLogger.Data;

/// <summary>
/// What a cross-model entry is allowed to contribute to. A flag combination, because one entry can
/// be sound on quality and degraded on both of the other two axes at once.
///
/// <para><see cref="Comparable"/> is the absence of every flag rather than a flag of its own, so a
/// consumer that forgets to test one of the others cannot accidentally read a degraded entry as a
/// clean one.</para>
/// </summary>
[Flags]
public enum BenchmarkCrossModelState
{
    /// <summary>Every axis is sound: the entry may be plotted on quality, speed and cost.</summary>
    Comparable = 0,

    /// <summary>The speed axis mixes timing conditions. The point is plotted, the axis is flagged.</summary>
    DegradedSpeed = 1,

    /// <summary>The cost axis mixes pricing or timing conditions. The point is plotted, the axis is flagged.</summary>
    DegradedCost = 2,

    /// <summary>
    /// The entry measures something else and reaches no chart at all. Its measures are withheld
    /// rather than merely marked, so no consumer can plot it by ignoring a flag.
    /// </summary>
    Excluded = 4
}

/// <summary>
/// One point of a cross-model comparison, before its comparability has been judged: a candidate
/// model as configured, evidenced by one run (<i>R</i> = 1) or by the members of one poolable group
/// (<i>R</i> ≥ 2).
/// </summary>
public sealed record BenchmarkCrossModelEntry
{
    /// <summary>The caller's identifier for this point, echoed back on the verdict.</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>
    /// The runs behind the point. Must be non-empty. More than one run is only meaningful when the
    /// caller has already established that they pool — this class re-checks the model-axis and
    /// must-match keys within the entry, and excludes an entry whose own runs disagree on either.
    /// </summary>
    public IReadOnlyList<BenchmarkRun> Runs { get; init; } = Array.Empty<BenchmarkRun>();
}

/// <summary>The verdict on one entry, and everything a UI needs to say <i>why</i> rather than <i>that</i>.</summary>
public sealed record BenchmarkCrossModelVerdict
{
    public string Key { get; init; } = string.Empty;

    public BenchmarkCrossModelState State { get; init; }

    public bool IsExcluded => (State & BenchmarkCrossModelState.Excluded) != 0;

    public bool IsSpeedDegraded => (State & BenchmarkCrossModelState.DegradedSpeed) != 0;

    public bool IsCostDegraded => (State & BenchmarkCrossModelState.DegradedCost) != 0;

    public bool IsComparable => State == BenchmarkCrossModelState.Comparable;

    /// <summary>
    /// The must-match keys on which this entry parts company with the baseline, by name. Named
    /// rather than counted: "excluded" with no key is an accusation a reader cannot check.
    /// </summary>
    public IReadOnlyList<string> ExcludingKeys { get; init; } = Array.Empty<string>();

    /// <summary>Keys whose difference cost this entry its speed axis.</summary>
    public IReadOnlyList<string> SpeedDegradingKeys { get; init; } = Array.Empty<string>();

    /// <summary>Keys whose difference cost this entry its cost axis.</summary>
    public IReadOnlyList<string> CostDegradingKeys { get; init; } = Array.Empty<string>();

    /// <summary>
    /// The differing keys with their values — the baseline's and this entry's — reusing the shape
    /// the single-group tier dialog already renders.
    /// </summary>
    public IReadOnlyList<BenchmarkComparabilityKeyDifference> Differences { get; init; }
        = Array.Empty<BenchmarkComparabilityKeyDifference>();

    /// <summary>A sentence naming the state and exactly what moved.</summary>
    public string Explanation { get; init; } = string.Empty;
}

/// <summary>The judgement over a whole set of cross-model entries.</summary>
public sealed record BenchmarkCrossModelComparabilityResult
{
    /// <summary>One verdict per input entry, in input order.</summary>
    public IReadOnlyList<BenchmarkCrossModelVerdict> Entries { get; init; }
        = Array.Empty<BenchmarkCrossModelVerdict>();

    /// <summary>The entries that agreed on every must-match key, and therefore define the baseline.</summary>
    public IReadOnlyList<string> BaselineEntryKeys { get; init; } = Array.Empty<string>();

    /// <summary>The baseline's value for each must-match key, so a report can print the condition.</summary>
    public IReadOnlyDictionary<string, string> BaselineKeyValues { get; init; }
        = new Dictionary<string, string>();

    /// <summary>
    /// Thinking level differs across the entries that reached the charts. A disclosed caveat on the
    /// speed axis, never an exclusion — see <see cref="BenchmarkCrossModelComparability.ThinkingLevelSpeedCaveat"/>.
    /// </summary>
    public bool ThinkingLevelsDiffer { get; init; }

    /// <summary>The caveat text when <see cref="ThinkingLevelsDiffer"/>; null otherwise.</summary>
    public string? SpeedAxisCaveat { get; init; }

    /// <summary>A sentence describing the set: how many points may be charted, and how many may not.</summary>
    public string Explanation { get; init; } = string.Empty;
}

/// <summary>
/// Which comparability keys a cross-model comparison is allowed to vary, and what happens to an
/// entry that varies anything else. Pure computation: no I/O, no AI calls, no writes.
///
/// <para>This is the scientific gate of the comparison view. <see cref="BenchmarkComparabilityKey"/>
/// answers <i>may these runs be averaged together?</i> and its answer for two different models is
/// always no — its <see cref="BenchmarkComparabilityKeyKind.Candidate"/> documentation says two
/// models are compared as two Tier A groups and never merged into one. This class answers the other
/// half of that sentence: <i>may these two groups appear on one chart?</i></para>
///
/// <para>So models are never pooled. One point per model, and the keys below decide which points
/// may share an axis. The failure this exists to prevent is the one a chart makes far easier than a
/// table: six confident bars whose underlying runs were graded by different rubrics, priced from
/// different catalogs, or answered under a different prompt configuration. Which is why the rule is
/// enforced here, in the service layer, and an excluded entry is denied its measures rather than
/// merely badged.</para>
/// </summary>
public static class BenchmarkCrossModelComparability
{
    /// <summary>
    /// The model axis: the only keys two points on one chart are allowed to differ on. They are
    /// exactly the keys that name <i>which model, configured how</i> — which is the question the
    /// view asks, so a difference here is the subject of the comparison rather than a threat to it.
    ///
    /// <para><see cref="BenchmarkComparabilityKey.CandidatePromptOptionsKey"/> is deliberately
    /// <b>not</b> here, although it is a Candidate-kind key. It is not a property of the model; it
    /// is the configuration of the production chat prompt the model was graded under, and a
    /// <c>verboseMode</c> that differs across two points makes them incomparable on Completeness,
    /// Conciseness and Readability. A set with mixed prompt options is not comparable, not merely
    /// degraded.</para>
    /// </summary>
    public static readonly IReadOnlyList<string> ModelAxisKeys = new[]
    {
        BenchmarkComparabilityKey.CandidateProviderKey,
        BenchmarkComparabilityKey.CandidateModelKey,
        BenchmarkComparabilityKey.CandidateThinkingLevelKey,
        BenchmarkComparabilityKey.CandidateReasoningModeKey,
        BenchmarkComparabilityKey.CandidateReasoningSummaryKey,
        BenchmarkComparabilityKey.CandidateServiceTierKey,
        BenchmarkComparabilityKey.CandidateMaxOutputTokensKey,
        BenchmarkComparabilityKey.CandidateParallelExecutionModeKey
    };

    /// <summary>
    /// The keys that degrade an axis instead of excluding the point, mirroring the Tier B treatment
    /// in <see cref="BenchmarkComparabilityKey"/>. Which axes each one costs is read from the key's
    /// own <see cref="BenchmarkComparabilityKeyEntry.DegradesSpeed"/> and
    /// <see cref="BenchmarkComparabilityKeyEntry.DegradesCost"/> flags rather than restated here, so
    /// there is one definition of what a differing parallelism or pricing snapshot invalidates.
    /// </summary>
    public static readonly IReadOnlyList<string> DegradingKeys = new[]
    {
        BenchmarkComparabilityKey.QuestionParallelismKey,
        BenchmarkComparabilityKey.PricingSnapshotKey
    };

    /// <summary>
    /// Everything that is neither a model-axis key nor a degrading key: both Fundamental keys and
    /// every Instrument key. A difference on any of them means the points were produced by
    /// different instruments — a different suite, item revision, system prompt, tool guide set,
    /// knowledge base, harness, scoring method version, scoring profile, grading configuration or
    /// per-question budget — and the entry is excluded from every chart.
    /// </summary>
    public static bool IsMustMatchKey(string name)
        => !IsModelAxisKey(name) && !IsDegradingKey(name);

    public static bool IsModelAxisKey(string name)
        => ModelAxisKeys.Contains(name, StringComparer.Ordinal);

    public static bool IsDegradingKey(string name)
        => DegradingKeys.Contains(name, StringComparer.Ordinal);

    /// <summary>
    /// The standing caveat on the speed axis of a set whose points were configured at different
    /// thinking levels.
    /// </summary>
    public const string ThinkingLevelSpeedCaveat =
        "Thinking level differs across the plotted models, and thinking level dominates model time. "
        + "These points compare the models as they are configured, which is the question this view "
        + "asks; they are not a claim about the models at one common thinking level.";

    /// <summary>
    /// One run's comparability keys as a cross-model comparison reads them.
    ///
    /// <para>Identical to <see cref="BenchmarkComparabilityKey.Extract"/> but for
    /// <see cref="BenchmarkComparabilityKey.CandidatePromptOptionsKey"/>, whose shared value folds
    /// the candidate's parallel execution mode into the prompt signature. That mode is a model-axis
    /// key here, so folding it in would make two points that differ only on batching mode differ on
    /// a must-match key as well, and exclude both for a reason the axis already accounts for. The
    /// prompt configuration is therefore compared on its own canonical rendering, from the same
    /// <see cref="BenchmarkCandidatePromptOptions"/> definition, so the two readings cannot drift.</para>
    /// </summary>
    public static IReadOnlyList<BenchmarkComparabilityKeyEntry> Keys(BenchmarkRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        string promptOptionsOnly = BenchmarkCandidatePromptOptions
            .FromJson(run.CandidatePromptOptionsJson)
            .ToCanonicalJson();

        return BenchmarkComparabilityKey.Extract(run)
            .Select(k => k.Name == BenchmarkComparabilityKey.CandidatePromptOptionsKey
                ? k with { Value = promptOptionsOnly }
                : k)
            .ToList();
    }

    /// <summary>
    /// The must-match key values of one run, in canonical key order: everything that is neither a
    /// model-axis key nor a degrading key.
    ///
    /// <para>The subset is derived from <see cref="IsMustMatchKey"/> rather than listed, so the index
    /// that offers a set of runs and the comparison that judges it read one taxonomy.</para>
    /// </summary>
    public static IReadOnlyList<BenchmarkComparabilityKeyEntry> MustMatchKeys(BenchmarkRun run)
        => Keys(run).Where(k => IsMustMatchKey(k.Name)).ToList();

    /// <summary>
    /// A stable signature over those values, as lower-case hex SHA-256: two runs may share a chart
    /// only if their signatures agree.
    ///
    /// <para>The canonical form is the one <see cref="BenchmarkComparabilityKey.ComputeKeyHash"/>
    /// uses — <c>name=value</c> pairs in key order, newline separated — narrowed to the must-match
    /// keys, so the signature moves exactly when a difference would exclude an entry.</para>
    /// </summary>
    public static string MustMatchSignature(BenchmarkRun run)
        => Sha256Hex(string.Join("\n", MustMatchKeys(run).Select(k => $"{k.Name}={k.Value}")));

    /// <summary>
    /// Judges a set of entries.
    ///
    /// <para>The baseline is the largest set of entries that agree on every must-match key — ties
    /// broken by total run count, then by input order, so the answer is deterministic. Defining it
    /// by majority rather than by a nominated entry matters: after a scoring method version moves,
    /// the corpus really is split, and the view should chart the larger half and name the smaller
    /// one, not chart whichever entry happened to be listed first.</para>
    ///
    /// <para>An entry whose own runs disagree on a model-axis or a must-match key is excluded on
    /// those keys before the baseline is chosen. Such an entry is not one model measured several
    /// times, so it cannot be one point.</para>
    /// </summary>
    /// <param name="entries">The candidate points, in the order the caller wants them reported.</param>
    /// <param name="repricedToOneBasis">
    /// Every entry's cost has been recomputed from one pricing basis, so a differing stored pricing
    /// snapshot no longer describes the figures being charted and does not degrade the cost axis.
    /// </param>
    public static BenchmarkCrossModelComparabilityResult Resolve(
        IReadOnlyList<BenchmarkCrossModelEntry>? entries,
        bool repricedToOneBasis = false)
    {
        var members = (entries ?? Array.Empty<BenchmarkCrossModelEntry>())
            .Where(e => e != null && e.Runs != null && e.Runs.Count > 0)
            .ToList();

        if (members.Count == 0)
        {
            return new BenchmarkCrossModelComparabilityResult
            {
                Explanation = "No entries: a cross-model comparison is undefined over an empty set."
            };
        }

        // The taxonomy is read from the runs rather than restated, so a key added to
        // BenchmarkComparabilityKey is governed here the moment it exists.
        var taxonomy = Keys(members[0].Runs[0])
            .ToDictionary(k => k.Name, k => k, StringComparer.Ordinal);
        var keyOrder = Keys(members[0].Runs[0]).Select(k => k.Name).ToList();

        var values = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        var internalExclusions = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var internalSpeedDegradation = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var internalCostDegradation = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var entry in members)
        {
            var perRun = entry.Runs.Select(Keys).ToList();
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            var excluding = new List<string>();
            var speedDegrading = new List<string>();
            var costDegrading = new List<string>();

            foreach (string name in keyOrder)
            {
                var distinct = perRun
                    .Select(keys => keys.FirstOrDefault(k => k.Name == name)?.Value
                                    ?? BenchmarkComparabilityKey.NoValue)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                map[name] = distinct[0];
                if (distinct.Count <= 1) continue;

                if (IsDegradingKey(name))
                {
                    AddDegradation(name, taxonomy, speedDegrading, costDegrading, repricedToOneBasis);
                }
                else
                {
                    // Both a must-match key and a model-axis key are fatal *within* one entry: the
                    // first means its runs used different instruments, the second means it is not
                    // one model.
                    excluding.Add(name);
                }
            }

            values[entry.Key] = map;
            internalExclusions[entry.Key] = excluding;
            internalSpeedDegradation[entry.Key] = speedDegrading;
            internalCostDegradation[entry.Key] = costDegrading;
        }

        var mustMatchKeys = keyOrder.Where(IsMustMatchKey).ToList();
        var coherent = members.Where(e => internalExclusions[e.Key].Count == 0).ToList();

        var baseline = ChooseBaseline(coherent, values, mustMatchKeys);
        var baselineValues = baseline.Count > 0
            ? mustMatchKeys.ToDictionary(name => name, name => values[baseline[0].Key][name], StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal);
        var baselineKeySet = new HashSet<string>(baseline.Select(e => e.Key), StringComparer.Ordinal);
        var baselineRunIds = baseline.SelectMany(e => e.Runs.Select(r => r.Id)).OrderBy(id => id).ToList();

        // Degradation is a property of an axis, not of one point: once the plotted set mixes timing
        // modes, every point on that axis is measured under mixed conditions, so the flag goes on
        // all of them rather than on whichever one differs from the majority.
        var setSpeedDegrading = new List<string>();
        var setCostDegrading = new List<string>();
        if (baseline.Count > 1)
        {
            foreach (string name in DegradingKeys)
            {
                int distinct = baseline
                    .Select(e => values[e.Key][name])
                    .Distinct(StringComparer.Ordinal)
                    .Count();

                if (distinct > 1)
                {
                    AddDegradation(name, taxonomy, setSpeedDegrading, setCostDegrading, repricedToOneBasis);
                }
            }
        }

        bool thinkingLevelsDiffer = baseline
            .Select(e => values[e.Key][BenchmarkComparabilityKey.CandidateThinkingLevelKey])
            .Distinct(StringComparer.Ordinal)
            .Count() > 1;

        var verdicts = new List<BenchmarkCrossModelVerdict>();
        foreach (var entry in members)
        {
            bool inBaseline = baselineKeySet.Contains(entry.Key);

            var excluding = new List<string>(internalExclusions[entry.Key]);
            var differences = new List<BenchmarkComparabilityKeyDifference>();

            if (!inBaseline)
            {
                foreach (string name in mustMatchKeys)
                {
                    if (!baselineValues.TryGetValue(name, out string? baselineValue)) continue;
                    string value = values[entry.Key][name];
                    if (string.Equals(value, baselineValue, StringComparison.Ordinal)) continue;

                    if (!excluding.Contains(name, StringComparer.Ordinal)) excluding.Add(name);

                    taxonomy.TryGetValue(name, out var key);

                    differences.Add(new BenchmarkComparabilityKeyDifference
                    {
                        Name = name,
                        Kind = key?.Kind ?? BenchmarkComparabilityKeyKind.Instrument,
                        Variants = new[]
                        {
                            new BenchmarkComparabilityKeyVariant { Value = baselineValue, RunIds = baselineRunIds },
                            new BenchmarkComparabilityKeyVariant
                            {
                                Value = value,
                                RunIds = entry.Runs.Select(r => r.Id).OrderBy(id => id).ToList()
                            }
                        }
                    });
                }
            }

            var state = BenchmarkCrossModelState.Comparable;
            var speedKeys = new List<string>();
            var costKeys = new List<string>();

            if (excluding.Count > 0 || !inBaseline)
            {
                state |= BenchmarkCrossModelState.Excluded;
            }
            else
            {
                speedKeys = setSpeedDegrading.Concat(internalSpeedDegradation[entry.Key])
                    .Distinct(StringComparer.Ordinal).ToList();
                costKeys = setCostDegrading.Concat(internalCostDegradation[entry.Key])
                    .Distinct(StringComparer.Ordinal).ToList();

                if (speedKeys.Count > 0) state |= BenchmarkCrossModelState.DegradedSpeed;
                if (costKeys.Count > 0) state |= BenchmarkCrossModelState.DegradedCost;
            }

            verdicts.Add(new BenchmarkCrossModelVerdict
            {
                Key = entry.Key,
                State = state,
                ExcludingKeys = excluding,
                SpeedDegradingKeys = speedKeys,
                CostDegradingKeys = costKeys,
                Differences = differences,
                Explanation = Explain(state, excluding, speedKeys, costKeys)
            });
        }

        int excluded = verdicts.Count(v => v.IsExcluded);
        int charted = verdicts.Count - excluded;

        return new BenchmarkCrossModelComparabilityResult
        {
            Entries = verdicts,
            BaselineEntryKeys = baseline.Select(e => e.Key).ToList(),
            BaselineKeyValues = baselineValues,
            ThinkingLevelsDiffer = thinkingLevelsDiffer,
            SpeedAxisCaveat = thinkingLevelsDiffer ? ThinkingLevelSpeedCaveat : null,
            Explanation = excluded == 0
                ? $"All {charted} entries share one instrument and may be charted together."
                : $"{charted} of {verdicts.Count} entries share one instrument and may be charted; "
                  + $"{excluded} measured something else and are listed with the keys that differ."
        };
    }

    /// <summary>
    /// The largest set of entries agreeing on every must-match key. Ties go to the set with the most
    /// runs behind it, then to the one whose first member came first in the input — an arbitrary
    /// tie-break, but a stable one, which is what a view refreshed twice needs.
    /// </summary>
    private static List<BenchmarkCrossModelEntry> ChooseBaseline(
        List<BenchmarkCrossModelEntry> coherent,
        Dictionary<string, Dictionary<string, string>> values,
        List<string> mustMatchKeys)
    {
        if (coherent.Count == 0) return new List<BenchmarkCrossModelEntry>();

        var order = coherent.Select((e, i) => (e.Key, Index: i))
            .ToDictionary(x => x.Key, x => x.Index, StringComparer.Ordinal);

        return coherent
            .GroupBy(e => string.Join("\n", mustMatchKeys.Select(k => $"{k}={values[e.Key][k]}")), StringComparer.Ordinal)
            .OrderByDescending(g => g.Count())
            .ThenByDescending(g => g.Sum(e => e.Runs.Count))
            .ThenBy(g => g.Min(e => order[e.Key]))
            .First()
            .OrderBy(e => order[e.Key])
            .ToList();
    }

    private static void AddDegradation(
        string name,
        IReadOnlyDictionary<string, BenchmarkComparabilityKeyEntry> taxonomy,
        List<string> speed,
        List<string> cost,
        bool repricedToOneBasis)
    {
        // A set whose costs were all recomputed from one basis is not charting the stored snapshot
        // prices at all, so a snapshot difference no longer describes the figures on the axis.
        if (repricedToOneBasis && name == BenchmarkComparabilityKey.PricingSnapshotKey) return;

        if (!taxonomy.TryGetValue(name, out var key)) return;

        if (key.DegradesSpeed && !speed.Contains(name, StringComparer.Ordinal)) speed.Add(name);
        if (key.DegradesCost && !cost.Contains(name, StringComparer.Ordinal)) cost.Add(name);
    }

    private static string Explain(
        BenchmarkCrossModelState state,
        IReadOnlyList<string> excluding,
        IReadOnlyList<string> speedKeys,
        IReadOnlyList<string> costKeys)
    {
        if ((state & BenchmarkCrossModelState.Excluded) != 0)
        {
            return "Excluded from every chart: this entry was produced by a different instrument. "
                + "Differing keys: " + string.Join(", ", excluding) + ".";
        }

        if (state == BenchmarkCrossModelState.Comparable)
        {
            return "Comparable: every key outside the model axis matches the baseline.";
        }

        var parts = new List<string>();
        if (speedKeys.Count > 0) parts.Add("speed (" + string.Join(", ", speedKeys) + ")");
        if (costKeys.Count > 0) parts.Add("cost (" + string.Join(", ", costKeys) + ")");

        return "Plotted with a degraded axis: quality is sound, and " + string.Join(" and ", parts)
            + " mix conditions across the set.";
    }

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
