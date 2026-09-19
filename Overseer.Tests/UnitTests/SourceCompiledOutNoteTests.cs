namespace Overseer.Tests.UnitTests;

using Overseer.Services.Tools;
using Xunit;

/// <summary>
/// The "[Not compiled: ...]" note that get_function_definition, source_code_view and
/// source_code_search append when a shown range or match sits inside a "#if 0" region.
/// </summary>
public class SourceCompiledOutNoteTests
{
    // A single disabled block: lines 3-6 sit between "#if 0" (line 2) and "#endif" (line 7).
    private static readonly string[] SingleBlockFile = new[]
    {
        "line 1",
        "#if 0",
        "int unused_function(void)",
        "{",
        "    return 0;",
        "}",
        "#endif",
        "int real_function(void)",
        "{",
        "    return 1;",
        "}",
    };

    // Mirrors the shape of GnollHack's study_book: a disabled prompt replaced by a live
    // alternative right after the "#endif".
    private static readonly string[] StudyBookShapedFile = new[]
    {
        "int",
        "study_book(struct obj *spellbook)",
        "{",
        "    int booktype = spellbook->otyp;",
        "#if 0",
        "    /* old confirmation prompt, replaced by menu */",
        "    if (yn(\"Really read?\") == 'n')",
        "        return 0;",
        "#endif",
        "    if (!confirm_read(spellbook))",
        "        return 0;",
        "    return learn_spell(booktype);",
        "}",
    };

    [Fact]
    public void FindRanges_QueryFullyContainsRegion_ReturnsTheWholeRegion()
    {
        var ranges = SourceCompiledOutNote.FindRanges(SingleBlockFile, 1, SingleBlockFile.Length);

        Assert.Equal(new[] { (3, 6) }, ranges);
    }

    [Fact]
    public void FindRanges_QueryPartlyOverlapsRegion_ClipsToTheQuery()
    {
        var ranges = SourceCompiledOutNote.FindRanges(SingleBlockFile, 5, 9);

        Assert.Equal(new[] { (5, 6) }, ranges);
    }

    [Fact]
    public void FindRanges_QueryStartsInsideARegionThatOpenedEarlier_ClipsToTheQueryStart()
    {
        var ranges = SourceCompiledOutNote.FindRanges(SingleBlockFile, 4, 9);

        Assert.Equal(new[] { (4, 6) }, ranges);
    }

    [Fact]
    public void FindRanges_NestedIfdefInsideIfZero_DoesNotCloseTheOuterRegion()
    {
        string[] lines =
        {
            "#if 0",
            "#ifdef DEBUG",
            "    foo();",
            "#endif",
            "    bar();",
            "#endif",
            "live();",
        };

        var ranges = SourceCompiledOutNote.FindRanges(lines, 1, lines.Length);

        // The inner "#ifdef"/"#endif" pair is swallowed into the one outer region; the
        // trailing "live();" line is not part of it.
        Assert.Equal(new[] { (2, 5) }, ranges);
    }

    [Fact]
    public void FindRanges_ElseEndsTheZeroRegion()
    {
        string[] lines =
        {
            "#if 0",
            "    old_code();",
            "#else",
            "    new_code();",
            "#endif",
        };

        var ranges = SourceCompiledOutNote.FindRanges(lines, 1, lines.Length);

        Assert.Equal(new[] { (2, 2) }, ranges);
        Assert.False(SourceCompiledOutNote.IsInsideCompiledOutRegion(lines, 4));
    }

    [Fact]
    public void FindRanges_WhitespaceBeforeAndInsideTheDirective_IsRecognised()
    {
        string[] lines = { "a", "  #   if   0", "compiled", "#endif", "b" };

        var ranges = SourceCompiledOutNote.FindRanges(lines, 1, lines.Length);

        Assert.Equal(new[] { (3, 3) }, ranges);
    }

    [Fact]
    public void FindRanges_IfdefIsNeverTreatedAsCompiledOut()
    {
        string[] lines = { "#ifdef SOME_FLAG", "maybe_compiled();", "#endif" };

        var ranges = SourceCompiledOutNote.FindRanges(lines, 1, lines.Length);

        Assert.Empty(ranges);
    }

    [Fact]
    public void Build_NoRegionInRange_ReturnsNull()
    {
        string[] lines = { "int x = 1;", "int y = 2;" };

        Assert.Null(SourceCompiledOutNote.Build(lines, 1, lines.Length));
    }

