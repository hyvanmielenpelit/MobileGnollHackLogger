namespace Overseer.Tests.UnitTests;

using System.Collections.Generic;
using System.Linq;
using MobileGnollHackLogger.Data;
using Overseer.Services.Benchmarking;
using Xunit;

public class BenchmarkToolResultClassifierTests
{
    private const string PerToolCapSuffix =
        "... [Truncated: showing 10000 of 23456 characters. Narrow the query, or ask for a specific section, to see the rest.]";

    private static BenchmarkRunAnswerToolCall Succeeded(string name, string? result, int? resultLengthChars = null, bool resultTruncated = false)
    {
        return new BenchmarkRunAnswerToolCall
        {
            Name = name,
            Status = "completed",
            Result = result,
            ResultLengthChars = resultLengthChars ?? result?.Length ?? 0,
            ResultTruncated = resultTruncated
        };
    }

    private static BenchmarkRunAnswerToolCall Failed(string name, string error)
    {
        return new BenchmarkRunAnswerToolCall
        {
            Name = name,
            Status = "error",
            Error = error
        };
    }

    private static string StatsJson(string? error = null, string? message = null)
    {
        var parts = new List<string>
        {
            "  \"flag_descriptions\": {}",
            "  \"macro_definitions\": {}",
            "  \"struct_definitions\": {}"
        };
        if (error != null) parts.Add($"  \"error\": \"{error}\"");
        if (message != null) parts.Add($"  \"message\": \"{message}\"");
        return "{\n" + string.Join(",\n", parts) + "\n}";
    }

    // ---------------------------------------------------------------------------------------
    // Outcome and payload availability.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Classify_Outcome_FollowsTheRecorderPartition()
    {
        Assert.Equal(BenchmarkToolCallOutcome.Succeeded,
            BenchmarkToolResultClassifier.Classify(Succeeded("wiki_search", "Article text")).Outcome);
        Assert.Equal(BenchmarkToolCallOutcome.Failed,
            BenchmarkToolResultClassifier.Classify(Failed("source_code_search", "Result too large (12000 chars). Please use a narrower search query to get fewer results.")).Outcome);
        Assert.Equal(BenchmarkToolCallOutcome.RefusedByBudget,
            BenchmarkToolResultClassifier.Classify(Failed("wiki_search", BenchmarkToolCallRecorder.PerQuestionBudgetRefusalMarker + ".")).Outcome);
        Assert.Equal(BenchmarkToolCallOutcome.RefusedByBudget,
            BenchmarkToolResultClassifier.Classify(Failed("wiki_search", BenchmarkToolCallRecorder.BudgetRefusalMarker + ".")).Outcome);
    }

    [Fact]
    public void Classify_ResultTooLarge_IsAFailedCall_NotACut()
    {
        var facets = BenchmarkToolResultClassifier.Classify(
            Failed("get_monster_stats", "Result too large (over 10000 chars) even after minification. Try viewing the source code file directly."));

        Assert.Equal(BenchmarkToolCallOutcome.Failed, facets.Outcome);
        Assert.Equal(BenchmarkToolModelVisibleCut.None, facets.ModelVisibleCut);
        Assert.False(facets.NotFound);
        Assert.Equal(string.Empty, BenchmarkToolResultClassifier.Note(facets));
    }

    [Fact]
    public void Classify_PrunedPayload_IsUnavailable_NeverAHitOrAMiss()
    {
        var facets = BenchmarkToolResultClassifier.Classify(Succeeded("wiki_search", null, resultLengthChars: 4096));

        Assert.True(facets.PayloadUnavailable);
        Assert.False(facets.IsInspectableSuccess);
        Assert.False(facets.NotFound);
        Assert.Equal(BenchmarkToolModelVisibleCut.None, facets.ModelVisibleCut);
        Assert.Equal("unavailable", BenchmarkToolResultClassifier.Note(facets));
    }

