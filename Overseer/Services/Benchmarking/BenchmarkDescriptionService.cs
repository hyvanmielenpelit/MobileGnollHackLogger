namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services;
using Overseer.Services.Agents;
using Overseer.Services.Privacy;
using Overseer.Services.Providers;

/// <summary>
/// Drafts a suite description with a single model call. Synchronous by design: the result, its
/// timing, token usage, cost and log are returned in one response, and nothing is written to the
/// suite. The operator edits the draft and saves it through the ordinary suite update.
/// </summary>
public class BenchmarkDescriptionService
{
    /// <summary>The SystemAiUsageLog.RoleContext value for suite description generation.</summary>
    public const int UsageRoleContext = 7;

    private const int ExcerptLength = 2000;

    private readonly ApplicationDbContext _db;
    private readonly AgentLoopRunner _agentLoopRunner;
    private readonly CryptoService _cryptoService;
    private readonly EndpointPolicy _endpointPolicy;
    private readonly SystemAiConfigService _configService;
    private readonly ModelPricingService _pricingService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<BenchmarkDescriptionService> _logger;

    public BenchmarkDescriptionService(
        ApplicationDbContext db,
        AgentLoopRunner agentLoopRunner,
        CryptoService cryptoService,
        EndpointPolicy endpointPolicy,
        SystemAiConfigService configService,
        ModelPricingService pricingService,
        IConfiguration configuration,
        ILogger<BenchmarkDescriptionService> logger)
    {
        _db = db;
        _agentLoopRunner = agentLoopRunner;
        _cryptoService = cryptoService;
        _endpointPolicy = endpointPolicy;
        _configService = configService;
        _pricingService = pricingService;
        _configuration = configuration;
        _logger = logger;
    }

