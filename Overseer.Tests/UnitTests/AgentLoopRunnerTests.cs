using System.Text.Json;
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
using Xunit;

namespace Overseer.Tests.UnitTests;

public class AgentLoopRunnerTests
{
    private class MockAiProvider : IAiProvider
    {
        public string ProviderName => "MockProvider";

        public IReadOnlyList<string> SupportedServiceTiers => new[] { "default" };

        public void AppendAssistantToolCallsToHistory(List<object> messageHistory, string iterationText, List<JsonElement> toolCalls, List<JsonElement>? providerHistoryItems = null)
        {
            messageHistory.Add(new { role = "assistant", content = iterationText });
        }

        public void AppendToolResultsToHistory(List<object> messageHistory, List<ProviderToolResult> results)
        {
            foreach (var tr in results)
            {
                messageHistory.Add(new { role = "tool", content = tr.Content });
            }
        }

        public Dictionary<string, object> BuildChatRequestBody(string modelId, List<object> messageHistory, int? maxOutputTokens, string? thinkingLevel, ToolsForRequest requestTools, string? reasoningMode = null, string? reasoningSummary = null, string? serviceTier = null, bool? parallelToolCalls = null)
        {
            return new Dictionary<string, object> { { "model", modelId }, { "messages", messageHistory } };
        }

        public object BuildFunctionDeclaration(string name, string description, object parameterSchema)
        {
            return new { name, description, parameterSchema };
        }

        public virtual object? BuildToolsPayload(List<object> providerTools, List<object> functionDeclarations) => null;

        public object? BuildWebSearchTool() => null;

        public void ConfigureRequest(HttpRequestMessage request, string apiKey)
        {
            request.Headers.Add("X-Mock-ApiKey", apiKey);
        }

        public object FormatMessage(string role, string text, List<SendMessageAttachment>? imageAttachments)
        {
            return new { role, content = text };
        }

        public string GetChatStreamUrl(string modelId, string apiKey)
        {
            return "https://mock.ai.test/stream";
        }

        public virtual async IAsyncEnumerable<ChatEvent> ParseStreamAsync(HttpResponseMessage response, bool showDebugLog, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            yield return new ChatEvent { Type = "chunk", Data = "Hello from Mock AI!" };
            await Task.CompletedTask;
        }

        public List<object> PrepareMessageHistory(List<object> messages)
        {
            return new List<object>(messages);
        }

        public Dictionary<string, object> BuildTitleRequestBody(string modelId, string systemPrompt, string userMessage, int maxTokens, string? serviceTier = null)
        {
            return new Dictionary<string, object>();
        }

        public string GetTitleUrl(string modelId, string apiKey) => "https://mock.ai.test/title";

        public string? ParseTitleResponse(JsonElement root) => "Mock Title";
    }

    private class NullClientBridge : IClientToolBridge
    {
        public bool IsClientConnected => true;
        public Task<ToolResult> SendToolRequestAsync(long sessionId, string toolName, JsonElement parameters, CancellationToken cancellationToken)
        {
            return Task.FromResult(new ToolResult { Success = true, Content = "Client result" });
        }
    }

