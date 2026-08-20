using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Overseer.Services.Tools
{
    public class ListIndexedFilesTool : IToolHandler
    {
        private readonly SourceCodeService _sourceCodeService;

        public string ToolName => "list_indexed_files";
        public string Description { get; set; } = "List all indexed files in the GnollHack repository. Use this to discover available files before searching.";
        public ToolExecutionLocation ExecutionLocation => ToolExecutionLocation.Server;
        public ToolCategory Category => ToolCategory.InformationRetrieval;

        public JsonElement ParameterSchema { get; }

        public ListIndexedFilesTool(SourceCodeService sourceCodeService)
        {
            _sourceCodeService = sourceCodeService;

            ParameterSchema = JsonDocument.Parse(@"
            {
                ""type"": ""object"",
                ""properties"": {
                    ""path_filter"": { ""type"": ""string"", ""description"": ""Optional. Filter files by a substring in their path (case-insensitive, e.g., 'src', 'potion', '.h')"" }
                }
            }").RootElement;
        }

        public Task<ToolResult> ExecuteAsync(JsonElement parameters, ToolExecutionContext context, CancellationToken cancellationToken)
        {
            if (!_sourceCodeService.IsIndexingComplete)
            {
                return Task.FromResult(new ToolResult { Success = false, ErrorMessage = ToolGuardMessages.SourceCodeIndexingInProgress });
            }

            string pathFilter = "";
            if (parameters.TryGetProperty("path_filter", out var pathFilterElem))
            {
                pathFilter = pathFilterElem.GetString() ?? "";
            }

            bool includeNetCode = context.OverseerMode == 2;

            var content = _sourceCodeService.ListFiles(pathFilter, includeNetCode);

            if (string.IsNullOrWhiteSpace(content))
            {
                return Task.FromResult(new ToolResult { Success = true, Content = "No indexed files found matching the filter." });
            }

            return Task.FromResult(new ToolResult { Success = true, Content = content });
        }
    }
}
