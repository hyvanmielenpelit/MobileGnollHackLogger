using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace Overseer.Services.Tools
{
    public class GetItemStatsTool : IToolHandler
    {
        private readonly SourceCodeService _sourceCodeService;
        private readonly int _truncationThreshold;
        private readonly int _hardLimit;

        public GetItemStatsTool(SourceCodeService sourceCodeService, IConfiguration configuration)
        {
            _sourceCodeService = sourceCodeService;
            _truncationThreshold = configuration.GetValue<int>("Tools:get_item_stats:TruncationThreshold", 9900);
            _hardLimit = configuration.GetValue<int>("Tools:get_item_stats:HardLimit", 10000);
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
                    "description": "The exact name of the item as defined in src/objects.c. Use the bare oc_name, not the display name: 'digging', not 'wand of digging'." 
                },
                "object_class": {
                    "type": "string",
                    "description": "Optional. Selects among object classes when several hold an entry of the same name, e.g. 'WAND_CLASS' for the wand rather than the scroll. Omit unless a previous result reported ambiguous_object_classes."
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

            string? name = arguments.TryGetProperty("name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String
                ? nameElement.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(name))
            {
                return Task.FromResult(new ToolResult { Success = false, ErrorMessage = "Missing name parameter" });
            }

            string? objectClass = arguments.TryGetProperty("object_class", out var objectClassElem)
                ? objectClassElem.GetString()
                : null;

            var result = _sourceCodeService.GetItemStats(name, objectClass);
            
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            
            string jsonResult = JsonSerializer.Serialize(result, options);

            if (jsonResult.Length > _truncationThreshold)
            {
                if (result.RawDefinition != null)
                {
                    var minResult = Minify(result, options, _truncationThreshold);
                    jsonResult = JsonSerializer.Serialize(minResult, options);
                }
            }

            if (jsonResult.Length > _hardLimit)
            {
                return Task.FromResult(new ToolResult { Success = false, ErrorMessage = $"Result too large (over {_hardLimit} chars) even after minification. Try viewing the source code file directly." });
            }

            return Task.FromResult(new ToolResult { Success = true, Content = jsonResult });
        }

        /// <summary>The phrase in a Level 2 <see cref="StatsResponse{T}.Message"/> that opens the resolver's failure reason.</summary>
        private const string FailureReasonPhrase = "Structured values were not available:";

        /// <summary>
        /// The minified form of a truncated <see cref="StatsResponse{ItemStats}"/>: the fixed
        /// truncation sentence, extended with the original message's failure reason when it carries
        /// one, and the raw definition's own invoked macro kept alone — dropped instead when it
        /// would not fit under <paramref name="threshold"/>. Stats, RawDefinition and Error pass
        /// through unchanged; every other field keeps its default.
        /// </summary>
        internal static StatsResponse<ItemStats> Minify(StatsResponse<ItemStats> result, JsonSerializerOptions options, int threshold)
        {
            string message = "Response truncated due to size limits. Macro definitions and flag descriptions omitted.";
            int failureIndex = result.Message?.IndexOf(FailureReasonPhrase, StringComparison.Ordinal) ?? -1;
            if (failureIndex >= 0)
            {
                message += " " + result.Message!.Substring(failureIndex);
            }

            var minResult = new StatsResponse<ItemStats>
            {
                Stats = result.Stats,
                RawDefinition = result.RawDefinition,
                Message = message,
                Error = result.Error
            };

            string? invokedMacro = InvokedMacro(result.RawDefinition);
            if (invokedMacro != null && result.MacroDefinitions.TryGetValue(invokedMacro, out var macroDefinition))
            {
                minResult.MacroDefinitions = new Dictionary<string, string> { { invokedMacro, macroDefinition } };
                if (JsonSerializer.Serialize(minResult, options).Length > threshold)
                {
                    minResult.MacroDefinitions = new Dictionary<string, string>();
                }
            }

            return minResult;
        }

        /// <summary>The identifier before the raw definition's first '(', e.g. "SPELL" from "SPELL(...)".</summary>
        private static string? InvokedMacro(string? rawDefinition)
        {
            if (string.IsNullOrEmpty(rawDefinition)) return null;

            string trimmed = rawDefinition.TrimStart();
            int parenIndex = trimmed.IndexOf('(');
            return parenIndex >= 0 ? trimmed.Substring(0, parenIndex).Trim() : null;
        }
    }
}
