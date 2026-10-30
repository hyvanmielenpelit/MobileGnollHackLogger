using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Overseer.Services.Tools
{
    /// <summary>
    /// Counts and lists the articles of the GnollHack or NetHack wiki from the statistics published
    /// with each index. It runs no Lucene query, and its results carry no filesystem path, commit id
    /// or byte count.
    /// </summary>
    public class WikiOverviewTool : IToolHandler
    {
        private const string GnollHackWiki = "gnollhack";
        private const string NetHackWiki = "nethack";
        private const int DefaultLimit = 100;
        private const int MaxLimit = 200;
        private const string IndexUnavailable = "The wiki index is not available.";
        private const string LiveSiteNote = "The live site can differ slightly.";
        private const string SpoilerFreeReminder = "[SPOILER-FREE MODE ACTIVE: Review the spoiler_policy before sharing this information. Only share mechanics, not unrevealed content.]";

        private readonly WikiService _wikiService;
        private readonly NetHackWikiService _netHackWikiService;

        public string ToolName => "wiki_overview";
        public string Description { get; set; } = "Count and list the articles of the GnollHack or NetHack wiki, from Overseer's index.";
        public ToolExecutionLocation ExecutionLocation => ToolExecutionLocation.Server;
        public ToolCategory Category => ToolCategory.InformationRetrieval;

        public JsonElement ParameterSchema { get; }

        public WikiOverviewTool(WikiService wikiService, NetHackWikiService netHackWikiService)
        {
            _wikiService = wikiService;
            _netHackWikiService = netHackWikiService;

            ParameterSchema = JsonDocument.Parse(@"
            {
                ""type"": ""object"",
                ""properties"": {
                    ""wiki"": {
                        ""type"": ""string"",
                        ""description"": ""Which wiki: 'gnollhack' (default) or 'nethack'"",
                        ""enum"": [""gnollhack"", ""nethack""]
                    },
                    ""category"": { ""type"": ""string"", ""description"": ""Optional, GnollHack wiki only. Case-insensitive substring of the article's path inside the wiki, file name included."" },
                    ""list_titles"": { ""type"": ""boolean"", ""description"": ""Also list article paths (GnollHack) or titles (NetHack). Default false."" },
                    ""offset"": { ""type"": ""integer"", ""description"": ""How many listed articles to skip. Default 0."" },
                    ""limit"": { ""type"": ""integer"", ""description"": ""How many articles to list. Default 100, maximum 200."" }
                }
            }").RootElement;
        }

        public Task<ToolResult> ExecuteAsync(JsonElement parameters, ToolExecutionContext context, CancellationToken cancellationToken)
        {
            string wiki = GnollHackWiki;
            if (parameters.TryGetProperty("wiki", out var wikiElem) && wikiElem.ValueKind != JsonValueKind.Null)
            {
                wiki = wikiElem.ValueKind == JsonValueKind.String ? wikiElem.GetString()?.Trim().ToLowerInvariant() ?? string.Empty : "?";
                if (wiki.Length == 0) wiki = GnollHackWiki;
            }

            if (wiki != GnollHackWiki && wiki != NetHackWiki)
            {
                return Task.FromResult(Failed("Unknown wiki. Use 'gnollhack' or 'nethack'."));
            }

            bool isNetHack = wiki == NetHackWiki;
            if (isNetHack ? !_netHackWikiService.IsIndexingComplete : !_wikiService.IsIndexingComplete)
            {
                return Task.FromResult(Failed(isNetHack ? ToolGuardMessages.NetHackWikiIndexingInProgress : ToolGuardMessages.WikiIndexingInProgress));
            }

            string? category = null;
            if (parameters.TryGetProperty("category", out var categoryElem) && categoryElem.ValueKind != JsonValueKind.Null)
            {
                if (categoryElem.ValueKind != JsonValueKind.String)
                {
                    return Task.FromResult(Failed("category must be a string."));
                }

                category = categoryElem.GetString();
                if (string.IsNullOrWhiteSpace(category)) category = null;
            }

            if (isNetHack && category != null)
            {
                return Task.FromResult(Failed("category applies to the GnollHack wiki only; the NetHack wiki has no directories. Call again without category."));
            }

            bool listTitles = parameters.TryGetProperty("list_titles", out var listElem) && listElem.ValueKind == JsonValueKind.True;
            int offset = Math.Max(ReadInt(parameters, "offset", 0), 0);
            int limit = Math.Clamp(ReadInt(parameters, "limit", DefaultLimit), 1, MaxLimit);

            ToolResult result = isNetHack
                ? DescribeNetHackWiki(listTitles, offset, limit)
                : DescribeGnollHackWiki(category, listTitles, offset, limit);

            if (result.Success && listTitles && context.SpoilerFreeMode)
            {
                result.Content += "\n\n" + SpoilerFreeReminder;
            }

            return Task.FromResult(result);
        }

        private ToolResult DescribeGnollHackWiki(string? category, bool listTitles, int offset, int limit)
        {
            var overview = _wikiService.GetOverview();
            if (overview == null) return Failed(IndexUnavailable);

            string counts = Headline("GnollHack wiki", overview.FileCount, overview.IndexedAtUtc) + "\n"
                + string.Join("; ", overview.Categories.Select(c => c.Category + ": " + Number(c.Articles)));

            var page = category != null || listTitles
                ? _wikiService.ListArticles(category, offset, limit)
                : WikiArticlePage.Empty;

            var sb = new StringBuilder();
            if (category == null)
            {
                sb.Append(counts);
            }
            else
            {
                string shown = category.Trim();
                if (page.Total == 0)
                {
                    // The opening BenchmarkToolResultClassifier reads as a miss.
                    sb.Append("No GnollHack wiki article path contains '").Append(shown).Append("'.\n").Append(counts);
                    return new ToolResult { Success = true, Content = sb.ToString() };
                }

                sb.Append(counts).Append('\n')
                    .Append(Count(page.Total)).Append(" with '").Append(shown).Append("' in the path.");
            }

            if (listTitles) AppendListing(sb, page, offset);
            return new ToolResult { Success = true, Content = sb.ToString() };
        }

        private ToolResult DescribeNetHackWiki(bool listTitles, int offset, int limit)
        {
            var overview = _netHackWikiService.GetOverview();
            if (overview == null) return Failed(IndexUnavailable);

            var sb = new StringBuilder(Headline("NetHack wiki", overview.FileCount, overview.IndexedAtUtc)).Append('\n')
                .Append(string.Join("; ", overview.Namespaces.Select(n => n.Namespace + ": " + Number(n.Articles))));

            if (listTitles) AppendListing(sb, _netHackWikiService.ListTitles(offset, limit), offset);
            return new ToolResult { Success = true, Content = sb.ToString() };
        }

        private static string Headline(string wikiName, int fileCount, DateTime indexedAtUtc)
        {
            string built = indexedAtUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            return $"{wikiName}: {Count(fileCount)} in Overseer's index, built {built} UTC. {LiveSiteNote}";
        }

        /// <summary>Appends the page under a line that says where it sits and how to get the next one.</summary>
        private static void AppendListing(StringBuilder sb, WikiArticlePage page, int offset)
        {
            sb.Append("\n\n");
            if (page.Articles.Count == 0)
            {
                sb.Append("Nothing to list at offset ").Append(Number(offset)).Append(": ").Append(Count(page.Total)).Append(" in all.");
                return;
            }

            int end = offset + page.Articles.Count;
            sb.Append("Articles ").Append(Number(offset + 1)).Append('-').Append(Number(end)).Append(" of ").Append(Number(page.Total)).Append('.');
            if (end < page.Total)
            {
                sb.Append(" Call again with offset ").Append(end.ToString(CultureInfo.InvariantCulture)).Append(" for more.");
            }

            foreach (string article in page.Articles)
            {
                sb.Append('\n').Append(article);
            }
        }

        private static string Count(int articles) => Number(articles) + (articles == 1 ? " article" : " articles");

        private static string Number(int value) => value.ToString("N0", CultureInfo.InvariantCulture);

        private static int ReadInt(JsonElement parameters, string name, int defaultValue)
        {
            return parameters.TryGetProperty(name, out var elem)
                && elem.ValueKind == JsonValueKind.Number
                && elem.TryGetInt32(out int value)
                    ? value
                    : defaultValue;
        }

        private static ToolResult Failed(string message) => new() { Success = false, ErrorMessage = message };
    }
}
