using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Overseer.Services.Tools
{
    public class GetConstantsTool : IToolHandler
    {
        private readonly SourceCodeService _sourceCodeService;

        public string ToolName => "get_constants";
        public string Description { get; set; } = "Look up #define constants and enum values from the GnollHack source code.";
        public ToolExecutionLocation ExecutionLocation => ToolExecutionLocation.Server;
        public ToolCategory Category => ToolCategory.InformationRetrieval;

        public JsonElement ParameterSchema { get; }

        public GetConstantsTool(SourceCodeService sourceCodeService)
        {
            _sourceCodeService = sourceCodeService;
            ParameterSchema = JsonDocument.Parse(@"
            {
                ""type"": ""object"",
                ""properties"": {
                    ""name"": { ""type"": ""string"", ""description"": ""Constant name or wildcard pattern (e.g., 'PM_GNOLL', 'AD_*', 'WAN_*')"" },
                    ""prefix_filter"": { ""type"": ""string"", ""description"": ""Optional. Filter by prefix (e.g., 'PM_', 'AD_', 'WAN_', 'ART_')"" }
                },
                ""required"": [""name""]
            }").RootElement;
        }

        public Task<ToolResult> ExecuteAsync(JsonElement parameters, ToolExecutionContext context, CancellationToken cancellationToken)
        {
            string namePattern = "";
            if (parameters.TryGetProperty("name", out var nameElem))
            {
                namePattern = nameElem.GetString() ?? "";
            }

            string? prefixFilter = null;
            if (parameters.TryGetProperty("prefix_filter", out var prefixElem))
            {
                prefixFilter = prefixElem.GetString();
            }

            if (string.IsNullOrWhiteSpace(namePattern) && string.IsNullOrWhiteSpace(prefixFilter))
            {
                return Task.FromResult(new ToolResult { Success = false, ErrorMessage = "Missing name or prefix_filter parameter" });
            }

            var constants = _sourceCodeService.GetConstants(namePattern, prefixFilter).ToList();
            
            if (!constants.Any())
            {
                return Task.FromResult(new ToolResult { Success = true, Content = "No constants found matching the specified criteria." });
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Found {constants.Count} matching constants:");
            sb.AppendLine("```c");
            foreach (var c in constants)
            {
                sb.AppendLine($"#define {c.Name} {c.Value} // {c.FilePath}:{c.LineNumber}");
            }
            sb.AppendLine("```");

            return Task.FromResult(new ToolResult { Success = true, Content = sb.ToString() });
        }
    }
}
