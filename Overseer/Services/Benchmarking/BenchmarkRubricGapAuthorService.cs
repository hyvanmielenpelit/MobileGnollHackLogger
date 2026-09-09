namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MobileGnollHackLogger.Data;
using Overseer.Services;
using Overseer.Services.Agents;

/// <summary>The outcome of parsing one drafting response.</summary>
public sealed class BenchmarkRubricGapAuthorDraftParseResult
{
    public bool Success { get; set; }

    /// <summary>False when the model judged the rubric already covers the claim. Not an error.</summary>
    public bool ProposeAddition { get; set; } = true;

    public string? ProposedText { get; set; }
    public string? Citation { get; set; }
    public string? Justification { get; set; }
    public string? ConfidenceNote { get; set; }
    public List<string> ValidationErrors { get; set; } = new();
}

/// <summary>
/// Drafts a proposed rubric addition per eligible gap cluster, using read-only tools to confirm the
/// citation.
///
/// <para><b>This service writes nothing.</b> It produces drafts in memory. The only path from a
/// draft into a rubric is an explicit human acceptance of one draft, through the controller, and
/// that acceptance applies the operator's submitted text rather than the model's. The boundary is
/// the point of the feature: § 7 rung 1 of the benchmark-to-chat-transfer method requires human
/// authorship of curated knowledge, and <see cref="BenchmarkRubricGapDetector"/> documents that a
/// gap is surfaced for a human and never applied automatically.</para>
/// </summary>
public class BenchmarkRubricGapAuthorService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly BenchmarkRubricGapAuthorJobManager _jobManager;
    private readonly AgentLoopRunner _agentLoopRunner;
    private readonly CryptoService _cryptoService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<BenchmarkRubricGapAuthorService> _logger;

    /// <summary>
    /// Read-only lookups only. The author confirms a citation; it has no reason to reach anything
    /// that can change state, and the allowlist is what makes "writes nothing" a property of the
    /// configuration rather than of the prompt's good behaviour.
    /// </summary>
    private static readonly List<string> DefaultAllowedTools = new()
    {
        "wiki_search", "wiki_view", "get_knowledge_article",
        "nethack_wiki_search", "nethack_wiki_view",
        "monster_lookup", "item_lookup", "get_monster_stats",
        "get_item_stats", "get_artifact_stats", "get_constants",
        "get_function_definition", "search_definitions",
        "source_code_search", "source_code_view", "list_indexed_files"
    };

    public BenchmarkRubricGapAuthorService(
        IServiceScopeFactory scopeFactory,
        BenchmarkRubricGapAuthorJobManager jobManager,
        AgentLoopRunner agentLoopRunner,
        CryptoService cryptoService,
        IConfiguration configuration,
        ILogger<BenchmarkRubricGapAuthorService> logger)
    {
        _scopeFactory = scopeFactory;
        _jobManager = jobManager;
        _agentLoopRunner = agentLoopRunner;
        _cryptoService = cryptoService;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// A cluster may be drafted against only when a claim verifier checked one of its occurrences
    /// against the source or the wiki and returned Supported <b>with a citation</b>.
    ///
    /// Cross-model agreement alone (<see cref="BenchmarkRubricGapVerdict.LikelyRubricGap"/>) is a
    /// proxy for truth and is deliberately not enough here: a draft asserts a fact into the answer
    /// key, so it needs evidence of truth rather than a proxy for it. A
    /// <see cref="BenchmarkRubricGapVerdict.LikelyHallucination"/> cluster — which is also every
    /// single-family cluster the verifier never supported — is never eligible, because absorbing
    /// one model's invention into the answer key is the exact failure this whole subsystem exists
    /// to prevent.
    /// </summary>
    public static bool IsClusterEligible(BenchmarkRubricGapVerdict verdict, string? citation)
        => verdict == BenchmarkRubricGapVerdict.VerifiedRubricGap && !string.IsNullOrWhiteSpace(citation);

    /// <summary>
    /// Parses and validates one drafting response. A proposal with no citation is rejected, as is
    /// one with no text; both are rule 2 and rule 1 of the prompt, enforced here rather than
    /// trusted there.
    /// </summary>
    public static BenchmarkRubricGapAuthorDraftParseResult ParseDraft(string? rawText)
    {
        var result = new BenchmarkRubricGapAuthorDraftParseResult();

        if (string.IsNullOrWhiteSpace(rawText))
        {
            result.ValidationErrors.Add("Empty response received from the rubric gap author.");
            return result;
        }

        string cleanJson = BenchmarkJsonExtractor.Extract(rawText);

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(cleanJson);
        }
        catch (JsonException ex)
        {
            result.ValidationErrors.Add($"JSON parse error: {ex.Message}");
            return result;
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                result.ValidationErrors.Add("Root element must be a JSON object.");
                return result;
            }

            result.ProposeAddition = !root.TryGetProperty("proposeAddition", out var pa)
                                     || pa.ValueKind != JsonValueKind.False;

            result.ProposedText = ReadString(root, "proposedText");
            result.Citation = ReadString(root, "citation");
            result.Justification = ReadString(root, "justification");
            result.ConfidenceNote = ReadString(root, "confidenceNote");

            if (!result.ProposeAddition)
            {
                // "The rubric already covers this" is a valid, useful answer, not a failure.
                result.Success = true;
                return result;
            }

            if (string.IsNullOrWhiteSpace(result.ProposedText))
            {
                result.ValidationErrors.Add("proposedText is required when proposeAddition is true.");
            }

            if (string.IsNullOrWhiteSpace(result.Citation))
            {
                result.ValidationErrors.Add("citation is required for every proposed addition; a proposal without one is rejected.");
            }

            result.Success = result.ValidationErrors.Count == 0;
            return result;
        }
    }

    private static string? ReadString(JsonElement root, string name)
        => root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()?.Trim()
            : null;

    /// <summary>
    /// Reads every unverified claim recorded for a suite and returns the eligible clusters with the
    /// verifier evidence attached. Public so the controller can build a job's draft list without
    /// duplicating the projection, and so it is testable on its own.
    /// </summary>
    public static IReadOnlyList<(BenchmarkRubricGapCluster Cluster, BenchmarkRubricGapAuthorClusterEvidence Evidence)>
        BuildEligibleClusters(IReadOnlyList<BenchmarkUnverifiedClaimSample> samples)
    {
        var clusters = BenchmarkRubricGapDetector.Detect(samples);
        var result = new List<(BenchmarkRubricGapCluster, BenchmarkRubricGapAuthorClusterEvidence)>();

        var perQuestionIndex = new Dictionary<long, int>();

        foreach (var cluster in clusters)
        {
            int index = perQuestionIndex.TryGetValue(cluster.QuestionId, out int n) ? n : 0;
            perQuestionIndex[cluster.QuestionId] = index + 1;

            var claimSet = new HashSet<string>(cluster.Claims.Select(c => c.Trim()), StringComparer.Ordinal);
            var members = samples
                .Where(s => s.QuestionId == cluster.QuestionId && claimSet.Contains(s.Claim.Trim()))
                .ToList();

            var supported = members
                .Where(m => m.VerificationVerdict == BenchmarkClaimVerdict.Supported
                            && !string.IsNullOrWhiteSpace(m.Citation))
                .ToList();

            string? citation = supported.Select(m => m.Citation).FirstOrDefault();
            string? basis = supported.Select(m => m.Basis).FirstOrDefault(b => !string.IsNullOrWhiteSpace(b));

            if (!IsClusterEligible(cluster.Verdict, citation))
            {
                continue;
            }

            result.Add((cluster, new BenchmarkRubricGapAuthorClusterEvidence
            {
                ClusterKey = $"{cluster.QuestionId}:{index}",
                Claims = cluster.Claims,
                Citation = citation,
                Basis = basis,
                Recurrence = members.Select(m => m.RunId).Distinct().Count(),
                RunIds = members.Select(m => m.RunId).Distinct().OrderBy(id => id).ToList(),
                ModelFamilies = cluster.ModelFamilies,
                Verdict = cluster.Verdict
            }));
        }

        return result;
    }

    public async Task RunRubricGapAuthorAsync(
        string jobId,
        IReadOnlyDictionary<string, BenchmarkRubricGapAuthorClusterEvidence> evidenceByClusterKey,
        CancellationToken ct)
    {
        var job = _jobManager.TryGet(jobId);
        if (job == null) return;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var configService = scope.ServiceProvider.GetRequiredService<SystemAiConfigService>();

            var suite = await db.BenchmarkSuites
                .Include(s => s.Questions)
                .FirstOrDefaultAsync(s => s.Id == job.SuiteId, ct);

            if (suite == null)
            {
                job.AddLog("Suite not found.", "error");
                job.SetStatus(BenchmarkRubricGapAuthorJobStatus.Failed);
                return;
            }

            var config = await db.SystemAiApiConfigurations
                .FirstOrDefaultAsync(c => c.Id == job.AuthorConfigId, ct);
            if (config == null || !config.IsEnabled)
            {
                job.AddLog("Author model configuration not found or disabled.", "error");
                job.SetStatus(BenchmarkRubricGapAuthorJobStatus.Failed);
                return;
            }

            string apiKey = _cryptoService.Decrypt(config.EncryptedApiKey!, config.ApiKeyNonce!, config.ApiKeyTag!, "SYSTEM_API_KEY");

            var allowedTools = _configuration.GetSection("Benchmark:AllowedTools").Get<List<string>>() ?? DefaultAllowedTools;
            int maxResultLength = _configuration.GetValue<int>("Benchmark:MaxResultLength", 10000);
            int maxOutputTokens = Math.Max(config.MaxOutputTokens ?? 4096, 4096);

            bool hasErrors = false;

            foreach (var draft in job.Drafts)
            {
                ct.ThrowIfCancellationRequested();

                var question = suite.Questions.FirstOrDefault(q => q.Id == draft.QuestionId);
                if (question == null)
                {
                    job.SetDraftFailed(draft.ClusterKey, "Question not found in suite.");
                    hasErrors = true;
                    continue;
                }

                if (!evidenceByClusterKey.TryGetValue(draft.ClusterKey, out var evidence))
                {
                    job.SetDraftFailed(draft.ClusterKey, "Cluster evidence missing.");
                    hasErrors = true;
                    continue;
                }

                if (!IsClusterEligible(evidence.Verdict, evidence.Citation))
                {
                    // Belt and braces: the controller filters, and so does this. A draft against a
                    // claim no verifier supported must never reach a model, let alone an operator.
                    draft.Status = BenchmarkRubricGapAuthorDraftStatus.Skipped;
                    draft.ErrorMessage = "Cluster is not verifier-supported; drafting is not permitted for it.";
                    job.AddLog($"Q{draft.QuestionOrderIndex}: cluster skipped — not verifier-supported.", "warning");
                    continue;
                }

                draft.Status = BenchmarkRubricGapAuthorDraftStatus.Drafting;
                job.AddLog($"Drafting a rubric addition for question {draft.QuestionOrderIndex} (cluster {draft.ClusterKey}) using {config.DisplayName}...");

                string prompt = BenchmarkRubricGapAuthorPrompt.BuildPrompt(question, evidence, job.Instructions);

                var (runResult, terminalError) = await ExecuteModelCallAsync(
                    config, apiKey, prompt, maxOutputTokens, allowedTools, maxResultLength, job, draft.ClusterKey, ct);

                await RecordUsageAsync(configService, config, job, runResult);

                if (terminalError != null)
                {
                    job.AddLog($"Terminal provider error drafting for question {draft.QuestionOrderIndex}: {terminalError}", "error");
                    job.SetDraftFailed(draft.ClusterKey, terminalError);
                    hasErrors = true;
                    continue;
                }

                var parsed = ParseDraft(runResult.FinalText);

                if (!parsed.Success)
                {
                    job.AddLog($"Initial draft failed validation for question {draft.QuestionOrderIndex}. Attempting repair...", "warning");

                    string repairPrompt = BenchmarkRubricGapAuthorPrompt.BuildRepairPrompt(
                        runResult.FinalText ?? string.Empty,
                        string.Join("; ", parsed.ValidationErrors));

                    var (repairResult, repairError) = await ExecuteModelCallAsync(
                        config, apiKey, repairPrompt, maxOutputTokens, allowedTools, maxResultLength, job, draft.ClusterKey, ct);

                    await RecordUsageAsync(configService, config, job, repairResult);

                    if (repairError != null)
                    {
                        job.AddLog($"Repair call encountered terminal provider error: {repairError}", "error");
                        job.SetDraftFailed(draft.ClusterKey, repairError);
                        hasErrors = true;
                        continue;
                    }

                    parsed = ParseDraft(repairResult.FinalText);
                }

                if (!parsed.Success)
                {
                    string err = string.Join("; ", parsed.ValidationErrors);
                    job.AddLog($"Draft for question {draft.QuestionOrderIndex} rejected: {err}", "error");
                    job.SetDraftFailed(draft.ClusterKey, err);
                    hasErrors = true;
                    continue;
                }

                if (!parsed.ProposeAddition)
                {
                    draft.Status = BenchmarkRubricGapAuthorDraftStatus.Skipped;
                    draft.Justification = parsed.Justification;
                    draft.ConfidenceNote = parsed.ConfidenceNote;
                    job.AddLog($"Q{draft.QuestionOrderIndex}: no addition proposed — {parsed.Justification ?? "the rubric already covers the claim."}");
                    continue;
                }

                job.SetDraftResult(draft.ClusterKey, parsed.ProposedText!, parsed.Citation!, parsed.Justification, parsed.ConfidenceNote);
                job.AddLog($"Q{draft.QuestionOrderIndex}: draft ready ({parsed.ProposedText!.Length} chars, cited).");
            }

            job.SetStatus(hasErrors
                ? BenchmarkRubricGapAuthorJobStatus.CompletedWithErrors
                : BenchmarkRubricGapAuthorJobStatus.Completed);
        }
        catch (OperationCanceledException)
        {
            job.AddLog("Rubric gap authoring was cancelled.", "warning");
            job.SetStatus(BenchmarkRubricGapAuthorJobStatus.Cancelled);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Rubric gap author job {JobId} failed.", jobId);
            job.AddLog($"Unexpected failure: {ex.Message}", "error");
            job.SetStatus(BenchmarkRubricGapAuthorJobStatus.Failed);
        }
    }

    private static async Task RecordUsageAsync(
        SystemAiConfigService configService,
        SystemAiApiConfiguration config,
        BenchmarkRubricGapAuthorJob job,
        AgentRunResult runResult)
    {
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
    }

    private async Task<(AgentRunResult Result, string? TerminalError)> ExecuteModelCallAsync(
        SystemAiApiConfiguration config,
        string apiKey,
        string prompt,
        int maxOutputTokens,
        List<string> allowedTools,
        int maxResultLength,
        BenchmarkRubricGapAuthorJob job,
        string clusterKey,
        CancellationToken ct)
    {
        var runRequest = new AgentRunRequest
        {
            ProviderName = config.Provider,
            ModelId = config.ModelId,
            ApiKey = apiKey,
            ModelDisplayName = config.DisplayName,
            SystemPrompt = BenchmarkRubricGapAuthorPrompt.SystemPrompt,
            ThinkingLevel = config.ThinkingLevel,
            ReasoningMode = config.ReasoningMode,
            ReasoningSummary = config.ReasoningSummary,
            ServiceTier = config.ServiceTier,
            MaxOutputTokens = maxOutputTokens,
            MaxToolIterations = 8,
            EnableToolUse = true,
            EnableWebSearch = false,
            EnableSubAgents = false,
            AllowedTools = allowedTools,
            SystemModelId = config.Id,
            Budget = new AgentRunBudget { MaxTotalModelCalls = 10 },
            ToolExecutionContext = new Tools.ToolExecutionContext
            {
                SessionId = Overseer.Services.Privacy.SessionRef.Persistent(job.SuiteId),
                ToolBudgetScopeId = $"rubricgapauthor_{job.Id}_{clusterKey.Replace(':', '_')}",
                UserId = job.StartedByUserId ?? string.Empty,
                MaxResultLength = maxResultLength,
                MaxCallsPerSession = 20,
                ShowDebugLog = false
            },
            SeedHistory = new List<object>
            {
                new { role = "user", content = prompt }
            }
        };

        var runResult = new AgentRunResult();
        string? terminalError = null;

        await foreach (var evt in _agentLoopRunner.RunAsync(runRequest, runRequest.Budget, runResult, ct))
        {
            if (evt.Type == "error")
            {
                terminalError = evt.Data?.ToString();
            }
        }

        return (runResult, terminalError);
    }
}
