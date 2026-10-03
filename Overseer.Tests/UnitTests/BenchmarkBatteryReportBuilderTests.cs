namespace Overseer.Tests.UnitTests;

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Services.Benchmarking;
using Xunit;

/// <summary>
/// The battery Markdown report: every numbered section is present for a complete battery, an
/// incomplete battery prints no headline, and the download file name is sanitized.
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
