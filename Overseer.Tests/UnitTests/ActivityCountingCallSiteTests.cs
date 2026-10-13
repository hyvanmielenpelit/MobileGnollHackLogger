using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MobileGnollHackLogger.Data;
using Overseer.Controllers;
using Overseer.Services;
using Overseer.Services.Privacy;
using Overseer.Services.Providers;
using Overseer.Services.Telemetry;
using Overseer.Services.Tools;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// Where Overseer counts its daily activity: chat creation in <see cref="ChatController"/> and
/// <see cref="SessionController"/>, and messages and replies in <see cref="ChatService"/>. Incognito chats and messages
/// count only on the anonymous row, never under the user.
/// </summary>
public sealed class ActivityCountingCallSiteTests : IDisposable
{
    private const string Owner = "activity-owner";

    private readonly string _dataDirectory =
        Path.Combine(Path.GetTempPath(), "overseer-activity-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dataDirectory)) Directory.Delete(_dataDirectory, recursive: true);
    }

    /// <summary>Records every call instead of writing a row.</summary>
    private sealed class RecordingActivityCounter : IActivityCounter
    {
        private readonly ConcurrentQueue<(string? UserId, ActivityDelta Delta)> _calls = new();

        public IReadOnlyList<(string? UserId, ActivityDelta Delta)> Calls => _calls.ToArray();

        public Task TryAddAsync(string? userId, ActivityDelta delta, DateTime utcNow)
        {
            _calls.Enqueue((userId, delta));
            return Task.CompletedTask;
        }
    }

    // --- ChatController ------------------------------------------------------------------------

