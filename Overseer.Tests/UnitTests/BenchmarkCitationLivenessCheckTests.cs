namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using MobileGnollHackLogger.Data;
using Overseer.Services;
using Overseer.Services.Benchmarking;
using Xunit;

/// <summary>
/// A verification that cites a GnollHack function nothing calls: how the function is found from the
/// cited line, what counts as a live call site, and how the note demotes the verdict for flags and
/// counts while the stored verdict stays as the verifier gave it.
/// </summary>
public class BenchmarkCitationLivenessCheckTests
{
    private static readonly string[] PriestC =
    {
        "/* priest.c */",                                 // 1
        "#include \"hack.h\"",                            // 2
        "",                                               // 3
        "STATIC_DCL int priest_talk(struct monst *);",    // 4
        "",                                               // 5
        "int",                                            // 6
        "priest_talk(priest)",                            // 7
        "struct monst *priest;",                          // 8
        "{",                                              // 9
        "    int x = 1;",                                 // 10
        "    return x;",                                  // 11
        "}",                                              // 12
        "",                                               // 13
        "void",                                           // 14
        "live_helper(mtmp)",                              // 15
        "struct monst *mtmp;",                            // 16
        "{",                                              // 17
        "    mtmp->mhp = 3;",                             // 18
        "}"                                               // 19
    };

    private static readonly string[] SoundsC =
    {
        "#include \"hack.h\"",
        "",
        "void",
        "dosounds()",
        "{",
        "    // priest_talk(mtmp);",
        "    /* if (x)",
        "        priest_talk(mtmp); */",
        "    live_helper(mtmp);",
        "    pline(\"priest_talk(\");",
        "}"
    };

    private static readonly string[] ExternH =
    {
        "E int priest_talk(struct monst *);",
        "E void live_helper(struct monst *);"
    };

