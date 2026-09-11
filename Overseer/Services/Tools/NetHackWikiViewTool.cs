using System;
using System.Linq;
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
                return Task.FromResult(new ToolResult { Success = true, Content = $"NetHack wiki article matching '{article}' not found." });
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

        // Trims, collapses internal whitespace, and lowercases, so a request differing from the resolved title only by spacing or case still counts as an exact hit.
        private static string NormalizeForComparison(string s) => Regex.Replace(s.Trim(), @"\s+", " ").ToLowerInvariant();

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
