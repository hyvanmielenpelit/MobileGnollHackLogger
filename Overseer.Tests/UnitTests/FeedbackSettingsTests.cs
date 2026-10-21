namespace Overseer.Tests.UnitTests;

using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Overseer.Services.Feedback;
using Xunit;

/// <summary>
/// The <c>Feedback</c> section is parsed by hand: a bad value resolves to its documented default and
/// never throws.
/// </summary>
public class FeedbackSettingsTests
{
    private static FeedbackSettings Read(string? enabled, string? cap)
    {
        var values = new Dictionary<string, string?>();
        if (enabled != null) values["Feedback:Enabled"] = enabled;
        if (cap != null) values["Feedback:MaxReportsPerUserPerDay"] = cap;
        return FeedbackSettings.Read(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData(" True ", true)]
    [InlineData("false", false)]
    [InlineData("yes", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(null, false)]
    public void Enabled_IsTrueOnlyForAParseableTrue(string? value, bool expected)
    {
        Assert.Equal(expected, Read(value, "20").Enabled);
    }

    [Theory]
    [InlineData("20", 20)]
    [InlineData(" 7 ", 7)]
    [InlineData("1", 1)]
    [InlineData("200", 200)]
    [InlineData("0", 20)]
    [InlineData("-5", 20)]
    [InlineData("201", 20)]
    [InlineData("yes", 20)]
    [InlineData("", 20)]
    [InlineData(null, 20)]
    public void TheDailyCap_FallsBackToTwenty_WhenUnparseableOrOutOfRange(string? value, int expected)
    {
        Assert.Equal(expected, Read("true", value).MaxReportsPerUserPerDay);
    }

    [Fact]
    public void AMissingSection_IsOffWithTheDefaultCap()
    {
        Assert.Equal(new FeedbackSettings(false, 20), Read(null, null));
    }
}
