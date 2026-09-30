namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using MobileGnollHackLogger.Data;

/// <summary>
/// Cross-run judge-family diagnostics for a model comparison whose runs were all graded by one
/// two-family assessor panel. <see cref="Applicable"/> is false, with every list empty and every
/// label null, when the runs do not share one panel; <see cref="NotApplicableReason"/> says why.
/// </summary>
public sealed record BenchmarkPanelDiagnosticsResult(
    bool Applicable,
    string? NotApplicableReason,
    string? MemberALabel,
    string? MemberAProvider,
    string? MemberBLabel,
    string? MemberBProvider,
    string? ReferenceLabel,
    string? ReferenceProvider,
    IReadOnlyList<BenchmarkPanelEntryIndices> Entries,
    IReadOnlyList<BenchmarkPanelRankedPair> JudgeDependentPairs,
    IReadOnlyList<BenchmarkPanelRankedPair> ReferenceDependentPairs,
    IReadOnlyList<BenchmarkPanelFamilyGap> FamilyGaps,
    IReadOnlyList<BenchmarkPanelAuditCell> AccusationAudit,
    IReadOnlyList<BenchmarkPanelMemberAuditSummary> AuditSummaries,
    IReadOnlyList<string> Caveats);

/// <summary>
/// One comparison entry's difficulty-weighted indices under member A alone, member B alone, the
/// panel and the reference reader, pooled over the entry's runs, with the entry's rank under each.
/// </summary>
public sealed record BenchmarkPanelEntryIndices(
    string EntryKey,
    string EntryLabel,
    string? CandidateProvider,
    int? MemberAIndex,
    int? MemberBIndex,
    int? PanelIndex,
    int? ReferenceIndex,
    int? RankA,
    int? RankB,
    int? RankPanel,
    int? RankReference);

/// <summary>Two entries whose order depends on which grader is read; the first is the one the first-named grader ranks higher.</summary>
public sealed record BenchmarkPanelRankedPair(
    string FirstEntryKey,
    string FirstEntryLabel,
    string SecondEntryKey,
    string SecondEntryLabel,
    string Description);

/// <summary>A mean with its 95 % confidence interval; all three are null when the data are insufficient.</summary>
public sealed record BenchmarkPanelEstimate(double? Value, double? CiLow, double? CiHigh);

/// <summary>
/// The mean per-question quality difference <c>Provider1 − Provider2</c> between two candidate
/// families under each grader, over the questions both families answered at the same item revision.
/// The interaction contrast and the asymmetry estimate are present only on the row whose providers
/// are the two members' own (<see cref="IsMemberProviderPair"/>).
/// </summary>
public sealed record BenchmarkPanelFamilyGap(
    string Provider1,
    string Provider2,
    int PairedQuestionCount,
    bool InsufficientData,
    bool IsMemberProviderPair,
    BenchmarkPanelEstimate GapA,
    BenchmarkPanelEstimate GapB,
    BenchmarkPanelEstimate GapPanel,
    BenchmarkPanelEstimate? GapRef,
    BenchmarkPanelEstimate? InteractionContrast,
    string? InteractionContrastLabel,
    BenchmarkPanelEstimate? AsymmetryEstimate,
    string? AsymmetryEstimateLabel);

/// <summary>
/// One member's charges against one candidate family and how the claim verifier ruled on them.
/// <see cref="OverturnRate"/> is <c>Overturned / (Overturned + Upheld)</c>: indeterminate verdicts
/// are counted but carry no ruling.
/// </summary>
public sealed record BenchmarkPanelAuditCell(
    string Member,
    string MemberProvider,
    string CandidateProvider,
    bool SameFamily,
    int Charges,
    int Overturned,
    int Upheld,
    int Indeterminate,
    double? OverturnRate);

/// <summary>
/// A member's overturn rate on other-family candidates minus its rate on same-family candidates;
/// positive when its charges against the other family fail more often.
/// </summary>
public sealed record BenchmarkPanelMemberAuditSummary(string Member, string MemberProvider, double? FamilyOverturnGap);

/// <summary>
/// One panel member's spread over the answers both members scored in one run: the range and sample
/// standard deviation of its quality scores, and its most frequent tuple of criterion levels
/// (accuracy, completeness, conciseness, readability) with the number of answers that had it.
/// <see cref="AnswerCount"/> is the number of answers both members scored. The range is null without
/// a score, the deviation below two scores, and the modal levels when no answer records all four.
/// </summary>
public sealed record BenchmarkPanelMemberSpread(
    string Member,
    int AnswerCount,
    int? MinQualityScore,
    int? MaxQualityScore,
    double? StandardDeviation,
    IReadOnlyList<int>? ModalLevels,
    int ModalLevelsCount);

