namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services.Benchmarking;
using Xunit;
using static Overseer.Tests.UnitTests.BenchmarkReportPackFixture;

/// <summary>
/// The comparison-scope fact sheet: letters across the covered set, joint ranks, the Pareto
/// frontiers, the paired families over the covered models, the per-question matrix and its excerpt
/// budget, a subset whose other models appear nowhere, and the bounds of two and twelve models.
/// </summary>
public class BenchmarkComparisonReportFactsTests
{
    private static string? Display(BenchmarkReportFactSheet sheet, string key)
        => sheet.Facts.SingleOrDefault(f => f.Key == key)?.Display;

    private static BenchmarkReportFact Fact(BenchmarkReportFactSheet sheet, string key)
        => Assert.Single(sheet.Facts, f => f.Key == key);

    [Fact]
    public void Letters_FollowTheIntelligenceIndex_TiesByEntryKey()
    {
        var sheet = ComparisonFacts().Sheet!;

        Assert.True(sheet.IsComparison);
        Assert.Equal(
            new[] { ("A", "run:31"), ("B", "run:32"), ("C", "run:33"), ("D", "run:34"), ("E", "run:35") },
            sheet.Peers.Select(p => (p.Letter, p.EntryKey)));
        Assert.Equal(sheet.Peers.Select(p => (p.Letter, p.EntryKey, p.Label)), sheet.Models!.Select(m => (m.Letter, m.EntryKey, m.Label)));
        Assert.All(sheet.Entries, e => Assert.False(e.IsSubject));
        Assert.Equal(new[] { "A", "B", "C", "D", "E" }, sheet.Entries.Select(e => e.PeerLetter));
    }

    [Fact]
    public void TheWholeComparison_StoresItsCoverageAndSubjectKey()
    {
        var built = ComparisonFacts();
        var sheet = built.Sheet!;

        Assert.True(sheet.CoversAllEntries);
        Assert.Equal(5, sheet.ComparisonEntryCount);
        Assert.Equal("comparison:12", sheet.SubjectKey);
        Assert.Equal("Comparison #12", sheet.SubjectLabel);
        Assert.Equal(BenchmarkComparisonReportFacts.SubjectKind, sheet.SubjectKind);
        Assert.Equal(new[] { "run:31", "run:32", "run:33", "run:34", "run:35" }, built.CoveredEntryKeys);
        Assert.Equal(BenchmarkReportComparisonKey.ForCoveredSet(built.CoveredEntryKeys), built.CoveredSetKey);

        // A set covering every entry has the comparison's own key.
        Assert.Equal(ComparisonRow().ComparisonKey, built.CoveredSetKey);

        // The root keys the list and the covers read.
        var stored = BenchmarkReportRenderService.ReadFacts(BenchmarkReportJson.Serialize(sheet));
        Assert.True(stored.CoversAllEntries);
        Assert.Equal(5, stored.ComparisonEntryCount);
        Assert.Equal("Orion Max", stored.EntryLabels["run:31"].Label);
        Assert.Equal("Northwind", stored.EntryLabels["run:31"].Provider);
        Assert.Equal("E", stored.PeerLetters["run:35"]);
        Assert.Equal(5, stored.PeerLetters.Count);
    }

    [Fact]
    public void Ranks_AreJointWhereIntervalsOverlap()
    {
        var sheet = ComparisonFacts().Sheet!;

        Assert.Equal("joint 1st of 5 (intervals overlap)", Display(sheet, "model.A.quality.rank"));
        Assert.Equal("joint 1st of 5 (intervals overlap)", Display(sheet, "model.B.quality.rank"));
        Assert.Equal("joint 3rd of 5 (intervals overlap)", Display(sheet, "model.C.quality.rank"));
        Assert.Equal("joint 3rd of 5 (intervals overlap)", Display(sheet, "model.D.quality.rank"));
        Assert.Equal("5th of 5", Display(sheet, "model.E.quality.rank"));
        Assert.Equal("2nd of 5", Display(sheet, "model.B.speed.rank"));
        Assert.Equal("1st of 5", Display(sheet, "model.E.cost.rank"));

        Assert.True(BenchmarkReportPackPromptIsTrue(Fact(sheet, "pair.A.B.intervalOverlap")));
        Assert.False(BenchmarkReportPackPromptIsTrue(Fact(sheet, "pair.A.E.intervalOverlap")));
    }

