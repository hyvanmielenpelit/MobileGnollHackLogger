using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Overseer.Services.CorpusIndexing;

namespace Overseer.Services;

public class NetHackWikiService : IDisposable
{
    private const string PathConfigKey = "NetHackWikiPath";
    private const string MaxFileSizeConfigKey = "MaxNetHackWikiFileSizeKB";

    private readonly string _wikiPath;
    private readonly int _maxFileSizeKB;
    private readonly int _indexingParallelism;
    private readonly bool _startIndexing;
    private readonly ILogger<NetHackWikiService>? _logger;
    private readonly NetHackWikiConversionReportReader _conversionReports;
    private readonly CorpusIndexJobRunner _runner;
    private readonly object _swapLock = new();
    private bool _disposed;
    private RAMDirectory? _directory;
    private DirectoryReader? _reader;
    private IndexSearcher? _searcher;
    private Analyzer? _analyzer;

    /* The statistics of the index the tools read; published with it under _swapLock. */
    private PublishedIndex? _published;
    private long _generation;

    /// <summary>The startup pass; a completed task when the service was built with <c>startIndexing: false</c>.</summary>
    public Task InitializationTask { get; }

    /// <summary>True once the startup pass has ended; never true for a service built with <c>startIndexing: false</c>.</summary>
    public bool IsIndexingComplete => _startIndexing && (InitializationTask?.IsCompleted ?? false);

    /// <summary>What the index the tools currently read holds; null until the first pass finishes.</summary>
    public CorpusContentFingerprint? ContentFingerprint { get; private set; }

    /// <summary>The id of the most recently started indexing job; null before the first.</summary>
    public string? LastStartedJobId => _runner.LastStartedJobId;
    
    public NetHackWikiService(IConfiguration configuration, ILogger<NetHackWikiService>? logger = null)
        : this(configuration, logger, indexingParallelism: null)
    {
    }

    /// <summary>The constructor dependency injection uses: it has the most parameters it can resolve.</summary>
    public NetHackWikiService(IConfiguration configuration, ILogger<NetHackWikiService>? logger, NetHackWikiConversionReportReader conversionReports)
        : this(configuration, logger, null, startIndexing: true, conversionReports)
    {
    }

    internal NetHackWikiService(IConfiguration configuration, ILogger<NetHackWikiService>? logger, int? indexingParallelism)
        : this(configuration, logger, indexingParallelism, startIndexing: true)
    {
    }

    /// <param name="startIndexing">
    /// False only in tests: the single startup pass never starts and no job is recorded, so the
    /// service reports indexing in progress for its whole life; <see cref="InitializationTask"/> is
    /// a completed task.
    /// </param>
    /// <param name="conversionReports">The reader of the converter's report; null builds one from <paramref name="configuration"/>.</param>
    internal NetHackWikiService(IConfiguration configuration, ILogger<NetHackWikiService>? logger, int? indexingParallelism, bool startIndexing,
        NetHackWikiConversionReportReader? conversionReports = null)
    {
        _logger = logger;
        _wikiPath = configuration[PathConfigKey] ?? string.Empty;
        _maxFileSizeKB = int.TryParse(configuration[MaxFileSizeConfigKey], out var maxFileSize) ? maxFileSize : 500;
        _indexingParallelism = Math.Max(indexingParallelism ?? Math.Clamp(Environment.ProcessorCount / 2, 1, 8), 1);
        _startIndexing = startIndexing;
        _conversionReports = conversionReports ?? new NetHackWikiConversionReportReader(configuration);
        _runner = new CorpusIndexJobRunner("NetHack wiki", (ILogger?)logger ?? NullLogger.Instance);

        // NetHackWiki consists of thousands of static files that change very seldomly.
        // To avoid heavy periodic disk I/O and Lucene re-indexing, indexing is performed at startup and
        // on request only (RequestReindex); there is no timer. If NetHackWiki files are updated on disk,
        // the site must be restarted, or a re-index requested, to re-read them.
        InitializationTask = startIndexing
            ? _runner.TryStart(CorpusIndexTrigger.Startup, RunPassAsync, out _) ?? Task.CompletedTask
            : Task.CompletedTask;
    }

    /// <summary>
    /// Starts a pass that reads the whole tree again: <see cref="CorpusIndexStartResult.NotConfigured"/>
    /// when <c>NetHackWikiPath</c> is not set, <see cref="CorpusIndexStartResult.PathMissing"/> when its
    /// folder does not exist, <see cref="CorpusIndexStartResult.AlreadyRunning"/> while a pass runs.
    /// The previous index keeps serving until the new one is published.
    /// </summary>
    public CorpusIndexStartResult RequestReindex(CorpusIndexTrigger trigger = CorpusIndexTrigger.Manual)
    {
        TryStartReindex(trigger, out var result);
        return result;
    }

