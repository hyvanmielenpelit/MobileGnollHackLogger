using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace Overseer.Services.Tools
{
    public class SourceCodeViewTool : IToolHandler
    {
        private readonly SourceCodeService _sourceCodeService;
        private readonly NetHackSourceCodeService _netHackService;
        private readonly int _defaultLineCount;

        public string ToolName => "source_code_view";
        public string Description { get; set; } = "View a section of a GnollHack or NetHack source code file by line range.";
        public ToolExecutionLocation ExecutionLocation => ToolExecutionLocation.Server;
        public ToolCategory Category => ToolCategory.InformationRetrieval;

        public JsonElement ParameterSchema { get; }

        public SourceCodeViewTool(SourceCodeService sourceCodeService, NetHackSourceCodeService netHackService, IConfiguration configuration)
        {
            _sourceCodeService = sourceCodeService;
            _netHackService = netHackService;
            _defaultLineCount = configuration.GetValue<int>("Tools:source_code_view:LineCount", 50);

            ParameterSchema = JsonDocument.Parse(@"
            {
                ""type"": ""object"",
                ""properties"": {
                    ""file"": { ""type"": ""string"", ""description"": ""File path relative to the repository root (e.g., 'src/potion.c')"" },
                    ""start_line"": { ""type"": ""integer"", ""description"": ""Optional. The starting line number to view"" },
                    ""line_count"": { ""type"": ""integer"", ""description"": ""Optional. Number of lines to view (default 50, max 1000)"" },
                    ""search_term"": { ""type"": ""string"", ""description"": ""Optional. Find this term and show context around it (alternative to start_line)"" },
                    ""repository"": {
                        ""type"": ""string"",
                        ""description"": ""Which codebase to view: 'gnollhack' (default) or 'nethack'"",
                        ""enum"": [""gnollhack"", ""nethack""]
                    }
                },
                ""required"": [""file""]
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

            string file = "";
            if (parameters.TryGetProperty("file", out var fileElem))
            {
                file = fileElem.GetString() ?? "";
            }

            if (string.IsNullOrWhiteSpace(file))
            {
                return Task.FromResult(new ToolResult { Success = false, ErrorMessage = "Missing file parameter" });
            }

            if (file.Contains(".."))
            {
                return Task.FromResult(new ToolResult { Success = false, ErrorMessage = "Path traversal is not allowed" });
            }

            if (string.IsNullOrWhiteSpace(service.SourceCodePath) || !Directory.Exists(service.SourceCodePath))
            {
                return Task.FromResult(new ToolResult { Success = false, ErrorMessage = "Source code repository path is not configured or not found." });
            }

            // Verify path is within configured root
            string rootPath = Path.GetFullPath(service.SourceCodePath);
            try 
            {
                string combinedPath = Path.GetFullPath(Path.Combine(rootPath, file));
                if (!combinedPath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(new ToolResult { Success = false, ErrorMessage = "File path is outside of the repository directory" });
                }
            }
            catch (Exception ex)
            {
                return Task.FromResult(new ToolResult { Success = false, ErrorMessage = $"Invalid file path: {ex.Message}" });
            }

            // Check extensions based on mode
            string ext = Path.GetExtension(file).ToLowerInvariant();
            bool isNormalMode = ext == ".c" || ext == ".h" || ext == ".des" || ext == ".txt";
            bool isDebugMode = ext == ".cs" || ext == ".xaml";
            
            if (!isNormalMode && (!isDebugMode || context.OverseerMode != 2 || service is NetHackSourceCodeService))
            {
                 return Task.FromResult(new ToolResult { Success = false, ErrorMessage = $"Access to '{ext}' files is not permitted in the current mode." });
            }

            int? startLine = null;
            if (parameters.TryGetProperty("start_line", out var startLineElem) && startLineElem.ValueKind == JsonValueKind.Number)
            {
                startLine = startLineElem.GetInt32();
            }

            string? searchTerm = null;
            if (parameters.TryGetProperty("search_term", out var searchTermElem))
            {
                searchTerm = searchTermElem.GetString();
            }

            if (startLine == null && string.IsNullOrWhiteSpace(searchTerm))
            {
                return Task.FromResult(new ToolResult { Success = false, ErrorMessage = "Missing start_line or search_term parameter" });
            }

            int lineCount = _defaultLineCount;
            if (parameters.TryGetProperty("line_count", out var lineCountElem) && lineCountElem.ValueKind == JsonValueKind.Number)
            {
                lineCount = lineCountElem.GetInt32();
            }

            var content = service.GetFileExcerpt(file, startLine, lineCount, searchTerm);

            if (context.SpoilerFreeMode)
            {
                content += "\n\n[SPOILER-FREE MODE ACTIVE: Review the spoiler_policy before sharing this information. Only share mechanics, not unrevealed content.]";
            }

            return Task.FromResult(new ToolResult { Success = true, Content = content });
        }
    }
}
