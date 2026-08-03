using System;
using System.Diagnostics;
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
        
        public class ConstantInfo
        {
            public string Name { get; set; } = string.Empty;
            public string Value { get; set; } = string.Empty;
            public string FilePath { get; set; } = string.Empty;
            public int LineNumber { get; set; }
        }

        private readonly ConcurrentDictionary<string, ConstantInfo> _constants = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, SourceDocument> _documents = new(StringComparer.OrdinalIgnoreCase);
        
        private readonly HashSet<string> _excludedFiles = new(StringComparer.OrdinalIgnoreCase)
        {
            "vis_tab.c", "vis_tab.h", "date.h"
        };
        
        private readonly IConfiguration _configuration;
        private readonly string[] _makedefsSourceFiles = new[]
        {
            "src/monst.c", "src/objects.c", "src/animdef.c", "util/makedefs.c"
        };
        private Dictionary<string, DateTime> _lastMakedefsSourceTimestamps = new();
        
        public SourceCodeService(IConfiguration configuration, ILogger<SourceCodeService> logger)
        {
            _configuration = configuration;
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
            RegenerateHeaders(force: true);
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
                        RegenerateHeaders();
                        IndexRepository();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking for repository updates.");
            }
        }

        /// <summary>
        /// Regenerate onames.h, pm.h, animoff.h, animtotals.h by building and running makedefs.
        /// Only runs when the source files compiled into makedefs have changed (or on startup when force=true).
        /// </summary>
        private void RegenerateHeaders(bool force = false)
        {
            try
            {
                var currentTimestamps = new Dictionary<string, DateTime>();
                // 0a. Check if any makedefs source files actually changed
                if (!force)
                {
                    bool anyChanged = false;
                    foreach (var relPath in _makedefsSourceFiles)
                    {
                        string fullPath = Path.Combine(_sourceCodePath, relPath.Replace('/', Path.DirectorySeparatorChar));
                        if (File.Exists(fullPath))
                        {
                            var lastWrite = File.GetLastWriteTimeUtc(fullPath);
                            currentTimestamps[relPath] = lastWrite;
                            if (!_lastMakedefsSourceTimestamps.TryGetValue(relPath, out var prev) || prev != lastWrite)
                            {
                                anyChanged = true;
                            }
                        }
                    }
                    if (!anyChanged)
                    {
                        _logger.LogDebug("makedefs source files unchanged, skipping header regeneration.");
                        return;
                    }
                }

                // 0b. Optional branch restriction
                string? allowedBranch = _configuration["MakedefsBranch"];
                if (!string.IsNullOrEmpty(allowedBranch))
                {
                    // Read the current branch from .git/HEAD (e.g., "ref: refs/heads/master")
                    string gitHeadFile = Path.Combine(_sourceCodePath, ".git", "HEAD");
                    if (File.Exists(gitHeadFile))
                    {
                        string headContent = File.ReadAllText(gitHeadFile).Trim();
                        string currentBranch = headContent.StartsWith("ref: refs/heads/")
                            ? headContent.Substring("ref: refs/heads/".Length)
                            : "";
                        if (!string.Equals(currentBranch, allowedBranch, StringComparison.OrdinalIgnoreCase))
                        {
                            _logger.LogInformation("Skipping makedefs: current branch '{Current}' != allowed '{Allowed}'",
                                currentBranch, allowedBranch);
                            return;
                        }
                    }
                }

                // 1. Rebuild makedefs if a build command is configured
                string? buildCmd = _configuration["MakedefsBuildCommand"];
                if (!string.IsNullOrEmpty(buildCmd))
                {
                    if (!RunProcess(buildCmd, _sourceCodePath))
                    {
                        _logger.LogWarning("makedefs build failed — aborting header generation to avoid running a stale binary.");
                        return;  // Do NOT continue to run a potentially stale makedefs.exe
                    }
                }

                // 2. Locate the executable
                string? makedefsPath = _configuration["MakedefsExecutablePath"];
                if (string.IsNullOrEmpty(makedefsPath))
                {
                    // The GnollHack build system outputs makedefs.exe to tools\$(Configuration)\$(Platform)\
                    // (defined in win/win32/vs/dirs.props as ToolsDir), NOT bin\.
                    makedefsPath = Path.Combine(_sourceCodePath, "tools", "Release", "x64", "makedefs.exe");
                }
                
                if (!File.Exists(makedefsPath))
                {
                    _logger.LogWarning("makedefs executable not found at {Path}, skipping header generation", makedefsPath);
                    return;
                }

                // 3. Generate headers
                // Working directories match aftermakedefs.proj: -o/-p from util/, -a from dat/.
                // All use INCLUDE_TEMPLATE ("../include/%s") so output goes to include/ either way.
                var utilDir = Path.Combine(_sourceCodePath, "util");
                bool allSucceeded = true;
                allSucceeded &= RunProcess($"\"{makedefsPath}\" -o", utilDir);  // onames.h
                allSucceeded &= RunProcess($"\"{makedefsPath}\" -p", utilDir);  // pm.h
                allSucceeded &= RunProcess($"\"{makedefsPath}\" -a", Path.Combine(_sourceCodePath, "dat"));  // animoff.h, animtotals.h
                
                // Only commit timestamps if ALL steps succeeded.
                // If any failed, we want to retry on the next cycle.
                if (allSucceeded && !force)
                {
                    _lastMakedefsSourceTimestamps = currentTimestamps;
                }
                
                _logger.LogInformation(allSucceeded
                    ? "makedefs header regeneration completed successfully."
                    : "makedefs header regeneration partially failed — will retry next cycle.");
            }
            catch (Exception ex)
            {
                // Graceful degradation: if makedefs fails entirely, log the error
                // and continue with indexing. Previously generated headers (if any)
                // will still be on disk and will be indexed. If no headers exist,
                // they simply won't be indexed — the rest of the source code still works.
                _logger.LogError(ex, "makedefs header regeneration failed. Continuing with existing headers (if any).");
            }
        }

        /// <summary>
        /// Runs a shell command. Cross-platform: uses cmd.exe on Windows, /bin/bash on Linux/macOS.
        /// Returns true if the process ran and exited with code 0, false otherwise.
        /// </summary>
        private bool RunProcess(string command, string workingDir)
        {
            try
            {
                bool isWindows = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                    System.Runtime.InteropServices.OSPlatform.Windows);

                var psi = new ProcessStartInfo(
                    isWindows ? "cmd.exe" : "/bin/bash",
                    isWindows ? $"/c {command}" : $"-c \"{command}\"")
                {
                    WorkingDirectory = workingDir,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var process = Process.Start(psi);
                if (process == null) return false;
                
                // IMPORTANT: Read streams asynchronously to avoid deadlock.
                // If the child process fills the OS pipe buffer for one stream while
                // we're synchronously reading the other, both processes deadlock.
                var stdoutTask = process.StandardOutput.ReadToEndAsync();
                var stderrTask = process.StandardError.ReadToEndAsync();
                
                bool exited = process.WaitForExit(30000); // 30 second timeout
                
                if (!exited)
                {
                    _logger.LogWarning("makedefs command timed out: {Command}", command);
                    try { process.Kill(); } catch { /* best effort */ }
                    return false;
                }
                
                // Process has exited — drain any remaining buffered output.
                // GetAwaiter().GetResult() is safe here because the pipes are closed.
                string stdout = stdoutTask.GetAwaiter().GetResult();
                string stderr = stderrTask.GetAwaiter().GetResult();
                
                if (process.ExitCode != 0)
                {
                    _logger.LogWarning("makedefs command failed (exit {Code}): {Command}, stderr: {StdErr}",
                        process.ExitCode, command, stderr);
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error running makedefs command: {Command}", command);
                return false;
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
                var newConstants = new ConcurrentDictionary<string, ConstantInfo>(StringComparer.OrdinalIgnoreCase);
                
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
                            var contentLines = File.ReadAllLines(file);
                            
                            newDocuments[relPath] = new SourceDocument
                            {
                                FilePath = file,
                                RelativePath = relPath,
                                IsNetCode = isDebugMode,
                                ContentLines = contentLines
                            };
                            
                            // Parse constants
                            bool inEnum = false;
                            for (int i = 0; i < contentLines.Length; i++)
                            {
                                string line = contentLines[i].Trim();
                                
                                var defineMatch = Regex.Match(line, @"^#define\s+([A-Za-z0-9_]+)(?:\s+([^/]*))?");
                                if (defineMatch.Success)
                                {
                                    string name = defineMatch.Groups[1].Value;
                                    string val = defineMatch.Groups[2].Success ? defineMatch.Groups[2].Value.Trim() : "";
                                    newConstants[name] = new ConstantInfo { Name = name, Value = val, FilePath = relPath, LineNumber = i + 1 };
                                    continue;
                                }
                                
                                if (Regex.IsMatch(line, @"\benum\b.*\{")) inEnum = true;
                                
                                if (inEnum)
                                {
                                    var matches = Regex.Matches(line, @"([A-Za-z0-9_]+)\s*(?:=\s*([^,}]+))?\s*(?:,|})");
                                    foreach (Match m in matches)
                                    {
                                        string name = m.Groups[1].Value;
                                        if (name == "enum") continue;
                                        string val = m.Groups[2].Success ? m.Groups[2].Value.Trim() : "";
                                        newConstants[name] = new ConstantInfo { Name = name, Value = val, FilePath = relPath, LineNumber = i + 1 };
                                    }
                                    if (line.Contains("}")) inEnum = false;
                                }
                            }
                        }
                    }
                }
                
                // Swap in the new dictionaries
                _documents.Clear();
                foreach (var kvp in newDocuments)
                {
                    _documents[kvp.Key] = kvp.Value;
                }
                
                _constants.Clear();
                foreach (var kvp in newConstants)
                {
                    _constants[kvp.Key] = kvp.Value;
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

        public string SearchFiles(string query, string? fileFilter, int maxResults, bool includeNetCode, int maxResultLength, bool isRegex = false, bool filenamesOnly = false, int contextLines = 5, bool caseSensitive = false)
        {
            maxResults = Math.Clamp(maxResults, 1, 100);
            contextLines = Math.Clamp(contextLines, 0, 25);
            if (string.IsNullOrWhiteSpace(query)) return string.Empty;
            
            Regex? regex = null;
            if (isRegex)
            {
                try
                {
                    var options = caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
                    regex = new Regex(query, options);
                }
                catch (Exception ex)
                {
                    return $"Error: Invalid regular expression. {ex.Message}";
                }
            }
            
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
                    if (isRegex && regex != null)
                    {
                        if (regex.IsMatch(doc.ContentLines[i]))
                        {
                            matches.Add(i);
                        }
                    }
                    else
                    {
                        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
                        if (doc.ContentLines[i].Contains(query, comparison))
                        {
                            matches.Add(i);
                        }
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
            
            if (filenamesOnly)
            {
                var sb = new System.Text.StringBuilder();
                foreach (var result in results)
                {
                    sb.AppendLine($"{result.Document.RelativePath} ({result.MatchLines.Count} matches)");
                }
                return sb.ToString().Trim();
            }
            
            var resultSb = new System.Text.StringBuilder();
            
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
                        if (line - lastGroup.Last() <= contextLines * 2)
                        {
                            lastGroup.Add(line);
                        }
                        else
                        {
                            groups.Add(new List<int> { line });
                        }
                    }
                }
                
                // Limit to 5 match groups per file
                int totalGroups = groups.Count;
                var groupsToProcess = groups.Take(5).ToList();
                
                foreach (var group in groupsToProcess)
                {
                    int startLine = Math.Max(0, group.First() - contextLines);
                    int endLine = Math.Min(result.Document.ContentLines.Length - 1, group.Last() + contextLines);
                    
                    resultSb.AppendLine($"--- {result.Document.RelativePath}:L{startLine + 1} ---");
                    for (int i = startLine; i <= endLine; i++)
                    {
                        string prefix = group.Contains(i) ? ">>> " : "    ";
                        resultSb.AppendLine($"{prefix}{i + 1}: {result.Document.ContentLines[i]}");
                    }
                    resultSb.AppendLine();
                }
                
                if (totalGroups > 5)
                {
                    resultSb.AppendLine($"[... {totalGroups - 5} additional match groups in this file hidden ...]");
                    resultSb.AppendLine();
                }
            }
            
            string finalResult = resultSb.ToString();
            if (finalResult.Length > maxResultLength)
            {
                finalResult = finalResult.Substring(0, maxResultLength) + "\n\n[... output truncated ...]\n[Additional matches not shown — refine your query or use source_code_view]";
            }
            
            return finalResult.Trim();
        }

        public string FindDefinition(string symbol, string kind)
        {
            var results = new System.Text.StringBuilder();
            
            // Prioritize .c files over .h files for definitions
            var docsToSearch = _documents.Values
                .Where(d => !d.IsNetCode)
                .OrderBy(d => d.FilePath.EndsWith(".c", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ToList();
                
            string escapedSymbol = Regex.Escape(symbol);
            int maxResults = 10;
            int resultCount = 0;

            foreach (var doc in docsToSearch)
            {
                bool isCFile = doc.FilePath.EndsWith(".c", StringComparison.OrdinalIgnoreCase);
                
                for (int i = 0; i < doc.ContentLines.Length; i++)
                {
                    string line = doc.ContentLines[i];
                    bool match = false;
                    int contextLines = 5;
                    
                    if ((kind == "any" || kind == "function") && isCFile)
                    {
                        // Function definition: match symbol at start of line followed by (
                        if (Regex.IsMatch(line, $@"^{escapedSymbol}\s*\("))
                        {
                            match = true;
                            // For C functions, include the preceding line for the return type
                            contextLines = 8;
                        }
                    }
                    
                    if ((kind == "any" || kind == "macro") && !match)
                    {
                        if (Regex.IsMatch(line, $@"^\s*#define\s+{escapedSymbol}[\s(]"))
                        {
                            match = true;
                        }
                    }
                    
                    if ((kind == "any" || kind == "struct") && !match)
                    {
                        if (Regex.IsMatch(line, $@"^\s*struct\s+{escapedSymbol}\s*{{"))
                        {
                            match = true;
                        }
                    }
                    
                    if ((kind == "any" || kind == "type") && !match)
                    {
                        if (Regex.IsMatch(line, $@"^\s*typedef\s+.*\s+{escapedSymbol}\s*;"))
                        {
                            match = true;
                        }
                    }
                    
                    if ((kind == "any" || kind == "enum") && !match)
                    {
                        if (Regex.IsMatch(line, $@"^\s*enum\s+{escapedSymbol}\s*{{"))
                        {
                            match = true;
                        }
                        else if (Regex.IsMatch(line, $@"^\s*{escapedSymbol}\s*=\s*\d+") || Regex.IsMatch(line, $@"^\s*{escapedSymbol}\s*,"))
                        {
                            if (doc.ContentLines.Take(i).Reverse().Take(50).Any(l => l.Contains("enum ")))
                            {
                                match = true;
                            }
                        }
                    }

                    if (match)
                    {
                        int startLine = Math.Max(0, i - (kind == "function" || kind == "any" ? 2 : 1));
                        int endLine = Math.Min(doc.ContentLines.Length - 1, i + contextLines);
                        
                        results.AppendLine($"--- {doc.RelativePath}:L{i + 1} ---");
                        for (int j = startLine; j <= endLine; j++)
                        {
                            string prefix = (j == i) ? ">>> " : "    ";
                            results.AppendLine($"{prefix}{j + 1}: {doc.ContentLines[j]}");
                        }
                        results.AppendLine();
                        
                        resultCount++;
                        if (resultCount >= maxResults)
                        {
                            results.AppendLine($"[... Found {maxResults} definitions, stopping search ...]");
                            return results.ToString().Trim();
                        }
                        
                        // Skip ahead so we don't match multiple times in the same context
                        i = endLine;
                    }
                }
            }

            return results.Length > 0 ? results.ToString().Trim() : $"No definition found for '{symbol}' of kind '{kind}'.";
        }

        public string GetFileExcerpt(string relativePath, int? startLineReq, int lineCount, string? searchTerm = null)
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
            
            int startLine = 1;
            
            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                int matchLine = -1;
                for (int i = 0; i < doc.ContentLines.Length; i++)
                {
                    if (doc.ContentLines[i].Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                    {
                        matchLine = i;
                        break;
                    }
                }
                
                if (matchLine == -1)
                {
                    return $"Error: Search term '{searchTerm}' not found in file '{doc.RelativePath}'.";
                }
                
                startLine = Math.Max(1, matchLine + 1 - (lineCount / 2));
            }
            else if (startLineReq.HasValue)
            {
                startLine = startLineReq.Value;
            }
            else
            {
                return "Error: Either start_line or search_term must be provided.";
            }
            
            startLine = Math.Max(1, startLine) - 1; // 0-indexed
            lineCount = Math.Clamp(lineCount, 1, 1000);
            
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

        public IEnumerable<ConstantInfo> GetConstants(string namePattern, string? prefixFilter)
        {
            var results = _constants.Values.AsEnumerable();
            
            if (!string.IsNullOrWhiteSpace(prefixFilter))
            {
                results = results.Where(c => c.Name.StartsWith(prefixFilter, StringComparison.OrdinalIgnoreCase));
            }
            
            if (!string.IsNullOrWhiteSpace(namePattern))
            {
                if (namePattern.Contains("*"))
                {
                    string pattern = "^" + Regex.Escape(namePattern).Replace("\\*", ".*") + "$";
                    var regex = new Regex(pattern, RegexOptions.IgnoreCase);
                    results = results.Where(c => regex.IsMatch(c.Name));
                }
                else
                {
                    results = results.Where(c => string.Equals(c.Name, namePattern, StringComparison.OrdinalIgnoreCase));
                }
            }
            
            return results.OrderBy(c => c.Name).Take(100);
        }

        public string ListFiles(string? pathFilter, bool includeNetCode)
        {
            var docsToSearch = _documents.Values.AsEnumerable();
            if (!includeNetCode)
            {
                docsToSearch = docsToSearch.Where(d => !d.IsNetCode);
            }
            if (!string.IsNullOrWhiteSpace(pathFilter))
            {
                docsToSearch = docsToSearch.Where(d => d.RelativePath.Contains(pathFilter, StringComparison.OrdinalIgnoreCase));
            }
            
            var sortedDocs = docsToSearch.OrderBy(d => d.RelativePath).ToList();
            if (!sortedDocs.Any()) return string.Empty;
            
            var sb = new System.Text.StringBuilder();
            foreach (var doc in sortedDocs)
            {
                sb.AppendLine($"{doc.RelativePath} ({doc.ContentLines.Length} lines)");
            }
            sb.AppendLine($"Total: {sortedDocs.Count} files indexed");
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
