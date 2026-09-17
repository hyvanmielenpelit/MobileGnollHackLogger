namespace Overseer.Tests.UnitTests;

using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services.Benchmarking;
using Xunit;

/// <summary>
/// POST snapshots/match: the read-only preflight the suite import review step uses to say whether
/// an identical snapshot is already stored, and who owns it.
/// </summary>
public class AdminBenchmarkSnapshotMatchTests
{
    private const string Board = "Dlvl:3 $:10 HP:14(14)\n\nThe map\n@....|";

    private static async Task<BenchmarkGameSnapshot> SeedBoardAsync(
        ApplicationDbContext db, string name, string text, string? suiteName = null)
    {
        var stored = BenchmarkSnapshotImporter.StoredForm(text, false);
        var board = new BenchmarkGameSnapshot
        {
            Name = name,
            SanitizedText = stored.Text,
            DigestText = string.Empty,
            CharCount = stored.Text.Length,
            Sha256 = stored.Sha256,
            CaptureMethod = "TextUpload"
        };
        db.BenchmarkGameSnapshots.Add(board);

        if (suiteName != null)
        {
            db.BenchmarkSuites.Add(new BenchmarkSuite { Name = suiteName, GameSnapshot = board });
        }

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return board;
    }

    private static MatchSnapshotResult ReadResult(IActionResult result)
        => Assert.IsType<MatchSnapshotResult>(Assert.IsType<OkObjectResult>(result).Value);

    [Fact]
    public async Task NoStoredSnapshot_ReportsNoMatch_AndTheStoredForm()
    {
        var ct = TestContext.Current.CancellationToken;
        var (controller, _, _) = BenchmarkComplianceGuardTests.CreateTestBenchmarkController();

        var value = ReadResult(await controller.MatchSnapshot(new MatchSnapshotRequest { Text = Board }, ct));

        Assert.Null(value.Match);
        Assert.False(value.Truncated);
        Assert.False(value.IsHtml);
        Assert.Equal(BenchmarkSnapshotImporter.StoredForm(Board, false).Sha256, value.Sha256);
        Assert.Equal(BenchmarkSnapshotImporter.StoredForm(Board, false).Text.Length, value.CharCount);
    }

    [Fact]
    public async Task UnattachedSnapshot_IsReportedWithoutASuite()
    {
        var ct = TestContext.Current.CancellationToken;
        var (controller, db, _) = BenchmarkComplianceGuardTests.CreateTestBenchmarkController();
        var board = await SeedBoardAsync(db, "Free board", Board);

        var value = ReadResult(await controller.MatchSnapshot(new MatchSnapshotRequest { Text = Board }, ct));

        Assert.NotNull(value.Match);
        Assert.Equal(board.Id, value.Match!.Id);
        Assert.Equal("Free board", value.Match.Name);
        Assert.Null(value.Match.SuiteId);
        Assert.Null(value.Match.SuiteName);
    }

    [Fact]
    public async Task AttachedSnapshot_ReportsItsOwningSuite()
    {
        var ct = TestContext.Current.CancellationToken;
        var (controller, db, _) = BenchmarkComplianceGuardTests.CreateTestBenchmarkController();
        var board = await SeedBoardAsync(db, "Owned board", Board, "Owning Suite");

        var value = ReadResult(await controller.MatchSnapshot(new MatchSnapshotRequest { Text = Board }, ct));

        Assert.NotNull(value.Match);
        Assert.Equal(board.Id, value.Match!.Id);
        Assert.Equal("Owning Suite", value.Match.SuiteName);
        Assert.NotNull(value.Match.SuiteId);
    }

    [Fact]
    public async Task UnattachedSnapshotIsPreferredOverAnAttachedOne()
    {
        var ct = TestContext.Current.CancellationToken;
        var (controller, db, _) = BenchmarkComplianceGuardTests.CreateTestBenchmarkController();
        await SeedBoardAsync(db, "Owned board", Board, "Owning Suite");
        var free = await SeedBoardAsync(db, "Free board", Board);

        var value = ReadResult(await controller.MatchSnapshot(new MatchSnapshotRequest { Text = Board }, ct));

        Assert.Equal(free.Id, value.Match!.Id);
        Assert.Null(value.Match.SuiteId);
    }

    [Fact]
    public async Task OverlongText_IsTruncatedAndCounted()
    {
        var ct = TestContext.Current.CancellationToken;
        var (controller, _, _) = BenchmarkComplianceGuardTests.CreateTestBenchmarkController();

        var value = ReadResult(await controller.MatchSnapshot(new MatchSnapshotRequest { Text = new string('a', 70000) }, ct));

        Assert.True(value.Truncated);
        Assert.Equal(
            BenchmarkSnapshotImporter.DefaultMaxSnapshotChars + "\n\n[SNAPSHOT TRUNCATED at 60000 characters.]".Length,
            value.CharCount);
    }

    [Fact]
    public async Task HtmlIsDetectedAndFlattenedBeforeHashing()
    {
        var ct = TestContext.Current.CancellationToken;
        var (controller, db, _) = BenchmarkComplianceGuardTests.CreateTestBenchmarkController();
        var board = await SeedBoardAsync(db, "Flat board", "Dlvl:3 HP:14(14)\nThe map");

        var value = ReadResult(await controller.MatchSnapshot(
            new MatchSnapshotRequest { Text = "<html><body><pre>Dlvl:3 HP:14(14)\nThe map</pre></body></html>" }, ct));

        Assert.True(value.IsHtml);
        Assert.Equal(board.Sha256, value.Sha256);
        Assert.Equal(board.Id, value.Match!.Id);
    }

    [Fact]
    public async Task BlankText_Returns400_AndWritesNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var (controller, db, _) = BenchmarkComplianceGuardTests.CreateTestBenchmarkController();
        await SeedBoardAsync(db, "Free board", Board);

        Assert.IsType<BadRequestObjectResult>(await controller.MatchSnapshot(new MatchSnapshotRequest { Text = "   " }, ct));
        Assert.IsType<OkObjectResult>(await controller.MatchSnapshot(new MatchSnapshotRequest { Text = Board }, ct));

        Assert.Equal(1, await db.BenchmarkGameSnapshots.CountAsync(ct));
        Assert.Equal(0, await db.BenchmarkSuites.CountAsync(ct));
    }
}
