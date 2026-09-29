namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MobileGnollHackLogger.Data;
using Overseer.Services;
using Xunit;

/// <summary>
/// The API key check: which endpoint and header each provider gets, how a provider's answer maps to
/// a verdict, what the failure detail carries, and that the key never comes back in it. Every call
/// goes to a fake handler; none reaches a provider.
/// </summary>
public class ApiKeyValidatorTests
{
    private const string TestKey = "test-key-not-real-0001-abcdefghij";

    // ---- Endpoints and credentials ----

    [Fact]
    public async Task Anthropic_CallsOfficialModelsEndpointWithApiKeyHeaders()
    {
        var (validator, handler, factory, _) = Create(_ => Json(HttpStatusCode.OK, "{\"data\":[]}"));

        var result = await validator.ValidateAsync("Anthropic", TestKey, CancellationToken.None);

        Assert.Equal(ApiKeyVerdict.Valid, result.Verdict);
        Assert.Equal("ApiKeyValidation", Assert.Single(factory.ClientNames));
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https://api.anthropic.com/v1/models", request.Uri);
        Assert.Equal(TestKey, request.Header("x-api-key"));
        Assert.Equal("2023-06-01", request.Header("anthropic-version"));
        Assert.Null(request.Header("Authorization"));
    }

    [Fact]
    public async Task Google_SendsKeyInHeaderNeverInUrl()
    {
        var (validator, handler, _, _) = Create(_ => Json(HttpStatusCode.OK, "{\"models\":[]}"));

        var result = await validator.ValidateAsync("Google", TestKey, CancellationToken.None);

        Assert.Equal(ApiKeyVerdict.Valid, result.Verdict);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://generativelanguage.googleapis.com/v1beta/models", request.Uri);
        Assert.DoesNotContain("key=", request.Uri, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(TestKey, request.Uri);
        Assert.Equal(TestKey, request.Header("x-goog-api-key"));
        Assert.Null(request.Header("Authorization"));
    }

    [Fact]
    public async Task OpenAI_SendsBearerToken()
    {
        var (validator, handler, _, _) = Create(_ => Json(HttpStatusCode.OK, "{\"data\":[]}"));

        var result = await validator.ValidateAsync("OpenAI", TestKey, CancellationToken.None);

        Assert.Equal(ApiKeyVerdict.Valid, result.Verdict);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://api.openai.com/v1/models", request.Uri);
        Assert.Equal("Bearer " + TestKey, request.Header("Authorization"));
    }

    [Fact]
    public async Task ProviderName_IsMatchedIgnoringCase_AndReportedCanonically()
    {
        var (validator, handler, _, _) = Create(_ => Json(HttpStatusCode.OK, "{\"data\":[]}"));

        var result = await validator.ValidateAsync("openai", TestKey, CancellationToken.None);

        Assert.Equal(ApiKeyVerdict.Valid, result.Verdict);
        Assert.Equal("OpenAI accepted this key.", result.Message);
        Assert.Null(result.Detail);
        Assert.Equal("https://api.openai.com/v1/models", Assert.Single(handler.Requests).Uri);
    }

    [Fact]
    public async Task UnknownProvider_IsInvalid_WithoutCallingAnything()
    {
        var (validator, handler, _, _) = Create(_ => Json(HttpStatusCode.OK, "{}"));

        var result = await validator.ValidateAsync("Mistral", TestKey, CancellationToken.None);

        Assert.Equal(ApiKeyVerdict.Invalid, result.Verdict);
        Assert.Empty(handler.Requests);
    }

    // ---- Rejected keys ----

    [Fact]
    public async Task Status401_IsInvalid_WithParsedProviderMessage()
    {
        var (validator, _, _, _) = Create(_ => Json(HttpStatusCode.Unauthorized,
            "{\"type\":\"error\",\"error\":{\"type\":\"authentication_error\",\"message\":\"invalid x-api-key\"}}"));

        var result = await validator.ValidateAsync("Anthropic", TestKey, CancellationToken.None);

        Assert.Equal(ApiKeyVerdict.Invalid, result.Verdict);
        Assert.Equal("Anthropic rejected this key: it does not exist, has been revoked or has expired.", result.Message);
        Assert.NotNull(result.Detail);
        Assert.Equal(401, result.Detail!.HttpStatus);
        Assert.Equal("invalid x-api-key", result.Detail.ProviderError);
        Assert.Equal("GET https://api.anthropic.com/v1/models", result.Detail.Request);
    }

    [Fact]
    public async Task Status403_IsInvalid_WithParsedProviderMessage()
    {
        var (validator, _, _, _) = Create(_ => Json(HttpStatusCode.Forbidden,
            "{\"error\":{\"code\":403,\"message\":\"Permission denied: the key has been revoked.\",\"status\":\"PERMISSION_DENIED\"}}"));

        var result = await validator.ValidateAsync("Google", TestKey, CancellationToken.None);

        Assert.Equal(ApiKeyVerdict.Invalid, result.Verdict);
        Assert.Equal(403, result.Detail!.HttpStatus);
        Assert.Equal("Permission denied: the key has been revoked.", result.Detail.ProviderError);
    }

    [Fact]
    public async Task GoogleInvalidKey400_IsInvalid_WithParsedProviderMessage()
    {
        var (validator, _, _, _) = Create(_ => Json(HttpStatusCode.BadRequest,
            "{\"error\":{\"code\":400,\"message\":\"API key not valid. Please pass a valid API key.\",\"status\":\"INVALID_ARGUMENT\","
            + "\"details\":[{\"@type\":\"type.googleapis.com/google.rpc.ErrorInfo\",\"reason\":\"API_KEY_INVALID\",\"domain\":\"googleapis.com\"}]}}"));

        var result = await validator.ValidateAsync("Google", TestKey, CancellationToken.None);

        Assert.Equal(ApiKeyVerdict.Invalid, result.Verdict);
        Assert.Equal("Google rejected this key: it does not exist, has been revoked or has expired.", result.Message);
        Assert.Equal(400, result.Detail!.HttpStatus);
        Assert.Equal("API key not valid. Please pass a valid API key.", result.Detail.ProviderError);
    }

    [Fact]
    public async Task GoogleExpiredKey400_IsInvalid()
    {
        var (validator, _, _, _) = Create(_ => Json(HttpStatusCode.BadRequest,
            "{\"error\":{\"code\":400,\"message\":\"API key expired. Please renew the API key.\",\"status\":\"INVALID_ARGUMENT\"}}"));

        var result = await validator.ValidateAsync("Google", TestKey, CancellationToken.None);

        Assert.Equal(ApiKeyVerdict.Invalid, result.Verdict);
    }

    [Fact]
    public async Task Google400_WithoutKeyReason_IsUnverifiable()
    {
        var (validator, _, _, _) = Create(_ => Json(HttpStatusCode.BadRequest,
            "{\"error\":{\"code\":400,\"message\":\"Request contains an invalid argument.\",\"status\":\"INVALID_ARGUMENT\"}}"));

        var result = await validator.ValidateAsync("Google", TestKey, CancellationToken.None);

        Assert.Equal(ApiKeyVerdict.Unverifiable, result.Verdict);
        Assert.Equal(400, result.Detail!.HttpStatus);
    }

    // ---- Accepted with a warning ----

    [Fact]
    public async Task Status429_IsValidWithWarning()
    {
        var (validator, _, _, _) = Create(_ => Json(HttpStatusCode.TooManyRequests,
            "{\"type\":\"error\",\"error\":{\"type\":\"rate_limit_error\",\"message\":\"Number of requests has exceeded your rate limit.\"}}"));

        var result = await validator.ValidateAsync("Anthropic", TestKey, CancellationToken.None);

        Assert.Equal(ApiKeyVerdict.ValidWithWarning, result.Verdict);
        Assert.Equal("Anthropic accepted this key but is rate-limiting it right now.", result.Message);
        Assert.Equal(429, result.Detail!.HttpStatus);
        Assert.Equal("Number of requests has exceeded your rate limit.", result.Detail.ProviderError);
    }

    [Fact]
    public async Task OpenAIInsufficientQuota_IsValidWithWarning()
    {
        var (validator, _, _, _) = Create(_ => Json(HttpStatusCode.TooManyRequests,
            "{\"error\":{\"message\":\"You exceeded your current quota, please check your plan and billing details.\","
            + "\"type\":\"insufficient_quota\",\"param\":null,\"code\":\"insufficient_quota\"}}"));

        var result = await validator.ValidateAsync("OpenAI", TestKey, CancellationToken.None);

        Assert.Equal(ApiKeyVerdict.ValidWithWarning, result.Verdict);
        Assert.Equal("OpenAI accepted this key but reports that the account has no credit left.", result.Message);
        Assert.Equal("You exceeded your current quota, please check your plan and billing details.", result.Detail!.ProviderError);
    }

    // ---- Unverifiable ----

    [Fact]
    public async Task Status503_IsUnverifiable_WithStatusAndProviderMessage()
    {
        var (validator, _, _, _) = Create(_ => Json(HttpStatusCode.ServiceUnavailable,
            "{\"error\":{\"message\":\"The server is overloaded.\",\"type\":\"server_error\"}}"));

        var result = await validator.ValidateAsync("OpenAI", TestKey, CancellationToken.None);

        Assert.Equal(ApiKeyVerdict.Unverifiable, result.Verdict);
        Assert.Equal("OpenAI could not confirm this key.", result.Message);
        Assert.Equal(503, result.Detail!.HttpStatus);
        Assert.Equal("Service Unavailable", result.Detail.HttpReason);
        Assert.Equal("The server is overloaded.", result.Detail.ProviderError);
        Assert.Null(result.Detail.Exception);
        Assert.StartsWith("GET https://api.openai.com/v1/models → HTTP 503 Service Unavailable after ", result.Detail.ToText());
        Assert.EndsWith(" s. OpenAI said: \"The server is overloaded.\"", result.Detail.ToText());
    }

    [Fact]
    public async Task Timeout_IsUnverifiable_WithTimeoutMessage()
    {
        var (validator, _, _, _) = Create(_ => throw new TaskCanceledException(
            "The request was canceled due to the configured HttpClient.Timeout.", new TimeoutException()));

        var result = await validator.ValidateAsync("Anthropic", TestKey, CancellationToken.None);

        Assert.Equal(ApiKeyVerdict.Unverifiable, result.Verdict);
        Assert.Equal("Anthropic could not confirm this key.", result.Message);
        Assert.Null(result.Detail!.HttpStatus);
        Assert.Equal("The request timed out after 15 seconds.", result.Detail.Exception);
    }

    [Fact]
    public async Task ClientTimeout_IsUnverifiable_NotThrown()
    {
        var (validator, _, _, _) = Create(
            async (_, token) =>
            {
                await Task.Delay(Timeout.Infinite, token);
                return Json(HttpStatusCode.OK, "{}");
            },
            TimeSpan.FromMilliseconds(100));

        var result = await validator.ValidateAsync("Google", TestKey, CancellationToken.None);

        Assert.Equal(ApiKeyVerdict.Unverifiable, result.Verdict);
        Assert.StartsWith("The request timed out after ", result.Detail!.Exception);
    }

    [Fact]
    public async Task HttpRequestException_IsUnverifiable_WithExceptionMessage()
    {
        var (validator, _, _, _) = Create(_ => throw new HttpRequestException("No such host is known. (api.openai.com:443)"));

        var result = await validator.ValidateAsync("OpenAI", TestKey, CancellationToken.None);

        Assert.Equal(ApiKeyVerdict.Unverifiable, result.Verdict);
        Assert.Null(result.Detail!.HttpStatus);
        Assert.Equal("No such host is known. (api.openai.com:443)", result.Detail.Exception);
        Assert.Contains("No such host is known.", result.Detail.ToText());
    }

    [Fact]
    public async Task CallerCancellation_Propagates()
    {
        using var cts = new CancellationTokenSource();
        var (validator, _, _, _) = Create(
            async (_, token) =>
            {
                cts.Cancel();
                await Task.Delay(Timeout.Infinite, token);
                return Json(HttpStatusCode.OK, "{}");
            },
            TimeSpan.FromSeconds(15));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => validator.ValidateAsync("OpenAI", TestKey, cts.Token));
    }

