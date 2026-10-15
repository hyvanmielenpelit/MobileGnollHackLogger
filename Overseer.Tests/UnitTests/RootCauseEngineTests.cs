namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using Overseer.Services.Benchmarking;
using Overseer.Services.Benchmarking.RootCause;
using Xunit;

/// <summary>
/// The root-cause engine end to end for a run-completion subject: the control limit with no, one and two
/// controls, the control run it proposes, the order of the recommended runs and the runs a replicate folds
/// in, the item-level control table, the chart rows and determinism; and for three compared models: which
/// differences are decomposed, the research questions, the model profiles and the hypothesis labels.
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
    /// </summary>
    private static RootCauseModelInput Compared(string letter, int number, double index, double speed, double cost)
    {
        var questions = Enumerable.Range(1, 12).ToList();
        var answers = questions.Select(q => Answer(q, q % 4 == 0 ? 30 : index - 10 + 2 * (q % 5), BandOf(q), noTool: q % 4 == 0, critical: letter == "C" && q == 3)).ToList();
        return new RootCauseModelInput
        {
            Token = letter,
            Letter = letter,
            Label = "Model label " + letter,
            Provider = "Provider " + letter,
            ModelId = "model-" + letter.ToLowerInvariant(),
            ThinkingLevel = "high",
            EntryKey = "run:" + (600 + number),
            RunIds = new long[] { 600 + number },
            QualityIndex = index,
            QualityLower = index - 5,
            QualityUpper = index + 5,
            Dimensions = new double?[] { index, index - 10, index - 3, index - 2 },
            ModelTimeP50Ms = 18000 * speed,
            CostPerQuestionUsd = 0.0183 * cost,
            CostPerRunUsd = 0.22 * cost,
            Evidence = new RootCauseModelEvidence
            {
                Answers = answers,
                RunCount = 1,
                RunsPerSuite = 1,
                Priced = true,
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
                        Tokens = item.Tokens.Select(t => t * cost).ToArray(),
                        Cost = item.Cost.Select((c, k) => c * cost * (1 + 0.01 * (q % 4) * k)).ToArray()
                    };
                }).ToList()
            }
        };
    }

    /// <summary>
    /// Three compared models: A best and the reference, B slowest, C lowest and most expensive. The paired
    /// speed test of A and B is established, of A and C not; no pair's cost is tested.
    /// </summary>
    private static RootCauseInput ComparisonInput() => new()
    {
        Scope = RootCauseEngine.ComparisonScope,
        Origin = RootCauseOrigins.Comparison,
        ComputedAtUtc = new DateTime(2026, 10, 15, 8, 0, 0, DateTimeKind.Utc),
        PricingBasis = "As run",
        PricingBasisKind = "AsRun",
        Models = new[]
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
    public void AReplicate_FoldsTheTimeOfDayAndThinkingPairRunsItCovers()
    {
        // The subject's time is mostly time to first token (WS1, a time-of-day repeat), and it answered every
        // fourth question without tools (WQ1, a replicate): the replicate covers the subject, so it folds the repeat in.
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
        Assert.Equal("start each run at a different time of day", replicate.When);
        Assert.Equal(RootCausePurposes.Chat, replicate.Purpose);
        Assert.Equal(1, replicate.Number);
        Assert.Equal(Enumerable.Range(1, result.RecommendedRuns.Count), result.RecommendedRuns.Select(r => r.Number));

        string summary = RootCauseNextRuns.Summary(replicate);
        Assert.StartsWith("Run 1, a replicate of ", summary);
        Assert.DoesNotContain(replicate.Decides, summary);
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
        Assert.Equal("Why did Model A score highest?", result.Questions[0].Text);
        Assert.Equal("Why did Model C score lowest?", result.Questions[1].Text);
        Assert.Equal("Why is Model B slowest?", result.Questions[2].Text);
        Assert.Equal("Why is Model C the most expensive?", result.Questions[3].Text);

        var facts = new BenchmarkReportFacts.FactList();
        RootCauseFacts.Add(facts, result, t => "Model " + t);
        var byKey = facts.Sorted().ToDictionary(f => f.Key, StringComparer.Ordinal);
        foreach (var question in result.Questions)
        {
            var about = result.Hypotheses
                .Where(h => h.Kind != "null" && h.Axis == question.Axis && h.Models.Contains(question.Token))
                .ToList();
            Assert.Equal(about.Select(h => h.Key).OrderBy(k => k, StringComparer.Ordinal), question.HypothesisKeys.OrderBy(k => k, StringComparer.Ordinal));
            var ranks = question.HypothesisKeys.Select(k => RootCauseGrades.Rank(about.Single(h => h.Key == k).Grade)).ToList();
            Assert.Equal(ranks.OrderByDescending(r => r).ToList(), ranks);

            string? best = about.Where(h => RootCauseGrades.Rank(h.Grade) >= RootCauseGrades.Rank(RootCauseGrades.Low))
                .OrderByDescending(h => RootCauseGrades.Rank(h.Grade)).Select(h => h.Grade).FirstOrDefault();
            Assert.Equal(best, question.Grade);

            string prefix = "rca.question." + question.Number + ".";
            Assert.Equal(question.Text, byKey[prefix + "text"].Display);
            Assert.Equal("Model " + question.Token, byKey[prefix + "model"].Display);
            Assert.Equal(RootCauseGrades.WordWithGrade(best), byKey[prefix + "status"].Display);
            Assert.True(byKey.ContainsKey(prefix + "hypotheses"));
            Assert.True(byKey.ContainsKey(prefix + "signals"));
        }

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
        Assert.Contains(a.Strengths, s => s.Text == "first on Accuracy of 3 models");
        Assert.Contains(a.Weaknesses, w => w.Kind == RootCauseProfiles.WeakestDimension && w.Text == "Completeness, 10.0 points below Accuracy");
        Assert.Contains(a.Weaknesses, w => w.Kind == RootCauseProfiles.LostPointsOn && w.Text.StartsWith("answers without tools: ", StringComparison.Ordinal));

        var c = result.Profiles.Single(p => p.Token == "C");
        Assert.Contains(c.Flags, f => f.Kind == RootCauseProfiles.CriticalErrors && f.Text == "1 critical error (Q3)");
        Assert.Contains(c.Weaknesses, w => w.Kind == RootCauseProfiles.LowestRank && w.Text == "last on Accuracy of 3 models");

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
        Assert.Equal("critical errors of Model F against Model E", RootCauseLabels.Of(critical, t => "Model " + t));
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
