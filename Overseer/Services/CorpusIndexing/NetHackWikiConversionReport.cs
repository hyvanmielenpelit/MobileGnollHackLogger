using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace Overseer.Services.CorpusIndexing;

/// <summary>
/// One reason the converter skipped pages, with its page count. <c>Breakdown</c> lists
/// the parts of the count when the report gives them; null when it does not.
/// </summary>
/// <param name="Reason">The report's own key, such as <c>non_english</c>.</param>
public sealed record ConversionSkipReasonDto(string Reason, int Count, IReadOnlyList<ConversionSkipReasonDto>? Breakdown = null);

/// <summary>
/// What the last NetHack wiki conversion reported, read from <c>_conversion_report.json</c> in the
/// wiki folder. Every field the report lacks, or holds with an unexpected type, is null.
/// </summary>
public sealed class NetHackWikiConversionReportDto
{
    /// <summary>The report file's last write time.</summary>
    public DateTime ReportLastWriteUtc { get; init; }

    /// <summary><c>generated</c>: the converter's local time, as written.</summary>
    public string? GeneratedAt { get; init; }

    /// <summary><c>dump.modified</c>: the dump file's local modification time, as written.</summary>
    public string? DumpModified { get; init; }

    /// <summary><c>dump.bytes</c>.</summary>
    public long? DumpBytes { get; init; }

    /// <summary><c>duration_seconds</c>.</summary>
    public double? DurationSeconds { get; init; }

    /// <summary><c>pages.written</c>.</summary>
    public int? PagesWritten { get; init; }

    /// <summary><c>pages.redirects</c>.</summary>
    public int? Redirects { get; init; }

    /// <summary><c>pages.errors</c>.</summary>
    public int? Errors { get; init; }

    /// <summary>
    /// <c>pages.skipped</c>, one entry per property in file order: a number is its count, an array its
    /// length, an object the sum of its numbers with those numbers as the breakdown. An object named
    /// <c>X_by_Y</c> beside a number <c>X</c> is the breakdown of <c>X</c>, not a reason of its own.
    /// </summary>
    public IReadOnlyList<ConversionSkipReasonDto>? Skipped { get; init; }

    /// <summary><c>tables_converted</c>.</summary>
    public int? TablesConverted { get; init; }

    /// <summary>The length of <c>dropped_templates</c>.</summary>
    public int? DroppedTemplates { get; init; }

    /// <summary><c>quality.largest_file.name</c>.</summary>
    public string? LargestFileName { get; init; }

    /// <summary><c>quality.largest_file.bytes</c>.</summary>
    public long? LargestFileBytes { get; init; }

    /// <summary>The length of <c>quality.files_over_500_kb</c>.</summary>
    public int? FilesOver500Kb { get; init; }
}

/// <summary>
/// Reads the NetHack wiki converter's report from <c>&lt;NetHackWikiPath&gt;\_conversion_report.json</c>.
/// A parsed report, or the error of a malformed one, is reused until the file's last write time
/// changes; a read that fails on I/O is retried on the next call.
/// </summary>
public sealed class NetHackWikiConversionReportReader
{
    public const string ConfigKey = "NetHackWikiPath";
    public const string FileName = "_conversion_report.json";

    private const long MaxReportBytes = 16L * 1024 * 1024;
    private const string BreakdownSeparator = "_by_";

    private readonly IConfiguration _configuration;
    private readonly object _cacheLock = new();
    private bool _hasCache;
    private string? _cachedPath;
    private DateTime _cachedWriteUtc;
    private NetHackWikiConversionReportDto? _cachedReport;
    private string? _cachedError;

    public NetHackWikiConversionReportReader(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _configuration = configuration;
    }

