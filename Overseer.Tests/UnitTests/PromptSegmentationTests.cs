using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MobileGnollHackLogger.Data;
using Overseer.Hubs;
using Overseer.Services;
using Overseer.Services.Providers;
using Overseer.Services.Tools;
using Xunit;

namespace Overseer.Tests.UnitTests;

public class PromptSegmentationTests
{
    private static ChatService CreateChatService(Dictionary<string, string?>? configOverrides = null)
    {
        var services = new ServiceCollection();
        var dummyKey = Convert.ToBase64String(new byte[32]);
        var inMemorySettings = new Dictionary<string, string?>
        {
            { "AesEncryptionKey", dummyKey },
            { "PromptCacheSettings:EnableSegmentedPrompt", "true" },
            { "PromptCacheSettings:EnableAnthropicCacheControl", "true" },
            { "PromptCacheSettings:EnableOpenAiPromptCacheKey", "true" },
            { "PromptCacheSettings:TruncationBlockSize", "8" }
        };

        if (configOverrides != null)
        {
            foreach (var kvp in configOverrides)
            {
                inMemorySettings[kvp.Key] = kvp.Value;
            }
        }

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString()));

        services.AddSingleton<IConfiguration>(config);
        services.AddHttpClient();
        services.AddSignalR();
        services.AddMemoryCache();
        services.AddSingleton<IClientToolBridge, DummyClientToolBridge>();
        services.AddScoped<ToolRegistry>();
        services.AddScoped<ToolExecutor>();
        services.AddScoped<CryptoService>();
        services.AddScoped<WikiService>();
        services.AddScoped<ModelMetadataService>();
        services.AddScoped<KnowledgeBaseService>();
        services.AddScoped<OngoingChatManager>();
        services.AddScoped<IAiProvider, OpenAiResponsesProvider>();
        services.AddScoped<IAiProvider, AnthropicProvider>();
        services.AddScoped<IAiProvider, GoogleProvider>();
        services.AddScoped<Overseer.Services.Agents.AgentLoopRunner>();
        services.AddSingleton<Overseer.Services.ParallelExecutionResolver>();
        services.AddScoped<ChatService>();

        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<ChatService>();
    }

    [Fact]
    public void BuildSegmentedSystemPrompt_DividesSectionsCorrectly()
    {
        var chatService = CreateChatService();
        var wikiArticles = new List<string> { "# Vorpal Blade\nA powerful artifact." };

        var (frozen, session, volatileSuffix) = chatService.BuildSegmentedSystemPrompt(
            wikiArticles,
            spoilerFreeMode: false,
            verboseMode: true,
            isGameOn: true,
            developerMode: false,
            overseerMode: 0,
            hasGameSnapshot: true,
            hasMessageHistory: true,
            clientSettings: "{\"BoolData\":{\"someFlag\":true}}",
            enableToolUse: true,
            enableWebSearch: true,
            allowSourceCodeReferences: false,
            enableSubAgents: true,
            parallelMode: MobileGnollHackLogger.Data.ParallelExecutionMode.Enabled);

        // Segment A (Frozen prefix) contains Identity, Decision Priorities, and Section 15 (Tool Usage Policy + Subagents)
        Assert.Contains("Gnoll Overseer", frozen);
        Assert.Contains("Decision Priorities", frozen);
        Assert.Contains("Tool Usage Policy", frozen);
        Assert.Contains("Subagent Delegation", frozen);
        Assert.DoesNotContain("Wiki Knowledge Base", frozen);
        Assert.DoesNotContain("## Response Style", frozen);

        // Segment B (Session prefix) contains Response Style and Client Environment
        Assert.Contains("## Response Style — Verbose", session);
        Assert.Contains("Client Environment", session);
        Assert.DoesNotContain("Tool Usage Policy", session);
        Assert.DoesNotContain("Wiki Knowledge Base", session);

        // Segment C (Volatile suffix) contains Wiki Knowledge Base
        Assert.Contains("Wiki Knowledge Base", volatileSuffix);
        Assert.Contains("Vorpal Blade", volatileSuffix);
        Assert.DoesNotContain("Tool Usage Policy", volatileSuffix);
        Assert.DoesNotContain("## Response Style", volatileSuffix);
    }

    [Fact]
    public void ToolRegistry_SortsHandlersDeterministicallyByToolName()
    {
        var dummyHandlerZ = new DummyToolHandler("z_tool");
        var dummyHandlerA = new DummyToolHandler("a_tool");
        var dummyHandlerM = new DummyToolHandler("m_tool");

        var handlers = new List<IToolHandler> { dummyHandlerZ, dummyHandlerA, dummyHandlerM };
        var registry = new ToolRegistry(
            handlers,
            new DummyClientToolBridge(),
            NullLogger<ToolRegistry>.Instance);

        var provider = new OpenAiResponsesProvider(new ConfigurationBuilder().Build());
        var tools = registry.BuildToolsForRequest(provider, new ToolExecutionContext(), false, true, false, false);
        var declarations = tools.FunctionDeclarations;
        Assert.Equal(3, declarations.Count);

        // Verify sorted order: a_tool, m_tool, z_tool
        var name0 = ProviderHelper.GetProperty(declarations[0], "name")?.ToString();
        var name1 = ProviderHelper.GetProperty(declarations[1], "name")?.ToString();
        var name2 = ProviderHelper.GetProperty(declarations[2], "name")?.ToString();

        Assert.Equal("a_tool", name0);
        Assert.Equal("m_tool", name1);
        Assert.Equal("z_tool", name2);
    }

    [Fact]
    public void OpenAiProvider_BuildChatRequestBody_IncludesPromptCacheKey()
    {
        var inMemorySettings = new Dictionary<string, string?>
        {
            { "PromptCacheSettings:EnableOpenAiPromptCacheKey", "true" }
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemorySettings).Build();
        var provider = new OpenAiResponsesProvider(config);

        var history = new List<object>
        {
            provider.FormatMessage("user", "Hello", null)
        };

        var requestBody = provider.BuildChatRequestBody(
            "gpt-4o",
            history,
            1024,
            null,
            new ToolsForRequest(),
            promptCacheKey: "sample_cache_key_12345");

        Assert.True(requestBody.ContainsKey("prompt_cache_key"));
        Assert.Equal("sample_cache_key_12345", requestBody["prompt_cache_key"]);
    }

    [Fact]
    public void AnthropicProvider_BuildChatRequestBody_WithSegmentedPrompt_AddsCacheControlBreakpoints()
    {
        var inMemorySettings = new Dictionary<string, string?>
        {
            { "PromptCacheSettings:EnableAnthropicCacheControl", "true" }
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemorySettings).Build();
        var provider = new AnthropicProvider(config);

        var segmentedPrompt = new SegmentedPrompt(
            "Frozen Prefix Identity & Policy",
            "Session Prefix Style",
            "Volatile Wiki Context");

        var rawMessages = new List<object>
        {
            provider.FormatMessage("system", segmentedPrompt.FullPrompt, null),
            provider.FormatMessage("user", "What is my next tactical move?", null)
        };

        var prepared = provider.PrepareMessageHistory(rawMessages);

        var tools = new ToolsForRequest
        {
            FunctionDeclarations = new List<object>
            {
                provider.BuildFunctionDeclaration("tool_a", "First tool", new { }),
                provider.BuildFunctionDeclaration("tool_b", "Second tool", new { })
            }
        };

        var requestBody = provider.BuildChatRequestBody(
            "claude-3-7-sonnet-20250219",
            prepared,
            1024,
            null,
            tools,
            segmentedPrompt: segmentedPrompt);

        // Verify System blocks array
        var systemBlocks = requestBody["system"] as List<object>;
        Assert.NotNull(systemBlocks);
        Assert.Equal(3, systemBlocks.Count);

        // Block 1 (Frozen): has cache_control ephemeral
        Assert.Equal("Frozen Prefix Identity & Policy", ProviderHelper.GetProperty(systemBlocks[0], "text")?.ToString());
        var cc1 = ProviderHelper.GetProperty(systemBlocks[0], "cache_control");
        Assert.NotNull(cc1);
        Assert.Equal("ephemeral", ProviderHelper.GetProperty(cc1, "type")?.ToString());

        // Block 2 (Session): has cache_control ephemeral
        Assert.Equal("Session Prefix Style", ProviderHelper.GetProperty(systemBlocks[1], "text")?.ToString());
        var cc2 = ProviderHelper.GetProperty(systemBlocks[1], "cache_control");
        Assert.NotNull(cc2);
        Assert.Equal("ephemeral", ProviderHelper.GetProperty(cc2, "type")?.ToString());

        // Block 3 (Volatile): NO cache_control
        Assert.Equal("Volatile Wiki Context", ProviderHelper.GetProperty(systemBlocks[2], "text")?.ToString());
        var cc3 = ProviderHelper.GetProperty(systemBlocks[2], "cache_control");
        Assert.Null(cc3);

        // Tools Breakpoint: Last tool (tool_b) has cache_control
        var reqTools = requestBody["tools"] as List<object>;
        Assert.NotNull(reqTools);
        Assert.Equal(2, reqTools.Count);
        var lastToolCc = ProviderHelper.GetProperty(reqTools[1], "cache_control");
        Assert.NotNull(lastToolCc);
        Assert.Equal("ephemeral", ProviderHelper.GetProperty(lastToolCc, "type")?.ToString());

        // Message Tail Breakpoint: Last message has cache_control on its block
        var reqMessages = requestBody["messages"] as List<object>;
        Assert.NotNull(reqMessages);
        var lastMsgContent = ProviderHelper.GetProperty(reqMessages[^1], "content") as List<object>;
        Assert.NotNull(lastMsgContent);
        var lastBlockCc = ProviderHelper.GetProperty(lastMsgContent[^1], "cache_control");
        Assert.NotNull(lastBlockCc);
        Assert.Equal("ephemeral", ProviderHelper.GetProperty(lastBlockCc, "type")?.ToString());
    }

    private class DummyToolHandler : IToolHandler
    {
        public string ToolName { get; }
        public string Description { get; set; }
        public ToolExecutionLocation ExecutionLocation => ToolExecutionLocation.Server;
        public ToolCategory Category => ToolCategory.InformationRetrieval;
        public JsonElement ParameterSchema => JsonDocument.Parse("{\"type\":\"object\"}").RootElement;

        public DummyToolHandler(string toolName)
        {
            ToolName = toolName;
            Description = $"Description for {toolName}";
        }

        public Task<ToolResult> ExecuteAsync(JsonElement parameters, ToolExecutionContext context, System.Threading.CancellationToken cancellationToken)
        {
            return Task.FromResult(new ToolResult { Success = true, Content = "ok" });
        }
    }
}