    private static BenchmarkCitationLivenessCheck Check()
    {
        var corpus = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["src/priest.c"] = PriestC,
            ["src/sounds.c"] = SoundsC,
            ["include/extern.h"] = ExternH
        };
        return new BenchmarkCitationLivenessCheck(() => corpus);
    }

    [Fact]
    public void AFunctionWhoseOnlyCallersAreCommentedOut_GetsTheNote()
    {
        // Both callers are commented out (// and /* */), one line only names it inside a string,
        // and the rest are its definition and two prototypes.
        Assert.Equal("cited function priest_talk has no live call site", Check().NoteFor("src/priest.c:10"));
        Assert.Equal("cited function priest_talk has no live call site", Check().NoteFor("src/priest.c:10-11"));
    }

    [Fact]
    public void ACitationOfTheDefinitionLineItself_GetsTheDefinitionLineNote_NotTheLivenessNote()
    {
        // Line 7 is "priest_talk(priest)" itself, not a line inside the body.
        Assert.Equal(
            "cited line src/priest.c:7 is only the definition line of priest_talk",
            Check().NoteFor("src/priest.c:7 (priest_talk)"));
    }

    [Fact]
    public void AFunctionWithOneLiveCaller_GetsNoNote()
    {
        Assert.Null(Check().NoteFor("src/priest.c:18"));
    }

    [Theory]
    [InlineData("wiki:Priest")]
    [InlineData("include/extern.h:1")]
    [InlineData("src/priest.c:10; wiki:Priest")]
    [InlineData("board: \"a - a blessed +1 quarterstaff\"")]
    [InlineData("NetHack src/priest.c:10")]
    [InlineData("src/priest.c:2")]
    [InlineData("src/priest.c:500")]
    [InlineData(null)]
    public void ACitationThatIsNotASingleLiveCheckableSourceLine_GetsNoNote(string? citation)
    {
        Assert.Null(Check().NoteFor(citation));
    }

    [Theory]
    [InlineData("src/priest.c", "cited file src/priest.c without a line")]
    [InlineData("src/priest.c:priest_talk", "cited file src/priest.c without a line")]
    public void ACitationNamingAFileWithoutALine_GetsTheLinelessNote(string citation, string expected)
    {
        // "src/priest.c" names no line at all; "src/priest.c:priest_talk" names a symbol, not a
        // line number — the colon is not followed by a digit, so neither is a checkable source line.
        Assert.Equal(expected, Check().NoteFor(citation));
    }

    [Fact]
    public void ALinelessSourceFile_GetsTheLinelessNote_RegardlessOfWhetherItIsIndexed()
    {
        // The lineless check is purely textual; "src/shk.c" need not be in the corpus for it to fire.
        Assert.Equal("cited file src/shk.c without a line", Check().NoteFor("src/shk.c"));
    }

    [Fact]
    public void ALinelessSourceFileAlongsideAWikiPage_GetsNoNote()
    {
        Assert.Null(Check().NoteFor("src/shk.c; wiki:Shops"));
    }

    [Fact]
    public void ALinelessNetHackSourceFile_GetsNoNote()
    {
        Assert.Null(Check().NoteFor("nethack/src/x.c"));
    }

    [Fact]
    public void AnIndeterminateVerdictWithALinelessCitation_IsNeverAnnotated()
    {
        var verification = new BenchmarkClaimVerification(0, "Claim.", BenchmarkClaimVerdict.Indeterminate, "src/shk.c", "Basis.");

        var annotated = Assert.Single(Check().Annotate(new[] { verification }));

        Assert.Null(annotated.CitationNote);
    }

    private static BenchmarkCitationLivenessCheck SpellCheck()
    {
        var corpus = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["src/spell.c"] = new[]
            {
                "/* spell.c */",                                     // 1
                "#include \"hack.h\"",                                // 2
                "",                                                   // 3
                "STATIC_DCL int percent_success(struct obj *, boolean);", // 4
                "",                                                   // 5
                "int",                                                // 6
                "study_book(spellbook)",                              // 7
                "struct obj *spellbook;",                             // 8
                "{",                                                  // 9
                "    percent_success(spellbook, FALSE);",             // 10
                "}",                                                  // 11
                "percent_success(spell, limited)",                    // 12
                "{",                                                  // 13
                "    return 50;",                                     // 14
                "}"                                                   // 15
            }
        };
        return new BenchmarkCitationLivenessCheck(() => corpus);
    }

    [Fact]
    public void ACitationOfOnlyTheDefinitionLine_GetsTheDefinitionLineNote()
    {
        Assert.Equal(
            "cited line src/spell.c:12 is only the definition line of percent_success",
            SpellCheck().NoteFor("src/spell.c:12"));
    }

    [Fact]
    public void ARangeStartingOnTheDefinitionLine_GetsNoDefinitionLineNote()
    {
        // The range also reaches into the body, and percent_success has a live call site (line 10),
        // so the ordinary liveness check applies instead — and finds nothing to note.
        Assert.Null(SpellCheck().NoteFor("src/spell.c:12-40"));
    }

    private static BenchmarkCitationLivenessCheck ApplyCheck()
    {
        // use_lamp's definition line is 2604, as in the run-73 source clone.
        var apply = Enumerable.Repeat(string.Empty, 2602).ToList();
        apply.AddRange(new[]
        {
            "void",                          // 2603
            "use_lamp(obj)",                 // 2604
            "struct obj *obj;",              // 2605
            "{",                             // 2606
            "    obj->lamplit = 1;",         // 2607
            "}",                             // 2608
            "",                              // 2609
            "int",                           // 2610
            "doapply()",                     // 2611
            "{",                             // 2612
            "    use_lamp(uwep);",           // 2613
            "    return 1;",                 // 2614
            "}"                              // 2615
        });

        var corpus = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["src/apply.c"] = apply
        };
        return new BenchmarkCitationLivenessCheck(() => corpus);
    }

    [Fact]
    public void ADefinitionLineCitationOfAClaimAboutWhereTheFunctionIsDefined_GetsNoNote()
    {
        // Run 73's Q4: the claim locates use_lamp() near line 2602, and the citation is its
        // definition line, which settles exactly that.
        Assert.Null(ApplyCheck().NoteFor(
            "src/apply.c:2604",
            "Torches are lit and snuffed by the same routine (`use_lamp()` in `src/apply.c`, around line 2602)."));
    }

    [Theory]
    [InlineData("torches cannot be relit")]
    [InlineData("use_lamp() decides whether a torch can be relit.")]
    [InlineData("use_lamp() decides whether a torch can be relit (around line 2500).")]
    [InlineData("use_lamps is defined around line 2604.")]
    [InlineData(null)]
    public void ADefinitionLineCitationOfAnyOtherClaim_GetsTheDefinitionLineNote(string? claim)
    {
        Assert.Equal(
            "cited line src/apply.c:2604 is only the definition line of use_lamp",
            ApplyCheck().NoteFor("src/apply.c:2604", claim));
    }

    [Fact]
    public void AClaimAboutWhereADeadFunctionIsDefined_StillGetsTheLivenessCheck()
    {
        // The definition line settles the location, not whether anything calls the function.
        Assert.Equal(
            "cited function priest_talk has no live call site",
            Check().NoteFor("src/priest.c:7", "priest_talk() is defined at line 7 of src/priest.c."));
    }

    [Fact]
    public void Annotate_PassesTheClaimToTheDefinitionLineCheck()
    {
        var located = new BenchmarkClaimVerification(
            0, "`use_lamp()` in `src/apply.c`, around line 2602, handles torches.", BenchmarkClaimVerdict.Supported, "src/apply.c:2604", "Found.");
        var unrelated = new BenchmarkClaimVerification(
            1, "Torches cannot be relit.", BenchmarkClaimVerdict.Supported, "src/apply.c:2604", "Found.");

        var annotated = ApplyCheck().Annotate(new[] { located, unrelated });

        Assert.Null(annotated[0].CitationNote);
        Assert.Equal("cited line src/apply.c:2604 is only the definition line of use_lamp", annotated[1].CitationNote);
    }

    [Fact]
    public void ACitationOfAFileNotInTheIndex_GetsTheMissingFileNote()
    {
        // The file is absent from the corpus entirely — never indexed, or dropped for exceeding
        // the indexer's per-file size limit; the note's wording is the same for both.
        Assert.Equal("cited file src/mattackm.c is not in the indexed source", Check().NoteFor("src/mattackm.c:40"));
    }

    [Fact]
    public void ACitationOfAnIndexedFile_DoesNotGetTheMissingFileNote()
    {
        Assert.Equal("cited function priest_talk has no live call site", Check().NoteFor("src/priest.c:10"));
        Assert.Null(Check().NoteFor("src/priest.c:18"));
    }

    [Fact]
    public void AnExceptionInsideTheCorpusAccessor_YieldsNoNote()
    {
        var check = new BenchmarkCitationLivenessCheck(() => throw new InvalidOperationException("index not ready"));
        var verification = new BenchmarkClaimVerification(0, "Claim.", BenchmarkClaimVerdict.Refuted, "src/priest.c:10", "Basis.");

        Assert.Null(check.NoteFor("src/priest.c:10"));
        var annotated = Assert.Single(check.Annotate(new[] { verification }));
        Assert.Null(annotated.CitationNote);
        Assert.Equal(BenchmarkClaimVerdict.Refuted, annotated.EffectiveVerdict);
    }

    [Fact]
    public void TheNote_DemotesRefutedAndSupportedToIndeterminateForCounting_AndKeepsTheStoredVerdict()
    {
        string[] roles = { BenchmarkClaimRoles.UnverifiedClaim };
        var verifications = new[]
        {
            new BenchmarkClaimVerification(0, "Priests talk about donations.", BenchmarkClaimVerdict.Refuted, "src/priest.c:10", "Dead code says otherwise.") { Roles = roles },
            new BenchmarkClaimVerification(1, "Priests bless water.", BenchmarkClaimVerdict.Supported, "src/priest.c:11", "Found.") { Roles = roles },
            new BenchmarkClaimVerification(2, "Helpers set hit points.", BenchmarkClaimVerdict.Refuted, "src/priest.c:18", "Live code.") { Roles = roles },
            new BenchmarkClaimVerification(3, "Nothing was found.", BenchmarkClaimVerdict.Indeterminate, "src/priest.c:10", null) { Roles = roles }
        };

        var annotated = Check().Annotate(verifications);

        Assert.Equal(BenchmarkClaimVerdict.Refuted, annotated[0].Verdict);
        Assert.Equal(BenchmarkClaimVerdict.Indeterminate, annotated[0].EffectiveVerdict);
        Assert.Equal(BenchmarkClaimVerdict.Supported, annotated[1].Verdict);
        Assert.Equal(BenchmarkClaimVerdict.Indeterminate, annotated[1].EffectiveVerdict);
        Assert.Null(annotated[2].CitationNote);
        Assert.Equal(BenchmarkClaimVerdict.Refuted, annotated[2].EffectiveVerdict);
        Assert.Null(annotated[3].CitationNote);

        var answer = new BenchmarkRunAnswer();
        BenchmarkService.ApplyClaimVerificationOutcome(answer, annotated, isCriticalErrorAdjudication: false, outOfRubricBasis: null);

        Assert.Equal(0, answer.ClaimsSupportedCount);
        Assert.Equal(1, answer.ClaimsRefutedCount);
        Assert.Equal(3, answer.ClaimsIndeterminateCount);

        // The stored record prints both: the verifier's verdict and the harness's note.
        var stored = JsonSerializer.Deserialize<List<BenchmarkClaimVerification>>(answer.ClaimVerificationJson!)!;
        Assert.Equal(BenchmarkClaimVerdict.Refuted, stored[0].Verdict);
        Assert.Equal("cited function priest_talk has no live call site", stored[0].CitationNote);
        Assert.Equal(BenchmarkClaimVerdict.Indeterminate, stored[0].EffectiveVerdict);
        Assert.DoesNotContain("effectiveVerdict", answer.ClaimVerificationJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ADemotedRefutation_NoLongerRaisesRefutedClaim()
    {
        var verifications = new[]
        {
            new BenchmarkClaimVerification(0, "Priests talk about donations.", BenchmarkClaimVerdict.Refuted, "src/priest.c:10", "Dead code.")
                { Roles = new[] { BenchmarkClaimRoles.UnverifiedClaim } }
        };
        var answer = new BenchmarkRunAnswer { AnswerFlags = (int)BenchmarkAnswerFlags.RefutedClaim };

        BenchmarkService.ApplyClaimVerificationOutcome(answer, Check().Annotate(verifications), false, null);

        Assert.Equal(0, answer.AnswerFlags & (int)BenchmarkAnswerFlags.RefutedClaim);
    }

    [Fact]
    public void AFunctionReachedOnlyThroughAFunctionPointer_GetsNoNote()
    {
        // Run 56: learn() is never called by name; it is passed to set_occupation().
        var corpus = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["src/spell.c"] = new[]
            {
                "static int learn(void);",                       // 1
                "",                                              // 2
                "static int",                                    // 3
                "learn(void)",                                   // 4
                "{",                                             // 5
                "    int i = 0;",                                // 6
                "    return i;",                                 // 7
                "}",                                             // 8
                "",                                              // 9
                "int",                                           // 10
                "study_book(struct obj *spellbook)",             // 11
                "{",                                             // 12
                "    set_occupation(learn, \"studying\");",      // 13
                "    return 1;",                                 // 14
                "}"                                              // 15
            }
        };
        var check = new BenchmarkCitationLivenessCheck(() => corpus);

        Assert.Null(check.NoteFor("src/spell.c:6"));
    }

    [Fact]
    public void ACitationIntoAMacroTableRow_GetsNoNote()
    {
        var corpus = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["src/objects.c"] = new[]
            {
                "void",                                                             // 1
                "objects_init(void)",                                               // 2
                "{",                                                                // 3
                "    return;",                                                      // 4
                "}",                                                                // 5
                "",                                                                 // 6
                "#define SCROLL(name,text,prob,cost) \\",                           // 7
                "        OBJECT(OBJ(name, text), prob, cost)",                      // 8
                "SCROLL(\"mail\",          \"stamped\",   0,  0),",                 // 9
                "SCROLL(\"blank paper\", \"unlabeled\",  25, 60),",                 // 10
                "#undef SCROLL"                                                     // 11
            }
        };
        var check = new BenchmarkCitationLivenessCheck(() => corpus);

        Assert.Null(check.NoteFor("src/objects.c:10"));
        Assert.Null(check.NoteFor("src/objects.c:9"));
    }

    private static BenchmarkCitationLivenessCheck MacroCheck()
    {
        var corpus = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["src/objects.c"] = new[]
            {
                "#include \"hack.h\"",                                                   // 1
                "",                                                                      // 2
                "#define SPELL(name, desc, sub, prob, delay, level, \\",                 // 3
                "              mgc, dir, color)                     \\",                 // 4
                "    OBJECT(OBJ(name, desc), prob, delay, level, \\",                    // 5
                "           mgc, dir, color)",                                           // 6
                "SPELL(\"dig\", \"parchment\", P_MATTER_SPELL, 20, 6, 5, 1, RAY, HI_PAPER),", // 7
                "#undef SPELL",                                                          // 8
                "#define WAND(name, typ, \\",                                             // 9
                "             prob, cost, \\",                                           // 10
                "             nodir) \\",                                                // 11
                "    OBJECT(name, typ, prob, cost, nodir)"                               // 12
            },
            ["include/objclass.h"] = new[]
            {
                "struct objclass {",                                                     // 1
                "    short oc_delay;",                                                   // 2
                "};",                                                                    // 3
                "#define objects_delay(otyp) \\",                                        // 4
                "    (objects[otyp].oc_delay)",                                          // 5
                "#define MAXSPELL 12"                                                    // 6
            }
        };
        return new BenchmarkCitationLivenessCheck(() => corpus);
    }

    [Theory]
    [InlineData("src/objects.c:3", "cited line src/objects.c:3 is only the definition of macro SPELL")]
    // The continuation line that closes SPELL's parameter list.
    [InlineData("src/objects.c:4", "cited line src/objects.c:4 is only the definition of macro SPELL")]
    // WAND's parameter list runs over three lines: a middle one and the closing one.
    [InlineData("src/objects.c:10", "cited line src/objects.c:10 is only the definition of macro WAND")]
    [InlineData("src/objects.c:11", "cited line src/objects.c:11 is only the definition of macro WAND")]
    [InlineData("include/objclass.h:4", "cited line include/objclass.h:4 is only the definition of macro objects_delay")]
    public void ASingleLineInsideAMultiLineDefineHeader_GetsTheMacroDefinitionNote(string citation, string expected)
    {
        Assert.Equal(expected, MacroCheck().NoteFor(citation));
    }

    [Theory]
    // Body lines of a function-like macro, after its parameter list closed.
    [InlineData("src/objects.c:5")]
    [InlineData("src/objects.c:6")]
    [InlineData("src/objects.c:12")]
    [InlineData("include/objclass.h:5")]
    // The line after the header: the first line not ending in "\" closed it at line 6.
    [InlineData("src/objects.c:7")]
    // A range is never noted.
    [InlineData("src/objects.c:3-6")]
    [InlineData("include/objclass.h:4-5")]
    // A header line outside any macro.
    [InlineData("include/objclass.h:2")]
    // A one-line object-like macro carries its value on the #define line.
    [InlineData("include/objclass.h:6")]
    public void ALineOutsideADefineHeader_OrARange_GetsNoMacroNote(string citation)
    {
        Assert.Null(MacroCheck().NoteFor(citation));
    }

    private static BenchmarkCitationLivenessCheck OneLineMacroCheck()
    {
        var corpus = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["include/general.h"] = new[]
            {
                "#define BREATH_WEAPON_MANA_COST 15",                                    // 1
                "#define LIMIT /* see below */",                                         // 2
                "#define FALLBACK_LIMIT // set elsewhere",                               // 3
                "#define FOO(a, b) \\",                                                  // 4
                "    ((a) + (b))",                                                       // 5
                "#define BAR(a) /* swaps */ \\",                                         // 6
                "    (-(a))"                                                             // 7
            },
            ["include/rm.h"] = new[]
            {
                "struct rm {",                                                           // 1
                "    schar typ;",                                                        // 2
                "};",                                                                    // 3
                "#define ugod_is_angry() (u.ualign.record < 0)"                          // 4
            }
        };
        return new BenchmarkCitationLivenessCheck(() => corpus);
    }

    [Theory]
    // Nothing but a comment follows the name of an object-like macro.
    [InlineData("include/general.h:2", "cited line include/general.h:2 is only the definition of macro LIMIT")]
    [InlineData("include/general.h:3", "cited line include/general.h:3 is only the definition of macro FALLBACK_LIMIT")]
    // Nothing but a trailing "\" (and a comment) follows the closing ")" of the parameter list.
    [InlineData("include/general.h:4", "cited line include/general.h:4 is only the definition of macro FOO")]
    [InlineData("include/general.h:6", "cited line include/general.h:6 is only the definition of macro BAR")]
    public void ADefineLineWithoutABody_GetsTheMacroDefinitionNote(string citation, string expected)
    {
        Assert.Equal(expected, OneLineMacroCheck().NoteFor(citation));
    }

    [Theory]
    // A one-line object-like macro: its value follows the name.
    [InlineData("include/general.h:1")]
    // A one-line function-like macro: its body follows the parameter list.
    [InlineData("include/rm.h:4")]
    // Body rows after a header that ended on the row before.
    [InlineData("include/general.h:5")]
    [InlineData("include/general.h:7")]
    public void ADefineLineCarryingTheMacroBody_GetsNoMacroNote(string citation)
    {
        Assert.Null(OneLineMacroCheck().NoteFor(citation));
    }

    [Fact]
    public void AOneLineMacroCitedForItsValue_KeepsItsVerdictForCounting()
    {
        var verification = new BenchmarkClaimVerification(0, "Breath weapons cost 15 mana.", BenchmarkClaimVerdict.Supported, "include/general.h:1", "The macro's value is 15.");

        var annotated = Assert.Single(OneLineMacroCheck().Annotate(new[] { verification }));

        Assert.Null(annotated.CitationNote);
        Assert.Equal(BenchmarkClaimVerdict.Supported, annotated.EffectiveVerdict);
    }

    [Fact]
    public void TheMacroNote_DemotesTheVerdictForCounting_AndKeepsTheStoredVerdict()
    {
        var verification = new BenchmarkClaimVerification(0, "Spells take 6 turns to learn.", BenchmarkClaimVerdict.Refuted, "src/objects.c:4", "The SPELL macro says otherwise.");

        var annotated = Assert.Single(MacroCheck().Annotate(new[] { verification }));

        Assert.Equal("cited line src/objects.c:4 is only the definition of macro SPELL", annotated.CitationNote);
        Assert.Equal(BenchmarkClaimVerdict.Refuted, annotated.Verdict);
        Assert.Equal(BenchmarkClaimVerdict.Indeterminate, annotated.EffectiveVerdict);
    }

    [Fact]
    public void AVerificationThatAlreadyCarriesANote_KeepsIt()
    {
        var verification = new BenchmarkClaimVerification(0, "Claim.", BenchmarkClaimVerdict.Supported, "src/objects.c:4", "Basis.")
        {
            CitationNote = BenchmarkClaimVerificationParser.ChargedPartNotJudgedNote
        };

        var annotated = Assert.Single(MacroCheck().Annotate(new[] { verification }));

        Assert.Equal(BenchmarkClaimVerificationParser.ChargedPartNotJudgedNote, annotated.CitationNote);
    }

    [Fact]
    public void CommentsAndLiterals_AreBlankedInPlace_AcrossLines()
    {
        var stripped = SourceLivenessIndex.StripCommentsAndLiterals(new[]
        {
            "a(); /* b(",
            "c( */ d(); // e(",
            "f(\"g(\\\"\", 'h');"
        });

        Assert.Equal(3, stripped.Length);
        Assert.Contains("a()", stripped[0]);
        Assert.DoesNotContain("b(", stripped[0]);
        Assert.DoesNotContain("c(", stripped[1]);
        Assert.Contains("d()", stripped[1]);
        Assert.DoesNotContain("e(", stripped[1]);
        Assert.Contains("f(", stripped[2]);
        Assert.DoesNotContain("g(", stripped[2]);
        Assert.DoesNotContain("h", stripped[2]);
        Assert.Equal("c( */ d(); // e(".Length, stripped[1].Length);
    }

    [Theory]
    // A line in prose after the file reads as src/<file>:<line>, so no lineless note.
    [InlineData("src/matcomps.c (in function foo) at line 156", "cited file src/matcomps.c is not in the indexed source")]
    [InlineData("src/priest.c, in priest_talk, at line 10", "cited function priest_talk has no live call site")]
    [InlineData("src/priest.c L10", "cited function priest_talk has no live call site")]
    [InlineData("src/priest.c lines 10-11", "cited function priest_talk has no live call site")]
    // One source file named: a free-standing line anywhere is attributed to it.
    [InlineData("priest_talk at line 10 of src/priest.c", "cited function priest_talk has no live call site")]
    // Two source files named: a free-standing line is attributed to neither.
    [InlineData("line 10 of src/priest.c or src/sounds.c", "cited file src/priest.c without a line")]
    public void AProseLineReference_IsReadAsAFileLine(string citation, string expected)
    {
        Assert.Equal(expected, Check().NoteFor(citation));
    }

    [Fact]
    public void AProseLineRange_IsARange()
    {
        // A single line 12 is only percent_success's definition line; the range 12 to 40 reaches
        // into its body, and percent_success has a live caller.
        Assert.Equal(
            "cited line src/spell.c:12 is only the definition line of percent_success",
            SpellCheck().NoteFor("src/spell.c line 12"));
        Assert.Null(SpellCheck().NoteFor("src/spell.c lines 12 to 40"));
    }

    [Fact]
    public void AProseLineNextToABoardLine_IsNotTakenForASourceLine()
    {
        Assert.Equal("cited file src/priest.c without a line", Check().NoteFor("src/priest.c; board line 10"));
    }

    private static BenchmarkCitationLivenessCheck BlankLineCheck()
    {
        var corpus = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["src/blank.c"] = new[]
            {
                "/* blank.c */",                   // 1
                "",                                // 2
                "int",                             // 3
                "blank_helper(void)",              // 4
                "{",                               // 5
                "    /* nothing here */",          // 6
                "",                                // 7
                "    return 0;",                   // 8
                "}",                               // 9
                "",                                // 10
                "void",                            // 11
                "use_it(void)",                    // 12
                "{",                               // 13
                "    blank_helper();",             // 14
                "}"                                // 15
            }
        };
        return new BenchmarkCitationLivenessCheck(() => corpus);
    }

    [Theory]
    [InlineData("src/blank.c:1", "cited line src/blank.c:1 is blank or a comment")]
    [InlineData("src/blank.c:2", "cited line src/blank.c:2 is blank or a comment")]
    [InlineData("src/blank.c:6-7", "cited lines src/blank.c:6-7 are blank or comments")]
    public void ABlankOrCommentOnlyCitedLine_GetsTheBlankLineNote(string citation, string expected)
    {
        Assert.Equal(expected, BlankLineCheck().NoteFor(citation));
    }

    [Fact]
    public void ARangeWithOneCodeLine_GetsNoBlankLineNote()
    {
        // Line 8 is code inside blank_helper, which use_it calls.
        Assert.Null(BlankLineCheck().NoteFor("src/blank.c:6-8"));
    }

    [Fact]
    public void TheBlankLineNote_DemotesTheVerdictForCounting()
    {
        var verification = new BenchmarkClaimVerification(0, "Claim.", BenchmarkClaimVerdict.Supported, "src/blank.c:7", "Basis.");

        var annotated = Assert.Single(BlankLineCheck().Annotate(new[] { verification }));

        Assert.Equal("cited line src/blank.c:7 is blank or a comment", annotated.CitationNote);
        Assert.Equal(BenchmarkClaimVerdict.Supported, annotated.Verdict);
        Assert.Equal(BenchmarkClaimVerdict.Indeterminate, annotated.EffectiveVerdict);
    }
}
