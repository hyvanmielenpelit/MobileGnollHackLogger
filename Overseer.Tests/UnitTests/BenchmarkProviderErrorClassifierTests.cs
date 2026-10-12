namespace Overseer.Tests.UnitTests;

using System;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Threading.Tasks;
using Overseer.Services.Benchmarking;
using Xunit;

public class BenchmarkProviderErrorClassifierTests
{
    // --- Message-based classifier: locale-independent transport substrings (run-29 Q8) ---

    [Theory]
    [InlineData(
        "A connection attempt failed because the connected party did not properly respond after a period of time, or established connection failed because connected host has failed to respond.",
        408)]
    [InlineData("An existing connection was forcibly closed by the remote host.", 503)]
    [InlineData("No such host is known.", 503)]
    [InlineData("Name or service not known", 503)]
    [InlineData("Connection actively refused by the remote host.", 503)]
    [InlineData("The SSL connection could not be established, see inner exception.", 502)]
    public void Classify_Message_MatchesTransportSubstring_RegardlessOfStatusCodeOrTimeoutWord(string message, int expectedStatus)
    {
        var result = BenchmarkProviderErrorClassifier.Classify(message);

        Assert.True(result.IsProviderError);
        Assert.Equal(expectedStatus, result.HttpStatus);
    }

    [Fact]
    public void Classify_Message_NonEnglishTransportFailure_IsNotRecognized()
    {
        // A German rendering of the same connect-timeout failure carries none of
        // TransportMessagePatterns' English needles and no status code, so the message-only
        // overload cannot recognize it. This is precisely the gap Classify(exception, ...) closes.
        string message = "Ein Verbindungsversuch ist fehlgeschlagen, da der verbundene Computer nach einer bestimmten Zeitspanne nicht richtig reagiert hat.";

        var result = BenchmarkProviderErrorClassifier.Classify(message);

        Assert.False(result.IsProviderError);
        Assert.Null(result.HttpStatus);
    }

    // --- Message-based classifier: OpenAI in-stream error vocabulary (harness version 21) ---

    [Theory]
    [InlineData("server_error", 503)]
    [InlineData("[server_error]", 503)]
    [InlineData("Our servers are currently overloaded", 503)]
    [InlineData("rate_limit_exceeded", 429)]
    public void Classify_Message_OpenAiInStreamErrorCode_MatchesExpectedStatus(string message, int expectedStatus)
    {
        var result = BenchmarkProviderErrorClassifier.Classify(message);

        Assert.True(result.IsProviderError);
        Assert.Equal(expectedStatus, result.HttpStatus);
    }

    [Fact]
    public void Classify_Message_RateLimitExceeded_IsCheckedBeforeServerError()
    {
        // 429 is checked first regardless of vocabulary added since: a message naming both must
        // still classify as the rate limit, not the server error.
        var result = BenchmarkProviderErrorClassifier.Classify("rate_limit_exceeded: server_error also present");

        Assert.True(result.IsProviderError);
        Assert.Equal(429, result.HttpStatus);
    }

    [Theory]
    [InlineData("[520]", 520)]
    [InlineData("[521]", 521)]
    public void Classify_Message_BracketedFiveHundredsStatus_MapsToThatStatus(string message, int expectedStatus)
    {
        var result = BenchmarkProviderErrorClassifier.Classify(message);

        Assert.True(result.IsProviderError);
        Assert.Equal(expectedStatus, result.HttpStatus);
    }

    [Fact]
    public void Classify_Message_BracketedStatusOutsideFiveHundreds_IsNotRecognizedByTheBracketRule()
    {
        // [429] is caught by the 429 branch above as an anchored status; a bracketed code the earlier
        // branches do not recognize, and that is not itself in 500-599, is not a provider error.
        var result = BenchmarkProviderErrorClassifier.Classify("[418]");

        Assert.False(result.IsProviderError);
    }

    // --- Typed classifier: Classify(Exception?, string?, bool) ---

    [Fact]
    public void Classify_Typed_SocketTimedOut_Returns408()
    {
        var ex = new SocketException((int)SocketError.TimedOut);

        var result = BenchmarkProviderErrorClassifier.Classify(ex, null, false);

        Assert.True(result.IsProviderError);
        Assert.Equal(408, result.HttpStatus);
    }

