namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services.Benchmarking;
using Overseer.Services.Benchmarking.RootCause;
using Xunit;

/// <summary>
/// The Root Cause Analysis term registry, and how the document uses it: the Key terms section lists the terms the
/// Summary and the Conclusions use and the glossary appendix every term the document uses, each term is glossed once, a
/// writer's repeated gloss included, a null hypothesis' grade reads in plain words, and the fact sheet section
/// exists only in the Markdown copy.
/// </summary>
public class RootCauseGlossaryTests
{
    private static readonly BenchmarkReportRenderOptions FullNamed = new() { Disclosure = BenchmarkReportDisclosure.Full, PeerNaming = BenchmarkReportPeerNaming.Named };

    public static TheoryData<string> DocumentNames => new() { "comparison", "pack", "runWithControls", "runNoControls", "battery" };

    private static BenchmarkReportDocument Document(string name) => name switch
    {
        "comparison" => RootCauseReportFixture.ComparisonDocument(),
        "pack" => RootCauseReportFixture.ModelDocument(RootCauseReportFixture.ModelPackSheet(), BenchmarkReportDocumentOrigin.ReportPack),
        "runWithControls" => RootCauseReportFixture.ModelDocument(RootCauseReportFixture.RunSheet(controls: true), BenchmarkReportDocumentOrigin.RunCompletion),
        "runNoControls" => RootCauseReportFixture.ModelDocument(RootCauseReportFixture.RunSheet(controls: false), BenchmarkReportDocumentOrigin.RunCompletion),
        "battery" => RootCauseReportFixture.BatteryDocument(),
        _ => throw new ArgumentOutOfRangeException(nameof(name))
    };

