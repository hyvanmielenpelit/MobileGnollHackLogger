using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Overseer.Services.Tools
{
    public class WikiViewTool : IToolHandler
    {
        private readonly WikiService _wikiService;

        public string ToolName => "wiki_view";
        public string Description { get; set; } = "View a specific wiki article by name. Use when you already know which article you want.";
        public ToolExecutionLocation ExecutionLocation => ToolExecutionLocation.Server;
        public ToolCategory Category => ToolCategory.InformationRetrieval;

        public JsonElement ParameterSchema { get; }

        public WikiViewTool(WikiService wikiService)
        {
            _wikiService = wikiService;
            ParameterSchema = JsonDocument.Parse(@"
            {
                ""type"": ""object"",
                ""properties"": {
                    ""article"": { ""type"": ""string"", ""description"": ""Article filename or title (fuzzy matched, e.g., 'potion', 'gnoll', 'valkyrie')"" },
                    ""section"": { ""type"": ""string"", ""description"": ""Optional. Specific section heading to extract (e.g., 'Strategy', 'Stats')"" }
                },
                ""required"": [""article""]
            }").RootElement;
        }

        public Task<ToolResult> ExecuteAsync(JsonElement parameters, ToolExecutionContext context, CancellationToken cancellationToken)
        {
            if (!_wikiService.IsIndexingComplete)
            {
                return Task.FromResult(new ToolResult { Success = false, ErrorMessage = ToolGuardMessages.WikiIndexingInProgress });
            }

            string article = "";
            if (parameters.TryGetProperty("article", out var articleElem))
            {
                article = articleElem.GetString() ?? "";
            }

            string? section = null;
            if (parameters.TryGetProperty("section", out var sectionElem))
            {
                section = sectionElem.GetString();
            }

            if (string.IsNullOrWhiteSpace(article))
            {
                return Task.FromResult(new ToolResult { Success = false, ErrorMessage = "Missing article parameter" });
            }

            string? fetched = _wikiService.GetArticle(article, section);

            if (IsEmptyArticle(fetched))
            {
                return Task.FromResult(new ToolResult { Success = true, Content = BuildMissContent(article, section) });
            }

            string content = fetched!;

            if (context.SpoilerFreeMode)
            {
                content += "\n\n[SPOILER-FREE MODE ACTIVE: Review the spoiler_policy before sharing this information. Only share mechanics, not unrevealed content.]";
            }

            return Task.FromResult(new ToolResult { Success = true, Content = content });
        }

        private const int ProbeMaxResults = 3;
        private const int ProbeMaxChars = 200;

        /// <summary>
        /// A result carrying nothing but the <c>--- filename ---</c> header, which is an article
        /// whose body is empty. A section no heading matches does <b>not</b> reach here:
        /// <see cref="WikiService.GetArticle"/> answers that case itself with an explanatory
        /// <c>[Section '…' not found in article. Returning full text.]</c> line followed by the whole
        /// article, which tells the model what happened and still gives it the content.
        /// </summary>
        private static bool IsEmptyArticle(string? content)
        {
            if (string.IsNullOrWhiteSpace(content)) return true;

            int newline = content.IndexOf('\n');
            return newline >= 0 && string.IsNullOrWhiteSpace(content.Substring(newline + 1));
        }

        /// <summary>
        /// Builds a miss message that points at a next action instead of a bare "not found":
        /// which nearby articles the index does hold, and which tool to reach for next. Never
        /// throws — falls back to a plain miss message. Kept to a few hundred characters: every
        /// tool result is re-sent to the model on each subsequent round of the same question.
        /// </summary>
        internal string BuildMissContent(string article, string? section)
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                sb.Append("No wiki article matched '").Append(article).Append('\'');

                if (!string.IsNullOrWhiteSpace(section))
                {
                    // It was the article that missed, not the heading: a heading that matches
                    // nothing returns the whole article instead, so re-spelling the section is
                    // not the recovery here.
                    sb.Append(" (the article itself, so section='").Append(section).Append("' was never reached)");
                }

                var near = SafeProbe(article);
                sb.Append(near.Count > 0
                    ? $". The index does hold {string.Join(", ", near)}"
                    : ". No article with a similar title is indexed either");

                sb.Append(". wiki_view matches on title and filename only — use wiki_search to find an article by its content, or nethack_wiki_view for a NetHack article.");
                return sb.ToString();
            }
            catch
            {
                return $"Wiki article matching '{article}' not found.";
            }
        }

        /// <summary>
        /// Runs one bounded near-title probe through the same index and returns the article
        /// filenames it hit. Swallows its own failures into "no hit", so a miss can never itself
        /// fail.
        /// </summary>
        private List<string> SafeProbe(string probeQuery)
        {
            try
            {
                return _wikiService
                    .GetRelevantSnippets(probeQuery, null, ProbeMaxResults, ProbeMaxChars)
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
    }
}
