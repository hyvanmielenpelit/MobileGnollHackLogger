namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MobileGnollHackLogger.Data;

/// <summary>
/// How comparable a set of runs is, and therefore what may be computed over it.
///
/// The ladder is ordered: a higher value permits everything a lower one does. Only
/// <see cref="Replicate"/> and <see cref="QualityComparable"/> may be pooled into one index —
/// see <see cref="BenchmarkComparabilityKey.IsPoolable"/>.
/// </summary>
public enum BenchmarkComparabilityTier
{
    /// <summary>
    /// Below Tier B. The runs measure different things and no aggregate over them means anything.
    /// A group at this tier must not be persisted; the differing keys say why.
    /// </summary>
    NotComparable = 0,

    /// <summary>
    /// Tier C — cross-condition. The candidate specification is identical and exactly one
    /// instrument key was deliberately moved (the T15 case: <c>ToolGuidesSha256</c>).
    ///
    /// **Never pooled into one index.** Such a set is really two groups, and the tool's job is to
    /// *compare* them. Pooling would average a before and an after into a number that describes
    /// neither.
    /// </summary>
    CrossCondition = 1,

    /// <summary>
    /// Tier B — quality-comparable. Tier A relaxed on the keys that affect speed and cost only:
    /// question parallelism and the pricing snapshot. Quality aggregates are valid; speed and cost
    /// aggregates carry a degraded flag.
    /// </summary>
    QualityComparable = 2,

    /// <summary>
    /// Tier A — replicate. Every key matches. This is the only tier at which a pooled multi-run
    /// index, its reproducibility component, and its speed and cost aggregates are all sound.
    /// </summary>
    Replicate = 3
}

/// <summary>
/// What a comparability key governs, which is what decides how a difference on it is treated.
/// </summary>
public enum BenchmarkComparabilityKeyKind
{
    /// <summary>
    /// Suite identity and the answer key. A difference here means the runs answered different
    /// questions, or the same questions against a different rubric, so nothing can be paired by
    /// question. Always <see cref="BenchmarkComparabilityTier.NotComparable"/> — never Tier C,
    /// because there is no cross-condition reading of "a different exam".
    /// </summary>
    Fundamental = 0,

    /// <summary>
    /// The candidate specification. Tier C is defined as *candidate identical*, so any difference
    /// here drops the set below Tier B. Two models are compared as two Tier A groups through
    /// <see cref="BenchmarkGroupStatistics.Compare"/>, never by putting both in one group.
    /// </summary>
    Candidate = 1,

    /// <summary>
    /// The instrument: prompts, guides, knowledge base, harness, grading regime and budgets.
    /// Exactly one differing instrument key is Tier C — the deliberate single-variable
    /// experiment. Two or more is an uncontrolled comparison and drops below Tier B.
    /// </summary>
    Instrument = 2,

    /// <summary>
    /// Keys that affect only how fast and how expensive a run was, never what it scored. A
    /// difference here degrades the set to Tier B and flags the affected aggregates.
    /// </summary>
    SpeedAndCost = 3
}

/// <summary>One comparability key of one run: its name, what it governs, and its value.</summary>
public sealed record BenchmarkComparabilityKeyEntry
{
    public string Name { get; init; } = string.Empty;

    public BenchmarkComparabilityKeyKind Kind { get; init; }

    /// <summary>
    /// The canonical rendering compared for equality. Never null — an absent value renders as
    /// <c>(none)</c>, so "the column was null on both runs" compares equal while staying legible
    /// in a diagnostics dump.
    /// </summary>
    public string Value { get; init; } = BenchmarkComparabilityKey.NoValue;

    /// <summary>True when a difference on this key degrades the group's speed aggregates.</summary>
    public bool DegradesSpeed { get; init; }

    /// <summary>True when a difference on this key degrades the group's cost aggregates.</summary>
    public bool DegradesCost { get; init; }
}

/// <summary>One distinct value of a key, and the runs that carry it.</summary>
public sealed record BenchmarkComparabilityKeyVariant
{
    public string Value { get; init; } = BenchmarkComparabilityKey.NoValue;

    public IReadOnlyList<long> RunIds { get; init; } = Array.Empty<long>();
}

