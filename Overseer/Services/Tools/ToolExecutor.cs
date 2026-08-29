using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;

namespace Overseer.Services.Tools
{
    public class ToolExecutor
    {
        private readonly IEnumerable<IToolHandler> _handlers;
        private readonly IClientToolBridge _clientBridge;
        private readonly ILogger<ToolExecutor> _logger;
        private readonly IMemoryCache _cache;
        private readonly object _rateLimitLock = new object();
        private readonly SemaphoreSlim _processThrottler;
        private readonly SemaphoreSlim _externalLookupThrottler;
        private readonly SemaphoreSlim _subAgentThrottler;
        private readonly int _minCallsPerSessionWithSubAgents;
        private readonly int _subAgentQueueWaitSeconds;

        public ToolExecutor(
            IEnumerable<IToolHandler> handlers,
            IClientToolBridge clientBridge,
            ILogger<ToolExecutor> logger,
            IMemoryCache cache,
            IConfiguration configuration)
        {
            _handlers = handlers;
            _clientBridge = clientBridge;
            _logger = logger;
            _cache = cache;

            int maxProcess = configuration.GetValue<int>("ToolExecutionLimits:MaxProcessParallelToolCalls", 30);
            int maxLookup = configuration.GetValue<int>("ToolExecutionLimits:MaxProcessExternalLookupCalls", 3);
            int maxProcessSubAgents = configuration.GetValue<int>("SubAgentSettings:MaxProcessParallelSubAgents", 12);
            _subAgentQueueWaitSeconds = configuration.GetValue<int>("SubAgentSettings:SubAgentQueueWaitSeconds", 30);
            _minCallsPerSessionWithSubAgents = configuration.GetValue<int>("SubAgentSettings:MinCallsPerSessionWithSubAgents", 200);

            _processThrottler = new SemaphoreSlim(Math.Max(1, maxProcess));
            _externalLookupThrottler = new SemaphoreSlim(Math.Max(1, maxLookup));
            _subAgentThrottler = new SemaphoreSlim(Math.Max(1, maxProcessSubAgents));
        }

        public async Task<ToolResult> ExecuteAsync(
            string toolName,
            JsonElement parameters,
            ToolExecutionContext context,
            CancellationToken cancellationToken = default,
            string? toolCallId = null)
        {
            var callContext = context.CloneFor(toolCallId ?? Guid.NewGuid().ToString());

            using (_logger.BeginScope(new Dictionary<string, object>
            {
                ["ToolCallId"] = callContext.ToolCallId ?? "",
                ["SessionId"] = callContext.SessionId,
                ["ToolName"] = toolName,
                ["AgentName"] = callContext.AgentName ?? "Coordinator",
                ["AgentDepth"] = callContext.AgentDepth
            }))
            {
                _logger.LogInformation("Executing tool {ToolName} for Session {SessionId} (Agent: {AgentName}, Depth: {Depth})", 
                    toolName, callContext.SessionId, callContext.AgentName ?? "Coordinator", callContext.AgentDepth);

                // 1. Session Rate Limiting (atomic — tools may run concurrently)
                bool isSubAgentCall = callContext.AgentDepth > 0;
                var rateLimitKey = isSubAgentCall
                    ? $"tool_calls_session_{callContext.SessionId}_sub"
                    : $"tool_calls_session_{callContext.SessionId}";
                int sessionLimit = isSubAgentCall
                    ? Math.Max(callContext.MaxCallsPerSession, _minCallsPerSessionWithSubAgents)
                    : callContext.MaxCallsPerSession;
                bool allowed;
                lock (_rateLimitLock)
                {
                    int count = _cache.GetOrCreate(rateLimitKey, entry =>
                    {
                        entry.Size = 1;
                        entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(4);
                        return 0;
                    });

                    allowed = count < sessionLimit;
                    if (allowed)
                    {
                        _cache.Set(rateLimitKey, count + 1, new MemoryCacheEntryOptions
                        {
                            Size = 1,
                            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(4)
                        });
                    }
                }

                if (!allowed)
                {
                    _logger.LogWarning("Rate limit exceeded for Session {SessionId}", callContext.SessionId);
                    return new ToolResult { Success = false, ErrorMessage = "Maximum tool calls per session exceeded." };
                }

                // Enhanced Audit Logging
                _logger.LogInformation("Tool Execution Audit - Session: {SessionId}, Tool: {ToolName}, Parameters: {Parameters}", 
                    callContext.SessionId, toolName, parameters.GetRawText());

                // 2. Find Handler
                var handler = _handlers.FirstOrDefault(h => string.Equals(h.ToolName, toolName, StringComparison.OrdinalIgnoreCase));
                if (handler == null)
                {
                    return new ToolResult { Success = false, ErrorMessage = $"Tool '{toolName}' is not registered or not available." };
                }

                // 3. Throttling and Execution
                SemaphoreSlim? categoryThrottler = null;
                bool requiresProcessThrottler = true;

                if (handler.Category == ToolCategory.ExternalLookup)
                {
                    categoryThrottler = _externalLookupThrottler;
                }
                else if (handler.Category == ToolCategory.SubAgent)
                {
                    categoryThrottler = _subAgentThrottler;
                    requiresProcessThrottler = false; // Subagents do not consume normal process slots
                }

                bool categorySlot = false;
                bool processSlot = false;
                var swQueue = Stopwatch.StartNew();
                try
                {
                    if (categoryThrottler != null)
                    {
                        if (handler.Category == ToolCategory.SubAgent)
                        {
                            categorySlot = await categoryThrottler.WaitAsync(
                                TimeSpan.FromSeconds(_subAgentQueueWaitSeconds), cancellationToken);
                            if (!categorySlot)
                            {
                                return new ToolResult
                                {
                                    Success = false,
                                    ErrorMessage = "The server is at subagent capacity. Try the lookup directly, or retry shortly.",
                                    TerminationStatus = "error"
                                };
                            }
                        }
                        else
                        {
                            await categoryThrottler.WaitAsync(cancellationToken);
                            categorySlot = true;
                        }
                    }
                    if (requiresProcessThrottler)
                    {
                        await _processThrottler.WaitAsync(cancellationToken);
                        processSlot = true;
                    }
                }
                catch (OperationCanceledException)
                {
                    if (processSlot) _processThrottler.Release();
                    if (categorySlot) categoryThrottler?.Release();
                    return new ToolResult { Success = false, ErrorMessage = "Tool execution was canceled (request stopped)." };
                }
                finally
                {
                    swQueue.Stop();
                }

                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(handler.TimeoutSeconds));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
                ToolResult result;
                var swExec = Stopwatch.StartNew();

                try
                {
                    if (handler.ExecutionLocation == ToolExecutionLocation.Server)
                    {
                        result = await handler.ExecuteAsync(parameters, callContext, linkedCts.Token);
                    }
                    else if (handler.ExecutionLocation == ToolExecutionLocation.Client)
                    {
                        result = await _clientBridge.SendToolRequestAsync(callContext.SessionId, toolName, parameters, linkedCts.Token);
                        if (result.Success && !string.IsNullOrEmpty(result.Content) && result.Content.IndexOf('\u00A0') >= 0)
                        {
                            result.Content = result.Content.Replace('\u00A0', ' ');
                        }
                    }
                    else
                    {
                        result = new ToolResult { Success = false, ErrorMessage = "Unsupported execution location." };
                    }
                }
                catch (OperationCanceledException)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        _logger.LogInformation("Tool {ToolName} canceled by outer request for Session {SessionId}", toolName, callContext.SessionId);
                        result = new ToolResult { Success = false, ErrorMessage = "Tool execution was canceled (request stopped)." };
                    }
                    else
                    {
                        _logger.LogWarning("Tool {ToolName} timed out after {Timeout}s for Session {SessionId}", toolName, handler.TimeoutSeconds, callContext.SessionId);
                        result = new ToolResult { Success = false, ErrorMessage = $"Tool execution timed out after {handler.TimeoutSeconds} seconds." };
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error executing tool {ToolName} for Session {SessionId}", toolName, callContext.SessionId);
                    result = new ToolResult { Success = false, ErrorMessage = $"An error occurred during tool execution: {ex.Message}" };
                }
                finally
                {
                    swExec.Stop();
                    if (processSlot) _processThrottler.Release();
                    if (categorySlot) categoryThrottler?.Release();
                }