    [Fact]
    public void ASpreadOfOne_ReadsOnePointApart()
    {
        var values = new List<(string Letter, double? Value)> { ("A", 85.2), ("B", 83.8) };

        Assert.Equal("from 84 (Model B) to 85 (Model A), 1 point apart",
            BenchmarkComparisonReportFacts.SpreadDisplay(values, BenchmarkReportFormat.Whole, higherIsBetter: true, unit: " points"));
    }

    [Fact]
    public void ASpreadWhoseValuesDisplayAlike_ReadsForEveryModel()
    {
        var values = new List<(string Letter, double? Value)> { ("A", 97.2), ("B", 96.8), ("C", 97.0) };

        Assert.Equal("97 for every model",
            BenchmarkComparisonReportFacts.SpreadDisplay(values, BenchmarkReportFormat.Whole, higherIsBetter: true, unit: " points"));
        Assert.Equal("97 for every model with this figure",
            BenchmarkComparisonReportFacts.SpreadDisplay(values.Append(("D", (double?)null)).ToList(), BenchmarkReportFormat.Whole, higherIsBetter: true, unit: " points"));
    }

    private static bool BenchmarkReportPackPromptIsTrue(BenchmarkReportFact fact) => BenchmarkReportPackPrompt.IsTrue(fact);

    [Fact]
    public void TheFrontiers_HoldTheModelsNoOtherDominates()
    {
        var sheet = ComparisonFacts().Sheet!;

        // Nova Lite (D) is beaten on intelligence and cost by Lyra Mini (C), and is the fastest model.
        Assert.Equal("Models A, B, C and E", Display(sheet, "frontier.qualityCost"));
        Assert.Equal("Models A, B and D", Display(sheet, "frontier.qualitySpeed"));
        Assert.Equal("Models B, C, D and E", Display(sheet, "frontier.speedCost"));
        Assert.Equal("on the intelligence against speed and speed against cost frontiers", Display(sheet, "model.D.frontier"));
        Assert.Equal("on the intelligence against cost and intelligence against speed frontiers", Display(sheet, "model.A.frontier"));
        Assert.Equal("from 55 (Model E) to 85 (Model A), 30 points apart", Display(sheet, "spread.quality"));
    }

    [Fact]
    public void PerModelFacts_AreCopiedUnderTheLetter_WithoutNamesOrPeerComparisons()
    {
        var sheet = ComparisonFacts().Sheet!;

        Assert.Equal("85 / 100", Display(sheet, "model.A.quality.index"));
        Assert.Equal("1", Display(sheet, "model.A.runs"));
        Assert.Equal("1 of 6 answers", Display(sheet, "model.C.errors.critical"));
        Assert.DoesNotContain(sheet.Facts, f => f.Key.StartsWith("peer.", StringComparison.Ordinal));
        Assert.DoesNotContain(sheet.Facts, f => f.Key.EndsWith(".peerMean", StringComparison.Ordinal));
        Assert.DoesNotContain(sheet.Facts, f => f.Key.Contains(".label", StringComparison.Ordinal) || f.Key.Contains(".provider", StringComparison.Ordinal)
            || f.Key.EndsWith(".modelId", StringComparison.Ordinal));
        Assert.All(ComparisonModels, m => Assert.DoesNotContain(sheet.Facts, f => f.Display.Contains(m.Label, StringComparison.Ordinal)
            || f.Display.Contains(m.Provider, StringComparison.Ordinal)));
    }

