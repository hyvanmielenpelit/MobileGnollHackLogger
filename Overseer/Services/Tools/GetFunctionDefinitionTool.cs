using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Overseer.Services.Tools
{
    public class GetFunctionDefinitionTool : IToolHandler
    {
        private readonly SourceCodeService _sourceCodeService;
        private readonly NetHackSourceCodeService _netHackService;

        public GetFunctionDefinitionTool(SourceCodeService sourceCodeService, NetHackSourceCodeService netHackService)
        {
            _sourceCodeService = sourceCodeService;
            _netHackService = netHackService;
        }

        public string ToolName => "get_function_definition";
        
        public string Description { get; set; } = "Extract the complete body of a C function, macro, or struct from the source code. Returns the entire body by tracking braces.";

        public ToolExecutionLocation ExecutionLocation => ToolExecutionLocation.Server;
        public ToolCategory Category => ToolCategory.InformationRetrieval;
        
        private static readonly JsonElement _parameterSchema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "name": { 
                    "type": "string", 
                    "description": "Function, macro, or struct name" 
                },
                "type": { 
                    "type": "string", 
                    "enum": ["function", "macro", "struct", "any"], 
                    "description": "Optional (default 'any')" 
                },
                "start_line": {
                    "type": "integer",
                    "description": "Optional. Where to resume after a truncated result: the output line number printed in the truncation notice (1-based within the previous output), or an absolute file line inside the definition's L-range shown in its header. Not a line count."
                },
                "repository": {
                    "type": "string",
                    "description": "Which codebase to extract from: 'gnollhack' (default) or 'nethack'",
                    "enum": ["gnollhack", "nethack"]
                }
            },
            "required": ["name"]
        }
        """).RootElement.Clone();

        public JsonElement ParameterSchema => _parameterSchema;

        private SourceCodeService ResolveService(JsonElement parameters, out string guardMessage, out string repository)
        {
            if (parameters.TryGetProperty("repository", out var repo) &&
                repo.GetString()?.Equals("nethack", StringComparison.OrdinalIgnoreCase) == true)
            {
                guardMessage = ToolGuardMessages.NetHackSourceCodeIndexingInProgress;
                repository = "nethack";
                return _netHackService;
            }

            guardMessage = ToolGuardMessages.SourceCodeIndexingInProgress;
            repository = "gnollhack";
            return _sourceCodeService;
        }

        public Task<ToolResult> ExecuteAsync(JsonElement arguments, ToolExecutionContext context, System.Threading.CancellationToken cancellationToken)
        {
            var service = ResolveService(arguments, out var guardMessage, out var repository);
            if (!service.IsIndexingComplete)
            {
                return Task.FromResult(new ToolResult { Success = false, ErrorMessage = guardMessage });
            }

            string name = arguments.GetProperty("name").GetString() ?? string.Empty;
            string kind = "any";
            
            if (arguments.TryGetProperty("type", out var typeElement) && typeElement.ValueKind == JsonValueKind.String)
            {
                kind = typeElement.GetString() ?? "any";
            }
            
            int? startLine = null;
            if (arguments.TryGetProperty("start_line", out var startLineElement) && startLineElement.ValueKind == JsonValueKind.Number)
            {
                startLine = startLineElement.GetInt32();
            }

            var result = service.GetFunctionBody(name, kind, startLine);

            if (result.StartsWith(MissPrefix, StringComparison.Ordinal))
            {
                result = BuildMissContent(service, result, name, repository);
            }

            return Task.FromResult(new ToolResult { Success = true, Content = result });
        }

        private const string MissPrefix = "No definition found for '";
        private const int ProbeMaxResults = 3;
        private const int ProbeMaxResultLength = 1000;
        private const int MaxMissContentLength = 600;

        private const string HitGuidance = " This tool extracts a function, macro or struct body declared under that exact name."
            + " A struct member, function pointer or macro alias has no body here — use source_code_search on the file named above with context_lines to read it,"
            + " or search_definitions for the symbol it is assigned from.";

        private const string NoHitGuidance = " This tool extracts a function, macro or struct body declared under that exact name."
            + " A struct member, function pointer or macro alias has no body here — use source_code_search with context_lines to find where it is declared,"
            + " or search_definitions for the symbol it is assigned from.";

        /// <summary>
        /// Extends the service's own "no definition found" sentence with where the name does occur in
        /// the indexed source — or that it occurs nowhere — and why a struct member, function pointer
        /// or macro alias has no body to extract. Capped in length and never throws: on any failure the
        /// service's sentence is returned unchanged.
        /// </summary>
        private static string BuildMissContent(SourceCodeService service, string plainMiss, string name, string repository)
        {
            try
            {
                string probe = SafeProbe(service, name);
                bool hit = !string.IsNullOrWhiteSpace(probe);

                var sb = new StringBuilder(plainMiss);
                sb.Append(hit
                    ? $" '{name}' occurs in {SummarizeProbe(probe)}."
                    : $" '{name}' does not occur in the indexed {repository} source.");
                sb.Append(hit ? HitGuidance : NoHitGuidance);

                // The guidance is the part worth paying for, so an oversized payload loses the probe detail first.
                string content = sb.ToString();
                if (content.Length > MaxMissContentLength)
                {
                    content = Truncate(plainMiss + (hit ? HitGuidance : NoHitGuidance));
                }

                return content;
            }
            catch
            {
                return plainMiss;
            }
        }

        private static string Truncate(string content)
        {
            if (content.Length <= MaxMissContentLength) return content;

            int cut = content.LastIndexOf(' ', MaxMissContentLength - 1);
            if (cut < MaxMissContentLength / 2) cut = MaxMissContentLength - 1;
            return content.Substring(0, cut).TrimEnd() + "…";
        }

        /// <summary>Runs one bounded filenames_only occurrence probe, swallowing errors and "Error:"-prefixed content.</summary>
        private static string SafeProbe(SourceCodeService service, string probeQuery)
        {
            try
            {
                var result = service.SearchFiles(probeQuery, fileFilter: "", maxResults: ProbeMaxResults,
                    includeNetCode: false, maxResultLength: ProbeMaxResultLength, isRegex: false,
                    filenamesOnly: true, contextLines: 0, caseSensitive: false);

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
