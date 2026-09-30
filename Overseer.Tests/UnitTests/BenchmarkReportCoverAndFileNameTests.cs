namespace Overseer.Tests.UnitTests;

using System;
using System.Linq;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services.Benchmarking;
using Overseer.Services.Benchmarking.Pdf;
using Xunit;

/// <summary>
/// What the PDF and Word cover says about a report-pack document's comparison, and the download
/// name that keeps a comparison document apart from the run's own documents: the subject line, the
/// Compared with and Pricing basis rows under either peer naming, and the <c>vs-&lt;N&gt;-models_</c>
/// part of the file name. A stand-alone document keeps its cover and name.
/// </summary>
public class BenchmarkReportCoverAndFileNameTests
{
    private const string SheetTitleBase = "gpt-5.6-luna-on-the-overseer-gnollhack-assistant-benchmark";

    private static readonly string[] ComparisonLabels =
    {
        "Document ID", "Disclosure", "Compared with", "Pricing basis", "Suite", "Questions", "Run", "Created (UTC)", "Generated format",
        "Writer", "Provenance"
    };

    private static BenchmarkReportRenderOptions Options(
        BenchmarkReportPeerNaming naming, BenchmarkReportDisclosure disclosure = BenchmarkReportDisclosure.Summary)
        => new() { Disclosure = disclosure, PeerNaming = naming };

    /// <summary>The fixture document of <paramref name="audience"/> with its fact sheet changed.</summary>
    private static BenchmarkReportDocument WithSheet(BenchmarkReportAudience audience, Action<BenchmarkReportFactSheet> change)
    {
        var document = BenchmarkReportPackFixture.Document(audience);
        var sheet = BenchmarkReportPackFixture.Sheet();
        change(sheet);
        document.FactsJson = BenchmarkReportJson.Serialize(sheet);
        return document;
    }

    private static void AddPeers(BenchmarkReportFactSheet sheet)
    {
        sheet.Peers.Add(new BenchmarkReportPeer { Letter = "C", EntryKey = "run:15", Label = "Qwen 4", DisplayName = "Qwen 4", Provider = "Alibaba", ModelId = "qwen-4" });
        sheet.Peers.Add(new BenchmarkReportPeer { Letter = "D", EntryKey = "run:16", Label = "Llama 6", DisplayName = "Llama 6", Provider = "Meta", ModelId = "llama-6" });
    }

    private static string Fact(BenchmarkPdfDocumentInfo info, string label) => info.Facts.Single(f => f.Label == label).Value;

    // ---------------------------------------------------------------------------------------------
    // The cover
    // ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(BenchmarkReportPeerNaming.Named, "Grok 5 and Mistral Large 4 (2 models)")]
    [InlineData(BenchmarkReportPeerNaming.Anonymized, "2 models (A and B), identities withheld")]
    public void ACoverWithPeers_SaysItIsAComparison_AndStatesThePricingBasis(BenchmarkReportPeerNaming naming, string comparedWith)
    {
        var document = BenchmarkReportPackFixture.Document(BenchmarkReportAudience.ExecutiveSummary);

        var info = BenchmarkPdfDocumentInfo.ForReportDocument(document, Options(naming), BenchmarkPdfPaper.A4);

        Assert.Equal("GnollHack Core Suite · run #12 · compared with 2 models", info.SubjectLine);
        Assert.Equal(ComparisonLabels, info.Facts.Select(f => f.Label));
        Assert.Equal(comparedWith, Fact(info, "Compared with"));
        Assert.Equal("Catalog prices on 2026-09-20 (price card dated 2026-09-01)", Fact(info, "Pricing basis"));
        if (naming == BenchmarkReportPeerNaming.Anonymized)
        {
            foreach (string name in new[] { "Grok", "grok", "xAI", "Mistral", "mistral" })
            {
                Assert.All(info.Facts, f => Assert.DoesNotContain(name, f.Value));
                Assert.DoesNotContain(name, info.SubjectLine);
            }
        }
    }

