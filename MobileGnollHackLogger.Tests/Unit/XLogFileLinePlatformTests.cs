using MobileGnollHackLogger.Data;
using Xunit;

namespace Overseer.Tests.UnitTests;

public class XLogFileLinePlatformTests
{
    [Theory]
    [InlineData("android", "a", "Android")]
    [InlineData("ios", "i", "iOS")]
    [InlineData("maccatalyst", "m", "Mac Catalyst")]
    [InlineData("macos", "m", "macOS")]
    [InlineData("winui", "w", "Windows")]
    [InlineData("windows", "w", "Windows")]
    [InlineData("linux", "l", "Linux")]
    [InlineData("freebsd", "o", "freebsd")]
    [InlineData("", "", "")]
    [InlineData(null, null, null)]
    public void Platform_MapsToLetterAndDisplayName(string? platform, string? expectedLetter, string? expectedText)
    {
        var line = new XLogFileLine { Platform = platform };

        Assert.Equal(expectedLetter, line.PlatformLetter);
        Assert.Equal(expectedText, line.PlatformText);
    }
}
