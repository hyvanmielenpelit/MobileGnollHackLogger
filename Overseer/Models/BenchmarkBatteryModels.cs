namespace Overseer.Models;

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using MobileGnollHackLogger.Data;
using Overseer.Services.Benchmarking;

// Requests and DTOs for multi-suite benchmark batteries: defining a battery, starting a battery run
// over every suite of it for one model configuration, resuming a stopped one, and reading its
// analyses and leaderboard. The statistics records are passed through as the server defines them
// (`BenchmarkBatteryStatisticsResult`, `BenchmarkBatteryComparison`), not mirrored.

/// <summary>Starts a battery run: every suite of the battery, one after another, for one model configuration.</summary>
public class StartBenchmarkBatteryRunRequest
{
    public long BatteryId { get; set; }

    /// <summary>Replicate rounds; every suite runs once per round, in round-robin order.</summary>
    public int RunsPerSuite { get; set; } = 1;

    /// <summary>
    /// When a member is refused by the rolling run cap: true pauses the battery run in
    /// <c>WaitingForCap</c> and retries; false stops it with <c>RunCapReached</c>. Also admits a
    /// battery run that plans more launches than the daily cap.
    /// </summary>
    public bool AllowCapWait { get; set; }

    /// <summary>
    /// The run request every member is launched from. Its suite id is ignored and set per suite;
    /// its run count is ignored.
    /// </summary>
    public StartBenchmarkRunRequest Run { get; set; } = new();

    /// <summary>
    /// Existing runs to place in slots at start. Each is validated against the fingerprints the start
    /// records; one that does not qualify refuses the whole start.
    /// </summary>
    public List<BenchmarkBatteryAttachDto>? Attach { get; set; }
}

/// <summary>
/// An existing run placed in one (suite, round) slot of a battery run: an entry of
/// <see cref="StartBenchmarkBatteryRunRequest.Attach"/>, and the body of <c>POST runs/{id}/members</c>.
/// </summary>
public class BenchmarkBatteryAttachDto
{
    /// <summary>0-based position of the suite in the battery definition.</summary>
    public int SuiteIndex { get; set; }

    /// <summary>1-based replicate round.</summary>
    public int Round { get; set; }

    public long RunId { get; set; }
}

/// <summary>One slot of the reuse preview: the earlier run a start would attach, or why none qualifies.</summary>
public class BenchmarkBatteryReusePreviewSlotDto
{
    /// <summary>0-based position of the suite in the battery definition.</summary>
    public int SuiteIndex { get; set; }

    public long SuiteId { get; set; }
    public string SuiteName { get; set; } = string.Empty;

    /// <summary>1-based replicate round.</summary>
    public int Round { get; set; }

    /// <summary>The newest eligible run; null when none qualifies and the slot would be launched.</summary>
    public long? RunId { get; set; }

    public DateTime? RunStartedAtUtc { get; set; }
    public int? QualityIndex { get; set; }

    /// <summary>When no run qualifies: why the newest candidate was refused, or that there is none.</summary>
    public string? Reason { get; set; }
}

/// <summary>
/// Which slots of a battery run not yet started existing runs would fill, judged against the
/// fingerprints a start would record now. Creates and spends nothing.
/// </summary>
public class BenchmarkBatteryReusePreviewDto
{
    public long BatteryId { get; set; }
    public int SuiteCount { get; set; }
    public int RunsPerSuite { get; set; }

    /// <summary>Slots an earlier run would fill.</summary>
    public int ReusedCount { get; set; }

    /// <summary>Slots the battery run would launch.</summary>
    public int LaunchCount { get; set; }

    /// <summary>The runs to send as <see cref="StartBenchmarkBatteryRunRequest.Attach"/>, in planner order.</summary>
    public List<BenchmarkBatteryAttachDto> Attach { get; set; } = new();

    /// <summary>Every slot in planner order: round 1 for every suite, then round 2.</summary>
    public List<BenchmarkBatteryReusePreviewSlotDto> Slots { get; set; } = new();
}

/// <summary>A run that may, or may not, be attached to one slot of a battery run.</summary>
public class BenchmarkBatteryAttachCandidateDto
{
    public long RunId { get; set; }

    /// <summary>The run's <c>BenchmarkRunStatus</c> as text.</summary>
    public string RunStatus { get; set; } = string.Empty;