    [Theory]
    [InlineData(BenchmarkReportPeerNaming.Named, "Grok 5, Mistral Large 4, Qwen 4 and Llama 6 (4 models)")]
    [InlineData(BenchmarkReportPeerNaming.Anonymized, "4 models (A to D), identities withheld")]
    public void ACoverWithFourPeers_CountsThem(BenchmarkReportPeerNaming naming, string comparedWith)
    {
        var document = WithSheet(BenchmarkReportAudience.TechnicalReport, AddPeers);

        var info = BenchmarkPdfDocumentInfo.ForReportDocument(document, Options(naming), BenchmarkPdfPaper.A4);

        Assert.Equal("GnollHack Core Suite · run #12 · compared with 4 models", info.SubjectLine);
        Assert.Equal(comparedWith, Fact(info, "Compared with"));
    }

    [Theory]
    [InlineData(BenchmarkReportPeerNaming.Named, "Grok 5 (1 model)")]
    [InlineData(BenchmarkReportPeerNaming.Anonymized, "1 model (A), identity withheld")]
    public void ACoverWithOnePeer_SpeaksOfOneModel(BenchmarkReportPeerNaming naming, string comparedWith)
    {
        var document = WithSheet(BenchmarkReportAudience.ExecutiveSummary, sheet => sheet.Peers.RemoveAll(p => p.Letter == "B"));

        var info = BenchmarkPdfDocumentInfo.ForReportDocument(document, Options(naming), BenchmarkPdfPaper.A4);

        Assert.Equal("GnollHack Core Suite · run #12 · compared with 1 model", info.SubjectLine);
        Assert.Equal(comparedWith, Fact(info, "Compared with"));
    }

    [Fact]
    public void AGroupSubjectsCover_NamesTheGroup_InItsSubjectLine()
    {
        var document = BenchmarkReportPackFixture.Document(BenchmarkReportAudience.ExecutiveSummary);
        document.SubjectKey = "group:5";
        document.SubjectRunIdsJson = "[12,15]";

        var info = BenchmarkPdfDocumentInfo.ForReportDocument(document, Options(BenchmarkReportPeerNaming.Named), BenchmarkPdfPaper.A4);

        Assert.Equal("GnollHack Core Suite · group #5 · compared with 2 models", info.SubjectLine);
        Assert.Equal("#12, #15", Fact(info, "Runs"));
    }

    [Theory]
    [InlineData(BenchmarkReportPeerNaming.Named)]
    [InlineData(BenchmarkReportPeerNaming.Anonymized)]
    public void AStandaloneCover_IsUnchanged(BenchmarkReportPeerNaming naming)
    {
        var document = BenchmarkReportPackFixture.StandaloneDocument(BenchmarkReportAudience.ExecutiveSummary);

        var info = BenchmarkPdfDocumentInfo.ForReportDocument(document, Options(naming, BenchmarkReportDisclosure.Full), BenchmarkPdfPaper.A4);

        Assert.Equal("GnollHack Core Suite · run #12", info.SubjectLine);
        Assert.Equal(
            new[] { "Document ID", "Disclosure", "Peers", "Suite", "Questions", "Run", "Created (UTC)", "Generated format", "Writer", "Provenance" },
            info.Facts.Select(f => f.Label));
        Assert.Equal("none (stand-alone report)", Fact(info, "Peers"));
    }

    [Fact]
    public void ACoverWhoseSheetCannotBeRead_KeepsThePeersRow()
    {
        var document = BenchmarkReportPackFixture.Document(BenchmarkReportAudience.ExecutiveSummary);
        document.FactsJson = "not json";

        var info = BenchmarkPdfDocumentInfo.ForReportDocument(document, Options(BenchmarkReportPeerNaming.Named), BenchmarkPdfPaper.A4);

        Assert.Equal("Named", Fact(info, "Peers"));
        Assert.Equal("GnollHack Core Suite · run #12", info.SubjectLine);
        Assert.DoesNotContain(info.Facts, f => f.Label == "Compared with");
    }

