using Overseer.Services;
using Xunit;

namespace Overseer.Tests.UnitTests;

public class ChatOutputBudgetTests
{
    private static ModelMetadata WithMaximum(int maxOutputTokens) => new() { MaxOutputTokens = maxOutputTokens };

    [Fact]
    public void Resolve_NoneConfigured_GivesTheCatalogMaximum()
    {
        Assert.Equal(64000, ChatOutputBudget.Resolve(null, WithMaximum(64000)));
    }

    [Fact]
    public void Resolve_ConfiguredBelowTheMaximum_GivesTheConfiguredValue()
    {
        Assert.Equal(8000, ChatOutputBudget.Resolve(8000, WithMaximum(64000)));
    }

    [Fact]
    public void Resolve_ConfiguredAboveTheMaximum_IsClampedToTheMaximum()
    {
        Assert.Equal(64000, ChatOutputBudget.Resolve(100000, WithMaximum(64000)));
    }

    [Fact]
    public void Resolve_CatalogWithoutAMaximum_GivesTheConfiguredValueOrNull()
    {
        Assert.Equal(8000, ChatOutputBudget.Resolve(8000, WithMaximum(0)));
        Assert.Null(ChatOutputBudget.Resolve(null, WithMaximum(0)));
        Assert.Null(ChatOutputBudget.Resolve(null, null));
    }
}
