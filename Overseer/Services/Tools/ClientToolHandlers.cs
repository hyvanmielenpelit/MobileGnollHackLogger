using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Overseer.Services.Tools
{
    public abstract class ClientToolHandlerBase : IToolHandler
    {
        public abstract string ToolName { get; }
        public string Description { get; set; } = "Client tool";
        public ToolExecutionLocation ExecutionLocation => ToolExecutionLocation.Client;
        public virtual ToolCategory Category => ToolCategory.ClientStateQuery;

        public virtual JsonElement ParameterSchema { get; } = JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {}
        }").RootElement;

        public Task<ToolResult> ExecuteAsync(JsonElement parameters, ToolExecutionContext context, CancellationToken cancellationToken)
        {
            throw new NotSupportedException("Client tools must be executed via the IClientToolBridge.");
        }
    }

    public class GetFullMessageHistoryTool : ClientToolHandlerBase
    {
        public override string ToolName => "get_full_message_history";

        public override JsonElement ParameterSchema { get; } = JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""search_term"": { ""type"": ""string"", ""description"": ""Optional substring to filter messages"" },
                ""last_n"": { ""type"": ""integer"", ""description"": ""Limit to the last N messages. Max 16384."" }
            }
        }").RootElement;
    }

    public class GetDirectoryListingTool : ClientToolHandlerBase
    {
        public override string ToolName => "get_directory_listing";
    }

    public class RefreshSnapshotTool : ClientToolHandlerBase
    {
        public override string ToolName => "refresh_snapshot";
    }

    public class GetSaveInfoTool : ClientToolHandlerBase
    {
        public override string ToolName => "get_save_info";
    }
}
