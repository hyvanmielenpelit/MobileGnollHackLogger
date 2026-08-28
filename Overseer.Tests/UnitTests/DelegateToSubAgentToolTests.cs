using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MobileGnollHackLogger.Data;
using Overseer.Controllers;
using Overseer.Services;
using Overseer.Services.Agents;
using Overseer.Services.Providers;
using Overseer.Services.Tools;
using Xunit;

namespace Overseer.Tests.UnitTests;

public class DelegateToSubAgentToolTests
{
    [Fact]
    public async Task ExecuteAsync_MissingParameters_ReturnsError()
    {
        var services = new ServiceCollection();
        var sp = services.BuildServiceProvider();
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();

        var catalogService = new SubAgentCatalogService(config, NullLogger<SubAgentCatalogService>.Instance);
        var manager = new OngoingChatManager(config);
        var metadata = new ModelMetadataService();

        var tool = new DelegateToSubAgentTool(
            catalogService,
            manager,
            sp.GetRequiredService<IServiceScopeFactory>(),
            config,
            metadata,
            NullLogger<DelegateToSubAgentTool>.Instance);

        var context = new ToolExecutionContext { SessionId = 1 };
        var emptyParams = JsonDocument.Parse("{}").RootElement;

        var result = await tool.ExecuteAsync(emptyParams, context, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("Missing required parameters", result.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAsync_MaxDepthExceeded_ReturnsError()
    {
        var services = new ServiceCollection();
        var sp = services.BuildServiceProvider();
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();

        var catalogService = new SubAgentCatalogService(config, NullLogger<SubAgentCatalogService>.Instance);
        var manager = new OngoingChatManager(config);
        var metadata = new ModelMetadataService();

        var tool = new DelegateToSubAgentTool(
            catalogService,
            manager,
            sp.GetRequiredService<IServiceScopeFactory>(),
            config,
            metadata,
            NullLogger<DelegateToSubAgentTool>.Instance);

        var context = new ToolExecutionContext { SessionId = 1, AgentDepth = 1, MaxAgentDepth = 1 };
        var validParams = JsonDocument.Parse("{\"agent_name\":\"wiki_researcher\",\"task\":\"look up AC\"}").RootElement;

        var result = await tool.ExecuteAsync(validParams, context, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("Maximum subagent recursion depth", result.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAsync_UnknownSubAgent_ReturnsError()
    {
        var services = new ServiceCollection();
        var sp = services.BuildServiceProvider();
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();

        var catalogService = new SubAgentCatalogService(config, NullLogger<SubAgentCatalogService>.Instance);
        var manager = new OngoingChatManager(config);
        var metadata = new ModelMetadataService();

        var tool = new DelegateToSubAgentTool(
            catalogService,
            manager,
            sp.GetRequiredService<IServiceScopeFactory>(),
            config,
            metadata,
            NullLogger<DelegateToSubAgentTool>.Instance);

        var context = new ToolExecutionContext { SessionId = 1, AgentDepth = 0, MaxAgentDepth = 1 };
        var unknownParams = JsonDocument.Parse("{\"agent_name\":\"nonexistent_agent\",\"task\":\"do work\"}").RootElement;

        var result = await tool.ExecuteAsync(unknownParams, context, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("not registered", result.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAsync_BudgetExhausted_ReturnsError()
    {
        var services = new ServiceCollection();
        var sp = services.BuildServiceProvider();
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();

        var catalogService = new SubAgentCatalogService(config, NullLogger<SubAgentCatalogService>.Instance);
        var manager = new OngoingChatManager(config);
        var metadata = new ModelMetadataService();

        var tool = new DelegateToSubAgentTool(
            catalogService,
            manager,
            sp.GetRequiredService<IServiceScopeFactory>(),
            config,
            metadata,
            NullLogger<DelegateToSubAgentTool>.Instance);

        var budget = new AgentRunBudget
        {
            MaxSubAgentRuns = 1
        };
        // Exhaust the budget
        budget.TryStartSubAgent(false, out _);

        var context = new ToolExecutionContext
        {
            SessionId = 1,
            AgentDepth = 0,
            MaxAgentDepth = 1,
            Budget = budget
        };

        var validParams = JsonDocument.Parse("{\"agent_name\":\"wiki_researcher\",\"task\":\"look up AC\"}").RootElement;

        var result = await tool.ExecuteAsync(validParams, context, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("run limit", result.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAsync_InheritedModelRow_ResolvesMaxOutputTokens_And_ThinkingLevel()
    {
        var (tool, db, mockProvider, _) = CreateTestSetup();
        var session = new ChatSession { Id = 10, AspNetUserId = "user10", Title = "Test", CreatedUtc = DateTime.UtcNow, LastMessageUtc = DateTime.UtcNow };
        db.ChatSession.Add(session);
        db.UserAiModels.Add(new UserAiModel
        {
            Id = 10,
            AspNetUserId = "user10",
            Provider = "OpenAI",
            ModelId = "gpt-5.6-luna",
            MaxOutputTokens = 50000,
            ThinkingLevel = "high",
            OrderIndex = 0
        });
        await db.SaveChangesAsync();

        var context = new ToolExecutionContext { SessionId = 10, AgentDepth = 0, MaxAgentDepth = 1 };
        var validParams = JsonDocument.Parse("{\"agent_name\":\"wiki_researcher\",\"task\":\"compare prayers\"}").RootElement;

        var result = await tool.ExecuteAsync(validParams, context, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(50000, mockProvider.LastMaxOutputTokens);
        Assert.Equal("high", mockProvider.LastThinkingLevel);
    }

    [Fact]
    public async Task ExecuteAsync_CatalogFallback_ResolvesMaxOutputTokens_To128000()
    {
        var (tool, db, mockProvider, _) = CreateTestSetup();
        var session = new ChatSession { Id = 20, AspNetUserId = "user20", Title = "Test", CreatedUtc = DateTime.UtcNow, LastMessageUtc = DateTime.UtcNow };
        db.ChatSession.Add(session);
        db.UserAiModels.Add(new UserAiModel
        {
            Id = 20,
            AspNetUserId = "user20",
            Provider = "OpenAI",
            ModelId = "gpt-5.6-luna",
            MaxOutputTokens = null,
            OrderIndex = 0
        });
        await db.SaveChangesAsync();

        var context = new ToolExecutionContext { SessionId = 20, AgentDepth = 0, MaxAgentDepth = 1 };
        var validParams = JsonDocument.Parse("{\"agent_name\":\"wiki_researcher\",\"task\":\"compare prayers\"}").RootElement;

        var result = await tool.ExecuteAsync(validParams, context, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(128000, mockProvider.LastMaxOutputTokens);
    }

    [Fact]
    public async Task ExecuteAsync_SubAgentDef_MaxOutputTokens_OverridesInherited()
    {
        var customAgent = new SubAgentDefinition
        {
            Name = "custom_capped",
            DisplayName = "Custom Capped Agent",
            Instructions = "Answer briefly.",
            MaxOutputTokens = 25000,
            AllowedTools = new List<string> { "wiki_search" },
            IsEnabled = true
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var customCatalog = new CustomSubAgentCatalogService(config, NullLogger<SubAgentCatalogService>.Instance, customAgent);

        var (tool, db, mockProvider, _) = CreateTestSetup(mockCatalog: customCatalog);
        var session = new ChatSession { Id = 30, AspNetUserId = "user30", Title = "Test", CreatedUtc = DateTime.UtcNow, LastMessageUtc = DateTime.UtcNow };
        db.ChatSession.Add(session);
        db.UserAiModels.Add(new UserAiModel
        {
            Id = 30,
            AspNetUserId = "user30",
            Provider = "OpenAI",
            ModelId = "gpt-5.6-luna",
            MaxOutputTokens = 50000,
            OrderIndex = 0
        });
        await db.SaveChangesAsync();

        var context = new ToolExecutionContext { SessionId = 30, AgentDepth = 0, MaxAgentDepth = 1 };
        var validParams = JsonDocument.Parse("{\"agent_name\":\"custom_capped\",\"task\":\"do something\"}").RootElement;

        var result = await tool.ExecuteAsync(validParams, context, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(25000, mockProvider.LastMaxOutputTokens);
    }

    [Fact]
    public async Task ExecuteAsync_InvalidThinkingLevel_DroppedToNull()
    {
        var (tool, db, mockProvider, _) = CreateTestSetup();
        var session = new ChatSession { Id = 40, AspNetUserId = "user40", Title = "Test", CreatedUtc = DateTime.UtcNow, LastMessageUtc = DateTime.UtcNow };
        db.ChatSession.Add(session);
        db.UserAiModels.Add(new UserAiModel
        {
            Id = 40,
            AspNetUserId = "user40",
            Provider = "OpenAI",
            ModelId = "gpt-5.6-luna",
            ThinkingLevel = "invalid_thinking_level_xyz",
            OrderIndex = 0
        });
        await db.SaveChangesAsync();

        var context = new ToolExecutionContext { SessionId = 40, AgentDepth = 0, MaxAgentDepth = 1 };
        var validParams = JsonDocument.Parse("{\"agent_name\":\"wiki_researcher\",\"task\":\"compare prayers\"}").RootElement;

        var result = await tool.ExecuteAsync(validParams, context, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Null(mockProvider.LastThinkingLevel);
    }

    [Fact]
    public async Task ExecuteAsync_ToolExecutionContext_CarriesParentSettings()
    {
        var (tool, db, _, _) = CreateTestSetup();
        var session = new ChatSession { Id = 50, AspNetUserId = "user50", Title = "Test", CreatedUtc = DateTime.UtcNow, LastMessageUtc = DateTime.UtcNow };
        db.ChatSession.Add(session);
        await db.SaveChangesAsync();

        var context = new ToolExecutionContext
        {
            SessionId = 50,
            UserId = "user50",
            SpoilerFreeMode = true,
            AgentDepth = 0,
            MaxAgentDepth = 1,
            ShowDebugLog = true
        };
        var validParams = JsonDocument.Parse("{\"agent_name\":\"wiki_researcher\",\"task\":\"compare prayers\"}").RootElement;

        var result = await tool.ExecuteAsync(validParams, context, CancellationToken.None);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task ExecuteAsync_EventForwarding_FiltersNonDebugEvents()
    {
        var (tool, db, _, _) = CreateTestSetup();
        var session = new ChatSession { Id = 60, AspNetUserId = "user60", Title = "Test", CreatedUtc = DateTime.UtcNow, LastMessageUtc = DateTime.UtcNow };
        db.ChatSession.Add(session);
        await db.SaveChangesAsync();

        var forwardedEvents = new List<ChatEvent>();
        var context = new ToolExecutionContext
        {
            SessionId = 60,
            AgentDepth = 0,
            MaxAgentDepth = 1,
            ShowDebugLog = true,
            EventSink = evt =>
            {
                forwardedEvents.Add(evt);
                return Task.CompletedTask;
            }
        };
        var validParams = JsonDocument.Parse("{\"agent_name\":\"wiki_researcher\",\"task\":\"compare prayers\"}").RootElement;

        var result = await tool.ExecuteAsync(validParams, context, CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotEmpty(forwardedEvents);
        Assert.All(forwardedEvents, evt => Assert.Equal("debug", evt.Type));
        Assert.DoesNotContain(forwardedEvents, evt => evt.Type == "chunk");
    }

    [Fact]
    public async Task ExecuteAsync_SupportsSubAgentExecution_False_ReturnsError()
    {
        var (tool, db, _, _) = CreateTestSetup(mockMetadata: new DisallowedExecutionMetadataService());
        var session = new ChatSession { Id = 70, AspNetUserId = "user70", Title = "Test", CreatedUtc = DateTime.UtcNow, LastMessageUtc = DateTime.UtcNow };
        db.ChatSession.Add(session);
        await db.SaveChangesAsync();

        var context = new ToolExecutionContext { SessionId = 70, AgentDepth = 0, MaxAgentDepth = 1 };
        var validParams = JsonDocument.Parse("{\"agent_name\":\"wiki_researcher\",\"task\":\"compare prayers\"}").RootElement;

        var result = await tool.ExecuteAsync(validParams, context, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("not permitted to execute subagents", result.ErrorMessage);
    }

    private class DisallowedExecutionMetadataService : ModelMetadataService
    {
        public override ModelMetadata GetMetadata(string provider, string modelId)
        {
            var meta = base.GetMetadata(provider, modelId);
            meta.SupportsSubAgentExecution = false;
            return meta;
        }
    }

    private class CustomSubAgentCatalogService : SubAgentCatalogService
    {
        private readonly SubAgentDefinition _customAgent;

        public CustomSubAgentCatalogService(IConfiguration config, ILogger<SubAgentCatalogService> logger, SubAgentDefinition customAgent)
            : base(config, logger)
        {
            _customAgent = customAgent;
        }

        public override SubAgentDefinition? GetSubAgent(string name)
        {
            if (string.Equals(name, _customAgent.Name, StringComparison.OrdinalIgnoreCase))
                return _customAgent;
            return base.GetSubAgent(name);
        }
    }

    private static (DelegateToSubAgentTool tool, ApplicationDbContext db, CapturingMockProvider provider, OngoingChatManager manager) CreateTestSetup(
        string providerName = "OpenAI",
        Dictionary<string, string?>? customConfig = null,
        ModelMetadataService? mockMetadata = null,
        SubAgentCatalogService? mockCatalog = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var dbOptions = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new ApplicationDbContext(dbOptions);

        var keyBytes = new byte[32];
        keyBytes[0] = 42;
        var keyBase64 = Convert.ToBase64String(keyBytes);

        var inMemory = new Dictionary<string, string?>
        {
            { "AesEncryptionKey", keyBase64 },
            { "AI:Provider", providerName },
            { "AI:Model", "gpt-5.6-luna" },
            { "AI:APIKey", "test-key" },
            { "DefaultMaxOutputTokens:OpenAI", "128000" },
            { "DefaultMaxOutputTokens:Google", "65536" },
            { "DefaultMaxOutputTokens:Anthropic", "8192" }
        };
        if (customConfig != null)
        {
            foreach (var kvp in customConfig)
                inMemory[kvp.Key] = kvp.Value;
        }

        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemory).Build();
        var crypto = new CryptoService(config);
        var mockProvider = new CapturingMockProvider { ProviderName = providerName };

        var catalogService = mockCatalog ?? new SubAgentCatalogService(config, NullLogger<SubAgentCatalogService>.Instance);
        var manager = new OngoingChatManager(config);
        var metadata = mockMetadata ?? new ModelMetadataService();

        var clientBridge = new NullClientBridge();
        var handlers = new List<IToolHandler>();
        var toolRegistry = new ToolRegistry(handlers, clientBridge, NullLogger<ToolRegistry>.Instance);
        using var cache = new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());
        var toolExecutor = new ToolExecutor(handlers, clientBridge, NullLogger<ToolExecutor>.Instance, cache, config);

        services.AddSingleton(db);
        services.AddSingleton(crypto);
        services.AddSingleton<IAiProvider>(mockProvider);
        services.AddSingleton(metadata);
        services.AddSingleton(toolRegistry);
        services.AddSingleton(toolExecutor);
        services.AddSingleton(new KnowledgeBaseService(NullLogger<KnowledgeBaseService>.Instance, config));
        services.AddSingleton<IHttpClientFactory>(new MockHttpClientFactory());
        services.AddSingleton<IConfiguration>(config);
        services.AddSingleton<AgentLoopRunner>();

        var sp = services.BuildServiceProvider();

        var tool = new DelegateToSubAgentTool(
            catalogService,
            manager,
            sp.GetRequiredService<IServiceScopeFactory>(),
            config,
            metadata,
            NullLogger<DelegateToSubAgentTool>.Instance);

        return (tool, db, mockProvider, manager);
    }

    private class CapturingMockProvider : IAiProvider
    {
        public string ProviderName { get; set; } = "OpenAI";
        public IReadOnlyList<string> SupportedServiceTiers => new[] { "default", "priority" };
        public int? LastMaxOutputTokens { get; private set; }
        public string? LastThinkingLevel { get; private set; }
        public string? LastReasoningMode { get; private set; }
        public string? LastServiceTier { get; private set; }
        public string? LastModelId { get; private set; }

        public void AppendAssistantToolCallsToHistory(List<object> messageHistory, string iterationText, List<JsonElement> toolCalls, List<JsonElement>? providerHistoryItems = null)
        {
            messageHistory.Add(new { role = "assistant", content = iterationText });
        }

        public void AppendToolResultsToHistory(List<object> messageHistory, List<ProviderToolResult> results)
        {
            foreach (var r in results)
                messageHistory.Add(new { role = "tool", content = r.Content });
        }

        public Dictionary<string, object> BuildChatRequestBody(string modelId, List<object> messageHistory, int? maxOutputTokens, string? thinkingLevel, ToolsForRequest requestTools, string? reasoningMode = null, string? reasoningSummary = null, string? serviceTier = null)
        {
            LastModelId = modelId;
            LastMaxOutputTokens = maxOutputTokens;
            LastThinkingLevel = thinkingLevel;
            LastReasoningMode = reasoningMode;
            LastServiceTier = serviceTier;
            return new Dictionary<string, object>();
        }

        public object BuildFunctionDeclaration(string name, string description, object parameterSchema) => new { name, description, parameterSchema };
        public object? BuildToolsPayload(List<object> providerTools, List<object> functionDeclarations) => null;
        public object? BuildWebSearchTool() => null;
        public void ConfigureRequest(HttpRequestMessage request, string apiKey) { }
        public object FormatMessage(string role, string text, List<SendMessageAttachment>? imageAttachments) => new { role, content = text };
        public string GetChatStreamUrl(string modelId, string apiKey) => "https://mock.stream.test";
        public async IAsyncEnumerable<ChatEvent> ParseStreamAsync(HttpResponseMessage response, bool showDebugLog, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            yield return new ChatEvent { Type = "debug", Data = "subagent-debug-line" };
            yield return new ChatEvent { Type = "chunk", Data = "subagent-chunk-prose" };
            await Task.CompletedTask;
        }
        public List<object> PrepareMessageHistory(List<object> messages) => messages;
        public Dictionary<string, object> BuildTitleRequestBody(string modelId, string systemPrompt, string userMessage, int maxTokens, string? serviceTier = null) => new();
        public string GetTitleUrl(string modelId, string apiKey) => "https://mock.stream.test/title";
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

    private class MockHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new HttpClient(new MockHttpMessageHandler());
    }

    private class MockHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("data: {}\n\n")
            });
        }
    }
}