    // ---- The provider message ----

    [Fact]
    public async Task NonJsonBody_IsReturnedRaw_OnOneLine()
    {
        var (validator, _, _, _) = Create(_ => Text(HttpStatusCode.BadGateway, "<html>\r\n  Bad   gateway\n</html>"));

        var result = await validator.ValidateAsync("OpenAI", TestKey, CancellationToken.None);

        Assert.Equal(ApiKeyVerdict.Unverifiable, result.Verdict);
        Assert.Equal("<html> Bad gateway </html>", result.Detail!.ProviderError);
    }

    [Fact]
    public async Task LongBody_IsCutTo500Characters()
    {
        var (validator, _, _, _) = Create(_ => Text(HttpStatusCode.InternalServerError, new string('x', 2000)));

        var result = await validator.ValidateAsync("OpenAI", TestKey, CancellationToken.None);

        Assert.Equal(500, result.Detail!.ProviderError!.Length);
    }

    [Fact]
    public async Task BodyEchoingWholeKey_IsRedacted()
    {
        var (validator, _, _, _) = Create(_ => Json(HttpStatusCode.Unauthorized,
            "{\"error\":{\"message\":\"Incorrect API key provided: " + TestKey + ". Check it and try again.\"}}"));

        var result = await validator.ValidateAsync("OpenAI", TestKey, CancellationToken.None);

        Assert.Equal("Incorrect API key provided: [key]. Check it and try again.", result.Detail!.ProviderError);
        AssertNoKeyRun(result.Detail.ToText());
    }

