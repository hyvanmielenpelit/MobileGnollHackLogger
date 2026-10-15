namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using Overseer.Services.Benchmarking;
using Overseer.Services.Benchmarking.RootCause;
using Xunit;

/// <summary>
/// The root-cause engine end to end for a run-completion subject: the control limit with no, one and two
/// controls, the control run it proposes, the order of the recommended runs, the item-level control table,
/// the chart rows and determinism.
/// </summary>
public class RootCauseEngineTests
{
    private const long SubjectConfigId = 11;

    private static RootCauseAnswerRecord Answer(int q, double quality, string band, bool noTool = false, bool critical = false)
        => new()
        {
            RunId = 500,
            ItemKey = "q" + q + "@1",
            QuestionKey = "q" + q,
            Band = band,
            Ok = true,
            Counting = true,
            Quality = quality,
            NoTool = noTool,
            CriticalError = critical,
            Accuracy = quality,
            Completeness = quality,
            Conciseness = 80,
            Readability = 80,
            DurationMs = 20000 + 500 * q,
            ModelTimeMs = 15000 + 400 * q,
            ToolTimeMs = 3000,
            ModelCalls = 2 + q % 3
        };

    private static RootCauseItem SpeedItem(int q, string band)
        => new()
        {
            Key = "q" + q,
            Band = band,
            TtftMs = 3000 + 100 * q,
            DecodeMs = 10000 + 200 * q,
            ModelTimeMs = 15000 + 400 * q,
            DurationMs = 20000 + 500 * q,
            ToolMs = 3000,
            WaitMs = 200,
            Calls = 2 + q % 3,
            OutputTokens = 1500 + 30 * q
        };

    private static RootCauseItem CostItem(int q, string band)
        => new()
        {
            Key = "q" + q,
            Band = band,
            Tokens = new double[] { 4000 + 10 * q, 2000, 100, 800, 600 },
            Cost = new double[] { 0.004 + q * 1e-5, 0.0002, 0.0001, 0.008, 0.006 }
        };

    private static string BandOf(int q) => q <= 4 ? "Simple" : q <= 8 ? "Intermediate" : "Advanced";

    private static RootCauseModelInput Subject()
    {
        var questions = Enumerable.Range(1, 12).ToList();
        var answers = questions.Select(q => Answer(q, q % 4 == 0 ? 30 : 60 + 3 * q, BandOf(q), noTool: q % 4 == 0)).ToList();
        return new RootCauseModelInput
        {
            Token = RootCauseEngine.SubjectToken,
            Label = "Orion Max (high)",
            Provider = "OpenAI",
            ModelId = "orion-max",
            ThinkingLevel = "high",
            EntryKey = "run:500",
            IsSubject = true,
            RunIds = new long[] { 500 },
            QualityIndex = 70,
            QualityLower = 64,
            QualityUpper = 76,
            Dimensions = new double?[] { 70, 72, 80, 80 },
            ModelTimeP50Ms = 18000,
            CostPerQuestionUsd = 0.0183,
            CostPerRunUsd = 0.22,
            Evidence = new RootCauseModelEvidence
            {
                Answers = answers,
                RunCount = 1,
                RunsPerSuite = 1,
                Priced = true,
                CriticalErrorsConfirmed = true,
                MeanMinutesPerRun = 9,
                Settings = new RootCauseRunSettings
                {
                    SuiteId = 7,
                    SuiteName = "Core knowledge",
                    TestedModelConfigurationId = SubjectConfigId,
                    ScoringProfileId = 3,
                    VerboseMode = false,
                    AllowSourceCodeReferences = true,
                    AssessorModelConfigurationId = 21,
                    CoAssessorModelConfigurationId = 22,
                    ClaimVerifierModelConfigurationId = 23
                },
                SpeedItems = questions.Select(q => SpeedItem(q, BandOf(q))).ToList(),
                CostItems = questions.Select(q => CostItem(q, BandOf(q))).ToList()
            }
        };
    }

