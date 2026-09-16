namespace Overseer.Tests.UnitTests;

using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Controllers;
using Overseer.Models;
using Overseer.Services;
using Overseer.Services.Benchmarking;
using Xunit;

/// <summary>
/// The Save Attached Game Snapshot dialog's two endpoints: what the info call reports before a
/// save, and the version and capture time a save records.
/// </summary>
public class AdminBenchmarkAttachedSnapshotTests
{
    private const string OwnerId = "admin-1";
    private const string SnapshotText = "Dungeon Level 1\nHP: 12/60";
    private const string HandoffSettings =
        "{\"BoolData\":{\"isGameOn\":true},\"StringData\":{\"GHVersion\":\"0.9.4\",\"PortVersion\":\"4.5\",\"Platform\":\"Windows\"}}";

    private static readonly DateTime SnapshotTimestamp = new(2026, 9, 14, 18, 30, 0, DateTimeKind.Utc);

    private static (AdminBenchmarkController Controller, ApplicationDbContext Db) CreateController(string userId = OwnerId)
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options);

        // Both actions touch only the DbContext and the snapshot importer.
        var controller = new AdminBenchmarkController(
            db, null!, null!, null!, null!, null!, null!, null!, null!,
            new BenchmarkSnapshotImporter(db),
            null!, null!, null!, null!, null!, null!, null!, null!, null!)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        new[] { new Claim(ClaimTypes.NameIdentifier, userId) }, "TestAuth"))
                }
            }
        };

        return (controller, db);
    }

    private static async Task SeedSessionAsync(
        ApplicationDbContext db,
        long id,
        string ownerId = OwnerId,
        bool isConfidential = false,
        string? clientSettings = HandoffSettings,
        string? snapshotText = SnapshotText)
    {
        db.ChatSession.Add(new ChatSession
        {
            Id = id,
            AspNetUserId = ownerId,
            Title = "GnollHack Gameplay (Alice)",
            IsGnollHackSession = true,
            IsConfidential = isConfidential,
            ClientSettings = clientSettings,
            CreatedUtc = DateTime.UtcNow,
            LastMessageUtc = DateTime.UtcNow
        });

        if (snapshotText != null)
        {
            db.ChatMessage.Add(new ChatMessage
            {
                ChatSessionId = id,
                Role = "system",
                Content = ChatService.GameSnapshotPrefix + "\n" + snapshotText,
                IsGameSnapshot = true,
                TimestampUtc = SnapshotTimestamp
            });
        }

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static AttachedSnapshotInfoDto ReadInfo(IActionResult result)
        => Assert.IsType<AttachedSnapshotInfoDto>(Assert.IsType<OkObjectResult>(result).Value);

    [Fact]
    public async Task Info_WithoutASnapshot_ReportsNothingToSave()
    {
        var (controller, db) = CreateController();
        await SeedSessionAsync(db, 1, snapshotText: null);

        var info = ReadInfo(await controller.GetAttachedSnapshotInfo(1, TestContext.Current.CancellationToken));

        Assert.False(info.HasSnapshot);
        Assert.Equal(0, info.CharCount);
        Assert.Null(info.Sha256);
        Assert.Null(info.CapturedAtUtc);
        Assert.Empty(info.ExistingBoards);
    }

    [Fact]
    public async Task Info_WithASnapshot_ReportsWhatTheSaveWouldStore()
    {
        var (controller, db) = CreateController();
        await SeedSessionAsync(db, 2);

        var info = ReadInfo(await controller.GetAttachedSnapshotInfo(2, TestContext.Current.CancellationToken));
        var (expectedText, expectedSha) = BenchmarkSnapshotImporter.PrepareBoardText(
            DumpHtmlSanitizer.NormalizeFlattenedText(SnapshotText));

        Assert.True(info.HasSnapshot);
        Assert.Equal(expectedText.Length, info.CharCount);
        Assert.Equal(expectedSha, info.Sha256);
        Assert.Equal(SnapshotTimestamp, info.CapturedAtUtc);
        Assert.Equal("0.9.4", info.DetectedGnollHackVersion);
    }

    [Fact]
    public async Task Info_WithoutAReportedVersion_DetectsNone()
    {
        var (controller, db) = CreateController();
        await SeedSessionAsync(db, 3, clientSettings: "{\"BoolData\":{\"isGameOn\":true}}");

        var info = ReadInfo(await controller.GetAttachedSnapshotInfo(3, TestContext.Current.CancellationToken));

        Assert.True(info.HasSnapshot);
        Assert.Null(info.DetectedGnollHackVersion);
    }

    [Fact]
    public async Task Info_ListsBoardsSavedFromTheChat_MarkingTheIdenticalOne()
    {
        var ct = TestContext.Current.CancellationToken;
        var (controller, db) = CreateController();
        await SeedSessionAsync(db, 4);

        await new BenchmarkSnapshotImporter(db).FromSessionAttachmentAsync(
            "An earlier board", new BoardMetadata("earlier_board", SourceChatSessionId: 4), ct);
        Assert.IsType<OkObjectResult>(await controller.SaveAttachedSnapshot(
            new SaveAttachedSnapshotRequest { SessionId = 4, Name = "current_board" }, ct));
        // A board from another chat is never listed.
        await new BenchmarkSnapshotImporter(db).FromSessionAttachmentAsync(
            SnapshotText, new BoardMetadata("other_chat_board", SourceChatSessionId: 99), ct);

        var info = ReadInfo(await controller.GetAttachedSnapshotInfo(4, ct));

        Assert.Equal(2, info.ExistingBoards.Count);
        var current = Assert.Single(info.ExistingBoards, b => b.Name == "current_board");
        var earlier = Assert.Single(info.ExistingBoards, b => b.Name == "earlier_board");
        Assert.True(current.IsIdentical);
        Assert.False(earlier.IsIdentical);
        Assert.NotNull(current.SuiteId);
        Assert.Equal("Snapshot: current_board", current.SuiteName);
    }

    [Fact]
    public async Task Info_OnAConfidentialChat_Returns409()
    {
        var (controller, db) = CreateController();
        await SeedSessionAsync(db, 5, isConfidential: true);

        var result = await controller.GetAttachedSnapshotInfo(5, TestContext.Current.CancellationToken);

        Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public async Task Info_OnAnotherUsersChat_Returns403()
    {
        var (controller, db) = CreateController();
        await SeedSessionAsync(db, 6, ownerId: "someone-else");

        var result = await controller.GetAttachedSnapshotInfo(6, TestContext.Current.CancellationToken);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task Info_OnAnUnknownChat_Returns404()
    {
        var (controller, _) = CreateController();

        var result = await controller.GetAttachedSnapshotInfo(404, TestContext.Current.CancellationToken);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Save_WithoutARequestedVersion_StoresTheClientReportedOne(string? requestedVersion)
    {
        var ct = TestContext.Current.CancellationToken;
        var (controller, db) = CreateController();
        await SeedSessionAsync(db, 7);

        Assert.IsType<OkObjectResult>(await controller.SaveAttachedSnapshot(
            new SaveAttachedSnapshotRequest { SessionId = 7, Name = "board", SourceGnollHackVersion = requestedVersion }, ct));

        var board = await db.BenchmarkGameSnapshots.SingleAsync(ct);
        Assert.Equal("0.9.4", board.SourceGnollHackVersion);
    }

    [Fact]
    public async Task Save_WithARequestedVersion_StoresThatInstead()
    {
        var ct = TestContext.Current.CancellationToken;
        var (controller, db) = CreateController();
        await SeedSessionAsync(db, 8);

        Assert.IsType<OkObjectResult>(await controller.SaveAttachedSnapshot(
            new SaveAttachedSnapshotRequest { SessionId = 8, Name = "board", SourceGnollHackVersion = " 0.9.5 " }, ct));

        var board = await db.BenchmarkGameSnapshots.SingleAsync(ct);
        Assert.Equal("0.9.5", board.SourceGnollHackVersion);
    }

    [Fact]
    public async Task Save_RecordsTheSnapshotsOwnTimestamp()
    {
        var ct = TestContext.Current.CancellationToken;
        var (controller, db) = CreateController();
        await SeedSessionAsync(db, 9);

        Assert.IsType<OkObjectResult>(await controller.SaveAttachedSnapshot(
            new SaveAttachedSnapshotRequest { SessionId = 9, Name = "board" }, ct));

        var board = await db.BenchmarkGameSnapshots.SingleAsync(ct);
        Assert.Equal(SnapshotTimestamp, board.CapturedAtUtc);
        Assert.Equal(9, board.SourceChatSessionId);
    }
}
