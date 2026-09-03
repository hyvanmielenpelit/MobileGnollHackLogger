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

    [Fact]
    public void ToolRoutingMarker_WithControlTokensAndJsonLiteralBeforePayload_StrippedWhole()
    {
        var sanitizer = new ReasoningTextSanitizer();
        string payload = "to=functions.search<|constrain|>json<|message|>{\"query\":\"vorpal blade\"} and answer text";
        string output = sanitizer.Push(payload) + sanitizer.Flush();
        Assert.Equal(" and answer text", output);
    }
    // --- Generalised routing marker (Phase A1) ---
    //
    // Every fixture below is taken verbatim from the 2026-09-03 GPT-5.6 Luna benchmark run,
    // where the model emitted tool invocations into the text channel. The old marker regex was
    // pinned to `to=functions.<name>`, so all of this reached the assessor as authored prose.

    [Fact]
    public void MultiToolUseParallelMarker_WithBatchPayload_StrippedWhole()
    {
        string input =
            "I’m locating the shared saving-throw routine. to=multi_tool_use.parallel code\n" +
            "{\"tool_uses\":[{\"recipient_name\":\"functions.source_code_search\"," +
            "\"parameters\":{\"query\":\"check_ability_resistance_success\",\"repository\":\"gnollhack\"}}]}" +
            "\n\nAssuming your spellcast succeeds, GnollHack’s Fear has two defenses:";

        string output = ReasoningTextSanitizer.SanitizeStateless(input);

        Assert.DoesNotContain("to=", output);
        Assert.DoesNotContain("tool_uses", output);
        Assert.DoesNotContain("recipient_name", output);
        Assert.EndsWith("Assuming your spellcast succeeds, GnollHack’s Fear has two defenses:", output);
    }

    [Fact]
    public void BareToolUsesPayload_WithNoRoutingMarker_Stripped()
    {
        string input =
            "{\"tool_uses\":[{\"recipient_name\":\"functions.wiki_search\",\"parameters\":{\"query\":\"Fear spell\"}}]}" +
            "\n\nThe answer body follows.";

        string output = ReasoningTextSanitizer.SanitizeStateless(input);

        Assert.Equal("\n\nThe answer body follows.", output);
    }

    [Theory]
    [InlineData("to=functions.wiki_search")]
    [InlineData("to=multi_tool_use.parallel")]
    [InlineData("to=some_ns.sub_ns.deep_tool")]
    public void NamespacedMarkers_AllRecognised(string marker)
    {
        string input = marker + " code\n{\"query\":\"x\",\"repository\":\"gnollhack\"}\n\nBody.";

        string output = ReasoningTextSanitizer.SanitizeStateless(input);

        Assert.Equal("\n\nBody.", output);
    }

    [Theory]
    [InlineData("code")]
    [InlineData("code:")]
    [InlineData("/json")]
    [InlineData("(json)")]
    [InlineData("(commentary code)")]
    [InlineData("commentary code")]
    public void LeakedChannelLiterals_BetweenMarkerAndPayload_Stripped(string literal)
    {
        string input = "to=functions.get_function_definition  " + literal + "\n" +
                       "{\"symbol\":\"lined_up\",\"kind\":\"function\",\"repository\":\"gnollhack\"}\n\nBody.";

        string output = ReasoningTextSanitizer.SanitizeStateless(input);

        Assert.DoesNotContain(literal, output);
        Assert.DoesNotContain("lined_up", output);
        Assert.Equal("\n\nBody.", output);
    }

    [Fact]
    public void MarkerSplitAcrossDeltas_NonFunctionsNamespace_HeldBackAndStripped()
    {
        var sanitizer = new ReasoningTextSanitizer();
        var sb = new System.Text.StringBuilder();

        foreach (var delta in new[]
                 {
                     "Checking now. to=multi_", "tool_use.par", "allel code\n{\"tool_uses\":[",
                     "{\"recipient_name\":\"functions.x\"}]}", "\n\nDone."
                 })
        {
            sb.Append(sanitizer.Push(delta));
        }
        sb.Append(sanitizer.Flush());

        string output = sb.ToString();
        Assert.DoesNotContain("to=", output);
        Assert.DoesNotContain("multi_tool_use", output);
        Assert.Equal("Checking now. \n\nDone.", output);
    }

    // --- Fence guard (Phase A1) ---
    //
    // A fenced block is authored content, and it is the one place a model legitimately shows
    // raw tool-call JSON to a reader. Nothing inside one may be scrubbed.

    [Fact]
    public void RoutingMarkerInsideFencedBlock_SurvivesUntouched()
    {
        string input =
            "Here is what the leak looks like:\n\n" +
            "```\n" +
            "to=multi_tool_use.parallel code\n" +
            "{\"tool_uses\":[{\"recipient_name\":\"functions.x\"}]}\n" +
            "```\n\n" +
            "That is the artifact.";

        string output = ReasoningTextSanitizer.SanitizeStateless(input);

        Assert.Equal(input, output);
    }

    [Fact]
    public void ToolArgumentJsonInsideFencedJsonBlock_SurvivesUntouched()
    {
        string input =
            "Example arguments:\n\n" +
            "```json\n" +
            "{\"query\":\"lined_up\",\"repository\":\"gnollhack\",\"max_results\":20}\n" +
            "```\n\n" +
            "Pass those to the tool.";

        string output = ReasoningTextSanitizer.SanitizeStateless(input);

        Assert.Equal(input, output);
    }

    [Fact]
    public void LeakOutsideFence_StrippedWhileFencedCopySurvives()
    {
        string input =
            "to=functions.wiki_search code\n{\"query\":\"gnoll\",\"repository\":\"gnollhack\"}\n\n" +
            "The fenced illustration is authored content:\n\n" +
            "```\nto=functions.wiki_search {\"query\":\"gnoll\"}\n```\n";

        string output = ReasoningTextSanitizer.SanitizeStateless(input);

        // The bare leak is gone, the fenced illustration is intact.
        Assert.DoesNotContain("{\"query\":\"gnoll\",\"repository\":\"gnollhack\"}", output);
        Assert.Contains("```\nto=functions.wiki_search {\"query\":\"gnoll\"}\n```", output);
    }

    [Fact]
    public void FenceSplitAcrossDeltas_StateTrackedCorrectly()
    {
        var sanitizer = new ReasoningTextSanitizer();
        var sb = new System.Text.StringBuilder();

        // The opening fence arrives one backtick at a time.
        foreach (var delta in new[] { "Look:\n\n", "`", "`", "`\n", "to=functions.x {\"query\":\"a\"}\n", "```\n" })
        {
            sb.Append(sanitizer.Push(delta));
        }
        sb.Append(sanitizer.Flush());

        Assert.Equal("Look:\n\n```\nto=functions.x {\"query\":\"a\"}\n```\n", sb.ToString());
    }

    [Fact]
    public void InlineBacktickPair_DoesNotOpenAFence()
    {
        // Two backticks are not a fence, and an inline code span mid-line is not a fence either.
        string input = "The `FEAR_RESISTANCE` flag. to=functions.x code\n{\"query\":\"a\",\"repository\":\"gnollhack\"}\n\nBody.";

        string output = ReasoningTextSanitizer.SanitizeStateless(input);

        Assert.Equal("The `FEAR_RESISTANCE` flag. \n\nBody.", output);
    }

    [Fact]
    public void AuthoredJsonInProse_WithoutMarkerOrToolUses_SurvivesUntouched()
    {
        // Rule 1b keys on `tool_uses` alone; an authored JSON object in prose is not touched
        // by this class at all.
        string input = "The config is {\"enabled\":true,\"level\":3} by default.";

        string output = ReasoningTextSanitizer.SanitizeStateless(input);

        Assert.Equal(input, output);
    }
}
