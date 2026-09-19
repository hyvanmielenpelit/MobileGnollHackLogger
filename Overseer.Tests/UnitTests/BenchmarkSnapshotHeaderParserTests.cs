namespace Overseer.Tests.UnitTests;

using Overseer.Services.Benchmarking;
using Xunit;

/// <summary>
/// The header a GnollHack AI snapshot states about itself is read only from the first 12 lines,
/// since everything past that is board content an operator pasted in.
/// </summary>
public class BenchmarkSnapshotHeaderParserTests
{
    private const string Format3Header =
        "GnollHack AI Snapshot\n" +
        "\n" +
        "Snapshot format: 3\n" +
        "Windows GnollHack Version 4.3.0 (Build 20) - last build Fri Sep 18 14:52:42 2026.\n" +
        "Game began 2026-07-12 22:56:18, snapshot at 2026-09-18 15:09:58\n" +
        "Tommi2, lawful male human Monk\n" +
        "Map:\n";

    [Fact]
    public void Format3Header_ParsesVersionTimestampAndFormat()
    {
        var header = BenchmarkSnapshotHeaderParser.Parse(Format3Header);

        Assert.Equal(3, header.SnapshotFormatVersion);
        Assert.Equal("4.3.0 (Build 20)", header.GnollHackVersion);
        Assert.Equal("2026-09-18 15:09:58", header.SnapshotTimestamp);
    }

    [Fact]
    public void CrlfHeader_ParsesTheSameAsLf()
    {
        string crlfHeader = Format3Header.Replace("\n", "\r\n");

        var header = BenchmarkSnapshotHeaderParser.Parse(crlfHeader);

        Assert.Equal(3, header.SnapshotFormatVersion);
        Assert.Equal("4.3.0 (Build 20)", header.GnollHackVersion);
        Assert.Equal("2026-09-18 15:09:58", header.SnapshotTimestamp);
    }

    [Fact]
    public void Format1Header_WithNoFormatLine_GivesNullFormat()
    {
        const string format1Header =
            "GnollHack AI Snapshot\n" +
            "\n" +
            "Windows GnollHack Version 4.3.0 (Build 19) - last build Fri Sep 11 14:52:42 2026.\n" +
            "Game began 2026-07-12 22:56:18, snapshot at 2026-09-11 15:09:58\n" +
            "Tommi2, lawful male human Monk\n" +
            "Map:\n";

        var header = BenchmarkSnapshotHeaderParser.Parse(format1Header);

        Assert.Null(header.SnapshotFormatVersion);
        Assert.Equal("4.3.0 (Build 19)", header.GnollHackVersion);
        Assert.Equal("2026-09-11 15:09:58", header.SnapshotTimestamp);
    }

    [Fact]
    public void FormatLine_OnLine13OrLater_IsIgnored()
    {
        string text = string.Join("\n",
            "line 1", "line 2", "line 3", "line 4", "line 5", "line 6",
            "line 7", "line 8", "line 9", "line 10", "line 11", "line 12",
            "Snapshot format: 9");

        var header = BenchmarkSnapshotHeaderParser.Parse(text);

        Assert.Null(header.SnapshotFormatVersion);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("asldkjhq09w8ueoaisjdlkajsd")]
    public void NullEmptyOrGarbageInput_ReturnsAllNulls_WithoutThrowing(string? text)
    {
        var header = BenchmarkSnapshotHeaderParser.Parse(text);

        Assert.Null(header.SnapshotFormatVersion);
        Assert.Null(header.GnollHackVersion);
        Assert.Null(header.SnapshotTimestamp);
    }

    [Fact]
    public void VersionLine_OnLine13OrLater_DoesNotMatch()
    {
        string text = string.Join("\n",
            "line 1", "line 2", "line 3", "line 4", "line 5", "line 6",
            "line 7", "line 8", "line 9", "line 10", "line 11", "line 12",
            "Windows GnollHack Version 4.3.0 (Build 20) - last build Fri Sep 18 14:52:42 2026.");

        var header = BenchmarkSnapshotHeaderParser.Parse(text);

        Assert.Null(header.GnollHackVersion);
    }
}
