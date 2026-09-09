using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
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
/// A whole incognito turn driven through <see cref="ChatService"/>, asserting the guarantee the
/// mode actually makes: nothing reaches the database and nothing reaches the disk.
/// </summary>
/// <remarks>
/// The provider and the HTTP layer are stubbed, so the turn completes without a network call.
/// That matters for more than speed: it is what lets the assistant message be persisted, which
/// is the second of the two write sites and the one an early provider failure would never
/// reach.
/// </remarks>
public class EphemeralTurnTests : IDisposable
{
    private const string ProviderName = "StubProvider";
    private const string ModelId = "stub-model-1";

    private readonly string _dataDirectory =
        Path.Combine(Path.GetTempPath(), "overseer-ephemeral-" + Guid.NewGuid().ToString("N"));

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
        public const string ReplyText = "A stub answer.";

        public string ProviderName => EphemeralTurnTests.ProviderName;
        public IReadOnlyList<string> SupportedServiceTiers => new[] { "default" };

        /// <summary>Every message the runner handed the provider, so a test can look at it.</summary>
        public List<object> LastHistory { get; private set; } = new();

        public void AppendAssistantToolCallsToHistory(List<object> messageHistory, string iterationText, List<JsonElement> toolCalls, List<JsonElement>? providerHistoryItems = null)
            => messageHistory.Add(new { role = "assistant", content = iterationText });

        public void AppendToolResultsToHistory(List<object> messageHistory, List<ProviderToolResult> results)
        {
            foreach (var r in results) messageHistory.Add(new { role = "tool", content = r.Content });
        }

        public Dictionary<string, object> BuildChatRequestBody(string modelId, List<object> messageHistory, int? maxOutputTokens, string? thinkingLevel, ToolsForRequest requestTools, string? reasoningMode = null, string? reasoningSummary = null, string? serviceTier = null, bool? parallelToolCalls = null, SegmentedPrompt? segmentedPrompt = null, string? promptCacheKey = null)
        {
            LastHistory = new List<object>(messageHistory);
            return new Dictionary<string, object> { { "model", modelId } };
        }

        public bool TryRewriteToolResult(List<object> messageHistory, string toolCallId, string replacementText) => false;
        public object BuildFunctionDeclaration(string name, string description, object parameterSchema) => new { name };
        public object? BuildToolsPayload(List<object> providerTools, List<object> functionDeclarations) => null;
        public object? BuildWebSearchTool() => null;
        public void ConfigureRequest(HttpRequestMessage request, string apiKey, AiEndpointDescriptor endpoint) { }
        public object FormatMessage(string role, string text, List<SendMessageAttachment>? imageAttachments)
            => new { role, content = text, images = imageAttachments?.Count ?? 0 };
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

