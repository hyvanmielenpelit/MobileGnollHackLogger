namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services.Benchmarking;
using Xunit;

/// <summary>POST suites/import: always creates a new suite from an imported YAML document.</summary>
public class AdminBenchmarkSuiteImportTests
{
    private const string Board = "Dlvl:3 $:10 HP:14(14)\n\nThe map\n@....|";

    private static ImportBenchmarkSuiteSnapshot Snapshot(string text = Board) => new()
    {
        Name = "Valkyrie dlvl 3",
        Text = text,
        SourceGnollHackVersion = "4.2.0 Build 47",
        CapturedAtUtc = new DateTime(2026, 9, 16, 18, 4, 11, DateTimeKind.Utc),
        Notes = "Exported from the developer menu."
    };

    private static ImportBenchmarkSuiteRequest Request(string name, int questionCount) => new()
    {
        Name = name,
        Description = "  Imported description  ",
        Questions = Enumerable.Range(1, questionCount)
            .Select(i => new ImportBenchmarkQuestionItem
            {
                QuestionText = $"Question {i}",
                Difficulty = i == 2 ? BenchmarkDifficulty.Intermediate : null,
                ExpectedPoints = $"- point {i}",
                ReplaceExpectedPoints = true
            })
            .ToList()
    };

    [Fact]
    public async Task CreatesSuiteWithQuestionsInOrder_NotGenerated_NoSnapshot()
    {
        var ct = TestContext.Current.CancellationToken;
        var (controller, db, _) = BenchmarkComplianceGuardTests.CreateTestBenchmarkController();

        var result = await controller.ImportSuite(Request("  YAML Suite  ", 3), ct);

        var dto = Assert.IsType<BenchmarkSuiteDto>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal("YAML Suite", dto.Name);
        Assert.Equal(3, dto.QuestionCount);
        Assert.Null(dto.GameSnapshotId);
        Assert.Null(dto.DefaultSuiteKey);

        var suite = await db.BenchmarkSuites.Include(s => s.Questions).SingleAsync(s => s.Id == dto.Id, ct);
        Assert.Equal("Imported description", suite.Description);
        var questions = suite.Questions.OrderBy(q => q.OrderIndex).ToList();
        Assert.Equal(new[] { 1, 2, 3 }, questions.Select(q => q.OrderIndex).ToArray());
        Assert.Equal(new[] { "Question 1", "Question 2", "Question 3" }, questions.Select(q => q.QuestionText).ToArray());
        Assert.Equal(BenchmarkDifficulty.Simple, questions[0].Difficulty);
        Assert.Equal(BenchmarkDifficulty.Intermediate, questions[1].Difficulty);
        Assert.All(questions, q => Assert.False(q.IsGenerated));
    }

    [Fact]
    public async Task BlankName_Returns400()
    {
        var (controller, _, _) = BenchmarkComplianceGuardTests.CreateTestBenchmarkController();
        var bad = Assert.IsType<BadRequestObjectResult>(await controller.ImportSuite(Request("  ", 1), TestContext.Current.CancellationToken));
        Assert.Equal("Suite name is required.", bad.Value);
    }

    [Fact]
    public async Task OverlongName_Returns400()
    {
        var (controller, _, _) = BenchmarkComplianceGuardTests.CreateTestBenchmarkController();
        var bad = Assert.IsType<BadRequestObjectResult>(await controller.ImportSuite(Request(new string('x', 129), 1), TestContext.Current.CancellationToken));
        Assert.Equal("Suite name must be at most 128 characters.", bad.Value);
    }

    [Fact]
    public async Task NameCollision_GetsImportedSuffix()
    {
        var (controller, db, _) = BenchmarkComplianceGuardTests.CreateTestBenchmarkController();
        await BenchmarkComplianceGuardTests.SeedConfigsAndSuite(db);

        var first = Assert.IsType<BenchmarkSuiteDto>(Assert.IsType<OkObjectResult>(await controller.ImportSuite(Request("Test Suite", 1), TestContext.Current.CancellationToken)).Value);
        var second = Assert.IsType<BenchmarkSuiteDto>(Assert.IsType<OkObjectResult>(await controller.ImportSuite(Request("Test Suite", 1), TestContext.Current.CancellationToken)).Value);

        Assert.Equal("Test Suite (Imported)", first.Name);
        Assert.Equal("Test Suite (Imported 2)", second.Name);
    }

    [Fact]
    public async Task OverTheGuardLimit_Returns400_AndPersistsNoSuite()
    {
        var ct = TestContext.Current.CancellationToken;
        var (controller, db, _) = BenchmarkComplianceGuardTests.CreateTestBenchmarkController(maxQuestions: 2);

        var bad = Assert.IsType<BadRequestObjectResult>(await controller.ImportSuite(Request("Too Big", 3), ct));

        Assert.Contains("Suite question limit reached (2 questions maximum)", bad.Value?.ToString());
        Assert.Equal(0, await db.BenchmarkSuites.CountAsync(ct));
    }

