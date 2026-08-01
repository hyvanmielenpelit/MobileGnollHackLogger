using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Overseer.Services.Tools
{
    public class MonsterLookupTool : IToolHandler
    {
        private readonly WikiService _wikiService;

        public string ToolName => "monster_lookup";
        public string Description { get; set; } = "Look up a monster in the GnollHack database.";
        public ToolExecutionLocation ExecutionLocation => ToolExecutionLocation.Server;
        public ToolCategory Category => ToolCategory.InformationRetrieval;

        public JsonElement ParameterSchema { get; }

        public MonsterLookupTool(WikiService wikiService)
        {
            _wikiService = wikiService;
            ParameterSchema = JsonDocument.Parse(@"
            {
                ""type"": ""object"",
                ""properties"": {
                    ""name"": { ""type"": ""string"", ""description"": ""Monster name"" }
                },
                ""required"": [""name""]
            }").RootElement;
        }

        public Task<ToolResult> ExecuteAsync(JsonElement parameters, ToolExecutionContext context, CancellationToken cancellationToken)
        {
            string name = "";
            if (parameters.TryGetProperty("name", out var queryElem))
            {
                name = queryElem.GetString() ?? "";
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                return Task.FromResult(new ToolResult { Success = false, ErrorMessage = "Missing name parameter" });
            }

            // Using WikiService with 'monster' filter.
            var results = _wikiService.GetRelevantContext(name, "monster");
            var content = string.Join("\n\n", results);

            if (string.IsNullOrWhiteSpace(content))
            {
                // Fallback to searching without filter just in case
                results = _wikiService.GetRelevantContext(name);
                content = string.Join("\n\n", results);

                if (string.IsNullOrWhiteSpace(content))
                {
                    return Task.FromResult(new ToolResult { Success = true, Content = $"No information found for monster: {name}" });
                }
            }

            if (context.SpoilerFreeMode)
            {
                content = ApplySpoilerFreeMode(content);
            }

            return Task.FromResult(new ToolResult { Success = true, Content = content });
        }
        
        private string ApplySpoilerFreeMode(string content)
        {
            if (content.Length > 250)
            {
                var summary = content.Substring(0, 250);
                var nextNewline = content.IndexOf('\n', 250);
                if (nextNewline > 250 && nextNewline < 500)
                {
                    summary = content.Substring(0, nextNewline);
                }
                
                return summary + "\n\n[SPOILER FREE MODE: Detailed stats, resistances, and drop tables have been redacted. The player must discover these in-game.]";
            }
            return content;
        }
    }
}
