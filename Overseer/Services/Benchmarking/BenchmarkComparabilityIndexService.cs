namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MobileGnollHackLogger.Data;
using Overseer.Models;

/// <summary>
/// Buckets the runs and groups a picker is offering into comparability conditions, so the operator
/// is told which of them may share a chart <i>before</i> asking for a comparison.
///
/// <para>The bucketing reproduces <see cref="BenchmarkCrossModelComparability.Resolve"/> exactly:
/// sources are grouped by their must-match signature, and the conditions are ordered by descending
/// source count, then descending total run count, then first appearance — the same tie-break that
/// chooses the baseline. Condition 1 is therefore the condition a comparison over the same sources
/// would chart, and everything outside it is what that comparison would exclude.</para>
///
/// <para>A group whose own members disagree on a must-match or a model-axis key is not one point at
/// all, so it is reported self-inconsistent and assigned no condition — which is what
/// <see cref="BenchmarkCrossModelComparability.Resolve"/> does with such an entry before it chooses
/// a baseline.</para>
///
/// <para>Read-only arithmetic over stored rows: no writes, no AI calls, and nothing here can start a
/// run.</para>
/// </summary>
public class BenchmarkComparabilityIndexService
{
    /// <summary>
    /// The most runs one index may cover. The runs table itself is capped at 200 rows, so this is
    /// the whole of what a picker can offer and the query is bounded by construction.
    /// </summary>
    public const int MaxRunIds = 200;

    /// <summary>The most groups one index may cover.</summary>
    public const int MaxGroupIds = 100;

    /// <summary>The condition label of a group whose own runs do not describe one point.</summary>
    public const string SelfInconsistentLabel = "Self-inconsistent";

    /// <summary>The condition label of a source with no loadable runs.</summary>
    public const string NoRunsLabel = "No runs";

    /// <summary>
    /// The rule that picks the reference condition, in one sentence.
    ///
    /// <para>It is emitted with the index rather than written into a template so that the text an
    /// operator reads and the tie-break the bucketing applies — most sources, then most runs, then
    /// first appearance — have one definition. The last clause is the reason the rule is worth
    /// stating at all: a reference level chosen by recency would silently redefine what a saved
    /// comparison means every time a run landed under a changed instrument.</para>
    /// </summary>
    public const string ReferenceSelectionRule =
        "The reference condition is the one with the most sources; ties go to the most runs, then "
        + "to the source offered first. It is never chosen by recency, so the same set of sources "
        + "always charts the same condition.";

    private readonly ApplicationDbContext _db;
    private readonly ILogger<BenchmarkComparabilityIndexService>? _logger;

