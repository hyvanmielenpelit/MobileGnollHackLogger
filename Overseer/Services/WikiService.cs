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
using System.Threading.Tasks;
using System;
using System.Text.RegularExpressions;

namespace Overseer.Services;

public class WikiService : IDisposable
{
    private readonly string _wikiPath;
    private readonly int _maxFileSizeKB;
    private readonly ILogger<WikiService>? _logger;
    private readonly object _swapLock = new();
    private RAMDirectory? _directory;
    private DirectoryReader? _reader;
    private IndexSearcher? _searcher;
    private Analyzer? _analyzer;
    private Timer? _reindexTimer;
    private string? _lastGitSha;

    public Task InitializationTask { get; private set; }
    public bool IsIndexingComplete => InitializationTask?.IsCompleted ?? false;
    
    public WikiService(IConfiguration configuration, ILogger<WikiService>? logger = null)
    {
        _logger = logger;
        _wikiPath = configuration["WikiPath"] ?? "c:\\wiki";
        _maxFileSizeKB = int.TryParse(configuration["MaxWikiFileSizeKB"], out var maxFileSize) ? maxFileSize : 100;

        InitializationTask = Task.Run(() => IndexWikiFiles());
        
        // Check for Git repository updates every 10 minutes
        _reindexTimer = new Timer(CheckForUpdates, null, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10));
    }

    private void CheckForUpdates(object? state)
    {
        try
        {
            if (!System.IO.Directory.Exists(_wikiPath)) return;

            string? currentSha = GitHelper.GetGitHeadSha(_wikiPath);
            if (!string.IsNullOrEmpty(currentSha) && currentSha != _lastGitSha)
            {
                _logger?.LogInformation("Wiki repository update detected ({OldSha} -> {NewSha}). Re-indexing.", _lastGitSha, currentSha);
                Task.Run(() => IndexWikiFiles());
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error checking for Wiki repository updates.");
        }
    }

    private void IndexWikiFiles()
    {
        if (!System.IO.Directory.Exists(_wikiPath)) return;

        _lastGitSha = GitHelper.GetGitHeadSha(_wikiPath);

        var candidates = System.IO.Directory.GetFiles(_wikiPath, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                     || f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
                     || f.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // The wiki root is a working repository whose dot-directories hold agent, planning and
        // editor files. Those are not articles, so they stay out of the index.
        var files = candidates.Where(f => !IsUnderDotDirectory(f)).ToList();
        int skippedDotFiles = candidates.Count - files.Count;
        int indexedCount = 0;

        // Porter stemming: title and body tokens match across inflections (material/materials).
        _analyzer = new EnglishAnalyzer(LuceneVersion.LUCENE_48);

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
                    string relativeFile = GetWikiRelativePath(file);
                    string relativePath = StripFileExtension(relativeFile, Path.GetExtension(file));

                    var doc = new Document();
                    doc.Add(new TextField("title", Path.GetFileNameWithoutExtension(file), Field.Store.YES));
                    doc.Add(new TextField("content", File.ReadAllText(file), Field.Store.YES));
                    doc.Add(new StringField("path", file, Field.Store.YES));
                    doc.Add(new StringField("filename", Path.GetFileName(file), Field.Store.YES));

                    // The human-facing path form, e.g. "Races/Gnoll": what the article parameter
                    // and the disambiguation payload name, and the only field an exact path
                    // lookup can match. relpathlower carries the same value case-folded, because
                    // a StringField is one exact, case-sensitive term.
                    doc.Add(new StringField("relpath", relativePath, Field.Store.YES));
                    doc.Add(new StringField("relpathlower", relativePath.ToLowerInvariant(), Field.Store.NO));

                    // With its extension, e.g. "Races/Gnoll.md": the label every result header
                    // shows. Displayed only, so it is stored without being indexed.
                    doc.Add(new StoredField("relfile", relativeFile));

                    // The case-folded, forward-slashed wiki-relative path (with extension) the
                    // category filter matches, so a lowercase category value still matches a
                    // capitalized wiki directory.
                    doc.Add(new StringField("pathlower", relativeFile.ToLowerInvariant().Replace('\\', '/'), Field.Store.NO));

                    writer.AddDocument(doc);
                    indexedCount++;
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

        _logger?.LogInformation("Indexed {Count} GnollHack wiki articles, skipped {SkippedCount} file(s) under dot-directories.", indexedCount, skippedDotFiles);
    }

    /// <summary>
    /// The file's path relative to the wiki root, with <c>\</c> normalized to <c>/</c>.
    /// </summary>
    private string GetWikiRelativePath(string file)
    {
        return Path.GetRelativePath(_wikiPath, file).Replace('\\', '/');
    }

    /// <summary>
    /// True when any segment of the file's path relative to the wiki root begins with a dot.
    /// </summary>
    private bool IsUnderDotDirectory(string file)
    {
        return GetWikiRelativePath(file)
            .Split('/')
            .Any(segment => segment.StartsWith(".", StringComparison.Ordinal));
    }

    private static string StripFileExtension(string relativeFile, string extension)
    {
        return extension.Length > 0 && relativeFile.Length > extension.Length
            ? relativeFile.Substring(0, relativeFile.Length - extension.Length)
            : relativeFile;
    }

    public IEnumerable<string> GetRelevantContext(string query, string? categoryFilter = null, int? maxResults = null)
    {
        IndexSearcher? searcher;
        Analyzer? analyzer;
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
            boolQuery.Add(new WildcardQuery(new Term("pathlower", $"*{categoryFilter.Trim().ToLowerInvariant().Replace('\\', '/')}*")), Occur.MUST);
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

    public IEnumerable<string> GetRelevantSnippets(string query, string? categoryFilter, int maxResults, int perResultChars)
    {
        return GetRelevantSnippets(query, categoryFilter, maxResults, perResultChars, out _);
    }

    /// <summary>
    /// As <see cref="GetRelevantSnippets(string, string?, int, int)"/>, and additionally reports
    /// the query's total match count through <paramref name="totalHits"/> — which may exceed
    /// <paramref name="maxResults"/> — so a caller can tell the model more articles matched than
    /// were returned.
    /// </summary>
    public IEnumerable<string> GetRelevantSnippets(string query, string? categoryFilter, int maxResults, int perResultChars, out int totalHits)
    {
        totalHits = 0;

        IndexSearcher? searcher;
        Analyzer? analyzer;
        lock (_swapLock)
        {
            searcher = _searcher;
            analyzer = _analyzer;
        }
        if (searcher == null || analyzer == null || string.IsNullOrWhiteSpace(query)) return Enumerable.Empty<string>();

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
            return Enumerable.Empty<string>();
        }

        if (!string.IsNullOrEmpty(categoryFilter))
        {
            var boolQuery = new BooleanQuery();
            boolQuery.Add(luceneQuery, Occur.MUST);
            boolQuery.Add(new WildcardQuery(new Term("pathlower", $"*{categoryFilter.Trim().ToLowerInvariant().Replace('\\', '/')}*")), Occur.MUST);
            luceneQuery = boolQuery;
        }

        var hits = searcher.Search(luceneQuery, maxResults > 0 ? maxResults : 5);
        totalHits = hits.TotalHits;
        var results = new List<string>();
        var queryTerms = WikiSnippetExtractor.ExtractQueryTerms(query);

        foreach (var hit in hits.ScoreDocs)
        {
            var doc = searcher.Doc(hit.Doc);

            // The path form, so a hit on Races/Gnoll is distinguishable from one on
            // Monsters/Gnoll and the header can be passed straight back to wiki_view.
            string articlePath = doc.Get("relfile") ?? doc.Get("filename");
            string content = doc.Get("content");
            results.Add(WikiSnippetExtractor.BuildSnippet(articlePath, content, queryTerms, perResultChars));
        }

        return results;
    }

    /// <summary>The indexed extensions an article request may carry.</summary>
    private static readonly string[] IndexedExtensions = { ".md", ".txt", ".html" };

    /// <summary>How many hits the title query inspects for a title collision.</summary>
    private const int TitleQueryMaxHits = 8;

    /// <summary>How many colliding paths a disambiguation payload names before it elides.</summary>
    private const int DisambiguationMaxCandidates = 6;

    /// <summary>How many hits GetLookupContext inspects for an exact-title match.</summary>
    private const int LookupContextMaxHits = 8;

    /// <summary>How many other hit titles GetLookupContext's exact-match line names.</summary>
    private const int OtherMatchesMaxTitles = 4;

    /// <summary>
    /// The form of an article request that resolution matches against: trimmed, with <c>\</c>
    /// normalized to <c>/</c> and one trailing indexed extension (<c>.md</c>, <c>.txt</c> or
    /// <c>.html</c>, compared case-insensitively) removed, so a filename copied out of a
    /// <c>wiki_search</c> snippet header resolves. Casing is otherwise preserved. A request that
    /// is nothing but an extension keeps it, so <c>".md"</c> stays a term rather than becoming an
    /// empty request.
    /// </summary>
    public static string NormalizeArticleName(string articleName)
    {
        if (string.IsNullOrWhiteSpace(articleName)) return string.Empty;

        string normalized = articleName.Trim().Replace('\\', '/');

        foreach (var extension in IndexedExtensions)
        {
            if (normalized.Length > extension.Length &&
                normalized.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                return normalized.Substring(0, normalized.Length - extension.Length);
            }
        }

        return normalized;
    }

    public string? GetArticle(string articleName, string? section = null)
    {
        return GetArticle(articleName, section, out _);
    }

    /// <summary>
    /// Resolves an article request in three steps: an exact lookup of the path form, then a
    /// title/filename query whose hits are checked for a title collision, and finally the
    /// top-scoring hit with no relevance floor when no indexed title equals the request.
    /// <paramref name="isDisambiguation"/> reports the collision case, where the returned text is
    /// a one-line list of candidate paths rather than an article and <paramref name="section"/> is
    /// not applied.
    /// </summary>
    public string? GetArticle(string articleName, string? section, out bool isDisambiguation)
    {
        isDisambiguation = false;

        IndexSearcher? searcher;
        Analyzer? analyzer;
        lock (_swapLock)
        {
            searcher = _searcher;
            analyzer = _analyzer;
        }
        if (searcher == null || analyzer == null || string.IsNullOrWhiteSpace(articleName)) return null;

        string normalized = NormalizeArticleName(articleName);
        if (string.IsNullOrWhiteSpace(normalized)) return null;

        // A request that contains a slash may be a path, but is not necessarily one, so a miss
        // here falls through to the title query rather than ending in a miss.
        if (normalized.Contains('/'))
        {
            var pathHits = searcher.Search(new TermQuery(new Term("relpathlower", normalized.ToLowerInvariant())), 1);
            if (pathHits.TotalHits > 0)
            {
                return RenderArticle(searcher.Doc(pathHits.ScoreDocs[0].Doc), section);
            }
        }

        var parser = new MultiFieldQueryParser(
            LuceneVersion.LUCENE_48,
            new[] { "title", "filename" },
            analyzer
        );
        Query luceneQuery;
        try
        {
            luceneQuery = parser.Parse(QueryParserBase.Escape(normalized));
        }
        catch (Lucene.Net.QueryParsers.Classic.ParseException)
        {
            return null;
        }

        var hits = searcher.Search(luceneQuery, TitleQueryMaxHits);
        if (hits.TotalHits == 0) return null;

        // Titles collide across categories — every playable race and role has a monster twin,
        // every weapon that is also a skill has a skill twin — so a request that equals an
        // indexed title may name several articles, and the caller has to choose.
        var titleMatches = new List<Document>();
        foreach (var scoreDoc in hits.ScoreDocs.OrderBy(s => s.Doc))
        {
            var candidate = searcher.Doc(scoreDoc.Doc);
            if (TitleEquals(candidate.Get("title"), normalized))
            {
                titleMatches.Add(candidate);
            }
        }

        if (titleMatches.Count > 1)
        {
            var paths = titleMatches
                .Select(d => d.Get("relpath"))
                .Where(p => !string.IsNullOrEmpty(p))
                .ToList();

            if (paths.Count > 1)
            {
                isDisambiguation = true;
                return BuildDisambiguation(normalized, paths);
            }
        }

        if (titleMatches.Count > 0)
        {
            return RenderArticle(titleMatches[0], section);
        }

        // No indexed title equals the request: the best-scoring article stands, with no relevance
        // floor, so a garbled or invented name still yields something rather than a miss.
        return RenderArticle(searcher.Doc(hits.ScoreDocs[0].Doc), section);
    }

    /// <summary>
    /// True when an indexed title equals a normalized request under the case-insensitive
    /// comparison <see cref="GetArticle(string, string?, out bool)"/> and
    /// <see cref="GetLookupContext(string, string)"/> both use to detect an exact-title hit.
    /// </summary>
    private static bool TitleEquals(string? title, string normalizedRequest)
    {
        return string.Equals(title, normalizedRequest, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// As <see cref="GetRelevantContext(string, string?, int?)"/>, but when the request's
    /// normalized form (<see cref="NormalizeArticleName"/>) equals exactly one of the top
    /// <see cref="LookupContextMaxHits"/> hits' titles, returns that article alone — in the same
    /// per-article format <see cref="GetRelevantContext(string, string?, int?)"/> uses — followed
    /// by one <c>[Other matches: …]</c> line naming up to <see cref="OtherMatchesMaxTitles"/> of
    /// the other hit titles, so an exact-name lookup (e.g. "red dragon") is not diluted by
    /// neighbouring articles the boosted title/content query also matched (e.g. "Red dragon scale
    /// mail"). Two or more exact-title hits fall back to the same disambiguation shape
    /// <see cref="GetArticle(string, string?, out bool)"/> uses. Anything else — no exact-title
    /// hit at all — returns exactly what <see cref="GetRelevantContext(string, string?, int?)"/>
    /// returns.
    /// </summary>
    public IEnumerable<string> GetLookupContext(string name, string categoryFilter)
    {
        IndexSearcher? searcher;
        Analyzer? analyzer;
        lock (_swapLock)
        {
            searcher = _searcher;
            analyzer = _analyzer;
        }
        if (searcher == null || analyzer == null || string.IsNullOrWhiteSpace(name)) return Enumerable.Empty<string>();

        var parser = new MultiFieldQueryParser(
            LuceneVersion.LUCENE_48,
            new[] { "title", "content" },
            analyzer,
            new Dictionary<string, float> { { "title", 5.0f }, { "content", 1.0f } }
        );

        Query luceneQuery;
        try
        {
            luceneQuery = parser.Parse(QueryParserBase.Escape(name));
        }
        catch (Lucene.Net.QueryParsers.Classic.ParseException)
        {
            return Enumerable.Empty<string>();
        }

        if (!string.IsNullOrEmpty(categoryFilter))
        {
            var boolQuery = new BooleanQuery();
            boolQuery.Add(luceneQuery, Occur.MUST);
            boolQuery.Add(new WildcardQuery(new Term("pathlower", $"*{categoryFilter.Trim().ToLowerInvariant().Replace('\\', '/')}*")), Occur.MUST);
            luceneQuery = boolQuery;
        }

        var hits = searcher.Search(luceneQuery, LookupContextMaxHits);
        if (hits.TotalHits == 0) return Enumerable.Empty<string>();

        string normalized = NormalizeArticleName(name);
        var docs = hits.ScoreDocs.Select(scoreDoc => searcher.Doc(scoreDoc.Doc)).ToList();
        var matchIndexes = docs
            .Select((doc, index) => (doc, index))
            .Where(x => TitleEquals(x.doc.Get("title"), normalized))
            .Select(x => x.index)
            .ToList();

        if (matchIndexes.Count > 1)
        {
            var paths = matchIndexes
                .Select(i => docs[i].Get("relpath"))
                .Where(p => !string.IsNullOrEmpty(p))
                .ToList();

            if (paths.Count > 1)
            {
                return new[] { BuildDisambiguation(normalized, paths) };
            }
        }

        if (matchIndexes.Count > 0)
        {
            int matchIndex = matchIndexes[0];
            var matchDoc = docs[matchIndex];
            string article = $"--- {matchDoc.Get("filename")} ---\n{matchDoc.Get("content")}";

            var otherTitles = docs
                .Where((_, index) => index != matchIndex)
                .Select(doc => doc.Get("title"))
                .Where(t => !string.IsNullOrEmpty(t))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(OtherMatchesMaxTitles)
                .ToList();

            if (otherTitles.Count > 0)
            {
                article += $"\n[Other matches: {string.Join("; ", otherTitles)}]";
            }

            return new[] { article };
        }

        // No hit's title equals the request: fall back to the same category-filtered top-N join
        // GetRelevantContext returns.
        return GetRelevantContext(name, categoryFilter);
    }

    /// <summary>
    /// One line naming the articles that share a title, as path forms the caller can pass
    /// straight back in. Kept to a few hundred characters: every tool result is re-sent to the
    /// model on each subsequent round of the same question.
    /// </summary>
    private static string BuildDisambiguation(string articleName, List<string> paths)
    {
        var shown = paths.Take(DisambiguationMaxCandidates).ToList();
        string list = string.Join(", ", shown);
        if (paths.Count > shown.Count)
        {
            list += ", …";
        }

        return $"Several wiki articles are titled '{articleName}': {list}. " +
               $"Call wiki_view with the path form, for example article: \"{shown[0]}\".";
    }

    /// <summary>
    /// The article's text under a <c>--- Races/Gnoll.md ---</c> header, so the caller can see
    /// which of several same-titled articles it received.
    /// </summary>
    private string RenderArticle(Document doc, string? section)
    {
        string label = doc.Get("relfile") ?? doc.Get("filename");
        string content = doc.Get("content");

        if (!string.IsNullOrWhiteSpace(section))
        {
            content = MarkdownSectionExtractor.Extract(content, section);
        }

        return $"--- {label} ---\n{content}";
    }

    public void Dispose()
    {
        _reader?.Dispose();
        _directory?.Dispose();
        _reindexTimer?.Dispose();
        _reindexTimer = null;
    }
}
