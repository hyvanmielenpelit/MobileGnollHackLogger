using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Overseer.Services.Tools
{
    /// <summary>
    /// <c>get_character_info</c>: a GnollHack race or role from <see cref="CharacterTables"/> — which
    /// roles, races and alignments it allows, its intrinsics by experience level and, for a race,
    /// the races it is always peaceful or always hostile towards. Experimental: offered only to a
    /// benchmark request whose experimental changes offer it.
    /// </summary>
    public class GetCharacterInfoTool : IToolHandler
    {
        /// <summary>The longest result the tool returns, in characters.</summary>
        public const int MaxResultChars = 4000;

        public const string AttitudeNote =
            "Monster flags come before race attitudes: a monster that is always peaceful or always hostile, a quest leader or guardian (peaceful) and a quest nemesis (hostile) keep that whatever the player's race; read get_monster_stats for a monster's flags.";

        private readonly SourceCodeService _sourceCodeService;

        public GetCharacterInfoTool(SourceCodeService sourceCodeService)
        {
            _sourceCodeService = sourceCodeService;
        }

        public string ToolName => "get_character_info";

        public string Description { get; set; } =
            "Look up a GnollHack player race or role from src/role.c, src/attrib.c and src/prop.c: the roles, races and alignments it allows, the intrinsic abilities it gains by experience level, and for a race the races it is always peaceful or always hostile towards. Give race, role or both.";

        public ToolExecutionLocation ExecutionLocation => ToolExecutionLocation.Server;
        public ToolCategory Category => ToolCategory.InformationRetrieval;
        public bool IsExperimental => true;

        private static readonly JsonElement _parameterSchema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "race": {
                    "type": "string",
                    "description": "A player race by its noun, adjective or plural, e.g. 'gnoll', 'gnollish', 'elves'."
                },
                "role": {
                    "type": "string",
                    "description": "A player role by its male or female name or plural, e.g. 'Healer', 'Cavewoman', 'priests'."
                }
            }
        }
        """).RootElement.Clone();

        public JsonElement ParameterSchema => _parameterSchema;

        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public Task<ToolResult> ExecuteAsync(JsonElement arguments, ToolExecutionContext context, System.Threading.CancellationToken cancellationToken)
        {
            if (!_sourceCodeService.IsIndexingComplete)
            {
                return Task.FromResult(new ToolResult { Success = false, ErrorMessage = ToolGuardMessages.SourceCodeIndexingInProgress });
            }

            string? race = StringArgument(arguments, "race");
            string? role = StringArgument(arguments, "role");
            if (string.IsNullOrWhiteSpace(race) && string.IsNullOrWhiteSpace(role))
            {
                return Task.FromResult(new ToolResult { Success = false, ErrorMessage = "Missing race or role parameter" });
            }

            var response = BuildResponse(_sourceCodeService.GetCharacterTables(), race, role);
            return Task.FromResult(new ToolResult { Success = true, Content = Serialize(response) });
        }

        private static string? StringArgument(JsonElement arguments, string name)
            => arguments.ValueKind == JsonValueKind.Object
               && arguments.TryGetProperty(name, out var element)
               && element.ValueKind == JsonValueKind.String
                ? element.GetString()?.Trim()
                : null;

        internal static CharacterInfoResponse BuildResponse(CharacterTables? tables, string? raceQuery, string? roleQuery)
        {
            if (tables == null || (tables.Roles.Count == 0 && tables.Races.Count == 0))
            {
                string reason = tables == null || tables.Problems.Count == 0
                    ? "src/role.c has not been parsed."
                    : string.Join(" ", tables.Problems);
                return new CharacterInfoResponse
                {
                    Error = Cap($"The character tables are not available: {reason} Read src/role.c and src/attrib.c with source_code_view instead.", 1000)
                };
            }

            var response = new CharacterInfoResponse();
            var misses = new List<string>();

            if (!string.IsNullOrWhiteSpace(raceQuery))
            {
                var race = tables.FindRace(raceQuery);
                if (race == null) misses.Add($"No race named '{raceQuery}'.");
                else response.Race = RaceInfo(tables, race);
            }

            if (!string.IsNullOrWhiteSpace(roleQuery))
            {
                var role = tables.FindRole(roleQuery);
                if (role == null) misses.Add($"No role named '{roleQuery}'.");
                else response.Role = RoleInfo(tables, role);
            }

            if (misses.Count > 0)
            {
                string races = string.Join(", ", tables.Races.Select(r => r.Noun));
                string roles = string.Join(", ", tables.Roles.Select(r => r.FemaleName == null ? r.Name : r.Name + "/" + r.FemaleName));
                return new CharacterInfoResponse
                {
                    Error = Cap($"{string.Join(" ", misses)} Valid races: {races}. Valid roles: {roles}.", 1500)
                };
            }

            response.Note = AttitudeNote;
            return response;
        }

        private static CharacterRaceInfo RaceInfo(CharacterTables tables, CharacterRace race)
        {
            if (race.ParseError != null)
            {
                return new CharacterRaceInfo
                {
                    Name = race.Noun,
                    RawDefinition = race.RawDefinition,
                    Message = $"Could not parse the races[] entry for '{race.Noun}' in src/role.c: {race.ParseError} The raw initializer is given for manual interpretation (struct Race in include/you.h)."
                };
            }

            return new CharacterRaceInfo
            {
                Name = race.Noun,
                AllowedRoles = tables.RolesAllowing(race).Select(r => r.Name).ToList(),
                AllowedAlignments = CharacterTables.Alignments(race.Allow).ToList(),
                AlwaysPeacefulRaces = tables.RaceNouns(race.LoveMask).ToList(),
                AlwaysHostileRaces = tables.RaceNouns(race.HateMask).ToList(),
                AbilitiesByLevel = Abilities(race.Abilities),
                Traits = race.Traits.Count > 0 ? race.Traits.ToList() : null,
                Message = AbilitiesMessage(tables)
            };
        }

        private static CharacterRoleInfo RoleInfo(CharacterTables tables, CharacterRole role)
        {
            if (role.ParseError != null)
            {
                return new CharacterRoleInfo
                {
                    Name = role.Name,
                    RawDefinition = role.RawDefinition,
                    Message = $"Could not parse the roles[] entry for '{role.Name}' in src/role.c: {role.ParseError} The raw initializer is given for manual interpretation (struct Role in include/you.h)."
                };
            }

            return new CharacterRoleInfo
            {
                Name = role.Name,
                AllowedRaces = tables.RacesAllowedBy(role).Select(r => r.Noun).ToList(),
                AllowedAlignments = CharacterTables.Alignments(role.Allow).ToList(),
                QuestArtifact = role.QuestArtifactName ?? role.QuestArtifactConstant,
                AbilitiesByLevel = Abilities(role.Abilities),
                Traits = role.Traits.Count > 0 ? role.Traits.ToList() : null,
                Message = AbilitiesMessage(tables)
            };
        }

        private static List<CharacterAbilityInfo> Abilities(IReadOnlyList<CharacterAbility> abilities)
            => abilities.Select(a => new CharacterAbilityInfo { Level = a.Level, Ability = a.Name }).ToList();

        /// <summary>The problems that leave abilities missing or unnamed, when there are any.</summary>
        private static string? AbilitiesMessage(CharacterTables tables)
        {
            var relevant = tables.Problems
                .Where(p => p.StartsWith("src/attrib.c", System.StringComparison.Ordinal) || p.StartsWith("src/prop.c", System.StringComparison.Ordinal))
                .ToList();
            return relevant.Count == 0 ? null : Cap(string.Join(" ", relevant), 500);
        }

        private static string Cap(string text, int max) => text.Length > max ? text.Substring(0, max) : text;

        /// <summary>The response as JSON under <see cref="MaxResultChars"/>: raw definitions, then traits, give way first.</summary>
        internal static string Serialize(CharacterInfoResponse response)
        {
            string json = JsonSerializer.Serialize(response, SerializerOptions);
            if (json.Length <= MaxResultChars) return json;

            foreach (var info in new CharacterEntryInfo?[] { response.Race, response.Role })
            {
                if (info?.RawDefinition == null) continue;
                info.RawDefinition = null;
                info.Message = (info.Message + " The raw definition was omitted to fit the result size.").Trim();
            }
            json = JsonSerializer.Serialize(response, SerializerOptions);
            if (json.Length <= MaxResultChars) return json;

            foreach (var info in new CharacterEntryInfo?[] { response.Race, response.Role })
            {
                if (info != null) info.Traits = null;
            }
            json = JsonSerializer.Serialize(response, SerializerOptions);
            return json.Length <= MaxResultChars ? json : json.Substring(0, MaxResultChars);
        }

        internal sealed class CharacterInfoResponse
        {
            [JsonPropertyName("race")]
            public CharacterRaceInfo? Race { get; set; }

            [JsonPropertyName("role")]
            public CharacterRoleInfo? Role { get; set; }

            [JsonPropertyName("error")]
            public string? Error { get; set; }

            [JsonPropertyName("note")]
            public string? Note { get; set; }
        }

        internal abstract class CharacterEntryInfo
        {
            [JsonPropertyName("name")]
            [JsonPropertyOrder(-10)]
            public string Name { get; set; } = string.Empty;

            [JsonPropertyName("abilities_by_level")]
            [JsonPropertyOrder(10)]
            public List<CharacterAbilityInfo>? AbilitiesByLevel { get; set; }

            [JsonPropertyName("traits")]
            [JsonPropertyOrder(11)]
            public List<string>? Traits { get; set; }

            [JsonPropertyName("raw_definition")]
            [JsonPropertyOrder(12)]
            public string? RawDefinition { get; set; }

            [JsonPropertyName("message")]
            [JsonPropertyOrder(13)]
            public string? Message { get; set; }
        }

        internal sealed class CharacterRaceInfo : CharacterEntryInfo
        {
            [JsonPropertyName("allowed_roles")]
            public List<string>? AllowedRoles { get; set; }

            [JsonPropertyName("allowed_alignments")]
            public List<string>? AllowedAlignments { get; set; }

            [JsonPropertyName("always_peaceful_races")]
            public List<string>? AlwaysPeacefulRaces { get; set; }

            [JsonPropertyName("always_hostile_races")]
            public List<string>? AlwaysHostileRaces { get; set; }
        }

        internal sealed class CharacterRoleInfo : CharacterEntryInfo
        {
            [JsonPropertyName("allowed_races")]
            public List<string>? AllowedRaces { get; set; }

            [JsonPropertyName("allowed_alignments")]
            public List<string>? AllowedAlignments { get; set; }

            [JsonPropertyName("quest_artifact")]
            public string? QuestArtifact { get; set; }
        }

        internal sealed class CharacterAbilityInfo
        {
            [JsonPropertyName("level")]
            public int Level { get; set; }

            [JsonPropertyName("ability")]
            public string Ability { get; set; } = string.Empty;
        }
    }
}
