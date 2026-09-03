using System.Text;
using System.Text.RegularExpressions;
using Overseer.Services;
using Xunit;

namespace Overseer.Tests.UnitTests;

public class ThoughtBoundaryTests
{
    private static readonly Regex StripThoughtsRegex = new(@"<div class=""ai-thought"">[\s\S]*?(?:<\/div>|$)", RegexOptions.Compiled);

    [Fact]
    public void ThinkingOnlyIteration_ThenToolCall_ProducesSingleBalancedDiv()
    {
        var writer = new ThoughtMarkupWriter();
        var sb = new StringBuilder();

        writer.ResetIteration();
        writer.HandleThinkingChunk(sb, "Thinking about tool call...");
        writer.WrapPreToolVisibleText(sb);

        string result = sb.ToString();
        Assert.StartsWith("\n\n<div class=\"ai-thought\">\n\n", result);
        Assert.EndsWith("\n\n</div>\n\n", result);
        Assert.Equal(1, CountOccurrences(result, "<div class=\"ai-thought\">"));
        Assert.Equal(1, CountOccurrences(result, "</div>"));
    }

    [Fact]
    public void TwoConsecutiveThinkingIterations_WithToolCalls_ProducesTwoSiblingDivs_NoNesting()
    {
        var writer = new ThoughtMarkupWriter();
        var sb = new StringBuilder();

        // Iteration 1
        writer.ResetIteration();
        writer.HandleThinkingChunk(sb, "Iteration 1 thinking");
        writer.WrapPreToolVisibleText(sb);

        // Iteration 2
        writer.ResetIteration();
        writer.HandleThinkingChunk(sb, "Iteration 2 thinking");
        writer.WrapPreToolVisibleText(sb);

        string result = sb.ToString();
        Assert.Equal(2, CountOccurrences(result, "<div class=\"ai-thought\">"));
        Assert.Equal(2, CountOccurrences(result, "</div>"));
        Assert.DoesNotContain("<div class=\"ai-thought\">\n\n<div", result);
    }

    [Fact]
    public void ThinkingThenVisibleText_ThenToolCall_ProducesTwoSiblingDivs()
    {
        var writer = new ThoughtMarkupWriter();
        var sb = new StringBuilder();

        writer.ResetIteration();
        writer.HandleThinkingChunk(sb, "Thinking part");
        writer.HandleChunk(sb, "Pre-tool visible preamble", false);
        writer.WrapPreToolVisibleText(sb);

        string result = sb.ToString();
        Assert.Equal(2, CountOccurrences(result, "<div class=\"ai-thought\">"));
        Assert.Equal(2, CountOccurrences(result, "</div>"));
        Assert.DoesNotContain("<div class=\"ai-thought\">\n\n<div", result);
    }

    [Fact]
    public void VisibleTextThenThinking_ThenToolCall_WrapsBothAsSiblingDivs()
    {
        // This replaces an earlier test that asserted a *single* div here, which codified the
        // defect rather than a requirement: the visible preamble was silently dropped when the
        // reasoning summary opened its div, so it was never wrapped and stayed in the response
        // as answer prose.
        var writer = new ThoughtMarkupWriter();
        var sb = new StringBuilder();

        writer.ResetIteration();
        writer.HandleChunk(sb, "Opening greeting line", false);
        writer.HandleThinkingChunk(sb, "Thinking part");
        writer.WrapPreToolVisibleText(sb);

        string result = sb.ToString();
        Assert.Equal(2, CountOccurrences(result, "<div class=\"ai-thought\">"));
        Assert.Equal(2, CountOccurrences(result, "</div>"));
        Assert.DoesNotContain("<div class=\"ai-thought\">\n\n<div", result);

        // The preamble must not survive anywhere outside a thought div.
        string stripped = StripThoughtsRegex.Replace(result, "").Trim();
        Assert.DoesNotContain("Opening greeting line", stripped);
        Assert.DoesNotContain("Thinking part", stripped);
        Assert.Equal(string.Empty, stripped);
    }

    [Fact]
    public void OpenAiEventOrder_VisibleThenReasoningThenTool_LeavesNoAnswerProse()
    {
        // The exact GPT-5.x Responses ordering that produced the defect: visible preamble
        // (response.output_text.delta), reasoning summary
        // (response.reasoning_summary_text.delta), then the tool call. The preamble is tool-use
        // narration and must never reach the graded answer.
        var writer = new ThoughtMarkupWriter();
        var sb = new StringBuilder();

        writer.ResetIteration();
        writer.HandleChunk(sb, "I'm verifying whether GnollHack exposes the counter", false);
        writer.HandleChunk(sb, " through a command or priest consultation.", false);
        writer.HandleThinkingChunk(sb, "Need to search the source for the prayer timeout field.");
        writer.WrapPreToolVisibleText(sb);

        writer.ResetIteration();
        writer.HandleChunk(sb, "Prayer timeout is a countdown in game turns.", false);
        writer.CloseOpenThoughtDiv(sb);

        string result = sb.ToString();
        string stripped = StripThoughtsRegex.Replace(result, "").Trim();

        Assert.Equal("Prayer timeout is a countdown in game turns.", stripped);
        Assert.DoesNotContain("I'm verifying", stripped);
        Assert.Equal(2, CountOccurrences(result, "<div class=\"ai-thought\">"));
        Assert.Equal(2, CountOccurrences(result, "</div>"));
        Assert.DoesNotContain("<div class=\"ai-thought\">\n\n<div", result);
    }

