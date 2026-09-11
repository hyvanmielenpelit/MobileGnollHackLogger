namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text.RegularExpressions;

public record ProviderErrorClassification(bool IsProviderError, int? HttpStatus, string Message);

public static class BenchmarkProviderErrorClassifier
{
    private static readonly Regex ApiErrorRegex = new(@"API Error:\s*(\d{3})", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>A bracketed status code an upstream returns verbatim, e.g. a Cloudflare edge code ([520], [521]).</summary>
    private static readonly Regex BracketedStatusRegex = new(@"\[(\d{3})\]", RegexOptions.Compiled);

    /// <summary>
    /// Socket failures that mean the provider was unreachable rather than that it answered badly.
    /// </summary>
    private static readonly SocketError[] TransportSocketErrors =
    {
        SocketError.TimedOut,
        SocketError.ConnectionRefused,
        SocketError.HostNotFound,
        SocketError.ConnectionReset,
        SocketError.ConnectionAborted,
        SocketError.NetworkUnreachable,
        SocketError.HostUnreachable,
        SocketError.TryAgain
    };

    /// <summary>
    /// Transport-failure substrings that the message-only overload cannot reach through
    /// <c>timeout</c> or an HTTP status. They are matched only as a fallback: a socket or HTTP
    /// failure surfaces its own exception type, and only the streamed <c>"error"</c> event path
    /// arrives as a bare string.
    /// </summary>
    private static readonly (string Needle, int Status)[] TransportMessagePatterns =
    {
        ("connection attempt failed", 408),
        ("forcibly closed", 503),
        ("No such host is known", 503),
        ("Name or service not known", 503),
        ("actively refused", 503),
        ("SSL connection could not be established", 502)
    };

    /// <summary>
    /// Classifies from the exception type rather than from its text. An operating-system transport
    /// failure carries a message in the machine's display language, so a substring match against it
    /// holds only on an English-locale host; the exception type and <see cref="SocketError"/> do not
    /// vary. <paramref name="callerCanceled"/> distinguishes a user-initiated cancel — which is not a
    /// provider error — from a cancellation the transport raised, and only the caller knows which it
    /// was. Falls back to <see cref="Classify(string?)"/> when no type in the chain matches.
    /// </summary>
    public static ProviderErrorClassification Classify(Exception? exception, string? errorMessage, bool callerCanceled)
    {
        if (exception == null)
        {
            return Classify(errorMessage);
        }

        var chain = Unwrap(exception).ToList();
        string message = !string.IsNullOrWhiteSpace(errorMessage) ? errorMessage!.Trim() : exception.Message;

        // A caller cancel is checked ahead of every transport rule: tearing the provider stream down
        // raises an IOException or SocketException around the cancellation, and those rules would
        // otherwise claim the chain before the intent behind it is considered.
        if (callerCanceled && chain.OfType<OperationCanceledException>().Any())
        {
            return new ProviderErrorClassification(false, null, message);
        }

        // Each rule scans the whole chain before the next is tried, so the most specific transport
        // evidence wins over the HttpRequestException that usually wraps it.
        var socket = chain.OfType<SocketException>().FirstOrDefault(e => TransportSocketErrors.Contains(e.SocketErrorCode));
        if (socket != null)
        {
            return new ProviderErrorClassification(true, socket.SocketErrorCode == SocketError.TimedOut ? 408 : 503, message);
        }

        if (chain.OfType<OperationCanceledException>().Any())
        {
            return new ProviderErrorClassification(true, 408, message);
        }

        if (chain.OfType<TimeoutException>().Any())
        {
            return new ProviderErrorClassification(true, 408, message);
        }

        if (chain.Any(e => e is System.IO.IOException or AuthenticationException))
        {
            return new ProviderErrorClassification(true, 502, message);
        }

        if (chain.OfType<HttpRequestException>().Any())
        {
            return new ProviderErrorClassification(true, 503, message);
        }

        return Classify(message);
    }

    /// <summary>
    /// The exception and every level below it, outermost first, with an
    /// <see cref="AggregateException"/>'s branches walked rather than only its first.
    /// </summary>
    private static IEnumerable<Exception> Unwrap(Exception exception)
    {
        yield return exception;

        if (exception is AggregateException aggregate)
        {
            foreach (Exception inner in aggregate.InnerExceptions.SelectMany(Unwrap))
            {
                yield return inner;
            }

            yield break;
        }

        if (exception.InnerException != null)
        {
            foreach (Exception inner in Unwrap(exception.InnerException))
            {
                yield return inner;
            }
        }
    }

    public static ProviderErrorClassification Classify(string? errorMessage)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
        {
            return new ProviderErrorClassification(false, null, string.Empty);
        }

        string msg = errorMessage.Trim();

        // 429 Rate limiting
        if (msg.Contains("429") || msg.IndexOf("rate limit", StringComparison.OrdinalIgnoreCase) >= 0 ||
            msg.IndexOf("rate_limit_exceeded", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return new ProviderErrorClassification(true, 429, msg);
        }

        // 529 Overload (Anthropic)
        if (msg.Contains("529") || msg.IndexOf("overloaded_error", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return new ProviderErrorClassification(true, 529, msg);
        }

        // 503 / Overloaded / Service Unavailable / Bad Gateway / 504 Gateway Timeout / 500 Internal Server Error /
        // server_error and [server_error] (OpenAI's in-stream error code) / "Our servers are currently
        // overloaded" (OpenAI's own wording for the same condition)
        if (msg.Contains("503") || msg.IndexOf("overloaded", StringComparison.OrdinalIgnoreCase) >= 0 ||
            msg.IndexOf("service unavailable", StringComparison.OrdinalIgnoreCase) >= 0 ||
            msg.Contains("502") || msg.IndexOf("bad gateway", StringComparison.OrdinalIgnoreCase) >= 0 ||
            msg.Contains("504") || msg.IndexOf("gateway timeout", StringComparison.OrdinalIgnoreCase) >= 0 ||
            msg.Contains("500") || msg.IndexOf("internal server error", StringComparison.OrdinalIgnoreCase) >= 0 ||
            msg.IndexOf("server_error", StringComparison.OrdinalIgnoreCase) >= 0 ||
            msg.IndexOf("Our servers are currently overloaded", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            int code = msg.Contains("502") ? 502 : (msg.Contains("504") ? 504 : (msg.Contains("500") ? 500 : 503));
            return new ProviderErrorClassification(true, code, msg);
        }

        // A bracketed 5xx status the checks above did not already recognise by its literal digits.
        var bracketedStatus = BracketedStatusRegex.Match(msg);
        if (bracketedStatus.Success && int.TryParse(bracketedStatus.Groups[1].Value, out int bracketedCode) &&
            bracketedCode is >= 500 and <= 599)
        {
            return new ProviderErrorClassification(true, bracketedCode, msg);
        }

        // Timeouts
        if (msg.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0 ||
            msg.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return new ProviderErrorClassification(true, 408, msg);
        }

        // Transport failures whose text carries neither a status code nor the word "timeout".
        foreach ((string needle, int status) in TransportMessagePatterns)
        {
            if (msg.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return new ProviderErrorClassification(true, status, msg);
            }
        }

        // API Error: XXX
        var match = ApiErrorRegex.Match(msg);
        if (match.Success && int.TryParse(match.Groups[1].Value, out int statusCode))
        {
            if (statusCode == 429 || statusCode == 500 || statusCode == 502 || statusCode == 503 || statusCode == 504 || statusCode == 529 || statusCode == 408)
            {
                return new ProviderErrorClassification(true, statusCode, msg);
            }
        }

        // Genuine model or application errors (not provider transient failure)
        return new ProviderErrorClassification(false, null, msg);
    }
}