    public BenchmarkComparabilityIndexService(
        ApplicationDbContext db,
        ILogger<BenchmarkComparabilityIndexService>? logger = null)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Builds the index over the requested runs and groups.
    /// </summary>
    public async Task<(BenchmarkComparabilityIndexDto? Result, string? Error)> BuildAsync(
        BenchmarkComparabilityIndexRequest? request, CancellationToken ct = default)
    {
        var runIds = (request?.RunIds ?? new List<long>()).Distinct().ToList();
        var groupIds = (request?.GroupIds ?? new List<long>()).Distinct().ToList();

        if (runIds.Count == 0 && groupIds.Count == 0)
        {
            return (null, "A comparability index needs at least one run or group.");
        }

        if (runIds.Count > MaxRunIds)
        {
            return (null, $"Too many runs: the comparability index covers at most {MaxRunIds} runs, "
                + $"and {runIds.Count} were requested.");
        }

        if (groupIds.Count > MaxGroupIds)
        {
            return (null, $"Too many groups: the comparability index covers at most {MaxGroupIds} "
                + $"groups, and {groupIds.Count} were requested.");
        }

        var groups = groupIds.Count == 0
            ? new List<BenchmarkRunGroup>()
            : await _db.BenchmarkRunGroups
                .AsNoTracking()
                .Include(g => g.Members)
                .Where(g => groupIds.Contains(g.Id))
                .ToListAsync(ct);

        var missingGroups = groupIds.Except(groups.Select(g => g.Id)).ToList();
        if (missingGroups.Count > 0)
        {
            return (null, $"Group(s) not found: {string.Join(", ", missingGroups)}.");
        }

        var wantedRunIds = runIds
            .Concat(groups.SelectMany(g => g.Members.Select(m => m.BenchmarkRunId)))
            .Distinct()
            .ToList();

        // Without the answer graph: the index covers up to 200 runs, and the comparability keys need
        // three columns out of it, not the whole thing.
        var runs = wantedRunIds.Count == 0
            ? new List<BenchmarkRun>()
            : await _db.BenchmarkRuns
                .AsNoTracking()
                .Where(r => wantedRunIds.Contains(r.Id))
                .OrderBy(r => r.Id)
                .ToListAsync(ct);

        var runsById = runs.ToDictionary(r => r.Id);
        var missingRuns = runIds.Except(runsById.Keys).ToList();
        if (missingRuns.Count > 0)
        {
            return (null, $"Run(s) not found: {string.Join(", ", missingRuns)}.");
        }

        await LoadItemRevisionsAsync(runsById, wantedRunIds, ct);

        var suiteNames = await LoadSuiteNamesAsync(runs, ct);

        var sources = BuildSources(runIds, groupIds, groups, runsById);
        var result = Build(sources, suiteNames, DateTime.UtcNow);

        _logger?.LogInformation(
            "Computed a comparability index over {SourceCount} sources ({RunCount} runs): "
            + "{ConditionCount} condition(s).",
            sources.Count, runs.Count, result.Conditions.Count);

        return (result, null);
    }

    /// <summary>
    /// Fills each run's <see cref="BenchmarkRun.Answers"/> with stubs carrying the run id, both
    /// question identity columns and the item revision.
    ///
    /// <para><b>Invariant:</b> those four columns are the whole of what
    /// <c>BenchmarkComparabilityKey.ItemRevisionSignature</c> reads out of the answers, and the item
    /// revision signature is the only comparability key that touches them at all. A stub graph
    /// therefore yields the identical signature to a fully loaded one. A revision signature that
    /// grows to read a further column must carry that column here as well, or this index will bucket
    /// runs that the comparison then splits.</para>
    /// </summary>
    private async Task LoadItemRevisionsAsync(
        IReadOnlyDictionary<long, BenchmarkRun> runsById,
        IReadOnlyList<long> wantedRunIds,
        CancellationToken ct)
    {
        if (wantedRunIds.Count == 0) return;

        var revisions = await _db.BenchmarkRunAnswers
            .AsNoTracking()
            .Where(a => wantedRunIds.Contains(a.BenchmarkRunId)
                        && (a.BenchmarkQuestionIdUsed != null || a.BenchmarkQuestionId != null))
            .Select(a => new
            {
                a.BenchmarkRunId,
                a.BenchmarkQuestionIdUsed,
                a.BenchmarkQuestionId,
                a.ItemRevisionUsed,
                a.AssessedDifficulty
            })
            .ToListAsync(ct);

        foreach (var byRun in revisions.GroupBy(a => a.BenchmarkRunId))
        {
            if (!runsById.TryGetValue(byRun.Key, out var run)) continue;

            run.Answers = byRun
                .Select(a => new BenchmarkRunAnswer
                {
                    BenchmarkRunId = a.BenchmarkRunId,
                    BenchmarkQuestionIdUsed = a.BenchmarkQuestionIdUsed,
                    BenchmarkQuestionId = a.BenchmarkQuestionId,
                    ItemRevisionUsed = a.ItemRevisionUsed,
                    AssessedDifficulty = a.AssessedDifficulty
                })
                .ToList();
        }
    }

