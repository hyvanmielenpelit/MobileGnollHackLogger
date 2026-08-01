using System;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Overseer.Hubs;

namespace Overseer.Services.Tools
{
    public class SignalRClientToolBridge : IClientToolBridge
    {
        private readonly IHubContext<ChatHub> _hubContext;
        private readonly ConcurrentDictionary<string, TaskCompletionSource<ToolResult>> _pendingRequests;

        public SignalRClientToolBridge(IHubContext<ChatHub> hubContext)
        {
            _hubContext = hubContext;
            _pendingRequests = new ConcurrentDictionary<string, TaskCompletionSource<ToolResult>>();
        }

        // We assume true for v2. The timeout will catch disconnected clients.
        public bool IsClientConnected => true;

        public async Task<ToolResult> SendToolRequestAsync(long sessionId, string toolName, JsonElement parameters, TimeSpan timeout, CancellationToken ct)
        {
            var requestId = Guid.NewGuid().ToString();
            var tcs = new TaskCompletionSource<ToolResult>(TaskCreationOptions.RunContinuationsAsynchronously);

            _pendingRequests.TryAdd(requestId, tcs);

            try
            {
                var payload = new
                {
                    requestId = requestId,
                    toolName = toolName,
                    parameters = parameters
                };

                var chatEvent = new ChatEvent
                {
                    Type = "tool_client_request",
                    Data = JsonSerializer.Serialize(payload)
                };

                await _hubContext.Clients.Group(sessionId.ToString()).SendAsync("ReceiveChatEvent", chatEvent, ct);

                // Use Task.WhenAny to handle timeout properly
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(timeout);

                using (timeoutCts.Token.Register(() => tcs.TrySetCanceled(timeoutCts.Token)))
                {
                    return await tcs.Task;
                }
            }
            catch (OperationCanceledException)
            {
                return new ToolResult
                {
                    Success = false,
                    ErrorMessage = "Client tool request timed out."
                };
            }
            catch (Exception ex)
            {
                return new ToolResult
                {
                    Success = false,
                    ErrorMessage = $"Failed to send tool request: {ex.Message}"
                };
            }
            finally
            {
                _pendingRequests.TryRemove(requestId, out _);
            }
        }

        public void SubmitToolResult(string requestId, bool success, string content)
        {
            if (_pendingRequests.TryGetValue(requestId, out var tcs))
            {
                tcs.TrySetResult(new ToolResult
                {
                    Success = success,
                    Content = content,
                    ErrorMessage = success ? null : content // By convention, if it fails, error is in content
                });
            }
        }
    }
}
