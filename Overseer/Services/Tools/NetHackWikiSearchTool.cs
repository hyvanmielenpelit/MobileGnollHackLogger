using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;

namespace Overseer.Services.Tools
{
    [Obsolete("NetHackWikiSearchTool is disabled because nethackwiki.com is protected by Cloudflare WAF/Bot Management, which blocks automated backend HTTP requests with HTTP 403 Forbidden. Use local wiki tools (wiki_search, wiki_view), monster/item stats tools, or provider native web search instead.")]
    public class NetHackWikiSearchTool : IToolHandler
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IMemoryCache _cache;
        private readonly int _maxCallsPerMinute;
        private readonly int _maxResults;
        private readonly int _cacheMinutes;

        public string ToolName => "nethack_wiki_search";
        public string Description { get; set; } = "Search the general NetHack wiki for mechanics, monsters, and items not specific to GnollHack.";
        public ToolExecutionLocation ExecutionLocation => ToolExecutionLocation.Server;
        public ToolCategory Category => ToolCategory.ExternalLookup;

        public JsonElement ParameterSchema { get; }

        public NetHackWikiSearchTool(IHttpClientFactory httpClientFactory, IMemoryCache cache, IConfiguration configuration)
        {
            _httpClientFactory = httpClientFactory;
            _cache = cache;
            _maxCallsPerMinute = configuration.GetValue<int>("Tools:nethack_wiki_search:MaxCallsPerMinute", 10);
            _maxResults = configuration.GetValue<int>("Tools:nethack_wiki_search:MaxResults", 3);
            _cacheMinutes = configuration.GetValue<int>("Tools:nethack_wiki_search:CacheMinutes", 60);
            ParameterSchema = JsonDocument.Parse(@"
            {
                ""type"": ""object"",
                ""properties"": {
                    ""query"": { ""type"": ""string"", ""description"": ""The search terms to look up in the NetHack wiki"" },
                    ""max_results"": { ""type"": ""integer"", ""description"": ""Optional. Maximum number of articles to return (default 1, max 3)"" }
                },
                ""required"": [""query""]
            }").RootElement;
        }

        public async Task<ToolResult> ExecuteAsync(JsonElement parameters, ToolExecutionContext context, CancellationToken cancellationToken)
        {
            string query = "";
            if (parameters.TryGetProperty("query", out var queryElem))
            {
                query = queryElem.GetString() ?? "";
            }

            if (string.IsNullOrWhiteSpace(query))
            {
                return new ToolResult { Success = false, ErrorMessage = "Missing query parameter" };
            }

            int maxResults = 1;
            if (parameters.TryGetProperty("max_results", out var maxResElem) && maxResElem.ValueKind == JsonValueKind.Number)
            {
                maxResults = Math.Clamp(maxResElem.GetInt32(), 1, _maxResults);
            }

            var rateLimitKey = $"nhwiki_rate_{context.SessionId}";
            var currentCalls = _cache.GetOrCreate(rateLimitKey, entry =>
            {
                entry.Size = 1;
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1);
                return 0;
            });

            if (currentCalls >= _maxCallsPerMinute)
            {
                return new ToolResult { Success = false, ErrorMessage = "Rate limit exceeded. Please wait a minute before querying the external wiki again." };
            }

            _cache.Set(rateLimitKey, currentCalls + 1, new MemoryCacheEntryOptions
            {
                Size = 1,
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1)
            });

            var cacheKey = $"nhwiki_result_{query}_{maxResults}";
            if (_cache.TryGetValue(cacheKey, out string? cachedResult) && !string.IsNullOrEmpty(cachedResult))
            {
                return new ToolResult { Success = true, Content = cachedResult };
            }

