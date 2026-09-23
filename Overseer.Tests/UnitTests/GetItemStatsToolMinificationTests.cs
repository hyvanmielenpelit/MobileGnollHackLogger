using System.Collections.Generic;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Overseer.Services;
using Overseer.Services.Tools;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// Covers <see cref="GetItemStatsTool.Minify"/> in isolation: the fixed truncation sentence, the
/// Level 2 failure reason appended to it, which macro definition survives minification, and the
/// case where even the one invoked macro does not fit under the threshold. No wiki or source
/// corpus is involved — every input is a hand-built <see cref="StatsResponse{ItemStats}"/>.
/// </summary>
public class GetItemStatsToolMinificationTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private const string TruncationSentence = "Response truncated due to size limits. Macro definitions and flag descriptions omitted.";

    [Fact]
    public void Level2FailureMessage_IsKeptAfterTheFixedSentence()
    {
        var result = new StatsResponse<ItemStats>
        {
            RawDefinition = "SPELL(\"scroll of light\", ...)",
            Message = "Raw source for 'scroll of light', with the macro and struct definitions needed to read it. " +
                      "Interpret the macro invocation against those definitions. " +
                      "Structured values were not available: unresolved macro chain for 'scroll of light'.",
            MacroDefinitions = new Dictionary<string, string> { { "SPELL", "#define SPELL(...) { ... }" } }
        };

        var minified = GetItemStatsTool.Minify(result, Options, threshold: 100_000);

        Assert.StartsWith(TruncationSentence, minified.Message);
        Assert.Contains("Structured values were not available: unresolved macro chain for 'scroll of light'.", minified.Message);
    }

    [Fact]
    public void InvokedMacro_IsKept_EveryOtherMacroIsDropped()
    {
        var result = new StatsResponse<ItemStats>
        {
            RawDefinition = "  SPELL(\"scroll of light\", ...)",
            Message = "Structured values were not available: unresolved macro chain.",
            MacroDefinitions = new Dictionary<string, string>
            {
                { "SPELL", "#define SPELL(...) { ... }" },
                { "OBJ", "#define OBJ(...) { ... }" },
                { "BITS", "#define BITS(...) { ... }" }
            }
        };

        var minified = GetItemStatsTool.Minify(result, Options, threshold: 100_000);

        var macro = Assert.Single(minified.MacroDefinitions);
        Assert.Equal("SPELL", macro.Key);
        Assert.Equal("#define SPELL(...) { ... }", macro.Value);
    }

    [Fact]
    public void InvokedMacro_IsDropped_WhenKeepingItWouldExceedTheThreshold()
    {
        var result = new StatsResponse<ItemStats>
        {
            RawDefinition = "SPELL(\"scroll of light\", ...)",
            Message = "Structured values were not available: unresolved macro chain.",
            MacroDefinitions = new Dictionary<string, string> { { "SPELL", new string('x', 20_000) } }
        };

        // Below the size the invoked macro alone would add, but comfortably above the fixed
        // sentence plus the raw definition on their own.
        var minified = GetItemStatsTool.Minify(result, Options, threshold: 500);

        Assert.Empty(minified.MacroDefinitions);
    }

    [Fact]
    public void Level1Result_WithStatsAndNoFailurePhrase_KeepsTodaysMessageExactly()
    {
        var result = new StatsResponse<ItemStats>
        {
            Stats = new ItemStats(),
            RawDefinition = "SPELL(\"scroll of light\", ...)",
            Message = "Level 1 resolution succeeded with no failure to report."
        };

        var minified = GetItemStatsTool.Minify(result, Options, threshold: 100_000);

        Assert.Equal(TruncationSentence, minified.Message);
    }
}
