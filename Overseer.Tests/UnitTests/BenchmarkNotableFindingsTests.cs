namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using Overseer.Models;
using Overseer.Services.Benchmarking;
using Xunit;
using static Overseer.Tests.UnitTests.BenchmarkReportPackFixture;

/// <summary>
/// The notable findings of a comparison-scope sheet: each rule just in and just out of its threshold,
/// the sentence code writes for it, the caps of two of a kind and ten in all, the priority order, and
/// the facts the comparison sheet carries for them.
/// </summary>
public class BenchmarkNotableFindingsTests
{
    /// <summary>A model with an Intelligence Index and a 95% interval two points either side of it.</summary>
    private static BenchmarkNotableModel Model(
        string letter, double quality, double? cost = null, double? timeMs = null, string provider = "Northwind", string modelId = "model",
        string? thinkingLevel = null, double halfWidth = 2)
        => new()
        {
            Letter = letter,
            Provider = provider,
            ModelId = modelId + "-" + letter.ToLowerInvariant(),
            ThinkingLevel = thinkingLevel,
            Quality = quality,
            QualityLower = quality - halfWidth,
            QualityUpper = quality + halfWidth,
            CostPerQuestion = cost,
            ModelTimeP50Ms = timeMs
        };

    private static BenchmarkReportQuestion Row(int number, params (string Letter, double Score)[] cells) => new()
    {
        Number = number,
        Models = cells.Select(c => new BenchmarkReportQuestionModelScore { Letter = c.Letter, Score = c.Score }).ToList()
    };

    private static List<BenchmarkReportNotableFinding> Detect(
        IReadOnlyList<BenchmarkNotableModel> models,
        IReadOnlyList<BenchmarkReportQuestion>? questions = null,
        IReadOnlyList<BenchmarkReportPairedFamily>? families = null,
        string? reference = null,
        bool baseline = false,
        IReadOnlyList<(string, string)>? judge = null)
        => BenchmarkNotableFindings.Detect(new BenchmarkNotableFindingsInput
        {
            Models = models,
            Questions = questions ?? Array.Empty<BenchmarkReportQuestion>(),
            Families = families ?? Array.Empty<BenchmarkReportPairedFamily>(),
            ReferenceLetter = reference,
            ReferenceIsBaselineModel = baseline,
            JudgeDependentPairs = judge ?? Array.Empty<(string, string)>()
        });

    private static IEnumerable<BenchmarkReportNotableFinding> OfKind(IEnumerable<BenchmarkReportNotableFinding> findings, string kind)
        => findings.Where(f => f.Kind == kind);

    [Fact]
    public void CheaperAndBetter_NeedsAnEstablishedOrder_AndAtMostHalfTheCost()
    {
        var found = Assert.Single(Detect(new[] { Model("A", 80, cost: 0.01), Model("B", 60, cost: 0.02) }));

        Assert.Equal(BenchmarkNotableFindings.CheaperAndBetter, found.Kind);
        Assert.Equal(new[] { "A", "B" }, found.Models);
        Assert.Equal("Model A scored higher than Model B (established by 95% intervals that do not overlap) at 0.50 times its cost per question.", found.Summary);
        Assert.Contains("pair.A.B.intervalOverlap", found.Facts);
        Assert.Equal(1, found.Number);

        // Just over half the cost, or intervals that overlap with no paired verdict, is not notable.
        Assert.Empty(Detect(new[] { Model("A", 80, cost: 0.0101), Model("B", 60, cost: 0.02) }));
        Assert.Empty(Detect(new[] { Model("A", 80, cost: 0.01, halfWidth: 12), Model("B", 60, cost: 0.02, halfWidth: 12) }));
    }

    [Fact]
    public void AnOrderEstablishedByAPairedTest_Counts_EvenWhereTheIntervalsOverlap()
    {
        var family = new BenchmarkReportPairedFamily
        {
            Mode = "Reference",
            ReferenceLetter = "A",
            Measures =
            {
                new BenchmarkReportPairedMeasure
                {
                    Measure = BenchmarkPairedTests.IntelligenceMeasure,
                    Pairs = { new BenchmarkReportPairedTest { FirstLetter = "A", SecondLetter = "B", Established = true, Favors = "First", AdjustedPValue = 0.01 } }
                }
            }
        };

        var found = Assert.Single(Detect(new[] { Model("A", 80, timeMs: 4000, halfWidth: 12), Model("B", 70, timeMs: 9000, halfWidth: 12) }, families: new[] { family }));

        Assert.Equal(BenchmarkNotableFindings.FasterAndBetter, found.Kind);
        Assert.Contains("established by a paired test after its adjustment", found.Summary);
        Assert.Contains("0.44 times its median answer time", found.Summary);
        Assert.Contains("pair.A.B.quality.reference", found.Facts);
    }

