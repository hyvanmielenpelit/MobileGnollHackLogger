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
                    ""query"": { ""type"": ""string"", ""description"": ""The search terms to look up in the wiki"" }
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

            var results = _wikiService.GetRelevantContext(query);
            var content = string.Join("\n\n", results);

            if (string.IsNullOrWhiteSpace(content))
            {
                return Task.FromResult(new ToolResult { Success = true, Content = "No relevant information found in the GnollHack wiki." });
            }

            return Task.FromResult(new ToolResult { Success = true, Content = content });
        }
    }
}
