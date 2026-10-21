using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Overseer.Services
{
    public class StatsResponse<T>
    {
        [JsonPropertyName("stats")]
        public T? Stats { get; set; }
        
        [JsonPropertyName("raw_definition")]
        public string? RawDefinition { get; set; }
        
        [JsonPropertyName("flag_descriptions")]
        public Dictionary<string, string> FlagDescriptions { get; set; } = new();
        
        [JsonPropertyName("macro_definitions")]
        public Dictionary<string, string> MacroDefinitions { get; set; } = new();
        
        [JsonPropertyName("struct_definitions")]
        public Dictionary<string, string> StructDefinitions { get; set; } = new();
        
        [JsonPropertyName("error")]
        public string? Error { get; set; }
        
        [JsonPropertyName("message")]
        public string? Message { get; set; }

        /// <summary>
        /// The functions referencing the entry's constant, from <see cref="SourceReferenceIndex"/>;
        /// set only under a benchmark variant's <c>stats_referenced_by</c> experiment.
        /// </summary>
        [JsonPropertyName("referenced_by")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? ReferencedBy { get; set; }
    }

    public class MonsterStats
    {
        [JsonExtensionData]
        public Dictionary<string, object> Fields { get; set; } = new();
    }

    public class ItemStats
    {
        [JsonExtensionData]
        public Dictionary<string, object> Fields { get; set; } = new();
    }

    public class ArtifactStats
    {
        [JsonExtensionData]
        public Dictionary<string, object> Fields { get; set; } = new();
    }
}
