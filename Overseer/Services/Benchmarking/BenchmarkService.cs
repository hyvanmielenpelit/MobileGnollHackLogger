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
                .Include(r => r.BenchmarkSuite)
                .ThenInclude(s => s!.GameSnapshot)
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
            run.HarnessVersion = BenchmarkAssessmentPrompt.HarnessVersion;

            // Snapshotted, not read live: the profile can be edited after the run, and the
            // agreement figures below only mean something alongside the coverage that produced
            // them. A per-run override set in the start dialog is already on the run and wins.
            // Without a second-opinion assessor the mode is inert, so it is recorded as Off
            // rather than as a setting that looks like it did something.
            if (run.SecondOpinionModeUsed == 0 && run.SecondOpinionAssessorModelConfigurationId.HasValue)
            {
                run.SecondOpinionModeUsed = (int)scoringConstants.SecondOpinionMode;
            }
            else if (!run.SecondOpinionAssessorModelConfigurationId.HasValue)
            {
                run.SecondOpinionModeUsed = (int)BenchmarkSecondOpinionMode.Off;
            }

            string testedApiKey = _cryptoService.Decrypt(testedConfig.EncryptedApiKey, testedConfig.ApiKeyNonce!, testedConfig.ApiKeyTag!, "SYSTEM_API_KEY");
            string assessorApiKey = _cryptoService.Decrypt(assessorConfig.EncryptedApiKey, assessorConfig.ApiKeyNonce!, assessorConfig.ApiKeyTag!, "SYSTEM_API_KEY");

            var questions = (run.BenchmarkSuite?.Questions ?? new List<BenchmarkQuestion>())
                .OrderBy(q => q.OrderIndex)
                .ToList();



            run.TotalQuestionCount = questions.Count;
            int maxParallel = profile.MaxParallelQuestions;
            run.MaxParallelQuestionsUsed = maxParallel;
            run.SpeedMeasurementDegraded = maxParallel > 1;

            var board = run.BenchmarkSuite?.GameSnapshot;
            if (board != null)
            {
                run.GameSnapshotNameUsed = board.Name;
                run.GameSnapshotSha256Used = board.Sha256;
                run.GameSnapshotCharCountUsed = board.CharCount;
                run.GameSnapshotCaptureMethodUsed = board.CaptureMethod;
            }

            int reviewedCount = questions.Count(q => !q.IsGenerated || (q.ReviewedAtRevision != null && q.ReviewedAtRevision == q.ItemRevision));
            run.SuiteReviewedQuestionCountAtStart = reviewedCount;
            run.SuiteQuestionsReviewed = questions.Count > 0 && !questions.Any(q => q.IsGenerated && (q.ReviewedAtRevision == null || q.ReviewedAtRevision != q.ItemRevision));

            var allowedTools = _configuration.GetSection("Benchmark:AllowedTools").Get<List<string>>() ?? _defaultAllowedTools;
            int maxResultLength = _configuration.GetValue<int>("Benchmark:MaxResultLength", 10000);
            int maxCallsPerSession = _configuration.GetValue<int>("Benchmark:MaxCallsPerSession", 50);
            // Budgets are resolved per band inside ExecuteSingleQuestionAsync; this run-level
            // column records the largest of them, which is the Advanced band's. Resolving it
            // through ResolveToolCallBudget rather than the band default keeps a configuration
            // override visible here. BenchmarkRunAnswer.ToolCallBudgetUsed is the figure that
            // actually applied to a given question.
            int maxToolCallsPerQuestion = ResolveToolCallBudget(BenchmarkDifficulty.Advanced, null);
            run.MaxToolCallsPerQuestionUsed = maxToolCallsPerQuestion;
            await db.SaveChangesAsync(cancellationToken);

            bool suiteHasBoard = run.BenchmarkSuite?.GameSnapshot != null;
            string systemPrompt = _chatService.BuildSystemPrompt(
                wikiContext: Array.Empty<string>(),
                spoilerFreeMode: false,
                verboseMode: false,
                isGameOn: false,
                developerMode: false,
                overseerMode: 0,
                hasGameSnapshot: suiteHasBoard,
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
                        systemPrompt, allowedTools,
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
                            systemPrompt, allowedTools,
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

            // Stage 3, in FlaggedAndOutliers mode only: answers far below this run's own median.
            // It has to wait for every answer because it needs that median, which is the whole
            // reason it is a separate stage rather than another per-answer trigger. Placed before
            // synthesis so the synthesis sees the run in its final graded state.
            await RunOutlierSweepAsync(db, configService, run, scoringConstants, cancellationToken);

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
                .Include(r => r.BenchmarkSuite)
                .ThenInclude(s => s!.GameSnapshot)
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
            int maxResultLength = _configuration.GetValue<int>("Benchmark:MaxResultLength", 10000);
            int maxCallsPerSession = _configuration.GetValue<int>("Benchmark:MaxCallsPerSession", 50);

            bool suiteHasBoard = run.BenchmarkSuite?.GameSnapshot != null;
            string systemPrompt = _chatService.BuildSystemPrompt(
                wikiContext: Array.Empty<string>(),
                spoilerFreeMode: false,
                verboseMode: false,
                isGameOn: false,
                developerMode: false,
                overseerMode: 0,
                hasGameSnapshot: suiteHasBoard,
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
                    systemPrompt, allowedTools,
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

    /// <summary>
    /// The difficulty band whose caps apply to a question.
    ///
    /// Prefer the assessed difficulty: the authored band is the question writer's estimate, and
    /// on the reference run Q2 was authored Simple yet consumed all 25 calls. All four
    /// per-question caps resolve through this one helper so a question cannot take its budget
    /// from one band and its timeout from another — a mismatch nothing downstream could detect.
    /// </summary>
    private static BenchmarkDifficulty BandFor(BenchmarkDifficulty authoredBand, int? assessedDifficulty)
        => assessedDifficulty.HasValue
            ? BenchmarkDifficultyBands.BandOf(assessedDifficulty.Value)
            : authoredBand;

    /// <summary>
    /// Reads a banded per-question cap, falling back to the supplied band default.
    ///
    /// The banded keys sit under their own section prefix on purpose: a configuration key cannot
    /// be both a value and a section, so a flat <c>Benchmark:MaxFoo</c> and a banded
    /// <c>Benchmark:MaxFoo:{Band}</c> cannot coexist — whichever was read second would break.
    /// The flat keys the four banded sections replaced (<c>MaxToolCallsPerQuestion</c>,
    /// <c>MaxToolIterations</c>, <c>MaxTotalModelCalls</c>, <c>PerQuestionTimeoutSeconds</c>)
    /// were removed rather than kept as fallbacks, so there is exactly one place an operator can
    /// set each cap.
    /// </summary>
    private int ResolveBandedCap(string section, BenchmarkDifficulty band, int bandDefault)
    {
        int banded = _configuration.GetValue<int>($"Benchmark:{section}:{band}", 0);
        return banded > 0 ? banded : bandDefault;
    }

    /// <summary>
    /// Total tool calls a question may execute. This is the cap that is meant to bind on a
    /// saturated question: exhausting it blocks further calls, flags the answer
    /// <c>ToolBudgetExhausted</c>, and is explained in the run report. The other three caps are
    /// sized so they do not bind first.
    /// </summary>
    private int ResolveToolCallBudget(BenchmarkDifficulty authoredBand, int? assessedDifficulty)
    {
        var band = BandFor(authoredBand, assessedDifficulty);
        return ResolveBandedCap("ToolCallBudget", band, DefaultToolCallBudget(band));
    }

    /// <summary>
    /// Sequential tool rounds — one model call plus the batch of tool calls it emitted, then the
    /// results fed back. This bounds an investigation's *depth*, not its width: a model batching
    /// three calls per round spends three times the budget per iteration, and the 2026-09-03 run
    /// batched at roughly that rate when saturated. Sized at about half the tool call budget, so
    /// a model batching two calls per round can still spend the whole budget.
    /// </summary>
    private int ResolveToolIterations(BenchmarkDifficulty authoredBand, int? assessedDifficulty)
    {
        var band = BandFor(authoredBand, assessedDifficulty);
        return ResolveBandedCap("ToolIterations", band, DefaultToolIterations(band));
    }

    /// <summary>
    /// Total provider requests for the question. A runaway-loop safety net, not a tuning knob:
    /// hitting it forces a final response with only a debug line to show for it, so it is sized
    /// four to six above the iteration cap and must never be the cap that stops a healthy
    /// question.
    /// </summary>
    private int ResolveTotalModelCalls(BenchmarkDifficulty authoredBand, int? assessedDifficulty)
    {
        var band = BandFor(authoredBand, assessedDifficulty);
        return ResolveBandedCap("TotalModelCalls", band, DefaultTotalModelCalls(band));
    }

    /// <summary>
    /// The per-question wall-clock timeout.
    ///
    /// Banded rather than flat because it is pinned between two constraints. From above, a
    /// saturated Advanced question spending 45 tool calls over 22 rounds approaches the old flat
    /// 300 s. From below, <see cref="BenchmarkScoringConstants.SpeedTargetMs"/> and
    /// <see cref="BenchmarkScoringConstants.SpeedDecayK"/> are pinned to the invariant that the
    /// speed score floor stays unreachable within this timeout at every difficulty; the binding
    /// case inside a band is its *lowest* difficulty, which has the smallest speed target and so
    /// the earliest floor. A flat 720 s would put the Simple band's floor (about 468 s) 300 s
    /// inside the timeout and flatten the Speed Index — the exact failure those constants exist
    /// to avoid. <c>BenchmarkScoringTests</c> asserts the margins.
    /// </summary>
    private int ResolveQuestionTimeoutSeconds(BenchmarkDifficulty authoredBand, int? assessedDifficulty)
    {
        var band = BandFor(authoredBand, assessedDifficulty);
        return ResolveBandedCap("QuestionTimeoutSeconds", band, DefaultQuestionTimeoutSeconds(band));
    }

    internal static int DefaultToolCallBudget(BenchmarkDifficulty band) => band switch
    {
        BenchmarkDifficulty.Simple => 25,
        BenchmarkDifficulty.Intermediate => 35,
        BenchmarkDifficulty.Advanced => 45,
        _ => 35
    };

    internal static int DefaultToolIterations(BenchmarkDifficulty band) => band switch
    {
        BenchmarkDifficulty.Simple => 12,
        BenchmarkDifficulty.Intermediate => 16,
        BenchmarkDifficulty.Advanced => 22,
        _ => 16
    };

    internal static int DefaultTotalModelCalls(BenchmarkDifficulty band) => band switch
    {
        BenchmarkDifficulty.Simple => 16,
        BenchmarkDifficulty.Intermediate => 22,
        BenchmarkDifficulty.Advanced => 28,
        _ => 22
    };

    internal static int DefaultQuestionTimeoutSeconds(BenchmarkDifficulty band) => band switch
    {
        BenchmarkDifficulty.Simple => 420,
        BenchmarkDifficulty.Intermediate => 600,
        BenchmarkDifficulty.Advanced => 720,
        _ => 600
    };

    private static List<object> BuildCandidateSeedHistory(BenchmarkRun run, string questionText)
    {
        var seed = new List<object>();
        var board = run.BenchmarkSuite?.GameSnapshot;
        if (board != null)
        {
            seed.Add(new
            {
                role = "system",
                content = ChatService.GameSnapshotPrefix + "\n" + board.SanitizedText
            });
        }
        seed.Add(new { role = "user", content = questionText });
        return seed;
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
        int maxResultLength,
        int maxCallsPerSession,
        CancellationToken cancellationToken)
    {
        // All four caps are resolved per difficulty band, so they differ between questions in
        // one run. A flat 25 starved advanced questions - Q11, Q16 and Q18 of the 2026-09-03 run
        // each exhausted it and had further calls blocked mid-investigation, which alone moved
        // an otherwise clean run to CompletedWithLimits.
        int toolCallBudget = ResolveToolCallBudget(question.Difficulty, question.AssessedDifficulty);
        int maxToolIterations = ResolveToolIterations(question.Difficulty, question.AssessedDifficulty);
        int maxTotalModelCalls = ResolveTotalModelCalls(question.Difficulty, question.AssessedDifficulty);

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
                MaxCallsPerSession = toolCallBudget,
                ShowDebugLog = false
            },
            SeedHistory = BuildCandidateSeedHistory(run, question.QuestionText)
        };

        int perQuestionTimeoutSec = ResolveQuestionTimeoutSeconds(question.Difficulty, question.AssessedDifficulty);
        using var questionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        questionCts.CancelAfter(TimeSpan.FromSeconds(perQuestionTimeoutSec));

        var runResult = new AgentRunResult();
        var sw = Stopwatch.StartNew();

        string? terminalError = null;

        // The request is about to reach the provider, which is the moment the progress dialog
        // calls this question "Answering". The mark is cleared in the finally below, so a
        // throw, a timeout or a cancellation cannot leave the row stuck in that state.
        _runManager.MarkQuestionInFlight(run.Id, question.OrderIndex);
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
        finally
        {
            _runManager.ClearQuestionInFlight(run.Id, question.OrderIndex);
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

            // The stable identity of the item this answer was produced for. OrderIndex alone was
            // not one: reordering a suite rewrites it and touches no stored answer, so every
            // earlier run then rendered its answers against the wrong questions.
            BenchmarkQuestionId = question.Id,
            ItemRevisionUsed = question.ItemRevision,

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
            ToolCallBudgetUsed = toolCallBudget,
            ToolTimeMs = runResult.ToolTimeMs,
            TerminationReason = runResult.TerminationReason,
            ScrubbedArtifactText = sanitized.ScrubbedArtifactText,
            ScrubbedArtifactCount = sanitized.ScrubbedArtifactCount,
            NarrationBlockCount = sanitized.NarrationBlockCount,
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
        int maxResultLength,
        int maxCallsPerSession,
        CancellationToken cancellationToken)
    {
        int toolCallBudget = ResolveToolCallBudget(answer.Difficulty, answer.AssessedDifficulty);
        int maxToolIterations = ResolveToolIterations(answer.Difficulty, answer.AssessedDifficulty);
        int maxTotalModelCalls = ResolveTotalModelCalls(answer.Difficulty, answer.AssessedDifficulty);

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
                MaxCallsPerSession = toolCallBudget,
                ShowDebugLog = false
            },
            SeedHistory = BuildCandidateSeedHistory(run, answer.QuestionText)
        };

        int perQuestionTimeoutSec = ResolveQuestionTimeoutSeconds(answer.Difficulty, answer.AssessedDifficulty);
        using var questionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        questionCts.CancelAfter(TimeSpan.FromSeconds(perQuestionTimeoutSec));

        var runResult = new AgentRunResult();
        var sw = Stopwatch.StartNew();

        string? terminalError = null;

        // Re-runs show the same Answering state as a first run; see ExecuteSingleQuestionAsync.
        _runManager.MarkQuestionInFlight(run.Id, answer.OrderIndex);
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
        finally
        {
            _runManager.ClearQuestionInFlight(run.Id, answer.OrderIndex);
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
        answer.ToolCallBudgetUsed = toolCallBudget;
        answer.ToolTimeMs = runResult.ToolTimeMs;
        answer.TerminationReason = runResult.TerminationReason;
        answer.ScrubbedArtifactText = sanitized.ScrubbedArtifactText;
        answer.ScrubbedArtifactCount = sanitized.ScrubbedArtifactCount;
        answer.NarrationBlockCount = sanitized.NarrationBlockCount;
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
            answer.ToolBudgetExhausted,
            answer.ScrubbedArtifactCount,
            answer.ToolCallBudgetUsed,
            boardName: run.BenchmarkSuite?.GameSnapshot?.Name,
            boardText: run.BenchmarkSuite?.GameSnapshot?.SanitizedText);

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

        // Accumulated across the retry below, so a run that needed a second attempt reports what
        // it actually consumed. The stopwatch keeps running for the same reason.
        int assessmentInputTokens = runResult.TotalPromptTokens > 0 ? runResult.TotalPromptTokens : runResult.EstimatedInputTokens;
        int assessmentOutputTokens = runResult.OutputTokens > 0 ? runResult.OutputTokens : runResult.EstimatedOutputTokens;

        // The graded text is passed so an unverifiable critical error is demoted rather than
        // capping the question at 25 on an assertion nobody can check.
        var parseResult = string.IsNullOrWhiteSpace(terminalError)
            ? BenchmarkAssessmentParser.ParsePerQuestion(runResult.FinalText, answer.AnswerText)
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
                parseResult = BenchmarkAssessmentParser.ParsePerQuestion(retryResult.FinalText, answer.AnswerText);
            }
            assessmentInputTokens += retryResult.TotalPromptTokens > 0 ? retryResult.TotalPromptTokens : retryResult.EstimatedInputTokens;
            assessmentOutputTokens += retryResult.OutputTokens > 0 ? retryResult.OutputTokens : retryResult.EstimatedOutputTokens;
            if (retryResult.TotalPromptTokens > 0) runResult = retryResult;
        }

        sw.Stop();
        answer.AssessmentInputTokens = assessmentInputTokens;
        answer.AssessmentOutputTokens = assessmentOutputTokens;
        answer.AssessmentDurationMs = sw.ElapsedMilliseconds;

        if (string.IsNullOrWhiteSpace(terminalError) && parseResult.Success && parseResult.Result != null)
        {
            var res = parseResult.Result;
            answer.AccuracyLevel = res.AccuracyLevel;
            answer.CompletenessLevel = res.CompletenessLevel;
            answer.ConcisenessLevel = res.ConcisenessLevel;
            answer.ReadabilityLevel = res.ReadabilityLevel;
            answer.CriticalError = res.CriticalError;
            answer.ReviewComment = res.Comment;
            answer.CriticalErrorQuote = BenchmarkAssessmentFailure.Truncate(res.CriticalErrorQuote, 2048);
            answer.AssessmentEvidenceJson = BuildEvidenceJson(res);

            // Scoring method v6: recorded, never deducted for. The count is set even when the
            // list is empty, because for these runs "the assessor found none" is a real finding;
            // null is reserved for runs that predate the field and were never asked.
            answer.UnverifiedClaimCount = res.UnverifiedClaims.Count;
            answer.UnverifiedClaimsJson = res.UnverifiedClaims.Count > 0
                ? JsonSerializer.Serialize(res.UnverifiedClaims)
                : null;

            var flags = (BenchmarkAnswerFlags)answer.AnswerFlags;
            if (res.ContestedVerdict)
            {
                flags |= BenchmarkAnswerFlags.ContestedVerdict;
            }
            else
            {
                // Cleared on re-assessment: a fresh verdict that does not contradict itself must
                // not inherit the previous grader's contradiction.
                flags &= ~BenchmarkAnswerFlags.ContestedVerdict;
            }
            answer.AnswerFlags = (int)flags;

            if (res.CriticalErrorDemoted)
            {
                _logger.LogInformation(
                    "Benchmark run {RunId} answer {OrderIndex}: assessor claimed a critical error without a verifiable quote; not applied.",
                    run.Id, answer.OrderIndex);
            }

            if (res.UnverifiedClaimsDropped > 0)
            {
                _logger.LogInformation(
                    "Benchmark run {RunId} answer {OrderIndex}: {Dropped} unverified claim(s) discarded — not found verbatim in the graded answer.",
                    run.Id, answer.OrderIndex, res.UnverifiedClaimsDropped);
            }

            if (res.ContestedVerdict)
            {
                _logger.LogInformation(
                    "Benchmark run {RunId} answer {OrderIndex}: assessor prose describes a fabrication while criticalError is false; recorded as a contested verdict.",
                    run.Id, answer.OrderIndex);
            }

            answer.AccuracyScore = BenchmarkScoring.Score(res.AccuracyLevel, constants.LevelScores);
            answer.CompletenessScore = BenchmarkScoring.Score(res.CompletenessLevel, constants.LevelScores);
            answer.ConcisenessScore = BenchmarkScoring.Score(res.ConcisenessLevel, constants.LevelScores);
            answer.ReadabilityScore = BenchmarkScoring.Score(res.ReadabilityLevel, constants.LevelScores);

            var (qualityScore, rawQualityScore, _) = BenchmarkScoring.Quality(
                res.AccuracyLevel, res.CompletenessLevel, res.ConcisenessLevel, res.ReadabilityLevel,
                res.CriticalError, constants);

            answer.QualityScore = qualityScore;
            answer.RawQualityScore = rawQualityScore;
            answer.SpeedScore = BenchmarkScoring.Speed(
                answer.ModelTimeMs,
                answer.AssessedDifficulty ?? BenchmarkRunFinalizer.FallbackDifficulty(answer.Difficulty),
                constants);
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

        await MaybeRunSecondOpinionAsync(
            db, configService, run, answer, expectedPoints, constants, cancellationToken);
    }

    /// <summary>
    /// The assessor's own account of what each deduction rests on. Stored verbatim so a disputed
    /// score can be argued from the record: whether a deduction came from the authored rubric or
    /// from the grader's own knowledge is the thing that decides such an argument, and nothing
    /// recorded it before.
    /// </summary>
    private static string? BuildEvidenceJson(BenchmarkPerQuestionAssessmentResult res)
    {
        if (string.IsNullOrWhiteSpace(res.AccuracyEvidence) &&
            string.IsNullOrWhiteSpace(res.CompletenessEvidence) &&
            string.IsNullOrWhiteSpace(res.CriticalErrorQuote) &&
            !res.CriticalErrorDemoted)
        {
            return null;
        }

        return JsonSerializer.Serialize(new
        {
            accuracy = res.AccuracyEvidence,
            completeness = res.CompletenessEvidence,
            criticalErrorQuote = res.CriticalErrorQuote,
            criticalErrorDemoted = res.CriticalErrorDemoted
        });
    }

    /// <summary>
    /// Re-grades an answer once with a second assessor when the first verdict was severe: a
    /// critical error, or a quality score below <c>Benchmark:SecondOpinionQualityThreshold</c>
    /// (0 disables the pass). Both verdicts are kept and disagreement is flagged; the first stays
    /// authoritative for scoring, because replacing a score with whichever grader spoke last
    /// would buy agreement rather than accuracy.
    ///
    /// A failure here never fails the run: a missing second opinion leaves the first verdict
    /// exactly as it was.
    /// </summary>
    private async Task MaybeRunSecondOpinionAsync(
        ApplicationDbContext db,
        SystemAiConfigService configService,
        BenchmarkRun run,
        BenchmarkRunAnswer answer,
        string? expectedPoints,
        BenchmarkScoringConstants constants,
        CancellationToken cancellationToken)
    {
        if (answer.AssessmentStatus != BenchmarkAssessmentStatus.Scored || !answer.QualityScore.HasValue)
        {
            return;
        }

        // Selected per run in the start dialog, like every other model this harness uses. Absent
        // means the operator asked for no second opinion; it never falls back to the assessor
        // above, because a model checking its own verdict buys agreement, not a second reading.
        if (!run.SecondOpinionAssessorModelConfigurationId.HasValue)
        {
            return;
        }

        var mode = ResolveSecondOpinionMode(run, constants);
        string? trigger = ResolveSecondOpinionTrigger(answer, mode, constants);
        if (trigger == null)
        {
            return;
        }

        await RunSecondOpinionAsync(
            db, configService, run, answer, expectedPoints, constants, trigger, cancellationToken);
    }

    /// <summary>
    /// The mode this run actually uses. Read from the run's snapshot when it has one, falling
    /// back to the scoring profile: <see cref="BenchmarkRun.SecondOpinionModeUsed"/> is stamped at
    /// run start, and a value outside the enum means a row written before the mode existed.
    /// </summary>
    private static BenchmarkSecondOpinionMode ResolveSecondOpinionMode(
        BenchmarkRun run,
        BenchmarkScoringConstants constants)
    {
        return Enum.IsDefined(typeof(BenchmarkSecondOpinionMode), run.SecondOpinionModeUsed)
            ? (BenchmarkSecondOpinionMode)run.SecondOpinionModeUsed
            : constants.SecondOpinionMode;
    }

    /// <summary>
    /// Which rule, if any, selects this answer for a second verdict — the string is stored on the
    /// answer, so a report can say what produced each one. Null means no second opinion.
    ///
    /// Order matters: the first match wins, and the list runs from most to least specific so that
    /// a critical error is never attributed to a threshold it also happens to fall below.
    ///
    /// <see cref="BenchmarkSecondOpinionMode.All"/> short-circuits everything. Its whole purpose
    /// is that selection carries no information: an agreement rate over trigger-selected answers
    /// is conditioned on the first assessor's own uncertainty, which is why it cannot measure the
    /// instrument and All can.
    /// </summary>
    private static string? ResolveSecondOpinionTrigger(
        BenchmarkRunAnswer answer,
        BenchmarkSecondOpinionMode mode,
        BenchmarkScoringConstants constants)
    {
        if (mode == BenchmarkSecondOpinionMode.Off) return null;
        if (mode == BenchmarkSecondOpinionMode.All) return SecondOpinionTriggers.All;

        if (answer.CriticalError) return SecondOpinionTriggers.CriticalError;

        // The Q10 shape: the assessor wrote "hallucinates 'adamantium'" and left criticalError
        // false, so no existing trigger saw it and the synthesis reported the hallucination the
        // per-question verdict had declined to flag.
        if ((((BenchmarkAnswerFlags)answer.AnswerFlags) & BenchmarkAnswerFlags.ContestedVerdict) != 0)
        {
            return SecondOpinionTriggers.ContestedVerdict;
        }

        // The Q1 shape: claims the assessor could not adjudicate *and* an accuracy level it
        // nevertheless docked. Either alone is unremarkable — an assessor may legitimately report
        // an unverifiable claim and still award full accuracy — but together they suggest the
        // deduction rested on the thing scoring method v6 forbids deducting for.
        if ((answer.UnverifiedClaimCount ?? 0) > 0 && (answer.AccuracyLevel ?? 6) < 4)
        {
            return SecondOpinionTriggers.UnverifiedClaims;
        }

        int threshold = constants.SecondOpinionQualityThreshold;
        if (threshold > 0 && answer.QualityScore!.Value < threshold)
        {
            return SecondOpinionTriggers.BelowThreshold;
        }

        return null;
    }

    /// <summary>
    /// Re-grades answers that scored far below the run's own median, in
    /// <see cref="BenchmarkSecondOpinionMode.FlaggedAndOutliers"/> only.
    ///
    /// It exists because an absolute threshold cannot see an outlier in an otherwise strong run.
    /// On the 2026-09-03 run the median was 96 and the two worst answers scored 60 — the two the
    /// synthesis singled out as the model's failures — while the profile's absolute threshold of
    /// 50 selected neither, and the report's forgone-second-opinion line, computed from the same
    /// conditions, printed nothing at all.
    ///
    /// A failure here never fails the run: the answers keep the verdicts they already have.
    /// </summary>
    private async Task RunOutlierSweepAsync(
        ApplicationDbContext db,
        SystemAiConfigService configService,
        BenchmarkRun run,
        BenchmarkScoringConstants constants,
        CancellationToken cancellationToken)
    {
        if (!run.SecondOpinionAssessorModelConfigurationId.HasValue) return;
        if (ResolveSecondOpinionMode(run, constants) != BenchmarkSecondOpinionMode.FlaggedAndOutliers) return;

        int delta = constants.SecondOpinionOutlierDeltaPoints;
        if (delta <= 0) return;

        var scored = await db.BenchmarkRunAnswers
            .Where(a => a.BenchmarkRunId == run.Id &&
                        a.Status == BenchmarkAnswerStatus.Ok &&
                        a.QualityScore.HasValue)
            .ToListAsync(cancellationToken);

        if (scored.Count < MinimumAnswersForOutlierSweep) return;

        double median = Median(scored.Select(a => (double)a.QualityScore!.Value));

        var candidates = scored
            .Where(a => a.SecondOpinionQualityScore == null &&
                        median - a.QualityScore!.Value > delta)
            .OrderBy(a => a.QualityScore!.Value)
            .ThenBy(a => a.OrderIndex)
            .Take(MaxOutlierSweepAnswers)
            .ToList();

        if (candidates.Count == 0) return;

        _logger.LogInformation(
            "Benchmark run {RunId}: outlier sweep re-grading {Count} answer(s) more than {Delta} points below the median of {Median}.",
            run.Id, candidates.Count, delta, median);

        var suiteQuestions = await db.BenchmarkQuestions
            .Where(q => q.BenchmarkSuiteId == run.BenchmarkSuiteId)
            .ToDictionaryAsync(q => q.OrderIndex, q => q.ExpectedPoints, cancellationToken);

        foreach (var answer in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            suiteQuestions.TryGetValue(answer.OrderIndex, out var expectedPoints);

            try
            {
                await RunSecondOpinionAsync(
                    db, configService, run, answer, expectedPoints, constants,
                    SecondOpinionTriggers.Outlier, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Benchmark run {RunId} answer {OrderIndex}: outlier sweep second opinion failed. The first verdict stands.",
                    run.Id, answer.OrderIndex);
            }
        }
    }

    /// <summary>
    /// Below this, "the run's median" is not a meaningful reference point and the sweep would be
    /// re-grading against noise.
    /// </summary>
    private const int MinimumAnswersForOutlierSweep = 5;

    /// <summary>
    /// Highest deviation first, then stop. Without a cap a uniformly poor run re-grades nearly
    /// every answer — doubling assessor cost for no added information, since a run where
    /// everything is below the median has no outliers, only a low median.
    /// </summary>
    private const int MaxOutlierSweepAnswers = 4;

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        if (sorted.Count == 0) return 0.0;

        int mid = sorted.Count / 2;
        return sorted.Count % 2 == 1
            ? sorted[mid]
            : (sorted[mid - 1] + sorted[mid]) / 2.0;
    }

    /// <summary>
    /// One field out of the stored evidence JSON. Returns null on anything malformed: evidence is
    /// advisory context for the synthesis prompt, and a bad row must not fail the synthesis.
    /// </summary>
    private static string? ReadEvidence(string? evidenceJson, string field)
    {
        if (string.IsNullOrWhiteSpace(evidenceJson)) return null;

        try
        {
            using var doc = JsonDocument.Parse(evidenceJson);
            return doc.RootElement.TryGetProperty(field, out var prop) && prop.ValueKind == JsonValueKind.String
                ? prop.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Re-grades every answer of a completed run with an alternative assessor and records how its
    /// verdicts compare with the ones that actually scored — writing nothing to any answer.
    ///
    /// This is how an assessor change is decided from measurement rather than assumption. It makes
    /// <b>no candidate calls at all</b>: one assessor pass over text already stored, which makes it
    /// the cheapest AI operation here and produces a like-for-like cost figure against the recorded
    /// cost of the assessor it would replace.
    ///
    /// Results go to <see cref="BenchmarkAssessorCalibration"/> rather than to the second-opinion
    /// columns, so a calibration can neither collide with a real second opinion nor move a
    /// published index. For the same reason it never appears in the Markdown report: it is an
    /// experiment about graders, not a property of the run.
    /// </summary>
    public async Task RunAssessorCalibrationAsync(
        long runId,
        long assessorConfigId,
        string? createdByUserName,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var configService = scope.ServiceProvider.GetRequiredService<SystemAiConfigService>();

        var run = await db.BenchmarkRuns
            .Include(r => r.BenchmarkSuite)
            .ThenInclude(s => s!.Questions)
            .FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);

        if (run == null)
        {
            _logger.LogWarning("Benchmark run {RunId} not found for assessor calibration.", runId);
            return;
        }

        var calibration = new BenchmarkAssessorCalibration
        {
            BenchmarkRunId = run.Id,
            AssessorModelConfigurationId = assessorConfigId,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserName = createdByUserName
        };

        var (assessorConfig, assessorApiKey, resolveError) = await ResolveAssessorAsync(
            db, run, assessorConfigId, cancellationToken);

        if (assessorConfig == null || assessorApiKey == null)
        {
            calibration.ErrorMessage = BenchmarkAssessmentFailure.Truncate(resolveError);
            db.BenchmarkAssessorCalibrations.Add(calibration);
            await db.SaveChangesAsync(CancellationToken.None);
            return;
        }

        calibration.AssessorDisplayNameUsed = assessorConfig.DisplayName ?? assessorConfig.ModelId;
        calibration.AssessorProviderUsed = assessorConfig.Provider;
        calibration.AssessorModelIdUsed = assessorConfig.ModelId;
        calibration.AssessorThinkingLevelUsed = assessorConfig.ThinkingLevel;
        calibration.AssessorReasoningModeUsed = assessorConfig.ReasoningMode;
        calibration.AssessorServiceTierUsed = assessorConfig.ServiceTier;
        calibration.AssessorMaxOutputTokensUsed = assessorConfig.MaxOutputTokens;

        var answers = await db.BenchmarkRunAnswers
            .Where(a => a.BenchmarkRunId == run.Id)
            .OrderBy(a => a.OrderIndex)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var suiteQuestions = run.BenchmarkSuite?.Questions ?? new List<BenchmarkQuestion>();

        var profile = run.ScoringProfileId.HasValue
            ? await _scoringProfileService.GetProfileByIdAsync(run.ScoringProfileId.Value) ?? await _scoringProfileService.GetDefaultProfileAsync()
            : await _scoringProfileService.GetDefaultProfileAsync();
        var constants = _scoringProfileService.ToConstants(profile);

        var verdicts = new List<object>();
        var deltas = new List<int>();
        int disagreements = 0;
        var sw = Stopwatch.StartNew();

        foreach (var answer in answers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // A calibration compares graders, and there is nothing to compare on an answer the
            // original assessor never scored either.
            if (answer.Status != BenchmarkAnswerStatus.Ok || !answer.QualityScore.HasValue)
            {
                calibration.SkippedAnswerCount++;
                continue;
            }

            // Prefers the stored question key; the order index is the fallback for a historical
            // answer that has none, which is the case a suite reorder gets wrong.
            string? expectedPoints = answer.BenchmarkQuestionId.HasValue
                ? suiteQuestions.FirstOrDefault(q => q.Id == answer.BenchmarkQuestionId.Value)?.ExpectedPoints
                : suiteQuestions.FirstOrDefault(q => q.OrderIndex == answer.OrderIndex)?.ExpectedPoints;

            AssessorVerdict verdict;
            try
            {
                verdict = await GradeAnswerWithAssessorAsync(
                    run, answer, expectedPoints, assessorConfig, assessorApiKey, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Calibration of run {RunId} answer {OrderIndex} failed.", run.Id, answer.OrderIndex);
                calibration.SkippedAnswerCount++;
                continue;
            }

            calibration.InputTokens += verdict.InputTokens;
            calibration.OutputTokens += verdict.OutputTokens;

            if (verdict.Result == null)
            {
                calibration.SkippedAnswerCount++;
                continue;
            }

            var res = verdict.Result;
            var (quality, _, _) = BenchmarkScoring.Quality(
                res.AccuracyLevel, res.CompletenessLevel, res.ConcisenessLevel, res.ReadabilityLevel,
                res.CriticalError, constants);

            int delta = quality - answer.QualityScore.Value;
            // Same definition as a live run's, so a calibration and an All-mode run are read the
            // same way: a gap above one BARS level on the dominant dimension, or a split on
            // criticalError.
            bool disagreed = Math.Abs(delta) > SecondOpinionDisagreementPoints ||
                             res.CriticalError != answer.CriticalError;
            if (disagreed) disagreements++;

            deltas.Add(Math.Abs(delta));
            calibration.AnswerCount++;

            verdicts.Add(new
            {
                orderIndex = answer.OrderIndex,
                originalQualityScore = answer.QualityScore.Value,
                calibrationQualityScore = quality,
                delta,
                disagreed,
                originalCriticalError = answer.CriticalError,
                calibrationCriticalError = res.CriticalError,
                accuracyLevel = res.AccuracyLevel,
                completenessLevel = res.CompletenessLevel,
                concisenessLevel = res.ConcisenessLevel,
                readabilityLevel = res.ReadabilityLevel,
                comment = res.Comment,
                accuracyEvidence = res.AccuracyEvidence,
                completenessEvidence = res.CompletenessEvidence,
                unverifiedClaims = res.UnverifiedClaims
            });
        }

        sw.Stop();
        calibration.DurationMs = sw.ElapsedMilliseconds;
        calibration.DisagreementCount = disagreements;
        calibration.MeanAbsDelta = deltas.Count > 0 ? deltas.Average() : null;
        calibration.VerdictsJson = JsonSerializer.Serialize(verdicts);

        db.BenchmarkAssessorCalibrations.Add(calibration);
        await db.SaveChangesAsync(CancellationToken.None);

        try
        {
            await configService.RecordUsageAsync(
                assessorConfig.Id, run.StartedByUserId, calibration.InputTokens, calibration.OutputTokens,
                roleContext: 4, totalDurationMs: (int)calibration.DurationMs);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record usage for an assessor calibration run.");
        }

        _logger.LogInformation(
            "Calibration of run {RunId} with {Model}: {Count} answer(s), mean absolute delta {Delta}, {Disagreements} disagreement(s).",
            run.Id, calibration.AssessorDisplayNameUsed, calibration.AnswerCount,
            calibration.MeanAbsDelta, calibration.DisagreementCount);
    }

    /// <summary>One assessor pass over one stored answer: the verdict, and what it cost.</summary>
    private sealed record AssessorVerdict(
        BenchmarkPerQuestionAssessmentResult? Result,
        int InputTokens,
        int OutputTokens,
        long DurationMs,
        string? Error);

    /// <summary>
    /// Runs the per-question assessor prompt against a stored answer and parses the verdict,
    /// writing nothing. The read-only core shared by trial re-assessment and calibration runs,
    /// both of which must be able to grade an answer without touching it — which is exactly what
    /// <see cref="ExecutePerQuestionAssessmentAsync"/> cannot offer, because applying the verdict
    /// is its whole purpose.
    /// </summary>
    private async Task<AssessorVerdict> GradeAnswerWithAssessorAsync(
        BenchmarkRun run,
        BenchmarkRunAnswer answer,
        string? expectedPoints,
        SystemAiApiConfiguration assessorConfig,
        string assessorApiKey,
        CancellationToken cancellationToken)
    {
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
            answer.ToolBudgetExhausted,
            answer.ScrubbedArtifactCount,
            answer.ToolCallBudgetUsed,
            boardName: run.BenchmarkSuite?.GameSnapshot?.Name,
            boardText: run.BenchmarkSuite?.GameSnapshot?.SanitizedText);

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
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { terminalError = ex.Message; }

        sw.Stop();

        int inputTokens = runResult.TotalPromptTokens > 0 ? runResult.TotalPromptTokens : runResult.EstimatedInputTokens;
        int outputTokens = runResult.OutputTokens > 0 ? runResult.OutputTokens : runResult.EstimatedOutputTokens;

        var parseResult = string.IsNullOrWhiteSpace(terminalError)
            ? BenchmarkAssessmentParser.ParsePerQuestion(runResult.FinalText, answer.AnswerText)
            : new PerQuestionAssessmentParseResult { Success = false, ErrorMessage = terminalError };

        return new AssessorVerdict(
            parseResult.Success ? parseResult.Result : null,
            inputTokens,
            outputTokens,
            sw.ElapsedMilliseconds,
            terminalError ?? parseResult.ErrorMessage);
    }

    /// <summary>
    /// One model call that reports which GnollHack subsystems a suite does not test.
    ///
    /// Deliberately returns its result rather than persisting anything. The whole guardrail on
    /// this feature is that a coverage analysis is a **read-only report**: nothing is written into
    /// the suite, there is no endpoint that would write one, and a generated draft has to be
    /// edited and approved by a human before it becomes a question. Persisting the report would
    /// be the first step toward treating it as an answer key.
    ///
    /// The prompt receives question texts only. The suite's own answers and scores are withheld
    /// so the analysis cannot be shaped by which questions any model happened to do badly on.
    /// </summary>
    public async Task<(BenchmarkCoverageAnalysisResult? Result, string? Error, int InputTokens, int OutputTokens, long DurationMs)>
        RunCoverageAnalysisAsync(
            long suiteId,
            long analysisConfigId,
            CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var suite = await db.BenchmarkSuites
            .Include(s => s.Questions)
            .FirstOrDefaultAsync(s => s.Id == suiteId, cancellationToken);

        if (suite == null)
        {
            return (null, "Benchmark suite not found.", 0, 0, 0);
        }

        var config = await db.SystemAiApiConfigurations.FindAsync(new object?[] { analysisConfigId }, cancellationToken);
        if (config == null || !config.IsEnabled || string.IsNullOrWhiteSpace(config.EncryptedApiKey) || (config.ModelRole & 4) != 4)
        {
            return (null, "The selected analysis model is invalid, disabled, missing an API key, or not configured with the Benchmark role.", 0, 0, 0);
        }

        string apiKey = _cryptoService.Decrypt(config.EncryptedApiKey, config.ApiKeyNonce!, config.ApiKeyTag!, "SYSTEM_API_KEY");

        // Question texts only: no rubrics, no answers, no scores, no item statistics.
        var questionTexts = suite.Questions
            .OrderBy(q => q.OrderIndex)
            .Select(q => q.QuestionText)
            .ToList();

        // Resolved from the scope rather than injected: the coverage inventory is the only place
        // this service touches either index, and a constructor dependency for it would be paid by
        // every run.
        var sourceCode = scope.ServiceProvider.GetRequiredService<SourceCodeService>();
        var wiki = scope.ServiceProvider.GetRequiredService<NetHackWikiService>();

        var sourceInventory = BuildCoverageSourceInventory(sourceCode);
        var wikiInventory = BuildCoverageWikiInventory(wiki);

        string prompt = BenchmarkCoveragePrompt.BuildPrompt(
            suite.Name, questionTexts, sourceInventory, wikiInventory);

        int maxTokens = _configuration.GetValue<int>("Benchmark:AssessorMaxOutputTokens", 32000);

        var runRequest = new AgentRunRequest
        {
            ProviderName = config.Provider,
            ModelId = config.ModelId,
            ApiKey = apiKey,
            ModelDisplayName = config.DisplayName,
            SystemPrompt = "You are an objective GnollHack domain analyst. Strictly adhere to the requested JSON response format.",
            ThinkingLevel = config.ThinkingLevel,
            ReasoningMode = config.ReasoningMode,
            ReasoningSummary = config.ReasoningSummary,
            ServiceTier = config.ServiceTier,
            MaxOutputTokens = config.MaxOutputTokens ?? maxTokens,
            MaxToolIterations = 0,
            EnableToolUse = false,
            EnableWebSearch = false,
            EnableSubAgents = false,
            SystemModelId = config.Id,
            PromptCacheKey = $"benchmark:coverage:{config.ModelId}",
            Budget = new AgentRunBudget { MaxTotalModelCalls = 2 },
            ToolExecutionContext = new Tools.ToolExecutionContext
            {
                SessionId = suite.Id,
                UserId = string.Empty,
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
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { terminalError = ex.Message; }
        sw.Stop();

        int inputTokens = runResult.TotalPromptTokens > 0 ? runResult.TotalPromptTokens : runResult.EstimatedInputTokens;
        int outputTokens = runResult.OutputTokens > 0 ? runResult.OutputTokens : runResult.EstimatedOutputTokens;

        if (!string.IsNullOrWhiteSpace(terminalError))
        {
            return (null, terminalError, inputTokens, outputTokens, sw.ElapsedMilliseconds);
        }

        var parsed = BenchmarkCoveragePrompt.Parse(runResult.FinalText);
        return parsed.Success
            ? (parsed.Result, null, inputTokens, outputTokens, sw.ElapsedMilliseconds)
            : (null, parsed.ErrorMessage, inputTokens, outputTokens, sw.ElapsedMilliseconds);
    }

    /// <summary>
    /// A bounded inventory of source files, so a gap can cite a location that exists. Bounded
    /// because the index holds thousands of files and the point is orientation, not a listing.
    /// </summary>
    private List<string> BuildCoverageSourceInventory(SourceCodeService sourceCode)
    {
        const int maxEntries = 200;
        try
        {
            string listing = sourceCode.ListFiles("src/", includeNetCode: false);
            return listing
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(line => !line.StartsWith("Total:", StringComparison.Ordinal))
                .Take(maxEntries)
                .ToList();
        }
        catch (Exception ex)
        {
            // An inventory the index cannot supply costs the prompt some orientation, never the
            // analysis: the model is still asked for a location, and one it invents is discarded
            // by the parser only if it is missing, so a human checks what remains.
            _logger.LogWarning(ex, "Could not build the coverage source inventory.");
            return new List<string>();
        }
    }

    private List<string> BuildCoverageWikiInventory(NetHackWikiService wiki)
    {
        const int maxEntries = 120;
        try
        {
            return wiki.GetRelevantContext("GnollHack mechanics overview", maxResults: maxEntries)
                .Select(c => c.Length > 160 ? c.Substring(0, 160) : c)
                .Take(maxEntries)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not build the coverage wiki inventory.");
            return new List<string>();
        }
    }

    /// <summary>
    /// Grades one answer with an operator-chosen assessor and records the verdict beside the
    /// authoritative one, changing no score.
    ///
    /// It reuses the second-opinion columns because they already mean exactly this — a
    /// non-authoritative parallel verdict from a different model, with disagreement flagged and
    /// the first verdict still scoring — and adding a second set of columns for the same concept
    /// would leave two places to read a verdict from.
    ///
    /// Refuses rather than overwrites when a second opinion is already present: an automatic
    /// second opinion is run evidence, a manual trial is an experiment, and an experiment must
    /// not erase evidence. The caller surfaces the refusal so a deliberate replacement is an
    /// explicit act rather than a silent one.
    /// </summary>
    private async Task RunTrialAssessmentAsync(
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
        if (!answer.QualityScore.HasValue)
        {
            _logger.LogWarning(
                "Benchmark run {RunId} answer {OrderIndex}: trial re-assessment skipped — the answer has no verdict to compare against.",
                run.Id, answer.OrderIndex);
            return;
        }

        var verdict = await GradeAnswerWithAssessorAsync(
            run, answer, expectedPoints, assessorConfig, assessorApiKey, cancellationToken);

        // Assessor-side cost either way, so it is recorded even when the verdict is unusable.
        answer.AssessmentInputTokens = (answer.AssessmentInputTokens ?? 0) + verdict.InputTokens;
        answer.AssessmentOutputTokens = (answer.AssessmentOutputTokens ?? 0) + verdict.OutputTokens;
        answer.AssessmentDurationMs = (answer.AssessmentDurationMs ?? 0) + verdict.DurationMs;

        try
        {
            await configService.RecordUsageAsync(
                assessorConfig.Id, run.StartedByUserId, verdict.InputTokens, verdict.OutputTokens,
                roleContext: 4, totalDurationMs: (int)verdict.DurationMs);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record usage for a trial re-assessment.");
        }

        if (verdict.Result == null)
        {
            _logger.LogWarning(
                "Benchmark run {RunId} answer {OrderIndex}: trial re-assessment produced no usable verdict ({Error}).",
                run.Id, answer.OrderIndex, verdict.Error);
            return;
        }

        var res = verdict.Result;
        var (trialQuality, _, _) = BenchmarkScoring.Quality(
            res.AccuracyLevel, res.CompletenessLevel, res.ConcisenessLevel, res.ReadabilityLevel,
            res.CriticalError, constants);

        answer.SecondOpinionQualityScore = trialQuality;
        answer.SecondOpinionCriticalError = res.CriticalError;
        answer.SecondOpinionByModelDisplayNameUsed = assessorConfig.DisplayName ?? assessorConfig.ModelId;
        answer.SecondOpinionTrigger = SecondOpinionTriggers.Manual;
        answer.SecondOpinionJson = JsonSerializer.Serialize(new
        {
            assessor = assessorConfig.DisplayName ?? assessorConfig.ModelId,
            provider = assessorConfig.Provider,
            modelId = assessorConfig.ModelId,
            assessedAtUtc = DateTime.UtcNow,
            trial = true,
            accuracyLevel = res.AccuracyLevel,
            completenessLevel = res.CompletenessLevel,
            concisenessLevel = res.ConcisenessLevel,
            readabilityLevel = res.ReadabilityLevel,
            criticalError = res.CriticalError,
            criticalErrorQuote = res.CriticalErrorQuote,
            qualityScore = trialQuality,
            comment = res.Comment,
            accuracyEvidence = res.AccuracyEvidence,
            completenessEvidence = res.CompletenessEvidence,
            unverifiedClaims = res.UnverifiedClaims
        });

        answer.SecondOpinionDisagreed =
            Math.Abs(trialQuality - answer.QualityScore.Value) > SecondOpinionDisagreementPoints ||
            res.CriticalError != answer.CriticalError;

        await db.SaveChangesAsync(CancellationToken.None);

        _logger.LogInformation(
            "Benchmark run {RunId} answer {OrderIndex}: trial verdict {Trial} from {Model} against the scored {Scored}. Score unchanged.",
            run.Id, answer.OrderIndex, trialQuality,
            assessorConfig.DisplayName ?? assessorConfig.ModelId, answer.QualityScore.Value);
    }

    /// <summary>
    /// The suite question an answer belongs to. Prefers the stored foreign key and falls back to
    /// the order index only where there is none — a historical answer the backfill could not
    /// match unambiguously. The fallback is wrong after a reorder, which is exactly why the key
    /// exists; keeping it is still better than returning nothing for every pre-key answer.
    /// </summary>
    private static BenchmarkQuestion? MatchSuiteQuestion(BenchmarkRun run, BenchmarkRunAnswer answer)
    {
        var questions = run.BenchmarkSuite?.Questions;
        if (questions == null) return null;

        if (answer.BenchmarkQuestionId.HasValue)
        {
            return questions.FirstOrDefault(q => q.Id == answer.BenchmarkQuestionId.Value);
        }

        return questions.FirstOrDefault(q => q.OrderIndex == answer.OrderIndex);
    }

    /// <summary>Trigger names, stored on the answer and printed in the report.</summary>
    internal static class SecondOpinionTriggers
    {
        public const string CriticalError = "CriticalError";
        public const string ContestedVerdict = "ContestedVerdict";
        public const string UnverifiedClaims = "UnverifiedClaims";
        public const string BelowThreshold = "BelowThreshold";
        public const string Outlier = "Outlier";
        public const string All = "All";
        public const string Manual = "Manual";
    }

    /// <summary>
    /// Grades one answer with the run's second-opinion assessor and records the verdict. Split
    /// from the trigger logic so the post-scoring outlier sweep can reuse it without
    /// re-evaluating triggers it has already decided.
    /// </summary>
    private async Task RunSecondOpinionAsync(
        ApplicationDbContext db,
        SystemAiConfigService configService,
        BenchmarkRun run,
        BenchmarkRunAnswer answer,
        string? expectedPoints,
        BenchmarkScoringConstants constants,
        string trigger,
        CancellationToken cancellationToken)
    {
        // Re-checked rather than assumed: this is reached from the per-answer trigger path and
        // from the outlier sweep, and only the first of those has already established both.
        if (!run.SecondOpinionAssessorModelConfigurationId.HasValue || !answer.QualityScore.HasValue)
        {
            return;
        }

        int firstQualityScore = answer.QualityScore.Value;

        var (secondConfig, secondApiKey, resolveError) = await ResolveAssessorAsync(
            db, run, run.SecondOpinionAssessorModelConfigurationId.Value, cancellationToken);

        if (secondConfig == null || secondApiKey == null)
        {
            // Disabled or key-less since the run started. Skip: the first verdict stands, and
            // grading with the model that produced it would not be a second opinion.
            _logger.LogWarning(
                "Benchmark run {RunId} answer {OrderIndex}: second-opinion assessor {ConfigId} unusable ({Error}). The first verdict stands.",
                run.Id, answer.OrderIndex, run.SecondOpinionAssessorModelConfigurationId.Value, resolveError);
            return;
        }

        var allowedTools = _configuration.GetSection("Benchmark:AllowedTools").Get<List<string>>() ?? _defaultAllowedTools;
        string prompt = BenchmarkAssessmentPrompt.BuildSecondOpinionPrompt(
            run.SuiteName,
            answer.OrderIndex,
            answer.QuestionText,
            answer.Difficulty,
            expectedPoints,
            answer.AnswerText,
            answer.Status,
            firstQualityScore,
            answer.CriticalError,
            answer.ReviewComment,
            allowedTools,
            answer.ToolCallCount ?? 0,
            answer.ToolBudgetExhausted,
            answer.ScrubbedArtifactCount,
            answer.ToolCallBudgetUsed,
            boardName: run.BenchmarkSuite?.GameSnapshot?.Name,
            boardText: run.BenchmarkSuite?.GameSnapshot?.SanitizedText);

        int assessorMaxTokens = _configuration.GetValue<int>("Benchmark:AssessorMaxOutputTokens", 32000);

        var runRequest = new AgentRunRequest
        {
            ProviderName = secondConfig.Provider,
            ModelId = secondConfig.ModelId,
            ApiKey = secondApiKey,
            ModelDisplayName = secondConfig.DisplayName,
            SystemPrompt = "You are an objective AI benchmark evaluator. Strictly adhere to the requested JSON response format.",
            ThinkingLevel = secondConfig.ThinkingLevel,
            ReasoningMode = secondConfig.ReasoningMode,
            ReasoningSummary = secondConfig.ReasoningSummary,
            ServiceTier = secondConfig.ServiceTier,
            MaxOutputTokens = secondConfig.MaxOutputTokens ?? assessorMaxTokens,
            MaxToolIterations = 0,
            EnableToolUse = false,
            EnableWebSearch = false,
            EnableSubAgents = false,
            SystemModelId = secondConfig.Id,
            PromptCacheKey = $"benchmark:second_opinion:{secondConfig.ModelId}",
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
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { terminalError = ex.Message; }
        sw.Stop();

        // Assessor-side cost either way, so it is recorded even when the verdict is unusable.
        answer.AssessmentInputTokens = (answer.AssessmentInputTokens ?? 0) +
            (runResult.TotalPromptTokens > 0 ? runResult.TotalPromptTokens : runResult.EstimatedInputTokens);
        answer.AssessmentOutputTokens = (answer.AssessmentOutputTokens ?? 0) +
            (runResult.OutputTokens > 0 ? runResult.OutputTokens : runResult.EstimatedOutputTokens);
        answer.AssessmentDurationMs = (answer.AssessmentDurationMs ?? 0) + sw.ElapsedMilliseconds;

        var parseResult = string.IsNullOrWhiteSpace(terminalError)
            ? BenchmarkAssessmentParser.ParsePerQuestion(runResult.FinalText, answer.AnswerText)
            : new PerQuestionAssessmentParseResult { Success = false, ErrorMessage = terminalError };

        if (!parseResult.Success || parseResult.Result == null)
        {
            _logger.LogWarning(
                "Benchmark run {RunId} answer {OrderIndex}: second opinion unavailable ({Error}). The first verdict stands.",
                run.Id, answer.OrderIndex, parseResult.ErrorMessage ?? terminalError);
            await db.SaveChangesAsync(CancellationToken.None);
            return;
        }

        var second = parseResult.Result;
        var (secondQuality, _, _) = BenchmarkScoring.Quality(
            second.AccuracyLevel, second.CompletenessLevel, second.ConcisenessLevel, second.ReadabilityLevel,
            second.CriticalError, constants);

        answer.SecondOpinionQualityScore = secondQuality;
        answer.SecondOpinionCriticalError = second.CriticalError;
        answer.SecondOpinionByModelDisplayNameUsed = secondConfig.DisplayName ?? secondConfig.ModelId;
        answer.SecondOpinionTrigger = trigger;
        answer.SecondOpinionJson = JsonSerializer.Serialize(new
        {
            assessor = secondConfig.DisplayName ?? secondConfig.ModelId,
            provider = secondConfig.Provider,
            modelId = secondConfig.ModelId,
            assessedAtUtc = DateTime.UtcNow,
            accuracyLevel = second.AccuracyLevel,
            completenessLevel = second.CompletenessLevel,
            concisenessLevel = second.ConcisenessLevel,
            readabilityLevel = second.ReadabilityLevel,
            criticalError = second.CriticalError,
            criticalErrorQuote = second.CriticalErrorQuote,
            qualityScore = secondQuality,
            comment = second.Comment,
            accuracyEvidence = second.AccuracyEvidence,
            completenessEvidence = second.CompletenessEvidence
        });

        // 15 points is roughly one BARS level on the dominant dimension: below that the two
        // graders are saying the same thing in different words.
        answer.SecondOpinionDisagreed =
            Math.Abs(secondQuality - firstQualityScore) > SecondOpinionDisagreementPoints ||
            second.CriticalError != answer.CriticalError;

        await db.SaveChangesAsync(CancellationToken.None);

        if (answer.SecondOpinionDisagreed)
        {
            _logger.LogInformation(
                "Benchmark run {RunId} answer {OrderIndex}: assessors disagree ({First} vs {Second}, critical {FirstCritical} vs {SecondCritical}).",
                run.Id, answer.OrderIndex, answer.QualityScore.Value, secondQuality, answer.CriticalError, second.CriticalError);
        }

        try
        {
            await configService.RecordUsageAsync(
                secondConfig.Id,
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
            _logger.LogWarning(ex, "Failed to record usage for second-opinion assessor call.");
        }
    }

    /// <summary>
    /// Quality-score gap above which two verdicts are treated as disagreeing. Internal rather
    /// than private because the report prints the definition beside the rate it computes, and a
    /// second copy of the number would eventually disagree with this one.
    /// </summary>
    internal const int SecondOpinionDisagreementPoints = 15;

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
            AccuracyEvidence = ReadEvidence(a.AssessmentEvidenceJson, "accuracy"),
            CompletenessEvidence = ReadEvidence(a.AssessmentEvidenceJson, "completeness"),
            UnverifiedClaimCount = a.UnverifiedClaimCount ?? 0,
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
            .Include(s => s.GameSnapshot)
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
                string prompt = BenchmarkDifficultyPrompt.BuildPrompt(suite.Name, currentBatch, suite.GameSnapshot?.Name, suite.GameSnapshot?.DigestText);
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

                    string repairPrompt = BenchmarkDifficultyPrompt.BuildRepairPrompt(suite.Name, currentBatch, rawExcerpt, suite.GameSnapshot?.Name, suite.GameSnapshot?.DigestText);
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
            a.SpeedScore = BenchmarkScoring.Speed(
                a.ModelTimeMs,
                a.AssessedDifficulty ?? BenchmarkRunFinalizer.FallbackDifficulty(a.Difficulty),
                constants);
            a.Score = quality;
        }

        var scorableItems = run.Answers
            .Where(a => a.Status == BenchmarkAnswerStatus.Ok && a.QualityScore.HasValue)
            .Select(a => (a.QualityScore, a.AssessedDifficulty ?? BenchmarkRunFinalizer.FallbackDifficulty(a.Difficulty)))
            .ToList();

        run.QualityIndex = BenchmarkScoring.QualityIndex(scorableItems);
        run.SpeedIndex = BenchmarkScoring.SpeedIndex(run.Answers
            .Where(a => a.Status == BenchmarkAnswerStatus.Ok && a.SpeedScore.HasValue)
            .Select(a => a.SpeedScore));

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
            .Include(a => a.BenchmarkRun).ThenInclude(r => r.BenchmarkSuite).ThenInclude(s => s!.GameSnapshot)
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
            int maxResultLength = _configuration.GetValue<int>("Benchmark:MaxResultLength", 10000);
            int maxCallsPerSession = _configuration.GetValue<int>("Benchmark:MaxCallsPerSession", 50);
            int maxToolCallsPerQuestion = ResolveToolCallBudget(BenchmarkDifficulty.Advanced, null);

            bool suiteHasBoard = run.BenchmarkSuite?.GameSnapshot != null;
            string systemPrompt = _chatService.BuildSystemPrompt(
                wikiContext: Array.Empty<string>(),
                spoilerFreeMode: false,
                verboseMode: false,
                isGameOn: false,
                developerMode: false,
                overseerMode: 0,
                hasGameSnapshot: suiteHasBoard,
                hasMessageHistory: false,
                clientSettings: null,
                enableToolUse: true,
                enableWebSearch: false,
                allowSourceCodeReferences: true,
                enableSubAgents: false,
                parallelMode: testedConfig.ParallelExecutionMode);

            string? expectedPoints = MatchSuiteQuestion(run, answer)?.ExpectedPoints;

            await ReExecuteSingleAnswerAsync(
                db, configService, run, answer, testedConfig, testedApiKey,
                systemPrompt, allowedTools,
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

    /// <summary>
    /// Re-grades one stored answer.
    ///
    /// <paramref name="trial"/> decides whether this <b>replaces the verdict</b> or merely records
    /// a second one. Applied (the default) is what the action has always done and is what settling
    /// a disputed verdict needs: the score moves, the run's indices are recomputed, and the
    /// provenance columns record what was overwritten and by which model — which nothing did
    /// before, so a published Intelligence Index could change after publication while the run's
    /// assessor snapshot still named the model that graded everything.
    ///
    /// Trial writes into the second-opinion columns with trigger <c>Manual</c> and touches no
    /// score, level, flag, or index. It exists so a prospective assessor can be compared against
    /// the one in use without altering the result being compared.
    /// </summary>
    public async Task ReassessSingleQuestionAsync(
        long answerId,
        long? assessorConfigId = null,
        bool trial = false,
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

            // Flipped to Running so the admin UI shows the work in progress. For a trial these
            // two are restored below: a trial must leave the stored run byte-identical, and
            // CompletedAtUtc is part of the record, not scratch state.
            var originalStatus = run.Status;
            var originalCompletedAtUtc = run.CompletedAtUtc;

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
                var suiteQ = MatchSuiteQuestion(run, answer);
                expectedPoints = suiteQ?.ExpectedPoints;
            }

            if (trial)
            {
                await RunTrialAssessmentAsync(
                    db, configService, run, answer, expectedPoints,
                    assessorConfig, assessorApiKey, constants, cancellationToken);

                // Deliberately no BenchmarkRunFinalizer.Apply. It recomputes the indices and
                // stamps CompletedAtUtc, and a trial that moved either would be exactly the
                // destructive act it exists to avoid. Nothing it would recompute has changed:
                // the verdict is untouched and Manual second opinions are excluded from the
                // agreement aggregates.
                run.Status = originalStatus;
                run.CompletedAtUtc = originalCompletedAtUtc;
                await db.SaveChangesAsync(CancellationToken.None);
                return;
            }
            else
            {
                // Captured before the verdict is overwritten. PreviousQualityScore holds the
                // *original* score, so a second re-assessment increments the count and leaves
                // this alone rather than recording whichever verdict it happened to displace.
                if (answer.ReassessmentCount == 0)
                {
                    answer.PreviousQualityScore = answer.QualityScore;
                }

                await ExecutePerQuestionAssessmentAsync(
                    db, configService, run, answer, expectedPoints,
                    assessorConfig, assessorApiKey, constants, cancellationToken);

                answer.ReassessmentCount++;
                answer.ReassessedAtUtc = DateTime.UtcNow;
                answer.ReassessedByModelDisplayNameUsed =
                    assessorConfig.DisplayName ?? assessorConfig.ModelId;
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