    [Fact]
    public void Build_SingleLineRegion_UsesSingularWording()
    {
        string[] lines = { "#if 0", "old();", "#endif" };

        string? note = SourceCompiledOutNote.Build(lines, 1, lines.Length);

        Assert.Equal(
            "[Not compiled: file line 2 is inside #if 0 ... #endif. They show removed or disabled code, not what the game does; the live code is outside them.]",
            note);
        Assert.True(note!.Length <= SourceCompiledOutNote.MaxLength);
    }

    [Fact]
    public void Build_MultiLineRegion_UsesPluralWording()
    {
        string? note = SourceCompiledOutNote.Build(SingleBlockFile, 1, SingleBlockFile.Length);

        Assert.Equal(
            "[Not compiled: file lines 3-6 are inside #if 0 ... #endif. They show removed or disabled code, not what the game does; the live code is outside them.]",
            note);
    }

    [Fact]
    public void Build_MoreThanThreeRanges_NamesFirstThreeAndCountsTheRest()
    {
        string[] lines =
        {
            "#if 0", "a1();", "#endif",     // lines 1-3, region (2,2)
            "#if 0", "a2();", "#endif",     // lines 4-6, region (5,5)
            "#if 0", "a3();", "#endif",     // lines 7-9, region (8,8)
            "#if 0", "a4();", "#endif",     // lines 10-12, region (11,11)
            "#if 0", "a5();", "#endif",     // lines 13-15, region (14,14)
        };

        string? note = SourceCompiledOutNote.Build(lines, 1, lines.Length);

        Assert.NotNull(note);
        Assert.Contains("file lines 2, 5, 8 and 2 more are inside #if 0 ... #endif.", note);
        Assert.True(note!.Length <= SourceCompiledOutNote.MaxLength);
    }

    [Fact]
    public void Build_StudyBookShapedFixture_NamesOnlyTheDisabledBlock()
    {
        var ranges = SourceCompiledOutNote.FindRanges(StudyBookShapedFile, 1, StudyBookShapedFile.Length);
        Assert.Equal(new[] { (6, 8) }, ranges);

        string? note = SourceCompiledOutNote.Build(StudyBookShapedFile, 1, StudyBookShapedFile.Length);

        Assert.NotNull(note);
        Assert.Contains("file lines 6-8", note);
        Assert.DoesNotContain("10-12", note);
    }

    [Fact]
    public void ForViewResult_NumberedBodyInsideARegion_ProducesTheNote()
    {
        string content = "--- src/spell.c:L3-L6 ---\r\n"
            + "3: int unused_function(void)\r\n"
            + "4: {\r\n"
            + "5:     return 0;\r\n"
            + "6: }\r\n";

        string? note = SourceCompiledOutNote.ForViewResult(content, path => SingleBlockFile);

        Assert.Equal(
            "[Not compiled: file lines 3-6 are inside #if 0 ... #endif. They show removed or disabled code, not what the game does; the live code is outside them.]",
            note);
    }

    [Fact]
    public void ForFunctionDefinitionResult_TruncatedBody_UsesTheLastShownFileLine()
    {
        string content = "--- src/spell.c:L1-L11 (real_function, 11 lines) ---\n"
            + "line 1\n"
            + "#if 0\n"
            + "\n[Output truncated at line 2 of 11. Call again with start_line=3 (file line 4) to continue.]";

        string? note = SourceCompiledOutNote.ForFunctionDefinitionResult(content, path => SingleBlockFile);

        // Only file line 3 was actually shown (the notice resumes at file line 4), so the
        // region (3-6) is clipped to just line 3.
        Assert.Equal(
            "[Not compiled: file line 3 is inside #if 0 ... #endif. They show removed or disabled code, not what the game does; the live code is outside them.]",
            note);
    }

    [Fact]
    public void ForSearchMatches_MarkedLineInsideARegion_NamesTheFileAndLine()
    {
        string content = "--- src/spell.c:L3 ---\n"
            + ">>> 4: {\n"
            + "    5:     return 0;\n";

        string? note = SourceCompiledOutNote.ForSearchMatches(content, path => SingleBlockFile);

        Assert.Equal("[Not compiled: the match at src/spell.c:4 is inside #if 0 ... #endif.]", note);
    }

    [Fact]
    public void ForSearchMatches_MarkedLineOutsideARegion_ReturnsNull()
    {
        string content = "--- src/spell.c:L8 ---\n"
            + ">>> 8: int real_function(void)\n";

        Assert.Null(SourceCompiledOutNote.ForSearchMatches(content, path => SingleBlockFile));
    }
}
