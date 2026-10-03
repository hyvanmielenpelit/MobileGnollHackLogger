namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Services.Benchmarking;
using Overseer.Tests.Helpers;
using Xunit;

/// <summary>
/// The battery Markdown report: every numbered section is present for a complete battery, an
/// incomplete battery prints no headline, the manifest lists the graders and the earlier runs of the
/// battery, and the download file name is sanitized.
/// </summary>
public class BenchmarkBatteryReportBuilderTests
{
    private readonly ApplicationDbContext _db = BenchmarkBatteryTestData.NewDb();

    private Task<string> BuildAsync(params (BenchmarkRun Run, int SuiteIndex, int Round)[] members)
        => BuildAsync(null, members);

    private async Task<string> BuildAsync(
        BenchmarkBatteryAnswerOutcomes? answerOutcomes, params (BenchmarkRun Run, int SuiteIndex, int Round)[] members)
    {
        var definition = BenchmarkBatteryTestData.Definition();
        long id = await BenchmarkBatteryTestData.SeedAsync(_db, definition, members);

        var service = BenchmarkBatteryTestData.Service(_db);
        var (analysis, result, _, error) = await service.AnalyseAsync(id, null, null, TestContext.Current.CancellationToken);
        Assert.Null(error);

        var loaded = await service.LoadAsync(id, TestContext.Current.CancellationToken);
        var batteryRun = await _db.BenchmarkBatteryRuns.Include(b => b.Members).SingleAsync(b => b.Id == id, TestContext.Current.CancellationToken);

        return BenchmarkBatteryReportBuilder.BuildMarkdownReport(
            batteryRun,
            definition,
            result!,
            analysis,
            loaded!.MemberRuns,
            loaded.Comparability,
            overseerVersion: "1.2.3",
            answerOutcomes: answerOutcomes);
    }

    /// <summary>
    /// Analyses a seeded battery run and renders it with the Graders block and the history table the
    /// analysis service loads for it, as the report endpoint does; <paramref name="graders"/> and
    /// <paramref name="earlierRuns"/> replace the loaded ones when given.
    /// </summary>
    private async Task<string> BuildWithManifestAsync(
        long batteryRunId,
        BenchmarkBatteryGraders? graders = null,
        IReadOnlyList<BenchmarkBatteryEarlierRun>? earlierRuns = null)
    {
        var ct = TestContext.Current.CancellationToken;
        var service = BenchmarkBatteryTestData.Service(_db);
        var (analysis, result, _, error) = await service.AnalyseAsync(batteryRunId, null, null, ct);
        Assert.Null(error);

        var loaded = await service.LoadAsync(batteryRunId, ct);
        var batteryRun = await _db.BenchmarkBatteryRuns.Include(b => b.Members).SingleAsync(b => b.Id == batteryRunId, ct);
        var usable = loaded!.MemberRuns.Where(r => loaded.UsableMemberRunIds.Contains(r.Id)).ToList();

        return BenchmarkBatteryReportBuilder.BuildMarkdownReport(
            batteryRun,
            loaded.Definition,
            result!,
            analysis,
            loaded.MemberRuns,
            loaded.Comparability,
            graders: graders ?? await service.LoadGradersAsync(batteryRun, usable, ct),
            earlierRuns: earlierRuns ?? await service.LoadEarlierRunsAsync(batteryRun, ct));
    }

