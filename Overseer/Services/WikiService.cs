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
    private StandardAnalyzer? _analyzer;
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

    public IEnumerable<string> GetRelevantSnippets(string query, string? categoryFilter, int maxResults, int perResultChars)
    {
        IndexSearcher? searcher;
        StandardAnalyzer? analyzer;
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
            boolQuery.Add(new WildcardQuery(new Term("path", $"*{categoryFilter}*")), Occur.MUST);
            luceneQuery = boolQuery;
        }
        
        var hits = searcher.Search(luceneQuery, maxResults > 0 ? maxResults : 5);
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

    /// <summary>
    /// How many characters of the heading list a section-miss marker line carries before it is
    /// truncated with an ellipsis.
    /// </summary>
    private const int SectionMissHeadingListMaxChars = 600;

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
        StandardAnalyzer? analyzer;
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
            if (string.Equals(candidate.Get("title"), normalized, StringComparison.OrdinalIgnoreCase))
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
            content = ExtractMarkdownSection(content, section);
        }

        return $"--- {label} ---\n{content}";
    }

    /// <summary>
    /// The requested section's text: the matched heading line, then every line up to but not
    /// including the next heading of the same or a higher level. Matching runs in two passes over
    /// the article's headings — case-insensitive equality on the heading text, then equality with
    /// both sides normalised — so a heading carrying an emoji prefix (<c>## 🔮 Elbereth</c>)
    /// answers a request for <c>Elbereth</c>, while an exact heading always beats a normalised
    /// one. Either pass takes the first heading it matches. A section no heading matches yields a
    /// marker line naming the article's headings, followed by the whole article.
    /// </summary>
    private string ExtractMarkdownSection(string content, string section)
    {
        var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        var headings = new List<(int LineIndex, int Level, string Title)>();

        for (int i = 0; i < lines.Length; i++)
        {
            if (!lines[i].TrimStart().StartsWith("#"))
            {
                continue;
            }

            var match = Regex.Match(lines[i], @"^(#+)\s+(.*)");
            if (match.Success)
            {
                headings.Add((i, match.Groups[1].Value.Length, match.Groups[2].Value.Trim()));
            }
        }

        int selected = headings.FindIndex(h => h.Title.Equals(section, StringComparison.OrdinalIgnoreCase));

        if (selected < 0)
        {
            string normalizedSection = NormalizeHeadingTitle(section);
            if (normalizedSection.Length > 0)
            {
                selected = headings.FindIndex(
                    h => NormalizeHeadingTitle(h.Title).Equals(normalizedSection, StringComparison.OrdinalIgnoreCase));
            }
        }

        if (selected < 0)
        {
            return $"[Section '{section}' not found in article.{BuildHeadingListFragment(headings.Select(h => h.Title))} Returning full text.]\n\n{content}";
        }

        int sectionLevel = headings[selected].Level;
        int endLine = lines.Length;

        for (int i = selected + 1; i < headings.Count; i++)
        {
            if (headings[i].Level <= sectionLevel)
            {
                endLine = headings[i].LineIndex;
                break;
            }
        }

        var sb = new System.Text.StringBuilder();
        for (int i = headings[selected].LineIndex; i < endLine; i++)
        {
            sb.AppendLine(lines[i]);
        }

        return sb.ToString();
    }

    /// <summary>
    /// A heading title reduced to what a <c>section</c> request is compared against: every leading
    /// character that is not a letter or digit removed — emoji, variation selectors, punctuation
    /// and whitespace — internal whitespace runs collapsed to one space, and the result trimmed.
    /// Stripping is leading only, so a plain heading stays distinct from a decorated one of the
    /// same name.
    /// </summary>
    private static string NormalizeHeadingTitle(string title)
    {
        int start = 0;
        while (start < title.Length && !char.IsLetterOrDigit(title[start]))
        {
            start++;
        }

        var sb = new System.Text.StringBuilder(title.Length - start);
        bool pendingSpace = false;

        for (int i = start; i < title.Length; i++)
        {
            char c = title[i];

            if (char.IsWhiteSpace(c))
            {
                pendingSpace = sb.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                sb.Append(' ');
                pendingSpace = false;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    /// <summary>
    /// The <c>" Headings: a; b; c."</c> fragment the section-miss marker line carries, listing the
    /// article's headings in document order exactly as written so one can be copied straight back
    /// into a <c>section</c> argument. Capped, because the marker line precedes the whole article
    /// and every tool result is re-sent to the model on each subsequent round. An article with no
    /// headings contributes nothing.
    /// </summary>
    private static string BuildHeadingListFragment(IEnumerable<string> titles)
    {
        string list = string.Join("; ", titles);
        if (list.Length == 0)
        {
            return string.Empty;
        }

        if (list.Length > SectionMissHeadingListMaxChars)
        {
            int cut = SectionMissHeadingListMaxChars;

            // A heading list is mostly emoji at the boundaries, and cutting between the two halves
            // of a surrogate pair leaves a lone surrogate the serializer renders as U+FFFD.
            if (char.IsHighSurrogate(list[cut - 1])) cut--;

            list = list.Substring(0, cut) + "…";
        }

        return $" Headings: {list}.";
    }

    public void Dispose()
    {
        _reader?.Dispose();
        _directory?.Dispose();
        _reindexTimer?.Dispose();
        _reindexTimer = null;
    }
}
