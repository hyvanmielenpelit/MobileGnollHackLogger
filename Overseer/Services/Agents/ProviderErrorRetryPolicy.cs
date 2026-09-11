namespace Overseer.Services.Agents;

/// <summary>
/// Decides whether a provider stream error event is worth retrying. One retry vocabulary is
/// shared across every provider, because each spells a transient overload differently: bracketed
/// error codes, bare HTTP statuses, or plain prose. The deny list wins outright — a request that
/// is malformed, unauthenticated, over quota or over the context window fails the same way on
/// every attempt, even when its text also carries an otherwise retryable token.
/// </summary>
public static class ProviderErrorRetryPolicy
{
    private static readonly string[] NonRetryableTokens =
    {
        "invalid_request_error",
        "insufficient_quota",
        "context_length_exceeded",
        "authentication"
    };

    private static readonly string[] RetryableTokens =
    {
        "[overloaded_error]",
        "[rate_limit_error]",
        "[api_error]",
        "[server_error]",
        "[overloaded]",
        "[rate_limit_exceeded]",
        "529",
        "503",
        "502",
        "504",
        "overloaded",
        "service unavailable",
        "server_error",
        "rate limit"
    };

    public static bool IsRetryable(string? errorEventData)
    {
        if (string.IsNullOrEmpty(errorEventData)) return false;

        foreach (var token in NonRetryableTokens)
        {
            if (errorEventData.Contains(token, StringComparison.OrdinalIgnoreCase)) return false;
        }

        foreach (var token in RetryableTokens)
        {
            if (errorEventData.Contains(token, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }
}
