namespace Overseer.Services.ChatConsistency;

using System;
using System.Collections.Generic;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using MobileGnollHackLogger.Data;

// The request, result and view records of the GnollBench chat consistency analysis, in one place for
// the API, the reports and the UI. Every record serializes as camelCase JSON with enums as strings
// (ChatConsistencyJson.Options); collections are ordered, so a fixed input yields fixed JSON.

/// <summary>The serializer settings of every stored and returned chat consistency record.</summary>
public static class ChatConsistencyJson
{
    public static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}

/// <summary>A request the analysis refuses: the message says why, for the API to return as a 400.</summary>
public sealed class ChatConsistencyRequestException : Exception
{
    public ChatConsistencyRequestException(string message) : base(message)
    {
    }
}

/// <summary>How strongly a verdict or an attribution is supported.</summary>
public enum ChatConsistencyEvidenceGrade
{
    /// <summary>Publishable: a decisive verdict, every robustness check passed, the minimum sample met, telemetry-grade data, no relaxed pooling.</summary>
    Established = 0,

    /// <summary>A decisive verdict with one of: a failed robustness check, legacy data, relaxed pooling, a sample below the minimum.</summary>
    Indicated = 1,

    /// <summary>Inconclusive, or nothing to support a claim.</summary>
    NotEstablished = 2
}

/// <summary>The outcome of one robustness check.</summary>
public enum ChatConsistencyCheckStatus
{
    Passed = 0,
    Failed = 1,

    /// <summary>The data cannot run the check. Does not fail the grade.</summary>
    NotAssessable = 2
}

// --- Request -------------------------------------------------------------------------------------

/// <summary>What to analyze: one subject (a model axis key) over a baseline and a comparison period.</summary>
public sealed record ChatConsistencyAnalysisRequest
{
    public string? Name { get; init; }

    /// <summary>The subject, as <see cref="ChatConsistencyComparability.ModelAxisKey"/> renders it.</summary>
    public string SubjectModelKey { get; init; } = string.Empty;

    /// <summary>UTC; a run belongs to a period when start ≤ its start ≤ end.</summary>
    public DateTime BaselineStartUtc { get; init; }

    public DateTime BaselineEndUtc { get; init; }

    public DateTime ComparisonStartUtc { get; init; }

    public DateTime ComparisonEndUtc { get; init; }

    /// <summary>When set, exactly these runs form the baseline (each must be of the subject).</summary>
    public IReadOnlyList<long>? BaselineRunIds { get; init; }

    /// <summary>When set, exactly these runs form the comparison (each must be of the subject).</summary>
    public IReadOnlyList<long>? ComparisonRunIds { get; init; }

    /// <summary>When set, the candidate control runs; otherwise every other subject's run on the target suites in either period.</summary>
    public IReadOnlyList<long>? ControlRunIds { get; init; }

    public ChatConsistencyProtocolOverrides? ProtocolOverrides { get; init; }

    /// <summary>Pool across a measurement segment boundary instead of refusing; caps the affected grades at Indicated.</summary>
    public bool RelaxedPooling { get; init; }

    /// <summary>The assessor snapshot whose calibration verdicts serve as the common grader; null selects one automatically.</summary>
    public long? CommonGraderSnapshotId { get; init; }

    /// <summary>Models the operator can run as controls, as <c>provider/model</c>, for the next-run suggestions.</summary>
    public IReadOnlyList<string>? AvailableOtherProviderModels { get; init; }

    /// <summary>How the operator chose the runs in the wizard's step 1; recorded with the result, never used to pick runs.</summary>
    public ChatConsistencyRunSelection? RunSelection { get; init; }

    /// <summary>The battery or suite compared within; null analyzes the runs one by one, as analysis code version 3 did.</summary>
    public ChatConsistencyComparisonSetRef? ComparisonSet { get; init; }

    /// <summary>With a battery set, exactly these battery runs form the baseline; their usable members are analyzed.</summary>
    public IReadOnlyList<long>? BaselineBatteryRunIds { get; init; }

    /// <summary>With a battery set, exactly these battery runs form the comparison.</summary>
    public IReadOnlyList<long>? ComparisonBatteryRunIds { get; init; }
}

/// <summary>The kinds of <see cref="ChatConsistencyComparisonSetRef"/>, and the unit each analyzes.</summary>
public static class ChatConsistencyComparisonSetKinds
{
    /// <summary>One battery definition, every revision with that <c>DefinitionSha256</c>; key <c>battery:&lt;sha256&gt;</c>.</summary>
    public const string Battery = "battery";

    /// <summary>One suite; key <c>suite:&lt;suite identity&gt;</c> as <see cref="ChatConsistencyMeasures.SuiteIdentity"/> renders it.</summary>
    public const string Suite = "suite";

    public const string BatteryKeyPrefix = "battery:";
    public const string SuiteKeyPrefix = "suite:";

    /// <summary>The unit kind of a run-by-run analysis and of a suite set.</summary>
    public const string RunUnit = "run";

    /// <summary>The unit kind of a battery set.</summary>
    public const string BatteryRunUnit = "batteryRun";
}

/// <summary>The battery or suite an analysis compares within, as the request names it.</summary>
public sealed record ChatConsistencyComparisonSetRef
{
    /// <summary><c>battery</c> or <c>suite</c>.</summary>
    public string Kind { get; init; } = string.Empty;

    /// <summary><c>battery:&lt;DefinitionSha256&gt;</c> or <c>suite:&lt;suite identity&gt;</c>.</summary>
    public string Key { get; init; } = string.Empty;
}

/// <summary>The battery or suite an analysis compared within, as recorded with the result.</summary>
public sealed record ChatConsistencyComparedSet
{
    public string Kind { get; init; } = string.Empty;
    public string Key { get; init; } = string.Empty;

    /// <summary>The battery name with its revisions, for example "Two initial suites (revision 1)", or the suite name.</summary>
    public string Label { get; init; } = string.Empty;
}

/// <summary>A battery or suite the subject can be compared within, over the step-1 dates.</summary>
public sealed record ChatConsistencyComparisonSet
{
    /// <summary><c>battery</c> or <c>suite</c>.</summary>
    public string Kind { get; init; } = string.Empty;
    public string Key { get; init; } = string.Empty;

    /// <summary>The battery name with its revisions joined ("Two initial suites (revisions 1, 2)"), or the suite name.</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>Battery runs of the set (battery) or runs of the suite (suite) of the subject in the dates.</summary>
    public int UnitCount { get; init; }

    /// <summary>The runs behind <see cref="UnitCount"/>; equal to it for a suite.</summary>
    public int MemberRunCount { get; init; }
    public DateTime LatestStartedAtUtc { get; init; }
}

/// <summary>The sets the subject can be compared within, and the one step 1 selects by default.</summary>
public sealed record ChatConsistencyComparisonSets
{
    /// <summary>Battery sets first, then suite sets; each group newest first by <see cref="ChatConsistencyComparisonSet.LatestStartedAtUtc"/>.</summary>
    public IReadOnlyList<ChatConsistencyComparisonSet> Sets { get; init; } = Array.Empty<ChatConsistencyComparisonSet>();

    /// <summary>The battery set of the subject's newest battery run in the dates, else the suite of its newest run; null when there is no run.</summary>
    public string? DefaultKey { get; init; }
}

/// <summary>One analyzed unit: a battery run in a battery set, a run otherwise.</summary>
public sealed record ChatConsistencyUnitView
{
    /// <summary>The battery run id, or the run id.</summary>
    public long UnitId { get; init; }

