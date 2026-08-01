using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Overseer.Services.Tools
{
    public class NullClientToolBridge : IClientToolBridge
    {
        public bool IsClientConnected => false;

        public Task<ToolResult> SendToolRequestAsync(long sessionId, string toolName, JsonElement parameters, TimeSpan timeout, CancellationToken ct)
        {
            return Task.FromResult(new ToolResult
            {
                Success = false,
                ErrorMessage = "Client tools are not supported in this version or the client is disconnected."
            });
        }
    }
}
