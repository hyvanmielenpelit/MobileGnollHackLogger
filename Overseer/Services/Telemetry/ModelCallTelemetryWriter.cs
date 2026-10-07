namespace Overseer.Services.Telemetry;

using MobileGnollHackLogger.Data;
using Overseer.Services.Agents;

/// <summary>Where a model call's telemetry row belongs.</summary>
/// <param name="BenchmarkRunAnswerId">The answer; null when the row is attached through the answer's navigation, or for a run-level call.</param>
/// <param name="BenchmarkRunId">The run, for run-level grader calls such as synthesis.</param>
/// <param name="SystemAiApiConfigurationId">The configuration that made the call; attribution only.</param>
/// <param name="GraderRole">The grading role; null for a candidate call.</param>
public readonly record struct ModelCallLinks(
    long? BenchmarkRunAnswerId = null,
    long? BenchmarkRunId = null,
    long? SystemAiApiConfigurationId = null,
    ModelCallGraderRole? GraderRole = null);

/// <summary>
/// Turns the in-memory <see cref="ModelCallRecord"/>s of an agent run into
/// <see cref="ModelCallTelemetry"/> rows, cutting every string to its column and every count to its
/// <c>byte</c>. Pure apart from <see cref="AddRange"/> and <see cref="AttachTo"/>, which only add rows
/// to a context or a navigation; the caller saves.
/// </summary>
public static class ModelCallTelemetryWriter
{
    public static ModelCallTelemetry Map(ModelCallRecord record, ModelCallSource source, ModelCallLinks links)
    {
        return new ModelCallTelemetry
        {
            Source = source,
            GraderRole = source == ModelCallSource.BenchmarkGrader ? links.GraderRole : null,
            BenchmarkRunAnswerId = links.BenchmarkRunAnswerId,
            BenchmarkRunId = links.BenchmarkRunId,
            SystemAiApiConfigurationId = links.SystemAiApiConfigurationId,

            Provider = Cut(record.Provider, 64) ?? string.Empty,
            RequestedModelId = Cut(record.RequestedModelId, 128) ?? string.Empty,
            ThinkingLevelSent = Cut(record.ThinkingLevelSent, 32),
            ReasoningSummarySent = Cut(record.ReasoningSummarySent, 32),
            ServiceTierRequested = Cut(record.ServiceTierRequested, 32),
            MaxOutputTokensSent = record.MaxOutputTokensSent,
            EndpointKind = Cut(record.EndpointKind, 16),

            ServedModelId = Cut(record.ServedModelId, 160),
            ResponseId = Cut(record.ResponseId, 160),
            RequestId = Cut(record.RequestId, 160),
            ServedServiceTier = Cut(record.ServedServiceTier, 32),
            ServedSpeed = Cut(record.ServedSpeed, 16),
            FinishReason = Cut(record.FinishReason, 64),
            IsRefusal = record.IsRefusal,
            FallbackModelId = Cut(record.FallbackModelId, 160),
            HttpVersion = Cut(record.HttpVersion, 8),

            StartedAtUtc = record.StartedAtUtc,
            CallIndex = record.CallIndex,
            PermitWaitMs = Math.Max(0, record.PermitWaitMs),
            BackoffWaitMs = Math.Max(0, record.BackoffWaitMs),
            FailedAttemptMs = Math.Max(0, record.FailedAttemptMs),
            AttemptCount = ToByte(record.AttemptCount),
            Http429Count = ToByte(record.Http429Count),
            Http5xxCount = ToByte(record.Http5xxCount),
            StreamErrorRetryCount = ToByte(record.StreamErrorRetryCount),
            FinalHttpStatus = record.FinalHttpStatus,

            HeadersMs = record.HeadersMs,
            ServerProcessingMs = record.ServerProcessingMs,
            FirstEventMs = record.FirstEventMs,
            FirstReasoningMs = record.FirstReasoningMs,
            FirstOutputMs = record.FirstOutputMs,
            FirstToolCallMs = record.FirstToolCallMs,
            LastDeltaMs = record.LastDeltaMs,
            CompletedMs = record.CompletedMs,
            StreamEndMs = record.StreamEndMs,

            OutputDeltaCount = record.OutputDeltaCount,
            VisibleOutputChars = record.VisibleOutputChars,
            Last80DecodeSpanMs = record.Last80DecodeSpanMs,
            Last80VisibleChars = record.Last80VisibleChars,

            InputTokens = record.Usage?.TotalPromptTokens,
            CachedInputTokens = record.Usage?.CacheReadTokens,
            CacheWriteTokens = record.Usage?.CacheCreationTokens,
            OutputTokens = record.Usage?.OutputTokens,
            ReasoningTokens = record.Usage?.ReasoningTokens,

            RateLimitJson = record.RateLimitJson != null && record.RateLimitJson.Length <= 512 ? record.RateLimitJson : null,
            ErrorKind = Cut(record.ErrorKind, 64),
        };
    }

    /// <summary>Maps every call of <paramref name="result"/> in call order.</summary>
    public static List<ModelCallTelemetry> MapAll(AgentRunResult result, ModelCallSource source, ModelCallLinks links) =>
        result.ModelCalls.Select(r => Map(r, source, links)).ToList();

    /// <summary>Adds the rows of every call of <paramref name="result"/> to the context; returns how many.</summary>
    public static int AddRange(ApplicationDbContext db, AgentRunResult result, ModelCallSource source, ModelCallLinks links)
    {
        var rows = MapAll(result, source, links);
        db.ModelCallTelemetry.AddRange(rows);
        return rows.Count;
    }

    /// <summary>Adds the rows of every call to the answer's <see cref="BenchmarkRunAnswer.ModelCalls"/>, so they are saved with it.</summary>
    public static int AttachTo(BenchmarkRunAnswer answer, AgentRunResult result, ModelCallSource source, ModelCallLinks links)
    {
        var rows = MapAll(result, source, links with { BenchmarkRunAnswerId = null });
        answer.ModelCalls.AddRange(rows);
        return rows.Count;
    }

    /// <summary>
    /// The model id every call that reported one agrees on; null when none reported one or two
    /// disagree.
    /// </summary>
    public static string? ConsensusServedModelId(IEnumerable<ModelCallRecord> records)
    {
        string? served = null;
        foreach (var id in records.Select(r => r.ServedModelId).Where(id => !string.IsNullOrEmpty(id)))
        {
            if (served == null)
            {
                served = id;
            }
            else if (!string.Equals(served, id, StringComparison.Ordinal))
            {
                return null;
            }
        }

        return Cut(served, 160);
    }

    private static string? Cut(string? value, int max) =>
        value == null || value.Length <= max ? value : value[..max];

    private static byte ToByte(int value) => (byte)Math.Clamp(value, 0, byte.MaxValue);
}
