namespace Overseer.Tests.UnitTests;

using System;
using System.Linq;
using System.Text.Json.Nodes;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services.Benchmarking;
using Overseer.Services.Benchmarking.Pdf;
using Xunit;

/// <summary>
/// What the PDF and Word cover says about a report-pack document's comparison, and the download
/// name that keeps a comparison document apart from the run's own documents: the subject line, the
/// Compared with and Pricing basis rows under either peer naming, and the <c>vs-</c> part of the
/// file name that names the peers or counts them. A stand-alone document keeps its cover and name.
/// A document of a numbered comparison opens its cover with a Comparison row, names the comparison in
/// its subject line and running header, and is named <c>comparison-&lt;id&gt;_…</c>; an anonymized copy
/// never prints the comparison's name.
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

    [Theory]
    [InlineData(BenchmarkReportPeerNaming.Named)]
    [InlineData(BenchmarkReportPeerNaming.Anonymized)]
    public void ABatteryCover_NamesTheBatteryRun_ItsSuitesAndItsRounds(BenchmarkReportPeerNaming naming)
    {
        var document = BatteryReportFixture.Document(BenchmarkReportAudience.TechnicalReport);

        var info = BenchmarkPdfDocumentInfo.ForReportDocument(document, Options(naming, BenchmarkReportDisclosure.Full), BenchmarkPdfPaper.A4);

        Assert.Equal("Battery run #9 — Core knowledge (2 suites, 2 runs per suite)", info.SubjectLine);
        Assert.Equal(
            new[] { "Document ID", "Disclosure", "Peers", "Battery", "Questions", "Battery run", "Member runs", "Created (UTC)", "Generated format", "Writer", "Provenance" },
            info.Facts.Select(f => f.Label));
        Assert.Equal("Core knowledge, revision 2 (2 suites)", Fact(info, "Battery"));
        Assert.Equal("#9", Fact(info, "Battery run"));
        Assert.Equal("4", Fact(info, "Member runs"));
        Assert.Equal("none (stand-alone report)", Fact(info, "Peers"));
    }

    [Fact]
    public void ABatteryCoverWithAPeer_SaysItIsAComparison()
    {
        var document = BatteryReportFixture.Document(BenchmarkReportAudience.ExecutiveSummary);
        var sheet = BatteryReportFixture.Sheet();
        sheet.Peers.Add(new BenchmarkReportPeer { Letter = "A", EntryKey = "battery:11", Label = "Grok 5", DisplayName = "Grok 5", Provider = "xAI", ModelId = "grok-5" });
        document.FactsJson = BenchmarkReportJson.Serialize(sheet);

        var info = BenchmarkPdfDocumentInfo.ForReportDocument(document, Options(BenchmarkReportPeerNaming.Named), BenchmarkPdfPaper.A4);

        Assert.Equal("Battery run #9 — Core knowledge (2 suites, 2 runs per suite) · compared with 1 model", info.SubjectLine);
        Assert.Equal("Grok 5 (1 model)", Fact(info, "Compared with"));
        Assert.DoesNotContain(info.Facts, f => f.Label == "Suite");
    }

    // ---------------------------------------------------------------------------------------------
    // The file name
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void ABatteryDocumentsName_StartsWithTheBatteryRunNumber()
    {
        var document = BatteryReportFixture.Document(BenchmarkReportAudience.ExecutiveSummary);
        var options = Options(BenchmarkReportPeerNaming.Named, BenchmarkReportDisclosure.Full);

        string name = BenchmarkPdfFileNames.ForReportDocument(document, options);

        Assert.Equal("battery-run-9_" + BenchmarkPdfFileNames.SafeFileName(document.Title) + "_full_named_INTERNAL.pdf", name);
        Assert.Equal(name[..^".pdf".Length] + ".docx", BenchmarkPdfFileNames.ForReportDocument(document, options, "docx"));

        var researcher = BatteryReportFixture.Document(BenchmarkReportAudience.TechnicalReport);
        Assert.StartsWith("battery-run-9_gpt-5.6-luna-on-the-overseer-gnollhack-assistant-benchmark_Researcher_Report_",
            BenchmarkPdfFileNames.ForReportDocument(researcher, options));
    }

    [Fact]
    public void ABatteryDocumentWithPeers_NamesThePeers_AfterTheBatteryRunNumber()
    {
        var document = BatteryReportFixture.Document(BenchmarkReportAudience.ExecutiveSummary);
        var sheet = BatteryReportFixture.Sheet();
        sheet.Peers.Add(new BenchmarkReportPeer { Letter = "A", EntryKey = "battery:11", Label = "Grok 5" });
        document.FactsJson = BenchmarkReportJson.Serialize(sheet);

        Assert.StartsWith("battery-run-9_vs-battery-run-11_", BenchmarkPdfFileNames.ForReportDocument(document, Options(BenchmarkReportPeerNaming.Named)));
    }

    [Fact]
    public void AComparisonDocumentsName_NamesThePeersInLetterOrder_AfterTheRunNumber()
    {
        var researcher = BenchmarkReportPackFixture.Document(BenchmarkReportAudience.TechnicalReport);
        var executive = BenchmarkReportPackFixture.Document(BenchmarkReportAudience.ExecutiveSummary);

        Assert.Equal(
            "run-12_vs-run-14-run-13_" + SheetTitleBase + "_Researcher_Report_detailed_anonymized.pdf",
            BenchmarkPdfFileNames.ForReportDocument(researcher, Options(BenchmarkReportPeerNaming.Anonymized, BenchmarkReportDisclosure.Detailed)));
        Assert.Equal(
            "run-12_vs-run-14-run-13_" + SheetTitleBase + "_Researcher_Report_full_named_INTERNAL.pdf",
            BenchmarkPdfFileNames.ForReportDocument(researcher, Options(BenchmarkReportPeerNaming.Named, BenchmarkReportDisclosure.Full)));

        string pdf = BenchmarkPdfFileNames.ForReportDocument(executive, Options(BenchmarkReportPeerNaming.Named));
        Assert.Equal("run-12_vs-run-14-run-13_" + SheetTitleBase + "-executive-summary_summary_named.pdf", pdf);
        Assert.Equal(pdf[..^".pdf".Length] + ".docx", BenchmarkPdfFileNames.ForReportDocument(executive, Options(BenchmarkReportPeerNaming.Named), "docx"));
    }

    [Fact]
    public void APeerGroup_IsNamedAsAGroup_AndTheComparisonKeyIsNotUsedForNamedPeers()
    {
        var document = WithSheet(BenchmarkReportAudience.ExecutiveSummary, sheet => sheet.Peers.Single(p => p.Letter == "B").EntryKey = "group:5");
        document.ComparisonKey = new string('c', 64);

        Assert.StartsWith("run-12_vs-run-14-group-5_" + SheetTitleBase, BenchmarkPdfFileNames.ForReportDocument(document, Options(BenchmarkReportPeerNaming.Named)));
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
    public void ANonRunSubjectsName_StartsWithThePeers(string subjectKey)
    {
        var document = BenchmarkReportPackFixture.Document(BenchmarkReportAudience.ExecutiveSummary);
        document.SubjectKey = subjectKey;

        Assert.Equal(
            "vs-run-14-run-13_" + SheetTitleBase + "-executive-summary_summary_named.pdf",
            BenchmarkPdfFileNames.ForReportDocument(document, Options(BenchmarkReportPeerNaming.Named)));
    }

    [Fact]
    public void OneToThreePeers_AreNamed_AndFourAreCountedWithTheComparisonKey()
    {
        var one = WithSheet(BenchmarkReportAudience.ExecutiveSummary, sheet => sheet.Peers.RemoveAll(p => p.Letter == "B"));
        var three = WithSheet(BenchmarkReportAudience.ExecutiveSummary, sheet =>
            sheet.Peers.Add(new BenchmarkReportPeer { Letter = "C", EntryKey = "run:15", Label = "Qwen 4" }));
        var four = WithSheet(BenchmarkReportAudience.ExecutiveSummary, AddPeers);
        four.ComparisonKey = "3f9a0c21" + new string('0', 56);

        Assert.StartsWith("run-12_vs-run-14_" + SheetTitleBase, BenchmarkPdfFileNames.ForReportDocument(one, Options(BenchmarkReportPeerNaming.Named)));
        Assert.StartsWith("run-12_vs-run-14-run-13-run-15_" + SheetTitleBase, BenchmarkPdfFileNames.ForReportDocument(three, Options(BenchmarkReportPeerNaming.Named)));
        Assert.StartsWith("run-12_vs-4-models-3f9a0c21_" + SheetTitleBase, BenchmarkPdfFileNames.ForReportDocument(four, Options(BenchmarkReportPeerNaming.Named)));
    }

    [Fact]
    public void APeerWhoseEntryKeyCannotBeRead_CountsThePeers_AndALegacyRowWithoutAComparisonKeyKeepsTheOldForm()
    {
        var document = WithSheet(BenchmarkReportAudience.ExecutiveSummary, sheet => sheet.Peers.Single(p => p.Letter == "B").EntryKey = "custom:13");
        document.ComparisonKey = "0123456789abcdef" + new string('0', 48);

        Assert.StartsWith("run-12_vs-2-models-01234567_" + SheetTitleBase, BenchmarkPdfFileNames.ForReportDocument(document, Options(BenchmarkReportPeerNaming.Named)));

        document.ComparisonKey = null;
        Assert.StartsWith("run-12_vs-2-models_" + SheetTitleBase, BenchmarkPdfFileNames.ForReportDocument(document, Options(BenchmarkReportPeerNaming.Named)));

        var four = WithSheet(BenchmarkReportAudience.ExecutiveSummary, AddPeers);
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

    // ---------------------------------------------------------------------------------------------
    // A numbered comparison: the cover, the subject line and the running header
    // ---------------------------------------------------------------------------------------------

    private const string ComparisonName = "GPT-5.6 Luna vs Grok 5 vs Mistral Large 4";

    private static BenchmarkComparison Comparison(string name, int entryCount) => new()
    {
        Id = 12,
        ComparisonKey = new string('c', 64),
        EntryKeysJson = "[]",
        SubjectKind = BenchmarkComparisonSubjectKind.Runs,
        EntryCount = entryCount,
        DefaultName = name,
        CreatedAtUtc = BenchmarkReportPackFixture.CreatedAt
    };

    /// <summary>The fixture document of <paramref name="audience"/> as a per-model document of Comparison #12, its row loaded.</summary>
    private static BenchmarkReportDocument InComparison(BenchmarkReportAudience audience, string name = ComparisonName, int entryCount = 3)
    {
        var document = BenchmarkReportPackFixture.Document(audience);
        document.ComparisonId = 12;
        document.Comparison = Comparison(name, entryCount);
        document.CoveredEntryKeysJson = "[\"run:12\"]";
        document.CoveredSetKey = new string('5', 64);
        return document;
    }

    private static readonly (string Key, string Label, string Provider)[] FiveModels =
    {
        ("run:12", "GPT-5.6 Luna", "OpenAI"),
        ("run:14", "Grok 5", "xAI"),
        ("run:13", "Mistral Large 4", "Mistral"),
        ("run:15", "Qwen 4", "Alibaba"),
        ("run:16", "Llama 6", "Meta")
    };

    /// <summary>
    /// A comparison-scope document of Comparison #12 (five models, "Five-model comparison") covering
    /// <paramref name="covered"/>, its sheet carrying <c>coversAllEntries</c>, <c>comparisonEntryCount</c>
    /// and a <c>models</c> array.
    /// </summary>
    private static BenchmarkReportDocument ComparisonScope(
        BenchmarkReportAudience audience, bool coversAll, params (string Key, string Label, string Provider)[] covered)
    {
        var document = InComparison(audience, "Five-model comparison", 5);
        document.Scope = BenchmarkReportScope.Comparison;
        document.SubjectKey = coversAll ? "comparison:12" : "comparison:12/" + new string('9', 16);
        document.CoveredEntryKeysJson = "[" + string.Join(",", covered.Select(c => "\"" + c.Key + "\"")) + "]";
        document.CoveredSetKey = "3f9a0c" + new string('0', 58);

        var facts = JsonNode.Parse(document.FactsJson)!.AsObject();
        facts["coversAllEntries"] = coversAll;
        facts["comparisonEntryCount"] = 5;
        var models = new JsonArray();
        foreach (var (key, label, provider) in covered)
        {
            models.Add(new JsonObject { ["entryKey"] = key, ["label"] = label, ["provider"] = provider });
        }
        facts["models"] = models;
        document.FactsJson = facts.ToJsonString();
        return document;
    }

    [Fact]
    public void AComparisonDocumentsCover_OpensWithTheComparisonRow_AndNamesTheComparisonInItsHeader()
    {
        var info = BenchmarkPdfDocumentInfo.ForReportDocument(
            InComparison(BenchmarkReportAudience.ExecutiveSummary), Options(BenchmarkReportPeerNaming.Named), BenchmarkPdfPaper.A4);

        Assert.Equal(new[] { "Comparison" }.Concat(ComparisonLabels), info.Facts.Select(f => f.Label));
        Assert.Equal("Comparison #12 — " + ComparisonName + " · 3 models · computed 2026-09-20", Fact(info, "Comparison"));
        Assert.Equal("GnollHack Core Suite · run #12 · compared with 2 models · Comparison #12", info.SubjectLine);
        Assert.Equal("Comparison #12 — " + ComparisonName, info.HeaderText);
        Assert.Equal(info.HeaderText, info.RunningHeaderText);
    }

    [Fact]
    public void AnAnonymizedCopy_NumbersTheComparison_WithoutItsName()
    {
        var info = BenchmarkPdfDocumentInfo.ForReportDocument(
            InComparison(BenchmarkReportAudience.ExecutiveSummary), Options(BenchmarkReportPeerNaming.Anonymized), BenchmarkPdfPaper.A4);

        Assert.Equal("Comparison #12 · 3 models · computed 2026-09-20", Fact(info, "Comparison"));
        Assert.Equal("Comparison #12", info.HeaderText);
        foreach (string name in new[] { "Grok", "Mistral" })
        {
            Assert.All(info.Facts, f => Assert.DoesNotContain(name, f.Value));
            Assert.DoesNotContain(name, info.SubjectLine);
            Assert.DoesNotContain(name, info.RunningHeaderText);
        }
    }

    [Fact]
    public void ARowWhoseComparisonIsNotLoaded_PrintsTheNumberAlone()
    {
        var document = BenchmarkReportPackFixture.Document(BenchmarkReportAudience.ExecutiveSummary);
        document.ComparisonId = 12;

        var info = BenchmarkPdfDocumentInfo.ForReportDocument(document, Options(BenchmarkReportPeerNaming.Named), BenchmarkPdfPaper.A4);

        Assert.Equal("Comparison #12 · computed 2026-09-20", Fact(info, "Comparison"));
        Assert.Equal("Comparison #12", info.HeaderText);
    }

    [Fact]
    public void ALegacyRowWithoutAComparison_HasNoComparisonRow_AndItsHeaderIsTheSubjectLine()
    {
        var info = BenchmarkPdfDocumentInfo.ForReportDocument(
            BenchmarkReportPackFixture.Document(BenchmarkReportAudience.ExecutiveSummary), Options(BenchmarkReportPeerNaming.Named), BenchmarkPdfPaper.A4);

        Assert.DoesNotContain(info.Facts, f => f.Label == "Comparison");
        Assert.Null(info.HeaderText);
        Assert.Equal(info.SubjectLine, info.RunningHeaderText);
        Assert.Equal("GnollHack Core Suite · run #12 · compared with 2 models", info.SubjectLine);
    }

    [Theory]
    [InlineData(BenchmarkReportPeerNaming.Named, "Comparison #12 — Five-model comparison")]
    [InlineData(BenchmarkReportPeerNaming.Anonymized, "Comparison #12")]
    public void AComparisonWideCover_IsTheComparison(BenchmarkReportPeerNaming naming, string heading)
    {
        var document = ComparisonScope(BenchmarkReportAudience.ExecutiveSummary, coversAll: true, FiveModels);

        var info = BenchmarkPdfDocumentInfo.ForReportDocument(document, Options(naming), BenchmarkPdfPaper.A4);

        Assert.Equal(heading, info.SubjectLine);
        Assert.Equal(heading, info.HeaderText);
        Assert.Equal(heading + " · 5 models · computed 2026-09-20", Fact(info, "Comparison"));
        Assert.Equal("Comparison", info.Facts[0].Label);
    }

    [Theory]
    [InlineData(BenchmarkReportPeerNaming.Named, "Comparison #12 — Five-model comparison · 2 of 5 models: GPT-5.6 Luna and Grok 5")]
    [InlineData(BenchmarkReportPeerNaming.Anonymized, "Comparison #12 · 2 of 5 models")]
    public void ASubsetCover_CountsItsModels_AndNamesThemInANamedCopy(BenchmarkReportPeerNaming naming, string subjectLine)
    {
        var document = ComparisonScope(BenchmarkReportAudience.ExecutiveSummary, coversAll: false, FiveModels[0], FiveModels[1]);

        var info = BenchmarkPdfDocumentInfo.ForReportDocument(document, Options(naming), BenchmarkPdfPaper.A4);

        Assert.Equal(subjectLine, info.SubjectLine);
        Assert.Equal(naming == BenchmarkReportPeerNaming.Named ? "Comparison #12 — Five-model comparison" : "Comparison #12", info.HeaderText);
    }

    [Fact]
    public void Ellipsize_KeepsTextThatFits_AndCutsLongTextAtAWord()
    {
        Assert.Equal("Comparison #12", BenchmarkPdfDocumentInfo.Ellipsize("Comparison #12", 20));
        string cut = BenchmarkPdfDocumentInfo.Ellipsize("Comparison #12 — GPT-5.6 Luna vs Grok 5 vs Mistral Large 4", 30);
        Assert.True(cut.Length <= 30, cut);
        Assert.EndsWith("…", cut, StringComparison.Ordinal);
        Assert.Equal("Comparison #12 — GPT-5.6 Luna…", cut);
    }

    // ---------------------------------------------------------------------------------------------
    // A numbered comparison: the file name
    // ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportDisclosure.Summary, BenchmarkReportPeerNaming.Named, "pdf",
        "comparison-12_gpt-5.6-luna_executive-summary_summary_named.pdf")]
    [InlineData(BenchmarkReportAudience.TechnicalReport, BenchmarkReportDisclosure.Full, BenchmarkReportPeerNaming.Named, "pdf",
        "comparison-12_gpt-5.6-luna_researcher-report_full_named_INTERNAL.pdf")]
    [InlineData(BenchmarkReportAudience.TechnicalReport, BenchmarkReportDisclosure.Detailed, BenchmarkReportPeerNaming.Anonymized, "pdf",
        "comparison-12_gpt-5.6-luna_researcher-report_detailed_anonymized.pdf")]
    [InlineData(BenchmarkReportAudience.InternalBrief, BenchmarkReportDisclosure.Full, BenchmarkReportPeerNaming.Anonymized, "docx",
        "comparison-12_gpt-5.6-luna_internal-brief_full_anonymized_INTERNAL.docx")]
    public void APerModelDocumentOfAComparison_IsNamedAfterTheComparisonAndTheModel(
        BenchmarkReportAudience audience, BenchmarkReportDisclosure disclosure, BenchmarkReportPeerNaming naming, string extension, string expected)
    {
        Assert.Equal(expected, BenchmarkPdfFileNames.ForReportDocument(InComparison(audience), Options(naming, disclosure), extension));
    }

    [Fact]
    public void APerModelName_TakesTheModelSlugFromTheSubjectLabel_AndNeedsOnlyTheComparisonNumber()
    {
        var document = InComparison(BenchmarkReportAudience.ExecutiveSummary);
        document.SubjectLabel = "GPT-5.6 Luna (max)";
        var options = Options(BenchmarkReportPeerNaming.Named, BenchmarkReportDisclosure.Full);

        Assert.Equal("comparison-12_gpt-5.6-luna-max_executive-summary_full_named_INTERNAL.pdf", BenchmarkPdfFileNames.ForReportDocument(document, options));

        document.Comparison = null;
        Assert.Equal("comparison-12_gpt-5.6-luna-max_executive-summary_full_named_INTERNAL.pdf", BenchmarkPdfFileNames.ForReportDocument(document, options));
    }

    [Theory]
    [InlineData(BenchmarkReportPeerNaming.Named, "comparison-12_gpt-5.6-luna-max-vs-gpt-6.1-sol-medium_executive-summary_summary_named.pdf")]
    [InlineData(BenchmarkReportPeerNaming.Anonymized, "comparison-12_executive-summary_summary_anonymized.pdf")]
    public void AComparisonWideDocument_IsNamedAfterTheComparison_AndAnAnonymizedCopyLeavesTheNameOut(BenchmarkReportPeerNaming naming, string expected)
    {
        var document = ComparisonScope(BenchmarkReportAudience.ExecutiveSummary, coversAll: true, FiveModels);
        document.Comparison!.Name = "GPT-5.6 Luna (max) vs GPT-6.1 Sol (medium)";

        Assert.Equal(expected, BenchmarkPdfFileNames.ForReportDocument(document, Options(naming)));
    }

    [Fact]
    public void ASubsetOfUpToThreeModels_IsNamedAfterThem_AndALargerOrAnonymizedOneIsCounted()
    {
        var two = ComparisonScope(BenchmarkReportAudience.ExecutiveSummary, coversAll: false, FiveModels[0], FiveModels[1]);
        Assert.Equal("comparison-12_subset-gpt-5.6-luna-vs-grok-5_executive-summary_summary_named.pdf",
            BenchmarkPdfFileNames.ForReportDocument(two, Options(BenchmarkReportPeerNaming.Named)));
        Assert.Equal("comparison-12_subset-2-of-5-models-3f9a0c_executive-summary_summary_anonymized.pdf",
            BenchmarkPdfFileNames.ForReportDocument(two, Options(BenchmarkReportPeerNaming.Anonymized)));

        var four = ComparisonScope(BenchmarkReportAudience.InternalBrief, coversAll: false, FiveModels[0], FiveModels[1], FiveModels[2], FiveModels[3]);
        Assert.Equal("comparison-12_subset-4-of-5-models-3f9a0c_internal-brief_full_named_INTERNAL.pdf",
            BenchmarkPdfFileNames.ForReportDocument(four, Options(BenchmarkReportPeerNaming.Named, BenchmarkReportDisclosure.Full)));
    }

    [Fact]
    public void ALongCoveredSlug_IsCutAtAHyphenWithinFortyCharacters()
    {
        var three = ComparisonScope(BenchmarkReportAudience.ExecutiveSummary, coversAll: false,
            ("run:12", "Claude 5.5 Opus Extended Thinking", "Anthropic"),
            ("run:14", "Gemini 3.8 Pro Deep Think", "Google"),
            ("run:13", "GPT-6.1 Sol Medium Reasoning", "OpenAI"));

        Assert.Equal("comparison-12_subset-claude-5.5-opus-extended-thinking_executive-summary_summary_named.pdf",
            BenchmarkPdfFileNames.ForReportDocument(three, Options(BenchmarkReportPeerNaming.Named)));
    }

    [Theory]
    [InlineData("GPT-5.6 Luna (max) vs GPT-6.1 Sol (medium)", "gpt-5.6-luna-max-vs-gpt-6.1-sol-medium")]
    [InlineData("10 models · Core knowledge battery revision three", "10-models-core-knowledge-battery")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void TheNameSlug_IsTheSafeFileName_CutAtTheLastHyphenWithinFortyCharacters(string name, string slug)
    {
        Assert.Equal(slug, BenchmarkPdfFileNames.NameSlug(name));
        Assert.True(BenchmarkPdfFileNames.NameSlug(name).Length <= BenchmarkPdfFileNames.MaxNameSlugLength);
    }

    [Theory]
    [InlineData(BenchmarkReportAudience.ExecutiveSummary, "executive-summary")]
    [InlineData(BenchmarkReportAudience.TechnicalReport, "researcher-report")]
    [InlineData(BenchmarkReportAudience.InternalBrief, "internal-brief")]
    public void TheKindSlugs_AreOneSpellingEach(BenchmarkReportAudience audience, string slug)
    {
        Assert.Equal(slug, BenchmarkPdfFileNames.KindSlug(audience));
    }

    [Fact]
    public void RunAndBatteryCompletionDocuments_AndALegacyReportPackRow_KeepTodaysNames()
    {
        var options = Options(BenchmarkReportPeerNaming.Named, BenchmarkReportDisclosure.Full);

        var run = BenchmarkReportPackFixture.StandaloneDocument(BenchmarkReportAudience.ExecutiveSummary);
        string runName = BenchmarkPdfFileNames.ForReportDocument(run, options);
        run.ComparisonId = 12;
        run.Comparison = Comparison(ComparisonName, 3);
        Assert.Equal(runName, BenchmarkPdfFileNames.ForReportDocument(run, options));
        Assert.StartsWith("run-12_", runName, StringComparison.Ordinal);

        var battery = BatteryReportFixture.Document(BenchmarkReportAudience.ExecutiveSummary);
        string batteryName = BenchmarkPdfFileNames.ForReportDocument(battery, options);
        battery.ComparisonId = 12;
        Assert.Equal(batteryName, BenchmarkPdfFileNames.ForReportDocument(battery, options));
        Assert.StartsWith("battery-run-9_", batteryName, StringComparison.Ordinal);

        var legacy = BenchmarkReportPackFixture.Document(BenchmarkReportAudience.ExecutiveSummary);
        Assert.Null(legacy.ComparisonId);
        Assert.Equal("run-12_vs-run-14-run-13_" + SheetTitleBase + "-executive-summary_full_named_INTERNAL.pdf",
            BenchmarkPdfFileNames.ForReportDocument(legacy, options));
    }
}