    /// <summary><c>run</c> or <c>batteryRun</c>.</summary>
    public string Kind { get; init; } = string.Empty;

    /// <summary><c>baseline</c> or <c>comparison</c>.</summary>
    public string Period { get; init; } = string.Empty;
    public DateTime StartedAtUtc { get; init; }

    /// <summary>The runs merged into the unit, ordered by start, then id; the run itself for a run unit.</summary>
    public IReadOnlyList<long> MemberRunIds { get; init; } = Array.Empty<long>();
}

/// <summary>How the operator chose the runs in the wizard's step 1; recorded, never used to pick runs.</summary>
public sealed record ChatConsistencyRunSelection
{
    /// <summary>The step-1 dates as shown, for example "Last 30 days"; at most 64 characters.</summary>
    public string? RangeLabel { get; init; }
    public DateTime? RangeFromUtc { get; init; }
    public DateTime? RangeToUtc { get; init; }
    public long? FirstRunId { get; init; }
    public long? LastRunId { get; init; }

    /// <summary>Runs the operator unchecked; at most 5,000.</summary>
    public IReadOnlyList<long>? LeftOutRunIds { get; init; }

    /// <summary>In a battery set, the step-1 first and last battery runs.</summary>
    public long? FirstBatteryRunId { get; init; }
    public long? LastBatteryRunId { get; init; }

    /// <summary>In a battery set, the battery runs the operator unchecked; at most 5,000.</summary>
    public IReadOnlyList<long>? LeftOutBatteryRunIds { get; init; }
}

// --- Result --------------------------------------------------------------------------------------

/// <summary>The model a chat consistency analysis is about.</summary>
public sealed record ChatConsistencySubject
{
    public string Key { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Provider { get; init; } = string.Empty;
    public string ModelId { get; init; } = string.Empty;
    public string? ThinkingLevel { get; init; }
    public string? ServiceTier { get; init; }

    /// <summary>The latest run's configuration id; attribution only.</summary>
    public long? ConfigurationId { get; init; }
}

/// <summary>One period as analyzed.</summary>
public sealed record ChatConsistencyPeriodSummary
{
    /// <summary><c>baseline</c> or <c>comparison</c>.</summary>
    public string Name { get; init; } = string.Empty;
    public DateTime StartUtc { get; init; }
    public DateTime EndUtc { get; init; }
    public IReadOnlyList<long> RunIds { get; init; } = Array.Empty<long>();
    public int RunCount { get; init; }

    /// <summary>The distinct UTC days the runs started on, <c>yyyy-MM-dd</c>, ascending.</summary>
    public IReadOnlyList<string> Days { get; init; } = Array.Empty<string>();
    public int AnswerCount { get; init; }

    /// <summary>Distinct items (question and revision) answered.</summary>
    public int ItemCount { get; init; }
    public IReadOnlyList<string> SuiteNames { get; init; } = Array.Empty<string>();

    /// <summary>Runs without call telemetry.</summary>
    public int LegacyRunCount { get; init; }
}

/// <summary>Where in the week the comparison holds: the time strata both periods sampled.</summary>
public sealed record ChatConsistencyScope
{
    /// <summary>For example "weekdays 04–12 UTC; weekends 16–20 UTC".</summary>
    public string Text { get; init; } = string.Empty;
    public IReadOnlyList<int> StrataIndexes { get; init; } = Array.Empty<int>();
    public IReadOnlyList<string> StrataUsed { get; init; } = Array.Empty<string>();

    /// <summary>Answers outside the common strata, as a share of all timed answers.</summary>
    public double ExcludedShare { get; init; }
    public bool OneTimeStratum { get; init; }

    /// <summary>The common strata hold a US-business-hours block and a block outside them.</summary>
    public bool TimeOfDayAssessable { get; init; }
    public bool UsBusinessHoursCovered { get; init; }
    public bool OutsideBusinessHoursCovered { get; init; }
}

/// <summary>A two-sided interval.</summary>
public sealed record ChatConsistencyInterval(double Lower, double Upper);

/// <summary>One robustness check of one endpoint.</summary>
public sealed record ChatConsistencyCheck
{
    public string EndpointId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public ChatConsistencyCheckStatus Status { get; init; }
    public string Detail { get; init; } = string.Empty;
}

/// <summary>One primary endpoint's result.</summary>
public sealed record ChatConsistencyEndpointResult
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Unit { get; init; } = string.Empty;
    public ChatConsistencyEffectScale Scale { get; init; }
    public double Margin { get; init; }
    public string MarginText { get; init; } = string.Empty;

    /// <summary><c>higherIsBetter</c>, <c>lowerIsBetter</c>, or <c>work</c> (more / less work, not a quality judgment).</summary>
    public string Direction { get; init; } = string.Empty;

    public bool Computed { get; init; }
    public string? NotComputedReason { get; init; }

    /// <summary>Why the endpoint was not computed, one of <see cref="ChatConsistencyNotComputedKinds"/>; null when computed and before analysis code version 6.</summary>
    public string? NotComputedKind { get; init; }

    /// <summary>Comparison minus baseline on the effect scale.</summary>
    public double? Estimate { get; init; }

    /// <summary>For a log-ratio endpoint, 100 · (e^estimate − 1).</summary>
    public double? EstimatePercent { get; init; }
    public ChatConsistencyInterval? Ci95 { get; init; }
    public ChatConsistencyInterval? Ci90 { get; init; }
    public ChatConsistencyInterval? Ci95Percent { get; init; }
    public double? PValue { get; init; }

    /// <summary>Holm-adjusted across the computed primary endpoints.</summary>
    public double? AdjustedPValue { get; init; }
    public string PValueMethod { get; init; } = string.Empty;
    public ConsistencyVerdict? Verdict { get; init; }

    /// <summary>"degraded", "improved", "more work", "less work", "changed, negligible", "equivalent", "inconclusive" or "not computable".</summary>
    public string VerdictLabel { get; init; } = string.Empty;
    public ChatConsistencyEvidenceGrade Grade { get; init; } = ChatConsistencyEvidenceGrade.NotEstablished;
    public IReadOnlyList<string> GradeReasons { get; init; } = Array.Empty<string>();

    /// <summary>The minimum detectable effect at the protocol's α and power, on the effect scale.</summary>
    public double? MinimumDetectableEffect { get; init; }
    public double? MinimumDetectableEffectPercent { get; init; }
    public string? MinimumDetectableEffectNote { get; init; }

    /// <summary>Runs per period that would bring the MDE down to the margin, from the run-to-run SD; null when not estimable.</summary>
    public int? RunsPerPeriodForMargin { get; init; }
    public bool MinimumSampleMet { get; init; }
    public string MinimumSampleDetail { get; init; } = string.Empty;

    /// <summary>P2 measured as legacy model time per item, not telemetry time to first answer text.</summary>
    public bool LegacyProxy { get; init; }

    /// <summary>Any compared run lacks call telemetry.</summary>
    public bool UsesLegacyData { get; init; }
    public bool CommonGrader { get; init; }

    /// <summary>The runs span more than one measurement segment and were pooled.</summary>
    public bool RelaxedPooling { get; init; }
    public int BaselineRunCount { get; init; }
    public int ComparisonRunCount { get; init; }
    public IReadOnlyList<long> BaselineRunIds { get; init; } = Array.Empty<long>();
    public IReadOnlyList<long> ComparisonRunIds { get; init; } = Array.Empty<long>();

    /// <summary>Paired items the estimate rests on.</summary>
    public int ItemCount { get; init; }
    public IReadOnlyList<string> StrataUsed { get; init; } = Array.Empty<string>();