/// <summary>
/// Computes <see cref="BenchmarkPanelDiagnosticsResult"/> over the entries of a model comparison.
/// Pure: no I/O, no model call, and no score is changed. "Family" means provider, compared trimmed
/// and case-insensitively as <see cref="BenchmarkComplianceGuard.IsSameProvider(string?, string?)"/>
/// compares it.
/// </summary>
public static class BenchmarkPanelDiagnostics
{
    public const string MemberA = "A";
    public const string MemberB = "B";

    /// <summary>Below this many paired questions a family gap carries no estimate.</summary>
    public const int MinPairedQuestions = 5;

    /// <summary>Below this many ruled charges on either side a member's family overturn gap is withheld.</summary>
    public const int MinRuledChargesPerSide = 10;

    public const string InteractionContrastLabel =
        "total same-family preference of both members; does not measure the bias of the panel mean";

    public const string AsymmetryEstimateLabel =
        "estimated panel bias; valid only if the reference reader is neutral between these two families";

    private const string ManualTrigger = "Manual";

    public static readonly IReadOnlyList<string> Caveats = new[]
    {
        "The published score is the panel mean. The member A and member B indices are each member's difficulty-weighted index over the same answers the panel scored; they are diagnostics, not alternative results.",
        "The reference reader's index is shown only for an entry where it graded every answer the panel scored. The reference reader never scores.",
        "A judge-dependent pair is two entries that member A and member B rank in opposite order. The panel's order for such a pair rests on the balance between the members, not on their agreement. A tie under one grader is not counted as a reversal.",
        "A family gap is the mean per-question difference between two candidate families under one grader, over questions both families answered at the same item revision. On its own it mixes a real capability difference with grader preference; only the contrasts between graders separate the two.",
        "For the pair of the members' own providers, Provider 1 is member A's provider and Provider 2 is member B's, so a positive interaction contrast means the members together favor their own families. Other pairs are ordered alphabetically.",
        "The interaction contrast (gap under A minus gap under B) measures the total same-family preference of both members. It does not measure the bias of the panel mean, which cancels only to the extent that the two members' preferences are equal.",
        "The asymmetry estimate (gap under the panel minus gap under the reference reader) is valid only if the reference reader is neutral between the two families. A reference reader with a preference of its own shifts the estimate by that preference.",
        "The asymmetry estimate also assumes the reference reader spreads its scores like the panel. A reader that compresses score differences — a lenient reader bunched near the top — reads a smaller gap, and the estimate then carries part of the real capability gap as well as the panel's bias. Compare the reader's index spread with the panel's before reading it.",
        "The accusation audit assumes the claim verifier is neutral between the families, and it measures only the charges the verifier could check: indeterminate verdicts are counted but excluded from the overturn rate, and a deduction that raised no checkable charge is invisible to it.",
        "Small samples: gap intervals are 95 % Student t intervals over paired questions and are withheld below 5 pairs; a family overturn gap is withheld below 10 ruled charges on either side. Questions, not runs, are the sampling unit: repeated runs average within a question and add no pairs."
    };

    private static readonly JsonSerializerOptions ClaimVerificationJsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Below this sample standard deviation of quality scores, in points, a member's range is narrow.</summary>
    public const double NarrowSpreadStandardDeviation = 8.0;

    // --- Member spread --------------------------------------------------------------------------

    /// <summary>
    /// <paramref name="member"/>'s <see cref="BenchmarkPanelMemberSpread"/> over
    /// <paramref name="bothScored"/>, the answers of one run both members scored, in question order.
    /// Member A's scores and levels are the answer's own columns, member B's its co-assessment. The
    /// deviation is the sample one (<see cref="BenchmarkGroupStatistics.SampleStandardDeviation"/>).
    /// Of several equally frequent level tuples the one first reached in question order is modal.
    /// </summary>
    public static BenchmarkPanelMemberSpread MemberSpread(IReadOnlyList<BenchmarkRunAnswer> bothScored, BenchmarkPanelMember member)
    {
        var answers = bothScored ?? Array.Empty<BenchmarkRunAnswer>();
        var scores = answers
            .Select(a => member == BenchmarkPanelMember.B ? a.CoAssessmentQualityScore : a.QualityScore)
            .Where(s => s.HasValue)
            .Select(s => s!.Value)
            .ToList();

        var modal = answers
            .Select(a => BenchmarkVerdictView.For(a, member))
            .Where(v => v != null)
            .Select(v => (Accuracy: v!.AccuracyLevel, Completeness: v.CompletenessLevel, Conciseness: v.ConcisenessLevel, Readability: v.ReadabilityLevel))
            .GroupBy(t => t)
            .OrderByDescending(g => g.Count())
            .FirstOrDefault();

        return new BenchmarkPanelMemberSpread(
            Member: BenchmarkVerdictView.LabelOf(member),
            AnswerCount: answers.Count,
            MinQualityScore: scores.Count > 0 ? scores.Min() : null,
            MaxQualityScore: scores.Count > 0 ? scores.Max() : null,
            StandardDeviation: BenchmarkGroupStatistics.SampleStandardDeviation(scores.Select(s => (double)s).ToList()),
            ModalLevels: modal == null
                ? null
                : new[] { modal.Key.Accuracy, modal.Key.Completeness, modal.Key.Conciseness, modal.Key.Readability },
            ModalLevelsCount: modal?.Count() ?? 0);
    }

