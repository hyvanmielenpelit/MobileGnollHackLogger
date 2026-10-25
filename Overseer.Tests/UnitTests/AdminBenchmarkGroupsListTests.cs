namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Controllers;
using Overseer.Models;
using Overseer.Services.Benchmarking;
using Overseer.Tests.Helpers;
using Xunit;

/// <summary>
/// <c>GET runs/groups</c> reads every group's latest analysis and newest member run in two set
/// queries. The list must be exactly what the per-group reads produced, which a copy of that
/// per-group algorithm here serves as the reference for.
/// </summary>
public class AdminBenchmarkGroupsListTests
{
    private static DbContextOptions<ApplicationDbContext> NewDatabase()
        => new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

    private static AdminBenchmarkController CreateController(ApplicationDbContext db, BenchmarkGroupAnalysisService? groupAnalysisService = null)
        => new(
            db, null!, null!, new BenchmarkRunManager(), null!, null!, null!, null!, null!, null!,
            null!, null!, null!, null!, null!, null!, null!, null!, groupAnalysisService!);

    private static readonly DateTime T0 = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    private static BenchmarkRun Run(long id, long suiteId, string suiteName, string modelId, string? displayName, DateTime startedAtUtc)
        => new()
        {
            Id = id,
            BenchmarkSuiteId = suiteId,
            SuiteName = suiteName,
            TestedModelSnapshot = BenchmarkModelSnapshots.Model(
                provider: "OpenAI", modelId: modelId, displayName: displayName,
                thinkingLevel: "high", reasoningMode: "enabled"),
            AssessorModelSnapshot = BenchmarkModelSnapshots.Assessor(),
            Status = BenchmarkRunStatus.Completed,
            StartedAtUtc = startedAtUtc
        };

    private static BenchmarkGroupAnalysis Analysis(long id, long groupId, DateTime computedAtUtc, params long[] runIds)
        => new()
        {
            Id = id,
            BenchmarkRunGroupId = groupId,
            ComputedAtUtc = computedAtUtc,
            MemberRunIdsJson = JsonSerializer.Serialize(runIds),
            RunCount = runIds.Length,
            ResultJson = "{}",
            HeadlineIndex = 70 + id,
            HeadlineHalfWidth = 2.5,
            HeadlineSpeedIndex = 60 + id,
            HeadlineCostPerRun = id == 3 ? null : 0.25 * id
        };

