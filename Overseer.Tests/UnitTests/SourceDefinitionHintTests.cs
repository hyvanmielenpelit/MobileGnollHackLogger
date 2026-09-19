namespace Overseer.Tests.UnitTests;

using System;
using Overseer.Services.Tools;
using Xunit;

/// <summary>
/// The pointer to get_function_definition that source_code_search and source_code_view append when
/// a result shows a function definition. Fixtures are results recorded in benchmark run 56.
/// </summary>
public class SourceDefinitionHintTests
{
    private const string StudyBookSearch =
        "--- src/spell.c:L721 ---\n" +
        "    721: \n" +
        "    722:     Sprintf(buf, \"%s %s\", spelltypesymbol(objects[booktype].oc_skill), lvlbuf);\n" +
        "    723: }\n" +
        "    724: \n" +
        "    725: int\n" +
        ">>> 726: study_book(struct obj *spellbook)\n" +
        "    727: {\n" +
        "    728:     if (!spellbook)\n" +
        "    729:         return 0;\n" +
        "    730: \n" +
        "    731:     int booktype = spellbook->otyp;";

    private const string LearnPrototypeSearch =
        "--- src/spell.c:L31 ---\n" +
        "    31: static int spell_let_to_idx(char);\n" +
        "    32: static boolean cursed_book(struct obj * bp);\n" +
        "    33: static boolean confused_book(struct obj *);\n" +
        "    34: static void deadbook(struct obj *);\n" +
        "    35: static void modronbook(struct obj*);\n" +
        ">>> 36: static int learn(void);\n" +
        "    37: static boolean rejectcasting(void);\n" +
        "    38: static boolean reject_specific_spell_casting(int);\n" +
        "    39: static boolean getspell(int *, int);\n" +
        "    40: static int CFDECLSPEC spell_cmp(const genericptr, const genericptr);\n" +
        "    41: static boolean spellsortmenu(void);";

    private const string ScrollTableSearch =
        "--- src/objects.c:L3533 ---\n" +
        "    3533: \n" +
        "    3534: SCROLL(\"mail\",          \"stamped\", None, 0,   0,   0, 0, 0, 0, 0, 0, 0, 0, S1_NONE, O1_NONE, O2_NONE, O3_NO_GENERATION, O4_NONE, O5_NONE, O6_NONE, PERMITTED_ALL),\n" +
        ">>> 3535: SCROLL(\"blank paper\", \"unlabeled\", None, 0,  25,  60, 0, 0, 0, 0, 0, 0, 0, S1_NONE, O1_NONE, O2_NONE, O3_NONE, O4_NONE, O5_OK_FOR_ILLITERATE, O6_NONE, PERMITTED_ALL),\n" +
        "    3536: #undef SCROLL";

    private const string LearnView =
        "--- src/spell.c:L398-L477 ---\r\n" +
        "398: learn(void)\r\n" +
        "399: {\r\n" +
        "400:     int i;\r\n" +
        "401:     short booktype;\r\n" +
        "475:             play_sfx_sound(SFX_ACQUIRE_CONFUSION);\r\n" +
        "476:             make_confused(itimeout_incr(HConfusion, rnd(4) + 5), FALSE);\r\n" +
        "477:         }\r\n";

    private const string WholeFunctionView =
        "--- src/spell.c:L1040-L1103 ---\r\n" +
        "1040: /* return True if spellcasting is inhibited;\r\n" +
        "1041:    only covers a small subset of reasons why casting won't work */\r\n" +
        "1042: static boolean\r\n" +
        "1043: rejectcasting(void)\r\n" +
        "1044: {\r\n" +
        "1101:     return FALSE;\r\n" +
        "1102: }\r\n" +
        "1103: \r\n";

    [Fact]
    public void ForSearchResult_MarkedDefinitionLine_NamesItsFileAndLine()
    {
        string? hint = SourceDefinitionHint.ForSearchResult(StudyBookSearch);

        Assert.Equal(
            "[Definition: study_book() at src/spell.c:726. get_function_definition {\"name\": \"study_book\"} returns the whole body in one call; paging it with source_code_view costs one model round per page.]",
            hint);
    }