    /// <summary>Observations outside the common strata, as a share (stratified endpoints).</summary>
    public double? StratumExcludedShare { get; init; }
    public IReadOnlyList<ChatConsistencyCheck> RobustnessChecks { get; init; } = Array.Empty<ChatConsistencyCheck>();
}

/// <summary>The values of <see cref="ChatConsistencyEndpointResult.NotComputedKind"/>.</summary>
public static class ChatConsistencyNotComputedKinds
{
    /// <summary>The periods share no measurement segment on the endpoint's axis.</summary>
    public const string MeasurementChanged = "measurementChanged";

    /// <summary>The periods share no time-of-week stratum.</summary>
    public const string NoCommonStratum = "noCommonStratum";

    /// <summary>A period has no run with the call telemetry the endpoint needs.</summary>
    public const string NoTelemetry = "noTelemetry";

    /// <summary>No price card resolves.</summary>
    public const string NoPricing = "noPricing";

    /// <summary>No item has a value in both periods.</summary>
    public const string TooFewPairs = "tooFewPairs";

    public const string Other = "other";
}

/// <summary>The hours one period's analyzed answers started in.</summary>
public sealed record ChatConsistencyPeriodHours
{
    /// <summary><c>baseline</c> or <c>comparison</c>.</summary>
    public string Period { get; init; } = string.Empty;

    /// <summary>The time-of-week strata, as <see cref="ChatConsistencyStatistics.StratumLabel"/> renders them, ascending.</summary>
    public IReadOnlyList<string> Strata { get; init; } = Array.Empty<string>();

    /// <summary>For example "weekdays 04–08 UTC"; "no timed answers" when there is none.</summary>
    public string Text { get; init; } = string.Empty;
}

/// <summary>Descriptive levels of one period over its analyzed units; not a comparison.</summary>
public sealed record ChatConsistencyPeriodLevels
{
    /// <summary><c>baseline</c> or <c>comparison</c>.</summary>
    public string Period { get; init; } = string.Empty;
    public int AnswerCount { get; init; }

    /// <summary>The mean of the native per-answer quality scores.</summary>
    public double? NativeMeanQuality { get; init; }

    /// <summary>
    /// The battery Overall Index when every unit is a battery run with a current stored battery analysis:
    /// the unit's index, or the mean of the units' indexes.
    /// </summary>
    public double? OverallIndex { get; init; }

    /// <summary>The 95 % half-width of <see cref="OverallIndex"/>; only with one unit.</summary>
    public double? OverallIndexHalfWidth { get; init; }

    /// <summary>"question sampling only" when the half-width rests on one round; else null.</summary>
    public string? OverallIndexIntervalNote { get; init; }

    /// <summary>The median time to first answer text over delivered answers with call telemetry.</summary>
    public double? MedianTimeToFirstAnswerTextMs { get; init; }

    /// <summary>The median answer streaming rate, tokens per second.</summary>
    public double? MedianStreamingRate { get; init; }

    /// <summary>The mean output tokens per delivered answer.</summary>
    public double? MeanOutputTokensPerAnswer { get; init; }

    /// <summary>The mean cost per delivered answer in US dollars at the analysis's price card.</summary>
    public double? MeanCostPerQuestionUsd { get; init; }

    /// <summary>Terminal failures among <see cref="AnswerCount"/>.</summary>
    public int FailedAnswerCount { get; init; }
}

/// <summary>Whether a saved analysis is out of date, and why.</summary>
public sealed record ChatConsistencyFreshness
{
    public int AnalysisId { get; init; }
    public int AnalysisCodeVersion { get; init; }
    public int CurrentAnalysisCodeVersion { get; init; }

    /// <summary>Saved under an earlier analysis code version than the running Overseer's.</summary>
    public bool EarlierAnalysisCode { get; init; }

    /// <summary>The input fingerprint recomputed today differs from the stored one; null when not checked.</summary>
    public bool? InputsChanged { get; init; }

    /// <summary>Why <see cref="InputsChanged"/> is null; null when it was checked.</summary>
    public string? InputsNote { get; init; }

    public bool OutOfDate { get; init; }
}

/// <summary>One secondary result.</summary>
public sealed record ChatConsistencySecondaryResult
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Unit { get; init; } = string.Empty;
    public double? BaselineValue { get; init; }
    public double? ComparisonValue { get; init; }

    /// <summary>Comparison minus baseline, in <see cref="Unit"/>.</summary>
    public double? Estimate { get; init; }
    public ChatConsistencyInterval? Ci95 { get; init; }
    public double? PValue { get; init; }

    /// <summary>Benjamini–Hochberg adjusted within the family.</summary>
    public double? AdjustedPValue { get; init; }
    public bool Rejected { get; init; }
    public string Method { get; init; } = string.Empty;
    public int? ItemCount { get; init; }
    public ConsistencyVerdict? Verdict { get; init; }
    public string? Note { get; init; }
}

/// <summary>One secondary family, its p-values adjusted together.</summary>
public sealed record ChatConsistencyFamilyResult
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public IReadOnlyList<ChatConsistencySecondaryResult> Results { get; init; } = Array.Empty<ChatConsistencySecondaryResult>();
    public string? Note { get; init; }
}

/// <summary>One answer-level or call-level rate compared between the periods.</summary>
public sealed record ChatConsistencyRateResult
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;

    /// <summary><c>answers</c> or <c>calls</c>.</summary>
    public string Denominator { get; init; } = string.Empty;
    public int BaselineCount { get; init; }
    public int BaselineTotal { get; init; }
    public double? BaselineRate { get; init; }
    public ChatConsistencyInterval? BaselineCi95 { get; init; }
    public int ComparisonCount { get; init; }
    public int ComparisonTotal { get; init; }
    public double? ComparisonRate { get; init; }
    public ChatConsistencyInterval? ComparisonCi95 { get; init; }

    /// <summary>Fisher's exact test, two-sided.</summary>
    public double? PValue { get; init; }

    /// <summary>Benjamini–Hochberg adjusted within the reliability family.</summary>
    public double? AdjustedPValue { get; init; }
    public bool Increased { get; init; }

    /// <summary>Rejected at the family's false discovery rate, higher in the comparison, and the run minimum met in both periods.</summary>
    public bool EstablishedIncrease { get; init; }
}

/// <summary>An Overseer change dated by the run that first shows it.</summary>
public sealed record ChatConsistencyEventView
{
    public DateTime AtUtc { get; init; }
    public string Kind { get; init; } = string.Empty;

    /// <summary>For example "tool guides edited on 2026-10-03".</summary>
    public string Label { get; init; } = string.Empty;
    public string? From { get; init; }
    public string? To { get; init; }
    public long RunId { get; init; }
    public long PreviousRunId { get; init; }
    public string SubjectKey { get; init; } = string.Empty;

    /// <summary>Detected in the subject's own series, rather than only in a control's.</summary>
    public bool InTargetSeries { get; init; }
}

/// <summary>A measurement change between two consecutive runs of one subject.</summary>
public sealed record ChatConsistencyBoundaryView
{
    public string SubjectKey { get; init; } = string.Empty;
    public long FromRunId { get; init; }
    public long ToRunId { get; init; }
    public DateTime AtUtc { get; init; }
    public MeasurementChangeKind Kind { get; init; }
    public IReadOnlyList<ChatConsistencyAxis> Axes { get; init; } = Array.Empty<ChatConsistencyAxis>();
    public bool Bridged { get; init; }
    public string Reason { get; init; } = string.Empty;
}