    /// <summary>
    /// Four groups: a replicate set whose latest analysis is stale and whose two newest runs start at
    /// the same instant, a single-run group with a current analysis, an empty group, and a group
    /// without a suite or an analysis.
    /// </summary>
    private static async Task SeedAsync(DbContextOptions<ApplicationDbContext> options)
    {
        using var db = new ApplicationDbContext(options);

        db.BenchmarkSuites.AddRange(
            new BenchmarkSuite { Id = 1, Name = "Suite One", Description = "Desc" },
            new BenchmarkSuite { Id = 2, Name = "Suite Two", Description = "Desc" });

        db.BenchmarkRuns.AddRange(
            Run(11, 1, "Suite One (as run)", "gpt-a", "GPT A", T0),
            Run(12, 1, "Suite One (as run)", "gpt-b", null, T0.AddHours(1)),
            Run(13, 1, "Suite One (renamed)", "gpt-c", "GPT C", T0.AddHours(1)),
            Run(14, 2, "Suite Two (as run)", "gpt-d", "GPT D", T0.AddHours(2)));

        db.BenchmarkRunGroups.AddRange(
            new BenchmarkRunGroup
            {
                Id = 1,
                Name = "Replicates",
                BenchmarkSuiteId = 1,
                Tier = BenchmarkRunGroupTier.Replicate,
                ComparabilityKeyHash = "abc",
                ComparabilityKeyVersion = BenchmarkComparabilityKey.DefinitionVersion,
                CreatedAtUtc = T0.AddDays(1),
                ModifiedAtUtc = T0.AddDays(1),
                Members = new List<BenchmarkRunGroupMember>
                {
                    new() { Id = 101, BenchmarkRunId = 11, AddedAtUtc = T0 },
                    new() { Id = 102, BenchmarkRunId = 12, AddedAtUtc = T0.AddMinutes(1) },
                    new() { Id = 103, BenchmarkRunId = 13, AddedAtUtc = T0.AddMinutes(2) }
                }
            },
            new BenchmarkRunGroup
            {
                Id = 2,
                Name = "Single",
                BenchmarkSuiteId = 2,
                Tier = BenchmarkRunGroupTier.CrossCondition,
                CrossCondition = true,
                Notes = "A note",
                CreatedFromSeriesId = 5,
                CreatedAtUtc = T0.AddDays(2),
                ModifiedAtUtc = T0.AddDays(3),
                Members = new List<BenchmarkRunGroupMember>
                {
                    new() { Id = 201, BenchmarkRunId = 14, AddedAtUtc = T0 }
                }
            },
            new BenchmarkRunGroup
            {
                Id = 3,
                Name = "Empty",
                BenchmarkSuiteId = 1,
                CreatedAtUtc = T0.AddDays(3),
                ModifiedAtUtc = T0.AddDays(3)
            },
            new BenchmarkRunGroup
            {
                Id = 4,
                Name = "No suite",
                BenchmarkSuiteId = null,
                Tier = BenchmarkRunGroupTier.QualityComparable,
                CreatedAtUtc = T0.AddDays(4),
                ModifiedAtUtc = T0.AddDays(4),
                Members = new List<BenchmarkRunGroupMember>
                {
                    new() { Id = 401, BenchmarkRunId = 11, AddedAtUtc = T0 }
                }
            });

        db.BenchmarkGroupAnalyses.AddRange(
            Analysis(1, 1, T0.AddDays(1).AddHours(1), 11, 12, 13),
            Analysis(2, 1, T0.AddDays(1).AddHours(2), 11, 12),
            Analysis(3, 2, T0.AddDays(2).AddHours(1), 14));

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    // --- The per-group algorithm the list replaced, kept as the reference ------------------------------

    private static async Task<List<BenchmarkRunGroupDto>> ReferenceListAsync(DbContextOptions<ApplicationDbContext> options)
    {
        using var db = new ApplicationDbContext(options);

        var groups = await db.BenchmarkRunGroups
            .Include(g => g.Members)
            .Include(g => g.BenchmarkSuite)
            .OrderByDescending(g => g.CreatedAtUtc)
            .ToListAsync();

        var dtos = new List<BenchmarkRunGroupDto>(groups.Count);
        foreach (var group in groups)
        {
            var latest = await db.BenchmarkGroupAnalyses
                .Where(a => a.BenchmarkRunGroupId == group.Id)
                .OrderByDescending(a => a.ComputedAtUtc)
                .FirstOrDefaultAsync();

            var memberRunIds = group.Members.Select(m => m.BenchmarkRunId).ToList();
            var newest = memberRunIds.Count == 0
                ? null
                : await db.BenchmarkRuns
                    .Where(r => memberRunIds.Contains(r.Id))
                    .OrderByDescending(r => r.StartedAtUtc)
                    .ThenByDescending(r => r.Id)
                    .Select(r => new
                    {
                        r.SuiteName,
                        r.TestedModelSnapshot.DisplayName,
                        r.TestedModelSnapshot.ModelId,
                        r.TestedModelSnapshot.Provider,
                        r.TestedModelSnapshot.ThinkingLevel,
                        r.TestedModelSnapshot.ReasoningMode
                    })
                    .FirstOrDefaultAsync();

            DateTime? latestRerun = memberRunIds.Count == 0
                ? null
                : await db.BenchmarkRuns
                    .Where(r => memberRunIds.Contains(r.Id))
                    .MaxAsync(r => r.RerunCompletedAtUtc);

            var dto = new BenchmarkRunGroupDto
            {
                Id = group.Id,
                Name = group.Name,
                NameIsGenerated = BenchmarkRunGroupNames.IsDefault(
                    group.Name,
                    newest?.SuiteName ?? group.BenchmarkSuite?.Name,
                    newest?.DisplayName ?? newest?.ModelId,
                    group.Members.Count),
                BenchmarkSuiteId = group.BenchmarkSuiteId,
                SuiteName = newest?.SuiteName ?? group.BenchmarkSuite?.Name,
                TestedModelDisplayName = newest?.DisplayName ?? newest?.ModelId,
                TestedModelProvider = newest?.Provider,
                TestedModelId = newest?.ModelId,
                TestedModelThinkingLevel = newest?.ThinkingLevel,
                TestedModelReasoningMode = newest?.ReasoningMode,
                Tier = group.Tier.ToString(),
                TierLabel = DescribeTier(group.Tier),
                ComparabilityKeyHash = group.ComparabilityKeyHash,
                ComparabilityKeyStale = group.ComparabilityKeyVersion != BenchmarkComparabilityKey.DefinitionVersion,
                CrossCondition = group.CrossCondition,
                Notes = group.Notes,
                CreatedFromSeriesId = group.CreatedFromSeriesId,
                CreatedAtUtc = group.CreatedAtUtc,
                ModifiedAtUtc = group.ModifiedAtUtc,
                RunCount = group.Members.Count,
                MemberRunIds = memberRunIds.OrderBy(id => id).ToList(),
                LatestAnalysisId = latest?.Id,
                LatestAnalysisAtUtc = latest?.ComputedAtUtc,
                HeadlineIndex = latest?.HeadlineIndex,
                HeadlineHalfWidth = latest?.HeadlineHalfWidth,
                HeadlineSpeedIndex = latest?.HeadlineSpeedIndex,
                HeadlineCostPerRun = latest?.HeadlineCostPerRun,
                AnalysisStale = BenchmarkGroupAnalysisService.IsStale(group, latest, latestRerun)
            };
            BenchmarkProviderAvailabilityService.ApplyCard(
                dto, await new BenchmarkProviderAvailabilityService(db).CardForPoolAsync(memberRunIds));
            dtos.Add(dto);
        }

        return dtos;
    }

    private static string DescribeTier(BenchmarkRunGroupTier tier) => tier switch
    {
        BenchmarkRunGroupTier.Replicate => "Tier A — Replicate",
        BenchmarkRunGroupTier.QualityComparable => "Tier B — Quality-comparable",
        BenchmarkRunGroupTier.CrossCondition => "Tier C — Cross-condition",
        _ => "Not comparable"
    };

    // --- Tests ------------------------------------------------------------------------------------------

    [Fact]
    public async Task TheGroupsList_SerializesExactlyAsThePerGroupReadsDid()
    {
        var options = NewDatabase();
        await SeedAsync(options);

        var expected = await ReferenceListAsync(options);

        using var db = new ApplicationDbContext(options);
        var actual = Assert.IsAssignableFrom<IEnumerable<BenchmarkRunGroupDto>>(
            Assert.IsType<OkObjectResult>(await CreateController(db).GetRunGroups()).Value).ToList();

        Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual));