    /// <summary>
    /// The names of the suites the loaded runs were taken from, by suite id.
    ///
    /// <para>The suite id is a must-match key, so a condition's members share one suite by
    /// construction and this map exists only to let the legend read the suite as a name instead of
    /// as a bare number. Two projected columns over the distinct ids of at most
    /// <see cref="MaxRunIds"/> runs, untracked: the value it decorates is still the id itself.</para>
    /// </summary>
    private async Task<IReadOnlyDictionary<long, string>> LoadSuiteNamesAsync(
        IReadOnlyList<BenchmarkRun> runs, CancellationToken ct)
    {
        var suiteIds = runs
            .Where(r => r.BenchmarkSuiteId.HasValue)
            .Select(r => r.BenchmarkSuiteId!.Value)
            .Distinct()
            .ToList();

        if (suiteIds.Count == 0) return new Dictionary<long, string>();

        var rows = await _db.BenchmarkSuites
            .AsNoTracking()
            .Where(s => suiteIds.Contains(s.Id))
            .Select(s => new { s.Id, s.Name })
            .ToListAsync(ct);

        return rows
            .Where(s => !string.IsNullOrWhiteSpace(s.Name))
            .ToDictionary(s => s.Id, s => s.Name);
    }

    /// <summary>
    /// The offered sources in request order — runs first, then groups — which is the order the
    /// first-appearance tie-break reads.
    /// </summary>
    private static List<IndexSource> BuildSources(
        IReadOnlyList<long> runIds,
        IReadOnlyList<long> groupIds,
        IReadOnlyList<BenchmarkRunGroup> groups,
        IReadOnlyDictionary<long, BenchmarkRun> runsById)
    {
        var sources = new List<IndexSource>();

        foreach (long runId in runIds)
        {
            sources.Add(new IndexSource
            {
                Key = $"run:{runId}",
                SourceKind = "Run",
                SourceId = runId,
                Runs = new[] { runsById[runId] }
            });
        }

        foreach (long groupId in groupIds)
        {
            var group = groups.First(g => g.Id == groupId);
            var members = group.Members
                .Select(m => runsById.GetValueOrDefault(m.BenchmarkRunId))
                .Where(r => r != null)
                .Select(r => r!)
                .OrderBy(r => r.Id)
                .ToList();

            sources.Add(new IndexSource
            {
                Key = $"group:{groupId}",
                SourceKind = "Group",
                SourceId = groupId,
                Runs = members
            });
        }

        return sources;
    }

