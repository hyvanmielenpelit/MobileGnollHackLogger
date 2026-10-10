namespace Overseer.Services.ChatConsistency;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using MobileGnollHackLogger.Data;
using Overseer.Services.Benchmarking;

/// <summary>A measure a chat-consistency analysis tracks over time, each segmented on its own.</summary>
public enum ChatConsistencyAxis
{
    /// <summary>Graded quality: the native per-answer scores and the indices built from them.</summary>
    Quality = 0,

    /// <summary>Latency from the per-call telemetry (<see cref="BenchmarkRun.CallTelemetryVersion"/>).</summary>
    SpeedTelemetry = 1,

    /// <summary>Latency from the answer timing columns and the Speed Index computed from them.</summary>
    SpeedLegacy = 2,

    /// <summary>The work an answer took: tokens, model calls and tool calls.</summary>
    Work = 3,

    /// <summary>The candidate's cost.</summary>
    Cost = 4
}

/// <summary>Why two consecutive runs of one subject were measured differently.</summary>
public enum MeasurementChangeKind
{
    /// <summary>A grader configuration differs, or the harness changed how answers are graded.</summary>
    Grading = 0,

    /// <summary>The scoring method, scoring profile or speed calibration differs, or the harness changed scoring.</summary>
    Scoring = 1,

    /// <summary>The harness changed how candidate time is measured.</summary>
    CandidateTiming = 2,

    /// <summary>The per-call telemetry version differs between two runs that both recorded telemetry.</summary>
    CallTelemetry = 3,

    /// <summary>The harness changed how candidate tokens or cost are counted.</summary>
    CandidateAccounting = 4,

    /// <summary>The pricing snapshot differs. Always bridged by repricing with one rate card.</summary>
    Pricing = 5
}

/// <summary>Why a run is left out of a speed measure while kept for the others.</summary>
public enum SpeedExclusionReason
{
    /// <summary>The run answered questions concurrently (<see cref="BenchmarkRun.MaxParallelQuestionsUsed"/> above 1).</summary>
    ParallelQuestions = 0,

    /// <summary>The run recorded no per-call telemetry, so it has no telemetry latency.</summary>
    NoCallTelemetry = 1
}

/// <summary>
/// A measurement change between two consecutive runs of one subject. A boundary that is not
/// <see cref="Bridged"/> starts a new segment on each of its <see cref="Axes"/>.
/// </summary>
/// <param name="SubjectKey">The subject, as <see cref="ChatConsistencyComparability.ModelAxisKey"/> renders it.</param>
/// <param name="FromRunId">The earlier run compared.</param>
/// <param name="ToRunId">The later run, where the new segment starts.</param>
/// <param name="AtUtc">The later run's start.</param>
/// <param name="Kind">What changed.</param>
/// <param name="Axes">The axes the change breaks.</param>
/// <param name="Bridged">True when the caller's common re-grade, rescore or a repricing removes the change.</param>
/// <param name="Reason">One line naming what differs.</param>
public sealed record MeasurementBoundary(
    string SubjectKey,
    long FromRunId,
    long ToRunId,
    DateTime AtUtc,
    MeasurementChangeKind Kind,
    IReadOnlyList<ChatConsistencyAxis> Axes,
    bool Bridged,
    string Reason);

/// <summary>A run kept for every measure except the listed speed axes.</summary>
public sealed record SpeedExclusion(
    long RunId,
    SpeedExclusionReason Reason,
    IReadOnlyList<ChatConsistencyAxis> Axes,
    string Detail);

/// <summary>
/// One run's segment on every axis. Segment indices count from 0 within the run's subject; two runs
/// are comparable on an axis when they share the subject and the segment. A null segment means the
/// run is excluded from that axis.
/// </summary>
public sealed record RunSegments(
    long RunId,
    string SubjectKey,
    DateTime StartedAtUtc,
    int? Quality,
    int? SpeedTelemetry,
    int? SpeedLegacy,
    int? Work,
    int? Cost)
{
    /// <summary>The segment on <paramref name="axis"/>, or null when the run is excluded from it.</summary>
    public int? SegmentOf(ChatConsistencyAxis axis) => axis switch
    {
        ChatConsistencyAxis.Quality => Quality,
        ChatConsistencyAxis.SpeedTelemetry => SpeedTelemetry,
        ChatConsistencyAxis.SpeedLegacy => SpeedLegacy,
        ChatConsistencyAxis.Work => Work,
        ChatConsistencyAxis.Cost => Cost,
        _ => null
    };
}

/// <summary>
/// The measurement segmentation of a set of runs: each run's segments, every boundary with its
/// reason, and the runs left out of a speed measure.
/// </summary>
public sealed record ComparabilityAssessment(
    IReadOnlyList<RunSegments> Runs,
    IReadOnlyList<MeasurementBoundary> Boundaries,
    IReadOnlyList<SpeedExclusion> SpeedExclusions)
{
    /// <summary>The segments of run <paramref name="runId"/>, or null when it was not assessed.</summary>
    public RunSegments? RunOf(long runId) => Runs.FirstOrDefault(r => r.RunId == runId);

    /// <summary>
    /// True when both runs were assessed, belong to one subject, are included on
    /// <paramref name="axis"/>, and fall in the same segment of it.
    /// </summary>
    public bool AreComparable(long firstRunId, long secondRunId, ChatConsistencyAxis axis)
    {
        var a = RunOf(firstRunId);
        var b = RunOf(secondRunId);
        if (a == null || b == null) return false;
        if (!string.Equals(a.SubjectKey, b.SubjectKey, StringComparison.Ordinal)) return false;

        int? sa = a.SegmentOf(axis);
        int? sb = b.SegmentOf(axis);
        return sa.HasValue && sb.HasValue && sa.Value == sb.Value;
    }
}

