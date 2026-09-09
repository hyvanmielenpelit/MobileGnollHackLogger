using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using System.Text.RegularExpressions;

namespace Overseer.Services.Tools
{
    public class SourceCodeSearchTool : IToolHandler
    {
        private readonly SourceCodeService _sourceCodeService;
        private readonly NetHackSourceCodeService _netHackService;
        private readonly int _maxResultLength;
        private readonly int _defaultMaxResults;
        private readonly int _defaultContextLines;

        public string ToolName => "source_code_search";
        public string Description { get; set; } = "Search the GnollHack or NetHack C source code for functions, macros, constants, or game mechanic implementations.";
        public ToolExecutionLocation ExecutionLocation => ToolExecutionLocation.Server;
        public ToolCategory Category => ToolCategory.InformationRetrieval;

        public JsonElement ParameterSchema { get; }

        public SourceCodeSearchTool(SourceCodeService sourceCodeService, NetHackSourceCodeService netHackService, IConfiguration configuration)
        {
            _sourceCodeService = sourceCodeService;
            _netHackService = netHackService;
            
            if (!int.TryParse(configuration["MaxSourceResultLength"], out _maxResultLength))
            {
                _maxResultLength = 100000;
            }

            _defaultMaxResults = configuration.GetValue<int>("Tools:source_code_search:MaxResults", 10);
            _defaultContextLines = configuration.GetValue<int>("Tools:source_code_search:ContextLines", 5);

            ParameterSchema = JsonDocument.Parse(@"
            {
                ""type"": ""object"",
                ""properties"": {
                    ""query"": { ""type"": ""string"", ""description"": ""One literal substring, matched against each source line on its own. Not split into terms, cannot span a line break, and does not match file or symbol names. Prefer a short distinctive identifier over a phrase."" },
                    ""file_filter"": { ""type"": ""string"", ""description"": ""Optional. Restrict to a specific file (e.g., 'potion.c')"" },
                    ""max_results"": { ""type"": ""integer"", ""description"": ""Maximum number of files to return matches from (default 10, max 100)"" },
                    ""is_regex"": { ""type"": ""boolean"", ""description"": ""Optional. If true, treat the query as a regular expression"" },
                    ""whole_word"": { ""type"": ""boolean"", ""description"": ""Optional. If true and is_regex is false, search for whole words only"" },
                    ""case_sensitive"": { ""type"": ""boolean"", ""description"": ""Optional. If true, perform a case-sensitive search"" },
                    ""filenames_only"": { ""type"": ""boolean"", ""description"": ""Optional. If true, return only file paths and match counts without code snippets"" },
                    ""context_lines"": { ""type"": ""integer"", ""description"": ""Optional. Number of context lines around each match (default 5, max 25)"" },
                    ""repository"": {
                        ""type"": ""string"",
                        ""description"": ""Which codebase to search: 'gnollhack' (default) or 'nethack'"",
                        ""enum"": [""gnollhack"", ""nethack""]
                    }
                },
                ""required"": [""query""]
            }").RootElement;
        }

        private SourceCodeService ResolveService(JsonElement parameters, out string guardMessage)
        {
            if (parameters.TryGetProperty("repository", out var repo) &&
                repo.GetString()?.Equals("nethack", StringComparison.OrdinalIgnoreCase) == true)
            {
                guardMessage = ToolGuardMessages.NetHackSourceCodeIndexingInProgress;
                return _netHackService;
            }

            guardMessage = ToolGuardMessages.SourceCodeIndexingInProgress;
            return _sourceCodeService;
        }

        public Task<ToolResult> ExecuteAsync(JsonElement parameters, ToolExecutionContext context, CancellationToken cancellationToken)
        {
            var service = ResolveService(parameters, out var guardMessage);
            if (!service.IsIndexingComplete)
            {
                return Task.FromResult(new ToolResult { Success = false, ErrorMessage = guardMessage });
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

            string fileFilter = "";
            if (parameters.TryGetProperty("file_filter", out var fileFilterElem))
            {
                fileFilter = fileFilterElem.GetString() ?? "";
            }

            int maxResults = _defaultMaxResults;
            if (parameters.TryGetProperty("max_results", out var maxResElem) && maxResElem.ValueKind == JsonValueKind.Number)
            {
                maxResults = maxResElem.GetInt32();
            }

            bool isRegex = false;
            if (parameters.TryGetProperty("is_regex", out var isRegexElem) && (isRegexElem.ValueKind == JsonValueKind.True || isRegexElem.ValueKind == JsonValueKind.False))
            {
                isRegex = isRegexElem.GetBoolean();
            }

            bool filenamesOnly = false;
            if (parameters.TryGetProperty("filenames_only", out var filenamesOnlyElem) && (filenamesOnlyElem.ValueKind == JsonValueKind.True || filenamesOnlyElem.ValueKind == JsonValueKind.False))
            {
                filenamesOnly = filenamesOnlyElem.GetBoolean();
            }

            int contextLines = _defaultContextLines;
            if (parameters.TryGetProperty("context_lines", out var contextLinesElem) && contextLinesElem.ValueKind == JsonValueKind.Number)
            {
                contextLines = contextLinesElem.GetInt32();
            }

            bool caseSensitive = false;
            if (parameters.TryGetProperty("case_sensitive", out var caseSensitiveElem) && (caseSensitiveElem.ValueKind == JsonValueKind.True || caseSensitiveElem.ValueKind == JsonValueKind.False))
            {
                caseSensitive = caseSensitiveElem.GetBoolean();
            }

            bool wholeWord = false;
            if (parameters.TryGetProperty("whole_word", out var wholeWordElem) && (wholeWordElem.ValueKind == JsonValueKind.True || wholeWordElem.ValueKind == JsonValueKind.False))
            {
                wholeWord = wholeWordElem.GetBoolean();
            }

            if (wholeWord && !isRegex)
            {
                query = $@"\b{Regex.Escape(query)}\b";
                isRegex = true;
            }

            bool includeNetCode = context.OverseerMode == 2 && !(service is NetHackSourceCodeService);

            var content = service.SearchFiles(query, fileFilter, maxResults, includeNetCode, _maxResultLength, isRegex, filenamesOnly, contextLines, caseSensitive);

            if (string.IsNullOrWhiteSpace(content))
            {
                if (caseSensitive)
                {
                    content = service.SearchFiles(query, fileFilter, maxResults, includeNetCode, _maxResultLength, isRegex, filenamesOnly, contextLines, false);
                    if (!string.IsNullOrWhiteSpace(content))
                    {
                        content = $"[Note: No exact case match found. Falling back to case-insensitive search.]\n\n" + content;
                    }
                }
                
                if (string.IsNullOrWhiteSpace(content) && isRegex)
                {
                    content = service.SearchFiles(query, fileFilter, maxResults, includeNetCode, _maxResultLength, false, filenamesOnly, contextLines, false);
                    if (!string.IsNullOrWhiteSpace(content))
                    {
                        content = $"[Note: Regex search failed. Falling back to literal text search.]\n\n" + content;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(content))
            {
                return Task.FromResult(new ToolResult { Success = true, Content = BuildMissContent(service, query, fileFilter, includeNetCode, isRegex) });
            }

            if (context.SpoilerFreeMode)
            {
                content += "\n\n[SPOILER-FREE MODE ACTIVE: Review the spoiler_policy before sharing this information. Only share mechanics, not unrevealed content.]";
            }

            return Task.FromResult(new ToolResult { Success = true, Content = content });
        }

        private const int ProbeMaxResults = 3;
        private const int ProbeMaxResultLength = 1000;

        /// <summary>
        /// Builds a miss message that points at a next action instead of a bare "not found":
        /// near-neighbour identifiers, a whitespace-collapsed retry, or per-term phrase probes,
        /// depending on the shape of the query. Never throws — falls back to a plain miss message.
        /// </summary>
        private string BuildMissContent(SourceCodeService service, string query, string fileFilter, bool includeNetCode, bool isRegex)
        {
            try
            {
                var sb = new System.Text.StringBuilder("No relevant source code found for '").Append(query).Append('\'');
                if (!string.IsNullOrWhiteSpace(fileFilter))
                {
                    sb.Append(" (file_filter='").Append(fileFilter).Append("' may be excluding the match)");
                }
                sb.Append('.');

                bool hasWhitespace = query.Any(char.IsWhiteSpace);

                if (!isRegex && !hasWhitespace && Regex.IsMatch(query, @"^[A-Za-z0-9_][A-Za-z0-9_.>\-]*$"))
                {
                    AppendIdentifierNeighbours(sb, service, query, fileFilter, includeNetCode);
                }
                else if (!isRegex && hasWhitespace)
                {
                    sb.Append(" No line contains it as one literal substring — spacing matters (e.g. 'a =' and 'a=' do not match).");
                    AppendCollapsedWhitespaceProbe(sb, service, query, fileFilter, includeNetCode);

                    var terms = query.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                        .Where(t => t.Length >= 2).Take(3).ToArray();
                    if (terms.Length >= 2)
                    {
                        AppendPhraseProbes(sb, service, terms, fileFilter, includeNetCode);
                    }
                }

                sb.Append(" Try search_definitions for a known symbol, or list_indexed_files to see what is indexed.");
                return sb.ToString();
            }
            catch
            {
                return "No relevant source code found.";
            }
        }

        private static void AppendIdentifierNeighbours(System.Text.StringBuilder sb, SourceCodeService service, string query, string fileFilter, bool includeNetCode)
        {
            var candidates = new List<string>();
            var tokens = Regex.Matches(query, @"[A-Za-z0-9]+")
                .Select(m => m.Value).Where(t => t.Length >= 3)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            if (tokens.Count > 1)
            {
                candidates.AddRange(tokens.OrderByDescending(t => t.Length).Take(2));
            }
            else if (query.Length > 6)
            {
                candidates.Add(query.Substring(0, query.Length - 2));
                int half = Math.Max(3, query.Length / 2);
                if (half < query.Length - 2) candidates.Add(query.Substring(0, half));
            }

            bool found = false;
            foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase).Take(2))
            {
                string probe = SafeProbe(service, candidate, fileFilter, includeNetCode);
                if (!string.IsNullOrWhiteSpace(probe))
                {
                    found = true;
                    sb.Append(" '").Append(candidate).Append("' matches ").Append(SummarizeProbe(probe)).Append('.');
                }
            }
            if (!found && candidates.Count > 0)
            {
                sb.Append(" No shorter form of this identifier matched either.");
            }
        }

        private static void AppendCollapsedWhitespaceProbe(System.Text.StringBuilder sb, SourceCodeService service, string query, string fileFilter, bool includeNetCode)
        {
            string collapsed = Regex.Replace(query, @"\s+", "");
            if (collapsed.Length == 0 || string.Equals(collapsed, query, StringComparison.Ordinal)) return;

            string probe = SafeProbe(service, collapsed, fileFilter, includeNetCode);
            if (!string.IsNullOrWhiteSpace(probe))
            {
                sb.Append(" With whitespace removed, '").Append(collapsed).Append("' matches ").Append(SummarizeProbe(probe)).Append('.');
            }
        }

        private static void AppendPhraseProbes(System.Text.StringBuilder sb, SourceCodeService service, string[] terms, string fileFilter, bool includeNetCode)
        {
            var hits = new List<string>();
            foreach (var term in terms)
            {
                string probe = SafeProbe(service, term, fileFilter, includeNetCode);
                if (!string.IsNullOrWhiteSpace(probe))
                {
                    hits.Add($"'{term}' matches {SummarizeProbe(probe)}");
                }
            }
            sb.Append(hits.Count > 0
                ? " " + string.Join("; ", hits) + "."
                : " None of the individual terms matched either.");
        }

        /// <summary>Runs one filenames_only near-miss probe, swallowing errors and the "Error: Invalid regular expression" content.</summary>
        private static string SafeProbe(SourceCodeService service, string probeQuery, string fileFilter, bool includeNetCode)
        {
            try
            {
                var result = service.SearchFiles(probeQuery, fileFilter, ProbeMaxResults, includeNetCode, ProbeMaxResultLength, false, true, 0, false);
                return string.IsNullOrWhiteSpace(result) || result.StartsWith("Error:", StringComparison.Ordinal)
                    ? string.Empty
                    : result;
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>Turns a filenames_only "path (N matches)" listing into a short "N lines across path, path2" summary.</summary>
        private static string SummarizeProbe(string probeContent)
        {
            var files = new List<string>();
            int totalMatches = 0;
            foreach (var line in probeContent.Split('\n'))
            {
                var m = Regex.Match(line.Trim(), @"^(.*?)\s*\((\d+) matches?\)$");
                if (m.Success)
                {
                    totalMatches += int.Parse(m.Groups[2].Value);
                    files.Add(m.Groups[1].Value);
                }
            }
            return files.Count == 0
                ? "some lines"
                : $"{totalMatches} lines across {string.Join(", ", files.Take(3))}";
        }
    }
}
