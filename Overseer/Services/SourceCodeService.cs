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
        
        private readonly Dictionary<string, string> _flagDescriptions = new(StringComparer.OrdinalIgnoreCase);
        private readonly GameDataParser _dataParser = new();
        
        public GameDataParser Parser => _dataParser;
        
        private readonly IConfiguration _configuration;
        private readonly string[] _makedefsSourceFiles = new[]
        {
            "src/monst.c", "src/objects.c", "src/animdef.c", "util/makedefs.c"
        };
        private Dictionary<string, DateTime> _lastMakedefsSourceTimestamps = new();
        private readonly int _maxFunctionBodyLines;
        
        public SourceCodeService(IConfiguration configuration, ILogger<SourceCodeService> logger)
        {
            _configuration = configuration;
            _logger = logger;
            _sourceCodePath = configuration["SourceCodePath"] ?? @"c:\gnollhack-repository";
            
            if (!int.TryParse(configuration["MaxSourceFileSizeKB"], out _maxFileSizeKB))
            {
                _maxFileSizeKB = 800;
            }
            _maxFunctionBodyLines = configuration.GetValue<int>("Tools:get_function_definition:MaxLinesPerChunk", 150);
        }
        
        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("SourceCodeService starting. Path: {Path}", _sourceCodePath);
            LoadFlagDescriptions();
            RegenerateHeaders(force: true);
            IndexRepository();
            
            // Check for changes every 10 minutes
            _reindexTimer = new Timer(CheckForUpdates, null, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10));
            
            return Task.CompletedTask;
        }

        private void LoadFlagDescriptions()
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "flag_descriptions.json");
                if (File.Exists(path))
                {
                    var content = File.ReadAllText(path);
                    var dict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(content);
                    if (dict != null)
                    {
                        foreach (var kvp in dict)
                        {
                            _flagDescriptions[kvp.Key] = kvp.Value;
                        }
                    }
                    _logger.LogInformation("Loaded {Count} flag descriptions.", _flagDescriptions.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading flag descriptions.");
            }
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

                // 2. Parse game data via GameDataParser
                try
                {
                    var permonstDoc = _documents.Values.FirstOrDefault(d => d.FilePath.EndsWith("include\\permonst.h", StringComparison.OrdinalIgnoreCase) || d.FilePath.EndsWith("include/permonst.h", StringComparison.OrdinalIgnoreCase));
                    var objclassDoc = _documents.Values.FirstOrDefault(d => d.FilePath.EndsWith("include\\objclass.h", StringComparison.OrdinalIgnoreCase) || d.FilePath.EndsWith("include/objclass.h", StringComparison.OrdinalIgnoreCase));
                    if (permonstDoc != null && objclassDoc != null)
                    {
                        _dataParser.ParseStructs(permonstDoc.ContentLines, objclassDoc.ContentLines);
                    }
                    var monstDoc = _documents.Values.FirstOrDefault(d => d.FilePath.EndsWith("src\\monst.c", StringComparison.OrdinalIgnoreCase) || d.FilePath.EndsWith("src/monst.c", StringComparison.OrdinalIgnoreCase));
                    var objectsDoc = _documents.Values.FirstOrDefault(d => d.FilePath.EndsWith("src\\objects.c", StringComparison.OrdinalIgnoreCase) || d.FilePath.EndsWith("src/objects.c", StringComparison.OrdinalIgnoreCase));
                    if (monstDoc != null) _dataParser.ParseMacros(monstDoc.ContentLines);
                    if (objectsDoc != null) _dataParser.ParseMacros(objectsDoc.ContentLines);
                    
                    _logger.LogInformation("Parsed game data macros and structs.");
                }
                catch (Exception pex)
                {
                    _logger.LogError(pex, "Error parsing game data macros and structs.");
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
        /// <summary>
        /// Shared helper: finds the first line in the index that matches a symbol of the given kind.
        /// Returns the document and 0-based line index, or (null, -1) if not found.
        /// </summary>
        private (SourceDocument? doc, int line) FindSymbolLine(string name, string kind)
        {
            var docsToSearch = _documents.Values
                .Where(d => !d.IsNetCode)
                .OrderBy(d => d.FilePath.EndsWith(".c", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ToList();

            string escapedSymbol = Regex.Escape(name);

            foreach (var doc in docsToSearch)
            {
                bool isCFile = doc.FilePath.EndsWith(".c", StringComparison.OrdinalIgnoreCase);

                for (int i = 0; i < doc.ContentLines.Length; i++)
                {
                    string line = doc.ContentLines[i];
                    bool match = false;

                    if ((kind == "any" || kind == "function") && isCFile)
                    {
                        if (Regex.IsMatch(line, $@"^{escapedSymbol}\s*\(")) match = true;
                    }
                    if ((kind == "any" || kind == "macro") && !match)
                    {
                        if (Regex.IsMatch(line, $@"^\s*#define\s+{escapedSymbol}[\s(]")) match = true;
                    }
                    if ((kind == "any" || kind == "struct") && !match)
                    {
                        if (Regex.IsMatch(line, $@"^\s*struct\s+{escapedSymbol}\s*{{")) match = true;
                    }
                    if ((kind == "any" || kind == "enum") && !match)
                    {
                        if (Regex.IsMatch(line, $@"^\s*enum\s+{escapedSymbol}\s*{{")) match = true;
                    }

                    if (match) return (doc, i);
                }
            }
            return (null, -1);
        }

        public string GetFunctionBody(string name, string kind, int? startLineReq = null)
        {
            var (matchDoc, matchLine) = FindSymbolLine(name, kind);

            if (matchDoc == null || matchLine < 0)
            {
                return $"No definition found for '{name}' of kind '{kind}'.";
            }

            // Decide how to extract
            string resultText = "";
            int extractStart = Math.Max(0, matchLine - (kind == "function" || kind == "any" ? 2 : 1));
            int extractEnd = matchLine;
            bool isMacro = Regex.IsMatch(matchDoc.ContentLines[matchLine], $@"^\s*#define");
            
            if (isMacro)
            {
                // Macro: read lines until no continuation
                int current = matchLine;
                while (current < matchDoc.ContentLines.Length && matchDoc.ContentLines[current].EndsWith("\\"))
                {
                    current++;
                }
                extractEnd = current;
                var sb = new System.Text.StringBuilder();
                for (int i = extractStart; i <= extractEnd; i++) sb.AppendLine(matchDoc.ContentLines[i]);
                resultText = sb.ToString();
            }
            else
            {
                // Function/Struct: look for { and use lexer
                var extraction = CLexer.ExtractBracedBlock(matchDoc.ContentLines, matchLine);
                if (extraction != null)
                {
                    extractEnd = extraction.EndLine;
                    var sb = new System.Text.StringBuilder();
                    for (int i = extractStart; i < extraction.StartLine; i++) sb.AppendLine(matchDoc.ContentLines[i]);
                    sb.AppendLine(extraction.Content);
                    resultText = sb.ToString();
                }
                else
                {
                    // Fallback to simple context
                    extractEnd = Math.Min(matchDoc.ContentLines.Length - 1, matchLine + 10);
                    var sb = new System.Text.StringBuilder();
                    for (int i = extractStart; i <= extractEnd; i++) sb.AppendLine(matchDoc.ContentLines[i]);
                    resultText = sb.ToString();
                }
            }

            string[] resultLines = resultText.Split('\n');
            int totalLines = resultLines.Length;
            int startOutputLine = startLineReq ?? 0;
            if (startOutputLine < 0) startOutputLine = 0;
            if (startOutputLine >= totalLines) startOutputLine = totalLines - 1;

            var finalSb = new System.Text.StringBuilder();
            finalSb.AppendLine($"--- {matchDoc.RelativePath}:L{extractStart + 1}-L{extractEnd + 1} ({name}, {totalLines} lines) ---");
            
            int maxLines = _maxFunctionBodyLines;
            int outputCount = 0;
            
            for (int i = startOutputLine; i < totalLines; i++)
            {
                finalSb.AppendLine(resultLines[i].TrimEnd('\r'));
                outputCount++;
                if (outputCount >= maxLines && i < totalLines - 1)
                {
                    finalSb.AppendLine($"\n[Output truncated at line {i + 1} of {totalLines}. Call again with start_line={i + 1} to continue.]");
                    break;
                }
            }

            return finalSb.ToString().Trim();
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

        public StatsResponse<MonsterStats> GetMonsterStats(string name)
        {
            var response = new StatsResponse<MonsterStats>();
            var doc = _documents.Values.FirstOrDefault(d => d.FilePath.EndsWith("src\\monst.c", StringComparison.OrdinalIgnoreCase) || d.FilePath.EndsWith("src/monst.c", StringComparison.OrdinalIgnoreCase));
            if (doc == null)
            {
                response.Error = "src/monst.c is not in the source code index. Ensure the GnollHack repository is indexed. Use monster_lookup or wiki_search as a fallback.";
                return response;
            }

            int matchLine = -1;
            string escapedName = Regex.Escape(name);
            var nameRegex = new Regex($@"^\s*(?:ANIMATED_MON|ENLARGED_MON|ENLARGED_ANIMATED_MON|MON)\(\s*""{escapedName}""", RegexOptions.IgnoreCase);

            for (int i = 0; i < doc.ContentLines.Length; i++)
            {
                if (nameRegex.IsMatch(doc.ContentLines[i]))
                {
                    matchLine = i;
                    break;
                }
            }

            if (matchLine == -1)
            {
                response.Error = $"No monster named '{name}' found in the game data. Try monster_lookup or wiki_search for partial matches, or check the spelling.";
                return response;
            }

            var extraction = CLexer.ExtractParenBlock(doc.ContentLines, matchLine);
            if (extraction == null)
            {
                response.Error = "Failed to parse monster definition block.";
                return response;
            }

            string rawDef = doc.ContentLines[matchLine].TrimStart() + "\n" + string.Join("\n", doc.ContentLines.Skip(matchLine + 1).Take(extraction.EndLine - matchLine));

            try
            {
                /* ExtractParenBlock includes outer parens; strip them before tokenizing */
                string innerContent = ExtractInnerArgs(extraction.Content);
                var tokens = _dataParser.ParseMonsterMacroArgs(innerContent);
                if (tokens.Count >= 24) /* MON has at least 24 top-level args before soundset fields */
                {
                    var stats = new MonsterStats();

                    /* Positional fields 0-5: name, title, description, femalename, commonname, mlet */
                    stats.Fields["mname"] = tokens[0].Trim('"');
                    if (tokens[1] != "None") stats.Fields["mtitle"] = tokens[1].Trim('"');
                    if (tokens[2] != "None") stats.Fields["mdescription"] = tokens[2].Trim('"');
                    if (tokens[3] != "None") stats.Fields["mfemalename"] = tokens[3].Trim('"');
                    if (tokens[4] != "None") stats.Fields["mcommonname"] = tokens[4].Trim('"');
                    stats.Fields["mlet"] = tokens[5].Trim();

                    /* Position 6: LVL(lvl, mov, ac, mc, mr, aln) */
                    ParseSubMacro(tokens[6], stats.Fields, new[] { "mlevel", "mmove", "ac", "mc", "mr", "maligntyp" });

                    /* Position 7: geno flags — e.g. (G_GENO | G_LGROUP | 2) */
                    ParseGenoFlags(tokens[7], stats.Fields);

                    /* Position 8: A(ATTK(...), ...) — 8 attack slots */
                    ParseAttacks(tokens[8], stats.Fields);

                    /* Position 9: SIZ(wt, nut, snd, siz, heads, lightrange, mat) */
                    ParseSubMacro(tokens[9], stats.Fields, new[] { "cwt", "cnutrit", "msound", "msize", "heads", "lightrange", "body_material_type" });

                    /* Position 10: STATS(str, dex, con, intl, wis, cha) */
                    ParseSubMacro(tokens[10], stats.Fields, new[] { "str", "dex", "con", "intl", "wis", "cha" });

                    /* Positions 11-13: mresists, mresists2, mconveys */
                    stats.Fields["mresists"] = ParseFlagField(tokens[11]);
                    stats.Fields["mresists2"] = ParseFlagField(tokens[12]);
                    stats.Fields["mconveys"] = ParseFlagField(tokens[13]);

                    /* Positions 14-21: mflags1 through mflags8 */
                    for (int f = 0; f < 8; f++)
                    {
                        stats.Fields[$"mflags{f + 1}"] = ParseFlagField(tokens[14 + f]);
                    }

                    /* Position 22: difficulty */
                    stats.Fields["difficulty"] = TryParseInt(tokens[22]);

                    /* Position 23: mcolor */
                    stats.Fields["mcolor"] = tokens[23].Trim();

                    /* Level 1 success: populate stats, populate flag_descriptions, leave macro/struct empty */
                    response.Stats = stats;
                    PopulateFlagDescriptions(response, tokens);

                    return response;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Level 1 parsing failed for monster '{Name}', falling back to Level 2.", name);
            }

            /* Level 2 fallback: raw dump + context */
            response.RawDefinition = rawDef;
            response.MacroDefinitions = _dataParser.GetMacroDefinitions("MON", "ANIMATED_MON", "ENLARGED_MON", "ENLARGED_ANIMATED_MON", "GENERAL_MON", "LVL", "A", "ATTK", "SIZ", "STATS");
            response.StructDefinitions = _dataParser.GetStructDefinitions("permonst", "attack");
            response.Message = $"Could not parse structured stats for '{name}'. Raw source and context provided for manual interpretation.";
            PopulateFlagDescriptions(response, rawDef);

            return response;
        }

        public IEnumerable<string> SearchMonsters(string query)
        {
            var doc = _documents.Values.FirstOrDefault(d => d.FilePath.EndsWith("src\\monst.c", StringComparison.OrdinalIgnoreCase) || d.FilePath.EndsWith("src/monst.c", StringComparison.OrdinalIgnoreCase));
            if (doc == null) return Enumerable.Empty<string>();

            var results = new List<string>();
            var regex = new Regex(@"^\s*(?:ANIMATED_MON|ENLARGED_MON|ENLARGED_ANIMATED_MON|MON)\(\s*""([^""]+)""", RegexOptions.IgnoreCase);
            foreach (var line in doc.ContentLines)
            {
                var m = regex.Match(line);
                if (m.Success)
                {
                    string monName = m.Groups[1].Value;
                    if (monName.Contains(query, StringComparison.OrdinalIgnoreCase))
                    {
                        results.Add(monName);
                    }
                }
            }
            return results;
        }

        public StatsResponse<ItemStats> GetItemStats(string name)
        {
            var response = new StatsResponse<ItemStats>();
            var doc = _documents.Values.FirstOrDefault(d => d.FilePath.EndsWith("src\\objects.c", StringComparison.OrdinalIgnoreCase) || d.FilePath.EndsWith("src/objects.c", StringComparison.OrdinalIgnoreCase));
            if (doc == null)
            {
                response.Error = "src/objects.c is not in the source code index. Ensure the GnollHack repository is indexed. Use item_lookup or wiki_search as a fallback.";
                return response;
            }

            int matchLine = -1;
            string escapedName = Regex.Escape(name);
            var nameRegex = new Regex($@"^\s*(?:WEAPON|ARMOR|POTION|SCROLL|SPELL|WAND|RING|AMULET|TOOL|GEM|ROCK|COIN|MISCELLANEOUSITEM|SUIT|HELM|CLOAK|SHIELD|GLOVES|BOOTS|SHIRT|ROBE|BRACERS|WEAPONSHIELD|WEAPONBOOTS|WEAPONGLOVES|DRGN_ARMR|FOOD|REAGENT|CHARGEDRING|SPELLTOOL|CONTAINER|WEPTOOL|BOW|PROJECTILE|GENERAL_[A-Z_]+)\(\s*""{escapedName}""", RegexOptions.IgnoreCase);

            for (int i = 0; i < doc.ContentLines.Length; i++)
            {
                if (nameRegex.IsMatch(doc.ContentLines[i]))
                {
                    matchLine = i;
                    break;
                }
            }

            if (matchLine == -1)
            {
                response.Error = $"No item named '{name}' found in the game data. Try item_lookup or wiki_search for partial matches, or check the spelling.";
                return response;
            }

            var extraction = CLexer.ExtractParenBlock(doc.ContentLines, matchLine);
            if (extraction == null)
            {
                response.Error = "Failed to parse item definition block.";
                return response;
            }

            string rawDef = doc.ContentLines[matchLine].TrimStart() + "\n" + string.Join("\n", doc.ContentLines.Skip(matchLine + 1).Take(extraction.EndLine - matchLine));

            /* Level 2 for items — raw dump + context (structured parsing requires per-macro handlers) */
            response.RawDefinition = rawDef;
            response.MacroDefinitions = _dataParser.GetMacroDefinitions("OBJECT", "OBJ", "BITS", "WEAPON", "ARMOR", "POTION", "SCROLL", "SPELL", "WAND", "RING", "AMULET", "TOOL", "GEM", "ROCK", "COIN", "MISCELLANEOUSITEM", "FOOD", "REAGENT", "BOW", "PROJECTILE");
            response.StructDefinitions = _dataParser.GetStructDefinitions("objclass");
            response.Message = $"Raw source and context provided for '{name}'. Item parsing uses many macro formats; interpret using the macro definitions provided.";
            PopulateFlagDescriptions(response, rawDef);

            return response;
        }

        public IEnumerable<string> SearchItems(string query)
        {
            var doc = _documents.Values.FirstOrDefault(d => d.FilePath.EndsWith("src\\objects.c", StringComparison.OrdinalIgnoreCase) || d.FilePath.EndsWith("src/objects.c", StringComparison.OrdinalIgnoreCase));
            if (doc == null) return Enumerable.Empty<string>();

            var results = new List<string>();
            var regex = new Regex(@"^\s*(?:WEAPON|ARMOR|POTION|SCROLL|SPELL|WAND|RING|AMULET|TOOL|GEM|ROCK|COIN|MISCELLANEOUSITEM|SUIT|HELM|CLOAK|SHIELD|GLOVES|BOOTS|SHIRT|ROBE|BRACERS|WEAPONSHIELD|WEAPONBOOTS|WEAPONGLOVES|DRGN_ARMR|FOOD|REAGENT|CHARGEDRING|SPELLTOOL|CONTAINER|WEPTOOL|BOW|PROJECTILE|GENERAL_[A-Z_]+)\(\s*""([^""]+)""", RegexOptions.IgnoreCase);
            foreach (var line in doc.ContentLines)
            {
                var m = regex.Match(line);
                if (m.Success)
                {
                    string itemName = m.Groups[1].Value;
                    if (itemName.Contains(query, StringComparison.OrdinalIgnoreCase))
                    {
                        results.Add(itemName);
                    }
                }
            }
            return results;
        }

        public StatsResponse<ArtifactStats> GetArtifactStats(string name)
        {
            var response = new StatsResponse<ArtifactStats>();
            var doc = _documents.Values.FirstOrDefault(d => d.FilePath.EndsWith("include\\artilist.h", StringComparison.OrdinalIgnoreCase) || d.FilePath.EndsWith("include/artilist.h", StringComparison.OrdinalIgnoreCase));
            if (doc == null)
            {
                response.Error = "include/artilist.h is not in the source code index. Ensure the GnollHack repository is indexed. Use wiki_search as a fallback.";
                return response;
            }

            int matchLine = -1;
            bool isGeneralArtifact = false;
            string escapedName = Regex.Escape(name);
            var nameRegex = new Regex($@"^\s*(?:GENERAL_ARTIFACT|A)\(\s*""{escapedName}""", RegexOptions.IgnoreCase);

            for (int i = 0; i < doc.ContentLines.Length; i++)
            {
                if (nameRegex.IsMatch(doc.ContentLines[i]))
                {
                    matchLine = i;
                    isGeneralArtifact = doc.ContentLines[i].TrimStart().StartsWith("GENERAL_ARTIFACT(", StringComparison.OrdinalIgnoreCase);
                    break;
                }
            }

            if (matchLine == -1)
            {
                response.Error = $"No artifact named '{name}' found in the game data. Try wiki_search for partial matches, or check the spelling.";
                return response;
            }

            var extraction = CLexer.ExtractParenBlock(doc.ContentLines, matchLine);
            if (extraction == null)
            {
                response.Error = "Failed to parse artifact definition block.";
                return response;
            }

            string rawDef = doc.ContentLines[matchLine].TrimStart() + "\n" + string.Join("\n", doc.ContentLines.Skip(matchLine + 1).Take(extraction.EndLine - matchLine));

            try
            {
                /* ExtractParenBlock includes outer parens; strip them before tokenizing */
                string innerContent = ExtractInnerArgs(extraction.Content);
                var tokens = _dataParser.ParseMonsterMacroArgs(innerContent);
                int minTokens = isGeneralArtifact ? 37 : 34;
                if (tokens.Count >= minTokens)
                {
                    var stats = new ArtifactStats();

                    /* Positional fields for A() macro — 34 parameters */
                    /* 0: name */
                    stats.Fields["name"] = tokens[0].Trim('"');
                    /* 1: desc (unidentified name) */
                    if (tokens[1].Trim() != "None") stats.Fields["desc"] = tokens[1].Trim('"');
                    /* 2: hit_desc */
                    if (tokens[2].Trim() != "None") stats.Fields["hit_desc"] = tokens[2].Trim('"');
                    /* 3: typ (base object type) */
                    stats.Fields["otyp"] = tokens[3].Trim();
                    /* 4: masktyp */
                    stats.Fields["maskotyp"] = tokens[4].Trim();
                    /* 5: material */
                    stats.Fields["material"] = tokens[5].Trim();
                    /* 6: exceptionality */
                    stats.Fields["exceptionality"] = tokens[6].Trim();
                    /* 7: mythic_prefix */
                    stats.Fields["mythic_prefix"] = tokens[7].Trim();
                    /* 8: mythic_suffix */
                    stats.Fields["mythic_suffix"] = tokens[8].Trim();
                    /* 9: aflags */
                    stats.Fields["aflags"] = ParseFlagField(tokens[9]);
                    /* 10: aflags2 */
                    stats.Fields["aflags2"] = ParseFlagField(tokens[10]);
                    /* 11: spfx (wielded/worn special effects) */
                    stats.Fields["spfx"] = ParseFlagField(tokens[11]);
                    /* 12: cspfx (carried special effects) */
                    stats.Fields["cspfx"] = ParseFlagField(tokens[12]);
                    /* 13: mtype (monster type, symbol, or flag) */
                    stats.Fields["mtype"] = tokens[13].Trim();
                    /* 14-16: tohit dice */
                    stats.Fields["tohit_dice"] = TryParseInt(tokens[14]);
                    stats.Fields["tohit_diesize"] = TryParseInt(tokens[15]);
                    stats.Fields["tohit_plus"] = TryParseInt(tokens[16]);
                    /* 17: attk (attack sub-macro, e.g. PHYS(1,10)) */
                    stats.Fields["attk"] = tokens[17].Trim();
                    /* 18: worn_prop (defense property when wielded/worn) */
                    stats.Fields["worn_prop"] = tokens[18].Trim();
                    /* 19: carried_prop */
                    stats.Fields["carried_prop"] = tokens[19].Trim();
                    /* 20: inv_prop (invoke property) */
                    stats.Fields["inv_prop"] = tokens[20].Trim();
                    /* 21-23: invoke duration dice */
                    stats.Fields["inv_duration_dice"] = TryParseInt(tokens[21]);
                    stats.Fields["inv_duration_diesize"] = TryParseInt(tokens[22]);
                    stats.Fields["inv_duration_plus"] = TryParseInt(tokens[23]);
                    /* 24: inv_mana_cost */
                    stats.Fields["inv_mana_cost"] = TryParseInt(tokens[24]);
                    /* 25: repower_time */
                    stats.Fields["repower_time"] = TryParseInt(tokens[25]);
                    /* 26: alignment */
                    stats.Fields["alignment"] = tokens[26].Trim();
                    /* 27: role */
                    stats.Fields["role"] = tokens[27].Trim();
                    /* 28: race */
                    stats.Fields["race"] = tokens[28].Trim();
                    /* 29: cost */
                    string costVal = tokens[29].Trim().TrimEnd('L', 'l');
                    stats.Fields["cost"] = TryParseInt(costVal);
                    /* 30: acolor (glow color) */
                    stats.Fields["acolor"] = tokens[30].Trim();
                    /* 31: ocolor (object color override) */
                    stats.Fields["ocolor"] = tokens[31].Trim();
                    /* 32: tile_floor_height */
                    stats.Fields["tile_floor_height"] = TryParseInt(tokens[32]);
                    /* 33: soundset */
                    stats.Fields["soundset"] = tokens[33].Trim();

                    /* GENERAL_ARTIFACT has 3 extra fields */
                    if (isGeneralArtifact && tokens.Count >= 37)
                    {
                        stats.Fields["stand_animation"] = tokens[34].Trim();
                        stats.Fields["enlargement"] = tokens[35].Trim();
                        stats.Fields["replacement"] = tokens[36].Trim();
                    }

                    /* Level 1 success */
                    response.Stats = stats;
                    PopulateFlagDescriptions(response, tokens);

                    return response;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Level 1 parsing failed for artifact '{Name}', falling back to Level 2.", name);
            }

            /* Level 2 fallback: raw dump + context */
            response.RawDefinition = rawDef;
            response.MacroDefinitions = _dataParser.GetMacroDefinitions("GENERAL_ARTIFACT", "A", "PHYS", "PHYSI", "DRLI", "COLD", "FIRE", "ELEC", "STUN", "NO_ATTK");
            response.StructDefinitions = _dataParser.GetStructDefinitions("artifact", "attack");
            response.Message = $"Could not parse structured stats for '{name}'. Raw source and context provided for manual interpretation.";
            PopulateFlagDescriptions(response, rawDef);

            return response;
        }

        public IEnumerable<string> SearchArtifacts(string query)
        {
            var doc = _documents.Values.FirstOrDefault(d => d.FilePath.EndsWith("include\\artilist.h", StringComparison.OrdinalIgnoreCase) || d.FilePath.EndsWith("include/artilist.h", StringComparison.OrdinalIgnoreCase));
            if (doc == null) return Enumerable.Empty<string>();

            var results = new List<string>();
            var regex = new Regex(@"^\s*(?:GENERAL_ARTIFACT|A)\(\s*""([^""]+)""", RegexOptions.IgnoreCase);
            foreach (var line in doc.ContentLines)
            {
                var m = regex.Match(line);
                if (m.Success)
                {
                    string artifactName = m.Groups[1].Value;
                    if (!string.IsNullOrEmpty(artifactName) && artifactName.Contains(query, StringComparison.OrdinalIgnoreCase))
                    {
                        results.Add(artifactName);
                    }
                }
            }
            return results;
        }

        /* --- Helper methods for monster stats Level 1 parsing --- */

        /// <summary>
        /// Parse a sub-macro like LVL(3, 9, 8, 0, 0, -4) into named fields.
        /// </summary>
        private static void ParseSubMacro(string token, Dictionary<string, object> fields, string[] fieldNames)
        {
            string inner = ExtractInnerArgs(token);
            var args = SplitTopLevelCommas(inner);
            for (int i = 0; i < fieldNames.Length && i < args.Count; i++)
            {
                string val = args[i].Trim();
                fields[fieldNames[i]] = TryParseInt(val);
            }
        }

        /// <summary>
        /// Parse geno flags like (G_GENO | G_LGROUP | 2) into a list of flag names + frequency.
        /// </summary>
        private static void ParseGenoFlags(string token, Dictionary<string, object> fields)
        {
            string inner = token.Trim();
            /* Remove outer parens if present */
            if (inner.StartsWith("(") && inner.EndsWith(")"))
                inner = inner.Substring(1, inner.Length - 2).Trim();

            var parts = inner.Split('|').Select(p => p.Trim()).Where(p => !string.IsNullOrEmpty(p)).ToList();
            var flagNames = new List<string>();
            int? frequency = null;
            foreach (var part in parts)
            {
                if (int.TryParse(part, out int freq))
                    frequency = freq;
                else
                    flagNames.Add(part);
            }
            fields["geno"] = flagNames;
            if (frequency.HasValue)
                fields["geno_frequency"] = frequency.Value;
        }

        /// <summary>
        /// Parse A(ATTK(...), ATTK(...), ..., NO_ATTK, ...) into mattk array.
        /// </summary>
        private static void ParseAttacks(string token, Dictionary<string, object> fields)
        {
            string inner = ExtractInnerArgs(token);
            var attackTokens = SplitTopLevelCommas(inner);
            var attacks = new List<Dictionary<string, object>>();
            var attackFields = new[] { "aatyp", "adtyp", "damn", "damd", "damp", "mcadj", "mlevel", "range", "aflags", "action_tile" };

            foreach (var atkToken in attackTokens)
            {
                string trimmed = atkToken.Trim();
                if (trimmed == "NO_ATTK") continue;
                if (trimmed.StartsWith("ATTK(", StringComparison.OrdinalIgnoreCase))
                {
                    string atkInner = ExtractInnerArgs(trimmed);
                    var atkArgs = SplitTopLevelCommas(atkInner);
                    var attack = new Dictionary<string, object>();
                    for (int i = 0; i < attackFields.Length && i < atkArgs.Count; i++)
                    {
                        string val = atkArgs[i].Trim();
                        /* aflags often have UL suffix */
                        if (val.EndsWith("UL", StringComparison.OrdinalIgnoreCase))
                            val = val.Substring(0, val.Length - 2);
                        attack[attackFields[i]] = TryParseInt(val);
                    }
                    attacks.Add(attack);
                }
            }
            fields["mattk"] = attacks;
        }

        /// <summary>
        /// Parse a flag field like "M1_HUMANOID | M1_CARNIVORE" into a list, or a simple value.
        /// </summary>
        private static object ParseFlagField(string token)
        {
            string trimmed = token.Trim();
            if (trimmed.Contains('|'))
            {
                var flags = trimmed.Split('|').Select(p => p.Trim()).Where(p => !string.IsNullOrEmpty(p)).ToList();
                return flags;
            }
            return trimmed;
        }

        /// <summary>
        /// Extract the inner arguments from a macro call: "LVL(3, 9, 8)" -> "3, 9, 8"
        /// </summary>
        private static string ExtractInnerArgs(string token)
        {
            int openParen = token.IndexOf('(');
            if (openParen < 0) return token;
            int closeParen = token.LastIndexOf(')');
            if (closeParen <= openParen) return token.Substring(openParen + 1);
            return token.Substring(openParen + 1, closeParen - openParen - 1);
        }

        /// <summary>
        /// Split by commas at depth 0 (respecting nested parens).
        /// </summary>
        private static List<string> SplitTopLevelCommas(string text)
        {
            var result = new List<string>();
            int depth = 0;
            int start = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '(' || c == '{') depth++;
                else if (c == ')' || c == '}') depth--;
                else if (c == ',' && depth == 0)
                {
                    result.Add(text.Substring(start, i - start));
                    start = i + 1;
                }
            }
            if (start < text.Length) result.Add(text.Substring(start));
            return result;
        }

        /// <summary>
        /// Try to parse a string as integer; return the int if successful, otherwise the trimmed string.
        /// </summary>
        private static object TryParseInt(string val)
        {
            string trimmed = val.Trim();
            if (int.TryParse(trimmed, out int intVal)) return intVal;
            return trimmed;
        }

        /// <summary>
        /// Populate flag_descriptions from tokens list. Uses flag name as own description if not in dictionary (Fix #6).
        /// </summary>
        private void PopulateFlagDescriptions<T>(StatsResponse<T> response, List<string> tokens)
        {
            foreach (var token in tokens)
            {
                foreach (var part in token.Split('|', '(', ')', ' ', '\t', ','))
                {
                    string p = part.Trim();
                    if (string.IsNullOrEmpty(p)) continue;
                    /* Only include tokens that look like flag constants (contain underscore and uppercase) */
                    if (!p.Contains('_') || p.All(c => char.IsDigit(c) || c == '-')) continue;
                    if (_flagDescriptions.TryGetValue(p, out var desc))
                        response.FlagDescriptions[p] = desc;
                    else if (Regex.IsMatch(p, @"^[A-Z][A-Z0-9_]+$"))
                        response.FlagDescriptions[p] = p; /* Flag name as own description */
                }
            }
        }

        /// <summary>
        /// Populate flag_descriptions from raw definition string.
        /// </summary>
        private void PopulateFlagDescriptions<T>(StatsResponse<T> response, string rawDefinition)
        {
            foreach (var part in rawDefinition.Split('|', '(', ')', ',', ' ', '\t', '\n', '\r'))
            {
                string p = part.Trim();
                if (string.IsNullOrEmpty(p)) continue;
                if (!p.Contains('_') || p.All(c => char.IsDigit(c) || c == '-')) continue;
                if (_flagDescriptions.TryGetValue(p, out var desc))
                    response.FlagDescriptions[p] = desc;
                else if (Regex.IsMatch(p, @"^[A-Z][A-Z0-9_]+$"))
                    response.FlagDescriptions[p] = p;
            }
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