        // The fixture reaches the cases the batching has to get right.
        Assert.Equal(new long[] { 4, 3, 2, 1 }, actual.Select(g => g.Id));
        var replicates = actual.Single(g => g.Id == 1);
        Assert.Equal("Suite One (renamed)", replicates.SuiteName);
        Assert.Equal("GPT C", replicates.TestedModelDisplayName);
        Assert.Equal((long?)2, replicates.LatestAnalysisId);
        Assert.True(replicates.AnalysisStale);
        Assert.False(actual.Single(g => g.Id == 2).AnalysisStale);
        Assert.Equal("Suite One", actual.Single(g => g.Id == 3).SuiteName);
        Assert.Null(actual.Single(g => g.Id == 4).LatestAnalysisId);
    }

    [Fact]
    public async Task TheGroupsList_OfNoGroups_IsEmpty()
    {
        using var db = new ApplicationDbContext(NewDatabase());

        var actual = Assert.IsAssignableFrom<IEnumerable<BenchmarkRunGroupDto>>(
            Assert.IsType<OkObjectResult>(await CreateController(db).GetRunGroups()).Value);

        Assert.Empty(actual);
    }

    private static async Task<List<BenchmarkRunGroupDto>> ListAsync(DbContextOptions<ApplicationDbContext> options)
    {
        using var db = new ApplicationDbContext(options);
        return Assert.IsAssignableFrom<IEnumerable<BenchmarkRunGroupDto>>(
            Assert.IsType<OkObjectResult>(await CreateController(db).GetRunGroups()).Value).ToList();
    }

    [Fact]
    public async Task TheGroupsList_CarriesTheMemberRunIdsAscending_AndTheLatestAnalysisHeadlines()
    {
        var options = NewDatabase();
        await SeedAsync(options);

        var actual = await ListAsync(options);

        var replicates = actual.Single(g => g.Id == 1);
        Assert.Equal(new long[] { 11, 12, 13 }, replicates.MemberRunIds);
        Assert.Equal(72.0, replicates.HeadlineIndex);
        Assert.Equal(2.5, replicates.HeadlineHalfWidth);
        Assert.Equal(62.0, replicates.HeadlineSpeedIndex);
        Assert.Equal(0.5, replicates.HeadlineCostPerRun);

        var single = actual.Single(g => g.Id == 2);
        Assert.Equal(new long[] { 14 }, single.MemberRunIds);
        Assert.Equal(73.0, single.HeadlineIndex);
        Assert.Null(single.HeadlineCostPerRun);

        var empty = actual.Single(g => g.Id == 3);
        Assert.Empty(empty.MemberRunIds);
        Assert.Null(empty.HeadlineIndex);
    }

    [Fact]
    public async Task TheGroupsList_MarksAGeneratedName_AndNotAChosenOne()
    {
        var options = NewDatabase();
        await SeedAsync(options);

        Assert.All(await ListAsync(options), g => Assert.False(g.NameIsGenerated));

        // The newest member of group 1 is run 13: "Suite One (renamed)", "GPT C". Any date matches,
        // because an auto-created group is dated by its series or battery start.
        using (var db = new ApplicationDbContext(options))
        {
            var group = await db.BenchmarkRunGroups.SingleAsync(g => g.Id == 1, TestContext.Current.CancellationToken);
            group.Name = BenchmarkRunGroupNames.Default("Suite One (renamed)", "GPT C", T0.AddDays(-3), 3);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var actual = await ListAsync(options);
        Assert.True(actual.Single(g => g.Id == 1).NameIsGenerated);

        // The same shape with the wrong run count is a name someone typed.
        using (var db = new ApplicationDbContext(options))
        {
            var group = await db.BenchmarkRunGroups.SingleAsync(g => g.Id == 1, TestContext.Current.CancellationToken);
            group.Name = BenchmarkRunGroupNames.Default("Suite One (renamed)", "GPT C", T0, 4);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        Assert.False((await ListAsync(options)).Single(g => g.Id == 1).NameIsGenerated);
    }

    [Fact]
    public async Task AMemberReRunAfterTheAnalysis_MakesItStale_OnTheListAndTheDetail()
    {
        var options = NewDatabase();
        await SeedAsync(options);

        // Group 2's analysis (T0 + 2 d + 1 h) covers its current membership.
        Assert.False((await ListAsync(options)).Single(g => g.Id == 2).AnalysisStale);

        using (var db = new ApplicationDbContext(options))
        {
            var run = await db.BenchmarkRuns.SingleAsync(r => r.Id == 14, TestContext.Current.CancellationToken);
            run.RerunCompletedAtUtc = T0.AddDays(2);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // A re-run before the analysis is already in it.
        Assert.False((await ListAsync(options)).Single(g => g.Id == 2).AnalysisStale);

        using (var db = new ApplicationDbContext(options))
        {
            var run = await db.BenchmarkRuns.SingleAsync(r => r.Id == 14, TestContext.Current.CancellationToken);
            run.RerunCompletedAtUtc = T0.AddDays(2).AddHours(2);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        Assert.True((await ListAsync(options)).Single(g => g.Id == 2).AnalysisStale);

        using var detailDb = new ApplicationDbContext(options);
        var service = new BenchmarkGroupAnalysisService(
            detailDb, Microsoft.Extensions.Logging.Abstractions.NullLogger<BenchmarkGroupAnalysisService>.Instance);
        var detail = Assert.IsType<BenchmarkRunGroupDto>(
            Assert.IsType<OkObjectResult>(await CreateController(detailDb, service).GetRunGroup(2)).Value);
        Assert.True(detail.AnalysisStale);
        Assert.Equal(new long[] { 14 }, detail.MemberRunIds);
    }
}