    [Fact]
    public void MultipleVisibleRunsInOneIteration_ProduceOneDivEach_NoNesting()
    {
        // A provider may interleave the channels more than once in a single iteration. Each
        // visible run has to become its own sibling div; one div spanning them all would
        // enclose the reasoning divs sitting between them.
        var writer = new ThoughtMarkupWriter();
        var sb = new StringBuilder();

        writer.ResetIteration();
        writer.HandleChunk(sb, "Visible run one", false);
        writer.HandleThinkingChunk(sb, "Reasoning one");
        writer.HandleChunk(sb, "Visible run two", false);
        writer.HandleThinkingChunk(sb, "Reasoning two");
        writer.HandleChunk(sb, "Visible run three", false);
        writer.WrapPreToolVisibleText(sb);

        string result = sb.ToString();

        // Three visible runs plus two reasoning runs.
        Assert.Equal(5, CountOccurrences(result, "<div class=\"ai-thought\">"));
        Assert.Equal(5, CountOccurrences(result, "</div>"));
        Assert.DoesNotContain("<div class=\"ai-thought\">\n\n<div", result);

        string stripped = StripThoughtsRegex.Replace(result, "").Trim();
        Assert.Equal(string.Empty, stripped);
    }

    [Fact]
    public void VisibleTextAfterLastToolCall_IsNotWrapped()
    {
        var writer = new ThoughtMarkupWriter();
        var sb = new StringBuilder();

        // Tool iteration
        writer.ResetIteration();
        writer.HandleThinkingChunk(sb, "Thinking about tool");
        writer.WrapPreToolVisibleText(sb);

        // Final answer iteration
        writer.ResetIteration();
        writer.HandleChunk(sb, "Here is the final answer to your question.", false);
        writer.CloseOpenThoughtDiv(sb);

        string result = sb.ToString();
        Assert.Equal(1, CountOccurrences(result, "<div class=\"ai-thought\">"));
        Assert.Equal(1, CountOccurrences(result, "</div>"));
        Assert.EndsWith("Here is the final answer to your question.", result);
    }

    [Fact]
    public void ErrorWhileThoughtDivOpen_LandsOutsideDiv()
    {
        var writer = new ThoughtMarkupWriter();
        var sb = new StringBuilder();

        writer.ResetIteration();
        writer.HandleThinkingChunk(sb, "Thinking before crash...");
        writer.CloseOpenThoughtDiv(sb);
        sb.Append("\n\n**Error:** API stream disconnected.");

        string result = sb.ToString();
        Assert.EndsWith("\n\n**Error:** API stream disconnected.", result);
        Assert.True(result.IndexOf("</div>") < result.IndexOf("**Error:**"));
    }

    [Fact]
    public void CancellationTimeoutError_LandsOutsideDiv()
    {
        var writer = new ThoughtMarkupWriter();
        var sb = new StringBuilder();

        writer.ResetIteration();
        writer.HandleThinkingChunk(sb, "Thinking in progress");
        writer.CloseOpenThoughtDiv(sb);
        sb.Append("\n\n**Error:** The request timed out.");

        string result = sb.ToString();
        Assert.EndsWith("\n\n**Error:** The request timed out.", result);
        Assert.True(result.IndexOf("</div>") < result.IndexOf("**Error:**"));
    }

    [Fact]
    public void StripThoughtsRegex_LeavesNoResidualThoughtTextOrOrphanDiv()
    {
        var writer = new ThoughtMarkupWriter();
        var sb = new StringBuilder();

        writer.ResetIteration();
        writer.HandleThinkingChunk(sb, "Secret thought 1");
        writer.WrapPreToolVisibleText(sb);

        writer.ResetIteration();
        writer.HandleThinkingChunk(sb, "Secret thought 2");
        writer.WrapPreToolVisibleText(sb);

        writer.ResetIteration();
        writer.HandleChunk(sb, "Final clean answer.", false);
        writer.CloseOpenThoughtDiv(sb);

        string full = sb.ToString();
        string stripped = StripThoughtsRegex.Replace(full, "").Trim();

        Assert.Equal("Final clean answer.", stripped);
        Assert.DoesNotContain("Secret thought", stripped);
        Assert.DoesNotContain("</div>", stripped);
        Assert.DoesNotContain("<div", stripped);
    }

    private static int CountOccurrences(string text, string pattern)
    {
        int count = 0;
        int i = 0;
        while ((i = text.IndexOf(pattern, i, StringComparison.Ordinal)) != -1)
        {
            count++;
            i += pattern.Length;
        }
        return count;
    }
}
