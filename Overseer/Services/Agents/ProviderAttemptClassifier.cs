namespace Overseer.Services.Agents;

using System.Net.Http;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MobileGnollHackLogger.Data;

/// <summary>Where in its life an attempt ended.</summary>
public enum AttemptStage
{
    /// <summary>Before the request was sent: a governor refusal or a permit timeout.</summary>
    BeforeSend,

    /// <summary>Sent, waiting for the response headers.</summary>
    AwaitingHeaders,

    /// <summary>Headers received; reading the body or the stream.</summary>
    Streaming,
}

/// <summary>The structured evidence one attempt left, from which its outcome class is decided.</summary>
/// <param name="HttpStatus">The HTTP status of the attempt's response; 2xx for a stream that reported an error event.</param>
/// <param name="ErrorCode">The provider's error <c>code</c> (OpenAI <c>error.code</c>).</param>
/// <param name="ErrorType">The provider's error <c>type</c> (Anthropic and OpenAI <c>error.type</c>).</param>
/// <param name="BodyExcerpt">At most <see cref="ProviderAttemptClassifier.MaxBodyExcerptLength"/> characters of the error body or event text.</param>
/// <param name="RateLimitHeaders">The attempt's own rate-limit headers, lower-case names.</param>
/// <param name="StreamErrorEvent">The 2xx stream carried a provider <c>error</c> event.</param>
/// <param name="StreamErrorStatus">A numeric status inside that event, such as Gemini's <c>[429]</c>.</param>
/// <param name="ErrorStatus">The provider's error <c>status</c> (Google <c>error.status</c>).</param>
/// <param name="ErrorDetailTypes">Google <c>error.details[].@type</c> values.</param>
public sealed record ProviderAttemptEvidence(
    string Provider,
    BenchmarkEndpointKind EndpointKind,
    int? HttpStatus,
    string? ErrorCode,
    string? ErrorType,
    string? BodyExcerpt,
    IReadOnlyDictionary<string, string>? RateLimitHeaders,
    Exception? Exception,
    AttemptStage Stage,
    bool ChunksReceived,
    bool CompletionSeen,
    bool CallerCanceled,
    bool QuestionBudgetCanceled,
    bool GovernorRefused,
    bool ApiKeyBilling,
    bool ApiKeyRejected,
    bool ModelNotFound,
    bool StreamErrorEvent = false,
    int? StreamErrorStatus = null,
    string? ErrorStatus = null,
    IReadOnlyList<string>? ErrorDetailTypes = null);

/// <summary>The error fields a provider's JSON error body or stream error event carried.</summary>
public sealed record ProviderErrorFields(
    string? Type,
    string? Code,
    string? Status,
    int? NumericCode,
    IReadOnlyList<string> DetailTypes)
{
    public static readonly ProviderErrorFields Empty = new(null, null, null, null, Array.Empty<string>());
}

/// <summary>
/// Decides whose fault a provider attempt's outcome was (docs/overseer/ai-benchmark-provider-availability.md).
/// Pure and static. Rules run in a fixed order on structured evidence: our governor, our cancel, the
/// key and billing verdicts of <see cref="ApiKeyAlerts.ApiKeyFailureClassifier"/>, a missing model
/// (<see cref="Providers.ModelNotFoundClassifier"/>), the HTTP status, a stream error event, an
/// exception, and last a 2xx stream. An unknown shape lands in an unattributed class, never in a
/// provider one.
/// </summary>
public static class ProviderAttemptClassifier
{
    /// <summary>The longest provider error code kept on a record.</summary>
    public const int MaxErrorCodeLength = 64;

    /// <summary>The most body or event text held for classification; it is never stored.</summary>
    public const int MaxBodyExcerptLength = 2048;

    /// <summary>The longest error body parsed as JSON; a larger one is matched as text only.</summary>
    private const int MaxParsedBodyLength = 65536;

    public const string GoogleQuotaFailureType = "type.googleapis.com/google.rpc.QuotaFailure";

