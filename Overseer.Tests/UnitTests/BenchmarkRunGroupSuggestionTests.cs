namespace Overseer.Tests.UnitTests;

using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Services.Benchmarking;
using Xunit;

using static Overseer.Tests.UnitTests.BenchmarkRunGroupCreationTests;

/// <summary>
/// Suggested groups: clusters of a suite's settled runs sharing one full comparability key hash,
/// left out once a group holds them.
/// </summary>
public class BenchmarkRunGroupSuggestionTests
{
    private static async Task<System.Collections.Generic.List<Overseer.Models.BenchmarkRunGroupSuggestionDto>> SuggestAsync(
        DbContextOptions<ApplicationDbContext> options, long suiteId)
    {
        await using var db = new ApplicationDbContext(options);
        return await new BenchmarkRunGroupSuggestionService(db).SuggestAsync(suiteId, TestContext.Current.CancellationToken);
    }

    private static async Task AddGroupAsync(DbContextOptions<ApplicationDbContext> options, long suiteId, params long[] runIds)
    {
        await using var db = new ApplicationDbContext(options);
        var group = new BenchmarkRunGroup
        {
            Name = "Existing",
            BenchmarkSuiteId = suiteId,
            Tier = BenchmarkRunGroupTier.Replicate
        };
        foreach (long id in runIds)
        {
            group.Members.Add(new BenchmarkRunGroupMember { BenchmarkRunId = id });
        }
        db.BenchmarkRunGroups.Add(group);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task ThreeRunsWithOneKey_AreOneSuggestion()
    {
        var options = NewDatabase();
        var suite = await SeedSuiteAsync(options);
        var runIds = await SeedRunsAsync(options, suite.Id, 3, T0, seriesId: 7);

        var suggestion = Assert.Single(await SuggestAsync(options, suite.Id));

        Assert.Equal(runIds.OrderBy(id => id), suggestion.RunIds);
        Assert.Equal(3, suggestion.RunCount);
        Assert.Equal("Replicate", suggestion.Tier);
        Assert.False(string.IsNullOrEmpty(suggestion.KeyHash));
        Assert.Equal("GPT Luna", suggestion.TestedModel.DisplayName);
        Assert.Equal("gpt-luna", suggestion.TestedModel.Id);
        Assert.Equal("OpenAI", suggestion.TestedModel.Provider);
        Assert.Equal("high", suggestion.TestedModel.ThinkingLevel);
        Assert.Equal(T0, suggestion.FirstStartedAtUtc);
        Assert.Equal(T0.AddHours(2), suggestion.LastStartedAtUtc);
        Assert.Equal(new long[] { 7 }, suggestion.SeriesIds);
        Assert.Equal(80.0, suggestion.MeanQualityIndex);
    }

    [Fact]
    public async Task ACluster_AGroupHoldsExactlyOrWithin_IsNotSuggested()
    {
        var options = NewDatabase();
        var suite = await SeedSuiteAsync(options);
        var runIds = await SeedRunsAsync(options, suite.Id, 3, T0);

        await AddGroupAsync(options, suite.Id, runIds);
        Assert.Empty(await SuggestAsync(options, suite.Id));

        // A group of a larger set that includes the cluster hides it as well.
        var otherOptions = NewDatabase();
        var otherSuite = await SeedSuiteAsync(otherOptions);
        var clustered = await SeedRunsAsync(otherOptions, otherSuite.Id, 2, T0);
        var other = await SeedRunsAsync(otherOptions, otherSuite.Id, 1, T0.AddDays(1), ToolGuidesB);
        await AddGroupAsync(otherOptions, otherSuite.Id, clustered.Concat(other).ToArray());
        Assert.Empty(await SuggestAsync(otherOptions, otherSuite.Id));

        // A group of only part of a cluster does not.
        var partOptions = NewDatabase();
        var partSuite = await SeedSuiteAsync(partOptions);
        var partRuns = await SeedRunsAsync(partOptions, partSuite.Id, 3, T0);
        await AddGroupAsync(partOptions, partSuite.Id, partRuns.Take(2).ToArray());
        Assert.Equal(3, Assert.Single(await SuggestAsync(partOptions, partSuite.Id)).RunCount);
    }

    [Fact]
    public async Task TwoKeys_AreTwoSuggestions_NewestFirst()
    {
        var options = NewDatabase();
        var suite = await SeedSuiteAsync(options);
        var older = await SeedRunsAsync(options, suite.Id, 2, T0, ToolGuidesA);
        var newer = await SeedRunsAsync(options, suite.Id, 2, T0.AddDays(1), ToolGuidesB);

        var suggestions = await SuggestAsync(options, suite.Id);

        Assert.Equal(2, suggestions.Count);
        Assert.Equal(newer.OrderBy(id => id), suggestions[0].RunIds);
        Assert.Equal(older.OrderBy(id => id), suggestions[1].RunIds);
        Assert.NotEqual(suggestions[0].KeyHash, suggestions[1].KeyHash);
    }

    [Fact]
    public async Task AnUnsettledRun_IsIgnored()
    {
        var options = NewDatabase();
        var suite = await SeedSuiteAsync(options);
        var settled = await SeedRunsAsync(options, suite.Id, 2, T0);
        await SeedRunsAsync(options, suite.Id, 1, T0.AddHours(5), status: BenchmarkRunStatus.Running);

        var suggestion = Assert.Single(await SuggestAsync(options, suite.Id));
        Assert.Equal(settled.OrderBy(id => id), suggestion.RunIds);

        // One settled run beside a running one is no cluster.
        var loneOptions = NewDatabase();
        var loneSuite = await SeedSuiteAsync(loneOptions);
        await SeedRunsAsync(loneOptions, loneSuite.Id, 1, T0);
        await SeedRunsAsync(loneOptions, loneSuite.Id, 1, T0.AddHours(1), status: BenchmarkRunStatus.Failed);
        Assert.Empty(await SuggestAsync(loneOptions, loneSuite.Id));
    }
}