    [Fact]
    public void Classify_PrunedPayloadOfACutRecord_KeepsTheRecordCutFlag()
    {
        var facets = BenchmarkToolResultClassifier.Classify(
            Succeeded("source_code_search", null, resultLengthChars: 14000, resultTruncated: true));

        Assert.True(facets.PayloadUnavailable);
        Assert.True(facets.RecordCut);
        Assert.Equal("record cut, unavailable", BenchmarkToolResultClassifier.Note(facets));
    }

    [Fact]
    public void Classify_EmptySuccessfulRecord_IsInspectable_AndNotAMiss()
    {
        var facets = BenchmarkToolResultClassifier.Classify(Succeeded("wiki_search", null, resultLengthChars: 0));

        Assert.False(facets.PayloadUnavailable);
        Assert.True(facets.IsInspectableSuccess);
        Assert.False(facets.NotFound);
    }

    // ---------------------------------------------------------------------------------------
    // Not-found openings, per tool, matched at the start of the result.
    // ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData("source_code_search", "No relevant source code found for 'EXPLODE_FOO' (file_filter='zap.c' may be excluding the match). Try search_definitions for a known symbol, or list_indexed_files to see what is indexed.")]
    [InlineData("source_code_search", "No relevant source code found.")]
    [InlineData("get_function_definition", "No definition found for 'dozap' of kind 'function'. 'dozap' does not occur in the indexed GnollHack source.")]
    [InlineData("search_definitions", "No definition found for 'FOO_BAR' of kind 'any'.")]
    [InlineData("get_constants", "No constants found matching the specified criteria.")]
    [InlineData("list_indexed_files", "No indexed files found matching the filter.")]
    [InlineData("wiki_search", "No GnollHack wiki article matched 'grail healing'. Try a broader query.")]
    [InlineData("wiki_search", "No relevant information found in the GnollHack wiki.")]
    [InlineData("wiki_view", "No wiki article matched 'Holy Grail'. Did you mean: Grail?")]
    [InlineData("wiki_view", "Wiki article matching 'Holy Grail' not found.")]
    [InlineData("nethack_wiki_search", "No relevant information found in the NetHack wiki.")]
    [InlineData("nethack_wiki_view", "No NetHack wiki article matched 'Grail'.")]
    [InlineData("nethack_wiki_view", "NetHack wiki article matching 'Grail' not found.")]
    [InlineData("monster_lookup", "No GnollHack wiki article matched the monster 'gnoll lord'. Both the 'monster' path filter and an unfiltered search of the whole wiki missed.")]
    [InlineData("monster_lookup", "No information found for monster: gnoll lord")]
    [InlineData("item_lookup", "No GnollHack wiki article matched the item 'grail'. Both the 'item' path filter and an unfiltered search of the whole wiki missed.")]
    [InlineData("item_lookup", "No information found for item: grail")]
    public void Classify_MissOpening_IsNotFound(string tool, string result)
    {
        var facets = BenchmarkToolResultClassifier.Classify(Succeeded(tool, result));

        Assert.True(facets.NotFound);
        Assert.False(facets.ContentError);
        Assert.Equal("miss", BenchmarkToolResultClassifier.Note(facets));
    }

