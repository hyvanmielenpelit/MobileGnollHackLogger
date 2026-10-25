using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Overseer.Services;
using Sentry;
using Xunit;

namespace Overseer.Tests.UnitTests;

public class AuthSentryEventProcessorTests
{
    private HttpContext CreateAuthenticatedContext()
    {
        var context = new DefaultHttpContext();
        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "testuser") }, "TestAuth");
        context.User = new ClaimsPrincipal(identity);
        return context;
    }

    private HttpContext CreateUnauthenticatedContext()
    {
        return new DefaultHttpContext();
    }

    [Fact]
    public void Process_UnauthenticatedUser_DropsEvent()
    {
        var httpContextAccessor = new HttpContextAccessor { HttpContext = CreateUnauthenticatedContext() };
        var processor = new AuthSentryEventProcessor(httpContextAccessor);
        var @event = new SentryEvent();

        var result = processor.Process(@event);

        Assert.Null(result);
    }

    [Fact]
    public void Process_AuthenticatedUser_NormalException_PreservesEvent()
    {
        var httpContextAccessor = new HttpContextAccessor { HttpContext = CreateAuthenticatedContext() };
        var processor = new AuthSentryEventProcessor(httpContextAccessor);
        var @event = new SentryEvent(new InvalidOperationException("Something went wrong"));

        var result = processor.Process(@event);

        Assert.NotNull(result);
    }

    [Theory]
    [InlineData("Service unavailable", HttpStatusCode.ServiceUnavailable, true)] // Transient status, no URL.
    [InlineData("Error calling https://api.openai.com/v1/responses: 500 Internal Server Error", HttpStatusCode.InternalServerError, true)]
    [InlineData("Error calling internal service: 500 Internal Server Error", HttpStatusCode.InternalServerError, false)]
    [InlineData("Error calling https://api.github.com/repos/dotnet/maui: 500 Internal Server Error", HttpStatusCode.InternalServerError, true)]
    [InlineData("Connection failure connecting to https://api.github.com/repos/hyvanmielenpelit/GnollHack", null, true)]
    public void Process_HttpRequestException_DropsUpstreamFailuresAndKeepsOthers(string message, HttpStatusCode? status, bool expectedDropped)
    {
        var httpContextAccessor = new HttpContextAccessor { HttpContext = CreateAuthenticatedContext() };
        var processor = new AuthSentryEventProcessor(httpContextAccessor);

        var ex = new HttpRequestException(message, null, status);
        var @event = new SentryEvent(ex);

        var result = processor.Process(@event);

        Assert.Equal(expectedDropped, result == null);
    }

    [Theory]
    [InlineData("https://generativelanguage.googleapis.com/v1beta/models/gemini-3.7-flash:streamGenerateContent?alt=sse", "503", true)]
    [InlineData("https://api.anthropic.com/v1/messages", "429", true)]
    [InlineData("https://api.openai.com/v1/responses", "503", true)]
    [InlineData("https://internal-service.example.com/api/data", "503", false)]
    [InlineData("https://generativelanguage.googleapis.com/v1beta/models", "500", true)]
    [InlineData("https://api.anthropic.com/v1/messages", "501", true)]
    [InlineData("https://api.github.com/repos/hyvanmielenpelit/GnollHack/commits", "500", true)]
    [InlineData("https://api.github.com/search/issues?q=test", "503", true)]
    [InlineData("https://api.github.com/repos/dotnet/maui", "429", true)]
    public void Process_HttpFailedRequestHandler_DropsUpstreamFailuresAndKeepsOthers(string url, string status, bool expectedDropped)
    {
        var httpContextAccessor = new HttpContextAccessor { HttpContext = CreateAuthenticatedContext() };
        var processor = new AuthSentryEventProcessor(httpContextAccessor);

        var @event = new SentryEvent();
        @event.Request = new SentryRequest
        {
            Url = url
        };
        @event.SetTag("mechanism", "SentryHttpFailedRequestHandler");
        @event.SetTag("response.status_code", status);

        var result = processor.Process(@event);

        Assert.Equal(expectedDropped, result == null);
    }

    [Fact]
    public void Process_ScrubsCredentialBearingHeaders()
    {
        var httpContextAccessor = new HttpContextAccessor { HttpContext = CreateAuthenticatedContext() };
        var processor = new AuthSentryEventProcessor(httpContextAccessor);

        var @event = new SentryEvent(new InvalidOperationException("boom"));
        @event.Request = new SentryRequest { Url = "https://overseer.gnollhack.com/api/chat/send" };
        @event.Request.Headers["Authorization"] = "Bearer sk-ant-not-a-real-key";
        @event.Request.Headers["Cookie"] = ".AspNetCore.Identity.Application=abcdef";
        @event.Request.Headers["X-XSRF-TOKEN"] = "csrf-token-value";
        @event.Request.Headers["Accept"] = "application/json";
        @event.Request.Cookies = ".AspNetCore.Identity.Application=abcdef";

        var result = processor.Process(@event);

        Assert.NotNull(result);

        /* The property asserted is that the secret is gone, not the exact marker text: the
           SDK filters some header names itself on assignment (writing "[Filtered]"), so the
           marker varies by header and would make this test about the SDK's wording rather
           than about the credential leaving the process. */
        foreach (var name in new[] { "Authorization", "Cookie", "X-XSRF-TOKEN" })
        {
            string value = result!.Request!.Headers[name];
            Assert.DoesNotContain("sk-ant-not-a-real-key", value);
            Assert.DoesNotContain("abcdef", value);
            Assert.DoesNotContain("csrf-token-value", value);
        }

        Assert.DoesNotContain("abcdef", result!.Request!.Cookies ?? string.Empty);

        // A header that carries no credential is left alone: it is what makes a report useful.
        Assert.Equal("application/json", result.Request.Headers["Accept"]);
    }

    [Fact]
    public void Process_ScrubsNamedQueryValuesInBothQueryStringAndUrl()
    {
        var httpContextAccessor = new HttpContextAccessor { HttpContext = CreateAuthenticatedContext() };
        var processor = new AuthSentryEventProcessor(httpContextAccessor);

        var @event = new SentryEvent(new InvalidOperationException("boom"));
        @event.Request = new SentryRequest
        {
            Url = "https://overseer.gnollhack.com/api/auth/handoff?token=abc123&sessionId=42",
            QueryString = "?token=abc123&sessionId=42&apiKey=sk-live&password=hunter2"
        };

        var result = processor.Process(@event);

        Assert.NotNull(result);
        Assert.Equal("?token=[scrubbed]&sessionId=42&apiKey=[scrubbed]&password=[scrubbed]", result!.Request!.QueryString);
        Assert.Equal("https://overseer.gnollhack.com/api/auth/handoff?token=[scrubbed]&sessionId=42", result.Request.Url);
    }

    [Fact]
    public void ScrubQueryString_MatchesParameterNamesExactlyAndNotAsSubstrings()
    {
        /* "code" is on the list; "postcode" and "keyboard" are not. A substring match would
           blank both and quietly destroy the diagnostic value of the query. */
        Assert.Equal(
            "postcode=12345&code=[scrubbed]&keyboard=qwerty",
            AuthSentryEventProcessor.ScrubQueryString("postcode=12345&code=secret&keyboard=qwerty"));
    }

    [Fact]
    public void ScrubQueryString_LeavesAQueryWithoutSensitiveNamesUnchanged()
    {
        Assert.Equal("?sessionId=42&skip=0", AuthSentryEventProcessor.ScrubQueryString("?sessionId=42&skip=0"));
        Assert.Null(AuthSentryEventProcessor.ScrubQueryString(null));
        Assert.Equal(string.Empty, AuthSentryEventProcessor.ScrubQueryString(string.Empty));
    }

    [Fact]
    public void ScrubUrl_LeavesAUrlWithoutAQueryUnchanged()
    {
        Assert.Equal(
            "https://overseer.gnollhack.com/api/chat/send",
            AuthSentryEventProcessor.ScrubUrl("https://overseer.gnollhack.com/api/chat/send"));
    }

    [Fact]
    public void ScrubBreadcrumb_BlanksSensitiveQueryValuesInTheMessageAndData()
    {
        /* ASP.NET Core's "Request starting" log entry becomes a breadcrumb carrying the full
           URL, so the request scrubbing alone leaves the values readable in the event. */
        var breadcrumb = new Breadcrumb(
            "Request starting HTTP/2 GET https://overseer.gnollhack.com/api/auth/handoff?token=abc&postcode=00100&code=xyz - - -",
            "default",
            new Dictionary<string, string> { ["eventId"] = "1", ["url"] = "https://example.test/cb?Access_Token=t1#frag" },
            "Microsoft.AspNetCore.Hosting.Diagnostics",
            BreadcrumbLevel.Info);

        var result = AuthSentryEventProcessor.ScrubBreadcrumb(breadcrumb);

        Assert.Equal(
            "Request starting HTTP/2 GET https://overseer.gnollhack.com/api/auth/handoff?token=[scrubbed]&postcode=00100&code=[scrubbed] - - -",
            result.Message);
        Assert.Equal("1", result.Data!["eventId"]);
        Assert.Equal("https://example.test/cb?Access_Token=[scrubbed]#frag", result.Data["url"]);
        Assert.Equal("default", result.Type);
        Assert.Equal("Microsoft.AspNetCore.Hosting.Diagnostics", result.Category);
        Assert.Equal(BreadcrumbLevel.Info, result.Level);
    }

    [Fact]
    public void ScrubBreadcrumb_ReturnsABreadcrumbWithoutSensitiveValuesUnchanged()
    {
        var breadcrumb = new Breadcrumb(
            "Request starting HTTP/2 GET https://overseer.gnollhack.com/api/chat/sessions?keyboard=qwerty&postcode=00100 - - -",
            "default",
            new Dictionary<string, string> { ["eventId"] = "ExecutingEndpoint" });

        Assert.Same(breadcrumb, AuthSentryEventProcessor.ScrubBreadcrumb(breadcrumb));
    }

    [Fact]
    public void Process_NullsTheUserRecord()
    {
        var httpContextAccessor = new HttpContextAccessor { HttpContext = CreateAuthenticatedContext() };
        var processor = new AuthSentryEventProcessor(httpContextAccessor);

        var @event = new SentryEvent(new InvalidOperationException("boom"));
        @event.User.Email = "player@example.com";
        @event.User.Username = "player";
        @event.User.IpAddress = "203.0.113.7";

        var result = processor.Process(@event);

        Assert.NotNull(result);
        Assert.Null(result!.User.Email);
        Assert.Null(result.User.Username);
        Assert.Null(result.User.IpAddress);
    }

    [Fact]
    public void Process_WithNoHttpContext_KeepsTheEvent()
    {
        /* The unauthenticated drop is guarded by httpContext != null, so a crash raised
           outside any request -- a background task, or a streaming turn that outlived its
           request -- is reported rather than silently discarded. Later stages depend on this
           being the behaviour, so it is asserted rather than assumed. */
        var httpContextAccessor = new HttpContextAccessor { HttpContext = null };
        var processor = new AuthSentryEventProcessor(httpContextAccessor);

        var result = processor.Process(new SentryEvent(new InvalidOperationException("boom")));

        Assert.NotNull(result);
    }
}
