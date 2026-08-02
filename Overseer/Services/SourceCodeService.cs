using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Overseer.Services
{
    public class SourceCodeService : IHostedService, IDisposable
    {
        private readonly ILogger<SourceCodeService> _logger;
        private readonly string _sourceCodePath;
        private readonly int _maxFileSizeKB;
        private Timer? _reindexTimer;
        
        private string _lastHeadSha = string.Empty;
        
        private readonly ConcurrentDictionary<string, SourceDocument> _documents = new(StringComparer.OrdinalIgnoreCase);
        
        private readonly HashSet<string> _excludedFiles = new(StringComparer.OrdinalIgnoreCase)
        {
            "vis_tab.c", "vis_tab.h", "onames.h", "pm.h", "date.h", "animoff.h", "animtotals.h"
        };
        
        public SourceCodeService(IConfiguration configuration, ILogger<SourceCodeService> logger)
        {
            _logger = logger;
            _sourceCodePath = configuration["SourceCodePath"] ?? @"c:\gnollhack-repository";
            
            if (!int.TryParse(configuration["MaxSourceFileSizeKB"], out _maxFileSizeKB))
            {
                _maxFileSizeKB = 800;
            }
        }
        
        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("SourceCodeService starting. Path: {Path}", _sourceCodePath);
            IndexRepository();
            
            // Check for changes every 10 minutes
            _reindexTimer = new Timer(CheckForUpdates, null, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10));
            
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _reindexTimer?.Change(Timeout.Infinite, 0);
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _reindexTimer?.Dispose();
        }

        private void CheckForUpdates(object? state)
        {
            try
            {
                if (!Directory.Exists(_sourceCodePath)) return;
                
                string gitHeadPath = Path.Combine(_sourceCodePath, ".git", "refs", "heads", "master");
                if (!File.Exists(gitHeadPath))
                {
                    // Fallback to packed-refs or FETCH_HEAD if we can't find master ref easily
                    gitHeadPath = Path.Combine(_sourceCodePath, ".git", "FETCH_HEAD");
                }
                
                if (File.Exists(gitHeadPath))
                {
                    string currentSha = File.ReadAllText(gitHeadPath).Trim();
                    if (!string.IsNullOrEmpty(currentSha) && currentSha != _lastHeadSha)
                    {
                        _logger.LogInformation("Repository update detected ({OldSha} -> {NewSha}). Re-indexing.", _lastHeadSha, currentSha);
                        IndexRepository();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking for repository updates.");
            }
        }

        private void IndexRepository()
        {
            if (!Directory.Exists(_sourceCodePath))
            {
                _logger.LogWarning("Source code repository not found at {Path}", _sourceCodePath);
                return;
            }
            
            try
            {
                var newDocuments = new ConcurrentDictionary<string, SourceDocument>(StringComparer.OrdinalIgnoreCase);
                
                var targetDirs = new[] { "src", "include", "dat", @"win\win32\xpl" };
                
                foreach (var dir in targetDirs)
                {
                    string fullDirPath = Path.Combine(_sourceCodePath, dir);
                    if (!Directory.Exists(fullDirPath)) continue;
                    
                    var files = Directory.GetFiles(fullDirPath, "*.*", SearchOption.AllDirectories);
                    
                    foreach (var file in files)
                    {
                        var fileInfo = new FileInfo(file);
                        string ext = fileInfo.Extension.ToLowerInvariant();
                        string fileName = fileInfo.Name;
                        
                        // Check exclusions
                        if (_excludedFiles.Contains(fileName)) continue;
                        if (fileName.EndsWith("conf.h", StringComparison.OrdinalIgnoreCase) ||
                            fileName.StartsWith("win", StringComparison.OrdinalIgnoreCase) && ext == ".h" && fileName != "wintype.h" || // Rough approximation of platform headers, avoiding false positives if possible
                            fileName.StartsWith("mac", StringComparison.OrdinalIgnoreCase) && ext == ".h" ||
                            fileName.StartsWith("qt", StringComparison.OrdinalIgnoreCase) && ext == ".h")
                        {
                            // Skip platform specific headers
                            continue;
                        }
                        
                        // Check extensions
                        bool isNormalMode = ext == ".c" || ext == ".h" || ext == ".des" || ext == ".txt";
                        bool isDebugMode = ext == ".cs" || ext == ".xaml";
                        
                        if (!isNormalMode && !isDebugMode) continue;
                        
                        if (fileInfo.Length <= _maxFileSizeKB * 1024)
                        {
                            string relPath = Path.GetRelativePath(_sourceCodePath, file).Replace('\\', '/');
                            newDocuments[relPath] = new SourceDocument
                            {
                                FilePath = file,
                                RelativePath = relPath,
                                IsNetCode = isDebugMode,
                                ContentLines = File.ReadAllLines(file)
                            };
                        }
                    }
                }
                
                // Swap in the new dictionary
                _documents.Clear();
                foreach (var kvp in newDocuments)
                {
                    _documents[kvp.Key] = kvp.Value;
                }
                
                // Update SHA
                string gitHeadPath = Path.Combine(_sourceCodePath, ".git", "refs", "heads", "master");
                if (!File.Exists(gitHeadPath)) gitHeadPath = Path.Combine(_sourceCodePath, ".git", "FETCH_HEAD");
                if (File.Exists(gitHeadPath))
                {
                    _lastHeadSha = File.ReadAllText(gitHeadPath).Trim();
                }
                
                _logger.LogInformation("Indexed {Count} source files.", _documents.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error indexing source repository.");
            }
        }

        public string SearchFiles(string query, string? fileFilter, int maxResults, bool includeNetCode, int maxResultLength)
        {
            if (string.IsNullOrWhiteSpace(query)) return string.Empty;
            
            var results = new List<SearchResult>();
            
            var docsToSearch = _documents.Values.AsEnumerable();
            if (!includeNetCode)
            {
                docsToSearch = docsToSearch.Where(d => !d.IsNetCode);
            }
            if (!string.IsNullOrWhiteSpace(fileFilter))
            {
                docsToSearch = docsToSearch.Where(d => d.RelativePath.Contains(fileFilter, StringComparison.OrdinalIgnoreCase));
            }
            
            foreach (var doc in docsToSearch)
            {
                var matches = new List<int>();
                for (int i = 0; i < doc.ContentLines.Length; i++)
                {
                    if (doc.ContentLines[i].Contains(query, StringComparison.OrdinalIgnoreCase))
                    {
                        matches.Add(i);
                    }
                }
                
                if (matches.Any())
                {
                    results.Add(new SearchResult { Document = doc, MatchLines = matches });
                }
            }
            
            // Sort by number of matches (descending)
            results = results.OrderByDescending(r => r.MatchLines.Count).Take(maxResults).ToList();
            
            if (!results.Any()) return string.Empty;
            
            var sb = new System.Text.StringBuilder();
            
            foreach (var result in results)
            {
                // Group nearby matches within ±5 lines
                var groups = new List<List<int>>();
                foreach (var line in result.MatchLines)
                {
                    if (!groups.Any())
                    {
                        groups.Add(new List<int> { line });
                    }
                    else
                    {
                        var lastGroup = groups.Last();
                        if (line - lastGroup.Last() <= 10) // within 10 lines of the last match in the group (±5 context overlap)
                        {
                            lastGroup.Add(line);
                        }
                        else
                        {
                            groups.Add(new List<int> { line });
                        }
                    }
                }
                
                // Limit to 3 match groups per file
                int totalGroups = groups.Count;
                var groupsToProcess = groups.Take(3).ToList();
                
                foreach (var group in groupsToProcess)
                {
                    int startLine = Math.Max(0, group.First() - 5);
                    int endLine = Math.Min(result.Document.ContentLines.Length - 1, group.Last() + 5);
                    
                    sb.AppendLine($"--- {result.Document.RelativePath}:L{startLine + 1} ---");
                    for (int i = startLine; i <= endLine; i++)
                    {
                        sb.AppendLine($"{i + 1}: {result.Document.ContentLines[i]}");
                    }
                    sb.AppendLine();
                }
                
                if (totalGroups > 3)
                {
                    sb.AppendLine($"[... {totalGroups - 3} additional match groups in this file hidden ...]");
                    sb.AppendLine();
                }
            }
            
            string finalResult = sb.ToString();
            if (finalResult.Length > maxResultLength)
            {
                finalResult = finalResult.Substring(0, maxResultLength) + "\n\n[... output truncated ...]\n[Additional matches not shown — refine your query or use source_code_view]";
            }
            
            return finalResult.Trim();
        }

        public string GetFileExcerpt(string relativePath, int startLine, int lineCount)
        {
            // Normalize path for lookup
            relativePath = relativePath.Replace('\\', '/');
            if (!_documents.TryGetValue(relativePath, out var doc))
            {
                // Try case-insensitive fuzzy match if exact match fails
                doc = _documents.Values.FirstOrDefault(d => d.RelativePath.Equals(relativePath, StringComparison.OrdinalIgnoreCase));
                if (doc == null)
                {
                    return $"Error: File '{relativePath}' not found in indexed source code.";
                }
            }
            
            startLine = Math.Max(1, startLine) - 1; // 0-indexed
            lineCount = Math.Clamp(lineCount, 1, 100);
            
            if (startLine >= doc.ContentLines.Length)
            {
                return $"Error: File '{doc.RelativePath}' only has {doc.ContentLines.Length} lines. Requested start line {startLine + 1} is out of bounds.";
            }
            
            int endLine = Math.Min(doc.ContentLines.Length - 1, startLine + lineCount - 1);
            
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"--- {doc.RelativePath}:L{startLine + 1}-L{endLine + 1} ---");
            for (int i = startLine; i <= endLine; i++)
            {
                sb.AppendLine($"{i + 1}: {doc.ContentLines[i]}");
            }
            
            return sb.ToString();
        }

        private class SourceDocument
        {
            public string FilePath { get; set; } = string.Empty;
            public string RelativePath { get; set; } = string.Empty;
            public bool IsNetCode { get; set; }
            public string[] ContentLines { get; set; } = Array.Empty<string>();
        }

        private class SearchResult
        {
            public SourceDocument Document { get; set; } = new SourceDocument();
            public List<int> MatchLines { get; set; } = new List<int>();
        }
    }
}
