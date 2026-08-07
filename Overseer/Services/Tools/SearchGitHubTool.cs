using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Overseer.Services.Tools
{
    public class SearchGitHubTool : IToolHandler
    {
        private readonly GitHubApiService _gitHubApiService;

        public string ToolName => "search_github";
        public string Description { get; set; } = "Search across GitHub for issues, pull requests, or commits matching a query.";
        public ToolExecutionLocation ExecutionLocation => ToolExecutionLocation.Server;
        public ToolCategory Category => ToolCategory.ExternalLookup;
        public int TimeoutSeconds => 20;

        public JsonElement ParameterSchema { get; }

        public SearchGitHubTool(GitHubApiService gitHubApiService)
        {
            _gitHubApiService = gitHubApiService;
            ParameterSchema = JsonDocument.Parse(@"
            {
                ""type"": ""object"",
                ""properties"": {
                    ""query"": {
                        ""type"": ""string"",
                        ""description"": ""Search query (e.g., 'SkiaSharp crash Android')""
                    },
                    ""search_type"": {
                        ""type"": ""string"",
                        ""enum"": [""issues"", ""commits""],
                        ""description"": ""Type of GitHub content to search""
                    },
                    ""repo_filter"": {
                        ""type"": ""string"",
                        ""description"": ""Limit search to a specific repo (e.g., 'dotnet/maui')""
                    },
                    ""state_filter"": {
                        ""type"": ""string"",
                        ""enum"": [""open"", ""closed""],
                        ""description"": ""Filter issues by state""
                    },
                    ""sort"": {
                        ""type"": ""string"",
                        ""enum"": [""created"", ""updated"", ""comments"", ""reactions""],
                        ""description"": ""Sort order for results (default: 'updated')""
                    },
                    ""count"": {
                        ""type"": ""integer"",
                        ""description"": ""Number of results to return (default: 10, max: 30)""
                    }
                },
                ""required"": [""query"", ""search_type""]
            }").RootElement;
        }

        public async Task<ToolResult> ExecuteAsync(JsonElement parameters, ToolExecutionContext context, CancellationToken cancellationToken)
        {
            if (!parameters.TryGetProperty("query", out var queryProp) || string.IsNullOrWhiteSpace(queryProp.GetString()))
                return new ToolResult { Success = false, ErrorMessage = "Missing or empty 'query' parameter." };
            if (!parameters.TryGetProperty("search_type", out var typeProp) || string.IsNullOrWhiteSpace(typeProp.GetString()))
                return new ToolResult { Success = false, ErrorMessage = "Missing or empty 'search_type' parameter." };

            string query = queryProp.GetString()!;
            string searchType = typeProp.GetString()!;
            
            string repoFilter = parameters.TryGetProperty("repo_filter", out var repoProp) ? repoProp.GetString() ?? "" : "";
            string stateFilter = parameters.TryGetProperty("state_filter", out var stateProp) ? stateProp.GetString() ?? "" : "";
            string sort = parameters.TryGetProperty("sort", out var sortProp) ? sortProp.GetString() ?? "" : "";
            int count = parameters.TryGetProperty("count", out var countProp) ? countProp.GetInt32() : 10;
            
            string result = string.Empty;

            if (searchType == "issues")
            {
                result = await _gitHubApiService.SearchIssuesAsync(query, repoFilter, stateFilter, sort, count, cancellationToken);
            }
            else if (searchType == "commits")
            {
                result = await _gitHubApiService.SearchCommitsAsync(query, repoFilter, sort, count, cancellationToken);
            }
            else
            {
                return new ToolResult { Success = false, ErrorMessage = $"Unknown search_type: {searchType}" };
            }

            return new ToolResult
            {
                Success = !result.StartsWith("Error:"),
                Content = result,
                ErrorMessage = result.StartsWith("Error:") ? result : null
            };
        }
    }
}
