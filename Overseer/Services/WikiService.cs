using Microsoft.Extensions.Configuration;
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

public class WikiService : IDisposable
{
    private readonly string _wikiPath;
    private readonly int _maxFileSizeKB;
    private readonly object _swapLock = new();
    private RAMDirectory? _directory;
    private DirectoryReader? _reader;
    private IndexSearcher? _searcher;
    private StandardAnalyzer? _analyzer;
    private Timer? _reindexTimer;

    public WikiService(IConfiguration configuration)
    {
        _wikiPath = configuration["WikiPath"] ?? "c:\\wiki";
        _maxFileSizeKB = int.TryParse(configuration["MaxWikiFileSizeKB"], out var maxFileSize) ? maxFileSize : 100;

        IndexWikiFiles();
        
        // Re-index every 10 minutes
        _reindexTimer = new Timer(_ => IndexWikiFiles(), null, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10));
    }

    private void IndexWikiFiles()
    {
        if (!System.IO.Directory.Exists(_wikiPath)) return;

        var files = System.IO.Directory.GetFiles(_wikiPath, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                     || f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
                     || f.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            .ToList();

        _analyzer = new StandardAnalyzer(LuceneVersion.LUCENE_48);
        
        // Build the new index into a fresh directory
        var newDirectory = new RAMDirectory();
        var config = new IndexWriterConfig(LuceneVersion.LUCENE_48, _analyzer)
        {
            Similarity = new BM25Similarity()  // BM25 scoring
        };
        
        using (var writer = new IndexWriter(newDirectory, config))
        {
            foreach (var file in files)
            {
                var fileInfo = new FileInfo(file);
                if (fileInfo.Length <= _maxFileSizeKB * 1024)
                {
                    var doc = new Document();
                    doc.Add(new TextField("title", Path.GetFileNameWithoutExtension(file), Field.Store.YES));
                    doc.Add(new TextField("content", File.ReadAllText(file), Field.Store.YES));
                    doc.Add(new StringField("path", file, Field.Store.YES));
                    doc.Add(new StringField("filename", Path.GetFileName(file), Field.Store.YES));
                    writer.AddDocument(doc);
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
    }
    
    public IEnumerable<string> GetRelevantContext(string query, string? categoryFilter = null, int? maxResults = null)
    {
        IndexSearcher? searcher;
        StandardAnalyzer? analyzer;
        lock (_swapLock)
        {
            searcher = _searcher;
            analyzer = _analyzer;
        }
        if (searcher == null || analyzer == null || string.IsNullOrWhiteSpace(query)) return Enumerable.Empty<string>();
        
        // Build a BooleanQuery that searches both title (boosted) and content
        var parser = new MultiFieldQueryParser(
            LuceneVersion.LUCENE_48,
            new[] { "title", "content" },
            analyzer,
            new Dictionary<string, float> { { "title", 5.0f }, { "content", 1.0f } }
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
        
        // Apply category filter if provided
        if (!string.IsNullOrEmpty(categoryFilter))
        {
            var boolQuery = new BooleanQuery();
            boolQuery.Add(luceneQuery, Occur.MUST);
            boolQuery.Add(new WildcardQuery(new Term("path", $"*{categoryFilter}*")), Occur.MUST);
            luceneQuery = boolQuery;
        }
        
        var hits = searcher.Search(luceneQuery, maxResults ?? 5);
        var results = new List<string>();
        
        foreach (var hit in hits.ScoreDocs)
        {
            var doc = searcher.Doc(hit.Doc);
            string filename = doc.Get("filename");
            string content = doc.Get("content");
            results.Add($"--- {filename} ---\n{content}");
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
        string filename = doc.Get("filename");
        string content = doc.Get("content");
        
        if (!string.IsNullOrWhiteSpace(section))
        {
            content = ExtractMarkdownSection(content, section);
        }
        
        return $"--- {filename} ---\n{content}";
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