/// <summary>One run's segment on each axis; null = excluded from that axis.</summary>
public sealed record ChatConsistencySegmentView
{
    public long RunId { get; init; }
    public DateTime StartedAtUtc { get; init; }

    /// <summary><c>baseline</c>, <c>comparison</c> or <c>control</c>.</summary>
    public string Role { get; init; } = string.Empty;
    public int? Quality { get; init; }
    public int? SpeedTelemetry { get; init; }
    public int? SpeedLegacy { get; init; }
    public int? Work { get; init; }
    public int? Cost { get; init; }
}

/// <summary>A control run qualified for a target run.</summary>
public sealed record ChatConsistencyControlMatchView
{
    public string Period { get; init; } = string.Empty;
    public long TargetRunId { get; init; }
    public long ControlRunId { get; init; }
    public string ControlSubjectKey { get; init; } = string.Empty;
    public int PairedItemCount { get; init; }
}

/// <summary>A period without a qualifying control run, and the run that would close the gap.</summary>
public sealed record ChatConsistencyMissingControlView
{
    public string Period { get; init; } = string.Empty;
    public string SuiteName { get; init; } = string.Empty;
    public string Fingerprint { get; init; } = string.Empty;
    public string SuggestedText { get; init; } = string.Empty;
    public long TargetRunId { get; init; }

    /// <summary>In a battery comparison, the battery run the note is about; its suites are <see cref="SuiteName"/>, joined.</summary>
    public long? BatteryRunId { get; init; }

    /// <summary>The target's Overseer build no longer runs, so no control run can be made under it.</summary>
    public bool BuildReplaced { get; init; }
}

/// <summary>One control subject's own change on one endpoint, and the target's difference-in-differences against it.</summary>
public sealed record ChatConsistencyControlEffect
{
    public string EndpointId { get; init; } = string.Empty;
    public string ControlSubjectKey { get; init; } = string.Empty;
    public string ControlDisplay { get; init; } = string.Empty;
    public string ControlProvider { get; init; } = string.Empty;
    public bool SameProvider { get; init; }
    public IReadOnlyList<long> ControlBaselineRunIds { get; init; } = Array.Empty<long>();
    public IReadOnlyList<long> ControlComparisonRunIds { get; init; } = Array.Empty<long>();
    public int ItemCount { get; init; }

    /// <summary>The control's own comparison-minus-baseline change, on the endpoint's scale.</summary>
    public double? ControlChange { get; init; }
    public ChatConsistencyInterval? ControlChangeCi95 { get; init; }

    /// <summary>(target change) − (control change), item-paired.</summary>
    public double? DidEstimate { get; init; }
    public ChatConsistencyInterval? DidCi95 { get; init; }

    /// <summary>Run-cluster bootstrap, two-sided.</summary>
    public double? DidPValue { get; init; }
    public bool DidIncludesZero { get; init; }

    /// <summary>The DiD interval excludes zero on the side of the target's own change.</summary>
    public bool DidSeparatesTarget { get; init; }

    /// <summary>The control's own change has the sign of the target's change.</summary>
    public bool ControlMovedSameWay { get; init; }
}

/// <summary>The control runs and what they say.</summary>
public sealed record ChatConsistencyControls
{
    public IReadOnlyList<ChatConsistencyControlMatchView> Matches { get; init; } = Array.Empty<ChatConsistencyControlMatchView>();
    public IReadOnlyList<ChatConsistencyControlEffect> Effects { get; init; } = Array.Empty<ChatConsistencyControlEffect>();
    public IReadOnlyList<ChatConsistencyMissingControlView> MissingControls { get; init; } = Array.Empty<ChatConsistencyMissingControlView>();
    public IReadOnlyList<long> ControlRunIds { get; init; } = Array.Empty<long>();
}

/// <summary>An endpoint's total change, reported before any attribution.</summary>
public sealed record ChatConsistencyTotalChange
{
    public string EndpointId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string VerdictLabel { get; init; } = string.Empty;
    public ChatConsistencyEvidenceGrade Grade { get; init; }
}

/// <summary>One row of the attribution decision table that fired. Never names a mechanism or an intent.</summary>
public sealed record ChatConsistencyAttributionResult
{
    public string Label { get; init; } = string.Empty;

    /// <summary><c>ours</c>, <c>provider</c>, <c>infrastructure</c> or <c>undetermined</c>.</summary>
    public string Side { get; init; } = string.Empty;
    public ChatConsistencyEvidenceGrade Grade { get; init; }

    /// <summary>The decision-table row id, see <see cref="ChatConsistencyAttribution"/>.</summary>
    public string Rule { get; init; } = string.Empty;
    public IReadOnlyList<string> Endpoints { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> EventRefs { get; init; } = Array.Empty<string>();
    public string Evidence { get; init; } = string.Empty;
}

/// <summary>The total changes and the attributions, in that order.</summary>
public sealed record ChatConsistencyAttributionOutcome
{
    public IReadOnlyList<ChatConsistencyTotalChange> TotalChanges { get; init; } = Array.Empty<ChatConsistencyTotalChange>();
    public IReadOnlyList<ChatConsistencyAttributionResult> Attributions { get; init; } = Array.Empty<ChatConsistencyAttributionResult>();
}

/// <summary>A served model id and how many candidate calls reported it.</summary>
public sealed record ChatConsistencyServedModelCount(string ModelId, int CallCount);

/// <summary>What the provider reported serving in each period.</summary>
public sealed record ChatConsistencyServedModels
{
    public IReadOnlyList<ChatConsistencyServedModelCount> Baseline { get; init; } = Array.Empty<ChatConsistencyServedModelCount>();
    public IReadOnlyList<ChatConsistencyServedModelCount> Comparison { get; init; } = Array.Empty<ChatConsistencyServedModelCount>();

    /// <summary>Both periods recorded served ids and the sets differ.</summary>
    public bool Changed { get; init; }
    public int BaselineCalls { get; init; }
    public int ComparisonCalls { get; init; }
    public int BaselineTierMismatchCalls { get; init; }
    public int ComparisonTierMismatchCalls { get; init; }
    public int BaselineFallbackCalls { get; init; }
    public int ComparisonFallbackCalls { get; init; }
    public IReadOnlyList<string> BaselineServedSpeeds { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ComparisonServedSpeeds { get; init; } = Array.Empty<string>();

    /// <summary>Some call was served at another tier than requested, or by a fallback model.</summary>
    public bool ServedConfigurationDiffers { get; init; }
}

/// <summary>Overseer's own waiting inside the candidate's turns in one period.</summary>
public sealed record ChatConsistencyOwnWaits
{
    public string Period { get; init; } = string.Empty;
    public long PermitWaitMs { get; init; }
    public long BackoffWaitMs { get; init; }
    public long ModelTimeMs { get; init; }

    /// <summary>(permit + backoff) ÷ model time over answers with call telemetry; null without any.</summary>
    public double? OwnWaitShare { get; init; }
    public int RetryAttemptCount { get; init; }
    public int AnswersWithTelemetry { get; init; }
}

/// <summary>The common grader P1 used.</summary>
public sealed record ChatConsistencyCommonGrader
{
    public long SnapshotId { get; init; }
    public string Display { get; init; } = string.Empty;
    public bool Requested { get; init; }
    public IReadOnlyList<long> CalibrationIds { get; init; } = Array.Empty<long>();
    public IReadOnlyList<long> CoveredRunIds { get; init; } = Array.Empty<long>();

