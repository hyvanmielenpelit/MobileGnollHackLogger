using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MobileGnollHackLogger.Data;

namespace Overseer.Services;

public enum ApiKeyVerdict { Valid, ValidWithWarning, Invalid, Unverifiable }

public sealed record ApiKeyValidationResult(ApiKeyVerdict Verdict, string Message, ApiKeyCheckDetail? Detail);

/// <summary>What was called and what came back; never the key.</summary>
/// <param name="Request">The method and URL, e.g. "GET https://api.anthropic.com/v1/models".</param>
/// <param name="HttpStatus">The response status code; null when no response arrived.</param>
/// <param name="HttpReason">The status code's reason phrase, e.g. "Service Unavailable".</param>
/// <param name="ProviderError">The provider's own error message, one line, at most 500 characters, key redacted.</param>
/// <param name="Exception">Why no response arrived, key redacted.</param>
/// <param name="ElapsedMs">Time from sending the request to the verdict.</param>
public sealed record ApiKeyCheckDetail(
    string Request, int? HttpStatus, string? HttpReason, string? ProviderError, string? Exception, double ElapsedMs)
{
    /// <summary>
    /// One paragraph for the UI and the stored verification message, e.g.
    /// <c>GET https://api.openai.com/v1/models → HTTP 503 Service Unavailable after 2.1 s. OpenAI said: "The server is overloaded."</c>
    /// </summary>
    public string ToText()
    {
        var sb = new StringBuilder(Request);
        string seconds = (ElapsedMs / 1000.0).ToString("0.0", CultureInfo.InvariantCulture);

        if (HttpStatus.HasValue)
        {
            sb.Append(" → HTTP ").Append(HttpStatus.Value.ToString(CultureInfo.InvariantCulture));
            if (!string.IsNullOrWhiteSpace(HttpReason))
                sb.Append(' ').Append(HttpReason);
            sb.Append(" after ").Append(seconds).Append(" s.");
        }
        else
        {
            sb.Append(" → no response after ").Append(seconds).Append(" s.");
        }

        if (!string.IsNullOrWhiteSpace(Exception))
            sb.Append(' ').Append(Exception);

        if (!string.IsNullOrWhiteSpace(ProviderError))
            sb.Append(' ').Append(ProviderNameOf(Request)).Append(" said: \"").Append(ProviderError).Append('"');

        return sb.ToString();
    }

    /// <summary>The <see cref="ToText"/> paragraph, serialized as "text".</summary>
    public string Text => ToText();

    private static string ProviderNameOf(string request)
    {
        if (request.Contains("api.anthropic.com", StringComparison.OrdinalIgnoreCase)) return "Anthropic";
        if (request.Contains("googleapis.com", StringComparison.OrdinalIgnoreCase)) return "Google";
        if (request.Contains("api.openai.com", StringComparison.OrdinalIgnoreCase)) return "OpenAI";
        return "The provider";
    }
}

public interface IApiKeyValidator
{
    /// <summary>
    /// Checks a key against its provider's official list-models endpoint. Throws
    /// <see cref="OperationCanceledException"/> only when <paramref name="ct"/> is canceled.
    /// </summary>
    Task<ApiKeyValidationResult> ValidateAsync(string provider, string apiKey, CancellationToken ct);
}

/// <summary>
/// Verifies an API key with one call to the provider's official list-models endpoint. The key
/// travels only in a request header; it never appears in a URL, a log line or a returned detail.
/// </summary>
public sealed class ApiKeyValidator : IApiKeyValidator
{
    public const string HttpClientName = "ApiKeyValidation";

    private const int MaxProviderErrorLength = 500;
    private const int MinRedactedRunLength = 8;
    private const string Redacted = "[key]";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ApiKeyValidator> _logger;

