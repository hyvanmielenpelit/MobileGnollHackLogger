using System.Security.Cryptography;
using System.Text;

namespace Overseer.Services.ApiKeyAlerts;

/// <summary>
/// One failed provider response, already redacted, carrying everything the alert email needs
/// except what the dispatcher looks up. Holds no key: <see cref="Create"/> reads the key only to
/// derive the fingerprint, the hint and the redactions.
/// </summary>
public sealed record ApiKeyFailureReport
{
    /// <summary>Response headers that may appear in the email; they identify the request and the provider account.</summary>
    public static readonly IReadOnlyList<string> AllowedResponseHeaders = new[]
    {
        "request-id", "x-request-id", "anthropic-organization-id", "openai-organization", "openai-project", "retry-after"
    };

    /// <summary>Upper bound of <see cref="ResponseBodyExcerpt"/> whatever the caller asks for.</summary>
    public const int MaxResponseBodyCharsLimit = 20000;

    private const string FingerprintPrefix = "overseer-api-key-alert:v1:";
    private const int MinKeyLengthForHint = 12;

    public required ApiKeyFailureKind Kind { get; init; }
    public required string Provider { get; init; }
    public required long SystemAiApiConfigurationId { get; init; }

    /// <summary>Lower-case hex SHA-256 of a domain prefix plus the key.</summary>
    public required string KeyFingerprint { get; init; }

    /// <summary>The last four characters of the key, or null for a key shorter than 12 characters.</summary>
    public string? KeyHint { get; init; }

    public required ApiKeyAlertContext Context { get; init; }

    /// <summary>When the provider response arrived.</summary>
    public required DateTime OccurredUtc { get; init; }

    /// <summary>Null for a provider stream error event.</summary>
    public int? HttpStatus { get; init; }
    public string? HttpReason { get; init; }

    /// <summary>The provider's own error message, one line, at most 500 characters, key redacted.</summary>
    public string? ProviderErrorMessage { get; init; }

    /// <summary>The response body, key redacted, cut to the configured length.</summary>
    public string? ResponseBodyExcerpt { get; init; }

    /// <summary>"POST scheme://host/path" with no query, fragment or user info.</summary>
    public string? RequestTarget { get; init; }

    public IReadOnlyList<KeyValuePair<string, string>> ResponseHeaders { get; init; } = Array.Empty<KeyValuePair<string, string>>();
    public double? ElapsedMs { get; init; }
    public int Attempt { get; init; }

    /// <summary>The model id the request was sent with.</summary>
    public string? ModelId { get; init; }
    public string? ServiceTier { get; init; }

    public static ApiKeyFailureReport Create(
        ApiKeyFailureKind kind,
        string provider,
        long systemConfigId,
        string apiKey,
        ApiKeyAlertContext context,
        DateTime occurredUtc,
        int? httpStatus,
        string? httpReason,
        string? body,
        string? requestUri,
        IEnumerable<KeyValuePair<string, IEnumerable<string>>>? responseHeaders,
        double? elapsedMs,
        int attempt,
        string? modelId,
        string? serviceTier,
        int maxResponseBodyChars = 4000)
    {
        ArgumentNullException.ThrowIfNull(apiKey);
        int bodyLimit = Math.Clamp(maxResponseBodyChars, 1, MaxResponseBodyCharsLimit);

        string? excerpt = null;
        if (!string.IsNullOrEmpty(body))
        {
            excerpt = ApiKeyValidator.Redact(body, apiKey);
            if (excerpt.Length > bodyLimit) excerpt = excerpt[..bodyLimit] + "…";
        }

        var headers = new List<KeyValuePair<string, string>>();
        if (responseHeaders != null)
        {
            foreach (var header in responseHeaders)
            {
                if (!AllowedResponseHeaders.Contains(header.Key, StringComparer.OrdinalIgnoreCase)) continue;
                string value = ApiKeyValidator.Redact(string.Join(", ", header.Value), apiKey);
                headers.Add(new KeyValuePair<string, string>(header.Key.ToLowerInvariant(), value));
            }
        }

        return new ApiKeyFailureReport
        {
            Kind = kind,
            Provider = provider,
            SystemAiApiConfigurationId = systemConfigId,
            KeyFingerprint = Fingerprint(apiKey),
            KeyHint = apiKey.Length >= MinKeyLengthForHint ? apiKey[^4..] : null,
            Context = context,
            OccurredUtc = occurredUtc,
            HttpStatus = httpStatus,
            HttpReason = httpReason,
            ProviderErrorMessage = ApiKeyValidator.ExtractProviderError(body, apiKey),
            ResponseBodyExcerpt = excerpt,
            RequestTarget = BuildRequestTarget(requestUri, apiKey),
            ResponseHeaders = headers,
            ElapsedMs = elapsedMs,
            Attempt = attempt,
            ModelId = modelId,
            ServiceTier = serviceTier
        };
    }

    /// <summary>The throttle key of an API key. Never reversible to the key.</summary>
    public static string Fingerprint(string apiKey)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(FingerprintPrefix + apiKey));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Scheme, authority and path of a URL: Google's public endpoint carries the key in the query,
    /// and a custom base URL may carry credentials in its user info. Null when unparseable.
    /// </summary>
    public static string? StripUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)) return null;
        return uri.Scheme + "://" + uri.Authority + uri.AbsolutePath;
    }

    private static string? BuildRequestTarget(string? requestUri, string apiKey)
    {
        string? stripped = StripUrl(requestUri);
        return stripped == null ? null : ApiKeyValidator.Redact("POST " + stripped, apiKey);
    }
}
