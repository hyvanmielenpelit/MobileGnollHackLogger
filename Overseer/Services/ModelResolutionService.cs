using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MobileGnollHackLogger.Data;
using Overseer.Models;

namespace Overseer.Services;

/// <summary>
/// The model fields a user model and a system configuration share, which a resolution reads and rewrites.
/// </summary>
public sealed record ModelSettings
{
    public string Provider { get; init; } = string.Empty;
    public string ModelId { get; init; } = string.Empty;
    public string? DisplayName { get; init; }
    public string? DisplayNameMode { get; init; }
    public string? ThinkingLevel { get; init; }
    public string? ReasoningMode { get; init; }
    public string? ReasoningSummary { get; init; }
    public string? ServiceTier { get; init; }
    public int? MaxInputTokens { get; init; }
    public int? MaxOutputTokens { get; init; }
    public string? PricingMode { get; init; }
    public decimal? InputPricePerMillion { get; init; }
    public decimal? OutputPricePerMillion { get; init; }
    public decimal? CachedInputPricePerMillion { get; init; }
    public string? ModelCatalogMode { get; init; }

    public static ModelSettings FromUserModel(UserAiModel model) => new()
    {
        Provider = model.Provider,
        ModelId = model.ModelId,
        DisplayName = model.DisplayName,
        DisplayNameMode = model.DisplayNameMode,
        ThinkingLevel = model.ThinkingLevel,
        ReasoningMode = model.ReasoningMode,
        ReasoningSummary = model.ReasoningSummary,
        ServiceTier = model.ServiceTier,
        MaxInputTokens = model.MaxInputTokens,
        MaxOutputTokens = model.MaxOutputTokens,
        PricingMode = model.PricingMode,
        InputPricePerMillion = model.InputPricePerMillion,
        OutputPricePerMillion = model.OutputPricePerMillion,
        CachedInputPricePerMillion = model.CachedInputPricePerMillion,
        ModelCatalogMode = model.ModelCatalogMode
    };

    public static ModelSettings FromSystemConfig(SystemAiApiConfiguration config) => new()
    {
        Provider = config.Provider,
        ModelId = config.ModelId,
        DisplayName = config.DisplayName,
        DisplayNameMode = config.DisplayNameMode,
        ThinkingLevel = config.ThinkingLevel,
        ReasoningMode = config.ReasoningMode,
        ReasoningSummary = config.ReasoningSummary,
        ServiceTier = config.ServiceTier,
        MaxInputTokens = config.MaxInputTokens,
        MaxOutputTokens = config.MaxOutputTokens,
        PricingMode = config.PricingMode,
        InputPricePerMillion = config.InputPricePerMillion,
        OutputPricePerMillion = config.OutputPricePerMillion,
        CachedInputPricePerMillion = config.CachedInputPricePerMillion,
        ModelCatalogMode = config.ModelCatalogMode
    };

    /// <summary>Writes every field except <see cref="Provider"/>, which a resolution never changes.</summary>
    public void ApplyTo(UserAiModel model)
    {
        model.ModelId = ModelId;
        model.DisplayName = DisplayName;
        model.DisplayNameMode = DisplayNameMode;
        model.ThinkingLevel = ThinkingLevel;
        model.ReasoningMode = ReasoningMode;
        model.ReasoningSummary = ReasoningSummary;
        model.ServiceTier = ServiceTier;
        model.MaxInputTokens = MaxInputTokens;
        model.MaxOutputTokens = MaxOutputTokens;
        model.PricingMode = PricingMode;
        model.InputPricePerMillion = InputPricePerMillion;
        model.OutputPricePerMillion = OutputPricePerMillion;
        model.CachedInputPricePerMillion = CachedInputPricePerMillion;
        model.ModelCatalogMode = ModelCatalogMode;
    }

    /// <summary>Writes every field except <see cref="Provider"/>; a null display name keeps the configuration's own, which is required.</summary>
    public void ApplyTo(SystemAiApiConfiguration config)
    {
        config.ModelId = ModelId;
        config.DisplayName = DisplayName ?? config.DisplayName;
        config.DisplayNameMode = DisplayNameMode;
        config.ThinkingLevel = ThinkingLevel;
        config.ReasoningMode = ReasoningMode;
        config.ReasoningSummary = ReasoningSummary;
        config.ServiceTier = ServiceTier;
        config.MaxInputTokens = MaxInputTokens;
        config.MaxOutputTokens = MaxOutputTokens;
        config.PricingMode = PricingMode;
        config.InputPricePerMillion = InputPricePerMillion;
        config.OutputPricePerMillion = OutputPricePerMillion;
        config.CachedInputPricePerMillion = CachedInputPricePerMillion;
        config.ModelCatalogMode = ModelCatalogMode;
    }
}

/// <summary>The settings a resolution produces and the fields it changed, or why it was refused.</summary>
public sealed class ModelResolutionOutcome
{
    public ModelSettings? Updated { get; }
    public IReadOnlyList<ModelResolutionChange> Changes { get; }

