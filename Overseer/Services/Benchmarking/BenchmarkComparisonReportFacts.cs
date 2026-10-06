namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MobileGnollHackLogger.Data;
using Overseer.Models;

// The fact sheet of a comparison-scope document: every covered model of a comparison described as an
// equal, by its letter, and nothing about the comparison's other entries. Letters go to the covered
// models by Intelligence Index, highest first, ties by entry key (ordinal). Everything is computed
// over the covered models only, from each model's own per-model sheet over the covered models.
//
// Fact keys (sorted by key, ordinal, on the sheet):
//
//   suite.name                                     the suite of a comparison of runs and groups
//   comparison.models                              the covered models
//   comparison.pricingBasis, .pricingBasisKind, .pricedOn, .signature   as on a per-model sheet
//   model.<L>.*                                    every fact a per-model sheet states about its subject,
//                                                  under the model's letter: subject.thinkingLevel, .runs and
//                                                  .state as model.<L>.thinkingLevel, .runs and .state; the
//                                                  subject's name, provider and model id, the suite name, the
//                                                  comparison facts and every fact comparing it with peers
//                                                  are left out
//   model.<L>.quality.rank                         its joint rank among the covered models' Intelligence Indices
//   model.<L>.speed.rank, model.<L>.cost.rank      its rank among the covered models not degraded on the axis
//   model.<L>.frontier                             the Pareto frontiers it lies on
//   frontier.qualityCost, .qualitySpeed, .speedCost   the models on each two-measure Pareto frontier
//   spread.quality, spread.dimension.<d>, spread.speed, spread.cost   lowest and highest, by model
//   questions.anyCriticalError, .wideSpread, .sharedLow   question counts behind the excerpt priority
//   pair.<L>.<M>.intervalOverlap                   every pair: their 95 % intervals overlap
//   pair.<L>.<M>.quality.difference, .quality.interval, .sharedQuestions
//                                                  a tested pair's mean per-question difference, L minus M
//   pair.<L>.<M>.quality.<family>                  established after the family's Holm adjustment (family =
//                                                  reference or allPairs)
//   pair.<L>.<M>.speed.ratio, .cost.ratio          L's model time and spend per question relative to M's
//   pair.<L>.<M>.speed.<family>, .cost.<family>    established after the family's Holm adjustment
//
// L is always the earlier letter of a pair.

/// <summary>One covered model and what its own per-model preparation built.</summary>
public sealed class BenchmarkComparisonReportEntry
{
    public BenchmarkModelComparisonEntryDto Entry { get; init; } = default!;

    /// <summary>The model's per-model sheet over the covered models: its own figures as the subject.</summary>
    public BenchmarkReportFactSheet Sheet { get; init; } = default!;

    /// <summary>The model's own content snapshot, numbered as <see cref="Sheet"/> numbers its questions.</summary>
    public BenchmarkReportContentSnapshot Content { get; init; } = default!;
}

/// <summary>What <see cref="BenchmarkComparisonReportFacts.Build"/> reads.</summary>
public sealed class BenchmarkComparisonReportFactsInput
{
    /// <summary>The whole comparison as computed, every entry included.</summary>
    public BenchmarkModelComparisonDto Comparison { get; init; } = default!;

    /// <summary>The covered models, in any order.</summary>
    public IReadOnlyList<BenchmarkComparisonReportEntry> Entries { get; init; } = Array.Empty<BenchmarkComparisonReportEntry>();

    /// <summary>The paired tests of the covered models against the highest-Index one; null when not computed.</summary>
    public BenchmarkPairedComparisonDto? ReferenceFamily { get; init; }

    /// <summary>The paired tests of every pair of covered models; null when not computed.</summary>
    public BenchmarkPairedComparisonDto? AllPairsFamily { get; init; }

    /// <summary>Why the paired tests could not be computed; null when they were.</summary>
    public string? PairedTestsUnavailableReason { get; init; }

    public int AnswerExcerptChars { get; init; } = BenchmarkReportPackPreparation.DefaultAnswerExcerptChars;

    /// <summary>The numbered comparison, for the subject key; null when it has no number yet.</summary>
    public int? ComparisonId { get; init; }
}

/// <summary>A comparison-scope sheet and its content, or the reason none can be built.</summary>
public sealed class BenchmarkComparisonReportFactsResult
{
    public BenchmarkReportFactSheet? Sheet { get; init; }
    public BenchmarkReportContentSnapshot? Content { get; init; }

    /// <summary>The covered entry set's key, <see cref="BenchmarkReportComparisonKey.ForCoveredSet"/>.</summary>
    public string? CoveredSetKey { get; init; }

    /// <summary>The covered entry keys, sorted as <see cref="BenchmarkReportComparisonKey.CanonicalEntryKeys"/> sorts them.</summary>
    public IReadOnlyList<string> CoveredEntryKeys { get; init; } = Array.Empty<string>();

    /// <summary>What the preparations of the covered models recorded, stored with every document.</summary>
    public IReadOnlyList<BenchmarkReportValidationNote> Notes { get; init; } = Array.Empty<BenchmarkReportValidationNote>();

    public string? Refusal { get; init; }
}

/// <summary>
/// Builds the fact sheet and content of a comparison-scope document over the covered models of a
/// comparison. Pure: no I/O, no clock, no culture.
/// </summary>
public static class BenchmarkComparisonReportFacts
{
    /// <summary>The sheet's <see cref="BenchmarkReportFactSheet.SubjectKind"/>.</summary>
    public const string SubjectKind = "Comparison";

    /// <summary>The fewest covered models a comparison-scope document has.</summary>
    public const int MinEntries = 2;

    /// <summary>The most covered models a comparison-scope document has: MAX_COMPARISON_DOCUMENT_ENTRIES, the charts' MAX_PLOTTED_ENTRIES.</summary>
    public const int MaxEntries = 12;

    /// <summary>The All pairs family is added from three covered models up to this many.</summary>
    public const int AllPairsMaxEntries = 6;

    public const string ReferenceFamilyName = "reference";
    public const string AllPairsFamilyName = "allPairs";

    /// <summary>The subject key prefix of a comparison-scope document.</summary>
    public const string SubjectKeyPrefix = "comparison:";

    public const string TooFewRefusal = "A comparison-wide document covers at least two models: choose two or more.";

    public static readonly string TooManyRefusal =
        $"A comparison-wide document covers at most {MaxEntries.ToString(CultureInfo.InvariantCulture)} models: choose {MaxEntries.ToString(CultureInfo.InvariantCulture)} or fewer.";

    private static readonly string[] Dimensions = { "accuracy", "completeness", "conciseness", "readability" };

    /// <summary>
    /// <c>comparison:12</c> for a set covering every entry that is not Excluded, <c>comparison:12/&lt;first
    /// 16 hex of the covered set's key&gt;</c> for a subset; without a number, <c>comparison</c> and
    /// <c>comparison/&lt;16 hex&gt;</c>.
    /// </summary>
    public static string SubjectKeyOf(int? comparisonId, string coveredSetKey, bool coversAllEntries)
    {
        ArgumentNullException.ThrowIfNull(coveredSetKey);
        string head = comparisonId is int id ? SubjectKeyPrefix + Inv(id) : "comparison";
        return coversAllEntries ? head : head + "/" + coveredSetKey[..Math.Min(16, coveredSetKey.Length)];
    }

