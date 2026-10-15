namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services.Benchmarking;
using Overseer.Services.Benchmarking.Pdf;
using UglyToad.PdfPig;
using Xunit;

/// <summary>
/// What the benchmark PDFs keep together and how a comparison document's title page reads: a short
/// callout on one page with its heading, a heading with a lead paragraph and the table it introduces,
/// a table taller than a page flowing from where it starts, the comparison title printed once with the
/// document type in the metadata, and a tied rank printed as the span of its tie group.
/// </summary>
public class BenchmarkPdfLayoutTests
{
    private static readonly DateTime CreatedAt = new(2026, 9, 28, 10, 42, 0, DateTimeKind.Utc);

    public BenchmarkPdfLayoutTests()
    {
        BenchmarkPdfTestSetup.Configure();
    }

    // --- Callouts ----------------------------------------------------------------------------------

    [Fact]
    public void AShortCallout_StaysOnOnePageWithItsHeading_WhereverThePageBreakFalls()
    {
        bool movedPastPageOne = false;
        for (int filler = 14; filler <= 60; filler += 2)
        {
            var markdown = new StringBuilder();
            for (int i = 1; i <= filler; i++)
            {
                markdown.Append("Filler paragraph ").Append(i).Append(" moves the callout down the page.\n\n");
            }
            markdown.Append("### HEADINGMARKER\n\n")
                .Append("> [!WARNING]\n")
                .Append("> **Caveat.** CALLOUTSTART Established (High): measured, established after adjustment, robust, with two or more ")
                .Append("runs per suite on both sides. Probable (Moderate): one of those conditions is missed. Suggestive (Low): ")
                .Append("qualitative only, or the difference may be noise.\n")
                .Append(">\n")
                .Append("> CALLOUTEND One run per suite for every model, so no cause can be graded above Probable (Moderate).\n\n")
                .Append("Closing paragraph.\n");

            byte[] pdf = BenchmarkPdfRenderer.RenderMarkdown(markdown.ToString(), Info(), TestContext.Current.CancellationToken);

            using var reader = PdfDocument.Open(pdf);
            var pages = reader.GetPages().ToList();
            int heading = PageOf(pages, "HEADINGMARKER");
            int start = PageOf(pages, "CALLOUTSTART");
            int end = PageOf(pages, "CALLOUTEND");
            Assert.True(heading == start && start == end,
                $"With {filler} filler paragraphs the heading is on page {heading}, the callout starts on page {start} and ends on page {end}.");
            movedPastPageOne |= heading > 1;
        }

        Assert.True(movedPastPageOne, "No filler count pushed the callout past page 1, so no page break fell inside it.");
    }

    [Fact]
    public void ACallout_IsShortUpToAThirdOfAPage()
    {
        var frame = BenchmarkPdfRenderer.FigureFrameFor(BenchmarkPdfPaper.A4);
        string brief = "> [!NOTE]\n> **Question.** A short question.\n>\n> A short answer.\n";
        var longer = new StringBuilder("> [!NOTE]\n> **Question.** A long callout.\n");
        for (int i = 0; i < 30; i++) longer.Append(">\n> Paragraph ").Append(i).Append(" of a callout longer than a third of a page.\n");

        Assert.True(IsShortCallout(brief, frame));
        Assert.False(IsShortCallout(longer.ToString(), frame));
        Assert.False(IsShortCallout(brief, frame with { PageHeight = 0 }));
    }

    private static bool IsShortCallout(string markdown, BenchmarkPdfMarkdownComposer.FigureFrame frame)
    {
        var prepared = BenchmarkPdfMarkdownComposer.Prepare(markdown, "Title");
        var quote = prepared.Blocks.OfType<Markdig.Syntax.QuoteBlock>().Single();
        Assert.NotNull(BenchmarkPdfMarkdownComposer.CalloutOf(quote, prepared.Source));
        return BenchmarkPdfMarkdownComposer.IsShortCallout(quote, prepared, frame);
    }

