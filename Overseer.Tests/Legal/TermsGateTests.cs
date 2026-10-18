using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GnollHackServer.Data.Legal;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MobileGnollHackLogger.Data;
using Overseer.Controllers;
using Overseer.Security;
using Overseer.Services;
using Overseer.Services.Privacy;
using Overseer.Services.Providers;
using Overseer.Services.Security;
using Overseer.Services.Tools;
using Xunit;

namespace Overseer.Tests.Legal;

/// <summary>
/// Enforcement of the Gnoll Overseer Terms: the filter on the chat's send, and the check inside the chat turn
/// that covers both ways a turn starts.
/// </summary>
public class TermsGateTests : IDisposable
{
    private const string ProviderName = "StubProvider";
    private const string ModelId = "stub-model-1";

    private readonly TempWiki _wiki = new();
    private readonly string _dataDirectory = Path.Combine(Path.GetTempPath(), "overseer-terms-gate-" + Guid.NewGuid().ToString("N"));

    public TermsGateTests()
    {
        _wiki.Write(LegalTestSupport.RealFirstLines);
    }

    public void Dispose()
    {
        _wiki.Dispose();
        if (Directory.Exists(_dataDirectory)) Directory.Delete(_dataDirectory, recursive: true);
    }

    private static object? Read(object value, string name) => value.GetType().GetProperty(name)!.GetValue(value);