    [Theory]
    [InlineData("wiki_view", "Several wiki articles are titled 'Grail': Grail (item), Grail (quest). Showing the first.")]
    [InlineData("nethack_wiki_view", "[No NetHack wiki article titled 'Grail'. Showing 'Holy Grail'.]\n\nThe Holy Grail is ...")]
    [InlineData("wiki_view", "[Section 'Invoking' not found in article. Returning full text.]\n\nThe Holy Grail ...")]
    [InlineData("get_function_definition", "[No function named 'SPELLTOOL' in the indexed GnollHack source; showing the macro definition instead.]\n#define SPELLTOOL(...)")]
    [InlineData("source_code_search", "[Note: No exact case match found. Falling back to case-insensitive search.]\n\nsrc/zap.c:120: explode(...)")]
    [InlineData("source_code_search", "[Note: Regex search failed. Falling back to literal text search.]\n\nsrc/zap.c:120: explode(...)")]
    [InlineData("source_code_search", "src/zap.c (3 matches)")]
    [InlineData("wiki_search", "No definition found for 'x' — quoted from a wiki page body.")]
    public void Classify_LookalikesAndShortHits_AreNotMisses(string tool, string result)
    {
        var facets = BenchmarkToolResultClassifier.Classify(Succeeded(tool, result));

        Assert.False(facets.NotFound);
        Assert.Equal(string.Empty, BenchmarkToolResultClassifier.Note(facets));
    }

    [Fact]
    public void Classify_KnowledgeArticleMiss_IsTheOneFailedNotFound()
    {
        var facets = BenchmarkToolResultClassifier.Classify(
            Failed("get_knowledge_article", "Article not found for topic 'grails'. Available topics: artifacts, prayer"));

        Assert.Equal(BenchmarkToolCallOutcome.Failed, facets.Outcome);
        Assert.True(facets.NotFound);
        Assert.Equal("miss", BenchmarkToolResultClassifier.Note(facets));
    }

    [Fact]
    public void Classify_OtherFailures_AreNotMisses()
    {
        Assert.False(BenchmarkToolResultClassifier.Classify(
            Failed("wiki_search", "Article not found for topic 'x'.")).NotFound);
        Assert.False(BenchmarkToolResultClassifier.Classify(
            Failed("get_knowledge_article", "Tool execution timed out after 30 seconds.")).NotFound);
    }

    // ---------------------------------------------------------------------------------------
    // The stats tools' JSON payloads.
    // ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData("get_monster_stats", "No monster named 'little dgo' found in the game data. Try monster_lookup or wiki_search for partial matches, or check the spelling.")]
    [InlineData("get_item_stats", "No item named 'grail' found in the game data. Did you mean: grail of healing. Try item_lookup or wiki_search for partial matches, or check the spelling.")]
    [InlineData("get_artifact_stats", "No artifact named 'Holy Grail' found in the game data. Try wiki_search for partial matches, or check the spelling.")]
    public void Classify_StatsNotFoundError_IsAMiss(string tool, string error)
    {
        var facets = BenchmarkToolResultClassifier.Classify(Succeeded(tool, StatsJson(error: error)));

        Assert.True(facets.NotFound);
        Assert.False(facets.ContentError);
    }

    [Theory]
    [InlineData("src/monst.c is not in the source code index. Ensure the GnollHack repository is indexed. Use monster_lookup or wiki_search as a fallback.")]
    [InlineData("Failed to parse monster definition block.")]
    public void Classify_StatsStructuredError_IsAContentError_NotAMiss(string error)
    {
        var facets = BenchmarkToolResultClassifier.Classify(Succeeded("get_monster_stats", StatsJson(error: error)));

        Assert.False(facets.NotFound);
        Assert.True(facets.ContentError);
        Assert.Equal("content error", BenchmarkToolResultClassifier.Note(facets));
    }

    [Fact]
    public void Classify_StatsMessageWithoutError_IsADegradedSuccess_WithNoFacet()
    {
        var facets = BenchmarkToolResultClassifier.Classify(Succeeded("get_item_stats",
            StatsJson(message: "Raw source for 'grail of healing'. Structured values were not available: object_class mismatch")));

        Assert.False(facets.NotFound);
        Assert.False(facets.ContentError);
        Assert.False(facets.Partial);
        Assert.Equal(string.Empty, BenchmarkToolResultClassifier.Note(facets));
    }

