namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services.Benchmarking;
using Overseer.Tests.Helpers;
using Xunit;

/// <summary>A clock that moves one second forward on every read.</summary>
internal sealed class SteppingTimeProvider : TimeProvider
{
    private DateTimeOffset _now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow()
    {
        _now = _now.AddSeconds(1);
        return _now;
    }
}

/// <summary>
/// The store of experimental changes written in Overseer, on SQLite: id generation and collisions
/// with both sources, new versions, the generic-text confirmation, field errors, locking and the
/// conditional updates, retirement, deletion, the built-in id rule, and the folder download.
/// </summary>
public sealed class BenchmarkExperimentalChangeStoreTests : IDisposable
{
    private readonly ExperimentalChangeRoot _root = new();
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<ApplicationDbContext> _options;
    private readonly ApplicationDbContext _db;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public BenchmarkExperimentalChangeStoreTests()
    {
        _connection = SqliteSchemaTemplate.OpenCopy();
        _options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options;
        _db = new ApplicationDbContext(_options);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
        _root.Dispose();
    }

    private BenchmarkExperimentalChangeCatalog Catalog() => ExperimentalChangeFixtures.Catalog(_root.Root, _options);

    private BenchmarkExperimentalChangeStore Store(BenchmarkExperimentalChangeCatalog? catalog = null)
        => new(_db, catalog ?? Catalog(), new BenchmarkChangeTextScreen(_db), new SteppingTimeProvider());

    private static BenchmarkExperimentalChangeEditRequest Request(
        string title = "Cite the page first",
        string summary = "Adds a sentence asking the AI to name the page it read.",
        string? instructions = null,
        bool confirmed = true,
        params (string Tool, string Mode, string Text)[] edits)
        => new()
        {
            Title = title,
            Summary = summary,
            DescriptionEdits = edits.Select(e => new BenchmarkChangeDescriptionEditRequest { Tool = e.Tool, Mode = e.Mode, Text = e.Text }).ToList(),
            Instructions = instructions,
            GenericConfirmed = confirmed
        };

    private static BenchmarkExperimentalChangeEditRequest Edit(Overseer.Services.Benchmarking.BenchmarkExperimentalChange change, Action<BenchmarkExperimentalChangeEditRequest>? update = null)
    {
        var request = new BenchmarkExperimentalChangeEditRequest
        {
            Title = change.Title,
            Summary = change.Summary,
            ExpectedEffect = change.ExpectedEffect,
            DescriptionEdits = change.DescriptionEdits.Select(e => new BenchmarkChangeDescriptionEditRequest
            {
                Tool = e.Tool,
                Mode = BenchmarkExperimentalChangeWire.ModeName(e.Mode),
                Text = e.Text
            }).ToList(),
            Instructions = change.Instructions,
            ExpectedUpdatedAtUtc = change.UpdatedAtUtc
        };
        update?.Invoke(request);
        return request;
    }

    private async Task<Overseer.Services.Benchmarking.BenchmarkExperimentalChange> CreateAsync(
        BenchmarkExperimentalChangeStore store, BenchmarkExperimentalChangeEditRequest? request = null)
    {
        var result = await store.CreateAsync(request ?? Request(instructions: "Name the page you read."), Ct);
        Assert.Equal(BenchmarkChangeStoreOutcome.Created, result.Outcome);
        return result.Change!;
    }

    // -- Ids -----------------------------------------------------------------------------------

    [Theory]
    [InlineData("Cite the page first", "cite-the-page-first")]
    [InlineData("Search with the player's race and role", "search-with-the-player-s")]
    [InlineData("  Café Über!  ", "cafe-uber")]
    [InlineData("Averyveryveryveryverylongsingleword", "averyveryveryveryverylongs")]
    [InlineData("!!!", "change")]
    public void TheStem_IsLowerCaseAscii_TrimmedAtAWordBoundary(string title, string stem)
    {
        Assert.Equal(stem, BenchmarkExperimentalChangeStore.Stem(title));
        Assert.True(stem.Length <= BenchmarkExperimentalChangeStore.IdStemMaxLength);
        Assert.True(BenchmarkExperimentalChangeValidator.IsValidId(stem + "-99-v1"));
    }