    /// <summary>Control runs it does not cover; they are left out of the quality difference-in-differences.</summary>
    public IReadOnlyList<long> UncoveredControlRunIds { get; init; } = Array.Empty<long>();
}

/// <summary>Grader drift measured on an anchor run re-graded by one snapshot on different dates.</summary>
public sealed record ChatConsistencyGraderDrift
{
    public long AnchorRunId { get; init; }
    public long SnapshotId { get; init; }
    public string Display { get; init; } = string.Empty;
    public DateTime EarliestAtUtc { get; init; }
    public DateTime LatestAtUtc { get; init; }

    /// <summary>Mean quality of the latest re-grade minus the earliest, over the answers both graded.</summary>
    public double Drift { get; init; }
    public int ItemCount { get; init; }
    public bool WithinMargin { get; init; }
}

/// <summary>The one price card every compared run is costed at.</summary>
public sealed record ChatConsistencyPriceCard
{
    public bool Available { get; init; }

    /// <summary>"current configuration pricing", "latest run's pricing snapshot", or "none".</summary>
    public string Source { get; init; } = string.Empty;
    public long? RunId { get; init; }
    public decimal? InputPerMillion { get; init; }
    public decimal? OutputPerMillion { get; init; }
    public decimal? CachedInputPerMillion { get; init; }
    public decimal? CacheWritePerMillion { get; init; }
    public string? AsOf { get; init; }
}

/// <summary>A data-quality or limitation note.</summary>
public sealed record ChatConsistencyNote
{
    public string Kind { get; init; } = string.Empty;
    public string Text { get; init; } = string.Empty;
}

/// <summary>A usable run of the subject inside a period that the analysis did not use.</summary>
public sealed record ChatConsistencyUnanalyzedRun
{
    public long RunId { get; init; }

    /// <summary><c>baseline</c> or <c>comparison</c>.</summary>
    public string Period { get; init; } = string.Empty;
    public DateTime StartedAtUtc { get; init; }

    /// <summary><c>leftOut</c>, <c>outsideDateRange</c>, <c>beforeFirstRun</c>, <c>afterLastRun</c>, <c>notSelected</c> or <c>outsideComparisonSet</c>.</summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>In a battery set, the battery run the reason applies to; null for a run of a suite set or outside the compared set.</summary>
    public long? BatteryRunId { get; init; }
}

/// <summary>Why a usable run of the subject in a period was not analyzed; <see cref="All"/> is the order they are tested in.</summary>
public static class ChatConsistencyUnanalyzedReasons
{
    /// <summary>The operator unchecked it in step 1.</summary>
    public const string LeftOut = "leftOut";

    /// <summary>It started outside the step-1 dates.</summary>
    public const string OutsideDateRange = "outsideDateRange";

    /// <summary>It comes before the step-1 first run, by start, then id.</summary>
    public const string BeforeFirstRun = "beforeFirstRun";

    /// <summary>It comes after the step-1 last run, by start, then id.</summary>
    public const string AfterLastRun = "afterLastRun";

    /// <summary>None of the above: not assigned to a period in step 3.</summary>
    public const string NotSelected = "notSelected";

    /// <summary>It is not part of the compared battery or suite.</summary>
    public const string OutsideComparisonSet = "outsideComparisonSet";

    public static readonly IReadOnlyList<string> All = new[] { LeftOut, OutsideDateRange, BeforeFirstRun, AfterLastRun, NotSelected, OutsideComparisonSet };
}

/// <summary>The run selection as recorded with the analysis.</summary>
public sealed record ChatConsistencyRunSelectionView
{
    /// <summary>The request carried a selection; false for analyses saved before it was recorded.</summary>
    public bool Recorded { get; init; }
    public string? RangeLabel { get; init; }
    public DateTime? RangeFromUtc { get; init; }
    public DateTime? RangeToUtc { get; init; }
    public long? FirstRunId { get; init; }
    public long? LastRunId { get; init; }
    public IReadOnlyList<long> LeftOutRunIds { get; init; } = Array.Empty<long>();
    public long? FirstBatteryRunId { get; init; }
    public long? LastBatteryRunId { get; init; }
    public IReadOnlyList<long> LeftOutBatteryRunIds { get; init; } = Array.Empty<long>();

    /// <summary>Ordered by start, then id.</summary>
    public IReadOnlyList<ChatConsistencyUnanalyzedRun> UnanalyzedRuns { get; init; } = Array.Empty<ChatConsistencyUnanalyzedRun>();
}

/// <summary>The kinds of <see cref="ChatConsistencyNextRun"/>.</summary>
public static class ChatConsistencyNextRunKinds
{
    public const string Checkpoint = "checkpoint";
    public const string Control = "control";
    public const string Stratum = "stratum";
    public const string Regrade = "regrade";

    /// <summary>A new baseline under the current build, because the comparison period's build has been replaced.</summary>
    public const string NewCheckpoint = "newCheckpoint";
}

/// <summary>
/// A run that would resolve an inconclusive or unattributable verdict. A <c>newCheckpoint</c> suggestion
/// also carries the structured fields from <see cref="UnitNoun"/> to <see cref="SubjectModelConfigurationId"/>,
/// so a launcher can pre-fill it; every other kind leaves them null.
/// </summary>
public sealed record ChatConsistencyNextRun
{
    /// <summary><c>checkpoint</c>, <c>control</c>, <c>stratum</c>, <c>regrade</c> or <c>newCheckpoint</c> (<see cref="ChatConsistencyNextRunKinds"/>).</summary>
    public string Kind { get; init; } = string.Empty;

    /// <summary><c>baseline</c>, <c>comparison</c> or <c>both</c>; empty for a <c>newCheckpoint</c>, which belongs to neither period.</summary>
    public string Period { get; init; } = string.Empty;
    public string? EndpointId { get; init; }
    public string Reason { get; init; } = string.Empty;
    public string Suggestion { get; init; } = string.Empty;

    /// <summary>The run whose setup to repeat ("Repeat this run's setup"); null for a re-grade.</summary>
    public long? RepeatRunId { get; init; }

    /// <summary><c>run</c> or <c>battery run</c>: what <see cref="Count"/> counts.</summary>
    public string? UnitNoun { get; init; }

    /// <summary>How many units to make: the protocol's minimum units per period.</summary>
    public int? Count { get; init; }

    /// <summary>On how many different UTC days: the protocol's minimum days per period.</summary>
    public int? Days { get; init; }

    /// <summary>
    /// The time-of-week stratum each unit should start in, as <see cref="ChatConsistencyStatistics.StratumLabel"/>
    /// names it (<c>Weekday 12–16 UTC</c>): the comparison period's most populated stratum; null when it has no timed answer.
    /// </summary>
    public string? Stratum { get; init; }

    /// <summary><c>battery</c> or <c>suite</c> (<see cref="ChatConsistencyComparisonSetKinds"/>): what each unit is run on.</summary>
    public string? TargetKind { get; init; }

    /// <summary>The suite of the latest comparison run, when <see cref="TargetKind"/> is <c>suite</c>.</summary>
    public long? SuiteId { get; init; }

    /// <summary>The battery of the latest comparison battery run, when <see cref="TargetKind"/> is <c>battery</c>; null when it is not known.</summary>
    public long? BatteryId { get; init; }

    /// <summary>The model under test, as <see cref="ChatConsistencyComparability.ModelAxisKey"/> renders it.</summary>
    public string? SubjectModelKey { get; init; }

    /// <summary>The tested model configuration of the subject's latest run (<see cref="ChatConsistencySubject.ConfigurationId"/>).</summary>
    public long? SubjectModelConfigurationId { get; init; }

