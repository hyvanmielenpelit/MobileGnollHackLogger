namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using MobileGnollHackLogger.Data;
using Overseer.Services.Benchmarking;
using Overseer.Tests.Helpers;
using Xunit;

/// <summary>
/// The battery composite (M2–M7). Fixtures are built by running the real
/// <see cref="BenchmarkGroupStatistics.Compute"/> over synthetic runs, so the identities asserted
/// here hold against the group layer as it is, and expected values are derived in each test.
/// </summary>
public class BenchmarkBatteryStatisticsTests
{
    private const long SuiteA = 11;
    private const long SuiteB = 12;
    private const long SuiteC = 13;

    private static string SuiteName(long suiteId) => $"Suite {suiteId}";

    private static int?[] S(params int[] scores) => scores.Select(s => (int?)s).ToArray();

    private static BenchmarkQuestion[] Questions(long suiteId, params int[] difficulties)
    {
        return difficulties
            .Select((d, i) => new BenchmarkQuestion
            {
                Id = suiteId * 100 + i + 1,
                BenchmarkSuiteId = suiteId,
                OrderIndex = i + 1,
                QuestionText = $"S{suiteId}Q{i + 1}",
                Difficulty = BenchmarkDifficulty.Intermediate,
                AssessedDifficulty = d,
                ItemRevision = 1
            })
            .ToArray();
    }

    private static BenchmarkRun Run(
        long runId,
        long suiteId,
        BenchmarkQuestion[] questions,
        int?[] scores,
        int? speedIndex = 80,
        int?[]? speedScores = null,
        int?[]? accuracy = null,
        bool[]? criticalErrors = null)
    {
        var run = new BenchmarkRun
        {
            Id = runId,
            BenchmarkSuiteId = suiteId,
            SuiteName = SuiteName(suiteId),
            TestedModelSnapshot = BenchmarkModelSnapshots.Model(modelId: "gpt-5.6-luna"),
            AssessorModelSnapshot = BenchmarkModelSnapshots.Model(provider: "Google", modelId: "gemini-3.7-pro"),
            ScoringMethodVersion = 8,
            SpeedIndex = speedIndex,
            QualityIndex = 70
        };

        for (int i = 0; i < questions.Length; i++)
        {
            run.Answers.Add(new BenchmarkRunAnswer
            {
                Id = runId * 1000 + i,
                BenchmarkRunId = runId,
                BenchmarkQuestionId = questions[i].Id,
                ItemRevisionUsed = 1,
                OrderIndex = i + 1,
                QuestionText = questions[i].QuestionText,
                Status = BenchmarkAnswerStatus.Ok,
                AssessmentStatus = scores[i].HasValue ? BenchmarkAssessmentStatus.Scored : BenchmarkAssessmentStatus.Failed,
                QualityScore = scores[i],
                AssessedDifficulty = questions[i].AssessedDifficulty,
                SpeedScore = speedScores?[i],
                AccuracyScore = accuracy?[i],
                CriticalError = criticalErrors?[i] ?? false,
                DurationMs = 20000 + 1000 * i,
                ToolTimeMs = 0,
                TimeToFirstTokenMs = 500 + 10 * i
            });
        }

        return run;
    }

    private static BenchmarkBatterySuiteInput Input(
        int suiteIndex,
        long suiteId,
        BenchmarkQuestion[] questions,
        IReadOnlyList<BenchmarkRun> runs,
        int[]? rounds = null,
        IReadOnlyCollection<BenchmarkGroupRunCost>? costs = null,
        int? expectedQuestionCount = null)
    {
        var suite = new BenchmarkSuite { Id = suiteId, Name = SuiteName(suiteId) };
        var statistics = BenchmarkGroupStatistics.Compute(suite, questions, runs, costs);

        // M2: the item row's weight where there is one, else the exam question's fallback.
        var itemWeights = statistics.Items.ToDictionary(i => i.QuestionId, i => i.Weight);
        double mass = questions.Sum(q => itemWeights.TryGetValue(q.Id, out double w)
            ? w
            : Math.Max(1.0, (double)(q.AssessedDifficulty ?? 50)));

        var roundByRun = runs
            .Select((r, i) => (r.Id, Round: rounds?[i] ?? i + 1))
            .ToDictionary(x => x.Id, x => x.Round);

        var answers = runs.SelectMany(r => r.Answers).Where(a => a.Status == BenchmarkAnswerStatus.Ok).ToList();

        return new BenchmarkBatterySuiteInput(
            suiteIndex,
            statistics,
            new BenchmarkBatterySuiteMass(questions.Length, mass),
            roundByRun,
            answers.Select(a => (double)a.ModelTimeMs).ToList(),
            answers.Where(a => a.TimeToFirstTokenMs.HasValue).Select(a => (double)a.TimeToFirstTokenMs!.Value).ToList(),
            runs.Count,
            expectedQuestionCount ?? questions.Length,
            Array.Empty<BenchmarkBatteryExcludedMember>());
    }

    private static BenchmarkBatteryDefinition Definition(
        BenchmarkBatteryWeightingScheme scheme,
        params (long SuiteId, double? CustomWeight)[] suites)
    {
        return new BenchmarkBatteryDefinition(
            1,
            "Test battery",
            1,
            scheme,
            suites.Select((s, i) => new BenchmarkBatteryDefinitionSuite(i, s.SuiteId, SuiteName(s.SuiteId), s.CustomWeight)).ToList());
    }

    private static BenchmarkBatteryDefinition Definition(BenchmarkBatteryWeightingScheme scheme, params long[] suiteIds)
        => Definition(scheme, suiteIds.Select(id => (id, (double?)null)).ToArray());

    // Two suites of different size and difficulty, used by several tests.
    private static readonly int[] DifficultiesA = { 20, 50, 80 };
    private static readonly int[] DifficultiesB = { 30, 40, 60, 70, 90, 10 };

    private static double Pooled(IReadOnlyList<int> difficulties, IReadOnlyList<double> values)
        => difficulties.Select((d, i) => d * values[i]).Sum() / difficulties.Sum();

    // --- M2: the difficulty-mass identity ---------------------------------------------------------

    [Fact]
    public void DifficultyMassIndex_EqualsThePooledDifficultyWeightedIndex_AtOneRun()
    {
        var qa = Questions(SuiteA, DifficultiesA);
        var qb = Questions(SuiteB, DifficultiesB);
        int[] scoresA = { 60, 70, 85 };
        int[] scoresB = { 50, 55, 65, 72, 45, 90 };

        var result = BenchmarkBatteryStatistics.Compute(
            Definition(BenchmarkBatteryWeightingScheme.DifficultyMass, SuiteA, SuiteB),
            new[]
            {
                Input(0, SuiteA, qa, new[] { Run(1101, SuiteA, qa, S(scoresA)) }),
                Input(1, SuiteB, qb, new[] { Run(1201, SuiteB, qb, S(scoresB)) })
            });

        // Every question of both suites pooled into one difficulty-weighted index.
        var difficulties = DifficultiesA.Concat(DifficultiesB).ToList();
        var scores = scoresA.Concat(scoresB).Select(s => (double)s).ToList();
        double pooled = Pooled(difficulties, scores);

        Assert.True(result.Complete);
        Assert.True(result.PooledIdentityHolds);
        Assert.Equal(pooled, result.OverallIndex!.PointEstimate, 9);
        Assert.Equal(1, result.MethodVersion);
    }

