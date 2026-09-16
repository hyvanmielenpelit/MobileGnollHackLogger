namespace Overseer.Tests.UnitTests;

using MobileGnollHackLogger.Data;
using Overseer.Services.Benchmarking;
using Xunit;

public class BenchmarkAnswerSanitizerTests
{
    [Fact]
    public void CleanAnswer_ReturnsNoFlags()
    {
        string raw = "The Vorpal Blade is an artifact long sword in GnollHack.";
        var result = BenchmarkAnswerSanitizer.Sanitize(raw);

        Assert.Equal(raw, result.AnswerText);
        Assert.Null(result.ThoughtText);
        Assert.Equal(BenchmarkAnswerFlags.None, result.Flags);
    }

    [Fact]
    public void EmptyOrWhitespaceAnswer_ReturnsEmptyFlag()
    {
        var result1 = BenchmarkAnswerSanitizer.Sanitize("");
        Assert.True(result1.Flags.HasFlag(BenchmarkAnswerFlags.Empty));

        var result2 = BenchmarkAnswerSanitizer.Sanitize("   \n\t  ");
        Assert.True(result2.Flags.HasFlag(BenchmarkAnswerFlags.Empty));

        var result3 = BenchmarkAnswerSanitizer.Sanitize(null);
        Assert.True(result3.Flags.HasFlag(BenchmarkAnswerFlags.Empty));
    }

    [Fact]
    public void TruncatedMarker_SetsTruncatedFlag()
    {
        string raw = "Here is some text\n\n[Answer truncated at 8000 tokens]";
        var result = BenchmarkAnswerSanitizer.Sanitize(raw);

        Assert.True(result.Flags.HasFlag(BenchmarkAnswerFlags.Truncated));
        Assert.Contains("Here is some text", result.AnswerText);
    }

    [Fact]
    public void HarnessArtifact_ToFunctions_SetsHarnessArtifactsFlag()
    {
        string raw = "to=functions.search {\"query\":\"vorpal\"} The vorpal blade decapitates.";
        var result = BenchmarkAnswerSanitizer.Sanitize(raw);

        Assert.True(result.Flags.HasFlag(BenchmarkAnswerFlags.HarnessArtifacts));
        Assert.Contains("The vorpal blade decapitates.", result.AnswerText);
    }

    [Fact]
    public void HarnessArtifact_ControlTokens_SetsHarnessArtifactsFlag()
    {
        string raw = "<|call|> <|constrain|> The answer is 42.";
        var result = BenchmarkAnswerSanitizer.Sanitize(raw);

        Assert.True(result.Flags.HasFlag(BenchmarkAnswerFlags.HarnessArtifacts));
        Assert.Contains("The answer is 42.", result.AnswerText);
    }

    [Fact]
    public void HarnessArtifact_BareToolJsonLine_SetsHarnessArtifactsFlag()
    {
        string raw = "{\"repository\": \"GnollHack\", \"file_filter\": \"src/allmain.c\"}\nHere is the answer.";
        var result = BenchmarkAnswerSanitizer.Sanitize(raw);

        Assert.True(result.Flags.HasFlag(BenchmarkAnswerFlags.HarnessArtifacts));
        Assert.Contains("Here is the answer.", result.AnswerText);
    }

    [Fact]
    public void ThoughtExtraction_SeparatesThoughtAndAnswer()
    {
        string raw = "<div class=\"ai-thought\">\nThinking about silver dragons...\n</div>\nSilver dragons reflect rays.";
        var result = BenchmarkAnswerSanitizer.Sanitize(raw);

        Assert.Equal("Silver dragons reflect rays.", result.AnswerText.Trim());
        Assert.NotNull(result.ThoughtText);
        Assert.Contains("Thinking about silver dragons...", result.ThoughtText);
        Assert.Equal(BenchmarkAnswerFlags.None, result.Flags);
    }

    [Fact]
    public void StripThoughts_RemovesLeadingClosedBlock()
    {
        string raw = "<div class=\"ai-thought\">\n\nI should aim for 120-300 words.\n\n</div>\n\nThis suite evaluates an AI assistant.";

        Assert.Equal("This suite evaluates an AI assistant.", BenchmarkAnswerSanitizer.StripThoughts(raw));
    }

    [Fact]
    public void StripThoughts_RemovesUnclosedTrailingBlock()
    {
        string raw = "Lead paragraph.\n\n<div class=\"ai-thought\">\nstill thinking";

        Assert.Equal("Lead paragraph.", BenchmarkAnswerSanitizer.StripThoughts(raw));
    }

    [Fact]
    public void StripThoughts_ThoughtOnly_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, BenchmarkAnswerSanitizer.StripThoughts("<div class=\"ai-thought\">\nonly thoughts\n</div>"));
    }

    [Fact]
    public void StripThoughts_NoBlock_TrimsOnly()
    {
        Assert.Equal("Plain text.", BenchmarkAnswerSanitizer.StripThoughts("  Plain text.  "));
    }

    [Fact]
    public void StripThoughts_NullOrEmpty_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, BenchmarkAnswerSanitizer.StripThoughts(null));
        Assert.Equal(string.Empty, BenchmarkAnswerSanitizer.StripThoughts(string.Empty));
    }

    [Fact]
    public void StripThoughts_ThenUnwrapMarkdown_RemovesFenceAfterThought()
    {
        string raw = "<div class=\"ai-thought\">\n\nPlanning the description.\n\n</div>\n\n```markdown\n### Covered Domains\n- **A**: b\n```";

        string description = BenchmarkDescriptionPrompt.UnwrapMarkdown(BenchmarkAnswerSanitizer.StripThoughts(raw));

        Assert.Equal("### Covered Domains\n- **A**: b", description);
    }
}
