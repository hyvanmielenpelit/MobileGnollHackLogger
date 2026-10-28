namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services.Benchmarking;
using Overseer.Services.Benchmarking.RootCause;
using Xunit;
using static Overseer.Tests.UnitTests.BenchmarkReportPackFixture;

/// <summary>
/// The report-pack renderer: which audience, disclosure and naming combinations it accepts, what
/// each disclosure level may print, peer anonymization, token substitution, and the determinism
/// rules — output from the stored row alone, no clock, no culture, stated ordering, <c>\n</c> line
/// breaks without a BOM, a versioned format checked against golden files, and identical bytes
/// after a database round trip.
/// </summary>
public class BenchmarkReportPackRendererTests
{
    public static IEnumerable<object[]> Combinations() => AllowedCombinations();

    private static string Render(
        BenchmarkReportAudience audience, BenchmarkReportDisclosure disclosure, BenchmarkReportPeerNaming naming)
        => BenchmarkReportPackRenderer.Render(
            Document(audience),
            new BenchmarkReportRenderOptions { Disclosure = disclosure, PeerNaming = naming });

    private static string Sha256(string text)
        => Convert.ToHexString(SHA256.HashData(new UTF8Encoding(false).GetBytes(text)));

    /// <summary>Byte equality, failing with the first differing line and both versions of it.</summary>
    private static void AssertSameText(string expected, string actual, string what)
    {
        if (string.Equals(expected, actual, StringComparison.Ordinal)) return;

        var expectedLines = expected.Split('\n');
        var actualLines = actual.Split('\n');
        int shared = Math.Min(expectedLines.Length, actualLines.Length);
        int index = 0;
        while (index < shared && string.Equals(expectedLines[index], actualLines[index], StringComparison.Ordinal)) index++;

        string expectedLine = index < expectedLines.Length ? expectedLines[index] : "<end of file>";
        string actualLine = index < actualLines.Length ? actualLines[index] : "<end of output>";
        Assert.Fail(what + ": first difference at line index " + index.ToString(CultureInfo.InvariantCulture)
            + "\nexpected: " + expectedLine
            + "\nactual:   " + actualLine);
    }

    // ---------------------------------------------------------------------------------------------
    // Combinations
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void AllowedDisclosures_AreEveryLevelForTheProviderIssueReport_AndFullAloneForTheRootCauseAnalysis()
    {
        var all = new[] { BenchmarkReportDisclosure.Summary, BenchmarkReportDisclosure.Detailed, BenchmarkReportDisclosure.Full };

        Assert.Equal(all, BenchmarkReportPackRenderer.AllowedDisclosures(BenchmarkReportAudience.ProviderIssueReport));
        Assert.Equal(new[] { BenchmarkReportDisclosure.Full }, BenchmarkReportPackRenderer.AllowedDisclosures(BenchmarkReportAudience.RootCauseAnalysis));
        Assert.Equal(4, Combinations().Count());
    }

    [Fact]
    public void TheExecutiveSummary_OffersSummaryAndFull_AndStillRendersDetailed()
    {
        Assert.Equal(
            new[] { BenchmarkReportDisclosure.Summary, BenchmarkReportDisclosure.Full },
            BenchmarkReportPackRenderer.AllowedDisclosures(BenchmarkReportAudience.ExecutiveSummary));

        foreach (var naming in new[] { BenchmarkReportPeerNaming.Named, BenchmarkReportPeerNaming.Anonymized })
        {
            var options = new BenchmarkReportRenderOptions { Disclosure = BenchmarkReportDisclosure.Detailed, PeerNaming = naming };
            Assert.True(BenchmarkReportPackRenderer.IsAllowed(BenchmarkReportAudience.ExecutiveSummary, options));

            string detailed = Render(BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportDisclosure.Detailed, naming);
            string summary = Render(BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportDisclosure.Summary, naming);

            // The same text as Summary, under the Executive Summary's own Detailed stamp and footer.
            Assert.Contains("*Confidential. Prepared for the model's provider. Review before sharing.*\n", detailed);
            Assert.Contains(" · disclosure Detailed · ", detailed);
            string sameBody = summary
                .Replace(BenchmarkReportPackRenderer.Stamp(BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportDisclosure.Summary),
                    BenchmarkReportPackRenderer.Stamp(BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportDisclosure.Detailed), StringComparison.Ordinal)
                .Replace(" · disclosure Summary · ", " · disclosure Detailed · ", StringComparison.Ordinal);
            AssertSameText(sameBody, detailed, "Executive Summary at Detailed");
            Assert.DoesNotContain(Q1Text, detailed);
            Assert.DoesNotContain(Q3Text, detailed);
        }

        Assert.False(BenchmarkReportPackRenderer.IsAllowed(
            BenchmarkReportAudience.RootCauseAnalysis,
            new BenchmarkReportRenderOptions { Disclosure = BenchmarkReportDisclosure.Detailed }));
    }

    [Theory]
    [InlineData(BenchmarkReportDisclosure.Summary, BenchmarkReportPeerNaming.Named)]
    [InlineData(BenchmarkReportDisclosure.Summary, BenchmarkReportPeerNaming.Anonymized)]
    [InlineData(BenchmarkReportDisclosure.Detailed, BenchmarkReportPeerNaming.Named)]
    [InlineData(BenchmarkReportDisclosure.Detailed, BenchmarkReportPeerNaming.Anonymized)]
    public void TheRootCauseAnalysis_BelowFull_IsRefused(BenchmarkReportDisclosure disclosure, BenchmarkReportPeerNaming naming)
    {
        var options = new BenchmarkReportRenderOptions { Disclosure = disclosure, PeerNaming = naming };

        Assert.False(BenchmarkReportPackRenderer.IsAllowed(BenchmarkReportAudience.RootCauseAnalysis, options));
        Assert.Throws<ArgumentException>(() =>
            BenchmarkReportPackRenderer.Render(Document(BenchmarkReportAudience.RootCauseAnalysis), options));
    }

    [Fact]
    public void UndefinedOptions_AreRefused()
    {
        var badDisclosure = new BenchmarkReportRenderOptions { Disclosure = (BenchmarkReportDisclosure)99 };
        var badNaming = new BenchmarkReportRenderOptions { PeerNaming = (BenchmarkReportPeerNaming)99 };

        Assert.False(BenchmarkReportPackRenderer.IsAllowed(BenchmarkReportAudience.ExecutiveSummary, badDisclosure));
        Assert.False(BenchmarkReportPackRenderer.IsAllowed(BenchmarkReportAudience.ExecutiveSummary, badNaming));
        Assert.False(BenchmarkReportPackRenderer.IsAllowed(BenchmarkReportAudience.ExecutiveSummary, null!));
        Assert.False(BenchmarkReportPackRenderer.IsAllowed((BenchmarkReportAudience)2, new BenchmarkReportRenderOptions()));
        Assert.Throws<ArgumentException>(() =>
            BenchmarkReportPackRenderer.Render(Document(BenchmarkReportAudience.ExecutiveSummary), badNaming));
    }

    [Theory]
    [MemberData(nameof(Combinations))]
    public void EveryAllowedCombination_Renders_AndMatchesItsGoldenFile(
        BenchmarkReportAudience audience, BenchmarkReportDisclosure disclosure, BenchmarkReportPeerNaming naming, string file)
    {
        Assert.True(BenchmarkReportPackRenderer.IsAllowed(audience, new BenchmarkReportRenderOptions { Disclosure = disclosure, PeerNaming = naming }));

        string rendered = Render(audience, disclosure, naming);

        AssertMatchesGolden(file, rendered);
    }

    [Theory]
    [InlineData(BenchmarkReportAudience.ExecutiveSummary, "exec_standalone.md")]
    public void TheStandaloneForm_MatchesItsGoldenFile(BenchmarkReportAudience audience, string file)
    {
        string rendered = BenchmarkReportPackRenderer.Render(
            StandaloneDocument(audience),
            new BenchmarkReportRenderOptions { Disclosure = BenchmarkReportDisclosure.Full, PeerNaming = BenchmarkReportPeerNaming.Named });

        AssertMatchesGolden(file, rendered);
    }

    /// <summary>The golden comparison, or with <c>OVERSEER_UPDATE_GOLDENS=1</c> the golden file written from the output.</summary>
    private static void AssertMatchesGolden(string file, string rendered)
    {
        if (UpdateGoldens)
        {
            WriteGolden(file, rendered);
            return;
        }

        AssertSameText(ReadGolden(file), rendered, file);
    }

    // ---------------------------------------------------------------------------------------------
    // Disclosure and naming
    // ---------------------------------------------------------------------------------------------

    private const string Q1Text = "What happens if I throw a gem at a co-aligned unicorn?";
    private const string Q3Text = "Will my gem break if I throw it at a unicorn?";
    private const string Q3Excerpt = "A thrown gem always shatters on impact, so never throw";
    private const string Q1Rubric = "A valuable gem raises Luck; worthless glass does not.";
    private const string Q1Evidence = "Accuracy: Matches rubric.";
    private const string Q3Ruling = "Gems are caught, not broken (dothrow.c).";

    [Fact]
    public void AtSummary_NoQuestionTextRubricAnswerOrGraderEvidenceIsPrinted()
    {
        string text = Render(BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportDisclosure.Summary, BenchmarkReportPeerNaming.Named);

        Assert.DoesNotContain(Q1Text, text);
        Assert.DoesNotContain(Q3Text, text);
        Assert.DoesNotContain(Q3Excerpt, text);
        Assert.DoesNotContain(Q1Rubric, text);
        Assert.DoesNotContain(Q1Evidence, text);
        Assert.DoesNotContain(Q3Ruling, text);
        Assert.Contains("Questions are described, not quoted.", text);
    }

    [Fact]
    public void TheExecutiveSummary_NeverQuotesQuestions_AtAnyLevel()
    {
        foreach (var disclosure in Enum.GetValues<BenchmarkReportDisclosure>())
        {
            string text = Render(BenchmarkReportAudience.ExecutiveSummary, disclosure, BenchmarkReportPeerNaming.Named);

            Assert.DoesNotContain(Q3Text, text);
            Assert.DoesNotContain(Q3Excerpt, text);
            Assert.DoesNotContain(Q1Rubric, text);
        }
    }

    [Theory]
    [MemberData(nameof(Combinations))]
    public void Anonymization_RemovesPeerNamesModelIdsAndProviders_Everywhere(
        BenchmarkReportAudience audience, BenchmarkReportDisclosure disclosure, BenchmarkReportPeerNaming naming, string file)
    {
        string text = Render(audience, disclosure, naming);
        string[] peerIdentity = { "Grok 5", "grok-5", "xAI", "Mistral Large 4", "mistral-large-4", "Mistral" };

        // The subject is always named.
        Assert.Contains("GPT-5.6 Luna", text);

        if (naming == BenchmarkReportPeerNaming.Anonymized)
        {
            foreach (var name in peerIdentity) Assert.DoesNotContain(name, text);
            Assert.DoesNotContain("| Provider |", text);
            Assert.Contains("Models A and B, identities withheld", text);
        }
        else
        {
            Assert.Contains("Model A = Grok 5 (xAI, grok-5)", text);
            Assert.Contains("Model B = Mistral Large 4 (Mistral, mistral-large-4)", text);
        }

        Assert.NotEmpty(file);
    }

    [Theory]
    [MemberData(nameof(Combinations))]
    public void Tokens_AreResolved_InEveryDocument(
        BenchmarkReportAudience audience, BenchmarkReportDisclosure disclosure, BenchmarkReportPeerNaming naming, string file)
    {
        string text = Render(audience, disclosure, naming);

        Assert.DoesNotContain("{{", text);
        Assert.DoesNotContain("}}", text);
        Assert.NotEmpty(file);
    }

    [Fact]
    public void PeerTokens_RenderAsTheNameOrTheLetter_AndFactTokensAsTheirDisplay()
    {
        string named = Render(BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportDisclosure.Summary, BenchmarkReportPeerNaming.Named);
        string anonymized = Render(BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportDisclosure.Summary, BenchmarkReportPeerNaming.Anonymized);

        Assert.Contains("GPT-5.6 Luna places 1–3 of 3 (tied: intervals overlap) on intelligence, but its interval overlaps those of Grok 5 and Mistral Large 4, "
            + "so the order is not established. Its paired difference from Grok 5 is -4.6 points.", named);
        Assert.Contains("GPT-5.6 Luna places 1–3 of 3 (tied: intervals overlap) on intelligence, but its interval overlaps those of Model A and Model B, "
            + "so the order is not established. Its paired difference from Model A is -4.6 points.", anonymized);
    }

    [Fact]
    public void AnUnavailableFact_IsStatedAsNotAvailable_WithItsReason()
    {
        var document = Document(BenchmarkReportAudience.ExecutiveSummary);
        var sheet = Sheet();
        var speed = sheet.Facts.Single(f => f.Key == "speed.modelTimeP50");
        speed.Display = BenchmarkReportFacts.NotAvailable;
        speed.Available = false;
        speed.UnavailableReason = "Speed was measured with parallel execution disabled.";
        document.FactsJson = BenchmarkReportJson.Serialize(sheet);

        string text = BenchmarkReportPackRenderer.Render(document,
            new BenchmarkReportRenderOptions { Disclosure = BenchmarkReportDisclosure.Summary, PeerNaming = BenchmarkReportPeerNaming.Named });

        Assert.Contains("- **Speed:** Not available. Speed was measured with parallel execution disabled.\n", text);
    }

    [Fact]
    public void TheRemovedContentNotice_ListsDroppedItems_OnlyWhenTheDocumentHasWarnings()
    {
        string summary = Render(BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportDisclosure.Summary, BenchmarkReportPeerNaming.Named);
        string full = Render(BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportDisclosure.Full, BenchmarkReportPeerNaming.Named);

        Assert.Contains("## Removed content", summary);
        Assert.Contains("- `weaknesses[2]` (rule 4)\n", summary);
        Assert.DoesNotContain("sections.abstract", summary);
        Assert.DoesNotContain("Cited R9", summary);
        Assert.Contains("- `weaknesses[2]` (rule 4): Cited R9, which does not exist.", full);

        var completed = Document(BenchmarkReportAudience.ExecutiveSummary);
        completed.Status = BenchmarkReportDocumentStatus.Completed;
        string clean = BenchmarkReportPackRenderer.Render(completed, new BenchmarkReportRenderOptions());
        Assert.DoesNotContain("## Removed content", clean);
    }

    // ---------------------------------------------------------------------------------------------
    // Determinism (D13)
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Rule1_OutputDependsOnlyOnTheStoredColumnsTheRendererReads()
    {
        var options = new BenchmarkReportRenderOptions { Disclosure = BenchmarkReportDisclosure.Full, PeerNaming = BenchmarkReportPeerNaming.Named };
        var baseline = Document(BenchmarkReportAudience.ExecutiveSummary);

        // Columns the renderer does not read: changing them changes nothing.
        var other = Document(BenchmarkReportAudience.ExecutiveSummary);
        other.Title = "A stale title";
        other.PackId = Guid.Empty;
        other.InputTokens = 1;
        other.CostUsd = null;
        other.Runs.Clear();

        Assert.Equal(
            BenchmarkReportPackRenderer.Render(baseline, options),
            BenchmarkReportPackRenderer.Render(other, options));
    }

    [Fact]
    public void Rule2_TheOnlyTimePrinted_IsTheStoredCreationTime()
    {
        var options = new BenchmarkReportRenderOptions { Disclosure = BenchmarkReportDisclosure.Full, PeerNaming = BenchmarkReportPeerNaming.Named };

        var first = Document(BenchmarkReportAudience.ExecutiveSummary);
        first.CreatedAtUtc = new DateTime(2001, 2, 3, 4, 5, 0, DateTimeKind.Utc);
        var second = Document(BenchmarkReportAudience.ExecutiveSummary);
        second.CreatedAtUtc = new DateTime(2011, 12, 13, 14, 15, 0, DateTimeKind.Utc);

        string a = BenchmarkReportPackRenderer.Render(first, options);
        string b = BenchmarkReportPackRenderer.Render(second, options);

        Assert.Contains("- **Date:** 2001-02-03\n", a);
        Assert.Contains("created 2001-02-03 04:05 UTC", a);
        AssertSameText(b, a.Replace("2001-02-03 04:05", "2011-12-13 14:15", StringComparison.Ordinal)
            .Replace("2001-02-03", "2011-12-13", StringComparison.Ordinal), "creation time substitution");
    }

    [Fact]
    public void Rule3_RenderingIsCultureInvariant()
    {
        var documents = AllowedCombinations()
            .Select(c => (Document: Document((BenchmarkReportAudience)c[0]), Options: new BenchmarkReportRenderOptions
            {
                Disclosure = (BenchmarkReportDisclosure)c[1],
                PeerNaming = (BenchmarkReportPeerNaming)c[2]
            }))
            .ToList();

        var culture = CultureInfo.CurrentCulture;
        var uiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("fi-FI");
            CultureInfo.CurrentUICulture = new CultureInfo("fi-FI");
            var finnish = documents.Select(d => BenchmarkReportPackRenderer.Render(d.Document, d.Options)).ToList();

            CultureInfo.CurrentCulture = new CultureInfo("en-US");
            CultureInfo.CurrentUICulture = new CultureInfo("en-US");
            var english = documents.Select(d => BenchmarkReportPackRenderer.Render(d.Document, d.Options)).ToList();

            for (int i = 0; i < finnish.Count; i++)
            {
                Assert.Equal(Sha256(english[i]), Sha256(finnish[i]));
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = uiCulture;
        }
    }

    [Fact]
    public void Rule4_OutputOrderIsIndependentOfStoredCollectionOrder()
    {
        var options = new BenchmarkReportRenderOptions { Disclosure = BenchmarkReportDisclosure.Full, PeerNaming = BenchmarkReportPeerNaming.Named };
        string expected = BenchmarkReportPackRenderer.Render(Document(BenchmarkReportAudience.ExecutiveSummary), options);

        var sheet = Sheet();
        sheet.Peers.Reverse();
        sheet.Questions.Reverse();
        sheet.Rows.Reverse();
        sheet.Entries.Reverse();
        sheet.Facts.Reverse();

        var writer = Writer();
        writer.Sections = writer.Sections.Reverse().ToDictionary(kv => kv.Key, kv => kv.Value);
        writer.QuestionTopics.Reverse();

        var shuffled = Document(BenchmarkReportAudience.ExecutiveSummary);
        shuffled.FactsJson = BenchmarkReportJson.Serialize(sheet);
        shuffled.WriterOutputJson = BenchmarkReportJson.Serialize(writer);

        AssertSameText(expected, BenchmarkReportPackRenderer.Render(shuffled, options), "shuffled collections");
    }

