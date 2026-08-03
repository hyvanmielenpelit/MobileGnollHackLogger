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
        public virtual ToolCategory Category => ToolCategory.ClientActiveSessionQuery;

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
        public override ToolCategory Category => ToolCategory.ClientPersistentDataQuery;
    }

    public class RefreshSnapshotTool : ClientToolHandlerBase
    {
        public override string ToolName => "refresh_snapshot";
    }

    public class GetSaveInfoTool : ClientToolHandlerBase
    {
        public override string ToolName => "get_save_info";
        public override ToolCategory Category => ToolCategory.ClientPersistentDataQuery;

        public override JsonElement ParameterSchema { get; } = JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""filename"": { ""type"": ""string"", ""description"": ""Full path to the save file"" }
            },
            ""required"": [""filename""]
        }").RootElement;
    }

    public class GetPlayerLibraryTool : ClientToolHandlerBase
    {
        public override string ToolName => "get_player_library";
        public override ToolCategory Category => ToolCategory.ClientPersistentDataQuery;

        public override JsonElement ParameterSchema { get; } = JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""item_id"": { ""type"": ""integer"", ""description"": ""ID of a specific manual to read in full. If omitted, returns a list of all discovered manuals with just their names and IDs (no text content)."" }
            }
        }").RootElement;
    }

    public class GetOracleConsultationsTool : ClientToolHandlerBase
    {
        public override string ToolName => "get_oracle_consultations";
        public override ToolCategory Category => ToolCategory.ClientPersistentDataQuery;

        public override JsonElement ParameterSchema { get; } = JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""item_id"": { ""type"": ""integer"", ""description"": ""ID of a specific consultation to read in full. If omitted, returns a list of all received consultations with just their names and IDs (no text content)."" }
            }
        }").RootElement;
    }

    public class GetPlayerXlogTool : ClientToolHandlerBase
    {
        public override string ToolName => "get_player_xlog";
        public override ToolCategory Category => ToolCategory.ClientPersistentDataQuery;

        public override JsonElement ParameterSchema { get; } = JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""limit"": { ""type"": ""integer"", ""description"": ""Maximum number of entries to return. Defaults to 50."" },
                ""offset"": { ""type"": ""integer"", ""description"": ""Number of newest entries to skip. Defaults to 0."" }
            }
        }").RootElement;
    }

    public class GetPlayerDumplogsTool : ClientToolHandlerBase
    {
        public override string ToolName => "get_player_dumplogs";
        public override ToolCategory Category => ToolCategory.ClientPersistentDataQuery;

        public override JsonElement ParameterSchema { get; } = JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""filename"": { ""type"": ""string"", ""description"": ""Filename of a specific dumplog to read (e.g. gnollhack.Gandalf.20260801100000.txt). Get filenames from the list mode or from get_player_xlog's dumplog_filename field. If omitted, returns a list of all existing dumplog files on the device."" },
                ""max_length"": { ""type"": ""integer"", ""description"": ""Maximum number of characters to return from the dumplog content. Defaults to 4000. Use a higher value (e.g. 16000) to get more complete dumplogs when needed."" }
            }
        }").RootElement;
    }

    public class GetAppLogTool : ClientToolHandlerBase
    {
        public override string ToolName => "get_app_log";
        public override ToolCategory Category => ToolCategory.ClientPersistentDataQuery;

        public override JsonElement ParameterSchema { get; } = JsonDocument.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""last_n"": { ""type"": ""integer"", ""description"": ""Return only the last N lines of the log. If omitted, returns the entire log (subject to server-side truncation)."" },
                ""search_term"": { ""type"": ""string"", ""description"": ""Optional substring to filter log lines (case-insensitive). Only lines containing this term are returned."" }
            }
        }").RootElement;
    }

    public class GetPanicLogTool : ClientToolHandlerBase
    {
        public override string ToolName => "get_panic_log";
        public override ToolCategory Category => ToolCategory.ClientPersistentDataQuery;
    }
}
