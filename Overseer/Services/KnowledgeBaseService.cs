using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace Overseer.Services;

public class KnowledgeArticle
{
    public string Topic { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}

public class KnowledgeBaseService : IDisposable
{
    private Dictionary<string, KnowledgeArticle> _articles = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<KnowledgeBaseService> _logger;
    private readonly IConfiguration _configuration;
    private Timer? _reloadTimer;
    private string? _lastGitSha;

    public Task InitializationTask { get; private set; }
    public bool IsIndexingComplete => InitializationTask?.IsCompleted ?? false;
    
    public KnowledgeBaseService(ILogger<KnowledgeBaseService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
        InitializationTask = Task.Run(() => LoadArticles());
        
        // Check for Git repository updates every 10 minutes
        _reloadTimer = new Timer(CheckForUpdates, null, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10));
    }

    private void CheckForUpdates(object? state)
    {
        try
        {
            var kbPath = _configuration["KbPath"];
            if (string.IsNullOrWhiteSpace(kbPath) || !Directory.Exists(kbPath)) return;

            string? currentSha = GitHelper.GetGitHeadSha(kbPath);
            if (!string.IsNullOrEmpty(currentSha) && currentSha != _lastGitSha)
            {
                _logger.LogInformation("Knowledge Base repository update detected ({OldSha} -> {NewSha}). Reloading.", _lastGitSha, currentSha);
                Task.Run(() => LoadArticles());
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking for Knowledge Base repository updates.");
        }
    }

    private void LoadArticles()
    {
        try
        {
            var kbPath = _configuration["KbPath"];
            if (string.IsNullOrWhiteSpace(kbPath))
            {
                _logger.LogWarning("KnowledgeBase directory not configured (KbPath is empty).");
                return;
            }

            _lastGitSha = GitHelper.GetGitHeadSha(kbPath);

            var contentPath = Path.Combine(kbPath, "Content");
            if (!Directory.Exists(contentPath))
            {
                _logger.LogWarning($"KnowledgeBase Content directory not found. Path: {contentPath}");
                return;
            }

            var newArticles = new Dictionary<string, KnowledgeArticle>(StringComparer.OrdinalIgnoreCase);
            var files = Directory.GetFiles(contentPath, "*.md", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                try
                {
                    var article = ParseArticle(file, contentPath);
                    newArticles[article.Topic] = article;
                    _logger.LogInformation($"Loaded knowledge base article: {article.Topic}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error parsing knowledge base article: {file}");
                }
            }

            Interlocked.Exchange(ref _articles, newArticles);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load knowledge base articles.");
        }
    }

    private KnowledgeArticle ParseArticle(string filePath, string contentPath)
    {
        var content = File.ReadAllText(filePath);
        
        var relPath = Path.GetRelativePath(contentPath, filePath);
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
                    }
                    else if (key == "summary")
                    {
                        article.Summary = value;
                    }
                }
            }
        }

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

    public IEnumerable<string> GetAvailableTopics()
    {
        var articles = _articles;
        return articles.Keys.OrderBy(k => k);
    }
    
    public void Dispose()
    {
        _reloadTimer?.Dispose();
        _reloadTimer = null;
    }
}