    [Theory]
    [MemberData(nameof(Combinations))]
    public void Rule5_LineBreaksAreLf_AndTheUtf8BytesCarryNoBom(
        BenchmarkReportAudience audience, BenchmarkReportDisclosure disclosure, BenchmarkReportPeerNaming naming, string file)
    {
        var document = Document(audience);
        var writer = Writer();
        writer.Headline = "Line one.\r\nLine two.";
        writer.Sections[BenchmarkReportSlots.Meaning] = "First.\r\n\r\nSecond.";
        document.WriterOutputJson = BenchmarkReportJson.Serialize(writer);

        string text = BenchmarkReportPackRenderer.Render(document, new BenchmarkReportRenderOptions { Disclosure = disclosure, PeerNaming = naming });
        byte[] bytes = new UTF8Encoding(false).GetBytes(text);

        Assert.DoesNotContain("\r", text);
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, file + " starts with a BOM");
        Assert.EndsWith("\n", text);
        Assert.False(text.StartsWith('﻿'));
    }

    [Fact]
    public async Task Rule7_TheSameDocumentYieldsTheSameBytes_TwiceAndAfterADatabaseRoundTrip()
    {
        var dbOptions = BenchmarkRunExamTests.InMemoryOptions();

        await using (var db = new ApplicationDbContext(dbOptions))
        {
            foreach (var audience in new[] { BenchmarkReportAudience.ExecutiveSummary })
            {
                var document = Document(audience);
                document.Id = 100 + (int)audience;
                db.BenchmarkReportDocuments.Add(document);
            }
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        foreach (var combination in AllowedCombinations())
        {
            var audience = (BenchmarkReportAudience)combination[0];
            var options = new BenchmarkReportRenderOptions
            {
                Disclosure = (BenchmarkReportDisclosure)combination[1],
                PeerNaming = (BenchmarkReportPeerNaming)combination[2]
            };

            var fresh = Document(audience);
            fresh.Id = 100 + (int)audience;
            string once = BenchmarkReportPackRenderer.Render(fresh, options);
            string twice = BenchmarkReportPackRenderer.Render(fresh, options);

            await using var db = new ApplicationDbContext(dbOptions);
            var stored = await db.BenchmarkReportDocuments.AsNoTracking().SingleAsync(d => d.Id == 100 + (int)audience, TestContext.Current.CancellationToken);
            string reloaded = BenchmarkReportPackRenderer.Render(stored, options);

            Assert.Equal(Sha256(once), Sha256(twice));
            Assert.Equal(Sha256(once), Sha256(reloaded));
        }
    }

    [Fact]
    public void TheRenderer_HoldsNoState_OnlyLiteralConstants()
    {
        Assert.True(typeof(BenchmarkReportPackRenderer).IsAbstract && typeof(BenchmarkReportPackRenderer).IsSealed,
            "BenchmarkReportPackRenderer must be static.");
        var fields = typeof(BenchmarkReportPackRenderer).GetFields(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        Assert.All(fields, f => Assert.True(f.IsLiteral, f.Name + " is not a const"));
        Assert.Contains(fields, f => f.Name == nameof(BenchmarkReportPackRenderer.ReportFormatVersion));
    }

    // ---------------------------------------------------------------------------------------------
    // Evidence lines, evaluation terms, the stand-alone form and the label
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void TheExecutiveSummary_PrintsNoEvidenceLine_AndPlainSupportLabels_AtEveryLevel()
    {
        foreach (var disclosure in Enum.GetValues<BenchmarkReportDisclosure>())
        {
            string text = Render(BenchmarkReportAudience.ExecutiveSummary, disclosure, BenchmarkReportPeerNaming.Named);

            Assert.DoesNotContain("*Evidence:*", text);
            Assert.DoesNotContain("as asked", text);
            Assert.Contains("- Answers simple questions precisely and briefly. *(raised by one grader)*\n", text);
            // An item both graders agreed on carries no label here.
            Assert.Contains(", where Grok 5 scored well.\n", text);
            Assert.DoesNotContain("both graders agreed", text);
            Assert.Contains("*(from per-question results)*\n", text);
            Assert.DoesNotContain("different provider", text);
            Assert.DoesNotContain("family", text);
        }

        Assert.Equal("the graders disagree", BenchmarkReportPackRenderer.PlainSupportLabel(BenchmarkReportFacts.SupportGradersDisagree));
        Assert.Equal("raised by one grader, from the model's own company",
            BenchmarkReportPackRenderer.PlainSupportLabel(BenchmarkReportFacts.SupportOneGraderSameProvider));
        Assert.Equal("raised by one grader, from the model's own company",
            BenchmarkReportPackRenderer.PlainSupportLabel(LegacySameFamilyLabel));
        Assert.Equal("raised by one grader", BenchmarkReportPackRenderer.PlainSupportLabel(LegacyDifferentFamilyLabel));
    }

    [Theory]
    [MemberData(nameof(Combinations))]
    public void NoRawFactKey_IsPrintedInAnEvidenceLine(
        BenchmarkReportAudience audience, BenchmarkReportDisclosure disclosure, BenchmarkReportPeerNaming naming, string file)
    {
        string text = Render(audience, disclosure, naming);
        var rawKey = new System.Text.RegularExpressions.Regex(@"[a-z]+\.[a-zA-Z.]+:");

        foreach (string line in text.Split('\n').Where(l => l.Contains("*Evidence:*", StringComparison.Ordinal)))
        {
            Assert.False(rawKey.IsMatch(line), file + ": " + line);
        }
    }

    [Theory]
    [InlineData(BenchmarkReportAudience.ExecutiveSummary)]
    public void WithoutFrontMatter_TheStampAndFactsListAreLeftOut(BenchmarkReportAudience audience)
    {
        var options = new BenchmarkReportRenderOptions
        {
            Disclosure = BenchmarkReportDisclosure.Full,
            PeerNaming = BenchmarkReportPeerNaming.Named,
            IncludeFrontMatter = false
        };
        string text = BenchmarkReportPackRenderer.Render(Document(audience), options);
        string withFrontMatter = Render(audience, BenchmarkReportDisclosure.Full, BenchmarkReportPeerNaming.Named);

        Assert.StartsWith("# " + BenchmarkReportPackRenderer.BuildTitle(audience, Sheet()) + "\n\n## ", text);
        Assert.DoesNotContain("- **Date:**", text);
        Assert.DoesNotContain("- **Peers:**", text);
        Assert.DoesNotContain("*INTERNAL —", text);
        Assert.Contains("- **Date:** 2026-09-28\n", withFrontMatter);
        Assert.Contains("- **Compared with:** Model A = Grok 5", withFrontMatter);
        Assert.True(new BenchmarkReportRenderOptions().IncludeFrontMatter);
        Assert.True(new BenchmarkReportRenderOptions().IncludeDocumentFooter);
    }

    [Theory]
    [InlineData(BenchmarkReportAudience.ExecutiveSummary)]
    public void WithoutTheDocumentFooter_TheIdVersionAndProvenanceLinesAreLeftOut(BenchmarkReportAudience audience)
    {
        var options = new BenchmarkReportRenderOptions
        {
            Disclosure = BenchmarkReportDisclosure.Full,
            PeerNaming = BenchmarkReportPeerNaming.Named,
            IncludeDocumentFooter = false
        };
        string text = BenchmarkReportPackRenderer.Render(Document(audience), options);
        string withFooter = Render(audience, BenchmarkReportDisclosure.Full, BenchmarkReportPeerNaming.Named);

        Assert.DoesNotContain("*Document ID", text);
        Assert.DoesNotContain("\n---\n", text);
        Assert.DoesNotContain("Figures and tables were computed by Overseer.", text);
        Assert.Contains("## Removed content", text);
        Assert.EndsWith("\n", text);
        Assert.Contains("*Document ID 101 · format version " + BenchmarkReportPackRenderer.ReportFormatVersion.ToString(CultureInfo.InvariantCulture) + " · ", withFooter);
        Assert.Contains("*Figures and tables were computed by Overseer. The prose was written by Claude Opus 5.5 from those figures and "
            + "checked automatically for structure, permitted figures and references, word limits, disclosure of benchmark text, "
            + "peer names, significance claims, interval-overlap wording, hype words and spelling; the checks do not verify the "
            + "prose's interpretations.*\n", withFooter);
        Assert.DoesNotContain("generated under format version", withFooter);
        Assert.Contains("## Evaluation terms", text);
    }

    [Fact]
    public void TheStamp_FollowsTheAudience()
    {
        string executiveFull = Render(BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportDisclosure.Full, BenchmarkReportPeerNaming.Named);
        string executiveDetailed = Render(BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportDisclosure.Detailed, BenchmarkReportPeerNaming.Named);
        string executiveSummary = Render(BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportDisclosure.Summary, BenchmarkReportPeerNaming.Named);

        Assert.Contains("*INTERNAL — unpublished benchmark results. Do not share outside the Overseer team.*\n", executiveFull);
        Assert.Contains("*Confidential. Prepared for the model's provider. Review before sharing.*\n", executiveDetailed);
        Assert.Contains("*Confidential. Prepared for the model's provider. Questions are described, not quoted.*\n", executiveSummary);
        Assert.DoesNotContain("contains benchmark questions", executiveFull);
        Assert.DoesNotContain("Contains benchmark questions", executiveDetailed);

        // A document that may quote questions and rubrics carries the stamps that say so.
        Assert.Equal("INTERNAL — contains benchmark questions and rubrics. Do not share outside the Overseer team.",
            BenchmarkReportPackRenderer.Stamp(BenchmarkReportAudience.RootCauseAnalysis, BenchmarkReportDisclosure.Full));
        Assert.Equal("Confidential. Prepared for the model's provider. Contains benchmark questions — do not publish.",
            BenchmarkReportPackRenderer.Stamp(BenchmarkReportDisclosure.Detailed));
    }

    [Fact]
    public void TheExecutiveSummary_HeadsItsConfidenceSlot_HowReliableThisResultIs()
    {
        string text = Render(BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportDisclosure.Summary, BenchmarkReportPeerNaming.Named);

        Assert.Contains("## How reliable this result is\n", text);
        Assert.DoesNotContain("How confident", text);
    }

    [Fact]
    public void KeyFigures_PrintTheIndexAndItsInterval_FromTheSameRounding()
    {
        string text = Render(BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportDisclosure.Summary, BenchmarkReportPeerNaming.Named);

        // The rank is stated once, on the Intelligence line.
        Assert.Contains("## Key figures\n\n- **Intelligence:** 80 / 100 (interval 77–83), 1–3 of 3 (tied: intervals overlap); its 95% interval overlaps every peer's.\n"
            + "- **Speed:**", text);
        Assert.Contains("- **Critical errors:** 1 of 4 answers.\n", text);
        Assert.DoesNotContain("±", text);
        Assert.DoesNotContain("- **Rank:**", text);
        Assert.DoesNotContain("the order is not established where intervals overlap", text);
    }

    [Fact]
    public void AWithheldRunsFact_AddsOneSentence_AfterTheKeyFigures()
    {
        const string Sentence = "1 of 3 runs withholds its own Intelligence Index because a question failed at the provider; "
            + "the Intelligence figure above is over the scored questions.";
        const string Plural = "2 of 3 runs withhold their own Intelligence Index because a question failed at the provider; "
            + "the Intelligence figure above is over the scored questions.";
        const BenchmarkReportAudience audience = BenchmarkReportAudience.ExecutiveSummary;
        var options = new BenchmarkReportRenderOptions { PeerNaming = BenchmarkReportPeerNaming.Named, Disclosure = BenchmarkReportDisclosure.Summary };
        string plain = BenchmarkReportPackRenderer.Render(Document(audience), options);
        Assert.DoesNotContain("because a question failed at the provider", plain);

        string text = RenderWithWithheldRuns(audience, options, 1, "1 of 3 runs");
        Assert.Single(AllIndexesOf(text, Sentence));
        Assert.InRange(text.IndexOf("- **Intelligence:**", StringComparison.Ordinal), 0, text.IndexOf(Sentence, StringComparison.Ordinal));
        Assert.Contains("\n\n" + Sentence + "\n\n", text);
        // The sentence is the only rendered difference: one paragraph, no extra blank line.
        AssertSameText(plain, text.Replace(Sentence + "\n\n", string.Empty), audience + " without the sentence");

        Assert.Single(AllIndexesOf(RenderWithWithheldRuns(audience, options, 2, "2 of 3 runs"), Plural));
    }

    /// <summary>The fixture document of <paramref name="audience"/> with a <c>quality.withheldRuns</c> fact added.</summary>
    private static string RenderWithWithheldRuns(BenchmarkReportAudience audience, BenchmarkReportRenderOptions options, int count, string display)
    {
        var sheet = Sheet();
        sheet.Facts.Add(new BenchmarkReportFact { Key = "quality.withheldRuns", Value = JsonValue.Create(count), Display = display });
        var document = Document(audience);
        document.FactsJson = BenchmarkReportJson.Serialize(sheet);
        return BenchmarkReportPackRenderer.Render(document, options);
    }

    [Fact]
    public void AboutThisBenchmark_SaysWhenAGraderSharesTheSubjectsCompany()
    {
        var document = Document(BenchmarkReportAudience.ExecutiveSummary);
        string plain = BenchmarkReportPackRenderer.Render(document, new BenchmarkReportRenderOptions());
        Assert.DoesNotContain("same company", plain);

        var sheet = Sheet();
        sheet.Graders[1].SameFamilyAsSubject = true;
        document.FactsJson = BenchmarkReportJson.Serialize(sheet);
        string oneOfTwo = BenchmarkReportPackRenderer.Render(document, new BenchmarkReportRenderOptions());

        Assert.Contains("Two AI graders from two different companies score every answer for accuracy, completeness, conciseness and "
            + "readability, weighted Accuracy 55%, Completeness 25%, Conciseness 10%, Readability 10%. One of them is from the "
            + "same company as the model under test, which may read it more favorably. A separate verifier", oneOfTwo);
    }

    [Fact]
    public void TheCostSentence_FollowsThePricingBasis_AndAnOlderDocumentKeepsItsOwn()
    {
        var document = Document(BenchmarkReportAudience.ExecutiveSummary);
        var options = new BenchmarkReportRenderOptions { PeerNaming = BenchmarkReportPeerNaming.Named };

        string catalog = BenchmarkReportPackRenderer.Render(document, options);
        Assert.Contains(" Cost is the model under test's price per question at the catalog prices of 2026-09-20, which is comparable "
            + "across dates but is not what was actually spent.\n", catalog);
        Assert.Contains("- **Pricing basis:** Catalog prices on 2026-09-20 (price card dated 2026-09-01)\n", catalog);
        Assert.DoesNotContain("- **Price card date:**", catalog);

        var sheet = Sheet();
        sheet.Facts.Single(f => f.Key == "comparison.pricingBasisKind").Display = BenchmarkReportFacts.PricingBasisSnapshot;
        var snapshotDocument = Document(BenchmarkReportAudience.ExecutiveSummary);
        snapshotDocument.FactsJson = BenchmarkReportJson.Serialize(sheet);
        string snapshot = BenchmarkReportPackRenderer.Render(snapshotDocument, options);
        Assert.Contains(" Cost is what the model under test's answers actually cost, at the prices stored with each run.\n", snapshot);

        sheet.Facts.RemoveAll(f => f.Key is "comparison.pricingBasisKind" or "comparison.pricedOn");
        var olderDocument = Document(BenchmarkReportAudience.ExecutiveSummary);
        olderDocument.FactsJson = BenchmarkReportJson.Serialize(sheet);
        string older = BenchmarkReportPackRenderer.Render(olderDocument, options);
        Assert.Contains(" Cost is list price for the model under test, on this basis: Priced from the catalog as of 2026-09-20. "
            + "Comparable across dates; not what was actually spent.\n", older);
        Assert.Contains("- **Pricing basis:** Priced from the catalog as of 2026-09-20.", older);
    }

    [Theory]
    [MemberData(nameof(Combinations))]
    public void AStoredVersion2Document_StillRenders(
        BenchmarkReportAudience audience, BenchmarkReportDisclosure disclosure, BenchmarkReportPeerNaming naming, string file)
    {
        string text = BenchmarkReportPackRenderer.Render(
            StoredV2Document(audience), new BenchmarkReportRenderOptions { Disclosure = disclosure, PeerNaming = naming });

        Assert.Contains("*Document ID 101 · generated under format version 2 · rendered with format version "
            + BenchmarkReportPackRenderer.ReportFormatVersion.ToString(CultureInfo.InvariantCulture) + " ·", text);
        Assert.Contains("80 ± 3 / 100", text);
        Assert.DoesNotContain("(interval ", text);
        Assert.DoesNotContain("{{", text);
        Assert.NotEmpty(file);
    }

    [Fact]
    public void AStoredVersion2Document_KeepsItsOlderWording()
    {
        string text = BenchmarkReportPackRenderer.Render(
            StoredV2Document(BenchmarkReportAudience.ExecutiveSummary),
            new BenchmarkReportRenderOptions { Disclosure = BenchmarkReportDisclosure.Full, PeerNaming = BenchmarkReportPeerNaming.Named });

        Assert.Contains("- **Pricing basis:** Priced from the catalog as of 2026-09-20.", text);
        Assert.Contains("Cost is list price for the model under test, on this basis:", text);
    }

    private static List<int> AllIndexesOf(string text, string value)
    {
        var indexes = new List<int>();
        for (int i = text.IndexOf(value, StringComparison.Ordinal); i >= 0; i = text.IndexOf(value, i + 1, StringComparison.Ordinal))
        {
            indexes.Add(i);
        }
        return indexes;
    }

    [Fact]
    public void TheExecutiveSummary_EndsWithTheEvaluationTerms_BeforeTheFooter()
    {
        string text = Render(BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportDisclosure.Summary, BenchmarkReportPeerNaming.Anonymized);

        int terms = text.IndexOf("## Evaluation terms\n", StringComparison.Ordinal);
        int footer = text.IndexOf("\n---\n", StringComparison.Ordinal);
        Assert.True(terms > 0 && terms < footer, "The evaluation terms do not come right before the footer.");
        Assert.Contains("- **Purpose statement:** " + PurposeStatement + "\n", text);
        Assert.Contains("- **Distillation / training prohibition:** No prompt, completion, or evaluation output in this benchmark is used for model training, fine-tuning, distillation, or developing competing AI models.", text);
        Assert.Contains("- **Third-party model content:** Outputs generated by **GPT-5.6 Luna** (OpenAI), graded by models from Google, Anthropic and DeepSeek, "
            + "and described in this document by **Claude Opus 5.5** (Anthropic) are third-party content", text);
    }

    [Fact]
    public void ADocumentWithoutPurposeStatements_SaysSo()
    {
        var document = Document(BenchmarkReportAudience.ExecutiveSummary);
        var sheet = Sheet();
        sheet.PurposeStatements.Clear();
        document.FactsJson = BenchmarkReportJson.Serialize(sheet);

        string text = BenchmarkReportPackRenderer.Render(document, new BenchmarkReportRenderOptions());

        Assert.Contains("- **Purpose statement:** Not recorded for this document.\n", text);
    }

    [Fact]
    public void TheStandaloneForm_HasNoPeerTablesColumnsOrSignificanceStatement()
    {
        const BenchmarkReportAudience audience = BenchmarkReportAudience.ExecutiveSummary;
        foreach (var disclosure in BenchmarkReportPackRenderer.AllowedDisclosures(audience))
        {
            foreach (var naming in new[] { BenchmarkReportPeerNaming.Named, BenchmarkReportPeerNaming.Anonymized })
            {
                string text = BenchmarkReportPackRenderer.Render(
                    StandaloneDocument(audience), new BenchmarkReportRenderOptions { Disclosure = disclosure, PeerNaming = naming });

                Assert.Contains("- **Peers:** None; this is a stand-alone report\n", text);
                Assert.DoesNotContain("Results against peers", text);
                Assert.DoesNotContain("Peer mean", text);
                Assert.DoesNotContain("peer mean", text);
                Assert.DoesNotContain("Testing every pair", text);
                Assert.DoesNotContain("tested for significance", text);
                Assert.DoesNotContain("- **Compared with:**", text);
                Assert.DoesNotContain("Judge-dependent", text);
                Assert.DoesNotContain("{{", text);
                Assert.Contains("## Evaluation terms", text);
            }
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Provider wording, writer independence, interval span, complete answers and tool use
    // ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(BenchmarkReportPeerNaming.Named)]
    [InlineData(BenchmarkReportPeerNaming.Anonymized)]
    public void AStoredFamilySupportLabel_RendersInProviderWording_AndKeepsItsStrength(BenchmarkReportPeerNaming naming)
    {
        var options = new BenchmarkReportRenderOptions { Disclosure = BenchmarkReportDisclosure.Detailed, PeerNaming = naming };
        string text = BenchmarkReportPackRenderer.Render(StoredV2Document(BenchmarkReportAudience.ExecutiveSummary), options);

        Assert.Contains("- Answers simple questions precisely and briefly. *(raised by one grader)*\n", text);
        Assert.DoesNotContain("family", text);
    }

    [Fact]
    public void AStoredSameFamilySupportLabel_KeepsItsPlainExecutiveWording()
    {
        var document = Document(BenchmarkReportAudience.ExecutiveSummary);
        var sheet = Sheet();
        sheet.Rows.Single(r => r.Id == "R2").SupportLabel = LegacySameFamilyLabel;
        document.FactsJson = BenchmarkReportJson.Serialize(sheet);

        string text = BenchmarkReportPackRenderer.Render(document, new BenchmarkReportRenderOptions { PeerNaming = BenchmarkReportPeerNaming.Named });

        Assert.Contains("- Answers simple questions precisely and briefly. *(raised by one grader, from the model's own company)*\n", text);
    }

    private const string WriterCaveat = "This document was written by Claude Opus 5.5, a model from OpenAI, the provider of the model under test. "
        + "A writer from the same provider may describe it more favorably; the figures and tables were computed by Overseer, not by the writer.";

    [Theory]
    [InlineData("OpenAI")]
    [InlineData(" openai ")]
    public void AWriterFromTheSubjectsProvider_GetsTheIndependenceCaveat(string writerProvider)
    {
        var executive = Document(BenchmarkReportAudience.ExecutiveSummary);
        executive.WriterProvider = writerProvider;
        var options = new BenchmarkReportRenderOptions { Disclosure = BenchmarkReportDisclosure.Full, PeerNaming = BenchmarkReportPeerNaming.Named };

        string executiveText = BenchmarkReportPackRenderer.Render(executive, options);

        Assert.Contains("a span of 6 points, and rests on 4 of 4 questions with a scored answer.\n\n" + WriterCaveat + "\n\n", executiveText);
        int reliable = executiveText.IndexOf("## How reliable this result is", StringComparison.Ordinal);
        int about = executiveText.IndexOf("## About this benchmark", StringComparison.Ordinal);
        int caveat = executiveText.IndexOf(WriterCaveat, StringComparison.Ordinal);
        Assert.True(reliable < caveat && caveat < about);
    }

    [Fact]
    public void AnIndependentWriter_GetsNoIndependenceCaveat()
    {
        string text = BenchmarkReportPackRenderer.Render(Document(BenchmarkReportAudience.ExecutiveSummary), new BenchmarkReportRenderOptions());

        Assert.DoesNotContain("the provider of the model under test", text);
        Assert.DoesNotContain("A writer from the same provider", text);
    }

    [Fact]
    public void TheExecutiveSummary_StatesTheIntervalSpan_OnlyWhenTheSheetHasIt()
    {
        string text = Render(BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportDisclosure.Summary, BenchmarkReportPeerNaming.Named);
        Assert.Contains("## How reliable this result is\n\nThe result rests on 4 questions; its 95% interval overlaps every peer's.\n\n"
            + "The 95% interval is 77–83, a span of 6 points, and rests on 4 of 4 questions with a scored answer.\n\n", text);

        string older = BenchmarkReportPackRenderer.Render(StoredV2Document(BenchmarkReportAudience.ExecutiveSummary), new BenchmarkReportRenderOptions());
        Assert.DoesNotContain("a span of", older);
    }

    // ---------------------------------------------------------------------------------------------
    // Format version 6: support wording, findings table, reference reader, knowledge base, style
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void AnItemCitingNoRow_ReadsFromPerQuestionResults_WhileTheStoredLabelStaysComputed()
    {
        string text = Render(BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportDisclosure.Detailed, BenchmarkReportPeerNaming.Named);

        Assert.Contains("- Scored lowest on intermediate questions (49). *(from per-question results)*\n", text);
        Assert.DoesNotContain("(Computed)", text);
        Assert.DoesNotContain("(computed)", text);
        Assert.Equal("Computed", BenchmarkReportFacts.SupportComputed);
        Assert.Equal("From per-question results", BenchmarkReportPackRenderer.SupportLabelText(BenchmarkReportFacts.SupportComputed));
        Assert.Equal(BenchmarkReportFacts.SupportBothGraders, BenchmarkReportPackRenderer.SupportLabelText(BenchmarkReportFacts.SupportBothGraders));
    }

    [Fact]
    public void AResponseStyleDisplayStoredWithYes_ReadsAsAClause_InProse()
    {
        var document = Document(BenchmarkReportAudience.ExecutiveSummary);
        var sheet = Sheet();
        var style = sheet.Facts.Single(f => f.Key == "style.responseStyleConflict");
        style.Display = "yes: Completeness is the lowest dimension, 13.5 points below Accuracy";
        style.Value = JsonValue.Create(true);
        var writer = Writer();
        writer.Sections[BenchmarkReportSlots.Meaning] = "Because {{style.responseStyleConflict}}, completeness reads low.";
        document.FactsJson = BenchmarkReportJson.Serialize(sheet);
        document.WriterOutputJson = BenchmarkReportJson.Serialize(writer);

        string text = BenchmarkReportPackRenderer.Render(document, new BenchmarkReportRenderOptions());

        Assert.Contains("Because Completeness is the lowest dimension, 13.5 points below Accuracy, completeness reads low.", text);
        Assert.DoesNotContain("yes: ", text);
    }

    [Fact]
    public void TheIntervalSentence_IsPrintedOnce_WithOneNounForTheScoredQuestions()
    {
        string text = Render(BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportDisclosure.Summary, BenchmarkReportPeerNaming.Named);
        Assert.Single(AllIndexesOf(text, "The 95% interval is 77–83"));
        Assert.Contains("rests on 4 of 4 questions with a scored answer.", text);
        Assert.DoesNotContain("questions questions", text);

        // A writer paragraph that already places the interval is not followed by the computed sentence.
        var document = Document(BenchmarkReportAudience.ExecutiveSummary);
        var writer = Writer();
        writer.Sections[BenchmarkReportSlots.Confidence] = "The interval is {{quality.interval}}, a span of {{quality.intervalSpan}}.";
        document.WriterOutputJson = BenchmarkReportJson.Serialize(writer);
        string restated = BenchmarkReportPackRenderer.Render(document, new BenchmarkReportRenderOptions());
        Assert.Contains("The interval is 77–83, a span of 6 points.\n", restated);
        Assert.DoesNotContain("The 95% interval is", restated);
    }

    [Fact]
    public void InThePdf_ARunOfHeadings_ReservesRoomForItself_AndTheStartOfTheNextBlock()
    {
        var blocks = Overseer.Services.Benchmarking.Pdf.BenchmarkPdfMarkdownComposer
            .Parse("## Results against peers\n\n### Quality\n\n| A | B |\n|---|---|\n| 1 | 2 |\n\n## Evaluation terms\n\n- An item.\n\n## Last\n")
            .ToList();
        var pair = blocks.Take(2).Cast<Markdig.Syntax.HeadingBlock>().ToList();
        var terms = (Markdig.Syntax.HeadingBlock)blocks[3];
        var last = (Markdig.Syntax.HeadingBlock)blocks[5];

        float h2 = Overseer.Services.Benchmarking.Pdf.BenchmarkPdfMarkdownComposer.HeadingHeight(2);
        float h3 = Overseer.Services.Benchmarking.Pdf.BenchmarkPdfMarkdownComposer.HeadingHeight(3);
        float spacing = Overseer.Services.Benchmarking.Pdf.BenchmarkPdfStyle.BlockSpacing;

        // Both headings and a table's header and first row stay together.
        Assert.Equal(h2 + spacing + h3 + spacing + Overseer.Services.Benchmarking.Pdf.BenchmarkPdfStyle.KeepWithTableHeight,
            Overseer.Services.Benchmarking.Pdf.BenchmarkPdfMarkdownComposer.KeepWithNextHeight(pair, blocks[2]), 3);
        // A heading before a list keeps three lines of it.
        Assert.Equal(h2 + spacing + Overseer.Services.Benchmarking.Pdf.BenchmarkPdfStyle.KeepWithNextHeight,
            Overseer.Services.Benchmarking.Pdf.BenchmarkPdfMarkdownComposer.KeepWithNextHeight(new[] { terms }, blocks[4]), 3);
        Assert.Equal(h2, Overseer.Services.Benchmarking.Pdf.BenchmarkPdfMarkdownComposer.KeepWithNextHeight(new[] { last }, null), 3);
    }

    // ---------------------------------------------------------------------------------------------
    // Format version 7: how it compares, rank, paired differences, compared models, limitations
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void TheExecutiveSummary_SaysHowTheModelCompares_AfterItsKeyFigures()
    {
        string named = Render(BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportDisclosure.Summary, BenchmarkReportPeerNaming.Named);
        string anonymized = Render(BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportDisclosure.Summary, BenchmarkReportPeerNaming.Anonymized);

        Assert.Contains("## How it compares\n\n"
            + "| Model | Intelligence Index | Median answer time | Cost per question | Critical errors |\n|---|---|---|---|---|\n"
            + "| **GPT-5.6 Luna** | 80 (77–83) | 12.3 s | $0.036 | 1 of 4 answers |\n"
            + "| Grok 5 | 85 (81–89) | 9.8 s | $0.052 | Not available |\n"
            + "| Mistral Large 4 | 78 (74–83) | Not available | $0.021 | Not available |\n\n"
            + "| Dimension | GPT-5.6 Luna | Peer mean | Difference |\n|---|---|---|---|\n"
            + "| Accuracy | 84 | 82 | +2 |\n"
            + "| Completeness | 70 | 78 | -8 |\n"
            + "| Conciseness | 88 | 85 | +3 |\n"
            + "| Readability | 90 | 89 | +1 |\n\n"
            + "GPT-5.6 Luna places 1–3 of 3 (tied: intervals overlap) on intelligence, but its interval overlaps those of Grok 5 and Mistral Large 4, so the order "
            + "is not established. Its paired difference from Grok 5 is -4.6 points.\n\n## What it did well\n", named);
        Assert.Contains("| Model A | 85 (81–89) | 9.8 s | $0.052 | Not available |\n", anonymized);
        Assert.Contains("its interval overlaps those of Model A and Model B, so the order is not established.", anonymized);

        Assert.True(named.IndexOf("## Key figures", StringComparison.Ordinal) < named.IndexOf("## How it compares", StringComparison.Ordinal));
        Assert.Single(AllIndexesOf(named, "## How it compares"));
    }

    [Fact]
    public void HowItCompares_IsLeftOutOfAStandaloneDocument_AndNoDocumentHasARankLine()
    {
        string standalone = BenchmarkReportPackRenderer.Render(StandaloneDocument(BenchmarkReportAudience.ExecutiveSummary), new BenchmarkReportRenderOptions());
        Assert.DoesNotContain("## How it compares", standalone);
        Assert.DoesNotContain("- **Rank:**", standalone);

        Assert.DoesNotContain("- **Rank:**", Render(BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportDisclosure.Full, BenchmarkReportPeerNaming.Named));
    }

    [Fact]
    public void ANamedCopy_NamesThePeersWhoseIntervalsOverlap_AndAnAnonymizedCopyKeepsTheLetters()
    {
        var document = Document(BenchmarkReportAudience.ExecutiveSummary);
        var sheet = Sheet();
        sheet.Facts.Single(f => f.Key == "quality.intervalOverlap").Value = JsonValue.Create(1);
        sheet.Facts.Single(f => f.Key == "quality.intervalOverlap").Display = "its 95% interval overlaps that of Model A";
        var b = sheet.Facts.Single(f => f.Key == "peer.B.intervalOverlap");
        b.Value = JsonValue.Create(false);
        b.Display = "its 95% interval does not overlap the subject's";
        // Model B's interval (70–76) sits below the subject's (77–83), as its overlap fact states.
        var entryB = sheet.Entries.Single(e => e.PeerLetter == "B");
        entryB.QualityIndex = 73.0;
        entryB.QualityLower = 70.0;
        entryB.QualityUpper = 76.0;
        document.FactsJson = BenchmarkReportJson.Serialize(sheet);

        string named = BenchmarkReportPackRenderer.Render(document, new BenchmarkReportRenderOptions { PeerNaming = BenchmarkReportPeerNaming.Named });
        string anonymized = BenchmarkReportPackRenderer.Render(document, new BenchmarkReportRenderOptions { PeerNaming = BenchmarkReportPeerNaming.Anonymized });

        Assert.Contains("- **Intelligence:** 80 / 100 (interval 77–83), 1–2 of 3 (tied: intervals overlap); its 95% interval overlaps that of Grok 5.\n", named);
        // The writer's {{quality.intervalOverlap}} token resolves the same way.
        Assert.Contains("The result rests on 4 questions; its 95% interval overlaps that of Grok 5.\n", named);
        Assert.Contains("- **Intelligence:** 80 / 100 (interval 77–83), 1–2 of 3 (tied: intervals overlap); its 95% interval overlaps that of Model A.\n", anonymized);
        Assert.Contains("The result rests on 4 questions; its 95% interval overlaps that of Model A.\n", anonymized);

        // With no overlap the stored sentence already names no model, and the rank is not joint.
        foreach (var fact in sheet.Facts.Where(f => f.Key is "peer.A.intervalOverlap" or "peer.B.intervalOverlap"))
        {
            fact.Value = JsonValue.Create(false);
        }
        sheet.Facts.Single(f => f.Key == "quality.intervalOverlap").Value = JsonValue.Create(0);
        sheet.Facts.Single(f => f.Key == "quality.intervalOverlap").Display = "its 95% interval overlaps no other model's";
        var entryA = sheet.Entries.Single(e => e.PeerLetter == "A");
        entryA.QualityLower = 84.0;
        entryA.QualityUpper = 89.0;
        document.FactsJson = BenchmarkReportJson.Serialize(sheet);
        Assert.Contains("2nd of 3; its 95% interval overlaps no other model's.\n",
            BenchmarkReportPackRenderer.Render(document, new BenchmarkReportRenderOptions { PeerNaming = BenchmarkReportPeerNaming.Named }));
    }

    [Fact]
    public void ANamedCopy_OfASheetWithoutPerPeerFacts_NamesTheLettersOfTheOverlapSentence()
    {
        string named = BenchmarkReportPackRenderer.Render(
            StoredV6Document(BenchmarkReportAudience.ExecutiveSummary),
            new BenchmarkReportRenderOptions { PeerNaming = BenchmarkReportPeerNaming.Named });
        string anonymized = BenchmarkReportPackRenderer.Render(
            StoredV6Document(BenchmarkReportAudience.ExecutiveSummary),
            new BenchmarkReportRenderOptions { PeerNaming = BenchmarkReportPeerNaming.Anonymized });

        Assert.Contains("; its 95% interval overlaps those of Grok 5 and Mistral Large 4.\n", named);
        Assert.Contains("; its 95% interval overlaps those of Models A and B.\n", anonymized);
    }

    [Theory]
    [InlineData(BenchmarkReportPeerNaming.Named, "with Mistral Large 4", "Asked about Mistral Large 4.")]
    [InlineData(BenchmarkReportPeerNaming.Anonymized, "with Model B", "Asked about Model B.")]
    public void JudgeDependentPairs_NameThePeer_InANamedCopy(BenchmarkReportPeerNaming naming, string pairs, string prose)
    {
        var document = Document(BenchmarkReportAudience.ExecutiveSummary);
        var sheet = Sheet();
        var fact = sheet.Facts.Single(f => f.Key == "panel.judgeDependentPairs");
        fact.Available = true;
        fact.UnavailableReason = null;
        fact.Display = "with Model B";
        fact.Value = JsonValue.Create(1);
        var writer = Writer();
        writer.Sections[BenchmarkReportSlots.Meaning] = "Asked about {{peer:B}}.";
        writer.Sections[BenchmarkReportSlots.Confidence] = "Pairs: {{panel.judgeDependentPairs}}.";
        document.FactsJson = BenchmarkReportJson.Serialize(sheet);
        document.WriterOutputJson = BenchmarkReportJson.Serialize(writer);

        string text = BenchmarkReportPackRenderer.Render(document, new BenchmarkReportRenderOptions { PeerNaming = naming });

        Assert.Contains("Pairs: " + pairs + ".\n", text);
        Assert.Contains(prose, text);
    }

    [Theory]
    [MemberData(nameof(Combinations))]
    public void AStoredVersion6Document_RendersWithoutTheSectionsItHasNoDataFor(
        BenchmarkReportAudience audience, BenchmarkReportDisclosure disclosure, BenchmarkReportPeerNaming naming, string file)
    {
        string text = BenchmarkReportPackRenderer.Render(
            StoredV6Document(audience), new BenchmarkReportRenderOptions { Disclosure = disclosure, PeerNaming = naming });

        Assert.Contains("*Document ID 101 · generated under format version 6 · rendered with format version "
            + BenchmarkReportPackRenderer.ReportFormatVersion.ToString(CultureInfo.InvariantCulture) + " ·", text);
        Assert.DoesNotContain("## How it compares", text);
        Assert.DoesNotContain("- **Rank:**", text);
        Assert.DoesNotContain("Paired difference", text);
        Assert.DoesNotContain("Harness version |", text);
        Assert.DoesNotContain("Run dates (UTC)", text);
        Assert.DoesNotContain("run-to-run variation", text);
        Assert.DoesNotContain("*Not written.*", text);
        Assert.DoesNotContain("{{", text);
        Assert.Contains("peer names, significance claims and spelling; the checks", text);
        Assert.NotEmpty(file);
    }

    // ---------------------------------------------------------------------------------------------
    // Format version 8: significance wording, peer names, rounding, evidence lists, the comparison
    // context, withheld graders and figure markers
    // ---------------------------------------------------------------------------------------------

    private const string NoSignificanceOfThree =
        "No pair of models is tested for significance: with 3 models, testing every pair would flag chance differences.";

    [Fact]
    public void TheSignificanceStatement_IsInTheDocumentsOwnWords_AndPrintedOnce()
    {
        const BenchmarkReportAudience audience = BenchmarkReportAudience.ExecutiveSummary;
        foreach (var disclosure in BenchmarkReportPackRenderer.AllowedDisclosures(audience))
        {
            foreach (var naming in new[] { BenchmarkReportPeerNaming.Named, BenchmarkReportPeerNaming.Anonymized })
            {
                string text = Render(audience, disclosure, naming);

                Assert.Single(AllIndexesOf(text, NoSignificanceOfThree + "\n"));
                Assert.DoesNotContain(Sheet().NoSignificanceSummary, text);
                Assert.DoesNotContain(Sheet().NoSignificanceInstead, text);
                Assert.DoesNotContain("Multi-Run Analysis", text);
                Assert.DoesNotContain("this view", text);
                Assert.DoesNotContain("- Significance:", text);
            }
        }

        string named = Render(audience, BenchmarkReportDisclosure.Summary, BenchmarkReportPeerNaming.Named);
        int statement = named.IndexOf(NoSignificanceOfThree, StringComparison.Ordinal);
        Assert.True(named.IndexOf("## How reliable this result is", StringComparison.Ordinal) < statement
                    && statement < named.IndexOf("## About this benchmark", StringComparison.Ordinal));
    }

    [Fact]
    public void TheSignificanceStatement_SpeaksOfTwoModels_WithOnePeer_AndIsLeftOut_WhenTheComparisonStatesNone()
    {
        var document = Document(BenchmarkReportAudience.ExecutiveSummary);
        var sheet = Sheet();
        sheet.Peers.RemoveAll(p => p.Letter == "B");
        document.FactsJson = BenchmarkReportJson.Serialize(sheet);

        string two = BenchmarkReportPackRenderer.Render(document, new BenchmarkReportRenderOptions());
        Assert.Contains("\n\nThe two models are not tested for significance, so a gap between them may be noise.\n\n", two);
        Assert.DoesNotContain("No pair of models", two);

        sheet = Sheet();
        sheet.NoSignificanceSummary = string.Empty;
        document.FactsJson = BenchmarkReportJson.Serialize(sheet);
        Assert.DoesNotContain("tested for significance", BenchmarkReportPackRenderer.Render(document, new BenchmarkReportRenderOptions()));
    }

    [Fact]
    public void HowItCompares_PrintsEachModelsOwnCriticalErrors_WhereTheSheetHasThem()
    {
        var document = Document(BenchmarkReportAudience.ExecutiveSummary);
        var sheet = Sheet();
        // A count that says what it counts prints its head alone under the column; a bare one from an older sheet as it is.
        sheet.Entries.Single(e => e.IsSubject).Extra.Add(new BenchmarkReportFact { Key = "errors.critical", Display = "1 of 4 answers with a critical error", Value = JsonValue.Create(1) });
        sheet.Entries.Single(e => e.PeerLetter == "A").Extra.Add(new BenchmarkReportFact { Key = "errors.critical", Display = "0 of 4 answers", Value = JsonValue.Create(0) });
        sheet.Entries.Single(e => e.PeerLetter == "B").Extra.Add(new BenchmarkReportFact
        {
            Key = "errors.critical", Display = BenchmarkReportFacts.NotAvailable, Available = false, UnavailableReason = "No scored answer."
        });
        document.FactsJson = BenchmarkReportJson.Serialize(sheet);

        string text = BenchmarkReportPackRenderer.Render(document, new BenchmarkReportRenderOptions { PeerNaming = BenchmarkReportPeerNaming.Named });

        Assert.Contains("| **GPT-5.6 Luna** | 80 (77–83) | 12.3 s | $0.036 | 1 of 4 answers |\n"
            + "| Grok 5 | 85 (81–89) | 9.8 s | $0.052 | 0 of 4 answers |\n"
            + "| Mistral Large 4 | 78 (74–83) | Not available | $0.021 | Not available |\n", text);
    }

    [Fact]
    public void AnAnonymizedCopy_WithholdsAGradersIdentity_WhereAPeerSharesItsProvider()
    {
        var document = Document(BenchmarkReportAudience.ExecutiveSummary);
        var sheet = Sheet();
        sheet.Graders[1].Provider = "xAI";
        document.FactsJson = BenchmarkReportJson.Serialize(sheet);

        string anonymized = BenchmarkReportPackRenderer.Render(document,
            new BenchmarkReportRenderOptions { Disclosure = BenchmarkReportDisclosure.Full, PeerNaming = BenchmarkReportPeerNaming.Anonymized });
        string named = BenchmarkReportPackRenderer.Render(document,
            new BenchmarkReportRenderOptions { Disclosure = BenchmarkReportDisclosure.Full, PeerNaming = BenchmarkReportPeerNaming.Named });

        // The evaluation terms name the graders' providers.
        Assert.Contains("graded by models from Google, a withheld provider and DeepSeek,", anonymized);
        Assert.DoesNotContain("xAI", anonymized);
        Assert.DoesNotContain("Claude Haiku 5", anonymized);
        Assert.DoesNotContain("claude-haiku-5", anonymized);

        Assert.Contains("graded by models from Google, xAI and DeepSeek,", named);
        Assert.DoesNotContain("withheld provider", named);
    }

    [Fact]
    public void TheFrontMatter_NamesTheComparison_AndItsPricingBasis()
    {
        string named = Render(BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportDisclosure.Summary, BenchmarkReportPeerNaming.Named);
        string anonymized = Render(BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportDisclosure.Summary, BenchmarkReportPeerNaming.Anonymized);
        const string pricing = "- **Pricing basis:** Catalog prices on 2026-09-20 (price card dated 2026-09-01)\n\n";

        Assert.Contains("- **Runs:** 1 (run 12)\n"
            + "- **Compared with:** Model A = Grok 5 (xAI, grok-5); Model B = Mistral Large 4 (Mistral, mistral-large-4)\n" + pricing, named);
        Assert.Contains("- **Compared with:** Models A and B, identities withheld\n" + pricing, anonymized);
        Assert.DoesNotContain("- **Peers:**", named);

        string standalone = BenchmarkReportPackRenderer.Render(StandaloneDocument(BenchmarkReportAudience.ExecutiveSummary), new BenchmarkReportRenderOptions());
        Assert.Contains("- **Peers:** None; this is a stand-alone report\n\n", standalone);
        Assert.DoesNotContain("Compared with", standalone);
        Assert.DoesNotContain("- **Pricing basis:**", standalone);
    }

    // --- Figure markers -----------------------------------------------------------------------------

    private static IReadOnlyList<BenchmarkReportRenderChart> Charts(params string[] keys)
        => keys.Select(key => new BenchmarkReportRenderChart
        {
            FigureKey = key,
            Title = "Chart " + key,
            Caption = "Caption of " + key,
            AltText = "Alt text of " + key,
            Png = new byte[] { 0x89, 0x50, 0x4E, 0x47 },
            WidthPx = 1600,
            HeightPx = 900,
            Sha256 = new string('0', 64)
        }).ToList();

    private static string RenderWithCharts(
        BenchmarkReportDocument document, BenchmarkReportDisclosure disclosure, BenchmarkReportPeerNaming naming,
        IReadOnlyList<BenchmarkReportRenderChart> charts)
        => BenchmarkReportPackRenderer.Render(document,
            new BenchmarkReportRenderOptions { Disclosure = disclosure, PeerNaming = naming, Charts = charts });

    private static string MarkerOf(string key) => "[[figure:" + key + "]]";

    /// <summary>Every index found, each after the one before it.</summary>
    private static void AssertInOrder(params int[] indexes)
    {
        for (int i = 0; i < indexes.Length; i++)
        {
            Assert.True(indexes[i] >= 0, "Part " + i.ToString(CultureInfo.InvariantCulture) + " is missing.");
            if (i > 0)
            {
                Assert.True(indexes[i - 1] < indexes[i], "Part " + i.ToString(CultureInfo.InvariantCulture) + " is out of order.");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Combinations))]
    public void WithCharts_EachFigureMarkerStandsAtItsAnchor_OnceAndInPlacementOrder(
        BenchmarkReportAudience audience, BenchmarkReportDisclosure disclosure, BenchmarkReportPeerNaming naming, string file)
    {
        // Given in reverse order, placed in the placement order.
        string text = RenderWithCharts(Document(audience), disclosure, naming,
            Charts(BenchmarkReportChartPlacement.FigureKeys.Reverse().ToArray()));

        foreach (string key in BenchmarkReportChartPlacement.FigureKeys)
        {
            Assert.Single(AllIndexesOf(text, MarkerOf(key)));
            Assert.Contains("\n\n" + MarkerOf(key) + "\n\n", text);
        }

        int Pos(string part) => text.IndexOf(part, StringComparison.Ordinal);
        int At(string key) => Pos(MarkerOf(key));
        var keys = BenchmarkReportChartPlacement.FigureKeys;

        // Provider availability (keys[7]) follows the Key figures; the other seven stand in How it compares.
        Assert.Equal(BenchmarkReportAudience.ExecutiveSummary, audience);
        AssertInOrder(
            Pos("## Key figures"), Pos("- **Critical errors:**"), At(keys[7]),
            Pos("## How it compares"), Pos("| Readability | 90 | 89 | +1 |\n"),
            At(keys[0]), At(keys[1]), At(keys[2]), At(keys[3]), At(keys[4]), At(keys[5]), At(keys[6]),
            Pos("GPT-5.6 Luna places 1–3 of 3 (tied: intervals overlap)"), Pos("## What it did well"));
        Assert.NotEmpty(file);
    }

    [Fact]
    public void WithoutCharts_AsTheMarkdownDownloadRendersIt_NoFigureMarkerIsWritten()
    {
        // The golden files pin the default (chart-less) render of every combination.
        Assert.Empty(new BenchmarkReportRenderOptions().Charts);
        Assert.DoesNotContain("[[figure:", RenderWithCharts(Document(BenchmarkReportAudience.ExecutiveSummary), BenchmarkReportDisclosure.Full,
            BenchmarkReportPeerNaming.Named, Array.Empty<BenchmarkReportRenderChart>()));
    }

    [Fact]
    public void OnlyTheChartsGiven_GetMarkers_AndAnUnknownKeyIsIgnored()
    {
        string text = RenderWithCharts(Document(BenchmarkReportAudience.ExecutiveSummary), BenchmarkReportDisclosure.Summary,
            BenchmarkReportPeerNaming.Named, Charts("p1b-speed", "no-such-figure", "p1b-speed"));

        Assert.Single(AllIndexesOf(text, "[[figure:"));
        Assert.Contains("| Readability | 90 | 89 | +1 |\n\n" + MarkerOf("p1b-speed") + "\n\nGPT-5.6 Luna places", text);
    }

    [Fact]
    public void AStandaloneDocument_GetsNoFigureMarker_EvenWithCharts()
    {
        string text = RenderWithCharts(StandaloneDocument(BenchmarkReportAudience.ExecutiveSummary), BenchmarkReportDisclosure.Full, BenchmarkReportPeerNaming.Named,
            Charts(BenchmarkReportChartPlacement.FigureKeys.ToArray()));

        Assert.DoesNotContain("[[figure:", text);
    }

    [Fact]
    public void ThePlacement_KnowsTheEightFigures_AndWhereEachAudienceDrawsThem()
    {
        Assert.Equal(
            new[] { "p1a-quality", "p1b-speed", "p1c-cost", "p2-profile", "s1-quality-speed", "s2-quality-cost", "s3-speed-cost", "a1-availability" },
            BenchmarkReportChartPlacement.FigureKeys);
        Assert.All(BenchmarkReportChartPlacement.FigureKeys, key => Assert.True(BenchmarkReportChartPlacement.IsKnown(key)));
        Assert.False(BenchmarkReportChartPlacement.IsKnown(null));
        Assert.False(BenchmarkReportChartPlacement.IsKnown(string.Empty));
        Assert.False(BenchmarkReportChartPlacement.IsKnown("P1A-QUALITY"));
        Assert.False(BenchmarkReportChartPlacement.IsKnown("p9-other"));

        var executive = BenchmarkReportAudience.ExecutiveSummary;
        var sevenFigures = BenchmarkReportChartPlacement.FigureKeys.Take(7).ToList();
        Assert.Equal(sevenFigures, BenchmarkReportChartPlacement.KeysAt(executive, BenchmarkReportChartAnchor.HowItCompares));

        // The model-scope Executive Summary draws provider availability after its Key figures.
        Assert.Equal(new[] { "a1-availability" }, BenchmarkReportChartPlacement.KeysAt(executive, BenchmarkReportChartAnchor.ProviderAvailability));
        Assert.Equal(BenchmarkReportChartAnchor.ProviderAvailability, BenchmarkReportChartPlacement.AnchorOf(executive, "a1-availability"));

        // The comparison-scope Executive Summary draws the seven at How they compare, and no availability figure.
        Assert.Equal(sevenFigures,
            BenchmarkReportChartPlacement.KeysAt(executive, BenchmarkReportChartAnchor.HowTheyCompare, BenchmarkReportScope.Comparison));
        Assert.Null(BenchmarkReportChartPlacement.AnchorOf(executive, "a1-availability", BenchmarkReportScope.Comparison));
        Assert.Empty(BenchmarkReportChartPlacement.KeysAt(executive, BenchmarkReportChartAnchor.HowTheyCompare));
        Assert.Empty(BenchmarkReportChartPlacement.KeysAt(executive, BenchmarkReportChartAnchor.HowItCompares, BenchmarkReportScope.Comparison));

        // An audience without a model-scope placement draws nothing.
        Assert.Empty(BenchmarkReportChartPlacement.KeysAt(BenchmarkReportAudience.ProviderIssueReport, BenchmarkReportChartAnchor.HowItCompares));
        Assert.Null(BenchmarkReportChartPlacement.AnchorOf(BenchmarkReportAudience.ProviderIssueReport, "p1a-quality"));

        Assert.Equal(BenchmarkReportChartAnchor.HowItCompares, BenchmarkReportChartPlacement.AnchorOf(executive, "s2-quality-cost"));
        Assert.Null(BenchmarkReportChartPlacement.AnchorOf(executive, "p9-other"));

        Assert.Equal("[[figure:p2-profile]]", BenchmarkReportChartPlacement.Marker("p2-profile"));
        Assert.True(BenchmarkReportChartPlacement.IsKnown("p2-profile"));
        Assert.False(BenchmarkReportChartPlacement.IsKnown("p9-other"));
        Assert.False(BenchmarkReportChartPlacement.IsKnown(null));
    }

    // ---------------------------------------------------------------------------------------------
    // Format version 9: assessed and authored bands, question details last, mean answer time,
    // support labels by the questions an item cites
    // ---------------------------------------------------------------------------------------------

    /// <summary><paramref name="sheet"/> with the facts and fields format version 9 adds.</summary>
    private static BenchmarkReportFactSheet WithVersion9Facts(BenchmarkReportFactSheet sheet)
    {
        sheet.Facts.Add(new BenchmarkReportFact { Key = "answers.scored", Display = "4", Value = JsonValue.Create(4) });
        sheet.Facts.Add(new BenchmarkReportFact { Key = "bands.authored.advanced", Display = "1", Value = JsonValue.Create(1) });
        sheet.Facts.Add(new BenchmarkReportFact { Key = "bands.authored.intermediate", Display = "1", Value = JsonValue.Create(1) });
        sheet.Facts.Add(new BenchmarkReportFact { Key = "bands.authored.simple", Display = "2", Value = JsonValue.Create(2) });
        sheet.Facts.Add(new BenchmarkReportFact { Key = "speed.modelTimeMean", Display = "14.0 s", Value = JsonValue.Create(14000.0) });
        sheet.Facts = sheet.Facts.OrderBy(f => f.Key, StringComparer.Ordinal).ToList();

        string[] authored = { "Simple", "Simple", "Intermediate", "Advanced" };
        foreach (var q in sheet.Questions)
        {
            q.AuthoredBand = authored[q.Number - 1];
        }
        return sheet;
    }

    private static BenchmarkReportDocument Version9Document(BenchmarkReportAudience audience)
    {
        var document = Document(audience);
        document.FactsJson = BenchmarkReportJson.Serialize(WithVersion9Facts(Sheet()));
        return document;
    }

    [Fact]
    public void KeyFigures_StateTheMeanAnswerTime()
    {
        string executive = BenchmarkReportPackRenderer.Render(Version9Document(BenchmarkReportAudience.ExecutiveSummary),
            new BenchmarkReportRenderOptions { PeerNaming = BenchmarkReportPeerNaming.Named });
        Assert.Contains("- **Speed:** Median answer time 12.3 s, mean 14.0 s, 2nd of 2.\n", executive);
    }

    [Fact]
    public void ASheetWithoutTheFormatVersion9Facts_StillRenders_LeavingThemOut()
    {
        // The fixture's sheet carries none of them, as a document stored under format version 8 does.
        var document = Document(BenchmarkReportAudience.ExecutiveSummary);
        document.ReportFormatVersion = 8;

        string text = BenchmarkReportPackRenderer.Render(document,
            new BenchmarkReportRenderOptions { Disclosure = BenchmarkReportDisclosure.Summary, PeerNaming = BenchmarkReportPeerNaming.Named });

        Assert.Contains("- **Speed:** Median answer time 12.3 s, 2nd of 2.\n", text);
        Assert.DoesNotContain("Mean answer time", text);
        Assert.DoesNotContain("{{", text);
    }

    [Fact]
    public void AnItemCitingAConvergentRow_IsBothGraders_OnlyOnTheRowsSharedQuestions()
    {
        var document = Document(BenchmarkReportAudience.ExecutiveSummary);
        var sheet = Sheet();
        var row = sheet.Rows.Single(r => r.Id == "R1");
        row.Questions = new List<int> { 3, 4 };
        row.QuestionsA = new List<int> { 3, 4 };
        row.QuestionsB = new List<int> { 3 };
        row.SharedQuestions = new List<int> { 3 };
        document.FactsJson = BenchmarkReportJson.Serialize(sheet);
        var writer = Writer();
        writer.Weaknesses.Add(new BenchmarkReportWriterItem { Text = "Overstated the wand's charges.", Questions = new List<int> { 4 }, Evidence = new List<string> { "R1" } });
        writer.Weaknesses.Add(new BenchmarkReportWriterItem { Text = "Made destruction claims the graders flagged.", Evidence = new List<string> { "R1" } });
        document.WriterOutputJson = BenchmarkReportJson.Serialize(writer);

        string text = BenchmarkReportPackRenderer.Render(document,
            new BenchmarkReportRenderOptions { Disclosure = BenchmarkReportDisclosure.Summary, PeerNaming = BenchmarkReportPeerNaming.Named });

        // Q3 is shared: both graders, which the Executive Summary leaves unlabeled.
        Assert.Contains("- Asserted a false outcome on Q3, where Grok 5 scored well.\n", text);
        // Only member A named Q4: one grader, of another provider than the model.
        Assert.Contains("- Overstated the wand's charges. *(raised by one grader)*\n", text);
        // Citing no question, the item is about the row's questions, which include the shared one.
        Assert.Contains("- Made destruction claims the graders flagged.\n", text);

        string anonymized = BenchmarkReportPackRenderer.Render(document, new BenchmarkReportRenderOptions());
        Assert.Contains("- Overstated the wand's charges. *(raised by one grader)*\n", anonymized);
        Assert.Contains("- Asserted a false outcome on Q3, where Model A scored well.\n", anonymized);
    }

    // ---------------------------------------------------------------------------------------------
    // Comparison scope
    // ---------------------------------------------------------------------------------------------

    public static IEnumerable<object[]> ComparisonGoldenCombinations() => ComparisonCombinations();

    private static string RenderComparison(
        BenchmarkReportAudience audience, BenchmarkReportDisclosure disclosure, BenchmarkReportPeerNaming naming, bool subset = false)
        => BenchmarkReportPackRenderer.Render(ComparisonDocument(audience, subset),
            new BenchmarkReportRenderOptions { Disclosure = disclosure, PeerNaming = naming });

    [Theory]
    [MemberData(nameof(ComparisonGoldenCombinations))]
    public void EveryComparisonCombination_Renders_AndMatchesItsGoldenFile(
        BenchmarkReportAudience audience, BenchmarkReportDisclosure disclosure, BenchmarkReportPeerNaming naming, string file)
    {
        AssertMatchesGolden(file, RenderComparison(audience, disclosure, naming));
    }

    [Theory]
    [InlineData(BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportDisclosure.Full, BenchmarkReportPeerNaming.Named, "comparison_subset_exec_full_named.md")]
    [InlineData(BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportDisclosure.Summary, BenchmarkReportPeerNaming.Anonymized, "comparison_subset_exec_summary_anonymized.md")]
    public void TheSubsetComparison_MatchesItsGoldenFile(
        BenchmarkReportAudience audience, BenchmarkReportDisclosure disclosure, BenchmarkReportPeerNaming naming, string file)
    {
        AssertMatchesGolden(file, RenderComparison(audience, disclosure, naming, subset: true));
    }

    [Fact]
    public void ComparisonTitles_NameTheComparison_AndAnAnonymizedCopyNeverPrintsItsName()
    {
        // The kicker names the document type; the H1 is the comparison's name alone.
        Assert.StartsWith("**EXECUTIVE SUMMARY**\n\n# Comparison #12 — Spring model sweep\n",
            RenderComparison(BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportDisclosure.Full, BenchmarkReportPeerNaming.Named));
        Assert.StartsWith("**EXECUTIVE SUMMARY**\n\n# Comparison #12\n",
            RenderComparison(BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportDisclosure.Summary, BenchmarkReportPeerNaming.Anonymized));
        Assert.StartsWith("**EXECUTIVE SUMMARY**\n\n# Comparison #12 — Orion Max vs Zeta Prime\n",
            RenderComparison(BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportDisclosure.Full, BenchmarkReportPeerNaming.Named, subset: true));
        Assert.StartsWith("**EXECUTIVE SUMMARY**\n\n# Comparison #12 — 2 of 5 models\n",
            RenderComparison(BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportDisclosure.Full, BenchmarkReportPeerNaming.Anonymized, subset: true));

        string anonymized = RenderComparison(BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportDisclosure.Full, BenchmarkReportPeerNaming.Anonymized);
        Assert.DoesNotContain(ComparisonName, anonymized);
    }

    [Fact]
    public void ASubsetDocument_NamesNoOtherModel_AndCountsThem()
    {
        foreach (var combination in ComparisonCombinations())
        {
            string text = RenderComparison((BenchmarkReportAudience)combination[0], (BenchmarkReportDisclosure)combination[1],
                (BenchmarkReportPeerNaming)combination[2], subset: true);
            foreach (var other in ComparisonModels.Where(m => m.RunId is 32 or 33 or 34))
            {
                Assert.DoesNotContain(other.Label, text, StringComparison.Ordinal);
                Assert.DoesNotContain(ModelIdOf(other.Label), text, StringComparison.Ordinal);
                Assert.DoesNotContain(other.Provider, text, StringComparison.Ordinal);
            }
        }

        string executive = RenderComparison(BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportDisclosure.Full, BenchmarkReportPeerNaming.Named, subset: true);
        Assert.Contains("- **Coverage:** 2 of 5 models of Comparison #12; the other 3 are not part of this document.\n", executive);
    }

    [Fact]
    public void ANamedComparisonCopy_NamesTheModels_AndAnAnonymizedCopyNamesNone()
    {
        string named = RenderComparison(BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportDisclosure.Full, BenchmarkReportPeerNaming.Named);
        Assert.DoesNotContain("Model A", named, StringComparison.Ordinal);
        Assert.DoesNotContain("Models A", named, StringComparison.Ordinal);
        Assert.Contains("Orion Max", named, StringComparison.Ordinal);
        Assert.Contains("| Orion Max (reference) | Northwind | 85 (82–88) | 1–2 (tied) |", named);

        foreach (var combination in ComparisonCombinations().Where(c => (BenchmarkReportPeerNaming)c[2] == BenchmarkReportPeerNaming.Anonymized))
        {
            string anonymized = RenderComparison((BenchmarkReportAudience)combination[0], (BenchmarkReportDisclosure)combination[1], BenchmarkReportPeerNaming.Anonymized);
            Assert.All(ComparisonModels, m => Assert.DoesNotContain(m.Label, anonymized, StringComparison.Ordinal));
            Assert.All(ComparisonModels, m => Assert.DoesNotContain(m.Provider, anonymized, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void ComparisonCharts_AreMarkedAtTheComparisonAnchors()
    {
        var all = Charts(BenchmarkReportChartPlacement.FigureKeys.ToArray());

        string executive = RenderWithCharts(ComparisonDocument(BenchmarkReportAudience.ExecutiveSummary), BenchmarkReportDisclosure.Full, BenchmarkReportPeerNaming.Named, all);
        AssertInOrder(
            executive.IndexOf("## How they compare\n", StringComparison.Ordinal),
            executive.IndexOf(MarkerOf(BenchmarkReportChartPlacement.QualityKey), StringComparison.Ordinal),
            executive.IndexOf(MarkerOf(BenchmarkReportChartPlacement.SpeedCostKey), StringComparison.Ordinal),
            executive.IndexOf("## Model by model\n", StringComparison.Ordinal));
        Assert.DoesNotContain(MarkerOf(BenchmarkReportChartPlacement.AvailabilityKey), executive);

        Assert.Equal(BenchmarkReportChartAnchor.HowTheyCompare,
            BenchmarkReportChartPlacement.AnchorOf(BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportChartPlacement.CostKey, BenchmarkReportScope.Comparison));
        Assert.Equal(BenchmarkReportChartAnchor.HowItCompares,
            BenchmarkReportChartPlacement.AnchorOf(BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportChartPlacement.CostKey));
    }

    /// <summary>A comparison document over <paramref name="keys"/> of the fixture comparison.</summary>
    private static BenchmarkReportDocument ComparisonDocumentOver(BenchmarkReportAudience audience, IReadOnlyList<string> keys)
    {
        var built = ComparisonFacts(keys);
        var document = ComparisonDocument(audience, subset: true);
        document.CoveredEntryKeysJson = BenchmarkReportJson.Serialize(built.CoveredEntryKeys.ToList());
        document.CoveredSetKey = built.CoveredSetKey;
        document.FactsJson = BenchmarkReportJson.Serialize(built.Sheet!);
        document.ContentJson = BenchmarkReportJson.Serialize(built.Content);
        document.WriterOutputJson = BenchmarkReportJson.Serialize(ComparisonWriter(audience, built.Sheet!));
        return document;
    }

    private static string RenderDocument(BenchmarkReportDocument document, BenchmarkReportDisclosure disclosure, BenchmarkReportPeerNaming naming)
        => BenchmarkReportPackRenderer.Render(document, new BenchmarkReportRenderOptions { Disclosure = disclosure, PeerNaming = naming });

    [Fact]
    public void ATwoModelComparison_SaysWhetherTheTwoIntervalsOverlap_AndWhetherTheComparisonEstablishesTheOrder()
    {
        // Orion Max (82 to 88) and Vega Pro (77 to 83) overlap; Orion Max and Zeta Prime (50 to 60) do not.
        string overlapping = RenderDocument(ComparisonDocumentOver(BenchmarkReportAudience.ExecutiveSummary, new[] { "run:31", "run:32" }),
            BenchmarkReportDisclosure.Full, BenchmarkReportPeerNaming.Named);
        Assert.Contains("The two models' 95% intervals overlap; the intervals alone do not establish which scored higher.\n", overlapping);
        Assert.DoesNotContain("Of the 1 pair", overlapping);

        var document = ComparisonDocument(BenchmarkReportAudience.ExecutiveSummary, subset: true);
        string apart = RenderDocument(document, BenchmarkReportDisclosure.Full, BenchmarkReportPeerNaming.Named);
        Assert.Contains("The two models' 95% intervals do not overlap.\n", apart);
        Assert.DoesNotContain("Of the 1 pair", apart);

        var sheet = BenchmarkReportJson.Deserialize<BenchmarkReportFactSheet>(document.FactsJson);
        var measures = (sheet.PairedTests ?? new List<BenchmarkReportPairedFamily>())
            .Select(f => f.Measures.FirstOrDefault(m => m.Measure == BenchmarkPairedTests.IntelligenceMeasure))
            .Where(m => m != null)
            .ToList();
        Assert.NotEmpty(measures);
        foreach (var measure in measures)
        {
            var pair = Assert.Single(measure!.Pairs);
            Assert.Contains(pair.Established && pair.Favors != "None"
                ? "; the comparison establishes which scored higher.\n"
                : "; the comparison does not establish which scored higher.\n", apart);
        }
        Assert.DoesNotContain(" of 1 comparison", apart);

        string five = RenderComparison(BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportDisclosure.Full, BenchmarkReportPeerNaming.Named);
        Assert.Contains("Of the 10 pairs of models, ", five);
        Assert.Contains(" comparisons establish which scored higher.\n", five);
        Assert.DoesNotContain("The two models' 95% intervals", five);
    }

    [Fact]
    public void AComparisonDocument_IsCurrentAtFormatVersion30()
    {
        string text = RenderComparison(BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportDisclosure.Full, BenchmarkReportPeerNaming.Named);

        Assert.Equal(30, BenchmarkReportPackRenderer.ComparisonReportFormatVersion);
        Assert.Contains(" · format version 30 · ", text);
        Assert.Contains(" · models named*", text);
    }

    // ---------------------------------------------------------------------------------------------
    // Root Cause Analysis: comparison format 21, model-scope format 16
    // ---------------------------------------------------------------------------------------------

    private static readonly BenchmarkReportRenderOptions RcaNamed = new() { Disclosure = BenchmarkReportDisclosure.Full, PeerNaming = BenchmarkReportPeerNaming.Named };
    private static readonly BenchmarkReportRenderOptions RcaAnonymized = new() { Disclosure = BenchmarkReportDisclosure.Full, PeerNaming = BenchmarkReportPeerNaming.Anonymized };

    /// <summary>The fixture's comparison-scope Root Cause Analysis, its sheet, content and writer output changed as given.</summary>
    private static BenchmarkReportDocument RcaDocument(
        Action<BenchmarkReportFactSheet>? sheet = null, Action<BenchmarkReportContentSnapshot>? content = null, Action<BenchmarkReportWriterOutput>? writer = null)
    {
        var document = RootCauseReportFixture.ComparisonDocument();
        if (sheet != null)
        {
            var s = BenchmarkReportJson.Deserialize<BenchmarkReportFactSheet>(document.FactsJson);
            sheet(s);
            document.FactsJson = BenchmarkReportJson.Serialize(s);
        }
        if (content != null)
        {
            var c = BenchmarkReportJson.Deserialize<BenchmarkReportContentSnapshot>(document.ContentJson);
            content(c);
            document.ContentJson = BenchmarkReportJson.Serialize(c);
        }
        if (writer != null)
        {
            var w = BenchmarkReportJson.Deserialize<BenchmarkReportWriterOutput>(document.WriterOutputJson);
            writer(w);
            document.WriterOutputJson = BenchmarkReportJson.Serialize(w);
        }
        return document;
    }

    /// <summary>The text from <paramref name="heading"/> to the next heading of its level or a higher one.</summary>
    private static string RcaSection(string text, string heading)
    {
        int start = text.IndexOf(heading + "\n", StringComparison.Ordinal);
        Assert.True(start >= 0, heading + " is missing");
        int level = heading.TakeWhile(c => c == '#').Count();
        int end = Enumerable.Range(1, level)
            .Select(l => text.IndexOf("\n" + new string('#', l) + " ", start + heading.Length, StringComparison.Ordinal))
            .Where(i => i >= 0)
            .DefaultIfEmpty(text.Length - 1)
            .Min();
        return text[start..(end + 1)];
    }

    [Fact]
    public void TheModelSelectionWaitsLine_IsPrintedOnlyWhenARunWaits()
    {
        const string waits = "A harness or suite hypothesis is graded Moderate or higher, so the model-selection runs wait until the system hypotheses are settled.";

        var none = RcaDocument(s =>
        {
            s.RootCause!.SystemFirst = true;
            foreach (var run in s.RootCause.RecommendedRuns) run.AfterSystemHypotheses = false;
        });
        Assert.DoesNotContain(waits, BenchmarkReportPackRenderer.Render(none, RcaNamed));

        var one = RcaDocument(s =>
        {
            s.RootCause!.SystemFirst = true;
            var last = s.RootCause.RecommendedRuns.OrderBy(r => r.Number).Last();
            last.Purpose = RootCausePurposes.ModelSelection;
            last.AfterSystemHypotheses = true;
        });
        Assert.Contains(waits, BenchmarkReportPackRenderer.Render(one, RcaNamed));
    }

    [Fact]
    public void ARootCauseAnalysisTable_LeavesOutAColumnThatIsZeroOrNotRecordedInEveryRow_AndSaysSo()
    {
        var document = RcaDocument(sheet =>
        {
            foreach (var row in sheet.RootCause!.SpeedRows.Where(r => r.Total.HasValue))
            {
                row.Segments = RootCauseSegments.Time
                    .Select((k, i) => new RootCauseSegment
                    {
                        Key = k,
                        Label = RootCauseSegments.Label(k),
                        Value = k is RootCauseSegments.OwnWaits or RootCauseSegments.Other ? 0 : 1.0 + i
                    })
                    .ToList();
                row.Total = row.Segments.Sum(s => s.Value);
            }
        });

        string speed = RcaSection(BenchmarkReportPackRenderer.Render(document, RcaNamed), "### Where the time goes");

        Assert.Contains("| Model | Wall time per question (median, tools included) | Wait before output | Output generation | Tool execution |\n", speed);
        Assert.DoesNotContain("| Own waits and retries |", speed);
        Assert.Contains("Left out because they are zero or not recorded in every row: own waits and retries and other.", speed);
        Assert.Contains("It differs from the median model time the other tables of this document give, which leaves tool execution out.", speed);
    }

    [Fact]
    public void AppendixG_GivesEachAnswerAsItsExcerpt_TheRubricWithoutItsSourceParagraphs_AndTheCriticalErrorResolution()
    {
        const string Rubric = "- The first rubric point.\n\nSOURCE: src/monst.c lines 10 to 20\nThe cited source text.\n\n- The second rubric point.";
        var document = RcaDocument(content: content =>
        {
            foreach (var question in content.Questions!)
            {
                question.ExpectedPoints = Rubric;
                question.ExpectedPointsRecorded = true;
            }
            foreach (var item in content.Runs.SelectMany(r => r.Questions))
            {
                item.AnswerExcerpt = "The excerpt of the answer.";
                item.AnswerExcerptCut = true;
                item.AnswerText = "The complete answer, which the analysis never prints.";
                item.CriticalErrorResolution = "OverturnedByVerifier";
            }
        });

        string appendix = RcaSection(BenchmarkReportPackRenderer.Render(document, RcaNamed), "## Appendix G: Question details");

        Assert.Contains("> - The first rubric point.\n", appendix);
        Assert.Contains("> - The second rubric point.\n", appendix);
        Assert.DoesNotContain("SOURCE", appendix);
        Assert.DoesNotContain("The cited source text.", appendix);
        Assert.Contains("**Answer excerpt:**\n\n> The excerpt of the answer.\n", appendix);
        Assert.DoesNotContain("The complete answer", appendix);
        Assert.Contains("**Critical error:** Overturned by the claim verifier (cap lifted).\n", appendix);
        Assert.Contains("Rubrics are shown without their source paragraphs.", appendix);
        Assert.DoesNotContain("No answer to this question was given as an excerpt.", appendix);
    }

    [Fact]
    public void AnObservation_StatesHowEachCriticalErrorOnItsQuestionsWasResolved()
    {
        var original = BenchmarkReportJson.Deserialize<BenchmarkReportContentSnapshot>(RootCauseReportFixture.ComparisonDocument().ContentJson);
        var run = original.Runs.First(r => r.Questions.Count > 0);
        int number = run.Questions[0].Number;
        string q = "Q" + number.ToString(CultureInfo.InvariantCulture);
        void Cite(BenchmarkReportWriterOutput w)
        {
            w.Observations![0].Questions = new List<int> { number };
            w.Observations[0].Evidence = new List<string> { q };
        }

        var document = RcaDocument(
            content: content =>
            {
                foreach (var item in content.Runs.SelectMany(r => r.Questions)) item.CriticalErrorResolution = null;
                content.Runs.First(r => r.RunId == run.RunId && r.Letter == run.Letter).Questions[0].CriticalErrorResolution = "UpheldByVerifier";
            },
            writer: Cite);
        var sheet = BenchmarkReportJson.Deserialize<BenchmarkReportFactSheet>(document.FactsJson);

        string observations = RcaSection(BenchmarkReportPackRenderer.Render(document, RcaNamed), "### What the graders recorded");

        string model = sheet.Peers.Single(p => p.Letter == run.Letter).Label;
        Assert.Contains("  - *Critical errors:* " + model + " on " + q + ": upheld by the claim verifier (cap applied).\n", observations);

        // Stored without the resolutions, the observation prints none.
        string stored = RcaSection(BenchmarkReportPackRenderer.Render(RcaDocument(
            content: content =>
            {
                foreach (var item in content.Runs.SelectMany(r => r.Questions)) item.CriticalErrorResolution = null;
            },
            writer: Cite), RcaNamed), "### What the graders recorded");
        Assert.DoesNotContain("*Critical errors:*", stored);
    }

    [Fact]
    public void AnObservation_StatesTheResolutionOfAnAnswerThatHasNoExcerpt_OncePerModel()
    {
        var original = RootCauseReportFixture.ComparisonDocument();
        var content = BenchmarkReportJson.Deserialize<BenchmarkReportContentSnapshot>(original.ContentJson);
        var facts = BenchmarkReportJson.Deserialize<BenchmarkReportFactSheet>(original.FactsJson);
        var writer = BenchmarkReportJson.Deserialize<BenchmarkReportWriterOutput>(original.WriterOutputJson);
        int number = writer.Observations![0].Questions![0];
        var excerpted = content.Runs.Where(r => r.Questions.Any(q => q.Number == number)).Select(r => r.Letter).ToHashSet(StringComparer.Ordinal);
        string letter = facts.Questions.Single(q => q.Number == number).Models!.Select(c => c.Letter).First(l => !excerpted.Contains(l));

        var document = RcaDocument(
            sheet =>
            {
                foreach (var cell in sheet.Questions.SelectMany(q => q.Models ?? new List<BenchmarkReportQuestionModelScore>())) cell.CriticalErrorResolutions = null;
                sheet.Questions.Single(q => q.Number == number).Models!.Single(c => c.Letter == letter).CriticalErrorResolutions =
                    new List<string> { "None", "UpheldByVerifier", "UpheldByVerifier" };
            },
            c =>
            {
                foreach (var item in c.Runs.SelectMany(r => r.Questions)) item.CriticalErrorResolution = null;
            });

        string observations = RcaSection(BenchmarkReportPackRenderer.Render(document, RcaNamed), "### What the graders recorded");

        string model = facts.Peers.Single(p => p.Letter == letter).Label;
        Assert.Contains("  - *Critical errors:* " + model + " on Q" + number.ToString(CultureInfo.InvariantCulture)
            + ": upheld by the claim verifier (cap applied).\n", observations);

        string anonymized = RcaSection(BenchmarkReportPackRenderer.Render(document, RcaAnonymized), "### What the graders recorded");
        Assert.Contains("  - *Critical errors:* Model " + letter + " on Q" + number.ToString(CultureInfo.InvariantCulture)
            + ": upheld by the claim verifier (cap applied).\n", anonymized);
    }

    [Fact]
    public void AnEvidenceLine_NamesTheModelsOfARootCauseFact_InTheNamedCopy_AndLettersThemInTheAnonymizedOne()
    {
        var facts = BenchmarkReportJson.Deserialize<BenchmarkReportFactSheet>(RootCauseReportFixture.ComparisonDocument().FactsJson);
        var profile = facts.Facts.First(f => f.Available && f.Key.StartsWith("rca.profile.", StringComparison.Ordinal)
                                             && facts.Peers.Any(p => p.Letter == f.Key.Split('.')[2]));
        var atRisk = facts.Facts.First(f => f.Available && f.Key.StartsWith("rca.atRisk.", StringComparison.Ordinal) && f.Key.EndsWith(".total", StringComparison.Ordinal));
        string profiled = profile.Key.Split('.')[2], risked = atRisk.Key.Split('.')[2];
        var document = RcaDocument(writer: w => w.Observations![0].Evidence = new List<string> { profile.Key, atRisk.Key });

        string named = RcaSection(BenchmarkReportPackRenderer.Render(document, RcaNamed), "### What the graders recorded");
        string Label(string letter) => facts.Peers.Single(p => p.Letter == letter).Label;
        Assert.Contains("  - *Evidence:* " + Label(profiled) + ": ", named);
        Assert.Contains(" · Index points at risk, " + Label(risked) + " (upper bound): " + atRisk.Display, named);
        Assert.DoesNotContain("profile: ", named);
        Assert.DoesNotContain("Profile, ", named);

        string anonymized = RcaSection(BenchmarkReportPackRenderer.Render(document, RcaAnonymized), "### What the graders recorded");
        Assert.Contains("  - *Evidence:* Model " + profiled + ": ", anonymized);
        Assert.Contains(" · Index points at risk, Model " + risked + " (upper bound): " + atRisk.Display, anonymized);
        Assert.DoesNotContain(Label(profiled), anonymized);
    }

    [Fact]
    public void AppendixG_GivesTheCitedAndTheCriticalErrorQuestions_ButNotOneWithARefutedSentenceAlone()
    {
        var content = BenchmarkReportJson.Deserialize<BenchmarkReportContentSnapshot>(RootCauseReportFixture.ComparisonDocument().ContentJson);
        var detailed = content.Runs.SelectMany(r => r.Questions).Select(q => q.Number).Distinct().OrderBy(n => n).ToList();
        Assert.True(detailed.Count >= 2, "the fixture is expected to give two questions in detail");
        int refuted = detailed[0], flagged = detailed[1];

        // The writer cites no question, so only a critical error brings a question into the appendix.
        BenchmarkReportDocument With(Action<BenchmarkReportQuestionModelScore> flag) => RcaDocument(
            sheet =>
            {
                foreach (var q in sheet.Questions)
                {
                    q.CriticalError = false;
                    q.CriticalErrorCount = null;
                    foreach (var cell in q.Models ?? new List<BenchmarkReportQuestionModelScore>())
                    {
                        cell.CriticalError = false;
                        cell.CriticalErrorCount = null;
                        cell.CriticalErrorResolutions = null;
                        cell.RefutedClaims = 0;
                        cell.RefutedAnswerSentences = 0;
                    }
                }
                var refutedCell = sheet.Questions.Single(q => q.Number == refuted).Models!.First();
                refutedCell.RefutedClaims = 2;
                refutedCell.RefutedAnswerSentences = 2;
                flag(sheet.Questions.Single(q => q.Number == flagged).Models!.First());
            },
            c =>
            {
                foreach (var item in c.Runs.SelectMany(r => r.Questions)) item.CriticalErrorResolution = null;
            },
            w =>
            {
                w.Observations = new List<BenchmarkReportWriterItem>();
                w.WriterHypotheses = new List<BenchmarkReportWriterHypothesis>();
            });
        static bool Has(string appendix, int number)
        {
            string label = "\n### Q" + number.ToString(CultureInfo.InvariantCulture);
            return appendix.Contains(label + "\n", StringComparison.Ordinal) || appendix.Contains(label + ": ", StringComparison.Ordinal);
        }
        string Appendix(BenchmarkReportDocument document)
        {
            var sheet = BenchmarkReportJson.Deserialize<BenchmarkReportFactSheet>(document.FactsJson);
            var writer = BenchmarkReportJson.Deserialize<BenchmarkReportWriterOutput>(document.WriterOutputJson);
            Assert.Empty(BenchmarkReportPackValidator.CitedQuestions(writer, sheet));
            return RcaSection(BenchmarkReportPackRenderer.Render(document, RcaNamed), "## Appendix G: Question details");
        }

        // Overturned, confirmed, and flagged on a method-12 run that records no resolution: each is printed.
        foreach (var flag in new Action<BenchmarkReportQuestionModelScore>[]
        {
            c => c.CriticalErrorResolutions = new List<string> { "OverturnedByVerifier" },
            c => { c.CriticalError = true; c.CriticalErrorResolutions = new List<string> { "None", "Agreed" }; },
            c => c.CriticalError = true
        })
        {
            string appendix = Appendix(With(flag));
            Assert.True(Has(appendix, flagged), appendix);
            Assert.False(Has(appendix, refuted), appendix);
        }

        // An answer resolved as None had no critical error.
        string none = Appendix(With(c => c.CriticalErrorResolutions = new List<string> { "None", "None" }));
        Assert.False(Has(none, flagged), none);
        Assert.False(Has(none, refuted), none);
        Assert.Contains("No question given in detail is cited.", none);
    }

    [Fact]
    public void AppendixG_StripsABoldInlineSourceParagraph_FromTheRubric()
    {
        const string Rubric = "- The first rubric point.\n\n**SOURCE** — src/monst.c lines 10 to 20, the cited source text.\n\n- The second rubric point.";
        var document = RcaDocument(content: content =>
        {
            foreach (var question in content.Questions!)
            {
                question.ExpectedPoints = Rubric;
                question.ExpectedPointsRecorded = true;
            }
            foreach (var item in content.Runs.SelectMany(r => r.Questions)) item.CriticalErrorResolution = "UpheldByVerifier";
        });

        string appendix = RcaSection(BenchmarkReportPackRenderer.Render(document, RcaNamed), "## Appendix G: Question details");

        Assert.Contains("> - The first rubric point.\n", appendix);
        Assert.Contains("> - The second rubric point.\n", appendix);
        Assert.DoesNotContain("SOURCE", appendix);
        Assert.DoesNotContain("src/monst.c", appendix);
    }

    [Fact]
    public void WithoutACountOrConfigurationFact_AppendixE_IsLeftOut_AndTheLaterAppendicesAreLetteredOn()
    {
        string current = BenchmarkReportPackRenderer.Render(RcaDocument(), RcaNamed);
        Assert.Contains("\n## Appendix E: Known facts\n", current);
        Assert.Contains("\n## Appendix H: Glossary\n", current);
        Assert.Contains("and Appendix H defines every term this document uses.", current);

        var document = RcaDocument(sheet => sheet.RootCause!.KnownFacts.RemoveAll(k => k.Kind is RootCauseKnownFacts.KindCount or RootCauseKnownFacts.KindConfiguration));
        string text = BenchmarkReportPackRenderer.Render(document, RcaNamed);

        Assert.DoesNotContain("Known facts", RcaSection(text, "## Appendices"));
        Assert.DoesNotContain("## Appendix E: Known facts", text);
        Assert.DoesNotContain("No rule found a count or a configuration fact to state.", text);
        Assert.DoesNotContain("## Appendix H", text);
        foreach (string heading in new[] { "## Appendix E: Reproducibility", "## Appendix F: Question details", "## Appendix G: Glossary" })
        {
            Assert.Contains("\n" + heading + "\n", text);
        }
        string list = RcaSection(text, "## Appendices");
        Assert.Contains("- **D. Run settings:** ", list);
        Assert.Contains("\n- **E. Reproducibility:** ", list);
        Assert.Contains("\n- **F. Question details:** ", list);
        Assert.Contains("\n- **G. Glossary:** ", list);
        Assert.Contains("and Appendix G defines every term this document uses.", RcaSection(text, "## Key terms"));
    }

    [Fact]
    public void TheCostAndTokenTables_HaveOneOutputColumn_MarkingAFigureWithThinkingIncluded()
    {
        static void Merge(List<RootCauseSegment> segments)
        {
            var output = segments.Single(s => s.Key == RootCauseSegments.Output);
            double reasoning = segments.Where(s => s.Key == RootCauseSegments.Reasoning).Sum(s => s.Value);
            segments.RemoveAll(s => s.Key is RootCauseSegments.Output or RootCauseSegments.Reasoning);
            segments.Add(new RootCauseSegment
            {
                Key = RootCauseSegments.OutputWithThinking,
                Label = RootCauseSegments.Label(RootCauseSegments.OutputWithThinking),
                Value = output.Value + reasoning
            });
        }

        double cost = 0, tokens = 0;
        var document = RcaDocument(sheet =>
        {
            var row = sheet.RootCause!.CostRows.First(r => r.Total.HasValue && r.Token != null);
            Merge(row.Segments);
            cost = row.Segments.Single(s => s.Key == RootCauseSegments.OutputWithThinking).Value;
            var evidence = sheet.RootCause.Evidence.First(e => e.Tokens.Any(s => s.Key == RootCauseSegments.Output));
            Merge(evidence.Tokens);
            tokens = evidence.Tokens.Single(s => s.Key == RootCauseSegments.OutputWithThinking).Value;
        });
        string text = BenchmarkReportPackRenderer.Render(document, RcaNamed);

        string costs = RcaSection(text, "### Where the cost goes");
        Assert.Contains(" | Output |", costs);
        Assert.DoesNotContain("Visible output |", costs);
        Assert.DoesNotContain("Output, thinking included", costs);
        Assert.Contains(" | " + BenchmarkReportFormat.Usd(cost) + "* |", costs);
        Assert.Contains(" \\* Thinking included; the provider does not report it separately.*\n", costs);

        string tokenTable = RcaSection(text, "### Tokens per question");
        Assert.Contains(" | Output |", tokenTable);
        Assert.DoesNotContain("Output, thinking included", tokenTable);
        Assert.Contains(" | " + BenchmarkReportFormat.Count(tokens) + "* |", tokenTable);
        Assert.Contains("\\* Thinking included; the provider does not report it separately.*\n", tokenTable);

        // Without a merged figure, the column is the visible output and no note is printed.
        string plain = BenchmarkReportPackRenderer.Render(RcaDocument(), RcaNamed);
        Assert.Contains(" | Visible output |", RcaSection(plain, "### Where the cost goes"));
        Assert.DoesNotContain("Thinking included;", plain);
    }

    [Fact]
    public void ReferenceSecond_TurnsADifferenceOrARatio_SoTheReferenceIsTheSecondModel()
    {
        var difference = new BenchmarkReportPairedTest
        {
            FirstLetter = "C", SecondLetter = "A", PairedItems = 12, EffectKind = "Difference", Effect = 4.5, Lower = 1.5, Upper = 7.0,
            PValue = 0.01, AdjustedPValue = 0.02, Established = true, Favors = "First"
        };
        var turned = BenchmarkReportPackRenderer.ReferenceSecond(difference, "C");
        Assert.Equal(("A", "C"), (turned.FirstLetter, turned.SecondLetter));
        Assert.Equal((-4.5, -7.0, -1.5), (turned.Effect!.Value, turned.Lower!.Value, turned.Upper!.Value));
        Assert.Equal("Second", turned.Favors);
        Assert.Equal((12, 0.02, true), (turned.PairedItems, turned.AdjustedPValue!.Value, turned.Established));
        Assert.Same(difference, BenchmarkReportPackRenderer.ReferenceSecond(difference, "A"));

        var ratio = new BenchmarkReportPairedTest
        {
            FirstLetter = "C", SecondLetter = "B", EffectKind = "Ratio", Effect = 2.0, Lower = 1.25, Upper = 4.0, Favors = "Second", Established = true
        };
        var inverted = BenchmarkReportPackRenderer.ReferenceSecond(ratio, "C");
        Assert.Equal(("B", "C"), (inverted.FirstLetter, inverted.SecondLetter));
        Assert.Equal((0.5, 0.25, 0.8), (inverted.Effect!.Value, inverted.Lower!.Value, inverted.Upper!.Value));
        Assert.Equal("First", inverted.Favors);
    }

    [Fact]
    public void TheBodysPairedTests_PutTheReferenceSecond_HoweverThePairsWereStored()
    {
        string reference = string.Empty;
        string Tables(BenchmarkReportDocument document)
        {
            string text = BenchmarkReportPackRenderer.Render(document, RcaNamed);
            var facts = BenchmarkReportJson.Deserialize<BenchmarkReportFactSheet>(document.FactsJson);
            var family = facts.PairedTests!.First(f => f.Mode != "AllPairs");
            reference = facts.Peers.Single(p => p.Letter == family.ReferenceLetter).Label;
            return RcaSection(text, "## Evidence: intelligence") + RcaSection(text, "## Evidence: speed") + RcaSection(text, "## Evidence: cost");
        }

        string stored = Tables(RcaDocument());
        string flipped = Tables(RcaDocument(s =>
        {
            foreach (var measure in s.PairedTests!.Where(f => f.Mode != "AllPairs").SelectMany(f => f.Measures))
            {
                // Each pair stored the other way round.
                bool ratio = measure.Measure != BenchmarkPairedTests.IntelligenceMeasure;
                measure.Pairs = measure.Pairs.Select(p => BenchmarkReportPackRenderer.ReferenceSecond(p, p.FirstLetter, ratio)).ToList();
            }
        }));

        Assert.Equal(stored, flipped);
        var rows = RcaSection(stored, "### Paired tests against " + reference)
            .Split('\n')
            .Where(l => l.StartsWith("| ", StringComparison.Ordinal) && !l.StartsWith("| Pair |", StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(rows);
        Assert.All(rows, row => Assert.StartsWith("| ", row));
        Assert.All(rows, row => Assert.Contains(" vs " + reference + " |", row));
    }

    [Fact]
    public void AppendixA_PrintsNoResults_AndOnlyThePairedTestsTheBodyDoesNot_AndAppendixE_OnlyCountsAndConfiguration()
    {
        var document = RcaDocument();
        var sheet = BenchmarkReportJson.Deserialize<BenchmarkReportFactSheet>(document.FactsJson);
        string text = BenchmarkReportPackRenderer.Render(document, RcaNamed);
        string appendixA = RcaSection(text, "## Appendix A: Intelligence detail");

        Assert.DoesNotContain("### Results\n", appendixA);
        Assert.DoesNotContain("**Against ", appendixA);
        Assert.DoesNotContain("Paired tests: every family", appendixA);
        if (sheet.PairedTests!.Any(f => f.Mode == "AllPairs")) Assert.Contains("**Every pair**", appendixA);

        string appendixE = RcaSection(text, "## Appendix E: Known facts");
        foreach (var known in sheet.RootCause!.KnownFacts)
        {
            string line = "- **" + known.Number.ToString(CultureInfo.InvariantCulture) + ".** ";
            if (known.Kind is "count" or "configuration") Assert.Contains(line, appendixE);
            else Assert.DoesNotContain(line, appendixE);
        }

        // With only the reference family, the appendix points to the body instead of printing it again.
        string pointer = RcaSection(BenchmarkReportPackRenderer.Render(RcaDocument(s => s.PairedTests = s.PairedTests!.Where(f => f.Mode != "AllPairs").ToList()), RcaNamed),
            "## Appendix A: Intelligence detail");
        Assert.Contains("### Paired tests\n\nEvery paired test of this comparison is under the evidence on intelligence, speed and cost.\n", pointer);
    }

    [Fact]
    public void TheLowestDimensionSentence_CountsTheModelsTheResponseStyleRuleMarks()
    {
        var document = RcaDocument(sheet =>
        {
            var profiles = sheet.RootCause!.Profiles;
            for (int i = 0; i < profiles.Count; i++)
            {
                profiles[i].Dimensions = new List<double?> { 90, i == 0 ? 80 : 70, 85, 88 };
            }
        });
        int models = BenchmarkReportJson.Deserialize<BenchmarkReportFactSheet>(document.FactsJson).RootCause!.Profiles.Count;
        string text = BenchmarkReportPackRenderer.Render(document, RcaNamed);

        string sentence = "Completeness is the lowest dimension for " + models + " of " + models + " models (10.0 to 20.0 points below Accuracy), "
            + "and at least 13 points below Accuracy for " + (models - 1) + " of them.";
        Assert.Contains(sentence + "\n", RcaSection(text, "## Evidence: intelligence"));

        // What we found builds its own sentence on the same figures.
        string shared = Assert.Single(RcaFindings(text), f => f.StartsWith("Completeness is the weakest dimension", StringComparison.Ordinal));
        Assert.StartsWith("Completeness is the weakest dimension of " + models + " of " + models + " models, 10.0 to 20.0 points below Accuracy", shared);
        Assert.EndsWith("; a pattern this wide does not tell the models apart.", shared);
    }

    // --- What we found ---------------------------------------------------------------------------

    /// <summary>The bullets of What we found, without their dashes.</summary>
    private static List<string> RcaFindings(string text)
        => RcaSection(text, "### What we found").Split('\n')
            .Where(l => l.StartsWith("- ", StringComparison.Ordinal))
            .Select(l => l[2..])
            .ToList();

    /// <summary>The fixture comparison's entry for the model with <paramref name="letter"/>.</summary>
    private static BenchmarkReportEntryFigures RcaEntry(BenchmarkReportFactSheet sheet, string letter)
        => sheet.Entries.Single(e => e.EntryKey == sheet.Peers.Single(p => p.Letter == letter).EntryKey);

    /// <summary>
    /// The fixture comparison with every What we found candidate silent: no answers without tools, no profile dimensions,
    /// no paired test, no notable finding, no family gap, the Intelligence Index intervals overlapping, costs within 1.5 times
    /// of each other and one time for every model.
    /// </summary>
    private static void RcaQuiet(BenchmarkReportFactSheet sheet)
    {
        foreach (var evidence in sheet.RootCause!.Evidence)
        {
            evidence.ScoredAnswers = 36;
            evidence.NoToolAnswers = 0;
        }
        foreach (var profile in sheet.RootCause.Profiles) profile.Dimensions = new List<double?>();
        sheet.RootCause.FamilyGaps = null;
        sheet.PairedTests = new List<BenchmarkReportPairedFamily>();
        sheet.NotableFindings = new List<BenchmarkReportNotableFinding>();
        sheet.Facts.RemoveAll(f => f.Key.StartsWith("notable.", StringComparison.Ordinal));
        var letters = sheet.Peers.Select(p => p.Letter).OrderBy(l => l, StringComparer.Ordinal).ToList();
        for (int i = 0; i < letters.Count; i++)
        {
            var entry = RcaEntry(sheet, letters[i]);
            entry.QualityIndex = 70;
            entry.QualityLower = 60;
            entry.QualityUpper = 80;
            entry.CostPerQuestionUsd = 0.020 + 0.002 * i;
            entry.CostDegraded = false;
            entry.ModelTimeP50Ms = 10_000;
            entry.SpeedDegraded = false;
        }
    }

    /// <summary>A paired intelligence test of <paramref name="first"/> against <paramref name="second"/> (first minus second).</summary>
    private static BenchmarkReportPairedTest RcaTest(string first, string second, double effect, bool established)
        => new()
        {
            FirstLetter = first, SecondLetter = second, PairedItems = 12, EffectKind = "Difference", Effect = effect,
            Lower = effect - 2, Upper = effect + 2, PValue = established ? 0.001 : 0.4, AdjustedPValue = established ? 0.004 : 0.8,
            Established = established, Favors = established ? (effect > 0 ? "First" : "Second") : "None"
        };

    /// <summary>A reference family with A as the reference and the given intelligence tests.</summary>
    private static BenchmarkReportPairedFamily RcaReferenceFamily(params BenchmarkReportPairedTest[] tests)
        => new()
        {
            Mode = "Reference",
            ReferenceLetter = "A",
            Measures = new List<BenchmarkReportPairedMeasure>
            {
                new() { Measure = BenchmarkPairedTests.IntelligenceMeasure, FamilySize = tests.Length, Adjustment = "Holm", Pairs = tests.ToList() }
            }
        };

    [Fact]
    public void WhatWeFound_IsSilent_WhenNoCandidateClearsItsGate()
    {
        Assert.Empty(RcaFindings(BenchmarkReportPackRenderer.Render(RcaDocument(RcaQuiet), RcaNamed)));
    }

    [Fact]
    public void WhatWeFound_AnswersWithoutTools_NeedsTheContrast()
    {
        string With(params (int NoTool, int Scored)[] counts)
        {
            var document = RcaDocument(s =>
            {
                RcaQuiet(s);
                var evidence = s.RootCause!.Evidence.OrderBy(e => e.Token, StringComparer.Ordinal).ToList();
                for (int i = 0; i < evidence.Count; i++) (evidence[i].NoToolAnswers, evidence[i].ScoredAnswers) = counts[Math.Min(i, counts.Length - 1)];
            });
            return string.Join("\n", RcaFindings(BenchmarkReportPackRenderer.Render(document, RcaNamed)));
        }

        // One model at 25%, the others at most 3 of 36: at least twice their median.
        Assert.Equal("Orion Max answered 9 of 36 scored answers (25%) without any tool call; no other model did so more than 3 times.",
            With((9, 36), (3, 36), (2, 36)));
        Assert.Equal("Orion Max answered 9 of 36 scored answers (25%) without any tool call; no other model did so.", With((9, 36), (0, 36)));

        // Just under twice the others' median: no finding.
        Assert.Equal(string.Empty, With((25, 100), (13, 100)));

        // Every model at or above 20%: one shared sentence.
        Assert.Equal("Every model answered at least 33% of its scored answers without any tool call.", With((34, 100), (33, 100), (40, 100)));
    }

    [Fact]
    public void WhatWeFound_SharedWeakness_OnlyWhenEveryModelOrTheStyleRuleAgrees()
    {
        const string end = "; a pattern this wide does not tell the models apart.";
        const string style = ", under the production chat's concise response style";
        string With(Func<int, List<double?>> dimensions, bool detailed = false)
        {
            var document = RcaDocument(s =>
            {
                RcaQuiet(s);
                var profiles = s.RootCause!.Profiles;
                for (int i = 0; i < profiles.Count; i++) profiles[i].Dimensions = dimensions(i);
                s.Facts.RemoveAll(f => f.Key.EndsWith("style.responseStyleConflict", StringComparison.Ordinal));
                if (detailed)
                {
                    foreach (var peer in s.Peers)
                    {
                        s.Facts.Add(new BenchmarkReportFact { Key = "model." + peer.Letter + ".style.responseStyleConflict", Value = JsonValue.Create(false), Display = "no" });
                    }
                }
            });
            return string.Join("\n", RcaFindings(BenchmarkReportPackRenderer.Render(document, RcaNamed)));
        }

        // Every model: printed, with the style when the rule marks them.
        Assert.Equal("Completeness is the weakest dimension of 5 of 5 models, 20.0 points below Accuracy" + style + end,
            With(_ => new List<double?> { 90, 70, 85, 88 }));
        Assert.Equal("Completeness is the weakest dimension of 5 of 5 models, 20.0 points below Accuracy" + end,
            With(_ => new List<double?> { 90, 70, 85, 88 }, detailed: true));
        Assert.Equal("Completeness is the weakest dimension of 5 of 5 models, 10.0 points below Accuracy" + end,
            With(_ => new List<double?> { 90, 80, 85, 88 }));

        // Four of five: only where the style rule marks at least two thirds of them.
        Func<int, List<double?>> fourOfFive(double completeness) => i => i == 0
            ? new List<double?> { 90, 85, 70, 88 }
            : new List<double?> { 90, completeness, 85, 88 };
        Assert.Equal("Completeness is the weakest dimension of 4 of 5 models, 20.0 points below Accuracy" + style + end, With(fourOfFive(70)));
        Assert.Equal(string.Empty, With(fourOfFive(80)));
        Assert.Equal(string.Empty, With(fourOfFive(70), detailed: true));
    }

    [Fact]
    public void WhatWeFound_ValueForMoney_PartitionsTheOtherModels()
    {
        void Set(BenchmarkReportFactSheet s, string letter, double cost, double lower, double upper)
        {
            var entry = RcaEntry(s, letter);
            entry.CostPerQuestionUsd = cost;
            entry.QualityLower = lower;
            entry.QualityUpper = upper;
            entry.QualityIndex = (lower + upper) / 2;
        }
        BenchmarkReportDocument Value(Action<BenchmarkReportFactSheet>? more = null) => RcaDocument(s =>
        {
            RcaQuiet(s);
            Set(s, "E", 0.001, 50, 60);  // the cheapest
            Set(s, "A", 0.067, 70, 80);  // established higher: its interval is above E's
            Set(s, "B", 0.039, 55, 65);  // not established either way
            Set(s, "C", 0.005, 55, 65);  // not established either way
            Set(s, "D", 0.013, 30, 40);  // established lower
            more?.Invoke(s);
        });

        Assert.Equal(new[]
            {
                "Zeta Prime costs the least per question. Only Orion Max is established to score higher, and Nova Lite scores lower; "
                + "for the other 2 models the order is not established."
            },
            RcaFindings(BenchmarkReportPackRenderer.Render(Value(), RcaNamed)));
        Assert.DoesNotContain(RcaFindings(BenchmarkReportPackRenderer.Render(Value(), RcaNamed)), f => f.Contains("times its cost", StringComparison.Ordinal));

        // A paired test that establishes nothing leaves the order to the intervals, as the notable findings read it.
        var untested = Value(s => s.PairedTests = new List<BenchmarkReportPairedFamily>
        {
            new()
            {
                Mode = "AllPairs",
                Measures = new List<BenchmarkReportPairedMeasure>
                {
                    new() { Measure = BenchmarkPairedTests.IntelligenceMeasure, FamilySize = 2, Adjustment = "Holm", Pairs = new List<BenchmarkReportPairedTest> { RcaTest("A", "E", 20, false), RcaTest("D", "E", -20, false) } }
                }
            }
        });
        Assert.Equal(RcaFindings(BenchmarkReportPackRenderer.Render(Value(), RcaNamed)), RcaFindings(BenchmarkReportPackRenderer.Render(untested, RcaNamed)));

        // Nothing established higher and nothing three times as dear among the rest: silent.
        string cheap = string.Join("\n", RcaFindings(BenchmarkReportPackRenderer.Render(Value(s =>
        {
            Set(s, "A", 0.002, 55, 65);
            Set(s, "B", 0.0029, 55, 65);
            Set(s, "C", 0.0025, 55, 65);
        }), RcaNamed)));
        Assert.DoesNotContain("costs the least", cheap);

        // Nothing established higher, one of the rest three times as dear: the rest by count.
        Assert.Equal("Zeta Prime costs the least per question. No model is established to score higher, and Nova Lite scores lower; "
            + "for the other 3 models the order is not established.",
            Assert.Single(RcaFindings(BenchmarkReportPackRenderer.Render(Value(s => Set(s, "A", 0.067, 55, 65)), RcaNamed))));

        // One model in the rest is named, and with nothing lower the clause is left out.
        Assert.Equal("Zeta Prime costs the least per question. Only Orion Max is established to score higher; for the other model, Vega Pro, "
            + "the order is not established.",
            Assert.Single(RcaFindings(BenchmarkReportPackRenderer.Render(Value(s =>
            {
                RcaEntry(s, "C").CostDegraded = true;
                RcaEntry(s, "D").CostDegraded = true;
            }), RcaNamed))));

        // A degraded cost is not priced: the cheapest is then Lyra Mini.
        string degraded = Assert.Single(RcaFindings(BenchmarkReportPackRenderer.Render(Value(s => RcaEntry(s, "E").CostDegraded = true), RcaNamed)));
        Assert.StartsWith("Lyra Mini costs the least per question. ", degraded);
        Assert.DoesNotContain("Zeta Prime", degraded);
    }

    [Fact]
    public void WhatWeFound_ThinkingLevel_OnlyWhenTheHigherLevelIsNotEstablishedAbove_AndCostsMore()
    {
        const string inversion = "Model C, at thinking level high, has an Intelligence Index of 83, at least that of Model B; took 0.36 times as long.";
        BenchmarkReportDocument Levels(double highTime, double highCost) => RcaDocument(s =>
        {
            RcaQuiet(s);
            foreach (var letter in new[] { "B", "C" })
            {
                var peer = s.Peers.Single(p => p.Letter == letter);
                peer.Provider = "Northwind";
                peer.ModelId = "vega-pro";
                peer.ThinkingLevel = letter == "B" ? "max" : "high";
                RcaEntry(s, letter).QualityIndex = 83;
            }
            RcaEntry(s, "B").ModelTimeP50Ms = highTime;
            RcaEntry(s, "B").CostPerQuestionUsd = highCost;
            RcaEntry(s, "C").ModelTimeP50Ms = 10_000;
            RcaEntry(s, "C").CostPerQuestionUsd = 0.020;
            s.NotableFindings = new List<BenchmarkReportNotableFinding>
            {
                new() { Number = 1, Kind = BenchmarkNotableFindings.ThinkingLevelInversion, Models = new List<string> { "B", "C" }, Summary = inversion }
            };
        });

        // The pair prints from the thinking-level bullet, and the notable finding on it is left out.
        Assert.Equal(new[]
            {
                "Vega Pro is not established to score higher than Lyra Mini, the same model at a lower thinking level (83 against 83), "
                + "but takes 2.8 times as long and costs 2.3 times as much."
            },
            RcaFindings(BenchmarkReportPackRenderer.Render(Levels(28_000, 0.046), RcaNamed)));

        // Below 1.5 times on both, the notable finding prints as stored, its models named as the copy names them.
        Assert.Equal(new[] { "Lyra Mini, at thinking level high, has an Intelligence Index of 83, at least that of Vega Pro; took 0.36 times as long." },
            RcaFindings(BenchmarkReportPackRenderer.Render(Levels(12_000, 0.024), RcaNamed)));

        // The higher level established above the lower: no thinking-level bullet.
        var above = Levels(28_000, 0.046);
        var sheet = BenchmarkReportJson.Deserialize<BenchmarkReportFactSheet>(above.FactsJson);
        RcaEntry(sheet, "B").QualityLower = 90;
        RcaEntry(sheet, "B").QualityUpper = 95;
        above.FactsJson = BenchmarkReportJson.Serialize(sheet);
        Assert.DoesNotContain(RcaFindings(BenchmarkReportPackRenderer.Render(above, RcaNamed)), f => f.Contains("thinking level", StringComparison.Ordinal) && f.StartsWith("Vega Pro", StringComparison.Ordinal));
    }

    [Fact]
    public void WhatWeFound_NotableFindings_SkipsTheCheaperAndBetterTheValueBulletStates()
    {
        static BenchmarkReportNotableFinding Notable(int number, string kind, string summary, params string[] models)
            => new() { Number = number, Kind = kind, Models = models.ToList(), Summary = summary };
        var document = RcaDocument(s =>
        {
            RcaQuiet(s);
            RcaEntry(s, "E").CostPerQuestionUsd = 0.001;
            RcaEntry(s, "D").CostPerQuestionUsd = 0.004;
            s.NotableFindings = new List<BenchmarkReportNotableFinding>
            {
                Notable(1, BenchmarkNotableFindings.CheaperAndBetter, "Model E scored higher than Model B at half its cost.", "B", "E"),
                Notable(2, BenchmarkNotableFindings.AnswerLengthOutlier, "Model C writes the longest answers.", "C"),
                Notable(3, BenchmarkNotableFindings.LeaderMiss, "Model A scored 40 on Q2, where the others scored 80.", "A"),
                Notable(4, BenchmarkNotableFindings.FasterAndBetter, "Model A scored higher than Model B at half its time.", "A", "B"),
                Notable(5, BenchmarkNotableFindings.SharedFailure, "Every model scored below 50 on Q3.")
            };
        });

        var findings = RcaFindings(BenchmarkReportPackRenderer.Render(document, RcaNamed));
        Assert.Equal(3, findings.Count);
        Assert.StartsWith("Zeta Prime costs the least per question. ", findings[0]);
        Assert.Equal("Orion Max scored 40 on Q2, where the others scored 80.", findings[1]);
        Assert.Equal("Orion Max scored higher than Vega Pro at half its time.", findings[2]);
    }

    [Fact]
    public void WhatWeFound_ReferenceDifferences_LeftOutWhenTheCheapestIsTheReference()
    {
        BenchmarkReportDocument Reference(double cEffect, bool cheapReference = false) => RcaDocument(s =>
        {
            RcaQuiet(s);
            s.PairedTests = new List<BenchmarkReportPairedFamily>
            {
                // A minus each other model: B established below A, the rest may be chance.
                RcaReferenceFamily(RcaTest("A", "B", 6, true), RcaTest("A", "C", cEffect, false), RcaTest("A", "D", 1, false), RcaTest("A", "E", -2, false))
            };
            if (cheapReference)
            {
                RcaEntry(s, "A").CostPerQuestionUsd = 0.001;
                RcaEntry(s, "C").CostPerQuestionUsd = 0.005;
            }
        });

        Assert.Equal(new[] { "Against the reference model, Orion Max, the paired tests establish only Vega Pro lower; the other 3 differences, up to 4 points, may be chance." },
            RcaFindings(BenchmarkReportPackRenderer.Render(Reference(4), RcaNamed)));

        // A difference the tests leave open but the intervals separate is named apart, and is not one that may be chance.
        var separated = RcaDocument(s =>
        {
            RcaQuiet(s);
            s.PairedTests = new List<BenchmarkReportPairedFamily>
            {
                RcaReferenceFamily(RcaTest("A", "B", 6, true), RcaTest("A", "C", 4, false), RcaTest("A", "D", 35, false), RcaTest("A", "E", -2, false))
            };
            RcaEntry(s, "D").QualityLower = 30;
            RcaEntry(s, "D").QualityUpper = 40;
        });
        Assert.Equal(new[]
            {
                "Against the reference model, Orion Max, the paired tests establish only Vega Pro lower; the 95% intervals separate Nova Lite from it; "
                + "the other 2 differences, up to 4 points, may be chance."
            },
            RcaFindings(BenchmarkReportPackRenderer.Render(separated, RcaNamed)));

        // Every difference not established below 3 points: nothing surprising to say.
        Assert.Empty(RcaFindings(BenchmarkReportPackRenderer.Render(Reference(2.5), RcaNamed)));

        // The cheapest model is the reference: value for money gives the verdicts against it, and this bullet is left out.
        var cheap = RcaFindings(BenchmarkReportPackRenderer.Render(Reference(4, cheapReference: true), RcaNamed));
        string value = Assert.Single(cheap);
        Assert.Equal("Orion Max costs the least per question. No model is established to score higher, and Vega Pro scores lower; "
            + "for the other 3 models the order is not established.", value);
    }

    /// <summary>The fixture comparison with every What we found candidate firing.</summary>
    private static BenchmarkReportDocument RcaEveryFinding() => RcaDocument(s =>
    {
        RcaQuiet(s);
        var ordered = s.Peers.OrderBy(p => p.Letter, StringComparer.Ordinal).ToList();
        foreach (var evidence in s.RootCause!.Evidence) evidence.NoToolAnswers = evidence.Token == "A" ? 9 : 0;
        foreach (var profile in s.RootCause.Profiles) profile.Dimensions = new List<double?> { 90, 70, 85, 88 };
        s.RootCause.FamilyGaps = new List<RootCauseFamilyGap>
        {
            new()
            {
                Provider1 = ordered[0].Provider, Provider2 = ordered[1].Provider, PairedQuestionCount = 12, IsMemberProviderPair = true,
                GapA = new RootCauseGapEstimate { Value = 3.9, Lower = 1.2, Upper = 6.6 },
                GapB = new RootCauseGapEstimate { Value = -1.2, Lower = -3.0, Upper = 0.6 },
                GapPanel = new RootCauseGapEstimate { Value = 1.4, Lower = 0.1, Upper = 2.7 },
                Contrast = new RootCauseGapEstimate { Value = 5.1, Lower = 2.0, Upper = 8.2 }
            }
        };
        RcaEntry(s, "E").CostPerQuestionUsd = 0.001;
        foreach (var letter in new[] { "B", "C" })
        {
            var peer = s.Peers.Single(p => p.Letter == letter);
            peer.Provider = "Northwind";
            peer.ModelId = "vega-pro";
            peer.ThinkingLevel = letter == "B" ? "max" : "high";
        }
        RcaEntry(s, "B").ModelTimeP50Ms = 28_000;
        s.PairedTests = new List<BenchmarkReportPairedFamily>
        {
            RcaReferenceFamily(RcaTest("A", "B", 6, true), RcaTest("A", "C", 4, false), RcaTest("A", "D", 1, false), RcaTest("A", "E", -2, false))
        };
        s.NotableFindings = new List<BenchmarkReportNotableFinding>
        {
            new() { Number = 1, Kind = BenchmarkNotableFindings.LeaderMiss, Models = new List<string> { "A" }, Summary = "Model A scored 40 on Q2, where the others scored 80." }
        };
        s.RootCause.Anomalies = new List<RootCauseAnomaly> { RcaAnomaly(1, RootCausePriors.PriceTierQuality, "D", "E", 70, 75) };
        RcaVolume(s, input: letter => letter == "A" ? 50_000 : 10_000, calls: letter => letter == "A" ? 5.6 : 3.4);
    });

    /// <summary>An unexpected result of <paramref name="prior"/>: <paramref name="expected"/> expected ahead of <paramref name="other"/>, untested.</summary>
    private static RootCauseAnomaly RcaAnomaly(int number, string prior, string expected, string other, double expectedValue, double otherValue)
        => new()
        {
            Number = number,
            Ref = "U" + number.ToString(CultureInfo.InvariantCulture),
            Prior = prior,
            Axis = RootCausePriors.Axis(prior),
            Expected = expected,
            Other = other,
            ExpectedValue = expectedValue,
            OtherValue = otherValue,
            Gap = prior == RootCausePriors.PriceTierSpeed ? expectedValue / otherValue : otherValue - expectedValue
        };

    /// <summary>Each fixture model's input and output tokens per question and model calls per answer, as given; a null figure removes the fact.</summary>
    private static void RcaVolume(BenchmarkReportFactSheet sheet, Func<string, double?> input, Func<string, double?>? output = null, Func<string, double?>? calls = null)
    {
        void Set(string key, double? value)
        {
            sheet.Facts.RemoveAll(f => f.Key == key);
            if (value is double v)
            {
                sheet.Facts.Add(new BenchmarkReportFact { Key = key, Value = JsonValue.Create(v), Display = BenchmarkReportFormat.Count(v) });
            }
        }
        foreach (var peer in sheet.Peers)
        {
            Set("model." + peer.Letter + ".tokens.inputPerQuestion", input(peer.Letter));
            Set("model." + peer.Letter + ".tokens.outputPerQuestion", output?.Invoke(peer.Letter) ?? 1_000);
        }
        foreach (var evidence in sheet.RootCause!.Evidence)
        {
            evidence.CallsPerAnswer = calls?.Invoke(evidence.Token) ?? 3.0;
        }
    }

    [Fact]
    public void WhatWeFound_PrintsAtMostSeven_InTheProjectOrder()
    {
        var findings = RcaFindings(BenchmarkReportPackRenderer.Render(RcaEveryFinding(), RcaNamed));

        Assert.Equal(7, findings.Count);
        Assert.StartsWith("Orion Max answered 9 of 36 scored answers (25%) without any tool call; no other model did so.", findings[0]);
        Assert.StartsWith("Completeness is the weakest dimension of 5 of 5 models", findings[1]);
        Assert.StartsWith("Grader family: ", findings[2]);
        Assert.Equal("Unexpected: Nova Lite scored below the cheaper Zeta Prime (U1). See Unexpected results.", findings[3]);
        Assert.StartsWith("Zeta Prime costs the least", findings[4]);
        Assert.StartsWith("Vega Pro is not established to score higher than Lyra Mini, the same model at a lower thinking level", findings[5]);
        Assert.Equal("Orion Max read 50,000 input tokens per question, 5.0 times the other models' median, at 5.6 model calls per answer "
            + "against their median of 3.4.", findings[6]);
        Assert.DoesNotContain(findings, f => f.StartsWith("Against the reference model", StringComparison.Ordinal));
    }

    [Fact]
    public void WhatWeFound_UnexpectedResults_GroupsTheAnomaliesOfOnePriorAndModel()
    {
        var document = RcaDocument(s =>
        {
            RcaQuiet(s);
            s.RootCause!.Anomalies = new List<RootCauseAnomaly>
            {
                RcaAnomaly(1, RootCausePriors.PriceTierQuality, "D", "E", 70, 75),
                RcaAnomaly(2, RootCausePriors.NewerRelease, "A", "B", 80, 83),
                RcaAnomaly(3, RootCausePriors.PriceTierSpeed, "E", "D", 20_000, 8_000),
                RcaAnomaly(4, RootCausePriors.PriceTierSpeed, "E", "C", 20_000, 9_000)
            };
        });

        Assert.Equal(new[]
            {
                "Unexpected: Nova Lite scored below the cheaper Zeta Prime (U1); Orion Max scored below the older Vega Pro (U2); "
                + "Zeta Prime was slower than the dearer Nova Lite and Lyra Mini (U3–U4). See Unexpected results."
            },
            RcaFindings(BenchmarkReportPackRenderer.Render(document, RcaNamed)));

        // Without an anomaly, and on a result stored without them, no bullet.
        Assert.Empty(RcaFindings(BenchmarkReportPackRenderer.Render(RcaDocument(s => { RcaQuiet(s); s.RootCause!.Anomalies = null; }), RcaNamed)));
    }

    [Fact]
    public void WhatWeFound_TokenVolume_NeedsTwiceTheOthersMedian_AndThreeModels()
    {
        string With(Func<string, double?> input, Func<string, double?>? output = null)
            => string.Join("\n", RcaFindings(BenchmarkReportPackRenderer.Render(RcaDocument(s =>
            {
                RcaQuiet(s);
                RcaVolume(s, input, output, letter => letter == "A" ? 5.6 : 3.4);
            }), RcaNamed)));

        Assert.Equal("Orion Max read 20,000 input tokens per question, 2.0 times the other models' median, at 5.6 model calls per answer "
            + "against their median of 3.4.", With(l => l == "A" ? 20_000 : 10_000));
        Assert.Equal(string.Empty, With(l => l == "A" ? 19_000 : 10_000));

        // Only the output tokens stand out: the output clause.
        Assert.Equal("Orion Max wrote 3,000 output tokens per question, reasoning included, 3.0 times the other models' median.",
            With(_ => 10_000, l => l == "A" ? 3_000 : 1_000));

        // Two models qualify: both, the larger multiple first; with fewer than three models with the figure, none.
        Assert.StartsWith("Vega Pro read 40,000 input tokens per question, 4.0 times", With(l => l switch { "A" => 30_000, "B" => 40_000, _ => 10_000 }));
        Assert.Equal(string.Empty, With(l => l switch { "A" => 30_000, "B" => 10_000, _ => null }));
    }

    [Fact]
    public void WhatWeFound_ThinkingLevels_ShareOneBullet_WithThePairedRatios()
    {
        var document = RcaDocument(s =>
        {
            RcaQuiet(s);
            foreach (var (letter, model, level) in new[] { ("B", "vega-pro", "max"), ("C", "vega-pro", "high"), ("D", "nova-lite", "high"), ("E", "nova-lite", "medium") })
            {
                var peer = s.Peers.Single(p => p.Letter == letter);
                peer.Provider = "Northwind";
                peer.ModelId = model;
                peer.ThinkingLevel = level;
            }
            RcaEntry(s, "B").ModelTimeP50Ms = 28_000;
            RcaEntry(s, "D").ModelTimeP50Ms = 15_000;
            static BenchmarkReportPairedTest Ratio(string first, string second, double effect) => new()
            {
                FirstLetter = first, SecondLetter = second, PairedItems = 12, EffectKind = "Ratio", Effect = effect, Lower = effect * 0.8, Upper = effect * 1.2,
                PValue = 0.01, AdjustedPValue = 0.02, Established = true, Favors = "First"
            };
            s.PairedTests = new List<BenchmarkReportPairedFamily>
            {
                new()
                {
                    Mode = "AllPairs",
                    Measures = new List<BenchmarkReportPairedMeasure>
                    {
                        new() { Measure = BenchmarkPairedTests.SpeedMeasure, FamilySize = 1, Adjustment = "None", Pairs = new List<BenchmarkReportPairedTest> { Ratio("B", "C", 2.0) } },
                        new() { Measure = BenchmarkPairedTests.CostMeasure, FamilySize = 1, Adjustment = "None", Pairs = new List<BenchmarkReportPairedTest> { Ratio("C", "B", 0.625) } }
                    }
                }
            };
        });

        // The paired ratios stand in for the medians (2.8) and the means (1.1); the pair without tests keeps its medians.
        Assert.Equal(new[]
            {
                "Vega Pro is not established to score higher than Lyra Mini, the same model at a lower thinking level (70 against 70), "
                + "but takes 2.0 times as long and costs 1.6 times as much; Nova Lite is not established to score higher than Zeta Prime, "
                + "the same model at a lower thinking level (70 against 70), but takes 1.5 times as long."
            },
            RcaFindings(BenchmarkReportPackRenderer.Render(document, RcaNamed)));
    }

    [Fact]
    public void WhatWeFound_UsesNoCausalWord_AndNamesModelsAsTheCopyDoes()
    {
        var document = RcaEveryFinding();
        var sheet = BenchmarkReportJson.Deserialize<BenchmarkReportFactSheet>(document.FactsJson);

        var named = RcaFindings(BenchmarkReportPackRenderer.Render(document, RcaNamed));
        var anonymized = RcaFindings(BenchmarkReportPackRenderer.Render(document, RcaAnonymized));
        Assert.Equal(7, anonymized.Count);
        foreach (string finding in named.Concat(anonymized))
        {
            Assert.DoesNotMatch(RootCauseClaimSupport.CausalRegex, finding);
        }

        Assert.Contains(named, f => f.Contains("Orion Max", StringComparison.Ordinal));
        foreach (var peer in sheet.Peers)
        {
            Assert.DoesNotContain(anonymized, f => f.Contains(peer.Label, StringComparison.Ordinal));
            Assert.DoesNotContain(anonymized, f => f.Contains(peer.Provider, StringComparison.Ordinal));
        }
        Assert.StartsWith("Model A answered 9 of 36 scored answers (25%)", anonymized[0]);
        Assert.Equal("Unexpected: Model D scored below the cheaper Model E (U1). See Unexpected results.", anonymized[3]);
        Assert.StartsWith("Model E costs the least", anonymized[4]);
        Assert.StartsWith("Model B is not established to score higher than Model C, ", anonymized[5]);
        Assert.StartsWith("Model A read 50,000 input tokens per question", anonymized[6]);
    }

    // --- At a glance and Why each model scored as it did ------------------------------------------

    /// <summary>How many times <paramref name="part"/> occurs in <paramref name="text"/>, without overlaps.</summary>
    private static int RcaCount(string text, string part)
    {
        int count = 0;
        for (int at = text.IndexOf(part, StringComparison.Ordinal); at >= 0; at = text.IndexOf(part, at + part.Length, StringComparison.Ordinal)) count++;
        return count;
    }

    /// <summary>The block of the model named <paramref name="name"/> under Why each model scored as it did: its lead line and its bullets.</summary>
    private static string RcaModelBlock(string text, string name)
    {
        string section = RcaSection(text, "## Why each model scored as it did");
        int start = section.IndexOf("\n**" + name, StringComparison.Ordinal);
        Assert.True(start >= 0, name + " has no block");
        int end = section.IndexOf("\n\n", section.IndexOf("\n- ", start, StringComparison.Ordinal), StringComparison.Ordinal);
        return section[(start + 1)..(end + 1)];
    }

    /// <summary>The fixture comparison's name of the model with <paramref name="letter"/>.</summary>
    private static string RcaLabel(BenchmarkReportDocument document, string letter)
        => BenchmarkReportJson.Deserialize<BenchmarkReportFactSheet>(document.FactsJson).Peers.Single(p => p.Letter == letter).Label;

    [Fact]
    public void AtAGlance_HasTheFiguresOnly_AndPointsToWhyEachModelScored()
    {
        string glance = RcaSection(BenchmarkReportPackRenderer.Render(RcaDocument(), RcaNamed), "### At a glance");

        Assert.Contains("| Model | Intelligence | Median time | Cost per question |\n|---|---|---|---|\n", glance);
        Assert.DoesNotContain("Where it lost most points", glance);
        Assert.DoesNotContain("| Flags |", glance);
        Assert.Contains("Why each model scored as it did gives, for each model, its strengths, where it gained and lost points, its difference from the reference "
            + "by dimension, its flags and the hypotheses about it.\n", glance);
    }

    [Fact]
    public void WhyEachModelScored_IsOneBlockPerModel_WithEveryEntryOfItsProfile()
    {
        var document = RcaDocument();
        var sheet = BenchmarkReportJson.Deserialize<BenchmarkReportFactSheet>(document.FactsJson);
        string text = BenchmarkReportPackRenderer.Render(document, RcaNamed);
        string section = RcaSection(text, "## Why each model scored as it did");
        var profiles = sheet.RootCause!.Profiles;

        Assert.DoesNotContain("| Model | Index |", section);
        foreach (string label in new[] { "- *Strengths:* ", "- *Gained points on:* ", "- *Lost points on:* ", "- *Flags:* " })
        {
            Assert.Equal(profiles.Count, RcaCount(section, "\n" + label));
        }
        foreach (var profile in profiles)
        {
            var peer = sheet.Peers.Single(p => p.Letter == profile.Token);
            string block = RcaModelBlock(text, peer.Label);
            Assert.StartsWith("**" + peer.Label, block);
            Assert.Contains("**: Intelligence " + BenchmarkReportFormat.Whole(RcaEntry(sheet, peer.Letter).QualityIndex!.Value) + " (", block);
            foreach (var entry in profile.Strengths.Concat(profile.Weaknesses).Concat(profile.Flags))
            {
                // Each entry's text with its questions, as today's cells printed it; its first letter may be capitalized.
                Assert.Contains(entry.Text[1..], block);
            }
        }
        Assert.Contains("Gained points on: the questions the model answered at least 10 points above the other models' median, with the index points "
            + "those margins add up to over its questions.", section);
    }

    [Fact]
    public void WhyEachModelScored_SetsEachModelAgainstTheReference_ByDimension()
    {
        string reference = string.Empty, other = string.Empty;
        var document = RcaDocument(s =>
        {
            var result = s.RootCause!;
            reference = result.ReferenceToken!;
            other = s.Peers.Select(p => p.Letter).OrderBy(l => l, StringComparer.Ordinal).First(l => l != reference);
            string first = other;
            result.QualitySplits.RemoveAll(q => q.First == first || q.Second == first);
            result.QualitySplits.Add(new RootCauseQualitySplit
            {
                First = reference,
                Second = other,
                IndexDifference = 4.1,
                Components = new List<RootCauseComponent>
                {
                    new() { Key = "accuracy", Label = "Accuracy", Amount = 5.0 },
                    new() { Key = "completeness", Label = "Completeness", Amount = -1.2 },
                    new() { Key = "conciseness", Label = "Conciseness", Amount = 0.3 },
                    new() { Key = "readability", Label = "Readability", Amount = 0.01 },
                    new() { Key = "other", Label = "Critical-error cap, difficulty weighting and spread", Amount = -0.01 }
                }
            });
        });
        string text = BenchmarkReportPackRenderer.Render(document, RcaNamed);

        // Model minus reference, the dimensions of at least 0.05 points largest first, the remainder left out while it is not the largest.
        Assert.Contains("- *Against the reference:* -4.1 index points: Accuracy -5.0, Completeness +1.2, Conciseness -0.3.\n",
            RcaModelBlock(text, RcaLabel(document, other)));
        Assert.Contains("- *Against the reference:* The reference.\n", RcaModelBlock(text, RcaLabel(document, reference)));
        Assert.Contains("Against the reference: the model's Intelligence Index minus the reference's, split by dimension (Appendix A).",
            RcaSection(text, "## Why each model scored as it did"));

        // Model scope: no line, and At a glance does not promise one.
        string model = BenchmarkReportPackRenderer.Render(
            RootCauseReportFixture.ModelDocument(RootCauseReportFixture.ModelPackSheet(), BenchmarkReportDocumentOrigin.ReportPack), RcaNamed);
        Assert.DoesNotContain("*Against the reference:*", model);
        Assert.DoesNotContain("its difference from the reference by dimension", model);
    }

    [Fact]
    public void WhyEachModelScored_NamesTheHypothesesAndTheUnexpectedResultsAboutEachModel()
    {
        var document = RcaDocument(s =>
        {
            var result = s.RootCause!;
            result.Hypotheses.RemoveAll(h => h.Axis == RootCauseAxes.Intelligence && h.Kind != "null");
            result.Hypotheses.Insert(0, new RootCauseHypothesis
            {
                Id = "HQ1", Number = 1, Key = "HQ1.1", Axis = RootCauseAxes.Intelligence, Kind = "pair", Models = new List<string> { "B", "A" },
                ModelsText = "Model B and Model A", Statement = "Model B makes more critical errors than Model A.",
                Label = "critical errors of Model B against Model A", Grade = RootCauseGrades.Low
            });

            // The single HQ5 of a comparison is about every model it names, so it is about none of them; with two models it looks like a pair.
            result.Hypotheses.Insert(1, new RootCauseHypothesis
            {
                Id = "HQ5", Number = 1, Key = "HQ5.1", Axis = RootCauseAxes.Intelligence, Kind = "pair", Models = new List<string> { "C", "D" },
                ModelsText = "Model C and Model D", Statement = "Completeness was the lowest dimension, at least 13 points below Accuracy, for 2 of 5 models under the concise response style.",
                Label = "the concise response style, 2 of 5 models", Grade = RootCauseGrades.Low
            });
            result.Anomalies = new List<RootCauseAnomaly>
            {
                RcaAnomaly(1, RootCausePriors.PriceTierQuality, "B", "E", 70, 75),
                RcaAnomaly(2, RootCausePriors.PriceTierSpeed, "E", "D", 20_000, 8_000)
            };
        });
        string text = BenchmarkReportPackRenderer.Render(document, RcaNamed);
        string Block(string letter) => RcaModelBlock(text, RcaLabel(document, letter));

        Assert.Contains("- *Hypotheses:* HQ1.1: critical errors of " + RcaLabel(document, "B") + " against " + RcaLabel(document, "A") + ".\n", Block("B"));
        Assert.DoesNotContain("*Hypotheses:*", Block("A"));
        Assert.DoesNotContain("*Hypotheses:*", Block("C"));
        Assert.DoesNotContain("HQ5.1", RcaSection(text, "## Why each model scored as it did"));

        Assert.Contains("- *Unexpected results:* U1 (Unexpected results).\n", Block("B"));
        Assert.Contains("- *Unexpected results:* U1, U2 (Unexpected results).\n", Block("E"));
        Assert.Contains("- *Unexpected results:* U2 (Unexpected results).\n", Block("D"));
        Assert.DoesNotContain("*Unexpected results:*", Block("A"));
    }

    [Fact]
    public void WhyEachModelScored_LeavesOutTheSharedWeakestDimension_AndSaysWhy()
    {
        BenchmarkReportDocument With(string? shared) => RcaDocument(s =>
        {
            var result = s.RootCause!;
            result.SharedLowestDimension = shared;
            var profiles = result.Profiles.OrderBy(p => p.Token, StringComparer.Ordinal).ToList();
            profiles[0].Weaknesses = new List<RootCauseProfileEntry>
            {
                new() { Kind = RootCauseProfiles.LostPointsOn, Text = "low-scoring answers: 2.2 index points on 2 questions (Q1 and Q2)", Short = "2.2 points" }
            };
            profiles[1].Weaknesses = new List<RootCauseProfileEntry>
            {
                new() { Kind = RootCauseProfiles.LostPointsOn, Text = "low-scoring answers, mostly Accuracy: 1.0 index points on 1 question (Q4)", Short = "mostly Accuracy, 1.0 points" }
            };
        });
        const string note = "Completeness is the weakest dimension of every model (What we found), so a Lost points entry that would say 'mostly Completeness' "
            + "leaves it out.";

        string shared = RcaSection(BenchmarkReportPackRenderer.Render(With("Completeness"), RcaNamed), "## Why each model scored as it did");
        Assert.Contains("- *Lost points on:* Low-scoring answers: 2.2 index points on 2 questions (Q1 and Q2).\n", shared);
        Assert.Contains("- *Lost points on:* Low-scoring answers, mostly Accuracy: 1.0 index points on 1 question (Q4).\n", shared);
        Assert.DoesNotContain("mostly Completeness:", shared);
        Assert.Contains(note, shared);

        string none = RcaSection(BenchmarkReportPackRenderer.Render(With(null), RcaNamed), "## Why each model scored as it did");
        Assert.DoesNotContain("is the weakest dimension of every model", none);
        Assert.Contains("- *Lost points on:* Low-scoring answers, mostly Accuracy: 1.0 index points on 1 question (Q4).\n", none);
    }

    [Fact]
    public void WhyEachModelScored_WithFewerThanThreeModels_SaysTheGainedPointsAreNotMeasured()
    {
        var document = RcaDocument(s =>
        {
            var result = s.RootCause!;
            result.Profiles = result.Profiles.OrderBy(p => p.Token, StringComparer.Ordinal).Take(2).ToList();
            foreach (var profile in result.Profiles) profile.Strengths.RemoveAll(e => e.Kind == RootCauseProfiles.GainedPointsOn);
        });
        string section = RcaSection(BenchmarkReportPackRenderer.Render(document, RcaNamed), "## Why each model scored as it did");

        Assert.Equal(2, RcaCount(section, "- *Gained points on:* Not measured with fewer than 3 models.\n"));
        Assert.DoesNotContain("Not measured with fewer than", BenchmarkReportPackRenderer.Render(RcaDocument(), RcaNamed));
    }

    // --- Unexpected results ----------------------------------------------------------------------

    [Fact]
    public void UnexpectedResults_GiveEachAnomalyAsABlock_AndSayWhichPairsHadNoPairedTest()
    {
        var u1 = RcaAnomaly(1, RootCausePriors.PriceTierQuality, "D", "E", 70, 75);
        u1.Confounds = new List<string> { "thinking level (medium vs xhigh)" };
        u1.Graders = new RootCauseAnomalyGraders
        {
            ExpectedMemberA = 68, OtherMemberA = 76, ExpectedMemberB = 72, OtherMemberB = 74, ExpectedReader = 70, OtherReader = 75, LargerSplit = "A"
        };
        u1.Dimensions = new List<RootCauseAnomalyDimension>
        {
            new() { Name = "Completeness", Expected = 60, Other = 72 },
            new() { Name = "Accuracy", Expected = 80, Other = 84 }
        };
        u1.Questions = new List<RootCauseAnomalyQuestion>
        {
            new() { Reference = "Q3", Expected = 30, Other = 70, OthersMedian = 60 },
            new() { Reference = "Q5", Expected = 50, Other = 80 }
        };
        u1.Hypotheses = new List<string> { "HQ1.1" };
        u1.SettledByRun = 2;
        var u2 = RcaAnomaly(2, RootCausePriors.PriceTierSpeed, "E", "D", 20_000, 8_000);
        u2.SpeedComponent = new RootCauseComponent { Key = "callCount", Label = "number of model calls", Amount = 7_000, Share = 0.6, ShareLower = 0.4, ShareUpper = 0.8 };
        u2.SettledByCheck = 1;

        string text = BenchmarkReportPackRenderer.Render(RcaDocument(s => s.RootCause!.Anomalies = new List<RootCauseAnomaly> { u1, u2 }), RcaNamed);
        string section = RcaSection(text, "## Unexpected results");

        int at = text.IndexOf("\n## Unexpected results\n", StringComparison.Ordinal);
        Assert.True(text.IndexOf("\n## Why each model scored as it did\n", StringComparison.Ordinal) < at);
        Assert.True(at < text.IndexOf("\n## Scope and design\n", StringComparison.Ordinal));

        Assert.Contains("**U1. Nova Lite scored 70, below the cheaper Zeta Prime at 75; not established, and the pair had no paired test.**\n\n", section);
        Assert.Contains("- *Configuration:* Thinking level (medium vs xhigh).\n", section);
        Assert.Contains("- *Graders:* Under panel member A, Nova Lite 68 and Zeta Prime 76; under panel member B, Nova Lite 72 and Zeta Prime 74; "
            + "under the reference reader, Nova Lite 70 and Zeta Prime 75; the two lie further apart under panel member A.\n", section);
        Assert.Contains("- *Dimensions:* Nova Lite against Zeta Prime: Completeness 60 against 72, Accuracy 80 against 84.\n", section);
        Assert.Contains("- *Where:* Nova Lite against Zeta Prime, with the other models' median: Q3 30 against 70 (60); Q5 50 against 80.\n", section);
        Assert.Contains("- *Hypotheses:* HQ1.1.\n", section);
        Assert.Contains("- *Settled by:* Run 2 (What to run next).\n", section);

        Assert.Contains("**U2. Zeta Prime was slower than the dearer Nova Lite: ", section);
        Assert.Contains("- *Configuration:* None: only the model differs.\n", section);
        Assert.Contains("- *Time:* Number of model calls, 60% (40–80%) of the time difference (Appendix B).\n", section);
        Assert.Contains("- *Settled by:* Check 1 (What to run next).\n", section);

        // The two results are one pair, which had no paired test.
        Assert.Contains("1 of these pairs had no paired test: the paired tests cover the reference pairs, and every pair only up to 6 models (Appendix A). "
            + "Established here means the two 95% intervals do not overlap.\n", section);
    }

    [Fact]
    public void UnexpectedResults_SayWhichPriorsWereChecked_OrWhyNoneCouldBe()
    {
        // The fixture's five models are of five providers.
        Assert.Contains("No unexpected results could be looked for: no two models share a provider.\n",
            RcaSection(BenchmarkReportPackRenderer.Render(RcaDocument(), RcaNamed), "## Unexpected results"));

        string checkedPriors = RcaSection(BenchmarkReportPackRenderer.Render(RcaDocument(s =>
        {
            s.RootCause!.Anomalies = new List<RootCauseAnomaly>();
            s.RootCause.PriorsChecked = new List<string> { RootCausePriors.PriceTierQuality, RootCausePriors.PriceTierSpeed };
        }), RcaNamed), "## Unexpected results");
        Assert.Contains("No unexpected results: every ordering the priors predict either holds or is within noise. Checked: the price-tier order of quality "
            + "and the price-tier order of speed.\n", checkedPriors);

        // A result stored without the anomalies, and model scope: no section.
        Assert.DoesNotContain("## Unexpected results", BenchmarkReportPackRenderer.Render(RcaDocument(s => s.RootCause!.Anomalies = null), RcaNamed));
        Assert.DoesNotContain("## Unexpected results", BenchmarkReportPackRenderer.Render(
            RootCauseReportFixture.ModelDocument(RootCauseReportFixture.ModelPackSheet(), BenchmarkReportDocumentOrigin.ReportPack), RcaNamed));
    }

    // --- Ranks only among two or more models ----------------------------------------------------

    /// <summary>The model-scope sheet of the fixture's first model against its second alone: two models.</summary>
    private static BenchmarkReportFactSheet RcaTwoModelSheet()
    {
        var all = BenchmarkReportPackFixture.FiveModelComparison();
        var keys = new[] { "run:" + RootCauseReportFixture.SubjectRunId, "run:" + BenchmarkReportPackFixture.ComparisonModels[1].RunId };
        var comparison = BenchmarkReportPackFixture.Comparison(all.Entries.Where(e => keys.Contains(e.Key)).ToArray());
        comparison.ComputedAtUtc = all.ComputedAtUtc;
        var built = BenchmarkReportFacts.Build(new BenchmarkReportFactsInput
        {
            Comparison = comparison,
            SubjectKey = keys[0],
            Runs = RootCauseReportFixture.Runs(),
            CandidatePricing = RootCauseReportFixture.Pricing(),
            Catalog = RootCauseReportFixture.Catalog()
        });
        Assert.True(built.Sheet != null, built.Refusal);
        Assert.Single(built.Sheet!.Peers);
        return built.Sheet!;
    }

    [Fact]
    public void AStandAloneRootCauseAnalysis_PrintsNoRank_WhileOneOfTwoOrMoreModelsDoes()
    {
        var alone = new[]
        {
            RootCauseReportFixture.ModelDocument(RootCauseReportFixture.RunSheet(controls: false), BenchmarkReportDocumentOrigin.RunCompletion),
            RootCauseReportFixture.ModelDocument(RootCauseReportFixture.RunSheet(controls: true), BenchmarkReportDocumentOrigin.RunCompletion),
            RootCauseReportFixture.BatteryDocument()
        };
        foreach (var document in alone)
        {
            string text = BenchmarkReportPackRenderer.Render(document, RcaNamed);
            Assert.DoesNotContain(", rank ", text);
            Assert.DoesNotContain("| Rank |", text);
            Assert.DoesNotContain("is a tied rank", text);
            Assert.Contains("| Model | Intelligence Index | 95% interval | Critical errors |\n|---|---|---|---|\n", RcaSection(text, "## Evidence: intelligence"));
        }

        var several = new[]
        {
            RootCauseReportFixture.ModelDocument(RcaTwoModelSheet(), BenchmarkReportDocumentOrigin.ReportPack),
            RootCauseReportFixture.ModelDocument(RootCauseReportFixture.ModelPackSheet(), BenchmarkReportDocumentOrigin.ReportPack),
            RcaDocument()
        };
        foreach (var document in several)
        {
            string text = BenchmarkReportPackRenderer.Render(document, RcaNamed);
            Assert.Contains(", rank ", RcaSection(text, "## Why each model scored as it did"));
            Assert.Contains("| Model | Intelligence Index | 95% interval | Rank | Critical errors |\n|---|---|---|---|---|\n", RcaSection(text, "## Evidence: intelligence"));
            Assert.Contains("is a tied rank", RcaSection(text, "## Evidence: intelligence"));
        }
    }

    // --- Evidence: cost, hypotheses, conclusions and appendices -----------------------------------

    /// <summary>A cost component of a decomposition.</summary>
    private static RootCauseComponent RcaPart(string key, string effect, string label, double amount, double share)
        => new() { Key = key, Effect = effect, Label = label, Amount = amount, Share = share };

    /// <summary>
    /// The fixture comparison with a reference family of two cost tests against Orion Max, and two cost decompositions: Orion Max
    /// against Vega Pro led by token volume, and against Lyra Mini, a grouped pair, led by the price cards.
    /// </summary>
    private static BenchmarkReportDocument RcaCostDocument() => RcaDocument(s =>
    {
        static BenchmarkReportPairedTest Ratio(string second, double effect, double lower, double upper, bool established) => new()
        {
            FirstLetter = "A", SecondLetter = second, PairedItems = 6, EffectKind = "Ratio", Effect = effect, Lower = lower, Upper = upper,
            PValue = established ? 0.001 : 0.3, AdjustedPValue = established ? 0.002 : 0.3, Established = established, Favors = established ? "First" : "None"
        };
        s.PairedTests = new List<BenchmarkReportPairedFamily>
        {
            new()
            {
                Mode = "Reference",
                ReferenceLetter = "A",
                Measures = new List<BenchmarkReportPairedMeasure>
                {
                    new()
                    {
                        Measure = BenchmarkPairedTests.CostMeasure, FamilySize = 2, Adjustment = "Holm",
                        Pairs = new List<BenchmarkReportPairedTest> { Ratio("B", 0.5, 0.4, 0.625, true), Ratio("C", 0.8, 0.5, 1.25, false) }
                    }
                }
            }
        };
        s.RootCause!.CostPairs = new List<RootCausePairDecomposition>
        {
            new()
            {
                Axis = RootCauseAxes.Cost, First = "A", Second = "B", SharedQuestions = 6, Difference = -0.010, FirstTotal = 0.020, SecondTotal = 0.030, VolumeRatio = 0.8,
                Components = new List<RootCauseComponent>
                {
                    RcaPart(RootCauseSegments.UncachedInput, RootCauseCostKeys.Price, "uncached input, price per token", -0.004, 0.4),
                    RcaPart(RootCauseSegments.Output, RootCauseCostKeys.Volume, "visible output, token volume", -0.006, 0.6),
                    RcaPart(RootCauseCostKeys.All, RootCauseCostKeys.Volume, "token volume, all components", -0.006, 0.6),
                    RcaPart(RootCauseCostKeys.All, RootCauseCostKeys.Price, "price per token, all components", -0.004, 0.4)
                }
            },
            new()
            {
                Axis = RootCauseAxes.Cost, First = "A", Second = "C", SharedQuestions = 6, Difference = -0.020, FirstTotal = 0.020, SecondTotal = 0.040, Grouped = true,
                Components = new List<RootCauseComponent>
                {
                    RcaPart(RootCauseCostKeys.Input, RootCauseCostKeys.Price, "input, price card", -0.014, 0.7),
                    RcaPart(RootCauseCostKeys.Output, RootCauseCostKeys.Volume, "output (with reasoning), token volume", -0.005, 0.25),
                    RcaPart(RootCauseCostKeys.Input, RootCauseCostKeys.Caching, "input, caching", -0.001, 0.05),
                    RcaPart(RootCauseCostKeys.All, RootCauseCostKeys.Volume, "token volume, all components", -0.005, 0.25),
                    RcaPart(RootCauseCostKeys.All, RootCauseCostKeys.Price, "price per token, all components", -0.014, 0.7),
                    RcaPart(RootCauseCostKeys.All, RootCauseCostKeys.Caching, "caching, all components", -0.001, 0.05)
                }
            }
        };
    });

    [Fact]
    public void TheCostEvidence_GivesTheRatioAtEqualPrices_AndListsThePairsThePriceCardsLead()
    {
        var document = RcaCostDocument();
        string cost = RcaSection(BenchmarkReportPackRenderer.Render(document, RcaNamed), "## Evidence: cost");
        string a = RcaLabel(document, "A"), b = RcaLabel(document, "B"), c = RcaLabel(document, "C");

        Assert.Contains("| Pair | Spend ratio (95% interval) | At equal prices | Result |\n", cost);
        // An established pair names the cheaper model in its Result cell, as the speed and intelligence tables do.
        Assert.Contains("| " + b + " vs " + a + " | 2.00 (1.60 to 2.50) | 1.25 | " + a + " was cheaper |\n", cost);
        Assert.Contains("| " + c + " vs " + a + " | 1.25 (0.80 to 2.00) | — | Not established |\n", cost);
        Assert.Contains("Spend ratios include the price cards. At equal prices is the token-volume ratio, the part the price cards do not set.", cost);

        // The pair the price cards lead gets no bullet of its own; the other names its leading component that is no price effect.
        Assert.Contains("- **" + a + " vs " + b + ":** ", cost);
        Assert.Contains("; leading component visible output, token volume, ", cost);
        Assert.Contains("Mostly the price cards: " + a + " vs " + c + ".\n", cost);
        Assert.DoesNotContain("- **" + a + " vs " + c + ":**", cost);
    }

    [Fact]
    public void AppendixB_GivesOneRowPerDecomposedPair()
    {
        var document = RcaDocument(s =>
        {
            var source = BenchmarkReportJson.Deserialize<BenchmarkReportFactSheet>(RcaCostDocument().FactsJson);
            s.RootCause!.CostPairs = source.RootCause!.CostPairs;
            s.RootCause.SpeedPairs = new List<RootCausePairDecomposition>
            {
                new()
                {
                    Axis = RootCauseAxes.Speed, First = "A", Second = "B", SharedQuestions = 6, Difference = 3_000,
                    Components = new List<RootCauseComponent>
                    {
                        new() { Key = "callCount", Label = "number of model calls", Amount = 1_500, Share = 0.5, ShareLower = 0.3, ShareUpper = 0.7 },
                        new() { Key = "perCallLatency", Label = "latency per call (time to first token)", Amount = 600, Share = 0.2 },
                        new() { Key = "outputTokens", Label = "output tokens", Amount = 600, Share = 0.2 },
                        new() { Key = "decodeRate", Label = "decode rate", Amount = 150, Share = 0.05 },
                        new() { Key = "gap", Label = "time outside the calls", Amount = 150, Share = 0.05 }
                    }
                }
            };
        });
        string appendix = RcaSection(BenchmarkReportPackRenderer.Render(document, RcaNamed), "## Appendix B: Speed and cost decompositions");
        string costs = RcaSection(appendix, "### Cost differences, decomposed");
        string speeds = RcaSection(appendix, "### Speed differences, decomposed");
        static int Rows(string table) => table.Split('\n').Count(l => l.StartsWith("| ", StringComparison.Ordinal) && !l.StartsWith("| Pair |", StringComparison.Ordinal));

        Assert.Contains("| Pair | Questions | Difference | Token volume | Price card | Caching | Leading component |\n", costs);
        Assert.Equal(2, Rows(costs));
        Assert.Contains(" | 60% | 40% | — | Visible output, token volume (60%) |\n", costs);
        Assert.Contains(" | 25% | 70% | 5% | Input, price card (70%) |\n", costs);
        Assert.DoesNotContain("| Component |", costs);

        Assert.Contains("| Pair | Questions | Difference | Model calls | Time to first token | Output tokens | Decode rate | Other |\n", speeds);
        Assert.Equal(1, Rows(speeds));
        Assert.Contains(" | 50% (30–70%) | 20% | 20% | 5% | 5% |\n", speeds);
    }

    [Fact]
    public void TheHypothesisTables_LeaveOutThePriceEffects_AndStateTheNullsInOneSentence()
    {
        static RootCauseHypothesis Null(string axis, int number, string first, string second, string grade) => new()
        {
            Id = axis == RootCauseAxes.Cost ? "H0-C" : axis == RootCauseAxes.Speed ? "H0-S" : "H0-Q",
            Number = number,
            Key = (axis == RootCauseAxes.Cost ? "H0-C." : axis == RootCauseAxes.Speed ? "H0-S." : "H0-Q.") + number.ToString(CultureInfo.InvariantCulture),
            Axis = axis,
            Kind = "null",
            Models = new List<string> { first, second },
            Statement = "The difference between the two models is chance.",
            Grade = grade
        };
        var document = RcaDocument(s =>
        {
            var hypotheses = s.RootCause!.Hypotheses;
            hypotheses.RemoveAll(h => h.Kind == "null");
            var pairs = new[] { ("A", "B"), ("A", "C"), ("A", "D"), ("A", "E"), ("B", "C"), ("B", "D"), ("B", "E") };
            for (int i = 0; i < pairs.Length; i++) hypotheses.Add(Null(RootCauseAxes.Intelligence, i + 1, pairs[i].Item1, pairs[i].Item2, RootCauseGrades.Low));
            hypotheses.Add(Null(RootCauseAxes.Intelligence, 8, "C", "D", RootCauseGrades.NotSupported));
            hypotheses.Add(Null(RootCauseAxes.Speed, 1, "A", "B", RootCauseGrades.Low));
            hypotheses.Add(Null(RootCauseAxes.Speed, 2, "A", "C", RootCauseGrades.Low));
            hypotheses.Add(Null(RootCauseAxes.Cost, 1, "A", "B", RootCauseGrades.NotSupported));
            hypotheses.Add(Null(RootCauseAxes.Cost, 2, "A", "C", RootCauseGrades.NotSupported));
        });
        var sheet = BenchmarkReportJson.Deserialize<BenchmarkReportFactSheet>(document.FactsJson);
        string text = BenchmarkReportPackRenderer.Render(document, RcaNamed);
        string section = RcaSection(text, "## Hypotheses");
        string Axis(string axis) => RcaSection(section, "### " + RootCauseAxes.Name(axis));
        string Name(string letter) => sheet.Peers.Single(p => p.Letter == letter).Label;

        var price = sheet.RootCause!.Hypotheses.Where(h => h.Axis == RootCauseAxes.Cost && h.Kind != "null" && h.PriceEffect).ToList();
        Assert.True(price.Count > 1, "the fixture is expected to have several price effects");
        foreach (var h in price) Assert.DoesNotContain("| " + h.Key + " |", Axis(RootCauseAxes.Cost));
        Assert.Contains(price.Count.ToString(CultureInfo.InvariantCulture) + " cost differences follow mainly from the price cards ("
            + RootCauseHypotheses.KeyRanges(price.Select(h => h.Key).ToList()) + "); Appendix C lists them.\n", Axis(RootCauseAxes.Cost));
        Assert.Contains("**" + RootCauseHypotheses.KeyRanges(price.Select(h => h.Key).ToList()) + "**", RcaSection(text, "## Appendix C: Hypothesis details"));

        Assert.Contains("7 differences may be chance: " + Name("A") + " vs " + Name("B") + "; " + Name("A") + " vs " + Name("C") + "; " + Name("A") + " vs "
            + Name("D") + "; " + Name("A") + " vs " + Name("E") + "; " + Name("B") + " vs " + Name("C") + " and 2 more (Appendix C); the other is real.\n",
            Axis(RootCauseAxes.Intelligence));
        Assert.Contains("No tested difference could be shown to be real; each may be chance.\n", Axis(RootCauseAxes.Speed));
        Assert.Contains("Every tested difference is real (H0-C.1–H0-C.2).\n", Axis(RootCauseAxes.Cost));
        Assert.DoesNotContain("A real difference was found", section);
        Assert.DoesNotContain("No real difference could be shown", section);
    }

    [Fact]
    public void AQuietAxis_PrintsItsLine_AndNoNotWrittenForTheSlotTheWriterLeftOut()
    {
        var document = RcaDocument(
            s =>
            {
                foreach (var h in s.RootCause!.Hypotheses.Where(h => h.Axis == RootCauseAxes.Cost && h.Kind != "null")) h.PriceEffect = true;
            },
            writer: w =>
            {
                w.Sections.Remove(BenchmarkReportSlots.HypothesesCost);
                w.Sections.Remove(BenchmarkReportSlots.HypothesesSpeed);
            });
        string text = BenchmarkReportPackRenderer.Render(document, RcaNamed);
        string section = RcaSection(text, "## Hypotheses");
        string Axis(string axis) => RcaSection(section, "### " + RootCauseAxes.Name(axis));

        // Only price effects and nulls fired on cost: the line says so, and the left-out slot prints nothing.
        string cost = Axis(RootCauseAxes.Cost);
        Assert.Contains(" cost differences follow mainly from the price cards (", cost);
        Assert.Contains("\nNo hypothesis other than a null or a price-card difference fired on this axis.\n", cost);
        Assert.DoesNotContain("No hypothesis other than a null fired on this axis.", cost);
        Assert.DoesNotContain("*Not written.*", cost);
        Assert.DoesNotContain("| Hypothesis |", cost);

        // Speed has hypotheses that fired, so its missing slot still shows.
        string speed = Axis(RootCauseAxes.Speed);
        Assert.Contains("*Not written.*", speed);
        Assert.DoesNotContain("No hypothesis other than a null", speed);
    }

    [Fact]
    public void TheConclusions_StateAReasonEveryAxisShares_OnceUnderTheTable()
    {
        const string reason = "Probable (Moderate) because each model ran only once per suite.";
        string With(Action<List<RootCauseAxisStatus>> axes) => RcaSection(BenchmarkReportPackRenderer.Render(RcaDocument(s => axes(s.RootCause!.Axes)), RcaNamed), "## Conclusions");
        static List<string> Rows(string section) => section.Split('\n')
            .Where(l => l.StartsWith("| ", StringComparison.Ordinal) && !l.StartsWith("| Axis |", StringComparison.Ordinal))
            .ToList();

        string shared = With(axes => { foreach (var axis in axes) axis.Reason = reason; });
        Assert.Contains("\nEach grade is capped at Probable (Moderate) because each model ran only once per suite.\n", shared);
        Assert.NotEmpty(Rows(shared));
        Assert.All(Rows(shared), row => Assert.EndsWith(" | Probable (Moderate) |", row));
        Assert.DoesNotContain("known from the price cards, explains", shared);

        string different = With(axes =>
        {
            foreach (var axis in axes) axis.Reason = reason;
            axes[^1].Reason = "No rule found a possible cause on this axis.";
        });
        Assert.DoesNotContain("Each grade is capped at", different);
        Assert.Contains(Rows(different), row => row.EndsWith(" | " + reason + " |", StringComparison.Ordinal));
    }

    [Fact]
    public void AppendixA_LeavesOutTheConfigurationDifferencesAppendixEStates_AndPointsToThem()
    {
        int number = 0;
        var document = RcaDocument(s =>
        {
            string stated = BenchmarkComparisonReportFacts.PairPrefix("A", "B") + "configDifferences";
            s.Facts.RemoveAll(f => f.Key.EndsWith(".configDifferences", StringComparison.Ordinal));
            s.Facts.Add(new BenchmarkReportFact { Key = stated, Value = JsonValue.Create("thinking level (high vs medium)"), Display = "thinking level (high vs medium)" });
            s.Facts.Add(new BenchmarkReportFact
            {
                Key = BenchmarkComparisonReportFacts.PairPrefix("A", "C") + "configDifferences",
                Value = JsonValue.Create("reasoning mode (summary vs none)"),
                Display = "reasoning mode (summary vs none)"
            });
            var known = s.RootCause!.KnownFacts;
            number = known.Count == 0 ? 1 : known.Max(k => k.Number) + 1;
            known.Add(new RootCauseKnownFact
            {
                Number = number, Kind = RootCauseKnownFacts.KindConfiguration, Text = "Model A and Model B differ on thinking level.", FactKeys = new List<string> { stated }
            });
        });
        string text = BenchmarkReportPackRenderer.Render(document, RcaNamed);
        string configuration = RcaSection(RcaSection(text, "## Appendix A: Intelligence detail"), "### Configuration differences");

        Assert.DoesNotContain("high vs medium", configuration);
        Assert.Contains("easoning mode (summary vs none)", configuration);
        Assert.Contains("- The configuration differences between the analyzed pairs are Appendix E's known fact " + number.ToString(CultureInfo.InvariantCulture) + ".\n",
            configuration);
        Assert.Contains("- **" + number.ToString(CultureInfo.InvariantCulture) + ".** " + RcaLabel(document, "A") + " and " + RcaLabel(document, "B")
            + " differ on thinking level.\n", RcaSection(text, "## Appendix E: Known facts"));
    }

    [Fact]
    public void AppendixC_GivesARuleThatFiredForSeveralPairs_AsOneEntryWithATable()
    {
        var document = RcaDocument();
        var sheet = BenchmarkReportJson.Deserialize<BenchmarkReportFactSheet>(document.FactsJson);
        var rule = sheet.RootCause!.Hypotheses.Where(h => h.Id == "HC2" && h.Kind == "pair" && h.Models.Count == 2).ToList();
        Assert.True(rule.Count > 1, "the fixture is expected to fire HC2 for several pairs");
        Assert.Single(rule.Select(h => h.Locus).Distinct(StringComparer.Ordinal));
        string Name(string letter) => sheet.Peers.Single(p => p.Letter == letter).Label;

        string appendix = RcaSection(RcaSection(BenchmarkReportPackRenderer.Render(document, RcaNamed), "## Appendix C: Hypothesis details"),
            "### " + RootCauseAxes.Name(RootCauseAxes.Cost));

        Assert.Contains("- **" + RootCauseHypotheses.KeyRanges(rule.Select(h => h.Key).ToList()) + "** (" + rule.Count.ToString(CultureInfo.InvariantCulture)
            + " pairs; locus " + rule[0].Locus + "): The first model costs more than the second model mainly from its price per token.\n", appendix);
        Assert.Contains("| Hypothesis | Pair | Share (95% interval) | Grade | Settled by |\n", appendix);
        foreach (var h in rule)
        {
            Assert.Contains("| " + h.Key + " | " + Name(h.Models[0]) + " vs " + Name(h.Models[1]) + " | ", appendix);
            Assert.DoesNotContain("- **" + h.Key + ": ", appendix);
        }
        if (rule.All(h => h.RefutedIf == rule[0].RefutedIf))
        {
            Assert.Equal(1, RcaCount(appendix, "  - *Refuted if:* " + BenchmarkReportSentenceCase.UpperFirst(rule[0].RefutedIf) + "\n"));
        }
    }

    [Fact]
    public void AppendixG_NamesNoQuestionAsInTheMatrixOnly_WhileItDetailsEveryCitedQuestion()
    {
        Assert.DoesNotContain("Also cited, in the question matrix only", BenchmarkReportPackRenderer.Render(RcaDocument(), RcaNamed));
    }

    [Fact]
    public void TheGraderFamilySentence_IsUnderWhatWeFound_AndTheCaveatPointsToIt()
    {
        var document = RcaDocument(s =>
        {
            var ordered = s.Peers.OrderBy(p => p.Letter, StringComparer.Ordinal).ToList();
            s.RootCause!.FamilyGaps = new List<RootCauseFamilyGap>
            {
                new()
                {
                    Provider1 = ordered[0].Provider, Provider2 = ordered[1].Provider, PairedQuestionCount = 12, IsMemberProviderPair = true,
                    GapA = new RootCauseGapEstimate { Value = 3.9, Lower = 1.2, Upper = 6.6 },
                    GapB = new RootCauseGapEstimate { Value = -1.2, Lower = -3.0, Upper = 0.6 },
                    GapPanel = new RootCauseGapEstimate { Value = 1.4, Lower = 0.1, Upper = 2.7 },
                    Contrast = new RootCauseGapEstimate { Value = 5.1, Lower = 2.0, Upper = 8.2 }
                }
            };
        });
        var peers = BenchmarkReportJson.Deserialize<BenchmarkReportFactSheet>(document.FactsJson).Peers.OrderBy(p => p.Letter, StringComparer.Ordinal).ToList();
        const string pointer = "> The order between models of those two providers depends on the panel's balance; see What we found.\n";

        string named = BenchmarkReportPackRenderer.Render(document, RcaNamed);
        Assert.Contains("- Grader family: the quality gap of models from " + peers[0].Provider + " minus models from " + peers[1].Provider
            + ", the two panel members' own providers, is +3.9 points (95% interval +1.2 to +6.6) under Panel member A and -1.2 points (95% interval "
            + "-3.0 to +0.6) under Panel member B, a contrast of +5.1 points (95% interval +2.0 to +8.2). The order between models of those two "
            + "providers depends on the panel's balance.\n", RcaSection(named, "### What we found"));
        string caveat = RcaSection(named, "### How sure are we?");
        Assert.Contains(pointer, caveat);
        Assert.DoesNotContain("Grader family:", caveat);

        string anonymized = BenchmarkReportPackRenderer.Render(document, RcaAnonymized);
        Assert.Contains("- Grader family: the quality gap of models from one panel member's provider minus models from the other member's provider is +3.9 points",
            RcaSection(anonymized, "### What we found"));
        Assert.DoesNotContain(peers[0].Provider, RcaSection(anonymized, "## Summary"));
        Assert.Contains(pointer, RcaSection(anonymized, "### How sure are we?"));

        // A contrast whose interval includes zero, and a result stored without the gaps, print neither.
        var included = RcaDocument(s => s.RootCause!.FamilyGaps = new List<RootCauseFamilyGap>
        {
            new() { Provider1 = "P", Provider2 = "Q", IsMemberProviderPair = true, GapA = new() { Value = 1 }, GapB = new() { Value = 0 }, Contrast = new() { Value = 1, Lower = -1, Upper = 3 } }
        });
        foreach (string text in new[] { BenchmarkReportPackRenderer.Render(included, RcaNamed), BenchmarkReportPackRenderer.Render(RcaDocument(), RcaNamed) })
        {
            Assert.DoesNotContain("Grader family:", text);
            Assert.DoesNotContain("see What we found.", text);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheSummary_HasNoResearchQuestions(bool comparison)
    {
        static BenchmarkReportDocument Fresh(bool comparison) => comparison
            ? RcaDocument()
            : RootCauseReportFixture.ModelDocument(RootCauseReportFixture.ModelPackSheet(), BenchmarkReportDocumentOrigin.ReportPack);
        static void AssertNoQuestions(string text)
        {
            string summary = RcaSection(text, "## Summary");
            Assert.DoesNotContain("### The questions", summary);
            Assert.DoesNotContain("Answer strength", summary);
            Assert.DoesNotContain("**Question.**", summary);
            Assert.DoesNotContain("*Measured signals:*", summary);
        }

        var current = Fresh(comparison);
        Assert.NotEmpty(BenchmarkReportJson.Deserialize<BenchmarkReportFactSheet>(current.FactsJson).RootCause!.Questions);
        string text = BenchmarkReportPackRenderer.Render(current, RcaNamed);
        AssertNoQuestions(text);

        // A document stored under the previous format, its answers in its writer output, renders the same way, and a question
        // cited only in an answer is not among Appendix G's cited questions.
        var stored = Fresh(comparison);
        stored.ReportFormatVersion = comparison ? 24 : 20;
        var sheet = BenchmarkReportJson.Deserialize<BenchmarkReportFactSheet>(stored.FactsJson);
        var writer = BenchmarkReportJson.Deserialize<BenchmarkReportWriterOutput>(stored.WriterOutputJson);
        var before = BenchmarkReportPackValidator.CitedQuestions(writer, sheet);
        var uncited = sheet.Questions.First(q => !before.Contains(q.Number));
        string reference = string.IsNullOrWhiteSpace(uncited.Reference) ? "Q" + uncited.Number.ToString(CultureInfo.InvariantCulture) : uncited.Reference!;
        foreach (string slot in BenchmarkReportSlots.RootCauseAnswerSlots) writer.Sections[slot] = "A stored answer about " + reference + ".";
        stored.WriterOutputJson = BenchmarkReportJson.Serialize(writer);
        Assert.Equal(before, BenchmarkReportPackValidator.CitedQuestions(writer, sheet));

        string old = BenchmarkReportPackRenderer.Render(stored, RcaNamed);
        AssertNoQuestions(old);
        Assert.DoesNotContain("A stored answer", old);
        static string Appendix(string t) => t[t.IndexOf("\n## Appendix G", StringComparison.Ordinal)..];
        var unanswered = Fresh(comparison);
        unanswered.ReportFormatVersion = stored.ReportFormatVersion;
        Assert.Equal(Appendix(BenchmarkReportPackRenderer.Render(unanswered, RcaNamed)), Appendix(old));
    }

    [Fact]
    public void AppendixD_ListsWhatARunDecides_AndNamesItsGraders_ButAnAnonymizedCopyGivesAnUnknownGradersIdAlone()
    {
        var document = RcaDocument(sheet =>
        {
            var run = sheet.RootCause!.RecommendedRuns[0];
            run.DecidesItems = new List<string> { "Confirms HQ1.1 if the gap stays.", "Refutes HS2.1 if the time ratio falls below 1." };
            run.AssessorModelConfigurationId = 19;
            run.AssessorName = "Northwind Grader @ medium";
            run.ScoringProfileId = 1;
            run.ScoringProfileName = "Default profile";
        });

        string named = RcaSection(BenchmarkReportPackRenderer.Render(document, RcaNamed), "## Appendix D: Run settings");
        Assert.Contains("- **Decides:**\n  - Confirms HQ1.1 if the gap stays.\n  - Refutes HS2.1 if the time ratio falls below 1.\n", named);
        Assert.Contains("| Assessor | Northwind Grader @ medium (configuration 19) |\n", named);
        Assert.Contains("| Scoring Profile | Default profile (profile 1) |\n", named);

        string anonymized = RcaSection(BenchmarkReportPackRenderer.Render(document, RcaAnonymized), "## Appendix D: Run settings");
        Assert.Contains("| Assessor | Configuration 19 |\n", anonymized);
        Assert.DoesNotContain("Northwind Grader", anonymized);
    }

    [Fact]
    public void AStoredFormat19RootCauseAnalysis_WithoutTheNewFields_RendersWithTheWordingItHadThen()
    {
        var document = RcaDocument(
            s =>
            {
                s.RootCause!.FamilyGaps = null;
                foreach (var stored in s.RootCause.RecommendedRuns)
                {
                    stored.DecidesItems = null;
                    stored.ScoringProfileName = stored.AssessorName = stored.CoAssessorName = stored.ReferenceReaderName = stored.ClaimVerifierName = null;
                }
            },
            c =>
            {
                foreach (var item in c.Runs.SelectMany(r => r.Questions)) item.CriticalErrorResolution = null;
            });
        document.ReportFormatVersion = 19;
        var sheet = BenchmarkReportJson.Deserialize<BenchmarkReportFactSheet>(document.FactsJson);

        string text = BenchmarkReportPackRenderer.Render(document, RcaNamed);

        Assert.Contains(" · generated under format version 19 · rendered with format version 30 · ", text);
        Assert.DoesNotContain("Grader family:", text);
        foreach (string resolution in new[] { "agreed by both members", "upheld by the claim verifier", "overturned by the claim verifier", "unresolved (averaged)", "single assessor" })
        {
            Assert.DoesNotContain("**Critical error:** " + resolution, text, StringComparison.OrdinalIgnoreCase);
        }
        Assert.DoesNotContain("*Critical errors:*", text);
        Assert.Contains("*Each answer shown is one the writer was given as an excerpt; the excerpts were chosen for the questions with critical errors "
            + "or refuted sentences first, then the widest spread between models, then those every model scored low.", text);

        // The anonymized copy prints a run's text as stored, so the joined Decides reads verbatim.
        string settings = RcaSection(BenchmarkReportPackRenderer.Render(document, RcaAnonymized), "## Appendix D: Run settings");
        foreach (var run in sheet.RootCause!.RecommendedRuns)
        {
            Assert.Contains("- **Decides:** " + BenchmarkReportSentenceCase.UpperFirst(run.Decides.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd().Replace('\n', ' ')) + "\n", settings);
            if (run.AssessorModelConfigurationId is long id)
            {
                Assert.Contains("| Assessor | Configuration " + id.ToString(CultureInfo.InvariantCulture) + " |\n", settings);
            }
        }
    }

    [Fact]
    public void TheComparisonRootCauseAnalysis_PlacesNoIntelligenceFigure_WhileTheModelScopeKeepsIt()
    {
        var rca = BenchmarkReportAudience.RootCauseAnalysis;
        Assert.Equal(new[] { BenchmarkReportChartPlacement.RcaPointsAtRiskKey },
            BenchmarkReportChartPlacement.KeysAt(rca, BenchmarkReportChartAnchor.RcaIntelligence, BenchmarkReportScope.Comparison));
        Assert.Equal(new[] { BenchmarkReportChartPlacement.QualityKey, BenchmarkReportChartPlacement.RcaPointsAtRiskKey },
            BenchmarkReportChartPlacement.KeysAt(rca, BenchmarkReportChartAnchor.RcaIntelligence, BenchmarkReportScope.Model));
        Assert.Null(BenchmarkReportChartPlacement.AnchorOf(rca, BenchmarkReportChartPlacement.QualityKey, BenchmarkReportScope.Comparison));
        Assert.Equal(BenchmarkReportChartAnchor.RcaIntelligence, BenchmarkReportChartPlacement.AnchorOf(rca, BenchmarkReportChartPlacement.QualityKey));
        Assert.Equal(new[] { BenchmarkReportChartPlacement.SpeedKey, BenchmarkReportChartPlacement.QualitySpeedKey, BenchmarkReportChartPlacement.RcaSpeedKey },
            BenchmarkReportChartPlacement.KeysAt(rca, BenchmarkReportChartAnchor.RcaSpeed, BenchmarkReportScope.Comparison));
    }

    [Fact]
    public void ANamedComparisonCopy_NamesTheModelsAWriterLeftAsBareLetters()
    {
        var document = ComparisonDocument(BenchmarkReportAudience.ExecutiveSummary);
        var sheet = BenchmarkReportJson.Deserialize<BenchmarkReportFactSheet>(document.FactsJson);
        var peers = sheet.Peers.OrderBy(p => p.Letter, StringComparer.Ordinal).Take(2).ToList();
        var writer = BenchmarkReportJson.Deserialize<BenchmarkReportWriterOutput>(document.WriterOutputJson);
        writer.Headline = "Models " + peers[0].Letter + " and " + peers[1].Letter + " lose Accuracy, and Model Q is no peer.";
        document.WriterOutputJson = BenchmarkReportJson.Serialize(writer);

        string named = BenchmarkReportPackRenderer.Render(document,
            new BenchmarkReportRenderOptions { Disclosure = BenchmarkReportDisclosure.Full, PeerNaming = BenchmarkReportPeerNaming.Named });
        string anonymized = BenchmarkReportPackRenderer.Render(document,
            new BenchmarkReportRenderOptions { Disclosure = BenchmarkReportDisclosure.Full, PeerNaming = BenchmarkReportPeerNaming.Anonymized });

        Assert.Contains(peers[0].Label + " and " + peers[1].Label + " lose Accuracy, and Model Q is no peer.", named);
        Assert.Contains("Models " + peers[0].Letter + " and " + peers[1].Letter + " lose Accuracy", anonymized);
    }

    public static IEnumerable<object[]> ComparisonBaselineGoldenCombinations() => ComparisonBaselineCombinations();

    [Theory]
    [MemberData(nameof(ComparisonBaselineGoldenCombinations))]
    public void TheBaselineComparison_MatchesItsGoldenFile(
        BenchmarkReportAudience audience, BenchmarkReportDisclosure disclosure, BenchmarkReportPeerNaming naming, string file)
    {
        string text = BenchmarkReportPackRenderer.Render(ComparisonDocument(audience, baseline: true),
            new BenchmarkReportRenderOptions { Disclosure = disclosure, PeerNaming = naming });

        Assert.DoesNotContain("\r", text);
        AssertMatchesGolden(file, text);
    }

    [Fact]
    public void TheBaselineComparison_SetsEveryModelAgainstTheBaselineModel_NamedOrAnonymized()
    {
        var options = new BenchmarkReportRenderOptions { Disclosure = BenchmarkReportDisclosure.Full, PeerNaming = BenchmarkReportPeerNaming.Named };
        string named = BenchmarkReportPackRenderer.Render(ComparisonDocument(BenchmarkReportAudience.ExecutiveSummary, baseline: true), options);

        Assert.Contains("## Compared with the baseline model\n\n**Reference:** Lyra Mini, the baseline model.\n", named);
        Assert.Contains("| Lyra Mini (baseline model, reference) | Eastgate |", named);
        Assert.Contains("Lyra Mini, the baseline model, was compared with each other model", named);
        Assert.DoesNotContain("Model C", named, StringComparison.Ordinal);
        Assert.DoesNotContain("Model A", named, StringComparison.Ordinal);

        string anonymized = BenchmarkReportPackRenderer.Render(ComparisonDocument(BenchmarkReportAudience.ExecutiveSummary, baseline: true),
            new BenchmarkReportRenderOptions { Disclosure = BenchmarkReportDisclosure.Full, PeerNaming = BenchmarkReportPeerNaming.Anonymized });
        Assert.Contains("## Compared with the baseline model\n\n**Reference:** Model C, the baseline model.\n", anonymized);
        Assert.Contains("| Model C (baseline model, reference) |", anonymized);
        Assert.Contains("Model C, the baseline model, was compared with each other model", anonymized);
        Assert.All(ComparisonModels, m => Assert.DoesNotContain(m.Label, anonymized, StringComparison.Ordinal));
        Assert.All(ComparisonModels, m => Assert.DoesNotContain(m.Provider, anonymized, StringComparison.Ordinal));
    }

    [Fact]
    public void TheExecutiveSummary_SetsTheModelsAgainstTheReference_AndListsTheFirstThreeNotableFindings()
    {
        var document = ComparisonDocument(BenchmarkReportAudience.ExecutiveSummary);
        var sheet = BenchmarkReportJson.Deserialize<BenchmarkReportFactSheet>(document.FactsJson);
        string text = RenderDocument(document, BenchmarkReportDisclosure.Summary, BenchmarkReportPeerNaming.Anonymized);

        AssertInOrder(
            text.IndexOf("## How they compare\n", StringComparison.Ordinal),
            text.IndexOf("## Compared with the reference model\n", StringComparison.Ordinal),
            text.IndexOf("## Also worth knowing\n", StringComparison.Ordinal),
            text.IndexOf("## Model by model\n", StringComparison.Ordinal));
        Assert.Contains("| Model | Paired difference (95% interval) | Better / similar / worse | May mislead per 20 | Time ratio of medians | Cost ratio of means |\n", text);
        Assert.DoesNotContain("## Notable findings", text);

        var findings = sheet.NotableFindings!;
        foreach (var finding in findings.Take(3)) Assert.Contains("- " + finding.Summary + "\n", text);
        foreach (var finding in findings.Skip(3)) Assert.DoesNotContain(finding.Summary, text);
    }

    [Fact]
    public void TheReferenceTable_NamesItsRatiosRatiosOfMediansAndMeans_AndThePairFactsSayPairedRatio()
    {
        string text = RenderDocument(ComparisonDocument(BenchmarkReportAudience.ExecutiveSummary), BenchmarkReportDisclosure.Summary, BenchmarkReportPeerNaming.Anonymized);

        Assert.Contains("| Time ratio of medians | Cost ratio of means |\n", text);
        Assert.Contains("Time ratio of medians: the model's median answer time divided by the reference's. Cost ratio of means: the model's mean cost "
            + "per question divided by the reference's. Neither is a paired ratio over the questions both answered.", text);

        var ratios = ComparisonFacts().Sheet!.Facts.Where(f => f.Available && f.Key.StartsWith("pair.", StringComparison.Ordinal)
            && (f.Key.EndsWith(".speed.ratio", StringComparison.Ordinal) || f.Key.EndsWith(".cost.ratio", StringComparison.Ordinal))).ToList();
        Assert.NotEmpty(ratios);
        Assert.All(ratios, f => Assert.Matches(@"^Model [A-Z] (took|cost) \d+\.\d\d times as (long|much) as Model [A-Z] on the same questions \(paired ratio(, 95% interval \d+\.\d\d to \d+\.\d\d)?\)$", f.Display));
    }

    [Fact]
    public void APairsResultInAFamily_NamesBothModels_EstablishedOrNot()
    {
        var comparison = FiveModelComparison();
        var runs = FiveModelRuns();
        var keys = BenchmarkComparisonReportFacts.DefaultCoveredKeys(comparison);
        var (reference, allPairs, unavailable) = BenchmarkComparisonReportFacts.PairedFromRuns(comparison, keys, runs);
        BenchmarkReportFactSheet Build() => BenchmarkComparisonReportFacts.BuildFromRuns(
            comparison, keys, runs, 120, reference, allPairs, unavailable, ComparisonNumber).Sheet!;

        // Every result of either family names the pair's two models, so two in one paragraph read apart.
        var before = Build();
        var results = before.Facts.Where(f => f.Available && f.Key.StartsWith("pair.", StringComparison.Ordinal)
            && (f.Key.EndsWith("." + BenchmarkComparisonReportFacts.ReferenceFamilyName, StringComparison.Ordinal)
                || f.Key.EndsWith("." + BenchmarkComparisonReportFacts.AllPairsFamilyName, StringComparison.Ordinal))).ToList();
        Assert.NotEmpty(results);
        Assert.All(results, f =>
        {
            string[] parts = f.Key.Split('.');
            Assert.Contains("Model " + parts[1], f.Display, StringComparison.Ordinal);
            Assert.Contains("Model " + parts[2], f.Display, StringComparison.Ordinal);
        });
        Assert.StartsWith("no difference in score between Model A and Model B established after the Holm adjustment across ",
            before.Facts.Single(f => f.Key == "pair.A.B.quality.reference").Display);

        // An established result names the model it favors first, then the other.
        string KeyOf(string letter) => before.Peers.Single(p => p.Letter == letter).EntryKey;
        var tested = new[] { KeyOf("A"), KeyOf("C") };
        reference!.Measures.Single(m => m.Measure == BenchmarkPairedTests.IntelligenceMeasure).Pairs
            .Single(p => tested.Contains(p.BaselineKey) && tested.Contains(p.TreatmentKey)).Established = true;
        Assert.StartsWith("Model A scored higher than Model C on the same questions, established after the Holm adjustment across ",
            Build().Facts.Single(f => f.Key == "pair.A.C.quality.reference").Display);
    }

    [Fact]
    public void ACoveredSetWithNothingNotable_PrintsNoAlsoWorthKnowing()
    {
        string text = RenderDocument(ComparisonDocumentOver(BenchmarkReportAudience.ExecutiveSummary, new[] { "run:32", "run:33" }),
            BenchmarkReportDisclosure.Summary, BenchmarkReportPeerNaming.Named);

        Assert.DoesNotContain("## Also worth knowing", text);
        Assert.DoesNotContain(BenchmarkNotableFindings.NothingDetected, text);
    }

    [Theory]
    [InlineData(BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportDisclosure.Full, BenchmarkReportPeerNaming.Named)]
    [InlineData(BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportDisclosure.Summary, BenchmarkReportPeerNaming.Anonymized)]
    public void AComparisonDocumentStoredUnderFormat13_RendersWithoutAnyFormat14Content(
        BenchmarkReportAudience audience, BenchmarkReportDisclosure disclosure, BenchmarkReportPeerNaming naming)
    {
        var options = new BenchmarkReportRenderOptions { Disclosure = disclosure, PeerNaming = naming };
        string text = BenchmarkReportPackRenderer.Render(StoredV13ComparisonDocument(audience), options);

        foreach (string absent in new[]
        {
            "Compared with the", "**Reference:**", "Also worth knowing", "Notable findings", "Why the models differ", "Failure profile",
            "Grader findings by category", "(reference)", "(baseline model)", "## 7.", "May mislead"
        })
        {
            Assert.DoesNotContain(absent, text, StringComparison.Ordinal);
        }
        Assert.Contains("generated under format version 13 · rendered with format version "
            + BenchmarkReportPackRenderer.ComparisonReportFormatVersion.ToString(System.Globalization.CultureInfo.InvariantCulture), text);
        Assert.Contains((naming == BenchmarkReportPeerNaming.Named ? "Orion Max" : "Model A")
            + ", the highest Intelligence Index, was compared with each other model", text);
    }

    // ---------------------------------------------------------------------------------------------
    // Chat consistency documents
    // ---------------------------------------------------------------------------------------------

    /// <summary>A stored chat consistency document of the audience over <see cref="ChatConsistencyReportTestData"/>, with its valid writer output.</summary>
    private static BenchmarkReportDocument ChatConsistencyDocument(BenchmarkReportAudience audience)
    {
        var sheet = ChatConsistencyReportTestData.Sheet(audience);
        return new BenchmarkReportDocument
        {
            Id = 41,
            PackId = Guid.Parse("7c1e2d3f-4a5b-4c6d-8e9f-0a1b2c3d4e5f"),
            Audience = audience,
            Origin = BenchmarkReportDocumentOrigin.ChatConsistencyReport,
            Scope = BenchmarkReportScope.ChatConsistency,
            ChatConsistencyAnalysisId = 7,
            SubjectKey = sheet.SubjectKey,
            SubjectLabel = sheet.SubjectLabel,
            SubjectRunIdsJson = BenchmarkReportJson.Serialize(sheet.SubjectRunIds),
            ComparisonRequestJson = "{}",
            SuiteName = sheet.SuiteName,
            WriterConfigId = 3,
            WriterDisplayName = "Writer One",
            WriterProvider = "Anthropic",
            WriterModelId = "writer-1",
            ReportFormatVersion = BenchmarkReportPackRenderer.ChatConsistencyReportFormatVersion,
            WriterPromptSha256 = BenchmarkReportPackPrompt.PromptSha256(audience, BenchmarkReportScope.ChatConsistency),
            AnswerExcerptChars = 0,
            FactsJson = BenchmarkReportJson.Serialize(sheet),
            ContentJson = BenchmarkReportJson.Serialize(ChatConsistencyReportTestData.Content()),
            WriterOutputJson = BenchmarkReportJson.Serialize(ChatConsistencyReportTestData.ValidOutput(audience)),
            ValidationNotesJson = "[]",
            Title = BenchmarkReportPackRenderer.BuildChatConsistencyTitle(sheet),
            Status = BenchmarkReportDocumentStatus.Completed,
            CreatedAtUtc = CreatedAt
        };
    }

    private static string RenderChatConsistency(
        BenchmarkReportAudience audience, BenchmarkReportDisclosure disclosure, BenchmarkReportPeerNaming naming)
        => BenchmarkReportPackRenderer.Render(ChatConsistencyDocument(audience),
            new BenchmarkReportRenderOptions { Disclosure = disclosure, PeerNaming = naming });

    public static TheoryData<BenchmarkReportAudience, BenchmarkReportDisclosure, BenchmarkReportPeerNaming, string> ChatConsistencyGoldenCombinations => new()
    {
        { BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportDisclosure.Detailed, BenchmarkReportPeerNaming.Anonymized, "chatconsistency_exec.md" },
        { BenchmarkReportAudience.ProviderIssueReport, BenchmarkReportDisclosure.Detailed, BenchmarkReportPeerNaming.Anonymized, "chatconsistency_provider.md" },
    };

    [Theory]
    [MemberData(nameof(ChatConsistencyGoldenCombinations))]
    public void EveryChatConsistencyAudience_Renders_AndMatchesItsGoldenFile(
        BenchmarkReportAudience audience, BenchmarkReportDisclosure disclosure, BenchmarkReportPeerNaming naming, string file)
    {
        Assert.True(BenchmarkReportPackRenderer.IsAllowed(audience, new BenchmarkReportRenderOptions { Disclosure = disclosure, PeerNaming = naming }));

        string first = RenderChatConsistency(audience, disclosure, naming);
        string second = RenderChatConsistency(audience, disclosure, naming);

        AssertSameText(first, second, file + " rendered twice");
        Assert.DoesNotContain("\r", first);
        AssertMatchesGolden(file, first);
    }

    [Fact]
    public void AChatConsistencyDocument_IsCurrentAtFormatVersion7_AndTheOtherScopesKeepTheirs()
    {
        Assert.Equal(7, BenchmarkReportPackRenderer.ChatConsistencyReportFormatVersion);
        Assert.Equal(7, BenchmarkReportPackRenderer.CurrentFormatVersion(BenchmarkReportScope.ChatConsistency));
        Assert.Equal(26, BenchmarkReportPackRenderer.CurrentFormatVersion(BenchmarkReportScope.Model));
        Assert.Equal(30, BenchmarkReportPackRenderer.CurrentFormatVersion(BenchmarkReportScope.Comparison));
        Assert.Equal(26, BenchmarkReportPackRenderer.ReportFormatVersion);
        Assert.Equal(30, BenchmarkReportPackRenderer.ComparisonReportFormatVersion);

        string text = RenderChatConsistency(BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportDisclosure.Full, BenchmarkReportPeerNaming.Named);
        Assert.Contains(" · format version 7 · ", text);
        Assert.Contains(" · controls named*", text);

        string provider = RenderChatConsistency(BenchmarkReportAudience.ProviderIssueReport, BenchmarkReportDisclosure.Full, BenchmarkReportPeerNaming.Anonymized);
        Assert.Contains(" · format version 7 · ", provider);
        Assert.Contains("- **Report format version:** 7\n", provider);
    }

    [Theory]
    [InlineData(BenchmarkReportAudience.ExecutiveSummary)]
    [InlineData(BenchmarkReportAudience.ProviderIssueReport)]
    public void ADocumentOfAnAnalysisSavedUnderEarlierCode_PrintsTheOutOfDateBox_DecidedWhenRendered(BenchmarkReportAudience audience)
    {
        // A format-1 document stored before the box existed, from an analysis saved under code version 4.
        var document = ChatConsistencyDocument(audience);
        document.ReportFormatVersion = 1;
        var result = ChatConsistencyReportTestData.Result() with { AnalysisCodeVersion = 4, PeriodHours = null, PeriodLevels = null };
        document.FactsJson = BenchmarkReportJson.Serialize(BenchmarkChatConsistencyReportFacts.Build(
            result, audience, audience == BenchmarkReportAudience.ProviderIssueReport ? ChatConsistencyReportTestData.RequestIds : null));

        string text = BenchmarkReportPackRenderer.Render(document, new BenchmarkReportRenderOptions { Disclosure = BenchmarkReportDisclosure.Full });

        string current = Overseer.Services.ChatConsistency.ChatConsistencyAnalysisService.CurrentAnalysisCodeVersion.ToString(CultureInfo.InvariantCulture);
        Assert.Contains("> **This document comes from an out-of-date analysis:** It was saved under analysis code version 4, and Overseer now "
            + "analyzes under version " + current + ". Saved analyses never change; analyze the same periods again for a current result before relying on this document.\n", text);
        AssertInOrder(text.IndexOf("> **This document comes from", StringComparison.Ordinal), text.IndexOf("## Overall verdict", StringComparison.Ordinal));
        Assert.DoesNotContain("**Where the chat stands**", text);
    }

    [Fact]
    public void ADocumentWrittenAfterChangedInputsWereConfirmed_SaysSoInItsBox()
    {
        var document = ChatConsistencyDocument(BenchmarkReportAudience.ExecutiveSummary);
        document.FactsJson = BenchmarkReportJson.Serialize(BenchmarkChatConsistencyReportFacts.Build(
            ChatConsistencyReportTestData.Result(), BenchmarkReportAudience.ExecutiveSummary, null, "changed inputs"));

        string text = BenchmarkReportPackRenderer.Render(document, new BenchmarkReportRenderOptions());

        Assert.Contains("> **This document comes from an out-of-date analysis:** Its runs, grades, controls, annotations or prices changed after it was saved. ", text);
    }

    [Fact]
    public void WhereTheChatStands_ListsEachPeriodsLevels_AndMarksAMeasureThatIsNotComparable()
    {
        var baseResult = ChatConsistencyReportTestData.Result();
        var result = baseResult with
        {
            Endpoints = baseResult.Endpoints.Select(e => e.Id == "P3"
                ? e with { NotComputedKind = Overseer.Services.ChatConsistency.ChatConsistencyNotComputedKinds.NoCommonStratum }
                : e).ToList()
        };
        var document = ChatConsistencyDocument(BenchmarkReportAudience.ExecutiveSummary);
        document.FactsJson = BenchmarkReportJson.Serialize(BenchmarkChatConsistencyReportFacts.Build(result, BenchmarkReportAudience.ExecutiveSummary));

        string text = BenchmarkReportPackRenderer.Render(document, new BenchmarkReportRenderOptions());

        Assert.Contains("**Where the chat stands**\n\n| Measure | Baseline | Comparison |\n", text);
        Assert.Contains("| Quality | Mean score 82.4 points | Mean score 78.1 points |\n", text);
        Assert.Contains("| Answer streaming rate (not comparable) | Median 61.2 tokens/s | Median 59.8 tokens/s |\n", text);
        Assert.Contains("| Work per turn (output tokens per answer) | Mean 9,044 output tokens per answer | Mean 8,233 output tokens per answer |\n", text);
        Assert.Contains("| Failed answers | 0 of 40 answers | 1 of 40 answers |\n", text);
        Assert.Contains("*Descriptive levels of each period, not a comparison: the verdicts above say what changed.*\n", text);
        Assert.Contains("- **Answer streaming rate not computable:** Speed cannot be compared: the baseline ran weekdays 04–12 UTC and the comparison weekdays 04–16 UTC, and response times vary with the time of day.\n", text);
    }

    [Fact]
    public void OneUnitPerPeriod_PutsTheCaveatUnderTheEndpointTable()
    {
        var baseResult = ChatConsistencyReportTestData.Result();
        var result = baseResult with
        {
            UnitKind = Overseer.Services.ChatConsistency.ChatConsistencyComparisonSetKinds.BatteryRunUnit,
            Units = new[]
            {
                new Overseer.Services.ChatConsistency.ChatConsistencyUnitView { UnitId = 11, Kind = "batteryRun", Period = "baseline", MemberRunIds = new long[] { 10, 11 } },
                new Overseer.Services.ChatConsistency.ChatConsistencyUnitView { UnitId = 12, Kind = "batteryRun", Period = "comparison", MemberRunIds = new long[] { 20, 21 } }
            }
        };
        var document = ChatConsistencyDocument(BenchmarkReportAudience.ExecutiveSummary);
        document.FactsJson = BenchmarkReportJson.Serialize(BenchmarkChatConsistencyReportFacts.Build(result, BenchmarkReportAudience.ExecutiveSummary));

        string text = BenchmarkReportPackRenderer.Render(document, new BenchmarkReportRenderOptions());

        Assert.Contains("| Measure | Result | What it means |\n", text);
        Assert.Contains("*With one battery run per period, the intervals and smallest detectable changes reflect question-to-question variation only; "
            + "run-to-run variation is not included, so the true uncertainty is larger.*\n", text);
        Assert.DoesNotContain("With one run per period", RenderChatConsistency(BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportDisclosure.Full, BenchmarkReportPeerNaming.Anonymized));
    }

    [Fact]
    public void ABatteryComparison_NamesAnUpdatesRunByItsBatteryRun_AndASameDaySplitPrintsTheBaselineUntilTheComparison()
    {
        var day = new DateTime(2026, 10, 8, 0, 0, 0, DateTimeKind.Utc);
        var split = day.AddHours(14).AddMinutes(49);
        var baseResult = ChatConsistencyReportTestData.Result();
        var result = baseResult with
        {
            UnitKind = Overseer.Services.ChatConsistency.ChatConsistencyComparisonSetKinds.BatteryRunUnit,
            Units = new[]
            {
                new Overseer.Services.ChatConsistency.ChatConsistencyUnitView { UnitId = 11, Kind = "batteryRun", Period = "baseline", MemberRunIds = new long[] { 10, 11 } },
                new Overseer.Services.ChatConsistency.ChatConsistencyUnitView { UnitId = 12, Kind = "batteryRun", Period = "comparison", MemberRunIds = new long[] { 20, 21 } }
            },
            Baseline = baseResult.Baseline with { StartUtc = day, EndUtc = split.AddMilliseconds(-1) },
            Comparison = baseResult.Comparison with { StartUtc = split, EndUtc = day.AddDays(1).AddMilliseconds(-1) }
        };
        var document = ChatConsistencyDocument(BenchmarkReportAudience.ProviderIssueReport);
        document.FactsJson = BenchmarkReportJson.Serialize(BenchmarkChatConsistencyReportFacts.Build(
            result, BenchmarkReportAudience.ProviderIssueReport, ChatConsistencyReportTestData.RequestIds));

        string text = BenchmarkReportPackRenderer.Render(document, new BenchmarkReportRenderOptions());

        Assert.Contains("| E1 | 2026-09-11 00:00 | Battery run #12 (run #20) | Game snapshot: off → on |\n", text);
        // A control's run belongs to no analyzed battery run, so it keeps its run number.
        Assert.Contains("| E2 | 2026-09-12 00:00 | Run #31 | Re-indexed: GnollHack wiki (812 → 815 files) (in the runs of Model A) |\n", text);
        Assert.Contains("- **Baseline period:** 2026-10-08 00:00 UTC until 14:49 UTC, 1 battery run\n", text);
        Assert.Contains("- **Comparison period:** From 2026-10-08 14:49 UTC to 2026-10-08 23:59 UTC, 1 battery run\n", text);
    }

    [Fact]
    public void ReliabilityRatesThatAreAllZero_ShareOneRow()
    {
        var baseResult = ChatConsistencyReportTestData.Result();
        var zero = new[] { ("terminalFailures", "Terminal failures"), ("timeouts", "Timeouts"), ("http429", "429 responses") }
            .Select(rate => new Overseer.Services.ChatConsistency.ChatConsistencyRateResult
            {
                Id = rate.Item1, Name = rate.Item2, Denominator = "answers",
                BaselineCount = 0, BaselineTotal = 40, BaselineRate = 0, ComparisonCount = 0, ComparisonTotal = 40, ComparisonRate = 0, PValue = 1.0
            }).ToList();
        var document = ChatConsistencyDocument(BenchmarkReportAudience.ProviderIssueReport);
        document.FactsJson = BenchmarkReportJson.Serialize(BenchmarkChatConsistencyReportFacts.Build(
            baseResult with { Reliability = zero }, BenchmarkReportAudience.ProviderIssueReport, ChatConsistencyReportTestData.RequestIds));

        string text = BenchmarkReportPackRenderer.Render(document, new BenchmarkReportRenderOptions());

        Assert.Contains("| Measure | Baseline → comparison, or change (95% interval) | Verdict |\n", text);
        Assert.Contains("| Failures of any kind | None in either period (429 responses, terminal failures and timeouts) | No established increase |\n", text);
        Assert.DoesNotContain("| Timeouts |", text);
    }

    [Theory]
    [InlineData(BenchmarkReportAudience.ExecutiveSummary)]
    [InlineData(BenchmarkReportAudience.ProviderIssueReport)]
    public void AChatConsistencyDocument_OpensWithTheOverallVerdict_ThenTheSlotsUnderTheirTitles_AndPrintsItsAudiencesBlocks(
        BenchmarkReportAudience audience)
    {
        string text = RenderChatConsistency(audience, BenchmarkReportDisclosure.Summary, BenchmarkReportPeerNaming.Anonymized);
        int Pos(string part) => text.IndexOf(part, StringComparison.Ordinal);
        bool executive = audience == BenchmarkReportAudience.ExecutiveSummary;

        Assert.StartsWith("# Overseer Chat Consistency Report: Test Model\n", text);
        var slots = BenchmarkReportSlots.For(audience, BenchmarkReportScope.ChatConsistency).RequiredSlots;
        var order = new List<int> { Pos("## Overall verdict\n"), Pos("## The result in one sentence\n") };
        order.AddRange(slots.Select(slot => Pos("## " + BenchmarkReportSlots.ChatConsistencySlotTitles[slot] + "\n")));
        order.Add(Pos("## How to read this\n"));
        AssertInOrder(order.ToArray());

        Assert.Contains("- **Verdict:** The chat changed\n", text);
        Assert.Contains(executive
            ? "- **Answer streaming rate not computable:** No telemetry run in the baseline.\n"
            : "- **Answer streaming rate (P3) not computable:** No telemetry run in the baseline.\n", text);
        Assert.DoesNotContain("The analysis's headline", text);
        Assert.DoesNotContain("out-of-date analysis", text);
        Assert.Contains("- **Sample:** Met: at least 2 runs on 2 days per period and 20 paired items\n", text);
        Assert.Contains("- **Hours:** Every result holds for weekdays 04–12 UTC only, the hours both periods share\n", text);
        Assert.Contains("- **Controls:** 1 control model (A), identity withheld, run on the same suites in both periods; 1 missing-control note\n", text);

        if (executive)
        {
            Assert.Contains("| Measure | Result | What it means |\n", text);
            Assert.Contains("| Quality | Degraded: \u22124.2\u00A0index points | Worse than before, beyond the margin of ±3\u00A0index points; graded Indicated |\n", text);
            Assert.Contains("| Cost per answer | Inconclusive: +3.1% | Undecided: these runs could detect only a change of ±12.7% or more |\n", text);
            Assert.Contains("*Not computable: Answer streaming rate; the overall verdict says why.*\n", text);
            Assert.Contains("**Where the change came from.** The analysis attributes Quality to the provider's side, graded Indicated. The analysis attributes Cost per answer to our change, graded Not established.\n", text);
            Assert.Contains("Between the periods, Overseer was updated once: E1, 2026-09-11 00:00 UTC — game snapshot: off → on. The control models' runs show one more update.\n", text);
            Assert.Contains("- **The five measures.** ", text);
            Assert.Contains("- **Grades.** *Established* means fit to publish", text);
            Assert.DoesNotContain("(P1)", text);
            Assert.DoesNotContain("(P5)", text[..text.IndexOf("## Evaluation terms", StringComparison.Ordinal)]);
            Assert.DoesNotContain("## Reproducibility\n", text);
            Assert.DoesNotContain("## Control models\n", text);
            Assert.DoesNotContain("- **Time strata:** ", text);
            Assert.DoesNotContain("It never infers anyone's intent", text);
        }
        else
        {
            Assert.Contains("| Endpoint | Change (95% interval) | Margin | Verdict and grade | Smallest detectable |\n", text);
            Assert.Contains("| Quality (P1) | \u22124.2\u00A0index points (\u22126.0 to \u22123.1\u00A0index points) | ±3\u00A0index points | Degraded, Indicated | ±2.5\u00A0index points |\n", text);
            Assert.Contains("- **Answer streaming rate (P3), not computable:** No telemetry run in the baseline.\n", text);
            Assert.Contains("| Event | When (UTC) | Run | What changed |\n", text);
            Assert.Contains("| E1 | 2026-09-11 00:00 | Run #20 | Game snapshot: off → on |\n", text);
            Assert.Contains("| E2 | 2026-09-12 00:00 | Run #31 | Re-indexed: GnollHack wiki (812 → 815 files) (in the runs of Model A) |\n", text);
            Assert.Contains("- **Model A:** 2 runs in the baseline and comparison; difference in differences Quality (P1) \u22124.0\u00A0index points (\u22126.1 to \u22122.0\u00A0index points)\n", text);
            Assert.Contains("| Period | Run (suite) | What to do |\n", text);
            Assert.Contains("| Comparison | Run #21 (Core suite) | Run Model A on Core suite under the build of run #21. |\n", text);
            Assert.Contains("- **Time strata:** 2 strata (weekdays 04–08 UTC, weekdays 08–12 UTC); time of day not assessable: ", text);
            Assert.Contains("- **Analysis code version:** " + Overseer.Services.ChatConsistency.ChatConsistencyAnalysisService.CurrentAnalysisCodeVersion.ToString(CultureInfo.InvariantCulture) + "\n", text);
            Assert.Contains("- **Baseline:** Runs #10 and #11\n", text);
            Assert.Contains("- **Comparison:** Runs #20 and #21\n", text);
            Assert.Contains("- **Control runs:** #30, #31\n", text);
            Assert.Contains("Holm-adjusted p ", text);
            Assert.DoesNotContain("| Check |", text);
            Assert.DoesNotContain("The analysis ran no robustness check on a computed endpoint.", text);
        }

        Assert.DoesNotContain("Input SHA-256", text);
        Assert.DoesNotContain("0123456789abcdef", text);
        Assert.DoesNotContain("instrument", text);
        Assert.DoesNotContain("log ratio", text);
        Assert.DoesNotContain(" -4.2", text);
        Assert.Contains("## Evaluation terms\n", text);
        Assert.DoesNotContain("*Not written.*", text);
        Assert.DoesNotContain("{{", text);
    }

    [Theory]
    [MemberData(nameof(ChatConsistencyGoldenCombinations))]
    public void EveryChatConsistencyDocument_IsReadable_WithoutHashesJsonWideTablesOrMonitoring(
        BenchmarkReportAudience audience, BenchmarkReportDisclosure disclosure, BenchmarkReportPeerNaming naming, string file)
    {
        string text = RenderChatConsistency(audience, disclosure, naming);

        AssertReadableChatConsistencyText(text, file);
    }

    /// <summary>
    /// The readable-text rules every rendered chat consistency document keeps, over the text its golden
    /// file pins: no JSON, no run of twelve or more hex digits, no Overseer event kind identifier, no
    /// table wider than five columns, and no "monitor" in any form.
    /// </summary>
    private static void AssertReadableChatConsistencyText(string text, string what)
    {
        Assert.False(text.Contains("{\"", StringComparison.Ordinal), what + " holds JSON.");
        var hex = System.Text.RegularExpressions.Regex.Match(text, "[0-9a-fA-F]{12,}");
        Assert.False(hex.Success, what + " holds the hex run " + hex.Value + ".");
        foreach (string kind in Overseer.Services.ChatConsistency.OverseerEventKinds.All)
        {
            Assert.False(text.Contains(kind, StringComparison.Ordinal), what + " names the field " + kind + ".");
        }
        Assert.False(text.Contains("monitor", StringComparison.OrdinalIgnoreCase), what + " says monitor.");

        foreach (string line in text.Split('\n').Where(l => l.StartsWith('|')))
        {
            int columns = System.Text.RegularExpressions.Regex.Matches(line, @"(?<!\\)\|").Count - 1;
            Assert.True(columns <= 5, what + " has a table of " + columns.ToString(CultureInfo.InvariantCulture) + " columns: " + line);
        }
    }

    [Fact]
    public void AChatConsistencyDocumentWrittenBeforeReadableEvents_RendersItsEventsInWords_WithoutJsonOrHashes()
    {
        var document = ChatConsistencyDocument(BenchmarkReportAudience.ProviderIssueReport);
        var sheet = BenchmarkReportJson.Deserialize<BenchmarkReportFactSheet>(document.FactsJson);
        var result = ChatConsistencyReportTestData.Result();

        // An older sheet: the raw from and to, an input hash display, a log ratio, a hyphen minus and the
        // instrument note, and none of the readable keys.
        sheet.Facts.RemoveAll(f => f.Key.StartsWith("eventGroups.", StringComparison.Ordinal)
                                   || f.Key.StartsWith("sample.", StringComparison.Ordinal)
                                   || f.Key.EndsWith(".change", StringComparison.Ordinal)
                                   || f.Key == "verdict.short");
        for (int i = 0; i < result.Events.Count; i++)
        {
            string p = "events." + (i + 1).ToString(CultureInfo.InvariantCulture) + ".";
            sheet.Facts.Add(new BenchmarkReportFact { Key = p + "from", Value = JsonValue.Create(result.Events[i].From), Display = result.Events[i].From! });
            sheet.Facts.Add(new BenchmarkReportFact { Key = p + "to", Value = JsonValue.Create(result.Events[i].To), Display = result.Events[i].To! });
        }
        sheet.Facts.Single(f => f.Key == "endpoint.P2.estimate").Display = "+2.0% (log ratio +0.020)";
        sheet.Facts.Single(f => f.Key == "endpoint.P5.mde").Display = "12.7%";
        sheet.Facts.Single(f => f.Key == "controls.missing.1.suggestion").Display = "Run Model A on Core suite under the build of run #21 (instrument 39113903b9b2).";
        sheet.Facts = sheet.Facts.OrderBy(f => f.Key, StringComparer.Ordinal).ToList();
        document.FactsJson = BenchmarkReportJson.Serialize(sheet);

        var writer = ChatConsistencyReportTestData.ValidOutput(BenchmarkReportAudience.ProviderIssueReport);
        writer.Sections[BenchmarkReportSlots.AffectedModel] = "Analysis {{analysis.id}} ran on input {{analysis.inputSha256}}, after {{events.1.from}}.";
        document.WriterOutputJson = BenchmarkReportJson.Serialize(writer);

        string text = BenchmarkReportPackRenderer.Render(document,
            new BenchmarkReportRenderOptions { Disclosure = BenchmarkReportDisclosure.Detailed, PeerNaming = BenchmarkReportPeerNaming.Anonymized });

        Assert.Contains("| E1 | 2026-09-11 00:00 | Run #20 | Game snapshot: off → on |\n", text);
        Assert.Contains("| E2 | 2026-09-12 00:00 | Run #31 | Re-indexed: GnollHack wiki (812 → 815 files) (in the runs of Model A) |\n", text);
        Assert.Contains("| Time to first answer text (P2) | +2.0% (", text);
        Assert.Contains(" | ±12.7% |", text);
        Assert.Contains("Analysis #7 ran on input not shown, after the earlier value.", text);
        Assert.Contains("Run Model A on Core suite under the build of run #21.", text);
        Assert.DoesNotContain("instrument", text);
        AssertReadableChatConsistencyText(text, "the older document");
    }

    [Fact]
    public void ChatConsistencyProse_PrintsOnePeriod_AfterAFactThatEndsInOne()
    {
        var document = ChatConsistencyDocument(BenchmarkReportAudience.ExecutiveSummary);
        var writer = ChatConsistencyReportTestData.ValidOutput(BenchmarkReportAudience.ExecutiveSummary);
        writer.Sections[BenchmarkReportSlots.NextRuns] = "The analysis suggests one more run: {{nextRuns.1.suggestion}}.";
        document.WriterOutputJson = BenchmarkReportJson.Serialize(writer);

        string text = BenchmarkReportPackRenderer.Render(document,
            new BenchmarkReportRenderOptions { Disclosure = BenchmarkReportDisclosure.Summary, PeerNaming = BenchmarkReportPeerNaming.Anonymized });

        Assert.Contains("The analysis suggests one more run: Repeat the comparison runs during US business hours.\n", text);
        Assert.DoesNotContain("hours..", text);
    }

    [Fact]
    public void TheProviderIssueReport_NeverNamesAControlModel_EvenInANamedCopy()
    {
        foreach (var naming in new[] { BenchmarkReportPeerNaming.Named, BenchmarkReportPeerNaming.Anonymized })
        {
            string text = RenderChatConsistency(BenchmarkReportAudience.ProviderIssueReport, BenchmarkReportDisclosure.Summary, naming);

            Assert.DoesNotContain(ChatConsistencyReportTestData.ControlName, text, StringComparison.Ordinal);
            Assert.DoesNotContain(ChatConsistencyReportTestData.ControlProvider, text, StringComparison.Ordinal);
            Assert.DoesNotContain("Weekly check", text, StringComparison.Ordinal);
            Assert.Contains("- **Model A:** ", text);
            Assert.Contains("- **Control models:** 1 control model (A), identity withheld\n", text);
            Assert.Contains(" · controls anonymized*", text);
            Assert.Contains("*Confidential. Prepared for the model's provider.*\n", text);
        }
    }

    [Fact]
    public void ANamedChatConsistencyCopy_NamesTheControlModel_AndAnAnonymizedOneLettersIt()
    {
        string named = RenderChatConsistency(BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportDisclosure.Summary, BenchmarkReportPeerNaming.Named);
        string anonymized = RenderChatConsistency(BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportDisclosure.Summary, BenchmarkReportPeerNaming.Anonymized);

        Assert.Contains("- **Control models:** " + ChatConsistencyReportTestData.ControlName + "\n", named);
        Assert.Contains("- **Analysis:** #7 — Weekly check\n", named);
        Assert.Contains(" · controls named*", named);

        Assert.DoesNotContain(ChatConsistencyReportTestData.ControlName, anonymized, StringComparison.Ordinal);
        Assert.DoesNotContain(ChatConsistencyReportTestData.ControlProvider, anonymized, StringComparison.Ordinal);
        Assert.Contains("- **Controls:** 1 control model (A), identity withheld", anonymized);
        Assert.Contains("- **Analysis:** #7\n", anonymized);
    }

    [Fact]
    public void AChatConsistencyDocument_PrintsTheSameTextAtEveryDisclosure_UnderItsOwnStamp()
    {
        string summary = RenderChatConsistency(BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportDisclosure.Summary, BenchmarkReportPeerNaming.Anonymized);
        string full = RenderChatConsistency(BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportDisclosure.Full, BenchmarkReportPeerNaming.Anonymized);

        Assert.Contains("*Confidential. Unpublished chat consistency results. Review before sharing.*\n", summary);
        Assert.Contains("*INTERNAL — unpublished chat consistency results. Do not share outside the Overseer team.*\n", full);
        string sameBody = summary
            .Replace(BenchmarkReportPackRenderer.ChatConsistencyStamp(BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportDisclosure.Summary),
                BenchmarkReportPackRenderer.ChatConsistencyStamp(BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportDisclosure.Full), StringComparison.Ordinal)
            .Replace(" · disclosure Summary · ", " · disclosure Full · ", StringComparison.Ordinal);
        AssertSameText(sameBody, full, "Executive Summary at Full");
    }

    [Fact]
    public void ThePlacement_PlacesTheChatConsistencyFiguresPerAudience_AndKeepsThemOutOfTheOtherScopes()
    {
        const BenchmarkReportScope chat = BenchmarkReportScope.ChatConsistency;
        var results = BenchmarkReportChartAnchor.ChatConsistencyResults;
        var events = BenchmarkReportChartAnchor.ChatConsistencyEvents;

        Assert.Equal(new[] { "cc1-quality", "cc2-speed", "cc3-work", "cc4-timeline", "cc5-availability" }, BenchmarkReportChartPlacement.ChatConsistencyFigureKeys);
        Assert.All(BenchmarkReportChartPlacement.ChatConsistencyFigureKeys, key => Assert.True(BenchmarkReportChartPlacement.IsKnown(key)));
        Assert.Equal(17, BenchmarkReportChartPlacement.AllFigureKeys.Count);

        // With a layout every audience may draw every figure: provider availability with quality, speed
        // and work after the verdict table, the timeline after the events table, and in the Executive
        // Summary, which has none, after the verdict table.
        Assert.Equal(new[] { "cc1-quality", "cc2-speed", "cc3-work", "cc4-timeline", "cc5-availability" }, BenchmarkReportChartPlacement.KeysAt(BenchmarkReportAudience.ExecutiveSummary, results, chat));
        Assert.Empty(BenchmarkReportChartPlacement.KeysAt(BenchmarkReportAudience.ExecutiveSummary, events, chat));
        Assert.Equal(new[] { "cc1-quality", "cc2-speed", "cc3-work", "cc5-availability" }, BenchmarkReportChartPlacement.KeysAt(BenchmarkReportAudience.ProviderIssueReport, results, chat));
        Assert.Equal(new[] { "cc4-timeline" }, BenchmarkReportChartPlacement.KeysAt(BenchmarkReportAudience.ProviderIssueReport, events, chat));
        Assert.Equal(results, BenchmarkReportChartPlacement.AnchorOf(BenchmarkReportAudience.ExecutiveSummary, "cc4-timeline", chat));

        // No legacy chart set holds the provider availability figure.
        foreach (var audience in BenchmarkReportSlots.ChatConsistencyAudiences)
        {
            Assert.Null(BenchmarkReportChartPlacement.AnchorOf(audience, "cc5-availability", chat, legacy: true));
        }

        // Without one, the legacy per-audience placement.
        Assert.Equal(new[] { "cc1-quality", "cc2-speed" }, BenchmarkReportChartPlacement.KeysAt(BenchmarkReportAudience.ExecutiveSummary, results, chat, legacy: true));
        Assert.Empty(BenchmarkReportChartPlacement.KeysAt(BenchmarkReportAudience.ExecutiveSummary, events, chat, legacy: true));
        Assert.Equal(new[] { "cc2-speed" }, BenchmarkReportChartPlacement.KeysAt(BenchmarkReportAudience.ProviderIssueReport, results, chat, legacy: true));
        Assert.Equal(new[] { "cc4-timeline" }, BenchmarkReportChartPlacement.KeysAt(BenchmarkReportAudience.ProviderIssueReport, events, chat, legacy: true));
        Assert.Null(BenchmarkReportChartPlacement.AnchorOf(BenchmarkReportAudience.ExecutiveSummary, "cc3-work", chat, legacy: true));

        // The legacy flag means nothing to the other scopes.
        Assert.Equal(BenchmarkReportChartPlacement.KeysAt(BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportChartAnchor.HowTheyCompare, BenchmarkReportScope.Comparison),
            BenchmarkReportChartPlacement.KeysAt(BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportChartAnchor.HowTheyCompare, BenchmarkReportScope.Comparison, legacy: true));

        Assert.Equal(results, BenchmarkReportChartPlacement.AnchorOf(BenchmarkReportAudience.ExecutiveSummary, "cc3-work", chat));
        Assert.Null(BenchmarkReportChartPlacement.AnchorOf(BenchmarkReportAudience.ExecutiveSummary, "cc1-quality"));
        Assert.Null(BenchmarkReportChartPlacement.AnchorOf(BenchmarkReportAudience.ExecutiveSummary, "cc1-quality", BenchmarkReportScope.Comparison));
        Assert.Null(BenchmarkReportChartPlacement.AnchorOf(BenchmarkReportAudience.ExecutiveSummary, "p1a-quality", chat));
        Assert.Equal(results, BenchmarkReportChartPlacement.AnchorOf(BenchmarkReportAudience.ProviderIssueReport, "cc1-quality", chat));
        Assert.Empty(BenchmarkReportChartPlacement.KeysAt(BenchmarkReportAudience.ProviderIssueReport, BenchmarkReportChartAnchor.HowItCompares));
        Assert.True(BenchmarkReportChartPlacement.IsKnown("cc4-timeline"));
    }

    [Theory]
    [InlineData(BenchmarkReportAudience.ExecutiveSummary, new[] { "cc1-quality", "cc2-speed" })]
    [InlineData(BenchmarkReportAudience.ProviderIssueReport, new[] { "cc2-speed", "cc4-timeline" })]
    public void AChatConsistencyDocument_DrawsItsFiguresAfterTheirTables_AndNoComparisonFigure(BenchmarkReportAudience audience, string[] placed)
    {
        const BenchmarkReportDisclosure disclosure = BenchmarkReportDisclosure.Summary;
        var keys = BenchmarkReportChartPlacement.AllFigureKeys.Reverse().ToArray();
        string text = RenderWithCharts(ChatConsistencyDocument(audience), disclosure, BenchmarkReportPeerNaming.Anonymized, Charts(keys));

        foreach (string key in BenchmarkReportChartPlacement.AllFigureKeys)
        {
            Assert.Equal(placed.Contains(key) ? 1 : 0, AllIndexesOf(text, MarkerOf(key)).Count);
        }

        int Pos(string part) => text.IndexOf(part, StringComparison.Ordinal);
        int verdictTable = Pos(audience == BenchmarkReportAudience.ExecutiveSummary
            ? "| Measure | Result | What it means |"
            : "| Endpoint | Change (95% interval) | Margin | Verdict and grade | Smallest detectable |");
        int eventsTable = Pos("| When (UTC) | Run | What changed |");
        Assert.True(verdictTable >= 0);
        foreach (string key in placed)
        {
            int at = Pos("\n\n" + MarkerOf(key) + "\n\n");
            Assert.True(at > (key == "cc4-timeline" ? eventsTable : verdictTable), key + " stands after its table.");
        }
        Assert.DoesNotContain("[[figure:", RenderChatConsistency(audience, disclosure, BenchmarkReportPeerNaming.Anonymized));
    }

    [Fact]
    public void AChatConsistencyDocumentWithAChartLayout_DrawsTheTimelineAfterTheVerdicts_InTheExecutiveSummary()
    {
        var document = ChatConsistencyDocument(BenchmarkReportAudience.ExecutiveSummary);
        var options = new BenchmarkReportRenderOptions
        {
            Disclosure = BenchmarkReportDisclosure.Summary,
            PeerNaming = BenchmarkReportPeerNaming.Anonymized,
            Charts = Charts("cc1-quality", "cc4-timeline")
        };

        string withLayout = BenchmarkReportPackRenderer.Render(document, options, chartLayoutPresent: true);
        string legacy = BenchmarkReportPackRenderer.Render(document, options, chartLayoutPresent: false);

        int verdicts = withLayout.IndexOf("| Measure | Result | What it means |", StringComparison.Ordinal);
        AssertInOrder(verdicts, withLayout.IndexOf(MarkerOf("cc1-quality"), StringComparison.Ordinal), withLayout.IndexOf(MarkerOf("cc4-timeline"), StringComparison.Ordinal),
            withLayout.IndexOf("## Is the Overseer chat with this model as good as before?\n", StringComparison.Ordinal));
        Assert.Single(AllIndexesOf(withLayout, MarkerOf("cc4-timeline")));

        // Without a layout, today's placement: the Executive Summary draws no timeline.
        Assert.Single(AllIndexesOf(legacy, MarkerOf("cc1-quality")));
        Assert.Empty(AllIndexesOf(legacy, MarkerOf("cc4-timeline")));
        Assert.Equal(legacy, BenchmarkReportPackRenderer.Render(document, options));
    }

    [Fact]
    public void AChatConsistencyDocumentWithAChartLayout_DrawsEveryChosenFigure_InTheProviderIssueReport()
    {
        var document = ChatConsistencyDocument(BenchmarkReportAudience.ProviderIssueReport);
        var options = new BenchmarkReportRenderOptions
        {
            Disclosure = BenchmarkReportDisclosure.Full,
            PeerNaming = BenchmarkReportPeerNaming.Anonymized,
            Charts = Charts("cc1-quality", "cc3-work")
        };

        string withLayout = BenchmarkReportPackRenderer.Render(document, options, chartLayoutPresent: true);
        string legacy = BenchmarkReportPackRenderer.Render(document, options, chartLayoutPresent: false);

        int verdicts = withLayout.IndexOf("| Endpoint | Change (95% interval) | Margin | Verdict and grade | Smallest detectable |", StringComparison.Ordinal);
        Assert.Single(AllIndexesOf(withLayout, MarkerOf("cc1-quality")));
        Assert.Single(AllIndexesOf(withLayout, MarkerOf("cc3-work")));
        AssertInOrder(verdicts, withLayout.IndexOf(MarkerOf("cc1-quality"), StringComparison.Ordinal), withLayout.IndexOf(MarkerOf("cc3-work"), StringComparison.Ordinal));
        // Without a layout, the Provider Issue Report draws neither.
        Assert.DoesNotContain("[[figure:", legacy);
    }

    [Fact]
    public void AChatConsistencyDocumentWithoutControls_StillDrawsItsFigures()
    {
        var document = ChatConsistencyDocument(BenchmarkReportAudience.ExecutiveSummary);
        var sheet = BenchmarkReportJson.Deserialize<BenchmarkReportFactSheet>(document.FactsJson);
        sheet.Peers.Clear();
        document.FactsJson = BenchmarkReportJson.Serialize(sheet);

        string text = RenderWithCharts(document, BenchmarkReportDisclosure.Summary, BenchmarkReportPeerNaming.Named, Charts("cc1-quality"));

        Assert.Single(AllIndexesOf(text, MarkerOf("cc1-quality")));
        Assert.Contains("- **Control models:** None\n", text);
    }

    // ---------------------------------------------------------------------------------------------
    // Provider availability (format version 12; comparison format version 13)
    // ---------------------------------------------------------------------------------------------

    private const string AvailabilityBullet = "- **Provider availability:** 0.81% of attempts failed at the provider "
        + "(12 of 1,480 attempts; 95% CI 0.46–1.4%), plus 3 re-runs (2 for provider errors).\n";

    /// <summary><see cref="Document"/> with the subject's <c>availability.*</c> facts of <paramref name="summary"/>, as a sheet of format version 12 carries them.</summary>
    private static BenchmarkReportDocument DocumentWithAvailability(BenchmarkReportAudience audience, ProviderAvailabilitySummary summary)
    {
        var document = Document(audience);
        var sheet = BenchmarkReportJson.Deserialize<BenchmarkReportFactSheet>(document.FactsJson);
        var availability = new BenchmarkReportFacts.FactList();
        BenchmarkReportFacts.AddAvailabilityFacts(availability, summary);
        sheet.Facts = sheet.Facts.Concat(availability.Sorted()).OrderBy(f => f.Key, StringComparer.Ordinal).ToList();
        document.FactsJson = BenchmarkReportJson.Serialize(sheet);
        return document;
    }

    private static string RenderAvailability(BenchmarkReportAudience audience, ProviderAvailabilitySummary summary)
        => BenchmarkReportPackRenderer.Render(DocumentWithAvailability(audience, summary),
            new BenchmarkReportRenderOptions { Disclosure = BenchmarkReportDisclosure.Full, PeerNaming = BenchmarkReportPeerNaming.Named });

    [Fact]
    public void KeyFigures_StateProviderAvailability_AsTheirFifthBullet()
    {
        string text = RenderAvailability(BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportFactsTests.RecordedAvailability());

        Assert.Contains("- **Critical errors:** 1 of 4 answers.\n" + AvailabilityBullet + "\n", text);
        Assert.Single(AllIndexesOf(text, "- **Provider availability:**"));
        Assert.DoesNotContain("throttl", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheExecutiveSummary_HasTheBulletOnly()
    {
        string text = RenderAvailability(BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportFactsTests.RecordedAvailability());

        Assert.Contains(AvailabilityBullet, text);
        Assert.DoesNotContain("### Provider availability", text);
        Assert.DoesNotContain("| Class | Count |", text);
        Assert.DoesNotContain("could not be attributed", text);
    }

    [Fact]
    public void ALegacyRun_RendersItsProviderAvailability_AsNotRecorded()
    {
        string text = RenderAvailability(BenchmarkReportAudience.ExecutiveSummary, ProviderAvailabilitySummary.NotRecorded(1));

        Assert.Contains("- **Critical errors:** 1 of 4 answers.\n- **Provider availability:** Not recorded before harness 56.\n", text);
        Assert.DoesNotContain("| Class | Count |", text);
        Assert.DoesNotContain("### Provider availability", text);
    }

    [Theory]
    [InlineData(BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportDisclosure.Summary, BenchmarkReportPeerNaming.Named)]
    [InlineData(BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportDisclosure.Full, BenchmarkReportPeerNaming.Anonymized)]
    public void ADocumentStoredUnderFormat11_RendersWithoutAnyProviderAvailabilityBlock(
        BenchmarkReportAudience audience, BenchmarkReportDisclosure disclosure, BenchmarkReportPeerNaming naming)
    {
        var document = Document(audience);
        document.ReportFormatVersion = 11;

        string text = BenchmarkReportPackRenderer.Render(document, new BenchmarkReportRenderOptions { Disclosure = disclosure, PeerNaming = naming });

        Assert.DoesNotContain("Provider availability", text);
        Assert.DoesNotContain("provider errors observed", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("generated under format version 11 · rendered with format version "
            + BenchmarkReportPackRenderer.ReportFormatVersion.ToString(CultureInfo.InvariantCulture), text);
    }

    /// <summary>
    /// The fixture comparison's document with provider availability for every model: the recorded pool
    /// of <see cref="BenchmarkReportFactsTests.RecordedAvailability"/>, and Zeta Prime (Model E) not recorded.
    /// </summary>
    private static BenchmarkReportDocument ComparisonDocumentWithAvailability(BenchmarkReportAudience audience)
    {
        var comparison = FiveModelComparison();
        var runs = FiveModelRuns();
        var keys = BenchmarkComparisonReportFacts.DefaultCoveredKeys(comparison);
        var (reference, allPairs, unavailable) = BenchmarkComparisonReportFacts.PairedFromRuns(comparison, keys, runs);
        var availability = keys.ToDictionary(
            k => k,
            k => k == "run:35" ? ProviderAvailabilitySummary.NotRecorded(1) : BenchmarkReportFactsTests.RecordedAvailability());
        var built = BenchmarkComparisonReportFacts.BuildFromRuns(
            comparison, keys, runs, 120, reference, allPairs, unavailable, ComparisonNumber, availability);

        var document = ComparisonDocument(audience);
        document.FactsJson = BenchmarkReportJson.Serialize(built.Sheet!);
        document.ContentJson = BenchmarkReportJson.Serialize(built.Content);
        document.WriterOutputJson = BenchmarkReportJson.Serialize(ComparisonWriter(audience, built.Sheet!));
        return document;
    }

    [Fact]
    public void AComparisonExecutiveSummary_PrintsNoProviderAvailability_AndDrawsNoFigureA1()
    {
        var options = new BenchmarkReportRenderOptions { Disclosure = BenchmarkReportDisclosure.Full, PeerNaming = BenchmarkReportPeerNaming.Anonymized };
        string executive = BenchmarkReportPackRenderer.Render(ComparisonDocumentWithAvailability(BenchmarkReportAudience.ExecutiveSummary), options);
        Assert.DoesNotContain("Provider availability", executive);
        Assert.DoesNotContain("Provider errors", executive);

        string withChart = RenderWithCharts(ComparisonDocumentWithAvailability(BenchmarkReportAudience.ExecutiveSummary),
            BenchmarkReportDisclosure.Full, BenchmarkReportPeerNaming.Named, Charts(BenchmarkReportChartPlacement.AvailabilityKey));
        Assert.DoesNotContain(MarkerOf("a1-availability"), withChart);
    }

    // ---------------------------------------------------------------------------------------------
    // Sentence case (model format 17, comparison format 22, chat consistency format 5)
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void AModelDocument_StartsEveryWriterSentence_AndAFactDisplayThatStartsOne_WithACapital()
    {
        var document = Document(BenchmarkReportAudience.ExecutiveSummary);
        var sheet = BenchmarkReportJson.Deserialize<BenchmarkReportFactSheet>(document.FactsJson);
        string overlap = sheet.Facts.Single(f => f.Key == "quality.intervalOverlap").Display;
        var writer = Writer();
        writer.Headline = "the model answered well. it was also fast.";
        writer.Sections[BenchmarkReportSlots.Meaning] = "It is usable. {{quality.intervalOverlap}}.\n\nso the order is open.";
        document.WriterOutputJson = BenchmarkReportJson.Serialize(writer);

        string text = BenchmarkReportPackRenderer.Render(document,
            new BenchmarkReportRenderOptions { Disclosure = BenchmarkReportDisclosure.Summary, PeerNaming = BenchmarkReportPeerNaming.Anonymized });

        Assert.StartsWith("its ", overlap);
        Assert.Contains("## The result in one sentence\n\nThe model answered well. It was also fast.\n", text);
        Assert.Contains("It is usable. I" + overlap[1..] + ".\n\nSo the order is open.\n", text);
    }

    [Fact]
    public void AComparisonDocument_StartsEveryWriterSentence_AndASpreadThatStartsOne_WithACapital()
    {
        var document = ComparisonDocument(BenchmarkReportAudience.ExecutiveSummary);
        var sheet = BenchmarkReportJson.Deserialize<BenchmarkReportFactSheet>(document.FactsJson);
        string spread = sheet.Facts.Single(f => f.Key == "spread.quality").Display;
        var writer = BenchmarkReportJson.Deserialize<BenchmarkReportWriterOutput>(document.WriterOutputJson);
        writer.Headline = "the models are close. {{spread.quality}} on intelligence.";
        document.WriterOutputJson = BenchmarkReportJson.Serialize(writer);

        string text = BenchmarkReportPackRenderer.Render(document,
            new BenchmarkReportRenderOptions { Disclosure = BenchmarkReportDisclosure.Full, PeerNaming = BenchmarkReportPeerNaming.Anonymized });

        Assert.StartsWith("from Model ", spread);
        Assert.DoesNotContain("(Model", spread);
        Assert.Contains("## The result in one sentence\n\nThe models are close. From" + spread["from".Length..] + " on intelligence.\n", text);
    }

    [Fact]
    public void AChatConsistencyDocument_StartsEveryWriterSentence_AndAFactDisplayThatStartsOne_WithACapital()
    {
        var document = ChatConsistencyDocument(BenchmarkReportAudience.ExecutiveSummary);
        var sheet = BenchmarkReportJson.Deserialize<BenchmarkReportFactSheet>(document.FactsJson);
        string hours = sheet.Facts.Single(f => f.Key == "scope.hours").Display;
        var writer = ChatConsistencyReportTestData.ValidOutput(BenchmarkReportAudience.ExecutiveSummary);
        writer.Headline = "{{scope.hours}} is the window. the chat held.";
        document.WriterOutputJson = BenchmarkReportJson.Serialize(writer);

        string text = BenchmarkReportPackRenderer.Render(document,
            new BenchmarkReportRenderOptions { Disclosure = BenchmarkReportDisclosure.Summary, PeerNaming = BenchmarkReportPeerNaming.Anonymized });

        Assert.True(char.IsLower(hours[0]), "The fixture's hours display starts in lowercase.");
        Assert.Contains("## The result in one sentence\n\n" + BenchmarkReportSentenceCase.UpperFirst(hours) + " is the window. The chat held.\n", text);
    }

    [Fact]
    public void ATableCell_AndTheTextAfterARunInLabel_StartWithACapital_ButAQuotedClaimIsLeftAsWritten()
    {
        var document = RcaDocument(content: c =>
        {
            foreach (var item in c.Runs.SelectMany(r => r.Questions))
            {
                item.ClaimRulings.Add(new BenchmarkReportContentClaimRuling
                {
                    Claim = "worthless glass angers the unicorn.", Verdict = "refuted", Rationale = "Glass is caught (dothrow.c).",
                    Role = BenchmarkReportContent.AccusedSentenceRole
                });
            }
        });

        string text = BenchmarkReportPackRenderer.Render(document, RcaNamed);

        Assert.Contains(":** \"worthless glass angers the unicorn.\" — Glass is caught (dothrow.c).\n", text);
        Assert.DoesNotContain("\"Worthless glass", text);
        Assert.Matches(@"\*\*Graders:\*\* [A-Z]", text);
        Assert.DoesNotMatch(@"\*\*Graders:\*\* [a-z]", text);
        foreach (string row in text.Split('\n').Where(l => l.StartsWith("| ", StringComparison.Ordinal)))
        {
            foreach (string cell in row.Split(" | "))
            {
                string trimmed = cell.Trim().Trim('|').Trim().Trim('*');
                Assert.False(trimmed is "none" or "not established" or "not tested" or "volume" or "price" or "yes" or "no",
                    "A table cell starts in lowercase: " + row);
            }
        }
    }
}