    /// <exception cref="KeyNotFoundException">The suite does not exist.</exception>
    /// <exception cref="InvalidOperationException">The generator configuration is unusable; the message is user-facing.</exception>
    public async Task<SuiteDescriptionGenerationResultDto> GenerateAsync(
        long suiteId,
        GenerateSuiteDescriptionRequest request,
        string? userId,
        CancellationToken ct)
    {
        var suite = await _db.BenchmarkSuites
            .Include(s => s.GameSnapshot)
            .Include(s => s.Questions)
            .FirstOrDefaultAsync(s => s.Id == suiteId, ct)
            ?? throw new KeyNotFoundException("Benchmark suite not found.");

        var config = await _db.SystemAiApiConfigurations
            .FirstOrDefaultAsync(c => c.Id == request.GeneratorModelConfigurationId, ct);
        if (config == null || !config.IsEnabled || string.IsNullOrWhiteSpace(config.EncryptedApiKey) || (config.ModelRole & 4) != 4)
        {
            throw new InvalidOperationException(
                "The selected generator model is invalid, disabled, missing an API key, or not configured with the Benchmark role.");
        }

        if (!_endpointPolicy.TryResolveStrict(config.BaseUrl, config.CustomHeadersJson, config.ApiVersion, out var endpoint, out var endpointError))
        {
            throw new InvalidOperationException(
                $"Configuration '{config.DisplayName}': its custom endpoint is not allowed by the endpoint policy: {endpointError}");
        }

        var questions = suite.Questions.OrderBy(q => q.OrderIndex).ToList();
        var snapshot = request.IncludeSnapshot ? suite.GameSnapshot : null;

        var result = new SuiteDescriptionGenerationResultDto
        {
            SuiteId = suite.Id,
            SuiteName = suite.Name,
            QuestionCount = questions.Count,
            SnapshotIncluded = snapshot != null,
            GameSnapshotName = suite.GameSnapshot?.Name,
            SnapshotCharCount = snapshot?.SanitizedText?.Length ?? 0,
            GeneratorConfigId = config.Id,
            GeneratorDisplayName = config.DisplayName ?? config.ModelId,
            GeneratorProvider = config.Provider,
            GeneratorModelId = config.ModelId,
            GeneratorThinkingLevel = config.ThinkingLevel,
            GeneratorReasoningMode = config.ReasoningMode,
            GeneratorServiceTier = config.ServiceTier,
            StartedAtUtc = DateTime.UtcNow
        };

        var runResult = new AgentRunResult();
        var sw = new Stopwatch();
        bool usageFinalized = false;

        try
        {
            AddLog(result, $"Loaded {questions.Count} questions.");
            if (snapshot != null)
            {
                AddLog(result, $"Snapshot included ({result.SnapshotCharCount} chars).");
            }
            else if (suite.GameSnapshot != null)
            {
                AddLog(result, "Snapshot attached but excluded by request.");
            }
            else
            {
                AddLog(result, "No snapshot.");
            }

            string instructions = string.IsNullOrWhiteSpace(request.Instructions)
                ? BenchmarkDescriptionPrompt.DefaultInstructions
                : request.Instructions.Trim();

            string prompt = BenchmarkDescriptionPrompt.BuildPrompt(suite.Name, questions, snapshot, instructions);
            result.PromptCharCount = prompt.Length;
            if (request.IncludeDebugText)
            {
                result.PromptText = prompt;
            }
            AddLog(result, $"Prompt built ({prompt.Length} chars).");

            string apiKey = _cryptoService.Decrypt(config.EncryptedApiKey!, config.ApiKeyNonce!, config.ApiKeyTag!, "SYSTEM_API_KEY");
            int maxOutputTokens = _configuration.GetValue<int?>("Benchmark:DescriptionMaxOutputTokens") ?? 4096;

            var runRequest = new AgentRunRequest
            {
                ProviderName = config.Provider,
                ModelId = config.ModelId,
                ApiKey = apiKey,
                Endpoint = endpoint,
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
                PromptCacheKey = $"benchmark:description:{config.ModelId}",
                CacheConversationTail = false,
                Budget = new AgentRunBudget { MaxTotalModelCalls = 1 },
                SeedHistory = new List<object>
                {
                    new { role = "user", content = prompt }
                }
            };

            AddLog(result, $"Calling {result.GeneratorDisplayName} (max {maxOutputTokens} output tokens).");

            string? terminalError = null;
            sw.Start();
            await foreach (var evt in _agentLoopRunner.RunAsync(runRequest, runRequest.Budget, runResult, ct))
            {
                if (evt.Type == "error")
                {
                    terminalError = evt.Data?.ToString();
                }
            }
            sw.Stop();

            ct.ThrowIfCancellationRequested();

            usageFinalized = true;
            await FinalizeUsageAsync(result, runResult, config, userId, sw);

            string rawText = runResult.FinalText ?? string.Empty;
            if (request.IncludeDebugText)
            {
                result.RawResponseText = rawText;
            }

            if (terminalError != null)
            {
                result.Status = "Failed";
                result.ErrorMessage = terminalError;
                AddLog(result, $"Terminal provider error: {terminalError}", "error", terminalError);
                return result;
            }

            string visibleText = BenchmarkAnswerSanitizer.StripThoughts(rawText);
            if (visibleText.Length != rawText.Trim().Length)
            {
                AddLog(result, "Removed the model's thinking block from the response.");
            }

            string description = BenchmarkDescriptionPrompt.UnwrapMarkdown(visibleText);
            if (description.Length == 0)
            {
                result.Status = "Failed";
                result.ErrorMessage = string.IsNullOrEmpty(runResult.ProviderFinishReason)
                    ? "The model returned no description text."
                    : $"The model returned no description text (finish reason: {runResult.ProviderFinishReason}).";
                AddLog(result, result.ErrorMessage, "error", rawText.Length == 0 ? null : Excerpt(rawText));
                return result;
            }

            result.Status = "Completed";
            result.Description = description;
            AddLog(result, $"Description generated ({description.Length} chars).");
        }
        catch (OperationCanceledException)
        {
            if (sw.IsRunning) sw.Stop();
            if (!usageFinalized && (runResult.TotalPromptTokens > 0 || runResult.OutputTokens > 0))
            {
                await TryFinalizeUsageAfterFailureAsync(result, runResult, config, userId, sw);
            }

            result.Status = "Cancelled";
            result.ErrorMessage = "Description generation was canceled.";
            AddLog(result, "Description generation was canceled.", "warning");
        }
        catch (Exception ex)
        {
            if (sw.IsRunning) sw.Stop();
            _logger.LogError(ex, "Suite description generation for suite {SuiteId} failed.", suiteId);
            if (!usageFinalized && (runResult.TotalPromptTokens > 0 || runResult.OutputTokens > 0))
            {
                await TryFinalizeUsageAfterFailureAsync(result, runResult, config, userId, sw);
            }

            result.Status = "Failed";
            result.ErrorMessage = ExceptionDetails.DescribeShort(ex);
            AddLog(result, $"Unexpected failure: {result.ErrorMessage}", "error", ExceptionDetails.Describe(ex));
        }
        finally
        {
            result.CompletedAtUtc = DateTime.UtcNow;
            result.DurationMs = sw.ElapsedMilliseconds;
        }

        return result;
    }

