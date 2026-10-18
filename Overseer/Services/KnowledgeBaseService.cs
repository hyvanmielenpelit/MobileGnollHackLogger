using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Overseer.Services.CorpusIndexing;

namespace Overseer.Services;

public class KnowledgeArticle
{
    public string Topic { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}

/// <summary>One loaded knowledge base article, as the status API reports it.</summary>
/// <param name="HasTitle">The front matter carried a non-blank <c>title</c>.</param>
/// <param name="HasSummary">The front matter carried a non-blank <c>summary</c>.</param>
public sealed record KnowledgeArticleInfoDto(string Topic, string? Title, bool HasTitle, bool HasSummary, long Bytes, DateTime LastWriteUtc);

/// <summary>
/// An article that a later file with the same topic, differing only by case, replaced. Paths are
/// relative to <c>KbPath</c>, with <c>/</c> separators.
/// </summary>
public sealed record KnowledgeShadowedTopicDto(string Topic, string Path, string ShadowedByPath);

/// <summary>The knowledge base's status: the common corpus status plus the loaded articles.</summary>
public sealed class KnowledgeBaseStatusDto : CorpusIndexStatusDto
{
    /// <summary>Every loaded article, sorted by topic.</summary>
    public IReadOnlyList<KnowledgeArticleInfoDto> Articles { get; init; } = Array.Empty<KnowledgeArticleInfoDto>();

    /// <summary>Articles the last published pass loaded and then replaced with a case-variant topic.</summary>
    public IReadOnlyList<KnowledgeShadowedTopicDto> ShadowedTopics { get; init; } = Array.Empty<KnowledgeShadowedTopicDto>();

    /// <summary>The length of <see cref="KnowledgeBaseService.GetTopicList"/>, which the system prompt carries.</summary>
    public int TopicListChars { get; init; }

    /// <summary>Whether <c>&lt;KbPath&gt;\Content</c>, the folder the articles are read from, exists.</summary>
    public bool ContentPathExists { get; init; }
}

public class KnowledgeBaseService : IDisposable
{
    private const string ConfigKey = "KbPath";
    private const int CheckIntervalMinutes = 10;
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(CheckIntervalMinutes);

    private Dictionary<string, KnowledgeArticle> _articles = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<KnowledgeBaseService> _logger;
    private readonly IConfiguration _configuration;
    private readonly Func<string, IEnumerable<string>> _listArticleFiles;
    private readonly CorpusIndexJobRunner _runner;
    private Timer? _reloadTimer;
    private long _nextCheckAtTicks;

    /* _articles, _published and ContentFingerprint are replaced together under _swapLock; readers
       read each reference once, without the lock. */
    private readonly object _swapLock = new();
    private volatile PublishedIndex? _published;
    private long _generation;

    public Task InitializationTask { get; private set; }
    public bool IsIndexingComplete => InitializationTask?.IsCompleted ?? false;

    /// <summary>What the loaded article set holds; null until the first load finishes.</summary>
    public CorpusContentFingerprint? ContentFingerprint { get; private set; }

    /// <summary>The id of the most recently started load, running or finished; null before the first.</summary>
    public string? LastStartedJobId => _runner.LastStartedJobId;

    public KnowledgeBaseService(ILogger<KnowledgeBaseService> logger, IConfiguration configuration)
        : this(logger, configuration, null)
    {
    }

    /// <param name="listArticleFiles">
    /// Lists the article files under the <c>Content</c> folder, in the order they are loaded; null
    /// enumerates <c>*.md</c> in every subfolder.
    /// </param>
    internal KnowledgeBaseService(ILogger<KnowledgeBaseService> logger, IConfiguration configuration,
        Func<string, IEnumerable<string>>? listArticleFiles)
    {
        _logger = logger;
        _configuration = configuration;
        _listArticleFiles = listArticleFiles ?? (contentPath => Directory.GetFiles(contentPath, "*.md", SearchOption.AllDirectories));
        _runner = new CorpusIndexJobRunner("Knowledge base", logger);
        InitializationTask = _runner.TryStart(CorpusIndexTrigger.Startup, RunPassAsync, out _) ?? Task.CompletedTask;

        // Check for Git repository updates every 10 minutes
        Interlocked.Exchange(ref _nextCheckAtTicks, DateTime.UtcNow.Add(CheckInterval).Ticks);
        _reloadTimer = new Timer(CheckForUpdates, null, CheckInterval, CheckInterval);
    }

