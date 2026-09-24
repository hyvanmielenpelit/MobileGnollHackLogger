using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services.Benchmarking;

namespace Overseer.Services;

/// <summary>
/// Decides whether a system AI configuration may be deleted now. History never blocks a delete:
/// benchmark history records its own settings snapshots and only an attribution id. What blocks is
/// something calling the model at this moment — a running benchmark run, an active series, or a
/// benchmark job in flight — because a delete would stop its calls partway through.
/// </summary>
public class SystemConfigUsageGuard
{
    private static readonly BenchmarkRunSeriesStatus[] ActiveSeriesStatuses =
    {
        BenchmarkRunSeriesStatus.Pending, BenchmarkRunSeriesStatus.Running, BenchmarkRunSeriesStatus.WaitingForCap
    };

    private readonly ApplicationDbContext _db;
    private readonly BenchmarkDifficultyJobManager _difficultyJobs;
    private readonly BenchmarkGenerationJobManager _generationJobs;
    private readonly BenchmarkRubricCheckJobManager _rubricCheckJobs;
    private readonly BenchmarkRubricGapAuthorJobManager _rubricGapAuthorJobs;

    public SystemConfigUsageGuard(
        ApplicationDbContext db,
        BenchmarkDifficultyJobManager difficultyJobs,
        BenchmarkGenerationJobManager generationJobs,
        BenchmarkRubricCheckJobManager rubricCheckJobs,
        BenchmarkRubricGapAuthorJobManager rubricGapAuthorJobs)
    {
        _db = db;
        _difficultyJobs = difficultyJobs;
        _generationJobs = generationJobs;
        _rubricCheckJobs = rubricCheckJobs;
        _rubricGapAuthorJobs = rubricGapAuthorJobs;
    }

    /// <summary>Everything calling the configuration's model right now. Empty means a delete interrupts nothing.</summary>
    public async Task<List<SystemConfigBlockerDto>> FindActiveUsesAsync(long configId, CancellationToken ct = default)
    {
        var blockers = new List<SystemConfigBlockerDto>();

        var runs = await _db.BenchmarkRuns
            .IgnoreAutoIncludes()
            .Where(r => r.Status == BenchmarkRunStatus.Running)
            .Where(r => r.TestedModelConfigurationId == configId
                || r.AssessorModelConfigurationId == configId
                || r.SecondOpinionAssessorModelConfigurationId == configId
                || r.ClaimVerifierModelConfigurationId == configId)
            .Select(r => new
            {
                r.Id, r.SuiteName, r.StartedAtUtc,
                r.TestedModelConfigurationId, r.AssessorModelConfigurationId,
                r.SecondOpinionAssessorModelConfigurationId, r.ClaimVerifierModelConfigurationId
            })
            .ToListAsync(ct);

        foreach (var r in runs)
        {
            blockers.Add(new SystemConfigBlockerDto
            {
                Kind = "run",
                Id = r.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                RunId = r.Id,
                Label = $"Benchmark run #{r.Id} on suite '{r.SuiteName}'",
                Roles = Roles(configId, r.TestedModelConfigurationId, r.AssessorModelConfigurationId,
                    r.SecondOpinionAssessorModelConfigurationId, r.ClaimVerifierModelConfigurationId),
                StartedAtUtc = r.StartedAtUtc
            });
        }

        foreach (var (series, request) in await LoadSeriesNamingAsync(configId, ActiveSeriesStatuses, ct))
        {
            blockers.Add(new SystemConfigBlockerDto
            {
                Kind = "series",
                Id = series.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Label = $"Benchmark series #{series.Id} on suite '{series.SuiteName}'",
                Roles = Roles(configId, request.TestedModelConfigurationId, request.AssessorModelConfigurationId,
                    request.SecondOpinionAssessorModelConfigurationId, request.ClaimVerifierModelConfigurationId),
                StartedAtUtc = series.StartedAtUtc
            });
        }

        var difficulty = _difficultyJobs.Current;
        if (difficulty != null && difficulty.Status == BenchmarkDifficultyJobStatus.Running && difficulty.AssessorConfigId == configId)
        {
            blockers.Add(Job("difficultyJob", difficulty.Id, $"Difficulty assessment on suite '{difficulty.SuiteName}'",
                "difficulty assessor", difficulty.StartedAtUtc));
        }

        var generation = _generationJobs.Current;
        if (generation != null && generation.Status == BenchmarkGenerationJobStatus.Running && generation.GeneratorConfigId == configId)
        {
            blockers.Add(Job("generationJob", generation.Id, $"Question generation on suite '{generation.SuiteName}'",
                "question generator", generation.StartedAtUtc));
        }

        var rubricCheck = _rubricCheckJobs.Current;
        if (rubricCheck != null && rubricCheck.Status == BenchmarkRubricCheckJobStatus.Running && rubricCheck.CheckerConfigId == configId)
        {
            blockers.Add(Job("rubricCheckJob", rubricCheck.Id, $"Rubric check on suite '{rubricCheck.SuiteName}'",
                "rubric checker", rubricCheck.StartedAtUtc));
        }

        var gapAuthor = _rubricGapAuthorJobs.Current;
        if (gapAuthor != null && gapAuthor.Status == BenchmarkRubricGapAuthorJobStatus.Running && gapAuthor.AuthorConfigId == configId)
        {
            blockers.Add(Job("rubricGapAuthorJob", gapAuthor.Id, $"Rubric gap drafting on suite '{gapAuthor.SuiteName}'",
                "rubric author", gapAuthor.StartedAtUtc));
        }

        return blockers;
    }