/// <summary>
/// A key on which the set does not agree, with every value and the runs carrying it. A boolean
/// verdict with no reason is unusable in a dialog or a bug report, which is why this exists.
/// </summary>
public sealed record BenchmarkComparabilityKeyDifference
{
    public string Name { get; init; } = string.Empty;

    public BenchmarkComparabilityKeyKind Kind { get; init; }

    public IReadOnlyList<BenchmarkComparabilityKeyVariant> Variants { get; init; }
        = Array.Empty<BenchmarkComparabilityKeyVariant>();

    /// <summary>A one-line rendering: the key, then each value and the runs that carry it.</summary>
    public string Describe()
    {
        var parts = Variants.Select(v =>
            $"{v.Value} (runs {string.Join(", ", v.RunIds)})");
        return $"{Name}: {string.Join(" vs ", parts)}";
    }
}

/// <summary>The tier a set resolves to, and everything a caller needs to explain that verdict.</summary>
public sealed record BenchmarkComparabilityResult
{
    public BenchmarkComparabilityTier Tier { get; init; }

    public IReadOnlyList<long> RunIds { get; init; } = Array.Empty<long>();

    /// <summary>
    /// True at Tier A and Tier B only. Tier C sets are compared, never pooled; a set below Tier B
    /// is neither.
    /// </summary>
    public bool PoolingPermitted { get; init; }

    /// <summary>Speed aggregates mix timing conditions and must be rendered with a degraded flag.</summary>
    public bool SpeedAggregatesDegraded { get; init; }

    /// <summary>Cost aggregates mix pricing or timing conditions and must be flagged.</summary>
    public bool CostAggregatesDegraded { get; init; }

    /// <summary>Keys on which every run agreed, in extraction order.</summary>
    public IReadOnlyList<string> MatchedKeys { get; init; } = Array.Empty<string>();

    /// <summary>Keys on which the set disagreed, with the values and the runs carrying them.</summary>
    public IReadOnlyList<BenchmarkComparabilityKeyDifference> Differences { get; init; }
        = Array.Empty<BenchmarkComparabilityKeyDifference>();

    /// <summary>
    /// A stable hash for the set, computed over the ordered distinct per-run key hashes. Equal
    /// sets hash equally regardless of the order they were passed in, and a set whose membership
    /// changes hashes differently — which is what lets a stored analysis be recognised as stale.
    /// </summary>
    public string ComparabilityKeyHash { get; init; } = string.Empty;

    /// <summary>Per-run key hashes, in run-id order, so a diagnostics dump can name the odd one out.</summary>
    public IReadOnlyDictionary<long, string> MemberKeyHashes { get; init; }
        = new Dictionary<long, string>();

    /// <summary>A sentence naming the tier and, when it is not Tier A, exactly what moved.</summary>
    public string Explanation { get; init; } = string.Empty;
}

/// <summary>
/// Extracts the comparability keys from a <see cref="BenchmarkRun"/> and resolves the tier of a
/// set of runs. Pure computation: no I/O, no AI calls, no writes.
///
/// The question this class answers is the one the whole multi-run feature rests on — *may these
/// runs be averaged together?* The failure it exists to prevent is a confident pooled index over
/// runs that measured different things, which is invisible once computed. So the resolution is
/// deliberately conservative: anything the keys cannot account for drops the set below Tier B
/// rather than being waved through.
///
/// **Callers must load <see cref="BenchmarkRun.Answers"/>.** The per-question item revision — a
/// rubric edit changes the answer key, which is exactly what the Rubric Gap Author does on
/// purpose — is read from the answers, and a run with no answers loaded contributes an empty
/// revision signature that compares equal to every other empty one.
/// </summary>
public static class BenchmarkComparabilityKey
{
    /// <summary>Rendering of an absent value. Two absent values compare equal.</summary>
    public const string NoValue = "(none)";

