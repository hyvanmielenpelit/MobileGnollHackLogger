using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Overseer.Services.Tools
{
    public class MonsterLookupTool : IToolHandler
    {
        private readonly WikiService _wikiService;

        public string ToolName => "monster_lookup";
        public string Description { get; set; } = "Look up a monster in the GnollHack wiki database.";
        public ToolExecutionLocation ExecutionLocation => ToolExecutionLocation.Server;
        public ToolCategory Category => ToolCategory.InformationRetrieval;

        public JsonElement ParameterSchema { get; }

        public MonsterLookupTool(WikiService wikiService)
        {
            _wikiService = wikiService;
            ParameterSchema = JsonDocument.Parse(@"
            {
                ""type"": ""object"",
                ""properties"": {
                    ""name"": { ""type"": ""string"", ""description"": ""Monster name"" }
                },
                ""required"": [""name""]
            }").RootElement;
        }

        public Task<ToolResult> ExecuteAsync(JsonElement parameters, ToolExecutionContext context, CancellationToken cancellationToken)
        {
            if (!_wikiService.IsIndexingComplete)
            {
                return Task.FromResult(new ToolResult { Success = false, ErrorMessage = ToolGuardMessages.WikiIndexingInProgress });
            }

            string name = "";
            if (parameters.TryGetProperty("name", out var queryElem))
            {
                name = queryElem.GetString() ?? "";
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                return Task.FromResult(new ToolResult { Success = false, ErrorMessage = "Missing name parameter" });
            }

            // Using WikiService with 'monster' filter.
            var results = _wikiService.GetRelevantContext(name, "monster");
            var content = string.Join("\n\n", results);

            if (string.IsNullOrWhiteSpace(content))
            {
                // Fallback to searching without filter just in case
                results = _wikiService.GetRelevantContext(name);
                content = string.Join("\n\n", results);

                if (string.IsNullOrWhiteSpace(content))
                {
                    return Task.FromResult(new ToolResult { Success = true, Content = BuildMissContent(name) });
                }
            }

            if (context.SpoilerFreeMode)
            {
                content = ApplySpoilerFreeMode(content);
            }

            return Task.FromResult(new ToolResult { Success = true, Content = content });
        }
        
        private const int ProbeMaxResults = 3;

        /// <summary>
        /// Builds a miss message that points at a next action instead of a bare "not found":
        /// that both the category-filtered search and the unfiltered fallback missed, which
        /// nearby articles the wiki does hold, and which tool to reach for next. Never throws —
        /// falls back to a plain miss message. Kept to a few hundred characters: every tool
        /// result is re-sent to the model on each subsequent round of the same question.
        /// </summary>
        internal string BuildMissContent(string name)
        {
            try
            {
                var sb = new System.Text.StringBuilder("No GnollHack wiki article matched the monster '")
                    .Append(name)
                    .Append("'. Both the 'monster' path filter and an unfiltered search of the whole wiki missed");

                var terms = name
                    .Split(new[] { ' ', '	', '-', '_' }, StringSplitOptions.RemoveEmptyEntries)
                    .Where(t => t.Length >= 4)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(2)
                    .ToArray();

                var hits = terms
                    .Select(t => (Term: t, Files: SafeProbe(t)))
                    .Where(x => x.Files.Count > 0)
                    .Select(x => $"'{x.Term}' matches {string.Join(", ", x.Files)}")
                    .ToList();

                sb.Append(hits.Count > 0
                    ? ", but " + string.Join("; ", hits) + "."
                    : terms.Length > 1 ? ", and so did each word of it separately." : ".");

                sb.Append(" Try get_monster_stats for the numeric stats straight from the source, or wiki_search with a broader query.");
                return sb.ToString();
            }
            catch
            {
                return $"No information found for monster: {name}";
            }
        }

        /// <summary>
        /// Runs one bounded near-miss probe through the same index and returns the article
        /// filenames it hit. Swallows its own failures into "no hit", so a miss can never itself
        /// fail.
        /// </summary>
        private List<string> SafeProbe(string probeQuery)
        {
            try
            {
                return _wikiService
                    .GetRelevantContext(probeQuery, null, ProbeMaxResults)
                    .Select(snippet => Regex.Match(snippet, @"^---\s*(.+?)\s*---"))
                    .Where(m => m.Success)
                    .Select(m => m.Groups[1].Value)
                    .Take(ProbeMaxResults)
                    .ToList();
            }
            catch
            {
                return new List<string>();
            }
        }

        private string ApplySpoilerFreeMode(string content)
        {
            return content + "\n\n[SPOILER-FREE MODE ACTIVE: Review the spoiler_policy before sharing this information. Only share mechanics, not unrevealed content.]";
        }
    }
}
