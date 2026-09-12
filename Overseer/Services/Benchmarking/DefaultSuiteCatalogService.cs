namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MobileGnollHackLogger.Data;

/// <summary>One question as a default-suite file carries it.</summary>
public sealed record DefaultSuiteQuestion(string QuestionText, BenchmarkDifficulty Difficulty, string? ExpectedPoints);

/// <summary>
/// One <c>*.json</c> file in the default-suite directory. <see cref="Error"/> is null when the file
/// is valid; an entry with an error is listed but cannot be imported. <see cref="Key"/> is null when
/// the file's key is missing or is not a valid slug.
/// </summary>
public sealed record DefaultSuiteCatalogEntry
{
    public string? Key { get; init; }
    public int? Version { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public int QuestionCount { get; init; }

    /// <summary>Question counts keyed "Simple", "Intermediate" and "Advanced".</summary>
    public IReadOnlyDictionary<string, int> DifficultyCounts { get; init; } = new Dictionary<string, int>();
    public string FileName { get; init; } = string.Empty;
    public string? Error { get; init; }
    public IReadOnlyList<DefaultSuiteQuestion> Questions { get; init; } = Array.Empty<DefaultSuiteQuestion>();
}

public sealed record DefaultSuiteImportSkip(string Key, string Reason);

public sealed record ImportDefaultSuitesOutcome(
    IReadOnlyList<BenchmarkSuite> Imported,
    IReadOnlyList<DefaultSuiteImportSkip> Skipped);

/// <summary>
/// Discovers the default question suites under <c>Benchmark:DefaultSuitesPath</c> (default
/// <c>&lt;AppBase&gt;/Data/DefaultSuites</c>), validates them, and imports selected ones as new suites.
///
/// <para>Registered as a singleton. Parsed files are cached per path and invalidated by the file's
/// last-write time and length; the directory itself is re-listed on every call, so added and
/// removed files show up without a restart. The DbContext and the compliance guard are scoped, so
/// <see cref="ImportAsync"/> takes them as arguments.</para>
///
/// <para>Import never overwrites an existing suite: a name already in use gets a numbered copy.</para>
/// </summary>
public class DefaultSuiteCatalogService
{
    public const string DefaultSuitesPathKey = "Benchmark:DefaultSuitesPath";
    public const int MaxKeyLength = 64;
    public const int MaxNameLength = 128;

    private static readonly Regex KeyPattern = new(@"^[a-z0-9-]{1,64}\z", RegexOptions.CultureInvariant);

    private readonly IConfiguration _configuration;
    private readonly ILogger<DefaultSuiteCatalogService>? _logger;
    private readonly ConcurrentDictionary<string, CachedFile> _cache = new(StringComparer.OrdinalIgnoreCase);

    private sealed record CachedFile(DateTime LastWriteTimeUtc, long Length, DefaultSuiteCatalogEntry Entry);

    public DefaultSuiteCatalogService(IConfiguration configuration, ILogger<DefaultSuiteCatalogService>? logger = null)
    {
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// The configured directory when set; otherwise <c>&lt;AppBase&gt;/Data/DefaultSuites</c>, falling
    /// back to <c>&lt;CurrentDirectory&gt;/Data/DefaultSuites</c> when the first does not exist.
    /// </summary>
    public string ResolveDirectory()
    {
        string? configured = _configuration[DefaultSuitesPathKey];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        string primary = Path.Combine(AppContext.BaseDirectory, "Data", "DefaultSuites");
        if (Directory.Exists(primary))
        {
            return primary;
        }

        return Path.Combine(Directory.GetCurrentDirectory(), "Data", "DefaultSuites");
    }

    /// <summary>
    /// Every <c>*.json</c> file in the directory, ordered by file name. Invalid files are listed
    /// with their error, never thrown; a missing directory yields an empty catalog.
    /// </summary>
    public IReadOnlyList<DefaultSuiteCatalogEntry> GetCatalog()
    {
        string directory = ResolveDirectory();
        if (!Directory.Exists(directory))
        {
            return Array.Empty<DefaultSuiteCatalogEntry>();
        }

        string[] files;
        try
        {
            files = Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger?.LogWarning(ex, "Default suite directory {Directory} could not be listed.", directory);
            return Array.Empty<DefaultSuiteCatalogEntry>();
        }

        Array.Sort(files, StringComparer.OrdinalIgnoreCase);

        var entries = new List<DefaultSuiteCatalogEntry>(files.Length);
        foreach (string path in files)
        {
            entries.Add(LoadEntry(path));
        }

        var present = new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);
        foreach (string stale in _cache.Keys.Where(k => !present.Contains(k)).ToList())
        {
            _cache.TryRemove(stale, out _);
        }

        return FlagDuplicateKeys(entries);
    }

