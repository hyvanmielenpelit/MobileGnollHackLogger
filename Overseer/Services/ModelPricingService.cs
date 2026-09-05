namespace Overseer.Services;

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using MobileGnollHackLogger.Data;

public enum ModelPricingSource { Catalog, Custom }

public record ModelPricing(
    decimal InputPerMillion,
    decimal OutputPerMillion,
    decimal? CachedInputPerMillion = null,
    decimal? CacheWritePerMillion = null,
    ModelPricingSource Source = ModelPricingSource.Catalog,
    string Currency = "USD",
    string? AsOf = null);

public record BenchmarkRunPricing(
    ModelPricing? Candidate = null,
    ModelPricing? Assessor = null,
    ModelPricing? ClaimVerifier = null,
    ModelPricing? SecondOpinion = null,
    bool IsSnapshot = false);

public class ModelPricingService
{
    private readonly ModelMetadataService _metadata;
    private readonly ApplicationDbContext _db;
    private readonly Dictionary<long, ModelPricing?> _configCache = new();

    public ModelPricingService(ModelMetadataService metadata, ApplicationDbContext db)
    {
        _metadata = metadata;
        _db = db;
    }

    // Resolution order, highest first:
    //   1. the configuration's own Custom override
    //   2. the provider catalog default for its model id
    //   3. null — "not published", never zero
    public virtual ModelPricing? Resolve(SystemAiApiConfiguration config)
    {
        if (config == null) return null;

        if (string.Equals(config.PricingMode, "custom", StringComparison.OrdinalIgnoreCase) &&
            config.InputPricePerMillion.HasValue && config.OutputPricePerMillion.HasValue)
        {
            return new ModelPricing(
                config.InputPricePerMillion.Value,
                config.OutputPricePerMillion.Value,
                config.CachedInputPricePerMillion,
                CacheWritePerMillion: null,
                Source: ModelPricingSource.Custom,
                Currency: "USD",
                AsOf: null);
        }

        return ResolveDefault(config.Provider, config.ModelId);
    }

    public virtual ModelPricing? Resolve(UserAiModel model)
    {
        if (model == null) return null;

        if (string.Equals(model.PricingMode, "custom", StringComparison.OrdinalIgnoreCase) &&
            model.InputPricePerMillion.HasValue && model.OutputPricePerMillion.HasValue)
        {
            return new ModelPricing(
                model.InputPricePerMillion.Value,
                model.OutputPricePerMillion.Value,
                model.CachedInputPricePerMillion,
                CacheWritePerMillion: null,
                Source: ModelPricingSource.Custom,
                Currency: "USD",
                AsOf: null);
        }

        return ResolveDefault(model.Provider, model.ModelId);
    }

    public virtual ModelPricing? ResolveDefault(string? provider, string? modelId)
    {
        if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(modelId))
            return null;

        var meta = _metadata.GetMetadata(provider, modelId);
        if (meta?.DefaultPricing == null)
            return null;

