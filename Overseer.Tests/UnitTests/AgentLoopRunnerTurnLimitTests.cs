using System.Net;
using Overseer.Services;
using Overseer.Services.Agents;
using Xunit;
using static Overseer.Tests.UnitTests.TurnLimitTestHarness;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// What <see cref="AgentRunRequest.ReportTurnLimits"/> and the recovery call do in <see cref="AgentLoopRunner"/>.
/// </summary>
public class AgentLoopRunnerTurnLimitTests
{
    private static IReadOnlyList<ChatEvent> ToolsThen(int callIndex, params ChatEvent[] final) =>
        callIndex == 0 ? new[] { ToolCall(), Done() } : final;

    [Fact]
    public async Task IterationLimit_RecordsOneHit_AndYieldsNoEvent()
    {
        var provider = new ScriptedProvider(i => i < 2 ? AlwaysTools(i) : new[] { Text("Answer."), Done() });

        var (result, events) = await RunAsync(CreateRunner(provider), Request(provider, maxToolIterations: 2));

        var hit = Assert.Single(result.LimitHits);
        Assert.Equal(TurnLimitCodes.ResearchRounds, hit.Code);
        Assert.Equal(2, hit.LimitValue);
        Assert.Equal(2, hit.ObservedValue);
        Assert.DoesNotContain(events, e => e.Type == "tool_error");
        Assert.Equal("iteration_limit", result.TerminationReason);
    }

    [Fact]
    public async Task IterationLimit_WithNeitherFlag_YieldsTodaysEventAndNoHit()
    {
        var provider = new ScriptedProvider(i => i < 2 ? AlwaysTools(i) : new[] { Text("Answer."), Done() });

        var (result, events) = await RunAsync(CreateRunner(provider), Request(provider, guidance: false, report: false, maxToolIterations: 2));

        Assert.Empty(result.LimitHits);
        Assert.Contains(events, e => e.Type == "tool_error" && e.Data == "Tool call limit reached. Forcing final response.");
        Assert.EndsWith("Answer.", result.FinalText);
    }

    [Fact]
    public async Task ModelCallBudget_RecordsOneHit()
    {
        var provider = new ScriptedProvider(i => i < 2 ? AlwaysTools(i) : new[] { Text("Answer."), Done() });

        var (result, _) = await RunAsync(CreateRunner(provider), Request(provider), new AgentRunBudget { MaxTotalModelCalls = 2 });

        var hit = Assert.Single(result.LimitHits);
        Assert.Equal(TurnLimitCodes.ModelCalls, hit.Code);
        Assert.Equal(2, hit.LimitValue);
    }

    [Fact]
    public async Task ToolBudget_RecordsOneHit_ThoughTwoCallsWereBlocked()
    {
        var provider = new ScriptedProvider(i => i < 3 ? AlwaysTools(i) : new[] { Text("Answer."), Done() });
        var request = Request(provider);
        request.ToolExecutionContext.MaxCallsPerSession = 1;

        var (result, _) = await RunAsync(CreateRunner(provider), request);

        var hit = Assert.Single(result.LimitHits);
        Assert.Equal(TurnLimitCodes.ToolBudget, hit.Code);
        Assert.Equal(2, result.ToolCallsBlocked);
    }

    [Fact]
    public async Task ToolBudgetNote_SaysInThisChat_WithoutAScope_AndForThisQuestion_WithOne()
    {
        var provider = new ScriptedProvider(i => ToolsThen(i, Text("Answer."), Done()));
        var request = Request(provider);
        request.ToolExecutionContext.MaxCallsPerSession = 3;

        await RunAsync(CreateRunner(provider), request);

        Assert.Contains("[Tool budget: 2 of 3 calls remaining in this chat.]", provider.ToolResultsSent[0]);

        var scoped = new ScriptedProvider(i => ToolsThen(i, Text("Answer."), Done()));
        var scopedRequest = Request(scoped);
        scopedRequest.ToolExecutionContext.MaxCallsPerSession = 3;
        scopedRequest.ToolExecutionContext.ToolBudgetScopeId = "bench_1_q1";

        await RunAsync(CreateRunner(scoped), scopedRequest);

        Assert.Contains("[Tool budget: 2 of 3 calls remaining for this question.]", scoped.ToolResultsSent[0]);
    }