    [Fact]
    public void AThinkingLevelInversion_IsTheLowerLevelAtLeastAsGood()
    {
        BenchmarkNotableModel SameModel(string letter, string level, double cost, double timeMs) => new()
        {
            Letter = letter, Provider = "Northwind", ModelId = "same-model", ThinkingLevel = level,
            Quality = 80, QualityLower = 75, QualityUpper = 85, CostPerQuestion = cost, ModelTimeP50Ms = timeMs
        };
        var models = new[] { SameModel("A", "low", 0.01, 5000), SameModel("B", "high", 0.04, 10000) };

        var found = Assert.Single(OfKind(Detect(models), BenchmarkNotableFindings.ThinkingLevelInversion));

        Assert.Equal("Model A, at thinking level low, has an Intelligence Index of 80, at least that of Model B, the same model at thinking level high (80); "
            + "the order between them is not established. Model A took 0.50 times as long and cost 0.25 times as much per question.", found.Summary);

        // The lower level just below the higher one, or levels that cannot be ordered, are not notable.
        var lower = models.Select(m => m.Letter == "A" ? Copy(m, quality: 79.9) : m).ToArray();
        Assert.Empty(OfKind(Detect(lower), BenchmarkNotableFindings.ThinkingLevelInversion));
        var budget = models.Select(m => m.Letter == "B" ? Copy(m, thinkingLevel: "8000") : m).ToArray();
        Assert.Empty(OfKind(Detect(budget), BenchmarkNotableFindings.ThinkingLevelInversion));
    }

    /// <summary>The same model at a thinking level, with an Intelligence Index and its 95% interval.</summary>
    private static BenchmarkNotableModel Leveled(string letter, string level, double quality, double lower, double upper) => new()
    {
        Letter = letter, Provider = "Northwind", ModelId = "same-model", ThinkingLevel = level,
        Quality = quality, QualityLower = lower, QualityUpper = upper
    };

    [Fact]
    public void AThinkingLevelInversion_IsNotReported_WhereAPairedTestEstablishesTheHigherLevelAbove()
    {
        BenchmarkReportPairedFamily Verdict(string favors) => new()
        {
            Mode = "Reference",
            ReferenceLetter = "A",
            Measures =
            {
                new BenchmarkReportPairedMeasure
                {
                    Measure = BenchmarkPairedTests.IntelligenceMeasure,
                    Pairs = { new BenchmarkReportPairedTest { FirstLetter = "A", SecondLetter = "B", Established = true, Favors = favors, AdjustedPValue = 0.01 } }
                }
            }
        };
        var models = new[] { Leveled("A", "low", 80, 75, 85), Leveled("B", "high", 80, 75, 85) };

        Assert.Empty(OfKind(Detect(models, families: new[] { Verdict("Second") }), BenchmarkNotableFindings.ThinkingLevelInversion));

        // A paired test establishing the lower level above the higher still reports the inversion.
        var found = Assert.Single(OfKind(Detect(models, families: new[] { Verdict("First") }), BenchmarkNotableFindings.ThinkingLevelInversion));
        Assert.Contains("the order is established by a paired test after its adjustment", found.Summary);
    }

    [Fact]
    public void AThinkingLevelInversion_IsNotReported_WhereTheIntervalsEstablishTheHigherLevelAbove()
    {
        // The intervals need not center on the index: the higher level's lies wholly above the lower's.
        var separated = new[] { Leveled("A", "low", 80, 70, 81), Leveled("B", "high", 80, 82, 90) };
        Assert.Empty(OfKind(Detect(separated), BenchmarkNotableFindings.ThinkingLevelInversion));

        // Intervals that touch establish nothing.
        var touching = new[] { Leveled("A", "low", 80, 70, 81), Leveled("B", "high", 80, 81, 90) };
        var found = Assert.Single(OfKind(Detect(touching), BenchmarkNotableFindings.ThinkingLevelInversion));
        Assert.Contains("the order between them is not established", found.Summary);
    }

