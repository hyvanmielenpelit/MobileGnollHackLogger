namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
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

        Assert.Contains("### Questions and answers\n", text);
        Assert.DoesNotContain("### Question details", text);
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

        Assert.Contains("### Question details\n", text);
        Assert.DoesNotContain("### Questions and answers", text);
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

        Assert.Contains("GPT-5.6 Luna scored 80 / 100 on 4 questions, ranking 2nd of 3 against Grok 5 and Mistral Large 4.", named);
        Assert.Contains("GPT-5.6 Luna scored 80 / 100 on 4 questions, ranking 2nd of 3 against Model A and Model B.", anonymized);
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
        Assert.Contains("  - *Evidence:* Intermediate band score: 49 · Q2 (72 / 100), Q3 (25 / 100) · the claim verifier refuted an answer sentence on Q3\n", text);
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
            + "  - *Evidence:* Both graders — completeness · Q2 (72 / 100), Q3 (25 / 100) · the claim verifier refuted an answer sentence on Q3\n", text);
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
            Assert.Contains(", where Grok 5 scored well. *(both graders agreed)*\n", text);
            Assert.Contains("*(computed from the figures)*\n", text);
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
        Assert.Contains("- **Peers:** Model A = Grok 5", withFrontMatter);
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
            + "peer names, significance claims and spelling; the checks do not verify the prose's interpretations.*\n", withFooter);
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

        Assert.Contains("- **Intelligence:** 80 / 100 (interval 77–83), 2nd of 3; its 95 % interval overlaps those of Models A and B.\n", text);
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

        Assert.Contains("| Q | Topic | Band | Score | Peer mean | Difference | Critical error | Refuted answer sentences | Tool calls | Model time |", text);
        Assert.Contains("| Q3 | Breaking a thrown gem | Intermediate | 25 | 68 | -43 | yes | 1 | 5 | 15.2 s |", text);
        Assert.DoesNotContain("| Refuted claims |", text);
    }

    [Fact]
    public void AtFull_EachClaimRulingNamesItsRole()
    {
        string text = Render(BenchmarkReportAudience.TechnicalReport, BenchmarkReportDisclosure.Full, BenchmarkReportPeerNaming.Named);

        Assert.Contains("- **Grader's statement — refuted (the answer was right):** \"Worthless glass angers the unicorn.\" — "
            + "Glass is caught and returned without anger (dothrow.c).\n", text);
        Assert.Contains("- **Answer sentence — supported:** \"The timeout is typically near 350.\"", text);
        Assert.Contains("- **Answer sentence accused by a grader — refuted (the grader was right):** \"A thrown gem always shatters on impact.\"", text);
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
        Assert.Contains("- **Third-party model content:** Outputs generated by **GPT-5.6 Luna** (OpenAI), graded by models from Google and Anthropic, "
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

        Assert.Contains("graded by models from Google and a withheld provider,", anonymized);
        Assert.DoesNotContain("xAI", anonymized);
        Assert.Contains("graded by models from Google and xAI,", named);
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
                Assert.DoesNotContain("Judge-dependent", text);
                Assert.DoesNotContain("{{", text);
                Assert.Contains("## Evaluation terms", text);
            }
        }

        if (audience == BenchmarkReportAudience.TechnicalReport)
        {
            string technical = BenchmarkReportPackRenderer.Render(StandaloneDocument(audience), new BenchmarkReportRenderOptions());
            Assert.Contains("## Results\n", technical);
            Assert.Contains("| Q | Topic | Band | Score | Critical error | Refuted answer sentences | Tool calls | Model time |", technical);
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
        Assert.Contains("| R2 | strength · accuracy | Q1 | One grader — different provider | 1 of 1 run |\n", text);
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
        Assert.Contains("## How reliable this result is\n\nThe result rests on 4 questions; its 95 % interval overlaps those of Models A and B.\n\n"
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
}