    [Fact]
    public void GeneratedIds_InsertACounterBeforeTheVersion_AndNewVersionsCountUp()
    {
        var taken = new HashSet<string> { "probe-v1", "probe-2-v1", "probe-v2" };

        Assert.Equal("probe-3-v1", BenchmarkExperimentalChangeStore.GenerateId("Probe", taken));
        Assert.Equal("other-v1", BenchmarkExperimentalChangeStore.GenerateId("Other", taken));
        Assert.Equal("probe-v3", BenchmarkExperimentalChangeStore.NextVersionId("probe-v1", taken));
        Assert.Equal("probe-v3", BenchmarkExperimentalChangeStore.NextVersionId("probe-v2", taken));
        Assert.Null(BenchmarkExperimentalChangeStore.NextVersionId("no-version", taken));
        Assert.Null(BenchmarkExperimentalChangeStore.NextVersionId("an-id-that-is-thirty-two-long-v9", new HashSet<string>()));
    }

    [Fact]
    public async Task ACreatedChange_TakesItsIdFromTheTitle_AvoidingBothSources()
    {
        _root.Write("cite-the-page-first-v1", ExperimentalChangeFixtures.ChangeJson("cite-the-page-first-v1", instructions: "i.md"), ("i.md", "Built in."));
        var store = Store();

        var first = await CreateAsync(store);
        var second = await CreateAsync(store);

        Assert.Equal("cite-the-page-first-2-v1", first.Id);
        Assert.Equal("cite-the-page-first-3-v1", second.Id);
        Assert.Equal(BenchmarkExperimentalChangeSource.Database, first.Source);
        Assert.Equal("Name the page you read.", first.Instructions);

        var row = await _db.BenchmarkExperimentalChanges.AsNoTracking().SingleAsync(c => c.ChangeId == first.Id, Ct);
        Assert.Equal(first.Hash, row.ContentHash);
        Assert.Equal(row.CreatedAtUtc, row.UpdatedAtUtc);
        Assert.Null(row.LockedAtUtc);
        Assert.Equal(0, row.UpdatedAtUtc.Ticks % TimeSpan.TicksPerMillisecond);
        Assert.NotNull(store.Catalog.Snapshot.Find(first.Id));
    }

    [Fact]
    public async Task ACopyAsNewVersion_TakesTheNextFreeVersionOfTheCopiedId()
    {
        _root.Write("probe-change-v1", ExperimentalChangeFixtures.ChangeJson("probe-change-v1", instructions: "i.md"), ("i.md", "Built in."));
        _root.Write("probe-tool-v1", ExperimentalChangeFixtures.ChangeJson("probe-tool-v1", experimentalTool: "get_character_info"));
        var store = Store();

        var request = Request(instructions: "A new version.");
        request.CopiedFrom = "probe-change-v1";
        var v2 = await CreateAsync(store, request);
        var v3 = await CreateAsync(store, request);

        Assert.Equal("probe-change-v2", v2.Id);
        Assert.Equal("probe-change-v1", v2.CopiedFrom);
        Assert.Equal("probe-change-v3", v3.Id);

        request.CopiedFrom = "probe-tool-v1";
        var refused = await store.CreateAsync(request, Ct);
        Assert.Equal(BenchmarkChangeStoreOutcome.Invalid, refused.Outcome);
        Assert.Contains("without a new tool or tool-output switch", refused.Error);

        request.CopiedFrom = "no-such-v1";
        Assert.Equal(BenchmarkChangeStoreOutcome.Invalid, (await store.CreateAsync(request, Ct)).Outcome);
    }

    // -- Validation and the confirmation -------------------------------------------------------

    [Fact]
    public async Task ACreate_WithoutTheConfirmation_OrWithBadFields_IsRefusedWithFieldErrors()
    {
        var store = Store();

        var unconfirmed = await store.CreateAsync(Request(instructions: "Text.", confirmed: false), Ct);
        Assert.Equal(BenchmarkChangeStoreOutcome.Invalid, unconfirmed.Outcome);
        Assert.Equal(BenchmarkExperimentalChangeStore.ConfirmationRequired, unconfirmed.FieldErrors!["genericConfirmed"]);

        var empty = await store.CreateAsync(Request(title: " ", summary: "", instructions: null), Ct);
        Assert.Contains("title", empty.FieldErrors!.Keys);
        Assert.Contains("summary", empty.FieldErrors.Keys);
        Assert.Contains("effects", empty.FieldErrors.Keys);

        var badEdits = await store.CreateAsync(Request(edits: new[] { ("no_such_tool", "add", "Text."), ("wiki_search", "add", " ") }), Ct);
        Assert.Contains("not a registered tool", badEdits.FieldErrors!["descriptionEdits[0].tool"]);
        Assert.Equal("The text is empty.", badEdits.FieldErrors["descriptionEdits[1].text"]);

        var longText = await store.CreateAsync(Request(instructions: new string('x', 4001)), Ct);
        Assert.Contains("longer than 4,000 characters", longText.FieldErrors!["instructions"]);

        Assert.Empty(await _db.BenchmarkExperimentalChanges.ToListAsync(Ct));
    }