    [Fact]
    public void DifficultyMassIndex_EqualsThePooledDifficultyWeightedIndex_AtThreeRuns()
    {
        var qa = Questions(SuiteA, DifficultiesA);
        var qb = Questions(SuiteB, DifficultiesB);
        int[][] scoresA = { new[] { 60, 70, 85 }, new[] { 65, 60, 90 }, new[] { 70, 75, 80 } };
        int[][] scoresB =
        {
            new[] { 50, 55, 65, 72, 45, 90 },
            new[] { 55, 50, 70, 70, 40, 95 },
            new[] { 45, 60, 60, 75, 50, 85 }
        };

        var result = BenchmarkBatteryStatistics.Compute(
            Definition(BenchmarkBatteryWeightingScheme.DifficultyMass, SuiteA, SuiteB),
            new[]
            {
                Input(0, SuiteA, qa, scoresA.Select((s, r) => Run(1101 + r, SuiteA, qa, S(s))).ToList()),
                Input(1, SuiteB, qb, scoresB.Select((s, r) => Run(1201 + r, SuiteB, qb, S(s))).ToList())
            });

        // Pooled over the per-question cross-run means.
        var difficulties = DifficultiesA.Concat(DifficultiesB).ToList();
        var means = Enumerable.Range(0, 3).Select(q => scoresA.Average(r => r[q]))
            .Concat(Enumerable.Range(0, 6).Select(q => scoresB.Average(r => r[q])))
            .ToList();

        Assert.True(result.PooledIdentityHolds);
        Assert.Equal(Pooled(difficulties, means), result.OverallIndex!.PointEstimate, 9);
    }

    [Fact]
    public void PooledIdentity_IsFalse_WhenAnExamQuestionHasNoScoredAnswer()
    {
        var qa = Questions(SuiteA, DifficultiesA);
        var qb = Questions(SuiteB, DifficultiesB);

        var result = BenchmarkBatteryStatistics.Compute(
            Definition(BenchmarkBatteryWeightingScheme.DifficultyMass, SuiteA, SuiteB),
            new[]
            {
                Input(0, SuiteA, qa, new[] { Run(1101, SuiteA, qa, S(60, 70, 85)) }),
                Input(1, SuiteB, qb, new[] { Run(1201, SuiteB, qb, new int?[] { 50, 55, null, 72, 45, 90 }) })
            });

        Assert.True(result.Complete);
        Assert.False(result.PooledIdentityHolds);
        Assert.Contains(BenchmarkBatteryStatistics.PooledIdentityCaveat, result.Caveats);
        Assert.Equal(5, result.Suites[1].ScoredItemCount);
        Assert.Equal(6, result.Suites[1].ExamItemCount);
    }

    [Fact]
    public void PooledIdentity_IsFalse_WhenTheExamIsShorterThanTheMembersQuestionCount()
    {
        var qa = Questions(SuiteA, DifficultiesA);
        var qb = Questions(SuiteB, DifficultiesB);

        var result = BenchmarkBatteryStatistics.Compute(
            Definition(BenchmarkBatteryWeightingScheme.DifficultyMass, SuiteA, SuiteB),
            new[]
            {
                Input(0, SuiteA, qa, new[] { Run(1101, SuiteA, qa, S(60, 70, 85)) }, expectedQuestionCount: 4),
                Input(1, SuiteB, qb, new[] { Run(1201, SuiteB, qb, S(50, 55, 65, 72, 45, 90)) })
            });

        Assert.False(result.PooledIdentityHolds);
        Assert.True(result.Suites[0].ExamIncomplete);
        Assert.Contains(result.Caveats, c => c.StartsWith("Exam incomplete for suite 'Suite 11': 3 of 4", StringComparison.Ordinal));
    }

    // --- M6: speed --------------------------------------------------------------------------------

    [Fact]
    public void OverallSpeedIndex_UsesQuestionCountWeights_UnderTheDefaultScheme()
    {
        // Suite A: 3 questions at difficulty 20 (mass 60), Speed Index 70.
        // Suite B: 6 questions at difficulty 50 (mass 300), Speed Index 40.
        // Count weights 1/3 and 2/3: 70/3 + 2*40/3 = 150/3 = 50. Difficulty-mass weights (1/6, 5/6)
        // would give 45, which is what the test rules out.
        var qa = Questions(SuiteA, 20, 20, 20);
        var qb = Questions(SuiteB, 50, 50, 50, 50, 50, 50);

        var result = BenchmarkBatteryStatistics.Compute(
            Definition(BenchmarkBatteryWeightingScheme.DifficultyMass, SuiteA, SuiteB),
            new[]
            {
                Input(0, SuiteA, qa, new[] { Run(1101, SuiteA, qa, S(80, 80, 80), speedIndex: 70) }),
                Input(1, SuiteB, qb, new[] { Run(1201, SuiteB, qb, S(60, 60, 60, 60, 60, 60), speedIndex: 40) })
            });

        Assert.Equal(1.0 / 6.0, result.Weights[0], 9);
        Assert.Equal(1.0 / 3.0, result.CountWeights[0], 9);
        Assert.Equal(2.0 / 3.0, result.CountWeights[1], 9);
        Assert.Equal(50.0, result.Speed!.OverallSpeedIndex!.Value, 9);

        // The pooled answer-level percentiles come from the supplied lists: model time is
        // 20000 + 1000 * i ms for the i-th answer of every run.
        var pooledTimes = Enumerable.Range(0, 3).Select(i => 20000.0 + 1000 * i)
            .Concat(Enumerable.Range(0, 6).Select(i => 20000.0 + 1000 * i))
            .ToList();
        Assert.Equal(9, result.Speed.PooledAnswerCount);
        Assert.Equal(BenchmarkGroupStatistics.Percentile(pooledTimes, 50.0), result.Speed.ModelTimeP50Ms);
        Assert.Equal(25000.0, result.Speed.ModelTimeMaxMs);
        Assert.Equal(pooledTimes.Sum(), result.Speed.TotalModelTimeMs, 9);
    }

    [Fact]
    public void OverallSpeedIndex_StaysWithinHalfAPointOfThePooledAnswerMean()
    {
        // Stored Speed Indices are integers: A = round(72.33) = 72, B = round(40.5) = 41.
        // Composite (72 + 2*41) / 3 = 51.33; pooled answer mean 460 / 9 = 51.11.
        var qa = Questions(SuiteA, 20, 20, 20);
        var qb = Questions(SuiteB, 50, 50, 50, 50, 50, 50);
        int[] speedA = { 71, 72, 74 };
        int[] speedB = { 40, 41, 41, 40, 40, 41 };

        var result = BenchmarkBatteryStatistics.Compute(
            Definition(BenchmarkBatteryWeightingScheme.DifficultyMass, SuiteA, SuiteB),
            new[]
            {
                Input(0, SuiteA, qa, new[]
                {
                    Run(1101, SuiteA, qa, S(80, 80, 80), speedIndex: BenchmarkScoring.SpeedIndex(S(speedA)), speedScores: S(speedA))
                }),
                Input(1, SuiteB, qb, new[]
                {
                    Run(1201, SuiteB, qb, S(60, 60, 60, 60, 60, 60), speedIndex: BenchmarkScoring.SpeedIndex(S(speedB)), speedScores: S(speedB))
                })
            });

        double pooled = speedA.Concat(speedB).Average();
        double overall = result.Speed!.OverallSpeedIndex!.Value;

        Assert.Equal((72.0 + 2 * 41.0) / 3.0, overall, 9);
        Assert.True(Math.Abs(overall - pooled) <= 0.5);
    }

    [Fact]
    public void ANullSuiteSpeedIndex_MakesTheOverallSpeedIndexNull_AndNamesTheSuite()
    {
        var qa = Questions(SuiteA, 20, 20, 20);
        var qb = Questions(SuiteB, 50, 50, 50);

        var result = BenchmarkBatteryStatistics.Compute(
            Definition(BenchmarkBatteryWeightingScheme.DifficultyMass, SuiteA, SuiteB),
            new[]
            {
                Input(0, SuiteA, qa, new[] { Run(1101, SuiteA, qa, S(80, 80, 80), speedIndex: 70) }),
                Input(1, SuiteB, qb, new[] { Run(1201, SuiteB, qb, S(60, 60, 60), speedIndex: null) })
            });

        Assert.True(result.Complete);
        Assert.Null(result.Speed!.OverallSpeedIndex);
        Assert.Equal(new[] { 1 }, result.Speed.SpeedIndexWithheldBySuiteIndex);
        Assert.Contains(result.Caveats, c => c.Contains("Suite 12", StringComparison.Ordinal) && c.Contains("Speed", StringComparison.Ordinal));
    }

