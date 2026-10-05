namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services.Benchmarking;
using Xunit;

/// <summary>
/// A hand-built battery document for the renderer, prompt, validator, cover and golden tests: the
/// stand-alone report fixture made a battery of two suites run twice each, its four questions
/// referred to as S1-Q1 … S2-Q2, two of them given in detail. Built by hand, not by
/// <see cref="BenchmarkBatteryReportFacts"/>, so the battery golden files depend on the renderer alone.
/// </summary>
internal static class BatteryReportFixture
{
    public const long BatteryRunId = 9;
    public const string BatteryName = "Core knowledge";

    public static readonly string[] References = { "S1-Q1", "S1-Q2", "S2-Q1", "S2-Q2" };

    public static BenchmarkReportFactSheet Sheet()
    {
        var sheet = BenchmarkReportPackFixture.StandaloneSheet();
        sheet.SubjectKey = "battery:9";
        sheet.SubjectKind = BenchmarkBatteryReportFacts.SubjectKind;
        sheet.SubjectRunIds = new List<long> { 12, 15, 21, 24 };
        sheet.SubjectExplanation = "Comparable: the battery definition and comparability class match the baseline.";
        sheet.SuiteId = null;
        sheet.SuiteName = BatteryName;
        sheet.Rows.Clear();
        sheet.Battery = new BenchmarkReportBatterySubject
        {
            BatteryRunId = BatteryRunId,
            Name = BatteryName,
            Revision = 2,
            Scheme = "Questions and difficulty",
            SuiteCount = 2,
            RunsPerSuite = 2,
            MemberRunCount = 4,
            Suites = new List<BenchmarkReportBatterySuite>
            {
                new() { Number = 1, SuiteId = 21, Name = "Item lore", QuestionCount = 2 },
                new() { Number = 2, SuiteId = 22, Name = "Hazards", QuestionCount = 2 }
            }
        };

        for (int i = 0; i < sheet.Questions.Count; i++)
        {
            var q = sheet.Questions[i];
            q.Reference = References[i];
            q.Suite = i < 2 ? 1 : 2;
            q.CriticalErrorCount = q.CriticalError ? 1 : 0;
            q.Detailed = i is 0 or 2;
            q.RunCount = 2;
        }

        var facts = sheet.Facts.ToDictionary(f => f.Key, StringComparer.Ordinal);
        facts["suite.name"] = Unavailable("suite.name", BenchmarkBatteryReportFacts.SuiteNameReason);
        facts["quality.intervalBasis"] = Fact("quality.intervalBasis", BenchmarkBatteryModelComparison.IntervalBasisItemSamplingOnly);
        foreach (string band in new[] { "simple", "intermediate", "advanced" })
        {
            facts["band." + band + ".score"] = Unavailable("band." + band + ".score", BenchmarkBatteryReportFacts.BandReason);
        }
        facts["tools.failed"] = Unavailable("tools.failed", BenchmarkBatteryReportFacts.ToolRowsReason);
        facts["tools.refusedByBudget"] = Unavailable("tools.refusedByBudget", BenchmarkBatteryReportFacts.ToolRowsReason);
        facts["run.ids"] = Fact("run.ids", "12, 15, 21, 24");
        facts["comparison.signature"] = Fact("comparison.signature", "5e1d0c7b3a91f08c");

        foreach (var fact in new[]
        {
            Fact("battery.name", BatteryName),
            Fact("battery.revision", "2", JsonValue.Create(2)),
            Fact("battery.scheme", "Questions and difficulty"),
            Fact("battery.suiteCount", "2 suites", JsonValue.Create(2)),
            Fact("battery.runsPerSuite", "2 runs", JsonValue.Create(2)),
            Fact("battery.memberRuns", "4 runs", JsonValue.Create(4)),
            Fact("battery.rounds", "2 complete rounds", JsonValue.Create(2)),
            Fact("battery.definitionSha256", "d41f0c9e2b7a"),
            Fact("battery.classSha256", "5e1d0c7b3a91"),
            Fact("battery.pooledIdentity", "the Overall Index equals one difficulty-weighted index over every question of every suite", JsonValue.Create(true)),
            Fact("battery.criticalErrorRate", "13 %", JsonValue.Create(0.125)),
            Fact("battery.speedIndex", "82 / 100", JsonValue.Create(82.0)),
            Fact("battery.suiteIndexSd", "7.1 points", JsonValue.Create(7.07)),
            Fact("battery.suiteIndexRange", "10.0 points", JsonValue.Create(10.0)),
            Fact("battery.excludedMembers", "0 member runs", JsonValue.Create(0)),
            Fact("battery.caveat.1", "Fewer than three complete rounds: the interval covers item sampling only."),
            Fact("suite.1.name", "Item lore"),
            Fact("suite.1.weight", "60.0 %", JsonValue.Create(0.6)),
            Fact("suite.1.index", "84 / 100", JsonValue.Create(84.0)),
            Fact("suite.1.contribution", "50.4 points", JsonValue.Create(50.4)),
            Fact("suite.1.interval", "78–90"),
            Fact("suite.1.scoredItems", "2 of 2 questions", JsonValue.Create(2)),
            Fact("suite.1.runs", "2 runs", JsonValue.Create(2)),
            Fact("suite.1.speedIndex", "84 / 100", JsonValue.Create(84.0)),
            Fact("suite.1.costPerRun", "$0.081", JsonValue.Create(0.081)),
            Fact("suite.1.criticalErrorRate", "0 %", JsonValue.Create(0.0)),
            Fact("suite.2.name", "Hazards"),
            Fact("suite.2.weight", "40.0 %", JsonValue.Create(0.4)),
            Fact("suite.2.index", "74 / 100", JsonValue.Create(74.0)),
            Fact("suite.2.contribution", "29.6 points", JsonValue.Create(29.6)),
            Unavailable("suite.2.interval", "No interval could be computed for this suite."),
            Fact("suite.2.scoredItems", "2 of 2 questions", JsonValue.Create(2)),
            Fact("suite.2.runs", "2 runs", JsonValue.Create(2)),
            Fact("suite.2.speedIndex", "80 / 100", JsonValue.Create(80.0)),
            Fact("suite.2.costPerRun", "$0.063", JsonValue.Create(0.063)),
            Fact("suite.2.criticalErrorRate", "25 %", JsonValue.Create(0.25)),
            Fact("sensitivity.difficultyMass", "80 / 100 under the Questions and difficulty weights (the declared scheme)", JsonValue.Create(80.0)),
            Fact("sensitivity.equal", "79 / 100 under the Equal per suite weights", JsonValue.Create(79.0)),
            Fact("sensitivity.itemCount", "79 / 100 under the Questions only weights", JsonValue.Create(79.0)),
            Fact("loo.1", "74 / 100 without Item lore, a change of -6.0 points", JsonValue.Create(74.0)),
            Fact("loo.2", "84 / 100 without Hazards, a change of +4.0 points", JsonValue.Create(84.0))
        })
        {
            facts[fact.Key] = fact;
        }

        sheet.Facts = facts.Values.OrderBy(f => f.Key, StringComparer.Ordinal).ToList();
        sheet.KnownNames = sheet.KnownNames
            .Concat(new[] { BatteryName, "Item lore", "Hazards" })
            .Where(n => n != "GnollHack Core Suite")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
        return sheet;
    }

