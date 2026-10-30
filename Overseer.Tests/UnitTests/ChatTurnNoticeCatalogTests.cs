using Overseer.Services.Agents;
using Overseer.Services.Telemetry;
using Xunit;

namespace Overseer.Tests.UnitTests;

public class ChatTurnNoticeCatalogTests
{
    private static readonly string[] UserFacingCodes =
    {
        TurnLimitCodes.NoFinalAnswer, TurnLimitCodes.ResearchRounds, TurnLimitCodes.ModelCalls, TurnLimitCodes.ToolBudget,
        TurnLimitCodes.AnswerLength, TurnLimitCodes.TimeLimit, TurnLimitCodes.ProviderBusy, TurnLimitCodes.ProviderOverloaded,
        TurnLimitCodes.ProviderTimeout, TurnLimitCodes.ProviderError, TurnLimitCodes.ConnectionLost,
        TurnLimitCodes.ApiKeyRejected, TurnLimitCodes.ApiKeyBalance
    };

    [Fact]
    public void EveryUserFacingCode_HasTextWithNoDigits()
    {
        foreach (string code in UserFacingCodes)
        {
            foreach (bool systemKey in new[] { true, false })
            {
                var notice = ChatTurnNoticeCatalog.Render(code, new ChatTurnNoticeFacts { SystemKey = systemKey, LimitValue = 22 }, null, null);

                Assert.NotNull(notice);
                Assert.False(string.IsNullOrWhiteSpace(notice!.Title));
                Assert.False(string.IsNullOrWhiteSpace(notice.Message));
                Assert.False(string.IsNullOrWhiteSpace(notice.Advice));
                Assert.DoesNotMatch("[0-9]", notice.Title + notice.Message + notice.Advice);
            }
        }
    }

    [Fact]
    public void ToolOutput_HasNoUserFacingText_AndIsNeverPrimary()
    {
        Assert.Null(ChatTurnNoticeCatalog.Render(TurnLimitCodes.ToolOutput, new ChatTurnNoticeFacts(), null, null));
        Assert.Null(ChatTurnNoticeCatalog.PrimaryCode(new[] { new TurnLimitHit { Code = TurnLimitCodes.ToolOutput } }));
    }

    [Fact]
    public void ApiKeyText_DependsOnWhoseKeyItIs()
    {
        var own = ChatTurnNoticeCatalog.Render(TurnLimitCodes.ApiKeyRejected, new ChatTurnNoticeFacts { SystemKey = false }, null, null)!;
        var system = ChatTurnNoticeCatalog.Render(TurnLimitCodes.ApiKeyRejected, new ChatTurnNoticeFacts { SystemKey = true }, null, null)!;

        Assert.Contains("your API key", own.Message);
        Assert.Contains("this model's key", system.Message);
        Assert.Equal(ChatTurnNoticeCatalog.ActionNone, own.Action);
    }

    [Theory]
    [InlineData(new[] { TurnLimitCodes.AnswerLength, TurnLimitCodes.ToolBudget }, TurnLimitCodes.ToolBudget)]
    [InlineData(new[] { TurnLimitCodes.NoFinalAnswer, TurnLimitCodes.ConnectionLost }, TurnLimitCodes.ConnectionLost)]
    [InlineData(new[] { TurnLimitCodes.TimeLimit, TurnLimitCodes.ProviderBusy }, TurnLimitCodes.TimeLimit)]
    [InlineData(new[] { TurnLimitCodes.ProviderBusy, TurnLimitCodes.ApiKeyBalance }, TurnLimitCodes.ApiKeyBalance)]
    [InlineData(new[] { TurnLimitCodes.ModelCalls, TurnLimitCodes.ResearchRounds }, TurnLimitCodes.ResearchRounds)]
    public void PrimaryCode_FollowsThePriority(string[] codes, string expected)
    {
        Assert.Equal(expected, ChatTurnNoticeCatalog.PrimaryCode(codes.Select(c => new TurnLimitHit { Code = c })));
    }

    [Fact]
    public void ARecoveredHit_IsNotPrimary()
    {
        Assert.Null(ChatTurnNoticeCatalog.PrimaryCode(new[] { new TurnLimitHit { Code = TurnLimitCodes.ConnectionLost, Recovered = true } }));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("   ", false)]
    [InlineData("\n\n<div class=\"ai-thought\">\n\nThinking\n\n</div>\n\n", false)]
    [InlineData("<div class=\"ai-thought\">\n\nThinking\n\n</div>\n\nAn answer.", true)]
    [InlineData("An answer.", true)]
    public void HasVisibleAnswer_LooksOutsideThoughts(string? content, bool expected)
    {
        Assert.Equal(expected, ChatTurnNoticeCatalog.HasVisibleAnswer(content));
    }

    [Fact]
    public void FactsRoundTrip_AndABrokenValueReadsAsEmpty()
    {
        var facts = new ChatTurnNoticeFacts { Severity = ChatTurnNoticeCatalog.SeverityError, SystemKey = true, LimitValue = 22, ObservedValue = 22 };

        Assert.Equal(facts, ChatTurnNoticeCatalog.ParseFacts(ChatTurnNoticeCatalog.SerializeFacts(facts)));
        Assert.Equal(new ChatTurnNoticeFacts(), ChatTurnNoticeCatalog.ParseFacts("not json"));
        Assert.Equal(new ChatTurnNoticeFacts(), ChatTurnNoticeCatalog.ParseFacts(null));
    }

    [Fact]
    public void Render_ForAMessageViewer_HidesAdminFactsFromARegularUser()
    {
        string factsJson = ChatTurnNoticeCatalog.SerializeFacts(new ChatTurnNoticeFacts { SystemKey = true, LimitValue = 22, ObservedValue = 22 });

        var user = ChatTurnNoticeDisclosure.Render(false, TurnLimitCodes.ResearchRounds, factsJson, 7)!;
        var admin = ChatTurnNoticeDisclosure.Render(true, TurnLimitCodes.ResearchRounds, factsJson, 7)!;

        Assert.Null(user.AdminFacts);
        Assert.Equal(7, user.Reference);
        Assert.Equal(22, admin.AdminFacts!["limitValue"]);
        Assert.Null(ChatTurnNoticeDisclosure.Render(true, null, null, null));
    }
}
