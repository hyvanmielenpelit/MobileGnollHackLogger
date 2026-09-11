namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Overseer.Services;
using Xunit;

/// <summary>
/// Pins <see cref="SourceCodeService.FindDefinition"/>'s rendered output for each <c>kind</c>, so a
/// change to how the method scans the corpus cannot change what it returns.
/// </summary>
public class SourceCodeServiceDefinitionTests : IDisposable
{
    private readonly string _sourceDir;

    public SourceCodeServiceDefinitionTests()
    {
        _sourceDir = Path.Combine(Path.GetTempPath(), "SourceCodeServiceDefinitionTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_sourceDir, "src"));
        Directory.CreateDirectory(Path.Combine(_sourceDir, "include"));

        // A function definition and an enum body with named members, so the function branch and
        // the enum-member branch (with its 50-line "enum " lookback) are both exercised.
        File.WriteAllText(Path.Combine(_sourceDir, "src", "mon.c"),
            "/* mon.c */\r\n" +
            "#include \"hack.h\"\r\n" +
            "enum monster_flags {\r\n" +
            "    MFLAG_HIDDEN = 1,\r\n" +
            "    MFLAG_PEACEFUL,\r\n" +
            "    MFLAG_TAME,\r\n" +
            "};\r\n" +
            "void\r\n" +
            "make_monster(int mtype)\r\n" +
            "{\r\n" +
            "    struct monst *mtmp = newmonst();\r\n" +
            "    mtmp->mnum = mtype;\r\n" +
            "    return mtmp;\r\n" +
            "}\r\n");

        // A #define, a struct, and a typedef.
        File.WriteAllText(Path.Combine(_sourceDir, "include", "hack.h"),
            "/* hack.h */\r\n" +
            "#define MAX_ENCOUNTERS 64\r\n" +
            "struct encounter_list {\r\n" +
            "    int count;\r\n" +
            "};\r\n" +
            "typedef struct encounter_list encounter_list_t;\r\n");

        // The three headers the win*.h platform heuristic decides between: winprocs.h and
        // wintype.h are real GnollHack headers it spares, wintty.h is a platform header it skips.
        File.WriteAllText(Path.Combine(_sourceDir, "include", "winprocs.h"),
            "/* winprocs.h */\r\n" +
            "struct window_procs {\r\n" +
            "    const char *name;\r\n" +
            "    int wincap;\r\n" +
            "};\r\n");

        File.WriteAllText(Path.Combine(_sourceDir, "include", "wintype.h"),
            "/* wintype.h */\r\n" +
            "typedef int winid;\r\n" +
            "#define WIN_ERR (-1)\r\n");

        File.WriteAllText(Path.Combine(_sourceDir, "include", "wintty.h"),
            "/* wintty.h */\r\n" +
            "struct tty_only_marker {\r\n" +
            "    int unused;\r\n" +
            "};\r\n");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_sourceDir)) Directory.Delete(_sourceDir, true);
        }
        catch (IOException)
        {
            /* A temp directory the OS still holds a handle on is not a test failure. */
        }
    }

    private SourceCodeService CreateService()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new KeyValuePair<string, string?>("SourceCodePath", _sourceDir),
                new KeyValuePair<string, string?>("MaxSourceFileSizeKB", "800")
            })
            .Build();

        var service = new SourceCodeService(config, NullLogger<SourceCodeService>.Instance);
        service.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        return service;
    }

    [Fact]
    public void FindDefinition_Function_ReturnsTheFunctionAndItsContext()
    {
        using var service = CreateService();

        string result = service.FindDefinition("make_monster", "function");

        string expected = string.Join(Environment.NewLine, new[]
        {
            "--- src/mon.c:L9 ---",
            "    7: };",
            "    8: void",
            ">>> 9: make_monster(int mtype)",
            "    10: {",
            "    11:     struct monst *mtmp = newmonst();",
            "    12:     mtmp->mnum = mtype;",
            "    13:     return mtmp;",
            "    14: }"
        });

        Assert.Equal(expected, result);
    }

    [Fact]
    public void FindDefinition_Macro_ReturnsTheDefineAndItsContext()
    {
        using var service = CreateService();

        string result = service.FindDefinition("MAX_ENCOUNTERS", "macro");

        string expected = string.Join(Environment.NewLine, new[]
        {
            "--- include/hack.h:L2 ---",
            "    1: /* hack.h */",
            ">>> 2: #define MAX_ENCOUNTERS 64",
            "    3: struct encounter_list {",
            "    4:     int count;",
            "    5: };",
            "    6: typedef struct encounter_list encounter_list_t;"
        });

        Assert.Equal(expected, result);
    }

    [Fact]
    public void FindDefinition_Struct_ReturnsTheStructAndItsContext()
    {
        using var service = CreateService();

        string result = service.FindDefinition("encounter_list", "struct");

        string expected = string.Join(Environment.NewLine, new[]
        {
            "--- include/hack.h:L3 ---",
            "    2: #define MAX_ENCOUNTERS 64",
            ">>> 3: struct encounter_list {",
            "    4:     int count;",
            "    5: };",
            "    6: typedef struct encounter_list encounter_list_t;"
        });

        Assert.Equal(expected, result);
    }

    [Fact]
    public void FindDefinition_Type_ReturnsTheTypedefAndItsContext()
    {
        using var service = CreateService();

        string result = service.FindDefinition("encounter_list_t", "type");

        string expected = string.Join(Environment.NewLine, new[]
        {
            "--- include/hack.h:L6 ---",
            "    5: };",
            ">>> 6: typedef struct encounter_list encounter_list_t;"
        });

        Assert.Equal(expected, result);
    }

    [Fact]
    public void FindDefinition_Enum_ReturnsTheMemberAndTheLookbackContext()
    {
        using var service = CreateService();

        string result = service.FindDefinition("MFLAG_TAME", "enum");

        string expected = string.Join(Environment.NewLine, new[]
        {
            "--- src/mon.c:L6 ---",
            "    5:     MFLAG_PEACEFUL,",
            ">>> 6:     MFLAG_TAME,",
            "    7: };",
            "    8: void",
            "    9: make_monster(int mtype)",
            "    10: {",
            "    11:     struct monst *mtmp = newmonst();"
        });

        Assert.Equal(expected, result);
    }

    /// <summary>
    /// <c>kind == "any"</c> always widens the leading context by the function/any lookback (2 lines),
    /// even when the match itself came from a different branch — here the typedef branch — rather
    /// than the 1-line lookback that a non-"any", non-"function" kind uses.
    /// </summary>
    [Fact]
    public void FindDefinition_Any_UsesTheFunctionLookbackEvenForATypedefMatch()
    {
        using var service = CreateService();

        string result = service.FindDefinition("encounter_list_t", "any");

        string expected = string.Join(Environment.NewLine, new[]
        {
            "--- include/hack.h:L6 ---",
            "    4:     int count;",
            "    5: };",
            ">>> 6: typedef struct encounter_list encounter_list_t;"
        });

        Assert.Equal(expected, result);
    }

    [Fact]
    public void FindDefinition_Miss_ReturnsTheNoDefinitionMessage()
    {
        using var service = CreateService();

        string result = service.FindDefinition("nosuchsymbol", "any");

        Assert.Equal("No definition found for 'nosuchsymbol' of kind 'any'.", result);
    }

    /// <summary>
    /// <c>include/winprocs.h</c> is a GnollHack header, not a platform header, so the indexer's
    /// <c>win*.h</c> heuristic must leave it in the corpus.
    /// </summary>
    [Fact]
    public void FindDefinition_Struct_ReachesWinprocsHeader()
    {
        using var service = CreateService();

        string result = service.FindDefinition("window_procs", "struct");

        string expected = string.Join(Environment.NewLine, new[]
        {
            "--- include/winprocs.h:L2 ---",
            "    1: /* winprocs.h */",
            ">>> 2: struct window_procs {",
            "    3:     const char *name;",
            "    4:     int wincap;",
            "    5: };"
        });

        Assert.Equal(expected, result);
    }

    /// <summary>
    /// <c>include/wintty.h</c> is a platform header, so the <c>win*.h</c> heuristic keeps it out of
    /// the corpus and nothing it declares is reachable.
    /// </summary>
    [Fact]
    public void FindDefinition_Struct_DoesNotReachAPlatformHeader()
    {
        using var service = CreateService();

        string result = service.FindDefinition("tty_only_marker", "struct");

        Assert.Equal("No definition found for 'tty_only_marker' of kind 'struct'.", result);
    }

    /// <summary>
    /// The two spared headers are both indexed and the platform header is not, as
    /// <see cref="SourceCodeService.ListFiles"/> reports the corpus itself.
    /// </summary>
    [Fact]
    public void ListFiles_WinFilter_ListsTheSparedHeadersOnly()
    {
        using var service = CreateService();

        string result = service.ListFiles("win", includeNetCode: false);

        string expected = string.Join(Environment.NewLine, new[]
        {
            "include/winprocs.h (5 lines)",
            "include/wintype.h (3 lines)",
            "Total: 2 files indexed",
            string.Empty
        });

        Assert.Equal(expected, result);
    }

    /// <summary>
    /// A file under a directory whose name begins with a dot — a Visual Studio <c>.vs</c> cache —
    /// is not indexed, even inside a target directory and with an indexed extension; its sibling is.
    /// </summary>
    [Fact]
    public void ListFiles_FileUnderDotDirectory_IsNotIndexed()
    {
        Directory.CreateDirectory(Path.Combine(_sourceDir, "src", ".vs", "x"));
        File.WriteAllText(Path.Combine(_sourceDir, "src", "a.c"), "/* a.c */\r\nint a;\r\n");
        File.WriteAllText(Path.Combine(_sourceDir, "src", ".vs", "x", "cache.txt"), "encounter cache\r\n");

        using var service = CreateService();

        string result = service.ListFiles(null, includeNetCode: false);

        Assert.Contains("src/a.c (2 lines)", result);
        Assert.DoesNotContain(".vs", result);
        Assert.Equal(string.Empty, service.ListFiles("cache.txt", includeNetCode: false));
    }

    /// <summary>
    /// A file under a "bin" or "obj" directory — build output — is not indexed, even inside a
    /// target directory and with an indexed extension; its sibling is. The rule is segment-based,
    /// so the fixture's use of "src" (the fixture's target directory) rather than the real
    /// "win\win32\xpl\**\(bin|obj)" location is representative.
    /// </summary>
    [Fact]
    public void ListFiles_FileUnderBinOrObjDirectory_IsNotIndexed()
    {
        Directory.CreateDirectory(Path.Combine(_sourceDir, "src", "obj", "Release"));
        Directory.CreateDirectory(Path.Combine(_sourceDir, "src", "bin", "Debug"));
        File.WriteAllText(Path.Combine(_sourceDir, "src", "a.c"), "/* a.c */\r\nint a;\r\n");
        File.WriteAllText(Path.Combine(_sourceDir, "src", "obj", "Release", "list.txt"), "build list\r\n");
        File.WriteAllText(Path.Combine(_sourceDir, "src", "bin", "Debug", "h.h"), "/* h.h */\r\n");

        using var service = CreateService();

        string result = service.ListFiles(null, includeNetCode: false);

        Assert.Contains("src/a.c (2 lines)", result);
        Assert.DoesNotContain("list.txt", result);
        Assert.DoesNotContain("bin/Debug/h.h", result);
        Assert.Equal(string.Empty, service.ListFiles("obj", includeNetCode: false));
        Assert.Equal(string.Empty, service.ListFiles("bin", includeNetCode: false));
    }

    /// <summary>
    /// Builds the service used by the <see cref="SourceCodeService.GetFunctionBody"/> continuation
    /// tests: a small max-chunk size and a function preceded by filler lines, so the extracted
    /// body's file lines (10-24, 1-based) never coincide with its output line numbers (1-15) — the
    /// two numberings a start_line value can be resolved against.
    /// </summary>
    private SourceCodeService CreateServiceWithSmallFunctionBodyChunks()
    {
        var lines = new System.Text.StringBuilder();
        lines.Append("/* big.c */\r\n");
        for (int i = 1; i <= 9; i++) lines.Append($"int filler{i};\r\n");
        lines.Append("void\r\n");
        lines.Append("big_function(void)\r\n");
        lines.Append("{\r\n");
        for (int i = 1; i <= 10; i++) lines.Append($"    line{i};\r\n");
        lines.Append("}\r\n");
        File.WriteAllText(Path.Combine(_sourceDir, "src", "big.c"), lines.ToString());

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new KeyValuePair<string, string?>("SourceCodePath", _sourceDir),
                new KeyValuePair<string, string?>("MaxSourceFileSizeKB", "800"),
                new KeyValuePair<string, string?>("Tools:get_function_definition:MaxLinesPerChunk", "12")
            })
            .Build();

        var service = new SourceCodeService(config, NullLogger<SourceCodeService>.Instance);
        service.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        return service;
    }

    /// <summary>
    /// The extracted body of <c>big_function</c> is file lines 10-24 (1-based) but only 15 output
    /// lines — the function sits well past the top of the file, so the two numberings never agree.
    /// With a 12-line chunk, the first call truncates after output line 12 (file line 21,
    /// <c>line8;</c>) and the notice names the next unseen line as both output line 13 and file
    /// line 22.
    /// </summary>
    [Fact]
    public void GetFunctionBody_FirstChunk_TruncatesWithBothNumberingsInTheNotice()
    {
        using var service = CreateServiceWithSmallFunctionBodyChunks();

        string result = service.GetFunctionBody("big_function", "function");

        // The truncation notice is appended as "\n[Output truncated ...]", a bare LF ahead of the
        // AppendLine-supplied Environment.NewLine — not a second full line break — so it is spelled
        // out here rather than folded into the string.Join(Environment.NewLine, ...) lines above it.
        string expected =
            "--- src/big.c:L10-L24 (big_function, 15 lines) ---" + Environment.NewLine +
            "int filler9;" + Environment.NewLine +
            "void" + Environment.NewLine +
            "big_function(void)" + Environment.NewLine +
            "{" + Environment.NewLine +
            "    line1;" + Environment.NewLine +
            "    line2;" + Environment.NewLine +
            "    line3;" + Environment.NewLine +
            "    line4;" + Environment.NewLine +
            "    line5;" + Environment.NewLine +
            "    line6;" + Environment.NewLine +
            "    line7;" + Environment.NewLine +
            "    line8;" + Environment.NewLine +
            "\n[Output truncated at line 12 of 15. Call again with start_line=13 (file line 22) to continue.]";

        Assert.Equal(expected, result);
    }

    /// <summary>
    /// (a) Passing the notice's output-relative start_line (13) resumes at exactly the line after
    /// the last one shown — <c>line9;</c> — with no line repeated or skipped, and since the
    /// remaining body (3 lines) fits under the 12-line chunk size, this is the whole rest of the
    /// body with no further truncation.
    /// </summary>
    [Fact]
    public void GetFunctionBody_ContinuationByOutputLine_ResumesRightAfterTheLastLineShown()
    {
        using var service = CreateServiceWithSmallFunctionBodyChunks();

        string result = service.GetFunctionBody("big_function", "function", startLineReq: 13);

        string expected = string.Join(Environment.NewLine, new[]
        {
            "--- src/big.c:L10-L24 (big_function, 15 lines) ---",
            "    line9;",
            "    line10;",
            "}"
        });

        Assert.Equal(expected, result);
    }

    /// <summary>
    /// (b) The absolute file line (22) the notice names for the same next line resolves to the
    /// identical continuation as the output-relative value (13) in the previous test.
    /// </summary>
    [Fact]
    public void GetFunctionBody_ContinuationByFileLine_MatchesTheOutputLineContinuation()
    {
        using var service = CreateServiceWithSmallFunctionBodyChunks();

        string byOutputLine = service.GetFunctionBody("big_function", "function", startLineReq: 13);
        string byFileLine = service.GetFunctionBody("big_function", "function", startLineReq: 22);

        Assert.Equal(byOutputLine, byFileLine);
    }

    /// <summary>
    /// (c) A start_line outside both the output range (1-15) and the file range (10-24) returns the
    /// header plus an explicit "outside this definition" message — never a clamp to the last line,
    /// and no body line.
    /// </summary>
    [Fact]
    public void GetFunctionBody_StartLineOutsideBothRanges_ReturnsTheExplicitMessage()
    {
        using var service = CreateServiceWithSmallFunctionBodyChunks();

        string result = service.GetFunctionBody("big_function", "function", startLineReq: 999);

        string expected = string.Join(Environment.NewLine, new[]
        {
            "--- src/big.c:L10-L24 (big_function, 15 lines) ---",
            "start_line 999 is outside this definition: output lines 1–15, file lines 10–24. Call again with a value in either range."
        });

        Assert.Equal(expected, result);
    }

    /// <summary>
    /// start_line 0 is the model's idiom for "from the beginning" — the same result as omitting
    /// start_line entirely, not an out-of-range error.
    /// </summary>
    [Fact]
    public void GetFunctionBody_StartLineZero_StartsAtTheBeginning()
    {
        using var service = CreateServiceWithSmallFunctionBodyChunks();

        string zeroResult = service.GetFunctionBody("big_function", "function", startLineReq: 0);
        string omittedResult = service.GetFunctionBody("big_function", "function", startLineReq: null);

        Assert.Equal(omittedResult, zeroResult);
    }
}
