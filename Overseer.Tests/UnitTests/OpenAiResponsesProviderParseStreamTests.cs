using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
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

    private static string Sse(string eventType, object data)
    {
        return "event: " + eventType + "\n" +
               "data: " + JsonSerializer.Serialize(data) + "\n\n";
    }

    private static string MessageItem(string id, int outputIndex, params string[] deltas)
    {
        var sb = new StringBuilder();
        sb.Append(Sse("response.output_item.added", new
        {
            type = "response.output_item.added",
            output_index = outputIndex,
            item = new { id, type = "message", role = "assistant", content = Array.Empty<object>() }
        }));
        foreach (var delta in deltas)
        {
            sb.Append(Sse("response.output_text.delta", new
            {
                type = "response.output_text.delta",
                item_id = id,
                output_index = outputIndex,
                content_index = 0,
                delta
            }));
        }
        sb.Append(Sse("response.output_item.done", new
        {
            type = "response.output_item.done",
            output_index = outputIndex,
            item = new
            {
                id,
                type = "message",
                role = "assistant",
                content = new[] { new { type = "output_text", text = string.Concat(deltas) } }
            }
        }));
        return sb.ToString();
    }

    private static string FunctionCallItem(string callId, int outputIndex)
    {
        const string arguments = "{\"query\":\"Holy Grail\"}";
        return
            Sse("response.output_item.added", new
            {
                type = "response.output_item.added",
                output_index = outputIndex,
                item = new { id = "fc_" + callId, type = "function_call", call_id = callId, name = "wiki_search", arguments = "" }
            }) +
            Sse("response.function_call_arguments.done", new
            {
                type = "response.function_call_arguments.done",
                item_id = "fc_" + callId,
                output_index = outputIndex,
                call_id = callId,
                arguments
            }) +
            Sse("response.output_item.done", new
            {
                type = "response.output_item.done",
                output_index = outputIndex,
                item = new { id = "fc_" + callId, type = "function_call", call_id = callId, name = "wiki_search", arguments }
            });
    }

    private static string Completed()
    {
        return Sse("response.completed", new
        {
            type = "response.completed",
            response = new { id = "resp_items", status = "completed" }
        });
    }

    private static List<string> Chunks(List<ChatEvent> events)
    {
        return events.Where(e => e.Type == "chunk").Select(e => e.Data).ToList();
    }

    [Fact]
    public async Task ParseStreamAsync_TwoMessageItems_AreSeparatedByParagraphBreak()
    {
        var sse = MessageItem("msg_1", 0, "A") + MessageItem("msg_2", 1, "B") + Completed();

        var events = await ParseAsync(sse);

        Assert.Equal("A\n\nB", string.Concat(Chunks(events)));
    }

    [Fact]
    public async Task ParseStreamAsync_SingleMessageItem_ChunksAreUnchanged()
    {
        var sse = MessageItem("msg_1", 0, "Hello", "\n\nThe Grail is ", "complete.") + Completed();

        var events = await ParseAsync(sse);

        Assert.Equal(new[] { "Hello", "\n\nThe Grail is ", "complete." }, Chunks(events));
    }

    [Fact]
    public async Task ParseStreamAsync_MessageThenFunctionCall_AddsNoBreak()
    {
        var sse = MessageItem("msg_1", 0, "Let me check.") + FunctionCallItem("call_1", 1) + Completed();

        var events = await ParseAsync(sse);

        Assert.Equal("Let me check.", string.Concat(Chunks(events)));
        Assert.Single(events, e => e.Type == "tool_call_complete");
    }

    [Fact]
    public async Task ParseStreamAsync_MessageFunctionCallMessage_BreaksBetweenTheMessages()
    {
        // The function call adds no break of its own; the second message item does.
        var sse =
            MessageItem("msg_1", 0, "A") +
            FunctionCallItem("call_1", 1) +
            MessageItem("msg_2", 2, "B") +
            Completed();

        var events = await ParseAsync(sse);

        Assert.Equal("A\n\nB", string.Concat(Chunks(events)));
        Assert.Single(events, e => e.Type == "tool_call_complete");
    }

    [Fact]
    public async Task ParseStreamAsync_FirstItemEndsInParagraphBreak_AddsNoExtraBreak()
    {
        var sse = MessageItem("msg_1", 0, "A\n\n") + MessageItem("msg_2", 1, "B") + Completed();

        var events = await ParseAsync(sse);

        Assert.Equal("A\n\nB", string.Concat(Chunks(events)));
    }

    [Fact]
    public async Task ParseStreamAsync_FirstItemEndsInOneNewline_AddsOneNewline()
    {
        var sse = MessageItem("msg_1", 0, "A\n") + MessageItem("msg_2", 1, "B") + Completed();

        var events = await ParseAsync(sse);

        Assert.Equal("A\n\nB", string.Concat(Chunks(events)));
    }

    [Fact]
    public async Task ParseStreamAsync_SecondItemBeginsWithParagraphBreak_AddsNoExtraBreak()
    {
        var sse = MessageItem("msg_1", 0, "A") + MessageItem("msg_2", 1, "\n\nB") + Completed();

        var events = await ParseAsync(sse);

        Assert.Equal("A\n\nB", string.Concat(Chunks(events)));
    }

    [Fact]
    public async Task ParseStreamAsync_FirstItemTailHeldBackBySanitizer_IsReleasedBeforeTheBreak()
    {
        // A trailing "to" could begin a "to=" routing marker, so the sanitizer holds it back and
        // the text emitted so far ends in a newline that is not the end of the item's text.
        var sse = MessageItem("msg_1", 0, "Next steps:\nto") + MessageItem("msg_2", 1, "Invoke it.") + Completed();

        var events = await ParseAsync(sse);

        Assert.Equal("Next steps:\nto\n\nInvoke it.", string.Concat(Chunks(events)));
    }

    [Fact]
    public async Task ParseStreamAsync_SecondMessageItemWithoutText_AddsNoBreak()
    {
        var sse = MessageItem("msg_1", 0, "A") + MessageItem("msg_2", 1) + Completed();

        var events = await ParseAsync(sse);

        Assert.Equal("A", string.Concat(Chunks(events)));
    }
}