    /// <summary>
    /// Buckets the sources and renders the index. Pure: the taxonomy, the signatures and the
    /// tie-break are all read from the runs already loaded.
    /// </summary>
    private static BenchmarkComparabilityIndexDto Build(
        IReadOnlyList<IndexSource> sources,
        IReadOnlyDictionary<long, string> suiteNames,
        DateTime computedAtUtc)
    {
        // The key order is a property of the taxonomy rather than of any run's data, so a request
        // that loaded no runs at all still reports the three key-name lists.
        var reference = sources.SelectMany(s => s.Runs).FirstOrDefault() ?? new BenchmarkRun();
        var keyOrder = BenchmarkCrossModelComparability.Keys(reference).Select(k => k.Name).ToList();
        var taxonomy = BenchmarkCrossModelComparability.Keys(reference)
            .ToDictionary(k => k.Name, k => k, StringComparer.Ordinal);
        var mustMatchKeyNames = BenchmarkCrossModelComparability.MustMatchKeys(reference)
            .Select(k => k.Name)
            .ToList();

        var states = new List<SourceState>();
        for (int i = 0; i < sources.Count; i++)
        {
            states.Add(Describe(sources[i], i, keyOrder));
        }

        var buckets = states
            .Where(s => s.Runs.Count > 0 && s.SelfInconsistentKeys.Count == 0)
            .GroupBy(s => s.Signature, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count())
            .ThenByDescending(g => g.Sum(s => s.Runs.Count))
            .ThenBy(g => g.Min(s => s.Order))
            .ToList();

        var ordinalBySignature = new Dictionary<string, int>(StringComparer.Ordinal);
        var conditions = new List<BenchmarkComparabilityConditionDto>();
        for (int i = 0; i < buckets.Count; i++)
        {
            int ordinal = i + 1;
            ordinalBySignature[buckets[i].Key] = ordinal;
            conditions.Add(new BenchmarkComparabilityConditionDto
            {
                Ordinal = ordinal,
                Label = ConditionLabel(ordinal),
                SourceCount = buckets[i].Count(),
                RunCount = buckets[i].Sum(s => s.Runs.Count),
                Signature = buckets[i].Key,
                NewestRunStartedAtUtc = buckets[i]
                    .SelectMany(s => s.Runs)
                    .Select(r => (DateTime?)r.StartedAtUtc)
                    .Max()
            });
        }

        var largest = buckets.Count > 0 ? buckets[0].ToList() : new List<SourceState>();
        var largestValues = largest.Count > 0
            ? mustMatchKeyNames.ToDictionary(
                name => name, name => largest[0].Values[name], StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal);
        var largestRunIds = largest.SelectMany(s => s.Runs.Select(r => r.Id)).OrderBy(id => id).ToList();
        var largestSignature = buckets.Count > 0 ? buckets[0].Key : string.Empty;

        var entries = new List<BenchmarkComparabilityIndexEntryDto>();
        foreach (var state in states)
        {
            int ordinal = state.Runs.Count > 0
                          && state.SelfInconsistentKeys.Count == 0
                          && ordinalBySignature.TryGetValue(state.Signature, out int assigned)
                ? assigned
                : 0;

            string label = ordinal > 0
                ? ConditionLabel(ordinal)
                : state.Runs.Count == 0 ? NoRunsLabel : SelfInconsistentLabel;

            bool inLargest = ordinal > 0
                && string.Equals(state.Signature, largestSignature, StringComparison.Ordinal);

            entries.Add(new BenchmarkComparabilityIndexEntryDto
            {
                Key = state.Key,
                SourceKind = state.SourceKind,
                SourceId = state.SourceId,
                ConditionOrdinal = ordinal,
                ConditionLabel = label,
                Signature = state.Signature,
                SelfInconsistent = state.SelfInconsistentKeys.Count > 0,
                SelfInconsistentKeys = state.SelfInconsistentKeys.ToList(),
                DifferencesFromLargest = inLargest || state.Runs.Count == 0
                    ? new List<BenchmarkComparabilityDifferenceDto>()
                    : Differences(state, mustMatchKeyNames, largestValues, largestRunIds, taxonomy),
                QuestionParallelism = state.Value(BenchmarkComparabilityKey.QuestionParallelismKey),
                PricingSnapshot = state.Value(BenchmarkComparabilityKey.PricingSnapshotKey)
            });
        }

        return new BenchmarkComparabilityIndexDto
        {
            ComputedAtUtc = computedAtUtc,
            Entries = entries,
            Conditions = conditions,
            LargestConditionKeys = DescribeKeys(mustMatchKeyNames, largestValues, taxonomy, suiteNames),
            ReferenceSelectionRule = BenchmarkComparabilityIndexService.ReferenceSelectionRule,
            MustMatchKeyNames = mustMatchKeyNames,
            ModelAxisKeyNames = BenchmarkCrossModelComparability.ModelAxisKeys.ToList(),
            DegradingKeyNames = BenchmarkCrossModelComparability.DegradingKeys.ToList()
        };
    }

