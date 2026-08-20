using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace Overseer.Services.Tools
{
    public class GetArtifactStatsTool : IToolHandler
    {
        private readonly SourceCodeService _sourceCodeService;
        private readonly int _truncationThreshold;
        private readonly int _hardLimit;

        public GetArtifactStatsTool(SourceCodeService sourceCodeService, IConfiguration configuration)
        {
            _sourceCodeService = sourceCodeService;
            _truncationThreshold = configuration.GetValue<int>("Tools:get_artifact_stats:TruncationThreshold", 9900);
            _hardLimit = configuration.GetValue<int>("Tools:get_artifact_stats:HardLimit", 10000);
        }

        public string ToolName => "get_artifact_stats";
        
        public string Description { get; set; } = "Extract complete statistics and properties for a specific artifact from include/artilist.h.";

        public ToolExecutionLocation ExecutionLocation => ToolExecutionLocation.Server;
        public ToolCategory Category => ToolCategory.InformationRetrieval;
        
        private static readonly JsonElement _parameterSchema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "name": { 
                    "type": "string", 
                    "description": "The exact name of the artifact as defined in include/artilist.h (e.g. 'Excalibur', 'Mjollnir', 'The Staff of Aesculapius')" 
                }
            },
            "required": ["name"]
        }
        """).RootElement.Clone();

        public JsonElement ParameterSchema => _parameterSchema;

        public Task<ToolResult> ExecuteAsync(JsonElement arguments, ToolExecutionContext context, System.Threading.CancellationToken cancellationToken)
        {
            if (!_sourceCodeService.IsIndexingComplete)
            {
                return Task.FromResult(new ToolResult { Success = false, ErrorMessage = ToolGuardMessages.SourceCodeIndexingInProgress });
            }

            string name = arguments.GetProperty("name").GetString() ?? string.Empty;
            
            var result = _sourceCodeService.GetArtifactStats(name);
            
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            
            string jsonResult = JsonSerializer.Serialize(result, options);
            
            if (jsonResult.Length > _truncationThreshold)
            {
                if (result.Stats != null)
                {
                    /* Level 1: drop flag_descriptions to fit */
                    var minResult = new StatsResponse<ArtifactStats>
                    {
                        Stats = result.Stats,
                        Message = "Response truncated due to size limits. Flag descriptions omitted.",
                        Error = result.Error
                    };
                    jsonResult = JsonSerializer.Serialize(minResult, options);
                }
                else if (result.RawDefinition != null)
                {
                    var minResult = new StatsResponse<ArtifactStats>
                    {
                        Stats = result.Stats,
                        RawDefinition = result.RawDefinition,
                        Message = "Response truncated due to size limits. Macro definitions and flag descriptions omitted.",
                        Error = result.Error
                    };
                    jsonResult = JsonSerializer.Serialize(minResult, options);
                }
            }
            
            if (jsonResult.Length > _hardLimit)
            {
                return Task.FromResult(new ToolResult { Success = false, ErrorMessage = $"Result too large (over {_hardLimit} chars) even after minification. Try viewing the source code file directly." });
            }
            
            return Task.FromResult(new ToolResult { Success = true, Content = jsonResult });
        }
    }
}