    [Fact]
    public void Classify_StatsMinificationMessage_IsPartial()
    {
        var facets = BenchmarkToolResultClassifier.Classify(Succeeded("get_artifact_stats",
            StatsJson(message: "Response truncated due to size limits. Flag descriptions omitted.")));

        Assert.True(facets.Partial);
        Assert.Equal("partial", BenchmarkToolResultClassifier.Note(facets));
    }

    [Fact]
    public void Classify_StatsJsonThatDoesNotParse_IsAContentError_NeverAHit()
    {
        var facets = BenchmarkToolResultClassifier.Classify(Succeeded("get_monster_stats", "{ \"stats\": { \"mname\": "));

        Assert.True(facets.ContentError);
        Assert.False(facets.NotFound);
    }

    [Fact]
    public void Classify_StatsRecordCutMidJson_IsARecordCut_NotAContentError()
    {
        string cut = "{\n  \"error\": \"No monster named 'dgo' found in the game data. Try monster_lookup" +
            "... [Record truncated: stored 80 of 400 characters]";
        var facets = BenchmarkToolResultClassifier.Classify(
            Succeeded("get_monster_stats", cut, resultLengthChars: 400, resultTruncated: true));

        Assert.True(facets.RecordCut);
        Assert.False(facets.ContentError);
        Assert.True(facets.NotFound);
        Assert.Equal("miss, record cut", BenchmarkToolResultClassifier.Note(facets));
    }

    // ---------------------------------------------------------------------------------------
    // Content errors returned as success.
    // ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData("source_code_search", "Error: Invalid regular expression. Unterminated [] set.")]
    [InlineData("source_code_view", "Error: File 'src/zapp.c' not found in indexed source code.")]
    [InlineData("source_code_view", "Error: Search term 'dozap' not found in file 'src/zap.c'.")]
    public void Classify_ErrorContentOnASucceededCall_IsAContentError(string tool, string result)
    {
        var facets = BenchmarkToolResultClassifier.Classify(Succeeded(tool, result));

        Assert.True(facets.ContentError);
        Assert.False(facets.NotFound);
        Assert.Equal("content error", BenchmarkToolResultClassifier.Note(facets));
    }

    // ---------------------------------------------------------------------------------------
    // Cuts, each matched where its producer puts it.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Classify_PerToolCapAtTheEnd_IsAModelVisibleCut()
    {
        var facets = BenchmarkToolResultClassifier.Classify(
            Succeeded("source_code_search", new string('x', 200) + PerToolCapSuffix));

        Assert.Equal(BenchmarkToolModelVisibleCut.PerToolCap, facets.ModelVisibleCut);
        Assert.False(facets.RecordCut);
        Assert.Equal("cut", BenchmarkToolResultClassifier.Note(facets));
    }

    [Fact]
    public void Classify_PerToolCapMarkerQuotedMidResult_IsNotACut()
    {
        string result = "src/foo.c:10: /* " + PerToolCapSuffix + " */\nsrc/foo.c:11: return 0;";
        var facets = BenchmarkToolResultClassifier.Classify(Succeeded("source_code_search", result));

        Assert.Equal(BenchmarkToolModelVisibleCut.None, facets.ModelVisibleCut);
    }

    [Theory]
    [InlineData("partial text\n\n... (truncated: batch output budget reached)", BenchmarkToolModelVisibleCut.BatchBudget)]
    [InlineData("(skipped: batch output budget reached)", BenchmarkToolModelVisibleCut.BatchBudget)]
    [InlineData("partial text\n\n[Tool output truncated: cumulative turn limit reached]", BenchmarkToolModelVisibleCut.TurnLimit)]
    [InlineData("[Tool output omitted: cumulative turn limit reached]", BenchmarkToolModelVisibleCut.TurnLimit)]
    [InlineData("partial text\n\n[Tool output truncated: cumulative turn limit reached]\n\n[Tool budget: 3 of 25 calls remaining for this question.]", BenchmarkToolModelVisibleCut.TurnLimit)]
    public void Classify_BatchAndTurnMarkers_AreTheirOwnLayers(string result, BenchmarkToolModelVisibleCut expected)
    {
        var facets = BenchmarkToolResultClassifier.Classify(Succeeded("wiki_search", result));

        Assert.Equal(expected, facets.ModelVisibleCut);
        Assert.Equal("cut", BenchmarkToolResultClassifier.Note(facets));
    }

