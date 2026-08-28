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

        public Dictionary<string, object> BuildChatRequestBody(string modelId, List<object> messageHistory, int? maxOutputTokens, string? thinkingLevel, ToolsForRequest requestTools, string? reasoningMode = null, string? reasoningSummary = null, string? serviceTier = null)
        {
            return new Dictionary<string, object> { { "model", modelId }, { "messages", messageHistory } };
        }

        public object BuildFunctionDeclaration(string name, string description, object parameterSchema)
        {
            return new { name, description, parameterSchema };
        }

        public object? BuildToolsPayload(List<object> providerTools, List<object> functionDeclarations) => null;

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

        public async IAsyncEnumerable<ChatEvent> ParseStreamAsync(HttpResponseMessage response, bool showDebugLog, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
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
            NullLogger<AgentLoopRunner>.Instance);

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
        Assert.Equal("Completed", result.TerminationReason);
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
            NullLogger<AgentLoopRunner>.Instance);

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
}
