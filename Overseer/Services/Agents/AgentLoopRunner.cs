namespace Overseer.Services.Agents;

using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MobileGnollHackLogger.Data;
using Overseer.Services.Providers;
using Overseer.Services.Tools;

public class AgentLoopRunner
{
    private readonly Dictionary<string, IAiProvider> _aiProviders;
    private readonly ToolRegistry _toolRegistry;
    private readonly ToolExecutor _toolExecutor;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly KnowledgeBaseService _knowledgeBaseService;
    private readonly ModelMetadataService _modelMetadataService;
    private readonly ILogger<AgentLoopRunner> _logger;
    private readonly SubAgentCatalogService? _subAgentCatalogService;
    private readonly AiRequestGovernor? _governor;

    public AgentLoopRunner(
        IEnumerable<IAiProvider> aiProviders,
        ToolRegistry toolRegistry,
        ToolExecutor toolExecutor,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        IServiceScopeFactory scopeFactory,
        KnowledgeBaseService knowledgeBaseService,
        ModelMetadataService modelMetadataService,
        ILogger<AgentLoopRunner> logger,
        SubAgentCatalogService? subAgentCatalogService = null,
        AiRequestGovernor? governor = null)
    {
        _aiProviders = aiProviders.ToDictionary(p => p.ProviderName, p => p, StringComparer.OrdinalIgnoreCase);
        _toolRegistry = toolRegistry;
        _toolExecutor = toolExecutor;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _scopeFactory = scopeFactory;
        _knowledgeBaseService = knowledgeBaseService;
        _modelMetadataService = modelMetadataService;
        _logger = logger;
        _subAgentCatalogService = subAgentCatalogService;
        _governor = governor;
    }