    public int? QualityIndex { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? TestedModelLabel { get; set; }
    public string? HarnessVersion { get; set; }
    public int ScoringMethodVersion { get; set; }

    public bool Eligible { get; set; }

    /// <summary>Why the run may not be attached; null when it may.</summary>
    public string? Reason { get; set; }
}

/// <summary>How a stopped battery run is resumed.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BenchmarkBatteryResumeMode
{
    /// <summary>Replaces the members that are not usable and launches the empty slots.</summary>
    Continue = 0,

    /// <summary>Supersedes every member and starts over under the current instrument.</summary>
    RerunUnderCurrentInstrument = 1
}

public class ResumeBenchmarkBatteryRunRequest
{
    public BenchmarkBatteryResumeMode Mode { get; set; } = BenchmarkBatteryResumeMode.Continue;
}

// --- Battery definitions ---------------------------------------------------------------------------

/// <summary>Creates a battery. The suites are listed in run order.</summary>
public class CreateBenchmarkBatteryRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public BenchmarkBatteryWeightingScheme WeightingScheme { get; set; } = BenchmarkBatteryWeightingScheme.DifficultyMass;

    /// <summary>The suite ids in run order; at least two, none twice.</summary>
    public List<long> SuiteIds { get; set; } = new();

    /// <summary>
    /// One declared weight per entry of <see cref="SuiteIds"/>, in the same order. Read only under
    /// the Custom scheme, where every weight must be a finite number above zero.
    /// </summary>
    public List<double?>? CustomWeights { get; set; }
}

/// <summary>
/// Replaces a battery's name, description, scheme, suites and weights. A change to the suites, their
/// order, the weights or the scheme creates a new revision; a name or description change does not.
/// </summary>
public class UpdateBenchmarkBatteryRequest : CreateBenchmarkBatteryRequest
{
}

/// <summary>Hides a battery from the launcher, or shows it again.</summary>
public class ArchiveBenchmarkBatteryRequest
{
    public bool Archived { get; set; } = true;
}

/// <summary>One suite of a battery, with its current exam size and difficulty readiness.</summary>
public class BenchmarkBatterySuiteDto
{
    /// <summary>0-based position in run order.</summary>
    public int Index { get; set; }

    /// <summary>Null when the suite has been deleted.</summary>
    public long? SuiteId { get; set; }

    public string SuiteName { get; set; } = string.Empty;
    public bool Deleted { get; set; }
    public double? CustomWeight { get; set; }

    public int QuestionCount { get; set; }
    public int AssessedQuestionCount { get; set; }

    /// <summary>Every question has an assessed difficulty; the launcher refuses the suite otherwise.</summary>
    public bool DifficultyFullyAssessed { get; set; }

    /// <summary>The sum of the current questions' difficulty weights, an unassessed question weighing 50.</summary>
    public double DifficultyMass { get; set; }
}

/// <summary>The normalized suite weights one scheme would give, in suite order.</summary>
public class BenchmarkBatteryWeightPreviewDto
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public BenchmarkBatteryWeightingScheme Scheme { get; set; }

    public string SchemeLabel { get; set; } = string.Empty;

    /// <summary>The battery's own scheme; the others are sensitivity alternatives.</summary>
    public bool Declared { get; set; }

    /// <summary>One weight per suite, summing to 1; empty when the weights are undefined.</summary>
    public List<double> Weights { get; set; } = new();
}

public class BenchmarkBatteryDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public BenchmarkBatteryWeightingScheme WeightingScheme { get; set; }

    public string WeightingSchemeLabel { get; set; } = string.Empty;
    public int Revision { get; set; }
    public string DefinitionSha256 { get; set; } = string.Empty;
    public bool IsArchived { get; set; }

    /// <summary>The names of the suites that have been deleted; the battery cannot run until it is edited.</summary>
    public List<string> BrokenSuiteNames { get; set; } = new();

    /// <summary>Every problem that keeps the battery from running; empty when it can run.</summary>
    public List<string> ValidationErrors { get; set; } = new();

    public string? CreatedByUserName { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ModifiedAtUtc { get; set; }

    public int BatteryRunCount { get; set; }

    /// <summary>A battery run of this battery is Pending, Running or WaitingForCap; delete is refused.</summary>
    public bool HasActiveBatteryRun { get; set; }

    /// <summary>
    /// The rows the leaderboard of the current definition hash ranks: battery runs whose latest
    /// analysis is complete and carries a comparability class.
    /// </summary>
    public int RankedResultCount { get; set; }

    /// <summary>The newest analysis on the current definition's leaderboard; null when there is none.</summary>
    public DateTime? LatestAnalysisAtUtc { get; set; }

    public List<BenchmarkBatterySuiteDto> Suites { get; set; } = new();

    /// <summary>One preview per scheme, the declared one marked, from the suites' current questions.</summary>
    public List<BenchmarkBatteryWeightPreviewDto> WeightPreviews { get; set; } = new();
}

