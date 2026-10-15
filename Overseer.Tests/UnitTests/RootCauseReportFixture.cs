namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services;
using Overseer.Services.Benchmarking;
using Overseer.Services.Benchmarking.RootCause;
using Xunit;

/// <summary>
/// The Root Cause Analysis fixtures: the five-model comparison of <see cref="BenchmarkReportPackFixture"/>
/// with candidate call telemetry, token counts and price cards, so the speed and cost decompositions are
/// computed; a per-model pack, a run completion with and without controls, and a writer output for each.
/// Built through the real fact builders and the root-cause engine, so a golden file of this fixture
/// depends on both.
/// </summary>
internal static class RootCauseReportFixture
{
    public const long SubjectRunId = 31;

    /// <summary>The fixture's configurations: one per model, by run id, and the configured graders.</summary>
    public static RootCauseCatalog Catalog() => new()
    {
        Configurations = BenchmarkReportPackFixture.ComparisonModels
            .Select((m, i) => new RootCauseConfigOption
            {
                Id = 100 + i,
                Provider = m.Provider,
                ModelId = BenchmarkReportPackFixture.ModelIdOf(m.Label),
                ThinkingLevel = "high",
                Label = m.Label + " (high)"
            })
            .Append(new RootCauseConfigOption { Id = 150, Provider = "Northwind", ModelId = "orion-max", ThinkingLevel = "medium", Label = "Orion Max (medium)" })
            .ToList(),
        Recommended = BenchmarkReportPackFixture.ComparisonModels
            .Select(m => new RootCauseRecommendedModel { Provider = m.Provider, Model = BenchmarkReportPackFixture.ModelIdOf(m.Label), ThinkingLevel = "high" })
            .ToList()
    };

    /// <summary>The five models' runs with candidate telemetry, tokens and tool counts on every answer.</summary>
    public static Dictionary<long, BenchmarkRun> Runs()
    {
        var runs = BenchmarkReportPackFixture.FiveModelRuns();
        int index = 0;
        foreach (var m in BenchmarkReportPackFixture.ComparisonModels)
        {
            var run = runs[m.RunId];
            int i = index++;
            run.CallTelemetryVersion = 2;
            run.CompletedAtUtc = run.StartedAtUtc.AddMinutes(8 + 2 * i);
            run.TestedModelConfigurationId = 100 + i;
            run.TestedModelSnapshot!.ThinkingLevel = "high";
            foreach (var answer in run.Answers)
            {
                int q = answer.OrderIndex;
                int calls = 2 + (q + i) % 2;
                var telemetry = new List<ModelCallTelemetry>();
                int modelTime = 0;
                for (int k = 0; k < calls; k++)
                {
                    int first = 700 + 250 * i + 40 * k + 10 * q;
                    int end = first + 1800 + 350 * i + 90 * q;
                    modelTime += end;
                    telemetry.Add(new ModelCallTelemetry
                    {
                        Source = ModelCallSource.BenchmarkCandidate,
                        CallIndex = k,
                        Provider = m.Provider,
                        RequestedModelId = BenchmarkReportPackFixture.ModelIdOf(m.Label),
                        StartedAtUtc = run.StartedAtUtc.AddSeconds(q * 60 + k * 10),
                        FirstEventMs = first,
                        FirstOutputMs = first,
                        StreamEndMs = end,
                        CompletedMs = end,
                        OutputDeltaCount = 40,
                        VisibleOutputChars = 1200 + 100 * i,
                        InputTokens = 1500 + 200 * i,
                        OutputTokens = 300 + 50 * i,
                        ReasoningTokens = 100 + 30 * i
                    });
                }
                answer.ModelCalls = telemetry;
                answer.ModelCallCount = calls;
                answer.ToolTimeMs = 1500;
                answer.DurationMs = modelTime + 300 + 1500;
                answer.ToolCallCount = (q + i) % 3;
                answer.InputTokens = 4000 + 500 * i + 100 * q;
                answer.CacheReadInputTokens = 1000 + 200 * i;
                answer.OutputTokens = 900 + 150 * i + 20 * q;
                answer.ReasoningTokens = 300 + 80 * i;
            }
        }
        return runs;
    }

    /// <summary>Each run's candidate card on the catalog basis.</summary>
    public static Dictionary<long, ModelPricing?> Pricing()
        => BenchmarkReportPackFixture.ComparisonModels
            .Select((m, i) => (m.RunId, Card: (ModelPricing?)new ModelPricing(1.25m + i, 10m + 2 * i, CachedInputPerMillion: 0.125m, CacheWritePerMillion: 1.5m)))
            .ToDictionary(x => x.RunId, x => x.Card);

