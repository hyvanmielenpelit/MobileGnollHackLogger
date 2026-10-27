using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MobileGnollHackLogger.Data;
using Overseer.Controllers;
using Overseer.Services;
using Overseer.Services.Agents;
using Overseer.Services.Providers;
using Overseer.Services.Tools;
using Overseer.Tests.Helpers;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// The per-call <see cref="ModelCallRecord"/> list <see cref="AgentLoopRunner"/> keeps on
/// <see cref="AgentRunResult.ModelCalls"/>: one record per call, permit wait kept apart from retry
/// backoff, <c>call_meta</c> consumed and never yielded, response headers copied, and a record left
/// by a call that failed for good. No network: the "AiProvider" client is a scripted handler.
/// </summary>
public class AgentLoopRunnerTelemetryTests
{
    private const string CredentialKey = "telemetry-test:key";

    /// <summary>
    /// A provider whose stream for each call is a scripted list of events, optionally followed by an
    /// awaited step (a failure, or a wait for cancellation) before the stream ends.
    /// </summary>
    private sealed class ScriptedProvider : IAiProvider
    {
        private readonly Func<int, IReadOnlyList<ChatEvent>> _script;
        private readonly Func<int, CancellationToken, Task>? _afterEvents;
        private int _callCount;

        public ScriptedProvider(
            Func<int, IReadOnlyList<ChatEvent>> script,
            string providerName = "MockProvider",
            Func<int, CancellationToken, Task>? afterEvents = null)
        {
            _script = script;
            ProviderName = providerName;
            _afterEvents = afterEvents;
        }

        public string ProviderName { get; }

        /// <summary>The level this provider sends when none is configured; null sends none.</summary>
        public string? DefaultThinkingLevel { get; init; }

        public string? EffectiveThinkingLevel(string? configuredLevel)
            => string.IsNullOrEmpty(configuredLevel) ? DefaultThinkingLevel : configuredLevel;

        public IReadOnlyList<string> SupportedServiceTiers => new[] { "default" };

        public void AppendAssistantToolCallsToHistory(List<object> messageHistory, string iterationText, List<JsonElement> toolCalls, List<JsonElement>? providerHistoryItems = null)
            => messageHistory.Add(new { role = "assistant", content = iterationText });

        public void AppendToolResultsToHistory(List<object> messageHistory, List<ProviderToolResult> results)
        {
            foreach (var tr in results)
            {
                messageHistory.Add(new { role = "tool", content = tr.Content });
            }
        }

        public void AppendUserTextToHistory(List<object> messageHistory, string text)
            => messageHistory.Add(new { role = "user", content = text });

        public Dictionary<string, object> BuildChatRequestBody(string modelId, List<object> messageHistory, int? maxOutputTokens, string? thinkingLevel, ToolsForRequest requestTools, string? reasoningMode = null, string? reasoningSummary = null, string? serviceTier = null, bool? parallelToolCalls = null, SegmentedPrompt? segmentedPrompt = null, string? promptCacheKey = null, bool cacheConversationTail = true, bool disablePromptCache = false)
            => new() { { "model", modelId }, { "messages", messageHistory } };

        public bool TryRewriteToolResult(List<object> messageHistory, string toolCallId, string replacementText) => false;

        public object BuildFunctionDeclaration(string name, string description, object parameterSchema)
            => new { name, description, parameterSchema };

        public object? BuildToolsPayload(List<object> providerTools, List<object> functionDeclarations) => new { };

        public object? BuildWebSearchTool() => null;

        public void ConfigureRequest(HttpRequestMessage request, string apiKey, AiEndpointDescriptor endpoint)
        {
        }

        public object FormatMessage(string role, string text, List<SendMessageAttachment>? imageAttachments)
            => new { role, content = text };

        public string GetChatStreamUrl(string modelId, string apiKey, AiEndpointDescriptor endpoint)
            => endpoint.ComposeUrl("https://mock.ai.test/stream", "/stream");

        public async IAsyncEnumerable<ChatEvent> ParseStreamAsync(HttpResponseMessage response, bool showDebugLog, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            int callIndex = _callCount++;
            foreach (var evt in _script(callIndex))
            {
                yield return evt;
            }
            if (_afterEvents != null)
            {
                await _afterEvents(callIndex, cancellationToken);
            }
            await Task.CompletedTask;
        }

        public List<object> PrepareMessageHistory(List<object> messages) => new(messages);

        public Dictionary<string, object> BuildTitleRequestBody(string modelId, string systemPrompt, string userMessage, int maxTokens, string? serviceTier = null)
            => new();

        public string GetTitleUrl(string modelId, string apiKey, AiEndpointDescriptor endpoint) => "https://mock.ai.test/title";

        public string? ParseTitleResponse(JsonElement root) => "Mock Title";
    }

    private static IReadOnlyList<ChatEvent> AnswerScript(int callIndex) => new[]
    {
        new ChatEvent { Type = "chunk", Data = "Answer." }
    };

    /// <summary>Answers each request with the next scripted response; the last one repeats.</summary>
    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Func<HttpResponseMessage>[] _responses;

