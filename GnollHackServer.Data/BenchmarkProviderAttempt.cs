namespace MobileGnollHackLogger.Data;

using System;
using System.ComponentModel.DataAnnotations;

/// <summary>
/// The outcome class of one provider attempt: whose fault a failure was. Classes 0–21 are valid
/// attempts (the denominator of the provider error rate); classes 30–36 are our own and are excluded.
/// Stored as a byte; the values are part of the stored contract.
/// </summary>
public enum ProviderAttemptOutcome : byte
{
    Ok = 0,

    ProviderOverloaded = 10,
    ProviderServerError = 11,
    ProviderTimeout = 12,
    ProviderStreamFailure = 13,

    RateLimitUnattributed = 20,
    TransportUnattributed = 21,

    OurRateLimit = 30,
    OurBilling = 31,
    OurKey = 32,
    OurRequest = 33,
    OurThrottle = 34,
    OurCanceled = 35,
    OurNetwork = 36,
}

/// <summary>Whose fault an answer's terminal failure was.</summary>
public enum BenchmarkFailureOrigin : byte
{
    None = 0,
    Provider = 1,
    Unattributed = 2,
    Ours = 3,
}

/// <summary>Why a run was replaced by a later run (<see cref="BenchmarkRun.ReplacesRunId"/>).</summary>
public enum BenchmarkReplacementCause : byte
{
    Unknown = 0,
    ProviderFailure = 1,
    OurFailure = 2,
    InstrumentChange = 3,
    OperatorRequest = 4,
}

/// <summary>Whether an attempt went to the provider's official endpoint or to a custom one (a gateway, Vertex, Azure, a local server).</summary>
public enum BenchmarkEndpointKind : byte
{
    Official = 0,
    Custom = 1,
}

/// <summary>Class predicates over <see cref="ProviderAttemptOutcome"/>.</summary>
public static class ProviderAttemptOutcomes
{
    /// <summary>In the denominator: <c>Ok</c>, the provider classes and the unattributed classes.</summary>
    public static bool IsValid(ProviderAttemptOutcome outcome) => (byte)outcome < 30;

    public static bool IsProvider(ProviderAttemptOutcome outcome) => (byte)outcome is >= 10 and < 20;

    public static bool IsUnattributed(ProviderAttemptOutcome outcome) => (byte)outcome is >= 20 and < 30;

    public static bool IsOurs(ProviderAttemptOutcome outcome) => (byte)outcome >= 30;

    /// <summary>The failure origin an answer ended by this outcome carries.</summary>
    public static BenchmarkFailureOrigin OriginOf(ProviderAttemptOutcome outcome) =>
        outcome == ProviderAttemptOutcome.Ok ? BenchmarkFailureOrigin.None
        : IsProvider(outcome) ? BenchmarkFailureOrigin.Provider
        : IsUnattributed(outcome) ? BenchmarkFailureOrigin.Unattributed
        : BenchmarkFailureOrigin.Ours;
}

/// <summary>
/// One provider attempt of a GnollBench run: one HTTP request, or one governor refusal before a request
/// was sent. Append-only: a re-run, re-assessment or re-synthesis never deletes a row; rows cascade with
/// their run only. Written only for runs with <see cref="BenchmarkRun.ProviderLedgerVersion"/> set.
/// Holds no message text, headers or secrets.
/// </summary>
public class BenchmarkProviderAttempt
{
    public long Id { get; set; }

    public long BenchmarkRunId { get; set; }
    public BenchmarkRun BenchmarkRun { get; set; } = default!;

    /// <summary>The answer the attempt belonged to. Indexed; not a foreign key, so it can outlive an answer a later re-run replaced.</summary>
    public long? BenchmarkRunAnswerId { get; set; }

    public long? QuestionId { get; set; }

    public ModelCallSource Source { get; set; }

    public ModelCallGraderRole? GraderRole { get; set; }

    [MaxLength(32)]
    public string Provider { get; set; } = default!;

    [MaxLength(128)]
    public string RequestedModelId { get; set; } = default!;

    /// <summary>Attribution only, not a foreign key.</summary>
    public long? SystemAiApiConfigurationId { get; set; }

    public BenchmarkEndpointKind EndpointKind { get; set; }

    /// <summary>0 for the initial execution, n for the n-th repair pass.</summary>
    public int Pass { get; set; }

    /// <summary>Zero-based position of the model call within its answer and role.</summary>
    public int CallIndex { get; set; }

    /// <summary>Zero-based position of the attempt within its model call.</summary>
    public byte AttemptIndex { get; set; }

    public DateTime StartedAtUtc { get; set; }

    public int DurationMs { get; set; }

    public ProviderAttemptOutcome Outcome { get; set; }

    public short? HttpStatus { get; set; }

    /// <summary>The provider's error type, code or status from the body or stream error event, restricted to <c>[A-Za-z0-9_.-]</c>.</summary>
    [MaxLength(64)]
    public string? ProviderErrorCode { get; set; }

    /// <summary>The call went on to another attempt after this one.</summary>
    public bool Retried { get; set; }

    public int? RetryAfterMs { get; set; }
}
