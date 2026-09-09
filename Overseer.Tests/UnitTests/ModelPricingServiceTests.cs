namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Services;
using Overseer.Services.Providers;
using Xunit;

public class ModelPricingServiceTests
{
    private readonly ModelMetadataService _metadataService = new();

    private ApplicationDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public void ResolveDefault_ExactModelId_ReturnsCatalogPricingWithMetadata()
    {
        using var db = CreateInMemoryDb();
        var service = new ModelPricingService(_metadataService, db);

        var result = service.ResolveDefault("OpenAI", "gpt-5.6-sol");

        Assert.NotNull(result);
        Assert.Equal(4.00m, result.InputPerMillion);
        Assert.Equal(20.00m, result.OutputPerMillion);
        Assert.Equal(0.40m, result.CachedInputPerMillion);
        Assert.Equal(5.00m, result.CacheWritePerMillion);
        Assert.Equal(ModelPricingSource.Catalog, result.Source);
        Assert.Equal("2026-09-06", result.AsOf);
    }

    [Fact]
    public void ResolveDefault_VersionedSuffix_ResolvesCatalogPricing()
    {
        using var db = CreateInMemoryDb();
        var service = new ModelPricingService(_metadataService, db);

        var result = service.ResolveDefault("Google", "gemini-3.7-flash-001");

        Assert.NotNull(result);
        Assert.Equal(0.75m, result.InputPerMillion);
        Assert.Equal(3.75m, result.OutputPerMillion);
        Assert.Equal(ModelPricingSource.Catalog, result.Source);
    }

    [Fact]
    public void ResolveDefault_UnknownModel_ReturnsNull()
    {
        using var db = CreateInMemoryDb();
        var service = new ModelPricingService(_metadataService, db);

        var result = service.ResolveDefault("OpenAI", "unknown-future-model");

        Assert.Null(result);
    }

    [Fact]
    public void Resolve_SystemConfig_CustomOverrideWinsOverCatalog()
    {
        using var db = CreateInMemoryDb();
        var service = new ModelPricingService(_metadataService, db);

        var config = new SystemAiApiConfiguration
        {
            Provider = "OpenAI",
            ModelId = "gpt-5.6",
            PricingMode = "custom",
            InputPricePerMillion = 5.0m,
            OutputPricePerMillion = 15.0m,
            CachedInputPricePerMillion = 1.0m
        };

        var result = service.Resolve(config);

        Assert.NotNull(result);
        Assert.Equal(ModelPricingSource.Custom, result.Source);
        Assert.Equal(5.0m, result.InputPerMillion);
        Assert.Equal(15.0m, result.OutputPerMillion);
        Assert.Equal(1.0m, result.CachedInputPerMillion);
    }

    [Fact]
    public void Resolve_SystemConfig_HalfFilledCustom_FallsBackToCatalog()
    {
        using var db = CreateInMemoryDb();
        var service = new ModelPricingService(_metadataService, db);

        var config = new SystemAiApiConfiguration
        {
            Provider = "OpenAI",
            ModelId = "gpt-5.6-sol",
            PricingMode = "custom",
            InputPricePerMillion = 5.0m,
            OutputPricePerMillion = null
        };

        var result = service.Resolve(config);

        Assert.NotNull(result);
        Assert.Equal(ModelPricingSource.Catalog, result.Source);
        Assert.Equal(4.00m, result.InputPerMillion);
        Assert.Equal(20.00m, result.OutputPerMillion);
    }

    [Fact]
    public void Resolve_UserAiModel_CustomOverrideWins()
    {
        using var db = CreateInMemoryDb();
        var service = new ModelPricingService(_metadataService, db);

        var userModel = new UserAiModel
        {
            Provider = "Anthropic",
            ModelId = "claude-3-7-sonnet-20250219",
            PricingMode = "custom",
            InputPricePerMillion = 4.0m,
            OutputPricePerMillion = 18.0m
        };

        var result = service.Resolve(userModel);

        Assert.NotNull(result);
        Assert.Equal(ModelPricingSource.Custom, result.Source);
        Assert.Equal(4.0m, result.InputPerMillion);
        Assert.Equal(18.0m, result.OutputPerMillion);
    }