    private IConfiguration ChatConfig() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        { "PrivacySettings:KeyRing:v1", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) },
        { "PrivacySettings:ActiveKeyVersion", "v1" },
        { "PrivacySettings:Ephemeral:TimeoutMinutes", "30" },
        { "ConversationsDataLocation", _dataDirectory },
        { "ChatRetentionSettings:MaxActiveSessionsPerUser", "50" },
        { "ChatRetentionSettings:SoftDeleteGracePeriodDays", "30" }
    }).Build();

    private static ApplicationDbContext NewDb() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private static ChatController CreateChatController(ApplicationDbContext db, IConfiguration config)
    {
        var protection = new ContentProtectionService(new ConfigurationContentKeyRing(config));
        var retention = new ChatRetentionService(db, config, NullLogger<ChatRetentionService>.Instance);

        return new ChatController(
            db, null!, config, null!, null!, null!, new OngoingChatManager(config), null!, null!,
            retention, null!, new AttachmentValidator(config),
            new ConfidentialityPostureService(config), new ConfidentialPolicyResolver(config), protection,
            new EphemeralSessionStore(config, startSweeper: false))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        new[] { new Claim(ClaimTypes.NameIdentifier, Owner) }, "TestAuth"))
                }
            }
        };
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SendCreatingAPersistedChat_CountsAChatForTheUser(bool confidential)
    {
        using var db = NewDb();
        var counter = new RecordingActivityCounter();
        var controller = CreateChatController(db, ChatConfig());

        var result = await controller.Send(new SendMessageRequest { Message = "Hello there", IsConfidential = confidential }, counter);

        Assert.IsType<OkObjectResult>(result);
        var call = Assert.Single(counter.Calls);
        Assert.Equal(Owner, call.UserId);
        Assert.Equal(new ActivityDelta(Chats: 1, ConfidentialChats: confidential ? 1 : 0), call.Delta);
    }

    [Fact]
    public async Task SendCreatingAnIncognitoChat_CountsOnlyOnTheAnonymousRow()
    {
        using var db = NewDb();
        var counter = new RecordingActivityCounter();
        var controller = CreateChatController(db, ChatConfig());

        var result = await controller.Send(
            new SendMessageRequest { Message = "Something transient", IsConfidential = true, IsEphemeral = true }, counter);

        Assert.IsType<OkObjectResult>(result);
        var call = Assert.Single(counter.Calls);
        Assert.Null(call.UserId);
        Assert.Equal(new ActivityDelta(IncognitoChats: 1), call.Delta);
        Assert.DoesNotContain(counter.Calls, c => c.UserId == Owner);
    }

    [Fact]
    public async Task SendToAnExistingChat_CountsNoChat()
    {
        using var db = NewDb();
        db.ChatSession.Add(new ChatSession
        {
            Id = 5, AspNetUserId = Owner, Title = "Existing", CreatedUtc = DateTime.UtcNow, LastMessageUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var counter = new RecordingActivityCounter();
        var controller = CreateChatController(db, ChatConfig());

        await controller.Send(new SendMessageRequest { Message = "Follow-up", SessionId = "5" }, counter);

        Assert.Empty(counter.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AttachSnapshotCreatingAPersistedChat_CountsAGnollHackChat(bool confidential)
    {
        using var db = NewDb();
        var counter = new RecordingActivityCounter();
        var controller = CreateChatController(db, ChatConfig());

        var result = await controller.AttachSnapshot(new AttachGameSnapshotRequest
        {
            SnapshotText = "Dungeon Level 1\nPlayer HP: 20/20",
            IsConfidential = confidential
        }, counter);

        Assert.IsType<OkObjectResult>(result);
        var call = Assert.Single(counter.Calls);
        Assert.Equal(Owner, call.UserId);
        Assert.Equal(new ActivityDelta(Chats: 1, ConfidentialChats: confidential ? 1 : 0, GnollHackChats: 1), call.Delta);
    }

    [Fact]
    public async Task AttachSnapshotCreatingAnIncognitoChat_CountsOnlyOnTheAnonymousRow()
    {
        using var db = NewDb();
        var counter = new RecordingActivityCounter();
        var controller = CreateChatController(db, ChatConfig());

        var result = await controller.AttachSnapshot(new AttachGameSnapshotRequest
        {
            SnapshotText = "Dungeon Level 1",
            IsConfidential = true,
            IsEphemeral = true
        }, counter);

        Assert.IsType<OkObjectResult>(result);
        var call = Assert.Single(counter.Calls);
        Assert.Null(call.UserId);
        Assert.Equal(new ActivityDelta(IncognitoChats: 1), call.Delta);
    }

    // --- SessionController ---------------------------------------------------------------------
    // Hand-written identity stubs, as in SessionControllerCreateTests: only finding the user and
    // checking the password are reached.

    private const string GameSecret = "test-antiforgery-token";
    private const string GameUserId = "game-user-1";

    private sealed class StubUserStore : IUserStore<ApplicationUser>
    {
        public ApplicationUser? User { get; set; }

        public Task<ApplicationUser?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken) => Task.FromResult(User);
        public Task<ApplicationUser?> FindByIdAsync(string userId, CancellationToken cancellationToken) => Task.FromResult(User);
        public Task<string> GetUserIdAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(user.Id);
        public Task<string?> GetUserNameAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(user.UserName);
        public Task<string?> GetNormalizedUserNameAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(user.UserName?.ToUpperInvariant());
        public Task SetNormalizedUserNameAsync(ApplicationUser user, string? normalizedName, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SetUserNameAsync(ApplicationUser user, string? userName, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IdentityResult> CreateAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(IdentityResult.Success);
        public Task<IdentityResult> UpdateAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(IdentityResult.Success);
        public Task<IdentityResult> DeleteAsync(ApplicationUser user, CancellationToken cancellationToken) => Task.FromResult(IdentityResult.Success);
        public void Dispose() { }
    }

    private sealed class StubUserManager : UserManager<ApplicationUser>
    {
        private readonly StubUserStore _store;

        public StubUserManager(StubUserStore store)
            : base(store, null!, null!, Array.Empty<IUserValidator<ApplicationUser>>(),
                Array.Empty<IPasswordValidator<ApplicationUser>>(), null!, null!, null!,
                NullLogger<UserManager<ApplicationUser>>.Instance)
        {
            _store = store;
        }

        public override Task<ApplicationUser?> FindByNameAsync(string userName) => Task.FromResult(_store.User);
    }

    private sealed class StubClaimsPrincipalFactory : IUserClaimsPrincipalFactory<ApplicationUser>
    {
        public Task<ClaimsPrincipal> CreateAsync(ApplicationUser user)
            => Task.FromResult(new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, user.Id) }, "TestAuth")));
    }

    private sealed class StubSignInManager : SignInManager<ApplicationUser>
    {
        public StubSignInManager(UserManager<ApplicationUser> userManager)
            : base(userManager, new HttpContextAccessor(), new StubClaimsPrincipalFactory(),
                Microsoft.Extensions.Options.Options.Create(new IdentityOptions()),
                NullLogger<SignInManager<ApplicationUser>>.Instance,
                null!, null!)
        {
        }

        public override Task<Microsoft.AspNetCore.Identity.SignInResult> CheckPasswordSignInAsync(
            ApplicationUser user, string password, bool lockoutOnFailure)
            => Task.FromResult(password == "correct-password"
                ? Microsoft.AspNetCore.Identity.SignInResult.Success
                : Microsoft.AspNetCore.Identity.SignInResult.Failed);
    }

    private IConfiguration SessionConfig() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        { "AntiForgeryToken", GameSecret },
        { "ConversationsDataLocation", _dataDirectory },
        { "PrivacySettings:KeyRing:v1", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) },
        { "PrivacySettings:ActiveKeyVersion", "v1" },
        { "ChatRetentionSettings:MaxActiveSessionsPerUser", "50" },
        { "ChatRetentionSettings:SoftDeleteGracePeriodDays", "30" }
    }).Build();

    private static SessionController CreateSessionController(ApplicationDbContext db, IConfiguration config)
    {
        var store = new StubUserStore { User = new ApplicationUser { Id = GameUserId, UserName = "gnollhacker" } };
        return new SessionController(
            new StubSignInManager(new StubUserManager(store)), db, new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 }), config,
            null!, new OngoingChatManager(config), new ChatRetentionService(db, config, NullLogger<ChatRetentionService>.Instance),
            new ConfidentialPolicyResolver(config), new ContentProtectionService(new ConfigurationContentKeyRing(config)),
            NullLogger<SessionController>.Instance);
    }

    private static CreateSessionRequest SessionRequest(bool gnollHack, bool confidential, string password = "correct-password") => new()
    {
        UserName = "gnollhacker",
        Password = password,
        AntiForgeryToken = GameSecret,
        SnapshotHtml = "<p>Dungeon Level 3</p>",
        MessageHistory = "You hit the newt.",
        DirectoryManifest = "save\t1024",
        IsGnollHackSession = gnollHack,
        IsConfidential = confidential
    };

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task SessionCreate_CountsAChatForTheGameUser(bool gnollHack, bool confidential)
    {
        using var db = NewDb();
        var counter = new RecordingActivityCounter();
        var controller = CreateSessionController(db, SessionConfig());

        var result = await controller.Create(SessionRequest(gnollHack, confidential), activityCounter: counter);

        Assert.IsType<OkObjectResult>(result);
        var call = Assert.Single(counter.Calls);
        Assert.Equal(GameUserId, call.UserId);
        Assert.Equal(new ActivityDelta(Chats: 1, ConfidentialChats: confidential ? 1 : 0, GnollHackChats: gnollHack ? 1 : 0), call.Delta);
    }

    [Fact]
    public async Task SessionCreateWithAWrongPassword_CountsNothing()
    {
        using var db = NewDb();
        var counter = new RecordingActivityCounter();
        var controller = CreateSessionController(db, SessionConfig());

        var result = await controller.Create(SessionRequest(true, false, password: "wrong"), activityCounter: counter);

        Assert.IsType<UnauthorizedResult>(result);
        Assert.Empty(counter.Calls);
    }

    // --- ChatService ---------------------------------------------------------------------------
    // The container and stubs of EphemeralTurnTests, with the recording counter registered: the
    // turn completes against a stub provider without a network call.

    private const string ProviderName = "StubProvider";
    private const string ModelId = "stub-model-1";

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("{\"stub\":true}") });
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new StubHttpMessageHandler());
    }

    private sealed class StubAiProvider : IAiProvider
    {
        public const string ReplyText = "A stub answer.";

        public string ProviderName => ActivityCountingCallSiteTests.ProviderName;
        public IReadOnlyList<string> SupportedServiceTiers => new[] { "default" };

        public void AppendAssistantToolCallsToHistory(List<object> messageHistory, string iterationText, List<JsonElement> toolCalls, List<JsonElement>? providerHistoryItems = null)
            => messageHistory.Add(new { role = "assistant", content = iterationText });

        public void AppendToolResultsToHistory(List<object> messageHistory, List<ProviderToolResult> results)
        {
            foreach (var r in results) messageHistory.Add(new { role = "tool", content = r.Content });
        }

        public Dictionary<string, object> BuildChatRequestBody(string modelId, List<object> messageHistory, int? maxOutputTokens, string? thinkingLevel, ToolsForRequest requestTools, string? reasoningMode = null, string? reasoningSummary = null, string? serviceTier = null, bool? parallelToolCalls = null, SegmentedPrompt? segmentedPrompt = null, string? promptCacheKey = null, bool cacheConversationTail = true, bool disablePromptCache = false)
            => new() { { "model", modelId } };

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

    private ServiceProvider BuildContainer(RecordingActivityCounter counter)
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
        services.AddSingleton<IActivityCounter>(counter);
        services.AddScoped<ChatService>();
        return services.BuildServiceProvider();
    }

    private static async Task<string> SeedUserAsync(ServiceProvider container)
    {
        string userId = Guid.NewGuid().ToString();
        using var scope = container.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var crypto = scope.ServiceProvider.GetRequiredService<CryptoService>();

        db.Users.Add(new ApplicationUser { Id = userId, UserName = "activity-tester" });
        db.UserAiSettings.Add(new UserAiSettings { AspNetUserId = userId, EnableToolUse = false });

        var (ciphertext, nonce, tag) = crypto.Encrypt("stub-api-key", userId);
        db.UserAiApiKeys.Add(new UserAiApiKey
        {
            AspNetUserId = userId, Provider = ProviderName, EncryptedApiKey = ciphertext, ApiKeyNonce = nonce, ApiKeyTag = tag
        });
        db.UserAiModels.Add(new UserAiModel { AspNetUserId = userId, Provider = ProviderName, ModelId = ModelId, OrderIndex = 0 });

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return userId;
    }

    private static async Task<long> SeedSessionAsync(ServiceProvider container, string userId)
    {
        using var scope = container.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var session = new ChatSession
        {
            AspNetUserId = userId, Title = "Saved chat", CreatedUtc = DateTime.UtcNow, LastMessageUtc = DateTime.UtcNow
        };
        db.ChatSession.Add(session);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return session.Id;
    }

    private static async Task<List<ChatEvent>> RunTurnAsync(ServiceProvider container, SessionRef sessionRef, string userId, bool isHidden)
    {
        var chatService = container.CreateScope().ServiceProvider.GetRequiredService<ChatService>();
        var events = new List<ChatEvent>();
        await foreach (var evt in chatService.StreamMessageAsync(
            sessionRef, "What is a cockatrice?", null, userId, isHidden, TestContext.Current.CancellationToken))
        {
            events.Add(evt);
        }
        return events;
    }

    [Fact]
    public async Task APersistedTurn_CountsTheUserMessageAndTheSavedReply()
    {
        var counter = new RecordingActivityCounter();
        using var container = BuildContainer(counter);
        string userId = await SeedUserAsync(container);
        long sessionId = await SeedSessionAsync(container, userId);

        var events = await RunTurnAsync(container, SessionRef.Persistent(sessionId), userId, isHidden: false);

        Assert.DoesNotContain(events, e => e.Type == "error");
        Assert.Single(counter.Calls, c => c.UserId == userId && c.Delta == new ActivityDelta(UserMessages: 1));
        Assert.Single(counter.Calls, c => c.UserId == userId && c.Delta == new ActivityDelta(AssistantMessages: 1));
        Assert.DoesNotContain(counter.Calls, c => c.UserId == null);
    }

    [Fact]
    public async Task AnIncognitoTurn_CountsTheMessageOnlyOnTheAnonymousRow_AndNoReply()
    {
        var counter = new RecordingActivityCounter();
        using var container = BuildContainer(counter);
        string userId = await SeedUserAsync(container);
        var store = container.GetRequiredService<EphemeralSessionStore>();
        var held = store.Create(userId, new ChatSession
        {
            AspNetUserId = userId, Title = "Incognito chat", CreatedUtc = DateTime.UtcNow, LastMessageUtc = DateTime.UtcNow, IsConfidential = true
        });

        var events = await RunTurnAsync(container, held.Ref, userId, isHidden: false);

        Assert.DoesNotContain(events, e => e.Type == "error");
        var call = Assert.Single(counter.Calls);
        Assert.Null(call.UserId);
        Assert.Equal(new ActivityDelta(IncognitoUserMessages: 1), call.Delta);
        // The reply was produced, and it is still not counted.
        Assert.Equal(2, held.MessageCount);
        Assert.DoesNotContain(counter.Calls, c => c.UserId == userId);
    }

    [Fact]
    public async Task AHiddenMessage_CountsNoUserMessage()
    {
        var counter = new RecordingActivityCounter();
        using var container = BuildContainer(counter);
        string userId = await SeedUserAsync(container);
        long sessionId = await SeedSessionAsync(container, userId);

        // The GnollHack client's initial prompt is sent hidden.
        var events = await RunTurnAsync(container, SessionRef.Persistent(sessionId), userId, isHidden: true);

        Assert.DoesNotContain(events, e => e.Type == "error");
        Assert.DoesNotContain(counter.Calls, c => c.Delta.UserMessages != 0 || c.Delta.IncognitoUserMessages != 0);
        Assert.Single(counter.Calls, c => c.UserId == userId && c.Delta == new ActivityDelta(AssistantMessages: 1));
    }
}