    // --- M5: dimensions and critical errors -------------------------------------------------------

    [Fact]
    public void DimensionsAndCriticalErrorRate_UseTheCountWeights()
    {
        // Count weights 1/3, 2/3 (declared difficulty-mass weights would be 1/6, 5/6).
        // Accuracy: 80/3 + 2*50/3 = 60, the pooled unweighted mean (3*80 + 6*50) / 9 = 60.
        // Critical-error rate: A = mean(1, 0, 0) = 1/3, B = 0; composite (1/3)(1/3) = 1/9.
        // Completeness was never scored, so it is withheld by both suites.
        var qa = Questions(SuiteA, 20, 20, 20);
        var qb = Questions(SuiteB, 50, 50, 50, 50, 50, 50);

        var result = BenchmarkBatteryStatistics.Compute(
            Definition(BenchmarkBatteryWeightingScheme.DifficultyMass, SuiteA, SuiteB),
            new[]
            {
                Input(0, SuiteA, qa, new[]
                {
                    Run(1101, SuiteA, qa, S(80, 80, 80), accuracy: S(80, 80, 80), criticalErrors: new[] { true, false, false })
                }),
                Input(1, SuiteB, qb, new[]
                {
                    Run(1201, SuiteB, qb, S(60, 60, 60, 60, 60, 60), accuracy: S(50, 50, 50, 50, 50, 50))
                })
            });

        var accuracy = result.Dimensions.Single(d => d.Dimension == "Accuracy");
        Assert.Equal(60.0, accuracy.Mean!.Value, 9);
        Assert.Empty(accuracy.WithheldBySuiteIndex);

        var completeness = result.Dimensions.Single(d => d.Dimension == "Completeness");
        Assert.Null(completeness.Mean);
        Assert.Equal(new[] { 0, 1 }, completeness.WithheldBySuiteIndex);

        Assert.Equal(1.0 / 9.0, result.CriticalErrorRate!.Value, 9);
    }

    /// <summary>
    /// <see cref="Run"/> as a panel run over four dimensions: <paramref name="memberA"/> holds member
    /// A's stored scores and <paramref name="memberBLevels"/> member B's levels, each indexed
    /// [dimension][question] in Accuracy, Completeness, Conciseness, Readability order.
    /// </summary>
    private static BenchmarkRun PanelRun(long runId, long suiteId, BenchmarkQuestion[] questions, int[][] memberA, int[][] memberBLevels)
    {
        var run = Run(runId, suiteId, questions, S(questions.Select(_ => 80).ToArray()));
        run.CoAssessorModelConfigurationId = 7;
        run.CoAssessorModelSnapshot = BenchmarkModelSnapshots.Model(provider: "Anthropic", modelId: "claude-judge");

        for (int i = 0; i < questions.Length; i++)
        {
            var answer = run.Answers[i];
            answer.AccuracyScore = memberA[0][i];
            answer.CompletenessScore = memberA[1][i];
            answer.ConcisenessScore = memberA[2][i];
            answer.ReadabilityScore = memberA[3][i];
            answer.CoAssessmentStatus = BenchmarkAssessmentStatus.Scored;
            answer.CoAssessmentQualityScore = 80;
            answer.PanelQualityScore = 80;
            answer.CoAssessmentJson = new BenchmarkCoAssessmentRecord
            {
                AccuracyLevel = memberBLevels[0][i],
                CompletenessLevel = memberBLevels[1][i],
                ConcisenessLevel = memberBLevels[2][i],
                ReadabilityLevel = memberBLevels[3][i]
            }.Serialize();
        }

        return run;
    }

    /// <summary>
    /// Battery run 2's arithmetic: two panel suites of equal size, so count weights of 1/2 each. The
    /// composite dimensions are the mean of the suites' panel means, never of member A's alone.
    /// </summary>
    [Fact]
    public void Dimensions_OfPanelSuites_AreTheCountWeightedPanelMeans_NotMemberAs()
    {
        // Default level table 1 / 15 / 35 / 55 / 72 / 87 / 100; ten answers per suite.
        // Suite 11, member A sums 949 / 704 / 886 / 921 (means 94.9 / 70.4 / 88.6 / 92.1), member B
        //   sums 959 / 696 / 788 / 877; panel means (A + B) / 20 = 95.4 / 70.0 / 83.7 / 89.9.
        // Suite 12, member A sums 965 / 604 / 971 / 845 (means 96.5 / 60.4 / 97.1 / 84.5), member B
        //   sums 935 / 586 / 853 / 871; panel means 95.0 / 59.5 / 91.2 / 85.8.
        // Composite: 95.2 / 64.75 / 87.45 / 87.85. Member A alone would give 95.7 / 65.4 / 92.85 / 88.3.
        // (Battery run 2's member-A Accuracy means, 94.8 and 96.4, cannot sit beside these panel means
        // on ten answers of the default level table; 94.9 and 96.5 keep the panel means exact.)
        var qa = Questions(SuiteA, 50, 50, 50, 50, 50, 50, 50, 50, 50, 50);
        var qb = Questions(SuiteB, 50, 50, 50, 50, 50, 50, 50, 50, 50, 50);

        var runA = PanelRun(1101, SuiteA, qa,
            new[]
            {
                new[] { 95, 95, 95, 95, 95, 95, 95, 95, 95, 94 },
                new[] { 70, 70, 70, 70, 70, 70, 71, 71, 71, 71 },
                new[] { 89, 89, 89, 89, 89, 89, 88, 88, 88, 88 },
                new[] { 92, 92, 92, 92, 92, 92, 92, 92, 92, 93 }
            },
            new[]
            {
                new[] { 4, 5, 6, 6, 6, 6, 6, 6, 6, 6 },
                new[] { 0, 0, 2, 4, 5, 6, 6, 6, 6, 6 },
                new[] { 0, 1, 4, 6, 6, 6, 6, 6, 6, 6 },
                new[] { 2, 3, 5, 6, 6, 6, 6, 6, 6, 6 }
            });
        var runB = PanelRun(1201, SuiteB, qb,
            new[]
            {
                new[] { 96, 96, 96, 96, 96, 97, 97, 97, 97, 97 },
                new[] { 60, 60, 60, 60, 60, 60, 61, 61, 61, 61 },
                new[] { 97, 97, 97, 97, 97, 97, 97, 97, 97, 98 },
                new[] { 84, 84, 84, 84, 84, 85, 85, 85, 85, 85 }
            },
            new[]
            {
                new[] { 2, 6, 6, 6, 6, 6, 6, 6, 6, 6 },
                new[] { 0, 0, 0, 2, 5, 5, 5, 5, 6, 6 },
                new[] { 2, 4, 4, 5, 5, 6, 6, 6, 6, 6 },
                new[] { 3, 3, 5, 5, 5, 6, 6, 6, 6, 6 }
            });

        double SuiteMean(long suiteId, BenchmarkQuestion[] questions, BenchmarkRun run, string dimension)
            => BenchmarkGroupStatistics.Compute(new BenchmarkSuite { Id = suiteId, Name = SuiteName(suiteId) }, questions, new[] { run })
                .Dimensions.Single(d => d.Dimension == dimension).Mean!.Value;

        string[] names = { "Accuracy", "Completeness", "Conciseness", "Readability" };
        double[] panelA = { 95.4, 70.0, 83.7, 89.9 };
        double[] panelB = { 95.0, 59.5, 91.2, 85.8 };
        for (int d = 0; d < names.Length; d++)
        {
            Assert.Equal(panelA[d], SuiteMean(SuiteA, qa, runA, names[d]), 9);
            Assert.Equal(panelB[d], SuiteMean(SuiteB, qb, runB, names[d]), 9);
        }

        var result = BenchmarkBatteryStatistics.Compute(
            Definition(BenchmarkBatteryWeightingScheme.DifficultyMass, SuiteA, SuiteB),
            new[]
            {
                Input(0, SuiteA, qa, new[] { runA }),
                Input(1, SuiteB, qb, new[] { runB })
            });

        double[] composite = { 95.2, 64.75, 87.45, 87.85 };
        double[] memberAComposite = { 95.7, 65.4, 92.85, 88.3 };
        for (int d = 0; d < names.Length; d++)
        {
            var dimension = result.Dimensions.Single(x => x.Dimension == names[d]);
            Assert.Empty(dimension.WithheldBySuiteIndex);
            Assert.Equal(composite[d], dimension.Mean!.Value, 9);
            Assert.NotEqual(memberAComposite[d], Math.Round(dimension.Mean!.Value, 9));
        }
    }