    [Theory]
    [InlineData(SocketError.ConnectionRefused)]
    [InlineData(SocketError.HostNotFound)]
    [InlineData(SocketError.ConnectionReset)]
    [InlineData(SocketError.ConnectionAborted)]
    [InlineData(SocketError.NetworkUnreachable)]
    [InlineData(SocketError.HostUnreachable)]
    [InlineData(SocketError.TryAgain)]
    public void Classify_Typed_OtherTransportSocketErrors_Return503(SocketError socketError)
    {
        var ex = new SocketException((int)socketError);

        var result = BenchmarkProviderErrorClassifier.Classify(ex, null, false);

        Assert.True(result.IsProviderError);
        Assert.Equal(503, result.HttpStatus);
    }

    [Fact]
    public void Classify_Typed_SocketErrorNotInTransportList_FallsBackToMessageClassifier()
    {
        var ex = new SocketException((int)SocketError.AccessDenied);

        var result = BenchmarkProviderErrorClassifier.Classify(ex, "500 Internal Server Error", false);

        Assert.True(result.IsProviderError);
        Assert.Equal(500, result.HttpStatus);
    }

    [Fact]
    public void Classify_Typed_HttpRequestExceptionAlone_Returns503()
    {
        var ex = new HttpRequestException("The request failed.");

        var result = BenchmarkProviderErrorClassifier.Classify(ex, null, false);

        Assert.True(result.IsProviderError);
        Assert.Equal(503, result.HttpStatus);
    }

    [Fact]
    public void Classify_Typed_HttpRequestExceptionWrappingSocketTimeout_Returns408NotTheWrapperStatus()
    {
        // The wrapper alone classifies as 503; the more specific transport evidence one level
        // down must win over it.
        var inner = new SocketException((int)SocketError.TimedOut);
        var ex = new HttpRequestException("The request failed.", inner);

        var result = BenchmarkProviderErrorClassifier.Classify(ex, null, false);

        Assert.True(result.IsProviderError);
        Assert.Equal(408, result.HttpStatus);
    }

    [Fact]
    public void Classify_Typed_TaskCanceledException_NotCallerInitiated_Returns408()
    {
        var ex = new TaskCanceledException("The task was canceled.");

        var result = BenchmarkProviderErrorClassifier.Classify(ex, "Operation canceled.", false);

        Assert.True(result.IsProviderError);
        Assert.Equal(408, result.HttpStatus);
    }

    [Fact]
    public void Classify_Typed_TaskCanceledException_CallerInitiated_IsNotAProviderError()
    {
        // Only the caller knows whether this cancel was user-initiated; when it was, it must not
        // be reported as a provider failure.
        var ex = new TaskCanceledException("The task was canceled.");

        var result = BenchmarkProviderErrorClassifier.Classify(ex, "Operation canceled.", true);

        Assert.False(result.IsProviderError);
    }

    [Fact]
    public void Classify_Typed_TimeoutException_Returns408()
    {
        var ex = new TimeoutException("The operation has timed out.");

        var result = BenchmarkProviderErrorClassifier.Classify(ex, null, false);

        Assert.True(result.IsProviderError);
        Assert.Equal(408, result.HttpStatus);
    }

    [Fact]
    public void Classify_Typed_IOException_Returns502()
    {
        var ex = new IOException("The pipe has been ended.");

        var result = BenchmarkProviderErrorClassifier.Classify(ex, null, false);

        Assert.True(result.IsProviderError);
        Assert.Equal(502, result.HttpStatus);
    }

    [Fact]
    public void Classify_Typed_AuthenticationException_Returns502()
    {
        var ex = new AuthenticationException("The TLS handshake failed.");

        var result = BenchmarkProviderErrorClassifier.Classify(ex, null, false);

        Assert.True(result.IsProviderError);
        Assert.Equal(502, result.HttpStatus);
    }

    [Fact]
    public void Classify_Typed_AggregateException_FindsSocketExceptionInSecondInnerException()
    {
        // The chain walker flattens every branch of an AggregateException rather than
        // following only its first.
        var aggregate = new AggregateException(
            new InvalidOperationException("Unrelated failure."),
            new SocketException((int)SocketError.ConnectionReset));

        var result = BenchmarkProviderErrorClassifier.Classify(aggregate, null, false);

        Assert.True(result.IsProviderError);
        Assert.Equal(503, result.HttpStatus);
    }

