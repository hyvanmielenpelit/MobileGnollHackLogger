using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Overseer.Services.Tools
{
    public class GetGitHubRepoInfoTool : IToolHandler
    {
        private readonly GitHubApiService _gitHubApiService;

        public string ToolName => "get_github_repo_info";
        public string Description { get; set; } = "Retrieve information about a public GitHub repository — commits, issues, pull requests, releases, or a general summary.";
        public ToolExecutionLocation ExecutionLocation => ToolExecutionLocation.Server;
        public ToolCategory Category => ToolCategory.ExternalLookup;
        public int TimeoutSeconds => 20;

        public JsonElement ParameterSchema { get; }

        public GetGitHubRepoInfoTool(GitHubApiService gitHubApiService)
        {
            _gitHubApiService = gitHubApiService;
            ParameterSchema = JsonDocument.Parse(@"
            {
                ""type"": ""object"",
                ""properties"": {
                    ""owner"": {
                        ""type"": ""string"",
                        ""description"": ""Repository owner (e.g., 'hyvanmielenpelit', 'dotnet', 'mono')""
                    },
                    ""repo"": {
                        ""type"": ""string"",
                        ""description"": ""Repository name (e.g., 'GnollHack', 'maui', 'SkiaSharp')""
                    },
                    ""info_type"": {
                        ""type"": ""string"",
                        ""enum"": [""repo_summary"", ""recent_commits"", ""open_issues"", ""pull_requests"",
                                 ""recent_releases"", ""issue_detail""],
                        ""description"": ""Type of information to retrieve""
                    },
                    ""issue_number"": {
                        ""type"": ""integer"",
                        ""description"": ""Issue or PR number (required when info_type is 'issue_detail')""
                    },
                    ""count"": {
                        ""type"": ""integer"",
                        ""description"": ""Number of items to return (default: 10, max: 30)""
                    },
                    ""label"": {
                        ""type"": ""string"",
                        ""description"": ""Filter issues/PRs by label""
                    },
                    ""state"": {
                        ""type"": ""string"",
                        ""enum"": [""open"", ""closed"", ""all""],
                        ""description"": ""Filter by state (default: 'open' for issues/PRs)""
                    }
                },
                ""required"": [""owner"", ""repo"", ""info_type""]
            }").RootElement;
        }

        public async Task<ToolResult> ExecuteAsync(JsonElement parameters, ToolExecutionContext context, CancellationToken cancellationToken)
        {
            if (!parameters.TryGetProperty("owner", out var ownerProp) || string.IsNullOrWhiteSpace(ownerProp.GetString()))
                return new ToolResult { Success = false, ErrorMessage = "Missing or empty 'owner' parameter." };
            if (!parameters.TryGetProperty("repo", out var repoProp) || string.IsNullOrWhiteSpace(repoProp.GetString()))
                return new ToolResult { Success = false, ErrorMessage = "Missing or empty 'repo' parameter." };
            if (!parameters.TryGetProperty("info_type", out var infoTypeProp) || string.IsNullOrWhiteSpace(infoTypeProp.GetString()))
                return new ToolResult { Success = false, ErrorMessage = "Missing or empty 'info_type' parameter." };

            string owner = ownerProp.GetString()!;
            string repo = repoProp.GetString()!;
            string infoType = infoTypeProp.GetString()!;
            
            int count = parameters.TryGetProperty("count", out var countProp) ? countProp.GetInt32() : 10;
            string state = parameters.TryGetProperty("state", out var stateProp) ? stateProp.GetString() ?? "" : "";
            string label = parameters.TryGetProperty("label", out var labelProp) ? labelProp.GetString() ?? "" : "";
            
            string result = string.Empty;

            switch (infoType)
            {
                case "repo_summary":
                    result = await _gitHubApiService.GetRepoSummaryAsync(owner, repo, cancellationToken);
                    break;
                case "recent_commits":
                    result = await _gitHubApiService.GetCommitsAsync(owner, repo, count, cancellationToken);
                    break;
                case "open_issues":
                    result = await _gitHubApiService.GetIssuesAsync(owner, repo, state, label, count, cancellationToken);
                    break;
                case "pull_requests":
                    result = await _gitHubApiService.GetPullRequestsAsync(owner, repo, state, count, cancellationToken);
                    break;
                case "recent_releases":
                    result = await _gitHubApiService.GetReleasesAsync(owner, repo, count, cancellationToken);
                    break;
                case "issue_detail":
                    if (!parameters.TryGetProperty("issue_number", out var issueNumProp))
                        return new ToolResult { Success = false, ErrorMessage = "Missing 'issue_number' parameter for info_type 'issue_detail'." };
                    
                    int issueNumber = issueNumProp.GetInt32();
                    result = await _gitHubApiService.GetIssueDetailAsync(owner, repo, issueNumber, cancellationToken);
                    break;
                default:
                    return new ToolResult { Success = false, ErrorMessage = $"Unknown info_type: {infoType}" };
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
