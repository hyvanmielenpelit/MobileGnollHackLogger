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