    [Fact]
    public void Classify_Typed_NullException_IsIdenticalToTheMessageOverload()
    {
        string message = "429 Too Many Requests";

        var typed = BenchmarkProviderErrorClassifier.Classify(null, message, false);
        var messageOnly = BenchmarkProviderErrorClassifier.Classify(message);

        Assert.Equal(messageOnly, typed);
    }

    [Fact]
    public void Classify_Typed_ExceptionTypeInNoRule_FallsBackToMessageClassifier()
    {
        var ex = new InvalidOperationException("Something else went wrong.");

        var result = BenchmarkProviderErrorClassifier.Classify(ex, "429 rate limited", false);

        Assert.True(result.IsProviderError);
        Assert.Equal(429, result.HttpStatus);
    }

    // --- Caller-cancel precedence over the transport rules (harness version 21) ---

    [Fact]
    public void Classify_Typed_IOExceptionWrappingOperationCanceled_CallerCanceled_IsNotAProviderError()
    {
        var inner = new OperationCanceledException("The operation was canceled.");
        var ex = new IOException("The pipe has been ended.", inner);

        var result = BenchmarkProviderErrorClassifier.Classify(ex, null, true);

        Assert.False(result.IsProviderError);
        Assert.Null(result.HttpStatus);
    }

    [Fact]
    public void Classify_Typed_SocketExceptionUnderHttpRequestUnderOperationCanceled_CallerCanceled_IsNotAProviderError()
    {
        var socket = new SocketException((int)SocketError.ConnectionAborted);
        var http = new HttpRequestException("The request failed.", socket);
        var ex = new OperationCanceledException("The operation was canceled.", http);

        var result = BenchmarkProviderErrorClassifier.Classify(ex, null, true);

        Assert.False(result.IsProviderError);
        Assert.Null(result.HttpStatus);
    }

    [Fact]
    public void Classify_Typed_IOExceptionWrappingOperationCanceled_NotCallerCanceled_KeepsItsPriorClassification()
    {
        // Without callerCanceled, this chain is unchanged from before the caller-cancel rule
        // existed: the OperationCanceledException rule runs ahead of the IOException rule
        // regardless of which exception wraps which, so this chain classifies as a 408 timeout,
        // not the 502 a bare IOException alone would produce.
        var inner = new OperationCanceledException("The operation was canceled.");
        var ex = new IOException("The pipe has been ended.", inner);

        var result = BenchmarkProviderErrorClassifier.Classify(ex, null, false);

        Assert.True(result.IsProviderError);
        Assert.Equal(408, result.HttpStatus);
    }

    [Fact]
    public void Classify_Typed_SocketExceptionUnderHttpRequestUnderOperationCanceled_NotCallerCanceled_Returns503()
    {
        // The socket rule is checked first and wins regardless of the OperationCanceledException
        // elsewhere in the chain: the more specific transport evidence takes precedence.
        var socket = new SocketException((int)SocketError.ConnectionAborted);
        var http = new HttpRequestException("The request failed.", socket);
        var ex = new OperationCanceledException("The operation was canceled.", http);

        var result = BenchmarkProviderErrorClassifier.Classify(ex, null, false);

        Assert.True(result.IsProviderError);
        Assert.Equal(503, result.HttpStatus);
    }

    // --- Anchored status digits: a bare number is not a status ---

    [Theory]
    [InlineData("API Error: 400 - {\"type\":\"error\",\"error\":{\"type\":\"invalid_request_error\",\"message\":\"prompt is too long: 205000 tokens > 200000 maximum\"}}")]
    [InlineData("prompt is too long: 205000 tokens > 200000")]
    [InlineData("API Error: 400 - The request used 4290 input tokens and 15030 cached tokens.")]
    [InlineData("API Error: 400 - Invalid value at 'generation_config.max_output_tokens' (5029).")]
    public void Classify_Message_StatusDigitsInsideOtherNumbers_AreNotAProviderError(string message)
    {
        var result = BenchmarkProviderErrorClassifier.Classify(message);

        Assert.False(result.IsProviderError);
        Assert.Null(result.HttpStatus);
    }