    /// <summary>The report's full path; null when <c>NetHackWikiPath</c> is not set or is not a valid path.</summary>
    public string? ReportPath
    {
        get
        {
            string? root = _configuration[ConfigKey];
            if (string.IsNullOrWhiteSpace(root)) return null;
            try
            {
                return Path.Combine(root, FileName);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }
    }

    /// <summary>The report file's last write time; null when the key is not set or the file is missing or unreadable.</summary>
    public DateTime? GetReportLastWriteUtc()
    {
        string? path = ReportPath;
        if (path == null) return null;
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? info.LastWriteTimeUtc : null;
        }
        catch (Exception ex) when (IsFileSystemError(ex))
        {
            return null;
        }
    }

    /// <summary>
    /// The parsed report; null with a null <paramref name="error"/> when the key is not set or the file
    /// does not exist, and null with <paramref name="error"/> set when the file cannot be read or is not
    /// a JSON object. Never throws. While the file's last write time is unchanged, the same instance
    /// is returned.
    /// </summary>
    public NetHackWikiConversionReportDto? Read(out string? error)
    {
        error = null;
        string? path = ReportPath;
        if (path == null) return null;

        DateTime writeUtc;
        long length;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return null;
            writeUtc = info.LastWriteTimeUtc;
            length = info.Length;
        }
        catch (Exception ex) when (IsFileSystemError(ex))
        {
            error = "The conversion report could not be read: " + Describe(ex);
            return null;
        }

        lock (_cacheLock)
        {
            if (_hasCache && _cachedWriteUtc == writeUtc && string.Equals(_cachedPath, path, StringComparison.OrdinalIgnoreCase))
            {
                error = _cachedError;
                return _cachedReport;
            }
        }

        if (length > MaxReportBytes)
        {
            error = $"The conversion report is larger than {MaxReportBytes / (1024 * 1024)} MB and was not read.";
            return null;
        }

