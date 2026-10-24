namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Overseer.Models;
using Overseer.Services.Benchmarking;
using Overseer.Services.Tools;
using Overseer.Tests.Helpers;
using Xunit;
using ExperimentalChange = Overseer.Services.Benchmarking.BenchmarkExperimentalChange;
using StoredChange = MobileGnollHackLogger.Data.BenchmarkExperimentalChange;

/// <summary>
/// The experimental change catalog: what makes a change valid, built in or stored, the change and
/// selection hashes, text normalization, conflicts, mentions, applicability, the built-in versus
/// database id rule, resolution of a spec with its refusals, composition into the request context,
/// and the equivalence of the four shipped changes with the variants they replace.
/// </summary>
public sealed class BenchmarkExperimentalChangesTests : IDisposable
{
    private readonly ExperimentalChangeRoot _root = new();

    public void Dispose() => _root.Dispose();

    private BenchmarkExperimentalChangeCatalog Catalog(IEnumerable<IToolHandler>? handlers = null, params StoredChange[] rows)
        => rows.Length == 0
            ? ExperimentalChangeFixtures.Catalog(_root.Root, handlers)
            : ExperimentalChangeFixtures.Catalog(_root.Root, handlers, _ => Task.FromResult<IReadOnlyList<StoredChange>>(rows));

    private static string Sha256(string text)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private static StoredChange Row(
        string id,
        string title = "Stored change",
        string? editsJson = null,
        string? instructions = "Stored instructions.",
        DateTime? lockedAtUtc = null,
        DateTime? retiredAtUtc = null)
        => new()
        {
            ChangeId = id,
            Title = title,
            Summary = "A stored summary.",
            DescriptionEditsJson = editsJson,
            Instructions = instructions,
            ContentHash = new string('0', 64),
            CreatedAtUtc = new DateTime(2026, 10, 1, 12, 0, 0),
            UpdatedAtUtc = new DateTime(2026, 10, 2, 12, 0, 0),
            LockedAtUtc = lockedAtUtc,
            RetiredAtUtc = retiredAtUtc
        };

    private static string EditsJson(params (string Tool, string Mode, string Text)[] edits)
        => JsonSerializer.Serialize(edits.Select(e => new BenchmarkStoredDescriptionEdit { Tool = e.Tool, Mode = e.Mode, Text = e.Text }).ToList());

    // -- The shipped changes -------------------------------------------------------------------

    private static BenchmarkExperimentalChangeCatalog ShippedCatalog()
    {
        string? shipped = ExperimentalChangeFixtures.ShippedRoot();
        Assert.SkipUnless(shipped != null, "BenchmarkChanges is not copied to the test output.");
        return ExperimentalChangeFixtures.Catalog(shipped!, ExperimentalChangeFixtures.ShippedHandlers());
    }

    [Fact]
    public void TheFourShippedChanges_LoadValid_WithTitlesSummariesAndKinds()
    {
        var catalog = ShippedCatalog();
        var changes = catalog.Snapshot.Changes;

        Assert.Equal(
            new[] { "asker-character-procedure-v1", "race-role-tool-v1", "asker-search-words-v1", "stats-referenced-by-v1" },
            changes.Select(c => c.Id));
        Assert.All(changes, c =>
        {
            Assert.True(c.IsValid, c.InvalidReason);
            Assert.Equal(BenchmarkExperimentalChangeSource.BuiltIn, c.Source);
            Assert.False(c.Retired);
            Assert.False(string.IsNullOrWhiteSpace(c.Summary));
            Assert.False(string.IsNullOrWhiteSpace(c.ExpectedEffect));
            Assert.Equal(64, c.Hash.Length);
        });

        var byId = changes.ToDictionary(c => c.Id);
        Assert.Equal("Race and role lookup tool", byId["race-role-tool-v1"].Title);
        Assert.Equal("Where the game code uses it", byId["stats-referenced-by-v1"].Title);
        Assert.Equal("Search with the player's race and role", byId["asker-search-words-v1"].Title);
        Assert.Equal("Answer from the player's own character", byId["asker-character-procedure-v1"].Title);

        Assert.Equal(new[] { BenchmarkExperimentalChangeKind.NewTool }, byId["race-role-tool-v1"].Kinds);
        Assert.Equal(new[] { BenchmarkExperimentalChangeKind.ToolOutput, BenchmarkExperimentalChangeKind.ToolDescription }, byId["stats-referenced-by-v1"].Kinds);
        Assert.Equal(new[] { BenchmarkExperimentalChangeKind.ToolDescription }, byId["asker-search-words-v1"].Kinds);
        Assert.Equal(new[] { BenchmarkExperimentalChangeKind.Instructions }, byId["asker-character-procedure-v1"].Kinds);
    }

    [Fact]
    public void TheShippedChanges_MentionsAndConflicts_AreFoundMechanically()
    {
        var byId = ShippedCatalog().Snapshot.Changes.ToDictionary(c => c.Id);

        // The instructions name get_character_info; the stats additions name only their own switch's referenced_by.
        Assert.Equal(new[] { "race-role-tool-v1" }, byId["asker-character-procedure-v1"].Mentions);
        Assert.Empty(byId["stats-referenced-by-v1"].Mentions);
        Assert.Empty(byId["race-role-tool-v1"].Mentions);
        Assert.Empty(byId["asker-search-words-v1"].Mentions);
        Assert.All(byId.Values, c => Assert.Empty(c.ConflictsWith));
    }