    /// <summary>
    /// Starts a load of the articles. <see cref="CorpusIndexStartResult.NotConfigured"/> when
    /// <c>KbPath</c> is not set and <see cref="CorpusIndexStartResult.PathMissing"/> when its folder
    /// does not exist; otherwise the runner's answer.
    /// </summary>
    public CorpusIndexStartResult RequestReindex(CorpusIndexTrigger trigger = CorpusIndexTrigger.Manual)
    {
        var kbPath = _configuration[ConfigKey];
        if (string.IsNullOrWhiteSpace(kbPath)) return CorpusIndexStartResult.NotConfigured;
        if (!Directory.Exists(kbPath)) return CorpusIndexStartResult.PathMissing;

        _runner.TryStart(trigger, RunPassAsync, out var result);
        return result;
    }

    private void CheckForUpdates(object? state)
    {
        Interlocked.Exchange(ref _nextCheckAtTicks, DateTime.UtcNow.Add(CheckInterval).Ticks);
        try
        {
            var kbPath = _configuration[ConfigKey];
            if (string.IsNullOrWhiteSpace(kbPath) || !Directory.Exists(kbPath)) return;

            string? currentSha = GitHelper.GetGitHeadSha(kbPath);
            string? publishedSha = _published?.Info.HeadSha;
            if (!string.IsNullOrEmpty(currentSha) && currentSha != publishedSha)
            {
                _logger.LogInformation("Knowledge Base repository update detected ({OldSha} -> {NewSha}). Reloading.", publishedSha, currentSha);
                RequestReindex(CorpusIndexTrigger.RepositoryChange);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking for Knowledge Base repository updates.");
        }
    }

    private Task<string?> RunPassAsync(CorpusPassContext context, CancellationToken cancellationToken) =>
        Task.FromResult(LoadArticles(context, cancellationToken));

    /// <summary>
    /// Loads every article and publishes the set with its statistics. A file that cannot be read is
    /// listed and skipped. A missing <c>KbPath</c> or <c>Content</c> folder fails the pass and keeps
    /// the previous set. Returns the job message.
    /// </summary>
    private string? LoadArticles(CorpusPassContext context, CancellationToken cancellationToken)
    {
        var kbPath = _configuration[ConfigKey];
        if (string.IsNullOrWhiteSpace(kbPath))
        {
            _logger.LogWarning("KnowledgeBase directory not configured (KbPath is empty).");
            context.Fail($"{ConfigKey} is not configured.");
            return null;
        }

        if (!Directory.Exists(kbPath))
        {
            _logger.LogWarning("KnowledgeBase directory not found ({Key}).", ConfigKey);
            context.Fail($"The {ConfigKey} folder does not exist.");
            return null;
        }

        var stopwatch = Stopwatch.StartNew();
        string? headSha = GitHelper.GetGitHeadSha(kbPath);
        string branch = GitHelper.GetCurrentBranch(kbPath);

        var contentPath = Path.Combine(kbPath, "Content");
        if (!Directory.Exists(contentPath))
        {
            _logger.LogWarning($"KnowledgeBase Content directory not found. Path: {contentPath}");
            context.Fail($"The Content folder under {ConfigKey} does not exist.");
            return null;
        }

        context.SetPhase(CorpusIndexPhase.ReadingFiles);
        var newArticles = new Dictionary<string, KnowledgeArticle>(StringComparer.OrdinalIgnoreCase);
        var sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var infos = new Dictionary<string, KnowledgeArticleInfoDto>(StringComparer.OrdinalIgnoreCase);
        var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var shadowed = new List<KnowledgeShadowedTopicDto>();
        var failed = new List<FailedCorpusFileDto>();
        int failedCount = 0;
        int filesLoaded = 0;
        long totalBytes = 0;

        var fingerprint = new CorpusContentFingerprintBuilder();
        var files = _listArticleFiles(contentPath).ToList();
        context.ReportFiles(0, files.Count);
        int processed = 0;
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string relativePath = Path.GetRelativePath(kbPath, file).Replace('\\', '/');
            try
            {
                var fileInfo = new FileInfo(file);
                long bytes = fileInfo.Length;
                DateTime lastWriteUtc = fileInfo.LastWriteTimeUtc;

                var article = ParseArticle(file, contentPath, fingerprint, out string source, out bool hasTitle, out bool hasSummary);
                if (paths.TryGetValue(article.Topic, out var earlierPath) && newArticles.TryGetValue(article.Topic, out var earlier))
                {
                    shadowed.Add(new KnowledgeShadowedTopicDto(earlier.Topic, earlierPath, relativePath));
                }

                newArticles[article.Topic] = article;
                sources[article.Topic] = source;
                paths[article.Topic] = relativePath;
                infos[article.Topic] = new KnowledgeArticleInfoDto(article.Topic, article.Title, hasTitle, hasSummary, bytes, lastWriteUtc);
                filesLoaded++;
                totalBytes += bytes;
                _logger.LogInformation($"Loaded knowledge base article: {article.Topic}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error parsing knowledge base article: {file}");
                failedCount++;
                if (failed.Count < CorpusIndexLimits.MaxListedFiles)
                {
                    failed.Add(new FailedCorpusFileDto(relativePath, ex.GetType().Name + ": " + ex.Message));
                }
            }

            context.ReportFiles(++processed, files.Count);
        }

        context.SetPhase(CorpusIndexPhase.Publishing);
        var builtFingerprint = fingerprint.Build();
        var articleInfos = infos.Values.OrderBy(a => a.Topic, StringComparer.OrdinalIgnoreCase).ToList();
        stopwatch.Stop();

        lock (_swapLock)
        {
            var info = new CorpusIndexInfoDto
            {
                Generation = ++_generation,
                HeadSha = headSha,
                Branch = string.IsNullOrEmpty(branch) ? null : branch,
                IndexedAtUtc = builtFingerprint.IndexedAtUtc,
                DurationMs = stopwatch.ElapsedMilliseconds,
                FileCount = filesLoaded,
                TotalBytes = totalBytes,
                FingerprintShort = Shorten(builtFingerprint.Sha256),
                MaxFileSizeKB = null,
                FailedFiles = failed,
                FailedFileCount = failedCount
            };

            Interlocked.Exchange(ref _articles, newArticles);
            _published = new PublishedIndex(info, articleInfos, shadowed, sources);
            ContentFingerprint = builtFingerprint;
        }

        string message = string.Format(CultureInfo.InvariantCulture, "{0:N0} {1}", newArticles.Count, newArticles.Count == 1 ? "article" : "articles");
        if (failedCount > 0) message += string.Format(CultureInfo.InvariantCulture, ", {0:N0} unreadable", failedCount);
        if (shadowed.Count > 0) message += string.Format(CultureInfo.InvariantCulture, ", {0:N0} shadowed", shadowed.Count);
        return message;
    }