    /// <summary>
    /// Imports each requested key as a new suite. Each key succeeds or is skipped on its own: an
    /// unknown key, an invalid file, or a suite over the per-suite question limit is reported as a
    /// skip with its reason, and writes no rows. A key repeated in the request is imported once.
    /// </summary>
    public async Task<ImportDefaultSuitesOutcome> ImportAsync(
        IEnumerable<string> keys,
        ApplicationDbContext db,
        BenchmarkComplianceGuard guard,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(guard);

        var catalog = GetCatalog();
        var imported = new List<BenchmarkSuite>();
        var skipped = new List<DefaultSuiteImportSkip>();
        var requested = new HashSet<string>(StringComparer.Ordinal);

        foreach (string? rawKey in keys)
        {
            string key = rawKey?.Trim() ?? string.Empty;
            if (!requested.Add(key))
            {
                continue;
            }

            var entry = catalog.FirstOrDefault(e => string.Equals(e.Key, key, StringComparison.Ordinal));
            if (entry == null)
            {
                skipped.Add(new DefaultSuiteImportSkip(key, "No default suite with this key was found on the server."));
                continue;
            }

            if (entry.Error != null)
            {
                skipped.Add(new DefaultSuiteImportSkip(key, $"The default suite file {entry.FileName} is invalid: {entry.Error}"));
                continue;
            }

            var (canAdd, denial) = guard.CanAddQuestions(0, entry.Questions.Count);
            if (!canAdd)
            {
                skipped.Add(new DefaultSuiteImportSkip(key,
                    $"{denial} The default suite has {entry.Questions.Count} questions, so nothing was imported."));
                continue;
            }

            string finalName = entry.Name;
            int counter = 1;
            while (await db.BenchmarkSuites.AnyAsync(s => s.Name == finalName, ct))
            {
                counter++;
                finalName = $"{entry.Name} ({counter})";
            }

            var now = DateTime.UtcNow;
            var suite = new BenchmarkSuite
            {
                Name = finalName,
                Description = entry.Description ?? string.Empty,
                DefaultSuiteKey = entry.Key,
                DefaultSuiteVersion = entry.Version,
                CreatedAtUtc = now
            };

            // OrderIndex comes from array position; the file's own orderIndex field is not read.
            // Questions arrive at ItemRevision 1, unreviewed and unassessed.
            int order = 1;
            foreach (var q in entry.Questions)
            {
                suite.Questions.Add(new BenchmarkQuestion
                {
                    OrderIndex = order++,
                    QuestionText = q.QuestionText,
                    Difficulty = q.Difficulty,
                    ExpectedPoints = q.ExpectedPoints,
                    CreatedAtUtc = now,
                    ModifiedAtUtc = now
                });
            }

            db.BenchmarkSuites.Add(suite);
            await db.SaveChangesAsync(ct);
            imported.Add(suite);
        }

        return new ImportDefaultSuitesOutcome(imported, skipped);
    }

    private DefaultSuiteCatalogEntry LoadEntry(string path)
    {
        string fileName = Path.GetFileName(path);
        DateTime lastWrite;
        long length;
        string json;
        try
        {
            var info = new FileInfo(path);
            lastWrite = info.LastWriteTimeUtc;
            length = info.Length;

            if (_cache.TryGetValue(path, out var cached) && cached.LastWriteTimeUtc == lastWrite && cached.Length == length)
            {
                return cached.Entry;
            }

            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not cached: a file that is being written is read again on the next call.
            return new DefaultSuiteCatalogEntry
            {
                Name = Path.GetFileNameWithoutExtension(path),
                FileName = fileName,
                DifficultyCounts = EmptyDifficultyCounts(),
                Error = $"The file could not be read: {ex.Message}"
            };
        }

        var entry = ParseFile(fileName, json);
        if (entry.Error != null)
        {
            _logger?.LogWarning("Default suite file {FileName} is invalid: {Error}", fileName, entry.Error);
        }

        _cache[path] = new CachedFile(lastWrite, length, entry);
        return entry;
    }

    /// <summary>Parses and validates one file's text. Never throws on bad content.</summary>
    internal static DefaultSuiteCatalogEntry ParseFile(string fileName, string json)
    {
        string fallbackName = Path.GetFileNameWithoutExtension(fileName);

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            return new DefaultSuiteCatalogEntry
            {
                Name = fallbackName,
                FileName = fileName,
                DifficultyCounts = EmptyDifficultyCounts(),
                Error = $"The file is not valid JSON: {ex.Message}"
            };
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return new DefaultSuiteCatalogEntry
                {
                    Name = fallbackName,
                    FileName = fileName,
                    DifficultyCounts = EmptyDifficultyCounts(),
                    Error = "The top level of the file must be a JSON object."
                };
            }

            var errors = new List<string>();