// --- Battery runs --------------------------------------------------------------------------------

/// <summary>One suite of a battery run's definition snapshot.</summary>
public class BenchmarkBatteryRunSuiteDto
{
    public int Index { get; set; }
    public long SuiteId { get; set; }
    public string SuiteName { get; set; } = string.Empty;
    public double? CustomWeight { get; set; }

    // The instrument hashes recorded for this suite when the battery run started or was last
    // resumed under the current instrument. Null means not recorded.
    public string? CandidateSystemPromptSha256 { get; set; }
    public string? ToolGuidesSha256 { get; set; }
    public string? KnowledgeBaseHeadSha { get; set; }
    public string? WikiHeadSha { get; set; }
    public string? SourceCodeHeadSha { get; set; }
}

/// <summary>One run in one (suite, round) slot of a battery run.</summary>
public class BenchmarkBatteryMemberDto
{
    public long MemberId { get; set; }

    /// <summary>0-based position of the suite in the definition snapshot.</summary>
    public int SuiteIndex { get; set; }

    /// <summary>1-based replicate round.</summary>
    public int Round { get; set; }

    public long RunId { get; set; }

    /// <summary>The run's <c>BenchmarkRunStatus</c> as text.</summary>
    public string RunStatus { get; set; } = string.Empty;

    public int? QualityIndex { get; set; }
    public int? SpeedIndex { get; set; }

    /// <summary><c>Launched</c> or <c>Attached</c>.</summary>
    public string Origin { get; set; } = string.Empty;

    public bool Superseded { get; set; }

    /// <summary>The member may enter a statistic (Statistical Method M4).</summary>
    public bool Usable { get; set; }

    /// <summary>Why the member is not usable, in a few words; null when it is.</summary>
    public string? UnusableReason { get; set; }

    public string? GuardFailure { get; set; }
    public DateTime AddedAtUtc { get; set; }
    public DateTime? RunStartedAtUtc { get; set; }
    public DateTime? RunCompletedAtUtc { get; set; }

    /// <summary>
    /// Answered questions / questions in the run: for a running member the answer rows written so
    /// far, for any other the run's own answered count.
    /// </summary>
    public int AnsweredQuestionCount { get; set; }
    public int TotalQuestionCount { get; set; }

    /// <summary>The <c>BenchmarkRunStage</c> of a running member as this process drives it; null otherwise.</summary>
    public string? Stage { get; set; }

    /// <summary>1.96 × the run's index standard error; null when the index or its standard error is.</summary>
    public double? QualityIndexHalfWidth { get; set; }

    /// <summary>Completed minus started; null while the run has not completed.</summary>
    public long? DurationMs { get; set; }

    public int ClaimsRefutedCount { get; set; }
    public int AdvisoryFlagAnswerCount { get; set; }

    // The member run's estimated cost in US dollars, live while it runs. Null on the battery-run list,
    // on a superseded member, and when the figure is unknown.
    public decimal? EstimatedCost { get; set; }
    public decimal? EstimatedCandidateCost { get; set; }

    /// <summary>
    /// Mean model time of the member run's Ok answers, in ms: the turn duration less tool time.
    /// Null on the battery-run list, on a superseded member, and while no answer is Ok.
    /// </summary>
    public double? MeanModelTimeMs { get; set; }
}

/// <summary>
/// A battery run's estimated cost so far, in US dollars: each role summed over the non-superseded
/// member runs that report a figure for it. A role is null when no member has one, or when any member
/// spent on it without a price card; Total is null when any member's pricing is incomplete.
/// </summary>
public class BenchmarkBatteryLiveCostDto
{
    public decimal? Total { get; set; }
    public decimal? Candidate { get; set; }
    public decimal? Assessor { get; set; }

