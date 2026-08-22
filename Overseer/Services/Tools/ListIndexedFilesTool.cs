using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Overseer.Services.Tools
{
    public class ListIndexedFilesTool : IToolHandler
    {
        private readonly SourceCodeService _sourceCodeService;
        private readonly NetHackSourceCodeService _netHackService;

        public string ToolName => "list_indexed_files";
        public string Description { get; set; } = "List all indexed files in the GnollHack or NetHack repository. Use this to discover available files before searching.";
        public ToolExecutionLocation ExecutionLocation => ToolExecutionLocation.Server;
        public ToolCategory Category => ToolCategory.InformationRetrieval;

        public JsonElement ParameterSchema { get; }

        public ListIndexedFilesTool(SourceCodeService sourceCodeService, NetHackSourceCodeService netHackService)
        {
            _sourceCodeService = sourceCodeService;
            _netHackService = netHackService;

            ParameterSchema = JsonDocument.Parse(@"
            {
                ""type"": ""object"",
                ""properties"": {
                    ""path_filter"": { ""type"": ""string"", ""description"": ""Optional. Filter files by a substring in their path (case-insensitive, e.g., 'src', 'potion', '.h')"" },
                    ""repository"": {
                        ""type"": ""string"",
                        ""description"": ""Which codebase to list: 'gnollhack' (default) or 'nethack'"",
                        ""enum"": [""gnollhack"", ""nethack""]
                    }
                }
            }").RootElement;
        }

        private SourceCodeService ResolveService(JsonElement parameters, out string guardMessage)
        {
            if (parameters.TryGetProperty("repository", out var repo) &&
                repo.GetString()?.Equals("nethack", StringComparison.OrdinalIgnoreCase) == true)
            {
                guardMessage = ToolGuardMessages.NetHackSourceCodeIndexingInProgress;
                return _netHackService;
            }

            guardMessage = ToolGuardMessages.SourceCodeIndexingInProgress;
            return _sourceCodeService;
        }

        public Task<ToolResult> ExecuteAsync(JsonElement parameters, ToolExecutionContext context, CancellationToken cancellationToken)
        {
            var service = ResolveService(parameters, out var guardMessage);
            if (!service.IsIndexingComplete)
            {
                return Task.FromResult(new ToolResult { Success = false, ErrorMessage = guardMessage });
            }

            string pathFilter = "";
            if (parameters.TryGetProperty("path_filter", out var pathFilterElem))
            {
                pathFilter = pathFilterElem.GetString() ?? "";
            }

            bool includeNetCode = context.OverseerMode == 2 && !(service is NetHackSourceCodeService);

            var content = service.ListFiles(pathFilter, includeNetCode);

            if (string.IsNullOrWhiteSpace(content))
            {
                return Task.FromResult(new ToolResult { Success = true, Content = "No indexed files found matching the filter." });
            }

            return Task.FromResult(new ToolResult { Success = true, Content = content });
        }
    }
}