    private static string? Shorten(string? sha256) =>
        string.IsNullOrEmpty(sha256) ? null
            : sha256.Length <= CorpusIndexLimits.FingerprintShortLength ? sha256
            : sha256.Substring(0, CorpusIndexLimits.FingerprintShortLength);

    /// <param name="source">The file's text as read, front matter included.</param>
    /// <param name="hasTitle">The front matter set a non-blank title.</param>
    /// <param name="hasSummary">The front matter set a non-blank summary.</param>
    private KnowledgeArticle ParseArticle(string filePath, string contentPath, CorpusContentFingerprintBuilder fingerprint,
        out string source, out bool hasTitle, out bool hasSummary)
    {
        var content = File.ReadAllText(filePath);
        source = content;
        bool titleSet = false;
        bool summarySet = false;

        var relPath = Path.GetRelativePath(contentPath, filePath);
        fingerprint.Add(relPath, content);
        var topic = relPath.Replace('\\', '/');
        if (topic.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            topic = topic.Substring(0, topic.Length - 3);
        }

        var article = new KnowledgeArticle
        {
            Topic = topic,
            Title = topic, // fallback
            Summary = "",
            Content = content
        };

        // Extract YAML frontmatter
        var match = Regex.Match(content, @"^---\s*\n(.*?)\n---\s*\n(.*)$", RegexOptions.Singleline);
        if (match.Success)
        {
            var frontmatter = match.Groups[1].Value;
            article.Content = match.Groups[2].Value.TrimStart();

            var lines = frontmatter.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var colonIndex = line.IndexOf(':');
                if (colonIndex > 0)
                {
                    var key = line.Substring(0, colonIndex).Trim().ToLowerInvariant();
                    var value = line.Substring(colonIndex + 1).Trim();

                    if (key == "title")
                    {
                        article.Title = value;
                        titleSet = true;
                    }
                    else if (key == "summary")
                    {
                        article.Summary = value;
                        summarySet = true;
                    }
                }
            }
        }

