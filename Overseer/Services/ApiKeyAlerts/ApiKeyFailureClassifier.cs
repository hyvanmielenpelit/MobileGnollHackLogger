namespace Overseer.Services.ApiKeyAlerts;

public enum ApiKeyFailureKind { InsufficientBalance, KeyRejected }

/// <summary>
/// Decides whether a provider's error response means the operator's API key itself is unusable:
/// out of balance, or rejected as invalid, expired, revoked or not permitted. Deliberately
/// conservative, because a match emails the developers: a rate limit, an overload, a server error
/// or a timeout is never a key failure, and a 429 counts only when it names an empty balance.
/// </summary>
/// <remarks>
/// Google answers both per-minute rate limits and depleted quota with
/// <c>429 RESOURCE_EXHAUSTED</c>, and no token in the body reliably separates the two, so a Google
/// 429 is never classified. See <c>docs/overseer/api-key-alerts.md</c>.
/// </remarks>
public static class ApiKeyFailureClassifier
{
    /// <param name="provider">The provider name; a custom endpoint uses its provider's rules.</param>
    /// <param name="httpStatus">The response status, or null for a provider stream error event.</param>
    /// <param name="body">The response body or the stream error text.</param>
    public static ApiKeyFailureKind? Classify(string? provider, int? httpStatus, string? body)
    {
        string text = body ?? string.Empty;
        bool Has(string token) => text.Contains(token, StringComparison.OrdinalIgnoreCase);

        if (httpStatus is >= 500) return null;

        switch (CanonicalProvider(provider))
        {
            case "Anthropic":
                if (httpStatus == null)
                {
                    if (Has("billing_error")) return ApiKeyFailureKind.InsufficientBalance;
                    if (Has("authentication_error")) return ApiKeyFailureKind.KeyRejected;
                    return null;
                }
                if (httpStatus == 402 || Has("billing_error") || (httpStatus == 400 && Has("credit balance")))
                    return ApiKeyFailureKind.InsufficientBalance;
                if (httpStatus is 401 or 403)
                    return ApiKeyFailureKind.KeyRejected;
                return null;

            case "OpenAI":
                if (Has("insufficient_quota") || Has("billing_hard_limit_reached") || httpStatus == 402)
                    return ApiKeyFailureKind.InsufficientBalance;
                if (httpStatus == null)
                    return Has("invalid_api_key") ? ApiKeyFailureKind.KeyRejected : null;
                if (httpStatus is 401 or 403)
                    return ApiKeyFailureKind.KeyRejected;
                return null;

            case "Google":
                if (httpStatus == null)
                {
                    if (Has("BILLING_DISABLED")) return ApiKeyFailureKind.InsufficientBalance;
                    if (Has("API_KEY_INVALID")) return ApiKeyFailureKind.KeyRejected;
                    return null;
                }
                if (httpStatus == 402
                    || (httpStatus is 400 or 403 && (Has("BILLING_DISABLED") || Has("billing account"))))
                    return ApiKeyFailureKind.InsufficientBalance;
                if (httpStatus is 401 or 403
                    || (httpStatus == 400 && (Has("API_KEY_INVALID") || Has("API key not valid") || Has("API key expired"))))
                    return ApiKeyFailureKind.KeyRejected;
                return null;

            default:
                return null;
        }
    }

    private static string? CanonicalProvider(string? provider)
    {
        if (string.IsNullOrWhiteSpace(provider)) return null;
        string trimmed = provider.Trim();
        foreach (var name in new[] { "Anthropic", "Google", "OpenAI" })
        {
            if (string.Equals(trimmed, name, StringComparison.OrdinalIgnoreCase))
                return name;
        }
        return null;
    }
}
