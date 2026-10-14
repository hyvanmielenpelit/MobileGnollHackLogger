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

        var context = new ToolExecutionContext { SessionId = Overseer.Services.Privacy.SessionRef.Persistent(1), EnableSubAgents = true };
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

        var context = new ToolExecutionContext { SessionId = Overseer.Services.Privacy.SessionRef.Persistent(1), AgentDepth = 1, MaxAgentDepth = 1, EnableSubAgents = true };
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

        var context = new ToolExecutionContext { SessionId = Overseer.Services.Privacy.SessionRef.Persistent(1), AgentDepth = 0, MaxAgentDepth = 1, EnableSubAgents = true };
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
            SessionId = Overseer.Services.Privacy.SessionRef.Persistent(1),
            AgentDepth = 0,
            MaxAgentDepth = 1,
            EnableSubAgents = true,
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
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var context = new ToolExecutionContext { SessionId = Overseer.Services.Privacy.SessionRef.Persistent(10), AgentDepth = 0, MaxAgentDepth = 1, EnableSubAgents = true };
        var validParams = JsonDocument.Parse("{\"agent_name\":\"wiki_researcher\",\"task\":\"compare prayers\"}").RootElement;

        var result = await tool.ExecuteAsync(validParams, context, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(50000, mockProvider.LastMaxOutputTokens);
        Assert.Equal("high", mockProvider.LastThinkingLevel);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExecuteAsync_SubAgentRequest_InheritsDisableProviderPromptCache(bool disabled)
    {
        var (tool, db, mockProvider, _) = CreateTestSetup();
        var session = new ChatSession { Id = 15, AspNetUserId = "user15", Title = "Test", CreatedUtc = DateTime.UtcNow, LastMessageUtc = DateTime.UtcNow };
        db.ChatSession.Add(session);
        db.UserAiModels.Add(new UserAiModel
        {
            Id = 15,
            AspNetUserId = "user15",
            Provider = "OpenAI",
            ModelId = "gpt-5.6-luna",
            OrderIndex = 0
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var context = new ToolExecutionContext
        {
            SessionId = Overseer.Services.Privacy.SessionRef.Persistent(15),
            AgentDepth = 0,
            MaxAgentDepth = 1,
            EnableSubAgents = true,
            DisableProviderPromptCache = disabled
        };
        var validParams = JsonDocument.Parse("{\"agent_name\":\"wiki_researcher\",\"task\":\"compare prayers\"}").RootElement;

        var result = await tool.ExecuteAsync(validParams, context, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(disabled, mockProvider.LastDisablePromptCache);
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
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var context = new ToolExecutionContext { SessionId = Overseer.Services.Privacy.SessionRef.Persistent(20), AgentDepth = 0, MaxAgentDepth = 1, EnableSubAgents = true };
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
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var context = new ToolExecutionContext { SessionId = Overseer.Services.Privacy.SessionRef.Persistent(30), AgentDepth = 0, MaxAgentDepth = 1, EnableSubAgents = true };
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
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var context = new ToolExecutionContext { SessionId = Overseer.Services.Privacy.SessionRef.Persistent(40), AgentDepth = 0, MaxAgentDepth = 1, EnableSubAgents = true };
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
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var context = new ToolExecutionContext
        {
            SessionId = Overseer.Services.Privacy.SessionRef.Persistent(50),
            UserId = "user50",
            SpoilerFreeMode = true,
            AgentDepth = 0,
            MaxAgentDepth = 1,
            EnableSubAgents = true,
            ShowDebugLog = true
        };
        var validParams = JsonDocument.Parse("{\"agent_name\":\"wiki_researcher\",\"task\":\"compare prayers\"}").RootElement;

        var result = await tool.ExecuteAsync(validParams, context, CancellationToken.None);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task ExecuteAsync_EventForwarding_ForwardsSubAgentToolEvents()
    {
        var (tool, db, provider, _) = CreateTestSetup();
        provider.EmitToolCallOnFirstIteration = true;
        var session = new ChatSession { Id = 60, AspNetUserId = "user60", Title = "Test", CreatedUtc = DateTime.UtcNow, LastMessageUtc = DateTime.UtcNow };
        db.ChatSession.Add(session);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var forwardedEvents = new List<ChatEvent>();
        var context = new ToolExecutionContext
        {
            SessionId = Overseer.Services.Privacy.SessionRef.Persistent(60),
            AgentDepth = 0,
            MaxAgentDepth = 1,
            EnableSubAgents = true,
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

        // Subagent debug and tool lifecycle events MUST be forwarded
        Assert.Contains(forwardedEvents, evt => evt.Type == "debug");
        Assert.Contains(forwardedEvents, evt => evt.Type == "tool_start");
        Assert.Contains(forwardedEvents, evt => evt.Type == "tool_error");

        // Subagent prose, reasoning, status, timing, and errors MUST stay isolated
        var deniedTypes = new[] { "chunk", "thinking_chunk", "status", "ttft", "duration", "error" };
        Assert.DoesNotContain(forwardedEvents, evt => deniedTypes.Contains(evt.Type));
    }

    [Fact]
    public async Task ExecuteAsync_SubAgentToolCalls_EnrichesToolStartWithHierarchy()
    {
        var (tool, db, provider, _) = CreateTestSetup();
        provider.EmitToolCallOnFirstIteration = true;
        var session = new ChatSession { Id = 61, AspNetUserId = "user61", Title = "Test", CreatedUtc = DateTime.UtcNow, LastMessageUtc = DateTime.UtcNow };
        db.ChatSession.Add(session);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var forwardedEvents = new List<ChatEvent>();
        string expectedParentToolCallId = "parent-delegate-call-123";
        var context = new ToolExecutionContext
        {
            SessionId = Overseer.Services.Privacy.SessionRef.Persistent(61),
            AgentDepth = 0,
            MaxAgentDepth = 1,
            EnableSubAgents = true,
            ToolCallId = expectedParentToolCallId,
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
        var toolStartEvent = Assert.Single(forwardedEvents, evt => evt.Type == "tool_start");
        Assert.NotNull(toolStartEvent.Data);

        using var doc = JsonDocument.Parse(toolStartEvent.Data);
        var root = doc.RootElement;

        Assert.Equal("wiki_search", root.GetProperty("name").GetString());
        Assert.Equal("wiki_researcher", root.GetProperty("agent_name").GetString());
        Assert.Equal(expectedParentToolCallId, root.GetProperty("parent_tool_call_id").GetString());
        Assert.Equal(1, root.GetProperty("depth").GetInt32());
    }

    [Fact]
    public async Task ExecuteAsync_SupportsSubAgentExecution_False_ReturnsError()
    {
        var (tool, db, _, _) = CreateTestSetup(mockMetadata: new DisallowedExecutionMetadataService());
        var session = new ChatSession { Id = 70, AspNetUserId = "user70", Title = "Test", CreatedUtc = DateTime.UtcNow, LastMessageUtc = DateTime.UtcNow };
        db.ChatSession.Add(session);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var context = new ToolExecutionContext { SessionId = Overseer.Services.Privacy.SessionRef.Persistent(70), AgentDepth = 0, MaxAgentDepth = 1, EnableSubAgents = true };
        var validParams = JsonDocument.Parse("{\"agent_name\":\"wiki_researcher\",\"task\":\"compare prayers\"}").RootElement;

        var result = await tool.ExecuteAsync(validParams, context, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("not permitted to execute subagents", result.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAsync_ActiveUserModelId_InheritsExactSelectedModel()
    {
        var (tool, db, provider, _) = CreateTestSetup();
        var keyBytes = new byte[32];
        keyBytes[0] = 42;
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { { "AesEncryptionKey", Convert.ToBase64String(keyBytes) } }).Build();
        var crypto = new CryptoService(config);

        var (encKey, nonce, tag) = crypto.Encrypt("custom-user-api-key", "user80");

        db.UserAiModels.Add(new UserAiModel { Id = 101, AspNetUserId = "user80", Provider = "OpenAI", ModelId = "gpt-5.6-first", OrderIndex = 0 });
        db.UserAiModels.Add(new UserAiModel { Id = 102, AspNetUserId = "user80", Provider = "OpenAI", ModelId = "gpt-5.6-second", OrderIndex = 1 });
        db.UserAiApiKeys.Add(new UserAiApiKey { AspNetUserId = "user80", Provider = "OpenAI", EncryptedApiKey = encKey, ApiKeyNonce = nonce, ApiKeyTag = tag });

        var session = new ChatSession { Id = 80, AspNetUserId = "user80", Title = "Test", CreatedUtc = DateTime.UtcNow, LastMessageUtc = DateTime.UtcNow };
        db.ChatSession.Add(session);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var context = new ToolExecutionContext { SessionId = Overseer.Services.Privacy.SessionRef.Persistent(80), ActiveUserModelId = 102, AgentDepth = 0, MaxAgentDepth = 1, EnableSubAgents = true };
        var validParams = JsonDocument.Parse("{\"agent_name\":\"wiki_researcher\",\"task\":\"compare prayers\"}").RootElement;

        var result = await tool.ExecuteAsync(validParams, context, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("gpt-5.6-second", provider.LastModelId);
        Assert.Equal("custom-user-api-key", provider.LastApiKey);
    }

    [Fact]
    public async Task ExecuteAsync_ActiveSystemModelId_InheritsExactSelectedSystemModel()
    {
        var (tool, db, provider, _) = CreateTestSetup();
        var keyBytes = new byte[32];
        keyBytes[0] = 42;
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { { "AesEncryptionKey", Convert.ToBase64String(keyBytes) } }).Build();
        var crypto = new CryptoService(config);

        var (sysKey, sysNonce, sysTag) = crypto.Encrypt("system-secret-key", "SYSTEM_API_KEY");
        var (userKey, userNonce, userTag) = crypto.Encrypt("user-key-81", "user81");

        db.SystemAiApiConfigurations.Add(new SystemAiApiConfiguration
        {
            Id = 201,
            DisplayName = "System Model",
            Provider = "OpenAI",
            ModelId = "gpt-5.6-system-custom",
            IsEnabled = true,
            IsSystemWide = true,
            ModelRole = 3,
            EncryptedApiKey = sysKey,
            ApiKeyNonce = sysNonce,
            ApiKeyTag = sysTag
        });

        db.UserAiModels.Add(new UserAiModel { Id = 103, AspNetUserId = "user81", Provider = "OpenAI", ModelId = "gpt-5.6-user-model", OrderIndex = 0 });
        db.UserAiApiKeys.Add(new UserAiApiKey { AspNetUserId = "user81", Provider = "OpenAI", EncryptedApiKey = userKey, ApiKeyNonce = userNonce, ApiKeyTag = userTag });

        var session = new ChatSession { Id = 81, AspNetUserId = "user81", Title = "Test", CreatedUtc = DateTime.UtcNow, LastMessageUtc = DateTime.UtcNow };
        db.ChatSession.Add(session);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var context = new ToolExecutionContext { SessionId = Overseer.Services.Privacy.SessionRef.Persistent(81), ActiveSystemModelId = 201, AgentDepth = 0, MaxAgentDepth = 1, EnableSubAgents = true };
        var validParams = JsonDocument.Parse("{\"agent_name\":\"wiki_researcher\",\"task\":\"compare prayers\"}").RootElement;

        var result = await tool.ExecuteAsync(validParams, context, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("gpt-5.6-system-custom", provider.LastModelId);
        Assert.Equal("system-secret-key", provider.LastApiKey);
    }

    [Fact]
    public async Task ExecuteAsync_SubAgentRequest_CarriesSubAgentApiKeyAlertContext()
    {
        var alertService = new Overseer.Services.ApiKeyAlerts.ApiKeyAlertService(
            new Overseer.Services.ApiKeyAlerts.ApiKeyAlertOptions { Enabled = true, RecipientEmail = "alerts@example.test" },
            NullLogger<Overseer.Services.ApiKeyAlerts.ApiKeyAlertService>.Instance);
        var (tool, db, provider, _) = CreateTestSetup(alertService: alertService);
        // A rejected-key stream error is the observable point where the sub-agent request's context is read.
        provider.StreamErrorBeforeChunks = "[invalid_api_key] Incorrect API key provided.";

        var keyBytes = new byte[32];
        keyBytes[0] = 42;
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { { "AesEncryptionKey", Convert.ToBase64String(keyBytes) } }).Build();
        var crypto = new CryptoService(config);
        var (sysKey, sysNonce, sysTag) = crypto.Encrypt("test-key-" + "0123456789abcdefWXYZ", "SYSTEM_API_KEY");

        db.SystemAiApiConfigurations.Add(new SystemAiApiConfiguration
        {
            Id = 211,
            DisplayName = "System Model",
            Provider = "OpenAI",
            ModelId = "gpt-5.6-system-custom",
            IsEnabled = true,
            IsSystemWide = true,
            ModelRole = 3,
            EncryptedApiKey = sysKey,
            ApiKeyNonce = sysNonce,
            ApiKeyTag = sysTag
        });
        db.ChatSession.Add(new ChatSession { Id = 84, AspNetUserId = "user84", Title = "Test", CreatedUtc = DateTime.UtcNow, LastMessageUtc = DateTime.UtcNow });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var session = Overseer.Services.Privacy.SessionRef.Persistent(84);
        var context = new ToolExecutionContext
        {
            SessionId = session,
            ActiveSystemModelId = 211,
            AgentDepth = 0,
            MaxAgentDepth = 1,
            EnableSubAgents = true,
            ApiKeyAlert = new Overseer.Services.ApiKeyAlerts.ApiKeyAlertContext(
                Overseer.Services.ApiKeyAlerts.ApiKeyUsage.Chat, session, "user84", "User84")
        };
        var validParams = JsonDocument.Parse("{\"agent_name\":\"wiki_researcher\",\"task\":\"compare prayers\"}").RootElement;

        await tool.ExecuteAsync(validParams, context, CancellationToken.None);

        Assert.True(alertService.Reader.TryRead(out var report));
        Assert.Equal(Overseer.Services.ApiKeyAlerts.ApiKeyUsage.SubAgent, report!.Context.Usage);
        Assert.Equal("wiki_researcher", report.Context.AgentName);
        Assert.Equal("user84", report.Context.UserId);
        Assert.Equal(211, report.SystemAiApiConfigurationId);
        Assert.Equal(Overseer.Services.ApiKeyAlerts.ApiKeyFailureKind.KeyRejected, report.Kind);
        Assert.Equal(Overseer.Services.ApiKeyAlerts.ApiKeyUsage.Chat, context.ApiKeyAlert.Usage);
    }

    [Fact]
    public async Task ExecuteAsync_ExcludesTitleOnlySystemModels()
    {
        var (tool, db, provider, _) = CreateTestSetup();
        var keyBytes = new byte[32];
        keyBytes[0] = 42;
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { { "AesEncryptionKey", Convert.ToBase64String(keyBytes) } }).Build();
        var crypto = new CryptoService(config);

        var (titleKey, titleNonce, titleTag) = crypto.Encrypt("title-only-key", "SYSTEM_API_KEY");

        db.SystemAiApiConfigurations.Add(new SystemAiApiConfiguration
        {
            Id = 202,
            DisplayName = "Title Model",
            Provider = "OpenAI",
            ModelId = "gpt-5.6-title-only",
            IsEnabled = true,
            IsSystemWide = true,
            ModelRole = 2, // Title Generation only
            EncryptedApiKey = titleKey,
            ApiKeyNonce = titleNonce,
            ApiKeyTag = titleTag
        });

        var session = new ChatSession { Id = 82, AspNetUserId = "user82", Title = "Test", CreatedUtc = DateTime.UtcNow, LastMessageUtc = DateTime.UtcNow };
        db.ChatSession.Add(session);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var context = new ToolExecutionContext { SessionId = Overseer.Services.Privacy.SessionRef.Persistent(82), AgentDepth = 0, MaxAgentDepth = 1, EnableSubAgents = true };
        var validParams = JsonDocument.Parse("{\"agent_name\":\"wiki_researcher\",\"task\":\"compare prayers\"}").RootElement;

        var result = await tool.ExecuteAsync(validParams, context, CancellationToken.None);

        Assert.True(result.Success);
        // Excludes title-only system model and falls through to AppSettings
        Assert.Equal("gpt-5.6-luna", provider.LastModelId);
        Assert.Equal("test-key", provider.LastApiKey);
    }

    [Fact]
    public async Task ExecuteAsync_ChecksUserAndGroupAssignments()
    {
        var (tool, db, provider, _) = CreateTestSetup();
        var keyBytes = new byte[32];
        keyBytes[0] = 42;
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { { "AesEncryptionKey", Convert.ToBase64String(keyBytes) } }).Build();
        var crypto = new CryptoService(config);

        var (privKey, privNonce, privTag) = crypto.Encrypt("private-key", "SYSTEM_API_KEY");

        db.SystemAiApiConfigurations.Add(new SystemAiApiConfiguration
        {
            Id = 203,
            DisplayName = "Private Model",
            Provider = "OpenAI",
            ModelId = "gpt-5.6-private-system",
            IsEnabled = true,
            IsSystemWide = false, // Not system-wide and not assigned to user83
            ModelRole = 3,
            EncryptedApiKey = privKey,
            ApiKeyNonce = privNonce,
            ApiKeyTag = privTag
        });

        var session = new ChatSession { Id = 83, AspNetUserId = "user83", Title = "Test", CreatedUtc = DateTime.UtcNow, LastMessageUtc = DateTime.UtcNow };
        db.ChatSession.Add(session);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var context = new ToolExecutionContext { SessionId = Overseer.Services.Privacy.SessionRef.Persistent(83), AgentDepth = 0, MaxAgentDepth = 1, EnableSubAgents = true };
        var validParams = JsonDocument.Parse("{\"agent_name\":\"wiki_researcher\",\"task\":\"compare prayers\"}").RootElement;

        var result = await tool.ExecuteAsync(validParams, context, CancellationToken.None);

        Assert.True(result.Success);
        // Excludes unassigned private model and falls through to AppSettings
        Assert.Equal("gpt-5.6-luna", provider.LastModelId);
        Assert.Equal("test-key", provider.LastApiKey);
    }

    [Fact]
    public async Task ExecuteAsync_Fails_WhenSubAgentsDisabledInContext()
    {
        var (tool, db, _, _) = CreateTestSetup();
        var session = new ChatSession { Id = 90, AspNetUserId = "user90", Title = "Test", CreatedUtc = DateTime.UtcNow, LastMessageUtc = DateTime.UtcNow };
        db.ChatSession.Add(session);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var context = new ToolExecutionContext { SessionId = Overseer.Services.Privacy.SessionRef.Persistent(90), AgentDepth = 0, MaxAgentDepth = 1, EnableSubAgents = false };
        var validParams = JsonDocument.Parse("{\"agent_name\":\"wiki_researcher\",\"task\":\"compare prayers\"}").RootElement;

        var result = await tool.ExecuteAsync(validParams, context, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("Subagent execution is disabled", result.ErrorMessage);
    }

    [Fact]
    public void ParameterSchema_IncludesDynamicEnum_WhenEnabledAgentsExist()
    {
        var (tool, _, _, _) = CreateTestSetup();
        var schema = tool.ParameterSchema;
        var schemaJson = schema.GetRawText();

        Assert.Contains("\"enum\":", schemaJson);
        Assert.Contains("wiki_researcher", schemaJson);
    }

    [Fact]
    public void ParameterSchema_OmitsDynamicEnum_WhenNoEnabledAgentsExist()
    {
        var emptyCatalog = new EmptySubAgentCatalogService(
            new ConfigurationBuilder().Build(),
            NullLogger<SubAgentCatalogService>.Instance);
        var (tool, _, _, _) = CreateTestSetup(mockCatalog: emptyCatalog);

        var schema = tool.ParameterSchema;
        var schemaJson = schema.GetRawText();

        Assert.DoesNotContain("\"enum\":", schemaJson);
    }

    [Fact]
    public async Task ExecuteAsync_FiltersDebugEvents_WhenShowDebugLogFalse()
    {
        var (tool, db, _, _) = CreateTestSetup();
        var session = new ChatSession { Id = 100, AspNetUserId = "user100", Title = "Test", CreatedUtc = DateTime.UtcNow, LastMessageUtc = DateTime.UtcNow };
        db.ChatSession.Add(session);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var sinkEvents = new List<ChatEvent>();
        var context = new ToolExecutionContext
        {
            SessionId = Overseer.Services.Privacy.SessionRef.Persistent(100),
            AgentDepth = 0,
            MaxAgentDepth = 1,
            EnableSubAgents = true,
            ShowDebugLog = false,
            EventSink = evt => { sinkEvents.Add(evt); return Task.CompletedTask; }
        };

        var validParams = JsonDocument.Parse("{\"agent_name\":\"wiki_researcher\",\"task\":\"compare prayers\"}").RootElement;
        var result = await tool.ExecuteAsync(validParams, context, CancellationToken.None);

        Assert.True(result.Success);
        // Verify EventSink does not receive debug events when ShowDebugLog is false
        Assert.DoesNotContain(sinkEvents, e => e.Type == "debug");
    }

    private static List<TokenUsageReport> OneUsage() => new()
    {
        new TokenUsageReport { TotalPromptTokens = 12_000, UncachedInputTokens = 12_000, CacheReadTokens = 3_000, OutputTokens = 800 }
    };

    private static (string ciphertext, string nonce, string tag) EncryptUserKey(string plain, string userId)
    {
        var keyBytes = new byte[32];
        keyBytes[0] = 42;
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { { "AesEncryptionKey", Convert.ToBase64String(keyBytes) } }).Build();
        return new CryptoService(config).Encrypt(plain, userId);
    }

    [Fact]
    public async Task ExecuteAsync_AppSettingsFallback_PricesRunAsOperatorFundedCatalogCost()
    {
        var (tool, db, provider, _) = CreateTestSetup();
        var usages = OneUsage();
        provider.UsagePerCall = usages;
        db.ChatSession.Add(new ChatSession { Id = 300, AspNetUserId = "user300", Title = "Test", CreatedUtc = DateTime.UtcNow, LastMessageUtc = DateTime.UtcNow });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var context = new ToolExecutionContext { SessionId = Overseer.Services.Privacy.SessionRef.Persistent(300), ToolCallId = "call-300", AgentDepth = 0, MaxAgentDepth = 1, EnableSubAgents = true };
        var validParams = JsonDocument.Parse("{\"agent_name\":\"wiki_researcher\",\"task\":\"compare prayers\"}").RootElement;

        var result = await tool.ExecuteAsync(validParams, context, CancellationToken.None);

        var run = Assert.Single(result.DelegatedRunCosts!);
        Assert.True(run.IsOperatorFunded);
        Assert.Null(run.SystemModelId);
        Assert.Equal("wiki_researcher", run.AgentName);
        Assert.Equal("call-300", run.ToolCallId);
        Assert.Equal(1, run.ModelCallCount);

        var card = new ModelPricingService(new ModelMetadataService(), db).ResolveDefault("OpenAI", "gpt-5.6-luna");
        Assert.NotNull(card);
        Assert.Equal(ModelPricingService.ComputeCost(card, usages), run.EstimatedCost);
        Assert.Equal("catalog", run.PricingSource);
    }

    [Fact]
    public async Task ExecuteAsync_ActiveUserModelOnUserKey_PricesRunOnTheUsersCustomRates()
    {
        var (tool, db, provider, _) = CreateTestSetup();
        var usages = OneUsage();
        provider.UsagePerCall = usages;
        var (enc, nonce, tag) = EncryptUserKey("user-key-301", "user301");
        var userModel = new UserAiModel
        {
            Id = 301, AspNetUserId = "user301", Provider = "OpenAI", ModelId = "gpt-5.6-luna", OrderIndex = 0,
            PricingMode = "custom", InputPricePerMillion = 7m, OutputPricePerMillion = 21m
        };
        db.UserAiModels.Add(userModel);
        db.UserAiApiKeys.Add(new UserAiApiKey { AspNetUserId = "user301", Provider = "OpenAI", EncryptedApiKey = enc, ApiKeyNonce = nonce, ApiKeyTag = tag });
        db.ChatSession.Add(new ChatSession { Id = 301, AspNetUserId = "user301", Title = "Test", CreatedUtc = DateTime.UtcNow, LastMessageUtc = DateTime.UtcNow });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var context = new ToolExecutionContext { SessionId = Overseer.Services.Privacy.SessionRef.Persistent(301), ActiveUserModelId = 301, AgentDepth = 0, MaxAgentDepth = 1, EnableSubAgents = true };
        var validParams = JsonDocument.Parse("{\"agent_name\":\"wiki_researcher\",\"task\":\"compare prayers\"}").RootElement;

        var result = await tool.ExecuteAsync(validParams, context, CancellationToken.None);

        Assert.Equal("user-key-301", provider.LastApiKey);
        var run = Assert.Single(result.DelegatedRunCosts!);
        Assert.False(run.IsOperatorFunded);
        Assert.Equal("custom", run.PricingSource);
        var customCard = new ModelPricingService(new ModelMetadataService(), db).Resolve(userModel)!;
        Assert.Equal(ModelPricingSource.Custom, customCard.Source);
        Assert.Equal(ModelPricingService.ComputeCost(customCard, usages), run.EstimatedCost);
    }

    [Fact]
    public async Task ExecuteAsync_ActiveUserModelOnServerKey_IsOperatorFundedAtCatalogPrice()
    {
        var (tool, db, provider, _) = CreateTestSetup();
        var usages = OneUsage();
        provider.UsagePerCall = usages;
        var userModel = new UserAiModel
        {
            Id = 302, AspNetUserId = "user302", Provider = "OpenAI", ModelId = "gpt-5.6-luna", OrderIndex = 0,
            PricingMode = "custom", InputPricePerMillion = 7m, OutputPricePerMillion = 21m
        };
        db.UserAiModels.Add(userModel);
        db.ChatSession.Add(new ChatSession { Id = 302, AspNetUserId = "user302", Title = "Test", CreatedUtc = DateTime.UtcNow, LastMessageUtc = DateTime.UtcNow });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var context = new ToolExecutionContext { SessionId = Overseer.Services.Privacy.SessionRef.Persistent(302), ActiveUserModelId = 302, AgentDepth = 0, MaxAgentDepth = 1, EnableSubAgents = true };
        var validParams = JsonDocument.Parse("{\"agent_name\":\"wiki_researcher\",\"task\":\"compare prayers\"}").RootElement;

        var result = await tool.ExecuteAsync(validParams, context, CancellationToken.None);

        // No user key for the provider: the run fell back to the AI:APIKey server key.
        Assert.Equal("test-key", provider.LastApiKey);
        var run = Assert.Single(result.DelegatedRunCosts!);
        Assert.True(run.IsOperatorFunded);
        Assert.Equal("catalog", run.PricingSource);

        var pricingService = new ModelPricingService(new ModelMetadataService(), db);
        var catalogCard = pricingService.ResolveDefault("OpenAI", "gpt-5.6-luna");
        Assert.NotNull(catalogCard);
        Assert.Equal(ModelPricingService.ComputeCost(catalogCard, usages), run.EstimatedCost);
        Assert.NotEqual(ModelPricingService.ComputeCost(pricingService.Resolve(userModel)!, usages), run.EstimatedCost);
    }

    [Fact]
    public async Task ExecuteAsync_ActiveSystemModel_IsOperatorFundedWithSystemModelId()
    {
        var (tool, db, provider, _) = CreateTestSetup();
        var usages = OneUsage();
        provider.UsagePerCall = usages;
        var (sysKey, sysNonce, sysTag) = EncryptUserKey("system-secret-key", "SYSTEM_API_KEY");
        var sysConfig = new SystemAiApiConfiguration
        {
            Id = 303, DisplayName = "System Model", Provider = "OpenAI", ModelId = "gpt-5.6-luna",
            IsEnabled = true, IsSystemWide = true, ModelRole = 3,
            EncryptedApiKey = sysKey, ApiKeyNonce = sysNonce, ApiKeyTag = sysTag,
            PricingMode = "custom", InputPricePerMillion = 3m, OutputPricePerMillion = 9m
        };
        db.SystemAiApiConfigurations.Add(sysConfig);
        db.ChatSession.Add(new ChatSession { Id = 303, AspNetUserId = "user303", Title = "Test", CreatedUtc = DateTime.UtcNow, LastMessageUtc = DateTime.UtcNow });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var context = new ToolExecutionContext { SessionId = Overseer.Services.Privacy.SessionRef.Persistent(303), ActiveSystemModelId = 303, AgentDepth = 0, MaxAgentDepth = 1, EnableSubAgents = true };
        var validParams = JsonDocument.Parse("{\"agent_name\":\"wiki_researcher\",\"task\":\"compare prayers\"}").RootElement;

        var result = await tool.ExecuteAsync(validParams, context, CancellationToken.None);

        Assert.Equal("system-secret-key", provider.LastApiKey);
        var run = Assert.Single(result.DelegatedRunCosts!);
        Assert.True(run.IsOperatorFunded);
        Assert.Equal(303, run.SystemModelId);
        Assert.Equal("custom", run.PricingSource);
        var card = new ModelPricingService(new ModelMetadataService(), db).Resolve(sysConfig)!;
        Assert.Equal(ModelPricingService.ComputeCost(card, usages), run.EstimatedCost);
    }

    [Fact]
    public async Task ExecuteAsync_LongContextModel_PricesEachCallOnItsOwnCard()
    {
        var catalog = new ModelMetadataService();
        string? providerName = null;
        string? modelId = null;
        foreach (var p in new[] { "OpenAI", "Google", "Anthropic" })
        {
            var entry = catalog.GetCatalogEntries(p).FirstOrDefault(e =>
                e.Pricing?.LongContext != null && e.Pricing.LongContext.ThresholdInputTokens > 0 &&
                e.SupportsSubAgentExecution && e.Prefixes.Count > 0 && !string.IsNullOrEmpty(e.Prefixes[0]) &&
                catalog.GetMetadata(p, e.Prefixes[0]).DefaultPricing?.LongContext != null);
            if (entry != null)
            {
                providerName = p;
                modelId = entry.Prefixes[0];
                break;
            }
        }
        Assert.NotNull(providerName);
        Assert.NotNull(modelId);

        var (tool, db, provider, _) = CreateTestSetup(
            providerName: providerName,
            customConfig: new Dictionary<string, string?> { { "AI:Model", modelId } });

        var card = new ModelPricingService(new ModelMetadataService(), db).ResolveDefault(providerName, modelId);
        Assert.NotNull(card);
        Assert.NotNull(card.LongContext);
        int threshold = card.LongContext.ThresholdInputTokens;

        var usages = new List<TokenUsageReport>
        {
            new() { TotalPromptTokens = threshold + 5_000, UncachedInputTokens = threshold + 5_000, OutputTokens = 1_500 },
            new() { TotalPromptTokens = 2_000, UncachedInputTokens = 2_000, OutputTokens = 300 }
        };
        provider.UsagePerCall = usages;
        provider.EmitToolCallOnFirstIteration = true;
        db.ChatSession.Add(new ChatSession { Id = 304, AspNetUserId = "user304", Title = "Test", CreatedUtc = DateTime.UtcNow, LastMessageUtc = DateTime.UtcNow });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var context = new ToolExecutionContext { SessionId = Overseer.Services.Privacy.SessionRef.Persistent(304), AgentDepth = 0, MaxAgentDepth = 1, EnableSubAgents = true };
        var validParams = JsonDocument.Parse("{\"agent_name\":\"wiki_researcher\",\"task\":\"compare prayers\"}").RootElement;

        var result = await tool.ExecuteAsync(validParams, context, CancellationToken.None);

        var run = Assert.Single(result.DelegatedRunCosts!);
        Assert.Equal(2, run.ModelCallCount);
        decimal perCall = ModelPricingService.ComputeCost(card, usages);
        decimal flat = ModelPricingService.ComputeCost(card,
            usages.Sum(u => (long)u.UncachedInputTokens), usages.Sum(u => (long)u.OutputTokens));
        Assert.Equal(perCall, run.EstimatedCost);
        Assert.NotEqual(flat, run.EstimatedCost);
    }

    [Fact]
    public async Task ExecuteAsync_ProviderErrorAfterUsage_StillCarriesTheBilledCall()
    {
        var (tool, db, provider, _) = CreateTestSetup();
        var usages = OneUsage();
        provider.UsagePerCall = usages;
        provider.StreamErrorAfterUsage = "invalid_request_error: unsupported parameter";
        db.ChatSession.Add(new ChatSession { Id = 305, AspNetUserId = "user305", Title = "Test", CreatedUtc = DateTime.UtcNow, LastMessageUtc = DateTime.UtcNow });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var context = new ToolExecutionContext { SessionId = Overseer.Services.Privacy.SessionRef.Persistent(305), AgentDepth = 0, MaxAgentDepth = 1, EnableSubAgents = true };
        var validParams = JsonDocument.Parse("{\"agent_name\":\"wiki_researcher\",\"task\":\"compare prayers\"}").RootElement;

        var result = await tool.ExecuteAsync(validParams, context, CancellationToken.None);

        var run = Assert.Single(result.DelegatedRunCosts!);
        Assert.Equal(1, run.ModelCallCount);
        var card = new ModelPricingService(new ModelMetadataService(), db).ResolveDefault("OpenAI", "gpt-5.6-luna")!;
        Assert.Equal(ModelPricingService.ComputeCost(card, usages), run.EstimatedCost);
    }

    [Fact]
    public async Task ExecuteAsync_UnknownProvider_CarriesNoRunCost()
    {
        var (tool, db, _, _) = CreateTestSetup(customConfig: new Dictionary<string, string?> { { "AI:Provider", "NoSuchProvider" } });
        db.ChatSession.Add(new ChatSession { Id = 306, AspNetUserId = "user306", Title = "Test", CreatedUtc = DateTime.UtcNow, LastMessageUtc = DateTime.UtcNow });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var context = new ToolExecutionContext { SessionId = Overseer.Services.Privacy.SessionRef.Persistent(306), AgentDepth = 0, MaxAgentDepth = 1, EnableSubAgents = true };
        var validParams = JsonDocument.Parse("{\"agent_name\":\"wiki_researcher\",\"task\":\"compare prayers\"}").RootElement;

        var result = await tool.ExecuteAsync(validParams, context, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("is not available", result.ErrorMessage);
        Assert.Null(result.DelegatedRunCosts);
    }

    private class EmptySubAgentCatalogService : SubAgentCatalogService
    {
        public EmptySubAgentCatalogService(IConfiguration config, ILogger<SubAgentCatalogService> logger)
            : base(config, logger) { }

        public override IReadOnlyList<SubAgentDefinition> GetEnabledSubAgents() => new List<SubAgentDefinition>();
        public override IReadOnlyList<SubAgentDefinition> GetSubAgents() => new List<SubAgentDefinition>();
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

        public override IReadOnlyList<SubAgentDefinition> GetEnabledSubAgents()
        {
            return new List<SubAgentDefinition> { _customAgent };
        }
    }

    [Fact]
    public void ParameterSchema_IncludesOptionalSubagentNameParameter()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var catalogService = new SubAgentCatalogService(config, NullLogger<SubAgentCatalogService>.Instance);
        var manager = new OngoingChatManager(config);
        var metadata = new ModelMetadataService();
        var services = new ServiceCollection();
        var sp = services.BuildServiceProvider();

        var tool = new DelegateToSubAgentTool(
            catalogService,
            manager,
            sp.GetRequiredService<IServiceScopeFactory>(),
            config,
            metadata,
            NullLogger<DelegateToSubAgentTool>.Instance);

        var schema = tool.ParameterSchema;
        Assert.True(schema.TryGetProperty("properties", out var props));
        Assert.True(props.TryGetProperty("subagent_name", out var subProp));
        Assert.Equal("string", subProp.GetProperty("type").GetString());
        Assert.False(string.IsNullOrWhiteSpace(subProp.GetProperty("description").GetString()));

        Assert.True(schema.TryGetProperty("required", out var req));
        var reqList = req.EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("agent_name", reqList);
        Assert.Contains("task", reqList);
        Assert.DoesNotContain("subagent_name", reqList);
    }

    /// <summary>Raw JSON values for <c>subagent_name</c> that cannot be used as a title.</summary>
    public static TheoryData<string> UnusableSubagentNames => new()
    {
        "\"\"",                                // blank
        "\"   \"",                             // whitespace only
        "\"" + new string('z', 500) + "\"",    // oversized
        "42"                                   // not a string
    };

    [Theory]
    [MemberData(nameof(UnusableSubagentNames))]
    public async Task ExecuteAsync_Succeeds_WithAnUnusableSubagentName(string subagentNameJson)
    {
        var (tool, db, provider, manager) = CreateTestSetup();
        var context = new ToolExecutionContext { SessionId = Overseer.Services.Privacy.SessionRef.Persistent(1), AgentDepth = 0, MaxAgentDepth = 1, EnableSubAgents = true };
        var pars = JsonDocument.Parse($"{{\"agent_name\":\"wiki_researcher\",\"task\":\"Research stats\",\"subagent_name\":{subagentNameJson}}}").RootElement;

        var result = await tool.ExecuteAsync(pars, context, CancellationToken.None);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task ExecuteAsync_PersonalizesSeedSystemMessage_WithoutMutatingCatalog()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var catalogService = new SubAgentCatalogService(config, NullLogger<SubAgentCatalogService>.Instance);
        string originalInstructions = catalogService.GetSubAgent("wiki_researcher")!.Instructions;

        var (tool, db, provider, manager) = CreateTestSetup(mockCatalog: catalogService);
        var context = new ToolExecutionContext { SessionId = Overseer.Services.Privacy.SessionRef.Persistent(1), AgentDepth = 0, MaxAgentDepth = 1, EnableSubAgents = true };

        // Delegation 1
        var pars1 = JsonDocument.Parse("{\"agent_name\":\"wiki_researcher\",\"task\":\"Task 1\",\"subagent_name\":\"Instance One\"}").RootElement;
        var result1 = await tool.ExecuteAsync(pars1, context, CancellationToken.None);
        Assert.True(result1.Success);
        Assert.NotNull(provider.LastMessageHistory);

        var firstSysMsgObj = provider.LastMessageHistory[0];
        string firstContent = (string)firstSysMsgObj.GetType().GetProperty("content")!.GetValue(firstSysMsgObj)!;
        Assert.Contains("Instance One", firstContent);
        Assert.Contains(originalInstructions, firstContent);

        // Delegation 2
        var pars2 = JsonDocument.Parse("{\"agent_name\":\"wiki_researcher\",\"task\":\"Task 2\",\"subagent_name\":\"Instance Two\"}").RootElement;
        var result2 = await tool.ExecuteAsync(pars2, context, CancellationToken.None);
        Assert.True(result2.Success);

        var secondSysMsgObj = provider.LastMessageHistory[0];
        string secondContent = (string)secondSysMsgObj.GetType().GetProperty("content")!.GetValue(secondSysMsgObj)!;
        Assert.Contains("Instance Two", secondContent);
        Assert.DoesNotContain("Instance One", secondContent);

        // Catalog instruction verification
        string afterInstructions = catalogService.GetSubAgent("wiki_researcher")!.Instructions;
        Assert.Equal(originalInstructions, afterInstructions);
    }

    private static (DelegateToSubAgentTool tool, ApplicationDbContext db, CapturingMockProvider provider, OngoingChatManager manager) CreateTestSetup(
        string providerName = "OpenAI",
        Dictionary<string, string?>? customConfig = null,
        ModelMetadataService? mockMetadata = null,
        SubAgentCatalogService? mockCatalog = null,
        Overseer.Services.ApiKeyAlerts.ApiKeyAlertService? alertService = null)
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
        var cache = new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());
        var toolExecutor = new ToolExecutor(handlers, clientBridge, NullLogger<ToolExecutor>.Instance, cache, config);

        services.AddSingleton(db);
        services.AddSingleton(crypto);
        services.AddSingleton<SettingsService>();
        services.AddSingleton<SystemAiConfigService>();
        services.AddSingleton<IAiProvider>(mockProvider);
        services.AddSingleton(metadata);
        services.AddSingleton(toolRegistry);
        services.AddSingleton(toolExecutor);
        services.AddSingleton(new KnowledgeBaseService(NullLogger<KnowledgeBaseService>.Instance, config));
        services.AddSingleton(catalogService);
        services.AddSingleton<IHttpClientFactory>(new MockHttpClientFactory());
        services.AddSingleton<IConfiguration>(config);
        services.AddSingleton<Overseer.Services.Privacy.EndpointPolicy>();
        services.AddSingleton<Overseer.Services.Privacy.ConfidentialityPostureService>();
        services.AddSingleton<AgentLoopRunner>();
        services.AddScoped<ModelPricingService>();
        if (alertService != null)
        {
            services.AddSingleton(alertService);
        }

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
        public string? LastApiKey { get; private set; }
        public List<object>? LastMessageHistory { get; private set; }
        public bool? LastDisablePromptCache { get; private set; }
        public bool EmitToolCallOnFirstIteration { get; set; } = false;

        /// <summary>When set, every stream yields only this provider error event.</summary>
        public string? StreamErrorBeforeChunks { get; set; }

        /// <summary>When set, call n also yields a usage report of UsagePerCall[n - 1], if there is one.</summary>
        public List<TokenUsageReport>? UsagePerCall { get; set; }

        /// <summary>When set, every stream yields its usage report and then only this provider error event:
        /// a call that was billed and then failed.</summary>
        public string? StreamErrorAfterUsage { get; set; }
        private int _streamCallCount = 0;

        private TokenUsageReport? UsageFor(int call) =>
            UsagePerCall != null && call <= UsagePerCall.Count ? UsagePerCall[call - 1] : null;

        public void AppendAssistantToolCallsToHistory(List<object> messageHistory, string iterationText, List<JsonElement> toolCalls, List<JsonElement>? providerHistoryItems = null)
        {
            messageHistory.Add(new { role = "assistant", content = iterationText });
        }

        public void AppendToolResultsToHistory(List<object> messageHistory, List<ProviderToolResult> results)
        {
            foreach (var r in results)
                messageHistory.Add(new { role = "tool", content = r.Content });
        }

        public Dictionary<string, object> BuildChatRequestBody(string modelId, List<object> messageHistory, int? maxOutputTokens, string? thinkingLevel, ToolsForRequest requestTools, string? reasoningMode = null, string? reasoningSummary = null, string? serviceTier = null, bool? parallelToolCalls = null, SegmentedPrompt? segmentedPrompt = null, string? promptCacheKey = null, bool cacheConversationTail = true, bool disablePromptCache = false)
        {
            LastModelId = modelId;
            LastMaxOutputTokens = maxOutputTokens;
            LastThinkingLevel = thinkingLevel;
            LastReasoningMode = reasoningMode;
            LastServiceTier = serviceTier;
            LastMessageHistory = new List<object>(messageHistory);
            LastDisablePromptCache = disablePromptCache;
            return new Dictionary<string, object>();
        }

        public bool TryRewriteToolResult(List<object> messageHistory, string toolCallId, string replacementText) => false;

        public object BuildFunctionDeclaration(string name, string description, object parameterSchema) => new { name, description, parameterSchema };
        public object? BuildToolsPayload(List<object> providerTools, List<object> functionDeclarations) => null;
        public object? BuildWebSearchTool() => null;
        public void ConfigureRequest(HttpRequestMessage request, string apiKey, AiEndpointDescriptor endpoint)
        {
            LastApiKey = apiKey;
        }
        public object FormatMessage(string role, string text, List<SendMessageAttachment>? imageAttachments) => new { role, content = text };
        public string GetChatStreamUrl(string modelId, string apiKey, AiEndpointDescriptor endpoint) => "https://mock.stream.test";
        public async IAsyncEnumerable<ChatEvent> ParseStreamAsync(HttpResponseMessage response, bool showDebugLog, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            int call = System.Threading.Interlocked.Increment(ref _streamCallCount);
            if (StreamErrorBeforeChunks != null)
            {
                yield return new ChatEvent { Type = "error", Data = StreamErrorBeforeChunks };
                yield break;
            }
            if (StreamErrorAfterUsage != null)
            {
                if (UsageFor(call) is { } billed)
                    yield return new ChatEvent { Type = "usage", UsageReport = billed };
                yield return new ChatEvent { Type = "error", Data = StreamErrorAfterUsage };
                yield break;
            }
            yield return new ChatEvent { Type = "debug", Data = "subagent-debug-line" };
            yield return new ChatEvent { Type = "chunk", Data = "subagent-chunk-prose" };
            if (UsageFor(call) is { } usage)
                yield return new ChatEvent { Type = "usage", UsageReport = usage };

            if (EmitToolCallOnFirstIteration && call == 1)
            {
                yield return new ChatEvent
                {
                    Type = "tool_call_complete",
                    Data = JsonSerializer.Serialize(new
                    {
                        id = "subagent-tool-call-1",
                        name = "wiki_search",
                        arguments = "{\"query\":\"prayer timeout\"}"
                    })
                };
            }
            await Task.CompletedTask;
        }
        public List<object> PrepareMessageHistory(List<object> messages) => messages;
        public Dictionary<string, object> BuildTitleRequestBody(string modelId, string systemPrompt, string userMessage, int maxTokens, string? serviceTier = null) => new();
        public string GetTitleUrl(string modelId, string apiKey, AiEndpointDescriptor endpoint) => "https://mock.stream.test/title";
        public string? ParseTitleResponse(JsonElement root) => "Mock Title";
    }

    private class NullClientBridge : IClientToolBridge
    {
        public bool IsClientConnected => true;
        public Task<ToolResult> SendToolRequestAsync(Overseer.Services.Privacy.SessionRef sessionRef, string toolName, JsonElement parameters, CancellationToken cancellationToken)
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