    private static BenchmarkNotableModel Copy(BenchmarkNotableModel m, double? quality = null, string? thinkingLevel = null) => new()
    {
        Letter = m.Letter, Provider = m.Provider, ModelId = m.ModelId, ThinkingLevel = thinkingLevel ?? m.ThinkingLevel,
        Quality = quality ?? m.Quality, QualityLower = m.QualityLower, QualityUpper = m.QualityUpper,
        CostPerQuestion = m.CostPerQuestion, ModelTimeP50Ms = m.ModelTimeP50Ms
    };

    [Fact]
    public void ThinkingLevels_AreOrderedByName_OrByBudget_NeverOneAgainstTheOther()
    {
        Assert.True(BenchmarkNotableFindings.CompareLevels("low", "high") < 0);
        Assert.True(BenchmarkNotableFindings.CompareLevels("xhigh", "medium") > 0);
        Assert.Equal(0, BenchmarkNotableFindings.CompareLevels("High", "high"));
        Assert.True(BenchmarkNotableFindings.CompareLevels("1024", "8000") < 0);
        Assert.Null(BenchmarkNotableFindings.CompareLevels("low", "8000"));
        Assert.Null(BenchmarkNotableFindings.CompareLevels(null, "high"));
    }

    private static readonly BenchmarkNotableModel[] ThreeModels = { Model("A", 85), Model("B", 75, halfWidth: 20), Model("C", 70, halfWidth: 20) };

    [Fact]
    public void ALeadersMiss_IsFiftyOrLessWhereTheOthersMedianIsSeventyFiveOrMore()
    {
        var found = Assert.Single(Detect(ThreeModels, new[] { Row(3, ("A", 50), ("B", 80), ("C", 75)) }));
        Assert.Equal(BenchmarkNotableFindings.LeaderMiss, found.Kind);
        Assert.Equal(new[] { 3 }, found.Questions);
        Assert.Equal("Model A, the highest Intelligence Index, scored 50 on Q3, where the other models' median was 78.", found.Summary);

        Assert.Empty(Detect(ThreeModels, new[] { Row(3, ("A", 51), ("B", 80), ("C", 75)) }));
        Assert.Empty(Detect(ThreeModels, new[] { Row(3, ("A", 40), ("B", 80), ("C", 69)) }));

        // The reference's miss names its role; a battery question is named by its reference.
        var battery = Row(3, ("A", 90), ("B", 90), ("C", 20));
        battery.Reference = "S2-Q1";
        var miss = Assert.Single(Detect(ThreeModels, new[] { battery }, reference: "C", baseline: true));
        Assert.Equal("Model C, the baseline model, scored 20 on S2-Q1, where the other models' median was 90.", miss.Summary);
    }

    [Fact]
    public void AnUnderdogsWin_IsFifteenPointsAboveEveryOtherModel()
    {
        var found = Assert.Single(Detect(ThreeModels, new[] { Row(4, ("A", 60), ("B", 75), ("C", 90)) }));
        Assert.Equal(BenchmarkNotableFindings.UnderdogWin, found.Kind);
        Assert.Equal("Model C, the lowest Intelligence Index, scored 90 on Q4, at least 15 points above every other model (the next best scored 75).", found.Summary);

        Assert.Empty(Detect(ThreeModels, new[] { Row(4, ("A", 60), ("B", 75), ("C", 89)) }));
    }

    [Fact]
    public void ASharedFailure_IsEveryModelBelowFifty_ThreeQuestionsAFinding()
    {
        var questions = new[]
        {
            Row(1, ("A", 40), ("B", 30), ("C", 20)),
            Row(2, ("A", 49), ("B", 10), ("C", 10)),
            Row(3, ("A", 50), ("B", 10), ("C", 10)),
            Row(4, ("A", 30), ("B", 30), ("C", 30)),
            Row(5, ("A", 45), ("B", 45), ("C", 45)),
            Row(6, ("A", 35), ("B", 35), ("C", 35))
        };

        var shared = OfKind(Detect(ThreeModels, questions), BenchmarkNotableFindings.SharedFailure).ToList();

        Assert.Equal(2, shared.Count);
        Assert.Equal(new[] { 1, 4, 6 }, shared[0].Questions);
        Assert.Equal("Every model scored below 50 on Q1, Q4 and Q6: a likely chat, corpus or question problem rather than one model's.", shared[0].Summary);
        Assert.Equal(new[] { 2, 5 }, shared[1].Questions);
        Assert.Equal(new[] { "A", "B", "C" }, shared[0].Models);
    }

