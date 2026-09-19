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
        // Longer than half the budget, so the short-article rule does not apply.
        string markdown = @"Intro text about mechanics.

## Section 1
First section content.

## Section 2
Second section content. " + new string('z', 1300);

        // Query terms that match nothing in the document
        string snippet = WikiSnippetExtractor.BuildSnippet("Test.md", markdown, new[] { "nonexistentterm" }, 2500);

        Assert.Contains("--- Test.md ---", snippet);
        Assert.Contains("Intro text about mechanics.", snippet);
        Assert.Contains("### Section 1", snippet);
        Assert.DoesNotContain("### Section 2", snippet);
        Assert.Contains("further section(s) omitted", snippet);
    }

    [Fact]
    public void BuildSnippet_ShortArticleWithNoScoringSection_IsReturnedWhole()
    {
        string markdown = @"## Movement
Spells that move the caster or a target.

## Components
None of them needs a gesture.";

        string snippet = WikiSnippetExtractor.BuildSnippet("Spells/Movement.md", markdown, new[] { "somatic" }, 2500);

        Assert.Contains("### Movement", snippet);
        Assert.Contains("None of them needs a gesture.", snippet);
        Assert.EndsWith("[article: Spells/Movement.md — complete]", snippet);
    }

    [Fact]
    public void BuildSnippet_LongArticleWithNoScoringSection_ReturnsItsLeadSections()
    {
        string markdown = "## Overview\nThe lead section.\n\n"
            + "## History\n" + new string('h', 900) + "\n\n"
            + "## Trivia\n" + new string('t', 900);

        string snippet = WikiSnippetExtractor.BuildSnippet("Long.md", markdown, new[] { "somatic" }, 2500);

        Assert.Contains("The lead section.", snippet);
        Assert.DoesNotContain("### Trivia", snippet);
        Assert.Contains("[article: Long.md — 2 further section(s) omitted; use wiki_view for the full text]", snippet);
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

    // Spells/Cure petrification.md as the run-52 wiki carried it: the stat block is section 0,
    // and only the Description section shares a term with the query.
    private const string CurePetrificationArticle = @"## Level 4 healing spell

- **Attributes:** Wisdom
- **Mana cost:** 30.0
- **Casting time:** 1 round
- **Cooldown:** None
- **Targeting:** One target in selected direction
- **Range:** 25'
- **Train chance:** 100%
- **Base write cost:** 60 charges
- **Write cost:** From half to full base cost
- **Components:** Verbal, Material

## Material components - 2 castings

1. a ginseng root

## Description

Cures petrification";

    [Fact]
    public void BuildSnippet_ShortArticle_IsReturnedWholeWithItsStatBlock()
    {
        string snippet = WikiSnippetExtractor.BuildSnippet(
            "Spells/Cure petrification.md", CurePetrificationArticle, "cure petrification", 2500);

        Assert.Contains("- **Mana cost:** 30.0", snippet);
        Assert.Contains("1. a ginseng root", snippet);
        Assert.Contains("Cures petrification", snippet);
        Assert.EndsWith("[article: Spells/Cure petrification.md — complete]", snippet);
    }

    private static string LongArticle(string leadBody)
    {
        string filler = new string('x', 900);
        return "## Level 3 enchantment spell\n\n" + leadBody + "\n\n"
            + "## History\n\n" + filler + "\n\n"
            + "## Strategy\n\n" + filler + "\n\n"
            + "## Trivia\n\n" + filler + "\n\n"
            + "## Description\n\nCauses monsters to flee.";
    }

    [Fact]
    public void BuildSnippet_LongArticle_KeepsAShortLeadBlockAheadOfTheRankedSections()
    {
        string lead = "- **Mana cost:** 20.0\n- **Components:** Verbal, Material";
        string snippet = WikiSnippetExtractor.BuildSnippet("Spells/Fear.md", LongArticle(lead), "flee description", 1500);

        Assert.Contains("- **Mana cost:** 20.0", snippet);
        Assert.Contains("Causes monsters to flee.", snippet);
        Assert.Contains("further section(s) omitted", snippet);
        Assert.True(
            snippet.IndexOf("Mana cost", System.StringComparison.Ordinal) < snippet.IndexOf("Causes monsters", System.StringComparison.Ordinal),
            "The lead block keeps its document position ahead of the ranked section.");
    }

    [Fact]
    public void BuildSnippet_LongArticle_DropsALeadBlockLongerThanTheLimit()
    {
        string lead = "- **Mana cost:** 20.0\n" + new string('y', 1800);
        string snippet = WikiSnippetExtractor.BuildSnippet("Spells/Fear.md", LongArticle(lead), "flee description", 2500);

        Assert.DoesNotContain("- **Mana cost:** 20.0", snippet);
        Assert.Contains("Causes monsters to flee.", snippet);
    }
}
