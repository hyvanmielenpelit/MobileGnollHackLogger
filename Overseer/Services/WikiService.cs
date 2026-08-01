using Microsoft.Extensions.Configuration;

namespace Overseer.Services;

public class WikiService
{
    private readonly string _wikiPath;
    private readonly int _maxFiles;
    private readonly int _maxFileSizeKB;
    private readonly List<WikiDocument> _documents = new();

    public WikiService(IConfiguration configuration)
    {
        _wikiPath = configuration["WikiPath"] ?? "c:\\wiki";
        _maxFiles = int.TryParse(configuration["MaxWikiFilesToInclude"], out var maxFiles) ? maxFiles : 5;
        _maxFileSizeKB = int.TryParse(configuration["MaxWikiFileSizeKB"], out var maxFileSize) ? maxFileSize : 100;

        IndexWikiFiles();
    }

    private void IndexWikiFiles()
    {
        if (!Directory.Exists(_wikiPath)) return;

        var files = Directory.GetFiles(_wikiPath, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                     || f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
                     || f.EndsWith(".html", StringComparison.OrdinalIgnoreCase));

        foreach (var file in files)
        {
            var fileInfo = new FileInfo(file);
            if (fileInfo.Length <= _maxFileSizeKB * 1024)
            {
                _documents.Add(new WikiDocument
                {
                    FilePath = file,
                    FileName = Path.GetFileName(file),
                    Content = File.ReadAllText(file)
                });
            }
        }
    }

    private static readonly HashSet<string> _stopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "a", "an", "is", "it", "to", "in", "of", "and", "or", "for",
        "on", "at", "by", "be", "as", "do", "if", "so", "no", "up", "my",
        "we", "he", "me", "am", "us", "its", "has", "had", "was", "are",
        "not", "but", "all", "can", "her", "him", "his", "how", "our",
        "out", "own", "say", "she", "too", "use", "way", "who", "did",
        "get", "got", "let", "may", "new", "now", "old", "see", "two",
        "any", "few", "than", "then", "them", "they", "this", "that",
        "what", "when", "will", "with", "very", "your", "from", "have",
        "been", "here", "just", "like", "make", "many", "more", "much",
        "some", "such", "take", "also", "back", "come", "each", "even",
        "give", "good", "most", "only", "over", "said", "same", "tell",
        "time", "want", "well", "went", "were", "work", "year", "about",
        "after", "being", "could", "every", "first", "found", "great",
        "still", "their", "there", "these", "thing", "think", "those",
        "under", "where", "which", "while", "world", "would", "other",
        "should", "through", "does", "into"
    };

    public IEnumerable<string> GetRelevantContext(string query, string? categoryFilter = null)
    {
        if (string.IsNullOrWhiteSpace(query)) return Enumerable.Empty<string>();

        var words = query.Split(new[] { ' ', '.', ',', '?', '!', ':', ';', '(', ')', '[', ']', '"', '\'' },
                                StringSplitOptions.RemoveEmptyEntries)
                         .Where(w => w.Length > 1 && !_stopWords.Contains(w))
                         .Select(w => w.ToLowerInvariant())
                         .Distinct()
                         .ToList();

        if (!words.Any()) return Enumerable.Empty<string>();

        var scoredDocs = _documents.Select(doc => new
        {
            Document = doc,
            Score = words.Count(w => doc.Content.Contains(w, StringComparison.OrdinalIgnoreCase))
        })
        .Where(x => x.Score > 0 && (string.IsNullOrEmpty(categoryFilter) || x.Document.FilePath.Contains(categoryFilter, StringComparison.OrdinalIgnoreCase)))
        .OrderByDescending(x => x.Score)
        .Take(_maxFiles)
        .Select(x => $"--- {x.Document.FileName} ---\n{x.Document.Content}")
        .ToList();

        return scoredDocs;
    }

    private class WikiDocument
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
    }
}