    [Fact]
    public async Task TheManifest_ListsTheGraders_FromTheNewestUsableMember_AndTheReportWriter()
    {
        var ct = TestContext.Current.CancellationToken;
        _db.SystemAiApiConfigurations.Add(new SystemAiApiConfiguration
        {
            Id = 9,
            DisplayName = "Writer",
            Provider = "Anthropic",
            ModelId = "claude-writer",
            ThinkingLevel = "max"
        });

        var suiteA = BenchmarkBatteryTestData.SuiteARun(1);
        var suiteB = BenchmarkBatteryTestData.SuiteBRun(2);
        foreach (var run in new[] { suiteA, suiteB })
        {
            run.SecondOpinionAssessorModelConfigurationId = 5;
            run.SecondOpinionAssessorModelSnapshot = BenchmarkModelSnapshots.Model("Anthropic", "claude-reader", "Reader", thinkingLevel: "low");
            run.SecondOpinionModeUsed = (int)BenchmarkSecondOpinionMode.All;
            run.ClaimVerifierModelConfigurationId = 6;
            run.ClaimVerifierModelSnapshot = BenchmarkModelSnapshots.Model("Google", "gemini-verifier", "Verifier", thinkingLevel: "medium");
        }
        suiteB.SecondOpinionGradedAnswerCount = 2;

        long id = await BenchmarkBatteryTestData.SeedAsync(_db, BenchmarkBatteryTestData.Definition(), (suiteA, 0, 1), (suiteB, 1, 1));
        var batteryRun = await _db.BenchmarkBatteryRuns.SingleAsync(b => b.Id == id, ct);
        batteryRun.ReportWriterModelConfigurationId = 9;
        await _db.SaveChangesAsync(ct);

        string report = await BuildWithManifestAsync(id);

        // Run 2 started a minute after run 1, so it is the newest usable member.
        Assert.Contains("**Graders** — as recorded on run 2, the newest usable member; the report writer is the battery run's own configuration.", report);
        Assert.Contains("| Model under test | `gpt-5.6-luna` | OpenAI | high | reasoning mode Default · service tier Default |", report);
        Assert.Contains("| Assessor | Gemini 3.7 Pro (`gemini-3.7-pro`) | Google | Default | — |", report);
        Assert.Contains("| Co-assessor (panel member B) | *none — a single assessor graded every answer* | — | — | — |", report);
        Assert.Contains("| Second reader | Reader (`claude-reader`) | Anthropic | low | coverage: every answer (`All`), 2 of 2 answers on run 2 |", report);
        Assert.Contains("| Claim verifier | Verifier (`gemini-verifier`) | Google | medium | — |", report);
        Assert.Contains("| Report writer | Writer (`claude-writer`) | Anthropic | max | writes the AI-written battery documents; grades nothing |", report);

        // Inside the manifest, before its suite table; no earlier run of a battery without an id.
        int graders = report.IndexOf("**Graders**", StringComparison.Ordinal);
        Assert.True(graders > report.IndexOf("## 1. Battery Manifest", StringComparison.Ordinal));
        Assert.True(graders < report.IndexOf("| # | Suite | Suite id |", StringComparison.Ordinal));
        Assert.DoesNotContain("Earlier Runs of This Battery", report);
    }

    [Fact]
    public async Task TheGraders_OfAPanelRun_NameBothMembersAndTheReferenceReader_AndADeletedWriter()
    {
        var older = BenchmarkBatteryTestData.SuiteARun(1);
        var newest = BenchmarkBatteryTestData.SuiteBRun(2);
        newest.CoAssessorModelConfigurationId = 7;
        newest.CoAssessorModelSnapshot = BenchmarkModelSnapshots.Model("Anthropic", "claude-judge", "Judge", thinkingLevel: "high");
        newest.SecondOpinionAssessorModelSnapshot = BenchmarkModelSnapshots.Model("Google", "gemini-reader", "gemini-reader");
        newest.SecondOpinionModeUsed = (int)BenchmarkSecondOpinionMode.FlaggedPlusSample;
        var writerless = new BenchmarkBatteryRun { BatteryName = "Core knowledge", ReportWriterModelConfigurationId = 12 };

        var graders = BenchmarkBatteryAnalysisService.GradersOf(writerless, new[] { older, newest }, reportWriter: null);

        Assert.Equal((long?)2, graders.SourceRunId);
        Assert.True(graders.Panel);
        Assert.Equal("claude-judge", graders.CoAssessor!.ModelId);
        Assert.Equal("high", graders.CoAssessor.ThinkingLevel);
        Assert.Equal(BenchmarkSecondOpinionMode.FlaggedPlusSample, graders.ReaderMode);
        Assert.Null(graders.ReportWriter);
        Assert.Equal((long?)12, graders.ReportWriterConfigurationId);

        long id = await BenchmarkBatteryTestData.SeedAsync(
            _db, BenchmarkBatteryTestData.Definition(),
            (BenchmarkBatteryTestData.SuiteARun(3), 0, 1), (BenchmarkBatteryTestData.SuiteBRun(4), 1, 1));
        string report = await BuildWithManifestAsync(id, graders);

        Assert.Contains("| Assessor (panel member A) | Gemini 3.7 Pro (`gemini-3.7-pro`) | Google | Default | — |", report);
        Assert.Contains("| Co-assessor (panel member B) | Judge (`claude-judge`) | Anthropic | high | — |", report);
        Assert.Contains("| Reference reader | `gemini-reader` | Google | Default | coverage: flagged answers plus a sample (`FlaggedPlusSample`), 0 of 2 answers on run 2 |", report);
        Assert.Contains("| Report writer | *configuration #12, since deleted* | — | — | — |", report);
    }

