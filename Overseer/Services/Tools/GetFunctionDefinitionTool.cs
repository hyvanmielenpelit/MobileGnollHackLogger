using System.Text.Json;
using System.Threading.Tasks;

namespace Overseer.Services.Tools
{
    public class GetFunctionDefinitionTool : IToolHandler
    {
        private readonly SourceCodeService _sourceCodeService;

        public GetFunctionDefinitionTool(SourceCodeService sourceCodeService)
        {
            _sourceCodeService = sourceCodeService;
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
                }
            },
            "required": ["name"]
        }
        """).RootElement.Clone();

        public JsonElement ParameterSchema => _parameterSchema;

        public Task<ToolResult> ExecuteAsync(JsonElement arguments, ToolExecutionContext context, System.Threading.CancellationToken cancellationToken)
        {
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

            var result = _sourceCodeService.GetFunctionBody(name, kind, startLine);
            return Task.FromResult(new ToolResult { Success = true, Content = result });
        }
    }
}
