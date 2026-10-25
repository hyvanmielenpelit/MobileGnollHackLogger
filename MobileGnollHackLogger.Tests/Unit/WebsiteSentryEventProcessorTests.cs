using Microsoft.AspNetCore.Http;
using MobileGnollHackLogger.Monitoring;
using Sentry;

namespace MobileGnollHackLogger.Tests.Unit;

public class WebsiteSentryEventProcessorTests
{
    private const string Scrubbed = "[scrubbed]";

    [Fact]
    public void ScrubQueryString_ScrubsNamedKeysCaseInsensitively_KeepsOthers()
    {
        Assert.Equal("?token=[scrubbed]&postcode=1&Email=[scrubbed]",
            WebsiteSentryEventProcessor.ScrubQueryString("?token=a&postcode=1&Email=x"));
    }

    [Fact]
    public void ScrubQueryString_WithoutLeadingQuestionMark_AddsNone()
    {
        Assert.Equal("token=[scrubbed]&page=2", WebsiteSentryEventProcessor.ScrubQueryString("token=a&page=2"));
    }

    [Fact]
    public void ScrubQueryString_PartsWithoutName_AreLeftAlone()
    {
        Assert.Equal("?flag&token=[scrubbed]&=x", WebsiteSentryEventProcessor.ScrubQueryString("?flag&token=a&=x"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ScrubQueryString_NullOrEmpty_PassesThrough(string? queryString)
    {
        Assert.Equal(queryString, WebsiteSentryEventProcessor.ScrubQueryString(queryString));
    }

    [Fact]
    public void ScrubUrl_WithoutQuery_IsUnchanged()
    {
        Assert.Equal("https://localhost/Identity/Account/Login", WebsiteSentryEventProcessor.ScrubUrl("https://localhost/Identity/Account/Login"));
    }

    [Fact]
    public void ScrubUrl_WithQuery_ScrubsQueryAndKeepsPath()
    {
        Assert.Equal("https://localhost/Identity/Account/ResetPassword?code=[scrubbed]&page=1",
            WebsiteSentryEventProcessor.ScrubUrl("https://localhost/Identity/Account/ResetPassword?code=abc&page=1"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ScrubUrl_NullOrEmpty_PassesThrough(string? url)
    {
        Assert.Equal(url, WebsiteSentryEventProcessor.ScrubUrl(url));
    }

    [Fact]
    public void ScrubText_UrlInsideFreeText_IsScrubbed()
    {
        Assert.Equal("Request starting HTTP/1.1 GET https://localhost/Identity/Account/ConfirmEmail?userId=[scrubbed]&code=[scrubbed] - -",
            WebsiteSentryEventProcessor.ScrubText("Request starting HTTP/1.1 GET https://localhost/Identity/Account/ConfirmEmail?userId=abc&code=xyz - -"));
    }

    [Fact]
    public void ScrubText_Postcode_IsNotMatchedAsCode()
    {
        Assert.Equal("GET https://localhost/x?postcode=00100&code=[scrubbed]",
            WebsiteSentryEventProcessor.ScrubText("GET https://localhost/x?postcode=00100&code=abc"));
        Assert.Equal("postcode=00100", WebsiteSentryEventProcessor.ScrubText("postcode=00100"));
    }

    [Fact]
    public void ScrubBreadcrumb_NothingToScrub_ReturnsSameInstance()
    {
        var breadcrumb = new Breadcrumb("GET https://localhost/TopScores?mode=normal", "http",
            new Dictionary<string, string> { ["url"] = "https://localhost/TopScores?mode=normal" }, "http", BreadcrumbLevel.Info);

        Assert.Same(breadcrumb, WebsiteSentryEventProcessor.ScrubBreadcrumb(breadcrumb));
    }

    [Fact]
    public void ScrubBreadcrumb_SensitiveMessageAndData_AreScrubbed()
    {
        var breadcrumb = new Breadcrumb("GET https://localhost/x?token=t&page=2", "http",
            new Dictionary<string, string>
            {
                ["url"] = "https://localhost/y?email=p@example.invalid&page=3",
                ["method"] = "GET"
            }, "http", BreadcrumbLevel.Warning);

        var result = WebsiteSentryEventProcessor.ScrubBreadcrumb(breadcrumb);

        Assert.NotSame(breadcrumb, result);
        Assert.NotNull(result.Message);
        Assert.Equal("GET https://localhost/x?token=[scrubbed]&page=2", result.Message);
        Assert.NotNull(result.Data);
        Assert.Equal("https://localhost/y?email=[scrubbed]&page=3", result.Data["url"]);
        Assert.Equal("GET", result.Data["method"]);
        Assert.Equal("http", result.Type);
        Assert.Equal("http", result.Category);
        Assert.Equal(BreadcrumbLevel.Warning, result.Level);
    }

    [Theory]
    [InlineData("direct")]
    [InlineData("inner")]
    [InlineData("aggregate")]
    public void Process_BadHttpRequestException_DropsEvent(string shape)
    {
        var bad = new Microsoft.AspNetCore.Http.BadHttpRequestException("Unexpected end of request content.");
        Exception exception = shape switch
        {
            "direct" => bad,
            "inner" => new InvalidOperationException("Reading the form failed.", bad),
            _ => new AggregateException(new InvalidOperationException("Unrelated."), bad)
        };
        var processor = new WebsiteSentryEventProcessor(new HttpContextAccessor());

        Assert.Null(processor.Process(new SentryEvent(exception)));
    }

    [Theory]
    [InlineData("OperationCanceled")]
    [InlineData("IO")]
    public void Process_AbortException_WhileRequestAborted_DropsEvent(string kind)
    {
        var processor = new WebsiteSentryEventProcessor(Accessor(aborted: true));

        Assert.Null(processor.Process(new SentryEvent(AbortException(kind))));
    }

    [Theory]
    [InlineData("OperationCanceled")]
    [InlineData("IO")]
    public void Process_AbortException_OnLiveRequest_KeepsEvent(string kind)
    {
        var processor = new WebsiteSentryEventProcessor(Accessor(aborted: false));
        var @event = new SentryEvent(AbortException(kind));

        Assert.Same(@event, processor.Process(@event));
    }

    [Theory]
    [InlineData("OperationCanceled")]
    [InlineData("IO")]
    public void Process_AbortException_WithoutHttpContext_KeepsEvent(string kind)
    {
        var processor = new WebsiteSentryEventProcessor(new HttpContextAccessor());
        var @event = new SentryEvent(AbortException(kind));

        Assert.Same(@event, processor.Process(@event));
    }

    [Fact]
    public void Process_OtherException_WhileRequestAborted_KeepsEvent()
    {
        var processor = new WebsiteSentryEventProcessor(Accessor(aborted: true));
        var @event = new SentryEvent(new InvalidOperationException("A genuine failure."));

        Assert.Same(@event, processor.Process(@event));
    }

    [Fact]
    public void Process_KeptEvent_ScrubsHeadersCookiesQueryUserAndTags()
    {
        var request = new SentryRequest
        {
            Url = "https://localhost/Identity/Account/ConfirmEmail?userId=abc&code=xyz&page=1",
            QueryString = "?userId=abc&code=xyz&page=1",
            Cookies = ".AspNetCore.Identity.Application=secret"
        };
        request.Headers["Authorization"] = "Bearer secret";
        request.Headers["Cookie"] = ".AspNetCore.Identity.Application=secret";
        request.Headers["Set-Cookie"] = "a=b";
        request.Headers["Proxy-Authorization"] = "Basic secret";
        request.Headers["X-XSRF-TOKEN"] = "xsrf";
        request.Headers["X-Api-Key"] = "key";
        request.Headers["X-Sentry-Auth"] = "sentry";
        request.Headers["Accept"] = "text/html";

        var @event = new SentryEvent(new InvalidOperationException("A genuine failure."))
        {
            Request = request,
            User = new SentryUser
            {
                Id = "user-id",
                Email = "player@example.invalid",
                Username = "player",
                IpAddress = "192.0.2.1"
            }
        };
        @event.SetTag("token", "secret-token");
        @event.SetTag("email", "player@example.invalid");
        @event.SetTag("region", "eu");

        var processor = new WebsiteSentryEventProcessor(Accessor(aborted: false));
        var result = processor.Process(@event);

        Assert.NotNull(result);
        Assert.Same(@event, result);
        foreach (var name in new[] { "Cookie", "Set-Cookie", "X-XSRF-TOKEN", "X-Api-Key", "X-Sentry-Auth" })
        {
            Assert.Equal(Scrubbed, result.Request.Headers[name]);
        }
        // The SDK's header dictionary stores these two as its own filter marker, whatever value is written.
        foreach (var name in new[] { "Authorization", "Proxy-Authorization" })
        {
            Assert.DoesNotContain("secret", result.Request.Headers[name]);
        }
        Assert.Equal("text/html", result.Request.Headers["Accept"]);
        Assert.Equal(Scrubbed, result.Request.Cookies);
        Assert.Equal("?userId=[scrubbed]&code=[scrubbed]&page=1", result.Request.QueryString);
        Assert.Equal("https://localhost/Identity/Account/ConfirmEmail?userId=[scrubbed]&code=[scrubbed]&page=1", result.Request.Url);

        Assert.Null(result.User.Email);
        Assert.Null(result.User.Username);
        Assert.Null(result.User.IpAddress);

        Assert.Equal(Scrubbed, result.Tags["token"]);
        Assert.Equal(Scrubbed, result.Tags["email"]);
        Assert.Equal("eu", result.Tags["region"]);
    }

    private static IHttpContextAccessor Accessor(bool aborted)
    {
        var context = new DefaultHttpContext
        {
            RequestAborted = aborted ? new CancellationToken(canceled: true) : CancellationToken.None
        };
        return new HttpContextAccessor { HttpContext = context };
    }

    private static Exception AbortException(string kind) => kind switch
    {
        "OperationCanceled" => new OperationCanceledException("The request was aborted."),
        _ => new IOException("The client disconnected.")
    };
}
