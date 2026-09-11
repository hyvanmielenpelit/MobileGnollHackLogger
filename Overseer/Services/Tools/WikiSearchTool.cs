using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace Overseer.Services.Tools
{
    public class WikiSearchTool : IToolHandler
    {
        private readonly WikiService _wikiService;
        private readonly int _configuredMaxResults;
        private readonly int _perResultChars;

        public string ToolName => "wiki_search";
        public string Description { get; set; } = "Search the GnollHack specific wiki for information.";
        public ToolExecutionLocation ExecutionLocation => ToolExecutionLocation.Server;
        public ToolCategory Category => ToolCategory.InformationRetrieval;

        /* This tool's own budget is Tools:wiki_search:MaxResults x PerResultChars - 5 x 2500 =
           12500 - which exceeds the generic 10000-char per-tool cap, so a full-yield search was
           always truncated mid-article on its last hit. A floor here raises the cap for this tool
           alone: ToolExecutor takes max(base, override), so it can never lower one, and it leaves
           the shared MaxResultLength default - which is every other tool's cap and the live chat
           setting - untouched. 13000 is the 12500 plus headroom for the per-hit separators, and
           clamping max_results to _configuredMaxResults is what keeps a full yield inside it. */
        public int? MaxResultLengthOverride => 13000;

        public JsonElement ParameterSchema { get; }

        public WikiSearchTool(WikiService wikiService, IConfiguration configuration)
        {
            _wikiService = wikiService;
            _configuredMaxResults = configuration.GetValue<int>("Tools:wiki_search:MaxResults", 5);
            _perResultChars = configuration.GetValue<int>("Tools:wiki_search:PerResultChars", 2500);
            ParameterSchema = JsonDocument.Parse($@"
            {{
                ""type"": ""object"",
                ""properties"": {{
                    ""query"": {{ ""type"": ""string"", ""description"": ""The search terms to look up in the wiki"" }},
                    ""category"": {{ ""type"": ""string"", ""description"": ""Optional. Filter by category (e.g., 'monster', 'item', 'spell', 'class')"" }},
                    ""max_results"": {{ ""type"": ""integer"", ""description"": ""Maximum number of wiki articles to return (default and maximum {_configuredMaxResults}; larger values are clamped)."" }}
                }},
                ""required"": [""query""]
            }}").RootElement;
        }

        public Task<ToolResult> ExecuteAsync(JsonElement parameters, ToolExecutionContext context, CancellationToken cancellationToken)
        {
            if (!_wikiService.IsIndexingComplete)
            {
                return Task.FromResult(new ToolResult { Success = false, ErrorMessage = ToolGuardMessages.WikiIndexingInProgress });
            }

            string query = "";
            if (parameters.TryGetProperty("query", out var queryElem))
            {
                query = queryElem.GetString() ?? "";
            }

            if (string.IsNullOrWhiteSpace(query))
            {
                return Task.FromResult(new ToolResult { Success = false, ErrorMessage = "Missing query parameter" });
            }

            int maxResults = _configuredMaxResults;
            if (parameters.TryGetProperty("max_results", out var maxResElem) && maxResElem.ValueKind == JsonValueKind.Number)
            {
                maxResults = maxResElem.GetInt32();
            }
            maxResults = Math.Clamp(maxResults, 1, Math.Max(1, _configuredMaxResults));

            string? category = null;
            if (parameters.TryGetProperty("category", out var categoryElem))
            {
                category = categoryElem.GetString();
            }

            var results = _wikiService.GetRelevantSnippets(query, category, maxResults, _perResultChars);
            var content = string.Join("\n\n", results);

            if (string.IsNullOrWhiteSpace(content))
            {
                return Task.FromResult(new ToolResult { Success = true, Content = BuildMissContent(query, category, maxResults) });
            }

            if (context.SpoilerFreeMode)
            {
                content = ApplySpoilerFreeMode(content);
            }

            return Task.FromResult(new ToolResult { Success = true, Content = content });
        }
        
        private const int ProbeMaxResults = 3;
        private const int ProbeMaxChars = 200;

        /// <summary>
        /// Builds a miss message that points at a next action instead of a bare "not found":
        /// whether the category filter is what excluded the match, which near-neighbour terms do
        /// have articles, and which tool to reach for next. Never throws — falls back to a plain
        /// miss message. Kept to a few hundred characters: every tool result is re-sent to the
        /// model on each subsequent round of the same question, so a verbose miss is paid once per
        /// remaining round.
        /// </summary>
        internal string BuildMissContent(string query, string? category, int maxResults)
        {
            try
            {
                var sb = new System.Text.StringBuilder("No GnollHack wiki article matched '").Append(query).Append('\'');

                if (!string.IsNullOrWhiteSpace(category))
                {
                    // category is a wildcard against the indexed file's filesystem path, not a
                    // taxonomy field, so a plausible-looking value that appears in no path
                    // excludes every hit.
                    sb.Append(" with category='").Append(category).Append('\'');
                    sb.Append(". category matches the article's file path as a substring, not a tag");

                    var unfiltered = SafeProbe(query, null, maxResults);
                    sb.Append(unfiltered.Count > 0
                        ? $", and without it the query matches {string.Join(", ", unfiltered)} — retry with no category"
                        : ", and the query matches nothing without it either");
                    sb.Append('.');
                }
                else
                {
                    sb.Append('.');

                    var terms = query
                        .Split(new[] { ' ', '\t', '-', '_' }, StringSplitOptions.RemoveEmptyEntries)
                        .Where(t => t.Length >= 4)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Take(2)
                        .ToArray();

                    var hits = terms
                        .Select(t => (Term: t, Files: SafeProbe(t, null, ProbeMaxResults)))
                        .Where(x => x.Files.Count > 0)
                        .Select(x => $"'{x.Term}' matches {string.Join(", ", x.Files)}")
                        .ToList();

                    sb.Append(hits.Count > 0
                        ? " " + string.Join("; ", hits) + "."
                        : terms.Length > 0 ? " No individual term matched either." : string.Empty);
                }

                sb.Append(" Try wiki_view with a known article title, or nethack_wiki_search for a NetHack mechanic GnollHack inherits.");
                return sb.ToString();
            }
            catch
            {
                return "No relevant information found in the GnollHack wiki.";
            }
        }

        /// <summary>
        /// Runs one bounded near-miss probe through the same index and returns the article
        /// filenames it hit. Swallows its own failures into "no hit", so a miss can never itself
        /// fail.
        /// </summary>
        private List<string> SafeProbe(string probeQuery, string? category, int maxResults)
        {
            try
            {
                return _wikiService
                    .GetRelevantSnippets(probeQuery, category, Math.Min(maxResults, ProbeMaxResults), ProbeMaxChars)
                    .Select(snippet => System.Text.RegularExpressions.Regex.Match(snippet, @"^---\s*(.+?)\s*---"))
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
            // In spoiler-free mode, return full content but add a reminder.
            // The LLM's spoiler policy (from spoiler_policy.md) handles what to share.
            return content + "\n\n[SPOILER-FREE MODE ACTIVE: Review the spoiler_policy before sharing this information. Only share mechanics, not unrevealed content.]";
        }
    }
}
