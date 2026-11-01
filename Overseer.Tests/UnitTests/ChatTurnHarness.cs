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
/// A real <see cref="ChatService"/> over an in-memory database with a scripted provider that records every
/// history it receives. No network.
/// </summary>
public abstract class ChatTurnHarness : IDisposable
{
    protected const string ProviderName = "StubProvider";
    protected const string ModelId = "stub-model-1";
    protected const string AdminName = "the-admin";

    protected readonly string _dataDirectory =
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

    protected sealed class ScriptedAiProvider : IAiProvider
    {
        public Func<CancellationToken, IReadOnlyList<ChatEvent>> Script { get; set; } = _ => Array.Empty<ChatEvent>();
        public Func<CancellationToken, Task>? AfterEvents { get; set; }
        public List<List<object>> Histories { get; } = new();
        public Func<string, string, List<SendMessageAttachment>?, object>? Formatter { get; set; }

        public string ProviderName => ChatTurnHarness.ProviderName;
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
            => Formatter != null ? Formatter(role, text, imageAttachments) : new { role, content = text };
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

    protected ServiceProvider BuildContainer(ScriptedAiProvider provider, IDictionary<string, string?>? settings = null, Action<IServiceCollection>? configure = null)
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
            .AddInMemoryCollection(settings ?? new Dictionary<string, string?>())
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
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    protected static async Task<(string UserId, long SessionId)> SeedAsync(ServiceProvider container, string userName = "regular-user")
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

    protected static async Task<List<ChatEvent>> RunTurnAsync(ServiceProvider container, SessionRef sessionRef, string userId, CancellationToken? token = null, long? userModelId = null, long? systemModelId = null)
    {
        var chatService = container.CreateScope().ServiceProvider.GetRequiredService<ChatService>();
        var events = new List<ChatEvent>();
        await foreach (var evt in chatService.StreamMessageAsync(
            sessionRef, "What is a cockatrice?", null, userId, false, token ?? TestContext.Current.CancellationToken, userModelId, systemModelId))
        {
            events.Add(evt);
        }
        return events;
    }

    protected static ChatEvent Completed()
    {
        var meta = new ProviderCallMeta();
        meta.MarkCompleted(ProviderCallMeta.Now());
        return new ChatEvent { Type = "call_meta", CallMeta = meta };
    }

    protected static T Read<T>(ServiceProvider container, Func<ApplicationDbContext, T> read)
    {
        using var scope = container.CreateScope();
        return read(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }
}
