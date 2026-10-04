using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using MobileGnollHackLogger.Data;
using Overseer.Services.Agents;
using Overseer.Services.Benchmarking;
using Overseer.Services.Providers;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// The claim verifier's parse retry request and the description of how a verification loop ended.
/// Nothing here reaches a network.
/// </summary>
public class BenchmarkClaimVerificationRetryTests
{
    private const string BoardText =
        "Dlvl:3  HP:14(14)  Pw:5(5)  AC:7  Xp:2/24  T:512\n"
        + "Inventory:\n"
        + "a - a blessed +1 quarterstaff (weapon in hands)\n"
        + "b - an uncursed hooded cloak (being worn)\n"
        + "c - 3 fortune cookies\n";

    private const string Rubric = "**BOARD FACTS**\n- \"c - 3 fortune cookies\"\n**REQUIRED**\n- Read the cookies.";

    private const string EmptyReAsk =
        "Your previous response was empty. From the evidence above, output ONLY the JSON object the schema above requires, with a verdict for every item. Do not call tools.";

    private static SystemAiApiConfiguration VerifierConfig(string provider = "Anthropic") => new()
    {
        Id = 5,
        Provider = provider,
        ModelId = "verifier-model",
        DisplayName = "Verifier",
        ThinkingLevel = "high",
        ReasoningMode = "adaptive",
        ReasoningSummary = "auto",
        ServiceTier = "priority"
    };

    private static AgentRunRequest FirstRequest(string prompt, string provider = "Anthropic")
        => BenchmarkService.BuildClaimVerificationRequest(
            VerifierConfig(provider), "test-key", AiEndpointDescriptor.Official, prompt,
            new List<string> { "source_code_search", "wiki_search" },
            maxOutputTokens: 16000, toolIterations: 8, totalModelCalls: 12, toolCallBudget: 15,
            maxResultLength: 10000, runId: 54, orderIndex: 4, startedByUserId: "user-1");

    private static ChatMessageToolCall Call(string name, string args, string? result, string status = "completed")
        => new() { Name = name, ArgsText = args, Result = result, Status = status };

    private static string SeedText(AgentRunRequest request)
    {
        var message = Assert.Single(request.SeedHistory);
        Assert.Equal("user", (string?)message.GetType().GetProperty("role")!.GetValue(message));
        return (string)message.GetType().GetProperty("content")!.GetValue(message)!;
    }

    [Fact]
    public void RetryRequest_HasAFreshBudgetOfTwo_ToolsOff_AndTheFirstRequestsModelFields()
    {
        var first = FirstRequest("PROMPT");
        first.Budget!.TryIncrementModelCall();

        var retry = BenchmarkService.BuildClaimVerificationRetryRequest(
            first, "PROMPT", new List<ChatMessageToolCall>(), "not json", "bad", 40000);

        Assert.NotSame(first.Budget, retry.Budget);
        Assert.Equal(2, retry.Budget!.MaxTotalModelCalls);
        Assert.Equal(0, retry.Budget.TotalModelCalls);
        Assert.False(retry.EnableToolUse);
        Assert.Equal(1, retry.MaxToolIterations);

        Assert.Equal(first.ProviderName, retry.ProviderName);
        Assert.Equal(first.ModelId, retry.ModelId);
        Assert.Equal(first.ApiKey, retry.ApiKey);
        Assert.Same(first.Endpoint, retry.Endpoint);
        Assert.Equal(first.ModelDisplayName, retry.ModelDisplayName);
        Assert.Equal(first.SystemPrompt, retry.SystemPrompt);
        Assert.Equal(first.ThinkingLevel, retry.ThinkingLevel);
        Assert.Equal(first.ReasoningMode, retry.ReasoningMode);
        Assert.Equal(first.ReasoningSummary, retry.ReasoningSummary);
        Assert.Equal(first.ServiceTier, retry.ServiceTier);
        Assert.Equal(first.MaxOutputTokens, retry.MaxOutputTokens);
        Assert.Equal(first.SystemModelId, retry.SystemModelId);
        Assert.Same(first.ToolExecutionContext, retry.ToolExecutionContext);
    }

    [Fact]
    public void RetryRequest_SeedStartsWithThePrompt_AndListsTheCompletedResultsInOrder()
    {
        var gathered = new List<ChatMessageToolCall>
        {
            Call("wiki_search", "{\"query\":\"fortune cookie\"}", "RESULT-ONE"),
            Call("wiki_view", "{\"page\":\"Broken\"}", null, "error"),
            Call("source_code_search", "{\"query\":\"cookie\"}", "RESULT-TWO"),
            Call("wiki_view", "{\"page\":\"Pending\"}", null)
        };

        var retry = BenchmarkService.BuildClaimVerificationRetryRequest(
            FirstRequest("PROMPT"), "PROMPT", gathered, "not json", "bad", 40000);
        string seed = SeedText(retry);

        Assert.StartsWith("PROMPT\n\nEvidence you already gathered with your tools (each result shortened):\n", seed);
        int one = seed.IndexOf("[1] wiki_search {\"query\":\"fortune cookie\"}\nRESULT-ONE", StringComparison.Ordinal);
        int two = seed.IndexOf("[2] source_code_search {\"query\":\"cookie\"}\nRESULT-TWO", StringComparison.Ordinal);
        Assert.True(one > 0);
        Assert.True(two > one);
        Assert.DoesNotContain("[3]", seed);
        Assert.DoesNotContain("Broken", seed);
        Assert.DoesNotContain("Pending", seed);
        Assert.DoesNotContain("further results omitted", seed);
    }

