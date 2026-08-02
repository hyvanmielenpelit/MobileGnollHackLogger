using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace Overseer.Services.Tools
{
    public class SourceCodeSearchTool : IToolHandler
    {
        private readonly SourceCodeService _sourceCodeService;
        private readonly int _maxResultLength;

        public string ToolName => "source_code_search";
        public string Description { get; set; } = "Search the GnollHack C source code for functions, macros, constants, or game mechanic implementations.";
        public ToolExecutionLocation ExecutionLocation => ToolExecutionLocation.Server;
        public ToolCategory Category => ToolCategory.InformationRetrieval;

        public JsonElement ParameterSchema { get; }

        public SourceCodeSearchTool(SourceCodeService sourceCodeService, IConfiguration configuration)
        {
            _sourceCodeService = sourceCodeService;
            
            if (!int.TryParse(configuration["MaxSourceResultLength"], out _maxResultLength))
            {
                _maxResultLength = 3000;
            }

            ParameterSchema = JsonDocument.Parse(@"
            {
                ""type"": ""object"",
                ""properties"": {
                    ""query"": { ""type"": ""string"", ""description"": ""The search terms to look up in the source code"" },
                    ""file_filter"": { ""type"": ""string"", ""description"": ""Optional. Restrict to a specific file (e.g., 'potion.c')"" },
                    ""max_results"": { ""type"": ""integer"", ""description"": ""Maximum number of files to return matches from (default 5)"" }
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

            string fileFilter = "";
            if (parameters.TryGetProperty("file_filter", out var fileFilterElem))
            {
                fileFilter = fileFilterElem.GetString() ?? "";
            }

            int maxResults = 5;
            if (parameters.TryGetProperty("max_results", out var maxResElem) && maxResElem.ValueKind == JsonValueKind.Number)
            {
                maxResults = maxResElem.GetInt32();
            }

            bool includeNetCode = context.OverseerMode == 2;

            var content = _sourceCodeService.SearchFiles(query, fileFilter, maxResults, includeNetCode, _maxResultLength);

            if (string.IsNullOrWhiteSpace(content))
            {
                return Task.FromResult(new ToolResult { Success = true, Content = "No relevant source code found." });
            }

            if (context.SpoilerFreeMode)
            {
                content += "\n\n[SPOILER-FREE MODE ACTIVE: Review the spoiler_policy before sharing this information. Only share mechanics, not unrevealed content.]";
            }

            return Task.FromResult(new ToolResult { Success = true, Content = content });
        }
    }
}