    // --- Panel agreement --------------------------------------------------------------------------

    /// <summary>One panel pair per answer, member B and the reader as given, and the reader's offset from the panel mean.</summary>
    private static BenchmarkBatteryPanelPair[] Pairs(int[] difficulties, int[] memberA, int[] memberB, int[] reader)
        => difficulties
            .Select((d, i) => new BenchmarkBatteryPanelPair(
                memberA[i], memberB[i], reader[i], reader[i] - (memberA[i] + memberB[i]) / 2.0, d))
            .ToArray();

    [Fact]
    public void PanelAgreement_PoolsTheIccOverEveryPair_AndComposesTheMemberIndicesUnderTheDeclaredWeights()
    {
        // Masses 150 and 300, so w = 1/3 and 2/3. Suite A's members agree closely and suite B's do
        // not, so the pooled ICC is not the mean of the two runs' ICCs.
        var qa = Questions(SuiteA, 20, 50, 80);
        var qb = Questions(SuiteB, DifficultiesB);
        int[] dA = { 20, 40, 60, 80, 50 };
        int[] dB = { 30, 40, 60, 70, 90 };
        var pairsA = Pairs(dA, new[] { 60, 70, 80, 90, 50 }, new[] { 62, 71, 79, 92, 49 }, new[] { 65, 70, 75, 95, 55 });
        var pairsB = Pairs(dB, new[] { 60, 70, 80, 90, 50 }, new[] { 80, 60, 90, 70, 65 }, new[] { 70, 65, 85, 80, 60 });

        var result = BenchmarkBatteryStatistics.Compute(
            Definition(BenchmarkBatteryWeightingScheme.DifficultyMass, SuiteA, SuiteB),
            new[]
            {
                Input(0, SuiteA, qa, new[] { Run(1101, SuiteA, qa, S(60, 70, 85)) }) with { PanelPairs = pairsA, PanelDisagreementCount = 0 },
                Input(1, SuiteB, qb, new[] { Run(1201, SuiteB, qb, S(50, 55, 65, 72, 45, 90)) }) with { PanelPairs = pairsB, PanelDisagreementCount = 2 }
            });

        double wA = result.Weights[0];
        double wB = result.Weights[1];
        Assert.Equal(1.0 / 3.0, wA, 9);
        Assert.Equal(2.0 / 3.0, wB, 9);

        double Composite(Func<BenchmarkBatteryPanelPair, double> score)
            => wA * Pooled(dA, pairsA.Select(score).ToList()) + wB * Pooled(dB, pairsB.Select(score).ToList());

        var panel = result.PanelAgreement;
        Assert.NotNull(panel);
        Assert.Equal(Composite(p => p.MemberA!.Value), panel!.MemberAAloneIndex!.Value, 9);
        Assert.Equal(Composite(p => p.MemberB!.Value), panel.MemberBAloneIndex!.Value, 9);
        Assert.Equal(Composite(p => p.ReferenceReader!.Value), panel.ReferenceReaderIndex!.Value, 9);

        var all = pairsA.Concat(pairsB).ToList();
        Assert.Equal(10, panel.PairCount);
        Assert.Equal(2, panel.Disagreements);
        Assert.Equal(all.Average(p => Math.Abs(p.MemberB!.Value - p.MemberA!.Value)), panel.MeanAbsoluteDelta!.Value, 9);
        Assert.Equal(all.Average(p => p.MemberB!.Value - p.MemberA!.Value), panel.MeanSignedDelta!.Value, 9);
        Assert.Equal(all.Average(p => p.ReferenceReaderOffset!.Value), panel.ReferenceReaderOffset!.Value, 9);

        double? Icc(IEnumerable<BenchmarkBatteryPanelPair> pairs)
            => BenchmarkScoring.IntraclassCorrelationAbsolute(pairs.Select(p => (p.MemberA!.Value, p.MemberB!.Value)).ToList());
        double pooled = Icc(all)!.Value;
        double meanOfRuns = (Icc(pairsA)!.Value + Icc(pairsB)!.Value) / 2.0;
        Assert.Equal(pooled, panel.IntraclassCorrelation!.Value, 9);
        Assert.True(Math.Abs(pooled - meanOfRuns) > 0.01, $"Pooled {pooled} and the mean of the runs' ICCs {meanOfRuns} should differ.");
    }

    [Fact]
    public void PanelAgreement_IsNull_WithoutPanelPairs_AndAMemberIndexIsNull_WhenASuiteHasNone()
    {
        var qa = Questions(SuiteA, 20, 50, 80);
        var qb = Questions(SuiteB, DifficultiesB);
        var definition = Definition(BenchmarkBatteryWeightingScheme.DifficultyMass, SuiteA, SuiteB);
        var inputA = Input(0, SuiteA, qa, new[] { Run(1101, SuiteA, qa, S(60, 70, 85)) });
        var inputB = Input(1, SuiteB, qb, new[] { Run(1201, SuiteB, qb, S(50, 55, 65, 72, 45, 90)) });

        Assert.Null(BenchmarkBatteryStatistics.Compute(definition, new[] { inputA, inputB }).PanelAgreement);

        var pairs = Pairs(new[] { 20, 40, 60, 80, 50 }, new[] { 60, 70, 80, 90, 50 }, new[] { 62, 71, 79, 92, 49 }, new[] { 65, 70, 75, 95, 55 });
        var panel = BenchmarkBatteryStatistics.Compute(definition, new[] { inputA with { PanelPairs = pairs }, inputB }).PanelAgreement;

        Assert.NotNull(panel);
        Assert.Null(panel!.MemberAAloneIndex);
        Assert.Null(panel.ReferenceReaderIndex);
        Assert.Equal(5, panel.PairCount);
        Assert.Null(panel.Disagreements);
        Assert.NotNull(panel.IntraclassCorrelation);
    }

    // --- M6: cost ---------------------------------------------------------------------------------

    private static BenchmarkGroupRunCost Cost(long runId, double candidate, double assessor)
        => new() { RunId = runId, CostByRole = new Dictionary<string, double> { ["candidate"] = candidate, ["assessor"] = assessor } };

    [Fact]
    public void CostTotal_IsTheSumOfTheSuiteTotals()
    {
        // A: two runs costing 0.75 and 1.25 (total 2.0, mean 1.0); B: one run costing 1.5.
        // Total 3.5, one pass 1.0 + 1.5 = 2.5, answer rows 2*3 + 6 = 12.
        var qa = Questions(SuiteA, 20, 50, 80);
        var qb = Questions(SuiteB, 50, 50, 50, 50, 50, 50);
        var runsA = new[] { Run(1101, SuiteA, qa, S(60, 70, 80)), Run(1102, SuiteA, qa, S(70, 80, 90)) };
        var runsB = new[] { Run(1201, SuiteB, qb, S(60, 60, 60, 60, 60, 60)) };

        var result = BenchmarkBatteryStatistics.Compute(
            Definition(BenchmarkBatteryWeightingScheme.DifficultyMass, SuiteA, SuiteB),
            new[]
            {
                Input(0, SuiteA, qa, runsA, costs: new[] { Cost(1101, 0.5, 0.25), Cost(1102, 1.0, 0.25) }),
                Input(1, SuiteB, qb, runsB, costs: new[] { Cost(1201, 1.0, 0.5) })
            });

        var cost = result.Cost!;
        Assert.True(cost.Available);
        Assert.Equal(result.Suites.Sum(s => s.TotalCost!.Value), cost.TotalCost!.Value, 12);
        Assert.Equal(3.5, cost.TotalCost!.Value, 12);
        Assert.Equal(2.5, cost.PassCost!.Value, 12);
        Assert.Equal(2.5, cost.TotalCostByRole!["candidate"], 12);
        Assert.Equal(12, cost.AnswerRowCount);
        Assert.Equal(3.5 / 12.0, cost.CostPerQuestion!.Value, 12);
        Assert.Equal(2.5 / result.OverallIndex!.PointEstimate, cost.CostPerIndexPoint!.Value, 12);
    }