    // Key names are constants because the UI, the diagnostics text and the tests all name them,
    // and a differing key reported by a string literal in three places drifts.
    public const string SuiteKey = "BenchmarkSuiteId";
    public const string ItemRevisionsKey = "SuiteItemRevisions";
    public const string CandidateProviderKey = "CandidateProvider";
    public const string CandidateModelKey = "CandidateModelId";
    public const string CandidateThinkingLevelKey = "CandidateThinkingLevel";
    public const string CandidateReasoningModeKey = "CandidateReasoningMode";
    public const string CandidateReasoningSummaryKey = "CandidateReasoningSummary";
    public const string CandidateServiceTierKey = "CandidateServiceTier";
    public const string CandidateMaxOutputTokensKey = "CandidateMaxOutputTokens";
    public const string CandidateParallelExecutionModeKey = "CandidateParallelExecutionMode";
    public const string CandidatePromptOptionsKey = "CandidatePromptOptions";
    public const string CandidateSystemPromptKey = "CandidateSystemPromptSha256";
    public const string ToolGuidesKey = "ToolGuidesSha256";
    public const string KnowledgeBaseKey = "KnowledgeBaseHeadSha";
    public const string HarnessVersionKey = "HarnessVersion";
    public const string ScoringMethodVersionKey = "ScoringMethodVersion";
    public const string ScoringProfileKey = "ScoringProfile";
    public const string AssessorConfigurationKey = "AssessorConfiguration";
    public const string SecondOpinionConfigurationKey = "SecondOpinionConfiguration";
    public const string ClaimVerifierConfigurationKey = "ClaimVerifierConfiguration";
    public const string PerQuestionBudgetsKey = "PerQuestionBudgets";
    public const string QuestionParallelismKey = "QuestionParallelism";
    public const string PricingSnapshotKey = "PricingSnapshot";

    /// <summary>Tier A and Tier B may be pooled into one index; Tier C and below may not.</summary>
    public static bool IsPoolable(BenchmarkComparabilityTier tier)
        => tier == BenchmarkComparabilityTier.Replicate || tier == BenchmarkComparabilityTier.QualityComparable;

    /// <summary>
    /// Every comparability key of one run, in a stable order.
    ///
    /// Related fields are combined into one composite key on purpose. "The assessor changed" is
    /// one experimental difference, not nine; splitting it across provider, model id, thinking
    /// level and the rest would push a single deliberate assessor swap below Tier B, because Tier
    /// C permits exactly one differing instrument key.
    /// </summary>
    public static IReadOnlyList<BenchmarkComparabilityKeyEntry> Extract(BenchmarkRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        var keys = new List<BenchmarkComparabilityKeyEntry>
        {
            // --- Fundamental: the exam and its answer key -----------------------------------
            Key(SuiteKey, BenchmarkComparabilityKeyKind.Fundamental, Render(run.BenchmarkSuiteId)),
            Key(ItemRevisionsKey, BenchmarkComparabilityKeyKind.Fundamental, ItemRevisionSignature(run)),

            // --- Candidate specification ------------------------------------------------------
            Key(CandidateProviderKey, BenchmarkComparabilityKeyKind.Candidate, Render(run.TestedModelProviderUsed)),
            Key(CandidateModelKey, BenchmarkComparabilityKeyKind.Candidate, Render(run.TestedModelIdUsed)),
            Key(CandidateThinkingLevelKey, BenchmarkComparabilityKeyKind.Candidate, Render(run.TestedModelThinkingLevelUsed)),
            Key(CandidateReasoningModeKey, BenchmarkComparabilityKeyKind.Candidate, Render(run.TestedModelReasoningModeUsed)),
            Key(CandidateReasoningSummaryKey, BenchmarkComparabilityKeyKind.Candidate, Render(run.TestedModelReasoningSummaryUsed)),
            Key(CandidateServiceTierKey, BenchmarkComparabilityKeyKind.Candidate, Render(run.TestedModelServiceTierUsed)),
            Key(CandidateMaxOutputTokensKey, BenchmarkComparabilityKeyKind.Candidate, Render(run.TestedModelMaxOutputTokensUsed)),
            Key(CandidateParallelExecutionModeKey, BenchmarkComparabilityKeyKind.Candidate, Render((int)run.TestedModelParallelExecutionModeUsed)),

            // The prompt options and the parallel mode travel together through the same signature
            // BenchmarkCandidatePromptOptions already defines, so there is one definition of "the
            // same prompt configuration" rather than two that can drift.
            Key(CandidatePromptOptionsKey, BenchmarkComparabilityKeyKind.Candidate,
                BenchmarkCandidatePromptOptions.FromJson(run.CandidatePromptOptionsJson)
                    .ComparabilitySignature(run.TestedModelParallelExecutionModeUsed)),

            // --- Instrument -------------------------------------------------------------------
            Key(CandidateSystemPromptKey, BenchmarkComparabilityKeyKind.Instrument, Render(run.CandidateSystemPromptSha256)),
            Key(ToolGuidesKey, BenchmarkComparabilityKeyKind.Instrument, Render(run.ToolGuidesSha256)),
            Key(KnowledgeBaseKey, BenchmarkComparabilityKeyKind.Instrument, Render(run.KnowledgeBaseHeadSha)),
            Key(HarnessVersionKey, BenchmarkComparabilityKeyKind.Instrument, Render(run.HarnessVersion)),
            Key(ScoringMethodVersionKey, BenchmarkComparabilityKeyKind.Instrument, Render(run.ScoringMethodVersion)),

            // The profile id AND the snapshot. The id alone is not enough: the Default profile is
            // edited in place, so two runs can name profile 1 and have been scored under two
            // different definitions of it.
            Key(ScoringProfileKey, BenchmarkComparabilityKeyKind.Instrument,
                $"id={Render(run.ScoringProfileId)};snapshot={ShortHash(run.ScoringProfileSnapshotJson)}"),

            Key(AssessorConfigurationKey, BenchmarkComparabilityKeyKind.Instrument, AssessorSignature(run)),
            Key(SecondOpinionConfigurationKey, BenchmarkComparabilityKeyKind.Instrument, SecondOpinionSignature(run)),
            Key(ClaimVerifierConfigurationKey, BenchmarkComparabilityKeyKind.Instrument, ClaimVerifierSignature(run)),
            Key(PerQuestionBudgetsKey, BenchmarkComparabilityKeyKind.Instrument, BudgetSignature(run)),

            // --- Speed and cost ---------------------------------------------------------------
            //
            // Question parallelism degrades speed *and* cost, not speed alone: running questions
            // concurrently changes prompt-cache behaviour and therefore token spend, so a cost
            // aggregate that mixes timing modes is not comparable either. The pricing snapshot
            // degrades cost only — prices cannot move a quality or a speed score.
            Key(QuestionParallelismKey, BenchmarkComparabilityKeyKind.SpeedAndCost,
                Render(run.MaxParallelQuestionsUsed), degradesSpeed: true, degradesCost: true),
            Key(PricingSnapshotKey, BenchmarkComparabilityKeyKind.SpeedAndCost,
                ShortHash(run.PricingSnapshotJson), degradesSpeed: false, degradesCost: true)
        };

        return keys;
    }

