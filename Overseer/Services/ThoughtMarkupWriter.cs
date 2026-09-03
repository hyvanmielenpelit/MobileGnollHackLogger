using System;
using System.Collections.Generic;
using System.Text;

namespace Overseer.Services;

public class ThoughtMarkupWriter
{
    public bool InThoughtDiv { get; set; } = false;
    public int EmittedDivCount { get; private set; } = 0;

    /// <summary>
    /// Visible (text-channel) runs of the current iteration that have already been closed,
    /// as (start, end) offsets into the response buffer.
    ///
    /// A single iteration can produce several visible runs, because a provider may interleave
    /// the channels. A GPT-5.x Responses iteration arrives as visible preamble
    /// (<c>response.output_text.delta</c>), then a reasoning summary
    /// (<c>response.reasoning_summary_text.delta</c>), then the tool call. The preamble used to
    /// be *discarded* when the reasoning summary opened its thought div, so
    /// <see cref="WrapPreToolVisibleText"/> found nothing to wrap and the model's tool-use
    /// narration stayed in the response as answer prose — visible in chat and, worse, graded as
    /// an answer by the benchmark assessor (the 2026-09-03 run penalised five answers for it).
    /// Remembering every run instead of only the last one is the fix.
    /// </summary>
    private readonly List<(int Start, int End)> _visibleSpans = new();

    /// <summary>
    /// Start offset of the visible run currently being appended to, or -1 when none is open.
    /// </summary>
    private int _openVisibleStart = -1;

    public void ResetIteration()
    {
        InThoughtDiv = false;
        _visibleSpans.Clear();
        _openVisibleStart = -1;
    }

    public void HandleThinkingChunk(StringBuilder fullResponse, string delta)
    {
        if (string.IsNullOrEmpty(delta)) return;

        if (!InThoughtDiv)
        {
            // Close — never discard — the visible run this reasoning summary interrupts. Text
            // that a provider emitted before a reasoning summary and a tool call in the same
            // iteration is analysis-channel narration, and the span record is the only thing
            // that lets WrapPreToolVisibleText find it later.
            CloseOpenVisibleSpan(fullResponse);

            fullResponse.Append("\n\n<div class=\"ai-thought\">\n\n");
            InThoughtDiv = true;
            EmittedDivCount++;
        }

        fullResponse.Append(delta);
    }

    public void HandleChunk(StringBuilder fullResponse, string delta, bool needsSpacer)
    {
        if (InThoughtDiv)
        {
            fullResponse.Append("\n\n</div>\n\n");
            InThoughtDiv = false;
        }

        if (needsSpacer)
        {
            fullResponse.Append("\n\n");
        }

        if (!string.IsNullOrEmpty(delta))
        {
            if (_openVisibleStart < 0)
            {
                _openVisibleStart = fullResponse.Length;
            }
            fullResponse.Append(delta);
        }
    }

    public void CloseOpenThoughtDiv(StringBuilder fullResponse)
    {
        if (InThoughtDiv)
        {
            fullResponse.Append("\n\n</div>\n\n");
            InThoughtDiv = false;
        }
    }

    /// <summary>
    /// This iteration's visible text so far, or null when the iteration has emitted none.
    ///
    /// Deliberately only the run currently being appended to: the caller uses this to look for a
    /// tool call the model leaked into the text channel, which can only be in the text still
    /// being produced.
    /// </summary>
    public string? GetIterationVisibleText(StringBuilder fullResponse)
    {
        if (InThoughtDiv || _openVisibleStart < 0) return null;
        if (fullResponse.Length <= _openVisibleStart) return null;

        return fullResponse.ToString(_openVisibleStart, fullResponse.Length - _openVisibleStart);
    }

    /// <summary>
    /// Wraps the first <paramref name="visibleLength"/> characters of this iteration's visible
    /// text in a thought div and leaves the remainder as answer prose.
    ///
    /// Used when a tool call leaked into the text channel. Unlike a normal tool iteration, the
    /// narration and the real answer then arrive in the *same* iteration, so the whole visible
    /// run must not be wrapped — doing that would move the answer into the thought block and
    /// leave the answer empty.
    /// </summary>
    public void WrapLeadingVisibleText(StringBuilder fullResponse, int visibleLength)
    {
        if (InThoughtDiv || _openVisibleStart < 0) return;

        int available = fullResponse.Length - _openVisibleStart;
        if (visibleLength <= 0 || available <= 0) return;

        visibleLength = Math.Min(visibleLength, available);

        string leading = fullResponse.ToString(_openVisibleStart, visibleLength);
        string trimmed = leading.Trim();
        if (string.IsNullOrEmpty(trimmed)) return;

        fullResponse.Remove(_openVisibleStart, visibleLength);
        fullResponse.Insert(_openVisibleStart, $"<div class=\"ai-thought\">\n\n{trimmed}\n\n</div>\n\n");
        EmittedDivCount++;

        // Earlier visible runs of this same iteration are narration in full: each was cut short
        // by a reasoning summary, so none of them can be the answer. Wrapped *after* the split
        // above and from last to first, so that no offset still to be used is disturbed.
        WrapRecordedVisibleSpans(fullResponse);

        // The remainder of the iteration's visible text is answer prose; it is no longer a
        // candidate for pre-tool wrapping.
        _openVisibleStart = -1;
    }

    public void WrapPreToolVisibleText(StringBuilder fullResponse)
    {
        CloseOpenThoughtDiv(fullResponse);
        CloseOpenVisibleSpan(fullResponse);

        WrapRecordedVisibleSpans(fullResponse);
    }

    /// <summary>
    /// Records the visible run currently open, so that it survives whatever comes next — a
    /// reasoning summary opening a thought div, or the end of the iteration.
    /// </summary>
    private void CloseOpenVisibleSpan(StringBuilder fullResponse)
    {
        if (_openVisibleStart >= 0 && fullResponse.Length > _openVisibleStart)
        {
            _visibleSpans.Add((_openVisibleStart, fullResponse.Length));
        }

        _openVisibleStart = -1;
    }

    /// <summary>
    /// Wraps every recorded visible run of this iteration in its own thought div, so several
    /// runs become sibling divs rather than one div swallowing the reasoning divs between them.
    ///
    /// Iterates from last to first: wrapping rewrites the buffer at the span's own offset and
    /// shifts everything after it, so an earlier span's offsets stay valid only while the later
    /// spans are handled first.
    /// </summary>
    private void WrapRecordedVisibleSpans(StringBuilder fullResponse)
    {
        for (int i = _visibleSpans.Count - 1; i >= 0; i--)
        {
            WrapVisibleSpan(fullResponse, _visibleSpans[i]);
        }

        _visibleSpans.Clear();
    }

    private void WrapVisibleSpan(StringBuilder fullResponse, (int Start, int End) span)
    {
        int start = span.Start;
        int end = Math.Min(span.End, fullResponse.Length);
        if (end <= start) return;

        string text = fullResponse.ToString(start, end - start);
        string trimmed = text.Trim();

        // A run that trims away to nothing is dropped rather than wrapped: an empty thought div
        // is markup the reader has to look at for no reason.
        if (string.IsNullOrEmpty(trimmed)) return;

        fullResponse.Remove(start, end - start);
        fullResponse.Insert(start, $"<div class=\"ai-thought\">\n\n{trimmed}\n\n</div>\n\n");
        EmittedDivCount++;
    }
}
