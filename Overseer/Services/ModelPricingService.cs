namespace Overseer.Services;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MobileGnollHackLogger.Data;
using Overseer.Services.Providers;

public enum ModelPricingSource { Catalog, Custom }

/// <summary>
/// The second rate card a provider applies to a single request whose prompt exceeds
/// <see cref="ThresholdInputTokens"/>. Absolute rates rather than a multiplier: providers surcharge
/// input and output by different factors (OpenAI 2x/1.5x, Google Pro 2x/1.5x).
/// </summary>
public record LongContextPricing(
    int ThresholdInputTokens,
    decimal InputPerMillion,
    decimal OutputPerMillion,
    decimal? CachedInputPerMillion = null,
    decimal? CacheWritePerMillion = null);

/// <summary>
/// A price change the provider has already announced for a future date. The base card is always the
/// rate in force today; this is the card that takes over on <see cref="EffectiveFrom"/>.
/// </summary>
public record ScheduledPricingChange(
    DateOnly EffectiveFrom,
    decimal InputPerMillion,
    decimal OutputPerMillion,
    decimal? CachedInputPerMillion = null,
    decimal? CacheWritePerMillion = null,
    string? Note = null);

public record ModelPricing(
    decimal InputPerMillion,
    decimal OutputPerMillion,
    decimal? CachedInputPerMillion = null,
    decimal? CacheWritePerMillion = null,
    ModelPricingSource Source = ModelPricingSource.Catalog,
    string? AsOf = null,
    LongContextPricing? LongContext = null,
    IReadOnlyDictionary<string, decimal>? ServiceTierMultipliers = null,
    ScheduledPricingChange? ScheduledChange = null,
    bool ScheduleElapsed = false);

public record BenchmarkRunPricing(
    ModelPricing? Candidate = null,
    ModelPricing? Assessor = null,
    ModelPricing? ClaimVerifier = null,
    ModelPricing? SecondOpinion = null,
    bool IsSnapshot = false);