    [Fact]
    public void ThePairedFamilies_AreOverTheCoveredModels_WithTheReferenceLetteredA()
    {
        var sheet = ComparisonFacts().Sheet!;
        var families = sheet.PairedTests!;

        var reference = Assert.Single(families, f => f.Mode == "Reference");
        Assert.Equal("A", reference.ReferenceLetter);
        var intelligence = Assert.Single(reference.Measures, m => m.Measure == BenchmarkPairedTests.IntelligenceMeasure);
        Assert.Equal(4, intelligence.Pairs.Count);
        Assert.All(intelligence.Pairs, p => Assert.Equal("A", p.FirstLetter));
        Assert.True(intelligence.FamilySize <= 4);

        // Five covered models: all pairs as well, ten per measure.
        var allPairs = Assert.Single(families, f => f.Mode == "AllPairs");
        Assert.Equal(10, Assert.Single(allPairs.Measures, m => m.Measure == BenchmarkPairedTests.IntelligenceMeasure).Pairs.Count);

        Assert.Contains(sheet.Facts, f => f.Key == "pair.A.E.quality.reference");
        Assert.Contains(sheet.Facts, f => f.Key == "pair.C.D.quality.allPairs");
        Assert.DoesNotContain(sheet.Facts, f => f.Key == "pair.C.D.quality.reference");
    }

    [Fact]
    public void APairedDifference_IsTheEarlierLetterMinusTheLater()
    {
        var sheet = ComparisonFacts().Sheet!;
        var difference = Fact(sheet, "pair.A.E.quality.difference");

        Assert.True(difference.Available, difference.UnavailableReason);
        Assert.StartsWith("+", difference.Display);
        Assert.EndsWith("points (Model A minus Model E)", difference.Display);
    }

    [Fact]
    public void TheMatrix_HoldsEveryCoveredModelPerQuestion_AndTheExcerptsFollowThePriority()
    {
        var built = ComparisonFacts();
        var sheet = built.Sheet!;

        Assert.Equal(6, sheet.Questions.Count);
        Assert.All(sheet.Questions, q => Assert.Equal(new[] { "A", "B", "C", "D", "E" }, q.Models!.Select(c => c.Letter)));
        var q3 = sheet.Questions.Single(q => q.Number == 3);
        Assert.True(q3.CriticalError);
        Assert.True(q3.Models!.Single(c => c.Letter == "C").CriticalError);
        Assert.Equal(25.0, q3.PeerMin);
        Assert.Equal(70.0, q3.PeerMax);

        // Q3, with the critical error and the refuted claim, comes first in the excerpt order.
        Assert.Equal(3, BenchmarkReportContent.ExcerptPriority(sheet.Questions)[0].Number);

        // One budget of 120 characters a question for all models, never one per model.
        var content = built.Content!;
        Assert.Equal(6, content.Questions!.Count);
        int excerpted = content.Runs.SelectMany(r => r.Questions).Sum(q => q.AnswerExcerpt.Length);
        Assert.True(excerpted <= 120 * 6);
        Assert.Contains(content.Runs, r => r.Letter == "C" && r.Questions.Any(q => q.Number == 3));
        Assert.All(content.Runs, r => Assert.NotNull(r.EntryKey));
        Assert.Equal(q3.ExcerptLetters, content.Runs.Where(r => r.Questions.Any(q => q.Number == 3)).Select(r => r.Letter!).ToList());
        Assert.All(sheet.Questions, q => Assert.True(q.Detailed));
    }