    [Fact]
    public void ADifficultyInversion_IsTenPointsHigherOnTheAdvancedBand_EachWithThreeQuestions()
    {
        BenchmarkNotableModel Banded(double advanced, int questions) => new()
        {
            Letter = "A", Quality = 80, SimpleBandScore = 70, SimpleBandQuestions = questions, AdvancedBandScore = advanced, AdvancedBandQuestions = questions
        };

        var found = Assert.Single(Detect(new[] { Banded(80, 3), Model("B", 70) }));
        Assert.Equal(BenchmarkNotableFindings.DifficultyInversion, found.Kind);
        Assert.Equal("Model A scored 80 on advanced questions and 70 on simple ones, 10 points higher on the harder band.", found.Summary);

        Assert.Empty(Detect(new[] { Banded(79.4, 3), Model("B", 70) }));
        Assert.Empty(Detect(new[] { Banded(90, 2), Model("B", 70) }));
    }

    [Fact]
    public void CriticalErrors_AreConcentrated_FromTwo_AtTwiceTheOthersMedianRate()
    {
        BenchmarkNotableModel Errors(string letter, int count) => new() { Letter = letter, Quality = 80, CriticalErrors = count, CountingAnswers = 10 };

        var found = Assert.Single(Detect(new[] { Errors("A", 2), Errors("B", 1), Errors("C", 1) }));
        Assert.Equal(BenchmarkNotableFindings.CriticalErrorConcentration, found.Kind);
        Assert.Equal("Model A made 2 critical errors in 10 answers, 2.00 times the other models' median rate.", found.Summary);

        Assert.Empty(Detect(new[] { Errors("A", 2), Errors("B", 2), Errors("C", 1) }));
        Assert.Empty(Detect(new[] { Errors("A", 1), Errors("B", 0), Errors("C", 0) }));
        Assert.EndsWith("while the other models' median rate is zero.",
            Assert.Single(Detect(new[] { Errors("A", 2), Errors("B", 0), Errors("C", 0) })).Summary);
    }

    [Fact]
    public void PanelDisagreements_AreConcentrated_FromThree()
    {
        BenchmarkNotableModel Disagreements(string letter, int count) => new() { Letter = letter, Quality = 80, PanelDisagreements = count, PanelAnswers = 10 };

        var found = Assert.Single(Detect(new[] { Disagreements("A", 3), Disagreements("B", 1), Disagreements("C", 1) }));
        Assert.Equal(BenchmarkNotableFindings.GraderDisagreementConcentration, found.Kind);
        Assert.Contains("so its Intelligence Index is less certain", found.Summary);

        Assert.Empty(Detect(new[] { Disagreements("A", 2), Disagreements("B", 0), Disagreements("C", 0) }));
    }

    [Fact]
    public void AJudgeDependentPair_IsNotable_InLetterOrder()
    {
        var found = Assert.Single(Detect(ThreeModels, judge: new[] { ("C", "A") }));

        Assert.Equal(BenchmarkNotableFindings.JudgeDependentOrder, found.Kind);
        Assert.Equal(new[] { "A", "C" }, found.Models);
        Assert.StartsWith("Which of Model A and Model C scored higher depends on which panel member grades", found.Summary);
    }

    [Fact]
    public void OutputTrouble_IsTruncationAndToolLimitsOnTenPercentOfAnswers()
    {
        BenchmarkNotableModel Output(int answers) => new() { Letter = "A", Quality = 80, Answers = answers, Truncated = 1, ToolLimitHits = 1 };

        var found = Assert.Single(Detect(new[] { Output(20), Model("B", 70) }));
        Assert.Equal("Model A had 1 truncated answer and 1 tool-limit hit in 20 answers (10%).", found.Summary);

        Assert.Empty(Detect(new[] { Output(21), Model("B", 70) }));
    }