    [Fact]
    public async Task ToolOutputCeiling_RecordsOneHit()
    {
        var provider = new ScriptedProvider(i => ToolsThen(i, Text("Answer."), Done()));
        var runner = CreateRunner(provider, toolContent: new string('x', 500),
            settings: new() { ["ToolExecutionLimits:MaxTurnResultLength"] = "100" });

        var (result, _) = await RunAsync(runner, Request(provider));

        var hit = Assert.Single(result.LimitHits);
        Assert.Equal(TurnLimitCodes.ToolOutput, hit.Code);
        Assert.Equal(100, hit.LimitValue);
    }

    [Fact]
    public async Task AnswerLength_RecordsOneHit_InsteadOfTheItalicNote()
    {
        var provider = new ScriptedProvider(_ => new[]
        {
            Text("Cut off"), new ChatEvent { Type = "finish_reason", Data = "max_tokens" }, Done()
        });

        var (result, _) = await RunAsync(CreateRunner(provider), Request(provider));

        Assert.Equal(TurnLimitCodes.AnswerLength, Assert.Single(result.LimitHits).Code);
        Assert.DoesNotContain("Response truncated", result.FinalText);
    }

    [Fact]
    public async Task AnswerLength_WithNeitherFlag_KeepsTheItalicNote()
    {
        var provider = new ScriptedProvider(_ => new[]
        {
            Text("Cut off"), new ChatEvent { Type = "finish_reason", Data = "max_tokens" }, Done()
        });

        var (result, _) = await RunAsync(CreateRunner(provider), Request(provider, guidance: false, report: false));

        Assert.Empty(result.LimitHits);
        Assert.Contains("[Response truncated: output token limit reached.]", result.FinalText);
    }

    [Fact]
    public async Task RateLimitRetriesExhausted_IsABusyHit_AndNoText()
    {
        var provider = new ScriptedProvider(_ => new[] { Text("Unused."), Done() });
        var handler = new ScriptedHandler(_ => Status(HttpStatusCode.TooManyRequests, "{}"));
        var runner = CreateRunner(provider, handler, settings: new() { ["AiRateLimitSettings:Max429RetriesPerCall"] = "0" });

        var (result, events) = await RunAsync(runner, Request(provider));

        var hit = Assert.Single(result.LimitHits);
        Assert.Equal(TurnLimitCodes.ProviderBusy, hit.Code);
        Assert.Equal(429, hit.HttpStatus);
        Assert.DoesNotContain(events, e => e.Type == "error");
        Assert.DoesNotContain("429", result.FinalText);
        Assert.False(result.EndedWithoutFinalAnswer);
    }

    [Fact]
    public async Task RateLimitRetriesExhausted_WithNeitherFlag_YieldsTodaysErrorText()
    {
        var provider = new ScriptedProvider(_ => new[] { Text("Unused."), Done() });
        var handler = new ScriptedHandler(_ => Status(HttpStatusCode.TooManyRequests, "{}"));
        var runner = CreateRunner(provider, handler, settings: new() { ["AiRateLimitSettings:Max429RetriesPerCall"] = "0" });

        var (result, events) = await RunAsync(runner, Request(provider, guidance: false, report: false));

        Assert.Empty(result.LimitHits);
        Assert.Contains(events, e => e.Type == "error" && e.Data == "429 Rate Limited. Max retries (0) exceeded. Please try again later." && e.LimitCode == null);
        Assert.Contains("**Error:** 429 Rate Limited.", result.FinalText);
    }

