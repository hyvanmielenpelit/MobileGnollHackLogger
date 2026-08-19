using System;
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

    [Fact]
    public void Process_AuthenticatedUser_TransientHttpRequestException_DropsEvent()
    {
        var httpContextAccessor = new HttpContextAccessor { HttpContext = CreateAuthenticatedContext() };
        var processor = new AuthSentryEventProcessor(httpContextAccessor);
        var httpEx = new HttpRequestException("Service unavailable", null, HttpStatusCode.ServiceUnavailable);
        var @event = new SentryEvent(httpEx);

        var result = processor.Process(@event);

        Assert.Null(result);
    }

    [Fact]
    public void Process_HttpFailedRequestHandler_AiProvider503_DropsEvent()
    {
        var httpContextAccessor = new HttpContextAccessor { HttpContext = CreateAuthenticatedContext() };
        var processor = new AuthSentryEventProcessor(httpContextAccessor);
        
        var @event = new SentryEvent();
        @event.Request = new SentryRequest
        {
            Url = "https://generativelanguage.googleapis.com/v1beta/models/gemini-3.7-flash:streamGenerateContent?alt=sse"
        };
        @event.SetTag("mechanism", "SentryHttpFailedRequestHandler");
        @event.SetTag("response.status_code", "503");

        var result = processor.Process(@event);

        Assert.Null(result);
    }

    [Fact]
    public void Process_HttpFailedRequestHandler_AiProvider429_DropsEvent()
    {
        var httpContextAccessor = new HttpContextAccessor { HttpContext = CreateAuthenticatedContext() };
        var processor = new AuthSentryEventProcessor(httpContextAccessor);
        
        var @event = new SentryEvent();
        @event.Request = new SentryRequest
        {
            Url = "https://api.anthropic.com/v1/messages"
        };
        @event.SetTag("mechanism", "SentryHttpFailedRequestHandler");
        @event.SetTag("response.status_code", "429");

        var result = processor.Process(@event);

        Assert.Null(result);
    }

    [Fact]
    public void Process_HttpFailedRequestHandler_OpenAi503_DropsEvent()
    {
        var httpContextAccessor = new HttpContextAccessor { HttpContext = CreateAuthenticatedContext() };
        var processor = new AuthSentryEventProcessor(httpContextAccessor);
        
        var @event = new SentryEvent();
        @event.Request = new SentryRequest
        {
            Url = "https://api.openai.com/v1/responses"
        };
        @event.SetTag("mechanism", "SentryHttpFailedRequestHandler");
        @event.SetTag("response.status_code", "503");

        var result = processor.Process(@event);

        Assert.Null(result);
    }

    [Fact]
    public void Process_HttpFailedRequestHandler_NonAiUrl503_PreservesEvent()
    {
        var httpContextAccessor = new HttpContextAccessor { HttpContext = CreateAuthenticatedContext() };
        var processor = new AuthSentryEventProcessor(httpContextAccessor);
        
        var @event = new SentryEvent();
        @event.Request = new SentryRequest
        {
            Url = "https://internal-service.example.com/api/data"
        };
        @event.SetTag("mechanism", "SentryHttpFailedRequestHandler");
        @event.SetTag("response.status_code", "503");

        var result = processor.Process(@event);

        Assert.NotNull(result);
    }

    [Fact]
    public void Process_HttpFailedRequestHandler_AiProvider500_DropsEvent()
    {
        var httpContextAccessor = new HttpContextAccessor { HttpContext = CreateAuthenticatedContext() };
        var processor = new AuthSentryEventProcessor(httpContextAccessor);
        
        var @event = new SentryEvent();
        @event.Request = new SentryRequest
        {
            Url = "https://generativelanguage.googleapis.com/v1beta/models"
        };
        @event.SetTag("mechanism", "SentryHttpFailedRequestHandler");
        @event.SetTag("response.status_code", "500");

        var result = processor.Process(@event);

        Assert.Null(result);
    }

    [Fact]
    public void Process_HttpFailedRequestHandler_AiProvider501_DropsEvent()
    {
        var httpContextAccessor = new HttpContextAccessor { HttpContext = CreateAuthenticatedContext() };
        var processor = new AuthSentryEventProcessor(httpContextAccessor);
        
        var @event = new SentryEvent();
        @event.Request = new SentryRequest
        {
            Url = "https://api.anthropic.com/v1/messages"
        };
        @event.SetTag("mechanism", "SentryHttpFailedRequestHandler");
        @event.SetTag("response.status_code", "501");

        var result = processor.Process(@event);

        Assert.Null(result);
    }

    [Fact]
    public void Process_HttpRequestException_AiProvider500_DropsEvent()
    {
        var httpContextAccessor = new HttpContextAccessor { HttpContext = CreateAuthenticatedContext() };
        var processor = new AuthSentryEventProcessor(httpContextAccessor);
        
        var ex = new HttpRequestException("Error calling https://api.openai.com/v1/responses: 500 Internal Server Error", null, HttpStatusCode.InternalServerError);
        var @event = new SentryEvent(ex);

        var result = processor.Process(@event);

        Assert.Null(result);
    }

    [Fact]
    public void Process_HttpRequestException_NonAiProvider500_PreservesEvent()
    {
        var httpContextAccessor = new HttpContextAccessor { HttpContext = CreateAuthenticatedContext() };
        var processor = new AuthSentryEventProcessor(httpContextAccessor);
        
        var ex = new HttpRequestException("Error calling internal service: 500 Internal Server Error", null, HttpStatusCode.InternalServerError);
        var @event = new SentryEvent(ex);

        var result = processor.Process(@event);

        Assert.NotNull(result);
    }

    [Fact]
    public void Process_HttpFailedRequestHandler_GitHub500_DropsEvent()
    {
        var httpContextAccessor = new HttpContextAccessor { HttpContext = CreateAuthenticatedContext() };
        var processor = new AuthSentryEventProcessor(httpContextAccessor);
        
        var @event = new SentryEvent();
        @event.Request = new SentryRequest
        {
            Url = "https://api.github.com/repos/hyvanmielenpelit/GnollHack/commits"
        };
        @event.SetTag("mechanism", "SentryHttpFailedRequestHandler");
        @event.SetTag("response.status_code", "500");

        var result = processor.Process(@event);

        Assert.Null(result);
    }

    [Fact]
    public void Process_HttpFailedRequestHandler_GitHub503_DropsEvent()
    {
        var httpContextAccessor = new HttpContextAccessor { HttpContext = CreateAuthenticatedContext() };
        var processor = new AuthSentryEventProcessor(httpContextAccessor);
        
        var @event = new SentryEvent();
        @event.Request = new SentryRequest
        {
            Url = "https://api.github.com/search/issues?q=test"
        };
        @event.SetTag("mechanism", "SentryHttpFailedRequestHandler");
        @event.SetTag("response.status_code", "503");

        var result = processor.Process(@event);

        Assert.Null(result);
    }

    [Fact]
    public void Process_HttpFailedRequestHandler_GitHub429_DropsEvent()
    {
        var httpContextAccessor = new HttpContextAccessor { HttpContext = CreateAuthenticatedContext() };
        var processor = new AuthSentryEventProcessor(httpContextAccessor);
        
        var @event = new SentryEvent();
        @event.Request = new SentryRequest
        {
            Url = "https://api.github.com/repos/dotnet/maui"
        };
        @event.SetTag("mechanism", "SentryHttpFailedRequestHandler");
        @event.SetTag("response.status_code", "429");

        var result = processor.Process(@event);

        Assert.Null(result);
    }

    [Fact]
    public void Process_HttpRequestException_GitHub500_DropsEvent()
    {
        var httpContextAccessor = new HttpContextAccessor { HttpContext = CreateAuthenticatedContext() };
        var processor = new AuthSentryEventProcessor(httpContextAccessor);
        
        var ex = new HttpRequestException("Error calling https://api.github.com/repos/dotnet/maui: 500 Internal Server Error", null, HttpStatusCode.InternalServerError);
        var @event = new SentryEvent(ex);

        var result = processor.Process(@event);

        Assert.Null(result);
    }

    [Fact]
    public void Process_HttpRequestException_GitHubConnectionFailure_DropsEvent()
    {
        var httpContextAccessor = new HttpContextAccessor { HttpContext = CreateAuthenticatedContext() };
        var processor = new AuthSentryEventProcessor(httpContextAccessor);
        
        var ex = new HttpRequestException("Connection failure connecting to https://api.github.com/repos/hyvanmielenpelit/GnollHack");
        var @event = new SentryEvent(ex);

        var result = processor.Process(@event);

        Assert.Null(result);
    }
}
