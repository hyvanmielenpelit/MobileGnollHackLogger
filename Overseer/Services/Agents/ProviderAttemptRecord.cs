namespace Overseer.Services.Agents;

using MobileGnollHackLogger.Data;

/// <summary>
/// One provider attempt of a model call, kept in memory on <see cref="ModelCallRecord.Attempts"/>: one
/// HTTP request, or one governor refusal before a request was sent. Only GnollBench persists it, as a
/// <c>BenchmarkProviderAttempt</c> row. Holds no body text, headers or secrets.
/// </summary>
public sealed class ProviderAttemptRecord
{
    /// <summary>Zero-based position of the attempt within its call.</summary>
    public int AttemptIndex { get; set; }

    /// <summary>Wall-clock time of the attempt's send (of the permit request, for a governor refusal).</summary>
    public DateTime StartedAtUtc { get; set; }

    public int DurationMs { get; set; }

    public ProviderAttemptOutcome Outcome { get; set; }

    public int? HttpStatus { get; set; }

    /// <summary>The provider's error type, code or status, restricted to <c>[A-Za-z0-9_.-]</c> and at most 64 characters.</summary>
    public string? ProviderErrorCode { get; set; }

    public BenchmarkEndpointKind EndpointKind { get; set; }

    /// <summary>The call went on to another attempt after this one.</summary>
    public bool Retried { get; set; }

    public int? RetryAfterMs { get; set; }

    /// <summary>The attempt was cut by the caller's question time budget rather than by an operator cancel.</summary>
    public bool QuestionBudgetCanceled { get; set; }
}