    // ---------------------------------------------------------------------------------------------
    // The registry
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void EveryKey_AndEveryName_IsUnique()
    {
        var terms = RootCauseGlossary.Terms;

        Assert.Equal(terms.Count, terms.Select(t => t.Key).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(terms.Count, terms.Select(t => t.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(terms, t =>
        {
            Assert.False(string.IsNullOrWhiteSpace(t.Name), t.Key);
            Assert.False(string.IsNullOrWhiteSpace(t.Definition), t.Key);
            Assert.False(string.IsNullOrWhiteSpace(t.Gloss), t.Key);
            Assert.Same(t, RootCauseGlossary.Get(t.Key));
        });
    }

    [Fact]
    public void EveryGloss_HoldsNoDigit_AndAtMostTwelveWords()
    {
        Assert.All(RootCauseGlossary.Terms, t =>
        {
            Assert.False(t.Gloss.Any(char.IsDigit), t.Key + ": " + t.Gloss);
            int words = t.Gloss.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            Assert.True(words <= 12, t.Key + " has " + words + " words: " + t.Gloss);
        });
    }

    [Fact]
    public void EveryGloss_HoldsNoCausalOrCertaintyWord_SoTheWriterMayCopyIt()
    {
        Assert.All(RootCauseGlossary.Terms, t =>
        {
            Assert.False(RootCauseClaimSupport.CausalRegex.IsMatch(t.Gloss), t.Key + ": " + t.Gloss);
            Assert.False(RootCauseClaimSupport.CertaintyRegex.IsMatch(t.Gloss), t.Key + ": " + t.Gloss);
        });
    }

    [Fact]
    public void TheTiedRankTokensAndReferenceModelTerms_SayWhatTheDocumentPrints()
    {
        var tied = RootCauseGlossary.Get(RootCauseGlossary.TiedRank);
        Assert.Equal("When a model's 95% interval overlaps others', the data cannot fix its place, so its rank is the span of places the interval "
            + "allows, such as 5–9 (tied). The span starts one place below the models whose intervals lie wholly above its own and ends one place "
            + "above those whose intervals lie wholly below it.", tied.Definition);
        Assert.Equal("the span of places the model's interval allows", tied.Gloss);

        var tokens = RootCauseGlossary.Get(RootCauseGlossary.Tokens);
        Assert.Equal("The pieces of text, roughly three quarters of a word each, that a model reads and writes; providers price by them. Uncached "
            + "input is text read at the full price. Cache reads are input stored from an earlier call and charged less; cache writes store it. "
            + "Visible output is the answer, and reasoning is the model's hidden thinking, billed as output; a provider that does not report it "
            + "separately counts it inside output.", tokens.Definition);
        Assert.Equal("the word pieces a model reads and writes, which set its price", tokens.Gloss);

        var reference = RootCauseGlossary.Get(RootCauseGlossary.ReferenceModel);
        Assert.Equal("The model each other model is paired-tested against: the one chosen for the comparison, or else the model with the highest "
            + "Intelligence Index. It is also called the baseline model.", reference.Definition);
        Assert.Equal("the model every other model is tested against", reference.Gloss);
        Assert.True(RootCauseGlossary.Contains(RootCauseGlossary.BaselineModel));
    }

    [Fact]
    public void AnUnknownKey_Throws()
    {
        Assert.False(RootCauseGlossary.Contains("noSuchTerm"));
        Assert.Throws<ArgumentException>(() => RootCauseGlossary.Get("noSuchTerm"));
    }

    // ---------------------------------------------------------------------------------------------
    // The document
    // ---------------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(DocumentNames))]
    public void EveryTermTheRendererMarksAsUsed_IsInTheRegistry(string name)
    {
        BenchmarkReportPackRenderer.Render(Document(name), FullNamed, out var used);

        Assert.NotEmpty(used);
        Assert.All(used, key => Assert.True(RootCauseGlossary.Contains(key), key));
        Assert.Equal(used.Count, used.Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [MemberData(nameof(DocumentNames))]
    public void TheKeyTermsSection_ListsTheSummarysAndConclusionsTerms_AndTheGlossary_EveryUsedTerm(string name)
    {
        string text = BenchmarkReportPackRenderer.Render(Document(name), FullNamed, out var used);
        var expected = RootCauseGlossary.Alphabetical(used).ToList();

        int summary = text.IndexOf("\n## Summary\n", StringComparison.Ordinal);
        int keyTerms = text.IndexOf("\n## Key terms\n", StringComparison.Ordinal);
        int why = text.IndexOf("\n## Why each model scored as it did\n", StringComparison.Ordinal);
        Assert.True(summary >= 0 && keyTerms > summary && why > keyTerms, "Key terms must follow the Summary and precede the profiles");

        string Row(RootCauseTerm t) => "| " + t.Name + " | " + char.ToUpperInvariant(t.Gloss[0]) + t.Gloss[1..] + ". |";
        var rows = Section(text, "## Key terms")
            .Split('\n')
            .Where(l => l.StartsWith("| ", StringComparison.Ordinal) && !l.StartsWith("| Term |", StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(rows);
        Assert.Equal(expected.Select(Row).Where(rows.Contains), rows);
        Assert.Contains(Row(RootCauseGlossary.Get(RootCauseGlossary.GradeWords)), rows);

        // A term only the detail after the Summary uses is defined in the glossary, not listed among the key terms.
        foreach (string detailOnly in new[] { RootCauseGlossary.Locus, RootCauseGlossary.DecompositionShare, RootCauseGlossary.BootstrapInterval, RootCauseGlossary.ToolRound })
        {
            if (used.Contains(detailOnly)) Assert.DoesNotContain(Row(RootCauseGlossary.Get(detailOnly)), rows);
        }

        // The glossary is Appendix H, or G when the document has no Known facts appendix; the Key terms lead names it.
        string letter = text.Contains("\n## Appendix E: Known facts\n", StringComparison.Ordinal) ? "H" : "G";
        Assert.Contains("and Appendix " + letter + " defines every term this document uses.", Section(text, "## Key terms"));
        var entries = Section(text, "## Appendix " + letter + ": Glossary")
            .Split('\n')
            .Where(l => l.StartsWith("- **", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(expected.Select(t => "- **" + t.Name + ":** " + t.Definition), entries);
    }

    [Fact]
    public void AWritersRepeatedGloss_IsPrintedOnce_AndTheTemplateDoesNotGlossTheTermAgain()
    {
        var document = Document("comparison");
        var writer = BenchmarkReportJson.Deserialize<BenchmarkReportWriterOutput>(document.WriterOutputJson);
        string gloss = " (" + RootCauseGlossary.Get(RootCauseGlossary.Interval95).Gloss + ")";
        writer.Headline = "Each 95% interval" + gloss + " is wide, and the next 95% interval" + gloss + " is wide too.";
        document.WriterOutputJson = BenchmarkReportJson.Serialize(writer);

        string text = BenchmarkReportPackRenderer.Render(document, FullNamed, out var used);
        string body = WithoutSection(text, "## Key terms");

        Assert.Contains("> **Key finding.** Each 95% interval" + gloss + " is wide, and the next 95% interval is wide too.\n", body);
        Assert.Equal(1, CountOf(body, gloss));
        Assert.Contains(RootCauseGlossary.Interval95, used);
    }

    [Theory]
    [MemberData(nameof(DocumentNames))]
    public void ATerm_IsGlossedAtMostOnce_OutsideTheKeyTerms(string name)
    {
        string text = BenchmarkReportPackRenderer.Render(Document(name), FullNamed, out var used);
        string body = WithoutSection(text, "## Key terms");

        foreach (var term in used.Select(RootCauseGlossary.Get))
        {
            Assert.True(CountOf(body, " (" + term.Gloss + ")") <= 1, term.Key + " is glossed more than once");
        }
    }

    [Fact]
    public void TheHolmAdjustment_IsGlossedAtItsFirstUse_AndNotAfter()
    {
        string text = BenchmarkReportPackRenderer.Render(Document("comparison"), FullNamed, out _);
        string body = WithoutSection(text, "## Key terms");
        string gloss = " (" + RootCauseGlossary.Get(RootCauseGlossary.Holm).Gloss + ")";

        var uses = Regex.Matches(body, @"Holm-adjusted across \d+ tests( \([^)]*\))?").Cast<Match>().ToList();
        Assert.True(uses.Count >= 2, "the fixture is expected to name the Holm adjustment more than once");
        Assert.Equal(gloss, uses[0].Groups[1].Value);
        Assert.All(uses.Skip(1), m => Assert.Equal(string.Empty, m.Groups[1].Value));
    }

    [Fact]
    public void PointsAtRisk_AreGlossedAtTheirFirstUse()
    {
        string text = BenchmarkReportPackRenderer.Render(Document("comparison"), FullNamed, out _);
        string keyTerms = Section(text, "## Key terms");
        string after = text[(text.IndexOf(keyTerms, StringComparison.Ordinal) + keyTerms.Length)..];
        string prose = string.Join("\n", after.Split('\n').Where(l => !l.StartsWith("|", StringComparison.Ordinal)));
        string glossed = "points at risk (" + RootCauseGlossary.Get(RootCauseGlossary.PointsAtRisk).Gloss + ")";

        int first = prose.IndexOf("points at risk", StringComparison.Ordinal);
        Assert.True(first >= 0);
        Assert.Equal(first, prose.IndexOf(glossed, StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------------------------------------
    // Null hypotheses
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void ANullHypothesis_ReadsRejectedOrStands_WithItsGradeInParentheses()
    {
        Assert.Equal("Rejected — a real difference (Not supported)", RootCauseGrades.NullWordWithGrade(RootCauseGrades.NotSupported));
        Assert.Equal("Stands — may be chance (Moderate)", RootCauseGrades.NullWordWithGrade(RootCauseGrades.Moderate));
        Assert.Equal("Stands — may be chance (Low)", RootCauseGrades.NullWordWithGrade(RootCauseGrades.Low));
        Assert.Equal("Unknown", RootCauseGrades.NullWordWithGrade(null));

        var rejected = new RootCauseHypothesis { Id = "H0-Q", Kind = "null", Grade = RootCauseGrades.NotSupported };
        var stands = new RootCauseHypothesis { Id = "H0-S", Kind = "null", Grade = RootCauseGrades.Moderate };
        var refuted = new RootCauseHypothesis { Id = "HQ1", Kind = "pair", Grade = RootCauseGrades.NotSupported };
        Assert.Equal("Rejected — a real difference (Not supported)", RootCauseGrades.HypothesisWordWithGrade(rejected));
        Assert.Equal("Stands — may be chance (Moderate)", RootCauseGrades.HypothesisWordWithGrade(stands));
        Assert.Equal("Ruled out (Not supported)", RootCauseGrades.HypothesisWordWithGrade(refuted));

        // The grade itself is unchanged, so sorting and the grade rules read it as before.
        Assert.Equal(RootCauseGrades.NotSupported, rejected.Grade);
        Assert.Equal(0, RootCauseGrades.Rank(rejected.Grade));
    }

    [Fact]
    public void AppendixC_PrintsEachNullHypothesis_InPlainWords()
    {
        var document = Document("comparison");
        var sheet = BenchmarkReportJson.Deserialize<BenchmarkReportFactSheet>(document.FactsJson);
        var nulls = sheet.RootCause!.Hypotheses.Where(h => h.Kind == "null").ToList();
        Assert.NotEmpty(nulls);

        string appendix = Section(BenchmarkReportPackRenderer.Render(document, FullNamed), "## Appendix C: Hypothesis details");

        foreach (var h in nulls)
        {
            string row = appendix.Split('\n').Single(l => l.StartsWith("| " + h.Key + " |", StringComparison.Ordinal));
            Assert.EndsWith(" | " + RootCauseGrades.HypothesisWordWithGrade(h) + " |", row);
            Assert.DoesNotContain("Ruled out", row);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // The fact sheet
    // ---------------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(DocumentNames))]
    public void TheFactSheetSection_IsInTheMarkdownCopyOnly(string name)
    {
        string markdown = BenchmarkReportPackRenderer.Render(Document(name), FullNamed);
        Assert.Contains("\n## Fact sheet\n\n```json\n", markdown);

        var native = new BenchmarkReportRenderOptions
        {
            Disclosure = BenchmarkReportDisclosure.Full,
            PeerNaming = BenchmarkReportPeerNaming.Named,
            IncludeFrontMatter = false,
            IncludeDocumentFooter = false,
            IncludeFactSheet = false
        };
        string pdfBound = BenchmarkReportPackRenderer.Render(Document(name), native);
        Assert.DoesNotContain("## Fact sheet", pdfBound);
        Assert.DoesNotContain("Markdown copy", pdfBound);
        Assert.DoesNotContain("```json", pdfBound);
    }

    // ---------------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------------

    /// <summary>The text from <paramref name="heading"/> to the next level-two heading.</summary>
    private static string Section(string text, string heading)
    {
        int start = text.IndexOf(heading + "\n", StringComparison.Ordinal);
        Assert.True(start >= 0, heading + " is missing");
        int end = text.IndexOf("\n## ", start + heading.Length, StringComparison.Ordinal);
        return end < 0 ? text[start..] : text[start..(end + 1)];
    }

    /// <summary>The text with the section under <paramref name="heading"/> left out.</summary>
    private static string WithoutSection(string text, string heading)
    {
        string section = Section(text, heading);
        int start = text.IndexOf(section, StringComparison.Ordinal);
        return text[..start] + text[(start + section.Length)..];
    }

    private static int CountOf(string text, string value)
    {
        int count = 0;
        for (int at = text.IndexOf(value, StringComparison.Ordinal); at >= 0; at = text.IndexOf(value, at + value.Length, StringComparison.Ordinal)) count++;
        return count;
    }
}
