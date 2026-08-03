using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;

namespace Overseer.Services.Tools
{
    public class NetHackWikiSearchTool : IToolHandler
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IMemoryCache _cache;
        
        private const int MaxCallsPerMinute = 10;

        public string ToolName => "nethack_wiki_search";
        public string Description { get; set; } = "Search the general NetHack wiki for mechanics, monsters, and items not specific to GnollHack.";
        public ToolExecutionLocation ExecutionLocation => ToolExecutionLocation.Server;
        public ToolCategory Category => ToolCategory.ExternalLookup;

        public JsonElement ParameterSchema { get; }

        public NetHackWikiSearchTool(IHttpClientFactory httpClientFactory, IMemoryCache cache)
        {
            _httpClientFactory = httpClientFactory;
            _cache = cache;
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
                maxResults = Math.Clamp(maxResElem.GetInt32(), 1, 3);
            }

            var rateLimitKey = $"nhwiki_rate_{context.SessionId}";
            var currentCalls = _cache.GetOrCreate(rateLimitKey, entry =>
            {
                entry.Size = 1;
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1);
                return 0;
            });

            if (currentCalls >= MaxCallsPerMinute)
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
                client.DefaultRequestHeaders.Add("User-Agent", "GnollHackOverseer/1.0 (https://gnollhack.com/)");

                var searchUrl = $"https://nethackwiki.com/api.php?action=query&list=search&srsearch={Uri.EscapeDataString(query)}&format=json";
                var searchResponse = await client.GetAsync(searchUrl, cancellationToken);
                searchResponse.EnsureSuccessStatusCode();

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
                            var parseUrl = $"https://nethackwiki.com/api.php?action=parse&page={Uri.EscapeDataString(title)}&prop=text&format=json";
                            var parseResponse = await client.GetAsync(parseUrl, cancellationToken);
                            if (!parseResponse.IsSuccessStatusCode) continue;
                            
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
                            Size = resultContent.Length,
                            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(60)
                        });
                        
                        return new ToolResult { Success = true, Content = resultContent };
                    }
                }

                return new ToolResult { Success = true, Content = "No relevant information found on the NetHack wiki." };
            }
            catch (Exception ex)
            {
                return new ToolResult { Success = false, ErrorMessage = $"Failed to query NetHack wiki: {ex.Message}" };
            }
        }
    }
}
