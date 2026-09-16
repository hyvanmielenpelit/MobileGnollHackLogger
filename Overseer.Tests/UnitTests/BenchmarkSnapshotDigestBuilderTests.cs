namespace Overseer.Tests.UnitTests;

using System;
using System.Linq;
using Overseer.Services.Benchmarking;
using Xunit;

/// <summary>
/// The digest is the only board text the difficulty assessor sees, so what it keeps and what it
/// drops is pinned here against a board in the <c>dump_map_ai()</c> format.
/// </summary>
public class BenchmarkSnapshotDigestBuilderTests
{
    /// <summary>
    /// A synthetic board shaped like a real AI snapshot: preamble, the map legend with its symbol
    /// table and notable locations, the map grid, the status rows, messages, pets, inventory with
    /// a container sub-header, and the reference lists the digest omits.
    /// </summary>
    private static string BuildBoard(int inventoryLines = 4, int notableLines = 3, int messageLines = 3)
    {
        var lines = new System.Collections.Generic.List<string>
        {
            "GnollHack Version 4.1.2 built Sep 14 2026 21:03:11",
            "",
            "Game began 2026-09-14 21:10:02, snapshot at 2026-09-15 19:44:51",
            "",
            "Tommi2, neutral female gnoll Ranger",
            "",
            "Map:",
            "The map is drawn one character per cell, read left to right.",
            "The hero is at <41,9>, shown as '@'.",
            "",
            "Symbols on this map:",
            "  @  the hero (1 cell)",
            "  d  a jackal (2 cells)",
            "",
            "Notable locations:"
        };

        for (int i = 1; i <= notableLines; i++)
        {
            lines.Add($"Creature <{20 + i},9>: level 2 hostile jackal, HP:7(7) AC:7 chaotic");
        }

        lines.Add("");
        lines.Add("Map grid:");
        lines.Add("    1         2         3");
        lines.Add("    1234567890123456789012345678901234567890");
        for (int y = 0; y <= 20; y++)
        {
            lines.Add($"{y,2}: " + new string('-', 40));
        }

        lines.Add("");
        lines.Add("Status:");
        lines.Add("Tommi2 the Tenderfoot  St:14 Dx:17 Co:13 In:9 Wi:11 Ch:8  Neutral");
        lines.Add("Dlvl:3 $:412 HP:31(44) Pw:12(12) AC:5 Xp:5/241 T:2184");
        lines.Add("Hungry Burdened");
        lines.Add("");
        lines.Add("Latest messages:");
        for (int i = 1; i <= messageLines; i++)
        {
            lines.Add($"You hit the jackal. ({i})");
        }

        // The real dump puts Pets directly after the messages, with no blank line between them.
        lines.Add("Pets:");
        lines.Add("Hachi the little dog, <40,9>, distance 1, HP:18(18)");
        lines.Add("");
        lines.Add("Inventory:");
        lines.Add("a - an oriental silk sack");
        lines.Add("Contents of the oriental silk sack:");
        for (int i = 1; i <= inventoryLines; i++)
        {
            lines.Add($"  {(char)('b' + ((i - 1) % 25))} - {i} uncursed food rations");
        }

        lines.Add("");
        lines.Add("Background:");
        lines.Add("You were a Ranger of the neutral gnoll persuasion.");
        lines.Add("You were on dungeon level 3 of the Dungeons of Doom.");
        lines.Add("");
        lines.Add("Current Status:");
        lines.Add("You were burdened.");
        lines.Add("You were wielding an elven bow.");
        lines.Add("");
        lines.Add("Discoveries:");
        lines.Add("scroll of identify (unlabeled)");
        lines.Add("");
        lines.Add("Dungeon overview:");
        lines.Add("Level 3: an altar");

        return string.Join("\n", lines);
    }

    [Fact]
    public void Build_EmitsThePreambleAndEveryKnownSectionInOrder()
    {
        string digest = BenchmarkSnapshotDigestBuilder.Build(BuildBoard());

        Assert.StartsWith(BenchmarkSnapshotDigestBuilder.DigestHeaderLine, digest);
        Assert.Contains("GnollHack Version 4.1.2", digest);
        Assert.Contains("Tommi2, neutral female gnoll Ranger", digest);

        int[] positions =
        {
            digest.IndexOf("Status:", StringComparison.Ordinal),
            digest.IndexOf("Background:", StringComparison.Ordinal),
            digest.IndexOf("Current Status:", StringComparison.Ordinal),
            digest.IndexOf("Latest messages:", StringComparison.Ordinal),
            digest.IndexOf("Pets:", StringComparison.Ordinal),
            digest.IndexOf("Notable locations:", StringComparison.Ordinal),
            digest.IndexOf("Inventory:", StringComparison.Ordinal)
        };

        Assert.DoesNotContain(-1, positions);
        Assert.Equal(positions.OrderBy(p => p).ToArray(), positions);
    }

