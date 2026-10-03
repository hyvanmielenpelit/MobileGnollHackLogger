namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Overseer.Services;
using Overseer.Services.Tools;
using Xunit;

/// <summary>
/// The "[Not reachable: …]" note that get_function_definition, source_code_view and
/// source_code_search add when a result shows the definition of a GnollHack function nothing calls,
/// and the <see cref="SourceLivenessIndex"/> behind it.
/// </summary>
public class SourceReachabilityNoteTests
{
    private static readonly string[] PriestC =
    {
        "int",                                          // 1
        "priest_talk(struct monst *priest)",            // 2
        "{",                                            // 3
        "    return 1;",                                // 4
        "}",                                            // 5
        "",                                             // 6
        "int",                                          // 7
        "live_helper(struct monst *mtmp)",              // 8
        "{",                                            // 9
        "    return 0;",                                // 10
        "}",                                            // 11
        "",                                             // 12
        "static int",                                   // 13
        "learn(void)",                                  // 14
        "{",                                            // 15
        "    return 0;",                                // 16
        "}",                                            // 17
        "",                                             // 18
        "int",                                          // 19
        "old_helper(void)",                             // 20
        "{",                                            // 21
        "    return 0;",                                // 22
        "}",                                            // 23
        "",                                             // 24
        "void",                                         // 25
        "dosounds(struct monst *mtmp)",                 // 26
        "{",                                            // 27
        "    // priest_talk(mtmp);",                    // 28
        "    /* priest_talk(mtmp); */",                 // 29
        "    live_helper(mtmp);",                       // 30
        "    set_occupation(learn, \"studying\");",     // 31
        "#if 0",                                        // 32
        "    old_helper();",                            // 33
        "#endif",                                       // 34
        "}"                                             // 35
    };

