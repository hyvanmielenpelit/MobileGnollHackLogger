using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
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

public class KnowledgeBaseService
{
    private readonly Dictionary<string, KnowledgeArticle> _articles = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<KnowledgeBaseService> _logger;
    private readonly IConfiguration _configuration;

    public KnowledgeBaseService(ILogger<KnowledgeBaseService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
        LoadArticles();
    }

    private void LoadArticles()
    {
        try
        {
            var kbPath = _configuration["KbPath"];
            if (string.IsNullOrWhiteSpace(kbPath) || !Directory.Exists(kbPath))
            {
                _logger.LogWarning($"KnowledgeBase directory not found or not configured. Path: {kbPath}");
                return;
            }

            var files = Directory.GetFiles(kbPath, "*.md");
            foreach (var file in files)
            {
                try
                {
                    var article = ParseArticle(file);
                    _articles[article.Topic] = article;
                    _logger.LogInformation($"Loaded knowledge base article: {article.Topic}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error parsing knowledge base article: {file}");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load knowledge base articles.");
        }
    }

    private KnowledgeArticle ParseArticle(string filePath)
    {
        var content = File.ReadAllText(filePath);
        var topic = Path.GetFileNameWithoutExtension(filePath);

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
        if (_articles.Count == 0) return string.Empty;

        var sb = new StringBuilder();
        foreach (var article in _articles.Values.OrderBy(a => a.Topic))
        {
            sb.AppendLine($"- **{article.Topic}**: {article.Summary}");
        }
        return sb.ToString();
    }

    public string? GetArticle(string topic)
    {
        if (_articles.TryGetValue(topic, out var article))
        {
            return article.Content;
        }
        return null;
    }

    public IEnumerable<string> GetAvailableTopics()
    {
        return _articles.Keys.OrderBy(k => k);
    }
}
