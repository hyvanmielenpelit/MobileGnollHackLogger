namespace Overseer.Tests.UnitTests;

using Overseer.Services;
using Xunit;

/// <summary>
/// The input limit chat history is truncated to at the start of a turn: an explicit Max Input Tokens
/// always wins, and otherwise a model with a long-context card keeps its starting prompt under the
/// card's threshold less the turn's headroom, in estimator units.
/// </summary>
public class ChatHistoryBudgetTests
{
    private const int Headroom = 40000;
    private const decimal Factor = 1.3m;

    private static ModelMetadata LongContextMetadata() => Metadata(872000, 100000);

    private static ModelMetadata Metadata(int maxInputTokens, int? threshold) => new()
    {
        MaxInputTokens = maxInputTokens,
        DefaultPricing = threshold.HasValue
            ? new ModelCatalogPricing { LongContext = new ModelCatalogLongContextPricing { ThresholdInputTokens = threshold.Value } }
            : new ModelCatalogPricing()
    };

    [Theory]
    [InlineData(30000)]
    [InlineData(200000)]
    public void ExplicitLimit_AlwaysWins_EvenAboveTheThreshold(int explicitLimit)
    {
        var limit = ChatHistoryBudget.Resolve(explicitLimit, LongContextMetadata(), true, Headroom, Factor);

        Assert.Equal(new ChatHistoryLimit(explicitLimit, ChatHistoryLimitSource.Explicit), limit);
    }

    [Fact]
    public void LongContextCard_WithDefaults_IsTheLongContextBudget()
    {
        var meta = LongContextMetadata();
        Assert.Equal(872000, meta.MaxInputTokens);

        var limit = ChatHistoryBudget.Resolve(null, meta, true, Headroom, Factor);

        // (100,000 - 40,000) / 1.3 = 46,153.8, floored.
        Assert.Equal(new ChatHistoryLimit(46153, ChatHistoryLimitSource.LongContextBudget), limit);
    }

    [Fact]
    public void FlatRateModel_IsItsModelMaximum()
    {
        var limit = ChatHistoryBudget.Resolve(null, Metadata(872000, null), true, Headroom, Factor);

        Assert.Equal(new ChatHistoryLimit(872000, ChatHistoryLimitSource.ModelMaximum), limit);
    }

    [Fact]
    public void UnknownModel_IsTheFallback()
    {
        var limit = ChatHistoryBudget.Resolve(null, Metadata(0, null), true, Headroom, Factor);

        Assert.Equal(new ChatHistoryLimit(100000, ChatHistoryLimitSource.Fallback), limit);
    }

    [Fact]
    public void SwitchedOff_IsTheModelMaximum()
    {
        var limit = ChatHistoryBudget.Resolve(null, LongContextMetadata(), false, Headroom, Factor);

        Assert.Equal(new ChatHistoryLimit(872000, ChatHistoryLimitSource.ModelMaximum), limit);
    }

    [Theory]
    [InlineData(100000)]
    [InlineData(150000)]
    public void HeadroomAtOrAboveTheThreshold_IsTheModelMaximum(int headroom)
    {
        var limit = ChatHistoryBudget.Resolve(null, LongContextMetadata(), true, headroom, Factor);

        Assert.Equal(new ChatHistoryLimit(872000, ChatHistoryLimitSource.ModelMaximum), limit);
    }

    [Fact]
    public void ZeroFactor_IsTreatedAsOne()
    {
        var limit = ChatHistoryBudget.Resolve(null, LongContextMetadata(), true, Headroom, 0m);

        Assert.Equal(new ChatHistoryLimit(60000, ChatHistoryLimitSource.LongContextBudget), limit);
    }

    [Fact]
    public void NegativeHeadroom_IsTreatedAsZero()
    {
        var limit = ChatHistoryBudget.Resolve(null, LongContextMetadata(), true, -5000, 1.0m);

        Assert.Equal(new ChatHistoryLimit(100000, ChatHistoryLimitSource.LongContextBudget), limit);
    }

    [Fact]
    public void Gpt5ShapedCard_IsBudgetedBelowItsThreshold()
    {
        var limit = ChatHistoryBudget.Resolve(null, Metadata(360000, 272000), true, Headroom, Factor);

        // (272,000 - 40,000) / 1.3 = 178,461.5, floored.
        Assert.Equal(new ChatHistoryLimit(178461, ChatHistoryLimitSource.LongContextBudget), limit);
    }
}
