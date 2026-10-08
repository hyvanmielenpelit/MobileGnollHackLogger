namespace Overseer.Services.Telemetry;

using System;
using System.Collections.Generic;
using System.Linq;
using MobileGnollHackLogger.Data;

/// <summary>
/// The per-answer measures derived from an answer's <see cref="ModelCallSource.BenchmarkCandidate"/>
/// <see cref="ModelCallTelemetry"/> rows: how long the user waited for answer text, how fast it then
/// streamed, and how much of the turn was Overseer's own waiting. Pure and culture-independent; every
/// measure is null when a mark it needs was not recorded.
/// </summary>
public static class CallTelemetryMeasures
{
    /// <summary>Characters per token used to estimate visible tokens where the provider does not count them.</summary>
    public const double EstimatedCharsPerToken = 4.0;

    /// <summary>The answer's candidate calls, in <see cref="ModelCallTelemetry.CallIndex"/> order.</summary>
    public static IReadOnlyList<ModelCallTelemetry> CandidateCalls(BenchmarkRunAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(answer);
        return (answer.ModelCalls ?? new List<ModelCallTelemetry>())
            .Where(c => c.Source == ModelCallSource.BenchmarkCandidate)
            .OrderBy(c => c.CallIndex)
            .ThenBy(c => c.StartedAtUtc)
            .ToList();
    }

    /// <summary>True when the answer carries at least one candidate telemetry row.</summary>
    public static bool HasCandidateTelemetry(BenchmarkRunAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(answer);
        return answer.ModelCalls != null && answer.ModelCalls.Any(c => c.Source == ModelCallSource.BenchmarkCandidate);
    }

    /// <summary>The candidate call with the highest <see cref="ModelCallTelemetry.CallIndex"/>: the one that produced the answer text. Null when there is none.</summary>
    public static ModelCallTelemetry? FinalCandidateCall(BenchmarkRunAnswer answer)
    {
        var calls = CandidateCalls(answer);
        return calls.Count > 0 ? calls[^1] : null;
    }

    /// <summary>
    /// Time from the start of the turn to the first visible output of the final candidate call, minus
    /// every permit and retry-backoff wait of the turn, in milliseconds, floored at 0:
    /// <code>
    /// gross = (final.StartedAtUtc − answer.StartedAtUtc)
    ///       + final.PermitWaitMs + final.BackoffWaitMs + final.FailedAttemptMs
    ///       + final.FirstOutputMs
    /// net   = gross − answer.PermitWaitMs − answer.BackoffWaitMs
    /// </code>
    /// The first term ends where the final call starts, before its own permit wait. Its permit wait,
    /// backoff and failed attempts take it to the send of its successful attempt, which
    /// <see cref="ModelCallTelemetry.FirstOutputMs"/> is relative to. The gross span therefore holds
    /// every wait of the turn, the final call's included, and the answer's totals, which count the final
    /// call's waits too, remove each exactly once. Failed attempts are provider time and stay in.
    /// Null when <see cref="BenchmarkRunAnswer.StartedAtUtc"/> is null, there is no candidate call, or the
    /// final call's <see cref="ModelCallTelemetry.FirstOutputMs"/> is null.
    /// </summary>
    public static long? TimeToFirstAnswerTextMs(BenchmarkRunAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(answer);
        if (!answer.StartedAtUtc.HasValue)
        {
            return null;
        }

        var final = FinalCandidateCall(answer);
        if (final == null || final.FirstOutputMs is not int firstOutputMs)
        {
            return null;
        }

        double turnStartToCallStartMs = (final.StartedAtUtc - answer.StartedAtUtc.Value).TotalMilliseconds;
        double callStartToSendMs = (double)final.PermitWaitMs + final.BackoffWaitMs + final.FailedAttemptMs;
        double grossMs = turnStartToCallStartMs + callStartToSendMs + firstOutputMs;
        double turnWaitsMs = (double)(answer.PermitWaitMs ?? 0L) + (answer.BackoffWaitMs ?? 0L);

        return Math.Max(0L, (long)Math.Round(grossMs - turnWaitsMs, MidpointRounding.AwayFromZero));
    }

    /// <summary>
    /// The shortest <see cref="ModelCallTelemetry.Last80DecodeSpanMs"/> a streaming rate is measured
    /// over. A shorter span is visible text that arrived in one burst after thinking, whose rate
    /// measures delivery, not decoding.
    /// </summary>
    public const int MinMeasurableDecodeSpanMs = 500;

    /// <summary>The highest streaming rate, in tokens per second, reported as a decode rate.</summary>
    public const int MaxPlausibleTokensPerSecond = 1000;