    [Fact]
    public async Task BodyEchoingSixteenCharacterRunOfKey_IsRedacted()
    {
        string run = TestKey.Substring(9, 16);
        var (validator, _, _, _) = Create(_ => Json(HttpStatusCode.Unauthorized,
            "{\"error\":{\"message\":\"Incorrect API key provided: ****" + run + "****.\"}}"));

        var result = await validator.ValidateAsync("OpenAI", TestKey, CancellationToken.None);

        Assert.Equal("Incorrect API key provided: ****[key]****.", result.Detail!.ProviderError);
        AssertNoKeyRun(result.Detail.ToText());
    }

    [Fact]
    public async Task NonJsonBodyEchoingKey_IsRedacted()
    {
        var (validator, _, _, _) = Create(_ => Text(HttpStatusCode.ServiceUnavailable, "upstream refused " + TestKey));

        var result = await validator.ValidateAsync("Anthropic", TestKey, CancellationToken.None);

        Assert.Equal("upstream refused [key]", result.Detail!.ProviderError);
    }

    [Fact]
    public async Task ExceptionEchoingKey_IsRedacted()
    {
        var (validator, _, _, _) = Create(_ => throw new HttpRequestException("Connection refused while sending " + TestKey));

        var result = await validator.ValidateAsync("OpenAI", TestKey, CancellationToken.None);

        Assert.Equal("Connection refused while sending [key]", result.Detail!.Exception);
    }

