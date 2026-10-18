using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Overseer.Controllers;
using Overseer.Services;
using Overseer.Services.Agents;
using Overseer.Services.Providers;
using Overseer.Services.Tools;
using Overseer.Tests.Helpers;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// Guards on what a model can make the tool layer do: bounded regular expressions, GitHub owner
/// and repository validation, exception text that reaches the model, dumplog path construction,
/// and refusal of a tool call the request never declared.
/// </summary>
public class ToolHardeningTests : IDisposable
{
    private readonly string _sourceDir;

    public ToolHardeningTests()
    {
        _sourceDir = Path.Combine(Path.GetTempPath(), "ToolHardeningTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_sourceDir, "src"));
        Directory.CreateDirectory(Path.Combine(_sourceDir, "include"));

        File.WriteAllText(Path.Combine(_sourceDir, "src", "redos.c"),
            "/* redos.c */\n" + new string('a', 40) + "!\nint encounter_count = 0;\n");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_sourceDir)) Directory.Delete(_sourceDir, true);
        }
        catch (IOException)
        {
            /* A temp directory the OS still holds a handle on is not a test failure. */
        }
    }

    private SourceCodeService CreateSourceService()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new KeyValuePair<string, string?>("SourceCodePath", _sourceDir),
                new KeyValuePair<string, string?>("MaxSourceFileSizeKB", "800")
            })
            .Build();

        var service = new SourceCodeService(config, NullLogger<SourceCodeService>.Instance);
        service.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        service.InitializationTask.GetAwaiter().GetResult();
        return service;
    }

    // ---- Regular expressions ----

    [Fact]
    public void SearchFiles_CatastrophicRegex_ReturnsTheTimeoutErrorQuickly()
    {
        using var service = CreateSourceService();

        var stopwatch = Stopwatch.StartNew();
        string result = service.SearchFiles("(a+)+$", null, 10, false, 10000, isRegex: true);
        stopwatch.Stop();

        Assert.Equal(SourceCodeService.RegexTooSlowMessage, result);
        Assert.StartsWith("Error: The regular expression took too long to evaluate", result);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"The search took {stopwatch.Elapsed}.");
    }

    [Fact]
    public void SearchFiles_OrdinaryRegex_StillMatches()
    {
        using var service = CreateSourceService();

        string result = service.SearchFiles(@"encounter_\w+", null, 10, false, 10000, isRegex: true, filenamesOnly: true);

        Assert.Contains("redos.c", result);
    }

    [Fact]
    public void SearchFiles_InvalidRegex_KeepsTheCompilerMessage()
    {
        using var service = CreateSourceService();

        string result = service.SearchFiles("zzz([0-9", null, 10, false, 10000, isRegex: true);

        Assert.StartsWith("Error: Invalid regular expression.", result);
    }

    [Fact]
    public void GetConstants_RepeatedWildcards_DoNotThrow()
    {
        using var service = CreateSourceService();

        var constants = service.GetConstants(new string('*', 200) + "X", null).ToList();

        Assert.NotNull(constants);
    }

    // ---- GitHub owner and repository ----

    private static readonly IReadOnlyCollection<string> Defaults = GitHubRepositoryGuard.DefaultAllowedOwners;

    [Fact]
    public void GitHubGuard_AllowedOwnerAndRepository_IsValid()
    {
        Assert.Null(GitHubRepositoryGuard.ValidateOwnerRepo("hyvanmielenpelit", "GnollHack", Defaults));
    }

    [Fact]
    public void GitHubGuard_OwnerIsCaseInsensitive()
    {
        Assert.Null(GitHubRepositoryGuard.ValidateOwnerRepo("HyvanMielenPelit", "GnollHack", Defaults));
        Assert.Null(GitHubRepositoryGuard.ValidateOwnerRepo("DotNet", "runtime", Defaults));
    }

    [Theory]
    [InlineData("..", "user")]
    [InlineData("hyvanmielenpelit", "..")]
    [InlineData("hyvanmielenpelit", ".")]
    [InlineData("hyvanmielenpelit", "a/b")]
    [InlineData("hyvan/mielenpelit", "GnollHack")]
    [InlineData("-leadinghyphen", "GnollHack")]
    [InlineData("", "GnollHack")]
    public void GitHubGuard_MalformedNames_AreRefused(string owner, string repo)
    {
        var error = GitHubRepositoryGuard.ValidateOwnerRepo(owner, repo, Defaults);

        Assert.NotNull(error);
        Assert.Contains("Allowed owners: hyvanmielenpelit, dotnet, microsoft, mono", error);
    }

    [Fact]
    public void GitHubGuard_UnlistedOwner_IsRefusedAndTheAllowedOwnersAreNamed()
    {
        var error = GitHubRepositoryGuard.ValidateOwnerRepo("someoneelse", "private-repo", Defaults);

        Assert.NotNull(error);
        Assert.Contains("someoneelse", error);
        Assert.Contains("hyvanmielenpelit", error);
        Assert.Contains("mono", error);
    }

    [Theory]
    [InlineData("hyvanmielenpelit")]
    [InlineData("hyvanmielenpelit/")]
    [InlineData("/GnollHack")]
    [InlineData("hyvanmielenpelit/GnollHack/extra")]
    [InlineData("someoneelse/repo")]
    [InlineData("hyvanmielenpelit/..")]
    public void GitHubGuard_BadRepoFilter_IsRefused(string repoFilter)
    {
        Assert.NotNull(GitHubRepositoryGuard.ValidateRepoFilter(repoFilter, Defaults));
    }

    [Fact]
    public void GitHubGuard_GoodRepoFilter_IsValid()
    {
        Assert.Null(GitHubRepositoryGuard.ValidateRepoFilter("hyvanmielenpelit/GnollHack", Defaults));
    }

    [Fact]
    public void GitHubGuard_EmptyConfiguration_GivesTheDefaults()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();

        Assert.Equal(GitHubRepositoryGuard.DefaultAllowedOwners, GitHubRepositoryGuard.ResolveAllowedOwners(config));
    }

    [Fact]
    public void GitHubGuard_ConfiguredOwners_ReplaceTheDefaults()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GitHub:AllowedOwners:0"] = "hyvanmielenpelit",
                ["GitHub:AllowedOwners:1"] = "example-org"
            })
            .Build();

        var owners = GitHubRepositoryGuard.ResolveAllowedOwners(config);

        Assert.Equal(new[] { "hyvanmielenpelit", "example-org" }, owners);
        Assert.NotNull(GitHubRepositoryGuard.ValidateOwnerRepo("dotnet", "runtime", owners));
        Assert.Null(GitHubRepositoryGuard.ValidateOwnerRepo("example-org", "tools", owners));
    }

    // ---- Exception text shown to the model ----

    [Fact]
    public void ToolErrorText_CorrectableExceptions_KeepTheirMessage()
    {
        Assert.Equal("bad argument", ToolErrorText.ForModel(new ArgumentException("bad argument")));
        Assert.Contains("missing value", ToolErrorText.ForModel(new ArgumentNullException("x", "missing value")));
        Assert.Contains("out of range", ToolErrorText.ForModel(new ArgumentOutOfRangeException("x", "out of range")));
        Assert.Equal("bad format", ToolErrorText.ForModel(new FormatException("bad format")));
        Assert.Equal("bad json", ToolErrorText.ForModel(new JsonException("bad json")));
        Assert.Equal("not supported", ToolErrorText.ForModel(new NotSupportedException("not supported")));
    }

    [Fact]
    public void ToolErrorText_OtherExceptions_HideTheirMessage()
    {
        var text = ToolErrorText.ForModel(new IOException(@"Could not find file 'C:\secret\appsettings.Production.json'."));

        Assert.DoesNotContain("secret", text);
        Assert.DoesNotContain("appsettings", text);
        Assert.Equal("An internal error occurred while running the tool (IOException). It has been logged.", text);
    }

    // ---- Dumplog paths ----

    [Fact]
    public void BuildDumplogPath_NormalName_BuildsTheSamePathAsBefore()
    {
        string basePath = Path.Combine(Path.GetTempPath(), "dumplogs");

        string? path = SearchServerDumplogsTool.BuildDumplogPath(basePath, "Player1", 1234567890);

        Assert.Equal(Path.Combine(basePath, "Player1", "gnollhack.Player1.1234567890.txt"), path);
    }

    [Fact]
    public void BuildDumplogPath_BasePathWithTrailingSeparator_IsAccepted()
    {
        string basePath = Path.Combine(Path.GetTempPath(), "dumplogs") + Path.DirectorySeparatorChar;

        Assert.NotNull(SearchServerDumplogsTool.BuildDumplogPath(basePath, "Player1", 1));
    }

    [Fact]
    public void BuildDumplogPath_UnsafeNames_ReturnNull()
    {
        string basePath = Path.Combine(Path.GetTempPath(), "dumplogs");
        string rooted = Path.Combine(Path.GetTempPath(), "elsewhere");

        Assert.Null(SearchServerDumplogsTool.BuildDumplogPath(basePath, "..", 1));
        Assert.Null(SearchServerDumplogsTool.BuildDumplogPath(basePath, ".", 1));
        Assert.Null(SearchServerDumplogsTool.BuildDumplogPath(basePath, "a/b", 1));
        Assert.Null(SearchServerDumplogsTool.BuildDumplogPath(basePath, @"a\b", 1));
        Assert.Null(SearchServerDumplogsTool.BuildDumplogPath(basePath, rooted, 1));
        Assert.Null(SearchServerDumplogsTool.BuildDumplogPath(basePath, "", 1));
    }

    // ---- Undeclared tool calls ----

    [Fact]
    public void IsDeclared_UntrackedDeclarations_AdmitEveryName()
    {
        Assert.True(AgentLoopRunner.IsDeclared(new ToolsForRequest(), "anything"));
    }

    [Fact]
    public void IsDeclared_TrackedDeclarations_AdmitOnlyDeclaredNames()
    {
        var tools = new ToolsForRequest
        {
            DeclaredToolNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "wiki_search" }
        };

        Assert.True(AgentLoopRunner.IsDeclared(tools, "wiki_search"));
        Assert.True(AgentLoopRunner.IsDeclared(tools, "WIKI_SEARCH"));
        Assert.False(AgentLoopRunner.IsDeclared(tools, "search_github"));
    }

    [Fact]
    public async Task RunAsync_CallToAToolTheRequestDidNotDeclare_IsRefusedWithoutRunningTheHandler()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var sp = services.BuildServiceProvider();
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        using var cache = new MemoryCache(new MemoryCacheOptions());

        var provider = new UndeclaredCallProvider("counting_tool");
        var handler = new CountingToolHandler();
        var bridge = new NullClientBridge();
        var handlers = new List<IToolHandler> { handler };

        var runner = new AgentLoopRunner(
            new IAiProvider[] { provider },
            new ToolRegistry(handlers, bridge, NullLogger<ToolRegistry>.Instance),
            new ToolExecutor(handlers, bridge, NullLogger<ToolExecutor>.Instance, cache, config),
            new StubHttpClientFactory(),
            config,
            sp.GetRequiredService<IServiceScopeFactory>(),
            TestKnowledgeBase.Empty,
            new ModelMetadataService(),
            NullLogger<AgentLoopRunner>.Instance,
            new SubAgentCatalogService(config, NullLogger<SubAgentCatalogService>.Instance));

        var request = new AgentRunRequest
        {
            ProviderName = provider.ProviderName,
            AiProvider = provider,
            ModelId = "stub-model",
            ApiKey = "stub-api-key",
            EnableToolUse = true,
            AllowedToolNames = new[] { "some_other_tool" },
            SeedHistory = new List<object> { new { role = "user", content = "Run the tool" } }
        };

        var result = new AgentRunResult();
        var events = new List<ChatEvent>();
        await foreach (var evt in runner.RunAsync(request, null, result, TestContext.Current.CancellationToken))
        {
            events.Add(evt);
        }

        const string refusal = "Tool 'counting_tool' is not available in this conversation.";
        Assert.Equal(0, handler.Calls);
        var toolError = Assert.Single(events, e => e.Type == "tool_error" && e.Data != null && e.Data.StartsWith("{"));
        using (var doc = JsonDocument.Parse(toolError.Data!))
        {
            Assert.Equal(refusal, doc.RootElement.GetProperty("error").GetString());
        }
        Assert.Contains(provider.ToolResults, r => r.Contains(refusal));
        Assert.Equal("Done.", result.FinalText);
    }

    [Fact]
    public async Task RunAsync_CallToADeclaredTool_RunsTheHandler()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var sp = services.BuildServiceProvider();
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        using var cache = new MemoryCache(new MemoryCacheOptions());

        var provider = new UndeclaredCallProvider("counting_tool");
        var handler = new CountingToolHandler();
        var bridge = new NullClientBridge();
        var handlers = new List<IToolHandler> { handler };

        var runner = new AgentLoopRunner(
            new IAiProvider[] { provider },
            new ToolRegistry(handlers, bridge, NullLogger<ToolRegistry>.Instance),
            new ToolExecutor(handlers, bridge, NullLogger<ToolExecutor>.Instance, cache, config),
            new StubHttpClientFactory(),
            config,
            sp.GetRequiredService<IServiceScopeFactory>(),
            TestKnowledgeBase.Empty,
            new ModelMetadataService(),
            NullLogger<AgentLoopRunner>.Instance,
            new SubAgentCatalogService(config, NullLogger<SubAgentCatalogService>.Instance));

        var request = new AgentRunRequest
        {
            ProviderName = provider.ProviderName,
            AiProvider = provider,
            ModelId = "stub-model",
            ApiKey = "stub-api-key",
            EnableToolUse = true,
            SeedHistory = new List<object> { new { role = "user", content = "Run the tool" } }
        };

        var result = new AgentRunResult();
        await foreach (var _ in runner.RunAsync(request, null, result, TestContext.Current.CancellationToken)) { }

        Assert.Equal(1, handler.Calls);
        Assert.Contains(provider.ToolResults, r => r.Contains("Counted."));
    }

    private sealed class CountingToolHandler : IToolHandler
    {
        private int _calls;

        public int Calls => _calls;
        public string ToolName => "counting_tool";
        public string Description { get; set; } = "Counts its calls.";
        public ToolExecutionLocation ExecutionLocation => ToolExecutionLocation.Server;
        public ToolCategory Category => ToolCategory.InformationRetrieval;
        public int TimeoutSeconds => 10;
        public JsonElement ParameterSchema => JsonSerializer.SerializeToElement(new { type = "object" });

        public Task<ToolResult> ExecuteAsync(JsonElement parameters, ToolExecutionContext context, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(new ToolResult { Success = true, Content = "Counted." });
        }
    }

    /// <summary>Calls one named tool on its first model call and answers "Done." after that.</summary>
    private sealed class UndeclaredCallProvider : IAiProvider
    {
        private readonly string _toolName;
        private int _calls;

        public UndeclaredCallProvider(string toolName) => _toolName = toolName;

        public List<string> ToolResults { get; } = new();

        public string ProviderName => "StubProvider";
        public IReadOnlyList<string> SupportedServiceTiers => new[] { "default" };

        public Dictionary<string, object> BuildChatRequestBody(string modelId, List<object> messageHistory, int? maxOutputTokens, string? thinkingLevel, ToolsForRequest requestTools, string? reasoningMode = null, string? reasoningSummary = null, string? serviceTier = null, bool? parallelToolCalls = null, SegmentedPrompt? segmentedPrompt = null, string? promptCacheKey = null, bool cacheConversationTail = true, bool disablePromptCache = false)
            => new() { { "model", modelId } };

        public string GetChatStreamUrl(string modelId, string apiKey, AiEndpointDescriptor endpoint)
            => endpoint.ComposeUrl("https://stub.ai.test/stream", "/stream");

        public void ConfigureRequest(HttpRequestMessage request, string apiKey, AiEndpointDescriptor endpoint) { }

        public async IAsyncEnumerable<ChatEvent> ParseStreamAsync(HttpResponseMessage response, bool showDebugLog, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            _calls++;
            if (_calls == 1)
            {
                yield return new ChatEvent
                {
                    Type = "tool_call_complete",
                    Data = JsonSerializer.Serialize(new { id = "call_1", name = _toolName, arguments = "{}" })
                };
            }
            else
            {
                yield return new ChatEvent { Type = "chunk", Data = "Done." };
            }
            await Task.CompletedTask;
        }

        public object FormatMessage(string role, string text, List<SendMessageAttachment>? imageAttachments) => new { role, content = text };
        public List<object> PrepareMessageHistory(List<object> messages) => new(messages);

        public void AppendAssistantToolCallsToHistory(List<object> messageHistory, string iterationText, List<JsonElement> toolCalls, List<JsonElement>? providerHistoryItems = null)
            => messageHistory.Add(new { role = "assistant", content = iterationText });

        public void AppendToolResultsToHistory(List<object> messageHistory, List<ProviderToolResult> results)
        {
            foreach (var r in results)
            {
                ToolResults.Add(r.Content);
                messageHistory.Add(new { role = "tool", content = r.Content });
            }
        }

        public bool TryRewriteToolResult(List<object> messageHistory, string toolCallId, string replacementText) => false;
        public Dictionary<string, object> BuildTitleRequestBody(string modelId, string systemPrompt, string userMessage, int maxTokens, string? serviceTier = null) => new();
        public string GetTitleUrl(string modelId, string apiKey, AiEndpointDescriptor endpoint) => "https://stub.ai.test/title";
        public string? ParseTitleResponse(JsonElement root) => "Stub Title";
        public object? BuildWebSearchTool() => null;
        public object BuildFunctionDeclaration(string name, string description, object parameterSchema) => new { name };
        public object? BuildToolsPayload(List<object> providerTools, List<object> functionDeclarations) => new { };
    }

    private sealed class NullClientBridge : IClientToolBridge
    {
        public bool IsClientConnected => true;

        public Task<ToolResult> SendToolRequestAsync(Overseer.Services.Privacy.SessionRef sessionRef, string toolName, JsonElement parameters, CancellationToken cancellationToken)
            => Task.FromResult(new ToolResult { Success = true, Content = "Client result" });
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new OkHandler());

        private sealed class OkHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
                => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("data: {}\n\n") });
        }
    }
}