    /// <summary>The bracketed token a provider's stream error text opens with: <c>stream error: [overloaded_error]</c>, <c>[429]</c>.</summary>
    private static readonly Regex StreamErrorBracketRegex = new(@"stream error:\s*\[(?<token>[^\]\s]{1,64})\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly string[] BillingCodes = { "insufficient_quota", "billing_hard_limit_reached", "billing_error" };

    private static readonly string[] KeyCodes = { "authentication_error", "permission_error", "invalid_api_key", "unauthenticated", "permission_denied" };

    private static readonly string[] RateLimitCodes = { "rate_limit_error", "rate_limit_exceeded", "resource_exhausted" };

    private static readonly string[] OverloadCodes = { "overloaded_error", "overloaded", "unavailable" };

    private static readonly string[] ServerErrorCodes = { "api_error", "server_error", "internal", "internal_error", "deadline_exceeded", "timeout_error" };

    private static readonly string[] RequestCodes =
    {
        "invalid_request_error", "context_length_exceeded", "invalid_argument", "failed_precondition",
        "not_found_error", "not_found", "request_too_large", "invalid_prompt", "out_of_range"
    };

    public static ProviderAttemptOutcome Classify(ProviderAttemptEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        if (evidence.GovernorRefused) return ProviderAttemptOutcome.OurThrottle;
        if (evidence.CallerCanceled || evidence.QuestionBudgetCanceled) return ProviderAttemptOutcome.OurCanceled;
        if (evidence.ApiKeyBilling) return ProviderAttemptOutcome.OurBilling;
        if (evidence.ApiKeyRejected) return ProviderAttemptOutcome.OurKey;
        if (evidence.ModelNotFound) return ProviderAttemptOutcome.OurRequest;

        if (evidence.HttpStatus is int status && !IsSuccess(status))
        {
            return ClassifyStatus(evidence, status);
        }

        if (evidence.StreamErrorEvent)
        {
            return ClassifyStreamError(evidence);
        }

        if (evidence.Exception != null)
        {
            return ClassifyException(evidence);
        }

        if (evidence.HttpStatus is int okStatus && IsSuccess(okStatus))
        {
            return evidence.CompletionSeen ? ProviderAttemptOutcome.Ok : ProviderAttemptOutcome.ProviderStreamFailure;
        }

        // No response, no exception and no cancel: nothing to hold anyone to account for.
        return ProviderAttemptOutcome.TransportUnattributed;
    }

    private static bool IsSuccess(int status) => status is >= 200 and <= 299;

    private static ProviderAttemptOutcome ClassifyStatus(ProviderAttemptEvidence e, int status)
    {
        if (HasAnyCode(e, BillingCodes) || status == 402) return ProviderAttemptOutcome.OurBilling;
        if (status is 401 or 403) return ProviderAttemptOutcome.OurKey;
        if (status == 499 || HasAnyCode(e, "cancelled")) return ProviderAttemptOutcome.OurCanceled;
        if (status == 429) return ClassifyRateLimit(e);
        if (status == 408) return ProviderAttemptOutcome.ProviderTimeout;
        if (status == 529) return ProviderAttemptOutcome.ProviderOverloaded;
        if (status == 503) return IsOpenAiSlowDown(e) ? ProviderAttemptOutcome.OurRateLimit : ProviderAttemptOutcome.ProviderOverloaded;
        if (status is >= 500 and <= 599)
        {
            return IsOverloaded(e) ? ProviderAttemptOutcome.ProviderOverloaded : ProviderAttemptOutcome.ProviderServerError;
        }
        if (status is >= 400 and <= 499) return ProviderAttemptOutcome.OurRequest;

        // A redirect or an informational status the client did not follow: no one's documented failure.
        return ProviderAttemptOutcome.TransportUnattributed;
    }

    private static ProviderAttemptOutcome ClassifyStreamError(ProviderAttemptEvidence e)
    {
        int? s = e.StreamErrorStatus;

        if (HasAnyCode(e, BillingCodes) || s == 402) return ProviderAttemptOutcome.OurBilling;
        if (HasAnyCode(e, KeyCodes) || s is 401 or 403) return ProviderAttemptOutcome.OurKey;
        if (HasAnyCode(e, "cancelled") || s == 499) return ProviderAttemptOutcome.OurCanceled;
        if (HasAnyCode(e, RateLimitCodes) || s == 429) return ClassifyRateLimit(e);
        if (HasAnyCode(e, OverloadCodes) || s is 503 or 529 || IsOverloaded(e))
        {
            return s == 503 && IsOpenAiSlowDown(e) ? ProviderAttemptOutcome.OurRateLimit : ProviderAttemptOutcome.ProviderOverloaded;
        }
        if (HasAnyCode(e, ServerErrorCodes) || s is >= 500 and <= 599) return ProviderAttemptOutcome.ProviderServerError;
        if (s == 408) return ProviderAttemptOutcome.ProviderTimeout;
        if (HasAnyCode(e, RequestCodes) || s is >= 400 and <= 499) return ProviderAttemptOutcome.OurRequest;

        return e.ChunksReceived ? ProviderAttemptOutcome.ProviderStreamFailure : ProviderAttemptOutcome.TransportUnattributed;
    }

    /// <summary>
    /// A 429 is ours when the provider documents it as our limit or our own headers show a spent
    /// limit, and unattributed otherwise: a shared-capacity 429 looks the same on the wire. Every 429
    /// from a custom endpoint is unattributed.
    /// </summary>
    private static ProviderAttemptOutcome ClassifyRateLimit(ProviderAttemptEvidence e)
    {
        if (e.EndpointKind == BenchmarkEndpointKind.Custom) return ProviderAttemptOutcome.RateLimitUnattributed;

        switch (CanonicalProvider(e.Provider))
        {
            case "Anthropic":
                return ProviderAttemptOutcome.OurRateLimit;

            case "OpenAI":
                if (HasAnyCode(e, "rate_limit_exceeded")
                    || HeaderIsZero(e.RateLimitHeaders, "x-ratelimit-remaining-requests")
                    || HeaderIsZero(e.RateLimitHeaders, "x-ratelimit-remaining-tokens"))
                {
                    return ProviderAttemptOutcome.OurRateLimit;
                }
                break;

            case "Google":
                if (HasGoogleQuotaFailure(e)) return ProviderAttemptOutcome.OurRateLimit;
                break;
        }

        return AnyRemainingIsZero(e.RateLimitHeaders) ? ProviderAttemptOutcome.OurRateLimit : ProviderAttemptOutcome.RateLimitUnattributed;
    }

    private static ProviderAttemptOutcome ClassifyException(ProviderAttemptEvidence e)
    {
        var chain = Unwrap(e.Exception!).ToList();
        bool streaming = e.Stage == AttemptStage.Streaming;

        if (IsHttpClientTimeout(e.Exception!))
        {
            return streaming ? ProviderAttemptOutcome.ProviderStreamFailure : ProviderAttemptOutcome.ProviderTimeout;
        }

        foreach (var socket in chain.OfType<SocketException>())
        {
            if (socket.SocketErrorCode is SocketError.HostNotFound or SocketError.NoData or SocketError.TryAgain or SocketError.NetworkUnreachable)
            {
                return ProviderAttemptOutcome.OurNetwork;
            }
        }

        if (chain.OfType<HttpRequestException>().Any(h => h.HttpRequestError == HttpRequestError.NameResolutionError))
        {
            return ProviderAttemptOutcome.OurNetwork;
        }

        if (chain.Any(x => x is SocketException or System.IO.IOException or AuthenticationException or HttpRequestException))
        {
            return ProviderAttemptOutcome.TransportUnattributed;
        }

        if (chain.OfType<OperationCanceledException>().Any())
        {
            return streaming ? ProviderAttemptOutcome.ProviderStreamFailure : ProviderAttemptOutcome.TransportUnattributed;
        }

        // Before a response, anything else was thrown while building or sending our own request.
        return streaming ? ProviderAttemptOutcome.TransportUnattributed : ProviderAttemptOutcome.OurRequest;
    }

    /// <summary>The <c>AiProvider</c> HttpClient's own timeout, as opposed to a token we canceled.</summary>
    public static bool IsHttpClientTimeout(Exception exception) =>
        exception is TaskCanceledException tce
        && (tce.InnerException is TimeoutException || exception.Message.Contains("HttpClient.Timeout", StringComparison.Ordinal));

    private static bool IsOpenAiSlowDown(ProviderAttemptEvidence e) =>
        CanonicalProvider(e.Provider) == "OpenAI"
        && e.BodyExcerpt != null
        && (e.BodyExcerpt.Contains("Slow Down", StringComparison.OrdinalIgnoreCase)
            || e.BodyExcerpt.Contains("sudden increase in your request rate", StringComparison.OrdinalIgnoreCase));

    private static bool IsOverloaded(ProviderAttemptEvidence e) =>
        HasAnyCode(e, OverloadCodes)
        || (e.BodyExcerpt != null && e.BodyExcerpt.Contains("currently overloaded", StringComparison.OrdinalIgnoreCase));

    private static bool HasGoogleQuotaFailure(ProviderAttemptEvidence e) =>
        (e.ErrorDetailTypes?.Any(t => string.Equals(t, GoogleQuotaFailureType, StringComparison.OrdinalIgnoreCase)) ?? false)
        || (e.BodyExcerpt != null && e.BodyExcerpt.Contains("google.rpc.QuotaFailure", StringComparison.OrdinalIgnoreCase));

    private static bool HasAnyCode(ProviderAttemptEvidence e, params string[] codes)
    {
        foreach (string code in codes)
        {
            if (string.Equals(e.ErrorCode, code, StringComparison.OrdinalIgnoreCase)
                || string.Equals(e.ErrorType, code, StringComparison.OrdinalIgnoreCase)
                || string.Equals(e.ErrorStatus, code, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static bool HeaderIsZero(IReadOnlyDictionary<string, string>? headers, string name) =>
        headers != null && headers.TryGetValue(name, out string? value) && IsZero(value);

    /// <summary>Any captured <c>remaining</c> count at zero: <c>x-ratelimit-remaining-*</c>, <c>anthropic-ratelimit-*-remaining</c>.</summary>
    private static bool AnyRemainingIsZero(IReadOnlyDictionary<string, string>? headers)
    {
        if (headers == null) return false;
        foreach (var (name, value) in headers)
        {
            if (name.Contains("remaining", StringComparison.OrdinalIgnoreCase) && IsZero(value)) return true;
        }
        return false;
    }

    private static bool IsZero(string? value) =>
        value != null && long.TryParse(value.Split(',')[0].Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out long n) && n <= 0;

    // --- Evidence helpers ---------------------------------------------------------------------

    /// <summary>
    /// The error fields of a provider's JSON error body: Anthropic and OpenAI <c>{"error":{"type","code"}}</c>,
    /// Google <c>{"error":{"code","status","details":[{"@type"}]}}</c> (also as a one-element array), the
    /// OpenAI Responses stream's <c>{"response":{"error":{...}}}</c> and top-level <c>{"type":"error","code"}</c>.
    /// Null when the text is not such a body.
    /// </summary>
    public static ProviderErrorFields? ParseErrorBody(string? body)
    {
        if (string.IsNullOrWhiteSpace(body) || body.Length > MaxParsedBodyLength) return null;
        string trimmed = body.TrimStart();
        if (trimmed.Length == 0 || (trimmed[0] != '{' && trimmed[0] != '[')) return null;

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Array)
            {
                if (root.GetArrayLength() == 0) return null;
                root = root[0];
            }
            if (root.ValueKind != JsonValueKind.Object) return null;

            JsonElement error;
            bool topLevel = false;
            if (root.TryGetProperty("error", out var e1) && e1.ValueKind == JsonValueKind.Object)
            {
                error = e1;
            }
            else if (root.TryGetProperty("response", out var resp) && resp.ValueKind == JsonValueKind.Object
                && resp.TryGetProperty("error", out var e2) && e2.ValueKind == JsonValueKind.Object)
            {
                error = e2;
            }
            else if (root.TryGetProperty("code", out _) || root.TryGetProperty("status", out _))
            {
                error = root;
                topLevel = true;
            }
            else
            {
                return null;
            }

            string? type = StringOf(error, "type");

            // A top-level event's "type" names the event ("error"), not the failure.
            if (topLevel && type == "error")
            {
                type = null;
            }

            string? code = null;
            int? numeric = null;
            if (error.TryGetProperty("code", out var codeProp))
            {
                if (codeProp.ValueKind == JsonValueKind.String) code = codeProp.GetString();
                else if (codeProp.ValueKind == JsonValueKind.Number && codeProp.TryGetInt32(out int n)) numeric = n;
            }

            string? status = StringOf(error, "status");

            var detailTypes = new List<string>();
            if (error.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array)
            {
                foreach (var d in details.EnumerateArray())
                {
                    if (d.ValueKind == JsonValueKind.Object && StringOf(d, "@type") is string t)
                    {
                        detailTypes.Add(t);
                    }
                }
            }

            return new ProviderErrorFields(type, code, status, numeric, detailTypes);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The error fields of a provider stream <c>error</c> event: the JSON of its detail when it carries
    /// one, completed by the bracketed token its text opens with (<c>[overloaded_error]</c>, <c>[429]</c>).
    /// </summary>
    public static ProviderErrorFields ParseStreamErrorEvent(string? data, string? detail)
    {
        var parsed = ParseErrorBody(detail) ?? ParseErrorBody(data) ?? ProviderErrorFields.Empty;

        if (data != null)
        {
            var match = StreamErrorBracketRegex.Match(data);
            if (match.Success)
            {
                string token = match.Groups["token"].Value;
                if (token.Length == 3 && int.TryParse(token, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int n))
                {
                    if (parsed.NumericCode == null) parsed = parsed with { NumericCode = n };
                }
                else if (parsed.Type == null && parsed.Code == null && parsed.Status == null)
                {
                    parsed = parsed with { Type = token };
                }
            }
        }

        return parsed;
    }

    /// <summary>
    /// The one code a record keeps: Anthropic's <c>type</c>, OpenAI's <c>code</c> (else <c>type</c>),
    /// Google's <c>status</c>; sanitized by <see cref="SanitizeErrorCode"/>.
    /// </summary>
    public static string? PreferredErrorCode(string? provider, string? code, string? type, string? status)
    {
        string? chosen = CanonicalProvider(provider) switch
        {
            "Anthropic" => type ?? code ?? status,
            "Google" => status ?? code ?? type,
            _ => code ?? type ?? status
        };
        return SanitizeErrorCode(chosen);
    }

    /// <summary>Keeps <c>[A-Za-z0-9_.-]</c> only, at most <see cref="MaxErrorCodeLength"/> characters; null when nothing is left.</summary>
    public static string? SanitizeErrorCode(string? code)
    {
        if (string.IsNullOrEmpty(code)) return null;
        var sb = new StringBuilder(Math.Min(code.Length, MaxErrorCodeLength));
        foreach (char c in code)
        {
            if (sb.Length >= MaxErrorCodeLength) break;
            if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' || c == '.' || c == '-')
            {
                sb.Append(c);
            }
        }
        return sb.Length == 0 ? null : sb.ToString();
    }

    /// <summary>The first <see cref="MaxBodyExcerptLength"/> characters of <paramref name="text"/>.</summary>
    public static string? Excerpt(string? text) =>
        text == null || text.Length <= MaxBodyExcerptLength ? text : text.Substring(0, MaxBodyExcerptLength);

    private static string? StringOf(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static IEnumerable<Exception> Unwrap(Exception exception)
    {
        yield return exception;
        if (exception is AggregateException aggregate)
        {
            foreach (var inner in aggregate.InnerExceptions.SelectMany(Unwrap)) yield return inner;
            yield break;
        }
        if (exception.InnerException != null)
        {
            foreach (var inner in Unwrap(exception.InnerException)) yield return inner;
        }
    }

    private static string? CanonicalProvider(string? provider)
    {
        if (string.IsNullOrWhiteSpace(provider)) return null;
        string trimmed = provider.Trim();
        foreach (var name in new[] { "Anthropic", "Google", "OpenAI" })
        {
            if (string.Equals(trimmed, name, StringComparison.OrdinalIgnoreCase)) return name;
        }
        return null;
    }
}
