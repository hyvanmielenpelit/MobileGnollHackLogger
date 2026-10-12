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
    /// <summary>
    /// A status code in a form only a status takes: <c>API Error: NNN</c>, <c>HTTP NNN</c>,
    /// <c>status code NNN</c> (also .NET's "status code does not indicate success: NNN"),
    /// <c>Status: NNN</c>, a bracketed <c>[NNN]</c> such as a Cloudflare edge code, and a leading
    /// <c>NNN </c> at the start of the text or of a " | "-joined part, which is how the agent loop's
    /// own terminal texts begin ("429 Rate Limited. …"). A bare number anywhere else, such as a token
    /// count, is not a status.
    /// </summary>
    private static readonly Regex AnchoredStatusRegex = new(
        @"(?:API Error:\s*|\bHTTP\s+|\bstatus code\s+(?:does not indicate success:\s*)?|\bStatus:\s*)(?<code>\d{3})\b"
        + @"|\[(?<code>\d{3})\]"
        + @"|(?:^|\s\|\s)(?<code>\d{3})(?=\s)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Fixed openings of Overseer's own failure texts: the request governor's permit timeout and
    /// cooldown refusal, and the benchmark's per-question time budget. They are never provider errors,
    /// whatever status-like words they carry.
    /// </summary>
    private static readonly string[] OwnFailureMarkers =
    {
        "Request throttled:",
        "Rate limit cooldown active",
        "Per-question timeout exceeded ("
    };

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
    /// <param name="questionBudgetCanceled">
    /// The caller's own per-question time budget cut the attempt. Like a caller cancel, that is never a
    /// provider error.
    /// </param>
    public static ProviderErrorClassification Classify(Exception? exception, string? errorMessage, bool callerCanceled, bool questionBudgetCanceled = false)
    {
        if (exception == null)
        {
            return Classify(errorMessage);
        }

        var chain = Unwrap(exception).ToList();
        string message = !string.IsNullOrWhiteSpace(errorMessage) ? errorMessage!.Trim() : exception.Message;

        if (IsOwnFailure(message))
        {
            return new ProviderErrorClassification(false, null, message);
        }

        // A caller cancel is checked ahead of every transport rule: tearing the provider stream down
        // raises an IOException or SocketException around the cancellation, and those rules would
        // otherwise claim the chain before the intent behind it is considered.
        if ((callerCanceled || questionBudgetCanceled) && chain.OfType<OperationCanceledException>().Any())
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

        if (IsOwnFailure(msg))
        {
            return new ProviderErrorClassification(false, null, msg);
        }

        var statuses = AnchoredStatuses(msg);

        // 429 Rate limiting
        if (statuses.Contains(429) || msg.IndexOf("rate limit", StringComparison.OrdinalIgnoreCase) >= 0 ||
            msg.IndexOf("rate_limit_exceeded", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return new ProviderErrorClassification(true, 429, msg);
        }

        // 529 Overload (Anthropic)
        if (statuses.Contains(529) || msg.IndexOf("overloaded_error", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return new ProviderErrorClassification(true, 529, msg);
        }

        // 503 / Overloaded / Service Unavailable / Bad Gateway / 504 Gateway Timeout / 500 Internal Server Error /
        // server_error and [server_error] (OpenAI's in-stream error code) / "Our servers are currently
        // overloaded" (OpenAI's own wording for the same condition)
        if (statuses.Contains(503) || msg.IndexOf("overloaded", StringComparison.OrdinalIgnoreCase) >= 0 ||
            msg.IndexOf("service unavailable", StringComparison.OrdinalIgnoreCase) >= 0 ||
            statuses.Contains(502) || msg.IndexOf("bad gateway", StringComparison.OrdinalIgnoreCase) >= 0 ||
            statuses.Contains(504) || msg.IndexOf("gateway timeout", StringComparison.OrdinalIgnoreCase) >= 0 ||
            statuses.Contains(500) || msg.IndexOf("internal server error", StringComparison.OrdinalIgnoreCase) >= 0 ||
            msg.IndexOf("server_error", StringComparison.OrdinalIgnoreCase) >= 0 ||
            msg.IndexOf("Our servers are currently overloaded", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            int code = statuses.Contains(502) ? 502 : (statuses.Contains(504) ? 504 : (statuses.Contains(500) ? 500 : 503));
            return new ProviderErrorClassification(true, code, msg);
        }

        // An anchored 5xx status the checks above did not already recognize, such as a Cloudflare edge code.
        int otherServerStatus = statuses.FirstOrDefault(s => s is >= 500 and <= 599);
        if (otherServerStatus != 0)
        {
            return new ProviderErrorClassification(true, otherServerStatus, msg);
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

        // An anchored 408 Request Timeout.
        if (statuses.Contains(408))
        {
            return new ProviderErrorClassification(true, 408, msg);
        }

        // Genuine model or application errors (not provider transient failure)
        return new ProviderErrorClassification(false, null, msg);
    }

    /// <summary>Whether the text is one of Overseer's own failures, by <see cref="OwnFailureMarkers"/>.</summary>
    public static bool IsOwnFailure(string message)
    {
        foreach (string marker in OwnFailureMarkers)
        {
            if (message.Contains(marker, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>The status codes the text carries in an anchored form, in order of appearance.</summary>
    private static List<int> AnchoredStatuses(string message)
    {
        var statuses = new List<int>();
        foreach (Match match in AnchoredStatusRegex.Matches(message))
        {
            if (int.TryParse(match.Groups["code"].Value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int status))
            {
                statuses.Add(status);
            }
        }
        return statuses;
    }
}
