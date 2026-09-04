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
                job.AddLog("Suite not found or has no game board bound to it.", "error");
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
            bool isFirstBand = true;

            foreach (var item in job.Items)
            {
                if (item.RequestedCount <= 0)
                {
                    item.Status = BenchmarkGenerationItemStatus.Skipped;
                    continue;
                }

                ct.ThrowIfCancellationRequested();

                item.Status = BenchmarkGenerationItemStatus.Generating;
                job.AddLog($"Generating {item.RequestedCount} {item.Difficulty} questions using {config.DisplayName}...");

                var existingQuestions = suite.Questions.Select(q => q.QuestionText).ToList();
                string prompt = BenchmarkGenerationPrompt.BuildPrompt(
                    suite.GameSnapshot,
                    job.Instructions,
                    item.Difficulty,
                    item.RequestedCount,
                    existingQuestions,
                    isFirstBand: isFirstBand);

                int maxOutputTokens = Math.Max(config.MaxOutputTokens ?? 8192, 8192);
                var (runResult, sw, terminalError) = await ExecuteModelCallAsync(config, apiKey, prompt, maxOutputTokens, ct);

                int promptTokens = runResult.TotalPromptTokens > 0 ? runResult.TotalPromptTokens : runResult.EstimatedInputTokens;
                int outputTokens = runResult.OutputTokens > 0 ? runResult.OutputTokens : runResult.EstimatedOutputTokens;
                job.AddUsage(promptTokens, outputTokens);

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
                    job.AddLog($"Terminal provider error: {terminalError}", "error");
                    item.Status = BenchmarkGenerationItemStatus.Failed;
                    item.ErrorMessage = terminalError;
                    hasErrors = true;
                    continue;
                }

                string responseText = runResult.FinalText ?? string.Empty;
                var parseResult = BenchmarkGenerationParser.Parse(responseText, item.RequestedCount);

                if (!parseResult.Success)
                {
                    job.AddLog($"Initial response failed validation ({string.Join("; ", parseResult.ValidationErrors)}). Attempting repair...", "warning");

                    string repairPrompt = BenchmarkGenerationPrompt.BuildRepairPrompt(
                        responseText,
                        string.Join("; ", parseResult.ValidationErrors));

                    var (repairResult, repairSw, repairError) = await ExecuteModelCallAsync(config, apiKey, repairPrompt, maxOutputTokens, ct);

                    int repairPromptTokens = repairResult.TotalPromptTokens > 0 ? repairResult.TotalPromptTokens : repairResult.EstimatedInputTokens;
                    int repairOutputTokens = repairResult.OutputTokens > 0 ? repairResult.OutputTokens : repairResult.EstimatedOutputTokens;
                    job.AddUsage(repairPromptTokens, repairOutputTokens);

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
                        job.AddLog($"Repair call encountered terminal provider error: {repairError}", "error");
                        item.Status = BenchmarkGenerationItemStatus.Failed;
                        item.ErrorMessage = repairError;
                        hasErrors = true;
                        continue;
                    }

                    parseResult = BenchmarkGenerationParser.Parse(repairResult.FinalText ?? string.Empty, item.RequestedCount);
                }

                if (!parseResult.Success || parseResult.Questions.Count == 0)
                {
                    string err = string.Join("; ", parseResult.ValidationErrors);
                    job.AddLog($"Generation for {item.Difficulty} failed: {err}", "error");
                    item.Status = BenchmarkGenerationItemStatus.Failed;
                    item.ErrorMessage = err;
                    hasErrors = true;
                    continue;
                }

                if (isFirstBand && !string.IsNullOrWhiteSpace(parseResult.BoardDigest))
                {
                    suite.GameSnapshot.DigestText = parseResult.BoardDigest;
                    suite.GameSnapshot.ModifiedAtUtc = DateTime.UtcNow;
                    job.AddLog("Updated board digest from generated response.");
                }

                int maxOrder = suite.Questions.Count > 0 ? suite.Questions.Max(q => q.OrderIndex) : 0;
                foreach (var qItem in parseResult.Questions)
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
                }

                suite.HasGeneratedQuestions = true;
                suite.ModifiedAtUtc = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);

                item.Status = BenchmarkGenerationItemStatus.Completed;
                item.GeneratedCount = parseResult.Questions.Count;
                job.AddLog($"Successfully generated {item.GeneratedCount} {item.Difficulty} questions.");

                if (item.GeneratedCount < item.RequestedCount)
                {
                    hasErrors = true;
                }

                isFirstBand = false;
            }

            job.SetStatus(hasErrors
                ? BenchmarkGenerationJobStatus.CompletedWithErrors
                : BenchmarkGenerationJobStatus.Completed);
        }
        catch (OperationCanceledException)
        {
            job.AddLog("Question generation was cancelled.", "warning");
            job.SetStatus(BenchmarkGenerationJobStatus.Cancelled);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Question generation job {JobId} failed.", jobId);
            job.AddLog($"Unexpected failure: {ex.Message}", "error");
            job.SetStatus(BenchmarkGenerationJobStatus.Failed);
        }
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

        return (runResult, sw, terminalError);
    }
}
