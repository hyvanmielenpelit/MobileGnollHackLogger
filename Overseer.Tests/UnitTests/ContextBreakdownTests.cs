using Overseer.Services;
using Xunit;

namespace Overseer.Tests.UnitTests;

public class ContextBreakdownTests
{
    private static int Sum(ContextBreakdown b)
        => b.SystemPrompt + b.ToolDefinitions + b.GameContext + b.History + b.Documents + b.Images + b.ToolActivity;

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(29900)]
    [InlineData(1000003)]
    [InlineData(int.MaxValue)]
    public void TheCategories_SumExactlyToTheTotal(int total)
    {
        var seed = new ContextBreakdownSeed(8120, 4200, 7300, 1100, 650);

        var breakdown = ContextBreakdownEstimator.Scale(seed, 6430, 600, total, 0, 0, 0);

        Assert.NotNull(breakdown);
        Assert.Equal(total, Sum(breakdown));
    }

    [Fact]
    public void Scaling_KeepsTheProportions_AndAZeroWeightStaysZero()
    {
        var seed = new ContextBreakdownSeed(1000, 0, 3000, 0, 0);

        var breakdown = ContextBreakdownEstimator.Scale(seed, 0, 0, 4000, 0, 0, 0)!;

        Assert.Equal(1000, breakdown.SystemPrompt);
        Assert.Equal(3000, breakdown.History);
        Assert.Equal(0, breakdown.GameContext);
        Assert.Equal(0, breakdown.ToolActivity);
    }

    [Fact]
    public void ImagesWeigh_TheirEstimate_NotTheirBase64()
    {
        int imageTokens = ChatTokenEstimator.EstimateImage("Anthropic", 1000, 500);
        var seed = new ContextBreakdownSeed(0, 0, 648, 0, imageTokens);

        var breakdown = ContextBreakdownEstimator.Scale(seed, 0, 0, 2000, 0, 0, 0)!;

        Assert.Equal(1000, breakdown.Images);
        Assert.Equal(1000, breakdown.History);
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(-1, 100)]
    [InlineData(100, 0)]
    public void NothingToScale_IsNull(int total, int weight)
        => Assert.Null(ContextBreakdownEstimator.Scale(new ContextBreakdownSeed(weight, 0, 0, 0, 0), 0, 0, total, 0, 0, 0));

    [Fact]
    public void NegativeSizes_CountAsZero()
    {
        var breakdown = ContextBreakdownEstimator.Scale(new ContextBreakdownSeed(100, -5, 0, 0, 0), 0, -7, 50, 0, 0, 0)!;

        Assert.Equal(50, breakdown.SystemPrompt);
        Assert.Equal(50, Sum(breakdown));
    }

    [Fact]
    public void TheMeasuredFigures_PassThrough()
    {
        var breakdown = ContextBreakdownEstimator.Scale(new ContextBreakdownSeed(1, 0, 0, 0, 0), 0, 0, 100, 40, 30, 20)!;

        Assert.Equal(40, breakdown.CacheRead);
        Assert.Equal(30, breakdown.CacheWrite);
        Assert.Equal(20, breakdown.Reasoning);
    }

    [Fact]
    public void TheJson_FitsTheColumn_AndRoundTrips()
    {
        var largest = new ContextBreakdown
        {
            SystemPrompt = int.MaxValue, ToolDefinitions = int.MaxValue, GameContext = int.MaxValue, History = int.MaxValue,
            Documents = int.MaxValue, Images = int.MaxValue, ToolActivity = int.MaxValue,
            CacheRead = int.MaxValue, CacheWrite = int.MaxValue, Reasoning = int.MaxValue
        };

        string json = ContextBreakdownEstimator.ToJson(largest)!;

        Assert.True(json.Length < 1000);
        Assert.Contains("\"systemPrompt\"", json);
        Assert.Equal(largest, ContextBreakdownEstimator.FromJson(json));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("{\"version\":2,\"history\":5}")]
    [InlineData("{\"history\":\"many\",\"version\":1}")]
    public void AMissingOrMalformedOrUnknownBreakdown_IsNull(string? json)
        => Assert.Null(ContextBreakdownEstimator.FromJson(json));
}
