using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Services;
using Overseer.Services.Benchmarking;
using Xunit;

namespace Overseer.Tests.UnitTests;

public class BenchmarkSnapshotImporterTests
{
    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task Capture_CreatesBoardAndLinkedEmptySuite()
    {
        using var db = CreateDbContext();
        var importer = new BenchmarkSnapshotImporter(db);

        var meta = new BoardMetadata("emergency_low_hp", "Adjacent monsters", "0.9.4");
        var (snapshot, suite) = await importer.FromClientTextAsync("HP: 12/60\nturn: 120", meta);

        Assert.NotNull(snapshot);
        Assert.NotNull(suite);
        Assert.Equal("emergency_low_hp", snapshot.Name);
        Assert.Equal("ClientRefresh", snapshot.CaptureMethod);
        Assert.Equal("Board: emergency_low_hp", suite.Name);
        Assert.Equal(snapshot.Id, suite.GameSnapshotId);
        Assert.False(suite.HasGeneratedQuestions);
        Assert.Empty(suite.Questions);

        var profile = await db.BenchmarkScoringProfiles.FirstOrDefaultAsync(p => p.Name == "Situational Advisor");
        Assert.NotNull(profile);
        Assert.Equal(25000, profile.SpeedTargetMs);
        Assert.Equal(20.0, profile.SpeedDecayK);
    }

    [Fact]
    public async Task CollidingSuiteName_AppendsCounter()
    {
        using var db = CreateDbContext();
        db.BenchmarkSuites.Add(new BenchmarkSuite { Name = "Board: dup_test" });
        await db.SaveChangesAsync();

        var importer = new BenchmarkSnapshotImporter(db);
        var (snapshot, suite) = await importer.FromClientTextAsync("HP: 50", new BoardMetadata("dup_test"));

        Assert.Equal("Board: dup_test (2)", suite.Name);
    }

    [Fact]
    public async Task DuplicateBoardName_AutoSuffixes()
    {
        using var db = CreateDbContext();
        var importer = new BenchmarkSnapshotImporter(db);
        var (b1, s1) = await importer.FromClientTextAsync("HP: 50", new BoardMetadata("same_board"));
        var (b2, s2) = await importer.FromClientTextAsync("HP: 60", new BoardMetadata("same_board"));
        var (b3, s3) = await importer.FromClientTextAsync("HP: 70", new BoardMetadata("same_board"));

        Assert.Equal("same_board", b1.Name);
        Assert.Equal("Board: same_board", s1.Name);

        Assert.Equal("same_board (2)", b2.Name);
        Assert.Equal("Board: same_board (2)", s2.Name);

        Assert.Equal("same_board (3)", b3.Name);
        Assert.Equal("Board: same_board (3)", s3.Name);
    }

    [Fact]
    public async Task FromSessionAttachmentAsync_StampsCaptureMethodAndPersistsSourceChatSessionId()
    {
        using var db = CreateDbContext();
        var importer = new BenchmarkSnapshotImporter(db);
        var meta = new BoardMetadata("session_attached_board", Notes: "Admin note", SourceGnollHackVersion: "1.0", SourceChatSessionId: 42);
        var (board, suite) = await importer.FromSessionAttachmentAsync("Attached snapshot text\nMore details", meta);

        Assert.Equal("SessionAttachment", board.CaptureMethod);
        Assert.Equal(42, board.SourceChatSessionId);
        Assert.Equal("Admin note", board.Notes);
        Assert.Equal("session_attached_board", board.Name);
        Assert.Equal("Board: session_attached_board", suite.Name);
    }

    [Fact]
    public async Task Determinism_SameInputYieldsSameSha256()
    {
        using var db = CreateDbContext();
        var importer = new BenchmarkSnapshotImporter(db);

        string boardText = "Dungeon Level 1\n.......";
        var (b1, _) = await importer.FromClientTextAsync(boardText, new BoardMetadata("board1"));
        var (b2, _) = await importer.FromClientTextAsync(boardText, new BoardMetadata("board2"));

        Assert.Equal(b1.Sha256, b2.Sha256);
    }

    [Fact]
    public async Task TextOver60k_TruncatesAt60kWithMarker()
    {
        using var db = CreateDbContext();
        var importer = new BenchmarkSnapshotImporter(db);

        string largeText = new string('a', 70000);
        var (board, _) = await importer.FromClientTextAsync(largeText, new BoardMetadata("large_board"));

        Assert.Contains("[SNAPSHOT TRUNCATED at 60000 characters.]", board.SanitizedText);
        Assert.StartsWith(new string('a', 60000), board.SanitizedText);
    }

    [Fact]
    public async Task PrefixedStoredText_SatisfiesIsGameSnapshotMessage()
    {
        using var db = CreateDbContext();
        var importer = new BenchmarkSnapshotImporter(db);

        var (board, _) = await importer.FromClientTextAsync("Level 1", new BoardMetadata("message_test"));
        string message = ChatService.GameSnapshotPrefix + "\n" + board.SanitizedText;

        Assert.True(ChatService.IsGameSnapshotMessage(message));
    }

    [Fact]
    public async Task EmptyOrWhitespaceInput_ThrowsArgumentException()
    {
        using var db = CreateDbContext();
        var importer = new BenchmarkSnapshotImporter(db);

        await Assert.ThrowsAsync<ArgumentException>(
            () => importer.FromClientTextAsync("    \n\t ", new BoardMetadata("empty")));
    }

    [Fact]
    public async Task DigestText_IsSeeded_AndCutAtLineBoundary()
    {
        using var db = CreateDbContext();
        var importer = new BenchmarkSnapshotImporter(db);

        string firstPart = new string('x', 1500);
        string secondPart = new string('y', 700);
        string fullText = firstPart + "\n" + secondPart;

        var (board, _) = await importer.FromClientTextAsync(fullText, new BoardMetadata("digest_test"));

        Assert.True(board.DigestText!.Length <= 2000);
        Assert.Equal(firstPart, board.DigestText);
    }

    [Fact]
    public async Task DocumentedSanitizerDivergence_BetweenClientAndHtmlSanitize()
    {
        using var db = CreateDbContext();
        var importer = new BenchmarkSnapshotImporter(db);

        // R8: table rows have a pre-existing divergence between server sanitizer
        // (which has <tr> in block-open) and client (which does not, producing extra newlines).
        string html = "<table><tr><td>Force bolt</td><td>75%</td></tr></table>";
        string clientShapedText = "Force bolt 75%\n\nExtra row";

        var (boardHtml, _) = await importer.FromRawHtmlAsync(html, new BoardMetadata("html_test"));
        var (boardClient, _) = await importer.FromClientTextAsync(clientShapedText, new BoardMetadata("client_test"));

        Assert.NotEqual(boardHtml.Sha256, boardClient.Sha256);
    }
}
