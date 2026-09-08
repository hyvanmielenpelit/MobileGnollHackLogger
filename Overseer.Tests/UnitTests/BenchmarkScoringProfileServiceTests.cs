namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
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
        var stored = await db.BenchmarkScoringProfiles.FirstAsync(p => p.Name == "Speed-Weighted Experiment", TestContext.Current.CancellationToken);

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

    // --- CanonicalSignature ---------------------------------------------------------------------

    /// <summary>
    /// One mutation per scoring field on <see cref="BenchmarkScoringProfile"/>. A field left out of
    /// the signature makes two profiles that score differently compare equal — a false replicate,
    /// which is worse than a false difference — so this list must be extended whenever the entity
    /// gains a field. The names of any fields that failed to move the signature are reported, so a
    /// failure says which one is missing.
    /// </summary>
    [Fact]
    public void CanonicalSignature_MovesWithEveryScoringField()
    {
        var mutations = new (string Field, Action<BenchmarkScoringProfile> Mutate)[]
        {
            (nameof(BenchmarkScoringProfile.WeightAccuracy), p => p.WeightAccuracy = 0.60),
            (nameof(BenchmarkScoringProfile.WeightCompleteness), p => p.WeightCompleteness = 0.20),
            (nameof(BenchmarkScoringProfile.WeightConciseness), p => p.WeightConciseness = 0.15),
            (nameof(BenchmarkScoringProfile.WeightReadability), p => p.WeightReadability = 0.15),
            (nameof(BenchmarkScoringProfile.LevelScoresJson), p => p.LevelScoresJson = "[1, 15, 35, 55, 72, 90, 100]"),
            (nameof(BenchmarkScoringProfile.CriticalErrorCeiling), p => p.CriticalErrorCeiling = 30),
            (nameof(BenchmarkScoringProfile.SecondOpinionQualityThreshold), p => p.SecondOpinionQualityThreshold = 60),
            (nameof(BenchmarkScoringProfile.SecondOpinionMode), p => p.SecondOpinionMode = (int)BenchmarkSecondOpinionMode.All),
            (nameof(BenchmarkScoringProfile.SecondOpinionBlind), p => p.SecondOpinionBlind = false),
            (nameof(BenchmarkScoringProfile.SecondOpinionOutlierDeltaPoints), p => p.SecondOpinionOutlierDeltaPoints = 30),
            (nameof(BenchmarkScoringProfile.SecondOpinionMinimumSample), p => p.SecondOpinionMinimumSample = 6),
            (nameof(BenchmarkScoringProfile.SpeedTargetMs), p => p.SpeedTargetMs = 18000),
            (nameof(BenchmarkScoringProfile.SpeedDecayK), p => p.SpeedDecayK = 22.0),
            (nameof(BenchmarkScoringProfile.SpeedDifficultyScaling), p => p.SpeedDifficultyScaling = 1.5),
            (nameof(BenchmarkScoringProfile.MaxParallelQuestions), p => p.MaxParallelQuestions = 3)
        };

        string baseline = BenchmarkScoringProfileService.CanonicalSignature(FullProfile());
        var unmoved = new List<string>();

        foreach (var (field, mutate) in mutations)
        {
            var mutated = FullProfile();
            mutate(mutated);

            if (BenchmarkScoringProfileService.CanonicalSignature(mutated) == baseline)
            {
                unmoved.Add(field);
            }
        }

        Assert.Empty(unmoved);
    }

    /// <summary>
    /// The four fields on the entity that carry no scoring meaning. A rename, a change of which
    /// profile is the default, or an edit-and-revert must leave the signature where it was.
    /// </summary>
    [Fact]
    public void CanonicalSignature_IgnoresNameIsDefaultAndTimestamps()
    {
        var baseline = FullProfile();

        var relabelled = FullProfile();
        relabelled.Id = 7;
        relabelled.Name = "Standard Intelligence Index (Default)";
        relabelled.IsDefault = false;
        relabelled.CreatedAtUtc = new DateTime(2026, 7, 4, 12, 0, 0, DateTimeKind.Utc);
        relabelled.ModifiedAtUtc = new DateTime(2026, 9, 6, 18, 30, 0, DateTimeKind.Utc);

        Assert.Equal(
            BenchmarkScoringProfileService.CanonicalSignature(baseline),
            BenchmarkScoringProfileService.CanonicalSignature(relabelled));
    }

    [Fact]
    public void CanonicalSignature_IgnoresLevelScoreWhitespaceAndTrailingZeros()
    {
        var spaced = FullProfile();
        spaced.LevelScoresJson = "[1, 15, 35, 55, 72, 87, 100]";

        var tight = FullProfile();
        tight.LevelScoresJson = "[1,15,35,55,72,87,100.0]";

        Assert.Equal(
            BenchmarkScoringProfileService.CanonicalSignature(spaced),
            BenchmarkScoringProfileService.CanonicalSignature(tight));
    }

    /// <summary>
    /// The fixed number format is what makes a weight compare by value rather than by binary
    /// representation: 0.1 + 0.2 is 0.30000000000000004, which is the same weight as 0.3.
    /// </summary>
    [Fact]
    public void CanonicalSignature_IgnoresFloatingPointRepresentationOfADoubleField()
    {
        var plain = FullProfile();
        plain.WeightAccuracy = 0.3;

        var computed = FullProfile();
        computed.WeightAccuracy = 0.1 + 0.2;

        // Guards the test from going vacuous: the two doubles really are different values.
        Assert.True(plain.WeightAccuracy != computed.WeightAccuracy);

        Assert.Equal(
            BenchmarkScoringProfileService.CanonicalSignature(plain),
            BenchmarkScoringProfileService.CanonicalSignature(computed));
    }

    [Fact]
    public void CanonicalSignature_IsStableAcrossInstances()
    {
        Assert.Equal(
            BenchmarkScoringProfileService.CanonicalSignature(FullProfile()),
            BenchmarkScoringProfileService.CanonicalSignature(FullProfile()));
    }

    [Fact]
    public void CanonicalSignature_RendersAnUnparseableLevelScoreTableDeterministically()
    {
        var broken = FullProfile();
        broken.LevelScoresJson = "[1, 15, oops]";

        string first = BenchmarkScoringProfileService.CanonicalSignature(broken);

        Assert.Equal(first, BenchmarkScoringProfileService.CanonicalSignature(broken));
        Assert.NotEqual(BenchmarkScoringProfileService.CanonicalSignature(FullProfile()), first);
    }

    private static BenchmarkScoringProfile FullProfile()
    {
        return new BenchmarkScoringProfile
        {
            Id = 1,
            Name = "Standard Intelligence Index",
            IsDefault = true,
            WeightAccuracy = 0.55,
            WeightCompleteness = 0.25,
            WeightConciseness = 0.10,
            WeightReadability = 0.10,
            LevelScoresJson = "[1, 15, 35, 55, 72, 87, 100]",
            CriticalErrorCeiling = 25,
            SecondOpinionQualityThreshold = 50,
            SecondOpinionMode = (int)BenchmarkSecondOpinionMode.FlaggedPlusSample,
            SecondOpinionBlind = true,
            SecondOpinionOutlierDeltaPoints = 25,
            SecondOpinionMinimumSample = 4,
            SpeedTargetMs = 15000,
            SpeedDecayK = 20.0,
            SpeedDifficultyScaling = 1.0,
            MaxParallelQuestions = 1,
            CreatedAtUtc = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            ModifiedAtUtc = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc)
        };
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
