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
                    "description": "Optional. 1-based. Omit, or pass 0, to start at the beginning. To resume after a truncated result pass the output line the notice names, or an absolute file line inside the header's L-range. Any other value returns an explicit error."
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

            if (result.StartsWith(SourceMissContentBuilder.MissPrefix, StringComparison.Ordinal))
            {
                // A requested kind with zero matches falls back to any kind, so a macro carrying a
                // function's name is returned rather than a miss. A kind that did match never gets here.
                if (!kind.Equals("any", StringComparison.OrdinalIgnoreCase))
                {
                    var anyResult = service.GetFunctionBody(name, "any", startLine);
                    if (!anyResult.StartsWith(SourceMissContentBuilder.MissPrefix, StringComparison.Ordinal))
                    {
                        string note = $"[No {kind} named '{name}' in the indexed {repository} source; showing the {DescribeHitKind(anyResult, name)} definition instead.]\n";
                        return Task.FromResult(new ToolResult { Success = true, Content = note + anyResult });
                    }
                }

                result = SourceMissContentBuilder.Build(service, result, name, repository, HitGuidance, NoHitGuidance);
            }

            return Task.FromResult(new ToolResult { Success = true, Content = result });
        }

        /// <summary>
        /// Names the kind of a <see cref="SourceCodeService.GetFunctionBody"/> hit. The body opens with a
        /// "--- path:L…-L… (name, N lines) ---" header and up to two lines of leading context, so the
        /// declaring line is the first one naming the symbol; its first token decides the kind.
        /// </summary>
        private static string DescribeHitKind(string body, string name)
        {
            string? declaring = null;
            string? firstBodyLine = null;

            foreach (var raw in body.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith("--- ", StringComparison.Ordinal) && line.EndsWith("---", StringComparison.Ordinal)) continue;

                firstBodyLine ??= line;
                if (line.Contains(name, StringComparison.Ordinal))
                {
                    declaring = line;
                    break;
                }
            }

            string candidate = declaring ?? firstBodyLine ?? string.Empty;

            if (candidate.StartsWith("#define", StringComparison.Ordinal)) return "macro";
            if (Regex.IsMatch(candidate, @"^(struct|union|enum|typedef)\b")) return "struct";
            return "function";
        }

        private const string HitGuidance = " This tool extracts a function, macro or struct body declared under that exact name."
            + " A struct member, function pointer or macro alias has no body here — use source_code_search on the file named above with context_lines to read it,"
            + " or search_definitions for the symbol it is assigned from.";

        private const string NoHitGuidance = " This tool extracts a function, macro or struct body declared under that exact name."
            + " A struct member, function pointer or macro alias has no body here — use source_code_search with context_lines to find where it is declared,"
            + " or search_definitions for the symbol it is assigned from.";
    }
}
