using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Overseer.Services.Tools
{
    public class SearchDefinitionsTool : IToolHandler
    {
        private readonly SourceCodeService _sourceCodeService;
        private readonly NetHackSourceCodeService _netHackService;

        public string ToolName => "search_definitions";
        public string Description { get; set; } = "Find the definition of a specific C symbol (function, struct, macro, enum, typedef) in the GnollHack or NetHack C core.";
        public ToolExecutionLocation ExecutionLocation => ToolExecutionLocation.Server;
        public ToolCategory Category => ToolCategory.InformationRetrieval;

        public JsonElement ParameterSchema { get; }

        public SearchDefinitionsTool(SourceCodeService sourceCodeService, NetHackSourceCodeService netHackService)
        {
            _sourceCodeService = sourceCodeService;
            _netHackService = netHackService;
            ParameterSchema = JsonDocument.Parse(@"
            {
                ""type"": ""object"",
                ""properties"": {
                    ""symbol"": { ""type"": ""string"", ""description"": ""The name of the symbol to find"" },
                    ""kind"": { 
                        ""type"": ""string"", 
                        ""enum"": [""function"", ""struct"", ""macro"", ""enum"", ""type"", ""any""],
                        ""description"": ""The kind of symbol you are looking for (default 'any')"" 
                    },
                    ""repository"": {
                        ""type"": ""string"",
                        ""description"": ""Which codebase to search: 'gnollhack' (default) or 'nethack'"",
                        ""enum"": [""gnollhack"", ""nethack""]
                    }
                },
                ""required"": [""symbol""]
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

            string symbol = "";
            if (parameters.TryGetProperty("symbol", out var symbolElem))
            {
                symbol = symbolElem.GetString() ?? "";
            }

            if (string.IsNullOrWhiteSpace(symbol))
            {
                return Task.FromResult(new ToolResult { Success = false, ErrorMessage = "Missing symbol parameter" });
            }

            string kind = "any";
            if (parameters.TryGetProperty("kind", out var kindElem))
            {
                kind = kindElem.GetString() ?? "any";
            }

            string result = service.FindDefinition(symbol, kind);

            return Task.FromResult(new ToolResult { Success = true, Content = result });
        }
    }
}
