namespace Overseer.Tests.UnitTests;

using System;
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
/// PUT snapshots/{id}/text: normalizing, hashing, counting and rebuilding the digest for a
/// hand-edited snapshot, with an optimistic-concurrency check against a stale Sha256.
/// </summary>
public class AdminBenchmarkSnapshotTextEditTests
{
    private const string OwnerId = "admin-1";

    private static (AdminBenchmarkController Controller, ApplicationDbContext Db) CreateController()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options);

        // The action touches only the DbContext.
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
                        new[] { new Claim(ClaimTypes.NameIdentifier, OwnerId) }, "TestAuth"))
                }
            }
        };

        return (controller, db);
    }

    private static async Task<BenchmarkGameSnapshot> SeedSnapshotAsync(
        ApplicationDbContext db, string name, string text, DateTime? modifiedAtUtc = null)
    {
        var importer = new BenchmarkSnapshotImporter(db);
        var (board, _) = await importer.FromClientTextAsync(text, new BoardMetadata(name), TestContext.Current.CancellationToken);
        if (modifiedAtUtc != null)
        {
            board.ModifiedAtUtc = modifiedAtUtc.Value;
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        return board;
    }

    private static UpdateBenchmarkSnapshotTextResponse ReadResponse(IActionResult result)
        => Assert.IsType<UpdateBenchmarkSnapshotTextResponse>(Assert.IsType<OkObjectResult>(result).Value);

    private static BenchmarkGameSnapshotDto ReadDto(IActionResult result)
        => ReadResponse(result).Snapshot;

    [Fact]
    public async Task SavesEditedText_NormalizesHashesCountsAndRebuildsTheDigest()
    {
        var ct = TestContext.Current.CancellationToken;
        var (controller, db) = CreateController();
        var oldModified = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var board = await SeedSnapshotAsync(db, "edit_target", "Dlvl:1 $:0 HP:12(12)", oldModified);

        var result = await controller.UpdateSnapshotText(
            board.Id, new UpdateBenchmarkGameSnapshotTextRequest { Text = "Dlvl:2 $:50 HP:20(20)" }, ct);

        var dto = ReadDto(result);
        string normalized = DumpHtmlSanitizer.NormalizeFlattenedText("Dlvl:2 $:50 HP:20(20)");
        var (expectedText, expectedSha256) = BenchmarkSnapshotImporter.PrepareBoardText(normalized);

        Assert.Equal(expectedSha256, dto.Sha256);
        Assert.Equal(expectedText.Length, dto.CharCount);
        Assert.Equal(expectedText, dto.SanitizedText);
        Assert.Equal(BenchmarkSnapshotDigestBuilder.Build(expectedText), dto.DigestText);
        Assert.Equal("ClientRefresh", dto.CaptureMethod);

        var stored = await db.BenchmarkGameSnapshots.SingleAsync(s => s.Id == board.Id, ct);
        Assert.Equal(expectedSha256, stored.Sha256);
        Assert.Equal(expectedText, stored.SanitizedText);
        Assert.Equal(BenchmarkSnapshotDigestBuilder.Build(expectedText), stored.DigestText);
        Assert.Equal("ClientRefresh", stored.CaptureMethod);
        Assert.True(stored.ModifiedAtUtc > oldModified);
    }

    [Fact]
    public async Task CrlfInput_IsStoredWithLf_AndHashesLikeItsLfForm()
    {
        var ct = TestContext.Current.CancellationToken;
        var (controller, db) = CreateController();
        var board = await SeedSnapshotAsync(db, "crlf_target", "placeholder");

        string crlfText = "Header line\r\nMiddle line\r\n\r\n\r\nFooter line";
        string lfText = "Header line\nMiddle line\n\n\nFooter line";
        var (_, expectedSha256) = BenchmarkSnapshotImporter.PrepareBoardText(DumpHtmlSanitizer.NormalizeFlattenedText(lfText));

        var result = await controller.UpdateSnapshotText(
            board.Id, new UpdateBenchmarkGameSnapshotTextRequest { Text = crlfText }, ct);

        var dto = ReadDto(result);
        Assert.Equal(expectedSha256, dto.Sha256);
        Assert.DoesNotContain('\r', dto.SanitizedText!);
        Assert.Equal("Header line\nMiddle line\n\nFooter line", dto.SanitizedText);
    }

    [Fact]
    public async Task WhitespaceOnlyText_Returns400_AndLeavesTheStoredSnapshotUntouched()
    {
        var ct = TestContext.Current.CancellationToken;
        var (controller, db) = CreateController();
        var board = await SeedSnapshotAsync(db, "whitespace_target", "Dlvl:1 $:0 HP:12(12)");
        string originalText = board.SanitizedText;
        string originalSha256 = board.Sha256;

        var result = await controller.UpdateSnapshotText(
            board.Id, new UpdateBenchmarkGameSnapshotTextRequest { Text = "   \n\t  " }, ct);

        Assert.IsType<BadRequestObjectResult>(result);
        var stored = await db.BenchmarkGameSnapshots.SingleAsync(s => s.Id == board.Id, ct);
        Assert.Equal(originalText, stored.SanitizedText);
        Assert.Equal(originalSha256, stored.Sha256);
    }

    [Fact]
    public async Task TextOverTheMax_IsCutWithTheTruncationMarker_AndCharCountReflectsTheStoredLength()
    {
        var ct = TestContext.Current.CancellationToken;
        var (controller, db) = CreateController();
        var board = await SeedSnapshotAsync(db, "truncation_target", "placeholder");
        string largeText = new string('a', BenchmarkSnapshotImporter.DefaultMaxSnapshotChars + 500);

        var result = await controller.UpdateSnapshotText(
            board.Id, new UpdateBenchmarkGameSnapshotTextRequest { Text = largeText }, ct);

        var dto = ReadDto(result);
        Assert.Contains("[SNAPSHOT TRUNCATED at 60000 characters.]", dto.SanitizedText);
        Assert.StartsWith(new string('a', BenchmarkSnapshotImporter.DefaultMaxSnapshotChars), dto.SanitizedText);
        Assert.Equal(dto.SanitizedText!.Length, dto.CharCount);
    }

    [Fact]
    public async Task StaleExpectedSha256_Returns409_AndWritesNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var (controller, db) = CreateController();
        var board = await SeedSnapshotAsync(db, "stale_target", "Dlvl:1 $:0 HP:12(12)");
        string originalText = board.SanitizedText;
        string originalSha256 = board.Sha256;

        var result = await controller.UpdateSnapshotText(
            board.Id,
            new UpdateBenchmarkGameSnapshotTextRequest { Text = "Dlvl:2 $:50 HP:20(20)", ExpectedSha256 = "not-the-real-hash" },
            ct);

        Assert.IsType<ConflictObjectResult>(result);
        var stored = await db.BenchmarkGameSnapshots.SingleAsync(s => s.Id == board.Id, ct);
        Assert.Equal(originalText, stored.SanitizedText);
        Assert.Equal(originalSha256, stored.Sha256);
    }

    [Fact]
    public async Task MatchingExpectedSha256_Returns200_AndSaves()
    {
        var ct = TestContext.Current.CancellationToken;
        var (controller, db) = CreateController();
        var board = await SeedSnapshotAsync(db, "matching_target", "Dlvl:1 $:0 HP:12(12)");
        string originalSha256 = board.Sha256;

        var result = await controller.UpdateSnapshotText(
            board.Id,
            new UpdateBenchmarkGameSnapshotTextRequest { Text = "Dlvl:2 $:50 HP:20(20)", ExpectedSha256 = originalSha256 },
            ct);

        Assert.IsType<OkObjectResult>(result);
        var stored = await db.BenchmarkGameSnapshots.SingleAsync(s => s.Id == board.Id, ct);
        Assert.NotEqual(originalSha256, stored.Sha256);
    }

    [Fact]
    public async Task UnknownId_Returns404()
    {
        var (controller, _) = CreateController();

        var result = await controller.UpdateSnapshotText(
            404, new UpdateBenchmarkGameSnapshotTextRequest { Text = "Dlvl:1" }, TestContext.Current.CancellationToken);

        Assert.IsType<NotFoundResult>(result);
    }

    // --- BOARD FACTS quote check -------------------------------------------------------------

    [Fact]
    public async Task EditedText_ReturnsTheCheckForTheOwningSuite_AgainstTheNewText()
    {
        var ct = TestContext.Current.CancellationToken;
        var (controller, db) = CreateController();
        var importer = new BenchmarkSnapshotImporter(db);
        var (board, suite) = await importer.FromClientTextAsync(
            "T - the Holy Grail (0 charges, 0 rechargings)", new BoardMetadata("owned_target"), ct);
        const string rubric = "**BOARD FACTS**\n- The inventory lists \"T - the Holy Grail (0 charges, 0 rechargings)\".";
        db.BenchmarkQuestions.Add(new BenchmarkQuestion
        {
            BenchmarkSuiteId = suite.Id,
            OrderIndex = 6,
            QuestionText = "Q6",
            ExpectedPoints = rubric
        });
        await db.SaveChangesAsync(ct);

        var response = ReadResponse(await controller.UpdateSnapshotText(
            board.Id,
            new UpdateBenchmarkGameSnapshotTextRequest { Text = "T - the uncursed Holy Grail (0 charges, 0 rechargings)" },
            ct));

        Assert.Equal(board.Id, response.Snapshot.Id);
        var check = Assert.IsType<BoardFactsCheckDto>(response.BoardFactsCheck);
        Assert.Equal(1, check.CheckedLiteralCount);
        var missing = Assert.Single(check.MissingLiterals);
        Assert.Equal(6, missing.OrderIndex);
        Assert.Equal("T - the Holy Grail (0 charges, 0 rechargings)", missing.Literal);

        // Advisory only: the rubric is not touched.
        Assert.Equal(rubric, (await db.BenchmarkQuestions.AsNoTracking().SingleAsync(ct)).ExpectedPoints);
    }

    [Fact]
    public async Task EditedText_OfASnapshotNoSuiteOwns_ReturnsANullCheck()
    {
        var ct = TestContext.Current.CancellationToken;
        var (controller, db) = CreateController();
        var board = new BenchmarkGameSnapshot
        {
            Name = "orphan",
            SanitizedText = "Dlvl:1",
            CharCount = 6,
            Sha256 = "sha",
            CaptureMethod = "TextUpload"
        };
        db.BenchmarkGameSnapshots.Add(board);
        await db.SaveChangesAsync(ct);

        var response = ReadResponse(await controller.UpdateSnapshotText(
            board.Id, new UpdateBenchmarkGameSnapshotTextRequest { Text = "Dlvl:2" }, ct));

        Assert.Null(response.Snapshot.SuiteId);
        Assert.Null(response.BoardFactsCheck);
    }
}