/// <summary>
/// The item an answer was produced for: the question, as
/// <see cref="BenchmarkReportFacts.QuestionKeyOf"/> renders it, and the item revision it was graded
/// against. A null revision predates the column; it pairs only with another null and is flagged.
/// </summary>
public readonly record struct ChatItemKey(string Question, int? Revision)
{
    /// <summary>True when the revision was recorded.</summary>
    public bool HasRevision => Revision.HasValue;

    /// <summary><c>&lt;question&gt;@&lt;revision&gt;</c>, with <c>?</c> for an unrecorded revision.</summary>
    public override string ToString()
        => Question + "@" + (Revision?.ToString(CultureInfo.InvariantCulture) ?? "?");
}

/// <summary>An item answered in both periods, with how many answers each period holds for it.</summary>
public sealed record PairedItem(ChatItemKey Key, int BaselineAnswerCount, int ComparisonAnswerCount)
{
    /// <summary>True when the item's revision was not recorded, so the pairing rests on null matching null.</summary>
    public bool NullRevision => !Key.HasRevision;
}

/// <summary>
/// The items two periods share, and what pairing had to leave out.
/// </summary>
/// <param name="Paired">Items present in both periods, in item order.</param>
/// <param name="BaselineOnly">Items present in the baseline period only.</param>
/// <param name="ComparisonOnly">Items present in the comparison period only.</param>
/// <param name="RevisedQuestions">Questions present in both periods under at least one revision the other period lacks.</param>
/// <param name="NullRevisionItems">Every item, paired or not, whose revision was not recorded.</param>
public sealed record ItemPairing(
    IReadOnlyList<PairedItem> Paired,
    IReadOnlyList<ChatItemKey> BaselineOnly,
    IReadOnlyList<ChatItemKey> ComparisonOnly,
    IReadOnlyList<string> RevisedQuestions,
    IReadOnlyList<ChatItemKey> NullRevisionItems)
{
    /// <summary>The number of distinct items in either period.</summary>
    public int TotalItemCount => Paired.Count + BaselineOnly.Count + ComparisonOnly.Count;

    /// <summary>The number of items present in one period only.</summary>
    public int ExcludedItemCount => BaselineOnly.Count + ComparisonOnly.Count;

    /// <summary>The share of distinct items present in one period only; 0 when there are no items.</summary>
    public double ExcludedShare => TotalItemCount == 0 ? 0.0 : (double)ExcludedItemCount / TotalItemCount;
}

/// <summary>
/// A change to what the Overseer chat is, dated by the run that first shows it. An event never
/// excludes data: it is a point on the timeline that control runs later attribute.
/// </summary>
/// <param name="AtUtc">The later run's start.</param>
/// <param name="Kind">The changed field, one of <see cref="OverseerEventKinds"/>.</param>
/// <param name="From">The value the earlier run recorded.</param>
/// <param name="To">The value the later run recorded.</param>
/// <param name="RunId">The later run.</param>
public sealed record OverseerEvent(DateTime AtUtc, string Kind, string? From, string? To, long RunId)
{
    /// <summary>The subject whose series shows the change.</summary>
    public string SubjectKey { get; init; } = string.Empty;

    /// <summary>The earlier run that recorded <see cref="From"/>.</summary>
    public long PreviousRunId { get; init; }
}

/// <summary>The <see cref="OverseerEvent.Kind"/> values, named after the run field that changed.</summary>
public static class OverseerEventKinds
{
    public const string CandidateSystemPrompt = "CandidateSystemPromptSha256";
    public const string ToolGuides = "ToolGuidesSha256";
    public const string KnowledgeBase = "KnowledgeBaseHeadSha";
    public const string Wiki = "WikiHeadSha";
    public const string SourceCode = "SourceCodeHeadSha";
    public const string CorpusIndex = "CorpusIndexFingerprintsJson";
    public const string CandidatePromptOptions = "CandidatePromptOptionsJson";
    public const string ToolIterationCaps = "ToolIterationCapsJson";
    public const string TotalModelCallCaps = "TotalModelCallCapsJson";
    public const string QuestionTimeouts = "QuestionTimeoutSecondsJson";
    public const string MaxToolCallsPerQuestion = "MaxToolCallsPerQuestionUsed";

    /// <summary>A harness version change whose ledger impact includes <see cref="HarnessImpact.CandidateInput"/>.</summary>
    public const string HarnessVersion = "HarnessVersion";

    /// <summary>Every kind, in the order events of one run are listed.</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        CandidateSystemPrompt, ToolGuides, KnowledgeBase, Wiki, SourceCode, CorpusIndex,
        CandidatePromptOptions, ToolIterationCaps, TotalModelCallCaps, QuestionTimeouts,
        MaxToolCallsPerQuestion, HarnessVersion
    };
}

/// <summary>A named stretch of time and the target runs it holds.</summary>
public sealed record ChatConsistencyPeriod(string Name, IReadOnlyList<BenchmarkRun> TargetRuns);

/// <summary>A control run qualified for a target run: another subject, the same Overseer build and suite.</summary>
public sealed record ControlRunMatch(string Period, long TargetRunId, long ControlRunId, int PairedItemCount)
{
    /// <summary>The control run's subject.</summary>
    public string ControlSubjectKey { get; init; } = string.Empty;
}

/// <summary>A period with no qualifying control run, and the run that would close the gap.</summary>
public sealed record MissingControlNote(string Period, string SuiteName, string Fingerprint, string SuggestedText)
{
    /// <summary>The latest target run of the period on this suite and build.</summary>
    public long TargetRunId { get; init; }
}

/// <summary>The control runs that qualified, and a note for every period that has none.</summary>
public sealed record ControlRunMatching(
    IReadOnlyList<ControlRunMatch> Matches,
    IReadOnlyList<MissingControlNote> MissingControls);

