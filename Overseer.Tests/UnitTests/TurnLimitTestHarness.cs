using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Overseer.Controllers;
using Overseer.Services;
using Overseer.Services.Agents;
using Overseer.Services.Providers;
using Overseer.Services.Tools;
using Overseer.Tests.Helpers;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>A scripted provider and runner for the turn-limit tests. No network.</summary>
internal static class TurnLimitTestHarness
{
    public const string ToolName = "mock_tool";

    internal sealed class ScriptedProvider : IAiProvider
    {
        private readonly Func<int, IReadOnlyList<ChatEvent>> _script;
        private readonly Func<int, CancellationToken, Task>? _afterEvents;
        private int _callCount;

        public ScriptedProvider(Func<int, IReadOnlyList<ChatEvent>> script, Func<int, CancellationToken, Task>? afterEvents = null, string providerName = "MockProvider")
        {
            _script = script;
            _afterEvents = afterEvents;
            ProviderName = providerName;
        }

        public string ProviderName { get; }

        public int CallCount => _callCount;

        /// <summary>Whether each request declared any tool, in call order.</summary>
        public List<bool> ToolsDeclared { get; } = new();

        /// <summary>The tool results the model received, in order.</summary>
        public List<string> ToolResultsSent { get; } = new();

        /// <summary>The text-only assistant turns appended by recovery.</summary>
        public List<string> AssistantTexts { get; } = new();

        public List<string> UserTexts { get; } = new();

        public IReadOnlyList<string> SupportedServiceTiers => new[] { "default" };

        public void AppendAssistantToolCallsToHistory(List<object> messageHistory, string iterationText, List<JsonElement> toolCalls, List<JsonElement>? providerHistoryItems = null)
            => messageHistory.Add(new { role = "assistant", content = iterationText });

        public void AppendToolResultsToHistory(List<object> messageHistory, List<ProviderToolResult> results)
        {
            foreach (var result in results)
            {
                ToolResultsSent.Add(result.Content);
                messageHistory.Add(new { role = "tool", content = result.Content });
            }
        }

        public void AppendUserTextToHistory(List<object> messageHistory, string text)
        {
            UserTexts.Add(text);
            messageHistory.Add(new { role = "user", content = text });
        }

        public void AppendAssistantTextToHistory(List<object> messageHistory, string visibleText, List<JsonElement>? providerHistoryItems = null)
            => AssistantTexts.Add(visibleText);

        public Dictionary<string, object> BuildChatRequestBody(string modelId, List<object> messageHistory, int? maxOutputTokens, string? thinkingLevel, ToolsForRequest requestTools, string? reasoningMode = null, string? reasoningSummary = null, string? serviceTier = null, bool? parallelToolCalls = null, SegmentedPrompt? segmentedPrompt = null, string? promptCacheKey = null, bool cacheConversationTail = true, bool disablePromptCache = false)
        {
            ToolsDeclared.Add(requestTools.FunctionDeclarations.Count > 0);
            return new() { { "model", modelId } };
        }

        public bool TryRewriteToolResult(List<object> messageHistory, string toolCallId, string replacementText) => false;
        public object BuildFunctionDeclaration(string name, string description, object parameterSchema) => new { name, description, parameterSchema };
        public object? BuildToolsPayload(List<object> providerTools, List<object> functionDeclarations) => new { };
        public object? BuildWebSearchTool() => null;
        public void ConfigureRequest(HttpRequestMessage request, string apiKey, AiEndpointDescriptor endpoint) { }
        public object FormatMessage(string role, string text, List<SendMessageAttachment>? imageAttachments) => new { role, content = text };
        public string GetChatStreamUrl(string modelId, string apiKey, AiEndpointDescriptor endpoint) => endpoint.ComposeUrl("https://mock.ai.test/stream", "/stream");

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
        public Dictionary<string, object> BuildTitleRequestBody(string modelId, string systemPrompt, string userMessage, int maxTokens, string? serviceTier = null) => new();
        public string GetTitleUrl(string modelId, string apiKey, AiEndpointDescriptor endpoint) => "https://mock.ai.test/title";
        public string? ParseTitleResponse(JsonElement root) => "Mock Title";
    }

    internal sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Func<int, HttpResponseMessage> _respond;

