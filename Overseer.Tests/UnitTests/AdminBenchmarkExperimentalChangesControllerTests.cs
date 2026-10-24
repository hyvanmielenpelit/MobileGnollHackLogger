namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MobileGnollHackLogger.Data;
using Overseer.Controllers;
using Overseer.Models;
using Overseer.Services;
using Overseer.Services.Benchmarking;
using Overseer.Tests.Helpers;
using Xunit;

/// <summary>
/// The eight experimental-change actions of the benchmark admin controller, on SQLite: the catalog
/// with run counts and recent selections, the runs of a spec, create, edit, retire, restore, delete,
/// the overlap screen and the folder download, with their success and refusal codes and bodies.
/// </summary>
public sealed class AdminBenchmarkExperimentalChangesControllerTests : IDisposable
{
    private readonly ExperimentalChangeRoot _root = new();
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<ApplicationDbContext> _options;
    private readonly ApplicationDbContext _db;
    private readonly SystemAiConfigurationSnapshot _candidate = BenchmarkModelSnapshots.Model("OpenAI", "gpt-test", displayName: "Candidate");
    private readonly SystemAiConfigurationSnapshot _assessor = BenchmarkModelSnapshots.Model("Anthropic", "claude-test", displayName: "Assessor");
    private readonly BenchmarkExperimentalChangeCatalog _catalog;
    private readonly AdminBenchmarkController _controller;
    private readonly BenchmarkExperimentalChangeStore _store;
    private readonly BenchmarkChangeTextScreen _screen;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public AdminBenchmarkExperimentalChangesControllerTests()
    {
        _root.Write("probe-tool-v1", ExperimentalChangeFixtures.ChangeJson("probe-tool-v1", title: "Probe tool",
            experimentalTool: "get_character_info"));
        _root.Write("probe-steps-v1", ExperimentalChangeFixtures.ChangeJson("probe-steps-v1", title: "Probe steps",
            instructions: "i.md"), ("i.md", "Read get_character_info first."));
        _root.Write("probe-old-v1", ExperimentalChangeFixtures.ChangeJson("probe-old-v1", title: "Probe old",
            instructions: "i.md"), ("i.md", "Old steps."));
        _root.WriteRetired("probe-old-v1");

        _connection = SqliteSchemaTemplate.OpenCopy();
        _options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options;
        _db = new ApplicationDbContext(_options);
        _catalog = ExperimentalChangeFixtures.Catalog(_root.Root, _options);
        _screen = new BenchmarkChangeTextScreen(_db);
        _store = new BenchmarkExperimentalChangeStore(_db, _catalog, _screen, new SteppingTimeProvider());

        // The actions touch only the DbContext and the benchmark service's change catalog.
        _controller = new AdminBenchmarkController(
            _db, CreateService(_catalog), null!, null!, null!, null!, null!, null!, null!,
            null!, null!, null!, null!, null!, null!, null!, null!, null!, null!);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
        _root.Dispose();
    }

