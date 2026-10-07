using System.Collections.Generic;
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
using Overseer.Controllers;
using Overseer.Services;
using Overseer.Services.Agents;
using Overseer.Services.Providers;
using Overseer.Services.Tools;
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

    /// <summary>A provider whose stream for each call is a scripted list of events.</summary>
    private sealed class ScriptedProvider : IAiProvider
    {
        private readonly Func<int, IReadOnlyList<ChatEvent>> _script;
        private int _callCount;

        public ScriptedProvider(Func<int, IReadOnlyList<ChatEvent>> script) => _script = script;

        public string ProviderName => "MockProvider";

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
            foreach (var evt in _script(_callCount++))
            {
                yield return evt;
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
            new KnowledgeBaseService(NullLogger<KnowledgeBaseService>.Instance, config),
            new ModelMetadataService(),
            NullLogger<AgentLoopRunner>.Instance,
            new SubAgentCatalogService(config, NullLogger<SubAgentCatalogService>.Instance),
            governor);
    }

    private static AgentRunRequest Request(IAiProvider provider, string modelId = "mock-model", bool showDebugLog = false) => new()
    {
        ProviderName = provider.ProviderName,
        ModelId = modelId,
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
    public async Task RateLimitedThenOk_CountsTheRetryApartFromPermitWait()
    {
        var config = FastRetryConfig();
        var governor = new AiRequestGovernor(config, NullLogger<AiRequestGovernor>.Instance);
        var provider = new ScriptedProvider(AnswerScript);
        var handler = new ScriptedHandler(
            () => Status(HttpStatusCode.TooManyRequests, "{\"error\":{\"message\":\"Rate limit reached.\",\"code\":\"rate_limit_exceeded\"}}"),
            () => Status(HttpStatusCode.OK));
        var runner = CreateRunner(new[] { provider }, handler, config, governor);

        var (result, events) = await RunAsync(runner, Request(provider));

        Assert.Equal(2, handler.CallCount);
        var record = Assert.Single(result.ModelCalls);
        Assert.Equal(1, record.Http429Count);
        Assert.Equal(0, record.Http5xxCount);
        Assert.Equal(2, record.AttemptCount);
        Assert.Equal(200, record.FinalHttpStatus);
        Assert.Null(record.ErrorKind);
        Assert.True(record.PermitWaitMs < 500, $"PermitWaitMs was {record.PermitWaitMs}");
        Assert.True(record.BackoffWaitMs < 500, $"BackoffWaitMs was {record.BackoffWaitMs}");
        Assert.True(record.FailedAttemptMs >= 0);
        Assert.Equal(1, result.RetryAttemptCount);
        Assert.DoesNotContain(events, e => e.Type == "error");
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
}