    /// <summary>A control run of another provider's model is suggested beside each unit.</summary>
    public bool? ControlSuggested { get; init; }
}

/// <summary>An admin annotation on the timeline.</summary>
public sealed record ChatConsistencyAnnotationView
{
    public int Id { get; init; }
    public DateTime AtUtc { get; init; }
    public string? Provider { get; init; }
    public string? ModelId { get; init; }
    public ChatConsistencyAnnotationKind Kind { get; init; }
    public string Text { get; init; } = string.Empty;
    public string? SourceUrl { get; init; }
    public DateTime CreatedAtUtc { get; init; }
}

/// <summary>A new annotation.</summary>
public sealed record ChatConsistencyAnnotationInput
{
    public DateTime AtUtc { get; init; }
    public string? Provider { get; init; }
    public string? ModelId { get; init; }
    public ChatConsistencyAnnotationKind Kind { get; init; }
    public string Text { get; init; } = string.Empty;
    public string? SourceUrl { get; init; }
}

/// <summary>
/// A saved chat consistency analysis. Leads with <see cref="Headline"/>: the verdict on the chat.
/// <see cref="AnalysisId"/> and <see cref="CreatedAtUtc"/> come from the stored row and are not part
/// of <c>ResultJson</c>, so two analyses of the same inputs store identical JSON.
/// </summary>
public sealed record ChatConsistencyAnalysisResult
{
    public int? AnalysisId { get; init; }
    public DateTime? CreatedAtUtc { get; init; }
    public string Name { get; init; } = string.Empty;

    /// <summary>"Overseer chat with &lt;model&gt;: quality …; speed …; work …; cost … within &lt;scope&gt;", plus any established reliability increase.</summary>
    public string Headline { get; init; } = string.Empty;
    public IReadOnlyList<string> HeadlineReliabilityIncreases { get; init; } = Array.Empty<string>();
    public ChatConsistencySubject Subject { get; init; } = new();
    public ChatConsistencyScope Scope { get; init; } = new();
    public ChatConsistencyPeriodSummary Baseline { get; init; } = new();
    public ChatConsistencyPeriodSummary Comparison { get; init; } = new();
    public ChatConsistencyProtocol Protocol { get; init; } = ChatConsistencyProtocol.V1;
    public string ProtocolLabel { get; init; } = string.Empty;
    public IReadOnlyList<ChatConsistencyEndpointResult> Endpoints { get; init; } = Array.Empty<ChatConsistencyEndpointResult>();
    public IReadOnlyList<ChatConsistencyFamilyResult> SecondaryFamilies { get; init; } = Array.Empty<ChatConsistencyFamilyResult>();
    public IReadOnlyList<ChatConsistencyCheck> RobustnessChecks { get; init; } = Array.Empty<ChatConsistencyCheck>();
    public IReadOnlyList<ChatConsistencyRateResult> Reliability { get; init; } = Array.Empty<ChatConsistencyRateResult>();
    public IReadOnlyList<ChatConsistencyEventView> Events { get; init; } = Array.Empty<ChatConsistencyEventView>();
    public IReadOnlyList<ChatConsistencyBoundaryView> Boundaries { get; init; } = Array.Empty<ChatConsistencyBoundaryView>();
    public IReadOnlyList<ChatConsistencySegmentView> Segments { get; init; } = Array.Empty<ChatConsistencySegmentView>();
    public ChatConsistencyControls Controls { get; init; } = new();
    public ChatConsistencyAttributionOutcome Attribution { get; init; } = new();
    public ChatConsistencyServedModels ServedModels { get; init; } = new();
    public IReadOnlyList<ChatConsistencyOwnWaits> OwnWaits { get; init; } = Array.Empty<ChatConsistencyOwnWaits>();
    public ChatConsistencyCommonGrader? CommonGrader { get; init; }
    public IReadOnlyList<ChatConsistencyGraderDrift> GraderDrift { get; init; } = Array.Empty<ChatConsistencyGraderDrift>();
    public ChatConsistencyPriceCard PriceCard { get; init; } = new();
    public IReadOnlyList<ChatConsistencyAnnotationView> Annotations { get; init; } = Array.Empty<ChatConsistencyAnnotationView>();
    public IReadOnlyList<ChatConsistencyNote> DataQuality { get; init; } = Array.Empty<ChatConsistencyNote>();
    public IReadOnlyList<string> Limitations { get; init; } = Array.Empty<string>();
    public IReadOnlyList<ChatConsistencyNextRun> NextRuns { get; init; } = Array.Empty<ChatConsistencyNextRun>();

    /// <summary>How the runs were chosen, and the usable runs of the subject in the periods that were not analyzed.</summary>
    public ChatConsistencyRunSelectionView RunSelection { get; init; } = new();

    /// <summary>The battery or suite compared within; null for a run-by-run analysis and for analysis code version 3 or earlier.</summary>
    public ChatConsistencyComparedSet? ComparisonSet { get; init; }

    /// <summary><c>run</c> or <c>batteryRun</c>: what the minimum sample, the bootstrap and leave-one-out count.</summary>
    public string UnitKind { get; init; } = ChatConsistencyComparisonSetKinds.RunUnit;

    /// <summary>The analyzed units of both periods, ordered by period, start, then id.</summary>
    public IReadOnlyList<ChatConsistencyUnitView> Units { get; init; } = Array.Empty<ChatConsistencyUnitView>();

    /// <summary>The hours each period's answers started in, baseline first; null before analysis code version 6.</summary>
    public IReadOnlyList<ChatConsistencyPeriodHours>? PeriodHours { get; init; }

    /// <summary>Each period's descriptive levels, baseline first; null before analysis code version 6.</summary>
    public IReadOnlyList<ChatConsistencyPeriodLevels>? PeriodLevels { get; init; }

    /// <summary>The request as analyzed, which the freshness check repeats; null before analysis code version 6.</summary>
    public ChatConsistencyAnalysisRequest? Request { get; init; }

