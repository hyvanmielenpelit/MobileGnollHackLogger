using Overseer.Controllers;
using Overseer.Services;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>Covers the path that carries the GnollHack host's game state from the
/// OverseerSettings JSON stored on the handoff session to the SPA's gameOn parameter.</summary>
public class ClientSettingsReaderTests
{
    /* The shape the GnollHack client actually posts, so a key is read out of a realistic
       payload rather than a one-key object built to match the reader. */
    private const string RealisticSettings = """
        {
          "BoolData": {
            "allowSpoilers": true,
            "verboseResponses": false,
            "isGameOn": true,
            "sendGameContext": true,
            "enableClientTools": true
          },
          "IntData": { "overseerMode": 0 },
          "StringData": { "version": "4.5.1" }
        }
        """;

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ReadBool_ReturnsNull_ForEmptyJson(string? json)
    {
        Assert.Null(ClientSettingsReader.ReadBool(json, "isGameOn"));
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{\"BoolData\": {")]
    [InlineData("{ \"BoolData\": { \"isGameOn\": tru } }")]
    public void ReadBool_ReturnsNull_ForMalformedJson(string json)
    {
        Assert.Null(ClientSettingsReader.ReadBool(json, "isGameOn"));
    }

    /* TryGetProperty throws on a non-object element rather than answering false, so a valid
       JSON document of the wrong shape is a separate case from malformed text. */
    [Theory]
    [InlineData("[1, 2, 3]")]
    [InlineData("42")]
    [InlineData("{ \"BoolData\": \"isGameOn\" }")]
    [InlineData("{ \"BoolData\": [] }")]
    public void ReadBool_ReturnsNull_ForWrongShape(string json)
    {
        Assert.Null(ClientSettingsReader.ReadBool(json, "isGameOn"));
    }

    [Fact]
    public void ReadBool_ReturnsNull_WhenBoolDataIsMissing()
    {
        Assert.Null(ClientSettingsReader.ReadBool("{ \"IntData\": { \"overseerMode\": 0 } }", "isGameOn"));
    }

    [Fact]
    public void ReadBool_ReturnsNull_WhenKeyIsMissing()
    {
        Assert.Null(ClientSettingsReader.ReadBool("{ \"BoolData\": { \"allowSpoilers\": true } }", "isGameOn"));
    }

    [Fact]
    public void ReadBool_ReturnsTrue_ForTrue()
    {
        Assert.True(ClientSettingsReader.ReadBool("{ \"BoolData\": { \"isGameOn\": true } }", "isGameOn"));
    }

    [Fact]
    public void ReadBool_ReturnsFalse_ForFalse()
    {
        Assert.False(ClientSettingsReader.ReadBool("{ \"BoolData\": { \"isGameOn\": false } }", "isGameOn"));
    }

    /* A number or a string is not a boolean and must not be coerced into one: a host that
       sends 1 has told us nothing we can act on. */
    [Theory]
    [InlineData("{ \"BoolData\": { \"isGameOn\": 1 } }")]
    [InlineData("{ \"BoolData\": { \"isGameOn\": \"true\" } }")]
    [InlineData("{ \"BoolData\": { \"isGameOn\": null } }")]
    public void ReadBool_ReturnsNull_ForNonBooleanValue(string json)
    {
        Assert.Null(ClientSettingsReader.ReadBool(json, "isGameOn"));
    }

    [Fact]
    public void ReadBool_ReadsEachKey_FromARealisticPayload()
    {
        Assert.True(ClientSettingsReader.ReadBool(RealisticSettings, "isGameOn"));
        Assert.True(ClientSettingsReader.ReadBool(RealisticSettings, "sendGameContext"));
        Assert.False(ClientSettingsReader.ReadBool(RealisticSettings, "verboseResponses"));
        Assert.Null(ClientSettingsReader.ReadBool(RealisticSettings, "overseerMode"));
    }
}

/// <summary>The three rows of the handoff design table, plus the opt-out that overrides a
/// running game.</summary>
public class ResolveGameOnFlagTests
{
    [Fact]
    public void ReturnsOne_WhenGameIsOnAndContextIsNotOptedOut()
    {
        Assert.Equal("1", AuthController.ResolveGameOnFlag("{ \"BoolData\": { \"isGameOn\": true } }"));
        Assert.Equal("1", AuthController.ResolveGameOnFlag("{ \"BoolData\": { \"isGameOn\": true, \"sendGameContext\": true } }"));
    }

    [Fact]
    public void ReturnsZero_WhenNoGameIsRunning()
    {
        Assert.Equal("0", AuthController.ResolveGameOnFlag("{ \"BoolData\": { \"isGameOn\": false } }"));
    }

    /* refresh_snapshot throws for an opted-out player too, so the button would produce the
       same error toast it does with no game running. */
    [Fact]
    public void ReturnsZero_WhenGameContextIsOptedOut()
    {
        Assert.Equal("0", AuthController.ResolveGameOnFlag("{ \"BoolData\": { \"sendGameContext\": false } }"));
        Assert.Equal("0", AuthController.ResolveGameOnFlag("{ \"BoolData\": { \"isGameOn\": true, \"sendGameContext\": false } }"));
    }

    /* A GnollHack build predating the isGameOn field must keep the button it has today. */
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{ \"BoolData\": { \"allowSpoilers\": true } }")]
    [InlineData("not json at all")]
    public void ReturnsNull_WhenTheHostReportedNothing(string? clientSettings)
    {
        Assert.Null(AuthController.ResolveGameOnFlag(clientSettings));
    }
}