/// <summary>
/// One benchmark run's cost, split across the five roles that spend on it.
///
/// <para><see cref="Grading"/> is <see cref="Assessor"/>, <see cref="SecondOpinion"/>,
/// <see cref="ClaimVerifier"/> and <see cref="Synthesis"/> together; <see cref="Total"/> is those
/// four plus <see cref="Candidate"/>. Both subtotals are derived where the roles are costed, so a
/// report and a screen reading the same run cannot disagree about what "grading" includes.</para>
///
/// <para><see cref="Incomplete"/> says a role that spent tokens has no resolved price. The per-role
/// figures still stand and are worth showing, but <see cref="Total"/> is 0 in that case: a sum that
/// silently omits a role reads as a run total and is worse than no figure at all.</para>
///
/// <para><see cref="Source"/> is <c>"custom"</c>, <c>"catalog"</c>, <c>"mixed"</c>, or empty when no
/// role resolved to a price card at all.</para>
/// </summary>
public readonly record struct BenchmarkRoleCosts(
    decimal Candidate, decimal Assessor, decimal SecondOpinion, decimal ClaimVerifier,
    decimal Synthesis, decimal Grading, decimal Total, bool Incomplete, string Source);

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
            // Custom overrides are flat-rate by design: no long-context card, no service-tier multiplier, no
            // schedule. Supporting all three would need a threshold, four rates, a multiplier map and a date on
            // both override entities; nobody has one. An operator who needs any of them sets the rate they want.
            // The model form says so to the operator — see ai-model-form's pricing help text.
            return new ModelPricing(
                config.InputPricePerMillion.Value,
                config.OutputPricePerMillion.Value,
                config.CachedInputPricePerMillion,
                CacheWritePerMillion: null,
                Source: ModelPricingSource.Custom,
                AsOf: null,
                LongContext: null,
                ServiceTierMultipliers: null,
                ScheduledChange: null);
        }

        return ResolveDefault(config.Provider, config.ModelId);
    }

    public virtual ModelPricing? Resolve(UserAiModel model)
    {
        if (model == null) return null;

        if (string.Equals(model.PricingMode, "custom", StringComparison.OrdinalIgnoreCase) &&
            model.InputPricePerMillion.HasValue && model.OutputPricePerMillion.HasValue)
        {
            // Custom overrides are flat-rate by design: no long-context card, no service-tier multiplier, no
            // schedule. Supporting all three would need a threshold, four rates, a multiplier map and a date on
            // both override entities; nobody has one. An operator who needs any of them sets the rate they want.
            // The model form says so to the operator — see ai-model-form's pricing help text.
            return new ModelPricing(
                model.InputPricePerMillion.Value,
                model.OutputPricePerMillion.Value,
                model.CachedInputPricePerMillion,
                CacheWritePerMillion: null,
                Source: ModelPricingSource.Custom,
                AsOf: null,
                LongContext: null,
                ServiceTierMultipliers: null,
                ScheduledChange: null);
        }

        return ResolveDefault(model.Provider, model.ModelId);
    }

    /// <param name="today">UTC today by default; injected only by tests, which must be able to stand
    /// either side of a scheduled change without waiting for the calendar.</param>
    public virtual ModelPricing? ResolveDefault(string? provider, string? modelId, DateOnly? today = null)
    {
        if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(modelId))
            return null;

        var meta = _metadata.GetMetadata(provider, modelId);
        if (meta?.DefaultPricing == null)
            return null;

        var dp = meta.DefaultPricing;
        var scheduled = ParseScheduled(dp.ScheduledChange);   // null when absent or the date is unparseable
        var now = today ?? DateOnly.FromDateTime(DateTime.UtcNow);
        bool elapsed = scheduled != null && now >= scheduled.EffectiveFrom;

        // The base card is always the rate in force today, so an elapsed schedule replaces it outright.
        // ScheduleElapsed is not an error: the price is correct, but the catalog entry now carries a
        // change that has already happened and should be folded into the base rates and re-verified. The
        // admin views surface it for exactly that reason.
        return new ModelPricing(
            elapsed ? scheduled!.InputPerMillion  : dp.InputPerMillion,
            elapsed ? scheduled!.OutputPerMillion : dp.OutputPerMillion,
            elapsed ? (scheduled!.CachedInputPerMillion ?? dp.CachedInputPerMillion) : dp.CachedInputPerMillion,
            elapsed ? (scheduled!.CacheWritePerMillion  ?? dp.CacheWritePerMillion)  : dp.CacheWritePerMillion,
            ModelPricingSource.Catalog,
            dp.AsOf,
            LongContext: ToLongContext(dp.LongContext),
            ServiceTierMultipliers: dp.ServiceTierMultipliers,
            ScheduledChange: scheduled,
            ScheduleElapsed: elapsed);
    }

    /// <summary>
    /// Converts a catalog long-context block to its runtime record, or null when the entry publishes a
    /// single flat rate. Never inferred from a sibling model: a long-context tier exists only where the
    /// provider's own page states one.
    /// </summary>
    internal static LongContextPricing? ToLongContext(ModelCatalogLongContextPricing? source)
    {
        if (source == null || source.ThresholdInputTokens <= 0) return null;

        return new LongContextPricing(
            source.ThresholdInputTokens,
            source.InputPerMillion,
            source.OutputPerMillion,
            source.CachedInputPerMillion,
            source.CacheWritePerMillion);
    }

    /// <summary>
    /// Converts a catalog scheduled-change block to its runtime record. An unparseable or missing
    /// <c>effectiveFrom</c> yields null — the base card, unchanged. A malformed date must never silently
    /// change a price.
    /// </summary>
    internal static ScheduledPricingChange? ParseScheduled(ModelCatalogScheduledPricing? source)
    {
        if (source == null || string.IsNullOrWhiteSpace(source.EffectiveFrom)) return null;

        if (!DateOnly.TryParseExact(
                source.EffectiveFrom.Trim(), "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var effectiveFrom))
        {
            return null;
        }

        return new ScheduledPricingChange(
            effectiveFrom,
            source.InputPerMillion,
            source.OutputPerMillion,
            source.CachedInputPerMillion,
            source.CacheWritePerMillion,
            source.Note);
    }

    /// <summary>
    /// The multiplier for the tier the provider actually served. Reads ActualServiceTierUsed first and
    /// only falls back to the requested tier: the two use different value spaces (OpenAI requests
    /// "auto"/"fast" and serves "default"/"priority"), and a priority request that was served default
    /// must be billed as default. Unknown or unlisted tiers — including "default" and "standard" — are
    /// 1.0, never zero.
    /// </summary>
    public static decimal ResolveServiceTierMultiplier(
        ModelPricing pricing, string? actualServiceTier, string? requestedServiceTier)
    {
        if (pricing?.ServiceTierMultipliers == null) return 1.0m;

        // Once the provider has reported a served tier, that tier is what was billed — listed or not.
        // Falling through to the requested tier here would be exactly the R3 defect: OpenAI requests
        // "auto"/"fast" and serves "default"/"priority", so a priority request served as default is
        // reported as "default", which no catalog lists, and the fall-through would bill it at 2x.
        var servedKey = ProviderHelper.NormalizeServiceTier(actualServiceTier);
        if (servedKey != null)
        {
            return pricing.ServiceTierMultipliers.TryGetValue(servedKey, out var served) ? served : 1.0m;
        }

        // Only when the provider reported no tier at all does the requested one stand in for it.
        var requestedKey = ProviderHelper.NormalizeServiceTier(requestedServiceTier);
        if (requestedKey != null && pricing.ServiceTierMultipliers.TryGetValue(requestedKey, out var requested))
        {
            return requested;
        }

        return 1.0m;
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
                        string? asOf = roleElem.TryGetProperty("asOf", out var pAsOf) ? pAsOf.GetString() : null;

                        // The snapshot stores the *resolved* card, so a run started before a scheduled
                        // change recosts at the rates that applied when it ran, and no schedule logic runs
                        // on the replay path at all. A snapshot without these keys parses to flat pricing,
                        // which every run recorded before tiered pricing existed is.
                        LongContextPricing? longContext = null;
                        if (roleElem.TryGetProperty("longContext", out var pLc) && pLc.ValueKind == JsonValueKind.Object)
                        {
                            int threshold = pLc.TryGetProperty("thresholdInputTokens", out var pTh) && pTh.ValueKind == JsonValueKind.Number
                                ? pTh.GetInt32() : 0;
                            if (threshold > 0)
                            {
                                longContext = new LongContextPricing(
                                    threshold,
                                    pLc.TryGetProperty("inputPerMillion", out var lIn) ? lIn.GetDecimal() : 0m,
                                    pLc.TryGetProperty("outputPerMillion", out var lOut) ? lOut.GetDecimal() : 0m,
                                    pLc.TryGetProperty("cachedInputPerMillion", out var lCIn) && lCIn.ValueKind == JsonValueKind.Number ? lCIn.GetDecimal() : null,
                                    pLc.TryGetProperty("cacheWritePerMillion", out var lCW) && lCW.ValueKind == JsonValueKind.Number ? lCW.GetDecimal() : null);
                            }
                        }

                        Dictionary<string, decimal>? tiers = null;
                        if (roleElem.TryGetProperty("serviceTierMultipliers", out var pTiers) && pTiers.ValueKind == JsonValueKind.Object)
                        {
                            tiers = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
                            foreach (var tierProp in pTiers.EnumerateObject())
                            {
                                if (tierProp.Value.ValueKind == JsonValueKind.Number)
                                {
                                    tiers[tierProp.Name] = tierProp.Value.GetDecimal();
                                }
                            }
                            if (tiers.Count == 0) tiers = null;
                        }

                        return new ModelPricing(
                            inPrice, outPrice, cachedIn, cacheWrite, source, asOf,
                            LongContext: longContext,
                            ServiceTierMultipliers: tiers);
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

    /// <summary>
    /// Flat-rate costing. Ignores any long-context card and any service-tier multiplier — see the
    /// per-call overload. Correct for roles costed from aggregate totals only (assessor, second
    /// opinion, claim verifier).
    /// </summary>
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

    /// <summary>
    /// Costs one turn from its individual model calls, so a long-context card is applied per request,
    /// which is how every provider that publishes one bills it, and then scaled by the served service
    /// tier, which is a property of the turn. The schedule has already been applied: the ModelPricing
    /// handed in is the card in force today.
    ///
    /// Prefer this over the aggregate overload wherever per-call usage survives. Passing a turn's summed
    /// tokens to the aggregate overload is correct only for a flat-rate model: with a long-context card
    /// it would surcharge the whole turn whenever the sum crossed the threshold, and an agentic turn's
    /// sum crosses it routinely while no single request does. Run 13's Q18 reported 847,245 input tokens
    /// across ~30 calls against a 272,000 threshold — not one of them was a long-context request.
    ///
    /// Each call is split into the four disjoint buckets the four-argument overload bills: base input,
    /// output, cache reads and cache writes. Base input is <see cref="TokenUsageReport.BillableUncachedInputTokens"/>,
    /// not <see cref="TokenUsageReport.UncachedInputTokens"/>, because the latter still contains the
    /// cache-creation tokens that the cache-write rate already covers.
    /// </summary>
    public static decimal ComputeCost(
        ModelPricing pricing,
        IReadOnlyList<TokenUsageReport> calls,
        string? actualServiceTier = null,
        string? requestedServiceTier = null)
    {
        if (pricing == null || calls == null || calls.Count == 0) return 0m;

        decimal tierMultiplier =
            ResolveServiceTierMultiplier(pricing, actualServiceTier, requestedServiceTier);

        decimal total = 0m;
        foreach (var call in calls)
        {
            bool longContext = pricing.LongContext != null
                && call.TotalPromptTokens > pricing.LongContext.ThresholdInputTokens;

            // The surcharge applies to the full request — input, cached input, cache writes and output
            // alike — not only to the tokens above the threshold. A null CacheWritePerMillion on the
            // long-context card falls back to the base rate, which is what a provider page that does not
            // mention cache writes is saying.
            var card = longContext
                ? pricing with
                  {
                      InputPerMillion = pricing.LongContext!.InputPerMillion,
                      OutputPerMillion = pricing.LongContext.OutputPerMillion,
                      CachedInputPerMillion =
                          pricing.LongContext.CachedInputPerMillion ?? pricing.CachedInputPerMillion,
                      CacheWritePerMillion =
                          pricing.LongContext.CacheWritePerMillion ?? pricing.CacheWritePerMillion
                  }
                : pricing;

            total += ComputeCost(
                card, call.BillableUncachedInputTokens, call.OutputTokens,
                call.CacheReadTokens, call.CacheCreationTokens);
        }

        // Multiplicative composition is what the providers document: Anthropic states fast-mode pricing
        // "stacks with other pricing modifiers", and OpenAI's gpt-5.4 page applies the long-context
        // surcharge "for standard, batch, and flex" — i.e. within whichever tier is in force.
        return total * tierMultiplier;
    }

    /// <summary>
    /// The portion of a turn's tokens that came from model calls large enough to bill at the long-context
    /// rate. Not additional tokens — a subset of the same ones, which is what makes the split at costing
    /// time a partition rather than a double count. All zero for a flat-rate model, so a caller may store
    /// the result unconditionally.
    /// </summary>
    public static LongContextTokenBuckets ComputeLongContextBuckets(
        ModelPricing? pricing, IReadOnlyList<TokenUsageReport>? calls)
    {
        if (pricing?.LongContext == null || calls == null || calls.Count == 0)
        {
            return default;
        }

        int threshold = pricing.LongContext.ThresholdInputTokens;
        int input = 0, output = 0, cacheRead = 0, cacheCreation = 0, callCount = 0;
        foreach (var call in calls)
        {
            if (call.TotalPromptTokens <= threshold) continue;
            callCount++;
            input += call.TotalPromptTokens;
            output += call.OutputTokens;
            cacheRead += call.CacheReadTokens;
            cacheCreation += call.CacheCreationTokens;
        }

        return new LongContextTokenBuckets(input, output, cacheRead, cacheCreation, callCount);
    }

    /// <summary>
    /// Costs a benchmark role from stored run totals, charging the long-context portion at its own card and
    /// the remainder at the base card, then scaling by the served service tier.
    ///
    /// <paramref name="totalPromptTokens"/> and <paramref name="longContextPromptTokens"/> are <b>total</b>
    /// prompt tokens including both cache reads and cache writes — the shape BenchmarkRun stores — not the
    /// uncached figure the four-argument overload takes. The long-context figures are a subset of the
    /// totals, so subtracting them leaves the standard portion.
    ///
    /// Each portion is then partitioned into the four disjoint buckets the four-argument overload bills:
    /// both the cache reads and the cache writes come off the prompt total, leaving only the tokens billed
    /// at the base input rate, because cache reads and cache writes each carry their own rate.
    ///
    /// With no long-context tokens recorded this is exactly the flat-rate result, which is what every run
    /// predating tiered pricing must still produce.
    /// </summary>
    public static decimal ComputeCostFromTotals(
        ModelPricing pricing,
        long totalPromptTokens, long totalOutputTokens,
        long cacheReadTokens, long cacheCreationTokens,
        long longContextPromptTokens = 0, long longContextOutputTokens = 0,
        long longContextCacheReadTokens = 0, long longContextCacheCreationTokens = 0,
        string? actualServiceTier = null, string? requestedServiceTier = null)
    {
        if (pricing == null) return 0m;

        decimal tierMultiplier =
            ResolveServiceTierMultiplier(pricing, actualServiceTier, requestedServiceTier);

        long lcPrompt = Math.Clamp(longContextPromptTokens, 0, totalPromptTokens);
        long lcCacheRead = Math.Clamp(longContextCacheReadTokens, 0, cacheReadTokens);
        long lcOutput = Math.Clamp(longContextOutputTokens, 0, totalOutputTokens);
        long lcCacheCreation = Math.Clamp(longContextCacheCreationTokens, 0, cacheCreationTokens);

        long stdPrompt = totalPromptTokens - lcPrompt;
        long stdCacheRead = cacheReadTokens - lcCacheRead;
        long stdOutput = totalOutputTokens - lcOutput;
        long stdCacheCreation = cacheCreationTokens - lcCacheCreation;

        decimal cost = ComputeCost(
            pricing,
            Math.Max(0, stdPrompt - stdCacheRead - stdCacheCreation), stdOutput, stdCacheRead, stdCacheCreation);

        if (lcPrompt > 0 && pricing.LongContext != null)
        {
            var card = pricing with
            {
                InputPerMillion = pricing.LongContext.InputPerMillion,
                OutputPerMillion = pricing.LongContext.OutputPerMillion,
                CachedInputPerMillion =
                    pricing.LongContext.CachedInputPerMillion ?? pricing.CachedInputPerMillion,
                CacheWritePerMillion =
                    pricing.LongContext.CacheWritePerMillion ?? pricing.CacheWritePerMillion
            };

            cost += ComputeCost(
                card,
                Math.Max(0, lcPrompt - lcCacheRead - lcCacheCreation), lcOutput, lcCacheRead, lcCacheCreation);
        }

        return cost * tierMultiplier;
    }

    /// <summary>
    /// Whether a role spent anything billable. Cache reads and cache writes count: a role whose
    /// prompt was served entirely from cache reports no uncached input tokens and still costs money.
    /// </summary>
    public static bool RoleHasTokens(
        long inputTokens, long outputTokens, long cacheReadTokens = 0, long cacheCreationTokens = 0)
        => inputTokens > 0 || outputTokens > 0 || cacheReadTokens > 0 || cacheCreationTokens > 0;

    /// <summary>
    /// The one costing of a benchmark run: five roles, their grading subtotal and their total, from
    /// the run's stored totals and the price cards resolved for it. Every surface that reports a run's
    /// cost — the run detail, the history list, the series estimate, the group analysis and the
    /// report — calls this, so none of them can hold a second copy of the arithmetic.
    ///
    /// <para>The candidate is costed from totals with its long-context subsets and the service tier the
    /// provider actually served, because its per-call evidence was bucketed at answer time and
    /// persisted. The grading roles are costed flat from aggregate totals, each with its own cache
    /// read and cache creation figures: no per-call usage is recorded for them, so neither a
    /// long-context card nor a served tier is knowable for them here.</para>
    ///
    /// <para>The final synthesis is priced on <see cref="BenchmarkRunPricing.Assessor"/>: it runs on the
    /// assessor's configuration, which is why <see cref="BenchmarkRunPricing"/> carries no synthesis
    /// member. It is a peer of the per-question assessments, not a component of them, and
    /// <see cref="BenchmarkRoleCosts.Assessor"/> excludes it.</para>
    /// </summary>
    /// <param name="run">The run whose stored totals are costed.</param>
    /// <param name="pricing">The price cards resolved for the run's roles.</param>
    /// <param name="servedServiceTier">The tier the provider served for the candidate. Null resolves it
    /// from the run's answer rows, which a caller that did not load them must therefore pass itself.</param>
    public static BenchmarkRoleCosts ComputeRunRoleCosts(
        BenchmarkRun run, BenchmarkRunPricing pricing, string? servedServiceTier = null)
    {
        if (run == null || pricing == null)
        {
            return new BenchmarkRoleCosts(0m, 0m, 0m, 0m, 0m, 0m, 0m, true, string.Empty);
        }

        string? servedTier = servedServiceTier
            ?? Benchmarking.BenchmarkRunFinalizer.ResolveServedServiceTier(run.Answers);

        var candidateCard = pricing.Candidate;
        var assessorCard = pricing.Assessor;
        var secondOpinionCard = pricing.SecondOpinion;
        var verifierCard = pricing.ClaimVerifier;

        bool hasAssessor = RoleHasTokens(
            run.TotalAssessmentInputTokens, run.TotalAssessmentOutputTokens,
            run.TotalAssessmentCacheReadTokens, run.TotalAssessmentCacheCreationTokens);
        bool hasSecondOpinion = RoleHasTokens(
            run.TotalSecondOpinionInputTokens, run.TotalSecondOpinionOutputTokens,
            run.TotalSecondOpinionCacheReadTokens, run.TotalSecondOpinionCacheCreationTokens);
        bool hasVerifier = RoleHasTokens(
            run.TotalClaimVerificationInputTokens, run.TotalClaimVerificationOutputTokens,
            run.TotalClaimVerificationCacheReadTokens, run.TotalClaimVerificationCacheCreationTokens);
        bool hasSynthesis = RoleHasTokens(
            run.TotalSynthesisInputTokens, run.TotalSynthesisOutputTokens,
            run.TotalSynthesisCacheReadTokens, run.TotalSynthesisCacheCreationTokens);

        decimal candidate = candidateCard != null
            ? ComputeCostFromTotals(
                candidateCard,
                run.TotalInputTokens, run.TotalOutputTokens,
                run.TotalCacheReadTokens, run.TotalCacheCreationTokens,
                run.TotalLongContextInputTokens, run.TotalLongContextOutputTokens,
                run.TotalLongContextCacheReadTokens, run.TotalLongContextCacheCreationTokens,
                actualServiceTier: servedTier,
                requestedServiceTier: run.TestedModelServiceTierUsed)
            : 0m;

        // Every stored Total*InputTokens column is a *total* prompt figure that already contains the
        // cache reads and cache writes beside it — the shape ComputeCostFromTotals takes. With no
        // long-context subset and no served tier, which no grading role records, it is the flat rate
        // over the four disjoint buckets, and identical to what a run predating these columns cost.
        decimal assessor = hasAssessor && assessorCard != null
            ? ComputeCostFromTotals(
                assessorCard,
                run.TotalAssessmentInputTokens, run.TotalAssessmentOutputTokens,
                run.TotalAssessmentCacheReadTokens, run.TotalAssessmentCacheCreationTokens)
            : 0m;

        decimal secondOpinion = hasSecondOpinion && secondOpinionCard != null
            ? ComputeCostFromTotals(
                secondOpinionCard,
                run.TotalSecondOpinionInputTokens, run.TotalSecondOpinionOutputTokens,
                run.TotalSecondOpinionCacheReadTokens, run.TotalSecondOpinionCacheCreationTokens)
            : 0m;

        decimal claimVerifier = hasVerifier && verifierCard != null
            ? ComputeCostFromTotals(
                verifierCard,
                run.TotalClaimVerificationInputTokens, run.TotalClaimVerificationOutputTokens,
                run.TotalClaimVerificationCacheReadTokens, run.TotalClaimVerificationCacheCreationTokens)
            : 0m;

        decimal synthesis = hasSynthesis && assessorCard != null
            ? ComputeCostFromTotals(
                assessorCard,
                run.TotalSynthesisInputTokens, run.TotalSynthesisOutputTokens,
                run.TotalSynthesisCacheReadTokens, run.TotalSynthesisCacheCreationTokens)
            : 0m;

        decimal grading = assessor + secondOpinion + claimVerifier + synthesis;

        // The candidate's card is required whatever its token counts: a run whose model under test
        // cannot be priced has no total worth printing.
        bool incomplete = candidateCard == null
            || (hasAssessor && assessorCard == null)
            || (hasSecondOpinion && secondOpinionCard == null)
            || (hasVerifier && verifierCard == null)
            || (hasSynthesis && assessorCard == null);

        var sources = new List<ModelPricingSource>(4);
        if (candidateCard != null) sources.Add(candidateCard.Source);
        if ((hasAssessor || hasSynthesis) && assessorCard != null) sources.Add(assessorCard.Source);
        if (hasSecondOpinion && secondOpinionCard != null) sources.Add(secondOpinionCard.Source);
        if (hasVerifier && verifierCard != null) sources.Add(verifierCard.Source);

        string source = string.Empty;
        if (sources.Count > 0)
        {
            if (sources.All(s => s == ModelPricingSource.Custom)) source = "custom";
            else if (sources.All(s => s == ModelPricingSource.Catalog)) source = "catalog";
            else source = "mixed";
        }

        return new BenchmarkRoleCosts(
            candidate, assessor, secondOpinion, claimVerifier, synthesis, grading,
            incomplete ? 0m : candidate + grading,
            incomplete, source);
    }
}

/// <summary>
/// The subset of a turn's tokens billed at a model's long-context rate, plus how many calls produced it.
/// All zero for a flat-rate model.
/// </summary>
public readonly record struct LongContextTokenBuckets(
    int InputTokens,
    int OutputTokens,
    int CacheReadTokens,
    int CacheCreationTokens,
    int CallCount);