    /// <summary><see cref="RequestReindex"/>, also returning the started pass's task; null when none started.</summary>
    internal Task? TryStartReindex(CorpusIndexTrigger trigger, out CorpusIndexStartResult result)
    {
        if (string.IsNullOrWhiteSpace(_wikiPath))
        {
            result = CorpusIndexStartResult.NotConfigured;
            return null;
        }

        if (!System.IO.Directory.Exists(_wikiPath))
        {
            result = CorpusIndexStartResult.PathMissing;
            return null;
        }

        return _runner.TryStart(trigger, RunPassAsync, out result);
    }

    /// <summary>The corpus status for the NetHack Wiki dashboard; reads the published statistics once.</summary>
    public NetHackWikiStatusDto GetStatus()
    {
        PublishedIndex? published;
        lock (_swapLock)
        {
            published = _published;
        }

        bool configured = !string.IsNullOrWhiteSpace(_wikiPath);
        bool pathExists = configured && System.IO.Directory.Exists(_wikiPath);
        bool isGitRepository = pathExists && System.IO.Directory.Exists(Path.Combine(_wikiPath, ".git"));

        var report = _conversionReports.Read(out string? reportError);
        DateTime? reportWriteUtc = report?.ReportLastWriteUtc ?? _conversionReports.GetReportLastWriteUtc();

        return new NetHackWikiStatusDto
        {
            Kind = CorpusKind.NetHackWiki,
            ConfigKey = PathConfigKey,
            Configured = configured,
            Path = configured ? _wikiPath : null,
            PathExists = pathExists,
            IsGitRepository = isGitRepository,
            IsIndexingComplete = IsIndexingComplete,
            CurrentHeadSha = isGitRepository ? GitHelper.GetGitHeadSha(_wikiPath) : null,
            CurrentBranch = isGitRepository ? NullIfEmpty(GitHelper.GetCurrentBranch(_wikiPath)) : null,
            Index = published?.Info,
            UpToDate = true,
            RefreshMode = CorpusRefreshMode.StartupAndManual,
            NextCheckAtUtc = null,
            CheckIntervalMinutes = null,
            CurrentJob = _runner.CurrentJob,
            RecentJobs = _runner.RecentJobs,
            ServerTimeUtc = DateTime.UtcNow,
            Namespaces = published?.Namespaces ?? Array.Empty<NetHackWikiNamespaceDto>(),
            ConversionReport = report,
            ConversionReportError = reportError,
            TreeChangedSinceIndex = published != null && reportWriteUtc is DateTime writeUtc && writeUtc > published.Info.IndexedAtUtc
        };
    }

    /// <summary>
    /// The published index's article count, namespaces and build time; null until a pass publishes.
    /// Reads no file and no Lucene reader, so a reindex cannot close anything under it.
    /// </summary>
    public NetHackWikiOverview? GetOverview()
    {
        PublishedIndex? published;
        lock (_swapLock) published = _published;

        return published == null
            ? null
            : new NetHackWikiOverview(published.Info.FileCount, published.Namespaces, published.Info.IndexedAtUtc);
    }

    /// <summary>One page of the published index's article titles, sorted.</summary>
    public WikiArticlePage ListTitles(int offset, int limit)
    {
        PublishedIndex? published;
        lock (_swapLock) published = _published;

        return published == null ? WikiArticlePage.Empty : WikiArticlePage.Of(published.Titles, offset, limit);
    }

    /// <summary>
    /// False when the index's analyzer leaves no term of <paramref name="query"/>: it holds only
    /// punctuation or stop words. True while no index is published, so the search reports its own miss.
    /// </summary>
    public bool IsSearchableQuery(string query)
    {
        Analyzer? analyzer;
        lock (_swapLock) analyzer = _analyzer;

        return analyzer == null || WikiService.AnalyzeDistinctTerms(analyzer, query).Count > 0;
    }

    private Task<string?> RunPassAsync(CorpusPassContext context, CancellationToken cancellationToken) =>
        Task.FromResult(IndexWikiFiles(context, cancellationToken));

