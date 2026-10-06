using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services.Benchmarking;
using Overseer.Services.Benchmarking.Pdf;
using Overseer.Tests.Helpers;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// The joint-rank helper and how documents print ranks whose 95 % intervals overlap; named copies
/// without peer letters and the Letter column of Compared models; the Markdown's Comparison line; and
/// the document list's comparison, scope and covered-model fields with its comparison filter.
/// </summary>
public class BenchmarkReportComparisonIdentityRenderTests
{
    private static BenchmarkReportRenderOptions Options(
        BenchmarkReportPeerNaming naming, BenchmarkReportDisclosure disclosure = BenchmarkReportDisclosure.Full)
        => new() { Disclosure = disclosure, PeerNaming = naming };

    // ---------------------------------------------------------------------------------------------
    // The joint-rank helper
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void TwoOverlappingIntervals_ShareTheFirstRank()
    {
        var ranks = BenchmarkReportFacts.JointRanks(new List<(string Key, double Score, double? Lower, double? Upper)>
        {
            ("run:2", 84, 81, 86),
            ("run:1", 85, 83, 87)
        });

        Assert.Equal(new BenchmarkReportFacts.JointRank(1, true, 2), ranks["run:1"]);
        Assert.Equal(new BenchmarkReportFacts.JointRank(1, true, 2), ranks["run:2"]);
        Assert.Equal("joint 1st of 2 (intervals overlap)", BenchmarkReportFacts.JointRankText(ranks["run:2"], 2));
    }

    [Fact]
    public void SeparateIntervals_RankInScoreOrder_AndPrintPlainRanks()
    {
        var ranks = BenchmarkReportFacts.JointRanks(new List<(string Key, double Score, double? Lower, double? Upper)>
        {
            ("a", 70, 68, 72),
            ("b", 90, 88, 92)
        });

        Assert.Equal(new BenchmarkReportFacts.JointRank(1, false, 1), ranks["b"]);
        Assert.Equal(new BenchmarkReportFacts.JointRank(2, false, 1), ranks["a"]);
        Assert.Equal("2nd of 2", BenchmarkReportFacts.JointRankText(ranks["a"], 2));
    }

    [Fact]
    public void AChainOfOverlappingNeighbors_IsOneGroup_EvenWhereItsEndsDoNotOverlap()
    {
        var ranks = BenchmarkReportFacts.JointRanks(new List<(string Key, double Score, double? Lower, double? Upper)>
        {
            ("c", 78, 74, 81),
            ("a", 90, 86, 94),
            ("b", 84, 80, 87)
        });

        Assert.All(new[] { "a", "b", "c" }, key => Assert.Equal(new BenchmarkReportFacts.JointRank(1, true, 3), ranks[key]));
    }

    [Fact]
    public void AGroupBelowTheTop_TakesTheRankAfterTheEntriesAboveIt()
    {
        var ranks = BenchmarkReportFacts.JointRanks(new List<(string Key, double Score, double? Lower, double? Upper)>
        {
            ("a", 95, 93, 97),
            ("b", 85, 82, 88),
            ("c", 84, 80, 86),
            ("d", 60, 55, 65)
        });

        Assert.Equal(new BenchmarkReportFacts.JointRank(1, false, 1), ranks["a"]);
        Assert.Equal(new BenchmarkReportFacts.JointRank(2, true, 2), ranks["b"]);
        Assert.Equal(new BenchmarkReportFacts.JointRank(2, true, 2), ranks["c"]);
        Assert.Equal(new BenchmarkReportFacts.JointRank(4, false, 1), ranks["d"]);
        Assert.Equal("joint 2nd of 4 (intervals overlap)", BenchmarkReportFacts.JointRankText(ranks["c"], 4));
    }

    [Fact]
    public void TouchingBoundsOverlap_AMissingIntervalOverlapsOnlyAnEqualScore()
    {
        var touching = BenchmarkReportFacts.JointRanks(new List<(string Key, double Score, double? Lower, double? Upper)>
        {
            ("a", 85, 82, 88),
            ("b", 78, 75, 82)
        });
        Assert.True(touching["b"].Joint);

        var missing = BenchmarkReportFacts.JointRanks(new List<(string Key, double Score, double? Lower, double? Upper)>
        {
            ("a", 80, null, null),
            ("b", 79, 75, 85)
        });
        Assert.Equal(new BenchmarkReportFacts.JointRank(1, false, 1), missing["a"]);
        Assert.Equal(new BenchmarkReportFacts.JointRank(2, false, 1), missing["b"]);

        var tied = BenchmarkReportFacts.JointRanks(new List<(string Key, double Score, double? Lower, double? Upper)>
        {
            ("a", 80, null, null),
            ("b", 80, null, null)
        });
        Assert.True(tied["a"].Joint);
        Assert.Equal(1, tied["b"].Rank);
    }

