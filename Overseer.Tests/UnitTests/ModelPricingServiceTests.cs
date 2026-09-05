namespace Overseer.Tests.UnitTests;

using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Overseer.Services;
using Xunit;

public class ModelPricingServiceTests
{
    [Fact]
    public void GetPricing_ReturnsNull_WhenNotConfigured()
    {
        var configuration = new ConfigurationBuilder().Build();
        var service = new ModelPricingService(configuration);

        var result = service.GetPricing("gpt-5.6");

        Assert.Null(result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GetPricing_ReturnsNull_WhenModelIdIsNullOrEmpty(string? modelId)
    {
        var inMemory = new Dictionary<string, string?>
        {
            ["ModelPricing:gpt-5.6:InputPerMillion"] = "2.50",
            ["ModelPricing:gpt-5.6:OutputPerMillion"] = "10.00"
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(inMemory).Build();
        var service = new ModelPricingService(configuration);

        var result = service.GetPricing(modelId);

        Assert.Null(result);
    }

    [Fact]
    public void GetPricing_ReturnsNull_WhenNoPrefixMatches()
    {
        var inMemory = new Dictionary<string, string?>
        {
            ["ModelPricing:gpt-5.6:InputPerMillion"] = "2.50",
            ["ModelPricing:gpt-5.6:OutputPerMillion"] = "10.00"
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(inMemory).Build();
        var service = new ModelPricingService(configuration);

        var result = service.GetPricing("claude-3-opus");

        Assert.Null(result);
    }

    [Fact]
    public void GetPricing_ResolvesExactAndPrefixMatch_CaseInsensitive()
    {
        var inMemory = new Dictionary<string, string?>
        {
            ["ModelPricing:gpt-5.6:InputPerMillion"] = "2.50",
            ["ModelPricing:gpt-5.6:OutputPerMillion"] = "10.00",
            ["ModelPricing:gpt-5.6:CachedInputPerMillion"] = "0.25"
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(inMemory).Build();
        var service = new ModelPricingService(configuration);

        var result = service.GetPricing("GPT-5.6-luna");

        Assert.NotNull(result);
        Assert.Equal(2.50m, result.InputPerMillion);
        Assert.Equal(10.00m, result.OutputPerMillion);
        Assert.Equal(0.25m, result.CachedInputPerMillion);
    }

    [Fact]
    public void GetPricing_ResolvesLongestPrefix_WhenMultiplePrefixesMatch()
    {
        var inMemory = new Dictionary<string, string?>
        {
            ["ModelPricing:gpt:InputPerMillion"] = "5.00",
            ["ModelPricing:gpt:OutputPerMillion"] = "15.00",
            ["ModelPricing:gpt-5:InputPerMillion"] = "3.00",
            ["ModelPricing:gpt-5:OutputPerMillion"] = "12.00",
            ["ModelPricing:gpt-5.6:InputPerMillion"] = "2.50",
            ["ModelPricing:gpt-5.6:OutputPerMillion"] = "10.00"
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(inMemory).Build();
        var service = new ModelPricingService(configuration);

        var result = service.GetPricing("gpt-5.6-preview");

        Assert.NotNull(result);
        Assert.Equal(2.50m, result.InputPerMillion);
        Assert.Equal(10.00m, result.OutputPerMillion);
        Assert.Null(result.CachedInputPerMillion);
    }
}