    /// <summary>True when both members' standard deviations are recorded and below <see cref="NarrowSpreadStandardDeviation"/>.</summary>
    public static bool IsNarrowSpread(BenchmarkPanelMemberSpread memberA, BenchmarkPanelMemberSpread memberB)
        => memberA.StandardDeviation < NarrowSpreadStandardDeviation
           && memberB.StandardDeviation < NarrowSpreadStandardDeviation;

    public static BenchmarkPanelDiagnosticsResult Compute(
        IReadOnlyList<(string EntryKey, string EntryLabel, IReadOnlyList<BenchmarkRun> Runs)> entries)
    {
        var entryList = (entries ?? Array.Empty<(string, string, IReadOnlyList<BenchmarkRun>)>())
            .Select(e => (EntryKey: e.EntryKey ?? string.Empty, EntryLabel: e.EntryLabel ?? string.Empty,
                Runs: (IReadOnlyList<BenchmarkRun>)(e.Runs ?? Array.Empty<BenchmarkRun>()).Where(r => r != null).ToList()))
            .ToList();

        var runs = entryList.SelectMany(e => e.Runs).ToList();
        if (runs.Count == 0)
        {
            return NotApplicable("The comparison holds no runs.");
        }

        var nonPanel = runs.Where(r => !BenchmarkRunFinalizer.IsPanelRun(r)).Select(r => r.Id).OrderBy(id => id).ToList();
        if (nonPanel.Count > 0)
        {
            return NotApplicable(
                $"Every run must be a panel run; {Plural(nonPanel.Count, "run")} ({IdList(nonPanel)}) had a single assessor.");
        }

        var first = runs.OrderBy(r => r.Id).First();
        string? gateFailure =
            GateFailure(runs, "member A", r => GraderFingerprint(r.AssessorModelSnapshot, r.AssessorEffectiveMaxOutputTokens))
            ?? GateFailure(runs, "member B", r => GraderFingerprint(r.CoAssessorModelSnapshot, r.CoAssessorEffectiveMaxOutputTokens))
            ?? GateFailure(
                runs.Where(r => r.SecondOpinionAssessorModelSnapshot != null).ToList(),
                "the reference reader",
                r => GraderFingerprint(r.SecondOpinionAssessorModelSnapshot, r.SecondOpinionEffectiveMaxOutputTokens));
        if (gateFailure != null)
        {
            return NotApplicable(gateFailure);
        }

        var entryProviders = entryList.Select(e => CandidateProviderOf(e.Runs)).ToList();
        if (entryProviders.All(p => p == null))
        {
            return NotApplicable("No entry has a candidate provider that every one of its runs shares.");
        }

        string? providerA = Trimmed(first.AssessorModelSnapshot?.Provider);
        string? providerB = Trimmed(first.CoAssessorModelSnapshot?.Provider);
        var referenceRun = runs.Where(r => r.SecondOpinionAssessorModelSnapshot != null).OrderBy(r => r.Id).FirstOrDefault();
        bool referencePresent = referenceRun != null;

        var indexRows = BuildEntryIndices(entryList, entryProviders);
        var orderedRows = indexRows
            .OrderBy(r => r.RankPanel ?? int.MaxValue)
            .ThenBy(r => r.EntryLabel, StringComparer.Ordinal)
            .ThenBy(r => r.EntryKey, StringComparer.Ordinal)
            .ToList();

        var judgePairs = RankReversals(orderedRows, r => r.MemberAIndex, r => r.MemberBIndex, "Member A", "member B");
        var referencePairs = RankReversals(orderedRows, r => r.PanelIndex, r => r.ReferenceIndex, "The panel", "the reference reader");

        var familyGaps = BuildFamilyGaps(entryList, entryProviders, providerA, providerB, referencePresent);
        var (auditCells, auditSummaries) = BuildAccusationAudit(runs, entryProviders, providerA, providerB);

        return new BenchmarkPanelDiagnosticsResult(
            Applicable: true,
            NotApplicableReason: null,
            MemberALabel: first.AssessorModelSnapshot.Label(),
            MemberAProvider: providerA,
            MemberBLabel: first.CoAssessorModelSnapshot.Label(),
            MemberBProvider: providerB,
            ReferenceLabel: referenceRun?.SecondOpinionAssessorModelSnapshot.Label(),
            ReferenceProvider: Trimmed(referenceRun?.SecondOpinionAssessorModelSnapshot?.Provider),
            Entries: orderedRows,
            JudgeDependentPairs: judgePairs,
            ReferenceDependentPairs: referencePairs,
            FamilyGaps: familyGaps,
            AccusationAudit: auditCells,
            AuditSummaries: auditSummaries,
            Caveats: Caveats);
    }

