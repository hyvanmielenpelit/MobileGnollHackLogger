using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MobileGnollHackLogger.Data;
using Overseer.Controllers;
using Overseer.Services;
using Overseer.Services.Agents;
using Overseer.Services.Privacy;
using Overseer.Services.Providers;
using Overseer.Services.Tools;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// A chat turn's notice through <see cref="ChatService"/>: the two message columns, the event rows, the
/// last <c>turn_notice</c> event, and the cancel-cause decision. No network.
/// </summary>
public sealed class ChatTurnLimitPersistenceTests : IDisposable
{
    private const string ProviderName = "StubProvider";
    private const string ModelId = "stub-model-1";
    private const string AdminName = "the-admin";
    private const string Marker = "SECRET-MARKER-IN-PROVIDER-BODY";

    private readonly string _dataDirectory =
        Path.Combine(Path.GetTempPath(), "overseer-turn-limit-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dataDirectory)) Directory.Delete(_dataDirectory, recursive: true);
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("{\"stub\":true}") });
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new StubHttpMessageHandler());
    }

    private sealed class ScriptedAiProvider : IAiProvider
    {
        public Func<CancellationToken, IReadOnlyList<ChatEvent>> Script { get; set; } = _ => Array.Empty<ChatEvent>();
        public Func<CancellationToken, Task>? AfterEvents { get; set; }
        public List<List<object>> Histories { get; } = new();

        public string ProviderName => ChatTurnLimitPersistenceTests.ProviderName;
        public IReadOnlyList<string> SupportedServiceTiers => new[] { "default" };

        public void AppendAssistantToolCallsToHistory(List<object> messageHistory, string iterationText, List<JsonElement> toolCalls, List<JsonElement>? providerHistoryItems = null)
            => messageHistory.Add(new { role = "assistant", content = iterationText });

        public void AppendToolResultsToHistory(List<object> messageHistory, List<ProviderToolResult> results)
        {
            foreach (var r in results) messageHistory.Add(new { role = "tool", content = r.Content });
        }

        public Dictionary<string, object> BuildChatRequestBody(string modelId, List<object> messageHistory, int? maxOutputTokens, string? thinkingLevel, ToolsForRequest requestTools, string? reasoningMode = null, string? reasoningSummary = null, string? serviceTier = null, bool? parallelToolCalls = null, SegmentedPrompt? segmentedPrompt = null, string? promptCacheKey = null, bool cacheConversationTail = true, bool disablePromptCache = false)
        {
            Histories.Add(new List<object>(messageHistory));
            return new() { { "model", modelId } };
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

        public async IAsyncEnumerable<ChatEvent> ParseStreamAsync(HttpResponseMessage response, bool showDebugLog, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var evt in Script(cancellationToken))
            {
                yield return evt;
            }
            if (AfterEvents != null)
            {
                await AfterEvents(cancellationToken);
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

    private ServiceProvider BuildContainer(ScriptedAiProvider provider)
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "AesEncryptionKey", Convert.ToBase64String(new byte[32]) },
                { "ConversationsDataLocation", _dataDirectory },
                { "PrivacySettings:Ephemeral:TimeoutMinutes", "60" },
                { "PrivacySettings:ActiveKeyVersion", null },
                { "Admins", AdminName }
            })
            .Build();

        string databaseName = Guid.NewGuid().ToString();
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
        services.AddSingleton<OngoingChatManager>();
        services.AddSingleton<IAiProvider>(provider);
        services.AddScoped<AgentLoopRunner>();
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
        services.AddSingleton<Overseer.Services.Rag.IEmbeddingService, Overseer.Services.Rag.LocalOnnxEmbeddingService>();
        services.AddSingleton<Overseer.Services.Rag.DocumentRagService>();
        services.AddScoped<Overseer.Services.Rag.RagSidecarStore>();
        services.AddSingleton(sp => new EphemeralSessionStore(sp.GetRequiredService<IConfiguration>(), logger: null, startSweeper: false));
        services.AddScoped<ModelPricingService>();
        services.AddScoped<SystemAiConfigService>();
        services.AddScoped<ChatService>();
        return services.BuildServiceProvider();
    }

    private static async Task<(string UserId, long SessionId)> SeedAsync(ServiceProvider container, string userName = "regular-user")
    {
        string userId = Guid.NewGuid().ToString();
        using var scope = container.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var crypto = scope.ServiceProvider.GetRequiredService<CryptoService>();

        db.Users.Add(new ApplicationUser { Id = userId, UserName = userName });
        db.UserAiSettings.Add(new UserAiSettings { AspNetUserId = userId, EnableToolUse = false });

        var (ciphertext, nonce, tag) = crypto.Encrypt("stub-api-key", userId);
        db.UserAiApiKeys.Add(new UserAiApiKey
        {
            AspNetUserId = userId, Provider = ProviderName, EncryptedApiKey = ciphertext, ApiKeyNonce = nonce, ApiKeyTag = tag
        });
        db.UserAiModels.Add(new UserAiModel { AspNetUserId = userId, Provider = ProviderName, ModelId = ModelId, OrderIndex = 0 });

        var session = new ChatSession { AspNetUserId = userId, Title = "Saved chat", CreatedUtc = DateTime.UtcNow, LastMessageUtc = DateTime.UtcNow };
        db.ChatSession.Add(session);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return (userId, session.Id);
    }

    private static async Task<List<ChatEvent>> RunTurnAsync(ServiceProvider container, SessionRef sessionRef, string userId, CancellationToken? token = null)
    {
        var chatService = container.CreateScope().ServiceProvider.GetRequiredService<ChatService>();
        var events = new List<ChatEvent>();
        await foreach (var evt in chatService.StreamMessageAsync(
            sessionRef, "What is a cockatrice?", null, userId, false, token ?? TestContext.Current.CancellationToken))
        {
            events.Add(evt);
        }
        return events;
    }

    private static ChatEvent Completed()
    {
        var meta = new ProviderCallMeta();
        meta.MarkCompleted(ProviderCallMeta.Now());
        return new ChatEvent { Type = "call_meta", CallMeta = meta };
    }

    private static T Read<T>(ServiceProvider container, Func<ApplicationDbContext, T> read)
    {
        using var scope = container.CreateScope();
        return read(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    [Fact]
    public async Task AnEmptyReply_StoresEmptyContent_TheNoticeAndAnEventRow_AndEmitsTheNoticeLast()
    {
        var provider = new ScriptedAiProvider { Script = _ => new[] { Completed() } };
        using var container = BuildContainer(provider);
        var (userId, sessionId) = await SeedAsync(container);

        var events = await RunTurnAsync(container, SessionRef.Persistent(sessionId), userId);

        var stored = Read(container, db => db.ChatMessage.Single(m => m.Role == "assistant"));
        Assert.Equal(string.Empty, stored.Content);
        Assert.Equal(TurnLimitCodes.NoFinalAnswer, stored.TurnNoticeCode);
        Assert.Contains("\"severity\":\"error\"", stored.TurnNoticeFactsJson);

        var row = Read(container, db => db.ChatTurnLimitEvents.Single());
        Assert.Equal(TurnLimitCodes.NoFinalAnswer, row.Code);
        Assert.Equal((byte)1, row.Severity);
        Assert.Equal(userId, row.AspNetUserId);
        Assert.Equal(sessionId, row.ChatSessionId);
        Assert.Equal(stored.Id, row.AssistantMessageId);
        Assert.NotNull(row.UserMessageId);
        Assert.Equal(AiKeySource.UserKey, row.KeySource);

        Assert.DoesNotContain(events, e => e.Type == "error");
        Assert.Equal("turn_notice", events[^1].Type);
        using var notice = JsonDocument.Parse(events[^1].Data);
        Assert.Equal(TurnLimitCodes.NoFinalAnswer, notice.RootElement.GetProperty("code").GetString());
        Assert.Equal("error", notice.RootElement.GetProperty("severity").GetString());
        Assert.Equal(row.Id, notice.RootElement.GetProperty("reference").GetInt64());
        Assert.False(notice.RootElement.TryGetProperty("adminFacts", out _));
    }

    [Fact]
    public async Task ANormalTurn_HasNoNoticeAndNoRow()
    {
        var provider = new ScriptedAiProvider
        {
            Script = _ => new[] { new ChatEvent { Type = "chunk", Data = "An answer." }, Completed() }
        };
        using var container = BuildContainer(provider);
        var (userId, sessionId) = await SeedAsync(container);

        var events = await RunTurnAsync(container, SessionRef.Persistent(sessionId), userId);

        Assert.DoesNotContain(events, e => e.Type == "turn_notice");
        Assert.Null(Read(container, db => db.ChatMessage.Single(m => m.Role == "assistant")).TurnNoticeCode);
        Assert.Equal(0, Read(container, db => db.ChatTurnLimitEvents.Count()));
    }

    [Fact]
    public async Task AProviderError_LeavesNoMarkerAnywhereStored_AndOnlyAnAdministratorSeesItLive()
    {
        foreach (var (userName, isAdmin) in new[] { ("regular-user", false), (AdminName, true) })
        {
            var provider = new ScriptedAiProvider
            {
                Script = _ => new[] { new ChatEvent { Type = "error", Data = $"bad {Marker}", Detail = Marker }, Completed() }
            };
            using var container = BuildContainer(provider);
            var (userId, sessionId) = await SeedAsync(container, userName);

            var events = await RunTurnAsync(container, SessionRef.Persistent(sessionId), userId);

            var stored = Read(container, db => db.ChatMessage.Single(m => m.Role == "assistant"));
            Assert.DoesNotContain(Marker, stored.Content);
            Assert.DoesNotContain(Marker, stored.TurnNoticeFactsJson);
            Assert.Equal(TurnLimitCodes.ProviderError, stored.TurnNoticeCode);

            var row = Read(container, db => db.ChatTurnLimitEvents.Single());
            Assert.DoesNotContain(Marker, JsonSerializer.Serialize(TextFields(row)));

            foreach (var evt in events.Where(e => e.Type != "turn_notice"))
            {
                Assert.DoesNotContain(Marker, evt.Data ?? "");
                Assert.DoesNotContain(Marker, evt.Detail ?? "");
            }

            var notice = Assert.Single(events, e => e.Type == "turn_notice");
            Assert.Equal(isAdmin, notice.Data.Contains(Marker));
        }
    }

    [Fact]
    public async Task HistoryReplay_SkipsAnEmptyAssistantRow()
    {
        var provider = new ScriptedAiProvider
        {
            Script = _ => new[] { new ChatEvent { Type = "chunk", Data = "An answer." }, Completed() }
        };
        using var container = BuildContainer(provider);
        var (userId, sessionId) = await SeedAsync(container);
        using (var scope = container.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.ChatMessage.Add(new ChatMessage { ChatSessionId = sessionId, Role = "user", Content = "Earlier", TimestampUtc = DateTime.UtcNow.AddMinutes(-2) });
            db.ChatMessage.Add(new ChatMessage { ChatSessionId = sessionId, Role = "assistant", Content = "", TimestampUtc = DateTime.UtcNow.AddMinutes(-1) });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await RunTurnAsync(container, SessionRef.Persistent(sessionId), userId);

        var history = Assert.Single(provider.Histories);
        Assert.DoesNotContain(history, m => ProviderHelper.GetProperty(m, "role")?.ToString() == "assistant");
    }

    [Fact]
    public async Task TheTimerFiring_IsATimeLimitHit_WithThePartialTextSaved()
    {
        using var cts = new CancellationTokenSource();
        var provider = new ScriptedAiProvider
        {
            Script = _ => new[] { new ChatEvent { Type = "chunk", Data = "Partial" } },
            AfterEvents = async ct =>
            {
                cts.Cancel();
                await Task.Delay(Timeout.Infinite, ct);
            }
        };
        using var container = BuildContainer(provider);
        var (userId, sessionId) = await SeedAsync(container);
        var sessionRef = SessionRef.Persistent(sessionId);
        var manager = container.GetRequiredService<OngoingChatManager>();
        Assert.True(manager.TryStart(sessionRef, cts, out var state));
        state.DeadlineUtc = state.StartedAtUtc.AddSeconds(300);

        var events = await RunTurnAsync(container, sessionRef, userId, cts.Token);

        var stored = Read(container, db => db.ChatMessage.Single(m => m.Role == "assistant"));
        Assert.Contains("Partial", stored.Content);
        Assert.Equal(TurnLimitCodes.TimeLimit, stored.TurnNoticeCode);
        Assert.DoesNotContain("**Error:**", stored.Content);
        Assert.Equal(300, Read(container, db => db.ChatTurnLimitEvents.Single()).LimitValue);
        Assert.Equal(1, events.Count(e => e.Type == "turn_notice"));
        Assert.DoesNotContain(events, e => e.Type == "error");
    }

    [Fact]
    public async Task AUserCancel_IsNotALimit()
    {
        var provider = new ScriptedAiProvider();
        using var container = BuildContainer(provider);
        var (userId, sessionId) = await SeedAsync(container);
        var sessionRef = SessionRef.Persistent(sessionId);
        var manager = container.GetRequiredService<OngoingChatManager>();
        using var cts = new CancellationTokenSource();
        Assert.True(manager.TryStart(sessionRef, cts, out _));
        provider.Script = _ => new[] { new ChatEvent { Type = "chunk", Data = "Partial" } };
        provider.AfterEvents = async ct =>
        {
            manager.TryCancelAndRemove(sessionRef);
            await Task.Delay(Timeout.Infinite, ct);
        };

        var events = await RunTurnAsync(container, sessionRef, userId, cts.Token);

        Assert.DoesNotContain(events, e => e.Type == "turn_notice");
        Assert.Equal(0, Read(container, db => db.ChatTurnLimitEvents.Count()));
        Assert.Null(Read(container, db => db.ChatMessage.Single(m => m.Role == "assistant")).TurnNoticeCode);
    }

    [Fact]
    public async Task AnIncognitoTurnOnTheUsersOwnKey_ShowsTheNotice_AndWritesNoRow()
    {
        var provider = new ScriptedAiProvider { Script = _ => new[] { Completed() } };
        using var container = BuildContainer(provider);
        var (userId, _) = await SeedAsync(container);
        var store = container.GetRequiredService<EphemeralSessionStore>();
        var held = store.Create(userId, new ChatSession { AspNetUserId = userId, Title = "Incognito", CreatedUtc = DateTime.UtcNow, LastMessageUtc = DateTime.UtcNow });

        var events = await RunTurnAsync(container, held.Ref, userId);

        Assert.Single(events, e => e.Type == "turn_notice");
        Assert.Equal(0, Read(container, db => db.ChatTurnLimitEvents.Count()));
        Assert.Equal(TurnLimitCodes.NoFinalAnswer, held.Messages.Single(m => m.Role == "assistant").TurnNoticeCode);
    }

    private static object TextFields(ChatTurnLimitEvent row) => new
    {
        row.Code, row.ProviderErrorCode, row.ProviderFinishReason, row.AssistantPhase, row.Provider, row.RequestedModelId
    };
}
