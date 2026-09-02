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
    private readonly BenchmarkDifficultyJobManager _difficultyJobManager;
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
        BenchmarkDifficultyJobManager difficultyJobManager,
        BenchmarkScoringProfileService scoringProfileService,
        IConfiguration configuration,
        ILogger<BenchmarkService> logger)
    {
        _scopeFactory = scopeFactory;
        _chatService = chatService;
        _agentLoopRunner = agentLoopRunner;
        _cryptoService = cryptoService;
        _runManager = runManager;
        _difficultyJobManager = difficultyJobManager;
        _scoringProfileService = scoringProfileService;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task CleanupOrphanedRunsAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var orphanedRuns = await db.BenchmarkRuns
            .Include(r => r.Answers)
            .Where(r => r.Status == BenchmarkRunStatus.Running)
            .ToListAsync();

        if (orphanedRuns.Count > 0)
        {
            foreach (var run in orphanedRuns)
            {
                if (run.Answers.Count == 0)
                {
                    run.Status = BenchmarkRunStatus.Failed;
                }
                else
                {
                    BenchmarkRunFinalizer.Apply(run, run.Answers);
                }
                run.ErrorMessage = "Run interrupted by application restart.";
                run.CompletedAtUtc = DateTime.UtcNow;
            }
            await db.SaveChangesAsync();
            _logger.LogInformation("Cleaned up {Count} orphaned benchmark runs.", orphanedRuns.Count);
        }
    }

    private async Task<(SystemAiApiConfiguration? Config, string? ApiKey, string? Error)> ResolveAssessorAsync(
        ApplicationDbContext db, BenchmarkRun run, long? overrideConfigId, CancellationToken ct)
    {
        SystemAiApiConfiguration? config;
        if (overrideConfigId.HasValue)
        {
            config = await db.SystemAiApiConfigurations.FirstOrDefaultAsync(c => c.Id == overrideConfigId.Value, ct);
            if (config == null)
            {
                return (null, null, "The specified assessor configuration was not found.");
            }
        }
        else
        {
            config = run.AssessorModelConfiguration ??
                (run.AssessorModelConfigurationId.HasValue
                    ? await db.SystemAiApiConfigurations.FirstOrDefaultAsync(c => c.Id == run.AssessorModelConfigurationId.Value, ct)
                    : null);
            if (config == null)
            {
                return (null, null, "Run assessor configuration was not found.");
            }
        }

        if (!config.IsEnabled)
        {
            return (null, null, "The assessor configuration is disabled.");
        }

        if (string.IsNullOrWhiteSpace(config.EncryptedApiKey))
        {
            return (null, null, "The assessor configuration has no API key.");
        }

        if ((config.ModelRole & 4) != 4)
        {
            return (null, null, "The assessor configuration does not have the Benchmark role.");
        }

        string apiKey = _cryptoService.Decrypt(config.EncryptedApiKey, config.ApiKeyNonce!, config.ApiKeyTag!, "SYSTEM_API_KEY");
        return (config, apiKey, null);
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
            run.HarnessVersion = _configuration.GetValue<string>("Benchmark:HarnessVersion", "2");

            string testedApiKey = _cryptoService.Decrypt(testedConfig.EncryptedApiKey, testedConfig.ApiKeyNonce!, testedConfig.ApiKeyTag!, "SYSTEM_API_KEY");
            string assessorApiKey = _cryptoService.Decrypt(assessorConfig.EncryptedApiKey, assessorConfig.ApiKeyNonce!, assessorConfig.ApiKeyTag!, "SYSTEM_API_KEY");

            var questions = (run.BenchmarkSuite?.Questions ?? new List<BenchmarkQuestion>())
                .OrderBy(q => q.OrderIndex)
                .ToList();



            run.TotalQuestionCount = questions.Count;
            int maxParallel = profile.MaxParallelQuestions;
            run.MaxParallelQuestionsUsed = maxParallel;
            run.SpeedMeasurementDegraded = maxParallel > 1;

            var allowedTools = _configuration.GetSection("Benchmark:AllowedTools").Get<List<string>>() ?? _defaultAllowedTools;
            int maxToolIterations = _configuration.GetValue<int>("Benchmark:MaxToolIterations", 8);
            int maxTotalModelCalls = _configuration.GetValue<int>("Benchmark:MaxTotalModelCalls", 12);
            int maxResultLength = _configuration.GetValue<int>("Benchmark:MaxResultLength", 10000);
            int maxCallsPerSession = _configuration.GetValue<int>("Benchmark:MaxCallsPerSession", 50);
            int maxToolCallsPerQuestion = _configuration.GetValue<int>("Benchmark:MaxToolCallsPerQuestion", 25);
            run.MaxToolCallsPerQuestionUsed = maxToolCallsPerQuestion;
            await db.SaveChangesAsync(cancellationToken);

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
                        maxResultLength, maxToolCallsPerQuestion, cancellationToken);

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
                            maxResultLength, maxToolCallsPerQuestion, cancellationToken);

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

            var allAnswers = await db.BenchmarkRunAnswers.Where(a => a.BenchmarkRunId == run.Id).ToListAsync(CancellationToken.None);
            BenchmarkRunFinalizer.Apply(run, allAnswers);
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
                run.ErrorMessage = BenchmarkAssessmentFailure.Truncate(ex.Message);
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

            var allAnswers = await db.BenchmarkRunAnswers.Where(a => a.BenchmarkRunId == run.Id).ToListAsync(CancellationToken.None);
            BenchmarkRunFinalizer.Apply(run, allAnswers);
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
                ToolBudgetScopeId = $"bench_{run.Id}_q{question.OrderIndex}",
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

        int perQuestionTimeoutSec = _configuration.GetValue<int>("Benchmark:PerQuestionTimeoutSeconds", 300);
        using var questionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        questionCts.CancelAfter(TimeSpan.FromSeconds(perQuestionTimeoutSec));

        var runResult = new AgentRunResult();
        var sw = Stopwatch.StartNew();

        string? terminalError = null;
        try
        {
            await foreach (var evt in _agentLoopRunner.RunAsync(runRequest, runRequest.Budget, runResult, questionCts.Token))
            {
                if (evt.Type == "error")
                {
                    terminalError = evt.Data?.ToString();
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && questionCts.IsCancellationRequested)
        {
            terminalError = $"Per-question timeout exceeded ({perQuestionTimeoutSec} s).";
        }
        catch (Exception ex)
        {
            terminalError = ex.Message;
        }
        sw.Stop();

        var classification = BenchmarkProviderErrorClassifier.Classify(terminalError);
        var sanitized = BenchmarkAnswerSanitizer.Sanitize(runResult.FinalText);

        var succeededCalls = runResult.ToolCalls
            .Where(tc => tc.Status == "completed" && string.IsNullOrEmpty(tc.Error) && !string.IsNullOrEmpty(tc.Name))
            .GroupBy(tc => tc.Name!)
            .Select(g => $"{g.Key}×{g.Count()}")
            .ToList();

        int blockedCount = runResult.ToolCalls.Count(tc => tc.Error != null && tc.Error.Contains("Maximum tool calls per session exceeded"));
        string toolSummary = string.Join(", ", succeededCalls);
        if (blockedCount > 0)
        {
            toolSummary = string.IsNullOrEmpty(toolSummary)
                ? $"None ({blockedCount} blocked by budget)"
                : $"{toolSummary} ({blockedCount} blocked by budget)";
        }

        int assessedDiff = question.AssessedDifficulty ?? BenchmarkRunFinalizer.FallbackDifficulty(question.Difficulty);

        BenchmarkAnswerStatus status;
        if (classification.IsProviderError)
        {
            status = BenchmarkAnswerStatus.ProviderError;
        }
        else if (!string.IsNullOrEmpty(terminalError))
        {
            status = BenchmarkAnswerStatus.Failed;
        }
        else if (sanitized.Flags.HasFlag(BenchmarkAnswerFlags.Empty))
        {
            status = BenchmarkAnswerStatus.EmptyAnswer;
        }
        else
        {
            status = BenchmarkAnswerStatus.Ok;
        }

        var answer = new BenchmarkRunAnswer
        {
            BenchmarkRunId = run.Id,
            OrderIndex = question.OrderIndex,
            QuestionText = question.QuestionText,
            Difficulty = question.Difficulty,
            AssessedDifficulty = assessedDiff,
            AnswerText = sanitized.AnswerText,
            ThoughtText = sanitized.ThoughtText,
            Status = status,
            AssessmentStatus = BenchmarkAssessmentStatus.Pending,
            ErrorMessage = BenchmarkAssessmentFailure.Truncate(terminalError),
            HttpStatusCode = classification.HttpStatus,
            DurationMs = runResult.TotalDurationMs ?? sw.ElapsedMilliseconds,
            TimeToFirstTokenMs = runResult.TimeToFirstTokenMs,
            ActualServiceTierUsed = runResult.ActualServiceTier,
            ToolCallSummary = string.IsNullOrEmpty(toolSummary) ? null : toolSummary,
            InputTokens = runResult.TotalPromptTokens > 0 ? runResult.TotalPromptTokens : runResult.EstimatedInputTokens,
            OutputTokens = runResult.OutputTokens > 0 ? runResult.OutputTokens : runResult.EstimatedOutputTokens,
            CacheReadInputTokens = runResult.CacheReadTokens,
            CacheCreationInputTokens = runResult.CacheCreationTokens,
            ModelCallCount = runResult.ModelCallCount,
            ToolCallCount = runResult.ToolCallCount,
            ToolBudgetExhausted = runResult.ToolBudgetExhausted,
            TerminationReason = runResult.TerminationReason,
            AnswerFlags = (int)sanitized.Flags
        };

        db.BenchmarkRunAnswers.Add(answer);
        await db.SaveChangesAsync(CancellationToken.None);

        try
        {
            await configService.RecordUsageAsync(
                testedConfig.Id,
                run.StartedByUserId,
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
                ToolBudgetScopeId = $"bench_{run.Id}_q{answer.OrderIndex}",
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

        int perQuestionTimeoutSec = _configuration.GetValue<int>("Benchmark:PerQuestionTimeoutSeconds", 300);
        using var questionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        questionCts.CancelAfter(TimeSpan.FromSeconds(perQuestionTimeoutSec));

        var runResult = new AgentRunResult();
        var sw = Stopwatch.StartNew();

        string? terminalError = null;
        try
        {
            await foreach (var evt in _agentLoopRunner.RunAsync(runRequest, runRequest.Budget, runResult, questionCts.Token))
            {
                if (evt.Type == "error")
                {
                    terminalError = evt.Data?.ToString();
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && questionCts.IsCancellationRequested)
        {
            terminalError = $"Per-question timeout exceeded ({perQuestionTimeoutSec} s).";
        }
        catch (Exception ex)
        {
            terminalError = ex.Message;
        }
        sw.Stop();

        var classification = BenchmarkProviderErrorClassifier.Classify(terminalError);
        var sanitized = BenchmarkAnswerSanitizer.Sanitize(runResult.FinalText);

        var succeededCalls = runResult.ToolCalls
            .Where(tc => tc.Status == "completed" && string.IsNullOrEmpty(tc.Error) && !string.IsNullOrEmpty(tc.Name))
            .GroupBy(tc => tc.Name!)
            .Select(g => $"{g.Key}×{g.Count()}")
            .ToList();

        int blockedCount = runResult.ToolCalls.Count(tc => tc.Error != null && tc.Error.Contains("Maximum tool calls per session exceeded"));
        string toolSummary = string.Join(", ", succeededCalls);
        if (blockedCount > 0)
        {
            toolSummary = string.IsNullOrEmpty(toolSummary)
                ? $"None ({blockedCount} blocked by budget)"
                : $"{toolSummary} ({blockedCount} blocked by budget)";
        }

        BenchmarkAnswerStatus status;
        if (classification.IsProviderError)
        {
            status = BenchmarkAnswerStatus.ProviderError;
        }
        else if (!string.IsNullOrEmpty(terminalError))
        {
            status = BenchmarkAnswerStatus.Failed;
        }
        else if (sanitized.Flags.HasFlag(BenchmarkAnswerFlags.Empty))
        {
            status = BenchmarkAnswerStatus.EmptyAnswer;
        }
        else
        {
            status = BenchmarkAnswerStatus.Ok;
        }

        answer.AnswerText = sanitized.AnswerText;
        answer.ThoughtText = sanitized.ThoughtText;
        answer.Status = status;
        answer.AssessmentStatus = BenchmarkAssessmentStatus.Pending;
        answer.ErrorMessage = BenchmarkAssessmentFailure.Truncate(terminalError);
        answer.HttpStatusCode = classification.HttpStatus;
        answer.DurationMs = runResult.TotalDurationMs ?? sw.ElapsedMilliseconds;
        answer.TimeToFirstTokenMs = runResult.TimeToFirstTokenMs;
        answer.ActualServiceTierUsed = runResult.ActualServiceTier;
        answer.ToolCallSummary = string.IsNullOrEmpty(toolSummary) ? null : toolSummary;
        answer.InputTokens = runResult.TotalPromptTokens > 0 ? runResult.TotalPromptTokens : runResult.EstimatedInputTokens;
        answer.OutputTokens = runResult.OutputTokens > 0 ? runResult.OutputTokens : runResult.EstimatedOutputTokens;
        answer.CacheReadInputTokens = runResult.CacheReadTokens;
        answer.CacheCreationInputTokens = runResult.CacheCreationTokens;
        answer.ModelCallCount = runResult.ModelCallCount;
        answer.ToolCallCount = runResult.ToolCallCount;
        answer.ToolBudgetExhausted = runResult.ToolBudgetExhausted;
        answer.TerminationReason = runResult.TerminationReason;
        answer.AnswerFlags = (int)sanitized.Flags;

        await db.SaveChangesAsync(CancellationToken.None);

        try
        {
            await configService.RecordUsageAsync(
                testedConfig.Id,
                run.StartedByUserId,
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

        var allowedTools = _configuration.GetSection("Benchmark:AllowedTools").Get<List<string>>() ?? _defaultAllowedTools;
        string prompt = BenchmarkAssessmentPrompt.BuildPerQuestionPrompt(
            run.SuiteName,
            answer.OrderIndex,
            answer.QuestionText,
            answer.Difficulty,
            expectedPoints,
            answer.AnswerText,
            answer.Status,
            allowedTools,
            answer.ToolCallCount ?? 0,
            answer.ToolBudgetExhausted);

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
        string? terminalError = null;
        try
        {
            await foreach (var evt in _agentLoopRunner.RunAsync(runRequest, runRequest.Budget, runResult, cancellationToken))
            {
                if (evt.Type == "error") terminalError = evt.Data?.ToString();
            }
        }
        catch (OperationCanceledException) { throw; }   // cancellation must still cancel the run
        catch (Exception ex) { terminalError = ex.Message; }
        sw.Stop();

        var parseResult = string.IsNullOrWhiteSpace(terminalError)
            ? BenchmarkAssessmentParser.ParsePerQuestion(runResult.FinalText)
            : new PerQuestionAssessmentParseResult { Success = false, ErrorMessage = terminalError };

        if (string.IsNullOrWhiteSpace(terminalError) && !parseResult.Success)
        {
            _logger.LogWarning("Assessor per-question output failed JSON parsing. Retrying once...");
            runRequest.SeedHistory.Add(new { role = "assistant", content = runResult.FinalText ?? string.Empty });
            runRequest.SeedHistory.Add(new { role = "user", content = $"Your previous response was not valid JSON or could not be parsed: {parseResult.ErrorMessage}. Please output ONLY the raw JSON object according to the schema without any markdown wrapping or extra text." });

            var retryResult = new AgentRunResult();
            try
            {
                await foreach (var evt in _agentLoopRunner.RunAsync(runRequest, runRequest.Budget, retryResult, cancellationToken))
                {
                    if (evt.Type == "error") terminalError = evt.Data?.ToString();
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { terminalError = ex.Message; }

            if (string.IsNullOrWhiteSpace(terminalError))
            {
                parseResult = BenchmarkAssessmentParser.ParsePerQuestion(retryResult.FinalText);
            }
            if (retryResult.TotalPromptTokens > 0) runResult = retryResult;
        }

        if (string.IsNullOrWhiteSpace(terminalError) && parseResult.Success && parseResult.Result != null)
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

            var (qualityScore, rawQualityScore, _) = BenchmarkScoring.Quality(
                res.AccuracyLevel, res.CompletenessLevel, res.ConcisenessLevel, res.ReadabilityLevel,
                res.CriticalError, constants);

            answer.QualityScore = qualityScore;
            answer.RawQualityScore = rawQualityScore;
            answer.SpeedScore = BenchmarkScoring.Speed(answer.DurationMs, constants);
            answer.Score = qualityScore; // Legacy field backfill
            answer.AssessmentStatus = BenchmarkAssessmentStatus.Scored;
            answer.AssessmentError = null;
        }
        else
        {
            var failure = BenchmarkAssessmentFailure.Describe(terminalError, parseResult.ErrorMessage);
            answer.AssessmentStatus = BenchmarkAssessmentStatus.Failed;
            answer.AssessmentError = failure.Message;
            _logger.LogWarning("Benchmark run {RunId} answer {OrderIndex} assessment failed: {Error}",
                run.Id, answer.OrderIndex, failure.Message);
        }

        answer.AssessedByModelConfigurationId = assessorConfig.Id;
        answer.AssessedByModelDisplayNameUsed = assessorConfig.DisplayName;
        answer.AssessedByModelProviderUsed = assessorConfig.Provider;
        answer.AssessedByModelIdUsed = assessorConfig.ModelId;
        answer.AssessedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(CancellationToken.None);

        try
        {
            await configService.RecordUsageAsync(
                assessorConfig.Id,
                run.StartedByUserId,
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
            AssessedDifficulty = a.AssessedDifficulty ?? BenchmarkRunFinalizer.FallbackDifficulty(a.Difficulty),
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
                run.StartedByUserId,
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

    public async Task RunDifficultyAssessmentAsync(string jobId, CancellationToken cancellationToken)
    {
        var job = _difficultyJobManager.TryGet(jobId);
        if (job == null)
        {
            _logger.LogWarning("RunDifficultyAssessmentAsync: job {JobId} not found.", jobId);
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var configService = scope.ServiceProvider.GetRequiredService<SystemAiConfigService>();

        var suite = await db.BenchmarkSuites
            .Include(s => s.Questions)
            .FirstOrDefaultAsync(s => s.Id == job.SuiteId, cancellationToken);

        if (suite == null)
        {
            job.AddLog("Suite not found.", "error");
            job.SetStatus(BenchmarkDifficultyJobStatus.Failed);
            _difficultyJobManager.Complete(job.Id, BenchmarkDifficultyJobStatus.Failed);
            return;
        }

        var assessorConfig = await db.SystemAiApiConfigurations.FindAsync(new object[] { job.AssessorConfigId }, cancellationToken);
        if (assessorConfig == null || string.IsNullOrWhiteSpace(assessorConfig.EncryptedApiKey))
        {
            job.AddLog("Assessor model configuration missing or has no API key.", "error");
            job.SetStatus(BenchmarkDifficultyJobStatus.Failed);
            _difficultyJobManager.Complete(job.Id, BenchmarkDifficultyJobStatus.Failed);
            return;
        }

        string assessorApiKey = _cryptoService.Decrypt(assessorConfig.EncryptedApiKey, assessorConfig.ApiKeyNonce!, assessorConfig.ApiKeyTag!, "SYSTEM_API_KEY");

        var targetQuestionIds = new HashSet<long>(job.Items.Select(i => i.QuestionId));
        var questionsToRate = suite.Questions
            .Where(q => targetQuestionIds.Contains(q.Id))
            .OrderBy(q => q.OrderIndex)
            .ToList();

        if (questionsToRate.Count == 0)
        {
            job.SetStatus(BenchmarkDifficultyJobStatus.Completed);
            _difficultyJobManager.Complete(job.Id, BenchmarkDifficultyJobStatus.Completed);
            return;
        }

        int batchSize = _configuration.GetValue<int>("Benchmark:Difficulty:BatchSize", 4);
        int rawResponseExcerptLength = _configuration.GetValue<int>("Benchmark:Difficulty:RawResponseExcerptLength", 4000);
        int maxModelCalls = 2 * questionsToRate.Count + 8;

        // Second line of defence behind BenchmarkDifficultyFailurePolicy: an error the
        // classifier reads as transient, but which is in fact permanent, would otherwise fail
        // every batch in turn. Counted across batches and reset by any successful parse.
        int maxConsecutiveProviderErrors = _configuration.GetValue<int>(
            "Benchmark:Difficulty:MaxConsecutiveProviderErrors", 3);
        int consecutiveProviderErrors = 0;

        var questionItems = questionsToRate.Select(q => new BenchmarkDifficultyQuestionItem
        {
            Id = q.Id,
            OrderIndex = q.OrderIndex,
            QuestionText = q.QuestionText,
            AuthorBand = q.Difficulty,
            ExpectedPoints = q.ExpectedPoints
        }).ToList();

        var initialBatches = BenchmarkDifficultyBatchPlanner.Plan(questionItems, batchSize);
        var batchQueue = new Queue<IReadOnlyList<BenchmarkDifficultyQuestionItem>>(initialBatches);
        var reattemptedSingleQuestions = new HashSet<long>();

        while (batchQueue.Count > 0)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                job.MarkRemainingSkipped();
                job.SetStatus(BenchmarkDifficultyJobStatus.Cancelled);
                _difficultyJobManager.Complete(job.Id, BenchmarkDifficultyJobStatus.Cancelled);
                return;
            }

            if (job.TotalModelCalls >= maxModelCalls)
            {
                job.AddLog($"Runaway guard triggered: total model calls reached limit ({maxModelCalls}).", "error");
                job.MarkRemainingSkipped();
                job.SetStatus(BenchmarkDifficultyJobStatus.Failed);
                _difficultyJobManager.Complete(job.Id, BenchmarkDifficultyJobStatus.Failed);
                return;
            }

            var currentBatch = batchQueue.Dequeue();
            var batchQuestionIds = currentBatch.Select(q => q.Id).ToList();
            job.UpdateItemsStatus(batchQuestionIds, BenchmarkDifficultyItemStatus.Assessing);

            try
            {
                string prompt = BenchmarkDifficultyPrompt.BuildPrompt(suite.Name, currentBatch);
                int maxOutput = assessorConfig.MaxOutputTokens ?? Math.Clamp(1024 + 768 * currentBatch.Count, 4096, 32768);

                var (runResult, sw, terminalError) = await ExecuteAssessorCallAsync(assessorConfig, assessorApiKey, prompt, maxOutput, cancellationToken);
                await RecordJobUsageAsync(job, configService, assessorConfig, runResult, sw, rawResponseExcerptLength);

                var failureAction = BenchmarkDifficultyFailurePolicy.Decide(terminalError);
                if (failureAction != BenchmarkDifficultyFailureAction.ParseResponse)
                {
                    consecutiveProviderErrors++;
                    string providerExcerpt = GetExcerpt(terminalError, rawResponseExcerptLength);

                    if (failureAction == BenchmarkDifficultyFailureAction.AbortJob)
                    {
                        _logger.LogError("Difficulty assessment aborted: assessor rejected the request for batch [{BatchIds}]: {Error}",
                            string.Join(",", batchQuestionIds), GetExcerpt(terminalError, 1000));
                        foreach (long qId in batchQuestionIds)
                        {
                            job.SetItemFailed(qId, terminalError!);
                        }
                        job.AddLog($"Assessor model rejected the request; aborting job: {terminalError}", "error", providerExcerpt);
                        job.MarkRemainingSkipped();
                        job.SetStatus(BenchmarkDifficultyJobStatus.Failed);
                        _difficultyJobManager.Complete(job.Id, BenchmarkDifficultyJobStatus.Failed);
                        return;
                    }

                    // FailBatch: transient. Fail these questions and move on — a repair prompt
                    // or a smaller batch cannot help an overloaded or rate-limited endpoint.
                    _logger.LogWarning("Difficulty assessment batch [{BatchIds}] failed with a provider error: {Error}",
                        string.Join(",", batchQuestionIds), GetExcerpt(terminalError, 1000));
                    foreach (long qId in batchQuestionIds)
                    {
                        job.SetItemFailed(qId, terminalError!);
                    }
                    job.AddLog($"Provider error assessing batch of {currentBatch.Count} questions: {terminalError}", "error", providerExcerpt);

                    if (consecutiveProviderErrors >= maxConsecutiveProviderErrors)
                    {
                        job.AddLog($"Aborting after {consecutiveProviderErrors} consecutive provider errors.", "error");
                        job.MarkRemainingSkipped();
                        job.SetStatus(BenchmarkDifficultyJobStatus.Failed);
                        _difficultyJobManager.Complete(job.Id, BenchmarkDifficultyJobStatus.Failed);
                        return;
                    }

                    continue;
                }

                var parseResult = BenchmarkDifficultyParser.Parse(runResult.FinalText);

                if (!parseResult.Success)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        job.MarkRemainingSkipped();
                        job.SetStatus(BenchmarkDifficultyJobStatus.Cancelled);
                        _difficultyJobManager.Complete(job.Id, BenchmarkDifficultyJobStatus.Cancelled);
                        return;
                    }

                    if (job.TotalModelCalls >= maxModelCalls)
                    {
                        job.AddLog($"Runaway guard triggered before repair attempt ({maxModelCalls}).", "error");
                        job.MarkRemainingSkipped();
                        job.SetStatus(BenchmarkDifficultyJobStatus.Failed);
                        _difficultyJobManager.Complete(job.Id, BenchmarkDifficultyJobStatus.Failed);
                        return;
                    }

                    string rawExcerpt = GetExcerpt(runResult.FinalText, rawResponseExcerptLength);
                    _logger.LogWarning("Difficulty parse attempt 1 failed for batch [{BatchIds}]. Excerpt: {Excerpt}",
                        string.Join(",", batchQuestionIds),
                        GetExcerpt(runResult.FinalText, 1000));
                    job.AddLog($"Parse attempt 1 failed for batch of {currentBatch.Count} questions. Retrying with repair prompt...", "warning", rawExcerpt);

                    string repairPrompt = BenchmarkDifficultyPrompt.BuildRepairPrompt(suite.Name, currentBatch, rawExcerpt);
                    var (repairResult, repairSw, repairTerminalError) = await ExecuteAssessorCallAsync(assessorConfig, assessorApiKey, repairPrompt, maxOutput, cancellationToken);
                    await RecordJobUsageAsync(job, configService, assessorConfig, repairResult, repairSw, rawResponseExcerptLength);

                    // The repair attempt can hit the same wall. Do not fall through to the
                    // split: splitting a batch the provider refused only multiplies the
                    // refusals.
                    var repairFailureAction = BenchmarkDifficultyFailurePolicy.Decide(repairTerminalError);
                    if (repairFailureAction != BenchmarkDifficultyFailureAction.ParseResponse)
                    {
                        consecutiveProviderErrors++;
                        string repairProviderExcerpt = GetExcerpt(repairTerminalError, rawResponseExcerptLength);
                        _logger.LogWarning("Difficulty repair attempt for batch [{BatchIds}] failed with a provider error: {Error}",
                            string.Join(",", batchQuestionIds), GetExcerpt(repairTerminalError, 1000));

                        foreach (long qId in batchQuestionIds)
                        {
                            job.SetItemFailed(qId, repairTerminalError!);
                        }

                        bool abortAfterRepair =
                            repairFailureAction == BenchmarkDifficultyFailureAction.AbortJob ||
                            consecutiveProviderErrors >= maxConsecutiveProviderErrors;

                        if (abortAfterRepair)
                        {
                            job.AddLog($"Assessor model rejected the repair request; aborting job: {repairTerminalError}", "error", repairProviderExcerpt);
                            job.MarkRemainingSkipped();
                            job.SetStatus(BenchmarkDifficultyJobStatus.Failed);
                            _difficultyJobManager.Complete(job.Id, BenchmarkDifficultyJobStatus.Failed);
                            return;
                        }

                        job.AddLog($"Provider error on repair attempt for batch of {currentBatch.Count} questions: {repairTerminalError}", "error", repairProviderExcerpt);
                        continue;
                    }

                    parseResult = BenchmarkDifficultyParser.Parse(repairResult.FinalText);
                    if (!parseResult.Success)
                    {
                        string repairRawExcerpt = GetExcerpt(repairResult.FinalText, rawResponseExcerptLength);
                        _logger.LogWarning("Difficulty parse attempt 2 failed for batch [{BatchIds}]. Excerpt: {Excerpt}",
                            string.Join(",", batchQuestionIds),
                            GetExcerpt(repairResult.FinalText, 1000));

                        var splitBatches = BenchmarkDifficultyBatchPlanner.Split(currentBatch);
                        if (splitBatches.Count > 0)
                        {
                            job.AddLog($"Parse attempt 2 failed for batch of {currentBatch.Count} questions. Splitting into {splitBatches.Count} smaller batches.", "warning", repairRawExcerpt);
                            foreach (var half in splitBatches)
                            {
                                batchQueue.Enqueue(half);
                            }
                            continue;
                        }
                        else
                        {
                            long failedId = currentBatch[0].Id;
                            string errMsg = parseResult.ErrorMessage ?? "Failed to parse difficulty rating after repair attempt.";
                            job.SetItemFailed(failedId, errMsg);
                            job.AddLog($"Question {failedId} difficulty assessment failed: {errMsg}", "error", repairRawExcerpt);
                            continue;
                        }
                    }
                }

                // The assessor answered and the answer parsed, so whatever provider trouble
                // preceded it has cleared.
                consecutiveProviderErrors = 0;

                if (parseResult.Salvaged)
                {
                    job.AddLog($"Batch of {currentBatch.Count} questions parsed using salvage strategy.", "warning");
                }

                var dbQuestionsInBatch = questionsToRate.Where(q => batchQuestionIds.Contains(q.Id)).ToList();
                var ratingsById = parseResult.Items.ToDictionary(i => i.Id);
                var matchedQuestionIds = new HashSet<long>();

                foreach (var q in dbQuestionsInBatch)
                {
                    if (ratingsById.TryGetValue(q.Id, out var parsedItem))
                    {
                        BenchmarkQuestionAssessment.ApplySnapshot(q, parsedItem.Difficulty, assessorConfig, DateTime.UtcNow);
                        job.SetItemRated(q.Id, parsedItem.Difficulty);
                        matchedQuestionIds.Add(q.Id);
                    }
                }

                var unmatchedParsedItems = parseResult.Items.Where(i => !matchedQuestionIds.Contains(i.Id)).ToList();
                var unratedDbQuestions = dbQuestionsInBatch.Where(q => !matchedQuestionIds.Contains(q.Id)).ToList();

                if (unmatchedParsedItems.Count > 0 && unratedDbQuestions.Count > 0)
                {
                    int matchCount = Math.Min(unmatchedParsedItems.Count, unratedDbQuestions.Count);
                    for (int i = 0; i < matchCount; i++)
                    {
                        var q = unratedDbQuestions[i];
                        var parsedItem = unmatchedParsedItems[i];
                        job.AddLog($"Question ID mismatch: returned id {parsedItem.Id} positionally matched to question {q.Id} (order {q.OrderIndex}).", "warning");
                        _logger.LogWarning("Difficulty assessment ID mismatch: model returned id {ModelId} for question {QuestionId}", parsedItem.Id, q.Id);

                        BenchmarkQuestionAssessment.ApplySnapshot(q, parsedItem.Difficulty, assessorConfig, DateTime.UtcNow);
                        job.SetItemRated(q.Id, parsedItem.Difficulty);
                        matchedQuestionIds.Add(q.Id);
                    }
                }

                await db.SaveChangesAsync(cancellationToken);

                var stillUnrated = currentBatch.Where(q => !matchedQuestionIds.Contains(q.Id)).ToList();
                foreach (var unratedQ in stillUnrated)
                {
                    if (reattemptedSingleQuestions.Add(unratedQ.Id))
                    {
                        job.AddLog($"Question {unratedQ.Id} missing from assessor response; requeuing as single-item batch.", "warning");
                        batchQueue.Enqueue(new List<BenchmarkDifficultyQuestionItem> { unratedQ });
                    }
                    else
                    {
                        job.SetItemFailed(unratedQ.Id, "Question was omitted by the assessor model.");
                        job.AddLog($"Question {unratedQ.Id} omitted by assessor after single re-queue.", "error");
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error processing difficulty assessment batch [{BatchIds}]", string.Join(",", batchQuestionIds));

                string shortDescription = ExceptionDetails.DescribeShort(ex);
                job.AddLog($"Exception assessing batch: {shortDescription}", "error",
                    ExceptionDetails.Describe(ex, rawResponseExcerptLength));

                foreach (long qId in batchQuestionIds)
                {
                    job.SetItemFailed(qId, shortDescription);
                }

                // An Added entity that the database rejected fails identically on every
                // later save in this scope, so one bad insert would otherwise doom every
                // remaining batch. Nothing in this loop legitimately inserts rows.
                // Modified entries are the question updates and are left alone: detaching
                // them would silently discard a rating for a requeued question.
                if (ex is DbUpdateException dbUpdateEx)
                {
                    DetachFailedInserts(dbUpdateEx, job);
                }
            }
        }

        if (cancellationToken.IsCancellationRequested)
        {
            job.MarkRemainingSkipped();
            job.SetStatus(BenchmarkDifficultyJobStatus.Cancelled);
            _difficultyJobManager.Complete(job.Id, BenchmarkDifficultyJobStatus.Cancelled);
        }
        else
        {
            var dto = job.ToDto();
            BenchmarkDifficultyJobStatus finalStatus = dto.FailedCount > 0
                ? BenchmarkDifficultyJobStatus.CompletedWithErrors
                : BenchmarkDifficultyJobStatus.Completed;

            job.SetStatus(finalStatus);
            _difficultyJobManager.Complete(job.Id, finalStatus);
            job.AddLog($"Assessment finished with status: {finalStatus}. Rated: {dto.RatedCount}, Failed: {dto.FailedCount}.", "info");
        }
    }

    /// <summary>
    /// Detaches entities a failed insert left in the change tracker, so the next save in
    /// the same scope is not doomed to repeat the same failure.
    /// </summary>
    private void DetachFailedInserts(DbUpdateException ex, BenchmarkDifficultyJob job)
    {
        try
        {
            var added = ex.Entries
                .Where(e => e.State == EntityState.Added)
                .ToList();

            if (added.Count == 0)
            {
                return;
            }

            var typeNames = added
                .Select(e => e.Entity.GetType().Name)
                .Distinct()
                .ToList();

            foreach (var entry in added)
            {
                entry.State = EntityState.Detached;
            }

            job.AddLog($"Discarded {added.Count} rejected pending insert(s) ({string.Join(", ", typeNames)}) so later batches can save.", "warning");
            _logger.LogWarning("Detached {Count} rejected pending insert(s) after a failed save: {Types}",
                added.Count, string.Join(", ", typeNames));
        }
        catch (Exception detachEx)
        {
            _logger.LogWarning(detachEx, "Failed to detach rejected pending inserts after a failed save.");
        }
    }

    private async Task<(AgentRunResult Result, Stopwatch Sw, string? TerminalError)> ExecuteAssessorCallAsync(
        SystemAiApiConfiguration assessorConfig,
        string assessorApiKey,
        string prompt,
        int maxOutputTokens,
        CancellationToken cancellationToken)
    {
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
            MaxOutputTokens = maxOutputTokens,
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

        // A terminal provider error is also appended to the response text by the agent loop,
        // so a caller that swallows these events cannot tell an HTTP 400 apart from a model
        // that answered badly — and would escalate through repair prompts and batch splits
        // against an endpoint that is refusing the request outright.
        string? terminalError = null;
        await foreach (var evt in _agentLoopRunner.RunAsync(runRequest, runRequest.Budget, runResult, cancellationToken))
        {
            if (evt.Type == "error")
            {
                terminalError = evt.Data?.ToString();
            }
        }
        sw.Stop();

        return (runResult, sw, terminalError);
    }

    private async Task RecordJobUsageAsync(
        BenchmarkDifficultyJob job,
        SystemAiConfigService configService,
        SystemAiApiConfiguration assessorConfig,
        AgentRunResult runResult,
        Stopwatch sw,
        int detailExcerptLength)
    {
        int promptTokens = runResult.TotalPromptTokens > 0 ? runResult.TotalPromptTokens : runResult.EstimatedInputTokens;
        int outputTokens = runResult.OutputTokens > 0 ? runResult.OutputTokens : runResult.EstimatedOutputTokens;
        job.RecordModelCall(promptTokens, outputTokens);

        try
        {
            await configService.RecordUsageAsync(
                assessorConfig.Id,
                job.StartedByUserId,
                promptTokens,
                outputTokens,
                roleContext: 4,
                cacheReadTokens: runResult.CacheReadTokens,
                cacheCreationTokens: runResult.CacheCreationTokens,
                totalDurationMs: (int)sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record usage for difficulty assessment call.");

            // Visible in the Diagnostics panel: a usage failure used to reach the ILogger
            // only, which made the batch failures it caused look causeless.
            job.AddLog("Failed to record model usage for this call; assessment continues.", "warning",
                ExceptionDetails.Describe(ex, detailExcerptLength));
        }
    }

    private static string GetExcerpt(string? text, int maxLength)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }
        return text.Length <= maxLength ? text : text.Substring(0, maxLength) + "...";
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

            var (quality, rawQuality, _) = BenchmarkScoring.Quality(
                a.AccuracyLevel.Value, a.CompletenessLevel.Value, a.ConcisenessLevel.Value, a.ReadabilityLevel.Value,
                a.CriticalError, constants);

            a.QualityScore = quality;
            a.RawQualityScore = rawQuality;
            a.SpeedScore = BenchmarkScoring.Speed(a.DurationMs, constants);
            a.Score = quality;
        }

        var scorableItems = run.Answers
            .Where(a => a.Status == BenchmarkAnswerStatus.Ok && a.QualityScore.HasValue)
            .Select(a => (a.QualityScore, a.AssessedDifficulty ?? BenchmarkRunFinalizer.FallbackDifficulty(a.Difficulty)))
            .ToList();

        var speedItems = run.Answers
            .Where(a => a.Status == BenchmarkAnswerStatus.Ok && a.SpeedScore.HasValue)
            .Select(a => (a.SpeedScore, a.AssessedDifficulty ?? BenchmarkRunFinalizer.FallbackDifficulty(a.Difficulty)))
            .ToList();

        run.QualityIndex = BenchmarkScoring.QualityIndex(scorableItems);
        run.SpeedIndex = BenchmarkScoring.SpeedIndex(speedItems);

        await db.SaveChangesAsync();
        _logger.LogInformation("Successfully re-scored benchmark run {RunId} using profile '{ProfileName}'.", runId, profile.Name);
        return (true, null);
    }

    public async Task RerunSingleQuestionAsync(
        long answerId,
        long? assessorConfigId = null,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var configService = scope.ServiceProvider.GetRequiredService<SystemAiConfigService>();

        var answer = await db.BenchmarkRunAnswers
            .Include(a => a.BenchmarkRun).ThenInclude(r => r.TestedModelConfiguration)
            .Include(a => a.BenchmarkRun).ThenInclude(r => r.AssessorModelConfiguration)
            .Include(a => a.BenchmarkRun).ThenInclude(r => r.BenchmarkSuite).ThenInclude(s => s!.Questions)
            .FirstOrDefaultAsync(a => a.Id == answerId, cancellationToken);

        if (answer == null)
        {
            _logger.LogWarning("Answer {AnswerId} not found for rerun.", answerId);
            return;
        }

        var run = answer.BenchmarkRun;
        try
        {
            var testedConfig = run.TestedModelConfiguration;
            if (testedConfig == null || string.IsNullOrWhiteSpace(testedConfig.EncryptedApiKey))
            {
                answer.AssessmentError = BenchmarkAssessmentFailure.Truncate("Tested model configuration missing or has no API key.");
                await db.SaveChangesAsync(CancellationToken.None);
                return;
            }

            var (assessorConfig, assessorApiKey, assessorError) = await ResolveAssessorAsync(db, run, assessorConfigId, cancellationToken);
            if (assessorConfig == null || assessorApiKey == null)
            {
                answer.AssessmentError = BenchmarkAssessmentFailure.Truncate(assessorError);
                await db.SaveChangesAsync(CancellationToken.None);
                return;
            }

            string testedApiKey = _cryptoService.Decrypt(testedConfig.EncryptedApiKey, testedConfig.ApiKeyNonce!, testedConfig.ApiKeyTag!, "SYSTEM_API_KEY");

            run.Status = BenchmarkRunStatus.Running;
            run.CompletedAtUtc = null;
            await db.SaveChangesAsync(cancellationToken);

            answer.AccuracyLevel = null;
            answer.CompletenessLevel = null;
            answer.ConcisenessLevel = null;
            answer.ReadabilityLevel = null;
            answer.AccuracyScore = null;
            answer.CompletenessScore = null;
            answer.ConcisenessScore = null;
            answer.ReadabilityScore = null;
            answer.QualityScore = null;
            answer.SpeedScore = null;
            answer.Score = null;
            answer.CriticalError = false;
            answer.ReviewComment = null;
            answer.AssessmentError = null;
            answer.AssessedByModelConfigurationId = null;
            answer.AssessedByModelDisplayNameUsed = null;
            answer.AssessedByModelProviderUsed = null;
            answer.AssessedByModelIdUsed = null;
            answer.AssessedAtUtc = null;
            answer.AssessmentStatus = BenchmarkAssessmentStatus.Pending;
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
            int maxToolCallsPerQuestion = _configuration.GetValue<int>("Benchmark:MaxToolCallsPerQuestion", 25);

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

            string? expectedPoints = run.BenchmarkSuite?.Questions.FirstOrDefault(q => q.OrderIndex == answer.OrderIndex)?.ExpectedPoints;

            await ReExecuteSingleAnswerAsync(
                db, configService, run, answer, testedConfig, testedApiKey,
                systemPrompt, allowedTools, maxToolIterations, maxTotalModelCalls,
                maxResultLength, maxToolCallsPerQuestion, cancellationToken);

            await ExecutePerQuestionAssessmentAsync(
                db, configService, run, answer, expectedPoints,
                assessorConfig, assessorApiKey, scoringConstants, cancellationToken);

            var allAnswers = await db.BenchmarkRunAnswers
                .Where(a => a.BenchmarkRunId == run.Id)
                .ToListAsync(CancellationToken.None);
            BenchmarkRunFinalizer.Apply(run, allAnswers);
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            run.Status = BenchmarkRunStatus.Canceled;
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Rerun failed for answer {AnswerId}.", answerId);
            run.Status = BenchmarkRunStatus.CompletedWithErrors;
            run.ErrorMessage = BenchmarkAssessmentFailure.Truncate(ex.Message);
            await db.SaveChangesAsync(CancellationToken.None);
        }
        finally
        {
            _runManager.Complete(run.Id);
        }
    }

    public async Task ReassessSingleQuestionAsync(
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
            _logger.LogWarning("Answer {AnswerId} not found for reassessment.", answerId);
            return;
        }

        var run = answer.BenchmarkRun;
        try
        {
            var (assessorConfig, assessorApiKey, error) = await ResolveAssessorAsync(db, run, assessorConfigId, cancellationToken);
            if (assessorConfig == null || assessorApiKey == null)
            {
                answer.AssessmentError = BenchmarkAssessmentFailure.Truncate(error);
                await db.SaveChangesAsync(CancellationToken.None);
                return;
            }

            run.Status = BenchmarkRunStatus.Running;
            run.CompletedAtUtc = null;
            await db.SaveChangesAsync(cancellationToken);

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

            var allAnswers = await db.BenchmarkRunAnswers
                .Where(a => a.BenchmarkRunId == run.Id)
                .ToListAsync(CancellationToken.None);

            BenchmarkRunFinalizer.Apply(run, allAnswers);
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            run.Status = BenchmarkRunStatus.Canceled;
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Reassessment failed for answer {AnswerId}.", answerId);
            answer.AssessmentError = BenchmarkAssessmentFailure.Truncate(ex.Message);
            run.Status = BenchmarkRunStatus.CompletedWithErrors;
            run.ErrorMessage = BenchmarkAssessmentFailure.Truncate(ex.Message);
            await db.SaveChangesAsync(CancellationToken.None);
        }
        finally
        {
            _runManager.Complete(run.Id);
        }
    }

    public async Task RerunFinalSynthesisAsync(
        long runId,
        long? assessorConfigId = null,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var configService = scope.ServiceProvider.GetRequiredService<SystemAiConfigService>();

        var run = await db.BenchmarkRuns
            .Include(r => r.Answers)
            .Include(r => r.AssessorModelConfiguration)
            .FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);

        if (run == null)
        {
            _logger.LogWarning("Run {RunId} not found for final synthesis rerun.", runId);
            _runManager.Complete(runId);
            return;
        }

        try
        {
            var (assessorConfig, assessorApiKey, error) = await ResolveAssessorAsync(db, run, assessorConfigId, cancellationToken);
            if (assessorConfig == null || assessorApiKey == null)
            {
                run.ErrorMessage = BenchmarkAssessmentFailure.Truncate(error);
                await db.SaveChangesAsync(CancellationToken.None);
                return;
            }

            run.Status = BenchmarkRunStatus.Running;
            run.CompletedAtUtc = null;
            await db.SaveChangesAsync(cancellationToken);

            var profile = run.ScoringProfileId.HasValue
                ? await _scoringProfileService.GetProfileByIdAsync(run.ScoringProfileId.Value) ?? await _scoringProfileService.GetDefaultProfileAsync()
                : await _scoringProfileService.GetDefaultProfileAsync();
            var constants = _scoringProfileService.ToConstants(profile);

            await ExecuteFinalSynthesisAsync(db, configService, run, assessorConfig, assessorApiKey, constants, cancellationToken);

            var allAnswers = await db.BenchmarkRunAnswers
                .Where(a => a.BenchmarkRunId == run.Id)
                .ToListAsync(CancellationToken.None);
            BenchmarkRunFinalizer.Apply(run, allAnswers);
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            run.Status = BenchmarkRunStatus.Canceled;
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Final synthesis rerun failed for run {RunId}.", runId);
            run.Status = BenchmarkRunStatus.CompletedWithErrors;
            run.ErrorMessage = BenchmarkAssessmentFailure.Truncate(ex.Message);
            await db.SaveChangesAsync(CancellationToken.None);
        }
        finally
        {
            _runManager.Complete(run.Id);
        }
    }

    public async Task RetryFailedAssessmentsAsync(
        long runId,
        long? assessorConfigId = null,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var configService = scope.ServiceProvider.GetRequiredService<SystemAiConfigService>();

        var run = await db.BenchmarkRuns
            .Include(r => r.Answers)
            .Include(r => r.AssessorModelConfiguration)
            .Include(r => r.BenchmarkSuite)
            .ThenInclude(s => s!.Questions)
            .FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);

        if (run == null)
        {
            _logger.LogWarning("Run {RunId} not found for retry failed assessments.", runId);
            _runManager.Complete(runId);
            return;
        }

        try
        {
            var (assessorConfig, assessorApiKey, error) = await ResolveAssessorAsync(db, run, assessorConfigId, cancellationToken);
            if (assessorConfig == null || assessorApiKey == null)
            {
                run.ErrorMessage = BenchmarkAssessmentFailure.Truncate(error);
                await db.SaveChangesAsync(CancellationToken.None);
                return;
            }

            run.Status = BenchmarkRunStatus.Running;
            run.CompletedAtUtc = null;
            await db.SaveChangesAsync(cancellationToken);

            var profile = run.ScoringProfileId.HasValue
                ? await _scoringProfileService.GetProfileByIdAsync(run.ScoringProfileId.Value) ?? await _scoringProfileService.GetDefaultProfileAsync()
                : await _scoringProfileService.GetDefaultProfileAsync();
            var constants = _scoringProfileService.ToConstants(profile);

            var suiteQuestions = (run.BenchmarkSuite?.Questions ?? new List<BenchmarkQuestion>())
                .ToDictionary(q => q.OrderIndex, q => q.ExpectedPoints);

            var unscoredAnswers = run.Answers
                .Where(a => a.AssessmentStatus != BenchmarkAssessmentStatus.Scored)
                .OrderBy(a => a.OrderIndex)
                .ToList();

            foreach (var answer in unscoredAnswers)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    run.Status = BenchmarkRunStatus.Canceled;
                    await db.SaveChangesAsync(CancellationToken.None);
                    return;
                }

                string? expectedPoints = suiteQuestions.TryGetValue(answer.OrderIndex, out var ep) ? ep : null;
                await ExecutePerQuestionAssessmentAsync(
                    db, configService, run, answer, expectedPoints,
                    assessorConfig, assessorApiKey, constants, cancellationToken);
            }

            var allAnswers = await db.BenchmarkRunAnswers
                .Where(a => a.BenchmarkRunId == run.Id)
                .ToListAsync(CancellationToken.None);
            BenchmarkRunFinalizer.Apply(run, allAnswers);
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            run.Status = BenchmarkRunStatus.Canceled;
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Retry failed assessments failed for run {RunId}.", runId);
            run.Status = BenchmarkRunStatus.CompletedWithErrors;
            run.ErrorMessage = BenchmarkAssessmentFailure.Truncate(ex.Message);
            await db.SaveChangesAsync(CancellationToken.None);
        }
        finally
        {
            _runManager.Complete(run.Id);
        }
    }
}
