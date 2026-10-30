namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// What information the candidate may reach in a run. Production is what a user gets; the other
/// two are benchmark-only conditions, analyzed against a production run as a condition contrast.
/// </summary>
public enum BenchmarkInformationCondition
{
    Production,

    /// <summary>The wiki, knowledge-base and lookup tools withheld; the source and stats tools kept.</summary>
    SourceOnly,

    /// <summary>No tools at all.</summary>
    ClosedBook
}

public static class BenchmarkInformationConditions
{
    public const string ProductionWire = "production";
    public const string SourceOnlyWire = "sourceOnly";
    public const string ClosedBookWire = "closedBook";

    /// <summary>
    /// The tools withheld in <see cref="BenchmarkInformationCondition.SourceOnly"/>: every tool that
    /// reads a wiki or the knowledge base. The lookup tools read the GnollHack wiki.
    /// </summary>
    public static readonly IReadOnlyList<string> SourceOnlyExcludedTools = new[]
    {
        "wiki_search",
        "wiki_view",
        "wiki_overview",
        "get_knowledge_article",
        "nethack_wiki_search",
        "nethack_wiki_view",
        "monster_lookup",
        "item_lookup"
    };

    /// <summary>
    /// Appended to the candidate prompt in <see cref="BenchmarkInformationCondition.SourceOnly"/>. The
    /// frozen prefix and <c>_policy.md</c> keep telling the model to consult the knowledge base and
    /// the wiki first, so the notice says those instructions do not apply.
    /// </summary>
    public const string SourceOnlyNotice =
        "In this session the GnollHack wiki, the NetHack wiki, the wiki overview, the knowledge base and the monster and item lookup tools are not available, "
        + "and instructions elsewhere in this prompt that tell you to consult them first do not apply. "
        + "Treat the GnollHack source code, read with the source code and stats tools, as the source of truth.";

    /// <summary>Parses a wire name; null and empty mean Production. False for an unknown value.</summary>
    public static bool TryParse(string? wire, out BenchmarkInformationCondition condition)
    {
        condition = BenchmarkInformationCondition.Production;
        if (string.IsNullOrWhiteSpace(wire)) return true;
        switch (wire.Trim())
        {
            case ProductionWire:
                condition = BenchmarkInformationCondition.Production;
                return true;
            case SourceOnlyWire:
                condition = BenchmarkInformationCondition.SourceOnly;
                return true;
            case ClosedBookWire:
                condition = BenchmarkInformationCondition.ClosedBook;
                return true;
            default:
                return false;
        }
    }

    /// <summary>The condition a stored wire name records; Production for null or an unknown value.</summary>
    public static BenchmarkInformationCondition FromWire(string? wire)
        => TryParse(wire, out var condition) ? condition : BenchmarkInformationCondition.Production;

    public static string ToWire(this BenchmarkInformationCondition condition) => condition switch
    {
        BenchmarkInformationCondition.SourceOnly => SourceOnlyWire,
        BenchmarkInformationCondition.ClosedBook => ClosedBookWire,
        _ => ProductionWire
    };

    /// <summary>The wire name a record stores: null for Production, so production records keep their bytes.</summary>
    public static string? ToStoredWire(this BenchmarkInformationCondition condition)
        => condition == BenchmarkInformationCondition.Production ? null : condition.ToWire();

    public static string DisplayName(this BenchmarkInformationCondition condition) => condition switch
    {
        BenchmarkInformationCondition.SourceOnly => "Source code only — no wiki, knowledge base or lookup tools",
        BenchmarkInformationCondition.ClosedBook => "Closed book — no tools",
        _ => "Production — all tools (production default)"
    };

    /// <summary>The condition's name alone, as a sentence names it: <i>Source code only</i>.</summary>
    public static string ShortName(this BenchmarkInformationCondition condition) => condition switch
    {
        BenchmarkInformationCondition.SourceOnly => "Source code only",
        BenchmarkInformationCondition.ClosedBook => "Closed book",
        _ => "Production"
    };

    /// <summary>
    /// The candidate's tool allowlist under <paramref name="condition"/>: the configured list as it
    /// is, without the excluded tools in its original order, or empty.
    /// </summary>
    public static IReadOnlyList<string> ResolveAllowedTools(
        this BenchmarkInformationCondition condition, IReadOnlyList<string> configured) => condition switch
    {
        BenchmarkInformationCondition.SourceOnly => configured
            .Where(t => !SourceOnlyExcludedTools.Contains(t, StringComparer.OrdinalIgnoreCase))
            .ToList(),
        BenchmarkInformationCondition.ClosedBook => Array.Empty<string>(),
        _ => configured
    };
}
