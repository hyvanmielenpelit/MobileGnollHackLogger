namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services;
using Overseer.Services.Benchmarking;
using Xunit;

/// <summary>
/// Paired tests: the Holm families, the two-entry family, the reference default, the log-ratio speed
/// and cost tests, the not-tested reasons, and the two pins that keep the Intelligence rows equal to
/// the methods they reuse — Multi-Run Analysis's <i>Compare with group</i> and the battery M7.
/// </summary>
public class BenchmarkPairedTestsTests
{
    private const long SuiteId = 41;

    private static readonly DateTime ComputedAt = new(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>$2 / M input, $10 / M output, $0.20 / M cache read.</summary>
    private static ModelPricing Card() => new(2m, 10m, 0.20m);

    /// <summary>A fully keyed completed run of one suite, one answer per score, every question at difficulty 50.</summary>
    private static BenchmarkRun Run(
        long id,
        int[] scores,
        string modelId = "gpt-5.6-luna",
        int[]? accuracy = null,
        long[]? durations = null,
        int[]? inputTokens = null,
        int[]? outputTokens = null)
    {
        var run = BenchmarkBatteryTestData.Run(id, SuiteId, scores, Enumerable.Repeat(50, scores.Length).ToArray(), modelId);

        for (int q = 0; q < scores.Length; q++)
        {
            var answer = run.Answers[q];
            if (accuracy != null) answer.AccuracyScore = accuracy[q];
            if (durations != null) answer.DurationMs = durations[q];
            if (inputTokens != null) answer.InputTokens = inputTokens[q];
            if (outputTokens != null) answer.OutputTokens = outputTokens[q];
        }

        return run;
    }

    private static readonly int[] SixScores = { 60, 70, 80, 65, 75, 85 };

    private static BenchmarkPairedEntry Entry(string key, params BenchmarkRun[] runs)
        => BenchmarkPairedTests.FromRuns(key, key, runs, runs.ToDictionary(r => r.Id, _ => (ModelPricing?)Card()));

    private static BenchmarkPairedMeasureDto Measure(IEnumerable<BenchmarkPairedMeasureDto> measures, string name)
        => measures.Single(m => m.Measure == name);

    private static BenchmarkPairedTestDto Test(double? p, string direction = BenchmarkPairedTests.DirectionHigher)
        => new() { PValue = p, Direction = direction, EffectKind = BenchmarkPairedTests.EffectDifference };

    // --- Holm ---------------------------------------------------------------------------------------------

    [Fact]
    public void Holm_MatchesThePublishedWorkedExample()
    {
        // The Holm–Bonferroni worked example (Wikipedia, "Holm–Bonferroni method"): four hypotheses with
        // p = 0.01, 0.04, 0.03, 0.005 at α = 0.05. Sorted, 0.005·4 = 0.02 and 0.01·3 = 0.03 reject; 0.03·2
        // = 0.06 does not, so H1 and H4 are rejected and H2, H3 are not. Adjusted: 0.03, 0.06, 0.06, 0.02.
        var tests = new[] { Test(0.01), Test(0.04), Test(0.03), Test(0.005) };

        BenchmarkPairedTests.AdjustFamily(BenchmarkPairedTests.IntelligenceMeasure, tests);

        Assert.Equal(new[] { 0.03, 0.06, 0.06, 0.02 }, tests.Select(t => Math.Round(t.AdjustedPValue!.Value, 12)));
        Assert.Equal(new[] { true, false, false, true }, tests.Select(t => t.Established));
        Assert.Equal("Higher on the same questions", tests[0].Verdict);
        Assert.Equal(BenchmarkPairedTests.NoDifferenceVerdict, tests[1].Verdict);
    }

    [Fact]
    public void Holm_OnTies_IsMonotoneAndOrderFree()
    {
        // Sorted 0.02, 0.02, 0.04: 0.02·3 = 0.06, then max(0.06, 0.02·2) = 0.06, then max(0.06, 0.04·1) = 0.06.
        var three = new[] { Test(0.02), Test(0.04), Test(0.02) };
        BenchmarkPairedTests.AdjustFamily(BenchmarkPairedTests.IntelligenceMeasure, three);
        Assert.All(three, t => Assert.Equal(0.06, t.AdjustedPValue!.Value, 12));
        Assert.All(three, t => Assert.False(t.Established));

        // Two tied at 0.01: both 0.02, both established.
        var two = new[] { Test(0.01, BenchmarkPairedTests.DirectionLower), Test(0.01) };
        BenchmarkPairedTests.AdjustFamily(BenchmarkPairedTests.SpeedMeasure, two);
        Assert.All(two, t => Assert.Equal(0.02, t.AdjustedPValue!.Value, 12));
        Assert.Equal("Faster on the same questions", two[0].Verdict);
        Assert.Equal("Slower on the same questions", two[1].Verdict);
    }

    [Fact]
    public void Holm_LeavesUntestedPairsOutOfTheFamily()
    {
        // A pair with no p (every difference zero) and a pair not tested are not members: the family is
        // the remaining two, so 0.02 is adjusted by 2, not by 4.
        var untested = Test(0.001);
        untested.NotTestedReason = "Not tested.";
        var tests = new[] { Test(0.02), Test(null), untested, Test(0.3) };

        BenchmarkPairedTests.AdjustFamily(BenchmarkPairedTests.CostMeasure, tests);

        Assert.Equal(0.04, tests[0].AdjustedPValue!.Value, 12);
        Assert.Null(tests[1].AdjustedPValue);
        Assert.Equal(BenchmarkPairedTests.NoDifferenceVerdict, tests[1].Verdict);
        Assert.Null(tests[2].AdjustedPValue);
        Assert.Equal(BenchmarkPairedTests.NotTestedVerdict, tests[2].Verdict);
        Assert.Equal(0.3, tests[3].AdjustedPValue!.Value, 12);
        Assert.Equal("More expensive on the same questions", tests[0].Verdict);
    }

    [Fact]
    public void Verdicts_NeverSayEqual()
    {
        foreach (string measure in new[] { BenchmarkPairedTests.IntelligenceMeasure, BenchmarkPairedTests.SpeedMeasure, BenchmarkPairedTests.CostMeasure })
        {
            foreach (string direction in new[] { BenchmarkPairedTests.DirectionHigher, BenchmarkPairedTests.DirectionLower, BenchmarkPairedTests.DirectionNone })
            {
                foreach (bool established in new[] { true, false })
                {
                    Assert.DoesNotContain("equal", BenchmarkPairedTests.Verdict(measure, established, direction), StringComparison.OrdinalIgnoreCase);
                }
            }
        }

        Assert.Equal("Cheaper on the same questions", BenchmarkPairedTests.Verdict(BenchmarkPairedTests.CostMeasure, true, BenchmarkPairedTests.DirectionLower));
        Assert.Equal("Lower on the same questions", BenchmarkPairedTests.Verdict("Accuracy", true, BenchmarkPairedTests.DirectionLower));
    }

    // --- Families -----------------------------------------------------------------------------------------

    [Fact]
    public void TwoEntries_AreOneTestPerMeasure_AdjustedPEqualsRawP()
    {
        var a = Entry("run:1", Run(1, SixScores, accuracy: new[] { 6, 7, 8, 5, 7, 8 }));
        var b = Entry("run:2", Run(2, new[] { 75, 82, 88, 80, 79, 95 }, "claude-opus-5", accuracy: new[] { 8, 8, 9, 7, 8, 9 }));

        var dto = BenchmarkPairedTests.Build(new[] { a, b }, BenchmarkModelComparisonSubjectKinds.Runs,
            BenchmarkModelComparisonPricingBasis.Current, BenchmarkPairedComparisonMode.AllPairs, "run:1", ComputedAt);

        Assert.Equal(new[] { "Intelligence", "Accuracy", "Completeness", "Conciseness", "Readability", "Speed", "Cost" },
            dto.Measures.Select(m => m.Measure));
        Assert.True(dto.Measures[0].Primary);
        Assert.Equal(BenchmarkPairedTests.MeasuresNote, dto.MeasuresNote);
        Assert.Equal(BenchmarkPairedTests.SingleRunCaveat, dto.SingleRunCaveat);

        foreach (var measure in dto.Measures)
        {
            var pair = Assert.Single(measure.Pairs);
            Assert.Equal("run:1", pair.BaselineKey);
            Assert.Equal("run:2", pair.TreatmentKey);
            Assert.Equal(BenchmarkPairedTests.AdjustmentNone, measure.Adjustment);
            Assert.Equal(pair.PValue, pair.AdjustedPValue);
        }

        var intelligence = Measure(dto.Measures, "Intelligence");
        Assert.Equal(1, intelligence.FamilySize);
        Assert.Equal(BenchmarkPairedTests.SingleComparisonNote, intelligence.AdjustmentNote);

        // Every question scored higher: six positive differences, the exact two-sided p is 2/64.
        var row = intelligence.Pairs[0];
        Assert.Equal(6, row.PairedItems);
        Assert.Equal(2.0 / 64.0, row.PValue!.Value, 12);
        Assert.True(row.Established);
        Assert.Equal("Higher on the same questions", row.Verdict);
        Assert.Equal(BenchmarkPairedTests.EffectDifference, row.EffectKind);

        Assert.Equal(1, Measure(dto.Measures, "Accuracy").FamilySize);
        Assert.NotNull(Measure(dto.Measures, "Completeness").NotTestedReason);
    }

    [Fact]
    public void TheReference_DefaultsToTheHighestIntelligenceIndex_AndEveryOtherEntryIsTestedAgainstIt()
    {
        var low = Entry("run:1", Run(1, new[] { 40, 45, 50, 55, 60, 65 }));
        var high = Entry("run:2", Run(2, new[] { 80, 85, 90, 95, 92, 88 }, "claude-opus-5"));
        var mid = Entry("run:3", Run(3, new[] { 60, 65, 72, 70, 75, 80 }, "gemini-3.8-flash"));
        var entries = new[] { low, high, mid };

        string reference = BenchmarkPairedTests.DefaultReferenceKey(entries);
        Assert.Equal("run:2", reference);

        var dto = BenchmarkPairedTests.Build(entries, BenchmarkModelComparisonSubjectKinds.Runs,
            BenchmarkModelComparisonPricingBasis.Current, BenchmarkPairedComparisonMode.Reference, reference, ComputedAt);

        var intelligence = Measure(dto.Measures, "Intelligence");
        Assert.Equal(2, intelligence.Pairs.Count);
        Assert.All(intelligence.Pairs, p => Assert.Equal("run:2", p.BaselineKey));
        Assert.Equal(new[] { "run:1", "run:3" }, intelligence.Pairs.Select(p => p.TreatmentKey));
        Assert.Equal(2, intelligence.FamilySize);
        Assert.Equal(BenchmarkPairedTests.AdjustmentHolm, intelligence.Adjustment);
        Assert.Equal("Holm-adjusted across 2 tests", intelligence.AdjustmentNote);

        var holm = BenchmarkBatteryStatistics.HolmAdjust(intelligence.Pairs.Select(p => p.PValue!.Value).ToList());
        Assert.Equal(holm, intelligence.Pairs.Select(p => p.AdjustedPValue!.Value));
        Assert.All(intelligence.Pairs, p => Assert.Equal(BenchmarkPairedTests.DirectionLower, p.Direction));
    }

    [Fact]
    public void ATiedIndex_DefaultsToTheEarlierEntry()
    {
        var first = Entry("run:1", Run(1, SixScores));
        var second = Entry("run:2", Run(2, SixScores, "claude-opus-5"));

        Assert.Equal("run:1", BenchmarkPairedTests.DefaultReferenceKey(new[] { first, second }));
    }

    [Fact]
    public void AllPairs_TestsEveryPairOnce()
    {
        var entries = new[]
        {
            Entry("run:1", Run(1, new[] { 40, 45, 50, 55, 60, 65 })),
            Entry("run:2", Run(2, new[] { 80, 85, 90, 95, 92, 88 }, "claude-opus-5")),
            Entry("run:3", Run(3, new[] { 60, 65, 72, 70, 75, 80 }, "gemini-3.8-flash"))
        };

        var dto = BenchmarkPairedTests.Build(entries, BenchmarkModelComparisonSubjectKinds.Runs,
            BenchmarkModelComparisonPricingBasis.Current, BenchmarkPairedComparisonMode.AllPairs, "run:2", ComputedAt);

        var intelligence = Measure(dto.Measures, "Intelligence");
        Assert.Equal(new[] { ("run:1", "run:2"), ("run:1", "run:3"), ("run:2", "run:3") },
            intelligence.Pairs.Select(p => (p.BaselineKey, p.TreatmentKey)));
        Assert.Equal(3, intelligence.FamilySize);
        Assert.Equal("AllPairs", dto.Mode);
        Assert.Equal("run:2", dto.ReferenceKey);
    }

    [Fact]
    public void AllPairs_IsCappedAtTwelveComparableEntries()
    {
        Assert.Null(BenchmarkPairedTests.Validate(12, BenchmarkPairedComparisonMode.AllPairs));
        Assert.Equal(BenchmarkPairedTests.AllPairsLimitError(13), BenchmarkPairedTests.Validate(13, BenchmarkPairedComparisonMode.AllPairs));
        Assert.Null(BenchmarkPairedTests.Validate(13, BenchmarkPairedComparisonMode.Reference));
        Assert.Equal(BenchmarkPairedTests.NeedsTwoEntriesError, BenchmarkPairedTests.Validate(1, BenchmarkPairedComparisonMode.Reference));
        Assert.Equal("Comparing needs two comparable entries.", BenchmarkPairedTests.NeedsTwoEntriesError);
    }

    [Fact]
    public void TheResult_IsDeterministic()
    {
        BenchmarkPairedComparisonDto Build() => BenchmarkPairedTests.Build(
            new[]
            {
                Entry("run:1", Run(1, SixScores, durations: new long[] { 10000, 12000, 14000, 16000, 18000, 20000 })),
                Entry("run:2", Run(2, new[] { 75, 82, 88, 80, 79, 95 }, "claude-opus-5")),
                Entry("run:3", Run(3, new[] { 60, 65, 72, 70, 75, 80 }, "gemini-3.8-flash"))
            },
            BenchmarkModelComparisonSubjectKinds.Runs, BenchmarkModelComparisonPricingBasis.Current,
            BenchmarkPairedComparisonMode.AllPairs, "run:1", ComputedAt);

        Assert.Equal(JsonSerializer.Serialize(Build()), JsonSerializer.Serialize(Build()));
    }

    // --- Intelligence equals the methods it reuses ---------------------------------------------------------

    [Fact]
    public void TheIntelligenceRow_EqualsCompareWithGroup_ForTheSameTwoGroups()
    {
        var groupA = new[] { Run(1, SixScores), Run(2, new[] { 62, 68, 84, 61, 70, 90 }) };
        var groupB = new[]
        {
            Run(3, new[] { 70, 75, 78, 72, 80, 88 }, "claude-opus-5"),
            Run(4, new[] { 66, 79, 85, 70, 74, 91 }, "claude-opus-5")
        };

        var examA = BenchmarkRunExam.Build(groupA);
        var examB = BenchmarkRunExam.Build(groupB);
        var expected = BenchmarkGroupStatistics.Compare(
            BenchmarkGroupStatistics.Compute(examA.Suite, examA.Questions, groupA),
            BenchmarkGroupStatistics.Compute(examB.Suite, examB.Questions, groupB));

        var measures = BenchmarkPairedTests.ComparePair(Entry("group:1", groupA), Entry("group:2", groupB));
        var row = Assert.Single(Measure(measures, "Intelligence").Pairs);

        Assert.Equal(expected.PairedItemCount, row.PairedItems);
        Assert.Equal(expected.UnpairedItemCount, row.UnpairedItems);
        Assert.Equal(expected.RevisionMismatchedItemCount, row.RevisionMismatched);
        Assert.Equal(expected.MeanDifference, row.Effect!.Value, 12);
        Assert.Equal(expected.DifferenceConfidenceLower!.Value, row.EffectLower!.Value, 12);
        Assert.Equal(expected.DifferenceConfidenceUpper!.Value, row.EffectUpper!.Value, 12);
        Assert.Equal(expected.CohensDz!.Value, row.Dz!.Value, 12);
        Assert.Equal(expected.Wilcoxon.PValue!.Value, row.PValue!.Value, 12);
        Assert.Equal(row.PValue, row.AdjustedPValue);
    }

    private static BenchmarkBatterySuiteInput SuiteInput(int suiteIndex, BenchmarkRun run)
    {
        var runs = new[] { run };
        var exam = BenchmarkRunExam.Build(runs);
        var statistics = BenchmarkGroupStatistics.Compute(exam.Suite, exam.Questions, runs);
        var answers = run.Answers.Where(a => a.Status == BenchmarkAnswerStatus.Ok).ToList();

        return new BenchmarkBatterySuiteInput(
            suiteIndex,
            statistics,
            BenchmarkBatteryDefinition.SuiteMass(exam.Questions, statistics),
            new Dictionary<long, int> { [run.Id] = 1 },
            answers.Select(a => (double)a.ModelTimeMs).ToList(),
            answers.Where(a => a.TimeToFirstTokenMs.HasValue).Select(a => (double)a.TimeToFirstTokenMs!.Value).ToList(),
            1,
            run.Answers.Count,
            Array.Empty<BenchmarkBatteryExcludedMember>());
    }

    /// <summary>A two-suite battery result of eleven questions each, one run per suite.</summary>
    private static (BenchmarkBatteryStatisticsResult Result, BenchmarkRun[] Runs) Battery(long firstRunId, int[] scoresA, int[] scoresB, string modelId)
    {
        var definition = new BenchmarkBatteryDefinition(1, "Paired battery", 1, BenchmarkBatteryWeightingScheme.DifficultyMass, new[]
        {
            new BenchmarkBatteryDefinitionSuite(0, 51, "Suite 51", null),
            new BenchmarkBatteryDefinitionSuite(1, 52, "Suite 52", null)
        });

        var runA = BenchmarkBatteryTestData.Run(firstRunId, 51, scoresA, Enumerable.Repeat(50, scoresA.Length).ToArray(), modelId);
        var runB = BenchmarkBatteryTestData.Run(firstRunId + 1, 52, scoresB, Enumerable.Repeat(50, scoresB.Length).ToArray(), modelId);

        return (BenchmarkBatteryStatistics.Compute(definition, new[] { SuiteInput(0, runA), SuiteInput(1, runB) }), new[] { runA, runB });
    }

    private static BenchmarkPairedEntry BatteryEntry(string key, (BenchmarkBatteryStatisticsResult Result, BenchmarkRun[] Runs) battery)
        => BenchmarkPairedTests.FromBattery(key, key, battery.Result, new[]
        {
            new BenchmarkPairedSuiteRuns(0, "Suite 51", new[] { battery.Runs[0] }),
            new BenchmarkPairedSuiteRuns(1, "Suite 52", new[] { battery.Runs[1] })
        });

    [Fact]
    public void ABatteryPairsIntelligenceRow_IsM7_WithTheFixedSeed()
    {
        // 22 paired items exceed the exact limit of 20, so M7 resamples from its fixed seed.
        int[] flat = Enumerable.Repeat(50, 11).ToArray();
        int[] treatA = new[] { 12, -3, 7, 0, 15, -9, 4, 6, -2, 10, 3 }.Select(x => 50 + x).ToArray();
        int[] treatB = new[] { -4, 8, 11, -6, 2, 9, 0, 5, -1, 13, 7 }.Select(x => 50 + x).ToArray();

        var baseline = Battery(7001, flat, flat, "gpt-5.6-luna");
        var treatment = Battery(8001, treatA, treatB, "claude-opus-5");

        var expected = BenchmarkBatteryStatistics.Compare(baseline.Result, treatment.Result);
        Assert.Equal(BenchmarkBatteryRandomizationMethod.MonteCarlo, expected.RandomizationMethod);

        var first = BenchmarkPairedTests.ComparePair(BatteryEntry("battery:1", baseline), BatteryEntry("battery:2", treatment));
        var second = BenchmarkPairedTests.ComparePair(BatteryEntry("battery:1", baseline), BatteryEntry("battery:2", treatment));

        var row = Assert.Single(Measure(first, "Intelligence").Pairs);
        Assert.Equal(expected.CompositeDifference!.Value, row.Effect!.Value, 12);
        Assert.Equal(expected.CompositeConfidenceLower!.Value, row.EffectLower!.Value, 12);
        Assert.Equal(expected.CompositeConfidenceUpper!.Value, row.EffectUpper!.Value, 12);
        Assert.Equal(expected.RandomizationPValue!.Value, row.PValue!.Value, 12);
        Assert.Equal(22, row.PairedItems);
        Assert.Null(row.Dz);
        Assert.Contains("seed " + BenchmarkBatteryStatistics.DefaultSeed, row.Method);
        Assert.Equal(expected.Suites.Select(s => s.HolmAdjustedPValue), row.Suites!.Select(s => s.HolmAdjustedPValue));

        Assert.Equal(row.PValue, Measure(second, "Intelligence").Pairs[0].PValue);

        // The dimension rows pool (suite, question) pairs and say so.
        Assert.Equal(BenchmarkPairedTests.BatteryDimensionCaption, Measure(first, "Accuracy").Caption);
        Assert.Equal(BenchmarkPairedTests.BatterySingleRunCaveat,
            BenchmarkPairedTests.SingleRunCaveatOf(new[] { BatteryEntry("battery:1", baseline) }));
    }

    [Fact]
    public async Task ABatteryPairsIntelligenceRow_EqualsThePersistedM7Comparison()
    {
        using var db = BenchmarkBatteryTestData.NewDb();
        var analysis = BenchmarkBatteryTestData.Service(db);

        long luna = await BenchmarkBatteryTestData.SeedAsync(db, BenchmarkBatteryTestData.Definition(),
            (BenchmarkBatteryTestData.SuiteARun(1), 0, 1), (BenchmarkBatteryTestData.SuiteBRun(2), 1, 1));
        Assert.Null((await analysis.AnalyseAsync(luna, null, null, Ct)).Error);

        long opus = await BenchmarkBatteryTestData.SeedAsync(db, BenchmarkBatteryTestData.Definition(),
            (BenchmarkBatteryTestData.SuiteARun(3, "claude-opus-5", shift: 10), 0, 1),
            (BenchmarkBatteryTestData.SuiteBRun(4, "claude-opus-5", shift: 5), 1, 1));
        var (_, _, persisted, error) = await analysis.AnalyseAsync(opus, null, luna, Ct);
        Assert.True(persisted != null, error);

        var service = new BenchmarkPairedTestsService(db, new BenchmarkModelComparisonService(db));
        var (result, refusal, notFound) = await service.CompareBatteriesAsync(new BenchmarkBatteryPairedComparisonRequest
        {
            BatteryRunId = opus,
            BaselineBatteryRunId = luna,
            PricingBasis = BenchmarkModelComparisonPricingBasis.AsRun
        }, Ct);

        Assert.True(result != null, refusal);
        Assert.False(notFound);
        Assert.Equal("ModelComparison", result!.Kind);
        Assert.Equal("Battery", result.SubjectKind);
        Assert.Contains(BenchmarkComparabilityKey.CandidateModelKey, result.ChangedKeys);

        var row = Assert.Single(Measure(result.Measures, "Intelligence").Pairs);
        Assert.Equal(persisted!.CompositeDifference!.Value, row.Effect!.Value, 12);
        Assert.Equal(persisted.RandomizationPValue!.Value, row.PValue!.Value, 12);
        Assert.Equal(5, row.PairedItems);

        // No pricing service, so no price card: the cost row says why rather than approximating.
        Assert.Contains("No candidate price card", Measure(result.Measures, "Cost").NotTestedReason);
    }

    // --- Speed and cost ------------------------------------------------------------------------------------

    [Fact]
    public void Speed_IsAWilcoxonOnTheLogRatio_WithTheGeometricMeanRatioAsTheEffect()
    {
        long[] baseTimes = { 10000, 12000, 14000, 16000, 18000, 20000 };
        long[] treatTimes = { 5000, 6600, 7000, 8800, 9000, 10400 };

        var measures = BenchmarkPairedTests.ComparePair(
            Entry("run:1", Run(1, SixScores, durations: baseTimes)),
            Entry("run:2", Run(2, SixScores, "claude-opus-5", durations: treatTimes)));

        var speed = Measure(measures, "Speed");
        var row = Assert.Single(speed.Pairs);

        var logs = treatTimes.Zip(baseTimes, (t, b) => Math.Log((double)t / b)).ToList();
        double mean = logs.Average();
        double half = BenchmarkGroupStatistics.StudentTCritical95(5) * BenchmarkGroupStatistics.SampleStandardDeviation(logs)!.Value / Math.Sqrt(6);

        Assert.Equal(BenchmarkPairedTests.EffectRatio, row.EffectKind);
        Assert.Equal(Math.Exp(mean), row.Effect!.Value, 12);
        Assert.Equal(Math.Exp(mean - half), row.EffectLower!.Value, 12);
        Assert.Equal(Math.Exp(mean + half), row.EffectUpper!.Value, 12);
        Assert.Equal(BenchmarkGroupStatistics.WilcoxonSignedRank(logs).PValue!.Value, row.PValue!.Value, 12);
        Assert.Equal(BenchmarkGroupStatistics.CohensDz(logs)!.Value, row.Dz!.Value, 12);

        // Every question faster: the exact two-sided p is 2/64.
        Assert.Equal(2.0 / 64.0, row.PValue!.Value, 12);
        Assert.Equal(BenchmarkPairedTests.DirectionLower, row.Direction);
        Assert.Equal("Faster on the same questions", row.Verdict);
        Assert.Equal(BenchmarkPairedTests.RatioCaption, speed.Caption);
    }

    [Fact]
    public void Cost_IsAWilcoxonOnTheLogRatio_OfPerAnswerCandidateCost()
    {
        int[] baseInput = { 100000, 110000, 90000, 120000, 100000, 95000 };
        int[] baseOutput = { 10000, 12000, 9000, 11000, 10000, 9500 };
        int[] treatInput = { 210000, 205000, 190000, 260000, 230000, 180000 };
        int[] treatOutput = { 20000, 25000, 21000, 20000, 22000, 19000 };

        var measures = BenchmarkPairedTests.ComparePair(
            Entry("run:1", Run(1, SixScores, inputTokens: baseInput, outputTokens: baseOutput)),
            Entry("run:2", Run(2, SixScores, "claude-opus-5", inputTokens: treatInput, outputTokens: treatOutput)));

        var row = Assert.Single(Measure(measures, "Cost").Pairs);

        double Cost(int input, int output) => (double)ModelPricingService.ComputeCostFromTotals(Card(), input, output, 0, 0);
        var logs = Enumerable.Range(0, 6)
            .Select(q => Math.Log(Cost(treatInput[q], treatOutput[q]) / Cost(baseInput[q], baseOutput[q])))
            .ToList();

        Assert.Equal(Math.Exp(logs.Average()), row.Effect!.Value, 12);
        Assert.Equal(BenchmarkGroupStatistics.WilcoxonSignedRank(logs).PValue!.Value, row.PValue!.Value, 12);
        Assert.Equal(BenchmarkPairedTests.DirectionHigher, row.Direction);
        Assert.Equal("More expensive on the same questions", row.Verdict);
    }

    [Fact]
    public void Cost_WithoutPerAnswerTokenCounts_IsNotTested_RatherThanApproximated()
    {
        var measures = BenchmarkPairedTests.ComparePair(
            Entry("run:1", Run(1, SixScores)),
            Entry("run:2", Run(2, SixScores, "claude-opus-5")));

        var cost = Measure(measures, "Cost");
        Assert.Contains("no per-answer token counts", cost.NotTestedReason);
        Assert.Equal(0, cost.FamilySize);
        Assert.Null(cost.Pairs[0].PValue);
        Assert.Equal(BenchmarkPairedTests.NotTestedVerdict, cost.Pairs[0].Verdict);
    }

    [Fact]
    public void DegradedAxes_AreNotTested()
    {
        var a = Entry("run:1", Run(1, SixScores, durations: new long[] { 10000, 12000, 14000, 16000, 18000, 20000 },
            inputTokens: Enumerable.Repeat(1000, 6).ToArray(), outputTokens: Enumerable.Repeat(100, 6).ToArray())) with
        {
            SpeedDegraded = true,
            SpeedDegradedReason = BenchmarkComparabilityKey.QuestionParallelismKey,
            CostDegraded = true,
            CostDegradedReason = BenchmarkComparabilityKey.PricingSnapshotKey
        };
        var b = Entry("run:2", Run(2, SixScores, "claude-opus-5",
            inputTokens: Enumerable.Repeat(2000, 6).ToArray(), outputTokens: Enumerable.Repeat(200, 6).ToArray()));

        var measures = BenchmarkPairedTests.ComparePair(a, b);

        var speed = Measure(measures, "Speed");
        Assert.Contains("speed axis is degraded", speed.NotTestedReason);
        Assert.Contains(BenchmarkComparabilityKey.QuestionParallelismKey, speed.NotTestedReason);
        Assert.Equal(0, speed.FamilySize);
        Assert.Null(speed.Pairs[0].Effect);

        var cost = Measure(measures, "Cost");
        Assert.Contains("cost axis is degraded", cost.NotTestedReason);

        // Quality is untouched by a degrading key.
        Assert.Null(Measure(measures, "Intelligence").NotTestedReason);
    }

    [Fact]
    public void FewerThanTheMinimumPairs_AreNotTested()
    {
        Assert.Equal(BenchmarkReportFacts.PairedMinimumQuestions, BenchmarkPairedTests.MinimumPairedItems);

        var measures = BenchmarkPairedTests.ComparePair(
            Entry("run:1", Run(1, new[] { 60, 70, 80, 90 })),
            Entry("run:2", Run(2, new[] { 70, 80, 90, 95 }, "claude-opus-5")));

        var intelligence = Measure(measures, "Intelligence");
        Assert.Equal("Only 4 question(s) are paired; a paired test needs at least 5.", intelligence.NotTestedReason);
        Assert.Equal(4, intelligence.Pairs[0].PairedItems);
        Assert.Null(intelligence.Pairs[0].PValue);
        Assert.Equal(0, intelligence.FamilySize);
    }

    [Fact]
    public void IdenticalTimings_HaveNoPValue_AndEstablishNothing()
    {
        // Same durations on both sides: every log ratio is zero, so Wilcoxon has nothing to rank.
        var measures = BenchmarkPairedTests.ComparePair(
            Entry("run:1", Run(1, SixScores)),
            Entry("run:2", Run(2, SixScores, "claude-opus-5")));

        var row = Measure(measures, "Speed").Pairs[0];
        Assert.Null(row.NotTestedReason);
        Assert.Null(row.PValue);
        Assert.Equal(1.0, row.Effect!.Value, 12);
        Assert.Equal(BenchmarkPairedTests.NoDifferenceVerdict, row.Verdict);
        Assert.Equal(0, Measure(measures, "Speed").FamilySize);
    }

    // --- The service's refusals ---------------------------------------------------------------------------

    [Fact]
    public async Task AMixedRequest_IsRefused()
    {
        using var db = BenchmarkBatteryTestData.NewDb();
        var service = new BenchmarkPairedTestsService(db, new BenchmarkModelComparisonService(db));

        var (result, error) = await service.CompareAsync(new BenchmarkPairedComparisonRequest
        {
            RunIds = { 1 },
            BatteryRunIds = { 2 }
        }, Ct);

        Assert.Null(result);
        Assert.Equal(BenchmarkBatteryModelComparison.MixedSourcesError, error);
    }

    [Fact]
    public async Task OneSource_IsRefused_WithTheTwoEntriesText()
    {
        using var db = BenchmarkBatteryTestData.NewDb();
        var service = new BenchmarkPairedTestsService(db, new BenchmarkModelComparisonService(db));

        var (result, error) = await service.CompareAsync(new BenchmarkPairedComparisonRequest { RunIds = { 1 } }, Ct);

        Assert.Null(result);
        Assert.Equal("Comparing needs two comparable entries.", error);
    }
}
