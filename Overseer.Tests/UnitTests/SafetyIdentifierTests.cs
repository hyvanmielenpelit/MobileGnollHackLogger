using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MobileGnollHackLogger.Data;
using Overseer.Controllers;
using Overseer.Services;
using Overseer.Services.Agents;
using Overseer.Services.Providers;
using Overseer.Services.Security;
using Overseer.Services.Tools;
using Overseer.Tests.Helpers;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// The per-user pseudonym sent with operator-funded requests: its shape and determinism, the reverse lookup,
/// where each provider puts it in the request body, and the agent loop leaving it off a custom endpoint.
/// </summary>
public class SafetyIdentifierTests
{
    private static readonly Regex IdentifierShape = new("^u[0-9a-f]{32}$", RegexOptions.CultureInvariant);

    private static byte[] Key(byte fill) => Enumerable.Repeat(fill, 32).ToArray();

    // ── Shape and determinism ───────────────────────────────────────────────────

    [Fact]
    public void For_IsDeterministicPerUser()
    {
        string userId = Guid.NewGuid().ToString();

        Assert.Equal(new SafetyIdentifierService(Key(7)).For(userId), new SafetyIdentifierService(Key(7)).For(userId));
    }

    [Fact]
    public void For_DiffersAcrossUsers()
    {
        var service = new SafetyIdentifierService(Key(7));

        var identifiers = Enumerable.Range(0, 50).Select(i => service.For("user-" + i)).ToList();

        Assert.Equal(identifiers.Count, identifiers.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void For_DiffersAcrossKeys()
    {
        string userId = Guid.NewGuid().ToString();

        Assert.NotEqual(new SafetyIdentifierService(Key(7)).For(userId), new SafetyIdentifierService(Key(8)).For(userId));
    }

    [Theory]
    [InlineData("3f2b8c1e-7d4a-4e9b-a1c2-5d6e7f8091ab")]
    [InlineData("gnollhacker")]
    [InlineData("u")]
    [InlineData("")]
    public void For_IsUFollowedBy32LowercaseHexDigits(string userId)
    {
        string identifier = new SafetyIdentifierService(Key(7)).For(userId);

        Assert.Equal(33, identifier.Length);
        Assert.StartsWith("u", identifier, StringComparison.Ordinal);
        Assert.Matches(IdentifierShape, identifier);
    }

    /* Hex-only ids, because those are the only ones the identifier's own alphabet could carry
       through: an id with any other character is already excluded by the shape test above. */
    [Theory]
    [InlineData("abc123def456")]
    [InlineData("0123456789abcdef")]
    [InlineData("deadbeefdeadbeefdeadbeefdeadbeef")]
    public void For_NeverContainsTheUserId(string userId)
    {
        string identifier = new SafetyIdentifierService(Key(7)).For(userId);

        Assert.DoesNotContain(userId, identifier, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheConfigurationConstructor_DerivesFromAesEncryptionKey()
    {
        byte[] master = Key(9);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            { "AesEncryptionKey", Convert.ToBase64String(master) }
        }).Build();

        Assert.Equal(new SafetyIdentifierService(master).For("user-1"), new SafetyIdentifierService(config).For("user-1"));
    }

    [Fact]
    public void TheConfigurationConstructor_RefusesAMissingKey()
    {
        var config = new ConfigurationBuilder().Build();

        Assert.Throws<InvalidOperationException>(() => new SafetyIdentifierService(config));
    }

    // ── FindUserIdAsync ─────────────────────────────────────────────────────────

    private static async Task<ServiceProvider> BuildUsersAsync(params string[] userIds)
    {
        var services = new ServiceCollection();
        string databaseName = Guid.NewGuid().ToString();
        services.AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(databaseName));
        var sp = services.BuildServiceProvider();

        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        foreach (string id in userIds)
        {
            db.Users.Add(new ApplicationUser { Id = id, UserName = "name-" + id });
        }
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return sp;
    }

    [Fact]
    public async Task FindUserIdAsync_FindsTheUserWhoseIdentifierItIs()
    {
        using var sp = await BuildUsersAsync("user-a", "user-b", "user-c");
        var service = new SafetyIdentifierService(Key(7), sp.GetRequiredService<IServiceScopeFactory>());
        var ct = TestContext.Current.CancellationToken;

        Assert.Equal("user-b", await service.FindUserIdAsync(service.For("user-b"), ct));
        Assert.Equal("user-c", await service.FindUserIdAsync(service.For("user-c"), ct));
    }

    [Fact]
    public async Task FindUserIdAsync_IgnoresCaseAndSurroundingWhitespace()
    {
        using var sp = await BuildUsersAsync("user-a", "user-b");
        var service = new SafetyIdentifierService(Key(7), sp.GetRequiredService<IServiceScopeFactory>());

        string pasted = "  " + service.For("user-a").ToUpperInvariant() + " ";

        Assert.Equal("user-a", await service.FindUserIdAsync(pasted, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task FindUserIdAsync_MissesAnUnknownIdentifier()
    {
        using var sp = await BuildUsersAsync("user-a", "user-b");
        var service = new SafetyIdentifierService(Key(7), sp.GetRequiredService<IServiceScopeFactory>());
        var ct = TestContext.Current.CancellationToken;

        Assert.Null(await service.FindUserIdAsync(service.For("user-z"), ct));
        // An identifier made under another key matches no user.
        Assert.Null(await service.FindUserIdAsync(new SafetyIdentifierService(Key(8)).For("user-a"), ct));
        Assert.Null(await service.FindUserIdAsync("", ct));
        Assert.Null(await service.FindUserIdAsync("   ", ct));
    }

    [Fact]
    public async Task FindUserIdAsync_IsNullWithoutAScopeFactory()
    {
        var service = new SafetyIdentifierService(Key(7));

        Assert.Null(await service.FindUserIdAsync(service.For("user-a"), TestContext.Current.CancellationToken));
    }

    // ── Provider request bodies ─────────────────────────────────────────────────

    private const string Identifier = "u0123456789abcdef0123456789abcdef";

    private static IConfiguration EmptyConfig() => new ConfigurationBuilder().Build();

    /// <summary>Builds a chat body through the interface overload the agent loop calls.</summary>
    private static Dictionary<string, object> ChatBody(IAiProvider provider, string modelId, string? safetyIdentifier)
    {
        var history = provider.PrepareMessageHistory(new List<object>
        {
            provider.FormatMessage("user", "What is a newt?", null)
        });

        return provider.BuildChatRequestBody(
            modelId, history, 1024, null, new ToolsForRequest(),
            null, null, null, null, null, null, true, false,
            safetyIdentifier);
    }

    private static Dictionary<string, object> TitleBody(IAiProvider provider, string modelId, string? safetyIdentifier)
        => provider.BuildTitleRequestBody(modelId, "Write a title.", "What is a newt?", 32, null, safetyIdentifier);

    [Fact]
    public void OpenAiChatBody_CarriesSafetyIdentifier_OnlyWhenGiven()
    {
        IAiProvider provider = new OpenAiResponsesProvider(EmptyConfig());

        var withId = ChatBody(provider, "gpt-5", Identifier);
        Assert.Equal(Identifier, withId["safety_identifier"]);

        var withoutId = ChatBody(provider, "gpt-5", null);
        Assert.False(withoutId.ContainsKey("safety_identifier"));
    }

    [Fact]
    public void OpenAiTitleBody_CarriesSafetyIdentifier_OnlyWhenGiven()
    {
        IAiProvider provider = new OpenAiResponsesProvider(EmptyConfig());

        Assert.Equal(Identifier, TitleBody(provider, "gpt-5", Identifier)["safety_identifier"]);
        Assert.False(TitleBody(provider, "gpt-5", null).ContainsKey("safety_identifier"));
    }

    [Fact]
    public void OpenAiBody_IsOtherwiseUnchangedByTheIdentifier()
    {
        IAiProvider provider = new OpenAiResponsesProvider(EmptyConfig());

        var withId = ChatBody(provider, "gpt-5", Identifier);
        withId.Remove("safety_identifier");

        Assert.Equal(JsonSerializer.Serialize(ChatBody(provider, "gpt-5", null)), JsonSerializer.Serialize(withId));
    }

    [Fact]
    public void AnthropicChatBody_CarriesMetadataUserId_OnlyWhenGiven()
    {
        IAiProvider provider = new AnthropicProvider(EmptyConfig());

        var withId = ChatBody(provider, "claude-opus-5", Identifier);
        var metadata = Assert.IsType<Dictionary<string, object>>(withId["metadata"]);
        Assert.Equal(Identifier, metadata["user_id"]);
        Assert.Contains($"\"metadata\":{{\"user_id\":\"{Identifier}\"}}", JsonSerializer.Serialize(withId));

        var withoutId = ChatBody(provider, "claude-opus-5", null);
        Assert.False(withoutId.ContainsKey("metadata"));
    }

    [Fact]
    public void AnthropicTitleBody_CarriesMetadataUserId_OnlyWhenGiven()
    {
        IAiProvider provider = new AnthropicProvider(EmptyConfig());

        var metadata = Assert.IsType<Dictionary<string, object>>(TitleBody(provider, "claude-opus-5", Identifier)["metadata"]);
        Assert.Equal(Identifier, metadata["user_id"]);
        Assert.False(TitleBody(provider, "claude-opus-5", null).ContainsKey("metadata"));
    }

    [Fact]
    public void GoogleBodies_AreUnchangedByTheIdentifier()
    {
        IAiProvider provider = new GoogleProvider(EmptyConfig());

        string chatWith = JsonSerializer.Serialize(ChatBody(provider, "gemini-3.7-flash", Identifier));
        string chatWithout = JsonSerializer.Serialize(ChatBody(provider, "gemini-3.7-flash", null));
        Assert.Equal(chatWithout, chatWith);
        Assert.DoesNotContain(Identifier, chatWith);

        string titleWith = JsonSerializer.Serialize(TitleBody(provider, "gemini-3.7-flash", Identifier));
        string titleWithout = JsonSerializer.Serialize(TitleBody(provider, "gemini-3.7-flash", null));
        Assert.Equal(titleWithout, titleWith);
        Assert.DoesNotContain(Identifier, titleWith);
    }

    // ── AgentLoopRunner ─────────────────────────────────────────────────────────

    /// <summary>Records the safety identifier each chat request body was built with.</summary>
    private sealed class RecordingProvider : IAiProvider
    {
        public List<string?> SafetyIdentifiers { get; } = new();

        public string ProviderName => "RecordingProvider";
        public IReadOnlyList<string> SupportedServiceTiers => new[] { "default" };

        public void AppendAssistantToolCallsToHistory(List<object> messageHistory, string iterationText, List<JsonElement> toolCalls, List<JsonElement>? providerHistoryItems = null)
            => messageHistory.Add(new { role = "assistant", content = iterationText });

        public void AppendToolResultsToHistory(List<object> messageHistory, List<ProviderToolResult> results)
        {
            foreach (var r in results) messageHistory.Add(new { role = "tool", content = r.Content });
        }

        public void AppendUserTextToHistory(List<object> messageHistory, string text)
            => messageHistory.Add(new { role = "user", content = text });

        public Dictionary<string, object> BuildChatRequestBody(string modelId, List<object> messageHistory, int? maxOutputTokens, string? thinkingLevel, ToolsForRequest requestTools, string? reasoningMode = null, string? reasoningSummary = null, string? serviceTier = null, bool? parallelToolCalls = null, SegmentedPrompt? segmentedPrompt = null, string? promptCacheKey = null, bool cacheConversationTail = true, bool disablePromptCache = false)
            => new() { { "model", modelId } };

        public Dictionary<string, object> BuildChatRequestBody(string modelId, List<object> messageHistory, int? maxOutputTokens, string? thinkingLevel, ToolsForRequest requestTools, string? reasoningMode, string? reasoningSummary, string? serviceTier, bool? parallelToolCalls, SegmentedPrompt? segmentedPrompt, string? promptCacheKey, bool cacheConversationTail, bool disablePromptCache, string? safetyIdentifier)
        {
            SafetyIdentifiers.Add(safetyIdentifier);
            return new() { { "model", modelId } };
        }

        public bool TryRewriteToolResult(List<object> messageHistory, string toolCallId, string replacementText) => false;
        public object BuildFunctionDeclaration(string name, string description, object parameterSchema) => new { name };
        public object? BuildToolsPayload(List<object> providerTools, List<object> functionDeclarations) => null;
        public object? BuildWebSearchTool() => null;
        public void ConfigureRequest(HttpRequestMessage request, string apiKey, AiEndpointDescriptor endpoint) { }
        public object FormatMessage(string role, string text, List<SendMessageAttachment>? imageAttachments) => new { role, content = text };
        public string GetChatStreamUrl(string modelId, string apiKey, AiEndpointDescriptor endpoint)
            => endpoint.ComposeUrl("https://recording.ai.test/stream", "/stream");

        public async IAsyncEnumerable<ChatEvent> ParseStreamAsync(HttpResponseMessage response, bool showDebugLog, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            yield return new ChatEvent { Type = "chunk", Data = "Answer." };
            await Task.CompletedTask;
        }

        public List<object> PrepareMessageHistory(List<object> messages) => new(messages);
        public Dictionary<string, object> BuildTitleRequestBody(string modelId, string systemPrompt, string userMessage, int maxTokens, string? serviceTier = null) => new();
        public string GetTitleUrl(string modelId, string apiKey, AiEndpointDescriptor endpoint) => "https://recording.ai.test/title";
        public string? ParseTitleResponse(JsonElement root) => "Title";
    }

    private sealed class NullClientBridge : IClientToolBridge
    {
        public bool IsClientConnected => false;
        public Task<ToolResult> SendToolRequestAsync(Overseer.Services.Privacy.SessionRef sessionRef, string toolName, JsonElement parameters, CancellationToken cancellationToken)
            => Task.FromResult(new ToolResult { Success = false, ErrorMessage = "no client" });
    }

    private sealed class OkHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("{\"ok\":true}") });
    }

    private sealed class OkHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new OkHttpMessageHandler());
    }

    private static async Task<List<string?>> RunAgentLoopAsync(AiEndpointDescriptor endpoint)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        using var sp = services.BuildServiceProvider();

        var provider = new RecordingProvider();
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var bridge = new NullClientBridge();
        var handlers = new List<IToolHandler>();

        var runner = new AgentLoopRunner(
            new[] { provider },
            new ToolRegistry(handlers, bridge, NullLogger<ToolRegistry>.Instance),
            new ToolExecutor(handlers, bridge, NullLogger<ToolExecutor>.Instance, cache, config),
            new OkHttpClientFactory(),
            config,
            sp.GetRequiredService<IServiceScopeFactory>(),
            TestKnowledgeBase.Empty,
            new ModelMetadataService(),
            NullLogger<AgentLoopRunner>.Instance);

        var request = new AgentRunRequest
        {
            ProviderName = provider.ProviderName,
            ModelId = "recording-model",
            ApiKey = "test-key",
            SeedHistory = new List<object> { new { role = "user", content = "Hello" } },
            AiProvider = provider,
            Endpoint = endpoint,
            SafetyIdentifier = Identifier
        };

        var result = new AgentRunResult();
        await foreach (var _ in runner.RunAsync(request, null, result, TestContext.Current.CancellationToken))
        {
        }

        Assert.Equal("completed", result.TerminationReason);
        return provider.SafetyIdentifiers;
    }

    [Fact]
    public async Task AgentLoopRunner_PassesTheIdentifierToTheOfficialEndpoint()
    {
        var sent = await RunAgentLoopAsync(AiEndpointDescriptor.Official);

        Assert.NotEmpty(sent);
        Assert.All(sent, id => Assert.Equal(Identifier, id));
    }

    [Fact]
    public async Task AgentLoopRunner_PassesNullToACustomEndpoint()
    {
        var custom = new AiEndpointDescriptor("https://gateway.example.test", null, null, AiEndpointAuthStyle.ProviderDefault);
        Assert.True(custom.IsCustom);

        var sent = await RunAgentLoopAsync(custom);

        Assert.NotEmpty(sent);
        Assert.All(sent, id => Assert.Null(id));
    }
}
