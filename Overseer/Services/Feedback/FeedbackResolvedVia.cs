namespace Overseer.Services.Feedback;

using System;
using System.Collections.Generic;
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
