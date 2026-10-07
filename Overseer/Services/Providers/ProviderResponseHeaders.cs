namespace Overseer.Services.Providers;

using System.Globalization;
using System.Net.Http;
using System.Text.Json;

/// <summary>
/// The few response headers a model call keeps: the provider's request id, OpenAI's server
/// processing time, and the rate-limit headers as compact JSON. Nothing else is read or stored.
/// </summary>
public sealed record ProviderResponseHeaders(string? RequestId, int? ServerProcessingMs, string? RateLimitJson)
{
    /// <summary>The longest <see cref="RateLimitJson"/> kept; the column holds 512 characters.</summary>
    public const int MaxRateLimitJsonLength = 512;

    private static readonly string[] RequestIdHeaders = { "x-request-id", "request-id", "x-goog-request-id" };

    public static ProviderResponseHeaders From(HttpResponseMessage response)
    {
        string? requestId = null;
        foreach (string name in RequestIdHeaders)
        {
            requestId = First(response, name);
            if (!string.IsNullOrEmpty(requestId))
            {
                break;
            }
        }

        int? processingMs = null;
        if (int.TryParse(First(response, "openai-processing-ms"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
        {
            processingMs = parsed;
        }

        var limits = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var header in response.Headers)
        {
            string key = header.Key.ToLowerInvariant();
            if (key.StartsWith("x-ratelimit-", StringComparison.Ordinal)
                || key.StartsWith("anthropic-ratelimit-", StringComparison.Ordinal)
                || key == "retry-after")
            {
                limits[key] = string.Join(",", header.Value);
            }
        }

        string? rateLimitJson = null;
        if (limits.Count > 0)
        {
            rateLimitJson = JsonSerializer.Serialize(limits);
            if (rateLimitJson.Length > MaxRateLimitJsonLength)
            {
                rateLimitJson = null;
                // Drop the longest values until the object fits; a truncated JSON string is unreadable.
                foreach (var key in limits.OrderByDescending(kv => kv.Value.Length).Select(kv => kv.Key).ToList())
                {
                    limits.Remove(key);
                    string candidate = JsonSerializer.Serialize(limits);
                    if (candidate.Length <= MaxRateLimitJsonLength)
                    {
                        rateLimitJson = limits.Count > 0 ? candidate : null;
                        break;
                    }
                }
            }
        }

        return new ProviderResponseHeaders(Truncate(requestId, 160), processingMs, rateLimitJson);
    }

    private static string? First(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

    private static string? Truncate(string? value, int max) =>
        value == null || value.Length <= max ? value : value[..max];
}