    // --- Gate -----------------------------------------------------------------------------------

    private static BenchmarkPanelDiagnosticsResult NotApplicable(string reason)
        => new(
            Applicable: false,
            NotApplicableReason: reason,
            MemberALabel: null,
            MemberAProvider: null,
            MemberBLabel: null,
            MemberBProvider: null,
            ReferenceLabel: null,
            ReferenceProvider: null,
            Entries: Array.Empty<BenchmarkPanelEntryIndices>(),
            JudgeDependentPairs: Array.Empty<BenchmarkPanelRankedPair>(),
            ReferenceDependentPairs: Array.Empty<BenchmarkPanelRankedPair>(),
            FamilyGaps: Array.Empty<BenchmarkPanelFamilyGap>(),
            AccusationAudit: Array.Empty<BenchmarkPanelAuditCell>(),
            AuditSummaries: Array.Empty<BenchmarkPanelMemberAuditSummary>(),
            Caveats: Array.Empty<string>());

    /// <summary>
    /// Null when every run's grader fingerprint for the role is identical; otherwise the reason,
    /// naming the lowest run id of the first fingerprint and of the first one that differs from it.
    /// </summary>
    private static string? GateFailure(IReadOnlyList<BenchmarkRun> runs, string role, Func<BenchmarkRun, string> fingerprint)
    {
        var ordered = runs.OrderBy(r => r.Id).ToList();
        if (ordered.Count < 2) return null;

        string expected = fingerprint(ordered[0]);
        var differing = ordered.FirstOrDefault(r => !string.Equals(fingerprint(r), expected, StringComparison.Ordinal));
        return differing == null
            ? null
            : $"The runs were not graded by the same panel: {role} of run {differing.Id.ToString(CultureInfo.InvariantCulture)} "
              + $"differs from that of run {ordered[0].Id.ToString(CultureInfo.InvariantCulture)} "
              + "(provider, model, thinking, reasoning mode, reasoning summary, service tier, output cap or endpoint).";
    }

    /// <summary>
    /// The fields <see cref="BenchmarkComparabilityKey"/> signs for a grader role: provider, model,
    /// thinking, reasoning mode and summary, service tier, the output cap its calls sent, and the
    /// endpoint fingerprint. A blank field on an incomplete snapshot renders differently from a
    /// blank one on a complete snapshot, as in the key.
    /// </summary>
    private static string GraderFingerprint(SystemAiConfigurationSnapshot? grader, int? effectiveMaxOutputTokens)
    {
        if (grader == null) return "(absent)";

        string Field(string? value)
            => string.IsNullOrWhiteSpace(value) ? (grader.IsComplete ? "(none)" : "(not recorded)") : value.Trim();

        return string.Join("\n", new[]
        {
            Field(grader.Provider),
            Field(grader.ModelId),
            Field(grader.ThinkingLevel),
            Field(grader.ReasoningMode),
            Field(grader.ReasoningSummary),
            Field(grader.ServiceTier),
            effectiveMaxOutputTokens.HasValue
                ? effectiveMaxOutputTokens.Value.ToString(CultureInfo.InvariantCulture)
                : "(not recorded)",
            SystemAiConfigurationSnapshotStore.EndpointFingerprint(grader)
        });
    }

    /// <summary>The provider every run of the entry tested; null when the runs disagree or none records one.</summary>
    private static string? CandidateProviderOf(IReadOnlyList<BenchmarkRun> runs)
    {
        var providers = runs
            .Select(r => Trimmed(r.TestedModelSnapshot?.Provider))
            .ToList();
        if (providers.Count == 0 || providers.Any(p => p == null)) return null;

        var distinct = providers.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return distinct.Count == 1 ? CanonicalProvider(providers.Select(p => p!)) : null;
    }

    // --- Indices and rankings -------------------------------------------------------------------

    /// <summary>
    /// The answers every figure is computed over: those the run finalizer counts toward the Quality
    /// Index that both members scored and that carry a panel score, the population of
    /// <see cref="BenchmarkRunFinalizer.ApplyPanelStatistics"/>. A run's terminal failures are
    /// outside it already; its indices are not withheld here, as the comparison's own pooled
    /// statistics do not withhold them.
    /// </summary>
    private static IEnumerable<BenchmarkRunAnswer> PanelScoredAnswers(BenchmarkRun run)
        => (run.Answers ?? new List<BenchmarkRunAnswer>())
            .Where(BenchmarkRunFinalizer.CountsTowardQualityIndex)
            .Where(a => a.AssessmentStatus == BenchmarkAssessmentStatus.Scored
                        && a.CoAssessmentStatus == BenchmarkAssessmentStatus.Scored
                        && a.QualityScore.HasValue
                        && a.CoAssessmentQualityScore.HasValue
                        && BenchmarkScoring.IndexQuality(a, true).HasValue);