    /// <summary>Session 20494: tool rounds, then a final call whose stream closes with no completion and no text.</summary>
    [Fact]
    public async Task AFinalStreamThatClosesEarlyWithNoText_GetsOneRecoveryCall_WithNoTools()
    {
        var provider = new ScriptedProvider(i => i switch
        {
            0 => new[] { Text("Let me look."), ToolCall(), Done() },
            1 => new[] { ClosedEarly() },
            _ => new[] { Text("The wiki has 512 pages."), Done() }
        });

        var (result, _) = await RunAsync(CreateRunner(provider), Request(provider));

        Assert.Equal(3, provider.CallCount);
        Assert.Equal(new[] { true, true, false }, provider.ToolsDeclared);
        Assert.True(result.UnfinishedAnswerRecovered);
        Assert.False(result.EndedWithoutFinalAnswer);
        Assert.Equal("completed", result.TerminationReason);
        Assert.Equal(AgentLoopRunner.ForcedFinalInstruction, Assert.Single(provider.UserTexts));
        Assert.Contains("The wiki has 512 pages.", result.FinalText);

        var hit = Assert.Single(result.LimitHits);
        Assert.Equal(TurnLimitCodes.ConnectionLost, hit.Code);
        Assert.True(hit.Recovered);
    }

    [Fact]
    public async Task ARecoveryCallThatAlsoFails_EndsWithoutAFinalAnswer()
    {
        var provider = new ScriptedProvider(i => i == 0 ? new[] { ToolCall(), Done() } : new[] { ClosedEarly() });

        var (result, _) = await RunAsync(CreateRunner(provider), Request(provider));

        Assert.Equal(3, provider.CallCount);
        Assert.True(result.EndedWithoutFinalAnswer);
        Assert.Equal(
            new[] { TurnLimitCodes.ConnectionLost, TurnLimitCodes.NoFinalAnswer },
            result.LimitHits.Select(h => h.Code));
        Assert.All(result.LimitHits, h => Assert.False(h.Recovered));
    }

    [Fact]
    public async Task EndingOnCommentary_GetsOneRecoveryCall_AndTheCommentaryBecomesAThought()
    {
        var provider = new ScriptedProvider(i => i switch
        {
            0 => new[] { ToolCall(), Done() },
            1 => new[] { Text("Still checking the wiki."), Phase("commentary"), Done() },
            _ => new[] { Text("Final answer."), Phase("final_answer"), Done() }
        });

        var (result, _) = await RunAsync(CreateRunner(provider), Request(provider));

        Assert.Equal(3, provider.CallCount);
        Assert.False(provider.ToolsDeclared[2]);
        Assert.Equal("Still checking the wiki.", Assert.Single(provider.AssistantTexts));
        Assert.Contains("<div class=\"ai-thought\">\n\nStill checking the wiki.", result.FinalText);
        Assert.Contains("Final answer.", result.FinalText);
        Assert.Empty(result.LimitHits);
    }

    [Fact]
    public async Task StillOnCommentaryAfterRecovery_EndsWithoutAFinalAnswer_WithTheTextAsAThought()
    {
        var provider = new ScriptedProvider(i => i == 0
            ? new[] { ToolCall(), Done() }
            : new[] { Text("Still checking."), Phase("commentary"), Done() });

        var (result, _) = await RunAsync(CreateRunner(provider), Request(provider));

        Assert.Equal(3, provider.CallCount);
        Assert.True(result.EndedWithoutFinalAnswer);
        Assert.Equal(TurnLimitCodes.NoFinalAnswer, Assert.Single(result.LimitHits).Code);
        Assert.False(Overseer.Services.Telemetry.ChatTurnNoticeCatalog.HasVisibleAnswer(result.FinalText));
    }

    [Fact]
    public async Task NoRecovery_OnFinalAnswer_OrOnAMissingPhaseWithText()
    {
        foreach (var finalEvents in new[]
        {
            new[] { Text("Answer."), Phase("final_answer"), Done() },
            new[] { Text("Answer."), Done() }
        })
        {
            var provider = new ScriptedProvider(i => ToolsThen(i, finalEvents));

            var (result, _) = await RunAsync(CreateRunner(provider), Request(provider));

            Assert.Equal(2, provider.CallCount);
            Assert.False(result.UnfinishedAnswerRecovered);
            Assert.Empty(result.LimitHits);
        }
    }