    [Theory]
    [InlineData("429 Rate Limited. Max retries (4) exceeded. Please try again later.", 429)]
    [InlineData("503 Unavailable. Max retries exceeded.", 503)]
    [InlineData("API Error: 529 - {\"type\":\"error\",\"error\":{\"type\":\"overloaded_error\"}}", 529)]
    [InlineData("API Error: 502 - <html>Bad Gateway</html>", 502)]
    [InlineData("API Error: 408 - Request Timeout", 408)]
    [InlineData("Upstream returned HTTP 504 after 60 s.", 504)]
    [InlineData("Response status code does not indicate success: 503 (Service Unavailable).", 503)]
    [InlineData("Proxy error. Status: 500", 500)]
    [InlineData("Google stream error: [503] This model is currently experiencing high demand.", 503)]
    [InlineData("Stream interrupted: The response ended prematurely. | 503 Unavailable. Max retries exceeded.", 503)]
    public void Classify_Message_AnchoredStatus_StillClassifies(string message, int expectedStatus)
    {
        var result = BenchmarkProviderErrorClassifier.Classify(message);

        Assert.True(result.IsProviderError);
        Assert.Equal(expectedStatus, result.HttpStatus);
    }

    [Fact]
    public void Classify_Message_AnchoredOtherFiveHundred_MapsToThatStatus()
    {
        var result = BenchmarkProviderErrorClassifier.Classify("Upstream returned HTTP 520.");

        Assert.True(result.IsProviderError);
        Assert.Equal(520, result.HttpStatus);
    }

    // --- Overseer's own failure texts are never provider errors ---

    [Theory]
    [InlineData("Request throttled: Timed out after 120.0s waiting for concurrent model slot on Anthropic:system:429.")]
    [InlineData("Request throttled: Rate limit cooldown active for OpenAI:system:503 (30.0s remaining), exceeding wait timeout of 10.0s.")]
    [InlineData("Rate limit cooldown active for Google:system:529 (12.5s remaining), exceeding wait timeout of 5.0s.")]
    [InlineData("Per-question timeout exceeded (720 s) after 3 model call(s) and 2 tool call(s); first token at 1.6 s; last stream event 690 s before the timeout.")]
    public void Classify_Message_OwnFailureText_IsNotAProviderError(string message)
    {
        var result = BenchmarkProviderErrorClassifier.Classify(message);

        Assert.False(result.IsProviderError);
        Assert.Null(result.HttpStatus);
        Assert.Equal(message, result.Message);
    }

    [Fact]
    public void Classify_Typed_OwnFailureText_IsNotAProviderErrorWhateverTheException()
    {
        string message = "Request throttled: Timed out after 120.0s waiting for concurrent model slot on OpenAI:system:503.";

        var result = BenchmarkProviderErrorClassifier.Classify(new TimeoutException(message), message, false);

        Assert.False(result.IsProviderError);
        Assert.Null(result.HttpStatus);
    }

    // --- The question budget is ours ---

    [Fact]
    public void Classify_Typed_OperationCanceled_QuestionBudget_IsNotAProviderError()
    {
        var ex = new TaskCanceledException("A task was canceled.");

        var result = BenchmarkProviderErrorClassifier.Classify(ex, "A task was canceled.", callerCanceled: false, questionBudgetCanceled: true);

        Assert.False(result.IsProviderError);
        Assert.Null(result.HttpStatus);
    }

    [Fact]
    public void Classify_Typed_QuestionTimeoutText_WithoutTheFlag_IsStillNotAProviderError()
    {
        // The budget's own text is recognized by its marker before the exception chain is read.
        string message = "Per-question timeout exceeded (420 s) after 1 model call(s) and 0 tool call(s); no token received; last stream event 420 s before the timeout.";

        var result = BenchmarkProviderErrorClassifier.Classify(new OperationCanceledException(), message, callerCanceled: false);

        Assert.False(result.IsProviderError);
    }

    [Theory]
    [InlineData("Harness delivery check failed: the Google request for question 4 did not contain the board.")]
    [InlineData("Harness delivery check failed: the OpenAI request for question 12 did not contain the system prompt.")]
    [InlineData("Harness delivery check failed: the Anthropic request did not contain the system prompt and the board.")]
    public void Classify_HarnessDeliveryFailure_IsNotAProviderError(string message)
    {
        // The answer is stored Failed and lands under Transport Defects; ProviderError would put a
        // harness bug on the provider's account and change what the run's integrity block says.
        // This pins the message against a later widening of the substring rules above.
        var result = BenchmarkProviderErrorClassifier.Classify(
            new InvalidOperationException(message), message, false);

        Assert.False(result.IsProviderError);
        Assert.Null(result.HttpStatus);
        Assert.Equal(message, result.Message);
    }
}