    [Fact]
    public void ASubsetOfTwoOfFive_LettersItsOwnModels_AndTheOthersAppearNowhere()
    {
        var built = ComparisonFacts(SubsetKeys);
        var sheet = built.Sheet!;

        Assert.False(sheet.CoversAllEntries);
        Assert.Equal(5, sheet.ComparisonEntryCount);
        Assert.Equal(new[] { ("A", "run:31"), ("B", "run:35") }, sheet.Peers.Select(p => (p.Letter, p.EntryKey)));
        Assert.Equal("comparison:12/" + built.CoveredSetKey![..16], sheet.SubjectKey);
        Assert.Equal("Comparison #12 · 2 of 5 models", sheet.SubjectLabel);
        Assert.NotEqual(ComparisonRow().ComparisonKey, built.CoveredSetKey);
        Assert.Equal(new long[] { 31, 35 }, sheet.SubjectRunIds);

        // Two covered models: one reference test and no all-pairs family.
        var family = Assert.Single(sheet.PairedTests!);
        Assert.Equal("Reference", family.Mode);
        Assert.Single(Assert.Single(family.Measures, m => m.Measure == BenchmarkPairedTests.IntelligenceMeasure).Pairs);
        Assert.Equal("1st of 2", Display(sheet, "model.A.quality.rank"));

        string facts = BenchmarkReportJson.Serialize(sheet);
        string content = BenchmarkReportJson.Serialize(built.Content);
        foreach (var other in ComparisonModels.Where(m => m.RunId is 32 or 33 or 34))
        {
            Assert.DoesNotContain(other.Label, facts, StringComparison.Ordinal);
            Assert.DoesNotContain(ModelIdOf(other.Label), facts, StringComparison.Ordinal);
            Assert.DoesNotContain(other.Provider, facts, StringComparison.Ordinal);
            Assert.DoesNotContain("run:" + other.RunId, facts, StringComparison.Ordinal);
            Assert.DoesNotContain("\"runId\":" + other.RunId, content, StringComparison.Ordinal);
        }
        Assert.DoesNotContain(sheet.Facts, f => f.Key.StartsWith("model.C.", StringComparison.Ordinal));
        Assert.All(sheet.Questions, q => Assert.Equal(new[] { "A", "B" }, q.Models!.Select(c => c.Letter)));
    }

    [Fact]
    public void TheBounds_AreTwoAndTwelveModels()
    {
        Assert.Equal(BenchmarkComparisonReportFacts.TooFewRefusal, BenchmarkComparisonReportFacts.BoundsRefusal(1));
        Assert.Null(BenchmarkComparisonReportFacts.BoundsRefusal(2));
        Assert.Null(BenchmarkComparisonReportFacts.BoundsRefusal(12));
        Assert.Equal(BenchmarkComparisonReportFacts.TooManyRefusal, BenchmarkComparisonReportFacts.BoundsRefusal(13));

        var comparison = FiveModelComparison();
        var runs = FiveModelRuns();
        var one = BenchmarkComparisonReportFacts.BuildFromRuns(comparison, new[] { "run:31" }, runs, 120);
        Assert.Null(one.Sheet);
        Assert.Equal(BenchmarkComparisonReportFacts.TooFewRefusal, one.Refusal);

        var two = BenchmarkComparisonReportFacts.BuildFromRuns(comparison, new[] { "run:31", "run:32" }, runs, 120);
        Assert.NotNull(two.Sheet);
    }

    [Fact]
    public void TwelveModels_AreDocumented_AndThirteenAreRefused()
    {
        var entries = new List<BenchmarkModelComparisonEntryDto>();
        var runs = new Dictionary<long, BenchmarkRun>();
        for (int i = 0; i < 13; i++)
        {
            long id = 100 + i;
            runs[id] = Run(id, "Provider" + i, "model-" + i,
                new AnswerSpec(301, 1, 1, 50 + i), new AnswerSpec(302, 2, 1, 60 + i));
            entries.Add(Entry("run:" + id, new[] { id }, "Model " + i, "Provider" + i, 50 + i, 45 + i, 55 + i));
        }
        var comparison = Comparison(entries.ToArray());

        var twelve = BenchmarkComparisonReportFacts.BuildFromRuns(comparison, entries.Take(12).Select(e => e.Key).ToList(), runs, 120);
        Assert.True(twelve.Sheet != null, twelve.Refusal);
        Assert.Equal(12, twelve.Sheet!.Peers.Count);
        Assert.Equal("L", twelve.Sheet.Peers.Last().Letter);
        Assert.False(twelve.Sheet.CoversAllEntries);

        var thirteen = BenchmarkComparisonReportFacts.BuildFromRuns(comparison, entries.Select(e => e.Key).ToList(), runs, 120);
        Assert.Null(thirteen.Sheet);
        Assert.Equal(BenchmarkComparisonReportFacts.TooManyRefusal, thirteen.Refusal);
    }