/// <summary>
/// Whether benchmark runs made over time can be compared to tell if the Overseer chat with one model
/// changed. Pure computation over in-memory runs with their <see cref="BenchmarkRun.Answers"/>
/// loaded: no database, no clock.
///
/// <para>Instrument differences between runs are of two kinds, treated oppositely. A
/// <i>measurement</i> change alters how the chat is measured — the graders, the scoring, how time or
/// tokens are counted, the prices — and is bridged or segmented (<see cref="AssessMeasurement"/>).
/// An <i>Overseer</i> change alters what the chat is — the system prompt, the tool guides, the
/// corpora, the prompt options, the budgets, a harness version that changed candidate input — and is
/// never excluded; it becomes a dated event (<see cref="DetectOverseerEvents"/>) that control runs of
/// other subjects under the same Overseer build later attribute (<see cref="MatchControlRuns"/>).</para>
///
/// <para>A series is one subject (<see cref="ModelAxisKey"/>), ordered by
/// <see cref="BenchmarkRun.StartedAtUtc"/> and then by id; for Overseer events, one subject on one
/// suite. Runs of other subjects are separate series or controls, never neighbors.</para>
/// </summary>
public static class ChatConsistencyComparability
{
    private static readonly IReadOnlyList<ChatConsistencyAxis> QualityAxes = new[] { ChatConsistencyAxis.Quality };
    private static readonly IReadOnlyList<ChatConsistencyAxis> QualityAndLegacySpeedAxes =
        new[] { ChatConsistencyAxis.Quality, ChatConsistencyAxis.SpeedLegacy };
    private static readonly IReadOnlyList<ChatConsistencyAxis> LegacySpeedAxes = new[] { ChatConsistencyAxis.SpeedLegacy };
    private static readonly IReadOnlyList<ChatConsistencyAxis> TelemetrySpeedAxes = new[] { ChatConsistencyAxis.SpeedTelemetry };
    private static readonly IReadOnlyList<ChatConsistencyAxis> SpeedAxes =
        new[] { ChatConsistencyAxis.SpeedTelemetry, ChatConsistencyAxis.SpeedLegacy };
    private static readonly IReadOnlyList<ChatConsistencyAxis> WorkAndCostAxes =
        new[] { ChatConsistencyAxis.Work, ChatConsistencyAxis.Cost };
    private static readonly IReadOnlyList<ChatConsistencyAxis> CostAxes = new[] { ChatConsistencyAxis.Cost };

    // --- Subject ----------------------------------------------------------------------------

    /// <summary>
    /// The subject a run measured: the nine <see cref="BenchmarkCrossModelComparability.ModelAxisKeys"/>
    /// as <see cref="BenchmarkComparabilityKey.Extract"/> renders them, as <c>name=value</c> pairs in
    /// that order joined by <c>;</c>. Two runs with different keys are different subjects.
    /// </summary>
    public static string ModelAxisKey(BenchmarkRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        var values = BenchmarkComparabilityKey.Extract(run)
            .GroupBy(k => k.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Value, StringComparer.Ordinal);

        return string.Join(";", BenchmarkCrossModelComparability.ModelAxisKeys.Select(name =>
            name + "=" + (values.TryGetValue(name, out var value) ? value : BenchmarkComparabilityKey.NoValue)));
    }

    // --- Items ------------------------------------------------------------------------------

    /// <summary>The item <paramref name="answer"/> belongs to: its question and its recorded revision.</summary>
    public static ChatItemKey ItemKey(BenchmarkRunAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(answer);
        return new ChatItemKey(BenchmarkReportFacts.QuestionKeyOf(answer), answer.ItemRevisionUsed);
    }

    /// <summary>
    /// Pairs the items of two periods. An item pairs only on an identical question and revision, so a
    /// revised item falls out of the pairing on both sides and its question is listed in
    /// <see cref="ItemPairing.RevisedQuestions"/>; a null revision pairs only with a null one and is
    /// listed in <see cref="ItemPairing.NullRevisionItems"/>. Counts are answers per period.
    /// </summary>
    public static ItemPairing PairItems(IEnumerable<BenchmarkRun> baseline, IEnumerable<BenchmarkRun> comparison)
    {
        var baselineCounts = CountItems(baseline);
        var comparisonCounts = CountItems(comparison);

        var paired = baselineCounts.Keys
            .Where(comparisonCounts.ContainsKey)
            .OrderBy(k => k, ItemKeyComparer.Instance)
            .Select(k => new PairedItem(k, baselineCounts[k], comparisonCounts[k]))
            .ToList();

        var baselineOnly = baselineCounts.Keys
            .Where(k => !comparisonCounts.ContainsKey(k))
            .OrderBy(k => k, ItemKeyComparer.Instance)
            .ToList();

        var comparisonOnly = comparisonCounts.Keys
            .Where(k => !baselineCounts.ContainsKey(k))
            .OrderBy(k => k, ItemKeyComparer.Instance)
            .ToList();

        var baselineQuestions = new HashSet<string>(baselineCounts.Keys.Select(k => k.Question), StringComparer.Ordinal);
        var comparisonQuestions = new HashSet<string>(comparisonCounts.Keys.Select(k => k.Question), StringComparer.Ordinal);
        var revised = baselineOnly.Concat(comparisonOnly)
            .Select(k => k.Question)
            .Where(q => baselineQuestions.Contains(q) && comparisonQuestions.Contains(q))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(q => q, QuestionComparer.Instance)
            .ToList();

        var nullRevision = baselineCounts.Keys
            .Concat(comparisonCounts.Keys)
            .Where(k => !k.HasRevision)
            .Distinct()
            .OrderBy(k => k, ItemKeyComparer.Instance)
            .ToList();

        return new ItemPairing(paired, baselineOnly, comparisonOnly, revised, nullRevision);
    }

    // --- Measurement changes ----------------------------------------------------------------

