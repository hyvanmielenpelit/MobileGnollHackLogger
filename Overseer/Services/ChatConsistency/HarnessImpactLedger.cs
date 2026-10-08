namespace Overseer.Services.ChatConsistency;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Overseer.Services.Benchmarking;

/// <summary>
/// What a benchmark harness version changed, by what the change breaks in a comparison over time.
///
/// <para>The members fall into two groups that a chat-consistency analysis treats oppositely.
/// <see cref="CandidateInput"/> changes what the chat <i>is</i>: it never excludes data and becomes a
/// dated event on the timeline instead. Every other member except <see cref="ReportingOnly"/> changes
/// how the chat is <i>measured</i>, and must be bridged or segmented.</para>
/// </summary>
[Flags]
public enum HarnessImpact
{
    /// <summary>Nothing changed between the two versions.</summary>
    None = 0,

    /// <summary>
    /// What the candidate is sent or allowed: system prompt, tool output, tool guides, budgets,
    /// timeouts, request parameters. Changes behavior, quality and latency; an Overseer event, not a
    /// measurement change.
    /// </summary>
    CandidateInput = 1,

    /// <summary>How candidate time is measured. Breaks legacy latency measures only.</summary>
    CandidateTiming = 2,

    /// <summary>How answers are graded. Breaks native quality, not quality from one common grader.</summary>
    Grading = 4,

    /// <summary>How grades become scores. Breaks native scores; a rescore under one profile resolves it.</summary>
    Scoring = 8,

    /// <summary>Reports, UI and storage only. Breaks nothing.</summary>
    ReportingOnly = 16,

    /// <summary>
    /// How candidate tokens or cost are counted from what the provider reported. Breaks the work and
    /// cost measures; repricing with one rate card does not resolve it.
    /// </summary>
    CandidateAccounting = 32
}

/// <summary>
/// One harness version's classification.
/// </summary>
/// <param name="Version">The harness version, as <see cref="BenchmarkAssessmentPrompt.HarnessVersion"/> renders it.</param>
/// <param name="Impact">What this version changed relative to the version before it.</param>
/// <param name="Summary">One line naming the changes the classification rests on.</param>
public sealed record HarnessLedgerEntry(string Version, HarnessImpact Impact, string Summary)
{
    /// <summary>
    /// What may differ between two runs that carry this same stamp, because the stamp was not moved
    /// when the harness was. <see cref="HarnessImpact.None"/> for a version whose stamp is exact.
    /// </summary>
    public HarnessImpact SameStampImpact { get; init; } = HarnessImpact.None;

    /// <summary>
    /// True when the changelog was too terse to classify the version exactly and flags were added
    /// conservatively.
    /// </summary>
    public bool IsConservative { get; init; }

    /// <summary>The version as an integer.</summary>
    public int Number => int.Parse(Version, NumberStyles.None, CultureInfo.InvariantCulture);
}

/// <summary>
/// The classified harness changelog: for every harness version from 1 to
/// <see cref="BenchmarkAssessmentPrompt.HarnessVersion"/>, what it changed in the terms of
/// <see cref="HarnessImpact"/>. Pure lookup; no I/O.
///
/// <para>The classification is read from the changelog on
/// <see cref="BenchmarkAssessmentPrompt.HarnessVersion"/> and from <c>docs/overseer/ai-benchmark.md</c>,
/// and is conservative: a version that mixes kinds carries every flag that applies, a change to
/// tool output counts as <see cref="HarnessImpact.CandidateInput"/> even when no tool guide moved,
/// and a version that cannot be told apart carries every measurement flag. An unclassified version
/// resolves to <see cref="Unclassified"/>, so a harness bump that is not entered here is treated as
/// changing everything until it is.</para>
/// </summary>
public static class HarnessImpactLedger
{
    /// <summary>
    /// The impact assumed for a version that is unknown, unparseable or not yet classified: every
    /// flag except <see cref="HarnessImpact.ReportingOnly"/>.
    /// </summary>
    public const HarnessImpact Unclassified =
        HarnessImpact.CandidateInput
        | HarnessImpact.CandidateTiming
        | HarnessImpact.Grading
        | HarnessImpact.Scoring
        | HarnessImpact.CandidateAccounting;