    [Fact]
    public void Classify_RecordTruncationMarker_IsARecordCut_NotAModelVisibleCut()
    {
        string stored = new string('x', 100) + "... [Record truncated: stored 100 of 30000 characters]";
        var facets = BenchmarkToolResultClassifier.Classify(
            Succeeded("wiki_search", stored, resultLengthChars: 30000, resultTruncated: true));

        Assert.True(facets.RecordCut);
        Assert.Equal(BenchmarkToolModelVisibleCut.None, facets.ModelVisibleCut);
        Assert.Equal("record cut", BenchmarkToolResultClassifier.Note(facets));
    }

    [Fact]
    public void Classify_LegacyMarker_OnARecordCutShortOfTheTrueLength_IsTheRecordersCut()
    {
        string stored = new string('x', 100) + "... [Result truncated for length]";
        var facets = BenchmarkToolResultClassifier.Classify(
            Succeeded("source_code_search", stored, resultLengthChars: 20000, resultTruncated: true));

        Assert.True(facets.RecordCut);
        Assert.Equal(BenchmarkToolModelVisibleCut.None, facets.ModelVisibleCut);
    }

    [Fact]
    public void Classify_LegacyMarker_OnAnUncutRecord_IsThePerToolCap()
    {
        string stored = new string('x', 100) + "... [Result truncated for length]";
        var facets = BenchmarkToolResultClassifier.Classify(Succeeded("source_code_search", stored));

        Assert.False(facets.RecordCut);
        Assert.Equal(BenchmarkToolModelVisibleCut.PerToolCap, facets.ModelVisibleCut);
    }

    // ---------------------------------------------------------------------------------------
    // Partial-result notices, reported apart from the generic cap.
    // ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData("source_code_view", "120: int x;\n[Output truncated at line 80 of 200 requested (file line 199). Call again with start_line=201 to continue.]")]
    [InlineData("get_function_definition", "int dozap() {\n\n[Output truncated at line 150 of 400. Call again with start_line=152 (file line 900) to continue.]")]
    [InlineData("source_code_search", "src/zap.c:1: a\n\n[... output truncated ...]\n[Additional matches not shown — refine your query or use source_code_view]")]
    [InlineData("source_code_search", "src/zap.c:1: a\n[... 7 additional match groups in this file hidden ...]\nsrc/wand.c:3: b")]
    [InlineData("nethack_wiki_search", "== Grail ==\ntext... [Article truncated: showing 3000 of 9000 characters. Use nethack_wiki_view for the full article.]")]
    [InlineData("wiki_view", "[Article is 17840 characters; the first 9800 are shown. Headings: Overview; Special Sacrifices. Call wiki_view again with section set to one of them to read the rest.]\n--- Sacrifice Offering.md ---\ntext")]
    public void Classify_PartialNotice_IsPartial_NotACut(string tool, string result)
    {
        var facets = BenchmarkToolResultClassifier.Classify(Succeeded(tool, result));

        Assert.True(facets.Partial);
        Assert.Equal(BenchmarkToolModelVisibleCut.None, facets.ModelVisibleCut);
        Assert.Equal("partial", BenchmarkToolResultClassifier.Note(facets));
    }

    [Fact]
    public void Classify_ShowingNOfMLine_IsAHitCount_NotACut()
    {
        var facets = BenchmarkToolResultClassifier.Classify(Succeeded("wiki_search",
            "Article one...\n\n[Showing 5 of 271 matching articles — narrow the query, or add a distinctive word from the article's title, to see others.]"));

        Assert.False(facets.Partial);
        Assert.Equal(BenchmarkToolModelVisibleCut.None, facets.ModelVisibleCut);
        Assert.False(facets.NotFound);
    }

