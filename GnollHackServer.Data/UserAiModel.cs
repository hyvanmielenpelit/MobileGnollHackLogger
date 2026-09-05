namespace MobileGnollHackLogger.Data;

using System.ComponentModel.DataAnnotations;

public class UserAiModel
{
    public long Id { get; set; }

    [MaxLength(450)]
    public string AspNetUserId { get; set; } = default!;
    
    public ApplicationUser? AspNetUser { get; set; }

    [MaxLength(64)]
    public string Provider { get; set; } = default!;

    [MaxLength(128)]
    public string ModelId { get; set; } = default!;  // e.g. "claude-sonnet-4-20250514"

    [MaxLength(128)]
    public string? DisplayName { get; set; }  // User-facing label

    [MaxLength(32)]
    public string? DisplayNameMode { get; set; }  // "model_name" | "model_id" | "custom"; null = legacy row

    [MaxLength(32)]
    public string? ThinkingLevel { get; set; }

    [MaxLength(32)]
    public string? ReasoningMode { get; set; }

    [MaxLength(32)]
    public string? ReasoningSummary { get; set; }

    [MaxLength(64)]
    public string? ServiceTier { get; set; }

    public int? MaxInputTokens { get; set; }
    public int? MaxOutputTokens { get; set; }

    /// <summary>
    /// "default" | "custom"; null = legacy row, treated as "default". Mirrors
    /// <see cref="DisplayNameMode"/>: an explicit mode is what lets a custom price legitimately
    /// leave CachedInputPricePerMillion null, which a bare "all three nulls mean default" rule
    /// cannot express.
    /// </summary>
    [MaxLength(32)]
    public string? PricingMode { get; set; }

    public decimal? InputPricePerMillion { get; set; }
    public decimal? OutputPricePerMillion { get; set; }
    public decimal? CachedInputPricePerMillion { get; set; }

    public int OrderIndex { get; set; }  // For drag-and-drop ordering
}
