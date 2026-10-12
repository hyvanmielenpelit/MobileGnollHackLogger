namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Threading.Tasks;
using MobileGnollHackLogger.Data;
using Overseer.Services.Agents;
using Xunit;

/// <summary>
/// <see cref="ProviderAttemptClassifier"/>: every row of the outcome table, the provider-specific 429
/// rule, the custom-endpoint rule, and the evidence helpers the agent loop feeds it with.
/// </summary>
public class ProviderAttemptClassifierTests
{
    private const string AnthropicOverloadedBody =
        "{\"type\":\"error\",\"error\":{\"type\":\"overloaded_error\",\"message\":\"Overloaded\"}}";

    private const string AnthropicRateLimitBody =
        "{\"type\":\"error\",\"error\":{\"type\":\"rate_limit_error\",\"message\":\"This request would exceed your organization's rate limit.\"}}";

    private const string OpenAiRateLimitBody =
        "{\"error\":{\"message\":\"Rate limit reached for requests.\",\"type\":\"requests\",\"code\":\"rate_limit_exceeded\"}}";

    private const string OpenAiSlowDownBody =
        "{\"error\":{\"message\":\"Slow Down: please reduce your request rate. We detected a sudden increase in your request rate.\",\"type\":\"server_error\",\"code\":null}}";

    private const string GoogleQuotaBody =
        "{\"error\":{\"code\":429,\"message\":\"You exceeded your current quota.\",\"status\":\"RESOURCE_EXHAUSTED\",\"details\":["
        + "{\"@type\":\"type.googleapis.com/google.rpc.QuotaFailure\",\"violations\":[{\"quotaMetric\":\"generativelanguage.googleapis.com/generate_content_requests\"}]},"
        + "{\"@type\":\"type.googleapis.com/google.rpc.RetryInfo\",\"retryDelay\":\"20s\"}]}}";

    private const string GoogleBareExhaustedBody =
        "{\"error\":{\"code\":429,\"message\":\"Resource has been exhausted (e.g. check quota).\",\"status\":\"RESOURCE_EXHAUSTED\"}}";

    private static ProviderAttemptEvidence Evidence(
        string provider = "Anthropic",
        BenchmarkEndpointKind endpoint = BenchmarkEndpointKind.Official,
        int? status = null,
        string? body = null,
        IReadOnlyDictionary<string, string>? headers = null,
        Exception? exception = null,
        AttemptStage stage = AttemptStage.Streaming,
        bool chunks = false,
        bool completion = false,
        bool callerCanceled = false,
        bool budget = false,
        bool governor = false,
        bool billing = false,
        bool keyRejected = false,
        bool modelNotFound = false,
        bool streamError = false,
        string? streamData = null)
    {
        ProviderErrorFields? fields = streamError
            ? ProviderAttemptClassifier.ParseStreamErrorEvent(streamData, body)
            : ProviderAttemptClassifier.ParseErrorBody(body);

        return new ProviderAttemptEvidence(
            provider,
            endpoint,
            status,
            fields?.Code,
            fields?.Type,
            ProviderAttemptClassifier.Excerpt(body ?? streamData),
            headers,
            exception,
            stage,
            chunks,
            completion,
            callerCanceled,
            budget,
            governor,
            billing,
            keyRejected,
            modelNotFound,
            StreamErrorEvent: streamError,
            StreamErrorStatus: streamError ? fields?.NumericCode : null,
            ErrorStatus: fields?.Status,
            ErrorDetailTypes: fields?.DetailTypes);
    }

    private static ProviderAttemptOutcome Classify(ProviderAttemptEvidence evidence) => ProviderAttemptClassifier.Classify(evidence);

    // --- Ok and a 2xx stream ------------------------------------------------------------------

    [Theory]
    [InlineData("Anthropic")]
    [InlineData("OpenAI")]
    [InlineData("Google")]
    public void Classify_2xxStreamWithCompletionEvent_IsOk(string provider)
    {
        Assert.Equal(ProviderAttemptOutcome.Ok, Classify(Evidence(provider, status: 200, chunks: true, completion: true)));
    }

