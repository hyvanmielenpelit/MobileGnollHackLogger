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
    public void AllowedDisclosures_AreEveryLevelForTheResearcherReport_AndFullAloneForTheInternalBrief()
    {
        var all = new[] { BenchmarkReportDisclosure.Summary, BenchmarkReportDisclosure.Detailed, BenchmarkReportDisclosure.Full };

        Assert.Equal(all, BenchmarkReportPackRenderer.AllowedDisclosures(BenchmarkReportAudience.TechnicalReport));
        Assert.Equal(new[] { BenchmarkReportDisclosure.Full }, BenchmarkReportPackRenderer.AllowedDisclosures(BenchmarkReportAudience.InternalBrief));
        Assert.Equal(12, Combinations().Count());
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
            BenchmarkReportAudience.InternalBrief,
            new BenchmarkReportRenderOptions { Disclosure = BenchmarkReportDisclosure.Detailed }));
    }

    [Theory]
    [InlineData(BenchmarkReportDisclosure.Summary, BenchmarkReportPeerNaming.Named)]
    [InlineData(BenchmarkReportDisclosure.Summary, BenchmarkReportPeerNaming.Anonymized)]
    [InlineData(BenchmarkReportDisclosure.Detailed, BenchmarkReportPeerNaming.Named)]
    [InlineData(BenchmarkReportDisclosure.Detailed, BenchmarkReportPeerNaming.Anonymized)]
    public void TheInternalBrief_BelowFull_IsRefused(BenchmarkReportDisclosure disclosure, BenchmarkReportPeerNaming naming)
    {
        var options = new BenchmarkReportRenderOptions { Disclosure = disclosure, PeerNaming = naming };

        Assert.False(BenchmarkReportPackRenderer.IsAllowed(BenchmarkReportAudience.InternalBrief, options));
        Assert.Throws<ArgumentException>(() =>
            BenchmarkReportPackRenderer.Render(Document(BenchmarkReportAudience.InternalBrief), options));
    }

    [Fact]
    public void UndefinedOptions_AreRefused()
    {
        var badDisclosure = new BenchmarkReportRenderOptions { Disclosure = (BenchmarkReportDisclosure)99 };
        var badNaming = new BenchmarkReportRenderOptions { PeerNaming = (BenchmarkReportPeerNaming)99 };

        Assert.False(BenchmarkReportPackRenderer.IsAllowed(BenchmarkReportAudience.TechnicalReport, badDisclosure));
        Assert.False(BenchmarkReportPackRenderer.IsAllowed(BenchmarkReportAudience.TechnicalReport, badNaming));
        Assert.False(BenchmarkReportPackRenderer.IsAllowed(BenchmarkReportAudience.TechnicalReport, null!));
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
    [InlineData(BenchmarkReportAudience.TechnicalReport, "technical_standalone.md")]
    public void TheStandaloneForm_MatchesItsGoldenFile(BenchmarkReportAudience audience, string file)
    {
        string rendered = BenchmarkReportPackRenderer.Render(
            StandaloneDocument(audience),
            new BenchmarkReportRenderOptions { Disclosure = BenchmarkReportDisclosure.Full, PeerNaming = BenchmarkReportPeerNaming.Named });

        AssertMatchesGolden(file, rendered);
    }

    /// <summary>
    /// The run's stand-alone Internal Improvement Brief, a run-completion document: the stand-alone
    /// writer output with a recommendation for every target and a lead, and no peer anywhere.
    /// </summary>
    [Fact]
    public void TheStandaloneInternalBrief_MatchesItsGoldenFile_AndNamesNoPeer()
    {
        var document = StandaloneDocument(BenchmarkReportAudience.InternalBrief);
        var writer = StandaloneWriter();
        writer.Sections[BenchmarkReportSlots.ModelResult] = "{{subject}} scored {{quality.index}} at {{cost.perQuestion}} per question.";
        writer.Recommendations.AddRange(Writer().Recommendations.Where(r => r.For != BenchmarkReportSlots.TargetModelDevelopers));
        writer.Leads.AddRange(Writer().Leads);
        document.WriterOutputJson = BenchmarkReportJson.Serialize(writer);

        string rendered = BenchmarkReportPackRenderer.Render(document,
            new BenchmarkReportRenderOptions { Disclosure = BenchmarkReportDisclosure.Full, PeerNaming = BenchmarkReportPeerNaming.Named });

        AssertMatchesGolden("internal_standalone.md", rendered);
        Assert.Contains("- **Peers:** none; this is a stand-alone report\n", rendered);
        Assert.DoesNotContain("Compared with", rendered);
        Assert.DoesNotContain("Peer mean", rendered);
        Assert.DoesNotContain("No recommendations were recorded.", rendered);
        Assert.DoesNotContain("No leads were recorded.", rendered);
    }

    /// <summary>The battery run's stand-alone Internal Improvement Brief, a battery-completion document.</summary>
    [Fact]
    public void TheStandaloneBatteryInternalBrief_MatchesItsGoldenFile_AndNamesNoPeer()
    {
        var document = BatteryReportFixture.Document(BenchmarkReportAudience.InternalBrief);
        var writer = BatteryReportFixture.Writer();
        writer.Sections[BenchmarkReportSlots.BenchmarkSystem] = "Both panel members flagged the critical error on S2-Q1.";
        writer.Sections[BenchmarkReportSlots.ModelResult] = "{{subject}} scored {{quality.index}} at {{cost.perQuestion}} per question.";
        writer.Recommendations.Add(new BenchmarkReportWriterRecommendation
        {
            For = BenchmarkReportSlots.TargetOverseerChat,
            Text = "Send item-destruction questions to the source code first.",
            Evidence = new List<string> { "tools.share.sourceCode" }
        });
        writer.Recommendations.Add(new BenchmarkReportWriterRecommendation
        {
            For = BenchmarkReportSlots.TargetBenchmark,
            Text = "Check the S2-Q1 rubric against the source before the next run.",
            Questions = new List<int> { 3 },
            Evidence = new List<string> { "S2-Q1" }
        });
        writer.Leads.Add(new BenchmarkReportLead
        {
            Triage = "suite",
            Text = "The S2-Q1 rubric may understate how often a thrown gem survives.",
            Questions = new List<int> { 3 },
            Evidence = new List<string> { "S2-Q1" }
        });
        document.WriterOutputJson = BenchmarkReportJson.Serialize(writer);

        string rendered = BenchmarkReportPackRenderer.Render(document,
            new BenchmarkReportRenderOptions { Disclosure = BenchmarkReportDisclosure.Full, PeerNaming = BenchmarkReportPeerNaming.Named });

        AssertMatchesGolden("internal_battery_standalone.md", rendered);
        Assert.Contains("- **Peers:** none; this is a stand-alone report\n", rendered);
        Assert.Contains("### The suites of this battery\n", rendered);
        Assert.DoesNotContain("Compared with", rendered);
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
    private const string Q1Excerpt = "A real gem of your alignment raises your Luck";
    private const string Q3Text = "Will my gem break if I throw it at a unicorn?";
    private const string Q3Excerpt = "A thrown gem always shatters on impact, so never throw";
    private const string Q1Rubric = "A valuable gem raises Luck; worthless glass does not.";
    private const string Q1Evidence = "Accuracy: Matches rubric.";
    private const string Q3Ruling = "Gems are caught, not broken (dothrow.c).";

    [Fact]
    public void AtSummary_NoQuestionTextRubricAnswerOrGraderEvidenceIsPrinted()
    {
        foreach (var audience in new[] { BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportAudience.TechnicalReport })
        {
            string text = Render(audience, BenchmarkReportDisclosure.Summary, BenchmarkReportPeerNaming.Named);

            Assert.DoesNotContain(Q1Text, text);
            Assert.DoesNotContain(Q3Text, text);
            Assert.DoesNotContain(Q3Excerpt, text);
            Assert.DoesNotContain(Q1Rubric, text);
            Assert.DoesNotContain(Q1Evidence, text);
            Assert.DoesNotContain(Q3Ruling, text);
            Assert.Contains("Questions are described, not quoted.", text);
        }

        Assert.Contains("| Q3 | Breaking a thrown gem |",
            Render(BenchmarkReportAudience.TechnicalReport, BenchmarkReportDisclosure.Summary, BenchmarkReportPeerNaming.Named));
    }

    [Fact]
    public void AtDetailed_EveryQuestionAndAnswerExcerptIsQuoted_AndNoRubricOrEvidence()
    {
        string text = Render(BenchmarkReportAudience.TechnicalReport, BenchmarkReportDisclosure.Detailed, BenchmarkReportPeerNaming.Named);

        Assert.Contains("\n## Questions and answers\n", text);
        Assert.DoesNotContain("Question details", text);
        foreach (var question in Content().Runs.SelectMany(r => r.Questions))
        {
            Assert.Single(AllIndexesOf(text, "> " + question.QuestionText + "\n"));
            Assert.Single(AllIndexesOf(text, "> " + question.AnswerExcerpt + "\n"));
        }
        Assert.Contains(Q1Text, text);
        Assert.Contains(Q1Excerpt, text);
        Assert.Contains(Q3Text, text);
        Assert.Contains(Q3Excerpt, text);
        // A noted question keeps its note, and a finding's evidence line never quotes the question it cites.
        Assert.Contains("Claimed a thrown gem always shatters; the rubric says it can survive.", text);
        Assert.DoesNotContain("> **Question:**", text);
        Assert.DoesNotContain("as asked", text);
        Assert.DoesNotContain("**Rubric:**", text);
        Assert.DoesNotContain("*Rubric not recorded for this answer.*", text);
        Assert.DoesNotContain(Q1Rubric, text);
        Assert.DoesNotContain("\n**Graders:**\n", text);
        Assert.DoesNotContain(Q1Evidence, text);
        Assert.DoesNotContain("**Claim verifier:**", text);
        Assert.DoesNotContain(Q3Ruling, text);
        Assert.DoesNotContain("Slightly below the peers.", text);
        Assert.Contains("Contains benchmark questions — do not publish.", text);
    }

    [Fact]
    public void AtFull_EveryQuestionRubricGraderEvidenceAndRulingIsPrinted()
    {
        string text = Render(BenchmarkReportAudience.TechnicalReport, BenchmarkReportDisclosure.Full, BenchmarkReportPeerNaming.Named);

        Assert.Contains("\n## Question details\n", text);
        Assert.DoesNotContain("Questions and answers", text);
        foreach (var question in Content().Runs.SelectMany(r => r.Questions))
        {
            Assert.Single(AllIndexesOf(text, question.QuestionText));
        }
        Assert.Contains(Q1Text, text);
        Assert.Contains(Q3Text, text);
        Assert.Contains(Q1Rubric, text);
        Assert.Contains(Q1Evidence, text);
        Assert.Contains(Q3Ruling, text);
        Assert.Contains("*Rubric not recorded for this answer.*", text);
        Assert.Contains("INTERNAL — contains benchmark questions and rubrics.", text);
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
        string named = Render(BenchmarkReportAudience.TechnicalReport, BenchmarkReportDisclosure.Summary, BenchmarkReportPeerNaming.Named);
        string anonymized = Render(BenchmarkReportAudience.TechnicalReport, BenchmarkReportDisclosure.Summary, BenchmarkReportPeerNaming.Anonymized);

        Assert.Contains("GPT-5.6 Luna scored 80 / 100 on 4 questions, ranking joint 1st of 3 (intervals overlap) against Grok 5 and Mistral Large 4.", named);
        Assert.Contains("GPT-5.6 Luna scored 80 / 100 on 4 questions, ranking joint 1st of 3 (intervals overlap) against Model A and Model B.", anonymized);
    }

    [Fact]
    public void AnUnavailableFact_IsStatedAsNotAvailable_WithItsReason()
    {
        string text = Render(BenchmarkReportAudience.TechnicalReport, BenchmarkReportDisclosure.Summary, BenchmarkReportPeerNaming.Named);

        Assert.Contains("Judge-dependent pairs: not available. The compared runs were not all graded by the same panel.", text);
    }

    [Fact]
    public void TheRemovedContentNotice_ListsDroppedItems_OnlyWhenTheDocumentHasWarnings()
    {
        string summary = Render(BenchmarkReportAudience.TechnicalReport, BenchmarkReportDisclosure.Summary, BenchmarkReportPeerNaming.Named);
        string full = Render(BenchmarkReportAudience.TechnicalReport, BenchmarkReportDisclosure.Full, BenchmarkReportPeerNaming.Named);

        Assert.Contains("## Removed content", summary);
        Assert.Contains("- `weaknesses[2]` (rule 4)\n", summary);
        Assert.DoesNotContain("sections.abstract", summary);
        Assert.DoesNotContain("Cited R9", summary);
        Assert.Contains("- `weaknesses[2]` (rule 4): Cited R9, which does not exist.", full);

        var completed = Document(BenchmarkReportAudience.TechnicalReport);
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
        var baseline = Document(BenchmarkReportAudience.TechnicalReport);

        // Columns the renderer does not read: changing them changes nothing.
        var other = Document(BenchmarkReportAudience.TechnicalReport);
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

        var first = Document(BenchmarkReportAudience.TechnicalReport);
        first.CreatedAtUtc = new DateTime(2001, 2, 3, 4, 5, 0, DateTimeKind.Utc);
        var second = Document(BenchmarkReportAudience.TechnicalReport);
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
        string expected = BenchmarkReportPackRenderer.Render(Document(BenchmarkReportAudience.TechnicalReport), options);

        var sheet = Sheet();
        sheet.Peers.Reverse();
        sheet.Questions.Reverse();
        sheet.Rows.Reverse();
        sheet.Entries.Reverse();
        sheet.Facts.Reverse();

        var writer = Writer();
        writer.Sections = writer.Sections.Reverse().ToDictionary(kv => kv.Key, kv => kv.Value);
        writer.QuestionTopics.Reverse();
        writer.QuestionNotes.Reverse();

        var shuffled = Document(BenchmarkReportAudience.TechnicalReport);
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

    [Theory]
    [MemberData(nameof(Combinations))]
    public void Rule6_TheFormatVersionIsPrintedInTheFooter(
        BenchmarkReportAudience audience, BenchmarkReportDisclosure disclosure, BenchmarkReportPeerNaming naming, string file)
    {
        string text = Render(audience, disclosure, naming);

        Assert.Contains("· format version " + BenchmarkReportPackRenderer.ReportFormatVersion.ToString(CultureInfo.InvariantCulture) + " ·", text);
        Assert.Contains("*Document ID 101 ·", text);
        Assert.NotEmpty(file);
    }

    [Fact]
    public async Task Rule7_TheSameDocumentYieldsTheSameBytes_TwiceAndAfterADatabaseRoundTrip()
    {
        var dbOptions = BenchmarkRunExamTests.InMemoryOptions();

        await using (var db = new ApplicationDbContext(dbOptions))
        {
            foreach (var audience in new[] { BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportAudience.TechnicalReport, BenchmarkReportAudience.InternalBrief })
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
        var fields = typeof(BenchmarkReportPackRenderer).GetFields(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        Assert.All(fields, f => Assert.True(f.IsLiteral, f.Name + " is not a const"));
        Assert.Contains(fields, f => f.Name == nameof(BenchmarkReportPackRenderer.ReportFormatVersion));
    }

    // ---------------------------------------------------------------------------------------------
    // Evidence lines, evaluation terms, the stand-alone form and the label
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void EveryStrengthWeaknessAndRecommendation_PrintsOneEvidenceLineBeneathIt()
    {
        string text = Render(BenchmarkReportAudience.TechnicalReport, BenchmarkReportDisclosure.Detailed, BenchmarkReportPeerNaming.Named);

        Assert.Contains("- Answers simple questions precisely and briefly. *(One grader — different provider)*\n"
            + "  - *Evidence:* One grader — different provider — accuracy · Q1 (90 / 100)\n\n", text);
        Assert.Contains("*(Both graders)*\n"
            + "  - *Evidence:* Both graders — critical error · Q3 (25 / 100) · the claim verifier refuted an answer sentence on Q3\n", text);
        Assert.Contains("  - *Evidence:* Intermediate band score: 49 · Q3 (25 / 100), Q2 (72 / 100) · the claim verifier refuted an answer sentence on Q3\n", text);
        Assert.Contains("- Verify object-destruction rules before asserting them. *(Both graders)*\n"
            + "  - *Evidence:* Both graders — critical error · Q3 (25 / 100) · the claim verifier refuted an answer sentence on Q3\n", text);
    }

    [Theory]
    [InlineData(BenchmarkReportDisclosure.Summary)]
    [InlineData(BenchmarkReportDisclosure.Detailed)]
    [InlineData(BenchmarkReportDisclosure.Full)]
    public void TheResearcherReport_ExpandsNoQuestionUnderAFinding(BenchmarkReportDisclosure disclosure)
    {
        string text = Render(BenchmarkReportAudience.TechnicalReport, disclosure, BenchmarkReportPeerNaming.Named);
        var evidence = text.Split('\n').Where(l => l.StartsWith("  - *Evidence:*", StringComparison.Ordinal)).ToList();

        // Evidence lines are printed at Detailed and Full only.
        Assert.Equal(disclosure == BenchmarkReportDisclosure.Summary ? 0 : 4, evidence.Count);
        Assert.DoesNotContain("as asked", text);
        Assert.DoesNotContain(" on Q3:*", text);
        Assert.All(evidence, line => Assert.DoesNotContain("R1", line));
        // The finding sections hold no grader note; Question details at Full holds each once.
        string findings = text[..text.IndexOf("## Recommendations for model developers", StringComparison.Ordinal)];
        Assert.DoesNotContain("Accuracy: The gem does not always shatter.", findings);
        if (disclosure == BenchmarkReportDisclosure.Full)
        {
            Assert.Single(AllIndexesOf(text, "  - Accuracy: The gem does not always shatter.\n"));
        }
    }

    [Fact]
    public void AFindingsQuestions_AreTheWritersOwn_NotItsRowsQuestions()
    {
        var document = Document(BenchmarkReportAudience.TechnicalReport);
        var sheet = Sheet();
        sheet.Rows.Add(new BenchmarkReportFindingRow
        {
            Id = "R4", Kind = "weakness", Category = "completeness", Questions = new List<int> { 2, 3 },
            Status = "Convergent", SupportLabel = BenchmarkReportFacts.SupportBothGraders
        });
        var writer = Writer();
        writer.Weaknesses = new List<BenchmarkReportWriterItem>
        {
            new() { Text = "Left out the survival chance on Q3.", Questions = new List<int> { 3 }, Evidence = new List<string> { "R4" } },
            new() { Text = "Left out details across the exam.", Evidence = new List<string> { "R4" } }
        };
        document.FactsJson = BenchmarkReportJson.Serialize(sheet);
        document.WriterOutputJson = BenchmarkReportJson.Serialize(writer);

        string text = BenchmarkReportPackRenderer.Render(document,
            new BenchmarkReportRenderOptions { Disclosure = BenchmarkReportDisclosure.Detailed, PeerNaming = BenchmarkReportPeerNaming.Named });

        Assert.Contains("- Left out the survival chance on Q3. *(Both graders)*\n"
            + "  - *Evidence:* Both graders — completeness · Q3 (25 / 100) · the claim verifier refuted an answer sentence on Q3\n", text);
        // An item naming no question takes its rows' questions.
        Assert.Contains("- Left out details across the exam. *(Both graders)*\n"
            + "  - *Evidence:* Both graders — completeness · Q3 (25 / 100), Q2 (72 / 100) · the claim verifier refuted an answer sentence on Q3\n", text);
    }

    [Fact]
    public void ARefutedGradersStatement_IsNotAnAnswerSentence_InTheEvidenceLine()
    {
        string text = Render(BenchmarkReportAudience.TechnicalReport, BenchmarkReportDisclosure.Full, BenchmarkReportPeerNaming.Named);

        // Q1's only refuted ruling is a grader's statement.
        Assert.Contains("  - *Evidence:* One grader — different provider — accuracy · Q1 (90 / 100)\n", text);
        Assert.DoesNotContain("refuted an answer sentence on Q1", text);
    }

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
    [InlineData(BenchmarkReportAudience.TechnicalReport)]
    [InlineData(BenchmarkReportAudience.InternalBrief)]
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
    [InlineData(BenchmarkReportAudience.TechnicalReport)]
    [InlineData(BenchmarkReportAudience.InternalBrief)]
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
        if (audience != BenchmarkReportAudience.InternalBrief)
        {
            Assert.Contains("## Evaluation terms", text);
        }
    }

    [Fact]
    public void TheStamp_FollowsTheAudience()
    {
        string executiveFull = Render(BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportDisclosure.Full, BenchmarkReportPeerNaming.Named);
        string executiveDetailed = Render(BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportDisclosure.Detailed, BenchmarkReportPeerNaming.Named);
        string executiveSummary = Render(BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportDisclosure.Summary, BenchmarkReportPeerNaming.Named);
        string researcherFull = Render(BenchmarkReportAudience.TechnicalReport, BenchmarkReportDisclosure.Full, BenchmarkReportPeerNaming.Named);
        string researcherDetailed = Render(BenchmarkReportAudience.TechnicalReport, BenchmarkReportDisclosure.Detailed, BenchmarkReportPeerNaming.Named);

        Assert.Contains("*INTERNAL — unpublished benchmark results. Do not share outside the Overseer team.*\n", executiveFull);
        Assert.Contains("*Confidential. Prepared for the model's provider. Review before sharing.*\n", executiveDetailed);
        Assert.Contains("*Confidential. Prepared for the model's provider. Questions are described, not quoted.*\n", executiveSummary);
        Assert.DoesNotContain("contains benchmark questions", executiveFull);
        Assert.DoesNotContain("Contains benchmark questions", executiveDetailed);
        Assert.Contains("*INTERNAL — contains benchmark questions and rubrics. Do not share outside the Overseer team.*\n", researcherFull);
        Assert.Contains("*Confidential. Prepared for the model's provider. Contains benchmark questions — do not publish.*\n", researcherDetailed);
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

        Assert.Contains("- **Intelligence:** 80 / 100 (interval 77–83), joint 1st of 3 (intervals overlap); its 95 % interval overlaps every peer's.\n", text);
        Assert.Contains("- **Critical errors:** 1 of 4 answers.\n", text);
        Assert.DoesNotContain("±", text);
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
            + "readability, weighted Accuracy 55 %, Completeness 25 %, Conciseness 10 %, Readability 10 %. One of them is from the "
            + "same company as the model under test, which may read it more favorably. A separate verifier", oneOfTwo);
    }

    [Fact]
    public void TheCostSentence_FollowsThePricingBasis_AndAnOlderDocumentKeepsItsOwn()
    {
        var document = Document(BenchmarkReportAudience.TechnicalReport);
        var options = new BenchmarkReportRenderOptions { PeerNaming = BenchmarkReportPeerNaming.Named };

        string catalog = BenchmarkReportPackRenderer.Render(Document(BenchmarkReportAudience.ExecutiveSummary), options);
        Assert.Contains(" Cost is the model under test's price per question at the catalog prices of 2026-09-20, which is comparable "
            + "across dates but is not what was actually spent.\n", catalog);

        string researcher = BenchmarkReportPackRenderer.Render(document, options);
        Assert.Contains("- **Pricing basis:** Catalog prices on 2026-09-20 (price card dated 2026-09-01)\n", researcher);
        Assert.DoesNotContain("- **Price card date:**", researcher);

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

        document.FactsJson = BenchmarkReportJson.Serialize(sheet);
        string olderResearcher = BenchmarkReportPackRenderer.Render(document, options);
        Assert.Contains("- **Pricing basis:** Priced from the catalog as of 2026-09-20.", olderResearcher);
        Assert.Contains("- **Price card date:** 2026-09-01\n", olderResearcher);
    }

    [Fact]
    public void TheResearcherReport_HasASpeedAndCostTable_AfterItsResults()
    {
        string text = Render(BenchmarkReportAudience.TechnicalReport, BenchmarkReportDisclosure.Summary, BenchmarkReportPeerNaming.Named);

        Assert.Contains("## Speed and cost\n\n| Measure | GPT-5.6 Luna |\n|---|---|\n"
            + "| Median answer time | 12.3 s |\n"
            + "| 90th-percentile answer time | 15.0 s |\n"
            + "| Cost per question | $0.036 |\n"
            + "| Cost per run | $0.144 |\n"
            + "| Input tokens per question | 18,250 |\n"
            + "| Output tokens per question | 1,140 |\n\n", text);
        Assert.True(text.IndexOf("## Results against peers", StringComparison.Ordinal) < text.IndexOf("## Speed and cost", StringComparison.Ordinal));
        Assert.True(text.IndexOf("## Speed and cost", StringComparison.Ordinal) < text.IndexOf("## Why it scored this way", StringComparison.Ordinal));
        Assert.DoesNotContain("Total cost per run", text);
    }

    [Fact]
    public void TheSpeedAndCostTable_IsLeftOut_WhenTheSheetStatesNoneOfItsFacts()
    {
        var document = Document(BenchmarkReportAudience.TechnicalReport);
        var sheet = Sheet();
        sheet.Facts.RemoveAll(f => BenchmarkReportPackRenderer.SpeedAndCostKeys().Contains(f.Key));
        sheet.Facts.Add(new BenchmarkReportFact { Key = "cost.totalRunPerRun", Display = BenchmarkReportFacts.NotAvailable, Available = false, UnavailableReason = "Not recorded." });
        document.FactsJson = BenchmarkReportJson.Serialize(sheet);

        string text = BenchmarkReportPackRenderer.Render(document, new BenchmarkReportRenderOptions());

        Assert.DoesNotContain("## Speed and cost", text);
    }

    [Fact]
    public void TheResearcherReport_PrintsEachSlotBeforeItsList_UnderItsOwnHeading()
    {
        string text = Render(BenchmarkReportAudience.TechnicalReport, BenchmarkReportDisclosure.Summary, BenchmarkReportPeerNaming.Named);

        Assert.Contains("## Why it scored this way\n\nOne critical error on Q3 capped that answer at 25.\n\nCompleteness was 70 against a peer mean of 78.\n\n### Weaknesses\n\n- Asserted", text);
        Assert.Contains("## What worked well\n\nShort, accurate answers on simple questions (R2).\n\n### Strengths\n\n- Answers simple", text);
        Assert.Contains("## Recommendations for model developers\n\n- Verify object-destruction rules", text);
    }

    [Fact]
    public void ThePerQuestionTable_CountsRefutedAnswerSentences_AndPrintsWholeToolCalls()
    {
        string text = Render(BenchmarkReportAudience.TechnicalReport, BenchmarkReportDisclosure.Summary, BenchmarkReportPeerNaming.Named);

        Assert.Contains("| Q | Topic | Assessed band | Authored | Score | Peer mean | Difference | Critical error | Refuted answer sentences | Tool calls | Model time |", text);
        // The fixture's sheet records no authored band.
        Assert.Contains("| Q3 | Breaking a thrown gem | Intermediate | — | 25 | 68 | -43 | yes | 1 | 5 | 15.2 s |", text);
        Assert.DoesNotContain("| Refuted claims |", text);
    }

    [Fact]
    public void AtFull_EachClaimRulingNamesItsRole()
    {
        string text = Render(BenchmarkReportAudience.TechnicalReport, BenchmarkReportDisclosure.Full, BenchmarkReportPeerNaming.Named);

        Assert.Contains("- **Grader's statement — refuted (the verifier sided with the answer):** \"Worthless glass angers the unicorn.\" — "
            + "Glass is caught and returned without anger (dothrow.c).\n", text);
        Assert.Contains("- **Answer sentence — supported:** \"The timeout is typically near 350.\"", text);
        Assert.Contains("- **Answer sentence accused by a grader — refuted (the verifier sided with the grader):** \"A thrown gem always shatters on impact.\"", text);
        Assert.DoesNotContain("(the grader was right)", text);
        Assert.DoesNotContain("(the answer was right)", text);
        Assert.Contains("- **R1:** Panel member A: States that a thrown gem always shatters. · Panel member B: Claims the gem is always destroyed.\n", text);
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
            StoredV2Document(BenchmarkReportAudience.TechnicalReport),
            new BenchmarkReportRenderOptions { Disclosure = BenchmarkReportDisclosure.Full, PeerNaming = BenchmarkReportPeerNaming.Named });

        Assert.Contains("| Refuted claims |", text);
        Assert.Contains("  - *Evidence:* Both graders — critical error · Q3 (25 / 100) · the claim verifier refuted a claim on Q3\n", text);
        Assert.Contains("- **refuted:** \"A thrown gem always shatters on impact.\"", text);
        Assert.Contains("- **Pricing basis:** Priced from the catalog as of 2026-09-20.", text);
        Assert.Contains("- **Price card date:** 2026-09-01\n", text);
        Assert.Contains("Cost is list price for the model under test, on this basis:", BenchmarkReportPackRenderer.Render(
            StoredV2Document(BenchmarkReportAudience.ExecutiveSummary), new BenchmarkReportRenderOptions()));
        Assert.Contains("| Median answer time | 12.3 s |\n| Cost per question | $0.036 |\n\n", text);
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
    public void TheInternalBrief_PrintsNoEvidenceLinesAndNoEvaluationTerms()
    {
        string text = Render(BenchmarkReportAudience.InternalBrief, BenchmarkReportDisclosure.Full, BenchmarkReportPeerNaming.Named);

        Assert.DoesNotContain("*Evidence:*", text);
        Assert.DoesNotContain("## Evaluation terms", text);
    }

    [Theory]
    [InlineData(BenchmarkReportAudience.ExecutiveSummary)]
    [InlineData(BenchmarkReportAudience.TechnicalReport)]
    public void BothDocuments_EndWithTheEvaluationTerms_BeforeTheFooter(BenchmarkReportAudience audience)
    {
        string text = Render(audience, BenchmarkReportDisclosure.Summary, BenchmarkReportPeerNaming.Anonymized);

        int terms = text.IndexOf("## Evaluation terms\n", StringComparison.Ordinal);
        int footer = text.IndexOf("\n---\n", StringComparison.Ordinal);
        Assert.True(terms > 0 && terms < footer, "The evaluation terms do not come right before the footer.");
        Assert.Contains("- **Purpose statement:** " + PurposeStatement + "\n", text);
        Assert.Contains("- **Distillation / training prohibition:** No prompt, completion, or evaluation output in this benchmark is used for model training, fine-tuning, distillation, or developing competing AI models.", text);
        Assert.Contains("- **Third-party model content:** Outputs generated by **GPT-5.6 Luna** (OpenAI), graded by models from Google, Anthropic and DeepSeek, "
            + "and described in this document by **Claude Opus 5.5** (Anthropic) are third-party content", text);
    }

    [Fact]
    public void TheEvaluationTerms_WithholdAGradersProviderThatAnAnonymizedPeerShares()
    {
        var document = Document(BenchmarkReportAudience.ExecutiveSummary);
        var sheet = Sheet();
        sheet.Graders[1].Provider = "xAI";
        document.FactsJson = BenchmarkReportJson.Serialize(sheet);

        string anonymized = BenchmarkReportPackRenderer.Render(document, new BenchmarkReportRenderOptions { PeerNaming = BenchmarkReportPeerNaming.Anonymized });
        string named = BenchmarkReportPackRenderer.Render(document, new BenchmarkReportRenderOptions { PeerNaming = BenchmarkReportPeerNaming.Named });

        Assert.Contains("graded by models from Google, a withheld provider and DeepSeek,", anonymized);
        Assert.DoesNotContain("xAI", anonymized);
        Assert.Contains("graded by models from Google, xAI and DeepSeek,", named);
    }

    [Fact]
    public void ADocumentWithoutPurposeStatements_SaysSo()
    {
        var document = Document(BenchmarkReportAudience.TechnicalReport);
        var sheet = Sheet();
        sheet.PurposeStatements.Clear();
        document.FactsJson = BenchmarkReportJson.Serialize(sheet);

        string text = BenchmarkReportPackRenderer.Render(document, new BenchmarkReportRenderOptions());

        Assert.Contains("- **Purpose statement:** not recorded for this document.\n", text);
    }

    [Theory]
    [InlineData(BenchmarkReportAudience.ExecutiveSummary)]
    [InlineData(BenchmarkReportAudience.TechnicalReport)]
    public void TheStandaloneForm_HasNoPeerTablesColumnsOrSignificanceStatement(BenchmarkReportAudience audience)
    {
        foreach (var disclosure in BenchmarkReportPackRenderer.AllowedDisclosures(audience))
        {
            foreach (var naming in new[] { BenchmarkReportPeerNaming.Named, BenchmarkReportPeerNaming.Anonymized })
            {
                string text = BenchmarkReportPackRenderer.Render(
                    StandaloneDocument(audience), new BenchmarkReportRenderOptions { Disclosure = disclosure, PeerNaming = naming });

                Assert.Contains("- **Peers:** none; this is a stand-alone report\n", text);
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

        if (audience == BenchmarkReportAudience.TechnicalReport)
        {
            string technical = BenchmarkReportPackRenderer.Render(StandaloneDocument(audience), new BenchmarkReportRenderOptions());
            Assert.Contains("## Results\n", technical);
            Assert.Contains("| Q | Topic | Assessed band | Authored | Score | Critical error | Refuted answer sentences | Tool calls | Model time |", technical);
            Assert.Contains("### Questions scoring below 50 or with a critical error", technical);
            Assert.Contains("**Q3** (Breaking a thrown gem): Claimed a thrown gem always shatters", technical);
            Assert.Contains("describes the model on its own", technical);
        }
    }

    [Fact]
    public void TheResearcherReport_IsTitledWithItsNewName()
    {
        string text = Render(BenchmarkReportAudience.TechnicalReport, BenchmarkReportDisclosure.Summary, BenchmarkReportPeerNaming.Named);

        Assert.StartsWith("# GPT-5.6 Luna on the Overseer GnollHack Assistant Benchmark — Report for AI Researchers and Developers\n", text);
        Assert.Equal("Report for AI Researchers and Developers", BenchmarkReportRenderService.AudienceName(BenchmarkReportAudience.TechnicalReport));
        Assert.Equal(
            "GPT-5.6 Luna on the Overseer GnollHack Assistant Benchmark — Report for AI Researchers and Developers",
            BenchmarkReportRenderService.CurrentTitle(BenchmarkReportAudience.TechnicalReport,
                "GPT-5.6 Luna on the Overseer GnollHack Assistant Benchmark — Technical Report"));
        Assert.Equal(
            "GPT-5.6 Luna on the Overseer GnollHack Assistant Benchmark — Executive Summary",
            BenchmarkReportRenderService.CurrentTitle(BenchmarkReportAudience.ExecutiveSummary,
                "GPT-5.6 Luna on the Overseer GnollHack Assistant Benchmark — Executive Summary"));
    }

    [Fact]
    public void TheInternalBrief_EmbedsTheFactSheet_WithSortedKeys_AndAnonymizedPeers()
    {
        string named = Render(BenchmarkReportAudience.InternalBrief, BenchmarkReportDisclosure.Full, BenchmarkReportPeerNaming.Named);
        string anonymized = Render(BenchmarkReportAudience.InternalBrief, BenchmarkReportDisclosure.Full, BenchmarkReportPeerNaming.Anonymized);

        Assert.Contains("```json\n{\n  \"entries\": [", named);
        Assert.Contains("\"label\": \"Grok 5\"", named);
        Assert.Contains("\"label\": \"Model A\"", anonymized);
        Assert.Contains("\"knownNames\": [", anonymized);
        Assert.Contains("\"GnollHack Core Suite\"", anonymized);
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
        string text = BenchmarkReportPackRenderer.Render(StoredV2Document(BenchmarkReportAudience.TechnicalReport), options);

        Assert.Contains("  - Panel member A: Gemini 3.8 Flash (Google, gemini-3.8-flash), different provider from the model under test\n", text);
        Assert.Contains("- Answers simple questions precisely and briefly. *(One grader — different provider)*\n"
            + "  - *Evidence:* One grader — different provider — accuracy · Q1 (90 / 100)\n", text);
        Assert.Contains("| R2 | strength · accuracy: Precise on the unicorn throwing rules. | Q1 | One grader — different provider |\n", text);
        Assert.Contains("Each grader's provider relation to the model under test is stated under Setup and method; "
            + "a grader from the model's own provider may read it more favorably.", text);
        Assert.DoesNotContain("family", text);
        Assert.DoesNotContain("*(Computed)*\n  - *Evidence:* One grader", text);

        string brief = BenchmarkReportPackRenderer.Render(
            StoredV2Document(BenchmarkReportAudience.InternalBrief),
            new BenchmarkReportRenderOptions { Disclosure = BenchmarkReportDisclosure.Full, PeerNaming = naming });
        Assert.Contains("\"supportLabel\": \"One grader — different provider\"", brief);
        Assert.DoesNotContain("different family", brief);
        Assert.Contains("\"sameFamilyAsSubject\": false", brief);
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
        var technical = Document(BenchmarkReportAudience.TechnicalReport);
        technical.WriterProvider = writerProvider;
        var brief = Document(BenchmarkReportAudience.InternalBrief);
        brief.WriterProvider = writerProvider;
        var options = new BenchmarkReportRenderOptions { Disclosure = BenchmarkReportDisclosure.Full, PeerNaming = BenchmarkReportPeerNaming.Named };

        string executiveText = BenchmarkReportPackRenderer.Render(executive, options);
        string technicalText = BenchmarkReportPackRenderer.Render(technical, options);
        string briefText = BenchmarkReportPackRenderer.Render(brief, options);

        Assert.Contains("a span of 6 points, and rests on 4 of 4 questions with a scored answer.\n\n" + WriterCaveat + "\n\n", executiveText);
        int reliable = executiveText.IndexOf("## How reliable this result is", StringComparison.Ordinal);
        int about = executiveText.IndexOf("## About this benchmark", StringComparison.Ordinal);
        int caveat = executiveText.IndexOf(WriterCaveat, StringComparison.Ordinal);
        Assert.True(reliable < caveat && caveat < about);

        int threats = technicalText.IndexOf("## Threats to validity", StringComparison.Ordinal);
        int reproducibility = technicalText.IndexOf("## Reproducibility appendix", StringComparison.Ordinal);
        int technicalCaveat = technicalText.IndexOf("- " + WriterCaveat + "\n", StringComparison.Ordinal);
        Assert.True(threats < technicalCaveat && technicalCaveat < reproducibility);

        Assert.DoesNotContain("the provider of the model under test", briefText);
    }

    [Theory]
    [InlineData(BenchmarkReportAudience.ExecutiveSummary)]
    [InlineData(BenchmarkReportAudience.TechnicalReport)]
    public void AnIndependentWriter_GetsNoIndependenceCaveat(BenchmarkReportAudience audience)
    {
        string text = BenchmarkReportPackRenderer.Render(Document(audience), new BenchmarkReportRenderOptions());

        Assert.DoesNotContain("the provider of the model under test", text);
        Assert.DoesNotContain("A writer from the same provider", text);
    }

    [Fact]
    public void TheExecutiveSummary_StatesTheIntervalSpan_OnlyWhenTheSheetHasIt()
    {
        string text = Render(BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportDisclosure.Summary, BenchmarkReportPeerNaming.Named);
        Assert.Contains("## How reliable this result is\n\nThe result rests on 4 questions; its 95 % interval overlaps every peer's.\n\n"
            + "The 95 % interval is 77–83, a span of 6 points, and rests on 4 of 4 questions with a scored answer.\n\n", text);

        string older = BenchmarkReportPackRenderer.Render(StoredV2Document(BenchmarkReportAudience.ExecutiveSummary), new BenchmarkReportRenderOptions());
        Assert.DoesNotContain("a span of", older);
    }

    [Fact]
    public void AtFull_ACutAnswerIsPrintedWhole_AndAnUncutOneAsItsExcerpt()
    {
        string text = Render(BenchmarkReportAudience.TechnicalReport, BenchmarkReportDisclosure.Full, BenchmarkReportPeerNaming.Named);
        var q3 = Content().Runs[0].Questions.Single(q => q.Number == 3);
        var q1 = Content().Runs[0].Questions.Single(q => q.Number == 1);

        Assert.Contains("**Answer:**\n\n> Yes. A thrown gem always shatters on impact, so never throw your valuable gems at a unicorn; "
            + "keep them for selling or for wishing instead.\n>\n> If you must throw something, throw worthless glass.\n", text);
        Assert.DoesNotContain("> " + q3.AnswerExcerpt + "\n", text);
        Assert.Contains("**Answer:**\n\n> " + q1.AnswerExcerpt + "\n", text);
        Assert.DoesNotContain("**Answer excerpt:**", text);
        Assert.DoesNotContain("predates complete-answer capture", text);
    }

    [Fact]
    public void AtFull_ACutExcerptWithoutAStoredAnswer_CarriesTheOlderDocumentNote()
    {
        string text = BenchmarkReportPackRenderer.Render(
            StoredV2Document(BenchmarkReportAudience.TechnicalReport),
            new BenchmarkReportRenderOptions { Disclosure = BenchmarkReportDisclosure.Full, PeerNaming = BenchmarkReportPeerNaming.Named });
        var q3 = Content().Runs[0].Questions.Single(q => q.Number == 3);

        Assert.Contains("**Answer excerpt:**\n\n> " + q3.AnswerExcerpt + "\n\n"
            + "*This document predates complete-answer capture; the answer is shown as the excerpt stored when it was written.*\n", text);
        Assert.Single(AllIndexesOf(text, "predates complete-answer capture"));
        Assert.Single(AllIndexesOf(text, "**Answer excerpt:**"));
        Assert.DoesNotContain("selling or for wishing", text);
    }

    [Fact]
    public void AtDetailed_OnlyTheStoredExcerptIsPrinted()
    {
        string text = Render(BenchmarkReportAudience.TechnicalReport, BenchmarkReportDisclosure.Detailed, BenchmarkReportPeerNaming.Named);

        Assert.Equal(4, AllIndexesOf(text, "**Answer excerpt:**").Count);
        Assert.DoesNotContain("**Answer:**", text);
        Assert.DoesNotContain("selling or for wishing", text);
        Assert.DoesNotContain("predates complete-answer capture", text);
    }

    private const string ExecutionOnlyNote = "*Recorded success or failure describes whether a tool call executed. It does not show that the "
        + "query was well chosen, that the result was relevant, or that the corpus was current.*";

    [Theory]
    [InlineData(BenchmarkReportDisclosure.Summary)]
    [InlineData(BenchmarkReportDisclosure.Detailed)]
    [InlineData(BenchmarkReportDisclosure.Full)]
    public void ToolUse_SaysSuccessMeansExecution_AndPointsToTheToolCallLogAtFullOnly(BenchmarkReportDisclosure disclosure)
    {
        string text = Render(BenchmarkReportAudience.TechnicalReport, disclosure, BenchmarkReportPeerNaming.Named);

        Assert.Contains("- **" + BenchmarkReportFactLabels.Label("tools.refusedByBudget") + ":** 0\n\n" + ExecutionOnlyNote + "\n\n", text);
        const string pointer = "*Per-call arguments and results are in each run's Tool-call log until the retention sweep prunes them.*";
        if (disclosure == BenchmarkReportDisclosure.Full)
        {
            Assert.Contains(ExecutionOnlyNote + "\n\n" + pointer + "\n\n", text);
        }
        else
        {
            Assert.DoesNotContain("Tool-call log", text);
        }
        Assert.DoesNotContain("did not record per-call", text);
    }

    [Fact]
    public void ToolUse_AtFull_SaysWhenTheRunsRecordedNoPerCallRows()
    {
        var document = Document(BenchmarkReportAudience.TechnicalReport);
        var sheet = Sheet();
        foreach (var fact in sheet.Facts.Where(f => f.Key is "tools.failed" or "tools.refusedByBudget"))
        {
            fact.Display = BenchmarkReportFacts.NotAvailable;
            fact.Available = false;
            fact.UnavailableReason = "The runs recorded no per-call rows.";
        }
        document.FactsJson = BenchmarkReportJson.Serialize(sheet);

        string text = BenchmarkReportPackRenderer.Render(document,
            new BenchmarkReportRenderOptions { Disclosure = BenchmarkReportDisclosure.Full, PeerNaming = BenchmarkReportPeerNaming.Named });

        Assert.Contains(ExecutionOnlyNote + "\n\n*These runs did not record per-call arguments or results.*\n\n", text);
        Assert.DoesNotContain("Tool-call log", text);
    }

    // ---------------------------------------------------------------------------------------------
    // Format version 6: support wording, findings table, reference reader, knowledge base, style
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void TheFormatVersion_IsEleven()
    {
        Assert.Equal(11, BenchmarkReportPackRenderer.ReportFormatVersion);
    }

    [Fact]
    public void ABatteryPerQuestionTable_LeavesOutTheRunsColumn_WhenOneRunScoredEachQuestion()
    {
        var options = new BenchmarkReportRenderOptions { Disclosure = BenchmarkReportDisclosure.Full, PeerNaming = BenchmarkReportPeerNaming.Named };
        var document = BatteryReportFixture.Document(BenchmarkReportAudience.TechnicalReport);
        var sheet = BatteryReportFixture.Sheet();
        foreach (var question in sheet.Questions) question.RunCount = 1;
        document.FactsJson = BenchmarkReportJson.Serialize(sheet);

        string text = BenchmarkReportPackRenderer.Render(document, options);

        Assert.Contains("| Question | Topic | Assessed band | Authored | Mean score | Critical errors | Refuted answer sentences | Tool calls | Model time |\n"
            + "|---|---|---|---|---|---|---|---|---|\n", text);
        Assert.Contains("| S2-Q1 | Breaking a thrown gem | Intermediate | — | 25 | 1 | 1 | 5 | 15.2 s |\n", text);
        Assert.DoesNotContain("Runs scored", text);
        Assert.Contains("*One member run scored each question, so each mean score is that run's score; its critical errors count whether that run had a critical error.*", text);

        // Two runs per question keep the column and the pooled wording.
        string pooled = BenchmarkReportPackRenderer.Render(BatteryReportFixture.Document(BenchmarkReportAudience.TechnicalReport), options);
        Assert.Contains("| Mean score | Runs scored | Critical errors |", pooled);
        Assert.Contains("| S2-Q1 | Breaking a thrown gem | Intermediate | — | 25 | 2 | 1 | 1 | 5 | 15.2 s |\n", pooled);
        Assert.Contains("*Each question's mean score is over the member runs that scored it; its critical errors count those runs with a critical error.*", pooled);
    }

    [Fact]
    public void AnItemCitingNoRow_ReadsFromPerQuestionResults_WhileTheStoredLabelStaysComputed()
    {
        string text = Render(BenchmarkReportAudience.TechnicalReport, BenchmarkReportDisclosure.Detailed, BenchmarkReportPeerNaming.Named);

        Assert.Contains("- Scored lowest on intermediate questions (49). *(From per-question results)*\n", text);
        Assert.DoesNotContain("(Computed)", text);
        Assert.Equal("Computed", BenchmarkReportFacts.SupportComputed);
        Assert.Equal("From per-question results", BenchmarkReportPackRenderer.SupportLabelText(BenchmarkReportFacts.SupportComputed));
        Assert.Equal(BenchmarkReportFacts.SupportBothGraders, BenchmarkReportPackRenderer.SupportLabelText(BenchmarkReportFacts.SupportBothGraders));
    }

    [Fact]
    public void AStrengthsEvidenceLine_NeverListsARefutation()
    {
        var document = Document(BenchmarkReportAudience.TechnicalReport);
        var writer = Writer();
        writer.Strengths = new List<BenchmarkReportWriterItem>
        {
            new() { Text = "Named the right object on Q3.", Questions = new List<int> { 3 }, Evidence = new List<string> { "Q3" } }
        };
        document.WriterOutputJson = BenchmarkReportJson.Serialize(writer);

        string text = BenchmarkReportPackRenderer.Render(document,
            new BenchmarkReportRenderOptions { Disclosure = BenchmarkReportDisclosure.Detailed, PeerNaming = BenchmarkReportPeerNaming.Named });

        Assert.Contains("- Named the right object on Q3. *(From per-question results)*\n  - *Evidence:* Q3 (25 / 100)\n", text);
        // A weakness citing the same question still lists it.
        Assert.Contains("*(Both graders)*\n"
            + "  - *Evidence:* Both graders — critical error · Q3 (25 / 100) · the claim verifier refuted an answer sentence on Q3\n", text);
    }

    [Fact]
    public void AtSummary_TheFindingsTableIsLeftOut_AndSaysWhere()
    {
        string text = Render(BenchmarkReportAudience.TechnicalReport, BenchmarkReportDisclosure.Summary, BenchmarkReportPeerNaming.Named);

        Assert.DoesNotContain("| Row | Finding |", text);
        Assert.DoesNotContain("States that a thrown gem always shatters.", text);
        Assert.Contains("*The graders' findings are listed at Detailed and Full disclosure only, since their wording may quote the benchmark's questions and answers.*\n", text);
    }

    [Fact]
    public void AtDetailed_TheFindingsTableCarriesEachFindingsTextCut_AndNoRecurrenceForOneRun()
    {
        string text = Render(BenchmarkReportAudience.TechnicalReport, BenchmarkReportDisclosure.Detailed, BenchmarkReportPeerNaming.Named);

        Assert.Contains("| Row | Finding | Questions | Support |\n|---|---|---|---|\n", text);
        Assert.Contains("| R1 | weakness · critical error: States that a thrown gem always shatters. | Q3 | Both graders |\n", text);
        Assert.Contains("| R3 | strength (A) vs weakness (B) · conciseness: A: Admirably brief. B: Too terse to be useful. | Q2 | Graders disagree |\n", text);
        Assert.DoesNotContain("Recurrence", text);
        Assert.DoesNotContain("1 of 1 run", text);

        var document = Document(BenchmarkReportAudience.TechnicalReport);
        var sheet = Sheet();
        sheet.Rows[0].MemberAText = string.Join(" ", Enumerable.Repeat("The answer states that a thrown gem always shatters.", 6));
        document.FactsJson = BenchmarkReportJson.Serialize(sheet);
        string cut = BenchmarkReportPackRenderer.Render(document,
            new BenchmarkReportRenderOptions { Disclosure = BenchmarkReportDisclosure.Detailed, PeerNaming = BenchmarkReportPeerNaming.Named });
        string row = cut.Split('\n').Single(l => l.StartsWith("| R1 |", StringComparison.Ordinal));
        string finding = row.Split(" | ")[1]["weakness · critical error: ".Length..];
        Assert.True(finding.Length <= 162, "The finding text is not cut: " + finding);
        Assert.EndsWith(BenchmarkReportContent.Ellipsis, finding);
    }

    [Fact]
    public void AtFull_TheFindingsTableKeepsCategories_AndTheTextsFollowWhole_AndAGroupShowsRecurrence()
    {
        string single = Render(BenchmarkReportAudience.TechnicalReport, BenchmarkReportDisclosure.Full, BenchmarkReportPeerNaming.Named);
        Assert.Contains("| R1 | weakness · critical error | Q3 | Both graders |\n", single);
        Assert.Contains("- **R1:** Panel member A: States that a thrown gem always shatters. · Panel member B: Claims the gem is always destroyed.\n", single);

        var document = Document(BenchmarkReportAudience.TechnicalReport);
        var sheet = Sheet();
        sheet.SubjectRunIds = new List<long> { 12, 15 };
        sheet.Rows[0].Recurrence = 2;
        document.FactsJson = BenchmarkReportJson.Serialize(sheet);
        string group = BenchmarkReportPackRenderer.Render(document,
            new BenchmarkReportRenderOptions { Disclosure = BenchmarkReportDisclosure.Full, PeerNaming = BenchmarkReportPeerNaming.Named });

        Assert.Contains("| Row | Finding | Questions | Support | Recurrence |\n|---|---|---|---|---|\n", group);
        Assert.Contains("| R1 | weakness · critical error | Q3 | Both graders | 2 of 2 runs |\n", group);
    }

    [Fact]
    public void GraderReliability_PrintsTheReferenceReader_WithItsCaveat()
    {
        string text = Render(BenchmarkReportAudience.TechnicalReport, BenchmarkReportDisclosure.Summary, BenchmarkReportPeerNaming.Named);

        Assert.Contains("- **Panel member B alone:** 79 / 100\n"
            + "- **Reference reader (advisory, third provider):** 90 / 100\n"
            + "- **Reference reader's mean offset from the panel:** +16.8 points. Its scores do not count toward the score; its neutrality between the two panel families is an assumption.\n"
            + "- **Response-style conflict:** none\n", text);
        Assert.DoesNotContain("It never scores", text);

        string older = BenchmarkReportPackRenderer.Render(StoredV2Document(BenchmarkReportAudience.TechnicalReport), new BenchmarkReportRenderOptions());
        Assert.DoesNotContain("Reference reader (advisory", older);
        Assert.DoesNotContain("Its scores do not count toward the score", older);
        Assert.Contains("- **Response-style conflict:** none\n", older);
    }

    [Fact]
    public void ToolUse_LeavesOutTheKnowledgeBaseCount_WhenNoQuestionIsAKnowledgeBaseTopic()
    {
        string text = Render(BenchmarkReportAudience.TechnicalReport, BenchmarkReportDisclosure.Summary, BenchmarkReportPeerNaming.Named);
        Assert.DoesNotContain(BenchmarkReportFactLabels.Label("tools.zeroKnowledgeBaseAnswers"), text);
        Assert.Contains("- **" + BenchmarkReportFactLabels.Label("tools.share.other") + ":** 0 %\n- **" + BenchmarkReportFactLabels.Label("tools.failed") + ":** 0\n", text);

        // A document stored before format version 6 prints the count it stored.
        string older = BenchmarkReportPackRenderer.Render(StoredV2Document(BenchmarkReportAudience.TechnicalReport), new BenchmarkReportRenderOptions());
        Assert.Contains("- **Answers without a knowledge-base article:** 4 of 4\n", older);
    }

    [Fact]
    public void AResponseStyleDisplayStoredWithYes_ReadsAsAClause_InProseAndInTheSection()
    {
        var document = Document(BenchmarkReportAudience.TechnicalReport);
        var sheet = Sheet();
        var style = sheet.Facts.Single(f => f.Key == "style.responseStyleConflict");
        style.Display = "yes: Completeness is the lowest dimension, 13.5 points below Accuracy";
        style.Value = JsonValue.Create(true);
        var writer = Writer();
        writer.Sections[BenchmarkReportSlots.WhyItScored] = "Because {{style.responseStyleConflict}}, completeness reads low.";
        document.FactsJson = BenchmarkReportJson.Serialize(sheet);
        document.WriterOutputJson = BenchmarkReportJson.Serialize(writer);

        string text = BenchmarkReportPackRenderer.Render(document, new BenchmarkReportRenderOptions());

        Assert.Contains("Because Completeness is the lowest dimension, 13.5 points below Accuracy, completeness reads low.", text);
        Assert.Contains("- **Response-style conflict:** Completeness is the lowest dimension, 13.5 points below Accuracy\n", text);
        Assert.DoesNotContain("yes: ", text);
    }

    [Fact]
    public void TheIntervalSentence_IsPrintedOnce_WithOneNounForTheScoredQuestions()
    {
        string text = Render(BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportDisclosure.Summary, BenchmarkReportPeerNaming.Named);
        Assert.Single(AllIndexesOf(text, "The 95 % interval is 77–83"));
        Assert.Contains("rests on 4 of 4 questions with a scored answer.", text);
        Assert.DoesNotContain("questions questions", text);

        // A writer paragraph that already places the interval is not followed by the computed sentence.
        var document = Document(BenchmarkReportAudience.ExecutiveSummary);
        var writer = Writer();
        writer.Sections[BenchmarkReportSlots.Confidence] = "The interval is {{quality.interval}}, a span of {{quality.intervalSpan}}.";
        document.WriterOutputJson = BenchmarkReportJson.Serialize(writer);
        string restated = BenchmarkReportPackRenderer.Render(document, new BenchmarkReportRenderOptions());
        Assert.Contains("The interval is 77–83, a span of 6 points.\n", restated);
        Assert.DoesNotContain("The 95 % interval is", restated);
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

    private const string PairedNote = "*Paired difference: mean per-question difference, subject minus peer, over the questions both answered; "
        + "95 % paired-bootstrap interval. It reflects question sampling only, is not adjusted for comparing several models, and is "
        + "not a significance test.*";

    [Fact]
    public void TheExecutiveSummary_SaysHowTheModelCompares_AfterItsKeyFigures()
    {
        string named = Render(BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportDisclosure.Summary, BenchmarkReportPeerNaming.Named);
        string anonymized = Render(BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportDisclosure.Summary, BenchmarkReportPeerNaming.Anonymized);

        Assert.Contains("## How it compares\n\n"
            + "| Model | Intelligence Index | Median answer time | Cost per question | Critical errors |\n|---|---|---|---|---|\n"
            + "| **GPT-5.6 Luna** | 80 (77–83) | 12.3 s | $0.036 | 1 of 4 answers |\n"
            + "| Grok 5 | 85 (81–89) | 9.8 s | $0.052 | not available |\n"
            + "| Mistral Large 4 | 78 (74–83) | not available | $0.021 | not available |\n\n"
            + "| Dimension | GPT-5.6 Luna | Peer mean | Difference |\n|---|---|---|---|\n"
            + "| Accuracy | 84 | 82 | +2 |\n"
            + "| Completeness | 70 | 78 | -8 |\n"
            + "| Conciseness | 88 | 85 | +3 |\n"
            + "| Readability | 90 | 89 | +1 |\n\n"
            + "GPT-5.6 Luna places joint 1st of 3 (intervals overlap) on intelligence, but its interval overlaps those of Grok 5 and Mistral Large 4, so the order "
            + "is not established. Its paired difference from Grok 5 is -4.6 points.\n\n## What it did well\n", named);
        Assert.Contains("| Model A | 85 (81–89) | 9.8 s | $0.052 | not available |\n", anonymized);
        Assert.Contains("its interval overlaps those of Model A and Model B, so the order is not established.", anonymized);

        Assert.True(named.IndexOf("## Key figures", StringComparison.Ordinal) < named.IndexOf("## How it compares", StringComparison.Ordinal));
        Assert.Single(AllIndexesOf(named, "## How it compares"));
    }

    [Fact]
    public void HowItCompares_BelongsToTheExecutiveSummaryWithPeers_Only_AndNoDocumentHasARankLine()
    {
        string standalone = BenchmarkReportPackRenderer.Render(StandaloneDocument(BenchmarkReportAudience.ExecutiveSummary), new BenchmarkReportRenderOptions());
        Assert.DoesNotContain("## How it compares", standalone);
        Assert.DoesNotContain("- **Rank:**", standalone);

        foreach (var audience in new[] { BenchmarkReportAudience.TechnicalReport, BenchmarkReportAudience.InternalBrief })
        {
            string text = Render(audience, BenchmarkReportDisclosure.Full, BenchmarkReportPeerNaming.Named);
            Assert.DoesNotContain("## How it compares", text);
            Assert.DoesNotContain("- **Rank:**", text);
        }

        Assert.DoesNotContain("- **Rank:**", Render(BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportDisclosure.Full, BenchmarkReportPeerNaming.Named));
    }

    [Fact]
    public void TheExecutiveSummarysKeyFigures_StateTheRankOnce_OnTheIntelligenceLine()
    {
        string text = Render(BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportDisclosure.Summary, BenchmarkReportPeerNaming.Named);

        Assert.Contains("## Key figures\n\n- **Intelligence:** 80 / 100 (interval 77–83), joint 1st of 3 (intervals overlap); its 95 % interval overlaps every peer's.\n"
            + "- **Speed:**", text);
        Assert.DoesNotContain("- **Rank:**", text);
        Assert.DoesNotContain("the order is not established where intervals overlap", text);
    }

    [Fact]
    public void ANamedCopy_NamesThePeersWhoseIntervalsOverlap_AndAnAnonymizedCopyKeepsTheLetters()
    {
        var document = Document(BenchmarkReportAudience.ExecutiveSummary);
        var sheet = Sheet();
        sheet.Facts.Single(f => f.Key == "quality.intervalOverlap").Value = JsonValue.Create(1);
        sheet.Facts.Single(f => f.Key == "quality.intervalOverlap").Display = "its 95 % interval overlaps that of Model A";
        var b = sheet.Facts.Single(f => f.Key == "peer.B.intervalOverlap");
        b.Value = JsonValue.Create(false);
        b.Display = "its 95 % interval does not overlap the subject's";
        // Model B's interval (70–76) sits below the subject's (77–83), as its overlap fact states.
        var entryB = sheet.Entries.Single(e => e.PeerLetter == "B");
        entryB.QualityIndex = 73.0;
        entryB.QualityLower = 70.0;
        entryB.QualityUpper = 76.0;
        document.FactsJson = BenchmarkReportJson.Serialize(sheet);

        string named = BenchmarkReportPackRenderer.Render(document, new BenchmarkReportRenderOptions { PeerNaming = BenchmarkReportPeerNaming.Named });
        string anonymized = BenchmarkReportPackRenderer.Render(document, new BenchmarkReportRenderOptions { PeerNaming = BenchmarkReportPeerNaming.Anonymized });

        Assert.Contains("- **Intelligence:** 80 / 100 (interval 77–83), joint 1st of 3 (intervals overlap); its 95 % interval overlaps that of Grok 5.\n", named);
        // The writer's {{quality.intervalOverlap}} token resolves the same way.
        Assert.Contains("The result rests on 4 questions; its 95 % interval overlaps that of Grok 5.\n", named);
        Assert.Contains("- **Intelligence:** 80 / 100 (interval 77–83), joint 1st of 3 (intervals overlap); its 95 % interval overlaps that of Model A.\n", anonymized);
        Assert.Contains("The result rests on 4 questions; its 95 % interval overlaps that of Model A.\n", anonymized);

        // With no overlap the stored sentence already names no model, and the rank is not joint.
        foreach (var fact in sheet.Facts.Where(f => f.Key is "peer.A.intervalOverlap" or "peer.B.intervalOverlap"))
        {
            fact.Value = JsonValue.Create(false);
        }
        sheet.Facts.Single(f => f.Key == "quality.intervalOverlap").Value = JsonValue.Create(0);
        sheet.Facts.Single(f => f.Key == "quality.intervalOverlap").Display = "its 95 % interval overlaps no other model's";
        var entryA = sheet.Entries.Single(e => e.PeerLetter == "A");
        entryA.QualityLower = 84.0;
        entryA.QualityUpper = 89.0;
        document.FactsJson = BenchmarkReportJson.Serialize(sheet);
        Assert.Contains("2nd of 3; its 95 % interval overlaps no other model's.\n",
            BenchmarkReportPackRenderer.Render(document, new BenchmarkReportRenderOptions { PeerNaming = BenchmarkReportPeerNaming.Named }));
    }

    [Fact]
    public void ANamedCopy_OfASheetWithoutPerPeerFacts_NamesTheLettersOfTheOverlapSentence()
    {
        string named = BenchmarkReportPackRenderer.Render(
            StoredV6Document(BenchmarkReportAudience.TechnicalReport),
            new BenchmarkReportRenderOptions { PeerNaming = BenchmarkReportPeerNaming.Named });
        string anonymized = BenchmarkReportPackRenderer.Render(
            StoredV6Document(BenchmarkReportAudience.TechnicalReport),
            new BenchmarkReportRenderOptions { PeerNaming = BenchmarkReportPeerNaming.Anonymized });

        Assert.Contains("*GPT-5.6 Luna: its 95 % interval overlaps those of Grok 5 and Mistral Large 4. This describes where", named);
        Assert.Contains("*GPT-5.6 Luna: its 95 % interval overlaps those of Models A and B. This describes where", anonymized);
    }

    [Theory]
    [InlineData(BenchmarkReportPeerNaming.Named, "with Mistral Large 4", "Asked about Mistral Large 4.")]
    [InlineData(BenchmarkReportPeerNaming.Anonymized, "with Model B", "Asked about Model B.")]
    public void JudgeDependentPairs_NameThePeer_InANamedCopy(BenchmarkReportPeerNaming naming, string pairs, string prose)
    {
        var document = Document(BenchmarkReportAudience.TechnicalReport);
        var sheet = Sheet();
        var fact = sheet.Facts.Single(f => f.Key == "panel.judgeDependentPairs");
        fact.Available = true;
        fact.UnavailableReason = null;
        fact.Display = "with Model B";
        fact.Value = JsonValue.Create(1);
        var writer = Writer();
        writer.Sections[BenchmarkReportSlots.WhatWorked] = "Asked about {{peer:B}}.";
        writer.Sections[BenchmarkReportSlots.WhyItScored] = "Pairs: {{panel.judgeDependentPairs}}.";
        document.FactsJson = BenchmarkReportJson.Serialize(sheet);
        document.WriterOutputJson = BenchmarkReportJson.Serialize(writer);

        string text = BenchmarkReportPackRenderer.Render(document, new BenchmarkReportRenderOptions { PeerNaming = naming });

        Assert.Contains("Pairs whose order depends on which panel member graded them, involving GPT-5.6 Luna: " + pairs + ".\n", text);
        Assert.Contains("Pairs: " + pairs + ".\n", text);
        Assert.Contains(prose, text);
    }

    [Theory]
    [InlineData(BenchmarkReportPeerNaming.Named, "Grok 5", "Mistral Large 4", "| xAI ")]
    [InlineData(BenchmarkReportPeerNaming.Anonymized, "Model A", "Model B", "")]
    public void TheResearcherReport_PrintsThePairedDifference_OnceInItsResults_WithItsNote(
        BenchmarkReportPeerNaming naming, string a, string b, string provider)
    {
        string text = Render(BenchmarkReportAudience.TechnicalReport, BenchmarkReportDisclosure.Summary, naming);

        Assert.Contains(" | Intelligence Index | 95 % interval | Rank | Paired difference |\n", text);
        Assert.Contains("| " + a + " " + provider + "| 85 | 81–89 | joint 1 | -4.6 points (-9.8 to +0.7) |\n", text);
        Assert.Contains("| " + b + " ", text);
        Assert.Contains(" | 78 | 74–83 | joint 1 | not available |\n", text);
        Assert.Contains(" | 80 | 77–83 | joint 1 | — |\n", text);
        Assert.Contains(PairedNote + "\n\nNo paired difference:\n\n- " + b + ": " + PairedTooFewReason + "\n\n", text);
        Assert.Single(AllIndexesOf(text, PairedNote));

        int results = text.IndexOf("## Results against peers", StringComparison.Ordinal);
        int speed = text.IndexOf("### Speed", StringComparison.Ordinal);
        int note = text.IndexOf(PairedNote, StringComparison.Ordinal);
        Assert.True(results < note && note < speed);

        Assert.DoesNotContain("Paired difference", Render(BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportDisclosure.Full, naming));
        Assert.DoesNotContain("Paired difference", BenchmarkReportPackRenderer.Render(StandaloneDocument(BenchmarkReportAudience.TechnicalReport), new BenchmarkReportRenderOptions()));
    }

    [Theory]
    [InlineData(BenchmarkReportPeerNaming.Named)]
    [InlineData(BenchmarkReportPeerNaming.Anonymized)]
    public void TheResearcherReport_ListsTheComparedModels_UnderSetupAndMethod(BenchmarkReportPeerNaming naming)
    {
        string text = Render(BenchmarkReportAudience.TechnicalReport, BenchmarkReportDisclosure.Summary, naming);

        if (naming == BenchmarkReportPeerNaming.Named)
        {
            Assert.Contains("### Compared models\n\n"
                + "| Model | Provider | Letter | Kind | Runs | Thinking level | Harness version | Run dates (UTC) |\n|---|---|---|---|---|---|---|---|\n"
                + "| **GPT-5.6 Luna** | OpenAI | — | run | 1 | high | 41 | 2026-09-20 |\n"
                + "| Grok 5 | xAI | A | run | 1 | high | 41 | 2026-09-19 |\n"
                + "| Mistral Large 4 | Mistral | B | run | 1 | not set | 41 | 2026-09-12 |\n\n", text);
        }
        else
        {
            Assert.Contains("### Compared models\n\n"
                + "| Model | Letter | Kind | Runs | Thinking level | Harness version | Run dates (UTC) |\n|---|---|---|---|---|---|---|\n"
                + "| **GPT-5.6 Luna** | — | run | 1 | high | 41 | 2026-09-20 |\n"
                + "| Model A | A | run | 1 | high | 41 | 2026-09-19 |\n"
                + "| Model B | B | run | 1 | not set | 41 | 2026-09-12 |\n\n", text);
        }

        int setup = text.IndexOf("## Setup and method", StringComparison.Ordinal);
        int compared = text.IndexOf("### Compared models", StringComparison.Ordinal);
        int results = text.IndexOf("## Results against peers", StringComparison.Ordinal);
        Assert.True(setup < compared && compared < results);

        // The stand-alone form keeps its single-run setup lines.
        string standalone = BenchmarkReportPackRenderer.Render(StandaloneDocument(BenchmarkReportAudience.TechnicalReport), new BenchmarkReportRenderOptions());
        Assert.DoesNotContain("### Compared models", standalone);
        Assert.Contains("- **Model under test:** GPT-5.6 Luna (OpenAI, gpt-5.6-luna), thinking level high; 1 run.\n", standalone);
    }

    [Fact]
    public void ComparedModels_SpansTheRunDates_AndMarksAGroup()
    {
        var document = Document(BenchmarkReportAudience.TechnicalReport);
        var sheet = Sheet();
        var peer = sheet.Entries.Single(e => e.PeerLetter == "A");
        peer.EntryKey = "group:4";
        peer.RunCount = 3;
        peer.HarnessVersion = BenchmarkReportFacts.MixedHarnessVersion;
        peer.LastRunUtc = new DateTime(2026, 9, 22, 7, 0, 0, DateTimeKind.Utc);
        sheet.Peers.Single(p => p.Letter == "A").EntryKey = "group:4";
        document.FactsJson = BenchmarkReportJson.Serialize(sheet);

        string text = BenchmarkReportPackRenderer.Render(document,
            new BenchmarkReportRenderOptions { Disclosure = BenchmarkReportDisclosure.Summary, PeerNaming = BenchmarkReportPeerNaming.Anonymized });

        Assert.Contains("| Model A | A | group | 3 | high | mixed | 2026-09-19 to 2026-09-22 |\n", text);
    }

    [Fact]
    public void TheResearcherReport_EndsItsThreatsToValidity_WithTheWritersLimitations()
    {
        string named = Render(BenchmarkReportAudience.TechnicalReport, BenchmarkReportDisclosure.Summary, BenchmarkReportPeerNaming.Named);
        string anonymized = Render(BenchmarkReportAudience.TechnicalReport, BenchmarkReportDisclosure.Summary, BenchmarkReportPeerNaming.Anonymized);

        Assert.Contains("a grader from the model's own provider may read it more favorably.\n\n"
            + "Mistral Large 4 is degraded on speed, and every model rests on a single run, so no interval covers run-to-run variation.\n\n"
            + "## Reproducibility appendix\n", named);
        Assert.Contains("\n\nModel B is degraded on speed, and every model rests on a single run", anonymized);

        string standalone = BenchmarkReportPackRenderer.Render(StandaloneDocument(BenchmarkReportAudience.TechnicalReport), new BenchmarkReportRenderOptions());
        Assert.Contains("\n\nThe result rests on a single run, so its interval covers question sampling alone.\n\n## Reproducibility appendix\n", standalone);
    }

    [Theory]
    [InlineData(BenchmarkReportPeerNaming.Named, "Grok 5's Intelligence Index: 85 / 100")]
    [InlineData(BenchmarkReportPeerNaming.Anonymized, "Model A's Intelligence Index: 85 / 100")]
    public void APeersOwnFact_InAnEvidenceLine_IsLabeledWithThePeerNaming(BenchmarkReportPeerNaming naming, string expected)
    {
        var document = Document(BenchmarkReportAudience.TechnicalReport);
        var writer = Writer();
        writer.Weaknesses[1].Evidence = new List<string> { "band.intermediate.score", "peer.A.quality.index" };
        document.WriterOutputJson = BenchmarkReportJson.Serialize(writer);

        string text = BenchmarkReportPackRenderer.Render(document,
            new BenchmarkReportRenderOptions { Disclosure = BenchmarkReportDisclosure.Detailed, PeerNaming = naming });

        Assert.Contains("  - *Evidence:* Intermediate band score: 49 · " + expected + " · Q3 (25 / 100), Q2 (72 / 100)", text);
        if (naming == BenchmarkReportPeerNaming.Anonymized)
        {
            Assert.DoesNotContain("Grok 5", text);
        }
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
        if (audience == BenchmarkReportAudience.TechnicalReport)
        {
            // The table keeps the columns a version 6 sheet has data for.
            Assert.Contains("### Compared models\n\n", text);
            Assert.Contains(naming == BenchmarkReportPeerNaming.Named
                ? "| Model | Provider | Letter | Kind | Runs | Thinking level |\n"
                : "| Model | Letter | Kind | Runs | Thinking level |\n", text);
        }
        Assert.NotEmpty(file);
    }

    // ---------------------------------------------------------------------------------------------
    // Format version 8: significance wording, peer names, rounding, evidence lists, the comparison
    // context, withheld graders and figure markers
    // ---------------------------------------------------------------------------------------------

    private const string NoSignificanceOfThree =
        "No pair of models is tested for significance: with 3 models, testing every pair would flag chance differences.";

    [Theory]
    [InlineData(BenchmarkReportAudience.ExecutiveSummary)]
    [InlineData(BenchmarkReportAudience.TechnicalReport)]
    public void TheSignificanceStatement_IsInTheDocumentsOwnWords_AndPrintedOnce(BenchmarkReportAudience audience)
    {
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
        if (audience == BenchmarkReportAudience.ExecutiveSummary)
        {
            Assert.True(named.IndexOf("## How reliable this result is", StringComparison.Ordinal) < statement
                        && statement < named.IndexOf("## About this benchmark", StringComparison.Ordinal));
        }
        else
        {
            Assert.True(named.IndexOf("### Quality", StringComparison.Ordinal) < statement
                        && statement < named.IndexOf("### Speed", StringComparison.Ordinal));
        }
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
    public void ThePerQuestionDifference_IsTheDifferenceOfThePrintedNumbers()
    {
        var document = Document(BenchmarkReportAudience.TechnicalReport);
        var sheet = Sheet();
        var q1 = sheet.Questions.Single(q => q.Number == 1);
        q1.Score = 90.5;
        q1.PeerMean = 85.4;
        q1.Difference = q1.Score - q1.PeerMean;
        var q2 = sheet.Questions.Single(q => q.Number == 2);
        q2.Score = 80.4;
        q2.PeerMean = 81.6;
        q2.Difference = q2.Score - q2.PeerMean;
        document.FactsJson = BenchmarkReportJson.Serialize(sheet);

        string text = BenchmarkReportPackRenderer.Render(document, new BenchmarkReportRenderOptions { PeerNaming = BenchmarkReportPeerNaming.Named });

        // 91 - 85 and 80 - 82, not the rounded +5.1 and -1.2.
        Assert.Contains("| Q1 | Throwing gems at unicorns | Simple | — | 91 | 85 | +6 | no |", text);
        Assert.Contains("| Q2 | Prayer timeout | Intermediate | — | 80 | 82 | -2 | no |", text);
    }

    [Theory]
    [InlineData(BenchmarkReportAudience.TechnicalReport, BenchmarkReportDisclosure.Summary)]
    [InlineData(BenchmarkReportAudience.InternalBrief, BenchmarkReportDisclosure.Full)]
    public void TheNotedQuestionsHeading_StatesItsThreshold(BenchmarkReportAudience audience, BenchmarkReportDisclosure disclosure)
    {
        string text = Render(audience, disclosure, BenchmarkReportPeerNaming.Named);

        Assert.Contains("### Questions more than 15 points below the peer mean, or with a critical error\n", text);
        Assert.DoesNotContain("### Questions below the peer mean", text);
    }

    [Fact]
    public void AnEvidenceLine_ListsAtMostSixQuestions_LowestFirstForAWeakness_AndHighestFirstForAStrength()
    {
        var document = Document(BenchmarkReportAudience.TechnicalReport);
        var sheet = Sheet();
        // Q5 to Q10 join Q1 90, Q2 72, Q3 25 and Q4 87.
        double[] scores = { 60, 95, 40, 70, 55, 80 };
        for (int i = 0; i < scores.Length; i++)
        {
            sheet.Questions.Add(new BenchmarkReportQuestion
            {
                Number = 5 + i,
                QuestionKey = (105 + i).ToString(CultureInfo.InvariantCulture),
                OrderIndex = 5 + i,
                Band = "Simple",
                Score = scores[i],
                PeerMean = 70,
                Difference = scores[i] - 70,
                PeerCount = 2,
                RunCount = 1
            });
        }
        sheet.Rows.Add(new BenchmarkReportFindingRow
        {
            Id = "R4", Kind = "weakness", Category = "readability", Questions = new List<int>(),
            Status = "Convergent", SupportLabel = BenchmarkReportFacts.SupportBothGraders
        });
        var all = Enumerable.Range(1, 10).ToList();
        var writer = Writer();
        writer.Strengths = new List<BenchmarkReportWriterItem>
        {
            new() { Text = "Precise throughout.", Questions = all, Evidence = new List<string> { "R2" } }
        };
        writer.Weaknesses = new List<BenchmarkReportWriterItem>
        {
            new() { Text = "Wrong throughout.", Questions = all, Evidence = new List<string> { "R1" } },
            new() { Text = "Hard to scan.", Evidence = new List<string> { "R4" } }
        };
        document.FactsJson = BenchmarkReportJson.Serialize(sheet);
        document.WriterOutputJson = BenchmarkReportJson.Serialize(writer);

        string text = BenchmarkReportPackRenderer.Render(document,
            new BenchmarkReportRenderOptions { Disclosure = BenchmarkReportDisclosure.Detailed, PeerNaming = BenchmarkReportPeerNaming.Named });

        Assert.Contains("- Precise throughout. *(One grader — different provider)*\n"
            + "  - *Evidence:* One grader — different provider — accuracy · Q6 (95 / 100), Q1 (90 / 100), Q4 (87 / 100), Q10 (80 / 100), "
            + "Q2 (72 / 100), Q8 (70 / 100) and 4 more\n", text);
        Assert.Contains("- Wrong throughout. *(Both graders)*\n"
            + "  - *Evidence:* Both graders — critical error · Q3 (25 / 100), Q7 (40 / 100), Q9 (55 / 100), Q5 (60 / 100), "
            + "Q8 (70 / 100), Q2 (72 / 100) and 4 more · the claim verifier refuted an answer sentence on Q3\n", text);
        // A row with no questions gives no question list.
        Assert.Contains("- Hard to scan. *(Both graders)*\n  - *Evidence:* Both graders — readability\n", text);
    }

    [Fact]
    public void WithoutTheFactSheet_TheInternalBriefSaysWhereItIs()
    {
        var options = new BenchmarkReportRenderOptions
        {
            Disclosure = BenchmarkReportDisclosure.Full,
            PeerNaming = BenchmarkReportPeerNaming.Named,
            IncludeFactSheet = false
        };

        string text = BenchmarkReportPackRenderer.Render(Document(BenchmarkReportAudience.InternalBrief), options);

        Assert.Contains("## 6. Fact sheet\n\nThe fact sheet is in the Markdown copy of this document.\n\n", text);
        Assert.DoesNotContain("```json", text);
        Assert.True(new BenchmarkReportRenderOptions().IncludeFactSheet);
        Assert.Contains("## 6. Fact sheet\n\n```json\n", Render(BenchmarkReportAudience.InternalBrief, BenchmarkReportDisclosure.Full, BenchmarkReportPeerNaming.Named));
    }

    [Fact]
    public void HowItCompares_PrintsEachModelsOwnCriticalErrors_WhereTheSheetHasThem()
    {
        var document = Document(BenchmarkReportAudience.ExecutiveSummary);
        var sheet = Sheet();
        sheet.Entries.Single(e => e.IsSubject).Extra.Add(new BenchmarkReportFact { Key = "errors.critical", Display = "1 of 4 answers", Value = JsonValue.Create(1) });
        sheet.Entries.Single(e => e.PeerLetter == "A").Extra.Add(new BenchmarkReportFact { Key = "errors.critical", Display = "0 of 4 answers", Value = JsonValue.Create(0) });
        sheet.Entries.Single(e => e.PeerLetter == "B").Extra.Add(new BenchmarkReportFact
        {
            Key = "errors.critical", Display = BenchmarkReportFacts.NotAvailable, Available = false, UnavailableReason = "No scored answer."
        });
        document.FactsJson = BenchmarkReportJson.Serialize(sheet);

        string text = BenchmarkReportPackRenderer.Render(document, new BenchmarkReportRenderOptions { PeerNaming = BenchmarkReportPeerNaming.Named });

        Assert.Contains("| **GPT-5.6 Luna** | 80 (77–83) | 12.3 s | $0.036 | 1 of 4 answers |\n"
            + "| Grok 5 | 85 (81–89) | 9.8 s | $0.052 | 0 of 4 answers |\n"
            + "| Mistral Large 4 | 78 (74–83) | not available | $0.021 | not available |\n", text);
    }

    [Fact]
    public void TheInternalBriefsModelResult_StatesTheIntervalSpan_AfterItsKeyFigures()
    {
        string text = Render(BenchmarkReportAudience.InternalBrief, BenchmarkReportDisclosure.Full, BenchmarkReportPeerNaming.Named);

        Assert.Contains("- **Critical errors:** 1 of 4 answers.\n\n"
            + "The 95 % interval is 77–83, a span of 6 points, and rests on 4 of 4 questions with a scored answer.\n\n### Strengths\n", text);
        Assert.Single(AllIndexesOf(text, "The 95 % interval is"));
    }

    [Theory]
    [InlineData(BenchmarkReportPeerNaming.Named, "Grok 5")]
    [InlineData(BenchmarkReportPeerNaming.Anonymized, "Model A")]
    public void TheQualityBlock_SaysWhereAPairedIntervalExcludesZero(BenchmarkReportPeerNaming naming, string name)
    {
        var document = Document(BenchmarkReportAudience.TechnicalReport);
        var sheet = Sheet();
        var excludes = new BenchmarkReportFact { Key = "peer.A.pairedExcludesZero", Display = "the paired interval excludes zero", Value = JsonValue.Create(true) };
        sheet.Facts.Add(excludes);
        sheet.Facts.Add(new BenchmarkReportFact
        {
            Key = "peer.B.pairedExcludesZero", Display = BenchmarkReportFacts.NotAvailable, Available = false, UnavailableReason = PairedTooFewReason
        });
        document.FactsJson = BenchmarkReportJson.Serialize(sheet);
        var options = new BenchmarkReportRenderOptions { PeerNaming = naming };

        string text = BenchmarkReportPackRenderer.Render(document, options);

        string line = "On the same questions the paired difference with " + name + " excludes zero (not adjusted for several comparisons).";
        Assert.Single(AllIndexesOf(text, "\n\n" + line + "\n\n"));
        Assert.Single(AllIndexesOf(text, "excludes zero (not adjusted"));
        int overlap = text.IndexOf("*GPT-5.6 Luna: its 95 % interval overlaps", StringComparison.Ordinal);
        int at = text.IndexOf(line, StringComparison.Ordinal);
        Assert.True(overlap < at && at < text.IndexOf("### Speed", StringComparison.Ordinal));

        excludes.Display = "the paired interval includes zero";
        excludes.Value = JsonValue.Create(false);
        document.FactsJson = BenchmarkReportJson.Serialize(sheet);
        Assert.DoesNotContain("excludes zero", BenchmarkReportPackRenderer.Render(document, options));
    }

    [Fact]
    public void AnAnonymizedCopy_WithholdsAGradersIdentity_WhereAPeerSharesItsProvider()
    {
        var document = Document(BenchmarkReportAudience.TechnicalReport);
        var sheet = Sheet();
        sheet.Graders[1].Provider = "xAI";
        document.FactsJson = BenchmarkReportJson.Serialize(sheet);

        string anonymized = BenchmarkReportPackRenderer.Render(document,
            new BenchmarkReportRenderOptions { Disclosure = BenchmarkReportDisclosure.Full, PeerNaming = BenchmarkReportPeerNaming.Anonymized });
        string named = BenchmarkReportPackRenderer.Render(document,
            new BenchmarkReportRenderOptions { Disclosure = BenchmarkReportDisclosure.Full, PeerNaming = BenchmarkReportPeerNaming.Named });

        // Setup and method, the Reproducibility appendix and Full question details.
        Assert.Contains("  - Panel member B: a model from a withheld provider, different provider from the model under test\n", anonymized);
        Assert.Contains("  - Panel member A: Gemini 3.8 Flash (Google, gemini-3.8-flash), different provider from the model under test\n", anonymized);
        Assert.Contains("; Panel member B: a model from a withheld provider; Reference reader: DeepSeek deepseek-v4;", anonymized);
        Assert.Contains("- **Panel member B (a model from a withheld provider):** score 88.", anonymized);
        Assert.Contains("graded by models from Google, a withheld provider and DeepSeek,", anonymized);
        Assert.DoesNotContain("xAI", anonymized);
        Assert.DoesNotContain("Claude Haiku 5", anonymized);
        Assert.DoesNotContain("claude-haiku-5", anonymized);

        Assert.Contains("  - Panel member B: Claude Haiku 5 (xAI, claude-haiku-5), different provider from the model under test\n", named);
        Assert.Contains("; Panel member B: xAI claude-haiku-5; ", named);
        Assert.Contains("- **Panel member B (Claude Haiku 5):** score 88.", named);
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
        Assert.Contains("- **Peers:** none; this is a stand-alone report\n\n", standalone);
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

        switch (audience)
        {
            case BenchmarkReportAudience.ExecutiveSummary:
                AssertInOrder(
                    Pos("## How it compares"), Pos("| Readability | 90 | 89 | +1 |\n"),
                    At(keys[0]), At(keys[1]), At(keys[2]), At(keys[3]), At(keys[4]), At(keys[5]), At(keys[6]),
                    Pos("GPT-5.6 Luna places joint 1st of 3 (intervals overlap)"), Pos("## What it did well"));
                break;
            case BenchmarkReportAudience.TechnicalReport:
                AssertInOrder(
                    Pos("### Quality"), Pos("*GPT-5.6 Luna: its 95 % interval overlaps"), Pos("No pair of models"), At("p1a-quality"),
                    Pos("### Speed"), Pos("Not ranked on speed:"), At("p1b-speed"),
                    Pos("### Cost"), At("p1c-cost"), Pos("### Dimensions"),
                    Pos("### Judge-dependent pairs"), At("p2-profile"),
                    Pos("## Speed and cost"), Pos("| Output tokens per question |"),
                    At("s1-quality-speed"), At("s2-quality-cost"), At("s3-speed-cost"), Pos("## Why it scored this way"));
                break;
            default:
                AssertInOrder(
                    Pos("## 3. The model's result"), Pos("### Key figures"), Pos("The 95 % interval is 77–83"),
                    At(keys[0]), At(keys[1]), At(keys[2]), At(keys[3]), At(keys[4]), At(keys[5]), At(keys[6]),
                    Pos("### Strengths"), Pos("## 4. Leads"));
                break;
        }
        Assert.NotEmpty(file);
    }

    [Theory]
    [MemberData(nameof(Combinations))]
    public void WithoutCharts_AsTheMarkdownDownloadRendersIt_NoFigureMarkerIsWritten(
        BenchmarkReportAudience audience, BenchmarkReportDisclosure disclosure, BenchmarkReportPeerNaming naming, string file)
    {
        Assert.Empty(new BenchmarkReportRenderOptions().Charts);
        Assert.DoesNotContain("[[figure:", Render(audience, disclosure, naming));
        Assert.DoesNotContain("[[figure:", RenderWithCharts(Document(audience), disclosure, naming, Array.Empty<BenchmarkReportRenderChart>()));
        Assert.NotEmpty(file);
    }

    [Fact]
    public void OnlyTheChartsGiven_GetMarkers_AndAnUnknownKeyIsIgnored()
    {
        string text = RenderWithCharts(Document(BenchmarkReportAudience.TechnicalReport), BenchmarkReportDisclosure.Summary,
            BenchmarkReportPeerNaming.Named, Charts("p1b-speed", "no-such-figure", "p1b-speed"));

        Assert.Single(AllIndexesOf(text, "[[figure:"));
        Assert.Contains("\n\n" + MarkerOf("p1b-speed") + "\n\n### Cost\n", text);
    }

    [Theory]
    [InlineData(BenchmarkReportAudience.ExecutiveSummary)]
    [InlineData(BenchmarkReportAudience.TechnicalReport)]
    public void AStandaloneDocument_GetsNoFigureMarker_EvenWithCharts(BenchmarkReportAudience audience)
    {
        string text = RenderWithCharts(StandaloneDocument(audience), BenchmarkReportDisclosure.Full, BenchmarkReportPeerNaming.Named,
            Charts(BenchmarkReportChartPlacement.FigureKeys.ToArray()));

        Assert.DoesNotContain("[[figure:", text);
    }

    [Fact]
    public void ThePlacement_KnowsTheSevenFigures_AndWhereEachAudienceDrawsThem()
    {
        Assert.Equal(
            new[] { "p1a-quality", "p1b-speed", "p1c-cost", "p2-profile", "s1-quality-speed", "s2-quality-cost", "s3-speed-cost" },
            BenchmarkReportChartPlacement.FigureKeys);
        Assert.All(BenchmarkReportChartPlacement.FigureKeys, key => Assert.True(BenchmarkReportChartPlacement.IsKnown(key)));
        Assert.False(BenchmarkReportChartPlacement.IsKnown(null));
        Assert.False(BenchmarkReportChartPlacement.IsKnown(string.Empty));
        Assert.False(BenchmarkReportChartPlacement.IsKnown("P1A-QUALITY"));
        Assert.False(BenchmarkReportChartPlacement.IsKnown("p9-other"));

        Assert.Equal(BenchmarkReportChartPlacement.FigureKeys,
            BenchmarkReportChartPlacement.KeysAt(BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportChartAnchor.HowItCompares));
        Assert.Equal(BenchmarkReportChartPlacement.FigureKeys,
            BenchmarkReportChartPlacement.KeysAt(BenchmarkReportAudience.InternalBrief, BenchmarkReportChartAnchor.ModelResult));

        var researcher = BenchmarkReportAudience.TechnicalReport;
        Assert.Equal(new[] { "p1a-quality" }, BenchmarkReportChartPlacement.KeysAt(researcher, BenchmarkReportChartAnchor.QualityResults));
        Assert.Equal(new[] { "p1b-speed" }, BenchmarkReportChartPlacement.KeysAt(researcher, BenchmarkReportChartAnchor.SpeedResults));
        Assert.Equal(new[] { "p1c-cost" }, BenchmarkReportChartPlacement.KeysAt(researcher, BenchmarkReportChartAnchor.CostResults));
        Assert.Equal(new[] { "p2-profile" }, BenchmarkReportChartPlacement.KeysAt(researcher, BenchmarkReportChartAnchor.ResultsAgainstPeersEnd));
        Assert.Equal(new[] { "s1-quality-speed", "s2-quality-cost", "s3-speed-cost" },
            BenchmarkReportChartPlacement.KeysAt(researcher, BenchmarkReportChartAnchor.SpeedAndCost));
        Assert.Empty(BenchmarkReportChartPlacement.KeysAt(researcher, BenchmarkReportChartAnchor.HowItCompares));
        Assert.Empty(BenchmarkReportChartPlacement.KeysAt(BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportChartAnchor.QualityResults));

        Assert.Equal(BenchmarkReportChartAnchor.SpeedAndCost, BenchmarkReportChartPlacement.AnchorOf(researcher, "s2-quality-cost"));
        Assert.Equal(BenchmarkReportChartAnchor.ModelResult, BenchmarkReportChartPlacement.AnchorOf(BenchmarkReportAudience.InternalBrief, "p1a-quality"));
        Assert.Null(BenchmarkReportChartPlacement.AnchorOf(researcher, "p9-other"));

        Assert.Equal("[[figure:p2-profile]]", BenchmarkReportChartPlacement.Marker("p2-profile"));
        Assert.True(BenchmarkReportChartPlacement.TryParseMarker("  [[figure:p2-profile]] ", out string parsed));
        Assert.Equal("p2-profile", parsed);
        Assert.False(BenchmarkReportChartPlacement.TryParseMarker("[[figure:p9-other]]", out _));
        Assert.False(BenchmarkReportChartPlacement.TryParseMarker("See [[figure:p2-profile]]", out _));
        Assert.False(BenchmarkReportChartPlacement.TryParseMarker(null, out _));
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
    public void TheDifficultyBands_AreTheAssessedOnes_WithTheAuthoredCountBeside()
    {
        string text = BenchmarkReportPackRenderer.Render(Version9Document(BenchmarkReportAudience.TechnicalReport),
            new BenchmarkReportRenderOptions { Disclosure = BenchmarkReportDisclosure.Summary, PeerNaming = BenchmarkReportPeerNaming.Named });

        Assert.Contains("### Difficulty bands (assessed)\n\n"
            + "| Difficulty band | Questions | Authored questions | GPT-5.6 Luna | Peer mean | Difference |\n"
            + "|---|---|---|---|---|---|\n"
            + "| Simple | 1 | 2 | 90 | 85 | +5 |\n"
            + "| Intermediate | 2 | 1 | 49 | 69 | -21 |\n"
            + "| Advanced | 1 | 1 | 87 | 91 | -4 |\n\n", text);
        Assert.DoesNotContain("### Difficulty bands\n", text);

        // The per-question table names both bands.
        Assert.Contains("| Q | Topic | Assessed band | Authored | Score | Peer mean | Difference |", text);
        Assert.Contains("| Q2 | Prayer timeout | Intermediate | Simple | 72 | 70 | +2 | no | 0 | 3 | 11.0 s |\n", text);

        var standalone = StandaloneDocument(BenchmarkReportAudience.TechnicalReport);
        standalone.FactsJson = BenchmarkReportJson.Serialize(WithVersion9Facts(StandaloneSheet()));
        string alone = BenchmarkReportPackRenderer.Render(standalone, new BenchmarkReportRenderOptions());
        Assert.Contains("### Difficulty bands (assessed)\n\n"
            + "| Difficulty band | Questions | Authored questions | GPT-5.6 Luna |\n"
            + "|---|---|---|---|\n"
            + "| Simple | 1 | 2 | 90 |\n", alone);
        Assert.Contains("| Q | Topic | Assessed band | Authored | Score | Critical error |", alone);
    }

    [Fact]
    public void TheDifficultyBands_WithNoBandScore_LeaveTheScoreColumnsOut()
    {
        static BenchmarkReportFactSheet WithoutBandScores(BenchmarkReportFactSheet sheet)
        {
            foreach (var fact in sheet.Facts.Where(f => f.Key.StartsWith("band.", StringComparison.Ordinal) && !f.Key.EndsWith(".questions", StringComparison.Ordinal)))
            {
                fact.Display = BenchmarkReportFacts.NotAvailable;
                fact.Value = null;
                fact.Available = false;
                fact.UnavailableReason = BenchmarkBatteryReportFacts.BandReason;
            }
            return sheet;
        }

        var document = Document(BenchmarkReportAudience.TechnicalReport);
        document.FactsJson = BenchmarkReportJson.Serialize(WithoutBandScores(WithVersion9Facts(Sheet())));
        string text = BenchmarkReportPackRenderer.Render(document,
            new BenchmarkReportRenderOptions { Disclosure = BenchmarkReportDisclosure.Summary, PeerNaming = BenchmarkReportPeerNaming.Named });
        Assert.Contains("### Difficulty bands (assessed)\n\n"
            + "| Difficulty band | Questions | Authored questions |\n"
            + "|---|---|---|\n"
            + "| Simple | 1 | 2 |\n"
            + "| Intermediate | 2 | 1 |\n"
            + "| Advanced | 1 | 1 |\n\n", text);

        var standalone = StandaloneDocument(BenchmarkReportAudience.TechnicalReport);
        standalone.FactsJson = BenchmarkReportJson.Serialize(WithoutBandScores(WithVersion9Facts(StandaloneSheet())));
        string alone = BenchmarkReportPackRenderer.Render(standalone, new BenchmarkReportRenderOptions());
        Assert.Contains("### Difficulty bands (assessed)\n\n"
            + "| Difficulty band | Questions | Authored questions |\n"
            + "|---|---|---|\n"
            + "| Simple | 1 | 2 |\n", alone);
    }

    [Fact]
    public void KeyFigures_AndSpeedAndCost_StateTheMeanAnswerTime()
    {
        string executive = BenchmarkReportPackRenderer.Render(Version9Document(BenchmarkReportAudience.ExecutiveSummary),
            new BenchmarkReportRenderOptions { PeerNaming = BenchmarkReportPeerNaming.Named });
        Assert.Contains("- **Speed:** median answer time 12.3 s, mean 14.0 s, 2nd of 2.\n", executive);

        string researcher = BenchmarkReportPackRenderer.Render(Version9Document(BenchmarkReportAudience.TechnicalReport),
            new BenchmarkReportRenderOptions { PeerNaming = BenchmarkReportPeerNaming.Named });
        Assert.Contains("- **Speed:** median answer time 12.3 s, mean 14.0 s, 2nd of 2.\n", researcher);
        Assert.Contains("| Median answer time | 12.3 s |\n| Mean answer time | 14.0 s |\n| 90th-percentile answer time | 15.0 s |\n", researcher);
    }

    [Theory]
    [MemberData(nameof(Combinations))]
    public void ASheetWithoutTheFormatVersion9Facts_StillRenders_LeavingThemOut(
        BenchmarkReportAudience audience, BenchmarkReportDisclosure disclosure, BenchmarkReportPeerNaming naming, string file)
    {
        // The fixture's sheet carries none of them, as a document stored under format version 8 does.
        var document = Document(audience);
        document.ReportFormatVersion = 8;

        string text = BenchmarkReportPackRenderer.Render(document, new BenchmarkReportRenderOptions { Disclosure = disclosure, PeerNaming = naming });

        Assert.Contains("- **Speed:** median answer time 12.3 s, 2nd of 2.\n", text);
        Assert.DoesNotContain("Mean answer time", text);
        Assert.DoesNotContain("{{", text);
        if (audience != BenchmarkReportAudience.ExecutiveSummary)
        {
            Assert.Contains("| Q3 | Breaking a thrown gem | Intermediate | — | 25 |", text);
        }
        if (audience == BenchmarkReportAudience.TechnicalReport)
        {
            Assert.Contains("| Simple | 1 | — | 90 | 85 | +5 |\n", text);
        }
        Assert.NotEmpty(file);
    }

    [Theory]
    [InlineData(BenchmarkReportDisclosure.Detailed, "Questions and answers")]
    [InlineData(BenchmarkReportDisclosure.Full, "Question details")]
    public void TheResearcherReport_PutsItsQuestionDetails_AfterTheReproducibilityAppendix(BenchmarkReportDisclosure disclosure, string details)
    {
        string text = Render(BenchmarkReportAudience.TechnicalReport, disclosure, BenchmarkReportPeerNaming.Named);

        int Pos(string part) => text.IndexOf(part, StringComparison.Ordinal);
        AssertInOrder(
            Pos("## Per-question results\n"), Pos("### Questions more than 15 points below the peer mean"),
            Pos("## Tool-use behavior\n"), Pos("## Grader reliability\n"), Pos("## Threats to validity\n"),
            Pos("## Reproducibility appendix\n"), Pos("\n## " + details + "\n"), Pos("\n### Q1: Throwing gems at unicorns\n"),
            Pos("\n### Q4: Wand of wishing charges\n"), Pos("## Removed content\n"), Pos("## Evaluation terms\n"));
        Assert.DoesNotContain("#### Q1", text);

        // The table of contents lists the sections in the same order.
        var contents = Overseer.Services.Benchmarking.Pdf.BenchmarkPdfMarkdownComposer.Prepare(text, string.Empty)
            .Contents.Select(c => c.Text).ToList();
        int appendix = contents.IndexOf("Reproducibility appendix");
        Assert.True(appendix > contents.IndexOf("Per-question results"));
        Assert.Equal(new[] { "Reproducibility appendix", details, "Removed content", "Evaluation terms" }, contents.Skip(appendix));
    }

    [Fact]
    public void AtSummary_TheResearcherReportHasNoQuestionDetails_AndTheInternalBriefKeepsThemUnderPerQuestionResults()
    {
        string summary = Render(BenchmarkReportAudience.TechnicalReport, BenchmarkReportDisclosure.Summary, BenchmarkReportPeerNaming.Named);
        Assert.DoesNotContain("Question details", summary);
        Assert.DoesNotContain("Questions and answers", summary);

        string brief = Render(BenchmarkReportAudience.InternalBrief, BenchmarkReportDisclosure.Full, BenchmarkReportPeerNaming.Named);
        int Pos(string part) => brief.IndexOf(part, StringComparison.Ordinal);
        AssertInOrder(Pos("## 5. Per-question results\n"), Pos("\n### Question details\n"), Pos("\n#### Q1: Throwing gems at unicorns\n"), Pos("## 6. Fact sheet\n"));
    }

    [Fact]
    public void AnItemCitingAConvergentRow_IsBothGraders_OnlyOnTheRowsSharedQuestions()
    {
        var document = Document(BenchmarkReportAudience.TechnicalReport);
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
            new BenchmarkReportRenderOptions { Disclosure = BenchmarkReportDisclosure.Detailed, PeerNaming = BenchmarkReportPeerNaming.Named });

        // Q3 is shared: both graders.
        Assert.Contains("- Asserted a false outcome on Q3, where Grok 5 scored well. *(Both graders)*\n"
            + "  - *Evidence:* Both graders — critical error · Q3 (25 / 100)", text);
        // Only member A named Q4: one grader, of another provider than the model.
        Assert.Contains("- Overstated the wand's charges. *(One grader — different provider)*\n"
            + "  - *Evidence:* One grader — different provider — critical error · Q4 (87 / 100)\n", text);
        // Citing no question, the item is about the row's questions, which include the shared one.
        Assert.Contains("- Made destruction claims the graders flagged. *(Both graders)*\n", text);
        // The findings table keeps the row's own label.
        Assert.Contains("| R1 | weakness · critical error: States that a thrown gem always shatters. | Q3, Q4 | Both graders |\n", text);

        var executive = Document(BenchmarkReportAudience.ExecutiveSummary);
        executive.FactsJson = document.FactsJson;
        executive.WriterOutputJson = document.WriterOutputJson;
        string plain = BenchmarkReportPackRenderer.Render(executive, new BenchmarkReportRenderOptions());
        Assert.Contains("- Overstated the wand's charges. *(raised by one grader)*\n", plain);
        Assert.Contains("- Asserted a false outcome on Q3, where Model A scored well.\n", plain);
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
    [InlineData(BenchmarkReportAudience.TechnicalReport, BenchmarkReportDisclosure.Full, BenchmarkReportPeerNaming.Named, "comparison_subset_technical_full_named.md")]
    [InlineData(BenchmarkReportAudience.InternalBrief, BenchmarkReportDisclosure.Full, BenchmarkReportPeerNaming.Anonymized, "comparison_subset_internal_full_anonymized.md")]
    public void TheSubsetComparison_MatchesItsGoldenFile(
        BenchmarkReportAudience audience, BenchmarkReportDisclosure disclosure, BenchmarkReportPeerNaming naming, string file)
    {
        AssertMatchesGolden(file, RenderComparison(audience, disclosure, naming, subset: true));
    }

    [Fact]
    public void ComparisonTitles_NameTheComparison_AndAnAnonymizedCopyNeverPrintsItsName()
    {
        Assert.StartsWith("# Comparison #12 — Spring model sweep: Executive Summary\n",
            RenderComparison(BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportDisclosure.Full, BenchmarkReportPeerNaming.Named));
        Assert.StartsWith("# Comparison #12: Report for AI Researchers and Developers\n",
            RenderComparison(BenchmarkReportAudience.TechnicalReport, BenchmarkReportDisclosure.Summary, BenchmarkReportPeerNaming.Anonymized));
        Assert.StartsWith("# Comparison #12 — Orion Max vs Zeta Prime: Executive Summary\n",
            RenderComparison(BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportDisclosure.Full, BenchmarkReportPeerNaming.Named, subset: true));
        Assert.StartsWith("# Comparison #12 — 2 of 5 models: Internal Improvement Brief\n",
            RenderComparison(BenchmarkReportAudience.InternalBrief, BenchmarkReportDisclosure.Full, BenchmarkReportPeerNaming.Anonymized, subset: true));

        string anonymized = RenderComparison(BenchmarkReportAudience.TechnicalReport, BenchmarkReportDisclosure.Full, BenchmarkReportPeerNaming.Anonymized);
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

        string technical = RenderComparison(BenchmarkReportAudience.TechnicalReport, BenchmarkReportDisclosure.Full, BenchmarkReportPeerNaming.Named, subset: true);
        Assert.Contains("- **Coverage:** 2 of 5 models of Comparison #12; the other 3 are not part of this document.\n", technical);
    }

    [Fact]
    public void ANamedComparisonCopy_NamesTheModels_AndKeepsLettersInTheTablesOnly()
    {
        foreach (var audience in new[] { BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportAudience.TechnicalReport })
        {
            string named = RenderComparison(audience, BenchmarkReportDisclosure.Full, BenchmarkReportPeerNaming.Named);
            Assert.DoesNotContain("Model A", named, StringComparison.Ordinal);
            Assert.DoesNotContain("Models A", named, StringComparison.Ordinal);
            Assert.Contains("Orion Max", named, StringComparison.Ordinal);
        }

        string technical = RenderComparison(BenchmarkReportAudience.TechnicalReport, BenchmarkReportDisclosure.Full, BenchmarkReportPeerNaming.Named);
        Assert.Contains("| Letter | Model | Provider | Kind |", technical);
        Assert.Contains("| A | Orion Max | Northwind | run |", technical);
        Assert.Contains("joint 1", technical);

        foreach (var combination in ComparisonCombinations().Where(c => (BenchmarkReportPeerNaming)c[2] == BenchmarkReportPeerNaming.Anonymized))
        {
            string anonymized = RenderComparison((BenchmarkReportAudience)combination[0], (BenchmarkReportDisclosure)combination[1], BenchmarkReportPeerNaming.Anonymized);
            Assert.All(ComparisonModels, m => Assert.DoesNotContain(m.Label, anonymized, StringComparison.Ordinal));
            Assert.All(ComparisonModels, m => Assert.DoesNotContain(m.Provider, anonymized, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void AComparisonDocument_PrintsItsPairedTests_NamingTheFamilySize()
    {
        string technical = RenderComparison(BenchmarkReportAudience.TechnicalReport, BenchmarkReportDisclosure.Summary, BenchmarkReportPeerNaming.Anonymized);

        Assert.Contains("### Paired tests\n", technical);
        Assert.Contains("**Against Model A (the reference)**\n", technical);
        Assert.Contains("**Every pair**\n", technical);
        Assert.Contains("| Model A vs Model E | 6 | +", technical);
        Assert.DoesNotContain("not tested for significance", technical);
        Assert.Contains("### Per-question matrix\n", technical);
        Assert.Contains("| Q3 | ", technical);
        Assert.Contains(" CE |", technical);
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

        string technical = RenderWithCharts(ComparisonDocument(BenchmarkReportAudience.TechnicalReport), BenchmarkReportDisclosure.Full, BenchmarkReportPeerNaming.Named, all);
        AssertInOrder(
            technical.IndexOf("## Results\n", StringComparison.Ordinal),
            technical.IndexOf(MarkerOf(BenchmarkReportChartPlacement.QualityKey), StringComparison.Ordinal),
            technical.IndexOf("## Dimension profiles\n", StringComparison.Ordinal),
            technical.IndexOf(MarkerOf(BenchmarkReportChartPlacement.ProfileKey), StringComparison.Ordinal),
            technical.IndexOf("## Speed and cost frontier\n", StringComparison.Ordinal),
            technical.IndexOf(MarkerOf(BenchmarkReportChartPlacement.SpeedKey), StringComparison.Ordinal),
            technical.IndexOf(MarkerOf(BenchmarkReportChartPlacement.SpeedCostKey), StringComparison.Ordinal),
            technical.IndexOf("## Per-model analysis\n", StringComparison.Ordinal));

        string brief = RenderWithCharts(ComparisonDocument(BenchmarkReportAudience.InternalBrief), BenchmarkReportDisclosure.Full, BenchmarkReportPeerNaming.Named, all);
        AssertInOrder(
            brief.IndexOf("## Models compared\n", StringComparison.Ordinal),
            brief.IndexOf(MarkerOf(BenchmarkReportChartPlacement.QualityKey), StringComparison.Ordinal),
            brief.IndexOf("## 1. Shared gaps\n", StringComparison.Ordinal));

        Assert.Equal(BenchmarkReportChartAnchor.SpeedAndCostFrontier,
            BenchmarkReportChartPlacement.AnchorOf(BenchmarkReportAudience.TechnicalReport, BenchmarkReportChartPlacement.CostKey, BenchmarkReportScope.Comparison));
        Assert.Equal(BenchmarkReportChartAnchor.CostResults,
            BenchmarkReportChartPlacement.AnchorOf(BenchmarkReportAudience.TechnicalReport, BenchmarkReportChartPlacement.CostKey));
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

    /// <summary>The technical report with a topic for every question of the matrix, or for every question but Q2.</summary>
    private static BenchmarkReportDocument TechnicalWithTopics(bool everyQuestion)
    {
        var document = ComparisonDocument(BenchmarkReportAudience.TechnicalReport);
        var sheet = BenchmarkReportJson.Deserialize<BenchmarkReportFactSheet>(document.FactsJson);
        var writer = BenchmarkReportJson.Deserialize<BenchmarkReportWriterOutput>(document.WriterOutputJson);
        writer.QuestionTopics = sheet.Questions
            .Where(q => everyQuestion || q.Number != 2)
            .Select(q => new BenchmarkReportQuestionTopic { Question = q.Number, Topic = "A question about topic " + q.Number.ToString(CultureInfo.InvariantCulture) })
            .ToList();
        document.WriterOutputJson = BenchmarkReportJson.Serialize(writer);
        return document;
    }

    [Fact]
    public void TheMatrixLegend_NamesEachColumnInANamedCopy_AndPointsToTheModelsTableInAnAnonymizedOne()
    {
        var document = TechnicalWithTopics(everyQuestion: true);

        string named = RenderDocument(document, BenchmarkReportDisclosure.Summary, BenchmarkReportPeerNaming.Named);
        Assert.Contains("| Q | Topic | Band | A | B | C | D | E | Spread |\n", named);
        Assert.Contains("*Columns: A = Orion Max, B = Vega Pro, C = Lyra Mini, D = Nova Lite, E = Zeta Prime. "
            + "CE marks a critical error; Spread is the highest score minus the lowest. "
            + "— under a model marks a question it was not asked or not scored on.*\n", named);
        Assert.DoesNotContain("Each model's column is headed by its letter", named);

        string anonymized = RenderDocument(document, BenchmarkReportDisclosure.Summary, BenchmarkReportPeerNaming.Anonymized);
        Assert.Contains("| Q | Topic | Band | A | B | C | D | E | Spread |\n", anonymized);
        Assert.Contains("*Each model's column is headed by its letter in the models table. "
            + "CE marks a critical error; Spread is the highest score minus the lowest. "
            + "— under a model marks a question it was not asked or not scored on.*\n", anonymized);
        Assert.DoesNotContain("Columns: A =", anonymized);
    }

    [Fact]
    public void TheMatrixLegend_ExplainsAMissingTopic_OnlyWhenATopicIsMissing()
    {
        const string topicSentence = "— under Topic marks a question not given in detail.";

        string every = RenderDocument(TechnicalWithTopics(everyQuestion: true), BenchmarkReportDisclosure.Summary, BenchmarkReportPeerNaming.Named);
        Assert.DoesNotContain(topicSentence, every);

        string missing = RenderDocument(TechnicalWithTopics(everyQuestion: false), BenchmarkReportDisclosure.Summary, BenchmarkReportPeerNaming.Named);
        Assert.Contains("| Q2 | — | ", missing);
        Assert.Contains("— under a model marks a question it was not asked or not scored on. " + topicSentence + "*\n", missing);
    }

    [Fact]
    public void ATwoModelComparison_SaysWhetherTheTwoIntervalsOverlap_AndWhetherTheComparisonEstablishesTheOrder()
    {
        // Orion Max (82 to 88) and Vega Pro (77 to 83) overlap; Orion Max and Zeta Prime (50 to 60) do not.
        string overlapping = RenderDocument(ComparisonDocumentOver(BenchmarkReportAudience.ExecutiveSummary, new[] { "run:31", "run:32" }),
            BenchmarkReportDisclosure.Full, BenchmarkReportPeerNaming.Named);
        Assert.Contains("The two models' 95 % intervals overlap; the intervals alone do not establish which scored higher.\n", overlapping);
        Assert.DoesNotContain("Of the 1 pair", overlapping);

        var document = ComparisonDocument(BenchmarkReportAudience.ExecutiveSummary, subset: true);
        string apart = RenderDocument(document, BenchmarkReportDisclosure.Full, BenchmarkReportPeerNaming.Named);
        Assert.Contains("The two models' 95 % intervals do not overlap.\n", apart);
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
        Assert.DoesNotContain("The two models' 95 % intervals", five);
    }

    [Fact]
    public void ABatteryComparison_CountsTheQuestionsTheWriterWasGivenInFull_PerSuite()
    {
        var document = ComparisonDocument(BenchmarkReportAudience.TechnicalReport);
        var sheet = BenchmarkReportJson.Deserialize<BenchmarkReportFactSheet>(document.FactsJson);
        foreach (var q in sheet.Questions)
        {
            q.Suite = q.Number <= 3 ? 1 : 2;
            q.Reference = "S" + q.Suite.Value.ToString(CultureInfo.InvariantCulture) + "-Q" + q.Number.ToString(CultureInfo.InvariantCulture);
        }
        document.FactsJson = BenchmarkReportJson.Serialize(sheet);

        var content = BenchmarkReportJson.Deserialize<BenchmarkReportContentSnapshot>(document.ContentJson);
        content.Questions = new[] { 1, 3, 5, 3 }
            .Select(n => new BenchmarkReportContentQuestion { Number = n, QuestionText = "Question " + n.ToString(CultureInfo.InvariantCulture) + "?" })
            .ToList();
        document.ContentJson = BenchmarkReportJson.Serialize(content);

        string text = RenderDocument(document, BenchmarkReportDisclosure.Summary, BenchmarkReportPeerNaming.Named);
        Assert.Contains("- Detail: the writer was given the full text of 3 questions (2 of suite 1, 1 of suite 2), and the other questions as matrix rows.\n", text);

        content.Questions = new List<BenchmarkReportContentQuestion> { new() { Number = 4, QuestionText = "Question 4?" } };
        document.ContentJson = BenchmarkReportJson.Serialize(content);
        string one = RenderDocument(document, BenchmarkReportDisclosure.Summary, BenchmarkReportPeerNaming.Named);
        Assert.Contains("- Detail: the writer was given the full text of 1 question (0 of suite 1, 1 of suite 2), and the other questions as matrix rows.\n", one);

        content.Questions = new List<BenchmarkReportContentQuestion>();
        document.ContentJson = BenchmarkReportJson.Serialize(content);
        string none = RenderDocument(document, BenchmarkReportDisclosure.Summary, BenchmarkReportPeerNaming.Named);
        Assert.Contains("- Detail: the writer was given the full text of at most a few questions per suite, and the other questions as matrix rows.\n", none);

        string single = RenderComparison(BenchmarkReportAudience.TechnicalReport, BenchmarkReportDisclosure.Summary, BenchmarkReportPeerNaming.Named);
        Assert.DoesNotContain("- Detail: ", single);
    }

    [Fact]
    public void AComparisonDocument_IsCurrentAtFormatVersion12()
    {
        string text = RenderComparison(BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportDisclosure.Full, BenchmarkReportPeerNaming.Named);

        Assert.Equal(12, BenchmarkReportPackRenderer.ComparisonReportFormatVersion);
        Assert.Contains(" · format version 12 · ", text);
        Assert.Contains(" · models named*", text);
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
        { BenchmarkReportAudience.TechnicalReport, BenchmarkReportDisclosure.Detailed, BenchmarkReportPeerNaming.Anonymized, "chatconsistency_technical.md" },
        { BenchmarkReportAudience.InternalBrief, BenchmarkReportDisclosure.Full, BenchmarkReportPeerNaming.Anonymized, "chatconsistency_internal.md" },
        { BenchmarkReportAudience.ProviderIssueReport, BenchmarkReportDisclosure.Detailed, BenchmarkReportPeerNaming.Anonymized, "chatconsistency_provider.md" },
        { BenchmarkReportAudience.TechnicalReport, BenchmarkReportDisclosure.Detailed, BenchmarkReportPeerNaming.Named, "chatconsistency_technical_named.md" },
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
    public void AChatConsistencyDocument_IsCurrentAtFormatVersion1_AndTheOtherScopesKeepTheirs()
    {
        Assert.Equal(1, BenchmarkReportPackRenderer.ChatConsistencyReportFormatVersion);
        Assert.Equal(1, BenchmarkReportPackRenderer.CurrentFormatVersion(BenchmarkReportScope.ChatConsistency));
        Assert.Equal(11, BenchmarkReportPackRenderer.CurrentFormatVersion(BenchmarkReportScope.Model));
        Assert.Equal(12, BenchmarkReportPackRenderer.CurrentFormatVersion(BenchmarkReportScope.Comparison));
        Assert.Equal(11, BenchmarkReportPackRenderer.ReportFormatVersion);
        Assert.Equal(12, BenchmarkReportPackRenderer.ComparisonReportFormatVersion);

        string text = RenderChatConsistency(BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportDisclosure.Full, BenchmarkReportPeerNaming.Named);
        Assert.Contains(" · format version 1 · ", text);
        Assert.Contains(" · controls named*", text);
        Assert.Contains("- **Report format version:** 1\n", text);
    }

    [Theory]
    [InlineData(BenchmarkReportAudience.ExecutiveSummary)]
    [InlineData(BenchmarkReportAudience.TechnicalReport)]
    [InlineData(BenchmarkReportAudience.InternalBrief)]
    [InlineData(BenchmarkReportAudience.ProviderIssueReport)]
    public void AChatConsistencyDocument_OpensWithTheOverallVerdict_ThenTheSlotsUnderTheirTitles_AndEndsWithHowToReadAndReproducibility(
        BenchmarkReportAudience audience)
    {
        var disclosure = audience == BenchmarkReportAudience.InternalBrief ? BenchmarkReportDisclosure.Full : BenchmarkReportDisclosure.Summary;
        string text = RenderChatConsistency(audience, disclosure, BenchmarkReportPeerNaming.Anonymized);
        int Pos(string part) => text.IndexOf(part, StringComparison.Ordinal);

        Assert.StartsWith("# Overseer Chat Consistency Report: Test Model\n", text);
        var slots = BenchmarkReportSlots.For(audience, BenchmarkReportScope.ChatConsistency).RequiredSlots;
        var order = new List<int> { Pos("## Overall verdict\n"), Pos("## The result in one sentence\n") };
        order.AddRange(slots.Select(slot => Pos("## " + BenchmarkReportSlots.ChatConsistencySlotTitles[slot] + "\n")));
        order.Add(Pos("## How to read this\n"));
        AssertInOrder(order.ToArray());

        Assert.Contains("| Quality (P1) | ", text);
        Assert.Contains("| Endpoint | Estimate | 95 % interval | Verdict | Grade | Minimum detectable effect |\n", text);
        Assert.Contains("| Date | Kind | Change | From → to | Series | Run |\n", text);
        Assert.Contains("- **Input SHA-256:** `" + string.Concat(Enumerable.Repeat("0123456789abcdef", 4)) + "`\n", text);
        Assert.Contains("- **Baseline runs:** #10, #11\n", text);
        Assert.Contains("- **Comparison runs:** #20, #21\n", text);
        Assert.Contains("- **Control runs:** #30, #31\n", text);
        Assert.Contains("weekdays 04–12 UTC only, the hours both periods share", text);
        Assert.Contains("It never infers anyone's intent", text);
        Assert.Equal(audience != BenchmarkReportAudience.InternalBrief, text.Contains("## Evaluation terms\n", StringComparison.Ordinal));
        Assert.DoesNotContain("*Not written.*", text);
        Assert.DoesNotContain("{{", text);
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
            Assert.Contains("| Model A | ", text);
            Assert.Contains("- **Control models:** 1 control model (A), identity withheld\n", text);
            Assert.Contains(" · controls anonymized*", text);
            Assert.Contains("*Confidential. Prepared for the model's provider.*\n", text);
        }
    }

    [Fact]
    public void ANamedChatConsistencyCopy_NamesTheControlModel_AndAnAnonymizedOneLettersIt()
    {
        string named = RenderChatConsistency(BenchmarkReportAudience.TechnicalReport, BenchmarkReportDisclosure.Summary, BenchmarkReportPeerNaming.Named);
        string anonymized = RenderChatConsistency(BenchmarkReportAudience.TechnicalReport, BenchmarkReportDisclosure.Summary, BenchmarkReportPeerNaming.Anonymized);

        Assert.Contains("| " + ChatConsistencyReportTestData.ControlName + " | " + ChatConsistencyReportTestData.ControlProvider + " | ", named);
        Assert.Contains("- **Control models:** " + ChatConsistencyReportTestData.ControlName + "\n", named);
        Assert.Contains("- **Analysis:** #7 — Weekly check\n", named);

        Assert.DoesNotContain(ChatConsistencyReportTestData.ControlName, anonymized, StringComparison.Ordinal);
        Assert.DoesNotContain(ChatConsistencyReportTestData.ControlProvider, anonymized, StringComparison.Ordinal);
        Assert.Contains("| Model A | ", anonymized);
        Assert.Contains("- **Analysis:** #7\n", anonymized);
    }

    [Fact]
    public void AChatConsistencyDocument_PrintsTheSameTextAtEveryDisclosure_UnderItsOwnStamp()
    {
        string summary = RenderChatConsistency(BenchmarkReportAudience.TechnicalReport, BenchmarkReportDisclosure.Summary, BenchmarkReportPeerNaming.Anonymized);
        string full = RenderChatConsistency(BenchmarkReportAudience.TechnicalReport, BenchmarkReportDisclosure.Full, BenchmarkReportPeerNaming.Anonymized);

        Assert.Contains("*Confidential. Unpublished chat consistency results. Review before sharing.*\n", summary);
        Assert.Contains("*INTERNAL — unpublished chat consistency results. Do not share outside the Overseer team.*\n", full);
        string sameBody = summary
            .Replace(BenchmarkReportPackRenderer.ChatConsistencyStamp(BenchmarkReportAudience.TechnicalReport, BenchmarkReportDisclosure.Summary),
                BenchmarkReportPackRenderer.ChatConsistencyStamp(BenchmarkReportAudience.TechnicalReport, BenchmarkReportDisclosure.Full), StringComparison.Ordinal)
            .Replace(" · disclosure Summary · ", " · disclosure Full · ", StringComparison.Ordinal);
        AssertSameText(sameBody, full, "Report for AI Researchers and Developers at Full");
    }

    [Fact]
    public void ThePlacement_PlacesTheChatConsistencyFiguresPerAudience_AndKeepsThemOutOfTheOtherScopes()
    {
        const BenchmarkReportScope chat = BenchmarkReportScope.ChatConsistency;
        var results = BenchmarkReportChartAnchor.ChatConsistencyResults;
        var events = BenchmarkReportChartAnchor.ChatConsistencyEvents;

        Assert.Equal(new[] { "cc1-quality", "cc2-speed", "cc3-work", "cc4-timeline" }, BenchmarkReportChartPlacement.ChatConsistencyFigureKeys);
        Assert.All(BenchmarkReportChartPlacement.ChatConsistencyFigureKeys, key => Assert.True(BenchmarkReportChartPlacement.IsKnown(key)));
        Assert.Equal(11, BenchmarkReportChartPlacement.AllFigureKeys.Count);

        Assert.Equal(new[] { "cc1-quality", "cc2-speed" }, BenchmarkReportChartPlacement.KeysAt(BenchmarkReportAudience.ExecutiveSummary, results, chat));
        Assert.Empty(BenchmarkReportChartPlacement.KeysAt(BenchmarkReportAudience.ExecutiveSummary, events, chat));
        Assert.Equal(new[] { "cc1-quality", "cc2-speed", "cc3-work" }, BenchmarkReportChartPlacement.KeysAt(BenchmarkReportAudience.TechnicalReport, results, chat));
        Assert.Equal(new[] { "cc4-timeline" }, BenchmarkReportChartPlacement.KeysAt(BenchmarkReportAudience.TechnicalReport, events, chat));
        Assert.Equal(new[] { "cc1-quality", "cc3-work" }, BenchmarkReportChartPlacement.KeysAt(BenchmarkReportAudience.InternalBrief, results, chat));
        Assert.Equal(new[] { "cc2-speed" }, BenchmarkReportChartPlacement.KeysAt(BenchmarkReportAudience.ProviderIssueReport, results, chat));
        Assert.Equal(new[] { "cc4-timeline" }, BenchmarkReportChartPlacement.KeysAt(BenchmarkReportAudience.ProviderIssueReport, events, chat));

        Assert.Null(BenchmarkReportChartPlacement.AnchorOf(BenchmarkReportAudience.ExecutiveSummary, "cc3-work", chat));
        Assert.Null(BenchmarkReportChartPlacement.AnchorOf(BenchmarkReportAudience.ExecutiveSummary, "cc1-quality"));
        Assert.Null(BenchmarkReportChartPlacement.AnchorOf(BenchmarkReportAudience.TechnicalReport, "cc1-quality", BenchmarkReportScope.Comparison));
        Assert.Null(BenchmarkReportChartPlacement.AnchorOf(BenchmarkReportAudience.TechnicalReport, "p1a-quality", chat));
        Assert.Equal(results, BenchmarkReportChartPlacement.AnchorOf(BenchmarkReportAudience.TechnicalReport, "cc1-quality", chat));
        Assert.Empty(BenchmarkReportChartPlacement.KeysAt(BenchmarkReportAudience.ProviderIssueReport, BenchmarkReportChartAnchor.HowItCompares));
        Assert.True(BenchmarkReportChartPlacement.TryParseMarker("[[figure:cc4-timeline]]", out string parsed));
        Assert.Equal("cc4-timeline", parsed);
    }

    [Theory]
    [InlineData(BenchmarkReportAudience.ExecutiveSummary, new[] { "cc1-quality", "cc2-speed" })]
    [InlineData(BenchmarkReportAudience.TechnicalReport, new[] { "cc1-quality", "cc2-speed", "cc3-work", "cc4-timeline" })]
    [InlineData(BenchmarkReportAudience.InternalBrief, new[] { "cc1-quality", "cc3-work" })]
    [InlineData(BenchmarkReportAudience.ProviderIssueReport, new[] { "cc2-speed", "cc4-timeline" })]
    public void AChatConsistencyDocument_DrawsItsFiguresAfterTheirTables_AndNoComparisonFigure(BenchmarkReportAudience audience, string[] placed)
    {
        var disclosure = audience == BenchmarkReportAudience.InternalBrief ? BenchmarkReportDisclosure.Full : BenchmarkReportDisclosure.Summary;
        var keys = BenchmarkReportChartPlacement.AllFigureKeys.Reverse().ToArray();
        string text = RenderWithCharts(ChatConsistencyDocument(audience), disclosure, BenchmarkReportPeerNaming.Anonymized, Charts(keys));

        foreach (string key in BenchmarkReportChartPlacement.AllFigureKeys)
        {
            Assert.Equal(placed.Contains(key) ? 1 : 0, AllIndexesOf(text, MarkerOf(key)).Count);
        }

        int Pos(string part) => text.IndexOf(part, StringComparison.Ordinal);
        int verdictTable = Pos("| Endpoint | Estimate | 95 % interval | Verdict | Grade | Minimum detectable effect |");
        int eventsTable = Pos("| Date | Kind | Change | From → to | Series | Run |");
        foreach (string key in placed)
        {
            int at = Pos("\n\n" + MarkerOf(key) + "\n\n");
            Assert.True(at > (key == "cc4-timeline" ? eventsTable : verdictTable), key + " stands after its table.");
        }
        Assert.DoesNotContain("[[figure:", RenderChatConsistency(audience, disclosure, BenchmarkReportPeerNaming.Anonymized));
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
        Assert.Contains("- **Control models:** none\n", text);
    }
}
