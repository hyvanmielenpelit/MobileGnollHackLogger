namespace Overseer.Services.Benchmarking;

using System;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;

/// <summary>
/// Reads the corpus condition a wiki declares about itself in <c>.gnollbench-corpus.json</c> at its
/// root. A scrubbed benchmark branch carries the file; the live wiki does not. The wiki indexer never
/// indexes it: it keeps only <c>.md</c>, <c>.txt</c> and <c>.html</c> files and skips every path
/// segment that begins with a dot.
/// </summary>
/// <remarks>
/// Schema: <c>{ "condition": "scarce", "label": "…", "suite": "…", "baseWikiSha": "…",
/// "createdUtc": "…", "scrubbedPages": ["…"] }</c>. Only <c>condition</c> and <c>label</c> enter
/// the run's record.
/// </remarks>
public static class BenchmarkCorpusConditionMarker
{
    public const string FileName = ".gnollbench-corpus.json";

    /// <summary>What a marker that does not parse records.</summary>
    public const string Invalid = "invalid";

    /// <summary>What a run without a marker reads as, in comparability keys and reports.</summary>
    public const string Live = "live";

    public const int MaxLength = 128;

    /// <summary>
    /// <c>condition:label</c> from the marker under <paramref name="root"/>, at most 128
    /// characters; null when there is no marker; <see cref="Invalid"/> when it does not parse.
    /// </summary>
    public static string? Read(string? root, ILogger? logger = null)
    {
        if (string.IsNullOrWhiteSpace(root)) return null;
        string path = Path.Combine(root, FileName);
        if (!File.Exists(path)) return null;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var element = document.RootElement;
            if (element.ValueKind != JsonValueKind.Object
                || !element.TryGetProperty("condition", out var condition)
                || condition.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(condition.GetString()))
            {
                logger?.LogWarning("The corpus condition marker {Path} has no condition; the corpus is recorded as invalid.", path);
                return Invalid;
            }

            string label = element.TryGetProperty("label", out var labelElement) && labelElement.ValueKind == JsonValueKind.String
                ? labelElement.GetString() ?? string.Empty
                : string.Empty;
            string value = condition.GetString()!.Trim() + ":" + label.Trim();
            return value.Length > MaxLength ? value[..MaxLength] : value;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            logger?.LogWarning(ex, "The corpus condition marker {Path} does not parse; the corpus is recorded as invalid.", path);
            return Invalid;
        }
    }

    /// <summary>The printed form: <c>live</c>, or <c>scarce — label</c>.</summary>
    public static string Describe(string? corpusCondition)
    {
        if (string.IsNullOrWhiteSpace(corpusCondition)) return Live;
        int colon = corpusCondition.IndexOf(':');
        if (colon < 0) return corpusCondition;
        string condition = corpusCondition[..colon];
        string label = corpusCondition[(colon + 1)..];
        return label.Length == 0 ? condition : $"{condition} — {label}";
    }
}