    [Fact]
    public async Task TextsAreNormalized_AndBlankOptionalFieldsAreNone()
    {
        var store = Store();
        var request = Request(instructions: "\r\n  Line one.\r\nLine two.  \r\n", edits: new[] { ("WIKI_SEARCH", "add", "Added.\r\n") });
        request.ExpectedEffect = "  ";

        var change = await CreateAsync(store, request);

        Assert.Equal("Line one.\nLine two.", change.Instructions);
        Assert.Equal(new BenchmarkChangeDescriptionEdit("wiki_search", BenchmarkChangeDescriptionEditMode.Add, "Added."), Assert.Single(change.DescriptionEdits));
        Assert.Null(change.ExpectedEffect);
    }

    // -- Edits ---------------------------------------------------------------------------------

    [Fact]
    public async Task AnEdit_ChangesTheText_AndItsHash()
    {
        var store = Store();
        var created = await CreateAsync(store);

        var result = await store.UpdateAsync(created.Id, Edit(created, r =>
        {
            r.Instructions = "Name the page you read, and quote it.";
            r.GenericConfirmed = true;
        }), Ct);

        Assert.Equal(BenchmarkChangeStoreOutcome.Saved, result.Outcome);
        Assert.Equal("Name the page you read, and quote it.", result.Change!.Instructions);
        Assert.NotEqual(created.Hash, result.Change.Hash);
        Assert.True(result.Change.UpdatedAtUtc > created.UpdatedAtUtc);
    }

    [Fact]
    public async Task ATextEdit_NeedsTheConfirmation_AndADisplayEditDoesNot()
    {
        var store = Store();
        var created = await CreateAsync(store);

        var unconfirmed = await store.UpdateAsync(created.Id, Edit(created, r => r.Instructions = "Changed."), Ct);
        Assert.Equal(BenchmarkChangeStoreOutcome.Invalid, unconfirmed.Outcome);
        Assert.Contains("genericConfirmed", unconfirmed.FieldErrors!.Keys);

        var display = await store.UpdateAsync(created.Id, Edit(created, r => r.Title = "Cite the page, always"), Ct);
        Assert.Equal(BenchmarkChangeStoreOutcome.Saved, display.Outcome);
        Assert.Equal("Cite the page, always", display.Change!.Title);
        Assert.Equal(created.Id, display.Change.Id);
        Assert.Equal(created.Hash, display.Change.Hash);
    }

    [Fact]
    public async Task AnEditWithAStaleUpdatedAt_IsRefusedAsEditedElsewhere()
    {
        var store = Store();
        var created = await CreateAsync(store);
        var first = await store.UpdateAsync(created.Id, Edit(created, r => r.Summary = "A first edit."), Ct);
        Assert.True(first.Succeeded);

        var stale = await store.UpdateAsync(created.Id, Edit(created, r => r.Summary = "A second edit from the old copy."), Ct);
        var missing = await store.UpdateAsync(created.Id, Edit(created, r => r.ExpectedUpdatedAtUtc = null), Ct);

        Assert.Equal(BenchmarkChangeStoreOutcome.Conflict, stale.Outcome);
        Assert.Equal(BenchmarkChangeConflictReasons.EditedElsewhere, stale.ConflictReason);
        Assert.Equal(BenchmarkChangeConflictReasons.EditedElsewhere, missing.ConflictReason);
        Assert.Equal("A first edit.", (await _db.BenchmarkExperimentalChanges.AsNoTracking().SingleAsync(Ct)).Summary);
    }

    [Fact]
    public async Task ALockedChange_RefusesTextEdits_AndAcceptsDisplayEdits()
    {
        var store = Store();
        var created = await CreateAsync(store);

        Assert.Equal(1, await BenchmarkExperimentalChangeStore.LockAsync(_db, new[] { created.Id }, DateTime.UtcNow, Ct));
        Assert.Equal(0, await BenchmarkExperimentalChangeStore.LockAsync(_db, new[] { created.Id }, DateTime.UtcNow, Ct));
        await store.Catalog.RefreshAsync(Ct);
        var locked = store.Catalog.Snapshot.Find(created.Id)!;
        Assert.True(locked.Locked);

        var text = await store.UpdateAsync(created.Id, Edit(locked, r =>
        {
            r.Instructions = "Changed after a run.";
            r.GenericConfirmed = true;
        }), Ct);
        Assert.Equal(BenchmarkChangeStoreOutcome.Conflict, text.Outcome);
        Assert.Equal(BenchmarkChangeConflictReasons.Locked, text.ConflictReason);
        Assert.StartsWith("Its text is locked", text.Error);

        var display = await store.UpdateAsync(created.Id, Edit(locked, r =>
        {
            r.Title = "Renamed after a run";
            r.ExpectedEffect = "Fewer answers without a page.";
        }), Ct);
        Assert.Equal(BenchmarkChangeStoreOutcome.Saved, display.Outcome);
        Assert.Equal("Renamed after a run", display.Change!.Title);
        Assert.Equal(created.Hash, display.Change.Hash);
        Assert.True(display.Change.Locked);
    }

