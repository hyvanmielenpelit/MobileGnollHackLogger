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

public class BenchmarkRubricCheckService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly BenchmarkRubricCheckJobManager _jobManager;
    private readonly AgentLoopRunner _agentLoopRunner;
    private readonly CryptoService _cryptoService;
    private readonly ILogger<BenchmarkRubricCheckService> _logger;

    public BenchmarkRubricCheckService(
        IServiceScopeFactory scopeFactory,
        BenchmarkRubricCheckJobManager jobManager,
        AgentLoopRunner agentLoopRunner,
        CryptoService cryptoService,
        ILogger<BenchmarkRubricCheckService> logger)
    {
        _scopeFactory = scopeFactory;
        _jobManager = jobManager;
        _agentLoopRunner = agentLoopRunner;
        _cryptoService = cryptoService;
        _logger = logger;
    }

    public async Task RunRubricCheckAsync(string jobId, CancellationToken ct)
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
                job.SetStatus(BenchmarkRubricCheckJobStatus.Failed);
                return;
            }

            var config = await db.SystemAiApiConfigurations
                .FirstOrDefaultAsync(c => c.Id == job.CheckerConfigId, ct);
            if (config == null || !config.IsEnabled)
            {
                job.AddLog("Checker model configuration not found or disabled.", "error");
                job.SetStatus(BenchmarkRubricCheckJobStatus.Failed);
                return;
            }

            string apiKey = _cryptoService.Decrypt(config.EncryptedApiKey, config.ApiKeyNonce!, config.ApiKeyTag!, "SYSTEM_API_KEY");

            bool hasErrors = false;
            int maxOutputTokens = Math.Max(config.MaxOutputTokens ?? 4096, 4096);

            foreach (var item in job.Items)
            {
                ct.ThrowIfCancellationRequested();

                var question = suite.Questions.FirstOrDefault(q => q.Id == item.QuestionId);
                if (question == null)
                {
                    item.Status = BenchmarkRubricCheckItemStatus.Failed;
                    item.ErrorMessage = "Question not found in suite.";
                    hasErrors = true;
                    continue;
                }

                item.Status = BenchmarkRubricCheckItemStatus.Checking;
                job.AddLog($"Checking rubric facts for question {question.OrderIndex} (ID: {question.Id}) using {config.DisplayName}...");

                string prompt = BenchmarkRubricCheckPrompt.BuildPrompt(suite.GameSnapshot, question);
                var (runResult, sw, terminalError) = await ExecuteModelCallAsync(config, apiKey, prompt, maxOutputTokens, ct);

                int promptTokens = runResult.TotalPromptTokens > 0 ? runResult.TotalPromptTokens : runResult.EstimatedInputTokens;
                int outputTokens = runResult.OutputTokens > 0 ? runResult.OutputTokens : runResult.EstimatedOutputTokens;
                job.AddUsage(promptTokens, outputTokens);

                await configService.RecordUsageAsync(
                    config.Id,
                    job.StartedByUserId,
                    promptTokens,
                    outputTokens,
                    roleContext: 6,
                    cacheReadTokens: runResult.CacheReadTokens,
                    cacheCreationTokens: runResult.CacheCreationTokens);

                if (terminalError != null)
                {
                    job.AddLog($"Terminal provider error checking question {question.OrderIndex}: {terminalError}", "error");
                    job.SetItemFailed(item.QuestionId, terminalError);
                    hasErrors = true;
                    continue;
                }

                string responseText = runResult.FinalText ?? string.Empty;
                string rubricText = question.ExpectedPoints ?? string.Empty;
                var parseResult = BenchmarkRubricCheckParser.Parse(responseText, rubricText);

                if (!parseResult.Success)
                {
                    job.AddLog($"Initial response failed validation for question {question.OrderIndex}. Attempting repair...", "warning");

                    string repairPrompt = BenchmarkRubricCheckPrompt.BuildRepairPrompt(
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
                        roleContext: 6,
                        cacheReadTokens: repairResult.CacheReadTokens,
                        cacheCreationTokens: repairResult.CacheCreationTokens);

                    if (repairError != null)
                    {
                        job.AddLog($"Repair call encountered terminal provider error: {repairError}", "error");
                        job.SetItemFailed(item.QuestionId, repairError);
                        hasErrors = true;
                        continue;
                    }

                    parseResult = BenchmarkRubricCheckParser.Parse(repairResult.FinalText ?? string.Empty, rubricText);
                }

                if (!parseResult.Success)
                {
                    string err = string.Join("; ", parseResult.ValidationErrors);
                    job.AddLog($"Rubric check for question {question.OrderIndex} failed: {err}", "error");
                    job.SetItemFailed(item.QuestionId, err);
                    hasErrors = true;
                    continue;
                }

                foreach (var df in parseResult.DiscardedFindings)
                {
                    job.AddLog($"Q{question.OrderIndex}: {df}", "warning");
                }

                job.SetItemResult(item.QuestionId, parseResult.Verdict, parseResult.Findings);
                job.AddLog($"Q{question.OrderIndex} verdict: {parseResult.Verdict} ({parseResult.Findings.Count} finding(s)).");
            }

            job.SetStatus(hasErrors
                ? BenchmarkRubricCheckJobStatus.CompletedWithErrors
                : BenchmarkRubricCheckJobStatus.Completed);
        }
        catch (OperationCanceledException)
        {
            job.AddLog("Rubric verification was cancelled.", "warning");
            job.SetStatus(BenchmarkRubricCheckJobStatus.Cancelled);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Rubric verification job {JobId} failed.", jobId);
            job.AddLog($"Unexpected failure: {ex.Message}", "error");
            job.SetStatus(BenchmarkRubricCheckJobStatus.Failed);
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
            SystemPrompt = "You are an objective GnollHack verification and fact-checking engine.",
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
