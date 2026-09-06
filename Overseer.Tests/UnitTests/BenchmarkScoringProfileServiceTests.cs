namespace Overseer.Tests.UnitTests;

using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MobileGnollHackLogger.Data;
using Overseer.Services.Benchmarking;
using Xunit;

public class BenchmarkScoringProfileServiceTests
{
    [Fact]
    public async Task DefaultProfileSeed_UsesFlaggedPlusSample_WithAMinimumSampleOfFour()
    {
        var service = NewService(out _);

        var profile = await service.GetDefaultProfileAsync();

        Assert.True(profile.IsDefault);
        Assert.Equal((int)BenchmarkSecondOpinionMode.FlaggedPlusSample, profile.SecondOpinionMode);
        Assert.Equal(4, profile.SecondOpinionMinimumSample);
    }

    [Fact]
    public async Task NonDefaultProfile_LoadsWithAMinimumSampleOfZero()
    {
        // The grading regime must never travel to a profile that did not ask for it: a non-zero
        // column default would have moved every existing profile to sampled second opinions the
        // moment the migration ran (BenchmarkScoringProfile.SecondOpinionMinimumSample).
        var service = NewService(out var dbOptions);

        var created = await service.CreateProfileAsync(new BenchmarkScoringProfile
        {
            Name = "Speed-Weighted Experiment",
            SecondOpinionMode = (int)BenchmarkSecondOpinionMode.Flagged
        });

        Assert.True(created.Success);

        await using var db = new ApplicationDbContext(dbOptions);
        var stored = await db.BenchmarkScoringProfiles.FirstAsync(p => p.Name == "Speed-Weighted Experiment");

        Assert.False(stored.IsDefault);
        Assert.Equal(0, stored.SecondOpinionMinimumSample);
    }

    [Fact]
    public void ValidateProfile_AcceptsFlaggedPlusSample_WithAPositiveMinimumSample()
    {
        // Mode 4 is the value the validation message enumerates but an older Enum.IsDefined table
        // would have rejected; this is the "controller accepts mode 4" case at the layer that
        // actually decides it.
        var service = NewService(out _);

        bool valid = service.ValidateProfile(
            NewProfile(BenchmarkSecondOpinionMode.FlaggedPlusSample, minimumSample: 4),
            out var errors);

        Assert.True(valid);
        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateProfile_RejectsFlaggedPlusSample_WithAZeroMinimumSample()
    {
        // A zero target in this mode would top up nothing, which contradicts the mode the operator
        // chose — the same reasoning the outlier delta is validated under.
        var service = NewService(out _);

        bool valid = service.ValidateProfile(
            NewProfile(BenchmarkSecondOpinionMode.FlaggedPlusSample, minimumSample: 0),
            out var errors);

        Assert.False(valid);
        Assert.Contains(errors, e => e.Contains("SecondOpinionMinimumSample must be greater than 0", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateProfile_AcceptsAZeroMinimumSample_OutsideFlaggedPlusSample()
    {
        // The field is inert outside the mode, so the 0 every other profile carries must not fail
        // validation and lock those profiles out of the editor.
        var service = NewService(out _);

        bool valid = service.ValidateProfile(
            NewProfile(BenchmarkSecondOpinionMode.Flagged, minimumSample: 0),
            out var errors);

        Assert.True(valid);
        Assert.Empty(errors);
    }

    private static BenchmarkScoringProfile NewProfile(BenchmarkSecondOpinionMode mode, int minimumSample)
    {
        return new BenchmarkScoringProfile
        {
            Name = "Profile Under Test",
            SecondOpinionMode = (int)mode,
            SecondOpinionMinimumSample = minimumSample
        };
    }

    private static BenchmarkScoringProfileService NewService(out DbContextOptions<ApplicationDbContext> dbOptions)
    {
        dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var options = dbOptions;
        var services = new ServiceCollection();
        services.AddScoped(_ => new ApplicationDbContext(options));
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        return new BenchmarkScoringProfileService(
            scopeFactory,
            NullLogger<BenchmarkScoringProfileService>.Instance);
    }
}