    private static BenchmarkNotableModel Tools(string letter, double calls) => new() { Letter = letter, Quality = 80, ToolCallsPerQuestion = calls };
    private static BenchmarkNotableModel Length(string letter, double chars) => new() { Letter = letter, Quality = 80, MedianAnswerChars = chars };

    [Fact]
    public void ToolUse_AndAnswerLength_AreOutliersAtHalfOrTwiceTheMedianOfAllModels()
    {
        var tools = Assert.Single(Detect(new[] { Tools("A", 1.0), Tools("B", 2.0), Tools("C", 2.0) }));
        Assert.Equal("Model A made 1.0 tool calls per question, at most half the median of all 3 models (2.0).", tools.Summary);
        Assert.Empty(Detect(new[] { Tools("A", 1.01), Tools("B", 2.0), Tools("C", 2.0) }));
        Assert.Empty(Detect(new[] { Tools("A", 0.0), Tools("B", 0.5), Tools("C", 0.5) }));

        var length = Assert.Single(Detect(new[] { Length("A", 2000), Length("B", 1000), Length("C", 1000) }));
        Assert.Equal("Model A's median answer is 2,000 characters, at least twice the median of all 3 models (1,000).", length.Summary);
        Assert.Empty(Detect(new[] { Length("A", 1999), Length("B", 1000), Length("C", 1000) }));
    }

    [Fact]
    public void AnOutlier_NeedsThreeModelsWithTheFigure()
    {
        Assert.Empty(Detect(new[] { Length("A", 2000), Length("B", 1000) }));
        Assert.Empty(Detect(new[] { Tools("A", 4.0), Tools("B", 1.0) }));

        // A model without the figure does not count toward the three.
        Assert.Empty(Detect(new[] { Length("A", 2000), Length("B", 1000), Model("C", 70) }));
    }

    [Fact]
    public void AnOutlier_IsMeasuredAgainstTheMedianOfAllModels_ItsOwnValueIncluded()
    {
        var found = Assert.Single(Detect(new[] { Length("A", 1000), Length("B", 1000), Length("C", 4000) }));

        Assert.Equal(BenchmarkNotableFindings.AnswerLengthOutlier, found.Kind);
        Assert.Equal(new[] { "C" }, found.Models);
        Assert.Equal("Model C's median answer is 4,000 characters, at least twice the median of all 3 models (1,000).", found.Summary);
    }

    [Fact]
    public void ToolUse_IsNoOutlier_WhereTheMedianOfAllModelsIsAtMostHalfACall()
    {
        // Model A's 0.2 is under half the other two models' median of 0.85, but the median of all three is 0.5.
        Assert.Empty(Detect(new[] { Tools("A", 0.2), Tools("B", 0.5), Tools("C", 1.2) }));
        Assert.Empty(Detect(new[] { Tools("A", 0.0), Tools("B", 0.0), Tools("C", 3.0) }));

        Assert.Equal(new[] { "A" }, Assert.Single(Detect(new[] { Tools("A", 0.2), Tools("B", 0.6), Tools("C", 1.1) })).Models);
    }

    [Fact]
    public void ProviderErrors_AreNotable_FromTwoPercentAndFiveTimesTheOthersMedian()
    {
        BenchmarkNotableModel Errors(string letter, double rate) => new() { Letter = letter, Quality = 80, ProviderErrorRate = rate };

        var found = Assert.Single(Detect(new[] { Errors("A", 0.03), Errors("B", 0.005), Errors("C", 0.005) }));
        Assert.Equal(BenchmarkNotableFindings.ProviderErrors, found.Kind);
        Assert.StartsWith("Model A met provider errors on ", found.Summary);

        Assert.Empty(Detect(new[] { Errors("A", 0.019), Errors("B", 0.0), Errors("C", 0.0) }));
        Assert.Empty(Detect(new[] { Errors("A", 0.024), Errors("B", 0.005), Errors("C", 0.005) }));
    }