    [Theory]
    [InlineData("Anthropic")]
    [InlineData("OpenAI")]
    [InlineData("Google")]
    public void Classify_2xxStreamWithoutCompletionEvent_IsProviderStreamFailure(string provider)
    {
        Assert.Equal(ProviderAttemptOutcome.ProviderStreamFailure, Classify(Evidence(provider, status: 200, chunks: true, completion: false)));
    }

    // --- Provider overload ----------------------------------------------------------------------

    [Theory]
    [InlineData("Anthropic", BenchmarkEndpointKind.Official)]
    [InlineData("OpenAI", BenchmarkEndpointKind.Official)]
    [InlineData("Google", BenchmarkEndpointKind.Official)]
    [InlineData("Anthropic", BenchmarkEndpointKind.Custom)]
    [InlineData("Google", BenchmarkEndpointKind.Custom)]
    public void Classify_Http529_IsProviderOverloaded(string provider, BenchmarkEndpointKind endpoint)
    {
        Assert.Equal(ProviderAttemptOutcome.ProviderOverloaded, Classify(Evidence(provider, endpoint, status: 529, body: AnthropicOverloadedBody)));
    }

    [Theory]
    [InlineData("Anthropic", BenchmarkEndpointKind.Official)]
    [InlineData("Google", BenchmarkEndpointKind.Official)]
    [InlineData("OpenAI", BenchmarkEndpointKind.Official)]
    [InlineData("OpenAI", BenchmarkEndpointKind.Custom)]
    public void Classify_Http503_IsProviderOverloaded(string provider, BenchmarkEndpointKind endpoint)
    {
        string body = provider == "Google"
            ? "{\"error\":{\"code\":503,\"message\":\"The model is overloaded.\",\"status\":\"UNAVAILABLE\"}}"
            : "{\"error\":{\"message\":\"Service Unavailable\"}}";

        Assert.Equal(ProviderAttemptOutcome.ProviderOverloaded, Classify(Evidence(provider, endpoint, status: 503, body: body)));
    }

