namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MobileGnollHackLogger.Data;
using Overseer.Controllers;
using Overseer.Models;
using Overseer.Services.Benchmarking;
using Overseer.Tests.Helpers;
using Xunit;

/// <summary>
/// Creating a run group: the generated name, the name length limit, the duplicate refusal, the
/// analysis made at once, and the comparison guard and its records on the analysis.
/// </summary>
public class BenchmarkRunGroupCreationTests
{
    internal static readonly DateTime T0 = new(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);

    internal const string ToolGuidesA = "f59d8b30a1c7e4d2b6f0a8c3e9d5b1f7";
    internal const string ToolGuidesB = "0a1b2c3d4e5f60718293a4b5c6d7e8f9";

    internal static DbContextOptions<ApplicationDbContext> NewDatabase()
        => new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

    /// <summary>A three-question suite, its questions assessed at 50.</summary>
    internal static async Task<BenchmarkSuite> SeedSuiteAsync(DbContextOptions<ApplicationDbContext> options, string name = "Creation Suite")
    {
        await using var db = new ApplicationDbContext(options);

        var suite = new BenchmarkSuite { Name = name, Description = "Desc" };
        for (int i = 1; i <= 3; i++)
        {
            suite.Questions.Add(new BenchmarkQuestion
            {
                QuestionText = $"{name} Q{i}",
                ExpectedPoints = $"- point {i}",
                OrderIndex = i,
                ItemRevision = 1,
                AssessedDifficulty = 50,
                Difficulty = BenchmarkDifficulty.Intermediate
            });
        }

        db.BenchmarkSuites.Add(suite);
        await db.SaveChangesAsync();
        return suite;
    }

    /// <summary>
    /// <paramref name="count"/> runs of one condition that answered every question of the suite,
    /// started an hour apart from <paramref name="firstStartedAtUtc"/>; runs of one call resolve Tier A.
    /// </summary>
    internal static async Task<long[]> SeedRunsAsync(
        DbContextOptions<ApplicationDbContext> options,
        long suiteId,
        int count,
        DateTime firstStartedAtUtc,
        string toolGuides = ToolGuidesA,
        BenchmarkRunStatus status = BenchmarkRunStatus.Completed,
        long? seriesId = null)
    {
        await using var db = new ApplicationDbContext(options);

        var suite = await db.BenchmarkSuites.Include(s => s.Questions).SingleAsync(s => s.Id == suiteId);
        var questions = suite.Questions.OrderBy(q => q.OrderIndex).ToList();

        var runs = new List<BenchmarkRun>();
        for (int r = 0; r < count; r++)
        {
            var run = new BenchmarkRun
            {
                BenchmarkSuiteId = suite.Id,
                BenchmarkSuiteIdUsed = suite.Id,
                SuiteName = suite.Name,
                Status = status,
                StartedAtUtc = firstStartedAtUtc.AddHours(r),
                CompletedAtUtc = firstStartedAtUtc.AddHours(r).AddMinutes(30),
                QualityIndex = 70 + 10 * (r % 3),
                SpeedIndex = 100,
                RunSeriesId = seriesId,
                RunSeriesIndex = seriesId.HasValue ? r + 1 : null,
                TestedModelSnapshot = BenchmarkModelSnapshots.Model(
                    provider: "OpenAI", modelId: "gpt-luna", displayName: "GPT Luna", thinkingLevel: "high"),
                AssessorModelSnapshot = BenchmarkModelSnapshots.Model(provider: "Google", modelId: "gemini-pro"),
                CandidatePromptOptionsJson = "{\"verboseMode\":false,\"spoilerFreeMode\":false,\"overseerMode\":0}",
                CandidateSystemPromptSha256 = "e9b3e9a7c4d1b8f0a2e6c9d3b7f1a4e8",
                ToolGuidesSha256 = toolGuides,
                KnowledgeBaseHeadSha = "576ca574b2e8d0f6a4c2e8d4b0f6a2c8",
                HarnessVersion = "12",
                ScoringMethodVersion = 9,
                MaxParallelQuestionsUsed = 1,
                TotalQuestionCount = questions.Count
            };

            for (int i = 0; i < questions.Count; i++)
            {
                int score = 60 + 10 * (r % 3) + 10 * i;
                run.Answers.Add(new BenchmarkRunAnswer
                {
                    BenchmarkQuestionId = questions[i].Id,
                    BenchmarkQuestionIdUsed = questions[i].Id,
                    ItemRevisionUsed = 1,
                    ExpectedPointsUsed = questions[i].ExpectedPoints,
                    ExpectedPointsRecorded = true,
                    OrderIndex = questions[i].OrderIndex,
                    QuestionText = questions[i].QuestionText,
                    AnswerText = "An answer.",
                    Difficulty = questions[i].Difficulty,
                    AssessedDifficulty = 50,
                    Status = BenchmarkAnswerStatus.Ok,
                    AssessmentStatus = BenchmarkAssessmentStatus.Scored,
                    QualityScore = score,
                    AccuracyScore = score,
                    SpeedScore = 100,
                    DurationMs = 30000,
                    ToolTimeMs = 0,
                    TimeToFirstTokenMs = 900
                });
            }

            runs.Add(run);
        }

        db.BenchmarkRuns.AddRange(runs);
        await db.SaveChangesAsync();
        return runs.Select(r => r.Id).ToArray();
    }