    /// <summary>Copies usage onto the result, records it in the system AI quota ledger, and costs it.</summary>
    private async Task FinalizeUsageAsync(
        SuiteDescriptionGenerationResultDto result,
        AgentRunResult runResult,
        SystemAiApiConfiguration config,
        string? userId,
        Stopwatch sw)
    {
        var (promptTokens, outputTokens, estimated) = NormalizeTokens(runResult);

        result.PromptTokens = promptTokens;
        result.OutputTokens = outputTokens;
        result.TokensEstimated = estimated;
        result.UncachedInputTokens = runResult.UncachedInputTokens;
        result.CacheReadTokens = runResult.CacheReadTokens;
        result.CacheCreationTokens = runResult.CacheCreationTokens;
        result.ReasoningTokens = SumReasoningTokens(runResult);
        result.TimeToFirstTokenMs = runResult.TimeToFirstTokenMs;
        result.ActualServiceTier = runResult.ActualServiceTier;
        result.ModelCalls = runResult.ModelCallCount;

        AddLog(result, $"Model call finished in {sw.ElapsedMilliseconds} ms: {promptTokens} input and {outputTokens} output tokens{(estimated ? " (estimated)" : string.Empty)}.");

        await _configService.RecordUsageAsync(
            config.Id,
            userId,
            promptTokens,
            outputTokens,
            roleContext: UsageRoleContext,
            cacheReadTokens: runResult.CacheReadTokens,
            cacheCreationTokens: runResult.CacheCreationTokens,
            totalDurationMs: (int)sw.ElapsedMilliseconds);

        var pricing = _pricingService.Resolve(config);
        if (pricing != null)
        {
            result.CostUsd = ComputeCost(pricing, runResult, config.ServiceTier);
            result.PricingSource = pricing.Source == ModelPricingSource.Custom ? "custom" : "catalog";
        }
        else
        {
            AddLog(result, "No price card resolves for this model; cost is not shown.", "warning");
        }
    }

    /// <summary>Records usage for a call that was cancelled or failed after the provider reported tokens.</summary>
    private async Task TryFinalizeUsageAfterFailureAsync(
        SuiteDescriptionGenerationResultDto result,
        AgentRunResult runResult,
        SystemAiApiConfiguration config,
        string? userId,
        Stopwatch sw)
    {
        try
        {
            await FinalizeUsageAsync(result, runResult, config, userId, sw);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Recording usage for suite description generation failed.");
            AddLog(result, $"Recording usage failed: {ExceptionDetails.DescribeShort(ex)}", "warning", ExceptionDetails.Describe(ex));
        }
    }

    /// <summary>Provider-reported totals, or the runner's estimates when the provider reported none.</summary>
    internal static (int PromptTokens, int OutputTokens, bool Estimated) NormalizeTokens(AgentRunResult runResult)
    {
        bool promptEstimated = runResult.TotalPromptTokens <= 0;
        bool outputEstimated = runResult.OutputTokens <= 0;
        int promptTokens = promptEstimated ? runResult.EstimatedInputTokens : runResult.TotalPromptTokens;
        int outputTokens = outputEstimated ? runResult.EstimatedOutputTokens : runResult.OutputTokens;
        return (promptTokens, outputTokens, promptEstimated || outputEstimated);
    }

    internal static int SumReasoningTokens(AgentRunResult runResult)
        => runResult.ModelCallUsages.Count > 0
            ? runResult.ModelCallUsages.Sum(u => u.ReasoningTokens)
            : runResult.ReasoningTokens;

    /// <summary>
    /// Per-call costing when the provider reported per-call usage; otherwise the flat overload on the
    /// aggregates, with cache writes excluded from the base input because they carry their own rate.
    /// </summary>
    internal static decimal ComputeCost(ModelPricing pricing, AgentRunResult runResult, string? requestedServiceTier)
    {
        if (runResult.ModelCallUsages.Count > 0)
        {
            return ModelPricingService.ComputeCost(
                pricing, runResult.ModelCallUsages,
                actualServiceTier: runResult.ActualServiceTier,
                requestedServiceTier: requestedServiceTier);
        }

        var (_, outputTokens, _) = NormalizeTokens(runResult);
        int inputTokens = runResult.TotalPromptTokens > 0 ? runResult.UncachedInputTokens : runResult.EstimatedInputTokens;
        return ModelPricingService.ComputeCost(
            pricing,
            Math.Max(0, inputTokens - runResult.CacheCreationTokens),
            outputTokens,
            runResult.CacheReadTokens,
            runResult.CacheCreationTokens);
    }

    private static string Excerpt(string text) => text.Length <= ExcerptLength ? text : text[..ExcerptLength];

    private static void AddLog(SuiteDescriptionGenerationResultDto result, string message, string severity = "info", string? rawExcerpt = null)
    {
        result.Log.Add(new SuiteDescriptionLogEntryDto
        {
            TimestampUtc = DateTime.UtcNow,
            Message = message,
            Severity = severity,
            RawExcerpt = rawExcerpt
        });
    }
}