    [Fact]
    public void AnUnresolvedRunCost_MakesEveryCostFigureNull_AndNamesTheSuite()
    {
        // Pricing resolved for one of suite A's two usable members: the group layer would sum the
        // one it has, which is an unknown, not a smaller total.
        var qa = Questions(SuiteA, 20, 50, 80);
        var qb = Questions(SuiteB, 50, 50, 50);
        var runsA = new[] { Run(1101, SuiteA, qa, S(60, 70, 80)), Run(1102, SuiteA, qa, S(70, 80, 90)) };
        var runsB = new[] { Run(1201, SuiteB, qb, S(60, 60, 60)) };

        var result = BenchmarkBatteryStatistics.Compute(
            Definition(BenchmarkBatteryWeightingScheme.DifficultyMass, SuiteA, SuiteB),
            new[]
            {
                Input(0, SuiteA, qa, runsA, costs: new[] { Cost(1101, 0.5, 0.25) }),
                Input(1, SuiteB, qb, runsB, costs: new[] { Cost(1201, 1.0, 0.5) })
            });

        var cost = result.Cost!;
        Assert.False(cost.Available);
        Assert.Null(cost.TotalCost);
        Assert.Null(cost.PassCost);
        Assert.Null(cost.CostPerQuestion);
        Assert.Null(cost.CostPerIndexPoint);
        Assert.Equal(new[] { 0 }, cost.WithheldBySuiteIndex);
        Assert.Contains("Suite 11", cost.WithheldReason, StringComparison.Ordinal);
    }

    // --- M2, M5: schemes, sensitivity, leave-one-out ----------------------------------------------

    // Three suites with constant scores, so every suite index is exact:
    //   A: 3 questions at difficulty 20, all 80 -> I = 80, mass 60
    //   B: 3 questions at difficulty 60, all 60 -> I = 60, mass 180
    //   C: 6 questions at difficulty 30, all 40 -> I = 40, mass 180
    private static BenchmarkBatterySuiteInput[] ThreeConstantSuites()
    {
        var qa = Questions(SuiteA, 20, 20, 20);
        var qb = Questions(SuiteB, 60, 60, 60);
        var qc = Questions(SuiteC, 30, 30, 30, 30, 30, 30);
        return new[]
        {
            Input(0, SuiteA, qa, new[] { Run(1101, SuiteA, qa, S(80, 80, 80)) }),
            Input(1, SuiteB, qb, new[] { Run(1201, SuiteB, qb, S(60, 60, 60)) }),
            Input(2, SuiteC, qc, new[] { Run(1301, SuiteC, qc, S(40, 40, 40, 40, 40, 40)) })
        };
    }

    [Fact]
    public void EqualScheme_PointEstimate_SensitivityAndLeaveOneOut_OnAHandComputedFixture()
    {
        var result = BenchmarkBatteryStatistics.Compute(
            Definition(BenchmarkBatteryWeightingScheme.Equal, SuiteA, SuiteB, SuiteC),
            ThreeConstantSuites());

        // Equal: (80 + 60 + 40) / 3 = 60.
        Assert.Equal(60.0, result.OverallIndex!.PointEstimate, 9);

        // Sensitivity: difficulty mass (60*80 + 180*60 + 180*40) / 420 = 22800 / 420 = 380/7;
        // item count (3*80 + 3*60 + 6*40) / 12 = 660 / 12 = 55.
        var declared = result.WeightingSensitivity[0];
        Assert.True(declared.Declared);
        Assert.Equal(BenchmarkBatteryWeightingScheme.Equal, declared.Scheme);
        Assert.Equal(60.0, declared.Index, 9);
        Assert.Equal(3, result.WeightingSensitivity.Count);
        Assert.Equal(380.0 / 7.0, result.WeightingSensitivity.Single(s => s.Scheme == BenchmarkBatteryWeightingScheme.DifficultyMass).Index, 9);
        Assert.Equal(55.0, result.WeightingSensitivity.Single(s => s.Scheme == BenchmarkBatteryWeightingScheme.ItemCount).Index, 9);

        // Leave one out, remaining equal weights renormalized: without A (60+40)/2 = 50,
        // without B (80+40)/2 = 60, without C (80+60)/2 = 70.
        Assert.Equal(new[] { 50.0, 60.0, 70.0 }, result.LeaveOneSuiteOut.Select(l => Math.Round(l.Index!.Value, 9)));
        Assert.Equal(new[] { -10.0, 0.0, 10.0 }, result.LeaveOneSuiteOut.Select(l => Math.Round(l.Change!.Value, 9)));

        // Profile unevenness: SD of 80, 60, 40 is 20; range 40.
        Assert.Equal(20.0, result.BetweenSuiteStandardDeviation!.Value, 9);
        Assert.Equal(40.0, result.BetweenSuiteRange!.Value, 9);
    }

    [Fact]
    public void CustomScheme_NormalizesTheDeclaredWeights()
    {
        // Custom 2 : 1 : 1 -> 0.5, 0.25, 0.25: 40 + 15 + 10 = 65.
        var result = BenchmarkBatteryStatistics.Compute(
            Definition(BenchmarkBatteryWeightingScheme.Custom, (SuiteA, 2.0), (SuiteB, 1.0), (SuiteC, 1.0)),
            ThreeConstantSuites());

        Assert.Equal(65.0, result.OverallIndex!.PointEstimate, 9);
        Assert.Equal(new[] { 0.5, 0.25, 0.25 }, result.Weights.Select(w => Math.Round(w, 12)));
        Assert.Equal(0.5 * 80.0, result.Suites[0].Contribution!.Value, 9);

        // A Custom battery reports all three automatic schemes as sensitivity figures.
        Assert.Equal(4, result.WeightingSensitivity.Count);
    }

    [Fact]
    public void AllZeroItemSamplingErrors_GiveAZeroHalfWidth_WithoutDividingByZero()
    {
        // Constant scores give every suite an item-sampling SE of exactly 0, so Satterthwaite is
        // 0/0: the half-width is 0 and nu is reported as the sum of (n_s - 1) = 2 + 2 + 5 = 9.
        var result = BenchmarkBatteryStatistics.Compute(
            Definition(BenchmarkBatteryWeightingScheme.DifficultyMass, SuiteA, SuiteB, SuiteC),
            ThreeConstantSuites());

        var overall = result.OverallIndex!;
        Assert.Equal(0.0, overall.ItemSamplingStandardError!.Value, 12);
        Assert.Equal(0.0, overall.ItemSamplingHalfWidth!.Value, 12);
        Assert.Equal(9.0, overall.EffectiveDegreesOfFreedom!.Value, 12);
        Assert.Equal(0.0, overall.CombinedHalfWidth!.Value, 12);
    }

    // --- M3: uncertainty --------------------------------------------------------------------------

