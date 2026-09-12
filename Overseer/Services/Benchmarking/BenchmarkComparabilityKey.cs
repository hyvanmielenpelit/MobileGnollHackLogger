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

/// <summary>
/// How a key's value should be read, so a view renders it correctly without sniffing it.
///
/// <para>The kind describes the <i>shape</i> of the canonical value, never its meaning: it is what
/// tells a reader that a 64-character string is a digest to be abbreviated rather than a name to be
/// printed whole, and that a semicolon-separated signature is a list of settings rather than one
/// unbreakable token.</para>
/// </summary>
public enum BenchmarkComparabilityValueKind
{
    /// <summary>A short human-readable value that is printed as it stands.</summary>
    Text = 0,

    /// <summary>A database identifier, which a view may pair with the name it stands for.</summary>
    Identifier = 1,

    /// <summary>A hex fingerprint: legible as a short prefix, verifiable only in full.</summary>
    Hash = 2,

    /// <summary>A JSON document, which is legible only pretty-printed.</summary>
    Json = 3,

    /// <summary>Several settings joined into one signature, separable into one element each.</summary>
    List = 4
}

/// <summary>
/// What one comparability key is, in words a reader outside this file can use.
///
/// <para>The comparability machinery names its keys by C# constant so that the UI, the diagnostics
/// text and the tests cannot drift apart; those names are precise and mean nothing to an operator.
/// This record is the other half: the phrase a methods statement prints, and the one line that says
/// what a difference on the key would cost a comparison.</para>
/// </summary>
public sealed record BenchmarkComparabilityKeyInfo
{
    /// <summary>The key name, as <see cref="BenchmarkComparabilityKeyEntry.Name"/> reports it.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>A short noun phrase — "Question suite", "Candidate system prompt".</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>One line: what a difference on this key would mean for a comparison.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>The shape of the canonical value, so a view renders it without inspecting it.</summary>
    public BenchmarkComparabilityValueKind ValueKind { get; init; }
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
    public const string AssessedDifficultiesKey = "SuiteAssessedDifficulties";
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

    /// <summary>
    /// The pricing snapshot property that records when the snapshot was taken. Excluded from the
    /// comparability fingerprint — see <see cref="PricingSignature"/>.
    /// </summary>
    private const string PricingCapturedAtProperty = "capturedAtUtc";

    /// <summary>
    /// Options for reading a stored snapshot back. Snapshots are written with the serializer's
    /// defaults, so their property names are PascalCase; matching case-insensitively costs nothing
    /// and also reads a camelCase snapshot, should one have been written by another path.
    /// </summary>
    private static readonly JsonSerializerOptions SnapshotSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>Tier A and Tier B may be pooled into one index; Tier C and below may not.</summary>
    public static bool IsPoolable(BenchmarkComparabilityTier tier)
        => tier == BenchmarkComparabilityTier.Replicate || tier == BenchmarkComparabilityTier.QualityComparable;

