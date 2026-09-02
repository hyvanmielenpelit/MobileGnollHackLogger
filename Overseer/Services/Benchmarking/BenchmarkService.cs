namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MobileGnollHackLogger.Data;
using Overseer.Services.Agents;
using Overseer.Services.Providers;

public class BenchmarkService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ChatService _chatService;
    private readonly AgentLoopRunner _agentLoopRunner;
    private readonly CryptoService _cryptoService;
    private readonly BenchmarkRunManager _runManager;
    private readonly BenchmarkScoringProfileService _scoringProfileService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<BenchmarkService> _logger;

    private readonly List<string> _defaultAllowedTools = new()
    {
        "wiki_search", "wiki_view", "get_knowledge_article",
        "nethack_wiki_search", "nethack_wiki_view",
        "monster_lookup", "item_lookup", "get_monster_stats",
        "get_item_stats", "get_artifact_stats", "get_constants",
        "get_function_definition", "search_definitions",
        "source_code_search", "source_code_view", "list_indexed_files"
    };

    public BenchmarkService(
        IServiceScopeFactory scopeFactory,
        ChatService chatService,
        AgentLoopRunner agentLoopRunner,
        CryptoService cryptoService,
        BenchmarkRunManager runManager,
        BenchmarkScoringProfileService scoringProfileService,
        IConfiguration configuration,
        ILogger<BenchmarkService> logger)
    {
        _scopeFactory = scopeFactory;
        _chatService = chatService;
        _agentLoopRunner = agentLoopRunner;
        _cryptoService = cryptoService;
        _runManager = runManager;
        _scoringProfileService = scoringProfileService;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task CleanupOrphanedRunsAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var staleCutoff = DateTime.UtcNow.AddMinutes(-30);
        var orphanedRuns = await db.BenchmarkRuns
            .Where(r => r.Status == BenchmarkRunStatus.Running && r.StartedAtUtc < staleCutoff)
            .ToListAsync();

        if (orphanedRuns.Count > 0)
        {
            foreach (var run in orphanedRuns)
            {
                run.Status = BenchmarkRunStatus.Failed;
                run.ErrorMessage = "Run interrupted by application restart.";
                run.CompletedAtUtc = DateTime.UtcNow;
            }
            await db.SaveChangesAsync();
            _logger.LogInformation("Cleaned up {Count} orphaned benchmark runs.", orphanedRuns.Count);
        }
    }

    public async Task RunAsync(long runId, CancellationToken cancellationToken)
    {
        var runStopwatch = Stopwatch.StartNew();
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var configService = scope.ServiceProvider.GetRequiredService<SystemAiConfigService>();

            var run = await db.BenchmarkRuns
                .Include(r => r.BenchmarkSuite)
                .ThenInclude(s => s!.Questions)
                .Include(r => r.TestedModelConfiguration)
                .Include(r => r.AssessorModelConfiguration)
                .FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);

            if (run == null)
            {
                _logger.LogError("Benchmark run {RunId} not found.", runId);
                _runManager.Complete(runId);
                return;
            }

            var testedConfig = run.TestedModelConfiguration;
            var assessorConfig = run.AssessorModelConfiguration;

            if (testedConfig == null || string.IsNullOrWhiteSpace(testedConfig.EncryptedApiKey) ||
                assessorConfig == null || string.IsNullOrWhiteSpace(assessorConfig.EncryptedApiKey))
            {
                run.Status = BenchmarkRunStatus.Failed;
                run.ErrorMessage = "Tested or assessor model configuration missing or has no API key.";
                run.CompletedAtUtc = DateTime.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
                _runManager.Complete(runId);
                return;
            }

            // Load scoring profile
            BenchmarkScoringProfile profile;
            if (run.ScoringProfileId.HasValue)
            {
                profile = await _scoringProfileService.GetProfileByIdAsync(run.ScoringProfileId.Value) ??
                          await _scoringProfileService.GetDefaultProfileAsync();
            }
            else
            {
                profile = await _scoringProfileService.GetDefaultProfileAsync();
            }

            var scoringConstants = _scoringProfileService.ToConstants(profile);
            run.ScoringProfileId = profile.Id;
            run.ScoringProfileSnapshotJson = JsonSerializer.Serialize(profile);
            run.ScoringMethodVersion = BenchmarkAssessmentPrompt.ScoringMethodVersion;

            string testedApiKey = _cryptoService.Decrypt(testedConfig.EncryptedApiKey, testedConfig.ApiKeyNonce!, testedConfig.ApiKeyTag!, "SYSTEM_API_KEY");
            string assessorApiKey = _cryptoService.Decrypt(assessorConfig.EncryptedApiKey, assessorConfig.ApiKeyNonce!, assessorConfig.ApiKeyTag!, "SYSTEM_API_KEY");

            var questions = (run.BenchmarkSuite?.Questions ?? new List<BenchmarkQuestion>())
                .OrderBy(q => q.OrderIndex)
                .ToList();



            run.TotalQuestionCount = questions.Count;
            int maxParallel = profile.MaxParallelQuestions;
            run.MaxParallelQuestionsUsed = maxParallel;
            run.SpeedMeasurementDegraded = maxParallel > 1;
            await db.SaveChangesAsync(cancellationToken);

            var allowedTools = _configuration.GetSection("Benchmark:AllowedTools").Get<List<string>>() ?? _defaultAllowedTools;
            int maxToolIterations = _configuration.GetValue<int>("Benchmark:MaxToolIterations", 8);
            int maxTotalModelCalls = _configuration.GetValue<int>("Benchmark:MaxTotalModelCalls", 12);
            int maxResultLength = _configuration.GetValue<int>("Benchmark:MaxResultLength", 10000);
            int maxCallsPerSession = _configuration.GetValue<int>("Benchmark:MaxCallsPerSession", 50);

            string systemPrompt = _chatService.BuildSystemPrompt(
                wikiContext: Array.Empty<string>(),
                spoilerFreeMode: false,
                verboseMode: false,
                isGameOn: false,
                developerMode: false,
                overseerMode: 0,
                hasGameSnapshot: false,
                hasMessageHistory: false,
                clientSettings: null,
                enableToolUse: true,
                enableWebSearch: false,
                allowSourceCodeReferences: true,
                enableSubAgents: false,
                parallelMode: testedConfig.ParallelExecutionMode);

            // Check credential collision between candidate and assessor
            string testedKey = AiRequestGovernor.GetCredentialKey(testedConfig.Provider, null, testedConfig.Id);
            string assessorKey = AiRequestGovernor.GetCredentialKey(assessorConfig.Provider, null, assessorConfig.Id);
            bool credentialCollision = string.Equals(testedKey, assessorKey, StringComparison.OrdinalIgnoreCase);

            if (credentialCollision)
            {
                _logger.LogInformation("Tested and assessor models share credential key '{Key}'. Serializing assessment behind answering.", testedKey);
            }

            var createdAnswers = new ConcurrentBag<BenchmarkRunAnswer>();

            if (maxParallel <= 1)
            {
                // Sequential Execution
                foreach (var question in questions)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        run.Status = BenchmarkRunStatus.Canceled;
                        run.CompletedAtUtc = DateTime.UtcNow;
                        await db.SaveChangesAsync(CancellationToken.None);
                        _runManager.Complete(runId);
                        return;
                    }

                    var ans = await ExecuteSingleQuestionAsync(
                        db, configService, run, question, testedConfig, testedApiKey,
                        systemPrompt, allowedTools, maxToolIterations, maxTotalModelCalls,
                        maxResultLength, maxCallsPerSession, cancellationToken);

                    createdAnswers.Add(ans);

                    if (!credentialCollision)
                    {
                        // Pipelined immediate assessment
                        await ExecutePerQuestionAssessmentAsync(
                            db, configService, run, ans, question.ExpectedPoints,
                            assessorConfig, assessorApiKey, scoringConstants, cancellationToken);
                    }
                }
            }
            else
            {
                // Bounded Parallel Execution
                using var semaphore = new SemaphoreSlim(maxParallel, maxParallel);
                var answerTasks = questions.Select(async question =>
                {
                    await semaphore.WaitAsync(cancellationToken);
                    try
                    {
                        using var qScope = _scopeFactory.CreateScope();
                        var qDb = qScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                        var qConfigService = qScope.ServiceProvider.GetRequiredService<SystemAiConfigService>();

                        var ans = await ExecuteSingleQuestionAsync(
                            qDb, qConfigService, run, question, testedConfig, testedApiKey,
                            systemPrompt, allowedTools, maxToolIterations, maxTotalModelCalls,
                            maxResultLength, maxCallsPerSession, cancellationToken);

                        createdAnswers.Add(ans);

                        if (!credentialCollision)
                        {
                            await ExecutePerQuestionAssessmentAsync(
                                qDb, qConfigService, run, ans, question.ExpectedPoints,
                                assessorConfig, assessorApiKey, scoringConstants, cancellationToken);
                        }
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

                await Task.WhenAll(answerTasks);
            }

            // If assessment was serialized due to credential collision, run all assessments now
            if (credentialCollision)
            {
                var answersToAssess = await db.BenchmarkRunAnswers
                    .Where(a => a.BenchmarkRunId == runId)
                    .OrderBy(a => a.OrderIndex)
                    .ToListAsync(cancellationToken);

                var suiteQuestions = questions.ToDictionary(q => q.OrderIndex, q => q.ExpectedPoints);

                foreach (var ans in answersToAssess)
                {
                    suiteQuestions.TryGetValue(ans.OrderIndex, out var ep);
                    await ExecutePerQuestionAssessmentAsync(
                        db, configService, run, ans, ep,
                        assessorConfig, assessorApiKey, scoringConstants, cancellationToken);
                }
            }

            // Final Synthesis Pass
            await ExecuteFinalSynthesisAsync(db, configService, run, assessorConfig, assessorApiKey, scoringConstants, cancellationToken);

            // Finalize Run totals & status
            runStopwatch.Stop();
            run.TotalDurationMs = runStopwatch.ElapsedMilliseconds;
            run.CompletedAtUtc = DateTime.UtcNow;

            var allAnswers = await db.BenchmarkRunAnswers.Where(a => a.BenchmarkRunId == runId).ToListAsync(cancellationToken);
            run.TotalInputTokens = allAnswers.Sum(a => (long)(a.InputTokens ?? 0));
            run.TotalOutputTokens = allAnswers.Sum(a => (long)(a.OutputTokens ?? 0));
            run.TotalCacheReadTokens = allAnswers.Sum(a => (long)(a.CacheReadInputTokens ?? 0));
            run.TotalCacheCreationTokens = allAnswers.Sum(a => (long)(a.CacheCreationInputTokens ?? 0));
            run.TotalAnswerDurationMs = allAnswers.Sum(a => a.DurationMs);
            run.AnsweredQuestionCount = allAnswers.Count(a => a.Status == BenchmarkAnswerStatus.Ok);

            // Calculate Indices
            var scorableItems = allAnswers
                .Where(a => a.Status == BenchmarkAnswerStatus.Ok)
                .Select(a => (a.QualityScore, a.AssessedDifficulty ?? GetFallbackDifficulty(a.Difficulty)))
                .ToList();

            var speedItems = allAnswers
                .Where(a => a.Status == BenchmarkAnswerStatus.Ok)
                .Select(a => (a.SpeedScore, a.AssessedDifficulty ?? GetFallbackDifficulty(a.Difficulty)))
                .ToList();

            run.QualityIndex = BenchmarkScoring.QualityIndex(scorableItems);
            run.SpeedIndex = BenchmarkScoring.SpeedIndex(speedItems);

            if (allAnswers.Any(a => a.Status == BenchmarkAnswerStatus.ProviderError || a.Status == BenchmarkAnswerStatus.Failed))
            {
                run.Status = BenchmarkRunStatus.CompletedWithErrors;
            }
            else
            {
                run.Status = BenchmarkRunStatus.Completed;
            }

            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var run = await db.BenchmarkRuns.FindAsync(runId);
            if (run != null)
            {
                run.Status = BenchmarkRunStatus.Canceled;
                run.CompletedAtUtc = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Benchmark run {RunId} failed with exception.", runId);
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var run = await db.BenchmarkRuns.FindAsync(runId);
            if (run != null)
            {
                run.Status = BenchmarkRunStatus.Failed;
                run.ErrorMessage = ex.Message;
                run.CompletedAtUtc = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }
        }
        finally
        {
            _runManager.Complete(runId);
        }
    }

    public async Task RunFailedQuestionsAsync(long runId, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var configService = scope.ServiceProvider.GetRequiredService<SystemAiConfigService>();

            var run = await db.BenchmarkRuns
                .Include(r => r.Answers)
                .Include(r => r.TestedModelConfiguration)
                .Include(r => r.AssessorModelConfiguration)
                .Include(r => r.BenchmarkSuite)
                .ThenInclude(s => s!.Questions)
                .FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);

            if (run == null)
            {
                _runManager.Complete(runId);
                return;
            }

            var testedConfig = run.TestedModelConfiguration;
            var assessorConfig = run.AssessorModelConfiguration;

            if (testedConfig == null || string.IsNullOrWhiteSpace(testedConfig.EncryptedApiKey) ||
                assessorConfig == null || string.IsNullOrWhiteSpace(assessorConfig.EncryptedApiKey))
            {
                _runManager.Complete(runId);
                return;
            }

            string testedApiKey = _cryptoService.Decrypt(testedConfig.EncryptedApiKey, testedConfig.ApiKeyNonce!, testedConfig.ApiKeyTag!, "SYSTEM_API_KEY");
            string assessorApiKey = _cryptoService.Decrypt(assessorConfig.EncryptedApiKey, assessorConfig.ApiKeyNonce!, assessorConfig.ApiKeyTag!, "SYSTEM_API_KEY");

            var failedAnswers = run.Answers
                .Where(a => a.Status == BenchmarkAnswerStatus.ProviderError || a.Status == BenchmarkAnswerStatus.Failed)
                .OrderBy(a => a.OrderIndex)
                .ToList();

            if (failedAnswers.Count == 0)
            {
                _runManager.Complete(runId);
                return;
            }

            run.Status = BenchmarkRunStatus.Running;
            await db.SaveChangesAsync(cancellationToken);

            var profile = run.ScoringProfileId.HasValue
                ? await _scoringProfileService.GetProfileByIdAsync(run.ScoringProfileId.Value) ?? await _scoringProfileService.GetDefaultProfileAsync()
                : await _scoringProfileService.GetDefaultProfileAsync();
            var scoringConstants = _scoringProfileService.ToConstants(profile);

            var allowedTools = _configuration.GetSection("Benchmark:AllowedTools").Get<List<string>>() ?? _defaultAllowedTools;
            int maxToolIterations = _configuration.GetValue<int>("Benchmark:MaxToolIterations", 8);
            int maxTotalModelCalls = _configuration.GetValue<int>("Benchmark:MaxTotalModelCalls", 12);
            int maxResultLength = _configuration.GetValue<int>("Benchmark:MaxResultLength", 10000);
            int maxCallsPerSession = _configuration.GetValue<int>("Benchmark:MaxCallsPerSession", 50);

            string systemPrompt = _chatService.BuildSystemPrompt(
                wikiContext: Array.Empty<string>(),
                spoilerFreeMode: false,
                verboseMode: false,
                isGameOn: false,
                developerMode: false,
                overseerMode: 0,
                hasGameSnapshot: false,
                hasMessageHistory: false,
                clientSettings: null,
                enableToolUse: true,
                enableWebSearch: false,
                allowSourceCodeReferences: true,
                enableSubAgents: false,
                parallelMode: testedConfig.ParallelExecutionMode);

            var suiteQuestions = (run.BenchmarkSuite?.Questions ?? new List<BenchmarkQuestion>())
                .ToDictionary(q => q.OrderIndex, q => q.ExpectedPoints);

            foreach (var answer in failedAnswers)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    run.Status = BenchmarkRunStatus.Canceled;
                    await db.SaveChangesAsync(CancellationToken.None);
                    _runManager.Complete(runId);
                    return;
                }

                await ReExecuteSingleAnswerAsync(
                    db, configService, run, answer, testedConfig, testedApiKey,
                    systemPrompt, allowedTools, maxToolIterations, maxTotalModelCalls,
                    maxResultLength, maxCallsPerSession, cancellationToken);

                suiteQuestions.TryGetValue(answer.OrderIndex, out var ep);
                await ExecutePerQuestionAssessmentAsync(
                    db, configService, run, answer, ep,
                    assessorConfig, assessorApiKey, scoringConstants, cancellationToken);
            }

            // Re-run synthesis over all answers
            await ExecuteFinalSynthesisAsync(db, configService, run, assessorConfig, assessorApiKey, scoringConstants, cancellationToken);

            var allAnswers = await db.BenchmarkRunAnswers.Where(a => a.BenchmarkRunId == runId).ToListAsync(cancellationToken);
            run.TotalInputTokens = allAnswers.Sum(a => (long)(a.InputTokens ?? 0));
            run.TotalOutputTokens = allAnswers.Sum(a => (long)(a.OutputTokens ?? 0));
            run.TotalCacheReadTokens = allAnswers.Sum(a => (long)(a.CacheReadInputTokens ?? 0));
            run.TotalCacheCreationTokens = allAnswers.Sum(a => (long)(a.CacheCreationInputTokens ?? 0));
            run.TotalAnswerDurationMs = allAnswers.Sum(a => a.DurationMs);
            run.AnsweredQuestionCount = allAnswers.Count(a => a.Status == BenchmarkAnswerStatus.Ok);
            run.CompletedAtUtc = DateTime.UtcNow;

            var scorableItems = allAnswers
                .Where(a => a.Status == BenchmarkAnswerStatus.Ok)
                .Select(a => (a.QualityScore, a.AssessedDifficulty ?? GetFallbackDifficulty(a.Difficulty)))
                .ToList();

            var speedItems = allAnswers
                .Where(a => a.Status == BenchmarkAnswerStatus.Ok)
                .Select(a => (a.SpeedScore, a.AssessedDifficulty ?? GetFallbackDifficulty(a.Difficulty)))
                .ToList();

            run.QualityIndex = BenchmarkScoring.QualityIndex(scorableItems);
            run.SpeedIndex = BenchmarkScoring.SpeedIndex(speedItems);

            if (allAnswers.Any(a => a.Status == BenchmarkAnswerStatus.ProviderError || a.Status == BenchmarkAnswerStatus.Failed))
            {
                run.Status = BenchmarkRunStatus.CompletedWithErrors;
            }
            else
            {
                run.Status = BenchmarkRunStatus.Completed;
            }

            await db.SaveChangesAsync(CancellationToken.None);
        }
        finally
        {
            _runManager.Complete(runId);
        }
    }

    private async Task<BenchmarkRunAnswer> ExecuteSingleQuestionAsync(
        ApplicationDbContext db,
        SystemAiConfigService configService,
        BenchmarkRun run,
        BenchmarkQuestion question,
        SystemAiApiConfiguration testedConfig,
        string testedApiKey,
        string systemPrompt,
        List<string> allowedTools,
        int maxToolIterations,
        int maxTotalModelCalls,
        int maxResultLength,
        int maxCallsPerSession,
        CancellationToken cancellationToken)
    {
        var runRequest = new AgentRunRequest
        {
            ProviderName = testedConfig.Provider,
            ModelId = testedConfig.ModelId,
            ApiKey = testedApiKey,
            ModelDisplayName = testedConfig.DisplayName,
            SystemPrompt = systemPrompt,
            ThinkingLevel = testedConfig.ThinkingLevel,
            ReasoningMode = testedConfig.ReasoningMode,
            ReasoningSummary = testedConfig.ReasoningSummary,
            ServiceTier = testedConfig.ServiceTier,
            MaxOutputTokens = testedConfig.MaxOutputTokens,
            MaxToolIterations = maxToolIterations,
            EnableToolUse = true,
            EnableWebSearch = false,
            EnableSubAgents = false,
            AllowedTools = allowedTools,
            SystemModelId = testedConfig.Id,
            Budget = new AgentRunBudget { MaxTotalModelCalls = maxTotalModelCalls },
            ToolExecutionContext = new Tools.ToolExecutionContext
            {
                SessionId = run.Id,
                UserId = run.StartedByUserId ?? string.Empty,
                MaxResultLength = maxResultLength,
                MaxCallsPerSession = maxCallsPerSession,
                ShowDebugLog = false
            },
            SeedHistory = new List<object>
            {
                new { role = "user", content = question.QuestionText }
            }
        };

        var runResult = new AgentRunResult();
        var sw = Stopwatch.StartNew();

        string? terminalError = null;
        try
        {
            await foreach (var evt in _agentLoopRunner.RunAsync(runRequest, runRequest.Budget, runResult, cancellationToken))
            {
                if (evt.Type == "error")
                {
                    terminalError = evt.Data?.ToString();
                }
            }
        }
        catch (Exception ex)
        {
            terminalError = ex.Message;
        }
        sw.Stop();

        var classification = BenchmarkProviderErrorClassifier.Classify(terminalError);
        var sanitized = BenchmarkAnswerSanitizer.Sanitize(runResult.FinalText);

        var toolNames = runResult.ToolCalls
            .Select(tc => tc.Name)
            .Where(n => !string.IsNullOrEmpty(n))
            .Distinct();
        string toolSummary = string.Join(", ", toolNames);

        int assessedDiff = question.AssessedDifficulty ?? GetFallbackDifficulty(question.Difficulty);

        var answer = new BenchmarkRunAnswer
        {
            BenchmarkRunId = run.Id,
            OrderIndex = question.OrderIndex,
            QuestionText = question.QuestionText,
            Difficulty = question.Difficulty,
            AssessedDifficulty = assessedDiff,
            AnswerText = sanitized.AnswerText,
            ThoughtText = sanitized.ThoughtText,
            Status = classification.IsProviderError ? BenchmarkAnswerStatus.ProviderError : (!string.IsNullOrEmpty(terminalError) ? BenchmarkAnswerStatus.Failed : BenchmarkAnswerStatus.Ok),
            AssessmentStatus = BenchmarkAssessmentStatus.Pending,
            ErrorMessage = terminalError,
            HttpStatusCode = classification.HttpStatus,
            DurationMs = runResult.TotalDurationMs ?? sw.ElapsedMilliseconds,
            TimeToFirstTokenMs = runResult.TimeToFirstTokenMs,
            ActualServiceTierUsed = runResult.ActualServiceTier,
            ToolCallSummary = string.IsNullOrEmpty(toolSummary) ? null : toolSummary,
            InputTokens = runResult.TotalPromptTokens > 0 ? runResult.TotalPromptTokens : runResult.EstimatedInputTokens,
            OutputTokens = runResult.OutputTokens > 0 ? runResult.OutputTokens : runResult.EstimatedOutputTokens,
            CacheReadInputTokens = runResult.CacheReadTokens,
            CacheCreationInputTokens = runResult.CacheCreationTokens
        };

        db.BenchmarkRunAnswers.Add(answer);
        await db.SaveChangesAsync(CancellationToken.None);

        try
        {
            await configService.RecordUsageAsync(
                testedConfig.Id,
                run.StartedByUserId ?? string.Empty,
                answer.InputTokens ?? 0,
                answer.OutputTokens ?? 0,
                roleContext: 4,
                cacheReadTokens: answer.CacheReadInputTokens,
                cacheCreationTokens: answer.CacheCreationInputTokens,
                totalDurationMs: (int)answer.DurationMs);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record usage for benchmark answer.");
        }

        return answer;
    }

    private async Task ReExecuteSingleAnswerAsync(
        ApplicationDbContext db,
        SystemAiConfigService configService,
        BenchmarkRun run,
        BenchmarkRunAnswer answer,
        SystemAiApiConfiguration testedConfig,
        string testedApiKey,
        string systemPrompt,
        List<string> allowedTools,
        int maxToolIterations,
        int maxTotalModelCalls,
        int maxResultLength,
        int maxCallsPerSession,
        CancellationToken cancellationToken)
    {
        var runRequest = new AgentRunRequest
        {
            ProviderName = testedConfig.Provider,
            ModelId = testedConfig.ModelId,
            ApiKey = testedApiKey,
            ModelDisplayName = testedConfig.DisplayName,
            SystemPrompt = systemPrompt,
            ThinkingLevel = testedConfig.ThinkingLevel,
            ReasoningMode = testedConfig.ReasoningMode,
            ReasoningSummary = testedConfig.ReasoningSummary,
            ServiceTier = testedConfig.ServiceTier,
            MaxOutputTokens = testedConfig.MaxOutputTokens,
            MaxToolIterations = maxToolIterations,
            EnableToolUse = true,
            EnableWebSearch = false,
            EnableSubAgents = false,
            AllowedTools = allowedTools,
            SystemModelId = testedConfig.Id,
            Budget = new AgentRunBudget { MaxTotalModelCalls = maxTotalModelCalls },
            ToolExecutionContext = new Tools.ToolExecutionContext
            {
                SessionId = run.Id,
                UserId = run.StartedByUserId ?? string.Empty,
                MaxResultLength = maxResultLength,
                MaxCallsPerSession = maxCallsPerSession,
                ShowDebugLog = false
            },
            SeedHistory = new List<object>
            {
                new { role = "user", content = answer.QuestionText }
            }
        };

        var runResult = new AgentRunResult();
        var sw = Stopwatch.StartNew();

        string? terminalError = null;
        try
        {
            await foreach (var evt in _agentLoopRunner.RunAsync(runRequest, runRequest.Budget, runResult, cancellationToken))
            {
                if (evt.Type == "error")
                {
                    terminalError = evt.Data?.ToString();
                }
            }
        }
        catch (Exception ex)
        {
            terminalError = ex.Message;
        }
        sw.Stop();

        var classification = BenchmarkProviderErrorClassifier.Classify(terminalError);
        var sanitized = BenchmarkAnswerSanitizer.Sanitize(runResult.FinalText);

        var toolNames = runResult.ToolCalls
            .Select(tc => tc.Name)
            .Where(n => !string.IsNullOrEmpty(n))
            .Distinct();
        string toolSummary = string.Join(", ", toolNames);

        answer.AnswerText = sanitized.AnswerText;
        answer.ThoughtText = sanitized.ThoughtText;
        answer.Status = classification.IsProviderError ? BenchmarkAnswerStatus.ProviderError : (!string.IsNullOrEmpty(terminalError) ? BenchmarkAnswerStatus.Failed : BenchmarkAnswerStatus.Ok);
        answer.AssessmentStatus = BenchmarkAssessmentStatus.Pending;
        answer.ErrorMessage = terminalError;
        answer.HttpStatusCode = classification.HttpStatus;
        answer.DurationMs = runResult.TotalDurationMs ?? sw.ElapsedMilliseconds;
        answer.TimeToFirstTokenMs = runResult.TimeToFirstTokenMs;
        answer.ActualServiceTierUsed = runResult.ActualServiceTier;
        answer.ToolCallSummary = string.IsNullOrEmpty(toolSummary) ? null : toolSummary;
        answer.InputTokens = runResult.TotalPromptTokens > 0 ? runResult.TotalPromptTokens : runResult.EstimatedInputTokens;
        answer.OutputTokens = runResult.OutputTokens > 0 ? runResult.OutputTokens : runResult.EstimatedOutputTokens;
        answer.CacheReadInputTokens = runResult.CacheReadTokens;
        answer.CacheCreationInputTokens = runResult.CacheCreationTokens;

        await db.SaveChangesAsync(CancellationToken.None);

        try
        {
            await configService.RecordUsageAsync(
                testedConfig.Id,
                run.StartedByUserId ?? string.Empty,
                answer.InputTokens ?? 0,
                answer.OutputTokens ?? 0,
                roleContext: 4,
                cacheReadTokens: answer.CacheReadInputTokens,
                cacheCreationTokens: answer.CacheCreationInputTokens,
                totalDurationMs: (int)answer.DurationMs);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record usage for re-executed answer.");
        }
    }

    public async Task ExecutePerQuestionAssessmentAsync(
        ApplicationDbContext db,
        SystemAiConfigService configService,
        BenchmarkRun run,
        BenchmarkRunAnswer answer,
        string? expectedPoints,
        SystemAiApiConfiguration assessorConfig,
        string assessorApiKey,
        BenchmarkScoringConstants constants,
        CancellationToken cancellationToken)
    {
        answer.AssessmentStatus = BenchmarkAssessmentStatus.Assessing;
        await db.SaveChangesAsync(CancellationToken.None);

        string prompt = BenchmarkAssessmentPrompt.BuildPerQuestionPrompt(
            run.SuiteName,
            answer.OrderIndex,
            answer.QuestionText,
            answer.Difficulty,
            expectedPoints,
            answer.AnswerText,
            answer.Status,
            answer.DurationMs);

        int assessorMaxTokens = _configuration.GetValue<int>("Benchmark:AssessorMaxOutputTokens", 32000);

        var runRequest = new AgentRunRequest
        {
            ProviderName = assessorConfig.Provider,
            ModelId = assessorConfig.ModelId,
            ApiKey = assessorApiKey,
            ModelDisplayName = assessorConfig.DisplayName,
            SystemPrompt = "You are an objective AI benchmark evaluator. Strictly adhere to the requested JSON response format.",
            ThinkingLevel = assessorConfig.ThinkingLevel,
            ReasoningMode = assessorConfig.ReasoningMode,
            ReasoningSummary = assessorConfig.ReasoningSummary,
            ServiceTier = assessorConfig.ServiceTier,
            MaxOutputTokens = assessorConfig.MaxOutputTokens ?? assessorMaxTokens,
            MaxToolIterations = 0,
            EnableToolUse = false,
            EnableWebSearch = false,
            EnableSubAgents = false,
            SystemModelId = assessorConfig.Id,
            PromptCacheKey = $"benchmark:per_question:{assessorConfig.ModelId}",
            Budget = new AgentRunBudget { MaxTotalModelCalls = 2 },
            ToolExecutionContext = new Tools.ToolExecutionContext
            {
                SessionId = run.Id,
                UserId = run.StartedByUserId ?? string.Empty,
                ShowDebugLog = false
            },
            SeedHistory = new List<object>
            {
                new { role = "user", content = prompt }
            }
        };

        var runResult = new AgentRunResult();
        var sw = Stopwatch.StartNew();
        await foreach (var _ in _agentLoopRunner.RunAsync(runRequest, runRequest.Budget, runResult, cancellationToken)) { }
        sw.Stop();

        var parseResult = BenchmarkAssessmentParser.ParsePerQuestion(runResult.FinalText);

        if (!parseResult.Success)
        {
            _logger.LogWarning("Assessor per-question output failed JSON parsing. Retrying once...");
            runRequest.SeedHistory.Add(new { role = "assistant", content = runResult.FinalText ?? string.Empty });
            runRequest.SeedHistory.Add(new { role = "user", content = $"Your previous response was not valid JSON or could not be parsed: {parseResult.ErrorMessage}. Please output ONLY the raw JSON object according to the schema without any markdown wrapping or extra text." });

            var retryResult = new AgentRunResult();
            await foreach (var _ in _agentLoopRunner.RunAsync(runRequest, runRequest.Budget, retryResult, cancellationToken)) { }
            parseResult = BenchmarkAssessmentParser.ParsePerQuestion(retryResult.FinalText);
            if (retryResult.TotalPromptTokens > 0) runResult = retryResult;
        }

        if (parseResult.Success && parseResult.Result != null)
        {
            var res = parseResult.Result;
            answer.AccuracyLevel = res.AccuracyLevel;
            answer.CompletenessLevel = res.CompletenessLevel;
            answer.ConcisenessLevel = res.ConcisenessLevel;
            answer.ReadabilityLevel = res.ReadabilityLevel;
            answer.CriticalError = res.CriticalError;
            answer.ReviewComment = res.Comment;

            answer.AccuracyScore = BenchmarkScoring.Score(res.AccuracyLevel, constants.LevelScores);
            answer.CompletenessScore = BenchmarkScoring.Score(res.CompletenessLevel, constants.LevelScores);
            answer.ConcisenessScore = BenchmarkScoring.Score(res.ConcisenessLevel, constants.LevelScores);
            answer.ReadabilityScore = BenchmarkScoring.Score(res.ReadabilityLevel, constants.LevelScores);

            var (qualityScore, _) = BenchmarkScoring.Quality(
                res.AccuracyLevel, res.CompletenessLevel, res.ConcisenessLevel, res.ReadabilityLevel,
                res.CriticalError, constants);

            answer.QualityScore = qualityScore;
            answer.SpeedScore = BenchmarkScoring.Speed(answer.DurationMs, constants);
            answer.Score = qualityScore; // Legacy field backfill
            answer.AssessmentStatus = BenchmarkAssessmentStatus.Scored;
            answer.AssessmentError = null;
        }
        else
        {
            answer.AssessmentStatus = BenchmarkAssessmentStatus.Failed;
            answer.AssessmentError = parseResult.ErrorMessage;
        }

        await db.SaveChangesAsync(CancellationToken.None);

        try
        {
            await configService.RecordUsageAsync(
                assessorConfig.Id,
                run.StartedByUserId ?? string.Empty,
                runResult.TotalPromptTokens > 0 ? runResult.TotalPromptTokens : runResult.EstimatedInputTokens,
                runResult.OutputTokens > 0 ? runResult.OutputTokens : runResult.EstimatedOutputTokens,
                roleContext: 4,
                cacheReadTokens: runResult.CacheReadTokens,
                cacheCreationTokens: runResult.CacheCreationTokens,
                totalDurationMs: (int)sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record usage for per-question assessor call.");
        }
    }

    private async Task ExecuteFinalSynthesisAsync(
        ApplicationDbContext db,
        SystemAiConfigService configService,
        BenchmarkRun run,
        SystemAiApiConfiguration assessorConfig,
        string assessorApiKey,
        BenchmarkScoringConstants constants,
        CancellationToken cancellationToken)
    {
        var answers = await db.BenchmarkRunAnswers
            .Where(a => a.BenchmarkRunId == run.Id)
            .OrderBy(a => a.OrderIndex)
            .ToListAsync(cancellationToken);

        var summaries = answers.Select(a => new BenchmarkPerQuestionVerdictSummary
        {
            OrderIndex = a.OrderIndex,
            QuestionText = a.QuestionText,
            AccuracyLevel = a.AccuracyLevel,
            CompletenessLevel = a.CompletenessLevel,
            ConcisenessLevel = a.ConcisenessLevel,
            ReadabilityLevel = a.ReadabilityLevel,
            QualityScore = a.QualityScore,
            SpeedScore = a.SpeedScore,
            DurationMs = a.DurationMs,
            AssessedDifficulty = a.AssessedDifficulty ?? GetFallbackDifficulty(a.Difficulty),
            CriticalError = a.CriticalError,
            ReviewComment = a.ReviewComment,
            Status = a.Status
        }).ToList();

        if (run.BenchmarkSuiteId.HasValue)
        {
            var suiteQuestions = await db.BenchmarkQuestions
                .Where(q => q.BenchmarkSuiteId == run.BenchmarkSuiteId.Value)
                .ToDictionaryAsync(q => q.OrderIndex, q => q.ExpectedPoints, cancellationToken);

            foreach (var s in summaries)
            {
                if (suiteQuestions.TryGetValue(s.OrderIndex, out var ep))
                {
                    s.ExpectedPoints = ep;
                }
            }
        }

        string synthesisPrompt = BenchmarkAssessmentPrompt.BuildFinalSynthesisPrompt(run.SuiteName, summaries);

        int assessorMaxTokens = _configuration.GetValue<int>("Benchmark:AssessorMaxOutputTokens", 32000);

        var runRequest = new AgentRunRequest
        {
            ProviderName = assessorConfig.Provider,
            ModelId = assessorConfig.ModelId,
            ApiKey = assessorApiKey,
            ModelDisplayName = assessorConfig.DisplayName,
            SystemPrompt = "You are an objective AI benchmark evaluator synthesizing a final report. Strictly adhere to the requested JSON response format.",
            ThinkingLevel = assessorConfig.ThinkingLevel,
            ReasoningMode = assessorConfig.ReasoningMode,
            ReasoningSummary = assessorConfig.ReasoningSummary,
            ServiceTier = assessorConfig.ServiceTier,
            MaxOutputTokens = assessorConfig.MaxOutputTokens ?? assessorMaxTokens,
            MaxToolIterations = 0,
            EnableToolUse = false,
            EnableWebSearch = false,
            EnableSubAgents = false,
            SystemModelId = assessorConfig.Id,
            Budget = new AgentRunBudget { MaxTotalModelCalls = 2 },
            ToolExecutionContext = new Tools.ToolExecutionContext
            {
                SessionId = run.Id,
                UserId = run.StartedByUserId ?? string.Empty,
                ShowDebugLog = false
            },
            SeedHistory = new List<object>
            {
                new { role = "user", content = synthesisPrompt }
            }
        };

        var runResult = new AgentRunResult();
        var sw = Stopwatch.StartNew();
        await foreach (var _ in _agentLoopRunner.RunAsync(runRequest, runRequest.Budget, runResult, cancellationToken)) { }
        sw.Stop();

        var parseResult = BenchmarkAssessmentParser.ParseFinalSynthesis(runResult.FinalText);

        if (!parseResult.Success)
        {
            _logger.LogWarning("Assessor synthesis output failed JSON parsing. Retrying once...");
            runRequest.SeedHistory.Add(new { role = "assistant", content = runResult.FinalText ?? string.Empty });
            runRequest.SeedHistory.Add(new { role = "user", content = $"Your previous response was not valid JSON or could not be parsed: {parseResult.ErrorMessage}. Please output ONLY the raw JSON object according to the schema without any markdown wrapping or extra text." });

            var retryResult = new AgentRunResult();
            await foreach (var _ in _agentLoopRunner.RunAsync(runRequest, runRequest.Budget, retryResult, cancellationToken)) { }
            parseResult = BenchmarkAssessmentParser.ParseFinalSynthesis(retryResult.FinalText);
            if (retryResult.TotalPromptTokens > 0) runResult = retryResult;
        }

        if (parseResult.Success && parseResult.Result != null)
        {
            run.FinalScore = parseResult.Result.FinalScore;
            run.AssessmentJson = parseResult.RawJson;
            run.AssessmentText = parseResult.Result.OverallComments;
            run.AssessmentParseFailed = false;
        }
        else
        {
            run.FinalScore = null;
            run.AssessmentJson = runResult.FinalText;
            run.AssessmentText = null;
            run.AssessmentParseFailed = true;
        }

        await db.SaveChangesAsync(CancellationToken.None);

        try
        {
            await configService.RecordUsageAsync(
                assessorConfig.Id,
                run.StartedByUserId ?? string.Empty,
                runResult.TotalPromptTokens > 0 ? runResult.TotalPromptTokens : runResult.EstimatedInputTokens,
                runResult.OutputTokens > 0 ? runResult.OutputTokens : runResult.EstimatedOutputTokens,
                roleContext: 4,
                cacheReadTokens: runResult.CacheReadTokens,
                cacheCreationTokens: runResult.CacheCreationTokens,
                totalDurationMs: (int)sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record usage for assessor synthesis call.");
        }
    }

    public async Task<(bool Success, int RatedCount, string? ErrorMessage)> RateSuiteDifficultyAsync(
        long suiteId,
        long assessorConfigId,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var configService = scope.ServiceProvider.GetRequiredService<SystemAiConfigService>();

        var assessorConfig = await db.SystemAiApiConfigurations.FindAsync(assessorConfigId);
        if (assessorConfig == null || string.IsNullOrWhiteSpace(assessorConfig.EncryptedApiKey))
        {
            return (false, 0, "Assessor model configuration missing or has no API key.");
        }

        string assessorApiKey = _cryptoService.Decrypt(assessorConfig.EncryptedApiKey, assessorConfig.ApiKeyNonce!, assessorConfig.ApiKeyTag!, "SYSTEM_API_KEY");

        return await RateSuiteDifficultyInternalAsync(db, configService, suiteId, assessorConfig, assessorApiKey, cancellationToken);
    }

    public async Task<(bool Success, int? Difficulty, string? ErrorMessage)> RateQuestionDifficultyAsync(
        long questionId,
        long assessorConfigId,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var configService = scope.ServiceProvider.GetRequiredService<SystemAiConfigService>();

        var question = await db.BenchmarkQuestions
            .Include(q => q.BenchmarkSuite)
            .FirstOrDefaultAsync(q => q.Id == questionId, cancellationToken);

        if (question == null)
        {
            return (false, null, "Question not found.");
        }

        var assessorConfig = await db.SystemAiApiConfigurations.FindAsync(assessorConfigId);
        if (assessorConfig == null || string.IsNullOrWhiteSpace(assessorConfig.EncryptedApiKey))
        {
            return (false, null, "Assessor model configuration missing or has no API key.");
        }

        string assessorApiKey = _cryptoService.Decrypt(assessorConfig.EncryptedApiKey, assessorConfig.ApiKeyNonce!, assessorConfig.ApiKeyTag!, "SYSTEM_API_KEY");

        var items = new List<BenchmarkDifficultyQuestionItem>
        {
            new()
            {
                Id = question.Id,
                OrderIndex = question.OrderIndex,
                QuestionText = question.QuestionText,
                AuthorBand = question.Difficulty,
                ExpectedPoints = question.ExpectedPoints
            }
        };

        string prompt = BenchmarkDifficultyPrompt.BuildPrompt(question.BenchmarkSuite?.Name ?? "GnollHack Suite", items);

        var runRequest = new AgentRunRequest
        {
            ProviderName = assessorConfig.Provider,
            ModelId = assessorConfig.ModelId,
            ApiKey = assessorApiKey,
            ModelDisplayName = assessorConfig.DisplayName,
            SystemPrompt = "You are an objective game mechanics expert. Rate the difficulty of the questions based strictly on the JSON schema requested.",
            ThinkingLevel = assessorConfig.ThinkingLevel,
            ReasoningMode = assessorConfig.ReasoningMode,
            ReasoningSummary = assessorConfig.ReasoningSummary,
            ServiceTier = assessorConfig.ServiceTier,
            MaxOutputTokens = assessorConfig.MaxOutputTokens ?? 2048,
            MaxToolIterations = 0,
            EnableToolUse = false,
            EnableWebSearch = false,
            EnableSubAgents = false,
            SystemModelId = assessorConfig.Id,
            Budget = new AgentRunBudget { MaxTotalModelCalls = 2 },
            SeedHistory = new List<object>
            {
                new { role = "user", content = prompt }
            }
        };

        var runResult = new AgentRunResult();
        var sw = Stopwatch.StartNew();
        await foreach (var _ in _agentLoopRunner.RunAsync(runRequest, runRequest.Budget, runResult, cancellationToken)) { }
        sw.Stop();

        var parseResult = BenchmarkDifficultyParser.Parse(runResult.FinalText);
        if (!parseResult.Success || parseResult.Items.Count == 0)
        {
            return (false, null, parseResult.ErrorMessage ?? "Failed to parse difficulty rating response.");
        }

        var rated = parseResult.Items.First();
        BenchmarkQuestionAssessment.ApplySnapshot(question, rated.Difficulty, assessorConfig, DateTime.UtcNow);

        await db.SaveChangesAsync(cancellationToken);

        try
        {
            await configService.RecordUsageAsync(
                assessorConfig.Id,
                string.Empty,
                runResult.TotalPromptTokens > 0 ? runResult.TotalPromptTokens : runResult.EstimatedInputTokens,
                runResult.OutputTokens > 0 ? runResult.OutputTokens : runResult.EstimatedOutputTokens,
                roleContext: 4,
                cacheReadTokens: runResult.CacheReadTokens,
                cacheCreationTokens: runResult.CacheCreationTokens,
                totalDurationMs: (int)sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record usage for difficulty rating.");
        }

        return (true, rated.Difficulty, null);
    }

    private async Task<(bool Success, int RatedCount, string? ErrorMessage)> RateSuiteDifficultyInternalAsync(
        ApplicationDbContext db,
        SystemAiConfigService configService,
        long suiteId,
        SystemAiApiConfiguration assessorConfig,
        string assessorApiKey,
        CancellationToken cancellationToken)
    {
        var suite = await db.BenchmarkSuites
            .Include(s => s.Questions)
            .FirstOrDefaultAsync(s => s.Id == suiteId, cancellationToken);

        if (suite == null)
        {
            return (false, 0, "Suite not found.");
        }

        var questionsToRate = suite.Questions.OrderBy(q => q.OrderIndex).ToList();
        if (questionsToRate.Count == 0)
        {
            return (true, 0, null);
        }

        var items = questionsToRate.Select(q => new BenchmarkDifficultyQuestionItem
        {
            Id = q.Id,
            OrderIndex = q.OrderIndex,
            QuestionText = q.QuestionText,
            AuthorBand = q.Difficulty,
            ExpectedPoints = q.ExpectedPoints
        }).ToList();

        string prompt = BenchmarkDifficultyPrompt.BuildPrompt(suite.Name, items);

        var runRequest = new AgentRunRequest
        {
            ProviderName = assessorConfig.Provider,
            ModelId = assessorConfig.ModelId,
            ApiKey = assessorApiKey,
            ModelDisplayName = assessorConfig.DisplayName,
            SystemPrompt = "You are an objective game mechanics expert. Rate the difficulty of the questions based strictly on the JSON schema requested.",
            ThinkingLevel = assessorConfig.ThinkingLevel,
            ReasoningMode = assessorConfig.ReasoningMode,
            ReasoningSummary = assessorConfig.ReasoningSummary,
            ServiceTier = assessorConfig.ServiceTier,
            MaxOutputTokens = assessorConfig.MaxOutputTokens ?? 4096,
            MaxToolIterations = 0,
            EnableToolUse = false,
            EnableWebSearch = false,
            EnableSubAgents = false,
            SystemModelId = assessorConfig.Id,
            Budget = new AgentRunBudget { MaxTotalModelCalls = 2 },
            SeedHistory = new List<object>
            {
                new { role = "user", content = prompt }
            }
        };

        var runResult = new AgentRunResult();
        var sw = Stopwatch.StartNew();
        await foreach (var _ in _agentLoopRunner.RunAsync(runRequest, runRequest.Budget, runResult, cancellationToken)) { }
        sw.Stop();

        var parseResult = BenchmarkDifficultyParser.Parse(runResult.FinalText);
        if (!parseResult.Success)
        {
            return (false, 0, parseResult.ErrorMessage ?? "Failed to parse suite difficulty ratings.");
        }

        var ratingsById = parseResult.Items.ToDictionary(i => i.Id);
        int ratedCount = 0;

        foreach (var q in questionsToRate)
        {
            if (ratingsById.TryGetValue(q.Id, out var item))
            {
                BenchmarkQuestionAssessment.ApplySnapshot(q, item.Difficulty, assessorConfig, DateTime.UtcNow);
                ratedCount++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        try
        {
            await configService.RecordUsageAsync(
                assessorConfig.Id,
                string.Empty,
                runResult.TotalPromptTokens > 0 ? runResult.TotalPromptTokens : runResult.EstimatedInputTokens,
                runResult.OutputTokens > 0 ? runResult.OutputTokens : runResult.EstimatedOutputTokens,
                roleContext: 4,
                cacheReadTokens: runResult.CacheReadTokens,
                cacheCreationTokens: runResult.CacheCreationTokens,
                totalDurationMs: (int)sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record usage for suite difficulty rating.");
        }

        return (true, ratedCount, null);
    }

    public async Task<(bool Success, string? ErrorMessage)> RescoreRunAsync(long runId, long? targetProfileId = null)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var run = await db.BenchmarkRuns
            .Include(r => r.Answers)
            .FirstOrDefaultAsync(r => r.Id == runId);

        if (run == null)
        {
            return (false, "Run not found.");
        }

        var answersWithLevels = run.Answers
            .Where(a => a.AccuracyLevel.HasValue && a.CompletenessLevel.HasValue && a.ConcisenessLevel.HasValue && a.ReadabilityLevel.HasValue)
            .ToList();

        if (answersWithLevels.Count == 0)
        {
            return (false, "Run does not contain dimensional level ratings (legacy Round-1 run). Re-scoring requires anchored dimensional levels.");
        }

        BenchmarkScoringProfile profile;
        if (targetProfileId.HasValue)
        {
            profile = await _scoringProfileService.GetProfileByIdAsync(targetProfileId.Value) ??
                      await _scoringProfileService.GetDefaultProfileAsync();
        }
        else if (run.ScoringProfileId.HasValue)
        {
            profile = await _scoringProfileService.GetProfileByIdAsync(run.ScoringProfileId.Value) ??
                      await _scoringProfileService.GetDefaultProfileAsync();
        }
        else
        {
            profile = await _scoringProfileService.GetDefaultProfileAsync();
        }

        var constants = _scoringProfileService.ToConstants(profile);

        run.ScoringProfileId = profile.Id;
        run.ScoringProfileSnapshotJson = JsonSerializer.Serialize(profile);
        run.ScoringMethodVersion = BenchmarkAssessmentPrompt.ScoringMethodVersion;

        foreach (var a in answersWithLevels)
        {
            a.AccuracyScore = BenchmarkScoring.Score(a.AccuracyLevel!.Value, constants.LevelScores);
            a.CompletenessScore = BenchmarkScoring.Score(a.CompletenessLevel!.Value, constants.LevelScores);
            a.ConcisenessScore = BenchmarkScoring.Score(a.ConcisenessLevel!.Value, constants.LevelScores);
            a.ReadabilityScore = BenchmarkScoring.Score(a.ReadabilityLevel!.Value, constants.LevelScores);

            var (quality, _) = BenchmarkScoring.Quality(
                a.AccuracyLevel.Value, a.CompletenessLevel.Value, a.ConcisenessLevel.Value, a.ReadabilityLevel.Value,
                a.CriticalError, constants);

            a.QualityScore = quality;
            a.SpeedScore = BenchmarkScoring.Speed(a.DurationMs, constants);
            a.Score = quality;
        }

        var scorableItems = run.Answers
            .Where(a => a.Status == BenchmarkAnswerStatus.Ok && a.QualityScore.HasValue)
            .Select(a => (a.QualityScore, a.AssessedDifficulty ?? GetFallbackDifficulty(a.Difficulty)))
            .ToList();

        var speedItems = run.Answers
            .Where(a => a.Status == BenchmarkAnswerStatus.Ok && a.SpeedScore.HasValue)
            .Select(a => (a.SpeedScore, a.AssessedDifficulty ?? GetFallbackDifficulty(a.Difficulty)))
            .ToList();

        run.QualityIndex = BenchmarkScoring.QualityIndex(scorableItems);
        run.SpeedIndex = BenchmarkScoring.SpeedIndex(speedItems);

        await db.SaveChangesAsync();
        _logger.LogInformation("Successfully re-scored benchmark run {RunId} using profile '{ProfileName}'.", runId, profile.Name);
        return (true, null);
    }

    public async Task<(bool Success, string? ErrorMessage)> ReassessQuestionAsync(
        long answerId,
        long? assessorConfigId = null,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var configService = scope.ServiceProvider.GetRequiredService<SystemAiConfigService>();

        var answer = await db.BenchmarkRunAnswers
            .Include(a => a.BenchmarkRun)
            .ThenInclude(r => r.BenchmarkSuite)
            .ThenInclude(s => s!.Questions)
            .Include(a => a.BenchmarkRun.AssessorModelConfiguration)
            .FirstOrDefaultAsync(a => a.Id == answerId, cancellationToken);

        if (answer == null)
        {
            return (false, "Answer not found.");
        }

        var run = answer.BenchmarkRun;
        var assessorConfig = assessorConfigId.HasValue
            ? await db.SystemAiApiConfigurations.FindAsync(assessorConfigId.Value)
            : run.AssessorModelConfiguration;

        if (assessorConfig == null || string.IsNullOrWhiteSpace(assessorConfig.EncryptedApiKey))
        {
            return (false, "Assessor configuration missing or has no API key.");
        }

        string assessorApiKey = _cryptoService.Decrypt(assessorConfig.EncryptedApiKey, assessorConfig.ApiKeyNonce!, assessorConfig.ApiKeyTag!, "SYSTEM_API_KEY");

        var profile = run.ScoringProfileId.HasValue
            ? await _scoringProfileService.GetProfileByIdAsync(run.ScoringProfileId.Value) ?? await _scoringProfileService.GetDefaultProfileAsync()
            : await _scoringProfileService.GetDefaultProfileAsync();
        var constants = _scoringProfileService.ToConstants(profile);

        string? expectedPoints = null;
        if (run.BenchmarkSuite != null)
        {
            var suiteQ = run.BenchmarkSuite.Questions.FirstOrDefault(q => q.OrderIndex == answer.OrderIndex);
            expectedPoints = suiteQ?.ExpectedPoints;
        }

        await ExecutePerQuestionAssessmentAsync(
            db, configService, run, answer, expectedPoints,
            assessorConfig, assessorApiKey, constants, cancellationToken);

        // Recompute run indices
        var allAnswers = await db.BenchmarkRunAnswers
            .Where(a => a.BenchmarkRunId == run.Id)
            .ToListAsync(cancellationToken);

        var scorableItems = allAnswers
            .Where(a => a.Status == BenchmarkAnswerStatus.Ok && a.QualityScore.HasValue)
            .Select(a => (a.QualityScore, a.AssessedDifficulty ?? GetFallbackDifficulty(a.Difficulty)))
            .ToList();

        var speedItems = allAnswers
            .Where(a => a.Status == BenchmarkAnswerStatus.Ok && a.SpeedScore.HasValue)
            .Select(a => (a.SpeedScore, a.AssessedDifficulty ?? GetFallbackDifficulty(a.Difficulty)))
            .ToList();

        run.QualityIndex = BenchmarkScoring.QualityIndex(scorableItems);
        run.SpeedIndex = BenchmarkScoring.SpeedIndex(speedItems);

        await db.SaveChangesAsync(CancellationToken.None);
        return (true, null);
    }

    private static int GetFallbackDifficulty(BenchmarkDifficulty diff) => diff switch
    {
        BenchmarkDifficulty.Simple => 25,
        BenchmarkDifficulty.Intermediate => 55,
        BenchmarkDifficulty.Advanced => 85,
        _ => 50
    };
}
