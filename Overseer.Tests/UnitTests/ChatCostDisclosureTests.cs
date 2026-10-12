namespace Overseer.Tests.UnitTests;

using Overseer.Services;
using Xunit;

public class ChatCostDisclosureTests
{
    // ---- New-shape rows, with OperatorEstimatedCost written ----

    [Fact]
    public void Admin_SeesWholeTurnAndBothShares()
    {
        var d = ChatCostDisclosure.For(true, 0.07m, "mixed", 5, operatorEstimatedCost: 0.05m, subAgentEstimatedCost: 0.02m);

        Assert.Equal(0.07m, d.EstimatedCost);
        Assert.Equal("mixed", d.PricingSource);
        Assert.Equal(0.05m, d.OperatorCost);
        Assert.Equal(0.02m, d.SubAgentCost);
        Assert.True(d.IsOperatorCost);
    }

    [Fact]
    public void RegularUser_PartOperator_SeesOnlyTheirShareAndNoSubAgentFigure()
    {
        var d = ChatCostDisclosure.For(false, 0.07m, "catalog", null, operatorEstimatedCost: 0.02m, subAgentEstimatedCost: 0.02m);

        Assert.Equal(0.05m, d.EstimatedCost);
        Assert.Equal("catalog", d.PricingSource);
        Assert.Null(d.OperatorCost);
        Assert.Null(d.SubAgentCost);
        Assert.True(d.IsOperatorCost);
    }

    [Fact]
    public void RegularUser_AllOperator_SeesNothingButTheWithheldFlag()
    {
        var d = ChatCostDisclosure.For(false, 0.07m, "catalog", 5, operatorEstimatedCost: 0.07m, subAgentEstimatedCost: 0.02m);

        Assert.Null(d.EstimatedCost);
        Assert.Null(d.PricingSource);
        Assert.Null(d.OperatorCost);
        Assert.Null(d.SubAgentCost);
        Assert.True(d.IsOperatorCost);
    }

    [Fact]
    public void RegularUser_NoOperatorShare_SeesWholeTurnAndSubAgentShare()
    {
        var d = ChatCostDisclosure.For(false, 0.07m, "catalog", null, operatorEstimatedCost: null, subAgentEstimatedCost: 0.02m);

        Assert.Equal(0.07m, d.EstimatedCost);
        Assert.Equal("catalog", d.PricingSource);
        Assert.Null(d.OperatorCost);
        Assert.Equal(0.02m, d.SubAgentCost);
        Assert.False(d.IsOperatorCost);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UserModelTurnWithOperatorFundedSubAgent_IsOperatorCostForBothViewers(bool admin)
    {
        var d = ChatCostDisclosure.For(admin, 0.07m, "catalog", null, operatorEstimatedCost: 0.02m, subAgentEstimatedCost: 0.02m);

        Assert.True(d.IsOperatorCost);
    }

    // ---- Legacy rows: OperatorEstimatedCost null, read through SystemAiConfigurationIdUsed ----

    [Fact]
    public void Legacy_SystemConfigRow_RegularUserGetsNullAndTheWithheldFlag()
    {
        var d = ChatCostDisclosure.For(false, 0.05m, "catalog", 5, null, null);

        Assert.Null(d.EstimatedCost);
        Assert.Null(d.PricingSource);
        Assert.True(d.IsOperatorCost);
    }

    [Fact]
    public void Legacy_SystemConfigRow_AdminGetsTheWhole()
    {
        var d = ChatCostDisclosure.For(true, 0.05m, "catalog", 5, null, null);

        Assert.Equal(0.05m, d.EstimatedCost);
        Assert.Equal("catalog", d.PricingSource);
        Assert.Equal(0.05m, d.OperatorCost);
        Assert.Null(d.SubAgentCost);
        Assert.True(d.IsOperatorCost);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Legacy_UserRow_BothViewersGetTheWhole(bool admin)
    {
        var d = ChatCostDisclosure.For(admin, 0.05m, "custom", null, null, null);

        Assert.Equal(0.05m, d.EstimatedCost);
        Assert.Equal("custom", d.PricingSource);
        Assert.Null(d.OperatorCost);
        Assert.False(d.IsOperatorCost);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Legacy_UnpricedSystemConfigRow_StaysOperatorCostSoPartialGlyphStaysOff(bool admin)
    {
        var d = ChatCostDisclosure.For(admin, null, null, 5, null, null);

        Assert.Null(d.EstimatedCost);
        Assert.True(d.IsOperatorCost);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Legacy_UnpricedUserRow_IsNotOperatorCost(bool admin)
    {
        var d = ChatCostDisclosure.For(admin, null, null, null, null, null);

        Assert.Null(d.EstimatedCost);
        Assert.False(d.IsOperatorCost);
    }
}
