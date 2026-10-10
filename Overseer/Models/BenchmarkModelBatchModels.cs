namespace Overseer.Models;

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using MobileGnollHackLogger.Data;

// Requests and DTOs for GnollBench model batches: several models under test, run one after another
// under one start request, with the guardrail findings and the projection the launcher shows before
// Start. Enum values travel as strings; the batch and member statuses are the server enums' names.

/// <summary>Starts a model batch, or asks for its preflight: the same body for both.</summary>
public class StartBenchmarkModelBatchRequest
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public BenchmarkModelBatchTargetKind TargetKind { get; set; } = BenchmarkModelBatchTargetKind.Suite;

    /// <summary>The suite of a suite target.</summary>
    public long? SuiteId { get; set; }

    /// <summary>The battery of a battery target.</summary>
    public long? BatteryId { get; set; }

    /// <summary>The models under test, in the order the operator listed them.</summary>
    public List<long> TestedModelConfigurationIds { get; set; } = new();

    /// <summary>R: runs per model on a suite (2 or more makes a replicate series), or runs per suite on a battery.</summary>
    public int RunsPerModel { get; set; } = 1;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public BenchmarkModelBatchOrder Order { get; set; } = BenchmarkModelBatchOrder.Randomized;

    /// <summary>When the run cap blocks the next launch: true waits for headroom, false stops the batch.</summary>
    public bool AllowCapWait { get; set; }

    /// <summary>
    /// The run settings every member is launched from. Its suite id, tested configuration id, run
    /// count and acknowledgment flags are ignored; the batch sets them per member.
    /// </summary>
    public StartBenchmarkRunRequest Run { get; set; } = new();

    /// <summary>The <see cref="BenchmarkModelBatchFindingDto.AcknowledgmentKey"/> of every warning the operator acknowledged.</summary>
    public List<string> AcknowledgedFindingKeys { get; set; } = new();
}

/// <summary>The severities of a guardrail finding, as wire values.</summary>
public static class BenchmarkModelBatchSeverity
{
    public const string Blocker = "Blocker";
    public const string Warning = "Warning";
    public const string Advice = "Advice";
}

/// <summary>One guardrail finding: a blocker, a warning to acknowledge, or advice.</summary>
public class BenchmarkModelBatchFindingDto
{
    /// <summary>The stable code, such as <c>MB-W01</c>.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>The code's name, such as <c>MixedFamiliesSingleAssessor</c>.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary><c>Blocker</c>, <c>Warning</c> or <c>Advice</c>.</summary>
    public string Severity { get; set; } = BenchmarkModelBatchSeverity.Advice;

    /// <summary>
    /// The launcher field the finding is about: <c>models</c>, <c>assessor</c>, <c>coAssessor</c>,
    /// <c>reader</c>, <c>verifier</c>, <c>reportWriter</c>, <c>profile</c>, <c>responseStyle</c>,
    /// <c>sourceReferences</c>, <c>runsPerModel</c>, <c>order</c>, <c>capWait</c> or <c>target</c>; null for none.
    /// </summary>
    public string? Field { get; set; }

    /// <summary>At most 60 characters.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>One sentence, at most 140 characters, except where a launcher refusal is quoted.</summary>
    public string Detail { get; set; } = string.Empty;

    /// <summary>The model configurations the finding is about, ascending.</summary>
    public List<long> ModelConfigurationIds { get; set; } = new();

    /// <summary>
    /// What a warning's acknowledgment is sent as: the code, an optional discriminator, and the sorted
    /// affected configuration ids. Null for a blocker and for advice.
    /// </summary>
    public string? AcknowledgmentKey { get; set; }
}

/// <summary>The run caps and the whole batch measured against them (guardrails § 3.5).</summary>
public class BenchmarkModelBatchLimitsDto
{
    public int MaxRunsPerDay { get; set; }
    public int MaxRunsPerHour { get; set; }
    public int RunsInLast24Hours { get; set; }
    public int RunsInLastHour { get; set; }
    public int RemainingDailyHeadroom { get; set; }

    /// <summary>⌈planned launches ÷ <see cref="MaxRunsPerDay"/>⌉: the rolling 24-hour windows the batch needs.</summary>
    public int? DaySpan { get; set; }