    /// <summary>
    /// Detects the measurement changes between consecutive runs of each subject and assigns every run
    /// a segment per axis.
    ///
    /// <list type="bullet">
    /// <item><b>Grading</b> — any grader snapshot id or grader configuration differs, or the harness
    /// ledger between the two versions carries <see cref="HarnessImpact.Grading"/>. Breaks quality;
    /// bridged when both runs are in <paramref name="commonGraderCovers"/>.</item>
    /// <item><b>Scoring</b> — the scoring method version or the scoring profile differs, or the ledger
    /// carries <see cref="HarnessImpact.Scoring"/>: breaks quality and the Speed Index. A differing
    /// speed calibration breaks the Speed Index alone. Bridged when both runs are in
    /// <paramref name="rescoredUnderOneProfile"/>.</item>
    /// <item><b>CandidateTiming</b> — the ledger carries <see cref="HarnessImpact.CandidateTiming"/>.
    /// Breaks legacy speed only, never bridged; telemetry speed is unaffected.</item>
    /// <item><b>CallTelemetry</b> — two runs that both recorded telemetry differ on
    /// <see cref="BenchmarkRun.CallTelemetryVersion"/>, compared against the subject's latest earlier
    /// run that recorded one. Breaks telemetry speed.</item>
    /// <item><b>CandidateAccounting</b> — the ledger carries
    /// <see cref="HarnessImpact.CandidateAccounting"/>. Breaks work and cost, never bridged.</item>
    /// <item><b>Pricing</b> — the pricing snapshot differs. Reported, always bridged by repricing
    /// with one rate card, never segments.</item>
    /// </list>
    ///
    /// <para>A run's harness is its <see cref="BenchmarkRun.HarnessVersion"/> together with any
    /// differing <see cref="BenchmarkRun.RerunHarnessVersion"/>; the ledger impact between two runs is
    /// the union over every pair of their versions, and a null or unknown version counts as every
    /// change. A run with <see cref="BenchmarkRun.MaxParallelQuestionsUsed"/> above 1 is excluded from
    /// both speed axes, and a run with no call telemetry from telemetry speed; both stay on every
    /// other axis, and an exclusion never moves a segment boundary.</para>
    /// </summary>
    /// <param name="runs">The runs to assess, of any number of subjects.</param>
    /// <param name="commonGraderCovers">Ids of runs whose answers one common grader configuration re-graded.</param>
    /// <param name="rescoredUnderOneProfile">Ids of runs rescored under one scoring profile.</param>
    public static ComparabilityAssessment AssessMeasurement(
        IEnumerable<BenchmarkRun> runs,
        IEnumerable<long>? commonGraderCovers = null,
        IEnumerable<long>? rescoredUnderOneProfile = null)
    {
        ArgumentNullException.ThrowIfNull(runs);

        var regraded = new HashSet<long>(commonGraderCovers ?? Array.Empty<long>());
        var rescored = new HashSet<long>(rescoredUnderOneProfile ?? Array.Empty<long>());

        var segments = new List<RunSegments>();
        var boundaries = new List<MeasurementBoundary>();
        var exclusions = new List<SpeedExclusion>();

        foreach (var series in Series(runs))
        {
            string subject = series.Key;
            var counters = Enum.GetValues<ChatConsistencyAxis>().ToDictionary(a => a, _ => 0);
            BenchmarkRun? previous = null;
            BenchmarkRun? lastTelemetryRun = null;

            foreach (var run in series.Value)
            {
                var pairBoundaries = new List<MeasurementBoundary>();
                if (previous != null)
                {
                    pairBoundaries.AddRange(DetectBoundaries(subject, previous, run, regraded, rescored));
                }

                if (run.CallTelemetryVersion.HasValue)
                {
                    if (lastTelemetryRun != null
                        && lastTelemetryRun.CallTelemetryVersion!.Value != run.CallTelemetryVersion.Value)
                    {
                        pairBoundaries.Add(new MeasurementBoundary(
                            subject, lastTelemetryRun.Id, run.Id, run.StartedAtUtc,
                            MeasurementChangeKind.CallTelemetry, TelemetrySpeedAxes, false,
                            "Call telemetry version "
                            + Inv(lastTelemetryRun.CallTelemetryVersion.Value) + " → " + Inv(run.CallTelemetryVersion.Value) + "."));
                    }

                    lastTelemetryRun = run;
                }

                foreach (var axis in pairBoundaries.Where(b => !b.Bridged).SelectMany(b => b.Axes).Distinct())
                {
                    counters[axis]++;
                }

                boundaries.AddRange(pairBoundaries);

                var runExclusions = SpeedExclusionsOf(run);
                exclusions.AddRange(runExclusions);
                var excludedAxes = new HashSet<ChatConsistencyAxis>(runExclusions.SelectMany(e => e.Axes));

                int? Segment(ChatConsistencyAxis axis) => excludedAxes.Contains(axis) ? null : counters[axis];

                segments.Add(new RunSegments(
                    run.Id, subject, run.StartedAtUtc,
                    Segment(ChatConsistencyAxis.Quality),
                    Segment(ChatConsistencyAxis.SpeedTelemetry),
                    Segment(ChatConsistencyAxis.SpeedLegacy),
                    Segment(ChatConsistencyAxis.Work),
                    Segment(ChatConsistencyAxis.Cost)));

                previous = run;
            }
        }

        return new ComparabilityAssessment(
            segments.OrderBy(s => s.StartedAtUtc).ThenBy(s => s.RunId).ToList(),
            boundaries
                .OrderBy(b => b.AtUtc)
                .ThenBy(b => b.ToRunId)
                .ThenBy(b => b.Kind)
                .ThenBy(b => b.FromRunId)
                .ToList(),
            exclusions.OrderBy(e => e.RunId).ThenBy(e => e.Reason).ToList());
    }

