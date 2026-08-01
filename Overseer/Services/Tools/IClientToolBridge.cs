using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Overseer.Services.Tools
{
    /// <summary>
    /// Abstraction for sending tool requests to the MAUI client.
    /// v1: No-op implementation (returns IsClientConnected=false).
    /// v2: Real implementation via SignalR -> Angular -> JS bridge -> MAUI.
    /// </summary>
    public interface IClientToolBridge
    {
        bool IsClientConnected { get; }
        Task<ToolResult> SendToolRequestAsync(long sessionId, string toolName, JsonElement parameters,
                                              CancellationToken ct);
    }
}
