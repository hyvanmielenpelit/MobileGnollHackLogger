using System;
using System.Net.Http;
using System.Threading.Tasks;
using MobileGnollHackLogger.Data;
using Overseer.Services.Telemetry;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// The ledger record of a title-generation attempt, classified from the evidence it left by the same classifier
/// the agent loop uses.
/// </summary>
public class TitleAttemptClassificationTests
{
    private static readonly DateTime Started = new(2026, 10, 7, 9, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void AnOverloaded503ThenA200_IsARetriedOverloadThenOk()
    {
        const string body = """{"type":"error","error":{"type":"overloaded_error","message":"Overloaded"}}""";

        var first = TitleAttemptEvidence.FromErrorResponse("Anthropic", BenchmarkEndpointKind.Official, 503, body,
            rateLimitHeaders: null, retryAfterMs: 1500, modelId: "claude-model", attemptIndex: 0, startedUtc: Started,
            durationMs: 120, retried: true);
        var second = TitleAttemptEvidence.Success("Anthropic", BenchmarkEndpointKind.Official, 200,
            attemptIndex: 1, startedUtc: Started.AddSeconds(2), durationMs: 800);

        Assert.Equal(ProviderAttemptOutcome.ProviderOverloaded, first.Outcome);
        Assert.True(first.Retried);
        Assert.Equal(503, first.HttpStatus);
        Assert.Equal(1500, first.RetryAfterMs);
        Assert.Equal("overloaded_error", first.ProviderErrorCode);
        Assert.Equal(0, first.AttemptIndex);
        Assert.Equal(Started, first.StartedAtUtc);
        Assert.Equal(120, first.DurationMs);

        Assert.Equal(ProviderAttemptOutcome.Ok, second.Outcome);
        Assert.False(second.Retried);
        Assert.Equal(200, second.HttpStatus);
        Assert.Null(second.ProviderErrorCode);
        Assert.Equal(1, second.AttemptIndex);
    }

    [Fact]
    public void AnOpenAi503SlowDown_IsOurRateLimit()
    {
        const string body = """{"error":{"message":"Slow Down: please reduce your request rate.","type":"server_error","code":null}}""";

        var record = TitleAttemptEvidence.FromErrorResponse("OpenAI", BenchmarkEndpointKind.Official, 503, body,
            rateLimitHeaders: null, retryAfterMs: null, modelId: "gpt-model", attemptIndex: 0, startedUtc: Started,
            durationMs: 90, retried: false);

        Assert.Equal(ProviderAttemptOutcome.OurRateLimit, record.Outcome);
        Assert.Equal("server_error", record.ProviderErrorCode);
    }

    [Fact]
    public void A503WithoutSlowDown_FromOpenAi_IsProviderOverloaded()
    {
        const string body = """{"error":{"message":"The server is temporarily unavailable.","type":"server_error"}}""";

        var record = TitleAttemptEvidence.FromErrorResponse("OpenAI", BenchmarkEndpointKind.Official, 503, body,
            null, null, "gpt-model", 0, Started, 90, retried: false);

        Assert.Equal(ProviderAttemptOutcome.ProviderOverloaded, record.Outcome);
    }

    [Fact]
    public void A401_IsOurKey()
    {
        const string body = """{"type":"error","error":{"type":"authentication_error","message":"invalid x-api-key"}}""";

        var record = TitleAttemptEvidence.FromErrorResponse("Anthropic", BenchmarkEndpointKind.Official, 401, body,
            null, null, "claude-model", 0, Started, 40, retried: false);

        Assert.Equal(ProviderAttemptOutcome.OurKey, record.Outcome);
        Assert.Equal(401, record.HttpStatus);
        Assert.Equal("authentication_error", record.ProviderErrorCode);
    }

    [Fact]
    public void AGovernorRefusalBeforeSend_IsOurThrottle()
    {
        var record = TitleAttemptEvidence.BeforeSend("Google", BenchmarkEndpointKind.Official, governorRefused: true,
            attemptIndex: 0, startedUtc: Started, durationMs: 30_000);

        Assert.Equal(ProviderAttemptOutcome.OurThrottle, record.Outcome);
        Assert.Null(record.HttpStatus);
        Assert.Null(record.ProviderErrorCode);
        Assert.False(record.Retried);
    }

    [Fact]
    public void ACancelBeforeSend_IsOurCanceled()
    {
        var record = TitleAttemptEvidence.BeforeSend("Google", BenchmarkEndpointKind.Custom, governorRefused: false,
            attemptIndex: 0, startedUtc: Started, durationMs: 5);

        Assert.Equal(ProviderAttemptOutcome.OurCanceled, record.Outcome);
        Assert.Equal(BenchmarkEndpointKind.Custom, record.EndpointKind);
    }

    [Fact]
    public void ACallerCancelWhileTheRequestWasOut_IsOurCanceled()
    {
        var record = TitleAttemptEvidence.FromException("OpenAI", BenchmarkEndpointKind.Official, new TaskCanceledException(),
            callerCanceled: true, attemptIndex: 0, startedUtc: Started, durationMs: 200, retried: false);

        Assert.Equal(ProviderAttemptOutcome.OurCanceled, record.Outcome);
    }

    [Fact]
    public void AnExceptionWithoutACancel_IsNeitherOkNorOurCancel()
    {
        var record = TitleAttemptEvidence.FromException("OpenAI", BenchmarkEndpointKind.Official,
            new HttpRequestException("The connection was reset."), callerCanceled: false, attemptIndex: 1,
            startedUtc: Started, durationMs: 200, retried: true);

        Assert.NotEqual(ProviderAttemptOutcome.Ok, record.Outcome);
        Assert.NotEqual(ProviderAttemptOutcome.OurCanceled, record.Outcome);
        // A transport failure that cannot be pinned on either side.
        Assert.Equal(ProviderAttemptOutcome.TransportUnattributed, record.Outcome);
        Assert.True(record.Retried);
        Assert.Null(record.HttpStatus);
    }
}