    private static IEnumerable<MeasurementBoundary> DetectBoundaries(
        string subject,
        BenchmarkRun previous,
        BenchmarkRun run,
        HashSet<long> regraded,
        HashSet<long> rescored)
    {
        var before = KeyValues(previous);
        var after = KeyValues(run);
        HarnessImpact harness = HarnessImpactBetween(previous, run);
        string harnessChange = "harness " + HarnessIdentity(previous) + " → " + HarnessIdentity(run);

        // --- Grading ---
        var grading = new List<string>();
        if (previous.AssessorModelSnapshotId != run.AssessorModelSnapshotId
            || !SameKey(before, after, BenchmarkComparabilityKey.AssessorConfigurationKey))
        {
            grading.Add(previous.CoAssessorModelSnapshotId.HasValue || run.CoAssessorModelSnapshotId.HasValue
                ? "assessor panel configuration differs"
                : "assessor configuration differs");
        }
        else if (previous.CoAssessorModelSnapshotId != run.CoAssessorModelSnapshotId)
        {
            grading.Add("co-assessor configuration differs");
        }

        if (previous.SecondOpinionAssessorModelSnapshotId != run.SecondOpinionAssessorModelSnapshotId
            || !SameKey(before, after, BenchmarkComparabilityKey.SecondOpinionConfigurationKey))
        {
            grading.Add("second-opinion configuration differs");
        }

        if (previous.ClaimVerifierModelSnapshotId != run.ClaimVerifierModelSnapshotId
            || !SameKey(before, after, BenchmarkComparabilityKey.ClaimVerifierConfigurationKey))
        {
            grading.Add("claim verifier configuration differs");
        }

        if (harness.HasFlag(HarnessImpact.Grading)) grading.Add(harnessChange + " changed grading");

        if (grading.Count > 0)
        {
            yield return new MeasurementBoundary(
                subject, previous.Id, run.Id, run.StartedAtUtc, MeasurementChangeKind.Grading, QualityAxes,
                regraded.Contains(previous.Id) && regraded.Contains(run.Id),
                Sentence(grading));
        }

        bool rescoredPair = rescored.Contains(previous.Id) && rescored.Contains(run.Id);

        // --- Scoring ---
        var scoring = new List<string>();
        if (previous.ScoringMethodVersion != run.ScoringMethodVersion)
        {
            scoring.Add("scoring method " + Inv(previous.ScoringMethodVersion) + " → " + Inv(run.ScoringMethodVersion));
        }

        if (!SameKey(before, after, BenchmarkComparabilityKey.ScoringProfileKey)) scoring.Add("scoring profile differs");
        if (harness.HasFlag(HarnessImpact.Scoring)) scoring.Add(harnessChange + " changed scoring");

        if (scoring.Count > 0)
        {
            yield return new MeasurementBoundary(
                subject, previous.Id, run.Id, run.StartedAtUtc, MeasurementChangeKind.Scoring, QualityAndLegacySpeedAxes,
                rescoredPair, Sentence(scoring));
        }

        if (!SameKey(before, after, BenchmarkComparabilityKey.SpeedCalibrationKey))
        {
            yield return new MeasurementBoundary(
                subject, previous.Id, run.Id, run.StartedAtUtc, MeasurementChangeKind.Scoring, LegacySpeedAxes,
                rescoredPair, Sentence(new[] { "speed calibration differs" }));
        }

        // --- Candidate timing and accounting ---
        if (harness.HasFlag(HarnessImpact.CandidateTiming))
        {
            yield return new MeasurementBoundary(
                subject, previous.Id, run.Id, run.StartedAtUtc, MeasurementChangeKind.CandidateTiming, LegacySpeedAxes,
                false, Sentence(new[] { harnessChange + " changed how candidate time is measured" }));
        }

        if (harness.HasFlag(HarnessImpact.CandidateAccounting))
        {
            yield return new MeasurementBoundary(
                subject, previous.Id, run.Id, run.StartedAtUtc, MeasurementChangeKind.CandidateAccounting, WorkAndCostAxes,
                false, Sentence(new[] { harnessChange + " changed how candidate tokens or cost are counted" }));
        }

        // --- Pricing ---
        if (!SameKey(before, after, BenchmarkComparabilityKey.PricingSnapshotKey))
        {
            yield return new MeasurementBoundary(
                subject, previous.Id, run.Id, run.StartedAtUtc, MeasurementChangeKind.Pricing, CostAxes,
                true, Sentence(new[] { "pricing snapshot differs; bridged by repricing with one rate card" }));
        }
    }

    private static List<SpeedExclusion> SpeedExclusionsOf(BenchmarkRun run)
    {
        var list = new List<SpeedExclusion>();
        if (run.MaxParallelQuestionsUsed > 1)
        {
            list.Add(new SpeedExclusion(
                run.Id, SpeedExclusionReason.ParallelQuestions, SpeedAxes,
                "Questions ran " + Inv(run.MaxParallelQuestionsUsed) + " at a time, so its latency is not sequential latency."));
        }

        if (!run.CallTelemetryVersion.HasValue)
        {
            list.Add(new SpeedExclusion(
                run.Id, SpeedExclusionReason.NoCallTelemetry, TelemetrySpeedAxes,
                "No per-call telemetry recorded."));
        }

        return list;
    }

    // --- Overseer events --------------------------------------------------------------------

