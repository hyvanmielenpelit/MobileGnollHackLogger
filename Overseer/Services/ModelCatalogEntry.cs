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
/// The second rate card a provider applies to a request whose prompt exceeds a threshold. Null where
/// the provider publishes a single flat rate — which is every Anthropic model and every Gemini Flash
/// model, so this is genuinely optional and is never inferred.
///
/// One step, deliberately: no provider publishes more than one.
/// </summary>
public class ModelCatalogLongContextPricing
{
    /// <summary>
    /// Prompt-token count a <b>single request</b> must exceed for this card to apply, compared
    /// against the provider's own reported prompt tokens for that call — cache reads included, which
    /// is how providers count input for the threshold.
    ///
    /// Never compared against a turn's summed tokens. An agentic turn makes tens of calls and its sum
    /// routinely exceeds any threshold while no individual request comes close.
    /// </summary>
    public int ThresholdInputTokens { get; set; }

    public decimal InputPerMillion { get; set; }
    public decimal OutputPerMillion { get; set; }
    public decimal? CachedInputPerMillion { get; set; }

    /// <summary>Null where the provider's page does not say cache writes are surcharged; costing then
    /// falls back to the base cache-write rate rather than guessing a multiplier.</summary>
    public decimal? CacheWritePerMillion { get; set; }
}

/// <summary>
/// A price change the provider has already announced for a future date — a promotional campaign
/// ending, introductory pricing expiring, or a scheduled rise or cut.
///
/// Structurally the same idea as <see cref="ModelCatalogLongContextPricing"/>: a second rate card
/// chosen by a condition. That one is keyed on the size of the request; this one on the calendar.
///
/// It exists because <c>AsOf</c> records when a price was *verified*, not when it *expires*. Without
/// this, a campaign's end date passes and every turn is costed at the promotional rate until somebody
/// happens to re-read a pricing page — silently, because nothing is wrong enough to fail.
///
/// One change per entry, not a queue: no provider publishes two. It replaces the four base rates
/// only, not the long-context card or the tier multipliers; if a provider ever schedules a change to
/// those, the entry is edited by hand on the day.
/// </summary>
public class ModelCatalogScheduledPricing
{
    /// <summary>ISO date (YYYY-MM-DD) from which these rates apply, compared against UTC today.
    /// Provider billing boundaries are in their own timezones; a one-day edge on a promotional
    /// boundary is not worth modelling.</summary>
    public string EffectiveFrom { get; set; } = string.Empty;

    public decimal InputPerMillion { get; set; }
    public decimal OutputPerMillion { get; set; }
    public decimal? CachedInputPerMillion { get; set; }
    public decimal? CacheWritePerMillion { get; set; }

    /// <summary>Free text for the operator, e.g. "Promotional pricing through 2026-12-31". Shown in
    /// the admin views beside the price; never parsed.</summary>
    public string? Note { get; set; }
}

/// <summary>
/// Published list price for one catalog entry, per million tokens. Null on the entry means
/// the price is not known to the catalog — a different fact from a price of zero, and
/// rendered as "not published" rather than "0.00".
/// All catalog prices are in USD. Overseer supports no other currency, so the catalog does
/// not carry a currency field.
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

    /// <summary>
    /// YYYY-MM-DD the figures were read from the provider's pricing page. Printed beside
    /// the price so a stale list price is visible instead of silent.
    /// </summary>
    public string AsOf { get; set; } = string.Empty;

    /// <summary>Long-prompt rate card, or null for a flat-rate model. Absolute rates rather than
    /// multipliers: providers surcharge input and output by different factors (OpenAI 2x/1.5x, Google Pro
    /// 2x/1.5x), so a single multiplier cannot express the published price.</summary>
    public ModelCatalogLongContextPricing? LongContext { get; set; }

    /// <summary>
    /// Scalar price multipliers keyed by the provider's <b>served</b> service tier, normalized the way
    /// ProviderHelper.NormalizeServiceTier normalizes it (lower-case, no SERVICE_TIER_ prefix): "flex",
    /// "priority", "fast", "batch". A tier not listed — including "default" and "standard" — costs 1.0.
    ///
    /// A scalar, unlike LongContext, because every published tier scales all four rates equally: OpenAI
    /// flex/batch 0.5x and fast 2.0x, Google flex/batch 0.5x and priority 1.8x.
    /// </summary>
    public Dictionary<string, decimal>? ServiceTierMultipliers { get; set; }

    /// <summary>An already-announced future price change, or null. The rates above are always the ones
    /// in force today; this is the card that takes over on its date.</summary>
    public ModelCatalogScheduledPricing? ScheduledChange { get; set; }
}