    [Fact]
    public async Task ATextEditRacingALock_IsRefused()
    {
        var store = Store();
        var created = await CreateAsync(store);

        // The editor loaded the unlocked change; a launch locks it before the save arrives.
        var request = Edit(created, r =>
        {
            r.Instructions = "Edited while a run launched.";
            r.GenericConfirmed = true;
        });
        await BenchmarkExperimentalChangeStore.LockAsync(_db, new[] { created.Id }, DateTime.UtcNow, Ct);

        var result = await store.UpdateAsync(created.Id, request, Ct);

        Assert.Equal(BenchmarkChangeConflictReasons.Locked, result.ConflictReason);
        Assert.Equal("Name the page you read.", (await _db.BenchmarkExperimentalChanges.AsNoTracking().SingleAsync(Ct)).Instructions);
    }

    [Fact]
    public async Task AnUnknownId_IsNotFound()
    {
        var store = Store();

        Assert.Equal(BenchmarkChangeStoreOutcome.NotFound, (await store.UpdateAsync("no-such-v1", Request(), Ct)).Outcome);
        Assert.Equal(BenchmarkChangeStoreOutcome.NotFound, (await store.RetireAsync("no-such-v1", Ct)).Outcome);
        Assert.Equal(BenchmarkChangeStoreOutcome.NotFound, (await store.DeleteAsync("no-such-v1", Ct)).Outcome);
    }

    // -- Retire, restore, delete ---------------------------------------------------------------

    [Fact]
    public async Task RetireAndRestore_ToggleTheChange()
    {
        var store = Store();
        var created = await CreateAsync(store);

        Assert.True((await store.RetireAsync(created.Id, Ct)).Succeeded);
        Assert.True(store.Catalog.Snapshot.Find(created.Id)!.Retired);
        Assert.Contains("is retired.", store.Catalog.Resolve(created.Id).Error);
        Assert.True(store.Catalog.Resolve(created.Id, allowRetired: true).IsValid);

        Assert.True((await store.RestoreAsync(created.Id, Ct)).Succeeded);
        Assert.False(store.Catalog.Snapshot.Find(created.Id)!.Retired);
        Assert.True(store.Catalog.Resolve(created.Id).IsValid);
    }

    [Fact]
    public async Task Delete_RemovesAnUnlockedChange_AndRefusesALockedOne()
    {
        var store = Store();
        var unlocked = await CreateAsync(store);
        var locked = await CreateAsync(store, Request(title: "Used by a run", instructions: "Used."));
        await BenchmarkExperimentalChangeStore.LockAsync(_db, new[] { locked.Id }, DateTime.UtcNow, Ct);

        Assert.True((await store.DeleteAsync(unlocked.Id, Ct)).Succeeded);
        var refused = await store.DeleteAsync(locked.Id, Ct);

        Assert.Equal(BenchmarkChangeConflictReasons.Locked, refused.ConflictReason);
        Assert.Null(store.Catalog.Snapshot.Find(unlocked.Id));
        Assert.Equal(new[] { locked.Id }, await _db.BenchmarkExperimentalChanges.Select(c => c.ChangeId).ToListAsync(Ct));
    }

    // -- Built in ------------------------------------------------------------------------------

    [Fact]
    public async Task ABuiltInChange_CannotBeEditedRetiredOrDeleted()
    {
        _root.Write("probe-change-v1", ExperimentalChangeFixtures.ChangeJson("probe-change-v1", instructions: "i.md"), ("i.md", "Built in."));
        var store = Store();

        foreach (var result in new[]
        {
            await store.UpdateAsync("probe-change-v1", Request(), Ct),
            await store.RetireAsync("probe-change-v1", Ct),
            await store.RestoreAsync("probe-change-v1", Ct),
            await store.DeleteAsync("probe-change-v1", Ct)
        })
        {
            Assert.Equal(BenchmarkChangeStoreOutcome.Conflict, result.Outcome);
            Assert.Equal(BenchmarkChangeConflictReasons.BuiltIn, result.ConflictReason);
        }
    }