        var dp = meta.DefaultPricing;
        return new ModelPricing(
            dp.InputPerMillion,
            dp.OutputPerMillion,
            dp.CachedInputPerMillion,
            dp.CacheWritePerMillion,
            ModelPricingSource.Catalog,
            "USD", // Overseer prices exclusively in USD; the catalog carries no currency field.
            dp.AsOf);
    }

    public virtual async Task<ModelPricing?> ResolveForConfigurationAsync(
        long? configurationId, string? providerFallback, string? modelIdFallback)
    {
        if (configurationId.HasValue)
        {
            if (_configCache.TryGetValue(configurationId.Value, out var cachedPricing))
            {
                return cachedPricing;
            }

            var config = await _db.SystemAiApiConfigurations.FindAsync(configurationId.Value);
            if (config != null)
            {
                var resolved = Resolve(config);
                _configCache[configurationId.Value] = resolved;
                return resolved;
            }
        }

        return ResolveDefault(providerFallback, modelIdFallback);
    }

    public virtual async Task<BenchmarkRunPricing> ResolveForRunAsync(BenchmarkRun run)
    {
        if (run == null) return new BenchmarkRunPricing();

        if (!string.IsNullOrWhiteSpace(run.PricingSnapshotJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(run.PricingSnapshotJson);
                var root = doc.RootElement;
                ModelPricing? ParseRole(string roleName)
                {
                    if (root.TryGetProperty(roleName, out var roleElem) && roleElem.ValueKind == JsonValueKind.Object)
                    {
                        decimal inPrice = roleElem.TryGetProperty("inputPerMillion", out var pIn) ? pIn.GetDecimal() : 0m;
                        decimal outPrice = roleElem.TryGetProperty("outputPerMillion", out var pOut) ? pOut.GetDecimal() : 0m;
                        decimal? cachedIn = roleElem.TryGetProperty("cachedInputPerMillion", out var pCIn) && pCIn.ValueKind == JsonValueKind.Number ? pCIn.GetDecimal() : null;
                        decimal? cacheWrite = roleElem.TryGetProperty("cacheWritePerMillion", out var pCW) && pCW.ValueKind == JsonValueKind.Number ? pCW.GetDecimal() : null;
                        string sourceStr = roleElem.TryGetProperty("source", out var pSrc) ? pSrc.GetString() ?? "catalog" : "catalog";
                        var source = string.Equals(sourceStr, "custom", StringComparison.OrdinalIgnoreCase) ? ModelPricingSource.Custom : ModelPricingSource.Catalog;
                        string currency = roleElem.TryGetProperty("currency", out var pCur) ? pCur.GetString() ?? "USD" : "USD";
                        string? asOf = roleElem.TryGetProperty("asOf", out var pAsOf) ? pAsOf.GetString() : null;

                        return new ModelPricing(inPrice, outPrice, cachedIn, cacheWrite, source, currency, asOf);
                    }
                    return null;
                }

                var candidate = ParseRole("candidate");
                var assessor = ParseRole("assessor");
                var claimVerifier = ParseRole("claimVerifier");
                var secondOpinion = ParseRole("secondOpinion");

                return new BenchmarkRunPricing(candidate, assessor, claimVerifier, secondOpinion, IsSnapshot: true);
            }
            catch
            {
                // Fall back to live resolution if snapshot json was corrupt
            }
        }

        var liveCandidate = await ResolveForConfigurationAsync(run.TestedModelConfigurationId, run.TestedModelProviderUsed, run.TestedModelIdUsed);
        var liveAssessor = await ResolveForConfigurationAsync(run.AssessorModelConfigurationId, run.AssessorModelProviderUsed, run.AssessorModelIdUsed);
        var liveVerifier = await ResolveForConfigurationAsync(run.ClaimVerifierModelConfigurationId, run.ClaimVerifierProviderUsed, run.ClaimVerifierModelIdUsed);
        var liveSecondOpinion = await ResolveForConfigurationAsync(run.SecondOpinionAssessorModelConfigurationId, run.SecondOpinionAssessorModelProviderUsed, run.SecondOpinionAssessorModelIdUsed);

        return new BenchmarkRunPricing(liveCandidate, liveAssessor, liveVerifier, liveSecondOpinion, IsSnapshot: false);
    }

    public static decimal ComputeCost(
        ModelPricing pricing,
        long inputTokens, long outputTokens,
        long cacheReadTokens = 0, long cacheCreationTokens = 0)
    {
        if (pricing == null) return 0m;

        // inputTokens is uncached input tokens
        decimal cost = (inputTokens / 1_000_000m) * pricing.InputPerMillion;
        cost += (outputTokens / 1_000_000m) * pricing.OutputPerMillion;

        if (cacheReadTokens > 0)
        {
            decimal cacheReadRate = pricing.CachedInputPerMillion ?? pricing.InputPerMillion;
            cost += (cacheReadTokens / 1_000_000m) * cacheReadRate;
        }

        if (cacheCreationTokens > 0 && pricing.CacheWritePerMillion.HasValue)
        {
            cost += (cacheCreationTokens / 1_000_000m) * pricing.CacheWritePerMillion.Value;
        }

        return cost;
    }
}

