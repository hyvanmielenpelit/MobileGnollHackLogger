using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Overseer.Services.Tools
{
    public class NetHackWikiSearchTool : IToolHandler
    {
        private readonly IHttpClientFactory _httpClientFactory;

        public string ToolName => "nethack_wiki_search";
        public string Description { get; set; } = "Search the general NetHack wiki for mechanics, monsters, and items not specific to GnollHack.";
        public ToolExecutionLocation ExecutionLocation => ToolExecutionLocation.Server;
        public ToolCategory Category => ToolCategory.ExternalLookup;

        public JsonElement ParameterSchema { get; }

        public NetHackWikiSearchTool(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
            ParameterSchema = JsonDocument.Parse(@"
            {
                ""type"": ""object"",
                ""properties"": {
                    ""query"": { ""type"": ""string"", ""description"": ""The search terms to look up in the NetHack wiki"" }
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

            try
            {
                var client = _httpClientFactory.CreateClient("NetHackWiki");
                var url = $"https://nethackwiki.com/api.php?action=query&list=search&srsearch={Uri.EscapeDataString(query)}&format=json";
                client.DefaultRequestHeaders.Add("User-Agent", "GnollHackOverseer/1.0 (https://gnollhack.com/)");

                var response = await client.GetAsync(url, cancellationToken);
                response.EnsureSuccessStatusCode();

                var jsonStr = await response.Content.ReadAsStringAsync(cancellationToken);
                var json = JsonDocument.Parse(jsonStr);

                if (json.RootElement.TryGetProperty("query", out var queryResult) &&
                    queryResult.TryGetProperty("search", out var searchResults) &&
                    searchResults.GetArrayLength() > 0)
                {
                    var results = searchResults.EnumerateArray()
                        .Take(3)
                        .Select(r => 
                        {
                            var title = r.GetProperty("title").GetString();
                            var snippet = r.GetProperty("snippet").GetString();
                            snippet = ChatService.SanitizeSnapshotForLlm(snippet ?? "");
                            return $"Title: {title}\nSnippet: {snippet}";
                        });

                    return new ToolResult { Success = true, Content = string.Join("\n\n", results) };
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