    [Fact]
    public void ItemSamplingStandardError_CombinesTheSuitesInQuadrature_OnSatterthwaiteDegreesOfFreedom()
    {
        var qa = Questions(SuiteA, DifficultiesA);
        var qb = Questions(SuiteB, DifficultiesB);

        var result = BenchmarkBatteryStatistics.Compute(
            Definition(BenchmarkBatteryWeightingScheme.DifficultyMass, SuiteA, SuiteB),
            new[]
            {
                Input(0, SuiteA, qa, new[] { Run(1101, SuiteA, qa, S(60, 70, 85)) }),
                Input(1, SuiteB, qb, new[] { Run(1201, SuiteB, qb, S(50, 55, 65, 72, 45, 90)) })
            });

        var w = result.Weights;
        var se = result.Suites.Select(s => s.Statistics!.Index.ItemSamplingStandardError!.Value).ToList();
        var n = result.Suites.Select(s => s.Statistics!.ItemCount).ToList();

        double variance = w[0] * w[0] * se[0] * se[0] + w[1] * w[1] * se[1] * se[1];
        double nu = variance * variance
            / (Math.Pow(w[0] * se[0], 4) / (n[0] - 1) + Math.Pow(w[1] * se[1], 4) / (n[1] - 1));

        var overall = result.OverallIndex!;
        Assert.Equal(Math.Sqrt(variance), overall.ItemSamplingStandardError!.Value, 12);
        Assert.Equal(nu, overall.EffectiveDegreesOfFreedom!.Value, 9);
        Assert.Equal(
            BenchmarkGroupStatistics.StudentTCritical95((int)Math.Floor(nu)) * Math.Sqrt(variance),
            overall.ItemSamplingHalfWidth!.Value,
            9);

        // Each suite's own interval stays on the group layer's 1.96.
        Assert.Equal(result.Suites[0].Statistics!.Index.CombinedHalfWidth, result.Suites[0].CombinedHalfWidth);
        Assert.Contains(BenchmarkBatteryStatistics.CriticalValueCaveat, result.Caveats);
    }

    [Fact]
    public void Satterthwaite_IsNMinusOne_WhenOneSuiteCarriesAllTheVariance()
    {
        double nu = BenchmarkBatteryStatistics.SatterthwaiteDegreesOfFreedom(
            new[] { 0.5, 0.5 }, new[] { 4.0, 0.0 }, new[] { 7, 2 });

        Assert.Equal(7.0, nu, 12);

        // All zero: 0/0, reported as the sum of the degrees of freedom.
        Assert.Equal(9.0, BenchmarkBatteryStatistics.SatterthwaiteDegreesOfFreedom(
            new[] { 0.5, 0.5 }, new[] { 0.0, 0.0 }, new[] { 7, 2 }), 12);
    }

    [Fact]
    public void PerRunIndexFromItemRows_EqualsPerRunIndices_OnACompleteFixture()
    {
        var qa = Questions(SuiteA, DifficultiesA);
        var runs = new[]
        {
            Run(1101, SuiteA, qa, S(60, 70, 85)),
            Run(1102, SuiteA, qa, S(65, 60, 90)),
            Run(1103, SuiteA, qa, S(70, 75, 80))
        };
        var statistics = BenchmarkGroupStatistics.Compute(new BenchmarkSuite { Id = SuiteA, Name = SuiteName(SuiteA) }, qa, runs);

        var byRun = BenchmarkBatteryStatistics.PerRunIndexByRunId(statistics);

        Assert.Equal(3, byRun.Count);
        Assert.Equal(statistics.Index.PerRunIndices[0], byRun[1101], 12);
        Assert.Equal(statistics.Index.PerRunIndices[1], byRun[1102], 12);
        Assert.Equal(statistics.Index.PerRunIndices[2], byRun[1103], 12);
    }

    [Fact]
    public void PerRunIndexFromItemRows_StaysAlignedToItsRun_WhenAnotherRunHasNoScoredItem()
    {
        // PerRunIndices silently drops run 1102 and shifts 1103 into its place; the item-row
        // reconstruction keeps every index on its own run.
        var qa = Questions(SuiteA, DifficultiesA);
        var silent = Run(1102, SuiteA, qa, S(65, 60, 90));
        foreach (var answer in silent.Answers)
        {
            answer.Status = BenchmarkAnswerStatus.ProviderError;
        }

        var runs = new[] { Run(1101, SuiteA, qa, S(60, 70, 85)), silent, Run(1103, SuiteA, qa, S(70, 75, 80)) };
        var statistics = BenchmarkGroupStatistics.Compute(new BenchmarkSuite { Id = SuiteA, Name = SuiteName(SuiteA) }, qa, runs);

        var byRun = BenchmarkBatteryStatistics.PerRunIndexByRunId(statistics);

        Assert.Equal(2, statistics.Index.PerRunIndices.Count);
        Assert.False(byRun.ContainsKey(1102));
        Assert.Equal(statistics.Index.PerRunIndices[0], byRun[1101], 12);
        Assert.Equal(statistics.Index.PerRunIndices[1], byRun[1103], 12);

        // (20*70 + 50*75 + 80*80) / 150 = 11550 / 150 = 77.
        Assert.Equal(77.0, byRun[1103], 9);
    }

    private static BenchmarkBatterySuiteInput[] TwoSuitesWithRuns(int runsA, int runsB, int[]? roundsA = null, int[]? roundsB = null)
    {
        int[][] poolA = { new[] { 60, 70, 85 }, new[] { 65, 60, 90 }, new[] { 70, 75, 80 }, new[] { 55, 65, 95 } };
        int[][] poolB =
        {
            new[] { 50, 55, 65, 72, 45, 90 },
            new[] { 55, 50, 70, 70, 40, 95 },
            new[] { 45, 60, 60, 75, 50, 85 },
            new[] { 60, 45, 75, 65, 55, 80 }
        };

        var qa = Questions(SuiteA, DifficultiesA);
        var qb = Questions(SuiteB, DifficultiesB);
        return new[]
        {
            Input(0, SuiteA, qa, Enumerable.Range(0, runsA).Select(r => Run(1101 + r, SuiteA, qa, S(poolA[r]))).ToList(), roundsA),
            Input(1, SuiteB, qb, Enumerable.Range(0, runsB).Select(r => Run(1201 + r, SuiteB, qb, S(poolB[r]))).ToList(), roundsB)
        };
    }

    [Fact]
    public void ThreeCompleteRounds_GiveReproducibilityFromRounds_AndTheirMeanIsTheComposite()
    {
        var result = BenchmarkBatteryStatistics.Compute(
            Definition(BenchmarkBatteryWeightingScheme.DifficultyMass, SuiteA, SuiteB),
            TwoSuitesWithRuns(3, 3));

        var overall = result.OverallIndex!;
        var perRound = overall.PerRoundIndices.Select(p => p.Index).ToList();

        Assert.Equal(BenchmarkBatteryReproducibilitySource.Rounds, overall.ReproducibilitySource);
        Assert.Equal(3, overall.RoundCount);
        Assert.Equal(new[] { 1, 2, 3 }, overall.PerRoundIndices.Select(p => p.Round));
        Assert.Equal(overall.PointEstimate, perRound.Average(), 9);

        double se = BenchmarkGroupStatistics.SampleStandardDeviation(perRound)!.Value / Math.Sqrt(3);
        Assert.Equal(se, overall.ReproducibilityStandardError!.Value, 12);
        Assert.Equal(4.3027 * se, overall.ReproducibilityHalfWidth!.Value, 9);
        Assert.Equal(
            Math.Sqrt(Math.Pow(overall.ReproducibilityHalfWidth!.Value, 2) + Math.Pow(overall.ItemSamplingHalfWidth!.Value, 2)),
            overall.CombinedHalfWidth!.Value,
            9);
    }

    [Fact]
    public void TwoRounds_GiveNoReproducibilityFigure_AndSaySo()
    {
        var result = BenchmarkBatteryStatistics.Compute(
            Definition(BenchmarkBatteryWeightingScheme.DifficultyMass, SuiteA, SuiteB),
            TwoSuitesWithRuns(2, 2));

        var overall = result.OverallIndex!;
        Assert.Equal(BenchmarkBatteryReproducibilitySource.NotAvailable, overall.ReproducibilitySource);
        Assert.Null(overall.ReproducibilityStandardError);
        Assert.Null(overall.ReproducibilityHalfWidth);
        Assert.Equal(2, overall.RoundCount);
        Assert.Equal(overall.ItemSamplingHalfWidth, overall.CombinedHalfWidth);
        Assert.Contains(BenchmarkBatteryStatistics.NoReproducibilityCaveat, result.Caveats);
    }

