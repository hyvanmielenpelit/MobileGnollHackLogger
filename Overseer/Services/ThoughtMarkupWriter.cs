using System;
using System.Text;

namespace Overseer.Services;

public class ThoughtMarkupWriter
{
    public bool InThoughtDiv { get; set; } = false;
    public int IterationVisibleStart { get; set; } = -1;
    public int EmittedDivCount { get; private set; } = 0;

    public void ResetIteration()
    {
        InThoughtDiv = false;
        IterationVisibleStart = -1;
    }

    public void HandleThinkingChunk(StringBuilder fullResponse, string delta)
    {
        if (string.IsNullOrEmpty(delta)) return;

        if (!InThoughtDiv)
        {
            fullResponse.Append("\n\n<div class=\"ai-thought\">\n\n");
            InThoughtDiv = true;
            IterationVisibleStart = -1;
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
            if (IterationVisibleStart < 0)
            {
                IterationVisibleStart = fullResponse.Length;
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
    /// </summary>
    public string? GetIterationVisibleText(StringBuilder fullResponse)
    {
        if (InThoughtDiv || IterationVisibleStart < 0) return null;
        if (fullResponse.Length <= IterationVisibleStart) return null;

        return fullResponse.ToString(IterationVisibleStart, fullResponse.Length - IterationVisibleStart);
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
        if (InThoughtDiv || IterationVisibleStart < 0) return;

        int available = fullResponse.Length - IterationVisibleStart;
        if (visibleLength <= 0 || available <= 0) return;

        visibleLength = Math.Min(visibleLength, available);

        string leading = fullResponse.ToString(IterationVisibleStart, visibleLength);
        string trimmed = leading.Trim();
        if (string.IsNullOrEmpty(trimmed)) return;

        fullResponse.Remove(IterationVisibleStart, visibleLength);
        fullResponse.Insert(IterationVisibleStart, $"<div class=\"ai-thought\">\n\n{trimmed}\n\n</div>\n\n");
        EmittedDivCount++;

        // The remainder of the iteration's visible text is answer prose; it is no longer a
        // candidate for pre-tool wrapping.
        IterationVisibleStart = -1;
    }

    public void WrapPreToolVisibleText(StringBuilder fullResponse)
    {
        CloseOpenThoughtDiv(fullResponse);

        if (IterationVisibleStart >= 0 && fullResponse.Length > IterationVisibleStart)
        {
            int len = fullResponse.Length - IterationVisibleStart;
            string trailing = fullResponse.ToString(IterationVisibleStart, len);
            string trimmed = trailing.Trim();

            if (!string.IsNullOrEmpty(trimmed))
            {
                fullResponse.Remove(IterationVisibleStart, len);
                fullResponse.Append($"<div class=\"ai-thought\">\n\n{trimmed}\n\n</div>\n\n");
                EmittedDivCount++;
            }
        }

        IterationVisibleStart = -1;
    }
}
