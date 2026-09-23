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
    private BenchmarkCitationLivenessCheck? _citationLivenessCheck;

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
    /// True when the run was graded under the scoring method this build grades under. Anything
    /// that grades part of a run (re-assess, calibrate, re-run, retries, series resume) is refused
    /// otherwise, so one run never holds verdicts from two methods. The harness version is not
    /// part of the test: a re-run under a newer harness is recorded (<c>RerunHarnessVersion</c>).
    /// </summary>
    internal static bool IsCurrentScoringMethod(BenchmarkRun run)
        => run.ScoringMethodVersion == BenchmarkAssessmentPrompt.ScoringMethodVersion;

    /// <summary>Why an operation that grades part of a run was refused for a run graded under another scoring method.</summary>
    internal static string ScoringMethodRefusal(BenchmarkRun run) =>
        $"Refused: run {run.Id} was graded under scoring method {run.ScoringMethodVersion}, and this build grades under "
        + $"{BenchmarkAssessmentPrompt.ScoringMethodVersion}. Mixing the two inside one run would make its scores meaningless. "
        + "Start a new run instead.";

    /// <summary>
    /// The newest scoring method whose changes a rescore applies. Methods up to 10 changed how stored
    /// levels are turned into points and indices, which a rescore recomputes; methods 11 and 12 changed
    /// only the rules the assessor grades by, which a rescore cannot apply because it grades nothing.
    /// </summary>
    internal const int LastMethodRescoreCanApply = 10;

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

    /// <summary>
    /// Puts a run whose row was marked Running for a re-run that then did not, or could not, run
    /// back on the terminal status its answers describe. Never Failed or Canceled: either would
    /// make the run refuse every later re-run for a launch that changed none of its answers.
    /// </summary>
    private static async Task RestoreTerminalStatusAsync(
        ApplicationDbContext db, BenchmarkRun run, string? reason)
    {
        var answers = await db.BenchmarkRunAnswers
            .Where(a => a.BenchmarkRunId == run.Id)
            .ToListAsync(CancellationToken.None);

        BenchmarkRunFinalizer.Apply(run, answers, preserveCompletedAt: true);
        if (reason != null)
        {
            run.ErrorMessage = BenchmarkAssessmentFailure.Truncate(reason);
        }
        await db.SaveChangesAsync(CancellationToken.None);
    }

    // The loads of the three secondary grading paths. Each includes the suite's board, which every
    // grading prompt reads; BenchmarkBoardGuard refuses a grading pass whose load did not.

    /// <summary>The answer, its run, the run's suite with questions and board, and the assessor, for re-assessment.</summary>
    internal static IQueryable<BenchmarkRunAnswer> ReassessmentAnswerQuery(ApplicationDbContext db) =>
        db.BenchmarkRunAnswers
            .Include(a => a.BenchmarkRun)
            .ThenInclude(r => r.BenchmarkSuite)
            .ThenInclude(s => s!.Questions)
            .Include(a => a.BenchmarkRun)
            .ThenInclude(r => r.BenchmarkSuite)
            .ThenInclude(s => s!.GameSnapshot)
            .Include(a => a.BenchmarkRun.AssessorModelConfiguration);

    /// <summary>The run with its answers, assessor and suite (questions and board), for retrying failed assessments.</summary>
    internal static IQueryable<BenchmarkRun> RetryAssessmentsRunQuery(ApplicationDbContext db) =>
        db.BenchmarkRuns
            .Include(r => r.Answers)
            .Include(r => r.AssessorModelConfiguration)
            .Include(r => r.BenchmarkSuite)
            .ThenInclude(s => s!.Questions)
            .Include(r => r.BenchmarkSuite)
            .ThenInclude(s => s!.GameSnapshot);

    /// <summary>The run with its suite (questions and board), for an assessor calibration.</summary>
    internal static IQueryable<BenchmarkRun> CalibrationRunQuery(ApplicationDbContext db) =>
        db.BenchmarkRuns
            .Include(r => r.BenchmarkSuite)
            .ThenInclude(s => s!.Questions)
            .Include(r => r.BenchmarkSuite)
            .ThenInclude(s => s!.GameSnapshot);

    /// <summary>Why a re-run was refused for a run that recorded no candidate prompt options.</summary>
    internal static string MissingPromptOptionsMessage(BenchmarkRun run) =>
        $"Re-run refused: run {run.Id} has no recorded candidate prompt options (BenchmarkRun.CandidatePromptOptionsJson is empty), "
        + "so the candidate prompt it was answered under cannot be rebuilt. Start a new run instead.";

    /// <summary>A trial leaves the run byte-identical, so every exit from one puts back what the caller captured.</summary>
    private static async Task RestoreCapturedStatusAsync(
        ApplicationDbContext db, BenchmarkRun run, BenchmarkRunStatus originalStatus, DateTime? originalCompletedAtUtc)
    {
        run.Status = originalStatus;
        run.CompletedAtUtc = originalCompletedAtUtc;
        await db.SaveChangesAsync(CancellationToken.None);
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

    /// <summary>
    /// Records on the run which board its suite carried at launch and, in
    /// <see cref="BenchmarkRun.BoardFactsCheckJson"/>, whether the BOARD FACTS quotes of
    /// <paramref name="questions"/> occur on it. Both describe the run as launched and are never
    /// recomputed against later edits. The check is advisory: it refuses nothing. A suite without a
    /// board leaves the board fields as they are and the check null.
    /// </summary>
    internal static void StampBoardProvenance(BenchmarkRun run, IEnumerable<BenchmarkQuestion> questions)
    {
        var board = run.BenchmarkSuite?.GameSnapshot;
        if (board != null)
        {
            run.GameSnapshotNameUsed = board.Name;
            run.GameSnapshotSha256Used = board.Sha256;
            run.GameSnapshotCharCountUsed = board.CharCount;
            run.GameSnapshotCaptureMethodUsed = board.CaptureMethod;
            // A board stored before the column existed carries no parsed format until its text is
            // next written, so the header is read here instead.
            run.GameSnapshotFormatVersionUsed = board.SnapshotFormatVersion
                ?? BenchmarkSnapshotHeaderParser.Parse(board.SanitizedText).SnapshotFormatVersion;
        }

        run.BoardFactsCheckJson = board != null
            ? BenchmarkBoardFactsChecker.Serialize(BenchmarkBoardFactsChecker.Check(board.SanitizedText, questions))
            : null;
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

            StampBoardProvenance(run, questions);

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

            VerifyCandidateDeliveryBeforeRun(
                run, testedConfig, systemPrompt, segmentedPrompt, questions.FirstOrDefault()?.QuestionText, isRerun: false);

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
    /// It carries two terminal handlers for the same reason <see cref="ExecuteRunAsync"/> does:
    /// without them a throw escaped the method with the row still reading <c>Running</c> and no
    /// owner in <see cref="BenchmarkRunManager"/>, which is a state nothing in the UI can leave.
    /// Both open their own scope, because the one above may be gone by the time they run. Unlike
    /// <see cref="ExecuteRunAsync"/>, the cancellation handler restores the status the answers
    /// describe rather than writing <c>Canceled</c>: a retry never removes an answer row, so the
    /// suite is as complete after the cancel as it was before it, and <c>Canceled</c> would make
    /// every later re-run refuse the run.
    /// </summary>
    public async Task RunFailedQuestionsAsync(long runId, CancellationToken cancellationToken)
    {
        var rerunStopwatch = Stopwatch.StartNew();

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

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

            if (!IsCurrentScoringMethod(run))
            {
                await RestoreTerminalStatusAsync(db, run, ScoringMethodRefusal(run));
                _runManager.Complete(runId);
                return;
            }

            var testedConfig = run.TestedModelConfiguration;
            var assessorConfig = run.AssessorModelConfiguration;

            if (testedConfig == null || string.IsNullOrWhiteSpace(testedConfig.EncryptedApiKey) ||
                assessorConfig == null || string.IsNullOrWhiteSpace(assessorConfig.EncryptedApiKey))
            {
                await RestoreTerminalStatusAsync(db, run, "Failed-question re-run could not start: the tested or assessor model configuration is missing or has no API key.");
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
                .Where(BenchmarkRunFinalizer.NeedsReExecution)
                .OrderBy(a => a.OrderIndex)
                .ToList();

            if (failedAnswers.Count == 0)
            {
                await RestoreTerminalStatusAsync(db, run, null);
                _runManager.Complete(runId);
                return;
            }

            // A re-run rebuilds the candidate prompt from the options the run recorded; defaults would
            // stand in for options nobody recorded, and the re-run's answers would claim them.
            if (string.IsNullOrWhiteSpace(run.CandidatePromptOptionsJson))
            {
                await RestoreTerminalStatusAsync(db, run, MissingPromptOptionsMessage(run));
                _runManager.Complete(runId);
                return;
            }

            // A re-run overwrites answer rows in place and adds none, so the suite totals cannot
            // describe its progress. The scope and the two marks below are what the progress
            // dialog counts instead.
            _runManager.SetRerunScope(runId, failedAnswers.Select(a => a.OrderIndex));

            run.Status = BenchmarkRunStatus.Running;
            run.RerunStartedAtUtc = DateTime.UtcNow;
            run.RerunCompletedAtUtc = null;
            await db.SaveChangesAsync(cancellationToken);

            var profile = run.ScoringProfileId.HasValue
                ? await _scoringProfileService.GetProfileByIdAsync(run.ScoringProfileId.Value) ?? await _scoringProfileService.GetDefaultProfileAsync()
                : await _scoringProfileService.GetDefaultProfileAsync();
            var scoringConstants = _scoringProfileService.ToConstants(profile);

            var allowedTools = _configuration.GetSection("Benchmark:AllowedTools").Get<List<string>>() ?? _defaultAllowedTools;
            int maxResultLength = _configuration.GetValue<int>("Benchmark:MaxResultLength", 10000);
            int maxCallsPerSession = _configuration.GetValue<int>("Benchmark:MaxCallsPerSession", 50);

            var promptOptions = BenchmarkCandidatePromptOptions.FromJson(run.CandidatePromptOptionsJson!);
            string systemPrompt = promptOptions.BuildSystemPrompt(_chatService, testedConfig.ParallelExecutionMode);
            var segmentedPrompt = BuildCandidateSegmentedPrompt(promptOptions, testedConfig.ParallelExecutionMode);

            // The re-run's own instrument, recorded in its own columns. The five original
            // fingerprints describe the instrument the run's other answers were produced under, and
            // they are the only record that the prompt did not move between two runs; overwriting
            // them falsifies the provenance of every answer this pass does not touch.
            PopulateRerunInstrumentFingerprint(run, systemPrompt);

            VerifyCandidateDeliveryBeforeRun(
                run, testedConfig, systemPrompt, segmentedPrompt, failedAnswers.FirstOrDefault()?.QuestionText, isRerun: true);

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
                _runManager.MarkRerunAnswered(runId, answer.OrderIndex);

                suiteQuestions.TryGetValue(answer.OrderIndex, out var ep);
                await ExecutePerQuestionAssessmentAsync(
                    db, configService, run, answer, ep,
                    assessorConfig, assessorApiKey, scoringConstants, cancellationToken);
                _runManager.MarkRerunScored(runId, answer.OrderIndex);
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
                rerunStopwatch.Stop();
                run.RerunCompletedAtUtc = DateTime.UtcNow;
                await RestoreTerminalStatusAsync(db, run, "Failed-question re-run canceled.");
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

    /// <summary>
    /// The candidate's message history: the production chat system prompt, then the suite's game
    /// snapshot when it has one, then the question carrying the instruction chat appends to every
    /// turn after the first. The suffix is in the model-facing message only; the question text
    /// stored on the answer is the plain question.
    /// </summary>
    /// <remarks>
    /// The order is byte-for-byte the shape <c>ChatService</c> sends — the prompt at index 0,
    /// snapshots as later system messages — and it is the only shape the providers handle
    /// correctly. <c>GoogleProvider.OrderSystemParts</c> and
    /// <c>AnthropicProvider.BuildChatRequestBody</c> replace the *first* system message with the
    /// prompt segments and hoist every later one, so a history whose first system message was the
    /// board dropped the board. The prompt is prepended whether or not a segmented prompt is in
    /// play: with <c>PromptCacheSettings:EnableSegmentedPrompt</c> off,
    /// <c>AgentLoopRunner</c> injects a prompt only into a history that has no system message at
    /// all — and a board is one.
    /// </remarks>
    internal static List<object> BuildCandidateSeedHistory(BenchmarkRun run, string systemPrompt, string questionText)
    {
        var seed = new List<object>
        {
            new { role = "system", content = systemPrompt }
        };
        var board = run.BenchmarkSuite?.GameSnapshot;
        if (board != null)
        {
            seed.Add(new
            {
                role = "system",
                content = ChatService.GameSnapshotPrefix + "\n" + board.SanitizedText
            });
        }
        seed.Add(new { role = "user", content = questionText + ChatService.NoGreetInstruction });
        return seed;
    }

    /// <summary>
    /// Builds the candidate request body through the provider that will send it and requires the
    /// system prompt and — when the suite has a board — the board to be in it. Throws
    /// <see cref="InvalidOperationException"/> when either is missing.
    /// </summary>
    /// <remarks>
    /// The provider is resolved the way <see cref="AgentLoopRunner"/> resolves it, from the same
    /// registrations, so the instance probed is the instance that runs. An unknown provider name
    /// fails here rather than one line later inside the agent loop, with the same effect.
    /// </remarks>
    private void VerifyCandidateDelivery(
        BenchmarkRun run,
        string providerName,
        IAiProvider? requestProvider,
        List<object> seedHistory,
        SegmentedPrompt? segmentedPrompt,
        string systemPrompt,
        int? questionNumber)
    {
        var provider = requestProvider;
        using var scope = provider == null ? _scopeFactory.CreateScope() : null;
        provider ??= scope!.ServiceProvider.GetServices<IAiProvider>()
            .FirstOrDefault(p => string.Equals(p.ProviderName, providerName, StringComparison.OrdinalIgnoreCase));

        if (provider == null)
        {
            throw new InvalidOperationException(
                $"Harness delivery check failed: unknown AI provider '{providerName}'.");
        }

        BenchmarkCandidateRequestProbe.Verify(
            provider,
            seedHistory,
            segmentedPrompt,
            systemPrompt,
            run.BenchmarkSuite?.GameSnapshot?.SanitizedText,
            questionNumber);
    }

    /// <summary>
    /// The once-per-execution delivery check, run before the first question so that a candidate
    /// that would receive neither the prompt nor the board costs nothing: the throw reaches the
    /// caller's terminal handler, which marks the run failed with this message and creates no
    /// answers. A re-run stamps <see cref="BenchmarkRun.RerunCandidateDeliveryVerifiedAtUtc"/> and
    /// leaves the pre-run stamp alone.
    /// </summary>
    private void VerifyCandidateDeliveryBeforeRun(
        BenchmarkRun run,
        SystemAiApiConfiguration testedConfig,
        string systemPrompt,
        SegmentedPrompt? segmentedPrompt,
        string? firstQuestionText,
        bool isRerun)
    {
        VerifyCandidateDelivery(
            run,
            testedConfig.Provider,
            requestProvider: null,
            BuildCandidateSeedHistory(run, systemPrompt, firstQuestionText ?? string.Empty),
            segmentedPrompt,
            systemPrompt,
            questionNumber: null);
        if (isRerun)
        {
            run.RerunCandidateDeliveryVerifiedAtUtc = DateTime.UtcNow;
        }
        else
        {
            run.CandidateDeliveryVerifiedAtUtc = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// The largest result cap a handler of one of <paramref name="allowedToolNames"/> declares, or
    /// null when none declares one or the tool registry cannot be resolved. The registry is a
    /// singleton, reached through a scope so the service's constructor does not depend on it.
    /// </summary>
    private int? ResolveLargestResultLengthOverride(IEnumerable<string> allowedToolNames)
    {
        using var scope = _scopeFactory.CreateScope();
        var toolRegistry = scope.ServiceProvider.GetService<Tools.ToolRegistry>();
        return toolRegistry?.LargestResultLengthOverride(allowedToolNames);
    }

    /// <summary>
    /// A single-shot grading request's system text as one frozen segment, and a seed history that
    /// carries the same text as its first system message, the board block as a second system
    /// message when there is one, then the user message. Providers that honour segments build
    /// <c>system</c> from the segment and hoist the second system message after it — Anthropic as
    /// the second cached block, Google as the next <c>systemInstruction</c> part — and OpenAI joins
    /// every history system message into <c>instructions</c>, so on all three the grader reads the
    /// instructions, then the whole board, then the question, and the first two form a prefix
    /// shared by every grading call of the run. The shape is the one
    /// <see cref="BuildCandidateSeedHistory"/> gives the candidate.
    /// </summary>
    internal static (SegmentedPrompt Prompt, List<object> SeedHistory) BuildGradingPrompt(
        string systemPrompt,
        string? preamble,
        string userMessage,
        string? boardBlock = null)
    {
        string frozen = string.IsNullOrEmpty(preamble)
            ? systemPrompt
            : systemPrompt + Environment.NewLine + Environment.NewLine + preamble;
        var segmented = new SegmentedPrompt(frozen, "", "");
        var seedHistory = new List<object>
        {
            new { role = "system", content = segmented.FullPrompt }
        };
        if (!string.IsNullOrEmpty(boardBlock))
        {
            seedHistory.Add(new { role = "system", content = boardBlock });
        }
        seedHistory.Add(new { role = "user", content = userMessage });
        return (segmented, seedHistory);
    }

    /// <summary>The system sentence every per-question grading request opens with.</summary>
    internal const string GradingSystemPrompt =
        "You are an objective AI benchmark evaluator. Strictly adhere to the requested JSON response format.";

    /// <summary>The board block every per-question grading role reads ahead of the question; null for a suite without a board.</summary>
    internal static string? GradingBoardBlock(BenchmarkRun run)
        => BenchmarkAssessmentPrompt.BuildGradingBoardBlock(
            run.BenchmarkSuite?.GameSnapshot?.Name, run.BenchmarkSuite?.GameSnapshot?.SanitizedText);

    /// <summary>
    /// Runs <see cref="BenchmarkGradingRequestProbe"/> against <paramref name="request"/> through
    /// the provider that will send it, resolved the way <see cref="AgentLoopRunner"/> resolves it.
    /// Throws <see cref="InvalidOperationException"/> when the request does not carry its
    /// instructions, the board and the question in that order, or names an unknown provider.
    /// </summary>
    private void VerifyGradingDelivery(
        AgentRunRequest request,
        string role,
        string systemText,
        string? boardBlock,
        string? boardText,
        string questionMarker,
        int questionNumber)
    {
        var provider = request.AiProvider;
        using var scope = provider == null ? _scopeFactory.CreateScope() : null;
        provider ??= scope!.ServiceProvider.GetServices<IAiProvider>()
            .FirstOrDefault(p => string.Equals(p.ProviderName, request.ProviderName, StringComparison.OrdinalIgnoreCase));

        if (provider == null)
        {
            throw new InvalidOperationException(
                $"Harness delivery check failed: unknown AI provider '{request.ProviderName}'.");
        }

        BenchmarkGradingRequestProbe.Verify(
            provider, request, role, systemText, boardBlock, boardText, questionMarker, questionNumber);
    }

    /// <summary>
    /// <see cref="VerifyGradingDelivery"/> for a claim verification request built by
    /// <see cref="BuildClaimVerificationRequest"/>: its one-sentence system prompt, then the
    /// verifier message's board block and question context.
    /// </summary>
    private void VerifyClaimVerificationDelivery(AgentRunRequest request, BenchmarkRun run, int questionNumber)
        => VerifyGradingDelivery(
            request,
            "claim verifier",
            request.SystemPrompt ?? string.Empty,
            BenchmarkClaimVerificationPrompt.BuildBoardBlock(
                run.BenchmarkSuite?.GameSnapshot?.Name, run.BenchmarkSuite?.GameSnapshot?.SanitizedText),
            run.BenchmarkSuite?.GameSnapshot?.SanitizedText,
            BenchmarkClaimVerificationPrompt.QuestionBlockMarker,
            questionNumber);

    /// <summary>
    /// <see cref="VerifyGradingDelivery"/> for a per-question grading request built by
    /// <see cref="BuildGradingPrompt"/> with <see cref="GradingBoardBlock"/>.
    /// </summary>
    private void VerifyPerQuestionGradingDelivery(AgentRunRequest request, string role, BenchmarkRun run, int questionNumber)
        => VerifyGradingDelivery(
            request,
            role,
            request.SegmentedPrompt?.FullPrompt ?? request.SystemPrompt ?? string.Empty,
            GradingBoardBlock(run),
            run.BenchmarkSuite?.GameSnapshot?.SanitizedText,
            BenchmarkAssessmentPrompt.QuestionBlockMarker,
            questionNumber);

    internal async Task<BenchmarkRunAnswer> ExecuteSingleQuestionAsync(
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
        // agent context below is given, so the two cannot drift apart, and from the largest result
        // cap an allowed tool's handler declares above it.
        var toolCallRecordLimits = BenchmarkToolCallRecordLimits.Resolve(
            _configuration, maxResultLength, ResolveLargestResultLengthOverride(allowedTools));

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
            SeedHistory = BuildCandidateSeedHistory(run, systemPrompt, question.QuestionText)
        };

        int perQuestionTimeoutSec = ResolveQuestionTimeoutSeconds(question.Difficulty, question.AssessedDifficulty);
        using var questionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        questionCts.CancelAfter(TimeSpan.FromSeconds(perQuestionTimeoutSec));

        var runResult = new AgentRunResult();
        var sw = Stopwatch.StartNew();

        string? terminalError = null;

        // The provider's own bounded error payload, from the first "error" event that carried one.
        // Stored on the answer as ProviderErrorDetail for a terminal failure.
        string? terminalErrorDetail = null;

        // Distinct error texts already folded into terminalError, so a provider that repeats the
        // same message on every retry does not turn it into a wall of duplicates.
        var seenErrorTexts = new HashSet<string>(StringComparer.Ordinal);

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
            // The run-level check ran against the first question's seed only. This one runs against
            // the request actually about to be sent, and a miss becomes the terminal error below:
            // the answer is stored Failed, never graded, counted under Transport Defects, and
            // repeatable with "re-run failed questions".
            VerifyCandidateDelivery(
                run, runRequest.ProviderName, runRequest.AiProvider, runRequest.SeedHistory,
                runRequest.SegmentedPrompt, systemPrompt, question.OrderIndex);

            await foreach (var evt in _agentLoopRunner.RunAsync(runRequest, runRequest.Budget, runResult, questionCts.Token))
            {
                if (evt.Type == "error")
                {
                    // The first event's text is the terminal error; a later, distinct text is
                    // appended rather than overwriting it, so a run that failed twice for two
                    // different reasons reports both instead of only the last.
                    string? text = evt.Data;
                    if (!string.IsNullOrEmpty(text) && seenErrorTexts.Add(text))
                    {
                        terminalError = terminalError == null ? text : $"{terminalError} | {text}";
                    }
                    terminalErrorDetail ??= evt.Detail;
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

        // A provider error that arrived before the cancel keeps its provider classification; only a
        // cancel with no provider error on record is the operator's.
        bool canceledByOperator = cancellationToken.IsCancellationRequested && seenErrorTexts.Count == 0;

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

        // The operator's cancel is decided ahead of the classification: tearing the provider stream
        // down surfaces as a transport exception, which is what the classifier sees, and a transport
        // exception is indistinguishable from a real outage without knowing the cancel was intended.
        // A per-question timeout cancels only its own linked token and is already caught above.
        BenchmarkAnswerStatus status;
        if (canceledByOperator)
        {
            status = BenchmarkAnswerStatus.Canceled;
            terminalError = "Canceled by the operator before the answer completed.";
            terminalErrorDetail = null;
        }
        else if (classification.IsProviderError)
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

        // Same shape as BenchmarkRunFinalizer.HasTerminalFailure: the provider failed the request
        // outright, or the operator cut it short, so there is nothing authored to grade. AnswerText
        // and ThoughtText are cleared — any text captured before the end is a fragment, not an
        // answer — and OutputTokens falls back to 0 rather than a character-based estimate against
        // text that no longer exists, unless the provider itself already reported real usage before
        // failing.
        bool isTerminalFailure = status is BenchmarkAnswerStatus.ProviderError or BenchmarkAnswerStatus.Failed
            or BenchmarkAnswerStatus.Canceled;

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
            // AnswerText's column is NOT NULL, so "cleared" means empty here rather than null; every
            // reader already treats the two identically through string.IsNullOrWhiteSpace.
            AnswerText = isTerminalFailure ? string.Empty : sanitized.AnswerText,
            ThoughtText = isTerminalFailure ? null : sanitized.ThoughtText,
            Status = status,
            AssessmentStatus = BenchmarkAssessmentStatus.Pending,
            ErrorMessage = BenchmarkAssessmentFailure.Truncate(terminalError),
            ProviderErrorDetail = isTerminalFailure ? BenchmarkAssessmentFailure.Truncate(terminalErrorDetail, 4000) : null,
            HttpStatusCode = canceledByOperator ? null : classification.HttpStatus,
            DurationMs = runResult.TotalDurationMs ?? sw.ElapsedMilliseconds,
            TimeToFirstTokenMs = runResult.TimeToFirstTokenMs,
            ActualServiceTierUsed = runResult.ActualServiceTier,
            ToolCallSummary = string.IsNullOrEmpty(toolSummary) ? null : toolSummary,
            InputTokens = runResult.TotalPromptTokens > 0 ? runResult.TotalPromptTokens : runResult.EstimatedInputTokens,
            OutputTokens = isTerminalFailure
                ? (runResult.OutputTokens > 0 ? runResult.OutputTokens : 0)
                : (runResult.OutputTokens > 0 ? runResult.OutputTokens : runResult.EstimatedOutputTokens),
            ReasoningTokens = BenchmarkDescriptionService.SumReasoningTokens(runResult),
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
        // agent context below is given, so the two cannot drift apart, and from the largest result
        // cap an allowed tool's handler declares above it.
        var toolCallRecordLimits = BenchmarkToolCallRecordLimits.Resolve(
            _configuration, maxResultLength, ResolveLargestResultLengthOverride(allowedTools));

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
            SeedHistory = BuildCandidateSeedHistory(run, systemPrompt, answer.QuestionText)
        };

        int perQuestionTimeoutSec = ResolveQuestionTimeoutSeconds(answer.Difficulty, answer.AssessedDifficulty);
        using var questionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        questionCts.CancelAfter(TimeSpan.FromSeconds(perQuestionTimeoutSec));

        var runResult = new AgentRunResult();
        var sw = Stopwatch.StartNew();

        string? terminalError = null;

        // See ExecuteSingleQuestionAsync for the terminalErrorDetail and seenErrorTexts contract.
        string? terminalErrorDetail = null;
        var seenErrorTexts = new HashSet<string>(StringComparer.Ordinal);

        // See ExecuteSingleQuestionAsync for why the exception itself is retained.
        Exception? terminalException = null;

        // Re-runs show the same Answering state as a first run; see ExecuteSingleQuestionAsync.
        _runManager.MarkQuestionInFlight(run.Id, answer.OrderIndex);
        try
        {
            // Per question, on the same contract as ExecuteSingleQuestionAsync.
            VerifyCandidateDelivery(
                run, runRequest.ProviderName, runRequest.AiProvider, runRequest.SeedHistory,
                runRequest.SegmentedPrompt, systemPrompt, answer.OrderIndex);

            await foreach (var evt in _agentLoopRunner.RunAsync(runRequest, runRequest.Budget, runResult, questionCts.Token))
            {
                if (evt.Type == "error")
                {
                    string? text = evt.Data;
                    if (!string.IsNullOrEmpty(text) && seenErrorTexts.Add(text))
                    {
                        terminalError = terminalError == null ? text : $"{terminalError} | {text}";
                    }
                    terminalErrorDetail ??= evt.Detail;
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

        // See ExecuteSingleQuestionAsync for the rule this follows.
        bool canceledByOperator = cancellationToken.IsCancellationRequested && seenErrorTexts.Count == 0;

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

        // See ExecuteSingleQuestionAsync for why the operator's cancel is decided ahead of the
        // classification.
        BenchmarkAnswerStatus status;
        if (canceledByOperator)
        {
            status = BenchmarkAnswerStatus.Canceled;
            terminalError = "Canceled by the operator before the answer completed.";
            terminalErrorDetail = null;
        }
        else if (classification.IsProviderError)
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

        // See ExecuteSingleQuestionAsync for the rule this follows, and for why AnswerText clears
        // to empty rather than null.
        bool isTerminalFailure = status is BenchmarkAnswerStatus.ProviderError or BenchmarkAnswerStatus.Failed
            or BenchmarkAnswerStatus.Canceled;

        // The replaced attempt's outcome, kept before the row is overwritten: nothing else records
        // why this answer was re-executed.
        answer.RerunOfStatus = answer.Status;
        answer.RerunOfErrorMessage = BenchmarkAssessmentFailure.Truncate(answer.ErrorMessage, 512);
        answer.RerunAtUtc = DateTime.UtcNow;

        answer.AnswerText = isTerminalFailure ? string.Empty : sanitized.AnswerText;
        answer.ThoughtText = isTerminalFailure ? null : sanitized.ThoughtText;
        answer.Status = status;
        answer.AssessmentStatus = BenchmarkAssessmentStatus.Pending;
        answer.ErrorMessage = BenchmarkAssessmentFailure.Truncate(terminalError);
        answer.ProviderErrorDetail = isTerminalFailure ? BenchmarkAssessmentFailure.Truncate(terminalErrorDetail, 4000) : null;
        answer.HttpStatusCode = canceledByOperator ? null : classification.HttpStatus;
        answer.DurationMs = runResult.TotalDurationMs ?? sw.ElapsedMilliseconds;
        answer.TimeToFirstTokenMs = runResult.TimeToFirstTokenMs;
        answer.ActualServiceTierUsed = runResult.ActualServiceTier;
        answer.ToolCallSummary = string.IsNullOrEmpty(toolSummary) ? null : toolSummary;
        answer.InputTokens = runResult.TotalPromptTokens > 0 ? runResult.TotalPromptTokens : runResult.EstimatedInputTokens;
        answer.OutputTokens = isTerminalFailure
            ? (runResult.OutputTokens > 0 ? runResult.OutputTokens : 0)
            : (runResult.OutputTokens > 0 ? runResult.OutputTokens : runResult.EstimatedOutputTokens);
        answer.ReasoningTokens = BenchmarkDescriptionService.SumReasoningTokens(runResult);
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
                answer.AssessmentError = answer.Status == BenchmarkAnswerStatus.Canceled
                    ? "Not assessed: the operator canceled the run before the answer completed; excluded from scoring."
                    : BenchmarkRunFinalizer.HasTerminalFailure(answer)
                        ? "Not assessed: the provider failed the request; excluded from scoring."
                        : "Not assessed: the answer contained no text, and the provider reported no normal stop.";
            }

            await db.SaveChangesAsync(CancellationToken.None);
            return;
        }

        // A board the load did not include fails this answer's assessment, re-runnable, rather
        // than grading a snapshot suite rubric-only.
        try
        {
            BenchmarkBoardGuard.RequireBoardLoaded(run);
        }
        catch (InvalidOperationException ex)
        {
            answer.AssessmentStatus = BenchmarkAssessmentStatus.Failed;
            answer.AssessmentError = BenchmarkAssessmentFailure.Truncate(ex.Message);
            _logger.LogWarning("Benchmark run {RunId} answer {OrderIndex} assessment failed: {Error}",
                run.Id, answer.OrderIndex, ex.Message);
            await db.SaveChangesAsync(CancellationToken.None);
            return;
        }

        answer.AssessmentStatus = BenchmarkAssessmentStatus.Assessing;
        await db.SaveChangesAsync(CancellationToken.None);

        var allowedTools = _configuration.GetSection("Benchmark:AllowedTools").Get<List<string>>() ?? _defaultAllowedTools;
        // Stamped on the answer only with a verdict, which every call reaches only after its
        // delivery probe passed.
        int? assessorBoardChars = BenchmarkBoardGuard.BoardCharsSent(run);
        string? boardBlock = GradingBoardBlock(run);
        string prompt = BenchmarkAssessmentPrompt.BuildPerQuestionBody(
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
            boardGivenAbove: boardBlock != null);
        var (gradingPrompt, gradingSeedHistory) = BuildGradingPrompt(
            GradingSystemPrompt,
            BenchmarkAssessmentPrompt.BuildPerQuestionPreamble(run.SuiteName),
            prompt,
            boardBlock);

        int assessorMaxTokens = _configuration.GetValue<int>("Benchmark:AssessorMaxOutputTokens", 32000);

        var runRequest = new AgentRunRequest
        {
            ProviderName = assessorConfig.Provider,
            ModelId = assessorConfig.ModelId,
            ApiKey = assessorApiKey,
            ModelDisplayName = assessorConfig.DisplayName,
            SystemPrompt = gradingPrompt.FullPrompt,
            SegmentedPrompt = gradingPrompt,
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
            CacheConversationTail = false,
            Budget = new AgentRunBudget { MaxTotalModelCalls = 2 },
            ToolExecutionContext = new Tools.ToolExecutionContext
            {
                SessionId = Overseer.Services.Privacy.SessionRef.Persistent(run.Id),
                UserId = run.StartedByUserId ?? string.Empty,
                ShowDebugLog = false
            },
            SeedHistory = gradingSeedHistory
        };

        var runResult = new AgentRunResult();
        var sw = Stopwatch.StartNew();
        string? terminalError = null;
        try
        {
            // A request that would not carry the board ahead of the question fails this answer's
            // assessment, re-runnable, before anything is sent.
            VerifyPerQuestionGradingDelivery(runRequest, "assessor", run, answer.OrderIndex);
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
                VerifyPerQuestionGradingDelivery(runRequest, "assessor", run, answer.OrderIndex);
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
            answer.AssessorBoardChars = assessorBoardChars;

            // An evidence-informed re-grade and a claim verification describe the verdict they read,
            // so a new verdict drops both.
            ClearReplacedVerdictEvidence(answer);
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
            // null is reserved for runs that predate the field and were never asked. A plain
            // duplicate of a "Suspected false:" entry is dropped first, so this count agrees with
            // what BuildClaimManifest actually submits and the Claim Verification Yield line reports.
            var dedupedUnverifiedClaims = DeduplicateUnverifiedClaims(res.UnverifiedClaims, answer.AnswerText);
            answer.UnverifiedClaimCount = dedupedUnverifiedClaims.Count;
            answer.UnverifiedClaimsJson = dedupedUnverifiedClaims.Count > 0
                ? JsonSerializer.Serialize(dedupedUnverifiedClaims)
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

            // Also set when the Accuracy evidence docks a sentence the assessor reported as
            // "Suspected false:" — an own-knowledge deduction without the marker.
            bool docksSuspectedFalse = !res.AccuracyOutOfRubric
                && DocksSuspectedFalse(answer.AnswerText, res.AccuracyEvidence, res.UnverifiedClaims, res.AccuracyLevel);
            if (res.AccuracyOutOfRubric || docksSuspectedFalse)
            {
                flags |= BenchmarkAnswerFlags.OutOfRubricAccuracyDeduction;
            }
            else
            {
                flags &= ~BenchmarkAnswerFlags.OutOfRubricAccuracyDeduction;
            }

            // Decided here rather than in the parser, unlike the flags above: the detector weighs
            // all four levels against one another, and the gate is the answer's own status, which
            // the parser never sees. An answer outside the quality index carries no verdict worth a
            // second reading — the detector's own rule already refuses a 0/0/0/0 grading, and the
            // gate says so at the level where the status is known. Cleared on re-assessment for the
            // same reason ContestedVerdict is.
            bool dimensionOutlier = BenchmarkRunFinalizer.CountsTowardQualityIndex(answer)
                && BenchmarkVerdictConsistency.IsDimensionOutlier(
                    res.AccuracyLevel, res.AccuracyEvidence,
                    res.CompletenessLevel, res.CompletenessEvidence,
                    res.ConcisenessLevel,
                    res.ReadabilityLevel,
                    res.Comment);

            if (dimensionOutlier)
            {
                flags |= BenchmarkAnswerFlags.DimensionOutlier;
            }
            else
            {
                flags &= ~BenchmarkAnswerFlags.DimensionOutlier;
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
            else if (docksSuspectedFalse)
            {
                _logger.LogInformation(
                    "Benchmark run {RunId} answer {OrderIndex}: accuracy evidence quotes a sentence the assessor reported as suspected false; recorded as an out-of-rubric deduction.",
                    run.Id, answer.OrderIndex);
            }

            if (dimensionOutlier)
            {
                _logger.LogInformation(
                    "Benchmark run {RunId} answer {OrderIndex}: one graded dimension collapsed beside three healthy ones with no defect of that kind named; recorded as a dimension outlier.",
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

        // The assessor's own text, on every graded answer and on both branches above: a verdict that
        // failed to parse is exactly the one whose text is worth having, and a verdict that parsed
        // still cannot say afterwards whether a dimension was graded at the floor or never graded at
        // all. The parse result carries the text when a reply arrived; runResult.FinalText is what
        // remains when the call ended in a terminal error before any parse.
        answer.AssessmentRawText = CapAssessmentRawText(parseResult.RawText ?? runResult.FinalText);

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
        // Accuracy deduction rests on, which the assessor gave from its own knowledge, and a
        // sentence of the answer the assessor quoted when it docked Accuracy.
        if (run.ClaimVerifierModelConfigurationId.HasValue && NeedsClaimVerificationOrAccusation(answer))
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
    /// The length <see cref="BenchmarkRunAnswer.AssessmentRawText"/> holds. A verdict runs to a few
    /// hundred characters, so the cap bounds a runaway reply rather than a normal one.
    /// </summary>
    private const int AssessmentRawTextMaxLength = 8000;

    /// <summary>
    /// The assessor's final text as that column stores it: trimmed, truncated to
    /// <see cref="AssessmentRawTextMaxLength"/> characters, and null when there was no text at all.
    /// Bounding is the writer's job — the column is the record of what the grader wrote, so a long
    /// reply is shortened rather than allowed to fail the save.
    /// </summary>
    private static string? CapAssessmentRawText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string trimmed = text.Trim();
        return trimmed.Length > AssessmentRawTextMaxLength ? trimmed[..AssessmentRawTextMaxLength] : trimmed;
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
            string.IsNullOrWhiteSpace(res.ReadabilityEvidence) &&
            string.IsNullOrWhiteSpace(res.CriticalErrorQuote) &&
            !res.CriticalErrorDemoted)
        {
            return null;
        }

        // Readability evidence is stored for the same reason the other two are: the FORM: marker it
        // carries is counted against the Readability level, and a count computed from a marker
        // boolean alone cannot tell a marker-only string from one that also names a real defect.
        // Absent on every run graded before this key existed, where the per-answer ReadabilityFormOnly
        // column is the only marker signal.
        return JsonSerializer.Serialize(new
        {
            accuracy = res.AccuracyEvidence,
            completeness = res.CompletenessEvidence,
            readability = res.ReadabilityEvidence,
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

        // The assessor docked Accuracy on its own knowledge: it prefixed the deduction with "Not in
        // rubric:", or grounded it on a claim it could not verify. Read from the flag, which carries
        // both. Weaker evidence than a refutation or a self-described fabrication, and stronger than
        // an unevidenced deduction, because the grader has stated where the basis came from. Gated
        // on the level so a level-6 answer whose evidence merely mentions an out-of-rubric
        // observation does not spend a verdict.
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

        // One dimension collapsed to 1 or 0 beside three at 3 or above, with nothing naming a defect
        // of that kind. Below every trigger above because each of those rests on something the
        // assessor wrote and this one rests on what it did not write, and above the two below
        // because a level nobody explained moves the quality score further than either of them can.
        if ((((BenchmarkAnswerFlags)answer.AnswerFlags) & BenchmarkAnswerFlags.DimensionOutlier) != 0)
        {
            return SecondOpinionTriggers.DimensionOutlier;
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
            .Where(a => NeedsClaimVerificationOrAccusation(a) ||
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

        // Only the answer's own sentences are persisted as its unverified claims; the assessor's
        // sentences are carried in the manifest alone. A retry finds the answer claims this branch
        // persisted earlier and recovers the assessor statements from the same deterministic
        // extraction.
        List<string> assessorStatements = new();
        if (isDisputed)
        {
            var disputed = ExtractDisputedClaims(answer, accuracyEvidence);
            if (claims == null || claims.Count == 0)
            {
                claims = disputed.AnswerClaims;
                assessorStatements = disputed.AssessorStatements;
                if (claims.Count > 0)
                {
                    answer.UnverifiedClaimCount = claims.Count;
                    answer.UnverifiedClaimsJson = JsonSerializer.Serialize(claims);
                }
            }
            else if (claims.SequenceEqual(disputed.AnswerClaims, StringComparer.Ordinal))
            {
                assessorStatements = disputed.AssessorStatements;
            }
        }

        // After the disputed branch has persisted whatever it collected, so the quote is carried in
        // the prompt only and never lands in the unadjudicable-claims columns. The out-of-rubric
        // basis is carried the same way. Fixed order: the critical-error quote is claim 0 and the
        // basis claim 1; alone, the basis is claim 0. The verifier preamble names the basis by that
        // position. Assessor statements and accused quotes follow; each item's roles come from this
        // manifest, never from model output.
        bool isCriticalErrorAdjudication = IsCriticalErrorAdjudication(answer);
        string? outOfRubricBasis = OutOfRubricBasisOf(answer);
        bool isOutOfRubricAdjudication = outOfRubricBasis != null;
        var manifest = BuildClaimManifest(
            claims,
            isCriticalErrorAdjudication ? answer.CriticalErrorQuote : null,
            outOfRubricBasis,
            AccusedQuotesFor(answer),
            assessorStatements,
            answer.AnswerText);
        claims = manifest.Select(m => m.Text).ToList();

        if (claims.Count == 0) return;

        // Recorded as a verification failure, which "retry failed claim verification" re-runs.
        try
        {
            BenchmarkBoardGuard.RequireBoardLoaded(run);
        }
        catch (InvalidOperationException ex)
        {
            answer.ClaimVerificationError = BenchmarkAssessmentFailure.Truncate(ex.Message, BenchmarkAssessmentFailure.MaxClaimVerificationErrorLength);
            _logger.LogWarning(
                "Benchmark run {RunId} answer {OrderIndex}: claim verification failed ({Error}).",
                run.Id, answer.OrderIndex, ex.Message);
            await db.SaveChangesAsync(CancellationToken.None);
            return;
        }

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

        var toolCallLeads = await LoadToolCallLeadsAsync(db, answer.Id, allowedTools, cancellationToken);

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
            assessorEvidence: accuracyEvidence,
            boardName: run.BenchmarkSuite?.GameSnapshot?.Name,
            boardText: run.BenchmarkSuite?.GameSnapshot?.SanitizedText,
            criticalErrorQuoteContext: isCriticalErrorAdjudication
                ? BenchmarkClaimVerificationPrompt.CriticalErrorQuoteContext(answer.AnswerText, answer.CriticalErrorQuote)
                : null,
            claimRoles: manifest.Select(m => m.Roles).ToList(),
            claimContexts: manifest.Select(m => m.Context).ToList(),
            toolCallLeads: toolCallLeads,
            claimCharges: manifest.Select(m => m.Charge).ToList(),
            claimChargedParts: manifest.Select(m => m.QuotedFragments).ToList());

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
            // A request that would not carry the board ahead of the question is a verification
            // failure, re-runnable, and nothing is sent. The board figure is recorded only once the
            // probe passed.
            VerifyClaimVerificationDelivery(runRequest, run, answer.OrderIndex);
            answer.VerifierBoardChars = BenchmarkBoardGuard.BoardCharsSent(run);
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
        bool verdictsPersisted = false;

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
                    VerifyClaimVerificationDelivery(runRequest, run, answer.OrderIndex);
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

                // The liveness note is added before anything is derived from the verdicts, so every
                // count and flag below reads the demoted verdict.
                var verifications = StampRoles(parseResult.Verifications, manifest);
                verifications = AnnotateCitationLiveness(verifications);
                ApplyClaimVerificationOutcome(answer, verifications, isCriticalErrorAdjudication, outOfRubricBasis);

                verdictsPersisted = true;
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

        if (verdictsPersisted)
        {
            await RunEvidenceInformedRegradeAsync(db, configService, run, answer, expectedPoints, cancellationToken);
        }
    }

    /// <summary>
    /// Stores a parsed, role-stamped verification on the answer with its counts and the three flags
    /// derived from it, each read from <see cref="BenchmarkClaimVerification.EffectiveVerdict"/>.
    ///
    /// The out-of-rubric basis, the critical-error quote, an accused quote and an assessor statement
    /// are the assessor's statements or charges, not claims the answer left unverified, so their
    /// verdicts stay out of the answer's claim counts and the RefutedClaim flag; the counts then total
    /// the unverified claims the answer actually made, each once. All are kept in
    /// ClaimVerificationJson with their roles, where their citations are the record, and the
    /// contested-verdict decisions read that full list.
    /// </summary>
    internal static void ApplyClaimVerificationOutcome(
        BenchmarkRunAnswer answer,
        IReadOnlyList<BenchmarkClaimVerification> verifications,
        bool isCriticalErrorAdjudication,
        string? outOfRubricBasis)
    {
        var answerClaimVerifications = verifications.Where(BenchmarkClaimRoles.IsOrdinaryClaim).ToList();
        answer.ClaimsSupportedCount = answerClaimVerifications.Count(v => v.EffectiveVerdict == BenchmarkClaimVerdict.Supported);
        answer.ClaimsRefutedCount = answerClaimVerifications.Count(v => v.EffectiveVerdict == BenchmarkClaimVerdict.Refuted);
        answer.ClaimsIndeterminateCount = answerClaimVerifications.Count(v => v.EffectiveVerdict == BenchmarkClaimVerdict.Indeterminate);
        answer.ClaimVerificationJson = JsonSerializer.Serialize(verifications);

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
            CriticalErrorQuoteWasSupported(verifications, answer.CriticalErrorQuote))
        {
            answer.AnswerFlags |= (int)BenchmarkAnswerFlags.ContestedCriticalError;
        }
        else
        {
            answer.AnswerFlags &= ~(int)BenchmarkAnswerFlags.ContestedCriticalError;
        }

        // The statement the out-of-rubric Accuracy deduction rests on was checked against the
        // source and refuted, a sentence the assessor charged as false was checked and supported
        // with a citation, a statement of the assessor's own evidence was refuted with one, or a
        // "Suspected false:" sentence the Accuracy evidence docks was supported with one.
        // Advisory in the same way: the deduction stays, no index moves, and the flag says the
        // deduction is contested. A refuted accused sentence, or a supported or indeterminate
        // assessor statement, leaves the deduction standing and adds no refutation of the answer.
        // Cleared otherwise, so a re-verification cannot leave a stale flag.
        if ((outOfRubricBasis != null && OutOfRubricBasisWasRefuted(verifications, outOfRubricBasis))
            || SupportedAccusations(verifications).Count > 0
            || RefutedAssessorStatements(verifications).Count > 0
            || SupportedDockedSuspicions(answer, verifications).Count > 0)
        {
            answer.AnswerFlags |= (int)BenchmarkAnswerFlags.ContestedAccuracyDeduction;
        }
        else
        {
            answer.AnswerFlags &= ~(int)BenchmarkAnswerFlags.ContestedAccuracyDeduction;
        }
    }

    /// <summary>
    /// The verifications with a <see cref="BenchmarkClaimVerification.CitationNote"/> where the cited
    /// GnollHack function has no live call site; unchanged when the source index is unavailable.
    /// </summary>
    private List<BenchmarkClaimVerification> AnnotateCitationLiveness(List<BenchmarkClaimVerification> verifications)
    {
        try
        {
            if (_citationLivenessCheck == null)
            {
                using var scope = _scopeFactory.CreateScope();
                var sourceCode = scope.ServiceProvider.GetService<SourceCodeService>();
                if (sourceCode == null) return verifications;

                int maxFileSizeKB = int.TryParse(_configuration["MaxSourceFileSizeKB"], out int kb) ? kb : 800;
                _citationLivenessCheck = BenchmarkCitationLivenessCheck.ForSourceCodeService(sourceCode, maxFileSizeKB);
            }

            return _citationLivenessCheck.Annotate(verifications);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Citation liveness check skipped.");
            return verifications;
        }
    }

    /// <summary>
    /// Drops what described the verdict a new one replaces: the evidence-informed re-grade that re-read
    /// it, and the claim verification of its quote, basis and accused sentences, with the counts and
    /// the three flags that verification set. The new verdict is then verified afresh, as a first
    /// grading is. Token and cost accounting of the discarded work is kept.
    /// </summary>
    internal static void ClearReplacedVerdictEvidence(BenchmarkRunAnswer answer)
    {
        answer.EvidenceInformedQualityScore = null;
        answer.EvidenceInformedCriticalError = null;
        answer.EvidenceInformedJson = null;

        answer.ClaimVerificationJson = null;
        answer.ClaimVerificationError = null;
        answer.ClaimVerificationRawText = null;
        answer.ClaimsSupportedCount = null;
        answer.ClaimsRefutedCount = null;
        answer.ClaimsIndeterminateCount = null;
        answer.AnswerFlags &= ~(int)(BenchmarkAnswerFlags.RefutedClaim
            | BenchmarkAnswerFlags.ContestedCriticalError
            | BenchmarkAnswerFlags.ContestedAccuracyDeduction);
    }

    /// <summary>
    /// The candidate's tool calls for one answer, as untrusted leads for the verifier. Loaded
    /// explicitly: the rows are in memory only when the answer was executed in the same pass, never on
    /// a retry, a re-assessment or a run-level sweep.
    /// </summary>
    internal static async Task<BenchmarkClaimVerificationPrompt.ToolCallLeads> LoadToolCallLeadsAsync(
        ApplicationDbContext db, long answerId, IReadOnlyList<string> allowedTools, CancellationToken cancellationToken)
    {
        var calls = await db.BenchmarkRunAnswerToolCalls
            .Where(tc => tc.BenchmarkRunAnswerId == answerId)
            .OrderBy(tc => tc.SortOrder)
            .Select(tc => new { tc.Name, tc.ArgsText })
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        return BenchmarkClaimVerificationPrompt.BuildToolCallLeads(calls.Select(c => (c.Name, c.ArgsText)), allowedTools);
    }

    /// <summary>
    /// The answer's grading was contested by the claim verifier: the statement an out-of-rubric
    /// Accuracy deduction rested on was refuted, the critical-error quote was supported, or the
    /// deduction is a verification-cleared one.
    /// </summary>
    internal static bool QualifiesForEvidenceInformedRegrade(BenchmarkRunAnswer answer)
    {
        var flags = (BenchmarkAnswerFlags)answer.AnswerFlags;
        return flags.HasFlag(BenchmarkAnswerFlags.ContestedAccuracyDeduction)
            || flags.HasFlag(BenchmarkAnswerFlags.ContestedCriticalError)
            || IsVerificationClearedAccuracyDeduction(answer);
    }

    /// <summary>
    /// Accuracy was docked with no defect named while the answer's out-of-rubric claims were all
    /// checked and supported — none refuted, none left indeterminate. The report's
    /// Verification-cleared Accuracy deductions are exactly these answers.
    /// </summary>
    internal static bool IsVerificationClearedAccuracyDeduction(BenchmarkRunAnswer a)
        => ((BenchmarkAnswerFlags)a.AnswerFlags).HasFlag(BenchmarkAnswerFlags.UnevidencedDeduction)
            && (a.UnverifiedClaimCount ?? 0) > 0
            && (a.ClaimsRefutedCount ?? 0) == 0
            && (a.ClaimsIndeterminateCount ?? 0) == 0
            && a.AccuracyLevel.HasValue
            && a.AccuracyLevel.Value <= BenchmarkVerdictConsistency.UnevidencedDeductionMaxLevel;

    /// <summary>
    /// Re-grades a contested answer once with the run's primary assessor, the claim verifier's
    /// findings in hand, and stores the verdict in the <c>EvidenceInformed*</c> columns. Advisory:
    /// no stored level, score, cap, flag or index changes, and the second-opinion columns are not
    /// touched. Cost is pooled into the answer's assessment fields. An unusable verdict, a timeout
    /// or an exception is logged and leaves the columns null; it never fails the answer or the run.
    /// A reply that parses without a <c>withdrawn</c> array gets one repair turn unless
    /// <c>Benchmark:EvidenceInformedRegrade:ParseRetryEnabled</c> is false, and the stored record
    /// says so. Off when <c>Benchmark:EvidenceInformedRegrade:Enabled</c> is false.
    /// </summary>
    private async Task RunEvidenceInformedRegradeAsync(
        ApplicationDbContext db,
        SystemAiConfigService configService,
        BenchmarkRun run,
        BenchmarkRunAnswer answer,
        string? expectedPoints,
        CancellationToken cancellationToken)
    {
        if (!_configuration.GetValue<bool>("Benchmark:EvidenceInformedRegrade:Enabled", true)) return;
        if (!QualifiesForEvidenceInformedRegrade(answer)) return;
        if (!run.AssessorModelConfigurationId.HasValue || !answer.QualityScore.HasValue) return;

        try
        {
            BenchmarkBoardGuard.RequireBoardLoaded(run);

            var verifications = JsonSerializer.Deserialize<List<BenchmarkClaimVerification>>(
                answer.ClaimVerificationJson ?? "[]",
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<BenchmarkClaimVerification>();
            if (verifications.Count == 0) return;

            // Nothing a finding can bear on means nothing a re-grade may withdraw.
            var targets = BuildEvidenceInformedTargets(answer, verifications);
            if (targets.Count == 0)
            {
                _logger.LogInformation(
                    "Benchmark run {RunId} answer {OrderIndex}: evidence-informed re-grade not run — no deduction a finding bears on.",
                    run.Id, answer.OrderIndex);
                return;
            }

            var (assessorConfig, assessorApiKey, resolveError) = await ResolveAssessorAsync(
                db, run, run.AssessorModelConfigurationId.Value, cancellationToken);
            if (assessorConfig == null || assessorApiKey == null)
            {
                _logger.LogWarning(
                    "Benchmark run {RunId} answer {OrderIndex}: evidence-informed re-grade skipped — assessor configuration unusable ({Error}).",
                    run.Id, answer.OrderIndex, resolveError);
                return;
            }

            var allowedTools = _configuration.GetSection("Benchmark:AllowedTools").Get<List<string>>() ?? _defaultAllowedTools;
            string prompt = BenchmarkAssessmentPrompt.BuildEvidenceInformedBody(
                answer.OrderIndex,
                answer.QuestionText,
                answer.Difficulty,
                expectedPoints,
                answer.AnswerText,
                answer.Status,
                verifications,
                criticalErrorQuote: answer.CriticalError ? answer.CriticalErrorQuote : null,
                outOfRubricBasis: OutOfRubricBasisOf(answer),
                allowedTools: allowedTools,
                toolCallsCompleted: answer.ToolCallCount ?? 0,
                toolBudgetExhausted: answer.ToolBudgetExhausted,
                scrubbedArtifactCount: answer.ScrubbedArtifactCount,
                toolCallBudget: answer.ToolCallBudgetUsed,
                boardGivenAbove: GradingBoardBlock(run) != null,
                targets: targets,
                originalLevels: (answer.AccuracyLevel ?? 0, answer.CompletenessLevel ?? 0, answer.ConcisenessLevel ?? 0, answer.ReadabilityLevel ?? 0),
                originalCriticalError: answer.CriticalError);

            // The one timeout a grading call already has; the primary assessment itself runs unbounded.
            // It bounds both turns of a repaired re-grade together.
            int timeoutSeconds = _configuration.GetValue<int>("Benchmark:SecondOpinion:TimeoutSeconds", 900);
            bool repairEnabled = _configuration.GetValue<bool>("Benchmark:EvidenceInformedRegrade:ParseRetryEnabled", true);
            using var regradeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            regradeCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

            AssessorVerdict verdict;
            EvidenceInformedValidation? validation = null;
            EvidenceInformedRepair? repair = null;
            try
            {
                var runRequest = BuildAssessorRequest(run, prompt, assessorConfig, assessorApiKey);
                var firstTurn = await RunAssessorTurnAsync(run, answer, runRequest, regradeCts.Token);
                verdict = ToAssessorVerdict(run, answer, firstTurn);
                if (verdict.Result != null)
                {
                    validation = ValidateEvidenceInformed(
                        verdict.RawText, targets, answer.AccuracyLevel ?? 0, answer.CriticalError,
                        verdict.Result.AccuracyLevel, verdict.Result.CriticalError);
                }

                // One repair turn, for a missing or non-array `withdrawn` only: every other failure
                // is a judgement the re-grade made and stands as its verdict. The request's budget of
                // two model calls fits exactly this one.
                if (repairEnabled && validation != null && NeedsWithdrawnRepair(validation))
                {
                    _logger.LogWarning(
                        "Benchmark run {RunId} answer {OrderIndex}: evidence-informed re-grade returned no `withdrawn` list. Repairing once...",
                        run.Id, answer.OrderIndex);
                    runRequest.SeedHistory.Add(new { role = "assistant", content = firstTurn.FinalText ?? string.Empty });
                    runRequest.SeedHistory.Add(new { role = "user", content = WithdrawnRepairMessage });

                    var repairTurn = await RunAssessorTurnAsync(run, answer, runRequest, regradeCts.Token);
                    var repaired = ToAssessorVerdict(run, answer, repairTurn);
                    var firstErrors = validation.Errors;
                    if (repaired.Result != null)
                    {
                        validation = ValidateEvidenceInformed(
                            repaired.RawText, targets, answer.AccuracyLevel ?? 0, answer.CriticalError,
                            repaired.Result.AccuracyLevel, repaired.Result.CriticalError);
                    }

                    repair = new EvidenceInformedRepair(
                        firstErrors, repaired.Result != null, repaired.Error,
                        repaired.InputTokens, repaired.OutputTokens, repaired.DurationMs);

                    // The repaired verdict replaces the first only when it parsed; the cost is both turns'.
                    verdict = (repaired.Result != null ? repaired : verdict) with
                    {
                        InputTokens = verdict.InputTokens + repaired.InputTokens,
                        OutputTokens = verdict.OutputTokens + repaired.OutputTokens,
                        DurationMs = verdict.DurationMs + repaired.DurationMs,
                        CacheReadTokens = verdict.CacheReadTokens + repaired.CacheReadTokens,
                        CacheCreationTokens = verdict.CacheCreationTokens + repaired.CacheCreationTokens
                    };
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(
                    "Benchmark run {RunId} answer {OrderIndex}: evidence-informed re-grade timed out ({Timeout} s).",
                    run.Id, answer.OrderIndex, timeoutSeconds);
                return;
            }

            answer.AssessmentInputTokens = (answer.AssessmentInputTokens ?? 0) + verdict.InputTokens;
            answer.AssessmentOutputTokens = (answer.AssessmentOutputTokens ?? 0) + verdict.OutputTokens;
            answer.AssessmentCacheReadTokens = (answer.AssessmentCacheReadTokens ?? 0) + verdict.CacheReadTokens;
            answer.AssessmentCacheCreationTokens = (answer.AssessmentCacheCreationTokens ?? 0) + verdict.CacheCreationTokens;
            answer.AssessmentDurationMs = (answer.AssessmentDurationMs ?? 0) + verdict.DurationMs;
            if (verdict.BoardChars.HasValue)
            {
                answer.AssessorBoardChars = verdict.BoardChars;
            }

            try
            {
                await configService.RecordUsageAsync(
                    assessorConfig.Id, run.StartedByUserId, verdict.InputTokens, verdict.OutputTokens,
                    roleContext: 4,
                    cacheReadTokens: verdict.CacheReadTokens,
                    cacheCreationTokens: verdict.CacheCreationTokens,
                    totalDurationMs: (int)verdict.DurationMs);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to record usage for an evidence-informed re-grade.");
            }

            if (verdict.Result == null)
            {
                _logger.LogWarning(
                    "Benchmark run {RunId} answer {OrderIndex}: evidence-informed re-grade produced no usable verdict ({Error}).",
                    run.Id, answer.OrderIndex, verdict.Error);
                await db.SaveChangesAsync(CancellationToken.None);
                return;
            }

            // This pass adjudicates Accuracy and the critical error only, so the advisory quality keeps
            // the primary's other three levels, scored with the constants the run was scored with.
            var res = verdict.Result;
            var constants = BenchmarkScoring.ConstantsFromSnapshot(run.ScoringProfileSnapshotJson);
            var (quality, _, _) = BenchmarkScoring.Quality(
                res.AccuracyLevel,
                answer.CompletenessLevel ?? res.CompletenessLevel,
                answer.ConcisenessLevel ?? res.ConcisenessLevel,
                answer.ReadabilityLevel ?? res.ReadabilityLevel,
                res.CriticalError, constants);
            validation ??= ValidateEvidenceInformed(
                verdict.RawText, targets, answer.AccuracyLevel ?? 0, answer.CriticalError, res.AccuracyLevel, res.CriticalError);

            answer.EvidenceInformedQualityScore = quality;
            answer.EvidenceInformedCriticalError = res.CriticalError;
            answer.EvidenceInformedJson = JsonSerializer.Serialize(new
            {
                assessor = assessorConfig.DisplayName ?? assessorConfig.ModelId,
                provider = assessorConfig.Provider,
                modelId = assessorConfig.ModelId,
                assessedAtUtc = DateTime.UtcNow,
                accuracyLevel = res.AccuracyLevel,
                completenessLevel = res.CompletenessLevel,
                concisenessLevel = res.ConcisenessLevel,
                readabilityLevel = res.ReadabilityLevel,
                criticalError = res.CriticalError,
                criticalErrorQuote = res.CriticalErrorQuote,
                qualityScore = quality,
                comment = res.Comment,
                accuracyEvidence = res.AccuracyEvidence,
                completenessEvidence = res.CompletenessEvidence,
                withdrawn = validation.Accepted.Select(a => a.Summary).ToList(),
                validationVersion = EvidenceInformedValidationVersion,
                targets = targets.Select(t => new { id = t.Id, kind = t.Kind, source = t.Source, text = t.Text, findingIds = t.FindingIds }).ToList(),
                accepted = validation.Accepted.Select(a => new { targetId = a.TargetId, kind = a.Kind, findingIds = a.FindingIds, reason = a.Reason }).ToList(),
                withdrawnDropped = validation.Dropped,
                validationErrors = validation.Errors,
                eligibleForSensitivity = validation.Eligible,
                repairTurns = repair == null ? 0 : 1,
                repair = repair == null ? null : new
                {
                    reason = WithdrawnMissingError,
                    firstTurnValidationErrors = repair.FirstTurnErrors,
                    parsed = repair.Parsed,
                    error = repair.Error,
                    inputTokens = repair.InputTokens,
                    outputTokens = repair.OutputTokens,
                    durationMs = repair.DurationMs
                }
            });

            await db.SaveChangesAsync(CancellationToken.None);

            _logger.LogInformation(
                "Benchmark run {RunId} answer {OrderIndex}: evidence-informed re-grade {Regrade} against the scored {Scored}, withdrew {Withdrawn} item(s), {Status}. Score unchanged.",
                run.Id, answer.OrderIndex, quality, answer.QualityScore.Value, validation.Accepted.Count,
                validation.Eligible ? "validated" : $"rejected ({string.Join("; ", validation.Errors)})");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Benchmark run {RunId} answer {OrderIndex}: evidence-informed re-grade failed. The verdict stands.",
                run.Id, answer.OrderIndex);
        }
    }

    /// <summary>
    /// The <c>withdrawn</c> list of an evidence-informed verdict's raw text; empty when the field
    /// is absent or malformed.
    /// </summary>
    internal static List<string> ReadWithdrawn(string? rawText)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(rawText)) return result;

        try
        {
            using var doc = JsonDocument.Parse(BenchmarkJsonExtractor.Extract(rawText));
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("withdrawn", out var list) &&
                list.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in list.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
                    {
                        result.Add(item.GetString()!.Trim());
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Advisory: a malformed list reads as empty.
        }

        return result;
    }

    /// <summary>The <c>withdrawn</c> list stored in <see cref="BenchmarkRunAnswer.EvidenceInformedJson"/>.</summary>
    internal static List<string> ReadEvidenceInformedWithdrawn(string? evidenceInformedJson)
        => ReadWithdrawn(evidenceInformedJson);

    /// <summary>The version of the withdrawal validation a stored re-grade carries; absent before harness 31.</summary>
    internal const int EvidenceInformedValidationVersion = 1;

    /// <summary>The first harness whose re-grades carry validation provenance.</summary>
    internal const int FirstValidatedRegradeHarness = 31;

    /// <summary>One withdrawal that passed validation, and the string stored for it in <c>withdrawn</c>.</summary>
    internal sealed record AcceptedWithdrawal(string TargetId, string Kind, IReadOnlyList<string> FindingIds, string Reason, string Summary);

    /// <summary>What the validator made of a re-grade: accepted withdrawals, dropped ones with reasons, and every error.</summary>
    internal sealed record EvidenceInformedValidation(
        IReadOnlyList<AcceptedWithdrawal> Accepted,
        IReadOnlyList<string> Dropped,
        IReadOnlyList<string> Errors)
    {
        public bool Eligible => Errors.Count == 0;
    }

    private static bool HasCitation(BenchmarkClaimVerification v) => !string.IsNullOrWhiteSpace(v.Citation);

    /// <summary>
    /// The deductions an evidence-informed re-grade may withdraw, each with the findings that bear on
    /// it: the critical error, when its quote was Supported with a citation; the out-of-rubric
    /// Accuracy deduction, when its basis was Refuted with a citation; one Accuracy target per
    /// accused sentence Supported with a citation; and one per assessor statement Refuted with a
    /// citation. Verdicts are read as <see cref="BenchmarkClaimVerification.EffectiveVerdict"/>. An
    /// ordinary Supported claim creates none. Finding
    /// ids are <c>F</c> + the stored claim index. A legacy record without roles is matched by text.
    /// </summary>
    internal static List<BenchmarkEvidenceInformedTarget> BuildEvidenceInformedTargets(
        BenchmarkRunAnswer answer,
        IReadOnlyList<BenchmarkClaimVerification> verifications)
    {
        var targets = new List<BenchmarkEvidenceInformedTarget>();
        bool withRoles = BenchmarkClaimRoles.HasRoles(verifications);
        string Next() => $"T{targets.Count + 1}";

        if (answer.CriticalError && !string.IsNullOrWhiteSpace(answer.CriticalErrorQuote))
        {
            string quote = answer.CriticalErrorQuote.Trim();
            var findings = verifications
                .Where(v => (withRoles
                        ? BenchmarkClaimRoles.HasRole(v, BenchmarkClaimRoles.CriticalErrorQuote)
                        : string.Equals(v.Claim?.Trim(), quote, StringComparison.Ordinal))
                    && v.EffectiveVerdict == BenchmarkClaimVerdict.Supported && HasCitation(v))
                .Select(v => $"F{v.ClaimIndex}")
                .ToList();
            if (findings.Count > 0)
            {
                targets.Add(new BenchmarkEvidenceInformedTarget(
                    Next(), BenchmarkEvidenceInformedTarget.CriticalErrorKind, BenchmarkClaimRoles.CriticalErrorQuote, quote, findings));
            }
        }

        string? basis = OutOfRubricBasisOf(answer);
        if (basis != null)
        {
            string trimmed = basis.Trim();
            var findings = verifications
                .Where(v => (withRoles
                        ? BenchmarkClaimRoles.HasRole(v, BenchmarkClaimRoles.OutOfRubricBasis)
                        : string.Equals(v.Claim?.Trim(), trimmed, StringComparison.Ordinal))
                    && v.EffectiveVerdict == BenchmarkClaimVerdict.Refuted && HasCitation(v))
                .Select(v => $"F{v.ClaimIndex}")
                .ToList();
            if (findings.Count > 0)
            {
                targets.Add(new BenchmarkEvidenceInformedTarget(
                    Next(), BenchmarkEvidenceInformedTarget.AccuracyKind, BenchmarkClaimRoles.OutOfRubricBasis, trimmed, findings));
            }
        }

        foreach (var accused in SupportedAccusations(verifications))
        {
            targets.Add(new BenchmarkEvidenceInformedTarget(
                Next(), BenchmarkEvidenceInformedTarget.AccuracyKind, BenchmarkClaimRoles.AccusedQuote,
                accused.Claim.Trim(), new[] { $"F{accused.ClaimIndex}" }));
        }

        foreach (var statement in RefutedAssessorStatements(verifications))
        {
            targets.Add(new BenchmarkEvidenceInformedTarget(
                Next(), BenchmarkEvidenceInformedTarget.AccuracyKind, BenchmarkClaimRoles.AssessorStatement,
                statement.Claim.Trim(), new[] { $"F{statement.ClaimIndex}" }));
        }

        return targets;
    }

    /// <summary>
    /// Validates an evidence-informed verdict's structured <c>withdrawn</c> list against the target
    /// catalog and the primary verdict. Every withdrawal must name a listed target once, with a
    /// reason, on findings from that target's own list. The re-grade may not lower Accuracy or add a
    /// critical error; it may remove the critical error or raise Accuracy only with a valid
    /// withdrawal of that kind. An empty list is valid only when both are unchanged. Separate from
    /// <see cref="ReadWithdrawn"/>, which keeps reading historical string arrays.
    /// </summary>
    internal static EvidenceInformedValidation ValidateEvidenceInformed(
        string? rawText,
        IReadOnlyList<BenchmarkEvidenceInformedTarget> targets,
        int primaryAccuracyLevel,
        bool primaryCriticalError,
        int regradeAccuracyLevel,
        bool regradeCriticalError)
    {
        var accepted = new List<AcceptedWithdrawal>();
        var dropped = new List<string>();
        var errors = new List<string>();
        var byId = targets.ToDictionary(t => t.Id, StringComparer.Ordinal);
        var seenTargets = new HashSet<string>(StringComparer.Ordinal);

        JsonElement list = default;
        bool haveList = false;
        if (!string.IsNullOrWhiteSpace(rawText))
        {
            try
            {
                using var doc = JsonDocument.Parse(BenchmarkJsonExtractor.Extract(rawText));
                if (doc.RootElement.ValueKind == JsonValueKind.Object
                    && doc.RootElement.TryGetProperty("withdrawn", out var w)
                    && w.ValueKind == JsonValueKind.Array)
                {
                    list = w.Clone();
                    haveList = true;
                }
            }
            catch (JsonException)
            {
                // Reported below as a missing list.
            }
        }

        if (!haveList)
        {
            errors.Add(WithdrawnMissingError);
        }
        else
        {
            int position = 0;
            foreach (var item in list.EnumerateArray())
            {
                position++;
                if (item.ValueKind != JsonValueKind.Object)
                {
                    string shown = item.ValueKind == JsonValueKind.String ? $"\"{item.GetString()}\"" : item.ValueKind.ToString();
                    Drop($"item {position} ({shown}) is not an object with a targetId.");
                    continue;
                }

                string? targetId = item.TryGetProperty("targetId", out var tid) && tid.ValueKind == JsonValueKind.String
                    ? tid.GetString()?.Trim()
                    : null;
                string? reason = item.TryGetProperty("reason", out var r) && r.ValueKind == JsonValueKind.String
                    ? r.GetString()?.Trim()
                    : null;
                var findingIds = new List<string>();
                if (item.TryGetProperty("findingIds", out var f) && f.ValueKind == JsonValueKind.Array)
                {
                    findingIds.AddRange(f.EnumerateArray()
                        .Where(x => x.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(x.GetString()))
                        .Select(x => x.GetString()!.Trim()));
                }

                if (string.IsNullOrEmpty(targetId) || !byId.TryGetValue(targetId, out var target))
                {
                    Drop($"item {position}: unknown targetId \"{targetId}\".");
                    continue;
                }
                if (!seenTargets.Add(targetId))
                {
                    Drop($"item {position}: duplicate targetId {targetId}.");
                    continue;
                }
                if (string.IsNullOrEmpty(reason))
                {
                    Drop($"item {position} ({targetId}): empty reason.");
                    continue;
                }
                if (findingIds.Count == 0)
                {
                    Drop($"item {position} ({targetId}): no findingIds.");
                    continue;
                }
                var foreign = findingIds.Where(id => !target.FindingIds.Contains(id, StringComparer.Ordinal)).ToList();
                if (foreign.Count > 0)
                {
                    Drop($"item {position} ({targetId}): finding(s) {string.Join(", ", foreign)} do not bear on this target.");
                    continue;
                }

                accepted.Add(new AcceptedWithdrawal(
                    targetId, target.Kind, findingIds, reason,
                    $"{targetId} {target.Kind}: \"{target.Text}\" ({string.Join(", ", findingIds)}) — {reason}"));
            }
        }

        bool accuracyWithdrawn = accepted.Any(a => a.Kind == BenchmarkEvidenceInformedTarget.AccuracyKind);
        bool criticalWithdrawn = accepted.Any(a => a.Kind == BenchmarkEvidenceInformedTarget.CriticalErrorKind);

        if (regradeAccuracyLevel < primaryAccuracyLevel)
        {
            errors.Add($"the re-grade lowered Accuracy from {primaryAccuracyLevel} to {regradeAccuracyLevel}.");
        }
        if (regradeCriticalError && !primaryCriticalError)
        {
            errors.Add("the re-grade added a critical error the primary verdict did not have.");
        }
        if (primaryCriticalError && !regradeCriticalError && !criticalWithdrawn)
        {
            errors.Add("the critical error was removed with no valid criticalError withdrawal.");
        }
        if (regradeAccuracyLevel > primaryAccuracyLevel && !accuracyWithdrawn)
        {
            errors.Add($"Accuracy was raised from {primaryAccuracyLevel} to {regradeAccuracyLevel} with no valid accuracy withdrawal.");
        }

        return new EvidenceInformedValidation(accepted, dropped, errors);

        void Drop(string message)
        {
            dropped.Add(message);
            errors.Add(message);
        }
    }

    /// <summary>The validation error for a re-grade whose JSON has no <c>withdrawn</c> array; the one error a repair turn follows.</summary>
    internal const string WithdrawnMissingError = "`withdrawn` is missing or not an array.";

    /// <summary>The user turn that asks a re-grade to return its verdict again with <c>withdrawn</c>.</summary>
    internal const string WithdrawnRepairMessage =
        "Your previous response could not be accepted: " + WithdrawnMissingError
        + " Output the complete JSON object again in the schema above, including `withdrawn` — use [] when you withdraw nothing. "
        + "Output ONLY the raw JSON object, without markdown wrapping, code fences or extra text.";

    /// <summary>
    /// Whether a re-grade earns its one repair turn: its JSON parsed but carried no <c>withdrawn</c>
    /// array. Level checks that fail beside it are made again against the repaired verdict.
    /// </summary>
    internal static bool NeedsWithdrawnRepair(EvidenceInformedValidation validation)
        => validation.Errors.Contains(WithdrawnMissingError, StringComparer.Ordinal);

    /// <summary>
    /// The repair turn of an evidence-informed re-grade: the first turn's validation errors, whether
    /// the repaired reply parsed, its error, and what the turn cost.
    /// </summary>
    internal sealed record EvidenceInformedRepair(
        IReadOnlyList<string> FirstTurnErrors,
        bool Parsed,
        string? Error,
        int InputTokens,
        int OutputTokens,
        long DurationMs);

    /// <summary>Whether the re-grade stored in <paramref name="evidenceInformedJson"/> carries validation provenance.</summary>
    internal static bool HasEvidenceInformedValidation(string? evidenceInformedJson)
        => ReadEvidenceInformedBool(evidenceInformedJson, "validationVersion", out _, requireNumber: true);

    /// <summary>
    /// A re-grade stored before validation existed: a run stamped below
    /// <see cref="FirstValidatedRegradeHarness"/> whose record carries no validation provenance. Its
    /// sensitivity is computed as it always was and labelled legacy, unvalidated.
    /// </summary>
    internal static bool IsLegacyEvidenceInformed(BenchmarkRun run, BenchmarkRunAnswer answer)
        => answer.EvidenceInformedQualityScore.HasValue
            && !HasEvidenceInformedValidation(answer.EvidenceInformedJson)
            && !IsValidatedRegradeHarness(run.HarnessVersion);

    internal static bool IsValidatedRegradeHarness(string? harnessVersion)
        => int.TryParse(harnessVersion, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int v)
            && v >= FirstValidatedRegradeHarness;

    /// <summary>
    /// The one predicate every consumer of a re-grade uses — the eligible count, the substitution in
    /// the sensitivity figure and the synthesis input. A validated record counts when it passed; a
    /// record without provenance counts only as a legacy one, on a run stamped before harness 31.
    /// </summary>
    internal static bool IsEligibleEvidenceInformedRegrade(BenchmarkRun run, BenchmarkRunAnswer answer)
    {
        if (!answer.EvidenceInformedQualityScore.HasValue) return false;
        if (HasEvidenceInformedValidation(answer.EvidenceInformedJson))
        {
            return ReadEvidenceInformedBool(answer.EvidenceInformedJson, "eligibleForSensitivity", out bool eligible, requireNumber: false)
                && eligible;
        }
        return !IsValidatedRegradeHarness(run.HarnessVersion);
    }

    private static bool ReadEvidenceInformedBool(string? json, string property, out bool value, bool requireNumber)
    {
        value = false;
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty(property, out var p))
            {
                return false;
            }
            if (requireNumber)
            {
                return p.ValueKind == JsonValueKind.Number;
            }
            if (p.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                value = p.GetBoolean();
                return true;
            }
            return false;
        }
        catch (JsonException)
        {
            return false;
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

        var run = await CalibrationRunQuery(db)
            .FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);

        if (run == null)
        {
            _logger.LogWarning("Benchmark run {RunId} not found for assessor calibration.", runId);
            return;
        }

        // A calibration compares the stored verdicts with ones graded under this build's anchors,
        // which is meaningless across a scoring method.
        if (!IsCurrentScoringMethod(run))
        {
            _logger.LogWarning("Assessor calibration of benchmark run {RunId} refused: {Reason}", runId, ScoringMethodRefusal(run));
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
        string? Error,
        int? BoardChars = null,
        string? RawText = null,
        int CacheReadTokens = 0,
        int CacheCreationTokens = 0);

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
        BenchmarkBoardGuard.RequireBoardLoaded(run);

        var allowedTools = _configuration.GetSection("Benchmark:AllowedTools").Get<List<string>>() ?? _defaultAllowedTools;
        string prompt = BenchmarkAssessmentPrompt.BuildPerQuestionBody(
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
            boardGivenAbove: GradingBoardBlock(run) != null);
        return await RunAssessorPromptAsync(run, answer, prompt, assessorConfig, assessorApiKey, cancellationToken);
    }

    /// <summary>
    /// Sends one per-question assessor body with the shared preamble and the board ahead of it, and
    /// parses the verdict, writing nothing. <see cref="AssessorVerdict.BoardChars"/> is the board's
    /// length when the delivery probe confirmed the request carried it, and null otherwise or
    /// without a board.
    /// </summary>
    private async Task<AssessorVerdict> RunAssessorPromptAsync(
        BenchmarkRun run,
        BenchmarkRunAnswer answer,
        string prompt,
        SystemAiApiConfiguration assessorConfig,
        string assessorApiKey,
        CancellationToken cancellationToken)
    {
        var runRequest = BuildAssessorRequest(run, prompt, assessorConfig, assessorApiKey);
        var turn = await RunAssessorTurnAsync(run, answer, runRequest, cancellationToken);
        return ToAssessorVerdict(run, answer, turn);
    }

    /// <summary>One sent (or probe-refused) grading turn: the model's raw text, the terminal error, and what it cost.</summary>
    private sealed record AssessorTurn(
        string? FinalText,
        string? Error,
        int InputTokens,
        int OutputTokens,
        int CacheReadTokens,
        int CacheCreationTokens,
        long DurationMs,
        bool DeliveryVerified);

    /// <summary>The parsed verdict of one turn, with that turn's cost.</summary>
    private static AssessorVerdict ToAssessorVerdict(BenchmarkRun run, BenchmarkRunAnswer answer, AssessorTurn turn)
    {
        var parseResult = string.IsNullOrWhiteSpace(turn.Error)
            ? BenchmarkAssessmentParser.ParsePerQuestion(turn.FinalText, answer.AnswerText)
            : new PerQuestionAssessmentParseResult { Success = false, ErrorMessage = turn.Error };

        return new AssessorVerdict(
            parseResult.Success ? parseResult.Result : null,
            turn.InputTokens,
            turn.OutputTokens,
            turn.DurationMs,
            turn.Error ?? parseResult.ErrorMessage,
            turn.DeliveryVerified ? BenchmarkBoardGuard.BoardCharsSent(run) : null,
            parseResult.RawText ?? turn.FinalText,
            turn.CacheReadTokens,
            turn.CacheCreationTokens);
    }

    /// <summary>
    /// Probes <paramref name="runRequest"/> and, when it carries the instructions, the board and the
    /// question in that order, sends it once. Writes nothing. A refused probe is the turn's error, and
    /// nothing reaches the provider.
    /// </summary>
    private async Task<AssessorTurn> RunAssessorTurnAsync(
        BenchmarkRun run,
        BenchmarkRunAnswer answer,
        AgentRunRequest runRequest,
        CancellationToken cancellationToken)
    {
        var runResult = new AgentRunResult();
        var sw = Stopwatch.StartNew();
        string? terminalError = null;
        bool deliveryVerified = false;
        try
        {
            VerifyPerQuestionGradingDelivery(runRequest, "assessor", run, answer.OrderIndex);
            deliveryVerified = true;
            await foreach (var evt in _agentLoopRunner.RunAsync(runRequest, runRequest.Budget, runResult, cancellationToken))
            {
                if (evt.Type == "error") terminalError = evt.Data?.ToString();
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { terminalError = ex.Message; }

        sw.Stop();

        return new AssessorTurn(
            runResult.FinalText,
            terminalError,
            runResult.TotalPromptTokens > 0 ? runResult.TotalPromptTokens : runResult.EstimatedInputTokens,
            runResult.OutputTokens > 0 ? runResult.OutputTokens : runResult.EstimatedOutputTokens,
            runResult.CacheReadTokens,
            runResult.CacheCreationTokens,
            sw.ElapsedMilliseconds,
            deliveryVerified);
    }

    /// <summary>
    /// The primary assessor's single-shot grading request for one per-question body: the grading
    /// instructions, the board block when the suite has one, then <paramref name="prompt"/>. Its
    /// budget of two model calls leaves room for exactly one repair turn.
    /// </summary>
    private AgentRunRequest BuildAssessorRequest(
        BenchmarkRun run,
        string prompt,
        SystemAiApiConfiguration assessorConfig,
        string assessorApiKey)
    {
        var (gradingPrompt, gradingSeedHistory) = BuildGradingPrompt(
            GradingSystemPrompt,
            BenchmarkAssessmentPrompt.BuildPerQuestionPreamble(run.SuiteName),
            prompt,
            GradingBoardBlock(run));

        int assessorMaxTokens = _configuration.GetValue<int>("Benchmark:AssessorMaxOutputTokens", 32000);

        return new AgentRunRequest
        {
            ProviderName = assessorConfig.Provider,
            ModelId = assessorConfig.ModelId,
            ApiKey = assessorApiKey,
            ModelDisplayName = assessorConfig.DisplayName,
            SystemPrompt = gradingPrompt.FullPrompt,
            SegmentedPrompt = gradingPrompt,
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
            CacheConversationTail = false,
            Budget = new AgentRunBudget { MaxTotalModelCalls = 2 },
            ToolExecutionContext = new Tools.ToolExecutionContext
            {
                SessionId = Overseer.Services.Privacy.SessionRef.Persistent(run.Id),
                UserId = run.StartedByUserId ?? string.Empty,
                ShowDebugLog = false
            },
            SeedHistory = gradingSeedHistory
        };
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
        answer.SecondOpinionBoardChars = verdict.BoardChars;
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
        public const string DimensionOutlier = "DimensionOutlier";
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

        try
        {
            BenchmarkBoardGuard.RequireBoardLoaded(run);
        }
        catch (InvalidOperationException ex)
        {
            answer.SecondOpinionError = BenchmarkAssessmentFailure.Truncate(ex.Message);
            _logger.LogWarning(
                "Benchmark run {RunId} answer {OrderIndex}: second opinion unavailable ({Error}). The first verdict stands.",
                run.Id, answer.OrderIndex, ex.Message);
            await db.SaveChangesAsync(CancellationToken.None);
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

        // The out-of-rubric basis, the critical-error quote, an accused sentence and an assessor
        // statement are the first assessor's statements or charges, not claims the answer left
        // unverified, and this context is presented to the second reader as claims from the
        // candidate answer: a blind reader gets no assessor accusation and no assessor quote. A
        // legacy list without roles strips the basis only, as it always did.
        if (claimVerifications != null)
        {
            claimVerifications = BenchmarkClaimRoles.HasRoles(claimVerifications)
                ? claimVerifications.Where(BenchmarkClaimRoles.IsOrdinaryClaim).ToList()
                : WithoutOutOfRubricBasis(claimVerifications, OutOfRubricBasisOf(answer));
        }

        var allowedTools = _configuration.GetSection("Benchmark:AllowedTools").Get<List<string>>() ?? _defaultAllowedTools;
        string prompt = BenchmarkAssessmentPrompt.BuildSecondOpinionBody(
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
            boardGivenAbove: GradingBoardBlock(run) != null,
            blind: blind,
            triggerLabel: trigger,
            claimVerifications: claimVerifications);
        var (gradingPrompt, gradingSeedHistory) = BuildGradingPrompt(
            GradingSystemPrompt,
            BenchmarkAssessmentPrompt.BuildPerQuestionPreamble(run.SuiteName),
            prompt,
            GradingBoardBlock(run));

        int assessorMaxTokens = _configuration.GetValue<int>("Benchmark:AssessorMaxOutputTokens", 32000);

        var runRequest = new AgentRunRequest
        {
            ProviderName = secondConfig.Provider,
            ModelId = secondConfig.ModelId,
            ApiKey = secondApiKey,
            ModelDisplayName = secondConfig.DisplayName,
            SystemPrompt = gradingPrompt.FullPrompt,
            SegmentedPrompt = gradingPrompt,
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
            CacheConversationTail = false,
            Budget = new AgentRunBudget { MaxTotalModelCalls = 2 },
            ToolExecutionContext = new Tools.ToolExecutionContext
            {
                SessionId = Overseer.Services.Privacy.SessionRef.Persistent(run.Id),
                UserId = run.StartedByUserId ?? string.Empty,
                ShowDebugLog = false
            },
            SeedHistory = gradingSeedHistory
        };

        int timeoutSeconds = _configuration.GetValue<int>("Benchmark:SecondOpinion:TimeoutSeconds", 900);
        bool retryEnabled = _configuration.GetValue<bool>("Benchmark:SecondOpinion:ParseRetryEnabled", true);

        using var opinionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        opinionCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        // Every turn, the retry included, is probed first; a refused probe is that turn's error and
        // records the second opinion as unavailable.
        async Task<string?> RunOpinionTurnAsync(AgentRunResult result)
        {
            string? error = null;
            try
            {
                VerifyPerQuestionGradingDelivery(runRequest, "second opinion", run, answer.OrderIndex);
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
        // A parsed verdict came from a turn whose delivery probe passed.
        answer.SecondOpinionBoardChars = BenchmarkBoardGuard.BoardCharsSent(run);
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

    /// <summary>Length a verifier-supported claim is cut to for the synthesis prompt.</summary>
    internal const int SupportedClaimMaxLength = 300;

    /// <summary>Verifier-supported claims carried into the synthesis prompt per answer.</summary>
    internal const int SupportedClaimsPerAnswer = 6;

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
            var supportedList = new List<string>();
            var supportedAccusations = new List<(string Claim, string? Citation)>();
            var refutedAssessorStatements = new List<string>();
            bool basisRefuted = false;
            if (!string.IsNullOrWhiteSpace(a.ClaimVerificationJson))
            {
                try
                {
                    var verifications = JsonSerializer.Deserialize<List<BenchmarkClaimVerification>>(
                        a.ClaimVerificationJson,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (verifications != null)
                    {
                        // The assessor's own statements and charges — the out-of-rubric basis, the
                        // critical-error quote, an accused sentence and an assessor statement — are
                        // excluded from both lists: a verdict on any of them is a statement about the
                        // grading, and the synthesis prints those separately.
                        var ownClaims = OrdinaryClaimVerifications(verifications, a);
                        basisRefuted = OutOfRubricBasisWasRefuted(verifications, outOfRubricBasis);
                        // A docked "Suspected false:" sentence the verifier supported is, to the
                        // synthesis, a sentence the assessor charged as false and the source bore out.
                        supportedAccusations.AddRange(SupportedAccusations(verifications)
                            .Concat(SupportedDockedSuspicions(a, verifications))
                            .Select(v => (Claim: v.Claim.Trim(), v.Citation))
                            .DistinctBy(x => x.Claim, StringComparer.Ordinal));
                        refutedAssessorStatements.AddRange(RefutedAssessorStatements(verifications)
                            .Select(v => v.Claim.Trim()));

                        foreach (var v in ownClaims.Where(x => x.EffectiveVerdict == BenchmarkClaimVerdict.Refuted))
                        {
                            refutedList.Add((v.Claim, v.Citation, v.Basis));
                        }

                        foreach (var v in ownClaims
                                     .Where(x => x.EffectiveVerdict == BenchmarkClaimVerdict.Supported
                                                 && !string.IsNullOrWhiteSpace(x.Claim))
                                     .Take(SupportedClaimsPerAnswer))
                        {
                            string claim = v.Claim.Trim();
                            if (claim.Length > SupportedClaimMaxLength)
                            {
                                claim = claim.Substring(0, SupportedClaimMaxLength).TrimEnd();
                            }

                            supportedList.Add(claim);
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
                SupportedClaims = supportedList,
                ContestedCriticalErrorQuotes =
                    (((BenchmarkAnswerFlags)a.AnswerFlags) & BenchmarkAnswerFlags.ContestedCriticalError) != 0
                     && !string.IsNullOrWhiteSpace(a.CriticalErrorQuote)
                        ? new[] { a.CriticalErrorQuote!.Trim() }
                        : Array.Empty<string>(),
                // A refuted out-of-rubric basis or assessor statement only; a supported accusation
                // sets the same flag and is listed on its own.
                ContestedAccuracyDeductionBases =
                    (((BenchmarkAnswerFlags)a.AnswerFlags) & BenchmarkAnswerFlags.ContestedAccuracyDeduction) != 0
                        ? (outOfRubricBasis != null && basisRefuted ? new[] { outOfRubricBasis } : Array.Empty<string>())
                            .Concat(refutedAssessorStatements)
                            .Distinct(StringComparer.Ordinal)
                            .ToArray()
                        : Array.Empty<string>(),
                SupportedAccusations = supportedAccusations,
                // A rejected or unprovenanced re-grade never reaches the synthesis as a withdrawal.
                EvidenceInformedQualityScore = IsEligibleEvidenceInformedRegrade(run, a) ? a.EvidenceInformedQualityScore : null,
                EvidenceInformedCriticalError = IsEligibleEvidenceInformedRegrade(run, a) ? a.EvidenceInformedCriticalError : null,
                EvidenceInformedWithdrawn = IsEligibleEvidenceInformedRegrade(run, a)
                    ? ReadEvidenceInformedWithdrawn(a.EvidenceInformedJson)
                    : Array.Empty<string>(),
                SecondOpinionQualityScore = a.SecondOpinionQualityScore,
                SecondOpinionCriticalError = a.SecondOpinionCriticalError,
                ReviewComment = a.ReviewComment,
                Status = a.Status
            };
        }).ToList();

        string? boardName = null;
        string? boardDigest = null;
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

            // Queried rather than read off run.BenchmarkSuite: not every caller loads the suite.
            var board = await db.BenchmarkSuites
                .Where(s => s.Id == run.BenchmarkSuiteId.Value && s.GameSnapshot != null)
                .Select(s => new { s.GameSnapshot!.Name, s.GameSnapshot.DigestText })
                .FirstOrDefaultAsync(cancellationToken);
            boardName = board?.Name;
            boardDigest = board?.DigestText;
        }

        string synthesisPrompt = BenchmarkAssessmentPrompt.BuildFinalSynthesisPrompt(run.SuiteName, summaries, boardName, boardDigest);
        // No board block: the synthesis reads the board's digest inside its prompt, never the whole board.
        var (gradingPrompt, gradingSeedHistory) = BuildGradingPrompt(
            "You are an objective AI benchmark evaluator synthesizing a final report. Strictly adhere to the requested JSON response format.",
            null,
            synthesisPrompt);

        int assessorMaxTokens = _configuration.GetValue<int>("Benchmark:AssessorMaxOutputTokens", 32000);

        var runRequest = new AgentRunRequest
        {
            ProviderName = assessorConfig.Provider,
            ModelId = assessorConfig.ModelId,
            ApiKey = assessorApiKey,
            ModelDisplayName = assessorConfig.DisplayName,
            SystemPrompt = gradingPrompt.FullPrompt,
            SegmentedPrompt = gradingPrompt,
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
            CacheConversationTail = false,
            Budget = new AgentRunBudget { MaxTotalModelCalls = 2 },
            ToolExecutionContext = new Tools.ToolExecutionContext
            {
                SessionId = Overseer.Services.Privacy.SessionRef.Persistent(run.Id),
                UserId = run.StartedByUserId ?? string.Empty,
                ShowDebugLog = false
            },
            SeedHistory = gradingSeedHistory
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

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await RunDifficultyAssessmentCoreAsync(job, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            FinalizeIfStillRunning(job, BenchmarkDifficultyJobStatus.Cancelled);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Difficulty assessment job {JobId} failed.", jobId);
            job.AddLog($"Assessment failed: {ExceptionDetails.DescribeShort(ex)}", "error",
                ExceptionDetails.Describe(ex, 4000));
            FinalizeIfStillRunning(job, BenchmarkDifficultyJobStatus.Failed);
        }
    }

    // The in-loop paths finalise the job themselves; this only closes a job an exception left open.
    private void FinalizeIfStillRunning(BenchmarkDifficultyJob job, BenchmarkDifficultyJobStatus status)
    {
        if (job.Status != BenchmarkDifficultyJobStatus.Running) return;
        if (status == BenchmarkDifficultyJobStatus.Cancelled) job.MarkRemainingCancelled();
        else job.MarkRemainingSkipped();
        job.SetStatus(status);
        _difficultyJobManager.Complete(job.Id, status);
        job.AddLog($"Assessment finished with status: {status}.", status == BenchmarkDifficultyJobStatus.Cancelled ? "info" : "error");
    }

    private async Task RunDifficultyAssessmentCoreAsync(BenchmarkDifficultyJob job, CancellationToken cancellationToken)
    {
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
                job.MarkRemainingCancelled();
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
                        job.MarkRemainingCancelled();
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
            job.MarkRemainingCancelled();
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
        // complete run uses, with nothing to mark the difference. "Stopped early" is the answer-row
        // coverage, not the status alone: a Canceled row whose answers cover the suite — a cancelled
        // retry of a finished run, or a run cancelled during grading — is scored over all of it.
        if (BenchmarkRunFinalizer.IsAbortedRun(run, run.Answers))
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

        // A rescore brings a run up to the last method whose changes it can apply, and never stamps a
        // later method onto levels graded under an earlier one's anchors.
        if (run.ScoringMethodVersion <= LastMethodRescoreCanApply)
        {
            run.ScoringMethodVersion = LastMethodRescoreCanApply;
        }

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
        long runId,
        long answerId,
        long? assessorConfigId = null,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var configService = scope.ServiceProvider.GetRequiredService<SystemAiConfigService>();

        // CancellationToken.None: this load sits outside the try below, and the row it fetches is
        // what the handlers there need in order to restore the status. Cancelling it would throw
        // past every handler and past the finally that releases the run manager, leaving the row
        // reading Running with no owner. The cancellation check is the first statement in the try.
        var answer = await db.BenchmarkRunAnswers
            .Include(a => a.BenchmarkRun).ThenInclude(r => r.TestedModelConfiguration)
            .Include(a => a.BenchmarkRun).ThenInclude(r => r.AssessorModelConfiguration)
            .Include(a => a.BenchmarkRun).ThenInclude(r => r.BenchmarkSuite).ThenInclude(s => s!.Questions)
            .Include(a => a.BenchmarkRun).ThenInclude(r => r.BenchmarkSuite).ThenInclude(s => s!.GameSnapshot)
            .FirstOrDefaultAsync(a => a.Id == answerId, CancellationToken.None);

        if (answer == null)
        {
            _logger.LogWarning("Answer {AnswerId} not found for rerun.", answerId);
            var orphanedRun = await db.BenchmarkRuns.FindAsync(new object[] { runId }, cancellationToken);
            if (orphanedRun != null)
            {
                await RestoreTerminalStatusAsync(db, orphanedRun, "Re-run could not start: the answer was not found.");
            }
            _runManager.Complete(runId);
            return;
        }

        var run = answer.BenchmarkRun;
        try
        {
            // Before the key is decrypted, so a retry launched with an already-cancelled token takes
            // the restore path below rather than failing on whatever it touched first.
            cancellationToken.ThrowIfCancellationRequested();

            if (!IsCurrentScoringMethod(run))
            {
                await RestoreTerminalStatusAsync(db, run, ScoringMethodRefusal(run));
                return;
            }

            var testedConfig = run.TestedModelConfiguration;
            if (testedConfig == null || string.IsNullOrWhiteSpace(testedConfig.EncryptedApiKey))
            {
                answer.AssessmentError = BenchmarkAssessmentFailure.Truncate("Tested model configuration missing or has no API key.");
                await RestoreTerminalStatusAsync(db, run, "Tested model configuration missing or has no API key.");
                return;
            }

            var (assessorConfig, assessorApiKey, assessorError) = await ResolveAssessorAsync(db, run, assessorConfigId, cancellationToken);
            if (assessorConfig == null || assessorApiKey == null)
            {
                answer.AssessmentError = BenchmarkAssessmentFailure.Truncate(assessorError);
                await RestoreTerminalStatusAsync(db, run, assessorError);
                return;
            }

            // Ahead of clearing the verdict below, so a refused re-run leaves the answer as it was.
            if (string.IsNullOrWhiteSpace(run.CandidatePromptOptionsJson))
            {
                await RestoreTerminalStatusAsync(db, run, MissingPromptOptionsMessage(run));
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

            var promptOptions = BenchmarkCandidatePromptOptions.FromJson(run.CandidatePromptOptionsJson!);
            string systemPrompt = promptOptions.BuildSystemPrompt(_chatService, testedConfig.ParallelExecutionMode);
            var segmentedPrompt = BuildCandidateSegmentedPrompt(promptOptions, testedConfig.ParallelExecutionMode);
            PopulateInstrumentFingerprint(run, systemPrompt);

            VerifyCandidateDeliveryBeforeRun(
                run, testedConfig, systemPrompt, segmentedPrompt, answer.QuestionText, isRerun: true);

            string? expectedPoints = MatchSuiteQuestion(run, answer)?.ExpectedPoints;

            // A one-question scope, on the same contract as the failed-question re-run: the row is
            // overwritten in place, so the suite totals say nothing about this pass.
            _runManager.SetRerunScope(runId, new[] { answer.OrderIndex });

            await ReExecuteSingleAnswerAsync(
                db, configService, run, answer, testedConfig, testedApiKey,
                systemPrompt, segmentedPrompt, allowedTools,
                maxResultLength, maxToolCallsPerQuestion, cancellationToken);
            _runManager.MarkRerunAnswered(runId, answer.OrderIndex);

            await ExecutePerQuestionAssessmentAsync(
                db, configService, run, answer, expectedPoints,
                assessorConfig, assessorApiKey, scoringConstants, cancellationToken);
            _runManager.MarkRerunScored(runId, answer.OrderIndex);

            var allAnswers = await db.BenchmarkRunAnswers
                .Where(a => a.BenchmarkRunId == run.Id)
                .ToListAsync(CancellationToken.None);
            BenchmarkRunFinalizer.Apply(run, allAnswers);
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            await RestoreTerminalStatusAsync(db, run, "Answer re-run canceled.");
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
        long runId,
        long answerId,
        long? assessorConfigId,
        bool trial,
        BenchmarkRunStatus originalStatus,
        DateTime? originalCompletedAtUtc,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var configService = scope.ServiceProvider.GetRequiredService<SystemAiConfigService>();

        // CancellationToken.None: this load sits outside the try below, and the row it fetches is
        // what the handlers there need in order to restore the status. Cancelling it would throw
        // past every handler and past the finally that releases the run manager, leaving the row
        // reading Running with no owner. The cancellation check is the first statement in the try.
        var answer = await ReassessmentAnswerQuery(db)
            .FirstOrDefaultAsync(a => a.Id == answerId, CancellationToken.None);

        if (answer == null)
        {
            _logger.LogWarning("Answer {AnswerId} not found for reassessment.", answerId);
            var orphanedRun = await db.BenchmarkRuns.FindAsync(new object[] { runId }, cancellationToken);
            if (orphanedRun != null)
            {
                if (trial)
                {
                    await RestoreCapturedStatusAsync(db, orphanedRun, originalStatus, originalCompletedAtUtc);
                }
                else
                {
                    await RestoreTerminalStatusAsync(db, orphanedRun, "Re-assessment could not start: the answer was not found.");
                }
            }
            _runManager.Complete(runId);
            return;
        }

        var run = answer.BenchmarkRun;
        try
        {
            // Before the assessor is resolved, so a retry launched with an already-cancelled token
            // takes the restore path below rather than failing on whatever it touched first.
            cancellationToken.ThrowIfCancellationRequested();

            if (!IsCurrentScoringMethod(run))
            {
                if (trial)
                {
                    await RestoreCapturedStatusAsync(db, run, originalStatus, originalCompletedAtUtc);
                }
                else
                {
                    await RestoreTerminalStatusAsync(db, run, ScoringMethodRefusal(run));
                }
                return;
            }

            var (assessorConfig, assessorApiKey, error) = await ResolveAssessorAsync(db, run, assessorConfigId, cancellationToken);
            if (assessorConfig == null || assessorApiKey == null)
            {
                answer.AssessmentError = BenchmarkAssessmentFailure.Truncate(error);
                if (trial)
                {
                    await RestoreCapturedStatusAsync(db, run, originalStatus, originalCompletedAtUtc);
                }
                else
                {
                    await RestoreTerminalStatusAsync(db, run, error);
                }
                return;
            }

            // The row was already flipped to Running by the caller before the answer was even
            // loaded, and the original values captured there are what a trial restores below: a
            // trial must leave the stored run byte-identical, and CompletedAtUtc is part of the
            // record, not scratch state. The flip that follows is an idempotent belt-and-braces —
            // the row already reads Running by the time this line runs.
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
            if (!trial)
            {
                await RestoreTerminalStatusAsync(db, run, "Reassessment canceled.");
            }
            else
            {
                await RestoreCapturedStatusAsync(db, run, originalStatus, originalCompletedAtUtc);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Reassessment failed for answer {AnswerId}.", answerId);
            answer.AssessmentError = BenchmarkAssessmentFailure.Truncate(ex.Message);
            if (!trial)
            {
                run.Status = BenchmarkRunStatus.CompletedWithErrors;
                run.ErrorMessage = BenchmarkAssessmentFailure.Truncate(ex.Message);
                await db.SaveChangesAsync(CancellationToken.None);
            }
            else
            {
                await RestoreCapturedStatusAsync(db, run, originalStatus, originalCompletedAtUtc);
            }
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

        // CancellationToken.None: this load sits outside the try below, and the row it fetches is
        // what the handlers there need in order to restore the status. Cancelling it would throw
        // past every handler and past the finally that releases the run manager, leaving the row
        // reading Running with no owner. The cancellation check is the first statement in the try.
        var run = await db.BenchmarkRuns
            .Include(r => r.Answers)
            .Include(r => r.AssessorModelConfiguration)
            .FirstOrDefaultAsync(r => r.Id == runId, CancellationToken.None);

        if (run == null)
        {
            _logger.LogWarning("Run {RunId} not found for final synthesis rerun.", runId);
            _runManager.Complete(runId);
            return;
        }

        try
        {
            // Before the assessor is resolved, so a retry launched with an already-cancelled token
            // takes the restore path below rather than failing on whatever it touched first.
            cancellationToken.ThrowIfCancellationRequested();

            var (assessorConfig, assessorApiKey, error) = await ResolveAssessorAsync(db, run, assessorConfigId, cancellationToken);
            if (assessorConfig == null || assessorApiKey == null)
            {
                await RestoreTerminalStatusAsync(db, run, error);
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
            await RestoreTerminalStatusAsync(db, run, "Synthesis re-run canceled.");
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

        // CancellationToken.None: this load sits outside the try below, and the row it fetches is
        // what the handlers there need in order to restore the status. Cancelling it would throw
        // past every handler and past the finally that releases the run manager, leaving the row
        // reading Running with no owner. The cancellation check is the first statement in the try.
        var run = await RetryAssessmentsRunQuery(db)
            .FirstOrDefaultAsync(r => r.Id == runId, CancellationToken.None);

        if (run == null)
        {
            _logger.LogWarning("Run {RunId} not found for retry failed assessments.", runId);
            _runManager.Complete(runId);
            return;
        }

        try
        {
            // Before the assessor is resolved, so a retry launched with an already-cancelled token
            // takes the restore path below rather than failing on whatever it touched first.
            cancellationToken.ThrowIfCancellationRequested();

            if (!IsCurrentScoringMethod(run))
            {
                await RestoreTerminalStatusAsync(db, run, ScoringMethodRefusal(run));
                return;
            }

            var (assessorConfig, assessorApiKey, error) = await ResolveAssessorAsync(db, run, assessorConfigId, cancellationToken);
            if (assessorConfig == null || assessorApiKey == null)
            {
                await RestoreTerminalStatusAsync(db, run, error);
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
                    await RestoreTerminalStatusAsync(db, run, "Assessment retry canceled.");
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
            await RestoreTerminalStatusAsync(db, run, "Assessment retry canceled.");
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

        // CancellationToken.None: this load sits outside the try below, and the row it fetches is
        // what the handlers there need in order to restore the status. Cancelling it would throw
        // past every handler and past the finally that releases the run manager, leaving the row
        // reading Running with no owner. The cancellation check is the first statement in the try.
        // The suite's board is part of the verifier's prompt from harness 29, so this path loads it
        // as the two run-level paths already do; without it a retry would verify board claims
        // against the rubric alone.
        var run = await db.BenchmarkRuns
            .Include(r => r.Answers)
            .Include(r => r.BenchmarkSuite)
            .ThenInclude(s => s!.GameSnapshot)
            .FirstOrDefaultAsync(r => r.Id == runId, CancellationToken.None);

        if (run == null)
        {
            _logger.LogWarning("Run {RunId} not found for retry failed claim verification.", runId);
            _runManager.Complete(runId);
            return;
        }

        try
        {
            // Before any work, so a retry launched with an already-cancelled token takes the
            // restore path below rather than failing on whatever it touched first.
            cancellationToken.ThrowIfCancellationRequested();

            if (!IsCurrentScoringMethod(run))
            {
                await RestoreTerminalStatusAsync(db, run, ScoringMethodRefusal(run));
                return;
            }

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
                await RestoreTerminalStatusAsync(db, run, null);
                return;
            }

            // A re-grade stored beside the failed attempt described findings that no longer exist; the
            // fresh attempt writes one only if it qualifies again and produces a valid verdict.
            foreach (var answer in failedAnswers)
            {
                answer.ClaimVerificationError = null;
                answer.ClaimVerificationRawText = null;
                answer.EvidenceInformedQualityScore = null;
                answer.EvidenceInformedCriticalError = null;
                answer.EvidenceInformedJson = null;
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
            await RestoreTerminalStatusAsync(db, run, "Claim verification retry canceled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Retry failed claim verification failed for run {RunId}.", runId);
            await RestoreTerminalStatusAsync(db, run, ex.Message);
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
    /// The instrument a failed-question re-run executed under, written to the three <c>Rerun*</c>
    /// instrument columns: the two fingerprints that a code or guide change can move, and the harness
    /// version the re-run's code carries. The three corpus heads belong to the corpora, which a re-run
    /// reads exactly as the original run did, and duplicating them would invite a reader to compare a
    /// run against itself.
    /// </summary>
    internal void PopulateRerunInstrumentFingerprint(BenchmarkRun run, string systemPrompt)
    {
        using var sha256 = SHA256.Create();
        byte[] promptHash = sha256.ComputeHash(Encoding.UTF8.GetBytes(systemPrompt));
        run.RerunCandidateSystemPromptSha256 = Convert.ToHexString(promptHash).ToLowerInvariant();
        run.RerunToolGuidesSha256 = ComputeToolGuidesSha256();
        run.RerunHarnessVersion = BenchmarkAssessmentPrompt.HarnessVersion;
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
    /// the flag is set and <see cref="OutOfRubricBasisOf"/> yields a statement from the stored
    /// accuracy evidence.
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
    /// stored accuracy evidence: the sentence after the <c>Not in rubric:</c> marker when there is
    /// one, otherwise the sentence citing unverifiability
    /// (<see cref="BenchmarkVerdictConsistency.UnverifiabilityBasisOf"/>). Null when the answer does
    /// not carry <see cref="BenchmarkAnswerFlags.OutOfRubricAccuracyDeduction"/> or neither yields a
    /// basis. Deterministic, so every reader of <see cref="BenchmarkRunAnswer.ClaimVerificationJson"/>
    /// recovers the same text the verifier was given.
    /// </summary>
    internal static string? OutOfRubricBasisOf(BenchmarkRunAnswer answer)
    {
        if ((((BenchmarkAnswerFlags)answer.AnswerFlags) & BenchmarkAnswerFlags.OutOfRubricAccuracyDeduction) == 0)
        {
            return null;
        }

        string? accuracyEvidence = ReadEvidence(answer.AssessmentEvidenceJson, "accuracy");
        return ExtractOutOfRubricBasis(accuracyEvidence)
            ?? BenchmarkVerdictConsistency.UnverifiabilityBasisOf(accuracyEvidence);
    }

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
    /// The verifications that concern the answer's own claims: every entry except the critical-error
    /// quote, matched by verbatim text the way <see cref="WithoutOutOfRubricBasis"/> matches the
    /// basis — <see cref="WithCriticalErrorQuoteFirst"/> submits the quote once and
    /// <see cref="BenchmarkClaimVerificationParser"/> echoes the submitted text back. The input is
    /// returned unchanged in content when there is no quote.
    ///
    /// The quote is the assessor's finding, not a claim of the answer, so its verdict stays out of
    /// the answer's claim counts; it remains in
    /// <see cref="BenchmarkRunAnswer.ClaimVerificationJson"/> and in the
    /// <see cref="BenchmarkAnswerFlags.ContestedCriticalError"/> decision, which read the unfiltered
    /// list.
    /// </summary>
    internal static List<BenchmarkClaimVerification> WithoutCriticalErrorQuote(
        IReadOnlyList<BenchmarkClaimVerification>? verifications,
        string? criticalErrorQuote)
    {
        if (verifications == null) return new List<BenchmarkClaimVerification>();
        if (string.IsNullOrWhiteSpace(criticalErrorQuote)) return verifications.ToList();

        string quote = criticalErrorQuote.Trim();
        return verifications
            .Where(v => !string.Equals(v.Claim?.Trim(), quote, StringComparison.Ordinal))
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

        return match != null && match.EffectiveVerdict == BenchmarkClaimVerdict.Refuted;
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

        return match != null && match.EffectiveVerdict == BenchmarkClaimVerdict.Supported;
    }

    /// <summary>
    /// What a contested answer with no unverified claims submits to the verifier: sentences of the
    /// answer, which are persisted as its unverified claims, and statements of the assessor, which
    /// are submitted as <see cref="BenchmarkClaimRoles.AssessorStatement"/> and never persisted.
    /// </summary>
    internal sealed record DisputedClaims(List<string> AnswerClaims, List<string> AssessorStatements);

    internal const int MaxDisputedAnswerNumericClaims = 4;
    internal const int MaxDisputedClaims = 8;
    internal const int MinDisputedAnswerClaimWords = 4;

    private static readonly Regex AnswerSentenceSplitRegex = new(@"(?<=[.!?])\s+", RegexOptions.Compiled);
    private static readonly Regex EvidenceSentenceSplitRegex = new(@"(?<=[.!?\n])\s+", RegexOptions.Compiled);
    private static readonly Regex LetterRunRegex = new(@"\p{L}{3,}", RegexOptions.Compiled);

    /// <summary>A sentence that only reports what the answer says; paired with a quotation, the accused-quote path checks that quotation.</summary>
    private static readonly Regex AnswerReportRegex = new(
        @"^(?:The answer|It|The response)\s+(?:states|says|claims|asserts|gives|lists|tells)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// 1. The critical-error quote, when there is one (an answer claim). 2. Up to
    /// <see cref="MaxDisputedAnswerNumericClaims"/> digit-bearing sentences of the answer, split on
    /// sentence ends only, each of at least <see cref="MinDisputedAnswerClaimWords"/> words and one
    /// run of three letters (answer claims). 3. Sentences of the accuracy evidence, or the whole
    /// evidence when nothing else was found, skipping a sentence that only reports what the answer
    /// says and quotes it, and a <c>Suspected false:</c> entry, which is a sentence of the answer
    /// (assessor statements). 4. The review comment when nothing at all was found (an assessor
    /// statement). At most <see cref="MaxDisputedClaims"/> in all.
    /// </summary>
    internal static DisputedClaims ExtractDisputedClaims(BenchmarkRunAnswer answer, string? accuracyEvidence)
    {
        var answerClaims = new List<string>();
        var assessorStatements = new List<string>();
        bool Seen(string text) => answerClaims.Contains(text, StringComparer.OrdinalIgnoreCase)
            || assessorStatements.Contains(text, StringComparer.OrdinalIgnoreCase);
        int Total() => answerClaims.Count + assessorStatements.Count;

        if (!string.IsNullOrWhiteSpace(answer.CriticalErrorQuote))
        {
            var quote = answer.CriticalErrorQuote.Trim();
            if (quote.Length >= 5)
            {
                answerClaims.Add(quote);
            }
        }

        if (!string.IsNullOrWhiteSpace(answer.AnswerText))
        {
            int numericClaimsCount = 0;
            foreach (var raw in AnswerSentenceSplitRegex.Split(answer.AnswerText))
            {
                var trimmed = raw.Trim();
                if (trimmed.Length >= 10 && trimmed.Length <= 300 && Regex.IsMatch(trimmed, @"\d")
                    && IsSubstantiveAnswerSentence(trimmed) && !Seen(trimmed))
                {
                    answerClaims.Add(trimmed);
                    numericClaimsCount++;
                    if (numericClaimsCount >= MaxDisputedAnswerNumericClaims) break;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(accuracyEvidence))
        {
            foreach (var raw in EvidenceSentenceSplitRegex.Split(accuracyEvidence))
            {
                var trimmed = raw.Trim();
                if (trimmed.Length >= 15 && trimmed.Length <= 300 && IsAssessorStatementCandidate(trimmed) && !Seen(trimmed))
                {
                    assessorStatements.Add(trimmed);
                    if (Total() >= MaxDisputedClaims) break;
                }
            }

            string whole = accuracyEvidence.Trim();
            if (Total() == 0 && whole.Length <= 400 && IsAssessorStatementCandidate(whole))
            {
                assessorStatements.Add(whole);
            }
        }

        if (Total() == 0 && !string.IsNullOrWhiteSpace(answer.ReviewComment))
        {
            var comment = answer.ReviewComment.Trim();
            if (comment.Length <= 400)
            {
                assessorStatements.Add(comment);
            }
        }

        return new DisputedClaims(answerClaims, assessorStatements);
    }

    /// <summary>At least <see cref="MinDisputedAnswerClaimWords"/> words carrying a letter, and one run of three letters.</summary>
    private static bool IsSubstantiveAnswerSentence(string sentence)
        => sentence.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Count(w => w.Any(char.IsLetter)) >= MinDisputedAnswerClaimWords
            && LetterRunRegex.IsMatch(sentence);

    /// <summary>False for a sentence that reports what the answer says and quotes it, and for a suspected-false entry.</summary>
    private static bool IsAssessorStatementCandidate(string sentence)
    {
        if (BenchmarkSuspectedFalseClaim.IsSuspectedFalse(sentence)) return false;
        return !(AnswerReportRegex.IsMatch(sentence) && ContainsQuotation(sentence));
    }

    private static bool ContainsQuotation(string text)
        => StraightQuotedSpanRegex.IsMatch(text)
            || TypographicQuotedSpanRegex.IsMatch(text)
            || StraightSingleQuotedSpanRegex.IsMatch(text)
            || TypographicSingleQuotedSpanRegex.IsMatch(text);

    /// <summary>
    /// One item submitted to the claim verifier: its text, why it was submitted, and the answer
    /// context it is read in; for an accused sentence the assessor's quoted fragments and charge, and
    /// for a suspected-false claim the assessor's reason and the entry it was recorded as.
    /// </summary>
    internal sealed record ClaimSubmission(string Text, IReadOnlyList<string> Roles, string? Context)
    {
        public IReadOnlyList<string>? QuotedFragments { get; init; }
        public string? Charge { get; init; }
        public bool SuspectedFalse { get; init; }
        public string? Suspicion { get; init; }
        public string? RecordedClaim { get; init; }
    }

    /// <summary>
    /// A sentence of the answer the assessor quoted in its accuracy evidence: the sentence or list
    /// item of the answer that encloses the quotation, its context, the quoted spans as they occur in
    /// the answer, and the assessor's evidence sentence(s) that quote it.
    /// </summary>
    internal sealed record AccusedQuote(string Text, string? Context, IReadOnlyList<string>? QuotedFragments = null, string? Charge = null);

    internal const int AccusedQuoteMinLength = 15;
    internal const int AccusedQuoteMaxLength = 400;
    internal const int AccusedQuoteChargeMaxLength = 400;
    internal const int MaxAccusedQuotesPerAnswer = 3;
    internal const int AccusedQuoteEligibleMaxAccuracyLevel = 5;
    private const int AccusedQuoteContextMaxLength = 300;

    private static readonly Regex StraightQuotedSpanRegex = new("\"([^\"]*)\"", RegexOptions.Compiled);
    private static readonly Regex TypographicQuotedSpanRegex = new("“([^”]*)”", RegexOptions.Compiled);

    // Single quotes double as apostrophes, so a span opens only after the start of the text,
    // whitespace, "(" or an em dash, and closes only before the end of the text, whitespace or
    // punctuation; a contraction's apostrophe is followed by a letter and never closes one.
    private static readonly Regex StraightSingleQuotedSpanRegex = new(@"(?<=^|[\s(—])'(?=\S)(.+?)(?<=\S)'(?=$|[\s\p{P}])", RegexOptions.Compiled);
    private static readonly Regex TypographicSingleQuotedSpanRegex = new(@"(?<=^|[\s(—])‘(?=\S)(.+?)(?<=\S)’(?=$|[\s\p{P}])", RegexOptions.Compiled);
    private static readonly Regex TrailingListMarkerRegex = new(@"(?:^|\n)[ \t]*(?:[-*+•]|\d+[.)])[ \t]*$", RegexOptions.Compiled);
    private static readonly Regex PrecedingSentenceBoundaryRegex = new(@"[.!?](?=\s)|\n", RegexOptions.Compiled | RegexOptions.RightToLeft);

    /// <summary>
    /// Whether the harness checks the sentences the assessor quoted against this answer: an Accuracy
    /// level of <see cref="AccusedQuoteEligibleMaxAccuracyLevel"/> or below, or a contested verdict,
    /// whatever the answer's unverified-claim count.
    /// </summary>
    internal static bool IsAccusedQuoteEligible(BenchmarkRunAnswer answer)
        => (answer.AccuracyLevel.HasValue && answer.AccuracyLevel.Value <= AccusedQuoteEligibleMaxAccuracyLevel)
            || ((BenchmarkAnswerFlags)answer.AnswerFlags).HasFlag(BenchmarkAnswerFlags.ContestedVerdict);

    private bool AccusedQuotesEnabled
        => _configuration.GetValue<bool>("Benchmark:ClaimVerification:AccusedQuotesEnabled", true);

    /// <summary>The accused quotes this answer would submit, empty when the feature is off or the answer is not eligible.</summary>
    internal IReadOnlyList<AccusedQuote> AccusedQuotesFor(BenchmarkRunAnswer answer)
        => AccusedQuotesEnabled && IsAccusedQuoteEligible(answer)
            ? ExtractAccusedQuotes(answer.AnswerText, ReadEvidence(answer.AssessmentEvidenceJson, "accuracy"))
            : Array.Empty<AccusedQuote>();

    /// <summary><see cref="NeedsClaimVerification"/>, or a sentence the assessor charged that the verifier can check.</summary>
    internal bool NeedsClaimVerificationOrAccusation(BenchmarkRunAnswer answer)
        => NeedsClaimVerification(answer) || AccusedQuotesFor(answer).Count > 0;

    /// <summary>
    /// The sentences of the answer the assessor quoted in its accuracy evidence: each span between
    /// paired double quotes (straight or typographic), or single quotes (straight or typographic)
    /// under the boundary rules of <see cref="StraightSingleQuotedSpanRegex"/>, of
    /// <see cref="AccusedQuoteMinLength"/> to <see cref="AccusedQuoteMaxLength"/> characters that
    /// occurs in the answer once Markdown emphasis and whitespace runs are ignored. A span the
    /// evidence clause around it approves of (<see cref="IsApprovedInEvidence"/>) is not an
    /// accusation and is skipped; a span not in the answer (a rubric or board quotation) is dropped.
    /// The answer's own span is widened to the sentence or list item enclosing it
    /// (<see cref="EnclosingSentence"/>), or kept alone when that exceeds
    /// <see cref="AccusedQuoteMaxLength"/>; spans whose widened ranges overlap become one submission
    /// carrying every quoted fragment and the evidence sentence of each. At most
    /// <see cref="MaxAccusedQuotesPerAnswer"/>, longest first, ties in evidence order. A bounded
    /// heuristic: it finds only accusations the assessor quoted.
    /// </summary>
    internal static List<AccusedQuote> ExtractAccusedQuotes(string? answerText, string? accuracyEvidence)
    {
        var result = new List<AccusedQuote>();
        if (string.IsNullOrWhiteSpace(answerText) || string.IsNullOrWhiteSpace(accuracyEvidence))
        {
            return result;
        }

        var (normalizedAnswer, map) = NormalizeWithMap(answerText);
        var candidates = new List<AccusedCandidate>();

        var matches = StraightQuotedSpanRegex.Matches(accuracyEvidence).Cast<Match>()
            .Concat(TypographicQuotedSpanRegex.Matches(accuracyEvidence).Cast<Match>())
            .Concat(StraightSingleQuotedSpanRegex.Matches(accuracyEvidence).Cast<Match>())
            .Concat(TypographicSingleQuotedSpanRegex.Matches(accuracyEvidence).Cast<Match>())
            .OrderBy(m => m.Index)
            .ToList();
        var quotedRanges = matches.Select(m => (Open: m.Index, Close: m.Index + m.Length - 1)).ToList();

        foreach (var match in matches)
        {
            string quoted = match.Groups[1].Value.Trim();
            if (quoted.Length < AccusedQuoteMinLength || quoted.Length > AccusedQuoteMaxLength)
            {
                continue;
            }

            if (IsApprovedInEvidence(accuracyEvidence, match.Index, match.Index + match.Length - 1))
            {
                continue;
            }

            string normalizedQuote = NormalizeWithMap(quoted).Normalized;
            if (normalizedQuote.Length == 0)
            {
                continue;
            }

            int at = normalizedAnswer.IndexOf(normalizedQuote, StringComparison.OrdinalIgnoreCase);
            if (at < 0)
            {
                continue;
            }

            int start = map[at];
            int end = map[at + normalizedQuote.Length - 1];
            string span = answerText.Substring(start, end - start + 1).Trim();
            if (span.Length == 0)
            {
                continue;
            }

            var (sentenceStart, sentenceEnd) = EnclosingSentence(answerText, start, end);
            if (sentenceEnd - sentenceStart + 1 > AccusedQuoteMaxLength)
            {
                sentenceStart = start;
                sentenceEnd = end;
            }

            string? charge = EvidenceSentenceAround(accuracyEvidence, match.Index, match.Index + match.Length - 1, quotedRanges);

            var overlapping = candidates.FirstOrDefault(c => c.Start <= sentenceEnd && sentenceStart <= c.End);
            if (overlapping != null)
            {
                int unionStart = Math.Min(overlapping.Start, sentenceStart);
                int unionEnd = Math.Max(overlapping.End, sentenceEnd);
                if (unionEnd - unionStart + 1 > AccusedQuoteMaxLength)
                {
                    continue;
                }

                overlapping.Start = unionStart;
                overlapping.End = unionEnd;
                if (!overlapping.Fragments.Contains(span, StringComparer.OrdinalIgnoreCase))
                {
                    overlapping.Fragments.Add(span);
                }
                if (charge != null && !overlapping.Charges.Contains(charge, StringComparer.Ordinal))
                {
                    overlapping.Charges.Add(charge);
                }
                continue;
            }

            var candidate = new AccusedCandidate(match.Index, sentenceStart, sentenceEnd);
            candidate.Fragments.Add(span);
            if (charge != null)
            {
                candidate.Charges.Add(charge);
            }
            candidates.Add(candidate);
        }

        foreach (var candidate in candidates
            .OrderByDescending(c => c.End - c.Start)
            .ThenBy(c => c.SourceIndex)
            .Take(MaxAccusedQuotesPerAnswer))
        {
            string text = answerText.Substring(candidate.Start, candidate.End - candidate.Start + 1);
            string? charge = candidate.Charges.Count > 0 ? CapWithEllipsis(string.Join(" ", candidate.Charges), AccusedQuoteChargeMaxLength) : null;
            result.Add(new AccusedQuote(
                text,
                AccusedQuoteContext(answerText, candidate.Start, text),
                candidate.Fragments.ToList(),
                charge));
        }

        return result;
    }

    internal const int DockedSpanMinLength = 4;

    /// <summary>
    /// The quoted spans of the accuracy evidence (<see cref="ExtractAccusedQuotes"/>'s four quote
    /// forms) of at least <see cref="DockedSpanMinLength"/> characters that the evidence does not
    /// approve of (<see cref="IsApprovedInEvidence"/>), normalised as <see cref="NormalizeWithMap"/>
    /// normalises; no upper bound.
    /// </summary>
    internal static List<string> DockedQuotedSpans(string? accuracyEvidence)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(accuracyEvidence)) return result;

        var matches = StraightQuotedSpanRegex.Matches(accuracyEvidence).Cast<Match>()
            .Concat(TypographicQuotedSpanRegex.Matches(accuracyEvidence).Cast<Match>())
            .Concat(StraightSingleQuotedSpanRegex.Matches(accuracyEvidence).Cast<Match>())
            .Concat(TypographicSingleQuotedSpanRegex.Matches(accuracyEvidence).Cast<Match>())
            .OrderBy(m => m.Index);

        foreach (var match in matches)
        {
            string quoted = match.Groups[1].Value.Trim();
            if (quoted.Length < DockedSpanMinLength) continue;
            if (IsApprovedInEvidence(accuracyEvidence, match.Index, match.Index + match.Length - 1)) continue;

            string normalized = NormalizeWithMap(quoted).Normalized;
            if (normalized.Length >= DockedSpanMinLength && !result.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(normalized);
            }
        }

        return result;
    }

    /// <summary>
    /// Whether an Accuracy level below 6 quotes, in its evidence, a sentence the assessor also
    /// reported as <c>Suspected false:</c> — a deduction on its own knowledge, which scoring method 12
    /// does not allow. True when a <see cref="DockedQuotedSpans"/> span occurs, ignoring case, in the
    /// sentence part (<see cref="BenchmarkSuspectedFalseClaim.TryParse"/>) of any such entry. Unlike
    /// <see cref="ExtractAccusedQuotes"/> it takes a span as short as a spell name.
    /// </summary>
    internal static bool DocksSuspectedFalse(string? answerText, string? accuracyEvidence, IEnumerable<string> unverifiedClaims, int? accuracyLevel)
    {
        if (accuracyLevel is not int level || level >= 6) return false;

        var suspectedSentences = SuspectedFalseSentences(answerText, unverifiedClaims);
        if (suspectedSentences.Count == 0) return false;

        return DockedQuotedSpans(accuracyEvidence)
            .Any(span => suspectedSentences.Any(s => s.Contains(span, StringComparison.OrdinalIgnoreCase)));
    }

    private static List<string> SuspectedFalseSentences(string? answerText, IEnumerable<string>? unverifiedClaims)
    {
        var sentences = new List<string>();
        foreach (string entry in unverifiedClaims ?? Array.Empty<string>())
        {
            if (BenchmarkSuspectedFalseClaim.TryParse(entry, answerText, out string sentence, out _))
            {
                string normalized = NormalizeWithMap(sentence).Normalized;
                if (normalized.Length > 0) sentences.Add(normalized);
            }
        }
        return sentences;
    }

    /// <summary>
    /// The <c>Suspected false:</c> sentences the verifier supported with a citation whose text holds
    /// a span the answer's Accuracy evidence quotes to dock it (<see cref="DockedQuotedSpans"/>): the
    /// deduction rests on a statement the source bears out. Empty at Accuracy 6 or above.
    /// </summary>
    internal static List<BenchmarkClaimVerification> SupportedDockedSuspicions(
        BenchmarkRunAnswer answer,
        IReadOnlyList<BenchmarkClaimVerification>? verifications)
    {
        if (verifications == null || answer.AccuracyLevel is not int level || level >= 6)
        {
            return new List<BenchmarkClaimVerification>();
        }

        var spans = DockedQuotedSpans(ReadEvidence(answer.AssessmentEvidenceJson, "accuracy"));
        if (spans.Count == 0) return new List<BenchmarkClaimVerification>();

        return verifications
            .Where(v => v.SuspectedFalse == true
                && v.EffectiveVerdict == BenchmarkClaimVerdict.Supported
                && !string.IsNullOrWhiteSpace(v.Citation)
                && !string.IsNullOrWhiteSpace(v.Claim))
            .Where(v =>
            {
                string claim = NormalizeWithMap(v.Claim).Normalized;
                return spans.Any(span => claim.Contains(span, StringComparison.OrdinalIgnoreCase));
            })
            .ToList();
    }

    /// <summary>One accused submission while it is assembled: the answer range it covers, and what it collected.</summary>
    private sealed class AccusedCandidate
    {
        public AccusedCandidate(int sourceIndex, int start, int end)
        {
            SourceIndex = sourceIndex;
            Start = start;
            End = end;
        }

        public int SourceIndex { get; }
        public int Start { get; set; }
        public int End { get; set; }
        public List<string> Fragments { get; } = new();
        public List<string> Charges { get; } = new();
    }

    private static readonly Regex LeadingListMarkerRegex = new(@"^(?:[-*+•]|\d+[.)])[ \t]+", RegexOptions.Compiled);

    private static bool IsSentenceTerminator(string text, int index)
        => text[index] is '.' or '!' or '?'
            && (index + 1 == text.Length || char.IsWhiteSpace(text[index + 1]));

    /// <summary>
    /// The sentence or list item of <paramref name="text"/> that encloses the characters from
    /// <paramref name="start"/> to <paramref name="end"/>: back to the previous line break or
    /// <c>.</c>, <c>!</c> or <c>?</c> followed by whitespace, and forward to the next line break or
    /// such a terminator, which is kept. Surrounding whitespace and a leading list marker are left
    /// out. Inclusive indexes.
    /// </summary>
    internal static (int Start, int End) EnclosingSentence(string text, int start, int end)
    {
        int s = 0;
        for (int i = start - 1; i >= 0; i--)
        {
            if (text[i] is '\n' or '\r' || IsSentenceTerminator(text, i))
            {
                s = i + 1;
                break;
            }
        }

        int e = text.Length - 1;
        if (text[end] is '.' or '!' or '?')
        {
            e = end;
        }
        else
        {
            for (int i = end + 1; i < text.Length; i++)
            {
                if (text[i] is '\n' or '\r')
                {
                    e = i - 1;
                    break;
                }
                if (IsSentenceTerminator(text, i))
                {
                    e = i;
                    break;
                }
            }
        }

        while (s < start && char.IsWhiteSpace(text[s])) s++;
        var marker = LeadingListMarkerRegex.Match(text.Substring(s, start - s));
        if (marker.Success)
        {
            s += marker.Length;
        }
        while (e > end && char.IsWhiteSpace(text[e])) e--;

        return (s, e);
    }

    /// <summary>
    /// The sentence of the evidence that holds the quotation delimited at <paramref name="open"/> and
    /// <paramref name="close"/>, ignoring terminators inside any quotation, trimmed; a sentence longer
    /// than <see cref="AccusedQuoteChargeMaxLength"/> is cut to a window around the quotation.
    /// </summary>
    private static string? EvidenceSentenceAround(
        string evidence, int open, int close, IReadOnlyList<(int Open, int Close)> quotedRanges)
    {
        bool InQuotation(int i) => quotedRanges.Any(r => i > r.Open && i < r.Close);
        bool IsEnd(int i) => evidence[i] is '\n' or '\r' || (IsSentenceTerminator(evidence, i) && !InQuotation(i));

        int s = 0;
        for (int i = open - 1; i >= 0; i--)
        {
            if (IsEnd(i))
            {
                s = i + 1;
                break;
            }
        }

        int e = evidence.Length - 1;
        for (int i = close + 1; i < evidence.Length; i++)
        {
            if (IsEnd(i))
            {
                e = evidence[i] is '\n' or '\r' ? i - 1 : i;
                break;
            }
        }

        string sentence = evidence.Substring(s, e - s + 1);
        if (sentence.Length > AccusedQuoteChargeMaxLength)
        {
            int windowStart = Math.Clamp(open - s - AccusedQuoteChargeMaxLength / 4, 0, sentence.Length - AccusedQuoteChargeMaxLength);
            string window = sentence.Substring(windowStart, AccusedQuoteChargeMaxLength).Trim();
            sentence = (windowStart > 0 ? "…" : string.Empty)
                + window
                + (windowStart + AccusedQuoteChargeMaxLength < sentence.Length ? "…" : string.Empty);
        }

        sentence = sentence.Trim();
        return sentence.Length > 0 ? sentence : null;
    }

    private static string CapWithEllipsis(string text, int maxLength)
        => text.Length <= maxLength ? text : text.Substring(0, maxLength).TrimEnd() + "…";

    /// <summary>
    /// Whether the evidence quotes the span delimited at <paramref name="open"/> and
    /// <paramref name="close"/> to approve of it rather than to charge it. The clause around the
    /// span runs back to the previous <c>.</c>, <c>;</c> or <c>:</c> followed by whitespace and
    /// forward to the next; the span itself is not read. A parenthetical that encloses the span
    /// is read first, innermost outwards, and decides when it carries a marker of either kind; a
    /// parenthetical with none belongs to the clause around it ("Correct on the core mechanic
    /// ("…")"). A clause approves when it carries an approval marker and no charge marker
    /// (<see cref="BenchmarkVerdictConsistency.AccusationClauseApproves"/>); a clause with neither
    /// keeps the span, so a span is skipped only on positive evidence of approval.
    ///
    /// When the clause boundary before the span lies inside a parenthetical that encloses it — a
    /// list of clauses in parentheses, "Core claims match (a; the exact '…' message; b)" — see
    /// <see cref="IsApprovedAcrossParentheses"/>.
    /// </summary>
    internal static bool IsApprovedInEvidence(string evidence, int open, int close)
    {
        var enclosingOpeners = new List<int>();
        int depth = 0;
        int clauseStart = 0;
        int innerStart = -1;
        int outerStart = 0;
        bool outerStartFound = false;
        for (int i = open - 1; i >= 0; i--)
        {
            char c = evidence[i];
            if (c == ')')
            {
                depth++;
            }
            else if (c == '(')
            {
                if (depth > 0)
                {
                    depth--;
                }
                else
                {
                    enclosingOpeners.Add(i);
                    outerStartFound = false;
                }
            }
            else if (depth == 0 && IsClauseBoundary(evidence, i))
            {
                if (innerStart < 0 && enclosingOpeners.Count > 0)
                {
                    clauseStart = i + 1;
                    break;
                }

                if (innerStart < 0)
                {
                    // The first boundary lies before any enclosing opener: the walk goes on to the
                    // start of the sentence for a parenthetical enclosing that boundary too.
                    innerStart = i + 1;
                    clauseStart = innerStart;
                }
                else if (enclosingOpeners.Count > 0 && !outerStartFound)
                {
                    outerStart = i + 1;
                    outerStartFound = true;
                }

                if (evidence[i] == '.') break;
            }
        }

        if (innerStart >= 0 && enclosingOpeners.Count > 0)
        {
            return IsApprovedAcrossParentheses(evidence, open, close, innerStart, enclosingOpeners, outerStart);
        }

        var enclosingClosers = new List<int>();
        depth = 0;
        int clauseEnd = evidence.Length;
        for (int i = close + 1; i < evidence.Length; i++)
        {
            char c = evidence[i];
            if (c == '(')
            {
                depth++;
            }
            else if (c == ')')
            {
                if (depth > 0) depth--;
                else enclosingClosers.Add(i);
            }
            else if (depth == 0 && IsClauseBoundary(evidence, i))
            {
                clauseEnd = i;
                break;
            }
        }

        for (int level = 0; level < enclosingOpeners.Count; level++)
        {
            int from = enclosingOpeners[level] + 1;
            int to = level < enclosingClosers.Count ? enclosingClosers[level] : clauseEnd;
            bool? parenthetical = BenchmarkVerdictConsistency.AccusationClauseApproves(ClauseAround(evidence, from, to, open, close));
            if (parenthetical.HasValue)
            {
                return parenthetical.Value;
            }
        }

        return BenchmarkVerdictConsistency.AccusationClauseApproves(ClauseAround(evidence, clauseStart, clauseEnd, open, close)) == true;
    }

    /// <summary>
    /// <see cref="IsApprovedInEvidence"/> for a span whose clause lies inside a parenthetical among
    /// other clauses (<paramref name="innerStart"/> is after a boundary inside the innermost
    /// enclosing parenthetical, whose openers are <paramref name="enclosingOpeners"/>, innermost
    /// first). Read in order, the first to carry a marker deciding: the span's own clause, bounded by
    /// the innermost parenthetical; each further enclosing parenthetical outwards, without the one it
    /// encloses; then the clause holding the outermost opener, from <paramref name="outerStart"/> to
    /// the boundary after its closer, without the parenthetical. The innermost parenthetical is not
    /// read whole: its other clauses judge other statements.
    /// </summary>
    private static bool IsApprovedAcrossParentheses(
        string evidence, int open, int close, int innerStart, IReadOnlyList<int> enclosingOpeners, int outerStart)
    {
        var closers = new List<int>();
        int depth = 0;
        int innerEnd = -1;
        int outerEnd = evidence.Length;
        for (int i = close + 1; i < evidence.Length; i++)
        {
            char c = evidence[i];
            if (c == '(')
            {
                depth++;
            }
            else if (c == ')')
            {
                if (depth > 0) depth--;
                else closers.Add(i);
            }
            else if (depth == 0 && IsClauseBoundary(evidence, i))
            {
                if (innerEnd < 0 && closers.Count == 0) innerEnd = i;
                if (closers.Count >= enclosingOpeners.Count || evidence[i] == '.')
                {
                    outerEnd = i;
                    break;
                }
            }
        }

        int innerTo = innerEnd >= 0 ? innerEnd : closers.Count > 0 ? closers[0] : outerEnd;
        bool? inner = BenchmarkVerdictConsistency.AccusationClauseApproves(ClauseAround(evidence, innerStart, innerTo, open, close));
        if (inner.HasValue)
        {
            return inner.Value;
        }

        int CloserAt(int level) => level < closers.Count ? closers[level] : outerEnd;

        for (int level = 1; level < enclosingOpeners.Count; level++)
        {
            int from = enclosingOpeners[level] + 1;
            int to = CloserAt(level);
            bool? parenthetical = BenchmarkVerdictConsistency.AccusationClauseApproves(
                TextOutside(evidence, from, to, enclosingOpeners[level - 1], CloserAt(level - 1)));
            if (parenthetical.HasValue)
            {
                return parenthetical.Value;
            }
        }

        int outermost = enclosingOpeners.Count - 1;
        return BenchmarkVerdictConsistency.AccusationClauseApproves(
            TextOutside(evidence, outerStart, outerEnd, enclosingOpeners[outermost], CloserAt(outermost))) == true;
    }

    /// <summary>The text from <paramref name="from"/> to <paramref name="to"/> without the parenthetical from <paramref name="opener"/> to <paramref name="closer"/>, both included.</summary>
    private static string TextOutside(string text, int from, int to, int opener, int closer)
    {
        string before = opener > from ? text.Substring(from, opener - from) : string.Empty;
        string after = to > closer + 1 ? text.Substring(closer + 1, to - closer - 1) : string.Empty;
        return before + " " + after;
    }

    private static bool IsClauseBoundary(string text, int index)
        => text[index] is '.' or ';' or ':'
            && index + 1 < text.Length
            && char.IsWhiteSpace(text[index + 1]);

    /// <summary>The text from <paramref name="from"/> to <paramref name="to"/> with the delimited span left out.</summary>
    private static string ClauseAround(string text, int from, int to, int open, int close)
    {
        string before = open > from ? text.Substring(from, open - from) : string.Empty;
        string after = to > close + 1 ? text.Substring(close + 1, to - close - 1) : string.Empty;
        return before + " " + after;
    }

    /// <summary>
    /// <paramref name="text"/> with Markdown emphasis markers dropped and whitespace runs collapsed
    /// to one space, trimmed — the normalisation <see cref="BenchmarkAssessmentParser"/> applies to a
    /// critical-error quote — and, for every character kept, its index in the original.
    /// </summary>
    private static (string Normalized, List<int> Map) NormalizeWithMap(string text)
    {
        var sb = new StringBuilder(text.Length);
        var map = new List<int>(text.Length);
        bool pendingSpace = false;
        int pendingSpaceIndex = 0;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c is '*' or '_' or '`' or '>' or '#')
            {
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                if (!pendingSpace)
                {
                    pendingSpace = true;
                    pendingSpaceIndex = i;
                }
                continue;
            }

            if (pendingSpace && sb.Length > 0)
            {
                sb.Append(' ');
                map.Add(pendingSpaceIndex);
            }
            pendingSpace = false;
            sb.Append(c);
            map.Add(i);
        }

        return (sb.ToString(), map);
    }

    /// <summary>
    /// What an accused sentence is read under: the heading of the list it belongs to, when it is a
    /// list item or fragment, and the text immediately before it. Null when neither exists.
    /// </summary>
    private static string? AccusedQuoteContext(string answerText, int start, string span)
    {
        string? heading = BenchmarkClaimVerificationPrompt.CriticalErrorQuoteContext(answerText, span);

        string prefix = answerText.Substring(0, start).Replace("\r\n", "\n");
        prefix = TrailingListMarkerRegex.Replace(prefix.TrimEnd(), string.Empty).TrimEnd();
        string? preceding = null;
        if (prefix.Length > 0)
        {
            var boundary = PrecedingSentenceBoundaryRegex.Match(prefix, prefix.Length - 1);
            string sentence = (boundary.Success ? prefix.Substring(boundary.Index + 1) : prefix).Trim();
            if (sentence.Length > AccusedQuoteContextMaxLength)
            {
                sentence = "…" + sentence.Substring(sentence.Length - AccusedQuoteContextMaxLength);
            }
            string bareHeading = heading ?? string.Empty;
            if (sentence.Length > 0 && !string.Equals(sentence.Trim('#', '*', ' ', ':'), bareHeading, StringComparison.OrdinalIgnoreCase))
            {
                preceding = sentence;
            }
        }

        return (heading, preceding) switch
        {
            (null, null) => null,
            (not null, null) => $"Under \"{heading}\".",
            (null, not null) => $"Preceded by: \"{preceding}\"",
            _ => $"Under \"{heading}\". Preceded by: \"{preceding}\""
        };
    }

    /// <summary>
    /// <paramref name="claims"/> with a later entry dropped when its text — the answer sentence
    /// <see cref="BenchmarkSuspectedFalseClaim.TryParse"/> extracts from it, or the entry itself when
    /// it carries no <c>Suspected false:</c> marker — equals an earlier kept entry's under
    /// ordinal-ignore-case comparison once trimmed and its whitespace collapsed. A plain duplicate of
    /// a <c>Suspected false:</c> entry is replaced by the marked one rather than dropped, since the
    /// marker carries a reason the plain sentence does not. Order is otherwise preserved.
    /// </summary>
    internal static List<string> DeduplicateUnverifiedClaims(IReadOnlyList<string>? claims, string? answerText)
    {
        if (claims == null || claims.Count == 0)
        {
            return new List<string>();
        }

        var kept = new List<string>();
        var suspectedKept = new List<bool>();
        var indexByKey = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (string claim in claims)
        {
            bool isSuspectedFalse = BenchmarkSuspectedFalseClaim.TryParse(claim, answerText, out string sentence, out _);
            string normalized = isSuspectedFalse ? sentence : claim;
            string key = string.Join(' ', normalized.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

            if (indexByKey.TryGetValue(key, out int index))
            {
                if (isSuspectedFalse && !suspectedKept[index])
                {
                    kept[index] = claim;
                    suspectedKept[index] = true;
                }
                continue;
            }

            indexByKey[key] = kept.Count;
            kept.Add(claim);
            suspectedKept.Add(isSuspectedFalse);
        }

        return kept;
    }

    /// <summary>
    /// The ordered submission manifest. Texts and their order are exactly what
    /// <see cref="WithCriticalErrorQuoteFirst"/> and <see cref="WithOutOfRubricBasis"/> make of the
    /// answer's claims, a <c>Suspected false:</c> entry standing as its sentence alone
    /// (<see cref="BenchmarkSuspectedFalseClaim.TryParse"/>, split against
    /// <paramref name="answerText"/>); assessor statements follow, then accused quotes. An assessor
    /// statement equal to an item already listed, or containing the out-of-rubric basis, is not
    /// submitted: it never takes a role on an answer sentence. An accused quote equal to an item
    /// already listed adds its role, fragments and charge to that item. A critical-error quote or
    /// basis equal to an unverified claim takes that item's place, as the text-matching filters
    /// always read it.
    /// </summary>
    internal static List<ClaimSubmission> BuildClaimManifest(
        IReadOnlyList<string>? unverifiedClaims,
        string? criticalErrorQuote,
        string? outOfRubricBasis,
        IReadOnlyList<AccusedQuote>? accusedQuotes,
        IReadOnlyList<string>? assessorStatements = null,
        string? answerText = null)
    {
        var suspected = new Dictionary<string, (string Entry, string? Reason)>(StringComparer.Ordinal);
        var claimTexts = new List<string>();
        foreach (string entry in DeduplicateUnverifiedClaims(unverifiedClaims, answerText))
        {
            if (BenchmarkSuspectedFalseClaim.TryParse(entry, answerText, out string sentence, out string? reason))
            {
                claimTexts.Add(sentence);
                suspected.TryAdd(sentence, (entry, reason));
            }
            else
            {
                claimTexts.Add(entry);
            }
        }

        List<string> texts = claimTexts;
        if (!string.IsNullOrWhiteSpace(criticalErrorQuote))
        {
            texts = WithCriticalErrorQuoteFirst(texts, criticalErrorQuote);
        }
        if (!string.IsNullOrWhiteSpace(outOfRubricBasis))
        {
            texts = WithOutOfRubricBasis(texts, outOfRubricBasis, string.IsNullOrWhiteSpace(criticalErrorQuote) ? 0 : 1);
        }

        string? quote = criticalErrorQuote?.Trim();
        string? basis = outOfRubricBasis?.Trim();
        var items = new List<ClaimSubmission>();
        foreach (string text in texts)
        {
            string trimmed = text.Trim();
            var itemRoles = new List<string>();
            if (!string.IsNullOrEmpty(quote) && string.Equals(trimmed, quote, StringComparison.Ordinal))
            {
                itemRoles.Add(BenchmarkClaimRoles.CriticalErrorQuote);
            }
            if (!string.IsNullOrEmpty(basis) && string.Equals(trimmed, basis, StringComparison.Ordinal))
            {
                itemRoles.Add(BenchmarkClaimRoles.OutOfRubricBasis);
            }

            var item = new ClaimSubmission(text, itemRoles, null);
            if (itemRoles.Count == 0)
            {
                itemRoles.Add(BenchmarkClaimRoles.UnverifiedClaim);
                if (suspected.TryGetValue(text, out var recorded))
                {
                    item = item with { SuspectedFalse = true, Suspicion = recorded.Reason, RecordedClaim = recorded.Entry };
                }
            }
            items.Add(item);
        }

        foreach (string statement in assessorStatements ?? Array.Empty<string>())
        {
            string trimmed = statement.Trim();
            if (trimmed.Length == 0) continue;
            if (items.Any(i => string.Equals(i.Text.Trim(), trimmed, StringComparison.OrdinalIgnoreCase))) continue;
            if (!string.IsNullOrEmpty(basis) && trimmed.Contains(basis, StringComparison.OrdinalIgnoreCase)) continue;

            items.Add(new ClaimSubmission(trimmed, new List<string> { BenchmarkClaimRoles.AssessorStatement }, null));
        }

        foreach (var accused in accusedQuotes ?? Array.Empty<AccusedQuote>())
        {
            string trimmed = accused.Text.Trim();
            int existing = items.FindIndex(i => string.Equals(i.Text.Trim(), trimmed, StringComparison.Ordinal));
            if (existing >= 0)
            {
                var item = items[existing];
                if (item.Roles.Contains(BenchmarkClaimRoles.AssessorStatement))
                {
                    continue;
                }

                var roles = item.Roles.ToList();
                if (!roles.Contains(BenchmarkClaimRoles.AccusedQuote))
                {
                    roles.Add(BenchmarkClaimRoles.AccusedQuote);
                }
                items[existing] = item with
                {
                    Roles = roles,
                    Context = item.Context ?? accused.Context,
                    QuotedFragments = item.QuotedFragments ?? accused.QuotedFragments,
                    Charge = item.Charge ?? accused.Charge
                };
                continue;
            }

            items.Add(new ClaimSubmission(trimmed, new List<string> { BenchmarkClaimRoles.AccusedQuote }, accused.Context)
            {
                QuotedFragments = accused.QuotedFragments,
                Charge = accused.Charge
            });
        }

        return items;
    }

    /// <summary>
    /// Each verification stamped with the manifest item at its claim index: its roles, and the
    /// fragments, charge and suspected-false record that item carries.
    /// </summary>
    internal static List<BenchmarkClaimVerification> StampRoles(
        IReadOnlyList<BenchmarkClaimVerification> verifications,
        IReadOnlyList<ClaimSubmission> manifest)
        => verifications
            .Select(v =>
            {
                if (v.ClaimIndex < 0 || v.ClaimIndex >= manifest.Count)
                {
                    return v with { Roles = null };
                }

                var item = manifest[v.ClaimIndex];
                return v with
                {
                    Roles = item.Roles.ToList(),
                    QuotedFragments = item.QuotedFragments?.ToList(),
                    Charge = item.Charge,
                    SuspectedFalse = item.SuspectedFalse ? true : null,
                    Suspicion = item.Suspicion,
                    RecordedClaim = item.RecordedClaim
                };
            })
            .ToList();

    /// <summary>
    /// The verifications of the answer's own claims. A list carrying roles is filtered by role; a
    /// legacy list without roles by the exact-text rule it was always read by: every entry except the
    /// out-of-rubric basis and the critical-error quote.
    /// </summary>
    internal static List<BenchmarkClaimVerification> OrdinaryClaimVerifications(
        IReadOnlyList<BenchmarkClaimVerification>? verifications,
        BenchmarkRunAnswer answer)
    {
        if (verifications == null) return new List<BenchmarkClaimVerification>();
        if (BenchmarkClaimRoles.HasRoles(verifications))
        {
            return verifications.Where(BenchmarkClaimRoles.IsOrdinaryClaim).ToList();
        }

        return WithoutCriticalErrorQuote(
            WithoutOutOfRubricBasis(verifications, OutOfRubricBasisOf(answer)),
            answer.CriticalErrorQuote);
    }

    /// <summary>
    /// The sentences the assessor charged as false that the verifier supported with a citation.
    /// Empty for a legacy list, which never carries the accused role.
    /// </summary>
    internal static List<BenchmarkClaimVerification> SupportedAccusations(IReadOnlyList<BenchmarkClaimVerification>? verifications)
        => (verifications ?? Array.Empty<BenchmarkClaimVerification>())
            .Where(v => BenchmarkClaimRoles.HasRole(v, BenchmarkClaimRoles.AccusedQuote)
                && v.EffectiveVerdict == BenchmarkClaimVerdict.Supported
                && !string.IsNullOrWhiteSpace(v.Citation))
            .ToList();

    /// <summary>
    /// The statements of the assessor's own evidence that the verifier refuted with a citation: the
    /// assessor was wrong, so the Accuracy deduction they belong to is contested. Empty for a legacy
    /// list, which never carries the role.
    /// </summary>
    internal static List<BenchmarkClaimVerification> RefutedAssessorStatements(IReadOnlyList<BenchmarkClaimVerification>? verifications)
        => (verifications ?? Array.Empty<BenchmarkClaimVerification>())
            .Where(v => BenchmarkClaimRoles.HasRole(v, BenchmarkClaimRoles.AssessorStatement)
                && v.EffectiveVerdict == BenchmarkClaimVerdict.Refuted
                && !string.IsNullOrWhiteSpace(v.Citation))
            .ToList();
}