    [Fact]
    public void RaggedRounds_FallBackToPerSuiteStandardErrors_AndSaySo()
    {
        // Suite A has three rounds, suite B four: no per-round composite exists for round 4.
        var result = BenchmarkBatteryStatistics.Compute(
            Definition(BenchmarkBatteryWeightingScheme.DifficultyMass, SuiteA, SuiteB),
            TwoSuitesWithRuns(3, 4));

        var overall = result.OverallIndex!;
        var w = result.Weights;
        var se = result.Suites.Select(s => s.Statistics!.Index.ReproducibilityStandardError!.Value).ToList();

        Assert.Equal(BenchmarkBatteryReproducibilitySource.PerSuiteFallback, overall.ReproducibilitySource);
        Assert.Equal(0, overall.RoundCount);
        Assert.Empty(overall.PerRoundIndices);
        Assert.Equal(Math.Sqrt(w[0] * w[0] * se[0] * se[0] + w[1] * w[1] * se[1] * se[1]), overall.ReproducibilityStandardError!.Value, 12);
        Assert.Equal(
            BenchmarkBatteryStatistics.SatterthwaiteDegreesOfFreedom(w, se, new[] { 2, 3 }),
            overall.ReproducibilityDegreesOfFreedom!.Value,
            12);
        Assert.Contains(BenchmarkBatteryStatistics.PerSuiteFallbackCaveat, result.Caveats);
    }

    [Fact]
    public void AnIntervalBeyondTheScoreRange_IsClampedAndMarkedTruncated()
    {
        // Two suites, each 3 questions at difficulty 50 scored 100, 100, 40: I_s = 80, SE_s = 20
        // (sqrt(2500 * 2400 * 3/2) / 150). Weights 0.5: SE = sqrt(2 * 0.25 * 400) = sqrt(200);
        // nu = 200^2 / (2 * 100^2 / 2) = 4; half-width = 2.7764 * sqrt(200) = 39.26 > 20.
        var qa = Questions(SuiteA, 50, 50, 50);
        var qb = Questions(SuiteB, 50, 50, 50);

        var result = BenchmarkBatteryStatistics.Compute(
            Definition(BenchmarkBatteryWeightingScheme.DifficultyMass, SuiteA, SuiteB),
            new[]
            {
                Input(0, SuiteA, qa, new[] { Run(1101, SuiteA, qa, S(100, 100, 40)) }),
                Input(1, SuiteB, qb, new[] { Run(1201, SuiteB, qb, S(100, 100, 40)) })
            });

        var overall = result.OverallIndex!;
        double half = 2.7764 * Math.Sqrt(200.0);

        Assert.Equal(80.0, overall.PointEstimate, 9);
        Assert.Equal(Math.Sqrt(200.0), overall.ItemSamplingStandardError!.Value, 9);
        Assert.Equal(4.0, overall.EffectiveDegreesOfFreedom!.Value, 9);
        Assert.Equal(half, overall.CombinedHalfWidth!.Value, 9);
        Assert.Equal(100.0, overall.CombinedUpper!.Value, 12);
        Assert.Equal(80.0 - half, overall.CombinedLower!.Value, 9);
        Assert.True(overall.CombinedIntervalTruncated);
    }

    // --- M4: completeness -------------------------------------------------------------------------

    [Fact]
    public void AnIncompleteBattery_HasNoHeadline_ButKeepsItsSuiteRows()
    {
        var qa = Questions(SuiteA, DifficultiesA);
        var excluded = new BenchmarkBatteryExcludedMember(1, 1, 1201, "index withheld");

        var result = BenchmarkBatteryStatistics.Compute(
            Definition(BenchmarkBatteryWeightingScheme.DifficultyMass, SuiteA, SuiteB),
            new[]
            {
                Input(0, SuiteA, qa, new[] { Run(1101, SuiteA, qa, S(60, 70, 85)) }),
                new BenchmarkBatterySuiteInput(
                    1,
                    null,
                    new BenchmarkBatterySuiteMass(6, 300.0),
                    new Dictionary<long, int>(),
                    Array.Empty<double>(),
                    Array.Empty<double>(),
                    0,
                    6,
                    new[] { excluded })
            });

        Assert.False(result.Complete);
        Assert.Equal(1, result.CompletedSuiteCount);
        Assert.Equal(2, result.SuiteCount);
        Assert.Null(result.OverallIndex);
        Assert.False(result.PooledIdentityHolds);
        Assert.Null(result.Speed);
        Assert.Null(result.Cost);
        Assert.Empty(result.WeightingSensitivity);
        Assert.Empty(result.LeaveOneSuiteOut);

        Assert.Equal(2, result.Suites.Count);
        Assert.True(result.Suites[0].Complete);
        Assert.NotNull(result.Suites[0].Index);
        Assert.False(result.Suites[1].Complete);
        Assert.Null(result.Suites[1].Index);

        Assert.Equal(new[] { excluded }, result.ExcludedMembers);
        Assert.Contains("Incomplete (1 of 2 suites): no Overall Index is reported.", result.Caveats);
    }

    [Fact]
    public void TheResult_RoundTripsThroughSystemTextJson()
    {
        var result = BenchmarkBatteryStatistics.Compute(
            Definition(BenchmarkBatteryWeightingScheme.DifficultyMass, SuiteA, SuiteB),
            TwoSuitesWithRuns(3, 3));

        string json = JsonSerializer.Serialize(result);
        var back = JsonSerializer.Deserialize<BenchmarkBatteryStatisticsResult>(json)!;

        Assert.Contains("\"Scheme\":\"DifficultyMass\"", json, StringComparison.Ordinal);
        Assert.Contains("\"ReproducibilitySource\":\"Rounds\"", json, StringComparison.Ordinal);
        Assert.Equal(result.OverallIndex!.PointEstimate, back.OverallIndex!.PointEstimate, 12);
        Assert.Equal(BenchmarkBatteryReproducibilitySource.Rounds, back.OverallIndex.ReproducibilitySource);
        Assert.Equal(result.Suites[1].Statistics!.Items.Count, back.Suites[1].Statistics!.Items.Count);
        Assert.Equal(result.SuiteMasses[1].DifficultyMass, back.SuiteMasses[1].DifficultyMass, 12);
    }

    // --- M7: comparison ---------------------------------------------------------------------------

    private static BenchmarkBatteryStatisticsResult OneRunResult(
        BenchmarkBatteryDefinition definition,
        long firstRunId,
        BenchmarkQuestion[] qa,
        int[] scoresA,
        BenchmarkQuestion[] qb,
        int[] scoresB)
    {
        return BenchmarkBatteryStatistics.Compute(definition, new[]
        {
            Input(0, SuiteA, qa, new[] { Run(firstRunId, SuiteA, qa, S(scoresA)) }),
            Input(1, SuiteB, qb, new[] { Run(firstRunId + 1, SuiteB, qb, S(scoresB)) })
        });
    }