    [Fact]
    public void AtMostTwoOfAKind_AndTenInAll_InPriorityOrder()
    {
        // Six models whose intervals never overlap; A is the cheapest and the fastest.
        var models = new[] { "A", "B", "C", "D", "E", "F" }
            .Select((letter, i) => Model(letter, 90 - 10 * i, cost: letter == "A" ? 0.01 : 0.05, timeMs: letter == "A" ? 1000 : 5000))
            .ToArray();
        var letters = models.Select(m => m.Letter).ToArray();
        BenchmarkReportQuestion Same(int number, double score, string? letter = null, double other = 0)
            => Row(number, letters.Select(l => (l, l == letter ? score : other == 0 ? score : other)).ToArray());

        var questions = new List<BenchmarkReportQuestion>
        {
            Same(1, 10, "A", 90), Same(2, 10, "A", 90),       // leader misses
            Same(3, 100, "F", 60), Same(4, 100, "F", 60)      // underdog wins
        };
        for (int n = 5; n <= 11; n++) questions.Add(Same(n, 40));   // shared failures

        var findings = Detect(models, questions);

        Assert.Equal(BenchmarkNotableFindings.MaxFindings, findings.Count);
        Assert.Equal(Enumerable.Range(1, 10), findings.Select(f => f.Number));
        Assert.Equal(
            new[]
            {
                BenchmarkNotableFindings.CheaperAndBetter, BenchmarkNotableFindings.CheaperAndBetter,
                BenchmarkNotableFindings.FasterAndBetter, BenchmarkNotableFindings.FasterAndBetter,
                BenchmarkNotableFindings.LeaderMiss, BenchmarkNotableFindings.LeaderMiss,
                BenchmarkNotableFindings.UnderdogWin, BenchmarkNotableFindings.UnderdogWin,
                BenchmarkNotableFindings.SharedFailure, BenchmarkNotableFindings.SharedFailure
            },
            findings.Select(f => f.Kind));
        Assert.Equal(new[] { "A", "B" }, findings[0].Models);
        Assert.Equal(new[] { "A", "C" }, findings[1].Models);
    }

    [Fact]
    public void TheComparisonSheet_StatesItsNotableFindingsAsFacts()
    {
        var sheet = ComparisonFacts().Sheet!;
        var findings = sheet.NotableFindings!;

        Assert.NotEmpty(findings);
        var count = Assert.Single(sheet.Facts, f => f.Key == "notable.count");
        Assert.True(BenchmarkReportPackPromptIsCount(count, findings.Count));
        Assert.Equal(findings.Count, BenchmarkNotableFindings.CountOf(sheet));
        foreach (var finding in findings)
        {
            string prefix = "notable." + finding.Number + ".";
            Assert.Equal(finding.Summary, Assert.Single(sheet.Facts, f => f.Key == prefix + "summary").Display);
            Assert.Equal(BenchmarkNotableFindings.KindText(finding.Kind), Assert.Single(sheet.Facts, f => f.Key == prefix + "kind").Display);
            Assert.Contains(sheet.Facts, f => f.Key == prefix + "models");
            Assert.Contains(sheet.Facts, f => f.Key == prefix + "questions");
            Assert.All(finding.Facts, key => Assert.Contains(sheet.Facts, f => f.Key == key));
            Assert.All(ComparisonModels, m => Assert.DoesNotContain(m.Label, finding.Summary, StringComparison.OrdinalIgnoreCase));
        }

        // Model B costs half of Model D's and its interval lies wholly above D's.
        Assert.Contains(findings, f => f.Kind == BenchmarkNotableFindings.CheaperAndBetter && f.Models.SequenceEqual(new[] { "B", "D" }));
    }

    private static bool BenchmarkReportPackPromptIsCount(BenchmarkReportFact fact, int count)
        => fact.Available && fact.Value is System.Text.Json.Nodes.JsonValue value && value.TryGetValue(out int stored) && stored == count;

    [Fact]
    public void ASheetWithNothingNotable_CountsNone()
    {
        // Vega Pro and Lyra Mini: Vega Pro is better and faster but not at half the time, and costs more.
        var sheet = ComparisonFacts(new[] { "run:32", "run:33" }).Sheet!;

        Assert.Empty(sheet.NotableFindings!);
        Assert.Equal("none detected", Assert.Single(sheet.Facts, f => f.Key == "notable.count").Display);
        Assert.Equal(0, BenchmarkNotableFindings.CountOf(sheet));
        Assert.DoesNotContain(sheet.Facts, f => f.Key.StartsWith("notable.1.", StringComparison.Ordinal));
    }
}
