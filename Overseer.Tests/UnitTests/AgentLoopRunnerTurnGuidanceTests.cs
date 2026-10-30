using System.Text.Json;
using Overseer.Services;
using Overseer.Services.Agents;
using Xunit;
using static Overseer.Tests.UnitTests.TurnLimitTestHarness;

namespace Overseer.Tests.UnitTests;

/// <summary>The limit warnings the model receives on its tool results, and only the model.</summary>
public class AgentLoopRunnerTurnGuidanceTests
{
    private const string LastChance = "This is your last chance to use tools";

    private static ScriptedProvider ToolsUntilForced(int toolRounds) =>
        new(i => i < toolRounds ? AlwaysTools(i) : new[] { Text("Answer."), Done() });

    private static int RoundsNoticeOf(string result)
    {
        var match = System.Text.RegularExpressions.Regex.Match(result, @"\[Limit notice: (\d+) more rounds? of tool calls");
        return match.Success ? int.Parse(match.Groups[1].Value) : 0;
    }

    [Fact]
    public async Task With22Iterations_NoticesAppearOnlyAtThreeAndOneRoundsLeft()
    {
        var provider = ToolsUntilForced(22);

        await RunAsync(CreateRunner(provider), Request(provider, maxToolIterations: 22));

        var noticed = provider.ToolResultsSent.Select(RoundsNoticeOf).ToList();
        Assert.Equal(22, noticed.Count);
        Assert.Equal(3, noticed[18]);
        Assert.Equal(1, noticed[20]);
        Assert.Equal(2, noticed.Count(n => n > 0));
        Assert.Contains(LastChance, provider.ToolResultsSent[20]);
    }

    [Fact]
    public async Task With3Iterations_TheFirstNoticeIsAtTwo_AndTheLastChanceAtOne()
    {
        var provider = ToolsUntilForced(3);

        await RunAsync(CreateRunner(provider), Request(provider, maxToolIterations: 3));

        Assert.Equal(new[] { 2, 1, 0 }, provider.ToolResultsSent.Select(RoundsNoticeOf));
        Assert.Contains(LastChance, provider.ToolResultsSent[1]);
    }

    [Fact]
    public async Task TheModelCallBudget_CanMakeTheRoundsLeftSmaller()
    {
        var provider = ToolsUntilForced(3);

        await RunAsync(CreateRunner(provider), Request(provider), new AgentRunBudget { MaxTotalModelCalls = 2 });

        Assert.Contains(LastChance, provider.ToolResultsSent[0]);
    }

    [Theory]
    [InlineData(22, 19, null, 0, 2)]
    [InlineData(22, 0, 3, 1, 2)]
    [InlineData(1, 0, null, 0, 0)]
    public void RoundsLeft_TakesTheSmallerOfBothLimits(int iterations, int current, int? maxCalls, int calls, int expected)
    {
        Assert.Equal(expected, TurnGuidance.RoundsLeft(iterations, current, maxCalls, calls));
    }

    [Fact]
    public void TimeNotices_FireOnceEachAt75And90Percent()
    {
        var state = new TurnGuidanceState();
        var start = new DateTime(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);
        var deadline = start.AddSeconds(1000);

        string? Notice(int seconds) => TurnGuidance.BuildNotices(state, 10, start.AddSeconds(seconds), start, deadline, 0, 100);

        Assert.Null(Notice(700));
        Assert.Equal("[Limit notice: about 250 seconds remain for this question. Finish your research and start your answer soon.]", Notice(750));
        Assert.Null(Notice(800));
        Assert.Contains("about 100 seconds remain", Notice(900));
        Assert.Null(Notice(950));
    }

    [Fact]
    public void AJumpPast90Percent_SendsOneNotice_AndNoSecond()
    {
        var state = new TurnGuidanceState();
        var start = new DateTime(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);
        var deadline = start.AddSeconds(1000);

        Assert.NotNull(TurnGuidance.BuildNotices(state, 10, start.AddSeconds(950), start, deadline, 0, 100));
        Assert.Null(TurnGuidance.BuildNotices(state, 10, start.AddSeconds(960), start, deadline, 0, 100));
    }

    [Fact]
    public void ToolOutputNotice_FiresOnceAt80Percent()
    {
        var state = new TurnGuidanceState();
        var now = DateTime.UtcNow;

        Assert.Null(TurnGuidance.BuildNotices(state, 10, now, now, null, 79, 100));
        Assert.Contains("most of the tool output", TurnGuidance.BuildNotices(state, 10, now, now, null, 80, 100));
        Assert.Null(TurnGuidance.BuildNotices(state, 10, now, now, null, 95, 100));
    }

    [Fact]
    public void SeveralDueNotices_ShareOneBlock()
    {
        var state = new TurnGuidanceState();
        var now = DateTime.UtcNow;

        string? block = TurnGuidance.BuildNotices(state, 1, now, now, null, 90, 100);

        Assert.Contains(LastChance, block);
        Assert.Contains("most of the tool output", block);
    }

    [Fact]
    public async Task ANotice_GoesOnTheLastResultOfTheBatch_AndOnlyThere()
    {
        var provider = new ScriptedProvider(i => i == 0
            ? new[] { ToolCall(1), ToolCall(2), Done() }
            : new[] { Text("Answer."), Done() });

        await RunAsync(CreateRunner(provider), Request(provider, maxToolIterations: 2));

        Assert.Equal(2, provider.ToolResultsSent.Count);
        Assert.DoesNotContain("[Limit notice:", provider.ToolResultsSent[0]);
        Assert.Contains(LastChance, provider.ToolResultsSent[1]);
    }

    [Fact]
    public async Task TheStoredRowAndTheEvent_NeverHoldANotice()
    {
        var provider = ToolsUntilForced(2);

        var (result, events) = await RunAsync(CreateRunner(provider), Request(provider, maxToolIterations: 2));

        Assert.Contains(provider.ToolResultsSent, r => r.Contains("[Limit notice:"));
        Assert.All(result.ToolCalls, tc => Assert.DoesNotContain("[Limit notice:", tc.Result));
        Assert.All(events.Where(e => e.Type == "tool_result"), e => Assert.DoesNotContain("[Limit notice:", e.Data));
    }

    [Fact]
    public async Task NoNotice_WithTurnGuidanceOff()
    {
        var provider = ToolsUntilForced(2);

        await RunAsync(CreateRunner(provider), Request(provider, guidance: false, report: false, maxToolIterations: 2));

        Assert.All(provider.ToolResultsSent, r => Assert.DoesNotContain("[Limit notice:", r));
    }
}
