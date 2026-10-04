using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.En;
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
    private readonly int _indexingParallelism;
    private readonly ILogger<NetHackWikiService>? _logger;
    private readonly object _swapLock = new();
    private RAMDirectory? _directory;
    private DirectoryReader? _reader;
    private IndexSearcher? _searcher;
    private Analyzer? _analyzer;

    public Task InitializationTask { get; private set; }
    public bool IsIndexingComplete => InitializationTask?.IsCompleted ?? false;

    /// <summary>What the index the tools currently read holds; null until the first pass finishes.</summary>
    public CorpusContentFingerprint? ContentFingerprint { get; private set; }
    
    public NetHackWikiService(IConfiguration configuration, ILogger<NetHackWikiService>? logger = null)
        : this(configuration, logger, null)
    {
    }

    internal NetHackWikiService(IConfiguration configuration, ILogger<NetHackWikiService>? logger, int? indexingParallelism)
    {
        _logger = logger;
        _wikiPath = configuration["NetHackWikiPath"] ?? string.Empty;
        _maxFileSizeKB = int.TryParse(configuration["MaxNetHackWikiFileSizeKB"], out var maxFileSize) ? maxFileSize : 500;
        _indexingParallelism = Math.Max(indexingParallelism ?? Math.Clamp(Environment.ProcessorCount / 2, 1, 8), 1);

        // NetHackWiki consists of thousands of static files that change very seldomly.
        // To avoid heavy periodic disk I/O and Lucene re-indexing, indexing is performed ONLY at startup.
        // If NetHackWiki files are updated on disk, the site must be restarted to re-read them.
        InitializationTask = Task.Run(() => IndexWikiFiles());
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

        // Porter stemming: title and body tokens match across inflections (material/materials).
        _analyzer = new EnglishAnalyzer(LuceneVersion.LUCENE_48);

        // Build the new index into a fresh directory
        var newDirectory = new RAMDirectory();
        var config = new IndexWriterConfig(LuceneVersion.LUCENE_48, _analyzer)
        {
            Similarity = new BM25Similarity()  // BM25 scoring
        };

        // Contiguous chunks merged in order keep document ids in file order, which ties and the candidate list rely on.
        int chunkCount = Math.Min(_indexingParallelism, Math.Max(files.Count, 1));
        int chunkSize = (files.Count + chunkCount - 1) / chunkCount;

        var fingerprint = new CorpusContentFingerprintBuilder();
        int indexedCount = 0;
        if (chunkCount == 1)
        {
            using var writer = new IndexWriter(newDirectory, config);
            foreach (var file in files)
            {
                if (IndexWikiFile(writer, file, fingerprint)) indexedCount++;
            }
            writer.Commit();
        }
        else
        {
            var chunkDirectories = new RAMDirectory[chunkCount];
            var chunkCounts = new int[chunkCount];
            try
            {
                Parallel.For(0, chunkCount, new ParallelOptions { MaxDegreeOfParallelism = chunkCount }, chunk =>
                {
                    var chunkDirectory = new RAMDirectory();
                    chunkDirectories[chunk] = chunkDirectory;
                    using var chunkAnalyzer = new EnglishAnalyzer(LuceneVersion.LUCENE_48);
                    var chunkConfig = new IndexWriterConfig(LuceneVersion.LUCENE_48, chunkAnalyzer)
                    {
                        Similarity = new BM25Similarity()
                    };
                    using var chunkWriter = new IndexWriter(chunkDirectory, chunkConfig);
                    int end = Math.Min((chunk + 1) * chunkSize, files.Count);
                    for (int i = chunk * chunkSize; i < end; i++)
                    {
                        if (IndexWikiFile(chunkWriter, files[i], fingerprint)) chunkCounts[chunk]++;
                    }
                    chunkWriter.Commit();
                });

                var chunkReaders = new List<IndexReader>(chunkCount);
                try
                {
                    foreach (var chunkDirectory in chunkDirectories)
                    {
                        chunkReaders.Add(DirectoryReader.Open(chunkDirectory));
                    }
                    using var writer = new IndexWriter(newDirectory, config);
                    writer.AddIndexes(chunkReaders.ToArray());
                    writer.Commit();
                }
                finally
                {
                    foreach (var chunkReader in chunkReaders) chunkReader.Dispose();
                }
            }
            finally
            {
                foreach (var chunkDirectory in chunkDirectories) chunkDirectory?.Dispose();
            }
            indexedCount = chunkCounts.Sum();
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
            ContentFingerprint = fingerprint.Build();
        }
        
        // Dispose old resources OUTSIDE the lock to avoid blocking queries
        oldReader?.Dispose();
        oldDirectory?.Dispose();

        _logger?.LogInformation("Indexed {Count} NetHack wiki articles.", indexedCount);
    }

    private bool IndexWikiFile(IndexWriter writer, string file, CorpusContentFingerprintBuilder fingerprint)
    {
        var fileInfo = new FileInfo(file);
        if (fileInfo.Length > _maxFileSizeKB * 1024)
        {
            return false;
        }

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
            fingerprint.Add(Path.GetRelativePath(_wikiPath, file), rawContent);
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error indexing NetHack wiki file: {File}", file);
            return false;
        }
    }

    public IEnumerable<string> GetRelevantContext(string query, string? namespaceFilter = null, int? maxResults = null)
    {
        IndexSearcher? searcher;
        Analyzer? analyzer;
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
        return GetArticleResolved(articleName, section).Content;
    }

    /// <summary>
    /// Resolves an article exactly as <see cref="GetArticle"/> does, and additionally returns
    /// the winning title plus a deduplicated candidate list (the first 5 title/filename hits,
    /// then the first 5 summary hits, in index order) so a caller can tell when the request did
    /// not match verbatim. The article itself is the first of the title/filename hits (up to 8,
    /// widened past the candidate list's first 5 so a stem match cannot crowd out an exact one)
    /// whose normalized title equals the normalized request, or the top hit if none does - the
    /// summary query only widens the candidate list, never the article chosen. When the
    /// title/filename query has no hit, Content and ResolvedTitle stay null even if the summary
    /// query has one, but the candidates it found are still returned.
    /// </summary>
    public (string? Content, string? ResolvedTitle, IReadOnlyList<string> Candidates) GetArticleResolved(string articleName, string? section = null)
    {
        return GetArticleResolved(articleName, section, out _);
    }

    /// <summary>
    /// <see cref="GetArticleResolved(string, string?)"/>, also reporting through
    /// <paramref name="sectionMissed"/> whether <paramref name="section"/> matched no heading of the
    /// resolved article, so Content carries the section-miss marker line and the whole article.
    /// </summary>
    public (string? Content, string? ResolvedTitle, IReadOnlyList<string> Candidates) GetArticleResolved(string articleName, string? section, out bool sectionMissed)
    {
        sectionMissed = false;
        IndexSearcher? searcher;
        Analyzer? analyzer;
        lock (_swapLock)
        {
            searcher = _searcher;
            analyzer = _analyzer;
        }
        if (searcher == null || analyzer == null || string.IsNullOrWhiteSpace(articleName))
        {
            return (null, null, Array.Empty<string>());
        }

        var candidates = new List<string>();

        // Try exact match on title or filename
        var parser = new MultiFieldQueryParser(
            LuceneVersion.LUCENE_48,
            new[] { "title", "filename" },
            analyzer
        );
        ScoreDoc[] titleHits = Array.Empty<ScoreDoc>();
        try
        {
            var luceneQuery = parser.Parse(QueryParserBase.Escape(articleName));
            titleHits = searcher.Search(luceneQuery, 8).ScoreDocs;
            foreach (var hit in titleHits.Take(5))
            {
                var doc = searcher.Doc(hit.Doc);
                string candidateTitle = doc.Get("title") ?? Path.GetFileNameWithoutExtension(doc.Get("filename") ?? "");
                if (!candidates.Contains(candidateTitle)) candidates.Add(candidateTitle);
            }
        }
        catch (Lucene.Net.QueryParsers.Classic.ParseException)
        {
            // Ignore parse errors, as the pre-existing title/filename query always has.
        }

        try
        {
            var summaryParser = new QueryParser(LuceneVersion.LUCENE_48, "summary", analyzer);
            var summaryQuery = summaryParser.Parse(QueryParserBase.Escape(articleName));
            foreach (var hit in searcher.Search(summaryQuery, 5).ScoreDocs)
            {
                var doc = searcher.Doc(hit.Doc);
                string candidateTitle = doc.Get("title") ?? Path.GetFileNameWithoutExtension(doc.Get("filename") ?? "");
                if (!candidates.Contains(candidateTitle)) candidates.Add(candidateTitle);
            }
        }
        catch (Lucene.Net.QueryParsers.Classic.ParseException)
        {
            // Ignore parse errors, same as the title/filename query.
        }
        catch
        {
            // A candidate-list failure must never break article retrieval.
        }

        if (titleHits.Length == 0)
        {
            return (null, null, candidates);
        }

        var normalizedRequest = NormalizeForComparison(articleName);
        var articleDoc = searcher.Doc(titleHits[0].Doc);
        foreach (var hit in titleHits)
        {
            var doc = searcher.Doc(hit.Doc);
            var candidateTitle = doc.Get("title") ?? Path.GetFileNameWithoutExtension(doc.Get("filename") ?? "");
            if (NormalizeForComparison(candidateTitle) == normalizedRequest)
            {
                articleDoc = doc;
                break;
            }
        }
        string title = articleDoc.Get("title") ?? Path.GetFileNameWithoutExtension(articleDoc.Get("filename") ?? "");
        string content = articleDoc.Get("content");

        if (!string.IsNullOrWhiteSpace(section))
        {
            content = MarkdownSectionExtractor.Extract(content, section, out sectionMissed);
        }

        return ($"--- {title} ---\n{content}", title, candidates);
    }

    // Trims, collapses internal whitespace and lowercases, so a request differing from a stored
    // title only by spacing or case still counts as an exact match. NetHackWikiViewTool applies the
    // same rule when it decides whether to prepend a resolution line.
    private static string NormalizeForComparison(string s) => Regex.Replace(s.Trim(), @"\s+", " ").ToLowerInvariant();

    public void Dispose()
    {
        _reader?.Dispose();
        _directory?.Dispose();
    }
}