    public async IAsyncEnumerable<ChatEvent> RunAsync(
        AgentRunRequest request,
        AgentRunBudget? budget,
        AgentRunResult result,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var aiProvider = request.AiProvider ?? _aiProviders.GetValueOrDefault(request.ProviderName)
            ?? throw new InvalidOperationException($"Unknown AI provider: {request.ProviderName}");

        string mainPrefix = string.IsNullOrEmpty(request.AgentName) ? "[Main Chat" : $"[SubAgent:{request.AgentName}";
        string providerPrefix = $"{mainPrefix} - {aiProvider.ProviderName}]";

        var messageHistory = new List<object>(request.SeedHistory);

        // AgentRunRequest.SystemPrompt used to be read nowhere: providers take the system
        // prompt from a { role = "system" } history entry or from SegmentedPrompt, and only
        // ChatService and DelegateToSubAgentTool inserted one. Every BenchmarkService path
        // therefore ran with no system prompt at all. Inject it here, but never when the
        // caller has already supplied one (ChatService sets both) or when a segmented prompt
        // is in play — the providers build `system` from SegmentedPrompt in that case.
        if (!string.IsNullOrWhiteSpace(request.SystemPrompt) && request.SegmentedPrompt == null &&
            !messageHistory.Any(m => string.Equals(
                ProviderHelper.GetProperty(m, "role")?.ToString(), "system", StringComparison.OrdinalIgnoreCase)))
        {
            messageHistory.Insert(0, new { role = "system", content = request.SystemPrompt });
        }

        // A caller may hand us a provider-neutral seed history ({ role, content }); ChatService
        // and DelegateToSubAgentTool pre-format theirs, BenchmarkService does not. Normalizing
        // here is what turns { role = "user", content = "..." } into Gemini's
        // { role: "user", parts: [{ text: "..." }] } — without it Google rejects the body with
        // `Unknown name "content" at 'contents[0]'`. All three implementations are idempotent:
        // Google passes a message that already has `parts` straight through, OpenAI's is a
        // no-op, and Anthropic's alternation pass leaves an already-alternating history
        // unchanged, so pre-formatted callers are not disturbed.
        messageHistory = aiProvider.PrepareMessageHistory(messageHistory);

        var thoughtWriter = new ThoughtMarkupWriter();
        var sbFullResponse = new StringBuilder();
        var streamToolCalls = result.ToolCalls;

        var execContext = request.ToolExecutionContext ?? new ToolExecutionContext();
        int maxToolIterations = request.MaxToolIterations;
        int maxParallelTools = request.MaxParallelTools;
        int maxParallelClientTools = request.MaxParallelClientTools;
        bool enableToolUse = request.EnableToolUse;
        bool enableWebSearch = request.EnableWebSearch;
        bool enableClientTools = request.EnableClientTools;
        bool enableGameActions = request.EnableGameActions;

        var runMetadata = _modelMetadataService.GetMetadata(request.ProviderName, request.ModelId);
        int? effectiveMaxOutputTokens = request.MaxOutputTokens;
        if (!effectiveMaxOutputTokens.HasValue && runMetadata.MaxOutputTokens > 0)
        {
            effectiveMaxOutputTokens = runMetadata.MaxOutputTokens;
        }
        else if (effectiveMaxOutputTokens.HasValue && runMetadata.MaxOutputTokens > 0)
        {
            effectiveMaxOutputTokens = Math.Min(effectiveMaxOutputTokens.Value, runMetadata.MaxOutputTokens);
        }

        var httpClient = _httpClientFactory.CreateClient("AiProvider");
        long? apiCallStartTime = null;
        int? timeToFirstTokenMs = null;
        int toolIterations = 0;
        bool hasToolsToRun = true;
        bool wasTruncatedByMaxTokens = false;
        bool hitBudgetLimit = false;
        bool hitIterationLimit = false;

        int maxTurnResultLength = _configuration.GetValue<int>("ToolExecutionLimits:MaxTurnResultLength", 120000);
        int cumulativeTurnResultLength = 0;
        string? priorSnapshotToolCallId = null;
        bool hasSupersededSnapshot = false;
        int supersessionMinChars = _configuration.GetValue<int>("PromptCacheSettings:SupersessionMinChars", 20000);

        while (toolIterations <= maxToolIterations && hasToolsToRun && !cancellationToken.IsCancellationRequested)
        {
            if (budget != null && !budget.TryIncrementModelCall())
            {
                hitBudgetLimit = true;
                if (request.ShowDebugLog)
                {
                    yield return new ChatEvent
                    {
                        Type = "debug",
                        Data = $"{mainPrefix}] Budget limit reached ({budget.TotalModelCalls}/{budget.MaxTotalModelCalls} model calls). Forcing final response."
                    };
                }
                enableToolUse = false;
                enableWebSearch = false;
                enableClientTools = false;
                enableGameActions = false;
            }
            else if (toolIterations == maxToolIterations && hasToolsToRun)
            {
                hitIterationLimit = true;
                yield return new ChatEvent { Type = "tool_error", Data = "Tool call limit reached. Forcing final response." };
                enableToolUse = false;
                enableWebSearch = false;
                enableClientTools = false;
                enableGameActions = false;
            }

            hasToolsToRun = false;
            string iterationText = "";
            bool lastEventWasToolCall = false;

            var requestTools = _toolRegistry.BuildToolsForRequest(
                aiProvider,
                execContext,
                enableWebSearch,
                enableToolUse,
                enableClientTools,
                enableGameActions,
                request.ModelId,
                request.AllowedToolNames);

            var currentIterationToolCalls = new List<JsonElement>();
            var currentIterationProviderItems = new List<JsonElement>();
            thoughtWriter.ResetIteration();

            var requestBody = aiProvider.BuildChatRequestBody(
                request.ModelId,
                messageHistory,
                effectiveMaxOutputTokens,
                request.ThinkingLevel,
                requestTools,
                request.ReasoningMode,
                request.ReasoningSummary,
                request.ServiceTier,
                parallelToolCalls: execContext.ParallelExecutionMode != ParallelExecutionMode.Disabled,
                segmentedPrompt: request.SegmentedPrompt,
                promptCacheKey: request.PromptCacheKey);

            var jsonRequest = JsonSerializer.Serialize(requestBody);
            if (request.ShowDebugLog)
            {
                yield return new ChatEvent { Type = "debug", Data = $"{providerPrefix} Request Body: {jsonRequest}" };
            }

            if (!apiCallStartTime.HasValue)
            {
                apiCallStartTime = System.Diagnostics.Stopwatch.GetTimestamp();
            }

            string credentialKey = request.CredentialKey ?? AiRequestGovernor.GetCredentialKey(aiProvider.ProviderName, null, request.SystemModelId);
            TimeSpan permitTimeout = request.PermitWaitTimeout ?? TimeSpan.FromSeconds(_configuration.GetValue<int>("AiRateLimitSettings:PermitWaitSeconds", 120));

            string? loggedTier = null;

            await foreach (var evt in ExecuteApiWithRetriesAsync(
                async ct =>
                {
                    var httpRequest = new HttpRequestMessage(HttpMethod.Post, aiProvider.GetChatStreamUrl(request.ModelId, request.ApiKey ?? ""));
                    aiProvider.ConfigureRequest(httpRequest, request.ApiKey ?? "");
                    httpRequest.Content = new StringContent(jsonRequest, Encoding.UTF8, "application/json");
                    return await httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct);
                },
                (response, ct) => aiProvider.ParseStreamAsync(response, request.ShowDebugLog, ct),
                aiProvider.ProviderName,
                request.SystemModelId,
                credentialKey,
                permitTimeout,
                mainPrefix,
                request.ShowDebugLog,
                cancellationToken,
                aiProvider,
                request.ServiceTier))
            {
                if (evt.Type == "service_tier")
                {
                    result.ActualServiceTier = evt.Data;
                    if (request.ShowDebugLog && !string.IsNullOrEmpty(evt.Data) && evt.Data != loggedTier)
                    {
                        loggedTier = evt.Data;
                        bool downgraded =
                            !string.IsNullOrEmpty(request.ServiceTier) &&
                            request.ServiceTier.Equals("priority", StringComparison.OrdinalIgnoreCase) &&
                            !evt.Data.Equals("priority", StringComparison.OrdinalIgnoreCase);
                        string requestedNote = string.IsNullOrEmpty(request.ServiceTier)
                            ? ""
                            : $", requested={request.ServiceTier}";
                        yield return new ChatEvent
                        {
                            Type = "debug",
                            Data = $"{providerPrefix} service tier: served={evt.Data}{requestedNote}"
                                 + (downgraded ? " — DOWNGRADED" : "")
                        };
                    }
                    continue;
                }
                else if (evt.Type == "provider_history_reset")
                {
                    currentIterationProviderItems.Clear();
                }
                else if (evt.Type == "provider_history_discard")
                {
                    currentIterationProviderItems.Clear();
                    if (request.ShowDebugLog)
                    {
                        yield return new ChatEvent { Type = "debug", Data = $"{providerPrefix} turn not replayable — using reconstruction" };
                    }
                }
                else if (evt.Type == "provider_history_item")
                {
                    try
                    {
                        currentIterationProviderItems.Add(JsonSerializer.Deserialize<JsonElement>(evt.Data));
                    }
                    catch { }
                }
                else if (evt.Type == "usage")
                {
                    try
                    {
                        var report = evt.UsageReport ?? (string.IsNullOrEmpty(evt.Data) ? null : JsonSerializer.Deserialize<TokenUsageReport>(evt.Data));
                        if (report != null)
                        {
                            result.TotalPromptTokens += report.TotalPromptTokens;
                            result.UncachedInputTokens += report.UncachedInputTokens;
                            result.CacheReadTokens += report.CacheReadTokens;
                            result.CacheCreationTokens += report.CacheCreationTokens;
                            result.OutputTokens += report.OutputTokens;
                            result.ReasoningTokens += report.ReasoningTokens;
                            // Last-report-wins: the final iteration's figures are the
                            // conversation's real context occupancy, unlike the accumulations
                            // above, which sum every tool iteration of the turn.
                            result.LastPromptTokens = report.TotalPromptTokens;
                            result.LastOutputTokens = report.OutputTokens;
                            budget?.AddActualTokens(report);
                        }
                    }
                    catch { }
                }
                else
                {
                    if (!timeToFirstTokenMs.HasValue && (evt.Type == "chunk" || evt.Type == "thinking_chunk" || evt.Type == "tool_call_complete" || evt.Type == "error"))
                    {
                        timeToFirstTokenMs = (int)System.Diagnostics.Stopwatch.GetElapsedTime(apiCallStartTime!.Value).TotalMilliseconds;
                        result.TimeToFirstTokenMs = timeToFirstTokenMs;
                        yield return new ChatEvent { Type = "ttft", Data = timeToFirstTokenMs.Value.ToString() };
                    }

                    if (evt.Type == "thinking_chunk")
                    {
                        thoughtWriter.HandleThinkingChunk(sbFullResponse, evt.Data);
                        iterationText += evt.Data;
                    }
                    else if (evt.Type == "chunk")
                    {
                        bool needsSpacer = false;
                        if (!string.IsNullOrEmpty(evt.Data))
                        {
                            if ((toolIterations > 0 && string.IsNullOrEmpty(iterationText)) || lastEventWasToolCall)
                            {
                                string cur = sbFullResponse.ToString();
                                if (!string.IsNullOrWhiteSpace(cur) && !cur.EndsWith("\n") && !cur.EndsWith(" "))
                                {
                                    needsSpacer = true;
                                    yield return new ChatEvent { Type = "chunk", Data = "\n\n" };
                                }
                                lastEventWasToolCall = false;
                            }
                        }

                        thoughtWriter.HandleChunk(sbFullResponse, evt.Data, needsSpacer);
                        iterationText += evt.Data;
                    }

                    if (evt.Type == "error")
                    {
                        thoughtWriter.CloseOpenThoughtDiv(sbFullResponse);
                        sbFullResponse.Append($"\n\n**Error:** {evt.Data}");
                    }

                    if (evt.Type == "debug" && !string.IsNullOrEmpty(evt.Data) &&
                        (evt.Data.Contains("Response incomplete: reason=max_output_tokens") ||
                         evt.Data.Contains("stop_reason=max_tokens") ||
                         evt.Data.Contains("finishReason=MAX_TOKENS")))
                    {
                        wasTruncatedByMaxTokens = true;
                    }

                    if (evt.Type == "tool_call_complete")
                    {
                        hasToolsToRun = true;
                        lastEventWasToolCall = true;
                        currentIterationToolCalls.Add(JsonSerializer.Deserialize<JsonElement>(evt.Data));
                        yield return new ChatEvent { Type = "tool_start", Data = EnrichToolStartData(evt.Data, request) };
                    }
                    else
                    {
                        yield return evt;
                    }
                }
            }

            if (hasToolsToRun && currentIterationToolCalls.Count > 0)
            {
                thoughtWriter.WrapPreToolVisibleText(sbFullResponse);

                aiProvider.AppendAssistantToolCallsToHistory(messageHistory, iterationText, currentIterationToolCalls, currentIterationProviderItems);

                var streamBaseIndex = streamToolCalls.Count;
                var batchItems = new List<ToolBatchItem>(currentIterationToolCalls.Count);

                foreach (var tc in currentIterationToolCalls)
                {
                    var tId = tc.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? Guid.NewGuid().ToString() : Guid.NewGuid().ToString();
                    var tName = tc.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "" : "";
                    var tArgsStr = tc.TryGetProperty("arguments", out var argsProp) ? argsProp.GetString() ?? "{}" : "{}";

                    streamToolCalls.Add(new ChatMessageToolCall
                    {
                        ToolCallId = tId,
                        Name = tName,
                        DisplayName = TryBuildSubAgentDisplayName(tName, tArgsStr),
                        ArgsText = tArgsStr,
                        Status = "running",
                        SortOrder = streamToolCalls.Count,
                        AgentName = request.AgentName,
                        ParentToolCallId = request.ParentToolCallId,
                        Depth = request.Depth,
                        BatchIndex = toolIterations
                    });

                    JsonElement tArgs = JsonDocument.Parse("{}").RootElement;
                    try { tArgs = JsonSerializer.Deserialize<JsonElement>(tArgsStr); } catch { }

                    batchItems.Add(new ToolBatchItem
                    {
                        ToolCallId = tId,
                        ToolName = tName,
                        Arguments = tArgs,
                        IsClientTool = _toolRegistry.GetExecutionLocation(tName) == ToolExecutionLocation.Client
                    });
                }

                var outcomeChannel = System.Threading.Channels.Channel.CreateUnbounded<ToolBatchOutcome>(
                    new System.Threading.Channels.UnboundedChannelOptions { SingleReader = true });

                var batchTask = ToolBatchRunner.RunAsync(
                    batchItems,
                    (item, ct) => _toolExecutor.ExecuteAsync(item.ToolName, item.Arguments, execContext, ct, item.ToolCallId),
                    maxParallelTools,
                    maxParallelClientTools,
                    outcomeChannel.Writer,
                    cancellationToken);

                try
                {
                    await foreach (var outcome in outcomeChannel.Reader.ReadAllAsync())
                    {
                        if (request.ShowDebugLog)
                        {
                            yield return new ChatEvent
                            {
                                Type = "debug",
                                Data = $"{mainPrefix}] Tool '{outcome.ToolName}' completed in {outcome.ExecutionMs}ms " +
                                       $"(queued {outcome.QueueWaitMs}ms). Success={outcome.Success}" +
                                       (outcome.Success ? "" : $", Error: {outcome.Content}")
                            };
                        }

                        if (outcome.Success)
                        {
                            var resObj = new { id = outcome.ToolCallId, name = outcome.ToolName, result = outcome.Content, status = outcome.TerminationStatus ?? "completed" };
                            yield return new ChatEvent { Type = "tool_result", Data = JsonSerializer.Serialize(resObj) };
                        }
                        else
                        {
                            var errObj = new { id = outcome.ToolCallId, name = outcome.ToolName, error = outcome.Content, status = outcome.TerminationStatus ?? "error" };
                            yield return new ChatEvent { Type = "tool_error", Data = JsonSerializer.Serialize(errObj) };
                        }
                    }
                }
                finally
                {
                    await batchTask;
                }

                var outcomes = await batchTask;

                int budgetChars = _configuration.GetValue<int>("ToolExecutionLimits:MaxBatchResultLength", 40000);
                var batchBudget = new ToolBatchResultBudget(Math.Max(budgetChars, execContext.MaxResultLength));

                var providerResults = new List<ProviderToolResult>(outcomes.Count);
                for (int i = 0; i < outcomes.Count; i++)
                {
                    var outcome = outcomes[i];

                    bool exemptFromBatchBudget =
                        _toolExecutor.GetEffectiveMaxResultLength(outcome.ToolName, execContext.MaxResultLength)
                            > execContext.MaxResultLength;

                    string finalContent = exemptFromBatchBudget ? outcome.Content : batchBudget.Apply(outcome.Content);

                    // Enforce cumulative turn-level output ceiling (Phase 4.2)
                    if (cumulativeTurnResultLength + finalContent.Length > maxTurnResultLength)
                    {
                        int remainingBudget = Math.Max(0, maxTurnResultLength - cumulativeTurnResultLength);
                        if (remainingBudget > 0 && finalContent.Length > remainingBudget)
                        {
                            finalContent = finalContent.Substring(0, remainingBudget) + "\n\n[Tool output truncated: cumulative turn limit reached]";
                        }
                        else if (remainingBudget == 0)
                        {
                            finalContent = "[Tool output omitted: cumulative turn limit reached]";
                        }
                    }
                    cumulativeTurnResultLength += finalContent.Length;

                    // Snapshot supersession (Phase 4.1)
                    if (outcome.ToolName == "refresh_snapshot" && outcome.Success)
                    {
                        if (priorSnapshotToolCallId != null && !hasSupersededSnapshot && finalContent.Length >= supersessionMinChars)
                        {
                            bool rewritten = aiProvider.TryRewriteToolResult(messageHistory, priorSnapshotToolCallId, "[Game state snapshot superseded by the updated snapshot below]");
                            if (rewritten)
                            {
                                hasSupersededSnapshot = true;
                                if (request.ShowDebugLog)
                                {
                                    yield return new ChatEvent { Type = "debug", Data = $"{providerPrefix} Superseded prior refresh_snapshot result ({priorSnapshotToolCallId}) with compact marker." };
                                }
                            }
                        }
                        priorSnapshotToolCallId = outcome.ToolCallId;
                    }

                    providerResults.Add(new ProviderToolResult
                    {
                        ToolCallId = outcome.ToolCallId,
                        ToolName = outcome.ToolName,
                        Content = finalContent,
                        Success = outcome.Success,
                        ProviderToolCallId = currentIterationToolCalls[i].TryGetProperty("provider_id", out var pid) ? pid.GetString() : null
                    });

                    var streamTc = streamToolCalls[streamBaseIndex + i];
                    streamTc.Result = outcome.Success ? outcome.Content : null;
                    streamTc.Error = outcome.Success ? null : outcome.Content;
                    streamTc.Status = outcome.TerminationStatus ?? (outcome.Success ? "completed" : "error");
                    streamTc.QueueWaitMs = (int)outcome.QueueWaitMs;
                    streamTc.ExecutionMs = (int)outcome.ExecutionMs;

                    if (outcome.NestedToolCalls != null && outcome.NestedToolCalls.Count > 0)
                    {
                        foreach (var nestedTc in outcome.NestedToolCalls)
                        {
                            if (string.IsNullOrEmpty(nestedTc.ParentToolCallId))
                            {
                                nestedTc.ParentToolCallId = outcome.ToolCallId;
                            }
                            if (nestedTc.Depth == 0)
                            {
                                nestedTc.Depth = streamTc.Depth + 1;
                            }
                            streamToolCalls.Add(nestedTc);
                        }
                    }
                }

                aiProvider.AppendToolResultsToHistory(messageHistory, providerResults);
            }

            toolIterations++;
            result.IterationsUsed = toolIterations;
        }