    /// <summary>
    /// The Overseer changes per model and suite, in time order: a change of
    /// <see cref="BenchmarkRun.CandidateSystemPromptSha256"/>, <see cref="BenchmarkRun.ToolGuidesSha256"/>,
    /// <see cref="BenchmarkRun.KnowledgeBaseHeadSha"/>, <see cref="BenchmarkRun.WikiHeadSha"/>,
    /// <see cref="BenchmarkRun.SourceCodeHeadSha"/>, <see cref="BenchmarkRun.CorpusIndexFingerprintsJson"/>,
    /// the candidate prompt options (compared in canonical form), the four budget fields, or a harness
    /// version whose ledger impact includes <see cref="HarnessImpact.CandidateInput"/>.
    ///
    /// <para>A series is one subject on one suite (<see cref="ModelAxisKey"/> and the suite id, else its
    /// name), so suites that alternate within a battery are never compared with each other. A null is
    /// "not recorded", never a value: a run that did not record a field neither starts nor ends an
    /// event, and a change is detected against the series' latest earlier run that recorded the field.
    /// Events never exclude data.</para>
    ///
    /// <para>One change seen in several suites of a subject is one event: events with the same
    /// subject, kind, <see cref="OverseerEvent.From"/> and <see cref="OverseerEvent.To"/> whose spans
    /// overlap are merged into the earliest. A span runs from the previous run's start (exclusive) to
    /// the run's start (inclusive).</para>
    /// </summary>
    public static IReadOnlyList<OverseerEvent> DetectOverseerEvents(IEnumerable<BenchmarkRun> runs)
    {
        ArgumentNullException.ThrowIfNull(runs);

        var distinct = Distinct(runs).ToList();
        var events = new List<OverseerEvent>();
        var kindOrder = OverseerEventKinds.All
            .Select((kind, index) => (kind, index))
            .ToDictionary(p => p.kind, p => p.index, StringComparer.Ordinal);

        foreach (var series in EventSeries(distinct))
        {
            var lastRecorded = new Dictionary<string, (string Value, long RunId)>(StringComparer.Ordinal);
            BenchmarkRun? lastHarnessRun = null;

            foreach (var run in series.Value)
            {
                foreach (var (kind, value) in OverseerFields(run))
                {
                    if (value == null) continue;

                    if (lastRecorded.TryGetValue(kind, out var last)
                        && !string.Equals(last.Value, value, StringComparison.Ordinal))
                    {
                        events.Add(new OverseerEvent(run.StartedAtUtc, kind, last.Value, value, run.Id)
                        {
                            SubjectKey = series.Key,
                            PreviousRunId = last.RunId
                        });
                    }

                    lastRecorded[kind] = (value, run.Id);
                }

                if (string.IsNullOrWhiteSpace(run.HarnessVersion)) continue;

                if (lastHarnessRun != null
                    && HarnessImpactBetween(lastHarnessRun, run).HasFlag(HarnessImpact.CandidateInput))
                {
                    events.Add(new OverseerEvent(
                        run.StartedAtUtc, OverseerEventKinds.HarnessVersion,
                        HarnessIdentity(lastHarnessRun), HarnessIdentity(run), run.Id)
                    {
                        SubjectKey = series.Key,
                        PreviousRunId = lastHarnessRun.Id
                    });
                }

                lastHarnessRun = run;
            }
        }

        var startOf = distinct.ToDictionary(r => r.Id, r => r.StartedAtUtc);

        return MergeAcrossSuites(events, startOf)
            .OrderBy(e => e.AtUtc)
            .ThenBy(e => e.RunId)
            .ThenBy(e => kindOrder[e.Kind])
            .ToList();
    }

    /// <summary>
    /// Events of one subject, kind and change, merged into the earliest of each group whose spans
    /// overlap; a merged span grows to cover every event it absorbed.
    /// </summary>
    private static IEnumerable<OverseerEvent> MergeAcrossSuites(
        IEnumerable<OverseerEvent> events, IReadOnlyDictionary<long, DateTime> startOf)
    {
        foreach (var group in events.GroupBy(x => (x.SubjectKey, x.Kind, x.From, x.To)))
        {
            var kept = new List<(OverseerEvent Event, DateTime After, DateTime Through)>();

            foreach (var e in group.OrderBy(x => x.AtUtc).ThenBy(x => x.RunId))
            {
                DateTime after = startOf.TryGetValue(e.PreviousRunId, out var previousStart) ? previousStart : e.AtUtc;
                DateTime through = e.AtUtc;

                int index = kept.FindIndex(s => s.After < through && after < s.Through);
                if (index < 0)
                {
                    kept.Add((e, after, through));
                    continue;
                }

                var span = kept[index];
                kept[index] = (
                    span.Event,
                    after < span.After ? after : span.After,
                    through > span.Through ? through : span.Through);
            }

            foreach (var merged in kept) yield return merged.Event;
        }
    }

    // --- Instrument fingerprint -------------------------------------------------------------

    /// <summary>
    /// A lower-case hex SHA-256 over exactly the Overseer-change fields of
    /// <see cref="DetectOverseerEvents"/> and the run's harness version (with any differing re-run
    /// harness). Two runs with equal fingerprints were made under the same Overseer build as far as
    /// the candidate is concerned. A null field renders as <see cref="BenchmarkComparabilityKey.NoValue"/>,
    /// so two runs that both did not record it agree on it.
    /// </summary>
    public static string OverseerInstrumentFingerprint(BenchmarkRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        var lines = OverseerFields(run)
            .Select(f => f.Kind + "=" + (f.Value ?? BenchmarkComparabilityKey.NoValue))
            .Append(OverseerEventKinds.HarnessVersion + "=" + HarnessIdentity(run));

        return Sha256Hex(string.Join("\n", lines));
    }

    // --- Control runs -----------------------------------------------------------------------