    /// <summary>A message for the user when the resolution was refused, else null.</summary>
    public string? Refusal { get; }

    public bool IsRefused => Refusal != null;

    private ModelResolutionOutcome(ModelSettings? updated, IReadOnlyList<ModelResolutionChange> changes, string? refusal)
    {
        Updated = updated;
        Changes = changes;
        Refusal = refusal;
    }

    public static ModelResolutionOutcome Success(ModelSettings updated, IReadOnlyList<ModelResolutionChange> changes)
        => new(updated, changes, null);

    public static ModelResolutionOutcome Refused(string message)
        => new(null, Array.Empty<ModelResolutionChange>(), message);
}

/// <summary>
/// Resolves a model row that needs attention, without persisting anything: switches it to a catalog
/// model of the same provider, keeping what the target supports the way the model form's edit mode does,
/// or keeps it as a custom model with its own limits and prices.
/// </summary>
public class ModelResolutionService
{
    public const string CustomPriceKeptNote = "Custom price kept — check it for the new model.";

    private const string DefaultLevel = "medium";

    private readonly ModelMetadataService _metadata;

    public ModelResolutionService(ModelMetadataService metadata)
    {
        _metadata = metadata;
    }

    /// <summary>Dispatches on <see cref="ModelResolutionRequest.Action"/>.</summary>
    public ModelResolutionOutcome Resolve(ModelSettings current, ModelResolutionRequest request)
    {
        if (string.Equals(request.Action, ModelResolutionActions.Switch, StringComparison.OrdinalIgnoreCase))
            return Switch(current, request.TargetModelId ?? string.Empty);

        if (string.Equals(request.Action, ModelResolutionActions.KeepCustom, StringComparison.OrdinalIgnoreCase))
            return KeepAsCustom(current, request);

        return ModelResolutionOutcome.Refused($"Unknown action \"{request.Action}\".");
    }

    public ModelResolutionOutcome Switch(ModelSettings current, string targetModelId)
    {
        var target = targetModelId?.Trim() ?? string.Empty;
        if (target.Length == 0 || !_metadata.IsWhitelisted(current.Provider, target))
            return ModelResolutionOutcome.Refused($"{target} is not a {current.Provider} model in the catalog.");

        var targetMeta = _metadata.GetMetadata(current.Provider, target);

        int? maxOutput = current.MaxOutputTokens;
        if (maxOutput.HasValue && targetMeta.MaxOutputTokens > 0 && maxOutput.Value > targetMeta.MaxOutputTokens)
            maxOutput = targetMeta.MaxOutputTokens;

        /* The target's own input maximum, as the model form clamps to it. */
        int? maxInput = current.MaxInputTokens;
        if (maxInput.HasValue && targetMeta.MaxInputTokens > 0 && maxInput.Value > targetMeta.MaxInputTokens)
            maxInput = targetMeta.MaxInputTokens;

        var updated = current with
        {
            ModelId = target,
            DisplayName = SwitchedDisplayName(current, target, targetMeta),
            ThinkingLevel = Retain(current.ThinkingLevel, targetMeta.SupportedThinkingLevels),
            ReasoningMode = Retain(current.ReasoningMode, targetMeta.SupportedReasoningModes),
            ReasoningSummary = Retain(current.ReasoningSummary, targetMeta.SupportedReasoningSummaries),
            MaxInputTokens = maxInput,
            MaxOutputTokens = maxOutput,
            ModelCatalogMode = ModelCatalogModes.Catalog
        };

        var changes = Diff(current, updated);
        if (PricingModes.Normalize(current.PricingMode) == PricingModes.Custom)
        {
            changes.Add(new ModelResolutionChange
            {
                Field = "Price",
                From = FormatPrices(current),
                To = FormatPrices(updated),
                Note = CustomPriceKeptNote
            });
        }

        return ModelResolutionOutcome.Success(updated, changes);
    }

    public ModelResolutionOutcome KeepAsCustom(ModelSettings current, ModelResolutionRequest request)
    {
        if (request.MaxInputTokens.HasValue && request.MaxInputTokens.Value <= 0)
            return ModelResolutionOutcome.Refused("Max input tokens must be a positive whole number.");

        if (request.MaxOutputTokens.HasValue && request.MaxOutputTokens.Value <= 0)
            return ModelResolutionOutcome.Refused("Max output tokens must be a positive whole number.");

        if (request.InputPricePerMillion < 0 || request.OutputPricePerMillion < 0 || request.CachedInputPricePerMillion < 0)
            return ModelResolutionOutcome.Refused("Prices cannot be negative.");

        var updated = current with
        {
            ModelCatalogMode = ModelCatalogModes.Custom,
            MaxInputTokens = request.MaxInputTokens ?? current.MaxInputTokens,
            MaxOutputTokens = request.MaxOutputTokens ?? current.MaxOutputTokens
        };

        if (request.InputPricePerMillion.HasValue && request.OutputPricePerMillion.HasValue)
        {
            updated = updated with
            {
                PricingMode = PricingModes.Custom,
                InputPricePerMillion = request.InputPricePerMillion,
                OutputPricePerMillion = request.OutputPricePerMillion,
                CachedInputPricePerMillion = request.CachedInputPricePerMillion
            };
        }

        return ModelResolutionOutcome.Success(updated, Diff(current, updated));
    }