    [Fact]
    public async Task QuestionWithoutText_Returns400()
    {
        var (controller, _, _) = BenchmarkComplianceGuardTests.CreateTestBenchmarkController();
        var request = Request("Blank Question", 2);
        request.Questions[1].QuestionText = null;

        var bad = Assert.IsType<BadRequestObjectResult>(await controller.ImportSuite(request, TestContext.Current.CancellationToken));
        Assert.Equal("Question text is required for a new question.", bad.Value);
    }

    [Fact]
    public async Task EmptyQuestionList_Returns400()
    {
        var (controller, _, _) = BenchmarkComplianceGuardTests.CreateTestBenchmarkController();
        var bad = Assert.IsType<BadRequestObjectResult>(await controller.ImportSuite(
            new ImportBenchmarkSuiteRequest { Name = "Empty", Questions = new List<ImportBenchmarkQuestionItem>() }, TestContext.Current.CancellationToken));
        Assert.Equal("A suite needs at least one question.", bad.Value);
    }

    // --- The snapshot carried by the document ---

    [Fact]
    public async Task WithSnapshot_CreatesAndAttachesOneBoard_WithTheFilesMetadata()
    {
        var ct = TestContext.Current.CancellationToken;
        var (controller, db, _) = BenchmarkComplianceGuardTests.CreateTestBenchmarkController();

        var request = Request("Snapshot Suite", 2);
        request.Snapshot = Snapshot();

        var dto = Assert.IsType<BenchmarkSuiteDto>(Assert.IsType<OkObjectResult>(await controller.ImportSuite(request, ct)).Value);

        Assert.NotNull(dto.GameSnapshotId);
        Assert.Equal("Valkyrie dlvl 3", dto.GameSnapshotName);

        var board = await db.BenchmarkGameSnapshots.SingleAsync(ct);
        Assert.Equal(dto.GameSnapshotId, board.Id);
        Assert.Equal(BenchmarkSnapshotImporter.YamlImportCaptureMethod, board.CaptureMethod);
        Assert.Equal("4.2.0 Build 47", board.SourceGnollHackVersion);
        Assert.Equal("Exported from the developer menu.", board.Notes);
        Assert.Equal(new DateTime(2026, 9, 16, 18, 4, 11, DateTimeKind.Utc), board.CapturedAtUtc);
        Assert.Equal(BenchmarkSnapshotImporter.StoredForm(Board, false).Sha256, board.Sha256);
    }

    [Fact]
    public async Task WithSnapshot_CrlfAndLfTextHashTheSame()
    {
        var ct = TestContext.Current.CancellationToken;
        var (controller, db, _) = BenchmarkComplianceGuardTests.CreateTestBenchmarkController();

        var lf = Request("LF Suite", 1);
        lf.Snapshot = Snapshot();
        await controller.ImportSuite(lf, ct);

        var crlf = Request("CRLF Suite", 1);
        crlf.Snapshot = Snapshot(Board.Replace("\n", "\r\n"));
        crlf.Snapshot.Name = "Valkyrie dlvl 3 crlf";
        await controller.ImportSuite(crlf, ct);

        var hashes = await db.BenchmarkGameSnapshots.Select(s => s.Sha256).ToListAsync(ct);
        Assert.Equal(2, hashes.Count);
        Assert.Equal(hashes[0], hashes[1]);
    }

    [Fact]
    public async Task IdenticalUnattachedSnapshot_IsReused_WithItsOwnMetadata()
    {
        var ct = TestContext.Current.CancellationToken;
        var (controller, db, _) = BenchmarkComplianceGuardTests.CreateTestBenchmarkController();

        var stored = BenchmarkSnapshotImporter.StoredForm(Board, false);
        var free = new BenchmarkGameSnapshot
        {
            Name = "Already stored",
            SanitizedText = stored.Text,
            DigestText = string.Empty,
            CharCount = stored.Text.Length,
            Sha256 = stored.Sha256,
            CaptureMethod = "TextUpload",
            Notes = "The original notes."
        };
        db.BenchmarkGameSnapshots.Add(free);
        await db.SaveChangesAsync(ct);

        var request = Request("Reusing Suite", 1);
        request.Snapshot = Snapshot();
        var dto = Assert.IsType<BenchmarkSuiteDto>(Assert.IsType<OkObjectResult>(await controller.ImportSuite(request, ct)).Value);

        Assert.Equal(free.Id, dto.GameSnapshotId);
        Assert.Equal(1, await db.BenchmarkGameSnapshots.CountAsync(ct));

        var board = await db.BenchmarkGameSnapshots.SingleAsync(ct);
        Assert.Equal("Already stored", board.Name);
        Assert.Equal("TextUpload", board.CaptureMethod);
        Assert.Equal("The original notes.", board.Notes);
    }

