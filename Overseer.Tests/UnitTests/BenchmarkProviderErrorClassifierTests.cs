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
        // [429] is already caught by the 429 branch above (a plain substring match); a bracketed
        // code the earlier branches do not already recognise, and that is not itself in 500-599, is
        // not a provider error.
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
}