    /// <summary>The comparison-scope sheet and content over every model, with the root-cause engine fed.</summary>
    public static BenchmarkComparisonReportFactsResult ComparisonFacts()
    {
        var comparison = BenchmarkReportPackFixture.FiveModelComparison();
        var runs = Runs();
        var keys = BenchmarkComparisonReportFacts.DefaultCoveredKeys(comparison);
        var (reference, allPairs, unavailable) = BenchmarkComparisonReportFacts.PairedFromRuns(comparison, keys, runs, null);
        var result = BenchmarkComparisonReportFacts.BuildFromRuns(
            comparison, keys, runs, 120, reference, allPairs, unavailable, BenchmarkReportPackFixture.ComparisonNumber,
            candidatePricing: Pricing(), catalog: Catalog());
        Assert.True(result.Sheet != null, result.Refusal);
        return result;
    }

    /// <summary>A stored comparison-scope Root Cause Analysis over every model of the fixture comparison.</summary>
    public static BenchmarkReportDocument ComparisonDocument()
    {
        var document = BenchmarkReportPackFixture.ComparisonDocument(BenchmarkReportAudience.ExecutiveSummary);
        var built = ComparisonFacts();
        var sheet = built.Sheet!;
        document.Audience = BenchmarkReportAudience.RootCauseAnalysis;
        document.FactsJson = BenchmarkReportJson.Serialize(sheet);
        document.ContentJson = BenchmarkReportJson.Serialize(built.Content);
        document.WriterOutputJson = BenchmarkReportJson.Serialize(Writer(sheet, comparison: true));
        document.Title = BenchmarkReportPackRenderer.BuildComparisonTitle(BenchmarkReportAudience.RootCauseAnalysis, sheet,
            new Overseer.Services.Benchmarking.Pdf.BenchmarkPdfComparison(document.Comparison!.Id, document.Comparison.DisplayName, document.Comparison.EntryCount),
            BenchmarkReportPeerNaming.Named);
        return document;
    }

    /// <summary>The model-scope sheet of the fixture's first model, against its four peers (a per-model pack).</summary>
    public static BenchmarkReportFactSheet ModelPackSheet()
    {
        var built = BenchmarkReportFacts.Build(new BenchmarkReportFactsInput
        {
            Comparison = BenchmarkReportPackFixture.FiveModelComparison(),
            SubjectKey = "run:" + SubjectRunId,
            Runs = Runs(),
            CandidatePricing = Pricing(),
            Catalog = Catalog()
        });
        Assert.True(built.Sheet != null, built.Refusal);
        return built.Sheet!;
    }

    /// <summary>
    /// The model-scope sheet of the fixture's first model alone, as its run completion builds it, with
    /// <paramref name="controls"/>: the second and fifth models' runs as controls when true, none when false.
    /// </summary>
    public static BenchmarkReportFactSheet RunSheet(bool controls)
    {
        var all = BenchmarkReportPackFixture.FiveModelComparison();
        var comparison = BenchmarkReportPackFixture.Comparison(all.Entries.Single(e => e.Key == "run:" + SubjectRunId));
        comparison.ComputedAtUtc = all.ComputedAtUtc;
        var runs = Runs();
        var built = BenchmarkReportFacts.Build(new BenchmarkReportFactsInput
        {
            Comparison = comparison,
            SubjectKey = "run:" + SubjectRunId,
            Runs = new Dictionary<long, BenchmarkRun> { [SubjectRunId] = runs[SubjectRunId] },
            CandidatePricing = Pricing(),
            Catalog = Catalog(),
            Controls = controls ? Controls(runs) : RootCauseControlSet.None(RootCauseControls.NoRunControlReason, "Northwind", 3)
        });
        Assert.True(built.Sheet != null, built.Refusal);
        return built.Sheet!;
    }

    /// <summary>Two controls of other providers: the fixture's second and fifth models.</summary>
    public static RootCauseControlSet Controls(IReadOnlyDictionary<long, BenchmarkRun> runs)
    {
        var picked = new[] { 32L, 35L };
        return new RootCauseControlSet
        {
            SubjectProvider = "Northwind",
            CandidatesConsidered = 4,
            Controls = picked.Select((id, n) =>
            {
                var m = BenchmarkReportPackFixture.ComparisonModels.Single(x => x.RunId == id);
                return new RootCauseControl
                {
                    Number = n + 1,
                    ModelLabel = m.Label + " (high)",
                    Provider = m.Provider,
                    ModelId = BenchmarkReportPackFixture.ModelIdOf(m.Label),
                    ThinkingLevel = "high",
                    RunId = id,
                    DateUtc = runs[id].StartedAtUtc,
                    SharedQuestions = 6
                };
            }).ToList(),
            Scores = picked.Select((id, n) => new RootCauseControlScores
            {
                Number = n + 1,
                ScoresByItem = RootCauseControls.RunScores(new[] { runs[id] })
            }).ToList()
        };
    }

