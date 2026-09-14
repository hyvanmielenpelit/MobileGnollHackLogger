using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MobileGnollHackLogger.Data;
using Overseer.Controllers;
using Overseer.Services;
using Overseer.Services.Privacy;
using Overseer.Services.Providers;
using Overseer.Services.Tools;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// The model eligibility gate as a whole turn: what a confidential chat does when the funding
/// credential has been refused, when nothing is established about it, and when the user has
/// already said yes.
/// </summary>
/// <remarks>
/// Both refusing exits happen before the user message is built and before any provider call, so
/// the assertions are about what did <em>not</em> happen as much as about the event emitted.
/// The provider is a stub and no network call is made either way.
/// </remarks>
public class ConfidentialGateTurnTests : IDisposable
{
    private const string ProviderName = "StubProvider";
    private const string ModelId = "stub-model-1";

    private readonly string _dataDirectory =
        Path.Combine(Path.GetTempPath(), "overseer-gate-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dataDirectory)) Directory.Delete(_dataDirectory, recursive: true);
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        /// <summary>How many times the turn actually reached the network.</summary>
        public int Calls;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("{\"stub\":true}")
            });
        }
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public StubHttpMessageHandler Handler { get; } = new();
        public HttpClient CreateClient(string name) => new(Handler, disposeHandler: false);
    }

    private sealed class StubAiProvider : IAiProvider
    {
        public const string ReplyText = "A stub answer.";

        public string ProviderName => ConfidentialGateTurnTests.ProviderName;
        public IReadOnlyList<string> SupportedServiceTiers => new[] { "default" };

        public void AppendAssistantToolCallsToHistory(List<object> messageHistory, string iterationText, List<JsonElement> toolCalls, List<JsonElement>? providerHistoryItems = null)
            => messageHistory.Add(new { role = "assistant", content = iterationText });

        public void AppendToolResultsToHistory(List<object> messageHistory, List<ProviderToolResult> results)
        {
            foreach (var r in results) messageHistory.Add(new { role = "tool", content = r.Content });
        }

        public Dictionary<string, object> BuildChatRequestBody(string modelId, List<object> messageHistory, int? maxOutputTokens, string? thinkingLevel, ToolsForRequest requestTools, string? reasoningMode = null, string? reasoningSummary = null, string? serviceTier = null, bool? parallelToolCalls = null, SegmentedPrompt? segmentedPrompt = null, string? promptCacheKey = null, bool cacheConversationTail = true)
            => new Dictionary<string, object> { { "model", modelId } };

        public bool TryRewriteToolResult(List<object> messageHistory, string toolCallId, string replacementText) => false;
        public object BuildFunctionDeclaration(string name, string description, object parameterSchema) => new { name };
        public object? BuildToolsPayload(List<object> providerTools, List<object> functionDeclarations) => null;
        public object? BuildWebSearchTool() => null;
        public void ConfigureRequest(HttpRequestMessage request, string apiKey, AiEndpointDescriptor endpoint) { }
        public object FormatMessage(string role, string text, List<SendMessageAttachment>? imageAttachments)
            => new { role, content = text };
        public string GetChatStreamUrl(string modelId, string apiKey, AiEndpointDescriptor endpoint)
            => endpoint.ComposeUrl("https://stub.ai.test/stream", "/stream");

        public async IAsyncEnumerable<ChatEvent> ParseStreamAsync(HttpResponseMessage response, bool showDebugLog, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            yield return new ChatEvent { Type = "chunk", Data = ReplyText };
            await Task.CompletedTask;
        }

        public List<object> PrepareMessageHistory(List<object> messages) => new(messages);
        public Dictionary<string, object> BuildTitleRequestBody(string modelId, string systemPrompt, string userMessage, int maxTokens, string? serviceTier = null) => new();
        public string GetTitleUrl(string modelId, string apiKey, AiEndpointDescriptor endpoint) => "https://stub.ai.test/title";
        public string? ParseTitleResponse(JsonElement root) => "Stub Title";
    }

    private sealed class StubClientToolBridge : IClientToolBridge
    {
        public bool IsClientConnected => false;
        public Task<ToolResult> SendToolRequestAsync(SessionRef sessionRef, string toolName, JsonElement parameters, CancellationToken ct)
            => Task.FromResult(new ToolResult { Success = false, ErrorMessage = "no client" });
    }

    private (ServiceProvider Container, StubHttpClientFactory Http) BuildContainer(string databaseName)
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "AesEncryptionKey", Convert.ToBase64String(new byte[32]) },
                { "ConversationsDataLocation", _dataDirectory },
                { "PrivacySettings:Ephemeral:TimeoutMinutes", "60" },
                { "PrivacySettings:ActiveKeyVersion", null }
            })
            .Build();

        var httpFactory = new StubHttpClientFactory();

        services.AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(databaseName));
        services.AddSingleton<IConfiguration>(config);
        services.AddSingleton<IHttpClientFactory>(httpFactory);
        services.AddLogging();
        services.AddSignalR();
        services.AddMemoryCache();
        services.AddSingleton<IClientToolBridge, StubClientToolBridge>();
        services.AddScoped<ToolRegistry>();
        services.AddScoped<ToolExecutor>();
        services.AddScoped<CryptoService>();
        services.AddScoped<WikiService>();
        services.AddScoped<ModelMetadataService>();
        services.AddScoped<KnowledgeBaseService>();
        services.AddScoped<OngoingChatManager>();
        services.AddSingleton<StubAiProvider>();
        services.AddSingleton<IAiProvider>(sp => sp.GetRequiredService<StubAiProvider>());
        services.AddScoped<Overseer.Services.Agents.AgentLoopRunner>();
        services.AddSingleton<ParallelExecutionResolver>();
        services.AddSingleton<AttachmentValidator>();
        services.AddSingleton<IAntiMalwareScanner, NullAntiMalwareScanner>();
        services.AddSingleton<EndpointPolicy>();
        services.AddSingleton<ConfidentialityPostureService>();
        services.AddSingleton<IContentKeyRing, ConfigurationContentKeyRing>();
        services.AddSingleton<ContentProtectionService>();
        services.AddSingleton<Overseer.Services.Privacy.Dlp.DlpScannerService>();
        services.AddSingleton<Overseer.Services.Documents.DocumentParserService>();
        services.AddSingleton<Overseer.Services.Rag.DocumentChunker>();
        services.AddSingleton<Overseer.Services.Rag.IEmbeddingService,
            Overseer.Services.Rag.LocalOnnxEmbeddingService>();
        services.AddSingleton<Overseer.Services.Rag.DocumentRagService>();
        services.AddScoped<Overseer.Services.Rag.RagSidecarStore>();
        services.AddSingleton(sp => new EphemeralSessionStore(
            sp.GetRequiredService<IConfiguration>(), logger: null, startSweeper: false));
        services.AddScoped<ModelPricingService>();
        services.AddScoped<SystemAiConfigService>();
        services.AddScoped<ChatService>();

        return (services.BuildServiceProvider(), httpFactory);
    }

    /// <summary>Seeds a user funding their turns with their own key, and returns the user id.</summary>
    private static async Task<string> SeedUserKeyAsync(
        ServiceProvider container, string? posture = null, bool? trusted = null)
    {
        string userId = Guid.NewGuid().ToString();
        using var scope = container.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var crypto = scope.ServiceProvider.GetRequiredService<CryptoService>();

        db.Users.Add(new ApplicationUser { Id = userId, UserName = "gate-tester" });
        db.UserAiSettings.Add(new UserAiSettings { AspNetUserId = userId, EnableToolUse = false });

        var (ciphertext, nonce, tag) = crypto.Encrypt("stub-api-key", userId);
        db.UserAiApiKeys.Add(new UserAiApiKey
        {
            AspNetUserId = userId,
            Provider = ProviderName,
            EncryptedApiKey = ciphertext,
            ApiKeyNonce = nonce,
            ApiKeyTag = tag,
            ConfidentialityPosture = posture,
            PostureDeclaredUtc = posture == null ? null : DateTime.UtcNow,
            UserTrustsForConfidential = trusted,
            ConfidentialTrustDecidedUtc = trusted.HasValue ? DateTime.UtcNow : null
        });
        db.UserAiModels.Add(new UserAiModel
        {
            AspNetUserId = userId,
            Provider = ProviderName,
            ModelId = ModelId,
            OrderIndex = 0
        });

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return userId;
    }

    /// <summary>
    /// Seeds a user whose only model is an operator-provided one, optionally with a recorded
    /// decision about it. Returns the user id and the configuration id.
    /// </summary>
    private static async Task<(string UserId, long ConfigId)> SeedSystemModelAsync(
        ServiceProvider container, bool? trusted = null, DateTime? postureVerifiedUtc = null,
        string? posture = null)
    {
        string userId = Guid.NewGuid().ToString();
        using var scope = container.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var crypto = scope.ServiceProvider.GetRequiredService<CryptoService>();

        db.Users.Add(new ApplicationUser { Id = userId, UserName = "gate-tester" });
        db.UserAiSettings.Add(new UserAiSettings { AspNetUserId = userId, EnableToolUse = false });

        var (ciphertext, nonce, tag) = crypto.Encrypt("stub-system-key", "SYSTEM_API_KEY");
        var config = new SystemAiApiConfiguration
        {
            Provider = ProviderName,
            ModelId = ModelId,
            DisplayName = "Provided Stub",
            IsEnabled = true,
            IsSystemWide = true,
            ModelRole = 1,
            EncryptedApiKey = ciphertext,
            ApiKeyNonce = nonce,
            ApiKeyTag = tag,
            ConfidentialityPosture = posture,
            PostureVerifiedUtc = postureVerifiedUtc
        };
        db.SystemAiApiConfigurations.Add(config);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        if (trusted.HasValue)
        {
            db.UserSystemModelConfidentialTrusts.Add(new UserSystemModelConfidentialTrust
            {
                AspNetUserId = userId,
                SystemAiApiConfigurationId = config.Id,
                UserTrustsForConfidential = trusted.Value,
                DecidedUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        return (userId, config.Id);
    }

    /// <summary>
    /// A confidential session template carrying the gate the test is exercising. Ephemeral
    /// rather than persisted, so the turn needs no keyring: the gate runs off the session's
    /// policy snapshot and does not care which kind of session holds it.
    /// </summary>
    private static ChatSession Template(string userId, ConfidentialityGateMode gate)
    {
        var template = new ChatSession
        {
            AspNetUserId = userId,
            Title = "Confidential chat",
            CreatedUtc = DateTime.UtcNow,
            LastMessageUtc = DateTime.UtcNow,
            IsConfidential = true
        };

        ConfidentialPolicyResolver.ApplyToSession(
            template, ConfidentialPolicy.Defaults with { ModelGate = gate });

        return template;
    }

    private static async Task<List<ChatEvent>> RunTurnAsync(
        ServiceProvider container, SessionRef sessionRef, string userId, long? systemModelId = null)
    {
        var chatService = container.CreateScope().ServiceProvider.GetRequiredService<ChatService>();
        var events = new List<ChatEvent>();

        await foreach (var evt in chatService.StreamMessageAsync(
            sessionRef, "Something private", null, userId, false,
            TestContext.Current.CancellationToken, systemModelId: systemModelId))
        {
            events.Add(evt);
        }

        return events;
    }

    [Fact]
    public async Task AKeyMarkedNotSuitableRefusesTheTurnAndReachesNoProvider()
    {
        var (container, http) = BuildContainer(Guid.NewGuid().ToString());
        using var _ = container;

        string userId = await SeedUserKeyAsync(container, trusted: false);
        var store = container.GetRequiredService<EphemeralSessionStore>();
        var held = store.Create(userId, Template(userId, ConfidentialityGateMode.UserDecides));

        var events = await RunTurnAsync(container, held.Ref, userId);

        var error = Assert.Single(events, e => e.Type == "error");
        Assert.Contains("not suitable for confidential chats", error.Data);
        Assert.DoesNotContain(events, e => e.Type == "user_message_created");
        Assert.Equal(0, http.Handler.Calls);
        // Refused before anything was written: the store holds no turn either.
        Assert.Empty(held.Messages);
    }

    [Fact]
    public async Task VerifiedPostureOnlyRefusesASelfDeclaredPostureAndSaysWhy()
    {
        var (container, http) = BuildContainer(Guid.NewGuid().ToString());
        using var _ = container;

        string userId = await SeedUserKeyAsync(container, posture: "ZeroRetention");
        var store = container.GetRequiredService<EphemeralSessionStore>();
        var held = store.Create(userId, Template(userId, ConfidentialityGateMode.VerifiedPostureOnly));

        var events = await RunTurnAsync(container, held.Ref, userId);

        var error = Assert.Single(events, e => e.Type == "error");
        Assert.Contains("self-declared", error.Data);
        Assert.Equal(0, http.Handler.Calls);
    }

    [Fact]
    public async Task AskWhenUnclearAsksOnceAboutAnUndecidedUserKey()
    {
        var (container, http) = BuildContainer(Guid.NewGuid().ToString());
        using var _ = container;

        string userId = await SeedUserKeyAsync(container);
        var store = container.GetRequiredService<EphemeralSessionStore>();
        var held = store.Create(userId, Template(userId, ConfidentialityGateMode.AskWhenUnclear));

        var events = await RunTurnAsync(container, held.Ref, userId);

        var ask = Assert.Single(events, e => e.Type == "confidential_gate");
        using var payload = JsonDocument.Parse(ask.Data!);
        Assert.Equal("user_key", payload.RootElement.GetProperty("kind").GetString());
        Assert.Equal(ProviderName, payload.RootElement.GetProperty("provider").GetString());
        Assert.False(string.IsNullOrWhiteSpace(payload.RootElement.GetProperty("reason").GetString()));

        Assert.DoesNotContain(events, e => e.Type == "error");
        Assert.DoesNotContain(events, e => e.Type == "user_message_created");
        Assert.Equal(0, http.Handler.Calls);
        Assert.Empty(held.Messages);
    }

    [Fact]
    public async Task AskWhenUnclearAsksAboutAnUndecidedProvidedModel()
    {
        var (container, http) = BuildContainer(Guid.NewGuid().ToString());
        using var _ = container;

        var (userId, configId) = await SeedSystemModelAsync(container);
        var store = container.GetRequiredService<EphemeralSessionStore>();
        var held = store.Create(userId, Template(userId, ConfidentialityGateMode.AskWhenUnclear));

        var events = await RunTurnAsync(container, held.Ref, userId, systemModelId: configId);

        var ask = Assert.Single(events, e => e.Type == "confidential_gate");
        using var payload = JsonDocument.Parse(ask.Data!);
        Assert.Equal("system_model", payload.RootElement.GetProperty("kind").GetString());
        Assert.Equal(configId, payload.RootElement.GetProperty("systemConfigurationId").GetInt64());
        Assert.Equal("Provided Stub", payload.RootElement.GetProperty("modelDisplayName").GetString());
        Assert.Equal(0, http.Handler.Calls);
    }

    [Fact]
    public async Task ADecisionOfYesOnAProvidedModelLetsTheTurnRunAndEmitsTheBadge()
    {
        var (container, _http) = BuildContainer(Guid.NewGuid().ToString());
        using var _ = container;

        var (userId, configId) = await SeedSystemModelAsync(container, trusted: true);
        var store = container.GetRequiredService<EphemeralSessionStore>();
        var held = store.Create(userId, Template(userId, ConfidentialityGateMode.AskWhenUnclear));

        var events = await RunTurnAsync(container, held.Ref, userId, systemModelId: configId);

        Assert.DoesNotContain(events, e => e.Type == "confidential_gate");
        Assert.DoesNotContain(events, e => e.Type == "error");

        var badge = Assert.Single(events, e => e.Type == "private_badge");
        using var payload = JsonDocument.Parse(badge.Data!);
        // Nothing is established about this provided model's retention, so the honest colour is
        // orange: a self-declared or unknown posture can never produce green.
        Assert.Equal("orange", payload.RootElement.GetProperty("state").GetString());
        Assert.Equal("Private", payload.RootElement.GetProperty("label").GetString());
        Assert.False(string.IsNullOrWhiteSpace(payload.RootElement.GetProperty("tooltip").GetString()));
    }

    [Fact]
    public async Task ADecisionOfNoOnAProvidedModelRefusesAndPointsAtTheRightSetting()
    {
        var (container, http) = BuildContainer(Guid.NewGuid().ToString());
        using var _ = container;

        var (userId, configId) = await SeedSystemModelAsync(container, trusted: false);
        var store = container.GetRequiredService<EphemeralSessionStore>();
        var held = store.Create(userId, Template(userId, ConfidentialityGateMode.UserDecides));

        var events = await RunTurnAsync(container, held.Ref, userId, systemModelId: configId);

        var error = Assert.Single(events, e => e.Type == "error");
        Assert.Contains("System Model Confidentiality", error.Data);
        Assert.DoesNotContain("API keys", error.Data);
        Assert.Equal(0, http.Handler.Calls);
    }

    [Fact]
    public async Task ANonConfidentialSessionEmitsNeitherEvent()
    {
        var (container, _http) = BuildContainer(Guid.NewGuid().ToString());
        using var _ = container;

        string userId = await SeedUserKeyAsync(container);

        using var scope = container.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var session = new ChatSession
        {
            AspNetUserId = userId,
            Title = "Ordinary chat",
            CreatedUtc = DateTime.UtcNow,
            LastMessageUtc = DateTime.UtcNow
        };
        db.ChatSession.Add(session);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var events = await RunTurnAsync(container, SessionRef.Persistent(session.Id), userId);

        Assert.DoesNotContain(events, e => e.Type == "confidential_gate");
        Assert.DoesNotContain(events, e => e.Type == "private_badge");
        Assert.DoesNotContain(events, e => e.Type == "error");
    }
}
