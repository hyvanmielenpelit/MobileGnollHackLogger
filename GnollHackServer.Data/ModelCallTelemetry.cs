namespace MobileGnollHackLogger.Data;

using System;
using System.ComponentModel.DataAnnotations;

/// <summary>Whose model call a <see cref="ModelCallTelemetry"/> row records.</summary>
public enum ModelCallSource
{
    BenchmarkCandidate = 1,
    BenchmarkGrader = 2,
}

/// <summary>The grading role of a <see cref="ModelCallSource.BenchmarkGrader"/> call.</summary>
public enum ModelCallGraderRole
{
    Assessor = 1,
    CoAssessor = 2,
    SecondOpinion = 3,
    ClaimVerifier = 4,
    Synthesis = 5,
}

/// <summary>
/// One model call of a GnollBench answer, candidate or grader: what was requested, what the provider
/// reported serving, and where the time went. Durations come from the client's monotonic clock;
/// <see cref="StartedAtUtc"/> is wall-clock time.
///
/// <para>The marks <see cref="HeadersMs"/>, <see cref="FirstEventMs"/> to <see cref="StreamEndMs"/> are relative to the
/// send of the successful attempt; <see cref="PermitWaitMs"/>, <see cref="BackoffWaitMs"/> and
/// <see cref="FailedAttemptMs"/> are the time before it. A call that failed for good still writes a
/// row, with <see cref="ErrorKind"/> set. Null means "not recorded", never zero.</para>
/// </summary>
public class ModelCallTelemetry
{
    public long Id { get; set; }

    public ModelCallSource Source { get; set; }

    public ModelCallGraderRole? GraderRole { get; set; }

    /// <summary>The answer the call belongs to. Cascades with the answer.</summary>
    public long? BenchmarkRunAnswerId { get; set; }
    public BenchmarkRunAnswer? BenchmarkRunAnswer { get; set; }

    /// <summary>The run, for run-level grader calls such as synthesis. Not a foreign key.</summary>
    public long? BenchmarkRunId { get; set; }

    /// <summary>Attribution only, not a foreign key.</summary>
    public long? SystemAiApiConfigurationId { get; set; }

    // --- Requested -------------------------------------------------------------------------------

    [MaxLength(64)]
    public string Provider { get; set; } = default!;

    [MaxLength(128)]
    public string RequestedModelId { get; set; } = default!;

    [MaxLength(32)]
    public string? ThinkingLevelSent { get; set; }

    [MaxLength(32)]
    public string? ReasoningSummarySent { get; set; }

    [MaxLength(32)]
    public string? ServiceTierRequested { get; set; }

    public int? MaxOutputTokensSent { get; set; }

    [MaxLength(16)]
    public string? EndpointKind { get; set; }

    // --- Served ----------------------------------------------------------------------------------

    [MaxLength(160)]
    public string? ServedModelId { get; set; }

    [MaxLength(160)]
    public string? ResponseId { get; set; }

    [MaxLength(160)]
    public string? RequestId { get; set; }

    [MaxLength(32)]
    public string? ServedServiceTier { get; set; }

    [MaxLength(16)]
    public string? ServedSpeed { get; set; }

    [MaxLength(64)]
    public string? FinishReason { get; set; }

    public bool IsRefusal { get; set; }

    [MaxLength(160)]
    public string? FallbackModelId { get; set; }

    [MaxLength(8)]
    public string? HttpVersion { get; set; }

    // --- Timing ----------------------------------------------------------------------------------

    public DateTime StartedAtUtc { get; set; }

    /// <summary>Zero-based position of the call within its answer and role.</summary>
    public int CallIndex { get; set; }

    public int PermitWaitMs { get; set; }
    public int BackoffWaitMs { get; set; }
    public int FailedAttemptMs { get; set; }

    public byte AttemptCount { get; set; }
    public byte Http429Count { get; set; }
    public byte Http5xxCount { get; set; }
    public byte StreamErrorRetryCount { get; set; }

    public int? FinalHttpStatus { get; set; }

    public int? HeadersMs { get; set; }

    /// <summary><c>openai-processing-ms</c> where the provider sends it.</summary>
    public int? ServerProcessingMs { get; set; }

    public int? FirstEventMs { get; set; }
    public int? FirstReasoningMs { get; set; }

    /// <summary>First visible text or tool-call delta, before the reasoning sanitizer.</summary>
    public int? FirstOutputMs { get; set; }

    public int? FirstToolCallMs { get; set; }
    public int? LastDeltaMs { get; set; }
    public int? CompletedMs { get; set; }
    public int? StreamEndMs { get; set; }

    public int OutputDeltaCount { get; set; }
    public int VisibleOutputChars { get; set; }

    /// <summary>Elapsed time and visible characters over the last 80 % of visible deltas.</summary>
    public int? Last80DecodeSpanMs { get; set; }
    public int? Last80VisibleChars { get; set; }

    // --- Tokens ----------------------------------------------------------------------------------

    public int? InputTokens { get; set; }
    public int? CachedInputTokens { get; set; }
    public int? CacheWriteTokens { get; set; }
    public int? OutputTokens { get; set; }
    public int? ReasoningTokens { get; set; }

    // --- Other -----------------------------------------------------------------------------------

    /// <summary>The rate-limit headers of the successful attempt, as compact JSON.</summary>
    [MaxLength(512)]
    public string? RateLimitJson { get; set; }

    [MaxLength(64)]
    public string? ErrorKind { get; set; }
}
