using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace Overseer.Services.Tools
{
    public class NetHackWikiSearchTool : IToolHandler
    {
        private readonly NetHackWikiService _netHackWikiService;
        private readonly int _configuredMaxResults;

        public string ToolName => "nethack_wiki_search";
        public string Description { get; set; } = "Search the local NetHack wiki database for mechanics, monsters, items, and features.";
        public ToolExecutionLocation ExecutionLocation => ToolExecutionLocation.Server;
        public ToolCategory Category => ToolCategory.InformationRetrieval;

        public JsonElement ParameterSchema { get; }

        public NetHackWikiSearchTool(NetHackWikiService netHackWikiService, IConfiguration configuration)
        {
            _netHackWikiService = netHackWikiService;
            _configuredMaxResults = configuration.GetValue<int>("Tools:nethack_wiki_search:MaxResults", 5);
            ParameterSchema = JsonDocument.Parse(@"
            {
                ""type"": ""object"",
                ""properties"": {
                    ""query"": {
                        ""type"": ""string"",
                        ""description"": ""The search terms to look up in the NetHack wiki""
                    },
                    ""namespace_filter"": {
                        ""type"": ""string"",
                        ""enum"": [""article"", ""source"", ""category"", ""forum"", ""help"", ""nethackwiki""],
                        ""description"": ""Optional. Filter results to a specific namespace. Default: search all namespaces.""
                    },
                    ""max_results"": {
                        ""type"": ""integer"",
                        ""description"": ""Optional. Maximum number of articles to return (default 3, max 5)""
                    }
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
            maxResults = Math.Clamp(maxResults, 1, Math.Max(1, _configuredMaxResults));

            string? namespaceFilter = null;
            if (parameters.TryGetProperty("namespace_filter", out var nsElem))
            {
                namespaceFilter = nsElem.GetString();
            }

            var results = _netHackWikiService.GetRelevantContext(query, namespaceFilter, maxResults);
            var content = string.Join("\n\n", results);

            if (string.IsNullOrWhiteSpace(content))
            {
                return Task.FromResult(new ToolResult { Success = true, Content = "No relevant information found in the NetHack wiki." });
            }

            if (context.SpoilerFreeMode)
            {
                content += "\n\n[SPOILER-FREE MODE ACTIVE: Review the spoiler_policy before sharing this information. Only share mechanics, not unrevealed content.]";
            }

            return Task.FromResult(new ToolResult { Success = true, Content = content });
        }
    }
}