    /// <summary>The fixture's content of the two questions given in detail, each from a run of its own suite.</summary>
    public static BenchmarkReportContentSnapshot Content()
    {
        var all = BenchmarkReportPackFixture.Content();
        var first = all.Runs[0].Questions.Single(q => q.Number == 1);
        var third = all.Runs[0].Questions.Single(q => q.Number == 3);
        return new BenchmarkReportContentSnapshot
        {
            AnswerExcerptChars = all.AnswerExcerptChars,
            Runs = new List<BenchmarkReportContentRun>
            {
                new() { RunId = 12, Questions = new List<BenchmarkReportContentQuestion> { first } },
                new() { RunId = 21, Questions = new List<BenchmarkReportContentQuestion> { third } }
            }
        };
    }

    /// <summary>A writer output for <see cref="Sheet"/>: battery references, no finding rows and no peers.</summary>
    public static BenchmarkReportWriterOutput Writer()
    {
        var writer = BenchmarkReportPackFixture.StandaloneWriter();
        writer.Sections[BenchmarkReportSlots.Abstract] =
            "{{subject}} scored {{quality.index}} across the {{battery.suiteCount}} of the battery, with one critical error on S2-Q1.";
        writer.Sections[BenchmarkReportSlots.WhyItScored] =
            "One critical error on S2-Q1 capped that answer at {{scoring.criticalErrorCap}}.\n\nThe Hazards suite held the composite down at {{suite.2.index}}.";
        writer.Sections[BenchmarkReportSlots.WhatWorked] = "Short, accurate answers on simple questions such as S1-Q1.";
        writer.Sections[BenchmarkReportSlots.Limitations] = "The result rests on two runs per suite, so its interval covers question sampling alone.";
        writer.Strengths = new List<BenchmarkReportWriterItem>
        {
            new() { Text = "Answers simple item questions precisely and briefly.", Questions = new List<int> { 1 }, Evidence = new List<string> { "S1-Q1" } }
        };
        writer.Weaknesses = new List<BenchmarkReportWriterItem>
        {
            new() { Text = "Asserted a false outcome on S2-Q1.", Questions = new List<int> { 3 }, Evidence = new List<string> { "S2-Q1" } },
            new() { Text = "Its weaker suite held the composite down ({{suite.2.index}}).", Evidence = new List<string> { "suite.2.index" } }
        };
        writer.Recommendations = new List<BenchmarkReportWriterRecommendation>
        {
            new()
            {
                For = BenchmarkReportSlots.TargetModelDevelopers,
                Text = "Asserting a destruction rule without checking it cost S2-Q1; verify object-destruction rules before stating them.",
                Questions = new List<int> { 3 },
                Evidence = new List<string> { "S2-Q1" }
            }
        };
        writer.QuestionTopics = new List<BenchmarkReportQuestionTopic>
        {
            new() { Question = 1, Topic = "Throwing gems at unicorns" },
            new() { Question = 3, Topic = "Breaking a thrown gem" }
        };
        writer.QuestionNotes = new List<BenchmarkReportQuestionNote>
        {
            new() { Question = 3, Note = "Claimed a thrown gem always shatters; the rubric says it can survive." }
        };
        writer.Leads.Clear();
        return writer;
    }

    /// <summary>The battery-completion document of <paramref name="audience"/>, stored without warnings.</summary>
    public static BenchmarkReportDocument Document(BenchmarkReportAudience audience)
    {
        var sheet = Sheet();
        return new BenchmarkReportDocument
        {
            Id = 202,
            PackId = new Guid("3f2b8c1e-0000-4000-8000-000000000202"),
            Audience = audience,
            Origin = BenchmarkReportDocumentOrigin.BatteryCompletion,
            SubjectKey = sheet.SubjectKey,
            SubjectLabel = sheet.SubjectLabel,
            SubjectRunIdsJson = "[12,15,21,24]",
            ComparisonRequestJson = "{\"runIds\":[],\"groupIds\":[],\"batteryRunIds\":[9],\"pricingBasis\":1}",
            SuiteId = null,
            SuiteName = BatteryName,
            WriterConfigId = 7,
            WriterDisplayName = "Claude Opus 5.5",
            WriterProvider = "Anthropic",
            WriterModelId = "claude-opus-5-5",
            WriterThinkingLevel = "high",
            SameProviderAcknowledged = false,
            ReportFormatVersion = BenchmarkReportPackRenderer.ReportFormatVersion,
            WriterPromptSha256 = new string('a', 64),
            AnswerExcerptChars = 120,
            FactsJson = BenchmarkReportJson.Serialize(sheet),
            ContentJson = BenchmarkReportJson.Serialize(Content()),
            WriterOutputJson = BenchmarkReportJson.Serialize(Writer()),
            ValidationNotesJson = BenchmarkReportJson.Serialize(new List<BenchmarkReportValidationNote>()),
            Title = BenchmarkReportPackRenderer.BuildTitle(audience, sheet),
            Status = BenchmarkReportDocumentStatus.Completed,
            CreatedAtUtc = BenchmarkReportPackFixture.CreatedAt,
            CreatedByUserId = "user-1",
            InputTokens = 30000,
            OutputTokens = 4000,
            DurationMs = 61000,
            CostUsd = 0.21m,
            PricingSource = "catalog"
        };
    }

    private static BenchmarkReportFact Fact(string key, string display, JsonNode? value = null)
        => new() { Key = key, Display = display, Value = value };

    private static BenchmarkReportFact Unavailable(string key, string reason)
        => new() { Key = key, Display = BenchmarkReportFacts.NotAvailable, Available = false, UnavailableReason = reason };
}

