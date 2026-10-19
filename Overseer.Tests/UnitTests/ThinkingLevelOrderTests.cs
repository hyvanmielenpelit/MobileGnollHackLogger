namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Overseer.Services;
using Xunit;

/// <summary>
/// The standardized thinking-level order the model pickers sort by
/// (<c>Overseer/Services/ModelCatalogs/ThinkingLevelOrder.json</c>), against every embedded catalog
/// and retired entry: each entry's levels are known to the order and listed lowest to highest in it,
/// and the order holds no level that no entry uses.
/// </summary>
public class ThinkingLevelOrderTests
{
    private const string OrderResourceName = "Overseer.Services.ModelCatalogs.ThinkingLevelOrder.json";
    private const string OrderPath = "Overseer/Services/ModelCatalogs/ThinkingLevelOrder.json";

    private static readonly string[] Providers = { "Anthropic", "OpenAI", "Google" };

    private static IReadOnlyList<string> ReadLevels()
    {
        using var stream = typeof(ModelMetadataService).Assembly.GetManifestResourceStream(OrderResourceName);
        Assert.NotNull(stream);
        using var document = JsonDocument.Parse(stream!);
        Assert.True(document.RootElement.TryGetProperty("levels", out var levels), $"{OrderPath} has no levels array.");
        return levels.EnumerateArray().Select(level => level.GetString() ?? string.Empty).ToList();
    }

    /// <summary>Every catalog and retired entry with the levels it lists, in its own order.</summary>
    private static IEnumerable<(string Provider, string DisplayName, IReadOnlyList<string> Levels)> Entries()
    {
        var service = new ModelMetadataService();
        foreach (var provider in Providers)
        {
            foreach (var entry in service.GetCatalogEntries(provider))
            {
                yield return (provider, entry.DisplayName, entry.ThinkingLevels ?? new List<string>());
            }
        }

        foreach (var retired in service.GetRetiredEntries())
        {
            yield return (retired.Provider, $"{retired.DisplayName} (retired)", retired.LastKnown?.ThinkingLevels ?? new List<string>());
        }
    }

    [Fact]
    public void Order_IsNonEmpty_WithoutBlanksOrDuplicates()
    {
        var levels = ReadLevels();

        Assert.NotEmpty(levels);
        Assert.All(levels, level => Assert.False(string.IsNullOrWhiteSpace(level), $"{OrderPath} lists a blank level."));
        var duplicates = levels.GroupBy(level => level.Trim(), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.True(duplicates.Count == 0, $"{OrderPath} lists {string.Join(", ", duplicates)} more than once.");
    }

    [Fact]
    public void EveryCatalogLevel_IsInTheOrder()
    {
        var known = new HashSet<string>(ReadLevels(), StringComparer.OrdinalIgnoreCase);

        var missing = Entries()
            .SelectMany(e => e.Levels.Where(level => !known.Contains(level.Trim()))
                .Select(level => $"{e.Provider} {e.DisplayName} lists thinking level {level}, which {OrderPath} does not; add it there at the rank the provider intends"))
            .ToList();

        Assert.True(missing.Count == 0, string.Join(Environment.NewLine, missing));
    }

    [Fact]
    public void EveryCatalogEntry_ListsItsLevelsLowestToHighest()
    {
        var levels = ReadLevels();
        var rank = levels.Select((level, i) => (level, i)).ToDictionary(p => p.level, p => p.i, StringComparer.OrdinalIgnoreCase);

        var outOfOrder = new List<string>();
        foreach (var (provider, displayName, entryLevels) in Entries())
        {
            var known = entryLevels.Where(level => rank.ContainsKey(level.Trim())).ToList();
            for (int i = 1; i < known.Count; i++)
            {
                if (rank[known[i - 1].Trim()] >= rank[known[i].Trim()])
                {
                    outOfOrder.Add($"{provider} {displayName} lists {known[i - 1]} before {known[i]}, against the order in {OrderPath}");
                }
            }
        }

        Assert.True(outOfOrder.Count == 0, string.Join(Environment.NewLine, outOfOrder));
    }

    [Fact]
    public void EveryLevelInTheOrder_IsUsedByACatalogEntry()
    {
        var used = new HashSet<string>(Entries().SelectMany(e => e.Levels).Select(level => level.Trim()), StringComparer.OrdinalIgnoreCase);

        var unused = ReadLevels().Where(level => !used.Contains(level)).ToList();

        Assert.True(unused.Count == 0, $"{OrderPath} lists {string.Join(", ", unused)}, which no catalog or retired entry uses.");
    }
}