    [Fact]
    public async Task Logs_NeverContainKeyOrBody()
    {
        const string bodyMarker = "body-marker-that-must-not-be-logged";
        var (validator, _, _, logger) = Create(_ => Json(HttpStatusCode.Unauthorized,
            "{\"error\":{\"message\":\"" + bodyMarker + " " + TestKey + "\"}}"));

        await validator.ValidateAsync("OpenAI", TestKey, CancellationToken.None);

        var entry = Assert.Single(logger.Entries);
        Assert.Contains("OpenAI", entry);
        Assert.Contains("401", entry);
        Assert.Contains("Invalid", entry);
        Assert.DoesNotContain(TestKey, entry);
        Assert.DoesNotContain(bodyMarker, entry);
    }

    // ---- Detail text and DTOs ----

    [Fact]
    public void ToText_WithStatusAndProviderError()
    {
        var detail = new ApiKeyCheckDetail(
            "GET https://api.openai.com/v1/models", 503, "Service Unavailable", "The server is overloaded.", null, 2100);

        Assert.Equal(
            "GET https://api.openai.com/v1/models → HTTP 503 Service Unavailable after 2.1 s. OpenAI said: \"The server is overloaded.\"",
            detail.ToText());
        Assert.Equal(detail.ToText(), detail.Text);
    }

    [Fact]
    public void ToText_WithoutResponse()
    {
        var detail = new ApiKeyCheckDetail(
            "GET https://api.anthropic.com/v1/models", null, null, null, "The request timed out after 15 seconds.", 15000);

        Assert.Equal(
            "GET https://api.anthropic.com/v1/models → no response after 15.0 s. The request timed out after 15 seconds.",
            detail.ToText());
    }

    [Fact]
    public void ToText_NamesGoogle()
    {
        var detail = new ApiKeyCheckDetail(
            "GET https://generativelanguage.googleapis.com/v1beta/models", 400, "Bad Request", "API key not valid.", null, 400);

        Assert.Equal(
            "GET https://generativelanguage.googleapis.com/v1beta/models → HTTP 400 Bad Request after 0.4 s. Google said: \"API key not valid.\"",
            detail.ToText());
    }