/// <summary>
/// The battery subject of a report document: its fact sheet read from the persisted battery analysis,
/// every generic key a run sheet carries, suite-qualified question references, the cap, priority and
/// budget of the questions given in detail, the validator and renderer reading those references, the
/// battery golden files, and the documents' own no-significance statement.
/// </summary>
public class BenchmarkBatteryReportFactsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static BenchmarkReportFact Fact(BenchmarkReportFactSheet sheet, string key) => sheet.Facts.Single(f => f.Key == key);

    /// <summary><see cref="BatteryReportFixture.Writer"/> with only the slots and lists of <paramref name="audience"/>.</summary>
    private static BenchmarkReportWriterOutput WriterFor(BenchmarkReportAudience audience)
    {
        var writer = BatteryReportFixture.Writer();
        var spec = BenchmarkReportSlots.For(audience);
        var slots = spec.SlotsFor(hasPeers: false);
        writer.Sections = writer.Sections.Where(kv => slots.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
        if (!spec.UsesRecommendations) writer.Recommendations.Clear();
        if (!spec.UsesQuestionNotes) writer.QuestionNotes.Clear();
        if (!spec.UsesLeads) writer.Leads.Clear();
        return writer;
    }

    /// <summary>Three rounds of both suites of <see cref="BenchmarkBatteryTestData.Definition"/>, analysed; round shifts 0, +5 and −5.</summary>
    private static async Task<long> SeedThreeRoundsAsync(
        ApplicationDbContext db, long firstRunId = 1, string modelId = "gpt-5.6-luna", Action<BenchmarkRun, int>? adjust = null)
    {
        var members = new List<(BenchmarkRun Run, int SuiteIndex, int Round)>();
        int[] shifts = { 0, 5, -5 };
        for (int round = 1; round <= 3; round++)
        {
            var a = BenchmarkBatteryTestData.SuiteARun(firstRunId + (round - 1) * 2, modelId, shifts[round - 1]);
            var b = BenchmarkBatteryTestData.SuiteBRun(firstRunId + (round - 1) * 2 + 1, modelId, shifts[round - 1]);
            adjust?.Invoke(a, round);
            adjust?.Invoke(b, round);
            members.Add((a, 0, round));
            members.Add((b, 1, round));
        }

        long id = await BenchmarkBatteryTestData.SeedAsync(db, BenchmarkBatteryTestData.Definition(), members.ToArray());
        var (analysis, _, _, error) = await BenchmarkBatteryTestData.Service(db).AnalyseAsync(id, null, null, Ct);
        Assert.True(analysis != null, error);
        return id;
    }

    private static async Task<BenchmarkReportPackPreparation> PrepareAsync(
        ApplicationDbContext db, long batteryRunId, IConfiguration? configuration = null, params long[] peerBatteryRunIds)
    {
        var request = BenchmarkReportPackPreparation.BatteryRequest(batteryRunId, new[] { BenchmarkReportAudience.TechnicalReport }, 0);
        request.BatteryRunIds.AddRange(peerBatteryRunIds);
        var (prep, refusal) = await BenchmarkReportPackPreparation.PrepareAsync(
            db, new BenchmarkModelComparisonService(db), request, BenchmarkReportPackPreparation.DefaultAnswerExcerptChars, Ct, configuration);
        Assert.True(prep != null, refusal);
        return prep!;
    }

    private static async Task<BenchmarkBatteryStatisticsResult> PersistedResultAsync(ApplicationDbContext db, long batteryRunId)
        => BenchmarkBatteryAnalysisService.DeserializeResult(await BenchmarkBatteryTestData.Service(db).GetLatestAsync(batteryRunId, Ct))!;

    private static IConfiguration Configuration(int? detailPerSuite = null, int? maxPromptChars = null)
    {
        var values = new Dictionary<string, string?>();
        if (detailPerSuite.HasValue) values[BenchmarkBatteryReportFacts.DetailQuestionsPerSuiteKey] = detailPerSuite.Value.ToString(CultureInfo.InvariantCulture);
        if (maxPromptChars.HasValue) values[BenchmarkBatteryReportFacts.MaxPromptCharsKey] = maxPromptChars.Value.ToString(CultureInfo.InvariantCulture);
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    // ---------------------------------------------------------------------------------------------
    // The sheet
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task ABatterySubject_IsReadFromThePersistedAnalysis()
    {
        await using var db = BenchmarkBatteryTestData.NewDb();
        long id = await SeedThreeRoundsAsync(db);
        var result = await PersistedResultAsync(db, id);

        var prep = await PrepareAsync(db, id);
        var sheet = prep.Sheet;

        Assert.Equal(BenchmarkBatteryReportFacts.SubjectKind, sheet.SubjectKind);
        Assert.Equal($"battery:{id}", sheet.SubjectKey);
        Assert.Equal("Core knowledge", sheet.SuiteName);
        Assert.Null(sheet.SuiteId);
        Assert.Empty(sheet.Peers);
        Assert.Empty(sheet.Rows);
        Assert.Empty(sheet.PairedDifferences);
        Assert.Equal(new long[] { 1, 2, 3, 4, 5, 6 }, sheet.SubjectRunIds);

        var battery = Assert.IsType<BenchmarkReportBatterySubject>(sheet.Battery);
        Assert.Equal(id, battery.BatteryRunId);
        Assert.Equal(2, battery.SuiteCount);
        Assert.Equal(3, battery.RunsPerSuite);
        Assert.Equal(6, battery.MemberRunCount);
        Assert.Equal(new[] { "Suite 21", "Suite 22" }, battery.Suites.Select(s => s.Name));

        var index = result.OverallIndex!;
        Assert.Equal(index.PointEstimate, Fact(sheet, "quality.index").Value!.GetValue<double>(), 12);
        Assert.Equal(BenchmarkReportFormat.Whole(index.PointEstimate) + " / 100", Fact(sheet, "quality.index").Display);
        Assert.Equal(BenchmarkBatteryModelComparison.IntervalBasisAcrossRounds, Fact(sheet, "quality.intervalBasis").Display);
        Assert.Equal("3 complete rounds", Fact(sheet, "battery.rounds").Display);
        Assert.Equal("2 suites", Fact(sheet, "battery.suiteCount").Display);
        Assert.Equal(BenchmarkBatteryReportFacts.SchemeName(result.Scheme), Fact(sheet, "battery.scheme").Display);

        foreach (var suite in result.Suites)
        {
            string prefix = "suite." + (suite.SuiteIndex + 1).ToString(CultureInfo.InvariantCulture) + ".";
            Assert.Equal(suite.SuiteName, Fact(sheet, prefix + "name").Display);
            Assert.Equal(suite.Index!.Value, Fact(sheet, prefix + "index").Value!.GetValue<double>(), 12);
            Assert.Equal(suite.Weight!.Value, Fact(sheet, prefix + "weight").Value!.GetValue<double>(), 12);
            Assert.Equal(suite.Contribution!.Value, Fact(sheet, prefix + "contribution").Value!.GetValue<double>(), 12);
            Assert.Equal(suite.UsableMemberCount, Fact(sheet, prefix + "runs").Value!.GetValue<int>());
        }

        foreach (var row in result.WeightingSensitivity)
        {
            Assert.Equal(row.Index, Fact(sheet, BenchmarkBatteryReportFacts.SensitivityKey(row.Scheme)).Value!.GetValue<double>(), 12);
        }
        foreach (var row in result.LeaveOneSuiteOut)
        {
            Assert.Contains(sheet.Facts, f => f.Key == "loo." + (row.SuiteIndex + 1).ToString(CultureInfo.InvariantCulture));
        }

        // What a battery cannot state is kept with its reason.
        Assert.Equal(BenchmarkBatteryReportFacts.SuiteNameReason, Fact(sheet, "suite.name").UnavailableReason);
        Assert.Equal(BenchmarkBatteryReportFacts.CompositeIndexReason, Fact(sheet, "quality.rawIndex").UnavailableReason);
        Assert.Equal("0", Fact(sheet, "tools.failed").Display);
        Assert.Equal(BenchmarkBatteryReportFacts.BandReason, Fact(sheet, "band.simple.score").UnavailableReason);
        Assert.Equal(BenchmarkBatteryReportFacts.StandaloneReason, Fact(sheet, "quality.rank").UnavailableReason);
        Assert.DoesNotContain(sheet.Facts, f => f.Key.StartsWith("peer.", StringComparison.Ordinal));
        Assert.Equal(string.Empty, sheet.NoSignificanceSummary);
        Assert.Empty(prep.Notes);
    }

    [Fact]
    public async Task ABatterySheet_SuppliesEveryKeyARunSheetCarries()
    {
        var run = BenchmarkBatteryTestData.SuiteARun(1);
        var runSheet = BenchmarkReportFacts.Build(BenchmarkReportPackFixture.Input(
            BenchmarkReportPackFixture.Comparison(BenchmarkReportPackFixture.Entry("run:1", new long[] { 1 }, "gpt-5.6-luna", "OpenAI", 70, 60, 80)),
            "run:1",
            run)).Sheet!;

        await using var db = BenchmarkBatteryTestData.NewDb();
        long id = await SeedThreeRoundsAsync(db);
        var batteryKeys = (await PrepareAsync(db, id)).Sheet.Facts.Select(f => f.Key).ToHashSet(StringComparer.Ordinal);

        var missing = runSheet.Facts.Select(f => f.Key).Where(k => !batteryKeys.Contains(k)).ToList();
        Assert.Empty(missing);
    }

    [Fact]
    public async Task APanelBattery_StatesItsPanelFacts_FromThePersistedAgreementBlock()
    {
        await using var db = BenchmarkBatteryTestData.NewDb();
        // Member B scores every answer as member A did.
        long id = await SeedThreeRoundsAsync(db, adjust: (run, _) => BenchmarkBatteryTestData.AsPanelRun(run));
        var panel = (await PersistedResultAsync(db, id)).PanelAgreement;
        Assert.NotNull(panel);

        var sheet = (await PrepareAsync(db, id)).Sheet;

        // Three rounds of both suites: 15 answers. Suite indices 70 and 60 under w = 0.6 / 0.4.
        Assert.Equal(15, panel!.PairCount);
        var icc = Fact(sheet, "panel.icc");
        Assert.Equal(panel.IntraclassCorrelation!.Value, icc.Value!.GetValue<double>(), 12);
        Assert.Equal("1.00 (pooled over 15 answers both members scored)", icc.Display);
        Assert.Equal("66.0 / 100", Fact(sheet, "panel.memberAAlone").Display);
        Assert.Equal("66.0 / 100", Fact(sheet, "panel.memberBAlone").Display);
        Assert.Equal("0.0 points", Fact(sheet, "panel.meanAbsDelta").Display);
        Assert.Single(sheet.Facts, f => f.Key == "panel.icc");

        // No reference reader graded, so its facts keep the run-level reason.
        Assert.False(Fact(sheet, "panel.referenceReaderIndex").Available);
    }

    [Fact]
    public void TheRunLevelTotalCostLabel_NamesWhatItCovers()
    {
        Assert.Equal(
            "Total cost per run (every grading and synthesis role; report writer excluded)",
            BenchmarkReportFactLabels.Label("cost.totalRunPerRun"));
    }

    [Fact]
    public async Task EveryQuestion_HasASuiteQualifiedReference_AndItsPersistedMean()
    {
        await using var db = BenchmarkBatteryTestData.NewDb();
        long id = await SeedThreeRoundsAsync(db);
        var result = await PersistedResultAsync(db, id);

        var sheet = (await PrepareAsync(db, id)).Sheet;

        Assert.Equal(new[] { "S1-Q1", "S1-Q2", "S1-Q3", "S2-Q1", "S2-Q2" }, sheet.Questions.Select(q => q.Reference));
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, sheet.Questions.Select(q => q.Number));
        Assert.Equal(new int?[] { 1, 1, 1, 2, 2 }, sheet.Questions.Select(q => q.Suite));
        Assert.All(sheet.Questions, q => Assert.Equal(3, q.RunCount));

        var items = result.Suites.SelectMany(s => s.Statistics!.Items.OrderBy(i => i.OrderIndex)).ToList();
        Assert.Equal(items.Select(i => i.Mean), sheet.Questions.Select(q => q.Score!.Value));
        Assert.Equal(items.Select(i => i.QuestionId.ToString(CultureInfo.InvariantCulture)), sheet.Questions.Select(q => q.QuestionKey));
        Assert.Equal("0 of 15 answers", Fact(sheet, "errors.critical").Display);
    }

    [Fact]
    public async Task TheDetail_TakesCriticalErrorsFirst_ThenTheLowestMean_FromTheMedianRound()
    {
        await using var db = BenchmarkBatteryTestData.NewDb();
        // S1-Q3 draws a critical error in round 2 only.
        long id = await SeedThreeRoundsAsync(db, adjust: (run, round) =>
        {
            if (round == 2 && run.BenchmarkSuiteId == BenchmarkBatteryTestData.SuiteA)
            {
                run.Answers.Single(a => a.OrderIndex == 3).CriticalError = true;
            }
        });

        var prep = await PrepareAsync(db, id, Configuration(detailPerSuite: 1));

        var detailed = prep.Sheet.Questions.Where(q => q.Detailed == true).Select(q => q.Reference).ToList();
        Assert.Equal(new[] { "S1-Q3", "S2-Q2" }, detailed);
        Assert.Equal(1, prep.Sheet.Questions.Single(q => q.Reference == "S1-Q3").CriticalErrorCount);

        // S1-Q3 scored 80, 85 and 75 in rounds 1 to 3, S2-Q2 50, 55 and 45: the median rounds are runs 1 and 2.
        var blocks = prep.Content.Runs.ToDictionary(r => r.RunId, r => r.Questions.Single());
        Assert.Equal(new long[] { 1, 2 }, blocks.Keys.OrderBy(k => k));
        Assert.Equal(3, blocks[1].Number);
        Assert.Equal(5, blocks[2].Number);
        Assert.Equal("S21Q3", blocks[1].QuestionText);

        // Only the questions given in detail need a topic, and a note where they scored low or erred.
        Assert.Equal(new[] { 3, 5 }, BenchmarkReportPackPrompt.QuestionsNeedingTopic(prep.Sheet));
        Assert.Equal(new[] { 3 }, BenchmarkReportPackPrompt.QuestionsNeedingNote(prep.Sheet));
    }

    [Fact]
    public async Task OtherBatteryResults_ArePeersWithIndexIntervalSpeedAndCost_AndNoPairedTest()
    {
        await using var db = BenchmarkBatteryTestData.NewDb();
        long luna = await SeedThreeRoundsAsync(db, 1);
        long opus = await SeedThreeRoundsAsync(db, 101, "claude-opus-5", (run, _) =>
        {
            foreach (var answer in run.Answers) answer.QualityScore += 10;
        });

        var sheet = (await PrepareAsync(db, luna, null, opus)).Sheet;

        var peer = Assert.Single(sheet.Peers);
        Assert.Equal("A", peer.Letter);
        Assert.Equal($"battery:{opus}", peer.EntryKey);
        foreach (string key in new[] { "quality.index", "quality.interval", "quality.rank", "intervalOverlap", "speed.medianSeconds", "cost.perQuestion", "runs" })
        {
            Assert.Contains(sheet.Facts, f => f.Key == "peer.A." + key);
        }
        Assert.DoesNotContain(sheet.Facts, f => f.Key.StartsWith("peer.A.paired", StringComparison.Ordinal));
        Assert.DoesNotContain(sheet.Facts, f => f.Key == "peer.A.sharedQuestions");
        Assert.Empty(sheet.PairedDifferences);
        Assert.Equal(BenchmarkReportFacts.NoSignificanceSummaryOfTwo, sheet.NoSignificanceSummary);
        Assert.Equal(2, sheet.Entries.Count);
        Assert.Equal("2nd of 2", Fact(sheet, "quality.rank").Display);

        string message = BenchmarkReportPackPrompt.Build(BenchmarkReportAudience.ExecutiveSummary, sheet, new BenchmarkReportContentSnapshot()).UserMessage;
        Assert.Contains("NO SIGNIFICANCE TEST (the comparison's own statement; code prints it in the document)\n"
            + BenchmarkReportFacts.NoSignificanceSummaryOfTwo + "\n", message);
    }

    [Fact]
    public async Task ARequestMixingBatteryResultsWithRuns_IsRefused()
    {
        await using var db = BenchmarkBatteryTestData.NewDb();
        long id = await SeedThreeRoundsAsync(db);
        var request = BenchmarkReportPackPreparation.BatteryRequest(id, new[] { BenchmarkReportAudience.ExecutiveSummary }, 0);
        request.RunIds.Add(1);

        var (prep, refusal) = await BenchmarkReportPackPreparation.PrepareAsync(
            db, new BenchmarkModelComparisonService(db), request, BenchmarkReportPackPreparation.DefaultAnswerExcerptChars, Ct);

        Assert.Null(prep);
        Assert.Equal(BenchmarkBatteryModelComparison.MixedSourcesError, refusal);
    }

    [Fact]
    public async Task TheBatteryEntry_IsDatedFromItsFirstStartToItsLastCompletion()
    {
        await using var db = BenchmarkBatteryTestData.NewDb();
        // Round r starts on October r, suite A at 08:00 and suite B at 09:00; each run takes 30 minutes.
        long id = await SeedThreeRoundsAsync(db, adjust: (run, round) =>
        {
            int hour = run.BenchmarkSuiteId == BenchmarkBatteryTestData.SuiteA ? 8 : 9;
            run.StartedAtUtc = new DateTime(2026, 10, round, hour, 0, 0, DateTimeKind.Utc);
            run.CompletedAtUtc = run.StartedAtUtc.AddMinutes(30);
        });

        var entry = Assert.Single((await PrepareAsync(db, id)).Sheet.Entries, e => e.IsSubject);

        Assert.Equal((DateTime?)new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc), entry.FirstRunUtc);
        Assert.Equal((DateTime?)new DateTime(2026, 10, 3, 9, 30, 0, DateTimeKind.Utc), entry.LastRunUtc);
    }

    // ---------------------------------------------------------------------------------------------
    // Tool-call outcomes and refuted answer sentences
    // ---------------------------------------------------------------------------------------------

    private static BenchmarkRunAnswerToolCall Call(int order, string? status, string? error = null) => new()
    {
        SortOrder = order,
        Name = "wiki_search",
        Status = status,
        Error = error,
        ArgsText = "{\"query\":\"gems\"}",
        Result = "A result."
    };

    private static string Verifications(params (string Claim, BenchmarkClaimVerdict Verdict, string? Role)[] rulings)
        => System.Text.Json.JsonSerializer.Serialize(rulings
            .Select(r => new BenchmarkClaimVerification(0, r.Claim, r.Verdict, null, null) { Roles = r.Role == null ? null : new[] { r.Role } })
            .ToArray());

    /// <summary>The battery subject's sheet, built as the preparation builds it, with <paramref name="outcomes"/> as its tool-call outcomes.</summary>
    private static async Task<BenchmarkReportFactSheet> BuildSheetAsync(ApplicationDbContext db, long batteryRunId, BenchmarkBatteryAnswerOutcomes? outcomes)
    {
        var request = BenchmarkReportPackPreparation.BatteryRequest(batteryRunId, new[] { BenchmarkReportAudience.TechnicalReport }, 0);
        var (comparison, subject, refusal) = await BenchmarkReportPackPreparation.CompareAsync(new BenchmarkModelComparisonService(db), request, Ct);
        Assert.True(refusal == null, refusal);
        var (sources, error) = await BenchmarkBatteryModelComparison.LoadAsync(db, new[] { batteryRunId }, Ct);
        Assert.True(sources != null, error);

        var runIds = subject!.RunIds.Distinct().ToList();
        var runs = await db.BenchmarkRuns.AsNoTracking().Include(r => r.Answers).Where(r => runIds.Contains(r.Id)).ToListAsync(Ct);
        var built = BenchmarkBatteryReportFacts.Build(new BenchmarkBatteryReportFactsInput
        {
            Comparison = comparison!,
            SubjectKey = subject.Key,
            Sources = sources!,
            Runs = runs.ToDictionary(r => r.Id),
            AnswerOutcomes = outcomes
        });
        Assert.True(built.Sheet != null, built.Refusal);
        return built.Sheet!;
    }

    [Fact]
    public async Task TheToolCallOutcomes_AreCountedOverTheMembersPerCallRows_ByTheRunRule()
    {
        await using var db = BenchmarkBatteryTestData.NewDb();
        // Round 1 of suite A: a completed call, a failed call and a budget refusal on its first answer.
        // Round 2 of suite B: a call that recorded no status, which counts as failed, and a completed one.
        long id = await SeedThreeRoundsAsync(db, adjust: (run, round) =>
        {
            if (round == 1 && run.BenchmarkSuiteId == BenchmarkBatteryTestData.SuiteA)
            {
                run.Answers[0].ToolCalls.AddRange(new[]
                {
                    Call(0, "completed"),
                    Call(1, "failed", "Tool wiki_search failed: timeout."),
                    Call(2, "failed", BenchmarkToolCallRecorder.PerQuestionBudgetRefusalMarker + ".")
                });
            }
            if (round == 2 && run.BenchmarkSuiteId == BenchmarkBatteryTestData.SuiteB)
            {
                run.Answers[1].ToolCalls.AddRange(new[] { Call(0, null), Call(1, "Completed") });
            }
        });

        var outcomes = await BenchmarkBatteryAnswerOutcomes.LoadAsync(db, new long[] { 1, 2, 3, 4, 5, 6 }, withRefutedSentences: false, Ct);
        var sheet = await BuildSheetAsync(db, id, outcomes);

        Assert.True(Fact(sheet, "tools.failed").Available);
        Assert.Equal("2", Fact(sheet, "tools.failed").Display);
        Assert.Equal(2, Fact(sheet, "tools.failed").Value!.GetValue<int>());
        Assert.Equal("1", Fact(sheet, "tools.refusedByBudget").Display);
        Assert.Single(sheet.Facts, f => f.Key == "tools.failed");

        // Outcomes a member run predates are kept unavailable with the run report's reason.
        var unrecorded = new BenchmarkBatteryAnswerOutcomes { ToolCallsUnavailableReason = BenchmarkBatteryAnswerOutcomes.ToolRecordsReason };
        var withheld = await BuildSheetAsync(db, id, unrecorded);
        Assert.False(Fact(withheld, "tools.failed").Available);
        Assert.Equal(BenchmarkBatteryAnswerOutcomes.ToolRecordsReason, Fact(withheld, "tools.failed").UnavailableReason);
        Assert.Equal(BenchmarkBatteryAnswerOutcomes.ToolRecordsReason, Fact(withheld, "tools.refusedByBudget").UnavailableReason);

        // Outcomes that were not loaded say so.
        var unloaded = await BuildSheetAsync(db, id, null);
        Assert.Equal(BenchmarkBatteryReportFacts.ToolRowsReason, Fact(unloaded, "tools.failed").UnavailableReason);
    }

    [Fact]
    public async Task TheAnswerOutcomes_CountOnlyTheAnswersThatCountTowardTheIndex_OfTheRunsAsked()
    {
        await using var db = BenchmarkBatteryTestData.NewDb();
        var first = BenchmarkBatteryTestData.SuiteARun(1);
        first.Answers[0].ToolCalls.AddRange(new[] { Call(0, "completed"), Call(1, "failed") });
        // An empty answer with no finish reason counts toward no index, so its calls are not counted.
        first.Answers[1].Status = BenchmarkAnswerStatus.EmptyAnswer;
        first.Answers[1].ToolCalls.Add(Call(0, "failed"));
        first.Answers[2].ClaimVerificationJson = Verifications(
            ("Sentence one.", BenchmarkClaimVerdict.Refuted, BenchmarkClaimRoles.UnverifiedClaim),
            ("Sentence two.", BenchmarkClaimVerdict.Refuted, BenchmarkClaimRoles.AccusedQuote),
            ("A grader's statement.", BenchmarkClaimVerdict.Refuted, BenchmarkClaimRoles.AssessorStatement));
        var second = BenchmarkBatteryTestData.SuiteBRun(2);
        second.Answers[0].ToolCalls.Add(Call(0, "failed", BenchmarkToolCallRecorder.BudgetRefusalMarker));
        var other = BenchmarkBatteryTestData.SuiteARun(3);
        other.Answers[0].ToolCalls.Add(Call(0, "failed"));
        db.BenchmarkRuns.AddRange(first, second, other);
        await db.SaveChangesAsync(Ct);

        var outcomes = await BenchmarkBatteryAnswerOutcomes.LoadAsync(db, new long[] { 1, 2 }, withRefutedSentences: true, Ct);

        Assert.Null(outcomes.ToolCallsUnavailableReason);
        Assert.Equal(1, outcomes.ToolCallsFailed);
        Assert.Equal(1, outcomes.ToolCallsRefusedByBudget);
        Assert.Equal(2, outcomes.RefutedAnswerSentences);

        var withoutSentences = await BenchmarkBatteryAnswerOutcomes.LoadAsync(db, new long[] { 1, 2 }, withRefutedSentences: false, Ct);
        Assert.Null(withoutSentences.RefutedAnswerSentences);
        Assert.Equal(1, withoutSentences.ToolCallsFailed);
    }

    [Fact]
    public async Task TheAnswerOutcomes_AreUnavailable_WhenAMemberRunPredatesPerCallToolRecords()
    {
        await using var db = BenchmarkBatteryTestData.NewDb();
        var old = BenchmarkBatteryTestData.Run(1, BenchmarkBatteryTestData.SuiteA, new[] { 60, 70, 80 }, new[] { 40, 40, 40 }, harnessVersion: "16");
        var current = BenchmarkBatteryTestData.SuiteBRun(2);
        current.Answers[0].ToolCalls.Add(Call(0, "failed"));
        db.BenchmarkRuns.AddRange(old, current);
        await db.SaveChangesAsync(Ct);

        var outcomes = await BenchmarkBatteryAnswerOutcomes.LoadAsync(db, new long[] { 1, 2 }, withRefutedSentences: false, Ct);

        Assert.Equal(BenchmarkBatteryAnswerOutcomes.ToolRecordsReason, outcomes.ToolCallsUnavailableReason);
        Assert.Null(outcomes.ToolCallsFailed);
        Assert.Null(outcomes.ToolCallsRefusedByBudget);
    }

    [Fact]
    public async Task TheRefutedAnswerSentences_AreTheQuestionsSum_AccusedOnesIncluded()
    {
        await using var db = BenchmarkBatteryTestData.NewDb();
        long id = await SeedThreeRoundsAsync(db, adjust: (run, round) =>
        {
            if (round == 1 && run.BenchmarkSuiteId == BenchmarkBatteryTestData.SuiteA)
            {
                run.ClaimsSupportedCount = 1;
                run.ClaimsRefutedCount = 1;
                run.Answers[0].ClaimVerificationJson = Verifications(
                    ("Sentence one.", BenchmarkClaimVerdict.Refuted, BenchmarkClaimRoles.UnverifiedClaim),
                    ("Sentence two.", BenchmarkClaimVerdict.Refuted, BenchmarkClaimRoles.AccusedQuote),
                    ("Sentence three.", BenchmarkClaimVerdict.Supported, BenchmarkClaimRoles.UnverifiedClaim),
                    ("A grader's statement.", BenchmarkClaimVerdict.Refuted, BenchmarkClaimRoles.AssessorStatement));
            }
            if (round == 3 && run.BenchmarkSuiteId == BenchmarkBatteryTestData.SuiteB)
            {
                run.Answers[1].ClaimVerificationJson = Verifications(
                    ("Another sentence.", BenchmarkClaimVerdict.Refuted, BenchmarkClaimRoles.AccusedQuote));
            }
        });

        var sheet = (await PrepareAsync(db, id)).Sheet;

        Assert.Equal("1", Fact(sheet, "claims.refuted").Display);
        Assert.Equal("3", Fact(sheet, "claims.refutedAnswerSentences").Display);
        Assert.Equal(sheet.Questions.Sum(q => q.RefutedAnswerSentences!.Value), Fact(sheet, "claims.refutedAnswerSentences").Value!.GetValue<int>());
        Assert.Equal("Refuted answer sentences, accused sentences included", BenchmarkReportFactLabels.Label("claims.refutedAnswerSentences"));
        Assert.Contains("claims.refutedAnswerSentences = 3\n",
            BenchmarkReportPackPrompt.Build(BenchmarkReportAudience.TechnicalReport, sheet, new BenchmarkReportContentSnapshot()).UserMessage);
    }

    [Fact]
    public async Task TheRefutedAnswerSentences_AreUnavailable_WhenAVerificationRecordsNoRoles()
    {
        await using var db = BenchmarkBatteryTestData.NewDb();
        long id = await SeedThreeRoundsAsync(db, adjust: (run, round) =>
        {
            if (round == 1 && run.BenchmarkSuiteId == BenchmarkBatteryTestData.SuiteA)
            {
                run.ClaimsRefutedCount = 1;
                run.Answers[0].ClaimVerificationJson = Verifications(("A sentence.", BenchmarkClaimVerdict.Refuted, null));
            }
        });

        var sheet = (await PrepareAsync(db, id)).Sheet;

        Assert.Equal("1", Fact(sheet, "claims.refuted").Display);
        Assert.False(Fact(sheet, "claims.refutedAnswerSentences").Available);
        Assert.Equal(BenchmarkBatteryReportFacts.RefutedSentencesReason, Fact(sheet, "claims.refutedAnswerSentences").UnavailableReason);
    }

    // ---------------------------------------------------------------------------------------------
    // The prompt budget
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task TwelveSuitesOfTenRounds_StayWithinThePromptBudget_AndSayWhatWasLeftOut()
    {
        const int suiteCount = 12;
        const int rounds = 10;
        int[] scores = { 35, 48, 62, 71, 84, 93 };
        int[] difficulties = { 30, 40, 50, 60, 70, 80 };
        string rubric = string.Concat(Enumerable.Repeat("- The rubric states this point about the game in full. ", 110));
        string answer = string.Concat(Enumerable.Repeat("The answer explains the mechanic at length. ", 50));
        string question = string.Concat(Enumerable.Repeat("How does this part of the game work? ", 10));

        await using var db = BenchmarkBatteryTestData.NewDb();
        var suites = Enumerable.Range(0, suiteCount)
            .Select(i => new BenchmarkBatteryDefinitionSuite(i, 31 + i, BenchmarkBatteryTestData.SuiteName(31 + i), null))
            .ToList();
        var definition = new BenchmarkBatteryDefinition(2, "Wide battery", 1, BenchmarkBatteryWeightingScheme.DifficultyMass, suites);

        var members = new List<(BenchmarkRun Run, int SuiteIndex, int Round)>();
        long runId = 1000;
        for (int round = 1; round <= rounds; round++)
        {
            for (int s = 0; s < suiteCount; s++)
            {
                var run = BenchmarkBatteryTestData.Run(++runId, 31 + s, scores.Select(v => v + round % 3).ToArray(), difficulties);
                foreach (var a in run.Answers)
                {
                    a.QuestionText = question + a.QuestionText;
                    a.ExpectedPointsUsed = rubric;
                    a.ExpectedPointsRecorded = true;
                    a.AnswerText = answer;
                    a.ReviewComment = "The grader explains its reading of the answer in a few sentences.";
                }
                members.Add((run, s, round));
            }
        }

        long id = await BenchmarkBatteryTestData.SeedAsync(db, definition, members.ToArray());
        var (analysis, _, _, error) = await BenchmarkBatteryTestData.Service(db).AnalyseAsync(id, null, null, Ct);
        Assert.True(analysis != null, error);

        var prep = await PrepareAsync(db, id);

        Assert.Equal(suiteCount * scores.Length, prep.Sheet.Questions.Count);
        int chars = BenchmarkBatteryReportFacts.PromptChars(prep.Sheet, prep.Content);
        Assert.True(chars <= BenchmarkBatteryReportFacts.DefaultMaxPromptChars,
            $"The largest prompt is {chars} characters, above {BenchmarkBatteryReportFacts.DefaultMaxPromptChars}.");

        var note = Assert.Single(prep.Notes);
        Assert.Equal(BenchmarkBatteryReportFacts.PromptBudgetRule, note.Rule);
        Assert.Equal("prompt", note.Location);
        Assert.False(note.Dropped);
        Assert.False(BenchmarkReportPackValidator.IsWarningRule(note.Rule));

        // Some detail was kept, the cap held, and every question left out is named in the note.
        var detailed = prep.Sheet.Questions.Where(q => q.Detailed == true).ToList();
        Assert.NotEmpty(detailed);
        Assert.All(detailed.GroupBy(q => q.Suite), g => Assert.True(g.Count() <= BenchmarkBatteryReportFacts.DefaultDetailQuestionsPerSuite));
        var leftOut = prep.Sheet.Questions.Where(q => q.Detailed != true).Select(q => q.Reference!).ToList();
        Assert.All(leftOut, r => Assert.Contains(r, note.Message));
        Assert.Equal(detailed.Select(q => q.Number).OrderBy(n => n), prep.Content.Runs.SelectMany(r => r.Questions).Select(q => q.Number).OrderBy(n => n));
    }

    // ---------------------------------------------------------------------------------------------
    // Validator, renderer and goldens
    // ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(BenchmarkReportAudience.ExecutiveSummary)]
    [InlineData(BenchmarkReportAudience.TechnicalReport)]
    public void TheValidator_ResolvesSuiteQualifiedReferences(BenchmarkReportAudience audience)
    {
        var notes = BenchmarkReportPackValidator.Validate(
            audience, WriterFor(audience), BatteryReportFixture.Sheet(), BatteryReportFixture.Content());

        Assert.Empty(notes);
    }

    [Fact]
    public void TheValidator_RefusesABareOrUnknownQuestionReference_OnABatterySheet()
    {
        var writer = WriterFor(BenchmarkReportAudience.TechnicalReport);
        writer.Weaknesses[0].Text = "Asserted a false outcome on Q3 and on S9-Q1.";
        writer.Weaknesses[0].Evidence = new List<string> { "Q3" };
        writer.Strengths[0].Evidence = new List<string> { "S3-Q1" };

        var notes = BenchmarkReportPackValidator.Validate(
            BenchmarkReportAudience.TechnicalReport, writer, BatteryReportFixture.Sheet(), BatteryReportFixture.Content());

        Assert.Contains(notes, n => n.Rule == 4 && n.Location == "weaknesses[0]" && n.Message.StartsWith("S9-Q1 is not a question of the battery", StringComparison.Ordinal));
        Assert.Contains(notes, n => n.Rule == 4 && n.Location == "weaknesses[0]" && n.Message.StartsWith("Q3 names no suite", StringComparison.Ordinal));
        Assert.Contains(notes, n => n.Rule == 4 && n.Location == "weaknesses[0]" && n.Message.StartsWith("Evidence Q3 refers to no question of the battery", StringComparison.Ordinal));
        Assert.Contains(notes, n => n.Rule == 4 && n.Location == "strengths[0]" && n.Message.StartsWith("Evidence S3-Q1 refers to no question of the battery", StringComparison.Ordinal));
        Assert.DoesNotContain(notes, n => n.Rule == 3);
    }

    [Fact]
    public void TheValidator_AsksTopicsAndNotes_OnlyForTheQuestionsGivenInDetail()
    {
        var writer = WriterFor(BenchmarkReportAudience.TechnicalReport);
        writer.QuestionTopics.RemoveAll(t => t.Question == 3);
        writer.QuestionNotes.Clear();

        var notes = BenchmarkReportPackValidator.Validate(
            BenchmarkReportAudience.TechnicalReport, writer, BatteryReportFixture.Sheet(), BatteryReportFixture.Content());

        Assert.Contains(notes, n => n.Rule == 4 && n.Location == "questionTopics"
            && n.Message == "No topic for S2-Q1: every question given in detail needs one.");
        Assert.Contains(notes, n => n.Rule == BenchmarkReportPackValidator.MissingQuestionNoteRule
            && n.Message.StartsWith("No note for S2-Q1:", StringComparison.Ordinal));
    }

    [Fact]
    public void TheTechnicalReport_NamesTheBattery_ItsProfile_AndEveryQuestionByReference()
    {
        string text = BenchmarkReportPackRenderer.Render(
            BatteryReportFixture.Document(BenchmarkReportAudience.TechnicalReport),
            new BenchmarkReportRenderOptions { Disclosure = BenchmarkReportDisclosure.Full, PeerNaming = BenchmarkReportPeerNaming.Named });

        Assert.Contains("- **Battery:** Core knowledge, revision 2: 2 suites, 2 runs per suite, weighting scheme Questions and difficulty\n", text);
        Assert.Contains("- **Member runs:** 4 (battery run 9)\n", text);
        Assert.Contains("\n## Battery profile\n", text);
        Assert.Contains("| S1 · Item lore | 60.0 % | 84 / 100 | 78–90 | 50.4 points | 2 of 2 questions | 2 runs | 84 / 100 | $0.081 | 0 % |\n", text);
        Assert.Contains("| S2 · Hazards | 40.0 % | 74 / 100 | — |", text);
        Assert.Contains("\n### Weighting sensitivity\n", text);
        Assert.Contains("- S2: 84 / 100 without Hazards, a change of +4.0 points\n", text);
        Assert.Contains("| S2-Q1 | Breaking a thrown gem |", text);
        Assert.Contains("| S1-Q2 | — |", text);
        Assert.DoesNotContain("| Q1 |", text);
        Assert.Contains("\n### S1-Q1: Throwing gems at unicorns\n", text);
        Assert.Contains("\n### S2-Q1: Breaking a thrown gem\n", text);
        Assert.DoesNotContain("### S1-Q2", text);
        Assert.Contains("**S2-Q1** (Breaking a thrown gem): Claimed a thrown gem always shatters", text);
        Assert.Contains("  - *Evidence:* S1-Q1 (90 / 100)" + "\n", text);
        Assert.Contains("- Battery analysis: Fewer than three complete rounds: the interval covers item sampling only.\n", text);
        Assert.Contains("A battery report lists no synthesis findings; each member run's report has its own.", text);
        Assert.Contains("| Candidate cost per battery pass | $0.144 |", text);
        Assert.Contains("| Candidate cost per question | $0.036 |", text);
    }

    [Theory]
    [InlineData(BenchmarkReportAudience.ExecutiveSummary, "exec_battery_standalone.md")]
    [InlineData(BenchmarkReportAudience.TechnicalReport, "technical_battery_standalone.md")]
    public void TheBatteryForm_MatchesItsGoldenFile(BenchmarkReportAudience audience, string file)
    {
        string rendered = BenchmarkReportPackRenderer.Render(
            BatteryReportFixture.Document(audience),
            new BenchmarkReportRenderOptions { Disclosure = BenchmarkReportDisclosure.Full, PeerNaming = BenchmarkReportPeerNaming.Named });

        if (BenchmarkReportPackFixture.UpdateGoldens)
        {
            BenchmarkReportPackFixture.WriteGolden(file, rendered);
            return;
        }

        Assert.Equal(BenchmarkReportPackFixture.ReadGolden(file), rendered);
    }

    // ---------------------------------------------------------------------------------------------
    // The documents' own no-significance statement
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void TheNoSignificanceStatement_IsTheDocumentsOwnText()
    {
        Assert.Equal((string.Empty, string.Empty), BenchmarkReportFacts.NoSignificanceStatement(1));
        Assert.Equal(
            ("This view runs no significance test, so a gap between the two models may be noise.",
             "Put each model's runs in an analysis group, open one in the Multi-Run Analysis tab and choose the other under Compare with group."),
            BenchmarkReportFacts.NoSignificanceStatement(2));
        Assert.Equal(
            "Testing every pair among these 3 models at once would flag chance differences as significant, so this view tests none.",
            BenchmarkReportFacts.NoSignificanceStatement(3).Summary);
    }

    [Fact]
    public void ARunSheet_KeepsItsStatement_WhateverTheComparisonViewSays()
    {
        var comparison = BenchmarkReportPackFixture.Comparison(
            BenchmarkReportPackFixture.Entry("run:1", new long[] { 1 }, "GPT", "OpenAI", 80, 75, 85),
            BenchmarkReportPackFixture.Entry("run:2", new long[] { 2 }, "Grok", "xAI", 78, 72, 84),
            BenchmarkReportPackFixture.Entry("run:3", new long[] { 3 }, "Mistral", "Mistral", 70, 64, 76));
        foreach (var measure in comparison.ExcludedMeasures)
        {
            measure.Summary = "The view's wording changed.";
            measure.Instead = "Look elsewhere.";
        }

        var sheet = BenchmarkReportFacts.Build(BenchmarkReportPackFixture.Input(comparison, "run:1",
            BenchmarkReportPackFixture.Run(1, "OpenAI", "gpt", new BenchmarkReportPackFixture.AnswerSpec(101, 1, 1, 80)),
            BenchmarkReportPackFixture.Run(2, "xAI", "grok", new BenchmarkReportPackFixture.AnswerSpec(101, 1, 1, 78)),
            BenchmarkReportPackFixture.Run(3, "Mistral", "mistral", new BenchmarkReportPackFixture.AnswerSpec(101, 1, 1, 70)))).Sheet!;

        Assert.Equal("Testing every pair among these 3 models at once would flag chance differences as significant, so this view tests none.",
            sheet.NoSignificanceSummary);
        Assert.Equal(BenchmarkReportFacts.NoSignificanceInsteadText, sheet.NoSignificanceInstead);
    }
}
