using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Overseer.Services.Tools
{
    public class ToolExecutor
    {
        private readonly IEnumerable<IToolHandler> _handlers;
        private readonly IClientToolBridge _clientBridge;
        private readonly ILogger<ToolExecutor> _logger;

        // Simple in-memory session rate limiter
        private static readonly ConcurrentDictionary<long, int> _sessionCallCounts = new ConcurrentDictionary<long, int>();
        private const int MaxCallsPerSession = 50;
        private const int MaxResultLength = 3000;

        public ToolExecutor(IEnumerable<IToolHandler> handlers, IClientToolBridge clientBridge, ILogger<ToolExecutor> logger)
        {
            _handlers = handlers;
            _clientBridge = clientBridge;
            _logger = logger;
        }

        public async Task<ToolResult> ExecuteAsync(string toolName, JsonElement parameters, ToolExecutionContext context)
        {
            _logger.LogInformation("Executing tool {ToolName} for Session {SessionId}", toolName, context.SessionId);

            // 1. Session Rate Limiting
            int count = _sessionCallCounts.AddOrUpdate(context.SessionId, 1, (key, oldValue) => oldValue + 1);
            if (count > MaxCallsPerSession)
            {
                _logger.LogWarning("Rate limit exceeded for Session {SessionId}", context.SessionId);
                return new ToolResult { Success = false, ErrorMessage = "Maximum tool calls per session exceeded." };
            }

            // 2. Find Handler
            var handler = _handlers.FirstOrDefault(h => string.Equals(h.ToolName, toolName, StringComparison.OrdinalIgnoreCase));
            if (handler == null)
            {
                // In v2, client tools might not be registered server-side but are forwarded anyway? 
                // Wait, all tools are registered in v1/v2 server-side.
                return new ToolResult { Success = false, ErrorMessage = $"Tool '{toolName}' is not registered or not available." };
            }

            // 3. Execution
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            ToolResult result;

            try
            {
                if (handler.ExecutionLocation == ToolExecutionLocation.Server)
                {
                    result = await handler.ExecuteAsync(parameters, context, cts.Token);
                }
                else if (handler.ExecutionLocation == ToolExecutionLocation.Client)
                {
                    result = await _clientBridge.SendToolRequestAsync(toolName, parameters, TimeSpan.FromSeconds(15), cts.Token);
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
                    // If it's a plain string, we can truncate.
                    if (result.Content.TrimStart().StartsWith("{") || result.Content.TrimStart().StartsWith("["))
                    {
                        // It's likely JSON. For v1, handlers should probably return plain text or we truncate fields individually inside the handler.
                        // Here, we'll try to parse and truncate strings, or if it fails, just truncate the raw string.
                        try
                        {
                            var doc = JsonDocument.Parse(result.Content);
                            // Complex to generically truncate JSON fields. For now, we assume handlers output plain text or handle their own JSON truncation.
                            // We will enforce the hard limit though to protect LLM context, even if it risks malformed JSON, 
                            // but we log a warning.
                            _logger.LogWarning("Truncating JSON string which may cause malformed JSON. Handlers should truncate internally.");
                            result.Content = result.Content.Substring(0, MaxResultLength) + "... [Result truncated for length]";
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

            return result;
        }
    }
}