    private static int DifficultyOf(BenchmarkRunAnswer answer)
        => answer.AssessedDifficulty ?? BenchmarkRunFinalizer.FallbackDifficulty(answer.Difficulty);

    /// <summary>The reference reader's verdict on the answer; a manual trial verdict is not the reference reader's.</summary>
    private static int? ReferenceScoreOf(BenchmarkRunAnswer answer)
        => string.Equals(answer.SecondOpinionTrigger, ManualTrigger, StringComparison.Ordinal)
            ? null
            : answer.SecondOpinionQualityScore;

    private static List<BenchmarkPanelEntryIndices> BuildEntryIndices(
        IReadOnlyList<(string EntryKey, string EntryLabel, IReadOnlyList<BenchmarkRun> Runs)> entries,
        IReadOnlyList<string?> entryProviders)
    {
        var raw = entries.Select((e, entryIndex) =>
        {
            var answers = e.Runs.SelectMany(PanelScoredAnswers).ToList();

            int? Index(Func<BenchmarkRunAnswer, double?> score)
                => BenchmarkScoring.QualityIndex(answers.Select(a => (score(a), (int?)DifficultyOf(a))).ToList());

            int? panel = Index(a => BenchmarkScoring.IndexQuality(a, true));
            int? memberA = Index(a => a.QualityScore);
            int? memberB = Index(a => a.CoAssessmentQualityScore);
            int? reference = answers.Count > 0 && answers.All(a => ReferenceScoreOf(a).HasValue)
                ? Index(a => ReferenceScoreOf(a))
                : null;

            return (e.EntryKey, e.EntryLabel, Provider: entryProviders[entryIndex], memberA, memberB, panel, reference);
        }).ToList();

        var ranksA = Ranks(raw.Select(r => r.memberA).ToList());
        var ranksB = Ranks(raw.Select(r => r.memberB).ToList());
        var ranksPanel = Ranks(raw.Select(r => r.panel).ToList());
        var ranksReference = Ranks(raw.Select(r => r.reference).ToList());

        return raw.Select((r, i) => new BenchmarkPanelEntryIndices(
                r.EntryKey, r.EntryLabel, r.Provider,
                r.memberA, r.memberB, r.panel, r.reference,
                ranksA[i], ranksB[i], ranksPanel[i], ranksReference[i]))
            .ToList();
    }

    /// <summary>Competition ranks, highest index first: tied entries share a rank. Null for an entry without an index.</summary>
    private static List<int?> Ranks(IReadOnlyList<int?> values)
        => values
            .Select(v => v.HasValue ? 1 + values.Count(o => o.HasValue && o.Value > v.Value) : (int?)null)
            .ToList();

    /// <summary>
    /// Every pair of entries the two graders order strictly oppositely. The first entry of a pair is
    /// the one <paramref name="first"/> ranks higher; pairs follow the order of
    /// <paramref name="rows"/>.
    /// </summary>
    private static List<BenchmarkPanelRankedPair> RankReversals(
        IReadOnlyList<BenchmarkPanelEntryIndices> rows,
        Func<BenchmarkPanelEntryIndices, int?> first,
        Func<BenchmarkPanelEntryIndices, int?> second,
        string firstName,
        string secondName)
    {
        var pairs = new List<(int Order1, int Order2, BenchmarkPanelRankedPair Pair)>();

        for (int i = 0; i < rows.Count; i++)
        {
            for (int j = i + 1; j < rows.Count; j++)
            {
                if (first(rows[i]) is not int fi || first(rows[j]) is not int fj
                    || second(rows[i]) is not int si || second(rows[j]) is not int sj)
                {
                    continue;
                }

                int firstOrder = Math.Sign(fi - fj);
                int secondOrder = Math.Sign(si - sj);
                if (firstOrder == 0 || secondOrder == 0 || firstOrder == secondOrder) continue;

                var (high, low, highIndex, lowIndex) = firstOrder > 0 ? (rows[i], rows[j], i, j) : (rows[j], rows[i], j, i);
                string description =
                    $"{firstName} ranks {high.EntryLabel} above {low.EntryLabel} ({Format(first(high))} vs {Format(first(low))}); "
                    + $"{secondName} ranks {low.EntryLabel} above {high.EntryLabel} ({Format(second(low))} vs {Format(second(high))}).";

                pairs.Add((highIndex, lowIndex, new BenchmarkPanelRankedPair(
                    high.EntryKey, high.EntryLabel, low.EntryKey, low.EntryLabel, description)));
            }
        }

        return pairs
            .OrderBy(p => p.Order1)
            .ThenBy(p => p.Order2)
            .Select(p => p.Pair)
            .ToList();
    }

