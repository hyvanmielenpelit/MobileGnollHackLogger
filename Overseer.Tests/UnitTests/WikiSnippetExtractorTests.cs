namespace Overseer.Tests.UnitTests;

using System.Collections.Generic;
using Overseer.Services;
using Xunit;

public class WikiSnippetExtractorTests
{
    [Fact]
    public void SplitSections_HeadingsLevels1To6_BuildsCorrectHeadingPaths()
    {
        string markdown = @"# H1 Title
Content 1
## H2 Section
Content 2
### H3 Subsection
Content 3
#### H4 Level
Content 4
##### H5 Level
Content 5
###### H6 Level
Content 6
## Another H2
Content 7";

        var sections = WikiSnippetExtractor.SplitSections(markdown);

        Assert.Equal(7, sections.Count);
        Assert.Equal("H1 Title", sections[0].HeadingPath);
        Assert.Equal("H1 Title › H2 Section", sections[1].HeadingPath);
        Assert.Equal("H1 Title › H2 Section › H3 Subsection", sections[2].HeadingPath);
        Assert.Equal("H1 Title › H2 Section › H3 Subsection › H4 Level", sections[3].HeadingPath);
        Assert.Equal("H1 Title › H2 Section › H3 Subsection › H4 Level › H5 Level", sections[4].HeadingPath);
        Assert.Equal("H1 Title › H2 Section › H3 Subsection › H4 Level › H5 Level › H6 Level", sections[5].HeadingPath);
        Assert.Equal("H1 Title › Another H2", sections[6].HeadingPath);
    }

    [Fact]
    public void SplitSections_NoHeadings_ReturnsSinglePreambleSection()
    {
        string markdown = "This is a simple article with no markdown headings anywhere.";

        var sections = WikiSnippetExtractor.SplitSections(markdown);

        Assert.Single(sections);
        Assert.Equal(0, sections[0].Level);
        Assert.Equal(string.Empty, sections[0].HeadingPath);
        Assert.Equal("This is a simple article with no markdown headings anywhere.", sections[0].Body);
    }

    [Fact]
    public void SplitSections_PreambleAndHeadings_SeparatesPreamble()
    {
        string markdown = @"> 👉 **Introductory note about the article.**

## First Section
First body content.

## Second Section
Second body content.";

        var sections = WikiSnippetExtractor.SplitSections(markdown);

        Assert.Equal(3, sections.Count);
        Assert.Equal(0, sections[0].Level);
        Assert.Equal(string.Empty, sections[0].HeadingPath);
        Assert.Contains("Introductory note", sections[0].Body);

        Assert.Equal(2, sections[1].Level);
        Assert.Equal("First Section", sections[1].HeadingPath);
        Assert.Equal("First body content.", sections[1].Body);

        Assert.Equal(2, sections[2].Level);
        Assert.Equal("Second Section", sections[2].HeadingPath);
    }

    [Fact]
    public void Score_DistinctTermScoring_WeightsHeadingOverBody()
    {
        var sectionInHeading = new WikiSection
        {
            HeadingPath = "Armor › Shields",
            Body = "Some generic protection item."
        };

        var sectionInBody = new WikiSection
        {
            HeadingPath = "Weapons › Swords",
            Body = "Shields and shields can block attacks with shields."
        };

        var terms = new[] { "shields" };

        int scoreHeading = WikiSnippetExtractor.Score(sectionInHeading, terms);
        int scoreBody = WikiSnippetExtractor.Score(sectionInBody, terms);

        // Heading match (10) beats repeated occurrences in body (1)
        Assert.Equal(10, scoreHeading);
        Assert.Equal(1, scoreBody);
        Assert.True(scoreHeading > scoreBody);
    }

    [Fact]
    public void BuildSnippet_CompleteMarker_WhenAllSectionsFitBudget()
    {
        string markdown = @"## Overview
Short overview text.

## Details
Short details text.";

        string snippet = WikiSnippetExtractor.BuildSnippet("Overview.md", markdown, new[] { "overview", "details" }, 2500);

        Assert.Contains("--- Overview.md ---", snippet);
        Assert.Contains("### Overview", snippet);
        Assert.Contains("### Details", snippet);
        Assert.Contains("[article: Overview.md — complete]", snippet);
        Assert.DoesNotContain("omitted", snippet);
    }

    [Fact]
    public void BuildSnippet_OmissionMarker_WhenSectionsExceedBudget()
    {
        string markdown = @"## Section One
Relevant content about special artifacts.

## Section Two
" + new string('A', 800) + @"

## Section Three
" + new string('B', 800) + @"

## Section Four
" + new string('C', 800);

        // With small budget of 600 chars, only the top scoring section fits
        string snippet = WikiSnippetExtractor.BuildSnippet("Artifacts.md", markdown, new[] { "artifacts" }, 600);

        Assert.Contains("--- Artifacts.md ---", snippet);
        Assert.Contains("### Section One", snippet);
        Assert.Contains("further section(s) omitted; use wiki_view for the full text", snippet);
        Assert.DoesNotContain("— complete]", snippet);
    }

    [Fact]
    public void BuildSnippet_EmptyMatchFallback_ReturnsPreamblePlusFirstSection()
    {
        string markdown = @"Intro text about mechanics.

## Section 1
First section content.

## Section 2
Second section content.";

        // Query terms that match nothing in the document
        string snippet = WikiSnippetExtractor.BuildSnippet("Test.md", markdown, new[] { "nonexistentterm" }, 2500);

        Assert.Contains("--- Test.md ---", snippet);
        Assert.Contains("Intro text about mechanics.", snippet);
        Assert.Contains("### Section 1", snippet);
        Assert.DoesNotContain("### Section 2", snippet);
        Assert.Contains("further section(s) omitted", snippet);
    }

    [Fact]
    public void BuildSnippet_DocumentOrder_PreservesDocumentOrderRegardlessOfScoreRank()
    {
        string markdown = @"## Section Alpha
Mentions armor once.

## Section Beta
Mentions nothing of interest.

## Section Gamma
Mentions armor in heading and armor in body and shields and elite quality.";

        // Gamma scores much higher than Alpha, but both should be rendered in document order: Alpha then Gamma
        string snippet = WikiSnippetExtractor.BuildSnippet("Items.md", markdown, new[] { "armor", "shields", "elite" }, 2500);

        int posAlpha = snippet.IndexOf("### Section Alpha");
        int posGamma = snippet.IndexOf("### Section Gamma");

        Assert.True(posAlpha >= 0);
        Assert.True(posGamma >= 0);
        Assert.True(posAlpha < posGamma, "Alpha should appear before Gamma in document order");
    }
}