            try
            {
                var client = _httpClientFactory.CreateClient("NetHackWiki");

                var searchUrl = $"https://nethackwiki.com/api.php?action=query&list=search&srsearch={Uri.EscapeDataString(query)}&format=json";
                var searchResponse = await client.GetAsync(searchUrl, cancellationToken);

                if (!searchResponse.IsSuccessStatusCode)
                {
                    int statusCode = (int)searchResponse.StatusCode;
                    if (searchResponse.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    {
                        return new ToolResult { Success = false, ErrorMessage = "Error: NetHack Wiki rate limit exceeded (429). Please wait a moment before querying the wiki again." };
                    }
                    if (statusCode >= 500 && statusCode <= 599)
                    {
                        return new ToolResult { Success = false, ErrorMessage = $"Error: NetHack Wiki is temporarily unavailable (HTTP {statusCode} {searchResponse.ReasonPhrase}). Please try again later." };
                    }
                    return new ToolResult { Success = false, ErrorMessage = $"Error: NetHack Wiki returned HTTP {statusCode} {searchResponse.ReasonPhrase}." };
                }

                var searchJsonStr = await searchResponse.Content.ReadAsStringAsync(cancellationToken);
                var searchJson = JsonDocument.Parse(searchJsonStr);

                if (searchJson.RootElement.TryGetProperty("query", out var queryResult) &&
                    queryResult.TryGetProperty("search", out var searchResults) &&
                    searchResults.GetArrayLength() > 0)
                {
                    var resultSb = new System.Text.StringBuilder();
                    int count = 0;
                    
                    foreach (var result in searchResults.EnumerateArray())
                    {
                        if (count >= maxResults) break;
                        
                        var title = result.GetProperty("title").GetString();
                        
                        if (!string.IsNullOrEmpty(title))
                        {
                            var parseUrl = $"https://nethackwiki.com/api.php?action=parse&page={Uri.EscapeDataString(title)}&prop=text&format=json&redirects=1";
                            var parseResponse = await client.GetAsync(parseUrl, cancellationToken);
                            
                            if (!parseResponse.IsSuccessStatusCode)
                            {
                                int parseStatusCode = (int)parseResponse.StatusCode;
                                if (parseResponse.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                                {
                                    return new ToolResult { Success = false, ErrorMessage = "Error: NetHack Wiki rate limit exceeded (429). Please wait a moment before querying the wiki again." };
                                }
                                if (parseStatusCode >= 500 && parseStatusCode <= 599)
                                {
                                    return new ToolResult { Success = false, ErrorMessage = $"Error: NetHack Wiki is temporarily unavailable (HTTP {parseStatusCode} {parseResponse.ReasonPhrase}). Please try again later." };
                                }
                                continue;
                            }
                            
                            var parseJsonStr = await parseResponse.Content.ReadAsStringAsync(cancellationToken);
                            var parseJson = JsonDocument.Parse(parseJsonStr);
                            
                            if (parseJson.RootElement.TryGetProperty("parse", out var parseObj) &&
                                parseObj.TryGetProperty("text", out var textObj) &&
                                textObj.TryGetProperty("*", out var htmlElem))
                            {
                                var html = htmlElem.GetString() ?? "";
                                var text = Regex.Replace(html, @"<(script|style)[^>]*>.*?</\1>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                                text = Regex.Replace(text, "<[^>]*>", " ");
                                text = System.Net.WebUtility.HtmlDecode(text);
                                
                                // Remove extra newlines and spaces
                                text = Regex.Replace(text, @"\n{3,}", "\n\n");
                                
                                if (text.Length > 4000)
                                {
                                    text = text.Substring(0, 4000) + "\n... [Article truncated for length]";
                                }

                                resultSb.AppendLine($"### Article: {title}");
                                resultSb.AppendLine(text);
                                resultSb.AppendLine("--------------------------------------------------");
                                count++;
                            }
                        }
                    }
                    
                    if (count > 0)
                    {
                        var resultContent = resultSb.ToString();
                        
                        _cache.Set(cacheKey, resultContent, new MemoryCacheEntryOptions
                        {
                            Size = 1, // CRITICAL: SizeLimit is configured globally
                            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(_cacheMinutes)
                        });
                        
                        return new ToolResult { Success = true, Content = resultContent };
                    }
                }

                return new ToolResult { Success = true, Content = "No relevant information found on the NetHack wiki." };
            }
            catch (OperationCanceledException)
            {
                return new ToolResult { Success = false, ErrorMessage = "Error: The NetHack wiki request timed out or was canceled." };
            }
            catch (HttpRequestException ex)
            {
                return new ToolResult { Success = false, ErrorMessage = $"Error: Could not connect to the NetHack wiki ({ex.GetType().Name}: {ex.Message})." };
            }
            catch (Exception ex)
            {
                var inner = ex.InnerException != null ? $" (Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message})" : "";
                return new ToolResult { Success = false, ErrorMessage = $"Error querying NetHack wiki: {ex.GetType().Name}: {ex.Message}{inner}" };
            }
        }
    }
}