    private const HarnessImpact CI = HarnessImpact.CandidateInput;
    private const HarnessImpact CT = HarnessImpact.CandidateTiming;
    private const HarnessImpact G = HarnessImpact.Grading;
    private const HarnessImpact S = HarnessImpact.Scoring;
    private const HarnessImpact R = HarnessImpact.ReportingOnly;
    private const HarnessImpact CA = HarnessImpact.CandidateAccounting;

    private static readonly IReadOnlyList<HarnessLedgerEntry> Table = BuildTable();

    private static readonly IReadOnlyDictionary<int, HarnessLedgerEntry> ByNumber =
        Table.ToDictionary(e => e.Number);

    /// <summary>The harness version this build runs under.</summary>
    public static string CurrentVersion => BenchmarkAssessmentPrompt.HarnessVersion;

    /// <summary>Every classified version, in increasing numeric order.</summary>
    public static IReadOnlyList<HarnessLedgerEntry> Entries => Table;

    /// <summary>True when <paramref name="version"/> is a canonical version number with an entry.</summary>
    public static bool IsClassified(string version) => TryGetEntry(version, out _);

    /// <summary>The entry for <paramref name="version"/>, or null when it is not classified.</summary>
    public static HarnessLedgerEntry? EntryOf(string? version)
        => TryGetEntry(version, out var entry) ? entry : null;

    /// <summary>
    /// What <paramref name="version"/> changed relative to the version before it;
    /// <see cref="Unclassified"/> for a null, unparseable or unclassified version.
    /// </summary>
    public static HarnessImpact ImpactOf(string? version)
        => TryGetEntry(version, out var entry) ? entry.Impact : Unclassified;

    /// <summary>
    /// What may differ between a run stamped <paramref name="from"/> and one stamped
    /// <paramref name="to"/>: the union of <see cref="HarnessLedgerEntry.Impact"/> over every version
    /// v with lower &lt; v &lt;= higher in numeric order, together with the
    /// <see cref="HarnessLedgerEntry.SameStampImpact"/> of both ends. The order of the arguments does
    /// not matter. Equal versions yield their same-stamp impact, which is
    /// <see cref="HarnessImpact.None"/> for every exact stamp; either version unknown, or an
    /// unclassified version between them, yields <see cref="Unclassified"/>.
    /// </summary>
    public static HarnessImpact ImpactBetween(string? from, string? to)
    {
        if (!TryGetEntry(from, out var a) || !TryGetEntry(to, out var b))
        {
            return Unclassified;
        }

        if (a.Number == b.Number)
        {
            return a.SameStampImpact;
        }

        var (lower, higher) = a.Number < b.Number ? (a, b) : (b, a);
        HarnessImpact impact = lower.SameStampImpact | higher.SameStampImpact;
        for (int v = lower.Number + 1; v <= higher.Number; v++)
        {
            impact |= ByNumber.TryGetValue(v, out var between) ? between.Impact : Unclassified;
        }

        return impact;
    }

    private static bool TryGetEntry(string? version, out HarnessLedgerEntry entry)
    {
        entry = null!;
        if (!TryParseVersion(version, out int number)) return false;
        if (!ByNumber.TryGetValue(number, out var found)) return false;

        entry = found;
        return true;
    }

    /// <summary>
    /// A version is canonical when it is a positive invariant integer with no sign, padding or
    /// leading zero, which is how <see cref="BenchmarkAssessmentPrompt.HarnessVersion"/> renders.
    /// </summary>
    private static bool TryParseVersion(string? version, out int number)
    {
        number = 0;
        if (string.IsNullOrWhiteSpace(version)) return false;

        string trimmed = version.Trim();
        if (!int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out number)) return false;