    [Fact]
    public async Task NoRecovery_OnAToolFreeFirstIteration_AnEmptyOneIsNoFinalAnswer()
    {
        var provider = new ScriptedProvider(_ => new[] { Done() });

        var (result, _) = await RunAsync(CreateRunner(provider), Request(provider));

        Assert.Equal(1, provider.CallCount);
        Assert.False(result.UnfinishedAnswerRecovered);
        Assert.Equal(TurnLimitCodes.NoFinalAnswer, Assert.Single(result.LimitHits).Code);
    }

    [Fact]
    public async Task RecoveryRespectsTheModelCallBudget()
    {
        var provider = new ScriptedProvider(i => i == 0 ? new[] { ToolCall(), Done() } : new[] { Done() });

        var (result, _) = await RunAsync(CreateRunner(provider), Request(provider), new AgentRunBudget { MaxTotalModelCalls = 2 });

        Assert.Equal(2, provider.CallCount);
        Assert.False(result.UnfinishedAnswerRecovered);
        Assert.True(result.EndedWithoutFinalAnswer);
    }

    [Fact]
    public async Task RecoveryRespectsTheIterationLimit()
    {
        // The second response is already the forced final call.
        var provider = new ScriptedProvider(i => i == 0 ? new[] { ToolCall(), Done() } : new[] { Done() });

        var (result, _) = await RunAsync(CreateRunner(provider), Request(provider, maxToolIterations: 1));

        Assert.Equal(2, provider.CallCount);
        Assert.False(result.UnfinishedAnswerRecovered);
        Assert.Single(provider.UserTexts);
    }

    [Fact]
    public async Task NoRecovery_WithoutTurnGuidance()
    {
        var provider = new ScriptedProvider(i => i == 0 ? new[] { ToolCall(), Done() } : new[] { ClosedEarly() });

        var (result, _) = await RunAsync(CreateRunner(provider), Request(provider, guidance: false, report: false));

        Assert.Equal(2, provider.CallCount);
        Assert.False(result.UnfinishedAnswerRecovered);
    }

    [Fact]
    public async Task ATimeoutMidStream_ReturnsNormallyWithThePartialText_WhenReporting()
    {
        using var cts = new CancellationTokenSource();
        var provider = new ScriptedProvider(
            _ => new[] { Text("Partial") },
            afterEvents: async (_, ct) =>
            {
                cts.Cancel();
                await Task.Delay(Timeout.Infinite, ct);
            });

        var (result, _) = await RunAsync(CreateRunner(provider), Request(provider), token: cts.Token);

        Assert.Contains("Partial", result.FinalText);
        Assert.Equal("canceled", result.TerminationReason);
        Assert.Empty(result.LimitHits);
    }

    [Fact]
    public async Task ATimeoutMidStream_Rethrows_WhenNotReporting()
    {
        using var cts = new CancellationTokenSource();
        var provider = new ScriptedProvider(
            _ => new[] { Text("Partial") },
            afterEvents: async (_, ct) =>
            {
                cts.Cancel();
                await Task.Delay(Timeout.Infinite, ct);
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            RunAsync(CreateRunner(provider), Request(provider, guidance: false, report: false), token: cts.Token));
    }

    [Fact]
    public async Task AStreamFailure_IsAConnectionLostHit_AndEndsNormally_WhenReporting()
    {
        var provider = new ScriptedProvider(
            _ => new[] { Text("Partial") },
            afterEvents: (_, _) => throw new IOException("The response ended prematurely."));

        var (result, events) = await RunAsync(CreateRunner(provider), Request(provider));

        Assert.Equal(TurnLimitCodes.ConnectionLost, Assert.Single(result.LimitHits).Code);
        Assert.DoesNotContain(events, e => e.Type == "error");
        Assert.DoesNotContain("prematurely", result.FinalText);
        Assert.Contains("Partial", result.FinalText);
    }
}
