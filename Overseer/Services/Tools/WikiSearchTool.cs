using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Overseer.Services.Tools
{
    public class WikiSearchTool : IToolHandler
    {
        private readonly WikiService _wikiService;

        public string ToolName => "wiki_search";
        public string Description { get; set; } = "Search the GnollHack specific wiki for information.";
        public ToolExecutionLocation ExecutionLocation => ToolExecutionLocation.Server;
        public ToolCategory Category => ToolCategory.InformationRetrieval;

        public JsonElement ParameterSchema { get; }

        public WikiSearchTool(WikiService wikiService)
        {
            _wikiService = wikiService;
            ParameterSchema = JsonDocument.Parse(@"
            {
                ""type"": ""object"",
                ""properties"": {
                    ""query"": { ""type"": ""string"", ""description"": ""The search terms to look up in the wiki"" },
                    ""category"": { ""type"": ""string"", ""description"": ""Optional. Filter by category (e.g., 'monster', 'item', 'spell', 'class')"" },
                    ""max_results"": { ""type"": ""integer"", ""description"": ""Maximum number of wiki articles to return (default 3)"" }
                },
                ""required"": [""query""]
            }").RootElement;
        }

        public Task<ToolResult> ExecuteAsync(JsonElement parameters, ToolExecutionContext context, CancellationToken cancellationToken)
        {
            string query = "";
            if (parameters.TryGetProperty("query", out var queryElem))
            {
                query = queryElem.GetString() ?? "";
            }

            if (string.IsNullOrWhiteSpace(query))
            {
                return Task.FromResult(new ToolResult { Success = false, ErrorMessage = "Missing query parameter" });
            }

            int maxResults = 3;
            if (parameters.TryGetProperty("max_results", out var maxResElem) && maxResElem.ValueKind == JsonValueKind.Number)
            {
                maxResults = maxResElem.GetInt32();
            }

            string? category = null;
            if (parameters.TryGetProperty("category", out var categoryElem))
            {
                category = categoryElem.GetString();
            }

            var results = _wikiService.GetRelevantContext(query, category, maxResults);
            var content = string.Join("\n\n", results);

            if (string.IsNullOrWhiteSpace(content))
            {
                return Task.FromResult(new ToolResult { Success = true, Content = "No relevant information found in the GnollHack wiki." });
            }

            if (context.SpoilerFreeMode)
            {
                content = ApplySpoilerFreeMode(content);
            }

            return Task.FromResult(new ToolResult { Success = true, Content = content });
        }
        
        private string ApplySpoilerFreeMode(string content)
        {
            // In spoiler-free mode, return full content but add a reminder.
            // The LLM's spoiler policy (from spoiler_policy.md) handles what to share.
            return content + "\n\n[SPOILER-FREE MODE ACTIVE: Review the spoiler_policy before sharing this information. Only share mechanics, not unrevealed content.]";
        }
    }
}