    /// <summary>
    /// A stable SHA-256 over one run's keys, as lower-case hex. Two runs that agree on every key
    /// hash equally; any difference changes the hash.
    /// </summary>
    public static string ComputeKeyHash(BenchmarkRun run)
    {
        var canonical = string.Join("\n", Extract(run).Select(k => $"{k.Name}={k.Value}"));
        return Sha256Hex(canonical);
    }

    /// <summary>
    /// Resolves the tier of a set of runs and reports every key it disagreed on.
    ///
    /// The resolution order is the ladder itself:
    /// <list type="number">
    /// <item>A <see cref="BenchmarkComparabilityKeyKind.Fundamental"/> or
    /// <see cref="BenchmarkComparabilityKeyKind.Candidate"/> difference →
    /// <see cref="BenchmarkComparabilityTier.NotComparable"/>.</item>
    /// <item>Two or more <see cref="BenchmarkComparabilityKeyKind.Instrument"/> differences →
    /// <see cref="BenchmarkComparabilityTier.NotComparable"/>: that is an uncontrolled
    /// comparison, not an experiment.</item>
    /// <item>Exactly one instrument difference → <see cref="BenchmarkComparabilityTier.CrossCondition"/>.</item>
    /// <item>Only <see cref="BenchmarkComparabilityKeyKind.SpeedAndCost"/> differences →
    /// <see cref="BenchmarkComparabilityTier.QualityComparable"/>, with the affected aggregates flagged.</item>
    /// <item>No differences → <see cref="BenchmarkComparabilityTier.Replicate"/>.</item>
    /// </list>
    ///
    /// An empty set is <see cref="BenchmarkComparabilityTier.NotComparable"/>. A single run is
    /// Tier A trivially — it agrees with itself — which is what lets the series orchestrator
    /// assert the tier of a group as it grows rather than only at the end.
    /// </summary>
    public static BenchmarkComparabilityResult Resolve(IReadOnlyCollection<BenchmarkRun>? runs)
    {
        var members = (runs ?? Array.Empty<BenchmarkRun>()).Where(r => r != null).ToList();
        var runIds = members.Select(r => r.Id).OrderBy(id => id).ToList();

        if (members.Count == 0)
        {
            return new BenchmarkComparabilityResult
            {
                Tier = BenchmarkComparabilityTier.NotComparable,
                PoolingPermitted = false,
                ComparabilityKeyHash = Sha256Hex(string.Empty),
                Explanation = "No runs: a comparability tier is undefined over an empty set."
            };
        }

        var extracted = members.ToDictionary(r => r, r => Extract(r));
        var hashes = members
            .OrderBy(r => r.Id)
            .ToDictionary(r => r.Id, ComputeKeyHash);

        var keyNames = extracted[members[0]].Select(k => k.Name).ToList();
        var kinds = extracted[members[0]].ToDictionary(k => k.Name, k => k.Kind);

        var matched = new List<string>();
        var differences = new List<BenchmarkComparabilityKeyDifference>();
        bool speedDegraded = false;
        bool costDegraded = false;

        foreach (var name in keyNames)
        {
            var byValue = new Dictionary<string, List<long>>(StringComparer.Ordinal);
            bool degradesSpeed = false;
            bool degradesCost = false;

            foreach (var run in members)
            {
                var entry = extracted[run].FirstOrDefault(k => k.Name == name);
                string value = entry?.Value ?? NoValue;
                degradesSpeed |= entry?.DegradesSpeed ?? false;
                degradesCost |= entry?.DegradesCost ?? false;

                if (!byValue.TryGetValue(value, out var list))
                {
                    list = new List<long>();
                    byValue[value] = list;
                }

                list.Add(run.Id);
            }

            if (byValue.Count <= 1)
            {
                matched.Add(name);
                continue;
            }

            speedDegraded |= degradesSpeed;
            costDegraded |= degradesCost;

            differences.Add(new BenchmarkComparabilityKeyDifference
            {
                Name = name,
                Kind = kinds[name],
                Variants = byValue
                    .OrderBy(kv => kv.Value.Min())
                    .Select(kv => new BenchmarkComparabilityKeyVariant
                    {
                        Value = kv.Key,
                        RunIds = kv.Value.OrderBy(id => id).ToList()
                    })
                    .ToList()
            });
        }

        int fundamental = differences.Count(d => d.Kind == BenchmarkComparabilityKeyKind.Fundamental);
        int candidate = differences.Count(d => d.Kind == BenchmarkComparabilityKeyKind.Candidate);
        int instrument = differences.Count(d => d.Kind == BenchmarkComparabilityKeyKind.Instrument);
        int speedCost = differences.Count(d => d.Kind == BenchmarkComparabilityKeyKind.SpeedAndCost);

        BenchmarkComparabilityTier tier;
        string explanation;

        if (fundamental > 0 || candidate > 0)
        {
            tier = BenchmarkComparabilityTier.NotComparable;
            var blocking = differences
                .Where(d => d.Kind == BenchmarkComparabilityKeyKind.Fundamental
                            || d.Kind == BenchmarkComparabilityKeyKind.Candidate)
                .ToList();
            explanation = fundamental > 0
                ? "Below Tier B: the runs did not answer the same questions against the same answer key. "
                  + string.Join("; ", blocking.Select(d => d.Describe()))
                : "Below Tier B: the candidate specification differs, so these runs measure different "
                  + "subjects. Compare two models as two Tier A groups, never as one group. "
                  + string.Join("; ", blocking.Select(d => d.Describe()));
        }
        else if (instrument > 1)
        {
            tier = BenchmarkComparabilityTier.NotComparable;
            explanation = "Below Tier B: " + instrument + " instrument keys differ, so nothing here is a "
                + "controlled comparison. "
                + string.Join("; ", differences
                    .Where(d => d.Kind == BenchmarkComparabilityKeyKind.Instrument)
                    .Select(d => d.Describe()));
        }
        else if (instrument == 1)
        {
            tier = BenchmarkComparabilityTier.CrossCondition;
            explanation = "Tier C — cross-condition: the candidate is identical and exactly one instrument "
                + "key was moved. This set is two conditions and must be compared, never pooled into one "
                + "index. "
                + differences.First(d => d.Kind == BenchmarkComparabilityKeyKind.Instrument).Describe();
        }
        else if (speedCost > 0)
        {
            tier = BenchmarkComparabilityTier.QualityComparable;
            explanation = "Tier B — quality-comparable: quality aggregates are valid; "
                + (speedDegraded && costDegraded ? "speed and cost aggregates are degraded. "
                    : speedDegraded ? "speed aggregates are degraded. "
                    : "cost aggregates are degraded. ")
                + string.Join("; ", differences
                    .Where(d => d.Kind == BenchmarkComparabilityKeyKind.SpeedAndCost)
                    .Select(d => d.Describe()));
        }
        else
        {
            tier = BenchmarkComparabilityTier.Replicate;
            explanation = members.Count == 1
                ? "Tier A — replicate: a single run agrees with itself on every key."
                : $"Tier A — replicate: all {matched.Count} comparability keys match across {members.Count} runs.";
        }

        // Below Tier B nothing is comparable, so the speed/cost flags would be a claim about a set
        // that has no valid aggregates at all. Leave them false there rather than half-reporting.
        if (tier == BenchmarkComparabilityTier.NotComparable)
        {
            speedDegraded = false;
            costDegraded = false;
        }

        return new BenchmarkComparabilityResult
        {
            Tier = tier,
            RunIds = runIds,
            PoolingPermitted = IsPoolable(tier),
            SpeedAggregatesDegraded = speedDegraded,
            CostAggregatesDegraded = costDegraded,
            MatchedKeys = matched,
            Differences = differences,
            ComparabilityKeyHash = Sha256Hex(string.Join("\n", hashes.Values.Distinct().OrderBy(h => h, StringComparer.Ordinal))),
            MemberKeyHashes = hashes,
            Explanation = explanation
        };
    }