    // --- Family gaps ----------------------------------------------------------------------------

    /// <summary>One family's mean quality on one question under each grader; the reference mean is null unless it graded every answer.</summary>
    private sealed record QuestionMeans(double A, double B, double Panel, double? Reference);

    private static List<BenchmarkPanelFamilyGap> BuildFamilyGaps(
        IReadOnlyList<(string EntryKey, string EntryLabel, IReadOnlyList<BenchmarkRun> Runs)> entries,
        IReadOnlyList<string?> entryProviders,
        string? providerA,
        string? providerB,
        bool referencePresent)
    {
        var byProvider = entries
            .Select((e, entryIndex) => (e.Runs, Provider: entryProviders[entryIndex]))
            .Where(e => e.Provider != null)
            .GroupBy(e => e.Provider!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => CanonicalProvider(g.Select(e => e.Provider!)),
                g => MeansByQuestion(g.SelectMany(e => e.Runs)),
                StringComparer.OrdinalIgnoreCase);

        var providers = byProvider.Keys.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ThenBy(p => p, StringComparer.Ordinal).ToList();
        bool membersDistinct = providerA != null && providerB != null && !IsSameProvider(providerA, providerB);

        var rows = new List<BenchmarkPanelFamilyGap>();
        for (int i = 0; i < providers.Count; i++)
        {
            for (int j = i + 1; j < providers.Count; j++)
            {
                string p1 = providers[i];
                string p2 = providers[j];
                bool memberPair = membersDistinct
                    && ((IsSameProvider(p1, providerA) && IsSameProvider(p2, providerB))
                        || (IsSameProvider(p1, providerB) && IsSameProvider(p2, providerA)));
                if (memberPair && IsSameProvider(p1, providerB))
                {
                    (p1, p2) = (p2, p1);
                }

                rows.Add(FamilyGap(p1, p2, byProvider[p1], byProvider[p2], memberPair, referencePresent));
            }
        }

        return rows
            .OrderBy(r => r.IsMemberProviderPair ? 0 : 1)
            .ThenBy(r => r.Provider1, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Provider2, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Per question and item revision, the family's mean quality under each grader over its
    /// panel-scored answers. Answers without a question link or a recorded item revision cannot be
    /// paired and are left out.
    /// </summary>
    private static Dictionary<(long QuestionId, int ItemRevision), QuestionMeans> MeansByQuestion(IEnumerable<BenchmarkRun> runs)
    {
        return runs
            .SelectMany(PanelScoredAnswers)
            .Where(a => BenchmarkItemAnalysis.QuestionKey(a).HasValue && a.ItemRevisionUsed.HasValue)
            .GroupBy(a => (QuestionId: BenchmarkItemAnalysis.QuestionKey(a)!.Value, ItemRevision: a.ItemRevisionUsed!.Value))
            .ToDictionary(
                g => g.Key,
                g => new QuestionMeans(
                    g.Average(a => (double)a.QualityScore!.Value),
                    g.Average(a => (double)a.CoAssessmentQualityScore!.Value),
                    g.Average(a => BenchmarkScoring.IndexQuality(a, true)!.Value),
                    g.All(a => ReferenceScoreOf(a).HasValue) ? (double?)g.Average(a => (double)ReferenceScoreOf(a)!.Value) : null));
    }

    private static BenchmarkPanelFamilyGap FamilyGap(
        string provider1,
        string provider2,
        IReadOnlyDictionary<(long QuestionId, int ItemRevision), QuestionMeans> means1,
        IReadOnlyDictionary<(long QuestionId, int ItemRevision), QuestionMeans> means2,
        bool memberPair,
        bool referencePresent)
    {
        var paired = means1.Keys
            .Where(means2.ContainsKey)
            .OrderBy(k => k.QuestionId)
            .ThenBy(k => k.ItemRevision)
            .Select(k => (First: means1[k], Second: means2[k]))
            .ToList();

        var diffA = paired.Select(p => p.First.A - p.Second.A).ToList();
        var diffB = paired.Select(p => p.First.B - p.Second.B).ToList();
        var diffPanel = paired.Select(p => p.First.Panel - p.Second.Panel).ToList();

        var withReference = paired
            .Where(p => p.First.Reference.HasValue && p.Second.Reference.HasValue)
            .ToList();
        var diffReference = withReference
            .Select(p => p.First.Reference!.Value - p.Second.Reference!.Value)
            .ToList();
        var diffAsymmetry = withReference
            .Select(p => (p.First.Panel - p.Second.Panel) - (p.First.Reference!.Value - p.Second.Reference!.Value))
            .ToList();

        return new BenchmarkPanelFamilyGap(
            Provider1: provider1,
            Provider2: provider2,
            PairedQuestionCount: paired.Count,
            InsufficientData: paired.Count < MinPairedQuestions,
            IsMemberProviderPair: memberPair,
            GapA: Estimate(diffA),
            GapB: Estimate(diffB),
            GapPanel: Estimate(diffPanel),
            GapRef: referencePresent ? Estimate(diffReference) : null,
            InteractionContrast: memberPair ? Estimate(diffA.Zip(diffB, (a, b) => a - b).ToList()) : null,
            InteractionContrastLabel: memberPair ? InteractionContrastLabel : null,
            AsymmetryEstimate: memberPair && referencePresent ? Estimate(diffAsymmetry) : null,
            AsymmetryEstimateLabel: memberPair && referencePresent ? AsymmetryEstimateLabel : null);
    }

    /// <summary>
    /// The mean of paired differences with its 95 % Student t interval; all null below
    /// <see cref="MinPairedQuestions"/> differences.
    /// </summary>
    private static BenchmarkPanelEstimate Estimate(IReadOnlyList<double> differences)
    {
        if (differences.Count < MinPairedQuestions)
        {
            return new BenchmarkPanelEstimate(null, null, null);
        }

        double mean = differences.Average();
        double sd = BenchmarkGroupStatistics.SampleStandardDeviation(differences) ?? 0.0;
        double halfWidth = BenchmarkGroupStatistics.StudentTCritical95(differences.Count - 1) * sd / Math.Sqrt(differences.Count);
        return new BenchmarkPanelEstimate(mean, mean - halfWidth, mean + halfWidth);
    }

    // --- Accusation audit -----------------------------------------------------------------------

    private sealed class AuditTally
    {
        public int Charges;
        public int Overturned;
        public int Upheld;
        public int Indeterminate;
    }

    /// <summary>
    /// Every charge a member raised, by member and candidate provider, with the verifier's ruling.
    /// A charge is a verification item the member raised that accuses the answer (a critical-error
    /// quote, an accused sentence or a suspected-false claim) or states the basis of an out-of-rubric
    /// Accuracy deduction. An accusation is overturned when the verifier supports the accused
    /// sentence; a basis is overturned when the verifier refutes it, because the basis is the
    /// member's own statement. A charge counts for the members <see cref="ChargingMembers"/> names.
    /// </summary>
    private static (List<BenchmarkPanelAuditCell> Cells, List<BenchmarkPanelMemberAuditSummary> Summaries) BuildAccusationAudit(
        IReadOnlyList<BenchmarkRun> runs,
        IEnumerable<string?> entryProviders,
        string? providerA,
        string? providerB)
    {
        var tallies = new Dictionary<(string Member, string Provider), AuditTally>();
        var candidateProviders = new List<string>();

        void AddProvider(string provider)
        {
            if (!candidateProviders.Any(p => IsSameProvider(p, provider))) candidateProviders.Add(provider);
        }

        foreach (var provider in entryProviders.Where(p => p != null)) AddProvider(provider!);

        foreach (var run in runs.OrderBy(r => r.Id))
        {
            string? candidate = Trimmed(run.TestedModelSnapshot?.Provider);
            if (candidate == null) continue;
            AddProvider(candidate);
            string canonical = candidateProviders.First(p => IsSameProvider(p, candidate));

            foreach (var answer in (run.Answers ?? new List<BenchmarkRunAnswer>()).Where(BenchmarkRunFinalizer.CountsTowardQualityIndex))
            {
                foreach (var item in ClaimVerificationsOf(answer))
                {
                    if (item.RaisedBy == null || item.RaisedBy.Count == 0) continue;

                    bool accusesAnswer = BenchmarkClaimRoles.HasRole(item, BenchmarkClaimRoles.CriticalErrorQuote)
                        || BenchmarkClaimRoles.HasRole(item, BenchmarkClaimRoles.AccusedQuote)
                        || item.SuspectedFalse == true;
                    bool isBasis = BenchmarkClaimRoles.HasRole(item, BenchmarkClaimRoles.OutOfRubricBasis);
                    if (!accusesAnswer && !isBasis) continue;

                    var verdict = item.EffectiveVerdict;
                    var overturning = accusesAnswer ? BenchmarkClaimVerdict.Supported : BenchmarkClaimVerdict.Refuted;
                    var upholding = accusesAnswer ? BenchmarkClaimVerdict.Refuted : BenchmarkClaimVerdict.Supported;

                    var members = ChargingMembers(item)
                        .Select(m => m?.Trim().ToUpperInvariant())
                        .Where(m => m == MemberA || m == MemberB)
                        .Distinct();
                    foreach (var member in members)
                    {
                        var key = (member!, canonical);
                        if (!tallies.TryGetValue(key, out var tally))
                        {
                            tally = new AuditTally();
                            tallies[key] = tally;
                        }

                        tally.Charges++;
                        if (verdict == overturning) tally.Overturned++;
                        else if (verdict == upholding) tally.Upheld++;
                        else tally.Indeterminate++;
                    }
                }
            }
        }

        var orderedProviders = candidateProviders
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p, StringComparer.Ordinal)
            .ToList();

        var cells = new List<BenchmarkPanelAuditCell>();
        var summaries = new List<BenchmarkPanelMemberAuditSummary>();
        foreach (var (member, memberProvider) in new[] { (MemberA, providerA), (MemberB, providerB) })
        {
            var memberCells = orderedProviders
                .Select(provider =>
                {
                    var tally = tallies.TryGetValue((member, provider), out var t) ? t : new AuditTally();
                    return new BenchmarkPanelAuditCell(
                        member,
                        memberProvider ?? string.Empty,
                        provider,
                        IsSameProvider(memberProvider, provider),
                        tally.Charges,
                        tally.Overturned,
                        tally.Upheld,
                        tally.Indeterminate,
                        Rate(tally.Overturned, tally.Upheld));
                })
                .ToList();
            cells.AddRange(memberCells);

            var same = memberCells.Where(c => c.SameFamily).ToList();
            var other = memberCells.Where(c => !c.SameFamily).ToList();
            int sameRuled = same.Sum(c => c.Overturned + c.Upheld);
            int otherRuled = other.Sum(c => c.Overturned + c.Upheld);
            double? gap = sameRuled >= MinRuledChargesPerSide && otherRuled >= MinRuledChargesPerSide
                ? Rate(other.Sum(c => c.Overturned), other.Sum(c => c.Upheld))!.Value
                  - Rate(same.Sum(c => c.Overturned), same.Sum(c => c.Upheld))!.Value
                : null;

            summaries.Add(new BenchmarkPanelMemberAuditSummary(member, memberProvider ?? string.Empty, gap));
        }

        return (cells, summaries);
    }

    /// <summary>
    /// The members a charge on <paramref name="item"/> is attributed to. A critical-error quote or an
    /// out-of-rubric basis is its raising members'; otherwise an accused sentence is its accusing
    /// members' and a suspected-false claim its suspecting members', each falling back to the raising
    /// members on a record without the role's own set.
    /// </summary>
    private static IEnumerable<string> ChargingMembers(BenchmarkClaimVerification item)
    {
        if (BenchmarkClaimRoles.HasRole(item, BenchmarkClaimRoles.CriticalErrorQuote)
            || BenchmarkClaimRoles.HasRole(item, BenchmarkClaimRoles.OutOfRubricBasis))
        {
            return item.RaisedBy ?? (IEnumerable<string>)Array.Empty<string>();
        }

        var members = new List<string>();
        if (BenchmarkClaimRoles.HasRole(item, BenchmarkClaimRoles.AccusedQuote))
        {
            members.AddRange(item.AccusingMembers ?? Array.Empty<string>());
        }
        if (item.SuspectedFalse == true)
        {
            members.AddRange(item.SuspectingMembers ?? Array.Empty<string>());
        }
        return members;
    }

    private static double? Rate(int overturned, int upheld)
        => overturned + upheld > 0 ? (double)overturned / (overturned + upheld) : null;

    /// <summary>Every verification item stored for an answer; empty when none is stored or the JSON is unreadable.</summary>
    private static IReadOnlyList<BenchmarkClaimVerification> ClaimVerificationsOf(BenchmarkRunAnswer answer)
    {
        if (string.IsNullOrWhiteSpace(answer.ClaimVerificationJson)) return Array.Empty<BenchmarkClaimVerification>();
        try
        {
            return JsonSerializer.Deserialize<List<BenchmarkClaimVerification>>(
                       answer.ClaimVerificationJson, ClaimVerificationJsonOptions)
                   ?? new List<BenchmarkClaimVerification>();
        }
        catch (JsonException)
        {
            return Array.Empty<BenchmarkClaimVerification>();
        }
    }

    // --- Helpers --------------------------------------------------------------------------------

    /// <summary>The same-provider rule of <see cref="BenchmarkComplianceGuard.IsSameProvider(string?, string?)"/>: trimmed, case-insensitive, blank never matches.</summary>
    private static bool IsSameProvider(string? first, string? second)
        => !string.IsNullOrWhiteSpace(first) && !string.IsNullOrWhiteSpace(second)
           && string.Equals(first.Trim(), second.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>One spelling for a provider written several ways: the ordinally first.</summary>
    private static string CanonicalProvider(IEnumerable<string> spellings)
        => spellings.OrderBy(s => s, StringComparer.Ordinal).First();

    private static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Format(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "-";

    private static string IdList(IReadOnlyList<long> ids)
        => string.Join(", ", ids.Select(id => id.ToString(CultureInfo.InvariantCulture)));

    private static string Plural(int count, string noun)
        => count == 1 ? $"1 {noun}" : $"{count.ToString(CultureInfo.InvariantCulture)} {noun}s";
}
