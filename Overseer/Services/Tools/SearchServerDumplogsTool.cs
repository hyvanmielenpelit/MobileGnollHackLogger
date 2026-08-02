using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;

namespace Overseer.Services.Tools
{
    public class SearchServerDumplogsTool : IToolHandler
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _configuration;

        public string ToolName => "search_server_dumplogs";
        public string Description { get; set; } = "Search server dumplogs for a specific term.";
        public ToolExecutionLocation ExecutionLocation => ToolExecutionLocation.Server;
        public ToolCategory Category => ToolCategory.InformationRetrieval;

        public JsonElement ParameterSchema { get; }

        public SearchServerDumplogsTool(IServiceScopeFactory scopeFactory, IConfiguration configuration)
        {
            _scopeFactory = scopeFactory;
            _configuration = configuration;
            ParameterSchema = JsonDocument.Parse(@"
            {
                ""type"": ""object"",
                ""properties"": {
                    ""search_term"": { ""type"": ""string"", ""description"": ""The term to search for across server dumplogs"" },
                    ""max_results"": { ""type"": ""integer"", ""description"": ""Maximum number of matching logs to return. Default is 3, max is 5."" }
                },
                ""required"": [""search_term""]
            }").RootElement;
        }

        public async Task<ToolResult> ExecuteAsync(JsonElement parameters, ToolExecutionContext context, CancellationToken cancellationToken)
        {
            try
            {
                if (!parameters.TryGetProperty("search_term", out var searchTermProp))
                {
                    return new ToolResult { Success = false, ErrorMessage = "Missing required parameter: search_term" };
                }

                string searchTerm = searchTermProp.GetString() ?? "";
                if (string.IsNullOrWhiteSpace(searchTerm))
                {
                    return new ToolResult { Success = false, ErrorMessage = "search_term cannot be empty" };
                }

                int maxResults = 3;
                if (parameters.TryGetProperty("max_results", out var maxResultsProp) && maxResultsProp.TryGetInt32(out int maxParsed))
                {
                    maxResults = Math.Clamp(maxParsed, 1, 5);
                }

                string dumplogBasePath = _configuration["DumpLogPath"] ?? "";
                if (string.IsNullOrEmpty(dumplogBasePath))
                {
                    return new ToolResult { Success = false, ErrorMessage = "Server DumplogPath is not configured." };
                }

                using var scope = _scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                // We want to search the most recent games first. We'll grab a chunk of recent games and search them.
                // We'll iterate in chunks until we find enough matches or hit a reasonable limit.
                int batchSize = 100;
                int maxBatches = 5; // Look at up to 500 recent games
                var results = new List<string>();

                for (int batch = 0; batch < maxBatches; batch++)
                {
                    var recentGames = await dbContext.GameLog
                        .OrderByDescending(g => g.Id)
                        .Skip(batch * batchSize)
                        .Take(batchSize)
                        .ToListAsync(cancellationToken);

                    foreach (var game in recentGames)
                    {
                        if (cancellationToken.IsCancellationRequested) break;

                        if (string.IsNullOrEmpty(game.Name)) continue;

                        string dir = Path.Combine(dumplogBasePath, game.Name);
                        string fileName = $"gnollhack.{game.Name}.{game.StartTimeUTC}.txt";
                        string filePath = Path.Combine(dir, fileName);

                        if (File.Exists(filePath))
                        {
                            try
                            {
                                string content = await File.ReadAllTextAsync(filePath, cancellationToken);
                                
                                int matchIndex = content.IndexOf(searchTerm, StringComparison.OrdinalIgnoreCase);
                                if (matchIndex >= 0)
                                {
                                    // Extract an excerpt
                                    int start = Math.Max(0, matchIndex - 100);
                                    int end = Math.Min(content.Length, matchIndex + searchTerm.Length + 100);
                                    string excerpt = content.Substring(start, end - start);
                                    
                                    excerpt = excerpt.Replace("\n", " ").Replace("\r", "");
                                    
                                    results.Add($"Game ID: {game.Id}, Character: {game.CharacterName} ({game.Role}), Result: {game.DeathText}\nExcerpt: \"...{excerpt}...\"\n");
                                    
                                    if (results.Count >= maxResults)
                                    {
                                        break;
                                    }
                                }
                            }
                            catch (Exception)
                            {
                                // Ignore read errors on individual files
                            }
                        }
                    }

                    if (results.Count >= maxResults || cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                }

                if (results.Count == 0)
                {
                    return new ToolResult { Success = true, Content = $"No results found for '{searchTerm}' in recent server dumplogs." };
                }

                string finalOutput = $"Found {results.Count} matches for '{searchTerm}':\n\n" + string.Join("\n", results);
                return new ToolResult { Success = true, Content = finalOutput };
            }
            catch (Exception ex)
            {
                return new ToolResult { Success = false, ErrorMessage = $"Error searching server dumplogs: {ex.Message}" };
            }
        }
    }
}