    [Theory]
    [InlineData("wiki_search", "[Article is 17840 characters; the first 9800 are shown. Headings: Overview. Call wiki_view again with section set to one of them to read the rest.]")]
    [InlineData("wiki_view", "--- Help.md ---\nThe tool may print [Article is 17840 characters; the first 9800 are shown. Headings: Overview.]")]
    public void Classify_WikiViewNotice_ElsewhereThanTheStartOfAWikiViewResult_IsNotPartial(string tool, string result)
    {
        var facets = BenchmarkToolResultClassifier.Classify(Succeeded(tool, result));

        Assert.False(facets.Partial);
    }

    // ---------------------------------------------------------------------------------------
    // Run-level totals against a hand classification.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Summarize_FixtureTotals_EqualAHandClassification()
    {
        var rows = new[]
        {
            Succeeded("wiki_search", "The Holy Grail is a unique artifact."),                                     // hit
            Succeeded("wiki_search", "No GnollHack wiki article matched 'grail healing'."),                       // miss
            Succeeded("source_code_search", "No relevant source code found for 'GRAIL_HEAL'."),                   // miss
            Succeeded("source_code_search", new string('x', 50) + PerToolCapSuffix),                               // per-tool cap
            Succeeded("wiki_view", "abc... [Record truncated: stored 3 of 50 characters]", 50, true),              // record cut
            Succeeded("wiki_search", null, resultLengthChars: 4096),                                             // unavailable
            Failed("get_knowledge_article", "Article not found for topic 'grail'. Available topics: artifacts"),   // failed miss
            Failed("get_monster_stats", "Result too large (over 10000 chars) even after minification."),          // failed
            Failed("source_code_search", BenchmarkToolCallRecorder.PerQuestionBudgetRefusalMarker + "."),          // refused
            Succeeded("source_code_view", "Error: File 'src/x.c' not found in indexed source code."),             // content error
            Succeeded("nethack_wiki_search", "text... [Article truncated: showing 3000 of 9000 characters. Use nethack_wiki_view for the full article.]"), // partial
            Succeeded("wiki_search", "(skipped: batch output budget reached)"),                                   // batch budget
            Succeeded("source_code_search", "abc\n\n[Tool output truncated: cumulative turn limit reached]")      // turn limit
        };

        var summary = BenchmarkToolResultClassifier.Summarize(rows);

        Assert.Equal(9, summary.InspectableSuccessful);
        Assert.Equal(2, summary.NotFound);
        Assert.Equal(
            new[] { new KeyValuePair<string, int>("source_code_search", 1), new KeyValuePair<string, int>("wiki_search", 1) },
            summary.NotFoundByTool.ToArray());
        Assert.Equal(1, summary.FailedNotFound);
        Assert.Equal("get_knowledge_article", Assert.Single(summary.FailedNotFoundByTool).Key);
        Assert.Equal(1, summary.Unavailable);
        Assert.Equal(1, summary.PerToolCap);
        Assert.Equal(1, summary.BatchBudget);
        Assert.Equal(1, summary.TurnLimit);
        Assert.Equal(3, summary.ModelVisibleCut);
        Assert.Equal(1, summary.RecordCut);
        Assert.Equal(1, summary.Partial);
        Assert.Equal(1, summary.ContentError);
    }

    [Fact]
    public void Summarize_NullOrEmpty_IsAllZero()
    {
        var summary = BenchmarkToolResultClassifier.Summarize(null!);

        Assert.Equal(0, summary.InspectableSuccessful);
        Assert.Equal(0, summary.NotFound);
        Assert.Empty(summary.NotFoundByTool);
        Assert.Equal(0, summary.ModelVisibleCut);
    }
}