    [Fact]
    public void CompositeDifference_IsTheDifferenceOfTheOverallIndices_AndThePooledWeightedDifference()
    {
        var definition = Definition(BenchmarkBatteryWeightingScheme.DifficultyMass, SuiteA, SuiteB);
        var qa = Questions(SuiteA, DifficultiesA);
        var qb = Questions(SuiteB, DifficultiesB);
        int[] baseA = { 60, 70, 80 };
        int[] baseB = { 50, 55, 65, 70, 45, 90 };
        int[] treatA = { 70, 65, 90 };
        int[] treatB = { 55, 60, 60, 80, 50, 85 };

        var baseline = OneRunResult(definition, 5001, qa, baseA, qb, baseB);
        var treatment = OneRunResult(definition, 6001, qa, treatA, qb, treatB);

        var comparison = BenchmarkBatteryStatistics.Compare(baseline, treatment);

        double d = comparison.CompositeDifference!.Value;
        Assert.Equal(treatment.OverallIndex!.PointEstimate - baseline.OverallIndex!.PointEstimate, d, 9);

        // Under the default, D is the difficulty-weighted mean difference over all paired questions.
        var difficulties = DifficultiesA.Concat(DifficultiesB).ToList();
        var deltas = treatA.Zip(baseA, (t, b) => (double)(t - b)).Concat(treatB.Zip(baseB, (t, b) => (double)(t - b))).ToList();
        Assert.Equal(Pooled(difficulties, deltas), d, 9);

        Assert.Equal(9, comparison.PairedItemCount);
        Assert.Equal(BenchmarkBatteryRandomizationMethod.Exact, comparison.RandomizationMethod);
        Assert.Null(comparison.Seed);

        // Suite A: (20*10 + 50*-5 + 80*10) / 150 = 750 / 150 = 5.
        Assert.Equal(5.0, comparison.Suites[0].WeightedDifference!.Value, 9);

        var w = baseline.Weights;
        var se = comparison.Suites.Select(s => s.WeightedDifferenceStandardError!.Value).ToList();
        Assert.Equal(Math.Sqrt(w[0] * w[0] * se[0] * se[0] + w[1] * w[1] * se[1] * se[1]), comparison.CompositeStandardError!.Value, 12);

        // Holm over the two suites' Wilcoxon p-values.
        var raw = comparison.Suites.Select(s => s.WilcoxonPValue!.Value).ToList();
        var holm = BenchmarkBatteryStatistics.HolmAdjust(raw);
        Assert.Equal(holm[0], comparison.Suites[0].HolmAdjustedPValue!.Value, 12);
        Assert.Equal(holm[1], comparison.Suites[1].HolmAdjustedPValue!.Value, 12);
        Assert.NotNull(comparison.Suites[0].Comparison);
    }

    [Fact]
    public void UnroundedWeightedStandardError_AgreesWithQualityIndexStandardError_OnAWholeWeightedMean()
    {
        // (20*60 + 50*70 + 80*80) / 150 = 74 and (10*90 + 30*40 + 60*70) / 100 = 63, both whole, so
        // the original's rounding of the centre changes nothing.
        (double Value, double Weight)[] first = { (60.0, 20.0), (70.0, 50.0), (80.0, 80.0) };
        (double Value, double Weight)[] second = { (90.0, 10.0), (40.0, 30.0), (70.0, 60.0) };

        Assert.Equal(
            BenchmarkScoring.QualityIndexStandardError(new (int?, int?)[] { (60, 20), (70, 50), (80, 80) })!.Value,
            BenchmarkBatteryStatistics.UnroundedWeightedStandardError(first)!.Value,
            12);
        Assert.Equal(
            BenchmarkScoring.QualityIndexStandardError(new (int?, int?)[] { (90, 10), (40, 30), (70, 60) })!.Value,
            BenchmarkBatteryStatistics.UnroundedWeightedStandardError(second)!.Value,
            12);

        Assert.Null(BenchmarkBatteryStatistics.UnroundedWeightedStandardError(first.Take(2).ToList()));
    }

    [Fact]
    public void ExactSignFlipPValue_MatchesAHandCount_OnSixItems()
    {
        // Every item at difficulty 50, both suites weighted 0.5, so every c_q = 0.5 * 50/150 = 1/6
        // and D* = (10/6) * sum(e_i * i) over i = 1..6 with the sixth difference negative.
        // Observed sum 1+2+3+4+5-6 = 9, D = 90/6 = 15. With s the sum of the flipped subset,
        // sum(e_i * i) = 21 - 2s and |21 - 2s| >= 9 iff s <= 6 or s >= 15. Subsets of {1..6} with
        // sum <= 6: 1+1+1+2+2+3+4 = 14, and as many with sum >= 15 by symmetry: p = 28/64 = 0.4375.
        var definition = Definition(BenchmarkBatteryWeightingScheme.DifficultyMass, SuiteA, SuiteB);
        var qa = Questions(SuiteA, 50, 50, 50);
        var qb = Questions(SuiteB, 50, 50, 50);

        var baseline = OneRunResult(definition, 5001, qa, new[] { 50, 50, 50 }, qb, new[] { 50, 40, 90 });
        var treatment = OneRunResult(definition, 6001, qa, new[] { 60, 70, 80 }, qb, new[] { 90, 90, 30 });

        var comparison = BenchmarkBatteryStatistics.Compare(baseline, treatment);

        Assert.Equal(15.0, comparison.CompositeDifference!.Value, 9);
        Assert.Equal(BenchmarkBatteryRandomizationMethod.Exact, comparison.RandomizationMethod);
        Assert.Equal(0.4375, comparison.RandomizationPValue!.Value, 12);
        Assert.Null(comparison.MonteCarloResamples);
    }

    [Fact]
    public void MonteCarloSignFlipPValue_IsDeterministicForAFixedSeed()
    {
        // 22 paired items exceed the exact limit of 20.
        var definition = Definition(BenchmarkBatteryWeightingScheme.DifficultyMass, SuiteA, SuiteB);
        var qa = Questions(SuiteA, Enumerable.Repeat(50, 11).ToArray());
        var qb = Questions(SuiteB, Enumerable.Repeat(50, 11).ToArray());
        int[] flat = Enumerable.Repeat(50, 11).ToArray();
        int[] treatA = new[] { 12, -3, 7, 0, 15, -9, 4, 6, -2, 10, 3 }.Select(x => 50 + x).ToArray();
        int[] treatB = new[] { -4, 8, 11, -6, 2, 9, 0, 5, -1, 13, 7 }.Select(x => 50 + x).ToArray();

        var baseline = OneRunResult(definition, 5001, qa, flat, qb, flat);
        var treatment = OneRunResult(definition, 6001, qa, treatA, qb, treatB);
        var options = new BenchmarkBatteryCompareOptions { MonteCarloResamples = 5000 };

        var first = BenchmarkBatteryStatistics.Compare(baseline, treatment, options);
        var second = BenchmarkBatteryStatistics.Compare(baseline, treatment, options);

        Assert.Equal(BenchmarkBatteryRandomizationMethod.MonteCarlo, first.RandomizationMethod);
        Assert.Equal(5000, first.MonteCarloResamples);
        Assert.Equal(BenchmarkBatteryStatistics.DefaultSeed, first.Seed);
        Assert.Equal(first.RandomizationPValue, second.RandomizationPValue);
        Assert.Equal(first.MonteCarloStandardError, second.MonteCarloStandardError);

        // Phipson-Smyth: p = (1 + count) / (B + 1), never zero.
        double p = first.RandomizationPValue!.Value;
        Assert.InRange(p, 1.0 / 5001.0, 1.0);
        Assert.Equal(Math.Round(p * 5001.0), p * 5001.0, 6);
        Assert.Equal(Math.Sqrt(p * (1.0 - p) / 5000.0), first.MonteCarloStandardError!.Value, 12);
    }

    [Fact]
    public void HolmAdjust_OnAKnownVector()
    {
        // Sorted: 0.005 (x4 = 0.02), 0.01 (x3 = 0.03), 0.03 (x2 = 0.06), 0.04 (x1 = 0.04 -> 0.06 by
        // monotonicity). Back in input order: 0.03, 0.06, 0.06, 0.02.
        var adjusted = BenchmarkBatteryStatistics.HolmAdjust(new[] { 0.01, 0.04, 0.03, 0.005 });

        Assert.Equal(new[] { 0.03, 0.06, 0.06, 0.02 }, adjusted.Select(p => Math.Round(p, 12)));
        Assert.Equal(new[] { 1.0, 1.0 }, BenchmarkBatteryStatistics.HolmAdjust(new[] { 0.6, 0.9 }));
        Assert.Empty(BenchmarkBatteryStatistics.HolmAdjust(Array.Empty<double>()));
    }
}