    [Fact]
    public async Task AChangeMovedIntoTheRepository_KeepsItsHash_AndIsNoLongerEditable()
    {
        var store = Store();
        var created = await CreateAsync(store, Request(instructions: "Moved text.", edits: new[] { ("wiki_search", "add", "Moved addition.") }));

        // The downloaded folder, put in the repository.
        using (var zip = new ZipArchive(new MemoryStream(BenchmarkExperimentalChangeStore.BuildFolderZip(created))))
        {
            zip.ExtractToDirectory(_root.Root);
        }
        var moved = Store();

        var builtIn = Assert.Single(moved.Catalog.BuiltIns);
        Assert.True(builtIn.IsValid, builtIn.InvalidReason);
        Assert.Equal(created.Hash, builtIn.Hash);
        Assert.Equal(BenchmarkExperimentalChangeSource.BuiltIn, moved.Catalog.Snapshot.Find(created.Id)!.Source);

        var edit = await moved.UpdateAsync(created.Id, Edit(created, r => r.Title = "Renamed"), Ct);
        Assert.Equal(BenchmarkChangeConflictReasons.BuiltIn, edit.ConflictReason);
        Assert.Equal(BenchmarkExperimentalChangeStore.BuiltInMessage, edit.Error);
    }

    // -- The folder download -------------------------------------------------------------------

    [Fact]
    public async Task TheFolderZip_HoldsChangeJsonAndItsMarkdownFiles_Utf8WithoutBomAndCrlf()
    {
        var store = Store();
        var request = Request(title: "The player's own words", instructions: "Line one.\nLine two.",
            edits: new[] { ("wiki_search", "add", "Added — once."), ("get_item_stats", "replace", "Replaced.") });
        var created = await CreateAsync(store, request);

        using var zip = new ZipArchive(new MemoryStream(BenchmarkExperimentalChangeStore.BuildFolderZip(created)));

        Assert.Equal(
            new[] { $"{created.Id}/change.json", $"{created.Id}/get_item_stats.md", $"{created.Id}/instructions.md", $"{created.Id}/wiki_search.md" },
            zip.Entries.Select(e => e.FullName).OrderBy(n => n, StringComparer.Ordinal));

        foreach (var entry in zip.Entries)
        {
            using var stream = entry.Open();
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            byte[] bytes = copy.ToArray();
            Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, entry.FullName);
            string text = Encoding.UTF8.GetString(bytes);
            Assert.EndsWith("\r\n", text);
            Assert.Equal(text.Count(c => c == '\n'), text.Count(c => c == '\r'));
        }

        string Read(string name)
        {
            using var reader = new StreamReader(zip.GetEntry($"{created.Id}/{name}")!.Open(), Encoding.UTF8);
            return reader.ReadToEnd();
        }

        Assert.Equal("Line one.\r\nLine two.\r\n", Read("instructions.md"));
        Assert.Equal("Added — once.\r\n", Read("wiki_search.md"));
        string json = Read("change.json");
        Assert.Contains("\"title\": \"The player's own words\"", json);
        var file = JsonSerializer.Deserialize<BenchmarkExperimentalChangeFile>(json)!;
        Assert.Equal(created.Id, file.Id);
        Assert.Equal("wiki_search.md", file.DescriptionAdditions!["wiki_search"]);
        Assert.Equal("get_item_stats.md", file.DescriptionReplacements!["get_item_stats"]);
        Assert.Equal("instructions.md", file.Instructions);
        Assert.Null(file.ExperimentalTool);
        Assert.Null(file.Experiment);
    }

    // -- Overlaps ------------------------------------------------------------------------------

    [Fact]
    public async Task ASave_ReturnsTheTextsOverlapsWithTheBenchmarkQuestions()
    {
        var suite = new BenchmarkSuite { Name = "Gnoll Basics", Description = "Desc" };
        suite.Questions.Add(new BenchmarkQuestion
        {
            QuestionText = "What does the healer get?",
            OrderIndex = 2,
            ExpectedPoints = "The quest artifact of the healer is the staff of Aesculapius.",
            Difficulty = BenchmarkDifficulty.Simple
        });
        _db.BenchmarkSuites.Add(suite);
        await _db.SaveChangesAsync(Ct);

        var result = await Store().CreateAsync(Request(instructions: "Remember: the quest artifact of the healer is the staff, always."), Ct);

        var overlap = Assert.Single(result.Overlaps);
        Assert.Equal("the quest artifact of the healer is the staff", overlap.Text);
        Assert.Equal(("Gnoll Basics", 3, BenchmarkChangeTextScreen.RubricField), (overlap.SuiteName, overlap.QuestionNumber, overlap.Field));
    }
}