    // --- Headings and tables -----------------------------------------------------------------------

    [Fact]
    public void AHeadingWithALeadParagraph_StaysOnThePageOfTheTableItIntroduces_WhereverThePageBreakFalls()
    {
        bool movedPastPageOne = false;
        for (int filler = 14; filler <= 60; filler += 2)
        {
            var markdown = new StringBuilder();
            for (int i = 1; i <= filler; i++)
            {
                markdown.Append("Filler paragraph ").Append(i).Append(" moves the heading and its table down the page.\n\n");
            }
            markdown.Append("## HEADINGMARKER\n\n")
                .Append("LEADMARKER In the order to make them: the Overseer chat first, then the benchmark, then model selection. ")
                .Append("Appendix D gives each run's launcher settings.\n\n")
                .Append("| # | Run | Purpose | Models |\n|---|---|---|---|\n")
                .Append("| 1 | FIRSTROWMARKER | C, the Overseer chat | Model A, Model B, Model C, Model D, Model E and Model F |\n")
                .Append("| 2 | Response-style pair | C, the Overseer chat | Model A |\n")
                .Append("| 3 | Time-of-day repeat | C, the Overseer chat | Model B |\n")
                .Append("| 4 | LASTROWMARKER | M, model selection | Model C and Model D |\n\n")
                .Append("Closing paragraph.\n");

            byte[] pdf = BenchmarkPdfRenderer.RenderMarkdown(markdown.ToString(), Info(), TestContext.Current.CancellationToken);

            using var reader = PdfDocument.Open(pdf);
            var pages = reader.GetPages().ToList();
            int heading = PageOf(pages, "HEADINGMARKER");
            int lead = PageOf(pages, "LEADMARKER");
            int first = PageOf(pages, "FIRSTROWMARKER");
            int last = PageOf(pages, "LASTROWMARKER");
            Assert.True(heading == lead && lead == first && first == last,
                $"With {filler} filler paragraphs the heading is on page {heading}, the lead on page {lead}, the first row on page {first} and the last on page {last}.");
            movedPastPageOne |= heading > 1;
        }

        Assert.True(movedPastPageOne, "No filler count pushed the heading past page 1.");
    }

    [Fact]
    public void AFewRowTableTallerThanAPage_FlowsFromItsHeadingsPage_InsteadOfLeavingItEmpty()
    {
        var markdown = new StringBuilder("Opening paragraph.\n\n")
            .Append("### How sure are we?\n\n")
            .Append("> [!WARNING]\n> **Caveat.** A short caveat.\n>\n> CALLOUTEND One run per suite for every model.\n\n")
            .Append("## TALLTABLEHEADING\n\n")
            .Append(TallTable());

        byte[] pdf = BenchmarkPdfRenderer.RenderMarkdown(markdown.ToString(), Info(), TestContext.Current.CancellationToken);

        using var reader = PdfDocument.Open(pdf);
        var pages = reader.GetPages().ToList();
        Assert.True(pages.Count >= 2, $"The table should run past one page, got {pages.Count}.");
        Assert.Equal(1, PageOf(pages, "CALLOUTEND"));
        Assert.Equal(1, PageOf(pages, "TALLTABLEHEADING"));
        Assert.Equal(1, PageOf(pages, "ROWMARKER1"));
        Assert.Contains("ROWMARKER9", string.Concat(pages.Select(p => Squash(p.Text))));
    }

