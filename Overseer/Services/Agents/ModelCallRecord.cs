namespace Overseer.Services.Agents;

using Overseer.Services.Providers;

/// <summary>
/// One model call of an agent run, kept in memory in call order on
/// <see cref="AgentRunResult.ModelCalls"/>: what was requested, what the provider reported serving,
/// and where the time went. Only GnollBench persists it, as <c>ModelCallTelemetry</c> rows.
///
/// <para>The <c>*Ms</c> marks from <see cref="HeadersMs"/> on are relative to the send of the
/// successful attempt (the last attempt when none succeeded). <see cref="PermitWaitMs"/>,
/// <see cref="BackoffWaitMs"/> and <see cref="FailedAttemptMs"/> are the time spent before that send:
/// waiting for Overseer's own request permit, sleeping between retries, and in attempts that failed.
/// Null means "not observed".</para>
/// </summary>
public sealed class ModelCallRecord
{
    /// <summary>Zero-based position of the call within the run.</summary>
    public int CallIndex { get; set; }

    public DateTime StartedAtUtc { get; set; }

    // --- Requested -------------------------------------------------------------------------------

    public string Provider { get; set; } = string.Empty;
    public string RequestedModelId { get; set; } = string.Empty;
    public string? ThinkingLevelSent { get; set; }
    public string? ReasoningSummarySent { get; set; }
    public string? ServiceTierRequested { get; set; }
    public int? MaxOutputTokensSent { get; set; }

    /// <summary><c>official</c> or <c>custom</c>.</summary>
    public string? EndpointKind { get; set; }

    // --- Served ----------------------------------------------------------------------------------

    public string? ServedModelId { get; set; }
    public string? ResponseId { get; set; }
    public string? RequestId { get; set; }
    public string? ServedServiceTier { get; set; }
    public string? ServedSpeed { get; set; }
    public string? FinishReason { get; set; }
    public bool IsRefusal { get; set; }
    public string? FallbackModelId { get; set; }
    public string? HttpVersion { get; set; }

    // --- Waits and attempts ----------------------------------------------------------------------

    public int PermitWaitMs { get; set; }
    public int BackoffWaitMs { get; set; }
    public int FailedAttemptMs { get; set; }
    public int AttemptCount { get; set; }
    public int Http429Count { get; set; }
    public int Http5xxCount { get; set; }
    public int StreamErrorRetryCount { get; set; }
    public int? FinalHttpStatus { get; set; }

    // --- Marks, relative to the successful attempt's send ---------------------------------------

    public int? HeadersMs { get; set; }
    public int? ServerProcessingMs { get; set; }
    public int? FirstEventMs { get; set; }
    public int? FirstReasoningMs { get; set; }
    public int? FirstOutputMs { get; set; }
    public int? FirstToolCallMs { get; set; }
    public int? LastDeltaMs { get; set; }
    public int? CompletedMs { get; set; }
    public int? StreamEndMs { get; set; }

    public int OutputDeltaCount { get; set; }
    public int VisibleOutputChars { get; set; }
    public int? Last80DecodeSpanMs { get; set; }
    public int? Last80VisibleChars { get; set; }

    // --- Usage and other -------------------------------------------------------------------------

    /// <summary>The call's usage report; null when the provider sent none.</summary>
    public TokenUsageReport? Usage { get; set; }

    public string? RateLimitJson { get; set; }

    /// <summary>Why the call failed for good; null on a call that produced a response.</summary>
    public string? ErrorKind { get; set; }

    /// <summary>Copies the provider's meta into this record, with marks relative to <paramref name="sendTicks"/>.</summary>
    public void ApplyMeta(ProviderCallMeta meta, long sendTicks)
    {
        ServedModelId = meta.ServedModelId ?? ServedModelId;
        ResponseId = meta.ResponseId ?? ResponseId;
        ServedSpeed = meta.ServedSpeed ?? ServedSpeed;
        IsRefusal |= meta.IsRefusal;
        FallbackModelId = meta.FallbackModelId ?? FallbackModelId;

        FirstEventMs = ProviderCallMeta.MsBetween(sendTicks, meta.FirstEventTicks);
        FirstReasoningMs = ProviderCallMeta.MsBetween(sendTicks, meta.FirstReasoningTicks);
        FirstOutputMs = ProviderCallMeta.MsBetween(sendTicks, meta.FirstOutputTicks);
        FirstToolCallMs = ProviderCallMeta.MsBetween(sendTicks, meta.FirstToolCallTicks);
        LastDeltaMs = ProviderCallMeta.MsBetween(sendTicks, meta.LastDeltaTicks);
        CompletedMs = ProviderCallMeta.MsBetween(sendTicks, meta.CompletedTicks);

        OutputDeltaCount = meta.OutputDeltaCount;
        VisibleOutputChars = meta.VisibleOutputChars;

        var last80 = meta.Last80();
        Last80DecodeSpanMs = last80 == null ? null : ProviderCallMeta.TicksToMs(last80.Value.SpanTicks);
        Last80VisibleChars = last80?.Chars;
    }
}