    public ApiKeyValidator(IHttpClientFactory httpClientFactory, ILogger<ApiKeyValidator> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<ApiKeyValidationResult> ValidateAsync(string provider, string apiKey, CancellationToken ct)
    {
        string? name = CanonicalProvider(provider);
        if (name == null)
            return new ApiKeyValidationResult(ApiKeyVerdict.Invalid, $"Unknown provider \"{provider}\".", null);

        if (string.IsNullOrWhiteSpace(apiKey))
            return new ApiKeyValidationResult(ApiKeyVerdict.Invalid, "No API key was given.", null);

        string url = name switch
        {
            "Anthropic" => "https://api.anthropic.com/v1/models",
            "Google" => "https://generativelanguage.googleapis.com/v1beta/models",
            _ => "https://api.openai.com/v1/models"
        };
        string requestText = "GET " + url;

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        switch (name)
        {
            case "Anthropic":
                request.Headers.TryAddWithoutValidation("x-api-key", apiKey);
                request.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
                break;
            case "Google":
                request.Headers.TryAddWithoutValidation("x-goog-api-key", apiKey);
                break;
            default:
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                break;
        }

        ct.ThrowIfCancellationRequested();
        var client = _httpClientFactory.CreateClient(HttpClientName);
        var stopwatch = Stopwatch.StartNew();

        int status;
        string? reason;
        string body;
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct);
            status = (int)response.StatusCode;
            reason = response.ReasonPhrase;
            body = await response.Content.ReadAsStringAsync(ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            stopwatch.Stop();
            string timeout = client.Timeout == Timeout.InfiniteTimeSpan
                ? "The request was canceled before a response arrived."
                : $"The request timed out after {client.Timeout.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture)} seconds.";
            return NoResponse(name, requestText, timeout, stopwatch.Elapsed.TotalMilliseconds, "timeout");
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            string message = Truncate(Redact(OneLine(ex.Message), apiKey));
            return NoResponse(name, requestText, message, stopwatch.Elapsed.TotalMilliseconds, nameof(HttpRequestException));
        }
        stopwatch.Stop();
        double elapsedMs = stopwatch.Elapsed.TotalMilliseconds;

        ApiKeyValidationResult result;
        if (status >= 200 && status < 300)
        {
            result = new ApiKeyValidationResult(ApiKeyVerdict.Valid, $"{name} accepted this key.", null);
        }
        else
        {
            var detail = new ApiKeyCheckDetail(
                requestText, status, reason, ExtractProviderError(body, apiKey), null, elapsedMs);

            if (status == (int)HttpStatusCode.Unauthorized || status == (int)HttpStatusCode.Forbidden
                || (name == "Google" && status == (int)HttpStatusCode.BadRequest && IsGoogleInvalidKeyBody(body)))
            {
                result = new ApiKeyValidationResult(ApiKeyVerdict.Invalid,
                    $"{name} rejected this key: it does not exist, has been revoked or has expired.", detail);
            }
            else if (name == "OpenAI" && body.Contains("insufficient_quota", StringComparison.OrdinalIgnoreCase))
            {
                result = new ApiKeyValidationResult(ApiKeyVerdict.ValidWithWarning,
                    $"{name} accepted this key but reports that the account has no credit left.", detail);
            }
            else if (status == (int)HttpStatusCode.TooManyRequests)
            {
                result = new ApiKeyValidationResult(ApiKeyVerdict.ValidWithWarning,
                    $"{name} accepted this key but is rate-limiting it right now.", detail);
            }
            else
            {
                result = new ApiKeyValidationResult(ApiKeyVerdict.Unverifiable, $"{name} could not confirm this key.", detail);
            }
        }

        _logger.LogInformation("API key check with {Provider}: HTTP {StatusCode}, verdict {Verdict}.",
            name, status, result.Verdict);
        return result;
    }

