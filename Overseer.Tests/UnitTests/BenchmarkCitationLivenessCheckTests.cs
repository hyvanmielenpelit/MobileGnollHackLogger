namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using MobileGnollHackLogger.Data;
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

    [Fact]
    public void CommentsAndLiterals_AreBlankedInPlace_AcrossLines()
    {
        var stripped = BenchmarkCitationLivenessCheck.StripCommentsAndLiterals(new[]
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
}