    [Fact]
    public void Classify_OpenAi500ServersCurrentlyOverloaded_IsProviderOverloaded()
    {
        var evidence = Evidence("OpenAI", status: 500,
            body: "{\"error\":{\"message\":\"That model is currently overloaded with other requests.\",\"type\":\"server_error\"}}");

        Assert.Equal(ProviderAttemptOutcome.ProviderOverloaded, Classify(evidence));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Classify_AnthropicStreamOverloadedError_IsProviderOverloaded_BeforeOrAfterChunks(bool chunks)
    {
        var evidence = Evidence("Anthropic", status: 200, streamError: true, chunks: chunks,
            streamData: "Anthropic stream error: [overloaded_error] Overloaded");

        Assert.Equal(ProviderAttemptOutcome.ProviderOverloaded, Classify(evidence));
    }

    [Fact]
    public void Classify_GoogleStream503_IsProviderOverloaded()
    {
        var evidence = Evidence("Google", status: 200, streamError: true,
            streamData: "Google stream error: [503] This model is currently experiencing high demand.",
            body: "{\"error\":{\"code\":503,\"message\":\"This model is currently experiencing high demand.\",\"status\":\"UNAVAILABLE\"}}");

        Assert.Equal(ProviderAttemptOutcome.ProviderOverloaded, Classify(evidence));
    }

    // --- Provider server error ------------------------------------------------------------------

    [Theory]
    [InlineData("Anthropic", 500)]
    [InlineData("Anthropic", 502)]
    [InlineData("Anthropic", 504)]
    [InlineData("OpenAI", 500)]
    [InlineData("OpenAI", 502)]
    [InlineData("OpenAI", 504)]
    [InlineData("Google", 500)]
    [InlineData("Google", 504)]
    [InlineData("Google", 520)]
    public void Classify_Http5xx_IsProviderServerError(string provider, int status)
    {
        Assert.Equal(ProviderAttemptOutcome.ProviderServerError, Classify(Evidence(provider, status: status, body: "{\"error\":{\"message\":\"boom\"}}")));
    }

    [Theory]
    [InlineData("Anthropic", "Anthropic stream error: [api_error] Internal server error", null)]
    [InlineData("OpenAI", "OpenAI stream error: [server_error] The server had an error.", "{\"type\":\"response.failed\",\"response\":{\"status\":\"failed\",\"error\":{\"code\":\"server_error\",\"message\":\"The server had an error.\"}}}")]
    [InlineData("Google", "Google stream error: [500] Internal error encountered.", "{\"error\":{\"code\":500,\"message\":\"Internal error encountered.\",\"status\":\"INTERNAL\"}}")]
    [InlineData("Google", "Google stream error: [520] Web server returned an unknown error", null)]
    public void Classify_StreamServerError_IsProviderServerError(string provider, string data, string? detail)
    {
        var evidence = Evidence(provider, status: 200, streamError: true, streamData: data, body: detail);

        Assert.Equal(ProviderAttemptOutcome.ProviderServerError, Classify(evidence));
    }

    // --- Provider timeout -----------------------------------------------------------------------

    [Fact]
    public void Classify_HttpClientTimeoutBeforeHeaders_IsProviderTimeout()
    {
        var ex = new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 600 seconds elapsing.", new TimeoutException());

        Assert.Equal(ProviderAttemptOutcome.ProviderTimeout, Classify(Evidence(exception: ex, stage: AttemptStage.AwaitingHeaders)));
    }

    [Fact]
    public void Classify_HttpClientTimeoutByMessageOnly_IsProviderTimeout()
    {
        var ex = new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 600 seconds elapsing.");

        Assert.Equal(ProviderAttemptOutcome.ProviderTimeout, Classify(Evidence(exception: ex, stage: AttemptStage.AwaitingHeaders)));
    }

    [Theory]
    [InlineData("Anthropic")]
    [InlineData("OpenAI")]
    [InlineData("Google")]
    public void Classify_Http408_IsProviderTimeout(string provider)
    {
        Assert.Equal(ProviderAttemptOutcome.ProviderTimeout, Classify(Evidence(provider, status: 408)));
    }

    // --- Provider stream failure ----------------------------------------------------------------

    [Fact]
    public void Classify_UnknownStreamErrorAfterChunks_IsProviderStreamFailure()
    {
        var evidence = Evidence("OpenAI", status: 200, streamError: true, chunks: true,
            streamData: "OpenAI stream error: [stream_error] The stream was interrupted.");

        Assert.Equal(ProviderAttemptOutcome.ProviderStreamFailure, Classify(evidence));
    }

    [Fact]
    public void Classify_MidStreamOperationCanceledWithoutOurCancel_IsProviderStreamFailure()
    {
        Assert.Equal(ProviderAttemptOutcome.ProviderStreamFailure,
            Classify(Evidence(status: 200, chunks: true, exception: new OperationCanceledException(), stage: AttemptStage.Streaming)));
    }

    [Fact]
    public void Classify_HttpClientTimeoutAfterHeaders_IsProviderStreamFailure()
    {
        var ex = new TaskCanceledException("The request was canceled.", new TimeoutException());

        Assert.Equal(ProviderAttemptOutcome.ProviderStreamFailure, Classify(Evidence(status: 200, exception: ex, stage: AttemptStage.Streaming)));
    }

    // --- The 429 rule ---------------------------------------------------------------------------

    [Fact]
    public void Classify_Anthropic429Official_IsOurRateLimit()
    {
        Assert.Equal(ProviderAttemptOutcome.OurRateLimit, Classify(Evidence("Anthropic", status: 429, body: AnthropicRateLimitBody)));
    }

    [Fact]
    public void Classify_Anthropic429WithoutBody_IsStillOurRateLimit()
    {
        Assert.Equal(ProviderAttemptOutcome.OurRateLimit, Classify(Evidence("Anthropic", status: 429)));
    }

    [Fact]
    public void Classify_OpenAi429RateLimitExceeded_IsOurRateLimit()
    {
        Assert.Equal(ProviderAttemptOutcome.OurRateLimit, Classify(Evidence("OpenAI", status: 429, body: OpenAiRateLimitBody)));
    }

    [Theory]
    [InlineData("x-ratelimit-remaining-requests")]
    [InlineData("x-ratelimit-remaining-tokens")]
    public void Classify_OpenAi429WithASpentLimitHeader_IsOurRateLimit(string header)
    {
        var headers = new Dictionary<string, string> { [header] = "0" };

        Assert.Equal(ProviderAttemptOutcome.OurRateLimit,
            Classify(Evidence("OpenAI", status: 429, body: "{\"error\":{\"message\":\"Too many requests.\"}}", headers: headers)));
    }

    [Fact]
    public void Classify_OpenAi429WithNoCodeAndRemainingHeaders_IsRateLimitUnattributed()
    {
        var headers = new Dictionary<string, string>
        {
            ["x-ratelimit-remaining-requests"] = "499",
            ["x-ratelimit-remaining-tokens"] = "29000"
        };

        Assert.Equal(ProviderAttemptOutcome.RateLimitUnattributed,
            Classify(Evidence("OpenAI", status: 429, body: "{\"error\":{\"message\":\"Too many requests.\"}}", headers: headers)));
    }

    [Fact]
    public void Classify_OpenAi429WithNoHeadersAtAll_IsRateLimitUnattributed()
    {
        Assert.Equal(ProviderAttemptOutcome.RateLimitUnattributed, Classify(Evidence("OpenAI", status: 429)));
    }

    [Fact]
    public void Classify_OpenAi503SlowDown_IsOurRateLimit()
    {
        Assert.Equal(ProviderAttemptOutcome.OurRateLimit, Classify(Evidence("OpenAI", status: 503, body: OpenAiSlowDownBody)));
    }

    [Fact]
    public void Classify_Anthropic503SlowDownText_IsProviderOverloaded()
    {
        // The Slow Down rule is OpenAI's; the same words from another provider are an overload.
        Assert.Equal(ProviderAttemptOutcome.ProviderOverloaded, Classify(Evidence("Anthropic", status: 503, body: OpenAiSlowDownBody)));
    }

    [Fact]
    public void Classify_Google429WithQuotaFailure_IsOurRateLimit()
    {
        Assert.Equal(ProviderAttemptOutcome.OurRateLimit, Classify(Evidence("Google", status: 429, body: GoogleQuotaBody)));
    }

    [Fact]
    public void Classify_Google429WithoutQuotaFailure_IsRateLimitUnattributed()
    {
        Assert.Equal(ProviderAttemptOutcome.RateLimitUnattributed, Classify(Evidence("Google", status: 429, body: GoogleBareExhaustedBody)));
    }

    [Fact]
    public void Classify_GoogleStream429WithQuotaFailureDetail_IsOurRateLimit()
    {
        var evidence = Evidence("Google", status: 200, streamError: true,
            streamData: "Google stream error: [429] You exceeded your current quota.", body: GoogleQuotaBody);

        Assert.Equal(ProviderAttemptOutcome.OurRateLimit, Classify(evidence));
    }

    [Fact]
    public void Classify_GoogleStream429WithoutDetail_IsRateLimitUnattributed()
    {
        var evidence = Evidence("Google", status: 200, streamError: true,
            streamData: "Google stream error: [429] Resource has been exhausted (e.g. check quota).");

        Assert.Equal(ProviderAttemptOutcome.RateLimitUnattributed, Classify(evidence));
    }

    [Fact]
    public void Classify_AnthropicStreamRateLimitError_IsOurRateLimit()
    {
        var evidence = Evidence("Anthropic", status: 200, streamError: true,
            streamData: "Anthropic stream error: [rate_limit_error] Rate limited");

        Assert.Equal(ProviderAttemptOutcome.OurRateLimit, Classify(evidence));
    }

    [Fact]
    public void Classify_UnknownProvider429WithASpentRemainingHeader_IsOurRateLimit()
    {
        var headers = new Dictionary<string, string> { ["anthropic-ratelimit-requests-remaining"] = "0" };

        Assert.Equal(ProviderAttemptOutcome.OurRateLimit, Classify(Evidence("MockProvider", status: 429, headers: headers)));
    }

    [Fact]
    public void Classify_UnknownProvider429WithoutHeaders_IsRateLimitUnattributed()
    {
        Assert.Equal(ProviderAttemptOutcome.RateLimitUnattributed, Classify(Evidence("MockProvider", status: 429)));
    }

    [Theory]
    [InlineData("Anthropic", AnthropicRateLimitBody)]
    [InlineData("OpenAI", OpenAiRateLimitBody)]
    [InlineData("Google", GoogleQuotaBody)]
    public void Classify_Any429OnACustomEndpoint_IsRateLimitUnattributed(string provider, string body)
    {
        var headers = new Dictionary<string, string> { ["x-ratelimit-remaining-requests"] = "0" };

        Assert.Equal(ProviderAttemptOutcome.RateLimitUnattributed,
            Classify(Evidence(provider, BenchmarkEndpointKind.Custom, status: 429, body: body, headers: headers)));
    }

    [Fact]
    public void Classify_BillingOn429OnACustomEndpoint_IsStillOurBilling()
    {
        Assert.Equal(ProviderAttemptOutcome.OurBilling,
            Classify(Evidence("OpenAI", BenchmarkEndpointKind.Custom, status: 429, billing: true)));
    }

    [Fact]
    public void Classify_CustomEndpoint5xx_KeepsTheProviderClass()
    {
        Assert.Equal(ProviderAttemptOutcome.ProviderServerError,
            Classify(Evidence("OpenAI", BenchmarkEndpointKind.Custom, status: 502)));
    }

    // --- Unattributed transport -----------------------------------------------------------------

    [Theory]
    [InlineData(SocketError.ConnectionReset)]
    [InlineData(SocketError.ConnectionAborted)]
    [InlineData(SocketError.ConnectionRefused)]
    [InlineData(SocketError.TimedOut)]
    [InlineData(SocketError.HostUnreachable)]
    public void Classify_TransportSocketFailure_IsTransportUnattributed(SocketError error)
    {
        var ex = new HttpRequestException("The request failed.", new SocketException((int)error));

        Assert.Equal(ProviderAttemptOutcome.TransportUnattributed, Classify(Evidence(exception: ex, stage: AttemptStage.AwaitingHeaders)));
    }

    [Fact]
    public void Classify_TlsFailure_IsTransportUnattributed()
    {
        var ex = new HttpRequestException("The SSL connection could not be established.", new AuthenticationException("handshake"));

        Assert.Equal(ProviderAttemptOutcome.TransportUnattributed, Classify(Evidence(exception: ex, stage: AttemptStage.AwaitingHeaders)));
    }

    [Fact]
    public void Classify_IOExceptionMidStream_IsTransportUnattributed()
    {
        Assert.Equal(ProviderAttemptOutcome.TransportUnattributed,
            Classify(Evidence(status: 200, chunks: true, exception: new IOException("The response ended prematurely."), stage: AttemptStage.Streaming)));
    }

    [Fact]
    public void Classify_UnknownStreamErrorBeforeChunks_IsTransportUnattributed()
    {
        var evidence = Evidence("OpenAI", status: 200, streamError: true, chunks: false,
            streamData: "OpenAI stream error: [response.failed] status=failed");

        Assert.Equal(ProviderAttemptOutcome.TransportUnattributed, Classify(evidence));
    }

    // --- Ours: billing, key, request ------------------------------------------------------------

    [Theory]
    [InlineData("Anthropic", 400)]
    [InlineData("OpenAI", 429)]
    [InlineData("Google", 403)]
    public void Classify_ApiKeyBillingVerdict_IsOurBilling(string provider, int status)
    {
        Assert.Equal(ProviderAttemptOutcome.OurBilling, Classify(Evidence(provider, status: status, billing: true)));
    }

    [Fact]
    public void Classify_OpenAiInsufficientQuotaCode_IsOurBillingEvenWithoutTheVerdict()
    {
        var body = "{\"error\":{\"message\":\"You exceeded your current quota.\",\"type\":\"insufficient_quota\",\"code\":\"insufficient_quota\"}}";

        Assert.Equal(ProviderAttemptOutcome.OurBilling, Classify(Evidence("OpenAI", status: 429, body: body)));
    }

    [Fact]
    public void Classify_Http402_IsOurBilling()
    {
        Assert.Equal(ProviderAttemptOutcome.OurBilling, Classify(Evidence("MockProvider", status: 402)));
    }

    [Theory]
    [InlineData("Anthropic", 401)]
    [InlineData("OpenAI", 401)]
    [InlineData("Google", 403)]
    [InlineData("MockProvider", 403)]
    public void Classify_Http401Or403_IsOurKey(string provider, int status)
    {
        Assert.Equal(ProviderAttemptOutcome.OurKey, Classify(Evidence(provider, status: status)));
    }

    [Fact]
    public void Classify_ApiKeyRejectedVerdict_IsOurKey()
    {
        Assert.Equal(ProviderAttemptOutcome.OurKey, Classify(Evidence("Google", status: 400, keyRejected: true)));
    }

    [Fact]
    public void Classify_AnthropicStreamAuthenticationError_IsOurKey()
    {
        var evidence = Evidence("Anthropic", status: 200, streamError: true,
            streamData: "Anthropic stream error: [authentication_error] invalid x-api-key");

        Assert.Equal(ProviderAttemptOutcome.OurKey, Classify(evidence));
    }

    [Theory]
    [InlineData("Anthropic", 400)]
    [InlineData("OpenAI", 404)]
    [InlineData("Google", 409)]
    [InlineData("Anthropic", 413)]
    [InlineData("OpenAI", 422)]
    public void Classify_Http4xxRequestError_IsOurRequest(string provider, int status)
    {
        Assert.Equal(ProviderAttemptOutcome.OurRequest, Classify(Evidence(provider, status: status, body: "{\"error\":{\"type\":\"invalid_request_error\",\"message\":\"bad\"}}")));
    }

    [Fact]
    public void Classify_PromptTooLong_IsOurRequest()
    {
        var body = "{\"type\":\"error\",\"error\":{\"type\":\"invalid_request_error\",\"message\":\"prompt is too long: 205000 tokens > 200000 maximum\"}}";

        Assert.Equal(ProviderAttemptOutcome.OurRequest, Classify(Evidence("Anthropic", status: 400, body: body)));
    }

    [Fact]
    public void Classify_ModelNotFoundVerdict_IsOurRequest()
    {
        Assert.Equal(ProviderAttemptOutcome.OurRequest, Classify(Evidence("Google", status: 404, modelNotFound: true)));
    }

    [Fact]
    public void Classify_StreamContextLengthExceeded_IsOurRequest()
    {
        var evidence = Evidence("OpenAI", status: 200, streamError: true,
            streamData: "OpenAI stream error: [context_length_exceeded] Your input exceeds the context window.");

        Assert.Equal(ProviderAttemptOutcome.OurRequest, Classify(evidence));
    }

    [Fact]
    public void Classify_ExceptionBuildingOurOwnRequest_IsOurRequest()
    {
        Assert.Equal(ProviderAttemptOutcome.OurRequest,
            Classify(Evidence(exception: new InvalidOperationException("An invalid request URI was provided."), stage: AttemptStage.AwaitingHeaders)));
    }

    // --- Ours: throttle, cancel, network --------------------------------------------------------

    [Fact]
    public void Classify_GovernorRefusal_IsOurThrottle()
    {
        Assert.Equal(ProviderAttemptOutcome.OurThrottle, Classify(Evidence(governor: true, stage: AttemptStage.BeforeSend)));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Classify_OurCancel_IsOurCanceled_WhateverElseItCarries(bool callerCanceled, bool budget)
    {
        var evidence = Evidence("Anthropic", status: 200, exception: new OperationCanceledException(),
            callerCanceled: callerCanceled, budget: budget, chunks: true);

        Assert.Equal(ProviderAttemptOutcome.OurCanceled, Classify(evidence));
    }

    [Fact]
    public void Classify_Http499_IsOurCanceled()
    {
        Assert.Equal(ProviderAttemptOutcome.OurCanceled, Classify(Evidence("Google", status: 499)));
    }

    [Fact]
    public void Classify_GoogleStreamCancelled_IsOurCanceled()
    {
        var evidence = Evidence("Google", status: 200, streamError: true,
            streamData: "Google stream error: [499] The operation was cancelled.",
            body: "{\"error\":{\"code\":499,\"message\":\"The operation was cancelled.\",\"status\":\"CANCELLED\"}}");

        Assert.Equal(ProviderAttemptOutcome.OurCanceled, Classify(evidence));
    }

    [Theory]
    [InlineData(SocketError.HostNotFound)]
    [InlineData(SocketError.NoData)]
    [InlineData(SocketError.TryAgain)]
    [InlineData(SocketError.NetworkUnreachable)]
    public void Classify_OurMachinesNetworkFailure_IsOurNetwork(SocketError error)
    {
        var ex = new HttpRequestException("The request failed.", new SocketException((int)error));

        Assert.Equal(ProviderAttemptOutcome.OurNetwork, Classify(Evidence(exception: ex, stage: AttemptStage.AwaitingHeaders)));
    }

    [Fact]
    public void Classify_NameResolutionError_IsOurNetwork()
    {
        var ex = new HttpRequestException(HttpRequestError.NameResolutionError, "No such host is known.");

        Assert.Equal(ProviderAttemptOutcome.OurNetwork, Classify(Evidence(exception: ex, stage: AttemptStage.AwaitingHeaders)));
    }

    // --- Rule order -----------------------------------------------------------------------------

    [Fact]
    public void Classify_KeyVerdictWinsOverTheStatus()
    {
        // OpenAI reports an empty balance as 429; it must never reach the 429 rule.
        Assert.Equal(ProviderAttemptOutcome.OurBilling, Classify(Evidence("OpenAI", status: 429, body: OpenAiRateLimitBody, billing: true)));
    }

    [Fact]
    public void Classify_StatusWinsOverAnException()
    {
        Assert.Equal(ProviderAttemptOutcome.ProviderOverloaded,
            Classify(Evidence("Anthropic", status: 529, exception: new IOException("pipe"))));
    }

    [Fact]
    public void Classify_NoEvidenceAtAll_IsTransportUnattributed()
    {
        Assert.Equal(ProviderAttemptOutcome.TransportUnattributed, Classify(Evidence(stage: AttemptStage.AwaitingHeaders)));
    }

    // --- Evidence helpers -----------------------------------------------------------------------

    [Fact]
    public void ParseErrorBody_Anthropic_ReadsTheErrorType()
    {
        var fields = ProviderAttemptClassifier.ParseErrorBody(AnthropicOverloadedBody);

        Assert.NotNull(fields);
        Assert.Equal("overloaded_error", fields!.Type);
        Assert.Null(fields.Code);
    }

    [Fact]
    public void ParseErrorBody_OpenAi_ReadsCodeAndType()
    {
        var fields = ProviderAttemptClassifier.ParseErrorBody(OpenAiRateLimitBody);

        Assert.NotNull(fields);
        Assert.Equal("rate_limit_exceeded", fields!.Code);
        Assert.Equal("requests", fields.Type);
    }

    [Fact]
    public void ParseErrorBody_Google_ReadsStatusNumericCodeAndDetailTypes()
    {
        var fields = ProviderAttemptClassifier.ParseErrorBody(GoogleQuotaBody);

        Assert.NotNull(fields);
        Assert.Equal("RESOURCE_EXHAUSTED", fields!.Status);
        Assert.Equal(429, fields.NumericCode);
        Assert.Equal(
            new[] { ProviderAttemptClassifier.GoogleQuotaFailureType, "type.googleapis.com/google.rpc.RetryInfo" },
            fields.DetailTypes.ToArray());
    }

    [Fact]
    public void ParseErrorBody_GoogleArrayWrappedBody_ReadsTheFirstElement()
    {
        var fields = ProviderAttemptClassifier.ParseErrorBody("[" + GoogleQuotaBody + "]");

        Assert.NotNull(fields);
        Assert.Equal("RESOURCE_EXHAUSTED", fields!.Status);
        Assert.Contains(ProviderAttemptClassifier.GoogleQuotaFailureType, fields.DetailTypes);
    }

    [Fact]
    public void ParseErrorBody_OpenAiResponseFailedEvent_ReadsTheNestedError()
    {
        var fields = ProviderAttemptClassifier.ParseErrorBody(
            "{\"type\":\"response.failed\",\"response\":{\"status\":\"failed\",\"error\":{\"code\":\"server_error\",\"message\":\"x\"}}}");

        Assert.NotNull(fields);
        Assert.Equal("server_error", fields!.Code);
    }

    [Fact]
    public void ParseErrorBody_TopLevelErrorEvent_DoesNotTakeTheEventTypeAsTheErrorType()
    {
        var fields = ProviderAttemptClassifier.ParseErrorBody("{\"type\":\"error\",\"code\":\"rate_limit_exceeded\",\"message\":\"x\"}");

        Assert.NotNull(fields);
        Assert.Equal("rate_limit_exceeded", fields!.Code);
        Assert.Null(fields.Type);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("<html><body>502 Bad Gateway</body></html>")]
    [InlineData("{not json")]
    [InlineData("{\"message\":\"no error object\"}")]
    public void ParseErrorBody_NotAnErrorBody_IsNull(string? body)
    {
        Assert.Null(ProviderAttemptClassifier.ParseErrorBody(body));
    }

    [Fact]
    public void ParseStreamErrorEvent_BracketedTypeFromText()
    {
        var fields = ProviderAttemptClassifier.ParseStreamErrorEvent("Anthropic stream error: [overloaded_error] Overloaded", null);

        Assert.Equal("overloaded_error", fields.Type);
        Assert.Null(fields.NumericCode);
    }

    [Fact]
    public void ParseStreamErrorEvent_BracketedNumberFromText_IsTheNumericCode()
    {
        var fields = ProviderAttemptClassifier.ParseStreamErrorEvent("Google stream error: [429] Resource has been exhausted.", null);

        Assert.Equal(429, fields.NumericCode);
        Assert.Null(fields.Type);
    }

    [Fact]
    public void ParseStreamErrorEvent_DetailJsonWinsAndTheTextFillsTheGaps()
    {
        var fields = ProviderAttemptClassifier.ParseStreamErrorEvent(
            "Google stream error: [429] You exceeded your current quota.", GoogleQuotaBody);

        Assert.Equal("RESOURCE_EXHAUSTED", fields.Status);
        Assert.Equal(429, fields.NumericCode);
        Assert.Contains(ProviderAttemptClassifier.GoogleQuotaFailureType, fields.DetailTypes);
    }

    [Theory]
    [InlineData("overloaded_error", "overloaded_error")]
    [InlineData("type.googleapis.com/google.rpc.QuotaFailure", "type.googleapis.comgoogle.rpc.QuotaFailure")]
    [InlineData("rate limit <script>", "ratelimitscript")]
    [InlineData("  ", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void SanitizeErrorCode_KeepsOnlyTheAllowedCharacters(string? input, string? expected)
    {
        Assert.Equal(expected, ProviderAttemptClassifier.SanitizeErrorCode(input));
    }

    [Fact]
    public void SanitizeErrorCode_CapsTheLength()
    {
        string code = ProviderAttemptClassifier.SanitizeErrorCode(new string('a', 200))!;

        Assert.Equal(ProviderAttemptClassifier.MaxErrorCodeLength, code.Length);
    }

    [Theory]
    [InlineData("Anthropic", "c", "t", "s", "t")]
    [InlineData("OpenAI", "c", "t", "s", "c")]
    [InlineData("OpenAI", null, "t", "s", "t")]
    [InlineData("Google", "c", "t", "s", "s")]
    [InlineData("MockProvider", null, null, "s", "s")]
    public void PreferredErrorCode_PicksTheProvidersOwnField(string provider, string? code, string? type, string? status, string expected)
    {
        Assert.Equal(expected, ProviderAttemptClassifier.PreferredErrorCode(provider, code, type, status));
    }

    [Fact]
    public void Excerpt_IsBounded()
    {
        string text = new string('x', ProviderAttemptClassifier.MaxBodyExcerptLength + 500);

        Assert.Equal(ProviderAttemptClassifier.MaxBodyExcerptLength, ProviderAttemptClassifier.Excerpt(text)!.Length);
        Assert.Equal("short", ProviderAttemptClassifier.Excerpt("short"));
        Assert.Null(ProviderAttemptClassifier.Excerpt(null));
    }
}
