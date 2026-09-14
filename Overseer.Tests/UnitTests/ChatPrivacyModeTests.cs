using Overseer.Services.Privacy;
using Xunit;

namespace Overseer.Tests.UnitTests;

public class ChatPrivacyModeTests
{
    [Theory]
    [InlineData("Standard", ChatPrivacyMode.Standard)]
    [InlineData("confidential", ChatPrivacyMode.Confidential)]
    [InlineData("INCOGNITO", ChatPrivacyMode.Incognito)]
    [InlineData(" Incognito ", ChatPrivacyMode.Incognito)]
    public void Parse_AcceptsEveryNameCaseInsensitively(string stored, ChatPrivacyMode expected)
    {
        Assert.Equal(expected, ChatPrivacyModes.Parse(stored));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Private")]
    [InlineData("Ephemeral")]
    public void Parse_ReturnsNullForAnUnrecognisedOrEmptyName(string? stored)
    {
        Assert.Null(ChatPrivacyModes.Parse(stored));
    }

    [Theory]
    [InlineData(ChatPrivacyMode.Standard)]
    [InlineData(ChatPrivacyMode.Confidential)]
    [InlineData(ChatPrivacyMode.Incognito)]
    public void ToString_RoundTripsThroughTheStoredForm(ChatPrivacyMode mode)
    {
        Assert.Equal(mode, ChatPrivacyModes.Parse(mode.ToString()));
    }
}