        hasTitle = titleSet && !string.IsNullOrWhiteSpace(article.Title);
        hasSummary = summarySet && !string.IsNullOrWhiteSpace(article.Summary);
        return article;
    }

    public string GetTopicList()
    {
        var articles = _articles;
        if (articles.Count == 0) return string.Empty;

        var sb = new StringBuilder();
        foreach (var article in articles.Values.OrderBy(a => a.Topic))
        {
            sb.AppendLine($"- **{article.Topic}**: {article.Summary}");
        }
        return sb.ToString();
    }

    public string? GetArticleTitle(string topic)
    {
        var articles = _articles;
        if (articles.TryGetValue(topic, out var article))
        {
            return article.Title;
        }
        return null;
    }

    public string? GetArticle(string topic)
    {
        var articles = _articles;
        if (articles.TryGetValue(topic, out var article))
        {
            return article.Content;
        }
        return null;
    }

    /// <summary>The article's file text as loaded, front matter included; null for an unknown topic. The topic is matched ignoring case.</summary>
    public string? GetArticleSource(string topic)
    {
        var published = _published;
        if (published != null && published.Sources.TryGetValue(topic, out var source))
        {
            return source;
        }
        return null;
    }

    public IEnumerable<string> GetAvailableTopics()
    {
        var articles = _articles;
        return articles.Keys.OrderBy(k => k);
    }

    /// <summary>The knowledge base's configuration, live Git state, published set and load jobs.</summary>
    public KnowledgeBaseStatusDto GetStatus()
    {
        var published = _published;
        var kbPath = _configuration[ConfigKey];
        bool configured = !string.IsNullOrWhiteSpace(kbPath);
        bool pathExists = configured && Directory.Exists(kbPath);
        string? currentSha = pathExists ? GitHelper.GetGitHeadSha(kbPath!) : null;
        string currentBranch = pathExists ? GitHelper.GetCurrentBranch(kbPath!) : string.Empty;

        return new KnowledgeBaseStatusDto
        {
            Kind = CorpusKind.KnowledgeBase,
            ConfigKey = ConfigKey,
            Configured = configured,
            Path = configured ? kbPath : null,
            PathExists = pathExists,
            IsGitRepository = pathExists && Directory.Exists(Path.Combine(kbPath!, ".git")),
            IsIndexingComplete = IsIndexingComplete,
            CurrentHeadSha = currentSha,
            CurrentBranch = string.IsNullOrEmpty(currentBranch) ? null : currentBranch,
            Index = published?.Info,
            UpToDate = published != null && string.Equals(currentSha, published.Info.HeadSha, StringComparison.Ordinal),
            RefreshMode = CorpusRefreshMode.GitPoll,
            NextCheckAtUtc = new DateTime(Interlocked.Read(ref _nextCheckAtTicks), DateTimeKind.Utc),
            CheckIntervalMinutes = CheckIntervalMinutes,
            CurrentJob = _runner.CurrentJob,
            RecentJobs = _runner.RecentJobs,
            Articles = published?.Articles ?? Array.Empty<KnowledgeArticleInfoDto>(),
            ShadowedTopics = published?.ShadowedTopics ?? Array.Empty<KnowledgeShadowedTopicDto>(),
            TopicListChars = GetTopicList().Length,
            ContentPathExists = pathExists && Directory.Exists(Path.Combine(kbPath!, "Content")),
            ServerTimeUtc = DateTime.UtcNow
        };
    }

    public void Dispose()
    {
        _reloadTimer?.Dispose();
        _reloadTimer = null;
        _runner.Dispose();
    }

    /// <summary>The published article set's statistics and file texts, keyed like <c>_articles</c>.</summary>
    private sealed record PublishedIndex(
        CorpusIndexInfoDto Info,
        IReadOnlyList<KnowledgeArticleInfoDto> Articles,
        IReadOnlyList<KnowledgeShadowedTopicDto> ShadowedTopics,
        Dictionary<string, string> Sources);
}
