namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using MobileGnollHackLogger.Data;

/// <summary>
/// One unit of a question's gold evidence: a wiki page (optionally a section), a source file
/// (optionally a line range or a function) or a knowledge-base topic. <see cref="Personal"/> marks
/// the asker's own race or role page.
/// </summary>
public sealed class BenchmarkEvidenceUnit
{
    public const string WikiKind = "wiki";
    public const string SourceKind = "source";
    public const string KnowledgeBaseKind = "kb";

    public static readonly IReadOnlyList<string> Kinds = new[] { WikiKind, SourceKind, KnowledgeBaseKind };

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = string.Empty;

    /// <summary>Repository-relative with forward slashes for wiki and source; the topic for kb.</summary>
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("section")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Section { get; set; }

    /// <summary><c>a-b</c>, 1-based and inclusive.</summary>
    [JsonPropertyName("lines")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Lines { get; set; }

    [JsonPropertyName("function")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Function { get; set; }

    [JsonPropertyName("personal")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Personal { get; set; }
}

/// <summary>Serialization, validation and the suite-level fingerprint of gold evidence.</summary>
public static class BenchmarkEvidence
{
    private static readonly JsonSerializerOptions CanonicalOptions = new()
    {
        WriteIndented = false
    };

    private static readonly Regex LinesPattern = new(@"^(\d+)-(\d+)$", RegexOptions.CultureInvariant);

    /// <summary>The canonical JSON of a unit list; null for null or empty.</summary>
    public static string? ToJson(IReadOnlyList<BenchmarkEvidenceUnit>? units)
        => units == null || units.Count == 0 ? null : JsonSerializer.Serialize(units, CanonicalOptions);

    /// <summary>The units stored in <paramref name="json"/>; null when absent or unreadable.</summary>
    public static List<BenchmarkEvidenceUnit>? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var units = JsonSerializer.Deserialize<List<BenchmarkEvidenceUnit>>(json, CanonicalOptions);
            return units == null || units.Count == 0 ? null : units;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The first problem with <paramref name="units"/>, or null when every unit is valid.</summary>
    public static string? Validate(IReadOnlyList<BenchmarkEvidenceUnit>? units)
    {
        if (units == null) return null;
        for (int i = 0; i < units.Count; i++)
        {
            var unit = units[i];
            string where = $"Evidence unit {i + 1}";
            if (!BenchmarkEvidenceUnit.Kinds.Contains(unit.Kind, StringComparer.Ordinal))
                return $"{where}: unknown kind '{unit.Kind}' (expected {string.Join(", ", BenchmarkEvidenceUnit.Kinds)}).";
            if (string.IsNullOrWhiteSpace(unit.Path))
                return $"{where}: path is empty.";
            if (unit.Kind != BenchmarkEvidenceUnit.KnowledgeBaseKind)
            {
                if (unit.Path.Contains('\\') || unit.Path.StartsWith('/') || unit.Path.Contains(':') || unit.Path.Split('/').Contains(".."))
                    return $"{where}: path '{unit.Path}' must be repository-relative with forward slashes.";
            }
            if (unit.Lines != null)
            {
                if (unit.Kind != BenchmarkEvidenceUnit.SourceKind)
                    return $"{where}: lines apply to source units only.";
                if (!TryParseLines(unit.Lines, out _, out _))
                    return $"{where}: lines '{unit.Lines}' must read a-b with 1 <= a <= b.";
            }
            if (unit.Function != null && unit.Kind != BenchmarkEvidenceUnit.SourceKind)
                return $"{where}: function applies to source units only.";
            if (unit.Section != null && unit.Kind != BenchmarkEvidenceUnit.WikiKind)
                return $"{where}: section applies to wiki units only.";
        }
        return null;
    }

    public static bool TryParseLines(string? lines, out int start, out int end)
    {
        start = end = 0;
        if (lines == null) return false;
        var match = LinesPattern.Match(lines.Trim());
        if (!match.Success) return false;
        if (!int.TryParse(match.Groups[1].Value, out start) || !int.TryParse(match.Groups[2].Value, out end)) return false;
        return start >= 1 && start <= end;
    }

    /// <summary>
    /// SHA-256 (lower-case hex) of the ordinal-sorted <c>questionId:canonicalEvidenceJson</c> lines of
    /// every question that has evidence; null when none has.
    /// </summary>
    public static string? SuiteEvidenceSha(IEnumerable<BenchmarkQuestion> questions)
    {
        var lines = questions
            .Select(q => (q.Id, Json: ToJson(FromJson(q.EvidenceJson))))
            .Where(e => e.Json != null)
            .Select(e => $"{e.Id}:{e.Json}")
            .OrderBy(l => l, StringComparer.Ordinal)
            .ToList();
        if (lines.Count == 0) return null;
        var text = string.Concat(lines.Select(l => l + "\n"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }

    /// <summary>The printed form of <see cref="SuiteEvidenceSha"/>: its first 12 characters.</summary>
    public static string? SuiteEvidenceShaPrefix(IEnumerable<BenchmarkQuestion> questions)
    {
        var sha = SuiteEvidenceSha(questions);
        return sha == null ? null : sha[..12];
    }
}
