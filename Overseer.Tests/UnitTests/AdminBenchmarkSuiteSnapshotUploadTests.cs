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
/// POST suites/{id}/snapshot: builds a board from an uploaded .snapshot.txt or raw HTML dump and
/// attaches it to an existing suite, replacing a current snapshot only when confirmed.
/// </summary>
public class AdminBenchmarkSuiteSnapshotUploadTests
{
    private static (AdminBenchmarkController Controller, ApplicationDbContext Db) CreateController()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options);

        // The action touches only the DbContext and the importer.
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
                        new[] { new Claim(ClaimTypes.NameIdentifier, "admin-1") }, "TestAuth"))
                }
            }
        };

        return (controller, db);
    }

    private static async Task<BenchmarkSuite> SeedSuiteAsync(ApplicationDbContext db, string name = "Upload Target")
    {
        var suite = new BenchmarkSuite { Name = name, Description = "Desc" };
        suite.Questions.Add(new BenchmarkQuestion { QuestionText = "Q1", OrderIndex = 1 });
        db.BenchmarkSuites.Add(suite);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return suite;
    }

    private static CaptureBenchmarkSnapshotResponse ReadResponse(IActionResult result)
        => Assert.IsType<CaptureBenchmarkSnapshotResponse>(Assert.IsType<OkObjectResult>(result).Value);

    [Fact]
    public async Task TextUpload_AttachesBoardToTheSuite_AndCreatesNoSuite()
    {
        var ct = TestContext.Current.CancellationToken;
        var (controller, db) = CreateController();
        var suite = await SeedSuiteAsync(db);
        string content = "Dlvl:3 $:10 HP:14(14)  \n\n\n\nThe map";

        var result = await controller.UploadSuiteSnapshot(suite.Id, new UploadSuiteSnapshotRequest
        {
            Name = "valk dlvl 3",
            Content = content,
            Notes = "notes",
            SourceGnollHackVersion = "4.1.0"
        }, ct);

        var response = ReadResponse(result);
        var (expectedText, expectedSha) = BenchmarkSnapshotImporter.PrepareBoardText(DumpHtmlSanitizer.NormalizeFlattenedText(content));

        Assert.Equal(suite.Id, response.Suite.Id);
        Assert.Equal(response.Board.Id, response.Suite.GameSnapshotId);
        Assert.Equal(1, await db.BenchmarkSuites.CountAsync(ct));

        var board = await db.BenchmarkGameSnapshots.SingleAsync(ct);
        Assert.Equal("TextUpload", board.CaptureMethod);
        Assert.Equal(expectedSha, board.Sha256);
        Assert.Equal(expectedText.Length, board.CharCount);
        Assert.Equal(expectedText, board.SanitizedText);
        Assert.False(string.IsNullOrEmpty(board.DigestText));
        Assert.Equal("notes", board.Notes);
        Assert.Equal("4.1.0", board.SourceGnollHackVersion);

        var stored = await db.BenchmarkSuites.AsNoTracking().SingleAsync(ct);
        Assert.Equal(board.Id, stored.GameSnapshotId);
    }

    [Fact]
    public async Task HtmlUpload_AutoDetected_IsSanitizedAndStoredAsServerUpload()
    {
        var ct = TestContext.Current.CancellationToken;
        var (controller, db) = CreateController();
        var suite = await SeedSuiteAsync(db);
        string html = "<html><body><pre>Dlvl:1 &amp; more</pre></body></html>";

        var response = ReadResponse(await controller.UploadSuiteSnapshot(suite.Id, new UploadSuiteSnapshotRequest
        {
            Name = "html dump",
            Content = html,
            ContentKind = "Auto"
        }, ct));

        var board = await db.BenchmarkGameSnapshots.SingleAsync(b => b.Id == response.Board.Id, ct);
        Assert.Equal("ServerUpload", board.CaptureMethod);
        Assert.Equal(DumpHtmlSanitizer.Sanitize(html), board.SanitizedText);
        Assert.DoesNotContain("<pre", board.SanitizedText);
    }

    [Fact]
    public async Task SecondUploadWithoutReplace_Returns409_AndLeavesTheOldBoard()
    {
        var ct = TestContext.Current.CancellationToken;
        var (controller, db) = CreateController();
        var suite = await SeedSuiteAsync(db);
        var first = ReadResponse(await controller.UploadSuiteSnapshot(suite.Id, new UploadSuiteSnapshotRequest { Name = "first", Content = "Dlvl:1" }, ct));

        var result = await controller.UploadSuiteSnapshot(suite.Id, new UploadSuiteSnapshotRequest { Name = "second", Content = "Dlvl:2" }, ct);

        Assert.IsType<ConflictObjectResult>(result);
        var board = await db.BenchmarkGameSnapshots.SingleAsync(ct);
        Assert.Equal(first.Board.Id, board.Id);
        Assert.Equal("Dlvl:1", board.SanitizedText);
        Assert.Equal(first.Board.Id, (await db.BenchmarkSuites.AsNoTracking().SingleAsync(ct)).GameSnapshotId);
    }

    [Fact]
    public async Task UploadWithReplace_RemovesTheOldBoard_AndPointsTheSuiteAtTheNewOne()
    {
        var ct = TestContext.Current.CancellationToken;
        var (controller, db) = CreateController();
        var suite = await SeedSuiteAsync(db);
        var first = ReadResponse(await controller.UploadSuiteSnapshot(suite.Id, new UploadSuiteSnapshotRequest { Name = "board", Content = "Dlvl:1" }, ct));

        var second = ReadResponse(await controller.UploadSuiteSnapshot(suite.Id, new UploadSuiteSnapshotRequest
        {
            Name = "board",
            Content = "Dlvl:2",
            ReplaceExisting = true
        }, ct));

        Assert.False(await db.BenchmarkGameSnapshots.AnyAsync(b => b.Id == first.Board.Id, ct));
        var board = await db.BenchmarkGameSnapshots.SingleAsync(ct);
        Assert.Equal(second.Board.Id, board.Id);
        Assert.Equal("board (2)", board.Name);
        Assert.Equal(1, await db.BenchmarkSuites.CountAsync(ct));
        Assert.Equal(board.Id, (await db.BenchmarkSuites.AsNoTracking().SingleAsync(ct)).GameSnapshotId);
    }

    [Fact]
    public async Task UnknownSuite_Returns404()
    {
        var (controller, _) = CreateController();
        var result = await controller.UploadSuiteSnapshot(404, new UploadSuiteSnapshotRequest { Name = "x", Content = "Dlvl:1" }, TestContext.Current.CancellationToken);
        Assert.IsType<NotFoundResult>(result);
    }

    [Theory]
    [InlineData("  ", "Dlvl:1")]
    [InlineData("name", "  ")]
    public async Task BlankNameOrContent_Returns400(string name, string content)
    {
        var ct = TestContext.Current.CancellationToken;
        var (controller, db) = CreateController();
        var suite = await SeedSuiteAsync(db);

        var result = await controller.UploadSuiteSnapshot(suite.Id, new UploadSuiteSnapshotRequest { Name = name, Content = content }, ct);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(0, await db.BenchmarkGameSnapshots.CountAsync(ct));
    }

    [Fact]
    public async Task UnknownContentKind_Returns400()
    {
        var ct = TestContext.Current.CancellationToken;
        var (controller, db) = CreateController();
        var suite = await SeedSuiteAsync(db);

        var result = await controller.UploadSuiteSnapshot(suite.Id, new UploadSuiteSnapshotRequest { Name = "x", Content = "Dlvl:1", ContentKind = "Pdf" }, ct);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task CrlfText_HashesLikeItsLfForm()
    {
        var ct = TestContext.Current.CancellationToken;
        var (controller, db) = CreateController();
        var crlfSuite = await SeedSuiteAsync(db, "crlf");
        var lfSuite = await SeedSuiteAsync(db, "lf");

        var crlf = ReadResponse(await controller.UploadSuiteSnapshot(crlfSuite.Id, new UploadSuiteSnapshotRequest { Name = "crlf", Content = "Header\r\nMiddle\r\n\r\n\r\nFooter" }, ct));
        var lf = ReadResponse(await controller.UploadSuiteSnapshot(lfSuite.Id, new UploadSuiteSnapshotRequest { Name = "lf", Content = "Header\nMiddle\n\n\nFooter" }, ct));

        Assert.Equal(lf.Board.Sha256, crlf.Board.Sha256);
        Assert.DoesNotContain('\r', crlf.Board.SanitizedText!);
    }

    [Fact]
    public async Task NameUsedByAnotherBoard_GetsNumberSuffix()
    {
        var ct = TestContext.Current.CancellationToken;
        var (controller, db) = CreateController();
        var a = await SeedSuiteAsync(db, "a");
        var b = await SeedSuiteAsync(db, "b");

        await controller.UploadSuiteSnapshot(a.Id, new UploadSuiteSnapshotRequest { Name = "shared", Content = "Dlvl:1" }, ct);
        var second = ReadResponse(await controller.UploadSuiteSnapshot(b.Id, new UploadSuiteSnapshotRequest { Name = "shared", Content = "Dlvl:2" }, ct));

        Assert.Equal("shared (2)", second.Board.Name);
    }

    // --- BOARD FACTS quote check -------------------------------------------------------------

    private const string BoardFactsRubric = "**BOARD FACTS**\n"
        + "- The status line reads \"Dlvl:3 $:10\".\n"
        + "- The inventory lists \"T - the Holy Grail (0 charges, 0 rechargings)\".\n"
        + "- The status line shows no hunger state.";

    private static async Task<BenchmarkSuite> SeedSuiteWithRubricAsync(ApplicationDbContext db)
    {
        var suite = new BenchmarkSuite { Name = "Rubric Target", Description = "Desc" };
        suite.Questions.Add(new BenchmarkQuestion { QuestionText = "Q1", OrderIndex = 1, ExpectedPoints = BoardFactsRubric });
        db.BenchmarkSuites.Add(suite);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return suite;
    }

    [Fact]
    public async Task Upload_ReturnsTheBoardFactsCheck_AgainstTheNewBoard_AndLeavesTheQuestionUntouched()
    {
        var ct = TestContext.Current.CancellationToken;
        var (controller, db) = CreateController();
        var suite = await SeedSuiteWithRubricAsync(db);

        var response = ReadResponse(await controller.UploadSuiteSnapshot(suite.Id, new UploadSuiteSnapshotRequest
        {
            Name = "board",
            Content = "Dlvl:3 $:10 HP:14(14)\nT - the uncursed Holy Grail (0 charges, 0 rechargings)"
        }, ct));

        var check = Assert.IsType<BoardFactsCheckDto>(response.BoardFactsCheck);
        Assert.Equal(3, check.BulletCount);
        Assert.Equal(2, check.CheckedLiteralCount);
        Assert.Equal(1, check.UnquotedBulletCount);
        var missing = Assert.Single(check.MissingLiterals);
        Assert.Equal("T - the Holy Grail (0 charges, 0 rechargings)", missing.Literal);
        Assert.Equal(1, missing.OrderIndex);

        // Advisory only: the rubric and its revision stand.
        var question = await db.BenchmarkQuestions.AsNoTracking().SingleAsync(ct);
        Assert.Equal(BoardFactsRubric, question.ExpectedPoints);
        Assert.Equal(1, question.ItemRevision);
    }

    [Fact]
    public async Task ReplacingTheBoard_ChecksTheReplacement()
    {
        var ct = TestContext.Current.CancellationToken;
        var (controller, db) = CreateController();
        var suite = await SeedSuiteWithRubricAsync(db);
        await controller.UploadSuiteSnapshot(suite.Id, new UploadSuiteSnapshotRequest { Name = "old", Content = "Dlvl:1" }, ct);

        var response = ReadResponse(await controller.UploadSuiteSnapshot(suite.Id, new UploadSuiteSnapshotRequest
        {
            Name = "new",
            Content = "Dlvl:3 $:10\nT - the Holy Grail (0 charges, 0 rechargings)",
            ReplaceExisting = true
        }, ct));

        var check = Assert.IsType<BoardFactsCheckDto>(response.BoardFactsCheck);
        Assert.Equal(2, check.CheckedLiteralCount);
        Assert.Empty(check.MissingLiterals);
    }

    [Fact]
    public async Task OnDemandCheck_ReportsTheSuitesCurrentBoardAndRubrics()
    {
        var ct = TestContext.Current.CancellationToken;
        var (controller, db) = CreateController();
        var suite = await SeedSuiteWithRubricAsync(db);
        await controller.UploadSuiteSnapshot(suite.Id, new UploadSuiteSnapshotRequest { Name = "board", Content = "Dlvl:3 $:10" }, ct);

        var result = await controller.GetBoardFactsCheck(suite.Id, ct);

        var check = Assert.IsType<BoardFactsCheckDto>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(2, check.CheckedLiteralCount);
        Assert.Equal("T - the Holy Grail (0 charges, 0 rechargings)", Assert.Single(check.MissingLiterals).Literal);
    }

    [Fact]
    public async Task OnDemandCheck_IsNullForASuiteWithNoBoard()
    {
        var ct = TestContext.Current.CancellationToken;
        var (controller, db) = CreateController();
        var suite = await SeedSuiteWithRubricAsync(db);

        var result = await controller.GetBoardFactsCheck(suite.Id, ct);

        Assert.Null(Assert.IsType<OkObjectResult>(result).Value);
    }

    [Fact]
    public async Task OnDemandCheck_UnknownSuite_Returns404()
    {
        var (controller, _) = CreateController();

        var result = await controller.GetBoardFactsCheck(404, TestContext.Current.CancellationToken);

        Assert.IsType<NotFoundResult>(result);
    }

    [Theory]
    [InlineData("<html><body>x</body></html>", true)]
    [InlineData("  <BODY>x</BODY>", true)]
    [InlineData("<pre>Dlvl:1</pre>", true)]
    [InlineData("<div>not a dump</div>", false)]
    [InlineData("Dlvl:1 <html>", false)]
    [InlineData("", false)]
    public void LooksLikeHtml_DetectsDumps(string content, bool expected)
    {
        Assert.Equal(expected, AdminBenchmarkController.LooksLikeHtml(content));
    }
}
