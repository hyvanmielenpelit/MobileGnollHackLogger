using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Search.Similarities;
using Lucene.Net.Store;
using Lucene.Net.Util;
using Lucene.Net.QueryParsers.Classic;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System;
using System.Text.RegularExpressions;

namespace Overseer.Services;

public class NetHackWikiService : IDisposable
{
    private readonly string _wikiPath;
    private readonly int _maxFileSizeKB;
    private readonly ILogger<NetHackWikiService>? _logger;
    private readonly object _swapLock = new();
    private RAMDirectory? _directory;
    private DirectoryReader? _reader;
    private IndexSearcher? _searcher;
    private StandardAnalyzer? _analyzer;
    private Timer? _reindexTimer;

    public NetHackWikiService(IConfiguration configuration, ILogger<NetHackWikiService>? logger = null)
    {
        _logger = logger;
        _wikiPath = configuration["NetHackWikiPath"] ?? string.Empty;
        _maxFileSizeKB = int.TryParse(configuration["MaxNetHackWikiFileSizeKB"], out var maxFileSize) ? maxFileSize : 500;

        IndexWikiFiles();
        
        // Re-index every 10 minutes
        _reindexTimer = new Timer(_ => IndexWikiFiles(), null, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10));
    }

    private void IndexWikiFiles()
    {
        if (string.IsNullOrWhiteSpace(_wikiPath))
        {
            _logger?.LogWarning("NetHackWiki directory not configured (NetHackWikiPath is empty).");
            return;
        }

        if (!System.IO.Directory.Exists(_wikiPath))
        {
            _logger?.LogWarning("NetHackWiki directory not found: {Path}", _wikiPath);
            return;
        }

        var files = System.IO.Directory.GetFiles(_wikiPath, "*.md", SearchOption.AllDirectories).ToList();

        _analyzer = new StandardAnalyzer(LuceneVersion.LUCENE_48);
        
        // Build the new index into a fresh directory
        var newDirectory = new RAMDirectory();
        var config = new IndexWriterConfig(LuceneVersion.LUCENE_48, _analyzer)
        {
            Similarity = new BM25Similarity()  // BM25 scoring
        };
        
        int indexedCount = 0;
        using (var writer = new IndexWriter(newDirectory, config))
        {
            foreach (var file in files)
            {
                var fileInfo = new FileInfo(file);
                if (fileInfo.Length <= _maxFileSizeKB * 1024)
                {
                    try
                    {
                        var rawContent = File.ReadAllText(file);
                        string title = Path.GetFileNameWithoutExtension(file);
                        string ns = "article";
                        string summary = "";
                        string bodyContent = rawContent;

                        var match = Regex.Match(rawContent, @"^---\s*\n(.*?)\n---\s*\n(.*)$", RegexOptions.Singleline);
                        if (match.Success)
                        {
                            var frontmatter = match.Groups[1].Value;
                            bodyContent = match.Groups[2].Value.TrimStart();

                            var lines = frontmatter.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                            foreach (var line in lines)
                            {
                                var colonIndex = line.IndexOf(':');
                                if (colonIndex > 0)
                                {
                                    var key = line.Substring(0, colonIndex).Trim().ToLowerInvariant();
                                    var value = line.Substring(colonIndex + 1).Trim();
                                    value = value.Trim('"', ' ').Replace("\\\"", "\"").Replace("\\\\", "\\");

                                    if (key == "title" && !string.IsNullOrWhiteSpace(value))
                                    {
                                        title = value;
                                    }
                                    else if (key == "namespace" && !string.IsNullOrWhiteSpace(value))
                                    {
                                        ns = value.ToLowerInvariant();
                                    }
                                    else if (key == "summary" && !string.IsNullOrWhiteSpace(value))
                                    {
                                        summary = value;
                                    }
                                }
                            }
                        }

                        var doc = new Document();
                        doc.Add(new TextField("title", title, Field.Store.YES));
                        doc.Add(new TextField("content", bodyContent, Field.Store.YES));
                        doc.Add(new StringField("path", file, Field.Store.YES));
                        doc.Add(new StringField("filename", Path.GetFileName(file), Field.Store.YES));
                        doc.Add(new StringField("namespace", ns, Field.Store.YES));
                        doc.Add(new TextField("summary", summary, Field.Store.YES));
                        writer.AddDocument(doc);
                        indexedCount++;
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Error indexing NetHack wiki file: {File}", file);
                    }
                }
            }
            writer.Commit();
        }
        
        var newReader = DirectoryReader.Open(newDirectory);
        var newSearcher = new IndexSearcher(newReader)
        {
            Similarity = new BM25Similarity()
        };
        
        // Hot-swap: atomically replace the old index, then dispose of the old one
        RAMDirectory? oldDirectory;
        DirectoryReader? oldReader;
        lock (_swapLock)
        {
            oldDirectory = _directory;
            oldReader = _reader;
            _directory = newDirectory;
            _reader = newReader;
            _searcher = newSearcher;
        }
        
        // Dispose old resources OUTSIDE the lock to avoid blocking queries
        oldReader?.Dispose();
        oldDirectory?.Dispose();

        _logger?.LogInformation("Indexed {Count} NetHack wiki articles.", indexedCount);
    }
    
    public IEnumerable<string> GetRelevantContext(string query, string? namespaceFilter = null, int? maxResults = null)
    {
        IndexSearcher? searcher;
        StandardAnalyzer? analyzer;
        lock (_swapLock)
        {
            searcher = _searcher;
            analyzer = _analyzer;
        }
        if (searcher == null || analyzer == null || string.IsNullOrWhiteSpace(query)) return Enumerable.Empty<string>();
        
        // Build a BooleanQuery that searches title (boosted), summary, and content
        var parser = new MultiFieldQueryParser(
            LuceneVersion.LUCENE_48,
            new[] { "title", "summary", "content" },
            analyzer,
            new Dictionary<string, float> { { "title", 5.0f }, { "summary", 2.0f }, { "content", 1.0f } }
        );
        
        Query luceneQuery;
        try
        {
            luceneQuery = parser.Parse(QueryParserBase.Escape(query));
        }
        catch (Lucene.Net.QueryParsers.Classic.ParseException)
        {
            return Enumerable.Empty<string>(); // Ignore parse errors
        }
        
        // Apply namespace filter if provided
        if (!string.IsNullOrWhiteSpace(namespaceFilter))
        {
            var boolQuery = new BooleanQuery();
            boolQuery.Add(luceneQuery, Occur.MUST);
            boolQuery.Add(new TermQuery(new Term("namespace", namespaceFilter.Trim().ToLowerInvariant())), Occur.MUST);
            luceneQuery = boolQuery;
        }
        
        var hits = searcher.Search(luceneQuery, maxResults ?? 5);
        var results = new List<string>();
        
        foreach (var hit in hits.ScoreDocs)
        {
            var doc = searcher.Doc(hit.Doc);
            string title = doc.Get("title") ?? Path.GetFileNameWithoutExtension(doc.Get("filename") ?? "");
            string content = doc.Get("content");
            results.Add($"--- {title} ---\n{content}");
        }
        
        return results;
    }

    public string? GetArticle(string articleName, string? section = null)
    {
        IndexSearcher? searcher;
        StandardAnalyzer? analyzer;
        lock (_swapLock)
        {
            searcher = _searcher;
            analyzer = _analyzer;
        }
        if (searcher == null || analyzer == null || string.IsNullOrWhiteSpace(articleName)) return null;
        
        // Try exact match on title or filename
        var parser = new MultiFieldQueryParser(
            LuceneVersion.LUCENE_48,
            new[] { "title", "filename" },
            analyzer
        );
        Query luceneQuery;
        try
        {
            luceneQuery = parser.Parse(QueryParserBase.Escape(articleName));
        }
        catch (Lucene.Net.QueryParsers.Classic.ParseException)
        {
            return null;
        }
        
        var hits = searcher.Search(luceneQuery, 1);
        if (hits.TotalHits == 0) return null;
        
        var doc = searcher.Doc(hits.ScoreDocs[0].Doc);
        string title = doc.Get("title") ?? Path.GetFileNameWithoutExtension(doc.Get("filename") ?? "");
        string content = doc.Get("content");
        
        if (!string.IsNullOrWhiteSpace(section))
        {
            content = ExtractMarkdownSection(content, section);
        }
        
        return $"--- {title} ---\n{content}";
    }

    private string ExtractMarkdownSection(string content, string section)
    {
        var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        var sb = new System.Text.StringBuilder();
        bool inSection = false;
        int sectionLevel = -1;
        
        foreach (var line in lines)
        {
            if (line.TrimStart().StartsWith("#"))
            {
                var match = Regex.Match(line, @"^(#+)\s+(.*)");
                if (match.Success)
                {
                    int level = match.Groups[1].Value.Length;
                    string title = match.Groups[2].Value.Trim();
                    
                    if (title.Equals(section, StringComparison.OrdinalIgnoreCase))
                    {
                        inSection = true;
                        sectionLevel = level;
                        sb.AppendLine(line);
                        continue;
                    }
                    else if (inSection && level <= sectionLevel)
                    {
                        break;
                    }
                }
            }
            
            if (inSection)
            {
                sb.AppendLine(line);
            }
        }
        
        if (!inSection)
        {
            return $"[Section '{section}' not found in article. Returning full text.]\n\n{content}";
        }
        
        return sb.ToString();
    }
    
    public void Dispose()
    {
        _reader?.Dispose();
        _directory?.Dispose();
        _reindexTimer?.Dispose();
        _reindexTimer = null;
    }
}
