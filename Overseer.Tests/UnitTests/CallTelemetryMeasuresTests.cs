namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using MobileGnollHackLogger.Data;
using Overseer.Services.Telemetry;
using Xunit;

public class CallTelemetryMeasuresTests
{
    private static readonly DateTime TurnStart = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);

    private static ModelCallTelemetry CandidateCall(
        int callIndex,
        int startOffsetMs,
        int permitWaitMs = 0,
        int backoffWaitMs = 0,
        int failedAttemptMs = 0,
        int? firstOutputMs = null,
        string provider = "OpenAI")
    {
        return new ModelCallTelemetry
        {
            Source = ModelCallSource.BenchmarkCandidate,
            Provider = provider,
            RequestedModelId = "model-a",
            CallIndex = callIndex,
            StartedAtUtc = TurnStart.AddMilliseconds(startOffsetMs),
            PermitWaitMs = permitWaitMs,
            BackoffWaitMs = backoffWaitMs,
            FailedAttemptMs = failedAttemptMs,
            FirstOutputMs = firstOutputMs,
            AttemptCount = 1,
        };
    }

    private static BenchmarkRunAnswer Answer(long? permitWaitMs, long? backoffWaitMs, params ModelCallTelemetry[] calls)
    {
        return new BenchmarkRunAnswer
        {
            OrderIndex = 1,
            QuestionText = "Q1",
            Status = BenchmarkAnswerStatus.Ok,
            StartedAtUtc = TurnStart,
            DurationMs = 10_000,
            ToolTimeMs = 0,
            PermitWaitMs = permitWaitMs,
            BackoffWaitMs = backoffWaitMs,
            ModelCalls = new List<ModelCallTelemetry>(calls),
        };
    }

    [Fact]
    public void TimeToFirstAnswerText_IsTheSpanToTheFinalCallsFirstOutput_LessEveryWaitOfTheTurn()
    {
        // Call 0 waits 2 s for a permit. Call 1 starts at 4 s, waits 1 s for a permit, backs off
        // 1 s, spends 0.5 s on a failed attempt, and shows output 1.5 s after its successful send:
        // its first output is at 4 + 1 + 1 + 0.5 + 1.5 = 8 s. The turn's waits are 2 + 1 permit and
        // 1 backoff, 4 s in all, so the net time to first answer text is 8 − 4 = 4 s.
        var answer = Answer(
            permitWaitMs: 3_000,
            backoffWaitMs: 1_000,
            CandidateCall(0, 0, permitWaitMs: 2_000, firstOutputMs: 500),
            CandidateCall(1, 4_000, permitWaitMs: 1_000, backoffWaitMs: 1_000, failedAttemptMs: 500, firstOutputMs: 1_500));

        Assert.Equal(4_000L, CallTelemetryMeasures.TimeToFirstAnswerTextMs(answer));
    }

    [Fact]
    public void TimeToFirstAnswerText_UsesTheHighestCallIndex_AndIgnoresGraderRows()
    {
        var grader = CandidateCall(5, 60_000, firstOutputMs: 100);
        grader.Source = ModelCallSource.BenchmarkGrader;
        grader.GraderRole = ModelCallGraderRole.Assessor;
        var answer = Answer(
            permitWaitMs: 0,
            backoffWaitMs: 0,
            CandidateCall(1, 3_000, firstOutputMs: 700),
            grader,
            CandidateCall(0, 0, firstOutputMs: 400));

        Assert.Equal(3_700L, CallTelemetryMeasures.TimeToFirstAnswerTextMs(answer));
        Assert.Equal(1, CallTelemetryMeasures.FinalCandidateCall(answer)!.CallIndex);
        Assert.Equal(2, CallTelemetryMeasures.CandidateCalls(answer).Count);
    }

    [Fact]
    public void TimeToFirstAnswerText_IsNull_WithoutTurnStartCandidateCallOrFirstOutput()
    {
        var noStart = Answer(0, 0, CandidateCall(0, 0, firstOutputMs: 500));
        noStart.StartedAtUtc = null;
        Assert.Null(CallTelemetryMeasures.TimeToFirstAnswerTextMs(noStart));

        Assert.Null(CallTelemetryMeasures.TimeToFirstAnswerTextMs(Answer(0, 0)));
        Assert.Null(CallTelemetryMeasures.TimeToFirstAnswerTextMs(Answer(0, 0, CandidateCall(0, 0, firstOutputMs: null))));
    }

    [Fact]
    public void TimeToFirstAnswerText_IsFlooredAtZero()
    {
        // Answer totals larger than the span they sit in: inconsistent marks never go negative.
        var answer = Answer(5_000, 0, CandidateCall(0, 0, firstOutputMs: 200));

        Assert.Equal(0L, CallTelemetryMeasures.TimeToFirstAnswerTextMs(answer));
    }

    [Fact]
    public void AnswerStreamingRate_ScalesVisibleTokensToTheLast80PercentWindow()
    {
        var call = CandidateCall(0, 0, firstOutputMs: 100);
        call.OutputTokens = 600;
        call.ReasoningTokens = 200;
        call.VisibleOutputChars = 1_600;
        call.Last80VisibleChars = 1_280;
        call.Last80DecodeSpanMs = 4_000;

        // 400 visible tokens × 1280 / 1600 = 320 tokens over 4 s.
        var rate = CallTelemetryMeasures.AnswerStreamingRate(Answer(0, 0, call));

        Assert.NotNull(rate);
        Assert.Equal(80.0, rate!.Value.TokensPerSecond, 9);
        Assert.False(rate.Value.Estimated);
    }

    [Fact]
    public void AnswerStreamingRate_ReadsANullReasoningCountAsZero()
    {
        var call = CandidateCall(0, 0, firstOutputMs: 100);
        call.OutputTokens = 500;
        call.VisibleOutputChars = 2_000;
        call.Last80VisibleChars = 1_600;
        call.Last80DecodeSpanMs = 2_000;

        // 500 × 0.8 = 400 tokens over 2 s.
        Assert.Equal(200.0, CallTelemetryMeasures.AnswerStreamingRate(Answer(0, 0, call))!.Value.TokensPerSecond, 9);
    }

    [Fact]
    public void AnswerStreamingRate_IsEstimatedFromCharacters_ForAnthropic()
    {
        var call = CandidateCall(0, 0, firstOutputMs: 100, provider: "anthropic");
        call.OutputTokens = 5_000; // thinking included, so not used
        call.VisibleOutputChars = 1_600;
        call.Last80VisibleChars = 1_280;
        call.Last80DecodeSpanMs = 4_000;

        // 1280 / 4 = 320 estimated tokens over 4 s.
        var rate = CallTelemetryMeasures.AnswerStreamingRate(Answer(0, 0, call));

        Assert.Equal(80.0, rate!.Value.TokensPerSecond, 9);
        Assert.True(rate.Value.Estimated);
    }

    [Fact]
    public void AnswerStreamingRate_IsNull_WhenASpanCharacterCountOrTokenCountIsMissing()
    {
        ModelCallTelemetry Complete()
        {
            var call = CandidateCall(0, 0, firstOutputMs: 100);
            call.OutputTokens = 600;
            call.VisibleOutputChars = 1_600;
            call.Last80VisibleChars = 1_280;
            call.Last80DecodeSpanMs = 4_000;
            return call;
        }

        var noSpan = Complete(); noSpan.Last80DecodeSpanMs = null;
        var zeroSpan = Complete(); zeroSpan.Last80DecodeSpanMs = 0;
        var noChars = Complete(); noChars.VisibleOutputChars = 0;
        var noWindowChars = Complete(); noWindowChars.Last80VisibleChars = null;
        var noTokens = Complete(); noTokens.OutputTokens = null;
        var allReasoning = Complete(); allReasoning.ReasoningTokens = 600;

        foreach (var call in new[] { noSpan, zeroSpan, noChars, noWindowChars, noTokens, allReasoning })
        {
            Assert.Null(CallTelemetryMeasures.AnswerStreamingRate(Answer(0, 0, call)));
        }

        Assert.Null(CallTelemetryMeasures.AnswerStreamingRate(Answer(0, 0)));
    }

    private static ModelCallTelemetry StreamedCall(int spanMs, string provider = "OpenAI")
    {
        var call = CandidateCall(0, 0, firstOutputMs: 100, provider: provider);
        call.OutputTokens = 500;
        call.VisibleOutputChars = 2_000;
        call.Last80VisibleChars = 1_600;
        call.Last80DecodeSpanMs = spanMs;
        return call;
    }

    [Theory]
    [InlineData("OpenAI")]
    [InlineData("Anthropic")]
    [InlineData("Google")]
    public void AStreamingRateOverASpanShorterThanTheMinimum_IsNullAndUnmeasurable(string provider)
    {
        // 400 window tokens over 0.2 s: the visible text arrived in one burst after thinking.
        var answer = Answer(0, 0, StreamedCall(CallTelemetryMeasures.MinMeasurableDecodeSpanMs - 300, provider));

        Assert.Null(CallTelemetryMeasures.AnswerStreamingRate(answer));
        Assert.True(CallTelemetryMeasures.IsStreamingRateUnmeasurable(answer));
    }

    [Fact]
    public void AZeroSpanWithVisibleCharacters_IsUnmeasurable()
    {
        var answer = Answer(0, 0, StreamedCall(0));

        Assert.Null(CallTelemetryMeasures.AnswerStreamingRate(answer));
        Assert.True(CallTelemetryMeasures.IsStreamingRateUnmeasurable(answer));
    }

    [Theory]
    [InlineData("OpenAI")]
    [InlineData("Anthropic")]
    public void AStreamingRateAboveThePlausibleMaximum_IsNullAndUnmeasurable(string provider)
    {
        // OpenAI: 400 window tokens over 0.6 s = 667 tokens/s is plausible; with 2,000 output tokens,
        // 1,600 over 0.6 s = 2,667 tokens/s is not. Anthropic: 1,600 / 4 = 400 estimated tokens, so a
        // 0.6 s span is plausible and the window characters are raised to exceed the bound.
        var call = StreamedCall(600, provider);
        call.OutputTokens = 2_000;
        call.Last80VisibleChars = 1_600;
        if (provider == "Anthropic")
        {
            call.VisibleOutputChars = 4_000;
            call.Last80VisibleChars = 3_200; // 800 estimated tokens over 0.6 s = 1,333 tokens/s
        }
        var answer = Answer(0, 0, call);

        Assert.Null(CallTelemetryMeasures.AnswerStreamingRate(answer));
        Assert.True(CallTelemetryMeasures.IsStreamingRateUnmeasurable(answer));
    }

    [Fact]
    public void APlausibleStreamingRate_IsReturned_AndMeasurable()
    {
        // 400 window tokens over exactly the minimum span: 800 tokens/s, within both bounds.
        var answer = Answer(0, 0, StreamedCall(CallTelemetryMeasures.MinMeasurableDecodeSpanMs));

        Assert.Equal(800.0, CallTelemetryMeasures.AnswerStreamingRate(answer)!.Value.TokensPerSecond, 9);
        Assert.False(CallTelemetryMeasures.IsStreamingRateUnmeasurable(answer));
    }

    [Fact]
    public void WithoutTelemetry_TheRateIsNull_AndNotUnmeasurable()
    {
        var noRows = Answer(0, 0);
        var noSpan = Answer(0, 0, StreamedCall(4_000));
        noSpan.ModelCalls[0].Last80DecodeSpanMs = null;
        var noChars = Answer(0, 0, StreamedCall(100));
        noChars.ModelCalls[0].Last80VisibleChars = null;

        foreach (var answer in new[] { noRows, noSpan, noChars })
        {
            Assert.Null(CallTelemetryMeasures.AnswerStreamingRate(answer));
            Assert.False(CallTelemetryMeasures.IsStreamingRateUnmeasurable(answer));
        }
    }

    [Fact]
    public void NetModelTime_SubtractsOwnWaits_AndIsNullWhenNeitherWasRecorded()
    {
        var answer = Answer(1_500, 500);
        answer.ToolTimeMs = 2_000;

        // ModelTimeMs = 10000 − 2000 = 8000.
        Assert.Equal(6_000L, CallTelemetryMeasures.NetModelTimeMs(answer));
        Assert.Equal(7_500L, CallTelemetryMeasures.NetModelTimeMs(Answer(2_500, null)));
        Assert.Equal(0L, CallTelemetryMeasures.NetModelTimeMs(Answer(20_000, 0)));
        Assert.Null(CallTelemetryMeasures.NetModelTimeMs(Answer(null, null)));
    }

    [Fact]
    public void OwnWaitShare_DividesTotalWaitsByTotalModelTime_OverAnswersWithTelemetry()
    {
        var a = Answer(3_000, 1_000, CandidateCall(0, 0));
        var b = Answer(1_000, 0, CandidateCall(0, 0));
        var withoutTelemetry = Answer(9_000, 9_000);

        // (4000 + 1000) / (10000 + 10000); the answer without rows is skipped.
        Assert.Equal(0.25, CallTelemetryMeasures.OwnWaitShare(new[] { a, b, withoutTelemetry })!.Value, 9);
        Assert.Null(CallTelemetryMeasures.OwnWaitShare(new[] { withoutTelemetry }));
        Assert.Null(CallTelemetryMeasures.OwnWaitShare(Array.Empty<BenchmarkRunAnswer>()));
    }
}