    [Fact]
    public void RetryRequest_CutsEachResultAtTwoThousandCharacters()
    {
        string result = new string('a', BenchmarkService.ClaimVerificationRetryResultMaxChars) + "TAIL";
        var gathered = new List<ChatMessageToolCall> { Call("wiki_search", "{}", result) };

        var retry = BenchmarkService.BuildClaimVerificationRetryRequest(
            FirstRequest("PROMPT"), "PROMPT", gathered, "not json", "bad", 40000);
        string seed = SeedText(retry);

        Assert.Equal(2000, BenchmarkService.ClaimVerificationRetryResultMaxChars);
        Assert.Contains("[1] wiki_search {}\n" + new string('a', 2000) + "\n", seed);
        Assert.DoesNotContain("TAIL", seed);
    }

    [Fact]
    public void RetryRequest_StopsAtTheEvidenceCap_AndCountsTheOmittedResults()
    {
        var gathered = Enumerable.Range(1, 5)
            .Select(i => Call("wiki_search", "{}", new string((char)('a' + i), 2000)))
            .ToList();

        // Each entry is "\n[k] wiki_search {}\n" plus 2,000 characters and a line break: 2,021.
        var retry = BenchmarkService.BuildClaimVerificationRetryRequest(
            FirstRequest("PROMPT"), "PROMPT", gathered, "not json", "bad", 5000);
        string seed = SeedText(retry);

        Assert.Contains("[1] wiki_search {}", seed);
        Assert.Contains("[2] wiki_search {}", seed);
        Assert.DoesNotContain("[3]", seed);
        Assert.Contains("\n… 3 further results omitted.\n", seed);
    }

    [Fact]
    public void RetryRequest_BlankPreviousText_GetsTheEmptyReAsk()
    {
        var retry = BenchmarkService.BuildClaimVerificationRetryRequest(
            FirstRequest("PROMPT"), "PROMPT", new List<ChatMessageToolCall>(), "  ", "Verification text was empty.", 40000);
        string seed = SeedText(retry);

        Assert.EndsWith("\n" + EmptyReAsk, seed);
        Assert.DoesNotContain("could not be parsed", seed);
    }

    [Fact]
    public void RetryRequest_UnparseablePreviousText_GetsTheParseErrorReAsk()
    {
        var retry = BenchmarkService.BuildClaimVerificationRetryRequest(
            FirstRequest("PROMPT"), "PROMPT", new List<ChatMessageToolCall>(), "not json", "PARSE-ERROR", 40000);
        string seed = SeedText(retry);

        Assert.EndsWith(
            "\nYour previous response could not be parsed: PARSE-ERROR. Output ONLY the raw JSON object according to the schema, without markdown wrapping, code fences or extra text.",
            seed);
        Assert.DoesNotContain(EmptyReAsk, seed);
    }

    [Theory]
    [InlineData("Anthropic")]
    [InlineData("Google")]
    [InlineData("OpenAI")]
    public void RetryRequest_PassesTheBoardDeliveryProbe(string providerName)
    {
        var board = new BenchmarkRunBoardSnapshot { Id = 7, Sha256 = new string('1', 64), SanitizedText = BoardText, CharCount = BoardText.Length };
        var run = new BenchmarkRun
        {
            Id = 54,
            SuiteName = "Snapshot Suite",
            BenchmarkSuite = new BenchmarkSuite { Id = 1, Name = "Snapshot Suite" },
            GameSnapshotNameUsed = "Tommi2",
            GameSnapshotSha256Used = new string('0', 64),
            BoardSnapshotId = board.Id,
            BoardSnapshot = board
        };

        string prompt = BenchmarkClaimVerificationPrompt.BuildPrompt(
            run.SuiteName, 4, "Which of my items is worth reading first?", Rubric,
            new List<string> { "The hero carries 3 fortune cookies." },
            new List<string> { "source_code_search" }, 15,
            boardName: BenchmarkService.RunBoardName(run), boardText: BenchmarkService.RunBoardText(run));
        var gathered = new List<ChatMessageToolCall>
        {
            Call("source_code_search", "{\"query\":\"fortune cookie\"}", "objects.c: FORTUNE_COOKIE")
        };

        var retry = BenchmarkService.BuildClaimVerificationRetryRequest(
            FirstRequest(prompt, providerName), prompt, gathered, string.Empty, "Verification text was empty.", 40000);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "PromptCacheSettings:EnableAnthropicCacheControl", "true" },
                { "PromptCacheSettings:EnableOpenAiPromptCacheKey", "true" }
            })
            .Build();
        IAiProvider provider = providerName switch
        {
            "Google" => new GoogleProvider(config),
            "Anthropic" => new AnthropicProvider(config),
            _ => new OpenAiResponsesProvider(config)
        };

        BenchmarkGradingRequestProbe.Verify(
            provider,
            retry,
            "claim verifier",
            retry.SystemPrompt!,
            BenchmarkClaimVerificationPrompt.BuildBoardBlock(BenchmarkService.RunBoardName(run), BenchmarkService.RunBoardText(run)),
            BenchmarkService.RunBoardText(run),
            BenchmarkClaimVerificationPrompt.QuestionBlockMarker,
            questionNumber: 4);
    }

    [Fact]
    public void DescribeVerificationEnd_PrintsUnknownAndNotReported_ForNulls()
    {
        Assert.Equal(
            "loop ended unknown, provider finish reason not reported",
            BenchmarkService.DescribeVerificationEnd(new AgentRunResult()));
    }

    [Fact]
    public void DescribeVerificationEnd_PrintsBothReasons()
    {
        var result = new AgentRunResult { TerminationReason = "budget_exhausted", ProviderFinishReason = "max_tokens" };

        Assert.Equal(
            "loop ended budget_exhausted, provider finish reason max_tokens",
            BenchmarkService.DescribeVerificationEnd(result));
    }
}