    /// <summary>The comparison's entries that are not Excluded, as entry keys in comparison order.</summary>
    public static List<string> DefaultCoveredKeys(BenchmarkModelComparisonDto comparison)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        return comparison.Entries.Where(e => !e.Excluded).Select(e => e.Key).ToList();
    }

    /// <summary>The requested covered keys, trimmed and distinct, or every entry that is not Excluded when none is named.</summary>
    public static List<string> CoveredKeysOf(BenchmarkModelComparisonDto comparison, IEnumerable<string>? requested)
    {
        var keys = (requested ?? Enumerable.Empty<string>())
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => k.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return keys.Count == 0 ? DefaultCoveredKeys(comparison) : keys;
    }

    /// <summary>Why a covered key cannot be covered: it is not an entry of the comparison, or it is Excluded; null when every key can.</summary>
    public static string? CoveredRefusal(BenchmarkModelComparisonDto comparison, IReadOnlyList<string> coveredKeys)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        ArgumentNullException.ThrowIfNull(coveredKeys);

        foreach (string key in coveredKeys)
        {
            var entry = comparison.Entries.FirstOrDefault(e => string.Equals(e.Key, key, StringComparison.Ordinal));
            if (entry == null) return $"'{key}' is not an entry of this comparison.";
            if (entry.Excluded)
            {
                return $"{entry.Label} is excluded from this comparison and cannot be covered by its documents: {entry.Explanation}";
            }
        }
        return null;
    }

    /// <summary><see cref="TooFewRefusal"/> below <see cref="MinEntries"/>, <see cref="TooManyRefusal"/> above <see cref="MaxEntries"/>; null between.</summary>
    public static string? BoundsRefusal(int coveredCount)
        => coveredCount < MinEntries ? TooFewRefusal
            : coveredCount > MaxEntries ? TooManyRefusal
            : null;

    /// <summary>The entries in letter order: Intelligence Index, highest first, an entry without one last, ties by entry key (ordinal).</summary>
    public static List<BenchmarkModelComparisonEntryDto> InLetterOrder(IEnumerable<BenchmarkModelComparisonEntryDto> entries)
        => entries
            .OrderByDescending(e => e.Quality?.PointEstimate ?? double.NegativeInfinity)
            .ThenBy(e => e.Key, StringComparer.Ordinal)
            .ToList();

    /// <summary>A copy of the comparison holding only the entries of <paramref name="keys"/>, so a per-model sheet over it names no other.</summary>
    public static BenchmarkModelComparisonDto Restrict(BenchmarkModelComparisonDto comparison, IReadOnlyCollection<string> keys)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        ArgumentNullException.ThrowIfNull(keys);

        var set = new HashSet<string>(keys, StringComparer.Ordinal);
        var entries = comparison.Entries.Where(e => set.Contains(e.Key)).ToList();
        return new BenchmarkModelComparisonDto
        {
            SubjectKind = comparison.SubjectKind,
            PricingBasis = comparison.PricingBasis,
            PricingBasisLabel = comparison.PricingBasisLabel,
            ComputedAtUtc = comparison.ComputedAtUtc,
            BaselineSuiteId = comparison.BaselineSuiteId,
            BaselineSuiteName = comparison.BaselineSuiteName,
            BaselineBatteryName = comparison.BaselineBatteryName,
            BaselineEntryKeys = comparison.BaselineEntryKeys.Where(set.Contains).ToList(),
            BaselineKeyValues = comparison.BaselineKeyValues,
            BaselineSignature = comparison.BaselineSignature,
            ModelAxisKeys = comparison.ModelAxisKeys,
            Entries = entries,
            ComparableCount = entries.Count(e => !e.Excluded),
            ExcludedCount = entries.Count(e => e.Excluded),
            ThinkingLevelsDiffer = comparison.ThinkingLevelsDiffer,
            SpeedAxisCaveat = comparison.SpeedAxisCaveat,
            Explanation = comparison.Explanation,
            ExcludedMeasures = comparison.ExcludedMeasures,
            PanelDiagnostics = comparison.PanelDiagnostics
        };
    }

    /// <summary>
    /// The sheet and content of a comparison of runs and analysis groups over the covered keys: each
    /// model's per-model sheet over the covered models (<see cref="BenchmarkReportFacts.Build"/>) and
    /// its content (<see cref="BenchmarkReportContent.Build"/>), then <see cref="Build"/>.
    /// </summary>
    public static BenchmarkComparisonReportFactsResult BuildFromRuns(
        BenchmarkModelComparisonDto comparison,
        IReadOnlyList<string> coveredKeys,
        IReadOnlyDictionary<long, BenchmarkRun> runs,
        int answerExcerptChars,
        BenchmarkPairedComparisonDto? referenceFamily = null,
        BenchmarkPairedComparisonDto? allPairsFamily = null,
        string? pairedTestsUnavailableReason = null,
        int? comparisonId = null)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        ArgumentNullException.ThrowIfNull(coveredKeys);
        ArgumentNullException.ThrowIfNull(runs);

        string? refusal = CoveredRefusal(comparison, coveredKeys) ?? BoundsRefusal(coveredKeys.Count);
        if (refusal != null) return new BenchmarkComparisonReportFactsResult { Refusal = refusal };

        var restricted = Restrict(comparison, coveredKeys);
        var entries = new List<BenchmarkComparisonReportEntry>();
        foreach (var entry in restricted.Entries)
        {
            var built = BenchmarkReportFacts.Build(new BenchmarkReportFactsInput { Comparison = restricted, SubjectKey = entry.Key, Runs = runs });
            if (built.Sheet == null) return new BenchmarkComparisonReportFactsResult { Refusal = entry.Label + ": " + built.Refusal };

            var entryRuns = entry.RunIds.Where(runs.ContainsKey).Distinct().OrderBy(id => id).Select(id => runs[id]).ToList();
            entries.Add(new BenchmarkComparisonReportEntry
            {
                Entry = entry,
                Sheet = built.Sheet,
                Content = BenchmarkReportContent.Build(entryRuns, answerExcerptChars)
            });
        }

        return Build(new BenchmarkComparisonReportFactsInput
        {
            Comparison = comparison,
            Entries = entries,
            ReferenceFamily = referenceFamily,
            AllPairsFamily = allPairsFamily,
            PairedTestsUnavailableReason = pairedTestsUnavailableReason,
            AnswerExcerptChars = answerExcerptChars,
            ComparisonId = comparisonId
        });
    }

    /// <summary>
    /// The paired-test families of a comparison of runs and groups over the covered keys, computed in
    /// process by <see cref="BenchmarkPairedTests.Build"/> without price cards, so cost is not tested:
    /// against the highest-Index covered model, and over all pairs from three to
    /// <see cref="AllPairsMaxEntries"/> covered models. Each Holm adjustment counts the covered models' tests only.
    /// </summary>
    public static (BenchmarkPairedComparisonDto? Reference, BenchmarkPairedComparisonDto? AllPairs, string? Unavailable) PairedFromRuns(
        BenchmarkModelComparisonDto comparison, IReadOnlyList<string> coveredKeys, IReadOnlyDictionary<long, BenchmarkRun> runs)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        ArgumentNullException.ThrowIfNull(coveredKeys);
        ArgumentNullException.ThrowIfNull(runs);

        var covered = InLetterOrder(Restrict(comparison, coveredKeys).Entries.Where(e => !e.Excluded));
        if (covered.Count < 2) return (null, null, BenchmarkPairedTests.NeedsTwoEntriesError);

        var entries = new List<BenchmarkPairedEntry>();
        foreach (var entry in comparison.Entries.Where(e => covered.Any(c => c.Key == e.Key)))
        {
            var members = entry.RunIds.Where(runs.ContainsKey).Distinct().Select(id => runs[id]).ToList();
            if (members.Count == 0) return (null, null, $"The runs of {entry.Label} could not be loaded.");
            entries.Add(BenchmarkPairedTests.FromRuns(entry.Key, entry.Label, members));
        }

        var basis = Enum.TryParse<BenchmarkModelComparisonPricingBasis>(comparison.PricingBasis, out var parsed)
            ? parsed
            : BenchmarkModelComparisonPricingBasis.Current;
        string reference = covered[0].Key;
        var computedAt = comparison.ComputedAtUtc;

        var referenceFamily = BenchmarkPairedTests.Build(entries, comparison.SubjectKind, basis, BenchmarkPairedComparisonMode.Reference, reference, computedAt);
        var allPairs = UsesAllPairs(entries.Count)
            ? BenchmarkPairedTests.Build(entries, comparison.SubjectKind, basis, BenchmarkPairedComparisonMode.AllPairs, reference, computedAt)
            : null;
        return (referenceFamily, allPairs, null);
    }

    /// <summary>Whether the All pairs family is added for <paramref name="coveredCount"/> covered models.</summary>
    public static bool UsesAllPairs(int coveredCount) => coveredCount >= 3 && coveredCount <= AllPairsMaxEntries;

    public static BenchmarkComparisonReportFactsResult Build(BenchmarkComparisonReportFactsInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var comparison = input.Comparison ?? throw new ArgumentException("A comparison is required.", nameof(input));
        var supplied = (input.Entries ?? Array.Empty<BenchmarkComparisonReportEntry>()).Where(e => e?.Entry != null && e.Sheet != null).ToList();

        string? refusal = BoundsRefusal(supplied.Count);
        if (refusal != null) return new BenchmarkComparisonReportFactsResult { Refusal = refusal };

        var lettered = InLetterOrder(supplied.Select(e => e.Entry))
            .Select((entry, i) => (Letter: BenchmarkReportFacts.LetterFor(i), Item: supplied.First(s => s.Entry.Key == entry.Key)))
            .ToList();
        var letterOf = lettered.ToDictionary(l => l.Item.Entry.Key, l => l.Letter, StringComparer.Ordinal);

        var coveredKeys = BenchmarkReportComparisonKey.CanonicalEntryKeys(
            IdsOf(lettered.Select(l => l.Item.Entry.Key), "run:"),
            IdsOf(lettered.Select(l => l.Item.Entry.Key), "group:"),
            IdsOf(lettered.Select(l => l.Item.Entry.Key), "battery:"));
        string coveredSetKey = BenchmarkReportComparisonKey.ForCoveredSet(coveredKeys);
        var included = comparison.Entries.Where(e => !e.Excluded).Select(e => e.Key).ToHashSet(StringComparer.Ordinal);
        bool coversAll = included.SetEquals(coveredKeys);
        int entryCount = included.Count;

        var first = lettered[0].Item.Sheet;
        bool batteries = string.Equals(comparison.SubjectKind, BenchmarkModelComparisonSubjectKinds.Batteries, StringComparison.Ordinal);

        var sheet = new BenchmarkReportFactSheet
        {
            Scope = BenchmarkReportFactSheet.ComparisonScopeValue,
            SubjectKey = SubjectKeyOf(input.ComparisonId, coveredSetKey, coversAll),
            SubjectKind = SubjectKind,
            SubjectLabel = SubjectLabelOf(input.ComparisonId, coversAll, lettered.Count, entryCount),
            SubjectDisplayName = string.Empty,
            SubjectProvider = string.Empty,
            SubjectModelId = string.Empty,
            SubjectRunIds = lettered.SelectMany(l => l.Item.Entry.RunIds).Distinct().OrderBy(id => id).ToList(),
            SubjectState = lettered.All(l => string.Equals(l.Item.Entry.State, "Comparable", StringComparison.Ordinal)) ? "Comparable" : "Degraded",
            SubjectExplanation = string.Empty,
            SuiteId = batteries ? null : comparison.BaselineSuiteId ?? first.SuiteId,
            SuiteName = batteries
                ? comparison.BaselineBatteryName ?? first.Battery?.Name ?? first.SuiteName
                : comparison.BaselineSuiteName ?? first.SuiteName,
            Peers = lettered.Select(l => new BenchmarkReportPeer
            {
                Letter = l.Letter,
                EntryKey = l.Item.Entry.Key,
                Label = l.Item.Entry.Label,
                DisplayName = l.Item.Entry.ModelDisplayName,
                Provider = l.Item.Entry.Provider,
                ModelId = l.Item.Entry.ModelId,
                ThinkingLevel = l.Item.Entry.ThinkingLevel,
                RunIds = l.Item.Entry.RunIds.OrderBy(id => id).ToList(),
                State = l.Item.Entry.State,
                SpeedDegraded = l.Item.Entry.SpeedDegraded,
                CostDegraded = l.Item.Entry.CostDegraded,
                Explanation = l.Item.Entry.Explanation
            }).ToList(),
            Models = lettered.Select(l => new BenchmarkReportComparisonModel
            {
                Letter = l.Letter,
                EntryKey = l.Item.Entry.Key,
                Label = l.Item.Entry.Label,
                Provider = l.Item.Entry.Provider
            }).ToList(),
            Graders = Graders(lettered.Select(l => l.Item.Sheet)),
            PurposeStatements = lettered
                .SelectMany(l => l.Item.Sheet.PurposeStatements ?? new List<string>())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.Ordinal)
                .ToList(),
            CoversAllEntries = coversAll,
            ComparisonEntryCount = entryCount,
            PairedTestsUnavailableReason = input.PairedTestsUnavailableReason
        };

        var facts = new BenchmarkReportFacts.FactList();
        AddComparisonFacts(facts, comparison, lettered.Count, batteries ? null : sheet.SuiteName);
        foreach (var (letter, item) in lettered)
        {
            AddModelFacts(facts, letter, item.Sheet);
        }
        AddRankFacts(facts, lettered.Select(l => (l.Letter, l.Item.Entry)).ToList());
        AddFrontierFacts(facts, lettered.Select(l => (l.Letter, l.Item.Entry)).ToList());
        AddSpreadFacts(facts, lettered.Select(l => (l.Letter, l.Item.Entry, l.Item.Sheet)).ToList());
        AddOverlapFacts(facts, lettered.Select(l => (l.Letter, l.Item.Entry)).ToList());

        var families = new List<BenchmarkReportPairedFamily>();
        if (Family(input.ReferenceFamily, "Reference", letterOf) is { } reference) families.Add(reference);
        if (Family(input.AllPairsFamily, "AllPairs", letterOf) is { } allPairs) families.Add(allPairs);
        sheet.PairedTests = families;
        AddPairFacts(facts, families);

        var (questions, maps) = BuildMatrix(lettered.Select(l => (l.Letter, l.Item.Sheet)).ToList());
        AddQuestionFacts(facts, questions);
        sheet.Questions = questions;
        sheet.Facts = facts.Sorted();
        sheet.Entries = BuildEntries(lettered.Select(l => (l.Letter, l.Item.Entry, l.Item.Sheet)).ToList());
        sheet.KnownNames = lettered
            .SelectMany(l => l.Item.Sheet.KnownNames ?? new List<string>())
            .Concat(new[] { sheet.SuiteName })
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
        WriteNamesAsLetters(sheet);

        var content = BenchmarkReportContent.BuildComparison(
            questions,
            lettered.Select(l => new BenchmarkReportContent.ComparisonEntry(l.Letter, l.Item.Entry.Key, l.Item.Content ?? new BenchmarkReportContentSnapshot(), maps[l.Letter])).ToList(),
            input.AnswerExcerptChars);

        var withText = content.Questions?.Select(q => q.Number).ToHashSet() ?? new HashSet<int>();
        foreach (var question in questions)
        {
            question.Detailed = withText.Contains(question.Number);
            question.ExcerptLetters = content.Runs
                .Where(r => r.Questions.Any(q => q.Number == question.Number))
                .Select(r => r.Letter ?? string.Empty)
                .Where(l => l.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(l => l.Length)
                .ThenBy(l => l, StringComparer.Ordinal)
                .ToList();
        }

        return new BenchmarkComparisonReportFactsResult
        {
            Sheet = sheet,
            Content = content,
            CoveredSetKey = coveredSetKey,
            CoveredEntryKeys = coveredKeys
        };
    }

    /// <summary><c>Comparison #12</c>, with <c> · 2 of 5 models</c> for a subset; <c>Comparison</c> without a number.</summary>
    public static string SubjectLabelOf(int? comparisonId, bool coversAllEntries, int covered, int entries)
    {
        string head = comparisonId is int id ? "Comparison #" + Inv(id) : "Comparison";
        return coversAllEntries ? head : head + " · " + Inv(covered) + " of " + Inv(entries) + " models";
    }

    // ---------------------------------------------------------------------------------------------
    // Facts
    // ---------------------------------------------------------------------------------------------

    private static void AddComparisonFacts(BenchmarkReportFacts.FactList facts, BenchmarkModelComparisonDto comparison, int covered, string? suiteName)
    {
        facts.Add("comparison.models", covered, Inv(covered));
        facts.Add("comparison.pricingBasis", comparison.PricingBasisLabel, comparison.PricingBasisLabel);
        BenchmarkReportFacts.AddPricingBasisKind(facts, comparison);
        if (string.IsNullOrWhiteSpace(comparison.BaselineSignature))
        {
            facts.Unavailable("comparison.signature", "The comparison recorded no baseline signature.");
        }
        else
        {
            facts.Add("comparison.signature", comparison.BaselineSignature, comparison.BaselineSignature);
        }
        if (!string.IsNullOrWhiteSpace(suiteName)) facts.Add("suite.name", suiteName, suiteName);
    }

    /// <summary>The prefix of a covered model's own facts: <c>model.A.</c>.</summary>
    public static string ModelPrefix(string letter) => "model." + letter + ".";

    /// <summary>The prefix of a pair's facts: <c>pair.A.C.</c>, the earlier letter first.</summary>
    public static string PairPrefix(string first, string second) => "pair." + first + "." + second + ".";

    /// <summary>Whether a per-model fact describes the subject alone and is copied under its letter.</summary>
    public static bool IsModelFact(string key)
        => !BenchmarkReportFacts.IsPeerFact(key)
           && !key.StartsWith("comparison.", StringComparison.Ordinal)
           && key is not ("subject.label" or "subject.provider" or "subject.modelId" or "suite.name");

    /// <summary>A per-model fact key under a covered model's letter: <c>subject.runs</c> becomes <c>model.A.runs</c>.</summary>
    public static string ModelKey(string letter, string key)
        => ModelPrefix(letter) + (key.StartsWith("subject.", StringComparison.Ordinal) ? key["subject.".Length..] : key);

    private static void AddModelFacts(BenchmarkReportFacts.FactList facts, string letter, BenchmarkReportFactSheet sheet)
    {
        foreach (var fact in sheet.Facts.Where(f => IsModelFact(f.Key)))
        {
            string key = ModelKey(letter, fact.Key);
            if (fact.Available)
            {
                facts.Add(key, fact.Value?.DeepClone(), fact.Display);
            }
            else
            {
                facts.Unavailable(key, fact.UnavailableReason);
            }
        }
    }

    private static void AddRankFacts(BenchmarkReportFacts.FactList facts, IReadOnlyList<(string Letter, BenchmarkModelComparisonEntryDto Entry)> models)
    {
        var joint = BenchmarkReportFacts.JointRanks(models
            .Where(m => m.Entry.Quality != null)
            .Select(m => (m.Entry.Key, m.Entry.Quality!.PointEstimate, m.Entry.Quality.IntervalLower, m.Entry.Quality.IntervalUpper))
            .ToList());
        var speeds = models.Where(m => !m.Entry.SpeedDegraded && m.Entry.Speed?.ModelTimeP50Ms != null).Select(m => m.Entry.Speed!.ModelTimeP50Ms!.Value).ToList();
        var costs = models.Where(m => !m.Entry.CostDegraded && m.Entry.Cost?.CandidateCostPerQuestionUsd != null).Select(m => m.Entry.Cost!.CandidateCostPerQuestionUsd!.Value).ToList();

        foreach (var (letter, entry) in models)
        {
            string prefix = ModelPrefix(letter);
            if (joint.TryGetValue(entry.Key, out var rank))
            {
                facts.Add(prefix + "quality.rank", rank.Rank, BenchmarkReportFacts.JointRankText(rank, joint.Count));
            }
            else
            {
                facts.Unavailable(prefix + "quality.rank", "The comparison computed no quality figure for this model.");
            }

            if (entry.SpeedDegraded) facts.Unavailable(prefix + "speed.rank", entry.Explanation);
            else if (entry.Speed?.ModelTimeP50Ms is double p50)
            {
                int r = BenchmarkReportFacts.RankOf(p50, speeds, higherIsBetter: false);
                facts.Add(prefix + "speed.rank", r, BenchmarkReportFormat.Rank(r, speeds.Count));
            }
            else facts.Unavailable(prefix + "speed.rank", "No model time was recorded.");

            if (entry.CostDegraded) facts.Unavailable(prefix + "cost.rank", entry.Explanation);
            else if (entry.Cost?.CandidateCostPerQuestionUsd is double cost)
            {
                int r = BenchmarkReportFacts.RankOf(cost, costs, higherIsBetter: false);
                facts.Add(prefix + "cost.rank", r, BenchmarkReportFormat.Rank(r, costs.Count));
            }
            else facts.Unavailable(prefix + "cost.rank", "No price card was resolved for every run behind this model.");
        }
    }

    /// <summary>One two-measure Pareto frontier: its key, its words and how each model reads on it.</summary>
    private sealed record FrontierAxis(string Key, string Name, Func<BenchmarkModelComparisonEntryDto, double?> First, bool FirstHigher, Func<BenchmarkModelComparisonEntryDto, double?> Second, bool SecondHigher);

    private static readonly FrontierAxis[] Frontiers =
    {
        new("frontier.qualityCost", "intelligence against cost", Quality, true, Cost, false),
        new("frontier.qualitySpeed", "intelligence against speed", Quality, true, Speed, false),
        new("frontier.speedCost", "speed against cost", Speed, false, Cost, false)
    };

    private static double? Quality(BenchmarkModelComparisonEntryDto e) => e.Quality?.PointEstimate;
    private static double? Speed(BenchmarkModelComparisonEntryDto e) => e.SpeedDegraded ? null : e.Speed?.ModelTimeP50Ms;
    private static double? Cost(BenchmarkModelComparisonEntryDto e) => e.CostDegraded ? null : e.Cost?.CandidateCostPerQuestionUsd;

    /// <summary>
    /// The letters of the models on a Pareto frontier: no other model is at least as good on both
    /// measures and better on one. Models without both measures take no part; null with fewer than two.
    /// </summary>
    public static IReadOnlyList<string>? FrontierLetters(
        IReadOnlyList<(string Letter, BenchmarkModelComparisonEntryDto Entry)> models,
        Func<BenchmarkModelComparisonEntryDto, double?> first, bool firstHigher,
        Func<BenchmarkModelComparisonEntryDto, double?> second, bool secondHigher)
    {
        var points = models
            .Where(m => first(m.Entry).HasValue && second(m.Entry).HasValue)
            .Select(m => (m.Letter, A: (firstHigher ? 1 : -1) * first(m.Entry)!.Value, B: (secondHigher ? 1 : -1) * second(m.Entry)!.Value))
            .ToList();
        if (points.Count < 2) return null;

        return points
            .Where(p => !points.Any(o => o.Letter != p.Letter && o.A >= p.A && o.B >= p.B && (o.A > p.A || o.B > p.B)))
            .Select(p => p.Letter)
            .ToList();
    }

    private static void AddFrontierFacts(BenchmarkReportFacts.FactList facts, IReadOnlyList<(string Letter, BenchmarkModelComparisonEntryDto Entry)> models)
    {
        var onFrontiers = models.ToDictionary(m => m.Letter, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var axis in Frontiers)
        {
            var letters = FrontierLetters(models, axis.First, axis.FirstHigher, axis.Second, axis.SecondHigher);
            if (letters == null)
            {
                facts.Unavailable(axis.Key, "Fewer than two models have both figures.");
                continue;
            }

            facts.Add(axis.Key, letters.Count, ModelsText(letters));
            foreach (string letter in letters) onFrontiers[letter].Add(axis.Name);
        }

        foreach (var (letter, _) in models)
        {
            var names = onFrontiers[letter];
            facts.Add(ModelPrefix(letter) + "frontier", names.Count > 0,
                names.Count == 0 ? "on none of the frontiers" : "on the " + BenchmarkReportFormat.LetterList(names) + (names.Count == 1 ? " frontier" : " frontiers"));
        }
    }

    private static void AddSpreadFacts(
        BenchmarkReportFacts.FactList facts,
        IReadOnlyList<(string Letter, BenchmarkModelComparisonEntryDto Entry, BenchmarkReportFactSheet Sheet)> models)
    {
        AddSpread(facts, "spread.quality", models.Select(m => (m.Letter, Quality(m.Entry))).ToList(),
            BenchmarkReportFormat.Whole, higherIsBetter: true, unit: " points");

        foreach (string dimension in Dimensions)
        {
            AddSpread(facts, "spread.dimension." + dimension,
                models.Select(m => (m.Letter, NumberOf(m.Sheet, "dimension." + dimension))).ToList(),
                BenchmarkReportFormat.Whole, higherIsBetter: true, unit: " points");
        }

        AddSpread(facts, "spread.speed", models.Select(m => (m.Letter, Speed(m.Entry))).ToList(),
            BenchmarkReportFormat.Seconds, higherIsBetter: false, unit: null);
        AddSpread(facts, "spread.cost", models.Select(m => (m.Letter, Cost(m.Entry))).ToList(),
            BenchmarkReportFormat.Usd, higherIsBetter: false, unit: null);
    }

    /// <summary>
    /// The spread fact: its value is the range, its display <see cref="SpreadDisplay"/>. Unavailable
    /// with fewer than two values.
    /// </summary>
    private static void AddSpread(
        BenchmarkReportFacts.FactList facts, string key, IReadOnlyList<(string Letter, double? Value)> values,
        Func<double, string> format, bool higherIsBetter, string? unit)
    {
        var present = values.Where(v => v.Value.HasValue).Select(v => v.Value!.Value).ToList();
        if (present.Count < 2)
        {
            facts.Unavailable(key, "Fewer than two models have this figure.");
            return;
        }

        facts.Add(key, present.Max() - present.Min(), SpreadDisplay(values, format, higherIsBetter, unit)!);
    }

    /// <summary>
    /// <c>from 62 (Model D) to 85 (Model A), 23 points apart</c>: the lowest and highest value, worst
    /// first, <c>1 point apart</c> for a range of one; <c>97 for every model</c> when both display
    /// alike. Null with fewer than two values.
    /// </summary>
    internal static string? SpreadDisplay(
        IReadOnlyList<(string Letter, double? Value)> values, Func<double, string> format, bool higherIsBetter, string? unit)
    {
        var present = values.Where(v => v.Value.HasValue).Select(v => (v.Letter, Value: v.Value!.Value)).ToList();
        if (present.Count < 2) return null;

        var low = present.OrderBy(v => v.Value).ThenBy(v => v.Letter.Length).ThenBy(v => v.Letter, StringComparer.Ordinal).First();
        var high = present.OrderByDescending(v => v.Value).ThenBy(v => v.Letter.Length).ThenBy(v => v.Letter, StringComparer.Ordinal).First();
        var (worst, best) = higherIsBetter ? (low, high) : (high, low);

        string display;
        if (string.Equals(format(worst.Value), format(best.Value), StringComparison.Ordinal))
        {
            display = format(best.Value) + (present.Count == values.Count ? " for every model" : " for every model with this figure");
        }
        else
        {
            display = "from " + format(worst.Value) + " (Model " + worst.Letter + ") to " + format(best.Value) + " (Model " + best.Letter + ")";
            if (unit != null)
            {
                string apart = BenchmarkReportFormat.WholeDifference(high.Value, low.Value).TrimStart('+');
                display += ", " + apart + (apart == "1" ? unit.TrimEnd('s') : unit) + " apart";
            }
        }
        return display;
    }

    private static void AddOverlapFacts(BenchmarkReportFacts.FactList facts, IReadOnlyList<(string Letter, BenchmarkModelComparisonEntryDto Entry)> models)
    {
        for (int i = 0; i < models.Count; i++)
        {
            for (int j = i + 1; j < models.Count; j++)
            {
                var (a, first) = models[i];
                var (b, second) = models[j];
                string key = PairPrefix(a, b) + "intervalOverlap";
                if (first.Quality?.IntervalLower is not double aLower || first.Quality?.IntervalUpper is not double aUpper
                    || second.Quality?.IntervalLower is not double bLower || second.Quality?.IntervalUpper is not double bUpper)
                {
                    facts.Unavailable(key, "A quality interval could not be computed.");
                    continue;
                }

                bool overlaps = aLower <= bUpper && bLower <= aUpper;
                facts.Add(key, overlaps, overlaps
                    ? "the 95 % intervals of Model " + a + " and Model " + b + " overlap"
                    : "the 95 % intervals of Model " + a + " and Model " + b + " do not overlap");
            }
        }
    }

    private static void AddQuestionFacts(BenchmarkReportFacts.FactList facts, IReadOnlyList<BenchmarkReportQuestion> questions)
    {
        int total = questions.Count;
        int critical = questions.Count(q => (q.Models ?? new List<BenchmarkReportQuestionModelScore>()).Any(c => c.CriticalError));
        int wide = questions.Count(q => q.PeerMin.HasValue && q.PeerMax.HasValue && q.PeerCount >= 2
                                        && q.PeerMax.Value - q.PeerMin.Value >= BenchmarkReportContent.WideSpreadPoints);
        int low = questions.Count(q => q.PeerMax is double max && max < BenchmarkReportContent.SharedLowScore);

        string Of(int n) => Inv(n) + " of " + Inv(total) + (total == 1 ? " question" : " questions");
        facts.Add("questions.anyCriticalError", critical, Of(critical));
        facts.Add("questions.wideSpread", wide, Of(wide));
        facts.Add("questions.sharedLow", low, Of(low));
    }

    // ---------------------------------------------------------------------------------------------
    // Paired tests
    // ---------------------------------------------------------------------------------------------

    private static readonly string[] PairedMeasures =
    {
        BenchmarkPairedTests.IntelligenceMeasure, BenchmarkPairedTests.SpeedMeasure, BenchmarkPairedTests.CostMeasure
    };

    /// <summary>
    /// A paired-test response as a family of the sheet: Intelligence, Speed and Cost, each pair
    /// oriented so the earlier letter comes first, its effect and interval turned to match.
    /// </summary>
    private static BenchmarkReportPairedFamily? Family(BenchmarkPairedComparisonDto? dto, string mode, IReadOnlyDictionary<string, string> letterOf)
    {
        if (dto == null) return null;

        var family = new BenchmarkReportPairedFamily
        {
            Mode = mode,
            ReferenceLetter = dto.ReferenceKey != null && letterOf.TryGetValue(dto.ReferenceKey, out var r) ? r : null,
            SingleRunCaveat = dto.SingleRunCaveat
        };

        foreach (string name in PairedMeasures)
        {
            var measure = dto.Measures.FirstOrDefault(m => string.Equals(m.Measure, name, StringComparison.Ordinal));
            if (measure == null) continue;

            var pairs = new List<BenchmarkReportPairedTest>();
            foreach (var test in measure.Pairs)
            {
                if (!letterOf.TryGetValue(test.BaselineKey, out var baseline) || !letterOf.TryGetValue(test.TreatmentKey, out var treatment)) continue;
                pairs.Add(Oriented(test, baseline, treatment));
            }

            family.Measures.Add(new BenchmarkReportPairedMeasure
            {
                Measure = name,
                FamilySize = measure.FamilySize,
                Adjustment = measure.Adjustment,
                AdjustmentNote = measure.AdjustmentNote,
                NotTestedReason = measure.NotTestedReason,
                Pairs = pairs
                    .OrderBy(p => p.FirstLetter.Length).ThenBy(p => p.FirstLetter, StringComparer.Ordinal)
                    .ThenBy(p => p.SecondLetter.Length).ThenBy(p => p.SecondLetter, StringComparer.Ordinal)
                    .ToList()
            });
        }

        return family;
    }

    /// <summary>Letter order before ordinal: <c>Z</c> before <c>AA</c>.</summary>
    private static bool Precedes(string a, string b)
        => a.Length != b.Length ? a.Length < b.Length : string.CompareOrdinal(a, b) < 0;

    /// <summary>
    /// The test with the earlier letter first. The response's effect is treatment against baseline
    /// (B − A, or B ÷ A); when the baseline has the earlier letter the difference is negated and the
    /// ratio inverted, each interval with it.
    /// </summary>
    private static BenchmarkReportPairedTest Oriented(BenchmarkPairedTestDto test, string baseline, string treatment)
    {
        bool baselineFirst = Precedes(baseline, treatment);
        bool ratio = string.Equals(test.EffectKind, BenchmarkPairedTests.EffectRatio, StringComparison.Ordinal);

        double? effect = test.Effect;
        double? lower = test.EffectLower;
        double? upper = test.EffectUpper;
        if (baselineFirst)
        {
            if (ratio)
            {
                effect = effect is double e && e != 0 ? 1 / e : null;
                (lower, upper) = (upper is double u && u != 0 ? (double?)(1 / u) : null, lower is double l && l != 0 ? (double?)(1 / l) : null);
            }
            else
            {
                effect = -effect;
                (lower, upper) = (-upper, -lower);
            }
        }

        string favors = "None";
        if (test.Established && effect is double value)
        {
            // A higher Intelligence Index is better; a smaller time or spend is better.
            bool firstBetter = ratio ? value < 1 : value > 0;
            bool secondBetter = ratio ? value > 1 : value < 0;
            favors = firstBetter ? "First" : secondBetter ? "Second" : "None";
        }

        return new BenchmarkReportPairedTest
        {
            FirstLetter = baselineFirst ? baseline : treatment,
            SecondLetter = baselineFirst ? treatment : baseline,
            PairedItems = test.PairedItems,
            EffectKind = test.EffectKind,
            Effect = effect,
            Lower = lower,
            Upper = upper,
            PValue = test.PValue,
            AdjustedPValue = test.AdjustedPValue,
            Established = test.Established,
            Favors = favors,
            NotTestedReason = test.NotTestedReason
        };
    }

    private static void AddPairFacts(BenchmarkReportFacts.FactList facts, IReadOnlyList<BenchmarkReportPairedFamily> families)
    {
        var effects = new HashSet<string>(StringComparer.Ordinal);
        foreach (var family in families)
        {
            string familyName = family.Mode == "AllPairs" ? AllPairsFamilyName : ReferenceFamilyName;
            foreach (var measure in family.Measures)
            {
                string axis = measure.Measure switch
                {
                    BenchmarkPairedTests.SpeedMeasure => "speed",
                    BenchmarkPairedTests.CostMeasure => "cost",
                    _ => "quality"
                };

                foreach (var pair in measure.Pairs)
                {
                    string prefix = PairPrefix(pair.FirstLetter, pair.SecondLetter);
                    if (effects.Add(prefix + axis)) AddEffectFacts(facts, prefix, axis, pair);
                    AddVerdictFact(facts, prefix + axis + "." + familyName, axis, measure, pair);
                }
            }
        }
    }

    /// <summary>The pair's effect and interval on one axis, family-independent; Intelligence also gives the shared questions.</summary>
    private static void AddEffectFacts(BenchmarkReportFacts.FactList facts, string prefix, string axis, BenchmarkReportPairedTest pair)
    {
        string a = "Model " + pair.FirstLetter;
        string b = "Model " + pair.SecondLetter;
        string reason = pair.NotTestedReason ?? "The pair was not tested.";
        bool tested = pair.NotTestedReason == null && pair.Effect.HasValue;

        if (axis == "quality")
        {
            if (!tested)
            {
                facts.Unavailable(prefix + "quality.difference", reason);
                facts.Unavailable(prefix + "quality.interval", reason);
                facts.Unavailable(prefix + "sharedQuestions", reason);
                return;
            }

            facts.Add(prefix + "quality.difference", pair.Effect!.Value,
                BenchmarkReportFormat.SignedOneDecimal(pair.Effect.Value) + " points (" + a + " minus " + b + ")");
            if (pair.Lower is double lower && pair.Upper is double upper)
            {
                facts.Text(prefix + "quality.interval", BenchmarkReportFormat.SignedOneDecimal(lower) + " to " + BenchmarkReportFormat.SignedOneDecimal(upper));
            }
            else
            {
                facts.Unavailable(prefix + "quality.interval", "No interval could be computed.");
            }
            facts.Add(prefix + "sharedQuestions", pair.PairedItems, Inv(pair.PairedItems) + (pair.PairedItems == 1 ? " question" : " questions"));
            return;
        }

        string key = prefix + axis + ".ratio";
        if (!tested)
        {
            facts.Unavailable(key, reason);
            return;
        }

        string what = axis == "speed" ? " took " + Ratio(pair.Effect!.Value) + " times as long as " : " cost " + Ratio(pair.Effect!.Value) + " times as much as ";
        string interval = pair.Lower is double l && pair.Upper is double u ? " (" + Ratio(l) + " to " + Ratio(u) + ")" : string.Empty;
        facts.Add(key, pair.Effect.Value, a + what + b + " on the same questions" + interval);
    }

    /// <summary>The pair's result in one family: established or not after the family's adjustment, with the adjusted p-value.</summary>
    private static void AddVerdictFact(
        BenchmarkReportFacts.FactList facts, string key, string axis, BenchmarkReportPairedMeasure measure, BenchmarkReportPairedTest pair)
    {
        if (pair.NotTestedReason != null || pair.AdjustedPValue == null)
        {
            facts.Unavailable(key, pair.NotTestedReason ?? measure.NotTestedReason ?? "The pair was not tested.");
            return;
        }

        string adjustment = measure.FamilySize > 1
            ? "after the Holm adjustment across " + Inv(measure.FamilySize) + " tests"
            : "as a single test";
        string p = "(adjusted p " + PValue(pair.AdjustedPValue.Value) + ")";

        string winner = pair.Favors == "First" ? "Model " + pair.FirstLetter : "Model " + pair.SecondLetter;
        string result = axis switch
        {
            "speed" => winner + " was faster on the same questions",
            "cost" => winner + " was cheaper on the same questions",
            _ => winner + " scored higher on the same questions"
        };

        string display = pair.Established && pair.Favors != "None"
            ? result + ", established " + adjustment + " " + p
            : "no difference established " + adjustment + " " + p;
        facts.Add(key, pair.Established, display);
    }

    // ---------------------------------------------------------------------------------------------
    // Questions, entries, graders
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The per-question matrix: every question any covered model was asked, in the first model's
    /// order, then the questions only later models were asked; a battery question is matched across
    /// models by its suite-qualified reference, any other by question and item revision. Each model's
    /// own question numbers are mapped to the matrix's.
    /// </summary>
    private static (List<BenchmarkReportQuestion> Questions, Dictionary<string, IReadOnlyDictionary<int, int>> Maps) BuildMatrix(
        IReadOnlyList<(string Letter, BenchmarkReportFactSheet Sheet)> models)
    {
        var rows = new List<(string Key, BenchmarkReportQuestion Template, List<(string Letter, BenchmarkReportQuestion Question)> Cells)>();
        var indexByKey = new Dictionary<string, int>(StringComparer.Ordinal);
        var maps = new Dictionary<string, IReadOnlyDictionary<int, int>>(StringComparer.Ordinal);

        foreach (var (letter, sheet) in models)
        {
            var map = new Dictionary<int, int>();
            foreach (var q in sheet.Questions.OrderBy(q => q.Number))
            {
                string key = !string.IsNullOrWhiteSpace(q.Reference)
                    ? "ref:" + q.Reference
                    : "q:" + q.QuestionKey + "@" + (q.ItemRevisionUsed?.ToString(CultureInfo.InvariantCulture) ?? "?");
                if (!indexByKey.TryGetValue(key, out int index))
                {
                    index = rows.Count;
                    indexByKey[key] = index;
                    rows.Add((key, q, new List<(string, BenchmarkReportQuestion)>()));
                }
                if (rows[index].Cells.All(c => c.Letter != letter)) rows[index].Cells.Add((letter, q));
                map.TryAdd(q.Number, index + 1);
            }
            maps[letter] = map;
        }

        var questions = new List<BenchmarkReportQuestion>();
        for (int i = 0; i < rows.Count; i++)
        {
            var (_, template, cells) = rows[i];
            var scored = cells.Select(c => new BenchmarkReportQuestionModelScore
            {
                Letter = c.Letter,
                Score = c.Question.Score,
                CriticalError = c.Question.CriticalError,
                CriticalErrorCount = c.Question.CriticalErrorCount,
                RefutedClaims = c.Question.RefutedClaims,
                RefutedAnswerSentences = c.Question.RefutedAnswerSentences,
                ToolCalls = c.Question.ToolCalls,
                ModelTimeMs = c.Question.ModelTimeMs,
                RunCount = c.Question.RunCount
            }).ToList();
            var scores = scored.Where(c => c.Score.HasValue).Select(c => c.Score!.Value).ToList();
            var times = scored.Where(c => c.ModelTimeMs.HasValue).Select(c => c.ModelTimeMs!.Value).ToList();

            questions.Add(new BenchmarkReportQuestion
            {
                Number = i + 1,
                QuestionKey = template.QuestionKey,
                ItemRevisionUsed = template.ItemRevisionUsed,
                OrderIndex = template.OrderIndex,
                Band = template.Band,
                AuthoredBand = template.AuthoredBand,
                Score = scores.Count > 0 ? scores.Average() : null,
                PeerMin = scores.Count > 0 ? scores.Min() : null,
                PeerMax = scores.Count > 0 ? scores.Max() : null,
                PeerCount = scores.Count,
                CriticalError = scored.Any(c => c.CriticalError),
                CriticalErrorCount = scored.Any(c => c.CriticalErrorCount.HasValue) ? scored.Sum(c => c.CriticalErrorCount ?? (c.CriticalError ? 1 : 0)) : null,
                RefutedClaims = scored.Sum(c => c.RefutedClaims),
                RefutedAnswerSentences = scored.All(c => c.RefutedAnswerSentences.HasValue) ? scored.Sum(c => c.RefutedAnswerSentences!.Value) : null,
                ToolCalls = scored.Count > 0 ? scored.Average(c => c.ToolCalls) : 0,
                ModelTimeMs = times.Count > 0 ? times.Average() : null,
                RunCount = scored.Sum(c => c.RunCount),
                Reference = template.Reference,
                Suite = template.Suite,
                Models = scored
            });
        }

        return (questions, maps);
    }

    /// <summary>Each covered model's figures, from its own sheet's subject row, lettered and ranked among the covered models.</summary>
    private static List<BenchmarkReportEntryFigures> BuildEntries(
        IReadOnlyList<(string Letter, BenchmarkModelComparisonEntryDto Entry, BenchmarkReportFactSheet Sheet)> models)
    {
        var joint = BenchmarkReportFacts.JointRanks(models
            .Where(m => m.Entry.Quality != null)
            .Select(m => (m.Entry.Key, m.Entry.Quality!.PointEstimate, m.Entry.Quality.IntervalLower, m.Entry.Quality.IntervalUpper))
            .ToList());
        var speeds = models.Select(m => Speed(m.Entry)).Where(v => v.HasValue).Select(v => v!.Value).ToList();
        var costs = models.Select(m => Cost(m.Entry)).Where(v => v.HasValue).Select(v => v!.Value).ToList();

        return models.Select(m =>
        {
            var own = m.Sheet.Entries.FirstOrDefault(e => e.IsSubject) ?? new BenchmarkReportEntryFigures { EntryKey = m.Entry.Key };
            double? speed = Speed(m.Entry);
            double? cost = Cost(m.Entry);
            return new BenchmarkReportEntryFigures
            {
                EntryKey = m.Entry.Key,
                PeerLetter = m.Letter,
                IsSubject = false,
                QualityIndex = m.Entry.Quality?.PointEstimate,
                QualityLower = m.Entry.Quality?.IntervalLower,
                QualityUpper = m.Entry.Quality?.IntervalUpper,
                QualityRank = joint.TryGetValue(m.Entry.Key, out var rank) ? rank.Rank : null,
                ModelTimeP50Ms = speed,
                SpeedRank = speed.HasValue ? BenchmarkReportFacts.RankOf(speed.Value, speeds, higherIsBetter: false) : null,
                SpeedDegraded = m.Entry.SpeedDegraded,
                CostPerQuestionUsd = cost,
                CostRank = cost.HasValue ? BenchmarkReportFacts.RankOf(cost.Value, costs, higherIsBetter: false) : null,
                CostDegraded = m.Entry.CostDegraded,
                RunCount = m.Entry.RunCount,
                HarnessVersion = own.HarnessVersion,
                FirstRunUtc = own.FirstRunUtc,
                LastRunUtc = own.LastRunUtc,
                Extra = (own.Extra ?? new List<BenchmarkReportFact>()).Select(f => new BenchmarkReportFact
                {
                    Key = f.Key,
                    Value = f.Value?.DeepClone(),
                    Display = f.Display,
                    Available = f.Available,
                    UnavailableReason = f.UnavailableReason
                }).ToList()
            };
        }).ToList();
    }

    /// <summary>
    /// Every grading role of the covered models' runs, once per configuration, in role order. A
    /// grader's provider relation is stated per model by the prompt and the renderer, so it is false here.
    /// </summary>
    private static List<BenchmarkReportGrader> Graders(IEnumerable<BenchmarkReportFactSheet> sheets)
    {
        var graders = new List<BenchmarkReportGrader>();
        foreach (var grader in sheets.SelectMany(s => s.Graders))
        {
            if (graders.Any(g => g.Role == grader.Role && g.Provider == grader.Provider && g.ModelId == grader.ModelId && g.ThinkingLevel == grader.ThinkingLevel))
            {
                continue;
            }
            graders.Add(new BenchmarkReportGrader
            {
                Role = grader.Role,
                Label = grader.Label,
                Provider = grader.Provider,
                ModelId = grader.ModelId,
                ThinkingLevel = grader.ThinkingLevel,
                SameFamilyAsSubject = false
            });
        }
        return graders;
    }

    // ---------------------------------------------------------------------------------------------
    // Names as letters
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The sheet's free text with each covered model's names written as <c>Model X</c>: every fact's
    /// display and unavailable reason, each entry's extra facts, each model's explanation, the purpose
    /// statements, and the paired tests' reasons, notes and caveats. The writer reads letters only; a
    /// named copy names the models again as it prints the text.
    /// </summary>
    private static void WriteNamesAsLetters(BenchmarkReportFactSheet sheet)
    {
        var names = NamesOf(sheet.Peers);
        if (names.Count == 0) return;

        void Facts(IEnumerable<BenchmarkReportFact>? facts)
        {
            foreach (var fact in facts ?? Enumerable.Empty<BenchmarkReportFact>())
            {
                fact.Display = Lettered(fact.Display, names) ?? string.Empty;
                fact.UnavailableReason = Lettered(fact.UnavailableReason, names);
            }
        }

        Facts(sheet.Facts);
        foreach (var entry in sheet.Entries) Facts(entry.Extra);
        foreach (var peer in sheet.Peers) peer.Explanation = Lettered(peer.Explanation, names) ?? string.Empty;
        sheet.PurposeStatements = (sheet.PurposeStatements ?? new List<string>()).Select(p => Lettered(p, names) ?? string.Empty).ToList();
        sheet.PairedTestsUnavailableReason = Lettered(sheet.PairedTestsUnavailableReason, names);

        foreach (var family in sheet.PairedTests ?? new List<BenchmarkReportPairedFamily>())
        {
            family.SingleRunCaveat = Lettered(family.SingleRunCaveat, names);
            foreach (var measure in family.Measures)
            {
                measure.AdjustmentNote = Lettered(measure.AdjustmentNote, names) ?? string.Empty;
                measure.NotTestedReason = Lettered(measure.NotTestedReason, names);
                foreach (var pair in measure.Pairs) pair.NotTestedReason = Lettered(pair.NotTestedReason, names);
            }
        }
    }

    /// <summary>
    /// Each covered model's label, display name and model id with the letters it names, longest name
    /// first: one letter, or several where models share a name, as one model at two thinking levels does.
    /// A name shorter than two characters is left out.
    /// </summary>
    public static IReadOnlyList<(string Name, IReadOnlyList<string> Letters)> NamesOf(IEnumerable<BenchmarkReportPeer> models)
    {
        ArgumentNullException.ThrowIfNull(models);
        var letters = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var model in models)
        {
            foreach (string? name in new[] { model.Label, model.DisplayName, model.ModelId })
            {
                string trimmed = name?.Trim() ?? string.Empty;
                if (trimmed.Length < 2) continue;
                if (!letters.TryGetValue(trimmed, out var list)) letters[trimmed] = list = new List<string>();
                if (!list.Contains(model.Letter, StringComparer.Ordinal)) list.Add(model.Letter);
            }
        }

        return letters
            .OrderByDescending(l => l.Key.Length)
            .ThenBy(l => l.Key, StringComparer.Ordinal)
            .Select(l => (l.Key, (IReadOnlyList<string>)l.Value
                .OrderBy(x => x.Length).ThenBy(x => x, StringComparer.Ordinal).ToList()))
            .ToList();
    }

    /// <summary>
    /// <paramref name="text"/> with each of <paramref name="names"/> written as <see cref="ModelsText"/>
    /// of its letters, as <see cref="Replaced"/> replaces them.
    /// </summary>
    public static string? Lettered(string? text, IReadOnlyList<(string Name, IReadOnlyList<string> Letters)> names)
        => Replaced(text, names.Select(n => (n.Name, ModelsText(n.Letters))));

    /// <summary>
    /// <paramref name="text"/> with each name replaced by its replacement in one pass, so no replacement
    /// is read again as a name: whole names only (no letter or digit on either side), case-insensitive,
    /// the earlier name first where two match at one place.
    /// </summary>
    public static string? Replaced(string? text, IEnumerable<(string Name, string Replacement)> replacements)
    {
        if (string.IsNullOrEmpty(text)) return text;

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();
        foreach (var (name, replacement) in replacements)
        {
            string trimmed = name?.Trim() ?? string.Empty;
            if (trimmed.Length == 0 || map.ContainsKey(trimmed)) continue;
            map[trimmed] = replacement;
            order.Add(trimmed);
        }
        if (order.Count == 0) return text;

        string pattern = @"(?<![\p{L}\p{N}])(?:" + string.Join("|", order.Select(Regex.Escape)) + @")(?![\p{L}\p{N}])";
        return Regex.Replace(text, pattern, match => map[match.Value], RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    // ---------------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------------

    /// <summary><c>Model A</c>, <c>Models A and C</c>, <c>Models A, B and D</c>; <c>none</c> for no letter.</summary>
    public static string ModelsText(IReadOnlyList<string> letters)
    {
        ArgumentNullException.ThrowIfNull(letters);
        return letters.Count switch
        {
            0 => "none",
            1 => "Model " + letters[0],
            _ => "Models " + BenchmarkReportFormat.LetterList(letters)
        };
    }

    /// <summary>A p-value with three decimals, <c>&lt; 0.001</c> below that.</summary>
    public static string PValue(double p)
        => p < 0.001 ? "< 0.001" : Math.Round(p, 3, MidpointRounding.AwayFromZero).ToString("0.000", CultureInfo.InvariantCulture);

    /// <summary>A ratio with two decimals.</summary>
    private static string Ratio(double value)
        => Math.Round(value, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture);

    private static double? NumberOf(BenchmarkReportFactSheet sheet, string key)
    {
        var fact = sheet.Facts.FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.Ordinal));
        return fact is { Available: true, Value: JsonValue value } && value.TryGetValue(out double number) ? number : null;
    }

    private static List<long> IdsOf(IEnumerable<string> keys, string prefix)
        => keys
            .Where(k => k.StartsWith(prefix, StringComparison.Ordinal))
            .Select(k => long.TryParse(k.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out long id) ? id : 0)
            .Where(id => id > 0)
            .ToList();

    private static string Inv(int value) => value.ToString(CultureInfo.InvariantCulture);
}