        thoughtWriter.CloseOpenThoughtDiv(sbFullResponse);
        result.EmittedDivCount = thoughtWriter.EmittedDivCount;

        result.TerminationReason =
            cancellationToken.IsCancellationRequested ? "canceled"
            : hitBudgetLimit                          ? "budget_exhausted"
            : hitIterationLimit                       ? "iteration_limit"
            : "completed";

        var fullResponse = ReasoningTextSanitizer.SanitizeStateless(sbFullResponse.ToString());
        if (wasTruncatedByMaxTokens && !fullResponse.Contains("[Response truncated: output token limit reached.]"))
        {
            fullResponse += "\n\n_[Response truncated: output token limit reached.]_";
        }
        result.FinalText = fullResponse;

        int? totalDurationMs = apiCallStartTime.HasValue
            ? (int)System.Diagnostics.Stopwatch.GetElapsedTime(apiCallStartTime.Value).TotalMilliseconds
            : null;
        result.TotalDurationMs = totalDurationMs;
        result.EstimatedOutputTokens = fullResponse.Length / 4;

        double hitRate = (result.TotalPromptTokens > 0)
            ? ((double)result.CacheReadTokens / result.TotalPromptTokens)
            : 0.0;
        _logger.LogInformation(
            "[Session {SessionId}] Turn complete: iterations={Iterations}, totalPrompt={TotalPrompt}, uncached={Uncached}, cacheRead={CacheRead} ({HitRate:P0}), cacheWrite={CacheWrite}, output={Output}, duration={Duration}ms",
            request.ToolExecutionContext?.SessionId,
            toolIterations,
            result.TotalPromptTokens,
            result.UncachedInputTokens,
            result.CacheReadTokens,
            hitRate,
            result.CacheCreationTokens,
            result.OutputTokens,
            totalDurationMs ?? 0);