    private void AddLegal(IServiceCollection services)
    {
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(LegalTestSupport.Options()));
        services.AddSingleton<LegalDocumentReader>();
        services.AddScoped<TermsAcceptanceStore>();
    }

    // ── RequireTermsAcceptedAttribute ───────────────────────────────────────────

    private ServiceProvider FilterServices(string databaseName, bool blocked = false, bool registerTerms = true)
    {
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(databaseName));
        services.AddSingleton<IConfiguration>(LegalTestSupport.Configuration(_wiki.Path));
        services.AddSingleton<IMemoryCache>(_ => LegalTestSupport.Cache());
        services.AddSingleton<OverseerAccessService>();
        if (registerTerms) AddLegal(services);
        var sp = services.BuildServiceProvider();

        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Users.Add(new ApplicationUser
        {
            Id = "filter-user",
            UserName = "filter-user",
            OverseerBlockedUtc = blocked ? DateTime.UtcNow : null,
            OverseerBlockReason = blocked ? "Abusive prompts" : null
        });
        db.SaveChanges();
        return sp;
    }

    /// <summary>Runs the filters in the order MVC runs them on ChatController.Send: by their Order.</summary>
    private static async Task<(ActionExecutingContext Context, bool NextCalled)> RunFiltersAsync(IServiceProvider requestServices, string? userId)
    {
        var httpContext = new DefaultHttpContext { User = LegalTestSupport.Principal(userId), RequestServices = requestServices };
        var actionContext = new ActionContext(httpContext, new Microsoft.AspNetCore.Routing.RouteData(), new ActionDescriptor());
        var context = new ActionExecutingContext(actionContext, new List<IFilterMetadata>(), new Dictionary<string, object?>(), controller: new object());

        var send = typeof(ChatController).GetMethod(nameof(ChatController.Send))!;
        var filters = send.GetCustomAttributes(inherit: true)
            .OfType<IAsyncActionFilter>()
            .OrderBy(f => f is IOrderedFilter o ? o.Order : 0)
            .ToList();

        bool nextCalled = false;
        foreach (var filter in filters)
        {
            bool reached = false;
            await filter.OnActionExecutionAsync(context, () =>
            {
                reached = true;
                return Task.FromResult(new ActionExecutedContext(actionContext, new List<IFilterMetadata>(), new object()));
            });
            if (!reached) return (context, false);
        }

        nextCalled = true;
        return (context, nextCalled);
    }

    [Fact]
    public async Task SendRefusesAnUnacceptedUserWith403TermsNotAccepted()
    {
        using var sp = FilterServices(Guid.NewGuid().ToString());

        var (context, nextCalled) = await RunFiltersAsync(sp, "filter-user");

        Assert.False(nextCalled);
        var result = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
        Assert.Equal("terms_not_accepted", Read(result.Value!, "error"));
        Assert.Equal("2026-10-18", Read(result.Value!, "requiredVersion"));
    }

    [Fact]
    public async Task ABlockedUserGetsOverseerBlockedFirst()
    {
        using var sp = FilterServices(Guid.NewGuid().ToString(), blocked: true);

        var (context, nextCalled) = await RunFiltersAsync(sp, "filter-user");

        Assert.False(nextCalled);
        var result = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal("overseer_blocked", Read(result.Value!, "code"));
    }

    [Fact]
    public async Task SendPassesForAnAcceptedUser()
    {
        using var sp = FilterServices(Guid.NewGuid().ToString());
        using (var scope = sp.CreateScope())
        {
            await LegalTestSupport.AddAcceptanceAsync(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(), "filter-user", "2026-10-18");
        }

        var (context, nextCalled) = await RunFiltersAsync(sp, "filter-user");

        Assert.True(nextCalled);
        Assert.Null(context.Result);
    }

    [Fact]
    public async Task SendPassesWhenNoTermsStoreIsRegistered()
    {
        using var sp = FilterServices(Guid.NewGuid().ToString(), registerTerms: false);

        var (_, nextCalled) = await RunFiltersAsync(sp, "filter-user");

        Assert.True(nextCalled);
    }

    [Fact]
    public void SendCarriesTheTermsFilterAfterTheAccessFilter()
    {
        var send = typeof(ChatController).GetMethod(nameof(ChatController.Send))!;
        var terms = send.GetCustomAttribute<RequireTermsAcceptedAttribute>();

        Assert.NotNull(terms);
        Assert.NotNull(send.GetCustomAttribute<RequireOverseerAccessAttribute>());
        Assert.True(terms!.Order > 0);
    }

    // ── ChatService turn ────────────────────────────────────────────────────────
    // The container follows OverseerBlockTests, with the Terms services registered as Program.cs does.

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private sealed class Handler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
                => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("{\"stub\":true}") });
        }

        public HttpClient CreateClient(string name) => new(new Handler());
    }

    private sealed class StubAiProvider : IAiProvider
    {
        private int _requestBodies;

        public int RequestBodies => Volatile.Read(ref _requestBodies);

        public string ProviderName => TermsGateTests.ProviderName;
        public IReadOnlyList<string> SupportedServiceTiers => new[] { "default" };

        public void AppendAssistantToolCallsToHistory(List<object> messageHistory, string iterationText, List<JsonElement> toolCalls, List<JsonElement>? providerHistoryItems = null)
            => messageHistory.Add(new { role = "assistant", content = iterationText });

        public void AppendToolResultsToHistory(List<object> messageHistory, List<ProviderToolResult> results)
        {
            foreach (var r in results) messageHistory.Add(new { role = "tool", content = r.Content });
        }

        public Dictionary<string, object> BuildChatRequestBody(string modelId, List<object> messageHistory, int? maxOutputTokens, string? thinkingLevel, ToolsForRequest requestTools, string? reasoningMode = null, string? reasoningSummary = null, string? serviceTier = null, bool? parallelToolCalls = null, SegmentedPrompt? segmentedPrompt = null, string? promptCacheKey = null, bool cacheConversationTail = true, bool disablePromptCache = false)
        {
            Interlocked.Increment(ref _requestBodies);
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
            yield return new ChatEvent { Type = "chunk", Data = "A stub answer." };
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

    private ServiceProvider ChatContainer(string databaseName, bool registerTerms = true)
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "AesEncryptionKey", Convert.ToBase64String(new byte[32]) },
                { "ConversationsDataLocation", _dataDirectory },
                { "PrivacySettings:Ephemeral:TimeoutMinutes", "60" },
                { "PrivacySettings:ActiveKeyVersion", null },
                { "WikiPath", _wiki.Path }
            })
            .Build();

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
        services.AddSingleton<OverseerAccessService>();
        if (registerTerms) AddLegal(services);
        return services.BuildServiceProvider();
    }

    private static async Task<string> SeedChatUserAsync(ServiceProvider container, bool accepted)
    {
        string userId = Guid.NewGuid().ToString();
        using var scope = container.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var crypto = scope.ServiceProvider.GetRequiredService<CryptoService>();

        db.Users.Add(new ApplicationUser { Id = userId, UserName = "terms-tester" });
        db.UserAiSettings.Add(new UserAiSettings { AspNetUserId = userId, EnableToolUse = false });
        var (ciphertext, nonce, tag) = crypto.Encrypt("stub-api-key", userId);
        db.UserAiApiKeys.Add(new UserAiApiKey { AspNetUserId = userId, Provider = ProviderName, EncryptedApiKey = ciphertext, ApiKeyNonce = nonce, ApiKeyTag = tag });
        db.UserAiModels.Add(new UserAiModel { AspNetUserId = userId, Provider = ProviderName, ModelId = ModelId, OrderIndex = 0 });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        if (accepted)
        {
            await LegalTestSupport.AddAcceptanceAsync(db, userId, "2026-10-18");
        }
        return userId;
    }

    private static ChatSession Template(string userId) => new()
    {
        AspNetUserId = userId,
        Title = "Terms test chat",
        CreatedUtc = DateTime.UtcNow,
        LastMessageUtc = DateTime.UtcNow,
        IsConfidential = true
    };

    private static async Task<List<ChatEvent>> RunTurnAsync(ServiceProvider container, SessionRef sessionRef, string userId)
    {
        using var scope = container.CreateScope();
        var ongoing = scope.ServiceProvider.GetRequiredService<OngoingChatManager>();
        var chatService = scope.ServiceProvider.GetRequiredService<ChatService>();
        using var cts = new CancellationTokenSource();
        Assert.True(ongoing.TryStart(sessionRef, cts, out var state));

        await chatService.GenerateAndBroadcastMessageAsync(sessionRef, "What should I do next?", null, userId, false, TestContext.Current.CancellationToken);

        return state.AccumulatedEvents.ToList();
    }

    [Fact]
    public async Task AnUnacceptedUserGetsTermsNotAcceptedAndNoProviderCall()
    {
        using var container = ChatContainer(Guid.NewGuid().ToString());
        string userId = await SeedChatUserAsync(container, accepted: false);
        var stub = container.GetRequiredService<StubAiProvider>();
        var held = container.GetRequiredService<EphemeralSessionStore>().Create(userId, Template(userId));

        var events = await RunTurnAsync(container, held.Ref, userId);

        var error = Assert.Single(events, e => e.Type == "error");
        Assert.Equal("terms_not_accepted", error.ErrorCode);
        Assert.Equal(RequireTermsAcceptedAttribute.NotAcceptedMessage, error.Data);
        Assert.DoesNotContain(events, e => e.Type == "chunk");
        Assert.Contains(events, e => e.Type == "done");
        Assert.Equal(0, stub.RequestBodies);
        Assert.Equal(0, held.MessageCount);
    }

    [Fact]
    public async Task AnAcceptedUserReachesTheProvider()
    {
        using var container = ChatContainer(Guid.NewGuid().ToString());
        string userId = await SeedChatUserAsync(container, accepted: true);
        var stub = container.GetRequiredService<StubAiProvider>();
        var held = container.GetRequiredService<EphemeralSessionStore>().Create(userId, Template(userId));

        var events = await RunTurnAsync(container, held.Ref, userId);

        Assert.DoesNotContain(events, e => e.ErrorCode == "terms_not_accepted");
        Assert.True(stub.RequestBodies > 0);
    }

    [Fact]
    public async Task WithoutTheTermsServicesThereIsNoGate()
    {
        using var container = ChatContainer(Guid.NewGuid().ToString(), registerTerms: false);
        string userId = await SeedChatUserAsync(container, accepted: false);
        var stub = container.GetRequiredService<StubAiProvider>();
        var held = container.GetRequiredService<EphemeralSessionStore>().Create(userId, Template(userId));

        var events = await RunTurnAsync(container, held.Ref, userId);

        Assert.DoesNotContain(events, e => e.ErrorCode == "terms_not_accepted");
        Assert.True(stub.RequestBodies > 0);
    }
}