            string? key = null;
            if (!root.TryGetProperty("key", out var keyProp))
            {
                errors.Add("Missing \"key\".");
            }
            else if (keyProp.ValueKind != JsonValueKind.String)
            {
                errors.Add("\"key\" must be a string.");
            }
            else
            {
                string rawKey = keyProp.GetString() ?? string.Empty;
                if (KeyPattern.IsMatch(rawKey))
                {
                    key = rawKey;
                }
                else
                {
                    errors.Add($"\"key\" \"{rawKey}\" is not valid: use 1 to {MaxKeyLength} lowercase letters, digits and hyphens.");
                }
            }

            int? version = null;
            if (root.TryGetProperty("version", out var versionProp) && versionProp.ValueKind != JsonValueKind.Null)
            {
                if (versionProp.ValueKind == JsonValueKind.Number && versionProp.TryGetInt32(out int v) && v >= 1)
                {
                    version = v;
                }
                else
                {
                    errors.Add("\"version\" must be a positive integer.");
                }
            }

            string name = fallbackName;
            if (root.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(nameProp.GetString()))
            {
                name = nameProp.GetString()!.Trim();
                if (name.Length > MaxNameLength)
                {
                    errors.Add($"\"name\" is longer than {MaxNameLength} characters.");
                }
            }
            else
            {
                errors.Add("Missing \"name\".");
            }

            string? description = null;
            if (root.TryGetProperty("description", out var descProp) && descProp.ValueKind != JsonValueKind.Null)
            {
                if (descProp.ValueKind == JsonValueKind.String)
                {
                    description = descProp.GetString();
                }
                else
                {
                    errors.Add("\"description\" must be a string.");
                }
            }

            var counts = EmptyDifficultyCounts();
            var questions = new List<DefaultSuiteQuestion>();
            int questionCount = 0;
            if (!root.TryGetProperty("questions", out var qArray) || qArray.ValueKind != JsonValueKind.Array)
            {
                errors.Add("\"questions\" must be an array.");
            }
            else
            {
                questionCount = qArray.GetArrayLength();
                int position = 0;
                foreach (var qEl in qArray.EnumerateArray())
                {
                    position++;
                    if (qEl.ValueKind != JsonValueKind.Object
                        || !qEl.TryGetProperty("questionText", out var textProp)
                        || textProp.ValueKind != JsonValueKind.String
                        || string.IsNullOrWhiteSpace(textProp.GetString()))
                    {
                        errors.Add($"Question {position} has no \"questionText\".");
                        continue;
                    }

                    // An absent or unrecognised difficulty is Simple.
                    var difficulty = BenchmarkDifficulty.Simple;
                    if (qEl.TryGetProperty("difficulty", out var diffProp) && diffProp.ValueKind == JsonValueKind.String
                        && Enum.TryParse<BenchmarkDifficulty>(diffProp.GetString(), true, out var parsed)
                        && Enum.IsDefined(parsed))
                    {
                        difficulty = parsed;
                    }

                    string? expected = null;
                    if (qEl.TryGetProperty("expectedPoints", out var epProp) && epProp.ValueKind != JsonValueKind.Null)
                    {
                        if (epProp.ValueKind == JsonValueKind.String)
                        {
                            expected = epProp.GetString();
                        }
                        else
                        {
                            errors.Add($"Question {position}: \"expectedPoints\" must be a string.");
                        }
                    }

                    counts[difficulty.ToString()]++;
                    questions.Add(new DefaultSuiteQuestion(textProp.GetString()!, difficulty, expected));
                }
            }

            return new DefaultSuiteCatalogEntry
            {
                Key = key,
                Version = version,
                Name = name,
                Description = description,
                QuestionCount = questionCount,
                DifficultyCounts = counts,
                FileName = fileName,
                Error = errors.Count == 0 ? null : string.Join(" ", errors),
                Questions = questions
            };
        }
    }

    /// <summary>Every entry sharing a key with another file is marked invalid, all of them.</summary>
    private static IReadOnlyList<DefaultSuiteCatalogEntry> FlagDuplicateKeys(List<DefaultSuiteCatalogEntry> entries)
    {
        var filesByKey = entries
            .Where(e => e.Key != null)
            .GroupBy(e => e.Key!, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .ToDictionary(g => g.Key, g => g.Select(e => e.FileName).ToList(), StringComparer.Ordinal);

        if (filesByKey.Count == 0)
        {
            return entries;
        }

        return entries.Select(e =>
        {
            if (e.Key == null || !filesByKey.TryGetValue(e.Key, out var files))
            {
                return e;
            }

            string others = string.Join(", ", files.Where(f => !string.Equals(f, e.FileName, StringComparison.OrdinalIgnoreCase)));
            string duplicate = $"Key \"{e.Key}\" is also used by {others}; keys must be unique.";
            return e with { Error = e.Error == null ? duplicate : e.Error + " " + duplicate };
        }).ToList();
    }

    private static Dictionary<string, int> EmptyDifficultyCounts() => Enum.GetNames<BenchmarkDifficulty>()
        .ToDictionary(n => n, _ => 0, StringComparer.Ordinal);
}
