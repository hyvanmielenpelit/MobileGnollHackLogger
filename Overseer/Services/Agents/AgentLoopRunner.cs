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
    /// <summary>How much of a non-2xx response body is logged and carried on the error event.</summary>
    private const int MaxLoggedErrorBodyLength = 1000;

    /// <summary>
    /// User text appended to the turn's own history before the forced final model call, once per
    /// turn. Never yielded as a chat event and never stored.
    /// </summary>
    public const string ForcedFinalInstruction =
        "No more tool calls are available in this turn. Answer now from the tool results above, in the format the request asked for.";

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

    /// <summary>
    /// Whether <paramref name="toolName"/> was declared to the model in <paramref name="requestTools"/>.
    /// Untracked declarations (<see cref="ToolsForRequest.DeclaredToolNames"/> null) admit every name.
    /// </summary>
    internal static bool IsDeclared(ToolsForRequest requestTools, string toolName)
        => requestTools.DeclaredToolNames is not { } declared || declared.Contains(toolName);

    /// <summary>The failed result an undeclared tool call gets; no handler runs and no budget is charged.</summary>
    internal static ToolResult UndeclaredToolResult(string toolName)
        => new() { Success = false, ErrorMessage = $"Tool '{toolName}' is not available in this conversation." };

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
        execContext.ActiveProvider ??= request.ProviderName;
        execContext.ActiveModelId ??= request.ModelId;
        int maxToolIterations = request.MaxToolIterations;
        int maxParallelTools = request.MaxParallelTools;
        int maxParallelClientTools = request.MaxParallelClientTools;
        bool enableToolUse = request.EnableToolUse;
        /* Forced off in a confidential session with egress blocked, using the same flag-flip
           the budget and iteration limits already use below. The registry would withhold the
           provider web-search tool anyway; flipping the flag here means the rest of the loop
           reasons about a session that simply has no web search, rather than one whose request
           says it does. */
        bool enableWebSearch = request.EnableWebSearch && request.ToolExecutionContext?.BlockExternalEgress != true;
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
        bool forcedFinalInstructionAppended = false;

        int maxTurnResultLength = _configuration.GetValue<int>("ToolExecutionLimits:MaxTurnResultLength", 120000);
        int cumulativeTurnResultLength = 0;
        string? priorSnapshotToolCallId = null;
        bool hasSupersededSnapshot = false;
        int supersessionMinChars = _configuration.GetValue<int>("PromptCacheSettings:SupersessionMinChars", 20000);

        while (toolIterations <= maxToolIterations && hasToolsToRun && !cancellationToken.IsCancellationRequested)
        {
            bool forcingFinalResponse = false;
            if (budget != null && !budget.TryIncrementModelCall())
            {
                forcingFinalResponse = true;
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
                forcingFinalResponse = true;
                hitIterationLimit = true;
                yield return new ChatEvent { Type = "tool_error", Data = "Tool call limit reached. Forcing final response." };
                enableToolUse = false;
                enableWebSearch = false;
                enableClientTools = false;
                enableGameActions = false;
            }

            // Only after a tool round of this turn, whose results then end the history. A call
            // that never ran tools (MaxToolIterations 0, or a budget spent before the first call)
            // is sent unchanged.
            if (forcingFinalResponse && toolIterations > 0 && !forcedFinalInstructionAppended)
            {
                aiProvider.AppendUserTextToHistory(messageHistory, ForcedFinalInstruction);
                forcedFinalInstructionAppended = true;
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
                request.AllowedToolNames,
                request.EnableFeedbackTool);

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
                promptCacheKey: request.PromptCacheKey,
                cacheConversationTail: request.CacheConversationTail,
                disablePromptCache: request.DisableProviderPromptCache,
                // A custom endpoint may reject a field it does not know.
                safetyIdentifier: request.Endpoint.IsCustom ? null : request.SafetyIdentifier);

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
            string? chatStreamUrl = TryGetChatStreamUrl(aiProvider, request);

            // Added when the call starts, so a call abandoned by an exception is still on the list.
            var callRecord = new ModelCallRecord
            {
                CallIndex = result.ModelCalls.Count,
                StartedAtUtc = DateTime.UtcNow,
                Provider = aiProvider.ProviderName,
                RequestedModelId = request.ModelId,
                ThinkingLevelSent = request.ThinkingLevel,
                ReasoningSummarySent = request.ReasoningSummary,
                ServiceTierRequested = request.ServiceTier,
                MaxOutputTokensSent = effectiveMaxOutputTokens,
                EndpointKind = request.Endpoint.IsCustom ? "custom" : "official"
            };
            result.ModelCalls.Add(callRecord);
            var call = new ModelCallTracker(callRecord)
            {
                Result = result,
                OperatorCancellation = request.OperatorCancellation,
                EndpointKind = request.Endpoint.IsCustom ? BenchmarkEndpointKind.Custom : BenchmarkEndpointKind.Official
            };

            await foreach (var evt in ExecuteApiWithRetriesAsync(
                async ct =>
                {
                    var httpRequest = new HttpRequestMessage(
                        HttpMethod.Post, aiProvider.GetChatStreamUrl(request.ModelId, request.ApiKey ?? "", request.Endpoint));
                    aiProvider.ConfigureRequest(httpRequest, request.ApiKey ?? "", request.Endpoint);
                    httpRequest.Content = new StringContent(jsonRequest, Encoding.UTF8, "application/json");
                    call.AttemptSendTicks = ProviderCallMeta.Now();
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
                request.ServiceTier,
                request.ApiKeyAlert,
                request.ApiKey,
                request.ModelId,
                chatStreamUrl,
                call))
            {
                if (evt.Type == "service_tier")
                {
                    result.ActualServiceTier = evt.Data;
                    callRecord.ServedServiceTier = evt.Data;
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
                else if (evt.Type == "finish_reason")
                {
                    // The typed reason where a provider supplies one; the debug-text match below survives only as the
                    // fallback for a provider that does not yet emit it. Matching log prose meant a reworded debug
                    // line silently disabled truncation detection. Not yielded onward: the client has never seen it.
                    if (!string.IsNullOrWhiteSpace(evt.Data))
                    {
                        result.ProviderFinishReason = evt.Data;
                        callRecord.FinishReason = evt.Data;
                        if (evt.Data.Equals("max_tokens", StringComparison.OrdinalIgnoreCase)
                            || evt.Data.Equals("max_output_tokens", StringComparison.OrdinalIgnoreCase))
                        {
                            wasTruncatedByMaxTokens = true;
                        }
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
                            result.ModelCallUsages.Add(report);
                            callRecord.Usage = report;
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
                else if (evt.Type == "call_meta")
                {
                    // Consumed here and never yielded: the caller, and through ChatService the client, never sees it.
                    if (evt.CallMeta != null)
                    {
                        callRecord.ApplyMeta(evt.CallMeta, call.SendTicks);
                    }
                    continue;
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

            if (!hasToolsToRun)
            {
                // The model attempted a tool call but emitted it as assistant *text* instead of
                // as a structured call, so the provider never parsed it and this iteration looks
                // tool-free. The narration leading up to that attempt is analysis-channel
                // content and must not be graded or displayed as answer prose.
                //
                // The split matters: narration, the leaked call, and the real answer all arrive
                // in this one iteration, so only the run up to the last leaked artifact is
                // wrapped. Wrapping the whole visible run would move the answer into the thought
                // block and leave the answer empty.
                var visibleText = thoughtWriter.GetIterationVisibleText(sbFullResponse);
                if (!string.IsNullOrEmpty(visibleText) &&
                    ReasoningTextSanitizer.TryFindLastLeakedArtifactEnd(visibleText, out int leakEnd))
                {
                    thoughtWriter.WrapLeadingVisibleText(sbFullResponse, leakEnd);
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

                // Wall-clock of the whole batch, not the sum of each tool's own timings: tools in
                // one batch run concurrently, so summing them would charge the model for time it
                // never spent waiting. This is what lets speed be scored on model-attributable
                // time rather than on harness I/O.
                var batchStartTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();

                var batchTask = ToolBatchRunner.RunAsync(
                    batchItems,
                    (item, ct) => IsDeclared(requestTools, item.ToolName)
                        ? _toolExecutor.ExecuteAsync(item.ToolName, item.Arguments, execContext, ct, item.ToolCallId)
                        : Task.FromResult(UndeclaredToolResult(item.ToolName)),
                    maxParallelTools,
                    maxParallelClientTools,
                    outcomeChannel.Writer,
                    cancellationToken,
                    _logger);

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

                foreach (var o in outcomes)
                    if (o.DelegatedRunCosts is { Count: > 0 } costs) result.DelegatedRunCosts.AddRange(costs);

                result.ToolTimeMs += (long)System.Diagnostics.Stopwatch
                    .GetElapsedTime(batchStartTimestamp).TotalMilliseconds;

                int budgetChars = _configuration.GetValue<int>("ToolExecutionLimits:MaxBatchResultLength", 40000);
                int batchBudgetChars = Math.Max(budgetChars, execContext.MaxResultLength);
                var batchBudget = new ToolBatchResultBudget(batchBudgetChars);

                var providerResults = new List<ProviderToolResult>(outcomes.Count);
                bool budgetNoticeEmittedThisIteration = false;
                for (int i = 0; i < outcomes.Count; i++)
                {
                    var outcome = outcomes[i];
                    if (outcome.BudgetExhausted)
                    {
                        result.ToolBudgetExhausted = true;
                        result.ToolCallsBlocked++;
                    }

                    // The exemption exists so a handler override larger than the batch budget is not
                    // defeated by it — refresh_snapshot's 60200 against a 40000 budget. Compared
                    // against the batch budget rather than the per-tool cap, so an override that
                    // fits inside the batch budget stays accountable to it: measured against
                    // MaxResultLength alone, every override exempted its tool outright.
                    bool exemptFromBatchBudget =
                        _toolExecutor.GetEffectiveMaxResultLength(outcome.ToolName, execContext.MaxResultLength)
                            > batchBudgetChars;

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

                    if (!budgetNoticeEmittedThisIteration && outcome.RemainingBudget.HasValue)
                    {
                        int totalBudget = execContext.MaxCallsPerSession;
                        int threshold = Math.Max(3, (int)(totalBudget * 0.20));
                        if (outcome.RemainingBudget.Value <= threshold)
                        {
                            string notice = $"[Tool budget: {outcome.RemainingBudget.Value} of {totalBudget} calls remaining for this question.]";
                            finalContent = string.IsNullOrEmpty(finalContent)
                                ? notice
                                : $"{finalContent}\n\n{notice}";
                            budgetNoticeEmittedThisIteration = true;
                        }
                    }

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

                    /* Masked on the way OUT and nowhere else. providerResults is what re-enters
                       messageHistory and therefore what the next request carries, so a
                       credential a tool returned -- from a file it read, a corpus it searched or
                       a sub-agent it ran -- would otherwise reach the provider by the longer
                       route even though the user's own message was masked.

                       streamTc below keeps outcome.Content unmasked, deliberately: that row is
                       what the user sees and what the database stores, and in a confidential
                       session it is encrypted at the choke point in ChatService. Masking is an
                       egress control, not a storage one. */
                    providerResults.Add(new ProviderToolResult
                    {
                        ToolCallId = outcome.ToolCallId,
                        ToolName = outcome.ToolName,
                        Content = request.DlpVault?.Mask(finalContent) ?? finalContent,
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
        result.ModelCallCount = budget?.TotalModelCalls ?? 1;
        result.ToolCallCount = result.ToolCalls.Count;
        result.PermitWaitMs = result.ModelCalls.Sum(c => (long)c.PermitWaitMs);
        result.BackoffWaitMs = result.ModelCalls.Sum(c => (long)c.BackoffWaitMs);
        result.RetryAttemptCount = result.ModelCalls.Sum(c => Math.Max(0, c.AttemptCount - 1));

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
        string? requestedServiceTier,
        ApiKeyAlerts.ApiKeyAlertContext? alertContext,
        string? apiKey,
        string modelId,
        string? requestUri,
        ModelCallTracker call)
    {
        int[] retryDelays = { 1, 5, 10, 20, 30, 60 };
        int attempt = 0;
        bool success = false;
        var record = call.Record;

        while (!success && !cancellationToken.IsCancellationRequested)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            bool retryScheduled = false;
            if (showDebugLog)
            {
                yield return new ChatEvent { Type = "debug", Data = $"{mainPrefix} - {providerName}] Starting POST request (Attempt {attempt + 1})..." };
            }
            yield return new ChatEvent { Type = "status", Data = $"Waiting for {providerName}..." };

            // The iteration's attempt record, appended in the finally below however the iteration ends.
            var draft = new AttemptDraft();
            IDisposable? permit = null;
            try
            {
                string? throttleError = null;
                if (_governor != null && !string.IsNullOrEmpty(credentialKey))
                {
                    // The governor's cooldown sleeps inside AcquirePermitAsync, so it counts as permit wait.
                    long permitStartTicks = ProviderCallMeta.Now();
                    try
                    {
                        permit = await _governor.AcquirePermitAsync(credentialKey, permitWaitTimeout, cancellationToken);
                    }
                    catch (TimeoutException tex)
                    {
                        throttleError = $"Request throttled: {tex.Message}";
                    }
                    catch (OperationCanceledException)
                    {
                        record.ErrorKind ??= "canceled";
                        draft.CanceledBeforeSend = true;
                        throw;
                    }
                    finally
                    {
                        record.PermitWaitMs += ElapsedMsSince(permitStartTicks);
                    }
                }

                if (throttleError != null)
                {
                    record.ErrorKind ??= "permit_timeout";
                    draft.GovernorRefused = true;
                    draft.EndTicks = ProviderCallMeta.Now();
                    yield return new ChatEvent { Type = "error", Data = throttleError };
                    yield break;
                }

                HttpResponseMessage? response = null;
                Exception? requestException = null;
                record.AttemptCount++;
                record.HeadersMs = null;
                record.StreamEndMs = null;
                record.FinalHttpStatus = null;
                record.HttpVersion = null;
                record.RequestId = null;
                record.ServerProcessingMs = null;
                record.RateLimitJson = null;
                // The request factory restamps just before SendAsync; this covers a factory that throws first.
                call.AttemptSendTicks = ProviderCallMeta.Now();
                draft.Stage = AttemptStage.AwaitingHeaders;
                try
                {
                    response = await requestFactory(cancellationToken);
                }
                catch (Exception ex)
                {
                    requestException = ex;
                }

                long sendTicks = call.AttemptSendTicks;
                call.SendTicks = sendTicks;
                draft.SendTicks = sendTicks;

                if (requestException != null)
                {
                    draft.Exception = requestException;
                    draft.EndTicks = ProviderCallMeta.Now();
                    sw.Stop();
                    bool isHttpClientTimeout = requestException is TaskCanceledException tce
                        && (tce.InnerException is TimeoutException || requestException.Message.Contains("HttpClient.Timeout"));

                    record.ErrorKind ??= isHttpClientTimeout ? "timeout"
                        : cancellationToken.IsCancellationRequested ? "canceled"
                        : "exception";

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
                record.HeadersMs = ElapsedMsSince(sendTicks);

                if (response != null && _governor != null && !string.IsNullOrEmpty(credentialKey))
                {
                    _governor.UpdateLimitsFromHeaders(credentialKey, response);
                }

                if (response != null)
                {
                    var headers = ProviderResponseHeaders.From(response);
                    record.RequestId = headers.RequestId;
                    record.ServerProcessingMs = headers.ServerProcessingMs;
                    record.RateLimitJson = headers.RateLimitJson;
                    record.HttpVersion = response.Version.ToString();
                    record.FinalHttpStatus = (int)response.StatusCode;
                    draft.HttpStatus = (int)response.StatusCode;
                    draft.Stage = AttemptStage.Streaming;
                    if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    {
                        record.Http429Count++;
                    }
                    else if ((int)response.StatusCode >= 500 && (int)response.StatusCode <= 599)
                    {
                        record.Http5xxCount++;
                    }
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
                                draft.Exception ??= ex;
                                break;
                            }
                            catch (Exception ex)
                            {
                                draft.Exception ??= ex;
                                draft.EndTicks ??= ProviderCallMeta.Now();
                                throw;
                            }

                            if (!hasNext) break;
                            var evt = enumerator.Current;

                            if (evt.Type == "service_tier")
                            {
                                sawTierEvent = true;
                            }
                            else if (evt.Type == "call_meta" && evt.CallMeta?.CompletedTicks != null)
                            {
                                draft.CompletionSeen = true;
                            }

                            if (!firstChunkReceived && (evt.Type == "chunk" || evt.Type == "thinking_chunk" || evt.Type == "tool_call_complete"))
                            {
                                firstChunkReceived = true;
                                if (showDebugLog)
                                {
                                    yield return new ChatEvent { Type = "debug", Data = $"{mainPrefix} - {providerName}] First token received after {ElapsedMsSince(sendTicks)}ms" };
                                }
                            }

                            if (evt.Type == "chunk" || evt.Type == "thinking_chunk" || evt.Type == "tool_call_complete")
                            {
                                hasYieldedChunks = true;
                                if (!draft.StreamErrorEvent)
                                {
                                    draft.ChunksReceived = true;
                                }
                            }

                            // The first provider error event is the attempt's evidence.
                            if (evt.Type == "error" && !draft.StreamErrorEvent)
                            {
                                CaptureStreamError(draft, providerName, evt, response);
                            }

                            if (evt.Type == "error" && !hasYieldedChunks)
                            {
                                string? streamErrorText = evt.Detail ?? evt.Data;
                                var streamKeyFailure = ApiKeyAlerts.ApiKeyFailureClassifier.Classify(providerName, null, streamErrorText);
                                if (streamKeyFailure != null)
                                {
                                    await RecordAndReportKeyFailureAsync(
                                        streamKeyFailure.Value, providerName, systemModelId, alertContext, apiKey, modelId, requestUri,
                                        null, null, streamErrorText, response.Headers, sw.ElapsedMilliseconds, attempt + 1, requestedServiceTier);
                                }

                                bool isRetryable = streamKeyFailure == null && ProviderErrorRetryPolicy.IsRetryable(evt.Data);

                                if (isRetryable && attempt < retryDelays.Length)
                                {
                                    int delaySeconds = retryDelays[attempt];
                                    if (_governor != null && !string.IsNullOrEmpty(credentialKey))
                                    {
                                        _governor.RecordRateLimit(credentialKey, TimeSpan.FromSeconds(delaySeconds));
                                    }

                                    draft.EndTicks = ProviderCallMeta.Now();
                                    record.FailedAttemptMs += ElapsedMsSince(sendTicks);
                                    record.StreamErrorRetryCount++;

                                    yield return new ChatEvent { Type = "status", Data = $"API Overloaded (attempt {attempt + 1}/{retryDelays.Length + 1}). Retrying in {delaySeconds}s..." };
                                    if (showDebugLog)
                                    {
                                        yield return new ChatEvent { Type = "debug", Data = $"{mainPrefix} - {providerName}] Sleeping for {delaySeconds}s before retry due to stream error: {evt.Data}" };
                                    }

                                    bool shouldBreak = false;
                                    long backoffStartTicks = ProviderCallMeta.Now();
                                    try
                                    {
                                        await Task.Delay(delaySeconds * 1000, cancellationToken);
                                    }
                                    catch (TaskCanceledException)
                                    {
                                        shouldBreak = true;
                                    }
                                    record.BackoffWaitMs += ElapsedMsSince(backoffStartTicks);

                                    if (shouldBreak)
                                    {
                                        record.ErrorKind ??= "canceled";
                                        break;
                                    }

                                    attempt++;
                                    retryTriggered = true;
                                    retryScheduled = true;
                                    break;
                                }
                                else if (isRetryable)
                                {
                                    record.ErrorKind ??= "stream_error";
                                    if (showDebugLog)
                                    {
                                        yield return new ChatEvent { Type = "debug", Data = $"{mainPrefix} - {providerName}] Max retries exhausted for stream error: {evt.Data}" };
                                    }
                                    yield return new ChatEvent
                                    {
                                        Type = "error",
                                        Data = $"The {providerName} API is currently overloaded. Max retries ({retryDelays.Length + 1}) exceeded. Please try again later.",
                                        // The provider's own last failure payload, so the cause survives the retry loop.
                                        Detail = evt.Detail
                                    };
                                    break;
                                }
                            }

                            if (!retryTriggered)
                            {
                                if (evt.Type == "error")
                                {
                                    record.ErrorKind ??= "stream_error";
                                }
                                yield return evt;
                            }
                        }

                        draft.EndTicks ??= ProviderCallMeta.Now();
                        record.StreamEndMs = ElapsedMsSince(sendTicks);
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
                        int elapsedSeconds = ElapsedMsSince(sendTicks) / 1000;
                        record.ErrorKind ??= cancellationToken.IsCancellationRequested ? "canceled"
                            : streamException is OperationCanceledException ? "timeout"
                            : "stream_error";
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
                    int attemptMs = ElapsedMsSince(sendTicks);
                    CaptureErrorResponse(draft, errorBody, response);
                    // The debug event only reaches a session that asked for it; the failure itself is
                    // always worth a server-side line, with the body bounded so a wall of HTML cannot
                    // flood the log.
                    string boundedErrorBody = errorBody.Length > MaxLoggedErrorBodyLength
                        ? errorBody.Substring(0, MaxLoggedErrorBodyLength)
                        : errorBody;
                    _logger.LogWarning("{Provider} HTTP {Status}: {Body}", providerName, (int)response.StatusCode, boundedErrorBody);
                    if (showDebugLog)
                    {
                        yield return new ChatEvent { Type = "debug", Data = $"{mainPrefix} - {providerName}] HTTP {(int)response.StatusCode} Received ({sw.ElapsedMilliseconds}ms)\nBody: {errorBody}" };
                    }

                    // Before the 429 branch: OpenAI reports an empty balance as 429 insufficient_quota,
                    // and neither an empty balance nor a rejected key clears by retrying.
                    var keyFailure = ApiKeyAlerts.ApiKeyFailureClassifier.Classify(providerName, (int)response.StatusCode, errorBody);
                    draft.ApiKeyBilling = keyFailure == ApiKeyAlerts.ApiKeyFailureKind.InsufficientBalance;
                    draft.ApiKeyRejected = keyFailure == ApiKeyAlerts.ApiKeyFailureKind.KeyRejected;
                    if (keyFailure != null)
                    {
                        await RecordAndReportKeyFailureAsync(
                            keyFailure.Value, providerName, systemModelId, alertContext, apiKey, modelId, requestUri,
                            (int)response.StatusCode, response.ReasonPhrase, errorBody, response.Headers,
                            sw.ElapsedMilliseconds, attempt + 1, requestedServiceTier);

                        record.ErrorKind ??= HttpErrorKind((int)response.StatusCode);
                        if (keyFailure == ApiKeyAlerts.ApiKeyFailureKind.InsufficientBalance && systemModelId.HasValue)
                        {
                            yield return new ChatEvent { Type = "error", Data = "The system provider budget has been exhausted. Please contact the administrator." };
                        }
                        else
                        {
                            yield return new ChatEvent
                            {
                                Type = "error",
                                Data = $"API Error: {(int)response.StatusCode} - {errorBody}",
                                Detail = boundedErrorBody
                            };
                        }
                        yield break;
                    }

                    // A withdrawn model does not come back by retrying.
                    if (ModelNotFoundClassifier.IsModelNotFound(providerName, (int)response.StatusCode, errorBody, modelId))
                    {
                        draft.ModelNotFound = true;
                        record.ErrorKind ??= "model_not_found";
                        if (systemModelId.HasValue)
                        {
                            using var errScope = _scopeFactory.CreateScope();
                            var errService = errScope.ServiceProvider.GetRequiredService<SystemAiConfigService>();
                            await errService.RecordErrorAsync(systemModelId.Value, $"404 Model Not Found ({modelId})");
                        }
                        string remedy = systemModelId.HasValue
                            ? "Choose another model; an administrator can update this configuration."
                            : "Choose another model, or update this model in Models.";
                        yield return new ChatEvent
                        {
                            Type = "error",
                            ErrorCode = "model_unavailable",
                            Detail = boundedErrorBody,
                            Data = $"{providerName} no longer serves the model {modelId}. {remedy}"
                        };
                        yield break;
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
                            record.FailedAttemptMs += attemptMs;
                            yield return new ChatEvent { Type = "status", Data = $"Rate limited (429). Retrying in {delay.TotalSeconds:F0}s (attempt {attempt + 1}/{maxRetries})..." };
                            if (showDebugLog)
                            {
                                yield return new ChatEvent { Type = "debug", Data = $"{mainPrefix} - {providerName}] 429 received. Cooldown {delay.TotalSeconds:F1}s before retry (attempt {attempt + 1}/{maxRetries})." };
                            }

                            long backoffStartTicks = ProviderCallMeta.Now();
                            try
                            {
                                await Task.Delay(delay, cancellationToken);
                            }
                            catch (TaskCanceledException)
                            {
                                record.BackoffWaitMs += ElapsedMsSince(backoffStartTicks);
                                record.ErrorKind ??= "canceled";
                                yield break;
                            }
                            record.BackoffWaitMs += ElapsedMsSince(backoffStartTicks);

                            attempt++;
                            retryScheduled = true;
                            continue;
                        }
                        else
                        {
                            record.ErrorKind ??= "http_429";
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
                    else if (response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable || response.StatusCode == System.Net.HttpStatusCode.BadGateway || response.StatusCode == System.Net.HttpStatusCode.InternalServerError)
                    {
                        if (attempt < retryDelays.Length)
                        {
                            int delaySeconds = retryDelays[attempt];
                            record.FailedAttemptMs += attemptMs;
                            yield return new ChatEvent { Type = "status", Data = $"503 Unavailable. Retrying in {delaySeconds}s..." };
                            if (showDebugLog)
                            {
                                yield return new ChatEvent { Type = "debug", Data = $"{mainPrefix} - {providerName}] Sleeping for {delaySeconds}s before retry..." };
                            }

                            long backoffStartTicks = ProviderCallMeta.Now();
                            try
                            {
                                await Task.Delay(delaySeconds * 1000, cancellationToken);
                            }
                            catch (TaskCanceledException)
                            {
                                record.BackoffWaitMs += ElapsedMsSince(backoffStartTicks);
                                record.ErrorKind ??= "canceled";
                                yield break;
                            }
                            record.BackoffWaitMs += ElapsedMsSince(backoffStartTicks);

                            attempt++;
                            retryScheduled = true;
                        }
                        else
                        {
                            record.ErrorKind ??= "http_5xx";
                            yield return new ChatEvent { Type = "error", Data = "503 Unavailable. Max retries exceeded." };
                            yield break;
                        }
                    }
                    else
                    {
                        record.ErrorKind ??= HttpErrorKind((int)response.StatusCode);
                        yield return new ChatEvent
                        {
                            Type = "error",
                            Data = $"API Error: {(int)response.StatusCode} - {errorBody}",
                            Detail = boundedErrorBody
                        };
                        yield break;
                    }
                }
            }
            finally
            {
                permit?.Dispose();
                // An exception, or a consumer that stopped enumerating, ends the call here without a verdict.
                if (!success && !retryScheduled)
                {
                    record.ErrorKind ??= cancellationToken.IsCancellationRequested ? "canceled" : "exception";
                }

                // Before an exception leaves the runner, so a caller holding the result reads the attempt.
                AppendAttempt(call, draft, providerName, success, retryScheduled, cancellationToken);
            }
        }

        // Reached without success only when cancellation stopped the loop between attempts.
        if (!success)
        {
            record.ErrorKind ??= "canceled";
            SetTerminalOutcome(call, cancellationToken);
        }
    }

    /// <summary>
    /// Appends the iteration's <see cref="ProviderAttemptRecord"/> to the call and refreshes the run's
    /// terminal outcome. Never throws: it runs in a finally, where a throw would replace the exception
    /// that is leaving.
    /// </summary>
    private void AppendAttempt(
        ModelCallTracker call, AttemptDraft draft, string providerName, bool success, bool retried, CancellationToken cancellationToken)
    {
        try
        {
            var record = call.Record;
            long endTicks = draft.EndTicks ?? ProviderCallMeta.Now();
            long startTicks = draft.SendTicks ?? draft.StartTicks;
            bool tokenCanceled = cancellationToken.IsCancellationRequested;

            // A verdict the provider (or our governor) gave. A 2xx stream that ended without its
            // completion event while our token was canceled was cut by us, not broken by the provider.
            bool httpError = draft.HttpStatus is int status && (status < 200 || status > 299);
            bool completed = success && (draft.CompletionSeen || draft.StreamErrorEvent || !tokenCanceled);
            bool ended = draft.GovernorRefused || httpError || draft.StreamErrorEvent || completed;

            bool cut = !ended && (tokenCanceled || draft.CanceledBeforeSend);
            bool budget = cut && IsQuestionBudgetCancel(call.OperatorCancellation);

            // No verdict, no exception and no cancel: the consumer stopped enumerating mid-attempt.
            bool abandoned = !ended && !cut && draft.Exception == null;

            var fields = draft.Error;
            var evidence = new ProviderAttemptEvidence(
                providerName,
                call.EndpointKind,
                draft.HttpStatus,
                fields?.Code,
                fields?.Type,
                draft.BodyExcerpt,
                draft.RateLimitHeaders,
                draft.Exception,
                draft.Stage,
                draft.ChunksReceived,
                draft.CompletionSeen,
                CallerCanceled: (cut && !budget) || abandoned,
                QuestionBudgetCanceled: budget,
                GovernorRefused: draft.GovernorRefused,
                ApiKeyBilling: draft.ApiKeyBilling,
                ApiKeyRejected: draft.ApiKeyRejected,
                ModelNotFound: draft.ModelNotFound,
                StreamErrorEvent: draft.StreamErrorEvent,
                StreamErrorStatus: draft.StreamErrorEvent ? fields?.NumericCode : null,
                ErrorStatus: fields?.Status,
                ErrorDetailTypes: fields?.DetailTypes);

            var outcome = ProviderAttemptClassifier.Classify(evidence);

            record.Attempts.Add(new ProviderAttemptRecord
            {
                AttemptIndex = record.Attempts.Count,
                StartedAtUtc = draft.StartedAtUtc.AddTicks(StopwatchToTimeSpanTicks(startTicks - draft.StartTicks)),
                DurationMs = Math.Max(0, ProviderCallMeta.MsBetween(startTicks, endTicks) ?? 0),
                Outcome = outcome,
                HttpStatus = draft.HttpStatus,
                ProviderErrorCode = outcome == ProviderAttemptOutcome.Ok || fields == null
                    ? null
                    : ProviderAttemptClassifier.PreferredErrorCode(providerName, fields.Code, fields.Type, fields.Status),
                EndpointKind = call.EndpointKind,
                Retried = retried,
                RetryAfterMs = draft.RetryAfterMs,
                QuestionBudgetCanceled = budget
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record a provider attempt of {Provider}.", providerName);
        }

        SetTerminalOutcome(call, cancellationToken);
    }

    /// <summary>
    /// Sets the run's terminal outcome from this call: cleared when the call produced a response,
    /// otherwise the class of its last attempt, or <c>OurCanceled</c> when a cancel ended the call
    /// between attempts (during a backoff, or before the next send).
    /// </summary>
    private static void SetTerminalOutcome(ModelCallTracker call, CancellationToken cancellationToken)
    {
        var result = call.Result;
        if (result == null) return;

        var record = call.Record;
        var last = record.Attempts.Count > 0 ? record.Attempts[^1] : null;

        if (record.ErrorKind == null && last?.Outcome != ProviderAttemptOutcome.OurCanceled)
        {
            result.TerminalOutcome = null;
            result.TerminalErrorCode = null;
            result.TerminalQuestionBudgetCanceled = false;
            return;
        }

        if (last == null || (record.ErrorKind == "canceled" && last.Outcome != ProviderAttemptOutcome.OurCanceled))
        {
            result.TerminalOutcome = ProviderAttemptOutcome.OurCanceled;
            result.TerminalErrorCode = null;
            result.TerminalQuestionBudgetCanceled =
                cancellationToken.IsCancellationRequested && IsQuestionBudgetCancel(call.OperatorCancellation);
            return;
        }

        result.TerminalOutcome = last.Outcome;
        result.TerminalErrorCode = last.ProviderErrorCode;
        result.TerminalQuestionBudgetCanceled = last.QuestionBudgetCanceled;
    }

    /// <summary>
    /// The run's token fired while the operator's had not: the caller's own time budget. A caller with
    /// no budget passes <see cref="CancellationToken.None"/>, and every cancel is then the caller's.
    /// </summary>
    private static bool IsQuestionBudgetCancel(CancellationToken operatorCancellation) =>
        operatorCancellation.CanBeCanceled && !operatorCancellation.IsCancellationRequested;

    private static long StopwatchToTimeSpanTicks(long stopwatchTicks) =>
        (long)(stopwatchTicks * ((double)TimeSpan.TicksPerSecond / System.Diagnostics.Stopwatch.Frequency));

    /// <summary>Records the first provider error event of a 2xx stream as the attempt's evidence.</summary>
    private static void CaptureStreamError(AttemptDraft draft, string providerName, ChatEvent evt, HttpResponseMessage response)
    {
        string? text = evt.Detail ?? evt.Data;
        draft.StreamErrorEvent = true;
        draft.Error = ProviderAttemptClassifier.ParseStreamErrorEvent(evt.Data, evt.Detail);
        draft.BodyExcerpt = ProviderAttemptClassifier.Excerpt(text);
        var keyFailure = ApiKeyAlerts.ApiKeyFailureClassifier.Classify(providerName, null, text);
        draft.ApiKeyBilling = keyFailure == ApiKeyAlerts.ApiKeyFailureKind.InsufficientBalance;
        draft.ApiKeyRejected = keyFailure == ApiKeyAlerts.ApiKeyFailureKind.KeyRejected;
        draft.RateLimitHeaders = ProviderResponseEvidence.ReadRateLimitHeaders(response);
    }

    /// <summary>Records a non-2xx response's error fields, rate-limit headers and Retry-After as the attempt's evidence.</summary>
    private static void CaptureErrorResponse(AttemptDraft draft, string errorBody, HttpResponseMessage response)
    {
        draft.EndTicks = ProviderCallMeta.Now();
        draft.Error = ProviderAttemptClassifier.ParseErrorBody(errorBody);
        draft.BodyExcerpt = ProviderAttemptClassifier.Excerpt(errorBody);
        draft.RateLimitHeaders = ProviderResponseEvidence.ReadRateLimitHeaders(response);
        draft.RetryAfterMs = ProviderResponseEvidence.ReadRetryAfterMs(response);
    }

    /// <summary>What one loop iteration has seen so far, from which its <see cref="ProviderAttemptRecord"/> is built.</summary>
    private sealed class AttemptDraft
    {
        public DateTime StartedAtUtc { get; } = DateTime.UtcNow;

        public long StartTicks { get; } = ProviderCallMeta.Now();

        public long? SendTicks { get; set; }

        public long? EndTicks { get; set; }

        public AttemptStage Stage { get; set; } = AttemptStage.BeforeSend;

        public bool GovernorRefused { get; set; }

        /// <summary>The permit wait was canceled; no request was sent.</summary>
        public bool CanceledBeforeSend { get; set; }

        public int? HttpStatus { get; set; }

        public ProviderErrorFields? Error { get; set; }

        public bool StreamErrorEvent { get; set; }

        /// <summary>At most <see cref="ProviderAttemptClassifier.MaxBodyExcerptLength"/> characters, dropped with the draft.</summary>
        public string? BodyExcerpt { get; set; }

        public IReadOnlyDictionary<string, string>? RateLimitHeaders { get; set; }

        public int? RetryAfterMs { get; set; }

        public Exception? Exception { get; set; }

        /// <summary>Output arrived before the first provider error event (or at all, without one).</summary>
        public bool ChunksReceived { get; set; }

        public bool CompletionSeen { get; set; }

        public bool ApiKeyBilling { get; set; }

        public bool ApiKeyRejected { get; set; }

        public bool ModelNotFound { get; set; }
    }

    private static int ElapsedMsSince(long startTicks) =>
        ProviderCallMeta.MsBetween(startTicks, ProviderCallMeta.Now()) ?? 0;

    private static string HttpErrorKind(int status) =>
        status == 429 ? "http_429"
        : status >= 500 && status <= 599 ? "http_5xx"
        : $"http_{status}";

    /// <summary>The record of the model call in flight, and the send stamps its marks are relative to.</summary>
    private sealed class ModelCallTracker
    {
        public ModelCallTracker(ModelCallRecord record) => Record = record;

        public ModelCallRecord Record { get; }

        /// <summary>Stopwatch ticks just before the current attempt's <c>SendAsync</c>; the request factory sets it.</summary>
        public long AttemptSendTicks { get; set; }

        /// <summary>The send of the latest attempt: the successful one once the stream is read.</summary>
        public long SendTicks { get; set; }

        /// <summary>The run whose terminal outcome each attempt refreshes; null for none.</summary>
        public AgentRunResult? Result { get; init; }

        /// <summary>The operator's own token, which tells a caller budget's cancel from the operator's.</summary>
        public CancellationToken OperatorCancellation { get; init; }

        public BenchmarkEndpointKind EndpointKind { get; init; }
    }

    /// <summary>The URL the request factory posts to, or null if it cannot be built; the request reports that failure itself.</summary>
    private static string? TryGetChatStreamUrl(IAiProvider aiProvider, AgentRunRequest request)
    {
        try
        {
            return aiProvider.GetChatStreamUrl(request.ModelId, request.ApiKey ?? "", request.Endpoint);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Records a key failure against the System AI Config and, for a user-facing run on an operator
    /// key, queues the alert email. Never throws into the chat.
    /// </summary>
    private async Task RecordAndReportKeyFailureAsync(
        ApiKeyAlerts.ApiKeyFailureKind kind,
        string providerName,
        long? systemModelId,
        ApiKeyAlerts.ApiKeyAlertContext? alertContext,
        string? apiKey,
        string modelId,
        string? requestUri,
        int? httpStatus,
        string? httpReason,
        string? body,
        IEnumerable<KeyValuePair<string, IEnumerable<string>>>? responseHeaders,
        long elapsedMs,
        int attempt,
        string? serviceTier)
    {
        if (!systemModelId.HasValue) return;

        try
        {
            using var scope = _scopeFactory.CreateScope();

            string message = ApiKeyValidator.ExtractProviderError(body, apiKey ?? string.Empty) ?? "(no message)";
            string status = httpStatus.HasValue ? "HTTP " + httpStatus.Value : "stream error";
            var errService = scope.ServiceProvider.GetService<SystemAiConfigService>();
            if (errService != null)
            {
                await errService.RecordErrorAsync(systemModelId.Value, $"{kind}: {status} {message}");
            }

            if (alertContext != null && !string.IsNullOrEmpty(apiKey))
            {
                var alertService = scope.ServiceProvider.GetService<ApiKeyAlerts.ApiKeyAlertService>();
                alertService?.TryReport(ApiKeyAlerts.ApiKeyFailureReport.Create(
                    kind, providerName, systemModelId.Value, apiKey, alertContext, DateTime.UtcNow,
                    httpStatus, httpReason, body, requestUri, responseHeaders, elapsedMs, attempt, modelId, serviceTier,
                    alertService.Options.MaxResponseBodyChars));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record or report the {Kind} failure of System AI Config {ConfigId}.", kind, systemModelId);
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
