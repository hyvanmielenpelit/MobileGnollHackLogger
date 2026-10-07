namespace Overseer.Services.Providers;

using System.Diagnostics;

/// <summary>
/// What one streamed model call reported about itself: the served model and ids, refusal and
/// fallback, and <see cref="Stopwatch"/> marks for the stream's milestones. A provider fills it while
/// parsing, stamping each mark right after the line is read and before any sanitizer, and yields it
/// once as a <c>call_meta</c> event after the stream ends. The agent loop consumes that event; it is
/// never forwarded to a client.
///
/// <para>Marks are raw <see cref="Stopwatch.GetTimestamp"/> values. The agent loop converts them to
/// milliseconds relative to the send of the successful attempt.</para>
/// </summary>
public sealed class ProviderCallMeta
{
    /// <summary>The most visible-output deltas whose marks are kept, for the decode-rate measurement.</summary>
    public const int MaxDeltaMarks = 4096;

    public string? ServedModelId { get; set; }
    public string? ResponseId { get; set; }

    /// <summary>Anthropic <c>usage.speed</c> (<c>fast</c> / <c>standard</c>); null elsewhere.</summary>
    public string? ServedSpeed { get; set; }

    public bool IsRefusal { get; set; }

    /// <summary>The model that served the turn instead of the requested one, when the provider says so.</summary>
    public string? FallbackModelId { get; set; }

    public long? FirstEventTicks { get; private set; }
    public long? FirstReasoningTicks { get; private set; }

    /// <summary>First visible text or tool-call output.</summary>
    public long? FirstOutputTicks { get; private set; }

    public long? FirstToolCallTicks { get; private set; }
    public long? LastDeltaTicks { get; private set; }
    public long? CompletedTicks { get; private set; }

    /// <summary>Visible text and tool-call deltas.</summary>
    public int OutputDeltaCount { get; private set; }

    /// <summary>Characters of visible text, raw, before any sanitizer.</summary>
    public int VisibleOutputChars { get; private set; }

    private readonly List<(long Ticks, int Chars)> _textMarks = new();

    /// <summary>Visible-text deltas in arrival order, at most <see cref="MaxDeltaMarks"/>.</summary>
    public IReadOnlyList<(long Ticks, int Chars)> TextDeltaMarks => _textMarks;

    public static long Now() => Stopwatch.GetTimestamp();

    public void MarkEvent(long ticks) => FirstEventTicks ??= ticks;

    public void MarkReasoning(long ticks)
    {
        MarkEvent(ticks);
        FirstReasoningTicks ??= ticks;
    }

    /// <summary>A visible text delta of <paramref name="chars"/> raw characters.</summary>
    public void MarkText(long ticks, int chars)
    {
        MarkEvent(ticks);
        if (chars <= 0)
        {
            return;
        }

        FirstOutputTicks ??= ticks;
        LastDeltaTicks = ticks;
        OutputDeltaCount++;
        VisibleOutputChars += chars;
        if (_textMarks.Count < MaxDeltaMarks)
        {
            _textMarks.Add((ticks, chars));
        }
    }

    /// <summary>A tool call started, or a delta of its arguments arrived.</summary>
    public void MarkToolCall(long ticks)
    {
        MarkEvent(ticks);
        FirstOutputTicks ??= ticks;
        FirstToolCallTicks ??= ticks;
        LastDeltaTicks = ticks;
        OutputDeltaCount++;
    }

    /// <summary>The provider reported the response finished, successfully or not. The first report wins.</summary>
    public void MarkCompleted(long ticks)
    {
        MarkEvent(ticks);
        CompletedTicks ??= ticks;
    }

    /// <summary>
    /// The decode span over the last 80 % of visible text deltas: the elapsed ticks from the delta at
    /// the 20 % position to the last one, and the characters that arrived after it. Null when fewer than
    /// two deltas fall in the window, which no rate can be computed from.
    /// </summary>
    public (long SpanTicks, int Chars)? Last80()
    {
        int n = _textMarks.Count;
        int start = (int)Math.Floor(n * 0.2);
        if (n - start < 2)
        {
            return null;
        }

        int chars = 0;
        for (int i = start + 1; i < n; i++)
        {
            chars += _textMarks[i].Chars;
        }

        return (_textMarks[n - 1].Ticks - _textMarks[start].Ticks, chars);
    }

    /// <summary>Milliseconds from <paramref name="originTicks"/> to <paramref name="ticks"/>; null when either is unknown.</summary>
    public static int? MsBetween(long? originTicks, long? ticks)
    {
        if (originTicks == null || ticks == null)
        {
            return null;
        }

        double ms = (ticks.Value - originTicks.Value) * 1000.0 / Stopwatch.Frequency;
        return (int)Math.Clamp(Math.Round(ms), int.MinValue, int.MaxValue);
    }

    public static int TicksToMs(long ticks) =>
        (int)Math.Clamp(Math.Round(ticks * 1000.0 / Stopwatch.Frequency), int.MinValue, int.MaxValue);
}
