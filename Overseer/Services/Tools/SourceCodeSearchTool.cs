using System;
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
        private readonly int _maxResultLength;
        private readonly int _defaultMaxResults;
        private readonly int _defaultContextLines;

        public string ToolName => "source_code_search";
        public string Description { get; set; } = "Search the GnollHack C source code for functions, macros, constants, or game mechanic implementations.";
        public ToolExecutionLocation ExecutionLocation => ToolExecutionLocation.Server;
        public ToolCategory Category => ToolCategory.InformationRetrieval;

        public JsonElement ParameterSchema { get; }

        public SourceCodeSearchTool(SourceCodeService sourceCodeService, IConfiguration configuration)
        {
            _sourceCodeService = sourceCodeService;
            
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
                    ""query"": { ""type"": ""string"", ""description"": ""The search terms to look up in the source code"" },
                    ""file_filter"": { ""type"": ""string"", ""description"": ""Optional. Restrict to a specific file (e.g., 'potion.c')"" },
                    ""max_results"": { ""type"": ""integer"", ""description"": ""Maximum number of files to return matches from (default 10, max 100)"" },
                    ""is_regex"": { ""type"": ""boolean"", ""description"": ""Optional. If true, treat the query as a regular expression"" },
                    ""whole_word"": { ""type"": ""boolean"", ""description"": ""Optional. If true and is_regex is false, search for whole words only"" },
                    ""case_sensitive"": { ""type"": ""boolean"", ""description"": ""Optional. If true, perform a case-sensitive search"" },
                    ""filenames_only"": { ""type"": ""boolean"", ""description"": ""Optional. If true, return only file paths and match counts without code snippets"" },
                    ""context_lines"": { ""type"": ""integer"", ""description"": ""Optional. Number of context lines around each match (default 5, max 25)"" }
                },
                ""required"": [""query""]
            }").RootElement;
        }

        public Task<ToolResult> ExecuteAsync(JsonElement parameters, ToolExecutionContext context, CancellationToken cancellationToken)
        {
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

            bool includeNetCode = context.OverseerMode == 2;

            var content = _sourceCodeService.SearchFiles(query, fileFilter, maxResults, includeNetCode, _maxResultLength, isRegex, filenamesOnly, contextLines, caseSensitive);

            if (string.IsNullOrWhiteSpace(content))
            {
                if (caseSensitive)
                {
                    content = _sourceCodeService.SearchFiles(query, fileFilter, maxResults, includeNetCode, _maxResultLength, isRegex, filenamesOnly, contextLines, false);
                    if (!string.IsNullOrWhiteSpace(content))
                    {
                        content = $"[Note: No exact case match found. Falling back to case-insensitive search.]\n\n" + content;
                    }
                }
                
                if (string.IsNullOrWhiteSpace(content) && isRegex)
                {
                    content = _sourceCodeService.SearchFiles(query, fileFilter, maxResults, includeNetCode, _maxResultLength, false, filenamesOnly, contextLines, false);
                    if (!string.IsNullOrWhiteSpace(content))
                    {
                        content = $"[Note: Regex search failed. Falling back to literal text search.]\n\n" + content;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(content))
            {
                return Task.FromResult(new ToolResult { Success = true, Content = "No relevant source code found." });
            }

            if (context.SpoilerFreeMode)
            {
                content += "\n\n[SPOILER-FREE MODE ACTIVE: Review the spoiler_policy before sharing this information. Only share mechanics, not unrevealed content.]";
            }

            return Task.FromResult(new ToolResult { Success = true, Content = content });
        }
    }
}
