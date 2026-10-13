namespace Overseer.Services.Agents;

/// <summary>What a provider response's headers say about rate limits, shared by the agent loop and title generation.</summary>
internal static class ProviderResponseEvidence
{
    /// <summary>The response's rate-limit headers, lower-case names; null when it sent none.</summary>
    internal static Dictionary<string, string>? ReadRateLimitHeaders(HttpResponseMessage response)
    {
        Dictionary<string, string>? limits = null;
        foreach (var header in response.Headers)
        {
            string key = header.Key.ToLowerInvariant();
            if (key.StartsWith("x-ratelimit-", StringComparison.Ordinal)
                || key.StartsWith("anthropic-ratelimit-", StringComparison.Ordinal)
                || key == "retry-after"
                || key == "retry-after-ms")
            {
                limits ??= new Dictionary<string, string>(StringComparer.Ordinal);
                limits[key] = string.Join(",", header.Value);
            }
        }
        return limits;
    }

    /// <summary>The wait the provider asked for, from <c>retry-after-ms</c> or <c>Retry-After</c>; null when it named none.</summary>
    internal static int? ReadRetryAfterMs(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("retry-after-ms", out var msValues)
            && int.TryParse(msValues.FirstOrDefault(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int ms)
            && ms >= 0)
        {
            return ms;
        }

        var retryAfter = response.Headers.RetryAfter;
        double? wait = retryAfter?.Delta is TimeSpan delta ? (double?)delta.TotalMilliseconds
            : retryAfter?.Date is DateTimeOffset date ? Math.Max(0, (date - DateTimeOffset.UtcNow).TotalMilliseconds)
            : (double?)null;
        return wait == null ? null : (int)Math.Min(int.MaxValue, Math.Round(wait.Value));
    }
}
