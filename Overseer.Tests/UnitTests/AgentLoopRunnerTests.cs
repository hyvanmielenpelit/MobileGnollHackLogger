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

        public virtual Dictionary<string, object> BuildChatRequestBody(string modelId, List<object> messageHistory, int? maxOutputTokens, string? thinkingLevel, ToolsForRequest requestTools, string? reasoningMode = null, string? reasoningSummary = null, string? serviceTier = null, bool? parallelToolCalls = null, SegmentedPrompt? segmentedPrompt = null, string? promptCacheKey = null)
        {
            return new Dictionary<string, object> { { "model", modelId }, { "messages", messageHistory } };
        }

        public bool TryRewriteToolResult(List<object> messageHistory, string toolCallId, string replacementText) => false;

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

        public virtual List<object> PrepareMessageHistory(List<object> messages)
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

    /// <summary>
    /// Captures the message history as the provider actually received it, and converts a
    /// provider-neutral <c>{ role, content }</c> message the way <c>GoogleProvider</c> does —
    /// which is the conversion the benchmark paths were missing.
    /// </summary>
    private class HistoryCapturingMockProvider : MockAiProvider
    {
        public List<object>? CapturedHistory { get; private set; }

        public int PrepareCallCount { get; private set; }

        public override List<object> PrepareMessageHistory(List<object> messages)
        {
            PrepareCallCount++;

            var formatted = new List<object>();
            foreach (var msg in messages)
            {
                var role = ProviderHelper.GetProperty(msg, "role")?.ToString() ?? "user";
                if (ProviderHelper.GetProperty(msg, "parts") != null)
                {
                    formatted.Add(msg);          // already provider-shaped: passthrough
                    continue;
                }

                var content = ProviderHelper.GetProperty(msg, "content")?.ToString() ?? "";
                var mappedRole = (role == "assistant" || role == "model") ? "model" : role;
                formatted.Add(new { role = mappedRole, parts = new[] { new { text = content } } });
            }
            return formatted;
        }

        public override Dictionary<string, object> BuildChatRequestBody(string modelId, List<object> messageHistory, int? maxOutputTokens, string? thinkingLevel, ToolsForRequest requestTools, string? reasoningMode = null, string? reasoningSummary = null, string? serviceTier = null, bool? parallelToolCalls = null, SegmentedPrompt? segmentedPrompt = null, string? promptCacheKey = null)
        {
            CapturedHistory ??= new List<object>(messageHistory);
            return base.BuildChatRequestBody(modelId, messageHistory, maxOutputTokens, thinkingLevel, requestTools, reasoningMode, reasoningSummary, serviceTier, parallelToolCalls, segmentedPrompt, promptCacheKey);
        }
    }

    private static AgentLoopRunner CreateRunner(IAiProvider provider, IConfiguration config, MemoryCache cache, IServiceProvider sp)
    {
        var clientBridge = new NullClientBridge();
        var handlers = new List<IToolHandler>();
        var toolRegistry = new ToolRegistry(handlers, clientBridge, NullLogger<ToolRegistry>.Instance);
        var toolExecutor = new ToolExecutor(handlers, clientBridge, NullLogger<ToolExecutor>.Instance, cache, config);

        return new AgentLoopRunner(
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
    }

    private static async Task<HistoryCapturingMockProvider> RunAndCaptureHistoryAsync(AgentRunRequest request)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var sp = services.BuildServiceProvider();
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        using var cache = new MemoryCache(new MemoryCacheOptions());

        var provider = new HistoryCapturingMockProvider();
        request.ProviderName = "MockProvider";
        request.AiProvider = provider;

        var runner = CreateRunner(provider, config, cache, sp);
        var result = new AgentRunResult();
        await foreach (var _ in runner.RunAsync(request, null, result, CancellationToken.None)) { }

        Assert.NotNull(provider.CapturedHistory);
        return provider;
    }

    private static string RoleOf(object message) =>
        ProviderHelper.GetProperty(message, "role")?.ToString() ?? "";

    private static string TextOf(object message) =>
        JsonSerializer.Serialize(ProviderHelper.GetProperty(message, "parts"));

    [Fact]
    public async Task RunAsync_NormalizesRawSeedHistoryThroughTheProvider()
    {
        // The reported Gemini failure: BenchmarkService hands over { role, content } and
        // never calls PrepareMessageHistory, so Google received a `content` field it rejects.
        var provider = await RunAndCaptureHistoryAsync(new AgentRunRequest
        {
            ModelId = "mock-model",
            ApiKey = "test-key",
            SeedHistory = new List<object> { new { role = "user", content = "Hello" } }
        });

        var history = provider.CapturedHistory!;
        Assert.Single(history);
        Assert.Equal("user", RoleOf(history[0]));
        Assert.Equal("[{\"text\":\"Hello\"}]", TextOf(history[0]));
        Assert.Null(ProviderHelper.GetProperty(history[0], "content"));
    }

    [Fact]
    public async Task RunAsync_LeavesAnAlreadyProviderShapedSeedHistoryUnchanged()
    {
        // ChatService and DelegateToSubAgentTool already normalize before handing over, so
        // the runner's own pass has to be idempotent for them.
        var provider = await RunAndCaptureHistoryAsync(new AgentRunRequest
        {
            ModelId = "mock-model",
            ApiKey = "test-key",
            SeedHistory = new List<object>
            {
                new { role = "user", parts = new[] { new { text = "Already shaped" } } }
            }
        });

        var history = provider.CapturedHistory!;
        Assert.Single(history);
        Assert.Equal("user", RoleOf(history[0]));
        Assert.Equal("[{\"text\":\"Already shaped\"}]", TextOf(history[0]));
    }

    [Fact]
    public async Task RunAsync_InjectsSystemPromptWhenTheHistoryHasNone()
    {
        // AgentRunRequest.SystemPrompt was read nowhere, so every BenchmarkService path ran
        // with no system prompt at all.
        var provider = await RunAndCaptureHistoryAsync(new AgentRunRequest
        {
            ModelId = "mock-model",
            ApiKey = "test-key",
            SystemPrompt = "You are an objective game mechanics expert.",
            SeedHistory = new List<object> { new { role = "user", content = "rate this" } }
        });

        var history = provider.CapturedHistory!;
        Assert.Equal(2, history.Count);
        Assert.Equal("system", RoleOf(history[0]));
        Assert.Equal("[{\"text\":\"You are an objective game mechanics expert.\"}]", TextOf(history[0]));
        Assert.Equal("user", RoleOf(history[1]));
        Assert.Single(history, m => RoleOf(m) == "system");
    }

    [Fact]
    public async Task RunAsync_DoesNotInjectSystemPromptWhenTheCallerSuppliedOne()
    {
        // ChatService sets both SystemPrompt and an explicit system message; injecting again
        // would duplicate the whole prompt.
        var provider = await RunAndCaptureHistoryAsync(new AgentRunRequest
        {
            ModelId = "mock-model",
            ApiKey = "test-key",
            SystemPrompt = "Injected copy",
            SeedHistory = new List<object>
            {
                new { role = "system", content = "The caller own prompt" },
                new { role = "user", content = "hi" }
            }
        });

        var history = provider.CapturedHistory!;
        Assert.Equal(2, history.Count);
        Assert.Single(history, m => RoleOf(m) == "system");
        Assert.Equal("[{\"text\":\"The caller own prompt\"}]", TextOf(history[0]));
    }

    [Fact]
    public async Task RunAsync_DoesNotInjectSystemPromptWhenASegmentedPromptIsInPlay()
    {
        // With a segmented prompt the providers build `system` from the segments; a history
        // entry would be a second, competing source.
        var provider = await RunAndCaptureHistoryAsync(new AgentRunRequest
        {
            ModelId = "mock-model",
            ApiKey = "test-key",
            SystemPrompt = "Should not be injected",
            SegmentedPrompt = new SegmentedPrompt("frozen", "session", "volatile"),
            SeedHistory = new List<object> { new { role = "user", content = "hi" } }
        });

        var history = provider.CapturedHistory!;
        Assert.Single(history);
        Assert.Equal("user", RoleOf(history[0]));
        Assert.DoesNotContain(history, m => RoleOf(m) == "system");
    }

    /// <summary>
    /// Emits two usage reports in one stream, the way a multi-iteration turn does: each
    /// iteration re-sends the whole conversation, so the prompt count grows and only the
    /// last report describes the real context occupancy.
    /// </summary>
    private class TwoUsageReportsMockProvider : MockAiProvider
    {
        public override async IAsyncEnumerable<ChatEvent> ParseStreamAsync(HttpResponseMessage response, bool showDebugLog, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            yield return new ChatEvent { Type = "chunk", Data = "Answer" };
            yield return new ChatEvent
            {
                Type = "usage",
                UsageReport = new TokenUsageReport { TotalPromptTokens = 1000, OutputTokens = 100 }
            };
            yield return new ChatEvent
            {
                Type = "usage",
                UsageReport = new TokenUsageReport { TotalPromptTokens = 3000, OutputTokens = 200 }
            };
            await Task.CompletedTask;
        }
    }

    [Fact]
    public async Task RunAsync_UsageReports_AccumulateTotalsButLastReportWinsForContext()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var sp = services.BuildServiceProvider();

        var mockProvider = new TwoUsageReportsMockProvider();
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
            SeedHistory = new List<object> { new { role = "user", content = "Hello" } },
            AiProvider = mockProvider
        };

        var result = new AgentRunResult();
        await foreach (var _ in runner.RunAsync(request, null, result, CancellationToken.None))
        {
        }

        // Accumulation is preserved - other consumers (billing, budgets) still depend on it.
        Assert.Equal(4000, result.TotalPromptTokens);
        Assert.Equal(300, result.OutputTokens);

        // Context occupancy is last-report-wins, never the sum.
        Assert.Equal(3000, result.LastPromptTokens);
        Assert.Equal(200, result.LastOutputTokens);
    }
}