    /// <summary>The verdict when no response arrived.</summary>
    private ApiKeyValidationResult NoResponse(
        string name, string requestText, string exception, double elapsedMs, string failureKind)
    {
        _logger.LogWarning("API key check with {Provider}: no response ({FailureKind}), verdict {Verdict}.",
            name, failureKind, ApiKeyVerdict.Unverifiable);
        return new ApiKeyValidationResult(ApiKeyVerdict.Unverifiable, $"{name} could not confirm this key.",
            new ApiKeyCheckDetail(requestText, null, null, null, exception, elapsedMs));
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

    private static bool IsGoogleInvalidKeyBody(string body) =>
        body.Contains("API_KEY_INVALID", StringComparison.OrdinalIgnoreCase)
        || body.Contains("expired", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The provider's error message: <c>error.message</c> from a JSON body (also inside a one-element
    /// array, as Google sometimes answers), otherwise the raw body. One line, key redacted, at most
    /// 500 characters; null for an empty body.
    /// </summary>
    internal static string? ExtractProviderError(string? body, string apiKey)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;

        string? message = null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
                root = root[0];

            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("error", out var error))
                {
                    if (error.ValueKind == JsonValueKind.Object
                        && error.TryGetProperty("message", out var inner) && inner.ValueKind == JsonValueKind.String)
                        message = inner.GetString();
                    else if (error.ValueKind == JsonValueKind.String)
                        message = error.GetString();
                }
                else if (root.TryGetProperty("message", out var top) && top.ValueKind == JsonValueKind.String)
                {
                    message = top.GetString();
                }
            }
        }
        catch (JsonException)
        {
            // Not JSON: the raw body is the message.
        }

        string text = OneLine(string.IsNullOrWhiteSpace(message) ? body : message!);
        if (text.Length == 0) return null;
        return Truncate(Redact(text, apiKey));
    }

    /// <summary>
    /// Replaces the key, and every run of 8 or more characters that also occurs in the key, with
    /// "[key]", ignoring case. A key shorter than 8 characters is replaced only where it occurs whole.
    /// </summary>
    internal static string Redact(string text, string apiKey)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(apiKey)) return text;

        if (apiKey.Length < MinRedactedRunLength)
            return text.Replace(apiKey, Redacted, StringComparison.OrdinalIgnoreCase);

        string lowerKey = apiKey.ToLowerInvariant();
        string lowerText = text.ToLowerInvariant();

        var keyRuns = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i + MinRedactedRunLength <= lowerKey.Length; i++)
            keyRuns.Add(lowerKey.Substring(i, MinRedactedRunLength));

        // Every 8-character window found in the key marks its characters; a run of any length is a chain of such windows.
        var covered = new bool[text.Length];
        bool any = false;
        for (int i = 0; i + MinRedactedRunLength <= lowerText.Length; i++)
        {
            if (!keyRuns.Contains(lowerText.Substring(i, MinRedactedRunLength))) continue;
            for (int j = i; j < i + MinRedactedRunLength; j++) covered[j] = true;
            any = true;
        }
        if (!any) return text;

        var sb = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            if (!covered[i])
            {
                sb.Append(text[i]);
                continue;
            }
            sb.Append(Redacted);
            while (i + 1 < text.Length && covered[i + 1]) i++;
        }
        return sb.ToString();
    }

    private static string OneLine(string text)
    {
        var sb = new StringBuilder(text.Length);
        bool pendingSpace = false;
        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = sb.Length > 0;
                continue;
            }
            if (pendingSpace) sb.Append(' ');
            pendingSpace = false;
            sb.Append(c);
        }
        return sb.ToString();
    }

    private static string Truncate(string text) =>
        text.Length <= MaxProviderErrorLength ? text : text[..(MaxProviderErrorLength - 1)] + "…";
}

/// <summary>The verification state of a stored key as the API returns it.</summary>
public sealed class ApiKeyVerificationDto
{
    /// <summary>"Verified", "NotVerified", or null when the key has never been checked.</summary>
    public string? Status { get; init; }
    public DateTime? CheckedAtUtc { get; init; }
    public string? Message { get; init; }

    public static ApiKeyVerificationDto From(ApiKeyVerificationStatus? status, DateTime? checkedAtUtc, string? message) =>
        new()
        {
            Status = status?.ToString(),
            CheckedAtUtc = checkedAtUtc,
            Message = message
        };
}

/// <summary>Body of a 400 (invalid) or 409 (unverifiable) key-save answer.</summary>
public sealed class ApiKeyRefusalDto
{
    /// <summary>"invalid" or "unverifiable".</summary>
    public string Verdict { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public ApiKeyCheckDetail? Detail { get; init; }

    public static ApiKeyRefusalDto From(ApiKeyValidationResult result) =>
        new()
        {
            Verdict = result.Verdict == ApiKeyVerdict.Invalid ? "invalid" : "unverifiable",
            Message = result.Message,
            Detail = result.Detail
        };
}
