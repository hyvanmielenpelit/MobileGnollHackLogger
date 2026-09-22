using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MobileGnollHackLogger.Data;
using Overseer.Hubs;
using Overseer.Services;
using Overseer.Services.Providers;
using Overseer.Services.Tools;
using Xunit;
using BenchmarkAssessmentPrompt = Overseer.Services.Benchmarking.BenchmarkAssessmentPrompt;

namespace Overseer.Tests.UnitTests;

public class PromptSegmentationTests
{
    private static ChatService CreateChatService(Dictionary<string, string?>? configOverrides = null)
    {
        var services = new ServiceCollection();
        var dummyKey = Convert.ToBase64String(new byte[32]);
        var inMemorySettings = new Dictionary<string, string?>
        {
            { "AesEncryptionKey", dummyKey },
            { "PromptCacheSettings:EnableSegmentedPrompt", "true" },
            { "PromptCacheSettings:EnableAnthropicCacheControl", "true" },
            { "PromptCacheSettings:EnableOpenAiPromptCacheKey", "true" },
            { "PromptCacheSettings:TruncationBlockSize", "8" }
        };

        if (configOverrides != null)
        {
            foreach (var kvp in configOverrides)
            {
                inMemorySettings[kvp.Key] = kvp.Value;
            }
        }

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString()));

        services.AddSingleton<IConfiguration>(config);
        services.AddHttpClient();
        services.AddSignalR();
        services.AddMemoryCache();
        services.AddSingleton<IClientToolBridge, DummyClientToolBridge>();
        services.AddScoped<ToolRegistry>();
        services.AddScoped<ToolExecutor>();
        services.AddScoped<CryptoService>();
        services.AddScoped<WikiService>();
        services.AddScoped<ModelMetadataService>();
        services.AddScoped<KnowledgeBaseService>();
        services.AddScoped<OngoingChatManager>();
        services.AddScoped<IAiProvider, OpenAiResponsesProvider>();
        services.AddScoped<IAiProvider, AnthropicProvider>();
        services.AddScoped<IAiProvider, GoogleProvider>();
        services.AddScoped<Overseer.Services.Agents.AgentLoopRunner>();
        services.AddSingleton<Overseer.Services.ParallelExecutionResolver>();
        services.AddSingleton<Overseer.Services.Privacy.AttachmentValidator>();
        services.AddSingleton<Overseer.Services.Privacy.IAntiMalwareScanner,
            Overseer.Services.Privacy.NullAntiMalwareScanner>();
        services.AddSingleton<Overseer.Services.Privacy.EndpointPolicy>();
        services.AddSingleton<Overseer.Services.Privacy.ConfidentialityPostureService>();
        services.AddSingleton<Overseer.Services.Privacy.IContentKeyRing,
            Overseer.Services.Privacy.ConfigurationContentKeyRing>();
        services.AddSingleton<Overseer.Services.Privacy.ContentProtectionService>();
        services.AddSingleton<Overseer.Services.Privacy.Dlp.DlpScannerService>();
        services.AddSingleton<Overseer.Services.Documents.DocumentParserService>();
        services.AddSingleton<Overseer.Services.Rag.DocumentChunker>();
        services.AddSingleton<Overseer.Services.Rag.IEmbeddingService,
            Overseer.Services.Rag.LocalOnnxEmbeddingService>();
        services.AddSingleton<Overseer.Services.Rag.DocumentRagService>();
        services.AddScoped<Overseer.Services.Rag.RagSidecarStore>();
        services.AddSingleton(sp => new Overseer.Services.Privacy.EphemeralSessionStore(sp.GetRequiredService<IConfiguration>(), startSweeper: false));
        services.AddScoped<ChatService>();

        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<ChatService>();
    }

    /* The segments for one set of session flags, with the arguments the prompt-content tests
       do not vary held at their least interesting values. */
    private static (string frozen, string session, string volatileSuffix) BuildPrompt(
        ChatService chatService,
        bool spoilerFreeMode = false,
        bool isGameOn = true,
        int overseerMode = 0,
        bool hasGameSnapshot = true,
        string? clientSettings = null)
    {
        return chatService.BuildSegmentedSystemPrompt(
            new List<string>(),
            spoilerFreeMode: spoilerFreeMode,
            verboseMode: false,
            isGameOn: isGameOn,
            developerMode: false,
            overseerMode: overseerMode,
            hasGameSnapshot: hasGameSnapshot,
            hasMessageHistory: false,
            clientSettings: clientSettings,
            enableToolUse: true,
            enableWebSearch: false,
            allowSourceCodeReferences: false);
    }

    [Fact]
    public void BuildSegmentedSystemPrompt_DividesSectionsCorrectly()
    {
        var chatService = CreateChatService();
        var wikiArticles = new List<string> { "# Vorpal Blade\nA powerful artifact." };

        var (frozen, session, volatileSuffix) = chatService.BuildSegmentedSystemPrompt(
            wikiArticles,
            spoilerFreeMode: false,
            verboseMode: true,
            isGameOn: true,
            developerMode: false,
            overseerMode: 0,
            hasGameSnapshot: true,
            hasMessageHistory: true,
            clientSettings: "{\"BoolData\":{\"someFlag\":true}}",
            enableToolUse: true,
            enableWebSearch: true,
            allowSourceCodeReferences: false,
            enableSubAgents: true,
            parallelMode: MobileGnollHackLogger.Data.ParallelExecutionMode.Enabled);

        // Segment A (Frozen prefix) contains Identity, Decision Priorities, and Section 15 (Tool Usage Policy + Subagents)
        Assert.Contains("Gnoll Overseer", frozen);
        Assert.Contains("Decision Priorities", frozen);
        Assert.Contains("Tool Usage Policy", frozen);
        Assert.Contains("Subagent Delegation", frozen);
        Assert.DoesNotContain("Wiki Knowledge Base", frozen);
        Assert.DoesNotContain("## Response Style", frozen);

        // Segment B (Session prefix) contains Response Style and Client Environment
        Assert.Contains("## Response Style — Verbose", session);
        Assert.Contains("Client Environment", session);
        Assert.DoesNotContain("Tool Usage Policy", session);
        Assert.DoesNotContain("Wiki Knowledge Base", session);

        // Segment C (Volatile suffix) contains Wiki Knowledge Base
        Assert.Contains("Wiki Knowledge Base", volatileSuffix);
        Assert.Contains("Vorpal Blade", volatileSuffix);
        Assert.DoesNotContain("Tool Usage Policy", volatileSuffix);
        Assert.DoesNotContain("## Response Style", volatileSuffix);
    }

    [Theory]
    [InlineData("{\"StringData\":{\"Platform\":\"Android\"},\"BoolData\":{\"KeyboardConnected\":false}}",
        "Do not mention keyboard keys")]
    [InlineData("{\"StringData\":{\"Platform\":\"Android\"},\"BoolData\":{\"KeyboardConnected\":true}}",
        "The player has a keyboard")]
    [InlineData("{\"StringData\":{\"Platform\":\"WinUI\"}}", "The player has a keyboard")]
    [InlineData(null, "is not known")]
    public void BuildSegmentedSystemPrompt_PutsTheControlsVariantInTheSessionSegment(
        string? clientSettings, string expectedSentence)
    {
        var chatService = CreateChatService();

        var (frozen, session, _) = BuildPrompt(chatService, clientSettings: clientSettings);

        Assert.Contains("## Controls", session);
        Assert.Contains(expectedSentence, session);
        Assert.DoesNotContain("## Controls", frozen);
    }

    /* The interpretation follows the raw settings it interprets. */
    [Fact]
    public void BuildSegmentedSystemPrompt_PutsControlsAfterClientEnvironment()
    {
        var chatService = CreateChatService();

        var (_, session, _) = BuildPrompt(
            chatService, clientSettings: "{\"StringData\":{\"Platform\":\"Android\"}}");

        Assert.True(session.IndexOf("## Client Environment", StringComparison.Ordinal)
            < session.IndexOf("## Controls", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildSegmentedSystemPrompt_DescribesLocationsOnlyWithAGame()
    {
        var chatService = CreateChatService();

        var (withGame, _, _) = BuildPrompt(chatService, isGameOn: false, hasGameSnapshot: true);
        var (withoutGame, _, _) = BuildPrompt(chatService, isGameOn: false, hasGameSnapshot: false);

        Assert.Contains("## Describing Locations", withGame);
        Assert.Contains("The player does not see map coordinates", withGame);
        Assert.DoesNotContain("## Describing Locations", withoutGame);
    }

    /* Which key a command has is the Controls section's decision, so the frozen prefix names
       none. */
    [Fact]
    public void BuildSegmentedSystemPrompt_NamesNoKeyForFarLook()
    {
        var chatService = CreateChatService();

        var (frozen, _, _) = BuildPrompt(chatService);

        Assert.DoesNotContain("far look with ';'", frozen);
    }

    [Fact]
    public void BuildSegmentedSystemPrompt_CarriesTheElberethRule_InSpoilerFreeMode()
    {
        var chatService = CreateChatService();

        var (frozen, _, _) = BuildPrompt(chatService, spoilerFreeMode: true, overseerMode: 0);

        Assert.Contains("**Elbereth IS a spoiler**", frozen);
        Assert.Contains("even when the player asks about it by name", frozen);
        Assert.Contains("**Asking is not permission.**", frozen);
        Assert.Contains("Elbereth allows no hint.", frozen);

        // The detailed policy is served from the copy of ToolGuides in the test output.
        int policyIndex = frozen.IndexOf("### Detailed Spoiler Policy", StringComparison.Ordinal);
        Assert.True(policyIndex >= 0);
        Assert.True(frozen.IndexOf("## Elbereth", policyIndex, StringComparison.Ordinal) > policyIndex);
        Assert.True(frozen.IndexOf("## Asking Is Not Permission", policyIndex, StringComparison.Ordinal) > policyIndex);
    }

    /* SECTION 12 is gated on spoiler-free mode and the Overseer mode alone, so a session with
       no game still carries the rule — which is what makes "no snapshot means not learned"
       work. */
    [Fact]
    public void BuildSegmentedSystemPrompt_CarriesTheElberethRule_WithNoGame()
    {
        var chatService = CreateChatService();

        var (frozen, _, _) = BuildPrompt(
            chatService, spoilerFreeMode: true, isGameOn: false, overseerMode: 1, hasGameSnapshot: false);

        Assert.Contains("**Elbereth IS a spoiler**", frozen);
        Assert.Contains("with no game snapshot in this conversation", frozen);
        Assert.Contains("**Asking is not permission.**", frozen);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    public void BuildSegmentedSystemPrompt_OmitsTheElberethRule_WhenSpoilerControlIsOff(
        bool spoilerFreeMode, int overseerMode)
    {
        var chatService = CreateChatService();

        var (frozen, _, _) = BuildPrompt(chatService, spoilerFreeMode: spoilerFreeMode, overseerMode: overseerMode);

        Assert.DoesNotContain("Elbereth", frozen);

        // The asking-is-not-permission bullet never says "Elbereth", so it needs its own check.
        Assert.DoesNotContain("**Asking is not permission.**", frozen);
    }

    [Fact]
    public void ToolRegistry_SortsHandlersDeterministicallyByToolName()
    {
        var dummyHandlerZ = new DummyToolHandler("z_tool");
        var dummyHandlerA = new DummyToolHandler("a_tool");
        var dummyHandlerM = new DummyToolHandler("m_tool");

        var handlers = new List<IToolHandler> { dummyHandlerZ, dummyHandlerA, dummyHandlerM };
        var registry = new ToolRegistry(
            handlers,
            new DummyClientToolBridge(),
            NullLogger<ToolRegistry>.Instance);

        var provider = new OpenAiResponsesProvider(new ConfigurationBuilder().Build());
        var tools = registry.BuildToolsForRequest(provider, new ToolExecutionContext(), false, true, false, false);
        var declarations = tools.FunctionDeclarations;
        Assert.Equal(3, declarations.Count);

        // Verify sorted order: a_tool, m_tool, z_tool
        var name0 = ProviderHelper.GetProperty(declarations[0], "name")?.ToString();
        var name1 = ProviderHelper.GetProperty(declarations[1], "name")?.ToString();
        var name2 = ProviderHelper.GetProperty(declarations[2], "name")?.ToString();

        Assert.Equal("a_tool", name0);
        Assert.Equal("m_tool", name1);
        Assert.Equal("z_tool", name2);
    }

    [Fact]
    public void OpenAiProvider_BuildChatRequestBody_IncludesPromptCacheKey()
    {
        var inMemorySettings = new Dictionary<string, string?>
        {
            { "PromptCacheSettings:EnableOpenAiPromptCacheKey", "true" }
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemorySettings).Build();
        var provider = new OpenAiResponsesProvider(config);

        var history = new List<object>
        {
            provider.FormatMessage("user", "Hello", null)
        };

        var requestBody = provider.BuildChatRequestBody(
            "gpt-4o",
            history,
            1024,
            null,
            new ToolsForRequest(),
            promptCacheKey: "sample_cache_key_12345");

        Assert.True(requestBody.ContainsKey("prompt_cache_key"));
        Assert.Equal("sample_cache_key_12345", requestBody["prompt_cache_key"]);
    }

    [Fact]
    public void OpenAiProvider_BuildChatRequestBody_FallsBackToTheSegmentedPromptForInstructions()
    {
        // This provider builds `instructions` from the history's system messages; Google and
        // Anthropic build theirs from the segments. A caller that carries its prompt only in
        // SegmentedPrompt would otherwise send no system text at all.
        var provider = new OpenAiResponsesProvider(new ConfigurationBuilder().Build());
        var segmentedPrompt = new SegmentedPrompt("Frozen Identity. ", "Session Style. ", "Volatile Context.");

        var requestBody = provider.BuildChatRequestBody(
            "gpt-4o",
            new List<object> { provider.FormatMessage("user", "Hello", null) },
            1024,
            null,
            new ToolsForRequest(),
            segmentedPrompt: segmentedPrompt);

        Assert.Equal("Frozen Identity. Session Style. Volatile Context.", requestBody["instructions"]);
    }

    [Fact]
    public void OpenAiProvider_BuildChatRequestBody_PrefersTheHistorySystemMessageOverTheSegments()
    {
        // ChatService always supplies a system message; the fallback above must not double it or
        // replace it.
        var provider = new OpenAiResponsesProvider(new ConfigurationBuilder().Build());

        var requestBody = provider.BuildChatRequestBody(
            "gpt-4o",
            new List<object>
            {
                new { role = "system", content = "The history's own prompt." },
                provider.FormatMessage("user", "Hello", null)
            },
            1024,
            null,
            new ToolsForRequest(),
            segmentedPrompt: new SegmentedPrompt("Frozen Identity. ", "Session Style. ", string.Empty));

        Assert.Equal("The history's own prompt.", requestBody["instructions"]);
    }

    [Fact]
    public void AnthropicProvider_BuildChatRequestBody_WithSegmentedPrompt_AddsCacheControlBreakpoints()
    {
        var inMemorySettings = new Dictionary<string, string?>
        {
            { "PromptCacheSettings:EnableAnthropicCacheControl", "true" }
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemorySettings).Build();
        var provider = new AnthropicProvider(config);

        var segmentedPrompt = new SegmentedPrompt(
            "Frozen Prefix Identity & Policy",
            "Session Prefix Style",
            "Volatile Wiki Context");

        var rawMessages = new List<object>
        {
            provider.FormatMessage("system", segmentedPrompt.FullPrompt, null),
            provider.FormatMessage("user", "What is my next tactical move?", null)
        };

        var prepared = provider.PrepareMessageHistory(rawMessages);

        var tools = new ToolsForRequest
        {
            FunctionDeclarations = new List<object>
            {
                provider.BuildFunctionDeclaration("tool_a", "First tool", new { }),
                provider.BuildFunctionDeclaration("tool_b", "Second tool", new { })
            }
        };

        var requestBody = provider.BuildChatRequestBody(
            "claude-3-7-sonnet-20250219",
            prepared,
            1024,
            null,
            tools,
            segmentedPrompt: segmentedPrompt);

        // Verify System blocks array
        var systemBlocks = requestBody["system"] as List<object>;
        Assert.NotNull(systemBlocks);
        Assert.Equal(3, systemBlocks.Count);

        // Block 1 (Frozen): has cache_control ephemeral
        Assert.Equal("Frozen Prefix Identity & Policy", ProviderHelper.GetProperty(systemBlocks[0], "text")?.ToString());
        var cc1 = ProviderHelper.GetProperty(systemBlocks[0], "cache_control");
        Assert.NotNull(cc1);
        Assert.Equal("ephemeral", ProviderHelper.GetProperty(cc1, "type")?.ToString());

        // Block 2 (Session): has cache_control ephemeral
        Assert.Equal("Session Prefix Style", ProviderHelper.GetProperty(systemBlocks[1], "text")?.ToString());
        var cc2 = ProviderHelper.GetProperty(systemBlocks[1], "cache_control");
        Assert.NotNull(cc2);
        Assert.Equal("ephemeral", ProviderHelper.GetProperty(cc2, "type")?.ToString());

        // Block 3 (Volatile): NO cache_control
        Assert.Equal("Volatile Wiki Context", ProviderHelper.GetProperty(systemBlocks[2], "text")?.ToString());
        var cc3 = ProviderHelper.GetProperty(systemBlocks[2], "cache_control");
        Assert.Null(cc3);

        // Tools Breakpoint: Last tool (tool_b) has cache_control
        var reqTools = requestBody["tools"] as List<object>;
        Assert.NotNull(reqTools);
        Assert.Equal(2, reqTools.Count);
        var lastToolCc = ProviderHelper.GetProperty(reqTools[1], "cache_control");
        Assert.NotNull(lastToolCc);
        Assert.Equal("ephemeral", ProviderHelper.GetProperty(lastToolCc, "type")?.ToString());

        // Message Tail Breakpoint: Last message has cache_control on its block
        var reqMessages = requestBody["messages"] as List<object>;
        Assert.NotNull(reqMessages);
        var lastMsgContent = ProviderHelper.GetProperty(reqMessages[^1], "content") as List<object>;
        Assert.NotNull(lastMsgContent);
        var lastBlockCc = ProviderHelper.GetProperty(lastMsgContent[^1], "cache_control");
        Assert.NotNull(lastBlockCc);
        Assert.Equal("ephemeral", ProviderHelper.GetProperty(lastBlockCc, "type")?.ToString());
    }

    private static AnthropicProvider CreateAnthropicCacheProvider() =>
        new AnthropicProvider(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            { "PromptCacheSettings:EnableAnthropicCacheControl", "true" }
        }).Build());

    private static Dictionary<string, object> BuildSingleShotAnthropicBody(AnthropicProvider provider, bool? cacheConversationTail)
    {
        var segmentedPrompt = new SegmentedPrompt("Frozen grading preamble", "", "");
        var prepared = provider.PrepareMessageHistory(new List<object>
        {
            provider.FormatMessage("system", segmentedPrompt.FullPrompt, null),
            provider.FormatMessage("user", "Question-specific grading body", null)
        });

        return cacheConversationTail.HasValue
            ? provider.BuildChatRequestBody(
                "claude-3-7-sonnet-20250219", prepared, 1024, null, new ToolsForRequest(),
                segmentedPrompt: segmentedPrompt, cacheConversationTail: cacheConversationTail.Value)
            : provider.BuildChatRequestBody(
                "claude-3-7-sonnet-20250219", prepared, 1024, null, new ToolsForRequest(),
                segmentedPrompt: segmentedPrompt);
    }

    private static int CountCacheControl(Dictionary<string, object> requestBody)
    {
        string json = JsonSerializer.Serialize(requestBody);
        int count = 0;
        for (int i = json.IndexOf("\"cache_control\"", StringComparison.Ordinal); i >= 0;
             i = json.IndexOf("\"cache_control\"", i + 1, StringComparison.Ordinal))
        {
            count++;
        }
        return count;
    }

    [Fact]
    public void AnthropicProvider_BuildChatRequestBody_CacheConversationTailFalse_MarksOnlyTheFrozenBlock()
    {
        var provider = CreateAnthropicCacheProvider();

        var requestBody = BuildSingleShotAnthropicBody(provider, cacheConversationTail: false);

        var systemBlocks = requestBody["system"] as List<object>;
        Assert.NotNull(systemBlocks);
        Assert.Single(systemBlocks);
        Assert.Equal("Frozen grading preamble", ProviderHelper.GetProperty(systemBlocks[0], "text")?.ToString());
        var frozenCc = ProviderHelper.GetProperty(systemBlocks[0], "cache_control");
        Assert.NotNull(frozenCc);
        Assert.Equal("ephemeral", ProviderHelper.GetProperty(frozenCc, "type")?.ToString());

        // No tools, so the frozen block is the request's only breakpoint: the user turn carries none.
        var reqMessages = requestBody["messages"] as List<object>;
        Assert.NotNull(reqMessages);
        if (ProviderHelper.GetProperty(reqMessages[^1], "content") is IEnumerable<object> blocks)
        {
            Assert.All(blocks, b => Assert.Null(ProviderHelper.GetProperty(b, "cache_control")));
        }
        Assert.Equal(1, CountCacheControl(requestBody));
    }

    [Fact]
    public void AnthropicProvider_BuildChatRequestBody_CacheConversationTailDefaultsToTrue()
    {
        Assert.True(new Overseer.Services.Agents.AgentRunRequest().CacheConversationTail);

        var provider = CreateAnthropicCacheProvider();

        var requestBody = BuildSingleShotAnthropicBody(provider, cacheConversationTail: null);

        var reqMessages = requestBody["messages"] as List<object>;
        Assert.NotNull(reqMessages);
        var lastMsgContent = ProviderHelper.GetProperty(reqMessages[^1], "content") as List<object>;
        Assert.NotNull(lastMsgContent);
        var tailCc = ProviderHelper.GetProperty(lastMsgContent[^1], "cache_control");
        Assert.NotNull(tailCc);
        Assert.Equal("ephemeral", ProviderHelper.GetProperty(tailCc, "type")?.ToString());
        Assert.Equal(2, CountCacheControl(requestBody));
    }

    [Fact]
    public void BenchmarkAssessmentPrompt_PreambleAndBody_ReassembleTheFullPromptsByteForByte()
    {
        const string suite = "Split Suite";
        const string question = "Which QX-UNIQUE-QUESTION altar is safest?";
        const string rubric = "RB-UNIQUE-RUBRIC point one.";
        const string answer = "AN-UNIQUE-ANSWER text.";
        var tools = new[] { "wiki_search", "source_code_search" };
        string nl = Environment.NewLine;

        string preamble = BenchmarkAssessmentPrompt.BuildPerQuestionPreamble(suite);
        string? boardBlock = BenchmarkAssessmentPrompt.BuildGradingBoardBlock("BD-UNIQUE-BOARD", "BT-UNIQUE-BOARD text");
        Assert.NotNull(boardBlock);
        string body = BenchmarkAssessmentPrompt.BuildPerQuestionBody(
            7, question, BenchmarkDifficulty.Advanced, rubric, answer, BenchmarkAnswerStatus.Ok,
            tools, 5, true, 2, 45, boardGivenAbove: true);
        string full = BenchmarkAssessmentPrompt.BuildPerQuestionPrompt(
            suite, 7, question, BenchmarkDifficulty.Advanced, rubric, answer, BenchmarkAnswerStatus.Ok,
            tools, 5, true, 2, 45, "BD-UNIQUE-BOARD", "BT-UNIQUE-BOARD text");

        // The order a grader reads: the preamble, the board, then the question's body.
        Assert.Equal(preamble + nl + boardBlock + nl + body, full);

        // The seams are the blank lines after the unverified-claims section and after the board.
        Assert.StartsWith("You are an expert game knowledge and reasoning assessor", preamble);
        Assert.EndsWith("and the claim verifier checks it against the source." + nl, preamble);
        Assert.StartsWith(BenchmarkAssessmentPrompt.GradingBoardHeading + nl, boardBlock);
        Assert.EndsWith("--- END GAME CONTEXT BOARD ---" + nl, boardBlock);
        Assert.StartsWith("--- QUESTION AND CANDIDATE ANSWER ---" + nl, body);
        Assert.Contains("against the source." + nl + nl + BenchmarkAssessmentPrompt.GradingBoardHeading + nl, full);
        Assert.Contains("--- END GAME CONTEXT BOARD ---" + nl + nl + "--- QUESTION AND CANDIDATE ANSWER ---" + nl, full);

        // The preamble carries the suite and nothing question-specific; the body carries none of the
        // preamble and none of the board, only the line that points at it.
        Assert.Contains($"Suite: {suite}" + nl, preamble);
        Assert.Equal(preamble, BenchmarkAssessmentPrompt.BuildPerQuestionPreamble(suite));
        foreach (var marker in new[] { "QX-UNIQUE-QUESTION", "RB-UNIQUE-RUBRIC", "AN-UNIQUE-ANSWER", "Question #7" })
        {
            Assert.DoesNotContain(marker, preamble);
            Assert.DoesNotContain(marker, boardBlock);
            Assert.Contains(marker, body);
        }
        foreach (var marker in new[] { "BD-UNIQUE-BOARD", "BT-UNIQUE-BOARD" })
        {
            Assert.DoesNotContain(marker, preamble);
            Assert.DoesNotContain(marker, body);
            Assert.Contains(marker, boardBlock);
        }
        Assert.Contains(BenchmarkAssessmentPrompt.BoardGivenAboveLine, body);
        Assert.DoesNotContain("CRITICAL INSTRUCTIONS:", body);
        Assert.DoesNotContain("--- SCORING DIMENSIONS (BARS 0-6) ---", body);

        string secondOpinionBody = BenchmarkAssessmentPrompt.BuildSecondOpinionBody(
            7, question, BenchmarkDifficulty.Advanced, rubric, answer, BenchmarkAnswerStatus.Ok,
            80, false, "First comment", tools, 5, true, 2, 45, boardGivenAbove: true,
            blind: true, triggerLabel: "BelowThreshold");
        string secondOpinion = BenchmarkAssessmentPrompt.BuildSecondOpinionPrompt(
            suite, 7, question, BenchmarkDifficulty.Advanced, rubric, answer, BenchmarkAnswerStatus.Ok,
            80, false, "First comment", tools, 5, true, 2, 45, "BD-UNIQUE-BOARD", "BT-UNIQUE-BOARD text",
            blind: true, triggerLabel: "BelowThreshold");

        Assert.Equal(preamble + nl + boardBlock + nl + secondOpinionBody, secondOpinion);
        Assert.StartsWith(body + nl, secondOpinionBody);
        Assert.Contains("--- SECOND OPINION ---", secondOpinionBody);
    }

    // --- Grading requests: the board after the instructions and ahead of the question -----------

    private const string GradingBoardText =
        "Dlvl:3  HP:14(14)  Pw:5(5)  AC:7  Xp:2/24  T:512\n"
        + "Inventory:\n"
        + "a - a blessed +1 quarterstaff (weapon in hands)\n"
        + "b - an uncursed hooded cloak (being worn)\n"
        + "c - 3 fortune cookies\n";

    /// <summary>A grading request as the assessor paths build it, with the board and the rubric quoting one of its lines.</summary>
    private static Overseer.Services.Agents.AgentRunRequest GradingRequest(string providerName, bool withBoard)
    {
        string? boardBlock = withBoard ? BenchmarkAssessmentPrompt.BuildGradingBoardBlock("Tommi2", GradingBoardText) : null;
        string body = BenchmarkAssessmentPrompt.BuildPerQuestionBody(
            4, "Which of my items is worth reading first?", BenchmarkDifficulty.Simple,
            "**BOARD FACTS**\n- \"c - 3 fortune cookies\"", "Read the cookies.", BenchmarkAnswerStatus.Ok,
            boardGivenAbove: withBoard);
        var (prompt, seed) = Overseer.Services.Benchmarking.BenchmarkService.BuildGradingPrompt(
            Overseer.Services.Benchmarking.BenchmarkService.GradingSystemPrompt,
            BenchmarkAssessmentPrompt.BuildPerQuestionPreamble("Snapshot Suite"),
            body,
            boardBlock);

        return new Overseer.Services.Agents.AgentRunRequest
        {
            ProviderName = providerName,
            ModelId = "probe",
            SystemPrompt = prompt.FullPrompt,
            SegmentedPrompt = prompt,
            PromptCacheKey = "benchmark:per_question:probe",
            CacheConversationTail = false,
            SeedHistory = seed
        };
    }

    [Fact]
    public void AnthropicProvider_GradingRequest_SendsInstructionsThenBoardAsTwoCachedSystemBlocks()
    {
        var provider = CreateAnthropicCacheProvider();
        var request = GradingRequest("Anthropic", withBoard: true);

        var body = provider.BuildChatRequestBody(
            "claude-opus-5", provider.PrepareMessageHistory(new List<object>(request.SeedHistory)), 1024, null,
            new ToolsForRequest(), segmentedPrompt: request.SegmentedPrompt, cacheConversationTail: false);

        var systemBlocks = body["system"] as List<object>;
        Assert.NotNull(systemBlocks);
        Assert.Equal(2, systemBlocks.Count);
        Assert.Equal(request.SegmentedPrompt!.FrozenPrefix, ProviderHelper.GetProperty(systemBlocks[0], "text")?.ToString());
        Assert.StartsWith(BenchmarkAssessmentPrompt.GradingBoardHeading, ProviderHelper.GetProperty(systemBlocks[1], "text")?.ToString());
        Assert.NotNull(ProviderHelper.GetProperty(systemBlocks[0], "cache_control"));
        Assert.NotNull(ProviderHelper.GetProperty(systemBlocks[1], "cache_control"));

        // Only the two system blocks are breakpoints; the single-shot user turn carries none.
        Assert.Equal(2, CountCacheControl(body));
        string messages = JsonSerializer.Serialize(body["messages"]);
        Assert.DoesNotContain("GAME CONTEXT BOARD (GROUND", messages);
        Assert.Contains("QUESTION AND CANDIDATE ANSWER", messages);
    }

    [Fact]
    public void GoogleProvider_GradingRequest_OrdersInstructionsThenBoardInSystemInstruction()
    {
        var provider = new GoogleProvider(new ConfigurationBuilder().Build());
        var request = GradingRequest("Google", withBoard: true);

        var body = provider.BuildChatRequestBody(
            "gemini-3.7-flash", provider.PrepareMessageHistory(new List<object>(request.SeedHistory)), 1024, null,
            new ToolsForRequest(), segmentedPrompt: request.SegmentedPrompt);

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(body));
        var parts = doc.RootElement.GetProperty("systemInstruction").GetProperty("parts");
        Assert.Equal(2, parts.GetArrayLength());
        Assert.Equal(request.SegmentedPrompt!.FrozenPrefix, parts[0].GetProperty("text").GetString());
        Assert.StartsWith(BenchmarkAssessmentPrompt.GradingBoardHeading, parts[1].GetProperty("text").GetString());
        Assert.DoesNotContain("GAME CONTEXT BOARD (GROUND", doc.RootElement.GetProperty("contents").GetRawText());
    }

    private class DummyToolHandler : IToolHandler
    {
        public string ToolName { get; }
        public string Description { get; set; }
        public ToolExecutionLocation ExecutionLocation => ToolExecutionLocation.Server;
        public ToolCategory Category => ToolCategory.InformationRetrieval;
        public JsonElement ParameterSchema => JsonDocument.Parse("{\"type\":\"object\"}").RootElement;

        public DummyToolHandler(string toolName)
        {
            ToolName = toolName;
            Description = $"Description for {toolName}";
        }

        public Task<ToolResult> ExecuteAsync(JsonElement parameters, ToolExecutionContext context, System.Threading.CancellationToken cancellationToken)
        {
            return Task.FromResult(new ToolResult { Success = true, Content = "ok" });
        }
    }

    [Fact]
    public void GoogleProvider_BuildChatRequestBody_WithSegmentedPrompt_OrdersSystemPartsBySegment()
    {
        // Gemini has no per-block cache_control and no prompt_cache_key. Its implicit cache keys
        // on a request prefix instead, so the segments are honoured as part order rather than as
        // breakpoints, and everything stable is serialized before the turn list.
        var provider = new GoogleProvider(new ConfigurationBuilder().Build());

        var segmentedPrompt = new SegmentedPrompt(
            "Frozen Prefix Identity & Policy",
            "Session Prefix Style",
            "Volatile Wiki Context");

        var prepared = provider.PrepareMessageHistory(new List<object>
        {
            provider.FormatMessage("system", segmentedPrompt.FullPrompt, null),
            provider.FormatMessage("user", "What is my next tactical move?", null)
        });

        var tools = new ToolsForRequest
        {
            FunctionDeclarations = new List<object>
            {
                provider.BuildFunctionDeclaration("tool_a", "First tool", new { }),
                provider.BuildFunctionDeclaration("tool_b", "Second tool", new { })
            }
        };

        var requestBody = provider.BuildChatRequestBody(
            "gemini-3.7-flash",
            prepared,
            1024,
            null,
            tools,
            segmentedPrompt: segmentedPrompt,
            promptCacheKey: "sample_cache_key_12345");

        string json = JsonSerializer.Serialize(requestBody);

        // The ampersand is & on the wire: System.Text.Json's default encoder escapes it, and
        // a prefix test asserts the bytes rather than the characters they stand for.
        Assert.Contains(
            "\"systemInstruction\":{\"parts\":[{\"text\":\"Frozen Prefix Identity \\u0026 Policy\"},{\"text\":\"Session Prefix Style\"},{\"text\":\"Volatile Wiki Context\"}]}",
            json);

        // Serialization follows key insertion order, and `contents` is inserted last precisely so
        // that the stable material can be a prefix.
        Assert.True(json.IndexOf("\"systemInstruction\"", StringComparison.Ordinal) < json.IndexOf("\"contents\"", StringComparison.Ordinal));
        Assert.True(json.IndexOf("\"tools\"", StringComparison.Ordinal) < json.IndexOf("\"contents\"", StringComparison.Ordinal));
        Assert.True(json.IndexOf("\"toolConfig\"", StringComparison.Ordinal) < json.IndexOf("\"contents\"", StringComparison.Ordinal));
    }

    [Fact]
    public void GoogleProvider_BuildChatRequestBody_OrdersSafetySettingsDeterministically()
    {
        // Configuration section order is not a contract, and a set that reorders between two
        // requests moves the byte where the prefix stops matching.
        var forward = new Dictionary<string, string?>
        {
            { "SafetySettings:Gemini:HARM_CATEGORY_HARASSMENT", "BLOCK_NONE" },
            { "SafetySettings:Gemini:HARM_CATEGORY_DANGEROUS_CONTENT", "BLOCK_NONE" }
        };
        var reversed = new Dictionary<string, string?>
        {
            { "SafetySettings:Gemini:HARM_CATEGORY_DANGEROUS_CONTENT", "BLOCK_NONE" },
            { "SafetySettings:Gemini:HARM_CATEGORY_HARASSMENT", "BLOCK_NONE" }
        };

        static string Body(Dictionary<string, string?> settings)
        {
            var provider = new GoogleProvider(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
            var prepared = provider.PrepareMessageHistory(new List<object>
            {
                provider.FormatMessage("user", "hi", null)
            });

            return JsonSerializer.Serialize(provider.BuildChatRequestBody(
                "gemini-3.7-flash", prepared, 1024, null, new ToolsForRequest()));
        }

        Assert.Equal(Body(forward), Body(reversed));
        Assert.Contains("HARM_CATEGORY_DANGEROUS_CONTENT", Body(forward));
    }
}