    // --- Key construction -------------------------------------------------------------------

    private static BenchmarkComparabilityKeyEntry Key(
        string name,
        BenchmarkComparabilityKeyKind kind,
        string value,
        bool degradesSpeed = false,
        bool degradesCost = false)
    {
        return new BenchmarkComparabilityKeyEntry
        {
            Name = name,
            Kind = kind,
            Value = string.IsNullOrWhiteSpace(value) ? NoValue : value,
            DegradesSpeed = degradesSpeed,
            DegradesCost = degradesCost
        };
    }

    /// <summary>
    /// The item revision of every question this run answered, as <c>questionId:revision</c> pairs
    /// in question-id order. A rubric edit bumps a revision, which changes the answer key, which
    /// is exactly why this is a Tier-A key rather than a detail.
    ///
    /// An answer whose revision was never recorded renders as <c>?</c> — reported, not assumed to
    /// be the current revision, following the <see cref="BenchmarkItemAnalysis"/> precedent.
    /// Unlinked answers are skipped: they cannot be attributed to a question at all.
    /// </summary>
    private static string ItemRevisionSignature(BenchmarkRun run)
    {
        var answers = run.Answers ?? new List<BenchmarkRunAnswer>();
        var parts = answers
            .Where(a => a.BenchmarkQuestionId.HasValue)
            .GroupBy(a => a.BenchmarkQuestionId!.Value)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var revisions = g.Select(a => a.ItemRevisionUsed)
                    .Distinct()
                    .OrderBy(r => r ?? int.MinValue)
                    .Select(r => r?.ToString(CultureInfo.InvariantCulture) ?? "?");
                return $"{g.Key}:{string.Join("/", revisions)}";
            });

        return string.Join(",", parts);
    }

    private static string AssessorSignature(BenchmarkRun run)
    {
        return string.Join(";", new[]
        {
            $"provider={Render(run.AssessorModelProviderUsed)}",
            $"model={Render(run.AssessorModelIdUsed)}",
            $"thinking={Render(run.AssessorModelThinkingLevelUsed)}",
            $"reasoningMode={Render(run.AssessorModelReasoningModeUsed)}",
            $"reasoningSummary={Render(run.AssessorModelReasoningSummaryUsed)}",
            $"serviceTier={Render(run.AssessorModelServiceTierUsed)}",
            $"maxOutputTokens={Render(run.AssessorModelMaxOutputTokensUsed)}",
            $"parallelMode={(int)run.AssessorModelParallelExecutionModeUsed}"
        });
    }

    private static string SecondOpinionSignature(BenchmarkRun run)
    {
        return string.Join(";", new[]
        {
            $"provider={Render(run.SecondOpinionAssessorModelProviderUsed)}",
            $"model={Render(run.SecondOpinionAssessorModelIdUsed)}",
            $"thinking={Render(run.SecondOpinionAssessorModelThinkingLevelUsed)}",
            $"reasoningMode={Render(run.SecondOpinionAssessorModelReasoningModeUsed)}",
            $"mode={run.SecondOpinionModeUsed.ToString(CultureInfo.InvariantCulture)}",
            $"blind={(run.SecondOpinionBlindUsed ? "1" : "0")}",
            $"minimumSample={SecondOpinionMinimumSample(run)}"
        });
    }

    /// <summary>
    /// The profile's <c>SecondOpinionMinimumSample</c>, read from the scoring profile snapshot the
    /// run stored at start time.
    ///
    /// TODO(Phase 3): the column does not exist yet — Phase 3 adds it to
    /// <c>BenchmarkScoringProfile</c>, and from that point the snapshot carries it and this reads
    /// the real value with no change here. Until then every run reports <c>(none)</c>, which
    /// compares equal across runs and therefore changes no tier. Reading it out of the snapshot
    /// rather than off a run column is deliberate: the snapshot is what the run was actually
    /// scored under, and it is already a Tier A key, so this is a legible restatement of a
    /// difference the profile hash would catch anyway.
    /// </summary>
    private static string SecondOpinionMinimumSample(BenchmarkRun run)
    {
        return SnapshotProperty(run.ScoringProfileSnapshotJson, "SecondOpinionMinimumSample") ?? NoValue;
    }

    private static string ClaimVerifierSignature(BenchmarkRun run)
    {
        return string.Join(";", new[]
        {
            $"provider={Render(run.ClaimVerifierProviderUsed)}",
            $"model={Render(run.ClaimVerifierModelIdUsed)}",
            $"thinking={Render(run.ClaimVerifierThinkingLevelUsed)}",
            $"reasoningMode={Render(run.ClaimVerifierReasoningModeUsed)}"
        });
    }

    /// <summary>
    /// The per-question budgets the run recorded.
    ///
    /// Only the tool-call budget is stored on the run. The tool-iteration cap, the total
    /// model-call cap and the question timeout are resolved from configuration per difficulty band
    /// at answer time (<c>BenchmarkService.ResolveBandedCap</c>) and are recorded nowhere, so they
    /// cannot enter this key. Two runs that straddle a change to
    /// <c>Benchmark:QuestionTimeoutSeconds</c> therefore still resolve Tier A. Closing that hole
    /// needs those three caps snapshotted onto <see cref="BenchmarkRun"/>, which this phase does
    /// not own; <see cref="BenchmarkRun.HarnessVersion"/> is the only partial cover today.
    /// </summary>
    private static string BudgetSignature(BenchmarkRun run)
    {
        return $"maxToolCallsPerQuestion={Render(run.MaxToolCallsPerQuestionUsed)}";
    }

    // --- Rendering and hashing ----------------------------------------------------------------

    private static string Render(string? value)
        => string.IsNullOrWhiteSpace(value) ? NoValue : value.Trim();

    private static string Render(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Render(int? value)
        => value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : NoValue;

    private static string Render(long? value)
        => value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : NoValue;

    /// <summary>
    /// A JSON snapshot's first twelve hash characters. The snapshot itself is far too long to put
    /// in a difference line an operator has to read, and its formatting is not canonical, so it is
    /// compared as a hash and displayed as a fingerprint.
    /// </summary>
    private static string ShortHash(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return NoValue;
        return Sha256Hex(json.Trim()).Substring(0, 12);
    }

    private static string? SnapshotProperty(string? snapshotJson, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(snapshotJson)) return null;

        try
        {
            using var doc = JsonDocument.Parse(snapshotJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            if (!doc.RootElement.TryGetProperty(propertyName, out var value)) return null;

            return value.ValueKind switch
            {
                JsonValueKind.Null => null,
                JsonValueKind.String => value.GetString(),
                _ => value.GetRawText()
            };
        }
        catch (JsonException)
        {
            // A malformed snapshot must not take a tier resolution down with it. The profile hash
            // still differs if the text differs, so the difference is not lost — only its label.
            return null;
        }
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