        return number > 0
            && string.Equals(number.ToString(CultureInfo.InvariantCulture), trimmed, StringComparison.Ordinal);
    }

    private static IReadOnlyList<HarnessLedgerEntry> BuildTable()
    {
        var table = new List<HarnessLedgerEntry>
        {
            new("1", Unclassified,
                "Baseline harness with no recorded changelog and no predecessor.")
                { IsConservative = true },
            new("2", CI | CT | G | S,
                "Per-question tool budget of 25 calls; model-attributable timing; artifacts scrubbed before grading; turn duration removed from the assessor prompt; scoring method 3."),
            new("3", CI | G | S,
                "Per-difficulty-band tool call budgets; recovered artifacts classified apart from transport defects; executed and blocked tool calls reported apart.")
                { IsConservative = true },
            new("4", G | S,
                "Critical error needs a verbatim quote (scoring method 5); deduction evidence; optional second-opinion pass; per-question assessor usage recorded."),
            new("5", CI | G,
                "Four banded per-question caps including the timeout; pre-tool visible text moved to the thought channel; wider narration scrubbing of the graded answer."),
            new("6", G,
                "Narration strip steps over unrecognized openers and orphan tokens before grading; removal count persisted; report annotations."),
            new("7", G | S,
                "Unadjudicable claims recorded instead of deducted (scoring method 6); contested verdicts routed to a second reader; second-opinion modes; calibration runs."),
            new("8", CI | G,
                "Game snapshots: the board reaches the candidate and the graders; AI-generated questions grounded in the board."),
            new("9", G | S,
                "A deduction below level 6 must name its defect (scoring method 7); unevidenced deductions flagged; claim verifier role introduced."),
            new("10", G,
                "Unverified-grounded deductions flagged; claim verifier prompt moved to the user turn; stage failures and live progress reported."),
            new("11", CI | G,
                "Scope-aware budget refusal and remaining-budget warning in tool results; omission is never an Accuracy deduction; blind second opinions; verification before the trigger cascade."),
            new("12", CI | G,
                "Heading-scoped wiki_search snippets; candidate prompt options and Response Style recorded; blind backfill, shared JSON extractor and substitution guard in grading.")
                { SameStampImpact = CI | CA | G | S },
            new("13", CI | CA,
                "Per-question tool, iteration and model-call caps flattened to the Advanced figures; long-context, service-tier and scheduled pricing in candidate costing; model calls reported."),
            new("14", G | S,
                "Completeness scope becomes a grading rule (scoring method 8); synthesis divergence detection; FlaggedPlusSample second opinions; replicate sets."),
            new("15", CI,
                "get_item_stats macro parsing repaired; per-role grader cost tracking and an Anthropic cache-creation costing fix; HarnessVersion constant re-synchronized."),
            new("16", R,
                "Run records the GnollHack wiki and source Git HEADs as provenance."),
            new("17", R,
                "Every tool call persisted with arguments, result and timings; nothing the candidate sees changed."),
            new("18", CI | G | S | CA,
                "wiki_search and nethack_wiki_search result caps, miss payloads and two guides; advisory grading limited to gradeable answers; no speed score for non-gradeable answers; Gemini usage counted once per call.")
                { SameStampImpact = CA },
            new("19", CI | G,
                "Two source-tool contracts and guides changed; critical-error quote dispatched to the claim verifier; grading rule that a rubric omission is not an invention."),
            new("20", CI | G,
                "Three tool guides changed; out-of-rubric Accuracy deduction checked by the claim verifier; verifier rule 3a."),
            new("21", CI | CT | G | S,
                "Terminal provider failures withhold the indices and skip grading; one provider retry policy for every provider, with an unrecorded effect on candidate timing.")
                { IsConservative = true },
            new("22", CI | G,
                "wiki_search clamps max_results and the definition matcher finds more definitions; unevidenced-deduction detection reaches level 5; re-run harness recorded."),
            new("23", CI | G,
                "source_code_view stops at a whole line with a resume hint; get_function_definition falls back to any kind; unevidenced-deduction detector reads named defects."),
            new("24", CI | G,
                "wiki_search stems English and reports match counts; grading preamble moved to a cacheable system segment; assessed difficulties become a comparability key."),
            new("25", CI | G,
                "search_definitions miss carries an occurrence probe; assessor preamble rule on out-of-rubric claims; detector vocabulary; synthesis receives supported claims."),
            new("26", CI | G,
                "Candidate message carries the no-greet instruction; monster_lookup and item_lookup return an exact-title article alone; detectors, verifier basis and difficulty prompt changed."),
            new("27", CI | G,
                "wiki_search category filter and nethack_wiki_view resolution fixed; the four assessor levels are required; DimensionOutlier routed to a second reader."),
            new("28", CI | G | S,
                "AD_SAMU flag description changed outside ToolGuidesSha256; verifier rule on resistance magnitude; speed model recalibrated into its own SpeedCalibration key."),
            new("29", CI | G,
                "Production system prompt delivered as the first system message, so OpenAI candidates receive the prompt and Google and Anthropic candidates the board; delivery probes; verifier receives the board."),
            new("30", CI | G,
                "wiki_search always returns an article's lead block; every grading path receives the board; contested answers re-graded with the verifier's findings."),
            new("31", CI | G | S,
                "Scoring method 11; source_code_search filtered-miss hint, wiki_search ranking, flag unions and two guides; accused sentences sent to the claim verifier."),
            new("32", G,
                "Every grading role reads the whole board ahead of the question; verifier rules 3d and 3e; accused-sentence extraction widened."),
            new("33", G | S,
                "Scoring method 12; the claim verifier tests the assessor's own sentences; re-run and board-format provenance."),
            new("34", CI | G | CA,
                "Source tools append a get_function_definition pointer; Gemini output tokens include thinking tokens; citation-liveness and flag rules; verifier rules 3f to 3h."),
            new("35", CI | G,
                "Source tools mark lines inside #if 0, wiki_search guide and _policy.md changed; flag detector vocabulary; citation notes for unindexed files."),
            new("36", CI | G,
                "A categorized wiki_search names its best match outside the category; citation notes for file-only and definition-line citations; verifier rule 3i."),
            new("37", CI | G,
                "wiki_search returns a short article whole and drops the outside-category line; synthesis divergence check; verifier rule 3j."),
            new("38", CI | G,
                "item_lookup searches the item and artifact paths; minified get_item_stats keeps the failure reason; the claim verifier judges the charged part."),
            new("39", CI | G,
                "wiki_view names what it cuts and lookup headers name the path; contested verdicts read from the comment and evidence; macro citation notes; verifier rule 3l."),
            new("40", G | S,
                "Two-family assessor panel whose published score is the panel mean; the second opinion becomes a reference reader; structured synthesis findings."),
            new("41", CI | G,
                "wiki_view section-miss headings and [Not reachable] notes with five guides; omission detector and citation notes; panel union manifest; verifier budget per item."),
            new("42", G,
                "Assessor prompt: the rubric's SOURCE line is provenance; definition-line citations; fabrication qualifiers; synthesis list attribution; knowledge-base topic guard."),
            new("43", CI | G,
                "get_item_stats resolves the unique item named '... of <name>' and its guide changed; pronoun context for claims; verifier rule 3m; union manifest de-duplication."),
            new("44", CI | G,
                "Source-code-reference prompt option recorded and disallowed by default; stats tools trim their inputs; accusedBy and suspectedBy attribution."),
            new("45", G | S,
                "Scoring method 13: a critical error comes only from the rubric or the board; notAttempted defined; synthesis states how critical errors were resolved."),
            new("46", G,
                "Rubric-charged Accuracy deductions sent to the claim verifier and RubricContradictedBySource raised; battery report fixes."),
            new("47", CI | G,
                "Source tool output: DLLEXPORT functions live, search_definitions miss text and definition pointer wording; table-header quotes never anchor; verifier rules 3n and 3o."),
            new("48", R,
                "Panel verification-cleared sensitivity reported; corpus index fingerprints recorded as provenance."),
            new("49", CI | G | S,
                "Scoring method 14; _policy.md knowledge-base scope and get_knowledge_article guide changed; claim verifier batches lookups and records per-call usage."),
            new("50", CI | G,
                "get_item_stats drops trailing words after a unique item name; the claim verifier's parse retry is a separate request with its own budget."),
            new("51", CI | G,
                "_policy.md retrieved-figures sentence removed; get_item_stats name cleanup; claim verifier retry recorded and asked for every item."),
            new("52", CI | G,
                "nethack_wiki_view headings notice for an over-cap article; forced-final instruction when the tools run out; one-line macro bodies count in citation liveness."),
            new("53", G,
                "A member whose charge the verifier upheld is never verification-cleared; the synthesis is told the verdicts on its charges; report fixes."),
            new("54", CI | G,
                "Graders told when source references are disallowed; get_constants reads brace-on-next-line enums and the source miss probe is whole-word; streaming-rate bounds and report fixes."),
            new("55", G,
                "Source-location grading rule names files and paths; claim verifier retries a provider error once; 'inverts' counts as a stated defect and a charge."),
        };

        return table.OrderBy(e => e.Number).ToList();
    }
}
