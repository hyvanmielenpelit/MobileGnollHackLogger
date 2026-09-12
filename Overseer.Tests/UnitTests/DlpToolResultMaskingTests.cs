using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MobileGnollHackLogger.Data;
using Overseer.Controllers;
using Overseer.Services;
using Overseer.Services.Agents;
using Overseer.Services.Privacy;
using Overseer.Services.Privacy.Dlp;
using Overseer.Services.Providers;
using Overseer.Services.Tools;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// A tool result carrying a credential, masked before it re-enters the prompt.
/// </summary>
/// <remarks>
/// The third masking site and the one furthest from the user: a tool that read a file, searched
/// a corpus or ran a sub-agent can return a secret, and that result becomes part of the next
/// request. Masking the user's own message alone would let it reach the provider by the longer
/// route. It is driven here rather than through <c>ChatService</c> because the loop is what owns
/// the site, and a full turn with a tool call needs the loop's own harness.
/// </remarks>
public class DlpToolResultMaskingTests
{
    /* Random enough to clear the entropy gate, which a placeholder like sk-XXXX does not. */
    private const string SecretInFile = "sk-ant-api03-Yv7Qm2LpR4tKw9BzXn8JdG6ScHaEuT1oNvXbAiQrZk5W";

    private sealed class SecretLeakingToolHandler : IToolHandler
    {
        public string ToolName => "read_config";
        public string Description { get; set; } = "Returns a configuration file that happens to contain a key.";
        public ToolExecutionLocation ExecutionLocation => ToolExecutionLocation.Server;
        public ToolCategory Category => ToolCategory.InformationRetrieval;
        public int TimeoutSeconds => 10;
        public JsonElement ParameterSchema => JsonSerializer.SerializeToElement(new { type = "object" });

        public Task<ToolResult> ExecuteAsync(JsonElement parameters, ToolExecutionContext context, CancellationToken cancellationToken)
            => Task.FromResult(new ToolResult
            {
                Success = true,
                Content = $"# app.config\napi_key = {SecretInFile}\ntimeout = 30"
            });
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

    private sealed class NullClientBridge : IClientToolBridge
    {
        public bool IsClientConnected => false;
        public Task<ToolResult> SendToolRequestAsync(SessionRef sessionRef, string toolName, JsonElement parameters, CancellationToken ct)
            => Task.FromResult(new ToolResult { Success = false, ErrorMessage = "no client" });
    }

    /// <summary>Calls the tool once, then answers, capturing what each request carried.</summary>
    private sealed class ToolThenAnswerProvider : IAiProvider
    {
        private int _calls;

        public string ProviderName => "StubProvider";
        public IReadOnlyList<string> SupportedServiceTiers => new[] { "default" };

        /// <summary>Every request body's history, serialized, in order.</summary>
        public List<string> RequestHistories { get; } = new();

        public void AppendAssistantToolCallsToHistory(List<object> messageHistory, string iterationText, List<JsonElement> toolCalls, List<JsonElement>? providerHistoryItems = null)
            => messageHistory.Add(new { role = "assistant", content = iterationText });

        public void AppendToolResultsToHistory(List<object> messageHistory, List<ProviderToolResult> results)
        {
            foreach (var r in results) messageHistory.Add(new { role = "tool", content = r.Content });
        }

        public Dictionary<string, object> BuildChatRequestBody(string modelId, List<object> messageHistory, int? maxOutputTokens, string? thinkingLevel, ToolsForRequest requestTools, string? reasoningMode = null, string? reasoningSummary = null, string? serviceTier = null, bool? parallelToolCalls = null, SegmentedPrompt? segmentedPrompt = null, string? promptCacheKey = null, bool cacheConversationTail = true)
        {
            RequestHistories.Add(JsonSerializer.Serialize(messageHistory));
            return new Dictionary<string, object> { { "model", modelId } };
        }

        public bool TryRewriteToolResult(List<object> messageHistory, string toolCallId, string replacementText) => false;
        public object BuildFunctionDeclaration(string name, string description, object parameterSchema) => new { name };
        public object? BuildToolsPayload(List<object> providerTools, List<object> functionDeclarations) => new { };
        public object? BuildWebSearchTool() => null;
        public void ConfigureRequest(HttpRequestMessage request, string apiKey, AiEndpointDescriptor endpoint) { }
        public object FormatMessage(string role, string text, List<SendMessageAttachment>? imageAttachments) => new { role, content = text };
        public string GetChatStreamUrl(string modelId, string apiKey, AiEndpointDescriptor endpoint)
            => endpoint.ComposeUrl("https://stub.ai.test/stream", "/stream");