    /// <summary>(<see cref="DaySpan"/> − 1) × 24 h, when the batch needs more than one window.</summary>
    public long? MinimumWallMs { get; set; }

    /// <summary>60 minutes ÷ the shortest recent mean run duration among the target's suites.</summary>
    public double? ProjectedRunsPerHour { get; set; }

    /// <summary>The launches one member plans: K × R on a battery, R on a suite.</summary>
    public int? MemberPlanRuns { get; set; }

    public int MaxBatteryMembers { get; set; }

    /// <summary>The spend guard would allow a launch now.</summary>
    public bool SpendAllowedNow { get; set; }

    /// <summary>The spend guard's refusal now; null when it allows a launch.</summary>
    public string? SpendDenialReason { get; set; }

    /// <summary>The refusal is the hourly or daily run cap, which a cap wait outlasts.</summary>
    public bool SpendDenialIsCap { get; set; }
}

/// <summary>The values of <see cref="BenchmarkModelBatchMemberProjectionDto.Basis"/>.</summary>
public static class BenchmarkModelBatchProjectionBasis
{
    /// <summary>Every suite projected from this model's own recent runs on it.</summary>
    public const string OwnRuns = "OwnRuns";

    /// <summary>Some suites from this model's own runs, the rest from the target's recent runs.</summary>
    public const string Mixed = "Mixed";

    /// <summary>Every suite projected from the target's recent runs, whichever models made them.</summary>
    public const string TargetMean = "TargetMean";

    /// <summary>A suite has no completed run to project from.</summary>
    public const string None = "None";
}

/// <summary>One member's share of the projection.</summary>
public class BenchmarkModelBatchMemberProjectionDto
{
    public long ModelConfigurationId { get; set; }
    public int PlannedRunCount { get; set; }
    public decimal? ProjectedCostUsd { get; set; }
    public long? ProjectedWallMs { get; set; }

    /// <summary>One of <see cref="BenchmarkModelBatchProjectionBasis"/>.</summary>
    public string Basis { get; set; } = BenchmarkModelBatchProjectionBasis.None;
}

/// <summary>Planned runs, projected cost and wall time per member and in total, and the run limits.</summary>
public class BenchmarkModelBatchProjectionDto
{
    public int PlannedRunCount { get; set; }

    /// <summary>Null when any member has no basis.</summary>
    public decimal? ProjectedCostUsd { get; set; }

    /// <summary>Null when any member has no basis.</summary>
    public long? ProjectedWallMs { get; set; }

    public BenchmarkModelBatchLimitsDto Limits { get; set; } = new();

    public List<BenchmarkModelBatchMemberProjectionDto> Members { get; set; } = new();
}

/// <summary>What <c>POST preflight</c> returns: every finding and the projection.</summary>
public class BenchmarkModelBatchPreflightResponse
{
    public List<BenchmarkModelBatchFindingDto> Findings { get; set; } = new();
    public BenchmarkModelBatchProjectionDto Projection { get; set; } = new();

    /// <summary><c>Benchmark:ModelBatch:MaxModels</c>.</summary>
    public int MaxModels { get; set; }
}

/// <summary>The body of a 400 or 409 refusal of a start: the findings that refused it.</summary>
public class BenchmarkModelBatchRefusalDto
{
    public string Message { get; set; } = string.Empty;
    public List<BenchmarkModelBatchFindingDto> Findings { get; set; } = new();
}

/// <summary>How a stopped model batch is resumed.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BenchmarkModelBatchResumeMode
{
    /// <summary>Resumes the stopped member's run, series or battery run, or launches the next member.</summary>
    Continue = 0,

    /// <summary>Marks the stopped member Skipped and goes on with the next one.</summary>
    SkipCurrent = 1,

    /// <summary>Continues over a moved instrument or grader configuration; the batch is then not comparable across the change.</summary>
    AcceptInstrumentChange = 2,

    /// <summary>Re-runs every member under the current instrument at full cost; earlier links are kept as superseded.</summary>
    RerunUnderCurrentInstrument = 3
}

public class ResumeBenchmarkModelBatchRequest
{
    public BenchmarkModelBatchResumeMode Mode { get; set; } = BenchmarkModelBatchResumeMode.Continue;
}

