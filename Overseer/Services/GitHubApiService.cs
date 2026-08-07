using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Overseer.Services
{
    public class GitHubApiService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly IMemoryCache _cache;
        private readonly ILogger<GitHubApiService> _logger;

        public GitHubApiService(
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            IMemoryCache cache,
            ILogger<GitHubApiService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _cache = cache;
            _logger = logger;
        }

        private HttpClient CreateClient()
        {
            var client = _httpClientFactory.CreateClient("GitHub");
            
            var token = _configuration["GitHub:PersonalAccessToken"];
            if (!string.IsNullOrWhiteSpace(token))
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
            }

            return client;
        }

        private async Task<string> FetchFromGitHubAsync(string url, CancellationToken ct)
        {
            if (_cache.TryGetValue(url, out string? cachedResult) && cachedResult != null)
            {
                return cachedResult;
            }

            var client = CreateClient();
            try
            {
                var response = await client.GetAsync(url, ct);
                
                string rateLimitRemaining = "Unknown";
                if (response.Headers.TryGetValues("X-RateLimit-Remaining", out var remainingValues))
                {
                    rateLimitRemaining = string.Join(", ", remainingValues);
                }

                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode == System.Net.HttpStatusCode.Forbidden && rateLimitRemaining == "0")
                    {
                        return $"Error: GitHub API rate limit exceeded. [GitHub API: 0 requests remaining this hour]";
                    }
                    if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        return $"Error: Repository or resource not found. [GitHub API: {rateLimitRemaining} requests remaining this hour]";
                    }
                    return $"Error: GitHub API returned {(int)response.StatusCode} {response.ReasonPhrase}. [GitHub API: {rateLimitRemaining} requests remaining this hour]";
                }

                var content = await response.Content.ReadAsStringAsync(ct);
                var formattedResult = $"{content}\n\n[GitHub API: {rateLimitRemaining} requests remaining this hour]";

                var cacheOptions = new MemoryCacheEntryOptions()
                    .SetSlidingExpiration(TimeSpan.FromMinutes(5))
                    .SetSize(1); // CRITICAL: SizeLimit is configured globally
                
                _cache.Set(url, formattedResult, cacheOptions);

                return formattedResult;
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("GitHub API call to {Url} was canceled or timed out.", url);
                return $"Error: The GitHub API request timed out or was canceled. The API may be slow or the tool execution time limit was reached.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling GitHub API at {Url}", url);
                return $"Error: An exception occurred while contacting the GitHub API ({ex.Message}).";
            }
        }

        private string ExtractRateLimitInfo(string formattedResult)
        {
            // The rate limit info is already embedded at the bottom of formattedResult by FetchFromGitHubAsync.
            // We just need to append it again if we process the JSON, or better yet, return the raw rate limit suffix.
            int index = formattedResult.LastIndexOf("\n\n[GitHub API:", StringComparison.Ordinal);
            if (index >= 0)
            {
                return formattedResult.Substring(index).Trim();
            }
            return "[GitHub API: Unknown requests remaining this hour]";
        }

        private string StripRateLimitInfo(string formattedResult)
        {
            int index = formattedResult.LastIndexOf("\n\n[GitHub API:", StringComparison.Ordinal);
            if (index >= 0)
            {
                return formattedResult.Substring(0, index);
            }
            return formattedResult;
        }

        public async Task<string> GetRepoSummaryAsync(string owner, string repo, CancellationToken ct)
        {
            string url = $"repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}";
            string rawResult = await FetchFromGitHubAsync(url, ct);
            if (rawResult.StartsWith("Error:")) return rawResult;

            var rateLimit = ExtractRateLimitInfo(rawResult);
            var json = StripRateLimitInfo(rawResult);

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                
                var desc = root.TryGetProperty("description", out var descProp) ? descProp.GetString() : "";
                var stars = root.TryGetProperty("stargazers_count", out var starsProp) ? starsProp.GetInt32() : 0;
                var openIssues = root.TryGetProperty("open_issues_count", out var issuesProp) ? issuesProp.GetInt32() : 0;
                var forks = root.TryGetProperty("forks_count", out var forksProp) ? forksProp.GetInt32() : 0;
                var updatedAt = root.TryGetProperty("updated_at", out var updatedProp) ? updatedProp.GetString() : "";

                return $"Repository: {owner}/{repo}\nDescription: {desc}\nStars: {stars} | Forks: {forks} | Open Issues/PRs: {openIssues}\nLast Updated: {updatedAt}\n\n{rateLimit}";
            }
            catch (JsonException)
            {
                return $"Error parsing repository summary.\n\n{rateLimit}";
            }
        }

        public async Task<string> GetCommitsAsync(string owner, string repo, int count, CancellationToken ct)
        {
            count = Math.Clamp(count, 1, 30);
            string url = $"repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/commits?per_page={count}";
            string rawResult = await FetchFromGitHubAsync(url, ct);
            if (rawResult.StartsWith("Error:")) return rawResult;

            var rateLimit = ExtractRateLimitInfo(rawResult);
            var json = StripRateLimitInfo(rawResult);

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Array) return $"Unexpected response format.\n\n{rateLimit}";

                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"Recent commits in {owner}/{repo} (last {Math.Min(count, root.GetArrayLength())}):\n");

                int i = 1;
                foreach (var item in root.EnumerateArray())
                {
                    if (i > count) break;
                    var sha = item.TryGetProperty("sha", out var shaProp) ? shaProp.GetString()?.Substring(0, 7) : "";
                    
                    var commitObj = item.TryGetProperty("commit", out var c) ? c : default;
                    var message = commitObj.ValueKind == JsonValueKind.Object && commitObj.TryGetProperty("message", out var mProp) ? mProp.GetString() : "";
                    // Only take the first line of the commit message
                    if (!string.IsNullOrEmpty(message))
                    {
                        int nl = message.IndexOf('\n');
                        if (nl > 0) message = message.Substring(0, nl);
                    }

                    var authorObj = commitObj.ValueKind == JsonValueKind.Object && commitObj.TryGetProperty("author", out var a) ? a : default;
                    var authorName = authorObj.ValueKind == JsonValueKind.Object && authorObj.TryGetProperty("name", out var anProp) ? anProp.GetString() : "Unknown";
                    var date = authorObj.ValueKind == JsonValueKind.Object && authorObj.TryGetProperty("date", out var dProp) ? dProp.GetString() : "Unknown";

                    sb.AppendLine($"{i}. {sha} — {message} ({authorName}, {date})");
                    i++;
                }

                sb.AppendLine($"\n{rateLimit}");
                return sb.ToString();
            }
            catch (JsonException)
            {
                return $"Error parsing commits.\n\n{rateLimit}";
            }
        }

        public async Task<string> GetIssuesAsync(string owner, string repo, string state, string? label, int count, CancellationToken ct)
        {
            count = Math.Clamp(count, 1, 30);
            state = string.IsNullOrWhiteSpace(state) ? "open" : state.ToLowerInvariant();
            
            string url = $"repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/issues?state={Uri.EscapeDataString(state)}&per_page={count}";
            if (!string.IsNullOrWhiteSpace(label))
            {
                url += $"&labels={Uri.EscapeDataString(label)}";
            }

            string rawResult = await FetchFromGitHubAsync(url, ct);
            if (rawResult.StartsWith("Error:")) return rawResult;

            var rateLimit = ExtractRateLimitInfo(rawResult);
            var json = StripRateLimitInfo(rawResult);

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Array) return $"Unexpected response format.\n\n{rateLimit}";

                var sb = new System.Text.StringBuilder();
                string labelFilter = string.IsNullOrWhiteSpace(label) ? "" : $" with label '{label}'";
                sb.AppendLine($"{char.ToUpper(state[0])}{state.Substring(1)} issues/PRs in {owner}/{repo}{labelFilter} (last {Math.Min(count, root.GetArrayLength())}):\n");

                int i = 1;
                foreach (var item in root.EnumerateArray())
                {
                    if (i > count) break;
                    
                    var number = item.TryGetProperty("number", out var numProp) ? numProp.GetInt32() : 0;
                    var title = item.TryGetProperty("title", out var titleProp) ? titleProp.GetString() : "";
                    
                    var userObj = item.TryGetProperty("user", out var u) ? u : default;
                    var author = userObj.ValueKind == JsonValueKind.Object && userObj.TryGetProperty("login", out var lProp) ? lProp.GetString() : "Unknown";
                    
                    var createdAt = item.TryGetProperty("created_at", out var createdProp) ? createdProp.GetString() : "Unknown";
                    var comments = item.TryGetProperty("comments", out var commentsProp) ? commentsProp.GetInt32() : 0;
                    var isPr = item.TryGetProperty("pull_request", out _) ? " (PR)" : "";

                    sb.AppendLine($"#{number}{isPr}: {title}");
                    sb.AppendLine($"  Author: {author} | Created: {createdAt} | Comments: {comments}");
                    
                    if (item.TryGetProperty("labels", out var labelsArray) && labelsArray.ValueKind == JsonValueKind.Array)
                    {
                        var labels = new System.Collections.Generic.List<string>();
                        foreach (var lbl in labelsArray.EnumerateArray())
                        {
                            if (lbl.TryGetProperty("name", out var nameProp))
                                labels.Add(nameProp.GetString() ?? "");
                        }
                        if (labels.Count > 0)
                        {
                            sb.AppendLine($"  Labels: {string.Join(", ", labels)}");
                        }
                    }
                    sb.AppendLine();
                    i++;
                }

                if (root.GetArrayLength() == 0) sb.AppendLine("No issues found.");

                sb.AppendLine(rateLimit);
                return sb.ToString();
            }
            catch (JsonException)
            {
                return $"Error parsing issues.\n\n{rateLimit}";
            }
        }

        public async Task<string> GetIssueDetailAsync(string owner, string repo, int issueNumber, CancellationToken ct)
        {
            string url = $"repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/issues/{issueNumber}";
            string rawResult = await FetchFromGitHubAsync(url, ct);
            if (rawResult.StartsWith("Error:")) return rawResult;

            var rateLimit = ExtractRateLimitInfo(rawResult);
            var json = StripRateLimitInfo(rawResult);

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                
                var title = root.TryGetProperty("title", out var titleProp) ? titleProp.GetString() : "";
                var state = root.TryGetProperty("state", out var stateProp) ? stateProp.GetString() : "unknown";
                
                var userObj = root.TryGetProperty("user", out var u) ? u : default;
                var author = userObj.ValueKind == JsonValueKind.Object && userObj.TryGetProperty("login", out var lProp) ? lProp.GetString() : "Unknown";
                
                var createdAt = root.TryGetProperty("created_at", out var createdProp) ? createdProp.GetString() : "Unknown";
                var commentsCount = root.TryGetProperty("comments", out var commentsProp) ? commentsProp.GetInt32() : 0;
                var body = root.TryGetProperty("body", out var bodyProp) ? bodyProp.GetString() : "(No description)";
                var isPr = root.TryGetProperty("pull_request", out _) ? "Pull Request" : "Issue";

                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"{isPr} #{issueNumber}: {title}");
                sb.AppendLine($"State: {state} | Author: {author} | Created: {createdAt} | Comments: {commentsCount}");
                
                if (root.TryGetProperty("labels", out var labelsArray) && labelsArray.ValueKind == JsonValueKind.Array)
                {
                    var labels = new System.Collections.Generic.List<string>();
                    foreach (var lbl in labelsArray.EnumerateArray())
                    {
                        if (lbl.TryGetProperty("name", out var nameProp))
                            labels.Add(nameProp.GetString() ?? "");
                    }
                    if (labels.Count > 0)
                    {
                        sb.AppendLine($"Labels: {string.Join(", ", labels)}");
                    }
                }
                
                sb.AppendLine("\nDescription:");
                sb.AppendLine(body);

                if (commentsCount > 0)
                {
                    string commentsUrl = $"{url}/comments?per_page=30";
                    string commentsRaw = await FetchFromGitHubAsync(commentsUrl, ct);
                    if (!commentsRaw.StartsWith("Error:"))
                    {
                        var commentsJson = StripRateLimitInfo(commentsRaw);
                        using var commentsDoc = JsonDocument.Parse(commentsJson);
                        var commentsRoot = commentsDoc.RootElement;
                        if (commentsRoot.ValueKind == JsonValueKind.Array)
                        {
                            sb.AppendLine("\n--- Comments ---\n");
                            foreach (var comment in commentsRoot.EnumerateArray())
                            {
                                var cUser = comment.TryGetProperty("user", out var cu) ? cu : default;
                                var cAuthor = cUser.ValueKind == JsonValueKind.Object && cUser.TryGetProperty("login", out var clProp) ? clProp.GetString() : "Unknown";
                                var cCreatedAt = comment.TryGetProperty("created_at", out var cCreatedProp) ? cCreatedProp.GetString() : "Unknown";
                                var cBody = comment.TryGetProperty("body", out var cbProp) ? cbProp.GetString() : "";

                                sb.AppendLine($"[{cAuthor} at {cCreatedAt}]:");
                                sb.AppendLine(cBody);
                                sb.AppendLine();
                            }
                        }
                    }
                }

                sb.AppendLine($"\n{rateLimit}");
                return sb.ToString();
            }
            catch (JsonException)
            {
                return $"Error parsing issue detail.\n\n{rateLimit}";
            }
        }

        public async Task<string> GetPullRequestsAsync(string owner, string repo, string state, int count, CancellationToken ct)
        {
            count = Math.Clamp(count, 1, 30);
            state = string.IsNullOrWhiteSpace(state) ? "open" : state.ToLowerInvariant();
            
            string url = $"repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/pulls?state={Uri.EscapeDataString(state)}&per_page={count}";
            string rawResult = await FetchFromGitHubAsync(url, ct);
            if (rawResult.StartsWith("Error:")) return rawResult;

            var rateLimit = ExtractRateLimitInfo(rawResult);
            var json = StripRateLimitInfo(rawResult);

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Array) return $"Unexpected response format.\n\n{rateLimit}";

                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"{char.ToUpper(state[0])}{state.Substring(1)} pull requests in {owner}/{repo} (last {Math.Min(count, root.GetArrayLength())}):\n");

                int i = 1;
                foreach (var item in root.EnumerateArray())
                {
                    if (i > count) break;
                    
                    var number = item.TryGetProperty("number", out var numProp) ? numProp.GetInt32() : 0;
                    var title = item.TryGetProperty("title", out var titleProp) ? titleProp.GetString() : "";
                    
                    var userObj = item.TryGetProperty("user", out var u) ? u : default;
                    var author = userObj.ValueKind == JsonValueKind.Object && userObj.TryGetProperty("login", out var lProp) ? lProp.GetString() : "Unknown";
                    
                    var createdAt = item.TryGetProperty("created_at", out var createdProp) ? createdProp.GetString() : "Unknown";

                    sb.AppendLine($"#{number}: {title}");
                    sb.AppendLine($"  Author: {author} | Created: {createdAt}\n");
                    i++;
                }

                if (root.GetArrayLength() == 0) sb.AppendLine("No pull requests found.");

                sb.AppendLine(rateLimit);
                return sb.ToString();
            }
            catch (JsonException)
            {
                return $"Error parsing pull requests.\n\n{rateLimit}";
            }
        }

        public async Task<string> GetReleasesAsync(string owner, string repo, int count, CancellationToken ct)
        {
            count = Math.Clamp(count, 1, 30);
            string url = $"repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/releases?per_page={count}";
            string rawResult = await FetchFromGitHubAsync(url, ct);
            if (rawResult.StartsWith("Error:")) return rawResult;

            var rateLimit = ExtractRateLimitInfo(rawResult);
            var json = StripRateLimitInfo(rawResult);

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Array) return $"Unexpected response format.\n\n{rateLimit}";

                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"Recent releases in {owner}/{repo} (last {Math.Min(count, root.GetArrayLength())}):\n");

                int i = 1;
                foreach (var item in root.EnumerateArray())
                {
                    if (i > count) break;
                    
                    var name = item.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : "";
                    var tagName = item.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() : "";
                    var publishedAt = item.TryGetProperty("published_at", out var pubProp) ? pubProp.GetString() : "Unknown";
                    var isPrerelease = item.TryGetProperty("prerelease", out var preProp) && preProp.GetBoolean();
                    var body = item.TryGetProperty("body", out var bodyProp) ? bodyProp.GetString() : "";

                    string typeStr = isPrerelease ? "[Pre-release]" : "[Release]";
                    
                    sb.AppendLine($"{typeStr} {tagName} ({name}) — Published: {publishedAt}");
                    if (!string.IsNullOrEmpty(body))
                    {
                        var lines = body.Split('\n');
                        for (int j = 0; j < Math.Min(lines.Length, 5); j++)
                        {
                            sb.AppendLine("  " + lines[j].TrimEnd());
                        }
                        if (lines.Length > 5) sb.AppendLine("  ...");
                    }
                    sb.AppendLine();
                    i++;
                }

                if (root.GetArrayLength() == 0) sb.AppendLine("No releases found.");

                sb.AppendLine(rateLimit);
                return sb.ToString();
            }
            catch (JsonException)
            {
                return $"Error parsing releases.\n\n{rateLimit}";
            }
        }

        public async Task<string> SearchIssuesAsync(string query, string? repoFilter, string? stateFilter, string? sort, int count, CancellationToken ct)
        {
            count = Math.Clamp(count, 1, 30);
            
            var searchQuery = query;
            if (!string.IsNullOrWhiteSpace(repoFilter))
                searchQuery += $" repo:{repoFilter}";
            if (!string.IsNullOrWhiteSpace(stateFilter))
                searchQuery += $" state:{stateFilter}";

            string url = $"search/issues?q={Uri.EscapeDataString(searchQuery)}&per_page={count}";
            if (!string.IsNullOrWhiteSpace(sort))
                url += $"&sort={Uri.EscapeDataString(sort)}";

            string rawResult = await FetchFromGitHubAsync(url, ct);
            if (rawResult.StartsWith("Error:")) return rawResult;

            var rateLimit = ExtractRateLimitInfo(rawResult);
            var json = StripRateLimitInfo(rawResult);

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var totalCount = root.TryGetProperty("total_count", out var tProp) ? tProp.GetInt32() : 0;
                
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"Search Issues/PRs for '{query}' (Found {totalCount}, showing top {Math.Min(count, totalCount)}):\n");

                if (root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
                {
                    int i = 1;
                    foreach (var item in items.EnumerateArray())
                    {
                        if (i > count) break;
                        
                        var repoUrl = item.TryGetProperty("repository_url", out var rProp) ? rProp.GetString() : "";
                        var repoName = repoUrl?.Substring(repoUrl.LastIndexOf("repos/") + 6);
                        
                        var number = item.TryGetProperty("number", out var numProp) ? numProp.GetInt32() : 0;
                        var title = item.TryGetProperty("title", out var titleProp) ? titleProp.GetString() : "";
                        var state = item.TryGetProperty("state", out var stateProp) ? stateProp.GetString() : "unknown";
                        var isPr = item.TryGetProperty("pull_request", out _) ? " (PR)" : "";

                        sb.AppendLine($"{i}. {repoName} #{number}{isPr}: {title} [{state}]");
                        i++;
                    }
                }

                if (totalCount == 0) sb.AppendLine("No results found.");

                sb.AppendLine($"\n{rateLimit}");
                return sb.ToString();
            }
            catch (JsonException)
            {
                return $"Error parsing search results.\n\n{rateLimit}";
            }
        }

        public async Task<string> SearchCommitsAsync(string query, string? repoFilter, string? sort, int count, CancellationToken ct)
        {
            count = Math.Clamp(count, 1, 30);
            
            var searchQuery = query;
            if (!string.IsNullOrWhiteSpace(repoFilter))
                searchQuery += $" repo:{repoFilter}";

            string url = $"search/commits?q={Uri.EscapeDataString(searchQuery)}&per_page={count}";
            if (!string.IsNullOrWhiteSpace(sort))
                url += $"&sort={Uri.EscapeDataString(sort)}";

            string rawResult = await FetchFromGitHubAsync(url, ct);
            if (rawResult.StartsWith("Error:")) return rawResult;

            var rateLimit = ExtractRateLimitInfo(rawResult);
            var json = StripRateLimitInfo(rawResult);

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var totalCount = root.TryGetProperty("total_count", out var tProp) ? tProp.GetInt32() : 0;
                
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"Search Commits for '{query}' (Found {totalCount}, showing top {Math.Min(count, totalCount)}):\n");

                if (root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
                {
                    int i = 1;
                    foreach (var item in items.EnumerateArray())
                    {
                        if (i > count) break;
                        
                        var repoObj = item.TryGetProperty("repository", out var ro) ? ro : default;
                        var repoName = repoObj.ValueKind == JsonValueKind.Object && repoObj.TryGetProperty("full_name", out var rnProp) ? rnProp.GetString() : "unknown";
                        
                        var sha = item.TryGetProperty("sha", out var shaProp) ? shaProp.GetString()?.Substring(0, 7) : "";
                        
                        var commitObj = item.TryGetProperty("commit", out var c) ? c : default;
                        var message = commitObj.ValueKind == JsonValueKind.Object && commitObj.TryGetProperty("message", out var mProp) ? mProp.GetString() : "";
                        if (!string.IsNullOrEmpty(message))
                        {
                            int nl = message.IndexOf('\n');
                            if (nl > 0) message = message.Substring(0, nl);
                        }

                        sb.AppendLine($"{i}. {repoName} @ {sha} — {message}");
                        i++;
                    }
                }

                if (totalCount == 0) sb.AppendLine("No results found.");

                sb.AppendLine($"\n{rateLimit}");
                return sb.ToString();
            }
            catch (JsonException)
            {
                return $"Error parsing commit search results.\n\n{rateLimit}";
            }
        }
    }
}