    [Fact]
    public void ATable_IsKeptTogether_OnlyWithFewRowsThatFitOnAPage()
    {
        var frame = BenchmarkPdfRenderer.FigureFrameFor(BenchmarkPdfPaper.A4);

        var shortTable = new StringBuilder("| Model | Index |\n|---|---|\n");
        for (int i = 1; i <= 5; i++) shortTable.Append("| Model ").Append(i).Append(" | ").Append(80 - i).Append(" |\n");
        var manyRows = new StringBuilder("| Model | Index |\n|---|---|\n");
        for (int i = 1; i <= 12; i++) manyRows.Append("| Model ").Append(i).Append(" | ").Append(80 - i).Append(" |\n");

        Assert.True(KeepsTableTogether(shortTable.ToString(), frame));
        Assert.False(KeepsTableTogether(manyRows.ToString(), frame));
        Assert.False(KeepsTableTogether(TallTable(), frame));
        // Without a page height the row count decides alone.
        Assert.True(KeepsTableTogether(TallTable(), frame with { PageHeight = 0 }));
    }

    private static bool KeepsTableTogether(string markdown, BenchmarkPdfMarkdownComposer.FigureFrame frame)
    {
        var prepared = BenchmarkPdfMarkdownComposer.Prepare(markdown, "Title");
        var table = prepared.Blocks.OfType<Markdig.Extensions.Tables.Table>().Single();
        return BenchmarkPdfMarkdownComposer.KeepsTableTogether(table, prepared, frame);
    }

    /// <summary>Nine body rows whose long cells make the table taller than a page.</summary>
    private static string TallTable()
    {
        const string cell = "low-scoring answers, mostly Completeness: 2.2 index points on 17 questions (S2-Q6, S2-Q8, S2-Q12 "
            + "and 14 more); weakest dimension Completeness, 30.0 points below Accuracy; last on Completeness of 9 models; "
            + "1 critical error (S2-Q11); 1 of 36 answers without tools (S2-Q5), above the models' median of 0 %";
        var table = new StringBuilder("| Model | Strengths | Lost points on | Flags |\n|---|---|---|---|\n");
        for (int i = 1; i <= 9; i++)
        {
            table.Append("| ROWMARKER").Append(i).Append(" | ").Append(cell).Append(" | ").Append(cell).Append(" | ").Append(cell).Append(" |\n");
        }
        return table.ToString();
    }

    // --- The title -----------------------------------------------------------------------------------

    [Theory]
    [InlineData("Comparison #4 — 9 models", "Comparison #4 — 9 models", true)]
    [InlineData("Comparison #4 — 9 models", "Comparison #4 — 9 models: Root Cause Analysis", true)]
    [InlineData("Comparison #4 — 9 models: Root Cause Analysis", "Comparison #4 — 9 models", true)]
    [InlineData("Comparison #4", "Comparison #4 — 9 models: Root Cause Analysis", false)]
    [InlineData("Summary", "Comparison #4", false)]
    public void TheFirstHeading_IsDropped_WhenItRepeatsTheTitle_WithOrWithoutTheDocumentType(string heading, string title, bool dropped)
    {
        Assert.Equal(dropped, BenchmarkPdfMarkdownComposer.RepeatsTitle(heading, title));

        var prepared = BenchmarkPdfMarkdownComposer.Prepare("# " + heading + "\n\nBody.\n", title);
        Assert.Equal(dropped, !prepared.Blocks.OfType<Markdig.Syntax.HeadingBlock>().Any());
    }

    [Fact]
    public void AComparisonHeading_LeavesTheDocumentTypeToTheKicker()
    {
        Assert.Equal("Comparison #4 — 9 models · Two initial suites",
            BenchmarkPdfDocumentInfo.ComparisonDocumentHeading("Comparison #4 — 9 models · Two initial suites: Root Cause Analysis", "Root Cause Analysis"));
        Assert.Equal("Comparison #4",
            BenchmarkPdfDocumentInfo.ComparisonDocumentHeading("Comparison #4: Executive Summary", "Executive Summary"));
        Assert.Equal("Comparison #4 — name",
            BenchmarkPdfDocumentInfo.ComparisonDocumentHeading("Comparison #4 — name", "Root Cause Analysis"));
    }

