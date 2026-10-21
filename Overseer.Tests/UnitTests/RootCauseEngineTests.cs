namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services.Benchmarking;
using Overseer.Services.Benchmarking.RootCause;
using Overseer.Services.Telemetry;
using Xunit;

/// <summary>
/// The root-cause engine end to end for a run-completion subject: the control limit with no, one and two
/// controls, the control run it proposes, the order of the recommended runs and the runs a replicate folds
/// in, the item-level control table, the chart rows and determinism; and for three compared models: which
/// differences are decomposed, the research questions and their signals, the model profiles, the hypothesis
/// labels and references, the grader rules and family gaps, nulls without a family test, the thinking level as a
/// configuration, price effects, a cost axis they alone fire on, their facts kept from the writer and the cost ratio
/// at equal prices of each decomposed pair, each check's noun-phrase summary, the next runs a standing null, a thinking
/// hypothesis and a style hypothesis propose, and reasoning a provider does not report separately, which splits a
/// pair into input and output when only one side reports it; the panel
/// indices a caller gives the engine input, the bare fact displays, the hypotheses each research question takes and
/// in which order, the style hypothesis once per model, and the known facts' quota.
/// </summary>
public class RootCauseEngineTests
{
    private const long SubjectConfigId = 11;

    private static RootCauseAnswerRecord Answer(int q, double quality, string band, bool noTool = false, bool critical = false, double completenessGap = 0)
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
            Completeness = quality - completenessGap,
            Conciseness = 80,
            Readability = 80,
            DurationMs = 20000 + 500 * q,
            ModelTimeMs = 15000 + 400 * q,
            ToolTimeMs = 3000,
            ModelCalls = 2 + q % 3
        };

    private static RootCauseItem SpeedItem(int q, string band, bool ttftLeads = false)
        => new()
        {
            Key = "q" + q,
            Band = band,
            TtftMs = ttftLeads ? 9000 + 100 * q : 3000 + 100 * q,
            DecodeMs = ttftLeads ? 3000 + 50 * q : 10000 + 200 * q,
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

    private static RootCauseModelInput Subject(bool ttftLeads = false)
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
                SpeedItems = questions.Select(q => SpeedItem(q, BandOf(q), ttftLeads)).ToList(),
                CostItems = questions.Select(q => CostItem(q, BandOf(q))).ToList()
            }
        };
    }

    /// <summary>
    /// One of three compared models: its Intelligence Index, its time and cost per question scaled by the
    /// factors, Completeness ten points below its Accuracy, and every fourth question answered without tools.
    /// With <paramref name="quality"/>, each answer's score instead, none critical, the answers
    /// <paramref name="noTool"/> picks without tools and the rest with them, its Completeness
    /// <paramref name="completenessGap"/> below its Accuracy. The provider, model id and thinking level default to
    /// the letter's and <c>high</c>; with <paramref name="panel"/> it was graded by a panel, with the member and
    /// reference-reader indices given. Without <paramref name="reasoningReported"/> its provider counts thinking
    /// inside output (the reasoning cost moves into the output slot). <paramref name="price"/> scales its cost
    /// without its tokens. <paramref name="outputPrice"/> is its card's list output price per million tokens,
    /// <paramref name="releaseDate"/> its release date and <paramref name="dimensions"/> its dimension means in place of
    /// the default ones; without <paramref name="intervals"/> its index has no 95% interval.
    /// </summary>
    private static RootCauseModelInput Compared(string letter, int number, double index, double speed, double cost,
        Func<int, double>? quality = null, double completenessGap = 0, double? cleared = null, Func<int, bool>? noTool = null,
        string? provider = null, string? modelId = null, string? thinkingLevel = null,
        bool panel = false, double? memberA = null, double? memberB = null, double? reader = null,
        bool reasoningReported = true, double price = 1.0, bool styleConflict = false,
        double? outputPrice = null, string? releaseDate = null, IReadOnlyList<double?>? dimensions = null, bool intervals = true)
    {
        var questions = Enumerable.Range(1, 12).ToList();
        var answers = quality != null
            ? questions.Select(q => Answer(q, quality(q), BandOf(q), noTool: noTool?.Invoke(q) ?? false, completenessGap: completenessGap)).ToList()
            : questions.Select(q => Answer(q, q % 4 == 0 ? 30 : index - 10 + 2 * (q % 5), BandOf(q), noTool: q % 4 == 0, critical: letter == "C" && q == 3)).ToList();

        // Output and reasoning as one slot, as the evidence records a provider that does not report reasoning.
        double[] Slots(double[] values) => reasoningReported ? values : new[] { values[0], values[1], values[2], values[3] + values[4], 0 };

        return new RootCauseModelInput
        {
            Token = letter,
            Letter = letter,
            Label = "Model label " + letter,
            Provider = provider ?? (reasoningReported ? "Provider " + letter : "Anthropic"),
            ModelId = modelId ?? "model-" + letter.ToLowerInvariant(),
            ThinkingLevel = thinkingLevel ?? "high",
            ReleaseDate = releaseDate,
            EntryKey = "run:" + (600 + number),
            RunIds = new long[] { 600 + number },
            QualityIndex = index,
            QualityLower = intervals ? index - 5 : null,
            QualityUpper = intervals ? index + 5 : null,
            VerificationClearedIndex = cleared,
            Dimensions = dimensions ?? new double?[] { index, index - 10, index - 3, index - 2 },
            ModelTimeP50Ms = 18000 * speed,
            CostPerQuestionUsd = 0.0183 * cost * price,
            CostPerRunUsd = 0.22 * cost * price,
            StyleConflict = styleConflict,
            MemberAIndex = memberA,
            MemberBIndex = memberB,
            ReferenceReaderIndex = reader,
            Evidence = new RootCauseModelEvidence
            {
                Answers = answers,
                RunCount = 1,
                RunsPerSuite = 1,
                Panel = panel,
                Priced = true,
                PricesPerMillion = new double?[] { null, null, null, outputPrice, null },
                ReasoningReported = reasoningReported,
                CriticalErrorsConfirmed = true,
                MeanMinutesPerRun = 9 * speed,
                Settings = new RootCauseRunSettings
                {
                    SuiteId = 7,
                    SuiteName = "Core knowledge",
                    TestedModelConfigurationId = 200 + number,
                    ScoringProfileId = 3,
                    VerboseMode = false,
                    AllowSourceCodeReferences = true,
                    AssessorModelConfigurationId = 21
                },
                SpeedItems = questions.Select(q =>
                {
                    var item = SpeedItem(q, BandOf(q));
                    return new RootCauseItem
                    {
                        Key = item.Key,
                        Band = item.Band,
                        TtftMs = item.TtftMs * speed,
                        DecodeMs = item.DecodeMs * speed * (1 + 0.02 * (q % 3)),
                        ModelTimeMs = item.ModelTimeMs * speed * (1 + 0.01 * (q % 3)),
                        DurationMs = item.DurationMs * speed,
                        ToolMs = item.ToolMs,
                        WaitMs = item.WaitMs,
                        Calls = item.Calls,
                        OutputTokens = item.OutputTokens * speed
                    };
                }).ToList(),
                CostItems = questions.Select(q =>
                {
                    var item = CostItem(q, BandOf(q));
                    return new RootCauseItem
                    {
                        Key = item.Key,
                        Band = item.Band,
                        Tokens = Slots(item.Tokens.Select(t => t * cost).ToArray()),
                        Cost = Slots(item.Cost.Select((c, k) => c * cost * price * (1 + 0.01 * (q % 4) * k)).ToArray())
                    };
                }).ToList()
            }
        };
    }

    /// <summary>
    /// Three compared models: A best and the reference, B slowest, C lowest and most expensive. The paired
    /// speed test of A and B is established, of A and C not; no pair's cost is tested.
    /// </summary>
    private static RootCauseInput ComparisonInput(params RootCauseModelInput[] models) => new()
    {
        Scope = RootCauseEngine.ComparisonScope,
        Origin = RootCauseOrigins.Comparison,
        ComputedAtUtc = new DateTime(2026, 10, 15, 8, 0, 0, DateTimeKind.Utc),
        PricingBasis = "As run",
        PricingBasisKind = "AsRun",
        Models = models.Length > 0
            ? models
            : new[]
            {
                Compared("A", 1, 85, 1.0, 1.0),
                Compared("B", 2, 75, 1.6, 1.2),
                Compared("C", 3, 60, 0.9, 2.0)
            },
        ReferenceToken = "A",
        PairTests = new[]
        {
            SpeedTest("A", "B", established: true),
            SpeedTest("A", "C", established: false)
        },
        Catalog = Catalog(),
        Questions = Enumerable.Range(1, 12).Select(q => new RootCauseQuestionRef { Number = q, Reference = "Q" + q, ItemKey = "q" + q + "@1" }).ToList()
    };

    /// <summary><paramref name="input"/> with <paramref name="tests"/> as its paired tests.</summary>
    private static RootCauseInput WithPairTests(RootCauseInput input, params RootCausePairTest[] tests) => new()
    {
        Scope = input.Scope,
        Origin = input.Origin,
        ComputedAtUtc = input.ComputedAtUtc,
        PricingBasis = input.PricingBasis,
        PricingBasisKind = input.PricingBasisKind,
        Models = input.Models,
        ReferenceToken = input.ReferenceToken,
        PairTests = tests,
        Catalog = input.Catalog,
        Questions = input.Questions
    };

    /// <summary>
    /// <paramref name="input"/> with <paramref name="graders"/> as its grading roles, <paramref name="familyGaps"/> as its
    /// panel diagnostics' grader-family gaps and, when given, <paramref name="reference"/> as its reference model.
    /// </summary>
    private static RootCauseInput WithGraders(RootCauseInput input, IReadOnlyList<(string Role, string Provider)>? graders = null,
        IReadOnlyList<RootCauseFamilyGap>? familyGaps = null, string? reference = null) => new()
    {
        Scope = input.Scope,
        Origin = input.Origin,
        ComputedAtUtc = input.ComputedAtUtc,
        PricingBasis = input.PricingBasis,
        PricingBasisKind = input.PricingBasisKind,
        Models = input.Models,
        ReferenceToken = reference ?? input.ReferenceToken,
        PairTests = input.PairTests,
        Graders = graders ?? input.Graders,
        FamilyGaps = familyGaps ?? input.FamilyGaps,
        Catalog = input.Catalog,
        Questions = input.Questions
    };

    /// <summary><paramref name="input"/> with, where given, <paramref name="catalog"/> as its catalog and <paramref name="releaseDates"/> as its release dates.</summary>
    private static RootCauseInput With(RootCauseInput input, RootCauseCatalog? catalog = null, IReadOnlyDictionary<string, string>? releaseDates = null) => new()
    {
        Scope = input.Scope,
        Origin = input.Origin,
        ComputedAtUtc = input.ComputedAtUtc,
        PricingBasis = input.PricingBasis,
        PricingBasisKind = input.PricingBasisKind,
        Models = input.Models,
        ReferenceToken = input.ReferenceToken,
        PairTests = input.PairTests,
        Graders = input.Graders,
        FamilyGaps = input.FamilyGaps,
        Catalog = catalog ?? input.Catalog,
        Questions = input.Questions,
        ReleaseDates = releaseDates ?? input.ReleaseDates
    };

    /// <summary>A grader-family gap of the members' own provider pair, Northwind minus Contoso, with its interaction contrast.</summary>
    private static RootCauseFamilyGap MemberPairGap(double gapA, double gapB, double contrast, double contrastLower, double contrastUpper) => new()
    {
        Provider1 = "Northwind",
        Provider2 = "Contoso",
        PairedQuestionCount = 12,
        IsMemberProviderPair = true,
        GapA = new RootCauseGapEstimate { Value = gapA, Lower = gapA - 3, Upper = gapA + 3 },
        GapB = new RootCauseGapEstimate { Value = gapB, Lower = gapB - 3, Upper = gapB + 3 },
        GapPanel = new RootCauseGapEstimate { Value = (gapA + gapB) / 2, Lower = (gapA + gapB) / 2 - 2, Upper = (gapA + gapB) / 2 + 2 },
        Contrast = new RootCauseGapEstimate { Value = contrast, Lower = contrastLower, Upper = contrastUpper }
    };

    private static readonly (string Role, string Provider)[] NorthwindContosoPanel =
    {
        (BenchmarkReportFacts.PanelMemberARole, "Northwind"),
        (BenchmarkReportFacts.PanelMemberBRole, "Contoso"),
        (BenchmarkReportFacts.ReferenceReaderRole, "Contoso"),
        (BenchmarkReportFacts.ClaimVerifierRole, "Northwind")
    };

    /// <summary>An established paired Intelligence Index test of the pair, favoring <paramref name="favors"/> (<c>First</c> or <c>Second</c>).</summary>
    private static RootCausePairTest IntelligenceTest(string first, string second, string favors) => new()
    {
        First = first,
        Second = second,
        Measure = BenchmarkPairedTests.IntelligenceMeasure,
        Family = BenchmarkComparisonReportFacts.ReferenceFamilyName,
        Effect = favors == "First" ? 3 : -3,
        Lower = favors == "First" ? 1 : -5,
        Upper = favors == "First" ? 5 : -1,
        Established = true,
        AdjustedPValue = 0.01,
        FamilySize = 2,
        Favors = favors,
        FactKey = "pair." + first + "." + second + ".intelligence.reference"
    };

    private static RootCausePairTest SpeedTest(string first, string second, bool established) => new()
    {
        First = first,
        Second = second,
        Measure = BenchmarkPairedTests.SpeedMeasure,
        Family = BenchmarkComparisonReportFacts.ReferenceFamilyName,
        Effect = established ? 0.6 : 0.95,
        Lower = established ? 0.5 : 0.8,
        Upper = established ? 0.7 : 1.1,
        Established = established,
        AdjustedPValue = established ? 0.01 : 0.4,
        FamilySize = 2,
        Favors = established ? "First" : "None",
        FactKey = "pair." + first + "." + second + ".speed.reference"
    };

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

    [Theory]
    [InlineData("Vega Pro", "high", "Vega Pro (high)")]
    [InlineData("Vega Pro (high)", "high", "Vega Pro (high)")]
    [InlineData("Vega Pro (HIGH)", "high", "Vega Pro (HIGH)")]
    [InlineData(" Vega Pro ", null, "Vega Pro")]
    [InlineData("vega-pro", " medium ", "vega-pro (medium)")]
    public void AControlsLabel_NamesItsThinkingLevelOnce(string label, string? level, string expected)
    {
        var control = new RootCauseControl { ModelLabel = label, ThinkingLevel = level };

        Assert.Equal(expected, control.LabelWithLevel());
    }

    [Fact]
    public void AControlsModelFact_CarriesItsThinkingLevelOnce()
    {
        var set = Controls(
            ("Vega Pro", "Anthropic", "vega-pro", 85),
            ("Nova Flash (high)", "Google", "nova-flash", 80));
        set.Controls[0].ThinkingLevel = "medium";
        set.Controls[1].ThinkingLevel = "high";
        var result = RootCauseEngine.Compute(Input(set));

        var facts = new BenchmarkReportFacts.FactList();
        RootCauseFacts.Add(facts, result, t => "Model " + t);
        var byKey = facts.Sorted().ToDictionary(f => f.Key, StringComparer.Ordinal);
        Assert.Equal("Vega Pro (medium)", byKey["rca.controls.1.model"].Display);
        Assert.Equal("Nova Flash (high)", byKey["rca.controls.2.model"].Display);
    }

    [Fact]
    public void TheRecommendedRuns_AreOrderedChatBenchmarkModelSelection_AndNumberedFromOne()
    {
        var result = RootCauseEngine.Compute(Input(Controls()));

        var orders = result.RecommendedRuns.Select(r => RootCausePurposes.Order(r.Purpose)).ToList();
        Assert.Equal(orders.OrderBy(o => o).ToList(), orders);
        Assert.Equal(Enumerable.Range(1, result.RecommendedRuns.Count), result.RecommendedRuns.Select(r => r.Number));
        Assert.All(result.RecommendedRuns, r => Assert.False(string.IsNullOrWhiteSpace(r.Decides)));

        // No time-of-day repeat or thinking-level pair is left whose models a replicate of three runs covers.
        foreach (var replicate in result.RecommendedRuns.Where(r => r.Kind == RootCauseRunKinds.Replicate && r.RunsPerModel >= RootCauseNextRuns.ReplicateRuns))
        {
            Assert.DoesNotContain(result.RecommendedRuns, r => r.Kind is RootCauseRunKinds.TimeStratum or RootCauseRunKinds.ThinkingPair
                && r.TestedModelConfigurationIds.Count > 0
                && r.TestedModelConfigurationIds.All(replicate.TestedModelConfigurationIds.Contains));
        }
    }

    [Fact]
    public void EveryRecommendedRun_HasAPurposeFact_WordedAsTheRunTableWordsIt()
    {
        var result = RootCauseEngine.Compute(Input(Controls()));
        Assert.NotEmpty(result.RecommendedRuns);

        var facts = new BenchmarkReportFacts.FactList();
        RootCauseFacts.Add(facts, result, t => "Model " + t);
        var byKey = facts.Sorted().ToDictionary(f => f.Key, StringComparer.Ordinal);
        foreach (var run in result.RecommendedRuns)
        {
            string key = "rca.run." + run.Number.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".purpose";
            Assert.Equal(RootCausePurposes.Text(run.Purpose), byKey[key].Display);
            Assert.True(BenchmarkReportFactLabels.TryLabel(key, out _), "No label for " + key);
        }
        Assert.Equal("C, the Overseer chat", RootCausePurposes.Text(RootCausePurposes.Chat));
        Assert.Equal("B, the benchmark", RootCausePurposes.Text(RootCausePurposes.Benchmark));
        Assert.Equal("M, model selection", RootCausePurposes.Text(RootCausePurposes.ModelSelection));
    }

    [Fact]
    public void AReplicate_FoldsTheTimeOfDayAndThinkingPairRunsItCovers()
    {
        // The subject's time is mostly the wait before output (WS1, a time-of-day repeat), and it answered every
        // fourth question without tools (WQ1, a replicate): the replicate covers the subject, so it folds the repeat in,
        // and is then one pass launched once per time of day, since a batch or a series would run back to back.
        var input = Input(Controls(("Vega Pro (medium)", "Anthropic", "vega-pro", 85), ("Nova Flash (high)", "Google", "nova-flash", 80)));
        var withTtft = new RootCauseInput
        {
            Scope = input.Scope,
            Origin = input.Origin,
            ComputedAtUtc = input.ComputedAtUtc,
            PricingBasis = input.PricingBasis,
            PricingBasisKind = input.PricingBasisKind,
            Models = new[] { Subject(ttftLeads: true) },
            Controls = input.Controls,
            Catalog = input.Catalog,
            Questions = input.Questions
        };

        var result = RootCauseEngine.Compute(withTtft);

        var ws1 = Assert.Single(result.Hypotheses, h => h.Id == "WS1");
        var replicate = Assert.Single(result.RecommendedRuns, r => r.Kind == RootCauseRunKinds.Replicate);
        Assert.DoesNotContain(result.RecommendedRuns, r => r.Kind == RootCauseRunKinds.TimeStratum);
        Assert.Contains(ws1.Key, replicate.HypothesisIds);
        Assert.Equal(replicate.Number, ws1.NextRunNumber);
        Assert.Equal("Launch it 3 times, each at a different time of day; Number of Runs 3, Runs per model 3 or a model batch would run them back to back",
            replicate.When);
        Assert.Equal(RootCauseNextRuns.TimeOfDayWhen, replicate.When);
        Assert.Equal(RootCauseNextRuns.ReplicateRuns, replicate.Launches);
        Assert.Equal("Single", replicate.RunMode);
        Assert.Equal(1, replicate.RunsPerModel);
        Assert.Equal(1, replicate.RunCount);
        Assert.Contains("Launch this setup 3 times, each at a different time of day.", replicate.Notes);
        Assert.Equal(RootCausePurposes.Chat, replicate.Purpose);
        Assert.Equal(1, replicate.Number);
        Assert.Equal(Enumerable.Range(1, result.RecommendedRuns.Count), result.RecommendedRuns.Select(r => r.Number));

        // The estimate is one launch's; its text states all three.
        Assert.Equal(0.22, replicate.EstimatedCostUsd!.Value, 9);
        Assert.Equal(9, replicate.EstimatedMinutes!.Value, 9);
        Assert.Equal("about $0.66 and 27 minutes over 3 launches", RootCauseNextRuns.EstimateText(replicate));

        string summary = RootCauseNextRuns.Summary(replicate);
        Assert.StartsWith("Run 1, a replicate of ", summary);
        Assert.EndsWith("(about $0.66 and 27 minutes over 3 launches)", summary);
        Assert.DoesNotContain(replicate.Decides, summary);

        // Every other run is launched once.
        Assert.All(result.RecommendedRuns.Where(r => r != replicate), r => Assert.Equal(1, r.Launches));
    }

    [Fact]
    public void OnlyEstablishedDifferences_AreDecomposed()
    {
        var input = ComparisonInput();
        var result = RootCauseEngine.Compute(input);

        Assert.NotEmpty(result.SpeedPairs);
        foreach (var pair in result.SpeedPairs)
        {
            Assert.NotNull(pair.Difference);
            var tests = input.PairTests.Where(t => t.Measure == BenchmarkPairedTests.SpeedMeasure
                && ((t.First == pair.First && t.Second == pair.Second) || (t.First == pair.Second && t.Second == pair.First))).ToList();
            bool established = tests.Any(t => t.Established && t.Favors != "None")
                || (!tests.Any(t => t.NotTestedReason == null) && (pair.DifferenceLower > 0 || pair.DifferenceUpper < 0));
            Assert.Equal(!established, pair.NotEstablished);
            Assert.Equal(established, pair.Components.Count > 0);
            if (!established)
            {
                Assert.Equal(RootCauseDecomposition.NotEstablishedReason, pair.UnavailableReason);
                Assert.NotNull(pair.DifferenceLower);
            }
        }
        Assert.Contains(result.SpeedPairs, p => p.First == "A" && p.Second == "B" && !p.NotEstablished);
        Assert.Contains(result.SpeedPairs, p => p.First == "A" && p.Second == "C" && p.NotEstablished);

        // Speed and cost pair rules fire only on a pair decomposed on their axis.
        static HashSet<string> Decomposed(IEnumerable<RootCausePairDecomposition> pairs)
            => pairs.Where(p => !p.NotEstablished && p.UnavailableReason == null).Select(p => p.First + "|" + p.Second).ToHashSet(StringComparer.Ordinal);
        var speed = Decomposed(result.SpeedPairs);
        var cost = Decomposed(result.CostPairs);
        Assert.All(result.Hypotheses.Where(h => h.Kind == "pair" && (h.Id.StartsWith("HS", StringComparison.Ordinal) || h.Id.StartsWith("HC", StringComparison.Ordinal))), h =>
        {
            var models = h.Models.OrderBy(m => m, StringComparer.Ordinal).ToList();
            Assert.Contains(models[0] + "|" + models[1], h.Id.StartsWith("HS", StringComparison.Ordinal) ? speed : cost);
        });
        Assert.DoesNotContain(result.Hypotheses, h => h.Id.StartsWith("HS", StringComparison.Ordinal) && h.Models.Contains("A") && h.Models.Contains("C"));
    }

    [Fact]
    public void TheFourQuestions_NameTheAxisExtremes_AndTheirBestGrade()
    {
        var result = RootCauseEngine.Compute(ComparisonInput());

        Assert.Equal(new[] { 1, 2, 3, 4 }, result.Questions.Select(q => q.Number));
        Assert.Equal(new[] { "A", "C", "B", "C" }, result.Questions.Select(q => q.Token));
        // A (80 to 90) and B (70 to 80) touch at 80, which counts as overlapping; C (55 to 65) is clear of B.
        Assert.Equal("Why did Model A score highest, though its lead over Model B is not established?", result.Questions[0].Text);
        Assert.Equal("Why did Model C score lowest?", result.Questions[1].Text);
        Assert.Equal("Why is Model B slowest?", result.Questions[2].Text);
        Assert.Equal("Why is Model C the most expensive?", result.Questions[3].Text);
        Assert.Equal(new bool?[] { false, true, null, null }, result.Questions.Select(q => q.OrderEstablished));

        var facts = new BenchmarkReportFacts.FactList();
        RootCauseFacts.Add(facts, result, t => "Model " + t);
        var byKey = facts.Sorted().ToDictionary(f => f.Key, StringComparer.Ordinal);
        int low = RootCauseGrades.Rank(RootCauseGrades.Low);
        foreach (var question in result.Questions)
        {
            // Why highest: the pairs where the model is the better side and the gap lies in the pair itself (HQ4, HQ8).
            // Why lowest, slowest or most expensive: the pairs where it is the worse side, and its own within-model hypotheses.
            var about = result.Hypotheses
                .Where(h => h.Axis == question.Axis && h.Id != "HQ3")
                .Where(h => question.Number == 1
                    ? h.Kind == "pair" && h.Models[1] == question.Token && h.Id is "HQ4" or "HQ8"
                    : (h.Kind == "pair" && h.Models[0] == question.Token) || (h.Kind == "within" && h.Models[0] == question.Token))
                .ToList();
            var comparative = about.Where(h => h.Kind == "pair").ToList();
            var within = about.Where(h => h.Kind == "within").ToList();
            Assert.All(question.HypothesisKeys, k => Assert.DoesNotContain(k, result.Hypotheses.Where(h => h.Kind == "null").Select(h => h.Key)));
            Assert.Equal(about.Select(h => h.Key).OrderBy(k => k, StringComparer.Ordinal), question.HypothesisKeys.OrderBy(k => k, StringComparer.Ordinal));
            Assert.NotNull(question.ComparativeKeys);
            Assert.NotNull(question.WithinKeys);
            Assert.Equal(comparative.Select(h => h.Key).OrderBy(k => k, StringComparer.Ordinal), question.ComparativeKeys!.OrderBy(k => k, StringComparer.Ordinal));
            Assert.Equal(within.Select(h => h.Key).OrderBy(k => k, StringComparer.Ordinal), question.WithinKeys!.OrderBy(k => k, StringComparer.Ordinal));
            Assert.Equal(question.ComparativeKeys.Concat(question.WithinKeys), question.HypothesisKeys);

            // Questions 3 and 4 order their comparative hypotheses by the compared model first: no model of the three
            // has a sibling at another level, so the reference (A), then the fastest (C) or the cheapest (A), then B.
            // Within one compared model, and on questions 1 and 2, best grade first.
            string[]? partners = question.Number switch { 3 => new[] { "A", "C" }, 4 => new[] { "A", "B" }, _ => null };
            int Preference(string key)
            {
                var h = about.Single(x => x.Key == key);
                return partners == null ? 0 : Array.IndexOf(partners, h.Models[0] == question.Token ? h.Models[1] : h.Models[0]);
            }
            var order = question.ComparativeKeys
                .Select(k => about.Single(h => h.Key == k))
                .Select(h => (Partner: Preference(h.Key), Price: h.PriceEffect ? 1 : 0, Rank: -RootCauseGrades.Rank(h.Grade)))
                .ToList();
            Assert.Equal(order.OrderBy(o => o.Partner).ThenBy(o => o.Price).ThenBy(o => o.Rank).ToList(), order);
            var withinRanks = question.WithinKeys.Select(k => RootCauseGrades.Rank(about.Single(h => h.Key == k).Grade)).ToList();
            Assert.Equal(withinRanks.OrderByDescending(r => r).ToList(), withinRanks);

            // The first comparative grade at Low or higher in that order; else the best within-model one, capped at Low.
            var first = question.ComparativeKeys.Select(k => about.Single(h => h.Key == k)).FirstOrDefault(h => RootCauseGrades.Rank(h.Grade) >= low);
            string? bestComparative = first?.Grade;
            string? bestWithin = within.Where(h => RootCauseGrades.Rank(h.Grade) >= low)
                .OrderByDescending(h => RootCauseGrades.Rank(h.Grade)).Select(h => h.Grade).FirstOrDefault();
            string? best = bestComparative ?? (bestWithin != null ? RootCauseGrades.Capped(bestWithin, RootCauseGrades.Low) : null);
            Assert.Equal(best, question.Grade);
            Assert.Equal(bestComparative == null && within.Count > 0, question.WithinOnly);
            Assert.Equal(question.Number == 4 && first?.PriceEffect == true, question.PriceCard);

            string prefix = "rca.question." + question.Number + ".";
            Assert.Equal(question.Text, byKey[prefix + "text"].Display);
            Assert.Equal("Model " + question.Token, byKey[prefix + "model"].Display);
            Assert.Equal(question.PriceCard ? RootCauseGrades.PriceCardWord : RootCauseGrades.WordWithGrade(best), byKey[prefix + "status"].Display);
            if (question.Number <= 2)
            {
                Assert.Equal("B", question.Neighbor);
                Assert.Equal("Model B", byKey[prefix + "neighbor"].Display);
                Assert.Equal("B", byKey[prefix + "neighbor"].Value!.GetValue<string>());
            }
            else
            {
                Assert.Null(question.Neighbor);
                Assert.False(byKey.ContainsKey(prefix + "neighbor"));
            }
            Assert.True(byKey.ContainsKey(prefix + "hypotheses"));
            Assert.True(byKey.ContainsKey(prefix + "signals"));
            Assert.Equal(question.WithinOnly, byKey[prefix + "withinOnly"].Value!.GetValue<bool>());
            if (question.Number <= 2) Assert.Equal(question.OrderEstablished!.Value, byKey[prefix + "orderEstablished"].Value!.GetValue<bool>());
            else Assert.False(byKey.ContainsKey(prefix + "orderEstablished"));
            Assert.All(byKey.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)), k => Assert.True(RootCauseFacts.WriterHidden(k), k));
        }

        // Model A scored highest: no hypothesis about where it lost, none about the runner-up's own failures, and only
        // strengths as its signals.
        var highest = result.Questions[0];
        Assert.All(highest.HypothesisKeys, k =>
        {
            var h = result.Hypotheses.Single(x => x.Key == k);
            Assert.Equal("pair", h.Kind);
            Assert.Equal("A", h.Models[1]);
            Assert.Contains(h.Id, new[] { "HQ4", "HQ8" });
        });
        Assert.DoesNotContain(highest.Signals, s => s.StartsWith("lost points on", StringComparison.Ordinal));
        Assert.Contains(result.Questions[1].HypothesisKeys, k => result.Hypotheses.Single(h => h.Key == k).Kind == "within");

        Assert.Equal("Established (High)", RootCauseGrades.WordWithGrade(RootCauseGrades.High));
        Assert.Equal("Probable (Moderate)", RootCauseGrades.WordWithGrade(RootCauseGrades.Moderate));
        Assert.Equal("Suggestive (Low)", RootCauseGrades.WordWithGrade(RootCauseGrades.Low));
        Assert.Equal("Unknown", RootCauseGrades.WordWithGrade(null));

        // One model alone has no question to answer.
        Assert.Empty(RootCauseEngine.Compute(Input(Controls())).Questions);
    }

    [Fact]
    public void EveryModel_HasAProfile_WithNoCausalWord()
    {
        var input = ComparisonInput();
        var result = RootCauseEngine.Compute(input);

        Assert.Equal(input.Models.Select(m => m.Token), result.Profiles.Select(p => p.Token));
        foreach (var profile in result.Profiles)
        {
            Assert.True(profile.Strengths.Count <= RootCauseProfiles.MaxStrengths);
            Assert.True(profile.Weaknesses.Count <= RootCauseProfiles.MaxWeaknesses);
            foreach (var entry in profile.Strengths.Concat(profile.Weaknesses).Concat(profile.Flags))
            {
                Assert.False(string.IsNullOrWhiteSpace(entry.Text));
                Assert.False(string.IsNullOrWhiteSpace(entry.Short));
                Assert.Empty(RootCauseClaimSupport.CausalPhrases(entry.Text));
                Assert.Empty(RootCauseClaimSupport.CausalPhrases(entry.Short));
            }
        }

        var a = result.Profiles.Single(p => p.Token == "A");
        Assert.Contains(a.Strengths, s => s.Text == "first of 3 models on Accuracy");
        Assert.Contains(a.Weaknesses, w => w.Kind == RootCauseProfiles.WeakestDimension && w.Text == "Completeness, 10.0 points below Accuracy");
        Assert.Contains(a.Weaknesses, w => w.Kind == RootCauseProfiles.LostPointsOn && w.Text.StartsWith("answers without tools: ", StringComparison.Ordinal));

        var c = result.Profiles.Single(p => p.Token == "C");
        Assert.Contains(c.Flags, f => f.Kind == RootCauseProfiles.CriticalErrors && f.Text == "1 critical error (Q3)");
        Assert.Contains(c.Weaknesses, w => w.Kind == RootCauseProfiles.LowestRank && w.Text == "last of 3 models on Accuracy");

        var facts = new BenchmarkReportFacts.FactList();
        RootCauseFacts.Add(facts, result, t => "Model " + t);
        var keys = facts.Sorted().Select(f => f.Key).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("rca.profile.A.lostPointsOn", keys);
        Assert.Contains("rca.profile.A.weakestDimension", keys);
        Assert.Contains("rca.profile.A.strength.1", keys);
        Assert.Contains("rca.profile.C.flag.1", keys);
        Assert.Contains("rca.profile.C.flags", keys);
    }

    [Fact]
    public void EveryKeptHypothesis_HasALabelOfAtMostTenWords()
    {
        foreach (var input in new[] { ComparisonInput(), Input(Controls()), Input(Controls(("Vega Pro (medium)", "Anthropic", "vega-pro", 85))) })
        {
            var result = RootCauseEngine.Compute(input);
            Assert.NotEmpty(result.Hypotheses);

            var facts = new BenchmarkReportFacts.FactList();
            RootCauseFacts.Add(facts, result, t => t == RootCauseEngine.SubjectToken ? "Orion Max (high)" : "Model " + t);
            var byKey = facts.Sorted().ToDictionary(f => f.Key, StringComparer.Ordinal);
            foreach (var hypothesis in result.Hypotheses)
            {
                Assert.False(string.IsNullOrWhiteSpace(hypothesis.Label), hypothesis.Key);
                Assert.True(RootCauseLabels.Words(hypothesis.Label).Length <= RootCauseLabels.MaxWords, hypothesis.Label);
                Assert.Empty(RootCauseClaimSupport.CausalPhrases(hypothesis.Label));
                Assert.Equal(hypothesis.Label, byKey["rca.hypothesis." + hypothesis.Key + ".label"].Display);
            }
        }

        var critical = new RootCauseHypothesis { Id = "HQ2", Kind = "pair", Models = { "F", "E" } };
        Assert.Equal("Model F's critical errors, compared with Model E", RootCauseLabels.Of(critical, t => "Model " + t));
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
    public void TheEngineVersion_IsFourteen()
    {
        Assert.Equal(14, RootCauseEngine.Version);
    }

    [Fact]
    public void AKnownFact_AboutCacheReads_TakesAPluralVerb()
    {
        var input = Input(Controls());
        // Cache reads lead the subject's cost per question.
        foreach (var item in input.Models[0].Evidence.CostItems) item.Cost[1] = 0.03;

        var result = RootCauseEngine.Compute(input);

        var fact = Assert.Single(result.KnownFacts, f => f.Text.EndsWith("'s cost per question.", StringComparison.Ordinal));
        Assert.StartsWith("Cache reads are ", fact.Text);
        Assert.DoesNotContain(result.KnownFacts, f => f.Text.Contains("Cache reads is ", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("cache reads", " are ")]
    [InlineData("cache writes", " are ")]
    [InlineData("own waits and retries", " are ")]
    [InlineData("output tokens", " are ")]
    [InlineData("visible output", " is ")]
    [InlineData("uncached input", " is ")]
    [InlineData("wait before output", " is ")]
    [InlineData("number of model calls", " is ")]
    public void AComponentLabel_TakesTheVerbOfItsNumber(string label, string verb)
    {
        Assert.Equal(verb, RootCauseKnownFacts.Verb(label, " is ", " are "));
    }

    [Fact]
    public void ConfigDifferences_NameEveryKeyButTheModel_WithBothValues()
    {
        var a = new RootCauseModelInput { Token = "A", Provider = "OpenAI", ModelId = "gpt-x", ThinkingLevel = "medium" };
        var b = new RootCauseModelInput { Token = "B", Provider = "OpenAI", ModelId = "gpt-y", ThinkingLevel = "high", ReasoningMode = "standard" };

        var parts = RootCauseContext.ConfigDifferenceParts(a, b);

        Assert.Equal(new[] { "thinking level", "reasoning mode" }, parts.Select(p => p.Name));
        Assert.Equal(("medium", "high"), (parts[0].First, parts[0].Second));
        Assert.Equal(("not set", "standard"), (parts[1].First, parts[1].Second));
        Assert.Equal(new[] { "thinking level (medium vs high)", "reasoning mode (not set vs standard)" }, RootCauseContext.ConfigDifferences(a, b));

        var other = new RootCauseModelInput { Token = "C", Provider = "Anthropic", ModelId = "claude-x", ThinkingLevel = "medium" };
        Assert.Equal(new[] { "provider" }, RootCauseContext.ConfigDifferences(a, other));
    }

    [Fact]
    public void ARuleWithSeveralShares_GetsItsShareSpanAndCount()
    {
        var result = new RootCauseResult
        {
            Hypotheses =
            {
                new RootCauseHypothesis { Id = "HQ1", Number = 1, Key = "HQ1.1", Kind = "pair", Axis = RootCauseAxes.Intelligence, Share = 0.45 },
                new RootCauseHypothesis { Id = "HQ1", Number = 2, Key = "HQ1.2", Kind = "pair", Axis = RootCauseAxes.Intelligence, Share = 0.48 },
                new RootCauseHypothesis { Id = "HQ1", Number = 3, Key = "HQ1.3", Kind = "pair", Axis = RootCauseAxes.Intelligence, Share = 0.29 },
                new RootCauseHypothesis { Id = "HS1", Number = 1, Key = "HS1.1", Kind = "pair", Axis = RootCauseAxes.Speed, Share = 0.52 },
                new RootCauseHypothesis { Id = "WC1", Number = 1, Key = "WC1.1", Kind = "within", Axis = RootCauseAxes.Cost, Share = 0.69 },
                new RootCauseHypothesis { Id = "WC1", Number = 2, Key = "WC1.2", Kind = "within", Axis = RootCauseAxes.Cost, Share = 0.54 },
                new RootCauseHypothesis { Id = "H0-Q", Number = 1, Key = "H0-Q.1", Kind = "null", Axis = RootCauseAxes.Intelligence },
                new RootCauseHypothesis { Id = "H0-Q", Number = 2, Key = "H0-Q.2", Kind = "null", Axis = RootCauseAxes.Intelligence },
            }
        };
        var facts = new BenchmarkReportFacts.FactList();

        RootCauseFacts.Add(facts, result, t => "Model " + t);

        var sorted = facts.Sorted();
        Assert.Equal("29% to 48% of each gap", Assert.Single(sorted, f => f.Key == "rca.rule.HQ1.shareSpan").Display);
        Assert.Equal("3 pairs", Assert.Single(sorted, f => f.Key == "rca.rule.HQ1.count").Display);
        Assert.DoesNotContain(sorted, f => f.Key.StartsWith("rca.rule.HS1.", StringComparison.Ordinal));
        Assert.Equal("54% to 69% of each model's own cost", Assert.Single(sorted, f => f.Key == "rca.rule.WC1.shareSpan").Display);
        Assert.Equal("2 models", Assert.Single(sorted, f => f.Key == "rca.rule.WC1.count").Display);
        Assert.DoesNotContain(sorted, f => f.Key.StartsWith("rca.rule.H0", StringComparison.Ordinal));
    }

    [Fact]
    public void EveryCheck_HasANounPhraseSummary_WhoseFactIsNoSentence()
    {
        // Without a configured model of another provider, the control run becomes a check, and so does any other
        // run the catalog cannot fill.
        var input = Input(Controls());
        var bare = new RootCauseInput
        {
            Scope = input.Scope,
            Origin = input.Origin,
            ComputedAtUtc = input.ComputedAtUtc,
            PricingBasis = input.PricingBasis,
            PricingBasisKind = input.PricingBasisKind,
            Models = input.Models,
            Controls = input.Controls,
            Catalog = new RootCauseCatalog(),
            Questions = input.Questions
        };

        var result = RootCauseEngine.Compute(bare);

        Assert.NotEmpty(result.Checks);
        Assert.Contains(result.Checks, c => c.Summary.EndsWith(", a new configuration of a recommended model of another provider, for a control run", StringComparison.Ordinal));
        var facts = new BenchmarkReportFacts.FactList();
        RootCauseFacts.Add(facts, result, t => "Model " + t);
        var byKey = facts.Sorted().ToDictionary(f => f.Key, StringComparer.Ordinal);
        foreach (var check in result.Checks)
        {
            string key = "rca.check." + check.Number.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Assert.StartsWith("Check " + check.Number.ToString(System.Globalization.CultureInfo.InvariantCulture) + ", ", check.Summary, StringComparison.Ordinal);
            Assert.Equal(check.Text, byKey[key].Display);
            Assert.Equal(check.Summary, byKey[key + ".summary"].Display);
            Assert.True(RootCauseClaimSupport.IsSentenceShaped(key));
            Assert.False(RootCauseClaimSupport.IsSentenceShaped(key + ".summary"));
            Assert.True(BenchmarkReportFactLabels.TryLabel(key + ".summary", out _), "No label for " + key + ".summary");
        }

        // A check stored before the summaries were added is named by its number alone.
        Assert.Equal("Check 4", RootCauseNextRuns.Summary(new RootCauseCheck { Number = 4, Kind = "rubric", Text = "Review the rubric." }));
    }

    [Fact]
    public void ACostAxisWhosePriceEffectsCoverLargerGaps_HasNoPriceNote_AndEachDecomposedPairHasACostRatioAtEqualPrices()
    {
        // Model B reads and writes 30% more tokens than Model A at its prices; Model C as many tokens at three times the price.
        var result = RootCauseEngine.Compute(ComparisonInput(
            Compared("A", 1, 85, 1.0, 1.0),
            Compared("B", 2, 75, 1.2, 1.3),
            Compared("C", 3, 70, 1.1, 1.0, price: 3.0)));

        var cost = result.Axes.Single(a => a.Axis == RootCauseAxes.Cost);
        var best = Assert.Single(result.Hypotheses, h => h.Key == cost.BestHypothesis);
        Assert.False(best.PriceEffect);
        Assert.Single(result.Hypotheses, h => h.PriceEffect && h.Models.SequenceEqual(new[] { "C", "A" }));

        var facts = new BenchmarkReportFacts.FactList();
        RootCauseFacts.Add(facts, result, t => "Model " + t);
        var sorted = facts.Sorted().ToList();
        var byKey = sorted.ToDictionary(f => f.Key, StringComparer.Ordinal);
        Assert.DoesNotContain(sorted, f => f.Key.EndsWith(".priceNote", StringComparison.Ordinal));
        Assert.False(BenchmarkReportFactLabels.TryLabel("rca.axis.cost.priceNote", out _));

        // Every decomposed cost pair states its cost ratio at equal prices and whether it is split into input and output.
        var decomposed = result.CostPairs.Where(p => p.Components.Count > 0).ToList();
        Assert.NotEmpty(decomposed);
        foreach (var pair in decomposed)
        {
            string prefix = "rca.cost." + pair.First + "." + pair.Second + ".";
            Assert.False(pair.Grouped);
            var ratio = byKey[prefix + "volumeRatio"];
            Assert.Equal(pair.VolumeRatio!.Value, ratio.Value!.GetValue<double>(), 12);
            Assert.Matches(@"^\d+(\.\d)? times$", ratio.Display);
            Assert.Equal(RootCauseFacts.NotGroupedDisplay, byKey[prefix + "grouped"].Display);
            Assert.False(byKey[prefix + "grouped"].Value!.GetValue<bool>());
            foreach (string key in new[] { prefix + "volumeRatio", prefix + "grouped" })
            {
                Assert.True(BenchmarkReportFactLabels.TryLabel(key, out string label), "No label for " + key);
                Assert.DoesNotContain(".", label.TrimEnd('.'));
                Assert.False(RootCauseClaimSupport.IsSentenceShaped(key));
            }
        }
        Assert.All(result.CostPairs.Where(p => p.Components.Count == 0), p => Assert.Null(p.VolumeRatio));

        Assert.Equal("2.3 times", RootCauseFacts.RatioDisplay(2.25));
        Assert.Equal("66 times", RootCauseFacts.RatioDisplay(66.4));
        Assert.Equal("10 times", RootCauseFacts.RatioDisplay(9.96));
    }

    [Fact]
    public void ATie_AtTheTopOrTheBottom_AsksNoQuestionAboutIt()
    {
        // Two models at one Intelligence Index: neither is highest nor lowest.
        var two = RootCauseEngine.Compute(ComparisonInput(
            Compared("A", 1, 72, 1.0, 1.0),
            Compared("B", 2, 72, 1.6, 1.2)));
        Assert.Equal(new[] { 3, 4 }, two.Questions.Select(q => q.Number));

        // A tie at the top only: the lowest model is still asked about.
        var top = RootCauseEngine.Compute(ComparisonInput(
            Compared("A", 1, 80, 1.0, 1.0),
            Compared("B", 2, 80, 1.6, 1.2),
            Compared("C", 3, 60, 0.9, 2.0)));
        Assert.DoesNotContain(top.Questions, q => q.Number == 1);
        Assert.Equal("C", Assert.Single(top.Questions, q => q.Number == 2).Token);

        var facts = new BenchmarkReportFacts.FactList();
        RootCauseFacts.Add(facts, top, t => "Model " + t);
        Assert.DoesNotContain(facts.Sorted(), f => f.Key.StartsWith("rca.question.1.", StringComparison.Ordinal));
    }

    [Fact]
    public void OverlappingIntervals_AskTheQuestion_SayingTheOrderIsNotEstablished()
    {
        // A (80 to 90) overlaps B (77 to 87); C (67 to 77) touches B at 77.
        var result = RootCauseEngine.Compute(ComparisonInput(
            Compared("A", 1, 85, 1.0, 1.0),
            Compared("B", 2, 82, 1.6, 1.2),
            Compared("C", 3, 72, 0.9, 2.0)));

        var highest = Assert.Single(result.Questions, q => q.Number == 1);
        Assert.Equal("Why did Model A score highest, though its lead over Model B is not established?", highest.Text);
        Assert.False(highest.OrderEstablished);
        var lowest = Assert.Single(result.Questions, q => q.Number == 2);
        Assert.Equal("Why did Model C score lowest, though its gap to Model B is not established?", lowest.Text);
        Assert.False(lowest.OrderEstablished);
    }

    [Fact]
    public void ClearIntervals_OrAnEstablishedPairedTestFavoringTheModel_AskThePlainQuestion()
    {
        // A (90 to 100) is clear of B (70 to 80), and C (50 to 60) of B.
        var clear = RootCauseEngine.Compute(ComparisonInput(
            Compared("A", 1, 95, 1.0, 1.0),
            Compared("B", 2, 75, 1.6, 1.2),
            Compared("C", 3, 55, 0.9, 2.0)));
        Assert.Equal("Why did Model A score highest?", clear.Questions.Single(q => q.Number == 1).Text);
        Assert.True(clear.Questions.Single(q => q.Number == 1).OrderEstablished);
        Assert.Equal("Why did Model C score lowest?", clear.Questions.Single(q => q.Number == 2).Text);
        Assert.True(clear.Questions.Single(q => q.Number == 2).OrderEstablished);

        // A (80 to 90) overlaps B (77 to 87), but a paired test of the two establishes A ahead.
        var models = new[]
        {
            Compared("A", 1, 85, 1.0, 1.0),
            Compared("B", 2, 82, 1.6, 1.2),
            Compared("C", 3, 60, 0.9, 2.0)
        };
        var tested = RootCauseEngine.Compute(WithPairTests(ComparisonInput(models), IntelligenceTest("A", "B", "First")));
        var highest = tested.Questions.Single(q => q.Number == 1);
        Assert.Equal("Why did Model A score highest?", highest.Text);
        Assert.True(highest.OrderEstablished);

        // A test that favors the other model does not establish the order.
        var against = RootCauseEngine.Compute(WithPairTests(ComparisonInput(models), IntelligenceTest("A", "B", "Second")));
        Assert.False(against.Questions.Single(q => q.Number == 1).OrderEstablished);
    }

    [Fact]
    public void ASlowestModel_AnsweredOnlyByItsOwnBreakdown_IsWithinOnly_AndGradedLowAtMost()
    {
        // No speed difference is established, so no speed pair rule fires: only B's own within-model rules remain.
        var result = RootCauseEngine.Compute(WithPairTests(ComparisonInput(),
            SpeedTest("A", "B", established: false),
            SpeedTest("A", "C", established: false),
            SpeedTest("B", "C", established: false)));

        var slowest = Assert.Single(result.Questions, q => q.Number == 3);
        Assert.Equal("B", slowest.Token);
        Assert.Empty(slowest.ComparativeKeys!);
        Assert.NotEmpty(slowest.WithinKeys!);
        Assert.All(slowest.WithinKeys!, k => Assert.Equal("within", result.Hypotheses.Single(h => h.Key == k).Kind));
        Assert.True(slowest.WithinOnly);
        Assert.Null(slowest.OrderEstablished);
        Assert.True(RootCauseGrades.Rank(slowest.Grade) <= RootCauseGrades.Rank(RootCauseGrades.Low));

        // The comparison has pairs, so within-model hypotheses alone cannot set the speed axis' status.
        var speed = result.Axes.Single(a => a.Axis == RootCauseAxes.Speed);
        Assert.Equal(RootCauseStatuses.NotEstablished, speed.Status);
        Assert.Equal(RootCauseHypotheses.WithinOnlyAxisReason, speed.Reason);
        Assert.Null(speed.BestHypothesis);
    }

    [Fact]
    public void ASlowestModel_WithAComparativeHypothesis_TakesItsGrade()
    {
        // The speed difference of A and B is established, so a speed pair rule fires with B the worse side.
        var result = RootCauseEngine.Compute(ComparisonInput());

        var slowest = Assert.Single(result.Questions, q => q.Number == 3);
        Assert.NotEmpty(slowest.ComparativeKeys!);
        var comparative = slowest.ComparativeKeys!.Select(k => result.Hypotheses.Single(h => h.Key == k)).ToList();
        Assert.All(comparative, h =>
        {
            Assert.Equal("pair", h.Kind);
            Assert.StartsWith("HS", h.Id, StringComparison.Ordinal);
            Assert.Equal("B", h.Models[0]);
        });
        var best = comparative.First(h => RootCauseGrades.Rank(h.Grade) >= RootCauseGrades.Rank(RootCauseGrades.Low));
        Assert.Equal(best.Grade, slowest.Grade);
        Assert.False(slowest.WithinOnly);

        // No model has a sibling at another level, so the reference leads, before the fastest model.
        Assert.Equal("A", comparative[0].Models[1]);
        Assert.Equal("A", best.Models[1]);
    }

    [Fact]
    public void HQ1_IsNotSupported_WhenAnswersWithoutToolsPutNoMoreOfTheWorseModelsPointsAtRisk()
    {
        // B answers every third question without tools at 65, its median, and the rest with tools at 40 or 100: its
        // answers without tools score lower on average and are more frequent than A's, yet put none of its points at risk.
        var input = ComparisonInput(
            Compared("A", 1, 80, 1.0, 1.0, q => 80),
            Compared("B", 2, 60, 1.6, 1.2, q => q % 3 == 0 ? 65 : q % 3 == 1 ? 40 : 100, noTool: q => q % 3 == 0));
        var ctx = new RootCauseContext(input);

        var hq1 = Assert.Single(RootCauseHypotheses.PairRules(ctx, new RootCausePair("A", "B"), new Dictionary<string, bool>()), d => d.Id == "HQ1");

        Assert.True(hq1.NotSupported);
        Assert.Equal(new[] { "Answers without tools put no more of Model B's index points at risk than of Model A's." }, hq1.Against);
        Assert.Equal(0.0, hq1.Explains!.Value, 9);
        Assert.Equal(RootCauseGrades.NotSupported, RootCauseHypotheses.Grade(hq1, ctx.Name).Grade);
    }

    /// <summary>Questions 9 to 12 (the Advanced band) score 40 below the rest.</summary>
    private static double HardAdvanced(int q, double index) => q >= 9 ? index - 40 : index + (q % 3);

    /// <summary>Questions 1 to 4 (the Simple band) score 40 below the rest.</summary>
    private static double HardSimple(int q, double index) => q <= 4 ? index - 40 : index + (q % 3);

    [Fact]
    public void ABandHardForEveryModel_IsNoModelsCause_AndIsNoted()
    {
        var result = RootCauseEngine.Compute(ComparisonInput(
            Compared("A", 1, 85, 1.0, 1.0, q => HardAdvanced(q, 85)),
            Compared("B", 2, 75, 1.6, 1.2, q => HardAdvanced(q, 75)),
            Compared("C", 3, 60, 0.9, 2.0, q => HardAdvanced(q, 60))));

        Assert.DoesNotContain(result.Hypotheses, h => h.Id == "WQ2");
        Assert.Contains("The advanced band holds most of the points at risk for 3 of 3 models; a contrast within one model there reflects the questions, not the model.", result.Notes);
    }

    [Fact]
    public void ABandHardForOneModelAlone_FiresWithThePeersMedian()
    {
        var result = RootCauseEngine.Compute(ComparisonInput(
            Compared("A", 1, 85, 1.0, 1.0, q => HardSimple(q, 85)),
            Compared("B", 2, 75, 1.6, 1.2, q => HardSimple(q, 75)),
            Compared("C", 3, 60, 0.9, 2.0, q => HardAdvanced(q, 60))));

        var wq2 = Assert.Single(result.Hypotheses, h => h.Id == "WQ2" && h.Models[0] == "C");
        Assert.Contains("The other models' median share there is 0%.", wq2.For);
        Assert.DoesNotContain(result.Hypotheses, h => h.Id == "WQ2" && h.Models[0] != "C" && h.ShortStatement.Contains("advanced", StringComparison.Ordinal));
    }

    [Fact]
    public void TheCatchAllSegment_IsNamedByTheDimensionThatLostMost()
    {
        // Model B's lowest dimension is Conciseness, so no dimension is the lowest of every model.
        var result = RootCauseEngine.Compute(ComparisonInput(
            Compared("A", 1, 85, 1.0, 1.0, q => HardAdvanced(q, 85), completenessGap: 15),
            Compared("B", 2, 75, 1.6, 1.2, dimensions: new double?[] { 75, 72, 60, 73 }),
            Compared("C", 3, 60, 0.9, 2.0)));

        Assert.Null(result.SharedLowestDimension);
        var lost = Assert.Single(result.Profiles.Single(p => p.Token == "A").Weaknesses, w => w.Kind == RootCauseProfiles.LostPointsOn);
        Assert.StartsWith("low-scoring answers, mostly Completeness: ", lost.Text, StringComparison.Ordinal);
        Assert.StartsWith("mostly Completeness, ", lost.Short, StringComparison.Ordinal);
        Assert.Empty(RootCauseClaimSupport.CausalPhrases(lost.Text));
    }

    [Fact]
    public void TheSharedLowestDimension_IsLeftOutOfTheCatchAllSegment_AndTheOtherDimensionIsKept()
    {
        // Completeness is every model's lowest dimension. Model A's low-scoring answers lose mostly Completeness, Model B's
        // mostly Accuracy.
        var result = RootCauseEngine.Compute(ComparisonInput(
            Compared("A", 1, 85, 1.0, 1.0, q => HardAdvanced(q, 85), completenessGap: 15),
            Compared("B", 2, 75, 1.6, 1.2, q => HardAdvanced(q, 75), completenessGap: -15),
            Compared("C", 3, 60, 0.9, 2.0)));

        Assert.Equal("Completeness", result.SharedLowestDimension);
        var a = Assert.Single(result.Profiles.Single(p => p.Token == "A").Weaknesses, w => w.Kind == RootCauseProfiles.LostPointsOn);
        Assert.StartsWith("low-scoring answers: ", a.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("mostly", a.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("mostly", a.Short, StringComparison.Ordinal);
        Assert.EndsWith(" points", a.Short, StringComparison.Ordinal);

        var b = Assert.Single(result.Profiles.Single(p => p.Token == "B").Weaknesses, w => w.Kind == RootCauseProfiles.LostPointsOn);
        Assert.StartsWith("low-scoring answers, mostly Accuracy: ", b.Text, StringComparison.Ordinal);
        Assert.StartsWith("mostly Accuracy, ", b.Short, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(61.3, true)]
    [InlineData(60.4, false)]
    [InlineData(58.0, false)]
    public void HQ9_FiresOnlyWhenTheVerifierWouldRaiseTheIndex(double cleared, bool fires)
    {
        var result = RootCauseEngine.Compute(ComparisonInput(
            Compared("A", 1, 85, 1.0, 1.0),
            Compared("B", 2, 75, 1.6, 1.2),
            Compared("C", 3, 60, 0.9, 2.0, cleared: cleared)));

        var hq9 = result.Hypotheses.Where(h => h.Id == "HQ9" && h.Models[0] == "C").ToList();
        if (!fires)
        {
            Assert.Empty(hq9);
            return;
        }
        Assert.NotEmpty(hq9);
        Assert.All(hq9, h =>
        {
            Assert.NotEqual(RootCauseGrades.NotSupported, h.Grade);
            Assert.Contains("Model C's verification-cleared Overall Index is 61.3 against its published 60.0.", h.For);
        });
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

    // --- Graders (HQ4 and the grader-family gap) ---------------------------------------------------------

    private static RootCauseDraft PairDraft(RootCauseInput input, string id)
    {
        var ctx = new RootCauseContext(input);
        return Assert.Single(RootCauseHypotheses.PairRules(ctx, new RootCausePair("A", "B"), new Dictionary<string, bool>()), d => d.Id == id);
    }

    [Fact]
    public void HQ4_IsRuledOut_WhenBothMembersAndTheReferenceReaderOrderThePairAlike_AndOnlyScoringGradersCount()
    {
        var input = WithGraders(ComparisonInput(
            Compared("A", 1, 85, 1.0, 1.0, provider: "Northwind", panel: true, memberA: 86, memberB: 84, reader: 85),
            Compared("B", 2, 75, 1.6, 1.2, provider: "Contoso", panel: true, memberA: 74, memberB: 76, reader: 75)), NorthwindContosoPanel);

        var hq4 = PairDraft(input, "HQ4");

        Assert.True(hq4.NotSupported);
        Assert.Equal("Both panel members and the reference reader order the two alike.", hq4.Against[0]);
        Assert.DoesNotContain("Both panel members order the two alike.", hq4.Against);
        Assert.Contains("Panel member A shares a provider with Model A.", hq4.For);
        Assert.Contains("Panel member B shares a provider with Model B.", hq4.For);
        Assert.DoesNotContain(hq4.For, line => line.StartsWith("Reference reader", StringComparison.Ordinal) || line.StartsWith("Claim verifier", StringComparison.Ordinal));
        var graded = RootCauseHypotheses.Grade(hq4, t => "Model " + t);
        Assert.Equal(RootCauseGrades.NotSupported, graded.Grade);
        Assert.Equal(RootCauseHypotheses.RefutedReason + ": both panel members and the reference reader order the two alike", graded.GradeReasons[0]);

        // Without the reference reader, the members' agreement is evidence against, not a refutation.
        var open = PairDraft(WithGraders(ComparisonInput(
            Compared("A", 1, 85, 1.0, 1.0, provider: "Northwind", panel: true, memberA: 86, memberB: 84),
            Compared("B", 2, 75, 1.6, 1.2, provider: "Contoso", panel: true, memberA: 74, memberB: 76)), NorthwindContosoPanel), "HQ4");
        Assert.False(open.NotSupported);
        Assert.Contains("Both panel members order the two alike.", open.Against);
    }

    [Fact]
    public void HQ4_IsQuantifiedByTheMembersInteractionContrast_AndTheFamilyGapsAreFacts()
    {
        var models = new[]
        {
            Compared("A", 1, 85, 1.0, 1.0, provider: "Northwind"),
            Compared("B", 2, 75, 1.6, 1.2, provider: "Contoso")
        };
        var input = WithGraders(ComparisonInput(models), NorthwindContosoPanel, new[] { MemberPairGap(6, -2, 8, 4, 12) });

        // A contrast of 8 moves the panel's gap by about 4 of the pair's 10 points.
        var hq4 = PairDraft(input, "HQ4");
        Assert.True(hq4.Quantified);
        Assert.True(hq4.Established);
        Assert.False(hq4.NotSupported);
        Assert.Equal(0.4, hq4.Share!.Value, 9);
        Assert.Equal(0.2, hq4.ShareLower!.Value, 9);
        Assert.Equal(0.6, hq4.ShareUpper!.Value, 9);
        Assert.Contains(hq4.For, line => line.Contains("an interaction contrast of +8.0 points (95% interval +4.0 to +12.0)", StringComparison.Ordinal));

        // A contrast whose interval includes zero measures the hypothesis without establishing it.
        var weak = PairDraft(WithGraders(ComparisonInput(models), NorthwindContosoPanel, new[] { MemberPairGap(3, 1, 2, -1, 5) }), "HQ4");
        Assert.True(weak.Quantified);
        Assert.False(weak.Established);
        Assert.Equal("the interaction contrast's 95% interval includes zero", weak.EstablishedText);

        var result = RootCauseEngine.Compute(input);
        var facts = new BenchmarkReportFacts.FactList();
        RootCauseFacts.Add(facts, result, t => "Model " + t);
        var byKey = facts.Sorted().ToDictionary(f => f.Key, StringComparer.Ordinal);
        Assert.Equal(1, byKey["rca.graders.familyGap.count"].Value!.GetValue<int>());
        Assert.Equal("Northwind − Contoso", byKey["rca.graders.familyGap.1.providers"].Display);
        Assert.Equal("+6.0 points (95% interval +3.0 to +9.0)", byKey["rca.graders.familyGap.1.underA"].Display);
        Assert.Equal("-2.0 points (95% interval -5.0 to +1.0)", byKey["rca.graders.familyGap.1.underB"].Display);
        Assert.Equal("+8.0 points (95% interval +4.0 to +12.0)", byKey["rca.graders.familyGap.1.contrast"].Display);
        Assert.True(byKey["rca.graders.familyGap.1.notable"].Value!.GetValue<bool>());
        Assert.All(byKey.Keys.Where(k => k.StartsWith("rca.graders.", StringComparison.Ordinal)), key =>
        {
            Assert.True(BenchmarkReportFactLabels.TryLabel(key, out string label), "No label for " + key);
            Assert.DoesNotContain(".", label.TrimEnd('.'));
            Assert.True(RootCauseFacts.WriterHidden(key), key);
        });

        // Too few paired questions: the estimates are unavailable and nothing is notable.
        var thin = MemberPairGap(6, -2, 8, 4, 12);
        thin.InsufficientData = true;
        var sparse = new BenchmarkReportFacts.FactList();
        RootCauseFacts.Add(sparse, RootCauseEngine.Compute(WithGraders(ComparisonInput(models), NorthwindContosoPanel, new[] { thin })), t => "Model " + t);
        var sparseByKey = sparse.Sorted().ToDictionary(f => f.Key, StringComparer.Ordinal);
        Assert.False(sparseByKey["rca.graders.familyGap.1.contrast"].Available);
        Assert.Equal(RootCauseFacts.FamilyGapInsufficientReason, sparseByKey["rca.graders.familyGap.1.contrast"].UnavailableReason);
        Assert.False(sparseByKey["rca.graders.familyGap.1.notable"].Value!.GetValue<bool>());

        // Without panel diagnostics there are no family-gap facts.
        var none = new BenchmarkReportFacts.FactList();
        RootCauseFacts.Add(none, RootCauseEngine.Compute(ComparisonInput(models)), t => "Model " + t);
        Assert.DoesNotContain(none.Sorted(), f => f.Key.StartsWith("rca.graders.", StringComparison.Ordinal));
    }

    // --- Nulls without a family test ---------------------------------------------------------------------

    [Fact]
    public void H0Q_WithoutAFamilyTest_IsRejectedByThePairedBootstrap_AndItsGradeReadsAsANull()
    {
        var result = RootCauseEngine.Compute(ComparisonInput(
            Compared("A", 1, 85, 1.0, 1.0, q => 85 + q % 3),
            Compared("B", 2, 60, 1.6, 1.2, q => 60 + q % 3)));

        var h0 = Assert.Single(result.Hypotheses, h => h.Id == "H0-Q");
        Assert.Equal(RootCauseGrades.NotSupported, h0.Grade);
        Assert.Contains("No family test covers this pair; the 95% paired-bootstrap interval of the per-question difference excludes zero (not adjusted).", h0.Against);
        Assert.Null(h0.NextRunNumber);

        var facts = new BenchmarkReportFacts.FactList();
        RootCauseFacts.Add(facts, result, t => "Model " + t);
        var byKey = facts.Sorted().ToDictionary(f => f.Key, StringComparer.Ordinal);
        Assert.Equal("Rejected — a real difference (Not supported)", byKey["rca.hypothesis." + h0.Key + ".grade"].Display);
        Assert.Equal(RootCauseGrades.NotSupported, byKey["rca.hypothesis." + h0.Key + ".grade"].Value!.GetValue<string>());
        Assert.Equal(h0.Key, byKey["rca.hypothesis." + h0.Key + ".ref"].Display);
        Assert.Equal("no intelligence difference", RootCauseLabels.Topic(h0));
        Assert.All(result.Hypotheses, h =>
        {
            // The reference is the bare id; the topic stays for the label.
            Assert.Equal(h.Key, byKey["rca.hypothesis." + h.Key + ".ref"].Display);
            if (h.Id != "H0-W") Assert.Contains(RootCauseLabels.Topic(h), h.Label, StringComparison.Ordinal);
            Assert.DoesNotContain("Model ", RootCauseLabels.Topic(h), StringComparison.Ordinal);
            Assert.True(BenchmarkReportFactLabels.TryLabel("rca.hypothesis." + h.Key + ".ref", out _));
        });
    }

    // --- Thinking level and configuration ----------------------------------------------------------------

    [Fact]
    public void TheSameModelAtTwoLevels_HasNoThinkingConfound_AndSaysTheLevelIsTheOnlyDifference()
    {
        var input = ComparisonInput(
            Compared("A", 1, 85, 1.0, 1.0, provider: "Northwind", modelId: "orion", thinkingLevel: "high"),
            Compared("B", 2, 75, 1.6, 1.2, provider: "Northwind", modelId: "orion", thinkingLevel: "max"));
        var ctx = new RootCauseContext(input);

        var drafts = RootCauseHypotheses.PairRules(ctx, new RootCausePair("A", "B"), new Dictionary<string, bool>())
            .Where(d => d.Axis is RootCauseAxes.Speed or RootCauseAxes.Cost)
            .ToList();

        Assert.Contains(drafts, d => d.Axis == RootCauseAxes.Speed);
        Assert.Contains(drafts, d => d.Axis == RootCauseAxes.Cost);
        Assert.All(drafts, d =>
        {
            Assert.DoesNotContain(d.Confounds, c => c.StartsWith("thinking level", StringComparison.Ordinal));
            var configuration = Assert.Single(d.Robustness, r => r.Name == RootCauseHypotheses.Configuration);
            Assert.Equal(RootCauseCheckStatuses.Passed, configuration.Status);
            Assert.Equal("Beyond the model, only its thinking level differs (max vs high), which is part of its configuration.", configuration.Detail);
            Assert.Contains("Model B and Model A are the same model at max and high thinking; the thinking level is the only configuration difference.", d.For);
        });
    }

    [Fact]
    public void TwoModelsAtDifferentLevels_PassTheConfigurationCheck_WithADetailNamingTheLevels()
    {
        var input = ComparisonInput(
            Compared("A", 1, 85, 1.0, 1.0, thinkingLevel: "high"),
            Compared("B", 2, 75, 1.6, 1.2, thinkingLevel: "max"));
        var ctx = new RootCauseContext(input);

        var drafts = RootCauseHypotheses.PairRules(ctx, new RootCausePair("A", "B"), new Dictionary<string, bool>())
            .Where(d => d.Robustness.Any(r => r.Name == RootCauseHypotheses.Configuration))
            .ToList();

        Assert.NotEmpty(drafts);
        Assert.All(drafts, d =>
        {
            var configuration = Assert.Single(d.Robustness, r => r.Name == RootCauseHypotheses.Configuration);
            Assert.Equal(RootCauseCheckStatuses.Passed, configuration.Status);
            Assert.Equal("Beyond the model, only its thinking level differs (max vs high), which is part of its configuration.", configuration.Detail);
            Assert.DoesNotContain(d.For, line => line.Contains("same model", StringComparison.Ordinal));
        });
        Assert.Equal("No configuration key differs beyond the model.", RootCauseHypotheses.ConfigurationCheck(Array.Empty<string>()).Detail);
    }

    // --- Price effects (HC2) -----------------------------------------------------------------------------

    [Fact]
    public void APriceEffect_HasNoReplicatesCheck_IsSettledByTheCards_AndIsExemptFromTheGradeCap()
    {
        // Model B reads and writes as many tokens as Model A at twice the price.
        var input = ComparisonInput(
            Compared("A", 1, 85, 1.0, 1.0),
            Compared("B", 2, 75, 1.6, 1.0, price: 2.0));

        var hc2 = PairDraft(input, "HC2");
        Assert.DoesNotContain(hc2.Robustness, r => r.Name == RootCauseHypotheses.Replicates);
        Assert.Equal("configuration", hc2.CheckKind);
        Assert.Equal("Compare the two models' price cards; the price effect follows from them.", hc2.CheckText);
        Assert.Equal("a comparison of the two models' price cards", hc2.CheckSummary);
        Assert.Null(hc2.NextRunKind);

        // No run can refute a share that follows from the price cards.
        Assert.True(hc2.PriceEffect);
        Assert.Equal(RootCauseHypotheses.PriceEffectRefutedIf, hc2.RefutedIf);
        Assert.Equal("not testable by a run: the share follows from the price cards and these runs' token counts", hc2.ConfirmIf);

        var result = RootCauseEngine.Compute(input);
        var kept = Assert.Single(result.Hypotheses, h => h.Id == "HC2");
        Assert.Null(kept.NextRunNumber);
        Assert.Equal(RootCauseHypotheses.PriceEffectRefutedIf, kept.RefutedIf);
        if (kept.Grade != RootCauseGrades.High)
        {
            var check = Assert.Single(result.Checks, c => c.Number == kept.CheckNumber);
            Assert.Equal("configuration", check.Kind);
            Assert.Equal("Check " + check.Number.ToString(System.Globalization.CultureInfo.InvariantCulture) + ", a comparison of the two models' price cards", check.Summary);
        }
        Assert.EndsWith("until a second run of each suite is made, apart from a price effect, which follows from the price cards.", result.GradeCap);
        Assert.EndsWith("until a second run of each suite is made.", RootCauseEngine.Compute(ComparisonInput()).GradeCap);
    }

    [Fact]
    public void APriceOnlyCostAxis_IsNotEstablished_AndItsMostExpensiveModelIsKnownFromThePriceCards()
    {
        // Model B reads and writes as many tokens as Model A at twice the price: only the price effect fires on cost.
        var result = RootCauseEngine.Compute(ComparisonInput(
            Compared("A", 1, 85, 1.0, 1.0),
            Compared("B", 2, 75, 1.6, 1.0, price: 2.0)));

        var hc2 = Assert.Single(result.Hypotheses, h => h.Id == "HC2");
        Assert.True(hc2.PriceEffect);
        Assert.NotEqual(RootCauseGrades.NotSupported, hc2.Grade);
        Assert.DoesNotContain(result.Hypotheses, h => h.Axis == RootCauseAxes.Cost && h.Kind == "pair" && !h.PriceEffect);
        Assert.Equal(RootCauseGrades.PriceCardWordWithGrade(hc2.Grade), RootCauseGrades.HypothesisWordWithGrade(hc2));
        Assert.StartsWith("Known from the price cards (", RootCauseGrades.HypothesisWordWithGrade(hc2), StringComparison.Ordinal);

        var cost = result.Axes.Single(a => a.Axis == RootCauseAxes.Cost);
        Assert.Equal(RootCauseStatuses.NotEstablished, cost.Status);
        Assert.Equal(RootCauseHypotheses.PriceOnlyAxisReason, cost.Reason);
        Assert.Equal("The runs show no cost mechanism beyond the price cards: every hypothesis that fired on this axis is a price-card difference.", cost.Reason);
        Assert.Equal(hc2.Key, cost.BestHypothesis);

        // A price effect sorts after every other non-null hypothesis of its axis.
        var costOrder = result.Hypotheses.Where(h => h.Axis == RootCauseAxes.Cost && h.Kind != "null").ToList();
        Assert.Same(hc2, costOrder[^1]);

        var mostExpensive = Assert.Single(result.Questions, q => q.Number == 4);
        Assert.Equal("B", mostExpensive.Token);
        Assert.True(mostExpensive.PriceCard);
        Assert.Equal(hc2.Grade, mostExpensive.Grade);
        Assert.All(result.Questions.Where(q => q.Number != 4), q => Assert.False(q.PriceCard));

        var facts = new BenchmarkReportFacts.FactList();
        RootCauseFacts.Add(facts, result, t => "Model " + t);
        var sorted = facts.Sorted().ToList();
        var byKey = sorted.ToDictionary(f => f.Key, StringComparer.Ordinal);
        string prefix = "rca.hypothesis." + hc2.Key + ".";
        Assert.Equal(RootCauseGrades.PriceCardWordWithGrade(hc2.Grade), byKey[prefix + "grade"].Display);
        Assert.Equal(hc2.Grade, byKey[prefix + "grade"].Value!.GetValue<string>());
        Assert.True(byKey[prefix + "priceCard"].Value!.GetValue<bool>());
        Assert.Equal(RootCauseFacts.PriceCardDisplay, byKey[prefix + "priceCard"].Display);
        Assert.DoesNotContain(sorted, f => f.Key.EndsWith(".priceCard", StringComparison.Ordinal) && !f.Key.StartsWith(prefix, StringComparison.Ordinal));
        Assert.Equal(RootCauseGrades.PriceCardWord, byKey["rca.question.4.status"].Display);
        Assert.True(byKey["rca.axis.cost.best"].Available);
        Assert.Equal(hc2.Key, byKey["rca.axis.cost.best"].Value!.GetValue<string>());

        // The price effect supports a causal word but no certainty word, and the cost axis is not established.
        var support = RootCauseClaimSupport.From(new BenchmarkReportFactSheet { Facts = sorted });
        Assert.True(support.SupportsCausal(prefix + "statement"));
        Assert.False(support.SupportsCertainty(prefix + "statement"));
        Assert.DoesNotContain(RootCauseAxes.Cost, support.EstablishedAxes);
        Assert.DoesNotContain(support.CertaintySupport(), item => item.StartsWith(prefix, StringComparison.Ordinal));
    }

    [Fact]
    public void APriceEffectsFacts_AreHiddenFromTheRootCauseWriter_AndSoIsARuleOfPriceEffectsAlone()
    {
        // Models B and C read and write as many tokens as Model A at two and three times the price: two price effects.
        var result = RootCauseEngine.Compute(ComparisonInput(
            Compared("A", 1, 85, 1.0, 1.0),
            Compared("B", 2, 75, 1.6, 1.0, price: 2.0),
            Compared("C", 3, 70, 1.1, 1.0, price: 3.0)));
        var priceEffects = result.Hypotheses.Where(h => h.PriceEffect).ToList();
        Assert.True(priceEffects.Count >= 2);
        Assert.All(result.Hypotheses.Where(h => h.Id == "HC2"), h => Assert.True(h.PriceEffect));

        var facts = new BenchmarkReportFacts.FactList();
        RootCauseFacts.Add(facts, result, t => "Model " + t);
        var sorted = facts.Sorted().ToList();
        var hidden = RootCauseFacts.RootCauseWriterHidden(result);

        foreach (var h in priceEffects)
        {
            string prefix = "rca.hypothesis." + h.Key + ".";
            var own = sorted.Where(f => f.Key.StartsWith(prefix, StringComparison.Ordinal)).ToList();
            Assert.NotEmpty(own);
            Assert.All(own, f => Assert.True(hidden(f.Key), f.Key));
        }
        Assert.All(sorted.Where(f => f.Key.StartsWith("rca.rule.HC2.", StringComparison.Ordinal)), f => Assert.True(hidden(f.Key), f.Key));
        Assert.True(RootCauseFacts.RootCauseWriterHidden(result, "rca.rule.HC2.shareSpan"));

        // Every other fact stays on the writer's list.
        Assert.All(result.Hypotheses.Where(h => !h.PriceEffect), h => Assert.False(hidden("rca.hypothesis." + h.Key + ".statement")));
        Assert.False(hidden("rca.axis.cost.status"));
        Assert.False(hidden("rca.hypothesis.HC2.10.statement"));
        Assert.False(hidden(null));
        Assert.False(RootCauseFacts.RootCauseWriterHidden(null, "rca.hypothesis." + priceEffects[0].Key + ".statement"));
        Assert.False(RootCauseFacts.RootCauseWriterHidden(RootCauseEngine.Compute(ComparisonInput()), "rca.rule.HC1.shareSpan"));
    }

    // --- Engine inputs and fact displays -------------------------------------------------------------------

    [Fact]
    public void ModelInput_TakesTheGivenIndices_OverThePanelDiagnostics()
    {
        var entry = new BenchmarkModelComparisonEntryDto { Key = "battery:7", Label = "Orion (high)", Provider = "Northwind", ModelId = "orion", RunIds = { 7 } };
        var diagnosed = new BenchmarkModelComparisonDto
        {
            PanelDiagnostics = new BenchmarkPanelDiagnosticsDto
            {
                Applicable = true,
                Entries = { new BenchmarkPanelEntryIndicesDto { EntryKey = "battery:7", MemberAIndex = 80, MemberBIndex = 70, ReferenceIndex = 75 } }
            }
        };
        RootCauseModelInput Build(BenchmarkModelComparisonDto comparison, double? memberA = null, double? memberB = null, double? readerIndex = null)
            => RootCauseEvidence.ModelInput(entry, "A", "A", false, Array.Empty<BenchmarkRun>(), new RootCauseModelEvidence(), new double?[4], false, comparison,
                memberAIndex: memberA, memberBIndex: memberB, referenceReaderIndex: readerIndex);

        var fromPanel = Build(diagnosed);
        Assert.Equal(80.0, fromPanel.MemberAIndex);
        Assert.Equal(70.0, fromPanel.MemberBIndex);
        Assert.Equal(75.0, fromPanel.ReferenceReaderIndex);

        var given = Build(diagnosed, 81.5, 72.25, 77.75);
        Assert.Equal(81.5, given.MemberAIndex);
        Assert.Equal(72.25, given.MemberBIndex);
        Assert.Equal(77.75, given.ReferenceReaderIndex);

        // Each given index wins on its own; the others still come from the diagnostics.
        var reader = Build(diagnosed, readerIndex: 60.5);
        Assert.Equal(80.0, reader.MemberAIndex);
        Assert.Equal(70.0, reader.MemberBIndex);
        Assert.Equal(60.5, reader.ReferenceReaderIndex);

        // A battery comparison without diagnostics has the given indices alone.
        var battery = Build(new BenchmarkModelComparisonDto(), 81.5, 72.25, 77.75);
        Assert.Equal(81.5, battery.MemberAIndex);
        Assert.Equal(72.25, battery.MemberBIndex);
        Assert.Equal(77.75, battery.ReferenceReaderIndex);
        var none = Build(new BenchmarkModelComparisonDto());
        Assert.Null(none.MemberAIndex);
        Assert.Null(none.MemberBIndex);
        Assert.Null(none.ReferenceReaderIndex);
    }

    [Fact]
    public void TheShareTotalAndAtRiskDisplays_AreTheFiguresAlone_AndANotDecomposedDifferenceSaysSoInItsOwnFact()
    {
        var result = RootCauseEngine.Compute(ComparisonInput());
        var facts = new BenchmarkReportFacts.FactList();
        RootCauseFacts.Add(facts, result, t => "Model " + t);
        var available = facts.Sorted().Where(f => f.Available).ToList();
        var byKey = available.ToDictionary(f => f.Key, StringComparer.Ordinal);

        var shares = available.Where(f => (f.Key.StartsWith("rca.speed.", StringComparison.Ordinal) || f.Key.StartsWith("rca.cost.", StringComparison.Ordinal))
                                          && f.Key.EndsWith(".share", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(shares);
        Assert.All(shares, f =>
        {
            Assert.EndsWith("%", f.Display, StringComparison.Ordinal);
            Assert.DoesNotContain("difference", f.Display, StringComparison.Ordinal);
        });

        var totals = available.Where(f => f.Key.StartsWith("rca.", StringComparison.Ordinal) && f.Key.EndsWith(".total", StringComparison.Ordinal)
                                          && (f.Key.StartsWith("rca.speed.", StringComparison.Ordinal) || f.Key.StartsWith("rca.cost.", StringComparison.Ordinal)
                                              || f.Key.StartsWith("rca.quality.", StringComparison.Ordinal))).ToList();
        Assert.NotEmpty(totals);
        Assert.All(totals, f =>
        {
            Assert.DoesNotContain("(", f.Display, StringComparison.Ordinal);
            Assert.DoesNotContain("minus", f.Display, StringComparison.Ordinal);
            Assert.DoesNotContain("Model ", f.Display, StringComparison.Ordinal);
            Assert.EndsWith(f.Key.StartsWith("rca.quality.", StringComparison.Ordinal) ? " index points" : " per question", f.Display, StringComparison.Ordinal);
        });
        Assert.Matches(@"^[+-]\d+\.\d s per question$", byKey["rca.speed.A.B.total"].Display);

        // A difference that may be chance is stated with its own .decomposed fact; a decomposed one has none.
        foreach (var pair in result.SpeedPairs.Concat(result.CostPairs).Where(p => p.Difference.HasValue && (p.UnavailableReason == null || p.NotEstablished)))
        {
            string key = "rca." + pair.Axis + "." + pair.First + "." + pair.Second + ".decomposed";
            if (pair.NotEstablished)
            {
                Assert.False(byKey[key].Value!.GetValue<bool>());
                Assert.Equal(RootCauseFacts.NotDecomposedDisplay, byKey[key].Display);
            }
            else
            {
                Assert.False(byKey.ContainsKey(key), key);
            }
        }
        Assert.True(byKey.ContainsKey("rca.speed.A.C.decomposed"));

        // Points at risk are the figure alone; the total names the median it is measured against.
        foreach (var summary in result.Evidence)
        {
            foreach (var segment in summary.AtRisk)
            {
                Assert.Matches(@"^\d+\.\d index points$", byKey["rca.atRisk." + summary.Token + "." + segment.Key].Display);
            }
            Assert.Matches(@"^\d+\.\d index points below a median answer of \d+$", byKey["rca.atRisk." + summary.Token + ".total"].Display);
        }
    }

    [Fact]
    public void TheCacheShareAndPriceDisplays_AreTheFiguresAlone_AndDifferingCardsAreTheirOwnFact()
    {
        RootCauseModelEvidence Evidence(bool differ) => new()
        {
            Answers = Enumerable.Range(1, 4).Select(q => new RootCauseAnswerRecord
            {
                ItemKey = "q" + q + "@1",
                QuestionKey = "q" + q,
                Ok = true,
                InputTokens = 1000,
                CacheReadTokens = 900,
                OutputTokens = 200
            }).ToList(),
            RunCount = 2,
            RunsPerSuite = 1,
            Priced = true,
            PricesPerMillion = new double?[] { 0.5, 0.05, null, 50, 50 },
            PricesDiffer = differ
        };

        var facts = new BenchmarkReportFacts.FactList();
        RootCauseEvidence.AddFacts(facts, Evidence(differ: true), pricingBasisKind: BenchmarkReportFacts.PricingBasisCatalog);
        var byKey = facts.Sorted().ToDictionary(f => f.Key, StringComparer.Ordinal);

        Assert.Equal(BenchmarkReportFormat.Percent(0.9 * 100), byKey["tokens.cacheReadShare"].Display);
        Assert.Equal("$0.50 per million tokens", byKey["price.uncachedInput.perMillion"].Display);
        Assert.Equal("$0.05 per million tokens", byKey["price.cacheRead.perMillion"].Display);
        Assert.Equal("$50.00 per million tokens", byKey["price.output.perMillion"].Display);
        Assert.True(byKey[RootCauseEvidence.PricesDifferKey].Value!.GetValue<bool>());
        Assert.Equal(RootCauseEvidence.PricesDifferDisplay, byKey[RootCauseEvidence.PricesDifferKey].Display);
        Assert.True(RootCauseFacts.WriterHidden(RootCauseEvidence.PricesDifferKey));
        Assert.True(RootCauseFacts.WriterHidden("model.B." + RootCauseEvidence.PricesDifferKey));

        var same = new BenchmarkReportFacts.FactList();
        RootCauseEvidence.AddFacts(same, Evidence(differ: false));
        Assert.DoesNotContain(same.Sorted(), f => f.Key == RootCauseEvidence.PricesDifferKey);
        Assert.Equal("$0.50 per million tokens", same.Sorted().Single(f => f.Key == "price.uncachedInput.perMillion").Display);
    }

    // --- The research questions' hypotheses -----------------------------------------------------------------

    [Fact]
    public void QuestionOne_IgnoresTheRunnerUpsOwnFailures_ButKeepsTheGradersAndTheThinkingLevel()
    {
        var target = new RootCauseQuestionTarget(1, RootCauseAxes.Intelligence, new RootCauseModelInput { Token = "A" }, new RootCauseModelInput { Token = "B" },
            "Why did Model A score highest?", true);
        RootCauseHypothesis Pair(string id, string worse, string better)
            => new() { Id = id, Axis = RootCauseAxes.Intelligence, Kind = "pair", Models = { worse, better } };

        foreach (string id in new[] { "HQ1", "HQ2", "HQ5", "HQ6", "HQ7", "HQ9" })
        {
            Assert.False(RootCauseProfiles.Answers(target, Pair(id, "B", "A")), id);
        }
        foreach (string id in new[] { "HQ4", "HQ8" })
        {
            Assert.True(RootCauseProfiles.Answers(target, Pair(id, "B", "A")), id);
            Assert.False(RootCauseProfiles.Answers(target, Pair(id, "A", "B")), id);
        }

        // Question 2 still takes the lowest model's own failures.
        var lowest = new RootCauseQuestionTarget(2, RootCauseAxes.Intelligence, new RootCauseModelInput { Token = "B" }, new RootCauseModelInput { Token = "A" },
            "Why did Model B score lowest?", true);
        Assert.True(RootCauseProfiles.Answers(lowest, Pair("HQ1", "B", "A")));

        // Model B is Model A's model at a lower thinking level and answers without tools more often: question 1 keeps
        // the thinking level and leaves B's own failures to question 2.
        var result = RootCauseEngine.Compute(ComparisonInput(
            Compared("A", 1, 85, 1.0, 1.0, q => 85 + q % 3, modelId: "orion", thinkingLevel: "high"),
            Compared("B", 2, 75, 1.6, 1.2, q => q % 3 == 0 ? 55 : 82, noTool: q => q % 3 == 0, modelId: "orion", thinkingLevel: "medium"),
            Compared("C", 3, 60, 0.9, 2.0)));
        var highest = Assert.Single(result.Questions, q => q.Number == 1);
        Assert.Equal("B", highest.Neighbor);
        Assert.Contains(highest.ComparativeKeys!, k => k.StartsWith("HQ8.", StringComparison.Ordinal));
        Assert.All(highest.HypothesisKeys, k => Assert.Contains(result.Hypotheses.Single(h => h.Key == k).Id, new[] { "HQ4", "HQ8" }));
        Assert.Contains(result.Hypotheses, h => h.Id == "HQ1" && h.Models[0] == "B" && h.Models[1] == "A");
        Assert.Empty(highest.WithinKeys!);
    }

    [Fact]
    public void QuestionThree_LeadsWithTheSiblingAtAnotherLevel_ThenTheReference_ThenTheFastest()
    {
        // B, D and E are one model at high, medium and low thinking; A is the reference; C is the fastest and cheapest.
        var ctx = new RootCauseContext(ComparisonInput(
            Compared("A", 1, 85, 1.0, 1.0),
            Compared("B", 2, 75, 2.0, 1.2, modelId: "model-b", thinkingLevel: "high"),
            Compared("C", 3, 70, 0.8, 0.9),
            Compared("D", 4, 65, 1.4, 1.1, modelId: "model-b", thinkingLevel: "medium"),
            Compared("E", 5, 60, 1.2, 2.0, modelId: "model-b", thinkingLevel: "low")));
        var targets = RootCauseProfiles.Targets(ctx);

        var slowest = targets.Single(t => t.Number == 3);
        Assert.Equal("B", slowest.Model.Token);
        Assert.Equal(new[] { "D", "E", "A", "C" }, RootCauseProfiles.PartnerPreference(ctx, slowest));
        var mostExpensive = targets.Single(t => t.Number == 4);
        Assert.Equal("E", mostExpensive.Model.Token);
        Assert.Equal(new[] { "D", "B", "A", "C" }, RootCauseProfiles.PartnerPreference(ctx, mostExpensive));

        RootCauseHypothesis Speed(string worse, string better)
            => new() { Id = "HS1", Axis = RootCauseAxes.Speed, Kind = "pair", Models = { worse, better } };
        Assert.Equal(0, RootCauseProfiles.PartnerRank(ctx, slowest, Speed("B", "D")));
        Assert.Equal(2, RootCauseProfiles.PartnerRank(ctx, slowest, Speed("B", "A")));
        Assert.Equal(3, RootCauseProfiles.PartnerRank(ctx, slowest, Speed("B", "C")));
        Assert.Equal(0, RootCauseProfiles.PartnerRank(ctx, targets.Single(t => t.Number == 1), Speed("B", "C")));

        // Model C is Model B's model at medium thinking, and an analyzed pair with it: question 3 leads with it, ahead
        // of the reference, though both pairs are decomposed.
        var result = RootCauseEngine.Compute(ComparisonInput(
            Compared("A", 1, 85, 1.0, 1.0),
            Compared("B", 2, 75, 1.6, 1.2),
            Compared("C", 3, 60, 0.9, 2.0, modelId: "model-b", thinkingLevel: "medium")));
        var question = Assert.Single(result.Questions, q => q.Number == 3);
        var comparative = question.ComparativeKeys!.Select(k => result.Hypotheses.Single(h => h.Key == k)).ToList();
        Assert.NotEmpty(comparative);
        Assert.Equal("C", comparative[0].Models[1]);
        Assert.Equal(comparative.Select(h => h.Models[1] == "C" ? 0 : 1).OrderBy(x => x), comparative.Select(h => h.Models[1] == "C" ? 0 : 1));
        Assert.Equal(comparative.First(h => RootCauseGrades.Rank(h.Grade) >= RootCauseGrades.Rank(RootCauseGrades.Low)).Grade, question.Grade);
    }

    [Fact]
    public void TheStyleHypothesis_IsOneHypothesisOfAComparison_AboutEveryModelWithTheConflict()
    {
        var input = ComparisonInput(
            Compared("A", 1, 85, 1.0, 1.0, styleConflict: true),
            Compared("B", 2, 75, 1.6, 1.2, styleConflict: true),
            Compared("C", 3, 60, 0.9, 2.0, styleConflict: true));
        var ctx = new RootCauseContext(input);

        // Shaped as HQ3: one pair-kind draft of the group all, naming every model with the conflict, the best one too.
        var draft = Assert.IsType<RootCauseDraft>(RootCauseHypotheses.SharedStyleConflict(ctx));
        Assert.Equal("HQ5", draft.Id);
        Assert.Equal("pair", draft.Kind);
        Assert.Equal("all", draft.Group);
        Assert.Equal(new[] { "A", "B", "C" }, draft.Models);
        Assert.Equal("Concise style: all models", draft.Short);
        Assert.Equal("Completeness was the lowest dimension, at least 13 points below Accuracy, for 3 of 3 models under the concise response style.", draft.Statement);
        Assert.Equal("the concise response style, 3 of 3 models", draft.Label);
        Assert.Equal(RootCauseHypotheses.StyleRefutedIf, draft.RefutedIf);
        Assert.Equal(RootCauseRunKinds.StylePair, draft.NextRunKind);
        Assert.False(draft.Quantified);
        Assert.Equal(3, draft.FactKeys.Count);
        Assert.All(draft.FactKeys, k => Assert.EndsWith("style.responseStyleConflict", k, StringComparison.Ordinal));
        Assert.Equal(RootCauseGrades.Low, RootCauseHypotheses.Grade(draft, ctx.Name).Grade);
        Assert.Contains("indicative for the other models", RootCauseHypotheses.StyleRefutedIf, StringComparison.Ordinal);

        // It answers no research question, and the result holds at most one.
        var result = RootCauseEngine.Compute(input);
        Assert.True(result.Hypotheses.Count(h => h.Id == "HQ5") <= 1);
        Assert.All(result.Hypotheses.Where(h => h.Id == "HQ5"), h =>
        {
            Assert.True(RootCauseHypotheses.IsDocumentWide(h));
            Assert.All(result.Questions, q => Assert.DoesNotContain(h.Key, q.HypothesisKeys));
        });

        // Two models with the conflict: the one hypothesis is kept, numbered and labeled as the rule states it.
        var two = RootCauseEngine.Compute(ComparisonInput(
            Compared("A", 1, 85, 1.0, 1.0, styleConflict: true),
            Compared("B", 2, 75, 1.6, 1.2, styleConflict: true)));
        var hq5 = Assert.Single(two.Hypotheses, h => h.Id == "HQ5");
        Assert.Equal("HQ5.1", hq5.Key);
        Assert.Equal("pair", hq5.Kind);
        Assert.Equal(new[] { "A", "B" }, hq5.Models);
        Assert.Equal("Completeness was the lowest dimension, at least 13 points below Accuracy, for 2 of 2 models under the concise response style.", hq5.Statement);
        Assert.Equal("the concise response style, 2 of 2 models", hq5.Label);
        Assert.All(two.Questions, q => Assert.DoesNotContain(hq5.Key, q.HypothesisKeys));

        // One model with the conflict among three is no shared style hypothesis.
        var one = ComparisonInput(
            Compared("A", 1, 85, 1.0, 1.0),
            Compared("B", 2, 75, 1.6, 1.2, styleConflict: true),
            Compared("C", 3, 60, 0.9, 2.0));
        Assert.Null(RootCauseHypotheses.SharedStyleConflict(new RootCauseContext(one)));
        Assert.DoesNotContain(RootCauseEngine.Compute(one).Hypotheses, h => h.Id == "HQ5");
    }

    [Fact]
    public void TheKnownFacts_KeepSlotsForCountsAndConfiguration()
    {
        var tests = Enumerable.Range(1, 30).Select(i => new RootCauseKnownFact { Kind = RootCauseKnownFacts.KindTest, Text = "Test " + i });
        var counts = Enumerable.Range(1, 8).Select(i => new RootCauseKnownFact
        {
            Kind = i % 2 == 0 ? RootCauseKnownFacts.KindConfiguration : RootCauseKnownFacts.KindCount,
            Text = "Count " + i
        });

        var facts = RootCauseKnownFacts.Select(tests.Concat(counts).ToList());

        int reserved = RootCauseKnownFacts.ReservedCountAndConfigurationFacts;
        Assert.Equal(RootCauseKnownFacts.MaxFacts, facts.Count);
        Assert.Equal(Enumerable.Range(1, RootCauseKnownFacts.MaxFacts), facts.Select(f => f.Number));
        Assert.Equal(Enumerable.Range(1, RootCauseKnownFacts.MaxFacts - reserved).Select(i => "Test " + i),
            facts.Where(f => f.Kind == RootCauseKnownFacts.KindTest).Select(f => f.Text));
        Assert.Equal(Enumerable.Range(1, reserved).Select(i => "Count " + i),
            facts.Where(f => f.Kind != RootCauseKnownFacts.KindTest).Select(f => f.Text));

        // With fewer counts than the reserved slots, the tests fill the rest; with few candidates, every one is kept.
        var few = RootCauseKnownFacts.Select(tests.Concat(counts.Take(2)).ToList());
        Assert.Equal(RootCauseKnownFacts.MaxFacts, few.Count);
        Assert.Equal(RootCauseKnownFacts.MaxFacts - 2, few.Count(f => f.Kind == RootCauseKnownFacts.KindTest));
        Assert.Equal(new[] { "Count 1", "Count 2" }, few.Skip(RootCauseKnownFacts.MaxFacts - 2).Select(f => f.Text));
        Assert.Equal(5, RootCauseKnownFacts.Select(tests.Take(3).Concat(counts.Take(2)).ToList()).Count);
    }

    // --- Next runs -----------------------------------------------------------------------------------------

    [Fact]
    public void ANullThatStands_PutsItsModelsInTheReplicate()
    {
        var result = RootCauseEngine.Compute(ComparisonInput());

        var standing = result.Hypotheses.Where(h => h.Kind == "null" && h.Grade != RootCauseGrades.NotSupported).ToList();
        Assert.NotEmpty(standing);
        var replicate = Assert.Single(result.RecommendedRuns, r => r.Kind == RootCauseRunKinds.Replicate);
        Assert.Equal(RootCauseNextRuns.ReplicateRuns, replicate.RunsPerModel);
        foreach (var h in standing)
        {
            Assert.Contains(h.Key, replicate.HypothesisIds);
            Assert.Equal(replicate.Number, h.NextRunNumber);
            Assert.All(h.Models, token => Assert.Contains(200L + (token[0] - 'A' + 1), replicate.TestedModelConfigurationIds));
        }
        Assert.Contains("if the paired test still finds no difference with three runs per model", replicate.Decides, StringComparison.Ordinal);
        Assert.Equal(replicate.Decides, string.Join(" ", replicate.DecidesItems!));
        Assert.All(result.Hypotheses.Where(h => h.Grade == RootCauseGrades.NotSupported), h => Assert.Null(h.NextRunNumber));
    }

    [Fact]
    public void ACrossModelThinkingHypothesis_NeverYieldsAThinkingPair()
    {
        var result = RootCauseEngine.Compute(ComparisonInput());
        var modelIds = result.Models.ToDictionary(m => m.Token, m => m.ModelId, StringComparer.Ordinal);

        var crossModel = result.Hypotheses
            .Where(h => h.Id is "HS3" or "HC3" && h.Models.Count == 2 && modelIds[h.Models[0]] != modelIds[h.Models[1]])
            .ToList();
        Assert.NotEmpty(crossModel);
        Assert.All(crossModel, h =>
        {
            Assert.DoesNotContain(result.RecommendedRuns, r => r.Kind == RootCauseRunKinds.ThinkingPair && r.HypothesisIds.Contains(h.Key));
            Assert.DoesNotContain(result.Checks, c => c.HypothesisIds.Contains(h.Key) && c.Text.Contains("another thinking level", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void AnAnalyzedSiblingAtAnotherLevel_IsTheThinkingPair_AndTheSlowestModelsNeighbor()
    {
        // Model C is Model B's model at medium thinking; no configuration of it is in the catalog.
        var result = RootCauseEngine.Compute(ComparisonInput(
            Compared("A", 1, 85, 1.0, 1.0),
            Compared("B", 2, 75, 1.6, 1.2),
            Compared("C", 3, 60, 0.9, 2.0, modelId: "model-b", thinkingLevel: "medium")));

        var ws2 = Assert.Single(result.Hypotheses, h => h.Id == "WS2" && h.Models[0] == "B");
        var run = Assert.Single(result.RecommendedRuns, r => r.Number == ws2.NextRunNumber);
        Assert.Contains(202L, run.TestedModelConfigurationIds);
        Assert.Contains(203L, run.TestedModelConfigurationIds);
        Assert.DoesNotContain(result.Checks, c => c.Text.Contains("of Model B exists at another thinking level", StringComparison.Ordinal));

        var slowest = Assert.Single(result.Questions, q => q.Number == 3);
        Assert.Equal("B", slowest.Token);
        Assert.Contains(slowest.Signals, s => s.StartsWith("against Model C: ", StringComparison.Ordinal));

        // Without the sibling, the missing level is a check.
        var alone = RootCauseEngine.Compute(ComparisonInput());
        var own = Assert.Single(alone.Hypotheses, h => h.Id == "WS2" && h.Models[0] == "B");
        Assert.Contains(alone.Checks, c => c.Kind == "configuration" && c.HypothesisIds.Contains(own.Key)
                                           && c.Text.Contains("another thinking level", StringComparison.Ordinal));
    }

    [Fact]
    public void TheStyleHypothesis_IsSettledByOneOptionalRunOfTheReferenceModel()
    {
        var input = ComparisonInput(
            Compared("A", 1, 85, 1.0, 1.0),
            Compared("B", 2, 75, 1.6, 1.2, styleConflict: true),
            Compared("C", 3, 60, 0.9, 2.0, styleConflict: true));
        var ctx = new RootCauseContext(input);
        Assert.DoesNotContain(ctx.Pairs.SelectMany(p => RootCauseHypotheses.PairRules(ctx, p, new Dictionary<string, bool>())), d => d.Id == "HQ5");
        var draft = Assert.IsType<RootCauseDraft>(RootCauseHypotheses.SharedStyleConflict(ctx));
        Assert.Equal(new[] { "B", "C" }, draft.Models);
        var kept = RootCauseHypotheses.Keep(new[] { (Hypothesis: RootCauseHypotheses.Grade(draft, ctx.Name), Draft: draft) });

        var (runs, _) = RootCauseNextRuns.Build(ctx, kept, systemFirst: false);

        var style = Assert.Single(runs, r => r.Kind == RootCauseRunKinds.StylePair);
        Assert.Equal(RootCauseMarks.Optional, style.Mark);
        Assert.Equal(new long[] { 201 }, style.TestedModelConfigurationIds);
        Assert.Equal(new[] { "HQ5.1" }, style.HypothesisIds);
        Assert.True(style.VerboseMode);
    }

    // --- Pairs, unexpected results and gained points -------------------------------------------------------

    [Fact]
    public void ThePairs_AddTheSameModelAtAdjacentThinkingLevels_SoHQ8CompareThem()
    {
        // B, C and D are one model at high, medium and low thinking; A is the reference and A and E the extremes of every axis.
        var ctx = new RootCauseContext(ComparisonInput(
            Compared("A", 1, 85, 1.0, 1.0),
            Compared("B", 2, 75, 1.1, 1.1, modelId: "model-x", thinkingLevel: "high"),
            Compared("C", 3, 70, 1.2, 1.2, modelId: "model-x", thinkingLevel: "medium"),
            Compared("D", 4, 65, 1.3, 1.3, modelId: "model-x", thinkingLevel: "low"),
            Compared("E", 5, 60, 1.4, 1.4)));

        var keys = ctx.Pairs.Select(p => p.Key).ToList();
        Assert.Contains("B|C", keys);
        Assert.Contains("C|D", keys);
        Assert.DoesNotContain("B|D", keys);
        Assert.Equal(keys.Count, keys.Distinct(StringComparer.Ordinal).Count());

        // C and D are neither the reference nor an extreme; HQ8 compares them, the lower level as the worse side.
        var pair = ctx.Pairs.Single(p => p.Key == "C|D");
        Assert.Contains(RootCauseHypotheses.PairRules(ctx, pair, new Dictionary<string, bool>()),
            d => d.Id == "HQ8" && d.Models.SequenceEqual(new[] { "D", "C" }));
    }

    [Fact]
    public void ThePairsOfUnexpectedResults_JoinTheAnalyzedPairs_LargestGapFirst_UpToTheCap()
    {
        // Four dearer models of one provider score 20 to 23 points below four cheaper ones: sixteen unexpected results.
        var ctx = new RootCauseContext(ComparisonInput(
            Compared("A", 1, 90, 1.0, 1.0),
            Compared("B", 2, 60, 1.0, 1.0, provider: "Acme", outputPrice: 15),
            Compared("C", 3, 61, 1.0, 1.0, provider: "Acme", outputPrice: 15),
            Compared("D", 4, 62, 1.0, 1.0, provider: "Acme", outputPrice: 15),
            Compared("E", 5, 63, 1.0, 1.0, provider: "Acme", outputPrice: 15),
            Compared("F", 6, 80, 1.0, 1.0, provider: "Acme", outputPrice: 5),
            Compared("G", 7, 81, 1.0, 1.0, provider: "Acme", outputPrice: 5),
            Compared("H", 8, 82, 1.0, 1.0, provider: "Acme", outputPrice: 5),
            Compared("I", 9, 83, 1.0, 1.0, provider: "Acme", outputPrice: 5)));

        Assert.Equal(16, ctx.Anomalies.Count);
        Assert.All(ctx.Anomalies, a => Assert.Equal(RootCausePriors.PriceTierQuality, a.Prior));
        Assert.Equal(Enumerable.Range(1, 16).Select(n => "U" + n), ctx.Anomalies.Select(a => a.Ref));
        Assert.Equal(new[] { RootCausePriors.PriceTierQuality, RootCausePriors.PriceTierSpeed }, ctx.PriorsChecked);

        // The reference pairs (A with each other model) come first, the extremes among them; then six anomaly pairs.
        Assert.Equal(8 + RootCauseEngine.MaxAnomalyPairs, ctx.Pairs.Count);
        Assert.Equal(new[] { "B|I", "B|H", "C|I", "B|G", "C|H", "D|I" }, ctx.Pairs.Skip(8).Select(p => p.Key));
    }

    [Fact]
    public void ThePriceTierQualityPrior_FlagsADearerTierTwoPointsBelow_ButNotAt1Point9_NorAcrossProviders()
    {
        RootCauseResult Run(double cheaperIndex, string cheaperProvider) => RootCauseEngine.Compute(ComparisonInput(
            Compared("A", 1, 85, 1.0, 1.0),
            Compared("B", 2, 79, 1.0, 1.0, provider: "Acme", modelId: "acme-pro", outputPrice: 15),
            Compared("C", 3, cheaperIndex, 1.0, 1.0, provider: cheaperProvider, modelId: "acme-mini", outputPrice: 5)));

        var flagged = Run(81, "Acme");
        var u1 = Assert.Single(flagged.Anomalies!);
        Assert.Equal(RootCausePriors.PriceTierQuality, u1.Prior);
        Assert.Equal(RootCauseAxes.Intelligence, u1.Axis);
        Assert.Equal(1, u1.Number);
        Assert.Equal("U1", u1.Ref);
        Assert.Equal("B", u1.Expected);
        Assert.Equal("C", u1.Other);
        Assert.Equal(2.0, u1.Gap, 9);
        Assert.Equal(15.0, u1.ExpectedOutputPrice);
        Assert.Equal(5.0, u1.OtherOutputPrice);
        Assert.False(u1.Tested);
        Assert.False(u1.Established);
        Assert.True(u1.IntervalsOverlap);
        Assert.Equal("Model B scored 79, below the cheaper Model C at 81; not established, the intervals overlap.",
            RootCauseAnomalies.Summary(u1, t => "Model " + t));
        Assert.Equal(new[] { RootCausePriors.PriceTierQuality, RootCausePriors.PriceTierSpeed }, flagged.PriorsChecked!);
        Assert.Contains(flagged.CostPairs.Concat(flagged.SpeedPairs), p => p.First == "B" && p.Second == "C");

        Assert.Empty(Run(80.9, "Acme").Anomalies!);
        var across = Run(81, "Other");
        Assert.Empty(across.Anomalies!);
        Assert.Empty(across.PriorsChecked!);
        Assert.False(RootCauseAnomalies.HasSameProviderPair(across.Models));
        Assert.Equal("No two models share a provider.", RootCauseAnomalies.NoPriorReason(across.Models));

        // A per-model sheet looks for none.
        Assert.Null(RootCauseEngine.Compute(Input(Controls())).Anomalies);
    }

    [Fact]
    public void ThePriceTierSpeedPrior_FlagsACheaperTierAt1Point25TimesTheTime_WithItsLeadingComponent()
    {
        var result = RootCauseEngine.Compute(WithPairTests(ComparisonInput(
            Compared("A", 1, 85, 1.0, 1.0),
            Compared("B", 2, 80, 1.0, 1.0, provider: "Acme", modelId: "acme-pro", outputPrice: 15),
            Compared("C", 3, 80, 1.25, 1.0, provider: "Acme", modelId: "acme-mini", outputPrice: 5))));

        var u1 = Assert.Single(result.Anomalies!);
        Assert.Equal(RootCausePriors.PriceTierSpeed, u1.Prior);
        Assert.Equal(RootCauseAxes.Speed, u1.Axis);
        Assert.Equal("C", u1.Expected);
        Assert.Equal("B", u1.Other);
        Assert.Equal(1.25, u1.Gap, 9);
        Assert.False(u1.Tested);
        Assert.True(u1.Established);
        Assert.Null(u1.IntervalsOverlap);
        Assert.Empty(u1.Questions);
        var lead = Assert.IsType<RootCauseComponent>(u1.SpeedComponent);
        var pair = Assert.Single(result.SpeedPairs, p => p.First == "B" && p.Second == "C");
        Assert.Equal(pair.Components.Where(c => c.Share.HasValue).Max(c => c.Share!.Value), lead.Share!.Value, 12);
        Assert.Equal("Model C was slower than the dearer Model B: a median model time of 22.5 s against 18.0 s; "
                     + "established, the 95% interval of the time difference excludes zero.", RootCauseAnomalies.Summary(u1, t => "Model " + t));
    }

    [Fact]
    public void TheNewerReleasePrior_ReadsTheModelLineAndTheReleaseDates_AndSkipsDifferentThinkingLevels()
    {
        Assert.Equal("gpt-luna", RootCauseAnomalies.ModelLine("gpt-6-luna"));
        Assert.Equal("gpt-luna", RootCauseAnomalies.ModelLine("GPT-5.6-Luna"));
        Assert.Equal("claude-opus", RootCauseAnomalies.ModelLine("claude-5.5-opus"));
        Assert.Equal("acme|acme-5.6-luna", RootCauseEvidence.ReleaseDateKey(" Acme ", "ACME-5.6-luna"));

        // B carries its own date; C's comes from the input's release dates.
        var dates = new Dictionary<string, string>(StringComparer.Ordinal) { [RootCauseEvidence.ReleaseDateKey("Acme", "acme-5.6-luna")] = "2026-03-01" };
        RootCauseInput Build(string olderLevel, IReadOnlyDictionary<string, string>? releaseDates) => With(ComparisonInput(
            Compared("A", 1, 85, 1.0, 1.0),
            Compared("B", 2, 78, 1.0, 1.0, provider: "Acme", modelId: "acme-6-luna", releaseDate: "2026-09-01"),
            Compared("C", 3, 81, 1.0, 1.0, provider: "Acme", modelId: "acme-5.6-luna", thinkingLevel: olderLevel)), releaseDates: releaseDates);

        var result = RootCauseEngine.Compute(Build("high", dates));
        var u1 = Assert.Single(result.Anomalies!);
        Assert.Equal(RootCausePriors.NewerRelease, u1.Prior);
        Assert.Equal("B", u1.Expected);
        Assert.Equal("C", u1.Other);
        Assert.Equal(3.0, u1.Gap, 9);
        Assert.Equal("2026-09-01", u1.ExpectedReleaseDate);
        Assert.Equal("2026-03-01", u1.OtherReleaseDate);
        Assert.Null(u1.ExpectedOutputPrice);
        Assert.Equal(new[] { RootCausePriors.NewerRelease }, result.PriorsChecked!);
        Assert.Equal("Model B scored 78, below the older Model C at 81; not established, the intervals overlap.",
            RootCauseAnomalies.Summary(u1, t => "Model " + t));

        // At another thinking level, or without the second release date, the prior is not checked.
        var levels = RootCauseEngine.Compute(Build("medium", dates));
        Assert.Empty(levels.Anomalies!);
        Assert.Empty(levels.PriorsChecked!);
        var undated = RootCauseEngine.Compute(Build("high", null));
        Assert.Empty(undated.Anomalies!);
        Assert.Empty(undated.PriorsChecked!);
        Assert.True(RootCauseAnomalies.HasSameProviderPair(undated.Models));
        Assert.StartsWith("No two models of one provider have ", RootCauseAnomalies.NoPriorReason(undated.Models), StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnexpectedResult_OfAnUntestedPair_IsEstablishedOnlyByClearIntervals_AndATestDecidesATestedPair()
    {
        RootCauseInput Build(double dearerIndex, bool intervals) => ComparisonInput(
            Compared("A", 1, 85, 1.0, 1.0),
            Compared("B", 2, dearerIndex, 1.0, 1.0, provider: "Acme", modelId: "acme-pro", outputPrice: 15, intervals: intervals),
            Compared("C", 3, 81, 1.0, 1.0, provider: "Acme", modelId: "acme-mini", outputPrice: 5));
        string Summary(RootCauseResult r) => RootCauseAnomalies.Summary(Assert.Single(r.Anomalies!), t => "Model " + t);

        // Without an interval nothing can establish it.
        var unknown = RootCauseEngine.Compute(Build(70, intervals: false));
        var u = Assert.Single(unknown.Anomalies!);
        Assert.False(u.Tested);
        Assert.False(u.Established);
        Assert.Null(u.IntervalsOverlap);
        Assert.EndsWith("; not established, and the pair had no paired test.", Summary(unknown), StringComparison.Ordinal);

        // Intervals that do not overlap establish it (65 to 75 against 76 to 86).
        var clear = RootCauseEngine.Compute(Build(70, intervals: true));
        Assert.True(Assert.Single(clear.Anomalies!).Established);
        Assert.EndsWith("; established, the intervals do not overlap.", Summary(clear), StringComparison.Ordinal);

        // A paired test of the pair decides it.
        var tested = RootCauseEngine.Compute(WithPairTests(Build(70, intervals: true), IntelligenceTest("B", "C", "Second")));
        var decided = Assert.Single(tested.Anomalies!);
        Assert.True(decided.Tested);
        Assert.True(decided.Established);
        Assert.EndsWith("; established by the paired test.", Summary(tested), StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnexpectedResult_CarriesItsExplanation_AndFactsWithLabels()
    {
        var result = RootCauseEngine.Compute(ComparisonInput(
            Compared("A", 1, 85, 1.0, 1.0),
            Compared("B", 2, 79, 1.0, 1.0, provider: "Acme", modelId: "acme-pro", thinkingLevel: "medium", outputPrice: 15,
                panel: true, memberA: 77, memberB: 80, reader: 79),
            Compared("C", 3, 81, 1.0, 1.0, provider: "Acme", modelId: "acme-mini", thinkingLevel: "xhigh", outputPrice: 5,
                panel: true, memberA: 83, memberB: 81, reader: 80)));

        var u1 = Assert.Single(result.Anomalies!);
        Assert.Equal(new[] { "thinking level (medium vs xhigh)" }, u1.Confounds);
        var graders = Assert.IsType<RootCauseAnomalyGraders>(u1.Graders);
        Assert.Equal((77.0, 80.0, 79.0), (graders.ExpectedMemberA!.Value, graders.ExpectedMemberB!.Value, graders.ExpectedReader!.Value));
        Assert.Equal((83.0, 81.0, 80.0), (graders.OtherMemberA!.Value, graders.OtherMemberB!.Value, graders.OtherReader!.Value));
        Assert.Equal("A", graders.LargerSplit);
        Assert.Equal(new[] { "Accuracy", "Completeness" }, u1.Dimensions.Select(d => d.Name));
        Assert.Equal((79.0, 81.0), (u1.Dimensions[0].Expected, u1.Dimensions[0].Other));
        Assert.Equal(new[] { "Q1", "Q2", "Q3" }, u1.Questions.Select(q => q.Reference));
        Assert.All(u1.Questions, q => Assert.True(q.Other > q.Expected));
        Assert.Equal(77.0, u1.Questions[0].OthersMedian);
        Assert.Null(u1.SpeedComponent);
        Assert.True(u1.Hypotheses.Count <= 2);
        Assert.All(u1.Hypotheses, k =>
        {
            var h = Assert.Single(result.Hypotheses, x => x.Key == k);
            Assert.Equal(RootCauseAxes.Intelligence, h.Axis);
            Assert.True(h.Models.Contains("B") && h.Models.Contains("C"));
        });

        // No configuration of Model B at xhigh exists, so a check settles it.
        Assert.Null(u1.SettledByRun);
        var check = Assert.Single(result.Checks, c => c.Number == u1.SettledByCheck);
        Assert.Equal("configuration", check.Kind);
        Assert.EndsWith("of Model B exists at xhigh thinking; add one to test U1.", check.Text, StringComparison.Ordinal);

        var facts = new BenchmarkReportFacts.FactList();
        RootCauseFacts.Add(facts, result, t => "Model " + t);
        var byKey = facts.Sorted().ToDictionary(f => f.Key, StringComparer.Ordinal);
        Assert.Equal("1 unexpected result", byKey["rca.anomaly.count"].Display);
        Assert.Equal("the price-tier order of quality and the price-tier order of speed", byKey["rca.anomaly.priorsChecked"].Display);
        Assert.Equal("1 pair", byKey["rca.anomaly.untestedPairs"].Display);
        Assert.Equal("U1", byKey["rca.anomaly.1.ref"].Display);
        Assert.Equal(RootCauseAnomalies.Summary(u1, t => "Model " + t), byKey["rca.anomaly.1.summary"].Display);
        Assert.Equal(RootCausePriors.PriceTierQuality, byKey["rca.anomaly.1.prior"].Value!.GetValue<string>());
        Assert.Equal("thinking level (medium vs xhigh)", byKey["rca.anomaly.1.confounds"].Display);
        Assert.Equal("under panel member A, Model B 77 and Model C 83; under panel member B, Model B 80 and Model C 81; "
                     + "under the reference reader, Model B 79 and Model C 80; the two lie further apart under panel member A",
            byKey["rca.anomaly.1.graders"].Display);
        Assert.Equal("check " + u1.SettledByCheck, byKey["rca.anomaly.1.settledBy"].Display);
        Assert.Equal("the pair had no paired test", byKey["rca.anomaly.1.tested"].Display);
        Assert.False(byKey["rca.anomaly.1.tested"].Value!.GetValue<bool>());

        Assert.All(byKey.Keys.Where(k => k.StartsWith("rca.anomaly.", StringComparison.Ordinal)), key =>
        {
            Assert.True(BenchmarkReportFactLabels.TryLabel(key, out string label), "No label for " + key);
            Assert.DoesNotContain(".", label.TrimEnd('.'));
            Assert.Equal(key == "rca.anomaly.1.summary", RootCauseClaimSupport.IsSentenceShaped(key));
            Assert.True(RootCauseFacts.WriterHidden(key), key);
            Assert.False(RootCauseFacts.RootCauseWriterHidden(result, key), key);
        });
    }

    [Fact]
    public void AnUnexpectedResultAcrossThinkingLevels_GetsAThinkingLevelPair_AndAnyOtherJoinsTheReplicate()
    {
        // Model B, expected ahead, at Model C's xhigh thinking is configured.
        var input = ComparisonInput(
            Compared("A", 1, 85, 1.0, 1.0),
            Compared("B", 2, 79, 1.0, 1.0, provider: "Acme", modelId: "acme-pro", thinkingLevel: "medium", outputPrice: 15),
            Compared("C", 3, 81, 1.0, 1.0, provider: "Acme", modelId: "acme-mini", thinkingLevel: "xhigh", outputPrice: 5));
        var catalog = new RootCauseCatalog
        {
            Configurations = Catalog().Configurations
                .Append(new RootCauseConfigOption { Id = 77, Provider = "Acme", ModelId = "acme-pro", ThinkingLevel = "xhigh", Label = "Acme Pro (xhigh)" })
                .ToList(),
            Recommended = Catalog().Recommended
        };
        var result = RootCauseEngine.Compute(With(input, catalog: catalog));

        var u1 = Assert.Single(result.Anomalies!);
        Assert.Null(u1.SettledByCheck);
        var run = Assert.Single(result.RecommendedRuns, r => r.Number == u1.SettledByRun);
        Assert.Contains(77L, run.TestedModelConfigurationIds);
        Assert.Contains(202L, run.TestedModelConfigurationIds);
        Assert.Contains("Shows whether U1 holds with both models at the same thinking level.", run.DecidesItems!);
        Assert.Equal(run.Decides, string.Join(" ", run.DecidesItems!));
        Assert.DoesNotContain(result.Checks, c => c.Text.Contains("add one to test U1", StringComparison.Ordinal));

        var facts = new BenchmarkReportFacts.FactList();
        RootCauseFacts.Add(facts, result, t => "Model " + t);
        Assert.Equal("next run " + run.Number, facts.Sorted().Single(f => f.Key == "rca.anomaly.1.settledBy").Display);

        // At one thinking level the result joins the replicate, with both of its models.
        var same = RootCauseEngine.Compute(ComparisonInput(
            Compared("A", 1, 85, 1.0, 1.0),
            Compared("B", 2, 79, 1.0, 1.0, provider: "Acme", modelId: "acme-pro", outputPrice: 15),
            Compared("C", 3, 81, 1.0, 1.0, provider: "Acme", modelId: "acme-mini", outputPrice: 5)));
        var u = Assert.Single(same.Anomalies!);
        var replicate = Assert.Single(same.RecommendedRuns, r => r.Kind == RootCauseRunKinds.Replicate);
        Assert.Equal(replicate.Number, u.SettledByRun);
        Assert.Contains("Shows whether U1 repeats over three runs per model.", replicate.DecidesItems!);
        Assert.Contains(202L, replicate.TestedModelConfigurationIds);
        Assert.Contains(203L, replicate.TestedModelConfigurationIds);
    }

    [Fact]
    public void GainedPoints_CountTheQuestionsTenOrMoreAboveTheOtherModelsMedian()
    {
        // Model A scored 90 on Q1, 10 above Models B's and C's 80, and 79.9 on Q2, 9.9 above their 70; every other answer is 70.
        var result = RootCauseEngine.Compute(ComparisonInput(
            Compared("A", 1, 85, 1.0, 1.0, q => q == 1 ? 90 : q == 2 ? 79.9 : 70),
            Compared("B", 2, 75, 1.6, 1.2, q => q == 1 ? 80 : 70),
            Compared("C", 3, 60, 0.9, 2.0, q => q == 1 ? 80 : 70)));

        var a = result.Profiles.Single(p => p.Token == "A");
        var gained = a.Strengths[0];
        Assert.Equal(RootCauseProfiles.GainedPointsOn, gained.Kind);
        Assert.Equal("answers above the other models' median by 10 or more: +0.8 index points on 1 question (Q1)", gained.Text);
        Assert.Equal("+0.8 points on 1 question", gained.Short);
        Assert.Equal(new[] { "Q1" }, gained.Questions);
        Assert.True(a.Strengths.Count <= RootCauseProfiles.MaxStrengths);
        Assert.Contains(a.Strengths, s => s.Text == "first of 3 models on Accuracy");
        Assert.Empty(RootCauseClaimSupport.CausalPhrases(gained.Text));

        var b = Assert.Single(result.Profiles.Single(p => p.Token == "B").Strengths, s => s.Kind == RootCauseProfiles.GainedPointsOn);
        Assert.Equal("no answer 10 or more above the other models' median", b.Text);
        Assert.Equal("none", b.Short);
        Assert.Empty(b.Questions);

        // The strength facts number the dimension ranks only.
        var facts = new BenchmarkReportFacts.FactList();
        RootCauseFacts.Add(facts, result, t => "Model " + t);
        var byKey = facts.Sorted().ToDictionary(f => f.Key, StringComparer.Ordinal);
        Assert.Equal(gained.Text, byKey["rca.profile.A.gainedPointsOn"].Display);
        Assert.Equal("first of 3 models on Accuracy", byKey["rca.profile.A.strength.1"].Display);
        Assert.True(BenchmarkReportFactLabels.TryLabel("rca.profile.A.gainedPointsOn", out string label));
        Assert.DoesNotContain(".", label.TrimEnd('.'));
        Assert.False(RootCauseClaimSupport.IsSentenceShaped("rca.profile.A.gainedPointsOn"));

        // With two models the entry is not measured.
        var two = RootCauseEngine.Compute(ComparisonInput(
            Compared("A", 1, 85, 1.0, 1.0, q => q == 1 ? 90 : 70),
            Compared("B", 2, 75, 1.6, 1.2, q => 70)));
        Assert.All(two.Profiles, p => Assert.DoesNotContain(p.Strengths, s => s.Kind == RootCauseProfiles.GainedPointsOn));
        var twoFacts = new BenchmarkReportFacts.FactList();
        RootCauseFacts.Add(twoFacts, two, t => "Model " + t);
        Assert.DoesNotContain(twoFacts.Sorted(), f => f.Key.EndsWith(".gainedPointsOn", StringComparison.Ordinal));
    }

    // --- Research questions' signals ---------------------------------------------------------------------

    [Fact]
    public void TheHighestModelsSignal_NamesTheDimensionCarryingItsLead_EvenWhenItsRunnerUpIsNoAnalyzedPair()
    {
        const string lead = "the largest part of the lead over Model B is Completeness (+2.7 of +10.0 points)";

        var result = RootCauseEngine.Compute(ComparisonInput());
        Assert.Equal(lead, Assert.Single(result.Questions, q => q.Number == 1).Signals[0]);

        // With Model C as the reference, Model A and Model B are no analyzed pair; their split is added for the question.
        var other = RootCauseEngine.Compute(WithGraders(ComparisonInput(), reference: "C"));
        Assert.Contains(other.QualitySplits, s => s.First == "A" && s.Second == "B");
        Assert.Equal(lead, Assert.Single(other.Questions, q => q.Number == 1).Signals[0]);
        var facts = new BenchmarkReportFacts.FactList();
        RootCauseFacts.Add(facts, other, t => "Model " + t);
        Assert.Contains(facts.Sorted(), f => f.Key == "rca.quality.A.B.total");
        Assert.Empty(RootCauseClaimSupport.CausalPhrases(lead));
    }

    [Fact]
    public void TheSlowestModelsSignal_NamesTheComponentCarryingItsGapToTheReference()
    {
        var result = RootCauseEngine.Compute(ComparisonInput());

        var slowest = Assert.Single(result.Questions, q => q.Number == 3);
        var signal = Assert.Single(slowest.Signals, s => s.StartsWith("against ", StringComparison.Ordinal));
        Assert.StartsWith("against Model A: ", signal, StringComparison.Ordinal);
        Assert.Contains(" carries ", signal, StringComparison.Ordinal);
        Assert.EndsWith(" s difference", signal, StringComparison.Ordinal);
        Assert.Empty(RootCauseClaimSupport.CausalPhrases(signal));
    }

    // --- Reasoning a provider does not report separately -------------------------------------------------

    [Fact]
    public void APairWhereNeitherReportsReasoning_WritesOneOutputRow_ThatSumsExactly()
    {
        var result = RootCauseEngine.Compute(ComparisonInput(
            Compared("A", 1, 85, 1.0, 1.0, reasoningReported: false),
            Compared("B", 2, 75, 1.6, 1.2, reasoningReported: false)));

        var pair = Assert.Single(result.CostPairs, p => p.First == "A" && p.Second == "B");
        Assert.False(pair.NotEstablished);
        Assert.False(pair.Grouped);
        Assert.DoesNotContain(pair.Components, c => c.Key is RootCauseSegments.Output or RootCauseSegments.Reasoning);
        Assert.Equal(new[] { "volume", "price" }, pair.Components.Where(c => c.Key == RootCauseSegments.OutputWithThinking).Select(c => c.Effect));
        Assert.Equal(pair.Difference!.Value, pair.Components.Where(c => c.Key != "all").Sum(c => c.Amount), 12);
        Assert.DoesNotContain(result.Hypotheses, h => h.Id == "HC3");

        var row = Assert.Single(result.CostRows, r => r.Token == "B");
        var merged = new[] { RootCauseSegments.UncachedInput, RootCauseSegments.CacheRead, RootCauseSegments.CacheWrite, RootCauseSegments.OutputWithThinking };
        Assert.Equal(merged, row.Segments.Select(s => s.Key));
        Assert.Equal(row.Total!.Value, row.Segments.Sum(s => s.Value), 12);
        Assert.Equal("output, thinking included", row.Segments[^1].Label);
        Assert.Equal(merged, Assert.Single(result.CostRows, r => r.Token == "A").Segments.Select(s => s.Key));
        Assert.Equal(merged, Assert.Single(result.Evidence, e => e.Token == "B").Cost.Select(s => s.Key));
        Assert.Equal(merged, Assert.Single(result.Evidence, e => e.Token == "B").Tokens.Select(s => s.Key));

        var facts = new BenchmarkReportFacts.FactList();
        RootCauseFacts.Add(facts, result, t => "Model " + t);
        var keys = facts.Sorted().Select(f => f.Key).ToList();
        Assert.Contains("rca.cost.A.B.outputWithThinking.volume.share", keys);
        Assert.Contains("rca.within.B.cost.outputWithThinking.share", keys);
        Assert.All(keys.Where(k => k.Contains("outputWithThinking", StringComparison.Ordinal)), key =>
        {
            Assert.True(BenchmarkReportFactLabels.TryLabel(key, out string label), "No label for " + key);
            Assert.DoesNotContain(".", label.TrimEnd('.'));
        });
    }

    [Fact]
    public void APairWhereOnlyOneReportsReasoning_IsSplitIntoInputAndOutput_WithLabeledFacts()
    {
        var result = RootCauseEngine.Compute(ComparisonInput(
            Compared("A", 1, 85, 1.0, 1.0),
            Compared("B", 2, 75, 1.6, 1.2, reasoningReported: false)));

        var pair = Assert.Single(result.CostPairs, p => p.First == "A" && p.Second == "B");
        Assert.False(pair.NotEstablished);
        Assert.True(pair.Grouped);
        Assert.All(pair.Components, c => Assert.Contains(c.Key, new[] { RootCauseCostKeys.Input, RootCauseCostKeys.Output, RootCauseCostKeys.All }));
        Assert.Equal(pair.Difference!.Value, pair.Components.Where(c => c.Key != RootCauseCostKeys.All).Sum(c => c.Amount), 12);
        Assert.DoesNotContain(result.Hypotheses, h => h.Id == "HC3");

        // The rows keep each model's own components.
        Assert.Equal(RootCauseSegments.CostComponents, Assert.Single(result.CostRows, r => r.Token == "A").Segments.Select(s => s.Key));
        Assert.Equal(RootCauseSegments.OutputWithThinking, Assert.Single(result.CostRows, r => r.Token == "B").Segments[^1].Key);

        var facts = new BenchmarkReportFacts.FactList();
        RootCauseFacts.Add(facts, result, t => "Model " + t);
        var byKey = facts.Sorted().ToDictionary(f => f.Key, StringComparer.Ordinal);
        Assert.True(byKey["rca.cost.A.B.grouped"].Value!.GetValue<bool>());
        Assert.Equal(RootCauseFacts.GroupedDisplay, byKey["rca.cost.A.B.grouped"].Display);
        Assert.Contains("rca.cost.A.B.input.volume.share", byKey.Keys);
        Assert.Contains("rca.cost.A.B.output.price.share", byKey.Keys);
        Assert.Contains("rca.cost.A.B.volumeRatio", byKey.Keys);
        Assert.All(byKey.Keys.Where(k => k.StartsWith("rca.cost.A.B.", StringComparison.Ordinal)), key =>
        {
            Assert.True(BenchmarkReportFactLabels.TryLabel(key, out string label), "No label for " + key);
            Assert.DoesNotContain(".", label.TrimEnd('.'));
        });
        Assert.True(BenchmarkReportFactLabels.TryLabel("rca.cost.A.B.input.caching.share", out _));
        Assert.True(BenchmarkReportFactLabels.TryLabel("rca.cost.A.B.all.caching.interval", out _));
        Assert.False(BenchmarkReportFactLabels.TryLabel("rca.cost.A.B.output.caching.share", out _));
    }

    [Fact]
    public void AProviderThatCountsThinkingInOutput_HasItsReasoningFactsUnavailable_AndItsOutputFactsSaySo()
    {
        var evidence = new RootCauseModelEvidence
        {
            Answers = Enumerable.Range(1, 4).Select(q => new RootCauseAnswerRecord
            {
                ItemKey = "q" + q + "@1",
                QuestionKey = "q" + q,
                Ok = true,
                InputTokens = 1000,
                OutputTokens = 800,
                ReasoningReported = false,
                ComponentTokens = new double[] { 1000, 0, 0, 800, 0 },
                ComponentCost = new double[] { 0.002, 0, 0, 0.008, 0 }
            }).ToList(),
            RunCount = 1,
            RunsPerSuite = 1,
            Priced = true,
            ReasoningReported = false,
            PricesPerMillion = new double?[] { 2, 0.2, null, 10, 10 }
        };
        var facts = new BenchmarkReportFacts.FactList();
        facts.Add("tokens.outputPerQuestion", 800.0, "800");

        RootCauseEvidence.AddFacts(facts, evidence);

        var byKey = facts.Sorted().ToDictionary(f => f.Key, StringComparer.Ordinal);
        foreach (string key in new[] { "tokens.reasoningPerQuestion", "tokens.reasoningShare", "cost.component.reasoning.perQuestion", "price.reasoning.perMillion" })
        {
            Assert.False(byKey[key].Available, key);
            Assert.Equal(RootCauseEvidence.ReasoningNotReportedReason, byKey[key].UnavailableReason);
        }
        Assert.EndsWith(RootCauseEvidence.ThinkingIncluded, byKey["cost.component.output.perQuestion"].Display, StringComparison.Ordinal);
        Assert.EndsWith(RootCauseEvidence.ThinkingIncluded, byKey["tokens.outputPerQuestion"].Display, StringComparison.Ordinal);
        Assert.Equal(800.0, byKey["tokens.outputPerQuestion"].Value!.GetValue<double>(), 9);
        Assert.True(byKey["price.output.perMillion"].Available);
        Assert.False(CallTelemetryMeasures.ReportsReasoningSeparately(" anthropic "));
        Assert.True(CallTelemetryMeasures.ReportsReasoningSeparately("OpenAI"));
        Assert.True(CallTelemetryMeasures.ReportsReasoningSeparately(null));
    }
}
