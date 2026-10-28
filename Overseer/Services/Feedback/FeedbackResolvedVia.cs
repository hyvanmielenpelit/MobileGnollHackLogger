namespace Overseer.Services.Feedback;

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

/// <summary>
/// The channels a feedback fix went through, as ids. Stored on an issue or a triage row as the chosen ids in
/// <see cref="All"/> order, comma-separated, and null when none; a list of ids on the wire and in files. The UI owns
/// the labels.
/// </summary>
public static class FeedbackResolvedVia
{
    public const string Wiki = "wiki";
    public const string KnowledgeBase = "knowledge_base";
    public const string OverseerRelease = "overseer_release";
    public const string GnollHackRelease = "gnollhack_release";
    public const string ServerConfiguration = "server_configuration";
    public const string Other = "other";

    /// <summary>The filter and facet value for an issue with no stored ids.</summary>
    public const string NoneFilterValue = "none";

    public static IReadOnlyList<string> All { get; } = new[]
    {
        Wiki, KnowledgeBase, OverseerRelease, GnollHackRelease, ServerConfiguration, Other
    };

    /// <summary>
    /// Reads a list of ids: each trimmed and compared case-sensitively, duplicates dropped, in <see cref="All"/> order.
    /// Null reads as an empty list. False with an error naming the first unknown id and the allowed ones.
    /// </summary>
    public static bool TryParse(IEnumerable<string?>? values, out IReadOnlyList<string> ids, out string? error)
    {
        ids = Array.Empty<string>();
        error = null;
        var chosen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in values ?? Enumerable.Empty<string?>())
        {
            string value = raw?.Trim() ?? "";
            if (!All.Contains(value, StringComparer.Ordinal))
            {
                error = $"Unknown resolved-via value `{value}`; allowed: {string.Join(", ", All)}.";
                return false;
            }
            chosen.Add(value);
        }
        ids = All.Where(chosen.Contains).ToList();
        return true;
    }

    /// <summary>
    /// Reads the comma list of the Resolved via filter: ids and <see cref="NoneFilterValue"/>, each trimmed and
    /// compared case-sensitively, duplicates dropped, ids in <see cref="All"/> order. Null or blank filters nothing.
    /// False with an error naming the first unknown value.
    /// </summary>
    public static bool TryParseFilter(string? commaList, out IReadOnlyList<string> ids, out bool includeNone,
        [NotNullWhen(false)] out string? error)
    {
        ids = Array.Empty<string>();
        includeNone = false;
        error = null;
        if (string.IsNullOrWhiteSpace(commaList)) return true;

        var chosen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in commaList.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (value == NoneFilterValue) { includeNone = true; continue; }
            if (!All.Contains(value, StringComparer.Ordinal))
            {
                error = $"Unknown resolved-via value `{value}`.";
                return false;
            }
            chosen.Add(value);
        }
        ids = All.Where(chosen.Contains).ToList();
        return true;
    }

    /// <summary>
    /// Whether a stored value holds any of <paramref name="ids"/>, or is null or empty when
    /// <paramref name="includeNone"/>. The in-memory form of the Resolved via filter.
    /// </summary>
    public static bool MatchesFilter(string? stored, IReadOnlyCollection<string> ids, bool includeNone)
    {
        if (string.IsNullOrEmpty(stored)) return includeNone;
        return FromStored(stored).Any(ids.Contains);
    }

    /// <summary>
    /// How many of <paramref name="storedValues"/>, one per issue, hold each id, in <see cref="All"/> order, then
    /// <see cref="NoneFilterValue"/> for the null or empty ones. Only present values.
    /// </summary>
    public static List<(string Value, int Count)> FacetCounts(IEnumerable<string?> storedValues)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var stored in storedValues)
        {
            var ids = string.IsNullOrEmpty(stored) ? new List<string> { NoneFilterValue } : FromStored(stored);
            foreach (var id in ids) counts[id] = counts.GetValueOrDefault(id) + 1;
        }
        return All.Append(NoneFilterValue).Where(counts.ContainsKey).Select(v => (v, counts[v])).ToList();
    }

    /// <summary>The stored form: comma-separated ids, null for none.</summary>
    public static string? ToStored(IReadOnlyCollection<string> ids)
        => ids.Count == 0 ? null : string.Join(",", All.Where(ids.Contains));

    /// <summary>The ids of a stored value, in <see cref="All"/> order; unknown tokens are ignored.</summary>
    public static List<string> FromStored(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored)) return new List<string>();
        var tokens = stored.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return All.Where(id => tokens.Contains(id, StringComparer.Ordinal)).ToList();
    }

    /// <summary>Whether two id lists hold the same ids, ignoring order.</summary>
    public static bool SameSet(IReadOnlyCollection<string> a, IReadOnlyCollection<string> b)
        => a.Count == b.Count && a.All(b.Contains);
}
