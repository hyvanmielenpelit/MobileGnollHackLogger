using System.Text.Json;
using System.Threading.Tasks;

namespace Overseer.Services.Tools
{
    public class GetFunctionDefinitionTool : IToolHandler
    {
        private readonly SourceCodeService _sourceCodeService;
        private readonly NetHackSourceCodeService _netHackService;

        public GetFunctionDefinitionTool(SourceCodeService sourceCodeService, NetHackSourceCodeService netHackService)
        {
            _sourceCodeService = sourceCodeService;
            _netHackService = netHackService;
        }

        public string ToolName => "get_function_definition";
        
        public string Description { get; set; } = "Extract the complete body of a C function, macro, or struct from the source code. Returns the entire body by tracking braces.";

        public ToolExecutionLocation ExecutionLocation => ToolExecutionLocation.Server;
        public ToolCategory Category => ToolCategory.InformationRetrieval;
        
        private static readonly JsonElement _parameterSchema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "name": { 
                    "type": "string", 
                    "description": "Function, macro, or struct name" 
                },
                "type": { 
                    "type": "string", 
                    "enum": ["function", "macro", "struct", "any"], 
                    "description": "Optional (default 'any')" 
                },
                "start_line": { 
                    "type": "integer", 
                    "description": "Optional. Start from this line within the function body (for continuing after truncation)" 
                },
                "repository": {
                    "type": "string",
                    "description": "Which codebase to extract from: 'gnollhack' (default) or 'nethack'",
                    "enum": ["gnollhack", "nethack"]
                }
            },
            "required": ["name"]
        }
        """).RootElement.Clone();

        public JsonElement ParameterSchema => _parameterSchema;

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

        public Task<ToolResult> ExecuteAsync(JsonElement arguments, ToolExecutionContext context, System.Threading.CancellationToken cancellationToken)
        {
            var service = ResolveService(arguments, out var guardMessage);
            if (!service.IsIndexingComplete)
            {
                return Task.FromResult(new ToolResult { Success = false, ErrorMessage = guardMessage });
            }

            string name = arguments.GetProperty("name").GetString() ?? string.Empty;
            string kind = "any";
            
            if (arguments.TryGetProperty("type", out var typeElement) && typeElement.ValueKind == JsonValueKind.String)
            {
                kind = typeElement.GetString() ?? "any";
            }
            
            int? startLine = null;
            if (arguments.TryGetProperty("start_line", out var startLineElement) && startLineElement.ValueKind == JsonValueKind.Number)
            {
                startLine = startLineElement.GetInt32();
            }

            var result = service.GetFunctionBody(name, kind, startLine);
            return Task.FromResult(new ToolResult { Success = true, Content = result });
        }
    }
}