    // ---------------------------------------------------------------------------------------------
    // The file name
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void AComparisonDocumentsName_CarriesThePeerCount_AfterTheRunNumber()
    {
        var researcher = BenchmarkReportPackFixture.Document(BenchmarkReportAudience.TechnicalReport);
        var executive = BenchmarkReportPackFixture.Document(BenchmarkReportAudience.ExecutiveSummary);

        Assert.Equal(
            "run-12_vs-2-models_" + SheetTitleBase + "_Researcher_Report_detailed_anonymized.pdf",
            BenchmarkPdfFileNames.ForReportDocument(researcher, Options(BenchmarkReportPeerNaming.Anonymized, BenchmarkReportDisclosure.Detailed)));
        Assert.Equal(
            "run-12_vs-2-models_" + SheetTitleBase + "_Researcher_Report_full_named_INTERNAL.pdf",
            BenchmarkPdfFileNames.ForReportDocument(researcher, Options(BenchmarkReportPeerNaming.Named, BenchmarkReportDisclosure.Full)));

        string pdf = BenchmarkPdfFileNames.ForReportDocument(executive, Options(BenchmarkReportPeerNaming.Named));
        Assert.Equal("run-12_vs-2-models_" + SheetTitleBase + "-executive-summary_summary_named.pdf", pdf);
        Assert.Equal(pdf[..^".pdf".Length] + ".docx", BenchmarkPdfFileNames.ForReportDocument(executive, Options(BenchmarkReportPeerNaming.Named), "docx"));
    }

    [Fact]
    public void AStandaloneRunDocumentsName_StartsWithTheRunNumber_AndHasNoPeerCount()
    {
        var document = BenchmarkReportPackFixture.StandaloneDocument(BenchmarkReportAudience.ExecutiveSummary);
        document.SubjectKey = "run:73";
        var options = Options(BenchmarkReportPeerNaming.Named, BenchmarkReportDisclosure.Full);

        string name = BenchmarkPdfFileNames.ForReportDocument(document, options);

        Assert.Equal("run-73_" + BenchmarkPdfFileNames.SafeFileName(document.Title) + "_full_named_INTERNAL.pdf", name);
        Assert.DoesNotContain("vs-", name);
        Assert.Equal(name[..^".pdf".Length] + ".docx", BenchmarkPdfFileNames.ForReportDocument(document, options, "docx"));
    }

    [Theory]
    [InlineData("group:5")]
    [InlineData("run:")]
    [InlineData("run:7a")]
    public void ANonRunSubjectsName_StartsWithThePeerCount(string subjectKey)
    {
        var document = BenchmarkReportPackFixture.Document(BenchmarkReportAudience.ExecutiveSummary);
        document.SubjectKey = subjectKey;

        Assert.Equal(
            "vs-2-models_" + SheetTitleBase + "-executive-summary_summary_named.pdf",
            BenchmarkPdfFileNames.ForReportDocument(document, Options(BenchmarkReportPeerNaming.Named)));
    }

    [Fact]
    public void ThePeerCount_IsWrittenAsIs_ForOneAndForFourPeers()
    {
        var one = WithSheet(BenchmarkReportAudience.ExecutiveSummary, sheet => sheet.Peers.RemoveAll(p => p.Letter == "B"));
        var four = WithSheet(BenchmarkReportAudience.ExecutiveSummary, AddPeers);

        Assert.StartsWith("run-12_vs-1-models_" + SheetTitleBase, BenchmarkPdfFileNames.ForReportDocument(one, Options(BenchmarkReportPeerNaming.Named)));
        Assert.StartsWith("run-12_vs-4-models_" + SheetTitleBase, BenchmarkPdfFileNames.ForReportDocument(four, Options(BenchmarkReportPeerNaming.Named)));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("{\"peers\":null}")]
    public void ADocumentWhosePeersCannotBeCounted_IsNamedWithoutAPeerCount(string factsJson)
    {
        var document = BenchmarkReportPackFixture.Document(BenchmarkReportAudience.ExecutiveSummary);
        document.FactsJson = factsJson;

        Assert.Equal(
            "run-12_" + SheetTitleBase + "-executive-summary_summary_named.pdf",
            BenchmarkPdfFileNames.ForReportDocument(document, Options(BenchmarkReportPeerNaming.Named)));
    }
}
