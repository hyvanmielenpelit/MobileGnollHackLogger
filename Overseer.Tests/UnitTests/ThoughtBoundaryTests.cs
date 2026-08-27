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
    public void VisibleTextThenThinking_ThenToolCall_DoesNotEncloseThoughtInsideVisible()
    {
        var writer = new ThoughtMarkupWriter();
        var sb = new StringBuilder();

        writer.ResetIteration();
        writer.HandleChunk(sb, "Opening greeting line", false);
        writer.HandleThinkingChunk(sb, "Thinking part");
        writer.WrapPreToolVisibleText(sb);

        string result = sb.ToString();
        Assert.Equal(1, CountOccurrences(result, "<div class=\"ai-thought\">"));
        Assert.Equal(1, CountOccurrences(result, "</div>"));
        Assert.DoesNotContain("<div class=\"ai-thought\">\n\n<div", result);
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
