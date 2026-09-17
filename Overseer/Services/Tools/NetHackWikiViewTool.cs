using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Overseer.Services.Tools
{
    public class NetHackWikiViewTool : IToolHandler
    {
        private readonly NetHackWikiService _netHackWikiService;

        public string ToolName => "nethack_wiki_view";
        public string Description { get; set; } = "View a specific NetHack wiki article by title. Use when you already know which article you want.";
        public ToolExecutionLocation ExecutionLocation => ToolExecutionLocation.Server;
        public ToolCategory Category => ToolCategory.InformationRetrieval;

        public JsonElement ParameterSchema { get; }

        public NetHackWikiViewTool(NetHackWikiService netHackWikiService)
        {
            _netHackWikiService = netHackWikiService;
            ParameterSchema = JsonDocument.Parse(@"
            {
                ""type"": ""object"",
                ""properties"": {
                    ""article"": {
                        ""type"": ""string"",
                        ""description"": ""The article title to retrieve (e.g. 'Cockatrice', 'Wand of digging', 'Elbereth')""
                    },
                    ""section"": {
                        ""type"": ""string"",
                        ""description"": ""Optional. A specific section heading to extract (e.g. 'Strategy', 'Generation')""
                    }
                },
                ""required"": [""article""]
            }").RootElement;
        }

        public Task<ToolResult> ExecuteAsync(JsonElement parameters, ToolExecutionContext context, CancellationToken cancellationToken)
        {
            if (!_netHackWikiService.IsIndexingComplete)
            {
                return Task.FromResult(new ToolResult { Success = false, ErrorMessage = ToolGuardMessages.NetHackWikiIndexingInProgress });
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

            var (content, resolvedTitle, candidates) = _netHackWikiService.GetArticleResolved(article, section);

            if (string.IsNullOrWhiteSpace(content))
            {
                return Task.FromResult(new ToolResult { Success = true, Content = BuildMissContent(article, candidates) });
            }

            if (resolvedTitle != null && !string.Equals(NormalizeForComparison(article), NormalizeForComparison(resolvedTitle), StringComparison.Ordinal))
            {
                content = BuildResolutionLine(article, resolvedTitle, candidates) + "\n" + content;
            }

            if (context.SpoilerFreeMode)
            {
                content += "\n\n[SPOILER-FREE MODE ACTIVE: Review the spoiler_policy before sharing this information. Only share mechanics, not unrevealed content.]";
            }

            return Task.FromResult(new ToolResult { Success = true, Content = content });
        }

        private const int ResolutionLineMaxChars = 600;
        private const int MaxOtherCandidates = 4;
        private const int MissContentMaxChars = 600;
        private const int MaxMissCandidates = 4;

        // Trims, collapses internal whitespace, and lowercases, so a request differing from the resolved title only by spacing or case still counts as an exact hit.
        private static string NormalizeForComparison(string s) => Regex.Replace(s.Trim(), @"\s+", " ").ToLowerInvariant();

        /// <summary>
        /// Builds the payload returned when <paramref name="article"/> resolved to no content at
        /// all: names the miss, lists up to <see cref="MaxMissCandidates"/> candidate titles (the
        /// resolver's own <paramref name="candidates"/>, or, when the resolver found none, the top
        /// titles of one <c>nethack_wiki_search</c>-style query for the same string), and points at
        /// <c>nethack_wiki_search</c> as the next action. Never exceeds
        /// <see cref="MissContentMaxChars"/>. Wrapped in a try/catch so a defect in the suggestion
        /// step falls back to the plain miss sentence rather than turning a miss into a tool error.
        /// </summary>
        private string BuildMissContent(string article, IReadOnlyList<string> candidates)
        {
            try
            {
                var titles = candidates.Count > 0 ? candidates : SearchCandidateTitles(article);

                var sb = new StringBuilder();
                sb.Append($"No NetHack wiki article matched '{article}'.");

                if (titles.Count > 0)
                {
                    var chosen = titles.Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxMissCandidates);
                    sb.Append($" Candidates: {string.Join("; ", chosen)}.");
                }
                else
                {
                    sb.Append(" No candidates found.");
                }

                sb.Append(" Try nethack_wiki_search.");

                string missContent = sb.ToString();
                return missContent.Length > MissContentMaxChars ? missContent.Substring(0, MissContentMaxChars) : missContent;
            }
            catch
            {
                return $"NetHack wiki article matching '{article}' not found.";
            }
        }

        /// <summary>
        /// The top article titles a <c>nethack_wiki_search</c>-style query for <paramref name="article"/>
        /// would surface, read off <see cref="NetHackWikiService.GetRelevantContext"/>'s
        /// <c>--- Title ---</c> headers. An exception here yields an empty list, which
        /// <see cref="BuildMissContent"/> renders as "No candidates found."
        /// </summary>
        private List<string> SearchCandidateTitles(string article)
        {
            try
            {
                var titles = new List<string>();
                foreach (var snippet in _netHackWikiService.GetRelevantContext(article, null, MaxMissCandidates))
                {
                    var m = Regex.Match(snippet, @"^--- (.+?) ---", RegexOptions.Multiline);
                    if (m.Success)
                    {
                        titles.Add(m.Groups[1].Value);
                    }
                }
                return titles;
            }
            catch
            {
                return new List<string>();
            }
        }

        /// <summary>
        /// Builds the line prepended when the request did not resolve to an exact title: names
        /// the article shown, then up to <see cref="MaxOtherCandidates"/> other candidates
        /// (the resolved title itself excluded), semicolon-joined. Omits the "Other candidates"
        /// clause when none remain, and never exceeds <see cref="ResolutionLineMaxChars"/>.
        /// </summary>
        private static string BuildResolutionLine(string request, string resolvedTitle, System.Collections.Generic.IReadOnlyList<string> candidates)
        {
            var others = candidates
                .Where(c => !string.Equals(NormalizeForComparison(c), NormalizeForComparison(resolvedTitle), StringComparison.Ordinal))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxOtherCandidates)
                .ToList();

            var line = $"[No NetHack wiki article titled '{request}'. Showing '{resolvedTitle}'.";
            if (others.Count > 0)
            {
                line += $" Other candidates: {string.Join("; ", others)}.";
            }
            line += "]";

            return line.Length > ResolutionLineMaxChars ? line.Substring(0, ResolutionLineMaxChars) : line;
        }
    }
}