    /// <summary>A stored model-scope Root Cause Analysis over <paramref name="sheet"/>.</summary>
    public static BenchmarkReportDocument ModelDocument(BenchmarkReportFactSheet sheet, BenchmarkReportDocumentOrigin origin)
    {
        var runs = Runs();
        var writer = Writer(sheet, comparison: false);
        return new BenchmarkReportDocument
        {
            Id = 131,
            PackId = new Guid("3f2b8c1e-0000-4000-8000-000000000131"),
            Audience = BenchmarkReportAudience.RootCauseAnalysis,
            Origin = origin,
            Scope = BenchmarkReportScope.Model,
            SubjectKey = sheet.SubjectKey,
            SubjectLabel = sheet.SubjectLabel,
            SubjectRunIdsJson = BenchmarkReportJson.Serialize(sheet.SubjectRunIds),
            ComparisonRequestJson = "{\"runIds\":[31],\"groupIds\":[],\"pricingBasis\":1}",
            SuiteId = 5,
            SuiteName = sheet.SuiteName,
            WriterConfigId = 7,
            WriterDisplayName = "Claude Opus 5.5",
            WriterProvider = "Anthropic",
            WriterModelId = "claude-opus-5-5",
            WriterThinkingLevel = "high",
            ReportFormatVersion = BenchmarkReportPackRenderer.ReportFormatVersion,
            WriterPromptSha256 = new string('c', 64),
            AnswerExcerptChars = 120,
            FactsJson = BenchmarkReportJson.Serialize(sheet),
            ContentJson = BenchmarkReportJson.Serialize(BenchmarkReportContent.Build(new[] { runs[SubjectRunId] }, 120)),
            WriterOutputJson = BenchmarkReportJson.Serialize(writer),
            ValidationNotesJson = "[]",
            Title = BenchmarkReportPackRenderer.BuildTitle(BenchmarkReportAudience.RootCauseAnalysis, sheet),
            Status = BenchmarkReportDocumentStatus.Completed,
            CreatedAtUtc = BenchmarkReportPackFixture.CreatedAt,
            CreatedByUserId = "user-1",
            InputTokens = 24000,
            OutputTokens = 7000,
            DurationMs = 80000,
            CostUsd = 0.31m,
            PricingSource = "catalog",
            Runs = new List<BenchmarkReportDocumentRun>()
        };
    }

