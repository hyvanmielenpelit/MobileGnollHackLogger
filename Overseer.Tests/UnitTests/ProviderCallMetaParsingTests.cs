using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Overseer.Services;
using Overseer.Services.Providers;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// The <c>call_meta</c> event each provider's <c>ParseStreamAsync</c> yields last, read from canned
/// SSE streams; the <see cref="ProviderCallMeta"/> mark arithmetic; and
/// <see cref="ProviderResponseHeaders.From"/>.
/// </summary>
public class ProviderCallMetaParsingTests
{
    private static IConfiguration EmptyConfig() => new ConfigurationBuilder().Build();

    private static async Task<List<ChatEvent>> ParseAsync(IAiProvider provider, string sse)
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(sse, Encoding.UTF8, "text/event-stream")
        };

        var events = new List<ChatEvent>();
        await foreach (var evt in provider.ParseStreamAsync(response, showDebugLog: true, TestContext.Current.CancellationToken))
        {
            events.Add(evt);
        }
        return events;
    }

    /// <summary>Asserts <c>call_meta</c> appears exactly once, as the last event, and returns its meta.</summary>
    private static ProviderCallMeta TakeMeta(List<ChatEvent> events)
    {
        Assert.Single(events, e => e.Type == "call_meta");
        Assert.Equal("call_meta", events[^1].Type);
        Assert.NotNull(events[^1].CallMeta);
        return events[^1].CallMeta!;
    }

    private static void AssertCallMetaAfterUsage(List<ChatEvent> events)
    {
        int usageIndex = events.FindIndex(e => e.Type == "usage");
        int metaIndex = events.FindIndex(e => e.Type == "call_meta");
        Assert.True(usageIndex >= 0, "the stream yielded no usage event");
        Assert.True(usageIndex < metaIndex, $"usage at {usageIndex} is not before call_meta at {metaIndex}");
    }

    private static void AssertReasoningThenTextMarks(ProviderCallMeta meta)
    {
        Assert.NotNull(meta.FirstEventTicks);
        Assert.NotNull(meta.FirstReasoningTicks);
        Assert.NotNull(meta.FirstOutputTicks);
        Assert.NotNull(meta.LastDeltaTicks);
        Assert.NotNull(meta.CompletedTicks);
        Assert.Null(meta.FirstToolCallTicks);

        Assert.True(meta.FirstEventTicks <= meta.FirstReasoningTicks);
        Assert.True(meta.FirstReasoningTicks <= meta.FirstOutputTicks);
        Assert.True(meta.FirstOutputTicks <= meta.LastDeltaTicks);
        Assert.True(meta.LastDeltaTicks <= meta.CompletedTicks);
    }

    // Raw delta texts; the counts are of these characters, not of what the sanitizer passes on.
    private const string Delta1 = "Hello";
    private const string Delta2 = ", world";

    // --- OpenAI ----------------------------------------------------------------------------------

    private static string OpenAiEvent(string type, string json) => $"event: {type}\ndata: {json}\n\n";

    private static string OpenAiTextDelta(string text) =>
        OpenAiEvent("response.output_text.delta", "{\"type\":\"response.output_text.delta\",\"delta\":" + JsonSerializer.Serialize(text) + "}");

    private const string OpenAiCompleted =
        """{"type":"response.completed","response":{"id":"resp_final","model":"gpt-x-final","status":"completed","usage":{"input_tokens":50,"input_tokens_details":{"cached_tokens":0},"output_tokens":20,"output_tokens_details":{"reasoning_tokens":8}}}}""";

    private static string OpenAiReasoningThenTextStream() =>
        OpenAiEvent("response.created", """{"type":"response.created","response":{"id":"resp_created","model":"gpt-x-created","status":"in_progress"}}""")
        + OpenAiEvent("response.output_item.added", """{"type":"response.output_item.added","item":{"type":"reasoning","id":"rs_1"}}""")
        + OpenAiEvent("response.reasoning_summary_text.delta", """{"type":"response.reasoning_summary_text.delta","delta":"Thinking it over."}""")
        + OpenAiTextDelta(Delta1)
        + OpenAiTextDelta(Delta2)
        + OpenAiEvent("response.completed", OpenAiCompleted);

    [Fact]
    public async Task OpenAi_ReasoningThenText_ReadsIdentityMarksAndRawTextCounts()
    {
        var events = await ParseAsync(new OpenAiResponsesProvider(EmptyConfig()), OpenAiReasoningThenTextStream());
        var meta = TakeMeta(events);

        // response.created names the call; the completion's identity overwrites it.
        Assert.Equal("gpt-x-final", meta.ServedModelId);
        Assert.Equal("resp_final", meta.ResponseId);
        Assert.False(meta.IsRefusal);

        AssertReasoningThenTextMarks(meta);
        Assert.Equal(2, meta.OutputDeltaCount);
        Assert.Equal(Delta1.Length + Delta2.Length, meta.VisibleOutputChars);
        Assert.Equal(new[] { Delta1.Length, Delta2.Length }, meta.TextDeltaMarks.Select(m => m.Chars));
        AssertCallMetaAfterUsage(events);
    }

    [Fact]
    public async Task OpenAi_ResponseCreatedOnly_ReadsIdentityFromIt()
    {
        var sse = OpenAiEvent("response.created", """{"type":"response.created","response":{"id":"resp_created","model":"gpt-x-created","status":"in_progress"}}""")
            + OpenAiTextDelta(Delta1);

        var meta = TakeMeta(await ParseAsync(new OpenAiResponsesProvider(EmptyConfig()), sse));

        Assert.Equal("gpt-x-created", meta.ServedModelId);
        Assert.Equal("resp_created", meta.ResponseId);
        Assert.Null(meta.CompletedTicks);
    }

    [Fact]
    public async Task OpenAi_ToolCallStream_SetsFirstToolCallAndNoVisibleChars()
    {
        var sse =
            OpenAiEvent("response.output_item.added", """{"type":"response.output_item.added","item":{"type":"function_call","call_id":"call_1","name":"wiki_search"}}""")
            + OpenAiEvent("response.function_call_arguments.delta", """{"type":"response.function_call_arguments.delta","call_id":"call_1","delta":"{\"q\":"}""")
            + OpenAiEvent("response.function_call_arguments.delta", """{"type":"response.function_call_arguments.delta","call_id":"call_1","delta":"\"x\"}"}""")
            + OpenAiEvent("response.output_item.done", """{"type":"response.output_item.done","item":{"type":"function_call","call_id":"call_1","name":"wiki_search","arguments":"{\"q\":\"x\"}"}}""")
            + OpenAiEvent("response.completed", OpenAiCompleted);

        var events = await ParseAsync(new OpenAiResponsesProvider(EmptyConfig()), sse);
        var meta = TakeMeta(events);

        Assert.Contains(events, e => e.Type == "tool_call_complete");
        Assert.NotNull(meta.FirstToolCallTicks);
        Assert.Equal(meta.FirstToolCallTicks, meta.FirstOutputTicks);
        Assert.True(meta.FirstToolCallTicks <= meta.LastDeltaTicks);
        Assert.True(meta.LastDeltaTicks <= meta.CompletedTicks);
        Assert.Equal(3, meta.OutputDeltaCount);
        Assert.Equal(0, meta.VisibleOutputChars);
        Assert.Empty(meta.TextDeltaMarks);
        Assert.Null(meta.FirstReasoningTicks);
    }

    [Fact]
    public async Task OpenAi_RefusalDelta_SetsIsRefusal()
    {
        var sse =
            OpenAiEvent("response.refusal.delta", """{"type":"response.refusal.delta","delta":"I can't help with that."}""")
            + OpenAiEvent("response.completed", OpenAiCompleted);

        var meta = TakeMeta(await ParseAsync(new OpenAiResponsesProvider(EmptyConfig()), sse));

        Assert.True(meta.IsRefusal);
    }

    // --- Anthropic -------------------------------------------------------------------------------

    private static string Data(string json) => $"data: {json}\n\n";

    private static string AnthropicTextDelta(int index, string text) =>
        Data("{\"type\":\"content_block_delta\",\"index\":" + index + ",\"delta\":{\"type\":\"text_delta\",\"text\":" + JsonSerializer.Serialize(text) + "}}");

    private const string AnthropicMessageStart =
        """{"type":"message_start","message":{"id":"msg_1","type":"message","role":"assistant","model":"claude-x-served","content":[],"usage":{"input_tokens":40,"output_tokens":1}}}""";

    private static string AnthropicMessageEnd(string stopReason, string extraUsage = "") =>
        Data("{\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"" + stopReason + "\"},\"usage\":{\"output_tokens\":20" + extraUsage + "}}")
        + Data("""{"type":"message_stop"}""");

    [Fact]
    public async Task Anthropic_ThinkingThenText_ReadsIdentityMarksAndRawTextCounts()
    {
        var sse =
            Data(AnthropicMessageStart)
            + Data("""{"type":"content_block_start","index":0,"content_block":{"type":"thinking","thinking":""}}""")
            + Data("""{"type":"content_block_delta","index":0,"delta":{"type":"thinking_delta","thinking":"Hmm."}}""")
            + Data("""{"type":"content_block_delta","index":0,"delta":{"type":"signature_delta","signature":"sig"}}""")
            + Data("""{"type":"content_block_stop","index":0}""")
            + Data("""{"type":"content_block_start","index":1,"content_block":{"type":"text","text":""}}""")
            + AnthropicTextDelta(1, Delta1)
            + AnthropicTextDelta(1, Delta2)
            + Data("""{"type":"content_block_stop","index":1}""")
            + AnthropicMessageEnd("end_turn");

        var events = await ParseAsync(new AnthropicProvider(EmptyConfig()), sse);
        var meta = TakeMeta(events);

        Assert.Equal("claude-x-served", meta.ServedModelId);
        Assert.Equal("msg_1", meta.ResponseId);
        Assert.False(meta.IsRefusal);
        Assert.Null(meta.ServedSpeed);
        Assert.Null(meta.FallbackModelId);

        AssertReasoningThenTextMarks(meta);
        Assert.Equal(2, meta.OutputDeltaCount);
        Assert.Equal(Delta1.Length + Delta2.Length, meta.VisibleOutputChars);
        AssertCallMetaAfterUsage(events);
    }

    [Fact]
    public async Task Anthropic_ToolUseStream_SetsFirstToolCall()
    {
        var sse =
            Data(AnthropicMessageStart)
            + Data("""{"type":"content_block_start","index":0,"content_block":{"type":"tool_use","id":"toolu_1","name":"wiki_search","input":{}}}""")
            + Data("""{"type":"content_block_delta","index":0,"delta":{"type":"input_json_delta","partial_json":"{\"q\":\"x\"}"}}""")
            + Data("""{"type":"content_block_stop","index":0}""")
            + AnthropicMessageEnd("tool_use");

        var events = await ParseAsync(new AnthropicProvider(EmptyConfig()), sse);
        var meta = TakeMeta(events);

        Assert.Contains(events, e => e.Type == "tool_call_complete");
        Assert.NotNull(meta.FirstToolCallTicks);
        Assert.Equal(meta.FirstToolCallTicks, meta.FirstOutputTicks);
        Assert.Equal(2, meta.OutputDeltaCount);
        Assert.Equal(0, meta.VisibleOutputChars);
    }

    [Fact]
    public async Task Anthropic_RefusalStopReason_SetsIsRefusal()
    {
        var sse = Data(AnthropicMessageStart) + AnthropicMessageEnd("refusal");

        var events = await ParseAsync(new AnthropicProvider(EmptyConfig()), sse);
        var meta = TakeMeta(events);

        Assert.True(meta.IsRefusal);
        Assert.NotNull(meta.CompletedTicks);
        Assert.Contains(events, e => e.Type == "finish_reason" && e.Data == "refusal");
    }

    [Fact]
    public async Task Anthropic_UsageSpeed_SetsServedSpeed()
    {
        var sse = Data(AnthropicMessageStart) + AnthropicMessageEnd("end_turn", ",\"speed\":\"fast\"");

        var meta = TakeMeta(await ParseAsync(new AnthropicProvider(EmptyConfig()), sse));

        Assert.Equal("fast", meta.ServedSpeed);
    }

    [Fact]
    public async Task Anthropic_FallbackIteration_SetsFallbackModelId()
    {
        const string iterations =
            ",\"iterations\":[{\"type\":\"message\",\"model\":\"claude-x-served\"},{\"type\":\"fallback\",\"model\":\"claude-x-fallback\"}]";
        var sse = Data(AnthropicMessageStart) + AnthropicMessageEnd("end_turn", iterations);

        var meta = TakeMeta(await ParseAsync(new AnthropicProvider(EmptyConfig()), sse));

        Assert.Equal("claude-x-fallback", meta.FallbackModelId);
        Assert.Equal("claude-x-served", meta.ServedModelId);
    }

    // --- Google ----------------------------------------------------------------------------------

    private static string GoogleTextChunk(string text, string extra = "") =>
        Data("{\"candidates\":[{\"content\":{\"role\":\"model\",\"parts\":[{\"text\":" + JsonSerializer.Serialize(text) + "}]}" + extra + "}],"
            + "\"modelVersion\":\"gemini-x-served\",\"responseId\":\"google-resp-1\"}");

    [Fact]
    public async Task Google_ThoughtThenText_ReadsIdentityMarksAndRawTextCounts()
    {
        var sse =
            Data("""{"candidates":[{"content":{"role":"model","parts":[{"text":"Pondering.","thought":true,"thoughtSignature":"sig"}]}}],"modelVersion":"gemini-x-served","responseId":"google-resp-1"}""")
            + GoogleTextChunk(Delta1)
            + Data("{\"candidates\":[{\"content\":{\"role\":\"model\",\"parts\":[{\"text\":" + JsonSerializer.Serialize(Delta2) + "}]},\"finishReason\":\"STOP\"}],"
                + "\"usageMetadata\":{\"promptTokenCount\":100,\"candidatesTokenCount\":5,\"totalTokenCount\":105},"
                + "\"modelVersion\":\"gemini-x-served\",\"responseId\":\"google-resp-1\"}");

        var events = await ParseAsync(new GoogleProvider(EmptyConfig()), sse);
        var meta = TakeMeta(events);

        Assert.Equal("gemini-x-served", meta.ServedModelId);
        Assert.Equal("google-resp-1", meta.ResponseId);
        Assert.False(meta.IsRefusal);

        AssertReasoningThenTextMarks(meta);
        Assert.Equal(2, meta.OutputDeltaCount);
        Assert.Equal(Delta1.Length + Delta2.Length, meta.VisibleOutputChars);
        AssertCallMetaAfterUsage(events);
    }

    [Fact]
    public async Task Google_FunctionCallPart_SetsFirstToolCall()
    {
        var sse = Data("""{"candidates":[{"content":{"role":"model","parts":[{"functionCall":{"name":"wiki_search","args":{"q":"x"}}}]},"finishReason":"STOP"}],"modelVersion":"gemini-x-served"}""");

        var events = await ParseAsync(new GoogleProvider(EmptyConfig()), sse);
        var meta = TakeMeta(events);

        Assert.Contains(events, e => e.Type == "tool_call_complete");
        Assert.NotNull(meta.FirstToolCallTicks);
        Assert.Equal(meta.FirstToolCallTicks, meta.FirstOutputTicks);
        Assert.Equal(1, meta.OutputDeltaCount);
        Assert.Equal(0, meta.VisibleOutputChars);
    }

    [Theory]
    [InlineData("SAFETY")]
    [InlineData("PROHIBITED_CONTENT")]
    [InlineData("BLOCKLIST")]
    [InlineData("SPII")]
    [InlineData("RECITATION")]
    public async Task Google_RefusalFinishReason_SetsIsRefusal(string finishReason)
    {
        var sse = Data("{\"candidates\":[{\"finishReason\":\"" + finishReason + "\"}],\"modelVersion\":\"gemini-x-served\"}");

        var meta = TakeMeta(await ParseAsync(new GoogleProvider(EmptyConfig()), sse));

        Assert.True(meta.IsRefusal);
        Assert.NotNull(meta.CompletedTicks);
    }

    [Fact]
    public async Task Google_StopFinishReason_IsNotARefusal()
    {
        var meta = TakeMeta(await ParseAsync(new GoogleProvider(EmptyConfig()), GoogleTextChunk(Delta1, ",\"finishReason\":\"STOP\"")));

        Assert.False(meta.IsRefusal);
    }

    [Fact]
    public async Task Google_PromptFeedbackBlockReason_SetsIsRefusalAndEmitsFinishReason()
    {
        var sse = Data("""{"promptFeedback":{"blockReason":"PROHIBITED_CONTENT"},"usageMetadata":{"promptTokenCount":12,"totalTokenCount":12},"modelVersion":"gemini-x-served"}""");

        var events = await ParseAsync(new GoogleProvider(EmptyConfig()), sse);
        var meta = TakeMeta(events);

        Assert.True(meta.IsRefusal);
        Assert.NotNull(meta.CompletedTicks);
        var finish = Assert.Single(events, e => e.Type == "finish_reason");
        Assert.Equal("PROHIBITED_CONTENT", finish.Data);
    }

    [Fact]
    public async Task Google_ToolUsePromptTokens_AreAddedToTotalPromptTokens()
    {
        var sse = GoogleTextChunk(Delta1)
            + Data("""{"candidates":[{"content":{"role":"model","parts":[{"text":"."}]},"finishReason":"STOP"}],"usageMetadata":{"promptTokenCount":100,"toolUsePromptTokenCount":30,"candidatesTokenCount":5,"totalTokenCount":135},"modelVersion":"gemini-x-served"}""");

        var events = await ParseAsync(new GoogleProvider(EmptyConfig()), sse);

        var usage = Assert.Single(events, e => e.Type == "usage");
        Assert.NotNull(usage.UsageReport);
        Assert.Equal(130, usage.UsageReport!.TotalPromptTokens);
        Assert.Equal(130, usage.UsageReport.UncachedInputTokens);
        AssertCallMetaAfterUsage(events);
    }

    // --- ProviderCallMeta arithmetic ------------------------------------------------------------

    [Fact]
    public void Last80_TenDeltas_StartsTheWindowAtIndexTwo()
    {
        var meta = new ProviderCallMeta();
        for (int i = 0; i < 10; i++)
        {
            meta.MarkText(1000 + i * 100, i + 1);
        }

        var window = meta.Last80();

        Assert.NotNull(window);
        // From the delta at index 2 (ticks 1200) to the last (ticks 1900).
        Assert.Equal(700L, window!.Value.SpanTicks);
        // Characters after index 2: 4 + 5 + ... + 10.
        Assert.Equal(Enumerable.Range(4, 7).Sum(), window.Value.Chars);
    }

    [Fact]
    public void Last80_FewerThanTwoDeltasInTheWindow_IsNull()
    {
        var empty = new ProviderCallMeta();
        Assert.Null(empty.Last80());

        var one = new ProviderCallMeta();
        one.MarkText(10, 3);
        Assert.Null(one.Last80());

        var two = new ProviderCallMeta();
        two.MarkText(10, 3);
        two.MarkText(30, 4);
        var window = two.Last80();
        Assert.NotNull(window);
        Assert.Equal(20L, window!.Value.SpanTicks);
        Assert.Equal(4, window.Value.Chars);
    }

    [Fact]
    public void MarkText_EmptyDelta_MarksTheEventButCountsNothing()
    {
        var meta = new ProviderCallMeta();
        meta.MarkText(5, 0);

        Assert.Equal(5L, meta.FirstEventTicks);
        Assert.Null(meta.FirstOutputTicks);
        Assert.Equal(0, meta.OutputDeltaCount);
        Assert.Empty(meta.TextDeltaMarks);
    }

    [Fact]
    public void MarkText_BeyondTheCap_KeepsCountingButStopsKeepingMarks()
    {
        var meta = new ProviderCallMeta();
        for (int i = 0; i < ProviderCallMeta.MaxDeltaMarks + 1; i++)
        {
            meta.MarkText(i + 1, 1);
        }

        Assert.Equal(ProviderCallMeta.MaxDeltaMarks, meta.TextDeltaMarks.Count);
        Assert.Equal(ProviderCallMeta.MaxDeltaMarks + 1, meta.OutputDeltaCount);
        Assert.Equal(ProviderCallMeta.MaxDeltaMarks + 1, meta.VisibleOutputChars);
        Assert.Equal((long)(ProviderCallMeta.MaxDeltaMarks + 1), meta.LastDeltaTicks);
    }

    [Fact]
    public void Marks_FirstWinsExceptLastDelta()
    {
        var meta = new ProviderCallMeta();
        meta.MarkReasoning(10);
        meta.MarkReasoning(20);
        meta.MarkToolCall(30);
        meta.MarkText(40, 2);
        meta.MarkCompleted(50);
        meta.MarkCompleted(60);

        Assert.Equal(10L, meta.FirstEventTicks);
        Assert.Equal(10L, meta.FirstReasoningTicks);
        Assert.Equal(30L, meta.FirstOutputTicks);
        Assert.Equal(30L, meta.FirstToolCallTicks);
        Assert.Equal(40L, meta.LastDeltaTicks);
        Assert.Equal(50L, meta.CompletedTicks);
        Assert.Equal(2, meta.OutputDeltaCount);
        Assert.Equal(2, meta.VisibleOutputChars);
    }

    [Fact]
    public void MsBetween_ConvertsStopwatchTicksAndPropagatesNull()
    {
        long f = Stopwatch.Frequency;

        Assert.Null(ProviderCallMeta.MsBetween(null, f));
        Assert.Null(ProviderCallMeta.MsBetween(0, null));
        Assert.Equal(0, ProviderCallMeta.MsBetween(f, f));
        Assert.Equal(1000, ProviderCallMeta.MsBetween(0, f));
        Assert.Equal(1500, ProviderCallMeta.MsBetween(f, f * 5 / 2));
        Assert.Equal(-1000, ProviderCallMeta.MsBetween(f, 0));
        Assert.Equal(2000, ProviderCallMeta.TicksToMs(f * 2));
    }

    // --- ProviderResponseHeaders -----------------------------------------------------------------

    private static HttpResponseMessage WithHeaders(params (string Name, string Value)[] headers)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        foreach (var (name, value) in headers)
        {
            Assert.True(response.Headers.TryAddWithoutValidation(name, value), "header not accepted: " + name);
        }
        return response;
    }

    [Fact]
    public void Headers_NoneOfInterest_AllNull()
    {
        using var response = WithHeaders(("x-other", "1"));

        var headers = ProviderResponseHeaders.From(response);

        Assert.Null(headers.RequestId);
        Assert.Null(headers.ServerProcessingMs);
        Assert.Null(headers.RateLimitJson);
    }

    [Fact]
    public void Headers_XRequestId_IsRead()
    {
        using var response = WithHeaders(("x-request-id", "req_openai_1"));

        Assert.Equal("req_openai_1", ProviderResponseHeaders.From(response).RequestId);
    }

    [Fact]
    public void Headers_RequestId_IsReadWhenThereIsNoXRequestId()
    {
        using var response = WithHeaders(("request-id", "req_anthropic_1"));

        Assert.Equal("req_anthropic_1", ProviderResponseHeaders.From(response).RequestId);
    }

    [Fact]
    public void Headers_XRequestId_WinsOverRequestId()
    {
        using var response = WithHeaders(("request-id", "second"), ("x-request-id", "first"));

        Assert.Equal("first", ProviderResponseHeaders.From(response).RequestId);
    }

    [Fact]
    public void Headers_LongRequestId_IsTruncatedTo160()
    {
        using var response = WithHeaders(("x-request-id", new string('r', 200)));

        Assert.Equal(new string('r', 160), ProviderResponseHeaders.From(response).RequestId);
    }

    [Theory]
    [InlineData("1234", 1234)]
    [InlineData("0", 0)]
    [InlineData("not-a-number", null)]
    public void Headers_OpenAiProcessingMs_IsParsed(string value, int? expected)
    {
        using var response = WithHeaders(("openai-processing-ms", value));

        Assert.Equal(expected, ProviderResponseHeaders.From(response).ServerProcessingMs);
    }

    [Fact]
    public void Headers_RateLimitHeaders_AreCollectedIntoSortedCompactJson()
    {
        using var response = WithHeaders(
            ("x-ratelimit-remaining-tokens", "999"),
            ("X-RateLimit-Limit-Requests", "500"),
            ("Retry-After", "3"),
            ("anthropic-ratelimit-requests-remaining", "40"),
            ("x-other", "ignored"));

        var json = ProviderResponseHeaders.From(response).RateLimitJson;

        Assert.Equal(
            """{"anthropic-ratelimit-requests-remaining":"40","retry-after":"3","x-ratelimit-limit-requests":"500","x-ratelimit-remaining-tokens":"999"}""",
            json);
    }

    [Fact]
    public void Headers_OversizeRateLimitJson_DropsTheLongestValuesUntilItFits()
    {
        using var response = WithHeaders(
            ("x-ratelimit-huge", new string('a', 600)),
            ("x-ratelimit-small", "1"));

        var json = ProviderResponseHeaders.From(response).RateLimitJson;

        Assert.NotNull(json);
        Assert.True(json!.Length <= ProviderResponseHeaders.MaxRateLimitJsonLength);
        Assert.Equal("""{"x-ratelimit-small":"1"}""", json);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
    }

    [Fact]
    public void Headers_OnlyAnOversizeRateLimitHeader_LeavesNoJson()
    {
        using var response = WithHeaders(("x-ratelimit-huge", new string('a', 600)));

        Assert.Null(ProviderResponseHeaders.From(response).RateLimitJson);
    }
}
