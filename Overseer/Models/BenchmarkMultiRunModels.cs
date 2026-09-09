namespace Overseer.Models;

using System;
using System.Collections.Generic;
using MobileGnollHackLogger.Data;

// DTOs for the multi-run feature: series execution, analysis groups, comparability tiers and the
// compliance limits the run-count field bounds itself by.
//
// They live in their own file rather than in BenchmarkAdminModels.cs because the multi-run contract
// is large enough to be worth reading in one place, and because the statistics records themselves
// are deliberately *not* duplicated here: `BenchmarkGroupStatistics` already returns immutable,
// DTO-shaped records with documented field names, and mirroring them into a second set of types
// would create exactly the drift the tier model exists to prevent.

/// <summary>
/// The compliance caps and the live rolling-window counts.
///
/// These were not exposed to the client at any endpoint before multi-run, which is why the
/// run-count field had nothing to bound itself by. One owner for the window arithmetic — the
/// guard — and this is its projection; the controller must not re-derive it.
/// </summary>
public class BenchmarkRunLimitsDto
{
    public int MaxRunsPerHour { get; set; }
    public int MaxRunsPerDay { get; set; }

    /// <summary>Runs started in the last rolling hour — not the current clock hour.</summary>
    public int RunsInLastHour { get; set; }

    /// <summary>
    /// Runs started in the last rolling 24 hours — **not** the current calendar day. The guard
    /// counts `StartedAtUtc >= now.AddHours(-24)`, so remaining headroom must be computed the same
    /// way or the field's maximum will disagree with the server that enforces it.
    /// </summary>
    public int RunsInLast24Hours { get; set; }

    /// <summary>`MaxRunsPerDay − RunsInLast24Hours`, floored at zero.</summary>
    public int RemainingDailyHeadroom { get; set; }

    /// <summary>The largest `RunCount` the server will currently accept: `MaxRunsPerDay`.</summary>
    public int MaxRunCountPerSeries { get; set; }
}

public class BenchmarkRunSeriesMemberDto
{
    public int Index { get; set; }
    public long RunId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public int? QualityIndex { get; set; }
    public int? SpeedIndex { get; set; }
    public double? EstimatedCost { get; set; }
    public long? DurationMs { get; set; }

    /// <summary>Answered / total, for the compact progress line on the running member.</summary>
    public int AnsweredQuestionCount { get; set; }
    public int TotalQuestionCount { get; set; }

    /// <summary>First eight hex characters of the candidate system prompt hash.</summary>
    public string? ShortFingerprint { get; set; }
}

public class BenchmarkRunSeriesDto
{
    public long Id { get; set; }
    public long? BenchmarkSuiteId { get; set; }
    public string SuiteName { get; set; } = string.Empty;

    public int RequestedRunCount { get; set; }
    public int CompletedRunCount { get; set; }
    public int FailedRunCount { get; set; }

    public string Status { get; set; } = string.Empty;

    /// <summary>`MemberFailed`, `RunCapReached` or `SpendDenied`; null unless the status is Stopped.</summary>
    public string? StopReason { get; set; }

    /// <summary>The stop reason in words, ready to render beside a Continue button.</summary>
    public string? StopReasonText { get; set; }

    public bool AllowCapWait { get; set; }
    public bool Resumable { get; set; }

    public DateTime StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? ErrorMessage { get; set; }

    // The first member's instrument fingerprint and the current one, so a refused resume is
    // self-explaining without a second request.
    public string? FirstMemberCandidateSystemPromptSha256 { get; set; }
    public string? FirstMemberToolGuidesSha256 { get; set; }
    public string? FirstMemberKnowledgeBaseHeadSha { get; set; }
    public string? FirstMemberWikiHeadSha { get; set; }
    public string? FirstMemberSourceCodeHeadSha { get; set; }
    public string? CurrentCandidateSystemPromptSha256 { get; set; }
    public string? CurrentToolGuidesSha256 { get; set; }
    public string? CurrentKnowledgeBaseHeadSha { get; set; }
    public string? CurrentWikiHeadSha { get; set; }
    public string? CurrentSourceCodeHeadSha { get; set; }

    /// <summary>Which of the five hashes moved since member 1. Empty when nothing moved.</summary>
    public List<string> ChangedInstrumentHashes { get; set; } = new();

    public bool InstrumentChangeAcknowledged { get; set; }

    public long? AutoCreatedGroupId { get; set; }
    public string? AutoCreatedGroupTier { get; set; }

    public List<BenchmarkRunSeriesMemberDto> Members { get; set; } = new();
}

public class ResumeBenchmarkRunSeriesRequest
{
    /// <summary>
    /// Proceed even though an instrument hash moved while the series was stopped. The resulting
    /// group is marked **Tier C**, so those runs stay usable for comparison and can never be pooled
    /// into one index — the dangerous case made impossible by construction rather than by
    /// discipline.
    /// </summary>
    public bool AcknowledgeInstrumentChange { get; set; }
}