    /// <summary>
    /// The battery-completion Root Cause Analysis of <see cref="BatteryReportFixture"/>: its hand-built sheet
    /// with a root-cause result the engine computed over eight answers in two suites, two runs per suite,
    /// and no control, and the result's facts added to the sheet.
    /// </summary>
    public static BenchmarkReportDocument BatteryDocument()
    {
        var sheet = BatteryReportFixture.Sheet();
        string[] bands = { "Simple", "Intermediate" };
        var answers = Enumerable.Range(1, 8).Select(i =>
        {
            int suite = i <= 4 ? 1 : 2;
            int q = (i - 1) % 4 + 1;
            double quality = suite == 1 ? 80 + q : q == 3 ? 30 : 65 + q;
            return new RootCauseAnswerRecord
            {
                RunId = suite == 1 ? 12 : 21,
                ItemKey = "q" + (20 + i) + "@1",
                QuestionKey = "q" + (20 + i),
                Band = bands[q % 2],
                Suite = suite,
                Ok = true,
                Counting = true,
                Quality = quality,
                NoTool = q == 3,
                CriticalError = suite == 2 && q == 3,
                Accuracy = quality,
                Completeness = quality,
                Conciseness = 80,
                Readability = 80,
                DurationMs = 18000 + 1000 * i,
                ModelTimeMs = 14000 + 800 * i,
                ToolTimeMs = 2000,
                ModelCalls = 2
            };
        }).ToList();
        var items = answers.Select(a => new RootCauseItem
        {
            Key = a.QuestionKey,
            Band = a.Band,
            Suite = a.Suite,
            TtftMs = 2500 + 50 * a.Suite!.Value,
            DecodeMs = a.ModelTimeMs - 3000,
            ModelTimeMs = a.ModelTimeMs,
            DurationMs = a.DurationMs,
            ToolMs = 2000,
            WaitMs = 100,
            Calls = 2,
            OutputTokens = 1400
        }).ToList();
        var costs = answers.Select(a => new RootCauseItem
        {
            Key = a.QuestionKey,
            Band = a.Band,
            Suite = a.Suite,
            Tokens = new double[] { 4000, 1000, 0, 900, 500 },
            Cost = new double[] { 0.005, 0.0001, 0, 0.009, 0.005 }
        }).ToList();

        var input = new RootCauseInput
        {
            Scope = RootCauseEngine.ModelScope,
            Origin = RootCauseOrigins.BatteryCompletion,
            ComputedAtUtc = BenchmarkReportPackFixture.CreatedAt,
            PricingBasis = "As run",
            PricingBasisKind = "AsRun",
            Battery = true,
            Models = new[]
            {
                new RootCauseModelInput
                {
                    Token = RootCauseEngine.SubjectToken,
                    Label = sheet.SubjectLabel,
                    Provider = "Northwind",
                    ModelId = "orion-max",
                    ThinkingLevel = "high",
                    EntryKey = sheet.SubjectKey,
                    IsSubject = true,
                    RunIds = sheet.SubjectRunIds,
                    QualityIndex = 80,
                    QualityLower = 74,
                    QualityUpper = 86,
                    Dimensions = new double?[] { 80, 78, 82, 84 },
                    ModelTimeP50Ms = 18000,
                    CostPerQuestionUsd = 0.0191,
                    CostPerRunUsd = 0.12,
                    Evidence = new RootCauseModelEvidence
                    {
                        Answers = answers,
                        RunCount = 4,
                        RunsPerSuite = 2,
                        Priced = true,
                        CriticalErrorsConfirmed = true,
                        MeanMinutesPerRun = 7,
                        Settings = new RootCauseRunSettings
                        {
                            BatteryId = 3,
                            BatteryName = BatteryReportFixture.BatteryName,
                            TestedModelConfigurationId = 100,
                            ScoringProfileId = 1,
                            VerboseMode = false,
                            AllowSourceCodeReferences = true,
                            AssessorModelConfigurationId = 21
                        },
                        SpeedItems = items,
                        CostItems = costs
                    }
                }
            },
            Controls = RootCauseControlSet.None(RootCauseControls.NoBatteryControlReason, "Northwind", 2),
            Catalog = Catalog(),
            Questions = Enumerable.Range(1, 8).Select(i => new RootCauseQuestionRef
            {
                Number = i,
                Reference = "S" + (i <= 4 ? 1 : 2) + "-Q" + ((i - 1) % 4 + 1),
                ItemKey = "q" + (20 + i) + "@1"
            }).ToList(),
            SuiteNames = new Dictionary<int, string> { [1] = "Item lore", [2] = "Hazards" },
            LeaveOneSuiteOutChanges = new Dictionary<int, double> { [1] = -4.5, [2] = 3.0 }
        };

        var result = RootCauseEngine.Compute(input);
        sheet.RootCause = result;
        var facts = new BenchmarkReportFacts.FactList();
        RootCauseFacts.Add(facts, result, token => token == RootCauseEngine.SubjectToken ? sheet.SubjectLabel : token);
        sheet.Facts = sheet.Facts.Where(f => !f.Key.StartsWith(RootCauseFacts.Prefix, StringComparison.Ordinal))
            .Concat(facts.Sorted())
            .OrderBy(f => f.Key, StringComparer.Ordinal)
            .ToList();

        var document = BatteryReportFixture.Document(BenchmarkReportAudience.RootCauseAnalysis);
        document.FactsJson = BenchmarkReportJson.Serialize(sheet);
        document.WriterOutputJson = BenchmarkReportJson.Serialize(Writer(sheet, comparison: false));
        document.Title = BenchmarkReportPackRenderer.BuildTitle(BenchmarkReportAudience.RootCauseAnalysis, sheet);
        return document;
    }