    private ServiceProvider BuildContainer(string databaseName)
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "AesEncryptionKey", Convert.ToBase64String(new byte[32]) },
                { "ConversationsDataLocation", _dataDirectory },
                { "PrivacySettings:Ephemeral:TimeoutMinutes", "60" },
                // No keyring at all, deliberately: an incognito chat has no data at rest to
                // protect, so it must work on a deployment that has never configured one.
                { "PrivacySettings:ActiveKeyVersion", null }
            })
            .Build();

        services.AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(databaseName));
        services.AddSingleton<IConfiguration>(config);
        services.AddSingleton<IHttpClientFactory, StubHttpClientFactory>();
        // Explicit because the stub factory replaces AddHttpClient(), which is what normally
        // brings logging in -- and SignalR's hub lifetime manager needs an ILogger.
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
        return services.BuildServiceProvider();
    }

    private static async Task<string> SeedUserAsync(ServiceProvider container)
    {
        string userId = Guid.NewGuid().ToString();
        using var scope = container.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var crypto = scope.ServiceProvider.GetRequiredService<CryptoService>();

        db.Users.Add(new ApplicationUser { Id = userId, UserName = "incognito-tester" });
        db.UserAiSettings.Add(new UserAiSettings { AspNetUserId = userId, EnableToolUse = false });

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

    private static ChatSession Template(string userId) => new()
    {
        AspNetUserId = userId,
        Title = "Incognito chat",
        CreatedUtc = DateTime.UtcNow,
        LastMessageUtc = DateTime.UtcNow,
        IsConfidential = true
    };

    private static List<SendMessageAttachment> OneTextAttachment() => new()
    {
        new SendMessageAttachment
        {
            FileName = "budget.txt",
            ContentType = "text/plain",
            Base64Data = Convert.ToBase64String(Encoding.UTF8.GetBytes("salary: 12345"))
        }
    };

    [Fact]
    public async Task AFullEphemeralTurnWithAnAttachmentWritesNoRowAndNoFile()
    {
        string databaseName = Guid.NewGuid().ToString();
        using var container = BuildContainer(databaseName);
        string userId = await SeedUserAsync(container);
        var store = container.GetRequiredService<EphemeralSessionStore>();
        var held = store.Create(userId, Template(userId));

        var chatService = container.CreateScope().ServiceProvider.GetRequiredService<ChatService>();
        var events = new List<ChatEvent>();
        await foreach (var evt in chatService.StreamMessageAsync(
            held.Ref, "What does the attached file say?", OneTextAttachment(), userId, false,
            TestContext.Current.CancellationToken))
        {
            events.Add(evt);
        }

        Assert.DoesNotContain(events, e => e.Type == "error");
        Assert.Contains(events, e => e.Type == "user_message_created");

        // The four chat tables the mode promises to leave alone.
        using var verifyScope = container.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Empty(await db.ChatSession.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await db.ChatMessage.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await db.ChatMessageToolCall.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await db.ChatMessageAttachment.ToListAsync(TestContext.Current.CancellationToken));

        // And nothing under ConversationsDataLocation -- not even the per-session directory the
        // persisted path creates before it writes.
        Assert.False(Directory.Exists(_dataDirectory) && Directory.GetFileSystemEntries(_dataDirectory).Length > 0);
    }

    [Fact]
    public async Task TheTurnIsStillFullyRecordedInTheStore()
    {
        // The other half of the guarantee: nothing persisted must not mean nothing works.
        string databaseName = Guid.NewGuid().ToString();
        using var container = BuildContainer(databaseName);
        string userId = await SeedUserAsync(container);
        var store = container.GetRequiredService<EphemeralSessionStore>();
        var held = store.Create(userId, Template(userId));

        var chatService = container.CreateScope().ServiceProvider.GetRequiredService<ChatService>();
        await foreach (var _ in chatService.StreamMessageAsync(
            held.Ref, "What does the attached file say?", OneTextAttachment(), userId, false,
            TestContext.Current.CancellationToken))
        {
        }

        var messages = held.Messages;
        Assert.Equal(2, messages.Count);
        var user = Assert.Single(messages, m => m.Role == "user");
        var assistant = Assert.Single(messages, m => m.Role == "assistant");

        // Plaintext in the store, and that is the deliberate choice: envelope encryption
        // protects data at rest, and there is no rest here. This container has no keyring
        // configured at all, so encrypting would have failed outright.
        Assert.Contains("What does the attached file say?", user.Content);
        Assert.DoesNotContain(ContentProtectionService.RowPrefix, user.Content);
        Assert.Contains(StubAiProvider.ReplyText, assistant.Content);
        Assert.DoesNotContain(ContentProtectionService.RowPrefix, assistant.Content);

        var attachment = Assert.Single(held.Attachments);
        Assert.Equal("budget.txt", attachment.FileName);
        Assert.Equal(user.Id, attachment.ChatMessageId);
        Assert.Equal("salary: 12345", Encoding.UTF8.GetString(attachment.Bytes));
    }

    [Fact]
    public async Task ClosingTheSessionAfterATurnErasesWhatTheTurnProduced()
    {
        string databaseName = Guid.NewGuid().ToString();
        using var container = BuildContainer(databaseName);
        string userId = await SeedUserAsync(container);
        var store = container.GetRequiredService<EphemeralSessionStore>();
        var held = store.Create(userId, Template(userId));

        var chatService = container.CreateScope().ServiceProvider.GetRequiredService<ChatService>();
        await foreach (var _ in chatService.StreamMessageAsync(
            held.Ref, "Remember this secret", OneTextAttachment(), userId, false,
            TestContext.Current.CancellationToken))
        {
        }

        var attachmentBuffer = Assert.Single(held.Attachments).Bytes;
        Assert.True(store.Close(held.Ref, userId));

        Assert.Empty(held.Messages);
        Assert.Empty(held.Attachments);
        Assert.All(attachmentBuffer, b => Assert.Equal(0, b));
        Assert.Null(store.Get(held.Ref, userId));
    }

    [Fact]
    public async Task ASecondTurnSeesTheFirstOneInItsHistory()
    {
        string databaseName = Guid.NewGuid().ToString();
        using var container = BuildContainer(databaseName);
        string userId = await SeedUserAsync(container);
        var store = container.GetRequiredService<EphemeralSessionStore>();
        var stub = container.GetRequiredService<StubAiProvider>();
        var held = store.Create(userId, Template(userId));

        var chatService = container.CreateScope().ServiceProvider.GetRequiredService<ChatService>();
        await foreach (var _ in chatService.StreamMessageAsync(
            held.Ref, "First question", null, userId, false, TestContext.Current.CancellationToken))
        {
        }
        await foreach (var _ in chatService.StreamMessageAsync(
            held.Ref, "Second question", null, userId, false, TestContext.Current.CancellationToken))
        {
        }

        // Replayed from RAM rather than from ChatMessage, so the conversation has to have
        // continuity without a single row behind it.
        string sent = JsonSerializer.Serialize(stub.LastHistory);
        Assert.Contains("First question", sent);
        Assert.Contains(StubAiProvider.ReplyText, sent);
        Assert.Contains("Second question", sent);
        Assert.Equal(4, held.MessageCount);
    }

    [Fact]
    public async Task AClosedSessionsReferenceIsRefusedRatherThanTreatedAsMissing()
    {
        string databaseName = Guid.NewGuid().ToString();
        using var container = BuildContainer(databaseName);
        string userId = await SeedUserAsync(container);
        var store = container.GetRequiredService<EphemeralSessionStore>();
        var held = store.Create(userId, Template(userId));
        var reference = held.Ref;
        store.Close(reference, userId);

        var chatService = container.CreateScope().ServiceProvider.GetRequiredService<ChatService>();
        var events = new List<ChatEvent>();
        await foreach (var evt in chatService.StreamMessageAsync(
            reference, "Still there?", null, userId, false, TestContext.Current.CancellationToken))
        {
            events.Add(evt);
        }

        var error = Assert.Single(events, e => e.Type == "error");
        // Named as its own outcome: "session not found" reads as a bug to somebody looking at
        // the chat they just had.
        Assert.Contains("incognito", error.Data, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("expired", error.Data, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnotherUsersReferenceGetsNothing()
    {
        string databaseName = Guid.NewGuid().ToString();
        using var container = BuildContainer(databaseName);
        string owner = await SeedUserAsync(container);
        string intruder = await SeedUserAsync(container);
        var store = container.GetRequiredService<EphemeralSessionStore>();
        var held = store.Create(owner, Template(owner));

        var chatService = container.CreateScope().ServiceProvider.GetRequiredService<ChatService>();
        var events = new List<ChatEvent>();
        await foreach (var evt in chatService.StreamMessageAsync(
            held.Ref, "Let me read that", null, intruder, false, TestContext.Current.CancellationToken))
        {
            events.Add(evt);
        }

        Assert.Contains(events, e => e.Type == "error");
        Assert.Equal(0, held.MessageCount);
    }

    [Fact]
    public async Task APersistedTurnStillWritesItsRows()
    {
        // The control. Everything above asserts an absence, which a broken write path would
        // also produce.
        string databaseName = Guid.NewGuid().ToString();
        using var container = BuildContainer(databaseName);
        string userId = await SeedUserAsync(container);

        long sessionId;
        using (var seedScope = container.CreateScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var session = new ChatSession
            {
                AspNetUserId = userId,
                Title = "Saved chat",
                CreatedUtc = DateTime.UtcNow,
                LastMessageUtc = DateTime.UtcNow
            };
            db.ChatSession.Add(session);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            sessionId = session.Id;
        }

        var chatService = container.CreateScope().ServiceProvider.GetRequiredService<ChatService>();
        await foreach (var _ in chatService.StreamMessageAsync(
            SessionRef.Persistent(sessionId), "What does the attached file say?", OneTextAttachment(),
            userId, false, TestContext.Current.CancellationToken))
        {
        }

        using var verifyScope = container.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var rows = await verifyDb.ChatMessage
            .Where(m => m.ChatSessionId == sessionId)
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, rows.Count);
        Assert.Single(await verifyDb.ChatMessageAttachment.ToListAsync(TestContext.Current.CancellationToken));
        Assert.True(Directory.Exists(Path.Combine(_dataDirectory, sessionId.ToString())));
    }
}
