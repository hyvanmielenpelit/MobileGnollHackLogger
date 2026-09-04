namespace Overseer.Controllers;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services.Benchmarking;
using Overseer.Services.Tools;
using Microsoft.Extensions.DependencyInjection;

[Route("api/admin/benchmark")]
[Authorize(Policy = "AdminOnly")]
[ApiController]
public class AdminBenchmarkController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly BenchmarkService _benchmarkService;
    private readonly BenchmarkScoringProfileService _scoringProfileService;
    private readonly BenchmarkRunManager _runManager;
    private readonly BenchmarkDifficultyJobManager _difficultyJobManager;
    private readonly BenchmarkComplianceGuard _complianceGuard;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly Services.SourceCodeService _sourceCodeService;
    private readonly Services.NetHackWikiService _wikiService;
    private readonly IClientToolBridge _clientToolBridge;
    private readonly BenchmarkSnapshotImporter _snapshotImporter;
    private readonly BenchmarkGenerationJobManager _generationJobManager;
    private readonly BenchmarkGenerationService _generationService;
    private readonly BenchmarkRubricCheckJobManager _rubricCheckJobManager;
    private readonly BenchmarkRubricCheckService _rubricCheckService;

    public AdminBenchmarkController(
        ApplicationDbContext dbContext,
        BenchmarkService benchmarkService,
        BenchmarkScoringProfileService scoringProfileService,
        BenchmarkRunManager runManager,
        BenchmarkDifficultyJobManager difficultyJobManager,
        BenchmarkComplianceGuard complianceGuard,
        IServiceScopeFactory scopeFactory,
        Services.SourceCodeService sourceCodeService,
        Services.NetHackWikiService wikiService,
        IClientToolBridge clientToolBridge,
        BenchmarkSnapshotImporter snapshotImporter,
        BenchmarkGenerationJobManager generationJobManager,
        BenchmarkGenerationService generationService,
        BenchmarkRubricCheckJobManager rubricCheckJobManager,
        BenchmarkRubricCheckService rubricCheckService)
    {
        _dbContext = dbContext;
        _benchmarkService = benchmarkService;
        _scoringProfileService = scoringProfileService;
        _runManager = runManager;
        _difficultyJobManager = difficultyJobManager;
        _complianceGuard = complianceGuard;
        _scopeFactory = scopeFactory;
        _sourceCodeService = sourceCodeService;
        _wikiService = wikiService;
        _clientToolBridge = clientToolBridge;
        _snapshotImporter = snapshotImporter;
        _generationJobManager = generationJobManager;
        _generationService = generationService;
        _rubricCheckJobManager = rubricCheckJobManager;
        _rubricCheckService = rubricCheckService;
    }

    private static BenchmarkSuiteDto ToSuiteDto(BenchmarkSuite s) => new()
    {
        Id = s.Id,
        Name = s.Name,
        Description = s.Description,
        CreatedAtUtc = s.CreatedAtUtc,
        ModifiedAtUtc = s.ModifiedAtUtc,
        QuestionCount = s.Questions.Count,
        AssessedQuestionCount = s.Questions.Count(q => q.AssessedDifficulty != null),
        DifficultyFullyAssessed = s.Questions.Count > 0 && s.Questions.Count(q => q.AssessedDifficulty != null) == s.Questions.Count,
        GameSnapshotId = s.GameSnapshotId,
        GameSnapshotName = s.GameSnapshot?.Name,
        GameSnapshotCharCount = s.GameSnapshot?.CharCount,
        HasGeneratedQuestions = s.HasGeneratedQuestions,
        ReviewedQuestionCount = s.Questions.Count(q => !q.IsGenerated || (q.ReviewedAtRevision != null && q.ReviewedAtRevision == q.ItemRevision))
    };

    private static BenchmarkQuestionDto ToQuestionDto(BenchmarkQuestion q) => new()
    {
        Id = q.Id,
        BenchmarkSuiteId = q.BenchmarkSuiteId,
        OrderIndex = q.OrderIndex,
        ItemRevision = q.ItemRevision,
        QuestionText = q.QuestionText,
        Difficulty = q.Difficulty,
        ExpectedPoints = q.ExpectedPoints,
        IsGenerated = q.IsGenerated,
        ReviewedAtRevision = q.ReviewedAtRevision,
        ReviewedAtUtc = q.ReviewedAtUtc,
        ReviewedByUserId = q.ReviewedByUserId,
        IsReviewed = !q.IsGenerated || (q.ReviewedAtRevision != null && q.ReviewedAtRevision == q.ItemRevision),
        AssessedDifficulty = q.AssessedDifficulty,
        AssessedDifficultyModel = q.AssessedDifficultyModel,
        AssessedDifficultyAtUtc = q.AssessedDifficultyAtUtc,
        AssessedDifficultyModelConfigurationId = q.AssessedDifficultyModelConfigurationId,
        AssessedDifficultyProviderUsed = q.AssessedDifficultyProviderUsed,
        AssessedDifficultyModelIdUsed = q.AssessedDifficultyModelIdUsed,
        AssessedDifficultyThinkingLevelUsed = q.AssessedDifficultyThinkingLevelUsed,
        AssessedDifficultyReasoningModeUsed = q.AssessedDifficultyReasoningModeUsed,
        AssessedDifficultyReasoningSummaryUsed = q.AssessedDifficultyReasoningSummaryUsed,
        AssessedDifficultyServiceTierUsed = q.AssessedDifficultyServiceTierUsed,
        AssessedDifficultyMaxOutputTokensUsed = q.AssessedDifficultyMaxOutputTokensUsed,
        CreatedAtUtc = q.CreatedAtUtc,
        ModifiedAtUtc = q.ModifiedAtUtc
    };

    private static BenchmarkGameSnapshotDto ToSnapshotDto(BenchmarkGameSnapshot s, long? suiteId = null, string? suiteName = null) => new()
    {
        Id = s.Id,
        Name = s.Name,
        SanitizedText = s.SanitizedText,
        DigestText = s.DigestText,
        CharCount = s.CharCount,
        Sha256 = s.Sha256,
        CaptureMethod = s.CaptureMethod,
        SourceGnollHackVersion = s.SourceGnollHackVersion,
        Notes = s.Notes,
        CapturedAtUtc = s.CapturedAtUtc,
        CreatedAtUtc = s.CreatedAtUtc,
        ModifiedAtUtc = s.ModifiedAtUtc,
        SuiteId = suiteId,
        SuiteName = suiteName
    };

    private IActionResult? CheckConflictingBenchmarkJob(long suiteId, string requestingJobName)
    {
        var diffJob = _difficultyJobManager.Current;
        if (diffJob != null && diffJob.Status == BenchmarkDifficultyJobStatus.Running && diffJob.SuiteId == suiteId)
        {
            return Conflict(new { error = $"Cannot start {requestingJobName}: Difficulty assessment job '{diffJob.Id}' is currently running on this suite." });
        }
        var genJob = _generationJobManager.Current;
        if (genJob != null && genJob.Status == BenchmarkGenerationJobStatus.Running && genJob.SuiteId == suiteId)
        {
            return Conflict(new { error = $"Cannot start {requestingJobName}: Question generation job '{genJob.Id}' is currently running on this suite." });
        }
        var rubJob = _rubricCheckJobManager.Current;
        if (rubJob != null && rubJob.Status == BenchmarkRubricCheckJobStatus.Running && rubJob.SuiteId == suiteId)
        {
            return Conflict(new { error = $"Cannot start {requestingJobName}: Rubric verification job '{rubJob.Id}' is currently running on this suite." });
        }
        return null;
    }

    // --- Scoring Profiles CRUD ---

    [HttpGet("scoring-profiles")]
    public async Task<IActionResult> GetScoringProfiles()
    {
        var profiles = await _scoringProfileService.GetAllProfilesAsync();
        var dtos = profiles.Select(p => new BenchmarkScoringProfileDto
        {
            Id = p.Id,
            Name = p.Name,
            IsDefault = p.IsDefault,
            WeightAccuracy = p.WeightAccuracy,
            WeightCompleteness = p.WeightCompleteness,
            WeightConciseness = p.WeightConciseness,
            WeightReadability = p.WeightReadability,
            LevelScoresJson = p.LevelScoresJson,
            CriticalErrorCeiling = p.CriticalErrorCeiling,
            SecondOpinionQualityThreshold = p.SecondOpinionQualityThreshold,
            SecondOpinionMode = p.SecondOpinionMode,
            SecondOpinionOutlierDeltaPoints = p.SecondOpinionOutlierDeltaPoints,
            SpeedTargetMs = p.SpeedTargetMs,
            SpeedDecayK = p.SpeedDecayK,
            SpeedDifficultyScaling = p.SpeedDifficultyScaling,
            MaxParallelQuestions = p.MaxParallelQuestions,
            CreatedAtUtc = p.CreatedAtUtc,
            ModifiedAtUtc = p.ModifiedAtUtc
        }).ToList();

        return Ok(dtos);
    }

    [HttpPost("scoring-profiles")]
    public async Task<IActionResult> CreateScoringProfile([FromBody] CreateBenchmarkScoringProfileRequest request)
    {
        var profile = new BenchmarkScoringProfile
        {
            Name = request.Name?.Trim() ?? string.Empty,
            IsDefault = request.IsDefault,
            WeightAccuracy = request.WeightAccuracy,
            WeightCompleteness = request.WeightCompleteness,
            WeightConciseness = request.WeightConciseness,
            WeightReadability = request.WeightReadability,
            LevelScoresJson = request.LevelScoresJson,
            CriticalErrorCeiling = request.CriticalErrorCeiling,
            SecondOpinionQualityThreshold = request.SecondOpinionQualityThreshold,
            SecondOpinionMode = request.SecondOpinionMode,
            SecondOpinionOutlierDeltaPoints = request.SecondOpinionOutlierDeltaPoints,
            SpeedTargetMs = request.SpeedTargetMs,
            SpeedDecayK = request.SpeedDecayK,
            SpeedDifficultyScaling = request.SpeedDifficultyScaling,
            MaxParallelQuestions = request.MaxParallelQuestions
        };

        var (success, created, errors) = await _scoringProfileService.CreateProfileAsync(profile);
        if (!success)
        {
            return BadRequest(new { errors });
        }

        return Ok(new BenchmarkScoringProfileDto
        {
            Id = created!.Id,
            Name = created.Name,
            IsDefault = created.IsDefault,
            WeightAccuracy = created.WeightAccuracy,
            WeightCompleteness = created.WeightCompleteness,
            WeightConciseness = created.WeightConciseness,
            WeightReadability = created.WeightReadability,
            LevelScoresJson = created.LevelScoresJson,
            CriticalErrorCeiling = created.CriticalErrorCeiling,
            SecondOpinionQualityThreshold = created.SecondOpinionQualityThreshold,
            SecondOpinionMode = created.SecondOpinionMode,
            SecondOpinionOutlierDeltaPoints = created.SecondOpinionOutlierDeltaPoints,
            SpeedTargetMs = created.SpeedTargetMs,
            SpeedDecayK = created.SpeedDecayK,
            SpeedDifficultyScaling = created.SpeedDifficultyScaling,
            MaxParallelQuestions = created.MaxParallelQuestions,
            CreatedAtUtc = created.CreatedAtUtc,
            ModifiedAtUtc = created.ModifiedAtUtc
        });
    }

    [HttpPut("scoring-profiles/{id}")]
    public async Task<IActionResult> UpdateScoringProfile(long id, [FromBody] UpdateBenchmarkScoringProfileRequest request)
    {
        var profile = new BenchmarkScoringProfile
        {
            Id = id,
            Name = request.Name?.Trim() ?? string.Empty,
            IsDefault = request.IsDefault,
            WeightAccuracy = request.WeightAccuracy,
            WeightCompleteness = request.WeightCompleteness,
            WeightConciseness = request.WeightConciseness,
            WeightReadability = request.WeightReadability,
            LevelScoresJson = request.LevelScoresJson,
            CriticalErrorCeiling = request.CriticalErrorCeiling,
            SecondOpinionQualityThreshold = request.SecondOpinionQualityThreshold,
            SecondOpinionMode = request.SecondOpinionMode,
            SecondOpinionOutlierDeltaPoints = request.SecondOpinionOutlierDeltaPoints,
            SpeedTargetMs = request.SpeedTargetMs,
            SpeedDecayK = request.SpeedDecayK,
            SpeedDifficultyScaling = request.SpeedDifficultyScaling,
            MaxParallelQuestions = request.MaxParallelQuestions
        };

        var (success, updated, errors) = await _scoringProfileService.UpdateProfileAsync(profile);
        if (!success)
        {
            return BadRequest(new { errors });
        }

        return Ok(new BenchmarkScoringProfileDto
        {
            Id = updated!.Id,
            Name = updated.Name,
            IsDefault = updated.IsDefault,
            WeightAccuracy = updated.WeightAccuracy,
            WeightCompleteness = updated.WeightCompleteness,
            WeightConciseness = updated.WeightConciseness,
            WeightReadability = updated.WeightReadability,
            LevelScoresJson = updated.LevelScoresJson,
            CriticalErrorCeiling = updated.CriticalErrorCeiling,
            SecondOpinionQualityThreshold = updated.SecondOpinionQualityThreshold,
            SecondOpinionMode = updated.SecondOpinionMode,
            SecondOpinionOutlierDeltaPoints = updated.SecondOpinionOutlierDeltaPoints,
            SpeedTargetMs = updated.SpeedTargetMs,
            SpeedDecayK = updated.SpeedDecayK,
            SpeedDifficultyScaling = updated.SpeedDifficultyScaling,
            MaxParallelQuestions = updated.MaxParallelQuestions,
            CreatedAtUtc = updated.CreatedAtUtc,
            ModifiedAtUtc = updated.ModifiedAtUtc
        });
    }

    [HttpPost("scoring-profiles/{id}/default")]
    public async Task<IActionResult> SetDefaultScoringProfile(long id)
    {
        var (success, error) = await _scoringProfileService.SetDefaultProfileAsync(id);
        if (!success)
        {
            return BadRequest(error);
        }
        return Ok();
    }

    [HttpDelete("scoring-profiles/{id}")]
    public async Task<IActionResult> DeleteScoringProfile(long id)
    {
        var (success, error) = await _scoringProfileService.DeleteProfileAsync(id);
        if (!success)
        {
            return BadRequest(error);
        }
        return Ok();
    }

    // --- Difficulty Rating Actions ---

    [HttpPost("difficulty-assessments")]
    public async Task<IActionResult> StartDifficultyAssessment([FromBody] StartDifficultyAssessmentRequest request)
    {
        var (canSpend, denialReason) = await _complianceGuard.CanSpendAsync();
        if (!canSpend)
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, denialReason);
        }

        var suite = await _dbContext.BenchmarkSuites
            .Include(s => s.Questions)
            .FirstOrDefaultAsync(s => s.Id == request.SuiteId);

        if (suite == null)
        {
            return BadRequest("Benchmark suite not found.");
        }

        var conflict = CheckConflictingBenchmarkJob(suite.Id, "difficulty assessment");
        if (conflict != null) return conflict;

        List<BenchmarkQuestion> targetQuestions;
        string scopeType = "suite";

        if (request.QuestionIds != null && request.QuestionIds.Count > 0)
        {
            scopeType = "questions";
            var suiteQuestionIds = suite.Questions.Select(q => q.Id).ToHashSet();
            foreach (var qId in request.QuestionIds)
            {
                if (!suiteQuestionIds.Contains(qId))
                {
                    return BadRequest($"Question ID {qId} does not belong to suite {request.SuiteId}.");
                }
            }

            var requestIdsSet = request.QuestionIds.ToHashSet();
            targetQuestions = suite.Questions
                .Where(q => requestIdsSet.Contains(q.Id))
                .OrderBy(q => q.OrderIndex)
                .ToList();
        }
        else
        {
            targetQuestions = suite.Questions
                .OrderBy(q => q.OrderIndex)
                .ToList();
        }

        if (targetQuestions.Count == 0)
        {
            return BadRequest("No questions found to assess.");
        }

        var assessorConfig = await _dbContext.SystemAiApiConfigurations.FindAsync(request.AssessorModelConfigurationId);
        if (assessorConfig == null)
        {
            return BadRequest("Assessor model configuration not found.");
        }
        if (!assessorConfig.IsEnabled)
        {
            return BadRequest("The assessor configuration is disabled.");
        }
        if (string.IsNullOrWhiteSpace(assessorConfig.EncryptedApiKey))
        {
            return BadRequest("The assessor configuration has no API key.");
        }
        if ((assessorConfig.ModelRole & 4) != 4)
        {
            return BadRequest("The selected assessor configuration does not have the Benchmark role enabled.");
        }

        string startedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

        var cts = new CancellationTokenSource();
        var job = new BenchmarkDifficultyJob
        {
            SuiteId = suite.Id,
            SuiteName = suite.Name,
            Scope = scopeType,
            AssessorConfigId = assessorConfig.Id,
            AssessorDisplayName = assessorConfig.DisplayName,
            StartedByUserId = string.IsNullOrEmpty(startedByUserId) ? null : startedByUserId,
            Cts = cts,
            Items = targetQuestions.Select(q => new BenchmarkDifficultyJobItem
            {
                QuestionId = q.Id,
                OrderIndex = q.OrderIndex,
                QuestionTextExcerpt = q.QuestionText.Length <= 160 ? q.QuestionText : q.QuestionText.Substring(0, 160) + "...",
                Status = BenchmarkDifficultyItemStatus.Pending
            }).ToList()
        };

        if (!_difficultyJobManager.TryStart(job, out var existingJob))
        {
            return StatusCode(StatusCodes.Status409Conflict, existingJob?.ToDto());
        }

        _ = Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            var svc = scope.ServiceProvider.GetRequiredService<BenchmarkService>();
            await svc.RunDifficultyAssessmentAsync(job.Id, cts.Token);
        });

        return Accepted(new { jobId = job.Id });
    }

    [HttpGet("difficulty-assessments/{jobId}")]
    public IActionResult GetDifficultyAssessment(string jobId)
    {
        var job = _difficultyJobManager.TryGet(jobId);
        if (job == null)
        {
            return NotFound();
        }
        return Ok(job.ToDto());
    }

    [HttpGet("difficulty-assessments/active")]
    public IActionResult GetActiveDifficultyAssessment()
    {
        var current = _difficultyJobManager.Current;
        if (current == null || current.Status != BenchmarkDifficultyJobStatus.Running)
        {
            return NoContent();
        }
        return Ok(current.ToDto());
    }

    [HttpPost("difficulty-assessments/{jobId}/cancel")]
    public IActionResult CancelDifficultyAssessment(string jobId)
    {
        var job = _difficultyJobManager.TryGet(jobId);
        if (job == null)
        {
            return NotFound();
        }

        bool cancelled = _difficultyJobManager.TryCancel(jobId);
        return Ok(new { cancelled });
    }

    // --- Suites CRUD ---

    [HttpGet("suites")]
    public async Task<IActionResult> GetSuites()
    {
        if (!await _dbContext.BenchmarkSuites.AnyAsync())
        {
            await EnsureDefaultSuiteInternalAsync();
        }

        var suites = await _dbContext.BenchmarkSuites
            .Include(s => s.Questions)
            .Include(s => s.GameSnapshot)
            .OrderBy(s => s.Name)
            .Select(s => ToSuiteDto(s))
            .ToListAsync();

        return Ok(suites);
    }

    [HttpPost("suites")]
    public async Task<IActionResult> CreateSuite([FromBody] CreateBenchmarkSuiteRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest("Suite name is required.");
        }

        if (await _dbContext.BenchmarkSuites.AnyAsync(s => s.Name == request.Name.Trim()))
        {
            return BadRequest("A suite with this name already exists.");
        }

        var suite = new BenchmarkSuite
        {
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            CreatedAtUtc = DateTime.UtcNow
        };

        _dbContext.BenchmarkSuites.Add(suite);
        await _dbContext.SaveChangesAsync();

        return Ok(ToSuiteDto(suite));
    }

    [HttpPut("suites/{id}")]
    public async Task<IActionResult> UpdateSuite(long id, [FromBody] UpdateBenchmarkSuiteRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest("Suite name is required.");
        }

        var suite = await _dbContext.BenchmarkSuites.FindAsync(id);
        if (suite == null) return NotFound();

        if (await _dbContext.BenchmarkSuites.AnyAsync(s => s.Name == request.Name.Trim() && s.Id != id))
        {
            return BadRequest("Another suite with this name already exists.");
        }

        suite.Name = request.Name.Trim();
        suite.Description = request.Description?.Trim();
        suite.ModifiedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();
        return Ok();
    }

    [HttpDelete("suites/{id}")]
    public async Task<IActionResult> DeleteSuite(long id)
    {
        var suite = await _dbContext.BenchmarkSuites.FindAsync(id);
        if (suite != null)
        {
            _dbContext.BenchmarkSuites.Remove(suite);
            await _dbContext.SaveChangesAsync();
        }
        return Ok();
    }

    [HttpPost("suites/{id}/duplicate")]
    public async Task<IActionResult> DuplicateSuite(long id)
    {
        var suite = await _dbContext.BenchmarkSuites
            .Include(s => s.Questions)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (suite == null) return NotFound();

        var (canAdd, addDenial) = _complianceGuard.CanAddQuestions(0, suite.Questions.Count);
        if (!canAdd)
        {
            return BadRequest($"Cannot duplicate suite: question count ({suite.Questions.Count}) exceeds maximum allowed ({_complianceGuard.MaxQuestionsPerSuite}).");
        }

        string baseName = suite.Name + " (Copy)";
        string newName = baseName;
        int copyCounter = 1;
        while (await _dbContext.BenchmarkSuites.AnyAsync(s => s.Name == newName))
        {
            copyCounter++;
            newName = $"{baseName} {copyCounter}";
        }

        var newSuite = new BenchmarkSuite
        {
            Name = newName,
            Description = suite.Description,
            CreatedAtUtc = DateTime.UtcNow
        };

        foreach (var q in suite.Questions.OrderBy(q => q.OrderIndex))
        {
            newSuite.Questions.Add(new BenchmarkQuestion
            {
                OrderIndex = q.OrderIndex,
                QuestionText = q.QuestionText,
                Difficulty = q.Difficulty,
                ExpectedPoints = q.ExpectedPoints,
                AssessedDifficulty = q.AssessedDifficulty,
                AssessedDifficultyModel = q.AssessedDifficultyModel,
                AssessedDifficultyAtUtc = q.AssessedDifficultyAtUtc,
                AssessedDifficultyModelConfigurationId = q.AssessedDifficultyModelConfigurationId,
                AssessedDifficultyProviderUsed = q.AssessedDifficultyProviderUsed,
                AssessedDifficultyModelIdUsed = q.AssessedDifficultyModelIdUsed,
                AssessedDifficultyThinkingLevelUsed = q.AssessedDifficultyThinkingLevelUsed,
                AssessedDifficultyReasoningModeUsed = q.AssessedDifficultyReasoningModeUsed,
                AssessedDifficultyReasoningSummaryUsed = q.AssessedDifficultyReasoningSummaryUsed,
                AssessedDifficultyServiceTierUsed = q.AssessedDifficultyServiceTierUsed,
                AssessedDifficultyMaxOutputTokensUsed = q.AssessedDifficultyMaxOutputTokensUsed,
                CreatedAtUtc = DateTime.UtcNow,
                ModifiedAtUtc = DateTime.UtcNow
            });
        }

        _dbContext.BenchmarkSuites.Add(newSuite);
        await _dbContext.SaveChangesAsync();

        return Ok(new BenchmarkSuiteDto
        {
            Id = newSuite.Id,
            Name = newSuite.Name,
            Description = newSuite.Description,
            CreatedAtUtc = newSuite.CreatedAtUtc,
            ModifiedAtUtc = newSuite.ModifiedAtUtc,
            QuestionCount = newSuite.Questions.Count
        });
    }

    [HttpPost("suites/import-default")]
    public async Task<IActionResult> ImportDefaultSuite()
    {
        var suite = await EnsureDefaultSuiteInternalAsync(forceNewCopy: true);
        if (suite == null)
        {
            return BadRequest("Default suite file not found.");
        }

        return Ok(ToSuiteDto(suite));
    }

    private async Task<BenchmarkSuite?> EnsureDefaultSuiteInternalAsync(bool forceNewCopy = false)
    {
        var defaultPath = Path.Combine(AppContext.BaseDirectory, "Data", "BenchmarkDefaultSuite.json");
        if (!System.IO.File.Exists(defaultPath))
        {
            defaultPath = Path.Combine(Directory.GetCurrentDirectory(), "Data", "BenchmarkDefaultSuite.json");
        }

        if (!System.IO.File.Exists(defaultPath))
        {
            return null;
        }

        var json = await System.IO.File.ReadAllTextAsync(defaultPath);
        var defaultDoc = JsonDocument.Parse(json);
        var root = defaultDoc.RootElement;

        string suiteName = root.TryGetProperty("name", out var nProp) ? nProp.GetString() ?? "GnollHack Intelligence Benchmark Suite" : "GnollHack Intelligence Benchmark Suite";
        string description = root.TryGetProperty("description", out var dProp) ? dProp.GetString() ?? "" : "";

        string finalName = suiteName;
        if (forceNewCopy)
        {
            int counter = 1;
            while (await _dbContext.BenchmarkSuites.AnyAsync(s => s.Name == finalName))
            {
                counter++;
                finalName = $"{suiteName} ({counter})";
            }
        }
        else if (await _dbContext.BenchmarkSuites.AnyAsync(s => s.Name == finalName))
        {
            return await _dbContext.BenchmarkSuites.Include(s => s.Questions).FirstOrDefaultAsync(s => s.Name == finalName);
        }

        var suite = new BenchmarkSuite
        {
            Name = finalName,
            Description = description,
            CreatedAtUtc = DateTime.UtcNow
        };

        if (root.TryGetProperty("questions", out var qArray) && qArray.ValueKind == JsonValueKind.Array)
        {
            int qCount = qArray.GetArrayLength();
            var (canAddDefault, _) = _complianceGuard.CanAddQuestions(0, qCount);
            if (!canAddDefault)
            {
                return null;
            }

            int order = 1;
            foreach (var qEl in qArray.EnumerateArray())
            {
                string text = qEl.GetProperty("questionText").GetString() ?? "";
                string diffStr = qEl.TryGetProperty("difficulty", out var diffProp) ? diffProp.GetString() ?? "Simple" : "Simple";
                var difficulty = Enum.TryParse<BenchmarkDifficulty>(diffStr, true, out var parsedDiff) ? parsedDiff : BenchmarkDifficulty.Simple;
                string? exp = qEl.TryGetProperty("expectedPoints", out var epProp) ? epProp.GetString() : null;

                suite.Questions.Add(new BenchmarkQuestion
                {
                    OrderIndex = order++,
                    QuestionText = text,
                    Difficulty = difficulty,
                    ExpectedPoints = exp,
                    CreatedAtUtc = DateTime.UtcNow,
                    ModifiedAtUtc = DateTime.UtcNow
                });
            }
        }

        _dbContext.BenchmarkSuites.Add(suite);
        await _dbContext.SaveChangesAsync();
        return suite;
    }

    // --- Questions CRUD ---

    // --- Suite health ---
    //
    // Four read-only reports. None of them writes a question, a rubric, or a difficulty rating,
    // and there is deliberately no endpoint that would: the panel's only action is "open this
    // question for editing", and a human decides what to change.

    /// <summary>
    /// Per-item statistics over the suite's stored runs. Pure arithmetic — no AI calls, no spend
    /// gate — but every figure is advisory: read the sample size and the two confound counts
    /// before the numbers.
    /// </summary>
    [HttpGet("suites/{suiteId}/item-analysis")]
    public async Task<IActionResult> GetItemAnalysis(long suiteId)
    {
        var suite = await _dbContext.BenchmarkSuites
            .Include(s => s.Questions)
            .FirstOrDefaultAsync(s => s.Id == suiteId);

        if (suite == null) return NotFound();

        var runs = await _dbContext.BenchmarkRuns
            .Where(r => r.BenchmarkSuiteId == suiteId)
            .Include(r => r.Answers)
            .AsNoTracking()
            .ToListAsync();

        var analysis = BenchmarkItemAnalysis.Compute(
            suite,
            suite.Questions.OrderBy(q => q.OrderIndex).ToList(),
            runs);

        return Ok(new BenchmarkSuiteItemAnalysisDto
        {
            SuiteId = analysis.SuiteId,
            SuiteName = analysis.SuiteName,
            QuestionCount = analysis.QuestionCount,
            RunCount = analysis.RunCount,
            DistinctModelCount = analysis.DistinctModelCount,
            DistinctAssessorCount = analysis.DistinctAssessorCount,
            DistinctScoringMethodVersionCount = analysis.DistinctScoringMethodVersionCount,
            LinkedAnswerCount = analysis.LinkedAnswerCount,
            UnlinkedAnswerCount = analysis.UnlinkedAnswerCount,
            MinRunsForMeasurement = BenchmarkItemAnalysis.MinRunsForMeasurement,
            MinRunsForDiscrimination = BenchmarkItemAnalysis.MinRunsForDiscrimination,
            Items = analysis.Items.Select(i => new BenchmarkItemStatisticsDto
            {
                QuestionId = i.QuestionId,
                OrderIndex = i.OrderIndex,
                QuestionText = i.QuestionText,
                AuthoredDifficulty = i.AuthoredDifficulty,
                ItemRevision = i.ItemRevision,
                RunCount = i.RunCount,
                DistinctModelCount = i.DistinctModelCount,
                DistinctAssessorCount = i.DistinctAssessorCount,
                DistinctScoringMethodVersionCount = i.DistinctScoringMethodVersionCount,
                UnknownRevisionCount = i.UnknownRevisionCount,
                MeanQuality = i.MeanQuality,
                MinQuality = i.MinQuality,
                MaxQuality = i.MaxQuality,
                StdDev = i.StdDev,
                EmpiricalDifficulty = i.EmpiricalDifficulty,
                AssessedDifficulty = i.AssessedDifficulty,
                DifficultyDelta = i.DifficultyDelta,
                Discrimination = i.Discrimination,
                MeanToolCalls = i.MeanToolCalls,
                BudgetBoundFraction = i.BudgetBoundFraction,
                Flags = (int)i.Flags,
                FlagNames = i.Flags == BenchmarkItemFlags.None
                    ? new List<string>()
                    : Enum.GetValues<BenchmarkItemFlags>()
                        .Where(f => f != BenchmarkItemFlags.None && i.Flags.HasFlag(f))
                        .Select(f => f.ToString())
                        .ToList(),
                Confounded = i.Confounded,
                InsufficientData = i.InsufficientData
            }).ToList()
        });
    }

    /// <summary>
    /// Clusters the unverified claims the suite's runs accumulated, and says which of them are
    /// evidence about the rubric rather than about one model. No AI calls.
    /// </summary>
    [HttpGet("suites/{suiteId}/rubric-gaps")]
    public async Task<IActionResult> GetRubricGaps(long suiteId)
    {
        var suite = await _dbContext.BenchmarkSuites.FindAsync(suiteId);
        if (suite == null) return NotFound();

        var rows = await _dbContext.BenchmarkRunAnswers
            .Where(a => a.BenchmarkRun.BenchmarkSuiteId == suiteId
                        && a.BenchmarkQuestionId != null
                        && a.UnverifiedClaimsJson != null)
            .Select(a => new
            {
                a.BenchmarkRunId,
                QuestionId = a.BenchmarkQuestionId!.Value,
                a.OrderIndex,
                a.ItemRevisionUsed,
                a.UnverifiedClaimsJson,
                a.ClaimVerificationJson,
                Provider = a.BenchmarkRun.TestedModelProviderUsed,
                ModelId = a.BenchmarkRun.TestedModelIdUsed
            })
            .AsNoTracking()
            .ToListAsync();

        var samples = new List<BenchmarkUnverifiedClaimSample>();
        foreach (var row in rows)
        {
            List<string>? claims;
            try
            {
                claims = JsonSerializer.Deserialize<List<string>>(row.UnverifiedClaimsJson!);
            }
            catch (JsonException)
            {
                // A malformed blob costs one answer's claims, never the report.
                continue;
            }

            Dictionary<string, BenchmarkClaimVerdict>? verificationsByClaim = null;
            if (!string.IsNullOrWhiteSpace(row.ClaimVerificationJson))
            {
                try
                {
                    var verifications = JsonSerializer.Deserialize<List<BenchmarkClaimVerification>>(row.ClaimVerificationJson);
                    if (verifications != null)
                    {
                        verificationsByClaim = new Dictionary<string, BenchmarkClaimVerdict>(StringComparer.Ordinal);
                        foreach (var v in verifications)
                        {
                            if (!string.IsNullOrWhiteSpace(v.Claim))
                            {
                                verificationsByClaim[v.Claim.Trim()] = v.Verdict;
                            }
                        }
                    }
                }
                catch (JsonException)
                {
                    // A malformed blob costs that answer's verdicts, never the report.
                }
            }

            foreach (string claim in claims ?? new List<string>())
            {
                if (string.IsNullOrWhiteSpace(claim)) continue;

                BenchmarkClaimVerdict? verdict = null;
                if (verificationsByClaim != null && verificationsByClaim.TryGetValue(claim.Trim(), out var v))
                {
                    verdict = v;
                }

                samples.Add(new BenchmarkUnverifiedClaimSample
                {
                    QuestionId = row.QuestionId,
                    QuestionOrderIndex = row.OrderIndex,
                    ItemRevisionUsed = row.ItemRevisionUsed,
                    RunId = row.BenchmarkRunId,
                    Provider = row.Provider,
                    ModelId = row.ModelId,
                    Claim = claim,
                    VerificationVerdict = verdict
                });
            }
        }

        var clusters = BenchmarkRubricGapDetector.Detect(samples);

        return Ok(new BenchmarkRubricGapReportDto
        {
            SuiteId = suiteId,
            RunCount = rows.Select(r => r.BenchmarkRunId).Distinct().Count(),
            ClaimCount = samples.Count,
            Clusters = clusters.Select(c => new BenchmarkRubricGapClusterDto
            {
                QuestionId = c.QuestionId,
                QuestionOrderIndex = c.QuestionOrderIndex,
                Claims = c.Claims.ToList(),
                ModelFamilies = c.ModelFamilies.ToList(),
                ModelIds = c.ModelIds.ToList(),
                Occurrences = c.Occurrences,
                Verdict = c.Verdict.ToString()
            }).ToList()
        });
    }

    /// <summary>
    /// Resolves the citations the suite's rubrics carry against the running source and wiki
    /// indexes. No AI calls. A POST rather than a GET because it walks the whole source index,
    /// which is work rather than a lookup.
    /// </summary>
    [HttpPost("suites/{suiteId}/validate-citations")]
    public async Task<IActionResult> ValidateCitations(long suiteId)
    {
        var suite = await _dbContext.BenchmarkSuites
            .Include(s => s.Questions)
            .FirstOrDefaultAsync(s => s.Id == suiteId);

        if (suite == null) return NotFound();

        var results = BenchmarkRubricCitationValidator.Validate(
            suite.Questions,
            path => _sourceCodeService.ListFiles(path, includeNetCode: false)
                .Contains(path, StringComparison.OrdinalIgnoreCase),
            symbol => !_sourceCodeService.FindDefinition(symbol, "any")
                .StartsWith("No definition found", StringComparison.OrdinalIgnoreCase),
            title => _wikiService.GetArticle(title) != null);

        return Ok(new BenchmarkCitationReportDto
        {
            SuiteId = suiteId,
            UnresolvedCount = results.Sum(r => r.UnresolvedCount),
            NotValidatedCount = results.Sum(r => r.NotValidatedCount),

            // An unresolved citation means little while the index is still building, so the
            // report says which of the two situations the reader is looking at.
            SourceIndexReady = _sourceCodeService.IsIndexingComplete,
            Questions = results.Select(r => new BenchmarkQuestionCitationsDto
            {
                QuestionId = r.QuestionId,
                OrderIndex = r.OrderIndex,
                UnresolvedCount = r.UnresolvedCount,
                NotValidatedCount = r.NotValidatedCount,
                HasNoCitations = r.HasNoCitations,
                Citations = r.Citations.Select(c => new BenchmarkCitationDto
                {
                    Kind = c.Kind.ToString(),
                    Value = c.Value,
                    Status = c.Status.ToString(),
                    LineNumber = c.LineNumber
                }).ToList()
            }).ToList()
        });
    }

    /// <summary>
    /// Asks an explicitly selected model which GnollHack subsystems the suite does not test.
    ///
    /// The only AI-using suite-health action, and gated by the spend caps like every other one.
    /// It returns a **read-only report**: nothing is written into the suite, and no endpoint
    /// exists that would write one. The prompt carries question texts only — no rubrics, no
    /// answers, no scores — so the analysis cannot be shaped by which questions any model
    /// happened to do badly on.
    /// </summary>
    [HttpPost("suites/{suiteId}/coverage-analysis")]
    public async Task<IActionResult> AnalyzeCoverage(long suiteId, [FromBody] CoverageAnalysisRequest request)
    {
        var (canSpend, denialReason) = await _complianceGuard.CanSpendAsync();
        if (!canSpend)
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, denialReason);
        }

        var suite = await _dbContext.BenchmarkSuites
            .Include(s => s.Questions)
            .FirstOrDefaultAsync(s => s.Id == suiteId);

        if (suite == null) return NotFound();
        if (suite.Questions.Count == 0) return BadRequest("Benchmark suite has no questions.");

        var config = await _dbContext.SystemAiApiConfigurations.FindAsync(request.AnalysisModelConfigurationId);
        if (config == null || !config.IsEnabled || string.IsNullOrWhiteSpace(config.EncryptedApiKey) || (config.ModelRole & 4) != 4)
        {
            return BadRequest("The selected analysis model is invalid, disabled, missing an API key, or not configured with the Benchmark role.");
        }

        var (result, error, inputTokens, outputTokens, durationMs) =
            await _benchmarkService.RunCoverageAnalysisAsync(suiteId, config.Id, CancellationToken.None);

        return Ok(new BenchmarkCoverageReportDto
        {
            SuiteId = suiteId,
            SuiteName = suite.Name,
            QuestionCount = suite.Questions.Count,

            // Disclosed on the report, exactly as a difficulty rating discloses its assessor.
            // Not snapshotted onto the suite, because the report itself is not persisted: keeping
            // a stale record of who analysed coverage would outlive the analysis it describes.
            AnalysisModelConfigurationId = config.Id,
            AnalysisModelDisplayNameUsed = config.DisplayName ?? config.ModelId,
            AnalysisModelProviderUsed = config.Provider,
            AnalysisModelIdUsed = config.ModelId,
            AnalysisModelThinkingLevelUsed = config.ThinkingLevel,
            AnalyzedAtUtc = DateTime.UtcNow,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            DurationMs = durationMs,
            ErrorMessage = error,
            Comment = result?.Comment,
            Gaps = (result?.Gaps ?? new List<BenchmarkCoverageGap>()).Select(g => new BenchmarkCoverageGapDto
            {
                Subsystem = g.Subsystem ?? string.Empty,
                SourceLocation = g.SourceLocation ?? string.Empty,
                Rationale = g.Rationale,
                SuggestedBand = g.SuggestedBand
            }).ToList()
        });
    }

    [HttpGet("suites/{suiteId}/questions")]
    public async Task<IActionResult> GetQuestions(long suiteId)
    {
        var questions = await _dbContext.BenchmarkQuestions
            .Where(q => q.BenchmarkSuiteId == suiteId)
            .OrderBy(q => q.OrderIndex)
            .Select(q => ToQuestionDto(q))
            .ToListAsync();

        return Ok(questions);
    }

    [HttpPost("suites/{suiteId}/questions")]
    public async Task<IActionResult> CreateQuestion(long suiteId, [FromBody] CreateBenchmarkQuestionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.QuestionText))
        {
            return BadRequest("Question text is required.");
        }

        var suite = await _dbContext.BenchmarkSuites.FindAsync(suiteId);
        if (suite == null) return NotFound();

        var (canAdd, addDenial) = await _complianceGuard.CanAddQuestionsAsync(suiteId, 1);
        if (!canAdd)
        {
            return BadRequest(addDenial);
        }

        var maxOrder = await _dbContext.BenchmarkQuestions
            .Where(q => q.BenchmarkSuiteId == suiteId)
            .Select(q => (int?)q.OrderIndex)
            .MaxAsync() ?? 0;

        var question = new BenchmarkQuestion
        {
            BenchmarkSuiteId = suiteId,
            OrderIndex = maxOrder + 1,
            QuestionText = request.QuestionText.Trim(),
            Difficulty = request.Difficulty,
            ExpectedPoints = request.ExpectedPoints?.Trim(),
            CreatedAtUtc = DateTime.UtcNow,
            ModifiedAtUtc = DateTime.UtcNow
        };

        suite.ModifiedAtUtc = DateTime.UtcNow;
        _dbContext.BenchmarkQuestions.Add(question);
        await _dbContext.SaveChangesAsync();

        return Ok(ToQuestionDto(question));
    }

    [HttpPut("questions/{id}")]
    public async Task<IActionResult> UpdateQuestion(long id, [FromBody] UpdateBenchmarkQuestionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.QuestionText))
        {
            return BadRequest("Question text is required.");
        }

        var question = await _dbContext.BenchmarkQuestions.FindAsync(id);
        if (question == null) return NotFound();

        string newText = request.QuestionText.Trim();
        string? newPoints = request.ExpectedPoints?.Trim();

        bool contentChanged = question.QuestionText != newText
            || question.Difficulty != request.Difficulty
            || question.ExpectedPoints != newPoints;

        question.QuestionText = newText;
        question.Difficulty = request.Difficulty;
        question.ExpectedPoints = newPoints;

        if (contentChanged)
        {
            BenchmarkQuestionAssessment.Clear(question);
            question.ModifiedAtUtc = DateTime.UtcNow;
            var suite = await _dbContext.BenchmarkSuites.FindAsync(question.BenchmarkSuiteId);
            if (suite != null) suite.ModifiedAtUtc = DateTime.UtcNow;
        }

        await _dbContext.SaveChangesAsync();
        return Ok(ToQuestionDto(question));
    }

    [HttpDelete("questions/{id}")]
    public async Task<IActionResult> DeleteQuestion(long id)
    {
        var question = await _dbContext.BenchmarkQuestions.FindAsync(id);
        if (question != null)
        {
            long suiteId = question.BenchmarkSuiteId;
            _dbContext.BenchmarkQuestions.Remove(question);
            await _dbContext.SaveChangesAsync();

            var remaining = await _dbContext.BenchmarkQuestions
                .Where(q => q.BenchmarkSuiteId == suiteId)
                .OrderBy(q => q.OrderIndex)
                .ToListAsync();

            for (int i = 0; i < remaining.Count; i++)
            {
                remaining[i].OrderIndex = i + 1;
            }

            var suite = await _dbContext.BenchmarkSuites.FindAsync(suiteId);
            if (suite != null) suite.ModifiedAtUtc = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync();
        }
        return Ok();
    }

    [HttpPut("suites/{suiteId}/questions/reorder")]
    public async Task<IActionResult> ReorderQuestions(long suiteId, [FromBody] ReorderRequest request)
    {
        var questions = await _dbContext.BenchmarkQuestions
            .Where(q => q.BenchmarkSuiteId == suiteId)
            .ToListAsync();

        var dict = questions.ToDictionary(q => q.Id);
        for (int i = 0; i < request.OrderedIds.Length; i++)
        {
            if (dict.TryGetValue(request.OrderedIds[i], out var q))
            {
                q.OrderIndex = i + 1;
            }
        }

        var suite = await _dbContext.BenchmarkSuites.FindAsync(suiteId);
        if (suite != null) suite.ModifiedAtUtc = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();
        return Ok();
    }

    // --- Question Review API ---

    [HttpPost("questions/{id}/review")]
    public async Task<IActionResult> ReviewQuestion(long id, [FromBody] ReviewBenchmarkQuestionRequest? request, CancellationToken ct)
    {
        var question = await _dbContext.BenchmarkQuestions.FirstOrDefaultAsync(q => q.Id == id, ct);
        if (question == null) return NotFound();

        bool markReviewed = request?.Reviewed ?? true;
        if (markReviewed)
        {
            question.ReviewedAtRevision = question.ItemRevision;
            question.ReviewedAtUtc = DateTime.UtcNow;
            question.ReviewedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        }
        else
        {
            question.ReviewedAtRevision = null;
            question.ReviewedAtUtc = null;
            question.ReviewedByUserId = null;
        }

        question.ModifiedAtUtc = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(ct);

        return Ok(ToQuestionDto(question));
    }

    [HttpPost("suites/{id}/review-all")]
    public async Task<IActionResult> ReviewAllQuestions(long id, CancellationToken ct)
    {
        var suite = await _dbContext.BenchmarkSuites
            .Include(s => s.Questions)
            .Include(s => s.GameSnapshot)
            .FirstOrDefaultAsync(s => s.Id == id, ct);
        if (suite == null) return NotFound();

        string userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        int count = 0;
        foreach (var q in suite.Questions.Where(q => q.IsGenerated && (q.ReviewedAtRevision == null || q.ReviewedAtRevision != q.ItemRevision)))
        {
            q.ReviewedAtRevision = q.ItemRevision;
            q.ReviewedAtUtc = DateTime.UtcNow;
            q.ReviewedByUserId = userId;
            q.ModifiedAtUtc = DateTime.UtcNow;
            count++;
        }

        await _dbContext.SaveChangesAsync(ct);
        return Ok(new { reviewedCount = count, suite = ToSuiteDto(suite) });
    }

    // --- Benchmark Game Snapshots API ---

    [HttpPost("snapshots/capture")]
    public async Task<IActionResult> CaptureSnapshot([FromBody] CaptureBenchmarkSnapshotRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { error = "Board name is required." });
        }

        var session = await _dbContext.ChatSession.FirstOrDefaultAsync(s => s.Id == request.SessionId, ct);
        if (session == null)
        {
            return NotFound(new { error = "Session not found." });
        }

        string userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        if (session.AspNetUserId != userId)
        {
            return Forbid();
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linkedCts.CancelAfter(TimeSpan.FromSeconds(45));

        var emptyParams = JsonDocument.Parse("{}").RootElement;
        var toolResult = await _clientToolBridge.SendToolRequestAsync(session.Id, "refresh_snapshot", emptyParams, linkedCts.Token);
        if (!toolResult.Success)
        {
            string msg = toolResult.ErrorMessage ?? toolResult.Content ?? "Client tool request failed.";
            return Conflict(new { error = msg });
        }

        string snapshotText = toolResult.Content ?? string.Empty;
        if (snapshotText.Length > 60200)
        {
            snapshotText = snapshotText.Substring(0, 60200);
        }

        var meta = new BoardMetadata(
            request.Name.Trim(),
            request.Notes?.Trim(),
            request.SourceGnollHackVersion?.Trim(),
            DateTime.UtcNow);

        try
        {
            var (board, suite) = await _snapshotImporter.FromClientTextAsync(snapshotText, meta, ct);
            return Ok(new CaptureBenchmarkSnapshotResponse
            {
                Board = ToSnapshotDto(board, suite.Id, suite.Name),
                Suite = ToSuiteDto(suite)
            });
        }
        catch (DuplicateBoardNameException ex)
        {
            return Conflict(new { error = ex.Message, existingBoardId = ex.ExistingBoardId });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("snapshots")]
    public async Task<IActionResult> UploadSnapshot([FromBody] UploadBenchmarkSnapshotRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { error = "Board name is required." });
        }
        if (string.IsNullOrWhiteSpace(request.Html))
        {
            return BadRequest(new { error = "HTML content is required." });
        }

        var meta = new BoardMetadata(
            request.Name.Trim(),
            request.Notes?.Trim(),
            request.SourceGnollHackVersion?.Trim(),
            DateTime.UtcNow);

        try
        {
            var (board, suite) = await _snapshotImporter.FromRawHtmlAsync(request.Html, meta, ct);
            return Ok(new CaptureBenchmarkSnapshotResponse
            {
                Board = ToSnapshotDto(board, suite.Id, suite.Name),
                Suite = ToSuiteDto(suite)
            });
        }
        catch (DuplicateBoardNameException ex)
        {
            return Conflict(new { error = ex.Message, existingBoardId = ex.ExistingBoardId });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("snapshots")]
    public async Task<IActionResult> GetSnapshots(CancellationToken ct)
    {
        var boards = await _dbContext.BenchmarkGameSnapshots
            .OrderByDescending(s => s.CreatedAtUtc)
            .Select(s => new BenchmarkGameSnapshotDto
            {
                Id = s.Id,
                Name = s.Name,
                DigestText = s.DigestText,
                CharCount = s.CharCount,
                Sha256 = s.Sha256,
                CaptureMethod = s.CaptureMethod,
                SourceGnollHackVersion = s.SourceGnollHackVersion,
                Notes = s.Notes,
                CapturedAtUtc = s.CapturedAtUtc,
                CreatedAtUtc = s.CreatedAtUtc,
                ModifiedAtUtc = s.ModifiedAtUtc
            })
            .ToListAsync(ct);

        var suiteMap = await _dbContext.BenchmarkSuites
            .Where(s => s.GameSnapshotId != null)
            .Select(s => new { s.GameSnapshotId, s.Id, s.Name })
            .ToDictionaryAsync(s => s.GameSnapshotId!.Value, s => new { s.Id, s.Name }, ct);

        foreach (var b in boards)
        {
            if (suiteMap.TryGetValue(b.Id, out var sw))
            {
                b.SuiteId = sw.Id;
                b.SuiteName = sw.Name;
            }
        }

        return Ok(boards);
    }

    [HttpGet("snapshots/{id}")]
    public async Task<IActionResult> GetSnapshot(long id, [FromQuery] bool includeText = false, CancellationToken ct = default)
    {
        var board = await _dbContext.BenchmarkGameSnapshots.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (board == null) return NotFound();

        var suite = await _dbContext.BenchmarkSuites.FirstOrDefaultAsync(s => s.GameSnapshotId == id, ct);
        var dto = ToSnapshotDto(board, suite?.Id, suite?.Name);
        if (!includeText)
        {
            dto.SanitizedText = null;
        }
        return Ok(dto);
    }

    [HttpGet("snapshots/{id}/text")]
    public async Task<IActionResult> DownloadSnapshotText(long id, CancellationToken ct)
    {
        var board = await _dbContext.BenchmarkGameSnapshots.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (board == null) return NotFound();

        string safeName = string.Join("_", board.Name.Split(Path.GetInvalidFileNameChars()));
        return File(
            System.Text.Encoding.UTF8.GetBytes(board.SanitizedText),
            "text/plain; charset=utf-8",
            $"{safeName}.snapshot.txt");
    }

    [HttpPut("snapshots/{id}")]
    public async Task<IActionResult> UpdateSnapshot(long id, [FromBody] UpdateBenchmarkGameSnapshotRequest request, CancellationToken ct)
    {
        var board = await _dbContext.BenchmarkGameSnapshots.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (board == null) return NotFound();

        if (!string.IsNullOrWhiteSpace(request.Name) && request.Name.Trim() != board.Name)
        {
            string newName = request.Name.Trim();
            bool nameExists = await _dbContext.BenchmarkGameSnapshots.AnyAsync(s => s.Name == newName && s.Id != id, ct);
            if (nameExists)
            {
                return Conflict(new { error = $"A benchmark snapshot named '{newName}' already exists." });
            }
            board.Name = newName;
        }

        if (request.Notes != null)
        {
            board.Notes = request.Notes.Trim();
        }

        if (request.DigestText != null)
        {
            board.DigestText = request.DigestText.Trim().Length > 2000
                ? request.DigestText.Trim()[..2000]
                : request.DigestText.Trim();
        }

        if (request.SourceGnollHackVersion != null)
        {
            board.SourceGnollHackVersion = request.SourceGnollHackVersion.Trim();
        }

        board.ModifiedAtUtc = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(ct);

        var suite = await _dbContext.BenchmarkSuites.FirstOrDefaultAsync(s => s.GameSnapshotId == id, ct);
        return Ok(ToSnapshotDto(board, suite?.Id, suite?.Name));
    }

    [HttpDelete("snapshots/{id}")]
    public async Task<IActionResult> DeleteSnapshot(long id, CancellationToken ct)
    {
        var board = await _dbContext.BenchmarkGameSnapshots.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (board == null) return NotFound();

        var suite = await _dbContext.BenchmarkSuites.FirstOrDefaultAsync(s => s.GameSnapshotId == id, ct);
        if (suite != null)
        {
            suite.GameSnapshotId = null;
            suite.ModifiedAtUtc = DateTime.UtcNow;
        }

        _dbContext.BenchmarkGameSnapshots.Remove(board);
        await _dbContext.SaveChangesAsync(ct);

        return NoContent();
    }

    // --- Question Generation Jobs API ---

    [HttpPost("question-generations")]
    public async Task<IActionResult> StartQuestionGeneration([FromBody] StartQuestionGenerationRequest request, CancellationToken ct)
    {
        if (request.SimpleCount <= 0 && request.IntermediateCount <= 0 && request.AdvancedCount <= 0)
        {
            return BadRequest(new { error = "At least one question band must have count greater than zero." });
        }

        var suite = await _dbContext.BenchmarkSuites
            .Include(s => s.GameSnapshot)
            .FirstOrDefaultAsync(s => s.Id == request.SuiteId, ct);
        if (suite == null) return NotFound(new { error = "Suite not found." });
        if (suite.GameSnapshot == null)
        {
            return BadRequest(new { error = "The suite does not have a game board bound to it. Question generation requires a game board." });
        }

        var conflict = CheckConflictingBenchmarkJob(suite.Id, "question generation");
        if (conflict != null) return conflict;

        int totalToGenerate = request.SimpleCount + request.IntermediateCount + request.AdvancedCount;
        var (canAdd, complianceMsg) = await _complianceGuard.CanAddQuestionsAsync(suite.Id, totalToGenerate);
        if (!canAdd)
        {
            return BadRequest(new { error = complianceMsg });
        }

        var generatorConfig = await _dbContext.SystemAiApiConfigurations.FindAsync(new object[] { request.GeneratorModelConfigurationId }, ct);
        if (generatorConfig == null || !generatorConfig.IsEnabled)
        {
            return BadRequest(new { error = "Generator model configuration not found or disabled." });
        }
        if (string.IsNullOrWhiteSpace(generatorConfig.EncryptedApiKey))
        {
            return BadRequest(new { error = "Generator model configuration has no API key." });
        }

        string startedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var cts = new CancellationTokenSource();

        var job = new BenchmarkGenerationJob
        {
            SuiteId = suite.Id,
            SuiteName = suite.Name,
            GeneratorConfigId = generatorConfig.Id,
            GeneratorDisplayName = generatorConfig.DisplayName,
            Instructions = request.Instructions?.Trim() ?? string.Empty,
            StartedByUserId = string.IsNullOrEmpty(startedByUserId) ? null : startedByUserId,
            Cts = cts,
            Items = new List<BenchmarkGenerationJobItem>
            {
                new() { Difficulty = BenchmarkDifficulty.Simple, RequestedCount = request.SimpleCount, Status = request.SimpleCount > 0 ? BenchmarkGenerationItemStatus.Pending : BenchmarkGenerationItemStatus.Skipped },
                new() { Difficulty = BenchmarkDifficulty.Intermediate, RequestedCount = request.IntermediateCount, Status = request.IntermediateCount > 0 ? BenchmarkGenerationItemStatus.Pending : BenchmarkGenerationItemStatus.Skipped },
                new() { Difficulty = BenchmarkDifficulty.Advanced, RequestedCount = request.AdvancedCount, Status = request.AdvancedCount > 0 ? BenchmarkGenerationItemStatus.Pending : BenchmarkGenerationItemStatus.Skipped }
            }
        };

        if (!_generationJobManager.TryStart(job, out var existingJob))
        {
            return StatusCode(StatusCodes.Status409Conflict, existingJob?.ToDto());
        }

        _ = Task.Run(async () =>
        {
            await _generationService.RunGenerationAsync(job.Id, cts.Token);
        });

        return Accepted(new { jobId = job.Id });
    }

    [HttpGet("question-generations/{jobId}")]
    public IActionResult GetQuestionGeneration(string jobId)
    {
        var job = _generationJobManager.TryGet(jobId);
        if (job == null) return NotFound();
        return Ok(job.ToDto());
    }

    [HttpGet("question-generations/active")]
    public IActionResult GetActiveQuestionGeneration()
    {
        var current = _generationJobManager.Current;
        if (current == null || current.Status != BenchmarkGenerationJobStatus.Running)
        {
            return NoContent();
        }
        return Ok(current.ToDto());
    }

    [HttpPost("question-generations/{jobId}/cancel")]
    public IActionResult CancelQuestionGeneration(string jobId)
    {
        var job = _generationJobManager.TryGet(jobId);
        if (job == null) return NotFound();
        bool cancelled = _generationJobManager.TryCancel(jobId);
        return Ok(new { cancelled });
    }

    // --- Rubric Verification Jobs API ---

    [HttpPost("rubric-checks")]
    public async Task<IActionResult> StartRubricCheck([FromBody] StartRubricCheckRequest request, CancellationToken ct)
    {
        var suite = await _dbContext.BenchmarkSuites
            .Include(s => s.GameSnapshot)
            .Include(s => s.Questions)
            .FirstOrDefaultAsync(s => s.Id == request.SuiteId, ct);
        if (suite == null) return NotFound(new { error = "Suite not found." });
        if (suite.GameSnapshot == null)
        {
            return BadRequest(new { error = "The suite does not have a game board bound to it. Rubric verification requires a game board." });
        }

        var conflict = CheckConflictingBenchmarkJob(suite.Id, "rubric verification");
        if (conflict != null) return conflict;

        var checkerConfig = await _dbContext.SystemAiApiConfigurations.FindAsync(new object[] { request.CheckerModelConfigurationId }, ct);
        if (checkerConfig == null || !checkerConfig.IsEnabled)
        {
            return BadRequest(new { error = "Checker model configuration not found or disabled." });
        }
        if (string.IsNullOrWhiteSpace(checkerConfig.EncryptedApiKey))
        {
            return BadRequest(new { error = "Checker model configuration has no API key." });
        }

        List<BenchmarkQuestion> targetQuestions;
        string scopeType = "suite";
        if (request.QuestionIds != null && request.QuestionIds.Count > 0)
        {
            scopeType = "questions";
            var idSet = new HashSet<long>(request.QuestionIds);
            targetQuestions = suite.Questions.Where(q => idSet.Contains(q.Id)).OrderBy(q => q.OrderIndex).ToList();
        }
        else
        {
            targetQuestions = suite.Questions.OrderBy(q => q.OrderIndex).ToList();
        }

        if (targetQuestions.Count == 0)
        {
            return BadRequest(new { error = "No questions found to check." });
        }

        string startedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var cts = new CancellationTokenSource();

        var job = new BenchmarkRubricCheckJob
        {
            SuiteId = suite.Id,
            SuiteName = suite.Name,
            Scope = scopeType,
            CheckerConfigId = checkerConfig.Id,
            CheckerDisplayName = checkerConfig.DisplayName,
            StartedByUserId = string.IsNullOrEmpty(startedByUserId) ? null : startedByUserId,
            Cts = cts,
            Items = targetQuestions.Select(q => new BenchmarkRubricCheckJobItem
            {
                QuestionId = q.Id,
                OrderIndex = q.OrderIndex,
                QuestionTextExcerpt = q.QuestionText.Length <= 160 ? q.QuestionText : q.QuestionText.Substring(0, 160) + "...",
                Status = BenchmarkRubricCheckItemStatus.Pending
            }).ToList()
        };

        if (!_rubricCheckJobManager.TryStart(job, out var existingJob))
        {
            return StatusCode(StatusCodes.Status409Conflict, existingJob?.ToDto());
        }

        _ = Task.Run(async () =>
        {
            await _rubricCheckService.RunRubricCheckAsync(job.Id, cts.Token);
        });

        return Accepted(new { jobId = job.Id });
    }

    [HttpGet("rubric-checks/{jobId}")]
    public IActionResult GetRubricCheck(string jobId)
    {
        var job = _rubricCheckJobManager.TryGet(jobId);
        if (job == null) return NotFound();
        return Ok(job.ToDto());
    }

    [HttpGet("rubric-checks/active")]
    public IActionResult GetActiveRubricCheck()
    {
        var current = _rubricCheckJobManager.Current;
        if (current == null || current.Status != BenchmarkRubricCheckJobStatus.Running)
        {
            return NoContent();
        }
        return Ok(current.ToDto());
    }

    [HttpPost("rubric-checks/{jobId}/cancel")]
    public IActionResult CancelRubricCheck(string jobId)
    {
        var job = _rubricCheckJobManager.TryGet(jobId);
        if (job == null) return NotFound();
        bool cancelled = _rubricCheckJobManager.TryCancel(jobId);
        return Ok(new { cancelled });
    }

    // --- Runs API ---

    [HttpPost("runs")]
    public async Task<IActionResult> StartRun([FromBody] StartBenchmarkRunRequest request)
    {
        if (_runManager.CurrentRunId.HasValue)
        {
            return Conflict("A benchmark run is already in progress.");
        }

        var (canSpend, denialReason) = await _complianceGuard.CanSpendAsync();
        if (!canSpend)
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, denialReason);
        }

        var suite = await _dbContext.BenchmarkSuites
            .Include(s => s.Questions)
            .FirstOrDefaultAsync(s => s.Id == request.SuiteId);

        if (suite == null) return NotFound("Benchmark suite not found.");
        if (suite.Questions.Count == 0) return BadRequest("Benchmark suite has no questions.");

        int unassessedCount = suite.Questions.Count(q => q.AssessedDifficulty == null);
        if (unassessedCount > 0)
        {
            return BadRequest(
                $"Benchmark suite '{suite.Name}' has {unassessedCount} of {suite.Questions.Count} " +
                "question(s) without an assessed difficulty. Assess question difficulty for the whole " +
                "suite before running a benchmark.");
        }

        var testedConfig = await _dbContext.SystemAiApiConfigurations.FindAsync(request.TestedModelConfigurationId);
        var assessorConfig = await _dbContext.SystemAiApiConfigurations.FindAsync(request.AssessorModelConfigurationId);

        if (testedConfig == null || string.IsNullOrWhiteSpace(testedConfig.EncryptedApiKey) || (testedConfig.ModelRole & 4) != 4)
            return BadRequest("Tested model configuration is invalid, missing an API key, or not configured with the Benchmark role.");

        if (assessorConfig == null || string.IsNullOrWhiteSpace(assessorConfig.EncryptedApiKey) || (assessorConfig.ModelRole & 4) != 4)
            return BadRequest("Assessor model configuration is invalid, missing an API key, or not configured with the Benchmark role.");

        // Optional: a second-opinion assessor re-grades severe verdicts. Held to the same bar as
        // the assessor, and simply absent when the operator did not pick one.
        SystemAiApiConfiguration? secondOpinionConfig = null;
        if (request.SecondOpinionAssessorModelConfigurationId.HasValue)
        {
            secondOpinionConfig = await _dbContext.SystemAiApiConfigurations
                .FindAsync(request.SecondOpinionAssessorModelConfigurationId.Value);

            if (secondOpinionConfig == null || !secondOpinionConfig.IsEnabled ||
                string.IsNullOrWhiteSpace(secondOpinionConfig.EncryptedApiKey) ||
                (secondOpinionConfig.ModelRole & 4) != 4)
            {
                return BadRequest("Second opinion assessor configuration is invalid, disabled, missing an API key, or not configured with the Benchmark role.");
            }
        }

        // Optional: a claim verifier checks unverified claims against source and wiki using read-only tools.
        SystemAiApiConfiguration? claimVerifierConfig = null;
        if (request.ClaimVerifierModelConfigurationId.HasValue)
        {
            claimVerifierConfig = await _dbContext.SystemAiApiConfigurations
                .FindAsync(request.ClaimVerifierModelConfigurationId.Value);

            if (claimVerifierConfig == null || !claimVerifierConfig.IsEnabled ||
                string.IsNullOrWhiteSpace(claimVerifierConfig.EncryptedApiKey) ||
                (claimVerifierConfig.ModelRole & 4) != 4)
            {
                return BadRequest("Claim verifier configuration is invalid, disabled, missing an API key, or not configured with the Benchmark role.");
            }
        }

        // The mode that will actually apply, resolved here rather than in the service: only this
        // method sees the start dialog's override, and only the service sees the profile. An
        // explicit Off drops the second-opinion assessor from the run, because the enum defines
        // the two as the same thing — the mode is inert without an assessor, and an assessor is
        // inert under Off — and because the run column cannot otherwise distinguish "the operator
        // chose Never" from "nothing was stamped yet", which is what the service's own fallback
        // reads a zero as.
        int? requestedMode = request.SecondOpinionMode;
        if (requestedMode.HasValue && !Enum.IsDefined(typeof(BenchmarkSecondOpinionMode), requestedMode.Value))
        {
            return BadRequest("SecondOpinionMode must be Off (0), Flagged (1), FlaggedAndOutliers (2), or All (3).");
        }

        if (requestedMode == (int)BenchmarkSecondOpinionMode.Off)
        {
            secondOpinionConfig = null;
        }

        bool isSameProvider = _complianceGuard.IsSameProvider(testedConfig, assessorConfig);
        if (isSameProvider && !request.AcknowledgeSameProvider)
        {
            return StatusCode(StatusCodes.Status409Conflict, new SameProviderWarningDto
            {
                SameProvider = true,
                Provider = testedConfig.Provider,
                TestedModelDisplayName = testedConfig.DisplayName,
                AssessorModelDisplayName = assessorConfig.DisplayName,
                Message = $"Both the model under test ({testedConfig.DisplayName}) and the assessor model ({assessorConfig.DisplayName}) belong to the same provider ({testedConfig.Provider}). Evaluation of a model by its own provider family may produce biased grading."
            });
        }

        string userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

        var run = new BenchmarkRun
        {
            BenchmarkSuiteId = suite.Id,
            SuiteName = suite.Name,
            TestedModelConfigurationId = testedConfig.Id,
            TestedModelDisplayNameUsed = testedConfig.DisplayName,
            TestedModelProviderUsed = testedConfig.Provider,
            TestedModelIdUsed = testedConfig.ModelId,
            TestedModelThinkingLevelUsed = testedConfig.ThinkingLevel,
            TestedModelReasoningModeUsed = testedConfig.ReasoningMode,
            TestedModelReasoningSummaryUsed = testedConfig.ReasoningSummary,
            TestedModelServiceTierUsed = testedConfig.ServiceTier,
            TestedModelMaxOutputTokensUsed = testedConfig.MaxOutputTokens,
            TestedModelParallelExecutionModeUsed = testedConfig.ParallelExecutionMode,

            AssessorModelConfigurationId = assessorConfig.Id,
            AssessorModelDisplayNameUsed = assessorConfig.DisplayName,
            AssessorModelProviderUsed = assessorConfig.Provider,
            AssessorModelIdUsed = assessorConfig.ModelId,
            AssessorModelThinkingLevelUsed = assessorConfig.ThinkingLevel,
            AssessorModelReasoningModeUsed = assessorConfig.ReasoningMode,

            SecondOpinionAssessorModelConfigurationId = secondOpinionConfig?.Id,
            SecondOpinionAssessorModelDisplayNameUsed = secondOpinionConfig?.DisplayName,
            SecondOpinionAssessorModelProviderUsed = secondOpinionConfig?.Provider,
            SecondOpinionAssessorModelIdUsed = secondOpinionConfig?.ModelId,
            SecondOpinionAssessorModelThinkingLevelUsed = secondOpinionConfig?.ThinkingLevel,
            SecondOpinionAssessorModelReasoningModeUsed = secondOpinionConfig?.ReasoningMode,

            ClaimVerifierModelConfigurationId = claimVerifierConfig?.Id,
            ClaimVerifierDisplayNameUsed = claimVerifierConfig?.DisplayName,
            ClaimVerifierProviderUsed = claimVerifierConfig?.Provider,
            ClaimVerifierModelIdUsed = claimVerifierConfig?.ModelId,
            ClaimVerifierThinkingLevelUsed = claimVerifierConfig?.ThinkingLevel,
            ClaimVerifierReasoningModeUsed = claimVerifierConfig?.ReasoningMode,

            // Left at Off (0) when the operator did not override, so the service stamps the
            // scoring profile's own default at run start.
            SecondOpinionModeUsed = secondOpinionConfig != null && requestedMode.HasValue
                ? requestedMode.Value
                : (int)BenchmarkSecondOpinionMode.Off,

            ScoringProfileId = request.ScoringProfileId,
            StartedByUserId = string.IsNullOrEmpty(userId) ? null : userId,
            Status = BenchmarkRunStatus.Running,
            StartedAtUtc = DateTime.UtcNow,
            TotalQuestionCount = suite.Questions.Count,
            PurposeStatementUsed = _complianceGuard.GetPurposeStatement(),
            SameProviderAcknowledged = isSameProvider && request.AcknowledgeSameProvider
        };

        _dbContext.BenchmarkRuns.Add(run);
        await _dbContext.SaveChangesAsync();

        var cts = new CancellationTokenSource();
        if (!_runManager.TryStart(run.Id, cts, out _))
        {
            return Conflict("A benchmark run is already in progress.");
        }

        _ = Task.Run(() => _benchmarkService.RunAsync(run.Id, cts.Token));

        return Accepted(new { runId = run.Id });
    }

    /// <summary>
    /// The assessor of the most recent completed run of a suite. The start dialog warns when the
    /// selected assessor differs from it, because a suite's runs are only comparable to each
    /// other while the grader is the same one — and the staged assessor migration is precisely a
    /// deliberate change of grader, so the warning fires exactly when it should.
    /// </summary>
    [HttpGet("suites/{suiteId}/last-assessor")]
    public async Task<IActionResult> GetLastAssessor(long suiteId)
    {
        var last = await _dbContext.BenchmarkRuns
            .Where(r => r.BenchmarkSuiteId == suiteId
                        && (r.Status == BenchmarkRunStatus.Completed
                            || r.Status == BenchmarkRunStatus.CompletedWithLimits
                            || r.Status == BenchmarkRunStatus.CompletedWithErrors))
            .OrderByDescending(r => r.CompletedAtUtc ?? r.StartedAtUtc)
            .Select(r => new BenchmarkLastAssessorDto
            {
                RunId = r.Id,
                AssessorModelConfigurationId = r.AssessorModelConfigurationId,
                AssessorModelDisplayNameUsed = r.AssessorModelDisplayNameUsed,
                AssessorModelProviderUsed = r.AssessorModelProviderUsed,
                SecondOpinionAssessorModelConfigurationId = r.SecondOpinionAssessorModelConfigurationId,
                SecondOpinionAssessorModelDisplayNameUsed = r.SecondOpinionAssessorModelDisplayNameUsed,
                CompletedAtUtc = r.CompletedAtUtc,
                HarnessVersion = r.HarnessVersion,
                ScoringMethodVersion = r.ScoringMethodVersion
            })
            .FirstOrDefaultAsync();

        // A suite with no completed run has no baseline to differ from, which is not an error.
        return Ok(last ?? new BenchmarkLastAssessorDto());
    }

    [HttpGet("runs/{id}")]
    public async Task<IActionResult> GetRun(long id)
    {
        var run = await _dbContext.BenchmarkRuns
            .Include(r => r.Answers)
            .Include(r => r.StartedByUser)
            .Include(r => r.ScoringProfile)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (run == null) return NotFound();

        // The constants this run was scored with, from its own snapshot. One reader for that
        // storage format lives in BenchmarkScoring; the client gets the fields, not the JSON.
        var runConstants = BenchmarkScoring.ConstantsFromSnapshot(run.ScoringProfileSnapshotJson);

        bool assessorAvailable = run.AssessorModelConfigurationId.HasValue &&
            await _dbContext.SystemAiApiConfigurations.AnyAsync(c =>
                c.Id == run.AssessorModelConfigurationId.Value &&
                c.IsEnabled && c.EncryptedApiKey != null && (c.ModelRole & 4) == 4);

        var dto = new BenchmarkRunDetailDto
        {
            Id = run.Id,
            BenchmarkSuiteId = run.BenchmarkSuiteId,
            SuiteName = run.SuiteName,
            TestedModelConfigurationId = run.TestedModelConfigurationId,
            TestedModelDisplayNameUsed = run.TestedModelDisplayNameUsed,
            TestedModelProviderUsed = run.TestedModelProviderUsed,
            TestedModelIdUsed = run.TestedModelIdUsed,
            TestedModelThinkingLevelUsed = run.TestedModelThinkingLevelUsed,
            TestedModelReasoningModeUsed = run.TestedModelReasoningModeUsed,
            TestedModelReasoningSummaryUsed = run.TestedModelReasoningSummaryUsed,
            TestedModelServiceTierUsed = run.TestedModelServiceTierUsed,
            TestedModelMaxOutputTokensUsed = run.TestedModelMaxOutputTokensUsed,
            TestedModelParallelExecutionModeUsed = run.TestedModelParallelExecutionModeUsed,

            AssessorModelConfigurationId = run.AssessorModelConfigurationId,
            AssessorModelDisplayNameUsed = run.AssessorModelDisplayNameUsed,
            AssessorModelProviderUsed = run.AssessorModelProviderUsed,
            AssessorModelIdUsed = run.AssessorModelIdUsed,
            AssessorModelThinkingLevelUsed = run.AssessorModelThinkingLevelUsed,
            AssessorModelReasoningModeUsed = run.AssessorModelReasoningModeUsed,
            AssessorAvailable = assessorAvailable,

            SecondOpinionAssessorModelConfigurationId = run.SecondOpinionAssessorModelConfigurationId,
            SecondOpinionAssessorModelDisplayNameUsed = run.SecondOpinionAssessorModelDisplayNameUsed,
            SecondOpinionAssessorModelProviderUsed = run.SecondOpinionAssessorModelProviderUsed,
            SecondOpinionAssessorModelIdUsed = run.SecondOpinionAssessorModelIdUsed,
            SecondOpinionAssessorModelThinkingLevelUsed = run.SecondOpinionAssessorModelThinkingLevelUsed,
            SecondOpinionAssessorModelReasoningModeUsed = run.SecondOpinionAssessorModelReasoningModeUsed,

            ClaimVerifierModelConfigurationId = run.ClaimVerifierModelConfigurationId,
            ClaimVerifierDisplayNameUsed = run.ClaimVerifierDisplayNameUsed,
            ClaimVerifierProviderUsed = run.ClaimVerifierProviderUsed,
            ClaimVerifierModelIdUsed = run.ClaimVerifierModelIdUsed,
            ClaimVerifierThinkingLevelUsed = run.ClaimVerifierThinkingLevelUsed,
            ClaimVerifierReasoningModeUsed = run.ClaimVerifierReasoningModeUsed,

            StartedByUserId = run.StartedByUserId,
            StartedByUserName = run.StartedByUser?.UserName,
            Status = run.Status,
            StartedAtUtc = run.StartedAtUtc,
            CompletedAtUtc = run.CompletedAtUtc,
            FinalScore = run.FinalScore,
            ComputedScore = run.ComputedScore,
            QualityIndex = run.QualityIndex,
            RawQualityIndex = BenchmarkScoring.QualityIndex(
                run.Answers
                    .Where(a => a.Status == BenchmarkAnswerStatus.Ok && a.QualityScore.HasValue)
                    .Select(a => (a.RawQualityScore ?? a.QualityScore, a.AssessedDifficulty ?? BenchmarkRunFinalizer.FallbackDifficulty(a.Difficulty)))
                    .ToList()),
            UnweightedQualityIndex = run.UnweightedQualityIndex,
            SpeedIndex = run.SpeedIndex,
            TotalAnswerDurationMs = run.TotalAnswerDurationMs,
            ScoringProfileId = run.ScoringProfileId,
            ScoringProfileName = run.ScoringProfile?.Name,
            ScoringProfileSnapshotJson = run.ScoringProfileSnapshotJson,
            ScoringProfileSpeedTargetMs = runConstants.SpeedTargetMs,
            ScoringProfileSpeedDecayK = runConstants.SpeedDecayK,
            ScoringProfileSecondOpinionQualityThreshold = runConstants.SecondOpinionQualityThreshold,
            ScoringProfileSecondOpinionOutlierDeltaPoints = runConstants.SecondOpinionOutlierDeltaPoints,
            ScoringMethodVersion = run.ScoringMethodVersion,
            HarnessVersion = run.HarnessVersion,
            MaxToolCallsPerQuestionUsed = run.MaxToolCallsPerQuestionUsed,
            DegradedAnswerCount = run.DegradedAnswerCount,
            ToolStarvedAnswerCount = run.ToolStarvedAnswerCount,
            TransportDefectAnswerCount = run.TransportDefectAnswerCount,
            RecoveredAnswerCount = run.RecoveredAnswerCount,
            AdvisoryFlagAnswerCount = run.AdvisoryFlagAnswerCount,
            ScrubbedArtifactAnswerCount = run.ScrubbedArtifactAnswerCount,
            ContestedVerdictAnswerCount = run.ContestedVerdictAnswerCount,
            UnevidencedDeductionAnswerCount = run.UnevidencedDeductionAnswerCount,
            RefutedClaimAnswerCount = run.RefutedClaimAnswerCount,
            ClaimVerifiedAnswerCount = run.ClaimVerifiedAnswerCount,
            ClaimsSupportedCount = run.ClaimsSupportedCount,
            ClaimsRefutedCount = run.ClaimsRefutedCount,
            ClaimsIndeterminateCount = run.ClaimsIndeterminateCount,
            ReassessedAnswerCount = run.ReassessedAnswerCount,
            SecondOpinionModeUsed = run.SecondOpinionModeUsed,
            SecondOpinionGradedAnswerCount = run.SecondOpinionGradedAnswerCount,
            SecondOpinionMeanAbsDelta = run.SecondOpinionMeanAbsDelta,

            // Manual verdicts are trials an operator ran by hand against a prospective assessor;
            // the agreement figures are about the run's own two graders.
            SecondOpinionDisagreementCount = run.Answers.Count(a =>
                a.SecondOpinionDisagreed && a.SecondOpinionQualityScore.HasValue &&
                !string.Equals(a.SecondOpinionTrigger, "Manual", StringComparison.Ordinal)),
            ToolOverheadMs = run.ToolOverheadMs,
            DifficultyFallbackUsed = run.DifficultyFallbackUsed,
            SpeedMeasurementDegraded = run.SpeedMeasurementDegraded,
            MaxParallelQuestionsUsed = run.MaxParallelQuestionsUsed,
            AnsweredQuestionCount = run.AnsweredQuestionCount,
            TotalQuestionCount = run.TotalQuestionCount,
            PurposeStatementUsed = run.PurposeStatementUsed,
            SameProviderAcknowledged = run.SameProviderAcknowledged,
            AssessmentJson = run.AssessmentJson,
            AssessmentText = run.AssessmentText,
            AssessmentParseFailed = run.AssessmentParseFailed,
            TotalInputTokens = run.TotalInputTokens,
            TotalOutputTokens = run.TotalOutputTokens,
            TotalCacheReadTokens = run.TotalCacheReadTokens,
            TotalCacheCreationTokens = run.TotalCacheCreationTokens,
            TotalDurationMs = run.TotalDurationMs,
            TotalAssessmentInputTokens = run.TotalAssessmentInputTokens,
            TotalAssessmentOutputTokens = run.TotalAssessmentOutputTokens,
            TotalAssessmentDurationMs = run.TotalAssessmentDurationMs,
            TotalClaimVerificationInputTokens = run.TotalClaimVerificationInputTokens,
            TotalClaimVerificationOutputTokens = run.TotalClaimVerificationOutputTokens,
            TotalClaimVerificationDurationMs = run.TotalClaimVerificationDurationMs,
            ErrorMessage = run.ErrorMessage,

            // Empty unless this is the run currently executing in this process.
            InFlightOrderIndexes = _runManager.GetInFlightQuestions(run.Id).ToList(),

            Answers = run.Answers.OrderBy(a => a.OrderIndex).Select(a => new BenchmarkRunAnswerDto
            {
                Id = a.Id,
                BenchmarkRunId = a.BenchmarkRunId,
                BenchmarkQuestionId = a.BenchmarkQuestionId,
                ItemRevisionUsed = a.ItemRevisionUsed,
                OrderIndex = a.OrderIndex,
                QuestionText = a.QuestionText,
                Difficulty = a.Difficulty,
                AssessedDifficulty = a.AssessedDifficulty,
                AnswerText = a.AnswerText,
                ThoughtText = a.ThoughtText,
                Status = a.Status,
                AssessmentStatus = a.AssessmentStatus,
                AssessmentError = a.AssessmentError,
                ErrorMessage = a.ErrorMessage,
                HttpStatusCode = a.HttpStatusCode,
                Score = a.Score,
                AccuracyLevel = a.AccuracyLevel,
                CompletenessLevel = a.CompletenessLevel,
                ConcisenessLevel = a.ConcisenessLevel,
                ReadabilityLevel = a.ReadabilityLevel,
                CriticalError = a.CriticalError,
                AccuracyScore = a.AccuracyScore,
                CompletenessScore = a.CompletenessScore,
                ConcisenessScore = a.ConcisenessScore,
                ReadabilityScore = a.ReadabilityScore,
                QualityScore = a.QualityScore,
                RawQualityScore = a.RawQualityScore,
                SpeedScore = a.SpeedScore,
                ReviewComment = a.ReviewComment,
                DurationMs = a.DurationMs,
                TimeToFirstTokenMs = a.TimeToFirstTokenMs,
                ActualServiceTierUsed = a.ActualServiceTierUsed,
                ToolCallSummary = a.ToolCallSummary,
                InputTokens = a.InputTokens,
                OutputTokens = a.OutputTokens,
                CacheReadInputTokens = a.CacheReadInputTokens,
                CacheCreationInputTokens = a.CacheCreationInputTokens,
                ModelCallCount = a.ModelCallCount,
                ToolCallCount = a.ToolCallCount,
                ToolBudgetExhausted = a.ToolBudgetExhausted,
                ToolCallBudgetUsed = a.ToolCallBudgetUsed,
                ToolTimeMs = a.ToolTimeMs,
                ModelTimeMs = a.ModelTimeMs,
                ScrubbedArtifactText = a.ScrubbedArtifactText,
                ScrubbedArtifactCount = a.ScrubbedArtifactCount,
                NarrationBlockCount = a.NarrationBlockCount,
                TerminationReason = a.TerminationReason,
                AnswerFlags = a.AnswerFlags,
                AnswerFlagNames = ((BenchmarkAnswerFlags)a.AnswerFlags != BenchmarkAnswerFlags.None)
                    ? Enum.GetValues<BenchmarkAnswerFlags>()
                        .Where(f => f != BenchmarkAnswerFlags.None && ((BenchmarkAnswerFlags)a.AnswerFlags).HasFlag(f))
                        .Select(f => f.ToString())
                        .ToList()
                    : new List<string>(),
                AssessedByModelConfigurationId = a.AssessedByModelConfigurationId,
                AssessedByModelDisplayNameUsed = a.AssessedByModelDisplayNameUsed,
                AssessedByModelProviderUsed = a.AssessedByModelProviderUsed,
                AssessedByModelIdUsed = a.AssessedByModelIdUsed,
                AssessedAtUtc = a.AssessedAtUtc,
                AssessmentInputTokens = a.AssessmentInputTokens,
                AssessmentOutputTokens = a.AssessmentOutputTokens,
                AssessmentDurationMs = a.AssessmentDurationMs,
                AssessmentEvidenceJson = a.AssessmentEvidenceJson,
                CriticalErrorQuote = a.CriticalErrorQuote,
                UnverifiedClaimCount = a.UnverifiedClaimCount,
                UnverifiedClaimsJson = a.UnverifiedClaimsJson,
                SecondOpinionQualityScore = a.SecondOpinionQualityScore,
                SecondOpinionCriticalError = a.SecondOpinionCriticalError,
                SecondOpinionByModelDisplayNameUsed = a.SecondOpinionByModelDisplayNameUsed,
                SecondOpinionJson = a.SecondOpinionJson,
                SecondOpinionDisagreed = a.SecondOpinionDisagreed,
                SecondOpinionTrigger = a.SecondOpinionTrigger,
                ClaimVerificationJson = a.ClaimVerificationJson,
                ClaimsSupportedCount = a.ClaimsSupportedCount,
                ClaimsRefutedCount = a.ClaimsRefutedCount,
                ClaimsIndeterminateCount = a.ClaimsIndeterminateCount,
                ClaimVerificationByModelDisplayNameUsed = a.ClaimVerificationByModelDisplayNameUsed,
                ClaimVerificationInputTokens = a.ClaimVerificationInputTokens,
                ClaimVerificationOutputTokens = a.ClaimVerificationOutputTokens,
                ClaimVerificationDurationMs = a.ClaimVerificationDurationMs,
                ClaimVerificationToolCallCount = a.ClaimVerificationToolCallCount,
                ClaimVerificationError = a.ClaimVerificationError,
                ReassessedAtUtc = a.ReassessedAtUtc,
                ReassessedByModelDisplayNameUsed = a.ReassessedByModelDisplayNameUsed,
                PreviousQualityScore = a.PreviousQualityScore,
                ReassessmentCount = a.ReassessmentCount
            }).ToList()
        };

        return Ok(dto);
    }

    /// <summary>
    /// Returns the id of the run currently executing, so a client that reloaded mid-run can
    /// reattach to it. Only the id is returned; the client calls <see cref="GetRun"/> for the
    /// detail rather than duplicating that projection here.
    /// </summary>
    [HttpGet("runs/active")]
    public IActionResult GetActiveRun()
    {
        var runId = _runManager.CurrentRunId;
        if (!runId.HasValue)
        {
            return NoContent();
        }
        return Ok(new { runId = runId.Value });
    }

    [HttpGet("runs")]
    public async Task<IActionResult> GetRuns([FromQuery] long? suiteId, [FromQuery] int? take)
    {
        var query = _dbContext.BenchmarkRuns
            .Include(r => r.StartedByUser)
            .AsQueryable();

        if (suiteId.HasValue)
        {
            query = query.Where(r => r.BenchmarkSuiteId == suiteId.Value);
        }

        int limit = Math.Clamp(take ?? 50, 1, 200);

        var runs = await query
            .OrderByDescending(r => r.StartedAtUtc)
            .Take(limit)
            .Select(r => new BenchmarkRunSummaryDto
            {
                Id = r.Id,
                BenchmarkSuiteId = r.BenchmarkSuiteId,
                SuiteName = r.SuiteName,
                TestedModelConfigurationId = r.TestedModelConfigurationId,
                TestedModelDisplayNameUsed = r.TestedModelDisplayNameUsed,
                TestedModelProviderUsed = r.TestedModelProviderUsed,
                TestedModelIdUsed = r.TestedModelIdUsed,
                AssessorModelConfigurationId = r.AssessorModelConfigurationId,
                AssessorModelDisplayNameUsed = r.AssessorModelDisplayNameUsed,
                StartedByUserName = r.StartedByUser != null ? r.StartedByUser.UserName : null,
                Status = r.Status,
                StartedAtUtc = r.StartedAtUtc,
                CompletedAtUtc = r.CompletedAtUtc,
                FinalScore = r.FinalScore,
                ComputedScore = r.ComputedScore,
                QualityIndex = r.QualityIndex,
                SpeedIndex = r.SpeedIndex,
                TotalAnswerDurationMs = r.TotalAnswerDurationMs,
                SpeedMeasurementDegraded = r.SpeedMeasurementDegraded,
                AnsweredQuestionCount = r.AnsweredQuestionCount,
                TotalQuestionCount = r.TotalQuestionCount,
                DegradedAnswerCount = r.DegradedAnswerCount,
                ToolStarvedAnswerCount = r.ToolStarvedAnswerCount,
                HarnessVersion = r.HarnessVersion,
                TotalDurationMs = r.TotalDurationMs
            })
            .ToListAsync();

        return Ok(runs);
    }

    [HttpPost("runs/{id}/rescore")]
    public async Task<IActionResult> RescoreRun(long id, [FromBody] RescoreRunRequest? request)
    {
        var (success, error) = await _benchmarkService.RescoreRunAsync(id, request?.ScoringProfileId);
        if (!success)
        {
            return BadRequest(error);
        }
        return Ok();
    }

    private async Task<(bool Success, string? Error)> ValidateAssessorConfigurationAsync(long? assessorConfigId)
    {
        if (!assessorConfigId.HasValue)
        {
            return (false, "No assessor model configuration specified.");
        }
        var config = await _dbContext.SystemAiApiConfigurations.FindAsync(assessorConfigId.Value);
        if (config == null)
        {
            return (false, "The assessor configuration was not found.");
        }
        if (!config.IsEnabled)
        {
            return (false, "The assessor configuration is disabled.");
        }
        if (string.IsNullOrWhiteSpace(config.EncryptedApiKey))
        {
            return (false, "The assessor configuration has no API key.");
        }
        if ((config.ModelRole & 4) != 4)
        {
            return (false, "The assessor configuration does not have the Benchmark role.");
        }
        return (true, null);
    }

    [HttpPost("runs/{id}/answers/{answerId}/reassess")]
    public async Task<IActionResult> ReassessAnswer(long id, long answerId, [FromBody] ReassessAnswerRequest? request)
    {
        if (_runManager.CurrentRunId.HasValue)
        {
            return Conflict("A benchmark run is already in progress.");
        }

        var (canSpend, denialReason) = await _complianceGuard.CanSpendAsync();
        if (!canSpend)
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, denialReason);
        }

        var run = await _dbContext.BenchmarkRuns
            .Include(r => r.Answers)
            .FirstOrDefaultAsync(r => r.Id == id);
        if (run == null) return NotFound();

        var answer = run.Answers.FirstOrDefault(a => a.Id == answerId);
        if (answer == null) return NotFound();

        long? targetAssessorId = request?.AssessorModelConfigurationId ?? run.AssessorModelConfigurationId;
        var (assessorValid, assessorError) = await ValidateAssessorConfigurationAsync(targetAssessorId);
        if (!assessorValid)
        {
            return BadRequest(assessorError);
        }

        bool trial = request?.Trial ?? false;

        // An automatic second opinion is run evidence; a manual trial is an experiment, and an
        // experiment must not erase evidence. Under All mode every answer carries a second
        // opinion, so a trial there is always a replacement - which is correct, and which this
        // makes an explicit act rather than a silent one.
        if (trial &&
            answer.SecondOpinionQualityScore.HasValue &&
            !(request?.ReplaceExistingSecondOpinion ?? false))
        {
            return Conflict(
                "This answer already has a second opinion from " +
                $"{answer.SecondOpinionByModelDisplayNameUsed ?? "another assessor"}. " +
                "Re-send with replaceExistingSecondOpinion to overwrite it.");
        }

        var cts = new CancellationTokenSource();
        if (!_runManager.TryStart(run.Id, cts, out _))
        {
            return Conflict("A benchmark run is already in progress.");
        }

        _ = Task.Run(() => _benchmarkService.ReassessSingleQuestionAsync(
            answerId, request?.AssessorModelConfigurationId, trial, cts.Token));
        return Accepted(new { runId = id, trial });
    }

    /// <summary>
    /// Re-grades a completed run's answers with an alternative assessor, non-destructively, and
    /// records how its verdicts compare with the ones that scored. Makes no candidate calls: it is
    /// one assessor pass over stored text, which is what makes it affordable enough to decide an
    /// assessor change from measurement rather than assumption.
    /// </summary>
    [HttpPost("runs/{id}/calibrate")]
    public async Task<IActionResult> CalibrateAssessor(long id, [FromBody] CalibrateAssessorRequest request)
    {
        if (_runManager.CurrentRunId.HasValue)
        {
            return Conflict("A benchmark run is already in progress.");
        }

        var (canSpend, denialReason) = await _complianceGuard.CanSpendAsync();
        if (!canSpend)
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, denialReason);
        }

        var run = await _dbContext.BenchmarkRuns.FirstOrDefaultAsync(r => r.Id == id);
        if (run == null) return NotFound();

        var (assessorValid, assessorError) = await ValidateAssessorConfigurationAsync(request.AssessorModelConfigurationId);
        if (!assessorValid)
        {
            return BadRequest(assessorError);
        }

        var cts = new CancellationTokenSource();
        if (!_runManager.TryStart(run.Id, cts, out _))
        {
            return Conflict("A benchmark run is already in progress.");
        }

        string? userName = User?.Identity?.Name;
        _ = Task.Run(async () =>
        {
            try
            {
                await _benchmarkService.RunAssessorCalibrationAsync(
                    id, request.AssessorModelConfigurationId, userName, cts.Token);
            }
            finally
            {
                _runManager.Complete(id);
            }
        });

        return Accepted(new { runId = id });
    }

    [HttpGet("runs/{id}/calibrations")]
    public async Task<IActionResult> GetCalibrations(long id)
    {
        var calibrations = await _dbContext.BenchmarkAssessorCalibrations
            .Where(c => c.BenchmarkRunId == id)
            .OrderByDescending(c => c.CreatedAtUtc)
            .Select(c => new BenchmarkAssessorCalibrationDto
            {
                Id = c.Id,
                BenchmarkRunId = c.BenchmarkRunId,
                AssessorDisplayNameUsed = c.AssessorDisplayNameUsed,
                AssessorProviderUsed = c.AssessorProviderUsed,
                AssessorModelIdUsed = c.AssessorModelIdUsed,
                AssessorThinkingLevelUsed = c.AssessorThinkingLevelUsed,
                CreatedAtUtc = c.CreatedAtUtc,
                CreatedByUserName = c.CreatedByUserName,
                AnswerCount = c.AnswerCount,
                SkippedAnswerCount = c.SkippedAnswerCount,
                MeanAbsDelta = c.MeanAbsDelta,
                DisagreementCount = c.DisagreementCount,
                InputTokens = c.InputTokens,
                OutputTokens = c.OutputTokens,
                DurationMs = c.DurationMs,
                VerdictsJson = c.VerdictsJson,
                ErrorMessage = c.ErrorMessage
            })
            .ToListAsync();

        return Ok(calibrations);
    }

    [HttpPost("runs/{id}/answers/{answerId}/rerun")]
    public async Task<IActionResult> RerunAnswer(long id, long answerId, [FromBody] BenchmarkRetryRequest? request)
    {
        if (_runManager.CurrentRunId.HasValue)
        {
            return Conflict("A benchmark run is already in progress.");
        }

        var (canSpend, denialReason) = await _complianceGuard.CanSpendAsync();
        if (!canSpend)
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, denialReason);
        }

        var run = await _dbContext.BenchmarkRuns
            .Include(r => r.Answers)
            .FirstOrDefaultAsync(r => r.Id == id);
        if (run == null) return NotFound();

        var answer = run.Answers.FirstOrDefault(a => a.Id == answerId);
        if (answer == null) return NotFound();

        if (string.IsNullOrWhiteSpace(answer.QuestionText))
        {
            return BadRequest("Question text is empty.");
        }

        if (!run.TestedModelConfigurationId.HasValue)
        {
            return BadRequest("Run has no tested model configuration.");
        }
        var testedConfig = await _dbContext.SystemAiApiConfigurations.FindAsync(run.TestedModelConfigurationId.Value);
        if (testedConfig == null)
        {
            return BadRequest("The tested model configuration was not found.");
        }
        if (string.IsNullOrWhiteSpace(testedConfig.EncryptedApiKey))
        {
            return BadRequest("The tested model configuration has no API key.");
        }

        long? targetAssessorId = request?.AssessorModelConfigurationId ?? run.AssessorModelConfigurationId;
        var (assessorValid, assessorError) = await ValidateAssessorConfigurationAsync(targetAssessorId);
        if (!assessorValid)
        {
            return BadRequest(assessorError);
        }

        var cts = new CancellationTokenSource();
        if (!_runManager.TryStart(run.Id, cts, out _))
        {
            return Conflict("A benchmark run is already in progress.");
        }

        _ = Task.Run(() => _benchmarkService.RerunSingleQuestionAsync(answerId, request?.AssessorModelConfigurationId, cts.Token));
        return Accepted(new { runId = id });
    }

    [HttpPost("runs/{id}/rerun-synthesis")]
    public async Task<IActionResult> RerunSynthesis(long id, [FromBody] BenchmarkRetryRequest? request)
    {
        if (_runManager.CurrentRunId.HasValue)
        {
            return Conflict("A benchmark run is already in progress.");
        }

        var (canSpend, denialReason) = await _complianceGuard.CanSpendAsync();
        if (!canSpend)
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, denialReason);
        }

        var run = await _dbContext.BenchmarkRuns
            .Include(r => r.Answers)
            .FirstOrDefaultAsync(r => r.Id == id);
        if (run == null) return NotFound();

        if (run.Answers.Count == 0)
        {
            return BadRequest("This run has no answers to synthesize.");
        }

        long? targetAssessorId = request?.AssessorModelConfigurationId ?? run.AssessorModelConfigurationId;
        var (assessorValid, assessorError) = await ValidateAssessorConfigurationAsync(targetAssessorId);
        if (!assessorValid)
        {
            return BadRequest(assessorError);
        }

        var cts = new CancellationTokenSource();
        if (!_runManager.TryStart(run.Id, cts, out _))
        {
            return Conflict("A benchmark run is already in progress.");
        }

        _ = Task.Run(() => _benchmarkService.RerunFinalSynthesisAsync(id, request?.AssessorModelConfigurationId, cts.Token));
        return Accepted(new { runId = id });
    }

    [HttpPost("runs/{id}/retry-failed-assessments")]
    public async Task<IActionResult> RetryFailedAssessments(long id, [FromBody] BenchmarkRetryRequest? request)
    {
        if (_runManager.CurrentRunId.HasValue)
        {
            return Conflict("A benchmark run is already in progress.");
        }

        var (canSpend, denialReason) = await _complianceGuard.CanSpendAsync();
        if (!canSpend)
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, denialReason);
        }

        var run = await _dbContext.BenchmarkRuns
            .Include(r => r.Answers)
            .FirstOrDefaultAsync(r => r.Id == id);
        if (run == null) return NotFound();

        if (!run.Answers.Any(a => a.AssessmentStatus != BenchmarkAssessmentStatus.Scored))
        {
            return BadRequest("This run has no unscored assessments to retry.");
        }

        long? targetAssessorId = request?.AssessorModelConfigurationId ?? run.AssessorModelConfigurationId;
        var (assessorValid, assessorError) = await ValidateAssessorConfigurationAsync(targetAssessorId);
        if (!assessorValid)
        {
            return BadRequest(assessorError);
        }

        var cts = new CancellationTokenSource();
        if (!_runManager.TryStart(run.Id, cts, out _))
        {
            return Conflict("A benchmark run is already in progress.");
        }

        _ = Task.Run(() => _benchmarkService.RetryFailedAssessmentsAsync(id, request?.AssessorModelConfigurationId, cts.Token));
        return Accepted(new { runId = id });
    }

    [HttpPost("runs/{id}/cancel")]
    public async Task<IActionResult> CancelRun(long id)
    {
        bool cancelled = _runManager.TryCancel(id);
        var run = await _dbContext.BenchmarkRuns.FindAsync(id);
        if (run != null && run.Status == BenchmarkRunStatus.Running)
        {
            run.Status = BenchmarkRunStatus.Canceled;
            run.CompletedAtUtc = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
        }
        return Ok(new { success = cancelled || (run != null && run.Status == BenchmarkRunStatus.Canceled) });
    }

    [HttpPost("runs/{id}/rerun-failed")]
    public async Task<IActionResult> RerunFailedQuestions(long id)
    {
        if (_runManager.CurrentRunId.HasValue)
        {
            return Conflict("A benchmark run is already in progress.");
        }

        var (canSpend, denialReason) = await _complianceGuard.CanSpendAsync();
        if (!canSpend)
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, denialReason);
        }

        var run = await _dbContext.BenchmarkRuns
            .Include(r => r.Answers)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (run == null) return NotFound();

        bool hasFailures = run.Answers.Any(a => a.Status == BenchmarkAnswerStatus.ProviderError || a.Status == BenchmarkAnswerStatus.Failed);
        if (!hasFailures)
        {
            return BadRequest("This run has no failed or provider-error questions to re-run.");
        }

        var cts = new CancellationTokenSource();
        if (!_runManager.TryStart(run.Id, cts, out _))
        {
            return Conflict("A benchmark run is already in progress.");
        }

        _ = Task.Run(() => _benchmarkService.RunFailedQuestionsAsync(run.Id, cts.Token));

        return Accepted(new { runId = run.Id });
    }

    [HttpGet("runs/{id}/report")]
    public async Task<IActionResult> GetRunReport(long id)
    {
        var run = await _dbContext.BenchmarkRuns
            .Include(r => r.Answers)
            .Include(r => r.StartedByUser)
            .Include(r => r.ScoringProfile)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (run == null) return NotFound();

        // The report's provenance line is worthless as a hard-coded fallback: every report ever
        // produced claimed "1.0.0" because this caller never passed a version.
        string markdown = BenchmarkReportBuilder.BuildMarkdownReport(run, GetOverseerVersion());
        string filename = $"{SanitizeFilename(run.SuiteName)}_{SanitizeFilename(run.TestedModelDisplayNameUsed)}_{run.StartedAtUtc:yyyyMMdd_HHmmss}.md";

        return File(Encoding.UTF8.GetBytes(markdown), "text/markdown; charset=utf-8", filename);
    }

    [HttpGet("suites/{id}/runs/footprint")]
    public async Task<IActionResult> GetSuiteRunsFootprint(long id)
    {
        var runIds = await _dbContext.BenchmarkRuns
            .Where(r => r.BenchmarkSuiteId == id)
            .Select(r => r.Id)
            .ToListAsync();

        int runCount = runIds.Count;
        long totalChars = 0;
        if (runCount > 0)
        {
            totalChars = await _dbContext.BenchmarkRunAnswers
                .Where(a => runIds.Contains(a.BenchmarkRunId) && a.AnswerText != null)
                .SumAsync(a => (long)a.AnswerText.Length);
        }

        return Ok(new BenchmarkFootprintDto
        {
            RunCount = runCount,
            TotalAnswerCharacters = totalChars
        });
    }

    [HttpDelete("suites/{id}/runs")]
    public async Task<IActionResult> DeleteSuiteRuns(long id)
    {
        var runs = await _dbContext.BenchmarkRuns
            .Where(r => r.BenchmarkSuiteId == id)
            .ToListAsync();

        if (_runManager.CurrentRunId.HasValue && runs.Any(r => r.Id == _runManager.CurrentRunId.Value))
        {
            return BadRequest("Cannot delete runs while a run in this suite is currently active.");
        }

        int count = runs.Count;
        _dbContext.BenchmarkRuns.RemoveRange(runs);
        await _dbContext.SaveChangesAsync();

        return Ok(new { deletedCount = count });
    }

    [HttpDelete("runs/{id}")]
    public async Task<IActionResult> DeleteRun(long id)
    {
        if (_runManager.CurrentRunId == id)
        {
            return BadRequest("Cannot delete an active benchmark run.");
        }

        var run = await _dbContext.BenchmarkRuns.FindAsync(id);
        if (run != null)
        {
            _dbContext.BenchmarkRuns.Remove(run);
            await _dbContext.SaveChangesAsync();
        }
        return Ok();
    }

    /// <summary>
    /// The running build, formatted as <c>SystemController</c> reports it to the client, so the
    /// report's version line and the diagnostics' "Overseer build" line always agree.
    /// </summary>
    private static string? GetOverseerVersion()
    {
        var informational = Assembly.GetEntryAssembly()?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (string.IsNullOrWhiteSpace(informational))
        {
            return null;
        }

        return informational.Split('+')[0];
    }

    private static string SanitizeFilename(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(name.Where(c => !invalid.Contains(c)).ToArray());
        return string.IsNullOrWhiteSpace(clean) ? "benchmark" : clean.Replace(' ', '_');
    }
}