    [Fact]
    public void TheShippedChanges_ApplyUnderTheConditionsTheirEffectsReach()
    {
        var byId = ShippedCatalog().Snapshot.Changes.ToDictionary(c => c.Id);

        Assert.All(byId.Values, c => Assert.Equal(BenchmarkChangeApplicability.Full, c.AppliesUnder(BenchmarkInformationCondition.Production)));
        Assert.Equal(new[] { "asker-character-procedure-v1" },
            byId.Values.Where(c => c.AppliesUnder(BenchmarkInformationCondition.ClosedBook) != BenchmarkChangeApplicability.None).Select(c => c.Id));
        Assert.Equal(BenchmarkChangeApplicability.None, byId["asker-search-words-v1"].AppliesUnder(BenchmarkInformationCondition.SourceOnly));
        Assert.Equal(new[] { "wiki_search" }, byId["asker-search-words-v1"].NotApplyingUnderSourceOnly);
        Assert.Equal(BenchmarkChangeApplicability.Full, byId["race-role-tool-v1"].AppliesUnder(BenchmarkInformationCondition.SourceOnly));
        Assert.Equal(BenchmarkChangeApplicability.Full, byId["stats-referenced-by-v1"].AppliesUnder(BenchmarkInformationCondition.SourceOnly));

        var dto = BenchmarkChangeApplicabilityDto.Of(byId["asker-search-words-v1"]);
        Assert.Equal(("full", "none", "none"), (dto.Production, dto.SourceOnly, dto.ClosedBook));
    }

    [Fact]
    public void TheProductionToolGuidesHash_ReadsOnlyTheToolGuidesFolder()
    {
        string? production = BenchmarkService.ComputeToolGuidesSha256();

        Assert.Equal(
            BenchmarkService.ComputeToolGuidesSha256(Path.Combine(AppContext.BaseDirectory, "ToolGuides")),
            production);

        _root.Write("probe-v1", ExperimentalChangeFixtures.ChangeJson("probe-v1", instructions: "instructions.md"),
            ("instructions.md", "A change that must not move the production hash."));
        Assert.True(Catalog().Snapshot.Find("probe-v1")!.IsValid);

        Assert.Equal(production, BenchmarkService.ComputeToolGuidesSha256());
    }

    // -- The four changes reproduce the variants they replace ---------------------------------

    private const string CharacterInfoGuide = """
        Look up a race's or a role's own rules directly from the GnollHack game tables (src/role.c, src/attrib.c and src/prop.c).

        Parameters: `race`, `role`, or both; at least one is required. Names match case-insensitively on the noun, the adjective, the plural, or the male or female form.

        Returns a JSON response:
        - for a race: "name", "allowed_roles", "allowed_alignments", "always_peaceful_races", "always_hostile_races", "abilities_by_level" (the intrinsics gained at each experience level) and "traits";
        - for a role: "name", "allowed_races", "allowed_alignments", "quest_artifact" and "abilities_by_level";
        - "note": how a monster's own flags take precedence over race attitudes;
        - "raw_definition" and "message" when a table row could not be parsed.

        Use this tool when the answer depends on the player's own race or role: which monster races are always peaceful or always hostile to that race, which intrinsics a race or role gains and at which experience level, which roles, races and alignments combine, and a role's quest artifact. The game tables are authoritative for these facts; a wiki page may only summarize them. Whether one particular monster is peaceful also depends on that monster's own flags; read get_monster_stats for those.
        """;

    private const string AskerAppendix =
        "Many GnollHack rules depend on the player's own character. When a question states or implies the player's race, role or alignment, "
        + "include those words in your searches, read that race's or role's page, and when no page states the rule, read it from the game's "
        + "race and role tables with get_character_info or from the source, rather than from general NetHack knowledge, which often differs from GnollHack.";

    /// <summary>The sentence each source-tools-v1 or asker-profile-v1 guide added to the production guide.</summary>
    private static readonly IReadOnlyDictionary<string, string> AddedSentences = new Dictionary<string, string>
    {
        ["get_artifact_stats"] = "`referenced_by` lists the functions whose code uses this artifact; when the question asks what it does in a particular situation, read the listed function that matches the situation with get_function_definition.",
        ["get_item_stats"] = "`referenced_by` lists the functions whose code uses this item; when the question asks what it does in a particular situation, read the listed function that matches the situation with get_function_definition.",
        ["get_monster_stats"] = "`referenced_by` lists the functions whose code uses this monster; when the question asks what it does in a particular situation, read the listed function that matches the situation with get_function_definition.",
        ["wiki_search"] = "When the question states the player's race, role or alignment, include those words in the query."
    };

    /// <summary>A variant guide: the production guide, a blank line, and the added sentence.</summary>
    private static string VariantGuide(string tool)
    {
        string? production = ExperimentalChangeFixtures.ProductionGuide(tool);
        Assert.SkipUnless(production != null, "ToolGuides is not copied to the test output.");
        return BenchmarkExperimentalChangeHash.Normalize(production)! + "\n\n" + AddedSentences[tool];
    }