    private static AdminBenchmarkController CreateController(ApplicationDbContext db)
        => new(
            db, null!, null!, new BenchmarkRunManager(), null!, null!, null!, null!, null!, null!,
            null!, null!, null!, null!, null!, null!, null!, null!,
            new BenchmarkGroupAnalysisService(db, NullLogger<BenchmarkGroupAnalysisService>.Instance))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        new[] { new Claim(ClaimTypes.NameIdentifier, "admin-user") }, "TestAuth"))
                }
            }
        };

    private static async Task<BenchmarkRunGroupTierPreviewDto> CreateAcceptedAsync(
        DbContextOptions<ApplicationDbContext> options, IEnumerable<long> runIds, string? name = null)
    {
        await using var db = new ApplicationDbContext(options);
        var result = await CreateController(db).CreateRunGroup(new CreateBenchmarkRunGroupRequest
        {
            Name = name,
            RunIds = runIds.ToList()
        });

        var dto = Assert.IsType<BenchmarkRunGroupTierPreviewDto>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.True(dto.Accepted, dto.Error);
        Assert.NotNull(dto.Group);
        return dto;
    }

    private static BenchmarkGroupAnalysisService Service(ApplicationDbContext db)
        => new(db, NullLogger<BenchmarkGroupAnalysisService>.Instance);

    // --- Names ------------------------------------------------------------------------------------

    [Fact]
    public async Task ABlankName_IsGeneratedFromTheNewestMembersSuiteAndModel_AndTheEarliestStart()
    {
        var options = NewDatabase();
        var suite = await SeedSuiteAsync(options);
        var runIds = await SeedRunsAsync(options, suite.Id, 3, T0);

        var dto = await CreateAcceptedAsync(options, runIds, name: "   ");

        Assert.Equal("Creation Suite · GPT Luna · 2026-10-01 · R=3", dto.Group!.Name);
        Assert.Equal(BenchmarkRunGroupNames.Default(suite.Name, "GPT Luna", T0, 3), dto.Group.Name);
        Assert.True(dto.Group.NameIsGenerated);
        Assert.Equal(runIds.OrderBy(id => id), dto.Group.MemberRunIds);
    }

    [Fact]
    public async Task AChosenName_IsKeptTrimmed_AndIsNotMarkedGenerated()
    {
        var options = NewDatabase();
        var suite = await SeedSuiteAsync(options);
        var runIds = await SeedRunsAsync(options, suite.Id, 2, T0);

        var dto = await CreateAcceptedAsync(options, runIds, name: "  Before the guide change  ");

        Assert.Equal("Before the guide change", dto.Group!.Name);
        Assert.False(dto.Group.NameIsGenerated);
    }

    [Fact]
    public async Task ANameLongerThan256Characters_IsRefused_AndNothingIsCreated()
    {
        var options = NewDatabase();
        var suite = await SeedSuiteAsync(options);
        var runIds = await SeedRunsAsync(options, suite.Id, 2, T0);

        await using var db = new ApplicationDbContext(options);
        var result = await CreateController(db).CreateRunGroup(new CreateBenchmarkRunGroupRequest
        {
            Name = new string('x', 257),
            RunIds = runIds.ToList()
        });

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("A group name can be at most 256 characters.", bad.Value);
        Assert.Equal(0, await db.BenchmarkRunGroups.CountAsync(TestContext.Current.CancellationToken));
    }

    // --- Duplicates -------------------------------------------------------------------------------

    [Fact]
    public async Task ASetAGroupAlreadyHoldsExactly_IsRefused_NamingThatGroup_OnCreateAndPreview()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = NewDatabase();
        var suite = await SeedSuiteAsync(options);
        var runIds = await SeedRunsAsync(options, suite.Id, 3, T0);

        var first = await CreateAcceptedAsync(options, runIds);
        long firstId = first.Group!.Id;

        await using var db = new ApplicationDbContext(options);
        var controller = CreateController(db);

        var request = new CreateBenchmarkRunGroupRequest { RunIds = runIds.OrderByDescending(id => id).ToList() };
        var refused = Assert.IsType<BenchmarkRunGroupTierPreviewDto>(
            Assert.IsType<BadRequestObjectResult>(await controller.CreateRunGroup(request)).Value);

        Assert.False(refused.Accepted);
        Assert.Equal(firstId, refused.DuplicateOfGroupId);
        Assert.Equal($"Group #{firstId} already holds exactly these runs.", refused.Error);
        Assert.Equal(1, await db.BenchmarkRunGroups.CountAsync(ct));

        var preview = Assert.IsType<BenchmarkRunGroupTierPreviewDto>(
            Assert.IsType<OkObjectResult>(await controller.PreviewRunGroupTier(request)).Value);
        Assert.False(preview.Accepted);
        Assert.Equal(firstId, preview.DuplicateOfGroupId);

        // A subset is a different set, and is accepted.
        var subset = await CreateAcceptedAsync(options, runIds.Take(2));
        Assert.Null(subset.DuplicateOfGroupId);
    }

    // --- The analysis made at creation --------------------------------------------------------------

    [Fact]
    public async Task ATierACreate_StoresAnAnalysis_WithItsHeadlines_UnderStatisticsMethod2()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = NewDatabase();
        var suite = await SeedSuiteAsync(options);
        var runIds = await SeedRunsAsync(options, suite.Id, 3, T0);

        var dto = await CreateAcceptedAsync(options, runIds);

        Assert.Equal(BenchmarkComparabilityTier.Replicate.ToString(), dto.Comparability!.Tier);
        Assert.NotNull(dto.Group!.LatestAnalysisId);

        await using var db = new ApplicationDbContext(options);
        var analysis = await db.BenchmarkGroupAnalyses.SingleAsync(ct);
        Assert.Equal(dto.Group.LatestAnalysisId, analysis.Id);
        Assert.Equal("admin-user", analysis.ComputedByUserId);
        Assert.Null(analysis.ComparisonJson);

        var result = BenchmarkGroupAnalysisService.DeserialiseResult(analysis);
        Assert.NotNull(result);
        Assert.Equal(2, result!.StatisticsMethodVersion);

        Assert.Equal(result.Index.PointEstimate, analysis.HeadlineIndex);
        Assert.Equal(80.0, analysis.HeadlineIndex!.Value, 6);
        Assert.Equal(result.Index.CombinedHalfWidth, analysis.HeadlineHalfWidth);
        Assert.Equal(result.Speed.MeanSpeedIndex, analysis.HeadlineSpeedIndex);
        Assert.Null(analysis.HeadlineCostPerRun);

        Assert.Equal(analysis.HeadlineIndex, dto.Group.HeadlineIndex);
        Assert.False(dto.Group.AnalysisStale);
    }

    // --- The comparison guard ---------------------------------------------------------------------

    [Fact]
    public async Task ComparingWithAGroupOfAnotherSuite_OrWithItself_IsRefused()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = NewDatabase();
        var suiteOne = await SeedSuiteAsync(options, "Suite One");
        var suiteTwo = await SeedSuiteAsync(options, "Suite Two");

        long groupOne = (await CreateAcceptedAsync(options, await SeedRunsAsync(options, suiteOne.Id, 3, T0))).Group!.Id;
        long groupTwo = (await CreateAcceptedAsync(options, await SeedRunsAsync(options, suiteTwo.Id, 3, T0.AddDays(1)))).Group!.Id;

        await using var db = new ApplicationDbContext(options);
        var service = Service(db);

        var (analysis, _, error) = await service.AnalyseAsync(groupTwo, null, groupOne, ct);
        Assert.Null(analysis);
        Assert.Equal($"Group #{groupOne} is in another suite; a comparison pairs items of one suite.", error);

        var (self, _, selfError) = await service.AnalyseAsync(groupTwo, null, groupTwo, ct);
        Assert.Null(self);
        Assert.Equal("A group cannot be compared with itself.", selfError);

        // Only the two analyses made at creation exist.
        Assert.Equal(2, await db.BenchmarkGroupAnalyses.CountAsync(ct));
    }

    [Fact]
    public async Task AComparisonWithAGroupDifferingOnlyOnTheToolGuides_RecordsThatOneKey()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = NewDatabase();
        var suite = await SeedSuiteAsync(options);

        long baseline = (await CreateAcceptedAsync(options, await SeedRunsAsync(options, suite.Id, 3, T0, ToolGuidesA))).Group!.Id;
        long treatment = (await CreateAcceptedAsync(options, await SeedRunsAsync(options, suite.Id, 3, T0.AddDays(1), ToolGuidesB))).Group!.Id;

        await using var db = new ApplicationDbContext(options);
        var (analysis, _, error) = await Service(db).AnalyseAsync(treatment, null, baseline, ct);
        Assert.True(analysis != null, error);

        var comparison = JsonSerializer.Deserialize<BenchmarkGroupComparison>(analysis!.ComparisonJson!);
        Assert.NotNull(comparison);
        Assert.Equal(2, comparison!.StatisticsMethodVersion);

        var difference = Assert.Single(comparison.PairKeyDifferences!);
        Assert.Equal(BenchmarkComparabilityKey.ToolGuidesKey, difference.Key);
        Assert.Equal(BenchmarkComparabilityKeyKind.Instrument.ToString(), difference.Kind);
        Assert.Equal(ToolGuidesA, difference.BaselineValue);
        Assert.Equal(ToolGuidesB, difference.TreatmentValue);
        Assert.Equal(0, comparison.PriorComparisonCount);
    }

    [Fact]
    public async Task PriorComparisonCount_CountsTheGroupsEarlierComparisons()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = NewDatabase();
        var suite = await SeedSuiteAsync(options);

        long baseline = (await CreateAcceptedAsync(options, await SeedRunsAsync(options, suite.Id, 3, T0, ToolGuidesA))).Group!.Id;
        long treatment = (await CreateAcceptedAsync(options, await SeedRunsAsync(options, suite.Id, 3, T0.AddDays(1), ToolGuidesB))).Group!.Id;

        var counts = new List<int?>();
        for (int i = 0; i < 3; i++)
        {
            await using var db = new ApplicationDbContext(options);
            var (analysis, _, error) = await Service(db).AnalyseAsync(treatment, null, baseline, ct);
            Assert.True(analysis != null, error);
            counts.Add(JsonSerializer.Deserialize<BenchmarkGroupComparison>(analysis!.ComparisonJson!)!.PriorComparisonCount);
        }

        // The analysis made at creation carries no comparison, so it is not counted.
        Assert.Equal(new int?[] { 0, 1, 2 }, counts);
    }
}