    /// <summary>
    /// The label, description and value kind of one key.
    ///
    /// <para>An unrecognised name degrades to itself as the label, an empty description and
    /// <see cref="BenchmarkComparabilityValueKind.Text"/>, so a key added to <see cref="Extract"/>
    /// renders plainly — as its own machine name against its raw value — rather than disappearing
    /// from a methods statement before it is described here.</para>
    /// </summary>
    public static BenchmarkComparabilityKeyInfo Describe(string name)
    {
        return name switch
        {
            // --- Fundamental: the exam and its answer key -----------------------------------
            SuiteKey => Info(name,
                "Question suite",
                "A difference means the runs answered different questions, so no measure can be "
                + "paired by question.",
                BenchmarkComparabilityValueKind.Identifier),

            ItemRevisionsKey => Info(name,
                "Suite item revisions",
                "A rubric edit bumps an item's revision, so a difference means the same questions "
                + "were graded against a different answer key.",
                BenchmarkComparabilityValueKind.List),

            AssessedDifficultiesKey => Info(name,
                "Suite assessed difficulties",
                "Assess Difficulty re-weights a question without bumping its item revision, so a "
                + "difference means the same answer key was scored under different Intelligence "
                + "Index weights.",
                BenchmarkComparabilityValueKind.List),

            // --- Candidate specification ------------------------------------------------------
            CandidateProviderKey => Info(name,
                "Candidate provider",
                "A difference means a different vendor served the answers, so the runs are two "
                + "subjects rather than replicates of one.",
                BenchmarkComparabilityValueKind.Text),

            CandidateModelKey => Info(name,
                "Candidate model",
                "A difference means a different model answered; two models are compared as two "
                + "groups and never averaged into one.",
                BenchmarkComparabilityValueKind.Text),

            CandidateThinkingLevelKey => Info(name,
                "Candidate thinking level",
                "Thinking level dominates model time, so a difference compares the models as they "
                + "are configured rather than at one common setting.",
                BenchmarkComparabilityValueKind.Text),

            CandidateReasoningModeKey => Info(name,
                "Candidate reasoning mode",
                "A difference changes how the model was asked to reason, so the runs describe two "
                + "differently configured subjects.",
                BenchmarkComparabilityValueKind.Text),

            CandidateReasoningSummaryKey => Info(name,
                "Candidate reasoning summary",
                "A difference changes what reasoning the model was asked to disclose, which is part "
                + "of how the subject was configured.",
                BenchmarkComparabilityValueKind.Text),

            CandidateServiceTierKey => Info(name,
                "Candidate service tier",
                "A difference means one model was served under a different provider tier, which "
                + "moves its speed and its price.",
                BenchmarkComparabilityValueKind.Text),

            CandidateMaxOutputTokensKey => Info(name,
                "Candidate output token cap",
                "A cap that binds truncates an answer, so a difference can move completeness as "
                + "well as cost.",
                BenchmarkComparabilityValueKind.Text),

            CandidateParallelExecutionModeKey => Info(name,
                "Candidate batching mode",
                "A difference changes how the candidate's model calls were batched, which is part "
                + "of how the subject was configured.",
                BenchmarkComparabilityValueKind.Text),

            CandidatePromptOptionsKey => Info(name,
                "Candidate prompt options",
                "The production chat prompt configuration the model was graded under: a difference "
                + "makes the points incomparable on completeness, conciseness and readability.",
                BenchmarkComparabilityValueKind.Json),

            // --- Instrument -------------------------------------------------------------------
            CandidateSystemPromptKey => Info(name,
                "Candidate system prompt",
                "A difference means the answers were produced under different instructions, which "
                + "is a different instrument rather than a different subject.",
                BenchmarkComparabilityValueKind.Hash),

            ToolGuidesKey => Info(name,
                "Tool guides",
                "A difference means the model was given different guidance about its tools — the "
                + "deliberate single-variable change a cross-condition comparison exists for.",
                BenchmarkComparabilityValueKind.Hash),

            KnowledgeBaseKey => Info(name,
                "Knowledge base head",
                "A difference means the retrievable corpus moved, so the same question had "
                + "different material available to answer it.",
                BenchmarkComparabilityValueKind.Hash),

            HarnessVersionKey => Info(name,
                "Harness version",
                "A difference means different harness code ran the questions, so the apparatus "
                + "itself is not the same.",
                BenchmarkComparabilityValueKind.Text),

            ScoringMethodVersionKey => Info(name,
                "Scoring method version",
                "A difference means the answers were scored by a different method, so the scores "
                + "are not on one scale.",
                BenchmarkComparabilityValueKind.Text),

            ScoringProfileKey => Info(name,
                "Scoring profile",
                "The profile identity and the scoring semantics it held at run time: a difference "
                + "means the weights, level scores or error ceiling behind the scores moved.",
                BenchmarkComparabilityValueKind.List),

            AssessorConfigurationKey => Info(name,
                "Assessor configuration",
                "The grading model and how it was configured; a difference means a different "
                + "grader produced the scores.",
                BenchmarkComparabilityValueKind.List),

            SecondOpinionConfigurationKey => Info(name,
                "Second-opinion configuration",
                "How and how often a second grader was consulted; a difference changes the "
                + "adjudication that settled the scores.",
                BenchmarkComparabilityValueKind.List),

            ClaimVerifierConfigurationKey => Info(name,
                "Claim verifier configuration",
                "The model that checked the candidate's factual claims; a difference changes how "
                + "accuracy was established.",
                BenchmarkComparabilityValueKind.List),

            PerQuestionBudgetsKey => Info(name,
                "Per-question budgets",
                "The tool-call, iteration, model-call and timeout limits; a limit that binds "
                + "truncates an investigation and moves what the candidate scored.",
                BenchmarkComparabilityValueKind.List),

            // --- Speed and cost ---------------------------------------------------------------
            QuestionParallelismKey => Info(name,
                "Question parallelism",
                "Answering questions concurrently changes both timing and prompt-cache behaviour, "
                + "so a difference degrades the speed and the cost aggregates alike.",
                BenchmarkComparabilityValueKind.Text),

            PricingSnapshotKey => Info(name,
                "Pricing snapshot",
                "The catalog prices the run was costed from; a difference degrades cost alone, "
                + "because prices cannot move a quality or a speed score.",
                BenchmarkComparabilityValueKind.Hash),

            _ => Info(name, name, string.Empty, BenchmarkComparabilityValueKind.Text)
        };
    }