    /// <summary>Panel member B.</summary>
    public decimal? CoAssessor { get; set; }

    /// <summary>The second or reference reader.</summary>
    public decimal? SecondOpinion { get; set; }

    public decimal? ClaimVerifier { get; set; }
    public decimal? Synthesis { get; set; }
    public decimal? CoSynthesis { get; set; }

    /// <summary>Every grading role together.</summary>
    public decimal? Grading { get; set; }

    /// <summary>A member run's pricing was incomplete.</summary>
    public bool PricingIncomplete { get; set; }

    /// <summary>The members' common pricing source; <c>mixed</c> when they differ; null when none resolved.</summary>
    public string? PricingSource { get; set; }

    /// <summary>The member runs estimated.</summary>
    public int PricedMemberCount { get; set; }

    /// <summary>
    /// The battery-completion documents' report-writer cost; null when none exists or any has no cost.
    /// Not part of <see cref="Total"/>.
    /// </summary>
    public decimal? ReportWriterCostUsd { get; set; }
}

/// <summary>One (suite, round) cell of the K × R grid, with the member occupying it, if any.</summary>
public class BenchmarkBatterySlotDto
{
    public int SuiteIndex { get; set; }
    public int Round { get; set; }

    /// <summary>The non-superseded member in this slot; null while the slot is empty.</summary>
    public BenchmarkBatteryMemberDto? Member { get; set; }
}

public class BenchmarkBatteryRunDto
{
    public long Id { get; set; }

    /// <summary>Null when the battery has been deleted; the run keeps its own snapshot.</summary>
    public long? BatteryId { get; set; }

    public string BatteryName { get; set; } = string.Empty;

    /// <summary>The battery revision the definition snapshot was taken at.</summary>
    public int DefinitionRevision { get; set; }

    public string DefinitionSha256 { get; set; } = string.Empty;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public BenchmarkBatteryWeightingScheme WeightingScheme { get; set; }

    public List<BenchmarkBatteryRunSuiteDto> Suites { get; set; } = new();

    /// <summary>K, the number of suites.</summary>
    public int SuiteCount { get; set; }

    /// <summary>R, the number of replicate rounds.</summary>
    public int RunsPerSuite { get; set; }

    /// <summary>K × R.</summary>
    public int RequestedMemberCount { get; set; }

    /// <summary>Slots holding a usable member.</summary>
    public int CompletedMemberCount { get; set; }

    public int FailedMemberCount { get; set; }

    /// <summary>Suites with at least one usable member; the Overall Index needs all K.</summary>
    public int CompletedSuiteCount { get; set; }

    /// <summary>The <c>BenchmarkRunSeriesStatus</c> as text.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary><c>MemberFailed</c>, <c>RunCapReached</c>, <c>SpendDenied</c> or <c>InstrumentChanged</c>; null unless stopped.</summary>
    public string? StopReason { get; set; }

    /// <summary>The stop reason in words, ready to render beside a Continue button.</summary>
    public string? StopReasonText { get; set; }

    public bool AllowCapWait { get; set; }

    /// <summary>Stopped, or Completed with errors while a slot holds no usable member.</summary>
    public bool Resumable { get; set; }

    /// <summary>This process is driving the battery run now.</summary>
    public bool IsDriving { get; set; }

    public DateTime StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime? LastProgressAtUtc { get; set; }
    public string? ErrorMessage { get; set; }
    public string? StartedByUserName { get; set; }

    public long? TestedModelConfigurationId { get; set; }

    /// <summary>The model under test, from a member run's snapshot, else the configuration's name.</summary>
    public string? TestedModelLabel { get; set; }

    // What the battery run was measured with: from the newest usable member run's snapshots, else
    // the newest member's, else the stored start request's configurations.
    public string? TestedProvider { get; set; }
    public string? TestedModelId { get; set; }
    public string? TestedThinkingLevel { get; set; }
    public string? TestedReasoningMode { get; set; }
    public string? TestedServiceTier { get; set; }
    public string? AssessorLabel { get; set; }
    public string? AssessorProvider { get; set; }
    public string? AssessorThinkingLevel { get; set; }
    public string? AssessorReasoningMode { get; set; }

    /// <summary>Panel member B; null on a single-assessor battery run.</summary>
    public string? CoAssessorLabel { get; set; }
    public string? CoAssessorProvider { get; set; }
    public string? CoAssessorThinkingLevel { get; set; }
    public string? CoAssessorReasoningMode { get; set; }