    /// <summary>
    /// The condition's must-match keys in canonical key order, each carrying what the key is as well
    /// as the value the cohort agreed on.
    ///
    /// <para>The kind comes from the taxonomy the runs themselves reported, and the label,
    /// description and value kind from <see cref="BenchmarkComparabilityKey.Describe"/>, so a key
    /// added to the taxonomy is described here the moment it exists — plainly, if it has no entry
    /// there yet.</para>
    ///
    /// <para><see cref="BenchmarkComparabilityKeyValueDto.Value"/> is the canonical string compared
    /// for equality and is copied verbatim. Anything friendlier belongs in
    /// <see cref="BenchmarkComparabilityKeyValueDto.DisplayValue"/>, which the server fills only
    /// where it knows something a client cannot derive from the value alone.</para>
    /// </summary>
    private static List<BenchmarkComparabilityKeyValueDto> DescribeKeys(
        IReadOnlyList<string> mustMatchKeyNames,
        IReadOnlyDictionary<string, string> values,
        IReadOnlyDictionary<string, BenchmarkComparabilityKeyEntry> taxonomy,
        IReadOnlyDictionary<long, string> suiteNames)
    {
        var described = new List<BenchmarkComparabilityKeyValueDto>();

        foreach (string name in mustMatchKeyNames)
        {
            if (!values.TryGetValue(name, out string? value)) continue;

            var info = BenchmarkComparabilityKey.Describe(name);
            taxonomy.TryGetValue(name, out var key);

            described.Add(new BenchmarkComparabilityKeyValueDto
            {
                Name = name,
                Label = info.Label,
                Description = info.Description,
                Kind = (key?.Kind ?? BenchmarkComparabilityKeyKind.Instrument).ToString(),
                ValueKind = info.ValueKind.ToString(),
                Value = value,
                DisplayValue = SuiteDisplayValue(name, value, suiteNames)
            });
        }

        return described;
    }

