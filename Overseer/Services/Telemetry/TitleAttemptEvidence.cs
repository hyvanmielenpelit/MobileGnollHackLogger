namespace Overseer.Services.Telemetry;

using MobileGnollHackLogger.Data;
using Overseer.Services.Agents;

/// <summary>
/// Builds the <see cref="ProviderAttemptRecord"/> of one title-generation attempt from the evidence it left, classified
/// with the same <see cref="ProviderAttemptClassifier"/> the agent loop uses.
/// </summary>
internal static class TitleAttemptEvidence
{
    /// <summary>A refusal before any request was sent: the governor's (a permit timeout) or the caller's cancel.</summary>
    public static ProviderAttemptRecord BeforeSend(string provider, BenchmarkEndpointKind endpointKind, bool governorRefused,
        int attemptIndex, DateTime startedUtc, int durationMs) =>
        Build(new ProviderAttemptEvidence(provider, endpointKind, null, null, null, null, null, null, AttemptStage.BeforeSend,
                ChunksReceived: false, CompletionSeen: false, CallerCanceled: !governorRefused, QuestionBudgetCanceled: false,
                GovernorRefused: governorRefused, ApiKeyBilling: false, ApiKeyRejected: false, ModelNotFound: false),
            null, attemptIndex, startedUtc, durationMs, retried: false, retryAfterMs: null);

    /// <summary>An exception while the request was out; a cancel of the caller's token is our cancel.</summary>
    public static ProviderAttemptRecord FromException(string provider, BenchmarkEndpointKind endpointKind, Exception exception,
        bool callerCanceled, int attemptIndex, DateTime startedUtc, int durationMs, bool retried) =>
        Build(new ProviderAttemptEvidence(provider, endpointKind, null, null, null, null, null, exception, AttemptStage.AwaitingHeaders,
                ChunksReceived: false, CompletionSeen: false, CallerCanceled: callerCanceled, QuestionBudgetCanceled: false,
                GovernorRefused: false, ApiKeyBilling: false, ApiKeyRejected: false, ModelNotFound: false),
            null, attemptIndex, startedUtc, durationMs, retried, retryAfterMs: null);

    /// <summary>A non-2xx response, with its body, rate-limit headers and Retry-After.</summary>
    public static ProviderAttemptRecord FromErrorResponse(string provider, BenchmarkEndpointKind endpointKind, int httpStatus, string? body,
        IReadOnlyDictionary<string, string>? rateLimitHeaders, int? retryAfterMs, string modelId,
        int attemptIndex, DateTime startedUtc, int durationMs, bool retried)
    {
        var fields = ProviderAttemptClassifier.ParseErrorBody(body);
        var keyFailure = ApiKeyAlerts.ApiKeyFailureClassifier.Classify(provider, httpStatus, body);
        var evidence = new ProviderAttemptEvidence(
            provider, endpointKind, httpStatus, fields?.Code, fields?.Type, ProviderAttemptClassifier.Excerpt(body),
            rateLimitHeaders, null, AttemptStage.Streaming,
            ChunksReceived: false, CompletionSeen: false, CallerCanceled: false, QuestionBudgetCanceled: false, GovernorRefused: false,
            ApiKeyBilling: keyFailure == ApiKeyAlerts.ApiKeyFailureKind.InsufficientBalance,
            ApiKeyRejected: keyFailure == ApiKeyAlerts.ApiKeyFailureKind.KeyRejected,
            ModelNotFound: Providers.ModelNotFoundClassifier.IsModelNotFound(provider, httpStatus, body, modelId),
            ErrorStatus: fields?.Status, ErrorDetailTypes: fields?.DetailTypes);
        return Build(evidence, fields, attemptIndex, startedUtc, durationMs, retried, retryAfterMs);
    }

    /// <summary>A 2xx response whose body was read whole.</summary>
    public static ProviderAttemptRecord Success(string provider, BenchmarkEndpointKind endpointKind, int httpStatus,
        int attemptIndex, DateTime startedUtc, int durationMs) =>
        Build(new ProviderAttemptEvidence(provider, endpointKind, httpStatus, null, null, null, null, null, AttemptStage.Streaming,
                ChunksReceived: true, CompletionSeen: true, CallerCanceled: false, QuestionBudgetCanceled: false,
                GovernorRefused: false, ApiKeyBilling: false, ApiKeyRejected: false, ModelNotFound: false),
            null, attemptIndex, startedUtc, durationMs, retried: false, retryAfterMs: null);

    private static ProviderAttemptRecord Build(ProviderAttemptEvidence evidence, ProviderErrorFields? fields, int attemptIndex,
        DateTime startedUtc, int durationMs, bool retried, int? retryAfterMs)
    {
        var outcome = ProviderAttemptClassifier.Classify(evidence);
        return new ProviderAttemptRecord
        {
            AttemptIndex = attemptIndex,
            StartedAtUtc = startedUtc,
            DurationMs = Math.Max(0, durationMs),
            Outcome = outcome,
            HttpStatus = evidence.HttpStatus,
            ProviderErrorCode = outcome == ProviderAttemptOutcome.Ok || fields == null
                ? null
                : ProviderAttemptClassifier.PreferredErrorCode(evidence.Provider, fields.Code, fields.Type, fields.Status),
            EndpointKind = evidence.EndpointKind,
            Retried = retried,
            RetryAfterMs = retryAfterMs
        };
    }
}
