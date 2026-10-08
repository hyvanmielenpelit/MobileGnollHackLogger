using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using MobileGnollHackLogger.Data;
using Overseer.Services.Agents;
using Overseer.Services.Benchmarking;
using Overseer.Services.Providers;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// The claim verifier's parse retry request, the description of how a verification loop ended,
/// and the per-call usage record that marks the retry's calls. Nothing here reaches a network.
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
        "Your previous response was empty. From the evidence above, output ONLY the JSON object the schema above requires, with a verdict for every item; give an item the evidence does not settle the verdict Indeterminate and say so in its basis. Do not call tools.";

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
            "\nYour previous response could not be parsed: PARSE-ERROR. Output ONLY the raw JSON object according to the schema, with a verdict for every item \u2014 Indeterminate, saying so in its basis, for an item the evidence above does not settle \u2014 without markdown wrapping, code fences or extra text.",
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

    private static TokenUsageReport Usage(int prompt, int cacheRead, int output)
        => new() { TotalPromptTokens = prompt, CacheReadTokens = cacheRead, OutputTokens = output };

    private const string FirstAttemptEnd = "loop ended completed, provider finish reason max_tokens";

    [Fact]
    public void RecordModelCalls_WithoutRetry_WritesPromptCacheAndOutputOnly()
    {
        var answer = new BenchmarkRunAnswer();

        BenchmarkService.RecordClaimVerificationModelCalls(
            answer, 2, new List<TokenUsageReport> { Usage(100, 40, 10), Usage(200, 150, 20) }, null);

        Assert.Equal(2, answer.ClaimVerificationModelCallCount);
        Assert.Equal("[{\"p\":100,\"c\":40,\"o\":10},{\"p\":200,\"c\":150,\"o\":20}]", answer.ClaimVerificationCallUsageJson);
    }

    [Fact]
    public void RecordModelCalls_MarksOnlyTheRetryEntries_AndTheFirstCarriesTheFirstAttemptsEnd()
    {
        var answer = new BenchmarkRunAnswer();

        BenchmarkService.RecordClaimVerificationModelCalls(
            answer, 3, new List<TokenUsageReport> { Usage(100, 40, 10), Usage(200, 150, 20), Usage(300, 0, 30) }, null,
            retryCallStart: 2, firstAttemptEnd: FirstAttemptEnd);

        Assert.Equal(
            "[{\"p\":100,\"c\":40,\"o\":10},{\"p\":200,\"c\":150,\"o\":20},{\"p\":300,\"c\":0,\"o\":30,\"r\":1,\"e\":\"" + FirstAttemptEnd + "\"}]",
            answer.ClaimVerificationCallUsageJson);
    }

    [Fact]
    public void RecordModelCalls_RetryWithoutUsage_AppendsOnePlaceholderEntry()
    {
        var answer = new BenchmarkRunAnswer();

        BenchmarkService.RecordClaimVerificationModelCalls(
            answer, 2, new List<TokenUsageReport> { Usage(100, 40, 10) }, null,
            retryCallStart: 1, firstAttemptEnd: FirstAttemptEnd);

        Assert.Equal(
            "[{\"p\":100,\"c\":40,\"o\":10},{\"p\":0,\"c\":0,\"o\":0,\"r\":1,\"e\":\"" + FirstAttemptEnd + "\"}]",
            answer.ClaimVerificationCallUsageJson);
    }

    [Fact]
    public void RecordModelCalls_NoUsageAndNoRetry_IsNull()
    {
        var answer = new BenchmarkRunAnswer { ClaimVerificationCallUsageJson = "stale" };

        BenchmarkService.RecordClaimVerificationModelCalls(answer, 0, new List<TokenUsageReport>(), null);

        Assert.Null(answer.ClaimVerificationCallUsageJson);
    }

    [Fact]
    public void RecordModelCalls_CutsTheFirstAttemptsEndAtTwoHundredCharacters()
    {
        var answer = new BenchmarkRunAnswer();

        BenchmarkService.RecordClaimVerificationModelCalls(
            answer, 1, new List<TokenUsageReport>(), null,
            retryCallStart: 0, firstAttemptEnd: new string('x', 200) + "TAIL");

        Assert.Equal(200, BenchmarkService.ClaimVerificationFirstAttemptEndMaxLength);
        Assert.Equal(
            "[{\"p\":0,\"c\":0,\"o\":0,\"r\":1,\"e\":\"" + new string('x', 200) + "\"}]",
            answer.ClaimVerificationCallUsageJson);
    }

    [Fact]
    public void RecordModelCalls_TheCapKeepsTheRetryEntries()
    {
        var answer = new BenchmarkRunAnswer();
        var usages = Enumerable.Range(1, 70).Select(i => Usage(i, 0, 1)).ToList();
        usages.Add(Usage(1001, 0, 2));
        usages.Add(Usage(1002, 0, 3));

        BenchmarkService.RecordClaimVerificationModelCalls(
            answer, 72, usages, null, retryCallStart: 70, firstAttemptEnd: FirstAttemptEnd);

        using var doc = System.Text.Json.JsonDocument.Parse(answer.ClaimVerificationCallUsageJson!);
        var entries = doc.RootElement.EnumerateArray().ToList();
        Assert.Equal(BenchmarkService.ClaimVerificationCallUsageMaxEntries, entries.Count);
        Assert.Equal(1, entries[0].GetProperty("p").GetInt32());
        Assert.Equal(62, entries[61].GetProperty("p").GetInt32());
        Assert.False(entries[61].TryGetProperty("r", out _));
        Assert.Equal(1001, entries[62].GetProperty("p").GetInt32());
        Assert.Equal(1, entries[62].GetProperty("r").GetInt32());
        Assert.Equal(FirstAttemptEnd, entries[62].GetProperty("e").GetString());
        Assert.Equal(1002, entries[63].GetProperty("p").GetInt32());
        Assert.Equal(1, entries[63].GetProperty("r").GetInt32());
        Assert.False(entries[63].TryGetProperty("e", out _));
    }

    private const string Google503 =
        "Google stream error: [503] This model is currently experiencing high demand. Spikes in demand are usually temporary. Please try again later.";

    /// <summary>An agent loop result of <paramref name="calls"/> model calls of the given usage each.</summary>
    private static AgentRunResult LoopResult(int calls, int promptTokens, int outputTokens = 10)
    {
        var result = new AgentRunResult
        {
            ModelCallCount = calls,
            TotalPromptTokens = promptTokens * calls,
            OutputTokens = outputTokens * calls
        };
        for (int i = 0; i < calls; i++)
        {
            result.ModelCallUsages.Add(Usage(promptTokens, 0, outputTokens));
        }
        return result;
    }

    /// <summary>Attempts that return <paramref name="script"/> in order, recording each attempt number asked for.</summary>
    private static Func<int, Task<BenchmarkService.ClaimVerificationAttempt>> Scripted(
        List<int> ran, params BenchmarkService.ClaimVerificationAttempt[] script)
        => attemptNumber =>
        {
            ran.Add(attemptNumber);
            return Task.FromResult(script[attemptNumber]);
        };

    [Fact]
    public async Task AProviderError_ThenASuccess_EndsWithoutAnError_AndRecordsBothAttemptsUsage()
    {
        var ran = new List<int>();
        var retried = new List<(int Attempt, string Error)>();

        var attempts = await BenchmarkService.RunClaimVerificationAttemptsAsync(
            Scripted(ran,
                new BenchmarkService.ClaimVerificationAttempt(LoopResult(2, 100), Google503),
                new BenchmarkService.ClaimVerificationAttempt(LoopResult(1, 300), null)),
            providerErrorRetries: 1,
            TimeSpan.Zero,
            CancellationToken.None,
            (attempt, error) => retried.Add((attempt, error)));

        Assert.Equal(new[] { 0, 1 }, ran);
        Assert.Equal(new[] { (1, Google503) }, retried);
        Assert.Null(attempts[attempts.Count - 1].TerminalError);

        var totals = BenchmarkService.TotalClaimVerificationAttempts(attempts);
        Assert.Equal(500, totals.InputTokens);
        Assert.Equal(30, totals.OutputTokens);
        Assert.Equal(3, totals.ModelCallCount);

        var answer = new BenchmarkRunAnswer();
        BenchmarkService.RecordClaimVerificationModelCalls(
            answer, totals.ModelCallCount, totals.CallUsages, totals.ServedServiceTier,
            providerErrorRetries: totals.ProviderErrorRetries);

        Assert.Equal(3, answer.ClaimVerificationModelCallCount);
        Assert.Equal(
            "[{\"p\":100,\"c\":0,\"o\":10},{\"p\":100,\"c\":0,\"o\":10},{\"p\":300,\"c\":0,\"o\":10,\"pe\":1,\"e\":\"" + Google503 + "\"}]",
            answer.ClaimVerificationCallUsageJson);
    }

    [Fact]
    public async Task ADenyListedProviderError_IsNotRetried()
    {
        // The deny list wins over the retryable "503" the same text carries.
        const string invalidRequest = "OpenAI stream error: [invalid_request_error] The request was rejected (503).";
        var ran = new List<int>();

        var attempts = await BenchmarkService.RunClaimVerificationAttemptsAsync(
            Scripted(ran,
                new BenchmarkService.ClaimVerificationAttempt(LoopResult(1, 100), invalidRequest),
                new BenchmarkService.ClaimVerificationAttempt(LoopResult(1, 300), null)),
            providerErrorRetries: 1,
            TimeSpan.Zero,
            CancellationToken.None);

        Assert.Equal(new[] { 0 }, ran);
        Assert.Equal(invalidRequest, Assert.Single(attempts).TerminalError);
    }

    [Fact]
    public async Task ZeroProviderErrorRetries_DisablesTheRetry()
    {
        var ran = new List<int>();

        var attempts = await BenchmarkService.RunClaimVerificationAttemptsAsync(
            Scripted(ran,
                new BenchmarkService.ClaimVerificationAttempt(LoopResult(1, 100), Google503),
                new BenchmarkService.ClaimVerificationAttempt(LoopResult(1, 300), null)),
            providerErrorRetries: 0,
            TimeSpan.Zero,
            CancellationToken.None);

        Assert.Equal(new[] { 0 }, ran);
        Assert.Equal(Google503, Assert.Single(attempts).TerminalError);
    }

    [Fact]
    public async Task ATimedOutAttempt_IsNotRetried_EvenWhenItsTextCarriesARetryableCode()
    {
        var ran = new List<int>();

        var attempts = await BenchmarkService.RunClaimVerificationAttemptsAsync(
            Scripted(ran,
                new BenchmarkService.ClaimVerificationAttempt(LoopResult(1, 100), "Claim verification timeout exceeded (503 s).", TimedOut: true),
                new BenchmarkService.ClaimVerificationAttempt(LoopResult(1, 300), null)),
            providerErrorRetries: 1,
            TimeSpan.Zero,
            CancellationToken.None);

        Assert.Equal(new[] { 0 }, ran);
        Assert.Single(attempts);
    }

    [Fact]
    public async Task ACancellationDuringTheWait_StopsTheRetry()
    {
        using var cts = new CancellationTokenSource();
        var ran = new List<int>();

        Task<BenchmarkService.ClaimVerificationAttempt> RunAttempt(int attemptNumber)
        {
            ran.Add(attemptNumber);
            cts.CancelAfter(TimeSpan.FromMilliseconds(50));
            return Task.FromResult(new BenchmarkService.ClaimVerificationAttempt(LoopResult(1, 100), Google503));
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            BenchmarkService.RunClaimVerificationAttemptsAsync(RunAttempt, 1, TimeSpan.FromSeconds(30), cts.Token));

        Assert.Equal(new[] { 0 }, ran);
    }

    [Fact]
    public void RecordModelCalls_AProviderErrorRepeatWithoutUsage_LeavesOnePlaceholder_BeforeTheParseRetry()
    {
        var answer = new BenchmarkRunAnswer();

        BenchmarkService.RecordClaimVerificationModelCalls(
            answer, 3, new List<TokenUsageReport> { Usage(100, 0, 10), Usage(200, 0, 20) }, null,
            retryCallStart: 1, firstAttemptEnd: FirstAttemptEnd,
            providerErrorRetries: new[] { new BenchmarkService.ClaimVerificationProviderErrorRetry(1, Google503) });

        Assert.Equal(
            "[{\"p\":100,\"c\":0,\"o\":10},{\"p\":0,\"c\":0,\"o\":0,\"pe\":1,\"e\":\"" + Google503 + "\"},{\"p\":200,\"c\":0,\"o\":20,\"r\":1,\"e\":\"" + FirstAttemptEnd + "\"}]",
            answer.ClaimVerificationCallUsageJson);
    }
}