    [Fact]
    public void Detail_SerializesItsText()
    {
        var detail = new ApiKeyCheckDetail("GET https://api.openai.com/v1/models", 503, "Service Unavailable", null, null, 1000);

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(detail, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        Assert.Equal(detail.ToText(), doc.RootElement.GetProperty("text").GetString());
        Assert.Equal(503, doc.RootElement.GetProperty("httpStatus").GetInt32());
    }

    [Fact]
    public void RefusalDto_MapsVerdict()
    {
        var detail = new ApiKeyCheckDetail("GET https://api.openai.com/v1/models", 401, "Unauthorized", null, null, 100);

        var invalid = ApiKeyRefusalDto.From(new ApiKeyValidationResult(ApiKeyVerdict.Invalid, "rejected", detail));
        var unverifiable = ApiKeyRefusalDto.From(new ApiKeyValidationResult(ApiKeyVerdict.Unverifiable, "unconfirmed", null));

        Assert.Equal("invalid", invalid.Verdict);
        Assert.Equal("rejected", invalid.Message);
        Assert.Same(detail, invalid.Detail);
        Assert.Equal("unverifiable", unverifiable.Verdict);
        Assert.Null(unverifiable.Detail);
    }

    [Fact]
    public void VerificationDto_MapsStatus()
    {
        var at = new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);

        var verified = ApiKeyVerificationDto.From(ApiKeyVerificationStatus.Verified, at, null);
        var notVerified = ApiKeyVerificationDto.From(ApiKeyVerificationStatus.NotVerified, at, "detail");
        var never = ApiKeyVerificationDto.From(null, null, null);

        Assert.Equal("Verified", verified.Status);
        Assert.Equal(at, verified.CheckedAtUtc);
        Assert.Equal("NotVerified", notVerified.Status);
        Assert.Equal("detail", notVerified.Message);
        Assert.Null(never.Status);
        Assert.Null(never.CheckedAtUtc);
    }

    // ---- Helpers ----

    private static void AssertNoKeyRun(string text)
    {
        for (int i = 0; i + 8 <= TestKey.Length; i++)
            Assert.DoesNotContain(TestKey.Substring(i, 8), text, StringComparison.OrdinalIgnoreCase);
    }

    private static (ApiKeyValidator Validator, FakeHandler Handler, FakeHttpClientFactory Factory, RecordingLogger Logger) Create(
        Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        Create((request, _) => Task.FromResult(respond(request)), TimeSpan.FromSeconds(15));

    private static (ApiKeyValidator Validator, FakeHandler Handler, FakeHttpClientFactory Factory, RecordingLogger Logger) Create(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond, TimeSpan timeout)
    {
        var handler = new FakeHandler(respond);
        var factory = new FakeHttpClientFactory(handler, timeout);
        var logger = new RecordingLogger();
        return (new ApiKeyValidator(factory, logger), handler, factory, logger);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Text(HttpStatusCode status, string text) =>
        new(status) { Content = new StringContent(text, Encoding.UTF8, "text/html") };

    private sealed record RecordedRequest(HttpMethod Method, string Uri, Dictionary<string, string> Headers)
    {
        public string? Header(string name) =>
            Headers.TryGetValue(name, out var value) ? value : null;
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _respond;

        public FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) => _respond = respond;

        public List<RecordedRequest> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var headers = request.Headers.ToDictionary(
                h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
            Requests.Add(new RecordedRequest(request.Method, request.RequestUri!.ToString(), headers));
            return _respond(request, cancellationToken);
        }
    }

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        private readonly TimeSpan _timeout;

        public FakeHttpClientFactory(HttpMessageHandler handler, TimeSpan timeout)
        {
            _handler = handler;
            _timeout = timeout;
        }

        public List<string> ClientNames { get; } = new();

        public HttpClient CreateClient(string name)
        {
            ClientNames.Add(name);
            return new HttpClient(_handler, disposeHandler: false) { Timeout = _timeout };
        }
    }

    private sealed class RecordingLogger : ILogger<ApiKeyValidator>
    {
        public List<string> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Entries.Add(formatter(state, exception) + (exception == null ? string.Empty : " " + exception));
        }
    }
}
