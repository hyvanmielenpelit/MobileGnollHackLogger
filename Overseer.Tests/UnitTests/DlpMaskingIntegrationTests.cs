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
using Overseer.Services.Privacy.Dlp;
using Overseer.Services.Providers;
using Overseer.Services.Tools;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// Outbound masking driven through the real <see cref="ChatService"/>: what the provider
/// actually receives, what the user actually sees, and what the database actually keeps.
/// </summary>
/// <remarks>
/// The provider and the HTTP layer are stubbed, so a whole turn completes with no network call
/// and the stub can be asked to reply with whatever the test needs — including a placeholder the
/// model has echoed, which is the only way to exercise the unmasking half.
/// </remarks>
public class DlpMaskingIntegrationTests : IDisposable
{
    private const string ProviderName = "StubProvider";
    private const string ModelId = "stub-model-1";

    /* A realistic-looking key: random enough to clear the entropy gate, which a placeholder
       like sk-XXXX deliberately does not. */
    private const string Secret = "sk-proj-7Qf2xVn8LpR4tKw9BzYm3JdG6ScHaEuT1oNvXbAiQrZk5W";
    private const string OtherSecret = "AKIA2QF7XV9NLPR4TKW8";

    private readonly string _dataDirectory =
        Path.Combine(Path.GetTempPath(), "overseer-dlp-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dataDirectory)) Directory.Delete(_dataDirectory, recursive: true);
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("{\"stub\":true}")
            });
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new StubHttpMessageHandler());
    }

    private sealed class StubAiProvider : IAiProvider
    {
        public string ProviderName => DlpMaskingIntegrationTests.ProviderName;
        public IReadOnlyList<string> SupportedServiceTiers => new[] { "default" };

        /// <summary>What the last request carried, serialized, so a test can search it.</summary>
        public string LastRequestJson { get; private set; } = "";

        /// <summary>Chunks the stub streams back. Split a token across two to exercise the window.</summary>
        public List<string> ReplyChunks { get; } = new() { "All done." };

        public void AppendAssistantToolCallsToHistory(List<object> messageHistory, string iterationText, List<JsonElement> toolCalls, List<JsonElement>? providerHistoryItems = null)
            => messageHistory.Add(new { role = "assistant", content = iterationText });

        public void AppendToolResultsToHistory(List<object> messageHistory, List<ProviderToolResult> results)
        {
            foreach (var r in results) messageHistory.Add(new { role = "tool", content = r.Content });
        }

        public Dictionary<string, object> BuildChatRequestBody(string modelId, List<object> messageHistory, int? maxOutputTokens, string? thinkingLevel, ToolsForRequest requestTools, string? reasoningMode = null, string? reasoningSummary = null, string? serviceTier = null, bool? parallelToolCalls = null, SegmentedPrompt? segmentedPrompt = null, string? promptCacheKey = null, bool cacheConversationTail = true)
        {
            LastRequestJson = JsonSerializer.Serialize(messageHistory);
            return new Dictionary<string, object> { { "model", modelId } };
        }

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
            foreach (var chunk in ReplyChunks)
            {
                yield return new ChatEvent { Type = "chunk", Data = chunk };
            }
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

    private ServiceProvider BuildContainer(string databaseName, params (string Key, string? Value)[] extraConfig)
    {
        var services = new ServiceCollection();
        var settings = new Dictionary<string, string?>
        {
            { "AesEncryptionKey", Convert.ToBase64String(new byte[32]) },
            { "ConversationsDataLocation", _dataDirectory },
            { "AiPerformanceSettings:IncludeToolCallHistoryDigest", "false" }
        };
        foreach (var (key, value) in extraConfig) settings[key] = value;

        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        services.AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(databaseName));
        services.AddSingleton<IConfiguration>(config);
        services.AddSingleton<IHttpClientFactory, StubHttpClientFactory>();
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
        services.AddSingleton<IContentKeyRing, ConfigurationContentKeyRing>();
        services.AddSingleton<ContentProtectionService>();
        services.AddSingleton(sp => new EphemeralSessionStore(
            sp.GetRequiredService<IConfiguration>(), logger: null, startSweeper: false));
        services.AddSingleton<DlpScannerService>();
        services.AddSingleton<Overseer.Services.Documents.DocumentParserService>();
        services.AddSingleton<Overseer.Services.Rag.DocumentChunker>();
        services.AddSingleton<Overseer.Services.Rag.IEmbeddingService,
            Overseer.Services.Rag.LocalOnnxEmbeddingService>();
        services.AddSingleton<Overseer.Services.Rag.DocumentRagService>();
        services.AddScoped<Overseer.Services.Rag.RagSidecarStore>();
        services.AddScoped<ModelPricingService>();
        services.AddScoped<SystemAiConfigService>();
        services.AddScoped<ChatService>();
        return services.BuildServiceProvider();
    }

    private static async Task<string> SeedUserAsync(ServiceProvider container, Action<UserAiSettings>? configureSettings = null)
    {
        string userId = Guid.NewGuid().ToString();
        using var scope = container.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var crypto = scope.ServiceProvider.GetRequiredService<CryptoService>();

        db.Users.Add(new ApplicationUser { Id = userId, UserName = "dlp-tester" });

        var aiSettings = new UserAiSettings { AspNetUserId = userId, EnableToolUse = false };
        configureSettings?.Invoke(aiSettings);
        db.UserAiSettings.Add(aiSettings);

        var (ciphertext, nonce, tag) = crypto.Encrypt("stub-api-key", userId);
        db.UserAiApiKeys.Add(new UserAiApiKey
        {
            AspNetUserId = userId,
            Provider = ProviderName,
            EncryptedApiKey = ciphertext,
            ApiKeyNonce = nonce,
            ApiKeyTag = tag
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

    private static async Task<long> SeedSessionAsync(ServiceProvider container, string userId)
    {
        using var scope = container.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var session = new ChatSession
        {
            AspNetUserId = userId,
            Title = "DLP test chat",
            CreatedUtc = DateTime.UtcNow,
            LastMessageUtc = DateTime.UtcNow
        };
        db.ChatSession.Add(session);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return session.Id;
    }

    private static async Task<List<ChatEvent>> RunTurnAsync(
        ServiceProvider container, long sessionId, string userId, string message)
    {
        var chatService = container.CreateScope().ServiceProvider.GetRequiredService<ChatService>();
        var events = new List<ChatEvent>();
        await foreach (var evt in chatService.StreamMessageAsync(
            SessionRef.Persistent(sessionId), message, null, userId, false,
            TestContext.Current.CancellationToken))
        {
            events.Add(evt);
        }
        return events;
    }

    private static string VisibleText(IEnumerable<ChatEvent> events)
        => string.Concat(events.Where(e => e.Type == "chunk").Select(e => e.Data));

    // ── the outbound half ───────────────────────────────────────────────────────

    [Fact]
    public async Task ASecretInTheUsersMessageNeverReachesTheProvider()
    {
        using var container = BuildContainer(Guid.NewGuid().ToString());
        string userId = await SeedUserAsync(container);
        long sessionId = await SeedSessionAsync(container, userId);
        var stub = container.GetRequiredService<StubAiProvider>();

        await RunTurnAsync(container, sessionId, userId, $"Is this key valid? {Secret}");

        Assert.DoesNotContain(Secret, stub.LastRequestJson);
        Assert.Contains("[REDACTED_API_KEY_1]", stub.LastRequestJson);
    }

    [Fact]
    public async Task TheDatabaseStillHoldsWhatTheUserActuallyTyped()
    {
        // Masking is an egress control, not a storage one: the row is the user's own record of
        // their own message, and in a confidential session Stage F encrypts it.
        using var container = BuildContainer(Guid.NewGuid().ToString());
        string userId = await SeedUserAsync(container);
        long sessionId = await SeedSessionAsync(container, userId);

        await RunTurnAsync(container, sessionId, userId, $"Is this key valid? {Secret}");

        using var scope = container.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var stored = await db.ChatMessage
            .Where(m => m.ChatSessionId == sessionId && m.Role == "user")
            .Select(m => m.Content)
            .FirstAsync(TestContext.Current.CancellationToken);

        Assert.Contains(Secret, stored);
    }

    [Fact]
    public async Task ASecretPersistedOnTheFirstTurnIsStillMaskedOnTheSecond()
    {
        /* N-6, and the reason masking cannot be limited to the new message: the database holds
           the unmasked text by design, so turn two replays it. Masking only what the user just
           typed would send the secret in clear on every turn after the first. */
        using var container = BuildContainer(Guid.NewGuid().ToString());
        string userId = await SeedUserAsync(container);
        long sessionId = await SeedSessionAsync(container, userId);
        var stub = container.GetRequiredService<StubAiProvider>();

        await RunTurnAsync(container, sessionId, userId, $"Is this key valid? {Secret}");
        await RunTurnAsync(container, sessionId, userId, "What did I just ask you?");

        Assert.DoesNotContain(Secret, stub.LastRequestJson);
        Assert.Contains("[REDACTED_API_KEY_1]", stub.LastRequestJson);
    }

    [Fact]
    public async Task TheSameSecretInHistoryAndInTheNewMessageYieldsOneToken()
    {
        // R-13: two tokens for one credential would have the model reason about them as two
        // different keys.
        using var container = BuildContainer(Guid.NewGuid().ToString());
        string userId = await SeedUserAsync(container);
        long sessionId = await SeedSessionAsync(container, userId);
        var stub = container.GetRequiredService<StubAiProvider>();

        await RunTurnAsync(container, sessionId, userId, $"Here it is: {Secret}");
        await RunTurnAsync(container, sessionId, userId, $"And again: {Secret}");

        Assert.DoesNotContain(Secret, stub.LastRequestJson);
        Assert.Contains("[REDACTED_API_KEY_1]", stub.LastRequestJson);
        Assert.DoesNotContain("[REDACTED_API_KEY_2]", stub.LastRequestJson);
    }

    [Fact]
    public async Task TwoDifferentSecretsGetTwoDifferentTokens()
    {
        using var container = BuildContainer(Guid.NewGuid().ToString());
        string userId = await SeedUserAsync(container);
        long sessionId = await SeedSessionAsync(container, userId);
        var stub = container.GetRequiredService<StubAiProvider>();

        await RunTurnAsync(container, sessionId, userId, $"Two keys: {Secret} and {OtherSecret}");

        Assert.DoesNotContain(Secret, stub.LastRequestJson);
        Assert.DoesNotContain(OtherSecret, stub.LastRequestJson);
        Assert.Contains("[REDACTED_API_KEY_1]", stub.LastRequestJson);
        Assert.Contains("[REDACTED_API_KEY_2]", stub.LastRequestJson);
    }

    [Fact]
    public async Task ADisabledClassIsSentInClear()
    {
        // The switch has to actually reach the turn, or the settings page is decoration.
        using var container = BuildContainer(Guid.NewGuid().ToString());
        string userId = await SeedUserAsync(container, s => s.DlpMaskApiKeys = false);
        long sessionId = await SeedSessionAsync(container, userId);
        var stub = container.GetRequiredService<StubAiProvider>();

        await RunTurnAsync(container, sessionId, userId, $"Is this key valid? {Secret}");

        Assert.Contains(Secret, stub.LastRequestJson);
    }

    [Fact]
    public async Task TheAdministratorsFloorOverridesAUserWhoTurnedTheClassOff()
    {
        using var container = BuildContainer(
            Guid.NewGuid().ToString(), ("PrivacySettings:DlpFloor:ApiKeys", "true"));
        string userId = await SeedUserAsync(container, s => s.DlpMaskApiKeys = false);
        long sessionId = await SeedSessionAsync(container, userId);
        var stub = container.GetRequiredService<StubAiProvider>();

        await RunTurnAsync(container, sessionId, userId, $"Is this key valid? {Secret}");

        Assert.DoesNotContain(Secret, stub.LastRequestJson);
    }

    [Fact]
    public async Task AnAddressIsSentInClearUnlessTheUserAsksOtherwise()
    {
        // Off by default: masking addresses measurably degrades answers, for a class of data
        // the user usually meant to send.
        using var container = BuildContainer(Guid.NewGuid().ToString());
        string userId = await SeedUserAsync(container);
        long sessionId = await SeedSessionAsync(container, userId);
        var stub = container.GetRequiredService<StubAiProvider>();

        await RunTurnAsync(container, sessionId, userId, "Write to alice.smith@example.com about it");
        Assert.Contains("alice.smith@example.com", stub.LastRequestJson);

        string optedIn = await SeedUserAsync(container, s => s.DlpMaskEmails = true);
        long optedInSession = await SeedSessionAsync(container, optedIn);
        await RunTurnAsync(container, optedInSession, optedIn, "Write to alice.smith@example.com about it");
        Assert.DoesNotContain("alice.smith@example.com", stub.LastRequestJson);
    }

    // ── the inbound half ────────────────────────────────────────────────────────

    [Fact]
    public async Task AReplyEchoingThePlaceholderReachesTheUserWithTheirOwnSecretRestored()
    {
        using var container = BuildContainer(Guid.NewGuid().ToString());
        string userId = await SeedUserAsync(container);
        long sessionId = await SeedSessionAsync(container, userId);
        var stub = container.GetRequiredService<StubAiProvider>();
        stub.ReplyChunks.Clear();
        stub.ReplyChunks.Add("The key [REDACTED_API_KEY_1] looks well-formed.");

        var events = await RunTurnAsync(container, sessionId, userId, $"Check {Secret}");

        string seen = VisibleText(events);
        Assert.Contains(Secret, seen);
        Assert.DoesNotContain("[REDACTED_", seen);
    }

    [Fact]
    public async Task APlaceholderSplitAcrossStreamedChunksIsStillRestored()
    {
        /* The case a per-chunk replace cannot handle. The provider decides where its chunk
           boundaries fall, so this is not a hypothetical shape. */
        using var container = BuildContainer(Guid.NewGuid().ToString());
        string userId = await SeedUserAsync(container);
        long sessionId = await SeedSessionAsync(container, userId);
        var stub = container.GetRequiredService<StubAiProvider>();
        stub.ReplyChunks.Clear();
        stub.ReplyChunks.Add("The key [REDA");
        stub.ReplyChunks.Add("CTED_API_");
        stub.ReplyChunks.Add("KEY_1] looks fine.");

        var events = await RunTurnAsync(container, sessionId, userId, $"Check {Secret}");

        string seen = VisibleText(events);
        Assert.Contains(Secret, seen);
        Assert.DoesNotContain("[REDACTED_", seen);
        Assert.Contains("looks fine.", seen);
    }

    [Fact]
    public async Task AReplyEndingMidWindowDoesNotLoseItsTail()
    {
        /* R-14. Without the flush the window keeps whatever trailing characters could still
           have become a token, so a reply ending in "[" or "[REDACTED" would silently lose its
           last few characters -- and only sometimes, which is the worst kind of bug to find
           later. */
        using var container = BuildContainer(Guid.NewGuid().ToString());
        string userId = await SeedUserAsync(container);
        long sessionId = await SeedSessionAsync(container, userId);
        var stub = container.GetRequiredService<StubAiProvider>();
        stub.ReplyChunks.Clear();
        stub.ReplyChunks.Add("See the note [REDACT");

        var events = await RunTurnAsync(container, sessionId, userId, $"Check {Secret}");

        Assert.Equal("See the note [REDACT", VisibleText(events));
    }

    [Fact]
    public async Task ThePersistedReplyIsWhatTheUserSawAndNotAPlaceholder()
    {
        using var container = BuildContainer(Guid.NewGuid().ToString());
        string userId = await SeedUserAsync(container);
        long sessionId = await SeedSessionAsync(container, userId);
        var stub = container.GetRequiredService<StubAiProvider>();
        stub.ReplyChunks.Clear();
        stub.ReplyChunks.Add("The key [REDACTED_API_KEY_1] looks well-formed.");

        await RunTurnAsync(container, sessionId, userId, $"Check {Secret}");

        using var scope = container.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var stored = await db.ChatMessage
            .Where(m => m.ChatSessionId == sessionId && m.Role == "assistant")
            .Select(m => m.Content)
            .FirstAsync(TestContext.Current.CancellationToken);

        Assert.Contains(Secret, stored!);
        Assert.DoesNotContain("[REDACTED_", stored!);
    }

    [Fact]
    public async Task AnOrdinaryTurnWithNoSecretIsUnchangedInBothDirections()
    {
        // The control: everything above asserts a substitution, and a scanner that matched
        // everything would satisfy several of them.
        using var container = BuildContainer(Guid.NewGuid().ToString());
        string userId = await SeedUserAsync(container);
        long sessionId = await SeedSessionAsync(container, userId);
        var stub = container.GetRequiredService<StubAiProvider>();
        stub.ReplyChunks.Clear();
        stub.ReplyChunks.Add("Wands of digging are found on most levels.");

        var events = await RunTurnAsync(container, sessionId, userId, "Where do I find a wand of digging?");

        Assert.Contains("Where do I find a wand of digging?", stub.LastRequestJson);
        Assert.DoesNotContain("[REDACTED_", stub.LastRequestJson);
        Assert.Equal("Wands of digging are found on most levels.", VisibleText(events));
    }
}
