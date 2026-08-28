using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Overseer.Services.Tools
{
    public abstract class ClientToolHandlerBase : IToolHandler
    {
        public abstract string ToolName { get; }
        public virtual string Description { get; set; } = "Client tool";
        public ToolExecutionLocation ExecutionLocation => ToolExecutionLocation.Client;
        public virtual ToolCategory Category => ToolCategory.ClientActiveSessionQuery;
        public virtual int TimeoutSeconds => 15;
        public virtual int? MaxResultLengthOverride => null;

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
        public override string Description { get; set; } = "Retrieve the full message history of the current game session from the client.";

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
        public override string Description { get; set; } = "Get a directory listing of files on the client device.";
        public override ToolCategory Category => ToolCategory.ClientPersistentDataQuery;
    }

    public class RefreshSnapshotTool : ClientToolHandlerBase
    {
        public override string ToolName => "refresh_snapshot";

        /* Overwritten at startup by ToolGuides/refresh_snapshot.md; this is the
           fallback used only if the guide file is missing. */
        public override string Description { get; set; } = "Request the client to take and upload a fresh snapshot of the game state.";

        /* Generating the snapshot on-device walks the whole game state and writes
           an HTML file before the round trip; 15s is not enough on a slow phone. */
        public override int TimeoutSeconds => 30;

        /* A snapshot truncated at an arbitrary character loses its tail sections -
           Discoveries and the dungeon overview - with no signal to the model that
           anything is missing. Match the client's own 60000-char cap
           (DefaultMaxSnapshotChars in OverseerPage.xaml.cs). */
        public override int? MaxResultLengthOverride => 60000;
    }

    public class GetSaveInfoTool : ClientToolHandlerBase
    {
        public override string ToolName => "get_save_info";
        public override string Description { get; set; } = "Retrieve information about a specific game save file on the client device.";
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
        public override string Description { get; set; } = "Read the contents of a discovered game manual from the player's library.";
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
        public override string Description { get; set; } = "Read the text of a received Oracle consultation.";
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
        public override string Description { get; set; } = "Retrieve the player's local xlog containing game statistics and past runs.";
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
        public override string Description { get; set; } = "Retrieve dumplogs of the player's past games from the client device.";
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
        public override string Description { get; set; } = "Retrieve the application log (applog) from the client device to diagnose issues.";
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
        public override string Description { get; set; } = "Retrieve the panic log from the client device to diagnose crashes.";
        public override ToolCategory Category => ToolCategory.ClientPersistentDataQuery;
    }
}