    [Fact]
    public async Task IdenticalSnapshotOfAnotherSuite_IsCopied()
    {
        var ct = TestContext.Current.CancellationToken;
        var (controller, db, _) = BenchmarkComplianceGuardTests.CreateTestBenchmarkController();

        var first = Request("First Suite", 1);
        first.Snapshot = Snapshot();
        var firstDto = Assert.IsType<BenchmarkSuiteDto>(Assert.IsType<OkObjectResult>(await controller.ImportSuite(first, ct)).Value);

        var second = Request("Second Suite", 1);
        second.Snapshot = Snapshot();
        var secondDto = Assert.IsType<BenchmarkSuiteDto>(Assert.IsType<OkObjectResult>(await controller.ImportSuite(second, ct)).Value);

        Assert.Equal(2, await db.BenchmarkGameSnapshots.CountAsync(ct));
        Assert.NotEqual(firstDto.GameSnapshotId, secondDto.GameSnapshotId);
        Assert.Equal("Valkyrie dlvl 3", firstDto.GameSnapshotName);
        Assert.Equal("Valkyrie dlvl 3 (2)", secondDto.GameSnapshotName);

        var owner = await db.BenchmarkSuites.SingleAsync(s => s.Id == firstDto.Id, ct);
        Assert.Equal(firstDto.GameSnapshotId, owner.GameSnapshotId);
    }

    [Fact]
    public async Task BlankSnapshotName_UsesTheRequestedSuiteName_NotTheImportedSuffix()
    {
        var ct = TestContext.Current.CancellationToken;
        var (controller, db, _) = BenchmarkComplianceGuardTests.CreateTestBenchmarkController();
        await BenchmarkComplianceGuardTests.SeedConfigsAndSuite(db);

        var request = Request("Test Suite", 1);
        request.Snapshot = Snapshot();
        request.Snapshot.Name = "   ";

        var dto = Assert.IsType<BenchmarkSuiteDto>(Assert.IsType<OkObjectResult>(await controller.ImportSuite(request, ct)).Value);

        Assert.Equal("Test Suite (Imported)", dto.Name);
        Assert.Equal("Test Suite", dto.GameSnapshotName);
    }

    [Fact]
    public async Task HtmlSnapshotText_IsFlattened()
    {
        var ct = TestContext.Current.CancellationToken;
        var (controller, db, _) = BenchmarkComplianceGuardTests.CreateTestBenchmarkController();

        var request = Request("Html Suite", 1);
        request.Snapshot = Snapshot("<html><body><pre>Dlvl:3 HP:14(14)\nThe map</pre></body></html>");

        Assert.IsType<OkObjectResult>(await controller.ImportSuite(request, ct));

        var board = await db.BenchmarkGameSnapshots.SingleAsync(ct);
        Assert.DoesNotContain("<", board.SanitizedText);
        Assert.Contains("Dlvl:3 HP:14(14)", board.SanitizedText);
    }

    [Fact]
    public async Task WhitespaceOnlySnapshotText_Returns400_AndPersistsNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var (controller, db, _) = BenchmarkComplianceGuardTests.CreateTestBenchmarkController();

        var request = Request("Blank Board", 1);
        request.Snapshot = Snapshot("   \n\n  ");

        var bad = Assert.IsType<BadRequestObjectResult>(await controller.ImportSuite(request, ct));
        Assert.Equal("Snapshot text is empty.", bad.Value);
        Assert.Equal(0, await db.BenchmarkSuites.CountAsync(ct));
        Assert.Equal(0, await db.BenchmarkGameSnapshots.CountAsync(ct));
    }

    [Fact]
    public async Task OverlongSnapshotVersion_Returns400_AndPersistsNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var (controller, db, _) = BenchmarkComplianceGuardTests.CreateTestBenchmarkController();

        var request = Request("Long Version", 1);
        request.Snapshot = Snapshot();
        request.Snapshot.SourceGnollHackVersion = new string('v', 65);

        var bad = Assert.IsType<BadRequestObjectResult>(await controller.ImportSuite(request, ct));
        Assert.Equal("Snapshot GnollHack version must be at most 64 characters.", bad.Value);
        Assert.Equal(0, await db.BenchmarkSuites.CountAsync(ct));
        Assert.Equal(0, await db.BenchmarkGameSnapshots.CountAsync(ct));
    }
}