        public async IAsyncEnumerable<ChatEvent> ParseStreamAsync(HttpResponseMessage response, bool showDebugLog, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            _calls++;
            if (_calls == 1)
            {
                yield return new ChatEvent
                {
                    Type = "tool_call_complete",
                    Data = JsonSerializer.Serialize(new { id = "call_1", name = "read_config", arguments = "{}" })
                };
            }
            else
            {
                yield return new ChatEvent { Type = "chunk", Data = "The file has a key in it." };
            }
            await Task.CompletedTask;
        }

        public List<object> PrepareMessageHistory(List<object> messages) => new(messages);
        public Dictionary<string, object> BuildTitleRequestBody(string modelId, string systemPrompt, string userMessage, int maxTokens, string? serviceTier = null) => new();
        public string GetTitleUrl(string modelId, string apiKey, AiEndpointDescriptor endpoint) => "https://stub.ai.test/title";
        public string? ParseTitleResponse(JsonElement root) => "Stub Title";
    }

    private static async Task<(ToolThenAnswerProvider Provider, AgentRunResult Result)> RunAsync(DlpTokenVault? vault)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var sp = services.BuildServiceProvider();
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        using var cache = new MemoryCache(new MemoryCacheOptions());

        var provider = new ToolThenAnswerProvider();
        var bridge = new NullClientBridge();
        var handlers = new List<IToolHandler> { new SecretLeakingToolHandler() };
        var registry = new ToolRegistry(handlers, bridge, NullLogger<ToolRegistry>.Instance);
        var executor = new ToolExecutor(handlers, bridge, NullLogger<ToolExecutor>.Instance, cache, config);

        var runner = new AgentLoopRunner(
            new IAiProvider[] { provider },
            registry,
            executor,
            new StubHttpClientFactory(),
            config,
            sp.GetRequiredService<IServiceScopeFactory>(),
            new KnowledgeBaseService(NullLogger<KnowledgeBaseService>.Instance, config),
            new ModelMetadataService(),
            NullLogger<AgentLoopRunner>.Instance,
            new SubAgentCatalogService(config, NullLogger<SubAgentCatalogService>.Instance));

        var request = new AgentRunRequest
        {
            ProviderName = provider.ProviderName,
            AiProvider = provider,
            ModelId = "stub-model-1",
            ApiKey = "stub-api-key",
            EnableToolUse = true,
            SeedHistory = new List<object> { new { role = "user", content = "What is in app.config?" } },
            DlpVault = vault,
            ToolExecutionContext = new ToolExecutionContext { SessionId = SessionRef.Persistent(1) }
        };

        var result = new AgentRunResult();
        await foreach (var _ in runner.RunAsync(request, null, result, TestContext.Current.CancellationToken)) { }

        return (provider, result);
    }

    private static DlpTokenVault NewVault()
        => new(new DlpScannerService(new ConfigurationBuilder().Build()), DlpPolicy.Defaults);

    [Fact]
    public async Task AKeyInAToolResultIsMaskedBeforeItReEntersThePrompt()
    {
        var vault = NewVault();

        var (provider, _) = await RunAsync(vault);

        // Two requests: the one that asked for the tool, and the one carrying its result.
        Assert.Equal(2, provider.RequestHistories.Count);
        string second = provider.RequestHistories[1];
        Assert.DoesNotContain(SecretInFile, second);
        Assert.Contains("[REDACTED_API_KEY_1]", second);
        // The rest of the tool's output is untouched: only the matched value is replaced.
        Assert.Contains("timeout = 30", second);
    }

    [Fact]
    public async Task ThePersistedToolCallRowStillHoldsWhatTheToolActuallyReturned()
    {
        /* Masking is an egress control and not a storage one. This row is what the user sees in
           the transcript and what the database keeps, and in a confidential session it is
           encrypted at the choke point in ChatService. Masking it here would hide from the user
           the very thing they need to see. */
        var vault = NewVault();

        var (_, result) = await RunAsync(vault);

        var toolCall = Assert.Single(result.ToolCalls);
        Assert.Contains(SecretInFile, toolCall.Result!);
        Assert.DoesNotContain("[REDACTED_", toolCall.Result!);
    }

    [Fact]
    public async Task WithNoVaultTheToolResultReachesTheProviderUnchanged()
    {
        // The control: a null vault is the every-class-off state, and it must not mask.
        var (provider, _) = await RunAsync(vault: null);

        Assert.Equal(2, provider.RequestHistories.Count);
        Assert.Contains(SecretInFile, provider.RequestHistories[1]);
    }

    [Fact]
    public async Task TheVaultCarriesTheToolsSecretOnwardSoTheReplyCanBeUnmasked()
    {
        /* The vault gains its first entry here, after ChatService has already built its stream
           unmaskers. That is why they are created whenever masking is enabled rather than only
           when the prompt already held something: otherwise a placeholder the model echoed from
           a tool result would reach the user verbatim. */
        var vault = NewVault();

        await RunAsync(vault);

        Assert.False(vault.IsEmpty);
        Assert.Equal(SecretInFile, vault.Unmask("[REDACTED_API_KEY_1]"));
    }
}