    /// <summary>
    /// A Root Cause Analysis writer output over <paramref name="sheet"/> meant to pass every rule: each slot
    /// citing the axis statuses and a hypothesis where it weighs one, an answer to each research question the
    /// sheet asks citing its status and a hypothesis label, observations citing a question and an evidence id,
    /// one writer hypothesis at Low, and a topic for each question it cites whose text was given.
    /// </summary>
    public static BenchmarkReportWriterOutput Writer(BenchmarkReportFactSheet sheet, bool comparison)
    {
        string T(string inner) => "{{" + inner + "}}";
        var keys = sheet.Facts.Where(f => f.Available).Select(f => f.Key).ToHashSet(StringComparer.Ordinal);
        string Status(string axis) => T("rca.axis." + axis + ".status");
        string? Hypothesis(string axis)
            => sheet.RootCause?.Hypotheses
                .Where(h => h.Axis == axis && h.Kind != "null")
                .Select(h => "rca.hypothesis." + h.Key + ".statement")
                .FirstOrDefault(keys.Contains);
        string Weigh(string axis)
            => Hypothesis(axis) is string key
                ? "The strongest hypothesis in the table: " + T(key) + " Its grade and the evidence against it stand beside it."
                : "No hypothesis on this axis has more support than its null.";
        string? known = keys.Contains("rca.known.1") ? T("rca.known.1") : null;

        int cited = sheet.Questions.Where(q => q.Detailed != false).Select(q => q.Number).DefaultIfEmpty(1).First();
        string subject = comparison
            ? T("model:" + sheet.Peers.OrderBy(p => p.Letter, StringComparer.Ordinal).First().Letter)
            : T("subject");

        var sections = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [BenchmarkReportSlots.Scope] = "One run per suite on each side limits what this design can decide; the next runs add replicates.",
            [BenchmarkReportSlots.EvidenceIntelligence] = known != null ? "The first known fact: " + known :"The evidence tables list every figure.",
            [BenchmarkReportSlots.EvidenceSpeed] = "The speed tables split the time per question into its components.",
            [BenchmarkReportSlots.EvidenceCost] = "The cost tables split the cost per question into volume and price.",
            [BenchmarkReportSlots.HypothesesIntelligence] = Weigh(RootCauseAxes.Intelligence),
            [BenchmarkReportSlots.HypothesesSpeed] = Weigh(RootCauseAxes.Speed),
            [BenchmarkReportSlots.HypothesesCost] = Weigh(RootCauseAxes.Cost),
            [BenchmarkReportSlots.Conclusions] = "Intelligence: " + Status(RootCauseAxes.Intelligence) + ". Speed: " + Status(RootCauseAxes.Speed)
                + ". Cost: " + Status(RootCauseAxes.Cost) + ".",
            [BenchmarkReportSlots.NextRuns] = "The runs are ordered chat first, then the benchmark, then model selection.",
            [BenchmarkReportSlots.Limitations] = "The benchmark asks single questions, so longer conversations are not measured."
        };

        // One answer per research question the sheet asks, citing its status and, where one fired, a hypothesis label.
        foreach (var question in sheet.RootCause?.Questions ?? new List<RootCauseQuestion>())
        {
            string prefix = "rca.question." + question.Number + ".";
            if (!keys.Contains(prefix + "text")) continue;
            string? label = question.HypothesisKeys
                .Select(k => "rca.hypothesis." + k + ".label")
                .FirstOrDefault(keys.Contains);
            sections[BenchmarkReportSlots.RootCauseAnswerSlots[question.Number - 1]] =
                "For " + T(prefix + "model") + " the measured signals are " + T(prefix + "signals") + ". The answer strength is " + T(prefix + "status") + "."
                + (label != null ? " The leading hypothesis is " + T(label) + "." : string.Empty);
        }

        return new BenchmarkReportWriterOutput
        {
            Headline = "Intelligence: " + Status(RootCauseAxes.Intelligence) + "; speed: " + Status(RootCauseAxes.Speed)
                       + "; cost: " + Status(RootCauseAxes.Cost) + ".",
            Sections = sections,
            QuestionTopics = new List<BenchmarkReportQuestionTopic>
            {
                new() { Question = cited, Topic = "An item question about gems" }
            },
            Observations = new List<BenchmarkReportWriterItem>
            {
                new()
                {
                    Text = "The grader recorded a comment on this answer.",
                    Questions = new List<int> { cited },
                    Evidence = new List<string> { "Q" + cited }
                }
            },
            WriterHypotheses = new List<BenchmarkReportWriterHypothesis>
            {
                new()
                {
                    Axis = RootCauseAxes.Intelligence,
                    Models = new List<string> { comparison ? sheet.Peers.OrderBy(p => p.Letter, StringComparer.Ordinal).First().Letter : RootCauseEngine.SubjectToken },
                    Locus = RootCauseLoci.Suite,
                    Statement = subject + " may have lost points to a rubric that asks for more than the question states.",
                    For = new List<string> { "The grader comment on Q" + cited + " names a missing point." },
                    Against = new List<string> { "Other models answered the same question well." },
                    RefutedIf = "A rubric check finds every point stated in the question.",
                    Confidence = "Low",
                    Evidence = new List<string> { "Q" + cited }
                }
            }
        };
    }
}
