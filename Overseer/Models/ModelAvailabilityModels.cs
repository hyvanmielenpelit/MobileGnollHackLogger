namespace Overseer.Models;

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

/// <summary>
/// The persisted model catalog modes. "catalog" means the model ID is expected to be in the model
/// catalog; "custom" means the owner uses an ID the catalog does not describe, with the row's own
/// limits and prices.
/// </summary>
public static class ModelCatalogModes
{
    public const string Catalog = "catalog";
    public const string Custom = "custom";

    /// <summary>Maps "custom" (case-insensitive) to "custom", and anything else, null included, to "catalog".</summary>
    public static string Normalize(string? value) =>
        string.Equals(value, Custom, StringComparison.OrdinalIgnoreCase)
            ? Custom
            : Catalog;
}

/// <summary>How a model row relates to the model catalog, in the order <c>ModelAvailabilityService.Evaluate</c> tests.</summary>
public enum ModelAvailabilityStatus
{
    Available,
    Custom,
    CustomEndpoint,
    Retired,
    NotInCatalog
}

/// <summary>Whether a user model or system configuration still names a model the catalog offers, and what to do if not.</summary>
public class ModelAvailabilityDto
{
    [JsonIgnore]
    public ModelAvailabilityStatus Kind { get; set; }

    /// <summary>"available", "custom", "customEndpoint", "retired" or "notInCatalog".</summary>
    public string Status => ToWireValue(Kind);

    /// <summary>True for a retired model and for one the catalog does not describe.</summary>
    public bool NeedsAttention => Kind is ModelAvailabilityStatus.Retired or ModelAvailabilityStatus.NotInCatalog;

    /// <summary>ISO date (YYYY-MM-DD) the provider withdrew a retired model.</summary>
    public string? RetiredOn { get; set; }

    public string? Note { get; set; }

    /// <summary>The retired entry's display name, else null.</summary>
    public string? CatalogDisplayName { get; set; }

    /// <summary>The retired entry's replacement, only when the catalog offers it.</summary>
    public ModelReplacementDto? Replacement { get; set; }

    /// <summary>Values to prefill "Keep as custom model" with.</summary>
    public SuggestedCustomDto? SuggestedCustom { get; set; }

    public static string ToWireValue(ModelAvailabilityStatus status) => status switch
    {
        ModelAvailabilityStatus.Available => "available",
        ModelAvailabilityStatus.Custom => "custom",
        ModelAvailabilityStatus.CustomEndpoint => "customEndpoint",
        ModelAvailabilityStatus.Retired => "retired",
        ModelAvailabilityStatus.NotInCatalog => "notInCatalog",
        _ => "notInCatalog"
    };
}

public class ModelReplacementDto
{
    public string ModelId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}

public class SuggestedCustomDto
{
    public int? MaxInputTokens { get; set; }
    public int? MaxOutputTokens { get; set; }
    public decimal? InputPricePerMillion { get; set; }
    public decimal? OutputPricePerMillion { get; set; }
    public decimal? CachedInputPricePerMillion { get; set; }
}

/// <summary>The actions <see cref="ModelResolutionRequest.Action"/> accepts.</summary>
public static class ModelResolutionActions
{
    public const string Switch = "switch";
    public const string KeepCustom = "keepCustom";
}

/// <summary>Resolves a model row that needs attention: switch it to a catalog model, or keep it as a custom model.</summary>
public class ModelResolutionRequest
{
    /// <summary>"switch" or "keepCustom".</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>The catalog model ID to switch to; "switch" only.</summary>
    public string? TargetModelId { get; set; }

    public int? MaxInputTokens { get; set; }
    public int? MaxOutputTokens { get; set; }
    public decimal? InputPricePerMillion { get; set; }
    public decimal? OutputPricePerMillion { get; set; }
    public decimal? CachedInputPricePerMillion { get; set; }

    /// <summary>True to compute the changes without saving them.</summary>
    public bool DryRun { get; set; }
}

public class ModelResolutionResult
{
    public List<ModelResolutionChange> Changes { get; set; } = new();

    /// <summary>Work using the system configuration right now, which the change would affect.</summary>
    public List<SystemConfigBlockerDto> Blockers { get; set; } = new();

    /// <summary>The row as the endpoint returns it elsewhere, after the change (or as it would be, on a dry run).</summary>
    public object? Model { get; set; }
}

/// <summary>One field a resolution changes, named as the model form labels it.</summary>
public class ModelResolutionChange
{
    public string Field { get; set; } = string.Empty;
    public string? From { get; set; }
    public string? To { get; set; }
    public string? Note { get; set; }
}

/// <summary>A catalog model a row can be switched to.</summary>
public class CatalogTargetDto
{
    public string ModelId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string ReleaseDate { get; set; } = string.Empty;
    public List<string> ThinkingLevels { get; set; } = new();
    public int ContextWindowSize { get; set; }
    public int MaxOutputTokens { get; set; }
    public decimal? InputPerMillion { get; set; }
    public decimal? OutputPerMillion { get; set; }
}