    public string InputSha256 { get; init; } = string.Empty;
    public int AnalysisCodeVersion { get; init; }
}

/// <summary>A saved analysis in a list.</summary>
public sealed record ChatConsistencyAnalysisSummary
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string SubjectModelKey { get; init; } = string.Empty;
    public DateTime BaselineStartUtc { get; init; }
    public DateTime BaselineEndUtc { get; init; }
    public DateTime ComparisonStartUtc { get; init; }
    public DateTime ComparisonEndUtc { get; init; }
    public string ProtocolVersion { get; init; } = string.Empty;
    public bool RelaxedPooling { get; init; }
    public long? CommonGraderSnapshotId { get; init; }
    public string? Headline { get; init; }
    public string InputSha256 { get; init; } = string.Empty;
    public int AnalysisCodeVersion { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public int ReportDocumentCount { get; init; }

    /// <summary>From the stored result; null for a run-by-run analysis and for analysis code version 3 or earlier.</summary>
    public string? ComparisonSetKey { get; init; }
    public string? ComparisonSetLabel { get; init; }

    /// <summary>The analyzed model, from the stored result; null when the result does not record it.</summary>
    public ChatConsistencySubjectBrief? Subject { get; init; }

    /// <summary>The endpoints' verdicts in result order, from the stored result; empty when it records none.</summary>
    public IReadOnlyList<ChatConsistencyEndpointBrief> Endpoints { get; init; } = Array.Empty<ChatConsistencyEndpointBrief>();
}

/// <summary>The model of a saved analysis, as its list entry shows it.</summary>
public sealed record ChatConsistencySubjectBrief
{
    public string DisplayName { get; init; } = string.Empty;
    public string Provider { get; init; } = string.Empty;
    public string ModelId { get; init; } = string.Empty;
    public string? ThinkingLevel { get; init; }
    public string? ServiceTier { get; init; }
}

/// <summary>One endpoint's verdict of a saved analysis, as its list entry shows it.</summary>
public sealed record ChatConsistencyEndpointBrief
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public bool Computed { get; init; }
    public string VerdictLabel { get; init; } = string.Empty;
    public ChatConsistencyEvidenceGrade Grade { get; init; } = ChatConsistencyEvidenceGrade.NotEstablished;
}

/// <summary>The outcome of a delete request.</summary>
public sealed record ChatConsistencyDeleteResult(bool Found, bool Deleted, string? Refusal);

// --- Timeline and run table ----------------------------------------------------------------------

/// <summary>A model axis that has runs.</summary>
public sealed record ChatConsistencyModelAxis
{
    public string Key { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Provider { get; init; } = string.Empty;
    public string ModelId { get; init; } = string.Empty;
    public string? ThinkingLevel { get; init; }
    public string? ServiceTier { get; init; }
    public int RunCount { get; init; }
    public int TelemetryRunCount { get; init; }
    public DateTime FirstRunAtUtc { get; init; }
    public DateTime LastRunAtUtc { get; init; }
    public long LatestRunId { get; init; }
    public IReadOnlyList<string> SuiteNames { get; init; } = Array.Empty<string>();

    /// <summary>Distinct battery runs with a non-superseded member on this axis.</summary>
    public int BatteryRunCount { get; init; }
}

/// <summary>A common-grader quality figure of one run.</summary>
public sealed record ChatConsistencyCommonGraderPoint
{
    public long SnapshotId { get; init; }
    public string Display { get; init; } = string.Empty;
    public long CalibrationId { get; init; }
    public DateTime CalibratedAtUtc { get; init; }

    /// <summary>Mean calibration quality over the answers it graded.</summary>
    public double MeanQuality { get; init; }
    public int ItemCount { get; init; }
}

/// <summary>One run on the timeline.</summary>
public record ChatConsistencyTimelinePoint
{
    public long RunId { get; init; }
    public DateTime StartedAtUtc { get; init; }
    public string SuiteName { get; init; } = string.Empty;
    public long? SuiteId { get; init; }
    public string? HarnessVersion { get; init; }
    public BenchmarkRunStatus Status { get; init; }
    public bool IsLegacy { get; init; }
    public bool IsAnchor { get; init; }

    /// <summary>The published, difficulty-weighted index; null on a battery point.</summary>
    public int? QualityIndex { get; init; }

    /// <summary>The equal-weight mean of the native per-answer scores.</summary>
    public double? NativeMeanQuality { get; init; }
    public IReadOnlyList<ChatConsistencyCommonGraderPoint> CommonGraderQuality { get; init; } = Array.Empty<ChatConsistencyCommonGraderPoint>();

    /// <summary>Median telemetry time to first answer text, net of own waits; null on a legacy run.</summary>
    public double? MedianTimeToFirstAnswerTextMs { get; init; }

    /// <summary>Median final-call streaming rate in tokens per second; null on a legacy run.</summary>
    public double? MedianStreamingRate { get; init; }
    public bool StreamingRateEstimated { get; init; }

    /// <summary>Median model time per answer; the legacy latency proxy.</summary>
    public double? MedianModelTimeMs { get; init; }

    /// <summary>"telemetry" or "legacy proxy"; on a battery point whose members differ, "mixed".</summary>
    public string LatencyLabel { get; init; } = string.Empty;
    public double? OutputTokensPerAnswer { get; init; }
    public double? ToolCallsPerAnswer { get; init; }
    public double? CostPerQuestionUsd { get; init; }
    public double? TerminalFailureRate { get; init; }
    public double? TimeoutRate { get; init; }
    public double? EmptyAnswerRate { get; init; }
    public double? RefusalRate { get; init; }
    public double? ToolBudgetExhaustedRate { get; init; }
    public IReadOnlyList<ChatConsistencyServedModelCount> ServedModelIds { get; init; } = Array.Empty<ChatConsistencyServedModelCount>();
    public IReadOnlyList<string> Strata { get; init; } = Array.Empty<string>();
    public bool StrataEstimated { get; init; }
    public int AnswerCount { get; init; }
    public int MaxParallelQuestions { get; init; }
}

/// <summary>
/// One battery run on the timeline. <see cref="ChatConsistencyTimelinePoint.RunId"/> holds the battery
/// run id, the unit id of a battery set. The measures are pooled over the answers of the usable members on
/// the subject's axis; <see cref="ChatConsistencyTimelinePoint.SuiteName"/> is the battery label,
/// <see cref="ChatConsistencyTimelinePoint.SuiteId"/> and <see cref="ChatConsistencyTimelinePoint.QualityIndex"/>
/// are null, and the battery's quality is <see cref="OverallIndex"/>.
/// </summary>
public sealed record ChatConsistencyBatteryTimelinePoint : ChatConsistencyTimelinePoint
{
    public ChatConsistencyBatteryTimelinePoint()
    {
    }

    /// <summary>A battery point carrying every field of <paramref name="measures"/>.</summary>
    public ChatConsistencyBatteryTimelinePoint(ChatConsistencyTimelinePoint measures)
        : base(measures)
    {
    }

    /// <summary><c>battery:</c> plus the battery run's definition hash.</summary>
    public string SetKey { get; init; } = string.Empty;
    public string BatteryName { get; init; } = string.Empty;

    /// <summary>The definition snapshot's revision; null when it cannot be read.</summary>
    public int? DefinitionRevision { get; init; }
    public DateTime? CompletedAtUtc { get; init; }
    public BenchmarkRunSeriesStatus BatteryStatus { get; init; }
    public int SuiteCount { get; init; }

    /// <summary>Every suite slot holds a usable member on the subject's axis.</summary>
    public bool Complete { get; init; }

    /// <summary>For example "1 of 2 suites usable"; null when complete.</summary>
    public string? IncompleteReason { get; init; }

    /// <summary>The usable members' run ids on the subject's axis, distinct, in suite order.</summary>
    public List<long> MemberRunIds { get; init; } = new();

    /// <summary>
    /// The Overall Index of the battery run's latest stored battery analysis, when the battery run is
    /// complete and the analysis is current over <see cref="MemberRunIds"/>; null otherwise.
    /// </summary>
    public double? OverallIndex { get; init; }

    /// <summary>Why <see cref="OverallIndex"/> is null; null when it is set.</summary>
    public string? OverallIndexNote { get; init; }

    /// <summary>The 95 % half-width of <see cref="OverallIndex"/> (item sampling and reproducibility combined); null with no index.</summary>
    public double? OverallIndexHalfWidth { get; init; }

    /// <summary>"question sampling only" when the half-width has no reproducibility part (one round); else null.</summary>
    public string? OverallIndexIntervalNote { get; init; }
}

/// <summary>The subject's timeline over a range.</summary>
public sealed record ChatConsistencyTimeline
{
    public ChatConsistencySubject Subject { get; init; } = new();
    public DateTime? FromUtc { get; init; }
    public DateTime? ToUtc { get; init; }
    public IReadOnlyList<ChatConsistencyTimelinePoint> Points { get; init; } = Array.Empty<ChatConsistencyTimelinePoint>();

