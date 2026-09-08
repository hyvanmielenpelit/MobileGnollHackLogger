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
using Overseer.Services;
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
    private readonly BenchmarkRubricGapAuthorJobManager _rubricGapAuthorJobManager;
    private readonly BenchmarkRubricGapAuthorService _rubricGapAuthorService;
    private readonly BenchmarkRunLauncher _runLauncher;
    private readonly BenchmarkSeriesOrchestrator _seriesOrchestrator;
    private readonly BenchmarkGroupAnalysisService _groupAnalysisService;
    private readonly ModelPricingService? _modelPricingService;

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
        BenchmarkRubricCheckService rubricCheckService,
        BenchmarkRubricGapAuthorJobManager rubricGapAuthorJobManager,
        BenchmarkRubricGapAuthorService rubricGapAuthorService,
        BenchmarkRunLauncher runLauncher,
        BenchmarkSeriesOrchestrator seriesOrchestrator,
        BenchmarkGroupAnalysisService groupAnalysisService,
        ModelPricingService? modelPricingService = null)
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
        _rubricGapAuthorJobManager = rubricGapAuthorJobManager;
        _rubricGapAuthorService = rubricGapAuthorService;
        _runLauncher = runLauncher;
        _seriesOrchestrator = seriesOrchestrator;
        _groupAnalysisService = groupAnalysisService;
        _modelPricingService = modelPricingService;
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
        SourceChatSessionId = s.SourceChatSessionId,
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
        var gapJob = _rubricGapAuthorJobManager.Current;
        if (gapJob != null && gapJob.Status == BenchmarkRubricGapAuthorJobStatus.Running && gapJob.SuiteId == suiteId)
        {
            return Conflict(new { error = $"Cannot start {requestingJobName}: Rubric gap author job '{gapJob.Id}' is currently running on this suite." });
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
            SecondOpinionMinimumSample = p.SecondOpinionMinimumSample,
            SecondOpinionBlind = p.SecondOpinionBlind,
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
            SecondOpinionMinimumSample = request.SecondOpinionMinimumSample,
            SecondOpinionBlind = request.SecondOpinionBlind,
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
            SecondOpinionMinimumSample = created.SecondOpinionMinimumSample,
            SecondOpinionBlind = created.SecondOpinionBlind,
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
            SecondOpinionMinimumSample = request.SecondOpinionMinimumSample,
            SecondOpinionBlind = request.SecondOpinionBlind,
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
            SecondOpinionMinimumSample = updated.SecondOpinionMinimumSample,
            SecondOpinionBlind = updated.SecondOpinionBlind,
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
    /// <summary>
    /// Every unverified claim recorded against a suite, with the claim verifier's verdict, citation
    /// and basis attached. One owner for the projection: the gap report and the Rubric Gap Author
    /// must see exactly the same claims, or an operator would be offered a draft for a cluster the
    /// panel above it does not show.
    /// </summary>
    private async Task<(List<BenchmarkUnverifiedClaimSample> Samples, int RunCount)> LoadUnverifiedClaimSamplesAsync(
        long suiteId,
        CancellationToken ct = default)
    {
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
            .ToListAsync(ct);

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

            Dictionary<string, BenchmarkClaimVerification>? verificationsByClaim = null;
            if (!string.IsNullOrWhiteSpace(row.ClaimVerificationJson))
            {
                try
                {
                    var verifications = JsonSerializer.Deserialize<List<BenchmarkClaimVerification>>(row.ClaimVerificationJson);
                    if (verifications != null)
                    {
                        verificationsByClaim = new Dictionary<string, BenchmarkClaimVerification>(StringComparer.Ordinal);
                        foreach (var v in verifications)
                        {
                            if (!string.IsNullOrWhiteSpace(v.Claim))
                            {
                                verificationsByClaim[v.Claim.Trim()] = v;
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
                string? citation = null;
                string? basis = null;
                if (verificationsByClaim != null && verificationsByClaim.TryGetValue(claim.Trim(), out var v))
                {
                    verdict = v.Verdict;
                    citation = v.Citation;
                    basis = v.Basis;
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
                    VerificationVerdict = verdict,
                    Citation = citation,
                    Basis = basis
                });
            }
        }

        return (samples, rows.Select(r => r.BenchmarkRunId).Distinct().Count());
    }

    [HttpGet("suites/{suiteId}/rubric-gaps")]
    public async Task<IActionResult> GetRubricGaps(long suiteId)
    {
        var suite = await _dbContext.BenchmarkSuites.FindAsync(suiteId);
        if (suite == null) return NotFound();

        var (samples, runCount) = await LoadUnverifiedClaimSamplesAsync(suiteId);

        var clusters = BenchmarkRubricGapDetector.Detect(samples);
        var kbGaps = BenchmarkRubricGapDetector.DetectKnowledgeBaseGaps(samples);

        return Ok(new BenchmarkRubricGapReportDto
        {
            SuiteId = suiteId,
            RunCount = runCount,
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
            }).ToList(),
            KnowledgeBaseGaps = kbGaps.Select(g => new BenchmarkKnowledgeBaseGapDto
            {
                Claim = g.Claim,
                Citation = g.Citation,
                Basis = g.Basis,
                QuestionOrderIndices = g.QuestionOrderIndices.ToList(),
                Recurrence = g.Recurrence
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
            DateTime.UtcNow,
            request.SessionId);

        try
        {
            var (board, suite) = await _snapshotImporter.FromClientTextAsync(snapshotText, meta, ct);
            return Ok(new CaptureBenchmarkSnapshotResponse
            {
                Board = ToSnapshotDto(board, suite.Id, suite.Name),
                Suite = ToSuiteDto(suite)
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("snapshots/from-session")]
    public async Task<IActionResult> SaveAttachedSnapshot([FromBody] SaveAttachedSnapshotRequest request, CancellationToken ct)
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

        string p0 = ChatService.GameSnapshotLikePatterns[0];
        string p1 = ChatService.GameSnapshotLikePatterns[1];
        var snapshotMessage = await _dbContext.ChatMessage
            .Where(m => m.ChatSessionId == session.Id && m.Role == "system" &&
                (EF.Functions.Like(m.Content, p0) || EF.Functions.Like(m.Content, p1)))
            .OrderByDescending(m => m.TimestampUtc)
            .FirstOrDefaultAsync(ct);

        if (snapshotMessage == null || string.IsNullOrWhiteSpace(snapshotMessage.Content))
        {
            return Conflict(new { error = "This chat has no attached game snapshot. Use Capture Live Board instead, or attach a snapshot first." });
        }

        string strippedContent = ChatService.StripGameSnapshotPrefix(snapshotMessage.Content);

        var meta = new BoardMetadata(
            request.Name.Trim(),
            request.Notes?.Trim(),
            request.SourceGnollHackVersion?.Trim(),
            DateTime.UtcNow,
            request.SessionId);

        try
        {
            var (board, suite) = await _snapshotImporter.FromSessionAttachmentAsync(strippedContent, meta, ct);
            return Ok(new CaptureBenchmarkSnapshotResponse
            {
                Board = ToSnapshotDto(board, suite.Id, suite.Name),
                Suite = ToSuiteDto(suite)
            });
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
                SourceChatSessionId = s.SourceChatSessionId,
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

    // --- Rubric Gap Author API ---
    //
    // The author *drafts*; a human accepts. Nothing in this section writes a rubric except
    // AcceptRubricAddition, which applies one draft, as the operator submitted it, to one question.
    // There is deliberately no accept-all endpoint: rung 1 of the benchmark-to-chat-transfer ladder
    // requires human authorship of curated knowledge, and BenchmarkRubricGapDetector already
    // documents that a gap is surfaced for a human and never applied automatically.

    [HttpPost("rubric-gap-author")]
    public async Task<IActionResult> StartRubricGapAuthor([FromBody] StartRubricGapAuthorRequest request, CancellationToken ct)
    {
        var suite = await _dbContext.BenchmarkSuites
            .Include(s => s.Questions)
            .FirstOrDefaultAsync(s => s.Id == request.SuiteId, ct);
        if (suite == null) return NotFound(new { error = "Suite not found." });

        var conflict = CheckConflictingBenchmarkJob(suite.Id, "rubric gap authoring");
        if (conflict != null) return conflict;

        var authorConfig = await _dbContext.SystemAiApiConfigurations.FindAsync(new object[] { request.AuthorModelConfigurationId }, ct);
        if (authorConfig == null || !authorConfig.IsEnabled)
        {
            return BadRequest(new { error = "Author model configuration not found or disabled." });
        }
        if (string.IsNullOrWhiteSpace(authorConfig.EncryptedApiKey))
        {
            return BadRequest(new { error = "Author model configuration has no API key." });
        }
        if ((authorConfig.ModelRole & 4) != 4)
        {
            return BadRequest(new { error = "Author model configuration is not enabled for the benchmarking role." });
        }

        var (canSpend, denialReason) = await _complianceGuard.CanSpendAsync();
        if (!canSpend)
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, denialReason);
        }

        var (samples, _) = await LoadUnverifiedClaimSamplesAsync(suite.Id, ct);
        var eligible = BenchmarkRubricGapAuthorService.BuildEligibleClusters(samples);

        if (request.ClusterKeys != null && request.ClusterKeys.Count > 0)
        {
            var wanted = new HashSet<string>(request.ClusterKeys, StringComparer.Ordinal);
            eligible = eligible.Where(e => wanted.Contains(e.Evidence.ClusterKey)).ToList();
        }

        if (eligible.Count == 0)
        {
            return BadRequest(new
            {
                error = "No eligible rubric gap clusters. A cluster is eligible only when a claim verifier checked it and returned Supported with a citation."
            });
        }

        string startedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var cts = new CancellationTokenSource();

        var job = new BenchmarkRubricGapAuthorJob
        {
            SuiteId = suite.Id,
            SuiteName = suite.Name,
            AuthorConfigId = authorConfig.Id,
            AuthorDisplayName = authorConfig.DisplayName,
            AuthorProviderUsed = authorConfig.Provider,
            AuthorModelIdUsed = authorConfig.ModelId,
            Instructions = string.IsNullOrWhiteSpace(request.Instructions) ? null : request.Instructions.Trim(),
            StartedByUserId = string.IsNullOrEmpty(startedByUserId) ? null : startedByUserId,
            Cts = cts,
            Drafts = eligible.Select(e =>
            {
                var question = suite.Questions.FirstOrDefault(q => q.Id == e.Cluster.QuestionId);
                string text = question?.QuestionText ?? string.Empty;
                return new BenchmarkRubricGapAuthorDraft
                {
                    ClusterKey = e.Evidence.ClusterKey,
                    QuestionId = e.Cluster.QuestionId,
                    QuestionOrderIndex = e.Cluster.QuestionOrderIndex,
                    QuestionTextExcerpt = text.Length <= 160 ? text : text.Substring(0, 160) + "...",
                    Claims = e.Cluster.Claims.ToList(),
                    ModelFamilies = e.Cluster.ModelFamilies.ToList(),
                    Occurrences = e.Cluster.Occurrences,
                    ClusterVerdict = e.Cluster.Verdict,
                    Status = BenchmarkRubricGapAuthorDraftStatus.Pending
                };
            }).ToList()
        };

        if (!_rubricGapAuthorJobManager.TryStart(job, out var existingJob))
        {
            return StatusCode(StatusCodes.Status409Conflict, existingJob?.ToDto());
        }

        var evidenceByKey = eligible.ToDictionary(e => e.Evidence.ClusterKey, e => e.Evidence, StringComparer.Ordinal);

        _ = Task.Run(async () =>
        {
            await _rubricGapAuthorService.RunRubricGapAuthorAsync(job.Id, evidenceByKey, cts.Token);
        });

        return Accepted(new { jobId = job.Id });
    }

    [HttpGet("rubric-gap-author/{jobId}")]
    public IActionResult GetRubricGapAuthor(string jobId)
    {
        var job = _rubricGapAuthorJobManager.TryGet(jobId);
        if (job == null) return NotFound();
        return Ok(job.ToDto());
    }

    [HttpGet("rubric-gap-author/active")]
    public IActionResult GetActiveRubricGapAuthor()
    {
        var current = _rubricGapAuthorJobManager.Current;
        if (current == null || current.Status != BenchmarkRubricGapAuthorJobStatus.Running)
        {
            return NoContent();
        }
        return Ok(current.ToDto());
    }

    [HttpPost("rubric-gap-author/{jobId}/cancel")]
    public IActionResult CancelRubricGapAuthor(string jobId)
    {
        var job = _rubricGapAuthorJobManager.TryGet(jobId);
        if (job == null) return NotFound();
        bool cancelled = _rubricGapAuthorJobManager.TryCancel(jobId);
        return Ok(new { cancelled });
    }

    /// <summary>
    /// Applies <b>one</b> operator-approved rubric addition to one question and bumps that
    /// question's item revision.
    ///
    /// The text written is <see cref="AcceptRubricAdditionRequest.AcceptedText"/> -- whatever the
    /// human submitted -- never the model's draft. The draft, the drafting model, the citation and a
    /// verbatim-or-edited flag are stored alongside it, because an authorship claim needs a stored
    /// fact rather than an assumption about who typed what.
    /// </summary>
    [HttpPost("questions/{id}/rubric-additions/accept")]
    public async Task<IActionResult> AcceptRubricAddition(long id, [FromBody] AcceptRubricAdditionRequest request, CancellationToken ct)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.AcceptedText))
        {
            return BadRequest(new { error = "The accepted rubric text is required." });
        }

        var question = await _dbContext.BenchmarkQuestions.FindAsync(new object[] { id }, ct);
        if (question == null) return NotFound();

        BenchmarkRubricGapAuthorDraft? draft = null;
        BenchmarkRubricGapAuthorJob? job = null;
        if (!string.IsNullOrWhiteSpace(request.JobId) && !string.IsNullOrWhiteSpace(request.ClusterKey))
        {
            job = _rubricGapAuthorJobManager.TryGet(request.JobId!);
            draft = job?.TryGetDraft(request.ClusterKey!);
            if (draft != null && draft.QuestionId != question.Id)
            {
                return BadRequest(new { error = "That draft belongs to a different question." });
            }
        }

        string acceptedText = request.AcceptedText.Trim();
        string draftText = draft?.ProposedText?.Trim() ?? string.Empty;

        string existing = question.ExpectedPoints ?? string.Empty;
        question.ExpectedPoints = string.IsNullOrWhiteSpace(existing)
            ? acceptedText
            : existing.TrimEnd() + Environment.NewLine + acceptedText;

        // A rubric edit is a content change, so the same clear-and-bump the question editor performs
        // applies here: an edited question is a different item and its statistics must not straddle
        // the rewrite.
        BenchmarkQuestionAssessment.Clear(question);
        question.ModifiedAtUtc = DateTime.UtcNow;

        var suite = await _dbContext.BenchmarkSuites.FindAsync(new object[] { question.BenchmarkSuiteId }, ct);
        if (suite != null) suite.ModifiedAtUtc = DateTime.UtcNow;

        string acceptedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

        var acceptance = new BenchmarkRubricAdditionAcceptance
        {
            BenchmarkQuestionId = question.Id,
            ItemRevisionAfter = question.ItemRevision,
            AcceptedText = acceptedText,
            DraftText = draftText,
            AcceptedVerbatim = draftText.Length > 0 && string.Equals(draftText, acceptedText, StringComparison.Ordinal),
            Citation = draft?.Citation,
            ClusterClaim = draft?.Claims.FirstOrDefault(),
            AuthorModelConfigurationId = job?.AuthorConfigId,
            AuthorProviderUsed = job?.AuthorProviderUsed,
            AuthorModelIdUsed = job?.AuthorModelIdUsed,
            AuthorModelDisplayName = job?.AuthorDisplayName,
            AcceptedByUserId = string.IsNullOrEmpty(acceptedByUserId) ? null : acceptedByUserId,
            AcceptedAtUtc = DateTime.UtcNow
        };
        _dbContext.BenchmarkRubricAdditionAcceptances.Add(acceptance);

        await _dbContext.SaveChangesAsync(ct);

        return Ok(new RubricAdditionAcceptanceDto
        {
            Id = acceptance.Id,
            QuestionId = question.Id,
            QuestionOrderIndex = question.OrderIndex,
            ItemRevisionAfter = acceptance.ItemRevisionAfter,
            AcceptedVerbatim = acceptance.AcceptedVerbatim,
            Citation = acceptance.Citation,
            AuthorModelDisplayName = acceptance.AuthorModelDisplayName,
            AcceptedAtUtc = acceptance.AcceptedAtUtc,
            ExpectedPoints = question.ExpectedPoints
        });
    }

    // --- Runs API ---

    /// <summary>
    /// Starts one benchmark run, or - when <c>RunCount</c> is greater than 1 - a series of them.
    ///
    /// <para>Both paths go through <see cref="BenchmarkRunLauncher.CreateAndLaunchRunAsync"/>, which
    /// owns every validation this endpoint used to perform inline. That extraction is the point: a
    /// series must admit its members under exactly the rules a single run is admitted under, and two
    /// copies of those rules would drift.</para>
    ///
    /// <para><c>RunCount</c> of 1 is the pre-multi-run behaviour exactly: no series row, no
    /// auto-created group, the same <c>{ runId }</c> response body.</para>
    /// </summary>
    [HttpPost("runs")]
    public async Task<IActionResult> StartRun([FromBody] StartBenchmarkRunRequest request)
    {
        string userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

        if (request.RunCount > 1)
        {
            var seriesResult = await _seriesOrchestrator.StartSeriesAsync(request, userId);
            return SeriesResultToActionResult(seriesResult);
        }

        var result = await _runLauncher.CreateAndLaunchRunAsync(request, userId);

        switch (result.Outcome)
        {
            case BenchmarkRunLaunchOutcome.Started:
                return Accepted(new { runId = result.RunId!.Value });

            case BenchmarkRunLaunchOutcome.Conflict:
                return Conflict(result.Error);

            case BenchmarkRunLaunchOutcome.NotFound:
                return NotFound(result.Error);

            case BenchmarkRunLaunchOutcome.SpendDenied:
                return StatusCode(StatusCodes.Status429TooManyRequests, result.Error);

            case BenchmarkRunLaunchOutcome.SameProviderNotAcknowledged:
                return StatusCode(StatusCodes.Status409Conflict, result.SameProviderWarning);

            default:
                return BadRequest(result.Error);
        }
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

        // While a run is running, the run-level totals are 0 because BenchmarkRunFinalizer
        // writes them once at the end. Compute live candidate totals from answers so the progress dialog
        // and mid-run diagnostics report actual progress. The finalizer remains the single writer.
        bool isLiveRun = run.Status is BenchmarkRunStatus.Running;
        var liveCandidateTotals = isLiveRun ? BenchmarkRunFinalizer.ComputeCandidateTotals(run.Answers) : default;

        long totalInputTokens = isLiveRun ? liveCandidateTotals.TotalInputTokens : run.TotalInputTokens;
        long totalOutputTokens = isLiveRun ? liveCandidateTotals.TotalOutputTokens : run.TotalOutputTokens;
        long totalCacheReadTokens = isLiveRun ? liveCandidateTotals.TotalCacheReadTokens : run.TotalCacheReadTokens;
        long totalCacheCreationTokens = isLiveRun ? liveCandidateTotals.TotalCacheCreationTokens : run.TotalCacheCreationTokens;
        long totalAnswerDurationMs = isLiveRun ? liveCandidateTotals.TotalAnswerDurationMs : run.TotalAnswerDurationMs;

        // The long-context portion of the totals above, and the tier the provider actually served. Both are
        // zero / null for a flat-rate model and for every run recorded before tiered pricing existed, so the
        // costing below reduces exactly to the flat-rate arithmetic it replaced.
        var liveLongContextTotals = isLiveRun ? BenchmarkRunFinalizer.ComputeCandidateLongContextTotals(run.Answers) : default;
        long totalLongContextInputTokens = isLiveRun ? liveLongContextTotals.TotalLongContextInputTokens : run.TotalLongContextInputTokens;
        long totalLongContextOutputTokens = isLiveRun ? liveLongContextTotals.TotalLongContextOutputTokens : run.TotalLongContextOutputTokens;
        long totalLongContextCacheReadTokens = isLiveRun ? liveLongContextTotals.TotalLongContextCacheReadTokens : run.TotalLongContextCacheReadTokens;
        long totalLongContextCacheCreationTokens = isLiveRun ? liveLongContextTotals.TotalLongContextCacheCreationTokens : run.TotalLongContextCacheCreationTokens;
        string? servedServiceTier = BenchmarkRunFinalizer.ResolveServedServiceTier(run.Answers);

        BenchmarkRunPricing? pricing = null;
        if (_modelPricingService != null)
        {
            pricing = await _modelPricingService.ResolveForRunAsync(run);
        }

        bool hasAssessor = run.TotalAssessmentInputTokens > 0 || run.TotalAssessmentOutputTokens > 0;
        bool hasVerifier = run.TotalClaimVerificationInputTokens > 0 || run.TotalClaimVerificationOutputTokens > 0;

        var candidatePricing = pricing?.Candidate;
        var assessorPricing = hasAssessor ? pricing?.Assessor : null;
        var verifierPricing = hasVerifier ? pricing?.ClaimVerifier : null;

        bool canEstimateCost = pricing != null &&
            candidatePricing != null &&
            (!hasAssessor || assessorPricing != null) &&
            (!hasVerifier || verifierPricing != null);

        decimal? candidateCost = null;
        decimal? assessorCost = null;
        decimal? verifierCost = null;
        decimal? totalEstimatedCost = null;
        string? pricingSource = null;
        bool pricingIncomplete = !canEstimateCost;

        if (pricing != null)
        {
            if (candidatePricing != null)
            {
                // The candidate is the only role costed from per-call evidence: its long-context portion was
                // bucketed per model call at answer time and persisted, so the surcharge can be reproduced
                // here without re-running anything.
                candidateCost = ModelPricingService.ComputeCostFromTotals(
                    candidatePricing,
                    totalInputTokens, totalOutputTokens, totalCacheReadTokens, totalCacheCreationTokens,
                    totalLongContextInputTokens, totalLongContextOutputTokens,
                    totalLongContextCacheReadTokens, totalLongContextCacheCreationTokens,
                    actualServiceTier: servedServiceTier,
                    requestedServiceTier: run.TestedModelServiceTierUsed);
            }
            // Assessor and verifier are costed flat, from aggregate totals only: no per-call usage is
            // recorded for either role, so neither a long-context card nor a served tier is knowable here.
            if (hasAssessor && assessorPricing != null)
            {
                assessorCost = ModelPricingService.ComputeCost(assessorPricing, run.TotalAssessmentInputTokens, run.TotalAssessmentOutputTokens);
            }
            if (hasVerifier && verifierPricing != null)
            {
                verifierCost = ModelPricingService.ComputeCost(verifierPricing, run.TotalClaimVerificationInputTokens, run.TotalClaimVerificationOutputTokens);
            }

            var sources = new List<ModelPricingSource>();
            if (candidatePricing != null) sources.Add(candidatePricing.Source);
            if (hasAssessor && assessorPricing != null) sources.Add(assessorPricing.Source);
            if (hasVerifier && verifierPricing != null) sources.Add(verifierPricing.Source);

            if (sources.Count > 0)
            {
                if (sources.All(s => s == ModelPricingSource.Custom)) pricingSource = "custom";
                else if (sources.All(s => s == ModelPricingSource.Catalog)) pricingSource = "catalog";
                else pricingSource = "mixed";
            }

            if (canEstimateCost)
            {
                totalEstimatedCost = (candidateCost ?? 0m) + (assessorCost ?? 0m) + (verifierCost ?? 0m);
            }
        }

        // H4. The verifier's own yield: what its dollars actually bought, and what the deterministic
        // token budget (Benchmark:ClaimVerificationInputTokenBudget) stopped it from checking.
        int claimsCheckedCount = run.ClaimsSupportedCount + run.ClaimsRefutedCount + run.ClaimsIndeterminateCount;
        int claimsNotCheckedAnswerCount = run.Answers.Count(a =>
            a.ClaimVerificationError != null &&
            a.ClaimVerificationError.StartsWith(BenchmarkService.ClaimVerificationNotCheckedPrefix, StringComparison.Ordinal));
        decimal? claimVerificationCostPerClaimUsd = (verifierCost.HasValue && claimsCheckedCount > 0)
            ? verifierCost.Value / claimsCheckedCount
            : null;
        double? claimVerificationCostSharePercent = (verifierCost.HasValue && totalEstimatedCost.HasValue && totalEstimatedCost.Value > 0)
            ? (double)(verifierCost.Value / totalEstimatedCost.Value) * 100.0
            : null;

        // H3: one classifier, on the server. The report already reads these figures through
        // BenchmarkChatTransfer; projecting them here is what lets the diagnostics stop keeping a second,
        // hard-coded copy of the tool-name lists that drifted every time a tool was added.
        var toolRouting = BenchmarkChatTransfer.AnalyzeToolRouting(run.Answers.ToList());

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
                    .Where(a => BenchmarkRunFinalizer.CountsTowardQualityIndex(a) && a.QualityScore.HasValue)
                    .Select(a => (a.RawQualityScore ?? a.QualityScore, a.AssessedDifficulty ?? BenchmarkRunFinalizer.FallbackDifficulty(a.Difficulty)))
                    .ToList()),
            UnweightedQualityIndex = run.UnweightedQualityIndex,
            QualityIndexStandardError = run.QualityIndexStandardError,
            SpeedIndex = run.SpeedIndex,
            TotalAnswerDurationMs = totalAnswerDurationMs,
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
            BudgetSaturatedAnswerCount = run.BudgetSaturatedAnswerCount,
            TransportDefectAnswerCount = run.TransportDefectAnswerCount,
            RecoveredAnswerCount = run.RecoveredAnswerCount,
            AdvisoryFlagAnswerCount = run.AdvisoryFlagAnswerCount,
            ScrubbedArtifactAnswerCount = run.ScrubbedArtifactAnswerCount,
            ContestedVerdictAnswerCount = run.ContestedVerdictAnswerCount,
            UnevidencedDeductionAnswerCount = run.UnevidencedDeductionAnswerCount,
            OmissionAsAccuracyAnswerCount = run.OmissionAsAccuracyAnswerCount,
            RefutedClaimAnswerCount = run.RefutedClaimAnswerCount,
            CompletenessOutOfScopeCount = run.Answers.Count(a => a.CompletenessOutOfScope),
            ReadabilityFormOnlyCount = run.Answers.Count(a => a.ReadabilityFormOnly),
            ClaimVerifiedAnswerCount = run.ClaimVerifiedAnswerCount,
            ClaimsSupportedCount = run.ClaimsSupportedCount,
            ClaimsRefutedCount = run.ClaimsRefutedCount,
            ClaimsIndeterminateCount = run.ClaimsIndeterminateCount,
            ClaimsCheckedCount = claimsCheckedCount,
            ClaimsNotCheckedAnswerCount = claimsNotCheckedAnswerCount,
            ClaimVerificationCostPerClaimUsd = claimVerificationCostPerClaimUsd,
            ClaimVerificationCostSharePercent = claimVerificationCostSharePercent,
            ReassessedAnswerCount = run.ReassessedAnswerCount,
            SecondOpinionModeUsed = run.SecondOpinionModeUsed,
            SecondOpinionBlindUsed = run.SecondOpinionBlindUsed,
            SecondOpinionSampleCountUsed = run.SecondOpinionSampleCountUsed,
            SecondOpinionGradedAnswerCount = run.SecondOpinionGradedAnswerCount,
            SecondOpinionMeanAbsDelta = run.SecondOpinionMeanAbsDelta,
            SecondOpinionMeanSignedDelta = run.SecondOpinionMeanSignedDelta,
            SecondOpinionCriticalErrorSplitCount = run.SecondOpinionCriticalErrorSplitCount,
            CandidatePromptOptionsJson = run.CandidatePromptOptionsJson,
            CandidatePromptSourceUsed = run.CandidatePromptSourceUsed,
            CandidateSystemPromptSha256 = run.CandidateSystemPromptSha256,
            ToolGuidesSha256 = run.ToolGuidesSha256,
            KnowledgeBaseHeadSha = run.KnowledgeBaseHeadSha,
            ToolFamilyCounts = toolRouting.FamilyCalls.ToDictionary(
                kv => kv.Key switch
                {
                    BenchmarkToolFamily.SourceCode => "source",
                    BenchmarkToolFamily.Wiki => "wiki",
                    BenchmarkToolFamily.StructuredLookup => "lookup",
                    BenchmarkToolFamily.KnowledgeBase => "knowledgeBase",
                    _ => "other"
                },
                kv => kv.Value),
            ZeroKnowledgeBaseAnswerCount = toolRouting.ZeroKnowledgeBaseAnswerCount,

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
            UnansweredQuestionCount = run.UnansweredQuestionCount,
            TotalQuestionCount = run.TotalQuestionCount,
            PurposeStatementUsed = run.PurposeStatementUsed,
            SameProviderAcknowledged = run.SameProviderAcknowledged,
            AssessmentJson = run.AssessmentJson,
            AssessmentText = run.AssessmentText,
            AssessmentParseFailed = run.AssessmentParseFailed,
            TotalInputTokens = totalInputTokens,
            TotalOutputTokens = totalOutputTokens,
            TotalCacheReadTokens = totalCacheReadTokens,
            TotalCacheCreationTokens = totalCacheCreationTokens,
            TotalDurationMs = run.TotalDurationMs,
            TotalAssessmentInputTokens = run.TotalAssessmentInputTokens,
            TotalAssessmentOutputTokens = run.TotalAssessmentOutputTokens,
            TotalAssessmentDurationMs = run.TotalAssessmentDurationMs,
            TotalClaimVerificationInputTokens = run.TotalClaimVerificationInputTokens,
            TotalClaimVerificationOutputTokens = run.TotalClaimVerificationOutputTokens,
            TotalClaimVerificationDurationMs = run.TotalClaimVerificationDurationMs,
            ErrorMessage = run.ErrorMessage,
            EstimatedCost = totalEstimatedCost,
            EstimatedCandidateCost = candidateCost,
            EstimatedAssessorCost = assessorCost,
            EstimatedVerifierCost = verifierCost,
            PricingSource = pricingSource,
            PricingIncomplete = pricingIncomplete,

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
                InputTokenShare = (a.InputTokens.HasValue && totalInputTokens > 0)
                    ? (double)a.InputTokens.Value / totalInputTokens
                    : null,
                ModelCallCount = a.ModelCallCount,
                ToolCallCount = a.ToolCallCount,
                ToolBudgetExhausted = a.ToolBudgetExhausted,
                ToolCallsBlocked = a.ToolCallsBlocked,
                ToolCallBudgetUsed = a.ToolCallBudgetUsed,
                ToolTimeMs = a.ToolTimeMs,
                ModelTimeMs = a.ModelTimeMs,
                ScrubbedArtifactText = a.ScrubbedArtifactText,
                ScrubbedArtifactCount = a.ScrubbedArtifactCount,
                NarrationBlockCount = a.NarrationBlockCount,
                TerminationReason = a.TerminationReason,
                ProviderFinishReason = a.ProviderFinishReason,
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
                SecondOpinionError = a.SecondOpinionError,
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
                ClaimVerificationRawText = a.ClaimVerificationRawText,
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

        var rows = await query
            .OrderByDescending(r => r.StartedAtUtc)
            .Take(limit)
            .Select(r => new
            {
                Summary = new BenchmarkRunSummaryDto
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
                    QualityIndexStandardError = r.QualityIndexStandardError,
                    SpeedIndex = r.SpeedIndex,
                    TotalAnswerDurationMs = r.TotalAnswerDurationMs,
                    SpeedMeasurementDegraded = r.SpeedMeasurementDegraded,
                    AnsweredQuestionCount = r.AnsweredQuestionCount,
                    UnansweredQuestionCount = r.UnansweredQuestionCount,
                    TotalQuestionCount = r.TotalQuestionCount,
                    DegradedAnswerCount = r.DegradedAnswerCount,
                    ToolStarvedAnswerCount = r.ToolStarvedAnswerCount,
                    BudgetSaturatedAnswerCount = r.BudgetSaturatedAnswerCount,
                    SecondOpinionBlindUsed = r.SecondOpinionBlindUsed,
                    SecondOpinionMeanSignedDelta = r.SecondOpinionMeanSignedDelta,
                    SecondOpinionCriticalErrorSplitCount = r.SecondOpinionCriticalErrorSplitCount,
                    CandidatePromptOptionsJson = r.CandidatePromptOptionsJson,
                    CandidatePromptSourceUsed = r.CandidatePromptSourceUsed,
                    CandidateSystemPromptSha256 = r.CandidateSystemPromptSha256,
                    ToolGuidesSha256 = r.ToolGuidesSha256,
                    KnowledgeBaseHeadSha = r.KnowledgeBaseHeadSha,
                    HarnessVersion = r.HarnessVersion,
                    TotalDurationMs = r.TotalDurationMs
                },
                r.PricingSnapshotJson,
                r.TestedModelConfigurationId,
                r.TestedModelProviderUsed,
                r.TestedModelIdUsed,
                r.AssessorModelConfigurationId,
                r.AssessorModelProviderUsed,
                r.AssessorModelIdUsed,
                r.ClaimVerifierModelConfigurationId,
                r.ClaimVerifierProviderUsed,
                r.ClaimVerifierModelIdUsed,
                r.SecondOpinionAssessorModelConfigurationId,
                r.SecondOpinionAssessorModelProviderUsed,
                r.SecondOpinionAssessorModelIdUsed,
                r.TotalInputTokens,
                r.TotalOutputTokens,
                r.TotalCacheReadTokens,
                r.TotalCacheCreationTokens,
                r.TotalLongContextInputTokens,
                r.TotalLongContextOutputTokens,
                r.TotalLongContextCacheReadTokens,
                r.TotalLongContextCacheCreationTokens,
                r.TestedModelServiceTierUsed,
                // The tier the provider actually served, pulled as a scalar subquery rather than by loading
                // every answer: costing must use the served tier, and the history list has no other access
                // to it. Null for a run whose provider reported none.
                ServedServiceTier = r.Answers
                    .Where(a => a.ActualServiceTierUsed != null)
                    .Select(a => a.ActualServiceTierUsed)
                    .FirstOrDefault(),
                r.TotalAssessmentInputTokens,
                r.TotalAssessmentOutputTokens,
                r.TotalClaimVerificationInputTokens,
                r.TotalClaimVerificationOutputTokens
            })
            .ToListAsync();

        if (_modelPricingService != null)
        {
            foreach (var item in rows)
            {
                var tempRun = new BenchmarkRun
                {
                    PricingSnapshotJson = item.PricingSnapshotJson,
                    TestedModelConfigurationId = item.TestedModelConfigurationId,
                    TestedModelProviderUsed = item.TestedModelProviderUsed,
                    TestedModelIdUsed = item.TestedModelIdUsed,
                    AssessorModelConfigurationId = item.AssessorModelConfigurationId,
                    AssessorModelProviderUsed = item.AssessorModelProviderUsed,
                    AssessorModelIdUsed = item.AssessorModelIdUsed,
                    ClaimVerifierModelConfigurationId = item.ClaimVerifierModelConfigurationId,
                    ClaimVerifierProviderUsed = item.ClaimVerifierProviderUsed,
                    ClaimVerifierModelIdUsed = item.ClaimVerifierModelIdUsed,
                    SecondOpinionAssessorModelConfigurationId = item.SecondOpinionAssessorModelConfigurationId,
                    SecondOpinionAssessorModelProviderUsed = item.SecondOpinionAssessorModelProviderUsed,
                    SecondOpinionAssessorModelIdUsed = item.SecondOpinionAssessorModelIdUsed,
                    TotalInputTokens = item.TotalInputTokens,
                    TotalOutputTokens = item.TotalOutputTokens,
                    TotalCacheReadTokens = item.TotalCacheReadTokens,
                    TotalCacheCreationTokens = item.TotalCacheCreationTokens,
                    TotalLongContextInputTokens = item.TotalLongContextInputTokens,
                    TotalLongContextOutputTokens = item.TotalLongContextOutputTokens,
                    TotalLongContextCacheReadTokens = item.TotalLongContextCacheReadTokens,
                    TotalLongContextCacheCreationTokens = item.TotalLongContextCacheCreationTokens,
                    TestedModelServiceTierUsed = item.TestedModelServiceTierUsed,
                    TotalAssessmentInputTokens = item.TotalAssessmentInputTokens,
                    TotalAssessmentOutputTokens = item.TotalAssessmentOutputTokens,
                    TotalClaimVerificationInputTokens = item.TotalClaimVerificationInputTokens,
                    TotalClaimVerificationOutputTokens = item.TotalClaimVerificationOutputTokens
                };

                var pricing = await _modelPricingService.ResolveForRunAsync(tempRun);

                bool hasAssessor = tempRun.TotalAssessmentInputTokens > 0 || tempRun.TotalAssessmentOutputTokens > 0;
                bool hasVerifier = tempRun.TotalClaimVerificationInputTokens > 0 || tempRun.TotalClaimVerificationOutputTokens > 0;

                var candidatePricing = pricing?.Candidate;
                var assessorPricing = hasAssessor ? pricing?.Assessor : null;
                var verifierPricing = hasVerifier ? pricing?.ClaimVerifier : null;

                bool canEstimateCost = pricing != null &&
                    candidatePricing != null &&
                    (!hasAssessor || assessorPricing != null) &&
                    (!hasVerifier || verifierPricing != null);

                if (!canEstimateCost)
                {
                    item.Summary.PricingIncomplete = true;
                }
                else
                {
                    decimal candCost = ModelPricingService.ComputeCostFromTotals(
                        candidatePricing!,
                        tempRun.TotalInputTokens, tempRun.TotalOutputTokens,
                        tempRun.TotalCacheReadTokens, tempRun.TotalCacheCreationTokens,
                        tempRun.TotalLongContextInputTokens, tempRun.TotalLongContextOutputTokens,
                        tempRun.TotalLongContextCacheReadTokens, tempRun.TotalLongContextCacheCreationTokens,
                        actualServiceTier: item.ServedServiceTier,
                        requestedServiceTier: tempRun.TestedModelServiceTierUsed);
                    decimal assCost = hasAssessor && assessorPricing != null ? ModelPricingService.ComputeCost(assessorPricing, tempRun.TotalAssessmentInputTokens, tempRun.TotalAssessmentOutputTokens) : 0m;
                    decimal verCost = hasVerifier && verifierPricing != null ? ModelPricingService.ComputeCost(verifierPricing, tempRun.TotalClaimVerificationInputTokens, tempRun.TotalClaimVerificationOutputTokens) : 0m;

                    item.Summary.EstimatedCost = candCost + assCost + verCost;
                    item.Summary.PricingIncomplete = false;
                }
            }
        }
        else
        {
            foreach (var item in rows)
            {
                item.Summary.PricingIncomplete = true;
            }
        }

        return Ok(rows.Select(r => r.Summary).ToList());
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

        if (run.Status is BenchmarkRunStatus.Canceled or BenchmarkRunStatus.Failed)
        {
            return BadRequest(BenchmarkService.AbortedRunRefusal);
        }

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

        if (run.Status is BenchmarkRunStatus.Canceled or BenchmarkRunStatus.Failed)
        {
            return BadRequest(BenchmarkService.AbortedRunRefusal);
        }

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

        if (run.Status is BenchmarkRunStatus.Canceled or BenchmarkRunStatus.Failed)
        {
            return BadRequest(BenchmarkService.AbortedRunRefusal);
        }

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

        if (run.Status is BenchmarkRunStatus.Canceled or BenchmarkRunStatus.Failed)
        {
            return BadRequest(BenchmarkService.AbortedRunRefusal);
        }

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

    [HttpPost("runs/{id}/retry-claim-verification")]
    public async Task<IActionResult> RetryClaimVerification(long id, [FromBody] BenchmarkRetryRequest? request)
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

        if (run.Status is BenchmarkRunStatus.Canceled or BenchmarkRunStatus.Failed)
        {
            return BadRequest(BenchmarkService.AbortedRunRefusal);
        }

        if (!run.Answers.Any(a => !string.IsNullOrWhiteSpace(a.ClaimVerificationError)))
        {
            return BadRequest("This run has no failed claim verifications to retry.");
        }

        long? targetVerifierId = request?.AssessorModelConfigurationId ?? run.ClaimVerifierModelConfigurationId;
        var (verifierValid, verifierError) = await ValidateAssessorConfigurationAsync(targetVerifierId);
        if (!verifierValid)
        {
            return BadRequest(verifierError);
        }

        var cts = new CancellationTokenSource();
        if (!_runManager.TryStart(run.Id, cts, out _))
        {
            return Conflict("A benchmark run is already in progress.");
        }

        _ = Task.Run(() => _benchmarkService.RetryFailedClaimVerificationAsync(id, targetVerifierId, cts.Token));
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

            // No live run to cancel means the row is orphaned and its own abort path will never run, so
            // what it consumed is recorded here instead. A live run measures its own wall clock, which
            // is why this is not done unconditionally.
            if (!cancelled)
            {
                var answers = await _dbContext.BenchmarkRunAnswers
                    .Where(a => a.BenchmarkRunId == run.Id)
                    .ToListAsync();

                BenchmarkRunFinalizer.ApplyTotals(run, answers);
                run.TotalDurationMs =
                    (long)(run.CompletedAtUtc.Value - run.StartedAtUtc).TotalMilliseconds;
            }

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

        if (run.Status is BenchmarkRunStatus.Canceled or BenchmarkRunStatus.Failed)
        {
            return BadRequest(BenchmarkService.AbortedRunRefusal);
        }

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
        BenchmarkRunPricing? runPricing = _modelPricingService != null
            ? await _modelPricingService.ResolveForRunAsync(run)
            : null;
        string markdown = BenchmarkReportBuilder.BuildMarkdownReport(run, GetOverseerVersion(), runPricing);
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

    // =======================================================================================
    // Multi-run: limits, series, groups and group analysis
    // =======================================================================================

    /// <summary>
    /// The run caps and the live rolling-window counts behind them.
    ///
    /// <para>Exists because the caps were not exposed at any endpoint, so the start dialog's
    /// run-count field had nothing to bound itself by. The arithmetic is
    /// <see cref="BenchmarkComplianceGuard"/>'s alone — this action must never re-derive the window,
    /// or the client would disagree with the guard that actually refuses the run.</para>
    /// </summary>
    [HttpGet("runs/limits")]
    public async Task<IActionResult> GetRunLimits()
    {
        var limits = await _complianceGuard.GetLimitsAsync();

        return Ok(new BenchmarkRunLimitsDto
        {
            MaxRunsPerHour = limits.MaxRunsPerHour,
            MaxRunsPerDay = limits.MaxRunsPerDay,
            RunsInLastHour = limits.RunsInLastHour,
            RunsInLast24Hours = limits.RunsInLast24Hours,
            RemainingDailyHeadroom = limits.RemainingDailyHeadroom,

            // The ceiling on a series is the daily cap itself, not the current headroom: a series
            // launched from an empty window of exactly MaxRunsPerDay members passes, because the
            // guard tests the count *before* creating each run and the last member sees Max - 1.
            MaxRunCountPerSeries = limits.MaxRunsPerDay
        });
    }

    [HttpPost("runs/series")]
    public async Task<IActionResult> StartRunSeries([FromBody] StartBenchmarkRunRequest request)
    {
        string userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var result = await _seriesOrchestrator.StartSeriesAsync(request, userId);
        return SeriesResultToActionResult(result);
    }

    [HttpGet("runs/series/{id}")]
    public async Task<IActionResult> GetRunSeries(long id)
    {
        var dto = await BuildSeriesDtoAsync(id);
        return dto == null ? NotFound() : Ok(dto);
    }

    /// <summary>
    /// The series this process is driving, or the most recent one that is still resumable. Returns
    /// <c>204 No Content</c> when there is nothing to show, so the client can poll it cheaply.
    /// </summary>
    [HttpGet("runs/series/active")]
    public async Task<IActionResult> GetActiveRunSeries()
    {
        long? id = _seriesOrchestrator.ActiveSeriesId;

        if (id == null)
        {
            id = await _dbContext.BenchmarkRunSeries
                .Where(s => s.Status == BenchmarkRunSeriesStatus.Running
                            || s.Status == BenchmarkRunSeriesStatus.WaitingForCap
                            || s.Status == BenchmarkRunSeriesStatus.Pending
                            || s.Status == BenchmarkRunSeriesStatus.Stopped)
                .OrderByDescending(s => s.StartedAtUtc)
                .Select(s => (long?)s.Id)
                .FirstOrDefaultAsync();
        }

        if (id == null) return NoContent();

        var dto = await BuildSeriesDtoAsync(id.Value);
        return dto == null ? NoContent() : Ok(dto);
    }

    [HttpPost("runs/series/{id}/cancel")]
    public async Task<IActionResult> CancelRunSeries(long id)
    {
        bool cancelled = await _seriesOrchestrator.CancelSeriesAsync(id);
        return cancelled ? Ok() : NotFound();
    }

    [HttpPost("runs/series/{id}/resume")]
    public async Task<IActionResult> ResumeRunSeries(long id, [FromBody] ResumeBenchmarkRunSeriesRequest? request)
    {
        var result = await _seriesOrchestrator.ResumeSeriesAsync(
            id, request?.AcknowledgeInstrumentChange ?? false);

        return SeriesResultToActionResult(result);
    }

    /// <summary>
    /// One mapping from the orchestrator's outcome vocabulary to HTTP, so start and resume cannot
    /// answer differently for the same condition.
    /// </summary>
    private IActionResult SeriesResultToActionResult(BenchmarkSeriesStartResult result)
    {
        switch (result.Outcome)
        {
            case BenchmarkSeriesStartOutcome.Started:
                return Accepted(new { seriesId = result.SeriesId!.Value });

            case BenchmarkSeriesStartOutcome.Conflict:
                return Conflict(result.Error);

            case BenchmarkSeriesStartOutcome.NotFound:
                return NotFound(result.Error);

            case BenchmarkSeriesStartOutcome.SpendDenied:
                return StatusCode(StatusCodes.Status429TooManyRequests, result.Error);

            case BenchmarkSeriesStartOutcome.SameProviderNotAcknowledged:
                return StatusCode(StatusCodes.Status409Conflict, result.SameProviderWarning);

            case BenchmarkSeriesStartOutcome.InstrumentChanged:
                // 409, not 400: the request is well-formed and the operator may legitimately confirm
                // through it with acknowledgeInstrumentChange, exactly as with the same-provider warning.
                return StatusCode(StatusCodes.Status409Conflict, new
                {
                    instrumentChanged = true,
                    seriesId = result.SeriesId,
                    changedHashes = result.ChangedInstrumentHashes,
                    message = result.Error
                });

            default:
                return BadRequest(result.Error);
        }
    }

    private async Task<BenchmarkRunSeriesDto?> BuildSeriesDtoAsync(long id)
    {
        var series = await _dbContext.BenchmarkRunSeries.FirstOrDefaultAsync(s => s.Id == id);
        if (series == null) return null;

        var members = await _dbContext.BenchmarkRuns
            .Where(r => r.RunSeriesId == id)
            .OrderBy(r => r.RunSeriesIndex)
            .ToListAsync();

        var memberIds = members.Select(m => m.Id).ToList();
        var answeredCounts = await _dbContext.BenchmarkRunAnswers
            .Where(a => memberIds.Contains(a.BenchmarkRunId))
            .GroupBy(a => a.BenchmarkRunId)
            .Select(g => new { RunId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.RunId, x => x.Count);

        // The served tier, not the requested one, is what costing needs — a priority request served
        // as default must be billed as default. Fetched for every member in one query rather than
        // loading each member's answers, because this endpoint is polled while a series runs.
        var servedTiers = (await _dbContext.BenchmarkRunAnswers
                .Where(a => memberIds.Contains(a.BenchmarkRunId) && a.ActualServiceTierUsed != null)
                .Select(a => new { a.BenchmarkRunId, a.ActualServiceTierUsed })
                .ToListAsync())
            .GroupBy(a => a.BenchmarkRunId)
            .ToDictionary(
                g => g.Key,
                g => g.GroupBy(x => x.ActualServiceTierUsed!, StringComparer.OrdinalIgnoreCase)
                      .OrderByDescending(x => x.Count())
                      .Select(x => x.Key)
                      .FirstOrDefault());

        var dto = new BenchmarkRunSeriesDto
        {
            Id = series.Id,
            BenchmarkSuiteId = series.BenchmarkSuiteId,
            SuiteName = series.SuiteName,
            RequestedRunCount = series.RequestedRunCount,
            CompletedRunCount = series.CompletedRunCount,
            FailedRunCount = series.FailedRunCount,
            Status = series.Status.ToString(),
            StopReason = series.StopReason?.ToString(),
            StopReasonText = DescribeStopReason(series.StopReason),
            AllowCapWait = series.AllowCapWait,

            // Cancelled, Completed and Failed are never resumable; the first by the operator's own
            // decision, the others because there is nothing left to launch.
            Resumable = (series.Status == BenchmarkRunSeriesStatus.Stopped
                         || series.Status == BenchmarkRunSeriesStatus.CompletedWithErrors)
                        && series.CompletedRunCount < series.RequestedRunCount,

            StartedAtUtc = series.StartedAtUtc,
            CompletedAtUtc = series.CompletedAtUtc,
            ErrorMessage = series.ErrorMessage,

            FirstMemberCandidateSystemPromptSha256 = series.FirstMemberCandidateSystemPromptSha256,
            FirstMemberToolGuidesSha256 = series.FirstMemberToolGuidesSha256,
            FirstMemberKnowledgeBaseHeadSha = series.FirstMemberKnowledgeBaseHeadSha,
            InstrumentChangeAcknowledged = series.InstrumentChangeAcknowledged,
            AutoCreatedGroupId = series.AutoCreatedGroupId
        };

        // The current fingerprint, so a refused resume is self-explaining in the diagnostics capture
        // rather than requiring the operator to work out what moved.
        var request = BenchmarkSeriesOrchestrator.DeserializeRequest(series);
        if (request != null)
        {
            var current = await _benchmarkService.ComputeCurrentInstrumentFingerprintAsync(
                _dbContext, request.SuiteId, request.TestedModelConfigurationId, request.VerboseMode ?? false);

            if (current != null)
            {
                dto.CurrentCandidateSystemPromptSha256 = current.Value.CandidateSystemPromptSha256;
                dto.CurrentToolGuidesSha256 = current.Value.ToolGuidesSha256;
                dto.CurrentKnowledgeBaseHeadSha = current.Value.KnowledgeBaseHeadSha;

                void Compare(string name, string? recorded, string? now)
                {
                    if (string.IsNullOrEmpty(recorded)) return;
                    if (!string.Equals(recorded, now, StringComparison.OrdinalIgnoreCase))
                    {
                        dto.ChangedInstrumentHashes.Add(name);
                    }
                }

                Compare("CandidateSystemPromptSha256", series.FirstMemberCandidateSystemPromptSha256, current.Value.CandidateSystemPromptSha256);
                Compare("ToolGuidesSha256", series.FirstMemberToolGuidesSha256, current.Value.ToolGuidesSha256);
                Compare("KnowledgeBaseHeadSha", series.FirstMemberKnowledgeBaseHeadSha, current.Value.KnowledgeBaseHeadSha);
            }
        }

        if (series.AutoCreatedGroupId.HasValue)
        {
            var tier = await _dbContext.BenchmarkRunGroups
                .Where(g => g.Id == series.AutoCreatedGroupId.Value)
                .Select(g => (BenchmarkRunGroupTier?)g.Tier)
                .FirstOrDefaultAsync();
            dto.AutoCreatedGroupTier = tier?.ToString();
        }

        foreach (var run in members)
        {
            dto.Members.Add(new BenchmarkRunSeriesMemberDto
            {
                Index = run.RunSeriesIndex ?? 0,
                RunId = run.Id,
                Status = run.Status.ToString(),
                StartedAtUtc = run.StartedAtUtc,
                CompletedAtUtc = run.CompletedAtUtc,
                QualityIndex = run.QualityIndex,
                SpeedIndex = run.SpeedIndex,
                EstimatedCost = (double?)await EstimateRunCostAsync(
                    run, servedTiers.TryGetValue(run.Id, out var tier) ? tier : null),
                DurationMs = run.CompletedAtUtc.HasValue
                    ? (long)(run.CompletedAtUtc.Value - run.StartedAtUtc).TotalMilliseconds
                    : null,
                AnsweredQuestionCount = answeredCounts.TryGetValue(run.Id, out int answered) ? answered : 0,
                TotalQuestionCount = run.TotalQuestionCount,
                ShortFingerprint = ShortFingerprint(run.CandidateSystemPromptSha256)
            });
        }

        return dto;
    }

    /// <summary>
    /// A run's total estimated cost across all three roles, or null when pricing is unavailable for
    /// any role that actually spent tokens. Null means "not known", never "free" — a partial figure
    /// presented as a total is worse than no figure.
    /// </summary>
    private async Task<decimal?> EstimateRunCostAsync(BenchmarkRun run, string? servedServiceTier)
    {
        if (_modelPricingService == null) return null;

        var pricing = await _modelPricingService.ResolveForRunAsync(run);
        if (pricing?.Candidate == null) return null;

        bool hasAssessor = run.TotalAssessmentInputTokens > 0 || run.TotalAssessmentOutputTokens > 0;
        bool hasVerifier = run.TotalClaimVerificationInputTokens > 0 || run.TotalClaimVerificationOutputTokens > 0;

        if (hasAssessor && pricing.Assessor == null) return null;
        if (hasVerifier && pricing.ClaimVerifier == null) return null;

        decimal candidateCost = ModelPricingService.ComputeCostFromTotals(
            pricing.Candidate,
            run.TotalInputTokens, run.TotalOutputTokens,
            run.TotalCacheReadTokens, run.TotalCacheCreationTokens,
            run.TotalLongContextInputTokens, run.TotalLongContextOutputTokens,
            run.TotalLongContextCacheReadTokens, run.TotalLongContextCacheCreationTokens,
            actualServiceTier: servedServiceTier,
            requestedServiceTier: run.TestedModelServiceTierUsed);

        decimal assessorCost = hasAssessor
            ? ModelPricingService.ComputeCost(pricing.Assessor!, run.TotalAssessmentInputTokens, run.TotalAssessmentOutputTokens)
            : 0m;

        decimal verifierCost = hasVerifier
            ? ModelPricingService.ComputeCost(pricing.ClaimVerifier!, run.TotalClaimVerificationInputTokens, run.TotalClaimVerificationOutputTokens)
            : 0m;

        return candidateCost + assessorCost + verifierCost;
    }

    private static string? DescribeStopReason(BenchmarkRunSeriesStopReason? reason) => reason switch
    {
        BenchmarkRunSeriesStopReason.MemberFailed => "A member run failed",
        BenchmarkRunSeriesStopReason.RunCapReached => "Run cap reached",
        BenchmarkRunSeriesStopReason.SpendDenied => "Spend guard denied the next run",
        _ => null
    };

    private static string? ShortFingerprint(string? sha) =>
        string.IsNullOrEmpty(sha) ? null : sha.Length <= 8 ? sha : sha.Substring(0, 8);

    // --- Groups -----------------------------------------------------------------------------

    [HttpGet("runs/groups")]
    public async Task<IActionResult> GetRunGroups()
    {
        var groups = await _dbContext.BenchmarkRunGroups
            .Include(g => g.Members)
            .Include(g => g.BenchmarkSuite)
            .OrderByDescending(g => g.CreatedAtUtc)
            .ToListAsync();

        var dtos = new List<BenchmarkRunGroupDto>(groups.Count);
        foreach (var group in groups)
        {
            dtos.Add(await BuildGroupDtoAsync(group, includeMembers: false));
        }

        return Ok(dtos);
    }

    [HttpGet("runs/groups/{id}")]
    public async Task<IActionResult> GetRunGroup(long id)
    {
        var group = await _dbContext.BenchmarkRunGroups
            .Include(g => g.Members)
            .Include(g => g.BenchmarkSuite)
            .FirstOrDefaultAsync(g => g.Id == id);

        if (group == null) return NotFound();

        return Ok(await BuildGroupDtoAsync(group, includeMembers: true));
    }

    /// <summary>
    /// Creates an analysis group, resolving its tier from the runs themselves.
    ///
    /// <para>Refuses to persist a set below Tier B and names the differing keys, and permits Tier C
    /// only when the caller sets <c>crossCondition</c> explicitly. Those two rules are what keep a
    /// pooled index from ever being computed over runs that were never comparable — the worst
    /// failure this feature can have — and they live here rather than in the UI because the UI is
    /// not the thing that must not be bypassed.</para>
    /// </summary>
    [HttpPost("runs/groups")]
    public async Task<IActionResult> CreateRunGroup([FromBody] CreateBenchmarkRunGroupRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest("A group needs a name.");
        }

        var (runs, error) = await LoadGroupCandidateRunsAsync(request.RunIds);
        if (error != null) return BadRequest(error);

        var comparability = BenchmarkComparabilityKey.Resolve(runs);
        var refusal = RefuseGroupTier(comparability, request.CrossCondition);
        if (refusal != null) return refusal;

        string userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

        var group = new BenchmarkRunGroup
        {
            Name = request.Name.Trim(),
            BenchmarkSuiteId = runs[0].BenchmarkSuiteId,
            Tier = (BenchmarkRunGroupTier)(int)comparability.Tier,
            ComparabilityKeyHash = comparability.ComparabilityKeyHash,
            TierReasonsJson = BenchmarkGroupAnalysisService.SerialiseTierReasons(comparability),
            CrossCondition = request.CrossCondition,
            Notes = request.Notes,
            CreatedByUserId = string.IsNullOrEmpty(userId) ? null : userId,
            CreatedAtUtc = DateTime.UtcNow,
            ModifiedAtUtc = DateTime.UtcNow
        };

        _dbContext.BenchmarkRunGroups.Add(group);
        await _dbContext.SaveChangesAsync();

        foreach (var run in runs)
        {
            _dbContext.BenchmarkRunGroupMembers.Add(new BenchmarkRunGroupMember
            {
                BenchmarkRunGroupId = group.Id,
                BenchmarkRunId = run.Id,
                AddedAtUtc = DateTime.UtcNow
            });
        }

        await _dbContext.SaveChangesAsync();

        await _dbContext.Entry(group).Collection(g => g.Members).LoadAsync();

        return Ok(new BenchmarkRunGroupTierPreviewDto
        {
            Accepted = true,
            Comparability = ToComparabilityDto(comparability),
            Group = await BuildGroupDtoAsync(group, includeMembers: true)
        });
    }

    [HttpPut("runs/groups/{id}")]
    public async Task<IActionResult> UpdateRunGroup(long id, [FromBody] UpdateBenchmarkRunGroupRequest request)
    {
        var group = await _dbContext.BenchmarkRunGroups
            .Include(g => g.Members)
            .FirstOrDefaultAsync(g => g.Id == id);

        if (group == null) return NotFound();

        if (request.Name != null)
        {
            if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest("A group needs a name.");
            group.Name = request.Name.Trim();
        }

        if (request.Notes != null) group.Notes = request.Notes;

        bool crossCondition = request.CrossCondition ?? group.CrossCondition;

        if (request.RunIds != null)
        {
            var (runs, error) = await LoadGroupCandidateRunsAsync(request.RunIds);
            if (error != null) return BadRequest(error);

            var comparability = BenchmarkComparabilityKey.Resolve(runs);
            var refusal = RefuseGroupTier(comparability, crossCondition);
            if (refusal != null) return refusal;

            _dbContext.BenchmarkRunGroupMembers.RemoveRange(group.Members);
            await _dbContext.SaveChangesAsync();

            foreach (var run in runs)
            {
                _dbContext.BenchmarkRunGroupMembers.Add(new BenchmarkRunGroupMember
                {
                    BenchmarkRunGroupId = group.Id,
                    BenchmarkRunId = run.Id,
                    AddedAtUtc = DateTime.UtcNow
                });
            }

            group.BenchmarkSuiteId = runs[0].BenchmarkSuiteId;
            group.Tier = (BenchmarkRunGroupTier)(int)comparability.Tier;
            group.ComparabilityKeyHash = comparability.ComparabilityKeyHash;
            group.TierReasonsJson = BenchmarkGroupAnalysisService.SerialiseTierReasons(comparability);
        }

        group.CrossCondition = crossCondition;
        group.ModifiedAtUtc = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        await _dbContext.Entry(group).Collection(g => g.Members).LoadAsync();

        return Ok(new BenchmarkRunGroupTierPreviewDto
        {
            Accepted = true,
            Group = await BuildGroupDtoAsync(group, includeMembers: true)
        });
    }

    [HttpDelete("runs/groups/{id}")]
    public async Task<IActionResult> DeleteRunGroup(long id)
    {
        var group = await _dbContext.BenchmarkRunGroups.FirstOrDefaultAsync(g => g.Id == id);
        if (group == null) return NotFound();

        // Members and analyses cascade; the runs themselves are untouched, which is the whole point
        // of the membership being a separate row.
        _dbContext.BenchmarkRunGroups.Remove(group);
        await _dbContext.SaveChangesAsync();
        return Ok();
    }

    /// <summary>
    /// The tier a set of runs would resolve to, without creating anything. This is what the group
    /// builder shows while the operator is still selecting runs.
    /// </summary>
    [HttpPost("runs/groups/preview")]
    public async Task<IActionResult> PreviewRunGroupTier([FromBody] CreateBenchmarkRunGroupRequest request)
    {
        var (runs, error) = await LoadGroupCandidateRunsAsync(request.RunIds);
        if (error != null)
        {
            return Ok(new BenchmarkRunGroupTierPreviewDto { Accepted = false, Error = error });
        }

        var comparability = BenchmarkComparabilityKey.Resolve(runs);
        var refusal = RefuseGroupTier(comparability, request.CrossCondition);

        return Ok(new BenchmarkRunGroupTierPreviewDto
        {
            Accepted = refusal == null,
            Error = refusal == null ? null : GroupTierRefusalMessage(comparability, request.CrossCondition),
            Comparability = ToComparabilityDto(comparability)
        });
    }

    private async Task<(List<BenchmarkRun> Runs, string? Error)> LoadGroupCandidateRunsAsync(List<long> runIds)
    {
        var distinct = (runIds ?? new List<long>()).Distinct().ToList();

        if (distinct.Count < 2)
        {
            return (new List<BenchmarkRun>(), "A group needs at least two runs.");
        }

        var runs = await _dbContext.BenchmarkRuns
            .Where(r => distinct.Contains(r.Id))
            .OrderBy(r => r.StartedAtUtc)
            .ToListAsync();

        if (runs.Count != distinct.Count)
        {
            var missing = distinct.Except(runs.Select(r => r.Id)).ToList();
            return (runs, $"Run(s) not found: {string.Join(", ", missing)}.");
        }

        return (runs, null);
    }

    /// <summary>
    /// Null when the set may be persisted at the tier it resolved to; otherwise the refusal, with
    /// the differing keys named. A "no" with no reason is unusable in the group builder.
    /// </summary>
    private IActionResult? RefuseGroupTier(BenchmarkComparabilityResult comparability, bool crossCondition)
    {
        if (comparability.Tier == BenchmarkComparabilityTier.NotComparable
            || (comparability.Tier == BenchmarkComparabilityTier.CrossCondition && !crossCondition))
        {
            return BadRequest(new BenchmarkRunGroupTierPreviewDto
            {
                Accepted = false,
                Error = GroupTierRefusalMessage(comparability, crossCondition),
                Comparability = ToComparabilityDto(comparability)
            });
        }

        return null;
    }

    private static string GroupTierRefusalMessage(BenchmarkComparabilityResult comparability, bool crossCondition)
    {
        string differing = comparability.Differences.Count == 0
            ? "no key was identified as differing"
            : string.Join("; ", comparability.Differences.Select(d => d.Describe()));

        if (comparability.Tier == BenchmarkComparabilityTier.CrossCondition && !crossCondition)
        {
            return "These runs differ on exactly one instrument key, which makes them a cross-condition " +
                   "comparison (Tier C) rather than a replicate set. A Tier C group is never pooled into one " +
                   "index. Set crossCondition to create it as a comparison. Differing: " + differing + ".";
        }

        return "These runs are not comparable, so no aggregate over them would mean anything. " +
               "Differing: " + differing + ".";
    }

    private static BenchmarkComparabilityResultDto ToComparabilityDto(BenchmarkComparabilityResult r) => new()
    {
        Tier = r.Tier.ToString(),
        TierLabel = DescribeTier(r.Tier),
        PoolingPermitted = r.PoolingPermitted,
        SpeedAggregatesDegraded = r.SpeedAggregatesDegraded,
        CostAggregatesDegraded = r.CostAggregatesDegraded,
        Explanation = r.Explanation,
        ComparabilityKeyHash = r.ComparabilityKeyHash,
        MatchedKeys = r.MatchedKeys.ToList(),
        RunIds = r.RunIds.ToList(),
        Differences = r.Differences.Select(d => new BenchmarkComparabilityDifferenceDto
        {
            Name = d.Name,
            Kind = d.Kind.ToString(),
            Description = d.Describe(),
            Variants = d.Variants.Select(v => new BenchmarkComparabilityVariantDto
            {
                Value = v.Value,
                RunIds = v.RunIds.ToList()
            }).ToList()
        }).ToList()
    };

    private static string DescribeTier(BenchmarkComparabilityTier tier) => tier switch
    {
        BenchmarkComparabilityTier.Replicate => "Tier A — Replicate",
        BenchmarkComparabilityTier.QualityComparable => "Tier B — Quality-comparable",
        BenchmarkComparabilityTier.CrossCondition => "Tier C — Cross-condition",
        _ => "Not comparable"
    };

    private static string DescribeTier(BenchmarkRunGroupTier tier) =>
        DescribeTier((BenchmarkComparabilityTier)(int)tier);

    private async Task<BenchmarkRunGroupDto> BuildGroupDtoAsync(BenchmarkRunGroup group, bool includeMembers)
    {
        var latest = await _groupAnalysisService.GetLatestAnalysisAsync(group.Id);

        var dto = new BenchmarkRunGroupDto
        {
            Id = group.Id,
            Name = group.Name,
            BenchmarkSuiteId = group.BenchmarkSuiteId,
            SuiteName = group.BenchmarkSuite?.Name,
            Tier = group.Tier.ToString(),
            TierLabel = DescribeTier(group.Tier),
            ComparabilityKeyHash = group.ComparabilityKeyHash,
            CrossCondition = group.CrossCondition,
            Notes = group.Notes,
            CreatedFromSeriesId = group.CreatedFromSeriesId,
            CreatedAtUtc = group.CreatedAtUtc,
            ModifiedAtUtc = group.ModifiedAtUtc,
            RunCount = group.Members.Count,
            LatestAnalysisId = latest?.Id,
            LatestAnalysisAtUtc = latest?.ComputedAtUtc,
            AnalysisStale = BenchmarkGroupAnalysisService.IsStale(group, latest)
        };

        if (includeMembers && group.Members.Count > 0)
        {
            var memberIds = group.Members.Select(m => m.BenchmarkRunId).ToList();
            var runs = await _dbContext.BenchmarkRuns
                .Where(r => memberIds.Contains(r.Id))
                .ToListAsync();

            foreach (var member in group.Members.OrderBy(m => m.AddedAtUtc))
            {
                var run = runs.FirstOrDefault(r => r.Id == member.BenchmarkRunId);
                if (run == null) continue;

                dto.Members.Add(new BenchmarkRunGroupMemberDto
                {
                    RunId = run.Id,
                    StartedAtUtc = run.StartedAtUtc,
                    Status = run.Status.ToString(),
                    QualityIndex = run.QualityIndex,
                    SpeedIndex = run.SpeedIndex,
                    TestedModelDisplayName = run.TestedModelDisplayNameUsed,
                    ShortFingerprint = ShortFingerprint(run.CandidateSystemPromptSha256),
                    AddedAtUtc = member.AddedAtUtc
                });
            }
        }

        return dto;
    }

    // --- Group analysis ---------------------------------------------------------------------

    [HttpPost("runs/groups/{id}/analysis")]
    public async Task<IActionResult> AnalyseRunGroup(long id, [FromBody] BenchmarkGroupAnalysisRequest? request)
    {
        string userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

        var (analysis, _, error) = await _groupAnalysisService.AnalyseAsync(
            id, string.IsNullOrEmpty(userId) ? null : userId, request?.CompareWithGroupId);

        if (analysis == null)
        {
            return BadRequest(error ?? "The group could not be analysed.");
        }

        var dto = await BuildAnalysisDtoAsync(analysis);
        return dto == null ? NotFound() : Ok(dto);
    }

    [HttpGet("runs/groups/{id}/analysis")]
    public async Task<IActionResult> GetRunGroupAnalysis(long id)
    {
        var analysis = await _groupAnalysisService.GetLatestAnalysisAsync(id);
        if (analysis == null) return NoContent();

        var dto = await BuildAnalysisDtoAsync(analysis);
        return dto == null ? NoContent() : Ok(dto);
    }

    private async Task<BenchmarkGroupAnalysisDto?> BuildAnalysisDtoAsync(BenchmarkGroupAnalysis analysis)
    {
        var group = await _dbContext.BenchmarkRunGroups
            .Include(g => g.Members)
            .FirstOrDefaultAsync(g => g.Id == analysis.BenchmarkRunGroupId);

        if (group == null) return null;

        long[] memberRunIds = DeserialiseMemberRunIds(analysis);

        string? comparedName = null;
        if (analysis.ComparedWithGroupId.HasValue)
        {
            comparedName = await _dbContext.BenchmarkRunGroups
                .Where(g => g.Id == analysis.ComparedWithGroupId.Value)
                .Select(g => g.Name)
                .FirstOrDefaultAsync();
        }

        return new BenchmarkGroupAnalysisDto
        {
            Id = analysis.Id,
            GroupId = group.Id,
            GroupName = group.Name,
            ComputedAtUtc = analysis.ComputedAtUtc,
            RunCount = memberRunIds.Length,
            MemberRunIds = memberRunIds.ToList(),
            Tier = group.Tier.ToString(),
            TierLabel = DescribeTier(group.Tier),
            HarnessVersion = analysis.HarnessVersion,
            ScoringMethodVersion = analysis.ScoringMethodVersion,
            Stale = BenchmarkGroupAnalysisService.IsStale(group, analysis),
            ComparedWithGroupId = analysis.ComparedWithGroupId,
            ComparedWithGroupName = comparedName,
            Result = BenchmarkGroupAnalysisService.DeserialiseResult(analysis),
            Comparison = DeserialiseComparison(analysis)
        };
    }

    private static long[] DeserialiseMemberRunIds(BenchmarkGroupAnalysis analysis)
    {
        try
        {
            return JsonSerializer.Deserialize<long[]>(analysis.MemberRunIdsJson) ?? Array.Empty<long>();
        }
        catch (JsonException)
        {
            return Array.Empty<long>();
        }
    }

    private static BenchmarkGroupComparison? DeserialiseComparison(BenchmarkGroupAnalysis analysis)
    {
        if (string.IsNullOrWhiteSpace(analysis.ComparisonJson)) return null;
        try
        {
            return JsonSerializer.Deserialize<BenchmarkGroupComparison>(analysis.ComparisonJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The multi-run Markdown report, mirroring <c>GET runs/{id}/report</c> exactly — same content
    /// type, same sanitized-filename treatment, same real Overseer version rather than a hard-coded
    /// fallback.
    ///
    /// <para>Built from the <b>persisted analysis</b>, not recomputed: the stored result records the
    /// member run ids it covered, so the report stays reproducible after a run is deleted or the
    /// group's membership changes.</para>
    /// </summary>
    [HttpGet("runs/groups/{id}/report")]
    public async Task<IActionResult> GetRunGroupReport(long id)
    {
        var group = await _dbContext.BenchmarkRunGroups
            .Include(g => g.Members)
            .Include(g => g.BenchmarkSuite)
            .FirstOrDefaultAsync(g => g.Id == id);

        if (group == null) return NotFound();

        var analysis = await _groupAnalysisService.GetLatestAnalysisAsync(id);
        if (analysis == null)
        {
            return BadRequest("This group has no analysis yet. Run the analysis before downloading a report.");
        }

        var result = BenchmarkGroupAnalysisService.DeserialiseResult(analysis);
        if (result == null)
        {
            return BadRequest("The stored analysis could not be read, so no report can be produced.");
        }

        long[] memberRunIds = DeserialiseMemberRunIds(analysis);

        var members = await _dbContext.BenchmarkRuns
            .Where(r => memberRunIds.Contains(r.Id))
            .OrderBy(r => r.StartedAtUtc)
            .ToListAsync();

        BenchmarkComparabilityResult? comparability = members.Count >= 2
            ? BenchmarkComparabilityKey.Resolve(members)
            : null;

        BenchmarkGroupComparison? comparison = DeserialiseComparison(analysis);
        string? comparisonGroupName = null;
        if (comparison != null && analysis.ComparedWithGroupId.HasValue)
        {
            comparisonGroupName = await _dbContext.BenchmarkRunGroups
                .Where(g => g.Id == analysis.ComparedWithGroupId.Value)
                .Select(g => g.Name)
                .FirstOrDefaultAsync();
        }

        string markdown = BenchmarkGroupReportBuilder.BuildMarkdownReport(
            group, result, comparability, members, comparison, comparisonGroupName,
            GetOverseerVersion(), analysis.ComputedAtUtc);

        string suiteName = group.BenchmarkSuite?.Name ?? "benchmark";
        string modelName = members.FirstOrDefault()?.TestedModelDisplayNameUsed ?? "model";

        string filename =
            $"{SanitizeFilename(suiteName)}_{SanitizeFilename(modelName)}_multirun_R{members.Count}_" +
            $"{analysis.ComputedAtUtc:yyyyMMdd_HHmmss}.md";

        return File(Encoding.UTF8.GetBytes(markdown), "text/markdown; charset=utf-8", filename);
    }

    // --- Cross-model comparison ----------------------------------------------------------------

    /// <summary>
    /// One point per candidate model — quality, speed and cost — over the runs and groups named in
    /// the query. Read-only arithmetic over stored data, including the re-pricing, so nothing here
    /// can trigger a run.
    ///
    /// <para>Models are never pooled: each run id and each group id contributes exactly one point.
    /// Which points may share a chart is decided by <c>BenchmarkCrossModelComparability</c> inside
    /// the service, and an entry produced by a different instrument comes back with its measures
    /// withheld and the differing keys named — so a chart cannot render it even by ignoring a
    /// flag.</para>
    /// </summary>
    [HttpGet("model-comparison")]
    public async Task<IActionResult> GetModelComparison(
        [FromQuery] BenchmarkModelComparisonRequest request,
        [FromServices] BenchmarkModelComparisonService comparisonService,
        CancellationToken ct)
    {
        var (result, error) = await comparisonService.CompareAsync(request, ct);

        return result == null
            ? BadRequest(error ?? "The comparison could not be computed.")
            : Ok(result);
    }

    /// <summary>
    /// The runs and groups named in the query, split into comparability conditions: which of them
    /// agree on every must-match key and would therefore share a chart, which would be excluded and
    /// on which keys, and which group's own members do not describe one point. Read-only arithmetic
    /// over stored data, so nothing here can trigger a run.
    ///
    /// <para>The split reproduces the baseline <c>model-comparison</c> would choose over the same
    /// sources, which is what lets a picker say what Compare is about to do before it is asked.</para>
    /// </summary>
    [HttpGet("model-comparison/comparability")]
    public async Task<IActionResult> GetModelComparisonComparability(
        [FromQuery] BenchmarkComparabilityIndexRequest request,
        [FromServices] BenchmarkComparabilityIndexService indexService,
        CancellationToken ct)
    {
        var (result, error) = await indexService.BuildAsync(request, ct);

        return result == null
            ? BadRequest(error ?? "The comparability index could not be computed.")
            : Ok(result);
    }
}