    [Fact]
    public void AComparisonRow_SaysOnlyWhatTheTitleDoesNot()
    {
        var comparison = new BenchmarkPdfComparison(4, "9 models · Two initial suites", 9);

        Assert.Equal("9 models · computed 2026-10-15", BenchmarkPdfDocumentInfo.ComparisonDetailText(
            comparison, "2026-10-15", BenchmarkReportPeerNaming.Named, "Comparison #4 — 9 models · Two initial suites"));
        // A subset's title names its models, not the comparison, so the row keeps the name.
        Assert.Equal("9 models · Two initial suites · 9 models · computed 2026-10-15", BenchmarkPdfDocumentInfo.ComparisonDetailText(
            comparison, "2026-10-15", BenchmarkReportPeerNaming.Named, "Comparison #4 — A vs B"));
        Assert.Equal("9 models · computed 2026-10-15", BenchmarkPdfDocumentInfo.ComparisonDetailText(
            comparison, "2026-10-15", BenchmarkReportPeerNaming.Anonymized, "Comparison #4"));
        Assert.Equal(string.Empty, BenchmarkPdfDocumentInfo.ComparisonDetailText(
            new BenchmarkPdfComparison(4, null, null), null, BenchmarkReportPeerNaming.Named, "Comparison #4"));
    }

    [Theory]
    [InlineData(BenchmarkReportPeerNaming.Named, "Comparison #12 — Five-model comparison")]
    [InlineData(BenchmarkReportPeerNaming.Anonymized, "Comparison #12")]
    public void AComparisonWideCover_PrintsTheComparisonAsItsTitle_AndKeepsTheDocumentTypeInTheMetadataTitle(
        BenchmarkReportPeerNaming naming, string heading)
    {
        var document = ComparisonScopeDocument(BenchmarkReportAudience.ExecutiveSummary);
        var options = new BenchmarkReportRenderOptions { Disclosure = BenchmarkReportDisclosure.Summary, PeerNaming = naming };

        var info = BenchmarkPdfDocumentInfo.ForReportDocument(document, options, BenchmarkPdfPaper.A4);

        Assert.Equal(heading, info.HeadingTitle);
        Assert.Equal(heading + ": Executive Summary", info.Title);
        Assert.Equal("5 models · computed 2026-09-20", info.Facts.Single(f => f.Label == "Comparison").Value);
        Assert.Equal(heading, info.SubjectLine);
    }

    [Theory]
    [InlineData("")]
    [InlineData(": Executive Summary")]
    public void AComparisonPdf_PrintsItsNameOnceOnItsTitlePage(string headingSuffix)
    {
        const string name = "Five-model comparison";
        var document = ComparisonScopeDocument(BenchmarkReportAudience.ExecutiveSummary);
        var options = new BenchmarkReportRenderOptions { Disclosure = BenchmarkReportDisclosure.Summary, PeerNaming = BenchmarkReportPeerNaming.Named };
        var info = BenchmarkPdfDocumentInfo.ForReportDocument(document, options, BenchmarkPdfPaper.A4);
        string markdown = "# Comparison #12 — " + name + headingSuffix + "\n\n## The result in one sentence\n\nBODYMARKER text.\n";

        byte[] pdf = BenchmarkPdfRenderer.RenderMarkdown(markdown, info, TestContext.Current.CancellationToken);

        using var reader = PdfDocument.Open(pdf);
        Assert.Equal("Comparison #12 — " + name + ": Executive Summary", reader.Information.Title);
        string first = Squash(reader.GetPage(1).Text);
        Assert.Contains("BODYMARKER", first);
        Assert.Contains("EXECUTIVESUMMARY", first);
        Assert.Single(AllIndexesOf(first, Squash(name)));
    }

