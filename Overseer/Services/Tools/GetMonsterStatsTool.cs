using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace Overseer.Services.Tools
{
    public class GetMonsterStatsTool : IToolHandler
    {
        private readonly SourceCodeService _sourceCodeService;
        private readonly IConfiguration _configuration;
        private readonly int _truncationThreshold;
        private readonly int _hardLimit;

        public GetMonsterStatsTool(SourceCodeService sourceCodeService, IConfiguration configuration)
        {
            _sourceCodeService = sourceCodeService;
            _configuration = configuration;
            _truncationThreshold = configuration.GetValue<int>("Tools:get_monster_stats:TruncationThreshold", 9900);
            _hardLimit = configuration.GetValue<int>("Tools:get_monster_stats:HardLimit", 10000);
        }

        public string ToolName => "get_monster_stats";
        
        public string Description { get; set; } = "Extract complete statistics and properties for a specific monster from src/monst.c.";

        public ToolExecutionLocation ExecutionLocation => ToolExecutionLocation.Server;
        public ToolCategory Category => ToolCategory.InformationRetrieval;
        
        private static readonly JsonElement _parameterSchema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "name": { 
                    "type": "string", 
                    "description": "The monster's name as defined in src/monst.c (e.g. 'goblin', 'adult red dragon'). A female or common name, such as 'kobold lady', also resolves."
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
                ? nameElement.GetString()?.Trim()
                : null;

            if (string.IsNullOrWhiteSpace(name))
            {
                return Task.FromResult(new ToolResult { Success = false, ErrorMessage = "Missing name parameter" });
            }
            
            var result = _sourceCodeService.GetMonsterStats(name);

            if (result.Error == null
                && context?.Variant?.HasExperiment(Benchmarking.BenchmarkExperimentalChangeCatalog.StatsReferencedByExperiment) == true)
            {
                result.ReferencedBy = SourceReferenceIndex.ForStatsResult(
                    _sourceCodeService, _configuration, MonsterConstantOf(result), SourceReferenceIndex.PmHeaderPath);
            }

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            string jsonResult = JsonSerializer.Serialize(result, options);

            if (jsonResult.Length > _truncationThreshold && result.ReferencedBy != null)
            {
                /* Level 0: drop referenced_by before anything else */
                result.ReferencedBy = null;
                jsonResult = JsonSerializer.Serialize(result, options);
            }

            if (jsonResult.Length > _truncationThreshold)
            {
                if (result.RawDefinition != null)
                {
                    /* Level 2: drop macros/structs/flags, keep raw definition */
                    var minResult = new StatsResponse<MonsterStats>
                    {
                        Stats = result.Stats,
                        RawDefinition = result.RawDefinition,
                        Message = "Response truncated due to size limits. Macro definitions and flag descriptions omitted.",
                        Error = result.Error
                    };
                    jsonResult = JsonSerializer.Serialize(minResult, options);
                }
                else if (result.Stats != null)
                {
                    /* Level 1: drop flag_descriptions to fit */
                    var minResult = new StatsResponse<MonsterStats>
                    {
                        Stats = result.Stats,
                        Message = "Response truncated due to size limits. Flag descriptions omitted.",
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

        /// <summary>
        /// The <c>PM_</c> constant of a monster result, from its parsed name and class; null for a raw
        /// definition of a were-creature, whose class decides the constant.
        /// </summary>
        private static string? MonsterConstantOf(StatsResponse<MonsterStats> result)
        {
            if (result.Stats != null)
            {
                result.Stats.Fields.TryGetValue("mname", out var mname);
                result.Stats.Fields.TryGetValue("mlet", out var mlet);
                return SourceReferenceIndex.MonsterConstant(mname as string, mlet as string);
            }

            string? rawName = SourceReferenceIndex.FirstStringLiteral(result.RawDefinition);
            if (rawName == null || rawName.StartsWith("were", StringComparison.Ordinal)) return null;
            return SourceReferenceIndex.MonsterConstant(rawName, null);
        }
    }
}