/// <summary>One distinct value of a comparability key, and the runs carrying it.</summary>
public class BenchmarkComparabilityVariantDto
{
    public string Value { get; set; } = string.Empty;
    public List<long> RunIds { get; set; } = new();
}

/// <summary>A key the set does not agree on. A tier verdict without these is unusable in a dialog.</summary>
public class BenchmarkComparabilityDifferenceDto
{
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<BenchmarkComparabilityVariantDto> Variants { get; set; } = new();
}

public class BenchmarkComparabilityResultDto
{
    /// <summary>`Replicate`, `QualityComparable`, `CrossCondition` or `NotComparable`.</summary>
    public string Tier { get; set; } = string.Empty;

    /// <summary>"Tier A — Replicate" and so on, ready to render.</summary>
    public string TierLabel { get; set; } = string.Empty;

    public bool PoolingPermitted { get; set; }
    public bool SpeedAggregatesDegraded { get; set; }
    public bool CostAggregatesDegraded { get; set; }
    public string Explanation { get; set; } = string.Empty;
    public string ComparabilityKeyHash { get; set; } = string.Empty;
    public List<string> MatchedKeys { get; set; } = new();
    public List<BenchmarkComparabilityDifferenceDto> Differences { get; set; } = new();
    public List<long> RunIds { get; set; } = new();
}

public class BenchmarkRunGroupMemberDto
{
    public long RunId { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public string Status { get; set; } = string.Empty;
    public int? QualityIndex { get; set; }
    public int? SpeedIndex { get; set; }
    public string? TestedModelDisplayName { get; set; }
    public string? ShortFingerprint { get; set; }
    public DateTime AddedAtUtc { get; set; }
}

public class BenchmarkRunGroupDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public long? BenchmarkSuiteId { get; set; }
    public string? SuiteName { get; set; }
    public string Tier { get; set; } = string.Empty;
    public string TierLabel { get; set; } = string.Empty;
    public string? ComparabilityKeyHash { get; set; }
    public bool CrossCondition { get; set; }
    public string? Notes { get; set; }
    public long? CreatedFromSeriesId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ModifiedAtUtc { get; set; }

    public int RunCount { get; set; }
    public List<BenchmarkRunGroupMemberDto> Members { get; set; } = new();

    /// <summary>Null until an analysis has been computed. The report download is disabled until then.</summary>
    public long? LatestAnalysisId { get; set; }
    public DateTime? LatestAnalysisAtUtc { get; set; }

    /// <summary>
    /// The group's membership changed after its last analysis, so the stored figures describe a
    /// different set of runs. Badged rather than discarded: a stale analysis is not wrong.
    /// </summary>
    public bool AnalysisStale { get; set; }
}

public class CreateBenchmarkRunGroupRequest
{
    public string Name { get; set; } = string.Empty;
    public List<long> RunIds { get; set; } = new();
    public string? Notes { get; set; }

    /// <summary>
    /// Required to persist a Tier C group. Without it a cross-condition set is refused, so a set
    /// that quietly stopped being a replicate set cannot become one by accident.
    /// </summary>
    public bool CrossCondition { get; set; }
}

public class UpdateBenchmarkRunGroupRequest
{
    public string? Name { get; set; }
    public List<long>? RunIds { get; set; }
    public string? Notes { get; set; }
    public bool? CrossCondition { get; set; }
}

/// <summary>
/// The result of creating or editing a group. Carries the computed tier and, on refusal, the keys
/// that differ and the runs carrying them — a "no" with no reason is unactionable.
/// </summary>
public class BenchmarkRunGroupTierPreviewDto
{
    public bool Accepted { get; set; }
    public string? Error { get; set; }
    public BenchmarkComparabilityResultDto? Comparability { get; set; }
    public BenchmarkRunGroupDto? Group { get; set; }
}

public class BenchmarkGroupAnalysisRequest
{
    /// <summary>Optional baseline group to pair this one against. Null computes the group alone.</summary>
    public long? CompareWithGroupId { get; set; }
}

/// <summary>
/// A stored analysis. <see cref="Result"/> and <see cref="Comparison"/> are the statistics records
/// themselves — <c>BenchmarkGroupStatisticsResult</c> and <c>BenchmarkGroupComparison</c> — passed
/// through rather than mirrored, so a field cannot exist on one side and not the other.
/// </summary>
public class BenchmarkGroupAnalysisDto
{
    public long Id { get; set; }
    public long GroupId { get; set; }
    public string GroupName { get; set; } = string.Empty;
    public DateTime ComputedAtUtc { get; set; }
    public int RunCount { get; set; }
    public List<long> MemberRunIds { get; set; } = new();
    public string Tier { get; set; } = string.Empty;
    public string TierLabel { get; set; } = string.Empty;
    public string? HarnessVersion { get; set; }
    public int ScoringMethodVersion { get; set; }
    public bool Stale { get; set; }

    public long? ComparedWithGroupId { get; set; }
    public string? ComparedWithGroupName { get; set; }

    public object? Result { get; set; }
    public object? Comparison { get; set; }
}
