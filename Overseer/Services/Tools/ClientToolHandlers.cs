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

        public JsonElement ParameterSchema { get; } = JsonDocument.Parse(@"{
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