    [Fact]
    public void ForSearchResult_PrototypeOnly_ReturnsNull()
    {
        Assert.Null(SourceDefinitionHint.ForSearchResult(LearnPrototypeSearch));
    }

    [Fact]
    public void ForSearchResult_MacroTableRow_ReturnsNull()
    {
        Assert.Null(SourceDefinitionHint.ForSearchResult(ScrollTableSearch));
    }

    [Fact]
    public void ForViewResult_OpeningOnADefinitionThatContinues_ReturnsTheHint()
    {
        string? hint = SourceDefinitionHint.ForViewResult(LearnView);

        Assert.NotNull(hint);
        Assert.StartsWith("[Definition: learn() at src/spell.c:398. get_function_definition {\"name\": \"learn\"}", hint);
    }

    [Fact]
    public void ForViewResult_EndingInAColumnZeroBrace_ReturnsNull()
    {
        Assert.Null(SourceDefinitionHint.ForViewResult(WholeFunctionView));
    }

    [Fact]
    public void ForSearchResult_ThreeDefinitions_NamesTwoWithinTheCap()
    {
        string content =
            "--- src/spell.c:L396 ---\n" +
            "    397: static int\n" +
            ">>> 398: learn(void)\n" +
            "    399: {\n" +
            "\n" +
            "--- src/spell.c:L725 ---\n" +
            "    725: int\n" +
            ">>> 726: study_book(struct obj *spellbook)\n" +
            "    727: {\n" +
            "\n" +
            "--- src/spell.c:L1024 ---\n" +
            "    1024: void\n" +
            ">>> 1025: age_spells(void)\n" +
            "    1026: {";

        string? hint = SourceDefinitionHint.ForSearchResult(content);

        Assert.NotNull(hint);
        Assert.Contains("learn() at src/spell.c:398; study_book() at src/spell.c:726.", hint);
        Assert.DoesNotContain("age_spells", hint);
        Assert.True(hint!.Length <= SourceDefinitionHint.MaxLength);
    }

    [Fact]
    public void ForSearchResult_NetHackRepository_NamesTheRepositoryArgument()
    {
        string? hint = SourceDefinitionHint.ForSearchResult(StudyBookSearch, "nethack");

        Assert.Contains("get_function_definition {\"name\": \"study_book\", \"repository\": \"nethack\"}", hint);
    }

    [Fact]
    public void SearchTool_AppendsTheHintAfterAResultThatFits_AndPutsItFirstWhenTheResultWouldBeCut()
    {
        string appended = SourceCodeSearchTool.AppendDefinitionHint(StudyBookSearch, "gnollhack", 10000);
        Assert.StartsWith(StudyBookSearch, appended);
        Assert.EndsWith("per page.]", appended);

        string prepended = SourceCodeSearchTool.AppendDefinitionHint(StudyBookSearch, "gnollhack", StudyBookSearch.Length);
        Assert.StartsWith("[Definition: study_book()", prepended);
        Assert.EndsWith(StudyBookSearch, prepended);
    }

    [Fact]
    public void ViewTool_PlacesTheHintAheadOfTheTruncationNotice()
    {
        string truncated = LearnView + "[Output truncated at line 7 of 80 requested (file line 477). Call again with start_line=478 to continue.]" + Environment.NewLine;

        string result = SourceCodeViewTool.InsertDefinitionHint(truncated, "gnollhack");

        int hintAt = result.IndexOf("[Definition: learn()", StringComparison.Ordinal);
        int noticeAt = result.IndexOf("[Output truncated at line 7", StringComparison.Ordinal);
        Assert.True(hintAt > 0);
        Assert.True(noticeAt > hintAt);
        Assert.True(result.IndexOf("477:         }", StringComparison.Ordinal) < hintAt);
    }
}