    public string? ScoringProfileName { get; set; }

    /// <summary>The candidate answered under the detailed response style.</summary>
    public bool VerboseMode { get; set; }

    /// <summary>The configuration that writes the battery run's AI-written documents; null when none was chosen.</summary>
    public long? ReportWriterModelConfigurationId { get; set; }

    // The report writer's settings, from that configuration. All null without a writer or when the
    // configuration has been deleted.
    public string? ReportWriterDisplayName { get; set; }
    public string? ReportWriterProvider { get; set; }
    public string? ReportWriterModelId { get; set; }
    public string? ReportWriterThinkingLevel { get; set; }
    public string? ReportWriterReasoningMode { get; set; }
    public string? ReportWriterServiceTier { get; set; }

    /// <summary>Where the battery run's two battery-completion documents stand.</summary>
    public BenchmarkRunReportDocumentsStatus ReportDocumentsStatus { get; set; }

    /// <summary>Why the documents failed or were skipped; null otherwise.</summary>
    public string? ReportDocumentsMessage { get; set; }

    /// <summary>The battery-completion documents of this battery run that exist, as the AI Reports tab lists them.</summary>
    public int ReportDocumentsWrittenCount { get; set; }

    // The banner position: the running member, else the next slot to launch while the battery run is
    // live. Null when neither applies.
    public int? CurrentSuiteIndex { get; set; }

    /// <summary>1-based suite position, for "Suite s of K".</summary>
    public int? CurrentSuitePosition { get; set; }

    public string? CurrentSuiteName { get; set; }
    public int? CurrentRound { get; set; }

    /// <summary>The member run in flight, if any.</summary>
    public long? CurrentRunId { get; set; }

    /// <summary>The K × R slots in launch order: round 1 for every suite, then round 2.</summary>
    public List<BenchmarkBatterySlotDto> Slots { get; set; } = new();

    /// <summary>Every member row, superseded ones included, in suite, round and insertion order.</summary>
    public List<BenchmarkBatteryMemberDto> Members { get; set; } = new();

    /// <summary>The estimated cost so far; null on the battery-run list and when no estimator is available.</summary>
    public BenchmarkBatteryLiveCostDto? LiveCost { get; set; }

    /// <summary>
    /// Mean model time over the Ok answers of every non-superseded member run, in ms; null on the
    /// battery-run list and while no answer is Ok.
    /// </summary>
    public double? MeanModelTimeMs { get; set; }

    /// <summary>The Ok answers <see cref="MeanModelTimeMs"/> is the mean of; 0 on the battery-run list.</summary>
    public int ModelTimedAnswerCount { get; set; }

    // The latest analysis, summarized for the battery-run table. All null until one is computed.
    public long? LatestAnalysisId { get; set; }
    public DateTime? LatestAnalysisAtUtc { get; set; }
    public bool? LatestAnalysisComplete { get; set; }
    /// <summary>The latest analysis's comparability class; null while it is incomplete.</summary>
    public string? ComparabilityClassSha256 { get; set; }
    public double? OverallIndex { get; set; }
    public double? OverallIndexHalfWidth { get; set; }
    public double? OverallIndexLower { get; set; }
    public double? OverallIndexUpper { get; set; }
    public double? OverallSpeedIndex { get; set; }
    public double? TotalCost { get; set; }

    /// <summary>The usable members changed since the latest analysis.</summary>
    public bool AnalysisStale { get; set; }

    /// <summary>The latest analysis lists an excluded member, so a recompute may change it.</summary>
    public bool AnalysisHasExcludedMembers { get; set; }
}

// --- Analyses ------------------------------------------------------------------------------------

/// <summary>Computes a battery analysis, optionally paired against a baseline battery run.</summary>
public class BenchmarkBatteryCompareRequest
{
    /// <summary>The baseline battery run. Null computes this battery run alone.</summary>
    public long? CompareWithBatteryRunId { get; set; }
}

/// <summary>
/// A stored battery analysis. <see cref="Result"/> and <see cref="Comparison"/> are the statistics
/// records themselves, passed through rather than mirrored.
/// </summary>
public class BenchmarkBatteryAnalysisDto
{
    public long Id { get; set; }
    public long BatteryRunId { get; set; }
    public string BatteryName { get; set; } = string.Empty;
    public DateTime ComputedAtUtc { get; set; }

