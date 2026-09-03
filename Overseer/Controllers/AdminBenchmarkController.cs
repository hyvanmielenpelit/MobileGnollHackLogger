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

    public AdminBenchmarkController(
        ApplicationDbContext dbContext,
        BenchmarkService benchmarkService,
        BenchmarkScoringProfileService scoringProfileService,
        BenchmarkRunManager runManager,
        BenchmarkDifficultyJobManager difficultyJobManager,
        BenchmarkComplianceGuard complianceGuard,
        IServiceScopeFactory scopeFactory)
    {
        _dbContext = dbContext;
        _benchmarkService = benchmarkService;
        _scoringProfileService = scoringProfileService;
        _runManager = runManager;
        _difficultyJobManager = difficultyJobManager;
        _complianceGuard = complianceGuard;
        _scopeFactory = scopeFactory;
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
        DifficultyFullyAssessed = s.Questions.Count > 0 && s.Questions.Count(q => q.AssessedDifficulty != null) == s.Questions.Count
    };

    private static BenchmarkQuestionDto ToQuestionDto(BenchmarkQuestion q) => new()
    {
        Id = q.Id,
        BenchmarkSuiteId = q.BenchmarkSuiteId,
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
        CreatedAtUtc = q.CreatedAtUtc,
        ModifiedAtUtc = q.ModifiedAtUtc
    };

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

        return Ok(new BenchmarkSuiteDto
        {
            Id = suite.Id,
            Name = suite.Name,
            Description = suite.Description,
            CreatedAtUtc = suite.CreatedAtUtc,
            ModifiedAtUtc = suite.ModifiedAtUtc,
            QuestionCount = 0,
            AssessedQuestionCount = 0,
            DifficultyFullyAssessed = false
        });
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

    [HttpGet("runs/{id}")]
    public async Task<IActionResult> GetRun(long id)
    {
        var run = await _dbContext.BenchmarkRuns
            .Include(r => r.Answers)
            .Include(r => r.StartedByUser)
            .Include(r => r.ScoringProfile)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (run == null) return NotFound();

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
            SpeedIndex = run.SpeedIndex,
            TotalAnswerDurationMs = run.TotalAnswerDurationMs,
            ScoringProfileId = run.ScoringProfileId,
            ScoringProfileName = run.ScoringProfile?.Name,
            ScoringProfileSnapshotJson = run.ScoringProfileSnapshotJson,
            ScoringMethodVersion = run.ScoringMethodVersion,
            HarnessVersion = run.HarnessVersion,
            MaxToolCallsPerQuestionUsed = run.MaxToolCallsPerQuestionUsed,
            DegradedAnswerCount = run.DegradedAnswerCount,
            ToolStarvedAnswerCount = run.ToolStarvedAnswerCount,
            TransportDefectAnswerCount = run.TransportDefectAnswerCount,
            RecoveredAnswerCount = run.RecoveredAnswerCount,
            AdvisoryFlagAnswerCount = run.AdvisoryFlagAnswerCount,
            ScrubbedArtifactAnswerCount = run.ScrubbedArtifactAnswerCount,
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
            ErrorMessage = run.ErrorMessage,

            // Empty unless this is the run currently executing in this process.
            InFlightOrderIndexes = _runManager.GetInFlightQuestions(run.Id).ToList(),

            Answers = run.Answers.OrderBy(a => a.OrderIndex).Select(a => new BenchmarkRunAnswerDto
            {
                Id = a.Id,
                BenchmarkRunId = a.BenchmarkRunId,
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
                SecondOpinionQualityScore = a.SecondOpinionQualityScore,
                SecondOpinionCriticalError = a.SecondOpinionCriticalError,
                SecondOpinionByModelDisplayNameUsed = a.SecondOpinionByModelDisplayNameUsed,
                SecondOpinionJson = a.SecondOpinionJson,
                SecondOpinionDisagreed = a.SecondOpinionDisagreed
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

        var cts = new CancellationTokenSource();
        if (!_runManager.TryStart(run.Id, cts, out _))
        {
            return Conflict("A benchmark run is already in progress.");
        }

        _ = Task.Run(() => _benchmarkService.ReassessSingleQuestionAsync(answerId, request?.AssessorModelConfigurationId, cts.Token));
        return Accepted(new { runId = id });
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