    /// <summary>The current value when it is empty or the target supports it; else "medium", else the target's first value, else null.</summary>
    private static string? Retain(string? value, IReadOnlyList<string> supported)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        if (supported.Contains(value, StringComparer.OrdinalIgnoreCase))
            return value;

        if (supported.Contains(DefaultLevel, StringComparer.OrdinalIgnoreCase))
            return DefaultLevel;

        return supported.Count > 0 ? supported[0] : null;
    }

    private string? SwitchedDisplayName(ModelSettings current, string target, ModelMetadata targetMeta)
    {
        var targetName = string.IsNullOrEmpty(targetMeta.DisplayName) ? target : targetMeta.DisplayName;

        switch (DisplayNameModes.Normalize(current.DisplayNameMode))
        {
            case DisplayNameModes.ModelName:
                return targetName;
            case DisplayNameModes.ModelId:
                return target;
            case DisplayNameModes.Custom:
                return current.DisplayName;
        }

        /* A legacy row: replace a name that was derived from the old model, keep one the owner wrote. */
        if (string.IsNullOrWhiteSpace(current.DisplayName))
            return current.DisplayName;

        var name = current.DisplayName.Trim();
        if (string.Equals(name, current.ModelId, StringComparison.OrdinalIgnoreCase))
            return targetName;

        var oldCatalogName = _metadata.GetMetadata(current.Provider, current.ModelId).DisplayName;
        if (!string.IsNullOrEmpty(oldCatalogName) && string.Equals(name, oldCatalogName, StringComparison.OrdinalIgnoreCase))
            return targetName;

        var retiredName = _metadata.GetRetiredEntry(current.Provider, current.ModelId)?.DisplayName;
        if (!string.IsNullOrEmpty(retiredName) && string.Equals(name, retiredName, StringComparison.OrdinalIgnoreCase))
            return targetName;

        return current.DisplayName;
    }

    /// <summary>One entry per changed field, named as the model form labels it.</summary>
    private static List<ModelResolutionChange> Diff(ModelSettings before, ModelSettings after)
    {
        var changes = new List<ModelResolutionChange>();

        void Add(string field, string? from, string? to)
        {
            if (!string.Equals(from, to, StringComparison.Ordinal))
                changes.Add(new ModelResolutionChange { Field = field, From = from, To = to });
        }

        Add("Model", before.ModelId, after.ModelId);
        Add("Model type", CatalogModeLabel(before.ModelCatalogMode), CatalogModeLabel(after.ModelCatalogMode));
        Add("Display name", before.DisplayName, after.DisplayName);
        Add("Thinking level", before.ThinkingLevel, after.ThinkingLevel);
        Add("Reasoning mode", before.ReasoningMode, after.ReasoningMode);
        Add("Reasoning summary", before.ReasoningSummary, after.ReasoningSummary);
        Add("Service tier", before.ServiceTier, after.ServiceTier);
        Add("Max input tokens", FormatInt(before.MaxInputTokens), FormatInt(after.MaxInputTokens));
        Add("Max output tokens", FormatInt(before.MaxOutputTokens), FormatInt(after.MaxOutputTokens));
        Add("Pricing", PricingModeLabel(before.PricingMode), PricingModeLabel(after.PricingMode));
        Add("Input price per million", FormatPrice(before.InputPricePerMillion), FormatPrice(after.InputPricePerMillion));
        Add("Output price per million", FormatPrice(before.OutputPricePerMillion), FormatPrice(after.OutputPricePerMillion));
        Add("Cached input price per million", FormatPrice(before.CachedInputPricePerMillion), FormatPrice(after.CachedInputPricePerMillion));

        return changes;
    }

    private static string CatalogModeLabel(string? mode) =>
        ModelCatalogModes.Normalize(mode) == ModelCatalogModes.Custom ? "Custom model" : "Catalog model";

    private static string PricingModeLabel(string? mode) =>
        PricingModes.Normalize(mode) == PricingModes.Custom ? "Custom" : "Default";

    private static string? FormatInt(int? value) =>
        value?.ToString(CultureInfo.InvariantCulture);

    private static string? FormatPrice(decimal? value) =>
        value?.ToString("0.######", CultureInfo.InvariantCulture);

    private static string FormatPrices(ModelSettings settings) =>
        $"in {FormatPrice(settings.InputPricePerMillion) ?? "—"}, out {FormatPrice(settings.OutputPricePerMillion) ?? "—"}, cached {FormatPrice(settings.CachedInputPricePerMillion) ?? "—"} per million";
}