    /// <summary>The blockers, and what a delete would remove and keep.</summary>
    public async Task<SystemConfigDeletionCheckDto> CheckDeletionAsync(SystemAiApiConfiguration config, CancellationToken ct = default)
    {
        long id = config.Id;
        var blockers = await FindActiveUsesAsync(id, ct);

        // Scans by attribution id: these columns carry no index, and the check runs once per click.
        int runReferences = await _db.BenchmarkRuns
            .IgnoreAutoIncludes()
            .CountAsync(r => r.TestedModelConfigurationId == id
                || r.AssessorModelConfigurationId == id
                || r.SecondOpinionAssessorModelConfigurationId == id
                || r.ClaimVerifierModelConfigurationId == id, ct);

        var stopped = await LoadSeriesNamingAsync(id, new[] { BenchmarkRunSeriesStatus.Stopped }, ct);

        return new SystemConfigDeletionCheckDto
        {
            ConfigId = id,
            DisplayName = config.DisplayName,
            CanDelete = blockers.Count == 0,
            Blockers = blockers,
            BenchmarkRunReferenceCount = runReferences,
            StoppedSeriesCount = stopped.Count,
            UserAssignmentCount = await _db.UserSystemAiApiConfigurations.CountAsync(a => a.SystemAiApiConfigurationId == id, ct),
            GroupAssignmentCount = await _db.GroupSystemAiApiConfigurations.CountAsync(a => a.SystemAiApiConfigurationId == id, ct),
            ConfidentialTrustCount = await _db.UserSystemModelConfidentialTrusts.CountAsync(t => t.SystemAiApiConfigurationId == id, ct)
        };
    }

    /// <summary>Series in one of <paramref name="statuses"/> whose stored start request names the configuration.</summary>
    private async Task<List<(BenchmarkRunSeries Series, StartBenchmarkRunRequest Request)>> LoadSeriesNamingAsync(
        long configId, BenchmarkRunSeriesStatus[] statuses, CancellationToken ct)
    {
        var series = await _db.BenchmarkRunSeries
            .AsNoTracking()
            .Where(s => statuses.Contains(s.Status))
            .ToListAsync(ct);

        var naming = new List<(BenchmarkRunSeries, StartBenchmarkRunRequest)>();
        foreach (var s in series)
        {
            StartBenchmarkRunRequest? request;
            try
            {
                request = JsonSerializer.Deserialize<StartBenchmarkRunRequest>(s.StartRequestJson);
            }
            catch (JsonException)
            {
                continue;
            }

            if (request != null && Roles(configId, request.TestedModelConfigurationId, request.AssessorModelConfigurationId,
                    request.SecondOpinionAssessorModelConfigurationId, request.ClaimVerifierModelConfigurationId).Count > 0)
            {
                naming.Add((s, request));
            }
        }

        return naming;
    }

    private static List<string> Roles(long configId, long? tested, long? assessor, long? secondOpinion, long? claimVerifier)
    {
        var roles = new List<string>();
        if (tested == configId) roles.Add("model under test");
        if (assessor == configId) roles.Add("assessor");
        if (secondOpinion == configId) roles.Add("second-opinion assessor");
        if (claimVerifier == configId) roles.Add("claim verifier");
        return roles;
    }

    private static SystemConfigBlockerDto Job(string kind, string id, string label, string role, DateTime startedAtUtc)
        => new() { Kind = kind, Id = id, Label = label, Roles = new List<string> { role }, StartedAtUtc = startedAtUtc };
}
