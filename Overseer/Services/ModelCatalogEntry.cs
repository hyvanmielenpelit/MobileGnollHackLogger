using System.Collections.Generic;

namespace Overseer.Services;

public class ModelCatalogEntry
{
    public List<string> Prefixes { get; set; } = new();
    public string DisplayName { get; set; } = string.Empty;
    public string ReleaseDate { get; set; } = string.Empty;
    public List<string> ThinkingLevels { get; set; } = new();
    public List<string> ReasoningModes { get; set; } = new();
    public List<string> ReasoningSummaries { get; set; } = new();
    public int ContextWindowSize { get; set; }
    public int MaxOutputTokens { get; set; }
    public bool SupportsSubAgentCoordination { get; set; } = true;
    public bool SupportsSubAgentExecution { get; set; } = true;
    public ModelCatalogPricing? Pricing { get; set; }
}

/// <summary>
/// Published list price for one catalog entry, per million tokens. Null on the entry means
/// the price is not known to the catalog — a different fact from a price of zero, and
/// rendered as "not published" rather than "0.00".
/// </summary>
public class ModelCatalogPricing
{
    public decimal InputPerMillion { get; set; }
    public decimal OutputPerMillion { get; set; }

    /// <summary>Null where the provider publishes no cached-input discount.</summary>
    public decimal? CachedInputPerMillion { get; set; }

    /// <summary>
    /// Cache *write* rate, which several providers charge at a premium over base input
    /// (Anthropic bills cache creation above the input rate). Null where the provider does
    /// not bill cache writes separately; costing then omits them rather than guessing.
    /// </summary>
    public decimal? CacheWritePerMillion { get; set; }

    /// <summary>ISO 4217, e.g. "USD". Costs are only ever summed within one currency.</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// YYYY-MM-DD the figures were read from the provider's pricing page. Printed beside
    /// the price so a stale list price is visible instead of silent.
    /// </summary>
    public string AsOf { get; set; } = string.Empty;
}