    [Fact]
    public void Build_OmitsTheMapGridTheLegendAndTheReferenceLists()
    {
        string digest = BenchmarkSnapshotDigestBuilder.Build(BuildBoard());

        Assert.DoesNotContain("Map grid:", digest);
        Assert.DoesNotContain("Symbols on this map:", digest);
        Assert.DoesNotContain("Discoveries:", digest);
        Assert.DoesNotContain("Dungeon overview:", digest);
        Assert.DoesNotContain("----------", digest);
    }

    [Fact]
    public void Build_KeepsContainerSubHeadersInsideTheInventory()
    {
        string digest = BenchmarkSnapshotDigestBuilder.Build(BuildBoard());

        Assert.Contains("Contents of the oriental silk sack:", digest);
        Assert.Contains("b - 1 uncursed food rations", digest);
    }

    [Fact]
    public void Build_EndsLatestMessagesAtTheNextKnownHeader()
    {
        // The dump writes Pets: directly after the messages with no blank line between them.
        string digest = BenchmarkSnapshotDigestBuilder.Build(BuildBoard(messageLines: 2));

        int messages = digest.IndexOf("Latest messages:", StringComparison.Ordinal);
        int pets = digest.IndexOf("Pets:", StringComparison.Ordinal);
        string between = digest.Substring(messages, pets - messages);

        Assert.Contains("You hit the jackal. (2)", between);
        Assert.DoesNotContain("Hachi the little dog", between);
    }

    [Fact]
    public void Build_KeepsTheLastFiveMessagesAndCountsTheRest()
    {
        string digest = BenchmarkSnapshotDigestBuilder.Build(BuildBoard(messageLines: 9));

        Assert.DoesNotContain("You hit the jackal. (4)", digest);
        Assert.Contains("You hit the jackal. (5)", digest);
        Assert.Contains("You hit the jackal. (9)", digest);
        Assert.Contains("  (+4 more lines)", digest);
    }

    [Fact]
    public void Build_CapsTheInventoryAndReportsHowManyLinesItDropped()
    {
        // 2 lines before the container contents, so 80 body lines against the cap of 60.
        string digest = BenchmarkSnapshotDigestBuilder.Build(BuildBoard(inventoryLines: 78));

        Assert.Contains("  (+20 more lines)", digest);
    }

    [Fact]
    public void Build_StaysUnderTheCapForAFullSizeBoard()
    {
        string board = BuildBoard(inventoryLines: 500, notableLines: 400, messageLines: 40);
        Assert.True(board.Length > 30000, $"fixture is only {board.Length} characters");

        string digest = BenchmarkSnapshotDigestBuilder.Build(board);

        Assert.True(
            digest.Length <= BenchmarkSnapshotDigestBuilder.MaxDigestChars,
            $"digest was {digest.Length} characters");
        Assert.StartsWith(BenchmarkSnapshotDigestBuilder.DigestHeaderLine, digest);
    }

    [Fact]
    public void Build_FallsBackToThePrefixWhenNoKnownHeaderIsPresent()
    {
        string firstPart = new string('x', 5500);
        string secondPart = new string('y', 900);
        string text = firstPart + "\n" + secondPart;

        string digest = BenchmarkSnapshotDigestBuilder.Build(text);

        Assert.Equal(firstPart, digest);
    }

    [Fact]
    public void Build_ReturnsShortHeaderlessTextUnchanged()
    {
        Assert.Equal("HP: 12/60\nturn: 120", BenchmarkSnapshotDigestBuilder.Build("HP: 12/60\nturn: 120"));
    }

    [Fact]
    public void Build_IsDeterministic()
    {
        string board = BuildBoard(inventoryLines: 120, notableLines: 90);

        Assert.Equal(
            BenchmarkSnapshotDigestBuilder.Build(board),
            BenchmarkSnapshotDigestBuilder.Build(board));
    }
}