    /// <summary>
    /// The final candidate call's visible decode rate over the last 80 % of its visible deltas, in
    /// tokens per second: window tokens ÷ (<see cref="ModelCallTelemetry.Last80DecodeSpanMs"/> / 1000).
    /// Window tokens are the visible tokens (<see cref="ModelCallTelemetry.OutputTokens"/> −
    /// <see cref="ModelCallTelemetry.ReasoningTokens"/>, a null reasoning count read as 0) scaled by
    /// <see cref="ModelCallTelemetry.Last80VisibleChars"/> ÷ <see cref="ModelCallTelemetry.VisibleOutputChars"/>.
    /// Anthropic counts thinking inside its output tokens, so for it the window tokens are estimated as
    /// <see cref="ModelCallTelemetry.Last80VisibleChars"/> ÷ <see cref="EstimatedCharsPerToken"/> and
    /// <c>Estimated</c> is true. Null when the span is missing or not positive, either character count
    /// is missing or zero, or the visible tokens are unknown or not positive; and, for every provider,
    /// when the span is shorter than <see cref="MinMeasurableDecodeSpanMs"/> or the rate exceeds
    /// <see cref="MaxPlausibleTokensPerSecond"/> (<see cref="IsStreamingRateUnmeasurable"/>).
    /// </summary>
    public static (double TokensPerSecond, bool Estimated)? AnswerStreamingRate(BenchmarkRunAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(answer);
        var final = FinalCandidateCall(answer);
        if (final == null || final.Last80DecodeSpanMs is not int spanMs || spanMs < MinMeasurableDecodeSpanMs)
        {
            return null;
        }

        var rate = UnboundedStreamingRate(final);
        return rate.HasValue && rate.Value.TokensPerSecond <= MaxPlausibleTokensPerSecond ? rate : null;
    }

    /// <summary>
    /// True when the final candidate call recorded a decode span and visible characters but its
    /// streaming rate fails a bound: the span is shorter than <see cref="MinMeasurableDecodeSpanMs"/>,
    /// or the rate exceeds <see cref="MaxPlausibleTokensPerSecond"/>. False when there is no final
    /// call, a mark is missing, or the rate is unknown for another reason.
    /// </summary>
    public static bool IsStreamingRateUnmeasurable(BenchmarkRunAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(answer);
        var final = FinalCandidateCall(answer);
        if (final == null
            || final.Last80DecodeSpanMs is not int spanMs || spanMs < 0
            || final.Last80VisibleChars is not int windowChars || windowChars <= 0
            || final.VisibleOutputChars <= 0)
        {
            return false;
        }

        if (spanMs < MinMeasurableDecodeSpanMs)
        {
            return true;
        }

        var rate = UnboundedStreamingRate(final);
        return rate.HasValue && rate.Value.TokensPerSecond > MaxPlausibleTokensPerSecond;
    }

    /// <summary><see cref="AnswerStreamingRate"/> for <paramref name="final"/> without its two bounds.</summary>
    private static (double TokensPerSecond, bool Estimated)? UnboundedStreamingRate(ModelCallTelemetry final)
    {
        if (final.Last80DecodeSpanMs is not int spanMs || spanMs <= 0
            || final.Last80VisibleChars is not int windowChars || windowChars <= 0
            || final.VisibleOutputChars <= 0)
        {
            return null;
        }

        double seconds = spanMs / 1000.0;
        if (string.Equals(final.Provider?.Trim(), "Anthropic", StringComparison.OrdinalIgnoreCase))
        {
            return (windowChars / EstimatedCharsPerToken / seconds, true);
        }

        if (final.OutputTokens is not int outputTokens)
        {
            return null;
        }

        int visibleTokens = outputTokens - (final.ReasoningTokens ?? 0);
        if (visibleTokens <= 0)
        {
            return null;
        }

        double windowTokens = visibleTokens * ((double)windowChars / final.VisibleOutputChars);
        return (windowTokens / seconds, false);
    }

    /// <summary>
    /// <see cref="BenchmarkRunAnswer.ModelTimeMs"/> minus the turn's permit and retry-backoff waits,
    /// floored at 0. Null when neither wait was recorded.
    /// </summary>
    public static long? NetModelTimeMs(BenchmarkRunAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(answer);
        if (!answer.PermitWaitMs.HasValue && !answer.BackoffWaitMs.HasValue)
        {
            return null;
        }

        return Math.Max(0L, answer.ModelTimeMs - (answer.PermitWaitMs ?? 0L) - (answer.BackoffWaitMs ?? 0L));
    }

    /// <summary>
    /// Total own waits (<see cref="BenchmarkRunAnswer.PermitWaitMs"/> + <see cref="BenchmarkRunAnswer.BackoffWaitMs"/>)
    /// ÷ total <see cref="BenchmarkRunAnswer.ModelTimeMs"/>, over the answers that carry candidate
    /// telemetry. Null when none does or their model time sums to zero.
    /// </summary>
    public static double? OwnWaitShare(IEnumerable<BenchmarkRunAnswer> answers)
    {
        ArgumentNullException.ThrowIfNull(answers);
        long waits = 0;
        long modelTime = 0;
        foreach (var answer in answers.Where(HasCandidateTelemetry))
        {
            waits += (answer.PermitWaitMs ?? 0L) + (answer.BackoffWaitMs ?? 0L);
            modelTime += answer.ModelTimeMs;
        }

        return modelTime > 0 ? (double)waits / modelTime : null;
    }
}