    private static readonly string[] ExternH =
    {
        "E int priest_talk(struct monst *);",
        "E int live_helper(struct monst *);",
        "#define talk_alias(p) priest_talk_macro(p)"
    };

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> Corpus() =>
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["src/priest.c"] = PriestC,
            ["include/extern.h"] = ExternH
        };

    private static SourceLivenessIndex Index()
    {
        var corpus = Corpus();
        return new SourceLivenessIndex(() => corpus);
    }

    private static string Note(string name) =>
        $"[Not reachable: {name}() has no live call site in the indexed source, so the game never runs it.]";

    [Fact]
    public void IsLive_CommentedOutCallers_AreNotLive()
    {
        Assert.False(Index().IsLive("priest_talk"));
    }

    [Fact]
    public void IsLive_OneLiveCaller_IsLive()
    {
        Assert.True(Index().IsLive("live_helper"));
    }

    [Fact]
    public void IsLive_AFunctionPointerPassed_IsLive()
    {
        Assert.True(Index().IsLive("learn"));
    }

    [Fact]
    public void IsLive_ACallInsideIfZero_IsNotLive()
    {
        Assert.False(Index().IsLive("old_helper"));
    }

    [Fact]
    public void IsLive_AReferenceFromPortCodeOnly_IsLive_ButThePortFileIsNotLookedUp()
    {
        var corpus = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["src/priest.c"] = PriestC,
            ["win/tty/wintty.c"] = new[] { "struct window_procs tty_procs = {", "    priest_talk,", "};" },
            ["win/Qt/qt_win.cpp"] = new[] { "void f()", "{", "    old_helper();", "}" }
        };
        var index = new SourceLivenessIndex(() => corpus);

        Assert.True(index.IsLive("priest_talk"));
        Assert.True(index.IsLive("old_helper"));
        Assert.Null(index.GetView().DefinitionAt("win/Qt/qt_win.cpp", 1));
    }

    private static readonly string[] CallbackH =
    {
        "#ifdef _WIN32",
        "#define DLLEXPORT __declspec(dllexport)",
        "#elif defined(__GNUC__)",
        "#  define DLLEXPORT __attribute__((visibility(\"default\")))",
        "#else",
        "#define DLLEXPORT",
        "#endif"
    };

    private static readonly string[] GnhapiH =
    {
        "DLLEXPORT int RunGnollHack(",
        "    char* gnhdir,",
        "    int runflags",
        ");"
    };

    private static readonly string[] GnhapiC =
    {
        "int",                                          // 1
        "RunGnollHack(char* gnhdir, int runflags)",     // 2
        "{",                                            // 3
        "    return 0;",                                // 4
        "}",                                            // 5
        "",                                             // 6
        "DLLEXPORT const char*",                        // 7
        "LibGetVersionString(void)",                    // 8
        "{",                                            // 9
        "    return 0;",                                // 10
        "}",                                            // 11
        "",                                             // 12
        "int",                                          // 13
        "unexported_helper(void)",                      // 14
        "{",                                            // 15
        "    return 0;",                                // 16
        "}"                                             // 17
    };

    private static SourceLivenessIndex ExportIndex()
    {
        var corpus = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["win/win32/xpl/libshare/callback.h"] = CallbackH,
            ["win/win32/xpl/libshare/gnhapi.h"] = GnhapiH,
            ["win/win32/xpl/libshare/gnhapi.c"] = GnhapiC
        };
        return new SourceLivenessIndex(() => corpus);
    }

    [Fact]
    public void IsLive_AnExportedFunctionWithNoCaller_IsLive()
    {
        var index = ExportIndex();

        Assert.True(index.IsLive("RunGnollHack"));
        Assert.True(index.IsLive("LibGetVersionString"));
        Assert.Null(SourceReachabilityNote.ForFunctionDefinitionResult(
            "--- win/win32/xpl/libshare/gnhapi.c:L1-L5 (RunGnollHack, 5 lines) ---\nint\nRunGnollHack(char* gnhdir, int runflags)\n{\n    return 0;\n}",
            index));
    }

    [Fact]
    public void IsLive_AnUnexportedFunctionWithNoCaller_StillGetsTheNote()
    {
        var index = ExportIndex();

        Assert.False(index.IsLive("unexported_helper"));
        Assert.Equal(Note("unexported_helper"), SourceReachabilityNote.ForFunctionDefinitionResult(
            "--- win/win32/xpl/libshare/gnhapi.c:L13-L17 (unexported_helper, 5 lines) ---\nint\nunexported_helper(void)\n{\n    return 0;\n}",
            index));
    }

    [Fact]
    public void Exported_TheDllExportDefines_NameNoFunction()
    {
        var view = ExportIndex().GetView();

        Assert.Equal(new[] { "LibGetVersionString", "RunGnollHack" }, view.Exported.OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public void IsLive_AnUnavailableCorpus_IsNull()
    {
        var index = new SourceLivenessIndex(() => throw new InvalidOperationException("index not ready"));

        Assert.Null(index.IsLive("priest_talk"));
        Assert.Null(SourceReachabilityNote.ForFunctionDefinitionResult("--- src/priest.c:L1-L5 (priest_talk, 5 lines) ---\nint", index));
    }

    [Fact]
    public void IsLive_ANewCorpusInstance_IsRebuilt()
    {
        var first = Corpus();
        var second = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["src/priest.c"] = PriestC,
            ["src/sounds.c"] = new[] { "void", "chat(void)", "{", "    priest_talk(0);", "}" }
        };
        var current = first;
        var index = new SourceLivenessIndex(() => current);

        Assert.False(index.IsLive("priest_talk"));
        current = second;
        Assert.True(index.IsLive("priest_talk"));
    }

    [Fact]
    public void IsLive_AWarmLookup_TakesUnderAMillisecond()
    {
        var index = Index();
        Assert.False(index.IsLive("priest_talk"));

        const int lookups = 1000;
        var stopwatch = Stopwatch.StartNew();
        for (int i = 0; i < lookups; i++) index.IsLive("priest_talk");
        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed.TotalMilliseconds / lookups < 1.0, $"{stopwatch.Elapsed.TotalMilliseconds / lookups:F4} ms per lookup");
    }

    [Fact]
    public void Build_NamesTheFunction_WithinTheCap()
    {
        string? note = SourceReachabilityNote.Build("priest_talk");

        Assert.Equal(Note("priest_talk"), note);
        Assert.True(note!.Length <= SourceReachabilityNote.MaxLength);
        Assert.Null(SourceReachabilityNote.Build(new string('x', 300)));
    }

    [Fact]
    public void ForFunctionDefinitionResult_AnUnreachableFunction_GetsTheNote()
    {
        string content = "--- src/priest.c:L1-L5 (priest_talk, 5 lines) ---\n"
            + "int\n"
            + "priest_talk(struct monst *priest)\n"
            + "{\n"
            + "    return 1;\n"
            + "}";

        Assert.Equal(Note("priest_talk"), SourceReachabilityNote.ForFunctionDefinitionResult(content, Index()));
    }

    [Theory]
    [InlineData("--- src/priest.c:L7-L11 (live_helper, 5 lines) ---\nint\nlive_helper(struct monst *mtmp)\n{\n    return 0;\n}")]
    [InlineData("--- src/priest.c:L13-L17 (learn, 5 lines) ---\nstatic int\nlearn(void)\n{\n    return 0;\n}")]
    // A macro is not a function definition.
    [InlineData("--- include/extern.h:L3-L3 (talk_alias, 1 lines) ---\n#define talk_alias(p) priest_talk_macro(p)")]
    // A file the index does not carry.
    [InlineData("--- util/makedefs.c:L1-L5 (priest_talk, 5 lines) ---\nint")]
    public void ForFunctionDefinitionResult_AReachableFunctionOrNoFunction_GetsNoNote(string content)
    {
        Assert.Null(SourceReachabilityNote.ForFunctionDefinitionResult(content, Index()));
    }

    [Fact]
    public void ForFunctionDefinitionResult_NoIndex_GetsNoNote()
    {
        Assert.Null(SourceReachabilityNote.ForFunctionDefinitionResult("--- src/priest.c:L1-L5 (priest_talk, 5 lines) ---\nint", null));
    }

    [Fact]
    public void ForViewResult_NamesTheFirstUnreachableDefinitionShown()
    {
        // learn (line 14) is reached through a function pointer; old_helper (line 20) only from #if 0.
        string content = "--- src/priest.c:L12-L23 ---\r\n"
            + string.Concat(Enumerable.Range(12, 12).Select(n => $"{n}: {PriestC[n - 1]}\r\n"));

        Assert.Equal(Note("old_helper"), SourceReachabilityNote.ForViewResult(content, Index()));
    }

    [Fact]
    public void ForViewResult_NoDefinitionShown_GetsNoNote()
    {
        // Lines 3-5 are priest_talk's body, not its definition line.
        string content = "--- src/priest.c:L3-L5 ---\r\n3: {\r\n4:     return 1;\r\n5: }\r\n";

        Assert.Null(SourceReachabilityNote.ForViewResult(content, Index()));
    }

    [Fact]
    public void ForSearchMatches_AMarkedUnreachableDefinition_GetsTheNote()
    {
        string content = "--- src/priest.c:L1 ---\n"
            + "    1: int\n"
            + ">>> 2: priest_talk(struct monst *priest)\n"
            + "    3: {\n";

        Assert.Equal(Note("priest_talk"), SourceReachabilityNote.ForSearchMatches(content, Index()));
    }

    [Fact]
    public void ForSearchMatches_OnlyACallMarked_GetsNoNote()
    {
        string content = "--- src/priest.c:L28 ---\n"
            + "    29:     /* priest_talk(mtmp); */\n"
            + ">>> 30:     live_helper(mtmp);\n";

        Assert.Null(SourceReachabilityNote.ForSearchMatches(content, Index()));
    }

    /* ==================================================================================
     * On-disk checks against the GnollHack working copy.
     * =============================================================================== */

    private static readonly Lazy<SourceLivenessIndex?> RealIndex = new(() =>
    {
        var config = new ConfigurationBuilder()
            .AddUserSecrets<SourceReachabilityNoteTests>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        var candidates = new[]
        {
            config["SourceCodePath"],
            Path.Combine(Path.GetDirectoryName(Directory.GetCurrentDirectory()) ?? ".", "GnollHack"),
            @"C:\hmp\GnollHack"
        };

        string? root = candidates
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .FirstOrDefault(c => File.Exists(Path.Combine(c!, "src", "priest.c")));
        if (root == null) return null;

        long maxFileBytes = (int.TryParse(config["MaxSourceFileSizeKB"], out int kb) ? kb : 800) * 1024L;
        var corpus = SourceLivenessIndex.LoadCorpus(root, maxFileBytes);
        return new SourceLivenessIndex(() => corpus);
    });

    /// <summary>The index over the real GnollHack source, or a skip with the reason.</summary>
    private static SourceLivenessIndex RealIndexOrSkip()
    {
        var index = RealIndex.Value;
        if (index == null)
        {
            Assert.Skip(
                "The GnollHack working copy was not found. Set the SourceCodePath user secret "
                + "for Overseer.Tests, or check GnollHack out beside this repository, to run the "
                + "on-disk reachability checks.");
        }

        return index!;
    }

    /// <summary>A source_code_view result showing only the definition line of <paramref name="name"/>.</summary>
    private static string DefinitionView(SourceLivenessIndex index, string path, string name)
    {
        var view = index.TryGetView()!;
        Assert.NotNull(view);
        Assert.True(view.Stripped.TryGetValue(path, out var lines), $"{path} is not in the index.");

        int line = Enumerable.Range(1, lines!.Length).FirstOrDefault(n => view.DefinitionAt(path, n) == name);
        Assert.True(line > 0, $"No definition of {name} in {path}.");
        return $"--- {path}:L{line}-L{line} ---\r\n{line}: {name}(void)\r\n";
    }

    [Fact]
    public void RealCorpus_PriestTalk_GetsTheNote()
    {
        var index = RealIndexOrSkip();

        Assert.False(index.IsLive("priest_talk"));
        Assert.Equal(Note("priest_talk"), SourceReachabilityNote.ForViewResult(DefinitionView(index, "src/priest.c", "priest_talk"), index));
    }

    [Fact]
    public void RealCorpus_Dopray_GetsNoNote()
    {
        var index = RealIndexOrSkip();

        Assert.True(index.IsLive("dopray"));
        Assert.Null(SourceReachabilityNote.ForViewResult(DefinitionView(index, "src/pray.c", "dopray"), index));
    }

    [Fact]
    public void RealCorpus_ExportedFunctions_AreLive()
    {
        var index = RealIndexOrSkip();
        var exported = index.GetView().Exported;

        Assert.Contains("RunGnollHack", exported);
        Assert.Contains("LibGetMaxManuals", exported);
        Assert.DoesNotContain("__declspec", exported);
        Assert.True(index.IsLive("RunGnollHack"));
    }
}
