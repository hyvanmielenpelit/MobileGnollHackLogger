using System;
using Overseer.Services.Providers;
using Xunit;

namespace Overseer.Tests.UnitTests;

public class ReasoningTextSanitizerTests
{
    [Fact]
    public void PlainProse_PassesThroughByteIdentical()
    {
        var sanitizer = new ReasoningTextSanitizer();
        string input = "Hello, I am the Gnoll Overseer. How can I help you today?";
        string output = sanitizer.Push(input) + sanitizer.Flush();
        Assert.Equal(input, output);
    }

    [Fact]
    public void ControlToken_SingleDelta_Stripped()
    {
        var sanitizer = new ReasoningTextSanitizer();
        string input = "Prefix <|channel|> middle <|call|> suffix";
        string output = sanitizer.Push(input) + sanitizer.Flush();
        Assert.Equal("Prefix  middle  suffix", output);
    }

    [Fact]
    public void ControlToken_SplitAcrossDeltas_Stripped()
    {
        var sanitizer = new ReasoningTextSanitizer();
        string d1 = "Text before <|cha";
        string d2 = "nnel|> text after";
        string out1 = sanitizer.Push(d1);
        string out2 = sanitizer.Push(d2);
        string flushed = sanitizer.Flush();
        string total = out1 + out2 + flushed;
        Assert.Equal("Text before  text after", total);
    }

    [Fact]
    public void ToolRoutingMarker_WithAdjacentJson_StrippedWhole()
    {
        var sanitizer = new ReasoningTextSanitizer();
        string payload = "to=functions.source_code_view {\"file\":\"src/potion.c\",\"start_line\":2197}";
        string output = sanitizer.Push(payload) + sanitizer.Flush();
        Assert.Equal("", output);
    }

    [Fact]
    public void ToolRoutingMarker_SplitAcrossMultipleDeltas_LongerThan32Chars_Stripped()
    {
        var sanitizer = new ReasoningTextSanitizer();
        string d1 = "Intro text to=func";
        string d2 = "tions.source_code_view {\"file\":\"src/";
        string d3 = "potion.c\",\"start_line\":2197} trailing text";

        string out1 = sanitizer.Push(d1);
        string out2 = sanitizer.Push(d2);
        string out3 = sanitizer.Push(d3);
        string flushed = sanitizer.Flush();

        string total = out1 + out2 + out3 + flushed;
        Assert.Equal("Intro text  trailing text", total);
    }

    [Fact]
    public void JsonWithBraceInStringLiteral_DoesNotEndScanEarly()
    {
        var sanitizer = new ReasoningTextSanitizer();
        string payload = "to=functions.search {\"query\":\"foo}bar\",\"extra\":123} clean tail";
        string output = sanitizer.Push(payload) + sanitizer.Flush();
        Assert.Equal(" clean tail", output);
    }

    [Fact]
    public void UnbalancedJsonOnFlush_ReleasesHeldTextIntact()
    {
        var sanitizer = new ReasoningTextSanitizer();
        string payload = "to=functions.source_code_view {\"file\":\"src/potion.c\"";
        string out1 = sanitizer.Push(payload);
        string flushed = sanitizer.Flush();
        string total = out1 + flushed;
        Assert.Equal(payload, total);
    }

    [Fact]
    public void HardBailout_4096Chars_ReleasesHeldTextIntact()
    {
        var sanitizer = new ReasoningTextSanitizer();
        string longBody = new string('x', 4100);
        string payload = "to=functions.view {" + longBody;
        string output = sanitizer.Push(payload) + sanitizer.Flush();
        Assert.Contains(longBody, output);
    }

    [Fact]
    public void DoubleNewline_DuringBraceScan_BailsOutAndReleasesText()
    {
        var sanitizer = new ReasoningTextSanitizer();
        string payload = "to=functions.view {\"file\":\"test\"}\n\nParagraph after break";
        string output = sanitizer.Push(payload) + sanitizer.Flush();
        Assert.Contains("Paragraph after break", output);
    }

    [Fact]
    public void NonJsonFollower_StripsMarkerAndPreservesRest()
    {
        var sanitizer = new ReasoningTextSanitizer();
        string payload = "to=functions.view } not a valid json";
        string output = sanitizer.Push(payload) + sanitizer.Flush();
        Assert.Equal(" } not a valid json", output);
    }

    [Fact]
    public void ReplacementCharAndPrivateUseGlyphs_Stripped()
    {
        var sanitizer = new ReasoningTextSanitizer();
        string input = "Valid text \uFFFD and \uE001 private \uF8FF glyphs.";
        string output = sanitizer.Push(input) + sanitizer.Flush();
        Assert.Equal("Valid text  and  private  glyphs.", output);
    }

    [Fact]
    public void GnollHackSentence_WithFilePath_SurvivesUntouched()
    {
        var sanitizer = new ReasoningTextSanitizer();
        string input = "Look at src/potion.c around line 2197 for the potionhit function.";
        string output = sanitizer.Push(input) + sanitizer.Flush();
        Assert.Equal(input, output);
    }

    [Fact]
    public void StatelessSweep_PreservesAiThoughtDivs()
    {
        string input = "Answer text\n\n<div class=\"ai-thought\">\n\nThought prose\n\n</div>\n\nFinal text";
        string output = ReasoningTextSanitizer.SanitizeStateless(input);
        Assert.Equal(input, output);
    }
}
