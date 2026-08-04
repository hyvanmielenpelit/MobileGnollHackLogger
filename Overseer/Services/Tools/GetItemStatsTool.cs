using System.Text.Json;
using System.Threading.Tasks;

namespace Overseer.Services.Tools
{
    public class GetItemStatsTool : IToolHandler
    {
        private readonly SourceCodeService _sourceCodeService;

        public GetItemStatsTool(SourceCodeService sourceCodeService)
        {
            _sourceCodeService = sourceCodeService;
        }

        public string ToolName => "get_item_stats";
        
        public string Description { get; set; } = "Extract complete statistics and properties for a specific item from src/objects.c.";

        public ToolExecutionLocation ExecutionLocation => ToolExecutionLocation.Server;
        public ToolCategory Category => ToolCategory.InformationRetrieval;
        
        private static readonly JsonElement _parameterSchema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "name": { 
                    "type": "string", 
                    "description": "The exact name of the item as defined in src/objects.c (e.g. 'long sword', 'potion of healing')" 
                }
            },
            "required": ["name"]
        }
        """).RootElement.Clone();

        public JsonElement ParameterSchema => _parameterSchema;

        public Task<ToolResult> ExecuteAsync(JsonElement arguments, ToolExecutionContext context, System.Threading.CancellationToken cancellationToken)
        {
            string name = arguments.GetProperty("name").GetString() ?? string.Empty;
            
            var result = _sourceCodeService.GetItemStats(name);
            
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            
            string jsonResult = JsonSerializer.Serialize(result, options);
            
            if (jsonResult.Length > 9900)
            {
                if (result.RawDefinition != null)
                {
                    var minResult = new StatsResponse<ItemStats>
                    {
                        Stats = result.Stats,
                        RawDefinition = result.RawDefinition,
                        Message = "Response truncated due to size limits. Macro definitions and flag descriptions omitted.",
                        Error = result.Error
                    };
                    jsonResult = JsonSerializer.Serialize(minResult, options);
                }
            }
            
            if (jsonResult.Length > 10000)
            {
                return Task.FromResult(new ToolResult { Success = false, ErrorMessage = "Result too large (over 10000 chars) even after minification. Try viewing the source code file directly." });
            }
            
            return Task.FromResult(new ToolResult { Success = true, Content = jsonResult });
        }
    }
}