    [Fact]
    public async Task TheManifest_ListsEarlierFinishedRunsOfTheBattery_NewestFirst_WithTheirClass()
    {
        var ct = TestContext.Current.CancellationToken;
        var definition = BenchmarkBatteryTestData.Definition();
        var service = BenchmarkBatteryTestData.Service(_db);
        var start = new DateTime(2026, 9, 20, 8, 0, 0, DateTimeKind.Utc);

        async Task<long> SeedRunAsync(int day, BenchmarkRunSeriesStatus status, bool analyse, params (BenchmarkRun Run, int SuiteIndex, int Round)[] members)
        {
            long batteryRunId = await BenchmarkBatteryTestData.SeedAsync(_db, definition, members);
            var row = await _db.BenchmarkBatteryRuns.SingleAsync(b => b.Id == batteryRunId, ct);
            row.BenchmarkBatteryId = 1;
            row.Status = status;
            row.StartedAtUtc = start.AddDays(day);
            row.CompletedAtUtc = status == BenchmarkRunSeriesStatus.Running ? null : start.AddDays(day).AddHours(2);
            await _db.SaveChangesAsync(ct);
            if (analyse)
            {
                Assert.Null((await service.AnalyseAsync(batteryRunId, null, null, ct)).Error);
            }
            return batteryRunId;
        }

        static BenchmarkRun Harness18(long runId, long suiteId, int[] scores, int[] difficulties)
            => BenchmarkBatteryTestData.Run(runId, suiteId, scores, difficulties, harnessVersion: "18");

        long sameClass = await SeedRunAsync(0, BenchmarkRunSeriesStatus.Completed, true,
            (BenchmarkBatteryTestData.SuiteARun(11), 0, 1), (BenchmarkBatteryTestData.SuiteBRun(12), 1, 1));
        long otherClass = await SeedRunAsync(1, BenchmarkRunSeriesStatus.Completed, true,
            (Harness18(13, BenchmarkBatteryTestData.SuiteA, new[] { 60, 70, 80 }, new[] { 40, 40, 40 }), 0, 1),
            (Harness18(14, BenchmarkBatteryTestData.SuiteB, new[] { 90, 50 }, new[] { 20, 60 }), 1, 1));
        long incomplete = await SeedRunAsync(2, BenchmarkRunSeriesStatus.CompletedWithErrors, true,
            (BenchmarkBatteryTestData.SuiteARun(15), 0, 1));
        long unanalysed = await SeedRunAsync(3, BenchmarkRunSeriesStatus.Stopped, false,
            (BenchmarkBatteryTestData.SuiteARun(16), 0, 1));
        long stillRunning = await SeedRunAsync(4, BenchmarkRunSeriesStatus.Running, false,
            (BenchmarkBatteryTestData.SuiteARun(17), 0, 1));
        long current = await SeedRunAsync(5, BenchmarkRunSeriesStatus.Completed, false,
            (BenchmarkBatteryTestData.SuiteARun(1), 0, 1), (BenchmarkBatteryTestData.SuiteBRun(2), 1, 1));
        long later = await SeedRunAsync(6, BenchmarkRunSeriesStatus.Completed, true,
            (BenchmarkBatteryTestData.SuiteARun(18), 0, 1), (BenchmarkBatteryTestData.SuiteBRun(19), 1, 1));

        string report = await BuildWithManifestAsync(current);

        Assert.Contains("### 1.2 Earlier Runs of This Battery", report);
        Assert.Contains(BenchmarkBatteryReportBuilder.EarlierRunsComparisonNote, report);

        var lines = report.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        string Row(long batteryRunId) => Assert.Single(lines, l => l.StartsWith($"| #{batteryRunId} |", StringComparison.Ordinal));

        // Suite B's two questions withhold the item-sampling component, so whether a half-width
        // follows the index is read from the stored result.
        async Task<string> IndexCellAsync(long batteryRunId)
        {
            var stored = BenchmarkBatteryAnalysisService.DeserializeResult(await service.GetLatestAsync(batteryRunId, ct))!;
            double? halfWidth = stored.OverallIndex!.CombinedHalfWidth;
            return stored.OverallIndex.PointEstimate.ToString("F2", CultureInfo.InvariantCulture)
                + (halfWidth.HasValue ? " ± " + halfWidth.Value.ToString("F2", CultureInfo.InvariantCulture) : string.Empty);
        }

        string sameIndex = await IndexCellAsync(sameClass);
        Assert.StartsWith("66.00", sameIndex);
        Assert.Equal($"| #{sameClass} | 2026-09-20 10:00 UTC | 17 | {sameIndex} | yes |", Row(sameClass));
        Assert.Equal($"| #{otherClass} | 2026-09-21 10:00 UTC | 18 | {await IndexCellAsync(otherClass)} | no |", Row(otherClass));

        Assert.Equal($"| #{incomplete} | 2026-09-22 10:00 UTC | 17 | *Incomplete* | no |", Row(incomplete));
        Assert.Equal($"| #{unanalysed} | 2026-09-23 10:00 UTC | — | *not analysed* | no |", Row(unanalysed));

        // Newest first; an unfinished run and a later run are not earlier finished runs.
        var order = new[] { unanalysed, incomplete, otherClass, sameClass }.Select(id => lines.IndexOf(Row(id))).ToList();
        Assert.Equal(order.OrderBy(i => i), order);
        Assert.DoesNotContain(lines, l => l.StartsWith($"| #{stillRunning} |", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.StartsWith($"| #{later} |", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.StartsWith($"| #{current} |", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnEarlierRun_PrintsItsIndexWithItsHalfWidth_AndItsClassAgainstThisRuns()
    {
        var ct = TestContext.Current.CancellationToken;
        long id = await BenchmarkBatteryTestData.SeedAsync(
            _db, BenchmarkBatteryTestData.Definition(),
            (BenchmarkBatteryTestData.SuiteARun(1), 0, 1), (BenchmarkBatteryTestData.SuiteBRun(2), 1, 1));
        var (analysis, _, _, _) = await BenchmarkBatteryTestData.Service(_db).AnalyseAsync(id, null, null, ct);
        string ownClass = analysis!.ComparabilityClassSha256!;

        var earlier = new[]
        {
            new BenchmarkBatteryEarlierRun
            {
                BatteryRunId = 41, FinishedAtUtc = new DateTime(2026, 9, 30, 18, 45, 0, DateTimeKind.Utc), Analysed = true,
                HarnessVersion = "46", OverallIndex = 84.04, OverallIndexHalfWidth = 3.957, ComparabilityClassSha256 = ownClass.ToUpperInvariant()
            },
            new BenchmarkBatteryEarlierRun
            {
                BatteryRunId = 40, FinishedAtUtc = null, Analysed = true,
                HarnessVersion = "45", OverallIndex = 80.5, ComparabilityClassSha256 = new string('a', 64)
            }
        };

        string report = await BuildWithManifestAsync(id, earlierRuns: earlier);

        Assert.Contains("| #41 | 2026-09-30 18:45 UTC | 46 | 84.04 ± 3.96 | yes |", report);
        Assert.Contains("| #40 | — | 45 | 80.50 | no |", report);
    }

    /// <summary>Both suites once, with token totals so the usage section is printed, and claim rulings on suite A.</summary>
    private static (BenchmarkRun Run, int SuiteIndex, int Round)[] MembersWithUsage()
    {
        var suiteA = BenchmarkBatteryTestData.SuiteARun(1);
        suiteA.TotalInputTokens = 12_000;
        suiteA.TotalOutputTokens = 3_000;
        suiteA.ClaimsSupportedCount = 4;
        suiteA.ClaimsRefutedCount = 2;
        var suiteB = BenchmarkBatteryTestData.SuiteBRun(2);
        suiteB.TotalInputTokens = 8_000;
        suiteB.TotalOutputTokens = 2_000;
        return new[] { (suiteA, 0, 1), (suiteB, 1, 1) };
    }

    [Fact]
    public async Task TheUsageSection_StatesTheToolCallOutcomes_AndTheRefutedAnswerSentences()
    {
        var outcomes = new BenchmarkBatteryAnswerOutcomes { ToolCallsFailed = 3, ToolCallsRefusedByBudget = 1, RefutedAnswerSentences = 5 };

        string report = await BuildAsync(outcomes, MembersWithUsage());

        Assert.Contains("- **Tool call outcomes:** 3 failed, 1 refused by the tool budget", report);
        Assert.Contains("- **Claim verification:** 6 claims checked — 4 supported, **2 refuted**, 0 indeterminate; refuted answer sentences (accused ones included): 5", report);
    }

    [Fact]
    public async Task TheUsageSection_SaysWhenTheToolCallOutcomesWereNotRecorded()
    {
        var outcomes = new BenchmarkBatteryAnswerOutcomes { ToolCallsUnavailableReason = BenchmarkBatteryAnswerOutcomes.ToolRecordsReason };

        string report = await BuildAsync(outcomes, MembersWithUsage());

        Assert.Contains("- **Tool call outcomes:** — *not recorded: " + BenchmarkBatteryAnswerOutcomes.ToolRecordsReason + "*", report);
        Assert.Contains("; refuted answer sentences (accused ones included): — *not countable: some verifications record no roles*", report);
    }

    [Fact]
    public async Task TheUsageSection_WithoutLoadedOutcomes_LeavesThemOut()
    {
        string report = await BuildAsync(MembersWithUsage());

        Assert.Contains("## 9. Token and Tool Usage", report);
        Assert.DoesNotContain("Tool call outcomes", report);
        Assert.Contains("- **Claim verification:** 6 claims checked — 4 supported, **2 refuted**, 0 indeterminate", report);
        Assert.DoesNotContain("refuted answer sentences", report);
    }

    [Fact]
    public async Task CompleteBattery_HasEveryNumberedSection_AndTheHeadline()
    {
        // Token totals make the usage section reportable; a run recording none has no usage block.
        var suiteA = BenchmarkBatteryTestData.SuiteARun(1);
        suiteA.TotalInputTokens = 12_000;
        suiteA.TotalOutputTokens = 3_000;
        var suiteB = BenchmarkBatteryTestData.SuiteBRun(2);
        suiteB.TotalInputTokens = 8_000;
        suiteB.TotalOutputTokens = 2_000;

        string report = await BuildAsync((suiteA, 0, 1), (suiteB, 1, 1));

        string[] sections =
        {
            "Battery Manifest",
            "Overall Intelligence Index",
            "Suite Profile",
            "Weighting Sensitivity",
            "Leave-One-Suite-Out",
            "Quality Dimensions",
            "Speed",
            "Cost",
            "Token and Tool Usage",
            "Method and Limits"
        };

        int previous = -1;
        for (int i = 0; i < sections.Length; i++)
        {
            string heading = $"## {i + 1}. {sections[i]}";
            int position = report.IndexOf(heading, StringComparison.Ordinal);
            Assert.True(position > previous, $"Missing or out of order: '{heading}'.");
            previous = position;
        }

        Assert.DoesNotContain("Paired Battery Comparison", report);
        Assert.Contains("**Overall Intelligence Index:** 66.00 / 100", report);
        Assert.Contains("Questions and difficulty", report);
        Assert.Contains("Chat Prompt Under Test", report);
        Assert.Contains("**Battery wall clock:** 2 h 05 min", report);
        Assert.Contains("**Overseer version:** 1.2.3", report);
        Assert.Contains(BenchmarkBatteryStatistics.NoReproducibilityCaveat, report);
    }

    [Fact]
    public async Task IncompleteBattery_PrintsNoHeadline_AndNamesTheExcludedMember()
    {
        string report = await BuildAsync(
            (BenchmarkBatteryTestData.SuiteARun(1), 0, 1),
            (BenchmarkBatteryTestData.Run(2, BenchmarkBatteryTestData.SuiteB, new[] { 90, 50 }, new[] { 20, 60 },
                status: BenchmarkRunStatus.CompletedWithErrors, qualityIndex: null, terminalFailures: 1), 1, 1));

        Assert.DoesNotContain("**Overall Intelligence Index:**", report);
        Assert.Contains("Incomplete (1 of 2 suites): no Overall Index is reported.", report);
        Assert.Contains("## 2. Overall Intelligence Index", report);
        Assert.Contains("## 3. Suite Profile", report);
        Assert.Contains("*no usable member*", report);
        Assert.Contains(BenchmarkBatteryPlanner.IndexWithheldProviderFailureReason, report);
        Assert.Contains("*Not reported: the battery is incomplete.*", report);
    }

    [Fact]
    public void BuildFileName_SanitizesEachPart_AndCarriesRunsPerSuite()
    {
        string name = BenchmarkBatteryReportBuilder.BuildFileName(
            "Core knowledge", "GPT/5.6 Luna", 3, new DateTime(2026, 10, 1, 12, 30, 45, DateTimeKind.Utc));

        Assert.Equal("Core_knowledge_GPT5.6_Luna_battery_R3_20261001_123045.md", name);
    }

    [Fact]
    public void BuildFileName_FallsBackWhenAPartIsEmpty()
    {
        string name = BenchmarkBatteryReportBuilder.BuildFileName(
            "  ", null, 1, new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal("battery_model_battery_R1_20261001_000000.md", name);
    }

    [Theory]
    [InlineData(65, "1 min 05 s")]
    [InlineData(7500, "2 h 05 min")]
    [InlineData(93600, "1 d 2 h 00 min")]
    public void Duration_FormatsTheWallClock(int seconds, string expected)
    {
        Assert.Equal(expected, BenchmarkBatteryReportBuilder.Duration(TimeSpan.FromSeconds(seconds)));
    }
}