    /// <summary>One pass over the tree; returns the job message, or null after <see cref="CorpusPassContext.Fail"/>.</summary>
    private string? IndexWikiFiles(CorpusPassContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_wikiPath))
        {
            _logger?.LogWarning("NetHackWiki directory not configured (NetHackWikiPath is empty).");
            context.Fail($"{PathConfigKey} is not configured.");
            return null;
        }

        if (!System.IO.Directory.Exists(_wikiPath))
        {
            _logger?.LogWarning("NetHackWiki directory not found: {Path}", _wikiPath);
            context.Fail($"The {PathConfigKey} folder does not exist.");
            return null;
        }

        var stats = new PassStats();
        string? headSha = GitHelper.GetGitHeadSha(_wikiPath);
        string? branch = NullIfEmpty(GitHelper.GetCurrentBranch(_wikiPath));

        context.SetPhase(CorpusIndexPhase.ReadingFiles);
        var files = System.IO.Directory.GetFiles(_wikiPath, "*.md", SearchOption.AllDirectories).ToList();
        context.SetFilesTotal(files.Count);

        // Porter stemming: title and body tokens match across inflections (material/materials).
        var analyzer = new EnglishAnalyzer(LuceneVersion.LUCENE_48);

        // Build the new index into a fresh directory
        var newDirectory = new RAMDirectory();
        var config = new IndexWriterConfig(LuceneVersion.LUCENE_48, analyzer)
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
                cancellationToken.ThrowIfCancellationRequested();
                if (IndexWikiFile(writer, file, fingerprint, stats)) indexedCount++;
                context.IncrementFilesRead();
            }
            context.SetPhase(CorpusIndexPhase.BuildingIndex);
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
                        if (cancellationToken.IsCancellationRequested) break;
                        if (IndexWikiFile(chunkWriter, files[i], fingerprint, stats)) chunkCounts[chunk]++;
                        context.IncrementFilesRead();
                    }
                    chunkWriter.Commit();
                });
                cancellationToken.ThrowIfCancellationRequested();
                context.SetPhase(CorpusIndexPhase.BuildingIndex);

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

        cancellationToken.ThrowIfCancellationRequested();
        var newReader = DirectoryReader.Open(newDirectory);
        var newSearcher = new IndexSearcher(newReader)
        {
            Similarity = new BM25Similarity()
        };
        
        // Hot-swap: atomically replace the old index, then dispose of the old one
        context.SetPhase(CorpusIndexPhase.Publishing);
        RAMDirectory? oldDirectory;
        DirectoryReader? oldReader;
        lock (_swapLock)
        {
            if (_disposed)
            {
                newReader.Dispose();
                newDirectory.Dispose();
                throw new OperationCanceledException("The NetHack wiki service was disposed during the pass.");
            }
            oldDirectory = _directory;
            oldReader = _reader;
            _directory = newDirectory;
            _reader = newReader;
            _searcher = newSearcher;
            _analyzer = analyzer;
            var contentFingerprint = fingerprint.Build();
            ContentFingerprint = contentFingerprint;
            _published = stats.ToPublished(++_generation, contentFingerprint, headSha, branch, _maxFileSizeKB);
        }
        
        // Dispose old resources OUTSIDE the lock to avoid blocking queries
        oldReader?.Dispose();
        oldDirectory?.Dispose();

        _logger?.LogInformation("Indexed {Count} NetHack wiki articles.", indexedCount);
        return stats.Describe(indexedCount);
    }

    /// <summary>
    /// Indexes one file; false when it was left out. An oversize file is recorded in
    /// <paramref name="stats"/> as skipped by size, a file that throws as unreadable.
    /// </summary>
    private bool IndexWikiFile(IndexWriter writer, string file, CorpusContentFingerprintBuilder fingerprint, PassStats stats)
    {
        try
        {
            var fileInfo = new FileInfo(file);
            if (fileInfo.Length > _maxFileSizeKB * 1024)
            {
                stats.AddSkippedBySize(Path.GetRelativePath(_wikiPath, file).Replace('\\', '/'), fileInfo.Length);
                return false;
            }

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
            stats.AddIndexed(ns, title, fileInfo.Length);
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error indexing NetHack wiki file: {File}", file);
            stats.AddFailed(Path.GetRelativePath(_wikiPath, file).Replace('\\', '/'), ex.GetType().Name + ": " + ex.Message);
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

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

    public void Dispose()
    {
        _runner.Dispose();
        lock (_swapLock)
        {
            if (_disposed) return;
            _disposed = true;
            _searcher = null;
            _reader?.Dispose();
            _reader = null;
            _directory?.Dispose();
            _directory = null;
        }
    }

    /// <summary>A published index's statistics, swapped in with the searcher.</summary>
    private sealed record PublishedIndex(CorpusIndexInfoDto Info, IReadOnlyList<NetHackWikiNamespaceDto> Namespaces, IReadOnlyList<string> Titles);

    /// <summary>What one pass read and left out; every member is safe to call from the parallel chunks.</summary>
    private sealed class PassStats
    {
        private readonly object _lock = new();
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private readonly Dictionary<string, int> _namespaces = new(StringComparer.Ordinal);
        private readonly List<SkippedCorpusFileDto> _skippedBySize = new();
        private readonly List<FailedCorpusFileDto> _failedFiles = new();
        private readonly List<string> _titles = new();
        private long _totalBytes;

        public void AddIndexed(string ns, string title, long bytes)
        {
            lock (_lock)
            {
                _namespaces[ns] = _namespaces.TryGetValue(ns, out int count) ? count + 1 : 1;
                _titles.Add(title);
                _totalBytes += bytes;
            }
        }

        public void AddSkippedBySize(string relativePath, long bytes)
        {
            lock (_lock) _skippedBySize.Add(new SkippedCorpusFileDto(relativePath, bytes));
        }

        public void AddFailed(string relativePath, string error)
        {
            lock (_lock) _failedFiles.Add(new FailedCorpusFileDto(relativePath, error));
        }

        public PublishedIndex ToPublished(long generation, CorpusContentFingerprint fingerprint, string? headSha, string? branch, int maxFileSizeKB)
        {
            lock (_lock)
            {
                var info = new CorpusIndexInfoDto
                {
                    Generation = generation,
                    HeadSha = headSha,
                    Branch = branch,
                    IndexedAtUtc = fingerprint.IndexedAtUtc,
                    DurationMs = _stopwatch.ElapsedMilliseconds,
                    FileCount = fingerprint.FileCount,
                    TotalBytes = _totalBytes,
                    FingerprintShort = fingerprint.Sha256.Length > CorpusIndexLimits.FingerprintShortLength
                        ? fingerprint.Sha256.Substring(0, CorpusIndexLimits.FingerprintShortLength)
                        : fingerprint.Sha256,
                    MaxFileSizeKB = maxFileSizeKB,
                    SkippedBySize = _skippedBySize
                        .OrderBy(f => f.Path, StringComparer.Ordinal)
                        .Take(CorpusIndexLimits.MaxListedFiles)
                        .ToList(),
                    SkippedBySizeCount = _skippedBySize.Count,
                    SkippedDotDirectories = 0,
                    FailedFiles = _failedFiles
                        .OrderBy(f => f.Path, StringComparer.Ordinal)
                        .Take(CorpusIndexLimits.MaxListedFiles)
                        .ToList(),
                    FailedFileCount = _failedFiles.Count
                };
                var namespaces = _namespaces
                    .OrderByDescending(n => n.Value)
                    .ThenBy(n => n.Key, StringComparer.Ordinal)
                    .Select(n => new NetHackWikiNamespaceDto(n.Key, n.Value))
                    .ToList();
                return new PublishedIndex(info, namespaces, WikiArticlePage.Sorted(_titles));
            }
        }

        /// <summary>The job message, such as <c>2,512 articles, 3 over the size limit, 1 unreadable</c>.</summary>
        public string Describe(int indexedCount)
        {
            int skipped, failed;
            lock (_lock)
            {
                skipped = _skippedBySize.Count;
                failed = _failedFiles.Count;
            }

            string message = indexedCount.ToString("N0", CultureInfo.InvariantCulture) + (indexedCount == 1 ? " article" : " articles");
            if (skipped > 0) message += ", " + skipped.ToString("N0", CultureInfo.InvariantCulture) + " over the size limit";
            if (failed > 0) message += ", " + failed.ToString("N0", CultureInfo.InvariantCulture) + " unreadable";
            return message;
        }
    }
}

/// <summary>The number of indexed articles in one NetHack wiki namespace (the front matter's <c>namespace</c>, lowercased; <c>article</c> when absent).</summary>
public sealed record NetHackWikiNamespaceDto(string Namespace, int Articles);

/// <summary>What the published NetHack wiki index holds, as <c>wiki_overview</c> reports it.</summary>
public sealed record NetHackWikiOverview(int FileCount, IReadOnlyList<NetHackWikiNamespaceDto> Namespaces, DateTime IndexedAtUtc);

/// <summary>The NetHack wiki's corpus status: the common part, its namespaces and the converter's last report.</summary>
public sealed class NetHackWikiStatusDto : CorpusIndexStatusDto
{
    /// <summary>Articles per namespace in the published index, most articles first; empty until a pass publishes.</summary>
    public IReadOnlyList<NetHackWikiNamespaceDto> Namespaces { get; init; } = Array.Empty<NetHackWikiNamespaceDto>();

    /// <summary>The parsed <c>_conversion_report.json</c>; null when there is none or it cannot be read.</summary>
    public NetHackWikiConversionReportDto? ConversionReport { get; init; }

    /// <summary>Why the conversion report could not be read; null when it was read or does not exist.</summary>
    public string? ConversionReportError { get; init; }

    /// <summary>The conversion report was written after the published index was built.</summary>
    public bool TreeChangedSinceIndex { get; init; }
}
