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
/// Spoiler-free mode and the response style driven through the real <see cref="ChatService"/>:
/// the account settings decide the system prompt, whatever an older GnollHack build put in the
/// session's client settings.
/// </summary>
/// <remarks>
/// The provider and the HTTP layer are stubbed, as in <see cref="DlpMaskingIntegrationTests"/>,
/// so a whole turn completes with no network call and the stub records the prompt it was given.
/// </remarks>
public class AccountPreferenceTurnTests : IDisposable
{
    private const string ProviderName = "StubProvider";
    private const string ModelId = "stub-model-1";

    private const string SpoilerFreeHeading = "SPOILER-FREE MODE IS ACTIVE";
    private const string VerboseHeading = "Response Style — Verbose";

    private readonly string _dataDirectory =
        Path.Combine(Path.GetTempPath(), "overseer-prefs-" + Guid.NewGuid().ToString("N"));

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
        public string ProviderName => AccountPreferenceTurnTests.ProviderName;
        public IReadOnlyList<string> SupportedServiceTiers => new[] { "default" };

        /// <summary>What the last request carried, serialized, so a test can search it.</summary>
        public string LastRequestJson { get; private set; } = "";

        /// <summary>The system prompt the last request was built from.</summary>
        public string? LastSystemPrompt { get; private set; }

        /// <summary>Chunks the stub streams back.</summary>
        public List<string> ReplyChunks { get; } = new() { "All done." };

        public void AppendAssistantToolCallsToHistory(List<object> messageHistory, string iterationText, List<JsonElement> toolCalls, List<JsonElement>? providerHistoryItems = null)
            => messageHistory.Add(new { role = "assistant", content = iterationText });

        public void AppendToolResultsToHistory(List<object> messageHistory, List<ProviderToolResult> results)
        {
            foreach (var r in results) messageHistory.Add(new { role = "tool", content = r.Content });
        }

        public Dictionary<string, object> BuildChatRequestBody(string modelId, List<object> messageHistory, int? maxOutputTokens, string? thinkingLevel, ToolsForRequest requestTools, string? reasoningMode = null, string? reasoningSummary = null, string? serviceTier = null, bool? parallelToolCalls = null, SegmentedPrompt? segmentedPrompt = null, string? promptCacheKey = null, bool cacheConversationTail = true, bool disablePromptCache = false)
        {
            LastRequestJson = JsonSerializer.Serialize(messageHistory);
            LastSystemPrompt = segmentedPrompt?.FullPrompt;
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
        services.AddSingleton<ConfidentialityPostureService>();
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

    private static async Task<string> SeedUserAsync(
        ServiceProvider container, Action<UserAiSettings>? configureSettings = null, bool withSettingsRow = true)
    {
        string userId = Guid.NewGuid().ToString();
        using var scope = container.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var crypto = scope.ServiceProvider.GetRequiredService<CryptoService>();

        db.Users.Add(new ApplicationUser { Id = userId, UserName = "prefs-tester" });

        if (withSettingsRow)
        {
            var aiSettings = new UserAiSettings { AspNetUserId = userId, EnableToolUse = false };
            configureSettings?.Invoke(aiSettings);
            db.UserAiSettings.Add(aiSettings);
        }

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

    private static async Task<long> SeedSessionAsync(ServiceProvider container, string userId, string? clientSettings)
    {
        using var scope = container.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var session = new ChatSession
        {
            AspNetUserId = userId,
            Title = "Preferences test chat",
            CreatedUtc = DateTime.UtcNow,
            LastMessageUtc = DateTime.UtcNow,
            ClientSettings = clientSettings
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

    /* Runs one turn and returns the system prompt the provider received, after checking the turn
       finished, so an infrastructure failure reads as one rather than as a wrong prompt. */
    private async Task<string> RunTurnForPromptAsync(
        Action<UserAiSettings>? configureSettings, string? clientSettings, bool withSettingsRow = true)
    {
        using var container = BuildContainer(Guid.NewGuid().ToString());
        string userId = await SeedUserAsync(container, configureSettings, withSettingsRow);
        long sessionId = await SeedSessionAsync(container, userId, clientSettings);
        var stub = container.GetRequiredService<StubAiProvider>();

        var events = await RunTurnAsync(container, sessionId, userId, "What is at the bottom of the Gnomish Mines?");

        var errors = events.Where(e => e.Type == "error").Select(e => e.Data).ToList();
        Assert.True(errors.Count == 0, "The turn reported an error: " + string.Join(" | ", errors));
        Assert.NotNull(stub.LastSystemPrompt);
        return stub.LastSystemPrompt!;
    }

    [Fact]
    public async Task AnOldAppsSpoilerDefaultDoesNotOverrideAnAccountThatAllowsSpoilers()
    {
        string prompt = await RunTurnForPromptAsync(
            s => s.SpoilerFreeMode = false, "{\"BoolData\":{\"allowSpoilers\":false}}");

        Assert.DoesNotContain(SpoilerFreeHeading, prompt);
    }

    [Fact]
    public async Task AnOldAppAllowingSpoilersDoesNotOverrideASpoilerFreeAccount()
    {
        string prompt = await RunTurnForPromptAsync(
            s => s.SpoilerFreeMode = true, "{\"BoolData\":{\"allowSpoilers\":true}}");

        Assert.Contains(SpoilerFreeHeading, prompt);
    }

    [Fact]
    public async Task AVerboseAccountGetsTheVerboseStyleWhateverTheAppSends()
    {
        string prompt = await RunTurnForPromptAsync(
            s => s.VerboseResponses = true, "{\"BoolData\":{\"verboseResponses\":false}}");

        Assert.Contains(VerboseHeading, prompt);
    }

    [Fact]
    public async Task AnOldAppAskingForVerboseDoesNotOverrideAConciseAccount()
    {
        string prompt = await RunTurnForPromptAsync(
            configureSettings: null, "{\"BoolData\":{\"verboseResponses\":true}}");

        Assert.DoesNotContain(VerboseHeading, prompt);
    }

    [Fact]
    public async Task AUserWithNoSettingsRowIsSpoilerFree()
    {
        string prompt = await RunTurnForPromptAsync(
            configureSettings: null, clientSettings: null, withSettingsRow: false);

        Assert.Contains(SpoilerFreeHeading, prompt);
    }
}
