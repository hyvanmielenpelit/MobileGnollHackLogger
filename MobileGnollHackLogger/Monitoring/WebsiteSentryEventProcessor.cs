using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using System.Text.RegularExpressions;
using Sentry;
using Sentry.Extensibility;

namespace MobileGnollHackLogger.Monitoring
{
    public class WebsiteSentryEventProcessor : ISentryEventProcessor
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        /* Header names and query parameter names whose values are removed before an event
           leaves the process. Matched case-insensitively and, for query parameters, by exact
           name rather than substring -- "code" must not also blank "postcode". "email" and
           "userId" are carried by the Identity confirmation and reset links. */
        private static readonly string[] ScrubbedHeaders =
        {
            "Authorization", "Cookie", "Set-Cookie", "Proxy-Authorization",
            "X-XSRF-TOKEN", "X-Api-Key", "X-Sentry-Auth"
        };

        private static readonly string[] ScrubbedQueryKeys =
        {
            "token", "key", "apiKey", "api_key", "password", "code", "secret", "access_token",
            "email", "userId"
        };

        private const string Scrubbed = "[scrubbed]";

        /* A scrubbed query parameter inside free text, such as a URL in a log message. The name
           must follow '?' or '&', so "postcode=" does not match "code". */
        private static readonly Regex ScrubbedQueryParameterInText = new Regex(
            @"(?<prefix>[?&])(?<name>" + string.Join("|", ScrubbedQueryKeys.Select(Regex.Escape)) + @")=[^&#\s]*",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public WebsiteSentryEventProcessor(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public SentryEvent? Process(SentryEvent @event)
        {
            /* Kestrel throws these for malformed or truncated requests, typically a mobile
               client dropping mid-upload; they are answered with 400 and are not server bugs. */
            if (ContainsException(@event.Exception, e => e is BadHttpRequestException))
            {
                return null;
            }

            /* Both conditions are required, so a genuine I/O or SQL failure on a live request is
               still reported. SqlClient reports a query cancelled by an aborted request as a
               SqlException, not as an OperationCanceledException. */
            if (_httpContextAccessor.HttpContext?.RequestAborted.IsCancellationRequested == true
                && ContainsException(@event.Exception, IsAbortException))
            {
                return null;
            }

            Scrub(@event);

            return @event;
        }

        private static bool IsAbortException(Exception exception)
        {
            return exception is OperationCanceledException
                || exception is IOException
                || (exception is SqlException sqlEx
                    && sqlEx.Message.Contains("Operation cancelled by user", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Walks the inner exceptions, and every member of an <see cref="AggregateException"/>.</summary>
        private static bool ContainsException(Exception? exception, Func<Exception, bool> predicate)
        {
            if (exception == null)
            {
                return false;
            }

            if (predicate(exception))
            {
                return true;
            }

            if (exception is AggregateException aggEx)
            {
                foreach (var inner in aggEx.InnerExceptions)
                {
                    if (ContainsException(inner, predicate))
                    {
                        return true;
                    }
                }

                return false;
            }

            return ContainsException(exception.InnerException, predicate);
        }

        /// <summary>
        /// Removes credentials and identity from an outgoing event: named request headers,
        /// named query values, and the whole user record.
        /// </summary>
        private static void Scrub(SentryEvent @event)
        {
            var request = @event.Request;
            if (request != null)
            {
                foreach (var name in ScrubbedHeaders)
                {
                    if (request.Headers.ContainsKey(name))
                        request.Headers[name] = Scrubbed;
                }

                /* Cookies live in their own property as well as in the Cookie header. */
                if (!string.IsNullOrEmpty(request.Cookies))
                    request.Cookies = Scrubbed;

                request.QueryString = ScrubQueryString(request.QueryString);
                request.Url = ScrubUrl(request.Url);
            }

            @event.User.Email = null;
            @event.User.Username = null;
            @event.User.IpAddress = null;
            @event.User.Other?.Clear();

            foreach (var tag in ScrubbedQueryKeys)
            {
                if (@event.Tags.ContainsKey(tag))
                    @event.SetTag(tag, Scrubbed);
            }
        }

        /// <summary>
        /// Blanks sensitive query values in a breadcrumb's message and data, such as the URL in
        /// ASP.NET Core's "Request starting" log entry. Registered with
        /// <c>SetBeforeBreadcrumb</c>, because an event's breadcrumbs cannot be replaced in
        /// <see cref="Process"/>.
        /// </summary>
        public static Breadcrumb ScrubBreadcrumb(Breadcrumb breadcrumb)
        {
            string? message = ScrubText(breadcrumb.Message);
            bool changed = !string.Equals(message, breadcrumb.Message, StringComparison.Ordinal);

            Dictionary<string, string>? data = null;
            if (breadcrumb.Data != null)
            {
                data = new Dictionary<string, string>(breadcrumb.Data.Count);
                foreach (var (name, value) in breadcrumb.Data)
                {
                    string scrubbed = ScrubText(value) ?? value;
                    changed |= !string.Equals(scrubbed, value, StringComparison.Ordinal);
                    data[name] = scrubbed;
                }
            }

            if (!changed)
                return breadcrumb;

            /* The constructor taking a timestamp is internal; this one stamps the current time,
               which is when the SDK hands a new breadcrumb to the callback. */
            return new Breadcrumb(message ?? string.Empty, breadcrumb.Type ?? string.Empty, data, breadcrumb.Category, breadcrumb.Level);
        }

        /// <summary>Blanks sensitive query values wherever they appear in free text.</summary>
        internal static string? ScrubText(string? text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            return ScrubbedQueryParameterInText.Replace(text, m => m.Groups["prefix"].Value + m.Groups["name"].Value + "=" + Scrubbed);
        }

        /// <summary>Blanks the value of every sensitive parameter, keeping the shape of the query.</summary>
        internal static string? ScrubQueryString(string? queryString)
        {
            if (string.IsNullOrEmpty(queryString))
                return queryString;

            bool leadingQuestionMark = queryString[0] == '?';
            string body = leadingQuestionMark ? queryString[1..] : queryString;

            var parts = body.Split('&');
            for (int i = 0; i < parts.Length; i++)
            {
                int eq = parts[i].IndexOf('=');
                if (eq <= 0)
                    continue;

                string name = parts[i][..eq];
                if (ScrubbedQueryKeys.Contains(name, StringComparer.OrdinalIgnoreCase))
                    parts[i] = name + "=" + Scrubbed;
            }

            return (leadingQuestionMark ? "?" : string.Empty) + string.Join("&", parts);
        }

        /// <summary>Applies the same scrubbing to the query part of a full URL.</summary>
        internal static string? ScrubUrl(string? url)
        {
            if (string.IsNullOrEmpty(url))
                return url;

            int q = url.IndexOf('?');
            if (q < 0)
                return url;

            return url[..q] + ScrubQueryString(url[q..]);
        }
    }
}