    /// <summary>
    /// For every target run of every period, the runs among <paramref name="candidateControls"/> of
    /// another subject with an identical <see cref="OverseerInstrumentFingerprint"/> and the same
    /// suite (<see cref="BenchmarkRun.BenchmarkSuiteIdUsed"/>, else <see cref="BenchmarkRun.BenchmarkSuiteId"/>,
    /// falling back to <see cref="BenchmarkRun.SuiteName"/> when either run has no id), with at least one
    /// item in common. A period with no qualifying control gets one <see cref="MissingControlNote"/>
    /// per suite and build among its targets, naming the run that would close the gap.
    /// </summary>
    /// <param name="periods">The periods, in the order they are reported.</param>
    /// <param name="candidateControls">The runs that may serve as controls.</param>
    /// <param name="availableOtherProviderModels">
    /// Models the caller can run, as <c>provider/model</c> or as a name the caller has already
    /// limited to other providers. An entry whose provider part equals the target's provider
    /// (ignoring case) is skipped; the first remaining entry, in the caller's order, is suggested.
    /// </param>
    public static ControlRunMatching MatchControlRuns(
        IEnumerable<ChatConsistencyPeriod> periods,
        IEnumerable<BenchmarkRun> candidateControls,
        IEnumerable<string>? availableOtherProviderModels = null)
    {
        ArgumentNullException.ThrowIfNull(periods);
        ArgumentNullException.ThrowIfNull(candidateControls);

        var models = (availableOtherProviderModels ?? Array.Empty<string>())
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Select(m => m.Trim())
            .ToList();

        var controls = Distinct(candidateControls)
            .Select(r => new RunFacts(r))
            .OrderBy(f => f.Run.Id)
            .ToList();

        var matches = new List<ControlRunMatch>();
        var notes = new List<MissingControlNote>();

        foreach (var period in periods)
        {
            var targets = Distinct(period.TargetRuns ?? Array.Empty<BenchmarkRun>())
                .Select(r => new RunFacts(r))
                .OrderBy(f => f.Run.Id)
                .ToList();

            var periodMatches = new List<ControlRunMatch>();
            foreach (var target in targets)
            {
                foreach (var control in controls)
                {
                    if (control.Run.Id == target.Run.Id) continue;
                    if (string.Equals(control.Subject, target.Subject, StringComparison.Ordinal)) continue;
                    if (!string.Equals(control.Fingerprint, target.Fingerprint, StringComparison.Ordinal)) continue;
                    if (!SameSuite(control.Run, target.Run)) continue;

                    int paired = target.Items.Count(control.Items.Contains);
                    if (paired == 0) continue;

                    periodMatches.Add(new ControlRunMatch(period.Name, target.Run.Id, control.Run.Id, paired)
                    {
                        ControlSubjectKey = control.Subject
                    });
                }
            }

            matches.AddRange(periodMatches);
            if (periodMatches.Count > 0 || targets.Count == 0) continue;

            var gaps = targets
                .GroupBy(t => (Suite: SuiteIdentity(t.Run), t.Fingerprint))
                .Select(g => g.OrderByDescending(t => t.Run.StartedAtUtc).ThenByDescending(t => t.Run.Id).First())
                .OrderBy(t => t.Run.StartedAtUtc)
                .ThenBy(t => t.Run.Id);

            foreach (var target in gaps)
            {
                notes.Add(new MissingControlNote(
                    period.Name, target.Run.SuiteName ?? string.Empty, target.Fingerprint,
                    SuggestControl(period.Name, target, models))
                {
                    TargetRunId = target.Run.Id
                });
            }
        }

        return new ControlRunMatching(matches, notes);
    }

    private static string SuggestControl(string period, RunFacts target, IReadOnlyList<string> models)
    {
        string provider = target.Provider;
        string? model = models.FirstOrDefault(m =>
        {
            int slash = m.IndexOf('/', StringComparison.Ordinal);
            return slash <= 0 || !string.Equals(m.Substring(0, slash).Trim(), provider, StringComparison.OrdinalIgnoreCase);
        });

        string subject = model != null
            ? "a run of " + model + " (a provider other than " + provider + ")"
            : "a run of a model from a provider other than " + provider;

        string suite = string.IsNullOrWhiteSpace(target.Run.SuiteName) ? SuiteIdentity(target.Run) : target.Run.SuiteName;

        return "No control run for period " + period + ": make " + subject
            + " on suite " + suite
            + " under the same Overseer build as run #" + Inv(target.Run.Id) + ".";
    }

    // --- Shared helpers ---------------------------------------------------------------------

    /// <summary>The facts control matching reads from one run, computed once.</summary>
    private sealed class RunFacts
    {
        public RunFacts(BenchmarkRun run)
        {
            Run = run;
            Subject = ModelAxisKey(run);
            Fingerprint = OverseerInstrumentFingerprint(run);
            Items = new HashSet<ChatItemKey>((run.Answers ?? new List<BenchmarkRunAnswer>()).Select(ItemKey));
            Provider = KeyValues(run).TryGetValue(BenchmarkComparabilityKey.CandidateProviderKey, out var p)
                ? p
                : BenchmarkComparabilityKey.NoValue;
        }

        public BenchmarkRun Run { get; }

        public string Subject { get; }

        public string Fingerprint { get; }

        public HashSet<ChatItemKey> Items { get; }

        public string Provider { get; }
    }

    /// <summary>Runs grouped by subject, each ordered by start and id, the subjects in ordinal order.</summary>
    private static IEnumerable<KeyValuePair<string, List<BenchmarkRun>>> Series(IEnumerable<BenchmarkRun> runs)
    {
        return Distinct(runs)
            .GroupBy(ModelAxisKey, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new KeyValuePair<string, List<BenchmarkRun>>(
                g.Key,
                g.OrderBy(r => r.StartedAtUtc).ThenBy(r => r.Id).ToList()));
    }

    /// <summary>
    /// Runs grouped by subject and suite, each ordered by start and id; keyed by the subject alone,
    /// the series in ordinal order of subject and then suite.
    /// </summary>
    private static IEnumerable<KeyValuePair<string, List<BenchmarkRun>>> EventSeries(IEnumerable<BenchmarkRun> runs)
    {
        return Distinct(runs)
            .GroupBy(r => (Subject: ModelAxisKey(r), Suite: SuiteIdentity(r)))
            .OrderBy(g => g.Key.Subject, StringComparer.Ordinal)
            .ThenBy(g => g.Key.Suite, StringComparer.Ordinal)
            .Select(g => new KeyValuePair<string, List<BenchmarkRun>>(
                g.Key.Subject,
                g.OrderBy(r => r.StartedAtUtc).ThenBy(r => r.Id).ToList()));
    }

    private static IEnumerable<BenchmarkRun> Distinct(IEnumerable<BenchmarkRun> runs)
        => runs.Where(r => r != null).DistinctBy(r => r.Id);

    private static Dictionary<ChatItemKey, int> CountItems(IEnumerable<BenchmarkRun> runs)
    {
        ArgumentNullException.ThrowIfNull(runs);

        var counts = new Dictionary<ChatItemKey, int>();
        foreach (var run in Distinct(runs))
        {
            foreach (var answer in run.Answers ?? new List<BenchmarkRunAnswer>())
            {
                var key = ItemKey(answer);
                counts[key] = counts.TryGetValue(key, out int n) ? n + 1 : 1;
            }
        }

        return counts;
    }