    private static RootCauseCatalog Catalog() => new()
    {
        Configurations = new[]
        {
            new RootCauseConfigOption { Id = SubjectConfigId, Provider = "OpenAI", ModelId = "orion-max", ThinkingLevel = "high", Label = "Orion Max (high)" },
            new RootCauseConfigOption { Id = 31, Provider = "Anthropic", ModelId = "vega-pro", ThinkingLevel = "medium", Label = "Vega Pro (medium)" },
            new RootCauseConfigOption { Id = 41, Provider = "Google", ModelId = "nova-flash", ThinkingLevel = "high", Label = "Nova Flash (high)" }
        },
        Recommended = new[]
        {
            new RootCauseRecommendedModel { Provider = "OpenAI", Model = "orion-max", ThinkingLevel = "high" },
            new RootCauseRecommendedModel { Provider = "Anthropic", Model = "vega-pro", ThinkingLevel = "medium" },
            new RootCauseRecommendedModel { Provider = "Google", Model = "nova-flash", ThinkingLevel = "high" }
        }
    };

    private static RootCauseControlSet Controls(params (string Label, string Provider, string ModelId, double Score)[] controls)
    {
        if (controls.Length == 0) return RootCauseControlSet.None(RootCauseControls.NoRunControlReason, "OpenAI", 4);
        return new RootCauseControlSet
        {
            SubjectProvider = "OpenAI",
            Controls = controls.Select((c, i) => new RootCauseControl
            {
                Number = i + 1,
                ModelLabel = c.Label,
                Provider = c.Provider,
                ModelId = c.ModelId,
                RunId = 400 + i,
                DateUtc = new DateTime(2026, 10, 10 - i, 12, 0, 0, DateTimeKind.Utc),
                SharedQuestions = 12
            }).ToList(),
            Scores = controls.Select((c, i) => new RootCauseControlScores
            {
                Number = i + 1,
                ScoresByItem = Enumerable.Range(1, 12).ToDictionary(q => "q" + q + "@1", q => q % 4 == 0 ? 35.0 : c.Score, StringComparer.Ordinal)
            }).ToList()
        };
    }

    private static RootCauseInput Input(RootCauseControlSet controls) => new()
    {
        Scope = RootCauseEngine.ModelScope,
        Origin = RootCauseOrigins.RunCompletion,
        ComputedAtUtc = new DateTime(2026, 10, 15, 8, 0, 0, DateTimeKind.Utc),
        PricingBasis = "As run",
        PricingBasisKind = "AsRun",
        Models = new[] { Subject() },
        Controls = controls,
        Catalog = Catalog(),
        Questions = Enumerable.Range(1, 12).Select(q => new RootCauseQuestionRef { Number = q, Reference = "Q" + q, ItemKey = "q" + q + "@1" }).ToList()
    };

    [Fact]
    public void WithNoControl_TheLimitIsLow_AndAControlRunOfOtherProvidersIsProposed()
    {
        var result = RootCauseEngine.Compute(Input(Controls()));

        Assert.Equal(RootCauseGrades.Low, result.ControlLimit);
        Assert.Contains("no other model answered these questions", result.ControlLimitReason);
        Assert.Contains(RootCauseControls.NoRunControlReason, result.Notes);

        var control = Assert.Single(result.RecommendedRuns, r => r.Kind == RootCauseRunKinds.Control);
        Assert.DoesNotContain(SubjectConfigId, control.TestedModelConfigurationIds);
        Assert.Equal(new long[] { 31, 41 }, control.TestedModelConfigurationIds.OrderBy(id => id));
        Assert.Equal(RootCauseMarks.Recommended, control.Mark);
        Assert.Null(control.ReportWriterModelConfigurationId);
        Assert.Equal(7, control.SuiteId);
        Assert.Equal(21, control.AssessorModelConfigurationId);
        Assert.Equal(22, control.CoAssessorModelConfigurationId);
        Assert.Equal(23, control.ClaimVerifierModelConfigurationId);

        Assert.All(result.Hypotheses.Where(h => h.Axis == RootCauseAxes.Intelligence && h.Kind == "within"
                                                && h.Loci.Any(l => l is RootCauseLoci.Suite or RootCauseLoci.Corpus or RootCauseLoci.Chat)),
            h => Assert.True(RootCauseGrades.Rank(h.Grade) <= RootCauseGrades.Rank(RootCauseGrades.Low)));
    }

    [Fact]
    public void WithOneControl_TheLimitIsModerate_AndAControlRunIsStillProposed()
    {
        var result = RootCauseEngine.Compute(Input(Controls(("Vega Pro (medium)", "Anthropic", "vega-pro", 85))));

        Assert.Equal(RootCauseGrades.Moderate, result.ControlLimit);
        var control = Assert.Single(result.RecommendedRuns, r => r.Kind == RootCauseRunKinds.Control);
        Assert.DoesNotContain(31, control.TestedModelConfigurationIds);
        Assert.DoesNotContain(SubjectConfigId, control.TestedModelConfigurationIds);
    }

