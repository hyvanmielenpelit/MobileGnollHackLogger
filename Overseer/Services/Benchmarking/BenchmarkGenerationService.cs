namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services;
using Overseer.Services.Agents;

public class BenchmarkGenerationService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly BenchmarkGenerationJobManager _jobManager;
    private readonly AgentLoopRunner _agentLoopRunner;
    private readonly CryptoService _cryptoService;
    private readonly BenchmarkComplianceGuard _complianceGuard;
    private readonly ILogger<BenchmarkGenerationService> _logger;

    public BenchmarkGenerationService(
        IServiceScopeFactory scopeFactory,
        BenchmarkGenerationJobManager jobManager,
        AgentLoopRunner agentLoopRunner,
        CryptoService cryptoService,
        BenchmarkComplianceGuard complianceGuard,
        ILogger<BenchmarkGenerationService> logger)
    {
        _scopeFactory = scopeFactory;
        _jobManager = jobManager;
        _agentLoopRunner = agentLoopRunner;
        _cryptoService = cryptoService;
        _complianceGuard = complianceGuard;
        _logger = logger;
    }

    public async Task RunGenerationAsync(string jobId, CancellationToken ct)
    {
        var job = _jobManager.TryGet(jobId);
        if (job == null) return;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var configService = scope.ServiceProvider.GetRequiredService<SystemAiConfigService>();

            var suite = await db.BenchmarkSuites
                .Include(s => s.GameSnapshot)
                .Include(s => s.Questions)
                .FirstOrDefaultAsync(s => s.Id == job.SuiteId, ct);

            if (suite == null || suite.GameSnapshot == null)
            {
                job.AddLog("Suite not found or has no game snapshot bound to it.", "error");
                job.SetStatus(BenchmarkGenerationJobStatus.Failed);
                return;
            }

            int totalRequested = job.Items.Sum(i => i.RequestedCount);
            var (canAdd, complianceMsg) = await _complianceGuard.CanAddQuestionsAsync(job.SuiteId, totalRequested);
            if (!canAdd)
            {
                job.AddLog(complianceMsg ?? "Compliance check failed for adding questions to suite.", "error");
                job.SetStatus(BenchmarkGenerationJobStatus.Failed);
                return;
            }

            var config = await db.SystemAiApiConfigurations
                .FirstOrDefaultAsync(c => c.Id == job.GeneratorConfigId, ct);
            if (config == null || !config.IsEnabled)
            {
                job.AddLog("Generator model configuration not found or disabled.", "error");
                job.SetStatus(BenchmarkGenerationJobStatus.Failed);
                return;
            }

            string apiKey = _cryptoService.Decrypt(config.EncryptedApiKey!, config.ApiKeyNonce!, config.ApiKeyTag!, "SYSTEM_API_KEY");

            bool hasErrors = false;
            int maxOutputTokens = Math.Max(config.MaxOutputTokens ?? 8192, 8192);

            foreach (var item in job.Items)
            {
                if (item.RequestedCount <= 0)
                {
                    job.SetItemStatus(item, BenchmarkGenerationItemStatus.Skipped);
                    continue;
                }

                ct.ThrowIfCancellationRequested();

                item.StartedAtUtc = DateTime.UtcNow;
                try
                {
                    bool itemHadError = item.Kind switch
                    {
                        BenchmarkGenerationItemKind.RubricOnly =>
                            await RunRubricOnlyItemAsync(job, item, suite, db, config, apiKey, maxOutputTokens, configService, ct),
                        BenchmarkGenerationItemKind.ReplaceQuestion =>
                            await RunReplaceQuestionItemAsync(job, item, suite, db, config, apiKey, maxOutputTokens, configService, ct),
                        _ =>
                            await RunBandItemAsync(job, item, suite, db, config, apiKey, maxOutputTokens, configService, ct)
                    };

                    if (itemHadError) hasErrors = true;
                }
                finally
                {
                    item.CompletedAtUtc = DateTime.UtcNow;
                }
            }

            job.SetStatus(hasErrors
                ? BenchmarkGenerationJobStatus.CompletedWithErrors
                : BenchmarkGenerationJobStatus.Completed);
        }
        catch (OperationCanceledException)
        {
            job.AddLog("Question generation was cancelled.", "warning");
            job.MarkUnfinishedItemsCancelled();
            job.SetStatus(BenchmarkGenerationJobStatus.Cancelled);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Question generation job {JobId} failed.", jobId);
            job.AddLog($"Unexpected failure: {ExceptionDetails.DescribeShort(ex)}", "error", ExceptionDetails.Describe(ex));
            job.SetStatus(BenchmarkGenerationJobStatus.Failed);
        }
    }

    /// <summary>
    /// A batch of new questions for one difficulty band. When the item carries discard ids
    /// (a discard-and-regenerate retry), the previously generated rows are removed and the
    /// suite's order renumbered before the prompt is built.
    /// </summary>
    private async Task<bool> RunBandItemAsync(
        BenchmarkGenerationJob job,
        BenchmarkGenerationJobItem item,
        BenchmarkSuite suite,
        ApplicationDbContext db,
        SystemAiApiConfiguration config,
        string apiKey,
        int maxOutputTokens,
        SystemAiConfigService configService,
        CancellationToken ct)
    {
        if (item.QuestionIdsToDiscard.Count > 0)
        {
            var discardIds = new HashSet<long>(item.QuestionIdsToDiscard);
            var toRemove = suite.Questions.Where(q => discardIds.Contains(q.Id) && q.BenchmarkSuiteId == suite.Id).ToList();
            if (toRemove.Count > 0)
            {
                db.BenchmarkQuestions.RemoveRange(toRemove);
                foreach (var q in toRemove)
                {
                    suite.Questions.Remove(q);
                }
                await db.SaveChangesAsync(ct);

                var remaining = suite.Questions.OrderBy(q => q.OrderIndex).ToList();
                for (int i = 0; i < remaining.Count; i++)
                {
                    remaining[i].OrderIndex = i + 1;
                }
                await db.SaveChangesAsync(ct);

                job.AddLog($"Discarded {toRemove.Count} previously generated {item.Difficulty} questions.");
            }
        }

        job.SetItemStatus(item, BenchmarkGenerationItemStatus.Generating);
        job.AddLog($"Generating {item.RequestedCount} {item.Difficulty} questions using {config.DisplayName}...");

        var existingQuestions = suite.Questions.Select(q => q.QuestionText).ToList();
        string prompt = BenchmarkGenerationPrompt.BuildPrompt(
            suite.GameSnapshot!,
            job.Instructions,
            item.Difficulty,
            item.RequestedCount,
            existingQuestions);

        var (success, questions, errorMessage) = await GenerateWithRepairAsync(
            job, item, config, apiKey, prompt, item.RequestedCount, maxOutputTokens, configService, ct);

        if (!success)
        {
            job.SetItemStatus(item, BenchmarkGenerationItemStatus.Failed, errorMessage);
            return true;
        }

        int maxOrder = suite.Questions.Count > 0 ? suite.Questions.Max(q => q.OrderIndex) : 0;
        var newQuestions = new List<BenchmarkQuestion>();
        foreach (var qItem in questions)
        {
            maxOrder++;
            var question = new BenchmarkQuestion
            {
                BenchmarkSuiteId = suite.Id,
                OrderIndex = maxOrder,
                QuestionText = qItem.QuestionText,
                Difficulty = item.Difficulty,
                ExpectedPoints = qItem.ExpectedPoints,
                ItemRevision = 1,
                IsGenerated = true,
                ReviewedAtRevision = null,
                ReviewedAtUtc = null,
                ReviewedByUserId = null,
                AssessedDifficulty = null,
                CreatedAtUtc = DateTime.UtcNow,
                ModifiedAtUtc = DateTime.UtcNow
            };
            db.BenchmarkQuestions.Add(question);
            suite.Questions.Add(question);
            newQuestions.Add(question);
        }

        suite.HasGeneratedQuestions = true;
        suite.ModifiedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        foreach (var q in newQuestions)
        {
            item.CreatedQuestionIds.Add(q.Id);
        }

        job.SetItemStatus(item, BenchmarkGenerationItemStatus.Completed, generatedCount: questions.Count);
        job.AddLog($"Successfully generated {questions.Count} {item.Difficulty} questions.");

        return questions.Count < item.RequestedCount;
    }

    /// <summary>A replacement rubric for one existing question; the question text is untouched.</summary>
    private async Task<bool> RunRubricOnlyItemAsync(
        BenchmarkGenerationJob job,
        BenchmarkGenerationJobItem item,
        BenchmarkSuite suite,
        ApplicationDbContext db,
        SystemAiApiConfiguration config,
        string apiKey,
        int maxOutputTokens,
        SystemAiConfigService configService,
        CancellationToken ct)
    {
        var question = suite.Questions.FirstOrDefault(q => q.Id == item.TargetQuestionId);
        if (question == null)
        {
            job.SetItemStatus(item, BenchmarkGenerationItemStatus.Failed, "Question no longer exists.");
            job.AddLog($"Rubric regeneration for Q{item.TargetQuestionOrderIndex} failed: question no longer exists.", "error");
            return true;
        }

        job.SetItemStatus(item, BenchmarkGenerationItemStatus.Generating);
        job.AddLog($"Regenerating the rubric for Q{question.OrderIndex} using {config.DisplayName}...");

        string prompt = BenchmarkGenerationPrompt.BuildRubricOnlyPrompt(
            suite.GameSnapshot!, job.Instructions, question.Difficulty, question.QuestionText);

        var (success, questions, errorMessage) = await GenerateWithRepairAsync(
            job, item, config, apiKey, prompt, 1, maxOutputTokens, configService, ct);

        if (!success)
        {
            job.SetItemStatus(item, BenchmarkGenerationItemStatus.Failed, errorMessage);
            return true;
        }

        question.ExpectedPoints = questions[0].ExpectedPoints;
        question.IsGenerated = true;
        BenchmarkQuestionAssessment.Clear(question);
        question.ModifiedAtUtc = DateTime.UtcNow;
        suite.HasGeneratedQuestions = true;
        suite.ModifiedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        item.UpdatedQuestionIds.Add(question.Id);
        job.SetItemStatus(item, BenchmarkGenerationItemStatus.Completed, generatedCount: 1);
        job.AddLog($"Successfully regenerated the rubric for Q{question.OrderIndex}.");

        return false;
    }

    /// <summary>A replacement question and rubric for one existing question.</summary>
    private async Task<bool> RunReplaceQuestionItemAsync(
        BenchmarkGenerationJob job,
        BenchmarkGenerationJobItem item,
        BenchmarkSuite suite,
        ApplicationDbContext db,
        SystemAiApiConfiguration config,
        string apiKey,
        int maxOutputTokens,
        SystemAiConfigService configService,
        CancellationToken ct)
    {
        var question = suite.Questions.FirstOrDefault(q => q.Id == item.TargetQuestionId);
        if (question == null)
        {
            job.SetItemStatus(item, BenchmarkGenerationItemStatus.Failed, "Question no longer exists.");
            job.AddLog($"Question regeneration for Q{item.TargetQuestionOrderIndex} failed: question no longer exists.", "error");
            return true;
        }

        job.SetItemStatus(item, BenchmarkGenerationItemStatus.Generating);
        job.AddLog($"Regenerating Q{question.OrderIndex} and its rubric using {config.DisplayName}...");

        var existingQuestions = suite.Questions.Where(q => q.Id != question.Id).Select(q => q.QuestionText).ToList();
        string prompt = BenchmarkGenerationPrompt.BuildPrompt(
            suite.GameSnapshot!,
            job.Instructions,
            question.Difficulty,
            1,
            existingQuestions,
            replacingQuestionText: question.QuestionText);

        var (success, questions, errorMessage) = await GenerateWithRepairAsync(
            job, item, config, apiKey, prompt, 1, maxOutputTokens, configService, ct);

        if (!success)
        {
            job.SetItemStatus(item, BenchmarkGenerationItemStatus.Failed, errorMessage);
            return true;
        }

        question.QuestionText = questions[0].QuestionText;
        question.ExpectedPoints = questions[0].ExpectedPoints;
        question.IsGenerated = true;
        BenchmarkQuestionAssessment.Clear(question);
        question.ModifiedAtUtc = DateTime.UtcNow;
        suite.HasGeneratedQuestions = true;
        suite.ModifiedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        item.UpdatedQuestionIds.Add(question.Id);
        job.SetItemStatus(item, BenchmarkGenerationItemStatus.Completed, generatedCount: 1);
        job.AddLog($"Successfully regenerated Q{question.OrderIndex} and its rubric.");

        return false;
    }

    /// <summary>
    /// Runs the model once, and once more with a repair prompt when the first response fails
    /// parsing or validation. Usage from both calls is recorded against <paramref name="item"/>
    /// and the job total via <see cref="BenchmarkGenerationJob.SetItemUsage"/>.
    /// </summary>
    private async Task<(bool Success, List<GeneratedQuestionItem> Questions, string? ErrorMessage)> GenerateWithRepairAsync(
        BenchmarkGenerationJob job,
        BenchmarkGenerationJobItem item,
        SystemAiApiConfiguration config,
        string apiKey,
        string prompt,
        int requestedCount,
        int maxOutputTokens,
        SystemAiConfigService configService,
        CancellationToken ct)
    {
        var (runResult, _, terminalError) = await ExecuteModelCallAsync(config, apiKey, prompt, maxOutputTokens, ct);

        int promptTokens = runResult.TotalPromptTokens > 0 ? runResult.TotalPromptTokens : runResult.EstimatedInputTokens;
        int outputTokens = runResult.OutputTokens > 0 ? runResult.OutputTokens : runResult.EstimatedOutputTokens;
        job.SetItemUsage(item, promptTokens, outputTokens);

        await configService.RecordUsageAsync(
            config.Id,
            job.StartedByUserId,
            promptTokens,
            outputTokens,
            roleContext: 5,
            cacheReadTokens: runResult.CacheReadTokens,
            cacheCreationTokens: runResult.CacheCreationTokens);

        if (terminalError != null)
        {
            job.AddLog($"Terminal provider error: {terminalError}", "error", terminalError);
            return (false, new List<GeneratedQuestionItem>(), terminalError);
        }

        string responseText = runResult.FinalText ?? string.Empty;
        var parseResult = BenchmarkGenerationParser.Parse(responseText, requestedCount);

        if (!parseResult.Success)
        {
            job.AddLog($"Initial response failed validation ({string.Join("; ", parseResult.ValidationErrors)}). Attempting repair...", "warning");
            job.SetItemStatus(item, BenchmarkGenerationItemStatus.Repairing);

            string repairPrompt = BenchmarkGenerationPrompt.BuildRepairPrompt(
                responseText,
                string.Join("; ", parseResult.ValidationErrors));

            var (repairResult, _, repairError) = await ExecuteModelCallAsync(config, apiKey, repairPrompt, maxOutputTokens, ct);

            int repairPromptTokens = repairResult.TotalPromptTokens > 0 ? repairResult.TotalPromptTokens : repairResult.EstimatedInputTokens;
            int repairOutputTokens = repairResult.OutputTokens > 0 ? repairResult.OutputTokens : repairResult.EstimatedOutputTokens;
            job.SetItemUsage(item, repairPromptTokens, repairOutputTokens);

            await configService.RecordUsageAsync(
                config.Id,
                job.StartedByUserId,
                repairPromptTokens,
                repairOutputTokens,
                roleContext: 5,
                cacheReadTokens: repairResult.CacheReadTokens,
                cacheCreationTokens: repairResult.CacheCreationTokens);

            if (repairError != null)
            {
                job.AddLog($"Repair call encountered terminal provider error: {repairError}", "error", repairError);
                return (false, new List<GeneratedQuestionItem>(), repairError);
            }

            responseText = repairResult.FinalText ?? string.Empty;
            parseResult = BenchmarkGenerationParser.Parse(responseText, requestedCount);
        }

        if (!parseResult.Success || parseResult.Questions.Count == 0)
        {
            string err = string.Join("; ", parseResult.ValidationErrors);
            string excerpt = responseText.Length <= 2000 ? responseText : responseText[..2000];
            job.AddLog($"Generation failed: {err}", "error", excerpt);
            return (false, new List<GeneratedQuestionItem>(), err);
        }

        return (true, parseResult.Questions, null);
    }

    private async Task<(AgentRunResult Result, Stopwatch Sw, string? TerminalError)> ExecuteModelCallAsync(
        SystemAiApiConfiguration config,
        string apiKey,
        string prompt,
        int maxOutputTokens,
        CancellationToken ct)
    {
        var runRequest = new AgentRunRequest
        {
            ProviderName = config.Provider,
            ModelId = config.ModelId,
            ApiKey = apiKey,
            ModelDisplayName = config.DisplayName,
            SystemPrompt = "You are an expert GnollHack benchmark author and game mechanics expert.",
            ThinkingLevel = config.ThinkingLevel,
            ReasoningMode = config.ReasoningMode,
            ReasoningSummary = config.ReasoningSummary,
            ServiceTier = config.ServiceTier,
            MaxOutputTokens = maxOutputTokens,
            MaxToolIterations = 0,
            EnableToolUse = false,
            EnableWebSearch = false,
            EnableSubAgents = false,
            SystemModelId = config.Id,
            Budget = new AgentRunBudget { MaxTotalModelCalls = 2 },
            SeedHistory = new List<object>
            {
                new { role = "user", content = prompt }
            }
        };

        var runResult = new AgentRunResult();
        var sw = Stopwatch.StartNew();
        string? terminalError = null;

        await foreach (var evt in _agentLoopRunner.RunAsync(runRequest, runRequest.Budget, runResult, ct))
        {
            if (evt.Type == "error")
            {
                terminalError = evt.Data?.ToString();
            }
        }
        sw.Stop();

        ct.ThrowIfCancellationRequested();

        return (runResult, sw, terminalError);
    }
}