    /// <summary>
    /// The fixture's Executive Summary as a comparison-scope document of Comparison #12, "Five-model
    /// comparison" of five models, covering every entry.
    /// </summary>
    private static BenchmarkReportDocument ComparisonScopeDocument(BenchmarkReportAudience audience)
    {
        var document = BenchmarkReportPackFixture.Document(audience);
        document.ComparisonId = 12;
        document.Comparison = new BenchmarkComparison
        {
            Id = 12,
            ComparisonKey = new string('c', 64),
            EntryKeysJson = "[]",
            SubjectKind = BenchmarkComparisonSubjectKind.Runs,
            EntryCount = 5,
            DefaultName = "Five-model comparison",
            CreatedAtUtc = BenchmarkReportPackFixture.CreatedAt
        };
        document.Scope = BenchmarkReportScope.Comparison;
        document.SubjectKey = "comparison:12";

        var facts = JsonNode.Parse(document.FactsJson)!.AsObject();
        facts["scope"] = BenchmarkReportFactSheet.ComparisonScopeValue;
        facts["coversAllEntries"] = true;
        facts["comparisonEntryCount"] = 5;
        document.FactsJson = facts.ToJsonString();
        return document;
    }

    // --- Tied ranks ----------------------------------------------------------------------------------

    [Fact]
    public void ATiedRank_PrintsTheSpanOfItsTieGroup()
    {
        var entries = new List<(string Key, double Score, double? Lower, double? Upper)> { ("m1", 90, 88, 92) };
        for (int i = 2; i <= 9; i++)
        {
            double score = 84 - i;
            entries.Add(("m" + i, score, score - 3, score + 3));
        }

        var ranks = BenchmarkReportFacts.JointRanks(entries);

        Assert.Equal("1st of 9", BenchmarkReportFacts.JointRankText(ranks["m1"], 9));
        Assert.Equal("2–9 of 9 (tied: intervals overlap)", BenchmarkReportFacts.JointRankText(ranks["m2"], 9));
        Assert.Equal("2–9 of 9 (tied: intervals overlap)", BenchmarkReportFacts.JointRankText(ranks["m9"], 9));
        Assert.Equal("2–9", BenchmarkReportFacts.TiedRankSpan(ranks["m9"]));
        Assert.Equal("1", BenchmarkReportFacts.TiedRankSpan(ranks["m1"]));
        Assert.DoesNotContain("joint", BenchmarkReportFacts.JointRankText(ranks["m5"], 9), StringComparison.OrdinalIgnoreCase);
    }

    // --- Helpers -------------------------------------------------------------------------------------

    private static BenchmarkPdfDocumentInfo Info() => new()
    {
        DocumentKind = "Technical Report",
        Title = "PDF Fixture Title",
        SubjectLine = "Fixture Suite · run #12",
        Classification = BenchmarkPdfClassification.ProviderConfidential,
        ClassificationText = "Confidential. Prepared for the fixture's provider.",
        Facts = new[] { new BenchmarkPdfFact("Run", "#12"), new BenchmarkPdfFact("Suite", "Fixture Suite") },
        CreatedAtUtc = CreatedAt,
        Keywords = new[] { "Fixture Model", "Fixture Suite", "GnollBench" }
    };

    /// <summary>The number of the first page whose text holds <paramref name="marker"/>.</summary>
    private static int PageOf(IReadOnlyList<UglyToad.PdfPig.Content.Page> pages, string marker)
        => pages.First(p => Squash(p.Text).Contains(marker, StringComparison.Ordinal)).Number;

    private static List<int> AllIndexesOf(string text, string value)
    {
        var indexes = new List<int>();
        for (int i = text.IndexOf(value, StringComparison.Ordinal); i >= 0; i = text.IndexOf(value, i + 1, StringComparison.Ordinal))
        {
            indexes.Add(i);
        }
        return indexes;
    }

    /// <summary>The text with all whitespace removed: extraction does not reliably keep spaces.</summary>
    private static string Squash(string text) => new(text.Where(c => !char.IsWhiteSpace(c)).ToArray());
}
