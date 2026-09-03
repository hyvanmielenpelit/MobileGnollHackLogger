namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MobileGnollHackLogger.Data;

public class BenchmarkScoringProfileService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BenchmarkScoringProfileService> _logger;

    public BenchmarkScoringProfileService(
        IServiceScopeFactory scopeFactory,
        ILogger<BenchmarkScoringProfileService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<BenchmarkScoringProfile> GetDefaultProfileAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var profile = await db.BenchmarkScoringProfiles
            .FirstOrDefaultAsync(p => p.IsDefault);

        if (profile == null)
        {
            profile = await SeedDefaultProfileInternalAsync(db);
        }

        return profile;
    }

    public async Task<BenchmarkScoringProfile?> GetProfileByIdAsync(long id)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await db.BenchmarkScoringProfiles.FindAsync(id);
    }

    public async Task<List<BenchmarkScoringProfile>> GetAllProfilesAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var list = await db.BenchmarkScoringProfiles
            .OrderByDescending(p => p.IsDefault)
            .ThenBy(p => p.Name)
            .ToListAsync();

        if (list.Count == 0)
        {
            var seeded = await SeedDefaultProfileInternalAsync(db);
            list.Add(seeded);
        }

        return list;
    }

    public async Task<(bool Success, BenchmarkScoringProfile? Profile, List<string> Errors)> CreateProfileAsync(BenchmarkScoringProfile profile)
    {
        if (!ValidateProfile(profile, out var errors))
        {
            return (false, null, errors);
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        bool nameExists = await db.BenchmarkScoringProfiles.AnyAsync(p => p.Name == profile.Name);
        if (nameExists)
        {
            return (false, null, new List<string> { $"A scoring profile with name '{profile.Name}' already exists." });
        }

        if (profile.IsDefault)
        {
            var existingDefaults = await db.BenchmarkScoringProfiles.Where(p => p.IsDefault).ToListAsync();
            foreach (var d in existingDefaults)
            {
                d.IsDefault = false;
                d.ModifiedAtUtc = DateTime.UtcNow;
            }
        }

        profile.CreatedAtUtc = DateTime.UtcNow;
        profile.ModifiedAtUtc = DateTime.UtcNow;

        db.BenchmarkScoringProfiles.Add(profile);
        await db.SaveChangesAsync();

        return (true, profile, new List<string>());
    }

    public async Task<(bool Success, BenchmarkScoringProfile? Profile, List<string> Errors)> UpdateProfileAsync(BenchmarkScoringProfile profile)
    {
        if (!ValidateProfile(profile, out var errors))
        {
            return (false, null, errors);
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var existing = await db.BenchmarkScoringProfiles.FindAsync(profile.Id);
        if (existing == null)
        {
            return (false, null, new List<string> { "Scoring profile not found." });
        }

        bool nameConflict = await db.BenchmarkScoringProfiles.AnyAsync(p => p.Name == profile.Name && p.Id != profile.Id);
        if (nameConflict)
        {
            return (false, null, new List<string> { $"A scoring profile with name '{profile.Name}' already exists." });
        }

        if (profile.IsDefault && !existing.IsDefault)
        {
            var existingDefaults = await db.BenchmarkScoringProfiles.Where(p => p.IsDefault && p.Id != profile.Id).ToListAsync();
            foreach (var d in existingDefaults)
            {
                d.IsDefault = false;
                d.ModifiedAtUtc = DateTime.UtcNow;
            }
        }
        else if (!profile.IsDefault && existing.IsDefault)
        {
            // Cannot unset default if it's the only default
            bool otherDefaults = await db.BenchmarkScoringProfiles.AnyAsync(p => p.IsDefault && p.Id != profile.Id);
            if (!otherDefaults)
            {
                return (false, null, new List<string> { "At least one scoring profile must remain marked as the default." });
            }
        }

        existing.Name = profile.Name;
        existing.IsDefault = profile.IsDefault;
        existing.WeightAccuracy = profile.WeightAccuracy;
        existing.WeightCompleteness = profile.WeightCompleteness;
        existing.WeightConciseness = profile.WeightConciseness;
        existing.WeightReadability = profile.WeightReadability;
        existing.LevelScoresJson = profile.LevelScoresJson;
        existing.CriticalErrorCeiling = profile.CriticalErrorCeiling;
        existing.SecondOpinionQualityThreshold = profile.SecondOpinionQualityThreshold;
        existing.SecondOpinionMode = profile.SecondOpinionMode;
        existing.SecondOpinionOutlierDeltaPoints = profile.SecondOpinionOutlierDeltaPoints;
        existing.SpeedTargetMs = profile.SpeedTargetMs;
        existing.SpeedDecayK = profile.SpeedDecayK;
        existing.SpeedDifficultyScaling = profile.SpeedDifficultyScaling;
        existing.MaxParallelQuestions = profile.MaxParallelQuestions;
        existing.ModifiedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync();

        return (true, existing, new List<string>());
    }

    public async Task<(bool Success, string? Error)> SetDefaultProfileAsync(long id)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var target = await db.BenchmarkScoringProfiles.FindAsync(id);
        if (target == null)
        {
            return (false, "Scoring profile not found.");
        }

        var allProfiles = await db.BenchmarkScoringProfiles.ToListAsync();
        foreach (var p in allProfiles)
        {
            p.IsDefault = (p.Id == id);
            p.ModifiedAtUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> DeleteProfileAsync(long id)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var target = await db.BenchmarkScoringProfiles.FindAsync(id);
        if (target == null)
        {
            return (false, "Scoring profile not found.");
        }

        if (target.IsDefault)
        {
            return (false, "Cannot delete the default scoring profile. Set another profile as default first.");
        }

        int count = await db.BenchmarkScoringProfiles.CountAsync();
        if (count <= 1)
        {
            return (false, "Cannot delete the only scoring profile.");
        }

        db.BenchmarkScoringProfiles.Remove(target);
        await db.SaveChangesAsync();
        return (true, null);
    }

    public bool ValidateProfile(BenchmarkScoringProfile profile, out List<string> errors)
    {
        errors = new List<string>();

        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            errors.Add("Profile name is required.");
        }

        double weightSum = profile.WeightAccuracy + profile.WeightCompleteness + profile.WeightConciseness + profile.WeightReadability;
        if (Math.Abs(weightSum - 1.0) > 0.001)
        {
            errors.Add($"Weights must sum to 1.0 (current sum: {weightSum:F3}).");
        }

        if (profile.WeightAccuracy < 0 || profile.WeightCompleteness < 0 || profile.WeightConciseness < 0 || profile.WeightReadability < 0)
        {
            errors.Add("Weights must be non-negative.");
        }

        try
        {
            var levels = ParseLevelScores(profile.LevelScoresJson);
            if (levels.Count != 7)
            {
                errors.Add($"Level scores table must have exactly 7 entries (found {levels.Count}).");
            }
            else
            {
                for (int i = 0; i < levels.Count - 1; i++)
                {
                    if (levels[i] >= levels[i + 1])
                    {
                        errors.Add("Level scores must be in strictly ascending order.");
                        break;
                    }
                }

                if (levels.Any(l => l < 1 || l > 100))
                {
                    errors.Add("Level scores must be between 1 and 100.");
                }
            }
        }
        catch (Exception)
        {
            errors.Add("LevelScoresJson must be a valid JSON array of 7 integers.");
        }

        if (profile.CriticalErrorCeiling < 1 || profile.CriticalErrorCeiling > 100)
        {
            errors.Add("CriticalErrorCeiling must be between 1 and 100.");
        }

        // 0 is meaningful here, unlike the ceiling above: it disables the score trigger and
        // leaves second opinions to critical errors alone.
        if (profile.SecondOpinionQualityThreshold < 0 || profile.SecondOpinionQualityThreshold > 100)
        {
            errors.Add("SecondOpinionQualityThreshold must be between 0 and 100.");
        }

        if (!Enum.IsDefined(typeof(BenchmarkSecondOpinionMode), profile.SecondOpinionMode))
        {
            errors.Add("SecondOpinionMode must be Off (0), Flagged (1), FlaggedAndOutliers (2), or All (3).");
        }

        // Only binding in FlaggedAndOutliers. A zero delta there would select nothing, which
        // contradicts the mode the operator chose; outside that mode the field is inert, so an
        // arbitrary stored value must not fail validation.
        if (profile.SecondOpinionMode == (int)BenchmarkSecondOpinionMode.FlaggedAndOutliers &&
            (profile.SecondOpinionOutlierDeltaPoints <= 0 || profile.SecondOpinionOutlierDeltaPoints > 100))
        {
            errors.Add("SecondOpinionOutlierDeltaPoints must be between 1 and 100 when SecondOpinionMode is FlaggedAndOutliers.");
        }

        if (profile.SpeedTargetMs <= 0)
        {
            errors.Add("SpeedTargetMs must be greater than 0.");
        }

        if (profile.SpeedDecayK <= 0)
        {
            errors.Add("SpeedDecayK must be greater than 0.");
        }

        if (profile.SpeedDifficultyScaling < 0.0 || profile.SpeedDifficultyScaling > 5.0)
        {
            errors.Add("SpeedDifficultyScaling must be between 0.0 and 5.0.");
        }

        if (profile.MaxParallelQuestions < 1)
        {
            errors.Add("MaxParallelQuestions must be at least 1.");
        }

        return errors.Count == 0;
    }

    public List<int> ParseLevelScores(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<int> { 1, 15, 35, 55, 72, 87, 100 };
        }

        return JsonSerializer.Deserialize<List<int>>(json) ?? new List<int> { 1, 15, 35, 55, 72, 87, 100 };
    }

    public BenchmarkScoringConstants ToConstants(BenchmarkScoringProfile profile)
    {
        return new BenchmarkScoringConstants
        {
            WeightAccuracy = profile.WeightAccuracy,
            WeightCompleteness = profile.WeightCompleteness,
            WeightConciseness = profile.WeightConciseness,
            WeightReadability = profile.WeightReadability,
            LevelScores = ParseLevelScores(profile.LevelScoresJson),
            CriticalErrorCeiling = profile.CriticalErrorCeiling,
            SecondOpinionQualityThreshold = profile.SecondOpinionQualityThreshold,
            SecondOpinionMode = Enum.IsDefined(typeof(BenchmarkSecondOpinionMode), profile.SecondOpinionMode)
                ? (BenchmarkSecondOpinionMode)profile.SecondOpinionMode
                : BenchmarkSecondOpinionMode.Flagged,
            SecondOpinionOutlierDeltaPoints = profile.SecondOpinionOutlierDeltaPoints,
            SpeedTargetMs = profile.SpeedTargetMs,
            SpeedDecayK = profile.SpeedDecayK,
            SpeedDifficultyScaling = profile.SpeedDifficultyScaling
        };
    }

    public async Task EnsureDefaultProfileSeededAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        if (!await db.BenchmarkScoringProfiles.AnyAsync())
        {
            await SeedDefaultProfileInternalAsync(db);
        }
    }

    private async Task<BenchmarkScoringProfile> SeedDefaultProfileInternalAsync(ApplicationDbContext db)
    {
        var defaultProfile = new BenchmarkScoringProfile
        {
            Name = "Standard Intelligence Index",
            IsDefault = true,
            WeightAccuracy = 0.55,
            WeightCompleteness = 0.25,
            WeightConciseness = 0.10,
            WeightReadability = 0.10,
            LevelScoresJson = "[1, 15, 35, 55, 72, 87, 100]",
            CriticalErrorCeiling = 25,
            SecondOpinionQualityThreshold = 50,
            // Flagged, not All: a seeded default must reproduce existing behaviour rather than
            // silently double every future run's assessor spend. The editor recommends All.
            SecondOpinionMode = (int)BenchmarkSecondOpinionMode.Flagged,
            SecondOpinionOutlierDeltaPoints = 25,
            // Recalibrated: the old 5000 ms / k=25 pair drove the speed score to its floor at
            // roughly 78 s, tying together every slower answer on an agentic run. See
            // BenchmarkScoringConstants for the two invariants these satisfy. Existing databases
            // are updated by the AddBenchmarkIntegrityAndTiming migration.
            SpeedTargetMs = 15000,
            SpeedDecayK = 20.0,
            SpeedDifficultyScaling = 1.0,
            MaxParallelQuestions = 1,
            CreatedAtUtc = DateTime.UtcNow,
            ModifiedAtUtc = DateTime.UtcNow
        };

        db.BenchmarkScoringProfiles.Add(defaultProfile);
        await db.SaveChangesAsync();
        _logger.LogInformation("Seeded default BenchmarkScoringProfile: {Name}", defaultProfile.Name);
        return defaultProfile;
    }
}
