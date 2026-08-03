using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Overseer.Services.Tools
{
    public class SearchDefinitionsTool : IToolHandler
    {
        private readonly SourceCodeService _sourceCodeService;

        public string ToolName => "search_definitions";
        public string Description { get; set; } = "Find the definition of a specific C symbol (function, struct, macro, enum, typedef) in the GnollHack C core.";
        public ToolExecutionLocation ExecutionLocation => ToolExecutionLocation.Server;
        public ToolCategory Category => ToolCategory.InformationRetrieval;

        public JsonElement ParameterSchema { get; }

        public SearchDefinitionsTool(SourceCodeService sourceCodeService)
        {
            _sourceCodeService = sourceCodeService;
            ParameterSchema = JsonDocument.Parse(@"
            {
                ""type"": ""object"",
                ""properties"": {
                    ""symbol"": { ""type"": ""string"", ""description"": ""The name of the symbol to find"" },
                    ""kind"": { 
                        ""type"": ""string"", 
                        ""enum"": [""function"", ""struct"", ""macro"", ""enum"", ""type"", ""any""],
                        ""description"": ""The kind of symbol you are looking for (default 'any')"" 
                    }
                },
                ""required"": [""symbol""]
            }").RootElement;
        }

        public Task<ToolResult> ExecuteAsync(JsonElement parameters, ToolExecutionContext context, CancellationToken cancellationToken)
        {
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

            string result = _sourceCodeService.FindDefinition(symbol, kind);

            return Task.FromResult(new ToolResult { Success = true, Content = result });
        }
    }
}
