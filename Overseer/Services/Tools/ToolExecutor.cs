using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;

namespace Overseer.Services.Tools
{
    public class ToolExecutor
    {
        private readonly IEnumerable<IToolHandler> _handlers;
        private readonly IClientToolBridge _clientBridge;
        private readonly ILogger<ToolExecutor> _logger;
        private readonly Microsoft.Extensions.Caching.Memory.IMemoryCache _cache;

        private const int MaxCallsPerSession = 50;
        private const int MaxResultLength = 3000;

        public ToolExecutor(IEnumerable<IToolHandler> handlers, IClientToolBridge clientBridge, ILogger<ToolExecutor> logger, Microsoft.Extensions.Caching.Memory.IMemoryCache cache)
        {
            _handlers = handlers;
            _clientBridge = clientBridge;
            _logger = logger;
            _cache = cache;
        }

        public async Task<ToolResult> ExecuteAsync(string toolName, JsonElement parameters, ToolExecutionContext context)
        {
            _logger.LogInformation("Executing tool {ToolName} for Session {SessionId}", toolName, context.SessionId);

            // 1. Session Rate Limiting
            var rateLimitKey = $"tool_calls_session_{context.SessionId}";
            var count = _cache.GetOrCreate(rateLimitKey, entry =>
            {
                entry.Size = 1;
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(4);
                return 0;
            });
            
            if (count > MaxCallsPerSession)
            {
                _logger.LogWarning("Rate limit exceeded for Session {SessionId}", context.SessionId);
                return new ToolResult { Success = false, ErrorMessage = "Maximum tool calls per session exceeded." };
            }
            
            _cache.Set(rateLimitKey, count + 1, new Microsoft.Extensions.Caching.Memory.MemoryCacheEntryOptions
            {
                Size = 1,
                AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(4)
            });

            // Enhanced Audit Logging
            _logger.LogInformation("Tool Execution Audit - Session: {SessionId}, Tool: {ToolName}, Parameters: {Parameters}", 
                context.SessionId, toolName, parameters.GetRawText());

            // 2. Find Handler
            var handler = _handlers.FirstOrDefault(h => string.Equals(h.ToolName, toolName, StringComparison.OrdinalIgnoreCase));
            if (handler == null)
            {
                // In v2, client tools might not be registered server-side but are forwarded anyway? 
                // Wait, all tools are registered in v1/v2 server-side.
                return new ToolResult { Success = false, ErrorMessage = $"Tool '{toolName}' is not registered or not available." };
            }

            // 3. Execution
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(handler.TimeoutSeconds));
            ToolResult result;

            try
            {
                if (handler.ExecutionLocation == ToolExecutionLocation.Server)
                {
                    result = await handler.ExecuteAsync(parameters, context, cts.Token);
                }
                else if (handler.ExecutionLocation == ToolExecutionLocation.Client)
                {
                    result = await _clientBridge.SendToolRequestAsync(context.SessionId, toolName, parameters, cts.Token);
                }
                else
                {
                    result = new ToolResult { Success = false, ErrorMessage = "Unsupported execution location." };
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Tool {ToolName} timed out for Session {SessionId}", toolName, context.SessionId);
                result = new ToolResult { Success = false, ErrorMessage = "Tool execution timed out." };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing tool {ToolName} for Session {SessionId}", toolName, context.SessionId);
                result = new ToolResult { Success = false, ErrorMessage = $"An error occurred during tool execution: {ex.Message}" };
            }

            // 4. Truncation
            if (result.Success && !string.IsNullOrEmpty(result.Content))
            {
                if (result.Content.Length > MaxResultLength)
                {
                    // As noted in plan, if it's JSON, blindly truncating the string will break it.
                    if (result.Content.TrimStart().StartsWith("{") || result.Content.TrimStart().StartsWith("["))
                    {
                        try
                        {
                            var doc = JsonDocument.Parse(result.Content);
                            _logger.LogWarning("Tool {ToolName} returned large JSON ({Length} bytes). Returning error to force narrower query.", toolName, result.Content.Length);
                            result = new ToolResult { Success = false, ErrorMessage = $"Result too large ({result.Content.Length} chars). Please use a narrower search query to get fewer results." };
                        }
                        catch
                        {
                            result.Content = result.Content.Substring(0, MaxResultLength) + "... [Result truncated for length]";
                        }
                    }
                    else
                    {
                        result.Content = result.Content.Substring(0, MaxResultLength) + "... [Result truncated for length]";
                    }
                }
            }

            _logger.LogInformation("Tool Execution Audit - Session: {SessionId}, Tool: {ToolName}, Success: {Success}", 
                context.SessionId, toolName, result.Success);

            return result;
        }
    }
}
