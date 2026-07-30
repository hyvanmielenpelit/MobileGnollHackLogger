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
        _wikiPath = configuration["Overseer:WikiPath"] ?? "c:\\wiki";
        _maxFiles = int.TryParse(configuration["Overseer:MaxWikiFilesToInclude"], out var maxFiles) ? maxFiles : 5;
        _maxFileSizeKB = int.TryParse(configuration["Overseer:MaxWikiFileSizeKB"], out var maxFileSize) ? maxFileSize : 100;

        IndexWikiFiles();
    }

    private void IndexWikiFiles()
    {
        if (!Directory.Exists(_wikiPath)) return;

        var files = Directory.GetFiles(_wikiPath, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".md", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase));

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

    public IEnumerable<string> GetRelevantContext(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return Enumerable.Empty<string>();

        var words = query.Split(new[] { ' ', '.', ',', '?', '!' }, StringSplitOptions.RemoveEmptyEntries)
                         .Where(w => w.Length > 4)
                         .Select(w => w.ToLowerInvariant())
                         .ToList();

        if (!words.Any()) return Enumerable.Empty<string>();

        var scoredDocs = _documents.Select(doc => new
        {
            Document = doc,
            Score = words.Count(w => doc.Content.Contains(w, StringComparison.OrdinalIgnoreCase))
        })
        .Where(x => x.Score > 0)
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
