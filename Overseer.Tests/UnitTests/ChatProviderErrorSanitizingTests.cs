using System.Net;
using Overseer.Services;
using Overseer.Services.Agents;
using Overseer.Services.Providers;
using Overseer.Services.Telemetry;
using Xunit;
using static Overseer.Tests.UnitTests.TurnLimitTestHarness;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// With <see cref="AgentRunRequest.ReportTurnLimits"/> on, no provider error text reaches the answer, an
/// event or a stored fact. A marker in the provider's body shows where it could go.
/// </summary>
public class ChatProviderErrorSanitizingTests
{
    private const string Marker = "SECRET-MARKER-IN-PROVIDER-BODY";

    private static string Body(string code = "bad_thing") =>
        $"{{\"error\":{{\"code\":\"{code}\",\"message\":\"{Marker} echoed from your prompt\"}}}}";

    private static async Task<(AgentRunResult Result, List<ChatEvent> Events)> RunWithStatusAsync(HttpStatusCode status, string body, bool report = true)
    {
        // A real provider's name, because the key-failure and missing-model classifiers go by provider.
        var provider = new ScriptedProvider(_ => new[] { Text("Unused."), Done() }, providerName: "OpenAI");
        var runner = CreateRunner(provider, new ScriptedHandler(_ => Status(status, body)));
        return await RunAsync(runner, Request(provider, guidance: report, report: report));
    }

    private static void AssertNoMarker(AgentRunResult result, IEnumerable<ChatEvent> events)
    {
        Assert.DoesNotContain(Marker, result.FinalText);
        foreach (var evt in events)
        {
            Assert.DoesNotContain(Marker, evt.Data ?? "");
            Assert.DoesNotContain(Marker, evt.Detail ?? "");
        }
    }

    [Fact]
    public async Task TheGenericNon2xxPath_IsAProviderError_AndTheBodyStaysOut()
    {
        var (result, events) = await RunWithStatusAsync(HttpStatusCode.BadRequest, Body());

        var hit = Assert.Single(result.LimitHits);
        Assert.Equal(TurnLimitCodes.ProviderError, hit.Code);
        Assert.Equal(400, hit.HttpStatus);
        Assert.DoesNotContain(events, e => e.Type == "error");
        AssertNoMarker(result, events);
    }

    [Fact]
    public async Task TheBody_IsOnlyInTheLiveFieldTheAdministratorsFactsRead()
    {
        var (result, _) = await RunWithStatusAsync(HttpStatusCode.BadRequest, Body());

        Assert.Contains(Marker, result.LiveProviderErrorBody);

        var facts = new ChatTurnNoticeFacts { HttpStatus = 400, ProviderErrorCode = "bad_thing", SystemKey = true };
        var admin = ChatTurnNoticeDisclosure.For(true, TurnLimitCodes.ProviderError, facts, result.LiveProviderErrorBody);
        var user = ChatTurnNoticeDisclosure.For(false, TurnLimitCodes.ProviderError, facts, result.LiveProviderErrorBody);

        Assert.Contains(Marker, (string)admin!["providerErrorBody"]!);
        Assert.Null(user);
        Assert.DoesNotContain(Marker, ChatTurnNoticeCatalog.SerializeFacts(facts));
    }

    [Fact]
    public async Task AKeyRejection_IsAnApiKeyHit()
    {
        var (result, events) = await RunWithStatusAsync(HttpStatusCode.Unauthorized,
            $"{{\"error\":{{\"code\":\"invalid_api_key\",\"message\":\"{Marker}\"}}}}");

        Assert.Equal(TurnLimitCodes.ApiKeyRejected, Assert.Single(result.LimitHits).Code);
        AssertNoMarker(result, events);
    }

    [Fact]
    public async Task AnEmptyBalance_IsABalanceHit()
    {
        var (result, events) = await RunWithStatusAsync(HttpStatusCode.TooManyRequests,
            $"{{\"error\":{{\"code\":\"insufficient_quota\",\"message\":\"{Marker}\"}}}}");

        Assert.Equal(TurnLimitCodes.ApiKeyBalance, Assert.Single(result.LimitHits).Code);
        AssertNoMarker(result, events);
    }

    [Fact]
    public async Task APassedThroughStreamError_IsAProviderError()
    {
        var provider = new ScriptedProvider(_ => new[]
        {
            new ChatEvent { Type = "error", Data = $"bad request {Marker}", Detail = Marker },
            Done()
        });

        var (result, events) = await RunAsync(CreateRunner(provider), Request(provider));

        Assert.Equal(TurnLimitCodes.ProviderError, Assert.Single(result.LimitHits).Code);
        Assert.DoesNotContain(events, e => e.Type == "error");
        AssertNoMarker(result, events);
    }

    [Fact]
    public async Task ARequestFailure_IsConnectionLost()
    {
        var provider = new ScriptedProvider(_ => new[] { Text("Unused."), Done() });
        var runner = CreateRunner(provider, new ThrowingHandler());

        var (result, events) = await RunAsync(runner, Request(provider));

        Assert.Equal(TurnLimitCodes.ConnectionLost, Assert.Single(result.LimitHits).Code);
        AssertNoMarker(result, events);
    }

    [Fact]
    public async Task ModelNotFound_KeepsItsEvent_WithoutItsDetail()
    {
        var (result, events) = await RunWithStatusAsync(HttpStatusCode.NotFound,
            $"{{\"error\":{{\"code\":\"model_not_found\",\"message\":\"The model does not exist. {Marker}\"}}}}");

        var error = Assert.Single(events, e => e.Type == "error");
        Assert.Equal("model_unavailable", error.ErrorCode);
        Assert.Null(error.Detail);
        Assert.Empty(result.LimitHits);
        AssertNoMarker(result, events);
    }

    [Fact]
    public async Task WithReportingOff_TodaysTextAndTheAttemptEvidenceAreUnchanged()
    {
        var (off, offEvents) = await RunWithStatusAsync(HttpStatusCode.BadRequest, Body(), report: false);
        var (on, _) = await RunWithStatusAsync(HttpStatusCode.BadRequest, Body());

        var error = Assert.Single(offEvents, e => e.Type == "error");
        Assert.Equal($"API Error: 400 - {Body()}", error.Data);
        Assert.Contains(Marker, error.Detail);
        Assert.Null(error.LimitCode);
        Assert.Contains($"**Error:** API Error: 400", off.FinalText);

        var offAttempt = Assert.Single(off.ModelCalls[0].Attempts);
        var onAttempt = Assert.Single(on.ModelCalls[0].Attempts);
        Assert.Equal(offAttempt.Outcome, onAttempt.Outcome);
        Assert.Equal(offAttempt.ProviderErrorCode, onAttempt.ProviderErrorCode);
        Assert.Equal(offAttempt.HttpStatus, onAttempt.HttpStatus);
    }

    [Fact]
    public void AUserOnTheirOwnKey_GetsTheStatusAndCodeButNotTheBody()
    {
        var facts = new ChatTurnNoticeFacts { HttpStatus = 401, ProviderErrorCode = "invalid_api_key", SystemKey = false };

        var disclosed = ChatTurnNoticeDisclosure.For(false, TurnLimitCodes.ApiKeyRejected, facts, Marker);

        Assert.Equal(401, disclosed!["httpStatus"]);
        Assert.Equal("invalid_api_key", disclosed["providerErrorCode"]);
        Assert.False(disclosed.ContainsKey("providerErrorBody"));
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException($"connection refused {Marker}");
    }
}