    /// <summary>
    /// The suite key rendered as its name beside its id — the one must-match value whose meaning
    /// lives in another table and therefore cannot be derived from the value itself. Every other
    /// key, and a suite whose name was not loaded, has no friendlier rendering than its value.
    /// </summary>
    private static string? SuiteDisplayValue(
        string name, string value, IReadOnlyDictionary<long, string> suiteNames)
    {
        if (!string.Equals(name, BenchmarkComparabilityKey.SuiteKey, StringComparison.Ordinal)) return null;
        if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long suiteId)) return null;
        if (!suiteNames.TryGetValue(suiteId, out string? suiteName)) return null;

        return $"{suiteName} (#{suiteId.ToString(CultureInfo.InvariantCulture)})";
    }

    /// <summary>
    /// One source's key values, its must-match signature, and the keys its own runs disagree on.
    ///
    /// <para>The representative values are the first run's, and a key on which the runs disagree
    /// outside the degrading pair is fatal within one source: a must-match difference means its runs
    /// used different instruments, a model-axis difference means it is not one model.</para>
    /// </summary>
    private static SourceState Describe(IndexSource source, int order, IReadOnlyList<string> keyOrder)
    {
        var state = new SourceState
        {
            Key = source.Key,
            SourceKind = source.SourceKind,
            SourceId = source.SourceId,
            Runs = source.Runs,
            Order = order
        };

        if (source.Runs.Count == 0) return state;

        var perRun = source.Runs.Select(BenchmarkCrossModelComparability.Keys).ToList();

        foreach (string name in keyOrder)
        {
            var distinct = perRun
                .Select(keys => keys.FirstOrDefault(k => k.Name == name)?.Value
                                ?? BenchmarkComparabilityKey.NoValue)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            state.Values[name] = distinct[0];

            if (distinct.Count > 1 && !BenchmarkCrossModelComparability.IsDegradingKey(name))
            {
                state.SelfInconsistentKeys.Add(name);
            }
        }

        // An absent Fundamental identity equals every other absent one, so such an entry cannot be
        // charted with anything — including another entry whose identity is equally absent. Excluded
        // through the same path as a self-inconsistent entry, naming the keys that carry no value;
        // an entry where only some runs lack identity is already flagged by the loop above.
        if (source.Runs.Any(BenchmarkCrossModelComparability.HasAbsentFundamentalIdentity))
        {
            foreach (var key in perRun[0].Where(k =>
                k.Kind == BenchmarkComparabilityKeyKind.Fundamental
                && string.Equals(k.Value, BenchmarkComparabilityKey.NoValue, StringComparison.Ordinal)))
            {
                if (!state.SelfInconsistentKeys.Contains(key.Name))
                {
                    state.SelfInconsistentKeys.Add(key.Name);
                }
            }
        }

        state.Signature = BenchmarkCrossModelComparability.MustMatchSignature(source.Runs[0]);
        return state;
    }

    /// <summary>
    /// The must-match keys this source parts company with the largest condition on, in the shape the
    /// tier dialog already renders — so the picker's tooltip reads the same as the comparison's
    /// excluded-entries section.
    /// </summary>
    private static List<BenchmarkComparabilityDifferenceDto> Differences(
        SourceState state,
        IReadOnlyList<string> mustMatchKeyNames,
        IReadOnlyDictionary<string, string> largestValues,
        IReadOnlyList<long> largestRunIds,
        IReadOnlyDictionary<string, BenchmarkComparabilityKeyEntry> taxonomy)
    {
        var differences = new List<BenchmarkComparabilityDifferenceDto>();
        var runIds = state.Runs.Select(r => r.Id).OrderBy(id => id).ToList();

        foreach (string name in mustMatchKeyNames)
        {
            if (!largestValues.TryGetValue(name, out string? largestValue)) continue;
            if (!state.Values.TryGetValue(name, out string? value)) continue;
            if (string.Equals(value, largestValue, StringComparison.Ordinal)) continue;

            taxonomy.TryGetValue(name, out var key);

            var difference = new BenchmarkComparabilityKeyDifference
            {
                Name = name,
                Kind = key?.Kind ?? BenchmarkComparabilityKeyKind.Instrument,
                Variants = new[]
                {
                    new BenchmarkComparabilityKeyVariant { Value = largestValue, RunIds = largestRunIds },
                    new BenchmarkComparabilityKeyVariant { Value = value, RunIds = runIds }
                }
            };

            differences.Add(new BenchmarkComparabilityDifferenceDto
            {
                Name = difference.Name,
                Kind = difference.Kind.ToString(),
                Description = difference.Describe(),
                Variants = difference.Variants.Select(v => new BenchmarkComparabilityVariantDto
                {
                    Value = v.Value,
                    RunIds = v.RunIds.ToList()
                }).ToList()
            });
        }

        return differences;
    }

    /// <summary>
    /// "Condition A" … "Condition Z", then "Condition AA". Bijective base 26, so no two conditions
    /// ever share a label however many the picker offers.
    /// </summary>
    private static string ConditionLabel(int ordinal)
    {
        var sb = new StringBuilder();
        int remaining = ordinal;

        while (remaining > 0)
        {
            int index = (remaining - 1) % 26;
            sb.Insert(0, (char)('A' + index));
            remaining = (remaining - 1) / 26;
        }

        return "Condition " + sb;
    }

    /// <summary>One offered source with its runs already loaded.</summary>
    private sealed class IndexSource
    {
        public string Key { get; init; } = string.Empty;

        public string SourceKind { get; init; } = string.Empty;

        public long SourceId { get; init; }

        public IReadOnlyList<BenchmarkRun> Runs { get; init; } = Array.Empty<BenchmarkRun>();
    }

    /// <summary>One source's comparability, before the conditions are ordered.</summary>
    private sealed class SourceState
    {
        public string Key { get; init; } = string.Empty;

        public string SourceKind { get; init; } = string.Empty;

        public long SourceId { get; init; }

        public IReadOnlyList<BenchmarkRun> Runs { get; init; } = Array.Empty<BenchmarkRun>();

        /// <summary>Position in the offered list, which is what the first-appearance tie-break reads.</summary>
        public int Order { get; init; }

        public string Signature { get; set; } = string.Empty;

        /// <summary>The first run's value for every comparability key.</summary>
        public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);

        public List<string> SelfInconsistentKeys { get; } = new();

        public string Value(string name) => Values.GetValueOrDefault(name, string.Empty);
    }
}