    [Fact]
    public void RaceRoleToolAndStatsReferencedBy_OfferExactlyWhatSourceToolsV1Offered()
    {
        var catalog = ShippedCatalog();

        var selection = catalog.Resolve("stats-referenced-by-v1+race-role-tool-v1");

        Assert.True(selection.IsValid, selection.Error);
        var context = selection.Context!;
        Assert.Equal("race-role-tool-v1+stats-referenced-by-v1", context.Name);
        Assert.Equal(new[] { "get_character_info" }, context.ExperimentalTools);
        Assert.Equal(new[] { BenchmarkExperimentalChangeCatalog.StatsReferencedByExperiment }, context.Experiments);
        Assert.Null(context.Appendix);
        Assert.Equal(
            new[] { "get_artifact_stats", "get_character_info", "get_item_stats", "get_monster_stats" },
            context.ToolGuides.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal(BenchmarkExperimentalChangeHash.Normalize(CharacterInfoGuide), context.ToolGuides["get_character_info"]);
        foreach (string tool in new[] { "get_artifact_stats", "get_item_stats", "get_monster_stats" })
        {
            Assert.Equal(VariantGuide(tool), context.ToolGuides[tool]);
        }
    }

    [Fact]
    public void AllFourChanges_GiveTheAppendixAndGuidesAskerProfileV1Gave()
    {
        var catalog = ShippedCatalog();

        var selection = catalog.Resolve("asker-character-procedure-v1+asker-search-words-v1+race-role-tool-v1+stats-referenced-by-v1");

        Assert.True(selection.IsValid, selection.Error);
        var context = selection.Context!;
        Assert.Equal(AskerAppendix, context.Appendix);
        Assert.Equal(new[] { new BenchmarkVariantAppendixPart("asker-character-procedure-v1", AskerAppendix) }, context.AppendixParts);
        Assert.Equal(new[] { "get_character_info" }, context.ExperimentalTools);
        Assert.Equal(new[] { BenchmarkExperimentalChangeCatalog.StatsReferencedByExperiment }, context.Experiments);
        Assert.Equal(VariantGuide("wiki_search"), context.ToolGuides["wiki_search"]);
        Assert.Equal(5, context.ToolGuides.Count);
        Assert.True(selection.Stamp!.Length <= BenchmarkExperimentalChangeCatalog.MaxStampLength);
    }

    // -- A valid built-in change ---------------------------------------------------------------

    [Fact]
    public void ABuiltInChange_ReadsItsTextsNormalized_AndHashesTheCanonicalLines()
    {
        _root.Write("probe-v1", ExperimentalChangeFixtures.ChangeJson(
                "probe-v1",
                title: " Probe ",
                experimentalTool: "GET_CHARACTER_INFO",
                experimentalToolDescription: "character.md",
                experiment: "stats_referenced_by",
                additions: new() { ["wiki_search"] = "wiki.md" },
                replacements: new() { ["get_item_stats"] = "items.md" },
                instructions: "instructions.md"),
            ("character.md", "Character line one.\r\nLine two.\r\n"),
            ("wiki.md", "\r\n  Read the page.  \r\n"),
            ("items.md", "Item guide.\rSecond line."),
            ("instructions.md", "Do this first.\n"));

        var change = Catalog().Snapshot.Find("probe-v1")!;

        Assert.True(change.IsValid, change.InvalidReason);
        Assert.Equal("Probe", change.Title);
        Assert.Equal("get_character_info", change.ExperimentalTool);
        Assert.Equal("Character line one.\nLine two.", change.ExperimentalToolDescription);
        Assert.Equal(new BenchmarkChangeDescriptionEdit("wiki_search", BenchmarkChangeDescriptionEditMode.Add, "Read the page."), change.DescriptionEdits[0]);
        Assert.Equal(new BenchmarkChangeDescriptionEdit("get_item_stats", BenchmarkChangeDescriptionEditMode.Replace, "Item guide.\nSecond line."), change.DescriptionEdits[1]);
        Assert.Equal("Do this first.", change.Instructions);

        var lines = new List<string>
        {
            "id:probe-v1",
            "tool:get_character_info",
            "tooldesc:" + Sha256("Character line one.\nLine two."),
            "experiment:stats_referenced_by",
            "add:wiki_search:" + Sha256("Read the page."),
            "replace:get_item_stats:" + Sha256("Item guide.\nSecond line."),
            "instructions:" + Sha256("Do this first.")
        };
        lines.Sort(StringComparer.Ordinal);
        Assert.Equal(Sha256(string.Concat(lines.Select(l => l + "\n"))), change.Hash);
        Assert.Equal(change.Hash[..12], change.HashPrefix);
        Assert.Equal(
            new[]
            {
                BenchmarkExperimentalChangeKind.NewTool, BenchmarkExperimentalChangeKind.ToolOutput,
                BenchmarkExperimentalChangeKind.ToolDescription, BenchmarkExperimentalChangeKind.Instructions
            },
            change.Kinds);
    }

    [Fact]
    public void TheHash_IgnoresTheDisplayFields_AndLineEndings_ButNotTheText()
    {
        var change = new ExperimentalChange
        {
            Id = "probe-v1",
            Title = "A title",
            Summary = "A summary.",
            Instructions = "First line.\nSecond line."
        };
        string hash = BenchmarkExperimentalChangeHash.Of(change);

        Assert.Equal(hash, BenchmarkExperimentalChangeHash.Of(change with { Title = "Another title", Summary = "Another summary.", ExpectedEffect = "More." }));
        Assert.Equal(hash, BenchmarkExperimentalChangeHash.Of(change with { Instructions = "First line.\r\nSecond line.\r\n" }));
        Assert.Equal(hash, BenchmarkExperimentalChangeHash.Of(change with { Source = BenchmarkExperimentalChangeSource.Database }));
        Assert.NotEqual(hash, BenchmarkExperimentalChangeHash.Of(change with { Instructions = "First line.\nSecond line, edited." }));
        Assert.NotEqual(hash, BenchmarkExperimentalChangeHash.Of(change with { Id = "probe-v2" }));
        Assert.Equal("a\nb", BenchmarkExperimentalChangeHash.Normalize("  a\r\nb\r "));
        Assert.Null(BenchmarkExperimentalChangeHash.NormalizeOrNull(" \r\n "));
    }

    [Fact]
    public void ACrlfBuiltInText_AndTheSameTextStored_HashAlike()
    {
        _root.Write("same-text-v1", ExperimentalChangeFixtures.ChangeJson("same-text-v1", instructions: "instructions.md"),
            ("instructions.md", "Line one.\r\nLine two.\r\n"));
        var builtIn = Catalog().Snapshot.Find("same-text-v1")!;

        var stored = Catalog(null, Row("other-v1", instructions: "Line one.\nLine two.")).FromStored(Row("same-text-v1", instructions: "Line one.\nLine two."));

        Assert.Equal(builtIn.Hash, stored.Hash);
    }

    // -- Invalid changes -----------------------------------------------------------------------

    public static TheoryData<string, string, string> InvalidDefinitions => new()
    {
        { "name-mismatch-v1", ExperimentalChangeFixtures.ChangeJson("other-name-v1", instructions: "i.md"), "not the folder name" },
        { "Not_Kebab-v1", ExperimentalChangeFixtures.ChangeJson("Not_Kebab-v1", instructions: "i.md"), "kebab-case ending -v<N>" },
        { "no-version", ExperimentalChangeFixtures.ChangeJson("no-version", instructions: "i.md"), "kebab-case ending -v<N>" },
        { "an-id-that-is-far-too-long-for-the-limit-v1", ExperimentalChangeFixtures.ChangeJson("an-id-that-is-far-too-long-for-the-limit-v1", instructions: "i.md"), "at most 32 characters" },
        { "no-title-v1", ExperimentalChangeFixtures.ChangeJson("no-title-v1", title: " ", instructions: "i.md"), "Give it a title." },
        { "long-title-v1", ExperimentalChangeFixtures.ChangeJson("long-title-v1", title: new string('t', 61), instructions: "i.md"), "longer than 60 characters" },
        { "no-summary-v1", ExperimentalChangeFixtures.ChangeJson("no-summary-v1", summary: "", instructions: "i.md"), "one-sentence summary" },
        { "long-summary-v1", ExperimentalChangeFixtures.ChangeJson("long-summary-v1", summary: new string('s', 221), instructions: "i.md"), "longer than 220 characters" },
        { "no-effect-v1", ExperimentalChangeFixtures.ChangeJson("no-effect-v1"), "at least one effect" },
        { "unknown-tool-v1", ExperimentalChangeFixtures.ChangeJson("unknown-tool-v1", experimentalTool: "no_such_tool"), "'no_such_tool' is not a registered tool." },
        { "production-tool-v1", ExperimentalChangeFixtures.ChangeJson("production-tool-v1", experimentalTool: "wiki_search"), "'wiki_search' is not an experimental tool." },
        { "unknown-experiment-v1", ExperimentalChangeFixtures.ChangeJson("unknown-experiment-v1", experiment: "time_travel"), "not a known experiment" },
        { "unknown-desc-tool-v1", ExperimentalChangeFixtures.ChangeJson("unknown-desc-tool-v1", additions: new() { ["no_such_tool"] = "g.md" }), "'no_such_tool' is not a registered tool." },
        { "experimental-desc-v1", ExperimentalChangeFixtures.ChangeJson("experimental-desc-v1", additions: new() { ["get_character_info"] = "g.md" }), "only a production tool's description" },
        { "not-allowlisted-v1", ExperimentalChangeFixtures.ChangeJson("not-allowlisted-v1", additions: new() { ["get_save_info"] = "g.md" }), "not offered to benchmark runs" },
        { "added-and-replaced-v1", ExperimentalChangeFixtures.ChangeJson("added-and-replaced-v1", additions: new() { ["wiki_search"] = "g.md" }, replacements: new() { ["wiki_search"] = "g.md" }), "'wiki_search' is both added to and replaced." },
        { "slash-file-v1", ExperimentalChangeFixtures.ChangeJson("slash-file-v1", instructions: "sub/i.md"), "not a Markdown file of the folder" },
        { "escaping-file-v1", ExperimentalChangeFixtures.ChangeJson("escaping-file-v1", instructions: "../outside.md"), "not a Markdown file of the folder" },
        { "text-file-v1", ExperimentalChangeFixtures.ChangeJson("text-file-v1", instructions: "i.txt"), "not a Markdown file of the folder" },
        { "missing-file-v1", ExperimentalChangeFixtures.ChangeJson("missing-file-v1", instructions: "absent.md"), "not a Markdown file of the folder" },
        { "empty-text-v1", ExperimentalChangeFixtures.ChangeJson("empty-text-v1", instructions: "empty.md"), "The instructions are empty." },
        { "long-text-v1", ExperimentalChangeFixtures.ChangeJson("long-text-v1", instructions: "long.md"), "longer than 4,000 characters" },
        { "tool-description-alone-v1", ExperimentalChangeFixtures.ChangeJson("tool-description-alone-v1", experimentalToolDescription: "g.md", instructions: "i.md"), "no experimental tool" }
    };

    [Theory]
    [MemberData(nameof(InvalidDefinitions))]
    public void AnInvalidBuiltInChange_IsListedWithItsReason_AndRefused(string folder, string changeJson, string reason)
    {
        _root.Write(folder, changeJson, ("i.md", "Instructions."), ("i.txt", "Text."), ("g.md", "Guide."),
            ("empty.md", " \r\n "), ("long.md", new string('x', 4001)));
        File.WriteAllText(Path.Combine(_root.Root, "outside.md"), "Outside the folder.");

        var catalog = Catalog();
        var change = catalog.Snapshot.Find(folder)!;

        Assert.False(change.IsValid);
        Assert.Contains(reason, change.InvalidReason);
        Assert.Contains(catalog.Snapshot.Changes, c => c.Id == folder);
        if (BenchmarkExperimentalChangeCatalog.IsWellFormedSpec(folder))
        {
            Assert.Contains("is invalid: ", catalog.Resolve(folder).Error);
        }
    }

    [Fact]
    public void AFolderWithoutAChangeFile_OrWithOneThatDoesNotParse_IsInvalid()
    {
        _root.Write("empty-folder-v1", null);
        _root.Write("unparsed-v1", "{ \"id\": ");

        var catalog = Catalog();

        Assert.Contains(BenchmarkExperimentalChangeCatalog.ChangeFileName, catalog.Snapshot.Find("empty-folder-v1")!.InvalidReason);
        Assert.Contains("does not parse", catalog.Snapshot.Find("unparsed-v1")!.InvalidReason);
    }

    [Fact]
    public void MoreThanSixDescriptionEdits_AreInvalid()
    {
        var change = new ExperimentalChange
        {
            Id = "many-edits-v1",
            Title = "Many edits",
            Summary = "Seven additions.",
            DescriptionEdits = Enumerable.Range(0, 7)
                .Select(i => new BenchmarkChangeDescriptionEdit("wiki_search", BenchmarkChangeDescriptionEditMode.Add, "Text " + i))
                .ToList()
        };

        var problems = BenchmarkExperimentalChangeValidator.Check(change, ExperimentalChangeFixtures.Handlers(), BenchmarkService.DefaultAllowedTools);

        Assert.Contains(problems, p => p.Field == "effects" && p.Message.Contains("at most 6 description edits"));
        Assert.Contains(problems, p => p.Field == "descriptionEdits[1].tool" && p.Message.Contains("more than one description edit"));
    }

    [Fact]
    public void AnInvalidDatabaseChange_IsListedWithItsReason()
    {
        var catalog = Catalog(null, Row("stored-v1", editsJson: EditsJson(("get_save_info", "add", "Text.")), instructions: null));

        var change = catalog.Snapshot.Find("stored-v1")!;

        Assert.Equal(BenchmarkExperimentalChangeSource.Database, change.Source);
        Assert.False(change.IsValid);
        Assert.Contains("not offered to benchmark runs", change.InvalidReason);
    }

    // -- Retirement ----------------------------------------------------------------------------

    [Fact]
    public void RetiredJson_RetiresBuiltInChanges_WhichResolveOnlyForReruns()
    {
        _root.Write("kept-v1", ExperimentalChangeFixtures.ChangeJson("kept-v1", instructions: "i.md"), ("i.md", "Kept."));
        _root.Write("old-v1", ExperimentalChangeFixtures.ChangeJson("old-v1", title: "Old change", instructions: "i.md"), ("i.md", "Old."));
        _root.WriteRetired("old-v1");

        var catalog = Catalog();

        Assert.True(catalog.Snapshot.Find("old-v1")!.Retired);
        Assert.False(catalog.Snapshot.Find("kept-v1")!.Retired);
        Assert.Equal("Old change is retired.", catalog.Resolve("old-v1").Error);
        Assert.True(catalog.Resolve("old-v1", allowRetired: true).IsValid);
    }

    [Fact]
    public void ARetiredDatabaseChange_IsRefusedInANewLaunch()
    {
        var catalog = Catalog(null, Row("stored-v1", title: "Stored change", retiredAtUtc: DateTime.UtcNow));

        Assert.True(catalog.Snapshot.Find("stored-v1")!.Retired);
        Assert.Equal("Stored change is retired.", catalog.Resolve("stored-v1").Error);
    }

    // -- Built in versus written in Overseer ---------------------------------------------------

    [Fact]
    public void ABuiltInAndADatabaseChangeWithOneIdAndEqualText_ListTheBuiltInOne()
    {
        _root.Write("shared-v1", ExperimentalChangeFixtures.ChangeJson("shared-v1", title: "Built in", instructions: "i.md"), ("i.md", "Same text.\r\n"));

        var catalog = Catalog(null, Row("shared-v1", title: "Stored", instructions: "Same text."));

        var listed = Assert.Single(catalog.Snapshot.Changes, c => c.Id == "shared-v1");
        Assert.Equal(BenchmarkExperimentalChangeSource.BuiltIn, listed.Source);
        Assert.Equal("Built in", listed.Title);
        Assert.True(listed.IsValid);
        Assert.True(catalog.Resolve("shared-v1").IsValid);
    }

    [Fact]
    public void ABuiltInAndADatabaseChangeWithOneIdAndOtherText_ListTheDatabaseOne_AndTheFolderIsInvalid()
    {
        _root.Write("shared-v1", ExperimentalChangeFixtures.ChangeJson("shared-v1", title: "Built in", instructions: "i.md"), ("i.md", "Folder text."));

        var catalog = Catalog(null, Row("shared-v1", title: "Stored", instructions: "Stored text."));

        var listed = Assert.Single(catalog.Snapshot.Changes, c => c.Id == "shared-v1");
        Assert.Equal(BenchmarkExperimentalChangeSource.Database, listed.Source);
        Assert.Equal("Stored text.", catalog.Resolve("shared-v1").Context!.Appendix);
        Assert.Contains(catalog.BuiltIns, b => b.Id == "shared-v1");
        Assert.Equal("Its id is taken by a change written in Overseer with different text.", BenchmarkExperimentalChangeCatalog.IdTakenReason);
    }

    [Fact]
    public async Task TheSnapshot_IsRebuiltByRefresh()
    {
        var rows = new List<StoredChange>();
        var catalog = ExperimentalChangeFixtures.Catalog(_root.Root, null,
            _ => Task.FromResult<IReadOnlyList<StoredChange>>(rows.ToList()));

        Assert.Null(catalog.Snapshot.Find("stored-v1"));
        Assert.True(catalog.Snapshot.DatabaseLoaded);

        rows.Add(Row("stored-v1"));
        await catalog.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(catalog.Snapshot.Find("stored-v1"));
    }

    [Fact]
    public async Task AFailedRefresh_KeepsTheLastSnapshot()
    {
        bool fail = false;
        var catalog = ExperimentalChangeFixtures.Catalog(_root.Root, null, _ => fail
            ? throw new InvalidOperationException("The database is down.")
            : Task.FromResult<IReadOnlyList<StoredChange>>(new[] { Row("stored-v1") }));
        Assert.NotNull(catalog.Snapshot.Find("stored-v1"));

        fail = true;
        await catalog.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(catalog.Snapshot.Find("stored-v1"));
    }

    // -- Specs and stamps ----------------------------------------------------------------------

    [Theory]
    [InlineData("b-v1+a-v1", "a-v1+b-v1")]
    [InlineData("b-v1 a-v1", "a-v1+b-v1")]
    [InlineData("  a-v1  ", "a-v1")]
    [InlineData("a/minus/b", "a/minus/b")]
    public void CanonicalizeSpec_ReadsSpacesAsPlus_AndSortsWellFormedIds(string spec, string expected)
        => Assert.Equal(expected, BenchmarkExperimentalChangeCatalog.CanonicalizeSpec(spec));

    [Fact]
    public void StampsAndSpecs_RoundTrip()
    {
        string hash = new string('c', 64);

        Assert.Equal("a-v1+b-v1:cccccccccccc", BenchmarkExperimentalChangeCatalog.StampOf("a-v1+b-v1", hash));
        Assert.Equal("a-v1+b-v1", BenchmarkExperimentalChangeCatalog.SpecOfStamp("a-v1+b-v1:cccccccccccc"));
        Assert.Equal("no-colon", BenchmarkExperimentalChangeCatalog.SpecOfStamp("no-colon"));
        Assert.Equal(new[] { "a-v1", "b-v1" }, BenchmarkExperimentalChangeCatalog.IdsOf("a-v1+b-v1:cccccccccccc"));
        Assert.Equal(new[] { "a-v1", "b-v1" }, BenchmarkExperimentalChangeCatalog.IdsOf("a-v1 b-v1"));
        Assert.Empty(BenchmarkExperimentalChangeCatalog.IdsOf(null));
        Assert.Null(BenchmarkExperimentalChangeCatalog.NormalizeSpec("  "));
        Assert.Equal('+', BenchmarkExperimentalChangeCatalog.SpecSeparator);
        Assert.Equal(160, BenchmarkExperimentalChangeCatalog.MaxStampLength);
        Assert.Equal(new[] { "stats_referenced_by" }, BenchmarkExperimentalChangeCatalog.KnownExperiments);
    }

    // -- Resolution ----------------------------------------------------------------------------

    /// <summary>
    /// <c>add-wiki-v1</c> and <c>add-wiki-two-v1</c> add to wiki_search; <c>replace-wiki-v1</c> and
    /// <c>replace-wiki-two-v1</c> replace it; <c>tool-v1</c> and <c>tool-two-v1</c> offer
    /// get_character_info; <c>switch-v1</c> and <c>switch-two-v1</c> turn on stats_referenced_by;
    /// <c>steps-v1</c> and <c>steps-two-v1</c> add instructions; <c>broken-v1</c> is invalid.
    /// </summary>
    private BenchmarkExperimentalChangeCatalog ResolutionCatalog(params StoredChange[] rows)
    {
        _root.Write("add-wiki-v1", ExperimentalChangeFixtures.ChangeJson("add-wiki-v1", title: "Add wiki", additions: new() { ["wiki_search"] = "w.md" }), ("w.md", "First addition."));
        _root.Write("add-wiki-two-v1", ExperimentalChangeFixtures.ChangeJson("add-wiki-two-v1", title: "Add wiki two", additions: new() { ["wiki_search"] = "w.md" }), ("w.md", "Second addition."));
        _root.Write("replace-wiki-v1", ExperimentalChangeFixtures.ChangeJson("replace-wiki-v1", title: "Replace wiki", replacements: new() { ["wiki_search"] = "w.md" }), ("w.md", "Replaced wiki."));
        _root.Write("replace-wiki-two-v1", ExperimentalChangeFixtures.ChangeJson("replace-wiki-two-v1", title: "Replace wiki two", replacements: new() { ["wiki_search"] = "w.md" }), ("w.md", "Replaced again."));
        _root.Write("replace-items-v1", ExperimentalChangeFixtures.ChangeJson("replace-items-v1", title: "Replace items", replacements: new() { ["get_item_stats"] = "w.md" }), ("w.md", "Replaced items."));
        _root.Write("tool-v1", ExperimentalChangeFixtures.ChangeJson("tool-v1", title: "Tool", experimentalTool: "get_character_info", experimentalToolDescription: "t.md"), ("t.md", "Own description."));
        _root.Write("tool-two-v1", ExperimentalChangeFixtures.ChangeJson("tool-two-v1", title: "Tool two", experimentalTool: "get_character_info"));
        _root.Write("switch-v1", ExperimentalChangeFixtures.ChangeJson("switch-v1", title: "Switch", experiment: "stats_referenced_by"));
        _root.Write("switch-two-v1", ExperimentalChangeFixtures.ChangeJson("switch-two-v1", title: "Switch two", experiment: "stats_referenced_by"));
        _root.Write("steps-v1", ExperimentalChangeFixtures.ChangeJson("steps-v1", title: "Steps", instructions: "i.md"), ("i.md", "Step one, with get_character_info."));
        _root.Write("steps-two-v1", ExperimentalChangeFixtures.ChangeJson("steps-two-v1", title: "Steps two", instructions: "i.md"), ("i.md", "Step two, read referenced_by."));
        _root.Write("broken-v1", null);
        return Catalog(null, rows);
    }

    public static TheoryData<string, string> Refusals => new()
    {
        { "add-wiki-v1/minus/x", "'add-wiki-v1/minus/x' is not a list of experimental change ids joined by '+'." },
        { "add-wiki-v1++steps-v1", "is not a list of experimental change ids joined by '+'." },
        { "Steps-V1", "is not a list of experimental change ids joined by '+'." },
        { "no-such-v1", "Unknown experimental change 'no-such-v1'." },
        { "broken-v1+steps-v1", "broken-v1 is invalid: " },
        { "steps-v1+steps-v1", "Steps is selected more than once." },
        { "replace-wiki-v1+replace-wiki-two-v1", "Replace wiki two and Replace wiki both replace the wiki_search description." },
        { "add-wiki-v1+replace-wiki-v1", "Add wiki and Replace wiki both change the wiki_search description, and one of them replaces it." },
        { "tool-v1+tool-two-v1", "Tool two and Tool both offer the get_character_info tool." },
        { "switch-v1+switch-two-v1", "Switch two and Switch both turn on the stats_referenced_by tool output." }
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public void ASpec_IsRefused_WithItsReason(string spec, string reason)
    {
        var selection = ResolutionCatalog().Resolve(spec);

        Assert.False(selection.IsValid);
        Assert.Null(selection.Context);
        Assert.Contains(reason, selection.Error);
    }

    [Fact]
    public void TheRefusals_ComeInTheirOrder()
    {
        var catalog = ResolutionCatalog();

        // An unknown id is named before a conflict; an invalid change before a duplicate.
        Assert.StartsWith("Unknown experimental change", catalog.Resolve("replace-wiki-v1+replace-wiki-two-v1+no-such-v1").Error);
        Assert.StartsWith("broken-v1 is invalid", catalog.Resolve("steps-v1+steps-v1+broken-v1").Error);
    }

    [Fact]
    public void AStampLongerThanTheRunRecord_IsRefused()
    {
        var ids = new List<string>();
        for (int i = 0; i < 7; i++)
        {
            string id = $"instruction-number-{(char)('a' + i)}-v1";
            ids.Add(id);
            _root.Write(id, ExperimentalChangeFixtures.ChangeJson(id, instructions: "i.md"), ("i.md", "Paragraph " + i + "."));
        }
        var catalog = Catalog();

        Assert.True(catalog.Resolve(string.Join('+', ids.Take(6))).IsValid);
        Assert.Equal(BenchmarkExperimentalChangeCatalog.StampTooLongRefusal, catalog.Resolve(string.Join('+', ids)).Error);
        Assert.Equal("Too many changes for one run: the run record holds 160 characters. Choose fewer.", BenchmarkExperimentalChangeCatalog.StampTooLongRefusal);
    }

    [Fact]
    public void ASelection_IsCanonical_AndStampedWithItsSelectionHash()
    {
        var catalog = ResolutionCatalog();

        var selection = catalog.Resolve(" steps-v1 add-wiki-v1 ");

        Assert.True(selection.IsValid, selection.Error);
        Assert.Equal("add-wiki-v1+steps-v1", selection.Spec);
        Assert.Equal(new[] { "add-wiki-v1", "steps-v1" }, selection.Changes.Select(c => c.Id));
        string expected = Sha256(string.Concat(selection.Changes.Select(c => $"{c.Id}:{c.Hash}\n")));
        Assert.Equal(expected, selection.Hash);
        Assert.Equal("add-wiki-v1+steps-v1:" + expected[..12], selection.Stamp);
        Assert.Same(selection.Context, catalog.Resolve("add-wiki-v1+steps-v1").Context);
        Assert.True(BenchmarkExperimentalChangeCatalog.ConflictBetween(selection.Changes[0], selection.Changes[1]) == null);
    }

    [Fact]
    public void ABlankSpec_IsNoChanges()
    {
        var selection = ResolutionCatalog().Resolve("  ");

        Assert.True(selection.IsEmpty);
        Assert.True(selection.IsValid);
        Assert.Null(selection.Context);
        Assert.Null(selection.Stamp);
    }

    // -- Composition ---------------------------------------------------------------------------

    [Fact]
    public void AnAddition_IsTheLiveDescription_ABlankLine_AndTheText()
    {
        var context = ResolutionCatalog().Resolve("add-wiki-v1").Context!;

        Assert.Equal(ExperimentalChangeFixtures.WikiDescription + "\n\nFirst addition.", context.ToolGuides["wiki_search"]);
        Assert.Single(context.ToolGuides);
        Assert.Empty(context.ExperimentalTools);
        Assert.Null(context.Appendix);
    }

    [Fact]
    public void Additions_ComposeInIdOrder()
    {
        var context = ResolutionCatalog().Resolve("add-wiki-v1+add-wiki-two-v1").Context!;

        Assert.Equal(ExperimentalChangeFixtures.WikiDescription + "\n\nSecond addition.\n\nFirst addition.", context.ToolGuides["wiki_search"]);
    }

    [Fact]
    public void AReplacement_IsTheWholeDescription()
    {
        var context = ResolutionCatalog().Resolve("replace-items-v1+add-wiki-v1").Context!;

        Assert.Equal("Replaced items.", context.ToolGuides["get_item_stats"]);
        Assert.Equal(ExperimentalChangeFixtures.WikiDescription + "\n\nFirst addition.", context.ToolGuides["wiki_search"]);
    }

    [Fact]
    public void ToolsAndSwitches_AreUnited_AndInstructionsJoinWithABlankLine()
    {
        var context = ResolutionCatalog().Resolve("steps-two-v1+tool-v1+switch-v1+steps-v1").Context!;

        Assert.Equal(new[] { "get_character_info" }, context.ExperimentalTools);
        Assert.Equal(new[] { BenchmarkExperimentalChangeCatalog.StatsReferencedByExperiment }, context.Experiments);
        Assert.True(context.HasExperiment(BenchmarkExperimentalChangeCatalog.StatsReferencedByExperiment));
        Assert.Equal("Own description.", context.ToolGuides["GET_CHARACTER_INFO"]);
        // Spec order: steps-two-v1 sorts before steps-v1.
        Assert.Equal("Step two, read referenced_by.\n\nStep one, with get_character_info.", context.Appendix);
        Assert.Equal(
            new[] { new BenchmarkVariantAppendixPart("steps-two-v1", "Step two, read referenced_by."), new BenchmarkVariantAppendixPart("steps-v1", "Step one, with get_character_info.") },
            context.AppendixParts);
        Assert.Equal("steps-two-v1+steps-v1+switch-v1+tool-v1".Split('+').OrderBy(i => i, StringComparer.Ordinal), context.Name.Split('+'));
    }

    [Fact]
    public void AToolWithoutItsOwnDescription_KeepsTheDeclaredOne()
    {
        var context = ResolutionCatalog().Resolve("tool-two-v1").Context!;

        Assert.Equal(new[] { "get_character_info" }, context.ExperimentalTools);
        Assert.Empty(context.ToolGuides);
    }

    // -- Mentions, conflicts and applicability -------------------------------------------------

    [Fact]
    public void Mentions_NameTheChangesWhoseToolOrSwitchATextNames()
    {
        var byId = ResolutionCatalog().Snapshot.Changes.ToDictionary(c => c.Id);

        Assert.Equal(new[] { "tool-two-v1", "tool-v1" }, byId["steps-v1"].Mentions.OrderBy(i => i, StringComparer.Ordinal));
        Assert.Equal(new[] { "switch-two-v1", "switch-v1" }, byId["steps-two-v1"].Mentions.OrderBy(i => i, StringComparer.Ordinal));
        Assert.Empty(byId["add-wiki-v1"].Mentions);
        Assert.Contains("replace-wiki-v1", byId["add-wiki-v1"].ConflictsWith);
        Assert.DoesNotContain("add-wiki-two-v1", byId["add-wiki-v1"].ConflictsWith);
        Assert.Equal(new[] { "tool-two-v1" }, byId["tool-v1"].ConflictsWith);
    }

    [Fact]
    public void AChangeWithoutEffectUnderTheCondition_IsRefused_AndAPartialOneIsNot()
    {
        _root.Write("wiki-and-items-v1", ExperimentalChangeFixtures.ChangeJson("wiki-and-items-v1", title: "Wiki and items",
                additions: new() { ["wiki_search"] = "w.md", ["get_item_stats"] = "w.md" }), ("w.md", "Added."));
        var catalog = ResolutionCatalog();

        Assert.Equal(
            "Add wiki has no effect under Source code only: wiki_search is not offered there. Uncheck it, or choose another Tool Access.",
            catalog.Resolve("add-wiki-v1").ConditionRefusal(BenchmarkInformationCondition.SourceOnly));
        Assert.Equal(
            "Add wiki has no effect under Closed book: no tools are offered there. Uncheck it, or choose another Tool Access.",
            catalog.Resolve("add-wiki-v1+steps-v1").ConditionRefusal(BenchmarkInformationCondition.ClosedBook));
        Assert.Null(catalog.Resolve("steps-v1").ConditionRefusal(BenchmarkInformationCondition.ClosedBook));
        Assert.Null(catalog.Resolve("add-wiki-v1").ConditionRefusal(BenchmarkInformationCondition.Production));

        var partial = catalog.Snapshot.Find("wiki-and-items-v1")!;
        Assert.Equal(BenchmarkChangeApplicability.Partial, partial.AppliesUnder(BenchmarkInformationCondition.SourceOnly));
        Assert.Null(catalog.Resolve("wiki-and-items-v1").ConditionRefusal(BenchmarkInformationCondition.SourceOnly));
    }

    // -- Titles --------------------------------------------------------------------------------

    [Fact]
    public void Describe_PrintsTitles_AndTheIdOfAnUnknownChange()
    {
        var catalog = ResolutionCatalog();

        Assert.Equal("Add wiki, Steps", catalog.Describe("add-wiki-v1+steps-v1:0123456789ab"));
        Assert.Equal("Steps, gone-v1", catalog.Describe("steps-v1+gone-v1"));
        Assert.Equal("none", catalog.Describe(null));
        Assert.Equal("gone-v1", catalog.TitleOf("gone-v1"));
    }

    // -- The DTO -------------------------------------------------------------------------------

    [Fact]
    public void TheDto_CarriesTheTextsKindsAndActions()
    {
        var catalog = ResolutionCatalog(Row("stored-v1", editsJson: EditsJson(("wiki_search", "add", "Stored addition.")), lockedAtUtc: DateTime.UtcNow));

        var tool = BenchmarkExperimentalChangeDto.From(catalog.Snapshot.Find("tool-v1")!, catalog, 3);
        Assert.Equal("builtIn", tool.Source);
        Assert.Equal(new[] { "newTool" }, tool.Kinds);
        Assert.Equal("get_character_info", tool.ExperimentalTool!.Name);
        Assert.Equal("Own description.", tool.ExperimentalTool.Description);
        Assert.Equal(ExperimentalChangeFixtures.CharacterSchema, tool.ExperimentalTool.ParameterSchemaJson);
        Assert.Equal(3, tool.RunCount);
        Assert.False(tool.CanEdit);
        Assert.False(tool.CanCopy);
        Assert.Null(tool.UpdatedAtUtc);
        Assert.Equal(12, tool.HashPrefix!.Length);

        var stored = BenchmarkExperimentalChangeDto.From(catalog.Snapshot.Find("stored-v1")!, catalog, 0);
        Assert.Equal("database", stored.Source);
        Assert.Equal(new[] { "toolDescription", "instructions" }, stored.Kinds);
        var edit = Assert.Single(stored.DescriptionEdits);
        Assert.Equal(("wiki_search", "add", "Stored addition.", ExperimentalChangeFixtures.WikiDescription), (edit.Tool, edit.Mode, edit.Text, edit.ProductionText));
        Assert.True(stored.Locked);
        Assert.True(stored.CanEdit);
        Assert.False(stored.CanEditText);
        Assert.False(stored.CanDelete);
        Assert.True(stored.CanCopy);
        Assert.Equal(DateTimeKind.Utc, stored.UpdatedAtUtc!.Value.Kind);
        Assert.Equal(new[] { "wiki_search" }, stored.NotApplyingUnderSourceOnly);
        Assert.Equal("partial", stored.AppliesUnder.SourceOnly);

        var tools = BenchmarkExperimentalChangeCatalogDto.ProductionToolsOf(catalog);
        Assert.Equal(new[] { "wiki_search", "get_monster_stats", "get_item_stats", "get_artifact_stats" }, tools.Select(t => t.Name));
        Assert.True(tools[0].WithheldUnderSourceOnly);
        Assert.Equal(ExperimentalChangeFixtures.ItemDescription, tools[2].Description);
    }
}