    [Fact]
    public void ComputeCost_PricesCacheReadsAndOmitCacheCreationWhenNoRate()
    {
        // Rate with cache write
        var pricingWithWrite = new ModelPricing(
            InputPerMillion: 3.0m,
            OutputPerMillion: 15.0m,
            CachedInputPerMillion: 0.30m,
            CacheWritePerMillion: 3.75m);

        decimal cost1 = ModelPricingService.ComputeCost(
            pricingWithWrite,
            inputTokens: 1_000_000,
            outputTokens: 1_000_000,
            cacheReadTokens: 1_000_000,
            cacheCreationTokens: 1_000_000);

        // 3.0 + 15.0 + 0.30 + 3.75 = 22.05
        Assert.Equal(22.05m, cost1);

        // Rate without cache write
        var pricingNoWrite = new ModelPricing(
            InputPerMillion: 3.0m,
            OutputPerMillion: 15.0m,
            CachedInputPerMillion: 0.30m,
            CacheWritePerMillion: null);

        decimal cost2 = ModelPricingService.ComputeCost(
            pricingNoWrite,
            inputTokens: 1_000_000,
            outputTokens: 1_000_000,
            cacheReadTokens: 1_000_000,
            cacheCreationTokens: 1_000_000);

        // 3.0 + 15.0 + 0.30 + 0 = 18.30
        Assert.Equal(18.30m, cost2);
    }

    [Fact]
    public async Task ResolveForConfigurationAsync_CachesLookups()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = CreateInMemoryDb();
        var config = new SystemAiApiConfiguration
        {
            Id = 42,
            DisplayName = "Test Config",
            Provider = "OpenAI",
            ModelId = "gpt-5.6-sol",
            PricingMode = "default"
        };
        db.SystemAiApiConfigurations.Add(config);
        await db.SaveChangesAsync(ct);

        var service = new ModelPricingService(_metadataService, db);

        var first = await service.ResolveForConfigurationAsync(42, null, null);
        Assert.NotNull(first);
        Assert.Equal(4.00m, first.InputPerMillion);

        // Remove from db to verify cached lookup succeeds
        db.SystemAiApiConfigurations.Remove(config);
        await db.SaveChangesAsync(ct);