    /// <summary>
    /// One point per battery run started between the bounds with a non-superseded member on the subject's
    /// axis, of every battery definition, ordered by start, then id.
    /// </summary>
    public List<ChatConsistencyBatteryTimelinePoint> BatteryPoints { get; init; } = new();
    public IReadOnlyList<ChatConsistencyEventView> Events { get; init; } = Array.Empty<ChatConsistencyEventView>();
    public IReadOnlyList<ChatConsistencyAnnotationView> Annotations { get; init; } = Array.Empty<ChatConsistencyAnnotationView>();
    public ChatConsistencyPriceCard PriceCard { get; init; } = new();
}

/// <summary>Whether a run is usable on one axis, and why not.</summary>
public sealed record ChatConsistencyAxisEligibility
{
    public ChatConsistencyAxis Axis { get; init; }
    public bool Eligible { get; init; }
    public int? Segment { get; init; }
    public string? Reason { get; init; }
}

/// <summary>Which re-grades by one assessor snapshot cover a run.</summary>
public sealed record ChatConsistencyRegradeCoverage
{
    public long SnapshotId { get; init; }
    public string Display { get; init; } = string.Empty;
    public IReadOnlyList<long> CalibrationIds { get; init; } = Array.Empty<long>();
    public DateTime LatestAtUtc { get; init; }
}

/// <summary>One run of the subject in the run table.</summary>
public sealed record ChatConsistencyRunRow
{
    public long RunId { get; init; }
    public DateTime StartedAtUtc { get; init; }
    public string SuiteName { get; init; } = string.Empty;
    public string? HarnessVersion { get; init; }
    public int ScoringMethodVersion { get; init; }
    public BenchmarkRunStatus Status { get; init; }
    public bool IsLegacy { get; init; }
    public bool IsAnchor { get; init; }
    public IReadOnlyList<ChatConsistencyAxisEligibility> Eligibility { get; init; } = Array.Empty<ChatConsistencyAxisEligibility>();
    public IReadOnlyList<ChatConsistencyRegradeCoverage> RegradeCoverage { get; init; } = Array.Empty<ChatConsistencyRegradeCoverage>();
    public IReadOnlyList<long> MatchedControlRunIds { get; init; } = Array.Empty<long>();
    public IReadOnlyList<ChatConsistencyServedModelCount> ServedModelIds { get; init; } = Array.Empty<ChatConsistencyServedModelCount>();

    public long? SuiteId { get; init; }

    /// <summary>The run's suite as <see cref="ChatConsistencyMeasures.SuiteIdentity"/> renders it; the suite set key is <c>suite:</c> plus this.</summary>
    public string SuiteKey { get; init; } = string.Empty;

    /// <summary>The newest battery run holding this run as a non-superseded member; null when none does.</summary>
    public long? BatteryRunId { get; init; }
    public string? BatteryName { get; init; }

    /// <summary>1-based position of the run's suite in that battery run's definition.</summary>
    public int? BatterySuitePosition { get; init; }
    public int? BatterySuiteCount { get; init; }
}

/// <summary>One battery run of the subject in the step-1 battery-run table.</summary>
public sealed record ChatConsistencyBatteryRunRow
{
    public long BatteryRunId { get; init; }
    public long? BatteryId { get; init; }
    public string BatteryName { get; init; } = string.Empty;
    public string DefinitionSha256 { get; init; } = string.Empty;
    public int DefinitionRevision { get; init; }

    /// <summary><c>battery:</c> plus <see cref="DefinitionSha256"/>.</summary>
    public string SetKey { get; init; } = string.Empty;
    public DateTime StartedAtUtc { get; init; }
    public DateTime? CompletedAtUtc { get; init; }
    public BenchmarkRunSeriesStatus Status { get; init; }
    public int SuiteCount { get; init; }

    /// <summary>Every suite slot holds a usable member on the subject's axis; only a complete battery run is analyzed.</summary>
    public bool Complete { get; init; }

    /// <summary>For example "1 of 2 suites usable"; null when complete.</summary>
    public string? IncompleteReason { get; init; }

    /// <summary>The members' harness versions, distinct, ascending.</summary>
    public IReadOnlyList<string> HarnessVersions { get; init; } = Array.Empty<string>();

    /// <summary>The usable members on the subject's axis, in suite order, then round.</summary>
    public IReadOnlyList<ChatConsistencyRunRow> Members { get; init; } = Array.Empty<ChatConsistencyRunRow>();

    /// <summary>Per axis: eligible when every member is; otherwise the members' reasons, each prefixed <c>#&lt;run id&gt;: </c>.</summary>
    public IReadOnlyList<ChatConsistencyAxisEligibility> Eligibility { get; init; } = Array.Empty<ChatConsistencyAxisEligibility>();
}

// --- Re-grade ------------------------------------------------------------------------------------

/// <summary>A request to re-grade runs with one assessor configuration.</summary>
public sealed record ChatConsistencyRegradeRequest
{
    public IReadOnlyList<long> RunIds { get; init; } = Array.Empty<long>();
    public long AssessorConfigId { get; init; }

    /// <summary>The operator has seen <see cref="ChatConsistencyRegradeEstimate"/> and confirmed the spend. Refused without it.</summary>
    public bool Confirmed { get; init; }
}

/// <summary>One run's part of a re-grade estimate.</summary>
public sealed record ChatConsistencyRegradeRunEstimate
{
    public long RunId { get; init; }
    public bool Eligible { get; init; }
    public string? Refusal { get; init; }
    public int GradableAnswerCount { get; init; }
    public long RecordedAssessorInputTokens { get; init; }
    public long RecordedAssessorOutputTokens { get; init; }
    public long RecordedAssessorCacheReadTokens { get; init; }
    public long RecordedAssessorCacheCreationTokens { get; init; }
    public decimal? EstimatedCostUsd { get; init; }
}

/// <summary>What a re-grade is expected to cost. Always an estimate.</summary>
public sealed record ChatConsistencyRegradeEstimate
{
    public long AssessorConfigId { get; init; }
    public string AssessorDisplay { get; init; } = string.Empty;
    public string? AssessorRefusal { get; init; }
    public IReadOnlyList<ChatConsistencyRegradeRunEstimate> Runs { get; init; } = Array.Empty<ChatConsistencyRegradeRunEstimate>();
    public int EligibleRunCount { get; init; }

    /// <summary>Null when the assessor has no published price.</summary>
    public decimal? EstimatedTotalCostUsd { get; init; }
    public bool PricingAvailable { get; init; }
    public string Note { get; init; } = string.Empty;
}

/// <summary>One run's error in a re-grade job.</summary>
public sealed record ChatConsistencyRegradeRunError(long RunId, string Message);

/// <summary>A re-grade job's progress.</summary>
public sealed record ChatConsistencyRegradeJobView
{
    public string Id { get; init; } = string.Empty;

    /// <summary><c>running</c>, <c>completed</c>, <c>completedWithErrors</c>, <c>canceled</c> or <c>failed</c>.</summary>
    public string Status { get; init; } = string.Empty;
    public long AssessorConfigId { get; init; }
    public string AssessorDisplay { get; init; } = string.Empty;
    public IReadOnlyList<long> RunIds { get; init; } = Array.Empty<long>();
    public int Total { get; init; }
    public int Done { get; init; }
    public long? CurrentRunId { get; init; }
    public IReadOnlyList<ChatConsistencyRegradeRunError> Errors { get; init; } = Array.Empty<ChatConsistencyRegradeRunError>();
    public DateTime StartedAtUtc { get; init; }
    public DateTime? CompletedAtUtc { get; init; }
    public string? StartedByUserName { get; init; }
}

/// <summary>The outcome of a start request: the job, or the reason it was refused.</summary>
public sealed record ChatConsistencyRegradeStartResult(bool Started, string? Refusal, ChatConsistencyRegradeJobView? Job);