    /// <summary>The usable member run ids the result was computed over.</summary>
    public List<long> MemberRunIds { get; set; } = new();

    public int RunCount { get; set; }
    public string DefinitionSha256 { get; set; } = string.Empty;

    /// <summary>Null when the battery run was incomplete.</summary>
    public string? ComparabilityClassSha256 { get; set; }

    public bool Complete { get; set; }
    public string? HarnessVersion { get; set; }
    public int ScoringMethodVersion { get; set; }

    /// <summary>The usable member runs differ from the ones the result was computed over.</summary>
    public bool Stale { get; set; }

    public long? ComparedWithBatteryRunId { get; set; }
    public string? ComparedWithBatteryName { get; set; }

    public BenchmarkBatteryStatisticsResult? Result { get; set; }
    public BenchmarkBatteryComparison? Comparison { get; set; }

    /// <summary>The members left out of the result, each with its reason.</summary>
    public List<BenchmarkBatteryExcludedMember> ExcludedMembers { get; set; } = new();
}

// --- Leaderboard ---------------------------------------------------------------------------------

/// <summary>The latest analysis of one battery run, as a leaderboard row.</summary>
public class BenchmarkBatteryLeaderboardRowDto
{
    public long BatteryRunId { get; set; }
    public long? BatteryId { get; set; }
    public string BatteryName { get; set; } = string.Empty;
    public int DefinitionRevision { get; set; }
    public long AnalysisId { get; set; }
    public DateTime ComputedAtUtc { get; set; }

    public long? TestedModelConfigurationId { get; set; }
    public string? TestedModelLabel { get; set; }

    // The tested model's settings, read as the battery run DTO reads them.
    public string? TestedProvider { get; set; }
    public string? TestedModelId { get; set; }
    public string? TestedThinkingLevel { get; set; }
    public string? TestedReasoningMode { get; set; }
    public string? TestedServiceTier { get; set; }

    /// <summary>The battery run's <c>BenchmarkRunSeriesStatus</c> as text.</summary>
    public string Status { get; set; } = string.Empty;

    public int RunsPerSuite { get; set; }
    public int SuiteCount { get; set; }
    public int CompletedSuiteCount { get; set; }
    public bool Complete { get; set; }
    public string? ComparabilityClassSha256 { get; set; }
    public string? HarnessVersion { get; set; }
    public int ScoringMethodVersion { get; set; }

    public double? OverallIndex { get; set; }
    public double? OverallIndexHalfWidth { get; set; }
    public double? OverallIndexLower { get; set; }
    public double? OverallIndexUpper { get; set; }
    public double? OverallSpeedIndex { get; set; }
    public double? TotalCost { get; set; }
    public double? PassCost { get; set; }
}

/// <summary>
/// The battery runs whose results may stand in one ranked list: same definition hash and same
/// comparability class (Statistical Method M9).
/// </summary>
public class BenchmarkBatteryLeaderboardClassDto
{
    public string ComparabilityClassSha256 { get; set; } = string.Empty;

    /// <summary>What distinguishes this class, ready to render as its heading.</summary>
    public string Label { get; set; } = string.Empty;

    public string? HarnessVersion { get; set; }
    public int ScoringMethodVersion { get; set; }

    /// <summary>
    /// The must-match comparability keys on which this class differs from another class of the
    /// same definition; empty when it is the only class.
    /// </summary>
    public List<string> DistinguishingKeys { get; set; } = new();

    /// <summary>Sorted by Overall Index, highest first.</summary>
    public List<BenchmarkBatteryLeaderboardRowDto> Rows { get; set; } = new();
}

public class BenchmarkBatteryLeaderboardDto
{
    public string DefinitionSha256 { get; set; } = string.Empty;

    /// <summary>The current battery carrying this hash, when there is one.</summary>
    public long? BatteryId { get; set; }
    public string? BatteryName { get; set; }

    /// <summary>One ranked list per comparability class; results of different classes are never ranked together.</summary>
    public List<BenchmarkBatteryLeaderboardClassDto> Classes { get; set; } = new();

    /// <summary>Battery runs whose latest analysis is incomplete, unranked, newest first.</summary>
    public List<BenchmarkBatteryLeaderboardRowDto> Incomplete { get; set; } = new();
}