    private static BenchmarkComparabilityKeyInfo Info(
        string name,
        string label,
        string description,
        BenchmarkComparabilityValueKind valueKind)
    {
        return new BenchmarkComparabilityKeyInfo
        {
            Name = name,
            Label = label,
            Description = description,
            ValueKind = valueKind
        };
    }

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
            Key(SuiteKey, BenchmarkComparabilityKeyKind.Fundamental,
                Render(run.BenchmarkSuiteIdUsed ?? run.BenchmarkSuiteId)),
            Key(ItemRevisionsKey, BenchmarkComparabilityKeyKind.Fundamental, ItemRevisionSignature(run)),
            Key(AssessedDifficultiesKey, BenchmarkComparabilityKeyKind.Fundamental, AssessedDifficultySignature(run)),

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

            // The profile id AND the scoring semantics of the snapshot. The id alone is not enough:
            // the Default profile is edited in place, so two runs can name profile 1 and have been
            // scored under two different definitions of it. See ScoringProfileSemanticsSignature
            // for what the semantics cover and what they ignore.
            Key(ScoringProfileKey, BenchmarkComparabilityKeyKind.Instrument,
                $"id={Render(run.ScoringProfileId)};semantics={ScoringProfileSemanticsSignature(run)}"),

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
                PricingSignature(run), degradesSpeed: false, degradesCost: true)
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

        // A Fundamental key every member agrees on only because none of them has a value is not
        // agreement: identity absent from all of them cannot establish that they sat the same exam,
        // and two runs from two different deleted suites would otherwise match on the sentinel.
        // Meaningful only across members, so a single run keeps the trivial Tier A below.
        var absentFundamental = members.Count < 2
            ? new List<string>()
            : keyNames
                .Where(n => kinds[n] == BenchmarkComparabilityKeyKind.Fundamental && matched.Contains(n))
                .Where(n => string.Equals(
                    extracted[members[0]].First(k => k.Name == n).Value,
                    NoValue,
                    StringComparison.Ordinal))
                .ToList();

        BenchmarkComparabilityTier tier;
        string explanation;

        if (absentFundamental.Count > 0)
        {
            tier = BenchmarkComparabilityTier.NotComparable;
            explanation = "Below Tier B: the exam these runs sat cannot be identified — "
                + string.Join(", ", absentFundamental)
                + (absentFundamental.Count == 1 ? " has" : " have")
                + " no value on any member, so agreement on "
                + (absentFundamental.Count == 1 ? "it" : "them")
                + " is absence, not a match. This happens when the suite was deleted before the "
                + "run's identity was recorded.";
        }
        else if (fundamental > 0 || candidate > 0)
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
    ///
    /// Question identity comes from <see cref="BenchmarkRunAnswer.BenchmarkQuestionIdUsed"/> in
    /// preference to the foreign key, which a suite deletion clears. Both yield the same rendering
    /// for a row that has both, so a run's signature does not move when its suite goes.
    /// </summary>
    private static string ItemRevisionSignature(BenchmarkRun run)
    {
        var answers = run.Answers ?? new List<BenchmarkRunAnswer>();
        var parts = answers
            .Select(a => new
            {
                QuestionId = a.BenchmarkQuestionIdUsed ?? a.BenchmarkQuestionId,
                a.ItemRevisionUsed
            })
            .Where(a => a.QuestionId.HasValue)
            .GroupBy(a => a.QuestionId!.Value)
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

    /// <summary>
    /// The assessed difficulty of every question this run answered, as <c>questionId:difficulty</c>
    /// pairs in question-id order. Assess Difficulty rewrites <see cref="BenchmarkRunAnswer.AssessedDifficulty"/>
    /// without bumping <see cref="BenchmarkRunAnswer.ItemRevisionUsed"/>, so this is a distinct
    /// Fundamental key from <see cref="ItemRevisionSignature"/> rather than folded into it: two runs
    /// can agree on every item revision and still have been weighted by two different exams.
    ///
    /// An answer whose difficulty was never assessed renders as <c>?</c>, following the same
    /// convention as <see cref="ItemRevisionSignature"/>. Unlinked answers are skipped.
    /// </summary>
    private static string AssessedDifficultySignature(BenchmarkRun run)
    {
        var answers = run.Answers ?? new List<BenchmarkRunAnswer>();
        var parts = answers
            .Select(a => new
            {
                QuestionId = a.BenchmarkQuestionIdUsed ?? a.BenchmarkQuestionId,
                a.AssessedDifficulty
            })
            .Where(a => a.QuestionId.HasValue)
            .GroupBy(a => a.QuestionId!.Value)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var difficulties = g.Select(a => a.AssessedDifficulty)
                    .Distinct()
                    .OrderBy(d => d ?? int.MinValue)
                    .Select(d => d?.ToString(CultureInfo.InvariantCulture) ?? "?");
                return $"{g.Key}:{string.Join("/", difficulties)}";
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
    /// run stored at start time. A snapshot written before the field existed reports
    /// <c>(none)</c>, which compares equal across such runs and therefore changes no tier.
    ///
    /// Reading it out of the snapshot rather than off a run column is deliberate: the snapshot is
    /// what the run was actually scored under, and it is already a Tier A key, so this is a legible
    /// restatement of a difference the profile signature would catch anyway.
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
    /// The whole per-question budget configuration the run recorded: the tool-call budget, and the
    /// per-band tables for the tool-iteration cap, the total model-call cap and the question
    /// timeout.
    ///
    /// <para>The three tables are rendered as the canonical JSON
    /// <see cref="BenchmarkRun.ToolIterationCapsJson"/> and its siblings store, so the key covers
    /// every band a run could have drawn from rather than one resolved figure. A run recorded
    /// before those columns existed renders them as <see cref="NoValue"/>: such runs match each
    /// other and differ from snapshotted ones, which is correct, because for them the harness
    /// genuinely does not know what caps applied.</para>
    ///
    /// <para>This is an instrument key because a cap that binds truncates an investigation, which
    /// moves what the candidate scored.</para>
    /// </summary>
    private static string BudgetSignature(BenchmarkRun run)
    {
        return string.Join(";", new[]
        {
            $"maxToolCallsPerQuestion={Render(run.MaxToolCallsPerQuestionUsed)}",
            $"toolIterationCaps={Render(run.ToolIterationCapsJson)}",
            $"totalModelCallCaps={Render(run.TotalModelCallCapsJson)}",
            $"questionTimeoutSeconds={Render(run.QuestionTimeoutSecondsJson)}"
        });
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

    /// <summary>
    /// A pricing snapshot's fingerprint over its <i>prices</i> alone.
    ///
    /// <para>The snapshot carries <c>capturedAtUtc</c> beside the four role price cards. That
    /// instant is provenance, not a pricing condition: two runs of one series are launched minutes
    /// apart from one identical request, so a fingerprint that included it would differ by
    /// construction and no series could ever resolve Tier A. Each role's <c>asOf</c> stays in — two
    /// runs priced from different catalog revisions genuinely are not cost-comparable.</para>
    ///
    /// <para>A snapshot that cannot be parsed falls back to hashing the whole string, which is
    /// deterministic and no worse than the value it replaces.</para>
    /// </summary>
    private static string PricingSignature(BenchmarkRun run)
    {
        string? json = run.PricingSnapshotJson;
        if (string.IsNullOrWhiteSpace(json)) return NoValue;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return ShortHash(json);

            var parts = doc.RootElement.EnumerateObject()
                .Where(p => !string.Equals(p.Name, PricingCapturedAtProperty, StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p.Name, StringComparer.Ordinal)
                .Select(p => $"{p.Name}={p.Value.GetRawText()}");

            return Sha256Hex(string.Join(";", parts)).Substring(0, 12);
        }
        catch (JsonException)
        {
            return ShortHash(json);
        }
    }

    /// <summary>
    /// A fingerprint of the scoring profile a run was graded under, over the profile's <i>scoring
    /// semantics</i> alone.
    ///
    /// <para>Read from <see cref="BenchmarkRun.ScoringProfileSnapshotJson"/> — the definition the
    /// run was actually scored under — and never from the live profile row, which is the reason the
    /// snapshot exists. The snapshot itself stays the full entity: the name a profile carried at
    /// run time is correct historical data. Only what this key reads out of it is narrowed.</para>
    ///
    /// <para>Covered: the four dimension weights, the level-score table, the critical-error
    /// ceiling, every second-opinion setting, the three speed constants, and question parallelism.
    /// See <see cref="BenchmarkScoringProfileService.CanonicalSignature"/> for the exact list and
    /// its canonical rendering.</para>
    ///
    /// <para>Deliberately ignored: <c>Name</c>, <c>IsDefault</c>, <c>CreatedAtUtc</c> and
    /// <c>ModifiedAtUtc</c>. None of them can move a score, and hashing the raw snapshot blob made
    /// all four move this key — so renaming a profile, promoting another profile to default, or
    /// editing and reverting any field ended a comparable series with no warning anywhere in the
    /// UI. That is the same defect <see cref="PricingSignature"/> exists to avoid, one snapshot
    /// over: a non-semantic field inside a hashed blob costs a tier. The profile id stays in the
    /// key beside this, because two profiles with identical semantics are still two profiles.</para>
    ///
    /// <para>A snapshot that will not deserialise falls back to hashing the whole string, which is
    /// deterministic and no worse than the value it replaces — a corrupt or oddly shaped row
    /// degrades to a blob comparison rather than matching everything.</para>
    /// </summary>
    private static string ScoringProfileSemanticsSignature(BenchmarkRun run)
    {
        string? json = run.ScoringProfileSnapshotJson;
        if (string.IsNullOrWhiteSpace(json)) return NoValue;

        try
        {
            var profile = JsonSerializer.Deserialize<BenchmarkScoringProfile>(json, SnapshotSerializerOptions);
            if (profile == null) return ShortHash(json);

            return Sha256Hex(BenchmarkScoringProfileService.CanonicalSignature(profile)).Substring(0, 12);
        }
        catch (JsonException)
        {
            return ShortHash(json);
        }
        catch (NotSupportedException)
        {
            return ShortHash(json);
        }
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