        public ScriptedHandler(params Func<HttpResponseMessage>[] responses) => _responses = responses;

        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var next = _responses[Math.Min(CallCount, _responses.Length - 1)];
            CallCount++;
            return Task.FromResult(next());
        }
    }

    /// <summary>Fails every request with the exception the test supplies, or waits for its cancellation.</summary>
    private sealed class FailingHandler : HttpMessageHandler
    {
        private readonly Func<CancellationToken, Task<HttpResponseMessage>> _send;

        public FailingHandler(Func<CancellationToken, Task<HttpResponseMessage>> send) => _send = send;

        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return _send(cancellationToken);
        }
    }

    private sealed class FixedHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public FixedHttpClientFactory(HttpMessageHandler handler) => _handler = handler;

        public HttpClient CreateClient(string name) => new HttpClient(_handler, disposeHandler: false);
    }

    private sealed class NullClientBridge : IClientToolBridge
    {
        public bool IsClientConnected => false;

        public Task<ToolResult> SendToolRequestAsync(Overseer.Services.Privacy.SessionRef sessionRef, string toolName, JsonElement parameters, CancellationToken cancellationToken)
            => Task.FromResult(new ToolResult { Success = false, Content = "No client." });
    }

    private sealed class MockToolHandler : IToolHandler
    {
        public string ToolName => "mock_tool";
        public string Description { get; set; } = "A mock tool";
        public ToolExecutionLocation ExecutionLocation => ToolExecutionLocation.Server;
        public ToolCategory Category => ToolCategory.InformationRetrieval;
        public int TimeoutSeconds => 10;
        public JsonElement ParameterSchema => JsonSerializer.SerializeToElement(new { type = "object" });

        public Task<ToolResult> ExecuteAsync(JsonElement parameters, ToolExecutionContext context, CancellationToken cancellationToken)
            => Task.FromResult(new ToolResult { Success = true, Content = "Tool ran successfully." });
    }

    private static HttpResponseMessage Status(HttpStatusCode status, string body = "{}") =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    /// <summary>
    /// Rate-limit bounds that keep every retry instant: <c>MaxRetryAfterSeconds</c> 0 caps the 429
    /// backoff and the governor cooldown at zero, and one permit per credential lets a test hold it.
    /// </summary>
    private static IConfiguration FastRetryConfig() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AiRateLimitSettings:MaxRetryAfterSeconds"] = "0",
            ["AiRateLimitSettings:MaxConcurrentModelCalls"] = "1",
            ["AiRateLimitSettings:PermitWaitSeconds"] = "10"
        })
        .Build();

    private static AgentLoopRunner CreateRunner(
        IEnumerable<IAiProvider> providers, HttpMessageHandler handler, IConfiguration config, AiRequestGovernor? governor = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var sp = services.BuildServiceProvider();

        var cache = new MemoryCache(new MemoryCacheOptions());
        var handlers = new List<IToolHandler> { new MockToolHandler() };
        var clientBridge = new NullClientBridge();

        return new AgentLoopRunner(
            providers,
            new ToolRegistry(handlers, clientBridge, NullLogger<ToolRegistry>.Instance),
            new ToolExecutor(handlers, clientBridge, NullLogger<ToolExecutor>.Instance, cache, config),
            new FixedHttpClientFactory(handler),
            config,
            sp.GetRequiredService<IServiceScopeFactory>(),
            TestKnowledgeBase.Empty,
            new ModelMetadataService(),
            NullLogger<AgentLoopRunner>.Instance,
            new SubAgentCatalogService(config, NullLogger<SubAgentCatalogService>.Instance),
            governor);
    }

    private static AgentRunRequest Request(IAiProvider provider, string modelId = "mock-model", bool showDebugLog = false, string? thinkingLevel = null) => new()
    {
        ProviderName = provider.ProviderName,
        ModelId = modelId,
        ThinkingLevel = thinkingLevel,
        ApiKey = "test-key",
        CredentialKey = CredentialKey,
        ShowDebugLog = showDebugLog,
        SeedHistory = new List<object> { new { role = "user", content = "Hello" } },
        AiProvider = provider
    };

    private static async Task<(AgentRunResult Result, List<ChatEvent> Events)> RunAsync(AgentLoopRunner runner, AgentRunRequest request)
    {
        var result = new AgentRunResult();
        var events = new List<ChatEvent>();
        await foreach (var evt in runner.RunAsync(request, null, result, TestContext.Current.CancellationToken))
        {
            events.Add(evt);
        }
        return (result, events);
    }

    private static ProviderCallMeta MetaWithText(string servedModelId, int chars)
    {
        var meta = new ProviderCallMeta { ServedModelId = servedModelId, ResponseId = servedModelId + "-resp" };
        long now = ProviderCallMeta.Now();
        meta.MarkText(now, chars);
        meta.MarkCompleted(ProviderCallMeta.Now());
        return meta;
    }

    [Fact]
    public async Task ToolUsingTurn_KeepsOneRecordPerCallInOrderParallelToUsages()
    {
        var provider = new ScriptedProvider(callIndex => callIndex == 0
            ? new[]
            {
                new ChatEvent { Type = "usage", UsageReport = new TokenUsageReport { TotalPromptTokens = 100, OutputTokens = 10 } },
                new ChatEvent { Type = "tool_call_complete", Data = JsonSerializer.Serialize(new { id = "call_1", name = "mock_tool", arguments = "{}" }) },
                new ChatEvent { Type = "call_meta", CallMeta = MetaWithText("served-0", 0) }
            }
            : new[]
            {
                new ChatEvent { Type = "chunk", Data = "Final answer" },
                new ChatEvent { Type = "usage", UsageReport = new TokenUsageReport { TotalPromptTokens = 250, OutputTokens = 20 } },
                new ChatEvent { Type = "call_meta", CallMeta = MetaWithText("served-1", 12) }
            });
        var handler = new ScriptedHandler(() => Status(HttpStatusCode.OK));
        var runner = CreateRunner(new[] { provider }, handler, FastRetryConfig());

        var (result, events) = await RunAsync(runner, Request(provider));

        Assert.Equal(2, handler.CallCount);
        Assert.Equal(2, result.ModelCalls.Count);
        Assert.Equal(2, result.ModelCallUsages.Count);
        Assert.Equal(new[] { 0, 1 }, result.ModelCalls.Select(c => c.CallIndex));
        for (int i = 0; i < 2; i++)
        {
            Assert.Same(result.ModelCallUsages[i], result.ModelCalls[i].Usage);
            Assert.Equal("MockProvider", result.ModelCalls[i].Provider);
            Assert.Equal("mock-model", result.ModelCalls[i].RequestedModelId);
            Assert.Equal(1, result.ModelCalls[i].AttemptCount);
            Assert.Equal(200, result.ModelCalls[i].FinalHttpStatus);
            Assert.Null(result.ModelCalls[i].ErrorKind);
        }

        Assert.Equal("served-0", result.ModelCalls[0].ServedModelId);
        Assert.Equal("served-1", result.ModelCalls[1].ServedModelId);
        Assert.Equal("served-1-resp", result.ModelCalls[1].ResponseId);
        Assert.Equal(12, result.ModelCalls[1].VisibleOutputChars);
        Assert.Equal(1, result.ModelCalls[1].OutputDeltaCount);
        Assert.NotNull(result.ModelCalls[1].FirstOutputMs);
        Assert.True(result.ModelCalls[0].StartedAtUtc <= result.ModelCalls[1].StartedAtUtc);
        Assert.Equal(0, result.RetryAttemptCount);
        Assert.DoesNotContain(events, e => e.Type == "call_meta" || e.CallMeta != null);
    }

    /// <summary>A two-call turn (a tool call, then the answer) on a provider with the given default level.</summary>
    private static async Task<AgentRunResult> RunTwoCallTurnAsync(string? defaultThinkingLevel, string? configuredLevel)
    {
        var provider = new ScriptedProvider(callIndex => callIndex == 0
            ? new[] { new ChatEvent { Type = "tool_call_complete", Data = JsonSerializer.Serialize(new { id = "call_1", name = "mock_tool", arguments = "{}" }) } }
            : AnswerScript(callIndex))
        {
            DefaultThinkingLevel = defaultThinkingLevel
        };
        var handler = new ScriptedHandler(() => Status(HttpStatusCode.OK));
        var runner = CreateRunner(new[] { provider }, handler, FastRetryConfig());

        var (result, _) = await RunAsync(runner, Request(provider, thinkingLevel: configuredLevel));
        Assert.Equal(2, result.ModelCalls.Count);
        return result;
    }

    [Fact]
    public async Task ThinkingLevelSent_IsTheProvidersSubstituteForAnUnsetLevel()
    {
        var result = await RunTwoCallTurnAsync(defaultThinkingLevel: "high", configuredLevel: null);

        Assert.All(result.ModelCalls, c => Assert.Equal("high", c.ThinkingLevelSent));
    }

    [Fact]
    public async Task ThinkingLevelSent_IsTheConfiguredLevelWhenOneIsSet()
    {
        var result = await RunTwoCallTurnAsync(defaultThinkingLevel: "high", configuredLevel: "low");

        Assert.All(result.ModelCalls, c => Assert.Equal("low", c.ThinkingLevelSent));
    }

    [Fact]
    public async Task ThinkingLevelSent_IsNullWhenNothingIsSent()
    {
        var result = await RunTwoCallTurnAsync(defaultThinkingLevel: null, configuredLevel: null);

        Assert.All(result.ModelCalls, c => Assert.Null(c.ThinkingLevelSent));
    }

    [Fact]
    public async Task HeldGovernorPermit_IsCountedAsPermitWaitNotBackoff()
    {
        var config = FastRetryConfig();
        var governor = new AiRequestGovernor(config, NullLogger<AiRequestGovernor>.Instance);
        var provider = new ScriptedProvider(AnswerScript);
        var handler = new ScriptedHandler(() => Status(HttpStatusCode.OK));
        var runner = CreateRunner(new[] { provider }, handler, config, governor);

        // The test holds the credential's only permit. The runner yields "Waiting for ..." right before
        // it asks the governor for one, so the release timer starts there and the wait is ~150 ms.
        var held = await governor.AcquirePermitAsync(CredentialKey, TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Task? release = null;
        var result = new AgentRunResult();
        try
        {
            await foreach (var evt in runner.RunAsync(Request(provider), null, result, TestContext.Current.CancellationToken))
            {
                if (release == null && evt.Type == "status" && evt.Data.StartsWith("Waiting for", StringComparison.Ordinal))
                {
                    release = Task.Delay(150, TestContext.Current.CancellationToken).ContinueWith(_ => held.Dispose(), TaskScheduler.Default);
                }
            }
        }
        finally
        {
            held.Dispose();
        }

        Assert.NotNull(release);
        await release!;

        var record = Assert.Single(result.ModelCalls);
        Assert.True(record.PermitWaitMs >= 100, $"PermitWaitMs was {record.PermitWaitMs}");
        Assert.Equal(0, record.BackoffWaitMs);
        Assert.Equal(1, record.AttemptCount);
        Assert.Equal(0, record.Http429Count);
        Assert.Equal(record.PermitWaitMs, result.PermitWaitMs);
        Assert.Equal(0, result.BackoffWaitMs);
        Assert.Equal(0, result.RetryAttemptCount);
    }

    [Fact]
    public async Task RealProviderStream_CallMetaIsConsumedAndNeverYielded()
    {
        var provider = new OpenAiResponsesProvider(new ConfigurationBuilder().Build());
        var handler = new ScriptedHandler(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(BenchmarkTelemetryPersistenceTests.CannedStream("OpenAI"), Encoding.UTF8, "text/event-stream")
        });
        var runner = CreateRunner(new IAiProvider[] { provider }, handler, FastRetryConfig());

        var (result, events) = await RunAsync(runner, Request(provider, modelId: "gpt-6.1-sol", showDebugLog: true));

        Assert.NotEmpty(events);
        Assert.DoesNotContain(events, e => e.Type == "call_meta");
        Assert.DoesNotContain(events, e => e.CallMeta != null);

        var record = Assert.Single(result.ModelCalls);
        Assert.Equal("OpenAI", record.Provider);
        Assert.Equal("gpt-6.1-sol", record.RequestedModelId);
        Assert.Equal("gpt-6.1-sol-2026-09-01", record.ServedModelId);
        Assert.Equal("resp_golden", record.ResponseId);
        Assert.Equal("completed", record.FinishReason);
        Assert.Equal("default", record.ServedServiceTier);
        Assert.Equal(1, record.OutputDeltaCount);
        Assert.True(record.VisibleOutputChars > 0);
        Assert.NotNull(record.Usage);
        Assert.Equal(420, record.Usage!.TotalPromptTokens);

        Assert.NotNull(record.HeadersMs);
        Assert.NotNull(record.FirstEventMs);
        Assert.NotNull(record.CompletedMs);
        Assert.NotNull(record.StreamEndMs);
        Assert.True(record.HeadersMs <= record.FirstEventMs);
        Assert.True(record.FirstEventMs <= record.CompletedMs);
        Assert.True(record.CompletedMs <= record.StreamEndMs);
    }

    [Fact]
    public async Task ResponseHeaders_AreCopiedOntoTheRecord()
    {
        var provider = new OpenAiResponsesProvider(new ConfigurationBuilder().Build());
        var handler = new ScriptedHandler(() =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(BenchmarkTelemetryPersistenceTests.CannedStream("OpenAI"), Encoding.UTF8, "text/event-stream")
            };
            response.Headers.TryAddWithoutValidation("x-request-id", "req_telemetry_1");
            response.Headers.TryAddWithoutValidation("openai-processing-ms", "321");
            response.Headers.TryAddWithoutValidation("x-ratelimit-remaining-requests", "499");
            return response;
        });
        var runner = CreateRunner(new IAiProvider[] { provider }, handler, FastRetryConfig());

        var (result, _) = await RunAsync(runner, Request(provider, modelId: "gpt-6.1-sol"));

        var record = Assert.Single(result.ModelCalls);
        Assert.Equal("req_telemetry_1", record.RequestId);
        Assert.Equal(321, record.ServerProcessingMs);
        Assert.Equal("{\"x-ratelimit-remaining-requests\":\"499\"}", record.RateLimitJson);
        Assert.False(string.IsNullOrEmpty(record.HttpVersion));
        Assert.Equal(200, record.FinalHttpStatus);
    }

    [Fact]
    public async Task CallThatFailsForGood_StillLeavesARecordWithErrorKindAndStatus()
    {
        var provider = new ScriptedProvider(AnswerScript);
        var handler = new ScriptedHandler(() =>
        {
            var response = Status(HttpStatusCode.BadRequest, "{\"error\":{\"message\":\"Invalid value for 'input'.\",\"type\":\"invalid_request_error\"}}");
            response.Headers.TryAddWithoutValidation("x-request-id", "req_failed_1");
            return response;
        });
        var runner = CreateRunner(new[] { provider }, handler, FastRetryConfig());

        var (result, events) = await RunAsync(runner, Request(provider));

        Assert.Equal(1, handler.CallCount);
        var record = Assert.Single(result.ModelCalls);
        Assert.Equal("http_400", record.ErrorKind);
        Assert.Equal(400, record.FinalHttpStatus);
        Assert.Equal(1, record.AttemptCount);
        Assert.Equal("req_failed_1", record.RequestId);
        Assert.False(string.IsNullOrEmpty(record.HttpVersion));
        Assert.Null(record.Usage);
        Assert.Empty(result.ModelCallUsages);
        Assert.Equal(0, result.RetryAttemptCount);
        Assert.Contains(events, e => e.Type == "error" && e.Data != null && e.Data.StartsWith("API Error: 400"));
    }

    // --- Provider attempts: exactly one record per loop iteration, in every path ---------------

    private const string OpenAiRateLimitBody =
        "{\"error\":{\"message\":\"Rate limit reached for requests.\",\"type\":\"requests\",\"code\":\"rate_limit_exceeded\"}}";

    private const string GoogleBareExhaustedBody =
        "{\"error\":{\"code\":429,\"message\":\"Resource has been exhausted (e.g. check quota).\",\"status\":\"RESOURCE_EXHAUSTED\"}}";

    private const string GoogleQuotaBody =
        "{\"error\":{\"code\":429,\"message\":\"You exceeded your current quota.\",\"status\":\"RESOURCE_EXHAUSTED\",\"details\":["
        + "{\"@type\":\"type.googleapis.com/google.rpc.QuotaFailure\",\"violations\":[]}]}}";

    private static ProviderCallMeta CompletedMeta()
    {
        var meta = new ProviderCallMeta();
        meta.MarkCompleted(ProviderCallMeta.Now());
        return meta;
    }

    /// <summary>A stream that answers and reports its completion, as the real providers do.</summary>
    private static IReadOnlyList<ChatEvent> CompletedAnswer(int callIndex) => new[]
    {
        new ChatEvent { Type = "chunk", Data = "Answer." },
        new ChatEvent { Type = "call_meta", CallMeta = CompletedMeta() }
    };

    /// <summary><see cref="FastRetryConfig"/> with no 429 retries, so a 429 ends the call at once.</summary>
    private static IConfiguration No429RetryConfig() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AiRateLimitSettings:MaxRetryAfterSeconds"] = "0",
            ["AiRateLimitSettings:MaxConcurrentModelCalls"] = "1",
            ["AiRateLimitSettings:PermitWaitSeconds"] = "10",
            ["AiRateLimitSettings:Max429RetriesPerCall"] = "0"
        })
        .Build();

    /// <summary>Runs the turn on <paramref name="cancellationToken"/> and returns what it left, with the exception that ended it.</summary>
    private static async Task<(AgentRunResult Result, List<ChatEvent> Events, Exception? Error)> RunCatchingAsync(
        AgentLoopRunner runner, AgentRunRequest request, CancellationToken cancellationToken)
    {
        var result = new AgentRunResult();
        var events = new List<ChatEvent>();
        try
        {
            await foreach (var evt in runner.RunAsync(request, null, result, cancellationToken))
            {
                events.Add(evt);
            }
        }
        catch (Exception ex)
        {
            return (result, events, ex);
        }
        return (result, events, null);
    }

    [Fact]
    public async Task Attempts_Success_RecordsOneOkAttemptAndNoTerminalOutcome()
    {
        var provider = new ScriptedProvider(CompletedAnswer, "Anthropic");
        var handler = new ScriptedHandler(() => Status(HttpStatusCode.OK));
        var runner = CreateRunner(new[] { provider }, handler, FastRetryConfig());

        var (result, _) = await RunAsync(runner, Request(provider));

        var call = Assert.Single(result.ModelCalls);
        var attempt = Assert.Single(call.Attempts);
        Assert.Equal(0, attempt.AttemptIndex);
        Assert.Equal(ProviderAttemptOutcome.Ok, attempt.Outcome);
        Assert.Equal(200, attempt.HttpStatus);
        Assert.False(attempt.Retried);
        Assert.Null(attempt.ProviderErrorCode);
        Assert.Null(attempt.RetryAfterMs);
        Assert.False(attempt.QuestionBudgetCanceled);
        Assert.Equal(BenchmarkEndpointKind.Official, attempt.EndpointKind);
        Assert.True(attempt.DurationMs >= 0);
        Assert.True(attempt.StartedAtUtc >= call.StartedAtUtc);
        Assert.Null(result.TerminalOutcome);
        Assert.Null(result.TerminalErrorCode);
        Assert.False(result.TerminalQuestionBudgetCanceled);
    }

    [Fact]
    public async Task Attempts_StreamWithoutCompletionEvent_IsProviderStreamFailureWhileTheCallSucceeds()
    {
        var provider = new ScriptedProvider(AnswerScript);
        var handler = new ScriptedHandler(() => Status(HttpStatusCode.OK));
        var runner = CreateRunner(new[] { provider }, handler, FastRetryConfig());

        var (result, _) = await RunAsync(runner, Request(provider));

        var call = Assert.Single(result.ModelCalls);
        var attempt = Assert.Single(call.Attempts);
        Assert.Equal(ProviderAttemptOutcome.ProviderStreamFailure, attempt.Outcome);
        Assert.Null(call.ErrorKind);
        Assert.Null(result.TerminalOutcome);
    }

    [Fact]
    public async Task Attempts_ErrorEventAfterChunksThenCompletion_IsProviderStreamFailureNotOk()
    {
        // The loop sets success after a yielded stream error; the attempt must still not be Ok.
        var provider = new ScriptedProvider(_ => new[]
        {
            new ChatEvent { Type = "chunk", Data = "Partial " },
            new ChatEvent { Type = "error", Data = "MockProvider stream error: [stream_error] The stream broke." },
            new ChatEvent { Type = "chunk", Data = "answer." },
            new ChatEvent { Type = "call_meta", CallMeta = CompletedMeta() }
        });
        var handler = new ScriptedHandler(() => Status(HttpStatusCode.OK));
        var runner = CreateRunner(new[] { provider }, handler, FastRetryConfig());

        var (result, _) = await RunAsync(runner, Request(provider));

        Assert.Equal(1, handler.CallCount);
        var call = Assert.Single(result.ModelCalls);
        var attempt = Assert.Single(call.Attempts);
        Assert.Equal(ProviderAttemptOutcome.ProviderStreamFailure, attempt.Outcome);
        Assert.Equal("stream_error", attempt.ProviderErrorCode);
        Assert.Equal(200, attempt.HttpStatus);
        Assert.Equal("stream_error", call.ErrorKind);
        Assert.Equal(ProviderAttemptOutcome.ProviderStreamFailure, result.TerminalOutcome);
        Assert.Equal("stream_error", result.TerminalErrorCode);
    }

    [Fact]
    public async Task Attempts_AnthropicOverloadedErrorAfterChunks_IsProviderOverloaded()
    {
        var provider = new ScriptedProvider(_ => new[]
        {
            new ChatEvent { Type = "chunk", Data = "Partial" },
            new ChatEvent { Type = "error", Data = "Anthropic stream error: [overloaded_error] Overloaded" },
            new ChatEvent { Type = "call_meta", CallMeta = CompletedMeta() }
        }, "Anthropic");
        var handler = new ScriptedHandler(() => Status(HttpStatusCode.OK));
        var runner = CreateRunner(new[] { provider }, handler, FastRetryConfig());

        var (result, _) = await RunAsync(runner, Request(provider));

        var attempt = Assert.Single(Assert.Single(result.ModelCalls).Attempts);
        Assert.Equal(ProviderAttemptOutcome.ProviderOverloaded, attempt.Outcome);
        Assert.Equal("overloaded_error", attempt.ProviderErrorCode);
        Assert.Equal(ProviderAttemptOutcome.ProviderOverloaded, result.TerminalOutcome);
    }

    [Fact]
    public async Task Attempts_StreamErrorRetry_RecordsTheRetriedAttemptThenOk()
    {
        // A stream error before the first chunk is retried after retryDelays[0] (1 s).
        var provider = new ScriptedProvider(callIndex => callIndex == 0
            ? new[] { new ChatEvent { Type = "error", Data = "Anthropic stream error: [overloaded_error] Overloaded" } }
            : CompletedAnswer(callIndex), "Anthropic");
        var handler = new ScriptedHandler(() => Status(HttpStatusCode.OK));
        var runner = CreateRunner(new[] { provider }, handler, FastRetryConfig());

        var (result, events) = await RunAsync(runner, Request(provider));

        Assert.Equal(2, handler.CallCount);
        var call = Assert.Single(result.ModelCalls);
        Assert.Equal(1, call.StreamErrorRetryCount);
        Assert.Equal(2, call.Attempts.Count);
        Assert.Equal(new[] { 0, 1 }, call.Attempts.Select(a => a.AttemptIndex));
        Assert.Equal(ProviderAttemptOutcome.ProviderOverloaded, call.Attempts[0].Outcome);
        Assert.True(call.Attempts[0].Retried);
        Assert.Equal(200, call.Attempts[0].HttpStatus);
        Assert.Equal("overloaded_error", call.Attempts[0].ProviderErrorCode);
        Assert.Equal(ProviderAttemptOutcome.Ok, call.Attempts[1].Outcome);
        Assert.False(call.Attempts[1].Retried);
        Assert.Null(call.ErrorKind);
        Assert.Null(result.TerminalOutcome);
        Assert.DoesNotContain(events, e => e.Type == "error");
    }

    [Fact]
    public async Task Attempts_OpenAi429ThenOk_RecordsOurRateLimitWithItsRetryAfterThenOk()
    {
        var provider = new ScriptedProvider(CompletedAnswer, "OpenAI");
        var handler = new ScriptedHandler(
            () =>
            {
                var response = Status(HttpStatusCode.TooManyRequests, OpenAiRateLimitBody);
                response.Headers.TryAddWithoutValidation("retry-after-ms", "1500");
                return response;
            },
            () => Status(HttpStatusCode.OK));
        var config = FastRetryConfig();
        var governor = new AiRequestGovernor(config, NullLogger<AiRequestGovernor>.Instance);
        var runner = CreateRunner(new[] { provider }, handler, config, governor);

        var (result, events) = await RunAsync(runner, Request(provider));

        Assert.Equal(2, handler.CallCount);
        var call = Assert.Single(result.ModelCalls);
        Assert.Equal(2, call.AttemptCount);
        Assert.Equal(1, call.Http429Count);
        Assert.Equal(0, call.Http5xxCount);
        Assert.Equal(200, call.FinalHttpStatus);
        Assert.Null(call.ErrorKind);
        Assert.True(call.PermitWaitMs < 500, $"PermitWaitMs was {call.PermitWaitMs}");
        Assert.True(call.BackoffWaitMs < 500, $"BackoffWaitMs was {call.BackoffWaitMs}");
        Assert.True(call.FailedAttemptMs >= 0);
        Assert.Equal(1, result.RetryAttemptCount);
        Assert.DoesNotContain(events, e => e.Type == "error");
        Assert.Equal(2, call.Attempts.Count);
        var limited = call.Attempts[0];
        Assert.Equal(ProviderAttemptOutcome.OurRateLimit, limited.Outcome);
        Assert.Equal(429, limited.HttpStatus);
        Assert.True(limited.Retried);
        Assert.Equal("rate_limit_exceeded", limited.ProviderErrorCode);
        Assert.Equal(1500, limited.RetryAfterMs);
        Assert.Equal(ProviderAttemptOutcome.Ok, call.Attempts[1].Outcome);
        Assert.True(call.Attempts[1].StartedAtUtc >= limited.StartedAtUtc);
        Assert.Null(result.TerminalOutcome);
    }

    [Fact]
    public async Task Attempts_Http503ThenOk_RecordsProviderOverloadedRetriedThenOk()
    {
        // The 5xx branch sleeps retryDelays[0] (1 s) before the second attempt.
        var provider = new ScriptedProvider(CompletedAnswer, "Google");
        var handler = new ScriptedHandler(
            () => Status(HttpStatusCode.ServiceUnavailable, "{\"error\":{\"code\":503,\"message\":\"The model is overloaded.\",\"status\":\"UNAVAILABLE\"}}"),
            () => Status(HttpStatusCode.OK));
        var runner = CreateRunner(new[] { provider }, handler, FastRetryConfig());

        var (result, _) = await RunAsync(runner, Request(provider));

        var call = Assert.Single(result.ModelCalls);
        Assert.Equal(1, call.Http5xxCount);
        Assert.Equal(2, call.Attempts.Count);
        Assert.Equal(ProviderAttemptOutcome.ProviderOverloaded, call.Attempts[0].Outcome);
        Assert.Equal("UNAVAILABLE", call.Attempts[0].ProviderErrorCode);
        Assert.True(call.Attempts[0].Retried);
        Assert.Equal(ProviderAttemptOutcome.Ok, call.Attempts[1].Outcome);
    }

    [Theory]
    [InlineData("Anthropic", 529, "{\"type\":\"error\",\"error\":{\"type\":\"overloaded_error\",\"message\":\"Overloaded\"}}", ProviderAttemptOutcome.ProviderOverloaded, "overloaded_error")]
    [InlineData("OpenAI", 504, "{}", ProviderAttemptOutcome.ProviderServerError, null)]
    [InlineData("Google", 408, "{}", ProviderAttemptOutcome.ProviderTimeout, null)]
    [InlineData("Anthropic", 400, "{\"type\":\"error\",\"error\":{\"type\":\"invalid_request_error\",\"message\":\"prompt is too long: 205000 tokens > 200000 maximum\"}}", ProviderAttemptOutcome.OurRequest, "invalid_request_error")]
    [InlineData("OpenAI", 401, "{\"error\":{\"message\":\"Incorrect API key provided.\",\"type\":\"invalid_request_error\",\"code\":\"invalid_api_key\"}}", ProviderAttemptOutcome.OurKey, "invalid_api_key")]
    [InlineData("OpenAI", 429, "{\"error\":{\"message\":\"You exceeded your current quota.\",\"type\":\"insufficient_quota\",\"code\":\"insufficient_quota\"}}", ProviderAttemptOutcome.OurBilling, "insufficient_quota", 1)]
    [InlineData("OpenAI", 429, OpenAiRateLimitBody, ProviderAttemptOutcome.OurRateLimit, "rate_limit_exceeded", 1, "http_429")]
    [InlineData("Google", 404, "{\"error\":{\"code\":404,\"message\":\"models/mock-model is not found for API version v1beta.\",\"status\":\"NOT_FOUND\"}}", ProviderAttemptOutcome.OurRequest, "NOT_FOUND", null, "model_not_found")]
    [InlineData("Google", 429, GoogleBareExhaustedBody, ProviderAttemptOutcome.RateLimitUnattributed, "RESOURCE_EXHAUSTED")]
    [InlineData("Google", 429, GoogleQuotaBody, ProviderAttemptOutcome.OurRateLimit, "RESOURCE_EXHAUSTED")]
    public async Task Attempts_StatusThatEndsTheCall_RecordsOneAttemptAndTheTerminalOutcome(
        string providerName, int status, string body, ProviderAttemptOutcome expected, string? expectedCode,
        int? expectedHttp429Count = null, string? expectedErrorKind = null)
    {
        var provider = new ScriptedProvider(CompletedAnswer, providerName);
        var handler = new ScriptedHandler(() => Status((HttpStatusCode)status, body));
        var runner = CreateRunner(new[] { provider }, handler, No429RetryConfig());

        var (result, events) = await RunAsync(runner, Request(provider));

        Assert.Equal(1, handler.CallCount);
        var call = Assert.Single(result.ModelCalls);
        Assert.NotNull(call.ErrorKind);
        if (expectedErrorKind != null)
        {
            Assert.Equal(expectedErrorKind, call.ErrorKind);
        }
        if (expectedHttp429Count.HasValue)
        {
            Assert.Equal(expectedHttp429Count.Value, call.Http429Count);
        }
        var attempt = Assert.Single(call.Attempts);
        Assert.Equal(expected, attempt.Outcome);
        Assert.Equal(status, attempt.HttpStatus);
        Assert.Equal(expectedCode, attempt.ProviderErrorCode);
        Assert.False(attempt.Retried);
        Assert.Equal(expected, result.TerminalOutcome);
        Assert.Equal(expectedCode, result.TerminalErrorCode);
        Assert.False(result.TerminalQuestionBudgetCanceled);
        Assert.Contains(events, e => e.Type == "error");
    }

    [Fact]
    public async Task Attempts_Anthropic429OnACustomEndpoint_IsRateLimitUnattributed()
    {
        var provider = new ScriptedProvider(CompletedAnswer, "Anthropic");
        var handler = new ScriptedHandler(() => Status(HttpStatusCode.TooManyRequests,
            "{\"type\":\"error\",\"error\":{\"type\":\"rate_limit_error\",\"message\":\"Rate limited\"}}"));
        var runner = CreateRunner(new[] { provider }, handler, No429RetryConfig());
        var request = Request(provider);
        request.Endpoint = new AiEndpointDescriptor("https://gateway.example.test", null, null, AiEndpointAuthStyle.BearerToken);

        var (result, _) = await RunAsync(runner, request);

        var attempt = Assert.Single(Assert.Single(result.ModelCalls).Attempts);
        Assert.Equal(BenchmarkEndpointKind.Custom, attempt.EndpointKind);
        Assert.Equal(ProviderAttemptOutcome.RateLimitUnattributed, attempt.Outcome);
        Assert.Equal("rate_limit_error", attempt.ProviderErrorCode);
    }

    [Fact]
    public async Task Attempts_GovernorRefusal_RecordsOurThrottleWithoutASend()
    {
        var config = FastRetryConfig();
        var governor = new AiRequestGovernor(config, NullLogger<AiRequestGovernor>.Instance);
        var provider = new ScriptedProvider(CompletedAnswer);
        var handler = new ScriptedHandler(() => Status(HttpStatusCode.OK));
        var runner = CreateRunner(new[] { provider }, handler, config, governor);
        var request = Request(provider);
        request.PermitWaitTimeout = TimeSpan.FromMilliseconds(50);

        // The test holds the credential's only permit for the whole turn.
        var held = await governor.AcquirePermitAsync(CredentialKey, TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        AgentRunResult result;
        List<ChatEvent> events;
        try
        {
            (result, events) = await RunAsync(runner, request);
        }
        finally
        {
            held.Dispose();
        }

        Assert.Equal(0, handler.CallCount);
        var call = Assert.Single(result.ModelCalls);
        Assert.Equal(0, call.AttemptCount);
        Assert.Equal("permit_timeout", call.ErrorKind);
        var attempt = Assert.Single(call.Attempts);
        Assert.Equal(ProviderAttemptOutcome.OurThrottle, attempt.Outcome);
        Assert.Null(attempt.HttpStatus);
        Assert.Null(attempt.ProviderErrorCode);
        Assert.False(attempt.Retried);
        Assert.True(attempt.DurationMs >= 0);
        Assert.Equal(ProviderAttemptOutcome.OurThrottle, result.TerminalOutcome);
        Assert.Contains(events, e => e.Type == "error" && e.Data.StartsWith("Request throttled:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Attempts_RequestTransportException_IsTransportUnattributed()
    {
        var provider = new ScriptedProvider(CompletedAnswer, "OpenAI");
        var handler = new FailingHandler(_ => Task.FromException<HttpResponseMessage>(
            new HttpRequestException("An error occurred while sending the request.", new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.ConnectionReset))));
        var runner = CreateRunner(new[] { provider }, handler, FastRetryConfig());

        var (result, events) = await RunAsync(runner, Request(provider));

        Assert.Equal(1, handler.CallCount);
        var call = Assert.Single(result.ModelCalls);
        Assert.Equal("exception", call.ErrorKind);
        var attempt = Assert.Single(call.Attempts);
        Assert.Equal(ProviderAttemptOutcome.TransportUnattributed, attempt.Outcome);
        Assert.Null(attempt.HttpStatus);
        Assert.Equal(ProviderAttemptOutcome.TransportUnattributed, result.TerminalOutcome);
        Assert.Contains(events, e => e.Type == "error" && e.Data.StartsWith("Request failed:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Attempts_HttpClientTimeoutBeforeHeaders_IsProviderTimeout()
    {
        var provider = new ScriptedProvider(CompletedAnswer, "Google");
        var handler = new FailingHandler(_ => Task.FromException<HttpResponseMessage>(
            new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 600 seconds elapsing.", new TimeoutException())));
        var runner = CreateRunner(new[] { provider }, handler, FastRetryConfig());

        var (result, _) = await RunAsync(runner, Request(provider));

        var call = Assert.Single(result.ModelCalls);
        Assert.Equal("timeout", call.ErrorKind);
        var attempt = Assert.Single(call.Attempts);
        Assert.Equal(ProviderAttemptOutcome.ProviderTimeout, attempt.Outcome);
        Assert.Equal(ProviderAttemptOutcome.ProviderTimeout, result.TerminalOutcome);
    }

    [Fact]
    public async Task Attempts_MidStreamIOException_IsRecordedBeforeTheExceptionLeaves()
    {
        var provider = new ScriptedProvider(
            _ => new[] { new ChatEvent { Type = "chunk", Data = "Partial" } },
            "OpenAI",
            (_, _) => Task.FromException(new IOException("The response ended prematurely.")));
        var handler = new ScriptedHandler(() => Status(HttpStatusCode.OK));
        var runner = CreateRunner(new[] { provider }, handler, FastRetryConfig());

        var (result, events, error) = await RunCatchingAsync(runner, Request(provider), TestContext.Current.CancellationToken);

        Assert.IsType<IOException>(error);
        var call = Assert.Single(result.ModelCalls);
        Assert.Equal("stream_error", call.ErrorKind);
        var attempt = Assert.Single(call.Attempts);
        Assert.Equal(ProviderAttemptOutcome.TransportUnattributed, attempt.Outcome);
        Assert.Equal(200, attempt.HttpStatus);
        Assert.Equal(ProviderAttemptOutcome.TransportUnattributed, result.TerminalOutcome);
        Assert.Contains(events, e => e.Type == "error" && e.Data.StartsWith("Stream interrupted:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Attempts_MidStreamCancelWithoutOurCancel_IsProviderStreamFailure()
    {
        var provider = new ScriptedProvider(
            _ => new[] { new ChatEvent { Type = "chunk", Data = "Partial" } },
            "Anthropic",
            (_, _) => Task.FromException(new OperationCanceledException("The stream was torn down.")));
        var handler = new ScriptedHandler(() => Status(HttpStatusCode.OK));
        var runner = CreateRunner(new[] { provider }, handler, FastRetryConfig());

        var (result, _, error) = await RunCatchingAsync(runner, Request(provider), TestContext.Current.CancellationToken);

        Assert.IsAssignableFrom<OperationCanceledException>(error);
        var attempt = Assert.Single(Assert.Single(result.ModelCalls).Attempts);
        Assert.Equal(ProviderAttemptOutcome.ProviderStreamFailure, attempt.Outcome);
        Assert.False(attempt.QuestionBudgetCanceled);
        Assert.Equal(ProviderAttemptOutcome.ProviderStreamFailure, result.TerminalOutcome);
    }

    // --- Cancellation: the caller's question budget against the operator's cancel ---------------

    /// <summary>
    /// A turn whose stream yields one chunk, then cancels <paramref name="toCancel"/> and waits for the
    /// run's token, run with <paramref name="operatorToken"/> as the operator's token.
    /// </summary>
    private static async Task<(AgentRunResult Result, Exception? Error)> CutMidStreamAsync(
        CancellationTokenSource runCts, CancellationTokenSource toCancel, CancellationToken operatorToken)
    {
        var provider = new ScriptedProvider(
            _ => new[] { new ChatEvent { Type = "chunk", Data = "Partial" } },
            "Anthropic",
            (_, ct) =>
            {
                toCancel.Cancel();
                return Task.Delay(Timeout.Infinite, ct);
            });
        var handler = new ScriptedHandler(() => Status(HttpStatusCode.OK));
        var runner = CreateRunner(new[] { provider }, handler, FastRetryConfig());
        var request = Request(provider);
        request.OperatorCancellation = operatorToken;

        var (result, _, error) = await RunCatchingAsync(runner, request, runCts.Token);
        return (result, error);
    }

    [Fact]
    public async Task Attempts_QuestionBudgetCutsTheStream_IsOurCanceledByTheBudget_AndOnTheResultBeforeTheThrow()
    {
        using var operatorCts = new CancellationTokenSource();
        using var questionCts = CancellationTokenSource.CreateLinkedTokenSource(operatorCts.Token);

        var (result, error) = await CutMidStreamAsync(questionCts, questionCts, operatorCts.Token);

        Assert.IsAssignableFrom<OperationCanceledException>(error);
        var call = Assert.Single(result.ModelCalls);
        Assert.Equal("canceled", call.ErrorKind);
        var attempt = Assert.Single(call.Attempts);
        Assert.Equal(ProviderAttemptOutcome.OurCanceled, attempt.Outcome);
        Assert.True(attempt.QuestionBudgetCanceled);
        Assert.Equal(ProviderAttemptOutcome.OurCanceled, result.TerminalOutcome);
        Assert.True(result.TerminalQuestionBudgetCanceled);
        Assert.Null(result.TerminalErrorCode);
    }

    [Fact]
    public async Task Attempts_OperatorCancelCutsTheStream_IsOurCanceledByTheCaller()
    {
        using var operatorCts = new CancellationTokenSource();
        using var questionCts = CancellationTokenSource.CreateLinkedTokenSource(operatorCts.Token);

        var (result, error) = await CutMidStreamAsync(questionCts, operatorCts, operatorCts.Token);

        Assert.IsAssignableFrom<OperationCanceledException>(error);
        var attempt = Assert.Single(Assert.Single(result.ModelCalls).Attempts);
        Assert.Equal(ProviderAttemptOutcome.OurCanceled, attempt.Outcome);
        Assert.False(attempt.QuestionBudgetCanceled);
        Assert.Equal(ProviderAttemptOutcome.OurCanceled, result.TerminalOutcome);
        Assert.False(result.TerminalQuestionBudgetCanceled);
    }

    [Fact]
    public async Task Attempts_CancelWithNoOperatorToken_IsTheCallersCancelNeverTheBudget()
    {
        // The production chat passes no operator token; its cancels are never a question budget.
        using var runCts = new CancellationTokenSource();

        var (result, error) = await CutMidStreamAsync(runCts, runCts, CancellationToken.None);

        Assert.IsAssignableFrom<OperationCanceledException>(error);
        var attempt = Assert.Single(Assert.Single(result.ModelCalls).Attempts);
        Assert.Equal(ProviderAttemptOutcome.OurCanceled, attempt.Outcome);
        Assert.False(attempt.QuestionBudgetCanceled);
        Assert.False(result.TerminalQuestionBudgetCanceled);
    }

    [Fact]
    public async Task Attempts_QuestionBudgetCutsTheSend_IsOurCanceledByTheBudgetWithoutAThrow()
    {
        using var operatorCts = new CancellationTokenSource();
        using var questionCts = CancellationTokenSource.CreateLinkedTokenSource(operatorCts.Token);
        var provider = new ScriptedProvider(CompletedAnswer, "OpenAI");
        var handler = new FailingHandler(async ct =>
        {
            questionCts.Cancel();
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("Unreachable.");
        });
        var runner = CreateRunner(new[] { provider }, handler, FastRetryConfig());
        var request = Request(provider);
        request.OperatorCancellation = operatorCts.Token;

        var (result, events, error) = await RunCatchingAsync(runner, request, questionCts.Token);

        Assert.Null(error);
        var call = Assert.Single(result.ModelCalls);
        Assert.Equal("canceled", call.ErrorKind);
        var attempt = Assert.Single(call.Attempts);
        Assert.Equal(ProviderAttemptOutcome.OurCanceled, attempt.Outcome);
        Assert.True(attempt.QuestionBudgetCanceled);
        Assert.Null(attempt.HttpStatus);
        Assert.Equal(ProviderAttemptOutcome.OurCanceled, result.TerminalOutcome);
        Assert.True(result.TerminalQuestionBudgetCanceled);
        Assert.DoesNotContain(events, e => e.Type == "error");
    }
}
