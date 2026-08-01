using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Overseer.Services.Tools
{
    public interface IToolHandler
    {
        string ToolName { get; }
        string Description { get; set; }
        ToolExecutionLocation ExecutionLocation { get; }
        ToolCategory Category { get; }
        JsonElement ParameterSchema { get; }

        Task<ToolResult> ExecuteAsync(JsonElement parameters,
                                      ToolExecutionContext context,
                                      CancellationToken cancellationToken);
    }

    public class ToolResult
    {
        public bool Success { get; set; }
        public string Content { get; set; } = string.Empty;
        public string? ErrorMessage { get; set; }
    }

    public class ToolExecutionContext
    {
        public long SessionId { get; set; }
        public string UserId { get; set; } = string.Empty;
        public bool SpoilerFreeMode { get; set; }
        public bool IsGameOn { get; set; }
        public int OverseerMode { get; set; }
        public string DataDirectory { get; set; } = string.Empty;
    }
}