    [Fact]
    public void JointRanks_RefusesARepeatedKey_AndAnswersEmptyForNoEntries()
    {
        Assert.Throws<ArgumentException>(() => BenchmarkReportFacts.JointRanks(new List<(string Key, double Score, double? Lower, double? Upper)>
        {
            ("a", 80, 78, 82),
            ("a", 70, 68, 72)
        }));
        Assert.Empty(BenchmarkReportFacts.JointRanks(new List<(string Key, double Score, double? Lower, double? Upper)>()));
    }

    // ---------------------------------------------------------------------------------------------
    // W2: joint ranks in the documents
    // ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(BenchmarkReportPeerNaming.Named)]
    [InlineData(BenchmarkReportPeerNaming.Anonymized)]
    public void AnOverlappingRank_PrintsAsJoint_InTheKeyFiguresAndTheQualityTable(BenchmarkReportPeerNaming naming)
    {
        // The fixture's three intervals (77–83, 81–89 and 74–83) overlap in a chain.
        string markdown = BenchmarkReportPackRenderer.Render(
            BenchmarkReportPackFixture.Document(BenchmarkReportAudience.TechnicalReport), Options(naming));

        Assert.Contains("- **Intelligence:** 80 / 100 (interval 77–83), joint 1st of 3 (intervals overlap);", markdown, StringComparison.Ordinal);
        Assert.Contains("| 80 | 77–83 | joint 1 |", markdown, StringComparison.Ordinal);
        Assert.Contains("| 85 | 81–89 | joint 1 |", markdown, StringComparison.Ordinal);
        Assert.Contains("ranking joint 1st of 3 (intervals overlap) against", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void ASeparateRank_KeepsItsStoredWording()
    {
        var document = BenchmarkReportPackFixture.Document(BenchmarkReportAudience.TechnicalReport);
        var sheet = BenchmarkReportPackFixture.Sheet();
        var subject = sheet.Entries.Single(e => e.IsSubject);
        subject.QualityLower = 79.2;
        subject.QualityUpper = 81.0;
        var peerA = sheet.Entries.Single(e => e.PeerLetter == "A");
        peerA.QualityLower = 83.0;
        peerA.QualityUpper = 87.0;
        var peerB = sheet.Entries.Single(e => e.PeerLetter == "B");
        peerB.QualityIndex = 70.0;
        peerB.QualityLower = 68.0;
        peerB.QualityUpper = 72.0;
        document.FactsJson = BenchmarkReportJson.Serialize(sheet);

        string markdown = BenchmarkReportPackRenderer.Render(document, Options(BenchmarkReportPeerNaming.Named));

        Assert.Contains("- **Intelligence:** 80 / 100 (interval 77–83), 2nd of 3;", markdown, StringComparison.Ordinal);
        Assert.Contains("| 80 | 79–81 | 2 |", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("joint ", markdown, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------------------------------
    // W1: no peer letter in a named copy; the Letter column
    // ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(BenchmarkReportAudience.ExecutiveSummary, BenchmarkReportDisclosure.Full)]
    [InlineData(BenchmarkReportAudience.TechnicalReport, BenchmarkReportDisclosure.Full)]
    [InlineData(BenchmarkReportAudience.InternalBrief, BenchmarkReportDisclosure.Full)]
    public void ANamedCopy_PrintsPeersWithoutTheirLetters(BenchmarkReportAudience audience, BenchmarkReportDisclosure disclosure)
    {
        string markdown = BenchmarkReportPackRenderer.Render(
            BenchmarkReportPackFixture.Document(audience), Options(BenchmarkReportPeerNaming.Named, disclosure));

        Assert.DoesNotContain("Grok 5 (A)", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("Mistral Large 4 (B)", markdown, StringComparison.Ordinal);
        Assert.Contains("Grok 5", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void ComparedModels_HasALetterColumn_InBothNamings()
    {
        var document = BenchmarkReportPackFixture.Document(BenchmarkReportAudience.TechnicalReport);

        string named = BenchmarkReportPackRenderer.Render(document, Options(BenchmarkReportPeerNaming.Named));
        Assert.Contains("| Model | Provider | Letter | Kind | Runs | Thinking level |", named, StringComparison.Ordinal);
        Assert.Contains("| **GPT-5.6 Luna** | OpenAI | — | run | 1 | high |", named, StringComparison.Ordinal);
        Assert.Contains("| Grok 5 | xAI | A | run | 1 | high |", named, StringComparison.Ordinal);
        Assert.Contains("| Mistral Large 4 | Mistral | B | run | 1 |", named, StringComparison.Ordinal);

        string anonymized = BenchmarkReportPackRenderer.Render(document, Options(BenchmarkReportPeerNaming.Anonymized));
        Assert.Contains("| Model | Letter | Kind | Runs | Thinking level |", anonymized, StringComparison.Ordinal);
        Assert.Contains("| Model A | A | run | 1 | high |", anonymized, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------------------------------
    // The Markdown's Comparison line
    // ---------------------------------------------------------------------------------------------

    private const string ComparisonName = "GPT-5.6 Luna vs Grok 5 vs Mistral Large 4";

    private static BenchmarkComparison NewComparison(string key = "c") => new()
    {
        ComparisonKey = new string(key[0], 64),
        EntryKeysJson = "[\"run:12\",\"run:13\",\"run:14\"]",
        SubjectKind = BenchmarkComparisonSubjectKind.Runs,
        EntryCount = 3,
        DefaultName = ComparisonName,
        CreatedAtUtc = BenchmarkReportPackFixture.CreatedAt
    };

    [Theory]
    [InlineData(BenchmarkReportPeerNaming.Named, "**Comparison:** Comparison #12 — " + ComparisonName + " · 3 models · computed 2026-09-20")]
    [InlineData(BenchmarkReportPeerNaming.Anonymized, "**Comparison:** Comparison #12 · 3 models · computed 2026-09-20")]
    public void TheMarkdown_StatesTheComparisonUnderItsTitle(BenchmarkReportPeerNaming naming, string line)
    {
        var document = BenchmarkReportPackFixture.Document(BenchmarkReportAudience.ExecutiveSummary);
        var comparison = NewComparison();
        comparison.Id = 12;
        document.ComparisonId = 12;
        document.Comparison = comparison;

        string markdown = BenchmarkReportPackRenderer.Render(document, Options(naming));

        Assert.StartsWith("# " + document.Title + "\n\n" + line + "\n\n*", markdown, StringComparison.Ordinal);

        // The PDF and Word downloads leave the front matter out; their cover carries the comparison.
        string native = BenchmarkReportPackRenderer.Render(document, new BenchmarkReportRenderOptions
        {
            Disclosure = BenchmarkReportDisclosure.Full,
            PeerNaming = naming,
            IncludeFrontMatter = false
        });
        Assert.DoesNotContain("**Comparison:**", native, StringComparison.Ordinal);

        // A legacy row without a comparison has no such line.
        Assert.DoesNotContain("**Comparison:**",
            BenchmarkReportPackRenderer.Render(BenchmarkReportPackFixture.Document(BenchmarkReportAudience.ExecutiveSummary), Options(naming)),
            StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------------------------------
    // The render service: the list, the detail and the render path
    // ---------------------------------------------------------------------------------------------

    private static BenchmarkReportRenderService Service(ApplicationDbContext db)
        => new(db, TestChartStores.Unconfigured(), NullLogger<BenchmarkReportRenderService>.Instance);

    private static BenchmarkReportDocument Stored(BenchmarkReportDocument document, int? comparisonId)
    {
        document.Id = 0;
        document.ComparisonId = comparisonId;
        if (comparisonId != null)
        {
            document.CoveredEntryKeysJson = "[\"" + document.SubjectKey + "\"]";
            document.CoveredSetKey = new string('5', 64);
        }
        return document;
    }

    /// <summary>A comparison-scope subset document covering run:12 and run:14, its sheet carrying the comparison-scope fields.</summary>
    private static BenchmarkReportDocument StoredSubset(int comparisonId)
    {
        var document = Stored(BenchmarkReportPackFixture.Document(BenchmarkReportAudience.ExecutiveSummary), comparisonId);
        document.Scope = BenchmarkReportScope.Comparison;
        document.SubjectKey = "comparison:" + comparisonId + "/" + new string('9', 16);
        document.CoveredEntryKeysJson = "[\"run:12\",\"run:14\"]";
        document.CoveredSetKey = new string('7', 64);

        var facts = JsonNode.Parse(document.FactsJson)!.AsObject();
        facts["coversAllEntries"] = false;
        facts["comparisonEntryCount"] = 3;
        facts["models"] = new JsonArray(
            new JsonObject { ["entryKey"] = "run:12", ["label"] = "GPT-5.6 Luna", ["provider"] = "OpenAI" },
            new JsonObject { ["entryKey"] = "run:14", ["label"] = "Grok 5", ["provider"] = "xAI" });
        document.FactsJson = facts.ToJsonString();
        return document;
    }

    [Fact]
    public async Task TheList_CarriesTheComparisonScopeAndCoveredModels_AndFiltersByComparison()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = new ApplicationDbContext(BenchmarkRunExamTests.InMemoryOptions());
        var first = NewComparison("a");
        var second = NewComparison("b");
        second.DefaultName = "Another comparison";
        db.BenchmarkComparisons.AddRange(first, second);
        await db.SaveChangesAsync(ct);

        var perModel = Stored(BenchmarkReportPackFixture.Document(BenchmarkReportAudience.ExecutiveSummary), first.Id);
        var subset = StoredSubset(first.Id);
        var other = Stored(BenchmarkReportPackFixture.Document(BenchmarkReportAudience.TechnicalReport), second.Id);
        var standalone = Stored(BenchmarkReportPackFixture.StandaloneDocument(BenchmarkReportAudience.ExecutiveSummary), null);
        db.BenchmarkReportDocuments.AddRange(perModel, subset, other, standalone);
        await db.SaveChangesAsync(ct);

        var service = Service(db);
        var filtered = await service.ListAsync(new BenchmarkReportDocumentListFilter { ComparisonId = first.Id }, ct);

        Assert.Equal(new[] { subset.Id, perModel.Id }.OrderBy(id => id), filtered.Select(d => d.Id).OrderBy(id => id));

        var model = filtered.Single(d => d.Id == perModel.Id);
        Assert.Equal(BenchmarkReportScope.Model, model.Scope);
        Assert.Equal(first.Id, model.ComparisonId);
        Assert.Equal(ComparisonName, model.ComparisonName);
        Assert.False(model.CoversAllEntries);
        Assert.Equal(new string('5', 64), model.CoveredSetKey);
        var subject = Assert.Single(model.CoveredModels);
        Assert.Equal(("run:12", "GPT-5.6 Luna", "OpenAI"), (subject.EntryKey, subject.Label, subject.Provider));

        var covered = filtered.Single(d => d.Id == subset.Id);
        Assert.Equal(BenchmarkReportScope.Comparison, covered.Scope);
        Assert.False(covered.CoversAllEntries);
        Assert.Equal(new (string, string, string?)[] { ("run:12", "GPT-5.6 Luna", "OpenAI"), ("run:14", "Grok 5", "xAI") },
            covered.CoveredModels.Select(m => (m.EntryKey, m.Label, (string?)m.Provider)).ToArray());

        var all = await service.ListAsync(new BenchmarkReportDocumentListFilter(), ct);
        Assert.Equal(4, all.Count);
        var legacy = all.Single(d => d.Id == standalone.Id);
        Assert.Null(legacy.ComparisonId);
        Assert.Null(legacy.ComparisonName);
        Assert.Equal(BenchmarkReportScope.Model, legacy.Scope);
        Assert.Equal("run:12", Assert.Single(legacy.CoveredModels).EntryKey);
        Assert.Equal("Another comparison", all.Single(d => d.Id == other.Id).ComparisonName);
    }

    [Fact]
    public async Task TheComparisonName_IsReadNow_InTheListTheDetailAndTheRenderedMarkdown()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = new ApplicationDbContext(BenchmarkRunExamTests.InMemoryOptions());
        var comparison = NewComparison();
        db.BenchmarkComparisons.Add(comparison);
        await db.SaveChangesAsync(ct);
        var document = Stored(BenchmarkReportPackFixture.Document(BenchmarkReportAudience.ExecutiveSummary), comparison.Id);
        db.BenchmarkReportDocuments.Add(document);
        await db.SaveChangesAsync(ct);

        comparison.Name = "Luna against the field";
        comparison.RenamedAtUtc = BenchmarkReportPackFixture.CreatedAt;
        await db.SaveChangesAsync(ct);

        var service = Service(db);
        Assert.Equal("Luna against the field", Assert.Single(await service.ListAsync(new BenchmarkReportDocumentListFilter(), ct)).ComparisonName);

        var detail = await service.GetAsync(document.Id, ct);
        Assert.NotNull(detail);
        Assert.Equal("Luna against the field", detail!.ComparisonName);
        Assert.Equal(comparison.Id, detail.ComparisonId);

        var (markdown, stored, notFound, refusal) = await service.RenderWithDocumentAsync(
            document.Id, Options(BenchmarkReportPeerNaming.Named), ct);
        Assert.False(notFound);
        Assert.Null(refusal);
        Assert.NotNull(stored!.Comparison);
        Assert.Contains("**Comparison:** Comparison #" + comparison.Id + " — Luna against the field · 3 models", markdown, StringComparison.Ordinal);
        Assert.StartsWith("comparison-" + comparison.Id + "_gpt-5.6-luna_executive-summary_",
            BenchmarkPdfFileNames.ForReportDocument(stored, Options(BenchmarkReportPeerNaming.Named)), StringComparison.Ordinal);
        Assert.Equal("Comparison #" + comparison.Id + " — Luna against the field",
            BenchmarkPdfDocumentInfo.ForReportDocument(stored, Options(BenchmarkReportPeerNaming.Named), BenchmarkPdfPaper.A4).HeaderText);
    }
}
