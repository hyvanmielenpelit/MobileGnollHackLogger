using System.Text.Json;

namespace Overseer.Services.Tools
{
    public class ToolDefinition
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public JsonElement Parameters { get; set; }
        public ToolCategory Category { get; set; }
        public ToolExecutionLocation ExecutionLocation { get; set; }
        public bool RequiresConfirmation { get; set; }
        public int TimeoutSeconds { get; set; } = 10;
    }

    public enum ToolCategory
    {
        InformationRetrieval,
        ExternalLookup,
        SessionData,
        ClientActiveSessionQuery,
        ClientPersistentDataQuery,
        GameAction,
        SubAgent
    }

    public enum ToolExecutionLocation
    {
        Provider,
        Server,
        Client
    }
}