        if (totalDurationMs.HasValue)
        {
            yield return new ChatEvent { Type = "duration", Data = totalDurationMs.Value.ToString() };
        }
    }

    private async IAsyncEnumerable<ChatEvent> ExecuteApiWithRetriesAsync(
        Func<CancellationToken, Task<HttpResponseMessage>> requestFactory,
        Func<HttpResponseMessage, CancellationToken, IAsyncEnumerable<ChatEvent>> streamParser,
        string providerName,
        long? systemModelId,
        string credentialKey,
        TimeSpan permitWaitTimeout,
        string mainPrefix,
        bool showDebugLog,
        [EnumeratorCancellation] CancellationToken cancellationToken,
        IAiProvider aiProvider,
        string? requestedServiceTier)
    {
        int[] retryDelays = { 1, 5, 10, 20, 30, 60 };
        int attempt = 0;
        bool success = false;

        while (!success && !cancellationToken.IsCancellationRequested)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            if (showDebugLog)
            {
                yield return new ChatEvent { Type = "debug", Data = $"{mainPrefix} - {providerName}] Starting POST request (Attempt {attempt + 1})..." };
            }
            yield return new ChatEvent { Type = "status", Data = $"Waiting for {providerName}..." };

            IDisposable? permit = null;
            string? throttleError = null;
            if (_governor != null && !string.IsNullOrEmpty(credentialKey))
            {
                try
                {
                    permit = await _governor.AcquirePermitAsync(credentialKey, permitWaitTimeout, cancellationToken);
                }
                catch (TimeoutException tex)
                {
                    throttleError = $"Request throttled: {tex.Message}";
                }
            }

            if (throttleError != null)
            {
                yield return new ChatEvent { Type = "error", Data = throttleError };
                yield break;
            }

            try
            {
                HttpResponseMessage? response = null;
                Exception? requestException = null;
                try
                {
                    response = await requestFactory(cancellationToken);
                }
                catch (Exception ex)
                {
                    requestException = ex;
                }

                if (requestException != null)
                {
                    sw.Stop();
                    bool isHttpClientTimeout = requestException is TaskCanceledException tce
                        && (tce.InnerException is TimeoutException || requestException.Message.Contains("HttpClient.Timeout"));

                    int elapsedSeconds = (int)(sw.ElapsedMilliseconds / 1000);
                    if (isHttpClientTimeout)
                    {
                        if (showDebugLog)
                        {
                            yield return new ChatEvent { Type = "debug", Data = $"{mainPrefix} - {providerName}] HTTP request timed out after {elapsedSeconds}s (HttpClient.Timeout). Exception: {requestException.Message}" };
                        }
                        yield return new ChatEvent { Type = "error", Data = $"The request to {providerName} timed out after {elapsedSeconds} seconds. The AI provider may be overloaded or unresponsive. Please try again." };
                    }
                    else if (cancellationToken.IsCancellationRequested)
                    {
                        if (showDebugLog)
                        {
                            yield return new ChatEvent { Type = "debug", Data = $"{mainPrefix} - {providerName}] Request canceled in {elapsedSeconds}s" };
                        }
                    }
                    else
                    {
                        if (showDebugLog)
                        {
                            yield return new ChatEvent { Type = "debug", Data = $"{mainPrefix} - {providerName}] Request failed in {elapsedSeconds}s: {requestException.GetType().Name}: {requestException.Message}" };
                        }
                        yield return new ChatEvent { Type = "error", Data = $"Request failed: {requestException.Message}" };
                    }
                    yield break;
                }

                sw.Stop();

                if (response != null && _governor != null && !string.IsNullOrEmpty(credentialKey))
                {
                    _governor.UpdateLimitsFromHeaders(credentialKey, response);
                }

                if (response!.IsSuccessStatusCode)
                {
                    if (showDebugLog)
                    {
                        yield return new ChatEvent { Type = "debug", Data = $"{mainPrefix} - {providerName}] HTTP {(int)response.StatusCode} Received ({sw.ElapsedMilliseconds}ms)" };
                    }
                    yield return new ChatEvent { Type = "status", Data = $"Streaming response..." };

                    bool hasYieldedChunks = false;
                    bool firstChunkReceived = false;
                    bool retryTriggered = false;
                    bool sawTierEvent = false;
                    IAsyncEnumerator<ChatEvent>? enumerator = null;
                    Exception? streamException = null;

                    try
                    {
                        enumerator = streamParser(response, cancellationToken).GetAsyncEnumerator(cancellationToken);
                        while (true)
                        {
                            bool hasNext = false;
                            try
                            {
                                hasNext = await enumerator.MoveNextAsync();
                            }
                            catch (Exception ex) when (ex is OperationCanceledException || ex is System.IO.IOException)
                            {
                                streamException = ex;
                                break;
                            }

                            if (!hasNext) break;
                            var evt = enumerator.Current;

                            if (evt.Type == "service_tier")
                            {
                                sawTierEvent = true;
                            }

                            if (!firstChunkReceived && (evt.Type == "chunk" || evt.Type == "thinking_chunk" || evt.Type == "tool_call_complete"))
                            {
                                firstChunkReceived = true;
                                if (showDebugLog)
                                {
                                    yield return new ChatEvent { Type = "debug", Data = $"{mainPrefix} - {providerName}] First token received after {sw.ElapsedMilliseconds}ms" };
                                }
                            }

                            if (evt.Type == "chunk" || evt.Type == "thinking_chunk" || evt.Type == "tool_call_complete")
                            {
                                hasYieldedChunks = true;
                            }

                            if (evt.Type == "error" && !hasYieldedChunks)
                            {
                                bool isRetryable = !string.IsNullOrEmpty(evt.Data) && (
                                    evt.Data.Contains("[overloaded_error]") ||
                                    evt.Data.Contains("[rate_limit_error]") ||
                                    evt.Data.Contains("[api_error]") ||
                                    evt.Data.Contains("529") ||
                                    evt.Data.Contains("503") ||
                                    evt.Data.Contains("502"));

                                if (isRetryable && attempt < retryDelays.Length)
                                {
                                    int delaySeconds = retryDelays[attempt];
                                    if (_governor != null && !string.IsNullOrEmpty(credentialKey))
                                    {
                                        _governor.RecordRateLimit(credentialKey, TimeSpan.FromSeconds(delaySeconds));
                                    }

                                    yield return new ChatEvent { Type = "status", Data = $"API Overloaded (attempt {attempt + 1}/{retryDelays.Length + 1}). Retrying in {delaySeconds}s..." };
                                    if (showDebugLog)
                                    {
                                        yield return new ChatEvent { Type = "debug", Data = $"{mainPrefix} - {providerName}] Sleeping for {delaySeconds}s before retry due to stream error: {evt.Data}" };
                                    }

                                    bool shouldBreak = false;
                                    try
                                    {
                                        await Task.Delay(delaySeconds * 1000, cancellationToken);
                                    }
                                    catch (TaskCanceledException)
                                    {
                                        shouldBreak = true;
                                    }

                                    if (shouldBreak) break;

                                    attempt++;
                                    retryTriggered = true;
                                    break;
                                }
                                else if (isRetryable)
                                {
                                    if (showDebugLog)
                                    {
                                        yield return new ChatEvent { Type = "debug", Data = $"{mainPrefix} - {providerName}] Max retries exhausted for stream error: {evt.Data}" };
                                    }
                                    yield return new ChatEvent { Type = "error", Data = $"The {providerName} API is currently overloaded. Max retries ({retryDelays.Length + 1}) exceeded. Please try again later." };
                                    break;
                                }
                            }

                            if (!retryTriggered)
                            {
                                yield return evt;
                            }
                        }
                    }
                    finally
                    {
                        if (enumerator != null) await enumerator.DisposeAsync();
                    }

                    if (showDebugLog && !string.IsNullOrEmpty(requestedServiceTier) &&
                        !requestedServiceTier.Equals("none", StringComparison.OrdinalIgnoreCase) &&
                        !requestedServiceTier.Equals("standard", StringComparison.OrdinalIgnoreCase) &&
                        !sawTierEvent)
                    {
                        var allowedHeaders = response.Headers
                            .Where(h => h.Key.StartsWith("x-", StringComparison.OrdinalIgnoreCase)
                                     || h.Key.StartsWith("openai-", StringComparison.OrdinalIgnoreCase)
                                     || h.Key.StartsWith("anthropic-", StringComparison.OrdinalIgnoreCase)
                                     || h.Key.Equals("retry-after", StringComparison.OrdinalIgnoreCase))
                            .Select(h => $"{h.Key}={string.Join(",", h.Value)}");
                        var headerSummary = string.Join("; ", allowedHeaders);
                        yield return new ChatEvent
                        {
                            Type = "debug",
                            Data = $"{mainPrefix} - {providerName}] Unresolved service tier (requested: {requestedServiceTier}, none reported in stream). Headers: [{(string.IsNullOrEmpty(headerSummary) ? "none" : headerSummary)}]"
                        };
                    }

                    if (streamException != null)
                    {
                        int elapsedSeconds = (int)(sw.ElapsedMilliseconds / 1000);
                        if (cancellationToken.IsCancellationRequested)
                        {
                            if (showDebugLog)
                            {
                                yield return new ChatEvent { Type = "debug", Data = $"{mainPrefix} - {providerName}] Stream interrupted after {elapsedSeconds}s — request timeout reached." };
                            }
                            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(streamException).Throw();
                        }
                        else
                        {
                            if (showDebugLog)
                            {
                                yield return new ChatEvent { Type = "debug", Data = $"{mainPrefix} - {providerName}] Stream reading failed after {elapsedSeconds}s: {streamException.GetType().Name}: {streamException.Message}" };
                            }
                            yield return new ChatEvent { Type = "error", Data = $"Stream interrupted: {streamException.Message}" };
                            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(streamException).Throw();
                        }
                    }

                    if (retryTriggered)
                    {
                        continue;
                    }

                    if (showDebugLog)
                    {
                        yield return new ChatEvent { Type = "debug", Data = $"{mainPrefix} - {providerName}] Stream completed." };
                    }
                    success = true;
                }
                else
                {
                    var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                    if (showDebugLog)
                    {
                        yield return new ChatEvent { Type = "debug", Data = $"{mainPrefix} - {providerName}] HTTP {(int)response.StatusCode} Received ({sw.ElapsedMilliseconds}ms)\nBody: {errorBody}" };
                    }

                    if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests) // 429
                    {
                        int maxRetries = _configuration.GetValue<int>("AiRateLimitSettings:Max429RetriesPerCall", 4);
                        int maxRetryAfterSec = _configuration.GetValue<int>("AiRateLimitSettings:MaxRetryAfterSeconds", 90);
                        double backoffSec = Math.Min(maxRetryAfterSec, Math.Pow(2, attempt + 1) + Random.Shared.NextDouble() * 1.5);
                        TimeSpan delay = TimeSpan.FromSeconds(backoffSec);

                        if (response.Headers.TryGetValues("Retry-After", out var rVals) && int.TryParse(rVals.FirstOrDefault(), out int raSec) && raSec > 0)
                        {
                            delay = TimeSpan.FromSeconds(Math.Min(maxRetryAfterSec, raSec));
                        }
                        else if (response.Headers.TryGetValues("retry-after-ms", out var rMsVals) && int.TryParse(rMsVals.FirstOrDefault(), out int raMs) && raMs > 0)
                        {
                            delay = TimeSpan.FromSeconds(Math.Min(maxRetryAfterSec, raMs / 1000.0));
                        }

                        if (_governor != null && !string.IsNullOrEmpty(credentialKey))
                        {
                            _governor.RecordRateLimit(credentialKey, delay);
                        }

                        if (attempt < maxRetries)
                        {
                            yield return new ChatEvent { Type = "status", Data = $"Rate limited (429). Retrying in {delay.TotalSeconds:F0}s (attempt {attempt + 1}/{maxRetries})..." };
                            if (showDebugLog)
                            {
                                yield return new ChatEvent { Type = "debug", Data = $"{mainPrefix} - {providerName}] 429 received. Cooldown {delay.TotalSeconds:F1}s before retry (attempt {attempt + 1}/{maxRetries})." };
                            }

                            try
                            {
                                await Task.Delay(delay, cancellationToken);
                            }
                            catch (TaskCanceledException)
                            {
                                yield break;
                            }

                            attempt++;
                            continue;
                        }
                        else
                        {
                            if (systemModelId.HasValue)
                            {
                                using var errScope = _scopeFactory.CreateScope();
                                var errService = errScope.ServiceProvider.GetRequiredService<SystemAiConfigService>();
                                await errService.RecordErrorAsync(systemModelId.Value, "429 Too Many Requests (retries exhausted)");
                            }
                            yield return new ChatEvent { Type = "error", Data = $"429 Rate Limited. Max retries ({maxRetries}) exceeded. Please try again later." };
                            yield break;
                        }
                    }
                    else if (response.StatusCode == System.Net.HttpStatusCode.PaymentRequired || errorBody.Contains("402") || errorBody.Contains("insufficient_quota"))
                    {
                        if (systemModelId.HasValue)
                        {
                            using var errScope = _scopeFactory.CreateScope();
                            var errService = errScope.ServiceProvider.GetRequiredService<SystemAiConfigService>();
                            await errService.RecordErrorAsync(systemModelId.Value, $"Budget Exhausted: {errorBody}");
                        }
                        yield return new ChatEvent { Type = "error", Data = "The system provider budget has been exhausted. Please contact the administrator." };
                        yield break;
                    }
                    else if (response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable || response.StatusCode == System.Net.HttpStatusCode.BadGateway || response.StatusCode == System.Net.HttpStatusCode.InternalServerError)
                    {
                        if (attempt < retryDelays.Length)
                        {
                            int delaySeconds = retryDelays[attempt];
                            yield return new ChatEvent { Type = "status", Data = $"503 Unavailable. Retrying in {delaySeconds}s..." };
                            if (showDebugLog)
                            {
                                yield return new ChatEvent { Type = "debug", Data = $"{mainPrefix} - {providerName}] Sleeping for {delaySeconds}s before retry..." };
                            }

                            try
                            {
                                await Task.Delay(delaySeconds * 1000, cancellationToken);
                            }
                            catch (TaskCanceledException)
                            {
                                yield break;
                            }

                            attempt++;
                        }
                        else
                        {
                            yield return new ChatEvent { Type = "error", Data = "503 Unavailable. Max retries exceeded." };
                            yield break;
                        }
                    }
                    else
                    {
                        yield return new ChatEvent { Type = "error", Data = $"API Error: {(int)response.StatusCode} - {errorBody}" };
                        yield break;
                    }
                }
            }
            finally
            {
                permit?.Dispose();
            }
        }
    }

    private string? TryBuildSubAgentDisplayName(string? toolName, string? argsJson)
    {
        if (!string.Equals(toolName, "delegate_to_subagent", StringComparison.Ordinal)) return null;
        try
        {
            using var doc = JsonDocument.Parse(argsJson ?? "{}");
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            string? agent = doc.RootElement.TryGetProperty("agent_name", out var agentProp) && agentProp.ValueKind == JsonValueKind.String
                ? agentProp.GetString()
                : null;
            string? instance = null;
            if (doc.RootElement.TryGetProperty("subagent_name", out var subProp) && subProp.ValueKind == JsonValueKind.String)
            {
                instance = subProp.GetString();
            }
            else if (doc.RootElement.TryGetProperty("subagentName", out var subCamelProp) && subCamelProp.ValueKind == JsonValueKind.String)
            {
                instance = subCamelProp.GetString();
            }
            return SubAgentUiHelper.BuildDisplayName(agent, instance, _subAgentCatalogService);
        }
        catch { return null; }
    }

    private string EnrichToolStartData(string eventData, AgentRunRequest request)
    {
        try
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(eventData);
            if (node is not System.Text.Json.Nodes.JsonObject rootObj)
            {
                return eventData;
            }

            string? toolName = rootObj.TryGetPropertyValue("name", out var nameNode)
                ? nameNode?.GetValue<string>()
                : null;

            // Knowledge article title enrichment (pre-existing behavior, unchanged).
            if (toolName == "get_knowledge_article" &&
                rootObj.TryGetPropertyValue("arguments", out var argsNode))
            {
                var argsStr = argsNode?.GetValue<string>();
                if (!string.IsNullOrEmpty(argsStr))
                {
                    var argsObjNode = System.Text.Json.Nodes.JsonNode.Parse(argsStr);
                    if (argsObjNode is System.Text.Json.Nodes.JsonObject argsObj &&
                        argsObj.TryGetPropertyValue("topic", out var topicNode))
                    {
                        var topic = topicNode?.GetValue<string>();
                        if (topic != null)
                        {
                            var title = _knowledgeBaseService.GetArticleTitle(topic) ?? topic;
                            argsObj["topic_title"] = title;
                            rootObj["arguments"] = argsObj.ToJsonString();
                        }
                    }
                }
            }

            // Subagent display name enrichment.
            if (toolName == "delegate_to_subagent" &&
                rootObj.TryGetPropertyValue("arguments", out var subArgsNode))
            {
                var subArgsStr = subArgsNode?.GetValue<string>();
                var displayName = TryBuildSubAgentDisplayName(toolName, subArgsStr);
                if (!string.IsNullOrEmpty(displayName))
                {
                    rootObj["display_name"] = displayName;
                }
            }

            // Delegation hierarchy metadata. Only present for subagent runs; for the
            // coordinator AgentName is null, ParentToolCallId is null, and Depth is 0,
            // so the payload is byte-identical to today's.
            //
            // agent_name is deliberately NOT stamped onto a nested delegate_to_subagent
            // tool_start: request.AgentName there is the *delegating* agent, whereas the
            // frontend's fallback (chat.component.ts:1276) correctly reads the *delegated-to*
            // agent from the tool arguments. Stamping it would shadow the better value.
            if (!string.IsNullOrEmpty(request.AgentName) && toolName != "delegate_to_subagent")
            {
                rootObj["agent_name"] = request.AgentName;
            }
            if (!string.IsNullOrEmpty(request.ParentToolCallId))
            {
                rootObj["parent_tool_call_id"] = request.ParentToolCallId;
            }
            if (request.Depth > 0)
            {
                rootObj["depth"] = request.Depth;
            }

            return rootObj.ToJsonString();
        }
        catch { }
        return eventData;
    }
}
