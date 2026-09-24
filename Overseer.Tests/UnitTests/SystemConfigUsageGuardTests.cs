using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services;
using Overseer.Services.Benchmarking;
using Overseer.Tests.Helpers;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// What may block deleting a system configuration: only something calling its model right now.
/// History never blocks, and neither does a stopped series, which is merely counted.
/// </summary>
public class SystemConfigUsageGuardTests
{
    private const long ConfigId = 5;

    private readonly ApplicationDbContext _db = new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private readonly BenchmarkDifficultyJobManager _difficulty = new();
    private readonly BenchmarkGenerationJobManager _generation = new();
    private readonly BenchmarkRubricCheckJobManager _rubricCheck = new();
    private readonly BenchmarkRubricGapAuthorJobManager _gapAuthor = new();

    private SystemConfigUsageGuard Guard() => new(_db, _difficulty, _generation, _rubricCheck, _gapAuthor);

    private async Task AddRunAsync(BenchmarkRunStatus status, long? assessorId = ConfigId, long? verifierId = null)
    {
        _db.BenchmarkRuns.Add(BenchmarkModelSnapshots.Attach(new BenchmarkRun
        {
            SuiteName = "Sokoban basics",
            Status = status,
            TestedModelConfigurationId = 1,
            AssessorModelConfigurationId = assessorId,
            ClaimVerifierModelConfigurationId = verifierId
        }));
        await _db.SaveChangesAsync();
    }

    private async Task AddSeriesAsync(BenchmarkRunSeriesStatus status)
    {
        _db.BenchmarkRunSeries.Add(new BenchmarkRunSeries
        {
            SuiteName = "Sokoban basics",
            Status = status,
            StartRequestJson = JsonSerializer.Serialize(new StartBenchmarkRunRequest
            {
                SuiteId = 1,
                TestedModelConfigurationId = 1,
                AssessorModelConfigurationId = 2,
                ClaimVerifierModelConfigurationId = ConfigId
            })
        });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task ARunningRun_Blocks_AndNamesItsRoles()
    {
        await AddRunAsync(BenchmarkRunStatus.Running, assessorId: ConfigId, verifierId: ConfigId);

        var blocker = Assert.Single(await Guard().FindActiveUsesAsync(ConfigId));

        Assert.Equal("run", blocker.Kind);
        Assert.NotNull(blocker.RunId);
        Assert.Contains("Sokoban basics", blocker.Label);
        Assert.Equal(new[] { "assessor", "claim verifier" }, blocker.Roles);
    }

    [Fact]
    public async Task ACompletedRun_DoesNotBlock_AndIsCountedAsHistory()
    {
        await AddRunAsync(BenchmarkRunStatus.Completed);
        var config = new SystemAiApiConfiguration { Id = ConfigId, DisplayName = "Assessor", Provider = "OpenAI", ModelId = "m" };

        var check = await Guard().CheckDeletionAsync(config);

        Assert.True(check.CanDelete);
        Assert.Empty(check.Blockers);
        Assert.Equal(1, check.BenchmarkRunReferenceCount);
    }

    [Theory]
    [InlineData(BenchmarkRunSeriesStatus.Pending)]
    [InlineData(BenchmarkRunSeriesStatus.Running)]
    [InlineData(BenchmarkRunSeriesStatus.WaitingForCap)]
    public async Task AnActiveSeries_BlocksThroughItsStartRequest(BenchmarkRunSeriesStatus status)
    {
        await AddSeriesAsync(status);

        var blocker = Assert.Single(await Guard().FindActiveUsesAsync(ConfigId));

        Assert.Equal("series", blocker.Kind);
        Assert.Equal(new[] { "claim verifier" }, blocker.Roles);
    }

    [Fact]
    public async Task AStoppedSeries_DoesNotBlock_AndIsCounted()
    {
        await AddSeriesAsync(BenchmarkRunSeriesStatus.Stopped);
        var config = new SystemAiApiConfiguration { Id = ConfigId, DisplayName = "Verifier", Provider = "OpenAI", ModelId = "m" };

        var check = await Guard().CheckDeletionAsync(config);

        Assert.True(check.CanDelete);
        Assert.Equal(1, check.StoppedSeriesCount);
    }

    [Fact]
    public async Task ADifficultyJob_BlocksWhileActive()
    {
        var job = new BenchmarkDifficultyJob { SuiteName = "Sokoban basics", AssessorConfigId = ConfigId };
        Assert.True(_difficulty.TryStart(job, out _));

        Assert.Equal("difficultyJob", Assert.Single(await Guard().FindActiveUsesAsync(ConfigId)).Kind);

        _difficulty.Complete(job.Id, BenchmarkDifficultyJobStatus.Completed);
        Assert.Empty(await Guard().FindActiveUsesAsync(ConfigId));
    }

    [Fact]
    public async Task AGenerationJob_BlocksWhileActive()
    {
        var job = new BenchmarkGenerationJob { SuiteName = "Sokoban basics", GeneratorConfigId = ConfigId };
        Assert.True(_generation.TryStart(job, out _));

        Assert.Equal("generationJob", Assert.Single(await Guard().FindActiveUsesAsync(ConfigId)).Kind);
    }

    [Fact]
    public async Task ARubricCheckJob_BlocksWhileActive()
    {
        var job = new BenchmarkRubricCheckJob { SuiteName = "Sokoban basics", CheckerConfigId = ConfigId };
        Assert.True(_rubricCheck.TryStart(job, out _));

        Assert.Equal("rubricCheckJob", Assert.Single(await Guard().FindActiveUsesAsync(ConfigId)).Kind);
    }

    [Fact]
    public async Task ARubricGapAuthorJob_BlocksWhileActive()
    {
        var job = new BenchmarkRubricGapAuthorJob { SuiteName = "Sokoban basics", AuthorConfigId = ConfigId };
        Assert.True(_gapAuthor.TryStart(job, out _));

        Assert.Equal("rubricGapAuthorJob", Assert.Single(await Guard().FindActiveUsesAsync(ConfigId)).Kind);
    }

    [Fact]
    public async Task AJobForAnotherConfiguration_DoesNotBlock()
    {
        Assert.True(_difficulty.TryStart(new BenchmarkDifficultyJob { SuiteName = "S", AssessorConfigId = ConfigId + 1 }, out _));

        Assert.Empty(await Guard().FindActiveUsesAsync(ConfigId));
    }
}