    private static BenchmarkService CreateService(BenchmarkExperimentalChangeCatalog catalog)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { { "AesEncryptionKey", Convert.ToBase64String(new byte[32]) } })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        return new BenchmarkService(
            scopeFactory,
            null!,
            null!,
            new CryptoService(config),
            new BenchmarkRunManager(),
            new BenchmarkDifficultyJobManager(),
            new BenchmarkScoringProfileService(scopeFactory, NullLogger<BenchmarkScoringProfileService>.Instance),
            new Overseer.Services.Privacy.EndpointPolicy(config),
            config,
            NullLogger<BenchmarkService>.Instance)
        {
            ExperimentalChangeCatalog = catalog
        };
    }

    private async Task AddRunAsync(string? stamp, DateTime startedAtUtc, string? condition = null)
    {
        _db.BenchmarkRuns.Add(new BenchmarkRun
        {
            SuiteName = "Change Suite",
            TestedModelSnapshot = _candidate,
            AssessorModelSnapshot = _assessor,
            StartedAtUtc = startedAtUtc,
            Status = BenchmarkRunStatus.Completed,
            InstructionVariant = stamp,
            CandidatePromptOptionsJson = BenchmarkService.CandidatePromptOptionsFor(
                false, true, false, BenchmarkInformationConditions.FromWire(condition)).ToCanonicalJson()
        });
        await _db.SaveChangesAsync(Ct);
    }

    private string StampOf(string spec) => _catalog.Resolve(spec, allowRetired: true).Stamp!;

    private static T Value<T>(IActionResult result) where T : class
        => Assert.IsType<T>(Assert.IsAssignableFrom<ObjectResult>(result).Value);

    /// <summary>An anonymous error body, read back as the client reads it.</summary>
    private static JsonElement Body(IActionResult result)
        => JsonSerializer.SerializeToElement(Assert.IsAssignableFrom<ObjectResult>(result).Value);

    private static BenchmarkExperimentalChangeEditRequest Request(string instructions = "Name the page you read.", bool confirmed = true) => new()
    {
        Title = "Cite the page",
        Summary = "Adds a sentence asking the AI to name the page.",
        Instructions = instructions,
        GenericConfirmed = confirmed
    };

    private async Task<BenchmarkExperimentalChangeDto> CreateAsync(BenchmarkExperimentalChangeEditRequest? request = null)
    {
        var result = await _controller.CreateExperimentalChange(request ?? Request(), _store, Ct);
        Assert.Equal(StatusCodes.Status201Created, Assert.IsAssignableFrom<ObjectResult>(result).StatusCode);
        return Value<BenchmarkExperimentalChangeSaveResultDto>(result).Change;
    }

    // -- GET experimental-changes --------------------------------------------------------------

    [Fact]
    public async Task TheCatalog_ListsEveryChange_WithRunCounts_RecentSelections_ToolsAndLimits()
    {
        var stored = await CreateAsync();
        var start = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
        await AddRunAsync(StampOf("probe-tool-v1"), start);
        await AddRunAsync(StampOf("probe-tool-v1"), start.AddHours(1));
        await AddRunAsync(StampOf("probe-steps-v1+probe-tool-v1"), start.AddHours(2));
        await AddRunAsync(StampOf("probe-old-v1"), start.AddHours(3));
        await AddRunAsync("gone-v1:000000000000", start.AddHours(4));
        await AddRunAsync(null, start.AddHours(5));

        var catalog = Value<BenchmarkExperimentalChangeCatalogDto>(await _controller.GetExperimentalChanges(Ct));

        Assert.Equal(new[] { "probe-old-v1", "probe-steps-v1", "probe-tool-v1", stored.Id }, catalog.Changes.Select(c => c.Id));
        var byId = catalog.Changes.ToDictionary(c => c.Id);
        Assert.Equal(3, byId["probe-tool-v1"].RunCount);
        Assert.Equal(1, byId["probe-steps-v1"].RunCount);
        Assert.Equal(0, byId[stored.Id].RunCount);
        Assert.True(byId["probe-old-v1"].Retired);
        Assert.Equal(new[] { "probe-tool-v1" }, byId["probe-steps-v1"].Mentions);
        Assert.Equal("database", byId[stored.Id].Source);

        // Newest first, without the retired change's selection or the unknown one.
        Assert.Equal(new[] { "probe-steps-v1+probe-tool-v1", "probe-tool-v1" }, catalog.RecentSelections.Select(r => r.Spec));
        var tool = catalog.RecentSelections[1];
        Assert.Equal(new[] { "probe-tool-v1" }, tool.ChangeIds);
        Assert.Equal(2, tool.RunCount);
        Assert.Equal(start.AddHours(1), tool.LastStartedAtUtc);

        Assert.Equal(new[] { "wiki_search", "get_monster_stats", "get_item_stats", "get_artifact_stats" }, catalog.ProductionTools.Select(t => t.Name));
        Assert.Equal((60, 220, 4000, 6), (catalog.Limits.TitleMax, catalog.Limits.SummaryMax, catalog.Limits.TextMax, catalog.Limits.DescriptionEditsMax));
        Assert.Equal(160, catalog.MaxStampLength);
    }

    // -- GET experimental-changes/runs ---------------------------------------------------------

    [Fact]
    public async Task TheRuns_AreThoseOfExactlyTheSpec_NewestFirst_WithTheirHashState()
    {
        string stamp = StampOf("probe-steps-v1+probe-tool-v1");
        var start = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i < 11; i++)
        {
            await AddRunAsync(stamp, start.AddHours(i), i == 10 ? BenchmarkInformationConditions.SourceOnlyWire : null);
        }
        await AddRunAsync("probe-steps-v1+probe-tool-v1:000000000000", start.AddHours(-1));
        await AddRunAsync(StampOf("probe-tool-v1"), start.AddHours(20));
        await AddRunAsync(StampOf("probe-steps-v1"), start.AddHours(21));

        // A query string decodes '+' to a space; the ids may come in any order.
        var runs = Value<BenchmarkExperimentalChangeRunsDto>(await _controller.GetExperimentalChangeRuns("probe-tool-v1 probe-steps-v1", Ct));

        Assert.Equal(12, runs.RunCount);
        Assert.Equal(1, runs.OlderHashRunCount);
        Assert.Equal(BenchmarkExperimentalChangeRunsDto.RunListSize, runs.Runs.Count);
        Assert.Equal(runs.Runs.Select(r => r.StartedAtUtc).OrderByDescending(t => t), runs.Runs.Select(r => r.StartedAtUtc));
        var newest = runs.Runs[0];
        Assert.Equal("probe-steps-v1+probe-tool-v1", newest.Spec);
        Assert.True(newest.HashMatchesCurrent);
        Assert.Equal(BenchmarkInformationConditions.SourceOnlyWire, newest.InformationCondition);
        Assert.Equal(("Change Suite", "Candidate", nameof(BenchmarkRunStatus.Completed)), (newest.SuiteName, newest.ModelDisplayName, newest.Status));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    [InlineData("asker-profile-v1/minus/guide.wiki_search")]
    public async Task TheRunsOfABlankOrMalformedSpec_Are400(string? spec)
    {
        var result = await _controller.GetExperimentalChangeRuns(spec, Ct);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.False(string.IsNullOrEmpty(Body(result).GetProperty("error").GetString()));
    }

    // -- POST experimental-changes -------------------------------------------------------------

    [Fact]
    public async Task ACreate_Is201_WithTheChangeAndItsOverlaps()
    {
        var suite = new BenchmarkSuite { Name = "Gnoll Basics", Description = "Desc" };
        suite.Questions.Add(new BenchmarkQuestion
        {
            QuestionText = "Q",
            OrderIndex = 0,
            ExpectedPoints = "Always name the wiki page that states the rule before answering.",
            Difficulty = BenchmarkDifficulty.Simple
        });
        _db.BenchmarkSuites.Add(suite);
        await _db.SaveChangesAsync(Ct);

        var result = await _controller.CreateExperimentalChange(Request("Always name the wiki page that states the rule before answering."), _store, Ct);

        Assert.Equal(StatusCodes.Status201Created, Assert.IsAssignableFrom<ObjectResult>(result).StatusCode);
        var saved = Value<BenchmarkExperimentalChangeSaveResultDto>(result);
        Assert.Equal("cite-the-page-v1", saved.Change.Id);
        Assert.Equal("database", saved.Change.Source);
        Assert.Equal(new[] { "instructions" }, saved.Change.Kinds);
        Assert.True(saved.Change.CanEditText);
        Assert.NotNull(saved.Change.UpdatedAtUtc);
        var overlap = Assert.Single(saved.Overlaps);
        Assert.Equal(("Gnoll Basics", 1, "rubric"), (overlap.SuiteName, overlap.QuestionNumber, overlap.Field));
    }

    [Fact]
    public async Task ACreate_WithoutTheConfirmation_Is400_WithFieldErrors()
    {
        var result = await _controller.CreateExperimentalChange(Request(confirmed: false), _store, Ct);

        Assert.IsType<BadRequestObjectResult>(result);
        var body = Body(result);
        Assert.Equal(BenchmarkExperimentalChangeStore.FieldErrorsMessage, body.GetProperty("error").GetString());
        Assert.Equal(BenchmarkExperimentalChangeStore.ConfirmationRequired, body.GetProperty("fieldErrors").GetProperty("genericConfirmed").GetString());
    }

    [Fact]
    public async Task ACreate_OfACopyOfAToolChange_Is400_WithAnErrorOnly()
    {
        var request = Request();
        request.CopiedFrom = "probe-tool-v1";

        var result = await _controller.CreateExperimentalChange(request, _store, Ct);

        Assert.IsType<BadRequestObjectResult>(result);
        var body = Body(result);
        Assert.Contains("without a new tool", body.GetProperty("error").GetString());
        Assert.False(body.TryGetProperty("fieldErrors", out _));
    }

    // -- PUT experimental-changes/{id} ---------------------------------------------------------

    private static BenchmarkExperimentalChangeEditRequest EditOf(BenchmarkExperimentalChangeDto change, string? instructions = null) => new()
    {
        Title = change.Title,
        Summary = change.Summary,
        ExpectedEffect = change.ExpectedEffect,
        Instructions = instructions ?? change.Instructions,
        GenericConfirmed = true,
        ExpectedUpdatedAtUtc = change.UpdatedAtUtc
    };

    [Fact]
    public async Task AnEdit_Is200_AndAStaleOne409EditedElsewhere()
    {
        var created = await CreateAsync();

        var result = await _controller.UpdateExperimentalChange(created.Id, EditOf(created, "Name the page, and quote it."), _store, Ct);
        var stale = await _controller.UpdateExperimentalChange(created.Id, EditOf(created, "From the old copy."), _store, Ct);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal("Name the page, and quote it.", Value<BenchmarkExperimentalChangeSaveResultDto>(result).Change.Instructions);
        Assert.IsType<ConflictObjectResult>(stale);
        Assert.Equal(BenchmarkChangeConflictReasons.EditedElsewhere, Body(stale).GetProperty("reason").GetString());
    }

    [Fact]
    public async Task ATextEditOfALockedChange_Is409Locked()
    {
        var created = await CreateAsync();
        await BenchmarkExperimentalChangeStore.LockAsync(_db, new[] { created.Id }, DateTime.UtcNow, Ct);

        var result = await _controller.UpdateExperimentalChange(created.Id, EditOf(created, "Changed after a run."), _store, Ct);

        Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal(BenchmarkChangeConflictReasons.Locked, Body(result).GetProperty("reason").GetString());
        Assert.StartsWith("Its text is locked", Body(result).GetProperty("error").GetString());
    }

    [Fact]
    public async Task AnEditOfABuiltInChange_Is409BuiltIn_AndOfAnUnknownOne404()
    {
        var builtIn = await _controller.UpdateExperimentalChange("probe-steps-v1", Request(), _store, Ct);
        var unknown = await _controller.UpdateExperimentalChange("no-such-v1", Request(), _store, Ct);

        Assert.IsType<ConflictObjectResult>(builtIn);
        Assert.Equal(BenchmarkChangeConflictReasons.BuiltIn, Body(builtIn).GetProperty("reason").GetString());
        Assert.IsType<NotFoundObjectResult>(unknown);
    }

    [Fact]
    public async Task AnEditWithoutTheConfirmation_Is400()
    {
        var created = await CreateAsync();
        var request = EditOf(created, "Changed.");
        request.GenericConfirmed = false;

        var result = await _controller.UpdateExperimentalChange(created.Id, request, _store, Ct);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.True(Body(result).GetProperty("fieldErrors").TryGetProperty("genericConfirmed", out _));
    }

    // -- Retire, restore, delete ---------------------------------------------------------------

    [Fact]
    public async Task RetireAndRestore_Are204_AndABuiltInChange409()
    {
        var created = await CreateAsync();

        Assert.IsType<NoContentResult>(await _controller.RetireExperimentalChange(created.Id, _store, Ct));
        Assert.True(_catalog.Snapshot.Find(created.Id)!.Retired);
        Assert.IsType<NoContentResult>(await _controller.RestoreExperimentalChange(created.Id, _store, Ct));
        Assert.False(_catalog.Snapshot.Find(created.Id)!.Retired);

        var builtIn = await _controller.RetireExperimentalChange("probe-steps-v1", _store, Ct);
        Assert.IsType<ConflictObjectResult>(builtIn);
        Assert.Equal(BenchmarkChangeConflictReasons.BuiltIn, Body(builtIn).GetProperty("reason").GetString());
        Assert.IsType<NotFoundObjectResult>(await _controller.RestoreExperimentalChange("no-such-v1", _store, Ct));
    }

    [Fact]
    public async Task Delete_Is204_AndALockedChange409()
    {
        var unlocked = await CreateAsync();
        var locked = await CreateAsync(Request("Used by a run."));
        await BenchmarkExperimentalChangeStore.LockAsync(_db, new[] { locked.Id }, DateTime.UtcNow, Ct);

        Assert.IsType<NoContentResult>(await _controller.DeleteExperimentalChange(unlocked.Id, _store, Ct));
        var refused = await _controller.DeleteExperimentalChange(locked.Id, _store, Ct);

        Assert.IsType<ConflictObjectResult>(refused);
        Assert.Equal(BenchmarkChangeConflictReasons.Locked, Body(refused).GetProperty("reason").GetString());
        Assert.IsType<NotFoundObjectResult>(await _controller.DeleteExperimentalChange(unlocked.Id, _store, Ct));
        Assert.IsType<ConflictObjectResult>(await _controller.DeleteExperimentalChange("probe-tool-v1", _store, Ct));
    }

    // -- Screen --------------------------------------------------------------------------------

    [Fact]
    public async Task TheScreen_ReturnsTheOverlaps()
    {
        var suite = new BenchmarkSuite { Name = "Gnoll Basics", Description = "Desc" };
        suite.Questions.Add(new BenchmarkQuestion
        {
            QuestionText = "Which artifact does a healer receive at the end of the quest?",
            OrderIndex = 4,
            Difficulty = BenchmarkDifficulty.Simple
        });
        _db.BenchmarkSuites.Add(suite);
        await _db.SaveChangesAsync(Ct);

        var result = await _controller.ScreenExperimentalChangeTexts(new BenchmarkChangeScreenRequest
        {
            Texts = new List<string?> { "Ask which artifact does a healer receive at the end of it.", null, "Nothing shared." }
        }, _screen, Ct);

        var overlap = Assert.Single(Value<BenchmarkChangeScreenResultDto>(result).Overlaps);
        Assert.Equal(("which artifact does a healer receive at the end of", 5, "question"), (overlap.Text, overlap.QuestionNumber, overlap.Field));
        Assert.Empty(Value<BenchmarkChangeScreenResultDto>(await _controller.ScreenExperimentalChangeTexts(null, _screen, Ct)).Overlaps);
    }

    // -- GET experimental-changes/{id}/folder --------------------------------------------------

    [Fact]
    public async Task TheFolder_IsAZipNamedByTheId_AndAnUnknownId404()
    {
        var created = await CreateAsync();

        var file = Assert.IsType<FileContentResult>(await _controller.DownloadExperimentalChangeFolder(created.Id, Ct));
        var builtIn = Assert.IsType<FileContentResult>(await _controller.DownloadExperimentalChangeFolder("probe-tool-v1", Ct));

        Assert.Equal("application/zip", file.ContentType);
        Assert.Equal($"{created.Id}.zip", file.FileDownloadName);
        using (var zip = new ZipArchive(new MemoryStream(file.FileContents)))
        {
            Assert.Equal(new[] { $"{created.Id}/change.json", $"{created.Id}/instructions.md" },
                zip.Entries.Select(e => e.FullName).OrderBy(n => n, StringComparer.Ordinal));
        }
        Assert.Equal("probe-tool-v1.zip", builtIn.FileDownloadName);
        Assert.IsType<NotFoundObjectResult>(await _controller.DownloadExperimentalChangeFolder("no-such-v1", Ct));
    }
}