                result.QueueWaitMs = swQueue.ElapsedMilliseconds;
                result.ExecutionMs = swExec.ElapsedMilliseconds;

                // 4. Truncation
                if (result.Success && !string.IsNullOrEmpty(result.Content))
                {
                    int baseMaxLen = handler.Category == ToolCategory.SubAgent
                        ? callContext.MaxSubAgentResultLength
                        : callContext.MaxResultLength;

                    int maxLen = handler.MaxResultLengthOverride is int handlerMax
                        ? Math.Max(baseMaxLen, handlerMax)
                        : baseMaxLen;

                    if (result.Content.Length > maxLen)
                    {
                        if (result.Content.TrimStart().StartsWith("{") || result.Content.TrimStart().StartsWith("["))
                        {
                            try
                            {
                                var doc = JsonDocument.Parse(result.Content);
                                _logger.LogWarning("Tool {ToolName} returned large JSON ({Length} bytes). Returning error to force narrower query.", toolName, result.Content.Length);
                                result = new ToolResult
                                {
                                    Success = false,
                                    ErrorMessage = $"Result too large ({result.Content.Length} chars). Please use a narrower search query to get fewer results.",
                                    QueueWaitMs = result.QueueWaitMs,
                                    ExecutionMs = result.ExecutionMs,
                                    NestedToolCalls = result.NestedToolCalls,
                                    TerminationStatus = result.TerminationStatus
                                };
                            }
                            catch
                            {
                                result.Content = result.Content.Substring(0, maxLen) + "... [Result truncated for length]";
                            }
                        }
                        else
                        {
                            result.Content = result.Content.Substring(0, maxLen) + "... [Result truncated for length]";
                        }
                    }
                }

                _logger.LogInformation("Tool Execution Audit - Session: {SessionId}, Tool: {ToolName}, Success: {Success}, Error: {Error}", 
                    callContext.SessionId, toolName, result.Success, result.ErrorMessage ?? "None");

                return result;
            }
        }

        /// <summary>
        /// The result-length cap that ExecuteAsync will apply to this tool, accounting
        /// for any per-handler override. Returns <paramref name="contextMax"/> for
        /// unknown tools.
        /// </summary>
        public int GetEffectiveMaxResultLength(string toolName, int contextMax)
        {
            var handler = _handlers.FirstOrDefault(
                h => string.Equals(h.ToolName, toolName, StringComparison.OrdinalIgnoreCase));
            return handler?.MaxResultLengthOverride is int handlerMax
                ? Math.Max(contextMax, handlerMax)
                : contextMax;
        }
    }
}
