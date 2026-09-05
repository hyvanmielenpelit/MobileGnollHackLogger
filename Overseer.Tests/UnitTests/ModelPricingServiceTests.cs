namespace Overseer.Tests.UnitTests;

using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Services;
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
        Assert.Equal("USD", result.Currency);
        Assert.Equal("2026-09-05", result.AsOf);
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
        Assert.Equal("USD", result.Currency);
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
        await db.SaveChangesAsync();

        var service = new ModelPricingService(_metadataService, db);

        var first = await service.ResolveForConfigurationAsync(42, null, null);
        Assert.NotNull(first);
        Assert.Equal(4.00m, first.InputPerMillion);

        // Remove from db to verify cached lookup succeeds
        db.SystemAiApiConfigurations.Remove(config);
        await db.SaveChangesAsync();

        var second = await service.ResolveForConfigurationAsync(42, null, null);
        Assert.Same(first, second);
    }
}