        NetHackWikiConversionReportDto? report = null;
        string? parseError = null;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var document = JsonDocument.Parse(stream, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                parseError = "The conversion report is not a JSON object.";
            }
            else
            {
                report = Map(document.RootElement, writeUtc);
            }
        }
        catch (JsonException ex)
        {
            parseError = "The conversion report is not valid JSON: " + FirstLine(ex.Message);
        }
        catch (Exception ex) when (IsFileSystemError(ex))
        {
            /* Not cached: a file locked by the converter or a scanner reads normally on a later call. */
            error = "The conversion report could not be read: " + Describe(ex);
            return null;
        }

        lock (_cacheLock)
        {
            _hasCache = true;
            _cachedPath = path;
            _cachedWriteUtc = writeUtc;
            _cachedReport = report;
            _cachedError = parseError;
        }

        error = parseError;
        return report;
    }

    private static NetHackWikiConversionReportDto Map(JsonElement root, DateTime writeUtc)
    {
        return new NetHackWikiConversionReportDto
        {
            ReportLastWriteUtc = writeUtc,
            GeneratedAt = AsString(Find(root, "generated")),
            DumpModified = AsString(Find(root, "dump", "modified")),
            DumpBytes = AsInt64(Find(root, "dump", "bytes")),
            DurationSeconds = AsDouble(Find(root, "duration_seconds")),
            PagesWritten = AsInt32(Find(root, "pages", "written")),
            Redirects = AsInt32(Find(root, "pages", "redirects")),
            Errors = AsInt32(Find(root, "pages", "errors")),
            Skipped = MapSkipped(Find(root, "pages", "skipped")),
            TablesConverted = AsInt32(Find(root, "tables_converted")),
            DroppedTemplates = ArrayLength(Find(root, "dropped_templates")),
            LargestFileName = AsString(Find(root, "quality", "largest_file", "name")),
            LargestFileBytes = AsInt64(Find(root, "quality", "largest_file", "bytes")),
            FilesOver500Kb = ArrayLength(Find(root, "quality", "files_over_500_kb"))
        };
    }

    private static IReadOnlyList<ConversionSkipReasonDto>? MapSkipped(JsonElement? skipped)
    {
        if (skipped is not { ValueKind: JsonValueKind.Object } section) return null;

        var properties = section.EnumerateObject().ToList();
        var numberNames = new HashSet<string>(
            properties.Where(p => p.Value.ValueKind == JsonValueKind.Number).Select(p => p.Name),
            StringComparer.Ordinal);

        /* "variant_only_by_rule" beside "variant_only": the parts of that count. */
        var breakdowns = new Dictionary<string, IReadOnlyList<ConversionSkipReasonDto>>(StringComparer.Ordinal);
        foreach (var property in properties)
        {
            if (BreakdownTarget(property, numberNames) is string target && !breakdowns.ContainsKey(target))
            {
                breakdowns[target] = NumericParts(property.Value);
            }
        }

        var reasons = new List<ConversionSkipReasonDto>();
        foreach (var property in properties)
        {
            var value = property.Value;
            switch (value.ValueKind)
            {
                case JsonValueKind.Number:
                {
                    if (AsInt32(value) is int count)
                    {
                        reasons.Add(new ConversionSkipReasonDto(property.Name, count,
                            breakdowns.TryGetValue(property.Name, out var parts) ? parts : null));
                    }
                    break;
                }

                case JsonValueKind.Array:
                    reasons.Add(new ConversionSkipReasonDto(property.Name, value.GetArrayLength()));
                    break;

                case JsonValueKind.Object:
                {
                    if (BreakdownTarget(property, numberNames) != null) break;
                    var numbers = NumericParts(value);
                    if (numbers.Count > 0)
                    {
                        reasons.Add(new ConversionSkipReasonDto(property.Name, ClampedSum(numbers), numbers));
                    }
                    break;
                }
            }
        }
        return reasons;
    }

    /// <summary>The number property this object breaks down; null when it is not a breakdown.</summary>
    private static string? BreakdownTarget(JsonProperty property, HashSet<string> numberNames)
    {
        if (property.Value.ValueKind != JsonValueKind.Object) return null;
        int separator = property.Name.IndexOf(BreakdownSeparator, StringComparison.Ordinal);
        if (separator <= 0) return null;
        string target = property.Name.Substring(0, separator);
        return numberNames.Contains(target) ? target : null;
    }

    private static List<ConversionSkipReasonDto> NumericParts(JsonElement obj)
    {
        var parts = new List<ConversionSkipReasonDto>();
        foreach (var property in obj.EnumerateObject())
        {
            if (AsInt32(property.Value) is int count) parts.Add(new ConversionSkipReasonDto(property.Name, count));
        }
        return parts;
    }

    private static int ClampedSum(IEnumerable<ConversionSkipReasonDto> parts)
    {
        long sum = parts.Sum(p => (long)p.Count);
        return (int)Math.Clamp(sum, int.MinValue, int.MaxValue);
    }

    private static JsonElement? Find(JsonElement root, params string[] path)
    {
        JsonElement current = root;
        foreach (string name in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(name, out var next)) return null;
            current = next;
        }
        return current;
    }

    private static string? AsString(JsonElement? element) =>
        element is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;

    private static long? AsInt64(JsonElement? element) =>
        element is { ValueKind: JsonValueKind.Number } value && value.TryGetInt64(out long result) ? result : null;

    private static int? AsInt32(JsonElement? element)
    {
        if (AsInt64(element) is not long value) return null;
        return (int)Math.Clamp(value, int.MinValue, int.MaxValue);
    }

    private static double? AsDouble(JsonElement? element) =>
        element is { ValueKind: JsonValueKind.Number } value && value.TryGetDouble(out double result) ? result : null;

    private static int? ArrayLength(JsonElement? element) =>
        element is { ValueKind: JsonValueKind.Array } value ? value.GetArrayLength() : null;

    private static bool IsFileSystemError(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException;

    private static string Describe(Exception ex) => ex.GetType().Name + ": " + FirstLine(ex.Message);

    private static string FirstLine(string text)
    {
        int end = text.IndexOfAny(new[] { '\r', '\n' });
        return (end >= 0 ? text.Substring(0, end) : text).Trim();
    }
}
