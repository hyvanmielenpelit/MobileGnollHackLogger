namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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

    public const string AbortedRunRefusal =
        "This run stopped before finishing its suite, so it has no Intelligence Index or Speed Index. " +
        "Re-scoring or re-running it would publish indices computed over only the questions that completed.";

    /// <summary>
    /// A provider finish reason cut to the width of
    /// <see cref="BenchmarkRunAnswer.ProviderFinishReason"/>. These are short enumerated tokens on
    /// every provider the harness talks to; the cut is here so an unexpected one is stored rather
    /// than throwing at save time.
    /// </summary>
    private static string? TruncateFinishReason(string? finishReason)
    {
        if (string.IsNullOrWhiteSpace(finishReason)) return null;

        string trimmed = finishReason.Trim();
        return trimmed.Length <= 64 ? trimmed : trimmed[..64];
    }

    /// <summary>
    /// Records what a run that stopped early consumed: the totals over the answers that completed,
    /// and — when a stopwatch measured it — the wall clock up to the stop. The status and the indices
    /// belong to the caller: such a run has a cost and a duration, and no score.
    /// </summary>
    private static async Task ApplyAbortedTotalsAsync(
        ApplicationDbContext db, BenchmarkRun run, long? elapsedMs)
    {
        var answers = await db.BenchmarkRunAnswers
            .Where(a => a.BenchmarkRunId == run.Id)
            .ToListAsync(CancellationToken.None);

        BenchmarkRunFinalizer.ApplyTotals(run, answers);

        if (elapsedMs.HasValue)
        {
            run.TotalDurationMs = elapsedMs.Value;
        }
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
                // An interrupted run never reached the end of its suite, so it gets its measured totals and no
                // score: an index over the answers that happen to exist describes a fraction of the instrument
                // and is indistinguishable, once stored, from an index over all of it.
                BenchmarkRunFinalizer.ApplyTotals(run, run.Answers);
                run.Status = BenchmarkRunStatus.Failed;
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

    public async Task RunAsync(long runId, CancellationToken cancellationToken, bool verboseMode = false)
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
            run.SecondOpinionBlindUsed = scoringConstants.SecondOpinionBlind;

            if (run.SecondOpinionModeUsed == 0 && run.SecondOpinionAssessorModelConfigurationId.HasValue)
            {
                run.SecondOpinionModeUsed = (int)scoringConstants.SecondOpinionMode;
            }
            else if (!run.SecondOpinionAssessorModelConfigurationId.HasValue)
            {
                run.SecondOpinionModeUsed = (int)BenchmarkSecondOpinionMode.Off;
            }

            if (run.ClaimVerifierModelConfigurationId.HasValue)
            {
                var (verifierConfig, verifierApiKey, verifierError) = await ResolveAssessorAsync(
                    db, run, run.ClaimVerifierModelConfigurationId.Value, cancellationToken);
                if (verifierConfig == null || verifierApiKey == null)
                {
                    _logger.LogWarning(
                        "Benchmark run {RunId}: claim verifier configuration {ConfigId} unusable ({Error}). Claim verification disabled for this run.",
                        run.Id, run.ClaimVerifierModelConfigurationId.Value, verifierError);
                }
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
            // The three resource caps are flat, so this run-level column records the single figure every
            // question got rather than the largest of three bands. Resolving it through
            // ResolveToolCallBudget rather than the compiled default keeps a configuration override
            // visible here. BenchmarkRunAnswer.ToolCallBudgetUsed is still the figure that applied to a
            // given question, and for runs 1-13 it carries the old per-band values.
            int maxToolCallsPerQuestion = ResolveToolCallBudget();
            run.MaxToolCallsPerQuestionUsed = maxToolCallsPerQuestion;

            // The other three caps are resolved per question, from the same configuration these
            // three reads use, so the snapshot cannot drift from the figures that were applied.
            run.ToolIterationCapsJson = RenderBandedCaps(_ => ResolveToolIterations());
            run.TotalModelCallCapsJson = RenderBandedCaps(_ => ResolveTotalModelCalls());
            run.QuestionTimeoutSecondsJson = RenderBandedCaps(
                band => ResolveBandedCap("QuestionTimeoutSeconds", band, DefaultQuestionTimeoutSeconds(band)));

            await db.SaveChangesAsync(cancellationToken);

            bool suiteHasBoard = run.BenchmarkSuite?.GameSnapshot != null;
            var promptOptions = new BenchmarkCandidatePromptOptions
            {
                VerboseMode = verboseMode,
                HasGameSnapshot = suiteHasBoard
            };
            run.CandidatePromptOptionsJson = promptOptions.ToCanonicalJson();
            run.CandidatePromptSourceUsed = "ChatService.BuildSystemPrompt";

            string systemPrompt = promptOptions.BuildSystemPrompt(_chatService, testedConfig.ParallelExecutionMode);
            var segmentedPrompt = BuildCandidateSegmentedPrompt(promptOptions, testedConfig.ParallelExecutionMode);
            PopulateInstrumentFingerprint(run, systemPrompt);

            // Check credential collision between candidate and assessor
            string testedKey = AiRequestGovernor.GetCredentialKey(testedConfig.Provider, null, testedConfig.Id);
            string assessorKey = AiRequestGovernor.GetCredentialKey(assessorConfig.Provider, null, assessorConfig.Id);
            bool credentialCollision = string.Equals(testedKey, assessorKey, StringComparison.OrdinalIgnoreCase);

            if (credentialCollision)
            {
                _logger.LogInformation("Tested and assessor models share credential key '{Key}'. Serializing assessment behind answering.", testedKey);
            }

            var createdAnswers = new ConcurrentBag<BenchmarkRunAnswer>();

            // Grading — assessment, claim verification and any per-answer second opinion — runs
            // alongside the next candidate turn rather than in front of it. The candidate itself is
            // still awaited one question at a time and its DurationMs, TtftMs and ToolTimeMs are
            // measured inside its own turn, so speed comparability across runs is unchanged; what
            // changes is that a slow assessor no longer idles the run between two questions. Each
            // grading task owns the DI scope its candidate ran in, so no DbContext is touched from
            // two tasks at once, and the bound keeps the assessor provider from being hammered.
            int maxConcurrentGrading = Math.Max(1, _configuration.GetValue<int>("Benchmark:MaxConcurrentGrading", 2));
            using var gradingGate = new SemaphoreSlim(maxConcurrentGrading, maxConcurrentGrading);
            var gradingTasks = new List<Task>();

            if (maxParallel <= 1)
            {
                // Sequential Execution
                foreach (var question in questions)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        run.Status = BenchmarkRunStatus.Canceled;
                        run.CompletedAtUtc = DateTime.UtcNow;
                        runStopwatch.Stop();
                        await ApplyAbortedTotalsAsync(db, run, runStopwatch.ElapsedMilliseconds);
                        await db.SaveChangesAsync(CancellationToken.None);
                        _runManager.Complete(runId);
                        return;
                    }

                    var qScope = _scopeFactory.CreateScope();
                    bool scopeHandedOver = false;
                    try
                    {
                        var qDb = qScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                        var qConfigService = qScope.ServiceProvider.GetRequiredService<SystemAiConfigService>();

                        var ans = await ExecuteSingleQuestionAsync(
                            qDb, qConfigService, run, question, testedConfig, testedApiKey,
                            systemPrompt, segmentedPrompt, allowedTools,
                            maxResultLength, maxToolCallsPerQuestion, cancellationToken);

                        createdAnswers.Add(ans);

                        if (!credentialCollision)
                        {
                            gradingTasks.Add(GradeAnswerAsync(
                                qScope, run, ans, question.ExpectedPoints,
                                assessorConfig, assessorApiKey, scoringConstants,
                                gradingGate, cancellationToken));
                            scopeHandedOver = true;
                        }
                    }
                    finally
                    {
                        // The grading task disposes the scope it was handed; on the collision path,
                        // where assessment is deferred to after the loop, nothing else needs it.
                        if (!scopeHandedOver) qScope.Dispose();
                    }
                }

                // Every run-level stage below needs the full set of scores, so the pipeline drains
                // here. A grading task that failed has already logged and left its answer as it
                // found it.
                await Task.WhenAll(gradingTasks);
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
                            systemPrompt, segmentedPrompt, allowedTools,
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

            // Claim Verification Stage: checks unverified claims against source and wiki.
            //
            // Ahead of the two run-level second-opinion stages below, so every second opinion in the
            // run reads the same verification state. It used to follow them, which made "did this
            // second reader see a refuted claim?" depend on which trigger selected it: the per-answer
            // path verifies before its own second opinion, so a flagged answer's second reader was
            // handed the refutation while an outlier-selected or sample-selected one was not. That
            // made the pooled agreement figure a mixture of two different measurements.
            _runManager.MarkStage(runId, BenchmarkRunStage.Verifying);
            await RunClaimVerificationAsync(db, configService, run, cancellationToken);

            // Stage 3, in FlaggedAndOutliers mode only: answers far below this run's own median.
            // It has to wait for every answer because it needs that median, which is the whole
            // reason it is a separate stage rather than another per-answer trigger. Placed before
            // synthesis so the synthesis sees the run in its final graded state.
            _runManager.MarkStage(runId, BenchmarkRunStage.SecondOpinion);
            await RunOutlierSweepAsync(db, configService, run, scoringConstants, cancellationToken);

            // FlaggedPlusSample only: top up second-opinion coverage to the profile's configured
            // minimum sample, deterministically, so a run that graded well still yields a grader
            // agreement figure (H3). Also waits for every answer, for the same reason as the
            // outlier sweep above: "lowest quality score first" needs the full set of scores.
            await RunSecondOpinionSampleTopUpAsync(db, configService, run, profile, scoringConstants, cancellationToken);

            // Again, because a critical-error split is one of the things that makes an answer a
            // verification candidate and only a second opinion can produce one. The pass filters on
            // ClaimVerificationJson and ClaimVerificationError both being null, so it re-checks
            // nothing and returns before any model call when the two stages above found no split.
            _runManager.MarkStage(runId, BenchmarkRunStage.Verifying);
            await RunClaimVerificationAsync(db, configService, run, cancellationToken);

            // Final Synthesis Pass
            _runManager.MarkStage(runId, BenchmarkRunStage.Synthesizing);
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
                runStopwatch.Stop();
                await ApplyAbortedTotalsAsync(db, run, runStopwatch.ElapsedMilliseconds);
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
                runStopwatch.Stop();
                await ApplyAbortedTotalsAsync(db, run, runStopwatch.ElapsedMilliseconds);
                await db.SaveChangesAsync();
            }
        }
        finally
        {
            _runManager.Complete(runId);
        }
    }

    /// <summary>
    /// Re-executes the answers a run failed on, in place, and re-runs every run-level grading stage
    /// over the whole run.
    ///
    /// It carries the same two terminal handlers as <see cref="ExecuteRunAsync"/> and for the same
    /// reason: without them a throw escaped the method with the row still reading <c>Running</c> and
    /// no owner in <see cref="BenchmarkRunManager"/>, which is a state nothing in the UI can leave.
    /// Both open their own scope, because the one above may be gone by the time they run.
    /// </summary>
    public async Task RunFailedQuestionsAsync(long runId, CancellationToken cancellationToken)
    {
        var rerunStopwatch = Stopwatch.StartNew();

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

            if (run.ClaimVerifierModelConfigurationId.HasValue)
            {
                var (verifierConfig, verifierApiKey, verifierError) = await ResolveAssessorAsync(
                    db, run, run.ClaimVerifierModelConfigurationId.Value, cancellationToken);
                if (verifierConfig == null || verifierApiKey == null)
                {
                    _logger.LogWarning(
                        "Benchmark run {RunId}: claim verifier configuration {ConfigId} unusable ({Error}). Claim verification disabled for this run.",
                        run.Id, run.ClaimVerifierModelConfigurationId.Value, verifierError);
                }
            }

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
            run.RerunStartedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            var profile = run.ScoringProfileId.HasValue
                ? await _scoringProfileService.GetProfileByIdAsync(run.ScoringProfileId.Value) ?? await _scoringProfileService.GetDefaultProfileAsync()
                : await _scoringProfileService.GetDefaultProfileAsync();
            var scoringConstants = _scoringProfileService.ToConstants(profile);

            var allowedTools = _configuration.GetSection("Benchmark:AllowedTools").Get<List<string>>() ?? _defaultAllowedTools;
            int maxResultLength = _configuration.GetValue<int>("Benchmark:MaxResultLength", 10000);
            int maxCallsPerSession = _configuration.GetValue<int>("Benchmark:MaxCallsPerSession", 50);

            bool suiteHasBoard = run.BenchmarkSuite?.GameSnapshot != null;
            var promptOptions = !string.IsNullOrWhiteSpace(run.CandidatePromptOptionsJson)
                ? BenchmarkCandidatePromptOptions.FromJson(run.CandidatePromptOptionsJson)
                : new BenchmarkCandidatePromptOptions { HasGameSnapshot = suiteHasBoard };
            string systemPrompt = promptOptions.BuildSystemPrompt(_chatService, testedConfig.ParallelExecutionMode);
            var segmentedPrompt = BuildCandidateSegmentedPrompt(promptOptions, testedConfig.ParallelExecutionMode);

            // The re-run's own instrument, recorded in its own columns. The five original
            // fingerprints describe the instrument the run's other answers were produced under, and
            // they are the only record that the prompt did not move between two runs; overwriting
            // them falsifies the provenance of every answer this pass does not touch.
            PopulateRerunInstrumentFingerprint(run, systemPrompt);

            var suiteQuestions = (run.BenchmarkSuite?.Questions ?? new List<BenchmarkQuestion>())
                .ToDictionary(q => q.OrderIndex, q => q.ExpectedPoints);

            _runManager.MarkStage(runId, BenchmarkRunStage.Answering);

            foreach (var answer in failedAnswers)
            {
                cancellationToken.ThrowIfCancellationRequested();

                await ReExecuteSingleAnswerAsync(
                    db, configService, run, answer, testedConfig, testedApiKey,
                    systemPrompt, segmentedPrompt, allowedTools,
                    maxResultLength, maxCallsPerSession, cancellationToken);

                suiteQuestions.TryGetValue(answer.OrderIndex, out var ep);
                await ExecutePerQuestionAssessmentAsync(
                    db, configService, run, answer, ep,
                    assessorConfig, assessorApiKey, scoringConstants, cancellationToken);
            }

            // The same run-level sequence, in the same order, as ExecuteRunAsync. The ordering is
            // load-bearing there — see the comment on its claim-verification stage — and a re-run
            // that skips the middle two stages reintroduces the mixed-measurement problem that
            // ordering fix removed, for a run whose figures are then compared with a clean one's.
            _runManager.MarkStage(runId, BenchmarkRunStage.Verifying);
            await RunClaimVerificationAsync(db, configService, run, cancellationToken);

            _runManager.MarkStage(runId, BenchmarkRunStage.SecondOpinion);
            await RunOutlierSweepAsync(db, configService, run, scoringConstants, cancellationToken);
            await RunSecondOpinionSampleTopUpAsync(db, configService, run, profile, scoringConstants, cancellationToken);

            _runManager.MarkStage(runId, BenchmarkRunStage.Verifying);
            await RunClaimVerificationAsync(db, configService, run, cancellationToken);

            _runManager.MarkStage(runId, BenchmarkRunStage.Synthesizing);
            await ExecuteFinalSynthesisAsync(db, configService, run, assessorConfig, assessorApiKey, scoringConstants, cancellationToken);

            rerunStopwatch.Stop();
            run.RerunCompletedAtUtc = DateTime.UtcNow;

            var allAnswers = await db.BenchmarkRunAnswers.Where(a => a.BenchmarkRunId == run.Id).ToListAsync(CancellationToken.None);

            // preserveCompletedAt: the run's elapsed wall time is the original execution's, and a
            // re-run launched hours later would otherwise absorb the interval into it. Its own span
            // is in the two Rerun columns.
            BenchmarkRunFinalizer.Apply(run, allAnswers, preserveCompletedAt: true);
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
                run.ErrorMessage = "Failed-question re-run canceled.";
                rerunStopwatch.Stop();
                run.RerunCompletedAtUtc = DateTime.UtcNow;
                run.CompletedAtUtc ??= DateTime.UtcNow;
                await ApplyAbortedTotalsAsync(db, run, null);
                await db.SaveChangesAsync(CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Benchmark run {RunId} failed-question re-run failed with exception.", runId);
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var run = await db.BenchmarkRuns.FindAsync(runId);
            if (run != null)
            {
                run.Status = BenchmarkRunStatus.Failed;
                run.ErrorMessage = BenchmarkAssessmentFailure.Truncate(ex.Message);
                rerunStopwatch.Stop();
                run.RerunCompletedAtUtc = DateTime.UtcNow;
                run.CompletedAtUtc ??= DateTime.UtcNow;
                await ApplyAbortedTotalsAsync(db, run, null);
                await db.SaveChangesAsync(CancellationToken.None);
            }
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
    /// This now serves <c>QuestionTimeoutSeconds</c> alone. The three resource caps are no longer
    /// banded — see <see cref="ResolveFlatCap"/> — and the timeout stays banded because it is pinned
    /// to the speed-score floor by an invariant BenchmarkScoringTests asserts, which flattening
    /// would break.
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
    /// A per-question cap rendered for every difficulty band, as canonical JSON:
    /// <c>{"Simple":n,"Intermediate":n,"Advanced":n}</c>, bands in that fixed order and numbers in
    /// the invariant culture, so two runs configured alike render byte-identical text and the value
    /// can be compared as a fingerprint. A flat cap renders the same figure under all three bands,
    /// which is what applied to each of them.
    /// </summary>
    private static string RenderBandedCaps(Func<BenchmarkDifficulty, int> resolve)
    {
        var bands = new[]
        {
            BenchmarkDifficulty.Simple,
            BenchmarkDifficulty.Intermediate,
            BenchmarkDifficulty.Advanced
        };

        var parts = bands.Select(band =>
            $"\"{band}\":{resolve(band).ToString(System.Globalization.CultureInfo.InvariantCulture)}");

        return "{" + string.Join(",", parts) + "}";
    }

    /// <summary>
    /// Reads a flat per-question cap, falling back to the compiled default.
    ///
    /// The three resource caps are no longer banded: every question gets what the Advanced band used
    /// to get, so a benchmark question is never stopped by a limit a production chat session would
    /// not have hit. Their configuration keys are plain values — a key cannot be both a value and a
    /// section, so the banded sections were removed rather than kept as fallbacks, exactly as the
    /// flat keys they had replaced were removed when banding was introduced. A leftover banded key
    /// makes this read return 0 and silently fall through to the default, so upgrades must delete it.
    /// </summary>
    private int ResolveFlatCap(string key, int fallback)
    {
        int configured = _configuration.GetValue<int>($"Benchmark:{key}", 0);
        return configured > 0 ? configured : fallback;
    }

    /// <summary>
    /// Total tool calls a question may execute. This is the cap that is meant to bind on a
    /// saturated question: exhausting it blocks further calls, flags the answer
    /// <c>ToolBudgetExhausted</c>, and is explained in the run report. The other three caps are
    /// sized so they do not bind first.
    /// </summary>
    private int ResolveToolCallBudget() => ResolveFlatCap("ToolCallBudget", DefaultToolCallBudget());

    /// <summary>
    /// Sequential tool rounds — one model call plus the batch of tool calls it emitted, then the
    /// results fed back. This bounds an investigation's *depth*, not its width: a model batching
    /// three calls per round spends three times the budget per iteration, and the 2026-09-03 run
    /// batched at roughly that rate when saturated. Sized at about half the tool call budget, so
    /// a model batching two calls per round can still spend the whole budget.
    /// </summary>
    private int ResolveToolIterations() => ResolveFlatCap("ToolIterations", DefaultToolIterations());

    /// <summary>
    /// Total provider requests for the question. A runaway-loop safety net, not a tuning knob:
    /// hitting it forces a final response with only a debug line to show for it, so it is sized
    /// four to six above the iteration cap and must never be the cap that stops a healthy
    /// question.
    /// </summary>
    private int ResolveTotalModelCalls() => ResolveFlatCap("TotalModelCalls", DefaultTotalModelCalls());

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

    // The three resource caps are flat: every question now gets what the Advanced band used to get.
    // Banding them was a response to a flat 25 starving Advanced questions on the 2026-09-03 run —
    // that is the evidence for choosing 45, not an argument for banding. Run 13 then showed two
    // Intermediate questions stopped at 32 of 35 while running below the run's mean, so the band was
    // still binding on questions a production chat session would have let run. Production applies no
    // per-question cap at these levels, so matching the Advanced figures everywhere is what makes the
    // harness comparable to production. QuestionTimeoutSeconds stays banded — see ResolveBandedCap.
    internal static int DefaultToolCallBudget() => 45;
    internal static int DefaultToolIterations() => 22;
    internal static int DefaultTotalModelCalls() => 28;
    internal static int DefaultQuestionTimeoutSeconds(BenchmarkDifficulty band) => band switch
    {
        BenchmarkDifficulty.Simple => 420,
        BenchmarkDifficulty.Intermediate => 600,
        BenchmarkDifficulty.Advanced => 720,
        _ => 600
    };

    /// <summary>
    /// The candidate's pricing card, needed only for its long-context threshold when bucketing an answer's
    /// model calls. Resolved per answer from its own scope because ModelPricingService is scoped and this
    /// method runs on both the sequential and the parallel answer paths; the cost is negligible beside the
    /// model call it accompanies. Null — an unpriced model — buckets to all zeros.
    /// </summary>
    private ModelPricing? ResolveCandidatePricing(SystemAiApiConfiguration testedConfig)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var pricingService = scope.ServiceProvider.GetRequiredService<ModelPricingService>();
            return pricingService.Resolve(testedConfig);
        }
        catch (Exception ex)
        {
            // Bucketing is a costing refinement, never a reason to lose an answer that has already run.
            _logger.LogWarning(ex, "Failed to resolve candidate pricing for long-context bucketing.");
            return null;
        }
    }

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
        SegmentedPrompt? segmentedPrompt,
        List<string> allowedTools,
        int maxResultLength,
        int maxCallsPerSession,
        CancellationToken cancellationToken)
    {
        // The three resource caps are the same for every question in a run; only the timeout is still
        // resolved per difficulty band. A flat 25 starved advanced questions - Q11, Q16 and Q18 of the
        // 2026-09-03 run each exhausted it and had further calls blocked mid-investigation, which alone
        // moved an otherwise clean run to CompletedWithLimits. That is the evidence for 45, not for
        // banding: run 13 then had two Intermediate questions stop at 32 of 35 while running below the
        // run's mean, so the band was binding on questions production would have let run.
        int toolCallBudget = ResolveToolCallBudget();
        int maxToolIterations = ResolveToolIterations();
        int maxTotalModelCalls = ResolveTotalModelCalls();

        // The stored tool-call record's payload caps, derived from the same maxResultLength the
        // agent context below is given, so the two cannot drift apart.
        var toolCallRecordLimits = BenchmarkToolCallRecordLimits.Resolve(_configuration, maxResultLength);

        var runRequest = new AgentRunRequest
        {
            ProviderName = testedConfig.Provider,
            ModelId = testedConfig.ModelId,
            ApiKey = testedApiKey,
            ModelDisplayName = testedConfig.DisplayName,
            SystemPrompt = systemPrompt,
            FrozenPrefix = segmentedPrompt?.FrozenPrefix,
            SessionPrefix = segmentedPrompt?.SessionPrefix,
            VolatileSuffix = segmentedPrompt?.VolatileSuffix,
            SegmentedPrompt = segmentedPrompt,
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
            /* A benchmark run is not a chat session, and SessionId is borrowed here for the
               tool-audit log tag and -- where no ToolBudgetScopeId is given -- the rate-limit
               scope key. Wrapping the run id keeps both strings exactly what they were; the
               conflation predates the typed reference and is now at least visible. */
            ToolExecutionContext = new Tools.ToolExecutionContext
            {
                SessionId = Overseer.Services.Privacy.SessionRef.Persistent(run.Id),
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

        // Retained for the typed classifier: a transport failure's message is an operating-system
        // string in the machine's display language, so the exception type is the only locale-stable
        // evidence of one. Null on the streamed "error" event path, which carries a string only.
        Exception? terminalException = null;

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
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested && questionCts.IsCancellationRequested)
        {
            terminalError = $"Per-question timeout exceeded ({perQuestionTimeoutSec} s).";
            terminalException = ex;
        }
        catch (Exception ex)
        {
            terminalError = ex.Message;
            terminalException = ex;
        }
        finally
        {
            _runManager.ClearQuestionInFlight(run.Id, question.OrderIndex);
        }
        sw.Stop();

        var classification = BenchmarkProviderErrorClassifier.Classify(
            terminalException, terminalError, cancellationToken.IsCancellationRequested);
        var sanitized = BenchmarkAnswerSanitizer.Sanitize(runResult.FinalText);

        // Bucketed per model call, never from the answer's summed tokens: an agentic answer's sum crosses
        // any published threshold routinely while no single request comes close. All zero for a flat-rate
        // candidate, which is what makes storing them unconditional safe.
        var longContextBuckets = ModelPricingService.ComputeLongContextBuckets(
            ResolveCandidatePricing(testedConfig), runResult.ModelCallUsages);

        var succeededCalls = runResult.ToolCalls
            .Where(tc => tc.Status == "completed" && string.IsNullOrEmpty(tc.Error) && !string.IsNullOrEmpty(tc.Name))
            .GroupBy(tc => tc.Name!)
            .Select(g => $"{g.Key}×{g.Count()}")
            .ToList();

        int blockedCount = runResult.ToolCalls.Count(tc => BenchmarkToolCallRecorder.IsBudgetRefusal(tc.Error));
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
            BenchmarkQuestionIdUsed = question.Id,
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
            LongContextInputTokens = longContextBuckets.InputTokens,
            LongContextOutputTokens = longContextBuckets.OutputTokens,
            LongContextCacheReadTokens = longContextBuckets.CacheReadTokens,
            LongContextCacheCreationTokens = longContextBuckets.CacheCreationTokens,
            ModelCallCount = runResult.ModelCallCount,
            ToolCallCount = runResult.ToolCallCount,
            ToolCallsBlocked = runResult.ToolCallsBlocked,
            ToolBudgetExhausted = runResult.ToolBudgetExhausted,
            ToolCallBudgetUsed = toolCallBudget,
            ToolTimeMs = runResult.ToolTimeMs,
            TerminationReason = runResult.TerminationReason,
            ProviderFinishReason = TruncateFinishReason(runResult.ProviderFinishReason),
            ScrubbedArtifactText = sanitized.ScrubbedArtifactText,
            ScrubbedArtifactCount = sanitized.ScrubbedArtifactCount,
            NarrationBlockCount = sanitized.NarrationBlockCount,
            AnswerFlags = (int)sanitized.Flags,

            // The per-call record in emission order. ToolCallSummary above lists only the calls
            // that succeeded; these rows also carry the ones that failed, returned too much, or
            // were refused for budget — the calls a tool-layer diagnosis is actually about.
            ToolCalls = BenchmarkToolCallRecorder.Build(runResult.ToolCalls, toolCallRecordLimits)
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
        SegmentedPrompt? segmentedPrompt,
        List<string> allowedTools,
        int maxResultLength,
        int maxCallsPerSession,
        CancellationToken cancellationToken)
    {
        int toolCallBudget = ResolveToolCallBudget();
        int maxToolIterations = ResolveToolIterations();
        int maxTotalModelCalls = ResolveTotalModelCalls();

        // The stored tool-call record's payload caps, derived from the same maxResultLength the
        // agent context below is given, so the two cannot drift apart.
        var toolCallRecordLimits = BenchmarkToolCallRecordLimits.Resolve(_configuration, maxResultLength);

        var runRequest = new AgentRunRequest
        {
            ProviderName = testedConfig.Provider,
            ModelId = testedConfig.ModelId,
            ApiKey = testedApiKey,
            ModelDisplayName = testedConfig.DisplayName,
            SystemPrompt = systemPrompt,
            FrozenPrefix = segmentedPrompt?.FrozenPrefix,
            SessionPrefix = segmentedPrompt?.SessionPrefix,
            VolatileSuffix = segmentedPrompt?.VolatileSuffix,
            SegmentedPrompt = segmentedPrompt,
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
                SessionId = Overseer.Services.Privacy.SessionRef.Persistent(run.Id),
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

        // See ExecuteSingleQuestionAsync for why the exception itself is retained.
        Exception? terminalException = null;

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
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested && questionCts.IsCancellationRequested)
        {
            terminalError = $"Per-question timeout exceeded ({perQuestionTimeoutSec} s).";
            terminalException = ex;
        }
        catch (Exception ex)
        {
            terminalError = ex.Message;
            terminalException = ex;
        }
        finally
        {
            _runManager.ClearQuestionInFlight(run.Id, answer.OrderIndex);
        }
        sw.Stop();

        var classification = BenchmarkProviderErrorClassifier.Classify(
            terminalException, terminalError, cancellationToken.IsCancellationRequested);
        var sanitized = BenchmarkAnswerSanitizer.Sanitize(runResult.FinalText);

        // Bucketed per model call, never from the answer's summed tokens: an agentic answer's sum crosses
        // any published threshold routinely while no single request comes close. All zero for a flat-rate
        // candidate, which is what makes storing them unconditional safe.
        var longContextBuckets = ModelPricingService.ComputeLongContextBuckets(
            ResolveCandidatePricing(testedConfig), runResult.ModelCallUsages);

        var succeededCalls = runResult.ToolCalls
            .Where(tc => tc.Status == "completed" && string.IsNullOrEmpty(tc.Error) && !string.IsNullOrEmpty(tc.Name))
            .GroupBy(tc => tc.Name!)
            .Select(g => $"{g.Key}×{g.Count()}")
            .ToList();

        int blockedCount = runResult.ToolCalls.Count(tc => BenchmarkToolCallRecorder.IsBudgetRefusal(tc.Error));
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
        answer.LongContextInputTokens = longContextBuckets.InputTokens;
        answer.LongContextOutputTokens = longContextBuckets.OutputTokens;
        answer.LongContextCacheReadTokens = longContextBuckets.CacheReadTokens;
        answer.LongContextCacheCreationTokens = longContextBuckets.CacheCreationTokens;
        answer.ModelCallCount = runResult.ModelCallCount;
        answer.ToolCallCount = runResult.ToolCallCount;
        answer.ToolCallsBlocked = runResult.ToolCallsBlocked;
        answer.ToolBudgetExhausted = runResult.ToolBudgetExhausted;
        answer.ToolCallBudgetUsed = toolCallBudget;
        answer.ToolTimeMs = runResult.ToolTimeMs;
        answer.TerminationReason = runResult.TerminationReason;
        answer.ProviderFinishReason = TruncateFinishReason(runResult.ProviderFinishReason);
        answer.ScrubbedArtifactText = sanitized.ScrubbedArtifactText;
        answer.ScrubbedArtifactCount = sanitized.ScrubbedArtifactCount;
        answer.NarrationBlockCount = sanitized.NarrationBlockCount;
        answer.AnswerFlags = (int)sanitized.Flags;

        // A rerun replaces this answer's tool-call record rather than appending to it: the rows
        // from the previous turn are deleted first, or an answer accumulates two turns' worth of
        // calls and every count derived from the rows silently doubles. The delete goes through the
        // DbSet, not the navigation property — the rerun entry points load the answer without its
        // ToolCalls, so that collection is empty and clearing it would delete nothing.
        await db.BenchmarkRunAnswerToolCalls
            .Where(tc => tc.BenchmarkRunAnswerId == answer.Id)
            .ExecuteDeleteAsync(CancellationToken.None);

        // ExecuteDeleteAsync bypasses the change tracker, so any instance a caller did load is now
        // tracked against a row that no longer exists; leaving it attached makes the save below try
        // to delete or update it a second time.
        foreach (var stale in db.ChangeTracker.Entries<BenchmarkRunAnswerToolCall>()
            .Where(e => e.Entity.BenchmarkRunAnswerId == answer.Id)
            .ToList())
        {
            stale.State = EntityState.Detached;
        }

        answer.ToolCalls = BenchmarkToolCallRecorder.Build(runResult.ToolCalls, toolCallRecordLimits);

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

    /// <summary>
    /// One question's grading, run inside the DI scope its candidate turn used and bounded by
    /// <paramref name="gate"/>. The scope is disposed here, so a caller that hands one over must
    /// not dispose it itself.
    ///
    /// A failure is logged and dropped rather than propagated: the answer keeps whatever verdict it
    /// has and the finalizer treats it as it treats any other unscored answer. Cancellation still
    /// propagates, so a canceled run is still canceled.
    /// </summary>
    private async Task GradeAnswerAsync(
        IServiceScope scope,
        BenchmarkRun run,
        BenchmarkRunAnswer answer,
        string? expectedPoints,
        SystemAiApiConfiguration assessorConfig,
        string assessorApiKey,
        BenchmarkScoringConstants scoringConstants,
        SemaphoreSlim gate,
        CancellationToken cancellationToken)
    {
        try
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var configService = scope.ServiceProvider.GetRequiredService<SystemAiConfigService>();

                await ExecutePerQuestionAssessmentAsync(
                    db, configService, run, answer, expectedPoints,
                    assessorConfig, assessorApiKey, scoringConstants, cancellationToken);
            }
            finally
            {
                gate.Release();
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Benchmark run {RunId} answer {OrderIndex}: per-question grading failed. The answer keeps its current verdict.",
                run.Id, answer.OrderIndex);
        }
        finally
        {
            scope.Dispose();
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
        // An answer with no text has nothing for a grader to read, and the assessor's empty reply was being
        // recorded as a harness stage failure — a guaranteed one, paid for at assessor rates on every empty
        // answer. Which branch applies is the whole distinction scoring method 10 rests on: a model that
        // ended its turn normally and returned nothing failed to answer and scores 0; an answer destroyed
        // in transit is not the candidate's fault and stays unscored. Either way the run reports
        // CompletedWithErrors — an empty answer is an error whatever produced it.
        if (answer.Status == BenchmarkAnswerStatus.EmptyAnswer || string.IsNullOrWhiteSpace(answer.AnswerText))
        {
            if (BenchmarkRunFinalizer.IsModelProducedEmptyAnswer(answer))
            {
                answer.QualityScore = 0;
                answer.RawQualityScore = 0;
                answer.Score = 0;
                answer.AssessmentStatus = BenchmarkAssessmentStatus.Scored;
                answer.ReviewComment =
                    "Not assessed by a grader: the model ended its turn without producing an answer. "
                    + "Scored 0 under scoring method 10.";
                answer.AssessmentError = null;
            }
            else
            {
                answer.AssessmentStatus = BenchmarkAssessmentStatus.Failed;
                answer.AssessmentError =
                    "Not assessed: the answer contained no text, and the provider reported no normal stop.";
            }

            await db.SaveChangesAsync(CancellationToken.None);
            return;
        }

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
                SessionId = Overseer.Services.Privacy.SessionRef.Persistent(run.Id),
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
        int assessmentCacheReadTokens = runResult.CacheReadTokens;
        int assessmentCacheCreationTokens = runResult.CacheCreationTokens;

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
            assessmentCacheReadTokens += retryResult.CacheReadTokens;
            assessmentCacheCreationTokens += retryResult.CacheCreationTokens;
            if (retryResult.TotalPromptTokens > 0) runResult = retryResult;
        }

        sw.Stop();
        answer.AssessmentInputTokens = assessmentInputTokens;
        answer.AssessmentOutputTokens = assessmentOutputTokens;
        answer.AssessmentCacheReadTokens = assessmentCacheReadTokens;
        answer.AssessmentCacheCreationTokens = assessmentCacheCreationTokens;
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

            // Scoring method v8: the assessor records a rubric point it placed outside the
            // question's scope under the OUT-OF-SCOPE: marker rather than deducting for it. Set on
            // every (re-)assessment through this path, mirroring every other per-verdict field
            // above; a re-assessment must not inherit the previous verdict's marker if the new one
            // did not repeat it. The second-opinion and trial paths never write this: they persist
            // an advisory JSON blob rather than replacing the primary verdict's fields, so there is
            // nothing on the answer entity for them to correct here.
            answer.CompletenessOutOfScope = res.CompletenessOutOfScope;

            // Scoring method v9's Readability counterpart, written on the same terms and for the
            // same reason: a rubric format suggestion the answer did not follow is recorded under
            // the FORM: marker rather than deducted for.
            answer.ReadabilityFormOnly = res.ReadabilityFormOnly;

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

            if (res.UnevidencedDeduction)
            {
                flags |= BenchmarkAnswerFlags.UnevidencedDeduction;
            }
            else
            {
                flags &= ~BenchmarkAnswerFlags.UnevidencedDeduction;
            }

            if (res.OmissionAsAccuracy)
            {
                flags |= BenchmarkAnswerFlags.OmissionAsAccuracy;
            }
            else
            {
                flags &= ~BenchmarkAnswerFlags.OmissionAsAccuracy;
            }

            if (res.AccuracyOutOfRubric)
            {
                flags |= BenchmarkAnswerFlags.OutOfRubricAccuracyDeduction;
            }
            else
            {
                flags &= ~BenchmarkAnswerFlags.OutOfRubricAccuracyDeduction;
            }

            // Read off the graded answer text and not cleared on re-assessment: the opener is the
            // candidate's own output, so a second grading pass over the same text cannot change it.
            if (BenchmarkArtifactScrubber.HasAnswerFramingOpener(answer.AnswerText))
            {
                flags |= BenchmarkAnswerFlags.AnswerFramingOpener;
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

            if (res.UnevidencedDeduction)
            {
                _logger.LogInformation(
                    "Benchmark run {RunId} answer {OrderIndex}: assessor docked a level while evidence names no defect; recorded as an unevidenced deduction.",
                    run.Id, answer.OrderIndex);
            }

            if (res.OmissionAsAccuracy)
            {
                _logger.LogInformation(
                    "Benchmark run {RunId} answer {OrderIndex}: assessor docked accuracy citing an omission; recorded as omission as accuracy.",
                    run.Id, answer.OrderIndex);
            }

            if (res.AccuracyOutOfRubric)
            {
                _logger.LogInformation(
                    "Benchmark run {RunId} answer {OrderIndex}: assessor marked an accuracy deduction \"{Marker}\"; recorded as an out-of-rubric deduction.",
                    run.Id, answer.OrderIndex, BenchmarkAssessmentParser.OutOfRubricAccuracyMarker);
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

        // A critical-error quote is checked here too, not only an unadjudicable claim: the quote is
        // the one assertion in the answer whose truth the cap already turns on, and the assessor
        // grades without tools while the verifier has them. So is the statement an out-of-rubric
        // Accuracy deduction rests on, which the assessor gave from its own knowledge.
        if (run.ClaimVerifierModelConfigurationId.HasValue && NeedsClaimVerification(answer))
        {
            try
            {
                var (verifierConfig, verifierApiKey, resolveError) = await ResolveAssessorAsync(
                    db, run, run.ClaimVerifierModelConfigurationId.Value, cancellationToken);
                if (verifierConfig != null && verifierApiKey != null)
                {
                    await VerifyAnswerClaimsAsync(
                        db, configService, run, answer, verifierConfig, verifierApiKey, expectedPoints, cancellationToken);
                }
                else
                {
                    _logger.LogWarning(
                        "Benchmark run {RunId} answer {OrderIndex}: claim verifier configuration unusable ({Error}).",
                        run.Id, answer.OrderIndex, resolveError);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Benchmark run {RunId} answer {OrderIndex}: per-answer claim verification threw.",
                    run.Id, answer.OrderIndex);
            }
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
    internal static string? ResolveSecondOpinionTrigger(
        BenchmarkRunAnswer answer,
        BenchmarkSecondOpinionMode mode,
        BenchmarkScoringConstants constants)
    {
        if (mode == BenchmarkSecondOpinionMode.Off) return null;

        // Ahead of the All short-circuit deliberately: an answer the quality index excludes has no
        // gradeable text, so a second verdict on it grades a transport failure and then contaminates
        // the run's grader-agreement aggregates. All must not select one either.
        if (!BenchmarkRunFinalizer.CountsTowardQualityIndex(answer)) return null;

        if (mode == BenchmarkSecondOpinionMode.All) return SecondOpinionTriggers.All;

        if (answer.CriticalError) return SecondOpinionTriggers.CriticalError;

        // Finding F3: Claim verification runs BEFORE this cascade now. If any claim was refuted,
        // that is hard evidence of an accuracy defect and takes precedence over soft triggers.
        if ((((BenchmarkAnswerFlags)answer.AnswerFlags) & BenchmarkAnswerFlags.RefutedClaim) != 0)
        {
            return SecondOpinionTriggers.RefutedClaim;
        }

        // The Q10 shape: the assessor wrote "hallucinates 'adamantium'" and left criticalError
        // false, so no existing trigger saw it and the synthesis reported the hallucination the
        // per-question verdict had declined to flag.
        if ((((BenchmarkAnswerFlags)answer.AnswerFlags) & BenchmarkAnswerFlags.ContestedVerdict) != 0)
        {
            return SecondOpinionTriggers.ContestedVerdict;
        }

        // The assessor prefixed an accuracy deduction with "Not in rubric:", declaring the basis for
        // it outside the instrument. Weaker evidence than a refutation or a self-described
        // fabrication, and stronger than an unevidenced deduction, because the grader has stated
        // where the basis came from. Gated on the level so a level-6 answer whose evidence merely
        // mentions an out-of-rubric observation does not spend a verdict.
        if ((((BenchmarkAnswerFlags)answer.AnswerFlags) & BenchmarkAnswerFlags.OutOfRubricAccuracyDeduction) != 0
            && (answer.AccuracyLevel ?? 6) <= BenchmarkVerdictConsistency.UnevidencedDeductionMaxLevel)
        {
            return SecondOpinionTriggers.OutOfRubricAccuracy;
        }

        // The Q1 shape from run 8: Accuracy 4/6 with accuracyEvidence "Matches rubric." — a deduction on
        // the 55%-weight dimension whose stated basis says nothing was wrong. Which of the two the grader
        // meant is not a judgement the harness is in a position to make, so it asks a second reader.
        if ((((BenchmarkAnswerFlags)answer.AnswerFlags) & BenchmarkAnswerFlags.UnevidencedDeduction) != 0)
        {
            return SecondOpinionTriggers.UnevidencedDeduction;
        }

        // Finding F1: Grader docked accuracy citing an omission. The second assessor gets a neutral prompt.
        if ((((BenchmarkAnswerFlags)answer.AnswerFlags) & BenchmarkAnswerFlags.OmissionAsAccuracy) != 0)
        {
            return SecondOpinionTriggers.OmissionAsAccuracy;
        }

        // Finding F3: UnverifiedClaims trigger condition adjusted.
        // If all unverified claims were positively supported, do NOT fire this trigger.
        bool allClaimsSupported = answer.ClaimsSupportedCount.HasValue
            && answer.ClaimsSupportedCount.Value == (answer.UnverifiedClaimCount ?? 0)
            && (answer.ClaimsRefutedCount ?? 0) == 0
            && (answer.ClaimsIndeterminateCount ?? 0) == 0;

        if (!allClaimsSupported
            && (answer.UnverifiedClaimCount ?? 0) > 0
            && (answer.AccuracyLevel ?? 6) <= UnverifiedClaimsAccuracyMaxLevel)
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
    /// <see cref="BenchmarkSecondOpinionMode.FlaggedPlusSample"/> only: tops up second-opinion
    /// coverage to <see cref="BenchmarkScoringProfile.SecondOpinionMinimumSample"/> after the
    /// per-answer triggers have resolved, so a run where nothing tripped a trigger still yields a
    /// grader agreement figure (H3) — under <see cref="BenchmarkSecondOpinionMode.Flagged"/> alone,
    /// coverage falls to zero exactly as a candidate gets good.
    ///
    /// Selection is deterministic on purpose: lowest quality score first, ties broken by ascending
    /// order index, so the same data selects the same answers on every run and a re-run cannot be
    /// used to fish for a different sample. Answers already graded twice (by any trigger, including
    /// a prior call to this method) count toward the target and are never re-selected.
    ///
    /// A failure here never fails the run: an answer keeps exactly the verdict it has, exactly like
    /// <see cref="RunOutlierSweepAsync"/>.
    /// </summary>
    private async Task RunSecondOpinionSampleTopUpAsync(
        ApplicationDbContext db,
        SystemAiConfigService configService,
        BenchmarkRun run,
        BenchmarkScoringProfile profile,
        BenchmarkScoringConstants constants,
        CancellationToken cancellationToken)
    {
        if (!run.SecondOpinionAssessorModelConfigurationId.HasValue) return;
        if (ResolveSecondOpinionMode(run, constants) != BenchmarkSecondOpinionMode.FlaggedPlusSample) return;

        int minimumSample = profile.SecondOpinionMinimumSample;
        if (minimumSample <= 0)
        {
            // Misconfigured (FlaggedPlusSample with no target): behave like Flagged alone rather
            // than throw, and report zero achieved rather than leaving the column at its default,
            // which would be indistinguishable from "never ran".
            run.SecondOpinionSampleCountUsed = 0;
            return;
        }

        var scored = await db.BenchmarkRunAnswers
            .Where(a => a.BenchmarkRunId == run.Id &&
                        a.Status == BenchmarkAnswerStatus.Ok &&
                        a.QualityScore.HasValue)
            .ToListAsync(cancellationToken);

        int alreadyGraded = scored.Count(a => a.SecondOpinionQualityScore != null);
        int needed = minimumSample - alreadyGraded;

        if (needed <= 0)
        {
            run.SecondOpinionSampleCountUsed = alreadyGraded;
            return;
        }

        var candidates = scored
            .Where(a => a.SecondOpinionQualityScore == null)
            .OrderBy(a => a.QualityScore!.Value)
            .ThenBy(a => a.OrderIndex)
            .Take(needed)
            .ToList();

        if (candidates.Count == 0)
        {
            run.SecondOpinionSampleCountUsed = alreadyGraded;
            return;
        }

        _logger.LogInformation(
            "Benchmark run {RunId}: sample top-up re-grading {Count} answer(s) to reach the configured minimum sample of {MinimumSample} ({AlreadyGraded} already graded twice).",
            run.Id, candidates.Count, minimumSample, alreadyGraded);

        var suiteQuestions = await db.BenchmarkQuestions
            .Where(q => q.BenchmarkSuiteId == run.BenchmarkSuiteId)
            .ToDictionaryAsync(q => q.OrderIndex, q => q.ExpectedPoints, cancellationToken);

        // "Lowest quality score first" is the selection rule and was never an execution order, so
        // the selected opinions run concurrently under the same bound the per-question grading
        // pipeline uses. Each owns a DI scope and re-loads its answer by Id there, so no DbContext
        // is touched from two tasks at once.
        int maxConcurrentGrading = Math.Max(1, _configuration.GetValue<int>("Benchmark:MaxConcurrentGrading", 2));
        int gradedThisPass = 0;

        using (var gate = new SemaphoreSlim(maxConcurrentGrading, maxConcurrentGrading))
        {
            var opinionTasks = candidates.Select(async selected =>
            {
                await gate.WaitAsync(cancellationToken);
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    suiteQuestions.TryGetValue(selected.OrderIndex, out var expectedPoints);

                    using var scope = _scopeFactory.CreateScope();
                    var sDb = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    var sConfigService = scope.ServiceProvider.GetRequiredService<SystemAiConfigService>();

                    var answer = await sDb.BenchmarkRunAnswers
                        .FirstOrDefaultAsync(a => a.Id == selected.Id, cancellationToken);
                    if (answer == null) return;

                    await RunSecondOpinionAsync(
                        sDb, sConfigService, run, answer, expectedPoints, constants,
                        SecondOpinionTriggers.Sample, cancellationToken);
                    Interlocked.Increment(ref gradedThisPass);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Benchmark run {RunId} answer {OrderIndex}: sample top-up second opinion failed. The first verdict stands.",
                        run.Id, selected.OrderIndex);
                }
                finally
                {
                    gate.Release();
                }
            });

            await Task.WhenAll(opinionTasks);
        }

        // Those verdicts were written through other contexts, so this one's tracked copies of the
        // same rows are stale, and the stages that follow — the second claim-verification pass,
        // the synthesis and the finalizer -- all read this context.
        foreach (var answer in candidates)
        {
            await db.Entry(answer).ReloadAsync(CancellationToken.None);
        }

        run.SecondOpinionSampleCountUsed = alreadyGraded + gradedThisPass;
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

    /// <summary>
    /// Checks the claims the assessor could not adjudicate against the source and the wiki, using the
    /// same read-only tools the candidate had.
    ///
    /// It exists because the assessor grades with EnableToolUse = false while the candidate spends
    /// 25-45 tool calls on retrieval, so a true fact outside the rubric is unverifiable *by
    /// construction* — on the 2026-09-03 run that produced 13 such claims across 5 answers, all of
    /// them on the run's three lowest-scoring ones. BenchmarkRubricGapDetector was the only route to
    /// that signal and it needs two independent model families across several runs; one call with
    /// tools answers it for a single run.
    ///
    /// Writes advisory fields only. No level, quality score, speed score or index reads anything this
    /// produces, and a failure here never fails the run: an answer keeps exactly the verdict it has.
    /// </summary>
    internal async Task RunClaimVerificationAsync(
        ApplicationDbContext db,
        SystemAiConfigService configService,
        BenchmarkRun run,
        CancellationToken cancellationToken)
    {
        if (!run.ClaimVerifierModelConfigurationId.HasValue) return;

        var (verifierConfig, verifierApiKey, resolveError) = await ResolveAssessorAsync(
            db, run, run.ClaimVerifierModelConfigurationId.Value, cancellationToken);

        if (verifierConfig == null || verifierApiKey == null)
        {
            _logger.LogWarning(
                "Benchmark run {RunId}: claim verifier configuration {ConfigId} unusable ({Error}). Claim verification disabled for this run.",
                run.Id, run.ClaimVerifierModelConfigurationId.Value, resolveError);
            return;
        }

        var candidateAnswers = await db.BenchmarkRunAnswers
            .Where(a => a.BenchmarkRunId == run.Id &&
                        a.Status == BenchmarkAnswerStatus.Ok &&
                        a.ClaimVerificationJson == null &&
                        a.ClaimVerificationError == null)
            .OrderBy(a => a.OrderIndex)
            .ToListAsync(cancellationToken);

        candidateAnswers = candidateAnswers
            .Where(a => NeedsClaimVerification(a) ||
                        (((BenchmarkAnswerFlags)a.AnswerFlags) & BenchmarkAnswerFlags.ContestedVerdict) != 0 ||
                        (a.SecondOpinionCriticalError.HasValue && a.SecondOpinionCriticalError.Value != a.CriticalError))
            .ToList();

        if (candidateAnswers.Count == 0) return;

        _logger.LogInformation(
            "Benchmark run {RunId}: running claim verification for {Count} answer(s) with unverified or disputed claims using {Verifier}.",
            run.Id, candidateAnswers.Count, verifierConfig.DisplayName ?? verifierConfig.ModelId);

        var suiteQuestions = await db.BenchmarkQuestions
            .Where(q => q.BenchmarkSuiteId == run.BenchmarkSuiteId)
            .ToDictionaryAsync(q => q.OrderIndex, q => q.ExpectedPoints, cancellationToken);

        // Optional deterministic verifier token budget (analysis_v4.md § 8.2): cost is unbounded
        // by default because a run-level cap on claim *count* would still let one expensive answer
        // (run 14: ~103 k input tokens per answer) blow the budget alone. Default 0 = unlimited, so
        // every existing run and every operator who never sets the key behaves exactly as before.
        int tokenBudget = _configuration.GetValue<int>("Benchmark:ClaimVerificationInputTokenBudget", 0);
        long tokensSpent = 0;
        if (tokenBudget > 0)
        {
            // Seeded from what this run has already spent verifying claims (e.g. a prior call to
            // this method after a failure retry), not from zero, so the budget is a true run total
            // rather than a per-call allowance that could be circumvented by calling this twice.
            tokensSpent = await db.BenchmarkRunAnswers
                .Where(a => a.BenchmarkRunId == run.Id)
                .SumAsync(a => a.ClaimVerificationInputTokens ?? 0, cancellationToken);
        }

        foreach (var answer in candidateAnswers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (tokenBudget > 0 && tokensSpent >= tokenBudget)
            {
                answer.ClaimVerificationError = BenchmarkClaimVerificationNotCheckedReason(tokenBudget);
                _logger.LogInformation(
                    "Benchmark run {RunId} answer {OrderIndex}: claim verification input token budget ({Budget:N0}) exhausted after {Spent:N0} token(s); not checked.",
                    run.Id, answer.OrderIndex, tokenBudget, tokensSpent);
                await db.SaveChangesAsync(cancellationToken);
                continue;
            }

            suiteQuestions.TryGetValue(answer.OrderIndex, out var expectedPoints);

            await VerifyAnswerClaimsAsync(
                db, configService, run, answer, verifierConfig, verifierApiKey, expectedPoints, cancellationToken);

            if (tokenBudget > 0)
            {
                tokensSpent += answer.ClaimVerificationInputTokens ?? 0;
            }
        }
    }

    /// <summary>
    /// The sentinel written to <see cref="BenchmarkRunAnswer.ClaimVerificationError"/> when the
    /// deterministic verifier token budget is exhausted before an answer's claims were checked.
    ///
    /// This deliberately reuses the existing error field rather than adding a schema column: the
    /// distinguishing substring below lets <c>BenchmarkReportBuilder</c> (and a test) tell "not
    /// checked — budget" apart from "checked and the call failed" without a new
    /// <see cref="BenchmarkClaimVerdict"/> value or a new persisted count. Keep the two files in
    /// step if this text changes.
    /// </summary>
    /// <remarks>
    /// Invariant-formatted deliberately. This string is <b>persisted</b> and read back by the report
    /// builder, so a server whose culture groups digits differently would write rows the same build
    /// renders inconsistently — and a database would end up holding both shapes. Every figure the
    /// report builder emits goes through its own <c>Inv</c> helper for the same reason.
    /// </remarks>
    internal static string BenchmarkClaimVerificationNotCheckedReason(int tokenBudget) =>
        string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "{0} claim verification input token budget ({1:N0} tokens) exhausted for this run.",
            ClaimVerificationNotCheckedPrefix,
            tokenBudget);

    /// <summary>
    /// The prefix that marks a <see cref="BenchmarkRunAnswer.ClaimVerificationError"/> value as a
    /// budget skip rather than a real verifier failure. <c>BenchmarkReportBuilder</c> checks for it
    /// to keep the two apart in the report.
    /// </summary>
    internal const string ClaimVerificationNotCheckedPrefix = "NotChecked:";

    /// <summary>
    /// Checks one answer's unverified claims with the run's claim verifier and records the verdict.
    ///
    /// The in-flight mark is set here rather than in the core below so every caller — the per-answer
    /// path that follows each assessment and the run-level follow-up pass — reports the row as being
    /// re-read rather than finished, and the <c>finally</c> is what keeps a throw or a cancel from
    /// leaving it that way.
    /// </summary>
    internal async Task VerifyAnswerClaimsAsync(
        ApplicationDbContext db,
        SystemAiConfigService configService,
        BenchmarkRun run,
        BenchmarkRunAnswer answer,
        SystemAiApiConfiguration verifierConfig,
        string verifierApiKey,
        string? expectedPoints,
        CancellationToken cancellationToken)
    {
        _runManager.MarkVerificationInFlight(run.Id, answer.OrderIndex);
        try
        {
            await VerifyAnswerClaimsCoreAsync(
                db, configService, run, answer, verifierConfig, verifierApiKey, expectedPoints, cancellationToken);
        }
        finally
        {
            _runManager.ClearVerificationInFlight(run.Id, answer.OrderIndex);
        }
    }

    private async Task VerifyAnswerClaimsCoreAsync(
        ApplicationDbContext db,
        SystemAiConfigService configService,
        BenchmarkRun run,
        BenchmarkRunAnswer answer,
        SystemAiApiConfiguration verifierConfig,
        string verifierApiKey,
        string? expectedPoints,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(answer.ClaimVerificationJson) || !string.IsNullOrWhiteSpace(answer.ClaimVerificationError))
        {
            return;
        }

        List<string>? claims = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(answer.UnverifiedClaimsJson))
            {
                claims = JsonSerializer.Deserialize<List<string>>(answer.UnverifiedClaimsJson);
            }
        }
        catch (JsonException)
        {
            claims = null;
        }

        bool isDisputed = (((BenchmarkAnswerFlags)answer.AnswerFlags) & BenchmarkAnswerFlags.ContestedVerdict) != 0 ||
                          (answer.SecondOpinionCriticalError.HasValue && answer.SecondOpinionCriticalError.Value != answer.CriticalError);

        string? accuracyEvidence = null;
        if (!string.IsNullOrWhiteSpace(answer.AssessmentEvidenceJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(answer.AssessmentEvidenceJson);
                if (doc.RootElement.TryGetProperty("accuracy", out var accElem))
                {
                    accuracyEvidence = accElem.GetString();
                }
            }
            catch
            {
                // Ignore parse errors on advisory evidence json
            }
        }

        if ((claims == null || claims.Count == 0) && isDisputed)
        {
            claims = ExtractDisputedClaims(answer, accuracyEvidence);
            if (claims.Count > 0)
            {
                answer.UnverifiedClaimCount = claims.Count;
                answer.UnverifiedClaimsJson = JsonSerializer.Serialize(claims);
            }
        }

        // After the disputed branch has persisted whatever it collected, so the quote is carried in
        // the prompt only and never lands in the unadjudicable-claims columns.
        bool isCriticalErrorAdjudication = IsCriticalErrorAdjudication(answer);
        if (isCriticalErrorAdjudication)
        {
            claims = WithCriticalErrorQuoteFirst(claims, answer.CriticalErrorQuote);
        }

        // The out-of-rubric basis is carried the same way. Fixed order: the critical-error quote is
        // claim 0 and the basis claim 1; alone, the basis is claim 0. The verifier preamble names
        // the basis by that position.
        string? outOfRubricBasis = OutOfRubricBasisOf(answer);
        bool isOutOfRubricAdjudication = outOfRubricBasis != null;
        if (isOutOfRubricAdjudication)
        {
            claims = WithOutOfRubricBasis(claims, outOfRubricBasis, isCriticalErrorAdjudication ? 1 : 0);
        }

        if (claims == null || claims.Count == 0) return;

        if (expectedPoints == null)
        {
            expectedPoints = await db.BenchmarkQuestions
                .Where(q => q.BenchmarkSuiteId == run.BenchmarkSuiteId && q.OrderIndex == answer.OrderIndex)
                .Select(q => q.ExpectedPoints)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var allowedTools = _configuration.GetSection("Benchmark:AllowedTools").Get<List<string>>() ?? _defaultAllowedTools;
        int toolCallBudget = _configuration.GetValue<int>("Benchmark:ClaimVerification:ToolCallBudget", 15);
        int toolIterations = _configuration.GetValue<int>("Benchmark:ClaimVerification:ToolIterations", 8);
        int totalModelCalls = _configuration.GetValue<int>("Benchmark:ClaimVerification:TotalModelCalls", 12);
        int maxOutputTokens = _configuration.GetValue<int>("Benchmark:ClaimVerification:MaxOutputTokens", 16000);
        int timeoutSeconds = _configuration.GetValue<int>("Benchmark:ClaimVerification:TimeoutSeconds", 300);
        int maxResultLength = _configuration.GetValue<int>("Benchmark:MaxResultLength", 10000);

        string prompt = BenchmarkClaimVerificationPrompt.BuildPrompt(
            run.SuiteName,
            answer.OrderIndex,
            answer.QuestionText,
            expectedPoints,
            claims,
            allowedTools,
            toolCallBudget,
            isDisputedVerdict: isDisputed,
            isCriticalErrorAdjudication: isCriticalErrorAdjudication,
            isOutOfRubricAdjudication: isOutOfRubricAdjudication,
            assessorEvidence: accuracyEvidence);

        var runRequest = BuildClaimVerificationRequest(
            verifierConfig,
            verifierApiKey,
            prompt,
            allowedTools,
            maxOutputTokens,
            toolIterations,
            totalModelCalls,
            toolCallBudget,
            maxResultLength,
            run.Id,
            answer.OrderIndex,
            run.StartedByUserId);

        using var verifyCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        verifyCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        var runResult = new AgentRunResult();
        var sw = Stopwatch.StartNew();
        string? terminalError = null;

        try
        {
            await foreach (var evt in _agentLoopRunner.RunAsync(runRequest, runRequest.Budget, runResult, verifyCts.Token))
            {
                if (evt.Type == "error")
                {
                    terminalError = evt.Data?.ToString();
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && verifyCts.IsCancellationRequested)
        {
            terminalError = $"Claim verification timeout exceeded ({timeoutSeconds} s).";
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            terminalError = ex.Message;
        }
        int inputTokens;
        int outputTokens;
        if (!string.IsNullOrWhiteSpace(terminalError))
        {
            inputTokens = runResult.TotalPromptTokens;
            outputTokens = runResult.OutputTokens;
        }
        else
        {
            inputTokens = runResult.TotalPromptTokens > 0 ? runResult.TotalPromptTokens : runResult.EstimatedInputTokens;
            outputTokens = runResult.OutputTokens > 0 ? runResult.OutputTokens : runResult.EstimatedOutputTokens;
        }
        int cacheReadTokens = runResult.CacheReadTokens;
        int cacheCreationTokens = runResult.CacheCreationTokens;
        int toolCallsCount = runResult.ToolCalls.Count(tc => tc.Status == "completed");

        if (!string.IsNullOrWhiteSpace(terminalError))
        {
            sw.Stop();
            answer.ClaimVerificationInputTokens = inputTokens;
            answer.ClaimVerificationOutputTokens = outputTokens;
            answer.ClaimVerificationCacheReadTokens = cacheReadTokens;
            answer.ClaimVerificationCacheCreationTokens = cacheCreationTokens;
            answer.ClaimVerificationDurationMs = sw.ElapsedMilliseconds;
            answer.ClaimVerificationToolCallCount = toolCallsCount;
            answer.ClaimVerificationByModelDisplayNameUsed = verifierConfig.DisplayName ?? verifierConfig.ModelId;
            answer.ClaimVerificationError = BenchmarkAssessmentFailure.Truncate(terminalError, BenchmarkAssessmentFailure.MaxClaimVerificationErrorLength);
            answer.ClaimVerificationRawText = null;
            _logger.LogWarning(
                "Benchmark run {RunId} answer {OrderIndex}: claim verification failed ({Error}).",
                run.Id, answer.OrderIndex, terminalError);
        }
        else
        {
            var parseResult = BenchmarkClaimVerificationParser.Parse(runResult.FinalText, claims);

            bool retryEnabled = _configuration.GetValue<bool>("Benchmark:ClaimVerification:ParseRetryEnabled", true);
            if (!parseResult.Success && retryEnabled)
            {
                _logger.LogWarning(
                    "Benchmark run {RunId} answer {OrderIndex}: claim verification output failed JSON parsing. Retrying once...",
                    run.Id, answer.OrderIndex);
                runRequest.SeedHistory.Add(new { role = "assistant", content = runResult.FinalText ?? string.Empty });
                runRequest.SeedHistory.Add(new { role = "user", content = $"Your previous response was not valid JSON or could not be parsed: {parseResult.ErrorMessage}. Please output ONLY the raw JSON object according to the schema without any markdown wrapping, code fences, or extra text." });

                var retryResult = new AgentRunResult();
                try
                {
                    await foreach (var evt in _agentLoopRunner.RunAsync(runRequest, runRequest.Budget, retryResult, verifyCts.Token))
                    {
                        if (evt.Type == "error")
                        {
                            terminalError = evt.Data?.ToString();
                        }
                    }
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && verifyCts.IsCancellationRequested)
                {
                    terminalError = $"Claim verification timeout exceeded ({timeoutSeconds} s).";
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    terminalError = ex.Message;
                }

                int retryInputTokens = retryResult.TotalPromptTokens > 0 ? retryResult.TotalPromptTokens : retryResult.EstimatedInputTokens;
                int retryOutputTokens = retryResult.OutputTokens > 0 ? retryResult.OutputTokens : retryResult.EstimatedOutputTokens;
                inputTokens += retryInputTokens;
                outputTokens += retryOutputTokens;
                cacheReadTokens += retryResult.CacheReadTokens;
                cacheCreationTokens += retryResult.CacheCreationTokens;
                toolCallsCount += retryResult.ToolCalls.Count(tc => tc.Status == "completed");

                if (string.IsNullOrWhiteSpace(terminalError))
                {
                    parseResult = BenchmarkClaimVerificationParser.Parse(retryResult.FinalText, claims);
                    if (retryResult.TotalPromptTokens > 0)
                    {
                        runResult = retryResult;
                    }
                }
            }

            sw.Stop();
            answer.ClaimVerificationInputTokens = inputTokens;
            answer.ClaimVerificationOutputTokens = outputTokens;
            answer.ClaimVerificationCacheReadTokens = cacheReadTokens;
            answer.ClaimVerificationCacheCreationTokens = cacheCreationTokens;
            answer.ClaimVerificationDurationMs = sw.ElapsedMilliseconds;
            answer.ClaimVerificationToolCallCount = toolCallsCount;
            answer.ClaimVerificationByModelDisplayNameUsed = verifierConfig.DisplayName ?? verifierConfig.ModelId;

            if (!string.IsNullOrWhiteSpace(terminalError))
            {
                answer.ClaimVerificationError = BenchmarkAssessmentFailure.Truncate(terminalError, BenchmarkAssessmentFailure.MaxClaimVerificationErrorLength);
                answer.ClaimVerificationRawText = null;
                _logger.LogWarning(
                    "Benchmark run {RunId} answer {OrderIndex}: claim verification failed on retry ({Error}).",
                    run.Id, answer.OrderIndex, terminalError);
            }
            else if (!parseResult.Success)
            {
                answer.ClaimVerificationError = BenchmarkAssessmentFailure.Truncate(parseResult.ErrorMessage ?? "Claim verification parse failed.", BenchmarkAssessmentFailure.MaxClaimVerificationErrorLength);
                answer.ClaimVerificationRawText = BenchmarkAssessmentFailure.Truncate(parseResult.RawResponse, 8000);
                _logger.LogWarning(
                    "Benchmark run {RunId} answer {OrderIndex}: claim verification parse failed ({Error}).",
                    run.Id, answer.OrderIndex, parseResult.ErrorMessage);
            }
            else
            {
                answer.ClaimVerificationError = null;
                answer.ClaimVerificationRawText = null;

                // The out-of-rubric basis is the assessor's statement, not the answer's, so its
                // verdict stays out of the answer's claim counts and the RefutedClaim flag. It is
                // kept in ClaimVerificationJson, where its citation is the record.
                var answerClaimVerifications = WithoutOutOfRubricBasis(parseResult.Verifications, outOfRubricBasis);
                answer.ClaimsSupportedCount = answerClaimVerifications.Count(v => v.Verdict == BenchmarkClaimVerdict.Supported);
                answer.ClaimsRefutedCount = answerClaimVerifications.Count(v => v.Verdict == BenchmarkClaimVerdict.Refuted);
                answer.ClaimsIndeterminateCount = answerClaimVerifications.Count(v => v.Verdict == BenchmarkClaimVerdict.Indeterminate);
                answer.ClaimVerificationJson = JsonSerializer.Serialize(parseResult.Verifications);

                if (answer.ClaimsRefutedCount > 0)
                {
                    answer.AnswerFlags |= (int)BenchmarkAnswerFlags.RefutedClaim;
                }
                else
                {
                    answer.AnswerFlags &= ~(int)BenchmarkAnswerFlags.RefutedClaim;
                }

                // The quote the critical error rests on was checked against the source and stood.
                // Advisory: the cap stays, the index does not move, and the flag says the finding
                // is contested so a reader argues it from the record rather than from the verdict.
                // Cleared in the negative case so a re-verification cannot leave a stale flag.
                if (isCriticalErrorAdjudication &&
                    CriticalErrorQuoteWasSupported(parseResult.Verifications, answer.CriticalErrorQuote))
                {
                    answer.AnswerFlags |= (int)BenchmarkAnswerFlags.ContestedCriticalError;
                }
                else
                {
                    answer.AnswerFlags &= ~(int)BenchmarkAnswerFlags.ContestedCriticalError;
                }

                // The statement the out-of-rubric Accuracy deduction rests on was checked against
                // the source and refuted. Advisory in the same way: the deduction stays, no index
                // moves, and the flag says the deduction is contested. Cleared on Supported or
                // Indeterminate, so a re-verification cannot leave a stale flag.
                if (isOutOfRubricAdjudication &&
                    OutOfRubricBasisWasRefuted(parseResult.Verifications, outOfRubricBasis))
                {
                    answer.AnswerFlags |= (int)BenchmarkAnswerFlags.ContestedAccuracyDeduction;
                }
                else
                {
                    answer.AnswerFlags &= ~(int)BenchmarkAnswerFlags.ContestedAccuracyDeduction;
                }
            }
        }

        await db.SaveChangesAsync(CancellationToken.None);

        try
        {
            await configService.RecordUsageAsync(
                verifierConfig.Id,
                run.StartedByUserId,
                inputTokens,
                outputTokens,
                roleContext: 4,
                cacheReadTokens: runResult.CacheReadTokens,
                cacheCreationTokens: runResult.CacheCreationTokens,
                totalDurationMs: (int)sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record usage for claim verification call.");
        }
    }

    internal static AgentRunRequest BuildClaimVerificationRequest(
        SystemAiApiConfiguration verifierConfig,
        string verifierApiKey,
        string prompt,
        List<string> allowedTools,
        int maxOutputTokens,
        int toolIterations,
        int totalModelCalls,
        int toolCallBudget,
        int maxResultLength,
        long runId,
        int orderIndex,
        string? startedByUserId)
    {
        return new AgentRunRequest
        {
            ProviderName = verifierConfig.Provider,
            ModelId = verifierConfig.ModelId,
            ApiKey = verifierApiKey,
            ModelDisplayName = verifierConfig.DisplayName,
            SystemPrompt = "You verify individual factual claims about GnollHack against the game's own source code and wiki. Strictly adhere to the requested JSON response format.",
            ThinkingLevel = verifierConfig.ThinkingLevel,
            ReasoningMode = verifierConfig.ReasoningMode,
            ReasoningSummary = verifierConfig.ReasoningSummary,
            ServiceTier = verifierConfig.ServiceTier,
            MaxOutputTokens = maxOutputTokens,
            MaxToolIterations = toolIterations,
            EnableToolUse = true,
            EnableWebSearch = false,
            EnableSubAgents = false,
            AllowedTools = allowedTools,
            SystemModelId = verifierConfig.Id,
            Budget = new AgentRunBudget { MaxTotalModelCalls = totalModelCalls },
            ToolExecutionContext = new Tools.ToolExecutionContext
            {
                SessionId = Overseer.Services.Privacy.SessionRef.Persistent(runId),
                ToolBudgetScopeId = $"bench_{runId}_verify_q{orderIndex}",
                UserId = startedByUserId ?? string.Empty,
                MaxResultLength = maxResultLength,
                MaxCallsPerSession = toolCallBudget,
                ShowDebugLog = false
            },
            SeedHistory = new List<object>
            {
                new { role = "user", content = prompt }
            }
        };
    }

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
                SessionId = Overseer.Services.Privacy.SessionRef.Persistent(run.Id),
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
                SessionId = Overseer.Services.Privacy.SessionRef.Persistent(suite.Id),
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
        // Pooled into the primary assessor's fields regardless of the trial model; BenchmarkAssessorCalibration carries this call's own record separately.
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
        public const string RefutedClaim = "RefutedClaim";
        public const string ContestedVerdict = "ContestedVerdict";
        public const string OutOfRubricAccuracy = "OutOfRubricAccuracy";
        public const string UnevidencedDeduction = "UnevidencedDeduction";
        public const string OmissionAsAccuracy = "OmissionAsAccuracy";
        public const string UnverifiedClaims = "UnverifiedClaims";
        public const string BelowThreshold = "BelowThreshold";
        public const string Outlier = "Outlier";
        public const string All = "All";
        public const string Manual = "Manual";
        public const string Sample = "Sample";
    }

    /// <summary>
    /// Grades one answer with the run's second-opinion assessor and records the verdict. Split
    /// from the trigger logic so the post-scoring outlier sweep can reuse it without
    /// re-evaluating triggers it has already decided.
    ///
    /// The in-flight mark is set here rather than in the core below so every caller — the
    /// per-answer trigger, the outlier sweep and the sample top-up — reports the answer as being
    /// re-graded, and the <c>finally</c> is what keeps a throw from leaving it that way.
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
        _runManager.MarkSecondOpinionInFlight(run.Id, answer.OrderIndex);
        try
        {
            await RunSecondOpinionCoreAsync(
                db, configService, run, answer, expectedPoints, constants, trigger, cancellationToken);
        }
        finally
        {
            _runManager.ClearSecondOpinionInFlight(run.Id, answer.OrderIndex);
        }
    }

    private async Task RunSecondOpinionCoreAsync(
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

        bool blind = run.SecondOpinionBlindUsed || (run.Id == 0 && constants.SecondOpinionBlind);

        List<BenchmarkClaimVerification>? claimVerifications = null;
        if (!string.IsNullOrWhiteSpace(answer.ClaimVerificationJson))
        {
            try
            {
                claimVerifications = JsonSerializer.Deserialize<List<BenchmarkClaimVerification>>(
                    answer.ClaimVerificationJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (JsonException)
            {
                claimVerifications = null;
            }
        }

        // The out-of-rubric basis is the first assessor's own statement, not a claim of the answer,
        // and this context is presented to the second reader as claims from the candidate answer.
        if (claimVerifications != null)
        {
            claimVerifications = WithoutOutOfRubricBasis(claimVerifications, OutOfRubricBasisOf(answer));
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
            boardText: run.BenchmarkSuite?.GameSnapshot?.SanitizedText,
            blind: blind,
            triggerLabel: trigger,
            claimVerifications: claimVerifications);

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
                SessionId = Overseer.Services.Privacy.SessionRef.Persistent(run.Id),
                UserId = run.StartedByUserId ?? string.Empty,
                ShowDebugLog = false
            },
            SeedHistory = new List<object>
            {
                new { role = "user", content = prompt }
            }
        };

        int timeoutSeconds = _configuration.GetValue<int>("Benchmark:SecondOpinion:TimeoutSeconds", 900);
        bool retryEnabled = _configuration.GetValue<bool>("Benchmark:SecondOpinion:ParseRetryEnabled", true);

        using var opinionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        opinionCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        async Task<string?> RunOpinionTurnAsync(AgentRunResult result)
        {
            string? error = null;
            try
            {
                await foreach (var evt in _agentLoopRunner.RunAsync(runRequest, runRequest.Budget, result, opinionCts.Token))
                {
                    if (evt.Type == "error") error = evt.Data?.ToString();
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && opinionCts.IsCancellationRequested)
            {
                error = $"Second opinion timeout exceeded ({timeoutSeconds} s).";
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { error = ex.Message; }
            return error;
        }

        var runResult = new AgentRunResult();
        var sw = Stopwatch.StartNew();
        string? terminalError = await RunOpinionTurnAsync(runResult);

        int opinionInputTokens = runResult.TotalPromptTokens > 0 ? runResult.TotalPromptTokens : runResult.EstimatedInputTokens;
        int opinionOutputTokens = runResult.OutputTokens > 0 ? runResult.OutputTokens : runResult.EstimatedOutputTokens;
        int opinionCacheReadTokens = runResult.CacheReadTokens;
        int opinionCacheCreationTokens = runResult.CacheCreationTokens;
        string? lastFinalText = runResult.FinalText;

        var parseResult = string.IsNullOrWhiteSpace(terminalError)
            ? BenchmarkAssessmentParser.ParsePerQuestion(runResult.FinalText, answer.AnswerText)
            : new PerQuestionAssessmentParseResult { Success = false, ErrorMessage = terminalError };

        if ((!parseResult.Success || parseResult.Result == null) && string.IsNullOrWhiteSpace(terminalError) && retryEnabled)
        {
            _logger.LogWarning(
                "Benchmark run {RunId} answer {OrderIndex}: second opinion output failed JSON parsing. Retrying once...",
                run.Id, answer.OrderIndex);
            runRequest.SeedHistory.Add(new { role = "assistant", content = runResult.FinalText ?? string.Empty });
            runRequest.SeedHistory.Add(new { role = "user", content = $"Your previous response was not valid JSON or could not be parsed: {parseResult.ErrorMessage}. Please output ONLY the raw JSON object according to the schema without any markdown wrapping, code fences, or extra text." });

            var retryResult = new AgentRunResult();
            terminalError = await RunOpinionTurnAsync(retryResult);

            opinionInputTokens += retryResult.TotalPromptTokens > 0 ? retryResult.TotalPromptTokens : retryResult.EstimatedInputTokens;
            opinionOutputTokens += retryResult.OutputTokens > 0 ? retryResult.OutputTokens : retryResult.EstimatedOutputTokens;
            opinionCacheReadTokens += retryResult.CacheReadTokens;
            opinionCacheCreationTokens += retryResult.CacheCreationTokens;
            if (!string.IsNullOrWhiteSpace(retryResult.FinalText))
            {
                lastFinalText = retryResult.FinalText;
            }

            parseResult = string.IsNullOrWhiteSpace(terminalError)
                ? BenchmarkAssessmentParser.ParsePerQuestion(retryResult.FinalText, answer.AnswerText)
                : new PerQuestionAssessmentParseResult { Success = false, ErrorMessage = terminalError };
        }
        sw.Stop();

        // Second-opinion-side cost either way, so it is recorded even when the verdict is unusable.
        answer.SecondOpinionInputTokens = (answer.SecondOpinionInputTokens ?? 0) + opinionInputTokens;
        answer.SecondOpinionOutputTokens = (answer.SecondOpinionOutputTokens ?? 0) + opinionOutputTokens;
        answer.SecondOpinionCacheReadTokens = (answer.SecondOpinionCacheReadTokens ?? 0) + opinionCacheReadTokens;
        answer.SecondOpinionCacheCreationTokens = (answer.SecondOpinionCacheCreationTokens ?? 0) + opinionCacheCreationTokens;
        answer.SecondOpinionDurationMs = (answer.SecondOpinionDurationMs ?? 0) + sw.ElapsedMilliseconds;

        if (!parseResult.Success || parseResult.Result == null)
        {
            string? failure = parseResult.ErrorMessage ?? terminalError;
            answer.SecondOpinionError = BenchmarkAssessmentFailure.Truncate(AppendSecondOpinionRawHead(failure, lastFinalText));
            _logger.LogWarning(
                "Benchmark run {RunId} answer {OrderIndex}: second opinion unavailable ({Error}). The first verdict stands.",
                run.Id, answer.OrderIndex, failure);
            await db.SaveChangesAsync(CancellationToken.None);
            return;
        }

        answer.SecondOpinionError = null;
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
                opinionInputTokens,
                opinionOutputTokens,
                roleContext: 4,
                cacheReadTokens: opinionCacheReadTokens,
                cacheCreationTokens: opinionCacheCreationTokens,
                totalDurationMs: (int)sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record usage for second-opinion assessor call.");
        }
    }

    /// <summary>
    /// Characters of the model's raw text kept in <c>SecondOpinionError</c> after an unusable verdict.
    /// </summary>
    internal const int SecondOpinionRawHeadChars = 600;

    /// <summary>
    /// Appends the head of the model's raw text, newlines collapsed, to a second-opinion failure
    /// message. The message is shortened first when needed, so the head survives the column's
    /// <see cref="BenchmarkAssessmentFailure.MaxErrorLength"/> cap.
    /// </summary>
    internal static string? AppendSecondOpinionRawHead(string? error, string? rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return error;
        }

        string collapsed = string.Join(" ", rawText.Split(
            new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        string head = collapsed.Length > SecondOpinionRawHeadChars ? collapsed.Substring(0, SecondOpinionRawHeadChars) : collapsed;
        string suffix = " | raw: " + head;

        string prefix = error ?? string.Empty;
        int room = BenchmarkAssessmentFailure.MaxErrorLength - suffix.Length;
        if (prefix.Length > room)
        {
            prefix = prefix.Substring(0, Math.Max(0, room));
        }

        return prefix + suffix;
    }

    /// <summary>
    /// Quality-score gap above which two verdicts are treated as disagreeing. Internal rather
    /// than private because the report prints the definition beside the rate it computes, and a
    /// second copy of the number would eventually disagree with this one.
    /// </summary>
    internal const int SecondOpinionDisagreementPoints = 15;

    /// <summary>
    /// Highest accuracy level that triggers a second opinion when unverified claims are present.
    /// Scoring method v6 forbids docking accuracy for unverified claims, so an accuracy level at
    /// or below this alongside unverified claims suggests the deduction rested on the out-of-rubric
    /// claim. Internal so the report can describe the threshold without duplicating a literal.
    /// </summary>
    internal const int UnverifiedClaimsAccuracyMaxLevel = 3;

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

        var summaries = answers.Select(a =>
        {
            string? outOfRubricBasis = OutOfRubricBasisOf(a);
            var refutedList = new List<(string Claim, string? Citation, string? Basis)>();
            if (!string.IsNullOrWhiteSpace(a.ClaimVerificationJson))
            {
                try
                {
                    var verifications = JsonSerializer.Deserialize<List<BenchmarkClaimVerification>>(
                        a.ClaimVerificationJson,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (verifications != null)
                    {
                        foreach (var v in WithoutOutOfRubricBasis(verifications, outOfRubricBasis)
                                     .Where(x => x.Verdict == BenchmarkClaimVerdict.Refuted))
                        {
                            refutedList.Add((v.Claim, v.Citation, v.Basis));
                        }
                    }
                }
                catch (JsonException)
                {
                    // Ignore malformed JSON in advisory/synthesis path
                }
            }

            return new BenchmarkPerQuestionVerdictSummary
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
                ClaimsSupportedCount = a.ClaimsSupportedCount,
                ClaimsRefutedCount = a.ClaimsRefutedCount,
                ClaimsIndeterminateCount = a.ClaimsIndeterminateCount,
                RefutedClaims = refutedList,
                ContestedCriticalErrorQuotes =
                    (((BenchmarkAnswerFlags)a.AnswerFlags) & BenchmarkAnswerFlags.ContestedCriticalError) != 0
                     && !string.IsNullOrWhiteSpace(a.CriticalErrorQuote)
                        ? new[] { a.CriticalErrorQuote!.Trim() }
                        : Array.Empty<string>(),
                ContestedAccuracyDeductionBases =
                    (((BenchmarkAnswerFlags)a.AnswerFlags) & BenchmarkAnswerFlags.ContestedAccuracyDeduction) != 0
                     && outOfRubricBasis != null
                        ? new[] { outOfRubricBasis }
                        : Array.Empty<string>(),
                SecondOpinionQualityScore = a.SecondOpinionQualityScore,
                SecondOpinionCriticalError = a.SecondOpinionCriticalError,
                ReviewComment = a.ReviewComment,
                Status = a.Status
            };
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
                SessionId = Overseer.Services.Privacy.SessionRef.Persistent(run.Id),
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

        // Accumulated across the retry below, so a synthesis that needed a second attempt reports
        // what it actually consumed rather than only the surviving attempt's usage.
        int synthesisInputTokens = runResult.TotalPromptTokens > 0 ? runResult.TotalPromptTokens : runResult.EstimatedInputTokens;
        int synthesisOutputTokens = runResult.OutputTokens > 0 ? runResult.OutputTokens : runResult.EstimatedOutputTokens;
        int synthesisCacheReadTokens = runResult.CacheReadTokens;
        int synthesisCacheCreationTokens = runResult.CacheCreationTokens;

        if (!parseResult.Success)
        {
            _logger.LogWarning("Assessor synthesis output failed JSON parsing. Retrying once...");
            runRequest.SeedHistory.Add(new { role = "assistant", content = runResult.FinalText ?? string.Empty });
            runRequest.SeedHistory.Add(new { role = "user", content = $"Your previous response was not valid JSON or could not be parsed: {parseResult.ErrorMessage}. Please output ONLY the raw JSON object according to the schema without any markdown wrapping or extra text." });

            var retryResult = new AgentRunResult();
            await foreach (var _ in _agentLoopRunner.RunAsync(runRequest, runRequest.Budget, retryResult, cancellationToken)) { }
            parseResult = BenchmarkAssessmentParser.ParseFinalSynthesis(retryResult.FinalText);
            synthesisInputTokens += retryResult.TotalPromptTokens > 0 ? retryResult.TotalPromptTokens : retryResult.EstimatedInputTokens;
            synthesisOutputTokens += retryResult.OutputTokens > 0 ? retryResult.OutputTokens : retryResult.EstimatedOutputTokens;
            synthesisCacheReadTokens += retryResult.CacheReadTokens;
            synthesisCacheCreationTokens += retryResult.CacheCreationTokens;
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

        // Assigned, not accumulated: a rerun-synthesis replaces the prior attempt's figure rather
        // than doubling it. Not summed in BenchmarkRunFinalizer.ApplyTotals — there is no per-answer
        // synthesis row to sum from.
        run.TotalSynthesisInputTokens = synthesisInputTokens;
        run.TotalSynthesisOutputTokens = synthesisOutputTokens;
        run.TotalSynthesisCacheReadTokens = synthesisCacheReadTokens;
        run.TotalSynthesisCacheCreationTokens = synthesisCacheCreationTokens;
        run.TotalSynthesisDurationMs = sw.ElapsedMilliseconds;

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

        // Re-scoring recomputes QualityIndex and SpeedIndex from whatever answers exist. On a run that
        // stopped early that is an index over a fraction of the suite, stored in the same column a
        // complete run uses, with nothing to mark the difference.
        if (run.Status is BenchmarkRunStatus.Canceled or BenchmarkRunStatus.Failed)
        {
            return (false, AbortedRunRefusal);
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
            .Where(a => BenchmarkRunFinalizer.CountsTowardQualityIndex(a) && a.QualityScore.HasValue)
            .Select(a => (a.QualityScore, a.AssessedDifficulty ?? BenchmarkRunFinalizer.FallbackDifficulty(a.Difficulty)))
            .ToList();

        run.QualityIndex = BenchmarkScoring.QualityIndex(scorableItems);
        run.QualityIndexStandardError = BenchmarkScoring.QualityIndexStandardError(scorableItems);
        run.UnweightedQualityIndex = BenchmarkScoring.UnweightedQualityMean(
            run.Answers.Where(BenchmarkRunFinalizer.CountsTowardQualityIndex).Select(a => a.QualityScore));

        // The speed filter keeps Status == Ok: a null SpeedScore excludes an unanswered answer
        // anyway, and saying so explicitly is clearer than relying on it.
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
            int maxToolCallsPerQuestion = ResolveToolCallBudget();

            bool suiteHasBoard = run.BenchmarkSuite?.GameSnapshot != null;
            var promptOptions = !string.IsNullOrWhiteSpace(run.CandidatePromptOptionsJson)
                ? BenchmarkCandidatePromptOptions.FromJson(run.CandidatePromptOptionsJson)
                : new BenchmarkCandidatePromptOptions { HasGameSnapshot = suiteHasBoard };
            string systemPrompt = promptOptions.BuildSystemPrompt(_chatService, testedConfig.ParallelExecutionMode);
            var segmentedPrompt = BuildCandidateSegmentedPrompt(promptOptions, testedConfig.ParallelExecutionMode);
            PopulateInstrumentFingerprint(run, systemPrompt);

            string? expectedPoints = MatchSuiteQuestion(run, answer)?.ExpectedPoints;

            await ReExecuteSingleAnswerAsync(
                db, configService, run, answer, testedConfig, testedApiKey,
                systemPrompt, segmentedPrompt, allowedTools,
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
            await ApplyAbortedTotalsAsync(db, run, null);
            run.CompletedAtUtc ??= DateTime.UtcNow;
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
            await ApplyAbortedTotalsAsync(db, run, null);
            run.CompletedAtUtc ??= DateTime.UtcNow;
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
            await ApplyAbortedTotalsAsync(db, run, null);
            run.CompletedAtUtc ??= DateTime.UtcNow;
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
                    await ApplyAbortedTotalsAsync(db, run, null);
                    run.CompletedAtUtc ??= DateTime.UtcNow;
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
            await ApplyAbortedTotalsAsync(db, run, null);
            run.CompletedAtUtc ??= DateTime.UtcNow;
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

    public async Task RetryFailedClaimVerificationAsync(
        long runId,
        long? verifierConfigId = null,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var configService = scope.ServiceProvider.GetRequiredService<SystemAiConfigService>();

        var run = await db.BenchmarkRuns
            .Include(r => r.Answers)
            .FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);

        if (run == null)
        {
            _logger.LogWarning("Run {RunId} not found for retry failed claim verification.", runId);
            _runManager.Complete(runId);
            return;
        }

        try
        {
            if (verifierConfigId.HasValue)
            {
                run.ClaimVerifierModelConfigurationId = verifierConfigId.Value;
            }

            var failedAnswers = run.Answers
                .Where(a => !string.IsNullOrWhiteSpace(a.ClaimVerificationError))
                .ToList();

            if (failedAnswers.Count == 0)
            {
                _logger.LogInformation("Run {RunId} has no failed claim verifications to retry.", runId);
                return;
            }

            foreach (var answer in failedAnswers)
            {
                answer.ClaimVerificationError = null;
                answer.ClaimVerificationRawText = null;
            }
            await db.SaveChangesAsync(cancellationToken);

            await RunClaimVerificationAsync(db, configService, run, cancellationToken);

            var allAnswers = await db.BenchmarkRunAnswers
                .Where(a => a.BenchmarkRunId == run.Id)
                .ToListAsync(CancellationToken.None);
            BenchmarkRunFinalizer.Apply(run, allAnswers);
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Claim verification retry canceled for run {RunId}.", runId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Retry failed claim verification failed for run {RunId}.", runId);
        }
        finally
        {
            _runManager.Complete(run.Id);
        }
    }

    /// <summary>
    /// The candidate's system prompt in the three cache segments the providers key their prompt
    /// caches on, or null when <c>PromptCacheSettings:EnableSegmentedPrompt</c> is off. The three
    /// concatenate to the flat string <see cref="PopulateInstrumentFingerprint"/> hashes, so
    /// carrying them changes what the request looks like on the wire and not what the run records.
    /// </summary>
    private SegmentedPrompt? BuildCandidateSegmentedPrompt(
        BenchmarkCandidatePromptOptions promptOptions, MobileGnollHackLogger.Data.ParallelExecutionMode parallelMode)
    {
        if (!_configuration.GetValue<bool>("PromptCacheSettings:EnableSegmentedPrompt", true))
        {
            return null;
        }

        var (frozen, session, volatileSuffix) =
            promptOptions.BuildSegmentedSystemPrompt(_chatService, parallelMode);
        return new SegmentedPrompt(frozen, session, volatileSuffix);
    }

    internal void PopulateInstrumentFingerprint(BenchmarkRun run, string systemPrompt)
    {
        using var sha256 = SHA256.Create();
        byte[] promptBytes = Encoding.UTF8.GetBytes(systemPrompt);
        byte[] promptHash = sha256.ComputeHash(promptBytes);
        run.CandidateSystemPromptSha256 = Convert.ToHexString(promptHash).ToLowerInvariant();

        bool storeText = _configuration.GetValue<bool>("Benchmark:StoreSystemPromptText", true);
        if (storeText)
        {
            run.CandidateSystemPromptText = systemPrompt;
        }

        run.ToolGuidesSha256 = ComputeToolGuidesSha256();

        var kbPath = _configuration["KbPath"];
        if (!string.IsNullOrWhiteSpace(kbPath))
        {
            run.KnowledgeBaseHeadSha = GitHelper.GetGitHeadSha(kbPath);
        }

        var wikiPath = _configuration["WikiPath"];
        if (!string.IsNullOrWhiteSpace(wikiPath))
        {
            run.WikiHeadSha = GitHelper.GetGitHeadSha(wikiPath);
        }

        var sourceCodePath = _configuration["SourceCodePath"];
        if (!string.IsNullOrWhiteSpace(sourceCodePath))
        {
            run.SourceCodeHeadSha = GitHelper.GetGitHeadSha(sourceCodePath);
        }
    }

    /// <summary>
    /// The instrument a failed-question re-run executed under, written to the two <c>Rerun*</c>
    /// columns. Only the two fingerprints that a code or guide change can move are recorded: the
    /// three corpus heads belong to the corpora, which a re-run reads exactly as the original run
    /// did, and duplicating them would invite a reader to compare a run against itself.
    /// </summary>
    internal void PopulateRerunInstrumentFingerprint(BenchmarkRun run, string systemPrompt)
    {
        using var sha256 = SHA256.Create();
        byte[] promptHash = sha256.ComputeHash(Encoding.UTF8.GetBytes(systemPrompt));
        run.RerunCandidateSystemPromptSha256 = Convert.ToHexString(promptHash).ToLowerInvariant();
        run.RerunToolGuidesSha256 = ComputeToolGuidesSha256();
    }

    internal static string? ComputeToolGuidesSha256()
    {
        try
        {
            string guidesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ToolGuides");
            if (!Directory.Exists(guidesPath))
            {
                return null;
            }

            var files = Directory.GetFiles(guidesPath, "*", SearchOption.AllDirectories);
            if (files.Length == 0)
            {
                return null;
            }

            using var sha256 = SHA256.Create();
            var entries = new List<(string RelPath, string FileHash)>();

            foreach (var file in files)
            {
                string relPath = Path.GetRelativePath(guidesPath, file).Replace('\\', '/');
                byte[] bytes = File.ReadAllBytes(file);
                byte[] hash = sha256.ComputeHash(bytes);
                entries.Add((relPath, Convert.ToHexString(hash).ToLowerInvariant()));
            }

            entries.Sort((a, b) => string.Compare(a.RelPath, b.RelPath, StringComparison.Ordinal));

            var sb = new StringBuilder();
            foreach (var (relPath, fileHash) in entries)
            {
                sb.Append(relPath).Append(':').Append(fileHash).Append('\n');
            }

            byte[] manifestBytes = Encoding.UTF8.GetBytes(sb.ToString());
            byte[] manifestHash = sha256.ComputeHash(manifestBytes);
            return Convert.ToHexString(manifestHash).ToLowerInvariant();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// The five instrument hashes as they would be stamped on a run launched <b>right now</b> from
    /// this request, without launching one.
    ///
    /// <para>This is the resume guard's other half. A series records member 1's fingerprint; before
    /// continuing it, the orchestrator recomputes the same five values here and refuses when any has
    /// moved. A replicate set whose members straddle a deployment is not a replicate set, and nothing
    /// downstream would notice — every statistic would still compute, confidently, over runs that
    /// answered under different instruments.</para>
    ///
    /// <para>It deliberately reuses <see cref="PopulateInstrumentFingerprint"/> against a throwaway
    /// <see cref="BenchmarkRun"/> rather than recomputing the five values independently: a guard that
    /// derived its hashes differently from the code that stamps them would compare two things that
    /// were never the same measurement.</para>
    /// </summary>
    /// <returns>Null when the suite or the tested configuration no longer exists.</returns>
    internal async Task<BenchmarkInstrumentFingerprint?> ComputeCurrentInstrumentFingerprintAsync(
        ApplicationDbContext db,
        long suiteId,
        long testedModelConfigurationId,
        bool verboseMode,
        CancellationToken ct = default)
    {
        var suite = await db.BenchmarkSuites
            .Include(s => s.GameSnapshot)
            .FirstOrDefaultAsync(s => s.Id == suiteId, ct);
        if (suite == null) return null;

        var testedConfig = await db.SystemAiApiConfigurations
            .FirstOrDefaultAsync(c => c.Id == testedModelConfigurationId, ct);
        if (testedConfig == null) return null;

        var promptOptions = new BenchmarkCandidatePromptOptions
        {
            VerboseMode = verboseMode,
            HasGameSnapshot = suite.GameSnapshot != null
        };

        string systemPrompt = promptOptions.BuildSystemPrompt(_chatService, testedConfig.ParallelExecutionMode);

        var probe = new BenchmarkRun();
        PopulateInstrumentFingerprint(probe, systemPrompt);

        return new BenchmarkInstrumentFingerprint(
            probe.CandidateSystemPromptSha256,
            probe.ToolGuidesSha256,
            probe.KnowledgeBaseHeadSha,
            probe.WikiHeadSha,
            probe.SourceCodeHeadSha);
    }

    /// <summary>
    /// The answer carries a critical error the verifier can actually check: the flag is set and the
    /// assessor supplied the offending sentence verbatim. Without a quote there is nothing to look
    /// up, so such an answer is not dispatched on this ground.
    /// </summary>
    internal static bool IsCriticalErrorAdjudication(BenchmarkRunAnswer answer)
        => answer.CriticalError && !string.IsNullOrWhiteSpace(answer.CriticalErrorQuote);

    /// <summary>
    /// The answer carries an out-of-rubric Accuracy deduction whose basis the verifier can check:
    /// the flag is set and the stored accuracy evidence yields a statement after the marker.
    /// </summary>
    internal static bool IsOutOfRubricAdjudication(BenchmarkRunAnswer answer)
        => OutOfRubricBasisOf(answer) != null;

    /// <summary>
    /// Whether the claim verifier has something to check on this answer: a claim the assessor could
    /// not adjudicate, a critical-error quote, or the basis of an out-of-rubric Accuracy deduction.
    /// The three are independent and an answer may carry any of them.
    /// </summary>
    internal static bool NeedsClaimVerification(BenchmarkRunAnswer answer)
        => (answer.UnverifiedClaimCount ?? 0) > 0 || IsCriticalErrorAdjudication(answer) || IsOutOfRubricAdjudication(answer);

    /// <summary>
    /// The out-of-rubric marker as <see cref="BenchmarkAssessmentParser"/> matches it when it sets
    /// <see cref="BenchmarkAnswerFlags.OutOfRubricAccuracyDeduction"/>: tolerant of "the", casing
    /// and spacing, with the colon required. See
    /// <see cref="BenchmarkAssessmentParser.OutOfRubricAccuracyMarker"/>.
    /// </summary>
    private static readonly Regex OutOfRubricBasisMarkerRegex = new(
        @"\bnot\s+in\s+(?:the\s+)?rubric\s*:",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>A full stop, question or exclamation mark followed by whitespace or the end, or a line break.</summary>
    private static readonly Regex BasisSentenceEndRegex = new(@"[.!?](?=\s|$)|[\r\n]", RegexOptions.Compiled);

    internal const int OutOfRubricBasisMaxLength = 600;

    /// <summary>
    /// The assessor's own-knowledge statement behind an out-of-rubric Accuracy deduction: the text
    /// after the first marker up to the end of that sentence, trimmed and capped at
    /// <see cref="OutOfRubricBasisMaxLength"/> characters. Null when the evidence carries no marker
    /// or nothing follows it. The basis is free prose, so only the marker's own sentence is taken; a
    /// statement the verifier cannot check comes back Indeterminate, which sets nothing.
    /// </summary>
    internal static string? ExtractOutOfRubricBasis(string? accuracyEvidence)
    {
        if (string.IsNullOrWhiteSpace(accuracyEvidence)) return null;

        var marker = OutOfRubricBasisMarkerRegex.Match(accuracyEvidence);
        if (!marker.Success) return null;

        string rest = accuracyEvidence.Substring(marker.Index + marker.Length).TrimStart();
        var end = BasisSentenceEndRegex.Match(rest);
        string sentence = end.Success
            ? rest.Substring(0, rest[end.Index] is '\r' or '\n' ? end.Index : end.Index + 1)
            : rest;
        sentence = sentence.Trim();

        if (sentence.Length > OutOfRubricBasisMaxLength)
        {
            sentence = sentence.Substring(0, OutOfRubricBasisMaxLength).TrimEnd();
        }

        return sentence.Length > 0 ? sentence : null;
    }

    /// <summary>
    /// The basis this answer's out-of-rubric Accuracy deduction is adjudicated on, read from its
    /// stored accuracy evidence; null when the answer does not carry
    /// <see cref="BenchmarkAnswerFlags.OutOfRubricAccuracyDeduction"/> or no basis can be extracted.
    /// Deterministic, so every reader of <see cref="BenchmarkRunAnswer.ClaimVerificationJson"/>
    /// recovers the same text the verifier was given.
    /// </summary>
    internal static string? OutOfRubricBasisOf(BenchmarkRunAnswer answer)
        => (((BenchmarkAnswerFlags)answer.AnswerFlags) & BenchmarkAnswerFlags.OutOfRubricAccuracyDeduction) != 0
            ? ExtractOutOfRubricBasis(ReadEvidence(answer.AssessmentEvidenceJson, "accuracy"))
            : null;

    /// <summary>
    /// The claim list the verifier is given for an out-of-rubric adjudication: the trimmed basis at
    /// <paramref name="position"/> (clamped to the list), submitted once. Like the critical-error
    /// quote it is carried in the prompt only and never written to
    /// <see cref="BenchmarkRunAnswer.UnverifiedClaimsJson"/> or
    /// <see cref="BenchmarkRunAnswer.UnverifiedClaimCount"/>: it is the assessor's statement, not a
    /// claim of the answer.
    /// </summary>
    internal static List<string> WithOutOfRubricBasis(IReadOnlyList<string>? claims, string? basis, int position)
    {
        var result = claims != null ? new List<string>(claims) : new List<string>();
        if (string.IsNullOrWhiteSpace(basis))
        {
            return result;
        }

        string trimmed = basis.Trim();
        result.RemoveAll(c => string.Equals(c?.Trim(), trimmed, StringComparison.Ordinal));
        result.Insert(Math.Clamp(position, 0, result.Count), trimmed);
        return result;
    }

    /// <summary>
    /// The verifications that concern the answer's own claims: every entry except the out-of-rubric
    /// basis, matched by verbatim text — <see cref="BenchmarkClaimVerificationParser"/> echoes the
    /// submitted text back and the basis is submitted once. The input is returned unchanged in
    /// content when there is no basis.
    /// </summary>
    internal static List<BenchmarkClaimVerification> WithoutOutOfRubricBasis(
        IReadOnlyList<BenchmarkClaimVerification>? verifications,
        string? basis)
    {
        if (verifications == null) return new List<BenchmarkClaimVerification>();
        if (string.IsNullOrWhiteSpace(basis)) return verifications.ToList();

        string trimmed = basis.Trim();
        return verifications
            .Where(v => !string.Equals(v.Claim?.Trim(), trimmed, StringComparison.Ordinal))
            .ToList();
    }

    /// <summary>
    /// Whether the verifier refuted the out-of-rubric basis itself: the statement the Accuracy
    /// deduction rests on is false. Supported and Indeterminate both return false.
    /// </summary>
    internal static bool OutOfRubricBasisWasRefuted(
        IReadOnlyList<BenchmarkClaimVerification>? verifications,
        string? basis)
    {
        if (verifications == null || verifications.Count == 0) return false;
        if (string.IsNullOrWhiteSpace(basis)) return false;

        string trimmed = basis.Trim();
        var match = verifications.FirstOrDefault(
            v => string.Equals(v.Claim?.Trim(), trimmed, StringComparison.Ordinal));

        return match != null && match.Verdict == BenchmarkClaimVerdict.Refuted;
    }

    /// <summary>
    /// The claim list the verifier is given for a critical-error adjudication: the trimmed quote at
    /// index 0, ahead of whatever else was already there, and not repeated when an entry already
    /// matches it verbatim.
    ///
    /// The quote is carried in the prompt only. It is deliberately never written back to
    /// <see cref="BenchmarkRunAnswer.UnverifiedClaimsJson"/> or
    /// <see cref="BenchmarkRunAnswer.UnverifiedClaimCount"/>: that column means "claims the assessor
    /// could not adjudicate", and a claim the assessor called outright false is the opposite of one.
    /// </summary>
    internal static List<string> WithCriticalErrorQuoteFirst(IReadOnlyList<string>? claims, string? criticalErrorQuote)
    {
        var result = claims != null ? new List<string>(claims) : new List<string>();
        if (string.IsNullOrWhiteSpace(criticalErrorQuote))
        {
            return result;
        }

        string quote = criticalErrorQuote.Trim();
        if (!result.Any(c => string.Equals(c, quote, StringComparison.Ordinal)))
        {
            result.Insert(0, quote);
        }

        return result;
    }

    /// <summary>
    /// Whether the verifier supported the critical-error quote itself. Matched by the index the
    /// quote was submitted at — <see cref="BenchmarkClaimVerificationParser"/> emits one
    /// verification per submitted claim, in submission order, with the submitted text echoed back —
    /// and by verbatim text as a fallback, so a reordered response is still read correctly.
    /// </summary>
    internal static bool CriticalErrorQuoteWasSupported(
        IReadOnlyList<BenchmarkClaimVerification>? verifications,
        string? criticalErrorQuote)
    {
        if (verifications == null || verifications.Count == 0) return false;
        if (string.IsNullOrWhiteSpace(criticalErrorQuote)) return false;

        string quote = criticalErrorQuote.Trim();

        var match = verifications.FirstOrDefault(
            v => v.ClaimIndex == 0 && string.Equals(v.Claim?.Trim(), quote, StringComparison.Ordinal));
        match ??= verifications.FirstOrDefault(
            v => string.Equals(v.Claim?.Trim(), quote, StringComparison.Ordinal));

        return match != null && match.Verdict == BenchmarkClaimVerdict.Supported;
    }

    internal static List<string> ExtractDisputedClaims(BenchmarkRunAnswer answer, string? accuracyEvidence)
    {
        var claims = new List<string>();

        // 1. Critical error quote if identified by the assessor
        if (!string.IsNullOrWhiteSpace(answer.CriticalErrorQuote))
        {
            var quote = answer.CriticalErrorQuote.Trim();
            if (quote.Length >= 5 && !claims.Contains(quote, StringComparer.OrdinalIgnoreCase))
            {
                claims.Add(quote);
            }
        }

        // 2. Candidate answer numeric assertions
        if (!string.IsNullOrWhiteSpace(answer.AnswerText))
        {
            var rawSentences = Regex.Split(answer.AnswerText, @"(?<=[.!?\n])\s+");
            int numericClaimsCount = 0;
            foreach (var raw in rawSentences)
            {
                var trimmed = raw.Trim();
                if (trimmed.Length >= 10 && trimmed.Length <= 300 && Regex.IsMatch(trimmed, @"\d"))
                {
                    if (!claims.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
                    {
                        claims.Add(trimmed);
                        numericClaimsCount++;
                        if (numericClaimsCount >= 4) break;
                    }
                }
            }
        }

        // 3. Counter-claims from assessor accuracy evidence
        if (!string.IsNullOrWhiteSpace(accuracyEvidence))
        {
            var evidenceSentences = Regex.Split(accuracyEvidence, @"(?<=[.!?\n])\s+");
            foreach (var raw in evidenceSentences)
            {
                var trimmed = raw.Trim();
                if (trimmed.Length >= 15 && trimmed.Length <= 300 && !claims.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
                {
                    claims.Add(trimmed);
                    if (claims.Count >= 8) break;
                }
            }

            if (claims.Count == 0 && accuracyEvidence.Trim().Length <= 400)
            {
                claims.Add(accuracyEvidence.Trim());
            }
        }

        // 4. Fallback to review comment if still empty
        if (claims.Count == 0 && !string.IsNullOrWhiteSpace(answer.ReviewComment))
        {
            var comment = answer.ReviewComment.Trim();
            if (comment.Length <= 400)
            {
                claims.Add(comment);
            }
        }

        return claims;
    }
}

