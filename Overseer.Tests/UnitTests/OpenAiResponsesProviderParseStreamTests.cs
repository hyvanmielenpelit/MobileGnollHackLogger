using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Overseer.Services;
using Overseer.Services.Providers;
using Xunit;

namespace Overseer.Tests.UnitTests;

public class OpenAiResponsesProviderParseStreamTests
{
    private static IConfiguration CreateConfig()
    {
        return new ConfigurationBuilder().Build();
    }

    private static async Task<List<ChatEvent>> ParseAsync(string sse)
    {
        var provider = new OpenAiResponsesProvider(CreateConfig());
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(sse, System.Text.Encoding.UTF8, "text/event-stream")
        };

        var events = new List<ChatEvent>();
        await foreach (var evt in provider.ParseStreamAsync(response, showDebugLog: true, CancellationToken.None))
        {
            events.Add(evt);
        }
        return events;
    }

    [Fact]
    public async Task ParseStreamAsync_ResponseFailedWithNestedError_EmitsCodeAndMessage()
    {
        var sse =
            "event: response.failed\n" +
            "data: {\"type\":\"response.failed\",\"response\":{\"id\":\"resp_1\",\"status\":\"failed\",\"error\":{\"code\":\"server_error\",\"message\":\"Our servers are currently overloaded. Please try again later.\"}}}\n\n";

        var events = await ParseAsync(sse);

        var errors = events.Where(e => e.Type == "error").ToList();
        Assert.Single(errors);
        Assert.Equal(
            "OpenAI stream error: [server_error] Our servers are currently overloaded. Please try again later.",
            errors[0].Data);
        Assert.False(string.IsNullOrEmpty(errors[0].Detail));
        Assert.Contains("server_error", errors[0].Detail!);
    }

    [Fact]
    public async Task ParseStreamAsync_ResponseFailedWithoutErrorObject_FallsBackToStatus()
    {
        var sse =
            "event: response.failed\n" +
            "data: {\"type\":\"response.failed\",\"response\":{\"id\":\"resp_2\",\"status\":\"failed\"}}\n\n";

        var events = await ParseAsync(sse);

        var errors = events.Where(e => e.Type == "error").ToList();
        Assert.Single(errors);
        Assert.Equal("OpenAI stream error: [response.failed] status=failed", errors[0].Data);
        Assert.False(string.IsNullOrEmpty(errors[0].Detail));
    }

    [Fact]
    public async Task ParseStreamAsync_TopLevelErrorEventWithErrorObject_EmitsCodeAndMessage()
    {
        var sse =
            "event: error\n" +
            "data: {\"type\":\"error\",\"error\":{\"code\":\"rate_limit_exceeded\",\"message\":\"Rate limit reached for gpt-5.\"}}\n\n";

        var events = await ParseAsync(sse);

        var errors = events.Where(e => e.Type == "error").ToList();
        Assert.Single(errors);
        Assert.Equal("OpenAI stream error: [rate_limit_exceeded] Rate limit reached for gpt-5.", errors[0].Data);
        Assert.False(string.IsNullOrEmpty(errors[0].Detail));
    }

    [Fact]
    public async Task ParseStreamAsync_TopLevelErrorEventWithFlatFields_EmitsCodeAndMessage()
    {
        var sse =
            "event: error\n" +
            "data: {\"type\":\"error\",\"code\":\"server_error\",\"message\":\"The server had an error.\"}\n\n";

        var events = await ParseAsync(sse);

        var errors = events.Where(e => e.Type == "error").ToList();
        Assert.Single(errors);
        Assert.Equal("OpenAI stream error: [server_error] The server had an error.", errors[0].Data);
    }

    [Fact]
    public async Task ParseStreamAsync_OversizedFailurePayload_BoundsDetailTo3500Characters()
    {
        var longMessage = new string('x', 8000);
        var sse =
            "event: response.failed\n" +
            "data: {\"type\":\"response.failed\",\"response\":{\"status\":\"failed\",\"error\":{\"code\":\"server_error\",\"message\":\"" + longMessage + "\"}}}\n\n";

        var events = await ParseAsync(sse);

        var errors = events.Where(e => e.Type == "error").ToList();
        Assert.Single(errors);
        Assert.NotNull(errors[0].Detail);
        Assert.Equal(3500, errors[0].Detail!.Length);
    }

    [Fact]
    public async Task ParseStreamAsync_ResponseIncomplete_EmitsNoErrorEvent()
    {
        var sse =
            "event: response.incomplete\n" +
            "data: {\"type\":\"response.incomplete\",\"response\":{\"status\":\"incomplete\",\"incomplete_details\":{\"reason\":\"max_output_tokens\"}}}\n\n";

        var events = await ParseAsync(sse);

        Assert.DoesNotContain(events, e => e.Type == "error");

        var finishReasons = events.Where(e => e.Type == "finish_reason").ToList();
        Assert.Single(finishReasons);
        Assert.Equal("max_output_tokens", finishReasons[0].Data);

        var debugs = events.Where(e => e.Type == "debug").ToList();
        Assert.Contains(debugs, d => d.Data.Contains("Response incomplete: reason=max_output_tokens"));
    }

    [Fact]
    public async Task ParseStreamAsync_MalformedFailurePayload_DoesNotThrow()
    {
        var sse =
            "event: response.failed\n" +
            "data: {\"type\":\"response.failed\",\"response\":\"not-an-object\"}\n\n";

        var events = await ParseAsync(sse);

        var errors = events.Where(e => e.Type == "error").ToList();
        Assert.Single(errors);
        Assert.Equal("OpenAI stream error: [response.failed] Unknown error", errors[0].Data);
    }
}
