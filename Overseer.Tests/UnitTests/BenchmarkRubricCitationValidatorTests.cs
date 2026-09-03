namespace Overseer.Tests.UnitTests;

using System.Linq;
using MobileGnollHackLogger.Data;
using Overseer.Services.Benchmarking;
using Xunit;

public class BenchmarkRubricCitationValidatorTests
{
    /// <summary>Run 7's Q1 rubric shape, which is what this parser was written against.</summary>
    private const string Q1Rubric = """
        **REQUIRED**
        - Names the gnoll's racial intrinsics.

        **CRITICAL ERROR**
        - Invents racial intrinsics that are not in the list above.

        **SOURCE**: `src/role.c`, gnoll race entry around line 1217; `include/objclass.h`;
        GnollHack wiki, "How GnollHack differs from NetHack".

        **FORM**
        - A short list.
        """;

    private static BenchmarkQuestion Question(string? rubric, int orderIndex = 1, long id = 1) => new()
    {
        Id = id,
        OrderIndex = orderIndex,
        QuestionText = $"Q{orderIndex}",
        ExpectedPoints = rubric
    };

    [Fact]
    public void Parse_ExtractsFilePathsSymbolsAndWikiTitlesFromTheSourceSection()
    {
        var citations = BenchmarkRubricCitationValidator.Parse(Q1Rubric);

        Assert.Contains(citations, c => c.Kind == BenchmarkCitationKind.SourceFile && c.Value == "src/role.c");
        Assert.Contains(citations, c => c.Kind == BenchmarkCitationKind.SourceFile && c.Value == "include/objclass.h");
        Assert.Contains(citations, c => c.Kind == BenchmarkCitationKind.WikiArticle
                                        && c.Value == "How GnollHack differs from NetHack");
    }

    [Fact]
    public void Parse_ReadsALineNumberButNeverValidatesIt()
    {
        // Line numbers in GnollHack's C source drift with every commit, so validating them would
        // produce permanent false alarms — and an operator who learns to ignore one line of a
        // panel learns to ignore the panel.
        var citation = Assert.Single(BenchmarkRubricCitationValidator.Parse(Q1Rubric)
            .Where(c => c.Value == "src/role.c"));

        Assert.Equal(1217, citation.LineNumber);
    }

    [Fact]
    public void Parse_StaysInsideTheSourceSection()
    {
        // A `**SOURCE**` line must not swallow the rest of the rubric: the CRITICAL ERROR section
        // above it and the FORM section below are not citations.
        var citations = BenchmarkRubricCitationValidator.Parse(Q1Rubric);

        Assert.DoesNotContain(citations, c => c.Value.Contains("short list"));
        Assert.DoesNotContain(citations, c => c.Value.Contains("Invents"));
    }

    [Fact]
    public void Parse_IgnoresBacktickedProseThatIsNotASymbol()
    {
        const string rubric = "**SOURCE**: `src/role.c`, `the gnoll race entry`, `MH_GNOLL`.";

        var symbols = BenchmarkRubricCitationValidator.Parse(rubric)
            .Where(c => c.Kind == BenchmarkCitationKind.Symbol)
            .Select(c => c.Value)
            .ToList();

        Assert.Contains("MH_GNOLL", symbols);
        Assert.DoesNotContain("the gnoll race entry", symbols);
    }

    [Fact]
    public void Parse_ReturnsNothingForARubricWithNoSourceSection()
    {
        Assert.Empty(BenchmarkRubricCitationValidator.Parse("**REQUIRED**\n- Names the intrinsics."));
        Assert.Empty(BenchmarkRubricCitationValidator.Parse(null));
        Assert.Empty(BenchmarkRubricCitationValidator.Parse("   "));
    }

    [Fact]
    public void Validate_ReportsResolvedAndUnresolvedCitations()
    {
        var results = BenchmarkRubricCitationValidator.Validate(
            new[] { Question(Q1Rubric) },
            path => path == "src/role.c",
            symbol => symbol == "MH_GNOLL",
            title => true);

        var question = Assert.Single(results);

        Assert.Contains(question.Citations, c => c.Value == "src/role.c" && c.Status == BenchmarkCitationStatus.Resolved);
        Assert.Contains(question.Citations, c => c.Value == "include/objclass.h" && c.Status == BenchmarkCitationStatus.Unresolved);
        Assert.Equal(question.Citations.Count(c => c.Status == BenchmarkCitationStatus.Unresolved), question.UnresolvedCount);
    }

    [Fact]
    public void Validate_PutsUnresolvedCitationsFirst()
    {
        var question = Assert.Single(BenchmarkRubricCitationValidator.Validate(
            new[] { Question(Q1Rubric) },
            path => path == "src/role.c",
            symbol => true,
            title => true));

        Assert.Equal(BenchmarkCitationStatus.Unresolved, question.Citations[0].Status);
    }

    [Fact]
    public void Validate_ReportsAWikiTitleAsNotValidatedWhenNoLookupExists()
    {
        // "We did not check" and "we checked and it is fine" are different facts, and a panel
        // that conflates them is worse than one that omits the row.
        var question = Assert.Single(BenchmarkRubricCitationValidator.Validate(
            new[] { Question(Q1Rubric) },
            path => true,
            symbol => true,
            wikiTitleResolver: null));

        var wiki = Assert.Single(question.Citations.Where(c => c.Kind == BenchmarkCitationKind.WikiArticle));
        Assert.Equal(BenchmarkCitationStatus.NotValidated, wiki.Status);
        Assert.Equal(1, question.NotValidatedCount);
    }

    [Fact]
    public void Validate_ReportsNotValidatedWhenTheResolverItselfIsUnavailable()
    {
        // An index still initialising must not fail the whole report.
        var question = Assert.Single(BenchmarkRubricCitationValidator.Validate(
            new[] { Question("**SOURCE**: `src/role.c`.") },
            path => throw new System.InvalidOperationException("index not ready"),
            symbol => true));

        var file = Assert.Single(question.Citations.Where(c => c.Kind == BenchmarkCitationKind.SourceFile));
        Assert.Equal(BenchmarkCitationStatus.NotValidated, file.Status);
    }

    [Fact]
    public void Validate_FlagsARubricThatCitesNothing()
    {
        var question = Assert.Single(BenchmarkRubricCitationValidator.Validate(
            new[] { Question("**REQUIRED**\n- Names the intrinsics.") },
            path => true,
            symbol => true));

        Assert.True(question.HasNoCitations);
        Assert.Empty(question.Citations);
    }
}