/// <summary>A resume mode valid now, with the button label and why it applies.</summary>
public class BenchmarkModelBatchResumeOptionDto
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public BenchmarkModelBatchResumeMode Mode { get; set; }

    public string Label { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

/// <summary>A tested configuration as it stood when the batch started; stored as the member's snapshot JSON.</summary>
public class BenchmarkModelBatchModelDto
{
    public long ConfigurationId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public string? ThinkingLevel { get; set; }
    public string? ReasoningMode { get; set; }
    public string? ServiceTier { get; set; }

    /// <summary>The parallel execution mode's name.</summary>
    public string? ParallelExecutionMode { get; set; }

    /// <summary><c>official</c>, or a fingerprinted description of the custom endpoint.</summary>
    public string Endpoint { get; set; } = "official";

    public int? MaxOutputTokens { get; set; }
}

/// <summary>The instrument one member's first run recorded, or the batch's first member's.</summary>
public class BenchmarkModelBatchInstrumentDto
{
    public string? CandidateSystemPromptSha256 { get; set; }
    public string? ToolGuidesSha256 { get; set; }
    public string? KnowledgeBaseHeadSha { get; set; }
    public string? WikiHeadSha { get; set; }
    public string? SourceCodeHeadSha { get; set; }
    public string? HarnessVersion { get; set; }
    public int? ScoringMethodVersion { get; set; }

    /// <summary>The corpus index fingerprints the run recorded, as canonical JSON; null on the batch's first-member record.</summary>
    public string? CorpusIndexFingerprintsJson { get; set; }
}

/// <summary>A member's result, read from its run, its series' group analysis or its battery run's analysis.</summary>
public class BenchmarkModelBatchMemberResultDto
{
    /// <summary>The run's index, or the series' pooled index; null on a battery member.</summary>
    public double? IntelligenceIndex { get; set; }

    /// <summary>The battery run's Overall Index; null on a suite member.</summary>
    public double? OverallIndex { get; set; }

    /// <summary>The index's 95 % half-width; null when not computed.</summary>
    public double? IndexHalfWidth { get; set; }

    /// <summary>Where the index comes from: <c>run</c>, <c>groupAnalysis</c>, <c>runMean</c> or <c>batteryAnalysis</c>.</summary>
    public string? IndexSource { get; set; }

    /// <summary>Median model time (turn duration less tool time) over the Ok answers of the member's runs.</summary>
    public double? MedianModelTimeMs { get; set; }

    /// <summary>Median time to first answer text over the Ok answers that carry call telemetry.</summary>
    public double? TtftP50Ms { get; set; }

    /// <summary>The candidate's cost divided by the answered questions.</summary>
    public decimal? CandidateCostPerQuestionUsd { get; set; }

    public decimal? CandidateCostUsd { get; set; }
    public decimal? TotalCostUsd { get; set; }

    public int RefutedClaims { get; set; }
    public int ConfirmedCriticalErrors { get; set; }

    /// <summary>Own permit and retry-backoff waits ÷ model time, over the answers with call telemetry.</summary>
    public double? OwnWaitShare { get; set; }

    /// <summary>Answers that did not end Ok.</summary>
    public int FailedAnswers { get; set; }

    /// <summary>Answers that ended with a provider error.</summary>
    public int ProviderErrors { get; set; }

    /// <summary>Candidate call attempts beyond the first, summed.</summary>
    public int Retries { get; set; }
}

public class BenchmarkModelBatchMemberDto
{
    public long Id { get; set; }

    /// <summary>0-based position in run order.</summary>
    public int OrderIndex { get; set; }

    public BenchmarkModelBatchModelDto Model { get; set; } = new();

    /// <summary>The <c>BenchmarkModelBatchMemberStatus</c> name.</summary>
    public string Status { get; set; } = string.Empty;

    public long? RunId { get; set; }
    public long? SeriesId { get; set; }
    public long? BatteryRunId { get; set; }

    /// <summary>Every run the member produced, ascending.</summary>
    public List<long> RunIds { get; set; } = new();

    /// <summary>The member's run in flight, if any.</summary>
    public long? CurrentRunId { get; set; }

    /// <summary>The run-level stage of <see cref="CurrentRunId"/> as this process drives it; null otherwise.</summary>
    public string? CurrentStage { get; set; }

    /// <summary>1-based position of the current run within the member: series member k of R, battery slot s of K × R.</summary>
    public int? CurrentStepIndex { get; set; }

    /// <summary>The runs the member plans: 1, R, or K × R.</summary>
    public int StepCount { get; set; }

    public int AnsweredQuestionCount { get; set; }
    public int TotalQuestionCount { get; set; }

    /// <summary>Null until the member has a finished run.</summary>
    public BenchmarkModelBatchMemberResultDto? Result { get; set; }

    public BenchmarkModelBatchInstrumentDto? Instrument { get; set; }

    /// <summary>The instrument keys on which this member's first run differs from the batch's first member.</summary>
    public List<string> InstrumentDriftKeys { get; set; } = new();

    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? ErrorMessage { get; set; }
}

public class BenchmarkModelBatchRunDto
{
    public long Id { get; set; }

    /// <summary>The <c>BenchmarkRunSeriesStatus</c> name: <c>Pending</c>, <c>Running</c>, <c>WaitingForCap</c>, <c>Stopped</c>, <c>Completed</c>, <c>CompletedWithErrors</c>, <c>Cancelled</c> or <c>Failed</c>.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>The <c>BenchmarkModelBatchStopReason</c> name; null unless stopped.</summary>
    public string? StopReason { get; set; }

    /// <summary>The stop reason in words.</summary>
    public string? StopReasonText { get; set; }

    public string? StopDetail { get; set; }

    /// <summary><c>Suite</c> or <c>Battery</c>.</summary>
    public string TargetKind { get; set; } = string.Empty;

    public long? SuiteId { get; set; }
    public long? BatteryId { get; set; }
    public string? TargetName { get; set; }
    public int? BatteryRevision { get; set; }
    public string? BatteryDefinitionSha256 { get; set; }

    /// <summary>The suites every member runs, in run order.</summary>
    public List<string> SuiteNames { get; set; } = new();

    public int RunsPerModel { get; set; }

    /// <summary><c>AsListed</c> or <c>Randomized</c>.</summary>
    public string Order { get; set; } = string.Empty;

    public int? OrderSeed { get; set; }
    public bool AllowCapWait { get; set; }

    /// <summary>The run settings template every member is launched from.</summary>
    public StartBenchmarkRunRequest? Run { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? CreatedByUserName { get; set; }

    public List<BenchmarkModelBatchMemberDto> Members { get; set; } = new();

    public int? CurrentMemberIndex { get; set; }
    public int RequestedMemberCount { get; set; }
    public int CompletedMemberCount { get; set; }
    public int FailedMemberCount { get; set; }
    public int SkippedMemberCount { get; set; }

    /// <summary>The candidate cost of every member run so far; null when any is unknown.</summary>
    public decimal? LiveCandidateCostUsd { get; set; }

    /// <summary>The total cost of every member run so far; null when any is unknown.</summary>
    public decimal? LiveTotalCostUsd { get; set; }

    public List<BenchmarkModelBatchFindingDto> AcknowledgedFindings { get; set; } = new();
    public List<BenchmarkModelBatchFindingDto> AdviceAtStart { get; set; } = new();

    /// <summary>The first member's instrument; null until its first run is stamped.</summary>
    public BenchmarkModelBatchInstrumentDto? FirstMemberInstrument { get; set; }

    public bool InstrumentChangeAcknowledged { get; set; }

    /// <summary>This process is driving the batch now.</summary>
    public bool IsDriving { get; set; }

    /// <summary>Stopped and not driven: <see cref="ResumeOptions"/> holds at least one mode.</summary>
    public bool Resumable { get; set; }

    /// <summary>The resume modes valid now, in the order the dialog shows them.</summary>
    public List<BenchmarkModelBatchResumeOptionDto> ResumeOptions { get; set; } = new();

    public DateTime? LastProgressAtUtc { get; set; }

    /// <summary>Live, and <see cref="LastProgressAtUtc"/> older than <see cref="StallMinutes"/> (MB-R5).</summary>
    public bool Stalled { get; set; }

    /// <summary><c>Benchmark:ModelBatch:StallMinutes</c>.</summary>
    public int StallMinutes { get; set; }

    /// <summary>The member links a Re-run under current instrument replaced, as stored JSON; null when none.</summary>
    public string? SupersededMembersJson { get; set; }
}