    [Fact]
    public void WithTwoControlsAcrossProviders_ThereIsNoCap_NoControlRun_AndAnItemLevelTable()
    {
        var result = RootCauseEngine.Compute(Input(Controls(
            ("Vega Pro (medium)", "Anthropic", "vega-pro", 85),
            ("Nova Flash (high)", "Google", "nova-flash", 80))));

        Assert.Null(result.ControlLimit);
        Assert.DoesNotContain(result.RecommendedRuns, r => r.Kind == RootCauseRunKinds.Control);
        Assert.Equal(2, result.Controls.Count);
        Assert.Equal(12, result.ControlItems.Count);
        // Q4, Q8 and Q12: the subject scored 30, every control 35.
        Assert.Equal(new[] { 4, 8, 12 }, result.ControlItems.Where(i => i.InstrumentSuspect).Select(i => i.Number));
        Assert.NotNull(result.ControlDeficitMean);
    }

    [Fact]
    public void TheRecommendedRuns_AreOrderedChatBenchmarkModelSelection_AndNumberedFromOne()
    {
        var result = RootCauseEngine.Compute(Input(Controls()));

        var orders = result.RecommendedRuns.Select(r => RootCausePurposes.Order(r.Purpose)).ToList();
        Assert.Equal(orders.OrderBy(o => o).ToList(), orders);
        Assert.Equal(Enumerable.Range(1, result.RecommendedRuns.Count), result.RecommendedRuns.Select(r => r.Number));
        Assert.All(result.RecommendedRuns, r => Assert.False(string.IsNullOrWhiteSpace(r.Decides)));
    }

    [Fact]
    public void AStandaloneSubject_GetsChartRowsForItselfAndEachDifficultyBand()
    {
        var result = RootCauseEngine.Compute(Input(Controls()));

        Assert.Equal(new[] { "model", "band", "band", "band" }, result.PointsAtRisk.Select(r => r.Kind));
        Assert.Equal(4, result.SpeedRows.Count);
        Assert.Equal(4, result.CostRows.Count);
        Assert.All(result.SpeedRows.Concat(result.CostRows).Concat(result.PointsAtRisk), row =>
        {
            Assert.Null(row.UnavailableReason);
            Assert.Equal(row.Total!.Value, row.Segments.Sum(s => s.Value), 9);
        });
        Assert.Equal(RootCauseAxes.All, result.Axes.Select(a => a.Axis));
    }

    [Fact]
    public void TheSameInput_GivesTheSameResult()
    {
        string first = BenchmarkReportJson.Serialize(RootCauseEngine.Compute(Input(Controls(("Vega Pro (medium)", "Anthropic", "vega-pro", 85)))));
        string second = BenchmarkReportJson.Serialize(RootCauseEngine.Compute(Input(Controls(("Vega Pro (medium)", "Anthropic", "vega-pro", 85)))));

        Assert.Equal(first, second);
    }

    [Fact]
    public void ARunWithoutTelemetry_HasUnavailableSpeedRows_NeverZero()
    {
        var input = Input(Controls());
        var subject = input.Models[0];
        var bare = new RootCauseModelInput
        {
            Token = subject.Token,
            Label = subject.Label,
            Provider = subject.Provider,
            ModelId = subject.ModelId,
            IsSubject = true,
            RunIds = subject.RunIds,
            QualityIndex = subject.QualityIndex,
            Dimensions = subject.Dimensions,
            Evidence = new RootCauseModelEvidence { Answers = subject.Evidence.Answers, RunCount = 1, RunsPerSuite = 1 }
        };
        var result = RootCauseEngine.Compute(new RootCauseInput
        {
            Scope = input.Scope,
            Origin = input.Origin,
            ComputedAtUtc = input.ComputedAtUtc,
            Models = new[] { bare },
            Controls = input.Controls,
            Catalog = input.Catalog,
            Questions = input.Questions
        });

        Assert.All(result.SpeedRows, r => Assert.NotNull(r.UnavailableReason));
        Assert.All(result.CostRows, r => Assert.NotNull(r.UnavailableReason));
        Assert.All(result.SpeedRows, r => Assert.Empty(r.Segments));
    }
}