    private class MockHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("{\"mock\":true}")
            };
            return Task.FromResult(response);
        }
    }

    private class MockHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new HttpClient(new MockHttpMessageHandler());
        }
    }

    [Fact]
    public async Task RunAsync_Coordinator_EmitsExpectedEventsAndMainChatDebugPrefixes()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var sp = services.BuildServiceProvider();

        var mockProvider = new MockAiProvider();
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        using var cache = new MemoryCache(new MemoryCacheOptions());

        var clientBridge = new NullClientBridge();
        var handlers = new List<IToolHandler>();
        var toolRegistry = new ToolRegistry(handlers, clientBridge, NullLogger<ToolRegistry>.Instance);
        var toolExecutor = new ToolExecutor(handlers, clientBridge, NullLogger<ToolExecutor>.Instance, cache, config);

        var runner = new AgentLoopRunner(
            new[] { mockProvider },
            toolRegistry,
            toolExecutor,
            new MockHttpClientFactory(),
            config,
            sp.GetRequiredService<IServiceScopeFactory>(),
            new KnowledgeBaseService(NullLogger<KnowledgeBaseService>.Instance, config),
            new ModelMetadataService(),
            NullLogger<AgentLoopRunner>.Instance,
            new SubAgentCatalogService(config, NullLogger<SubAgentCatalogService>.Instance));

        var request = new AgentRunRequest
        {
            ProviderName = "MockProvider",
            ModelId = "mock-model",
            ApiKey = "test-key",
            ShowDebugLog = true,
            SeedHistory = new List<object> { new { role = "user", content = "Hello" } },
            AiProvider = mockProvider
        };

        var result = new AgentRunResult();
        var events = new List<ChatEvent>();

        await foreach (var evt in runner.RunAsync(request, null, result, CancellationToken.None))
        {
            events.Add(evt);
        }

        Assert.Equal("Hello from Mock AI!", result.FinalText);
        Assert.Equal("completed", result.TerminationReason);
        Assert.True(result.TimeToFirstTokenMs.HasValue);

        // Verify [Main Chat - MockProvider] prefix was emitted for debug events
        var debugEvents = events.Where(e => e.Type == "debug").ToList();
        Assert.NotEmpty(debugEvents);
        Assert.Contains(debugEvents, d => d.Data != null && d.Data.StartsWith("[Main Chat - MockProvider]"));
    }

    [Fact]
    public async Task RunAsync_SubAgent_EmitsSubAgentPrefixes()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var sp = services.BuildServiceProvider();

        var mockProvider = new MockAiProvider();
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        using var cache = new MemoryCache(new MemoryCacheOptions());

        var clientBridge = new NullClientBridge();
        var handlers = new List<IToolHandler>();
        var toolRegistry = new ToolRegistry(handlers, clientBridge, NullLogger<ToolRegistry>.Instance);
        var toolExecutor = new ToolExecutor(handlers, clientBridge, NullLogger<ToolExecutor>.Instance, cache, config);

        var runner = new AgentLoopRunner(
            new[] { mockProvider },
            toolRegistry,
            toolExecutor,
            new MockHttpClientFactory(),
            config,
            sp.GetRequiredService<IServiceScopeFactory>(),
            new KnowledgeBaseService(NullLogger<KnowledgeBaseService>.Instance, config),
            new ModelMetadataService(),
            NullLogger<AgentLoopRunner>.Instance,
            new SubAgentCatalogService(config, NullLogger<SubAgentCatalogService>.Instance));

        var request = new AgentRunRequest
        {
            ProviderName = "MockProvider",
            ModelId = "mock-model",
            ApiKey = "test-key",
            AgentName = "wiki_researcher",
            ShowDebugLog = true,
            SeedHistory = new List<object> { new { role = "user", content = "Lookup" } },
            AiProvider = mockProvider
        };

        var result = new AgentRunResult();
        var events = new List<ChatEvent>();

        await foreach (var evt in runner.RunAsync(request, null, result, CancellationToken.None))
        {
            events.Add(evt);
        }

        Assert.Equal("Hello from Mock AI!", result.FinalText);

        // Verify [SubAgent:wiki_researcher - MockProvider] prefix was emitted for debug events
        var debugEvents = events.Where(e => e.Type == "debug").ToList();
        Assert.NotEmpty(debugEvents);
        Assert.Contains(debugEvents, d => d.Data != null && d.Data.StartsWith("[SubAgent:wiki_researcher - MockProvider]"));
    }

    private class ToolCallingMockProvider : MockAiProvider
    {
        private int _callCount = 0;

        public override async IAsyncEnumerable<ChatEvent> ParseStreamAsync(HttpResponseMessage response, bool showDebugLog, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            _callCount++;
            if (_callCount <= 2)
            {
                var tcJson = JsonSerializer.Serialize(new { id = $"call_{_callCount}", name = "mock_tool", arguments = "{}" });
                yield return new ChatEvent { Type = "tool_call_complete", Data = tcJson };
            }
            else
            {
                yield return new ChatEvent { Type = "chunk", Data = "Final summary" };
            }
            await Task.CompletedTask;
        }

        public override object? BuildToolsPayload(List<object> providerTools, List<object> functionDeclarations) => new { };
    }

    private class MockToolHandler : IToolHandler
    {
        public string ToolName => "mock_tool";
        public string Description { get; set; } = "A mock tool";
        public ToolExecutionLocation ExecutionLocation => ToolExecutionLocation.Server;
        public ToolCategory Category => ToolCategory.InformationRetrieval;
        public int TimeoutSeconds => 10;
        public JsonElement ParameterSchema => JsonSerializer.SerializeToElement(new { type = "object" });
        public Task<ToolResult> ExecuteAsync(JsonElement parameters, ToolExecutionContext context, CancellationToken cancellationToken)
        {
            return Task.FromResult(new ToolResult { Success = true, Content = "Tool ran successfully." });
        }
    }

    [Fact]
    public async Task RunAsync_WhenIterationLimitReached_SetsIterationLimitTerminationReason()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var sp = services.BuildServiceProvider();

        var toolCallingProvider = new ToolCallingMockProvider();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            { "AiPerformanceSettings:MaxToolIterations:Default", "1" }
        }).Build();
        using var cache = new MemoryCache(new MemoryCacheOptions());

        var clientBridge = new NullClientBridge();
        var handlers = new List<IToolHandler> { new MockToolHandler() };
        var toolRegistry = new ToolRegistry(handlers, clientBridge, NullLogger<ToolRegistry>.Instance);
        var toolExecutor = new ToolExecutor(handlers, clientBridge, NullLogger<ToolExecutor>.Instance, cache, config);

        var runner = new AgentLoopRunner(
            new[] { toolCallingProvider },
            toolRegistry,
            toolExecutor,
            new MockHttpClientFactory(),
            config,
            sp.GetRequiredService<IServiceScopeFactory>(),
            new KnowledgeBaseService(NullLogger<KnowledgeBaseService>.Instance, config),
            new ModelMetadataService(),
            NullLogger<AgentLoopRunner>.Instance,
            new SubAgentCatalogService(config, NullLogger<SubAgentCatalogService>.Instance));

        var request = new AgentRunRequest
        {
            ProviderName = "MockProvider",
            ModelId = "mock-model",
            ApiKey = "test-key",
            MaxToolIterations = 1,
            SeedHistory = new List<object> { new { role = "user", content = "Run tools" } },
            AiProvider = toolCallingProvider
        };

        var result = new AgentRunResult();
        await foreach (var _ in runner.RunAsync(request, null, result, CancellationToken.None)) { }

        Assert.Equal("iteration_limit", result.TerminationReason);
    }

    [Fact]
    public async Task RunAsync_WhenBudgetExhausted_SetsBudgetExhaustedTerminationReason()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var sp = services.BuildServiceProvider();

        var toolCallingProvider = new ToolCallingMockProvider();
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        using var cache = new MemoryCache(new MemoryCacheOptions());

        var clientBridge = new NullClientBridge();
        var handlers = new List<IToolHandler> { new MockToolHandler() };
        var toolRegistry = new ToolRegistry(handlers, clientBridge, NullLogger<ToolRegistry>.Instance);
        var toolExecutor = new ToolExecutor(handlers, clientBridge, NullLogger<ToolExecutor>.Instance, cache, config);

        var runner = new AgentLoopRunner(
            new[] { toolCallingProvider },
            toolRegistry,
            toolExecutor,
            new MockHttpClientFactory(),
            config,
            sp.GetRequiredService<IServiceScopeFactory>(),
            new KnowledgeBaseService(NullLogger<KnowledgeBaseService>.Instance, config),
            new ModelMetadataService(),
            NullLogger<AgentLoopRunner>.Instance,
            new SubAgentCatalogService(config, NullLogger<SubAgentCatalogService>.Instance));

        var request = new AgentRunRequest
        {
            ProviderName = "MockProvider",
            ModelId = "mock-model",
            ApiKey = "test-key",
            SeedHistory = new List<object> { new { role = "user", content = "Run tools" } },
            AiProvider = toolCallingProvider
        };

        var budget = new AgentRunBudget { MaxSubAgentRuns = 3, MaxTotalModelCalls = 1 };
        var result = new AgentRunResult();
        await foreach (var _ in runner.RunAsync(request, budget, result, CancellationToken.None)) { }

        Assert.Equal("budget_exhausted", result.TerminationReason);
    }

    private class ToolEmittingMockProvider : MockAiProvider
    {
        private readonly string _toolName;
        private readonly string _arguments;

        public ToolEmittingMockProvider(string toolName, string arguments)
        {
            _toolName = toolName;
            _arguments = arguments;
        }

        public override async IAsyncEnumerable<ChatEvent> ParseStreamAsync(HttpResponseMessage response, bool showDebugLog, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var tcJson = JsonSerializer.Serialize(new { id = "call_1", name = _toolName, arguments = _arguments });
            yield return new ChatEvent { Type = "tool_call_complete", Data = tcJson };
            yield return new ChatEvent { Type = "chunk", Data = "Done." };
            await Task.CompletedTask;
        }

        public override object? BuildToolsPayload(List<object> providerTools, List<object> functionDeclarations) => new { };
    }

    [Fact]
    public async Task ToolStart_ForDelegateToSubAgent_IncludesDisplayName()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var sp = services.BuildServiceProvider();

        var provider = new ToolEmittingMockProvider(
            "delegate_to_subagent",
            "{\"agent_name\":\"wiki_researcher\",\"task\":\"Stats\",\"subagent_name\":\"Rakshasa stats researcher\"}");

        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        using var cache = new MemoryCache(new MemoryCacheOptions());

        var clientBridge = new NullClientBridge();
        var handlers = new List<IToolHandler> { new MockToolHandler() };
        var toolRegistry = new ToolRegistry(handlers, clientBridge, NullLogger<ToolRegistry>.Instance);
        var toolExecutor = new ToolExecutor(handlers, clientBridge, NullLogger<ToolExecutor>.Instance, cache, config);

        var runner = new AgentLoopRunner(
            new[] { provider },
            toolRegistry,
            toolExecutor,
            new MockHttpClientFactory(),
            config,
            sp.GetRequiredService<IServiceScopeFactory>(),
            new KnowledgeBaseService(NullLogger<KnowledgeBaseService>.Instance, config),
            new ModelMetadataService(),
            NullLogger<AgentLoopRunner>.Instance,
            new SubAgentCatalogService(config, NullLogger<SubAgentCatalogService>.Instance));

        var request = new AgentRunRequest
        {
            ProviderName = "MockProvider",
            ModelId = "mock-model",
            ApiKey = "test-key",
            SeedHistory = new List<object> { new { role = "user", content = "Run" } },
            AiProvider = provider
        };

        var events = new List<ChatEvent>();
        var result = new AgentRunResult();
        await foreach (var evt in runner.RunAsync(request, null, result, CancellationToken.None))
        {
            events.Add(evt);
        }

        var toolStart = events.FirstOrDefault(e => e.Type == "tool_start");
        Assert.NotNull(toolStart);
        Assert.NotNull(toolStart.Data);

        using var doc = JsonDocument.Parse(toolStart.Data);
        Assert.True(doc.RootElement.TryGetProperty("display_name", out var dispProp));
        Assert.Equal("Invoking wiki researcher subagent: Rakshasa stats researcher", dispProp.GetString());
    }

    [Fact]
    public async Task ToolStart_ForOtherTools_OmitsDisplayName()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var sp = services.BuildServiceProvider();

        var provider = new ToolEmittingMockProvider("mock_tool", "{}");

        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        using var cache = new MemoryCache(new MemoryCacheOptions());

        var clientBridge = new NullClientBridge();
        var handlers = new List<IToolHandler> { new MockToolHandler() };
        var toolRegistry = new ToolRegistry(handlers, clientBridge, NullLogger<ToolRegistry>.Instance);
        var toolExecutor = new ToolExecutor(handlers, clientBridge, NullLogger<ToolExecutor>.Instance, cache, config);

        var runner = new AgentLoopRunner(
            new[] { provider },
            toolRegistry,
            toolExecutor,
            new MockHttpClientFactory(),
            config,
            sp.GetRequiredService<IServiceScopeFactory>(),
            new KnowledgeBaseService(NullLogger<KnowledgeBaseService>.Instance, config),
            new ModelMetadataService(),
            NullLogger<AgentLoopRunner>.Instance,
            new SubAgentCatalogService(config, NullLogger<SubAgentCatalogService>.Instance));

        var request = new AgentRunRequest
        {
            ProviderName = "MockProvider",
            ModelId = "mock-model",
            ApiKey = "test-key",
            SeedHistory = new List<object> { new { role = "user", content = "Run" } },
            AiProvider = provider
        };

        var events = new List<ChatEvent>();
        var result = new AgentRunResult();
        await foreach (var evt in runner.RunAsync(request, null, result, CancellationToken.None))
        {
            events.Add(evt);
        }

        var toolStart = events.FirstOrDefault(e => e.Type == "tool_start");
        Assert.NotNull(toolStart);
        Assert.NotNull(toolStart.Data);

        using var doc = JsonDocument.Parse(toolStart.Data);
        Assert.False(doc.RootElement.TryGetProperty("display_name", out _));
    }
}
