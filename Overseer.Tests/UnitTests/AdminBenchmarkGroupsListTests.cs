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

    private static AdminBenchmarkController CreateController(ApplicationDbContext db)
        => new(
            db, null!, null!, new BenchmarkRunManager(), null!, null!, null!, null!, null!, null!,
            null!, null!, null!, null!, null!, null!, null!, null!, null!);

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
            ResultJson = "{}"
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

        await db.SaveChangesAsync();
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

            dtos.Add(new BenchmarkRunGroupDto
            {
                Id = group.Id,
                Name = group.Name,
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
                LatestAnalysisId = latest?.Id,
                LatestAnalysisAtUtc = latest?.ComputedAtUtc,
                AnalysisStale = BenchmarkGroupAnalysisService.IsStale(group, latest)
            });
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
}
