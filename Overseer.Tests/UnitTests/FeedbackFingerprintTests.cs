namespace Overseer.Tests.UnitTests;

using System.Collections.Generic;
using System.Linq;
using Overseer.Services.Feedback;
using Xunit;

/// <summary>
/// The grouping key of feedback reports: exact after normalization of the subject, blind to the
/// summary, and split by the first related tool for a tool problem.
/// </summary>
public class FeedbackFingerprintTests
{
    private static ReportFeedbackArguments Args(string category, string subject, string summary = "Something went wrong.", params string[] tools)
        => new() { Category = category, Subject = subject, Summary = summary, RelatedTools = new List<string>(tools) };

    [Theory]
    [InlineData("Dilithium Crystal", "dilithium crystal")]
    [InlineData("  Dilithium   crystal\t", "dilithium crystal")]
    [InlineData("wiki page: Elbereth!", "wiki page elbereth")]
    [InlineData("src/mhitu.c", "src mhitu c")]
    [InlineData("monster_lookup", "monster lookup")]
    [InlineData("ＤＩＬＩＴＨＩＵＭ", "dilithium")]
    [InlineData("ﬁre trap", "fire trap")]
    [InlineData("...", "")]
    public void NormalizeSubject_FoldsCasePunctuationWhitespaceAndCompatibilityForms(string subject, string expected)
    {
        Assert.Equal(expected, FeedbackFingerprint.NormalizeSubject(subject));
    }

    [Fact]
    public void TheSameSubject_WithTwoSummaryWordings_IsOneFingerprint()
    {
        string first = FeedbackFingerprint.Compute(Args(FeedbackCategories.MissingInformation, "Dilithium crystal", "The wiki has no price for it."));
        string second = FeedbackFingerprint.Compute(Args(FeedbackCategories.MissingInformation, "dilithium  CRYSTAL.", "No base cost found anywhere."));

        Assert.Equal(first, second);
    }

    [Fact]
    public void TheCategory_IsPartOfTheKey()
    {
        Assert.NotEqual(
            FeedbackFingerprint.Compute(Args(FeedbackCategories.MissingInformation, "Elbereth")),
            FeedbackFingerprint.Compute(Args(FeedbackCategories.OwnMistake, "Elbereth")));
    }

    [Fact]
    public void AToolProblem_WithDifferentFirstTools_IsTwoFingerprints()
    {
        string search = FeedbackFingerprint.Compute(Args(FeedbackCategories.ToolProblem, "malformed result", tools: new[] { "wiki_search", "wiki_view" }));
        string view = FeedbackFingerprint.Compute(Args(FeedbackCategories.ToolProblem, "malformed result", tools: new[] { "wiki_view", "wiki_search" }));

        Assert.NotEqual(search, view);

        // The first tool compares case-insensitively.
        Assert.Equal(search, FeedbackFingerprint.Compute(Args(FeedbackCategories.ToolProblem, "malformed result", tools: new[] { "WIKI_SEARCH" })));
    }

    [Fact]
    public void RelatedTools_DoNotSplitOtherCategories()
    {
        Assert.Equal(
            FeedbackFingerprint.Compute(Args(FeedbackCategories.MissingInformation, "Elbereth", tools: new[] { "wiki_search" })),
            FeedbackFingerprint.Compute(Args(FeedbackCategories.MissingInformation, "Elbereth", tools: new[] { "wiki_view" })));
    }

    [Fact]
    public void TheFingerprint_Is64LowercaseHexCharacters()
    {
        string fingerprint = FeedbackFingerprint.Compute(Args(FeedbackCategories.ToolProblem, "monster_lookup"));

        Assert.Equal(64, fingerprint.Length);
        Assert.True(fingerprint.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'), fingerprint);
    }
}
