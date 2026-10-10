using System.Collections.Generic;

namespace Overseer.Services;

/// <summary>
/// A model the provider has withdrawn, from <c>ModelCatalogs/RetiredModels.json</c>. Its prefixes match
/// a model ID the same way catalog prefixes do (exactly, or followed by a snapshot suffix). It never
/// supplies runtime metadata or pricing.
/// </summary>
public class RetiredModelEntry
{
    public string Provider { get; set; } = string.Empty;
    public List<string> Prefixes { get; set; } = new();
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>ISO date (YYYY-MM-DD) the provider withdrew the model.</summary>
    public string RetiredOn { get; set; } = string.Empty;

    public string? Note { get; set; }

    /// <summary>Catalog model ID to offer as the switch target, or null.</summary>
    public string? Replacement { get; set; }

    public RetiredModelLastKnown? LastKnown { get; set; }
}

/// <summary>
/// The catalog card as it stood when the model was retired. Used only to prefill "Keep as custom model".
/// </summary>
public class RetiredModelLastKnown
{
    public List<string> ThinkingLevels { get; set; } = new();
    public int ContextWindowSize { get; set; }
    public int MaxOutputTokens { get; set; }
    public decimal? InputPerMillion { get; set; }
    public decimal? OutputPerMillion { get; set; }
    public decimal? CachedInputPerMillion { get; set; }
}