    [Fact]
    public void ACoveredEntry_ThatIsExcludedOrNotInTheComparison_IsRefused()
    {
        var comparison = FiveModelComparison();
        comparison.Entries.Single(e => e.Key == "run:34").Excluded = true;

        Assert.Contains("is excluded from this comparison", BenchmarkComparisonReportFacts.CoveredRefusal(comparison, new[] { "run:31", "run:34" }));
        Assert.Equal("'run:99' is not an entry of this comparison.", BenchmarkComparisonReportFacts.CoveredRefusal(comparison, new[] { "run:31", "run:99" }));
        Assert.Equal(new[] { "run:31", "run:32", "run:33", "run:35" }, BenchmarkComparisonReportFacts.CoveredKeysOf(comparison, null));
    }

    [Fact]
    public void TheSheetsFreeText_NamesTheModelsByLetter()
    {
        var sheet = ComparisonFacts().Sheet!;

        var texts = sheet.Facts.SelectMany(f => new[] { f.Display, f.UnavailableReason })
            .Concat(sheet.Entries.SelectMany(e => e.Extra).SelectMany(f => new[] { f.Display, f.UnavailableReason }))
            .Concat(sheet.Peers.Select(p => p.Explanation))
            .Concat(sheet.PurposeStatements)
            .Append(sheet.PairedTestsUnavailableReason)
            .Concat((sheet.PairedTests ?? new List<BenchmarkReportPairedFamily>()).SelectMany(f =>
                new[] { f.SingleRunCaveat }
                    .Concat(f.Measures.SelectMany(m => new[] { m.AdjustmentNote, m.NotTestedReason }))
                    .Concat(f.Measures.SelectMany(m => m.Pairs).Select(p => p.NotTestedReason))))
            .Where(t => !string.IsNullOrEmpty(t))
            .ToList();

        foreach (var model in ComparisonModels)
        {
            Assert.All(texts, t => Assert.DoesNotContain(model.Label, t!, StringComparison.OrdinalIgnoreCase));
            Assert.All(texts, t => Assert.DoesNotContain(ModelIdOf(model.Label), t!, StringComparison.OrdinalIgnoreCase));
        }
        Assert.Contains(texts, t => t!.Contains("run #31 of Model A", StringComparison.Ordinal));

        // The names themselves stay where the renderer reads them.
        Assert.Equal("Orion Max", sheet.Peers[0].Label);
        Assert.Equal("Orion Max", sheet.Models![0].Label);
    }

    [Fact]
    public void Lettered_ReplacesWholeNamesInOnePass_AndASharedNameByEveryLetter()
    {
        var names = BenchmarkComparisonReportFacts.NamesOf(new[]
        {
            new BenchmarkReportPeer { Letter = "A", Label = "Orion Max (high)", DisplayName = "Orion Max", ModelId = "orion-max" },
            new BenchmarkReportPeer { Letter = "B", Label = "Orion Max (low)", DisplayName = "Orion Max", ModelId = "orion-max" },
            new BenchmarkReportPeer { Letter = "C", Label = "Model A", DisplayName = "Model A", ModelId = "x" }
        });

        Assert.Equal("Model A and Models A and B; orion-maximal", BenchmarkComparisonReportFacts.Lettered("Orion Max (high) and orion max; orion-maximal", names));
        Assert.Equal("Model C beat Model B", BenchmarkComparisonReportFacts.Lettered("Model A beat Orion Max (low)", names));
        Assert.Null(BenchmarkComparisonReportFacts.Lettered(null, names));
    }
}