    /// <summary>
    /// The Overseer-change fields of one run in <see cref="OverseerEventKinds.All"/> order, harness
    /// version excluded. Blank strings read as null; prompt options are rendered canonically.
    /// </summary>
    private static IEnumerable<(string Kind, string? Value)> OverseerFields(BenchmarkRun run)
    {
        yield return (OverseerEventKinds.CandidateSystemPrompt, Clean(run.CandidateSystemPromptSha256));
        yield return (OverseerEventKinds.ToolGuides, Clean(run.ToolGuidesSha256));
        yield return (OverseerEventKinds.KnowledgeBase, Clean(run.KnowledgeBaseHeadSha));
        yield return (OverseerEventKinds.Wiki, Clean(run.WikiHeadSha));
        yield return (OverseerEventKinds.SourceCode, Clean(run.SourceCodeHeadSha));
        yield return (OverseerEventKinds.CorpusIndex, Clean(run.CorpusIndexFingerprintsJson));
        yield return (OverseerEventKinds.CandidatePromptOptions,
            Clean(run.CandidatePromptOptionsJson) == null
                ? null
                : BenchmarkCandidatePromptOptions.FromJson(run.CandidatePromptOptionsJson).ToCanonicalJson());
        yield return (OverseerEventKinds.ToolIterationCaps, Clean(run.ToolIterationCapsJson));
        yield return (OverseerEventKinds.TotalModelCallCaps, Clean(run.TotalModelCallCapsJson));
        yield return (OverseerEventKinds.QuestionTimeouts, Clean(run.QuestionTimeoutSecondsJson));
        yield return (OverseerEventKinds.MaxToolCallsPerQuestion,
            run.MaxToolCallsPerQuestionUsed.HasValue ? Inv(run.MaxToolCallsPerQuestionUsed.Value) : null);
    }

    /// <summary>The harness versions a run's answers were produced under: the run's own, and a differing re-run's.</summary>
    private static IReadOnlyList<string?> HarnessVersionsOf(BenchmarkRun run)
    {
        string? main = Clean(run.HarnessVersion);
        string? rerun = Clean(run.RerunHarnessVersion);
        return rerun == null || string.Equals(rerun, main, StringComparison.Ordinal)
            ? new[] { main }
            : new[] { main, rerun };
    }

    /// <summary>The union of the ledger impact over every pair of the two runs' harness versions.</summary>
    private static HarnessImpact HarnessImpactBetween(BenchmarkRun earlier, BenchmarkRun later)
    {
        HarnessImpact impact = HarnessImpact.None;
        foreach (var a in HarnessVersionsOf(earlier))
        {
            foreach (var b in HarnessVersionsOf(later))
            {
                impact |= HarnessImpactLedger.ImpactBetween(a, b);
            }
        }

        return impact;
    }

    /// <summary><c>53</c>, or <c>52 (re-run 53)</c> for a run partly re-executed under another harness.</summary>
    private static string HarnessIdentity(BenchmarkRun run)
    {
        var versions = HarnessVersionsOf(run);
        string main = versions[0] ?? BenchmarkComparabilityKey.NoValue;
        return versions.Count == 1 ? main : main + " (re-run " + versions[1] + ")";
    }

    private static Dictionary<string, string> KeyValues(BenchmarkRun run)
        => BenchmarkComparabilityKey.Extract(run)
            .GroupBy(k => k.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Value, StringComparer.Ordinal);

    private static bool SameKey(Dictionary<string, string> a, Dictionary<string, string> b, string name)
    {
        a.TryGetValue(name, out var va);
        b.TryGetValue(name, out var vb);
        return string.Equals(va, vb, StringComparison.Ordinal);
    }

    private static string SuiteIdentity(BenchmarkRun run)
        => (run.BenchmarkSuiteIdUsed ?? run.BenchmarkSuiteId) is long id
            ? "id:" + Inv(id)
            : "name:" + (run.SuiteName ?? string.Empty);

    private static bool SameSuite(BenchmarkRun a, BenchmarkRun b)
    {
        long? ida = a.BenchmarkSuiteIdUsed ?? a.BenchmarkSuiteId;
        long? idb = b.BenchmarkSuiteIdUsed ?? b.BenchmarkSuiteId;
        if (ida.HasValue && idb.HasValue) return ida.Value == idb.Value;

        return !string.IsNullOrWhiteSpace(a.SuiteName)
            && string.Equals(a.SuiteName, b.SuiteName, StringComparison.Ordinal);
    }

    private static string Sentence(IEnumerable<string> parts)
    {
        string joined = string.Join("; ", parts);
        return joined.Length == 0 ? joined : char.ToUpperInvariant(joined[0]) + joined.Substring(1) + ".";
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Inv(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Sha256Hex(string value)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    /// <summary>
    /// Questions in numeric id order, then unlinked <c>order:N</c> questions by N, then anything else
    /// ordinally.
    /// </summary>
    private sealed class QuestionComparer : IComparer<string>
    {
        public static readonly QuestionComparer Instance = new();

        private const string OrderPrefix = "order:";

        public int Compare(string? x, string? y)
        {
            var kx = SortKey(x ?? string.Empty);
            var ky = SortKey(y ?? string.Empty);
            int c = kx.Rank.CompareTo(ky.Rank);
            if (c != 0) return c;
            c = kx.Number.CompareTo(ky.Number);
            return c != 0 ? c : string.CompareOrdinal(x, y);
        }

        private static (int Rank, long Number) SortKey(string question)
        {
            if (long.TryParse(question, NumberStyles.None, CultureInfo.InvariantCulture, out long id)) return (0, id);
            if (question.StartsWith(OrderPrefix, StringComparison.Ordinal)
                && long.TryParse(question.Substring(OrderPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out long order))
            {
                return (1, order);
            }

            return (2, 0);
        }
    }

    /// <summary>Items by question, then revision with an unrecorded revision first.</summary>
    private sealed class ItemKeyComparer : IComparer<ChatItemKey>
    {
        public static readonly ItemKeyComparer Instance = new();

        public int Compare(ChatItemKey x, ChatItemKey y)
        {
            int c = QuestionComparer.Instance.Compare(x.Question, y.Question);
            return c != 0 ? c : Nullable.Compare(x.Revision, y.Revision);
        }
    }
}