        public ScriptedHandler(Func<int, HttpResponseMessage> respond) => _respond = respond;

        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_respond(CallCount++));
    }

    private sealed class FixedHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public FixedHttpClientFactory(HttpMessageHandler handler) => _handler = handler;

        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private sealed class NullClientBridge : IClientToolBridge
    {
        public bool IsClientConnected => false;

        public Task<ToolResult> SendToolRequestAsync(Overseer.Services.Privacy.SessionRef sessionRef, string toolName, JsonElement parameters, CancellationToken cancellationToken)
            => Task.FromResult(new ToolResult { Success = false, Content = "No client." });
    }

    private sealed class MockToolHandler : IToolHandler
    {
        private readonly string _content;

        public MockToolHandler(string content) => _content = content;

        public string ToolName => TurnLimitTestHarness.ToolName;
        public string Description { get; set; } = "A mock tool";
        public ToolExecutionLocation ExecutionLocation => ToolExecutionLocation.Server;
        public ToolCategory Category => ToolCategory.InformationRetrieval;
        public int TimeoutSeconds => 10;
        public JsonElement ParameterSchema => JsonSerializer.SerializeToElement(new { type = "object" });

        public Task<ToolResult> ExecuteAsync(JsonElement parameters, ToolExecutionContext context, CancellationToken cancellationToken)
            => Task.FromResult(new ToolResult { Success = true, Content = _content });
    }

    public static HttpResponseMessage Ok() =>
        new(HttpStatusCode.OK) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Status(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    public static AgentLoopRunner CreateRunner(
        IAiProvider provider, HttpMessageHandler? handler = null, string toolContent = "Tool ran successfully.",
        Dictionary<string, string?>? settings = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["AiRateLimitSettings:MaxRetryAfterSeconds"] = "0"
        };
        foreach (var pair in settings ?? new())
        {
            values[pair.Key] = pair.Value;
        }
        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        var sp = services.BuildServiceProvider();

        var handlers = new List<IToolHandler> { new MockToolHandler(toolContent) };
        var clientBridge = new NullClientBridge();

        return new AgentLoopRunner(
            new[] { provider },
            new ToolRegistry(handlers, clientBridge, NullLogger<ToolRegistry>.Instance),
            new ToolExecutor(handlers, clientBridge, NullLogger<ToolExecutor>.Instance, new MemoryCache(new MemoryCacheOptions()), config),
            new FixedHttpClientFactory(handler ?? new ScriptedHandler(_ => Ok())),
            config,
            sp.GetRequiredService<IServiceScopeFactory>(),
            TestKnowledgeBase.Empty,
            new ModelMetadataService(),
            NullLogger<AgentLoopRunner>.Instance,
            new SubAgentCatalogService(config, NullLogger<SubAgentCatalogService>.Instance));
    }

    public static AgentRunRequest Request(IAiProvider provider, bool guidance = true, bool report = true, int maxToolIterations = 22) => new()
    {
        ProviderName = provider.ProviderName,
        ModelId = "mock-model",
        ApiKey = "test-key",
        CredentialKey = "turn-limit-test:key",
        SeedHistory = new List<object> { new { role = "user", content = "Hello" } },
        AiProvider = provider,
        MaxToolIterations = maxToolIterations,
        EnableToolUse = true,
        ToolExecutionContext = new ToolExecutionContext
        {
            SessionId = Overseer.Services.Privacy.SessionRef.Persistent(1),
            MaxResultLength = 200000,
            MaxCallsPerSession = 1000
        },
        ApplyTurnGuidance = guidance,
        ReportTurnLimits = report
    };

    public static async Task<(AgentRunResult Result, List<ChatEvent> Events)> RunAsync(
        AgentLoopRunner runner, AgentRunRequest request, AgentRunBudget? budget = null, CancellationToken? token = null)
    {
        var result = new AgentRunResult();
        var events = new List<ChatEvent>();
        await foreach (var evt in runner.RunAsync(request, budget, result, token ?? TestContext.Current.CancellationToken))
        {
            events.Add(evt);
        }
        return (result, events);
    }

    public static ChatEvent Done()
    {
        var meta = new ProviderCallMeta();
        meta.MarkCompleted(ProviderCallMeta.Now());
        return new ChatEvent { Type = "call_meta", CallMeta = meta };
    }

    /// <summary>What a provider reports at the end of a stream that closed without a completion marker.</summary>
    public static ChatEvent ClosedEarly() => new() { Type = "call_meta", CallMeta = new ProviderCallMeta() };

    public static ChatEvent Text(string text) => new() { Type = "chunk", Data = text };

    public static ChatEvent Phase(string phase) => new() { Type = "assistant_phase", Data = phase };

    public static ChatEvent ToolCall(int id = 1) => new()
    {
        Type = "tool_call_complete",
        Data = JsonSerializer.Serialize(new { id = $"call_{id}", name = ToolName, arguments = "{}" })
    };

    /// <summary>One tool call per response, for every response.</summary>
    public static IReadOnlyList<ChatEvent> AlwaysTools(int callIndex) => new[] { ToolCall(callIndex), Done() };
}
