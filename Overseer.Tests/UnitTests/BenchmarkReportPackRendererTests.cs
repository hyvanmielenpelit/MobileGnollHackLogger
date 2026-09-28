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
    public void AllowedDisclosures_AreEveryLevel_ExceptForTheInternalBrief()
    {
        var all = new[] { BenchmarkReportDisclosure.Summary, BenchmarkReportDisclosure.Detailed, BenchmarkReportDisclosure.Full };

        Assert.Equal(all, BenchmarkReportPackRenderer.AllowedDisclosures(BenchmarkReportAudience.ExecutiveSummary));
        Assert.Equal(all, BenchmarkReportPackRenderer.AllowedDisclosures(BenchmarkReportAudience.TechnicalReport));
        Assert.Equal(new[] { BenchmarkReportDisclosure.Full }, BenchmarkReportPackRenderer.AllowedDisclosures(BenchmarkReportAudience.InternalBrief));
        Assert.Equal(14, Combinations().Count());
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
    public void AtDetailed_OnlyTheDiscussedQuestionsAreQuoted_WithAnswerExcerpts_AndNoRubricOrEvidence()
    {
        string text = Render(BenchmarkReportAudience.TechnicalReport, BenchmarkReportDisclosure.Detailed, BenchmarkReportPeerNaming.Named);

        Assert.Contains("> **Question:** " + Q3Text, text);
        Assert.Contains(Q3Excerpt, text);
        // Q1 is quoted only because a strength cites it, and only as its evidence.
        Assert.Contains("  - *Q1 as asked:* " + Q1Text, text);
        Assert.DoesNotContain("> **Question:** " + Q1Text, text);
        Assert.DoesNotContain(Q1Rubric, text);
        Assert.DoesNotContain(Q1Evidence, text);
        Assert.DoesNotContain(Q3Ruling, text);
        Assert.DoesNotContain("Slightly below the peers.", text);
        Assert.Contains("Contains benchmark questions — do not publish.", text);
    }

    [Fact]
    public void AtFull_EveryQuestionRubricGraderEvidenceAndRulingIsPrinted()
    {
        string text = Render(BenchmarkReportAudience.TechnicalReport, BenchmarkReportDisclosure.Full, BenchmarkReportPeerNaming.Named);

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
        foreach (var disclosure in BenchmarkReportPackRenderer.AllowedDisclosures(BenchmarkReportAudience.ExecutiveSummary))
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

        Assert.Contains("GPT-5.6 Luna scored 80 ± 3 / 100 on 4 questions, ranking 2nd of 3 against Grok 5 and Mistral Large 4.", named);
        Assert.Contains("GPT-5.6 Luna scored 80 ± 3 / 100 on 4 questions, ranking 2nd of 3 against Model A and Model B.", anonymized);
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
        Assert.Contains("*Report 101 ·", text);
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
            await db.SaveChangesAsync();
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
            var stored = await db.BenchmarkReportDocuments.AsNoTracking().SingleAsync(d => d.Id == 100 + (int)audience);
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
    public void EveryStrengthWeaknessAndRecommendation_PrintsItsEvidenceBeneathIt()
    {
        string text = Render(BenchmarkReportAudience.TechnicalReport, BenchmarkReportDisclosure.Summary, BenchmarkReportPeerNaming.Named);

        Assert.Contains("- Answers simple questions precisely and briefly. *(One grader — different family)*\n"
            + "  - *Evidence:* R2: One grader — different family — accuracy (Q1)\n", text);
        Assert.Contains("  - *Evidence:* R1: Both graders — critical error (Q3)\n"
            + "  - *Evidence:* Q3 — Breaking a thrown gem: score 25 / 100; peer mean 68 (-43); Panel member A 25, Panel member B 25; "
            + "a grader flagged a critical error; the claim verifier refuted one claim\n", text);
        Assert.Contains("  - *Evidence:* band.intermediate.score: 49\n", text);
    }

    [Fact]
    public void TheExecutiveSummary_PrintsEvidenceWithoutRowIds_AndNeverTheQuestionAsAsked()
    {
        foreach (var disclosure in BenchmarkReportPackRenderer.AllowedDisclosures(BenchmarkReportAudience.ExecutiveSummary))
        {
            string text = Render(BenchmarkReportAudience.ExecutiveSummary, disclosure, BenchmarkReportPeerNaming.Named);

            Assert.Contains("  - *Evidence:* Both graders — critical error (Q3)\n", text);
            Assert.DoesNotContain("as asked:*", text);
        }
    }

    [Fact]
    public void AtFull_TheResearcherReportAddsEachMembersAccuracyEvidence_ForTheCitedQuestions()
    {
        string full = Render(BenchmarkReportAudience.TechnicalReport, BenchmarkReportDisclosure.Full, BenchmarkReportPeerNaming.Named);
        string detailed = Render(BenchmarkReportAudience.TechnicalReport, BenchmarkReportDisclosure.Detailed, BenchmarkReportPeerNaming.Named);

        Assert.Contains("  - *Panel member A on Q3:* Accuracy: The gem does not always shatter.\n", full);
        Assert.DoesNotContain("*Panel member A on Q3:*", detailed);
        Assert.Contains("  - *Q3 as asked:* " + Q3Text + "\n", detailed);
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
            Assert.Contains("| Q | Topic | Band | Score | Critical error | Refuted claims | Tool calls | Model time |", technical);
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
}