        var second = await service.ResolveForConfigurationAsync(42, null, null);
        Assert.Same(first, second);
    }

    // ---------------------------------------------------------------------------------------------
    // Long-context rate cards. The threshold is a property of a single request, never of a turn's sum.
    // ---------------------------------------------------------------------------------------------

    private static ModelPricing LongContextPricingFixture() => new ModelPricing(
        InputPerMillion: 10.00m,
        OutputPerMillion: 50.00m,
        CachedInputPerMillion: 1.00m,
        CacheWritePerMillion: 12.50m,
        LongContext: new LongContextPricing(
            ThresholdInputTokens: 272_000,
            InputPerMillion: 20.00m,
            OutputPerMillion: 75.00m,
            CachedInputPerMillion: 2.00m,
            CacheWritePerMillion: 25.00m));

    private static TokenUsageReport Call(int promptTokens, int outputTokens = 0, int cacheRead = 0, int cacheCreation = 0) =>
        new TokenUsageReport
        {
            TotalPromptTokens = promptTokens,
            CacheReadTokens = cacheRead,
            CacheCreationTokens = cacheCreation,
            UncachedInputTokens = promptTokens - cacheRead,
            OutputTokens = outputTokens
        };

    [Fact]
    public void ComputeCost_ThirtySmallCalls_AreNotSurcharged_EvenThoughTheirSumExceedsTheThreshold()
    {
        // R2, the regression this whole overload exists for. Thirty calls of 100,000 prompt tokens sum to
        // 3,000,000 against a 272,000 threshold; not one of them is a long-context request. Costing the sum
        // would surcharge nearly every agentic turn Overseer makes, by about 2x.
        var pricing = LongContextPricingFixture();
        var calls = Enumerable.Range(0, 30).Select(_ => Call(100_000, outputTokens: 1_000)).ToList();

        decimal tiered = ModelPricingService.ComputeCost(pricing, calls);
        decimal flat = ModelPricingService.ComputeCost(pricing, 3_000_000, 30_000);

        Assert.Equal(flat, tiered);
        Assert.Equal((3_000_000 / 1_000_000m * 10.00m) + (30_000 / 1_000_000m * 50.00m), tiered);
    }

    [Fact]
    public void ComputeCost_SingleLongCall_ChargesAllFourComponentsAtTheLongContextCard()
    {
        var pricing = LongContextPricingFixture();
        var calls = new List<TokenUsageReport>
        {
            Call(300_000, outputTokens: 10_000, cacheRead: 100_000, cacheCreation: 50_000)
        };

        decimal cost = ModelPricingService.ComputeCost(pricing, calls);

        // The four buckets partition the 300,000-token prompt: base in 150,000 @ 20.00 (the prompt less the
        // 100,000 read from cache and the 50,000 written to it); out 10,000 @ 75.00; cache read 100,000 @
        // 2.00; cache write 50,000 @ 25.00.
        decimal expected = (150_000 / 1_000_000m * 20.00m)
            + (10_000 / 1_000_000m * 75.00m)
            + (100_000 / 1_000_000m * 2.00m)
            + (50_000 / 1_000_000m * 25.00m);
        Assert.Equal(expected, cost);
    }

    [Fact]
    public void ComputeCost_MixedTurn_ChargesOnlyTheLargeCallAtTheLongContextCard()
    {
        var pricing = LongContextPricingFixture();
        var calls = new List<TokenUsageReport>
        {
            Call(50_000, outputTokens: 1_000),
            Call(60_000, outputTokens: 2_000),
            Call(300_000, outputTokens: 4_000)
        };

        decimal cost = ModelPricingService.ComputeCost(pricing, calls);

        decimal expected =
            (50_000 / 1_000_000m * 10.00m) + (1_000 / 1_000_000m * 50.00m)
            + (60_000 / 1_000_000m * 10.00m) + (2_000 / 1_000_000m * 50.00m)
            + (300_000 / 1_000_000m * 20.00m) + (4_000 / 1_000_000m * 75.00m);
        Assert.Equal(expected, cost);
    }

    [Fact]
    public void ComputeCost_ThresholdBoundary_IsExclusive()
    {
        var pricing = LongContextPricingFixture();

        decimal atThreshold = ModelPricingService.ComputeCost(
            pricing, new List<TokenUsageReport> { Call(272_000) });
        decimal oneOver = ModelPricingService.ComputeCost(
            pricing, new List<TokenUsageReport> { Call(272_001) });

        Assert.Equal(272_000 / 1_000_000m * 10.00m, atThreshold);
        Assert.Equal(272_001 / 1_000_000m * 20.00m, oneOver);
    }

    [Fact]
    public void ComputeCost_LongContextCardWithoutACacheWriteRate_FallsBackToTheBaseRate()
    {
        // A provider page that says only "2x input and 1.5x output" is not saying cache writes double, so
        // the base rate is the conservative reading rather than a guessed multiplier.
        var pricing = new ModelPricing(
            InputPerMillion: 10.00m,
            OutputPerMillion: 50.00m,
            CachedInputPerMillion: 1.00m,
            CacheWritePerMillion: 12.50m,
            LongContext: new LongContextPricing(272_000, 20.00m, 75.00m, CachedInputPerMillion: 2.00m));

        decimal cost = ModelPricingService.ComputeCost(
            pricing, new List<TokenUsageReport> { Call(300_000, cacheCreation: 100_000) });

        // Base in 200,000 @ 20.00 (the 300,000-token prompt less the 100,000 written to cache), cache write
        // 100,000 @ the base card's 12.50.
        decimal expected = (200_000 / 1_000_000m * 20.00m) + (100_000 / 1_000_000m * 12.50m);
        Assert.Equal(expected, cost);
    }

    // ---------------------------------------------------------------------------------------------
    // Service tiers. A scalar on all four rates, keyed on the tier the provider actually served.
    // ---------------------------------------------------------------------------------------------

    private static ModelPricing TieredPricingFixture() => new ModelPricing(
        InputPerMillion: 10.00m,
        OutputPerMillion: 50.00m,
        ServiceTierMultipliers: new Dictionary<string, decimal>
        {
            ["flex"] = 0.5m,
            ["priority"] = 2.0m
        });

    [Fact]
    public void ComputeCost_ServiceTier_ScalesTheWholeTurn()
    {
        var pricing = TieredPricingFixture();
        var calls = new List<TokenUsageReport> { Call(1_000_000, outputTokens: 1_000_000) };

        decimal standard = ModelPricingService.ComputeCost(pricing, calls);
        decimal flex = ModelPricingService.ComputeCost(pricing, calls, actualServiceTier: "flex");
        decimal priority = ModelPricingService.ComputeCost(pricing, calls, actualServiceTier: "priority");

        Assert.Equal(60.00m, standard);
        Assert.Equal(30.00m, flex);
        Assert.Equal(120.00m, priority);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("default")]
    [InlineData("standard")]
    [InlineData("some-tier-nobody-declared")]
    public void ResolveServiceTierMultiplier_UnknownOrUnlistedTier_IsExactlyOne(string? tier)
    {
        // Never zero: an unlisted tier means "no multiplier published", not "free".
        Assert.Equal(1.0m, ModelPricingService.ResolveServiceTierMultiplier(TieredPricingFixture(), tier, null));
    }

    [Fact]
    public void ResolveServiceTierMultiplier_ServedTierBeatsRequestedTier()
    {
        // R3. OpenAI requests "auto"/"fast" and serves "default"/"priority"; a priority request that was
        // served as default must be billed as default.
        var pricing = TieredPricingFixture();

        Assert.Equal(1.0m, ModelPricingService.ResolveServiceTierMultiplier(pricing, "default", "priority"));
        Assert.Equal(2.0m, ModelPricingService.ResolveServiceTierMultiplier(pricing, "priority", "default"));
        // A served tier the catalog does not list is 1.0x and must not fall through to the requested one:
        // "default" is exactly such a tier, and falling through is the R3 defect.
        Assert.Equal(1.0m, ModelPricingService.ResolveServiceTierMultiplier(pricing, "standard", "flex"));
        // Falls back to the requested tier only when the provider reported none at all.
        Assert.Equal(2.0m, ModelPricingService.ResolveServiceTierMultiplier(pricing, null, "priority"));
    }

    [Fact]
    public void ResolveServiceTierMultiplier_NormalizesThePrefixedForm()
    {
        var pricing = TieredPricingFixture();
        Assert.Equal(0.5m, ModelPricingService.ResolveServiceTierMultiplier(pricing, "SERVICE_TIER_FLEX", null));
    }

    [Fact]
    public void ComputeCost_LongContextAndServiceTier_ComposeMultiplicatively()
    {
        var pricing = LongContextPricingFixture() with
        {
            ServiceTierMultipliers = new Dictionary<string, decimal> { ["priority"] = 2.0m }
        };

        decimal cost = ModelPricingService.ComputeCost(
            pricing, new List<TokenUsageReport> { Call(300_000) }, actualServiceTier: "priority");

        Assert.Equal(300_000 / 1_000_000m * 20.00m * 2.0m, cost);
    }

    [Fact]
    public void ShippedCatalogs_DeclareOnlyTierKeysThatNormalizeToThemselves()
    {
        // R4: a mistyped tier key produces no error, just quiet 1.0x mispricing. Every key the shipped
        // catalogs declare must be reachable through the normalization the provider readers apply.
        var providers = new[] { "OpenAI", "Anthropic", "Google" };
        int checkedKeys = 0;

        foreach (var provider in providers)
        {
            foreach (var entry in _metadataService.GetCatalogEntries(provider))
            {
                var multipliers = entry.Pricing?.ServiceTierMultipliers;
                if (multipliers == null) continue;

                foreach (var key in multipliers.Keys)
                {
                    Assert.Equal(key, ProviderHelper.NormalizeServiceTier(key));
                    checkedKeys++;
                }
            }
        }

        Assert.True(checkedKeys > 0, "No service-tier multipliers were found in the shipped catalogs.");
    }

    // ---------------------------------------------------------------------------------------------
    // Scheduled price changes. The base card is always the rate in force today.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void ResolveDefault_BeforeAScheduledChange_ResolvesTheBaseCard()
    {
        using var db = CreateInMemoryDb();
        var service = new ModelPricingService(_metadataService, db);

        var result = service.ResolveDefault("Google", "gemini-3.7-flash", new DateOnly(2026, 12, 31));

        Assert.NotNull(result);
        Assert.Equal(0.75m, result.InputPerMillion);
        Assert.Equal(3.75m, result.OutputPerMillion);
        Assert.False(result.ScheduleElapsed);
        Assert.NotNull(result.ScheduledChange);
        Assert.Equal(new DateOnly(2027, 1, 1), result.ScheduledChange.EffectiveFrom);
    }

    [Theory]
    [InlineData(2027, 1, 1)]
    [InlineData(2027, 6, 15)]
    public void ResolveDefault_OnOrAfterAScheduledChange_ResolvesTheScheduledCard(int year, int month, int day)
    {
        using var db = CreateInMemoryDb();
        var service = new ModelPricingService(_metadataService, db);

        var result = service.ResolveDefault("Google", "gemini-3.7-flash", new DateOnly(year, month, day));

        Assert.NotNull(result);
        Assert.Equal(1.50m, result.InputPerMillion);
        Assert.Equal(7.50m, result.OutputPerMillion);
        Assert.True(result.ScheduleElapsed);
    }

    [Fact]
    public void ParseScheduled_ScheduledCardWithoutACachedRate_InheritsTheBaseValue()
    {
        using var db = CreateInMemoryDb();
        var service = new ModelPricingService(_metadataService, db);

        // Exercised through ResolveDefault, which is where the inheritance actually happens.
        var catalogEntry = new ModelCatalogPricing
        {
            InputPerMillion = 1.00m,
            OutputPerMillion = 4.00m,
            CachedInputPerMillion = 0.10m,
            CacheWritePerMillion = 1.25m,
            ScheduledChange = new ModelCatalogScheduledPricing
            {
                EffectiveFrom = "2027-01-01",
                InputPerMillion = 2.00m,
                OutputPerMillion = 8.00m
            }
        };

        var scheduled = ModelPricingService.ParseScheduled(catalogEntry.ScheduledChange);
        Assert.NotNull(scheduled);
        Assert.Null(scheduled.CachedInputPerMillion);
        Assert.Null(scheduled.CacheWritePerMillion);

        // ResolveDefault falls back to the base value when the scheduled card omits one.
        decimal? resolvedCached = scheduled.CachedInputPerMillion ?? catalogEntry.CachedInputPerMillion;
        Assert.Equal(0.10m, resolvedCached);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("2027-13-01")]
    [InlineData("01/01/2027")]
    [InlineData("soon")]
    public void ParseScheduled_UnparseableDate_YieldsNoSchedule(string effectiveFrom)
    {
        // R6: a malformed date must never silently change a price. No schedule means the base card stands.
        var parsed = ModelPricingService.ParseScheduled(new ModelCatalogScheduledPricing
        {
            EffectiveFrom = effectiveFrom,
            InputPerMillion = 99.00m,
            OutputPerMillion = 99.00m
        });

        Assert.Null(parsed);
    }

    [Fact]
    public void Resolve_CustomOverride_HasNoLongContextCardNoTiersAndNoSchedule()
    {
        using var db = CreateInMemoryDb();
        var service = new ModelPricingService(_metadataService, db);

        // gemini-3.7-flash carries tier multipliers and a scheduled change in the catalog; a custom price
        // replaces all of it, because both override entities store four flat rates and nothing else.
        var config = new SystemAiApiConfiguration
        {
            Id = 7,
            DisplayName = "Custom priced",
            Provider = "Google",
            ModelId = "gemini-3.7-flash",
            PricingMode = "custom",
            InputPricePerMillion = 1.11m,
            OutputPricePerMillion = 2.22m
        };

        var result = service.Resolve(config);

        Assert.NotNull(result);
        Assert.Equal(ModelPricingSource.Custom, result.Source);
        Assert.Equal(1.11m, result.InputPerMillion);
        Assert.Null(result.LongContext);
        Assert.Null(result.ServiceTierMultipliers);
        Assert.Null(result.ScheduledChange);
        Assert.False(result.ScheduleElapsed);
    }

    // ---------------------------------------------------------------------------------------------
    // Pricing snapshot round-trip. A run replays at the rates that applied when it ran.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task ResolveForRunAsync_SnapshotRoundTripsTheLongContextCardAndTierMultipliers()
    {
        using var db = CreateInMemoryDb();
        var service = new ModelPricingService(_metadataService, db);

        var run = new BenchmarkRun
        {
            PricingSnapshotJson = """
            {
              "candidate": {
                "inputPerMillion": 10.00,
                "outputPerMillion": 50.00,
                "cachedInputPerMillion": 1.00,
                "cacheWritePerMillion": 12.50,
                "source": "catalog",
                "asOf": "2026-09-06",
                "longContext": {
                  "thresholdInputTokens": 272000,
                  "inputPerMillion": 20.00,
                  "outputPerMillion": 75.00,
                  "cachedInputPerMillion": 2.00,
                  "cacheWritePerMillion": 25.00
                },
                "serviceTierMultipliers": { "flex": 0.5, "priority": 2.0 }
              }
            }
            """
        };

        var pricing = await service.ResolveForRunAsync(run);

        Assert.True(pricing.IsSnapshot);
        Assert.NotNull(pricing.Candidate);
        Assert.NotNull(pricing.Candidate.LongContext);
        Assert.Equal(272_000, pricing.Candidate.LongContext.ThresholdInputTokens);
        Assert.Equal(20.00m, pricing.Candidate.LongContext.InputPerMillion);
        Assert.Equal(25.00m, pricing.Candidate.LongContext.CacheWritePerMillion);
        Assert.NotNull(pricing.Candidate.ServiceTierMultipliers);
        Assert.Equal(0.5m, pricing.Candidate.ServiceTierMultipliers["flex"]);
        // The schedule is deliberately absent: the snapshot stores the already-resolved card.
        Assert.Null(pricing.Candidate.ScheduledChange);
    }

    [Fact]
    public async Task ResolveForRunAsync_SnapshotWithoutTheNewKeys_ParsesToFlatPricing()
    {
        using var db = CreateInMemoryDb();
        var service = new ModelPricingService(_metadataService, db);

        var run = new BenchmarkRun
        {
            PricingSnapshotJson = """
            {
              "candidate": {
                "inputPerMillion": 4.00,
                "outputPerMillion": 20.00,
                "source": "catalog",
                "asOf": "2026-09-05"
              }
            }
            """
        };

        var pricing = await service.ResolveForRunAsync(run);

        Assert.NotNull(pricing.Candidate);
        Assert.Null(pricing.Candidate.LongContext);
        Assert.Null(pricing.Candidate.ServiceTierMultipliers);
        Assert.Equal(4.00m, pricing.Candidate.InputPerMillion);
    }

    // ---------------------------------------------------------------------------------------------
    // Costing from stored benchmark totals.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void ComputeCostFromTotals_WithNoLongContextTokens_MatchesFlatRateCosting()
    {
        // Every run recorded before tiered pricing existed must recost to exactly what it did before.
        var pricing = LongContextPricingFixture();

        decimal fromTotals = ModelPricingService.ComputeCostFromTotals(
            pricing, totalPromptTokens: 1_000_000, totalOutputTokens: 100_000,
            cacheReadTokens: 400_000, cacheCreationTokens: 50_000);

        // 1,000,000 prompt less 400,000 cache reads and 50,000 cache writes leaves 550,000 at the base rate.
        decimal flat = ModelPricingService.ComputeCost(pricing, 550_000, 100_000, 400_000, 50_000);

        Assert.Equal(flat, fromTotals);
    }

    [Fact]
    public void ComputeCostFromTotals_SplitsTheLongContextPortionOntoItsOwnCard()
    {
        var pricing = LongContextPricingFixture();

        decimal cost = ModelPricingService.ComputeCostFromTotals(
            pricing,
            totalPromptTokens: 1_000_000, totalOutputTokens: 100_000,
            cacheReadTokens: 400_000, cacheCreationTokens: 50_000,
            longContextPromptTokens: 300_000, longContextOutputTokens: 20_000,
            longContextCacheReadTokens: 100_000, longContextCacheCreationTokens: 10_000);

        // Standard portion: 700,000 prompt less 300,000 cache reads and 40,000 cache writes = 360,000 base.
        decimal standard = ModelPricingService.ComputeCost(pricing, 360_000, 80_000, 300_000, 40_000);
        // Long-context portion: 300,000 prompt less 100,000 cache reads and 10,000 cache writes = 190,000 base.
        decimal longCard = (190_000 / 1_000_000m * 20.00m)
            + (20_000 / 1_000_000m * 75.00m)
            + (100_000 / 1_000_000m * 2.00m)
            + (10_000 / 1_000_000m * 25.00m);

        Assert.Equal(standard + longCard, cost);
    }

    [Fact]
    public void ComputeLongContextBuckets_FlatRateModel_IsAllZero()
    {
        var flat = new ModelPricing(10.00m, 50.00m);
        var buckets = ModelPricingService.ComputeLongContextBuckets(
            flat, new List<TokenUsageReport> { Call(900_000, outputTokens: 5_000) });

        Assert.Equal(0, buckets.InputTokens);
        Assert.Equal(0, buckets.OutputTokens);
        Assert.Equal(0, buckets.CallCount);
    }

    [Fact]
    public void ComputeLongContextBuckets_CountsOnlyTheCallsOverTheThreshold()
    {
        var pricing = LongContextPricingFixture();
        var calls = new List<TokenUsageReport>
        {
            Call(100_000, outputTokens: 1_000, cacheRead: 40_000),
            Call(300_000, outputTokens: 4_000, cacheRead: 200_000, cacheCreation: 5_000),
            Call(272_000, outputTokens: 2_000)
        };

        var buckets = ModelPricingService.ComputeLongContextBuckets(pricing, calls);

        Assert.Equal(1, buckets.CallCount);
        Assert.Equal(300_000, buckets.InputTokens);
        Assert.Equal(4_000, buckets.OutputTokens);
        Assert.Equal(200_000, buckets.CacheReadTokens);
        Assert.Equal(5_000, buckets.CacheCreationTokens);
    }

    // ---------------------------------------------------------------------------------------------
    // The cache-creation partition. The four billed buckets — base input, output, cache read, cache
    // write — are disjoint, so a cache-written token is charged at the write rate and nowhere else.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// A usage report shaped the way AnthropicProvider shapes one: the API reports three separate prompt
    /// figures, so the total is their sum and UncachedInputTokens is the total less the cache reads —
    /// which still contains the cache-creation tokens.
    /// </summary>
    private static TokenUsageReport AnthropicCall(
        int inputTokens, int cacheCreationTokens, int cacheReadTokens, int outputTokens) =>
        new TokenUsageReport
        {
            TotalPromptTokens = inputTokens + cacheCreationTokens + cacheReadTokens,
            CacheReadTokens = cacheReadTokens,
            CacheCreationTokens = cacheCreationTokens,
            UncachedInputTokens = inputTokens + cacheCreationTokens,
            OutputTokens = outputTokens
        };

    [Fact]
    public void ComputeCost_AnthropicShapedCall_ChargesEachOfTheFourBucketsExactlyOnce()
    {
        var pricing = new ModelPricing(
            InputPerMillion: 2.00m,
            OutputPerMillion: 10.00m,
            CachedInputPerMillion: 0.20m,
            CacheWritePerMillion: 2.50m);

        var call = AnthropicCall(
            inputTokens: 40_000, cacheCreationTokens: 60_000,
            cacheReadTokens: 500_000, outputTokens: 8_000);

        // The report's own arithmetic: the provider's three prompt figures partition the prompt total, and
        // the property that names the base-input bucket recovers the first of them.
        Assert.Equal(600_000, call.TotalPromptTokens);
        Assert.Equal(100_000, call.UncachedInputTokens);
        Assert.Equal(40_000, call.BillableUncachedInputTokens);

        decimal cost = ModelPricingService.ComputeCost(
            pricing, new List<TokenUsageReport> { call });

        decimal expected = (40_000 / 1_000_000m * 2.00m)
            + (8_000 / 1_000_000m * 10.00m)
            + (500_000 / 1_000_000m * 0.20m)
            + (60_000 / 1_000_000m * 2.50m);
        Assert.Equal(expected, cost);

        // Billing the 60,000 written tokens at the base rate as well overcharges by exactly their base-rate
        // price, which is what makes the buckets' disjointness load-bearing rather than cosmetic.
        Assert.Equal(
            expected + (60_000 / 1_000_000m * 2.00m),
            ModelPricingService.ComputeCost(
                pricing, call.UncachedInputTokens, call.OutputTokens,
                call.CacheReadTokens, call.CacheCreationTokens));
    }

    [Fact]
    public void ComputeCost_Run27Totals_ChargeCacheWritesOnlyAtTheWriteRate()
    {
        // Benchmark run 27: a cache-heavy agentic run against Claude 5 Sonnet, whose 291,680 cache-written
        // tokens are nearly all of what is left of the prompt once the cache reads are taken off. Only 176
        // tokens are genuinely new input, so charging the written tokens twice inflates the run by ~41%.
        using var db = CreateInMemoryDb();
        var service = new ModelPricingService(_metadataService, db);

        var pricing = service.ResolveDefault("Anthropic", "claude-sonnet-5");
        Assert.NotNull(pricing);
        Assert.Equal(2.00m, pricing.InputPerMillion);
        Assert.Equal(10.00m, pricing.OutputPerMillion);
        Assert.Equal(0.20m, pricing.CachedInputPerMillion);
        Assert.Equal(2.50m, pricing.CacheWritePerMillion);
        Assert.Null(pricing.LongContext);

        const int promptTokens = 2_123_059;
        const int outputTokens = 31_844;
        const int cacheReadTokens = 1_831_203;
        const int cacheCreationTokens = 291_680;

        int baseInputTokens = promptTokens - cacheReadTokens - cacheCreationTokens;
        Assert.Equal(176, baseInputTokens);

        decimal expected = (baseInputTokens / 1_000_000m * 2.00m)
            + (outputTokens / 1_000_000m * 10.00m)
            + (cacheReadTokens / 1_000_000m * 0.20m)
            + (cacheCreationTokens / 1_000_000m * 2.50m);

        decimal fromTotals = ModelPricingService.ComputeCostFromTotals(
            pricing,
            totalPromptTokens: promptTokens, totalOutputTokens: outputTokens,
            cacheReadTokens: cacheReadTokens, cacheCreationTokens: cacheCreationTokens);
        Assert.Equal(expected, fromTotals);

        // The per-call path agrees: with no long-context card a single call costs the same either way.
        decimal perCall = ModelPricingService.ComputeCost(
            pricing,
            new List<TokenUsageReport>
            {
                AnthropicCall(baseInputTokens, cacheCreationTokens, cacheReadTokens, outputTokens)
            });
        Assert.Equal(expected, perCall);

        // The overcharge is the cache-written tokens at the base input rate, and nothing else.
        Assert.Equal(
            expected + (cacheCreationTokens / 1_000_000m * 2.00m),
            ModelPricingService.ComputeCost(
                pricing, promptTokens - cacheReadTokens, outputTokens,
                cacheReadTokens, cacheCreationTokens));
        Assert.True(fromTotals < 1.50m);
    }

    [Fact]
    public void ComputeCost_ReportWithoutCacheCreation_IsExactlyTheUncachedInputFigure()
    {
        // Google and the OpenAI Responses API report no cache-creation tokens at all, so for them the
        // base-input bucket and the uncached figure coincide and costing is bit-identical. Flat rates, so
        // that the per-call path and the four-argument path are reading the same card.
        var pricing = new ModelPricing(
            InputPerMillion: 10.00m,
            OutputPerMillion: 50.00m,
            CachedInputPerMillion: 1.00m,
            CacheWritePerMillion: 12.50m);
        var call = AnthropicCall(
            inputTokens: 150_000, cacheCreationTokens: 0,
            cacheReadTokens: 250_000, outputTokens: 12_000);

        Assert.Equal(call.UncachedInputTokens, call.BillableUncachedInputTokens);

        decimal perCall = ModelPricingService.ComputeCost(pricing, new List<TokenUsageReport> { call });
        decimal byUncachedFigure = ModelPricingService.ComputeCost(
            pricing, call.UncachedInputTokens, call.OutputTokens,
            call.CacheReadTokens, call.CacheCreationTokens);

        Assert.Equal(byUncachedFigure, perCall);
        Assert.Equal(
            (150_000 / 1_000_000m * 10.00m)
                + (12_000 / 1_000_000m * 50.00m)
                + (250_000 / 1_000_000m * 1.00m),
            perCall);

        // Same for the totals overload: with no cache writes recorded the extra subtraction is a no-op.
        Assert.Equal(
            ModelPricingService.ComputeCost(pricing, 600_000, 100_000, 400_000, 0),
            ModelPricingService.ComputeCostFromTotals(
                pricing, totalPromptTokens: 1_000_000, totalOutputTokens: 100_000,
                cacheReadTokens: 400_000, cacheCreationTokens: 0));
    }
}
