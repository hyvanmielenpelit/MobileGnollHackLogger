using System;
using Overseer.Models;

namespace Overseer.Services;

/// <summary>
/// Classifies a user model or system configuration against the model catalog: available, custom,
/// on a custom endpoint, retired, or not in the catalog.
/// </summary>
public class ModelAvailabilityService
{
    private readonly ModelMetadataService _metadata;

    public ModelAvailabilityService(ModelMetadataService metadata)
    {
        _metadata = metadata;
    }

    /// <summary>
    /// The first status that applies: a custom endpoint, a custom catalog mode, a model the catalog
    /// describes, a retired model, otherwise not in the catalog. <see cref="ModelAvailabilityDto.SuggestedCustom"/>
    /// is filled only for a retired model; the caller supplies the row's own values otherwise.
    /// </summary>
    public ModelAvailabilityDto Evaluate(string provider, string modelId, string? modelCatalogMode, string? baseUrl)
    {
        /* A gateway or Azure deployment name is legitimately uncatalogued. */
        if (!string.IsNullOrWhiteSpace(baseUrl))
            return new ModelAvailabilityDto { Kind = ModelAvailabilityStatus.CustomEndpoint };

        if (ModelCatalogModes.Normalize(modelCatalogMode) == ModelCatalogModes.Custom)
            return new ModelAvailabilityDto { Kind = ModelAvailabilityStatus.Custom };

        if (_metadata.IsDescribedByCatalog(provider, modelId))
            return new ModelAvailabilityDto { Kind = ModelAvailabilityStatus.Available };

        var retired = _metadata.GetRetiredEntry(provider, modelId);
        if (retired != null)
        {
            return new ModelAvailabilityDto
            {
                Kind = ModelAvailabilityStatus.Retired,
                RetiredOn = retired.RetiredOn,
                Note = retired.Note,
                CatalogDisplayName = retired.DisplayName,
                Replacement = BuildReplacement(provider, retired.Replacement),
                SuggestedCustom = BuildSuggestedCustom(retired.LastKnown)
            };
        }

        return new ModelAvailabilityDto { Kind = ModelAvailabilityStatus.NotInCatalog };
    }

    /// <summary>"catalog" when the catalog describes the model ID, else "custom".</summary>
    public string DeriveCatalogMode(string provider, string modelId)
    {
        return _metadata.IsDescribedByCatalog(provider, modelId)
            ? ModelCatalogModes.Catalog
            : ModelCatalogModes.Custom;
    }

    private ModelReplacementDto? BuildReplacement(string provider, string? replacementId)
    {
        if (string.IsNullOrWhiteSpace(replacementId) || !_metadata.IsWhitelisted(provider, replacementId))
            return null;

        var meta = _metadata.GetMetadata(provider, replacementId);
        return new ModelReplacementDto
        {
            ModelId = replacementId,
            DisplayName = string.IsNullOrEmpty(meta.DisplayName) ? replacementId : meta.DisplayName
        };
    }

    private static SuggestedCustomDto? BuildSuggestedCustom(RetiredModelLastKnown? lastKnown)
    {
        if (lastKnown == null)
            return null;

        int? maxOutput = lastKnown.MaxOutputTokens > 0 ? lastKnown.MaxOutputTokens : null;
        int? maxInput = lastKnown.ContextWindowSize > 0
            ? Math.Max(0, lastKnown.ContextWindowSize - (maxOutput ?? 0))
            : null;

        return new SuggestedCustomDto
        {
            MaxInputTokens = maxInput > 0 ? maxInput : null,
            MaxOutputTokens = maxOutput,
            InputPricePerMillion = lastKnown.InputPerMillion,
            OutputPricePerMillion = lastKnown.OutputPerMillion,
            CachedInputPricePerMillion = lastKnown.CachedInputPerMillion
        };
    }
}
